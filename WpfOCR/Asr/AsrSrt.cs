using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WpfOCR;

/// <summary>SRT 字幕条目。</summary>
sealed class AsrSrtCue {
	public int Index;
	public double Start;
	public double End;
	public string Text;
}

/// <summary>由 ASR 结果组装 SRT 字幕。</summary>
static class AsrSrt {
	/// <summary>合并极短条时参考的长度。</summary>
	const int SOFT_MAX_CHARS = 48;
	/// <summary>单条字幕建议最长（秒）；超时在词边界断开。</summary>
	const double SOFT_MAX_CUE_SEC = 7.0;
	/// <summary>硬上限：无标点也强制断（秒）。</summary>
	const double HARD_MAX_SEC = 14.0;
	const int HARD_MAX_CHARS = 90;
	/// <summary>token 间静音间隙 ≥ 此值则新起一条（秒）。</summary>
	const double PAUSE_GAP_SEC = 1.2;
	/// <summary>无 duration 时，单个 token 最长估计（秒）；避免 t1 跳到下一个很远的 stamp。</summary>
	const double MAX_TOKEN_SPAN_SEC = 1.2;
	const double DEFAULT_TOKEN_SEC = 0.18;
	const double MIN_CUE_SEC = 0.35;
	const int MIN_CUE_CHARS = 2;

	/// <summary>在逗号、顿号、句号等处切分（逗号会去掉）。</summary>
	static readonly Regex SplitAtPunct = new(
		@"([^，、,。！？；.!?;\n]*)([，、,。！？；.!?;\n]+)",
		RegexOptions.Compiled);

	/// <summary>从详细识别结果生成字幕条（优先 token 时间戳，否则按句均分时长）。</summary>
	public static List<AsrSrtCue> FromResult(AsrResult result, double audioSec) {
		if (result == null) return new List<AsrSrtCue>();
		if (result.HasTokenTimestamps)
			return fromTokens(result.Tokens, result.Timestamps, result.Durations, audioSec);
		var text = result.Text ?? "";
		if (string.IsNullOrWhiteSpace(text)) return new List<AsrSrtCue>();
		return fromPlainText(text, 0, Math.Max(audioSec, MIN_CUE_SEC));
	}

	public static string Format(IReadOnlyList<AsrSrtCue> cues) {
		if (cues == null || cues.Count == 0) return "";
		var sb = new StringBuilder(cues.Count * 64);
		for (int i = 0; i < cues.Count; i++) {
			var c = cues[i];
			var idx = c.Index > 0 ? c.Index : i + 1;
			sb.Append(idx).Append('\n');
			sb.Append(FormatTs(c.Start)).Append(" --> ").Append(FormatTs(c.End)).Append('\n');
			sb.Append(c.Text?.Trim() ?? "").Append("\n\n");
		}
		return sb.ToString().TrimEnd() + "\n";
	}

	public static void Save(string path, IReadOnlyList<AsrSrtCue> cues) {
		if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);
		File.WriteAllText(path, Format(cues), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	/// <summary>HH:MM:SS,mmm</summary>
	public static string FormatTs(double sec) {
		if (double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0) sec = 0;
		var totalMs = (long)Math.Round(sec * 1000.0);
		var ms = (int)(totalMs % 1000);
		var totalSec = totalMs / 1000;
		var s = (int)(totalSec % 60);
		var totalMin = totalSec / 60;
		var m = (int)(totalMin % 60);
		var h = (int)(totalMin / 60);
		return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00},{3:000}", h, m, s, ms);
	}

