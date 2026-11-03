using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenKit.DocView;

/// <summary>Markdown 块类型。</summary>
enum MdBlockKind {
	Paragraph,
	Heading,
	ListItem,
	Quote,
	Code,
	Hr,
	Table,
	Html,
	/// <summary>HTML &lt;img&gt;（含 style 宽高）。</summary>
	HtmlImg,
	/// <summary>HTML &lt;details&gt;/&lt;summary&gt;（对齐 mdview）。</summary>
	Details,
	Blank,
}

/// <summary>行内片段。</summary>
sealed class MdSpan {
	public string Kind; // text | bold | italic | code | link | image | mark | strike | softbr
	public string Text;
	public string Href; // link/image
}

/// <summary>块级节点。</summary>
sealed class MdBlock {
	public MdBlockKind Kind;
	/// <summary>源文件起始行（0-based）。</summary>
	public int SourceLine0;
	/// <summary>源文件结束行（含，0-based）。</summary>
	public int SourceLine1;
	public int Level; // heading 1-6 / list indent
	public bool Ordered; // list
	/// <summary>GFM 任务列表：null=普通项；true/false=已勾/未勾。</summary>
	public bool? TaskChecked;
	public string Lang; // code fence
	public string Text; // raw for code/hr/html; plain for heading stripped markers; img src
	public List<MdSpan> Spans;
	public List<string[]> TableRows; // table cells raw
	public List<string> TableAlign; // left|center|right
	/// <summary>details：子块（body 再解析）。</summary>
	public List<MdBlock> Children;
	/// <summary>details：summary 纯文本。</summary>
	public string Summary;
	/// <summary>details：是否默认展开（open 属性）。</summary>
	public bool DetailsOpen;
	/// <summary>HtmlImg：显式宽/高（CSS px）；null=未指定。</summary>
	public double? ImgWidthPx;
	public double? ImgHeightPx;
}

/// <summary>解析结果：块列表 + 源行映射。</summary>
sealed class MdDoc {
	public List<MdBlock> Blocks = new();
	/// <summary>源行 0-based → 块索引（首块）。</summary>
	public int[] LineToBlock;
}

/// <summary>
/// Markdown 多级标题自动编号（仅显示，不改源码）。
/// 形如 1 / 1.1 / 1.1.1；降级时清零更深层级。
/// </summary>
sealed class MdHeadingNumber {
	readonly int[] counters = new int[7]; // 1..6

	/// <summary>推进指定层级并返回编号文本（无末尾空格）。</summary>
	public string Next(int level) {
		if (level < 1) level = 1;
		if (level > 6) level = 6;
		counters[level]++;
		for (var i = level + 1; i <= 6; i++)
			counters[i] = 0;
		// 中间层未出现过时补 0，避免「.2」残缺
		for (var i = 1; i < level; i++)
			if (counters[i] <= 0) counters[i] = 1;
		var sb = new StringBuilder(16);
		for (var i = 1; i <= level; i++) {
			if (i > 1) sb.Append('.');
			sb.Append(counters[i]);
		}
		return sb.ToString();
	}

	/// <summary>为标题加「编号 + 空格」前缀；空标题不编号。</summary>
	public string PrefixTitle(int level, string title) {
		title ??= "";
		if (string.IsNullOrWhiteSpace(title)) return title;
		return Next(level) + " " + title;
	}
}

/// <summary>
/// 轻量 GFM 子集解析（参考 mdview 的块/行内结构，纯 C# 无外部依赖）。
/// 支持：标题、段落、列表、引用、围栏代码、HR、GFM 表、行内粗体/斜体/代码/链接/图/删除线/高亮。
/// </summary>
static class MdParser {
	static readonly Regex ReHeading = new(@"^(#{1,6})\s+(.*?)(?:\s+#*\s*)?$", RegexOptions.Compiled);
	static readonly Regex ReFence = new(@"^(`{3,}|~{3,})\s*([^\s`]*)\s*$", RegexOptions.Compiled);
	static readonly Regex ReHr = new(@"^\s{0,3}([-*_])(?:\s*\1){2,}\s*$", RegexOptions.Compiled);
	// 含 ●•○◦：mdview/部分编辑器会把列表符写成 Unicode 圆点（本机日记常见）
	static readonly Regex ReUl = new(@"^(\s*)([*+●•○◦-])\s+(.*)$", RegexOptions.Compiled);
	static readonly Regex ReOl = new(@"^(\s*)(\d{1,9})[.)]\s+(.*)$", RegexOptions.Compiled);
	static readonly Regex ReQuote = new(@"^>\s?(.*)$", RegexOptions.Compiled);
	static readonly Regex ReTableSep = new(@"^\s*\|?(\s*:?-+:?\s*\|)+\s*:?-+:?\s*\|?\s*$", RegexOptions.Compiled);
	static readonly Regex ReTask = new(@"^\[([ xX])\]\s+(.*)$", RegexOptions.Compiled);

