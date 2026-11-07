using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace ScreenKit;

/// <summary>
/// 工具 → 文档阅读器。标题栏自绘：左边菜单、中间 tab 芯片、右边窗口按钮。
/// 正文交给 <see cref="DocView.IDocViewer"/>：Markdown 用 FlowDocument，docx/xlsx/pdf
/// 由各自的阅读器按需渲染，图片点开走 ImageOverlay 弹层。
/// </summary>
public partial class MdReadWindow : Window {
	/// <summary>一个 tab 的状态。Viewer 为空表示还没加载过，切到它时才建。</summary>
	sealed class Tab {
		public string Path = "";
		public string Name = "";
		public DocView.DocKind Kind = DocView.DocKind.Unknown;
		public DocView.IDocViewer Viewer;
		public FrameworkElement Body;
		public Border Head;
		public TextBlock Label;
		public string Error = "";
	}

	OcrOptions opt;
	Tab cur;
	readonly List<Tab> tabs = new();
	/// <summary>拖窗时点在标题栏空白处，先记下起点，超过阈值才真的拖。</summary>
	int titletick;
	Point titlepoint;
	bool findopen;
	string findtext = "";
	/// <summary>窗口还没显示时攒下的待开文件。</summary>
	readonly List<string> pending = new();
	/// <summary>历史文件与上次的标签，存 mdread.json。</summary>
	MdReadState state = new();
	/// <summary>state 是否已经从磁盘读过；没读过就写会把别人的历史抹掉。</summary>
	bool stateloaded;
	/// <summary>拆出去的子窗口（拖成独立窗口时建，关掉后自然消失）。</summary>
	static readonly List<MdReadWindow> livelist = new();
	/// <summary>true = 拖动标签拆出来的窗口，标签全关掉就自己关。</summary>
	bool secondary;

	// —— 标签拖拽 ——
	/// <summary>拖动超过这个距离才算拖，避免误触。</summary>
	const double TAB_DRAG_THRESHOLD = 6;
	Tab dragtab;
	FrameworkElement draghead;
	Point dragstart;
	Point draggrab;       // 抓点在芯片内的位置
	bool dragging;
	/// <summary>已经拆成独立窗口、窗口正跟手。</summary>
	bool dragfloated;
	/// <summary>拖拽期间正在跨窗搬标签，用来吞掉 LostCapture。</summary>
	bool dragmoving;

	public MdReadWindow(OcrOptions o) {
		opt = o ?? new OcrOptions();
		DocView.DocViewOptions.Current.UiFontSize = 12;
		DocView.DocViewOptions.Current.MdHeadingAutoNumber = false;
		InitializeComponent();
		applylang();
		initev();
		WindowEsc.Attach(this);
		iniwatch();
		livelist.Add(this);
		Closed += (_, _) => livelist.Remove(this);
	}

	/// <summary>拆出来的子窗口：不进历史恢复，标签关完自动关窗。</summary>
	MdReadWindow(OcrOptions o, bool isSecondary) : this(o) {
		secondary = isSecondary;
	}

	/// <summary>打开一批文件；命令行和外部调用共用。</summary>
	public void OpenFiles(IEnumerable<string> files) {
		if (files == null) return;
		var list = new List<string>(files);
		if (list.Count == 0) return;
		// 窗口还没 Loaded 时先攒着，等布局好了再建阅读器
		if (!IsLoaded) {
			pending.AddRange(list);
			return;
		}
		opendrop(list.ToArray());
	}

	/// <summary>把上次关窗时开着的标签恢复回来；只开第一个，其余按需重建。</summary>
	public void RestoreLastSession() {
		loadstate();
		var files = state.Tabs.Where(File.Exists).ToList();
		if (files.Count == 0) return;
		if (!IsLoaded) {
			pending.AddRange(files);
			return;
		}
		openrestored(files, state.ActiveTab);
	}

	/// <summary>读一次磁盘上的状态；只读一次，读过后就只在本进程里攒。</summary>
	void loadstate() {
		if (stateloaded) return;
		stateloaded = true;
		state = MdReadState.Load();
	}

	/// <summary>自测用：当前标签的阅读器状态文字。</summary>
	internal string CurrentStatus => cur?.Viewer?.StatusText ?? cur?.Error ?? "";

	/// <summary>自测用：Markdown 标签的排版进度（已排块数 / 总块数）。</summary>
	internal string CurrentDebug => (cur?.Viewer as DocView.MdViewer)?.DebugState() ?? "";

	/// <summary>自测用：当前 Markdown 标签是否已把全部块排进文档。</summary>
	internal bool CurrentAllRendered => cur?.Viewer is DocView.MdViewer md && md.AllRendered;

	/// <summary>自测用：滚到当前 Markdown 标签的末尾，返回滚动位置。</summary>
	internal double ScrollToEnd() =>
		cur?.Viewer is DocView.MdViewer md ? md.ScrollToEndForTest() : -1;

	/// <summary>自测用：把当前 Markdown 标签滚到指定位置。</summary>
	internal void ScrollTo(double v) {
		if (cur?.Viewer is DocView.MdViewer md) md.ScrollToForTest(v);
	}

	/// <summary>自测用：视口顶部那块在全文里的下标。</summary>
	internal int CurrentTopBlock => (cur?.Viewer as DocView.MdViewer)?.TopBlockForTest() ?? -1;

	/// <summary>自测用：按倍率缩放当前标签。</summary>
	internal void ZoomBy(double factor) => cur?.Viewer?.ZoomBy(factor);

	/// <summary>自测用：已打开的标签数。</summary>
	internal int TabCount => tabs.Count;

	/// <summary>自测用：当前标签是否已建好阅读器。</summary>
	internal bool CurrentReady => cur?.Viewer != null || (cur?.Error?.Length ?? 0) > 0;

	/// <summary>自测用：切到第 i 个标签。</summary>
	internal void SelectTab(int i) {
		if (i < 0 || i >= tabs.Count) return;
		activatetab(tabs[i]);
	}

	/// <summary>自测用：当前标签顺序。</summary>
	internal List<string> TabPaths => tabs.Select(t => t.Path).ToList();

	/// <summary>自测用：「历史文件」菜单里的路径。</summary>
	internal List<string> HistoryPaths =>
		mnhistory.Items.OfType<MenuItem>()
			.Where(m => m.Tag is string)
			.Select(m => (string)m.Tag)
			.ToList();

	/// <summary>自测用：把第 i 个标签搬到第 to 个位置（模拟条内拖动落点）。</summary>
	internal void MoveTab(int i, int to) {
		if (i < 0 || i >= tabs.Count) return;
		if (to < 0) to = 0;
		if (to > tabs.Count) to = tabs.Count;
		var t = tabs[i];
		tabs.RemoveAt(i);
		var adj = to > i ? to - 1 : to;
		tabs.Insert(Math.Min(adj, tabs.Count), t);
		updatetabs();
		remember();
	}

