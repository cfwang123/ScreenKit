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
/// 先排开头一段让界面马上能看，其余的在后台空档一小段一小段接在后面，
/// 滚到底就是文档结尾——不再只排视口附近那一窗（那样滚到头就只有几十块）。
/// 不做编辑，工具栏只有缩放与查找；图片可点开大图（<see cref="ImageOverlay"/>）。
/// </summary>
sealed class MdViewer : IDocViewer {
	/// <summary>正文基准字号。表格列宽在 MdFlowBuilder 里按 14 量字，这里保持一致，缩放再乘上去。</summary>
	const double BASE_FONT = 14;
	const double PAD_X = 28;
	const double PAD_Y = 20;
	/// <summary>一批排进文档的块数；排完一批让出一次界面，接着排下一批。</summary>
	const int CHUNK = 200;

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
	/// <summary>已从 0 起连续排进文档的块数。</summary>
	int rendered;
	/// <summary>整体重排代数；早先排队的续排回调发现代数变了就作废，免得排重。</summary>
	int gen;
	/// <summary>窗口大小连着变时只重排一次。</summary>
	bool relayoutqueued;
	/// <summary>等待恢复的滚动位置（重载/跨窗搬标签后回到原处）。</summary>
	double restorev;
	double restoreh;

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
		scroller.SizeChanged += (_, _) => queuerelayout();
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
		rendered = 0;
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

