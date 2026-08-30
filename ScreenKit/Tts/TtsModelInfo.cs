namespace ScreenKit;

enum TtsModelType { Vits, Matcha }

/// <summary>语言标签：两位 ISO（zh/en/vi…）；可空表示未知。</summary>
static class TtsLang {
	public const string Zh = "zh";
	public const string En = "en";
	public const string Vi = "vi";
	public const string Ja = "ja";
	public const string Ko = "ko";
	public const string Yue = "yue";

	public static string Normalize(string s) {
		if (string.IsNullOrWhiteSpace(s)) return "";
		s = s.Trim().ToLowerInvariant().Replace('_', '-');
		// 区域性 → 主语言
		var dash = s.IndexOf('-');
		var primary = dash > 0 ? s.Substring(0, dash) : s;
		if (primary is "zh" or "cmn" or "chinese" or "中文" or "cn") return Zh;
		if (primary is "en" or "english" or "英文" or "英") return En;
		if (primary is "vi" or "vie" or "vietnamese" or "越南" or "越南语") return Vi;
		if (primary is "ja" or "jpn" or "japanese" or "日文" or "日语") return Ja;
		if (primary is "ko" or "kor" or "korean" or "韩文" or "韩语" or "朝鲜") return Ko;
		if (primary is "yue" or "cantonese" or "粤" or "粤语") return Yue;
		// 其它：保留两位或原 primary
		return primary.Length <= 3 ? primary : s;
	}

	/// <summary>短标签（发音人后缀）。</summary>
	public static string Label(string lang) => Normalize(lang) switch {
		Zh => Loc.T("lang.zh.short"),
		En => Loc.T("lang.en.short"),
		Vi => Loc.T("lang.vi.short"),
		Ja => Loc.T("lang.ja.short"),
		Ko => Loc.T("lang.ko.short"),
		Yue => Loc.T("lang.yue.short"),
		"" => "",
		var x => x,
	};

	/// <summary>筛选下拉显示名。</summary>
	public static string DisplayName(string lang) {
		lang = Normalize(lang);
		if (string.IsNullOrEmpty(lang)) return Loc.T("lang.all");
		return lang switch {
			Zh => $"{Loc.T("lang.zh")} (zh)",
			En => $"{Loc.T("lang.en")} (en)",
			Vi => $"{Loc.T("lang.vi")} (vi)",
			Ja => $"{Loc.T("lang.ja")} (ja)",
			Ko => $"{Loc.T("lang.ko")} (ko)",
			Yue => $"{Loc.T("lang.yue")} (yue)",
			_ => lang,
		};
	}

	/// <summary>是否匹配筛选（want 空=全部）。</summary>
	public static bool Match(string have, string want) {
		want = Normalize(want);
		if (string.IsNullOrEmpty(want)) return true;
		have = Normalize(have);
		if (string.IsNullOrEmpty(have)) return false;
		// 模型可标 zh,en
		foreach (var p in have.Split(new[] { ',', '/', '|', '+' }, StringSplitOptions.RemoveEmptyEntries)) {
			if (Normalize(p) == want) return true;
		}
		return have == want;
	}
}

/// <summary>性别标签：male / female；可空表示未知。</summary>
static class TtsGender {
	public const string Male = "male";
	public const string Female = "female";

	public static string Normalize(string s) {
		if (string.IsNullOrWhiteSpace(s)) return "";
		s = s.Trim().ToLowerInvariant();
		if (s is "m" or "male" or "man" or "男" or "男声" or "男性") return Male;
		if (s is "f" or "female" or "woman" or "女" or "女声" or "女性") return Female;
		return s;
	}

	public static string Label(string gender) => Normalize(gender) switch {
		Male => "男",
		Female => "女",
		_ => "",
	};

	public static bool Match(string have, string want) {
		want = Normalize(want);
		if (string.IsNullOrEmpty(want)) return true;
		have = Normalize(have);
		return !string.IsNullOrEmpty(have) && have == want;
	}

	/// <summary>从中文名/显示名推断男女。</summary>
	public static string InferFromText(string text) {
		if (string.IsNullOrEmpty(text)) return "";
		if (((text)?.IndexOf('男') ?? -1) >= 0) return Male;
		if (((text)?.IndexOf('女') ?? -1) >= 0) return Female;
		return "";
	}
}

