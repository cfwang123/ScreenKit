using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;
using ShapePath = System.Windows.Shapes.Path;

namespace ScreenKit.DocView;

/// <summary>
/// PDF 标注叠加层：笔迹 / 高亮 / 文字 / 注释 / 形状；
/// 支持选择、移动、缩放、删除、复制。坐标与 PdfEdit 一致（页 pt，左上 Y 下）。
/// </summary>
sealed class PdfAnnotSurface : Canvas {
	public enum Tool {
		/// <summary>手型：平移页面。</summary>
		Hand = 0,
		/// <summary>框选：选择/移动/调整标注。</summary>
		Select = 1,
		Pen = 2,
		Highlighter = 3,
		Text = 4,
		Note = 5,
		Rect = 6,
		Ellipse = 7,
		Line = 8,
		Arrow = 9,
		/// <summary>橡皮：擦除钢笔/荧光笔笔迹。</summary>
		Eraser = 10,
	}

	/// <summary>橡皮模式。</summary>
	public enum EraserMode {
		/// <summary>点擦：擦掉路径上的局部点，可拆成多段。</summary>
		Point = 0,
		/// <summary>整笔：碰到整条笔画即删除。</summary>
		Stroke = 1,
	}

	const double HANDLE = 7;
	const double MIN_SIZE = 8;
	const double NOTE_ICON_PT = 18;
	const double TEXT_MIN_W = 40;
	/// <summary>点擦默认半径（页 pt）。</summary>
	const double ERASER_R_PT = 5.5;
	/// <summary>整笔命中额外余量（页 pt）。</summary>
	const double ERASER_STROKE_PAD = 3.0;

	readonly PdfAnnotDoc doc;
	readonly Dictionary<string, FrameworkElement> hosts = new();
	readonly Dictionary<string, Popup> notePopups = new();
	/// <summary>当前为「点外关闭」挂接的窗口与处理（防止泄漏）。</summary>
	Window noteOutsideWin;
	MouseButtonEventHandler noteOutsideHandler;
	string noteOutsideId;
	readonly Canvas handleLayer = new() { IsHitTestVisible = true };
	readonly List<Rectangle> handles = new();
	/// <summary>多选/成组时的统一外框（单项选中不画此框，用各自 chrome）。</summary>
	Rectangle selFrame;

	Func<int, (double Left, double Top, double W, double H)> pageLayout;
	Func<int, (double PtW, double PtH)> pageSizePt;
	Func<WpfPoint, int> hitPage;
	ScrollViewer scroller;

	Tool tool = Tool.Select;
	bool editMode;

	// 多选拖动 / 缩放
	bool dragging;
	int resizeHandle = -1; // 0..7；-1 移动
	WpfPoint dragStart;
	readonly List<DragSnap> dragSnaps = new();
	double groupOrigX, groupOrigY, groupOrigW, groupOrigH;
	bool noteClickCandidate;
	WpfPoint noteClickStart;

	// 框选橡皮筋
	bool marquee;
	WpfPoint marqueeStart;
	Rectangle marqueeVisual;

	// 手型平移
	bool panning;
	WpfPoint panStart;
	double panOffX, panOffY;

	// 正在绘制
	bool drawing;
	PdfAnnotItem draft;
	FrameworkElement draftVisual;
	WpfPoint drawStart;

	// 橡皮
	bool erasing;
	EraserMode eraserMode = EraserMode.Point;
	/// <summary>整笔模式本拖中已删过的 id，避免重复处理。</summary>
	readonly HashSet<string> erasedStrokeIds = new();
	Ellipse eraserCursor;

	// 复制缓冲（可多条）
	List<PdfAnnotItem> clipboard;

	public EraserMode CurrentEraserMode {
		get => eraserMode;
		set => eraserMode = value;
	}
	public double EraserRadiusPt { get; set; } = ERASER_R_PT;

	sealed class DragSnap {
		public PdfAnnotItem It;
		public double X, Y, W, H, X2, Y2;
		public List<PdfAnnotPt> Pts;
	}

	public MediaColor DefaultColor = MediaColor.FromRgb(0xE5, 0x39, 0x35);
	public MediaColor HighlightColor = MediaColor.FromArgb(0x90, 0xFF, 0xEB, 0x3B);
	public double DefaultStroke = 1.8;
	public double HighlightStroke = 12;
	public string DefaultFont = "Microsoft YaHei";
	public double DefaultFontSize = 12;

	public PdfAnnotDoc Doc => doc;
	public event Action Changed;
	public event Action SelectionChanged;
	public event Action ToolChanged;

	public Tool CurrentTool {
		get => tool;
		set {
			if (tool == value) return;
			tool = value;
			canceldraft();
			closenotepopups();
			applycursorandhit();
			try { ToolChanged?.Invoke(); } catch { /* ignore */ }
		}
	}

	public bool EditMode {
		get => editMode;
		set {
			if (editMode == value) return;
			editMode = value;
			// 标注模式：半透明命中层；预览模式：Background=null 空白穿透，仅注释气泡可点
			Background = editMode
				? new SolidColorBrush(MediaColor.FromArgb(1, 0, 0, 0))
				: null;
			if (!editMode) {
				canceldraft();
				endpan();
				// 预览仍可点注释，不强制关 flyout（用户可能在看）
				doc.DeselectAll();
				refreshchrome();
				updatehandles();
			}
			applycursorandhit();
		}
	}

	public PdfAnnotItem Selected => doc.SelectedItem;

	public PdfAnnotSurface(PdfAnnotDoc annotDoc) {
		doc = annotDoc ?? new PdfAnnotDoc();
		Background = null;
		IsHitTestVisible = false; // 仅标注模式拦截
		SnapsToDevicePixels = true;
		Focusable = true;
		ClipToBounds = false;

		Children.Add(handleLayer);
		Panel.SetZIndex(handleLayer, 1000);

		selFrame = new Rectangle {
			Stroke = new SolidColorBrush(MediaColor.FromRgb(0x25, 0x63, 0xEB)),
			StrokeThickness = 1.5,
			StrokeDashArray = new DoubleCollection { 4, 2 },
			Fill = new SolidColorBrush(MediaColor.FromArgb(0x12, 0x25, 0x63, 0xEB)),
			Visibility = Visibility.Collapsed,
			IsHitTestVisible = false,
		};
		handleLayer.Children.Add(selFrame);

		for (var i = 0; i < 8; i++) {
			var h = new Rectangle {
				Width = HANDLE, Height = HANDLE,
				Fill = new SolidColorBrush(MediaColor.FromRgb(0x25, 0x63, 0xEB)),
				Stroke = WpfBrushes.White,
				StrokeThickness = 1,
				Visibility = Visibility.Collapsed,
				Tag = i,
				Cursor = handlecursor(i),
			};
			h.MouseLeftButtonDown += onhandledown;
			handles.Add(h);
			handleLayer.Children.Add(h);
		}

		marqueeVisual = new Rectangle {
			Stroke = new SolidColorBrush(MediaColor.FromRgb(0x25, 0x63, 0xEB)),
			StrokeThickness = 1,
			StrokeDashArray = new DoubleCollection { 3, 2 },
			Fill = new SolidColorBrush(MediaColor.FromArgb(0x28, 0x25, 0x63, 0xEB)),
			Visibility = Visibility.Collapsed,
			IsHitTestVisible = false,
		};
		handleLayer.Children.Add(marqueeVisual);

		eraserCursor = new Ellipse {
			Stroke = new SolidColorBrush(MediaColor.FromRgb(0x6B, 0x72, 0x80)),
			StrokeThickness = 1.2,
			StrokeDashArray = new DoubleCollection { 2, 2 },
			Fill = new SolidColorBrush(MediaColor.FromArgb(0x22, 0x9C, 0xA3, 0xAF)),
			Visibility = Visibility.Collapsed,
			IsHitTestVisible = false,
		};
		handleLayer.Children.Add(eraserCursor);

		// Preview 优先：保证空白处能开始框选（不被其它逻辑抢走）
		PreviewMouseLeftButtonDown += onpreviewdown;
		MouseLeftButtonDown += ondown;
		MouseMove += onmove;
		MouseLeftButtonUp += onup;
		LostMouseCapture += onlostcapture;
		KeyDown += onkey;
	}

	public void SetLayout(
		Func<int, (double Left, double Top, double W, double H)> layout,
		Func<int, (double PtW, double PtH)> sizePt,
		Func<WpfPoint, int> pageAt,
		ScrollViewer scroll = null) {
		pageLayout = layout;
		pageSizePt = sizePt;
		hitPage = pageAt;
		scroller = scroll;
	}

	void applycursorandhit() {
		// 预览模式：父级可命中（否则子级也不收事件），Background=null 空白穿透
		// 仅注释气泡 IsHitTestVisible，便于点开 flyout
		if (!editMode) {
			IsHitTestVisible = true;
			Cursor = WpfCursors.Arrow;
			foreach (var kv in hosts) {
				var it = doc.Find(kv.Key);
				kv.Value.IsHitTestVisible = it?.Kind == PdfAnnotKind.Note;
				if (kv.Value is Border nb)
					nb.Cursor = it?.Kind == PdfAnnotKind.Note ? WpfCursors.Hand : WpfCursors.Arrow;
			}
			handleLayer.IsHitTestVisible = false;
			return;
		}
		IsHitTestVisible = true;
		Cursor = tool switch {
			Tool.Hand => WpfCursors.Hand,
			Tool.Select => WpfCursors.Arrow,
			Tool.Eraser => WpfCursors.None, // 用自定义圈
			_ => WpfCursors.Cross,
		};
		if (tool != Tool.Eraser && eraserCursor != null)
			eraserCursor.Visibility = Visibility.Collapsed;
		// 手型：绝不命中标注，只 pan
		// 框选：命中全部
		// 文本工具：仅命中文本（点击编辑）
		// 注释工具：仅命中注释气泡
		// 橡皮/绘制：穿透，由表面逻辑处理
		foreach (var kv in hosts) {
			var it = doc.Find(kv.Key);
			var hit = false;
			if (tool == Tool.Select) hit = true;
			else if (tool == Tool.Text && it?.Kind == PdfAnnotKind.Text) hit = true;
			else if (tool == Tool.Note && it?.Kind == PdfAnnotKind.Note) hit = true;
			kv.Value.IsHitTestVisible = hit;
			// 框选：文字只读且不命中 TextBox，点在外框上才能拖/缩放；编辑仅文本工具
			if (kv.Value is Border bd && bd.Child is TextBox tb) {
				var edit = tool == Tool.Text;
				tb.IsReadOnly = !edit;
				tb.IsHitTestVisible = edit;
				tb.Focusable = edit;
				tb.Cursor = edit ? WpfCursors.IBeam : WpfCursors.SizeAll;
				if (!edit && tb.IsKeyboardFocused) {
					try { Keyboard.ClearFocus(); } catch { /* ignore */ }
				}
			}
		}
		handleLayer.IsHitTestVisible = tool == Tool.Select || tool == Tool.Eraser;
	}

