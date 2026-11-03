using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenKit.DocView;

/// <summary>
/// Markdown 阅读器：解析成 MdDoc 后用 FlowDocument 排版。
/// 不做编辑，工具栏只有缩放与查找；图片可点开大图（<see cref="ImageOverlay"/>）。
/// </summary>
sealed class MdViewer : IDocViewer {
	/// <summary>正文基准字号。表格列宽在 MdFlowBuilder 里按 14 量字，这里保持一致，缩放再乘上去。</summary>
	const double BASE_FONT = 14;
	const double PAD_X = 28;
	const double PAD_Y = 20;

	readonly Border root;
	readonly ScrollViewer scroller;
	readonly RichTextBox body;
	readonly TextBlock lbempty;
	string text = "";
	MdDoc doc;
	double zoom = 1.0;
	bool disposed;
	string findtext = "";
	readonly List<int> findhits = new();
	int findindex = -1;
	/// <summary>块数超过这个值就只排视口附近，避免超大文件一次性排版卡死。</summary>
	const int WINDOW_THRESHOLD = 300;
	/// <summary>视口上下各预排的块数。</summary>
	const int WINDOW_MARGIN = 40;
	/// <summary>当前文档里已排的块区间 [winFrom, winTo)。</summary>
	int winFrom;
	int winTo;

