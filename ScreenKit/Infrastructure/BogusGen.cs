using System.Globalization;
using System.Reflection;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 用程序目录里的 Bogus.dll 生成测试数据。程序不引用这个库，未安装时不加载。
/// 读入字节再加载，卸载时可以删掉文件。
/// </summary>
static class BogusGen {
	static readonly object gate = new();
	static object zh;
	static object en;

	public static readonly string[] Kinds = [
		"name", "cnnick", "ennick", "enname", "mail", "phone", "addr", "corp", "user", "id",
		"mac", "uuid", "job", "city", "url", "ip", "color", "date", "sentence", "product",
	];

	public static bool Installed => NativeRuntime.HasBogus();

	public static string Lines(string kind, int n) {
		if (n < 1 || n > 50) throw new ArgumentException("行数要在 1 到 50");
		if (string.IsNullOrEmpty(kind) || Array.IndexOf(Kinds, kind) < 0)
			throw new ArgumentException("请选择类型");
		if (!Installed) throw new InvalidOperationException("Bogus 未安装");
		ensure();
		var sb = new StringBuilder();
		for (var i = 0; i < n; i++) {
			if (i > 0) sb.Append('\n');
			sb.Append(one(kind));
		}
		return sb.ToString();
	}

	static void ensure() {
		if (zh != null) return;
		lock (gate) {
			if (zh != null) return;
			var path = Path.Combine(NativeRuntime.BaseDir, "Bogus.dll");
			var asm = Assembly.Load(File.ReadAllBytes(path));
			var type = asm.GetType("Bogus.Faker");
			if (type == null) throw new InvalidOperationException("Bogus.dll 里没有 Bogus.Faker");
			zh = Activator.CreateInstance(type, "zh_CN");
			en = Activator.CreateInstance(type, "en");
		}
	}

	static string one(string kind) {
		switch (kind) {
		case "name": return zhname();
		case "cnnick": return callstr(prop(zh, "Name"), "FirstName");
		case "ennick": return callstr(prop(en, "Internet"), "UserName");
		case "enname": return callstr(prop(en, "Name"), "FullName");
		case "mail": return callstr(prop(en, "Internet"), "ExampleEmail");
		case "phone": return callstr(prop(zh, "Phone"), "PhoneNumber", "1##########");
		case "addr": return callstr(prop(zh, "Address"), "FullAddress");
		case "corp": return callstr(prop(zh, "Company"), "CompanyName");
		case "user": return callstr(prop(en, "Internet"), "UserName");
		case "id": return idno();
		case "mac": return callstr(prop(en, "Internet"), "Mac").ToUpperInvariant();
		case "uuid": return uuid();
		case "job": return job();
		case "city": return callstr(prop(zh, "Address"), "City");
		case "url": return callstr(prop(en, "Internet"), "Url");
		case "ip": return callstr(prop(en, "Internet"), "Ip");
		case "color": return callstr(prop(en, "Commerce"), "Color");
		case "date": return date();
		case "sentence": return callstr(prop(en, "Lorem"), "Sentence");
		case "product": return callstr(prop(en, "Commerce"), "ProductName");
		default: throw new ArgumentException("请选择类型");
		}
	}

	/// <summary>中文姓名是姓在前，中间不空格。Bogus 的 FullName 是名在前。</summary>
	static string zhname() {
		var name = prop(zh, "Name");
		return callstr(name, "LastName") + callstr(name, "FirstName");
	}

	static string job() {
		try { return callstr(prop(zh, "Name"), "JobTitle"); }
		catch { return callstr(prop(en, "Name"), "JobTitle"); }
	}

	static string date() {
		var value = call(prop(en, "Date"), "Past");
		if (value is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		return text(value);
	}

	static string uuid() {
		var random = prop(en, "Random");
		foreach (var name in new[] { "Uuid", "Guid" }) {
			var method = random.GetType().GetMethod(name, Type.EmptyTypes);
			if (method == null) continue;
			return text(method.Invoke(random, null));
		}
		return Guid.NewGuid().ToString();
	}

	static string idno() {
		var year = num(1975, 2004);
		var month = num(1, 12);
		var day = num(1, 28);
		var seq = num(0, 999);
		var body = "110101"
			+ year.ToString(CultureInfo.InvariantCulture)
			+ month.ToString("00", CultureInfo.InvariantCulture)
			+ day.ToString("00", CultureInfo.InvariantCulture)
			+ seq.ToString("000", CultureInfo.InvariantCulture);
		int[] w = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
		const string map = "10X98765432";
		var sum = 0;
		for (var i = 0; i < 17; i++) sum += (body[i] - '0') * w[i];
		return body + map[sum % 11];
	}

	static int num(int min, int max) {
		var value = call(prop(zh, "Random"), "Number", min, max);
		return Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	static object prop(object target, string name) {
		var info = target.GetType().GetProperty(name);
		if (info == null) throw new MissingMemberException(target.GetType().FullName, name);
		return info.GetValue(target);
	}

	static string callstr(object target, string name, params object[] args) => text(call(target, name, args));

	static string text(object value) {
		if (value == null) return "";
		if (value is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
	}

	static object call(object target, string name, params object[] args) {
		args ??= [];
		MethodInfo best = null;
		var bestExtra = int.MaxValue;
		foreach (var method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)) {
			if (method.Name != name) continue;
			var ps = method.GetParameters();
			if (ps.Length < args.Length) continue;
			var ok = true;
			for (var i = 0; i < args.Length; i++) {
				if (args[i] == null) {
					if (ps[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(ps[i].ParameterType) == null) {
						ok = false;
						break;
					}
					continue;
				}
				if (!ps[i].ParameterType.IsInstanceOfType(args[i])) {
					ok = false;
					break;
				}
			}
			if (!ok) continue;
			for (var i = args.Length; i < ps.Length; i++) {
				if (!ps[i].IsOptional && !ps[i].HasDefaultValue) {
					ok = false;
					break;
				}
			}
			if (!ok) continue;
			var extra = ps.Length - args.Length;
			if (extra < bestExtra) {
				best = method;
				bestExtra = extra;
			}
		}
		if (best == null) throw new MissingMethodException(target.GetType().FullName, name);
		var parms = best.GetParameters();
		var callArgs = new object[parms.Length];
		for (var i = 0; i < parms.Length; i++) {
			if (i < args.Length) {
				callArgs[i] = args[i];
				continue;
			}
			var def = parms[i].HasDefaultValue ? parms[i].DefaultValue : null;
			if (def == DBNull.Value) def = null;
			callArgs[i] = def;
		}
		try { return best.Invoke(target, callArgs); }
		catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
	}
}
