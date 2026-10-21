namespace ScreenKit;

/// <summary>按字符集判断语种：「自动」互译、OCR 源语言和发音用。只分到脚本级，同脚本的语言不细分。</summary>
static class LangDetect {
	/// <summary>返回检测到的语言代码（脚本代表语言）。没有文字（数字/标点）返回 zh。</summary>
	public static string DetectCode(string text) {
		var script = DetectScript(text);
		return script.Length == 0 ? TrLang.Zh : codeof(script);
	}

	/// <summary>
	/// 自动互译：原文是母语→译成学习语言；原文不是母语→译成母语。
	/// 原文既不是母语脚本也不是学习语言脚本时，按检测到的语言作为源，目标取母语。
	/// </summary>
	public static void DetectPair(string text, string native, string learn, out string src, out string dst) {
		TrLang.NormalizeAuto(native, learn, out var nat, out var lrn);
		var script = DetectScript(text);
		if (script.Length == 0) {
			src = nat;
			dst = lrn;
			return;
		}
		if (string.Equals(script, scriptof(nat), StringComparison.Ordinal)) {
			src = nat;
			dst = lrn;
			return;
		}
		if (string.Equals(script, scriptof(lrn), StringComparison.Ordinal)) {
			src = lrn;
			dst = nat;
			return;
		}
		src = codeof(script);
		dst = nat;
	}

	/// <summary>划词翻译。两边都是自动时：原文是母语就译成学习语言，否则译成母语。只自动一边时，另一边用选定语言。</summary>
	public static void ResolveSel(string text, string src, string dst, string native, string learn, out string outSrc, out string outDst) {
		TrLang.NormalizeAuto(native, learn, out var nat, out var lrn);
		src = TrLang.Normalize(src);
		dst = TrLang.Normalize(dst);
		var srcAuto = src.Length == 0 || src == TrLang.Auto;
		var dstAuto = dst.Length == 0 || dst == TrLang.Auto;
		if (srcAuto && dstAuto) {
			DetectPair(text, nat, lrn, out outSrc, out outDst);
			return;
		}
		if (srcAuto) src = DetectCode(text);
		if (dstAuto) dst = string.Equals(src, nat, StringComparison.OrdinalIgnoreCase) ? lrn : nat;
		outSrc = src;
		outDst = dst;
	}

	/// <summary>兼容旧调用：固定中英自动。</summary>
	public static void DetectZhEnPair(string text, out string src, out string dst) =>
		DetectPair(text, TrLang.Zh, TrLang.En, out src, out dst);

