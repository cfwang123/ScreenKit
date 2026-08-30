using System.Text;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>
/// ASR 文本后处理：WeText C++ ITN + 中文逐位读数 + 日期时间空格 + 句末标点。
/// </summary>
static class AsrTextNorm {
	/// <summary>逐位中文数字：至少连续几位才转阿拉伯数字（默认 3，避免「一条」→「1条」）。</summary>
	public const int CnDigitMinRun = 3;

	static readonly Regex ReDateTimeGlue = new(
		@"(\d{4}[/\-.]\d{1,2}[/\-.]\d{1,2})(\d{1,2}:\d{2}(?::\d{2})?)",
		RegexOptions.Compiled);
	static readonly Regex ReDateTimeCnGlue = new(
		@"(\d{4}年\d{1,2}月\d{1,2}日)(\d{1,2}(?:[:：点时]|\d))",
		RegexOptions.Compiled);
	// 数字日期：2026/3/30、2026.03.30、2026-3-30
	static readonly Regex ReDateNumeric = new(
		@"\b(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})\b",
		RegexOptions.Compiled);
	// 中文日期：2026年3月30日
	static readonly Regex ReDateCn = new(
		@"(\d{4})年(\d{1,2})月(\d{1,2})日",
		RegexOptions.Compiled);
	// 量词前个位数字：1条→一条（ITN 误伤）
	static readonly Regex ReDigitClassifier = new(
		@"([1-9])([条个只件次本篇张位名岁天句声])",
		RegexOptions.Compiled);
	// 阿拉伯数字之间的空白（含全角空格）：12345 67890 → 1234567890
	static readonly Regex ReDigitGap = new(
		@"(\d)[\s\u00A0\u3000]+(?=\d)",
		RegexOptions.Compiled);
	// 中文数位词之间的空白（转换前合并，避免「一二三四五 六七八九」拆成两段）
	static readonly Regex ReCnDigitGap = new(
		@"([零〇○洞幺一二两三兩四五六七八九壹贰貳叁肆伍陆陸柒捌玖])[\s\u00A0\u3000]+(?=[零〇○洞幺一二两三兩四五六七八九壹贰貳叁肆伍陆陸柒捌玖])",
		RegexOptions.Compiled);

