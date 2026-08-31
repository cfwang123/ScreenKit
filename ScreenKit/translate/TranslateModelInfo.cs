namespace ScreenKit;

/// <summary>一对 Opus-MT 方向（如 zh→en）。</summary>
sealed class TranslateModelInfo {
	/// <summary>管道方向键，如 zh-en。</summary>
	public string DirKey { get; set; } = "";
	/// <summary>源语言代码：zh / en / …</summary>
	public string SourceLang { get; set; } = "";
	/// <summary>目标语言代码。</summary>
	public string TargetLang { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string ModelDir { get; set; } = "";
	public bool IsReady { get; set; }
	/// <summary>是否 ONNX 后端（encoder/decoder.onnx）。</summary>
	public bool IsOnnx { get; set; }

	public string ListName => IsReady
		? DisplayName
		: DisplayName + Loc.T("missing.files");

	public string PairLabel =>
		$"{TrLang.Label(SourceLang)} → {TrLang.Label(TargetLang)}";
}

/// <summary>翻译语言代码与显示名。</summary>
static class TrLang {
	public const string Auto = "auto";
	public const string Zh = "zh";
	public const string En = "en";

	/// <summary>LLM 翻译可选语言（ONNX 仍只列出已装模型）。常用语种靠前。</summary>
	public static readonly string[] LlmCodes = {
		"zh", "en", "ja", "ko", "cht", "yue",
		"fr", "de", "es", "pt", "ru", "it",
		"ar", "th", "vi", "id", "ms", "hi",
		"tr", "pl", "nl", "uk", "sv", "cs",
		"el", "he", "fa", "bn", "fi", "hu",
		"ro", "da", "no", "tl", "my", "km",
		"lo", "mn", "uz", "kk", "ug",
	};

	static readonly HashSet<string> LlmSet = new(LlmCodes, StringComparer.OrdinalIgnoreCase);

	public static string Label(string code) => Loc.LangName(code);

	public static bool IsLlm(string code) {
		code = Normalize(code);
		return code.Length > 0 && code != Auto && LlmSet.Contains(code);
	}

	public static int CompareLlm(string a, string b) {
		var d = llmrank(a).CompareTo(llmrank(b));
		if (d != 0) return d;
		return string.Compare(Label(a), Label(b), StringComparison.OrdinalIgnoreCase);
	}

	static int llmrank(string code) {
		code = Normalize(code);
		for (int i = 0; i < LlmCodes.Length; i++)
			if (string.Equals(LlmCodes[i], code, StringComparison.OrdinalIgnoreCase))
				return i;
		return 1000;
	}

	public static string Normalize(string code) => (code ?? "").Trim().ToLowerInvariant() switch {
		"zho" or "chinese" or "cn" or "zh-cn" or "zh-hans" or "zh_cn" => Zh,
		"eng" or "english" => En,
		"jpn" or "japanese" => "ja",
		"kor" or "korean" => "ko",
		"zh-tw" or "zh-hk" or "zh-hant" or "zh_tw" or "zh_hk" or "cht" or "tchinese" => "cht",
		"yue" or "cantonese" => "yue",
		"fra" or "fre" or "french" => "fr",
		"deu" or "ger" or "german" => "de",
		"spa" or "spanish" => "es",
		"por" or "portuguese" => "pt",
		"rus" or "russian" => "ru",
		"ita" or "italian" => "it",
		"ara" or "arabic" => "ar",
		"tha" or "thai" => "th",
		"vie" or "vietnamese" => "vi",
		"ind" or "indonesian" => "id",
		"msa" or "may" or "malay" => "ms",
		"hin" or "hindi" => "hi",
		"tur" or "turkish" => "tr",
		"pol" or "polish" => "pl",
		"nld" or "dut" or "dutch" => "nl",
		"ukr" or "ukrainian" => "uk",
		"swe" or "swedish" => "sv",
		"ces" or "cze" or "czech" => "cs",
		"ell" or "gre" or "greek" => "el",
		"heb" or "iw" or "hebrew" => "he",
		"fas" or "per" or "persian" or "farsi" => "fa",
		"ben" or "bengali" => "bn",
		"fin" or "finnish" => "fi",
		"hun" or "hungarian" => "hu",
		"ron" or "rum" or "romanian" => "ro",
		"dan" or "danish" => "da",
		"nor" or "norwegian" => "no",
		"tgl" or "fil" or "filipino" or "tagalog" => "tl",
		"mya" or "burmese" => "my",
		"khm" or "khmer" => "km",
		"lao" => "lo",
		"mon" or "mongolian" => "mn",
		"uzb" or "uzbek" => "uz",
		"kaz" or "kazakh" => "kk",
		"uig" or "uyghur" or "uighur" => "ug",
		var c => c,
	};

	/// <summary>从 opus-mt-zh-en / zh-en 解析源、目标。</summary>
	public static bool TryParsePair(string nameOrKey, out string src, out string dst) {
		src = dst = "";
		if (string.IsNullOrWhiteSpace(nameOrKey)) return false;
		var s = nameOrKey.Trim().ToLowerInvariant().Replace('_', '-');
		// opus-mt-zh-en / helsinki-nlp-opus-mt-en-zh
		var idx = s.LastIndexOf("opus-mt-", StringComparison.Ordinal);
		if (idx >= 0)
			s = s.Substring(idx + "opus-mt-".Length);
		// 已是 zh-en
		var parts = s.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2) return false;
		// 取最后两段为语言（兼容 mul-en 等）
		src = Normalize(parts[parts.Length - 2]);
		dst = Normalize(parts[parts.Length - 1]);
		return src.Length > 0 && dst.Length > 0;
	}
}
