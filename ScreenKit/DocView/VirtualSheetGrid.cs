using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace ScreenKit.DocView;

/// <summary>
/// 虚拟表格（Canvas/OnRender 自绘）：
/// 数据在内存 SheetModel 中；滚动时只绘制视口内单元格，无 FrameworkElement 单元格树。
/// </summary>
sealed class VirtualSheetGrid : UserControl {
	const double DEF_COL_W = 64;
	const double DEF_ROW_H = 20;
	const double HDR_H = 26;
	const double ROW_HDR_W = 48;
	const double MIN_COL_W = 4;
	const double MIN_ROW_H = 2;
	const double RESIZE_MIN_COL = 12;
	/// <summary>列宽不设实用上限（仅防溢出用极大值）。</summary>
	const double RESIZE_MAX_COL = 1e7;
	const double RESIZE_MIN_ROW = 8;
	const double RESIZE_MAX_ROW = 400;
	/// <summary>列/行分隔线命中半宽（DIP）；略大便于双击 autofit。</summary>
	const double EDGE_HIT = 6;
	/// <summary>双击列边 autofit 时额外放宽命中（相对 EDGE_HIT）。</summary>
	const double EDGE_HIT_AUTOFIT = 10;
	const int AUTOFIT_FULL_CELL_MAX = 2500;
	const int AUTOFIT_SKIP_ROWS = 3000;
	/// <summary>双击自适应列宽最多扫描行数（大表上限）。</summary>
	const int AUTOFIT_COL_MAX_ROWS = 8000;
	/// <summary>
	/// 表体高度低于此值才用视口撑大 outer。
	/// 长表若随最大化改 outer 宽，WPF 会对数万 DIP 高的 Canvas 做整表布局，卡顿约 1s。
	/// </summary>
	const double EXPAND_VIEWPORT_MAX_BODY_H = 8000;
	/// <summary>滚动停稳后多久切回完整绘制（ms）。</summary>
	const int SCROLL_SETTLE_MS = 90;
	/// <summary>进度/状态回调节流（ms）。</summary>
	const int SCROLL_PROGRESS_MS = 200;

	readonly ScrollViewer scroll;
	/// <summary>仅用于撑开滚动条范围（空壳）。</summary>
	readonly Canvas outer;
	/// <summary>视口大小的自绘层，钉在 offset 上。</summary>
	readonly SheetSurface surface;
	/// <summary>滚动中：轻量模式标记（视口已裁剪后仍画文字，仅用于节流进度回调）。</summary>
	bool scrubbing;
	DispatcherTimer settleTimer;
	int lastProgressTick;
	/// <summary>缓存 DPI，FormattedText 用对 pixelsPerDip 才能出字。</summary>
	double pixelsPerDip = 1.0;

	SheetModel model = new();
	int rows, cols;
	double[] colW = Array.Empty<double>();
	double[] rowH = Array.Empty<double>();
	double[] colX = Array.Empty<double>();
	double[] rowY = Array.Empty<double>();
	double totalBodyW, totalBodyH;
	double zoom = 1.0;
	/// <summary>当前视口左上角表坐标。</summary>
	double originX, originY;

	int selR0 = -1, selC0 = -1, selR1 = -1, selC1 = -1;
	bool selecting;
	/// <summary>正在拖选整列（列头）/ 整行（行号）。</summary>
	bool selectingCols, selectingRows;
	int anchorR, anchorC;
	/// <summary>当前活动格（Shift+方向键块选时移动此角，锚点不变）。</summary>
	int activeR = -1, activeC = -1;
	/// <summary>编辑模式：允许改格、合并、样式。</summary>
	bool editMode;
	/// <summary>单元格内联编辑框（叠在 outer 上，随表滚动）。</summary>
	TextBox editBox;
	int editR = -1, editC = -1;
	bool editCommiting;

	bool resizingCol, resizingRow;
	int resizeIdx = -1;
	double resizeStartMouse;
	double resizeStartBase;

	string findQuery;
	bool findIgnoreCase = true;
	readonly List<(int R, int C)> findHits = new List<(int R, int C)>();
	int findIndex = -1;

	// 冻结窗格（来自 SheetModel）
	int freezeRows, freezeCols;
	// 列筛选：key=列索引
	readonly Dictionary<int, ColFilter> colFilters = new Dictionary<int, ColFilter>();
	/// <summary>行是否被筛选隐藏（不含冻结区内强制显示）。</summary>
	bool[] rowHidden = Array.Empty<bool>();
	int filterHdrRow = -1;
	int filterR0 = -1, filterR1 = -1, filterC0 = -1, filterC1 = -1;
	Popup filterPopup;
	int filterPopupCol = -1;
	MouseButtonEventHandler filterOutsideHandler;
	bool filterOutsideHooked;
	/// <summary>当前排序列（-1=无）；仅作用于筛选数据区（表头行以下）。</summary>
	int sortCol = -1;
	/// <summary>true=升序，false=降序。</summary>
	bool sortAsc = true;
	/// <summary>右键命中的内嵌图片。</summary>
	SheetPicture ctxPic;
	/// <summary>首次排序前备份的数据行，用于「取消排序」恢复。</summary>
	SheetCell[][] sortBackupRows;
	double[] sortBackupH;
	int sortBackupR0 = -1, sortBackupR1 = -1;

	// 视口缓存（由 ScrollChanged 写入，OnRender 读取）
	double viewX, viewY, viewW, viewH;

