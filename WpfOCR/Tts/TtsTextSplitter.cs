namespace WpfOCR;

/// <summary>TTS 句段（原文 [Start, End) 偏移，供高亮）。</summary>
sealed class TtsSegment {
	public string Text { get; set; } = "";
	public int Start { get; set; }
	public int End { get; set; }
	public override string ToString() => Text;
}

/// <summary>
/// 按句末标点切分文本，保留原文偏移。
/// 对齐安卓 reader TextLoader.splitSentenceSpans（NEWLINE 模式）。
/// </summary>
static class TtsTextSplitter {
	/// <summary>单段过长时再切（保护 Sherpa）。</summary>
	public const int MaxSegmentChars = 300;

	/// <summary>
	/// 分句：。！？；… ． 及英文 .!?；换行即句尾；超长再按逗号/空格切开。
	/// 输入建议已 NormalizeNewlines；返回偏移基于同一字符串。
	/// </summary>
	public static List<TtsSegment> Split(string text) {
		var result = new List<TtsSegment>();
		if (string.IsNullOrEmpty(text)) return result;

		var src = NormalizeNewlines(text);
		foreach (var sp in splitsentencespans(src)) {
			if (sp.Text.Length <= MaxSegmentChars)
				result.Add(sp);
			else
				result.AddRange(splitlong(src, sp.Start, sp.End));
		}
		return result;
	}

	public static string NormalizeNewlines(string text) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		return text.Replace("\r\n", "\n").Replace('\r', '\n');
	}

	/// <summary>
	/// 将规范化串中的 [normStart, normEnd) 映射回可能含 \r\n 的 UI 文本偏移。
	/// </summary>
	public static (int start, int end) MapToUiOffsets(string uiText, int normStart, int normEnd) {
		if (string.IsNullOrEmpty(uiText) || normEnd <= normStart)
			return (0, 0);
		if (uiText.IndexOf('\r') < 0)
			return (
				Compat.Clamp(normStart, 0, uiText.Length),
				Compat.Clamp(normEnd, 0, uiText.Length));

		int ui = 0, norm = 0;
		var uiStart = -1;
		var uiEnd = -1;
		while (ui <= uiText.Length && norm <= normEnd) {
			if (norm == normStart && uiStart < 0) uiStart = ui;
			if (norm == normEnd) { uiEnd = ui; break; }
			if (ui >= uiText.Length) break;

			if (uiText[ui] == '\r' && ui + 1 < uiText.Length && uiText[ui + 1] == '\n') {
				ui += 2;
				norm += 1;
			}
			else {
				ui += 1;
				norm += 1;
			}
		}
		if (uiStart < 0) uiStart = Math.Min(normStart, uiText.Length);
		if (uiEnd < 0) uiEnd = uiText.Length;
		return (uiStart, Math.Max(uiStart, uiEnd));
	}

	static List<TtsSegment> splitsentencespans(string text) {
		var spans = new List<TtsSegment>(8);
		if (text.Length == 0) return spans;
		var start = 0;
		var i = 0;
		while (i < text.Length) {
			var c = text[i];
			if (c == '\n') {
				addtrimmed(spans, text, start, i);
				var j = i + 1;
				while (j < text.Length && text[j] == '\n') j++;
				start = j;
				i = j;
				continue;
			}

			var endAt = -1;
			switch (c) {
				case '。':
				case '！':
				case '？':
				case '．':
				case '；':
					endAt = i + 1;
					break;
				case '…': {
					var j = i;
					while (j < text.Length && (text[j] == '…' || text[j] == '.')) j++;
					endAt = j;
					break;
				}
				case '!':
				case '?':
					endAt = i + 1;
					break;
				case '.': {
					var next = i + 1 < text.Length ? text[i + 1] : '\0';
					if (next == '\0' || char.IsWhiteSpace(next) || next == '\n'
						|| next == '"' || next == '\u201D' || next == '」' || next == '』')
						endAt = i + 1;
					break;
				}
			}
			if (endAt > 0) {
				while (endAt < text.Length) {
					var n = text[endAt];
					if (n is '」' or '』' or '"' or '\u201D' or '\'')
						endAt++;
					else break;
				}
				addtrimmed(spans, text, start, endAt);
				start = endAt;
				i = endAt;
				continue;
			}
			i++;
		}
		if (start < text.Length)
			addtrimmed(spans, text, start, text.Length);
		if (spans.Count == 0) {
			var t = text.Trim();
			if (t.Length > 0) {
				var s = text.IndexOf(t, StringComparison.Ordinal);
				if (s < 0) s = 0;
				spans.Add(new TtsSegment { Text = t, Start = s, End = s + t.Length });
			}
		}
		return spans;
	}

	static void addtrimmed(List<TtsSegment> outList, string paragraph, int from, int to) {
		var a = from;
		var b = to;
		while (a < b && char.IsWhiteSpace(paragraph[a])) a++;
		while (b > a && char.IsWhiteSpace(paragraph[b - 1])) b--;
		if (a < b) {
			outList.Add(new TtsSegment {
				Text = paragraph.Substring(a, b - a),
				Start = a,
				End = b,
			});
		}
	}

	/// <summary>在 [from,to) 内按 MaxSegmentChars 切开（绝对偏移）。</summary>
	static List<TtsSegment> splitlong(string full, int from, int to) {
		var list = new List<TtsSegment>();
		var i = from;
		while (i < to) {
			while (i < to && char.IsWhiteSpace(full[i])) i++;
			if (i >= to) break;
			if (to - i <= MaxSegmentChars) {
				addtrimmed(list, full, i, to);
				break;
			}
			var limit = i + MaxSegmentChars;
			if (limit > to) limit = to;
			var cut = -1;
			for (var j = limit; j > i + MaxSegmentChars / 3; j--) {
				var c = full[j - 1];
				if (c is '，' or '、' or ',' or ';' or '；' or ' ' or '\t') {
					cut = j;
					break;
				}
			}
			if (cut <= i) cut = limit;
			addtrimmed(list, full, i, cut);
			i = cut;
		}
		return list;
	}
}