	void beginpan(MouseButtonEventArgs e) {
		if (scroller == null) return;
		panning = true;
		panStart = e.GetPosition(scroller);
		panOffX = scroller.HorizontalOffset;
		panOffY = scroller.VerticalOffset;
		CaptureMouse();
		Cursor = WpfCursors.ScrollAll;
	}

	void dopan(MouseEventArgs e) {
		if (!panning || scroller == null) return;
		var pt = e.GetPosition(scroller);
		scroller.ScrollToHorizontalOffset(Math.Max(0, panOffX - (pt.X - panStart.X)));
		scroller.ScrollToVerticalOffset(Math.Max(0, panOffY - (pt.Y - panStart.Y)));
	}

	void endpan() {
		if (!panning) return;
		panning = false;
		try { ReleaseMouseCapture(); } catch { /* ignore */ }
		applycursorandhit();
	}

	void closenotepopups(string exceptId = null) {
		foreach (var kv in notePopups) {
			if (exceptId != null && kv.Key == exceptId) continue;
			try { kv.Value.IsOpen = false; } catch { /* ignore */ }
		}
		// 若关闭的是正在监听外点的那个，卸掉钩子
		if (exceptId == null || (noteOutsideId != null && noteOutsideId != exceptId))
			detachnoteoutsideclose();
	}

	void detachnoteoutsideclose() {
		if (noteOutsideWin != null && noteOutsideHandler != null) {
			try { noteOutsideWin.PreviewMouseDown -= noteOutsideHandler; } catch { /* ignore */ }
		}
		try {
			if (noteOutsideWin != null)
				noteOutsideWin.PreviewMouseWheel -= onnotewheelclose;
		} catch { /* ignore */ }
		try {
			if (scroller != null) {
				scroller.ScrollChanged -= onnotescrollclose;
				scroller.PreviewMouseWheel -= onnotewheelclose;
			}
		} catch { /* ignore */ }
		noteOutsideWin = null;
		noteOutsideHandler = null;
		noteOutsideId = null;
	}

	/// <summary>弹出后：点击 flyout/气泡外任意处自动关闭；滚动/滚轮也关。</summary>
	void armnoteoutsideclose(Popup pop, string noteId) {
		detachnoteoutsideclose();
		if (pop == null || string.IsNullOrEmpty(noteId)) return;
		noteOutsideId = noteId;
		// 延后一拍，避免打开时同一次点击立刻关掉
		try {
			Dispatcher.BeginInvoke(new Action(() => {
				if (pop == null || !pop.IsOpen || noteOutsideId != noteId) return;
				var win = Window.GetWindow(this);
				if (win == null) return;
				MouseButtonEventHandler handler = null;
				handler = (_, e) => {
					if (noteOutsideId != noteId || pop == null || !pop.IsOpen) {
						detachnoteoutsideclose();
						return;
					}
					var src = e.OriginalSource as DependencyObject;
					// 点在 flyout 内：不关
					if (pop.Child != null && isvisualdescendant(pop.Child, src))
						return;
					// 点在本气泡上：不关（便于再点/拖）；换别的气泡由 open 路径关
					if (hosts.TryGetValue(noteId, out var host) && isvisualdescendant(host, src))
						return;
					try { pop.IsOpen = false; } catch { /* ignore */ }
					detachnoteoutsideclose();
				};
				noteOutsideHandler = handler;
				noteOutsideWin = win;
				win.PreviewMouseDown += handler;
				// 滚动条拖动 / 程序滚动
				try {
					if (scroller != null) {
						scroller.ScrollChanged -= onnotescrollclose;
						scroller.ScrollChanged += onnotescrollclose;
						scroller.PreviewMouseWheel -= onnotewheelclose;
						scroller.PreviewMouseWheel += onnotewheelclose;
					}
				} catch { /* ignore */ }
				// 窗口级滚轮（焦点在别处时也能关）
				try {
					win.PreviewMouseWheel -= onnotewheelclose;
					win.PreviewMouseWheel += onnotewheelclose;
				} catch { /* ignore */ }
			}), DispatcherPriority.Input);
		} catch { /* ignore */ }
	}

	void onnotescrollclose(object sender, ScrollChangedEventArgs e) {
		// 用户滚动或视口变化；忽略纯布局 extent 抖动里 offset 未变的情况
		var moved = Math.Abs(e.VerticalChange) >= 0.5 || Math.Abs(e.HorizontalChange) >= 0.5;
		if (!moved) return;
		closenoteflyoutsonscroll();
	}

	void onnotewheelclose(object sender, MouseWheelEventArgs e) {
		// 滚轮一动即关（含 Ctrl+滚轮缩放）
		closenoteflyoutsonscroll();
	}

	void closenoteflyoutsonscroll() {
		var anyOpen = false;
		foreach (var kv in notePopups) {
			if (kv.Value != null && kv.Value.IsOpen) { anyOpen = true; break; }
		}
		if (!anyOpen) return;
		closenotepopups();
	}

