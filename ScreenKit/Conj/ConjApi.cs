namespace ScreenKit;

/// <summary>日/韩/英动词变形的统一入口。网页走 HTTP，词典工具以后直接调这里。</summary>
static class ConjApi {
	/// <summary>按语言分发。ko/en 用 type（也接受 kind），ja 用 kind/flag/sub。</summary>
	public static ConjResult Build(string lang, string word, string kind, string flag, string sub, string type, string uiLang) {
		var l = (lang ?? "ja").Trim().ToLowerInvariant();
		if (l.StartsWith("ko", StringComparison.Ordinal))
			return KoConj.Build(word, pick(type, kind), uiLang);
		if (l.StartsWith("en", StringComparison.Ordinal))
			return EnConj.Build(word, pick(type, kind), uiLang);
		return JpConj.Build(word, kind, flag, sub, uiLang);
	}

	static string pick(string a, string b) =>
		string.IsNullOrWhiteSpace(a) ? (b ?? "") : a;
}