	static List<AsrSrtCue> fromTokens(string[] tokens, float[] stamps, float[] durs, double audioSec) {
		var items = new List<(string tok, double t0, double t1)>();
		for (int i = 0; i < tokens.Length; i++) {
			var tok = cleanToken(tokens[i]);
			if (tok.Length == 0) continue;
			var t0 = stamps[i];
			if (double.IsNaN(t0) || t0 < 0) t0 = 0;
			double t1 = estimateTokenEnd(t0, i, stamps, durs, tok);
			if (t1 <= t0) t1 = t0 + DEFAULT_TOKEN_SEC;
			items.Add((tok, t0, t1));
		}
		if (items.Count == 0) return new List<AsrSrtCue>();

		// 保证时间不倒退（坏时间戳时）
		for (int i = 1; i < items.Count; i++) {
			var (tok, t0, t1) = items[i];
			var prev = items[i - 1];
			if (t0 < prev.t0)
				t0 = prev.t1;
			if (t1 <= t0)
				t1 = t0 + DEFAULT_TOKEN_SEC;
			items[i] = (tok, t0, t1);
		}

		var cues = new List<AsrSrtCue>();
		var buf = new StringBuilder();
		double cueStart = items[0].t0;
		double cueEnd = items[0].t1;

		void flush(bool stripComma) {
			var t = buf.ToString().Trim();
			buf.Clear();
			if (stripComma) t = stripTrailComma(t);
			else t = t.Trim();
			t = t.Trim();
			if (t.Length < MIN_CUE_CHARS) return;
			if (cueEnd <= cueStart) cueEnd = cueStart + MIN_CUE_SEC;
			// 单条再兜底截断异常长结束时间
			if (cueEnd - cueStart > HARD_MAX_SEC * 2)
				cueEnd = cueStart + SOFT_MAX_CUE_SEC;
			if (audioSec > 0 && cueEnd > audioSec + 0.5) cueEnd = audioSec;
			// 极短条且紧挨上一条时合并
			if (cues.Count > 0 && t.Length <= 4) {
				var prev = cues[cues.Count - 1];
				if (prev.Text.Length + t.Length <= SOFT_MAX_CHARS
				    && cueStart - prev.End < PAUSE_GAP_SEC
				    && prev.End - prev.Start < SOFT_MAX_CUE_SEC) {
					prev.Text = stripTrailComma(prev.Text) + t;
					prev.End = Math.Max(prev.End, cueEnd);
					return;
				}
			}
			cues.Add(new AsrSrtCue {
				Index = cues.Count + 1,
				Start = Math.Max(0, cueStart),
				End = Math.Max(cueStart + MIN_CUE_SEC, cueEnd),
				Text = t,
			});
		}

		for (int i = 0; i < items.Count; i++) {
			var (tok, t0, t1) = items[i];

			if (buf.Length > 0) {
				// 关键：静音/停顿间隙 → 先结束上一条，再开新条
				var gap = t0 - cueEnd;
				if (gap >= PAUSE_GAP_SEC) {
					flush(stripComma: true);
					cueStart = t0;
					cueEnd = t1;
					buf.Append(tok);
					// 下面继续走标点/时长判断
				}
				else {
					// 软时长：当前条已够长则在词边界断（不断在词中间）
					var wouldDur = Math.Max(cueEnd, t1) - cueStart;
					if (wouldDur >= SOFT_MAX_CUE_SEC && displayLen(buf) >= 6) {
						flush(stripComma: true);
						cueStart = t0;
						cueEnd = t1;
						buf.Append(tok);
					}
					else {
						buf.Append(tok);
						// 仅小幅前移结束时间，不跨长静音
						if (t1 > cueEnd) cueEnd = t1;
					}
				}
			}
			else {
				cueStart = t0;
				cueEnd = t1;
				buf.Append(tok);
			}

			var weak = isWeakPunct(tok) || endsWithWeakPunct(buf);
			var strong = isStrongPunct(tok) || endsWithStrongPunct(buf);
			var len = displayLen(buf);
			var dur = cueEnd - cueStart;

			// 1) 逗号处断开（去掉逗号）
			if (weak && !strong) {
				flush(stripComma: true);
				continue;
			}
			// 2) 句号等强标点（保留）
			if (strong) {
				flush(stripComma: false);
				continue;
			}
			// 3) 硬上限兜底
			if (len >= HARD_MAX_CHARS || dur >= HARD_MAX_SEC)
				flush(stripComma: true);
		}
		if (buf.Length > 0)
			flush(stripComma: true);

		for (int i = 0; i < cues.Count; i++)
			cues[i].Index = i + 1;
		return cues;
	}

	/// <summary>
	/// 估计 token 结束时间。禁止把 t1 拉到「下一个很远的 stamp」（会把中间停顿算进同一条）。
	/// </summary>
	static double estimateTokenEnd(double t0, int i, float[] stamps, float[] durs, string tok) {
		if (durs != null && i < durs.Length && durs[i] > 0.02f && durs[i] < 30f)
			return t0 + durs[i];

		if (i + 1 < stamps.Length) {
			var next = (double)stamps[i + 1];
			var span = next - t0;
			// 正常连读：间隙很短，可接到下一 token 起点
			if (span > 0 && span <= MAX_TOKEN_SPAN_SEC)
				return next;
			// 中间有明显停顿：本 token 只占短时间
			if (span > MAX_TOKEN_SPAN_SEC)
				return t0 + Math.Min(MAX_TOKEN_SPAN_SEC, Math.Max(DEFAULT_TOKEN_SEC, tok.Length * 0.12));
		}

		// 末 token 或坏数据
		var est = Math.Max(DEFAULT_TOKEN_SEC, Math.Min(MAX_TOKEN_SPAN_SEC, tok.Length * 0.12));
		return t0 + est;
	}