	public static MdDoc Parse(string text) =>
		Parse(text, DocViewOptions.Current?.MdTabSize ?? 3);

	/// <param name="tabSize">Tab 列宽（字符），用于列表前导空白折算缩进层级。</param>
	public static MdDoc Parse(string text, int tabSize) {
		if (tabSize < 1) tabSize = 1;
		if (tabSize > 8) tabSize = 8;
		var doc = new MdDoc();
		var lines = SplitLines(text ?? "");
		doc.LineToBlock = new int[Math.Max(1, lines.Count)];
		for (var i = 0; i < doc.LineToBlock.Length; i++)
			doc.LineToBlock[i] = -1;

		var iLine = 0;
		while (iLine < lines.Count) {
			var line = lines[iLine];
			if (string.IsNullOrWhiteSpace(line)) {
				var b = new MdBlock {
					Kind = MdBlockKind.Blank,
					SourceLine0 = iLine,
					SourceLine1 = iLine,
				};
				add(doc, b);
				iLine++;
				continue;
			}

			// fenced code
			var fm = ReFence.Match(line);
			if (fm.Success) {
				var fence = fm.Groups[1].Value;
				var lang = fm.Groups[2].Value ?? "";
				var start = iLine;
				iLine++;
				var sb = new StringBuilder();
				while (iLine < lines.Count) {
					var L = lines[iLine];
					if (L.StartsWith(fence, StringComparison.Ordinal) && L.TrimEnd().Length >= fence.Length
						&& L.Trim().Trim('`', '~').Length == 0) {
						break;
					}
					if (sb.Length > 0) sb.Append('\n');
					sb.Append(L);
					iLine++;
				}
				var end = iLine < lines.Count ? iLine : lines.Count - 1;
				add(doc, new MdBlock {
					Kind = MdBlockKind.Code,
					SourceLine0 = start,
					SourceLine1 = end,
					Lang = lang,
					Text = sb.ToString(),
				});
				if (iLine < lines.Count) iLine++; // close fence
				continue;
			}

			if (ReHr.IsMatch(line)) {
				add(doc, new MdBlock {
					Kind = MdBlockKind.Hr,
					SourceLine0 = iLine,
					SourceLine1 = iLine,
					Text = line.Trim(),
				});
				iLine++;
				continue;
			}

			var hm = ReHeading.Match(line);
			if (hm.Success) {
				var level = hm.Groups[1].Value.Length;
				var body = hm.Groups[2].Value.Trim();
				add(doc, new MdBlock {
					Kind = MdBlockKind.Heading,
					SourceLine0 = iLine,
					SourceLine1 = iLine,
					Level = level,
					Text = body,
					Spans = ParseInlines(body),
				});
				iLine++;
				continue;
			}

			// table: header + sep + rows
			if (line.IndexOf('|') >= 0 && iLine + 1 < lines.Count && ReTableSep.IsMatch(lines[iLine + 1])) {
				var start = iLine;
				var rows = new List<string[]>();
				rows.Add(SplitTableRow(line));
				var align = ParseTableAlign(lines[iLine + 1]);
				iLine += 2;
				while (iLine < lines.Count && lines[iLine].IndexOf('|') >= 0 && !string.IsNullOrWhiteSpace(lines[iLine])) {
					if (ReTableSep.IsMatch(lines[iLine])) { iLine++; continue; }
					rows.Add(SplitTableRow(lines[iLine]));
					iLine++;
				}
				add(doc, new MdBlock {
					Kind = MdBlockKind.Table,
					SourceLine0 = start,
					SourceLine1 = iLine - 1,
					TableRows = rows,
					TableAlign = align,
				});
				continue;
			}

			var qm = ReQuote.Match(line);
			if (qm.Success) {
				var start = iLine;
				var sb = new StringBuilder();
				while (iLine < lines.Count) {
					var m = ReQuote.Match(lines[iLine]);
					if (!m.Success) break;
					if (sb.Length > 0) sb.Append('\n');
					sb.Append(m.Groups[1].Value);
					iLine++;
				}
				var body = sb.ToString();
				add(doc, new MdBlock {
					Kind = MdBlockKind.Quote,
					SourceLine0 = start,
					SourceLine1 = iLine - 1,
					Text = body,
					// 保留源码换行 → softbr（对齐 mdview）
					Spans = ParseInlines(body),
				});
				continue;
			}

			var um = ReUl.Match(line);
			var om = um.Success ? null : ReOl.Match(line);
			if (um.Success || (om != null && om.Success)) {
				var ordered = om != null && om.Success;
				var m = ordered ? om : um;
				var indentWs = m.Groups[1].Value;
				var indent = IndentCols(indentWs, tabSize);
				var start = iLine;
				var body = m.Groups[ordered ? 3 : 3].Value;
				bool? taskChecked = null;
				var tm = ReTask.Match(body);
				if (tm.Success) {
					var ch = tm.Groups[1].Value;
					taskChecked = ch == "x" || ch == "X";
					body = tm.Groups[2].Value;
				}
				// 续行：同缩进且非新列表/空行
				iLine++;
				while (iLine < lines.Count) {
					var L = lines[iLine];
					if (string.IsNullOrWhiteSpace(L)) break;
					if (ReHeading.IsMatch(L) || ReFence.IsMatch(L) || ReHr.IsMatch(L)) break;
					if (ReUl.IsMatch(L) || ReOl.IsMatch(L) || ReQuote.IsMatch(L)) break;
					// 缩进续行：保留换行（对齐 mdview，渲染时 softbr）
					if (L.Length > 0 && (L[0] == ' ' || L[0] == '\t')) {
						body += "\n" + L.Trim();
						iLine++;
						continue;
					}
					break;
				}
				add(doc, new MdBlock {
					Kind = MdBlockKind.ListItem,
					SourceLine0 = start,
					SourceLine1 = iLine - 1,
					Level = indent,
					Ordered = ordered,
					TaskChecked = taskChecked,
					Text = body,
					Spans = ParseInlines(body),
				});
				continue;
			}

			// HTML <details>…</details>（对齐 mdview；body 再解析 MD）
			var t = line.TrimStart();
			if (isdetailstag(t)) {
				if (trydetailsblock(lines, iLine, tabSize, 0, out var det)) {
					add(doc, det);
					iLine = det.SourceLine1 + 1;
					continue;
				}
			}

			// HTML <img ...>（含 style 宽高）
			if (isimgtag(t)) {
				if (tryhtmlimgblock(t, out var img)) {
					img.SourceLine0 = iLine;
					img.SourceLine1 = iLine;
					add(doc, img);
					iLine++;
					continue;
				}
			}

			// HTML block (其它标签：源码转义显示)
			if (t.StartsWith("<", StringComparison.Ordinal) && t.IndexOf('>') > 0
				&& !t.StartsWith("<http", StringComparison.OrdinalIgnoreCase)) {
				var start = iLine;
				var sb = new StringBuilder();
				while (iLine < lines.Count) {
					if (sb.Length > 0) sb.Append('\n');
					sb.Append(lines[iLine]);
					var done = lines[iLine].IndexOf("</", StringComparison.Ordinal) >= 0
						|| lines[iLine].TrimEnd().EndsWith("/>", StringComparison.Ordinal);
					iLine++;
					if (done) break;
					if (iLine < lines.Count && string.IsNullOrWhiteSpace(lines[iLine])) break;
				}
				add(doc, new MdBlock {
					Kind = MdBlockKind.Html,
					SourceLine0 = start,
					SourceLine1 = iLine - 1,
					Text = sb.ToString(),
				});
				continue;
			}

			// paragraph (merge consecutive)
			{
				var start = iLine;
				var sb = new StringBuilder();
				while (iLine < lines.Count) {
					var L = lines[iLine];
					if (string.IsNullOrWhiteSpace(L)) break;
					if (ReHeading.IsMatch(L) || ReFence.IsMatch(L) || ReHr.IsMatch(L)
						|| ReUl.IsMatch(L) || ReOl.IsMatch(L) || ReQuote.IsMatch(L))
						break;
					if (L.IndexOf('|') >= 0 && iLine + 1 < lines.Count && ReTableSep.IsMatch(lines[iLine + 1]))
						break;
					var Lt = L.TrimStart();
					if (isdetailstag(Lt) || isimgtag(Lt))
						break;
					if (Lt.StartsWith("<", StringComparison.Ordinal) && Lt.IndexOf('>') > 0
						&& !Lt.StartsWith("<http", StringComparison.OrdinalIgnoreCase))
						break;
					if (sb.Length > 0) sb.Append('\n');
					sb.Append(L);
					iLine++;
				}
				var body = sb.ToString();
				add(doc, new MdBlock {
					Kind = MdBlockKind.Paragraph,
					SourceLine0 = start,
					SourceLine1 = iLine - 1,
					Text = body,
					// 保留源码换行（不用空格拼接），ParseInlines 将 \n → softbr
					Spans = ParseInlines(body),
				});
			}
		}

		return doc;
	}

