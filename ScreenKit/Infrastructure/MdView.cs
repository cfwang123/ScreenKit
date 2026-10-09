using System.Text;
using System.Windows.Documents;

namespace ScreenKit;

/// <summary>把更新说明里常见的 Markdown 画进 FlowDocument。</summary>
static class MdView {
	static readonly FontFamily MonoFont = new FontFamily("Consolas, Cascadia Mono");
	static readonly FontFamily UiFont = new FontFamily("Segoe UI, Microsoft YaHei UI");

	public static void Fill(FlowDocument doc, string md) {
		if (doc == null) return;
		doc.Blocks.Clear();
		doc.FontFamily = UiFont;
		doc.FontSize = 13.5;
		doc.Foreground = brush("TextPrimary", Color.FromRgb(0x1F, 0x29, 0x37));
		doc.PagePadding = new Thickness(14, 10, 14, 16);
		doc.TextAlignment = TextAlignment.Left;
		if (string.IsNullOrWhiteSpace(md)) return;
		var lines = md.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		int i = 0;
		while (i < lines.Length) {
			var raw = lines[i];
			if (string.IsNullOrWhiteSpace(raw)) { i++; continue; }
			var line = raw.TrimEnd();
			if (isfence(line)) { i = addcode(doc, lines, i); continue; }
			if (isheading(line, out var level, out var title)) {
				addheading(doc, level, title);
				i++;
				continue;
			}
			if (ishr(line)) { i++; continue; }
			if (isquote(line)) { i = addquote(doc, lines, i); continue; }
			if (listitem(line, out var ordered, out _)) {
				i = addlist(doc, lines, i, ordered);
				continue;
			}
			addpara(doc, line.Trim());
			i++;
		}
	}

	static int addcode(FlowDocument doc, string[] lines, int i) {
		var sb = new StringBuilder();
		i++;
		while (i < lines.Length && !isfence(lines[i])) {
			if (sb.Length > 0) sb.Append('\n');
			sb.Append(lines[i]);
			i++;
		}
		if (i < lines.Length) i++;
		var p = new Paragraph {
			FontFamily = MonoFont,
			FontSize = 12.5,
			Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
			Padding = new Thickness(8, 6, 8, 6),
			Margin = new Thickness(0, 2, 0, 10),
		};
		p.Inlines.Add(new Run(sb.ToString()));
		doc.Blocks.Add(p);
		return i;
	}