	public MdViewer() {
		lbempty = new TextBlock {
			Text = DocViewLoc.T("md.assets"),
			Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0x99)),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Visibility = Visibility.Collapsed,
		};
		body = new RichTextBox {
			IsReadOnly = true,
			IsDocumentEnabled = true,
			BorderThickness = new Thickness(0),
			Background = Brushes.Transparent,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			IsInactiveSelectionHighlightEnabled = true,
			SelectionBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)),
		};
		scroller = new ScrollViewer {
			Content = body,
			Padding = new Thickness(PAD_X, PAD_Y, PAD_X, 40),
			Background = Brushes.White,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Focusable = false,
		};
		var grid = new Grid();
		grid.Children.Add(scroller);
		grid.Children.Add(lbempty);
		root = new Border {
			Child = grid,
			Background = Brushes.White,
		};
		scroller.SizeChanged += (_, _) => relayout();
		scroller.ScrollChanged += (_, e) => {
			if (e.VerticalChange == 0) return;
			ensurewindow();
		};
		body.PreviewMouseWheel += (_, e) => {
			if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
			e.Handled = true;
			SetZoom(zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
		};
	}

	public FrameworkElement View => root;
	public string FilePath { get; private set; }
	public string Title { get; private set; }
	public DocKind Kind => DocKind.Md;
	public double Zoom => zoom;
	public int PageCount => 1;
	public int CurrentPage => string.IsNullOrEmpty(text) ? 0 : 1;
	public bool HasOutline => false;
	public bool SidePanelVisible => false;

	public string StatusText {
		get {
			if (string.IsNullOrEmpty(FilePath)) return "Markdown";
			var n = doc?.Blocks?.Count ?? 0;
			return $"Markdown  ·  {n} 块  ·  {(int)(zoom * 100)}%";
		}
	}

	public event Action StatusChanged;

	public void Load(string path) {
		FilePath = path;
		Title = Path.GetFileName(path);
		try {
			text = TextFileIo.Load(path).Text ?? "";
		}
		catch (Exception ex) {
			DocViewLog.Error("MdViewer.Load", ex);
			text = "";
		}
		doc = MdParser.Parse(text);
		relayout();
		raisestatus();
	}

	public void SetZoom(double z) {
		z = z < 0.3 ? 0.3 : z > 4 ? 4 : z;
		if (Math.Abs(z - zoom) < 0.001) return;
		zoom = z;
		relayout();
		raisestatus();
	}

	public void ZoomBy(double factor) => SetZoom(zoom * factor);
	public void ZoomIn() => SetZoom(zoom * 1.1);
	public void ZoomOut() => SetZoom(zoom / 1.1);

	public void ZoomFitWidth() {
		zoom = 1.0;
		relayout();
		raisestatus();
	}

	public void ZoomFitPage() => ZoomFitWidth();
	public void GoPrevPage() { }
	public void GoNextPage() { }
	public void GoToPage(int page1Based) { }
	public void RotateBy(int deltaQuarterTurns) { }
	public void SetSidePanelVisible(bool show) { }

	/// <summary>Markdown 正文不分页：命中按块下标记，窗口滑动后仍能定位。</summary>
	public FindResult Find(string query, bool forward, bool ignoreCase, bool restart = false, bool fromView = false) {
		if (string.IsNullOrEmpty(query)) {
			ClearFind();
			return FindResult.Miss();
		}
		var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		if (restart || !string.Equals(query, findtext, StringComparison.Ordinal)) {
			findtext = query;
			findhits.Clear();
			var blocks = doc?.Blocks;
			if (blocks != null) {
				for (var i = 0; i < blocks.Count; i++) {
					if ((blocktext(blocks[i]) ?? "").IndexOf(query, cmp) >= 0)
						findhits.Add(i);
				}
			}
			findindex = -1;
		}
		if (findhits.Count == 0) return FindResult.Miss();
		findindex = forward
			? (findindex + 1) % findhits.Count
			: (findindex - 1 + findhits.Count) % findhits.Count;
		selecthit(findhits[findindex]);
		return FindResult.Hit(findindex + 1, findhits.Count);
	}

	public void ClearFind() {
		findhits.Clear();
		findindex = -1;
		findtext = "";
	}

	/// <summary>滚到命中所在的块并选中第一次出现的文字。</summary>
	void selecthit(int blockIndex) {
		try {
			showaround(blockIndex);
			body.UpdateLayout();
			foreach (var blk in body.Document.Blocks) {
				if (!Equals(blk.Tag, blockIndex)) continue;
				var range = new TextRange(blk.ContentStart, blk.ContentEnd);
				var plain = range.Text ?? "";
				var at = plain.IndexOf(findtext, StringComparison.OrdinalIgnoreCase);
				var start = at < 0 ? blk.ContentStart
					: blk.ContentStart.GetPositionAtOffset(at, LogicalDirection.Forward) ?? blk.ContentStart;
				var end = start.GetPositionAtOffset(findtext.Length, LogicalDirection.Forward) ?? start;
				body.Selection.Select(start, end);
				blk.BringIntoView();
				return;
			}
		}
		catch { }
	}

	/// <summary>一块里能被搜到的纯文本。</summary>
	static string blocktext(MdBlock b) {
		if (b == null) return "";
		var sb = new System.Text.StringBuilder();
		if (!string.IsNullOrEmpty(b.Text)) sb.Append(b.Text).Append('\n');
		if (!string.IsNullOrEmpty(b.Summary)) sb.Append(b.Summary).Append('\n');
		if (b.Spans != null) {
			foreach (var s in b.Spans)
				if (!string.IsNullOrEmpty(s.Text)) sb.Append(s.Text);
		}
		if (b.TableRows != null) {
			foreach (var row in b.TableRows) {
				if (row == null) continue;
				foreach (var cell in row)
					if (!string.IsNullOrEmpty(cell)) sb.Append(cell).Append(' ');
			}
		}
		if (b.Children != null) {
			foreach (var c in b.Children) sb.Append(blocktext(c));
		}
		return sb.ToString();
	}

	/// <summary>复制选中的正文；没有选区时不处理。</summary>
	public bool TryCopySelection() {
		var sel = body.Selection;
		if (sel == null || sel.IsEmpty) return false;
		try {
			Clipboard.SetText(sel.Text ?? "");
			return true;
		}
		catch { return false; }
	}

	public void CaptureViewState(out double h, out double v, out double z, out int sheetOrPage) {
		h = scroller.HorizontalOffset;
		v = scroller.VerticalOffset;
		z = zoom;
		sheetOrPage = 1;
	}

	public void RestoreViewState(double h, double v, double z, int sheetOrPage) {
		if (z > 0.29 && Math.Abs(z - zoom) > 0.001) SetZoom(z);
		try {
			scroller.Dispatcher.BeginInvoke(new Action(() => {
				scroller.ScrollToHorizontalOffset(h);
				scroller.ScrollToVerticalOffset(v);
			}), DispatcherPriority.Background);
		}
		catch { }
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		doc = null;
		body.Document = new FlowDocument();
	}

	/// <summary>
	/// 按当前可用宽度重排。FlowDocument 的排版宽度是页宽，缩放靠改字号实现，
	/// 所以放大时要按比例收窄页宽，等价于把内容放大。
	/// 块数多时只排开头一段，其余滚动到附近再补。
	/// </summary>
	void relayout() {
		if (doc == null) {
			body.Visibility = Visibility.Collapsed;
			lbempty.Visibility = Visibility.Visible;
			return;
		}
		var n = doc.Blocks?.Count ?? 0;
		var to = n > WINDOW_THRESHOLD ? Math.Min(n, WINDOW_MARGIN * 2) : n;
		fillwindow(0, to);
		body.Visibility = Visibility.Visible;
		lbempty.Visibility = Visibility.Collapsed;
	}

	/// <summary>滚动时视口靠近已排区间边缘，就以当前块为中心重排一窗。</summary>
	void ensurewindow() {
		var n = doc?.Blocks?.Count ?? 0;
		if (n <= WINDOW_THRESHOLD || winTo >= n && winFrom <= 0) return;
		var idx = blockatviewport();
		if (idx < 0) return;
		if (idx - winFrom > WINDOW_MARGIN / 2 && winTo - idx > WINDOW_MARGIN / 2) return;
		showaround(idx);
	}

	/// <summary>以指定块为中心排一窗，并尽量保持这一块在视口中的相对位置。</summary>
	void showaround(int blockIndex) {
		var n = doc?.Blocks?.Count ?? 0;
		if (n == 0) return;
		if (blockIndex < 0) blockIndex = 0;
		if (blockIndex >= n) blockIndex = n - 1;
		var from = Math.Max(0, blockIndex - WINDOW_MARGIN);
		var to = Math.Min(n, blockIndex + WINDOW_MARGIN);
		double keep = double.NaN;
		var anchor = blockof(blockIndex);
		if (anchor != null) {
			try { keep = anchor.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top - scroller.VerticalOffset; }
			catch { keep = double.NaN; }
		}
		fillwindow(from, to);
		var again = blockof(blockIndex);
		if (again == null || double.IsNaN(keep)) return;
		body.UpdateLayout();
		double now;
		try { now = again.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top; }
		catch { return; }
		scroller.ScrollToVerticalOffset(now - keep);
	}

	/// <summary>排 [from, to) 这段块；每个块的 Tag 存它在全文里的下标，滚动和查找都靠它定位。</summary>
	void fillwindow(int from, int to) {
		var viewW = scroller.ActualWidth - PAD_X * 2;
		if (viewW < 120) viewW = 720;
		var pageW = Math.Max(160, viewW / zoom);
		var fd = MdFlowBuilder.BuildRange(doc, from, to, pageW, onlink, FilePath, true);
		fd.FontSize = BASE_FONT * zoom;
		fd.LineHeight = BASE_FONT * zoom * 1.55;
		var i = from;
		foreach (var blk in fd.Blocks)
			blk.Tag = i++;
		body.Document = fd;
		body.Padding = new Thickness(PAD_X, PAD_Y, PAD_X, 40);
		winFrom = from;
		winTo = to;
	}

	/// <summary>视口顶部附近那一块在全文里的下标；还没排出来时返回 -1。</summary>
	int blockatviewport() {
		try {
			var pt = body.TranslatePoint(new Point(8, 8), body);
			var pos = body.GetPositionFromPoint(pt, true);
			var blk = pos?.Paragraph ?? pos?.Parent as TextElement;
			while (blk != null && !(blk is Block)) blk = (blk as TextElement)?.Parent as TextElement;
			if (blk?.Tag is int idx) return idx;
		}
		catch { }
		return winFrom;
	}

	/// <summary>已排文档里下标为 idx 的块。</summary>
	Block blockof(int idx) {
		foreach (var blk in body.Document.Blocks)
			if (Equals(blk.Tag, idx)) return blk;
		return null;
	}

	void onlink(string href) {
		if (string.IsNullOrEmpty(href)) return;
		if (href.StartsWith("#", StringComparison.Ordinal)) {
			jumptoanchor(href.TrimStart('#'));
			return;
		}
		if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			|| href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) {
			try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(href) { UseShellExecute = true }); }
			catch { }
		}
	}

	void jumptoanchor(string anchor) {
		if (string.IsNullOrEmpty(anchor) || doc?.Blocks == null) return;
		var want = anchor.Trim().ToLowerInvariant();
		for (var i = 0; i < doc.Blocks.Count; i++) {
			var b = doc.Blocks[i];
			if (b.Kind != MdBlockKind.Heading) continue;
			var slug = (b.Text ?? "").Trim().ToLowerInvariant().Replace(" ", "-");
			if (slug != want) continue;
			selecthit(i);
			return;
		}
	}

	void raisestatus() {
		try { StatusChanged?.Invoke(); } catch { }
	}

	/// <summary>把 href 规范成本地绝对路径；从 MdViewer 里搬过来，图像与链接共用。</summary>
	internal static string ResolveHrefPath(string baseFile, string href) {
		if (string.IsNullOrWhiteSpace(href)) return null;
		href = href.Trim().Trim('"');
		if (href.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) {
			try { return new Uri(href).LocalPath; }
			catch { }
		}
		string decoded = null;
		if (href.IndexOf('%') >= 0) {
			try {
				var d = Uri.UnescapeDataString(href);
				if (!string.Equals(d, href, StringComparison.Ordinal)) decoded = d;
			}
			catch { }
		}
		string first = null;
		foreach (var cand in new[] { href, decoded }) {
			if (cand == null) continue;
			var full = resolvelocalpath(baseFile, cand);
			if (full == null) continue;
			if (first == null) first = full;
			try {
				if (File.Exists(full) || Directory.Exists(full)) return full;
			}
			catch { }
		}
		if (decoded != null) {
			var dfull = resolvelocalpath(baseFile, decoded);
			if (dfull != null) return dfull;
		}
		return first;
	}

	static string resolvelocalpath(string baseFile, string href) {
		if (string.IsNullOrEmpty(href)) return null;
		href = href.Replace('/', Path.DirectorySeparatorChar);
		try {
			if (Path.IsPathRooted(href)) return Path.GetFullPath(href);
			var dir = string.IsNullOrEmpty(baseFile)
				? Environment.CurrentDirectory
				: (Path.GetDirectoryName(baseFile) ?? Environment.CurrentDirectory);
			return Path.GetFullPath(Path.Combine(dir, href));
		}
		catch { return null; }
	}
}
