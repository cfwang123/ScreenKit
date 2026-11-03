using System.Text;
using ScreenKit.Json;

namespace ScreenKit;

/// <summary>
/// 除 config.toml 之外的用户数据都放在 exe 旁的 data.json：便笺、性能小窗、
/// 数据库连接、HTTP 调试请求、文档阅读器的历史与标签。
/// 各模块只认自己那一段，读写都经过这里，避免多个文件各写各的。
/// 程序自带、不随用户变化的文件（如 ocr-display.json）不走这里。
/// </summary>
static class DataStore {
	const string FILE_NAME = "data.json";

	static readonly object Gate = new();
	/// <summary>整本数据的缓存；NotExist 表示还没读。</summary>
	static JSONF root;
	static string pathOverride;

	public static string StorePath =>
		pathOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FILE_NAME);

	/// <summary>自测用：把存储指到临时文件。传空恢复默认位置。</summary>
	public static void UsePath(string path) {
		lock (Gate) {
			pathOverride = string.IsNullOrWhiteSpace(path) ? null : path;
			root = default;
		}
	}

	/// <summary>丢掉内存缓存，下次读写重新读盘。</summary>
	public static void Reset() {
		lock (Gate) root = default;
	}

	/// <summary>读整本数据；文件不在或坏了都当空对象，不让调用方崩。</summary>
	static JSONF book() {
		lock (Gate) {
			if (root.Type == JSONF.NodeType.Object) return root;
			root = JSONF.newObject();
			var p = StorePath;
			try {
				if (File.Exists(p)) {
					var text = File.ReadAllText(p, Encoding.UTF8);
					if (!string.IsNullOrWhiteSpace(text)) {
						var got = JSONF.Parse(text);
						if (got.Type == JSONF.NodeType.Object) root = got;
					}
				}
			}
			catch { /* 坏了就当空 */ }
			return root;
		}
	}

	/// <summary>读某一段；没有这段时 Type 为 NotExist。</summary>
	public static JSONF Section(string name) {
		if (string.IsNullOrEmpty(name)) return default;
		var v = book().get(name);
		return v.Type is JSONF.NodeType.Object or JSONF.NodeType.Array ? v : default;
	}

	/// <summary>取一段并反序列化成对象；没有或坏了返回 null。</summary>
	public static T Get<T>(string name) where T : class {
		try {
			var sec = Section(name);
			if (sec.Type == JSONF.NodeType.NotExist) return null;
			return sec.ToObject<T>();
		}
		catch { return null; }
	}

	/// <summary>写一段并立刻落盘。</summary>
	public static void Put<T>(string name, T value) {
		if (string.IsNullOrEmpty(name) || value == null) return;
		try {
			var node = JSONF.FromObject(value);
			lock (Gate) {
				book()[name] = node;
			}
			save();
		}
		catch { /* 存不下来不影响使用 */ }
	}

	/// <summary>删掉一段（不需要就别留空壳）。</summary>
	public static void Remove(string name) {
		if (string.IsNullOrEmpty(name)) return;
		lock (Gate) {
			var b = book();
			if (b.ContainsKey(name)) b.Remove(name);
		}
		save();
	}

	/// <summary>整本写回磁盘：先写临时文件再替换，避免写到一半断电留下半个文件。</summary>
	static void save() {
		lock (Gate) {
			try {
				var p = StorePath;
				var dir = Path.GetDirectoryName(p);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
				var json = book().ToJSONString(2);
				var tmp = p + ".tmp";
				File.WriteAllText(tmp, json, new UTF8Encoding(false));
				if (File.Exists(p)) File.Replace(tmp, p, null);
				else File.Move(tmp, p);
			}
			catch { /* 忽略 */ }
		}
	}
}
