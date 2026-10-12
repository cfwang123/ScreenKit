using System.Reflection;

namespace ScreenKit;

/// <summary>用程序旁的 NPinyin.dll 把汉字转成拼音或首字母。程序集从字节加载，卸掉文件时不锁住。</summary>
static class PinyinConvert {
	public const int MAXLEN = 20000;

	static readonly object gate = new();
	static Assembly asm;
	static MethodInfo py;
	static MethodInfo ini;

	public static bool Installed => NativeRuntime.HasNPinyin();

	/// <summary>initials 为真时每个字一个大写首字母，否则是空格分开、不带声调的拼音。</summary>
	public static string Convert(string text, bool initials) {
		if (!Installed) throw new InvalidOperationException("NPinyin 未安装");
		text ??= "";
		if (text.Length > MAXLEN) throw new ArgumentException("文字太长");
		lock (gate) {
			ensure();
			var m = initials ? ini : py;
			return m.Invoke(null, new object[] { text }) as string ?? "";
		}
	}

	static void ensure() {
		if (asm != null) return;
		var path = Path.Combine(NativeRuntime.BaseDir, "NPinyin.dll");
		asm = Assembly.Load(File.ReadAllBytes(path));
		var t = asm.GetType("NPinyin.Pinyin");
		if (t == null) throw new InvalidOperationException("NPinyin.dll 里没有 NPinyin.Pinyin");
		py = t.GetMethod("GetPinyin", new[] { typeof(string) });
		ini = t.GetMethod("GetInitials", new[] { typeof(string) });
		if (py == null || ini == null) throw new InvalidOperationException("NPinyin.dll 缺少 GetPinyin 或 GetInitials");
	}
}
