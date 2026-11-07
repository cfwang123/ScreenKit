using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WpfRenderOptions = System.Windows.Media.RenderOptions;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace ScreenKit.DocView;

/// <summary>
/// PDF 阅读：pdfium 会话常开、优先队列渲染（预览→全清）、文字可选。
/// </summary>
sealed class PdfViewer : IDocViewer {
	const double LAYOUT_DPI = 96;     // 布局 DIP（96dpi 用户空间）
	/// <summary>可见区外各方向至少预取的页数（快速滚动防白页）。</summary>
	const int PREFETCH = 3;
	/// <summary>槽位保留余量：超出预取范围也不立刻拆掉，避免回滚白页。</summary>
	const int SLOT_KEEP = 4;
	const double PAGE_GAP = 10;
	/// <summary>页边框厚度（DIP）；Host 外扩同宽，避免 Border 挤占内容导致位图被 Stretch 发糊。</summary>
	const double PAGE_BORDER = 1;
	const double MIN_ZOOM = 0.2;
	const double MAX_ZOOM = 4.0;
	const double SIDE_W = 220;
	const int OUTLINE_SYNC_MS = 80;
	/// <summary>滚动同步目录防抖：连续翻页停稳后再改高亮，避免上下跳动。</summary>
	const int OUTLINE_DEBOUNCE_MS = 140;
	/// <summary>布局 DIP 超此高度分块（略小→高缩放时更多竖切块，单块像素可控）。</summary>
	const int TILE_MAX_DIP = 1200;
	const int KIND_PREVIEW = 0;
	const int KIND_FULL = 1;
	const double PREVIEW_SCALE = 0.35;
	/// <summary>
	/// 全清相对「布局 DIP × dpiScale」的倍率。
	/// 1.0 = 设备像素 1:1（最锐）；&gt;1 超采样但更易触顶封顶后被拉伸发糊。
	/// </summary>
	const double FULL_SHARP = 1.0;
	/// <summary>单边最大像素 / 单页最大像素（ARGB≈4B）。高缩放保清晰需足够预算。</summary>
	const int MAX_EDGE = 4800;
	const int MAX_PAGE_PIXELS = 12_000_000; // ~48MB/页上限（分块后单 tile 另有上限）
	/// <summary>位图缓存（预览小图多留一些，快滚时命中率高）。</summary>
	const int MAX_CACHE_ENTRIES = 36;
	const int MAX_TEXT_PAGES = 6;       // 抽字缓存页数（每页可达数千 PdfCharInfo）
	const int MAX_QUEUE = 28;



	static readonly double[] ZoomPresets = {
		0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0,
	};

	readonly Grid root;
	readonly ColumnDefinition colside;
	readonly Border pside;
	readonly TreeView tree;
	readonly TextBlock lboutline;
	readonly TextBox eoutline;
	readonly ScrollViewer scroller;
	readonly Grid contentRoot;
	readonly Canvas canvas;
	readonly Canvas selLayer;
	readonly Dictionary<int, PageSlot> slots = new();
	readonly Dictionary<long, BitmapSource> cache = new();
	readonly LinkedList<long> lru = new();
	readonly object gate = new();
	readonly List<OutlineEntry> outlineFlat = new();
	readonly List<RTask> queue = new();
	readonly AutoResetEvent queuePulse = new(false);
	readonly Dictionary<int, List<PdfCharInfo>> textCache = new();
	readonly LinkedList<int> textLru = new();
	readonly HashSet<int> textPending = new();

	Thread worker;
	volatile bool workerStop;
	volatile int visFirst, visLast, visAnchor;

	PdfiumSession session;
	string pdfPath;
	byte[] pdfBytes;
	SizeF[] pageSizesPt;
	double[] pageTop, pageH, pageW;
	int pageCount;
	double zoom = 1.0;
	/// <summary>已写入 pageW/pageH 的缩放（与 zoom 同步；保留字段兼容旧逻辑）。</summary>
	double layoutZoom = 1.0;
	double contentW, contentH;
	double dpiScale = 1.0;
	/// <summary>整文档视图旋转：0/1/2/3 = 0°/90°/180°/270°（顺时针）。</summary>
	int pageRotate;
	int gen;
	bool disposed;
	/// <summary>缩放布局过程中：禁止拆槽/新建占位页。</summary>
	bool zoomHold;
	/// <summary>停稳后再清缓存重渲（布局与滚动在滚轮时已完成，不再改位置）。</summary>
	DispatcherTimer zoomRenderTimer;
	/// <summary>连续 Ctrl+滚轮复用同一文档锚点（页+页内比例）。</summary>
	int zoomLockPage = -1;
	double zoomLockFracX, zoomLockFracY;
	WpfPoint zoomLockMouse;
	/// <summary>鼠标落在页面正文内时才用 X 作锚点；页外（左右灰边）仅锁 Y，X 用页中心。</summary>
	bool zoomLockXOnPage;
	int lastZoomTick;
	const int ZOOM_LOCK_MS = 600;
	/// <summary>停稳后再重渲清晰图；布局/滚动已在缩放时完成。</summary>
	const int ZOOM_RENDER_MS = 200;
	/// <summary>缩放后短时钉住：仅拦截布局引起的偏移漂移，不拦截用户主动滚动。</summary>
	double zoomPinH = -1, zoomPinV = -1;
	int zoomPinUntil;
	/// <summary>布局钉住窗口（用户滚轮/拖动手一动即解除，故可略长）。</summary>
	const int ZOOM_PIN_MS = 800;
	bool sideVisible = true;
	bool hasOutline;
	bool syncTree;
	/// <summary>下次 syncoutline 时展开到当前页最深目录项（仅恢复阅读位置用）。</summary>
	bool pendingExpandOutline;
	/// <summary>恢复阅读时的目标页（0-based）；布局未稳时 estimatepage 可能仍为 0，用此兜底。</summary>
	int restoreOutlinePage = -1;
	int outlineRestoreToken;
	bool panning;
	bool selecting;
	WpfPoint panStart;
	double panOffX, panOffY;
	int lastOutlineSync;
	int lastOutlinePage = -1;
	/// <summary>滚动目录同步防抖定时器。</summary>
	DispatcherTimer outlineDebounce;
	/// <summary>上次滚动位置，用于方向感知预取。</summary>
	double lastScrollY = -1;
	int scrollDir; // +1 向下 -1 向上 0 未知
	int pendingOutlinePage = -1;
	/// <summary>目录跳转：距页顶比例（与 pendingOutlinePage 配对）。</summary>
	double pendingOutlineFrac;
	int outlineNavToken;
	/// <summary>跳转历史（目录/链接/页码）；Alt+←/→ 后退/前进。</summary>
	readonly List<NavMark> navBack = new();
	readonly List<NavMark> navFwd = new();
	bool navRestoring;
	const int MAX_NAV = 64;
	List<PdfOutlineNode> outline;
	/// <summary>目录筛选关键字。</summary>
	string outlineQuery = "";
	// 右键命中的图片（ContextMenuOpening 时填充）
	PdfImageInfo ctxImage;
	MenuItem mnCopyText;
	MenuItem mnViewImg;
	MenuItem mnCopyImg;
	MenuItem mnSaveImg;

	// ---------- PDF 编辑模式 ----------
	bool editMode;
	bool editDirty;
	readonly PdfEditDoc editDoc = new();
	PdfEditSurface editSurface;
	public event Action EditModeChanged;
	public event Action DirtyChanged;
	public event Action EditSelectionChanged;
	public bool EditMode {
		get => editMode;
		set => seteditmode(value);
	}
	public bool IsDirty => editDirty || editDoc.Dirty;

	// ---------- PDF 标注模式（旁路 JSON，不写 PDF 本体）----------
	bool annotMode;
	readonly PdfAnnotDoc annotDoc = new();
	PdfAnnotSurface annotSurface;
	DispatcherTimer annotSaveTimer;
	public event Action AnnotModeChanged;
	public event Action AnnotChanged;
	public bool AnnotMode {
		get => annotMode;
		set => setannotmode(value);
	}
	public bool AnnotDirty => annotDoc != null && annotDoc.Dirty;

	// 选区：字符索引闭区间；跨页时 end 页可不同
	int selPage = -1, selStart = -1, selEnd = -1;
	int dragAnchorPage = -1, dragAnchorChar = -1;

	// 查找缓存：全部命中 + 当前下标（0-based）
	string findQuery;
	bool findIgnoreCase = true;
	readonly List<(int Page, int Start, int End)> findHits = new();
	int findIndex = -1;

	sealed class OutlineEntry {
		public int Page;
		/// <summary>树深度，0 为根。</summary>
		public int Depth;
		/// <summary>文档序（先序遍历下标）；同步时取「最后一个仍不超过当前位置」的项。</summary>
		public int Order;
		/// <summary>距页顶比例 0..1；无 Y 目标时为 0。</summary>
		public double TopFrac;
		public TreeViewItem Item;
	}

	sealed class PageSlot {
		public int Page;
		public Border Host;
		public System.Windows.Controls.Image[] Tiles;
		public TextBlock PageLabel;
	}

	sealed class RTask {
		public int Page, Tile, Kind, Gen, Rotate;
		public int PixelW, PixelH, ClipY0, ClipY1;
		public double DipDpi;
		public long CacheKey;
		public volatile bool Cancelled;
	}

	public FrameworkElement View => root;
	public string FilePath { get; private set; }
	public string Title { get; private set; }
	public DocKind Kind => DocKind.Pdf;
	public double Zoom => zoom;
	public string StatusText {
		get {
			if (pageCount <= 0) return "PDF";
			var cur = estimatepage() + 1;
			var sel = selPage >= 0 && selStart >= 0 ? "  ·  已选文字" : "";
			var rot = pageRotate == 0 ? "" : $"  ·  旋转{pageRotate * 90}°";
			var ed = editMode ? "  ·  编辑中" : "";
			var an = annotMode ? "  ·  标注中" : "";
			var d = (IsDirty || AnnotDirty) ? " *" : "";
			return $"PDF{d}  第 {cur}/{pageCount} 页  ·  {(int)(zoom * 100)}%{rot}{ed}{an}{sel}";
		}
	}
	public int PageCount => pageCount;
	public int CurrentPage => pageCount <= 0 ? 0 : estimatepage() + 1;

	public event Action StatusChanged;
	/// <summary>滚动定位章节时：理想书签 1-based 页码（主窗章节列表镜像用）。</summary>
	public event Action<int> OutlineHighlightChanged;

