namespace WpfOCR;

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
		: DisplayName + "（缺文件）";

	public string PairLabel =>
		$"{TrLang.Label(SourceLang)} → {TrLang.Label(TargetLang)}";
}

/// <summary>翻译语言代码与显示名。</summary>
static class TrLang {
	public const string Auto = "auto";
	public const string Zh = "zh";
	public const string En = "en";

	public static string Label(string code) => (code ?? "").Trim().ToLowerInvariant() switch {
		"zh" or "zho" or "chinese" => "中文",
		"en" or "eng" or "english" => "英文",
		"ja" or "jpn" or "japanese" => "日文",
		"ko" or "kor" or "korean" => "韩文",
		"auto" => "自动",
		_ => string.IsNullOrEmpty(code) ? "?" : code,
	};

	public static string Normalize(string code) => (code ?? "").Trim().ToLowerInvariant() switch {
		"zho" or "chinese" or "cn" => Zh,
		"eng" or "english" => En,
		"jpn" or "japanese" => "ja",
		"kor" or "korean" => "ko",
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