	static void addheading(FlowDocument doc, int level, string title) {
		var size = level switch {
			1 => 20.0,
			2 => 17.0,
			3 => 15.0,
			_ => 13.5,
		};
		var p = new Paragraph {
			FontSize = size,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0, level <= 2 ? 8 : 6, 0, 6),
		};
		addinlines(p, title);
		doc.Blocks.Add(p);
	}

	static int addquote(FlowDocument doc, string[] lines, int i) {
		var sb = new StringBuilder();
		while (i < lines.Length && isquote(lines[i])) {
			if (sb.Length > 0) sb.Append('\n');
			var s = lines[i].Trim();
			if (s.StartsWith(">", StringComparison.Ordinal)) s = s[1..].TrimStart();
			sb.Append(s);
			i++;
		}
		var p = new Paragraph {
			Margin = new Thickness(10, 0, 0, 8),
			Foreground = brush("TextMuted", Color.FromRgb(0x6B, 0x72, 0x80)),
		};
		addinlines(p, sb.ToString());
		doc.Blocks.Add(p);
		return i;
	}

	static int addlist(FlowDocument doc, string[] lines, int i, bool ordered) {
		var list = new System.Windows.Documents.List {
			MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
			Margin = new Thickness(4, 0, 0, 8),
			Padding = new Thickness(14, 0, 0, 0),
		};
		while (i < lines.Length && listitem(lines[i], out var ord, out var item)) {
			if (ord != ordered) break;
			var p = new Paragraph { Margin = new Thickness(0, 1, 0, 2) };
			addinlines(p, item);
			list.ListItems.Add(new ListItem(p));
			i++;
		}
		doc.Blocks.Add(list);
		return i;
	}

	static void addpara(FlowDocument doc, string text) {
		var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
		addinlines(p, text);
		doc.Blocks.Add(p);
	}

	static void addinlines(Paragraph p, string text) {
		if (string.IsNullOrEmpty(text)) return;
		var buf = new StringBuilder();
		var i = 0;
		void flush() {
			if (buf.Length == 0) return;
			p.Inlines.Add(new Run(buf.ToString()));
			buf.Clear();
		}
		while (i < text.Length) {
			if (text[i] == '\\' && i + 1 < text.Length) {
				buf.Append(text[i + 1]);
				i += 2;
				continue;
			}
			if (text[i] == '`') {
				var end = text.IndexOf('`', i + 1);
				if (end > i) {
					flush();
					p.Inlines.Add(new Run(text.Substring(i + 1, end - i - 1)) {
						FontFamily = MonoFont,
						Foreground = brush("TextPrimary", Color.FromRgb(0x1F, 0x29, 0x37)),
					});
					i = end + 1;
					continue;
				}
			}
			if (i + 1 < text.Length && (text.Substring(i, 2) == "**" || text.Substring(i, 2) == "__")) {
				var mk = text.Substring(i, 2);
				var end = text.IndexOf(mk, i + 2, StringComparison.Ordinal);
				if (end > i) {
					flush();
					p.Inlines.Add(new Run(text.Substring(i + 2, end - (i + 2))) {
						FontWeight = FontWeights.SemiBold,
					});
					i = end + 2;
					continue;
				}
			}
			if (text[i] == '!' && i + 1 < text.Length && text[i + 1] == '[') {
				if (trylink(text, i + 1, out var label, out _, out var next)) {
					flush();
					if (label.Length > 0) p.Inlines.Add(new Run(label));
					i = next;
					continue;
				}
			}
			if (text[i] == '[' && trylink(text, i, out var lab, out var url, out var nxt)) {
				flush();
				if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
					|| url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
					Uri uri = null;
					try { uri = new Uri(url); } catch { uri = null; }
					if (uri != null) {
						var link = new Hyperlink(new Run(lab.Length > 0 ? lab : url)) {
							NavigateUri = uri,
							Foreground = brush("Accent", Color.FromRgb(0x25, 0x63, 0xEB)),
						};
						p.Inlines.Add(link);
						i = nxt;
						continue;
					}
				}
			}
			buf.Append(text[i]);
			i++;
		}
		flush();
	}

	static bool trylink(string text, int i, out string label, out string url, out int next) {
		label = "";
		url = "";
		next = i;
		if (i >= text.Length || text[i] != '[') return false;
		var close = text.IndexOf(']', i + 1);
		if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(') return false;
		var end = text.IndexOf(')', close + 2);
		if (end < 0) return false;
		label = text.Substring(i + 1, close - i - 1);
		url = text.Substring(close + 2, end - (close + 2)).Trim();
		next = end + 1;
		return true;
	}

	static bool isfence(string line) {
		var s = line.Trim();
		return s.StartsWith("```", StringComparison.Ordinal);
	}

	static bool isheading(string line, out int level, out string text) {
		level = 0;
		text = null;
		var s = line.TrimStart();
		int i = 0;
		while (i < s.Length && s[i] == '#') i++;
		if (i == 0 || i > 6 || i >= s.Length || s[i] != ' ') return false;
		level = i;
		text = s[(i + 1)..].Trim();
		return text.Length > 0;
	}

	static bool ishr(string line) {
		var s = line.Trim();
		if (s.Length < 3) return false;
		var c = s[0];
		if (c != '-' && c != '*' && c != '_') return false;
		for (int i = 0; i < s.Length; i++)
			if (s[i] != c) return false;
		return true;
	}

	static bool isquote(string line) {
		var s = line.TrimStart();
		return s.StartsWith(">", StringComparison.Ordinal);
	}

	static bool listitem(string line, out bool ordered, out string text) {
		ordered = false;
		text = null;
		var s = line ?? "";
		int i = 0;
		while (i < s.Length && s[i] == ' ') i++;
		if (i >= s.Length) return false;
		if (s[i] == '-' || s[i] == '*' || s[i] == '+') {
			if (i + 1 >= s.Length || s[i + 1] != ' ') return false;
			text = s[(i + 2)..].TrimEnd();
			if (text.StartsWith("[ ] ", StringComparison.Ordinal)
				|| text.StartsWith("[x] ", StringComparison.Ordinal)
				|| text.StartsWith("[X] ", StringComparison.Ordinal))
				text = text[4..];
			return true;
		}
		int j = i;
		while (j < s.Length && char.IsDigit(s[j])) j++;
		if (j > i && j < s.Length && (s[j] == '.' || s[j] == ')') && j + 1 < s.Length && s[j + 1] == ' ') {
			ordered = true;
			text = s[(j + 2)..].TrimEnd();
			return true;
		}
		return false;
	}

	static Brush brush(string key, Color fallback) {
		try {
			if (Application.Current?.TryFindResource(key) is Brush b) return b;
		}
		catch { }
		var s = new SolidColorBrush(fallback);
		if (s.CanFreeze) s.Freeze();
		return s;
	}
}
