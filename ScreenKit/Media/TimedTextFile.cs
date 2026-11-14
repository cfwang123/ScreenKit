using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenKit;

// ==================== 定时文本模型 ====================
#region
sealed class TimedTextCue {
	public double Start;
	public double End;
	public string Text = "";
	public string Speaker = "";
	/// <summary>词级时间，可空；null 表示按权重均匀推算。</summary>
	public List<WordTime> Words;

	public double Duration => End > Start ? End - Start : 0;
}

sealed class WordTime {
	public int CharStart, CharLen;
	public double Start;
}

sealed class TimedTextFile {
	public List<TimedTextCue> Cues = new();
	public double OffsetSec;
	public string Encoding = "";
	/// <summary>true = LRC 歌词来源。</summary>
	public bool IsLyric;
	/// <summary>所有 cue 都有词级时间才为 true。</summary>
	public bool HasWordTime;
	public string Title = "";
	public string Artist = "";
	public string Path = "";

	public double LengthSec => Cues.Count == 0 ? 0 : Cues[Cues.Count - 1].End;

	/// <summary>二分查找当前 cue；返回 null 表示此刻无字幕。</summary>
	public TimedTextCue At(double tSec) {
		if (Cues.Count == 0) return null;
		var t = tSec - OffsetSec;
		int lo = 0, hi = Cues.Count - 1, hit = -1;
		while (lo <= hi) {
			var mid = (lo + hi) / 2;
			if (Cues[mid].Start <= t) { hit = mid; lo = mid + 1; }
			else hi = mid - 1;
		}
		if (hit < 0) return null;
		var c = Cues[hit];
		if (t >= c.End) return null;
		return c;
	}
}
#endregion

// ==================== 解析 ====================
// 编码嗅探顺序固定：BOM → 严格 UTF-8（非法字节抛异常）→ GB18030。老片源多为 GBK。
#region
static class TimedTextParser {
	static readonly Regex srtnum = new(@"^\d+$", RegexOptions.Compiled);
	static readonly Regex timedline = new(
		@"^\s*(\d{1,2}):(\d{1,2}):(\d{1,2})[,.，。](\d{1,3})\s*-->\s*(\d{1,2}):(\d{1,2}):(\d{1,2})[,.，。](\d{1,3})",
		RegexOptions.Compiled);
	static readonly Regex timedline2 = new(
		@"^\s*(\d{1,2}):(\d{1,2})[.,](\d{1,3})\s*-->\s*(\d{1,2}):(\d{1,2})[.,](\d{1,3})",
		RegexOptions.Compiled);
	static readonly Regex lrctime = new(
		@"[\[［](\d{1,3}):(\d{1,2})(?:[.:：](\d{1,3}))?[\]］]",
		RegexOptions.Compiled);
	static readonly Regex lrcword = new(
		@"<(\d{1,3}):(\d{1,2})(?:[.:：](\d{1,3}))?>",
		RegexOptions.Compiled);
	static readonly Regex lrctag = new(
		@"^[\[［](\w+):(.*)[\]］]$", RegexOptions.Compiled);
	static readonly Regex asstag = new(@"\{[^}]*\}", RegexOptions.Compiled);
	static readonly Regex assk = new(@"\\k[fo]?(\d+)", RegexOptions.Compiled);
	static readonly Regex vtttag = new(@"<[^>]+>", RegexOptions.Compiled);

	/// <summary>按扩展名解析；不认识返回 null。</summary>
	public static TimedTextFile Parse(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
		string text;
		string enc;
		if (!TryReadText(path, out text, out enc)) return null;
		var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
		TimedTextFile f = ext switch {
			".lrc" => parselrc(text),
			".ass" or ".ssa" => parseass(text),
			".vtt" => parsevtt(text),
			".srt" => parsesrt(text),
			_ => parsesrt(text) ?? parsevtt(text) ?? parselrc(text),
		};
		if (f == null) return null;
		f.Encoding = enc;
		f.Path = path;
		f.IsLyric = ext == ".lrc";
		f.Cues.Sort((a, b) => a.Start.CompareTo(b.Start));
		fillends(f);
		computehasword(f);
		return f;
	}

