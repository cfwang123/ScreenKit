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
	const double BASE_FONT = 15;
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

	/// <summary>Markdown 正文不分页：把整篇按文字偏移算一份命中表，前后查找就是挪下标。</summary>
	public FindResult Find(string query, bool forward, bool ignoreCase, bool restart = false, bool fromView = false) {
		if (string.IsNullOrEmpty(query)) {
			ClearFind();
			return FindResult.Miss();
		}
		var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		if (restart || !string.Equals(query, findtext, StringComparison.Ordinal)) {
			findtext = query;
			findhits.Clear();
			var plain = new TextRange(body.Document.ContentStart, body.Document.ContentEnd).Text;
			var pos = 0;
			while (pos < plain.Length) {
				var i = plain.IndexOf(query, pos, cmp);
				if (i < 0) break;
				findhits.Add(i);
				pos = i + Math.Max(1, query.Length);
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

	/// <summary>按字符偏移选中并滚到可见。</summary>
	void selecthit(int offset) {
		try {
			var start = body.Document.ContentStart.GetPositionAtOffset(offset, LogicalDirection.Forward);
			if (start == null) return;
			var end = start.GetPositionAtOffset(findtext.Length, LogicalDirection.Forward) ?? start;
			body.Selection.Select(start, end);
			start.Paragraph?.BringIntoView();
		}
		catch { }
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

	/// <summary>按当前可用宽度重排；宽度变了才重建，避免滚动时反复排版。</summary>
	void relayout() {
		if (doc == null) {
			body.Visibility = Visibility.Collapsed;
			lbempty.Visibility = Visibility.Visible;
			return;
		}
		var width = scroller.ActualWidth;
		if (width < 80) width = 720;
		width = Math.Max(200, width - PAD_X * 2);
		var fd = MdFlowBuilder.Build(doc, width, onlink, FilePath, true);
		fd.FontSize = BASE_FONT * zoom;
		fd.LineHeight = BASE_FONT * zoom * 1.55;
		body.Document = fd;
		if (Math.Abs((body.Padding.Left) - 0) < 0.1)
			body.Padding = new Thickness(PAD_X, PAD_Y, PAD_X, 40);
		body.Visibility = Visibility.Visible;
		lbempty.Visibility = Visibility.Collapsed;
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
		foreach (var b in doc.Blocks) {
			if (b.Kind != MdBlockKind.Heading) continue;
			var slug = (b.Text ?? "").Trim().ToLowerInvariant().Replace(" ", "-");
			if (slug != want) continue;
			// 标题块在 FlowDocument 里带 SourceLine0，找第一个匹配的段落滚过去
			foreach (var blk in body.Document.Blocks) {
				if (blk is Paragraph p && Equals(p.Tag, b.SourceLine0)) {
					p.BringIntoView();
					return;
				}
			}
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