	/// <summary>自测用：把第 i 个标签拆成本窗之外的新窗口，返回新窗口。</summary>
	internal MdReadWindow DetachTabToWindow(int i) {
		if (i < 0 || i >= tabs.Count) return null;
		var t = tabs[i];
		detacht(t);
		var nw = new MdReadWindow(opt, isSecondary: true) { Width = 720, Height = 480 };
		nw.adopttab(t);
		nw.Show();
		return nw;
	}

	/// <summary>自测用：接收另一个窗口的第 i 个标签（模拟拖入合并）。</summary>
	internal bool PullTabFrom(MdReadWindow from, int i) {
		if (from == null || i < 0 || i >= from.tabs.Count) return false;
		var t = from.tabs[i];
		from.detacht(t);
		adopttab(t);
		return tabs.Contains(t);
	}

	/// <summary>自测用：历史里最多记几条。</summary>
	internal static int HistoryMax => MdReadState.HISTORY_MAX;

	/// <summary>自测用：把标签与捕获的现状打出来，定位「芯片不见了」这类问题。</summary>
	internal string DebugTabs() {
		var cap = Mouse.Captured;
		var capname = cap == null ? "null"
			: cap.GetType().Name + "/" + (cap as FrameworkElement)?.Name
				+ "/tag=" + ((cap as FrameworkElement)?.Tag is Tab ? "Tab" : "-");
		var chips = tabhost?.Children.Count ?? -1;
		var widths = new List<string>();
		if (tabhost != null) {
			foreach (var c in tabhost.Children)
				if (c is FrameworkElement fe) widths.Add($"{fe.ActualWidth:0}x{fe.ActualHeight:0}");
		}
		return $"tabs={tabs.Count} chips={chips} cur={(cur == null ? "null" : cur.Name)}"
			+ $" capture={capname} titlevis={ptitle?.Visibility} svtabs={(svtabs == null ? 0 : svtabs.ActualWidth):0}x{(svtabs == null ? 0 : svtabs.ActualHeight):0}"
			+ $" hoff={(svtabs == null ? 0 : svtabs.HorizontalOffset):0}/{(svtabs == null ? 0 : svtabs.ScrollableWidth):0}"
			+ $" chipx=[{chipxs()}]"
			+ $" chipwh=[{string.Join(",", widths)}] hamburger={(mnhamburger?.IsSubmenuOpen == true ? "open" : "closed")}"
			+ $" state={WindowState} left={Left:0} top={Top:0} drag={(dragtab == null ? "null" : "on")}";
	}

	/// <summary>每个芯片相对标签条左边缘的 X，负值或超出宽度就是看不见了。</summary>
	string chipxs() {
		var list = new List<string>();
		if (tabhost == null || svtabs == null) return "";
		foreach (var c in tabhost.Children) {
			if (c is not FrameworkElement fe) continue;
			try {
				var p = fe.TransformToAncestor(svtabs).Transform(new Point(0, 0));
				list.Add($"{p.X:0}");
			}
			catch { list.Add("?"); }
		}
		return string.Join(",", list);
	}

	/// <summary>自测用：芯片是否真的能看见（可见性、透明度、尺寸、父链）。</summary>
	internal string DebugChips() {
		var sb = new System.Text.StringBuilder();
		sb.Append($"tabhost={(tabhost == null ? "null" : tabhost.Children.Count.ToString())}");
		sb.Append($" hostvis={tabhost?.Visibility} hostopa={tabhost?.Opacity:0.##}");
		sb.Append($" svvis={svtabs?.Visibility} svopa={svtabs?.Opacity:0.##}");
		sb.Append($" svsize={svtabs?.ActualWidth:0}x{svtabs?.ActualHeight:0}");
		if (tabhost != null) {
			for (var i = 0; i < tabhost.Children.Count; i++) {
				if (tabhost.Children[i] is not FrameworkElement fe) { sb.Append($" [{i}]notFE"); continue; }
				var vis = fe.IsVisible ? "vis" : "HIDDEN";
				var parent = fe.Parent == null ? "parentNull" : fe.Parent.GetType().Name;
				sb.Append($" [{i}]{vis} opa={fe.Opacity:0.##} wh={fe.ActualWidth:0}x{fe.ActualHeight:0} p={parent}");
			}
		}
		return sb.ToString();
	}