	static readonly Typeface TypefaceUi = new Typeface(
		new FontFamily("Segoe UI, Microsoft YaHei UI"),
		FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
	static readonly Typeface TypefaceUiBold = new Typeface(
		new FontFamily("Segoe UI, Microsoft YaHei UI"),
		FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

	readonly SolidColorBrush brushGrid = brush(0xE5, 0xE7, 0xEB);
	readonly SolidColorBrush brushHdr = brush(0xF3, 0xF4, 0xF6);
	/// <summary>行号/列头处于选区内时的高亮底。</summary>
	readonly SolidColorBrush brushHdrSel = brush(0xBF, 0xDB, 0xFE);
	readonly SolidColorBrush brushSel = brush(0xBF, 0xDB, 0xFE);
	readonly SolidColorBrush brushSelBorder = brush(0x25, 0x63, 0xEB);
	readonly SolidColorBrush brushText = brush(0x11, 0x18, 0x27);
	readonly SolidColorBrush brushHdrText = brush(0x37, 0x41, 0x51);
	readonly SolidColorBrush brushSelFill;
	readonly SolidColorBrush brushFindHit;
	readonly SolidColorBrush brushFindCur;
	readonly Pen penGrid;
	readonly Pen penSel;
	static readonly Dictionary<int, SolidColorBrush> ColorBrushCache = new Dictionary<int, SolidColorBrush>();
	static readonly Dictionary<string, Typeface> TypefaceCache = new Dictionary<string, Typeface>(StringComparer.Ordinal);

	public VirtualSheetGrid() {
		Focusable = true;
		Background = brush(0xF3, 0xF4, 0xF6);
		// 表体靠左靠上，禁止在 Tab 内容区被居中
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;
		HorizontalContentAlignment = HorizontalAlignment.Left;
		VerticalContentAlignment = VerticalAlignment.Top;

		penGrid = new Pen(brushGrid, 1.0);
		if (penGrid.CanFreeze) penGrid.Freeze();
		penSel = new Pen(brushSelBorder, 1.5);
		if (penSel.CanFreeze) penSel.Freeze();
		brushSelFill = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xBF, 0xDB, 0xFE));
		if (brushSelFill.CanFreeze) brushSelFill.Freeze();
		// 查找：其它命中浅黄，当前命中金黄
		brushFindHit = new SolidColorBrush(MediaColor.FromArgb(0x99, 0xFF, 0xF5, 0x9D));
		if (brushFindHit.CanFreeze) brushFindHit.Freeze();
		brushFindCur = new SolidColorBrush(MediaColor.FromArgb(0xC0, 0xFF, 0xC1, 0x07));
		if (brushFindCur.CanFreeze) brushFindCur.Freeze();

		outer = new Canvas {
			Background = Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			// surface 按视口尺寸钉在滚动偏移上，可大于 outer（长表 outer 不随视口撑开）
			ClipToBounds = false,
		};
		surface = new SheetSurface(this) {
			SnapsToDevicePixels = true,
			UseLayoutRounding = true,
			ClipToBounds = true,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
		};
		outer.Children.Add(surface);
		editBox = new TextBox {
			Visibility = Visibility.Collapsed,
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			BorderBrush = brushSelBorder,
			BorderThickness = new Thickness(2),
			Padding = new Thickness(2, 0, 2, 0),
			FontSize = 12,
			Background = Brushes.White,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		editBox.LostKeyboardFocus += (_, _) => {
			if (!editCommiting) endcelledit(commit: true);
		};
		editBox.PreviewKeyDown += oneditkeydown;
		Panel.SetZIndex(editBox, 20);
		outer.Children.Add(editBox);

		scroll = new ScrollViewer {
			Content = outer,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			CanContentScroll = false,
			Focusable = false,
			Background = Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Left,
			VerticalContentAlignment = VerticalAlignment.Top,
		};
		// 方向键默认会滚 ScrollViewer，改成移动选中格（仿 Excel）
		KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
		KeyboardNavigation.SetDirectionalNavigation(scroll, KeyboardNavigationMode.None);
		bindscrollnav();
		scroll.PreviewKeyDown += onkey;
		scroll.ScrollChanged += (_, e) => {
			// 拖列宽/行高时 outer 已由 apply*resize 更新；再走这里会二次全量 Invalidate，末列尤其卡
			if (resizingCol || resizingRow) {
				if (e.HorizontalChange != 0 || e.VerticalChange != 0)
					pinviewport();
				return;
			}
			// Extent 变化来自 applytablesize 写 outer，切勿再 applytablesize（会循环布局）
			pinviewport();
			var moved = e.HorizontalChange != 0 || e.VerticalChange != 0;
			if (moved) {
				// 拖条/滚轮中：轻量重绘，停稳后再画文字
				if (!scrubbing) scrubbing = true;
				schedulesettle();
				var now = Environment.TickCount;
				if (now - lastProgressTick >= SCROLL_PROGRESS_MS || lastProgressTick == 0) {
					lastProgressTick = now;
					try { ScrollProgressChanged?.Invoke(); } catch { /* ignore */ }
				}
			}
			// 仅滚动偏移变化需要重绘；纯 Extent 变化 pin 后尺寸已对，仍 Invalidate 一次即可
			surface.InvalidateVisual();
		};
		// 最大化/改窗体：只钉视口；长表不在这里撑 outer（见 applytablesize / onviewportresized）
		scroll.SizeChanged += (_, _) => onviewportresized();
		SizeChanged += (_, e) => {
			// 首次从 0 获得实际尺寸时补一刀（Tab 切换）
			if (e.PreviousSize.Width < 2 || e.PreviousSize.Height < 2)
				onviewportresized();
		};
		// Ctrl+滚轮：以鼠标位置为中心缩放
		scroll.PreviewMouseWheel += onpreviewwheel;

		Content = scroll;

		surface.MouseLeftButtonDown += ondown;
		surface.MouseMove += onmove;
		surface.MouseLeftButtonUp += onup;
		surface.MouseLeftButtonDown += (_, e) => {
			// 双击图片优先于进入单元格编辑
			if (e.ClickCount < 2) return;
			if (hitpicture(e.GetPosition(surface)) != null) return;
			if (!editMode) return;
			if (editR >= 0) endcelledit(commit: true);
			var pt = toabs(e.GetPosition(surface));
			if (!hittest(pt, out var r, out var c)) return;
			model.ResolveOrigin(ref r, ref c);
			begincelledit(r, c);
			e.Handled = true;
		};
		initpicmenu();
		surface.LostMouseCapture += (_, _) => {
			if (resizingCol || resizingRow) endresize();
			if (selecting) {
				selecting = false;
				selectingCols = false;
				selectingRows = false;
			}
		};
		Focusable = true;
		surface.Focusable = true;
		PreviewKeyDown += onkey;
		surface.PreviewKeyDown += onkey;
		Loaded += (_, _) => {
			try {
				pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
				if (pixelsPerDip < 0.5) pixelsPerDip = 1.0;
			} catch { pixelsPerDip = 1.0; }
			pinviewport();
			surface.InvalidateVisual();
		};
	}

	/// <summary>把 ScrollViewer 的方向键命令改成移动单元格，避免滚视窗。</summary>
	void bindscrollnav() {
		void bind(RoutedCommand cmd, int dr, int dc) {
			scroll.CommandBindings.Add(new CommandBinding(cmd,
				(_, e) => {
					moveselection(dr, dc, wrap: false);
					e.Handled = true;
				},
				(_, e) => {
					e.CanExecute = true;
					e.Handled = true;
				}));
		}
		bind(ScrollBar.LineUpCommand, -1, 0);
		bind(ScrollBar.LineDownCommand, 1, 0);
		bind(ScrollBar.LineLeftCommand, 0, -1);
		bind(ScrollBar.LineRightCommand, 0, 1);
	}

	public void SetData(SheetModel sheet, double zoomFactor = 1.0) {
		model = sheet ?? new SheetModel();
		rows = model.Rows;
		cols = model.Cols;
		if (cols <= 0 && model.Cells != null) {
			foreach (var r in model.Cells)
				if (r != null && r.Length > cols) cols = r.Length;
		}
		if (model.RowHeights != null && model.RowHeights.Length > rows)
			rows = model.RowHeights.Length;
		if (model.ColWidths != null && model.ColWidths.Length > cols)
			cols = model.ColWidths.Length;
		freezeRows = Math.Max(0, Math.Min(model.FreezeRows, rows));
		freezeCols = Math.Max(0, Math.Min(model.FreezeCols, cols));
		setupfilterrange();
		colFilters.Clear();
		sortCol = -1;
		sortAsc = true;
		clearsortbackup();
		rowHidden = new bool[Math.Max(0, rows)];
		zoom = clamp(zoomFactor, 0.5, 2.5);
		rebuildmetrics();
		autofitrowheights();
		applyrowhiddenheights();
		selR0 = selC0 = selR1 = selC1 = -1;
		decodepictures();
		applytablesize();
		pinviewport();
		surface.InvalidateVisual();
		// 布局完成后再钉一次视口：打开瞬间 Viewport 常为 0，否则 surface 尺寸不对会整页空白
		try {
			Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
				if (model == null) return;
				applytablesize();
				surface.InvalidateVisual();
			}));
		} catch { /* ignore */ }
	}

	void setupfilterrange() {
		filterHdrRow = -1;
		filterR0 = filterR1 = filterC0 = filterC1 = -1;
		if (model == null || rows <= 0 || cols <= 0) return;
		// 仅当工作表自带 AutoFilter（HasFilterRange）时显示筛选，勿默认第 0 行
		// （装箱清单等：第 1 行是合并标题，真正表头在更下方）
		if (!model.HasFilterRange) return;

		filterHdrRow = model.FilterR0;
		filterR0 = model.FilterR0;
		filterR1 = Math.Min(model.FilterR1, rows - 1);
		filterC0 = Math.Max(0, model.FilterC0);
		filterC1 = Math.Min(model.FilterC1, cols - 1);

		// 有冻结且筛选表头落在冻结区内：按钮钉在冻结底行
		if (freezeRows > 0 && freezeRows <= rows && filterHdrRow >= 0 && filterHdrRow < freezeRows) {
			filterHdrRow = freezeRows - 1;
			filterR0 = filterHdrRow;
		}
		// AutoFilter 有时只写表头一行 → 扩展到全部数据行
		if (filterHdrRow >= 0 && filterR1 <= filterHdrRow && rows > filterHdrRow + 1)
			filterR1 = rows - 1;
		if (filterC0 < 0) filterC0 = 0;
		if (filterC1 < filterC0) filterC1 = cols - 1;
		if (filterC1 >= cols) filterC1 = cols - 1;
	}

	bool canfiltercol(int c) =>
		filterHdrRow >= 0
		&& filterR1 > filterHdrRow
		&& c >= filterC0 && c <= filterC1
		&& c >= 0 && c < cols;

	double freezew() =>
		freezeCols > 0 && freezeCols <= cols && colX != null && freezeCols < colX.Length
			? colX[freezeCols] : 0;

	double freezeh() =>
		freezeRows > 0 && freezeRows <= rows && rowY != null && freezeRows < rowY.Length
			? rowY[freezeRows] : 0;

	/// <summary>表体列坐标 → surface 局部 X（冻结列不随横向滚动）。</summary>
	double bodylocalx(int c) {
		var tx = ROW_HDR_W + (c >= 0 && c < colX.Length ? colX[c] : 0);
		if (c < freezeCols) return tx; // 相对视口固定：不减 originX
		return tx - originX;
	}

	/// <summary>表体行坐标 → surface 局部 Y。</summary>
	double bodylocaly(int r) {
		var ty = HDR_H + (r >= 0 && r < rowY.Length ? rowY[r] : 0);
		if (r < freezeRows) return ty;
		return ty - originY;
	}

	/// <summary>surface 局部 → 表绝对坐标（考虑冻结）。</summary>
	WpfPoint toabs(WpfPoint local) {
		double ax, ay;
		var fw = freezew();
		var fh = freezeh();
		// X
		if (local.X < ROW_HDR_W)
			ax = local.X;
		else if (local.X < ROW_HDR_W + fw)
			ax = local.X; // 冻结区：local 即表坐标
		else
			ax = local.X + originX;
		// Y
		if (local.Y < HDR_H)
			ay = local.Y;
		else if (local.Y < HDR_H + fh)
			ay = local.Y;
		else
			ay = local.Y + originY;
		return new WpfPoint(ax, ay);
	}

	public double Zoom => zoom;
	/// <summary>Ctrl+滚轮等导致缩放后通知宿主更新状态。</summary>
	public event Action ZoomChanged;

	public void SetZoom(double z) => setzoomcore(z, null);

	/// <param name="mouseInScroll">鼠标相对 ScrollViewer；表体用「未缩放坐标」锚定，行号/列头不参与缩放。</param>
	void setzoomcore(double z, WpfPoint? mouseInScroll) {
		z = clamp(z, 0.5, 2.5);
		if (Math.Abs(z - zoom) < 0.0005) return;
		var old = zoom;
		if (old < 1e-6) old = 1;

		WpfPoint mouse = default;
		// 表体在 zoom=1 时的坐标（行号列宽、列头高固定）
		double baseBodyX = 0, baseBodyY = 0;
		var hasBodyX = false;
		var hasBodyY = false;
		var useAnchor = mouseInScroll.HasValue && scroll != null;
		if (useAnchor) {
			mouse = mouseInScroll.Value;
			var cx = scroll.HorizontalOffset + mouse.X;
			var cy = scroll.VerticalOffset + mouse.Y;
			if (cx > ROW_HDR_W) {
				hasBodyX = true;
				baseBodyX = (cx - ROW_HDR_W) / old;
			}
			if (cy > HDR_H) {
				hasBodyY = true;
				baseBodyY = (cy - HDR_H) / old;
			}
		}

		zoom = z;
		rebuildmetrics();
		applyrowhiddenheights();
		applytablesize();
		try { scroll?.UpdateLayout(); } catch { /* ignore */ }

		if (useAnchor && scroll != null) {
			// 新内容坐标：表头区不变，表体 = 固定边 + base*newZoom
			var newCx = hasBodyX ? ROW_HDR_W + baseBodyX * zoom : scroll.HorizontalOffset + mouse.X;
			var newCy = hasBodyY ? HDR_H + baseBodyY * zoom : scroll.VerticalOffset + mouse.Y;
			// 未落在表体时（纯表头）保持偏移，避免乱跳
			if (!hasBodyX) newCx = scroll.HorizontalOffset + mouse.X;
			if (!hasBodyY) newCy = scroll.VerticalOffset + mouse.Y;
			scroll.ScrollToHorizontalOffset(Math.Max(0, newCx - mouse.X));
			scroll.ScrollToVerticalOffset(Math.Max(0, newCy - mouse.Y));
		}
		pinviewport();
		if (editR >= 0) placeeditbox();
		surface.InvalidateVisual();
		try { ZoomChanged?.Invoke(); } catch { /* ignore */ }
	}

	void onpreviewwheel(object sender, MouseWheelEventArgs e) {
		if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
		e.Handled = true;
		var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
		setzoomcore(zoom * factor, e.GetPosition(scroll));
	}

	/// <summary>滚动变化（用于记忆阅读进度）。</summary>
	public event Action ScrollProgressChanged;

	public void GetScrollOffset(out double h, out double v) {
		h = scroll?.HorizontalOffset ?? 0;
		v = scroll?.VerticalOffset ?? 0;
	}

	public void SetScrollOffset(double h, double v) {
		if (scroll == null) return;
		try {
			if (h > 0) scroll.ScrollToHorizontalOffset(h);
			if (v > 0) scroll.ScrollToVerticalOffset(v);
			pinviewport();
			surface.InvalidateVisual();
		} catch { /* ignore */ }
	}

	public bool HasSelection => selR0 >= 0 && selC0 >= 0;
	/// <summary>当前表行数 / 列数（供状态栏描述选区）。</summary>
	public int Rows => rows;
	public int Cols => cols;
	public bool EditMode {
		get => editMode;
		set {
			if (editMode == value) return;
			if (editMode && !value)
				endcelledit(commit: true);
			editMode = value;
			if (!editMode)
				endcelledit(commit: false);
			surface?.InvalidateVisual();
			try { EditModeChanged?.Invoke(); } catch { /* ignore */ }
		}
	}
	public event Action EditModeChanged;
	public event Action SelectionChanged;
	/// <summary>单元格内容/样式/合并被用户修改。</summary>
	public event Action ModelEdited;
	public SheetModel Model => model;

	public void GetSelection(out int r0, out int c0, out int r1, out int c1) {
		if (!HasSelection) {
			r0 = c0 = r1 = c1 = -1;
			return;
		}
		r0 = Math.Min(selR0, selR1);
		c0 = Math.Min(selC0, selC1);
		r1 = Math.Max(selR0, selR1);
		c1 = Math.Max(selC0, selC1);
	}

	/// <summary>对选区应用样式；action 内可改 cell 字段。</summary>
	public bool ApplyToSelection(Action<SheetCell, int, int> action) {
		if (!editMode || !HasSelection || action == null || model == null) return false;
		endcelledit(commit: true);
		GetSelection(out var r0, out var c0, out var r1, out var c1);
		var n = 0;
		for (var r = r0; r <= r1; r++) {
			for (var c = c0; c <= c1; c++) {
				var cell = model.CellAt(r, c);
				if (cell.HiddenByMerge) continue;
				var mut = model.EnsureCell(r, c);
				action(mut, r, c);
				n++;
			}
		}
		if (n == 0) return false;
		surface.InvalidateVisual();
		notifyedited();
		return true;
	}

	public bool MergeSelection() {
		if (!editMode || !HasSelection || model == null) return false;
		endcelledit(commit: true);
		GetSelection(out var r0, out var c0, out var r1, out var c1);
		if (r0 == r1 && c0 == c1) return false;
		if (!model.MergeRange(r0, c0, r1, c1)) return false;
		// 选区保持合并块
		selR0 = r0; selC0 = c0; selR1 = r1; selC1 = c1;
		rows = model.Rows;
		cols = model.Cols;
		rebuildmetrics();
		applytablesize();
		surface.InvalidateVisual();
		notifyedited();
		raiseselection();
		return true;
	}

	public bool UnmergeSelection() {
		if (!editMode || !HasSelection || model == null) return false;
		endcelledit(commit: true);
		GetSelection(out var r0, out var c0, out var r1, out var c1);
		if (!model.UnmergeRange(r0, c0, r1, c1)) return false;
		rows = model.Rows;
		cols = model.Cols;
		rebuildmetrics();
		applytablesize();
		surface.InvalidateVisual();
		notifyedited();
		return true;
	}

	/// <summary>选中单元格自动换行开/关。</summary>
	public bool SetWrapSelection(bool wrap) {
		if (!editMode || !HasSelection || model == null) return false;
		endcelledit(commit: true);
		GetSelection(out var r0, out var c0, out var r1, out var c1);
		var n = 0;
		for (var r = r0; r <= r1; r++) {
			for (var c = c0; c <= c1; c++) {
				var cell = model.CellAt(r, c);
				if (cell.HiddenByMerge) continue;
				var mut = model.EnsureCell(r, c);
				mut.WrapText = wrap;
				n++;
			}
		}
		if (n == 0) return false;
		afterwrapchange(r0, r1);
		return true;
	}

	/// <summary>选中行整行自动换行。</summary>
	public bool SetWrapRows(bool wrap) {
		if (!editMode || !HasSelection || model == null || cols <= 0) return false;
		endcelledit(commit: true);
		GetSelection(out var r0, out _, out var r1, out _);
		for (var r = r0; r <= r1; r++) {
			for (var c = 0; c < cols; c++) {
				var cell = model.CellAt(r, c);
				if (cell.HiddenByMerge) continue;
				model.EnsureCell(r, c).WrapText = wrap;
			}
		}
		afterwrapchange(r0, r1);
		return true;
	}

	/// <summary>选中列整列自动换行。</summary>
	public bool SetWrapCols(bool wrap) {
		if (!editMode || !HasSelection || model == null || rows <= 0) return false;
		endcelledit(commit: true);
		GetSelection(out _, out var c0, out _, out var c1);
		for (var r = 0; r < rows; r++) {
			for (var c = c0; c <= c1; c++) {
				var cell = model.CellAt(r, c);
				if (cell.HiddenByMerge) continue;
				model.EnsureCell(r, c).WrapText = wrap;
			}
		}
		afterwrapchange(0, rows - 1);
		return true;
	}

	public bool ToggleWrapSelection() {
		var st = PeekSelectionStyle();
		return SetWrapSelection(!(st?.WrapText ?? false));
	}

	void afterwrapchange(int r0, int r1) {
		// 换行后按内容重算相关行高
		try {
			autofitrowheights();
		} catch { /* ignore */ }
		rebuildmetrics();
		applytablesize();
		surface.InvalidateVisual();
		notifyedited();
	}

	/// <summary>当前选区左上角样式快照（供工具栏回显）。</summary>
	public SheetCell PeekSelectionStyle() {
		if (!HasSelection || model == null) return null;
		GetSelection(out var r0, out var c0, out _, out _);
		model.ResolveOrigin(ref r0, ref c0);
		var c = model.CellAt(r0, c0);
		return ReferenceEquals(c, SheetCell.SharedEmpty) ? SheetCell.Empty() : c.CloneFull();
	}

	void notifyedited() {
		try { ModelEdited?.Invoke(); } catch { /* ignore */ }
	}

	void raiseselection() {
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
		try { ScrollProgressChanged?.Invoke(); } catch { /* ignore */ }
	}

	void begincelledit(int r, int c) {
		if (!editMode || model == null || editBox == null) return;
		if (r < 0 || c < 0 || r >= rows || c >= cols) return;
		model.ResolveOrigin(ref r, ref c);
		var cell = model.CellAt(r, c);
		if (cell.HiddenByMerge) return;
		endcelledit(commit: true);
		editR = r;
		editC = c;
		editBox.Text = cell.Text ?? "";
		editBox.FontSize = Math.Max(9, (cell.FontSizePt > 1 ? cell.FontSizePt : 11) * zoom * 96.0 / 72.0);
		editBox.FontWeight = cell.Bold ? FontWeights.Bold : FontWeights.Normal;
		editBox.FontStyle = cell.Italic ? FontStyles.Italic : FontStyles.Normal;
		placeeditbox();
		editBox.Visibility = Visibility.Visible;
		editBox.Focus();
		editBox.SelectAll();
	}

	void placeeditbox() {
		if (editBox == null || editR < 0 || editC < 0) return;
		if (editR >= rows || editC >= cols) return;
		var cell = model.CellAt(editR, editC);
		var x = ROW_HDR_W + colX[editC];
		var y = HDR_H + rowY[editR];
		var w = Math.Max(24, spanwidth(editC, Math.Max(1, cell.ColSpan)));
		var h = Math.Max(18, spanheight(editR, Math.Max(1, cell.RowSpan)));
		Canvas.SetLeft(editBox, x);
		Canvas.SetTop(editBox, y);
		editBox.Width = w;
		editBox.Height = h;
	}

	void endcelledit(bool commit) {
		if (editR < 0 || editBox == null) return;
		if (editCommiting) return;
		editCommiting = true;
		try {
			var r = editR;
			var c = editC;
			editR = editC = -1;
			editBox.Visibility = Visibility.Collapsed;
			if (commit && editMode && model != null) {
				var text = editBox.Text ?? "";
				var cell = model.EnsureCell(r, c);
				if (!string.Equals(cell.Text ?? "", text, StringComparison.Ordinal)) {
					cell.Text = text;
					notifyedited();
				}
			}
			surface.InvalidateVisual();
			try { Focus(); } catch { /* ignore */ }
		} finally {
			editCommiting = false;
		}
	}

	void oneditkeydown(object sender, KeyEventArgs e) {
		if (e.Key == Key.Escape) {
			endcelledit(commit: false);
			e.Handled = true;
			return;
		}
		// Shift+Enter：单元格内换行
		if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
			var tb = editBox;
			if (tb != null) {
				var i = tb.SelectionStart;
				var len = tb.SelectionLength;
				var t = tb.Text ?? "";
				if (len > 0)
					t = t.Remove(i, len);
				if (i < 0) i = 0;
				if (i > t.Length) i = t.Length;
				tb.Text = t.Insert(i, "\n");
				tb.CaretIndex = i + 1;
				// 换行后略增高编辑框
				try {
					if (tb.Height < 120) tb.Height = Math.Min(120, tb.Height + 18);
				} catch { /* ignore */ }
			}
			e.Handled = true;
			return;
		}
		// Enter：提交并下移
		if (e.Key == Key.Enter) {
			endcelledit(commit: true);
			moveselection(1, 0, wrap: false, extend: false);
			e.Handled = true;
			return;
		}
		// Tab：提交并右移；Shift+Tab 左移
		if (e.Key == Key.Tab) {
			endcelledit(commit: true);
			moveselection(0, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1, wrap: true, extend: false);
			e.Handled = true;
			return;
		}
		// 左/右键：优先在编辑框内移动光标（不拦，交给 TextBox）
		// 上/下：多行时也优先光标；单行时提交并移格
		if (e.Key == Key.Left || e.Key == Key.Right)
			return;
		if (e.Key == Key.Up || e.Key == Key.Down) {
			var tb = editBox;
			var multi = tb != null && (tb.Text?.IndexOf('\n') >= 0 || tb.LineCount > 1);
			if (multi) return; // 交给 TextBox 移行
			endcelledit(commit: true);
			moveselection(e.Key == Key.Up ? -1 : 1, 0, wrap: false, extend: false);
			e.Handled = true;
		}
	}

	/// <summary>是否正在单元格内联编辑。</summary>
	public bool IsEditingCell => editR >= 0 && editBox != null
		&& editBox.Visibility == Visibility.Visible;

	public bool TryCopySelection() {
		if (!HasSelection) return false;
		var r0 = Math.Min(selR0, selR1);
		var r1 = Math.Max(selR0, selR1);
		var c0 = Math.Min(selC0, selC1);
		var c1 = Math.Max(selC0, selC1);
		var sb = new StringBuilder();
		for (var r = r0; r <= r1; r++) {
			if (r > r0) sb.Append("\r\n");
			for (var c = c0; c <= c1; c++) {
				if (c > c0) sb.Append('\t');
				var sc = cellobj(r, c);
				if (sc.HiddenByMerge) sb.Append("");
				else sb.Append(sc.Text ?? "");
			}
		}
		var s = sb.ToString();
		if (s.Length == 0) return false;
		try {
			Clipboard.SetDataObject(s, true);
			return true;
		} catch {
			return false;
		}
	}

	public FindResult Find(string text, bool forward, bool ignoreCase = true, bool restart = false, bool fromView = false) {
		if (string.IsNullOrEmpty(text) || rows == 0) return FindResult.Miss();
		var needRebuild = restart
			|| findHits.Count == 0
			|| !string.Equals(findQuery, text, StringComparison.Ordinal)
			|| findIgnoreCase != ignoreCase;
		if (needRebuild) {
			findHits.Clear();
			findQuery = text;
			findIgnoreCase = ignoreCase;
			findIndex = -1;
			var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			for (var r = 0; r < rows; r++) {
				for (var c = 0; c < cols; c++) {
					var sc = cellobj(r, c);
					if (sc.HiddenByMerge) continue;
					if ((sc.Text ?? "").IndexOf(text, cmp) >= 0)
						findHits.Add((r, c));
				}
			}
		}
		if (findHits.Count == 0) {
			surface.InvalidateVisual();
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
		var hit = findHits[findIndex];
		// 不抢键盘焦点，便于工具栏查找框连续 Enter
		selectcell(hit.R, hit.C, takeFocus: false);
		surface.InvalidateVisual();
		return FindResult.Hit(findIndex + 1, findHits.Count);
	}

	/// <summary>
	/// 从当前视口起取命中。afterCurrent：当前命中仍在视口及以下时取「下一个」，
	/// 若已滚离当前命中则从新视口首个起。
	/// </summary>
	int pickfindfromview(bool forward, bool afterCurrent) {
		if (findHits.Count == 0) return -1;
		var r0 = 0;
		var c0 = 0;
		try {
			r0 = findrow(Math.Max(0, originY));
			c0 = findcol(Math.Max(0, originX));
		} catch { /* ignore */ }
		if (r0 < 0) r0 = 0;
		if (c0 < 0) c0 = 0;

		// 当前命中是否仍在视口起点及之后（未滚到它上面去）
		var curStillInOrBelow = false;
		if (afterCurrent && findIndex >= 0 && findIndex < findHits.Count) {
			var cur = findHits[findIndex];
			curStillInOrBelow = cur.R > r0 || (cur.R == r0 && cur.C >= c0);
		}

		if (forward) {
			if (curStillInOrBelow)
				return (findIndex + 1) % findHits.Count;
			for (var i = 0; i < findHits.Count; i++) {
				var h = findHits[i];
				if (h.R > r0 || (h.R == r0 && h.C >= c0)) return i;
			}
			return 0;
		}
		if (curStillInOrBelow)
			return (findIndex - 1 + findHits.Count) % findHits.Count;
		for (var i = findHits.Count - 1; i >= 0; i--) {
			var h = findHits[i];
			if (h.R < r0 || (h.R == r0 && h.C <= c0)) return i;
		}
		return findHits.Count - 1;
	}

	public void ClearFind() {
		if (findHits.Count == 0 && string.IsNullOrEmpty(findQuery)) return;
		findHits.Clear();
		findQuery = null;
		findIgnoreCase = true;
		findIndex = -1;
		try { surface.InvalidateVisual(); } catch { /* ignore */ }
	}

	/// <summary>
	/// 将自绘层钉在当前滚动偏移，尺寸=视口。
	/// 表坐标 (x,y) 绘制到 surface 局部坐标 (x-originX, y-originY)。
	/// </summary>
	/// <summary>窗口/控件尺寸变化（含最大化）：轻量钉视口，避免长表重写 outer 触发布局风暴。</summary>
	void onviewportresized() {
		if (scroll == null || surface == null) return;
		// 短表：outer 可能需随视口撑开；长表：只改 surface 尺寸
		if (totalBodyH < EXPAND_VIEWPORT_MAX_BODY_H)
			applytablesize();
		else
			pinviewport();
		surface.InvalidateVisual();
	}

	void pinviewport() {
		if (scroll == null || surface == null || outer == null) return;
		var ho = scroll.HorizontalOffset;
		var vo = scroll.VerticalOffset;
		var vw = scroll.ViewportWidth;
		var vh = scroll.ViewportHeight;
		if (double.IsNaN(vw) || vw < 2) vw = scroll.ActualWidth;
		if (double.IsNaN(vh) || vh < 2) vh = scroll.ActualHeight;
		if (double.IsNaN(vw) || vw < 2) vw = ActualWidth;
		if (double.IsNaN(vh) || vh < 2) vh = ActualHeight;
		if (double.IsNaN(vw) || vw < 2) vw = 800;
		if (double.IsNaN(vh) || vh < 2) vh = 600;
		originX = ho;
		originY = vo;
		viewX = ho;
		viewY = vo;
		viewW = vw;
		viewH = vh;
		Canvas.SetLeft(surface, ho);
		Canvas.SetTop(surface, vo);
		// 始终写入有限尺寸，避免 Width=NaN 时 OnRender 画不出任何内容
		if (double.IsNaN(surface.Width) || Math.Abs(surface.Width - vw) > 0.5)
			surface.Width = vw;
		if (double.IsNaN(surface.Height) || Math.Abs(surface.Height - vh) > 0.5)
			surface.Height = vh;
	}

	void applytablesize() {
		var w = Math.Max(ROW_HDR_W + 1, ROW_HDR_W + totalBodyW + 1);
		var h = Math.Max(HDR_H + 1, HDR_H + totalBodyH + 1);
		// 仅短表用视口撑 outer。长表最大化时若 outer 宽随视口变，会触发「超高 Canvas」整表布局 → 卡顿约 1s。
		// 视口铺满由 pinviewport(surface=视口大小) 负责。
		if (scroll != null && totalBodyH < EXPAND_VIEWPORT_MAX_BODY_H) {
			var svw = scroll.ViewportWidth;
			var svh = scroll.ViewportHeight;
			if (!double.IsNaN(svw) && svw > 2) w = Math.Max(w, svw);
			if (!double.IsNaN(svh) && svh > 2) h = Math.Max(h, svh);
			else if (!double.IsNaN(scroll.ActualHeight) && scroll.ActualHeight > 2)
				h = Math.Max(h, scroll.ActualHeight);
			if (!double.IsNaN(scroll.ActualWidth) && scroll.ActualWidth > 2)
				w = Math.Max(w, scroll.ActualWidth);
		}
		if (outer != null) {
			// 首次 outer.Width/Height 为 NaN：Math.Abs(NaN-w)>0.5 为 false，会永远不写入 → 空白表
			if (double.IsNaN(outer.Width) || Math.Abs(outer.Width - w) > 0.5)
				outer.Width = w;
			if (double.IsNaN(outer.Height) || Math.Abs(outer.Height - h) > 0.5)
				outer.Height = h;
		}
		pinviewport();
	}

	void rebuildmetrics() {
		if (cols <= 0) {
			colW = Array.Empty<double>();
			colX = new[] { 0.0 };
			rowH = Array.Empty<double>();
			rowY = new[] { 0.0 };
			totalBodyW = totalBodyH = 0;
			return;
		}
		var srcW = model.ColWidths;
		// 列数未变则复用数组，拖列宽时减少 GC
		if (colW == null || colW.Length != cols) colW = new double[cols];
		if (colX == null || colX.Length != cols + 1) colX = new double[cols + 1];
		colX[0] = 0;
		for (var c = 0; c < cols; c++) {
			var baseW = (srcW != null && c < srcW.Length && srcW[c] > 0) ? srcW[c] : DEF_COL_W;
			if (srcW != null && c < srcW.Length && srcW[c] <= 0)
				baseW = 0;
			colW[c] = Math.Max(0, baseW * zoom);
			if (colW[c] > 0 && colW[c] < MIN_COL_W * zoom)
				colW[c] = MIN_COL_W * zoom;
			colX[c + 1] = colX[c] + colW[c];
		}
		totalBodyW = colX[cols];

		if (rows <= 0) {
			rowH = Array.Empty<double>();
			rowY = new[] { 0.0 };
			totalBodyH = 0;
			return;
		}
		var srcH = model.RowHeights;
		if (rowH == null || rowH.Length != rows) rowH = new double[rows];
		if (rowY == null || rowY.Length != rows + 1) rowY = new double[rows + 1];
		rowY[0] = 0;
		for (var r = 0; r < rows; r++) {
			var baseH = (srcH != null && r < srcH.Length && srcH[r] > 0) ? srcH[r] : DEF_ROW_H;
			if (srcH != null && r < srcH.Length && srcH[r] <= 0)
				baseH = 0;
			rowH[r] = Math.Max(0, baseH * zoom);
			if (rowH[r] > 0 && rowH[r] < MIN_ROW_H * zoom)
				rowH[r] = MIN_ROW_H * zoom;
			rowY[r + 1] = rowY[r] + rowH[r];
		}
		totalBodyH = rowY[rows];
	}

	/// <summary>仅更新一列显示宽与后续 colX（拖列宽热路径，避免整表 rebuild）。</summary>
	void patchcolmetrics(int col, double baseW) {
		if (col < 0 || col >= cols || colW == null || colX == null || colW.Length != cols) {
			rebuildmetrics();
			return;
		}
		var z = zoom > 0.01 ? zoom : 1.0;
		var nw = Math.Max(0, baseW * z);
		if (nw > 0 && nw < MIN_COL_W * z) nw = MIN_COL_W * z;
		var delta = nw - colW[col];
		if (Math.Abs(delta) < 0.001) return;
		colW[col] = nw;
		for (var c = col + 1; c <= cols; c++)
			colX[c] += delta;
		totalBodyW = colX[cols];
	}

	/// <summary>仅更新一行显示高与后续 rowY。</summary>
	void patchrowmetrics(int row, double baseH) {
		if (row < 0 || row >= rows || rowH == null || rowY == null || rowH.Length != rows) {
			rebuildmetrics();
			return;
		}
		var z = zoom > 0.01 ? zoom : 1.0;
		var nh = Math.Max(0, baseH * z);
		if (nh > 0 && nh < MIN_ROW_H * z) nh = MIN_ROW_H * z;
		var delta = nh - rowH[row];
		if (Math.Abs(delta) < 0.001) return;
		rowH[row] = nh;
		for (var r = row + 1; r <= rows; r++)
			rowY[r] += delta;
		totalBodyH = rowY[rows];
	}

	void autofitrowheights() {
		// 打开表时全表扫一遍；交互路径请用 autofitrowheightsforcol
		autofitrowheightsforcol(-1);
	}

	/// <summary>
	/// 按换行内容抬高行高。col&gt;=0 时只处理「宽度依赖该列」的行（改列宽/双击自适应热路径）；
	/// col&lt;0 时全表（打开时）。交互路径一律非 precise，避免 FormattedText 卡顿数秒。
	/// </summary>
	void autofitrowheightsforcol(int col) {
		if (rows <= 0 || cols <= 0 || model == null) return;
		if (rows >= AUTOFIT_SKIP_ROWS) return;
		ensurecolwidths();
		ensurerowheights();

		var onlyCol = col >= 0 && col < cols;
		// 打开表：少量候选可用 precise；改列宽交互永远走快速估算
		var precise = false;
		if (!onlyCol) {
			var candidates = 0;
			for (var r = 0; r < rows && candidates <= AUTOFIT_FULL_CELL_MAX; r++) {
				if (r >= model.RowHeights.Length || model.RowHeights[r] <= 0) continue;
				var rowCells0 = model.Cells != null && r < model.Cells.Length ? model.Cells[r] : null;
				if (rowCells0 == null) continue;
				for (var c = 0; c < cols && c < rowCells0.Length; c++) {
					var sc0 = rowCells0[c];
					if (sc0 == null || sc0.HiddenByMerge || string.IsNullOrEmpty(sc0.Text)) continue;
					if (sc0.WrapText || sc0.Text.IndexOf('\n') >= 0 || sc0.Text.IndexOf('\r') >= 0)
						candidates++;
				}
			}
			precise = candidates > 0 && candidates <= AUTOFIT_FULL_CELL_MAX;
		}

		var changed = false;
		for (var r = 0; r < rows; r++) {
			if (r >= model.RowHeights.Length || model.RowHeights[r] <= 0) continue;
			var rowCells = model.Cells != null && r < model.Cells.Length ? model.Cells[r] : null;
			if (rowCells == null) continue;

			if (onlyCol) {
				// 该行是否有换行单元格的列跨度覆盖 col
				var affected = false;
				for (var c = 0; c < cols && c < rowCells.Length; c++) {
					var sc = rowCells[c];
					if (sc == null || sc.HiddenByMerge || string.IsNullOrEmpty(sc.Text)) continue;
					var hasNl = sc.Text.IndexOf('\n') >= 0 || sc.Text.IndexOf('\r') >= 0;
					if (!sc.WrapText && !hasNl) continue;
					var span = Math.Max(1, sc.ColSpan);
					var m = model.FindMerge(r, c);
					if (m != null && m.IsOrigin(r, c)) span = m.C1 - m.C0 + 1;
					if (col >= c && col < c + span) { affected = true; break; }
				}
				if (!affected) continue;
			}

			var need = 0.0;
			for (var c = 0; c < cols && c < rowCells.Length; c++) {
				var sc = rowCells[c];
				if (sc == null || sc.HiddenByMerge || sc.RowSpan > 1) continue;
				var h = measurecellbase(sc, c, Math.Max(1, sc.ColSpan), precise);
				if (h > need) need = h;
			}
			if (need > model.RowHeights[r] + 0.5) {
				model.RowHeights[r] = need;
				changed = true;
			}
		}
		if (changed) rebuildmetrics();
	}

	double measurecellbase(SheetCell sc, int c0, int colSpan, bool precise) {
		if (sc == null) return 0;
		var text = sc.Text ?? "";
		if (text.Length == 0) return 0;
		var hasNl = text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0;
		if (!sc.WrapText && !hasNl) return 0;
		if (colSpan < 1) colSpan = 1;
		var w = 0.0;
		var srcW = model.ColWidths;
		for (var c = c0; c < c0 + colSpan && c < cols; c++) {
			if (srcW != null && c < srcW.Length && srcW[c] > 0) w += srcW[c];
			else if (srcW == null || c >= srcW.Length) w += DEF_COL_W;
		}
		if (w < 4) return 0;
		var fs = sc.FontSizePt > 0 ? sc.FontSizePt * 96.0 / 72.0 : 11.0 * 96.0 / 72.0;
		if (fs < 8) fs = 8;
		const double padX = 8, padY = 4;
		var norm = text.Replace("\r\n", "\n").Replace('\r', '\n');
		if (!precise) {
			var lines = 1;
			foreach (var ch in norm) if (ch == '\n') lines++;
			if (sc.WrapText) {
				var avg = Math.Max(1, fs * 0.55);
				var cpl = Math.Max(1, (int)((w - padX) / avg));
				var plainLen = 0;
				for (var i = 0; i < norm.Length; i++) if (norm[i] != '\n') plainLen++;
				lines = Math.Max(lines, (plainLen + cpl - 1) / cpl);
			}
			return Math.Min(RESIZE_MAX_ROW, lines * fs * 1.25 + padY);
		}
		try {
			var ft = new FormattedText(norm, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
				TypefaceUi, fs, Brushes.Black, 1.0);
			if (sc.WrapText || hasNl) ft.MaxTextWidth = Math.Max(1, w - padX);
			return Math.Min(RESIZE_MAX_ROW, Math.Max(fs * 1.25 + padY, ft.Height + padY));
		} catch {
			return measurecellbase(sc, c0, colSpan, false);
		}
	}

	void ensurecolwidths() {
		if (cols <= 0) return;
		var src = model.ColWidths;
		if (src != null && src.Length >= cols) return;
		var n = new double[cols];
		for (var c = 0; c < cols; c++)
			n[c] = src != null && c < src.Length ? src[c] : DEF_COL_W;
		model.ColWidths = n;
	}

	void ensurerowheights() {
		if (rows <= 0) return;
		var src = model.RowHeights;
		if (src != null && src.Length >= rows) return;
		var n = new double[rows];
		for (var r = 0; r < rows; r++)
			n[r] = src != null && r < src.Length ? src[r] : DEF_ROW_H;
		model.RowHeights = n;
	}

	SheetCell cellobj(int r, int c) {
		if (model == null) return SheetCell.SharedEmpty;
		if (model.Dense && model.Cells != null && (uint)r < (uint)model.Cells.Length) {
			var row = model.Cells[r];
			if (row != null && (uint)c < (uint)row.Length)
				return row[c] ?? SheetCell.SharedEmpty;
		}
		return model.CellAt(r, c);
	}

	double spanwidth(int c0, int colSpan) {
		if (colSpan < 1) colSpan = 1;
		var c1 = Math.Min(cols, c0 + colSpan);
		if (c0 < 0 || c0 >= cols) return 0;
		return colX[c1] - colX[c0];
	}

	double spanheight(int r0, int rowSpan) {
		if (rowSpan < 1) rowSpan = 1;
		var r1 = Math.Min(rows, r0 + rowSpan);
		if (r0 < 0 || r0 >= rows) return 0;
		return rowY[r1] - rowY[r0];
	}

	// ---------- 自绘 ----------

	/// <summary>由 SheetSurface.OnRender 调用。</summary>
	internal void paint(DrawingContext dc) {
		try {
			paintcore(dc);
		} catch (Exception ex) {
			DocViewLog.Error("VirtualSheetGrid.paint", ex);
			try {
				dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 400, 200));
				var ft = new FormattedText("绘制失败: " + ex.Message,
					CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
					TypefaceUi, 12, Brushes.Red, 1.0);
				dc.DrawText(ft, new WpfPoint(12, 12));
			} catch { /* ignore */ }
		}
	}

	void schedulesettle() {
		if (settleTimer == null) {
			settleTimer = new DispatcherTimer(DispatcherPriority.Background) {
				Interval = TimeSpan.FromMilliseconds(SCROLL_SETTLE_MS),
			};
			settleTimer.Tick += (_, _) => {
				try { settleTimer.Stop(); } catch { /* ignore */ }
				if (!scrubbing) return;
				scrubbing = false;
				try { surface.InvalidateVisual(); } catch { /* ignore */ }
				// 停稳后补一次进度
				lastProgressTick = Environment.TickCount;
				try { ScrollProgressChanged?.Invoke(); } catch { /* ignore */ }
			};
		}
		settleTimer.Stop();
		settleTimer.Start();
	}

	void paintcore(DrawingContext dc) {
		// 优先 RenderSize（实际布局），Width 可能尚未参与布局仍为旧值/NaN
		var sw = surface.RenderSize.Width;
		var sh = surface.RenderSize.Height;
		if (double.IsNaN(sw) || sw < 2) sw = surface.Width;
		if (double.IsNaN(sh) || sh < 2) sh = surface.Height;
		if (double.IsNaN(sw) || sw < 2) sw = viewW > 2 ? viewW : 800;
		if (double.IsNaN(sh) || sh < 2) sh = viewH > 2 ? viewH : 600;
		// 与 pinviewport 对齐，供 cScroll/rScroll 计算
		if (viewW < 2) viewW = sw;
		if (viewH < 2) viewH = sh;
		dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, sw, sh));

		if (cols <= 0) {
			var tip = new FormattedText("（空表）", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
				TypefaceUi, 14, brushHdrText, 1.0);
			dc.DrawText(tip, new WpfPoint(16, 16));
			return;
		}

		var fw = freezew();
		var fh = freezeh();
		// 可见列：冻结列全部 + 滚动区列（不再 -1/+1 扩圈，减绘制量）
		var bodyX0 = Math.Max(0, originX + fw);
		var bodyX1 = Math.Max(0, originX + Math.Max(0, viewW - ROW_HDR_W));
		var bodyY0 = Math.Max(0, originY + fh);
		var bodyY1 = Math.Max(0, originY + Math.Max(0, viewH - HDR_H));
		var cScroll0 = freezeCols < cols ? clampi(findcol(bodyX0), freezeCols, cols - 1) : cols;
		var cScroll1 = freezeCols < cols ? clampi(findcol(bodyX1), freezeCols, cols - 1) : freezeCols - 1;
		var rScroll0 = freezeRows < rows ? clampi(findrow(bodyY0), freezeRows, rows - 1) : rows;
		var rScroll1 = freezeRows < rows ? clampi(findrow(bodyY1), freezeRows, rows - 1) : freezeRows - 1;

		var fontSize = Math.Max(9, 12 * zoom);
		var hdrFs = fontSize;
		// 视口已裁到可见行列（约几十格），始终画文字；scrub 只作滚动节流，不再藏字
		const bool drawText = true;

		// 1) 滚动区（非冻结）：裁到冻结线右下，避免与冻结带叠画
		var scrollClip = new Rect(ROW_HDR_W + fw, HDR_H + fh,
			Math.Max(0, sw - ROW_HDR_W - fw), Math.Max(0, sh - HDR_H - fh));
		if (scrollClip.Width > 0.5 && scrollClip.Height > 0.5 && rScroll0 <= rScroll1 && cScroll0 <= cScroll1) {
			dc.PushClip(new RectangleGeometry(scrollClip));
			paintcellsregion(dc, rScroll0, rScroll1, cScroll0, cScroll1, fontSize, drawText);
			paintgrid(dc, rScroll0, rScroll1, cScroll0, cScroll1);
			dc.Pop();
		}

		// 2) 冻结列 × 滚动行（左侧条，Y 裁到冻结行下）
		if (freezeCols > 0 && rScroll0 <= rScroll1) {
			var clip = new Rect(ROW_HDR_W, HDR_H + fh, Math.Max(0, fw), Math.Max(0, sh - HDR_H - fh));
			if (clip.Width > 0.5 && clip.Height > 0.5) {
				dc.PushClip(new RectangleGeometry(clip));
				paintcellsregion(dc, rScroll0, rScroll1, 0, freezeCols - 1, fontSize, drawText);
				paintgrid(dc, rScroll0, rScroll1, 0, freezeCols - 1);
				dc.Pop();
			}
		}

		// 3) 冻结行 × 滚动列（顶条，X 裁到冻结列右）
		if (freezeRows > 0 && cScroll0 <= cScroll1) {
			var clip = new Rect(ROW_HDR_W + fw, HDR_H, Math.Max(0, sw - ROW_HDR_W - fw), Math.Max(0, fh));
			if (clip.Width > 0.5 && clip.Height > 0.5) {
				dc.PushClip(new RectangleGeometry(clip));
				paintcellsregion(dc, 0, freezeRows - 1, cScroll0, cScroll1, fontSize, drawText);
				paintgrid(dc, 0, freezeRows - 1, cScroll0, cScroll1);
				dc.Pop();
			}
		}

		// 4) 冻结角
		if (freezeRows > 0 && freezeCols > 0) {
			var clip = new Rect(ROW_HDR_W, HDR_H, Math.Max(0, fw), Math.Max(0, fh));
			if (clip.Width > 0.5 && clip.Height > 0.5) {
				dc.PushClip(new RectangleGeometry(clip));
				paintcellsregion(dc, 0, freezeRows - 1, 0, freezeCols - 1, fontSize, drawText);
				paintgrid(dc, 0, freezeRows - 1, 0, freezeCols - 1);
				dc.Pop();
			}
		}

		// 5) 行号：先滚动区（裁到冻结下），再冻结行号盖在最上
		if (rows > 0) {
			var rhClip = new Rect(0, HDR_H + fh, ROW_HDR_W, Math.Max(0, sh - HDR_H - fh));
			if (rhClip.Height > 0.5) {
				dc.PushClip(new RectangleGeometry(rhClip));
				for (var r = rScroll0; r <= rScroll1 && r < rows; r++)
					paintrowhdr(dc, r, hdrFs);
				dc.Pop();
			}
			for (var r = 0; r < freezeRows; r++)
				paintrowhdr(dc, r, hdrFs);
		}

		// 6) 列头（固定顶部 A/B/C，仅列名，筛选在表头固定行）
		for (var c = 0; c < freezeCols && c < cols; c++)
			paintcolhdr(dc, c, hdrFs);
		for (var c = cScroll0; c <= cScroll1 && c < cols; c++)
			paintcolhdr(dc, c, hdrFs);

		// 6b) 表头固定行上的筛选下拉 / 排序标记（仿 Excel AutoFilter）
		if (filterHdrRow >= 0 && filterR1 > filterHdrRow) {
			for (var c = 0; c < freezeCols && c < cols; c++)
				paintfilterbtn(dc, c);
			for (var c = cScroll0; c <= cScroll1 && c < cols; c++)
				paintfilterbtn(dc, c);
		}

		// 左上角
		dc.DrawRectangle(brushHdr, null, new Rect(0, 0, ROW_HDR_W, HDR_H));
		dc.DrawLine(penGrid, new WpfPoint(ROW_HDR_W, 0), new WpfPoint(ROW_HDR_W, HDR_H));
		dc.DrawLine(penGrid, new WpfPoint(0, HDR_H), new WpfPoint(ROW_HDR_W, HDR_H));

		// 冻结分割线
		if (freezeCols > 0) {
			var x = ROW_HDR_W + fw;
			dc.DrawLine(penSel, new WpfPoint(x, 0), new WpfPoint(x, sh));
		}
		if (freezeRows > 0) {
			var y = HDR_H + fh;
			dc.DrawLine(penSel, new WpfPoint(0, y), new WpfPoint(sw, y));
		}

		// 选区
		if (selR0 >= 0 && selC0 >= 0 && rows > 0 && cols > 0) {
			var sr0 = clampi(Math.Min(selR0, selR1), 0, rows - 1);
			var sr1 = clampi(Math.Max(selR0, selR1), 0, rows - 1);
			var sc0 = clampi(Math.Min(selC0, selC1), 0, cols - 1);
			var sc1 = clampi(Math.Max(selC0, selC1), 0, cols - 1);
			var ox = bodylocalx(sc0);
			var oy = bodylocaly(sr0);
			var ox2 = bodylocalx(sc1) + (sc1 < colW.Length ? colW[sc1] : 0);
			var oy2 = bodylocaly(sr1) + (sr1 < rowH.Length ? rowH[sr1] : 0);
			var ow = ox2 - ox;
			var oh = oy2 - oy;
			if (ow > 0.5 && oh > 0.5)
				dc.DrawRectangle(brushSelFill, penSel, new Rect(ox, oy, ow, oh));
		}
		paintpictures(dc, sw, sh);
	}

	void paintcellsregion(DrawingContext dc, int r0, int r1, int c0, int c1, double fontSize, bool drawText) {
		if (r1 < r0 || c1 < c0 || rows <= 0) return;
		r0 = clampi(r0, 0, rows - 1);
		r1 = clampi(r1, 0, rows - 1);
		c0 = clampi(c0, 0, cols - 1);
		c1 = clampi(c1, 0, cols - 1);
		for (var r = r0; r <= r1; r++) {
			if (r >= rowH.Length || rowH[r] <= 0.5) continue;
			if (isrowhidden(r)) continue;
			for (var c = c0; c <= c1; c++) {
				if (c >= colW.Length || colW[c] <= 0.5) continue;
				var sc = cellobj(r, c);
				// 合并从属格：若原点不在本区，仍由原点画；此处跳过
				if (sc.HiddenByMerge) continue;
				// 优先用 Merges 表决定跨度（避免 RowSpan/ColSpan 未同步）
				var cs = Math.Max(1, sc.ColSpan);
				var rs = Math.Max(1, sc.RowSpan);
				var m = model?.FindMerge(r, c);
				if (m != null && m.IsOrigin(r, c)) {
					cs = m.C1 - m.C0 + 1;
					rs = m.R1 - m.R0 + 1;
				} else if (m != null && !m.IsOrigin(r, c)) {
					continue;
				}
				var tw = spanwidth(c, cs);
				var th = spanheight(r, rs);
				if (tw <= 0.5 || th <= 0.5) continue;
				if (drawText)
					paintcell(dc, sc, r, c, bodylocalx(c), bodylocaly(r), tw, th, fontSize);
				else
					paintcellbg(dc, sc, bodylocalx(c), bodylocaly(r), tw, th);
			}
		}
		// 原点在区外、但合并块与本区相交：补画（视口只露出合并下半时）
		if (model?.Merges != null) {
			foreach (var m in model.Merges) {
				if (m == null) continue;
				if (m.R1 < r0 || m.R0 > r1 || m.C1 < c0 || m.C0 > c1) continue;
				if (m.R0 >= r0 && m.R0 <= r1 && m.C0 >= c0 && m.C0 <= c1) continue; // 原点已画
				if (m.R0 < 0 || m.C0 < 0 || m.R0 >= rows || m.C0 >= cols) continue;
				var sc = cellobj(m.R0, m.C0);
				var cs = m.C1 - m.C0 + 1;
				var rs = m.R1 - m.R0 + 1;
				var tw = spanwidth(m.C0, cs);
				var th = spanheight(m.R0, rs);
				if (tw <= 0.5 || th <= 0.5) continue;
				if (drawText)
					paintcell(dc, sc, m.R0, m.C0, bodylocalx(m.C0), bodylocaly(m.R0), tw, th, fontSize);
				else
					paintcellbg(dc, sc, bodylocalx(m.C0), bodylocaly(m.R0), tw, th);
			}
		}
	}

	/// <summary>快滚：只铺底色，不排版文字。</summary>
	void paintcellbg(DrawingContext dc, SheetCell sc, double x, double y, double w, double h) {
		if (sc == null) sc = SheetCell.SharedEmpty;
		Brush bg = sc.BackColor.HasValue ? solid(sc.BackColor.Value) : Brushes.White;
		dc.DrawRectangle(bg, null, new Rect(x, y, w, h));
	}

	/// <summary>
	/// 网格线：按格绘制，合并区内不画内部分隔线（只画合并块外框）。
	/// </summary>
	void paintgrid(DrawingContext dc, int r0, int r1, int c0, int c1) {
		if (r1 < r0 || c1 < c0 || rows <= 0) return;
		r0 = clampi(r0, 0, rows - 1);
		r1 = clampi(r1, 0, rows - 1);
		c0 = clampi(c0, 0, cols - 1);
		c1 = clampi(c1, 0, cols - 1);
		for (var r = r0; r <= r1; r++) {
			if (isrowhidden(r) || r >= rowH.Length || rowH[r] <= 0.5) continue;
			for (var c = c0; c <= c1; c++) {
				if (c >= colW.Length || colW[c] <= 0.5) continue;
				var sc = cellobj(r, c);
				if (sc.HiddenByMerge) continue;
				var m = model?.FindMerge(r, c);
				if (m != null && !m.IsOrigin(r, c)) continue;
				var cs = m != null ? m.C1 - m.C0 + 1 : Math.Max(1, sc.ColSpan);
				var rs = m != null ? m.R1 - m.R0 + 1 : Math.Max(1, sc.RowSpan);
				// 合并原点：只画整块外框；普通格：右+下边，区域左边/顶边补左+上
				var x = bodylocalx(c);
				var y = bodylocaly(r);
				var tw = spanwidth(c, cs);
				var th = spanheight(r, rs);
				if (tw <= 0.5 || th <= 0.5) continue;
				if (cs > 1 || rs > 1) {
					dc.DrawRectangle(null, penGrid, new Rect(x, y, tw, th));
					continue;
				}
				// 右边、底边
				dc.DrawLine(penGrid, new WpfPoint(x + tw, y), new WpfPoint(x + tw, y + th));
				dc.DrawLine(penGrid, new WpfPoint(x, y + th), new WpfPoint(x + tw, y + th));
				if (c == c0)
					dc.DrawLine(penGrid, new WpfPoint(x, y), new WpfPoint(x, y + th));
				if (r == r0)
					dc.DrawLine(penGrid, new WpfPoint(x, y), new WpfPoint(x + tw, y));
			}
		}
		// 原点在区外的合并块：补外框
		if (model?.Merges != null) {
			foreach (var m in model.Merges) {
				if (m == null) continue;
				if (m.R1 < r0 || m.R0 > r1 || m.C1 < c0 || m.C0 > c1) continue;
				if (m.R0 >= r0 && m.R0 <= r1 && m.C0 >= c0 && m.C0 <= c1) continue;
				if (m.R0 < 0 || m.C0 < 0 || m.R0 >= rows || m.C0 >= cols) continue;
				var cs = m.C1 - m.C0 + 1;
				var rs = m.R1 - m.R0 + 1;
				var x = bodylocalx(m.C0);
				var y = bodylocaly(m.R0);
				var tw = spanwidth(m.C0, cs);
				var th = spanheight(m.R0, rs);
				if (tw > 0.5 && th > 0.5)
					dc.DrawRectangle(null, penGrid, new Rect(x, y, tw, th));
			}
		}
	}

	void paintrowhdr(DrawingContext dc, int r, double hdrFs) {
		if (r < 0 || r >= rows || r >= rowH.Length || rowH[r] <= 0.5) return;
		if (isrowhidden(r)) return;
		var y = bodylocaly(r);
		var h = rowH[r];
		var bg = isrowinselection(r) ? brushHdrSel : brushHdr;
		dc.DrawRectangle(bg, null, new Rect(0, y, ROW_HDR_W, h));
		dc.DrawLine(penGrid, new WpfPoint(0, y + h), new WpfPoint(ROW_HDR_W, y + h));
		dc.DrawLine(penGrid, new WpfPoint(ROW_HDR_W, y), new WpfPoint(ROW_HDR_W, y + h));
		var ppd = pixelsPerDip > 0.5 ? pixelsPerDip : 1.0;
		var ft = new FormattedText(
			(r + 1).ToString(CultureInfo.InvariantCulture),
			CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
			TypefaceUi, hdrFs * 0.9, brushHdrText, ppd);
		dc.DrawText(ft, new WpfPoint((ROW_HDR_W - ft.Width) / 2, y + (h - ft.Height) / 2));
	}

	void paintcolhdr(DrawingContext dc, int c, double hdrFs) {
		if (c < 0 || c >= cols || c >= colW.Length || colW[c] <= 0.5) return;
		var x = bodylocalx(c);
		var w = colW[c];
		var bg = iscolinselection(c) ? brushHdrSel : brushHdr;
		dc.DrawRectangle(bg, null, new Rect(x, 0, w, HDR_H));
		dc.DrawLine(penGrid, new WpfPoint(x + w, 0), new WpfPoint(x + w, HDR_H));
		dc.DrawLine(penGrid, new WpfPoint(x, HDR_H), new WpfPoint(x + w, HDR_H));
		var name = colname(c);
		var ppd = pixelsPerDip > 0.5 ? pixelsPerDip : 1.0;
		var ft = new FormattedText(
			name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
			TypefaceUiBold, hdrFs, brushHdrText, ppd);
		ft.MaxTextWidth = Math.Max(4, w - 6);
		ft.Trimming = TextTrimming.CharacterEllipsis;
		dc.DrawText(ft, new WpfPoint(x + 4, (HDR_H - ft.Height) / 2));
	}

	/// <summary>在筛选表头固定行单元格右缘画下拉按钮 / 排序标记（仿 Excel AutoFilter）。</summary>
	void paintfilterbtn(DrawingContext dc, int c) {
		if (!canfiltercol(c) || c < 0 || c >= cols || c >= colW.Length || colW[c] < 12) return;
		if (filterHdrRow < 0 || filterHdrRow >= rows || filterHdrRow >= rowH.Length) return;
		var x = bodylocalx(c);
		var y = bodylocaly(filterHdrRow);
		var w = colW[c];
		var h = rowH[filterHdrRow];
		if (w < 12 || h < 6) return;
		var active = colFilters.ContainsKey(c) && colFilters[c] != null && colFilters[c].IsActive;
		var sorted = sortCol == c;
		// Excel 风格：右侧小按钮（白底 + 边框 + ▼）
		const double btnW = 15;
		var bx0 = x + w - btnW - 1;
		var by0 = y + 1;
		var bh = Math.Max(10, h - 2);
		var bg = active || sorted
			? new SolidColorBrush(MediaColor.FromRgb(0xDE, 0xEB, 0xF7))
			: Brushes.White;
		var border = active || sorted ? brushSelBorder : brushGrid;
		dc.DrawRectangle(bg, new Pen(border, 1), new Rect(bx0, by0, btnW, bh));
		// ▼
		var midX = bx0 + btnW / 2;
		var midY = by0 + bh / 2;
		var br = (active || sorted) ? brushSelBorder : brushHdrText;
		var geo = new StreamGeometry();
		using (var ctx = geo.Open()) {
			ctx.BeginFigure(new WpfPoint(midX - 4, midY - 1.5), true, true);
			ctx.LineTo(new WpfPoint(midX + 4, midY - 1.5), true, false);
			ctx.LineTo(new WpfPoint(midX, midY + 3), true, false);
		}
		geo.Freeze();
		dc.DrawGeometry(br, null, geo);
		// 排序小箭头在按钮左侧
		if (sorted) {
			var ax = bx0 - 9;
			var ay = midY;
			var g2 = new StreamGeometry();
			using (var ctx = g2.Open()) {
				if (sortAsc) {
					ctx.BeginFigure(new WpfPoint(ax, ay + 3), true, true);
					ctx.LineTo(new WpfPoint(ax + 6, ay + 3), true, false);
					ctx.LineTo(new WpfPoint(ax + 3, ay - 3), true, false);
				} else {
					ctx.BeginFigure(new WpfPoint(ax, ay - 3), true, true);
					ctx.LineTo(new WpfPoint(ax + 6, ay - 3), true, false);
					ctx.LineTo(new WpfPoint(ax + 3, ay + 3), true, false);
				}
			}
			g2.Freeze();
			dc.DrawGeometry(brushSelBorder, null, g2);
		}
	}

	bool isrowhidden(int r) =>
		r >= 0 && rowHidden != null && r < rowHidden.Length && rowHidden[r];

	void paintcell(DrawingContext dc, SheetCell sc, int row, int col, double x, double y, double w, double h, double baseFont) {
		if (sc == null) sc = SheetCell.SharedEmpty;
		Brush bg = sc.BackColor.HasValue ? solid(sc.BackColor.Value) : Brushes.White;
		dc.DrawRectangle(bg, null, new Rect(x, y, w, h));

		var text = sc.Text;
		if (string.IsNullOrEmpty(text)) return;
		if (w < 3 || h < 3) return;

		var fs = sc.FontSizePt > 0 ? sc.FontSizePt * 96.0 / 72.0 * zoom : baseFont;
		if (fs < 8) fs = 8;
		if (fs > 48) fs = 48;
		// 行高不够时略缩字号，避免 MaxTextHeight 裁成空白
		if (fs > h - 1) fs = Math.Max(8, h - 1);
		var fg = sc.ForeColor.HasValue ? solid(sc.ForeColor.Value) : brushText;
		// 近白前景在白底上看不见
		if (sc.ForeColor.HasValue && isnearwhite(sc.ForeColor.Value) && !sc.BackColor.HasValue)
			fg = brushText;
		var tf = resolvetypeface(sc);

		// 避免每格 Replace；仅在确有换行时规范化
		var norm = text;
		if (text.IndexOf('\r') >= 0)
			norm = text.Replace("\r\n", "\n").Replace('\r', '\n');

		var ppd = pixelsPerDip > 0.5 ? pixelsPerDip : 1.0;
		FormattedText ft;
		try {
			ft = new FormattedText(norm, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
				tf, fs, fg, ppd);
		} catch {
			ft = new FormattedText(norm, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
				TypefaceUi, fs, brushText, ppd);
		}
		// 表头筛选行右侧留给下拉按钮
		var textPadR = (row == filterHdrRow && canfiltercol(col)) ? 18 : 6;
		var hasNl = norm.IndexOf('\n') >= 0;
		var wrap = sc.WrapText || hasNl;
		if (wrap) {
			// 自动换行：限宽多行，高度靠 clip
			ft.MaxTextWidth = Math.Max(1, w - textPadR);
		} else {
			// 不换行：单行 + 省略号
			ft.MaxTextWidth = Math.Max(1, w - textPadR);
			ft.MaxTextHeight = Math.Max(fs * 1.35, fs + 2);
			ft.Trimming = TextTrimming.CharacterEllipsis;
		}

		double tx = x + 3;
		if (sc.Align == TextAlignment.Center)
			tx = x + (w - Math.Min(ft.Width, w - textPadR)) / 2;
		else if (sc.Align == TextAlignment.Right)
			tx = x + w - textPadR - Math.Min(ft.Width, w - textPadR);

		double ty = y + 1;
		if (sc.VAlign == 1)
			ty = y + Math.Max(0, (h - Math.Min(ft.Height, h)) / 2);
		else if (sc.VAlign == 2)
			ty = y + Math.Max(0, h - Math.Min(ft.Height, h));

		dc.PushClip(new RectangleGeometry(new Rect(x, y, w, h)));
		// 查找：屏幕内匹配文字底色（在字下绘制，再叠字）
		if (!string.IsNullOrEmpty(findQuery) && findHits.Count > 0) {
			var q = findQuery;
			var cmp = findIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			if (norm.IndexOf(q, cmp) >= 0) {
				var isCur = findIndex >= 0 && findIndex < findHits.Count
					&& findHits[findIndex].R == row && findHits[findIndex].C == col;
				var hl = isCur ? brushFindCur : brushFindHit;
				var from = 0;
				var qLen = q.Length;
				if (qLen < 1) qLen = 1;
				while (from < norm.Length) {
					var idx = norm.IndexOf(q, from, cmp);
					if (idx < 0) break;
					try {
						var geo = ft.BuildHighlightGeometry(new WpfPoint(tx, ty), idx, q.Length);
						if (geo != null) dc.DrawGeometry(hl, null, geo);
					} catch { /* ignore geometry */ }
					from = idx + qLen;
				}
			}
		}
		dc.DrawText(ft, new WpfPoint(tx, ty));
		dc.Pop();
	}

	static bool isnearwhite(MediaColor c) =>
		c.A < 16 || (c.R > 240 && c.G > 240 && c.B > 240);

	/// <summary>
	/// 解析字体：Excel 字体名（Calibri/宋体）在部分环境 FormattedText 出不了字，
	/// 必须校验 GlyphTypeface，失败则回退到 UI 字体。
	/// </summary>
	static Typeface resolvetypeface(SheetCell sc) {
		if (sc == null || (!sc.Bold && !sc.Italic && string.IsNullOrWhiteSpace(sc.FontName)))
			return TypefaceUi;
		if (sc.Bold && !sc.Italic && string.IsNullOrWhiteSpace(sc.FontName))
			return TypefaceUiBold;

		var style = sc.Italic ? FontStyles.Italic : FontStyles.Normal;
		var weight = sc.Bold ? FontWeights.Bold : FontWeights.Normal;
		var name = (sc.FontName ?? "").Trim();
		// 常见西文字体对中文无字形；统一走 UI 栈更稳（数字/中文都能出）
		if (string.IsNullOrEmpty(name)
			|| name.Equals("Calibri", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("Arial", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("Helvetica", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("Cambria", StringComparison.OrdinalIgnoreCase))
			return sc.Bold ? TypefaceUiBold : TypefaceUi;

		var fam = name + ", Microsoft YaHei UI, Segoe UI, SimSun";
		var key = (sc.Bold ? "B" : "n") + (sc.Italic ? "I" : "n") + "|" + fam;
		if (TypefaceCache.TryGetValue(key, out var cached))
			return cached;

		Typeface tf;
		try {
			tf = new Typeface(new FontFamily(fam), style, weight, FontStretches.Normal);
			// 建不出字形则回退
			if (!tf.TryGetGlyphTypeface(out _))
				tf = sc.Bold ? TypefaceUiBold : TypefaceUi;
		} catch {
			tf = sc.Bold ? TypefaceUiBold : TypefaceUi;
		}
		if (TypefaceCache.Count < 64)
			TypefaceCache[key] = tf;
		return tf;
	}

	// ---------- 交互 ----------

	bool iscolinselection(int c) {
		if (selR0 < 0 || selC0 < 0 || c < 0) return false;
		var c0 = Math.Min(selC0, selC1);
		var c1 = Math.Max(selC0, selC1);
		return c >= c0 && c <= c1;
	}

	bool isrowinselection(int r) {
		if (selR0 < 0 || selC0 < 0 || r < 0) return false;
		var r0 = Math.Min(selR0, selR1);
		var r1 = Math.Max(selR0, selR1);
		return r >= r0 && r <= r1;
	}

	/// <summary>选中整列（可 Shift 连续多列）。</summary>
	void selectcolumns(int c0, int c1, bool setAnchor) {
		if (cols <= 0 || rows <= 0) return;
		c0 = clampi(c0, 0, cols - 1);
		c1 = clampi(c1, 0, cols - 1);
		if (c1 < c0) { var t = c0; c0 = c1; c1 = t; }
		selR0 = 0;
		selR1 = rows - 1;
		selC0 = c0;
		selC1 = c1;
		if (setAnchor) {
			anchorR = 0;
			anchorC = c0;
		}
		activeR = 0;
		activeC = c1;
	}

	/// <summary>选中整行（可 Shift 连续多行）。</summary>
	void selectrows(int r0, int r1, bool setAnchor) {
		if (cols <= 0 || rows <= 0) return;
		r0 = clampi(r0, 0, rows - 1);
		r1 = clampi(r1, 0, rows - 1);
		if (r1 < r0) { var t = r0; r0 = r1; r1 = t; }
		selR0 = r0;
		selR1 = r1;
		selC0 = 0;
		selC1 = cols - 1;
		if (setAnchor) {
			anchorR = r0;
			anchorC = 0;
		}
		activeR = r1;
		activeC = 0;
	}

	void decodepictures() {
		var pics = model?.Pictures;
		if (pics == null || pics.Count == 0) return;
		foreach (var p in pics) {
			try { XlsxPictures.EnsureBitmap(p); } catch { /* ignore */ }
		}
	}

	void initpicmenu() {
		var mview = new MenuItem { Header = "预览图片" };
		mview.Click += (_, _) => {
			if (ctxPic?.Bitmap != null)
				ImageOverlay.Show(ctxPic.Bitmap, string.IsNullOrEmpty(ctxPic.Name) ? "图片预览" : ctxPic.Name);
		};
		var mcopy = new MenuItem { Header = "复制图片" };
		mcopy.Click += (_, _) => {
			if (ctxPic?.Bitmap != null) ImageOverlay.CopyImage(ctxPic.Bitmap);
		};
		var msave = new MenuItem { Header = "图片另存为…" };
		msave.Click += (_, _) => {
			if (ctxPic?.Bitmap != null)
				ImageOverlay.SaveImageAs(Window.GetWindow(this), ctxPic.Bitmap, null);
		};
		var cm = new ContextMenu();
		cm.Items.Add(mview);
		cm.Items.Add(mcopy);
		cm.Items.Add(msave);
		surface.ContextMenuOpening += (_, e) => {
			ctxPic = hitpicture(Mouse.GetPosition(surface));
			var on = ctxPic?.Bitmap != null;
			mview.IsEnabled = on;
			mcopy.IsEnabled = on;
			msave.IsEnabled = on;
			if (!on) e.Handled = true;
		};
		surface.ContextMenu = cm;
	}

	void paintpictures(DrawingContext dc, double sw, double sh) {
		var pics = model?.Pictures;
		if (pics == null || pics.Count == 0 || sw < 2 || sh < 2) return;
		var fw = freezew();
		var fh = freezeh();
		// 四个窗格分别裁剪，冻结线两侧用各自的表坐标
		paintpicpane(dc, pics, new Rect(ROW_HDR_W, HDR_H, Math.Max(0, fw), Math.Max(0, fh)), 0, 0);
		paintpicpane(dc, pics,
			new Rect(ROW_HDR_W, HDR_H + fh, Math.Max(0, fw), Math.Max(0, sh - HDR_H - fh)),
			0, originY + fh);
		paintpicpane(dc, pics,
			new Rect(ROW_HDR_W + fw, HDR_H, Math.Max(0, sw - ROW_HDR_W - fw), Math.Max(0, fh)),
			originX + fw, 0);
		paintpicpane(dc, pics,
			new Rect(ROW_HDR_W + fw, HDR_H + fh, Math.Max(0, sw - ROW_HDR_W - fw), Math.Max(0, sh - HDR_H - fh)),
			originX + fw, originY + fh);
	}

	void paintpicpane(DrawingContext dc, List<SheetPicture> pics, Rect view, double sheetX0, double sheetY0) {
		if (view.Width < 1 || view.Height < 1) return;
		var sheetX1 = sheetX0 + view.Width;
		var sheetY1 = sheetY0 + view.Height;
		var any = false;
		foreach (var p in pics) {
			if (p?.Bitmap == null) continue;
			if (!picbody(p, out var br)) continue;
			if (br.Right < sheetX0 || br.Left > sheetX1 || br.Bottom < sheetY0 || br.Top > sheetY1) continue;
			any = true;
			break;
		}
		if (!any) return;
		dc.PushClip(new RectangleGeometry(view));
		foreach (var p in pics) {
			if (p?.Bitmap == null) continue;
			try {
				if (!picbody(p, out var br)) continue;
				if (br.Right < sheetX0 || br.Left > sheetX1 || br.Bottom < sheetY0 || br.Top > sheetY1) continue;
				var dest = new Rect(view.X + (br.X - sheetX0), view.Y + (br.Y - sheetY0), br.Width, br.Height);
				drawpic(dc, p, dest);
			} catch (Exception ex) {
				DocViewLog.Warn($"paint picture: {ex.Message}");
			}
		}
		dc.Pop();
	}

	void drawpic(DrawingContext dc, SheetPicture p, Rect dest) {
		if (p?.Bitmap == null || dest.Width < 0.5 || dest.Height < 0.5) return;
		var rot = Math.Abs(p.Rot) > 0.05;
		var flip = p.FlipH || p.FlipV;
		var cx = dest.X + dest.Width / 2;
		var cy = dest.Y + dest.Height / 2;
		if (rot) dc.PushTransform(new RotateTransform(p.Rot, cx, cy));
		if (flip) dc.PushTransform(new ScaleTransform(p.FlipH ? -1 : 1, p.FlipV ? -1 : 1, cx, cy));
		var crop = p.CropL > 0.001 || p.CropT > 0.001 || p.CropR > 0.001 || p.CropB > 0.001;
		if (crop) {
			var vw = 1 - p.CropL - p.CropR;
			var vh = 1 - p.CropT - p.CropB;
			if (vw < 0.02) vw = 0.02;
			if (vh < 0.02) vh = 0.02;
			var brush = new ImageBrush(p.Bitmap) {
				Viewbox = new Rect(p.CropL, p.CropT, vw, vh),
				ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
				Stretch = Stretch.Fill,
			};
			if (brush.CanFreeze) brush.Freeze();
			dc.DrawRectangle(brush, null, dest);
		} else dc.DrawImage(p.Bitmap, dest);
		if (flip) dc.Pop();
		if (rot) dc.Pop();
	}

	SheetPicture hitpicture(WpfPoint local) {
		if (!sheetpoint(local, out var bx, out var by)) return null;
		var pics = model?.Pictures;
		if (pics == null || pics.Count == 0) return null;
		for (var i = pics.Count - 1; i >= 0; i--) {
			var p = pics[i];
			if (p == null) continue;
			if (!picbody(p, out var rc)) continue;
			if (Math.Abs(p.Rot) > 1)
				rc = rotatedbounds(rc, p.Rot);
			if (bx < rc.X || by < rc.Y || bx > rc.Right || by > rc.Bottom) continue;
			XlsxPictures.EnsureBitmap(p);
			if (p.Bitmap != null) return p;
		}
		return null;
	}

	static Rect rotatedbounds(Rect rc, double deg) {
		var rad = deg * Math.PI / 180.0;
		var cos = Math.Abs(Math.Cos(rad));
		var sin = Math.Abs(Math.Sin(rad));
		var w = rc.Width * cos + rc.Height * sin;
		var h = rc.Width * sin + rc.Height * cos;
		var cx = rc.X + rc.Width / 2;
		var cy = rc.Y + rc.Height / 2;
		return new Rect(cx - w / 2, cy - h / 2, w, h);
	}

	bool sheetpoint(WpfPoint local, out double bx, out double by) {
		bx = by = 0;
		if (local.X < ROW_HDR_W || local.Y < HDR_H) return false;
		var fw = freezew();
		var fh = freezeh();
		if (local.X < ROW_HDR_W + fw)
			bx = local.X - ROW_HDR_W;
		else
			bx = local.X - ROW_HDR_W + originX;
		if (local.Y < HDR_H + fh)
			by = local.Y - HDR_H;
		else
			by = local.Y - HDR_H + originY;
		return true;
	}

	bool picbody(SheetPicture p, out Rect rc) {
		rc = Rect.Empty;
		if (p == null) return false;
		var z = zoom > 0.01 ? zoom : 1;
		if (p.Kind == 3) {
			var r = p.FromRow;
			var c = p.FromCol;
			if (r < 0 || c < 0 || r >= rows || c >= cols) return false;
			var r0 = r;
			var c0 = c;
			var cs = 1;
			var rs = 1;
			var m = model?.FindMerge(r, c);
			if (m != null) {
				r0 = m.R0;
				c0 = m.C0;
				cs = m.C1 - m.C0 + 1;
				rs = m.R1 - m.R0 + 1;
			}
			var x = c0 >= 0 && colX != null && c0 < colX.Length ? colX[c0] : 0;
			var y = r0 >= 0 && rowY != null && r0 < rowY.Length ? rowY[r0] : 0;
			var w = spanwidth(c0, cs);
			var h = spanheight(r0, rs);
			if (w < 2 || h < 2) return false;
			rc = new Rect(x + 1, y + 1, Math.Max(1, w - 2), Math.Max(1, h - 2));
			return true;
		}
		if (p.Kind == 2) {
			if (p.AbsW < 0.5 || p.AbsH < 0.5) return false;
			rc = new Rect(p.AbsX * z, p.AbsY * z, p.AbsW * z, p.AbsH * z);
			return true;
		}
		if (p.Kind == 1) {
			if (p.AbsW < 0.5 || p.AbsH < 0.5) return false;
			rc = new Rect(xpos(p.FromCol, p.FromX), ypos(p.FromRow, p.FromY), p.AbsW * z, p.AbsH * z);
			return true;
		}
		var x1 = xpos(p.FromCol, p.FromX);
		var y1 = ypos(p.FromRow, p.FromY);
		var x2 = xpos(p.ToCol, p.ToX);
		var y2 = ypos(p.ToRow, p.ToY);
		if (x2 < x1) { var t = x1; x1 = x2; x2 = t; }
		if (y2 < y1) { var t = y1; y1 = y2; y2 = t; }
		if (x2 - x1 < 1 || y2 - y1 < 1) return false;
		rc = new Rect(x1, y1, x2 - x1, y2 - y1);
		return true;
	}

	double xpos(int col, double off) {
		var z = zoom > 0.01 ? zoom : 1;
		if (col < 0) col = 0;
		double x;
		if (colX != null && col < colX.Length)
			x = colX[col];
		else if (colX != null && colX.Length > 0)
			x = colX[colX.Length - 1] + (col - (colX.Length - 1)) * DEF_COL_W * z;
		else
			x = col * DEF_COL_W * z;
		return x + off * z;
	}

	double ypos(int row, double off) {
		var z = zoom > 0.01 ? zoom : 1;
		if (row < 0) row = 0;
		double y;
		if (rowY != null && row < rowY.Length)
			y = rowY[row];
		else if (rowY != null && rowY.Length > 0)
			y = rowY[rowY.Length - 1] + (row - (rowY.Length - 1)) * DEF_ROW_H * z;
		else
			y = row * DEF_ROW_H * z;
		return y + off * z;
	}

	void ondown(object sender, MouseButtonEventArgs e) {
		try { surface.Focus(); } catch { try { Focus(); } catch { /* ignore */ } }
		if (editR >= 0 && e.ClickCount < 2)
			endcelledit(commit: true);
		var local = e.GetPosition(surface);
		// 双击内嵌图 → 全窗预览（与 PDF / Word 相同）
		if (e.ClickCount >= 2) {
			var hit = hitpicture(local);
			if (hit?.Bitmap != null) {
				e.Handled = true;
				var title = string.IsNullOrEmpty(hit.Name) ? "图片预览" : hit.Name;
				ImageOverlay.Show(hit.Bitmap, title);
				return;
			}
		}
		var pt = toabs(local);
		// 表头固定行：筛选下拉（仿 Excel）
		if (tryhitfilterbtn(local, out var fc)) {
			togglefilterpopup(fc, local);
			e.Handled = true;
			return;
		}
		var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

		// 左上角：全选
		if (pt.X >= 0 && pt.X < ROW_HDR_W && pt.Y >= 0 && pt.Y < HDR_H) {
			if (rows > 0 && cols > 0) {
				selecting = false;
				selectingCols = selectingRows = false;
				selR0 = 0; selC0 = 0;
				selR1 = rows - 1; selC1 = cols - 1;
				anchorR = 0; anchorC = 0;
				activeR = rows - 1; activeC = cols - 1;
				surface.InvalidateVisual();
				raiseselection();
			}
			e.Handled = true;
			return;
		}

		// 列字母头：双击右边线 → 自适应；拖边 → 改宽；点/拖列头 → 选列
		if (pt.Y >= 0 && pt.Y < HDR_H && pt.X >= ROW_HDR_W) {
			var bodyX = pt.X - ROW_HDR_W;
			if (e.ClickCount >= 2) {
				if (tryhitcoledge(bodyX, out var fitCol, EDGE_HIT_AUTOFIT)) {
					if (resizingCol || resizingRow) endresize();
					autofitcol(fitCol);
					e.Handled = true;
					return;
				}
			} else if (tryhitcoledge(bodyX, out var ci)) {
				begincolresize(ci, pt.X);
				surface.CaptureMouse();
				e.Handled = true;
				return;
			}
			// 点在列头主体：选整列；Shift 连续多列；拖动扩展
			var c = findcol(bodyX);
			c = clampi(c, 0, Math.Max(0, cols - 1));
			selecting = true;
			selectingCols = true;
			selectingRows = false;
			if (shift && anchorC >= 0) {
				selectcolumns(anchorC, c, setAnchor: false);
				activeC = c;
			} else {
				selectcolumns(c, c, setAnchor: true);
			}
			surface.CaptureMouse();
			surface.InvalidateVisual();
			raiseselection();
			e.Handled = true;
			return;
		}

		// 行号：拖边改高；点/拖行号 → 选行
		if (pt.X >= 0 && pt.X < ROW_HDR_W && pt.Y >= HDR_H) {
			var bodyY = pt.Y - HDR_H;
			if (tryhitrowedge(bodyY, out var ri)) {
				beginrowresize(ri, pt.Y);
				surface.CaptureMouse();
				e.Handled = true;
				return;
			}
			var r = findrow(bodyY);
			r = clampi(r, 0, Math.Max(0, rows - 1));
			// 点在隐藏行上时找最近可见行
			if (isrowhidden(r)) {
				var g = 0;
				while (r < rows && isrowhidden(r) && g++ < rows) r++;
				if (r >= rows) r = rows - 1;
			}
			selecting = true;
			selectingRows = true;
			selectingCols = false;
			if (shift && anchorR >= 0) {
				selectrows(anchorR, r, setAnchor: false);
				activeR = r;
			} else {
				selectrows(r, r, setAnchor: true);
			}
			surface.CaptureMouse();
			surface.InvalidateVisual();
			raiseselection();
			e.Handled = true;
			return;
		}

		if (!hittest(pt, out var cr, out var cc)) return;
		model.ResolveOrigin(ref cr, ref cc);
		selecting = true;
		selectingCols = false;
		selectingRows = false;
		// 普通点击重置锚点与活动格；Shift+点击可块选
		var shiftClick = shift && anchorR >= 0 && anchorC >= 0;
		var m = model.FindMerge(cr, cc);
		if (shiftClick) {
			activeR = m != null ? m.R0 : cr;
			activeC = m != null ? m.C0 : cc;
			applyselrect(anchorR, anchorC, activeR, activeC);
		} else {
			if (m != null) {
				selR0 = m.R0; selC0 = m.C0;
				selR1 = m.R1; selC1 = m.C1;
				anchorR = m.R0;
				anchorC = m.C0;
				activeR = m.R0;
				activeC = m.C0;
			} else {
				selR0 = selR1 = cr;
				selC0 = selC1 = cc;
				anchorR = activeR = cr;
				anchorC = activeC = cc;
			}
			expandselformerges();
		}
		surface.CaptureMouse();
		surface.InvalidateVisual();
		raiseselection();
		e.Handled = true;
	}

	void onmove(object sender, MouseEventArgs e) {
		var pt = toabs(e.GetPosition(surface));
		if (resizingCol) {
			applycolresize(pt.X);
			e.Handled = true;
			return;
		}
		if (resizingRow) {
			applyrowresize(pt.Y);
			e.Handled = true;
			return;
		}
		if (!selecting) {
			var local = e.GetPosition(surface);
			if (hitpicture(local) != null)
				Cursor = Cursors.Hand;
			else if (pt.Y >= 0 && pt.Y < HDR_H && tryhitcoledge(pt.X - ROW_HDR_W, out _))
				Cursor = Cursors.SizeWE;
			else if (pt.X >= 0 && pt.X < ROW_HDR_W && pt.Y >= HDR_H && tryhitrowedge(pt.Y - HDR_H, out _))
				Cursor = Cursors.SizeNS;
			else
				Cursor = Cursors.Arrow;
			if (Cursor == Cursors.Hand) {
				if (surface.ToolTip as string != "双击预览 · 右键复制/保存")
					surface.ToolTip = "双击预览 · 右键复制/保存";
			} else if (surface.ToolTip is string)
				surface.ToolTip = null;
		}
		if (!selecting || e.LeftButton != MouseButtonState.Pressed) return;

		// 拖选多列
		if (selectingCols) {
			var c = findcol(Math.Max(0, pt.X - ROW_HDR_W));
			c = clampi(c, 0, Math.Max(0, cols - 1));
			var col0 = Math.Min(anchorC, c);
			var col1 = Math.Max(anchorC, c);
			if (col0 == Math.Min(selC0, selC1) && col1 == Math.Max(selC0, selC1)
				&& selR0 == 0 && selR1 == rows - 1) {
				e.Handled = true;
				return;
			}
			selectcolumns(anchorC, c, setAnchor: false);
			activeC = c;
			surface.InvalidateVisual();
			raiseselection();
			e.Handled = true;
			return;
		}
		// 拖选多行
		if (selectingRows) {
			var r = findrow(Math.Max(0, pt.Y - HDR_H));
			r = clampi(r, 0, Math.Max(0, rows - 1));
			var row0 = Math.Min(anchorR, r);
			var row1 = Math.Max(anchorR, r);
			if (row0 == Math.Min(selR0, selR1) && row1 == Math.Max(selR0, selR1)
				&& selC0 == 0 && selC1 == cols - 1) {
				e.Handled = true;
				return;
			}
			selectrows(anchorR, r, setAnchor: false);
			activeR = r;
			surface.InvalidateVisual();
			raiseselection();
			e.Handled = true;
			return;
		}

		if (!hittest(pt, out var cellR, out var cellC)) {
			cellR = findrow(Math.Max(0, pt.Y - HDR_H));
			cellC = findcol(Math.Max(0, pt.X - ROW_HDR_W));
			cellR = clampi(cellR, 0, Math.Max(0, rows - 1));
			cellC = clampi(cellC, 0, Math.Max(0, cols - 1));
		}
		model.ResolveOrigin(ref cellR, ref cellC);
		var mEnd = model.FindMerge(cellR, cellC);
		var mAnc = model.FindMerge(anchorR, anchorC);
		var ar0 = mAnc != null ? mAnc.R0 : anchorR;
		var ac0 = mAnc != null ? mAnc.C0 : anchorC;
		var ar1 = mAnc != null ? mAnc.R1 : anchorR;
		var ac1 = mAnc != null ? mAnc.C1 : anchorC;
		var er0 = mEnd != null ? mEnd.R0 : cellR;
		var ec0 = mEnd != null ? mEnd.C0 : cellC;
		var er1 = mEnd != null ? mEnd.R1 : cellR;
		var ec1 = mEnd != null ? mEnd.C1 : cellC;
		var nr0 = Math.Min(ar0, er0);
		var nc0 = Math.Min(ac0, ec0);
		var nr1 = Math.Max(ar1, er1);
		var nc1 = Math.Max(ac1, ec1);
		// 选区未变则跳过整表重绘
		if (nr0 == selR0 && nc0 == selC0 && nr1 == selR1 && nc1 == selC1) {
			e.Handled = true;
			return;
		}
		selR0 = nr0;
		selC0 = nc0;
		selR1 = nr1;
		selC1 = nc1;
		// 拖选时活动角跟随鼠标（锚点仍为按下处）
		activeR = mEnd != null ? mEnd.R0 : cellR;
		activeC = mEnd != null ? mEnd.C0 : cellC;
		expandselformerges();
		surface.InvalidateVisual();
		raiseselection();
		e.Handled = true;
	}

	void onup(object sender, MouseButtonEventArgs e) {
		if (resizingCol || resizingRow) {
			endresize();
			try { surface.ReleaseMouseCapture(); } catch { /* ignore */ }
			e.Handled = true;
			return;
		}
		if (!selecting) return;
		selecting = false;
		selectingCols = false;
		selectingRows = false;
		try { surface.ReleaseMouseCapture(); } catch { /* ignore */ }
		raiseselection();
		e.Handled = true;
	}

	void onkey(object sender, KeyEventArgs e) {
		if (e.Handled) return;
		if (editR >= 0) return; // 编辑框自己处理
		if (e.Key == Key.F2 && editMode && HasSelection) {
			GetSelection(out var r0, out var c0, out _, out _);
			model.ResolveOrigin(ref r0, ref c0);
			begincelledit(r0, c0);
			e.Handled = true;
			return;
		}
		// 方向键 / Tab：移动选中格；Shift+方向键 = 块选
		if ((Keyboard.Modifiers & ModifierKeys.Control) == 0
			&& (Keyboard.Modifiers & ModifierKeys.Alt) == 0) {
			var extend = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
				&& e.Key != Key.Tab; // Tab 的 Shift 表示反向，不是块选
			if (e.Key == Key.Left) {
				moveselection(0, -1, wrap: false, extend: extend);
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Right) {
				moveselection(0, 1, wrap: false, extend: extend);
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Up) {
				moveselection(-1, 0, wrap: false, extend: extend);
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Down) {
				moveselection(1, 0, wrap: false, extend: extend);
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Tab) {
				moveselection(0, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1, wrap: true, extend: false);
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Enter && editMode) {
				// 编辑模式 Enter：进编辑或下移
				if (HasSelection) {
					GetSelection(out var r0, out var c0, out _, out _);
					model.ResolveOrigin(ref r0, ref c0);
					begincelledit(r0, c0);
					e.Handled = true;
					return;
				}
			}
		}
		if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
			if (TryCopySelection()) e.Handled = true;
		}
		if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
			if (rows > 0 && cols > 0) {
				selR0 = 0; selC0 = 0;
				selR1 = rows - 1; selC1 = cols - 1;
				surface.InvalidateVisual();
				raiseselection();
				e.Handled = true;
			}
		}
		if (e.Key == Key.Escape) {
			selR0 = selC0 = selR1 = selC1 = -1;
			surface.InvalidateVisual();
			raiseselection();
			e.Handled = true;
		}
		// 编辑模式：直接键入开始改字（不含方向键/Tab/Enter）
		if (editMode && HasSelection
			&& e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Up && e.Key != Key.Down
			&& e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl
			&& e.Key != Key.LeftShift && e.Key != Key.RightShift
			&& e.Key != Key.LeftAlt && e.Key != Key.RightAlt
			&& e.Key != Key.Tab && e.Key != Key.Enter
			&& (Keyboard.Modifiers & ModifierKeys.Control) == 0
			&& (Keyboard.Modifiers & ModifierKeys.Alt) == 0) {
			var ch = keytochar(e.Key);
			if (ch != null) {
				GetSelection(out var r0, out var c0, out _, out _);
				model.ResolveOrigin(ref r0, ref c0);
				begincelledit(r0, c0);
				if (editBox != null) {
					editBox.Text = ch;
					editBox.CaretIndex = editBox.Text.Length;
				}
				e.Handled = true;
			}
		}
	}

	/// <summary>
	/// 像 Excel：移动活动格；extend=true 时锚点不动、块选扩展。
	/// wrap=true 时 Tab 到行尾折到下一行。
	/// </summary>
	void moveselection(int dr, int dc, bool wrap, bool extend = false) {
		if (rows <= 0 || cols <= 0 || model == null) return;
		if (editR >= 0) endcelledit(commit: true);

		var oldR = selR0;
		var oldC = selC0;

		// 起点：块选时从 active 移；普通移动从当前选区
		int r, c;
		if (extend && activeR >= 0 && activeC >= 0) {
			r = activeR;
			c = activeC;
		} else if (!HasSelection) {
			r = 0;
			c = 0;
		} else if (activeR >= 0 && activeC >= 0) {
			r = activeR;
			c = activeC;
		} else {
			GetSelection(out var r0, out var c0, out _, out _);
			r = r0;
			c = c0;
		}
		model.ResolveOrigin(ref r, ref c);
		var m = model.FindMerge(r, c);
		if (m != null) {
			if (dc > 0) c = m.C1 + 1;
			else if (dc < 0) c = m.C0 - 1;
			else c = m.C0;
			if (dr > 0) r = m.R1 + 1;
			else if (dr < 0) r = m.R0 - 1;
			else r = m.R0;
		} else {
			r += dr;
			c += dc;
		}

		if (wrap && dc != 0 && dr == 0 && !extend) {
			if (c >= cols) {
				c = 0;
				r++;
			} else if (c < 0) {
				c = cols - 1;
				r--;
			}
		}

		// 跳过隐藏行
		var guard = 0;
		while (r >= 0 && r < rows && isrowhidden(r) && guard++ < rows + 2) {
			if (dr != 0) r += Math.Sign(dr);
			else if (wrap && dc != 0) r += dc > 0 ? 1 : -1;
			else break;
		}

		if (r < 0) r = 0;
		if (c < 0) c = 0;
		if (r >= rows) r = rows - 1;
		if (c >= cols) c = cols - 1;
		if (r < 0 || c < 0) return;

		model.ResolveOrigin(ref r, ref c);
		if (extend) {
			// 锚点无效则用当前位置
			if (anchorR < 0 || anchorC < 0) {
				anchorR = activeR >= 0 ? activeR : r;
				anchorC = activeC >= 0 ? activeC : c;
			}
			activeR = r;
			activeC = c;
			applyselrect(anchorR, anchorC, activeR, activeC);
			scrolltocell(r, c);
			surface.InvalidateVisual();
			try { surface.Focus(); } catch { /* ignore */ }
			raiseselection();
			DocViewLog.Info($"sheet extend dr={dr} dc={dc} anchor=({anchorR},{anchorC}) active=({activeR},{activeC}) sel=({selR0},{selC0})-({selR1},{selC1})");
		} else {
			selectcell(r, c, takeFocus: true);
			raiseselection();
			DocViewLog.Info($"sheet move dr={dr} dc={dc} ({oldR},{oldC})->({selR0},{selC0})-({selR1},{selC1})");
		}
	}

	/// <summary>以两格为对角设置选区（含各自合并块），并 expand 相交合并。</summary>
	void applyselrect(int rA, int cA, int rB, int cB) {
		model.ResolveOrigin(ref rA, ref cA);
		model.ResolveOrigin(ref rB, ref cB);
		var mA = model.FindMerge(rA, cA);
		var mB = model.FindMerge(rB, cB);
		var r0 = mA != null ? mA.R0 : rA;
		var c0 = mA != null ? mA.C0 : cA;
		var r1 = mA != null ? mA.R1 : rA;
		var c1 = mA != null ? mA.C1 : cA;
		var r0b = mB != null ? mB.R0 : rB;
		var c0b = mB != null ? mB.C0 : cB;
		var r1b = mB != null ? mB.R1 : rB;
		var c1b = mB != null ? mB.C1 : cB;
		selR0 = Math.Min(r0, r0b);
		selC0 = Math.Min(c0, c0b);
		selR1 = Math.Max(r1, r1b);
		selC1 = Math.Max(c1, c1b);
		expandselformerges();
	}

	/// <summary>供主窗：方向键移动选区。</summary>
	public void MoveSelectionBy(int dr, int dc) => moveselection(dr, dc, wrap: false, extend: false);

	/// <summary>供主窗：Shift+方向键块选。</summary>
	public void ExtendSelectionBy(int dr, int dc) => moveselection(dr, dc, wrap: false, extend: true);

	static string keytochar(Key key) {
		if (key >= Key.A && key <= Key.Z) {
			var c = (char)('a' + (key - Key.A));
			if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) c = char.ToUpperInvariant(c);
			return c.ToString();
		}
		if (key >= Key.D0 && key <= Key.D9) {
			if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return null;
			return ((char)('0' + (key - Key.D0))).ToString();
		}
		if (key >= Key.NumPad0 && key <= Key.NumPad9)
			return ((char)('0' + (key - Key.NumPad0))).ToString();
		if (key == Key.Space) return " ";
		return null;
	}

	void selectcell(int r, int c, bool takeFocus = true) {
		model.ResolveOrigin(ref r, ref c);
		var m = model.FindMerge(r, c);
		if (m != null) {
			selR0 = m.R0; selC0 = m.C0;
			selR1 = m.R1; selC1 = m.C1;
			anchorR = m.R0;
			anchorC = m.C0;
			activeR = m.R0;
			activeC = m.C0;
		} else {
			selR0 = selR1 = r;
			selC0 = selC1 = c;
			anchorR = activeR = r;
			anchorC = activeC = c;
		}
		expandselformerges();
		scrolltocell(r, c);
		surface.InvalidateVisual();
		if (takeFocus) {
			try { surface.Focus(); } catch { try { Focus(); } catch { /* ignore */ } }
		}
	}

	void expandselformerges() {
		if (selR0 < 0 || selC0 < 0 || model?.Merges == null || model.Merges.Count == 0) return;
		var r0 = Math.Min(selR0, selR1);
		var r1 = Math.Max(selR0, selR1);
		var c0 = Math.Min(selC0, selC1);
		var c1 = Math.Max(selC0, selC1);
		var guard = 0;
		bool changed;
		do {
			changed = false;
			foreach (var m in model.Merges) {
				if (m == null) continue;
				if (m.R1 < r0 || m.R0 > r1 || m.C1 < c0 || m.C0 > c1) continue;
				if (m.R0 < r0) { r0 = m.R0; changed = true; }
				if (m.R1 > r1) { r1 = m.R1; changed = true; }
				if (m.C0 < c0) { c0 = m.C0; changed = true; }
				if (m.C1 > c1) { c1 = m.C1; changed = true; }
			}
			guard++;
		} while (changed && guard < 64);
		selR0 = clampi(r0, 0, Math.Max(0, rows - 1));
		selR1 = clampi(r1, 0, Math.Max(0, rows - 1));
		selC0 = clampi(c0, 0, Math.Max(0, cols - 1));
		selC1 = clampi(c1, 0, Math.Max(0, cols - 1));
	}

	void scrolltocell(int r, int c) {
		if (r < 0 || c < 0 || r >= rows || c >= cols) return;
		var x = ROW_HDR_W + colX[c];
		var y = HDR_H + rowY[r];
		var w = spanwidth(c, cellobj(r, c).ColSpan);
		var h = spanheight(r, cellobj(r, c).RowSpan);
		try {
			if (x < scroll.HorizontalOffset)
				scroll.ScrollToHorizontalOffset(x);
			else if (x + w > scroll.HorizontalOffset + scroll.ViewportWidth)
				scroll.ScrollToHorizontalOffset(Math.Max(0, x + w - scroll.ViewportWidth));
			if (y < scroll.VerticalOffset)
				scroll.ScrollToVerticalOffset(y);
			else if (y + h > scroll.VerticalOffset + scroll.ViewportHeight)
				scroll.ScrollToVerticalOffset(Math.Max(0, y + h - scroll.ViewportHeight));
		} catch { /* ignore */ }
	}

	bool hittest(WpfPoint pt, out int r, out int c) {
		r = c = -1;
		if (cols <= 0 || rows <= 0) return false;
		if (pt.X < ROW_HDR_W || pt.Y < HDR_H) return false;
		c = findcol(pt.X - ROW_HDR_W);
		r = findrow(pt.Y - HDR_H);
		if (r < 0 || r >= rows || c < 0 || c >= cols) return false;
		return true;
	}

	bool tryhitcoledge(double x, out int col) => tryhitcoledge(x, out col, EDGE_HIT);

	/// <param name="hitTol">命中半宽（当前 zoom 下的 body 坐标，与 colX 同系）。</param>
	bool tryhitcoledge(double x, out int col, double hitTol) {
		col = -1;
		if (cols <= 0 || colX == null || colX.Length < 2) return false;
		if (hitTol < 1) hitTol = EDGE_HIT;
		// 只查命中列附近边界，避免大表 O(n) 全扫
		var c = findcol(x);
		var best = hitTol + 1;
		for (var i = Math.Max(0, c - 1); i <= Math.Min(cols - 1, c + 1); i++) {
			// 命中第 i 列的右边界 → 调整/自适应第 i 列
			var d = Math.Abs(x - colX[i + 1]);
			if (d <= hitTol && d <= best) { best = d; col = i; }
		}
		return col >= 0;
	}

	bool tryhitrowedge(double y, out int row) {
		row = -1;
		if (rows <= 0 || rowY == null || rowY.Length < 2) return false;
		// 只查命中行附近边界，避免 6000+ 行全扫
		var r = findrow(y);
		var best = EDGE_HIT + 1;
		for (var i = Math.Max(0, r - 1); i <= Math.Min(rows - 1, r + 1); i++) {
			var d = Math.Abs(y - rowY[i + 1]);
			if (d <= EDGE_HIT && d <= best) { best = d; row = i; }
		}
		return row >= 0;
	}

	/// <summary>
	/// 双击列右边界：按中/英文字符宽简单估算内容最大宽度并设列宽（zoom=1 base）。
	/// 中文（全角）≈ 1.05×字号 DIP；英文/半角 ≈ 0.55×字号。
	/// </summary>
	void autofitcol(int col) {
		if (col < 0 || col >= cols || model == null) return;
		ensurecolwidths();
		const double padX = 10; // 左右内边距（base DIP）
		var maxW = (double)RESIZE_MIN_COL;
		// 列头字母本身
		maxW = Math.Max(maxW, estimatetextwidth(colname(col), 11.0 * 96.0 / 72.0, bold: false) + padX);

		var limit = Math.Min(rows, AUTOFIT_COL_MAX_ROWS);
		for (var r = 0; r < limit; r++) {
			if (isrowhidden(r)) continue;
			var sc = model.CellAt(r, col);
			if (sc == null || sc.HiddenByMerge) continue;
			// 跨多列合并不单列自适应（避免被整段拉爆）
			if (sc.ColSpan > 1) continue;
			var m = model.FindMerge(r, col);
			if (m != null && (m.C1 > m.C0 || !m.IsOrigin(r, col))) continue;
			var text = sc.Text;
			if (string.IsNullOrEmpty(text)) continue;
			// 与 paint 一致：磅 → DIP；此处按 zoom=1 算 base 宽
			var fs = sc.FontSizePt > 0 ? sc.FontSizePt * 96.0 / 72.0 : 11.0 * 96.0 / 72.0;
			if (fs < 8) fs = 8;
			var w = estimatetextwidth(text, fs, sc.Bold) + padX;
			// 筛选表头行：右侧留给下拉按钮
			if (r == filterHdrRow && canfiltercol(col)) w += 18;
			if (w > maxW) maxW = w;
		}

		// 不人为封顶列宽；仅保证下限
		var baseW = Math.Max(RESIZE_MIN_COL, maxW);
		if (baseW > RESIZE_MAX_COL) baseW = RESIZE_MAX_COL; // 仅防 double 爆炸
		model.ColWidths[col] = baseW;
		patchcolmetrics(col, baseW);
		// 只重算依赖该列宽度的换行行，避免全表 FormattedText
		autofitrowheightsforcol(col);
		applytablesize();
		surface.InvalidateVisual();
		DocViewLog.Info($"autofitcol c={col} baseW={baseW:F1} rows={limit}");
	}

	/// <summary>按中/英文字宽估算文本显示宽（多行取最长行）。fs 为 DIP 字号。</summary>
	static double estimatetextwidth(string text, double fs, bool bold) {
		if (string.IsNullOrEmpty(text) || fs < 1) return 0;
		// 英文约 0.55em，中文/全角约 1.05em（略放大避免裁切）
		var enW = fs * 0.55;
		var zhW = fs * 1.05;
		if (bold) {
			enW *= 1.08;
			zhW *= 1.05;
		}
		double best = 0, line = 0;
		for (var i = 0; i < text.Length; i++) {
			var ch = text[i];
			if (ch == '\r') continue;
			if (ch == '\n') {
				if (line > best) best = line;
				line = 0;
				continue;
			}
			if (ch == '\t') {
				line += enW * 4;
				continue;
			}
			line += iswidechar(ch) ? zhW : enW;
		}
		if (line > best) best = line;
		return best;
	}

	/// <summary>中日韩/全角等宽字符（按一字宽计）。</summary>
	static bool iswidechar(char c) {
		if (c < 0x1100) return false;
		// Hangul Jamo
		if (c <= 0x115F) return true;
		// CJK 部首/符号/假名/汉字
		if (c >= 0x2E80 && c <= 0xA4CF) return true;
		// Hangul 音节
		if (c >= 0xAC00 && c <= 0xD7A3) return true;
		// CJK 兼容
		if (c >= 0xF900 && c <= 0xFAFF) return true;
		// 竖排标点 / 全角
		if (c >= 0xFE10 && c <= 0xFE19) return true;
		if (c >= 0xFE30 && c <= 0xFE6F) return true;
		if (c >= 0xFF00 && c <= 0xFF60) return true;
		if (c >= 0xFFE0 && c <= 0xFFE6) return true;
		return false;
	}

	void begincolresize(int col, double mouseX) {
		if (col < 0 || col >= cols) return;
		ensurecolwidths();
		resizingCol = true;
		resizingRow = false;
		selecting = false;
		resizeIdx = col;
		resizeStartMouse = mouseX;
		resizeStartBase = model.ColWidths[col] > 0 ? model.ColWidths[col] : DEF_COL_W;
		Cursor = Cursors.SizeWE;
	}

	void beginrowresize(int row, double mouseY) {
		if (row < 0 || row >= rows) return;
		ensurerowheights();
		resizingRow = true;
		resizingCol = false;
		selecting = false;
		resizeIdx = row;
		resizeStartMouse = mouseY;
		resizeStartBase = model.RowHeights[row] > 0 ? model.RowHeights[row] : DEF_ROW_H;
		Cursor = Cursors.SizeNS;
	}

	void applycolresize(double mouseX) {
		if (!resizingCol || resizeIdx < 0 || resizeIdx >= cols) return;
		ensurecolwidths();
		var z = zoom > 0.01 ? zoom : 1.0;
		// 列宽不设业务上限（仅下限 + 极大值防溢出）
		var baseW = resizeStartBase + (mouseX - resizeStartMouse) / z;
		if (baseW < RESIZE_MIN_COL) baseW = RESIZE_MIN_COL;
		if (baseW > RESIZE_MAX_COL) baseW = RESIZE_MAX_COL;
		if (Math.Abs(model.ColWidths[resizeIdx] - baseW) < 0.01) return;
		model.ColWidths[resizeIdx] = baseW;
		// 增量改 colX，勿 rebuild 全表行数组（末列拖动时 Extent 每帧变，旧路径会卡数秒）
		patchcolmetrics(resizeIdx, baseW);
		applytablesize();
		surface.InvalidateVisual();
	}

	void applyrowresize(double mouseY) {
		if (!resizingRow || resizeIdx < 0 || resizeIdx >= rows) return;
		ensurerowheights();
		var z = zoom > 0.01 ? zoom : 1.0;
		var baseH = clamp(resizeStartBase + (mouseY - resizeStartMouse) / z, RESIZE_MIN_ROW, RESIZE_MAX_ROW);
		if (Math.Abs(model.RowHeights[resizeIdx] - baseH) < 0.01) return;
		model.RowHeights[resizeIdx] = baseH;
		patchrowmetrics(resizeIdx, baseH);
		applytablesize();
		surface.InvalidateVisual();
	}

	void endresize() {
		var wasCol = resizingCol;
		var idx = resizeIdx;
		resizingCol = false;
		resizingRow = false;
		resizeIdx = -1;
		Cursor = Cursors.Arrow;
		if (wasCol && idx >= 0) {
			// 仅重算依赖该列的换行行；全表 precise 测量会卡几秒
			autofitrowheightsforcol(idx);
			applytablesize();
		}
		surface.InvalidateVisual();
	}

	int findcol(double x) {
		if (cols <= 0) return -1;
		if (x < 0) return 0;
		if (x >= totalBodyW) return cols - 1;
		var lo = 0;
		var hi = cols - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >> 1;
			if (x < colX[mid]) hi = mid - 1;
			else if (x >= colX[mid + 1]) lo = mid + 1;
			else return mid;
		}
		return clampi(lo, 0, cols - 1);
	}

	int findrow(double y) {
		if (rows <= 0) return -1;
		if (y < 0) return 0;
		if (y >= totalBodyH) return rows - 1;
		var lo = 0;
		var hi = rows - 1;
		while (lo <= hi) {
			var mid = (lo + hi) >> 1;
			if (y < rowY[mid]) hi = mid - 1;
			else if (y >= rowY[mid + 1]) lo = mid + 1;
			else return mid;
		}
		return clampi(lo, 0, rows - 1);
	}

	static string colname(int index) {
		var n = index + 1;
		var s = "";
		while (n > 0) {
			n--;
			s = (char)('A' + n % 26) + s;
			n /= 26;
		}
		return s;
	}

	static int clampi(int v, int lo, int hi) {
		if (hi < lo) return lo;
		if (v < lo) return lo;
		if (v > hi) return hi;
		return v;
	}

	static double clamp(double v, double lo, double hi) {
		if (v < lo) return lo;
		if (v > hi) return hi;
		return v;
	}

	static SolidColorBrush brush(byte r, byte g, byte b) {
		var br = new SolidColorBrush(MediaColor.FromRgb(r, g, b));
		br.Freeze();
		return br;
	}

	static SolidColorBrush solid(MediaColor c) {
		var key = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
		if (ColorBrushCache.TryGetValue(key, out var hit))
			return hit;
		var br = new SolidColorBrush(c);
		if (br.CanFreeze) br.Freeze();
		if (ColorBrushCache.Count < 512)
			ColorBrushCache[key] = br;
		return br;
	}

	// ---------- 筛选 ----------

	void applyrowhiddenheights() {
		if (rows <= 0) return;
		ensurerowheights();
		if (rowHidden == null || rowHidden.Length != rows)
			rowHidden = new bool[rows];
		for (var r = 0; r < rows; r++) {
			if (r < model.RowHeights.Length && model.RowHeights[r] <= 0) {
				rowH[r] = 0;
				continue;
			}
			if (isrowhidden(r) && r >= freezeRows) {
				rowH[r] = 0;
			} else {
				var baseH = (model.RowHeights != null && r < model.RowHeights.Length && model.RowHeights[r] > 0)
					? model.RowHeights[r] : DEF_ROW_H;
				rowH[r] = Math.Max(MIN_ROW_H * zoom, baseH * zoom);
			}
		}
		// 重建 rowY
		if (rowY == null || rowY.Length != rows + 1)
			rowY = new double[rows + 1];
		rowY[0] = 0;
		for (var r = 0; r < rows; r++)
			rowY[r + 1] = rowY[r] + rowH[r];
		totalBodyH = rowY[rows];
		applytablesize();
	}

	void reapplyfilters() {
		if (rows <= 0) {
			rowHidden = Array.Empty<bool>();
			return;
		}
		if (rowHidden == null || rowHidden.Length != rows)
			rowHidden = new bool[rows];
		for (var r = 0; r < rows; r++) {
			// 冻结行与表头不隐藏
			if (r < freezeRows || r == filterHdrRow) {
				rowHidden[r] = false;
				continue;
			}
			if (filterR0 >= 0 && (r < filterR0 || r > filterR1)) {
				rowHidden[r] = false;
				continue;
			}
			var hide = false;
			foreach (var kv in colFilters) {
				var f = kv.Value;
				if (f == null || !f.IsActive) continue;
				if (!f.Match(cellobj(r, kv.Key).Text ?? "")) {
					hide = true;
					break;
				}
			}
			rowHidden[r] = hide;
		}
		applyrowhiddenheights();
		surface.InvalidateVisual();
	}

	/// <summary>命中表头固定行右侧筛选按钮区。</summary>
	bool tryhitfilterbtn(WpfPoint local, out int col) {
		col = -1;
		if (filterHdrRow < 0 || filterHdrRow >= rows || filterHdrRow >= rowH.Length) return false;
		if (filterR1 <= filterHdrRow) return false;
		var y0 = bodylocaly(filterHdrRow);
		var h = rowH[filterHdrRow];
		if (h < 4 || local.Y < y0 || local.Y >= y0 + h) return false;
		for (var c = filterC0; c <= filterC1 && c < cols; c++) {
			if (!canfiltercol(c) || c >= colW.Length || colW[c] < 8) continue;
			var x = bodylocalx(c);
			var w = colW[c];
			// 右侧按钮 + 略扩点击区
			if (local.X >= x + w - 20 && local.X <= x + w + 2) {
				col = c;
				return true;
			}
		}
		return false;
	}

	bool cansortrange() {
		if (filterHdrRow < 0 || filterR1 <= filterHdrRow) return false;
		// 数据区有跨行合并时不物理重排
		if (model?.Merges != null) {
			foreach (var m in model.Merges) {
				if (m == null) continue;
				if (m.R1 > m.R0 && m.R1 > filterHdrRow && m.R0 <= filterR1)
					return false;
			}
		}
		return true;
	}

	void clearsortbackup() {
		sortBackupRows = null;
		sortBackupH = null;
		sortBackupR0 = sortBackupR1 = -1;
	}

	/// <summary>首次排序前快照数据行，供取消排序还原。</summary>
	void ensuresortbackup(int r0, int r1) {
		if (sortBackupRows != null
			&& sortBackupR0 == r0 && sortBackupR1 == r1
			&& sortBackupRows.Length == r1 - r0 + 1)
			return;
		ensurerowheights();
		var n = r1 - r0 + 1;
		sortBackupRows = new SheetCell[n][];
		sortBackupH = new double[n];
		var srcCells = model.Cells;
		var srcH = model.RowHeights;
		for (var i = 0; i < n; i++) {
			var r = r0 + i;
			sortBackupRows[i] = srcCells != null && r < srcCells.Length ? srcCells[r] : null;
			sortBackupH[i] = srcH != null && r < srcH.Length ? srcH[r] : DEF_ROW_H;
		}
		sortBackupR0 = r0;
		sortBackupR1 = r1;
	}

	void applysort(int col, bool asc) {
		if (model?.Cells == null || col < 0 || col >= cols) return;
		if (!cansortrange()) return;
		var r0 = filterHdrRow + 1;
		var r1 = filterR1 >= 0 ? filterR1 : rows - 1;
		if (r0 < 0) r0 = 0;
		if (r1 >= rows) r1 = rows - 1;
		if (r1 < r0) return;

		// 始终以「首次排序前」顺序为基准再排，取消排序才能还原
		if (sortBackupRows == null || sortBackupR0 != r0 || sortBackupR1 != r1)
			ensuresortbackup(r0, r1);
		else
			restoresortbackup(refilter: false);

		var n = r1 - r0 + 1;
		var order = new int[n];
		for (var i = 0; i < n; i++) order[i] = r0 + i;

		// 稳定排序：值比较，相同则保留原序
		Array.Sort(order, (ra, rb) => {
			var cmp = comparecelltext(cellobj(ra, col).Text, cellobj(rb, col).Text);
			if (cmp == 0) return ra.CompareTo(rb);
			return asc ? cmp : -cmp;
		});

		ensurerowheights();
		var srcCells = model.Cells;
		var srcH = model.RowHeights;
		var newRows = new SheetCell[n][];
		var newH = new double[n];
		for (var i = 0; i < n; i++) {
			var src = order[i];
			newRows[i] = srcCells[src];
			newH[i] = srcH != null && src < srcH.Length ? srcH[src] : DEF_ROW_H;
		}
		for (var i = 0; i < n; i++) {
			srcCells[r0 + i] = newRows[i];
			srcH[r0 + i] = newH[i];
		}

		sortCol = col;
		sortAsc = asc;
		// 查找缓存失效
		findHits.Clear();
		findIndex = -1;
		// 选区随数据变，清空以免指错
		selR0 = selC0 = selR1 = selC1 = -1;
		reapplyfilters();
		surface.InvalidateVisual();
		DocViewLog.Info($"sort col={colname(col)} asc={asc} rows={r0}-{r1}");
	}

	/// <summary>取消排序：恢复首次排序前的行序。</summary>
	void clearsort() {
		if (sortBackupRows == null) {
			sortCol = -1;
			surface.InvalidateVisual();
			return;
		}
		restoresortbackup(refilter: true);
		sortCol = -1;
		clearsortbackup();
		findHits.Clear();
		findIndex = -1;
		selR0 = selC0 = selR1 = selC1 = -1;
		DocViewLog.Info("sort cleared, original order restored");
	}

	void restoresortbackup(bool refilter) {
		if (sortBackupRows == null || model?.Cells == null) return;
		var r0 = sortBackupR0;
		var r1 = sortBackupR1;
		if (r0 < 0 || r1 < r0) return;
		ensurerowheights();
		var srcCells = model.Cells;
		var srcH = model.RowHeights;
		var n = Math.Min(sortBackupRows.Length, r1 - r0 + 1);
		for (var i = 0; i < n; i++) {
			var r = r0 + i;
			if (r >= srcCells.Length) break;
			srcCells[r] = sortBackupRows[i];
			if (srcH != null && r < srcH.Length && sortBackupH != null && i < sortBackupH.Length)
				srcH[r] = sortBackupH[i];
		}
		rebuildmetrics();
		applytablesize();
		if (refilter) reapplyfilters();
		else surface.InvalidateVisual();
	}

	/// <summary>排序键：空白靠后；数字/日期优先于文本；文本不区分大小写。</summary>
	static int comparecelltext(string a, string b) {
		a = a ?? "";
		b = b ?? "";
		var ea = a.Length == 0;
		var eb = b.Length == 0;
		if (ea && eb) return 0;
		if (ea) return 1;  // 空白靠后
		if (eb) return -1;

		if (double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var na)
			&& double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var nb))
			return na.CompareTo(nb);
		if (double.TryParse(a, NumberStyles.Any, CultureInfo.CurrentCulture, out na)
			&& double.TryParse(b, NumberStyles.Any, CultureInfo.CurrentCulture, out nb))
			return na.CompareTo(nb);

		if (tryparsedate(a, out var da) && tryparsedate(b, out var db))
			return da.CompareTo(db);

		return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
	}

	/// <summary>点击筛选三角：已打开同列则关，否则打开（切换）。</summary>
	void togglefilterpopup(int col, WpfPoint local) {
		if (!canfiltercol(col)) return;
		if (filterPopup != null && filterPopup.IsOpen && filterPopupCol == col) {
			closefilterpopup();
			return;
		}
		openfilterpopup(col, local);
	}

	void openfilterpopup(int col, WpfPoint local) {
		if (!canfiltercol(col)) return;
		closefilterpopup();
		filterPopupCol = col;

		if (!colFilters.TryGetValue(col, out var state) || state == null)
			state = new ColFilter();

		// 收集唯一值
		var uniques = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
		var dataR0 = filterHdrRow + 1;
		var dataR1 = filterR1 >= 0 ? filterR1 : rows - 1;
		for (var r = dataR0; r <= dataR1 && r < rows; r++) {
			if (r < freezeRows && r != filterHdrRow) continue;
			var t = cellobj(r, col).Text ?? "";
			if (uniques.Count < 500)
				uniques.Add(t);
		}

		var panel = new StackPanel { Margin = new Thickness(8), Width = 260 };
		panel.Children.Add(new TextBlock {
			Text = $"筛选 {colname(col)}",
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0, 0, 0, 6),
		});

		// 排序（有筛选数据区时，仿 Excel：升序 / 降序 / 取消排序）
		if (cansortrange()) {
			var sortColPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
			var bAsc = new Button {
				Content = "↑ 升序 (A→Z)",
				Padding = new Thickness(8, 4, 8, 4),
				Margin = new Thickness(0, 0, 0, 4),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				ToolTip = "按本列升序排列数据行",
			};
			var bDesc = new Button {
				Content = "↓ 降序 (Z→A)",
				Padding = new Thickness(8, 4, 8, 4),
				Margin = new Thickness(0, 0, 0, 4),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				ToolTip = "按本列降序排列数据行",
			};
			var bClearSort = new Button {
				Content = "✕ 取消排序",
				Padding = new Thickness(8, 4, 8, 4),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				ToolTip = "恢复排序前的原始行序",
				IsEnabled = sortCol >= 0 || sortBackupRows != null,
			};
			if (sortCol == col && sortAsc) bAsc.FontWeight = FontWeights.SemiBold;
			if (sortCol == col && !sortAsc) bDesc.FontWeight = FontWeights.SemiBold;
			bAsc.Click += (_, _) => {
				closefilterpopup();
				applysort(col, true);
			};
			bDesc.Click += (_, _) => {
				closefilterpopup();
				applysort(col, false);
			};
			bClearSort.Click += (_, _) => {
				closefilterpopup();
				clearsort();
			};
			sortColPanel.Children.Add(bAsc);
			sortColPanel.Children.Add(bDesc);
			sortColPanel.Children.Add(bClearSort);
			panel.Children.Add(sortColPanel);
			panel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 6) });
		}

		var eContains = new TextBox {
			Text = state.Contains ?? "",
			Margin = new Thickness(0, 0, 0, 6),
			ToolTip = "包含匹配（不区分大小写）",
		};
		panel.Children.Add(new TextBlock { Text = "包含：", FontSize = 11, Foreground = brushHdrText });
		panel.Children.Add(eContains);

		var hasDate = false;
		foreach (var u in uniques) {
			if (tryparsedate(u, out _)) { hasDate = true; break; }
		}
		DatePicker dpFrom = null, dpTo = null;
		if (hasDate) {
			panel.Children.Add(new TextBlock { Text = "日期从：", FontSize = 11, Foreground = brushHdrText, Margin = new Thickness(0, 4, 0, 0) });
			dpFrom = new DatePicker {
				SelectedDate = state.DateFrom,
				Margin = new Thickness(0, 0, 0, 4),
			};
			panel.Children.Add(dpFrom);
			panel.Children.Add(new TextBlock { Text = "日期到：", FontSize = 11, Foreground = brushHdrText });
			dpTo = new DatePicker {
				SelectedDate = state.DateTo,
				Margin = new Thickness(0, 0, 0, 6),
			};
			panel.Children.Add(dpTo);
		}

		panel.Children.Add(new TextBlock { Text = "值（勾选）：", FontSize = 11, Foreground = brushHdrText });
		var list = new ListBox {
			Height = 180,
			Margin = new Thickness(0, 0, 0, 6),
			SelectionMode = SelectionMode.Multiple,
		};
		// 全选 / 清空
		var btnsTop = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
		var bAll = new Button { Content = "全选", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 0) };
		var bNone = new Button { Content = "清空", Padding = new Thickness(6, 2, 6, 2) };
		btnsTop.Children.Add(bAll);
		btnsTop.Children.Add(bNone);
		panel.Children.Add(btnsTop);

		var checks = new List<CheckBox>();
		foreach (var u in uniques) {
			var label = string.IsNullOrEmpty(u) ? "(空白)" : u;
			var cb = new CheckBox {
				Content = label,
				Tag = u,
				IsChecked = state.SelectedValues == null || state.SelectedValues.Contains(u),
				Margin = new Thickness(0, 1, 0, 1),
			};
			checks.Add(cb);
			list.Items.Add(cb);
		}
		panel.Children.Add(list);
		bAll.Click += (_, _) => { foreach (var cb in checks) cb.IsChecked = true; };
		bNone.Click += (_, _) => { foreach (var cb in checks) cb.IsChecked = false; };

		var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		var bOk = new Button { Content = "应用", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
		var bClear = new Button { Content = "清除本列", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0) };
		var bCancel = new Button { Content = "取消", Padding = new Thickness(12, 4, 12, 4) };
		btns.Children.Add(bOk);
		btns.Children.Add(bClear);
		btns.Children.Add(bCancel);
		panel.Children.Add(btns);

		var border = new Border {
			Background = Brushes.White,
			BorderBrush = brushGrid,
			BorderThickness = new Thickness(1),
			Child = panel,
			Effect = null,
		};

		// StaysOpen=true：避免 MouseDown 打开后 MouseUp 被当成“外部点击”立刻关掉
		// 外部点击由 PreviewMouseDown 手动关闭；同列三角再次点击 toggle 关闭
		filterPopup = new Popup {
			Child = border,
			Placement = PlacementMode.RelativePoint,
			PlacementTarget = surface,
			HorizontalOffset = local.X,
			VerticalOffset = local.Y + 4,
			StaysOpen = true,
			AllowsTransparency = true,
			Focusable = false,
		};

		bOk.Click += (_, _) => {
			var nf = new ColFilter {
				Contains = eContains.Text?.Trim() ?? "",
				DateFrom = dpFrom?.SelectedDate,
				DateTo = dpTo?.SelectedDate,
			};
			var sel = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
			var allOn = true;
			foreach (var cb in checks) {
				if (cb.IsChecked == true)
					sel.Add(cb.Tag as string ?? "");
				else
					allOn = false;
			}
			// 全选且无其它条件 → 不激活
			if (!allOn)
				nf.SelectedValues = sel;
			else
				nf.SelectedValues = null;
			if (string.IsNullOrEmpty(nf.Contains) && nf.DateFrom == null && nf.DateTo == null
				&& nf.SelectedValues == null)
				colFilters.Remove(col);
			else
				colFilters[col] = nf;
			closefilterpopup();
			reapplyfilters();
		};
		bClear.Click += (_, _) => {
			colFilters.Remove(col);
			closefilterpopup();
			reapplyfilters();
		};
		bCancel.Click += (_, _) => closefilterpopup();

		filterPopup.Closed += (_, _) => {
			detachfilteroutside();
			if (filterPopupCol == col)
				filterPopupCol = -1;
		};

		// 延后打开 + 挂外部点击，避开本次 MouseDown 收尾导致的立刻关闭
		var openCol = col;
		Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
			if (filterPopup == null || filterPopupCol != openCol) return;
			try {
				filterPopup.IsOpen = true;
				attachfilteroutside();
			} catch { /* ignore */ }
		}));
	}

	void attachfilteroutside() {
		detachfilteroutside();
		// 窗口隧道 PreviewMouseDown：点 popup 外则关；筛选三角留给 toggle
		var win = Window.GetWindow(this) ?? Application.Current?.MainWindow;
		if (win == null) return;
		if (filterOutsideHandler == null)
			filterOutsideHandler = onfilteroutside;
		win.AddHandler(Mouse.PreviewMouseDownEvent, filterOutsideHandler, true);
		filterOutsideHooked = true;
	}

	void detachfilteroutside() {
		if (!filterOutsideHooked) return;
		filterOutsideHooked = false;
		try {
			var win = Window.GetWindow(this) ?? Application.Current?.MainWindow;
			if (win != null && filterOutsideHandler != null)
				win.RemoveHandler(Mouse.PreviewMouseDownEvent, filterOutsideHandler);
		} catch { /* ignore */ }
	}

	void onfilteroutside(object sender, MouseButtonEventArgs e) {
		if (filterPopup == null || !filterPopup.IsOpen) {
			detachfilteroutside();
			return;
		}
		// 点在 popup 内部 → 保留
		try {
			var fe = filterPopup.Child as FrameworkElement;
			if (fe != null) {
				var p = e.GetPosition(fe);
				if (p.X >= 0 && p.Y >= 0 && p.X <= fe.ActualWidth && p.Y <= fe.ActualHeight)
					return;
			}
		} catch { /* fall through */ }
		// 点在筛选三角上 → 交给 surface 的 toggle，此处不关
		try {
			var local = e.GetPosition(surface);
			if (tryhitfilterbtn(local, out _))
				return;
		} catch { /* ignore */ }

		closefilterpopup();
	}

	void closefilterpopup() {
		detachfilteroutside();
		if (filterPopup != null) {
			try { filterPopup.IsOpen = false; } catch { /* ignore */ }
			filterPopup = null;
		}
		filterPopupCol = -1;
	}

	static bool tryparsedate(string s, out DateTime dt) {
		dt = default;
		if (string.IsNullOrWhiteSpace(s)) return false;
		if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out dt))
			return true;
		if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt))
			return true;
		// Excel 序列日期
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa)
			&& oa > 200 && oa < 60000) {
			try {
				dt = DateTime.FromOADate(oa);
				return true;
			} catch { /* ignore */ }
		}
		return false;
	}

	/// <summary>单列筛选条件。</summary>
	sealed class ColFilter {
		/// <summary>null=全部值；否则仅允许集合内。</summary>
		public HashSet<string> SelectedValues;
		public string Contains = "";
		public DateTime? DateFrom, DateTo;

		public bool IsActive =>
			SelectedValues != null
			|| !string.IsNullOrEmpty(Contains)
			|| DateFrom != null || DateTo != null;

		public bool Match(string text) {
			text = text ?? "";
			if (!string.IsNullOrEmpty(Contains)
				&& text.IndexOf(Contains, StringComparison.CurrentCultureIgnoreCase) < 0)
				return false;
			if (DateFrom != null || DateTo != null) {
				if (!tryparsedate(text, out var dt)) return false;
				if (DateFrom != null && dt.Date < DateFrom.Value.Date) return false;
				if (DateTo != null && dt.Date > DateTo.Value.Date) return false;
			}
			if (SelectedValues != null && !SelectedValues.Contains(text))
				return false;
			return true;
		}
	}

	/// <summary>承载 OnRender 的表面，尺寸=整表，滚动由 ScrollViewer 负责。</summary>
	sealed class SheetSurface : FrameworkElement {
		readonly VirtualSheetGrid host;

		public SheetSurface(VirtualSheetGrid host) {
			this.host = host;
			Focusable = false;
		}

		protected override void OnRender(DrawingContext dc) {
			host.paint(dc);
		}
	}
}
