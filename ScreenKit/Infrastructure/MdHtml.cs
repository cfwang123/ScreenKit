using System.Text;

namespace ScreenKit;

/// <summary>
/// Markdown 转 HTML 片段。WPF MD 阅读器、/api/md、网页小说阅读器共用这一份解析，
/// 渲染规则只在这里改；页面外壳、主题配色仍在 <see cref="MdRead"/>。
/// </summary>
static class MdHtml {
	public sealed class Opt {
		/// <summary>相对地址的基准目录；空表示不解析相对图片/链接，http(s) 与页内锚点照原样保留。</summary>
		public string BaseDir = "";
		/// <summary>每句套一层 span.rs，网页小说阅读器按句朗读和高亮用。</summary>
		public bool Sentence;
		/// <summary>标题 id 按出现顺序写成 rdc0、rdc1…，并把前三级标题收进 <see cref="Result.Chapters"/>。</summary>
		public bool Chapter;
	}

	public sealed class Result {
		public string Html = "";
		public List<(string Id, string Text)> Chapters = new();
	}

	sealed class Ctx {
		public Opt Opt;
		public readonly StringBuilder Sb = new();
		public readonly List<(string Id, string Text)> Chap = new();
		public int Seq;
	}

	public static Result Render(string md, Opt opt) {
		opt ??= new();
		var c = new Ctx { Opt = opt };
		block(c, md ?? "", 0);
		return new Result { Html = c.Sb.ToString(), Chapters = c.Chap };
	}