	static List<AsrSrtCue> fromPlainText(string text, double startSec, double endSec) {
		text = text.Trim();
		if (text.Length == 0) return new List<AsrSrtCue>();
		var span = Math.Max(endSec - startSec, MIN_CUE_SEC);

		// 仅在逗号/句号处切；逗号去掉，句号保留
		var segs = new List<string>();
		var last = 0;
		foreach (Match m in SplitAtPunct.Matches(text)) {
			var body = m.Groups[1].Value;
			var punct = m.Groups[2].Value;
			var piece = body;
			if (isStrongPunctRun(punct))
				piece = body + takeFirstStrong(punct);
			// 弱标点（逗号）不拼进正文
			piece = piece.Trim();
			piece = stripTrailComma(piece);
			if (piece.Length >= MIN_CUE_CHARS)
				segs.Add(piece);
			last = m.Index + m.Length;
		}
		if (last < text.Length) {
			var tail = stripTrailComma(text.Substring(last).Trim());
			if (tail.Length >= MIN_CUE_CHARS)
				segs.Add(tail);
		}
		if (segs.Count == 0) {
			var one = stripTrailComma(text);
			if (one.Length > 0) segs.Add(one);
		}

		// 过长句再切；且按「预估时长」限制每段字数（避免整段文字占满整段音频）
		// 约 4 字/秒 的中文语速粗估
		var maxCharsByTime = Math.Max(12, (int)(SOFT_MAX_CUE_SEC * 4));
		var cutChars = Math.Min(HARD_MAX_CHARS, maxCharsByTime);
		var expanded = new List<string>();
		foreach (var s in segs) {
			if (s.Length <= cutChars) {
				expanded.Add(s);
				continue;
			}
			for (int i = 0; i < s.Length; i += cutChars) {
				var n = Math.Min(cutChars, s.Length - i);
				expanded.Add(s.Substring(i, n));
			}
		}
		segs = expanded;
		if (segs.Count == 0) return new List<AsrSrtCue>();

		// 按字数分配时间，但单条不超过 SOFT_MAX_CUE_SEC（多出来的时间作为「停顿」不归入字幕）
		var totalChars = segs.Sum(s => Math.Max(1, s.Length));
		var cues = new List<AsrSrtCue>();
		double cursor = startSec;
		for (int i = 0; i < segs.Count; i++) {
			var frac = Math.Max(1, segs[i].Length) / (double)totalChars;
			var rawDur = Math.Max(MIN_CUE_SEC, span * frac);
			// 限制单条显示时长；剩余视为间隙
			var speakDur = Math.Min(rawDur, SOFT_MAX_CUE_SEC);
			var t1 = cursor + speakDur;
			if (i == segs.Count - 1 && t1 < endSec) {
				// 最后一条可贴到 end，但仍不超硬上限
				t1 = Math.Min(endSec, cursor + HARD_MAX_SEC);
			}
			if (t1 <= cursor) t1 = cursor + MIN_CUE_SEC;
			cues.Add(new AsrSrtCue {
				Index = i + 1,
				Start = cursor,
				End = t1,
				Text = segs[i],
			});
			// 下一条：跳过「多余」时间，模拟停顿（避免一条字幕盖住整段静音）
			cursor = cursor + rawDur;
			if (cursor < t1) cursor = t1;
		}
		// 若最后超出 endSec，整体压缩回
		if (cues.Count > 0 && cues[cues.Count - 1].End > endSec + 0.05) {
			var scale = (endSec - startSec) / Math.Max(0.01, cues[cues.Count - 1].End - startSec);
			foreach (var c in cues) {
				c.Start = startSec + (c.Start - startSec) * scale;
				c.End = startSec + (c.End - startSec) * scale;
			}
		}
		return cues;
	}

	static string cleanToken(string tok) {
		if (string.IsNullOrEmpty(tok)) return "";
		if (tok.StartsWith("<|", StringComparison.Ordinal) && tok.EndsWith("|>", StringComparison.Ordinal))
			return "";
		if (tok == " " || tok == "▁" || tok == "\u2581") return " ";
		if (tok.StartsWith("\u2581", StringComparison.Ordinal) || tok.StartsWith("▁", StringComparison.Ordinal))
			return " " + tok.Substring(1);
		return tok;
	}

	static string stripTrailComma(string t) {
		if (string.IsNullOrEmpty(t)) return "";
		return t.TrimEnd('，', '、', ',', ' ', '\t', '\r', '\n');
	}

	static int displayLen(StringBuilder sb) {
		// 去掉末尾空白后计字数
		var n = sb.Length;
		while (n > 0 && char.IsWhiteSpace(sb[n - 1])) n--;
		return n;
	}

	static bool isStrongPunct(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		var c = s[s.Length - 1];
		return c is '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';';
	}

	static bool isWeakPunct(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		var c = s[s.Length - 1];
		return c is '，' or '、' or ',';
	}

	static bool endsWithStrongPunct(StringBuilder sb) {
		if (sb.Length == 0) return false;
		var c = sb[sb.Length - 1];
		return c is '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';';
	}

	static bool endsWithWeakPunct(StringBuilder sb) {
		if (sb.Length == 0) return false;
		var c = sb[sb.Length - 1];
		return c is '，' or '、' or ',';
	}

	static bool isStrongPunctRun(string punct) {
		if (string.IsNullOrEmpty(punct)) return false;
		foreach (var c in punct) {
			if (c is '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';')
				return true;
		}
		return false;
	}

	static string takeFirstStrong(string punct) {
		foreach (var c in punct) {
			if (c is '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';')
				return c.ToString();
		}
		return "";
	}
}
