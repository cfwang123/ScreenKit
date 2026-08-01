namespace WpfOCR;

/// <summary>极简语种检测：汉字占比判定中/英（仅用于「自动」中英互译）。</summary>
static class LangDetect {
	/// <summary>返回 zh 或 en（默认 zh）。</summary>
	public static string DetectCode(string text) {
		if (string.IsNullOrWhiteSpace(text)) return TrLang.Zh;
		int han = 0, letter = 0;
		foreach (var ch in text) {
			if (ch >= 0x4E00 && ch <= 0x9FFF) han++;
			else if (char.IsLetter(ch)) letter++;
		}
		if (han == 0 && letter == 0) return TrLang.Zh;
		// 汉字不少 → 中文；否则英文
		return han * 2 >= letter ? TrLang.Zh : TrLang.En;
	}

	/// <summary>自动中英：检测源语言，目标为另一种。</summary>
	public static void DetectZhEnPair(string text, out string src, out string dst) {
		src = DetectCode(text);
		dst = string.Equals(src, TrLang.Zh, StringComparison.OrdinalIgnoreCase) ? TrLang.En : TrLang.Zh;
	}

	public static TranslateDirection Detect(string text) {
		DetectZhEnPair(text, out var src, out var dst);
		return string.Equals(src, TrLang.En, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(dst, TrLang.Zh, StringComparison.OrdinalIgnoreCase)
			? TranslateDirection.EnToZh
			: TranslateDirection.ZhToEn;
	}

	public static string DirKey(TranslateDirection d) =>
		d == TranslateDirection.EnToZh ? "en-zh" : "zh-en";

	public static string DirKey(string src, string dst) =>
		$"{TrLang.Normalize(src)}-{TrLang.Normalize(dst)}";
}