	/// <summary>
	/// 完整后处理：wetext ITN → 日期空格 → 统一 <c>yyyy-MM-dd</c> → 量词还原 → 连续≥3 中文数字 → 数字间空格合并。
	/// </summary>
	public static string Postprocess(string text) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		try {
			if (WetextItn.IsAvailable)
				text = WetextItn.Normalize(text);
		}
		catch { }
		text = FixDateTimeSpacing(text);
		text = FormatDatesIso(text);
		// ITN 常把「一条」变成「1条」：量词前个位改回中文
		text = RestoreCnDigitBeforeClassifier(text);
		// 中文数位词间空格先合并，再整段转阿拉伯数字
		text = CollapseCnDigitSpaces(text);
		// 仅连续 ≥3 个中文数位词才转阿拉伯数字
		text = NormalizeCnDigits(text, CnDigitMinRun);
		// 12345 67890 → 1234567890（ITN/断句常在长数字中插空格）
		text = CollapseDigitSpaces(text);
		// 合并数字空格可能把「日期+时间」粘上，再补回
		text = FixDateTimeSpacing(text);
		text = FormatDatesIso(text);
		return text;
	}

	/// <summary>
	/// 去掉阿拉伯数字之间的空白：<c>12345 67890</c> → <c>1234567890</c>。
	/// 日期时间粘连由后续 <see cref="FixDateTimeSpacing"/> 再拆开。
	/// </summary>
	public static string CollapseDigitSpaces(string text) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		return ReDigitGap.Replace(text, "$1");
	}

	/// <summary>去掉中文数位词之间的空白，便于整段逐位转换。</summary>
	public static string CollapseCnDigitSpaces(string text) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		return ReCnDigitGap.Replace(text, "$1");
	}

	/// <summary>
	/// 日期与时间粘连时补空格：
	/// <c>2022/01/0312:34:56</c> → <c>2022/01/03 12:34:56</c>
	/// </summary>
	public static string FixDateTimeSpacing(string text) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		text = ReDateTimeGlue.Replace(text, "$1 $2");
		// 中文日期后直接接时刻：2022年1月2日12点 → 2022年1月2日 12点
		text = ReDateTimeCnGlue.Replace(text, "$1 $2");
		return text;
	}

	/// <summary>
	/// 统一日期为 <c>yyyy-MM-dd</c>（如 2026-03-30）。
	/// 支持 2026/3/30、2026.03.30、2026年3月30日。
	/// </summary>
	public static string FormatDatesIso(string text) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		text = ReDateCn.Replace(text, m => {
			if (!tryparsedate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out var iso))
				return m.Value;
			return iso;
		});
		text = ReDateNumeric.Replace(text, m => {
			if (!tryparsedate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out var iso))
				return m.Value;
			return iso;
		});
		return text;
	}

	static bool tryparsedate(string ys, string ms, string ds, out string iso) {
		iso = null;
		if (!int.TryParse(ys, out var y) || !int.TryParse(ms, out var mo) || !int.TryParse(ds, out var d))
			return false;
		if (y < 1900 || y > 2100 || mo < 1 || mo > 12 || d < 1 || d > 31)
			return false;
		// 简单合法日：不严格校验大小月
		iso = $"{y:D4}-{mo:D2}-{d:D2}";
		return true;
	}

	/// <summary>
	/// 句末若无标点则补「，」（断句/endpoint 用；不用句号）。
	/// </summary>
	public static string EnsureSentenceEnd(string text) {
		if (string.IsNullOrWhiteSpace(text)) return text ?? "";
		text = text.TrimEnd();
		if (text.Length == 0) return text;
		var last = text[text.Length - 1];
		if (last is '。' or '！' or '？' or '；' or '…'
			or '.' or '!' or '?' or ';'
			or '，' or ',' or '、' or ':' or '：')
			return text;
		return text + "，";
	}

	/// <summary>
	/// 量词前的个位阿拉伯数字还原为中文（抑制 ITN「一条→1条」）。
	/// 例：这是1条测试 → 这是一条测试
	/// </summary>
	public static string RestoreCnDigitBeforeClassifier(string text) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		return ReDigitClassifier.Replace(text, m => {
			var d = m.Groups[1].Value[0];
			var cl = m.Groups[2].Value;
			return digittozh(d) + cl;
		});
	}

	static char digittozh(char d) => d switch {
		'0' => '零',
		'1' => '一',
		'2' => '二',
		'3' => '三',
		'4' => '四',
		'5' => '五',
		'6' => '六',
		'7' => '七',
		'8' => '八',
		'9' => '九',
		_ => d,
	};

	/// <summary>
	/// 中文数位词 → 阿拉伯数字（电话/房号等）。
	/// 默认连续 ≥3 位才转换，避免「一条」「两个」被改。
	/// </summary>
	public static string NormalizeCnDigits(string text, int minRun = CnDigitMinRun) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		if (minRun < 1) minRun = 1;
		var sb = new StringBuilder(text.Length);
		var runZh = new StringBuilder();
		var runD = new StringBuilder();
		foreach (var ch in text) {
			// 仅中文数位词参与「连续位数」；已是阿拉伯数字原样输出（不凑 run）
			if (trydigitZh(ch, out var d)) {
				runZh.Append(ch);
				runD.Append(d);
				continue;
			}
			flushrun(sb, runZh, runD, minRun);
			sb.Append(ch);
		}
		flushrun(sb, runZh, runD, minRun);
		return sb.ToString();
	}

	static void flushrun(StringBuilder sb, StringBuilder runZh, StringBuilder runD, int minRun) {
		if (runD.Length == 0) return;
		if (runD.Length >= minRun)
			sb.Append(runD);
		else
			sb.Append(runZh);
		runZh.Clear();
		runD.Clear();
	}

	/// <summary>仅中文数位词 / 口语变体（不含 '0'-'9' 字符）。</summary>
	public static bool trydigitZh(char ch, out char digit) {
		digit = '\0';
		switch (ch) {
			case '零':
			case '〇':
			case '○':
			case '洞':
				digit = '0';
				return true;
			case '一':
			case '幺':
			case '壹':
				digit = '1';
				return true;
			case '二':
			case '两':
			case '兩':
			case '贰':
			case '貳':
				digit = '2';
				return true;
			case '三':
			case '叁':
				digit = '3';
				return true;
			case '四':
			case '肆':
				digit = '4';
				return true;
			case '五':
			case '伍':
				digit = '5';
				return true;
			case '六':
			case '陆':
			case '陸':
				digit = '6';
				return true;
			case '七':
			case '柒':
				digit = '7';
				return true;
			case '八':
			case '捌':
				digit = '8';
				return true;
			case '九':
			case '玖':
				digit = '9';
				return true;
			default:
				return false;
		}
	}
}