	public PdfViewer() {
		tree = new TreeView {
			BorderThickness = new Thickness(0),
			Background = WpfBrushes.Transparent,
			Padding = new Thickness(0, 0, 0, 4),
		};
		OutlineUi.ConfigureTree(tree);
		// 目录点击：防重入 + 防抖，避免快速连点时 SelectedItemChanged↔syncoutline 递归崩溃
		tree.SelectedItemChanged += (_, _) => {
			if (syncTree || disposed) return;
			if (tree.SelectedItem is TreeViewItem ti && ti.Tag is PdfOutlineNode node && node.PageIndex >= 0)
				queueoutlinejump(node);
		};
		// 用户展开/折叠后，按当前页重算可见路径上的高亮章节
		tree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(onoutlineexpandcollapse));
		tree.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(onoutlineexpandcollapse));

		lboutline = new TextBlock {
			Text = "无目录",
			Margin = new Thickness(10, 4, 10, 4),
			Foreground = new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80)),
			Visibility = Visibility.Collapsed,
		};
		eoutline = OutlineUi.MakeFilterBox();
		// 简易占位：空时显示灰色提示靠 ToolTip；GotFocus 不改
		eoutline.TextChanged += (_, _) => {
			outlineQuery = eoutline.Text?.Trim() ?? "";
			rebuildoutlineui();
		};

		var btoggle = new Button {
			Content = "«", Width = 28, Height = 22, Padding = new Thickness(0),
			ToolTip = "隐藏目录", Cursor = Cursors.Hand,
			Background = WpfBrushes.Transparent, BorderThickness = new Thickness(0),
			Foreground = new SolidColorBrush(WpfColor.FromRgb(0x37, 0x41, 0x51)),
		};
		btoggle.Click += (_, _) => setside(!sideVisible);
		// 默认先藏侧栏，有书签后再打开

		var head = new DockPanel { Margin = new Thickness(8, 6, 4, 4) };
		DockPanel.SetDock(btoggle, Dock.Right);
		head.Children.Add(btoggle);
		head.Children.Add(new TextBlock {
			Text = "目录", FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			FontSize = DocViewOptions.Current.UiFontSize,
			Foreground = new SolidColorBrush(WpfColor.FromRgb(0x11, 0x18, 0x27)),
		});

		var sideBody = new DockPanel();
		DockPanel.SetDock(head, Dock.Top);
		DockPanel.SetDock(eoutline, Dock.Top);
		DockPanel.SetDock(lboutline, Dock.Top);
		sideBody.Children.Add(head);
		sideBody.Children.Add(eoutline);
		sideBody.Children.Add(lboutline);
		sideBody.Children.Add(tree);

		pside = new Border {
			Background = new SolidColorBrush(WpfColor.FromRgb(0xF9, 0xFA, 0xFB)),
			BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xE5, 0xE7, 0xEB)),
			BorderThickness = new Thickness(0, 0, 1, 0),
			// 裁切，避免目录文字撑出横向滚动
			ClipToBounds = true,
			Child = sideBody,
		};

		canvas = new Canvas {
			Background = new SolidColorBrush(WpfColor.FromRgb(0xE5, 0xE7, 0xEB)),
			SnapsToDevicePixels = true,
			UseLayoutRounding = true,
		};
		selLayer = new Canvas {
			Background = WpfBrushes.Transparent,
			IsHitTestVisible = true,
			UseLayoutRounding = true,
		};
		contentRoot = new Grid {
			Background = new SolidColorBrush(WpfColor.FromRgb(0xE5, 0xE7, 0xEB)),
			SnapsToDevicePixels = true,
			UseLayoutRounding = true,
		};
		editSurface = new PdfEditSurface(editDoc) {
			IsHitTestVisible = false,
			Visibility = Visibility.Collapsed,
		};
		editSurface.Changed += () => {
			editDirty = true;
			try { DirtyChanged?.Invoke(); } catch { /* ignore */ }
			raisestatus();
		};
		editSurface.SelectionChanged += () => {
			try { EditSelectionChanged?.Invoke(); } catch { /* ignore */ }
		};
		editSurface.SetLayout(
			page => {
				if (page < 0 || page >= pageCount || pageW == null || pageH == null || pageTop == null)
					return (0, 0, 1, 1);
				var left = Math.Max(0, (contentW - pageW[page]) / 2);
				return (left, pageTop[page], pageW[page], pageH[page]);
			},
			page => {
				viewpagesizept(page, out var pw, out var ph);
				return (Math.Max(1, pw), Math.Max(1, ph));
			},
			pt => findpageat(pt.Y),
			(page, xPt, yPt) => trycaptureexisting(page, xPt, yPt));

		annotSurface = new PdfAnnotSurface(annotDoc) {
			Visibility = Visibility.Visible,
		};
		annotSurface.SetLayout(
			page => {
				if (page < 0 || page >= pageCount || pageW == null || pageH == null || pageTop == null)
					return (0, 0, 1, 1);
				var left = Math.Max(0, (contentW - pageW[page]) / 2);
				return (left, pageTop[page], pageW[page], pageH[page]);
			},
			page => {
				viewpagesizept(page, out var pw, out var ph);
				return (Math.Max(1, pw), Math.Max(1, ph));
			},
			pt => findpageat(pt.Y),
			scroller);
		annotSurface.Changed += () => {
			scheduleannotsave();
			try { AnnotChanged?.Invoke(); } catch { /* ignore */ }
			raisestatus();
		};
		annotSurface.SelectionChanged += () => {
			try { AnnotChanged?.Invoke(); } catch { /* ignore */ }
		};
		annotSurface.ToolChanged += () => {
			try { AnnotChanged?.Invoke(); } catch { /* ignore */ }
		};

		contentRoot.Children.Add(canvas);
		contentRoot.Children.Add(selLayer);
		contentRoot.Children.Add(annotSurface);
		contentRoot.Children.Add(editSurface);
		Panel.SetZIndex(annotSurface, 15);
		Panel.SetZIndex(editSurface, 20);

		scroller = new ScrollViewer {
			Content = contentRoot,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			Background = new SolidColorBrush(WpfColor.FromRgb(0xE5, 0xE7, 0xEB)),
			Focusable = true,
			CanContentScroll = false,
			PanningMode = PanningMode.Both,
		};
		scroller.ScrollChanged += (_, e) => {
			// 缩放钉：只挡「布局/尺寸变化」导致的偏移漂移；用户滚轮/拖动一律放行并解除钉
			if (iszoompinactive()) {
				var layoutDriven = Math.Abs(e.ExtentWidthChange) > 0.5
					|| Math.Abs(e.ExtentHeightChange) > 0.5
					|| Math.Abs(e.ViewportWidthChange) > 0.5
					|| Math.Abs(e.ViewportHeightChange) > 0.5;
				var dh = Math.Abs(scroller.HorizontalOffset - zoomPinH);
				var dv = Math.Abs(scroller.VerticalOffset - zoomPinV);
				if (layoutDriven) {
					if (dh > 1 || dv > 1) {
						DocViewLog.Warn(
							$"zoom PIN restore h {scroller.HorizontalOffset:F0}->{zoomPinH:F0} " +
							$"v {scroller.VerticalOffset:F0}->{zoomPinV:F0} " +
							$"extΔ=({e.ExtentWidthChange:F0},{e.ExtentHeightChange:F0})");
						scroller.ScrollToHorizontalOffset(zoomPinH);
						scroller.ScrollToVerticalOffset(zoomPinV);
						return;
					}
				} else if (dh > 2 || dv > 2) {
					// 用户主动滚动：立刻解除，否则会感觉「缩完 1 秒滚不动」
					clearzoompin();
				}
			}
			// 方向：向下多预取下方页，向上多预取上方页
			if (e.ExtentHeightChange == 0 && e.ExtentWidthChange == 0) {
				var y = scroller.VerticalOffset;
				if (lastScrollY >= 0) {
					var dy = y - lastScrollY;
					if (dy > 2) scrollDir = 1;
					else if (dy < -2) scrollDir = -1;
				}
				lastScrollY = y;
			}
			// 槽位创建 + 贴缓存必须同步，否则快滚时会整页空白半拍
			updateviewport(gen);
			syncoutline();
			drawselection();
			raisestatus();
		};
		scroller.SizeChanged += (_, _) => {
			var old = dpiScale;
			updatedpiscale();
			if (Math.Abs(dpiScale - old) > 0.02)
				onDpichanged();
			else
				updateviewport(gen);
		};
		initzoominput();

		var main = new Grid();
		main.Children.Add(scroller);

		root = new Grid();
		colside = new ColumnDefinition { Width = new GridLength(SIDE_W) };
		root.ColumnDefinitions.Add(colside);
		root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
		root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		var sp = new GridSplitter {
			Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch,
			Background = new SolidColorBrush(WpfColor.FromRgb(0xE5, 0xE7, 0xEB)),
			ResizeBehavior = GridResizeBehavior.PreviousAndNext,
		};
		Grid.SetColumn(pside, 0);
		Grid.SetColumn(sp, 1);
		Grid.SetColumn(main, 2);
		root.Children.Add(pside);
		root.Children.Add(sp);
		root.Children.Add(main);

		initselection();
		startworker();
		root.Loaded += (_, _) => {
			updatedpiscale();
			// 首帧再读一次 DPI（部分机器 Loaded 时 Visual 尚未挂到最终显示器）
			try {
				root.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
					if (disposed) return;
					var old = dpiScale;
					updatedpiscale();
					if (pageCount > 0 && Math.Abs(dpiScale - old) > 0.02)
						onDpichanged();
				}));
			} catch { /* ignore */ }
			// net48：DpiChanged 在 Window 上，不在 FrameworkElement
			try {
				var win = Window.GetWindow(root);
				if (win != null && !dpiHooked) {
					dpiHooked = true;
					win.DpiChanged += onwindowdpichanged;
				}
			} catch { /* ignore */ }
		};
		// 构造时侧栏先隐藏，等目录加载结果再决定
		setside(false);
	}

	bool dpiHooked;

	void onwindowdpichanged(object sender, DpiChangedEventArgs e) {
		var next = e.NewDpi.DpiScaleX > 0.1 ? e.NewDpi.DpiScaleX : 1.0;
		if (Math.Abs(next - dpiScale) < 0.02) return;
		DocViewLog.Info($"PdfViewer DpiChanged {dpiScale:F3} -> {next:F3}");
		dpiScale = next;
		onDpichanged();
	}

	public void Load(string path) => Load(path, null);

	/// <param name="fileBytes">后台预读字节；null 则现场读。</param>
	public void Load(string path, byte[] fileBytes) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("文件不存在", path);
		FilePath = Path.GetFullPath(path);
		Title = Path.GetFileName(path);
		pdfPath = FilePath;
		pageRotate = 0;
		clearsel();
		clearnavhistory();
		editDoc.Clear();
		editDirty = false;
		if (editMode) seteditmode(false);
		editSurface?.RebuildAll();
		// 切换文档前先落盘旧标注
		try { flushannotsave(); } catch { /* ignore */ }
		if (annotMode) setannotmode(false);
		annotDoc.Clear();
		annotSurface?.RebuildAll();
		DocViewLog.Info($"PdfViewer.Load begin path={pdfPath}");

		var t0 = Environment.TickCount;
		if (fileBytes != null && fileBytes.Length > 0)
			pdfBytes = fileBytes;
		else {
			pdfBytes = PdfIo.TryLoadBytes(pdfPath);
			if (pdfBytes == null)
				pdfBytes = DocFileIo.ReadAllBytesShared(pdfPath);
		}

		// 仅用 pdfium 会话（避免 PDFtoImage 再解析一遍大文件）
		PdfiumSession s = null;
		PdfIo.WithLock(() => {
			session?.Dispose();
			session = PdfiumSession.Open(pdfBytes);
			s = session;
		});

		pageCount = s.PageCount;
		pageSizesPt = s.PageSizesPt;
		pageTop = new double[pageCount];
		pageH = new double[pageCount];
		pageW = new double[pageCount];
		DocViewLog.Info($"PdfViewer.Load sizes ok pages={pageCount} bytes={pdfBytes.Length} cost={Environment.TickCount - t0}ms");

		outline = null;
		tree.Items.Clear();
		lboutline.Text = "加载目录…";
		lboutline.Visibility = Visibility.Visible;
		var pathLocal = pdfPath;
		Task.Run(() => {
			try {
				var t1 = Environment.TickCount;
				List<PdfOutlineNode> nodes = null;
				PdfIo.WithLock(() => {
					if (session != null && pdfPath == pathLocal)
						nodes = session.LoadOutline();
				});
				DocViewLog.Info($"PdfOutline roots={nodes?.Count ?? 0} cost={Environment.TickCount - t1}ms");
				scroller.Dispatcher.BeginInvoke(new Action(() => {
					if (disposed || pdfPath != pathLocal) return;
					outline = nodes ?? new List<PdfOutlineNode>();
					buildoutline();
				}));
			} catch (Exception ex) {
				DocViewLog.Error("PdfOutline failed", ex);
			}
		});

		// 加载同目录标注 JSON
		try {
			annotDoc.LoadForPdf(pdfPath);
			annotSurface?.RebuildAll();
		} catch (Exception ex) {
			DocViewLog.Error("annot load", ex);
		}

		updatedpiscale();
		// 加载时布局与逻辑缩放对齐
		layoutZoom = zoom;
		zoomHold = false;
		try { zoomRenderTimer?.Stop(); } catch { /* ignore */ }
		recalcmetrics();
		clearslots();
		clearcache();
		cleartextcache();
		cancelall();
		gen++;
		scheduleui();
		raisestatus();
		DocViewLog.Info($"PdfViewer.Load done pages={pageCount} dpiScale={dpiScale:F3}");
	}

	public void SetZoom(double z) => bakezoomimmediate(z);
	public void ZoomBy(double factor) { if (factor > 1) ZoomIn(); else ZoomOut(); }
	public void ZoomIn() => bakezoomimmediate(nextzoom(true));
	public void ZoomOut() => bakezoomimmediate(nextzoom(false));

	public void CaptureViewState(out double h, out double v, out double z, out int sheetOrPage) {
		h = scroller?.HorizontalOffset ?? 0;
		v = scroller?.VerticalOffset ?? 0;
		z = zoom;
		sheetOrPage = CurrentPage;
	}

	public void RestoreViewState(double h, double v, double z, int sheetOrPage) {
		if (z > 0.05 && Math.Abs(z - zoom) > 0.001)
			bakezoomimmediate(z, keepScroll: true);
		try {
			if (scroller == null) return;
			// 布局就绪后再滚
			scroller.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
				try {
					if (disposed || scroller == null) return;
					if (h > 0) scroller.ScrollToHorizontalOffset(h);
					if (v > 0) scroller.ScrollToVerticalOffset(v);
					else if (sheetOrPage > 0)
						GoToPage(sheetOrPage);
					// 恢复目标页：优先已滚位置；布局未稳时用会话页码兜底
					var ep = estimatepage();
					var target = ep;
					if (sheetOrPage > 0)
						target = Math.Max(target, sheetOrPage - 1);
					if (target < 0) target = 0;
					queueoutlinerestore(target);
					updateviewport(gen);
				} catch { /* ignore */ }
			}));
		} catch { /* ignore */ }
	}

	/// <summary>
	/// 恢复位置后多次同步目录（滚位置/目录异步/视口高度就绪有先后）。
	/// </summary>
	void queueoutlinerestore(int page0) {
		if (page0 < 0) page0 = 0;
		if (pageCount > 0 && page0 >= pageCount) page0 = pageCount - 1;
		restoreOutlinePage = page0;
		pendingExpandOutline = true;
		var token = ++outlineRestoreToken;
		void once() {
			if (disposed || token != outlineRestoreToken) return;
			try {
				var ep = estimatepage();
				if (ep > restoreOutlinePage)
					restoreOutlinePage = ep;
				pendingExpandOutline = true;
				syncoutline(force: true);
			} catch { /* ignore */ }
		}
		once();
		try {
			scroller.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(once));
			scroller.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(once));
			scroller.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
				once();
				// 恢复流程结束，不再用兜底页码覆盖滚动同步
				if (token == outlineRestoreToken)
					restoreOutlinePage = -1;
			}));
		} catch { /* ignore */ }
	}

	public void ZoomFitWidth() {
		if (pageCount <= 0 || scroller.ViewportWidth <= 1) return;
		var idx = estimatepage();
		viewpagesizept(idx, out var ptW, out _);
		ptW = Math.Max(1, ptW);
		var targetW = Math.Max(40, scroller.ViewportWidth - 24);
		var natural = ptW * LAYOUT_DPI / 72.0;
		bakezoomimmediate(targetW / natural);
	}

	public void ZoomFitPage() {
		if (pageCount <= 0 || scroller.ViewportWidth <= 1 || scroller.ViewportHeight <= 1) return;
		var idx = estimatepage();
		viewpagesizept(idx, out var ptW, out var ptH);
		ptW = Math.Max(1, ptW);
		ptH = Math.Max(1, ptH);
		var targetW = Math.Max(40, scroller.ViewportWidth - 24);
		var targetH = Math.Max(40, scroller.ViewportHeight - 24);
		var naturalW = ptW * LAYOUT_DPI / 72.0;
		var naturalH = ptH * LAYOUT_DPI / 72.0;
		bakezoomimmediate(Math.Min(targetW / naturalW, targetH / naturalH));
	}

	public void RotateBy(int deltaQuarterTurns) {
		if (disposed || pageCount <= 0 || deltaQuarterTurns == 0) return;
		var page = estimatepage();
		var frac = pagefrac(page);
		pageRotate = ((pageRotate + deltaQuarterTurns) % 4 + 4) % 4;
		gen++;
		cancelall();
		clearcache();
		// 字符盒随旋转重映射，清空抽字缓存
		cleartextcache();
		clearsel();
		recalcmetrics();
		clearslots();
		scrolltopagefrac(page, frac);
		scheduleui();
		syncoutline(force: true);
		raisestatus();
		DocViewLog.Info($"PdfViewer.RotateBy rot={pageRotate * 90}");
	}

	public void GoPrevPage() {
		var cur = estimatepage();
		if (cur <= 0) return;
		// 翻页不记历史（连续 PgUp/PgDn 不刷屏）
		scrolltopage(cur - 1);
	}

	public void GoNextPage() {
		var cur = estimatepage();
		if (cur >= pageCount - 1) return;
		scrolltopage(cur + 1);
	}

	public void GoToPage(int page1Based) {
		if (pageCount <= 0) return;
		var p = page1Based - 1;
		if (p < 0) p = 0;
		if (p >= pageCount) p = pageCount - 1;
		// 页码框 / Home / End / g 等显式跳转记历史
		jumpwithhistory(p, 0, fromOutline: false);
	}

	/// <summary>跳转历史：后退到跳转前位置。无记录返回 false。</summary>
	public bool TryNavBack() {
		if (navRestoring || disposed || navBack.Count == 0) return false;
		var cur = capturenav();
		var target = navBack[navBack.Count - 1];
		navBack.RemoveAt(navBack.Count - 1);
		navFwd.Add(cur);
		if (navFwd.Count > MAX_NAV) navFwd.RemoveAt(0);
		restorenav(target);
		DocViewLog.Info($"PdfViewer nav back → p={target.Page + 1} remaining={navBack.Count}");
		return true;
	}

	/// <summary>跳转历史：前进。无记录返回 false。</summary>
	public bool TryNavForward() {
		if (navRestoring || disposed || navFwd.Count == 0) return false;
		var cur = capturenav();
		var target = navFwd[navFwd.Count - 1];
		navFwd.RemoveAt(navFwd.Count - 1);
		navBack.Add(cur);
		if (navBack.Count > MAX_NAV) navBack.RemoveAt(0);
		restorenav(target);
		DocViewLog.Info($"PdfViewer nav forward → p={target.Page + 1} remaining={navFwd.Count}");
		return true;
	}

	public FindResult Find(string text, bool forward, bool ignoreCase, bool restart = false, bool fromView = false) {
		if (string.IsNullOrEmpty(text) || pageCount <= 0 || disposed)
			return FindResult.Miss();
		try {
			var needRebuild = restart
				|| findHits.Count == 0
				|| !string.Equals(findQuery, text, StringComparison.Ordinal)
				|| findIgnoreCase != ignoreCase;
			if (needRebuild) {
				rebuildfindhits(text, ignoreCase);
				findIndex = -1;
			}
			if (findHits.Count == 0) {
				drawselection();
				return FindResult.Miss(0);
			}

			if (fromView)
				findIndex = pickfindfromview(forward, afterCurrent: findIndex >= 0);
			else if (restart || findIndex < 0)
				findIndex = pickfindfromview(forward, afterCurrent: false);
			else
				findIndex = forward
					? (findIndex + 1) % findHits.Count
					: (findIndex - 1 + findHits.Count) % findHits.Count;
			jumptofindhit(findIndex);
			return FindResult.Hit(findIndex + 1, findHits.Count);
		} catch (Exception ex) {
			DocViewLog.Error("Pdf Find", ex);
			return FindResult.Miss(findHits.Count);
		}
	}

	public void ClearFind() {
		findHits.Clear();
		findQuery = null;
		findIgnoreCase = true;
		findIndex = -1;
		// 查找产生的选区一并清掉，避免残留黄框
		if (selPage >= 0) clearsel();
		else drawselection();
	}

	void rebuildfindhits(string text, bool ignoreCase) {
		findHits.Clear();
		findQuery = text;
		findIgnoreCase = ignoreCase;
		findIndex = -1;
		var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		for (var p = 0; p < pageCount; p++) {
			var ordered = getcharssync(p);
			if (ordered == null || ordered.Count == 0) continue;
			var sb = new StringBuilder(ordered.Count);
			foreach (var c in ordered) sb.Append(c.Char);
			var s = sb.ToString();
			if (s.Length == 0) continue;
			var from = 0;
			while (from < s.Length) {
				var idx = s.IndexOf(text, from, cmp);
				if (idx < 0) break;
				var end = idx + text.Length - 1;
				if (end >= ordered.Count) end = ordered.Count - 1;
				findHits.Add((p, ordered[idx].Index, ordered[end].Index));
				from = idx + Math.Max(1, text.Length);
			}
		}
		DocViewLog.Info($"Pdf find rebuild q={text} hits={findHits.Count}");
	}

	/// <summary>
	/// 从当前视口起取命中。afterCurrent：当前命中仍在视口及以下时取下一个；
	/// 已滚离则从新视口首个起。
	/// </summary>
	int pickfindfromview(bool forward, bool afterCurrent) {
		if (findHits.Count == 0) return -1;
		var scrollY = scroller?.VerticalOffset ?? 0;
		var page = 0;
		try { page = findpageat(scrollY); } catch { page = estimatepage(); }
		if (page < 0) page = 0;
		if (page >= pageCount) page = pageCount - 1;

		var curStillInOrBelow = false;
		if (afterCurrent && findIndex >= 0 && findIndex < findHits.Count) {
			var cur = findHits[findIndex];
			if (cur.Page > page) curStillInOrBelow = true;
			else if (cur.Page == page) curStillInOrBelow = hitabsy(cur) + 1 >= scrollY;
		}

		if (forward) {
			if (curStillInOrBelow)
				return (findIndex + 1) % findHits.Count;
			for (var i = 0; i < findHits.Count; i++) {
				var h = findHits[i];
				if (h.Page < page) continue;
				if (h.Page > page) return i;
				if (hitabsy(h) + 1 >= scrollY) return i;
			}
			return 0;
		}
		if (curStillInOrBelow)
			return (findIndex - 1 + findHits.Count) % findHits.Count;
		var viewBottom = scrollY + Math.Max(40, scroller?.ViewportHeight ?? 400);
		for (var i = findHits.Count - 1; i >= 0; i--) {
			var h = findHits[i];
			if (h.Page > page) continue;
			if (h.Page < page) return i;
			if (hitabsy(h) <= viewBottom) return i;
		}
		return findHits.Count - 1;
	}

	double hitabsy((int Page, int Start, int End) h) {
		if (h.Page < 0 || pageTop == null || h.Page >= pageTop.Length) return 0;
		var matchTop = 0.0;
		try {
			var ordered = getcharssync(h.Page);
			if (ordered != null) {
				foreach (var c in ordered) {
					if (c.Index == h.Start) { matchTop = c.Top; break; }
				}
			}
			viewpagesizept(h.Page, out _, out var vph);
			var sy = pageH[h.Page] / Math.Max(0.01, vph);
			return pageTop[h.Page] + matchTop * sy;
		} catch {
			return pageTop[h.Page];
		}
	}

	void jumptofindhit(int i) {
		if (i < 0 || i >= findHits.Count) return;
		var h = findHits[i];
		selPage = h.Page;
		selStart = h.Start;
		selEnd = h.End;
		try {
			var ordered = getcharssync(h.Page);
			double matchTop = 0;
			if (ordered != null) {
				foreach (var c in ordered) {
					if (c.Index == h.Start) { matchTop = c.Top; break; }
				}
			}
			viewpagesizept(h.Page, out _, out var vph);
			var sy = pageH[h.Page] / Math.Max(0.01, vph);
			scroller.ScrollToVerticalOffset(Math.Max(0, pageTop[h.Page] + matchTop * sy - 40));
			visAnchor = h.Page;
			scheduleui();
		} catch {
			GoToPage(h.Page + 1);
		}
		drawselection();
		raisestatus();
	}

	/// <summary>同步取页文字（查找用；优先缓存）。</summary>
	List<PdfCharInfo> getcharssync(int page) {
		if (page < 0 || page >= pageCount || disposed) return null;
		lock (textCache) {
			if (textCache.TryGetValue(page, out var hit) && hit != null) {
				touchtext(page);
				return hit.OrderBy(c => c.Index).ToList();
			}
		}
		List<PdfCharInfo> chars = null;
		try {
			var g = gen;
			var pathLocal = pdfPath;
			PdfIo.WithLock(() => {
				if (disposed || session == null || g != gen || pdfPath != pathLocal) return;
				chars = session.ExtractChars(page);
			});
		} catch (Exception ex) {
			DocViewLog.Warn($"getcharssync p={page}: {ex.Message}");
		}
		if (chars == null) return null;
		mapcharlistrotate(chars, page);
		lock (textCache) {
			if (!textCache.ContainsKey(page)) {
				textCache[page] = chars;
				textLru.Remove(page);
				textLru.AddLast(page);
				while (textLru.Count > MAX_TEXT_PAGES) {
					var old = textLru.First.Value;
					textLru.RemoveFirst();
					textCache.Remove(old);
				}
			} else {
				chars = textCache[page];
			}
		}
		return chars.OrderBy(c => c.Index).ToList();
	}

	public bool TryCopySelection() => copyselection();
	public bool HasOutline => hasOutline;

	/// <summary>
	/// 主窗章节列表高亮：当前视口对应书签的 1-based 页码；无则 -1。
	/// </summary>
	public int GetActiveOutlinePage1() {
		if (disposed || !hasOutline || outlineFlat.Count == 0 || pageCount <= 0) return -1;
		try {
			var page = estimatepage();
			var frac = pagefrac(page);
			var best = findoutlineat(page, frac);
			if (best == null) return -1;
			return best.Page + 1; // OutlineEntry.Page 为 0-based
		} catch {
			return -1;
		}
	}

	/// <summary>主窗侧栏 TOC：标题 / 深度 / 1-based 页码。</summary>
	public List<(string Title, int Depth, int Page1)> GetOutlineSnapshot() {
		var list = new List<(string, int, int)>();
		void walk(List<PdfOutlineNode> nodes, int d) {
			if (nodes == null) return;
			foreach (var n in nodes) {
				if (n == null) continue;
				list.Add((n.Title ?? "", d, n.PageIndex >= 0 ? n.PageIndex + 1 : 0));
				if (n.Children != null && n.Children.Count > 0)
					walk(n.Children, d + 1);
			}
		}
		try { walk(outline, 0); } catch { /* ignore */ }
		return list;
	}
	public bool SidePanelVisible => false;
	public void SetSidePanelVisible(bool show) => setside(false);

	// ---------- 编辑模式 API ----------
	void seteditmode(bool on) {
		if (editMode == on) return;
		if (on && annotMode) setannotmode(false);
		editMode = on;
		if (editSurface != null) {
			editSurface.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
			editSurface.IsHitTestVisible = on;
			if (on) {
				editSurface.Width = contentW;
				editSurface.Height = contentH;
				editSurface.CurrentTool = PdfEditSurface.Tool.Select;
				editSurface.Relayout();
			}
		}
		// 编辑时禁用文字框选层命中，避免冲突
		if (selLayer != null)
			selLayer.IsHitTestVisible = !on && !annotMode;
		try { EditModeChanged?.Invoke(); } catch { /* ignore */ }
		raisestatus();
	}

	// ---------- 标注模式 API ----------
	void setannotmode(bool on) {
		if (annotMode == on) return;
		if (on && editMode) seteditmode(false);
		annotMode = on;
		if (annotSurface != null) {
			annotSurface.Width = contentW;
			annotSurface.Height = contentH;
			annotSurface.EditMode = on;
			if (on) {
				annotSurface.CurrentTool = PdfAnnotSurface.Tool.Hand;
				annotSurface.Relayout();
				try { annotSurface.Focus(); } catch { /* ignore */ }
			} else {
				flushannotsave();
			}
		}
		if (selLayer != null)
			selLayer.IsHitTestVisible = !on && !editMode;
		try { AnnotModeChanged?.Invoke(); } catch { /* ignore */ }
		raisestatus();
	}

	void scheduleannotsave() {
		if (annotSaveTimer == null) {
			annotSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
			annotSaveTimer.Tick += (_, _) => {
				annotSaveTimer.Stop();
				flushannotsave();
			};
		}
		annotSaveTimer.Stop();
		annotSaveTimer.Start();
	}

	void flushannotsave() {
		try {
			if (annotSaveTimer != null) annotSaveTimer.Stop();
		} catch { /* ignore */ }
		if (annotDoc == null || !annotDoc.Dirty) return;
		if (string.IsNullOrEmpty(pdfPath)) return;
		annotDoc.Save(pdfPath);
		try { AnnotChanged?.Invoke(); } catch { /* ignore */ }
		raisestatus();
	}

	public PdfAnnotItem SelectedAnnot => annotSurface?.Selected;

	public void AnnotSetTool(PdfAnnotSurface.Tool t) {
		if (!annotMode) setannotmode(true);
		if (annotSurface != null) annotSurface.CurrentTool = t;
	}

	public void AnnotSetEraserMode(PdfAnnotSurface.EraserMode mode) {
		if (annotSurface != null) annotSurface.CurrentEraserMode = mode;
	}

	public PdfAnnotSurface.EraserMode AnnotEraserMode =>
		annotSurface?.CurrentEraserMode ?? PdfAnnotSurface.EraserMode.Point;

	public void AnnotDeleteSelected() => annotSurface?.DeleteSelected();
	public void AnnotCopySelected() => annotSurface?.CopySelected();
	public void AnnotPaste() => annotSurface?.PasteClipboard();
	public void AnnotDuplicate() => annotSurface?.DuplicateSelected();
	public void AnnotGroupSelected() => annotSurface?.GroupSelected();
	public void AnnotUngroupSelected() => annotSurface?.UngroupSelected();
	public PdfAnnotSurface.Tool AnnotCurrentTool =>
		annotSurface?.CurrentTool ?? PdfAnnotSurface.Tool.Hand;

	public void AnnotSetColor(WpfColor c) {
		if (!annotMode) setannotmode(true);
		annotSurface?.SetColor(c);
	}

	public void AnnotSetFont(string name) {
		if (!annotMode) setannotmode(true);
		annotSurface?.ApplyFont(name);
	}

	public void AnnotSetFontSize(double pt) {
		if (!annotMode) setannotmode(true);
		annotSurface?.ApplyFontSize(pt);
	}

	public bool SaveAnnots() {
		if (string.IsNullOrEmpty(pdfPath)) return false;
		return annotDoc.Save(pdfPath);
	}

	public string AnnotFilePath => PdfAnnotDoc.PathForPdf(pdfPath);

	/// <summary>
	/// 将当前标注栅格化烧入 PDF 后另存。不覆盖源文件时传入 outPath。
	/// 成功返回输出路径；无标注或失败抛异常/返回 null。
	/// </summary>
	public string SaveAnnotsAsPdf(string outPath) {
		if (session == null || pageSizesPt == null || pageCount <= 0)
			throw new InvalidOperationException("文档未打开");
		if (string.IsNullOrWhiteSpace(outPath))
			throw new ArgumentException("路径无效", nameof(outPath));
		outPath = Path.GetFullPath(outPath);
		// 先落盘 JSON 旁路
		try { flushannotsave(); } catch { /* ignore */ }
		if (annotDoc.Items.Count == 0)
			throw new InvalidOperationException("当前没有标注可写入");

		// 有视图旋转时仍按原始页渲染叠加（与 SaveEdits 一致）
		PdfAnnotSave.SaveRasterized(session, pageSizesPt, annotDoc.Items, outPath);
		DocViewLog.Info($"PdfViewer.SaveAnnotsAsPdf ok path={outPath} items={annotDoc.Items.Count}");
		return outPath;
	}

	public PdfEditItem SelectedEdit => editSurface?.Selected;

	public void EditSetToolSelect() {
		if (editSurface != null) editSurface.CurrentTool = PdfEditSurface.Tool.Select;
	}

	public void EditSetToolAddText() {
		if (!editMode) seteditmode(true);
		if (editSurface != null) editSurface.CurrentTool = PdfEditSurface.Tool.AddText;
	}

	public void EditSetToolAddImage(string path = null) {
		if (!editMode) seteditmode(true);
		if (editSurface == null) return;
		if (string.IsNullOrWhiteSpace(path)) {
			var dlg = new Microsoft.Win32.OpenFileDialog {
				Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
				Title = "插入图片",
			};
			if (dlg.ShowDialog() != true) return;
			path = dlg.FileName;
		}
		editSurface.ArmAddImage(path);
	}

	public void EditDeleteSelected() => editSurface?.DeleteSelected();

	public void EditSetFont(string name) {
		if (editSurface == null || string.IsNullOrWhiteSpace(name)) return;
		editSurface.DefaultFont = name.Trim();
		editSurface.ApplyStyleToSelected(it => {
			if (it.Kind == PdfEditKind.Text) it.FontName = name.Trim();
		});
	}

	public void EditSetFontSize(double pt) {
		if (editSurface == null) return;
		if (pt < 6) pt = 6;
		if (pt > 96) pt = 96;
		editSurface.DefaultFontSize = pt;
		editSurface.ApplyStyleToSelected(it => {
			if (it.Kind == PdfEditKind.Text) {
				it.FontSizePt = pt;
				it.H = Math.Max(it.H, pt * 1.6);
			}
		});
	}

	public void EditToggleBold() {
		editSurface?.ApplyStyleToSelected(it => {
			if (it.Kind == PdfEditKind.Text) it.Bold = !it.Bold;
		});
		if (editSurface != null)
			editSurface.DefaultBold = editSurface.Selected?.Bold ?? editSurface.DefaultBold;
	}

	public void EditToggleItalic() {
		editSurface?.ApplyStyleToSelected(it => {
			if (it.Kind == PdfEditKind.Text) it.Italic = !it.Italic;
		});
		if (editSurface != null)
			editSurface.DefaultItalic = editSurface.Selected?.Italic ?? editSurface.DefaultItalic;
	}

	public void EditSetForeColor(WpfColor c) {
		if (editSurface == null) return;
		editSurface.DefaultFore = c;
		editSurface.ApplyStyleToSelected(it => {
			if (it.Kind == PdfEditKind.Text) it.ForeColor = c;
		});
	}

	public void EditSetBackColor(WpfColor? c) {
		editSurface?.ApplyStyleToSelected(it => {
			if (it.Kind == PdfEditKind.Text) it.BackColor = c;
		});
	}

	/// <summary>
	/// 编辑模式下点中页上已有内容：优先图片，其次文字行/词，转为可编辑覆盖层。
	/// 原理：白底盖住原文 + 可改文字/可拖图片（非真正改 PDF 内部对象流）。
	/// </summary>
	bool trycaptureexisting(int page, double xPt, double yPt) {
		if (page < 0 || page >= pageCount) return false;
		// 1) 已有编辑对象命中则不再捕获
		foreach (var it in editDoc.OnPage(page)) {
			if (xPt >= it.X && xPt <= it.X + it.W && yPt >= it.Y && yPt <= it.Y + it.H)
				return false;
		}
		// 2) 图片
		if (trycaptureimage(page, xPt, yPt)) return true;
		// 3) 文字（点附近一行/一词）
		return trycapturetext(page, xPt, yPt);
	}

	bool trycaptureimage(int page, double xPt, double yPt) {
		if (session == null) return false;
		List<PdfImageInfo> imgs = null;
		try {
			PdfIo.WithLock(() => {
				if (session == null) return;
				imgs = session.ListImageBounds(page);
			});
		} catch (Exception ex) {
			DocViewLog.Warn($"trycaptureimage: {ex.Message}");
			return false;
		}
		if (imgs == null || imgs.Count == 0) return false;
		if (pageRotate != 0) {
			var ow = pageSizesPt[page].Width;
			var oh = pageSizesPt[page].Height;
			foreach (var img in imgs)
				mapboxrotate(ref img.Left, ref img.Top, ref img.Right, ref img.Bottom, ow, oh, pageRotate);
		}
		PdfImageInfo best = null;
		var bestArea = double.MaxValue;
		foreach (var img in imgs) {
			if (xPt < img.Left - 1 || xPt > img.Right + 1 || yPt < img.Top - 1 || yPt > img.Bottom + 1)
				continue;
			var area = Math.Max(1, (img.Right - img.Left) * (img.Bottom - img.Top));
			if (area < bestArea) {
				bestArea = area;
				best = img;
			}
		}
		if (best == null) return false;

		BitmapSource bmp = null;
		var objIdx = best.ObjectIndex;
		try {
			PdfIo.WithLock(() => {
				if (session == null) return;
				bmp = session.ExtractImageBitmap(page, objIdx);
			});
		} catch (Exception ex) {
			DocViewLog.Warn($"extract image: {ex.Message}");
		}
		if (bmp == null) return false;

		byte[] png;
		try {
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(bmp));
			using var ms = new System.IO.MemoryStream();
			enc.Save(ms);
			png = ms.ToArray();
		} catch {
			return false;
		}

		var pad = 0.5;
		// 白底遮罩盖住原图
		var cover = new PdfEditItem {
			Page = page,
			Kind = PdfEditKind.Whiteout,
			X = Math.Max(0, best.Left - pad),
			Y = Math.Max(0, best.Top - pad),
			W = Math.Max(4, best.Right - best.Left + pad * 2),
			H = Math.Max(4, best.Bottom - best.Top + pad * 2),
			BackColor = WpfColor.FromRgb(0xFF, 0xFF, 0xFF),
		};
		var imgItem = new PdfEditItem {
			Page = page,
			Kind = PdfEditKind.Image,
			X = cover.X,
			Y = cover.Y,
			W = cover.W,
			H = cover.H,
			ImagePng = png,
		};
		editDoc.Items.Add(cover);
		editSurface?.AdoptItem(imgItem);
		editDirty = true;
		DocViewLog.Info($"pdf capture image p={page} obj={objIdx} {cover.W:F0}x{cover.H:F0}pt");
		return true;
	}

	bool trycapturetext(int page, double xPt, double yPt) {
		var chars = getcharssync(page);
		if (chars == null || chars.Count == 0) return false;
		// 找距点击最近的字符
		PdfCharInfo best = null;
		var bestD = 36.0 * 36.0; // 约 36pt 内
		foreach (var ch in chars) {
			var cx = (ch.Left + ch.Right) * 0.5;
			var cy = (ch.Top + ch.Bottom) * 0.5;
			var dx = cx - xPt;
			var dy = cy - yPt;
			var d = dx * dx + dy * dy;
			// 也算盒内命中
			if (xPt >= ch.Left - 1 && xPt <= ch.Right + 1 && yPt >= ch.Top - 1 && yPt <= ch.Bottom + 1)
				d = 0;
			if (d < bestD) {
				bestD = d;
				best = ch;
			}
		}
		if (best == null) return false;

		// 扩展为同一「行」：垂直中心接近、水平相邻
		var lineY = (best.Top + best.Bottom) * 0.5;
		var lineH = Math.Max(4, best.Bottom - best.Top);
		var line = new List<PdfCharInfo>();
		foreach (var ch in chars) {
			var cy = (ch.Top + ch.Bottom) * 0.5;
			if (Math.Abs(cy - lineY) > lineH * 0.55) continue;
			line.Add(ch);
		}
		line.Sort((a, b) => a.Left.CompareTo(b.Left));
		// 再扩成词：从 best 向左右延伸，间隙过大则停
		var bi = line.FindIndex(c => c.Index == best.Index);
		if (bi < 0) bi = 0;
		var lo = bi;
		var hi = bi;
		while (lo > 0) {
			var gap = line[lo].Left - line[lo - 1].Right;
			if (gap > lineH * 0.8) break;
			lo--;
		}
		while (hi < line.Count - 1) {
			var gap = line[hi + 1].Left - line[hi].Right;
			if (gap > lineH * 0.8) break;
			hi++;
		}
		// 若词太短（1 字），扩成整行片段（同水平间隙 < 2*lineH）
		if (hi - lo < 1) {
			lo = bi;
			hi = bi;
			while (lo > 0 && line[lo].Left - line[lo - 1].Right < lineH * 2) lo--;
			while (hi < line.Count - 1 && line[hi + 1].Left - line[hi].Right < lineH * 2) hi++;
		}

		double minL = double.MaxValue, minT = double.MaxValue, maxR = double.MinValue, maxB = double.MinValue;
		var sb = new System.Text.StringBuilder();
		for (var i = lo; i <= hi; i++) {
			var ch = line[i];
			sb.Append(ch.Char);
			if (ch.Left < minL) minL = ch.Left;
			if (ch.Top < minT) minT = ch.Top;
			if (ch.Right > maxR) maxR = ch.Right;
			if (ch.Bottom > maxB) maxB = ch.Bottom;
		}
		if (minL >= maxR) return false;
		var pad = 2.0;
		var fontSz = Math.Max(8, maxB - minT);
		var it = new PdfEditItem {
			Page = page,
			Kind = PdfEditKind.Text,
			X = Math.Max(0, minL - pad),
			Y = Math.Max(0, minT - pad),
			W = Math.Max(20, maxR - minL + pad * 2),
			H = Math.Max(12, maxB - minT + pad * 2),
			Text = sb.ToString(),
			FontName = editSurface?.DefaultFont ?? "Microsoft YaHei",
			FontSizePt = fontSz,
			Bold = editSurface?.DefaultBold ?? false,
			Italic = editSurface?.DefaultItalic ?? false,
			ForeColor = editSurface?.DefaultFore ?? WpfColor.FromRgb(0x11, 0x18, 0x27),
			BackColor = WpfColor.FromRgb(0xFF, 0xFF, 0xFF),
		};
		editSurface?.AdoptItem(it);
		editDirty = true;
		DocViewLog.Info($"pdf capture text p={page} \"{it.Text}\" @({it.X:F0},{it.Y:F0})");
		return true;
	}

	/// <summary>
	/// 从当前文字选区生成可编辑覆盖（白底 + 原文），便于“修改”已有文字。
	/// </summary>
	public bool EditFromTextSelection() {
		if (!editMode) seteditmode(true);
		if (selPage < 0 || selStart < 0 || selEnd < 0 || pageCount <= 0) return false;
		var chars = getcharssync(selPage);
		if (chars == null || chars.Count == 0) return false;
		var a = Math.Min(selStart, selEnd);
		var b = Math.Max(selStart, selEnd);
		double minL = double.MaxValue, minT = double.MaxValue, maxR = double.MinValue, maxB = double.MinValue;
		var sb = new System.Text.StringBuilder();
		foreach (var ch in chars) {
			if (ch.Index < a || ch.Index > b) continue;
			sb.Append(ch.Char);
			// 字符盒：页面左上原点 Y 向下（mapcharlistrotate 后）
			if (ch.Left < minL) minL = ch.Left;
			if (ch.Top < minT) minT = ch.Top;
			if (ch.Right > maxR) maxR = ch.Right;
			if (ch.Bottom > maxB) maxB = ch.Bottom;
		}
		if (minL >= maxR || minT >= maxB) return false;
		viewpagesizept(selPage, out var ptW, out var ptH);
		// ch 坐标已是 pt（页空间）
		var pad = 2.0;
		var it = new PdfEditItem {
			Page = selPage,
			Kind = PdfEditKind.Text,
			X = Math.Max(0, minL - pad),
			Y = Math.Max(0, minT - pad),
			W = Math.Max(20, maxR - minL + pad * 2),
			H = Math.Max(12, maxB - minT + pad * 2),
			Text = sb.ToString(),
			FontName = editSurface?.DefaultFont ?? "Microsoft YaHei",
			FontSizePt = editSurface?.DefaultFontSize ?? 12,
			Bold = editSurface?.DefaultBold ?? false,
			Italic = editSurface?.DefaultItalic ?? false,
			ForeColor = editSurface?.DefaultFore ?? WpfColor.FromRgb(0x11, 0x18, 0x27),
			BackColor = WpfColor.FromRgb(0xFF, 0xFF, 0xFF),
		};
		editDoc.DeselectAll();
		it.Selected = true;
		editDoc.Items.Add(it);
		editSurface?.RebuildAll();
		editDirty = true;
		clearsel();
		drawselection();
		try { DirtyChanged?.Invoke(); } catch { /* ignore */ }
		try { EditSelectionChanged?.Invoke(); } catch { /* ignore */ }
		raisestatus();
		return true;
	}

	public void SaveEdits(string path = null) {
		path = string.IsNullOrWhiteSpace(path) ? pdfPath : System.IO.Path.GetFullPath(path);
		if (string.IsNullOrWhiteSpace(path))
			throw new InvalidOperationException("无保存路径");
		if (session == null || pageSizesPt == null)
			throw new InvalidOperationException("文档未打开");
		// 旋转视图下保存使用原始页尺寸（当前仅支持 0° 精确叠加；有旋转时仍按原始渲染）
		var sizes = pageSizesPt;
		PdfEditSave.SaveRasterized(session, sizes, editDoc.Items, path);
		pdfPath = path;
		FilePath = path;
		Title = System.IO.Path.GetFileName(path);
		// 重新加载保存后的文件
		byte[] bytes;
		try {
			bytes = DocFileIo.ReadAllBytesShared(path);
		} catch {
			bytes = System.IO.File.ReadAllBytes(path);
		}
		pdfBytes = bytes;
		PdfIo.WithLock(() => {
			session?.Dispose();
			session = PdfiumSession.Open(pdfBytes);
		});
		pageCount = session.PageCount;
		pageSizesPt = session.PageSizesPt;
		editDoc.Clear();
		editDirty = false;
		editSurface?.RebuildAll();
		clearcache();
		clearslots();
		gen++;
		recalcmetrics();
		scheduleui();
		try { DirtyChanged?.Invoke(); } catch { /* ignore */ }
		raisestatus();
		DocViewLog.Info($"PdfViewer.SaveEdits ok path={path}");
	}

	public void Dispose() {
		if (disposed) return;
		// 先落盘标注再标 disposed
		try { flushannotsave(); } catch { /* ignore */ }
		disposed = true;
		gen++;
		workerStop = true;
		queuePulse.Set();
		cancelall();
		clearslots();
		clearcache();
		cleartextcache();
		try {
			if (dpiHooked) {
				var win = Window.GetWindow(root);
				if (win != null) win.DpiChanged -= onwindowdpichanged;
				dpiHooked = false;
			}
		} catch { /* ignore */ }
		try {
			if (outlineDebounce != null) {
				outlineDebounce.Stop();
				outlineDebounce = null;
			}
		} catch { /* ignore */ }
		try {
			if (zoomRenderTimer != null) {
				zoomRenderTimer.Stop();
				zoomRenderTimer = null;
			}
		} catch { /* ignore */ }
		try {
			if (annotSaveTimer != null) {
				annotSaveTimer.Stop();
				annotSaveTimer = null;
			}
		} catch { /* ignore */ }
		zoomHold = false;
		PdfIo.WithLock(() => {
			session?.Dispose();
			session = null;
		});
		pdfBytes = null;
		pdfPath = null;
		try { queuePulse.Dispose(); } catch { /* ignore */ }
	}

	// ---------- 输入：缩放 / 平移 ----------
	void initzoominput() {
		scroller.PreviewMouseWheel += (_, e) => {
			if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) {
				// 普通滚轮 = 用户要滚，先解除缩放钉
				clearzoompin();
				return;
			}
			e.Handled = true;
			setzoomcore(nextzoom(e.Delta > 0), e.GetPosition(scroller));
		};
		// 中键平移（左键空白平移在 selLayer 上处理）
		scroller.PreviewMouseDown += (_, e) => {
			if (e.ChangedButton != MouseButton.Middle) return;
			beginpan(e.GetPosition(scroller), capture: scroller);
			e.Handled = true;
		};
		scroller.PreviewMouseMove += (_, e) => {
			if (!panning) return;
			dopan(e.GetPosition(scroller));
			e.Handled = true;
		};
		scroller.PreviewMouseUp += (_, e) => {
			if (!panning) return;
			if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Left) return;
			endpan();
			e.Handled = true;
		};
		scroller.LostMouseCapture += (_, _) => {
			if (panning) endpan();
			selecting = false;
		};
		// 双击：图片 → 预览（和右键菜单「预览图片」同一份实现）；空白 → 100% ⇄ 适宽。
		// 必须走 Preview：selLayer 的 MouseLeftButtonDown 每次都把 Handled 置真（选字/平移），
		// Control.MouseDoubleClick 见到 Handled 就不再抛，挂 MouseDoubleClick 等于永远收不到。
		scroller.PreviewMouseLeftButtonDown += (_, e) => {
			if (e.ClickCount != 2) return;
			if (editMode || annotMode) return;
			if (isscrollbarsource(e.OriginalSource as DependencyObject)) return;
			// 第一下可能已经起了平移/选字，双击时先收拾干净再动作
			if (panning) endpan();
			if (selecting) {
				selecting = false;
				try { selLayer.ReleaseMouseCapture(); } catch { /* ignore */ }
			}
			var cpt = e.GetPosition(selLayer);
			if (trypreviewimage(cpt)) {
				e.Handled = true;
				return;
			}
			// 点在字上：这一下是选词（原来的实现也是让选词吃掉双击），别顺手切缩放
			if (hitchar(cpt, out var textPage, out var textIndex, strict: true)) {
				_ = textPage;
				_ = textIndex;
				return;
			}
			e.Handled = true;
			if (Math.Abs(zoom - 1.0) < 0.05) ZoomFitWidth();
			else setzoomcore(1.0, e.GetPosition(scroller));
		};
	}

	/// <summary>自测用：第 page 页第一张图的中心在视图里的位置；没图返回 null。</summary>
	internal WpfPoint? ImageCanvasPoint(int page) => imagecenterpt(page);

	/// <summary>自测用：当前缩放倍数。</summary>
	internal double ZoomForTest => zoom;

	/// <summary>自测用：页左边那片灰底上的一个点（点这里必然不是图片，用来测空白双击）。</summary>
	internal WpfPoint? BlankCanvasPoint(int page) {
		if (page < 0 || page >= pageCount) return null;
		var left = Math.Max(0, (contentW - pageW[page]) / 2);
		var x = left - 120;
		if (x < 20) x = Math.Max(20, left + pageW[page] + 120);
		return new WpfPoint(x, pageTop[page] + pageH[page] * 0.5);
	}

	/// <summary>自测用：把视图内坐标换成屏幕像素（真鼠标点击用）。</summary>
	internal WpfPoint? CanvasPointToScreen(WpfPoint pt) {
		try {
			// PointToScreen 本来就是设备像素，别再乘一次 DPI
			return selLayer.PointToScreen(pt);
		} catch { return null; }
	}

	/// <summary>自测用：把视图内某点滚到视口中央，让真鼠标点得到。</summary>
	internal void ScrollToImagePoint(WpfPoint pt) {
		try {
			var vx = scroller.ViewportWidth;
			var vy = scroller.ViewportHeight;
			scroller.ScrollToVerticalOffset(Math.Max(0, pt.Y - vy * 0.5));
			scroller.ScrollToHorizontalOffset(Math.Max(0, pt.X - vx * 0.5));
			scroller.UpdateLayout();
		} catch { /* ignore */ }
	}

	/// <summary>点在图片上就开预览，返回是否命中（双击与右键菜单共用）。</summary>
	bool trypreviewimage(WpfPoint canvasPt) {
		try {
			var hit = hitimage(canvasPt);
			if (hit?.Bitmap == null) return false;
			showimagepreview(hit.Bitmap);
			return true;
		} catch (Exception ex) {
			DocViewLog.Warn($"preview pdf image: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// 自测用：在第 page 页第一张图的中心走一遍双击的命中与预览，
	/// 返回「命中/未命中 open=…」；这页没图返回空串。
	/// </summary>
	internal string DebugPreviewFirstImage(int page) {
		var c = imagecenterpt(page);
		if (c == null) return "";
		var hit = trypreviewimage(c.Value);
		var size = hit ? "命中" : "未命中";
		return $"{size} open={ImageOverlay.IsOpen}";
	}

	void beginpan(WpfPoint scrollerPt, IInputElement capture) {
		clearzoompin();
		panning = true;
		selecting = false;
		panStart = scrollerPt;
		panOffX = scroller.HorizontalOffset;
		panOffY = scroller.VerticalOffset;
		try { capture?.CaptureMouse(); } catch { /* ignore */ }
		scroller.Cursor = Cursors.Hand;
		selLayer.Cursor = Cursors.Hand;
	}

	void dopan(WpfPoint scrollerPt) {
		if (!panning) return;
		scroller.ScrollToHorizontalOffset(Math.Max(0, panOffX - (scrollerPt.X - panStart.X)));
		scroller.ScrollToVerticalOffset(Math.Max(0, panOffY - (scrollerPt.Y - panStart.Y)));
	}

	bool iszoompinactive() =>
		zoomPinUntil != 0
		&& unchecked(Environment.TickCount - zoomPinUntil) < 0
		&& zoomPinH >= 0 && zoomPinV >= 0;

	void clearzoompin() {
		zoomPinUntil = 0;
		zoomPinH = zoomPinV = -1;
	}

	void setzoompin(double h, double v) {
		zoomPinH = h;
		zoomPinV = v;
		zoomPinUntil = Environment.TickCount + ZOOM_PIN_MS;
	}

	void endpan() {
		if (!panning) return;
		panning = false;
		try { Mouse.Capture(null); } catch { /* ignore */ }
		scroller.Cursor = Cursors.Arrow;
		selLayer.Cursor = Cursors.Arrow;
	}

	void initselection() {
		selLayer.MouseLeftButtonDown += onseldown;
		selLayer.MouseMove += onselmove;
		selLayer.MouseLeftButtonUp += onselup;
		selLayer.Cursor = Cursors.Arrow;
		// 焦点与系统 Copy 命令：否则选区在后 Ctrl+C 常到不了本控件
		root.Focusable = true;
		scroller.Focusable = true;
		root.CommandBindings.Add(new CommandBinding(
			ApplicationCommands.Copy,
			(_, e) => {
				if (copyselection()) e.Handled = true;
			},
			(_, e) => {
				e.CanExecute = hassel();
				e.Handled = true;
			}));
		root.InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, Key.C, ModifierKeys.Control));
		scroller.CommandBindings.Add(new CommandBinding(
			ApplicationCommands.Copy,
			(_, e) => {
				if (copyselection()) e.Handled = true;
			},
			(_, e) => {
				e.CanExecute = hassel();
				e.Handled = true;
			}));
		root.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
				if (copyselection()) e.Handled = true;
			}
			if (e.Key == Key.Escape) {
				clearsel();
				drawselection();
				raisestatus();
			}
		};
		scroller.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
				if (copyselection()) e.Handled = true;
			}
		};
		// 右键菜单：文字复制 + 图片预览/复制/另存
		mnCopyText = new MenuItem { Header = "复制文字(_C)", InputGestureText = "Ctrl+C" };
		mnCopyText.Click += (_, _) => copyselection();
		mnViewImg = new MenuItem { Header = "预览图片" };
		mnViewImg.Click += (_, _) => {
			if (ctxImage?.Bitmap != null) showimagepreview(ctxImage.Bitmap);
		};
		mnCopyImg = new MenuItem { Header = "复制图片" };
		mnCopyImg.Click += (_, _) => copyctximage();
		mnSaveImg = new MenuItem { Header = "图片另存为…" };
		mnSaveImg.Click += (_, _) => savectximage();
		var cm = new ContextMenu();
		cm.Opened += onctxopened;
		cm.Items.Add(mnCopyText);
		cm.Items.Add(new Separator());
		cm.Items.Add(mnViewImg);
		cm.Items.Add(mnCopyImg);
		cm.Items.Add(mnSaveImg);
		selLayer.ContextMenu = cm;
		canvas.ContextMenu = cm;
	}

	bool hassel() => selPage >= 0 && selStart >= 0 && selEnd >= 0;

	// ---------- 右键：文字 / 图片 ----------
	void onctxopened(object sender, RoutedEventArgs e) {
		ctxImage = null;
		mnCopyText.IsEnabled = hassel();
		if (mnViewImg != null) mnViewImg.IsEnabled = false;
		mnCopyImg.IsEnabled = false;
		mnSaveImg.IsEnabled = false;
		try {
			var cm = sender as ContextMenu;
			// 取鼠标相对 selLayer 的位置
			var pt = Mouse.GetPosition(selLayer);
			if (selLayer.IsMouseOver || canvas.IsMouseOver)
				pt = Mouse.GetPosition(selLayer);
			ctxImage = hitimage(pt);
			if (ctxImage?.Bitmap != null) {
				if (mnViewImg != null) mnViewImg.IsEnabled = true;
				mnCopyImg.IsEnabled = true;
				mnSaveImg.IsEnabled = true;
			}
		} catch (Exception ex) {
			DocViewLog.Warn($"ctx open: {ex.Message}");
		}
	}

	/// <summary>
	/// 自测用：走右键菜单那条路——按页内坐标找图填进 ctxImage，再点「预览图片」，
	/// 返回 open=True/False；这页没图返回空串。
	/// </summary>
	internal string DebugPreviewViaMenuItem(int page) {
		var pt = imagecenterpt(page);
		if (pt == null) return "";
		ctxImage = hitimage(pt.Value);
		if (ctxImage?.Bitmap == null) return "未命中";
		mnViewImg.IsEnabled = true;
		mnViewImg.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
		return $"命中 open={ImageOverlay.IsOpen}";
	}

	/// <summary>第 page 页第一张图的中心点（canvas dip）；页内没图返回 null。</summary>
	WpfPoint? imagecenterpt(int page) {
		if (session == null || page < 0 || page >= pageCount) return null;
		List<PdfImageInfo> imgs = null;
		try {
			PdfIo.WithLock(() => {
				if (session == null) return;
				imgs = session.ListImageBounds(page);
			});
		} catch (Exception ex) {
			DocViewLog.Warn($"imagecenterpt: {ex.Message}");
			return null;
		}
		if (imgs == null || imgs.Count == 0) return null;
		var img = imgs[0];
		if (pageRotate != 0) {
			var ow = pageSizesPt[page].Width;
			var oh = pageSizesPt[page].Height;
			mapboxrotate(ref img.Left, ref img.Top, ref img.Right, ref img.Bottom, ow, oh, pageRotate);
		}
		viewpagesizept(page, out var vpw, out var vph);
		// 页坐标 pt → canvas dip（hitimage 的逆运算）
		var left = Math.Max(0, (contentW - pageW[page]) / 2);
		return new WpfPoint(
			left + (img.Left + img.Right) * 0.5 * pageW[page] / Math.Max(1, vpw),
			pageTop[page] + (img.Top + img.Bottom) * 0.5 * pageH[page] / Math.Max(1, vph));
	}

	PdfImageInfo hitimage(WpfPoint canvasPt) {
		if (pageCount <= 0 || session == null) return null;
		var p = findpageat(canvasPt.Y);
		if (p < 0 || p >= pageCount) return null;
		var left = Math.Max(0, (contentW - pageW[p]) / 2);
		var top = pageTop[p];
		var relX = canvasPt.X - left;
		var relY = canvasPt.Y - top;
		if (relY < -4 || relY > pageH[p] + 4) return null;
		viewpagesizept(p, out var vpw, out var vph);
		var sx = vpw / pageW[p];
		var sy = vph / pageH[p];
		var px = relX * sx;
		var py = relY * sy;

		List<PdfImageInfo> imgs = null;
		try {
			PdfIo.WithLock(() => {
				if (session == null) return;
				imgs = session.ListImageBounds(p);
			});
		} catch (Exception ex) {
			DocViewLog.Warn($"ListImageBounds p={p}: {ex.Message}");
			return null;
		}
		if (imgs == null || imgs.Count == 0) return null;
		// 图片边界转到当前旋转后的视图坐标
		if (pageRotate != 0) {
			var ow = pageSizesPt[p].Width;
			var oh = pageSizesPt[p].Height;
			foreach (var img in imgs)
				mapboxrotate(ref img.Left, ref img.Top, ref img.Right, ref img.Bottom, ow, oh, pageRotate);
		}

		// 命中包含点的最小图片（logo 常叠在大图上）
		PdfImageInfo best = null;
		var bestArea = double.MaxValue;
		foreach (var img in imgs) {
			if (px < img.Left - 1 || px > img.Right + 1 || py < img.Top - 1 || py > img.Bottom + 1)
				continue;
			var area = Math.Max(1, (img.Right - img.Left) * (img.Bottom - img.Top));
			if (area < bestArea) {
				bestArea = area;
				best = img;
			}
		}
		if (best == null) {
			var bestD = 144.0; // 12pt
			foreach (var img in imgs) {
				var mx = (img.Left + img.Right) * 0.5;
				var my = (img.Top + img.Bottom) * 0.5;
				var d = (mx - px) * (mx - px) + (my - py) * (my - py);
				if (d < bestD) {
					bestD = d;
					best = img;
				}
			}
		}
		if (best == null) return null;

		// 仅导出命中的一张
		try {
			BitmapSource bmp = null;
			var objIdx = best.ObjectIndex;
			PdfIo.WithLock(() => {
				if (session == null) return;
				bmp = session.ExtractImageBitmap(p, objIdx);
			});
			if (bmp == null) return null;
			best.Bitmap = bmp;
			return best;
		} catch (Exception ex) {
			DocViewLog.Warn($"ExtractImageBitmap: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// 双击图片和右键菜单「预览图片」共用：把抽出来的位图交给 ImageOverlay 弹层。
	/// 宿主锚点固定成这扇 PDF 视图，弹层才贴着阅读器开，而不是跑到主窗那边。
	/// </summary>
	void showimagepreview(BitmapSource bmp) {
		if (bmp == null) return;
		try {
			// 锚在这扇 PDF 视图上：多标签时弹层才贴着当前这个，不会飘到别的 tab
			ImageOverlay.HostAnchor = root;
			ImageOverlay.ActiveHost = Window.GetWindow(root);
		} catch { /* ignore */ }
		ImageOverlay.Show(bmp, Title);
	}

	/// <summary>点在滚动条上时不算文档双击（否则拉滚动条会被当成放大）。</summary>
	static bool isscrollbarsource(DependencyObject d) {
		while (d != null) {
			if (d is System.Windows.Controls.Primitives.ScrollBar) return true;
			if (d is ScrollViewer) return false;
			d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
				? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
		}
		return false;
	}

	void copyctximage() {
		if (ctxImage?.Bitmap == null) return;
		try {
			Clipboard.SetImage(ctxImage.Bitmap);
			DocViewLog.Info($"copy pdf image {ctxImage.Bitmap.PixelWidth}x{ctxImage.Bitmap.PixelHeight}");
		} catch (Exception ex) {
			DocViewLog.Error("Clipboard.SetImage", ex);
			MessageBox.Show("复制图片失败: " + ex.Message, "ScreenKit");
		}
	}

	void savectximage() {
		if (ctxImage?.Bitmap == null) return;
		try {
			var dlg = new SaveFileDialog {
				Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp",
				FileName = "image.png",
				Title = "图片另存为",
			};
			if (dlg.ShowDialog() != true) return;
			savebitmap(ctxImage.Bitmap, dlg.FileName);
			DocViewLog.Info($"save pdf image {dlg.FileName}");
		} catch (Exception ex) {
			DocViewLog.Error("save pdf image", ex);
			MessageBox.Show("保存失败: " + ex.Message, "ScreenKit");
		}
	}

	static void savebitmap(BitmapSource bi, string path) {
		BitmapEncoder enc;
		var ext = Path.GetExtension(path).ToLowerInvariant();
		if (ext == ".jpg" || ext == ".jpeg")
			enc = new JpegBitmapEncoder { QualityLevel = 92 };
		else if (ext == ".bmp")
			enc = new BmpBitmapEncoder();
		else
			enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(bi));
		using var fs = File.Create(path);
		enc.Save(fs);
	}

	// ---------- 选字 ----------
	void onseldown(object sender, MouseButtonEventArgs e) {
		if (panning) return;
		if (e.ChangedButton != MouseButton.Left) return;
		var pt = e.GetPosition(selLayer);

		// Ctrl+左键：书内链接跳转（或打开 URI）
		if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
			if (trylinkat(pt)) {
				e.Handled = true;
				return;
			}
		}

		// 预抽当前页文字（异步）
		var pageHint = findpageat(pt.Y);
		if (pageHint >= 0 && pageHint < pageCount)
			ensuretext(pageHint);

		// 只有点到字才选字；空白 = 平移
		if (!hitchar(pt, out var page, out var idx, strict: true)) {
			clearsel();
			drawselection();
			raisestatus();
			beginpan(e.GetPosition(scroller), capture: selLayer);
			e.Handled = true;
			return;
		}
		selecting = true;
		dragAnchorPage = page;
		dragAnchorChar = idx;
		selPage = page;
		selStart = idx;
		selEnd = idx;
		selLayer.CaptureMouse();
		try { scroller.Focus(); } catch { /* ignore */ }
		drawselection();
		raisestatus();
		e.Handled = true;
	}

	void onselmove(object sender, MouseEventArgs e) {
		// 左键空白拖：平移
		if (panning && e.LeftButton == MouseButtonState.Pressed) {
			dopan(e.GetPosition(scroller));
			e.Handled = true;
			return;
		}
		// 悬停光标：Ctrl+链接手型；字上 IBeam；空白箭头
		if (!selecting && !panning) {
			var hov = e.GetPosition(selLayer);
			if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && hitlink(hov) != null)
				selLayer.Cursor = Cursors.Hand;
			else
				selLayer.Cursor = hitchar(hov, out _, out _, strict: true) ? Cursors.IBeam : Cursors.Arrow;
		}
		if (!selecting || e.LeftButton != MouseButtonState.Pressed) return;
		var pt = e.GetPosition(selLayer);
		// 边缘自动滚
		var sp = e.GetPosition(scroller);
		if (sp.Y < 24) scroller.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset - 24));
		else if (sp.Y > scroller.ViewportHeight - 24)
			scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 24);

		var pageHint = findpageat(pt.Y);
		if (pageHint >= 0 && pageHint < pageCount && pageHint != dragAnchorPage)
			ensuretext(pageHint);

		// 拖选时放宽：行内空白也能延伸到行尾
		if (!hitchar(pt, out var page, out var idx, strict: false)) return;
		// 简化：同页拖选
		if (page == dragAnchorPage) {
			selPage = page;
			selStart = Math.Min(dragAnchorChar, idx);
			selEnd = Math.Max(dragAnchorChar, idx);
		} else {
			// 跨页：按页序
			List<PdfCharInfo> chars;
			lock (textCache) {
				if (!textCache.TryGetValue(dragAnchorPage, out chars) || chars == null || chars.Count == 0)
					return;
			}
			if (page > dragAnchorPage) {
				selPage = dragAnchorPage;
				selStart = Math.Min(dragAnchorChar, chars[chars.Count - 1].Index);
				selEnd = Math.Max(dragAnchorChar, chars[chars.Count - 1].Index);
			} else {
				selPage = dragAnchorPage;
				selStart = Math.Min(dragAnchorChar, chars[0].Index);
				selEnd = Math.Max(dragAnchorChar, chars[0].Index);
			}
		}
		drawselection();
		e.Handled = true;
	}

	void onselup(object sender, MouseButtonEventArgs e) {
		if (panning) {
			endpan();
			e.Handled = true;
			return;
		}
		if (!selecting) return;
		selecting = false;
		try { selLayer.ReleaseMouseCapture(); } catch { /* ignore */ }
		try { scroller.Focus(); } catch { /* ignore */ }
		raisestatus();
		e.Handled = true;
	}

	/// <param name="strict">true=必须点在字符框上（决定选字/平移）；false=行内空白可命中（拖选延伸）</param>
	bool hitchar(WpfPoint canvasPt, out int page, out int charIndex, bool strict = false) {
		page = -1;
		charIndex = -1;
		if (pageCount <= 0) return false;
		var p = findpageat(canvasPt.Y);
		if (p < 0 || p >= pageCount) return false;
		// 只用已缓存文字，绝不在 UI 线程同步抽字（1100 页手册会卡死）
		List<PdfCharInfo> chars;
		lock (textCache) {
			if (!textCache.TryGetValue(p, out chars) || chars == null || chars.Count == 0)
				return false;
			touchtext(p);
		}

		var left = Math.Max(0, (contentW - pageW[p]) / 2);
		var top = pageTop[p];
		// canvas → 页 pt（视图坐标，已含旋转）
		var relX = canvasPt.X - left;
		var relY = canvasPt.Y - top;
		if (relY < -8 || relY > pageH[p] + 8)
			return false;
		viewpagesizept(p, out var vpw, out var vph);
		var sx = vpw / pageW[p];
		var sy = vph / pageH[p];
		var px = relX * sx;
		var py = relY * sy;

		// 严格模式：必须落在某个字符包围盒内（小容差）
		if (strict) {
			const double pad = 2.5;
			PdfCharInfo best = null;
			var bestD = double.MaxValue;
			foreach (var c in chars) {
				if (char.IsWhiteSpace(c.Char) && c.Char != '\t') continue;
				if (px >= c.Left - pad && px <= c.Right + pad && py >= c.Top - pad && py <= c.Bottom + pad) {
					var mx = (c.Left + c.Right) * 0.5;
					var my = (c.Top + c.Bottom) * 0.5;
					var d = (mx - px) * (mx - px) + (my - py) * (my - py);
					if (d < bestD) { bestD = d; best = c; }
				}
			}
			if (best == null) return false;
			page = p;
			charIndex = best.Index;
			return true;
		}

		// 拖选模式：先按 Y 定视觉行，再按 X 定字（行首/行尾空白也能选到该行）
		var lines = grouplines(chars);
		if (lines.Count == 0) return false;

		var bestLine = -1;
		var bestDy = double.MaxValue;
		for (var i = 0; i < lines.Count; i++) {
			var line = lines[i];
			var t = line.Min(c => c.Top);
			var b = line.Max(c => c.Bottom);
			var mid = (t + b) * 0.5;
			var h = Math.Max(1, b - t);
			if (py >= t - h * 0.35 && py <= b + h * 0.35) {
				bestLine = i;
				bestDy = 0;
				break;
			}
			var dy = Math.Abs(py - mid);
			if (dy < bestDy) {
				bestDy = dy;
				bestLine = i;
			}
		}
		if (bestLine < 0) return false;
		{
			var line0 = lines[bestLine];
			var h0 = Math.Max(1, line0.Max(c => c.Bottom) - line0.Min(c => c.Top));
			if (bestDy > h0 * 1.6 && bestDy > 14) return false;
		}

		var ordered = lines[bestLine].OrderBy(c => c.Left).ToList();
		if (px <= ordered[0].Left) {
			page = p;
			charIndex = ordered[0].Index;
			return true;
		}
		if (px >= ordered[ordered.Count - 1].Right) {
			page = p;
			charIndex = ordered[ordered.Count - 1].Index;
			return true;
		}
		PdfCharInfo bestC = ordered[0];
		var bestDx = double.MaxValue;
		foreach (var c in ordered) {
			if (px >= c.Left - 1 && px <= c.Right + 1) {
				page = p;
				charIndex = c.Index;
				return true;
			}
			var mx = (c.Left + c.Right) * 0.5;
			var dx = Math.Abs(mx - px);
			if (dx < bestDx) {
				bestDx = dx;
				bestC = c;
			}
		}
		page = p;
		charIndex = bestC.Index;
		return true;
	}

	/// <summary>
	/// 后台异步抽字。调用方禁止在 UI 线程等待完成。
	/// </summary>
	void ensuretext(int page, Action onReady = null) {
		if (page < 0 || page >= pageCount || disposed) return;
		lock (textCache) {
			if (textCache.ContainsKey(page)) {
				touchtext(page);
				if (onReady != null) {
					var cb = onReady;
					scroller.Dispatcher.BeginInvoke(DispatcherPriority.Background, cb);
				}
				return;
			}
			if (textPending.Contains(page)) return;
			textPending.Add(page);
		}
		var g = gen;
		var pathLocal = pdfPath;
		Task.Run(() => {
			List<PdfCharInfo> chars = null;
			try {
				if (disposed || g != gen) return;
				PdfIo.WithLock(() => {
					if (disposed || session == null || g != gen || pdfPath != pathLocal) return;
					chars = session.ExtractChars(page);
				});
			} catch (Exception ex) {
				DocViewLog.Warn($"ExtractChars fail p={page}: {ex.Message}");
			} finally {
				if (chars != null) mapcharlistrotate(chars, page);
				lock (textCache) {
					textPending.Remove(page);
					if (chars != null && g == gen && !textCache.ContainsKey(page)) {
						textCache[page] = chars;
						textLru.Remove(page);
						textLru.AddLast(page);
						while (textLru.Count > MAX_TEXT_PAGES) {
							var old = textLru.First.Value;
							textLru.RemoveFirst();
							textCache.Remove(old);
						}
						DocViewLog.Info($"ExtractChars page={page} n={chars.Count}");
					}
				}
			}
			if (onReady == null || disposed || g != gen) return;
			try {
				scroller.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
					if (disposed || g != gen) return;
					onReady();
					if (selPage == page) drawselection();
				}));
			} catch { /* disposed */ }
		});
	}

	void touchtext(int page) {
		// 调用方已持 textCache 锁
		if (!textCache.ContainsKey(page)) return;
		textLru.Remove(page);
		textLru.AddLast(page);
	}

	// 查找：其它命中浅黄；当前命中金黄（Sumatra 风格）
	static readonly SolidColorBrush FindHitBrush = freezebrush(0x70, 0xFF, 0xF5, 0x9D);
	static readonly SolidColorBrush FindCurBrush = freezebrush(0xB0, 0xFF, 0xC1, 0x07);
	static readonly SolidColorBrush SelBrush = freezebrush(0x99, 0xFF, 0xF5, 0x9D);

	static SolidColorBrush freezebrush(byte a, byte r, byte g, byte b) {
		var br = new SolidColorBrush(WpfColor.FromArgb(a, r, g, b));
		if (br.CanFreeze) br.Freeze();
		return br;
	}

	void drawselection() {
		selLayer.Children.Clear();
		// 1) 屏幕内（可见页）全部查找命中
		drawfindhits();
		// 2) 当前选区（查找当前项或用户拖选）；与当前查找命中重合时由 drawfindhits 已用金黄画出
		if (!hassel()) return;
		if (findHits.Count > 0 && findIndex >= 0 && findIndex < findHits.Count) {
			var h = findHits[findIndex];
			if (selPage == h.Page && selStart == h.Start && selEnd == h.End)
				return;
		}
		addhitrects(selPage, selStart, selEnd, SelBrush);
	}

	/// <summary>可见页上的全部查找命中高亮；当前命中用更深色。</summary>
	void drawfindhits() {
		if (findHits.Count == 0 || string.IsNullOrEmpty(findQuery) || pageCount <= 0) return;
		var first = visFirst;
		var last = visLast;
		if (first > last) {
			// 布局尚未估出可见区时，至少覆盖当前选中页
			if (selPage >= 0) { first = last = selPage; }
			else return;
		}
		// 略扩一页，滚动半页时不闪
		first = Math.Max(0, first - 1);
		last = Math.Min(pageCount - 1, last + 1);

		for (var i = 0; i < findHits.Count; i++) {
			var h = findHits[i];
			if (h.Page < first || h.Page > last) continue;
			var cur = i == findIndex;
			addhitrects(h.Page, h.Start, h.End, cur ? FindCurBrush : FindHitBrush);
		}
	}

	void addhitrects(int page, int start, int end, SolidColorBrush brush) {
		if (page < 0 || page >= pageCount || start < 0 || end < 0 || brush == null) return;
		List<PdfCharInfo> chars;
		lock (textCache) {
			if (!textCache.TryGetValue(page, out chars) || chars == null) {
				// 可见页无字缓存时同步抽字（查找后跳页场景）
				chars = null;
			} else {
				touchtext(page);
			}
		}
		if (chars == null) {
			var ordered = getcharssync(page);
			if (ordered == null || ordered.Count == 0) return;
			chars = ordered;
		}

		var selected = new List<PdfCharInfo>();
		foreach (var c in chars) {
			if (c.Index < start || c.Index > end) continue;
			if (!char.IsWhiteSpace(c.Char)) selected.Add(c);
		}
		if (selected.Count == 0) {
			foreach (var c in chars) {
				if (c.Index >= start && c.Index <= end)
					selected.Add(c);
			}
		}
		if (selected.Count == 0) return;

		var lines = mergelinerects(selected);
		var left = Math.Max(0, (contentW - pageW[page]) / 2);
		var top = pageTop[page];
		viewpagesizept(page, out var vpw, out var vph);
		var sx = pageW[page] / Math.Max(0.01, vpw);
		var sy = pageH[page] / Math.Max(0.01, vph);
		foreach (var line in lines) {
			var r = new System.Windows.Shapes.Rectangle {
				Fill = brush,
				Width = Math.Max(1, (line.Right - line.Left) * sx),
				Height = Math.Max(1, (line.Bottom - line.Top) * sy),
				IsHitTestVisible = false,
			};
			Canvas.SetLeft(r, left + line.Left * sx);
			Canvas.SetTop(r, top + line.Top * sy);
			selLayer.Children.Add(r);
		}
	}

	/// <summary>
	/// 同行字符合并为一个矩形（参考安卓 PdfViewMapper.mergeLineRects）。
	/// </summary>
	static List<PdfCharInfo> mergelinerects(List<PdfCharInfo> chars) {
		var lines = new List<PdfCharInfo>();
		if (chars == null || chars.Count == 0) return lines;
		var sorted = chars.OrderBy(c => c.Top).ThenBy(c => c.Left).ToList();
		double sumH = 0;
		foreach (var c in sorted)
			sumH += Math.Max(1, c.Bottom - c.Top);
		var avgH = sumH / sorted.Count;
		var lineTol = avgH * 0.55;

		var curL = sorted[0].Left;
		var curT = sorted[0].Top;
		var curR = sorted[0].Right;
		var curB = sorted[0].Bottom;
		var curMidY = (curT + curB) * 0.5;
		for (var i = 1; i < sorted.Count; i++) {
			var c = sorted[i];
			var mid = (c.Top + c.Bottom) * 0.5;
			if (Math.Abs(mid - curMidY) <= lineTol) {
				if (c.Left < curL) curL = c.Left;
				if (c.Right > curR) curR = c.Right;
				if (c.Top < curT) curT = c.Top;
				if (c.Bottom > curB) curB = c.Bottom;
				curMidY = (curT + curB) * 0.5;
			} else {
				lines.Add(new PdfCharInfo { Left = curL, Top = curT, Right = curR, Bottom = curB });
				curL = c.Left;
				curT = c.Top;
				curR = c.Right;
				curB = c.Bottom;
				curMidY = mid;
			}
		}
		lines.Add(new PdfCharInfo { Left = curL, Top = curT, Right = curR, Bottom = curB });
		return lines;
	}

	bool copyselection() {
		if (selPage < 0 || selStart < 0) return false;
		List<PdfCharInfo> chars;
		lock (textCache) {
			if (!textCache.TryGetValue(selPage, out chars) || chars == null) return false;
			touchtext(selPage);
		}

		// 选中字符（含空白，用于判断段内空格）
		var selected = chars
			.Where(c => c.Index >= selStart && c.Index <= selEnd)
			.OrderBy(c => c.Index)
			.ToList();
		if (selected.Count == 0) return false;

		// 视觉行 → 段落：段内软换行用空格拼接，段间只一次 \r\n
		var lines = grouplines(selected);
		if (lines.Count == 0) return false;

		var lineTexts = new List<string>(lines.Count);
		var lineBottom = new double[lines.Count];
		var lineTop = new double[lines.Count];
		var lineLeft = new double[lines.Count];
		var lineRight = new double[lines.Count];
		var lineH = new double[lines.Count];
		double avgH = 0, avgW = 0;
		for (var i = 0; i < lines.Count; i++) {
			var ordered = lines[i].OrderBy(c => c.Left).ToList();
			lineTop[i] = ordered.Min(c => c.Top);
			lineBottom[i] = ordered.Max(c => c.Bottom);
			lineLeft[i] = ordered.Min(c => c.Left);
			lineRight[i] = ordered.Max(c => c.Right);
			lineH[i] = Math.Max(1, lineBottom[i] - lineTop[i]);
			avgH += lineH[i];
			avgW += Math.Max(1, lineRight[i] - lineLeft[i]);
			lineTexts.Add(linetext(ordered));
		}
		avgH /= lines.Count;
		avgW /= lines.Count;

		// 段间距：正常行距约 0.15~0.45 行高，段落常 ≥0.55 行高
		var paraGap = avgH * 0.55;
		// 页宽参考：用于“上一行明显偏短 → 段末”
		var fullLineW = avgW * 0.92;

		var paragraphs = new List<List<string>>();
		var curPara = new List<string> { lineTexts[0] };
		for (var i = 0; i < lines.Count - 1; i++) {
			var gap = lineTop[i + 1] - lineBottom[i];
			var curW = lineRight[i] - lineLeft[i];
			var nextLeft = lineLeft[i + 1];
			var curLeft = lineLeft[i];
			// 新段：行距明显加大，或上一行较短（段末），或下一行明显缩进/回退
			var isPara =
				gap > paraGap
				|| (curW < fullLineW && gap > avgH * 0.25)
				|| (nextLeft - curLeft > avgH * 0.8 && gap > avgH * 0.2)
				|| (curLeft - nextLeft > avgH * 0.8 && gap > avgH * 0.2);
			if (isPara) {
				paragraphs.Add(curPara);
				curPara = new List<string>();
			}
			curPara.Add(lineTexts[i + 1]);
		}
		paragraphs.Add(curPara);

		var sb = new StringBuilder();
		for (var p = 0; p < paragraphs.Count; p++) {
			if (p > 0) sb.Append("\r\n"); // 段间仅一次换行
			sb.Append(joinsoftlines(paragraphs[p]));
		}
		var s = sb.ToString().TrimEnd();
		if (s.Length == 0) return false;
		DocViewLog.Info($"copy paras={paragraphs.Count} lines={lines.Count} len={s.Length}");
		return setclipboard(s, lines.Count);
	}

	/// <summary>一行字符拼成字符串，压空白。</summary>
	static string linetext(List<PdfCharInfo> ordered) {
		var sb = new StringBuilder();
		foreach (var c in ordered) {
			if (c.Char == '\r' || c.Char == '\n') continue;
			sb.Append(c.Char);
		}
		// 行首尾空白去掉；行内多空白压成单空格
		var raw = sb.ToString().Trim();
		if (raw.Length == 0) return "";
		sb.Clear();
		var space = false;
		foreach (var ch in raw) {
			if (char.IsWhiteSpace(ch)) {
				space = true;
				continue;
			}
			if (space && sb.Length > 0) sb.Append(' ');
			space = false;
			sb.Append(ch);
		}
		return sb.ToString();
	}

	/// <summary>段内软换行：空格拼接；处理行末连字符。</summary>
	static string joinsoftlines(List<string> lines) {
		var sb = new StringBuilder();
		for (var i = 0; i < lines.Count; i++) {
			var t = lines[i] ?? "";
			if (t.Length == 0) continue;
			if (sb.Length == 0) {
				sb.Append(t);
				continue;
			}
			// 上一行以连字符结尾 → 直接拼（英文断词）
			if (sb.Length > 0 && sb[sb.Length - 1] == '-') {
				sb.Length--;
				sb.Append(t);
				continue;
			}
			// 中日韩后不插空格；否则补空格
			var prev = sb[sb.Length - 1];
			var next = t[0];
			if (iscjk(prev) || iscjk(next) || char.IsWhiteSpace(prev))
				sb.Append(t);
			else
				sb.Append(' ').Append(t);
		}
		return sb.ToString();
	}

	static bool iscjk(char c) =>
		(c >= 0x4E00 && c <= 0x9FFF)
		|| (c >= 0x3400 && c <= 0x4DBF)
		|| (c >= 0xF900 && c <= 0xFAFF)
		|| (c >= 0x3000 && c <= 0x303F)
		|| (c >= 0xFF00 && c <= 0xFFEF);

	static bool setclipboard(string s, int lineCount) {
		// SetText 偶发 COM 占用失败；SetDataObject 更稳，带重试
		Exception last = null;
		for (var i = 0; i < 3; i++) {
			try {
				Clipboard.SetDataObject(s, true);
				DocViewLog.Info($"copy sel len={s.Length} lines={lineCount}");
				return true;
			} catch (Exception ex) {
				last = ex;
				try { System.Threading.Thread.Sleep(40); } catch { /* ignore */ }
			}
		}
		try {
			Clipboard.SetText(s);
			DocViewLog.Info($"copy sel len={s.Length} lines={lineCount} (SetText)");
			return true;
		} catch (Exception ex) {
			DocViewLog.Error("Clipboard copy failed", last ?? ex);
			return false;
		}
	}

	/// <summary>将字符按行分组（与选区高亮同一规则）。</summary>
	static List<List<PdfCharInfo>> grouplines(List<PdfCharInfo> chars) {
		var result = new List<List<PdfCharInfo>>();
		if (chars == null || chars.Count == 0) return result;
		var sorted = chars.OrderBy(c => (c.Top + c.Bottom) * 0.5).ThenBy(c => c.Left).ToList();
		double sumH = 0;
		foreach (var c in sorted) sumH += Math.Max(1, c.Bottom - c.Top);
		var lineTol = (sumH / sorted.Count) * 0.55;

		var cur = new List<PdfCharInfo> { sorted[0] };
		var curMid = (sorted[0].Top + sorted[0].Bottom) * 0.5;
		for (var i = 1; i < sorted.Count; i++) {
			var c = sorted[i];
			var mid = (c.Top + c.Bottom) * 0.5;
			if (Math.Abs(mid - curMid) <= lineTol) {
				cur.Add(c);
				var t = cur.Min(x => x.Top);
				var b = cur.Max(x => x.Bottom);
				curMid = (t + b) * 0.5;
			} else {
				result.Add(cur);
				cur = new List<PdfCharInfo> { c };
				curMid = mid;
			}
		}
		result.Add(cur);
		return result;
	}

	void clearsel() {
		selPage = selStart = selEnd = -1;
		dragAnchorPage = dragAnchorChar = -1;
		selLayer.Children.Clear();
	}

	// ---------- 缩放核心：滚轮时立即改布局+钉滚动；停稳只重渲，不再改位置 ----------
	/// <param name="keepScroll">true=不改滚动（RestoreViewState 随后自己滚）。</param>
	void bakezoomimmediate(double z, bool keepScroll = false) {
		WpfPoint? mouse = null;
		if (!keepScroll && pageCount > 0 && scroller != null) {
			mouse = new WpfPoint(
				Math.Max(8, scroller.ViewportWidth * 0.5),
				Math.Max(8, scroller.ViewportHeight * 0.3));
		}
		applyzoomlayout(z, mouse, reuseLock: false, keepScroll: keepScroll);
	}

	/// <summary>Ctrl+滚轮：立即布局并限制在锚点位置；页外只锁 Y。</summary>
	void setzoomcore(double z, WpfPoint? anchorInScroller) {
		if (!anchorInScroller.HasValue) {
			bakezoomimmediate(z);
			return;
		}
		var now = Environment.TickCount;
		var mouse = anchorInScroller.Value;
		var reuse = zoomLockPage >= 0 && zoomLockPage < pageCount
			&& unchecked((int)(now - lastZoomTick)) >= 0
			&& unchecked((int)(now - lastZoomTick)) < ZOOM_LOCK_MS
			&& Math.Abs(mouse.Y - zoomLockMouse.Y) < 48
			&& (zoomLockXOnPage ? Math.Abs(mouse.X - zoomLockMouse.X) < 48 : true);
		if (reuse) {
			// 连续滚：Y 跟当前鼠标，页外 X 仍不参与
			if (zoomLockXOnPage)
				zoomLockMouse = mouse;
			else
				zoomLockMouse = new WpfPoint(zoomLockMouse.X, mouse.Y);
			mouse = zoomLockMouse;
		}
		lastZoomTick = now;
		applyzoomlayout(z, mouse, reuseLock: reuse, keepScroll: false);
	}

	/// <summary>
	/// 立即：重算布局 + 旧图整页铺满 + 滚动钉在锚点。
	/// 稍后：仅清缓存重渲，**不再改滚动**（避免 0.5s 后跳动）。
	/// </summary>
	void applyzoomlayout(double z, WpfPoint? mouseInScroller, bool reuseLock, bool keepScroll) {
		z = clamp(z, MIN_ZOOM, MAX_ZOOM);
		if (Math.Abs(z - zoom) < 0.0005 && Math.Abs(layoutZoom - z) < 0.0005)
			return;
		if (pageCount <= 0 || pageTop == null || pageW == null || pageH == null) {
			zoom = layoutZoom = z;
			recalcmetrics();
			return;
		}

		if (!keepScroll && mouseInScroller.HasValue) {
			if (!reuseLock)
				capturezoomlock(mouseInScroller.Value);
		} else if (!keepScroll && !mouseInScroller.HasValue) {
			var cx = Math.Max(8, scroller.ViewportWidth * 0.5);
			var cy = Math.Max(8, scroller.ViewportHeight * 0.3);
			var m = new WpfPoint(cx, cy);
			capturezoomlock(m);
			mouseInScroller = m;
		}

		var oldZ = zoom;
		var h0 = scroller.HorizontalOffset;
		var v0 = scroller.VerticalOffset;
		// 必须先解除 PIN，否则 ScrollChanged 会把新滚动拽回旧 pin（日志已证实）
		clearzoompin();
		zoom = layoutZoom = z;
		// hold 保持到 onzoomrender：期间可用邻近页位图建槽，禁止灰白占位
		zoomHold = true;
		// 立刻作废在途渲染/缓存：否则旧倍率分块会按新高度 Stretch → 压扁闪一下
		// （尤其 tilecount 不变时 tryapplyalltiles 会直接贴上旧条带）
		gen++;
		cancelall();
		clearcache();
		try {
			recalcmetrics();
			logzoomdiag("after_recalc");
			foreach (var kv in slots) {
				if (kv.Key < 0 || kv.Key >= pageCount) continue;
				// 多 tile → 合成单图软显示，避免条带在新高度下被 Fill 压扁
				forcesoftsingle(kv.Value);
			}
			logzoomdiag("after_soft");
			try {
				contentRoot.UpdateLayout();
				scroller.UpdateLayout();
			} catch { /* ignore */ }
			logzoomdiag("after_layout");
			if (!keepScroll && mouseInScroller.HasValue)
				applyzoomlockscroll(mouseInScroller.Value);
			logzoomdiag("after_scroll1");
			// 主动刷视口：新建槽用邻近位图作临时图，避免白页（hold 中禁止贴缓存分块）
			updateviewport(gen);
			logzoomdiag("after_viewport");
			if (!keepScroll && mouseInScroller.HasValue)
				applyzoomlockscroll(mouseInScroller.Value);
			logzoomdiag("after_scroll2");
		} catch (Exception ex) {
			DocViewLog.Error("applyzoomlayout", ex);
		}
		// 注意：此处不放开 zoomHold，等 onzoomrender 结束

		clearsel();
		raisestatus();
		if (!keepScroll)
			setzoompin(scroller.HorizontalOffset, scroller.VerticalOffset);
		schedulenewrender();
		DocViewLog.Info(
			$"zoom apply z={oldZ:F3}->{zoom:F3} p={zoomLockPage} " +
			$"frac=({zoomLockFracX:F3},{zoomLockFracY:F3}) xOnPage={zoomLockXOnPage} " +
			$"scroll {h0:F0},{v0:F0} -> {scroller.HorizontalOffset:F0},{scroller.VerticalOffset:F0} " +
			$"pin=({zoomPinH:F0},{zoomPinV:F0}) reuse={reuseLock} keepScroll={keepScroll} " +
			$"cw={contentW:F0} ch={contentH:F0} hold={zoomHold}");
		logzoomdiag("apply_end");
	}

	/// <summary>缩放诊断：槽位位置、图源纵横比 vs 布局、滚动。</summary>
	void logzoomdiag(string tag) {
		try {
			var vis = estimatepage();
			var sb = new StringBuilder();
			sb.Append($"zoomdiag[{tag}] z={zoom:F3} lz={layoutZoom:F3} hold={zoomHold} gen={gen} ");
			sb.Append($"cw={contentW:F0} ch={contentH:F0} ");
			sb.Append($"scroll=({scroller?.HorizontalOffset:F0},{scroller?.VerticalOffset:F0}) ");
			sb.Append($"vp=({scroller?.ViewportWidth:F0}x{scroller?.ViewportHeight:F0}) ");
			sb.Append($"ext=({scroller?.ExtentWidth:F0}x{scroller?.ExtentHeight:F0}) ");
			sb.Append($"slots={slots.Count} visP={vis}");
			if (annotSurface != null)
				sb.Append($" annot=({annotSurface.Width:F0}x{annotSurface.Height:F0} hit={annotSurface.IsHitTestVisible})");
			DocViewLog.Info(sb.ToString());

			// 视口附近页槽细节（最多 3 页）
			var n = 0;
			foreach (var kv in slots) {
				var p = kv.Key;
				var slot = kv.Value;
				if (slot?.Host == null) continue;
				if (p < vis - 1 || p > vis + 1) continue;
				if (n++ >= 3) break;
				var left = Canvas.GetLeft(slot.Host);
				var top = Canvas.GetTop(slot.Host);
				var expectL = pageW != null && p >= 0 && p < pageCount
					? Math.Max(0, (contentW - pageW[p]) / 2) - PAGE_BORDER
					: double.NaN;
				var expectT = pageTop != null && p >= 0 && p < pageCount
					? pageTop[p] - PAGE_BORDER
					: double.NaN;
				var dL = double.IsNaN(expectL) ? 0 : left - expectL;
				var dT = double.IsNaN(expectT) ? 0 : top - expectT;
				var pw = p >= 0 && p < pageCount && pageW != null ? pageW[p] : 0;
				var ph = p >= 0 && p < pageCount && pageH != null ? pageH[p] : 0;
				var layoutAr = pw > 1 ? ph / pw : 0;
				var tileInfo = "";
				if (slot.Tiles != null) {
					for (var t = 0; t < slot.Tiles.Length && t < 4; t++) {
						var img = slot.Tiles[t];
						var px = 0;
						var py = 0;
						var ar = 0.0;
						if (img?.Source is BitmapSource bs) {
							px = bs.PixelWidth;
							py = bs.PixelHeight;
							ar = px > 0 ? py / (double)px : 0;
						}
						var stretch = img?.Stretch.ToString() ?? "?";
						var arDiff = layoutAr > 0 && ar > 0 ? Math.Abs(ar - layoutAr) : -1;
						tileInfo += $" t{t}=[{px}x{py} ar={ar:F3} dip={img?.Width:F0}x{img?.Height:F0} st={stretch} dAR={arDiff:F3}]";
					}
				}
				DocViewLog.Info(
					$"zoomdiag[{tag}] slot p={p} host=({left:F1},{top:F1},{slot.Host.Width:F0}x{slot.Host.Height:F0}) " +
					$"expect=({expectL:F1},{expectT:F1}) d=({dL:F1},{dT:F1}) page={pw:F0}x{ph:F0} ar={layoutAr:F3} " +
					$"tiles={slot.Tiles?.Length ?? 0}{tileInfo}");
				if (Math.Abs(dL) > 1.5 || Math.Abs(dT) > 1.5)
					DocViewLog.Warn($"zoomdiag[{tag}] SLOT_POS_MISMATCH p={p} dL={dL:F1} dT={dT:F1}");
			}
		} catch (Exception ex) {
			DocViewLog.Warn($"zoomdiag[{tag}] fail: {ex.Message}");
		}
	}

	void capturezoomlock(WpfPoint mouseInScroller) {
		var layoutX = scroller.HorizontalOffset + mouseInScroller.X;
		var layoutY = scroller.VerticalOffset + mouseInScroller.Y;

		var page = findpageat(layoutY);
		if (page < 0) page = 0;
		if (page >= pageCount) page = pageCount - 1;
		zoomLockPage = page;
		var left = Math.Max(0, (contentW - pageW[page]) / 2);
		var pw = Math.Max(1e-6, pageW[page]);
		var ph = Math.Max(1e-6, pageH[page]);
		var right = left + pw;

		// 页外（左右灰边）：X 不用鼠标，锚页水平中心；只取 Y
		zoomLockXOnPage = layoutX >= left - 0.5 && layoutX <= right + 0.5;
		if (zoomLockXOnPage) {
			zoomLockFracX = (layoutX - left) / pw;
			if (zoomLockFracX < 0) zoomLockFracX = 0;
			if (zoomLockFracX > 1) zoomLockFracX = 1;
		} else {
			zoomLockFracX = 0.5;
		}

		zoomLockFracY = (layoutY - pageTop[page]) / ph;
		if (double.IsNaN(zoomLockFracY) || double.IsInfinity(zoomLockFracY)) zoomLockFracY = 0;
		if (zoomLockFracY < -0.05) zoomLockFracY = -0.05;
		if (zoomLockFracY > 1.05) zoomLockFracY = 1.05;

		if (zoomLockXOnPage)
			zoomLockMouse = mouseInScroller;
		else
			zoomLockMouse = new WpfPoint(Math.Max(8, scroller.ViewportWidth * 0.5), mouseInScroller.Y);

		DocViewLog.Info(
			$"zoom lock p={page} frac=({zoomLockFracX:F4},{zoomLockFracY:F4}) xOnPage={zoomLockXOnPage} " +
			$"layoutPt=({layoutX:F1},{layoutY:F1}) pageX=[{left:F0},{right:F0}]");
	}

	void contentpointfromlock(out double x, out double y) {
		x = y = 0;
		if (zoomLockPage < 0 || zoomLockPage >= pageCount) return;
		if (pageTop == null || pageW == null || pageH == null) return;
		var p = zoomLockPage;
		var left = Math.Max(0, (contentW - pageW[p]) / 2);
		x = left + zoomLockFracX * pageW[p];
		y = pageTop[p] + zoomLockFracY * pageH[p];
	}

	void applyzoomlockscroll(WpfPoint mouse) {
		if (zoomLockPage < 0 || zoomLockPage >= pageCount) return;
		if (pageTop == null || pageW == null || pageH == null || scroller == null) return;
		contentpointfromlock(out var newX, out var newY);
		// 页外只调垂直滚动，水平保持
		double? wantH = null;
		if (zoomLockXOnPage)
			wantH = Math.Max(0, newX - mouse.X);
		var wantV = Math.Max(0, newY - mouse.Y);
		for (var pass = 0; pass < 2; pass++) {
			if (wantH.HasValue)
				scroller.ScrollToHorizontalOffset(wantH.Value);
			scroller.ScrollToVerticalOffset(wantV);
			if (pass == 0) {
				try { scroller.UpdateLayout(); } catch { /* ignore */ }
			}
		}
		DocViewLog.Info(
			$"zoom scroll wantV={wantV:F1} wantH={(wantH.HasValue ? wantH.Value.ToString("F1") : "-")} " +
			$"got=({scroller.HorizontalOffset:F0},{scroller.VerticalOffset:F0}) " +
			$"okV={Math.Abs(scroller.VerticalOffset - wantV) < 2} xOnPage={zoomLockXOnPage}");
	}

	/// <summary>停稳后只重渲，绝不改滚动/布局锚点。</summary>
	void schedulenewrender() {
		if (zoomRenderTimer == null) {
			zoomRenderTimer = new DispatcherTimer(DispatcherPriority.Background) {
				Interval = TimeSpan.FromMilliseconds(ZOOM_RENDER_MS),
			};
			zoomRenderTimer.Tick += (_, _) => {
				try { zoomRenderTimer.Stop(); } catch { /* ignore */ }
				onzoomrender();
			};
		}
		try {
			zoomRenderTimer.Stop();
			zoomRenderTimer.Start();
		} catch { /* ignore */ }
	}

	void onzoomrender() {
		if (disposed) return;
		// 用户若已滚动则跟当前偏移，不再强行拽回缩放时的 pin
		var h = iszoompinactive() ? zoomPinH : scroller.HorizontalOffset;
		var v = iszoompinactive() ? zoomPinV : scroller.VerticalOffset;
		try {
			// applyzoomlayout 已 gen++/cancel/clear；此处再清一次并保持软单图
			cancelall();
			clearcache();
			foreach (var p in new List<int>(slots.Keys)) {
				if (p < 0 || p >= pageCount) continue;
				if (!slots.TryGetValue(p, out var slot) || slot?.Tiles == null) continue;
				forcesoftsingle(slot);
			}
			scroller.ScrollToHorizontalOffset(h);
			scroller.ScrollToVerticalOffset(v);
			// 仍 hold：建槽只用单块种子，禁止多 tile 占位
			scheduleui();
			scroller.ScrollToHorizontalOffset(h);
			scroller.ScrollToVerticalOffset(v);
			if (iszoompinactive())
				setzoompin(h, v); // 刷新钉点（尺寸可能刚变）
			DocViewLog.Info($"zoom render-only gen={gen} keepScroll=({h:F0},{v:F0}) got=({scroller.HorizontalOffset:F0},{scroller.VerticalOffset:F0})");
		} catch (Exception ex) {
			DocViewLog.Error("onzoomrender", ex);
		} finally {
			zoomHold = false;
		}
		// 放开 hold：再布局叠加层 + 补一帧预取
		try {
			if (editSurface != null) {
				editSurface.Width = contentW;
				editSurface.Height = contentH;
				editSurface.Relayout();
			}
			if (annotSurface != null) {
				annotSurface.Width = contentW;
				annotSurface.Height = contentH;
				annotSurface.Relayout();
			}
		} catch { /* ignore */ }
		logzoomdiag("render_after_hold");
		try { scheduleui(); } catch { /* ignore */ }
		logzoomdiag("render_after_ui");
	}

	static void softscaleimages(PageSlot slot) {
		if (slot?.Tiles == null) return;
		foreach (var img in slot.Tiles) {
			if (img?.Source == null) continue;
			try {
				WpfRenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
			} catch { /* ignore */ }
		}
	}

	/// <summary>不拆 Host：块数变化时优先保留旧图软显示，禁止把一张整页图复制到 N 条。</summary>
	void retileinplace(int page) {
		if (!slots.TryGetValue(page, out var slot) || slot?.Host == null) return;
		var need = tilecount(page);
		if (need < 1) need = 1;
		if (slot.Tiles != null && slot.Tiles.Length == need) {
			layoutslot(slot, fillExistingTiles: false);
			return;
		}
		// 块数变化：不重建 N 条（易复制整页图导致压扁），保持现有图按比例软布局
		layoutslot(slot, fillExistingTiles: true);
		softscaleimages(slot);
	}

	/// <summary>自动化调试：分步缩放+截图+延时观察（--zoomtest）。截图写到 logs/zoomshot/。</summary>
	public void DebugRunZoomTest(int steps = 8) {
		var shotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "zoomshot");
		try { Directory.CreateDirectory(shotDir); } catch { /* ignore */ }
		try {
			foreach (var f in Directory.GetFiles(shotDir, "z_*.png"))
				File.Delete(f);
		} catch { /* ignore */ }

		// 固定从 1.0 开始，覆盖跨过多 tile 阈值（~1200dip）的高倍路径
		try {
			zoomHold = false;
			zoom = layoutZoom = 1.0;
			recalcmetrics();
			if (pageCount > 5) {
				scroller.ScrollToVerticalOffset(pageTop[Math.Min(5, pageCount - 1)]);
				scroller.UpdateLayout();
			}
			updateviewport(gen);
		} catch (Exception ex) {
			DocViewLog.Error("zoomtest init", ex);
		}

		DocViewLog.Info($"zoomtest start pages={pageCount} z={zoom:F3} slots={slots.Count} " +
			$"scroll=({scroller.HorizontalOffset:F0},{scroller.VerticalOffset:F0}) shotDir={shotDir}");

		var mouseOn = new WpfPoint(
			Math.Max(20, scroller.ViewportWidth * 0.5),
			Math.Max(20, scroller.ViewportHeight * 0.45));
		// 先升到高倍（必过多 tile），再降回，观察压扁/跳动
		var targets = new[] { 1.5, 2.0, 2.5, 3.0, 2.0, 1.5 };
		var step = 0;
		// 防止 timer 被 GC
		DispatcherTimer keepAlive = null;

		void shot(string tag) {
			try {
				var path = Path.Combine(shotDir, $"z_{step:D2}_{tag}.png");
				captureshot(path);
				var bytes = 0L;
				try { bytes = new FileInfo(path).Length; } catch { /* ignore */ }
				DocViewLog.Info(
					$"zoomtest SHOT {tag} z={zoom:F3} scroll=({scroller.HorizontalOffset:F0},{scroller.VerticalOffset:F0}) " +
					$"pin=({zoomPinH:F0},{zoomPinV:F0}) slots={slots.Count} bytes={bytes} file={Path.GetFileName(path)}");
			} catch (Exception ex) {
				DocViewLog.Error($"zoomtest shot {tag}", ex);
			}
		}

		bool hasRealBitmap() {
			foreach (var kv in slots) {
				if (kv.Value?.Tiles == null) continue;
				foreach (var img in kv.Value.Tiles) {
					if (img?.Source is BitmapSource bs && bs.PixelWidth > 32)
						return true;
				}
			}
			return false;
		}

		void runStep() {
			if (step >= targets.Length) {
				DocViewLog.Info("zoomtest DONE");
				return;
			}
			var nz = targets[step];
			var h0 = scroller.HorizontalOffset;
			var v0 = scroller.VerticalOffset;
			DocViewLog.Info($"zoomtest STEP{step} begin z->{nz:F3} scroll=({h0:F0},{v0:F0}) tiles0={tilecount(Math.Min(5, pageCount - 1))}");
			try {
				setzoomcore(nz, mouseOn);
			} catch (Exception ex) {
				DocViewLog.Error($"zoomtest STEP{step} setzoom", ex);
			}
			shot($"s{step}_after_zoom");
			var h1 = scroller.HorizontalOffset;
			var v1 = scroller.VerticalOffset;
			keepAlive = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
			var capturedStep = step;
			keepAlive.Tick += (_, _) => {
				try { keepAlive.Stop(); } catch { /* ignore */ }
				try {
					var h2 = scroller.HorizontalOffset;
					var v2 = scroller.VerticalOffset;
					var jumped = Math.Abs(h2 - h1) > 2 || Math.Abs(v2 - v1) > 2;
					DocViewLog.Info(
						$"zoomtest STEP{capturedStep} after700ms jump={jumped} " +
						$"scroll {h1:F0},{v1:F0} -> {h2:F0},{v2:F0}");
					shot($"s{capturedStep}_after_700ms");
				} catch (Exception ex) {
					DocViewLog.Error($"zoomtest STEP{capturedStep} after", ex);
				}
				step++;
				runStep();
			};
			keepAlive.Start();
		}

		// 等首屏出图再开测（最多 ~2.5s）
		var waitN = 0;
		keepAlive = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
		keepAlive.Tick += (_, _) => {
			waitN++;
			if (hasRealBitmap() || waitN >= 12) {
				try { keepAlive.Stop(); } catch { /* ignore */ }
				DocViewLog.Info($"zoomtest ready waitN={waitN} hasBmp={hasRealBitmap()} z={zoom:F3}");
				shot("s00_baseline");
				runStep();
			}
		};
		keepAlive.Start();
	}

	void captureshot(string path) {
		if (scroller == null) return;
		scroller.UpdateLayout();
		var w = (int)Math.Max(1, scroller.ActualWidth);
		var h = (int)Math.Max(1, scroller.ActualHeight);
		var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
		rtb.Render(scroller);
		var enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(rtb));
		using var fs = File.Create(path);
		enc.Save(fs);
	}

	double nextzoom(bool up) {
		if (up) {
			foreach (var z in ZoomPresets)
				if (z > zoom + 0.001) return z;
			return MAX_ZOOM;
		}
		for (var i = ZoomPresets.Length - 1; i >= 0; i--)
			if (ZoomPresets[i] < zoom - 0.001) return ZoomPresets[i];
		return MIN_ZOOM;
	}

	/// <summary>文档内嵌目录侧栏已废弃（改用主窗「章节列表」），始终隐藏。</summary>
	void setside(bool show) {
		sideVisible = false;
		colside.Width = new GridLength(0);
		pside.Visibility = Visibility.Collapsed;
		raisestatus();
	}

	void updatedpiscale() {
		try {
			var d = VisualTreeHelper.GetDpi(root.IsLoaded ? root : scroller);
			dpiScale = d.DpiScaleX > 0.1 ? d.DpiScaleX : 1.0;
		} catch {
			dpiScale = 1.0;
		}
	}

	/// <summary>显示器 DPI 变化：清缓存按新物理像素 1:1 重渲。</summary>
	void onDpichanged() {
		if (disposed || pageCount <= 0) return;
		try {
			clearcache();
			cancelall();
			gen++;
			scheduleui();
			raisestatus();
		} catch (Exception ex) {
			DocViewLog.Error("onDpichanged", ex);
		}
	}

	/// <summary>DIP 对齐到设备像素，减少亚像素贴图发糊。</summary>
	double snapdip(double v) {
		if (dpiScale < 0.1) return v;
		return Math.Round(v * dpiScale) / dpiScale;
	}

	/// <summary>位图逻辑尺寸与控件 DIP 接近时 1:1 贴图（锐利），否则 Fant 软缩放。</summary>
	static void setbitmapsmoothing(System.Windows.Controls.Image img, BitmapSource bmp) {
		if (img == null || bmp == null) return;
		try {
			var dipW = img.Width > 1 ? img.Width : 0;
			var dipH = img.Height > 1 ? img.Height : 0;
			var logicalW = bmp.DpiX > 1
				? bmp.PixelWidth * 96.0 / bmp.DpiX
				: bmp.PixelWidth;
			var logicalH = bmp.DpiY > 1
				? bmp.PixelHeight * 96.0 / bmp.DpiY
				: bmp.PixelHeight;
			if (dipW < 1) dipW = logicalW;
			if (dipH < 1) dipH = logicalH;
			var match = Math.Abs(logicalW - dipW) <= 1.0 && Math.Abs(logicalH - dipH) <= 1.0;
			if (match) {
				// Stretch.None + 最近邻：禁止任何插值缩放（边框挤占时 Fill 会抹糊文字）
				img.Stretch = Stretch.None;
				WpfRenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
			} else {
				img.Stretch = Stretch.Fill;
				WpfRenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
			}
		} catch { /* ignore */ }
	}

	// ---------- 目录 ----------
	void buildoutline() {
		// 首次构建：决定有无目录与侧栏；实际树由 rebuildoutlineui 填充
		lastOutlinePage = -1;
		outlineQuery = "";
		if (eoutline != null) eoutline.Text = "";
		if (outline == null || outline.Count == 0) {
			hasOutline = false;
			tree.Items.Clear();
			outlineFlat.Clear();
			lboutline.Text = "无目录";
			lboutline.Visibility = Visibility.Visible;
			if (eoutline != null) eoutline.Visibility = Visibility.Collapsed;
			if (pageCount > 0)
				tree.Items.Add(new TreeViewItem {
					Header = OutlineUi.MakeHeader($"共 {pageCount} 页（无书签）", "", ""),
					IsEnabled = false,
				});
			setside(false);
			return;
		}
		hasOutline = true;
		if (eoutline != null) eoutline.Visibility = Visibility.Visible;
		setside(false);
		rebuildoutlineui();
		// 目录异步可能晚于滚动恢复：在恢复窗口内则展开并多拍同步
		if (restoreOutlinePage >= 0)
			pendingExpandOutline = true;
		syncoutline(force: true);
		if (restoreOutlinePage >= 0) {
			try {
				tree.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
					if (disposed || restoreOutlinePage < 0) return;
					pendingExpandOutline = true;
					syncoutline(force: true);
				}));
				tree.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
					if (disposed || restoreOutlinePage < 0) return;
					pendingExpandOutline = true;
					syncoutline(force: true);
				}));
			} catch { /* ignore */ }
		}
	}

	/// <summary>按筛选关键字重建目录树（只显示匹配项及其祖先）。</summary>
	void rebuildoutlineui() {
		tree.Items.Clear();
		outlineFlat.Clear();
		if (outline == null || outline.Count == 0) return;

		var q = outlineQuery ?? "";
		var filtered = string.IsNullOrWhiteSpace(q)
			? outline
			: filteroutlinenodes(outline, q);

		syncTree = true;
		try {
			if (filtered.Count == 0) {
				lboutline.Text = "无匹配章节";
				lboutline.Visibility = Visibility.Visible;
			} else {
				lboutline.Visibility = Visibility.Collapsed;
				foreach (var n in filtered)
					tree.Items.Add(makenode(n, 0, expandAll: q.Length > 0));
			}
		} finally { syncTree = false; }
	}

	/// <summary>保留：自身匹配，或子树有匹配（祖先路径不断）。</summary>
	static List<PdfOutlineNode> filteroutlinenodes(List<PdfOutlineNode> nodes, string q) {
		var result = new List<PdfOutlineNode>();
		if (nodes == null) return result;
		foreach (var n in nodes) {
			if (n == null) continue;
			var kids = filteroutlinenodes(n.Children, q);
			var self = OutlineUi.Match(n.Title, q);
			if (!self && kids.Count == 0) continue;
			result.Add(new PdfOutlineNode {
				Title = n.Title,
				PageIndex = n.PageIndex,
				HasDestY = n.HasDestY,
				DestY = n.DestY,
				TopFrac = n.TopFrac,
				Children = kids,
			});
		}
		return result;
	}

	TreeViewItem makenode(PdfOutlineNode n, int depth = 0, bool expandAll = false) {
		var raw = n.Title ?? "(未命名)";
		var pageSuffix = n.PageIndex >= 0 ? $"  ·  {n.PageIndex + 1}" : "";
		// 筛选时全部展开；默认全部折叠，仅恢复位置时再展开路径
		var item = new TreeViewItem {
			Header = OutlineUi.MakeHeader(raw, pageSuffix, outlineQuery),
			Tag = n, // 含页码 + 页内 Y，用于跳到章节标题并置顶
			IsExpanded = expandAll,
		};
		if (n.PageIndex >= 0)
			outlineFlat.Add(new OutlineEntry {
				Page = n.PageIndex,
				Depth = depth,
				Order = outlineFlat.Count,
				TopFrac = n.HasDestY ? clamp(n.TopFrac, 0, 0.99) : 0,
				Item = item,
			});
		if (n.Children != null)
			foreach (var c in n.Children)
				item.Items.Add(makenode(c, depth + 1, expandAll));
		return item;
	}

	/// <summary>目录点击跳转：防抖，只执行最后一次点击；尽量滚到章节标题并置顶。</summary>
	void queueoutlinejump(PdfOutlineNode node) {
		if (node == null) return;
		var page = node.PageIndex;
		if (page < 0 || page >= pageCount) return;
		var frac = node.HasDestY ? clamp(node.TopFrac, 0, 0.98) : 0;
		// 在防抖前先记下当前位置（以首次点击为准，连点只记一次）
		if (pendingOutlinePage < 0)
			pushnavbeforejump(page, frac);
		pendingOutlinePage = page;
		pendingOutlineFrac = frac;
		lastOutlinePage = page;
		lastOutlineSync = Environment.TickCount;
		var token = ++outlineNavToken;
		// 延迟一帧合并快速连点，避免同步重入
		scroller.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
			if (disposed || token != outlineNavToken) return;
			var p = pendingOutlinePage;
			var f = pendingOutlineFrac;
			pendingOutlinePage = -1;
			pendingOutlineFrac = 0;
			if (p < 0) return;
			try {
				// 历史已在 queue 时 push，此处直接滚
				scrolltopage(p, fromOutline: true, topFrac: f);
			} catch (Exception ex) {
				DocViewLog.Error("outline jump", ex);
			}
		}));
	}

	void onoutlineexpandcollapse(object sender, RoutedEventArgs e) {
		if (syncTree || disposed) return;
		// 等 IsExpanded 状态落定后再同步选中
		try {
			tree.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
				if (syncTree || disposed) return;
				syncoutline(force: true);
			}));
		} catch { /* ignore */ }
	}

	/// <summary>
	/// 同步目录选中。滚动时防抖；force/恢复位置立即生效。
	/// </summary>
	void syncoutline(bool force = false) {
		if (syncTree || disposed || outlineFlat.Count == 0 || pageCount <= 0) return;
		if (pendingOutlinePage >= 0) return;
		// 滚动：防抖合并连续翻页，避免高亮与目录条上下乱跳
		if (!force && !pendingExpandOutline) {
			var pagePeek = estimatepage();
			if (pagePeek == lastOutlinePage) {
				// 页未变但高亮可能已错（如折叠后/书签页码乱序），仍需纠正
				var fracPeek = pagefrac(pagePeek);
				var want = findoutlineat(pagePeek, fracPeek);
				var wantItem = want != null ? OutlineUi.FindVisibleOnPath(want.Item) : null;
				if (wantItem != null && ReferenceEquals(tree.SelectedItem, wantItem))
					return;
			}
			scheduleoutlinedebounce();
			return;
		}
		stopoutlinedebounce();
		applyoutlinesync(force, center: force || pendingExpandOutline);
	}

	void scheduleoutlinedebounce() {
		if (outlineDebounce == null) {
			outlineDebounce = new DispatcherTimer(DispatcherPriority.Background) {
				Interval = TimeSpan.FromMilliseconds(OUTLINE_DEBOUNCE_MS),
			};
			outlineDebounce.Tick += (_, _) => {
				try { outlineDebounce.Stop(); } catch { /* ignore */ }
				if (disposed) return;
				applyoutlinesync(force: false, center: false);
			};
		}
		outlineDebounce.Stop();
		outlineDebounce.Start();
	}

	void stopoutlinedebounce() {
		try { outlineDebounce?.Stop(); } catch { /* ignore */ }
	}

	/// <summary>
	/// 真正应用目录高亮：理想目标为当前位置对应章节；
	/// 默认不自动展开；pendingExpandOutline 时展开到最深节点。
	/// 未展开时选中路径上已可见的最深节点。
	/// center：恢复位置时目录居中；滚动同步仅最小滚入可视区。
	/// </summary>
	void applyoutlinesync(bool force, bool center) {
		if (syncTree || disposed || outlineFlat.Count == 0 || pageCount <= 0) return;
		if (pendingOutlinePage >= 0) return;
		var page = estimatepage();
		var frac = pagefrac(page);
		if (restoreOutlinePage >= 0 && page < restoreOutlinePage) {
			page = restoreOutlinePage;
			frac = 0;
		}
		var want0 = findoutlineat(page, frac);
		var want0Item = want0 != null ? OutlineUi.FindVisibleOnPath(want0.Item) : null;
		if (want0Item != null && ReferenceEquals(tree.SelectedItem, want0Item)
			&& !pendingExpandOutline) {
			lastOutlinePage = page;
			return;
		}
		var now = Environment.TickCount;
		if (force && page == lastOutlinePage && !pendingExpandOutline
			&& now - lastOutlineSync < OUTLINE_SYNC_MS
			&& want0Item != null && ReferenceEquals(tree.SelectedItem, want0Item))
			return;
		lastOutlineSync = now;
		var best = want0;
		if (best == null) {
			if (page <= 0) best = outlineFlat[0];
			else return;
		}
		if (best?.Item == null) return;
		if (pendingExpandOutline) {
			syncTree = true;
			try { OutlineUi.ExpandAncestors(best.Item); }
			finally { syncTree = false; }
			pendingExpandOutline = false;
		}
		// 主窗章节列表：书签页 1-based（与 GetOutlineSnapshot 的 Tag 一致）
		try { OutlineHighlightChanged?.Invoke(best.Page + 1); } catch { /* ignore */ }
		// 展开后可见路径可能变深，重新取 sel
		var sel = OutlineUi.FindVisibleOnPath(best.Item);
		if (sel == null) return;
		if (ReferenceEquals(tree.SelectedItem, sel)) {
			lastOutlinePage = page;
			return;
		}
		lastOutlinePage = page;
		syncTree = true;
		try {
			if (tree.SelectedItem is TreeViewItem old && !ReferenceEquals(old, sel))
				old.IsSelected = false;
			sel.IsSelected = true;
			if (force) {
				try {
					tree.UpdateLayout();
					sel.UpdateLayout();
				} catch { /* ignore */ }
			}
			OutlineUi.ScrollItemIntoView(sel, center);
		} catch (Exception ex) {
			DocViewLog.Warn($"syncoutline: {ex.Message}");
		} finally {
			syncTree = false;
		}
	}

	/// <summary>
	/// 当前位置对应目录项：文档先序中「最后一个目标不超过当前阅读位置」的书签。
	/// 比「最大页码」更稳——先前章节若含子书签页码偏大，不会盖住后续正确章节。
	/// 同页用 TopFrac 与当前页内滚动比例比较。
	/// </summary>
	OutlineEntry findoutlineat(int page0, double viewTopFrac) {
		if (outlineFlat.Count == 0) return null;
		viewTopFrac = clamp(viewTopFrac, 0, 1);
		// 视口靠上区域：略提前一点进入下一书签，减少「页上已是新节仍停在旧节」
		var y = viewTopFrac + 0.02;
		OutlineEntry best = null;
		foreach (var e in outlineFlat) {
			if (e.Page < 0) continue;
			if (e.Page > page0) continue;
			if (e.Page == page0 && e.TopFrac > y) continue;
			// 先序遍历：后出现的覆盖先前的（即「最后一个仍 ≤ 当前位置」）
			best = e;
		}
		return best;
	}

	// ---------- 布局 / 视口 ----------
	void recalcmetrics() {
		if (pageCount <= 0) {
			contentW = contentH = 0;
			contentRoot.Width = contentRoot.Height = 0;
			canvas.Width = canvas.Height = 0;
			selLayer.Width = selLayer.Height = 0;
			return;
		}
		// 布局缩放：与 layoutZoom 对齐（烘焙后 layoutZoom==zoom）
		// 页间距随缩放比例变化，使 pageTop 对 zoom 近似线性 → live 变换与烘焙滚动一致，消除 settle 跳动
		var zLayout = layoutZoom > 1e-6 ? layoutZoom : zoom;
		if (zLayout < 1e-6) zLayout = 1;
		var gap = PAGE_GAP * zLayout;
		double top = gap / 2;
		double maxW = 40;
		for (var i = 0; i < pageCount; i++) {
			viewpagesizept(i, out var ptW, out var ptH);
			var w = Math.Max(40, ptW * LAYOUT_DPI / 72.0 * zLayout);
			var h = Math.Max(40, ptH * LAYOUT_DPI / 72.0 * zLayout);
			// 防止单页布局尺寸离谱
			if (w > 8000) { h *= 8000 / w; w = 8000; }
			if (h > 12000) { w *= 12000 / h; h = 12000; }
			pageW[i] = w;
			pageH[i] = h;
			pageTop[i] = top;
			if (w > maxW) maxW = w;
			top += h + gap;
		}
		contentW = maxW + 24 * zLayout;
		contentH = top + gap / 2;
		// WPF 对超大 Height 不稳定：钳制滚动范围上限（约 2M DIP）
		const double MAX_EXTENT = 2_000_000;
		if (contentH > MAX_EXTENT) {
			DocViewLog.Warn($"contentH {contentH:F0} too large, clamp to {MAX_EXTENT}");
			// 等比压缩 pageTop/pageH 会破坏跳页；改为仅限制 Canvas 声明高度
			// 实际子元素仍按 pageTop 放置——若超出则跳页用 ScrollToVerticalOffset 仍可用 double
			contentH = MAX_EXTENT;
		}
		contentRoot.Width = contentW;
		contentRoot.Height = contentH;
		canvas.Width = contentW;
		canvas.Height = contentH;
		selLayer.Width = contentW;
		selLayer.Height = contentH;
		// 立刻重放页槽位置/尺寸（与 pageW/H/Top 同步）。
		// 否则 content 已变而 Host 仍停在旧 left/top，中间会闪一帧错位（日志 SLOT_POS_MISMATCH）。
		foreach (var kv in slots) {
			var p = kv.Key;
			var slot = kv.Value;
			if (slot?.Host == null || p < 0 || p >= pageCount) continue;
			try {
				placepagehost(slot, pageW[p], pageH[p], pageTop[p]);
				if (slot.Tiles != null && slot.Tiles.Length == 1 && slot.Tiles[0] != null) {
					slot.Tiles[0].Width = pageW[p];
					slot.Tiles[0].Height = Math.Max(1, pageH[p]);
				}
			} catch { /* ignore */ }
		}
		if (editSurface != null) {
			editSurface.Width = contentW;
			editSurface.Height = contentH;
			// 与页槽一样：布局变即刻 Relayout，避免缩放 hold 期间叠加层停在旧坐标闪一帧
			try { editSurface.Relayout(); } catch { /* ignore */ }
		}
		if (annotSurface != null) {
			annotSurface.Width = contentW;
			annotSurface.Height = contentH;
			// Relayout 内部 refitText=false，只放位置/尺寸，不 Measure 文本
			try { annotSurface.Relayout(); } catch { /* ignore */ }
		}
	}

	void scheduleui() {
		// 保留给跳页等非滚动路径：直接同步更新视口
		if (disposed) return;
		updateviewport(gen);
	}

	void updateviewport(int g) {
		if (disposed || g != gen || pageCount <= 0) return;
		var vpTop = scroller.VerticalOffset;
		var vpH = Math.Max(1, scroller.ViewportHeight);
		var vpBottom = vpTop + vpH;

		// 基础预取 + 滚动方向加码（快滚时前方多备 2 页）
		var preBehind = PREFETCH;
		var preAhead = PREFETCH;
		if (scrollDir > 0) preAhead += 2;
		else if (scrollDir < 0) preBehind += 2;

		var first = Math.Max(0, findpageat(vpTop) - preBehind);
		var last = Math.Min(pageCount - 1, findpageat(vpBottom) + preAhead);
		// 一屏多页时至少覆盖视口，但上限防止一次开太多槽（预览缓存会顶上）
		if (last - first > 14) {
			var mid = estimatepage();
			first = Math.Max(0, mid - 5);
			last = Math.Min(pageCount - 1, mid + 9);
			if (scrollDir < 0) {
				first = Math.Max(0, mid - 9);
				last = Math.Min(pageCount - 1, mid + 5);
			}
		}
		visFirst = first;
		visLast = last;
		visAnchor = estimatepage();

		// 槽位保留范围更大：离开预取区也不立刻拆，减少来回白页
		var keepFirst = Math.Max(0, first - SLOT_KEEP);
		var keepLast = Math.Min(pageCount - 1, last + SLOT_KEEP);

		// 仅取消远离保留区的任务（预取区内 in-flight 尽量保留）
		lock (gate) {
			foreach (var t in queue)
				if (t.Page < keepFirst - 1 || t.Page > keepLast + 1)
					t.Cancelled = true;
		}

		// 缩放 hold：不拆槽、不新建占位页（无缓存时新建只会闪灰/白）
		if (!zoomHold) {
			var remove = new List<int>();
			foreach (var kv in slots)
				if (kv.Key < keepFirst || kv.Key > keepLast) remove.Add(kv.Key);
			foreach (var p in remove) {
				canvas.Children.Remove(slots[p].Host);
				slots.Remove(p);
			}
		}

		// 先建槽 + 立刻贴缓存（有图就先显示，无图再排队渲染）
		for (var i = first; i <= last; i++) {
			if (!slots.TryGetValue(i, out var slot)) {
				// 始终先单块软槽；多 tile 仅 tryapplyalltiles 齐套后一次性换上
				var seed = findseedimages();
				slot = createslot(i, seed, softSingle: true);
				slots[i] = slot;
				canvas.Children.Add(slot.Host);
			}
			// hold 中绝不贴分块缓存（旧倍率条带 + 新高度 = 压扁）
			if (zoomHold) {
				forcesoftsingle(slot);
				continue;
			}
			if (!tryapplyalltiles(slot))
				forcesoftsingle(slot);
			ensurerender(i, g, aggressive: true);
		}
		if (!zoomHold) {
			for (var i = keepFirst; i <= keepLast; i++) {
				if (i >= first && i <= last) continue;
				if (!slots.TryGetValue(i, out var slot)) continue;
				if (!tryapplyalltiles(slot))
					forcesoftsingle(slot);
				ensurerender(i, g, aggressive: false);
			}
		}
		drawselection();
	}

	/// <summary>取任一页上的真实位图，供缩放时新建槽临时显示。</summary>
	List<ImageSource> findseedimages() {
		foreach (var kv in slots) {
			if (kv.Value?.Tiles == null) continue;
			foreach (var img in kv.Value.Tiles) {
				if (img?.Source is BitmapSource bs && bs.PixelWidth > 8)
					return new List<ImageSource> { bs };
			}
		}
		return null;
	}

	/// <summary>
	/// 建页槽。softSingle=true：始终 1 块图（整页软显示），避免高倍 tilecount&gt;1 时
	/// 把同一张整页旧图复制到每个分块再按条高度 Stretch → 纵向压扁。
	/// </summary>
	PageSlot createslot(int page, List<ImageSource> seedImages, bool softSingle = false) {
		var hasSeed = seedImages != null && seedImages.Count > 0;
		var tiles = (softSingle || hasSeed) ? 1 : Math.Max(1, tilecount(page));
		var panel = new Grid();
		var stack = new StackPanel { Orientation = Orientation.Vertical };
		var imgs = new System.Windows.Controls.Image[tiles];
		for (var t = 0; t < tiles; t++) {
			var img = new System.Windows.Controls.Image {
				Stretch = Stretch.Fill,
				SnapsToDevicePixels = true,
				UseLayoutRounding = true,
				Source = hasSeed
					? seedImages[0]
					: placeholdertiny(),
			};
			WpfRenderOptions.SetBitmapScalingMode(img,
				hasSeed ? BitmapScalingMode.Fant : BitmapScalingMode.NearestNeighbor);
			stack.Children.Add(img);
			imgs[t] = img;
		}
		var label = new TextBlock {
			Text = $"{page + 1}",
			FontSize = 28,
			Foreground = new SolidColorBrush(WpfColor.FromRgb(0x9C, 0xA3, 0xAF)),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false,
			Visibility = hasSeed ? Visibility.Collapsed : Visibility.Visible,
		};
		panel.Children.Add(stack);
		panel.Children.Add(label);
		var border = new Border {
			Child = panel,
			Background = WpfBrushes.White,
			BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xD1, 0xD5, 0xDB)),
			BorderThickness = new Thickness(1),
			ToolTip = $"第 {page + 1} 页",
		};
		return new PageSlot { Page = page, Host = border, Tiles = imgs, PageLabel = label };
	}

	/// <summary>
	/// 缩放/缓存未齐套时的软显示：保留现有位图，禁止「整页图×N 条」压扁。
	/// - 单块：铺满整页
	/// - 多块且像真条带：按像素高度比例排（不改 Source）
	/// - 多块实为同一张整页图：收成单块铺满
	/// 绝不 RTB 合成（未布局完成时会合成白图盖住旧内容）。
	/// </summary>
	void forcesoftsingle(PageSlot slot) {
		if (slot?.Host == null || slot.Tiles == null || slot.Tiles.Length == 0) return;
		if (slot.Tiles.Length == 1) {
			layoutslot(slot, fillExistingTiles: true);
			softscaleimages(slot);
			return;
		}
		// 多块 Source 全相同（整页被复制到每条）→ 收成单块
		var sameSrc = true;
		for (var t = 1; t < slot.Tiles.Length; t++) {
			if (!ReferenceEquals(slot.Tiles[0].Source, slot.Tiles[t].Source)) {
				sameSrc = false;
				break;
			}
		}
		if (sameSrc || !tileslooklikestrips(slot, slot.Page)) {
			// 取最像整页的一张（或第一张真图）作单块软显示
			BitmapSource best = null;
			var page = slot.Page;
			var pageAr = (page >= 0 && page < pageCount && pageW != null && pageW[page] > 1)
				? pageH[page] / pageW[page]
				: 1.4;
			var bestScore = double.MaxValue;
			foreach (var img in slot.Tiles) {
				if (img?.Source is not BitmapSource bs || bs.PixelWidth < 8) continue;
				var ar = bs.PixelHeight / (double)Math.Max(1, bs.PixelWidth);
				var score = Math.Abs(ar - pageAr);
				if (score < bestScore) {
					bestScore = score;
					best = bs;
				}
			}
			if (best == null && slot.Tiles[0].Source is BitmapSource b0 && b0.PixelWidth > 8)
				best = b0;
			if (best != null && slot.Host.Child is Grid panel && panel.Children.Count > 0
				&& panel.Children[0] is StackPanel stack) {
				var one = new System.Windows.Controls.Image {
					Stretch = Stretch.Fill,
					SnapsToDevicePixels = true,
					UseLayoutRounding = true,
					Source = best,
				};
				try {
					WpfRenderOptions.SetBitmapScalingMode(one, BitmapScalingMode.Fant);
				} catch { /* ignore */ }
				stack.Children.Clear();
				stack.Children.Add(one);
				slot.Tiles = new[] { one };
				if (slot.PageLabel != null)
					slot.PageLabel.Visibility = Visibility.Collapsed;
			}
			layoutslot(slot, fillExistingTiles: true);
			softscaleimages(slot);
			return;
		}
		// 真条带：只调高度比例，保留所有块
		layoutslotproportional(slot);
		softscaleimages(slot);
	}

	/// <summary>Host 外扩边框，内容区 = 页面 DIP；位置对齐设备像素。</summary>
	void placepagehost(PageSlot slot, double w, double h, double top) {
		var left = snapdip(Math.Max(0, (contentW - w) / 2));
		var topSnap = snapdip(top);
		Canvas.SetLeft(slot.Host, left - PAGE_BORDER);
		Canvas.SetTop(slot.Host, topSnap - PAGE_BORDER);
		// Border 吃掉 PAGE_BORDER*2，Host 多给这么多 → 子 Image 可用区仍是 w×h
		slot.Host.Width = w + PAGE_BORDER * 2;
		slot.Host.Height = h + PAGE_BORDER * 2;
	}

	/// <summary>多 tile 软排版：高度按位图像素高度比例，避免 Stretch 改变条带纵横比。</summary>
	void layoutslotproportional(PageSlot slot) {
		if (slot?.Tiles == null || slot.Tiles.Length == 0) return;
		var p = slot.Page;
		if (p < 0 || p >= pageCount || pageW == null || pageH == null || pageTop == null) return;
		var w = pageW[p];
		var h = pageH[p];
		placepagehost(slot, w, h, pageTop[p]);
		var n = slot.Tiles.Length;
		if (n == 1) {
			slot.Tiles[0].Visibility = Visibility.Visible;
			slot.Tiles[0].Width = w;
			slot.Tiles[0].Height = Math.Max(1, h);
			return;
		}
		double sum = 0;
		var weights = new double[n];
		for (var t = 0; t < n; t++) {
			var wh = 1.0;
			if (slot.Tiles[t]?.Source is BitmapSource bs && bs.PixelHeight > 1)
				wh = bs.PixelHeight;
			weights[t] = wh;
			sum += wh;
		}
		if (sum < 1) sum = n;
		var remain = h;
		for (var t = 0; t < n; t++) {
			var th = t < n - 1 ? Math.Floor(h * (weights[t] / sum)) : remain;
			if (th < 1) th = 1;
			if (th > remain) th = remain;
			slot.Tiles[t].Visibility = Visibility.Visible;
			slot.Tiles[t].Width = w;
			slot.Tiles[t].Height = Math.Max(1, th);
			remain -= th;
		}
	}

	/// <param name="fillExistingTiles">
	/// true=软显示（单块铺满；多块则按像素高度比例）；
	/// false=按真实 tilecount 分块高度（新图齐套后）。
	/// </param>
	void layoutslot(PageSlot slot, bool fillExistingTiles = false) {
		if (slot?.Tiles == null || slot.Tiles.Length == 0) return;
		var p = slot.Page;
		if (p < 0 || p >= pageCount || pageW == null || pageH == null || pageTop == null) return;
		var w = pageW[p];
		var h = pageH[p];
		placepagehost(slot, w, h, pageTop[p]);
		var n = slot.Tiles.Length;
		var need = tilecount(p);
		// 齐套且非软显示：标准分块高度
		if (!fillExistingTiles && n == need && n > 1 && tileslooklikestrips(slot, p)) {
			var remain = h;
			for (var t = 0; t < n; t++) {
				var th = tileheightpx(p, t);
				if (th > remain) th = remain;
				if (th < 0) th = 0;
				slot.Tiles[t].Visibility = Visibility.Visible;
				slot.Tiles[t].Stretch = Stretch.Fill;
				slot.Tiles[t].HorizontalAlignment = HorizontalAlignment.Stretch;
				slot.Tiles[t].VerticalAlignment = VerticalAlignment.Stretch;
				slot.Tiles[t].Width = w;
				slot.Tiles[t].Height = Math.Max(1, th);
				remain -= th;
			}
			return;
		}
		// 软显示或未齐套
		if (n == 1) {
			var img = slot.Tiles[0];
			img.Visibility = Visibility.Visible;
			// 软缩放：若位图纵横比与页框差太多，用 Uniform 居中避免 Fill 压扁/错位感
			var useUniform = false;
			if (fillExistingTiles && img.Source is BitmapSource bs && bs.PixelWidth > 8 && w > 1) {
				var arSrc = bs.PixelHeight / (double)bs.PixelWidth;
				var arBox = h / w;
				if (Math.Abs(arSrc - arBox) > 0.04) {
					useUniform = true;
					DocViewLog.Warn(
						$"soft AR mismatch p={p} src={bs.PixelWidth}x{bs.PixelHeight} ar={arSrc:F3} " +
						$"box={w:F0}x{h:F0} ar={arBox:F3} -> Uniform");
				}
			}
			if (useUniform) {
				img.Stretch = Stretch.Uniform;
				img.HorizontalAlignment = HorizontalAlignment.Center;
				img.VerticalAlignment = VerticalAlignment.Center;
				img.Width = w;
				img.Height = Math.Max(1, h);
			} else {
				img.Stretch = Stretch.Fill;
				img.HorizontalAlignment = HorizontalAlignment.Stretch;
				img.VerticalAlignment = VerticalAlignment.Stretch;
				img.Width = w;
				img.Height = Math.Max(1, h);
			}
			return;
		}
		layoutslotproportional(slot);
	}

	/// <summary>判断当前槽位图是否像竖直条带（而非整页图被复制到多块）。</summary>
	bool tileslooklikestrips(PageSlot slot, int page) {
		if (slot?.Tiles == null || slot.Tiles.Length < 2) return false;
		if (page < 0 || page >= pageCount || pageW[page] < 1 || pageH[page] < 1) return false;
		var pageAr = pageH[page] / pageW[page];
		for (var t = 0; t < slot.Tiles.Length; t++) {
			if (slot.Tiles[t]?.Source is not BitmapSource bs || bs.PixelWidth < 8)
				return false;
			var ar = bs.PixelHeight / (double)bs.PixelWidth;
			// 整页图的高宽比接近 pageAr；条带应明显更「扁」
			var expect = tileheightpx(page, t) / pageW[page];
			if (expect < 0.05) expect = 0.05;
			// 更接近整页比 → 不是条带
			if (Math.Abs(ar - pageAr) + 0.08 < Math.Abs(ar - expect))
				return false;
		}
		// 多块 Source 全相同 → 整页复制
		for (var t = 1; t < slot.Tiles.Length; t++) {
			if (!ReferenceEquals(slot.Tiles[0].Source, slot.Tiles[t].Source))
				return true;
		}
		return false;
	}

	/// <summary>把该页所有已缓存的 tile 立刻贴上（新建槽时用）。</summary>
	void applycachedtiles(PageSlot slot) {
		if (slot == null) return;
		tryapplyalltiles(slot);
	}

	/// <summary>
	/// 仅当本页所需分块在缓存中齐套且形态正确时，一次性换成新图。
	/// zoomHold 期间禁止：旧倍率缓存 + 新布局高度会压扁。
	/// </summary>
	bool tryapplyalltiles(PageSlot slot) {
		if (slot == null || zoomHold) return false;
		var page = slot.Page;
		var need = tilecount(page);
		if (need < 1) return false;
		var bmps = new BitmapSource[need];
		for (var t = 0; t < need; t++) {
			if (trygetcache(cachekey(page, t, KIND_FULL), out var full)) {
				bmps[t] = full;
				continue;
			}
			if (trygetcache(cachekey(page, t, KIND_PREVIEW), out var prev)) {
				bmps[t] = prev;
				continue;
			}
			return false; // 未齐套：保持软单图
		}
		// 多块时拒绝「整页形态」位图（防止旧单页缓存被当成条带）
		if (need > 1 && pageW[page] > 1 && pageH[page] > 1) {
			var pageAr = pageH[page] / pageW[page];
			for (var t = 0; t < need; t++) {
				var bs = bmps[t];
				if (bs == null || bs.PixelWidth < 8) return false;
				var ar = bs.PixelHeight / (double)bs.PixelWidth;
				var expect = tileheightpx(page, t) / pageW[page];
				if (expect < 0.05) expect = 0.05;
				if (Math.Abs(ar - pageAr) + 0.08 < Math.Abs(ar - expect))
					return false;
			}
		}
		replaceslotimages(page, bmps);
		return true;
	}

	/// <summary>用已就绪的新位图替换页槽图像，一步到位，避免旧图被 TILE 高度压扁。</summary>
	void replaceslotimages(int page, BitmapSource[] bmps) {
		if (!slots.TryGetValue(page, out var slot) || slot?.Host == null || bmps == null || bmps.Length == 0)
			return;
		var need = bmps.Length;
		if (slot.Host.Child is not Grid panel || panel.Children.Count < 1) return;
		var stack = panel.Children[0] as StackPanel;
		if (stack == null) return;
		// 先建好全部 Image 再替换 Children，减少中间帧
		var imgs = new System.Windows.Controls.Image[need];
		for (var t = 0; t < need; t++) {
			var img = new System.Windows.Controls.Image {
				Stretch = Stretch.Fill,
				SnapsToDevicePixels = true,
				UseLayoutRounding = true,
				Source = bmps[t],
			};
			imgs[t] = img;
		}
		stack.Children.Clear();
		foreach (var img in imgs)
			stack.Children.Add(img);
		slot.Tiles = imgs;
		if (slot.PageLabel != null)
			slot.PageLabel.Visibility = Visibility.Collapsed;
		layoutslot(slot, fillExistingTiles: false);
		// 布局后按「逻辑宽≈控件宽」选最近邻，避免全清图也被 Fant 抹糊
		for (var t = 0; t < need; t++) {
			if (imgs[t].Source is BitmapSource bs)
				setbitmapsmoothing(imgs[t], bs);
		}
	}

	/// <param name="aggressive">true=视口/预取页（预览+全清）；false=仅保槽页补预览。</param>
	void ensurerender(int page, int g, bool aggressive) {
		if (zoomHold) return; // 交互缩放中不重渲
		if (!slots.TryGetValue(page, out var slot)) return;
		var tiles = tilecount(page);
		// 缓存齐套才一次性换分块图；未齐套则强制软单图
		if (tryapplyalltiles(slot)) {
			if (!aggressive) return;
		} else {
			forcesoftsingle(slot);
		}
		var vpTop = scroller.VerticalOffset;
		var vpH = Math.Max(1, scroller.ViewportHeight);
		var vpBottom = vpTop + vpH;
		var margin = Math.Max(120, vpH * 0.85);
		var pageY = pageTop[page];

		for (var t = 0; t < tiles; t++) {
			var ty = pageY + tileoffsetpx(page, t);
			var th = tileheightpx(page, t);
			if (ty + th < vpTop - margin || ty > vpBottom + margin) {
				if (!aggressive) continue;
			}
			// 只排队，不单块贴图
			var fullKey = cachekey(page, t, KIND_FULL);
			if (trygetcache(fullKey, out _)) {
				if (aggressive) { /* 已有全清，下面齐套时贴 */ }
				continue;
			}
			var prevKey = cachekey(page, t, KIND_PREVIEW);
			if (trygetcache(prevKey, out _)) {
				if (aggressive)
					offer(page, t, KIND_FULL, g);
				continue;
			}
			offer(page, t, KIND_PREVIEW, g);
			if (aggressive)
				offer(page, t, KIND_FULL, g);
		}
		tryapplyalltiles(slot);
	}

	void applytile(PageSlot slot, int tile, BitmapSource bmp, bool hideLabel) {
		if (slot?.Tiles == null || tile < 0 || tile >= slot.Tiles.Length) return;
		if (!ReferenceEquals(slot.Tiles[tile].Source, bmp)) {
			slot.Tiles[tile].Source = bmp;
			setbitmapsmoothing(slot.Tiles[tile], bmp);
		}
		if (hideLabel && slot.PageLabel != null)
			slot.PageLabel.Visibility = Visibility.Collapsed;
	}

	// ---------- 渲染队列 ----------
	void startworker() {
		workerStop = false;
		worker = new Thread(workloop) {
			IsBackground = true,
			Name = "pdf-render",
		};
		worker.Start();
	}

	void workloop() {
		while (!workerStop) {
			RTask task = null;
			lock (gate) {
				// 清理取消项
				for (var i = queue.Count - 1; i >= 0; i--) {
					if (queue[i].Cancelled || queue[i].Gen != gen)
						queue.RemoveAt(i);
				}
				if (queue.Count == 0) {
					Monitor.Wait(gate, 400);
					continue;
				}
				// 优先：可见/预取区内、距锚点近；预览优先于全清（快滚先出模糊图）
				var best = 0;
				var bestScore = int.MaxValue;
				var anchor = visAnchor;
				var dir = scrollDir;
				for (var i = 0; i < queue.Count; i++) {
					var t = queue[i];
					if (t.Cancelled) continue;
					var dist = Math.Abs(t.Page - anchor);
					// 预览 kind=0 优先；全清 kind=1 稍后
					var score = dist * 10 + t.Kind * 3;
					// 滚动方向前方页加分（更先渲）
					if (dir > 0 && t.Page >= anchor) score -= 4;
					if (dir < 0 && t.Page <= anchor) score -= 4;
					// 预取窗口外降权
					if (t.Page < visFirst - 1 || t.Page > visLast + 1)
						score += 800;
					if (score < bestScore) {
						bestScore = score;
						best = i;
					}
				}
				task = queue[best];
				queue.RemoveAt(best);
			}
			if (task == null || task.Cancelled || task.Gen != gen) continue;
			// 已有更高清缓存则跳过
			if (task.Kind == KIND_PREVIEW && trygetcache(cachekey(task.Page, task.Tile, KIND_FULL), out _))
				continue;
			if (trygetcache(task.CacheKey, out _)) continue;

			try {
				BitmapSource bmp = null;
				var t0 = Environment.TickCount;
				try {
					PdfIo.WithLock(() => {
						if (disposed || session == null || task.Cancelled || task.Gen != gen) return;
						bmp = session.Render(task.Page, task.PixelW, task.PixelH, task.ClipY0, task.ClipY1, task.DipDpi, task.Rotate);
					});
				} catch (OutOfMemoryException) {
					DocViewLog.Warn($"render OOM p={task.Page} {task.PixelW}x{task.PixelH}, shrink retry");
					GC.Collect();
					var pw = Math.Max(200, task.PixelW / 2);
					var ph = Math.Max(200, task.PixelH / 2);
					var c0 = task.ClipY0 / 2;
					var c1 = Math.Max(c0 + 1, task.ClipY1 / 2);
					try {
						PdfIo.WithLock(() => {
							if (disposed || session == null || task.Cancelled) return;
							bmp = session.Render(task.Page, pw, ph, c0, c1, task.DipDpi, task.Rotate);
						});
					} catch (OutOfMemoryException ex) {
						DocViewLog.Error("render OOM retry failed", ex);
						continue;
					}
				}
				if (bmp == null || task.Cancelled || task.Gen != gen) continue;
				if (task.Page == visAnchor && task.Kind == KIND_FULL)
					DocViewLog.Info($"render full p={task.Page} {task.PixelW}x{task.ClipY1 - task.ClipY0} cost={Environment.TickCount - t0}ms");

				var taskLocal = task;
				var bmpLocal = bmp;
				scroller.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => {
					if (disposed || taskLocal.Gen != gen || taskLocal.Cancelled || zoomHold) return;
					putcache(taskLocal.CacheKey, bmpLocal);
					if (!slots.TryGetValue(taskLocal.Page, out var slot)) return;
					// 仅齐套后一次性替换；禁止把条带图单块贴进软单图槽（会拉满整页）
					if (tryapplyalltiles(slot)) return;
					var need = tilecount(taskLocal.Page);
					// 仅 need==1 且已是单块槽时，可直接贴
					if (need == 1 && slot.Tiles != null && slot.Tiles.Length == 1) {
						if (taskLocal.Kind == KIND_FULL || !trygetcache(cachekey(taskLocal.Page, 0, KIND_FULL), out _))
							applytile(slot, 0, bmpLocal, hideLabel: true);
					}
				}));
			} catch (Exception ex) {
				DocViewLog.Error($"render fail p={task.Page} k={task.Kind}", ex);
			}
		}
	}

	void offer(int page, int tile, int kind, int g) {
		if (page < 0 || page >= pageCount) return;
		var key = cachekey(page, tile, kind);
		lock (gate) {
			if (cache.ContainsKey(key)) return;
			foreach (var t in queue)
				if (t.CacheKey == key && t.Gen == g && !t.Cancelled) return;

			// 计算像素尺寸并硬性封顶（防 1100 页 OOM 崩溃）
			var layoutW = pageW[page];
			var layoutH = pageH[page];
			if (layoutW < 1 || layoutH < 1) return;
			var scale = kind == KIND_PREVIEW ? PREVIEW_SCALE : FULL_SHARP;
			// 目标：布局 DIP × 系统 dpiScale × 清晰倍率 ≈ 屏幕物理像素 1:1
			var pixelW = Math.Max(1, (int)Math.Round(layoutW * dpiScale * scale));
			var pixelH = Math.Max(1, (int)Math.Round(layoutH * dpiScale * scale));
			cappixels(ref pixelW, ref pixelH);

			var clipY0 = (int)Math.Round(tileoffsetpx(page, tile) / layoutH * pixelH);
			var clipY1 = (int)Math.Round((tileoffsetpx(page, tile) + tileheightpx(page, tile)) / layoutH * pixelH);
			if (clipY1 <= clipY0) clipY1 = clipY0 + 1;
			if (clipY1 > pixelH) clipY1 = pixelH;

			// 位图 DPI 对齐布局 DIP：WPF 逻辑宽 = layoutW，避免 Stretch 再缩放发糊
			// （即使像素被封顶，也避免「低分辨率图 + 错误 DPI」双重模糊）
			var dipDpi = layoutW > 0.5
				? 96.0 * pixelW / layoutW
				: 96.0 * dpiScale;
			if (dipDpi < 48) dipDpi = 48;
			if (dipDpi > 576) dipDpi = 576;
			queue.Add(new RTask {
				Page = page,
				Tile = tile,
				Kind = kind,
				Gen = g,
				Rotate = pageRotate,
				PixelW = pixelW,
				PixelH = pixelH,
				ClipY0 = clipY0,
				ClipY1 = clipY1,
				DipDpi = dipDpi,
				CacheKey = key,
			});
			// 队列过长：丢掉离当前页最远的
			while (queue.Count > MAX_QUEUE) {
				var worst = 0;
				var worstDist = -1;
				for (var i = 0; i < queue.Count; i++) {
					var d = Math.Abs(queue[i].Page - visAnchor);
					if (d > worstDist) { worstDist = d; worst = i; }
				}
				queue[worst].Cancelled = true;
				queue.RemoveAt(worst);
			}
			Monitor.Pulse(gate);
		}
		queuePulse.Set();
	}

	static void cappixels(ref int pixelW, ref int pixelH) {
		if (pixelW > MAX_EDGE) {
			var r = (double)MAX_EDGE / pixelW;
			pixelW = MAX_EDGE;
			pixelH = Math.Max(1, (int)(pixelH * r));
		}
		if (pixelH > MAX_EDGE * 2) {
			var r = (double)(MAX_EDGE * 2) / pixelH;
			pixelH = MAX_EDGE * 2;
			pixelW = Math.Max(1, (int)(pixelW * r));
		}
		var pixels = (long)pixelW * pixelH;
		if (pixels > MAX_PAGE_PIXELS) {
			var r = Math.Sqrt(MAX_PAGE_PIXELS / (double)pixels);
			pixelW = Math.Max(1, (int)(pixelW * r));
			pixelH = Math.Max(1, (int)(pixelH * r));
		}
	}

	void cancelall() {
		lock (gate) {
			foreach (var t in queue) t.Cancelled = true;
			queue.Clear();
			Monitor.PulseAll(gate);
		}
	}

	int tilecount(int page) {
		var h = pageH[page];
		if (h <= TILE_MAX_DIP) return 1;
		return Math.Max(1, (int)Math.Ceiling(h / TILE_MAX_DIP));
	}

	double tileheightpx(int page, int tile) {
		var h = pageH[page];
		var n = tilecount(page);
		if (n <= 1) return h;
		if (tile < n - 1) return TILE_MAX_DIP;
		return Math.Max(1, h - TILE_MAX_DIP * (n - 1));
	}

	double tileoffsetpx(int page, int tile) => tile <= 0 ? 0 : TILE_MAX_DIP * tile;

	int findpageat(double y) {
		if (pageCount <= 0) return 0;
		var lo = 0;
		var hi = pageCount - 1;
		while (lo < hi) {
			var mid = (lo + hi + 1) / 2;
			if (pageTop[mid] <= y) lo = mid;
			else hi = mid - 1;
		}
		return lo;
	}

	int estimatepage() {
		if (pageCount <= 0) return 0;
		var mid = scroller.VerticalOffset + scroller.ViewportHeight * 0.3;
		var p = findpageat(mid);
		if (p < 0) p = 0;
		if (p >= pageCount) p = pageCount - 1;
		return p;
	}

	double pagefrac(int page) {
		if (page < 0 || page >= pageCount) return 0;
		var rel = scroller.VerticalOffset - pageTop[page];
		var h = pageH[page];
		if (h < 1) return 0;
		return clamp(rel / h, 0, 1);
	}

	/// <param name="topFrac">距页顶比例 0=页首，目录跳转时用书签 Y 把章节标题顶到视口上沿。</param>
	void scrolltopage(int page, bool fromOutline = false, double topFrac = 0) {
		if (page < 0 || page >= pageCount || disposed) return;
		if (pageTop == null || page >= pageTop.Length) return;
		try {
			topFrac = clamp(topFrac, 0, 0.98);
			var y = pageTop[page];
			if (topFrac > 0.0001 && pageH != null && page < pageH.Length)
				y += pageH[page] * topFrac;
			scroller.ScrollToVerticalOffset(y);
			// 立刻刷视口 + 优先当前页
			visAnchor = page;
			// 取消窗口外渲染任务，避免快速跳章堆积
			lock (gate) {
				foreach (var t in queue)
					if (Math.Abs(t.Page - page) > 3)
						t.Cancelled = true;
			}
			scheduleui();
			// 目录点击勿再 syncoutline，否则 SelectedItemChanged 重入崩溃
			if (!fromOutline)
				syncoutline(force: true);
			raisestatus();
		} catch (Exception ex) {
			DocViewLog.Error($"scrolltopage {page}", ex);
		}
	}

	void scrolltopagefrac(int page, double frac) {
		if (page < 0 || page >= pageCount) { scheduleui(); return; }
		frac = clamp(frac, 0, 1);
		scroller.ScrollToVerticalOffset(pageTop[page] + pageH[page] * frac);
	}

	void clearslots() {
		foreach (var kv in slots)
			canvas.Children.Remove(kv.Value.Host);
		slots.Clear();
	}

	void clearcache() {
		lock (gate) {
			cache.Clear();
			lru.Clear();
		}
	}

	void cleartextcache() {
		lock (textCache) {
			textCache.Clear();
			textLru.Clear();
			textPending.Clear();
		}
	}

	static long cachekey(int page, int tile, int kind) =>
		((long)page << 20) | ((long)(tile & 0xFF) << 12) | ((long)(kind & 0xF) << 8);

	bool trygetcache(long key, out BitmapSource bmp) {
		lock (gate) {
			if (cache.TryGetValue(key, out bmp)) {
				lru.Remove(key);
				lru.AddLast(key);
				return true;
			}
		}
		bmp = null;
		return false;
	}

	void putcache(long key, BitmapSource bmp) {
		lock (gate) {
			if (cache.ContainsKey(key)) {
				cache[key] = bmp;
				lru.Remove(key);
				lru.AddLast(key);
				return;
			}
			cache[key] = bmp;
			lru.AddLast(key);
			while (lru.Count > MAX_CACHE_ENTRIES) {
				var old = lru.First.Value;
				lru.RemoveFirst();
				cache.Remove(old);
			}
		}
	}

	void raisestatus() => StatusChanged?.Invoke();

	/// <summary>当前旋转下的页尺寸（pt）。90°/270° 时宽高互换。</summary>
	void viewpagesizept(int page, out double w, out double h) {
		var pt = pageSizesPt[page];
		if (pageRotate == 1 || pageRotate == 3) {
			w = pt.Height;
			h = pt.Width;
		} else {
			w = pt.Width;
			h = pt.Height;
		}
	}

	/// <summary>Ctrl+点击链接：书内跳转或打开 URI。</summary>
	bool trylinkat(WpfPoint canvasPt) {
		var hit = hitlink(canvasPt);
		if (hit == null) return false;
		if (hit.DestPageIndex >= 0 && hit.DestPageIndex < pageCount) {
			var frac = hit.HasDestY ? clamp(hit.TopFrac, 0, 0.98) : 0;
			DocViewLog.Info($"PdfViewer link → page={hit.DestPageIndex + 1} frac={frac:F3}");
			jumpwithhistory(hit.DestPageIndex, frac, fromOutline: true);
			return true;
		}
		if (!string.IsNullOrEmpty(hit.Uri)) {
			tryopenuri(hit.Uri);
			return true;
		}
		return false;
	}

	/// <summary>显式跳转：先记当前位置再滚到目标。</summary>
	void jumpwithhistory(int page, double topFrac, bool fromOutline) {
		if (page < 0 || page >= pageCount || disposed) return;
		topFrac = clamp(topFrac, 0, 0.98);
		pushnavbeforejump(page, topFrac);
		scrolltopage(page, fromOutline: fromOutline, topFrac: topFrac);
	}

	struct NavMark {
		public int Page;
		public double TopFrac;
		public double H;
	}

	NavMark capturenav() {
		var p = 0;
		if (pageCount > 0 && pageTop != null)
			p = findpageat(scroller.VerticalOffset + 1);
		if (p < 0) p = 0;
		if (p >= pageCount) p = Math.Max(0, pageCount - 1);
		return new NavMark {
			Page = p,
			TopFrac = pagefrac(p),
			H = scroller?.HorizontalOffset ?? 0,
		};
	}

	/// <summary>跳转前压入当前位置；目标与当前几乎相同则跳过。</summary>
	void pushnavbeforejump(int toPage, double toFrac) {
		if (navRestoring || disposed || pageCount <= 0) return;
		var cur = capturenav();
		toFrac = clamp(toFrac, 0, 0.98);
		// 目标几乎就是当前视口：不记
		if (cur.Page == toPage && Math.Abs(cur.TopFrac - toFrac) < 0.01)
			return;
		// 与栈顶重复不叠推
		if (navBack.Count > 0 && nearmark(navBack[navBack.Count - 1], cur))
			return;
		navBack.Add(cur);
		if (navBack.Count > MAX_NAV) navBack.RemoveAt(0);
		navFwd.Clear();
	}

	void restorenav(NavMark m) {
		if (disposed || pageCount <= 0) return;
		navRestoring = true;
		try {
			var p = m.Page;
			if (p < 0) p = 0;
			if (p >= pageCount) p = pageCount - 1;
			scrolltopage(p, fromOutline: false, topFrac: clamp(m.TopFrac, 0, 0.98));
			try {
				scroller.ScrollToHorizontalOffset(Math.Max(0, m.H));
			} catch { /* ignore */ }
		} finally {
			navRestoring = false;
		}
	}

	void clearnavhistory() {
		navBack.Clear();
		navFwd.Clear();
	}

	static bool nearmark(NavMark a, NavMark b) =>
		a.Page == b.Page
		&& Math.Abs(a.TopFrac - b.TopFrac) < 0.02
		&& Math.Abs(a.H - b.H) < 8;

	/// <summary>命中页内链接（canvas 坐标）。</summary>
	PdfLinkHit hitlink(WpfPoint canvasPt) {
		if (pageCount <= 0 || session == null || pageW == null || pageH == null) return null;
		if (!canvastopagept(canvasPt, out var page, out var vx, out var vy)) return null;
		// 视图旋转坐标 → 未旋转页坐标（左上 Y 向下）
		var ow = pageSizesPt[page].Width;
		var oh = pageSizesPt[page].Height;
		unmapviewpt(ref vx, ref vy, ow, oh, pageRotate);
		PdfLinkHit hit = null;
		try {
			PdfIo.WithLock(() => {
				if (session == null) return;
				hit = session.HitLink(page, vx, vy);
			});
		} catch (Exception ex) {
			DocViewLog.Warn($"HitLink p={page}: {ex.Message}");
			return null;
		}
		return hit;
	}

	/// <summary>canvas → 当前视图页坐标（旋转后，左上 Y 向下，pt）。</summary>
	bool canvastopagept(WpfPoint canvasPt, out int page, out double pageX, out double pageY) {
		page = -1;
		pageX = 0;
		pageY = 0;
		if (pageCount <= 0 || pageW == null || pageH == null || pageTop == null) return false;
		var p = findpageat(canvasPt.Y);
		if (p < 0 || p >= pageCount) return false;
		var left = Math.Max(0, (contentW - pageW[p]) / 2);
		var top = pageTop[p];
		var relX = canvasPt.X - left;
		var relY = canvasPt.Y - top;
		if (relX < -2 || relX > pageW[p] + 2) return false;
		if (relY < -2 || relY > pageH[p] + 2) return false;
		viewpagesizept(p, out var vpw, out var vph);
		var sx = vpw / Math.Max(1e-6, pageW[p]);
		var sy = vph / Math.Max(1e-6, pageH[p]);
		page = p;
		pageX = relX * sx;
		pageY = relY * sy;
		return true;
	}

	static void tryopenuri(string uri) {
		if (string.IsNullOrWhiteSpace(uri)) return;
		// 仅允许常见协议，避免 javascript: 等
		var ok = uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			|| uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
			|| uri.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);
		if (!ok) {
			DocViewLog.Warn($"link URI blocked: {uri}");
			return;
		}
		try {
			Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
		} catch (Exception ex) {
			DocViewLog.Warn($"open URI: {ex.Message}");
		}
	}

	/// <summary>将未旋转页坐标的字符列表映射到当前视图旋转坐标。</summary>
	void mapcharlistrotate(List<PdfCharInfo> chars, int page) {
		if (chars == null || pageRotate == 0 || page < 0 || page >= pageCount) return;
		var ow = pageSizesPt[page].Width;
		var oh = pageSizesPt[page].Height;
		foreach (var c in chars)
			mapboxrotate(ref c.Left, ref c.Top, ref c.Right, ref c.Bottom, ow, oh, pageRotate);
	}

	/// <summary>
	/// 视图页坐标（旋转后左上 Y 向下）→ 未旋转页坐标。W/H 为未旋转页宽高。
	/// </summary>
	static void unmapviewpt(ref double x, ref double y, double W, double H, int rot) {
		rot = ((rot % 4) + 4) % 4;
		if (rot == 0) return;
		double nx, ny;
		switch (rot) {
		case 1: // 正向 (x,y)→(H-y,x)；逆 (rx,ry)→(ry, H-rx)
			nx = y;
			ny = H - x;
			break;
		case 2:
			nx = W - x;
			ny = H - y;
			break;
		case 3: // 正向 (x,y)→(y,W-x)；逆 (rx,ry)→(W-ry, rx)
			nx = W - y;
			ny = x;
			break;
		default:
			return;
		}
		x = nx;
		y = ny;
	}

	/// <summary>
	/// 页坐标盒子：未旋转左上原点 Y 向下 → 旋转后同约定。
	/// rot：0/1/2/3 = 0°/90°CW/180°/270°CW。
	/// </summary>
	static void mapboxrotate(ref double L, ref double T, ref double R, ref double B, double W, double H, int rot) {
		rot = ((rot % 4) + 4) % 4;
		if (rot == 0) return;
		double nL, nT, nR, nB;
		switch (rot) {
		case 1: // 90° CW：(x,y)→(H-y,x)
			nL = H - B;
			nT = L;
			nR = H - T;
			nB = R;
			break;
		case 2: // 180°
			nL = W - R;
			nT = H - B;
			nR = W - L;
			nB = H - T;
			break;
		case 3: // 270° CW / 90° CCW：(x,y)→(y,W-x)
			nL = T;
			nT = W - R;
			nR = B;
			nB = W - L;
			break;
		default:
			return;
		}
		if (nR < nL) { var t = nL; nL = nR; nR = t; }
		if (nB < nT) { var t = nT; nT = nB; nB = t; }
		L = nL; T = nT; R = nR; B = nB;
	}

	static double clamp(double v, double lo, double hi) {
		if (v < lo) return lo;
		if (v > hi) return hi;
		return v;
	}

	static BitmapSource placeholdertiny() {
		var wb = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgr32, null);
		var pixels = new byte[] {
			0xF3, 0xF4, 0xF6, 0xFF, 0xF3, 0xF4, 0xF6, 0xFF,
			0xF3, 0xF4, 0xF6, 0xFF, 0xF3, 0xF4, 0xF6, 0xFF,
		};
		wb.WritePixels(new Int32Rect(0, 0, 2, 2), pixels, 8, 0);
		wb.Freeze();
		return wb;
	}
}