	/// <summary>自测用：模拟 Ctrl+W（走和真按键同一条路径），返回关掉后的状态。</summary>
	internal string HotkeyClose() {
		var t = cur;
		onhotkey(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this),
			Environment.TickCount, Key.W) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
		Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
		return "closed=" + (t?.Name ?? "null") + " " + DebugTabs();
	}

	/// <summary>自测用：直接按住芯片走一遍拖拽判定的收尾，检查状态有没有留下垃圾。</summary>
	internal string DragProbe() {
		if (tabs.Count == 0) return "no tabs";
		var head = tabs[0].Head;
		if (head == null) return "no chip";
		var screen = head.PointToScreen(new Point(2, 2));
		dragtab = tabs[0];
		draghead = head;
		draggrab = new Point(2, 2);
		dragging = true;
		updatetabdrag(screen);
		endtabdrag(false);
		Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
		return "afterdrag " + DebugTabs();
	}

	/// <summary>自测用：把窗口内容渲染成 PNG，看排版是否真的有东西。</summary>
	internal bool ShotTo(string path) {
		try {
			UpdateLayout();
			var w = (int)Math.Round(ActualWidth);
			var h = (int)Math.Round(ActualHeight);
			if (w < 8 || h < 8) return false;
			var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
				w, h, 96, 96, PixelFormats.Pbgra32);
			rtb.Render(proot);
			var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
			enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
			using var fs = System.IO.File.Create(path);
			enc.Save(fs);
			return true;
		}
		catch { return false; }
	}

	/// <summary>自测用：当前标签正文里有没有图片。</summary>
	internal bool HasImage => cur?.Body != null && findimage(cur.Body) != null;

	/// <summary>
	/// 自测用：在当前 Markdown 标签里找第一个图片元素并触发它的双击放大，
	/// 确认 ImageOverlay 弹层真的开了。
	/// </summary>
	internal bool TestImageOverlay() {
		if (cur?.Body == null) return false;
		var img = findimage(cur.Body);
		if (img?.Source is not System.Windows.Media.Imaging.BitmapSource bmp) return false;
		try {
			DocView.ImageOverlay.ActiveHost = this;
			DocView.ImageOverlay.HostAnchor = cur.Body as FrameworkElement;
			DocView.ImageOverlay.Show(bmp, "自测图片", null);
			UpdateLayout();
			var ok = DocView.ImageOverlay.IsOpen;
			if (ok) {
				foreach (Window w in Application.Current.Windows) {
					if (w == this) continue;
					if (w.GetType().Name != "OverlayWindow") continue;
					UpdateLayout();
					// 弹层自己截一张，确认真的画出来了
					var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
						Math.Max(8, (int)w.ActualWidth), Math.Max(8, (int)w.ActualHeight),
						96, 96, PixelFormats.Pbgra32);
					rtb.Render(w);
					var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
					enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
					using var fs = System.IO.File.Create(
						System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenKit_docview_shot", "overlay.png"));
					enc.Save(fs);
					break;
				}
			}
			return ok;
		}
		catch (Exception ex) {
			CaptureLog.Ex("docview overlay", ex);
			return false;
		}
	}

	static System.Windows.Controls.Image findimage(DependencyObject d) {
		if (d is System.Windows.Controls.Image img) return img;
		var n = VisualTreeHelper.GetChildrenCount(d);
		for (var i = 0; i < n; i++) {
			var got = findimage(VisualTreeHelper.GetChild(d, i));
			if (got != null) return got;
		}
		return null;
	}

	void initev() {
		bopen.Click += (_, _) => pick();
		bprev.Click += (_, _) => navpage(-1);
		bnext.Click += (_, _) => navpage(1);
		bzoomout.Click += (_, _) => zoomby(1 / 1.15);
		bzoomin.Click += (_, _) => zoomby(1.15);
		bzoomfit.Click += (_, _) => cur?.Viewer?.ZoomFitWidth();
		bfind.Click += (_, _) => togglefind();
		bfindprev.Click += (_, _) => findnext(false);
		bfindnext.Click += (_, _) => findnext(true);
		epage.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			gotoPage();
		};
		epage.LostFocus += (_, _) => syncpagebox();
		bmin.Click += (_, _) => WindowState = WindowState.Minimized;
		bmax.Click += (_, _) => togglemax();
		bclosewin.Click += (_, _) => Close();
		mnopen.Click += (_, _) => pick();
		mnclose.Click += (_, _) => closetab(cur);
		mncloseall.Click += (_, _) => closeall();
		mnreload.Click += (_, _) => reload();
		mnpath.Click += (_, _) => copytext(cur?.Path);
		mnsysopen.Click += (_, _) => sysopen(cur?.Path);
		mnword.Click += (_, _) => export(true);
		mnpdf.Click += (_, _) => export(false);
		PreviewDragOver += (_, e) => {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		Drop += (_, e) => {
			if (e.Data.GetData(DataFormats.FileDrop) is string[] files) opendrop(files);
		};
		StateChanged += (_, _) => {
			if (bmax != null) {
				var maxed = WindowState == WindowState.Maximized;
				bmax.Content = maxed ? "❐" : "□";
				bmax.ToolTip = maxed ? "还原" : "最大化";
			}
			try {
				var chrome = WindowChrome.GetWindowChrome(this);
				if (chrome != null)
					chrome.ResizeBorderThickness = WindowState == WindowState.Maximized
						? new Thickness(0) : new Thickness(8, 6, 8, 6);
				proot.BorderThickness = WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(1);
			}
			catch { }
		};
		Loaded += (_, _) => {
			if (pending.Count > 0) {
				var queued = pending.ToArray();
				pending.Clear();
				opendrop(queued);
				return;
			}
			if (tabs.Count == 0) showempty();
			buildhistorymenu();
		};
		efind.TextChanged += (_, _) => {
			findtext = efind.Text ?? "";
			if (findtext.Length == 0) cur?.Viewer?.ClearFind();
		};
		efind.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.Enter) {
				e.Handled = true;
				findnext((Keyboard.Modifiers & ModifierKeys.Shift) == 0);
			}
			else if (e.Key == Key.Escape) {
				e.Handled = true;
				togglefind();
			}
		};
		Closed += (_, _) => {
			remember();
			foreach (var t in tabs) dispose(t);
			DocView.ImageOverlay.CloseIfOpen();
		};
		// Ctrl+W / Ctrl+F4 关当前标签；Ctrl+F 打开查找
		PreviewKeyDown += (_, e) => onhotkey(e);
	}

	void onhotkey(KeyEventArgs e) {
		if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
		if (e.Key is Key.W or Key.F4) {
			e.Handled = true;
			if (cur != null) closetab(cur);
			return;
		}
		if (e.Key == Key.F) {
			e.Handled = true;
			if (!findopen) togglefind();
			else {
				efind.Focus();
				efind.SelectAll();
			}
		}
	}

	void applylang() {
		Title = Loc.T("mdread.title");
		mnhamburger.ToolTip = Loc.T("mdread.menu");
		mnopen.Header = Loc.T("mdread.open");
		mnclose.Header = Loc.T("mdread.tabclose");
		mnclose.InputGestureText = "Ctrl+W";
		mncloseall.Header = Loc.T("mdread.closeall");
		mnreload.Header = Loc.T("mdread.reload");
		mnpath.Header = Loc.T("mdread.copypath");
		mnsysopen.Header = Loc.T("mdread.sysopen");
		mnword.Header = Loc.T("mdread.word");
		mnpdf.Header = Loc.T("mdread.pdf");
		// 工具栏按钮都是图标，文字只放在提示和自动化名里；这里不要再给 bopen 设 Content，
		// 否则会盖掉 XAML 里的图标，在左端露出半个字
		bprev.ToolTip = Loc.T("mdread.prevpage");
		bnext.ToolTip = Loc.T("mdread.nextpage");
		bzoomout.ToolTip = Loc.T("mdread.zoomout");
		bzoomin.ToolTip = Loc.T("mdread.zoomin");
		bzoomfit.ToolTip = Loc.T("mdread.zoomfit");
		bfind.ToolTip = Loc.T("mdread.find");
		efind.ToolTip = Loc.T("mdread.find");
		showstat();
	}

	void pick() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("mdread.open"),
			Filter = DocView.DocKindUtil.Filter,
			Multiselect = true,
		};
		if (ofd.ShowDialog(this) != true) return;
		opendrop(ofd.FileNames);
	}

	void opendrop(string[] files) {
		if (files == null || files.Length == 0) return;
		var opened = 0;
		var bad = 0;
		foreach (var f in files) {
			if (openone(MdRead.Resolve(f))) opened++;
			else bad++;
		}
		if (opened == 0) {
			lbstat.Text = Loc.T("mdread.bad");
			return;
		}
		updatetabs();
		// 一次拖入多个：最后进来的那个留在前台，前面的都在 tab 里。
		activatetab(tabs[tabs.Count - 1]);
		remember();
		if (bad > 0) lbstat.Text = string.Format(Loc.T("mdread.bad.part"), opened, bad);
	}

	/// <summary>恢复上次的标签：一次建齐，选中上次看的那个。</summary>
	void openrestored(List<string> files, int active) {
		var opened = 0;
		foreach (var f in files) {
			if (openone(f)) opened++;
		}
		if (opened == 0) return;
		updatetabs();
		activatetab(tabs[Math.Min(Math.Max(0, active), tabs.Count - 1)]);
	}

	/// <summary>记住当前标签与历史，写 mdread.json。子窗口不写，免得把主窗状态盖掉。</summary>
	void remember() {
		if (secondary) return;
		try {
			// 没读过磁盘就写，会把上次的历史和标签覆盖成空的
			loadstate();
			state.History.RemoveAll(p => !File.Exists(p));
			foreach (var t in tabs) state.Touch(t.Path);
			var active = cur == null ? 0 : Math.Max(0, tabs.IndexOf(cur));
			state.RememberTabs(tabs.Select(t => t.Path), active);
			state.Save();
			buildhistorymenu();
		}
		catch { }
	}

	/// <summary>重建「历史文件」子菜单；已开着的排前面并标出来。</summary>
	void buildhistorymenu() {
		if (mnhistory == null) return;
		loadstate();
		for (var i = mnhistory.Items.Count - 1; i >= 0; i--) {
			if (!ReferenceEquals(mnhistory.Items[i], mnhistempty))
				mnhistory.Items.RemoveAt(i);
		}
		var any = false;
		foreach (var path in state.History) {
			if (!File.Exists(path)) continue;
			var open = tabs.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
			var mi = new MenuItem {
				Header = open ? "✓ " + compact(path) : compact(path),
				ToolTip = path,
				Tag = path,
				IsEnabled = !open,
			};
			mi.Click += (_, _) => openhistory(path);
			mnhistory.Items.Add(mi);
			if (!any) {
				mnhistory.Items.Remove(mnhistempty);
				any = true;
			}
		}
		if (!any && !mnhistory.Items.Contains(mnhistempty))
			mnhistory.Items.Add(mnhistempty);
		mnhistempty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
	}

	/// <summary>菜单里显示「文件名 — 上级目录」，太长再收一收。</summary>
	static string compact(string path) {
		try {
			var name = System.IO.Path.GetFileName(path);
			var dir = System.IO.Path.GetDirectoryName(path) ?? "";
			var up = dir.Length == 0 ? "" : System.IO.Path.GetFileName(dir.TrimEnd('\\', '/'));
			var s = up.Length > 0 ? name + " — " + up : name;
			return s.Length > 60 ? s.Substring(0, 58) + "…" : s;
		}
		catch { return path ?? ""; }
	}

	void openhistory(string path) {
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
		if (!openone(path)) {
			lbstat.Text = Loc.T("mdread.bad");
			return;
		}
		activatetab(tabs[tabs.Count - 1]);
		remember();
	}

	/// <summary>同一个文件已经开着就直接切过去，不重复解析。</summary>
	bool openone(string path) {
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
		var kind = DocView.DocKindUtil.FromPath(path);
		if (kind == DocView.DocKind.Unknown) return false;
		// 浏览器 / 终端标签不在这扇窗里开
		if (kind == DocView.DocKind.Browser || kind == DocView.DocKind.Console) return false;
		foreach (var t in tabs) {
			if (string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) {
				activatetab(t);
				return true;
			}
		}
		// PDF 要用 pdfium 逐页出图；精简包没装时先让用户装
		if (kind == DocView.DocKind.Pdf && !NativeRuntime.HasPdfium() && !FeaturePrompt.EnsurePdf(this))
			return false;
		// Word / Excel 的解析库按需安装，不随发布包发
		if (!ensuredoclib(path, kind)) return false;
		tabs.Add(new Tab {
			Path = path,
			Name = System.IO.Path.GetFileName(path),
			Kind = kind,
		});
		return true;
	}

	/// <summary>
	/// Word / Excel 的解析库按需安装，不随发布包发：
	/// docx / xlsx 要 OpenXml，doc / xls 要 NPOI。md、pdf、文本、图片、csv 都不依赖它们。
	/// </summary>
	bool ensuredoclib(string path, DocView.DocKind kind) {
		if (kind is not (DocView.DocKind.Docx or DocView.DocKind.Xlsx)) return true;
		var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
		var legacy = ext is ".doc" or ".xls";
		if (!legacy && !NativeRuntime.HasOfficeOoxml() && !FeaturePrompt.EnsureDocOfficeOoxml(this))
			return false;
		if (legacy && !NativeRuntime.HasOfficeLegacy() && !FeaturePrompt.EnsureDocOfficeLegacy(this))
			return false;
		return true;
	}

	/// <summary>懒加载：切到某个 tab 时才建阅读器、读文件。</summary>
	void activatetab(Tab t) {
		if (t == null) return;
		cur = t;
		Title = t.Name + " - " + Loc.T("mdread.title");
		showtabstate();
		ensureviewer(t);
		showpage(t);
	}

	void ensureviewer(Tab t) {
		if (t.Viewer != null || t.Error.Length > 0) return;
		try {
			var v = DocView.ViewerFactory.Create(t.Kind);
			DocView.ImageOverlay.ActiveHost = this;
			DocView.ImageOverlay.HostAnchor = v.View as FrameworkElement;
			v.Load(t.Path);
			v.StatusChanged += () => {
				if (ReferenceEquals(t, cur)) showstat();
			};
			t.Viewer = v;
			t.Body = v.View;
		}
		catch (Exception ex) {
			t.Error = describe(ex);
			CaptureLog.Ex("docview " + t.Name, ex);
		}
	}

	/// <summary>把阅读器抛的异常翻成一句能看懂的话；认不出的退回原文。</summary>
	static string describe(Exception ex) {
		var m = ex?.Message ?? "";
		if (string.IsNullOrWhiteSpace(m)) return Loc.T("mdread.empty");
		return m;
	}

	void showpage(Tab t) {
		phost.Content = t?.Body
			?? (t != null && t.Error.Length > 0 ? errored(t.Error) : null);
		phost.Visibility = phost.Content == null ? Visibility.Collapsed : Visibility.Visible;
		showstat();
	}

	FrameworkElement errored(string text) {
		return new TextBlock {
			Text = text,
			Margin = new Thickness(24),
			TextWrapping = TextWrapping.Wrap,
			Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0x99)),
			VerticalAlignment = VerticalAlignment.Top,
		};
	}

	void showempty() {
		phost.Content = errored(Loc.T("mdread.hint"));
		phost.Visibility = Visibility.Visible;
		showstat();
	}

	// ==================== 标题栏 tab 芯片 ====================
	#region
	/// <summary>
	/// 重建标签条。每个芯片单独兜底：某个标签建不出来时不能连累后面的，
	/// 否则整条标签栏会空着（状态栏却还说着「共 N 个标签」）。
	/// </summary>
	void updatetabs() {
		try {
			tabhost.Children.Clear();
			foreach (var t in tabs) {
				try {
					var chip = maketab(t);
					t.Head = chip;
					tabhost.Children.Add(chip);
				}
				catch (Exception ex) {
					DocView.DocViewLog.Error($"maketab {t?.Name}", ex);
					CaptureLog.Ex("mdread maketab", ex);
					// 退一步也要有个能点的芯片，免得这一个标签彻底消失
					tabhost.Children.Add(maketabfallback(t));
				}
			}
			tabhost.UpdateLayout();
			showstat();
		}
		catch (Exception ex) {
			DocView.DocViewLog.Error($"updatetabs tabs={tabs.Count}", ex);
			CaptureLog.Ex("mdread updatetabs", ex);
		}
	}

	void iniwatch() {
		// 标签栏和标签列表不一致时重建一次：这能自愈「有标签但栏是空的」
		tabhost.LayoutUpdated += (_, _) => {
			try {
				if (tabs.Count != tabhost.Children.Count && tabs.Count > 0) {
					if (Environment.TickCount - lastheal < 400) return;
					lastheal = Environment.TickCount;
					DocView.DocViewLog.Warn(
						$"tab strip out of sync: tabs={tabs.Count} chips={tabhost.Children.Count}, rebuilding");
					Dispatcher.BeginInvoke(new Action(updatetabs),
						System.Windows.Threading.DispatcherPriority.Background);
				}
			}
			catch { }
		};
	}

	/// <summary>上次自愈的时间，避免 LayoutUpdated 里反复重建。</summary>
	int lastheal;

	/// <summary>芯片建失败时的最小替代：只剩文件名，点了能切过去。</summary>
	Border maketabfallback(Tab t) {
		var bd = new Border {
			Child = new TextBlock {
				Text = t?.Name ?? "?",
				FontSize = 12,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(10, 0, 10, 0),
			},
			Padding = new Thickness(0),
			Tag = t,
			VerticalAlignment = VerticalAlignment.Stretch,
			Background = Brushes.Transparent,
			Cursor = Cursors.Hand,
		};
		bd.MouseLeftButtonDown += (_, e) => {
			if (t != null) activatetab(t);
			e.Handled = true;
		};
		return bd;
	}

	/// <summary>一个芯片：文件名 + 关闭叉。标题栏属于非客户区，芯片要单独标可命中才能点。</summary>
	Border maketab(Tab t) {
		var title = t.Name.Length > 26 ? t.Name.Substring(0, 24) + "…" : t.Name;
		var label = new TextBlock {
			Text = title,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 4, 0),
			MaxWidth = 168,
			FontSize = 12,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x44)),
		};
		t.Label = label;
		var bclose = new Button {
			Content = "✕",
			Style = (Style)FindResource("CloseTabBtn"),
			ToolTip = Loc.T("mdread.tabclose"),
			Tag = t,
			VerticalAlignment = VerticalAlignment.Center,
		};
		System.Windows.Automation.AutomationProperties.SetAutomationId(bclose, "mdread.tabclose");
		System.Windows.Automation.AutomationProperties.SetName(bclose, Loc.T("mdread.tabclose"));
		bclose.Click += (_, e) => {
			e.Handled = true;
			closetab(t);
		};
		var panel = new StackPanel {
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
		};
		panel.Children.Add(label);
		panel.Children.Add(bclose);
		var bd = new Border {
			Child = panel,
			Tag = t,
			Style = (Style)FindResource("TitleTab"),
			ToolTip = t.Path,
		};
		WindowChrome.SetIsHitTestVisibleInChrome(bd, true);
		System.Windows.Automation.AutomationProperties.SetAutomationId(bd, "mdread.tab");
		bd.PreviewMouseLeftButtonDown += (_, e) => {
			if (e.ChangedButton != MouseButton.Left) return;
			// 点关闭叉不切 tab
			var d = e.OriginalSource as DependencyObject;
			while (d != null) {
				if (ReferenceEquals(d, bclose)) return;
				d = VisualTreeHelper.GetParent(d);
			}
			if (!ReferenceEquals(cur, t)) {
				activatetab(t);
				showstat();
			}
			// 记下抓点，鼠标移动超过阈值才开始拖（和 docview 一样用 null 参照，窗口挪了也不影响）
			dragtab = t;
			draghead = bd;
			dragstart = e.GetPosition(null);
			draggrab = e.GetPosition(bd);
			dragging = false;
			dragfloated = false;
			try { bd.CaptureMouse(); } catch { }
			e.Handled = true;
		};
		bd.PreviewMouseMove += (_, e) => {
			if (dragtab == null || !ReferenceEquals(dragtab, t)) return;
			if (e.LeftButton != MouseButtonState.Pressed) { endtabdrag(commit: false); return; }
			if (!dragging) {
				var pos = e.GetPosition(null);
				var dx = pos.X - dragstart.X;
				var dy = pos.Y - dragstart.Y;
				if (dx * dx + dy * dy < TAB_DRAG_THRESHOLD * TAB_DRAG_THRESHOLD) return;
				dragging = true;
				bd.Opacity = 0.72;
				bd.Cursor = Cursors.SizeAll;
				Panel.SetZIndex(bd, 100);
			}
			updatetabdrag(bd.PointToScreen(e.GetPosition(bd)));
			e.Handled = true;
		};
		bd.PreviewMouseLeftButtonUp += (_, e) => {
			if (dragtab == null || !ReferenceEquals(dragtab, t)) return;
			var screen = bd.PointToScreen(e.GetPosition(bd));
			var was = dragging;
			endtabdrag(commit: was, screen);
			e.Handled = true;
		};
		bd.LostMouseCapture += (_, _) => {
			// 跨窗搬标签时会主动放掉捕获，别当取消
			if (dragmoving) return;
			if (dragtab != null) endtabdrag(commit: false);
		};
		bd.MouseDown += (_, e) => {
			if (e.ChangedButton != MouseButton.Middle) return;
			closetab(t);
			e.Handled = true;
		};
		t.Head = bd;
		paintchip(t);
		return bd;
	}

	void showtabstate() {
		foreach (var t in tabs) paintchip(t);
	}

	// ---------- 标签拖拽：条内排序 / 拖出拆窗 / 拖回合并 ----------

	/// <summary>除自己之外还有没有开着的阅读器窗口（有才能把标签拖过去合并）。</summary>
	bool hasotherwindow() {
		foreach (var w in livelist) {
			if (w == null || ReferenceEquals(w, this)) continue;
			if (w.IsVisible) return true;
		}
		return false;
	}

	/// <summary>拖动中：判断光标在哪个窗口的标签条上，决定排序、拆窗还是合并。</summary>
	void updatetabdrag(Point screen) {
		if (dragtab == null) return;
		// 已拆成独立窗：窗口跟手；再拖到别的窗口标签条上就并回去
		if (dragfloated) {
			movefloatwin(screen);
			var over = hitwindow(screen, except: this);
			if (over != null) mergetowindow(over, screen);
			return;
		}
		var hit = hitwindow(screen);
		if (hit == null) {
			// 离开标签条：本窗还有别的标签就拆出去；只剩一个则不拆（否则留下空窗）
			if (tabs.Count > 1) floatout(screen);
			return;
		}
		if (ReferenceEquals(hit, this)) {
			// 本窗内：只挪芯片位置与顺序，不重建芯片（重建会把捕获弄丢）
			livereorder(insertindex(this, screen), screen);
		}
		else {
			mergetowindow(hit, screen);
		}
	}

	/// <summary>条内实时排序：改 tabs 顺序并只挪 tabhost 的子元素，不重建芯片。</summary>
	void livereorder(int newIndex, Point screen) {
		var t = dragtab;
		if (t == null) return;
		var old = tabs.IndexOf(t);
		if (old >= 0) {
			var adj = newIndex > old ? newIndex - 1 : newIndex;
			if (adj < 0) adj = 0;
			if (adj > tabs.Count - 1) adj = tabs.Count - 1;
			if (adj != old) {
				tabs.RemoveAt(old);
				tabs.Insert(adj, t);
				// 只挪芯片，别重建
				var chip = t.Head;
				if (chip != null && tabhost.Children.Contains(chip)) {
					tabhost.Children.Remove(chip);
					if (adj >= tabhost.Children.Count) tabhost.Children.Add(chip);
					else tabhost.Children.Insert(adj, chip);
					Panel.SetZIndex(chip, 100);
				}
			}
		}
		followdraggedtab(screen);
	}

	/// <summary>让被拖的芯片跟着光标走（TranslateTransform，不改布局）。</summary>
	void followdraggedtab(Point screen) {
		var chip = draghead;
		if (chip == null) return;
		try {
			if (chip.RenderTransform is not TranslateTransform tt) {
				tt = new TranslateTransform();
				chip.RenderTransform = tt;
			}
			tt.X = 0;
			tt.Y = 0;
			// 布局位置（清零后取）
			var layout = chip.TransformToAncestor(tabhost).Transform(new Point(0, 0));
			var mouse = tabhost.PointFromScreen(screen);
			tt.X = mouse.X - draggrab.X - layout.X;
			tt.Y = -2;
		}
		catch { }
	}

	/// <summary>先在光标所在处拆出一扇新窗，窗口顶边贴住光标（之后只有整窗拖动才会动它）。</summary>
	void floatout(Point screen) {
		var t = dragtab;
		if (t == null || dragfloated || tabs.Count <= 1) return;
		try {
			dragmoving = true;
			if (draghead != null) {
				try { draghead.ReleaseMouseCapture(); } catch { }
				draghead.Opacity = 1;
				draghead.Cursor = Cursors.Arrow;
				draghead.RenderTransform = Transform.Identity;
				Panel.SetZIndex(draghead, 0);
			}
			dragtab = null;
			draghead = null;
			dragging = false;
			dragfloated = false;
			var grab = draggrab;
			detacht(t);
			var w = Math.Max(720, ActualWidth * 0.85);
			var h = Math.Max(480, ActualHeight * 0.85);
			var dip = pointtodip(screen);
			var nw = new MdReadWindow(opt, isSecondary: true) {
				Width = w,
				Height = h,
				WindowStartupLocation = WindowStartupLocation.Manual,
				ShowInTaskbar = true,
				// 新窗的标签条落在光标下方一点，光标仍在标签条容错区内，接着拖就能并回去
				Left = dip.X - Math.Min(grab.X, 60),
				Top = dip.Y - Math.Min(grab.Y, 14),
			};
			nw.Show();
			nw.adopttab(t);
			nw.takefloatdrag(t, new Point(Math.Min(grab.X, 60), Math.Min(grab.Y, 14)), floated: true);
			nw.Activate();
			remember();
		}
		catch (Exception ex) {
			DocView.DocViewLog.Error("floatout", ex);
		}
		finally {
			dragmoving = false;
		}
	}

	/// <summary>
	/// 接手一个正在拖的标签。floated=true 表示这是刚拆出来的窗，窗口要跟着光标走；
	/// 并到别的窗里之后传 false，只做条内排序，窗口不动。
	/// </summary>
	void takefloatdrag(Tab t, Point grabInHeader, bool floated) {
		dragtab = t;
		draghead = t?.Head;
		dragging = true;
		dragfloated = floated;
		draggrab = grabInHeader;
		dragstart = new Point(0, 0);
		if (draghead != null) {
			draghead.Opacity = 0.72;
			draghead.Cursor = Cursors.SizeAll;
			Panel.SetZIndex(draghead, 100);
			try { draghead.CaptureMouse(); } catch { }
		}
	}

	/// <summary>拆出来的窗口跟手（只有这一步会动窗口）。</summary>
	void movefloatwin(Point screen) {
		try {
			var dip = pointtodip(screen);
			Left = dip.X - draggrab.X;
			Top = dip.Y - draggrab.Y;
		}
		catch { }
	}

	/// <summary>把正在拖的标签并到另一个窗口的标签条上。</summary>
	void mergetowindow(MdReadWindow target, Point screen) {
		var t = dragtab;
		if (t == null || target == null || ReferenceEquals(target, this)) return;
		var grab = draggrab;
		try {
			dragmoving = true;
			if (draghead != null) {
				try { draghead.ReleaseMouseCapture(); } catch { }
				draghead.Opacity = 1;
				draghead.Cursor = Cursors.Arrow;
				draghead.RenderTransform = Transform.Identity;
				Panel.SetZIndex(draghead, 0);
			}
			dragtab = null;
			draghead = null;
			dragging = false;
			dragfloated = false;
			var at = insertindex(target, screen);
			detacht(t);
			target.adopttab(t, at);
			// 并过去后由目标窗继续条内拖动，窗口不再跟着走
			target.takefloatdrag(t, grab, floated: false);
			target.Activate();
			remember();
			if (tabs.Count == 0 && secondary) Close();
		}
		catch (Exception ex) {
			DocView.DocViewLog.Error("mergetowindow", ex);
		}
		finally {
			dragmoving = false;
		}
	}

	/// <summary>
	/// 光标落在哪个阅读器窗口的标签条上。排除自己，并优先取最新的窗口
	/// （拆出去的新窗通常压在旧窗上面，位置又几乎重叠）。
	/// </summary>
	static MdReadWindow hitwindow(Point screen, MdReadWindow except = null) {
		var list = livelist.ToList();
		list.Reverse();
		foreach (var w in list) {
			if (w == null || ReferenceEquals(w, except)) continue;
			if (!w.IsVisible || w.tabhost == null) continue;
			if (overtabstrip(w, screen)) return w;
		}
		return null;
	}

	/// <summary>光标是否压在这个窗口的标签条上（含放大后的容错区）。</summary>
	static bool overtabstrip(MdReadWindow w, Point screen) {
		try {
			var el = (FrameworkElement)w.svtabs ?? w.tabhost;
			if (el == null || !el.IsVisible) return false;
			var tl = el.PointToScreen(new Point(0, 0));
			var br = el.PointToScreen(new Point(Math.Max(1, el.ActualWidth), Math.Max(30, el.ActualHeight)));
			// 容错区：上下各让出一截，拖的时候手一抖也还算在标签条上
			return screen.X >= tl.X - 16 && screen.X <= br.X + 16
				&& screen.Y >= tl.Y - 14 && screen.Y <= br.Y + 18;
		}
		catch { return false; }
	}

	/// <summary>标签条上的插入位置（按芯片中线左右判断）。</summary>
	static int insertindex(MdReadWindow w, Point screen) {
		var n = w?.tabhost?.Children.Count ?? 0;
		if (n == 0) return 0;
		for (var i = 0; i < n; i++) {
			if (w.tabhost.Children[i] is not FrameworkElement fe) continue;
			// 被拖的那个芯片有跟手位移，算插入点时要按布局位置，否则会来回抖
			if (ReferenceEquals(fe, w.draghead)) continue;
			try {
				var tl = fe.PointToScreen(new Point(0, 0));
				if (screen.X < tl.X + fe.ActualWidth * 0.5) return i;
			}
			catch { }
		}
		return n;
	}

	/// <summary>清掉拖拽状态，不动标签本身。捕获一定要放，否则整扇窗之后收不到鼠标。</summary>
	void cleardragstate() {
		if (draghead != null) {
			draghead.Opacity = 1;
			draghead.Cursor = Cursors.Arrow;
			draghead.RenderTransform = Transform.Identity;
			Panel.SetZIndex(draghead, 0);
			try {
				if (ReferenceEquals(Mouse.Captured, draghead)) draghead.ReleaseMouseCapture();
			}
			catch { }
		}
		// 芯片被重建过时 draghead 可能已脱离，按全局清一次更稳
		try {
			if (Mouse.Captured is Border b && b.Tag is Tab) Mouse.Capture(null);
		}
		catch { }
		dragtab = null;
		draghead = null;
		dragging = false;
		dragfloated = false;
	}

	void endtabdrag(bool commit, Point screen = default) {
		var t = dragtab;
		if (t == null) return;
		if (commit && dragfloated) {
			// 松手时窗口已经独立，保持现状
			cleardragstate();
			remember();
			return;
		}
		if (commit && dragging) {
			var hit = hitwindow(screen);
			if (hit != null && !ReferenceEquals(hit, this)) {
				mergetowindow(hit, screen);
				return;
			}
		}
		cleardragstate();
		remember();
	}

	/// <summary>从本窗摘掉一个标签，交出去之前保留阅读器实例，避免重新解析。</summary>
	void detacht(Tab t) {
		var i = tabs.IndexOf(t);
		if (i < 0) return;
		tabs.RemoveAt(i);
		if (ReferenceEquals(cur, t)) {
			cur = null;
			phost.Content = null;
		}
		updatetabs();
		if (tabs.Count == 0) {
			Title = Loc.T("mdread.title");
			showempty();
		}
		else if (cur == null)
			activatetab(tabs[Math.Min(i, tabs.Count - 1)]);
	}

	/// <summary>接收外来标签：沿用已经建好的阅读器，插到指定位置并选中。</summary>
	void adopttab(Tab t, int index = -1) {
		if (t == null) return;
		if (index < 0 || index > tabs.Count) tabs.Add(t);
		else tabs.Insert(index, t);
		updatetabs();
		activatetab(t);
	}

	/// <summary>屏幕像素转 DIP。拆窗时按光标定位、跨窗判命中都要用。</summary>
	static Point pointtodip(Window w, Point screen) {
		try {
			var src = PresentationSource.FromVisual(w);
			if (src?.CompositionTarget != null)
				return src.CompositionTarget.TransformFromDevice.Transform(screen);
		}
		catch { }
		return screen;
	}

	Point pointtodip(Point screen) => pointtodip(this, screen);

	void paintchip(Tab t) {
		if (t?.Head == null) return;
		var on = ReferenceEquals(t, cur);
		t.Head.Style = (Style)FindResource(on ? "TitleTabActive" : "TitleTab");
	}

	/// <summary>
	/// 关一个标签。真正的移除要等这一轮输入事件走完再做：关闭叉是 Button，
	/// 在它自己的 Click 里把按钮从树里摘掉，WPF 的鼠标捕获会留在这个已经脱离的
	/// 按钮上，之后整扇窗收不到鼠标输入，看起来就是卡死、关不掉。
	/// 中键关标签同理（在 MouseDown 里摘掉自己）。
	/// </summary>
	void closetab(Tab t) {
		if (t == null) return;
		if (tabs.IndexOf(t) < 0) return;
		Mouse.Capture(null);
		Dispatcher.BeginInvoke(new Action(() => removetab(t)),
			System.Windows.Threading.DispatcherPriority.Background);
	}

	void removetab(Tab t) {
		if (t == null) return;
		var i = tabs.IndexOf(t);
		if (i < 0) return;
		tabs.RemoveAt(i);
		dispose(t);
		updatetabs();
		if (tabs.Count == 0) {
			cur = null;
			Title = Loc.T("mdread.title");
			showempty();
			// 拖出来的子窗标签关完就自己关，别留个空窗
			if (secondary) Close();
			return;
		}
		activatetab(tabs[Math.Min(i, tabs.Count - 1)]);
	}

	void closeall() {
		foreach (var t in tabs) dispose(t);
		tabs.Clear();
		cur = null;
		Title = Loc.T("mdread.title");
		updatetabs();
		showempty();
	}

	void dispose(Tab t) {
		if (t?.Viewer == null) return;
		try { t.Viewer.Dispose(); } catch { }
		t.Viewer = null;
		t.Body = null;
	}

	void reload() {
		if (cur == null) return;
		var t = cur;
		var was = t.Viewer;
		// 记住阅读位置，重载后回到原处
		double h = 0, v = 0, z = 0;
		var page = 0;
		try { was.CaptureViewState(out h, out v, out z, out page); } catch { }
		dispose(t);
		t.Error = "";
		ensureviewer(t);
		showpage(t);
		if (t.Viewer == null) return;
		try { t.Viewer.RestoreViewState(h, v, z, page); } catch { }
	}
	#endregion

	// ==================== 工具栏动作 ====================
	#region
	void navpage(int delta) {
		var v = cur?.Viewer;
		if (v == null) return;
		if (delta < 0) v.GoPrevPage();
		else v.GoNextPage();
		showstat();
	}

	void zoomby(double factor) {
		var v = cur?.Viewer;
		if (v == null) return;
		v.ZoomBy(factor);
		showstat();
	}

	void togglefind() {
		findopen = !findopen;
		efind.Visibility = findopen ? Visibility.Visible : Visibility.Collapsed;
		bfindprev.Visibility = findopen ? Visibility.Visible : Visibility.Collapsed;
		bfindnext.Visibility = findopen ? Visibility.Visible : Visibility.Collapsed;
		if (!findopen) {
			efind.Text = "";
			cur?.Viewer?.ClearFind();
			findtext = "";
			return;
		}
		efind.Focus();
		efind.SelectAll();
	}

	/// <summary>页码框回车跳到该页。</summary>
	void gotoPage() {
		var v = cur?.Viewer;
		if (v == null) { syncpagebox(); return; }
		if (!int.TryParse((epage.Text ?? "").Trim(), out var n) || n < 1) { syncpagebox(); return; }
		n = Math.Min(n, Math.Max(1, v.PageCount));
		v.GoToPage(n);
		syncpagebox();
		showstat();
	}

	/// <summary>把阅读器的当前页写回页码框。</summary>
	void syncpagebox() {
		var v = cur?.Viewer;
		if (v == null) {
			epage.Text = "";
			lbpage.Text = "/ 0";
			return;
		}
		var n = v.PageCount;
		epage.Text = n > 0 ? Math.Max(1, v.CurrentPage).ToString() : "";
		lbpage.Text = "/ " + Math.Max(0, n);
		epage.IsEnabled = n > 0;
		bprev.IsEnabled = n > 1;
		bnext.IsEnabled = n > 1;
	}

	void findnext(bool forward) {
		var v = cur?.Viewer;
		if (v == null) return;
		var text = findtext;
		if (string.IsNullOrWhiteSpace(text)) return;
		var r = v.Find(text, forward, true);
		lbstat.Text = r.Found
			? string.Format(Loc.T("mdread.find.hit"), r.Current, r.Total)
			: Loc.T("mdread.find.miss");
	}

	/// <summary>标题栏空白处拖动/双击最大化；点在按钮、菜单、芯片上时不动窗。</summary>
	void ontitledrag(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton != MouseButton.Left) return;
		if (istitleinteractive(e.OriginalSource as DependencyObject)) return;
		if (e.ClickCount == 2) {
			togglemax();
			titletick = 0;
			e.Handled = true;
			return;
		}
		var pos = e.GetPosition(this);
		if (titletick != 0 && Environment.TickCount - titletick < 400
			&& Math.Abs(pos.X - titlepoint.X) < 3 && Math.Abs(pos.Y - titlepoint.Y) < 3) {
			e.Handled = true;
			return;
		}
		titletick = Environment.TickCount;
		titlepoint = pos;
		try {
			if (Mouse.LeftButton != MouseButtonState.Pressed) return;
			if (WindowState == WindowState.Maximized) {
				// 最大化状态下拖出：先还原，再让窗口跟着鼠标
				var mouse = PointToScreen(e.GetPosition(this));
				var ratio = SystemParameters.PrimaryScreenWidth > 1
					? mouse.X / SystemParameters.PrimaryScreenWidth : 0.5;
				WindowState = WindowState.Normal;
				Left = mouse.X - ActualWidth * ratio;
				Top = Math.Max(0, mouse.Y - 16);
			}
			DragMove();
		}
		catch { }
	}

	/// <summary>
	/// 标题栏里哪些东西自己吃鼠标（菜单、窗口按钮、tab 芯片）。
	/// 注意别把整个标签条 ScrollViewer 算进来：标签条右边的空白属于标题栏，
	/// 在那里按下应当能拖窗口。
	/// </summary>
	static bool istitleinteractive(DependencyObject d) {
		while (d != null) {
			if (d is Menu || d is MenuItem || d is Button) return true;
			if (d is Border br && br.Tag is Tab) return true;
			d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
				? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
		}
		return false;
	}

	void togglemax() => WindowState = WindowState == WindowState.Maximized
		? WindowState.Normal : WindowState.Maximized;

	void copytext(string text) {
		if (string.IsNullOrEmpty(text)) return;
		try { Clipboard.SetText(text); lbstat.Text = Loc.T("mdread.copied"); }
		catch { }
	}

	void sysopen(string path) {
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
		}
		catch { }
	}

	/// <summary>把当前 Markdown 导出成 Word / PDF：复用 MdHtml 的排版，不依赖阅读器界面。</summary>
	void export(bool word) {
		if (cur == null || cur.Kind != DocView.DocKind.Md) return;
		var ofd = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T(word ? "mdread.word" : "mdread.pdf"),
			Filter = word ? "Word|*.docx" : "PDF|*.pdf",
			FileName = System.IO.Path.GetFileNameWithoutExtension(cur.Path) + (word ? ".docx" : ".pdf"),
			DefaultExt = word ? ".docx" : ".pdf",
			AddExtension = true,
			OverwritePrompt = true,
		};
		try {
			var dir = System.IO.Path.GetDirectoryName(cur.Path);
			if (!string.IsNullOrEmpty(dir)) ofd.InitialDirectory = dir;
		}
		catch { }
		if (ofd.ShowDialog(this) != true) return;
		var dest = ofd.FileName;
		lbstat.Text = Loc.T("mdread.exporting");
		lbstat.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
		Mouse.OverrideCursor = Cursors.Wait;
		try {
			var baseDir = System.IO.Path.GetDirectoryName(cur.Path) ?? "";
			var body = MdHtml.Render(File.ReadAllText(cur.Path), new MdHtml.Opt { BaseDir = baseDir }).Html;
			var html = MdExport.PrintHtml("<html><head><meta charset=\"utf-8\"></head><body>" + body + "</body></html>");
			if (word) MdExport.ToWord(html, dest);
			else MdExport.ToPdf(html, dest);
			lbstat.Text = string.Format(Loc.T("mdread.exported"), dest);
		}
		catch (MdExportError ex) {
			lbstat.Text = Loc.T("mdread." + ex.Key);
		}
		catch (Exception ex) {
			lbstat.Text = string.Format(Loc.T("mdread.exportfail"), ex.Message);
		}
		finally {
			Mouse.OverrideCursor = null;
		}
	}
	#endregion

	// ==================== 状态栏 ====================
	#region
	void showstat() {
		mnclose.IsEnabled = cur != null;
		mncloseall.IsEnabled = tabs.Count > 0;
		mnreload.IsEnabled = cur != null;
		mnpath.IsEnabled = cur != null;
		mnsysopen.IsEnabled = cur != null;
		var v = cur?.Viewer;
		bzoomout.IsEnabled = v != null;
		bzoomin.IsEnabled = v != null;
		bzoomfit.IsEnabled = v != null;
		bfind.IsEnabled = v != null;
		bfindprev.IsEnabled = v != null;
		bfindnext.IsEnabled = v != null;
		syncpagebox();
		if (cur == null) {
			lbstat.Text = "";
			lbstat.ToolTip = null;
			return;
		}
		lbstat.ToolTip = cur.Path;
		if (cur.Error.Length > 0) {
			lbstat.Text = cur.Error;
			return;
		}
		if (v == null) {
			lbstat.Text = Loc.T("mdread.loading");
			return;
		}
		var page = Math.Max(1, v.CurrentPage);
		var count = Math.Max(page, v.PageCount);
		lbstat.Text = $"第{page}/{count}页 · {(int)(v.Zoom * 100)}%";
	}
	#endregion
}