/// <summary>Sherpa-ONNX TTS 模型目录信息（属性供 WPF 绑定）。</summary>
sealed class TtsModelInfo {
	public string DisplayName { get; set; } = "";
	public string ModelDir { get; set; } = "";
	public TtsModelType Type { get; set; } = TtsModelType.Vits;
	/// <summary>VITS: model.onnx | Matcha: model-steps-*.onnx</summary>
	public string OnnxFile { get; set; } = "model.onnx";
	/// <summary>Matcha vocoder 完整路径。</summary>
	public string VocoderPath { get; set; } = "";
	public bool HasLexicon { get; set; }
	public bool HasDictDir { get; set; }
	public bool HasRuleFsts { get; set; }
	/// <summary>播放/导出增益（来自 tts_config.json 的 volume，默认 1）。</summary>
	public float Volume { get; set; } = 1f;
	/// <summary>模型默认语言 zh/en，可逗号多语，如 zh,en。</summary>
	public string Lang { get; set; } = "";
	/// <summary>模型默认性别 male/female（单人模型常用）。</summary>
	public string Gender { get; set; } = "";
	public List<TtsSpeakerInfo> Speakers { get; set; } = new();
	public bool IsMultiSpeaker => Speakers.Count > 1;
	public override string ToString() => DisplayName;

	/// <summary>目录名推断语言（zh / en / zh,en）。按 token 匹配，避免 fanchen 含 en 误判。</summary>
	public static string InferLangFromName(string name) {
		if (string.IsNullOrEmpty(name)) return "";
		var n = name.ToLowerInvariant();
		// 显式双语
		if (n.Contains("zh_en") || n.Contains("zh-en") || n.Contains("melo"))
			return $"{TtsLang.Zh},{TtsLang.En}";
		var parts = n.Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		var zh = parts.Any(p => p is "zh" or "chinese" or "cn" or "aishell3" or "aishell" or "baker" or "fanchen")
			|| n.Contains("aishell") || n.Contains("baker") || n.Contains("fanchen");
		var en = parts.Any(p => p is "en" or "english" or "ljspeech" or "vctk" or "lj");
		if (zh && en) return $"{TtsLang.Zh},{TtsLang.En}";
		if (zh) return TtsLang.Zh;
		if (en) return TtsLang.En;
		return "";
	}
}

sealed class TtsSpeakerInfo {
	public string Name { get; set; } = "";
	public string ChineseName { get; set; } = "";
	public int Id { get; set; }
	/// <summary>语言 zh / en。</summary>
	public string Lang { get; set; } = "";
	/// <summary>性别 male / female。</summary>
	public string Gender { get; set; } = "";

	/// <summary>列表显示：中文名（性别·语言）或 原名。</summary>
	public string DisplayName {
		get {
			var baseName = string.IsNullOrEmpty(ChineseName) ? Name : ChineseName;
			var tags = new List<string>(2);
			var g = TtsGender.Label(Gender);
			var l = TtsLang.Label(Lang);
			if (!string.IsNullOrEmpty(g)) tags.Add(g);
			if (!string.IsNullOrEmpty(l)) tags.Add(l);
			if (tags.Count > 0) {
				// 有中文名时不重复塞英文 key，避免过长
				if (!string.IsNullOrEmpty(ChineseName) && !string.Equals(ChineseName, Name, StringComparison.Ordinal))
					return $"{baseName}（{string.Join("·", tags)}）";
				return $"{baseName}（{string.Join("·", tags)}）";
			}
			if (!string.IsNullOrEmpty(ChineseName) && ChineseName != Name)
				return $"{ChineseName}（{Name}）";
			return baseName;
		}
	}

	public override string ToString() => DisplayName;

	public bool MatchesFilter(string wantLang, string wantGender) =>
		TtsLang.Match(Lang, wantLang) && TtsGender.Match(Gender, wantGender);

	public static readonly Dictionary<string, string> ZhLlNames = new() {
		["suyingxue"] = "素影雪",
		["gunian"] = "谷念",
		["fushiyu"] = "浮世语",
		["bingjiao"] = "冰娇",
		["bazong"] = "八宗",
	};
}

/// <summary>推理设备偏好。</summary>
enum TtsComputeMode {
	Auto = 0,
	/// <summary>NVIDIA CUDA。</summary>
	Gpu = 1,
	Cpu = 2,
	/// <summary>Intel 核显等 DirectML。</summary>
	Igpu = 3,
}

/// <summary>TTS 引擎种类（UI 选择）。</summary>
enum TtsEngineKind {
	/// <summary>经典 System.Speech（SAPI5）。</summary>
	Sapi = 0,
	/// <summary>Windows Runtime / OneCore 神经语音（含越南语等）。</summary>
	WinRt = 2,
	/// <summary>Sherpa-ONNX 离线模型。</summary>
	Sherpa = 1,
}