	// ==================== 块 ====================
	#region
	static void block(Ctx c, string md, int depth) {
		var lines = (md ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		var i = 0;
		if (lines.Length > 0 && lines[0].Trim() == "---") {
			var j = 1;
			while (j < lines.Length && j < 80 && lines[j].Trim() != "---") j++;
			if (j < lines.Length && lines[j].Trim() == "---") i = j + 1;
		}
		while (i < lines.Length) {
			if (string.IsNullOrWhiteSpace(lines[i])) { i++; continue; }
			var line = lines[i];
			if (fenceopen(line, out var mk)) { i = addcode(c, lines, i, mk); continue; }
			if (isheading(line, out var level, out var title)) { heading(c, level, title); i++; continue; }
			if (i + 1 < lines.Length && setext(lines[i + 1], out level) && !blockstart(line)) {
				heading(c, level, line.Trim());
				i += 2;
				continue;
			}
			if (ishr(line)) { c.Sb.Append("<hr>"); i++; continue; }
			if (istable(lines, i)) { i = addtable(c, lines, i); continue; }
			if (line.TrimStart().StartsWith(">")) { i = addquote(c, lines, i, depth); continue; }
			if (listline(line, out _, out _, out _)) { i = addlist(c, lines, i); continue; }
			i = addpara(c, lines, i);
		}
	}

	static int addcode(Ctx c, string[] lines, int i, string mk) {
		var code = new StringBuilder();
		i++;
		while (i < lines.Length && !fenceclose(lines[i], mk)) {
			if (code.Length > 0) code.Append('\n');
			code.Append(lines[i]);
			i++;
		}
		if (i < lines.Length) i++;
		c.Sb.Append("<pre><code>").Append(esc(code.ToString())).Append("</code></pre>");
		return i;
	}

	static void heading(Ctx c, int level, string title) {
		if (level < 1) level = 1;
		if (level > 6) level = 6;
		var id = "";
		if (c.Opt.Chapter) {
			c.Seq++;
			id = "rdc" + (c.Seq - 1);
		}
		else id = slug(title);
		c.Sb.Append("<h").Append(level);
		if (id.Length > 0) c.Sb.Append(" id=\"").Append(esc(id)).Append('"');
		c.Sb.Append('>');
		sentences(c, title, 0);
		c.Sb.Append("</h").Append(level).Append('>');
		if (c.Opt.Chapter && level <= 3) c.Chap.Add((id, title));
	}

	static int addquote(Ctx c, string[] lines, int i, int depth) {
		var inner = new StringBuilder();
		while (i < lines.Length && lines[i].TrimStart().StartsWith(">")) {
			var s = lines[i].TrimStart();
			s = s.Substring(1);
			if (s.StartsWith(" ")) s = s.Substring(1);
			inner.Append(s).Append('\n');
			i++;
		}
		c.Sb.Append("<blockquote>");
		block(c, inner.ToString(), depth + 1);
		c.Sb.Append("</blockquote>");
		return i;
	}

	static int addlist(Ctx c, string[] lines, int i) {
		if (i >= lines.Length || !listline(lines[i], out var ind, out var ordered, out _)) return i;
		var start = i;
		c.Sb.Append(ordered ? "<ol>" : "<ul>");
		while (i < lines.Length) {
			if (string.IsNullOrWhiteSpace(lines[i])) {
				var k = i + 1;
				while (k < lines.Length && string.IsNullOrWhiteSpace(lines[k])) k++;
				if (k < lines.Length && listline(lines[k], out var ind2, out _, out _) && ind2 >= ind) {
					i = k;
					continue;
				}
				break;
			}
			if (!listline(lines[i], out var cur, out var ord, out var item)) break;
			if (cur != ind) break;
			if (ord != ordered) break;
			c.Sb.Append("<li>");
			if (!ordered && item.Length >= 4 && item[0] == '[' && item[2] == ']' && item[3] == ' '
				&& (item[1] == ' ' || item[1] == 'x' || item[1] == 'X')) {
				c.Sb.Append(item[1] == ' '
					? "<input type=\"checkbox\" disabled> "
					: "<input type=\"checkbox\" disabled checked> ");
				item = item.Substring(4);
			}
			sentences(c, item.TrimEnd(), 0);
			var n = i + 1;
			if (n < lines.Length && listline(lines[n], out var nind, out _, out _) && nind > cur)
				n = addlist(c, lines, n);
			else {
				while (n < lines.Length && !string.IsNullOrWhiteSpace(lines[n]) && !listline(lines[n], out _, out _, out _)
					&& !blockstart(lines[n]) && indentof(lines[n]) > cur) {
					c.Sb.Append("<br>");
					sentences(c, lines[n].Trim(), 0);
					n++;
				}
			}
			c.Sb.Append("</li>");
			i = n;
			if (i == start) break;
		}
		c.Sb.Append(ordered ? "</ol>" : "</ul>");
		if (i == start) i++;
		return i;
	}

	static int addtable(Ctx c, string[] lines, int i) {
		var head = splitrow(lines[i]);
		var sep = splitrow(lines[i + 1]);
		c.Sb.Append("<div class=\"tbl\"><table><thead><tr>");
		for (var k = 0; k < head.Count; k++) {
			var al = k < sep.Count ? alignof(sep[k]) : "";
			c.Sb.Append("<th");
			if (al.Length > 0) c.Sb.Append(" style=\"text-align:").Append(al).Append('"');
			c.Sb.Append('>');
			sentences(c, head[k], 0);
			c.Sb.Append("</th>");
		}
		c.Sb.Append("</tr></thead><tbody>");
		i += 2;
		while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].IndexOf('|') >= 0) {
			var cells = splitrow(lines[i]);
			c.Sb.Append("<tr>");
			for (var k = 0; k < head.Count; k++) {
				var al = k < sep.Count ? alignof(sep[k]) : "";
				c.Sb.Append("<td");
				if (al.Length > 0) c.Sb.Append(" style=\"text-align:").Append(al).Append('"');
				c.Sb.Append('>');
				if (k < cells.Count) sentences(c, cells[k], 0);
				c.Sb.Append("</td>");
			}
			c.Sb.Append("</tr>");
			i++;
		}
		c.Sb.Append("</tbody></table></div>");
		return i;
	}

	static int addpara(Ctx c, string[] lines, int i) {
		var buf = new List<string>();
		var brs = new List<bool>();
		while (i < lines.Length && !stoppara(lines, i)) {
			var raw = lines[i];
			var br = raw.EndsWith("  ") || raw.EndsWith("\\");
			if (br) {
				var cut = raw.EndsWith("\\") ? 1 : 2;
				raw = raw.Substring(0, raw.Length - cut);
			}
			buf.Add(raw.Trim());
			brs.Add(br);
			i++;
		}
		if (buf.Count == 0) return i + 1;
		c.Sb.Append("<p>");
		for (var k = 0; k < buf.Count; k++) {
			sentences(c, buf[k], 0);
			if (k + 1 < buf.Count) c.Sb.Append(brs[k] ? "<br>" : " ");
		}
		c.Sb.Append("</p>");
		return i;
	}

	static bool stoppara(string[] lines, int i) {
		if (i >= lines.Length || string.IsNullOrWhiteSpace(lines[i])) return true;
		if (blockstart(lines[i])) return true;
		if (istable(lines, i)) return true;
		if (i + 1 < lines.Length && setext(lines[i + 1], out _)) return true;
		return false;
	}

	static bool blockstart(string line) {
		if (string.IsNullOrWhiteSpace(line)) return true;
		if (fenceopen(line, out _)) return true;
		if (isheading(line, out _, out _)) return true;
		if (ishr(line)) return true;
		if (line.TrimStart().StartsWith(">")) return true;
		if (listline(line, out _, out _, out _)) return true;
		return false;
	}

	static bool fenceopen(string line, out string mk) {
		var t = line.Trim();
		mk = "";
		if (t.StartsWith("```")) mk = "```";
		else if (t.StartsWith("~~~")) mk = "~~~";
		return mk.Length > 0;
	}

	static bool fenceclose(string line, string mk) {
		var t = line.Trim();
		return t.StartsWith(mk) && t.Trim('`', '~', ' ', '\t').Length == 0;
	}

	static bool isheading(string line, out int level, out string title) {
		level = 0;
		title = "";
		var t = line.Trim();
		while (level < t.Length && level < 6 && t[level] == '#') level++;
		if (level == 0) return false;
		if (level < t.Length && t[level] != ' ' && t[level] != '\t') { level = 0; return false; }
		title = t.Substring(level).Trim();
		while (title.EndsWith("#")) title = title.Substring(0, title.Length - 1).TrimEnd();
		return true;
	}

	static bool setext(string line, out int level) {
		var t = line.Trim();
		level = 0;
		if (t.Length < 3) return false;
		if (t.Trim('=').Length == 0) { level = 1; return true; }
		if (t.IndexOf('|') < 0 && t.Trim('-').Length == 0) { level = 2; return true; }
		return false;
	}

	static bool ishr(string line) {
		var t = line.Trim();
		if (t.Length < 3 || t.IndexOf('|') >= 0) return false;
		var ch = t[0];
		if (ch != '-' && ch != '*' && ch != '_') return false;
		var n = 0;
		foreach (var c in t) {
			if (c == ch) { n++; continue; }
			if (c != ' ' && c != '\t') return false;
		}
		return n >= 3;
	}

	static bool istable(string[] lines, int i) {
		if (i + 1 >= lines.Length) return false;
		if (lines[i].IndexOf('|') < 0) return false;
		return tablesep(lines[i + 1]);
	}

	static bool tablesep(string line) {
		if (line.IndexOf('-') < 0) return false;
		var cells = splitrow(line);
		if (cells.Count == 0) return false;
		foreach (var cell in cells) {
			var t = cell.Trim();
			if (t.Length == 0) return false;
			foreach (var ch in t) {
				if (ch != '-' && ch != ':' && ch != ' ' && ch != '\t') return false;
			}
			if (t.IndexOf('-') < 0) return false;
		}
		return true;
	}

	static List<string> splitrow(string line) {
		var s = line.Trim();
		if (s.StartsWith("|")) s = s.Substring(1);
		if (s.EndsWith("|")) s = s.Substring(0, s.Length - 1);
		var list = new List<string>();
		foreach (var part in s.Split('|')) list.Add(part.Trim());
		return list;
	}

	static string alignof(string cell) {
		var c = cell.Trim();
		var left = c.StartsWith(":");
		var right = c.EndsWith(":");
		if (left && right) return "center";
		if (right) return "right";
		if (left) return "left";
		return "";
	}

	static bool listline(string line, out int indent, out bool ordered, out string item) {
		var n = skipindent(line, out indent);
		ordered = false;
		item = "";
		var s = n < line.Length ? line.Substring(n) : "";
		if (s.StartsWith("- ") || s.StartsWith("* ") || s.StartsWith("+ ") || s.StartsWith("● ") || s.StartsWith("• ")) {
			item = s.Substring(2);
			return true;
		}
		var j = 0;
		while (j < s.Length && char.IsDigit(s[j])) j++;
		if (j > 0 && j < s.Length && (s[j] == '.' || s[j] == ')') && j + 1 < s.Length && s[j + 1] == ' ') {
			ordered = true;
			item = s.Substring(j + 2);
			return true;
		}
		return false;
	}

	static int indentof(string line) {
		skipindent(line, out var col);
		return col;
	}

	static int skipindent(string line, out int col) {
		col = 0;
		var n = 0;
		while (n < line.Length) {
			var ch = line[n];
			if (ch == ' ') { col++; n++; continue; }
			if (ch == '\t') {
				col += 4 - (col % 4);
				n++;
				continue;
			}
			break;
		}
		return n;
	}

	static string slug(string title) {
		var sb = new StringBuilder();
		foreach (var c in title ?? "") {
			if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 127) sb.Append(c);
			else if (char.IsWhiteSpace(c) && sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
		}
		return sb.ToString().Trim('-');
	}
	#endregion

	// ==================== 行内 ====================
	#region
	/// <summary>按句切分后逐句渲染，朗读和高亮都靠这层 span。</summary>
	static void sentences(Ctx c, string text, int depth) {
		if (!c.Opt.Sentence) { inline(c, text, depth); return; }
		foreach (var one in splitsents(text)) {
			c.Sb.Append("<span class=\"rs\">");
			inline(c, one, depth);
			c.Sb.Append("</span>");
		}
	}

	static void inline(Ctx c, string text, int depth) {
		if (depth > 8) { c.Sb.Append(esc(text)); return; }
		var i = 0;
		while (i < text.Length) {
			var ch = text[i];
			if (ch == '\\' && i + 1 < text.Length) {
				c.Sb.Append(esc(text[i + 1].ToString()));
				i += 2;
				continue;
			}
			if (ch == '`') {
				var end = text.IndexOf('`', i + 1);
				if (end > i) {
					c.Sb.Append("<code>").Append(esc(text.Substring(i + 1, end - i - 1))).Append("</code>");
					i = end + 1;
					continue;
				}
			}
			if (ch == '!' && i + 1 < text.Length && text[i + 1] == '['
				&& trylink(text, i + 1, out var alt, out var src, out var nxt)) {
				var href = safehref(src, c.Opt.BaseDir);
				if (href.Length > 0)
					c.Sb.Append("<img alt=\"").Append(esc(alt)).Append("\" src=\"").Append(esc(href)).Append("\">");
				else
					c.Sb.Append(esc(alt));
				i = nxt;
				continue;
			}
			if (ch == '[' && trylink(text, i, out var lab, out var url, out var next)) {
				var href = safehref(url, c.Opt.BaseDir);
				if (href.Length == 0) inline(c, lab, depth + 1);
				else {
					c.Sb.Append("<a href=\"").Append(esc(href)).Append("\">");
					inline(c, lab, depth + 1);
					c.Sb.Append("</a>");
				}
				i = next;
				continue;
			}
			if (ch == '<' && tryangle(text, i, out var ang, out var angEnd)) {
				var href = safehref(ang, c.Opt.BaseDir);
				if (href.Length > 0) {
					c.Sb.Append("<a href=\"").Append(esc(href)).Append("\">").Append(esc(ang)).Append("</a>");
					i = angEnd;
					continue;
				}
			}
			if (trymark(text, i, "**", out var mend) || trymark(text, i, "__", out mend)) {
				c.Sb.Append("<strong>");
				inline(c, text.Substring(i + 2, mend - (i + 2)), depth + 1);
				c.Sb.Append("</strong>");
				i = mend + 2;
				continue;
			}
			if (trymark(text, i, "~~", out mend)) {
				c.Sb.Append("<del>");
				inline(c, text.Substring(i + 2, mend - (i + 2)), depth + 1);
				c.Sb.Append("</del>");
				i = mend + 2;
				continue;
			}
			if ((ch == '*' || ch == '_') && trymark(text, i, ch.ToString(), out mend)) {
				c.Sb.Append("<em>");
				inline(c, text.Substring(i + 1, mend - (i + 1)), depth + 1);
				c.Sb.Append("</em>");
				i = mend + 1;
				continue;
			}
			if (tryurl(text, i, out var bare, out var bareEnd)) {
				var href = safehref(bare, c.Opt.BaseDir);
				if (href.Length > 0) {
					c.Sb.Append("<a href=\"").Append(esc(href)).Append("\">").Append(esc(bare)).Append("</a>");
					i = bareEnd;
					continue;
				}
			}
			c.Sb.Append(esc(ch.ToString()));
			i++;
		}
	}

	/// <summary>与网页小说阅读器原来的切句规则一致：中文句末标点、分号、句点后接空白。</summary>
	static List<string> splitsents(string text) {
		const string closers = "」』”’\"')）]】";
		var list = new List<string>();
		var buf = new StringBuilder();
		text ??= "";
		for (var i = 0; i < text.Length; i++) {
			var ch = text[i];
			buf.Append(ch);
			var end = "。！？!?…".IndexOf(ch) >= 0;
			if (!end && (ch == ';' || ch == '；')) end = true;
			if (!end && ch == '.' && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))) end = true;
			if (!end) continue;
			while (i + 1 < text.Length && closers.IndexOf(text[i + 1]) >= 0) buf.Append(text[++i]);
			if (!string.IsNullOrWhiteSpace(buf.ToString())) list.Add(buf.ToString());
			buf.Clear();
		}
		if (!string.IsNullOrWhiteSpace(buf.ToString())) list.Add(buf.ToString());
		return list;
	}

	static bool trylink(string text, int i, out string label, out string dest, out int next) {
		label = "";
		dest = "";
		next = i;
		if (i >= text.Length || text[i] != '[') return false;
		var rb = text.IndexOf(']', i + 1);
		if (rb < 0 || rb + 1 >= text.Length || text[rb + 1] != '(') return false;
		var close = text.IndexOf(')', rb + 2);
		if (close < 0) return false;
		label = text.Substring(i + 1, rb - i - 1);
		dest = splitdest(text.Substring(rb + 2, close - (rb + 2)));
		next = close + 1;
		return true;
	}

	static string splitdest(string raw) {
		raw = (raw ?? "").Trim();
		if (raw.StartsWith("<")) {
			var gt = raw.IndexOf('>');
			if (gt > 0) return raw.Substring(1, gt - 1).Trim();
		}
		if (raw.Length > 2 && (raw.EndsWith("\"") || raw.EndsWith("'"))) {
			var q = raw[raw.Length - 1];
			var a = raw.LastIndexOf(q, raw.Length - 2);
			if (a > 0 && raw[a - 1] == ' ') raw = raw.Substring(0, a).TrimEnd();
		}
		return raw.Trim();
	}

	static bool tryangle(string text, int i, out string url, out int next) {
		url = "";
		next = i;
		var j = text.IndexOf('>', i + 1);
		if (j < 0) return false;
		var inner = text.Substring(i + 1, j - i - 1).Trim();
		if (!inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			&& !inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			&& !inner.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
			return false;
		url = inner;
		next = j + 1;
		return true;
	}

	static bool tryurl(string text, int i, out string url, out int next) {
		url = "";
		next = i;
		if (!at(text, i, "http://") && !at(text, i, "https://")) return false;
		var j = i;
		while (j < text.Length && !char.IsWhiteSpace(text[j]) && text[j] != '<' && text[j] != '"' && text[j] != ')')
			j++;
		while (j > i && (text[j - 1] == '.' || text[j - 1] == ',' || text[j - 1] == ';' || text[j - 1] == ':' || text[j - 1] == '!'))
			j--;
		if (j <= i) return false;
		url = text.Substring(i, j - i);
		next = j;
		return true;
	}

	static bool trymark(string text, int i, string mark, out int end) {
		end = -1;
		if (i + mark.Length > text.Length) return false;
		if (!at(text, i, mark)) return false;
		if (mark[0] == '_' && i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')) return false;
		var n = i + mark.Length;
		if (n >= text.Length || char.IsWhiteSpace(text[n])) return false;
		var j = text.IndexOf(mark, n, StringComparison.Ordinal);
		if (j <= n) return false;
		if (char.IsWhiteSpace(text[j - 1])) return false;
		if (mark[0] == '_' && j + mark.Length < text.Length && char.IsLetterOrDigit(text[j + mark.Length])) return false;
		end = j;
		return true;
	}

	static bool at(string text, int i, string s) {
		if (i < 0 || s.Length == 0 || i + s.Length > text.Length) return false;
		return string.CompareOrdinal(text, i, s, 0, s.Length) == 0;
	}
	#endregion

	// ==================== 地址与转义 ====================
	#region
	public static string Esc(string s) => esc(s);

	static string esc(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
	}

	/// <summary>Markdown 里的相对路径常按 URL 编码写（%20、%E5），先还原成文件路径，再由 fileurl 重新拼地址。</summary>
	static string decodeurl(string s) {
		if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s;
		try { return Uri.UnescapeDataString(s); }
		catch { return s; }
	}

	/// <summary>
	/// 本地路径转 file:// 地址。IE 把 %E5 这类 UTF-8 转义当 Latin-1 解码（变成 å¾），
	/// 中文、空格等只能原样写进 URL，只转义会弄坏地址的 ASCII 符号。
	/// </summary>
	public static string FileUrl(string full) {
		if (string.IsNullOrEmpty(full)) return "";
		var p = full.Replace('\\', '/');
		var sb = new StringBuilder(p.Length + 16);
		sb.Append(p.StartsWith("//", StringComparison.Ordinal) ? "file:" : "file:///");
		foreach (var ch in p) {
			if (ch == '%' || ch == '#' || ch == '?' || ch == '&' || ch == '"' || ch == '<' || ch == '>' || ch == '{' || ch == '}')
				sb.Append('%').Append(((int)ch).ToString("X2"));
			else if (ch == ' ') sb.Append("%20");
			else sb.Append(ch);
		}
		return sb.ToString();
	}

	static string safehref(string url, string baseDir) {
		url = (url ?? "").Trim();
		if (url.Length == 0) return "";
		if (url.StartsWith("#")) return url;
		if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return "";
		if (url.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)) return "";
		if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "";
		if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			|| url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
			|| url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
			return url;
		try {
			if (url.Length >= 3 && char.IsLetter(url[0]) && url[1] == ':')
				return FileUrl(decodeurl(url));
			if (string.IsNullOrEmpty(baseDir)) return "";
			var full = Path.GetFullPath(Path.Combine(baseDir, decodeurl(url).Replace('/', Path.DirectorySeparatorChar)));
			return FileUrl(full);
		}
		catch { return ""; }
	}
	#endregion
}
