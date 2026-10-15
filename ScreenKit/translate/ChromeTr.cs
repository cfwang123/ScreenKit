namespace ScreenKit;

/// <summary>用隐藏的本机 Chrome（Translator）翻译。和朗读共用一个进程，多路请求同时进行。</summary>
static class ChromeTr {
	public static string Bcp(string code) {
		code = TrLang.Normalize(code);
		if (code == TrLang.Zh) return "zh-Hans";
		if (code == "cht") return "zh-Hant";
		return code;
	}

	public static string Translate(string text, string src, string dst, string exe, int idleSec, CancellationToken ct) {
		var list = TranslateMany(new[] { text ?? "" }, src, dst, exe, idleSec, ct);
		return list.Count > 0 ? list[0] ?? "" : "";
	}

	public static List<string> TranslateMany(IReadOnlyList<string> items, string src, string dst, string exe, int idleSec, CancellationToken ct) {
		var list = new List<string>();
		if (items == null || items.Count == 0) return list;
		src = TrLang.Normalize(src);
		dst = TrLang.Normalize(dst);
		if (src.Length == 0 || dst.Length == 0 || src == TrLang.Auto || dst == TrLang.Auto)
			throw new InvalidOperationException("请选择源语言和目标语言");
		if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) {
			foreach (var s in items) list.Add(s ?? "");
			return list;
		}
		var texts = new List<string>();
		foreach (var s in items) texts.Add(s ?? "");
		return ChromeHost.Translate(texts, Bcp(src), Bcp(dst), exe, idleSec, ct);
	}

	public static int Test() {
		if (Bcp("zh") != "zh-Hans") return 2;
		if (Bcp("cht") != "zh-Hant") return 2;
		if (Bcp("zh-TW") != "zh-Hant") return 2;
		if (Bcp("en") != "en") return 2;
		if (Bcp("ja") != "ja") return 2;
		if (TryRun(out _)) return 2;
		return 0;
	}

	/// <summary>和朗读是同一个进程。children 是它下面还活着的子进程数。</summary>
	public static bool TryRun(out int children) => ChromeHost.TryRun(out children);

	public static void Stop() => ChromeHost.Stop();

	/// <summary>和朗读共用的用户目录。翻译包也装在这里。</summary>
	public static string ProfileDir() => ChromeTts.ProfileDir();
}