	static bool isvisualdescendant(DependencyObject root, DependencyObject node) {
		if (root == null || node == null) return false;
		var d = node;
		while (d != null) {
			if (ReferenceEquals(d, root)) return true;
			// Popup 内容在独立视觉树，用 LogicalTree 兜底
			if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) {
				try { d = VisualTreeHelper.GetParent(d); }
				catch { d = LogicalTreeHelper.GetParent(d); }
			} else {
				d = LogicalTreeHelper.GetParent(d);
			}
		}
		return false;
	}

	/// <summary>应用字体到选中文字/注释（及默认值）。</summary>
	public void ApplyFont(string name) {
		if (string.IsNullOrWhiteSpace(name)) return;
		DefaultFont = name.Trim();
		var it = doc.SelectedItem;
		if (it == null || it.Kind is not (PdfAnnotKind.Text or PdfAnnotKind.Note)) return;
		it.FontName = DefaultFont;
		refreshhost(it);
		placehost(it);
		if (it.Kind == PdfAnnotKind.Text) fittextheight(it);
		markdirty();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void ApplyFontSize(double pt) {
		if (pt < 6) pt = 6;
		if (pt > 96) pt = 96;
		DefaultFontSize = pt;
		var it = doc.SelectedItem;
		if (it == null || it.Kind is not (PdfAnnotKind.Text or PdfAnnotKind.Note)) return;
		it.FontSize = pt;
		refreshhost(it);
		placehost(it);
		if (it.Kind == PdfAnnotKind.Text) fittextheight(it);
		markdirty();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void Relayout() {
		// 缩放/滚动布局：只重放位置，禁止 fittextlayout（会 Measure 卡住 UI）
		var n = 0;
		foreach (var it in doc.Items) {
			placehost(it, refitText: false);
			// 诊断：前 2 条写位置，便于对照页槽 zoomdiag
			if (n < 2 && pageLayout != null) {
				try {
					var (left, top, pw, ph) = pageLayout(it.Page);
					if (hosts.TryGetValue(it.Id, out var el)) {
						DocViewLog.Info(
							$"annotRelayout kind={it.KindName} p={it.Page} " +
							$"host=({Canvas.GetLeft(el):F1},{Canvas.GetTop(el):F1},{el.Width:F0}x{el.Height:F0}) " +
							$"pageBox=({left:F0},{top:F0},{pw:F0}x{ph:F0}) pt=({it.X:F1},{it.Y:F1},{it.W:F1}x{it.H:F1})");
					}
				} catch { /* ignore */ }
			}
			n++;
		}
		updatehandles();
	}

	public void RebuildAll() {
		closenotepopups();
		notePopups.Clear();
		// 保留 handleLayer
		var keep = handleLayer;
		Children.Clear();
		hosts.Clear();
		Children.Add(keep);
		Panel.SetZIndex(keep, 1000);
		foreach (var it in doc.Items)
			ensurehost(it);
		Relayout();
		applycursorandhit();
	}

	public void ClearSelection() {
		doc.DeselectAll();
		refreshchrome();
		updatehandles();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void DeleteSelected() {
		var list = doc.SelectedItems;
		if (list.Count == 0) return;
		foreach (var it in list) {
			if (notePopups.TryGetValue(it.Id, out var pop)) {
				try { pop.IsOpen = false; } catch { /* ignore */ }
				notePopups.Remove(it.Id);
			}
			doc.Items.Remove(it);
			if (hosts.TryGetValue(it.Id, out var el)) {
				Children.Remove(el);
				hosts.Remove(it.Id);
			}
		}
		updatehandles();
		markdirty();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void CopySelected() {
		var list = doc.SelectedItems;
		if (list.Count == 0) return;
		clipboard = new List<PdfAnnotItem>();
		foreach (var it in list)
			clipboard.Add(it.Clone(newId: true));
	}

	public void PasteClipboard() {
		if (clipboard == null || clipboard.Count == 0) return;
		doc.DeselectAll();
		var nextClip = new List<PdfAnnotItem>();
		foreach (var src in clipboard) {
			var it = src.Clone(newId: true);
			it.X += 12;
			it.Y += 12;
			if (it.Kind is PdfAnnotKind.Line or PdfAnnotKind.Arrow) {
				it.X2 += 12;
				it.Y2 += 12;
			}
			if (it.Points != null) {
				foreach (var p in it.Points) {
					p.X += 12;
					p.Y += 12;
				}
				it.RecalcBoundsFromPoints();
			}
			it.Selected = true;
			doc.Items.Add(it);
			ensurehost(it);
			placehost(it);
			nextClip.Add(it.Clone(newId: true));
		}
		clipboard = nextClip;
		applycursorandhit();
		updatehandles();
		markdirty();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void DuplicateSelected() {
		CopySelected();
		PasteClipboard();
	}

	/// <summary>将当前多选标注成组。</summary>
	public void GroupSelected() {
		var list = doc.SelectedItems;
		if (list.Count < 2) return;
		var gid = Guid.NewGuid().ToString("N");
		foreach (var it in list)
			it.GroupId = gid;
		markdirty();
		updatehandles();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	/// <summary>解散选中项所在组。</summary>
	public void UngroupSelected() {
		var list = doc.SelectedItems;
		if (list.Count == 0) return;
		var gids = new HashSet<string>();
		foreach (var it in list)
			if (!string.IsNullOrEmpty(it.GroupId)) gids.Add(it.GroupId);
		if (gids.Count == 0) {
			foreach (var it in list) it.GroupId = "";
		} else {
			foreach (var it in doc.Items)
				if (!string.IsNullOrEmpty(it.GroupId) && gids.Contains(it.GroupId))
					it.GroupId = "";
		}
		markdirty();
		updatehandles();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void SetColor(MediaColor c) {
		DefaultColor = c;
		var any = false;
		foreach (var it in doc.SelectedItems) {
			if (it.Kind == PdfAnnotKind.Highlight)
				it.Color = MediaColor.FromArgb(0x90, c.R, c.G, c.B);
			else
				it.Color = c;
			refreshhost(it);
			placehost(it);
			if (it.Kind == PdfAnnotKind.Text) fittextlayout(it);
			any = true;
		}
		if (any) markdirty();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	void markdirty() {
		doc.Dirty = true;
		try { Changed?.Invoke(); } catch { /* ignore */ }
	}

	// ---------- 创建 host ----------

	void ensurehost(PdfAnnotItem it) {
		if (hosts.ContainsKey(it.Id)) {
			refreshhost(it);
			return;
		}
		var el = buildvisual(it);
		el.Tag = it.Id;
		// 预览可点注释；标注模式由 applycursorandhit 细化
		el.IsHitTestVisible = editMode || it.Kind == PdfAnnotKind.Note;
		el.MouseLeftButtonDown += onhostdown;
		Children.Add(el);
		hosts[it.Id] = el;
		refreshchrome();
		// 父层也需可命中，否则预览点不到注释
		if (!editMode && it.Kind == PdfAnnotKind.Note)
			IsHitTestVisible = true;
	}

	FrameworkElement buildvisual(PdfAnnotItem it) {
		switch (it.Kind) {
			case PdfAnnotKind.Ink:
			case PdfAnnotKind.Highlight:
				return buildpathhost(it);
			case PdfAnnotKind.Line:
			case PdfAnnotKind.Arrow:
				return buildlinehost(it);
			case PdfAnnotKind.Rect:
			case PdfAnnotKind.Ellipse:
				return buildshapehost(it);
			case PdfAnnotKind.Note:
				return buildnotehost(it);
			default:
				return buildtexthost(it);
		}
	}

	Border buildpathhost(PdfAnnotItem it) {
		var path = new ShapePath {
			Stroke = makebrush(it),
			StrokeThickness = it.Kind == PdfAnnotKind.Highlight
				? Math.Max(6, it.Stroke)
				: Math.Max(0.8, it.Stroke),
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			StrokeLineJoin = PenLineJoin.Round,
			Fill = WpfBrushes.Transparent,
			Data = buildgeometry(it),
			IsHitTestVisible = false,
		};
		if (it.Kind == PdfAnnotKind.Highlight)
			path.Opacity = it.Opacity > 0 && it.Opacity < 1 ? it.Opacity : 0.55;
		var bd = new Border {
			Child = path,
			Background = new SolidColorBrush(MediaColor.FromArgb(1, 0, 0, 0)),
			BorderBrush = WpfBrushes.Transparent,
			BorderThickness = new Thickness(1),
			Cursor = WpfCursors.SizeAll,
			Tag = it.Id,
		};
		return bd;
	}

	Border buildlinehost(PdfAnnotItem it) {
		var canvas = new Canvas { IsHitTestVisible = false };
		var line = new Line {
			Stroke = makebrush(it),
			StrokeThickness = Math.Max(0.8, it.Stroke),
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
		};
		canvas.Children.Add(line);
		if (it.Kind == PdfAnnotKind.Arrow) {
			var head = new Polygon {
				Fill = makebrush(it),
				Stroke = makebrush(it),
				StrokeThickness = 0.5,
			};
			canvas.Children.Add(head);
		}
		var bd = new Border {
			Child = canvas,
			Background = new SolidColorBrush(MediaColor.FromArgb(1, 0, 0, 0)),
			BorderBrush = WpfBrushes.Transparent,
			BorderThickness = new Thickness(1),
			Cursor = WpfCursors.SizeAll,
			Tag = it.Id,
		};
		return bd;
	}

	Border buildshapehost(PdfAnnotItem it) {
		Shape sh = it.Kind == PdfAnnotKind.Ellipse
			? new Ellipse()
			: new Rectangle();
		sh.Stroke = makebrush(it);
		sh.StrokeThickness = Math.Max(0.8, it.Stroke);
		sh.Fill = new SolidColorBrush(MediaColor.FromArgb(0x28, it.Color.R, it.Color.G, it.Color.B));
		sh.IsHitTestVisible = false;
		return new Border {
			Child = sh,
			Background = new SolidColorBrush(MediaColor.FromArgb(1, 0, 0, 0)),
			BorderBrush = WpfBrushes.Transparent,
			BorderThickness = new Thickness(1),
			Cursor = WpfCursors.SizeAll,
			Tag = it.Id,
		};
	}

	Border buildtexthost(PdfAnnotItem it) {
		var tb = new TextBox {
			Text = it.Text ?? "",
			FontSize = Math.Max(9, it.FontSize * 96.0 / 72.0),
			FontFamily = new FontFamily(string.IsNullOrWhiteSpace(it.FontName)
				? "Microsoft YaHei UI" : it.FontName),
			Foreground = makebrush(it),
			Background = WpfBrushes.Transparent,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(4, 2, 4, 2),
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			IsReadOnly = tool != Tool.Text,
			// 框选时不命中 TextBox，避免抢焦点进编辑；命中落在外框上可拖
			IsHitTestVisible = tool == Tool.Text,
			Focusable = tool == Tool.Text,
			Cursor = tool == Tool.Text ? WpfCursors.IBeam : WpfCursors.SizeAll,
			Tag = it.Id,
		};
		tb.TextChanged += (_, _) => {
			if (tb.Tag is not string id) return;
			var item = doc.Find(id);
			if (item == null) return;
			if (item.Text != tb.Text) {
				item.Text = tb.Text;
				markdirty();
			}
			// 自动变宽或固定宽 + 高度自适应（不改字号）
			fittextlayout(item, tb);
			placehost(item);
			updatehandles();
		};
		return new Border {
			Child = tb,
			Background = new SolidColorBrush(MediaColor.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
			BorderBrush = WpfBrushes.Transparent,
			BorderThickness = new Thickness(1),
			Cursor = WpfCursors.SizeAll,
			Tag = it.Id,
			MinHeight = 18,
		};
	}

	/// <summary>注释：页面上只显示小气泡；点击弹出 flyout 编辑正文。</summary>
	Border buildnotehost(PdfAnnotItem it) {
		var icon = new TextBlock {
			Text = "💬",
			FontSize = 13,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false,
		};
		var bd = new Border {
			Child = icon,
			Width = 26,
			Height = 26,
			CornerRadius = new CornerRadius(13),
			Background = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xF5, 0x9D)),
			BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0xF9, 0xA8, 0x25)),
			BorderThickness = new Thickness(1.5),
			Cursor = WpfCursors.Hand,
			Tag = it.Id,
			ToolTip = string.IsNullOrWhiteSpace(it.Text) ? "注释（点击查看）" : it.Text,
		};

		// flyout
		var tb = new TextBox {
			Text = it.Text ?? "",
			Width = 220,
			MinHeight = 72,
			MaxHeight = 220,
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			FontSize = Math.Max(11, it.FontSize * 96.0 / 72.0),
			FontFamily = new FontFamily(string.IsNullOrWhiteSpace(it.FontName)
				? "Microsoft YaHei UI" : it.FontName),
			Foreground = new SolidColorBrush(MediaColor.FromRgb(0x11, 0x18, 0x27)),
			Background = WpfBrushes.White,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(6),
			Tag = it.Id,
		};
		tb.TextChanged += (_, _) => {
			if (tb.Tag is string id) {
				var item = doc.Find(id);
				if (item != null && item.Text != tb.Text) {
					item.Text = tb.Text;
					bd.ToolTip = string.IsNullOrWhiteSpace(item.Text) ? "注释（点击查看）" : item.Text;
					markdirty();
				}
			}
		};
		var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
		var title = new TextBlock {
			Text = "注释",
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(MediaColor.FromRgb(0x37, 0x41, 0x51)),
		};
		var bclose = new Button {
			Content = "×", Width = 22, Height = 20, Padding = new Thickness(0),
			Background = WpfBrushes.Transparent, BorderThickness = new Thickness(0),
			Cursor = WpfCursors.Hand, HorizontalAlignment = HorizontalAlignment.Right,
		};
		DockPanel.SetDock(bclose, Dock.Right);
		head.Children.Add(bclose);
		head.Children.Add(title);
		var body = new DockPanel();
		DockPanel.SetDock(head, Dock.Top);
		body.Children.Add(head);
		body.Children.Add(tb);
		var fly = new Border {
			Child = body,
			Background = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xF8, 0xE1)),
			BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0xF9, 0xA8, 0x25)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(6),
			Padding = new Thickness(8),
			Effect = new System.Windows.Media.Effects.DropShadowEffect {
				BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35,
				Color = MediaColor.FromRgb(0, 0, 0),
			},
		};
		var popup = new Popup {
			PlacementTarget = bd,
			Placement = PlacementMode.Right,
			HorizontalOffset = 6,
			VerticalOffset = -4,
			// 由 armnoteoutsideclose 管「点外关闭」；StaysOpen=true 避免与捕获/叠加层冲突
			StaysOpen = true,
			AllowsTransparency = true,
			PopupAnimation = PopupAnimation.Fade,
			Child = fly,
			Focusable = false,
		};
		bclose.Click += (_, _) => {
			popup.IsOpen = false;
			detachnoteoutsideclose();
		};
		popup.Closed += (_, _) => {
			if (noteOutsideId == it.Id)
				detachnoteoutsideclose();
		};
		notePopups[it.Id] = popup;
		return bd;
	}

	/// <summary>
	/// 文本框布局：字号不变。
	/// AutoWidth=true 时随内容加宽，直至页右缘再换行；
	/// AutoWidth=false 时固定 W，高度随换行自适应。
	/// </summary>
	void fittextlayout(PdfAnnotItem it, TextBox tb = null) {
		if (it == null || it.Kind != PdfAnnotKind.Text) return;
		if (pageLayout == null || pageSizePt == null) return;
		var (_, _, pw, ph) = pageLayout(it.Page);
		var (ptW, ptH) = pageSizePt(it.Page);
		if (ptW < 1 || ptH < 1 || pw < 1 || ph < 1) return;
		var sx = pw / ptW;
		var sy = ph / ptH;
		var maxWpt = Math.Max(TEXT_MIN_W, ptW - it.X - 6);

		if (tb == null && hosts.TryGetValue(it.Id, out var el) && el is Border b && b.Child is TextBox t)
			tb = t;

		var text = string.IsNullOrEmpty(it.Text) ? " " : it.Text;
		var fsDip = Math.Max(9, it.FontSize * 96.0 / 72.0 * Math.Min(sx, sy));
		double preferWpt;

		try {
			var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
			var fam = string.IsNullOrWhiteSpace(it.FontName) ? "Microsoft YaHei UI" : it.FontName;
			var ft = new FormattedText(
				text.Replace("\r\n", "\n").Replace('\r', '\n'),
				CultureInfo.CurrentUICulture,
				FlowDirection.LeftToRight,
				new Typeface(new FontFamily(fam), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
				fsDip, makebrush(it), dpi);
			// 不限宽测自然宽度
			preferWpt = (ft.WidthIncludingTrailingWhitespace + 12) / Math.Max(1e-6, sx);
		} catch {
			preferWpt = Math.Max(TEXT_MIN_W, (text.Length + 1) * it.FontSize * 0.55);
		}

		if (it.AutoWidth) {
			it.W = Math.Min(maxWpt, Math.Max(TEXT_MIN_W, preferWpt));
		} else {
			if (it.W < TEXT_MIN_W) it.W = TEXT_MIN_W;
			if (it.W > maxWpt) it.W = maxWpt;
		}

		var dipW = Math.Max(24, it.W * sx);
		if (tb != null) {
			tb.TextWrapping = TextWrapping.Wrap;
			tb.Width = dipW;
			tb.Measure(new Size(dipW, double.PositiveInfinity));
			var hDip = Math.Max(18, tb.DesiredSize.Height + 4);
			it.H = hDip / Math.Max(1e-6, sy);
		} else {
			try {
				var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
				var fam = string.IsNullOrWhiteSpace(it.FontName) ? "Microsoft YaHei UI" : it.FontName;
				var ft = new FormattedText(
					text.Replace("\r\n", "\n").Replace('\r', '\n'),
					CultureInfo.CurrentUICulture,
					FlowDirection.LeftToRight,
					new Typeface(new FontFamily(fam), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
					fsDip, makebrush(it), dpi);
				ft.MaxTextWidth = Math.Max(8, dipW - 8);
				it.H = Math.Max(14, (ft.Height + 8) / Math.Max(1e-6, sy));
			} catch {
				it.H = Math.Max(14, it.FontSize * 1.6);
			}
		}
	}

	// 兼容旧调用名
	void fittextheight(PdfAnnotItem it, TextBox tb = null) => fittextlayout(it, tb);

	static Brush makebrush(PdfAnnotItem it) {
		var c = it.Color;
		if (it.Opacity > 0 && it.Opacity < 1 && it.Kind != PdfAnnotKind.Highlight)
			c = MediaColor.FromArgb((byte)(255 * it.Opacity), c.R, c.G, c.B);
		return new SolidColorBrush(c);
	}

	static Geometry buildgeometry(PdfAnnotItem it) {
		if (it.Points == null || it.Points.Count == 0)
			return Geometry.Empty;
		var geo = new StreamGeometry();
		using (var ctx = geo.Open()) {
			var first = true;
			foreach (var p in it.Points) {
				// 相对包围盒
				var lx = p.X - it.X;
				var ly = p.Y - it.Y;
				if (first) {
					ctx.BeginFigure(new WpfPoint(lx, ly), false, false);
					first = false;
				} else {
					ctx.LineTo(new WpfPoint(lx, ly), true, false);
				}
			}
		}
		geo.Freeze();
		return geo;
	}

	void refreshhost(PdfAnnotItem it) {
		if (!hosts.TryGetValue(it.Id, out var el)) return;
		// 简单：重建视觉
		var idx = Children.IndexOf(el);
		Children.Remove(el);
		hosts.Remove(it.Id);
		var neu = buildvisual(it);
		neu.Tag = it.Id;
		neu.IsHitTestVisible = editMode;
		neu.MouseLeftButtonDown += onhostdown;
		if (idx >= 0 && idx <= Children.Count)
			Children.Insert(idx, neu);
		else
			Children.Add(neu);
		hosts[it.Id] = neu;
		refreshchrome();
	}

	void refreshchrome() {
		// 多选或成组选中：单项不画选中框，只靠外框 selFrame
		var multi = doc.SelectedCount > 1;
		foreach (var kv in hosts) {
			var it = doc.Find(kv.Key);
			if (it == null || kv.Value is not Border b) continue;
			var showItemChrome = it.Selected && !multi;
			if (it.Kind == PdfAnnotKind.Note) {
				b.BorderBrush = showItemChrome
					? new SolidColorBrush(MediaColor.FromRgb(0x25, 0x63, 0xEB))
					: new SolidColorBrush(MediaColor.FromRgb(0xF9, 0xA8, 0x25));
				b.BorderThickness = new Thickness(showItemChrome ? 2.2 : 1.5);
				b.Background = showItemChrome
					? new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xEC, 0xB3))
					: new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xF5, 0x9D));
			} else {
				b.BorderBrush = showItemChrome
					? new SolidColorBrush(MediaColor.FromRgb(0x25, 0x63, 0xEB))
					: WpfBrushes.Transparent;
				b.BorderThickness = new Thickness(showItemChrome ? 1.5 : 1);
			}
		}
	}

	void placehost(PdfAnnotItem it, bool refitText = true) {
		if (pageLayout == null || pageSizePt == null) return;
		if (!hosts.TryGetValue(it.Id, out var el)) return;
		var (left, top, pw, ph) = pageLayout(it.Page);
		var (ptW, ptH) = pageSizePt(it.Page);
		if (ptW < 1) ptW = 1;
		if (ptH < 1) ptH = 1;
		var sx = pw / ptW;
		var sy = ph / ptH;

		if (it.Kind is PdfAnnotKind.Line or PdfAnnotKind.Arrow) {
			var minX = Math.Min(it.X, it.X2);
			var minY = Math.Min(it.Y, it.Y2);
			var maxX = Math.Max(it.X, it.X2);
			var maxY = Math.Max(it.Y, it.Y2);
			var pad = Math.Max(6, it.Stroke * 2);
			var boxL = minX - pad;
			var boxT = minY - pad;
			var boxW = Math.Max(MIN_SIZE, maxX - minX + pad * 2);
			var boxH = Math.Max(MIN_SIZE, maxY - minY + pad * 2);
			Canvas.SetLeft(el, left + boxL * sx);
			Canvas.SetTop(el, top + boxT * sy);
			el.Width = boxW * sx;
			el.Height = boxH * sy;
			if (el is Border b && b.Child is Canvas cv) {
				cv.Width = el.Width;
				cv.Height = el.Height;
				foreach (var ch in cv.Children) {
					if (ch is Line ln) {
						ln.X1 = (it.X - boxL) * sx;
						ln.Y1 = (it.Y - boxT) * sy;
						ln.X2 = (it.X2 - boxL) * sx;
						ln.Y2 = (it.Y2 - boxT) * sy;
						ln.StrokeThickness = Math.Max(0.8, it.Stroke * Math.Min(sx, sy));
					} else if (ch is Polygon poly) {
						// 箭头头部
						var dx = it.X2 - it.X;
						var dy = it.Y2 - it.Y;
						var len = Math.Sqrt(dx * dx + dy * dy);
						if (len < 1e-3) len = 1;
						var ux = dx / len;
						var uy = dy / len;
						var ah = 10.0; // pt
						var aw = 5.0;
						var tipX = (it.X2 - boxL) * sx;
						var tipY = (it.Y2 - boxT) * sy;
						var bx = it.X2 - ux * ah - boxL;
						var by = it.Y2 - uy * ah - boxT;
						var px = -uy * aw;
						var py = ux * aw;
						poly.Points = new PointCollection {
							new WpfPoint(tipX, tipY),
							new WpfPoint((bx + px) * sx, (by + py) * sy),
							new WpfPoint((bx - px) * sx, (by - py) * sy),
						};
					}
				}
			}
			return;
		}

		if (it.Kind is PdfAnnotKind.Ink or PdfAnnotKind.Highlight) {
			if (el is Border bp && bp.Child is ShapePath path) {
				path.Data = buildgeometry(it);
				path.StrokeThickness = (it.Kind == PdfAnnotKind.Highlight
					? Math.Max(6, it.Stroke)
					: Math.Max(0.8, it.Stroke)) * Math.Min(sx, sy);
				// 用 LayoutTransform 缩放相对几何
				path.Width = Math.Max(MIN_SIZE, it.W) * sx;
				path.Height = Math.Max(MIN_SIZE, it.H) * sy;
				path.Stretch = Stretch.Fill;
			}
		}

		if (it.Kind == PdfAnnotKind.Note) {
			// 固定小气泡尺寸
			var side = NOTE_ICON_PT * Math.Min(sx, sy);
			side = Math.Max(18, side);
			Canvas.SetLeft(el, left + it.X * sx);
			Canvas.SetTop(el, top + it.Y * sy);
			el.Width = side;
			el.Height = side;
			if (el is Border nb) {
				nb.CornerRadius = new CornerRadius(side / 2);
				nb.Width = side;
				nb.Height = side;
			}
			it.W = NOTE_ICON_PT;
			it.H = NOTE_ICON_PT;
			return;
		}

		Canvas.SetLeft(el, left + it.X * sx);
		Canvas.SetTop(el, top + it.Y * sy);
		el.Width = Math.Max(MIN_SIZE, it.W * sx);

		if (it.Kind == PdfAnnotKind.Text) {
			el.Width = Math.Max(MIN_SIZE, it.W * sx);
			el.Height = Math.Max(MIN_SIZE, it.H * sy);
			if (el is Border bt && bt.Child is TextBox tb) {
				if (refitText) {
					tb.LayoutTransform = Transform.Identity;
					// 字号仅随视图缩放显示，模型 FontSize 不因调框改变
					tb.FontSize = Math.Max(8, it.FontSize * 96.0 / 72.0 * Math.Min(sx, sy));
					tb.Foreground = makebrush(it);
					try {
						tb.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(it.FontName)
							? "Microsoft YaHei UI" : it.FontName);
					} catch { /* ignore */ }
					tb.IsReadOnly = tool != Tool.Text;
					fittextlayout(it, tb);
					el.Width = Math.Max(MIN_SIZE, it.W * sx);
					el.Height = Math.Max(MIN_SIZE, it.H * sy);
					tb.Width = el.Width;
					tb.Height = double.NaN;
				} else {
					// 缩放路径：外框按页比例移动；字用 LayoutTransform 跟缩放，避免改 FontSize 触发布局
					var fs = Math.Max(8, it.FontSize * 96.0 / 72.0 * Math.Min(sx, sy));
					var baseFs = tb.FontSize > 1 ? tb.FontSize : fs;
					// 若已有 ScaleTransform，先还原基准
					if (tb.LayoutTransform is ScaleTransform st0 && Math.Abs(st0.ScaleX) > 1e-3)
						baseFs = tb.FontSize; // FontSize 仍是改 transform 前的基准
					var scale = baseFs > 1 ? fs / baseFs : 1;
					if (Math.Abs(scale - 1) > 0.02 && scale > 0.05 && scale < 8) {
						tb.LayoutTransform = new ScaleTransform(scale, scale);
						tb.Width = el.Width / scale;
					} else {
						tb.LayoutTransform = Transform.Identity;
						tb.Width = el.Width;
					}
					tb.Height = double.NaN;
				}
			}
			return;
		}

		el.Height = Math.Max(MIN_SIZE, it.H * sy);

		if (it.Kind is PdfAnnotKind.Rect or PdfAnnotKind.Ellipse) {
			if (el is Border bs && bs.Child is Shape sh)
				sh.StrokeThickness = Math.Max(0.8, it.Stroke * Math.Min(sx, sy));
		}
	}

	// ---------- 命中 / 拖拽 ----------

	void onhostdown(object sender, MouseButtonEventArgs e) {
		if (sender is not FrameworkElement fe || fe.Tag is not string id) return;
		var it = doc.Find(id);
		if (it == null) return;

		// 纯预览：仅注释气泡可点开 flyout
		if (!editMode) {
			if (it.Kind == PdfAnnotKind.Note) {
				opennoteflyout(it, readOnly: true);
				e.Handled = true;
			}
			return;
		}

		// 手型：永不处理标注
		if (tool == Tool.Hand) return;

		// 文本工具：点文本 → 进入编辑
		if (tool == Tool.Text && it.Kind == PdfAnnotKind.Text) {
			doc.DeselectAll();
			it.Selected = true;
			refreshchrome();
			updatehandles();
			try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
			focustext(it);
			e.Handled = true;
			return;
		}

		// 注释工具：点气泡 → flyout
		if (tool == Tool.Note && it.Kind == PdfAnnotKind.Note) {
			doc.DeselectAll();
			it.Selected = true;
			refreshchrome();
			opennoteflyout(it, readOnly: false);
			e.Handled = true;
			return;
		}

		if (tool != Tool.Select) return;

		var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
			|| Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

		// 框选工具：点文字只选中/拖动/缩放，不进入编辑（编辑请用文本工具）
		if (it.Kind == PdfAnnotKind.Text) {
			try { Keyboard.ClearFocus(); } catch { /* ignore */ }
		}

		// 点选（含成组）
		// 关键：已选中再点（无 Shift）必须保留多选，才能整体拖动
		if (shift) {
			if (it.Selected) {
				if (!string.IsNullOrEmpty(it.GroupId)) {
					foreach (var x in doc.Items)
						if (x.GroupId == it.GroupId) x.Selected = false;
				} else it.Selected = false;
			} else {
				doc.SelectWithGroup(it, additive: true);
			}
		} else if (!it.Selected) {
			doc.SelectWithGroup(it, additive: false);
		} else if (!string.IsNullOrEmpty(it.GroupId) && doc.SelectedCount == 1) {
			// 仅选中组内一项时补全整组
			doc.SelectWithGroup(it, additive: false);
		}
		// else: 已在多选集合中 → 保持，准备整体移动

		refreshchrome();
		updatehandles();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }

		// 文字/其它标注：按下即开始拖动（缩放用手柄）
		noteClickCandidate = it.Kind == PdfAnnotKind.Note && doc.SelectedCount == 1;
		noteClickStart = e.GetPosition(this);
		begindragselected(e.GetPosition(this), resizeHi: -1);
		Focus();
		e.Handled = true;
	}

	void onhandledown(object sender, MouseButtonEventArgs e) {
		if (!editMode || tool != Tool.Select) return;
		if (sender is not Rectangle r || r.Tag is not int hi) return;
		var sel = doc.SelectedItems;
		if (sel.Count == 0) return;
		// 注释不缩放
		if (sel.Count == 1 && sel[0].Kind == PdfAnnotKind.Note) return;
		// 多页不统一缩放
		if (!sameselectionpage(out _)) return;

		// 单选文本：可调宽高（不改字号）；关 AutoWidth
		begindragselected(e.GetPosition(this), resizeHi: hi);
		Focus();
		e.Handled = true;
	}

	void begindragselected(WpfPoint canvasPt, int resizeHi) {
		dragging = true;
		resizeHandle = resizeHi;
		dragStart = canvasPt;
		dragSnaps.Clear();
		foreach (var it in doc.SelectedItems) {
			dragSnaps.Add(new DragSnap {
				It = it,
				X = it.X, Y = it.Y, W = it.W, H = it.H,
				X2 = it.X2, Y2 = it.Y2,
				Pts = clonepts(it.Points),
			});
		}
		if (trygetselectionbounds(out var bx, out var by, out var bw, out var bh)) {
			groupOrigX = bx; groupOrigY = by; groupOrigW = bw; groupOrigH = bh;
		}
		CaptureMouse();
	}

	bool sameselectionpage(out int page) {
		page = -1;
		foreach (var it in doc.SelectedItems) {
			if (page < 0) page = it.Page;
			else if (page != it.Page) return false;
		}
		return page >= 0;
	}

	/// <summary>选中项在页坐标下的包围盒（同页）。</summary>
	bool trygetselectionbounds(out double x, out double y, out double w, out double h) {
		x = y = w = h = 0;
		if (!sameselectionpage(out _)) return false;
		var minX = double.MaxValue;
		var minY = double.MaxValue;
		var maxX = double.MinValue;
		var maxY = double.MinValue;
		var any = false;
		foreach (var it in doc.SelectedItems) {
			getitembounds(it, out var x0, out var y0, out var x1, out var y1);
			if (x0 < minX) minX = x0;
			if (y0 < minY) minY = y0;
			if (x1 > maxX) maxX = x1;
			if (y1 > maxY) maxY = y1;
			any = true;
		}
		if (!any) return false;
		x = minX; y = minY; w = Math.Max(MIN_SIZE, maxX - minX); h = Math.Max(MIN_SIZE, maxY - minY);
		return true;
	}

	static void getitembounds(PdfAnnotItem it, out double x0, out double y0, out double x1, out double y1) {
		if (it.Kind is PdfAnnotKind.Line or PdfAnnotKind.Arrow) {
			x0 = Math.Min(it.X, it.X2);
			y0 = Math.Min(it.Y, it.Y2);
			x1 = Math.Max(it.X, it.X2);
			y1 = Math.Max(it.Y, it.Y2);
			return;
		}
		x0 = it.X; y0 = it.Y; x1 = it.X + it.W; y1 = it.Y + it.H;
	}

	/// <summary>是否点在标注本体或缩放手柄上。</summary>
	bool isoverannotorhandle(DependencyObject src) {
		var d = src;
		while (d != null && !ReferenceEquals(d, this)) {
			if (d is FrameworkElement fe) {
				if (fe.Tag is string id && hosts.ContainsKey(id))
					return true;
				if (fe.Tag is int) // 缩放手柄
					return true;
			}
			if (ReferenceEquals(d, marqueeVisual)) return false;
			d = VisualTreeHelper.GetParent(d);
		}
		return false;
	}

	void startmarquee(WpfPoint pt, bool clearSel) {
		closenotepopups();
		if (clearSel) ClearSelection();
		marquee = true;
		marqueeStart = pt;
		marqueeVisual.Visibility = Visibility.Visible;
		Canvas.SetLeft(marqueeVisual, pt.X);
		Canvas.SetTop(marqueeVisual, pt.Y);
		marqueeVisual.Width = 1;
		marqueeVisual.Height = 1;
		try { CaptureMouse(); } catch { /* ignore */ }
		Focus();
	}

	void onpreviewdown(object sender, MouseButtonEventArgs e) {
		if (!editMode || tool != Tool.Select) return;
		// 点在标注/手柄上：留给 onhostdown / onhandledown
		if (isoverannotorhandle(e.OriginalSource as DependencyObject)) return;
		// 空白：立即开始框选（任意区域，含页缝/灰边）
		var pt = e.GetPosition(this);
		var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
			|| Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
		startmarquee(pt, clearSel: !additive);
		e.Handled = true;
	}

	void onlostcapture(object sender, MouseEventArgs e) {
		if (marquee) endmarquee(commit: true);
		if (erasing) {
			erasing = false;
			erasedStrokeIds.Clear();
		}
	}

	// ---------- 橡皮 ----------

	void moveerasercursor(WpfPoint canvasPt) {
		if (eraserCursor == null) return;
		var rDip = eraserRadiusDip(canvasPt);
		eraserCursor.Width = rDip * 2;
		eraserCursor.Height = rDip * 2;
		Canvas.SetLeft(eraserCursor, canvasPt.X - rDip);
		Canvas.SetTop(eraserCursor, canvasPt.Y - rDip);
		eraserCursor.Visibility = Visibility.Visible;
		Panel.SetZIndex(eraserCursor, 2000);
	}

	double eraserRadiusDip(WpfPoint canvasPt) {
		var page = hitPage != null ? hitPage(canvasPt) : -1;
		if (page < 0 || pageLayout == null || pageSizePt == null)
			return EraserRadiusPt * 1.5;
		var (_, _, pw, ph) = pageLayout(page);
		var (ptW, ptH) = pageSizePt(page);
		if (ptW < 1 || pw < 1) return EraserRadiusPt * 1.5;
		var sx = pw / ptW;
		return Math.Max(4, EraserRadiusPt * sx);
	}

	void applyeraserat(int page, double xPt, double yPt, WpfPoint canvasPt) {
		if (page < 0) return;
		moveerasercursor(canvasPt);
		var r = Math.Max(2, EraserRadiusPt);
		var dirty = false;
		// 拷贝列表，擦除中可能增删
		var list = new List<PdfAnnotItem>(doc.Items);
		foreach (var it in list) {
			if (it == null || it.Page != page) continue;
			if (it.Kind is not (PdfAnnotKind.Ink or PdfAnnotKind.Highlight)) continue;
			if (it.Points == null || it.Points.Count < 2) continue;

			if (eraserMode == EraserMode.Stroke) {
				if (erasedStrokeIds.Contains(it.Id)) continue;
				if (strokehit(it, xPt, yPt, r + ERASER_STROKE_PAD)) {
					removeannotitem(it);
					erasedStrokeIds.Add(it.Id);
					dirty = true;
				}
			} else {
				if (eraserpointonstroke(it, xPt, yPt, r))
					dirty = true;
			}
		}
		if (dirty) markdirty();
	}

	/// <summary>点到折线任一段的距离是否在半径内。</summary>
	static bool strokehit(PdfAnnotItem it, double x, double y, double radius) {
		var pts = it.Points;
		if (pts == null || pts.Count < 2) return false;
		var r2 = radius * radius;
		for (var i = 0; i < pts.Count; i++) {
			var dx = pts[i].X - x;
			var dy = pts[i].Y - y;
			if (dx * dx + dy * dy <= r2) return true;
			if (i + 1 < pts.Count) {
				var d = distptseg(x, y, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
				if (d <= radius) return true;
			}
		}
		return false;
	}

	static double distptseg(double px, double py, double x1, double y1, double x2, double y2) {
		var vx = x2 - x1;
		var vy = y2 - y1;
		var len2 = vx * vx + vy * vy;
		if (len2 < 1e-12) {
			var dx = px - x1;
			var dy = py - y1;
			return Math.Sqrt(dx * dx + dy * dy);
		}
		var t = ((px - x1) * vx + (py - y1) * vy) / len2;
		if (t < 0) t = 0;
		else if (t > 1) t = 1;
		var qx = x1 + t * vx;
		var qy = y1 + t * vy;
		var dx2 = px - qx;
		var dy2 = py - qy;
		return Math.Sqrt(dx2 * dx2 + dy2 * dy2);
	}

	/// <summary>点擦：去掉半径内点，拆成多段笔画。</summary>
	bool eraserpointonstroke(PdfAnnotItem it, double x, double y, double radius) {
		var pts = it.Points;
		if (pts == null || pts.Count < 2) return false;
		var r2 = radius * radius;
		var anyHit = false;
		for (var i = 0; i < pts.Count; i++) {
			var dx = pts[i].X - x;
			var dy = pts[i].Y - y;
			if (dx * dx + dy * dy <= r2) { anyHit = true; break; }
		}
		if (!anyHit) {
			// 也擦线段经过的区域
			for (var i = 0; i + 1 < pts.Count; i++) {
				if (distptseg(x, y, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y) <= radius) {
					anyHit = true;
					break;
				}
			}
		}
		if (!anyHit) return false;

		// 标记保留点：到橡皮中心距离 > r；对靠近线段的点也删（采样）
		var keep = new bool[pts.Count];
		for (var i = 0; i < pts.Count; i++) {
			var dx = pts[i].X - x;
			var dy = pts[i].Y - y;
			keep[i] = dx * dx + dy * dy > r2;
		}
		// 拆段
		var segs = new List<List<PdfAnnotPt>>();
		List<PdfAnnotPt> cur = null;
		for (var i = 0; i < pts.Count; i++) {
			if (keep[i]) {
				if (cur == null) cur = new List<PdfAnnotPt>();
				cur.Add(new PdfAnnotPt(pts[i].X, pts[i].Y));
			} else {
				if (cur != null && cur.Count >= 2)
					segs.Add(cur);
				cur = null;
			}
		}
		if (cur != null && cur.Count >= 2)
			segs.Add(cur);

		if (segs.Count == 0) {
			removeannotitem(it);
			return true;
		}

		// 第一段写回原 item
		it.Points.Clear();
		foreach (var p in segs[0])
			it.Points.Add(p);
		it.RecalcBoundsFromPoints();
		if (hosts.ContainsKey(it.Id)) {
			refreshhost(it);
			placehost(it, refitText: false);
		} else {
			ensurehost(it);
			placehost(it, refitText: false);
		}

		// 额外段成新笔画
		for (var s = 1; s < segs.Count; s++) {
			var neu = it.Clone(newId: true);
			neu.Points.Clear();
			foreach (var p in segs[s])
				neu.Points.Add(p);
			neu.RecalcBoundsFromPoints();
			neu.Selected = false;
			doc.Items.Add(neu);
			ensurehost(neu);
			placehost(neu, refitText: false);
		}
		applycursorandhit();
		return true;
	}

	void removeannotitem(PdfAnnotItem it) {
		if (it == null) return;
		if (notePopups.TryGetValue(it.Id, out var pop)) {
			try { pop.IsOpen = false; } catch { /* ignore */ }
			notePopups.Remove(it.Id);
		}
		doc.Items.Remove(it);
		if (hosts.TryGetValue(it.Id, out var el)) {
			Children.Remove(el);
			hosts.Remove(it.Id);
		}
	}

	void endmarquee(bool commit) {
		if (!marquee) return;
		marquee = false;
		var x = Canvas.GetLeft(marqueeVisual);
		var y = Canvas.GetTop(marqueeVisual);
		var w = marqueeVisual.Width;
		var h = marqueeVisual.Height;
		marqueeVisual.Visibility = Visibility.Collapsed;
		try { if (IsMouseCaptured) ReleaseMouseCapture(); } catch { /* ignore */ }
		if (!commit) return;
		if (double.IsNaN(x) || double.IsNaN(y) || w < 2 || h < 2) return;
		var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
			|| Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
		selectinmarquee(new Rect(x, y, w, h), additive);
	}

	void ondown(object sender, MouseButtonEventArgs e) {
		if (!editMode || e.Handled) return;
		Focus();
		var pt = e.GetPosition(this);

		// 1) 手型：只 pan
		if (tool == Tool.Hand) {
			closenotepopups();
			beginpan(e);
			e.Handled = true;
			return;
		}

		// 框选空白已在 Preview 处理
		if (tool == Tool.Select) return;

		if (hitPage == null || pageLayout == null || pageSizePt == null) return;
		var page = hitPage(pt);
		if (page < 0) return;
		if (!topagept(page, pt, out var xPt, out var yPt)) return;

		// 橡皮：按下即擦
		if (tool == Tool.Eraser) {
			erasing = true;
			erasedStrokeIds.Clear();
			closenotepopups();
			doc.DeselectAll();
			refreshchrome();
			updatehandles();
			applyeraserat(page, xPt, yPt, pt);
			CaptureMouse();
			e.Handled = true;
			return;
		}

		// 3) 文本工具：空白添加文本框
		if (tool == Tool.Text) {
			if (!isoverannotorhandle(e.OriginalSource as DependencyObject)) {
				finishtextnote(page, xPt, yPt, PdfAnnotKind.Text, "");
				e.Handled = true;
			}
			return;
		}

		if (tool == Tool.Note) {
			if (!isoverannotorhandle(e.OriginalSource as DependencyObject)) {
				finishtextnote(page, xPt, yPt, PdfAnnotKind.Note, "");
				var note = doc.SelectedItem;
				if (note != null) opennoteflyout(note);
				e.Handled = true;
			}
			return;
		}

		// 4) 绘制工具
		drawing = true;
		drawStart = pt;
		canceldraft(keepFlag: true);

		switch (tool) {
			case Tool.Pen:
			case Tool.Highlighter:
				draft = new PdfAnnotItem {
					Page = page,
					Kind = tool == Tool.Highlighter ? PdfAnnotKind.Highlight : PdfAnnotKind.Ink,
					Color = tool == Tool.Highlighter ? HighlightColor : DefaultColor,
					Stroke = tool == Tool.Highlighter ? HighlightStroke : DefaultStroke,
					Opacity = tool == Tool.Highlighter ? 0.55 : 1,
					Points = new List<PdfAnnotPt> { new(xPt, yPt) },
				};
				draft.RecalcBoundsFromPoints();
				break;
			case Tool.Rect:
			case Tool.Ellipse:
				draft = new PdfAnnotItem {
					Page = page,
					Kind = tool == Tool.Ellipse ? PdfAnnotKind.Ellipse : PdfAnnotKind.Rect,
					X = xPt, Y = yPt, W = 4, H = 4,
					Color = DefaultColor,
					Stroke = DefaultStroke,
				};
				break;
			case Tool.Line:
			case Tool.Arrow:
				draft = new PdfAnnotItem {
					Page = page,
					Kind = tool == Tool.Arrow ? PdfAnnotKind.Arrow : PdfAnnotKind.Line,
					X = xPt, Y = yPt, X2 = xPt, Y2 = yPt,
					Color = DefaultColor,
					Stroke = DefaultStroke,
				};
				break;
		}

		if (draft != null) {
			ensurehost(draft);
			placehost(draft);
			draftVisual = hosts.ContainsKey(draft.Id) ? hosts[draft.Id] : null;
		}
		CaptureMouse();
		e.Handled = true;
	}

	void finishtextnote(int page, double xPt, double yPt, PdfAnnotKind kind, string def) {
		var it = new PdfAnnotItem {
			Page = page,
			Kind = kind,
			X = xPt,
			Y = yPt,
			W = kind == PdfAnnotKind.Note ? NOTE_ICON_PT : TEXT_MIN_W,
			H = kind == PdfAnnotKind.Note ? NOTE_ICON_PT : 22,
			Text = def,
			Color = DefaultColor,
			FontName = DefaultFont,
			FontSize = DefaultFontSize,
			AutoWidth = kind == PdfAnnotKind.Text,
		};
		doc.DeselectAll();
		it.Selected = true;
		doc.Items.Add(it);
		ensurehost(it);
		placehost(it);
		if (kind == PdfAnnotKind.Text) {
			fittextlayout(it);
			placehost(it);
		}
		applycursorandhit();
		updatehandles();
		markdirty();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
		if (kind == PdfAnnotKind.Text) {
			// 保持文本工具，便于继续输入
			focustext(it);
		}
	}

	void opennoteflyout(PdfAnnotItem it, bool readOnly = false) {
		if (it == null || it.Kind != PdfAnnotKind.Note) return;
		closenotepopups(it.Id);
		if (!notePopups.TryGetValue(it.Id, out var pop)) {
			refreshhost(it);
			placehost(it, refitText: false);
			notePopups.TryGetValue(it.Id, out pop);
		}
		if (pop == null) return;
		if (pop.Child is Border fly) {
			var tb = findtextbox(fly);
			if (tb != null) {
				if (tb.Text != (it.Text ?? "")) tb.Text = it.Text ?? "";
				tb.FontSize = Math.Max(11, it.FontSize * 96.0 / 72.0);
				try {
					tb.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(it.FontName)
						? "Microsoft YaHei UI" : it.FontName);
				} catch { /* ignore */ }
				tb.IsReadOnly = readOnly;
			}
		}
		pop.StaysOpen = true;
		pop.IsOpen = true;
		armnoteoutsideclose(pop, it.Id);
		if (readOnly) return;
		try {
			Dispatcher.BeginInvoke(new Action(() => {
				if (pop.IsOpen && pop.Child is Border b) {
					var tb = findtextbox(b);
					if (tb == null) return;
					tb.IsReadOnly = false;
					tb.Focus();
				}
			}), DispatcherPriority.Input);
		} catch { /* ignore */ }
	}

	static TextBox findtextbox(DependencyObject root) {
		if (root is TextBox t) return t;
		if (root is null) return null;
		var n = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < n; i++) {
			var c = findtextbox(VisualTreeHelper.GetChild(root, i));
			if (c != null) return c;
		}
		if (root is ContentControl cc && cc.Content is DependencyObject d)
			return findtextbox(d);
		if (root is Panel p) {
			foreach (var ch in p.Children)
				if (ch is DependencyObject d2) {
					var r = findtextbox(d2);
					if (r != null) return r;
				}
		}
		if (root is Decorator dec && dec.Child is DependencyObject d3)
			return findtextbox(d3);
		return null;
	}

	void onmove(object sender, MouseEventArgs e) {
		var pt = e.GetPosition(this);

		// 橡皮光标跟随
		if (editMode && tool == Tool.Eraser)
			moveerasercursor(pt);

		if (panning && e.LeftButton == MouseButtonState.Pressed) {
			dopan(e);
			e.Handled = true;
			return;
		}

		if (erasing && e.LeftButton == MouseButtonState.Pressed && tool == Tool.Eraser) {
			if (hitPage != null && topagept(hitPage(pt), pt, out var xPt, out var yPt))
				applyeraserat(hitPage(pt), xPt, yPt, pt);
			e.Handled = true;
			return;
		}

		if (marquee && e.LeftButton == MouseButtonState.Pressed) {
			var x = Math.Min(marqueeStart.X, pt.X);
			var y = Math.Min(marqueeStart.Y, pt.Y);
			var w = Math.Abs(pt.X - marqueeStart.X);
			var h = Math.Abs(pt.Y - marqueeStart.Y);
			Canvas.SetLeft(marqueeVisual, x);
			Canvas.SetTop(marqueeVisual, y);
			marqueeVisual.Width = w;
			marqueeVisual.Height = h;
			e.Handled = true;
			return;
		}

		if (drawing && draft != null && e.LeftButton == MouseButtonState.Pressed) {
			if (!topagept(draft.Page, pt, out var xPt, out var yPt)) return;
			if (draft.Kind is PdfAnnotKind.Ink or PdfAnnotKind.Highlight) {
				var last = draft.Points.Count > 0 ? draft.Points[draft.Points.Count - 1] : null;
				if (last == null || Math.Abs(last.X - xPt) + Math.Abs(last.Y - yPt) > 0.4) {
					draft.Points.Add(new PdfAnnotPt(xPt, yPt));
					draft.RecalcBoundsFromPoints();
					if (hosts.ContainsKey(draft.Id))
						refreshhost(draft);
					else
						ensurehost(draft);
					placehost(draft);
				}
			} else if (draft.Kind is PdfAnnotKind.Rect or PdfAnnotKind.Ellipse) {
				if (!topagept(draft.Page, drawStart, out var x0, out var y0)) return;
				draft.X = Math.Min(x0, xPt);
				draft.Y = Math.Min(y0, yPt);
				draft.W = Math.Max(4, Math.Abs(xPt - x0));
				draft.H = Math.Max(4, Math.Abs(yPt - y0));
				placehost(draft);
			} else if (draft.Kind is PdfAnnotKind.Line or PdfAnnotKind.Arrow) {
				draft.X2 = xPt;
				draft.Y2 = yPt;
				placehost(draft);
			}
			e.Handled = true;
			return;
		}

		if (!dragging || e.LeftButton != MouseButtonState.Pressed || dragSnaps.Count == 0) return;
		if (pageLayout == null || pageSizePt == null) return;
		var page = dragSnaps[0].It.Page;
		var (left, top, pw, ph) = pageLayout(page);
		var (ptW, ptH) = pageSizePt(page);
		if (ptW < 1 || ptH < 1 || pw < 1 || ph < 1) return;
		var dx = (pt.X - dragStart.X) * ptW / pw;
		var dy = (pt.Y - dragStart.Y) * ptH / ph;

		if (resizeHandle < 0) {
			if (noteClickCandidate) {
				var ddx = pt.X - noteClickStart.X;
				var ddy = pt.Y - noteClickStart.Y;
				if (ddx * ddx + ddy * ddy > 16) noteClickCandidate = false;
			}
			foreach (var s in dragSnaps) {
				var it = s.It;
				it.X = s.X + dx;
				it.Y = s.Y + dy;
				if (it.Kind is PdfAnnotKind.Line or PdfAnnotKind.Arrow) {
					it.X2 = s.X2 + dx;
					it.Y2 = s.Y2 + dy;
				}
				if (s.Pts != null && it.Points != null) {
					it.Points.Clear();
					foreach (var p in s.Pts)
						it.Points.Add(new PdfAnnotPt(p.X + dx, p.Y + dy));
					it.RecalcBoundsFromPoints();
				}
				// 移动时只 placehost（path.Data 会按新包围盒重建），禁止 refreshhost 以免卡顿
				placehost(it, refitText: false);
			}
		} else {
			// 组包围盒缩放
			applygroupresize(resizeHandle, dx, dy);
		}
		updatehandles();
		markdirty();
		e.Handled = true;
	}

	void applygroupresize(int hi, double dx, double dy) {
		if (groupOrigW < 1e-3 || groupOrigH < 1e-3) return;
		var x = groupOrigX;
		var y = groupOrigY;
		var w = groupOrigW;
		var h = groupOrigH;
		switch (hi) {
			case 0: x += dx; y += dy; w -= dx; h -= dy; break;
			case 1: y += dy; h -= dy; break;
			case 2: y += dy; w += dx; h -= dy; break;
			case 3: w += dx; break;
			case 4: w += dx; h += dy; break;
			case 5: h += dy; break;
			case 6: x += dx; w -= dx; h += dy; break;
			case 7: x += dx; w -= dx; break;
		}
		if (w < MIN_SIZE) { if (hi is 0 or 6 or 7) x = groupOrigX + groupOrigW - MIN_SIZE; w = MIN_SIZE; }
		if (h < MIN_SIZE) { if (hi is 0 or 1 or 2) y = groupOrigY + groupOrigH - MIN_SIZE; h = MIN_SIZE; }

		var sx = w / groupOrigW;
		var sy = h / groupOrigH;

		// 单选文本：只调框不缩放字号；关掉 AutoWidth
		if (dragSnaps.Count == 1 && dragSnaps[0].It.Kind == PdfAnnotKind.Text) {
			var s = dragSnaps[0];
			var it = s.It;
			it.AutoWidth = false;
			// 文本：宽高独立（字号不变）
			var nx = s.X; var ny = s.Y; var nw = s.W; var nh = s.H;
			switch (hi) {
				case 0: nx += dx; ny += dy; nw -= dx; nh -= dy; break;
				case 1: ny += dy; nh -= dy; break;
				case 2: ny += dy; nw += dx; nh -= dy; break;
				case 3: nw += dx; break;
				case 4: nw += dx; nh += dy; break;
				case 5: nh += dy; break;
				case 6: nx += dx; nw -= dx; nh += dy; break;
				case 7: nx += dx; nw -= dx; break;
			}
			if (nw < TEXT_MIN_W) { if (hi is 0 or 6 or 7) nx = s.X + s.W - TEXT_MIN_W; nw = TEXT_MIN_W; }
			if (nh < 12) { if (hi is 0 or 1 or 2) ny = s.Y + s.H - 12; nh = 12; }
			it.X = nx; it.Y = ny; it.W = nw; it.H = nh;
			// 高度仍可再按内容拟合下限，但保留用户拉高
			fittextlayout(it);
			if (it.H < nh) it.H = nh;
			placehost(it, refitText: false);
			return;
		}

		foreach (var s in dragSnaps) {
			var it = s.It;
			// 相对组原点缩放
			var rx = s.X - groupOrigX;
			var ry = s.Y - groupOrigY;
			it.X = x + rx * sx;
			it.Y = y + ry * sy;
			if (it.Kind is PdfAnnotKind.Line or PdfAnnotKind.Arrow) {
				var rx2 = s.X2 - groupOrigX;
				var ry2 = s.Y2 - groupOrigY;
				it.X2 = x + rx2 * sx;
				it.Y2 = y + ry2 * sy;
			} else if (it.Kind is PdfAnnotKind.Ink or PdfAnnotKind.Highlight) {
				if (s.Pts != null) {
					it.Points.Clear();
					foreach (var p in s.Pts) {
						var px = x + (p.X - groupOrigX) * sx;
						var py = y + (p.Y - groupOrigY) * sy;
						it.Points.Add(new PdfAnnotPt(px, py));
					}
					it.RecalcBoundsFromPoints();
				}
			} else if (it.Kind == PdfAnnotKind.Text) {
				it.AutoWidth = false;
				it.W = Math.Max(TEXT_MIN_W, s.W * sx);
				it.H = Math.Max(12, s.H * sy);
			} else if (it.Kind == PdfAnnotKind.Note) {
				it.W = NOTE_ICON_PT;
				it.H = NOTE_ICON_PT;
			} else {
				it.W = Math.Max(MIN_SIZE, s.W * sx);
				it.H = Math.Max(MIN_SIZE, s.H * sy);
			}
			placehost(it, refitText: false);
		}
	}

	void onup(object sender, MouseButtonEventArgs e) {
		if (panning) {
			endpan();
			e.Handled = true;
			return;
		}

		if (erasing) {
			erasing = false;
			erasedStrokeIds.Clear();
			try { ReleaseMouseCapture(); } catch { /* ignore */ }
			e.Handled = true;
			return;
		}

		if (marquee) {
			endmarquee(commit: true);
			e.Handled = true;
			return;
		}

		if (drawing) {
			drawing = false;
			try { ReleaseMouseCapture(); } catch { /* ignore */ }
			if (draft != null) {
				var keepDrawTool = draft.Kind is PdfAnnotKind.Ink or PdfAnnotKind.Highlight;
				if (draft.Kind is PdfAnnotKind.Ink or PdfAnnotKind.Highlight) {
					if (draft.Points == null || draft.Points.Count < 2) {
						removehost(draft.Id);
						draft = null;
						e.Handled = true;
						return;
					}
					draft.RecalcBoundsFromPoints();
				} else if (draft.Kind is PdfAnnotKind.Rect or PdfAnnotKind.Ellipse) {
					if (draft.W < 3 && draft.H < 3) {
						removehost(draft.Id);
						draft = null;
						e.Handled = true;
						return;
					}
				} else if (draft.Kind is PdfAnnotKind.Line or PdfAnnotKind.Arrow) {
					var len = Math.Abs(draft.X2 - draft.X) + Math.Abs(draft.Y2 - draft.Y);
					if (len < 3) {
						removehost(draft.Id);
						draft = null;
						e.Handled = true;
						return;
					}
				}

				if (!doc.Items.Contains(draft))
					doc.Items.Add(draft);
				if (!keepDrawTool) {
					doc.DeselectAll();
					draft.Selected = true;
				}
				if (!hosts.ContainsKey(draft.Id))
					ensurehost(draft);
				else
					refreshhost(draft);
				placehost(draft);
				if (!keepDrawTool) {
					updatehandles();
					try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
					CurrentTool = Tool.Select;
				} else {
					doc.DeselectAll();
					refreshchrome();
					updatehandles();
				}
				markdirty();
				draft = null;
			}
			e.Handled = true;
			return;
		}

		if (dragging) {
			var wasNoteClick = noteClickCandidate;
			var noteId = wasNoteClick && dragSnaps.Count == 1 ? dragSnaps[0].It.Id : null;
			dragging = false;
			resizeHandle = -1;
			dragSnaps.Clear();
			noteClickCandidate = false;
			try { ReleaseMouseCapture(); } catch { /* ignore */ }
			if (wasNoteClick && noteId != null) {
				var it = doc.Find(noteId);
				if (it != null && it.Kind == PdfAnnotKind.Note)
					opennoteflyout(it);
			}
			e.Handled = true;
		}
	}

	void selectinmarquee(Rect canvasRect, bool additive) {
		if (!additive) doc.DeselectAll();
		if (pageLayout == null || pageSizePt == null) return;
		var any = false;
		foreach (var it in doc.Items) {
			if (!tryitemcanvasrect(it, out var r)) continue;
			// 相交或被完全包住都算选中
			if (!canvasRect.IntersectsWith(r) && !canvasRect.Contains(r)) continue;
			if (!string.IsNullOrEmpty(it.GroupId)) {
				foreach (var g in doc.Items)
					if (g.GroupId == it.GroupId) g.Selected = true;
			} else {
				it.Selected = true;
			}
			any = true;
		}
		refreshchrome();
		updatehandles();
		if (any)
			try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	/// <summary>标注在叠加层 canvas 坐标下的包围矩形。</summary>
	bool tryitemcanvasrect(PdfAnnotItem it, out Rect r) {
		r = Rect.Empty;
		if (it == null || pageLayout == null || pageSizePt == null) return false;
		var (left, top, pw, ph) = pageLayout(it.Page);
		var (ptW, ptH) = pageSizePt(it.Page);
		if (ptW < 1 || ptH < 1 || pw < 1 || ph < 1) return false;
		var sx = pw / ptW;
		var sy = ph / ptH;
		getitembounds(it, out var x0, out var y0, out var x1, out var y1);
		// 略扩命中，便于框到细线/笔迹
		var pad = Math.Max(2, it.Stroke);
		x0 -= pad; y0 -= pad; x1 += pad; y1 += pad;
		var cx = left + x0 * sx;
		var cy = top + y0 * sy;
		var cw = Math.Max(2, (x1 - x0) * sx);
		var ch = Math.Max(2, (y1 - y0) * sy);
		r = new Rect(cx, cy, cw, ch);
		return true;
	}

	void onkey(object sender, KeyEventArgs e) {
		if (!editMode) return;
		var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
		var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
		if (e.Key == Key.Delete || e.Key == Key.Back) {
			// 文本编辑中不删标注
			if (Keyboard.FocusedElement is TextBox) return;
			DeleteSelected();
			e.Handled = true;
			return;
		}
		if (ctrl && e.Key == Key.C) {
			if (Keyboard.FocusedElement is TextBox) return;
			CopySelected();
			e.Handled = true;
			return;
		}
		if (ctrl && e.Key == Key.V) {
			if (Keyboard.FocusedElement is TextBox) return;
			PasteClipboard();
			e.Handled = true;
			return;
		}
		if (ctrl && e.Key == Key.D) {
			DuplicateSelected();
			e.Handled = true;
			return;
		}
		if (ctrl && !shift && e.Key == Key.G) {
			GroupSelected();
			e.Handled = true;
			return;
		}
		if (ctrl && shift && e.Key == Key.G) {
			UngroupSelected();
			e.Handled = true;
			return;
		}
		if (e.Key == Key.Escape) {
			ClearSelection();
			if (tool != Tool.Hand && tool != Tool.Select)
				CurrentTool = Tool.Select;
			e.Handled = true;
		}
	}

	void canceldraft(bool keepFlag = false) {
		if (draft != null && !doc.Items.Contains(draft))
			removehost(draft.Id);
		draft = null;
		draftVisual = null;
		if (!keepFlag) drawing = false;
	}

	void removehost(string id) {
		if (string.IsNullOrEmpty(id)) return;
		if (notePopups.TryGetValue(id, out var pop)) {
			try { pop.IsOpen = false; } catch { /* ignore */ }
			notePopups.Remove(id);
		}
		if (hosts.TryGetValue(id, out var el)) {
			Children.Remove(el);
			hosts.Remove(id);
		}
	}

	void focustext(PdfAnnotItem it) {
		if (!hosts.TryGetValue(it.Id, out var el) || el is not Border b) return;
		var tb = b.Child as TextBox ?? findtextbox(b);
		if (tb == null) return;
		tb.IsReadOnly = false;
		tb.Focus();
		if (string.IsNullOrEmpty(tb.Text))
			tb.CaretIndex = 0;
		else
			tb.SelectAll();
	}

	bool topagept(int page, WpfPoint canvasPt, out double xPt, out double yPt) {
		xPt = yPt = 0;
		if (pageLayout == null || pageSizePt == null) return false;
		var (left, top, pw, ph) = pageLayout(page);
		var (ptW, ptH) = pageSizePt(page);
		if (ptW < 1 || ptH < 1 || pw < 1 || ph < 1) return false;
		xPt = (canvasPt.X - left) * ptW / pw;
		yPt = (canvasPt.Y - top) * ptH / ph;
		return true;
	}

	void updatehandles() {
		foreach (var hd in handles) hd.Visibility = Visibility.Collapsed;
		if (selFrame != null) selFrame.Visibility = Visibility.Collapsed;
		if (!editMode || tool != Tool.Select) return;
		if (!trygetselectionbounds(out var bx, out var by, out var bw, out var bh)) return;
		if (!sameselectionpage(out var page)) return;

		var sel = doc.SelectedItems;
		var multi = sel.Count > 1;
		// 单选注释 / 单选线段：无包围盒手柄
		if (!multi && sel.Count == 1 && sel[0].Kind == PdfAnnotKind.Note) return;
		if (!multi && sel.Count == 1 && sel[0].Kind is PdfAnnotKind.Line or PdfAnnotKind.Arrow) return;

		if (pageLayout == null || pageSizePt == null) return;
		var (left, top, pw, ph) = pageLayout(page);
		var (ptW, ptH) = pageSizePt(page);
		if (ptW < 1 || ptH < 1) return;
		var sx = pw / ptW;
		var sy = ph / ptH;
		var x = left + bx * sx;
		var y = top + by * sy;
		var w = Math.Max(MIN_SIZE, bw * sx);
		var h = Math.Max(MIN_SIZE, bh * sy);

		// 多选/成组：只画统一外框
		if (multi && selFrame != null) {
			selFrame.Visibility = Visibility.Visible;
			Canvas.SetLeft(selFrame, x);
			Canvas.SetTop(selFrame, y);
			selFrame.Width = w;
			selFrame.Height = h;
		}

		double[] xs = { x, x + w / 2, x + w, x + w, x + w, x + w / 2, x, x };
		double[] ys = { y, y, y, y + h / 2, y + h, y + h, y + h, y + h / 2 };
		for (var i = 0; i < 8; i++) {
			handles[i].Visibility = Visibility.Visible;
			Canvas.SetLeft(handles[i], xs[i] - HANDLE / 2);
			Canvas.SetTop(handles[i], ys[i] - HANDLE / 2);
		}
	}

	static Cursor handlecursor(int i) => i switch {
		0 or 4 => WpfCursors.SizeNWSE,
		2 or 6 => WpfCursors.SizeNESW,
		1 or 5 => WpfCursors.SizeNS,
		_ => WpfCursors.SizeWE,
	};

	static List<PdfAnnotPt> clonepts(List<PdfAnnotPt> src) {
		if (src == null) return null;
		var list = new List<PdfAnnotPt>(src.Count);
		foreach (var p in src)
			list.Add(new PdfAnnotPt(p.X, p.Y));
		return list;
	}
}