	/// <summary>Markdown 正文不分页：命中按块下标记，跳到哪块就把那段排出来再定位。</summary>
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
			var blk = ensureto(blockIndex);
			if (blk == null) return;
			body.UpdateLayout();
			var range = new TextRange(blk.ContentStart, blk.ContentEnd);
			var plain = range.Text ?? "";
			var len = string.IsNullOrEmpty(findtext) ? 0 : findtext.Length;
			var at = len == 0 ? -1 : plain.IndexOf(findtext, StringComparison.OrdinalIgnoreCase);
			var start = at < 0 ? blk.ContentStart
				: blk.ContentStart.GetPositionAtOffset(at, LogicalDirection.Forward) ?? blk.ContentStart;
			var end = len == 0 ? start
				: start.GetPositionAtOffset(len, LogicalDirection.Forward) ?? start;
			body.Selection.Select(start, end);
			blk.BringIntoView();
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
		// 文档可能还没排完，先记下目标位置，等剩下的块排完再滚过去
		restorev = v;
		restoreh = h;
		try {
			scroller.Dispatcher.BeginInvoke(new Action(applyrestore), DispatcherPriority.Background);
		}
		catch { }
	}

	/// <summary>滚到 RestoreViewState 记下的位置；内容还不够长时留着，等排完再说。</summary>
	void applyrestore() {
		if (restorev <= 0 && restoreh <= 0) return;
		try {
			scroller.ScrollToHorizontalOffset(restoreh);
			scroller.ScrollToVerticalOffset(restorev);
		}
		catch { return; }
		// 滚动位置已经到位或者文档排完了，就不再等
		if (AllRendered || Math.Abs(scroller.VerticalOffset - restorev) < 1) {
			restorev = 0;
			restoreh = 0;
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		gen++;
		doc = null;
		body.Document = new FlowDocument();
	}

	/// <summary>自测用：排了多少块 / 共多少块、文档里实际的块数。</summary>
	internal string DebugState() {
		var n = doc?.Blocks?.Count ?? 0;
		return $"rendered={rendered}/{n} fdblocks={body.Document?.Blocks.Count ?? 0} zoom={zoom:0.##}";
	}

	/// <summary>是否已把全部块排进文档。</summary>
	internal bool AllRendered => rendered >= (doc?.Blocks?.Count ?? 0);

	/// <summary>自测用：滚到底，返回滚完的垂直位置。</summary>
	internal double ScrollToEndForTest() {
		try {
			scroller.UpdateLayout();
			scroller.ScrollToEnd();
			scroller.UpdateLayout();
			return scroller.VerticalOffset;
		}
		catch { return -1; }
	}

	/// <summary>自测用：滚到指定垂直位置。</summary>
	internal void ScrollToForTest(double v) {
		try {
			scroller.UpdateLayout();
			scroller.ScrollToVerticalOffset(v);
			scroller.UpdateLayout();
		}
		catch { }
	}

	/// <summary>自测用：视口顶部那块的下标（量不出来为 -1）。</summary>
	internal int TopBlockForTest() => viewportanchor().index;

	/// <summary>窗口尺寸连变时合并成一次重排。</summary>
	void queuerelayout() {
		if (relayoutqueued || disposed) return;
		relayoutqueued = true;
		try {
			body.Dispatcher.BeginInvoke(new Action(() => {
				relayoutqueued = false;
				relayout();
			}), DispatcherPriority.Background);
		}
		catch { relayoutqueued = false; }
	}

	/// <summary>
	/// 按当前可用宽度重排。FlowDocument 的排版宽度是页宽，缩放靠改字号实现，
	/// 所以放大时要按比例收窄页宽，等价于把内容放大。
	/// 先排前 CHUNK 块把第一屏显示出来，其余交给 pumpnext 接着排。
	/// </summary>
	void relayout() {
		if (doc == null) {
			body.Visibility = Visibility.Collapsed;
			lbempty.Visibility = Visibility.Visible;
			return;
		}
		// 缩放/改窗口大小前记下视口顶部那块，重排后回到原处
		var (index, offset) = viewportanchor();
		gen++;
		var n = doc.Blocks?.Count ?? 0;
		var first = Math.Min(n, CHUNK);
		rendered = 0;
		body.Document = makefd(0, first, null, 0);
		rendered = first;
		body.Visibility = Visibility.Visible;
		lbempty.Visibility = Visibility.Collapsed;
		if (index > 0) restoreanchor(index, offset);
		pumpnext();
	}

	/// <summary>剩下的块一批批接在后面，界面始终能滚能选。</summary>
	void pumpnext() {
		if (disposed || doc == null) return;
		var n = doc.Blocks?.Count ?? 0;
		if (rendered >= n) return;
		var g = gen;
		try {
			body.Dispatcher.BeginInvoke(new Action(() => {
				if (disposed || g != gen || doc == null || body.Document == null) return;
				var total = doc.Blocks?.Count ?? 0;
				if (rendered >= total) return;
				appendto(Math.Min(total, rendered + CHUNK));
				applyrestore();
				pumpnext();
			}), DispatcherPriority.Background);
		}
		catch { }
	}

	/// <summary>把 [rendered, to) 这段接到文档末尾。</summary>
	void appendto(int to) {
		var skip = body.Document.Blocks.Count;
		makefd(rendered, to, body.Document, skip);
		rendered = to;
	}

	/// <summary>确保下标 idx 那块已经排进文档（查找跳转要用），返回那个块。</summary>
	Block ensureto(int idx) {
		var n = doc?.Blocks?.Count ?? 0;
		if (n == 0 || body.Document == null) return null;
		if (idx < 0) idx = 0;
		if (idx >= n) idx = n - 1;
		while (rendered <= idx && rendered < n) {
			var to = Math.Min(n, Math.Max(rendered + CHUNK, idx + 1));
			appendto(to);
		}
		return blockof(idx);
	}

	/// <summary>
	/// 排 [from, to) 这段块：into 为空时新建 FlowDocument，否则续在它末尾。
	/// skip 是续写前文档里已有的块数，只标新排进来的块。
	/// </summary>
	FlowDocument makefd(int from, int to, FlowDocument into, int skip) {
		var viewW = scroller.ActualWidth - PAD_X * 2;
		if (viewW < 120) viewW = 720;
		var pageW = Math.Max(160, viewW / zoom);
		var fd = MdFlowBuilder.BuildRange(doc, from, to, pageW, onlink, FilePath, true, into);
		if (into == null) {
			fd.FontSize = BASE_FONT * zoom;
			fd.LineHeight = BASE_FONT * zoom * 1.55;
		}
		tagnew(from, to, fd, skip);
		return fd;
	}

	/// <summary>
	/// 给新排进来的块打上它在全文里的下标，定位都靠它。
	/// 空行不生成块，所以按块类型逐个对，不能简单按顺序编号。
	/// </summary>
	void tagnew(int from, int to, FlowDocument fd, int skip) {
		if (doc?.Blocks == null || fd?.Blocks == null) return;
		var list = new List<Block>();
		var seen = 0;
		foreach (var b in fd.Blocks) {
			if (seen++ >= skip) list.Add(b);
		}
		if (list.Count == 0) return;
		var k = 0;
		for (var i = from; i < to && k < list.Count; i++) {
			if (doc.Blocks[i].Kind == MdBlockKind.Blank) continue;
			list[k++].Tag = i;
		}
		// 对不上（理论上不会发生）就按顺序编号，至少还能粗定位
		if (k != list.Count) {
			for (var j = 0; j < list.Count; j++) list[j].Tag = from + j;
		}
	}

	/// <summary>已排文档里下标为 idx 的块。</summary>
	Block blockof(int idx) {
		if (body.Document == null) return null;
		foreach (var blk in body.Document.Blocks)
			if (Equals(blk.Tag, idx)) return blk;
		return null;
	}

	/// <summary>视口顶部那块在全文里的下标，和它顶端比视口高出的像素。量不出返 -1。</summary>
	(int index, double offset) viewportanchor() {
		if (body.Document == null || body.Document.Blocks.Count == 0) return (-1, 0);
		try {
			var pt = scroller.TranslatePoint(new Point(PAD_X + 4, 8), body);
			var pos = body.GetPositionFromPoint(pt, true);
			var el = pos?.Paragraph ?? pos?.Parent as TextElement;
			while (el != null && el is not Block) el = el.Parent as TextElement;
			if (el is Block blk && blk.Tag is int idx && idx > 0) {
				var top = blk.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top;
				return (idx, top - scroller.VerticalOffset);
			}
		}
		catch { }
		return (-1, 0);
	}

	/// <summary>排到 anchor 那块，并把它挪回重排前相对视口的位置。</summary>
	void restoreanchor(int index, double offset) {
		var blk = ensureto(index);
		if (blk == null) return;
		try {
			body.UpdateLayout();
			var top = blk.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top;
			scroller.ScrollToVerticalOffset(top - offset);
		}
		catch { }
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