	/// <summary>主要文字脚本：han/kana/hangul/cyr/grek/arab/hebr/thai/deva/beng/mymr/khmr/lao/latin；空串=没有文字。</summary>
	public static string DetectScript(string text) {
		if (string.IsNullOrWhiteSpace(text)) return "";
		int han = 0, kana = 0, hangul = 0, cyr = 0, grek = 0, arab = 0, hebr = 0;
		int thai = 0, deva = 0, beng = 0, mymr = 0, khmr = 0, lao = 0, latin = 0;
		foreach (var ch in text) {
			switch (scriptof(ch)) {
				case "han": han++; break;
				case "kana": kana++; break;
				case "hangul": hangul++; break;
				case "cyr": cyr++; break;
				case "grek": grek++; break;
				case "arab": arab++; break;
				case "hebr": hebr++; break;
				case "thai": thai++; break;
				case "deva": deva++; break;
				case "beng": beng++; break;
				case "mymr": mymr++; break;
				case "khmr": khmr++; break;
				case "lao": lao++; break;
				case "latin": latin++; break;
			}
		}
		// 中文不用假名/谚文，出现即按日文/韩文；汉字按 2 倍计权，照顾中英混排
		if (kana > 0) return "kana";
		if (hangul > 0) return "hangul";
		var best = "";
		var n = 0;
		take("han", han * 2);
		take("latin", latin);
		take("cyr", cyr);
		take("grek", grek);
		take("arab", arab);
		take("hebr", hebr);
		take("thai", thai);
		take("deva", deva);
		take("beng", beng);
		take("mymr", mymr);
		take("khmr", khmr);
		take("lao", lao);
		return best;

		void take(string script, int count) {
			if (count <= 0 || count <= n) return;
			best = script;
			n = count;
		}
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

	/// <summary>语言代码对应的主脚本；未知语言按拉丁字母处理。</summary>
	static string scriptof(string code) => TrLang.Normalize(code) switch {
		"zh" or "cht" or "yue" => "han",
		"ja" => "kana",
		"ko" => "hangul",
		"ru" or "uk" or "mn" or "kk" => "cyr",
		"el" => "grek",
		"ar" or "fa" or "ug" => "arab",
		"he" => "hebr",
		"th" => "thai",
		"hi" => "deva",
		"bn" => "beng",
		"my" => "mymr",
		"km" => "khmr",
		"lo" => "lao",
		_ => "latin",
	};

	/// <summary>脚本的代表语言代码。</summary>
	static string codeof(string script) => script switch {
		"han" => TrLang.Zh,
		"kana" => "ja",
		"hangul" => "ko",
		"cyr" => "ru",
		"grek" => "el",
		"arab" => "ar",
		"hebr" => "he",
		"thai" => "th",
		"deva" => "hi",
		"beng" => "bn",
		"mymr" => "my",
		"khmr" => "km",
		"lao" => "lo",
		_ => TrLang.En,
	};

	/// <summary>单个字符所属脚本；不属于已支持脚本返回空串。</summary>
	static string scriptof(char ch) {
		if ((ch >= 0x4E00 && ch <= 0x9FFF) || (ch >= 0x3400 && ch <= 0x4DBF)
			|| (ch >= 0xF900 && ch <= 0xFAFF))
			return "han";
		if ((ch >= 0x3040 && ch <= 0x30FF) || (ch >= 0x31F0 && ch <= 0x31FF)
			|| (ch >= 0xFF66 && ch <= 0xFF9D))
			return "kana";
		if ((ch >= 0x1100 && ch <= 0x11FF) || (ch >= 0x3130 && ch <= 0x318F)
			|| (ch >= 0xAC00 && ch <= 0xD7AF))
			return "hangul";
		if ((ch >= 0x0400 && ch <= 0x052F) || (ch >= 0x2DE0 && ch <= 0x2DFF)
			|| (ch >= 0xA640 && ch <= 0xA69F))
			return "cyr";
		if ((ch >= 0x0370 && ch <= 0x03FF) || (ch >= 0x1F00 && ch <= 0x1FFF))
			return "grek";
		if ((ch >= 0x0600 && ch <= 0x06FF) || (ch >= 0x0750 && ch <= 0x077F)
			|| (ch >= 0x08A0 && ch <= 0x08FF) || (ch >= 0xFB50 && ch <= 0xFDFF)
			|| (ch >= 0xFE70 && ch <= 0xFEFF))
			return "arab";
		if ((ch >= 0x0590 && ch <= 0x05FF) || (ch >= 0xFB1D && ch <= 0xFB4F))
			return "hebr";
		if (ch >= 0x0E00 && ch <= 0x0E7F) return "thai";
		if ((ch >= 0x0900 && ch <= 0x097F) || (ch >= 0xA8E0 && ch <= 0xA8FF))
			return "deva";
		if (ch >= 0x0980 && ch <= 0x09FF) return "beng";
		if (ch >= 0x1000 && ch <= 0x109F) return "mymr";
		if (ch >= 0x1780 && ch <= 0x17FF) return "khmr";
		if (ch >= 0x0E80 && ch <= 0x0EFF) return "lao";
		if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')
			|| (ch >= 0x00C0 && ch <= 0x024F) || (ch >= 0x1E00 && ch <= 0x1EFF))
			return "latin";
		return "";
	}
}