	/// <summary>
	/// 前导空白显示列宽（对齐 mdview indent_cols）：空格 +1，Tab 进到下一制表位。
	/// </summary>
	public static int IndentCols(string ws, int tabSize = 3) {
		if (string.IsNullOrEmpty(ws)) return 0;
		if (tabSize < 1) tabSize = 1;
		var col = 0;
		foreach (var ch in ws) {
			if (ch == '\t')
				col += tabSize - (col % tabSize);
			else if (ch == ' ')
				col++;
			else
				col++; // 其它空白按 1
		}
		return col;
	}

	/// <summary>
	/// 将 \\t 按 tabSize 展成空格（工具/测试用）。编辑器加载与 Tab 键默认保留 \\t，不调用此方法。
	/// <paramref name="outsideFencesOnly"/> 为 true 时保留围栏代码块内的 Tab。
	/// </summary>
	public static string ExpandTabs(string text, int tabSize = 3, bool outsideFencesOnly = true) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		if (tabSize < 1) tabSize = 1;
		if (text.IndexOf('\t') < 0) return text;
		var lines = SplitLines(text);
		var sb = new StringBuilder(text.Length + 32);
		var inFence = false;
		char fenceCh = '\0';
		var fenceLen = 0;
		for (var li = 0; li < lines.Count; li++) {
			if (li > 0) sb.Append('\n');
			var line = lines[li] ?? "";
			if (outsideFencesOnly) {
				var t = line.TrimStart();
				if (t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith("~~~", StringComparison.Ordinal)) {
					var ch = t[0];
					var n = 0;
					while (n < t.Length && t[n] == ch) n++;
					if (n >= 3) {
						if (!inFence) {
							inFence = true;
							fenceCh = ch;
							fenceLen = n;
							sb.Append(line);
							continue;
						}
						if (ch == fenceCh && n >= fenceLen && t.Substring(n).Trim().Length == 0) {
							inFence = false;
							sb.Append(line);
							continue;
						}
					}
				}
				if (inFence) {
					sb.Append(line);
					continue;
				}
			}
			var col = 0;
			foreach (var ch in line) {
				if (ch == '\t') {
					var nsp = tabSize - (col % tabSize);
					if (nsp <= 0) nsp = tabSize;
					sb.Append(' ', nsp);
					col += nsp;
				} else {
					sb.Append(ch);
					col++;
				}
			}
		}
		return sb.ToString();
	}

	/// <summary>
	/// 按旧/新 Tab 宽重算行首缩进（已展成空格后改 MdTabSize 时用）：
	/// 列宽 ÷ fromTab 得层级数，再 × toTab；余数列保留。
	/// 围栏代码块内不改。
	/// </summary>
	public static string RetargetLeadingIndent(string text, int fromTab, int toTab) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		if (fromTab < 1) fromTab = 1;
		if (toTab < 1) toTab = 1;
		if (fromTab == toTab) return text;
		var lines = SplitLines(text);
		var sb = new StringBuilder(text.Length + 32);
		var inFence = false;
		char fenceCh = '\0';
		var fenceLen = 0;
		for (var li = 0; li < lines.Count; li++) {
			if (li > 0) sb.Append('\n');
			var line = lines[li] ?? "";
			var t = line.TrimStart();
			if (t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith("~~~", StringComparison.Ordinal)) {
				var ch = t[0];
				var n = 0;
				while (n < t.Length && t[n] == ch) n++;
				if (n >= 3) {
					if (!inFence) {
						inFence = true;
						fenceCh = ch;
						fenceLen = n;
						sb.Append(line);
						continue;
					}
					if (ch == fenceCh && n >= fenceLen && t.Substring(n).Trim().Length == 0) {
						inFence = false;
						sb.Append(line);
						continue;
					}
				}
			}
			if (inFence) {
				sb.Append(line);
				continue;
			}
			var i = 0;
			while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
			if (i == 0) {
				sb.Append(line);
				continue;
			}
			var cols = IndentCols(line.Substring(0, i), fromTab);
			var levels = cols / fromTab;
			var rem = cols % fromTab;
			var newCols = levels * toTab + rem;
			if (newCols > 0) sb.Append(' ', newCols);
			sb.Append(line, i, line.Length - i);
		}
		return sb.ToString();
	}

	/// <summary>行内解析（公开便于单测）。</summary>
	public static List<MdSpan> ParseInlines(string text) {
		var spans = new List<MdSpan>();
		if (string.IsNullOrEmpty(text)) return spans;
		var i = 0;
		var n = text.Length;
		var buf = new StringBuilder();

		void flush() {
			if (buf.Length == 0) return;
			spans.Add(new MdSpan { Kind = "text", Text = buf.ToString() });
			buf.Clear();
		}

		while (i < n) {
			var c = text[i];
			// 源码硬换行 → softbr（预览 <br/>）；兼容「行尾两空格 / 行尾 \」
			if (c == '\n') {
				flush();
				spans.Add(new MdSpan { Kind = "softbr", Text = "\n" });
				i++;
				continue;
			}
			if (c == '\\' && i + 1 < n && text[i + 1] == '\n') {
				flush();
				spans.Add(new MdSpan { Kind = "softbr", Text = "\n" });
				i += 2;
				continue;
			}
			if (c == '`' ) {
				var j = text.IndexOf('`', i + 1);
				if (j > i) {
					flush();
					spans.Add(new MdSpan { Kind = "code", Text = text.Substring(i + 1, j - i - 1) });
					i = j + 1;
					continue;
				}
			}
			if (c == '!' && i + 1 < n && text[i + 1] == '[') {
				if (TryLink(text, i + 1, out var end, out var label, out var href)) {
					flush();
					spans.Add(new MdSpan { Kind = "image", Text = label, Href = href });
					i = end;
					continue;
				}
			}
			if (c == '[') {
				if (TryLink(text, i, out var end, out var label, out var href)) {
					flush();
					spans.Add(new MdSpan { Kind = "link", Text = label, Href = href });
					i = end;
					continue;
				}
			}
			// ==mark==
			if (c == '=' && i + 1 < n && text[i + 1] == '=') {
				var j = text.IndexOf("==", i + 2, StringComparison.Ordinal);
				if (j > i + 2) {
					flush();
					spans.Add(new MdSpan { Kind = "mark", Text = text.Substring(i + 2, j - i - 2) });
					i = j + 2;
					continue;
				}
			}
			// ~~strike~~
			if (c == '~' && i + 1 < n && text[i + 1] == '~') {
				var j = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
				if (j > i + 2) {
					flush();
					spans.Add(new MdSpan { Kind = "strike", Text = text.Substring(i + 2, j - i - 2) });
					i = j + 2;
					continue;
				}
			}
			// **bold** or __bold__
			if ((c == '*' || c == '_') && i + 1 < n && text[i + 1] == c) {
				var mark = new string(c, 2);
				var j = text.IndexOf(mark, i + 2, StringComparison.Ordinal);
				if (j > i + 2) {
					flush();
					spans.Add(new MdSpan { Kind = "bold", Text = text.Substring(i + 2, j - i - 2) });
					i = j + 2;
					continue;
				}
			}
			// *italic* or _italic_
			if (c == '*' || c == '_') {
				var j = text.IndexOf(c, i + 1);
				if (j > i + 1 && (j + 1 >= n || text[j + 1] != c)) {
					// 避免匹配空
					flush();
					spans.Add(new MdSpan { Kind = "italic", Text = text.Substring(i + 1, j - i - 1) });
					i = j + 1;
					continue;
				}
			}
			// autolink http(s)
			if (c == 'h' && (text.IndexOf("http://", i, StringComparison.Ordinal) == i
				|| text.IndexOf("https://", i, StringComparison.Ordinal) == i)) {
				var j = i;
				while (j < n && !char.IsWhiteSpace(text[j]) && text[j] != ')') j++;
				while (j > i && ".,;:".IndexOf(text[j - 1]) >= 0) j--;
				var url = text.Substring(i, j - i);
				flush();
				spans.Add(new MdSpan { Kind = "link", Text = url, Href = url });
				i = j;
				continue;
			}
			buf.Append(c);
			i++;
		}
		flush();
		return spans;
	}

	static bool TryLink(string text, int openBracket, out int end, out string label, out string href) {
		end = openBracket;
		label = null;
		href = null;
		if (openBracket >= text.Length || text[openBracket] != '[') return false;
		var close = text.IndexOf(']', openBracket + 1);
		if (close < 0) return false;
		if (close + 1 >= text.Length || text[close + 1] != '(') return false;
		var endp = text.IndexOf(')', close + 2);
		if (endp < 0) return false;
		label = text.Substring(openBracket + 1, close - openBracket - 1);
		href = text.Substring(close + 2, endp - close - 2).Trim();
		end = endp + 1;
		return true;
	}

	static void add(MdDoc doc, MdBlock b) {
		var idx = doc.Blocks.Count;
		doc.Blocks.Add(b);
		if (doc.LineToBlock == null || doc.LineToBlock.Length == 0) return;
		var a = Math.Max(0, b.SourceLine0);
		var z = Math.Min(doc.LineToBlock.Length - 1, b.SourceLine1);
		for (var i = a; i <= z; i++) {
			if (doc.LineToBlock[i] < 0)
				doc.LineToBlock[i] = idx;
		}
	}

	public static List<string> SplitLines(string text) {
		var list = new List<string>();
		if (text == null) { list.Add(""); return list; }
		text = text.Replace("\r\n", "\n").Replace('\r', '\n');
		if (text.Length == 0) { list.Add(""); return list; }
		var i = 0;
		while (i <= text.Length) {
			var j = text.IndexOf('\n', i);
			if (j < 0) {
				list.Add(text.Substring(i));
				break;
			}
			list.Add(text.Substring(i, j - i));
			i = j + 1;
			if (i == text.Length) {
				// 末尾换行保留空行
				list.Add("");
				break;
			}
		}
		return list;
	}

	static string[] SplitTableRow(string line) {
		line = line.Trim();
		if (line.StartsWith("|", StringComparison.Ordinal)) line = line.Substring(1);
		if (line.EndsWith("|", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
		var parts = line.Split('|');
		for (var i = 0; i < parts.Length; i++)
			parts[i] = parts[i].Trim();
		return parts;
	}

	/// <summary>是否 GFM 表分隔行（|:---|）。</summary>
	public static bool IsTableSeparator(string line) =>
		!string.IsNullOrEmpty(line) && ReTableSep.IsMatch(line);

	/// <summary>拆分表行单元格（去两侧 |）。</summary>
	public static string[] SplitTableCells(string line) => SplitTableRow(line ?? "");

	/// <summary>解析分隔行对齐。</summary>
	public static List<string> ParseTableAlignments(string sep) => ParseTableAlign(sep ?? "");

	static List<string> ParseTableAlign(string sep) {
		var cells = SplitTableRow(sep);
		var align = new List<string>(cells.Length);
		foreach (var c in cells) {
			var t = c.Trim();
			var left = t.StartsWith(":", StringComparison.Ordinal);
			var right = t.EndsWith(":", StringComparison.Ordinal);
			if (left && right) align.Add("center");
			else if (right) align.Add("right");
			else align.Add("left");
		}
		return align;
	}


	const int MAX_DETAILS_DEPTH = 8;

	static bool isdetailstag(string t) {
		if (string.IsNullOrEmpty(t)) return false;
		if (!t.StartsWith("<details", StringComparison.OrdinalIgnoreCase)) return false;
		if (t.Length == 8) return true;
		var c = t[8];
		return char.IsWhiteSpace(c) || c == '>';
	}

	static bool isimgtag(string t) {
		if (string.IsNullOrEmpty(t)) return false;
		if (!t.StartsWith("<img", StringComparison.OrdinalIgnoreCase)) return false;
		if (t.Length == 4) return true;
		var c = t[4];
		return char.IsWhiteSpace(c) || c == '/' || c == '>';
	}

	static bool tryhtmlimgblock(string line, out MdBlock block) {
		block = null;
		if (string.IsNullOrEmpty(line)) return false;
		var m = Regex.Match(line, @"<img\b([^>]*)/?\s*>", RegexOptions.IgnoreCase);
		if (!m.Success) return false;
		var attrs = parsehtmlattrs(m.Groups[1].Value);
		if (!attrs.TryGetValue("src", out var src) || string.IsNullOrWhiteSpace(src))
			return false;
		attrs.TryGetValue("alt", out var alt);
		double? w = null, h = null;
		if (attrs.TryGetValue("width", out var wa)) w = parsecsspx(wa);
		if (attrs.TryGetValue("height", out var ha)) h = parsecsspx(ha);
		if (attrs.TryGetValue("style", out var style)) {
			var st = parsecssstyle(style);
			if (st.TryGetValue("width", out var sw)) w = parsecsspx(sw) ?? w;
			if (st.TryGetValue("height", out var sh)) h = parsecsspx(sh) ?? h;
		}
		src = src.Trim();
		block = new MdBlock {
			Kind = MdBlockKind.HtmlImg,
			Text = src,
			Spans = new List<MdSpan> {
				new MdSpan { Kind = "image", Text = alt ?? "", Href = src },
			},
			ImgWidthPx = w,
			ImgHeightPx = h,
		};
		return true;
	}

	static Dictionary<string, string> parsehtmlattrs(string tag) {
		var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(tag)) return attrs;
		foreach (Match m in Regex.Matches(tag, @"([A-Za-z_:][\w:.-]*)\s*=\s*""([^""]*)"""))
			attrs[m.Groups[1].Value] = m.Groups[2].Value;
		foreach (Match m in Regex.Matches(tag, @"([A-Za-z_:][\w:.-]*)\s*=\s*'([^']*)'"))
			if (!attrs.ContainsKey(m.Groups[1].Value))
				attrs[m.Groups[1].Value] = m.Groups[2].Value;
		foreach (Match m in Regex.Matches(tag, @"([A-Za-z_:][\w:.-]*)\s*=\s*([^\s""'=<>`]+)"))
			if (!attrs.ContainsKey(m.Groups[1].Value))
				attrs[m.Groups[1].Value] = m.Groups[2].Value;
		if (Regex.IsMatch(tag, @"\bopen\b", RegexOptions.IgnoreCase)
			&& !attrs.ContainsKey("open"))
			attrs["open"] = "open";
		return attrs;
	}

	static Dictionary<string, string> parsecssstyle(string style) {
		var outDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(style)) return outDict;
		foreach (var part in style.Split(';')) {
			var p = part.Trim();
			if (p.Length == 0) continue;
			var colon = p.IndexOf(':');
			if (colon <= 0) continue;
			var k = p.Substring(0, colon).Trim();
			var v = p.Substring(colon + 1).Trim();
			if (k.Length > 0) outDict[k] = v;
		}
		return outDict;
	}

	static double? parsecsspx(string v) {
		if (string.IsNullOrWhiteSpace(v)) return null;
		v = v.Trim();
		if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			v = v.Substring(0, v.Length - 2).Trim();
		if (double.TryParse(v, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out var n)
			&& n > 0 && n < 20000)
			return n;
		return null;
	}

	static bool trydetailsblock(List<string> lines, int start, int tabSize, int depth, out MdBlock block) {
		block = null;
		if (lines == null || start < 0 || start >= lines.Count) return false;
		if (depth >= MAX_DETAILS_DEPTH) return false;
		var first = lines[start] ?? "";
		if (!isdetailstag(first.TrimStart())) return false;

		var openAngle = first.IndexOf('<');
		var closeAngle = first.IndexOf('>', openAngle >= 0 ? openAngle : 0);
		var attrStr = "";
		if (openAngle >= 0 && closeAngle > openAngle) {
			var tagInner = first.Substring(openAngle + 1, closeAngle - openAngle - 1);
			if (tagInner.Length >= 7)
				attrStr = tagInner.Substring(7);
		}
		var attrs = parsehtmlattrs(attrStr);
		var isOpen = attrs.ContainsKey("open");

		var nest = 0;
		var endIdx = -1;
		var buf = new StringBuilder();
		for (var i = start; i < lines.Count; i++) {
			var line = lines[i] ?? "";
			var lower = line.ToLowerInvariant();
			var pos = 0;
			while (pos < lower.Length) {
				var a = lower.IndexOf("<details", pos, StringComparison.Ordinal);
				var b = lower.IndexOf("</details>", pos, StringComparison.Ordinal);
				if (a < 0 && b < 0) break;
				if (a >= 0 && (b < 0 || a < b)) {
					nest++;
					pos = a + 8;
				} else {
					nest--;
					pos = b + 10;
					if (nest == 0) {
						endIdx = i;
						break;
					}
				}
			}
			if (buf.Length > 0) buf.Append('\n');
			buf.Append(line);
			if (endIdx >= 0) break;
		}
		if (endIdx < 0) return false;

		var blob = buf.ToString();
		var inner = Regex.Replace(blob, @"^.*?<details\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		inner = Regex.Replace(inner, @"</details>\s*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

		var summary = "Details";
		var sm = Regex.Match(inner, @"<summary\b[^>]*>(.*?)</summary>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		if (sm.Success) {
			summary = Regex.Replace(sm.Groups[1].Value, @"\s+", " ").Trim();
			if (string.IsNullOrEmpty(summary)) summary = "Details";
			inner = inner.Remove(sm.Index, sm.Length);
		}
		inner = inner.Trim('\r', '\n');

		var bodyLineOffset = start;
		for (var i = start; i <= endIdx; i++) {
			if ((lines[i] ?? "").IndexOf("</summary>", StringComparison.OrdinalIgnoreCase) >= 0) {
				bodyLineOffset = i + 1;
				break;
			}
		}

		List<MdBlock> children = null;
		if (!string.IsNullOrWhiteSpace(inner)) {
			var innerDoc = Parse(inner, tabSize);
			children = innerDoc?.Blocks ?? new List<MdBlock>();
			shiftblocklines(children, bodyLineOffset);
		} else {
			children = new List<MdBlock>();
		}

		block = new MdBlock {
			Kind = MdBlockKind.Details,
			SourceLine0 = start,
			SourceLine1 = endIdx,
			Summary = summary,
			DetailsOpen = isOpen,
			Children = children,
			Spans = ParseInlines(summary),
		};
		return true;
	}

	static void shiftblocklines(List<MdBlock> blocks, int delta) {
		if (blocks == null || delta == 0) return;
		foreach (var b in blocks) {
			b.SourceLine0 += delta;
			b.SourceLine1 += delta;
			if (b.Children != null)
				shiftblocklines(b.Children, delta);
		}
	}


	/// <summary>从源行号映射到块起始源行（同步滚动用）。</summary>
	public static int BlockIndexForLine(MdDoc doc, int line0) {
		if (doc?.LineToBlock == null || doc.LineToBlock.Length == 0) return 0;
		if (line0 < 0) line0 = 0;
		if (line0 >= doc.LineToBlock.Length) line0 = doc.LineToBlock.Length - 1;
		var b = doc.LineToBlock[line0];
		if (b >= 0) return b;
		for (var i = line0; i >= 0; i--)
			if (doc.LineToBlock[i] >= 0) return doc.LineToBlock[i];
		for (var i = line0; i < doc.LineToBlock.Length; i++)
			if (doc.LineToBlock[i] >= 0) return doc.LineToBlock[i];
		return 0;
	}
}