	public static bool TryReadText(string path, out string text, out string enc) {
		text = null;
		enc = "";
		try {
			var bytes = File.ReadAllBytes(path);
			if (bytes.Length == 0) {
				text = "";
				enc = "empty";
				return true;
			}
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
				text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
				enc = "utf-8 bom";
				return true;
			}
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) {
				text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
				enc = "utf-16 le";
				return true;
			}
			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) {
				text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
				enc = "utf-16 be";
				return true;
			}
			try {
				var strict = new UTF8Encoding(false, true);
				text = strict.GetString(bytes);
				enc = "utf-8";
				return true;
			}
			catch (DecoderFallbackException) { }
			try {
				var gb = Encoding.GetEncoding(54936); // GB18030
				text = gb.GetString(bytes);
				enc = "gb18030";
				return true;
			}
			catch {
				text = Encoding.UTF8.GetString(bytes);
				enc = "utf-8 lossy";
				return true;
			}
		}
		catch {
			return false;
		}
	}

	/// <summary>无结束时间时按下一条补；最后一条兜底。</summary>
	static void fillends(TimedTextFile f) {
		for (var i = 0; i < f.Cues.Count; i++) {
			var c = f.Cues[i];
			if (c.End > c.Start) continue;
			var next = i + 1 < f.Cues.Count ? f.Cues[i + 1].Start : 0;
			if (next > c.Start) c.End = next;
			else c.End = c.Start + (f.IsLyric ? 8.0 : 3.0);
		}
		// 重叠的两条截断，避免同时显示
		for (var i = 0; i + 1 < f.Cues.Count; i++) {
			var a = f.Cues[i];
			var b = f.Cues[i + 1];
			if (a.End > b.Start) a.End = b.Start;
		}
	}

	static void computehasword(TimedTextFile f) {
		f.HasWordTime = f.Cues.Count > 0;
		foreach (var c in f.Cues) {
			if (c.Words == null || c.Words.Count == 0) { f.HasWordTime = false; return; }
		}
	}

	// ==================== SRT ====================
	static TimedTextFile parsesrt(string text) {
		if (text == null || text.IndexOf("-->", StringComparison.Ordinal) < 0) return null;
		var f = new TimedTextFile();
		var lines = splitlines(text);
		var i = 0;
		while (i < lines.Count) {
			var ln = lines[i].Trim();
			if (ln.Length == 0) { i++; continue; }
			if (srtnum.IsMatch(ln)) { i++; continue; }
			if (!tryrange(ln, out var s, out var e)) { i++; continue; }
			i++;
			var sb = new StringBuilder();
			while (i < lines.Count && lines[i].Trim().Length > 0) {
				if (sb.Length > 0) sb.Append('\n');
				sb.Append(lines[i].TrimEnd());
				i++;
			}
			var body = sb.ToString();
			if (body.Length == 0) continue;
			var cue = new TimedTextCue { Start = s, End = e, Text = body };
			splitspk(cue);
			f.Cues.Add(cue);
		}
		return f.Cues.Count > 0 ? f : null;
	}

	static bool tryrange(string line, out double s, out double e) {
		s = e = 0;
		var m = timedline.Match(line);
		if (m.Success) {
			s = tosec(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
			e = tosec(m.Groups[5].Value, m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value);
			return e > s || e > 0;
		}
		var m2 = timedline2.Match(line);
		if (m2.Success) {
			s = tosec("0", m2.Groups[1].Value, m2.Groups[2].Value, m2.Groups[3].Value);
			e = tosec("0", m2.Groups[4].Value, m2.Groups[5].Value, m2.Groups[6].Value);
			return e > s || e > 0;
		}
		return false;
	}

	// ==================== WebVTT ====================
	static TimedTextFile parsevtt(string text) {
		if (text == null) return null;
		if (!text.TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)) return null;
		var f = new TimedTextFile();
		var lines = splitlines(text);
		var i = 0;
		while (i < lines.Count) {
			var ln = lines[i].Trim();
			if (ln.Length == 0) { i++; continue; }
			if (ln.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
			if (ln.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) ||
				ln.StartsWith("STYLE", StringComparison.OrdinalIgnoreCase) ||
				ln.StartsWith("REGION", StringComparison.OrdinalIgnoreCase)) {
				while (i < lines.Count && lines[i].Trim().Length > 0) i++;
				continue;
			}
			if (!tryrange(ln, out var s, out var e)) { i++; continue; }
			i++;
			var sb = new StringBuilder();
			while (i < lines.Count && lines[i].Trim().Length > 0) {
				var body = vtttag.Replace(lines[i], "");
				if (sb.Length > 0) sb.Append('\n');
				sb.Append(body.TrimEnd());
				i++;
			}
			var t = System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
			if (t.Length == 0) continue;
			var cue = new TimedTextCue { Start = s, End = e, Text = t };
			splitspk(cue);
			f.Cues.Add(cue);
		}
		return f.Cues.Count > 0 ? f : null;
	}

	// ==================== LRC ====================
	static TimedTextFile parselrc(string text) {
		if (text == null) return null;
		var f = new TimedTextFile { IsLyric = true };
		var lines = splitlines(text);
		foreach (var raw in lines) {
			var line = raw.Trim();
			if (line.Length == 0) continue;
			var tag = lrctag.Match(line);
			if (tag.Success) {
				var key = tag.Groups[1].Value.ToLowerInvariant();
				var val = tag.Groups[2].Value.Trim();
				if (key == "ti") f.Title = val;
				else if (key == "ar") f.Artist = val;
				else if (key == "offset" && int.TryParse(val, out var off))
					f.OffsetSec += off / 1000.0;
				continue;
			}
			var stamps = new List<double>();
			var m = lrctime.Match(line);
			var first = m;
			while (m.Success) {
				stamps.Add(tosec("0", m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
				m = m.NextMatch();
			}
			if (stamps.Count == 0) continue;
			// 去掉全部时间戳，剩下正文
			var body = lrctime.Replace(line, "");
			List<WordTime> words = null;
			var wm = lrcword.Match(body);
			if (wm.Success) {
				words = new List<WordTime>();
				var clean = new StringBuilder();
				var pos = 0;
				var mm = wm;
				while (mm.Success) {
					var pre = body.Substring(pos, mm.Index - pos);
					clean.Append(pre);
					var ws = tosec("0", mm.Groups[1].Value, mm.Groups[2].Value, mm.Groups[3].Value);
					// 词的正文到下一个时间戳为止
					var nextstart = mm.Index + mm.Length;
					var nextmm = mm.NextMatch();
					var nextend = nextmm.Success ? nextmm.Index : body.Length;
					var wtxt = body.Substring(nextstart, nextend - nextstart);
					words.Add(new WordTime { CharStart = clean.Length, CharLen = wtxt.Length, Start = ws });
					clean.Append(wtxt);
					pos = nextend;
					mm = nextmm;
				}
				if (pos < body.Length) clean.Append(body.Substring(pos));
				body = clean.ToString();
			}
			body = body.TrimEnd('\r', '\n');
			foreach (var st in stamps) {
				var cue = new TimedTextCue { Start = st, Text = body };
				if (words != null) {
					cue.Words = new List<WordTime>();
					foreach (var w in words)
						cue.Words.Add(new WordTime { CharStart = w.CharStart, CharLen = w.CharLen, Start = w.Start });
				}
				f.Cues.Add(cue);
			}
		}
		if (f.Cues.Count == 0) return null;
		f.Cues.Sort((a, b) => a.Start.CompareTo(b.Start));
		return f;
	}

	// ==================== ASS / SSA ====================
	static TimedTextFile parseass(string text) {
		if (text == null) return null;
		if (text.IndexOf("[Events]", StringComparison.OrdinalIgnoreCase) < 0) return null;
		var f = new TimedTextFile();
		var lines = splitlines(text);
		var inevents = false;
		string[] fmt = null;
		foreach (var raw in lines) {
			var line = raw.Trim();
			if (line.Length == 0) continue;
			if (line.StartsWith("[", StringComparison.Ordinal)) {
				inevents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
				continue;
			}
			if (!inevents) continue;
			if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase)) {
				var cols = line.Substring(7).Split(',');
				fmt = new string[cols.Length];
				for (var i = 0; i < cols.Length; i++) fmt[i] = cols[i].Trim().ToLowerInvariant();
				continue;
			}
			if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;
			var fieldstr = line.Substring(9);
			var fields = splitass(fieldstr, fmt == null ? 10 : fmt.Length);
			if (fields.Length < 2) continue;
			int istart = -1, iend = -1, itext = fields.Length - 1, iname = -1;
			if (fmt != null) {
				for (var i = 0; i < fmt.Length; i++) {
					if (fmt[i] == "start") istart = i;
					else if (fmt[i] == "end") iend = i;
					else if (fmt[i] == "text") itext = i;
					else if (fmt[i] == "name") iname = i;
				}
			}
			if (istart < 0) istart = 1;
			if (iend < 0) iend = 2;
			if (istart >= fields.Length || iend >= fields.Length || itext >= fields.Length) continue;
			if (!asstime(fields[istart], out var s) || !asstime(fields[iend], out var e)) continue;
			var body = fields[itext];
			var words = new List<WordTime>();
			var clean = new StringBuilder();
			var tcur = s;
			var pos = 0;
			var m = assk.Match(body);
			var hask = m.Success;
			while (m.Success) {
				var pre = asstag.Replace(body.Substring(pos, m.Index - pos), "");
				clean.Append(pre);
				var cs = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
				var wtxt = "";
				var nextstart = m.Index + m.Length;
				var nextmm = m.NextMatch();
				var nextend = nextmm.Success ? nextmm.Index : body.Length;
				wtxt = asstag.Replace(body.Substring(nextstart, nextend - nextstart), "");
				if (wtxt.Length > 0) {
					words.Add(new WordTime { CharStart = clean.Length, CharLen = wtxt.Length, Start = tcur });
					clean.Append(wtxt);
					tcur += cs / 100.0;
				}
				else tcur += cs / 100.0;
				pos = nextend;
				m = nextmm;
			}
			if (pos < body.Length) clean.Append(asstag.Replace(body.Substring(pos), ""));
			var text2 = clean.ToString().Replace("\\N", "\n").Replace("\\n", "\n").Trim();
			if (text2.Length == 0) continue;
			var cue = new TimedTextCue { Start = s, End = e, Text = text2 };
			if (iname >= 0 && iname < fields.Length) cue.Speaker = fields[iname].Trim();
			if (hask && words.Count > 0) cue.Words = words;
			f.Cues.Add(cue);
		}
		return f.Cues.Count > 0 ? f : null;
	}

	static string[] splitass(string s, int max) {
		var list = new List<string>();
		var sb = new StringBuilder();
		var n = 0;
		foreach (var ch in s) {
			if (ch == ',' && n < max - 1) {
				list.Add(sb.ToString());
				sb.Clear();
				n++;
			}
			else sb.Append(ch);
		}
		list.Add(sb.ToString());
		return list.ToArray();
	}

	static bool asstime(string s, out double sec) {
		sec = 0;
		var parts = s.Trim().Split(':');
		if (parts.Length < 3) return false;
		if (!int.TryParse(parts[0], out var h)) return false;
		if (!int.TryParse(parts[1], out var mi)) return false;
		if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var se)) return false;
		sec = h * 3600 + mi * 60 + se;
		return true;
	}

	// ==================== 公共 ====================
	static List<string> splitlines(string text) {
		var list = new List<string>();
		if (text == null) return list;
		var norm = text.Replace("\r\n", "\n").Replace('\r', '\n');
		foreach (var l in norm.Split('\n')) list.Add(l);
		return list;
	}

	static double tosec(string h, string m, string s, string frac) {
		var hh = h.Length > 0 ? int.Parse(h, CultureInfo.InvariantCulture) : 0;
		var mm = m.Length > 0 ? int.Parse(m, CultureInfo.InvariantCulture) : 0;
		var ss = s.Length > 0 ? int.Parse(s, CultureInfo.InvariantCulture) : 0;
		var f = 0.0;
		if (frac != null && frac.Length > 0) {
			var val = int.Parse(frac, CultureInfo.InvariantCulture);
			f = frac.Length switch {
				1 => val / 10.0,
				2 => val / 100.0,
				_ => val / 1000.0,
			};
		}
		return hh * 3600 + mm * 60 + ss + f;
	}

	/// <summary>拆出 "名字: 台词" 的说话人，SRT/VTT 常见。</summary>
	static void splitspk(TimedTextCue cue) {
		var t = cue.Text;
		var idx = t.IndexOf(':');
		if (idx <= 0 || idx > 20) return;
		var name = t.Substring(0, idx).Trim();
		if (name.Length == 0) return;
		foreach (var ch in name)
			if (ch == '\n' || ch == '：' || char.IsDigit(ch)) return;
		cue.Speaker = name;
		cue.Text = t.Substring(idx + 1).Trim();
	}
}
#endregion
