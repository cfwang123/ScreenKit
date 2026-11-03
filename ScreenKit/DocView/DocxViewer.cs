using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfPoint = System.Windows.Point;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;
using WP = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Microsoft.Win32;

namespace ScreenKit.DocView;

/// <summary>
/// DOCX：真正分页后竖向连续排列（一页接一页，仿 PDF）+ 目录 TOC。
/// </summary>
sealed class DocxViewer : IDocViewer {
	const double MIN_ZOOM = 0.4;
	const double MAX_ZOOM = 2.5;
	const double TWIP2DIP = 96.0 / 1440.0;
	const double EMU2DIP = 96.0 / 914400.0;
	const double DEF_PAGE_W = 11906 * TWIP2DIP;
	const double DEF_PAGE_H = 16838 * TWIP2DIP;
	const double DEF_MARGIN = 1440 * TWIP2DIP;
	const double SIDE_W = 220;
	const double PAGE_GAP = 12;
	/// <summary>滚轮一次滚动的视口高度比例。</summary>
	const double WHEEL_STEP = 0.22;

	readonly Grid root;
	readonly ColumnDefinition colside;
	readonly Border pside;
	readonly TreeView tree;
	readonly TextBlock lboutline;
	readonly TextBox eoutline;
	readonly StackPanel pageStack;
	readonly Border pageHost;
	readonly Grid pagePad;
	readonly ScrollViewer pageScroll;
	readonly ScaleTransform scaleXf;
	readonly List<TocEntry> tocEntries = new();
	readonly List<RichTextBox> pageBoxes = new();

	FlowDocument flow;
	DocumentPaginator paginator;
	double zoom = 1.0;
	double pageW = DEF_PAGE_W, pageH = DEF_PAGE_H;
	double padL = DEF_MARGIN, padT = DEF_MARGIN, padR = DEF_MARGIN, padB = DEF_MARGIN;
	int pageCount = 1;
	Dictionary<string, string> styleNames = new();
	/// <summary>样式段落属性（含继承链合并前的单层定义）。</summary>
	Dictionary<string, StylePPr> stylePPrs = new(StringComparer.OrdinalIgnoreCase);
	/// <summary>样式默认字符属性（Title 字号等）。</summary>
	Dictionary<string, StyleRPr> styleRPrs = new(StringComparer.OrdinalIgnoreCase);
	Dictionary<string, string> styleBasedOn = new(StringComparer.OrdinalIgnoreCase);
	MainDocumentPart mainPart;
	bool pendingPageBreak;
	bool syncTree;
	bool sideVisible = true;
	bool hasOutline;
	/// <summary>下次 synctoc 时展开到当前页最深目录项（仅恢复阅读位置用）。</summary>
	bool pendingExpandOutline;
	/// <summary>恢复阅读目标页（1-based）；布局未稳时 CurrentPage 可能仍为 1。</summary>
	int restoreOutlinePage;
	int outlineRestoreToken;
	/// <summary>滚动目录同步防抖。</summary>
	DispatcherTimer outlineDebounce;
	int lastTocPage = -1;
	bool panning;
	const int OUTLINE_DEBOUNCE_MS = 140;
	WpfPoint panStart;
	double panOffX, panOffY;
	string outlineQuery = "";

	// Word 编号：numId → abstractNumId；各级格式；各列表计数器
	Dictionary<int, int> numToAbs = new();
	Dictionary<int, NumLevel[]> absLevels = new();
	Dictionary<int, int[]> numCounters = new();

	// 查找：全部命中（页内字符偏移）+ 屏幕内背景高亮
	string findQuery;
	bool findIgnoreCase = true;
	readonly List<(int Page, int Start, int End)> findHits = new();
	int findIndex = -1;
	readonly List<TextRange> findHlRanges = new();
	int lastFindHlFirst = -1, lastFindHlLast = -1;
	static readonly SolidColorBrush FindHitBrush = makefindbrush(0x90, 0xFF, 0xF5, 0x9D);
	static readonly SolidColorBrush FindCurBrush = makefindbrush(0xC0, 0xFF, 0xC1, 0x07);

	static SolidColorBrush makefindbrush(byte a, byte r, byte g, byte b) {
		var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
		if (br.CanFreeze) br.Freeze();
		return br;
	}

	sealed class TocEntry {
		public string Title;
		public int Level;
		public Paragraph Para;
		public int Page1; // 1-based，布局后填充
		public TreeViewItem Item;
	}

	/// <summary>numbering.xml 中一级列表定义。</summary>
	sealed class NumLevel {
		public string Fmt = "Decimal";
		public string LvlText = "%1.";
		public int Start = 1;
		public double Left;
		public double Hanging;
		public double FirstLine;
	}

	/// <summary>styles.xml 中段落属性片段（编号可只在样式上）。</summary>
	sealed class StylePPr {
		public int? NumId;
		public int Ilvl;
		public double? Before, After, Line;
		public bool LineAuto = true;
		public double? Left, Right, First, Hanging;
		/// <summary>样式对齐（Title 等常只在样式上写 jc，段上无 jc）。</summary>
		public TextAlignment? Align;
	}

	/// <summary>styles.xml 中样式默认 run 属性（Title 字号/加粗常在 style rPr，run 上无 sz）。</summary>
	sealed class StyleRPr {
		/// <summary>字号（磅，已由 half-point/2 换算）。</summary>
		public double? FontSizePt;
		public bool? Bold;
		public bool? Italic;
		public string FontName;
	}

	public FrameworkElement View => root;
	public string FilePath { get; private set; }
	public string Title { get; private set; }
	public DocKind Kind => DocKind.Docx;
	public double Zoom => zoom;
	public string StatusText {
		get {
			var fmt = string.Equals(Path.GetExtension(FilePath), ".doc", StringComparison.OrdinalIgnoreCase)
				? "DOC" : "DOCX";
			return $"{fmt}  第 {CurrentPage}/{PageCount} 页  ·  {(int)(zoom * 100)}%  ·  连续分页（可选字）";
		}
	}
	public int PageCount => Math.Max(1, pageCount);
	public int CurrentPage {
		get {
			var n = estimatepage();
			if (n < 1) n = 1;
			if (n > PageCount) n = PageCount;
			return n;
		}
	}

	/// <summary>单页占位高度（含页间距），未缩放。</summary>
	double pagepitch => pageH + PAGE_GAP;

	public event Action StatusChanged;
	/// <summary>滚动定位章节时：理想大纲 1-based 页码（主窗章节列表镜像用）。</summary>
	public event Action<int> OutlineHighlightChanged;

	public DocxViewer() {
		tree = new TreeView {
			BorderThickness = new Thickness(0),
			Background = Brushes.Transparent,
			Padding = new Thickness(0, 0, 0, 4),
		};
		OutlineUi.ConfigureTree(tree);
		tree.SelectedItemChanged += (_, _) => {
			if (syncTree) return;
			if (tree.SelectedItem is TreeViewItem ti && ti.Tag is TocEntry te)
				gototoc(te);
		};
		// 用户展开/折叠后，按当前页重算可见路径上的高亮章节
		tree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(onoutlineexpandcollapse));
		tree.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(onoutlineexpandcollapse));
		lboutline = new TextBlock {
			Text = "无目录",
			Margin = new Thickness(10, 4, 10, 4),
			Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
		};
		eoutline = OutlineUi.MakeFilterBox();
		eoutline.TextChanged += (_, _) => {
			outlineQuery = eoutline.Text?.Trim() ?? "";
			rebuildtocui();
		};
		var btoggle = new Button {
			Content = "«", Width = 28, Height = 22, Padding = new Thickness(0),
			ToolTip = "隐藏目录", Cursor = Cursors.Hand,
			Background = Brushes.Transparent, BorderThickness = new Thickness(0),
		};
		btoggle.Click += (_, _) => setside(!sideVisible);
		var head = new DockPanel { Margin = new Thickness(8, 6, 4, 4) };
		DockPanel.SetDock(btoggle, Dock.Right);
		head.Children.Add(btoggle);
		head.Children.Add(new TextBlock {
			Text = "目录", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
			FontSize = DocViewOptions.Current.UiFontSize,
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
			Background = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
			BorderThickness = new Thickness(0, 0, 1, 0),
			ClipToBounds = true,
			Child = sideBody,
		};

		// 分页竖排：每页 RichTextBox（可选字）+ 页框，外层 ScrollViewer 连续滚（仿 PDF）
		pageStack = new StackPanel {
			Orientation = Orientation.Vertical,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		scaleXf = new ScaleTransform(1, 1);
		pageHost = new Border {
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(16, 16, 16, 32),
			Child = pageStack,
			LayoutTransform = scaleXf,
			SnapsToDevicePixels = false,
			UseLayoutRounding = false,
		};
		pagePad = new Grid {
			Background = Brushes.Transparent,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
		};
		pagePad.Children.Add(pageHost);
		pageScroll = new ScrollViewer {
			Content = pagePad,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
			Focusable = true,
			PanningMode = PanningMode.Both,
			CanContentScroll = false,
		};
		pageScroll.SizeChanged += (_, _) => updatepagepad();
		pageScroll.ScrollChanged += (_, _) => {
			synctoc();
			StatusChanged?.Invoke();
			// 滚动后刷新可见页上的查找高亮
			if (findHits.Count > 0) applyfindhighlights(force: false);
		};
		pageScroll.PreviewMouseWheel += onpreviewwheel;
		// 空白拖平移；点到文字区域仍可尝试选字（DocumentPage 内字形）
		pageScroll.PreviewMouseLeftButtonDown += onpanorselectdown;
		pageScroll.PreviewMouseRightButtonUp += onpagectximage;
		pageScroll.PreviewMouseMove += onpanmove;
		pageScroll.PreviewMouseLeftButtonUp += onpanup;
		pageScroll.LostMouseCapture += (_, _) => { if (panning) endpan(); };
		pageScroll.PreviewMouseDown += (_, e) => {
			if (e.ChangedButton != MouseButton.Middle) return;
			beginpan(e.GetPosition(pageScroll));
			e.Handled = true;
		};
		pageScroll.PreviewMouseUp += (_, e) => {
			if (e.ChangedButton == MouseButton.Middle && panning) {
				endpan();
				e.Handled = true;
			}
		};
		applyzoomsize();

		var main = new Grid();
		main.Children.Add(pageScroll);

		root = new Grid();
		colside = new ColumnDefinition { Width = new GridLength(SIDE_W) };
		root.ColumnDefinitions.Add(colside);
		root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
		root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		var sp = new GridSplitter {
			Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch,
			Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
			ResizeBehavior = GridResizeBehavior.PreviousAndNext,
		};
		Grid.SetColumn(pside, 0);
		Grid.SetColumn(sp, 1);
		Grid.SetColumn(main, 2);
		root.Children.Add(pside);
		root.Children.Add(sp);
		root.Children.Add(main);
		flow = newflow();
		// 构造时侧栏先隐藏，Load 后按是否有 TOC 决定
		setside(false);
	}

	public void Load(string path) => Load(path, null);

	/// <param name="fileBytes">后台线程预读的字节；null 则现场打开。</param>
	public void Load(string path, byte[] fileBytes) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("文件不存在", path);
		FilePath = Path.GetFullPath(path);
		Title = Path.GetFileName(path);

		// 单次共享打开（避免先整文件读入再解析，双倍耗时）
		Stream stream = fileBytes != null && fileBytes.Length > 0
			? new MemoryStream(fileBytes, writable: false)
			: DocFileIo.OpenReadShared(FilePath);
		var ext = Path.GetExtension(FilePath).ToLowerInvariant();
		if (ext == ".doc") {
			using (stream)
				loaddoclegacy(stream);
			return;
		}
		using (stream) {
			using var word = WordprocessingDocument.Open(stream, false);
			mainPart = word.MainDocumentPart;
			var body = mainPart?.Document?.Body;
			loadstyles(mainPart);
			loadnumbering(mainPart);
			readpagesettings(body);

			flow = newflow();
			applypagesize(flow);
			pendingPageBreak = false;
			tocEntries.Clear();
			numCounters.Clear();

			if (body == null) {
				flow.Blocks.Add(new Paragraph(new Run("(空文档)")));
			} else {
				// 解析段：少泵消息，靠时间节流即可
				var pump = 0;
				foreach (var el in body.Elements()) {
					addbodyel(el);
					UiPump.Every(ref pump, 40);
				}
			}
			if (flow.Blocks.Count == 0)
				flow.Blocks.Add(new Paragraph(new Run("(无可显示内容)")));

			// 分页 → 竖向一页接一页（仿 PDF）
			applyzoomsize();
			buildpages();
			pageScroll.ScrollToVerticalOffset(0);
			buildtocui();
			mainPart = null;
		}
		DocViewLog.Info($"Docx Load pages~={pageCount} toc={tocEntries.Count} stacked");
		StatusChanged?.Invoke();
	}

	void loaddoclegacy(Stream stream) {
		flow = newflow();
		pageW = DEF_PAGE_W;
		pageH = DEF_PAGE_H;
		padL = padT = padR = padB = DEF_MARGIN;
		applypagesize(flow);
		pendingPageBreak = false;
		tocEntries.Clear();
		numCounters.Clear();

		LegacyOfficeLoader.LoadDocInto(flow, stream);
		if (flow.Blocks.Count == 0)
			flow.Blocks.Add(new Paragraph(new Run("(无可显示内容)")));

		applyzoomsize();
		buildpages();
		pageScroll.ScrollToVerticalOffset(0);
		buildtocui();
		mainPart = null;
		DocViewLog.Info($"Doc Load (legacy) pages~={pageCount} toc={tocEntries.Count}");
		StatusChanged?.Invoke();
	}

	/// <summary>解析 body 子节点；递归展开 w:sdt（Word 目录常包在内容控件内）。</summary>
	void addbodyel(DocumentFormat.OpenXml.OpenXmlElement el) {
		if (el == null) return;
		if (el is W.Paragraph p) {
			addparagraph(p);
			return;
		}
		if (el is W.Table t) {
			if (pendingPageBreak) {
				flow.Blocks.Add(new Paragraph(new Run("\u00A0")) {
					BreakPageBefore = true, Margin = new Thickness(0), LineHeight = 1,
				});
				pendingPageBreak = false;
			}
			flow.Blocks.Add(buildtable(t));
			return;
		}
		if (el is W.SdtBlock sdt) {
			var content = sdt.SdtContentBlock;
			if (content != null)
				foreach (var ch in content.Elements())
					addbodyel(ch);
			return;
		}
		// 其它容器：尝试其子元素（兼容性）
		if (el.HasChildren && el is not W.SectionProperties) {
			foreach (var ch in el.Elements())
				addbodyel(ch);
		}
	}

	/// <summary>
	/// 分页后竖向堆叠：每页一个只读 RichTextBox（可选字）+ 页框。
	/// </summary>
	void buildpages() {
		clearpages();
		if (flow == null) return;
		try {
			paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
			paginator.PageSize = new Size(pageW, pageH);
			// 强制算出页数（稀疏泵消息，兼顾速度与可拖窗）
			var n = 0;
			while (n < 800) {
				var dp = paginator.GetPage(n);
				if (dp == DocumentPage.Missing) break;
				try { dp.Dispose(); } catch { /* ignore */ }
				n++;
				if (n % 5 == 0) UiPump.Once();
				if (paginator.IsPageCountValid && n >= paginator.PageCount) break;
			}
			pageCount = Math.Max(1, paginator.IsPageCountValid ? paginator.PageCount : Math.Max(1, n));

			var ddp = paginator as DynamicDocumentPaginator;
			// TOC 页码
			if (ddp != null) {
				foreach (var te in tocEntries) {
					if (te.Para == null) continue;
					try {
						var pg = ddp.GetPageNumber(te.Para.ContentStart);
						te.Page1 = pg >= 0 ? pg + 1 : 1;
					} catch {
						te.Page1 = 1;
					}
				}
			}

			// 每页内容起止 TextPointer
			var starts = new TextPointer[pageCount];
			var ends = new TextPointer[pageCount];
			if (ddp != null)
				fillpageranges(ddp, starts, ends);
			else {
				starts[0] = flow.ContentStart;
				ends[0] = flow.ContentEnd;
			}

			// 每页：可见 RTB 显示该页文本。高度随内容增高，避免固定 contentH 裁掉底行。
			getpagemetrics(out var mL, out var mT, out var mR, out var mB, out var contentW, out var contentH);
			// 中文行盒最小高度，防止 LineHeight 过小把字裁成半截
			var minLineH = pt2dip(10.5) * 1.35;

			var pump = 0;
			for (var i = 0; i < pageCount; i++) {
				var start = starts[i] ?? flow.ContentStart;
				var end = ends[i] ?? flow.ContentEnd;
				var pageDoc = cloneflowrange(start, end, contentW);
				ensurerlineheight(pageDoc, minLineH);

				var rtb = new RichTextBox {
					Document = pageDoc,
					IsReadOnly = true,
					IsDocumentEnabled = true,
					IsUndoEnabled = false,
					BorderThickness = new Thickness(0),
					Padding = new Thickness(0),
					Background = Brushes.White,
					HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
					// Disabled + 不设固定 Height：RTB 随文档长高，底行不会被视口裁掉
					VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
					Width = contentW,
					Focusable = true,
					Cursor = Cursors.IBeam,
					ClipToBounds = false,
					HorizontalAlignment = HorizontalAlignment.Left,
					VerticalAlignment = VerticalAlignment.Top,
				};
				pageDoc.PageWidth = contentW;
				pageDoc.ColumnWidth = contentW;
				pageDoc.PagePadding = new Thickness(0);

				// 测内容高度：至少一页内容区，不够再加高（可吃掉底边距，仍不够则撑高页框）
				var bodyH = contentH;
				try {
					rtb.Measure(new Size(contentW, double.PositiveInfinity));
					var need = rtb.DesiredSize.Height;
					if (!double.IsNaN(need) && !double.IsInfinity(need) && need > 1)
						bodyH = Math.Max(contentH, need + 2);
				} catch { /* keep contentH */ }
				rtb.Height = bodyH;
				rtb.MinHeight = contentH;

				var frameH = Math.Max(pageH, mT + bodyH + mB);
				// 底边距：内容已吃进原底边距时缩小 padding，避免总高过大
				var padB = mB;
				if (bodyH > contentH) {
					var overflow = bodyH - contentH;
					padB = Math.Max(8, mB - overflow);
				}

				rtb.PreviewMouseLeftButtonDown += onpageboxmousedown;
				rtb.PreviewMouseMove += onpageboxmousemove;

				var label = new TextBlock {
					Text = $"{i + 1} / {pageCount}",
					FontSize = 11,
					Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
					HorizontalAlignment = HorizontalAlignment.Right,
					VerticalAlignment = VerticalAlignment.Bottom,
					Margin = new Thickness(0, 0, 8, 6),
					IsHitTestVisible = false,
				};
				var inner = new Border {
					Padding = new Thickness(mL, mT, mR, padB),
					Background = Brushes.White,
					ClipToBounds = false,
					Child = rtb,
					VerticalAlignment = VerticalAlignment.Top,
				};
				var grid = new Grid {
					Width = pageW,
					MinHeight = pageH,
					Height = frameH,
					Background = Brushes.White,
					ClipToBounds = false,
				};
				grid.Children.Add(inner);
				grid.Children.Add(label);
				var frame = new Border {
					Width = pageW,
					MinHeight = pageH,
					Height = frameH,
					Background = Brushes.White,
					BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
					BorderThickness = new Thickness(1),
					Margin = new Thickness(0, 0, 0, PAGE_GAP),
					Child = grid,
					SnapsToDevicePixels = true,
					ClipToBounds = false,
					ToolTip = $"第 {i + 1} 页",
				};

				pageBoxes.Add(rtb);
				pageStack.Children.Add(frame);
				UiPump.Every(ref pump, 3);
			}
			pageHost.Width = pageW;
			// 页高可能因内容略增高，滚动估算仍用名义 pageH（偏差可接受）
			DocViewLog.Info($"Docx buildpages ok n={pageCount} size={pageW:F0}x{pageH:F0} contentW={contentW:F0} rtb-autoH");
		} catch (Exception ex) {
			DocViewLog.Error("Docx buildpages", ex);
			pageCount = 1;
		}
	}

	/// <summary>根据分页器给每页填 Content 起止指针。</summary>
	void fillpageranges(DynamicDocumentPaginator ddp, TextPointer[] starts, TextPointer[] ends) {
		// 扫描插入点，记录每页首次出现
		for (var tp = flow.ContentStart;
			tp != null && tp.CompareTo(flow.ContentEnd) < 0;
			tp = tp.GetNextContextPosition(LogicalDirection.Forward)) {
			int pg;
			try { pg = ddp.GetPageNumber(tp); }
			catch { continue; }
			if (pg < 0 || pg >= pageCount) continue;
			if (starts[pg] == null)
				starts[pg] = tp;
		}
		for (var i = 0; i < pageCount; i++) {
			if (starts[i] == null)
				starts[i] = i == 0 ? flow.ContentStart : (starts[i - 1] ?? flow.ContentStart);
		}
		for (var i = 0; i < pageCount; i++) {
			if (i + 1 < pageCount)
				ends[i] = starts[i + 1] ?? flow.ContentEnd;
			else
				ends[i] = flow.ContentEnd;
		}
	}

	/// <summary>
	/// 复制 TextRange 为独立 FlowDocument（保留格式，可选题）。
	/// 使用与分页器相同的内容区宽度，且不限制 PageHeight，避免二次分页丢掉页末文字。
	/// </summary>
	FlowDocument cloneflowrange(TextPointer start, TextPointer end, double contentW) {
		var doc = newflow();
		if (contentW < 40) contentW = 40;
		doc.PageWidth = contentW;
		doc.ColumnWidth = contentW;
		doc.ColumnGap = 0;
		doc.PagePadding = new Thickness(0);
		// 不设 PageHeight：整段范围连续排布，由外层 RTB 高度约束可视区
		if (start == null || end == null || start.CompareTo(end) >= 0) {
			doc.Blocks.Add(new Paragraph(new Run("")));
			return doc;
		}
		try {
			var range = new TextRange(start, end);
			using var ms = new MemoryStream();
			range.Save(ms, DataFormats.XamlPackage);
			ms.Position = 0;
			var dest = new TextRange(doc.ContentStart, doc.ContentEnd);
			dest.Load(ms, DataFormats.XamlPackage);
		} catch (Exception ex) {
			DocViewLog.Warn($"cloneflowrange: {ex.Message}");
			// 回退纯文本
			try {
				var t = new TextRange(start, end).Text ?? "";
				doc.Blocks.Clear();
				doc.Blocks.Add(new Paragraph(new Run(t)));
			} catch {
				doc.Blocks.Add(new Paragraph(new Run("")));
			}
		}
		// 再次钉死栏宽，防止 Load 后被包内页设置改掉
		doc.PageWidth = contentW;
		doc.ColumnWidth = contentW;
		doc.ColumnGap = 0;
		doc.PagePadding = new Thickness(0);
		return doc;
	}

	/// <summary>与分页器一致的页边距与内容区尺寸（最小边距 24 DIP）。</summary>
	void getpagemetrics(out double mL, out double mT, out double mR, out double mB,
		out double contentW, out double contentH) {
		mL = Math.Max(padL, 24);
		mT = Math.Max(padT, 24);
		mR = Math.Max(padR, 24);
		mB = Math.Max(padB, 24);
		contentW = Math.Max(80, pageW - mL - mR);
		contentH = Math.Max(80, pageH - mT - mB);
	}

	/// <summary>克隆后抬高过小行距，避免中文底半截被行盒裁切。</summary>
	static void ensurerlineheight(FlowDocument doc, double minLineH) {
		if (doc == null || minLineH < 1) return;
		try {
			foreach (var block in doc.Blocks)
				ensurerlineheightblock(block, minLineH);
		} catch { /* ignore */ }
	}

	static void ensurerlineheightblock(Block block, double minLineH) {
		if (block == null) return;
		if (block is Paragraph para) {
			if (double.IsNaN(para.LineHeight) || para.LineHeight < minLineH)
				para.LineHeight = minLineH;
		} else if (block is Section sec) {
			foreach (var b in sec.Blocks)
				ensurerlineheightblock(b, minLineH);
		} else if (block is Table table) {
			foreach (var rg in table.RowGroups)
			foreach (var row in rg.Rows)
			foreach (var cell in row.Cells)
			foreach (var b in cell.Blocks)
				ensurerlineheightblock(b, minLineH);
		} else if (block is List list) {
			foreach (var item in list.ListItems)
			foreach (var b in item.Blocks)
				ensurerlineheightblock(b, minLineH);
		}
	}

	void clearpages() {
		clearfindhighlights();
		findHits.Clear();
		findQuery = null;
		findIndex = -1;
		pageBoxes.Clear();
		pageStack.Children.Clear();
		paginator = null;
	}

	/// <summary>页内空白：箭头光标 + 取消选区 + pan；文字上：IBeam 选字。</summary>
	void onpageboxmousedown(object sender, MouseButtonEventArgs e) {
		if (sender is not RichTextBox rtb) return;
		if (istextover(e.OriginalSource as DependencyObject)) {
			rtb.Cursor = Cursors.IBeam;
			return;
		}
		// 非文字：取消本页及其它页选区
		clearallselections();
		rtb.Cursor = Cursors.Arrow;
		beginpan(e.GetPosition(pageScroll));
		e.Handled = true;
	}

	void onpageboxmousemove(object sender, MouseEventArgs e) {
		if (sender is not RichTextBox rtb) return;
		if (panning) {
			rtb.Cursor = Cursors.Hand;
			return;
		}
		rtb.Cursor = istextover(e.OriginalSource as DependencyObject)
			? Cursors.IBeam
			: Cursors.Arrow;
	}

	void clearallselections() {
		foreach (var box in pageBoxes) {
			try {
				if (box?.Selection == null || box.Selection.IsEmpty) continue;
				var caret = box.Selection.Start;
				box.Selection.Select(caret, caret);
			} catch { /* ignore */ }
		}
	}

	public void SetZoom(double z) => setzoomcore(z, null);

	/// <param name="mouseInScroll">鼠标相对 pageScroll 的位置；用 pageHost 局部坐标锚定（LayoutTransform+居中不能 content*scale）。</param>
	void setzoomcore(double z, WpfPoint? mouseInScroll) {
		z = clamp(z, MIN_ZOOM, MAX_ZOOM);
		if (Math.Abs(z - zoom) < 0.0005) return;

		WpfPoint mouse = default;
		WpfPoint? docLocal = null;
		if (mouseInScroll.HasValue && pageScroll != null && pageHost != null && pagePad != null) {
			mouse = mouseInScroll.Value;
			try {
				// 滚动内容坐标 → pageHost 局部（未缩放逻辑坐标）
				var contentPt = new WpfPoint(
					pageScroll.HorizontalOffset + mouse.X,
					pageScroll.VerticalOffset + mouse.Y);
				docLocal = pagePad.TranslatePoint(contentPt, pageHost);
			} catch {
				docLocal = null;
			}
		}

		zoom = z;
		applyzoomsize();
		try {
			pageHost?.UpdateLayout();
			pagePad?.UpdateLayout();
			pageScroll?.UpdateLayout();
		} catch { /* ignore */ }

		// 缩放后：同一文档点若偏离鼠标，用滚动补回（真正「以鼠标为中心」）
		if (docLocal.HasValue && pageScroll != null && pageHost != null) {
			try {
				var now = pageHost.TranslatePoint(docLocal.Value, pageScroll);
				var dx = now.X - mouse.X;
				var dy = now.Y - mouse.Y;
				if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01) {
					pageScroll.ScrollToHorizontalOffset(Math.Max(0, pageScroll.HorizontalOffset + dx));
					pageScroll.ScrollToVerticalOffset(Math.Max(0, pageScroll.VerticalOffset + dy));
				}
			} catch { /* ignore */ }
		}
		StatusChanged?.Invoke();
	}

	public void ZoomBy(double factor) => SetZoom(zoom * factor);
	public void ZoomIn() => SetZoom(zoom * 1.15);
	public void ZoomOut() => SetZoom(zoom / 1.15);
	public void ZoomFitWidth() {
		if (pageScroll.ViewportWidth <= 1 || pageW < 1) return;
		SetZoom(Math.Max(MIN_ZOOM, (pageScroll.ViewportWidth - 48) / pageW));
	}
	public void ZoomFitPage() {
		if (pageScroll.ViewportWidth <= 1 || pageScroll.ViewportHeight <= 1 || pageW < 1 || pageH < 1) return;
		var zx = (pageScroll.ViewportWidth - 48) / pageW;
		var zy = (pageScroll.ViewportHeight - 48) / pageH;
		SetZoom(Math.Max(MIN_ZOOM, Math.Min(zx, zy)));
	}
	public void RotateBy(int deltaQuarterTurns) { /* DOCX 不旋转 */ }

	public void CaptureViewState(out double h, out double v, out double z, out int sheetOrPage) {
		h = pageScroll?.HorizontalOffset ?? 0;
		v = pageScroll?.VerticalOffset ?? 0;
		z = zoom;
		sheetOrPage = CurrentPage;
	}

	public void RestoreViewState(double h, double v, double z, int sheetOrPage) {
		if (z > 0.05 && Math.Abs(z - zoom) > 0.001)
			SetZoom(z);
		try {
			pageScroll?.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
				try {
					if (pageScroll == null) return;
					if (h > 0) pageScroll.ScrollToHorizontalOffset(h);
					if (v > 0) pageScroll.ScrollToVerticalOffset(v);
					else if (sheetOrPage > 0)
						GoToPage(sheetOrPage);
					var cur = CurrentPage;
					var target = cur;
					if (sheetOrPage > 0)
						target = Math.Max(target, sheetOrPage);
					queueoutlinerestore(target);
				} catch { /* ignore */ }
			}));
		} catch { /* ignore */ }
	}

	/// <summary>恢复位置后多次同步目录（滚位置/视口就绪有先后）。</summary>
	void queueoutlinerestore(int page1) {
		if (page1 < 1) page1 = 1;
		if (page1 > PageCount) page1 = PageCount;
		restoreOutlinePage = page1;
		pendingExpandOutline = true;
		var token = ++outlineRestoreToken;
		void once() {
			if (token != outlineRestoreToken) return;
			try {
				var cur = CurrentPage;
				if (cur > restoreOutlinePage)
					restoreOutlinePage = cur;
				pendingExpandOutline = true;
				synctoc(force: true);
			} catch { /* ignore */ }
		}
		once();
		try {
			pageScroll.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(once));
			pageScroll.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(once));
			pageScroll.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
				once();
				if (token == outlineRestoreToken)
					restoreOutlinePage = 0;
			}));
		} catch { /* ignore */ }
	}

	void applyzoomsize() {
		// 页宽固定；总高度 = 页数 × 页高；LayoutTransform 整叠缩放
		pageHost.Width = Math.Max(120, pageW);
		var z = clamp(zoom, MIN_ZOOM, MAX_ZOOM);
		if (Math.Abs(scaleXf.ScaleX - z) > 0.0001 || Math.Abs(scaleXf.ScaleY - z) > 0.0001) {
			scaleXf.ScaleX = z;
			scaleXf.ScaleY = z;
		}
		updatepagepad();
	}

	/// <summary>垫层至少铺满视口宽度，正文水平居中。</summary>
	void updatepagepad() {
		if (pagePad == null || pageScroll == null) return;
		var vw = pageScroll.ViewportWidth;
		if (vw < 1) vw = pageScroll.ActualWidth;
		if (vw < 1) return;
		pagePad.MinWidth = vw;
		// 高度随内容，不强制铺满，避免底部大片空白难滚
	}

	void onpreviewwheel(object sender, MouseWheelEventArgs e) {
		if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
			e.Handled = true;
			// 以鼠标在滚动视口中的位置为缩放中心
			var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
			setzoomcore(zoom * factor, e.GetPosition(pageScroll));
			return;
		}
		// 按视口高度比例滚动（非翻页）
		e.Handled = true;
		var step = Math.Max(48, pageScroll.ViewportHeight * WHEEL_STEP);
		if (e.Delta > 0)
			pageScroll.ScrollToVerticalOffset(Math.Max(0, pageScroll.VerticalOffset - step));
		else
			pageScroll.ScrollToVerticalOffset(pageScroll.VerticalOffset + step);
	}

	void onpanorselectdown(object sender, MouseButtonEventArgs e) {
		if (panning) return;
		// 滚动条 / 滑块 / 上下箭头：必须交给 ScrollViewer 原生子控件，否则无法拖条翻页
		if (isscrollbarhit(e.OriginalSource as DependencyObject)) return;
		// 内嵌图片：点击放大查看（与 PDF 相同），不要被平移/选字抢走
		var img = findimage(e.OriginalSource as DependencyObject);
		if (img != null) {
			e.Handled = true;
			pageScroll.Cursor = Cursors.Hand;
			if (e.ClickCount <= 1) showimage(img);
			return;
		}
		// 点在文字上 → 交给 RichTextBox 选字
		if (istextover(e.OriginalSource as DependencyObject)) {
			pageScroll.Cursor = Cursors.IBeam;
			return;
		}
		// 页外空白 / 页缝：取消选择 + 平移
		clearallselections();
		pageScroll.Cursor = Cursors.Arrow;
		beginpan(e.GetPosition(pageScroll));
		e.Handled = true;
	}

	void onpanmove(object sender, MouseEventArgs e) {
		if (panning && e.LeftButton == MouseButtonState.Pressed) {
			dopan(e.GetPosition(pageScroll));
			e.Handled = true;
			return;
		}
		if (!panning) {
			var hit = e.OriginalSource as DependencyObject;
			if (isscrollbarhit(hit)) {
				pageScroll.Cursor = Cursors.Arrow;
				return;
			}
			pageScroll.Cursor = istextover(hit) ? Cursors.IBeam : Cursors.Arrow;
		}
	}

	void onpanup(object sender, MouseButtonEventArgs e) {
		if (!panning) return;
		endpan();
		e.Handled = true;
	}

	/// <summary>点击内嵌图片：放大查看（ImageOverlay，与 PDF 一致）。</summary>
	static void showimage(System.Windows.Controls.Image img) {
		var bs = img?.Tag as BitmapSource ?? img?.Source as BitmapSource;
		if (bs != null) ImageOverlay.Show(bs);
	}

	/// <summary>右键图片：弹出与 PDF 相同的菜单（预览 / 复制 / 另存为）。</summary>
	void onpagectximage(object sender, MouseButtonEventArgs e) {
		var img = findimage(e.OriginalSource as DependencyObject);
		if (img == null) return; // 文字区域的右键仍交给 RichTextBox（复制文字）
		var bs = img.Tag as BitmapSource ?? img.Source as BitmapSource;
		if (bs == null) return;
		e.Handled = true;
		var cm = img.ContextMenu ?? buildimgmenu(bs);
		img.ContextMenu = cm;
		cm.PlacementTarget = img;
		cm.Placement = PlacementMode.MousePoint;
		cm.IsOpen = true;
	}

	/// <summary>从命中元素向上找内嵌图片（Image 不在逻辑树常规路径上，逐层安全上溯）。</summary>
	static System.Windows.Controls.Image findimage(DependencyObject d) {
		while (d != null) {
			if (d is System.Windows.Controls.Image img
				&& (img.Tag is BitmapSource || img.Source is BitmapSource))
				return img;
			if (d is RichTextBox || d is ScrollViewer) return null;
			d = safeparent(d);
		}
		return null;
	}

	/// <summary>命中 ScrollViewer 自带滚动条（含 Thumb / Track / 箭头）则勿抢鼠标。</summary>
	static bool isscrollbarhit(DependencyObject d) {
		while (d != null) {
			if (d is ScrollBar || d is Thumb || d is Track)
				return true;
			// 滚动条箭头是 RepeatButton
			if (d is RepeatButton) {
				var p = safeparent(d);
				while (p != null) {
					if (p is ScrollBar) return true;
					if (p is ScrollViewer) break;
					p = safeparent(p);
				}
			}
			d = safeparent(d);
		}
		return false;
	}

	void beginpan(WpfPoint pt) {
		panning = true;
		panStart = pt;
		panOffX = pageScroll.HorizontalOffset;
		panOffY = pageScroll.VerticalOffset;
		try { pageScroll.CaptureMouse(); } catch { /* ignore */ }
		pageScroll.Cursor = Cursors.Hand;
	}

	void dopan(WpfPoint pt) {
		if (!panning) return;
		pageScroll.ScrollToHorizontalOffset(Math.Max(0, panOffX - (pt.X - panStart.X)));
		pageScroll.ScrollToVerticalOffset(Math.Max(0, panOffY - (pt.Y - panStart.Y)));
	}

	void endpan() {
		if (!panning) return;
		panning = false;
		try { pageScroll.ReleaseMouseCapture(); } catch { /* ignore */ }
		pageScroll.Cursor = Cursors.Arrow;
	}

	/// <summary>是否点在正文文字上（Run/Glyphs 等），空白段落块不算。</summary>
	static bool istextover(DependencyObject d) {
		while (d != null) {
			if (d is Run || d is Hyperlink || d is InlineUIContainer)
				return true;
			var name = d.GetType().Name;
			if (name.IndexOf("Glyph", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("TextLine", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("LineVisual", StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
			if (d is DocumentPageView || d is ScrollViewer || d is Border || d is Grid || d is StackPanel)
				return false;
			d = safeparent(d);
		}
		return false;
	}

	/// <summary>
	/// Paragraph/Run 等 ContentElement 不是 Visual，不能用 VisualTreeHelper.GetParent。
	/// </summary>
	static DependencyObject safeparent(DependencyObject d) {
		if (d == null) return null;
		try {
			if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
				return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
			return LogicalTreeHelper.GetParent(d);
		} catch {
			try { return LogicalTreeHelper.GetParent(d); }
			catch { return null; }
		}
	}

	public void GoPrevPage() {
		GoToPage(CurrentPage - 1);
	}
	public void GoNextPage() {
		GoToPage(CurrentPage + 1);
	}
	public void GoToPage(int page1Based) {
		try {
			var n = Math.Max(1, Math.Min(PageCount, page1Based));
			// 页框 + 间距；滚动偏移在缩放后坐标系
			var y = 16 * zoom + (n - 1) * pagepitch * zoom;
			pageScroll.ScrollToVerticalOffset(Math.Max(0, y));
		} catch (Exception ex) {
			DocViewLog.Warn($"GoToPage: {ex.Message}");
		}
		StatusChanged?.Invoke();
	}

	int estimatepage() {
		if (pagepitch < 1 || zoom < 1e-6) return 1;
		var y = (pageScroll.VerticalOffset + pageScroll.ViewportHeight * 0.2) / zoom - 16;
		if (y < 0) y = 0;
		var p = (int)(y / pagepitch) + 1;
		if (p < 1) p = 1;
		return p;
	}

	public bool TryCopySelection() {
		try {
			// 任一页 RichTextBox 有选区则复制
			foreach (var rtb in pageBoxes) {
				if (rtb.Selection == null || rtb.Selection.IsEmpty) continue;
				var t = rtb.Selection.Text;
				if (string.IsNullOrEmpty(t)) continue;
				Clipboard.SetDataObject(t, true);
				DocViewLog.Info($"Docx copy len={t.Length}");
				return true;
			}
			return false;
		} catch (Exception ex) {
			DocViewLog.Error("Docx copy", ex);
			return false;
		}
	}

	public bool HasOutline => hasOutline;

	/// <summary>主窗侧栏 TOC：标题 / 层级 / 1-based 页码。</summary>
	public List<(string Title, int Level, int Page1)> GetOutlineSnapshot() {
		var list = new List<(string, int, int)>();
		try {
			foreach (var t in tocEntries) {
				if (t == null) continue;
				list.Add((t.Title ?? "", t.Level, t.Page1));
			}
		} catch { /* ignore */ }
		return list;
	}

	/// <summary>
	/// 主窗章节列表高亮：当前页对应的大纲 1-based 页码；无则 -1。
	/// </summary>
	public int GetActiveOutlinePage1() {
		if (!hasOutline || tocEntries.Count == 0) return -1;
		try {
			var peek = CurrentPage;
			TocEntry best = null;
			foreach (var te in tocEntries) {
				if (te == null || te.Page1 <= 0 || te.Page1 > peek) continue;
				best = te;
			}
			return best != null ? best.Page1 : -1;
		} catch {
			return -1;
		}
	}
	public bool SidePanelVisible => false;
	public void SetSidePanelVisible(bool show) => setside(false);

	public FindResult Find(string text, bool forward, bool ignoreCase, bool restart = false, bool fromView = false) {
		if (string.IsNullOrEmpty(text) || pageBoxes.Count == 0)
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
				clearfindhighlights();
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
			DocViewLog.Error("Docx Find", ex);
			return FindResult.Miss(findHits.Count);
		}
	}

	public void ClearFind() {
		clearfindhighlights();
		findHits.Clear();
		findQuery = null;
		findIgnoreCase = true;
		findIndex = -1;
		lastFindHlFirst = lastFindHlLast = -1;
		// 清掉各页 Selection 黄选
		try { clearotherselections(-1); } catch { /* ignore */ }
	}

	void rebuildfindhits(string text, bool ignoreCase) {
		clearfindhighlights();
		findHits.Clear();
		findQuery = text;
		findIgnoreCase = ignoreCase;
		findIndex = -1;
		lastFindHlFirst = lastFindHlLast = -1;
		var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		for (var p = 0; p < pageBoxes.Count; p++) {
			var rtb = pageBoxes[p];
			if (rtb?.Document == null) continue;
			if (!collectruns(rtb.Document, out var runs, out var full) || full.Length == 0)
				continue;
			var from = 0;
			while (from < full.Length) {
				var idx = full.IndexOf(text, from, cmp);
				if (idx < 0) break;
				findHits.Add((p, idx, idx + text.Length));
				from = idx + Math.Max(1, text.Length);
			}
		}
		DocViewLog.Info($"Docx find rebuild q={text} hits={findHits.Count}");
	}

	/// <summary>
	/// 从当前视口页起取命中。afterCurrent：当前命中仍在视口页及以下时取下一个；
	/// 已滚离则从新视口首个起。
	/// </summary>
	int pickfindfromview(bool forward, bool afterCurrent) {
		if (findHits.Count == 0) return -1;
		var page = 0;
		try {
			if (pagepitch >= 1 && zoom >= 1e-6 && pageScroll != null) {
				var y = pageScroll.VerticalOffset / zoom - 16;
				if (y < 0) y = 0;
				page = (int)(y / pagepitch);
			} else {
				page = Math.Max(0, estimatepage() - 1);
			}
		} catch {
			page = Math.Max(0, estimatepage() - 1);
		}
		if (page < 0) page = 0;
		if (page >= pageBoxes.Count) page = Math.Max(0, pageBoxes.Count - 1);

		var curStillInOrBelow = false;
		if (afterCurrent && findIndex >= 0 && findIndex < findHits.Count)
			curStillInOrBelow = findHits[findIndex].Page >= page;

		if (forward) {
			if (curStillInOrBelow)
				return (findIndex + 1) % findHits.Count;
			for (var i = 0; i < findHits.Count; i++)
				if (findHits[i].Page >= page) return i;
			return 0;
		}
		if (curStillInOrBelow)
			return (findIndex - 1 + findHits.Count) % findHits.Count;
		for (var i = findHits.Count - 1; i >= 0; i--)
			if (findHits[i].Page <= page) return i;
		return findHits.Count - 1;
	}

	void jumptofindhit(int i) {
		if (i < 0 || i >= findHits.Count) return;
		var h = findHits[i];
		if (h.Page < 0 || h.Page >= pageBoxes.Count) return;
		var rtb = pageBoxes[h.Page];
		if (rtb?.Document == null) return;
		if (!collectruns(rtb.Document, out var runs, out _)) return;
		if (!tryhitpointers(runs, h.Start, h.End, out var p0, out var p1)) return;

		clearotherselections(h.Page);
		rtb.Selection.Select(p0, p1);
		GoToPage(h.Page + 1);
		try {
			// 不抢焦点：保持工具栏查找框，便于连续 Enter
			rtb.BringIntoView();
			var rect = p0.GetCharacterRect(LogicalDirection.Forward);
			if (!rect.IsEmpty)
				rtb.ScrollToVerticalOffset(Math.Max(0, rtb.VerticalOffset + rect.Top - 40));
		} catch { /* ignore */ }
		// 跳转后刷新可见页全部命中高亮
		applyfindhighlights(force: true);
	}

	/// <summary>估算当前视口内的页范围（0-based 闭区间）。</summary>
	void vispagerange(out int first, out int last) {
		first = 0;
		last = Math.Max(0, pageBoxes.Count - 1);
		if (pageBoxes.Count == 0 || pageScroll == null || pagepitch < 1 || zoom < 1e-6) return;
		try {
			var top = (pageScroll.VerticalOffset / zoom) - 16;
			var bot = ((pageScroll.VerticalOffset + pageScroll.ViewportHeight) / zoom) - 16;
			if (top < 0) top = 0;
			if (bot < top) bot = top;
			first = (int)(top / pagepitch);
			last = (int)(bot / pagepitch);
			if (first < 0) first = 0;
			if (last < first) last = first;
			if (first >= pageBoxes.Count) first = pageBoxes.Count - 1;
			if (last >= pageBoxes.Count) last = pageBoxes.Count - 1;
			// 扩一页，半页交界不丢高亮
			first = Math.Max(0, first - 1);
			last = Math.Min(pageBoxes.Count - 1, last + 1);
		} catch {
			var cur = Math.Max(1, estimatepage()) - 1;
			first = Math.Max(0, cur - 1);
			last = Math.Min(pageBoxes.Count - 1, cur + 1);
		}
		// 当前命中页始终纳入
		if (findIndex >= 0 && findIndex < findHits.Count) {
			var p = findHits[findIndex].Page;
			if (p < first) first = p;
			if (p > last) last = p;
		}
	}

	void applyfindhighlights(bool force) {
		if (findHits.Count == 0 || string.IsNullOrEmpty(findQuery)) {
			clearfindhighlights();
			return;
		}
		vispagerange(out var first, out var last);
		if (!force && first == lastFindHlFirst && last == lastFindHlLast
			&& findHlRanges.Count > 0) return;
		clearfindhighlights();
		lastFindHlFirst = first;
		lastFindHlLast = last;

		// 按页缓存 runs，避免同页重复 collect
		List<(int Off, TextPointer Ptr, string Txt)> runs = null;
		var runsPage = -1;
		for (var i = 0; i < findHits.Count; i++) {
			var h = findHits[i];
			if (h.Page < first || h.Page > last) continue;
			if (h.Page != runsPage) {
				var rtb = pageBoxes[h.Page];
				if (rtb?.Document == null) { runs = null; runsPage = -1; continue; }
				if (!collectruns(rtb.Document, out runs, out _)) { runs = null; runsPage = -1; continue; }
				runsPage = h.Page;
			}
			if (runs == null) continue;
			if (!tryhitpointers(runs, h.Start, h.End, out var tp0, out var tp1)) continue;
			try {
				var tr = new TextRange(tp0, tp1);
				tr.ApplyPropertyValue(TextElement.BackgroundProperty,
					i == findIndex ? FindCurBrush : FindHitBrush);
				findHlRanges.Add(tr);
			} catch { /* ignore single hit */ }
		}
	}

	void clearfindhighlights() {
		if (findHlRanges.Count == 0) {
			lastFindHlFirst = lastFindHlLast = -1;
			return;
		}
		foreach (var tr in findHlRanges) {
			try {
				tr.ApplyPropertyValue(TextElement.BackgroundProperty, null);
			} catch { /* ignore */ }
		}
		findHlRanges.Clear();
		lastFindHlFirst = lastFindHlLast = -1;
	}

	void clearotherselections(int keepPage) {
		for (var i = 0; i < pageBoxes.Count; i++) {
			if (i == keepPage) continue;
			var rtb = pageBoxes[i];
			if (rtb?.Document == null) continue;
			try {
				rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentStart);
			} catch { /* ignore */ }
		}
	}

	public void Dispose() {
		try {
			if (outlineDebounce != null) {
				outlineDebounce.Stop();
				outlineDebounce = null;
			}
		} catch { /* ignore */ }
		clearpages();
		flow = null;
		mainPart = null;
	}

	/// <summary>文档内嵌目录侧栏已废弃（改用主窗「章节列表」），始终隐藏。</summary>
	void setside(bool show) {
		sideVisible = false;
		colside.Width = new GridLength(0);
		pside.Visibility = Visibility.Collapsed;
		StatusChanged?.Invoke();
	}

	void buildtocui() {
		try {
			outlineQuery = "";
			if (eoutline != null) eoutline.Text = "";
			if (tocEntries.Count == 0) {
				hasOutline = false;
				tree.Items.Clear();
				lboutline.Text = "无目录";
				lboutline.Visibility = Visibility.Visible;
				if (eoutline != null) eoutline.Visibility = Visibility.Collapsed;
				tree.Items.Add(new TreeViewItem {
					Header = OutlineUi.MakeHeader($"共 {PageCount} 页", "", ""),
					IsEnabled = false,
				});
				setside(false);
				return;
			}
			hasOutline = true;
			if (eoutline != null) eoutline.Visibility = Visibility.Visible;
			setside(false);
			rebuildtocui();
			synctoc(force: true);
		} catch (Exception ex) {
			DocViewLog.Error("Docx buildtocui", ex);
			hasOutline = false;
			setside(false);
		}
	}

	/// <summary>按筛选关键字重建目录（只显示匹配项；祖先路径用占位 level 保留嵌套）。</summary>
	void rebuildtocui() {
		tree.Items.Clear();
		foreach (var te in tocEntries)
			te.Item = null;
		if (tocEntries.Count == 0) return;

		var q = outlineQuery ?? "";
		// 标记哪些条目应显示：自身匹配，或是匹配项的祖先
		var show = new bool[tocEntries.Count];
		if (string.IsNullOrWhiteSpace(q)) {
			for (var i = 0; i < show.Length; i++) show[i] = true;
		} else {
			for (var i = 0; i < tocEntries.Count; i++) {
				if (!OutlineUi.Match(tocEntries[i].Title, q)) continue;
				show[i] = true;
				// 向上标祖先
				var lv = tocEntries[i].Level < 1 ? 1 : tocEntries[i].Level;
				for (var j = i - 1; j >= 0; j--) {
					var lj = tocEntries[j].Level < 1 ? 1 : tocEntries[j].Level;
					if (lj < lv) {
						show[j] = true;
						lv = lj;
						if (lv <= 1) break;
					}
				}
			}
		}

		var any = false;
		for (var i = 0; i < show.Length; i++)
			if (show[i]) { any = true; break; }
		if (!any) {
			lboutline.Text = "无匹配章节";
			lboutline.Visibility = Visibility.Visible;
			return;
		}
		lboutline.Visibility = Visibility.Collapsed;

		syncTree = true;
		try {
			var stack = new Stack<TreeViewItem>();
			// 筛选时展开匹配路径；默认全部折叠，仅恢复位置时再展开
			var expandAll = !string.IsNullOrWhiteSpace(q);
			for (var i = 0; i < tocEntries.Count; i++) {
				if (!show[i]) continue;
				var te = tocEntries[i];
				var level = te.Level < 1 ? 1 : te.Level;
				var pageSuffix = te.Page1 > 0 ? $"  ·  {te.Page1}" : "";
				var item = new TreeViewItem {
					Header = OutlineUi.MakeHeader(te.Title ?? "", pageSuffix, q),
					Tag = te,
					IsExpanded = expandAll,
				};
				te.Item = item;
				while (stack.Count >= level && stack.Count > 0)
					stack.Pop();
				if (stack.Count == 0)
					tree.Items.Add(item);
				else
					stack.Peek().Items.Add(item);
				stack.Push(item);
			}
		} finally {
			syncTree = false;
		}
	}

	void gototoc(TocEntry te) {
		if (te == null) return;
		try {
			if (te.Page1 > 0)
				GoToPage(te.Page1);
			else if (te.Para != null && paginator is DynamicDocumentPaginator ddp) {
				var pg = ddp.GetPageNumber(te.Para.ContentStart);
				if (pg >= 0) GoToPage(pg + 1);
			}
		} catch (Exception ex) {
			DocViewLog.Warn($"gototoc: {ex.Message}");
		}
		StatusChanged?.Invoke();
	}

	void onoutlineexpandcollapse(object sender, RoutedEventArgs e) {
		if (syncTree) return;
		// 等 IsExpanded 状态落定后再同步选中
		try {
			tree.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
				if (syncTree) return;
				synctoc(force: true);
			}));
		} catch { /* ignore */ }
	}

	/// <summary>同步目录选中。滚动时防抖；force/恢复立即生效。</summary>
	void synctoc(bool force = false) {
		if (syncTree || !hasOutline || tocEntries.Count == 0) return;
		if (!force && !pendingExpandOutline) {
			var peek = CurrentPage;
			if (peek == lastTocPage) {
				// 页未变：若高亮已对则跳过；错了仍防抖纠正
				TocEntry bestPeek = null;
				foreach (var te in tocEntries) {
					if (te.Page1 <= 0 || te.Page1 > peek || te.Item == null) continue;
					bestPeek = te;
				}
				var want = bestPeek != null ? OutlineUi.FindVisibleOnPath(bestPeek.Item) : null;
				if (want != null && ReferenceEquals(tree.SelectedItem, want))
					return;
			}
			scheduleoutlinedebounce();
			return;
		}
		stopoutlinedebounce();
		applytocsync(force, center: force || pendingExpandOutline);
	}

	void scheduleoutlinedebounce() {
		if (outlineDebounce == null) {
			outlineDebounce = new DispatcherTimer(DispatcherPriority.Background) {
				Interval = TimeSpan.FromMilliseconds(OUTLINE_DEBOUNCE_MS),
			};
			outlineDebounce.Tick += (_, _) => {
				try { outlineDebounce.Stop(); } catch { /* ignore */ }
				applytocsync(force: false, center: false);
			};
		}
		outlineDebounce.Stop();
		outlineDebounce.Start();
	}

	void stopoutlinedebounce() {
		try { outlineDebounce?.Stop(); } catch { /* ignore */ }
	}

	/// <summary>
	/// 按当前页同步目录选中：文档顺序中最后一个 Page≤当前页 的项
	/// （避免「最大页码」把先前章节的偏大书签盖住后续正确章节）。
	/// 默认不自动展开；pendingExpandOutline 时展开路径。滚动时最小滚入可视区。
	/// </summary>
	void applytocsync(bool force, bool center) {
		if (syncTree || !hasOutline || tocEntries.Count == 0) return;
		var page = CurrentPage;
		if (restoreOutlinePage > 0 && page < restoreOutlinePage)
			page = restoreOutlinePage;
		// 文档序：后出现的覆盖先前的
		TocEntry best = null;
		foreach (var te in tocEntries) {
			if (te == null || te.Page1 <= 0 || te.Page1 > page) continue;
			best = te;
		}
		if (best == null) return;
		// 主窗章节列表镜像
		try { OutlineHighlightChanged?.Invoke(best.Page1); } catch { /* ignore */ }
		if (best.Item == null) {
			lastTocPage = page;
			return;
		}
		if (pendingExpandOutline) {
			syncTree = true;
			try { OutlineUi.ExpandAncestors(best.Item); }
			finally { syncTree = false; }
			pendingExpandOutline = false;
		}
		var sel = OutlineUi.FindVisibleOnPath(best.Item);
		if (sel == null) return;
		if (ReferenceEquals(tree.SelectedItem, sel)) {
			lastTocPage = page;
			return;
		}
		lastTocPage = page;
		syncTree = true;
		try {
			if (tree.SelectedItem is TreeViewItem old && !ReferenceEquals(old, sel))
				old.IsSelected = false;
			sel.IsSelected = true;
			OutlineUi.ScrollItemIntoView(sel, center);
		} catch { /* ignore */ }
		finally { syncTree = false; }
	}

	// ---------- 样式 / 页面 ----------
	void loadstyles(MainDocumentPart main) {
		styleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		stylePPrs = new Dictionary<string, StylePPr>(StringComparer.OrdinalIgnoreCase);
		styleRPrs = new Dictionary<string, StyleRPr>(StringComparer.OrdinalIgnoreCase);
		styleBasedOn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var styles = main?.StyleDefinitionsPart?.Styles;
		if (styles == null) return;
		foreach (var st in styles.Elements<W.Style>()) {
			var id = st.StyleId?.Value;
			if (string.IsNullOrEmpty(id)) continue;
			var name = st.StyleName?.Val?.Value;
			if (!string.IsNullOrEmpty(name))
				styleNames[id] = name.ToLowerInvariant();
			var based = st.BasedOn?.Val?.Value;
			if (!string.IsNullOrEmpty(based))
				styleBasedOn[id] = based;
			var sp = parsestyleppr(st.StyleParagraphProperties);
			if (sp != null)
				stylePPrs[id] = sp;
			// 样式默认字符格式（Title 的 sz=32 / b 等）
			var sr = parserpr(st.StyleRunProperties);
			if (sr != null)
				styleRPrs[id] = sr;
		}
	}

	/// <summary>解析 w:rPr（样式或 run）；字号 half-point → 磅。</summary>
	static StyleRPr parserpr(OpenXmlElement rPr) {
		if (rPr == null) return null;
		var sr = new StyleRPr();
		var any = false;
		// Bold / Italic：OpenXml 元素存在即开（Val 缺省=true）
		var b = rPr.GetFirstChild<W.Bold>();
		if (b != null) {
			sr.Bold = b.Val == null || b.Val.Value;
			any = true;
		}
		var i = rPr.GetFirstChild<W.Italic>();
		if (i != null) {
			sr.Italic = i.Val == null || i.Val.Value;
			any = true;
		}
		var sz = rPr.GetFirstChild<W.FontSize>();
		if (sz?.Val?.Value != null && double.TryParse(sz.Val.Value, out var hp) && hp > 0) {
			sr.FontSizePt = hp / 2.0;
			any = true;
		}
		var fonts = rPr.GetFirstChild<W.RunFonts>();
		if (fonts != null) {
			var name = fonts.EastAsia?.Value ?? fonts.Ascii?.Value ?? fonts.HighAnsi?.Value;
			if (!string.IsNullOrEmpty(name)) {
				sr.FontName = name;
				any = true;
			}
		}
		return any ? sr : null;
	}

	static StylePPr parsestyleppr(W.StyleParagraphProperties pPr) {
		if (pPr == null) return null;
		var sp = new StylePPr();
		var any = false;
		var np = pPr.NumberingProperties;
		if (np?.NumberingId?.Val?.Value != null) {
			sp.NumId = np.NumberingId.Val.Value;
			sp.Ilvl = np.NumberingLevelReference?.Val?.Value ?? 0;
			any = true;
		}
		var spacing = pPr.SpacingBetweenLines;
		if (spacing != null) {
			if (spacing.Before != null && int.TryParse(spacing.Before.Value, out var b)) {
				sp.Before = b * TWIP2DIP;
				any = true;
			}
			if (spacing.After != null && int.TryParse(spacing.After.Value, out var a)) {
				sp.After = a * TWIP2DIP;
				any = true;
			}
			if (spacing.Line != null && int.TryParse(spacing.Line.Value, out var line) && line > 0) {
				sp.Line = line;
				sp.LineAuto = spacing.LineRule == null
					|| spacing.LineRule.Value == W.LineSpacingRuleValues.Auto;
				any = true;
			}
		}
		var ind = pPr.Indentation;
		if (ind != null) {
			if (ind.Left != null && int.TryParse(ind.Left.Value, out var li)) { sp.Left = li * TWIP2DIP; any = true; }
			if (ind.Right != null && int.TryParse(ind.Right.Value, out var ri)) { sp.Right = ri * TWIP2DIP; any = true; }
			if (ind.FirstLine != null && int.TryParse(ind.FirstLine.Value, out var fi)) { sp.First = fi * TWIP2DIP; any = true; }
			if (ind.Hanging != null && int.TryParse(ind.Hanging.Value, out var hi)) { sp.Hanging = hi * TWIP2DIP; any = true; }
		}
		// 对齐：Title 等样式常只在 style pPr 写 jc，段落上无 jc
		if (pPr.Justification?.Val != null) {
			sp.Align = mapjustify(pPr.Justification.Val.Value);
			any = true;
		}
		return any ? sp : null;
	}

	static TextAlignment mapjustify(W.JustificationValues v) {
		if (v == W.JustificationValues.Center) return TextAlignment.Center;
		if (v == W.JustificationValues.Right) return TextAlignment.Right;
		if (v == W.JustificationValues.Both) return TextAlignment.Justify;
		// left / start / distribute 等按左
		return TextAlignment.Left;
	}

	/// <summary>合并样式继承链上的段落属性（祖先 → 自身）。</summary>
	StylePPr resolvestyleppr(string styleId) {
		if (string.IsNullOrEmpty(styleId)) return null;
		var chain = new List<string>();
		var guard = 0;
		for (var id = styleId; !string.IsNullOrEmpty(id) && guard++ < 24; ) {
			chain.Add(id);
			if (!styleBasedOn.TryGetValue(id, out var parent)) break;
			id = parent;
		}
		StylePPr acc = null;
		// 从基样式到当前
		for (var i = chain.Count - 1; i >= 0; i--) {
			if (!stylePPrs.TryGetValue(chain[i], out var layer)) continue;
			if (acc == null) acc = new StylePPr();
			if (layer.NumId != null) { acc.NumId = layer.NumId; acc.Ilvl = layer.Ilvl; }
			if (layer.Before != null) acc.Before = layer.Before;
			if (layer.After != null) acc.After = layer.After;
			if (layer.Line != null) { acc.Line = layer.Line; acc.LineAuto = layer.LineAuto; }
			if (layer.Left != null) acc.Left = layer.Left;
			if (layer.Right != null) acc.Right = layer.Right;
			if (layer.First != null) acc.First = layer.First;
			if (layer.Hanging != null) acc.Hanging = layer.Hanging;
			if (layer.Align != null) acc.Align = layer.Align;
		}
		return acc;
	}

	/// <summary>合并样式继承链上的默认 run 属性（祖先 → 自身）。</summary>
	StyleRPr resolvestylerpr(string styleId) {
		if (string.IsNullOrEmpty(styleId)) return null;
		var chain = new List<string>();
		var guard = 0;
		for (var id = styleId; !string.IsNullOrEmpty(id) && guard++ < 24; ) {
			chain.Add(id);
			if (!styleBasedOn.TryGetValue(id, out var parent)) break;
			id = parent;
		}
		StyleRPr acc = null;
		for (var i = chain.Count - 1; i >= 0; i--) {
			if (!styleRPrs.TryGetValue(chain[i], out var layer)) continue;
			if (acc == null) acc = new StyleRPr();
			if (layer.FontSizePt != null) acc.FontSizePt = layer.FontSizePt;
			if (layer.Bold != null) acc.Bold = layer.Bold;
			if (layer.Italic != null) acc.Italic = layer.Italic;
			if (!string.IsNullOrEmpty(layer.FontName)) acc.FontName = layer.FontName;
		}
		return acc;
	}

	/// <summary>加载 numbering.xml：多级编号 / 项目符号。</summary>
	void loadnumbering(MainDocumentPart main) {
		numToAbs = new Dictionary<int, int>();
		absLevels = new Dictionary<int, NumLevel[]>();
		numCounters = new Dictionary<int, int[]>();
		var numbering = main?.NumberingDefinitionsPart?.Numbering;
		if (numbering == null) return;
		foreach (var abs in numbering.Elements<W.AbstractNum>()) {
			var aid = abs.AbstractNumberId?.Value;
			if (aid == null) continue;
			var levels = new NumLevel[9];
			foreach (var lvl in abs.Elements<W.Level>()) {
				// w:ilvl
				var i = 0;
				if (lvl.LevelIndex?.Value != null)
					i = (int)lvl.LevelIndex.Value;
				if (i < 0 || i > 8) continue;
				var nl = new NumLevel();
				var fv = lvl.NumberingFormat?.Val?.Value;
				nl.Fmt = fv != null ? fv.ToString() : "Decimal";
				// 部分包 Val 为空时用 InnerText
				if (string.IsNullOrEmpty(nl.Fmt) || nl.Fmt == "0")
					nl.Fmt = lvl.NumberingFormat?.Val?.InnerText ?? "Decimal";
				nl.LvlText = lvl.LevelText?.Val?.Value ?? "%1.";
				nl.Start = lvl.StartNumberingValue?.Val?.Value ?? 1;
				// 级别缩进：w:pPr/w:ind（PreviousParagraphProperties）
				var ind = lvl.PreviousParagraphProperties?.Indentation
					?? lvl.Descendants<W.Indentation>().FirstOrDefault();
				if (ind != null) {
					if (ind.Left != null && int.TryParse(ind.Left.Value, out var li))
						nl.Left = li * TWIP2DIP;
					if (ind.Hanging != null && int.TryParse(ind.Hanging.Value, out var hi))
						nl.Hanging = hi * TWIP2DIP;
					if (ind.FirstLine != null && int.TryParse(ind.FirstLine.Value, out var fi))
						nl.FirstLine = fi * TWIP2DIP;
				}
				levels[i] = nl;
			}
			absLevels[aid.Value] = levels;
		}
		foreach (var num in numbering.Elements<W.NumberingInstance>()) {
			var nid = num.NumberID?.Value;
			var aid = num.AbstractNumId?.Val?.Value;
			if (nid != null && aid != null)
				numToAbs[nid.Value] = aid.Value;
		}
		DocViewLog.Info($"Docx numbering abs={absLevels.Count} num={numToAbs.Count}");
	}

	/// <summary>解析段上 + 样式继承的编号 id/级别。</summary>
	void resolvenumref(W.Paragraph p, string styleId, out int? numId, out int ilvl) {
		numId = null;
		ilvl = 0;
		var np = p.ParagraphProperties?.NumberingProperties;
		if (np?.NumberingId?.Val?.Value != null) {
			numId = np.NumberingId.Val.Value;
			ilvl = np.NumberingLevelReference?.Val?.Value ?? 0;
			return;
		}
		// 编号常只写在 List Paragraph 等样式上
		var sp = resolvestyleppr(styleId);
		if (sp?.NumId != null) {
			numId = sp.NumId;
			ilvl = sp.Ilvl;
		}
	}

	/// <summary>取本段编号/项目符文本，并推进计数器；无编号返回 null。</summary>
	string takenumber(int? numId, int ilvl, out double left, out double hang) {
		left = 0;
		hang = 0;
		if (numId == null) return null;
		if (!numToAbs.TryGetValue(numId.Value, out var absId)) return null;
		if (!absLevels.TryGetValue(absId, out var levels)) return null;
		if (ilvl < 0 || ilvl > 8 || levels[ilvl] == null) return null;
		var lvl = levels[ilvl];
		left = lvl.Left;
		hang = lvl.Hanging;

		if (!numCounters.TryGetValue(numId.Value, out var ctr)) {
			ctr = new int[9];
			numCounters[numId.Value] = ctr;
		}
		if (ctr[ilvl] <= 0)
			ctr[ilvl] = lvl.Start > 0 ? lvl.Start : 1;
		else
			ctr[ilvl]++;
		for (var j = ilvl + 1; j < 9; j++)
			ctr[j] = 0;

		var fmtName = lvl.Fmt ?? "Decimal";
		if (isbulletfmt(fmtName)) {
			// Word 箭头等多在 Wingdings 私用区，中文字体常显示为 □
			// 统一用通用项目符 •（U+2022），各字体均能显示
			return "• ";
		}

		var text = lvl.LvlText ?? "%1.";
		for (var i = 0; i <= ilvl; i++) {
			var n = ctr[i];
			if (n <= 0)
				n = levels[i]?.Start > 0 ? levels[i].Start : 1;
			var f = levels[i]?.Fmt ?? "Decimal";
			text = text.Replace("%" + (i + 1), formatlistnum(f, n));
		}
		for (var i = 1; i <= 9; i++)
			text = text.Replace("%" + i, "");
		if (text.Length > 0 && !char.IsWhiteSpace(text[text.Length - 1]))
			text += " ";
		return text;
	}

	static bool isbulletfmt(string fmt) {
		if (string.IsNullOrEmpty(fmt)) return false;
		return fmt.Equals("Bullet", StringComparison.OrdinalIgnoreCase)
			|| fmt.IndexOf("bullet", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static string formatlistnum(string fmt, int n) {
		if (n < 0) n = 0;
		if (string.IsNullOrEmpty(fmt)) return n.ToString();
		switch (fmt.ToLowerInvariant()) {
		case "decimal":
		case "japanesecounting":
		case "decimalfullwidth":
			return n.ToString();
		case "upperletter":
			return toletters(n, true);
		case "lowerletter":
			return toletters(n, false);
		case "upperroman":
			return toroman(n, true);
		case "lowerroman":
			return toroman(n, false);
		default:
			return n.ToString();
		}
	}

	static string toletters(int n, bool upper) {
		if (n <= 0) return upper ? "A" : "a";
		var sb = new StringBuilder();
		while (n > 0) {
			n--;
			sb.Insert(0, (char)((upper ? 'A' : 'a') + n % 26));
			n /= 26;
		}
		return sb.ToString();
	}

	static string toroman(int n, bool upper) {
		if (n <= 0) return upper ? "I" : "i";
		if (n > 3999) return n.ToString();
		var map = new[] {
			(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
			(100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
			(10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
		};
		var sb = new StringBuilder();
		foreach (var (v, s) in map) {
			while (n >= v) {
				sb.Append(s);
				n -= v;
			}
		}
		var r = sb.ToString();
		return upper ? r : r.ToLowerInvariant();
	}

	void readpagesettings(W.Body body) {
		pageW = DEF_PAGE_W;
		pageH = DEF_PAGE_H;
		padL = padT = padR = padB = DEF_MARGIN;
		var sect = body?.Elements<W.SectionProperties>().LastOrDefault()
			?? body?.Descendants<W.SectionProperties>().LastOrDefault();
		if (sect == null) return;
		var sz = sect.GetFirstChild<W.PageSize>();
		if (sz?.Width?.Value != null && sz.Height?.Value != null) {
			pageW = sz.Width.Value * TWIP2DIP;
			pageH = sz.Height.Value * TWIP2DIP;
		}
		var mar = sect.GetFirstChild<W.PageMargin>();
		if (mar != null) {
			if (mar.Left?.Value != null) padL = mar.Left.Value * TWIP2DIP;
			if (mar.Right?.Value != null) padR = mar.Right.Value * TWIP2DIP;
			if (mar.Top?.Value != null) padT = mar.Top.Value * TWIP2DIP;
			if (mar.Bottom?.Value != null) padB = mar.Bottom.Value * TWIP2DIP;
		}
		DocViewLog.Info($"Docx page {pageW:F0}x{pageH:F0} margin L{padL:F0} T{padT:F0} R{padR:F0} B{padB:F0}");
	}

	FlowDocument newflow() => new FlowDocument {
		FontFamily = new FontFamily("宋体, SimSun, Times New Roman, Microsoft YaHei UI, Segoe UI"),
		FontSize = pt2dip(10.5),
		TextAlignment = TextAlignment.Left,
		Background = Brushes.White,
		ColumnGap = 0,
		IsHyphenationEnabled = false,
		IsOptimalParagraphEnabled = false,
	};

	void applypagesize(FlowDocument d) {
		// 固定页尺寸 + 页边距；必须与 buildpages 显示用 metrics 一致
		getpagemetrics(out var l, out var t, out var r, out var b, out var col, out _);
		d.PageWidth = pageW;
		d.PageHeight = pageH;
		d.PagePadding = thicknonneg(l, t, r, b);
		d.ColumnWidth = col;
		d.ColumnGap = 0;
	}

	// ---------- 内容 ----------
	void addparagraph(W.Paragraph p) {
		var hasPageBreak = p.Descendants<W.Break>()
			.Any(b => b.Type != null && b.Type.Value == W.BreakValues.Page);
		var para = buildpara(p);
		if (pendingPageBreak) {
			para.BreakPageBefore = true;
			pendingPageBreak = false;
		}
		flow.Blocks.Add(para);
		if (hasPageBreak) pendingPageBreak = true;

		// 侧栏目录：仅章节标题（不含 toc 域样式、封面 Title）
		var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
		string styleName = null;
		if (styleId != null) styleNames.TryGetValue(styleId, out styleName);
		var level = headinglevel(styleName, styleId);
		if (level > 0) {
			var title = string.Concat(p.Descendants<W.Text>().Select(t => t.Text)).Trim();
			if (title.Length > 0) {
				tocEntries.Add(new TocEntry {
					Title = title.Length > 80 ? title.Substring(0, 80) + "…" : title,
					Level = level,
					Para = para,
				});
			}
		}
	}

	/// <summary>章节标题层级；toc / 封面 Title 不进侧栏。</summary>
	static int headinglevel(string styleName, string styleId) {
		if (styleName != null) {
			var n = styleName;
			// 正文目录样式（toc 1 / TOC 标题1 等）
			if (n.StartsWith("toc") || n.Contains("toc ") || n.Contains(" toc"))
				return 0;
			if (n == "title" || n == "标题")
				return 0;
			if (n.Contains("heading 1") || n.Contains("标题 1")) return 1;
			if (n.Contains("heading 2") || n.Contains("标题 2")) return 2;
			if (n.Contains("heading 3") || n.Contains("标题 3")) return 3;
		}
		// 常见 toc styleId：10/20/30、TOC1
		if (styleId != null) {
			if (styleId.Equals("TOC1", StringComparison.OrdinalIgnoreCase)
				|| styleId == "10" || styleId == "20" || styleId == "30"
				|| styleId == "a3")
				return 0;
			if (styleId == "1") return 1;
			if (styleId == "2") return 2;
			if (styleId == "3") return 3;
		}
		return 0;
	}

	static bool istocstyle(string styleName, string styleId) {
		if (styleName != null) {
			var n = styleName;
			if (n.StartsWith("toc") || n.Contains("toc ") || n.Contains(" toc"))
				return true;
		}
		if (styleId != null) {
			if (styleId.Equals("TOC1", StringComparison.OrdinalIgnoreCase))
				return true;
			if (styleId == "10" || styleId == "20" || styleId == "30")
				return true;
		}
		return false;
	}

	/// <summary>段落对齐：样式 → 段属性覆盖。</summary>
	static void applyalignment(Paragraph para, StylePPr styleP, W.ParagraphProperties pPr) {
		if (styleP?.Align != null)
			para.TextAlignment = styleP.Align.Value;
		if (pPr?.Justification?.Val != null)
			para.TextAlignment = mapjustify(pPr.Justification.Val.Value);
	}

	Paragraph buildpara(W.Paragraph p) {
		var para = new Paragraph { Margin = new Thickness(0) };
		var pPr = p.ParagraphProperties;
		var styleId = pPr?.ParagraphStyleId?.Val?.Value;
		string styleName = null;
		if (styleId != null) styleNames.TryGetValue(styleId, out styleName);
		var styleP = resolvestyleppr(styleId);

		var hasDrawing = p.Descendants<W.Drawing>().Any();
		var plainText = string.Concat(p.Descendants<W.Text>().Select(t => t.Text ?? ""));
		resolvenumref(p, styleId, out var numId, out var numIlvl);
		var isEmpty = !hasDrawing && string.IsNullOrWhiteSpace(plainText)
			&& !p.Descendants<W.Break>().Any(b => b.Type == null || b.Type.Value != W.BreakValues.Page)
			&& numId == null;

		// 段前/段后/行距：样式 → 直接属性
		double mL = styleP?.Left ?? 0, mT = styleP?.Before ?? 0, mR = styleP?.Right ?? 0, mB = styleP?.After ?? 0;
		double first = 0;
		// 注意：List Paragraph 常带 firstLine，有列表编号时必须忽略（改用悬挂）
		if (numId == null) {
			if (styleP?.Hanging != null) first = -styleP.Hanging.Value;
			else if (styleP?.First != null) first = styleP.First.Value;
		}
		if (styleP?.Line != null && styleP.Line > 0) {
			if (styleP.LineAuto)
				para.LineHeight = pt2dip(10.5) * (styleP.Line.Value / 240.0);
			else
				para.LineHeight = styleP.Line.Value * TWIP2DIP;
		}

		var sp = pPr?.SpacingBetweenLines;
		if (sp != null) {
			if (sp.Before != null && int.TryParse(sp.Before.Value, out var before)) mT = before * TWIP2DIP;
			if (sp.After != null && int.TryParse(sp.After.Value, out var after)) mB = after * TWIP2DIP;
			if (sp.Line != null && int.TryParse(sp.Line.Value, out var line) && line > 0) {
				if (sp.LineRule == null || sp.LineRule.Value == W.LineSpacingRuleValues.Auto)
					para.LineHeight = pt2dip(10.5) * (line / 240.0);
				else
					para.LineHeight = line * TWIP2DIP;
			}
		}
		// 默认行距约 1.15，避免段内挤成一团
		if (double.IsNaN(para.LineHeight) || para.LineHeight < 1)
			para.LineHeight = pt2dip(10.5) * 1.15;

		var ind = pPr?.Indentation;
		if (ind != null && numId == null) {
			// 无列表时才用段上缩进；有列表时以 numbering 级别为准
			if (ind.Left != null && int.TryParse(ind.Left.Value, out var li)) mL = li * TWIP2DIP;
			if (ind.Right != null && int.TryParse(ind.Right.Value, out var ri)) mR = ri * TWIP2DIP;
			if (ind.FirstLine != null && int.TryParse(ind.FirstLine.Value, out var fi)) first = fi * TWIP2DIP;
			if (ind.Hanging != null && int.TryParse(ind.Hanging.Value, out var hi)) first = -hi * TWIP2DIP;
		} else if (ind != null) {
			if (ind.Right != null && int.TryParse(ind.Right.Value, out var ri)) mR = ri * TWIP2DIP;
		}

		// 自动编号 / 项目符号（含样式上的 numPr）
		var numText = takenumber(numId, numIlvl, out var numLeft, out var numHang);
		if (numText != null) {
			// Word：left=正文列，hanging=符号区。WPF：Margin.Left=left，TextIndent=-hanging
			if (numLeft > 0.1)
				mL = numLeft;
			else if (numHang > 0.1)
				mL = numHang; // 仅有 hanging 时至少留出符号宽
			first = numHang > 0.1 ? -numHang : 0;
			// 符号+正文最小间距：悬挂太小时略加大
			if (numHang > 0.1 && numHang < 12)
				first = -14;
			if (mB < 2) mB = 3;
		}

		if (isEmpty && numText == null) {
			para.Inlines.Add(new Run("\u00A0"));
			if (double.IsNaN(para.LineHeight) || para.LineHeight < 1)
				para.LineHeight = pt2dip(15.6);
			para.Margin = thicknonneg(mL, mT, mR, mB);
			applyalignment(para, styleP, pPr);
			return para;
		}

		para.Margin = thicknonneg(mL, mT, mR, mB);
		para.TextIndent = first; // 含 0：清除样式遗留的首行缩进

		// 对齐：样式 jc（Title=center）→ 段落 jc 覆盖
		applyalignment(para, styleP, pPr);
		if (hasDrawing && string.IsNullOrWhiteSpace(plainText))
			para.TextAlignment = TextAlignment.Center;

		// 样式默认字符：Title 等 sz/b 只在 style rPr，run 上常无字号
		var styleR = resolvestylerpr(styleId);
		if (styleR != null) {
			if (styleR.FontSizePt != null && styleR.FontSizePt.Value > 0)
				para.FontSize = pt2dip(styleR.FontSizePt.Value);
			if (styleR.Bold == true)
				para.FontWeight = FontWeights.Bold;
			if (styleR.Italic == true)
				para.FontStyle = FontStyles.Italic;
			if (!string.IsNullOrEmpty(styleR.FontName))
				para.FontFamily = new FontFamily(styleR.FontName + ", 宋体, SimSun, Microsoft YaHei UI");
		}

		var lv = headinglevel(styleName, styleId);
		// 章节标题：无样式字号时用默认阶梯；有样式字号以样式为准
		if (lv == 1) {
			para.FontWeight = FontWeights.Bold;
			if (styleR?.FontSizePt == null) para.FontSize = pt2dip(18);
			para.Margin = thicknonneg(mL, Math.Max(mT, 14), mR, Math.Max(mB, 8));
		} else if (lv == 2) {
			para.FontWeight = FontWeights.Bold;
			if (styleR?.FontSizePt == null) para.FontSize = pt2dip(14);
			para.Margin = thicknonneg(mL, Math.Max(mT, 12), mR, Math.Max(mB, 6));
		} else if (lv == 3) {
			para.FontWeight = FontWeights.SemiBold;
			if (styleR?.FontSizePt == null) para.FontSize = pt2dip(12);
			para.Margin = thicknonneg(mL, Math.Max(mT, 10), mR, Math.Max(mB, 4));
		}

		// 正文目录：编号 | 标题 | 页码（Word 两制表符）
		if (istocstyle(styleName, styleId)) {
			buildtocline(para, p, styleId, styleName);
			return para;
		}

		if (!string.IsNullOrEmpty(numText)) {
			var nr = new Run(numText);
			// 编号/项目符跟随正文，• 在宋体/雅黑中均可显示
			if (para.FontSize > 0) nr.FontSize = para.FontSize;
			if (para.FontWeight != FontWeights.Normal) nr.FontWeight = para.FontWeight;
			para.Inlines.Add(nr);
		}

		foreach (var child in p.ChildElements) {
			if (child is W.Run run)
				addruntopara(para, run, styleR);
			else if (child is W.Hyperlink hl) {
				foreach (var hr in hl.Descendants<W.Run>())
					addruntopara(para, hr, styleR);
			} else if (child is W.SdtRun sdtRun) {
				var sc = sdtRun.SdtContentRun;
				if (sc != null)
					foreach (var hr in sc.Elements<W.Run>())
						addruntopara(para, hr, styleR);
			}
		}
		if (para.Inlines.Count == 0)
			para.Inlines.Add(new Run("\u00A0"));
		return para;
	}

	/// <summary>
	/// 目录行：Word 结构为「编号 [TAB] 标题 [TAB] 页码」→「1. 概述 …… 3」。
	/// </summary>
	void buildtocline(Paragraph para, W.Paragraph p, string styleId, string styleName) {
		// 按制表符切成多段
		var segs = new List<string> { "" };
		foreach (var run in p.Descendants<W.Run>()) {
			if (run.Elements<W.TabChar>().Any() || run.Descendants<W.PositionalTab>().Any()) {
				segs.Add("");
				continue;
			}
			var t = string.Concat(run.Elements<W.Text>().Select(x => x.Text ?? ""));
			if (t.Length > 0)
				segs[segs.Count - 1] = segs[segs.Count - 1] + t;
		}
		// 去掉空段
		var parts = segs.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
		string num = "", title = "", page = "";
		if (parts.Count >= 3) {
			// 编号 | 标题 | 页码（末段当页码）
			num = parts[0];
			page = parts[parts.Count - 1];
			title = string.Join(" ", parts.Skip(1).Take(parts.Count - 2));
		} else if (parts.Count == 2) {
			// 标题 | 页码 或 编号标题 | 页码
			title = parts[0];
			page = parts[1];
			// 若第二段不像页码，整行当标题
			if (!page.All(char.IsDigit) && page.Length > 4) {
				title = parts[0] + " " + parts[1];
				page = "";
			}
		} else if (parts.Count == 1) {
			title = parts[0];
			var i = title.Length - 1;
			while (i >= 0 && char.IsDigit(title[i])) i--;
			if (i < title.Length - 1 && i >= 0) {
				page = title.Substring(i + 1).Trim();
				title = title.Substring(0, i + 1).Trim();
			}
		}

		var label = string.IsNullOrEmpty(num)
			? title
			: (num.EndsWith(".") || num.EndsWith("、") ? num + " " + title : num + " " + title);
		label = label.Trim();

		var tocLv = 1;
		if (styleId == "20" || (styleName != null && (styleName.Contains("toc 2") || styleName.EndsWith("2"))))
			tocLv = 2;
		else if (styleId == "30" || (styleName != null && (styleName.Contains("toc 3") || styleName.EndsWith("3"))))
			tocLv = 3;
		var indent = (tocLv - 1) * 18.0;
		para.Margin = thicknonneg(indent, 2, 0, 2);
		para.FontSize = pt2dip(tocLv == 1 ? 11 : 10.5);
		para.LineHeight = pt2dip(16);

		if (label.Length == 0 && page.Length == 0) {
			para.Inlines.Add(new Run("\u00A0"));
			return;
		}
		para.Inlines.Add(new Run(label.Length > 0 ? label : "\u00A0"));
		if (page.Length > 0) {
			var contentW = Math.Max(80, pageW - padL - padR - indent - 24);
			var fs = para.FontSize > 1 ? para.FontSize : pt2dip(10.5);
			var used = (label.Length + page.Length) * fs * 0.55;
			var dotsN = Math.Max(4, (int)((contentW - used) / (fs * 0.32)));
			if (dotsN > 70) dotsN = 70;
			para.Inlines.Add(new Run(" " + new string('.', dotsN) + " ") {
				Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
				FontSize = fs * 0.85,
			});
			para.Inlines.Add(new Run(page));
		}
	}

	void addruntopara(Paragraph para, W.Run run, StyleRPr styleR = null) {
		foreach (var drawing in run.Elements<W.Drawing>()) {
			var img = trybuildimage(drawing);
			if (img != null)
				para.Inlines.Add(new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center });
		}
		var text = string.Concat(run.Elements<W.Text>().Select(t => t.Text));
		if (run.Elements<W.TabChar>().Any())
			para.Inlines.Add(new Run("\t"));
		if (!string.IsNullOrEmpty(text)) {
			var r = new Run(text);
			// 先继承段落样式字号/加粗，再被 run 属性覆盖
			if (para.FontSize > 0) r.FontSize = para.FontSize;
			if (para.FontWeight != FontWeights.Normal) r.FontWeight = para.FontWeight;
			if (para.FontStyle != FontStyles.Normal) r.FontStyle = para.FontStyle;
			if (para.FontFamily != null) r.FontFamily = para.FontFamily;
			applyrunprops(r, run.RunProperties, styleR);
			para.Inlines.Add(r);
		}
		foreach (var br in run.Elements<W.Break>()) {
			if (br.Type != null && br.Type.Value == W.BreakValues.Page) continue;
			para.Inlines.Add(new LineBreak());
		}
	}

	void applyrunprops(Run r, W.RunProperties rPr, StyleRPr styleR = null) {
		// 样式默认（run 未指定时）
		if (styleR != null) {
			if (styleR.FontSizePt != null && styleR.FontSizePt.Value > 0 && r.FontSize <= 0)
				r.FontSize = pt2dip(styleR.FontSizePt.Value);
			if (styleR.Bold == true && r.FontWeight == FontWeights.Normal)
				r.FontWeight = FontWeights.Bold;
			if (styleR.Italic == true && r.FontStyle == FontStyles.Normal)
				r.FontStyle = FontStyles.Italic;
			if (!string.IsNullOrEmpty(styleR.FontName) && r.FontFamily == null)
				r.FontFamily = new FontFamily(styleR.FontName + ", 宋体, SimSun, Microsoft YaHei UI");
		}
		if (rPr == null) {
			if (r.FontSize <= 0) r.FontSize = pt2dip(10.5);
			return;
		}
		if (rPr.Bold != null && (rPr.Bold.Val == null || rPr.Bold.Val.Value))
			r.FontWeight = FontWeights.Bold;
		if (rPr.Italic != null && (rPr.Italic.Val == null || rPr.Italic.Val.Value))
			r.FontStyle = FontStyles.Italic;
		if (rPr.Underline != null)
			r.TextDecorations = TextDecorations.Underline;
		var sz = rPr.FontSize?.Val?.Value;
		if (sz != null && double.TryParse(sz, out var hp) && hp > 0)
			r.FontSize = pt2dip(hp / 2.0);
		else if (r.FontSize <= 0)
			r.FontSize = pt2dip(10.5);
		var rFonts = rPr.RunFonts;
		if (rFonts != null) {
			var name = rFonts.EastAsia?.Value ?? rFonts.Ascii?.Value ?? rFonts.HighAnsi?.Value;
			if (!string.IsNullOrEmpty(name))
				r.FontFamily = new FontFamily(name + ", 宋体, SimSun, Microsoft YaHei UI");
		}
		var color = rPr.Color?.Val?.Value;
		if (!string.IsNullOrEmpty(color) && color != "auto" && color.Length == 6) {
			try {
				r.Foreground = new SolidColorBrush(Color.FromRgb(
					Convert.ToByte(color.Substring(0, 2), 16),
					Convert.ToByte(color.Substring(2, 2), 16),
					Convert.ToByte(color.Substring(4, 2), 16)));
			} catch { /* ignore */ }
		}
	}

	System.Windows.Controls.Image trybuildimage(W.Drawing drawing) {
		try {
			double wDip = 200, hDip = 150;
			var extent = drawing.Descendants<WP.Extent>().FirstOrDefault();
			if (extent?.Cx != null && extent.Cy != null) {
				wDip = extent.Cx.Value * EMU2DIP;
				hDip = extent.Cy.Value * EMU2DIP;
			}
			var maxW = Math.Max(40, pageW - padL - padR);
			if (wDip > maxW) {
				var s = maxW / wDip;
				wDip = maxW;
				hDip *= s;
			}
			var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
			var embed = blip?.Embed?.Value;
			if (string.IsNullOrEmpty(embed) || mainPart == null) return null;
			var part = mainPart.GetPartById(embed) as ImagePart;
			if (part == null) return null;

			BitmapImage bi;
			using (var s = part.GetStream()) {
				var ms = new MemoryStream();
				s.CopyTo(ms);
				ms.Position = 0;
				bi = new BitmapImage();
				bi.BeginInit();
				bi.StreamSource = ms;
				bi.CacheOption = BitmapCacheOption.OnLoad;
				bi.EndInit();
				bi.Freeze();
			}
			var img = new System.Windows.Controls.Image {
				Source = bi,
				Width = wDip,
				Height = hDip,
				Stretch = Stretch.Uniform,
				Margin = new Thickness(0, 4, 0, 4),
				SnapsToDevicePixels = true,
				Tag = bi,
				Cursor = Cursors.Hand,
				ToolTip = "点击放大查看 · 右键：预览 / 复制 / 另存为",
			};
						// 图片在 RichTextBox 里，鼠标事件由 pageScroll 的预览事件统一接管（见 onpagectximage / onpanorselectdown）
			img.ContextMenu = buildimgmenu(bi);
			return img;
		} catch (Exception ex) {
			DocViewLog.Warn($"Docx image: {ex.Message}");
			return null;
		}
	}

	static ContextMenu buildimgmenu(BitmapSource bi) {
		var cm = new ContextMenu();
		var mview = new MenuItem { Header = "预览图片" };
		mview.Click += (_, _) => ImageOverlay.Show(bi);
		var mcopy = new MenuItem { Header = "复制图片" };
		mcopy.Click += (_, _) => ImageOverlay.CopyImage(bi);
		var msave = new MenuItem { Header = "图片另存为…" };
		msave.Click += (_, _) => {
			try {
				var dlg = new SaveFileDialog {
					Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp",
					FileName = "image.png",
					Title = "图片另存为",
				};
				if (dlg.ShowDialog() != true) return;
				savebitmap(bi, dlg.FileName);
				DocViewLog.Info($"Docx save image {dlg.FileName}");
			} catch (Exception ex) {
				DocViewLog.Error("save image", ex);
				MessageBox.Show("保存失败: " + ex.Message, "ScreenKit");
			}
		};
		cm.Items.Add(mview);
		cm.Items.Add(mcopy);
		cm.Items.Add(msave);
		return cm;
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

	Table buildtable(W.Table t) {
		var table = new Table {
			CellSpacing = 0,
			BorderBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
			BorderThickness = new Thickness(0.75),
			Margin = new Thickness(0, 8, 0, 8),
		};
		table.Columns.Add(new TableColumn());
		var group = new TableRowGroup();
		table.RowGroups.Add(group);
		foreach (var row in t.Elements<W.TableRow>()) {
			var tr = new TableRow();
			foreach (var cell in row.Elements<W.TableCell>()) {
				var tc = new TableCell {
					BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
					BorderThickness = new Thickness(0.5),
					Padding = new Thickness(6, 4, 6, 4),
				};
				foreach (var cp in cell.Elements<W.Paragraph>())
					tc.Blocks.Add(buildpara(cp));
				if (tc.Blocks.Count == 0)
					tc.Blocks.Add(new Paragraph(new Run("")));
				tr.Cells.Add(tc);
			}
			while (table.Columns.Count < tr.Cells.Count)
				table.Columns.Add(new TableColumn());
			group.Rows.Add(tr);
		}
		return table;
	}

	static bool collectruns(FlowDocument doc, out List<(int Off, TextPointer Ptr, string Txt)> runs, out string full) {
		runs = new List<(int Off, TextPointer Ptr, string Txt)>();
		full = "";
		if (doc == null) return false;
		var sb = new StringBuilder();
		var nav = doc.ContentStart;
		while (nav != null && nav.CompareTo(doc.ContentEnd) < 0) {
			if (nav.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text) {
				var t = nav.GetTextInRun(LogicalDirection.Forward);
				if (!string.IsNullOrEmpty(t)) {
					runs.Add((sb.Length, nav, t));
					sb.Append(t);
					nav = nav.GetPositionAtOffset(t.Length, LogicalDirection.Forward)
						?? nav.GetNextContextPosition(LogicalDirection.Forward);
					continue;
				}
			}
			nav = nav.GetNextContextPosition(LogicalDirection.Forward);
		}
		full = sb.ToString();
		return runs.Count > 0;
	}

	/// <summary>
	/// 将字符偏移转为 TextPointer 区间 [start, end)。
	/// 终点用「仅计文本字符」前进，避免 GetPositionAtOffset 把行内元素符号算进去导致多高亮一字。
	/// </summary>
	static bool tryhitpointers(
		List<(int Off, TextPointer Ptr, string Txt)> runs,
		int start, int end,
		out TextPointer p0, out TextPointer p1) {
		p0 = null;
		p1 = null;
		if (runs == null || runs.Count == 0) return false;
		if (end < start) end = start;
		p0 = offsettopointer(runs, start);
		if (p0 == null) return false;
		p1 = advancetextchars(p0, end - start);
		if (p1 == null) return false;
		// 校正：TextRange.Text 去掉换行后若仍长于期望，收缩终点
		try {
			var expect = end - start;
			if (expect > 0) {
				var got = new TextRange(p0, p1).Text ?? "";
				var n = 0;
				for (var i = 0; i < got.Length; i++) {
					var ch = got[i];
					if (ch == '\r' || ch == '\n') continue;
					n++;
				}
				if (n > expect)
					p1 = advancetextchars(p0, expect) ?? p1;
			}
		} catch { /* ignore */ }
		return true;
	}

	/// <summary>从 start 起只按文本字符前进 nChars（跳过 ElementStart/End 等符号）。</summary>
	static TextPointer advancetextchars(TextPointer start, int nChars) {
		if (start == null) return null;
		if (nChars <= 0) return start;
		var nav = start;
		var left = nChars;
		while (nav != null && left > 0) {
			var ctx = nav.GetPointerContext(LogicalDirection.Forward);
			if (ctx == TextPointerContext.Text) {
				var t = nav.GetTextInRun(LogicalDirection.Forward);
				if (string.IsNullOrEmpty(t)) {
					nav = nav.GetNextContextPosition(LogicalDirection.Forward);
					continue;
				}
				if (t.Length <= left) {
					nav = nav.GetPositionAtOffset(t.Length, LogicalDirection.Forward)
						?? nav.GetNextContextPosition(LogicalDirection.Forward);
					left -= t.Length;
				} else {
					return nav.GetPositionAtOffset(left, LogicalDirection.Forward) ?? nav;
				}
			} else if (ctx == TextPointerContext.None) {
				break;
			} else {
				nav = nav.GetNextContextPosition(LogicalDirection.Forward);
			}
		}
		return nav;
	}

	static TextPointer offsettopointer(List<(int Off, TextPointer Ptr, string Txt)> runs, int offset) {
		if (runs == null || runs.Count == 0) return null;
		if (offset <= 0) return runs[0].Ptr;
		for (var i = 0; i < runs.Count; i++) {
			var r = runs[i];
			// 用 &lt; end：落在 run 边界时取下一 run 起点，避免 end 指针落在上一 run 末尾时
			// 与符号计数混用导致 TextRange 多吃一个字符。
			var runEnd = r.Off + r.Txt.Length;
			if (offset < runEnd || (offset == runEnd && i == runs.Count - 1)) {
				var local = offset - r.Off;
				if (local < 0) local = 0;
				if (local > r.Txt.Length) local = r.Txt.Length;
				return r.Ptr.GetPositionAtOffset(local, LogicalDirection.Forward) ?? r.Ptr;
			}
		}
		var last = runs[runs.Count - 1];
		return last.Ptr.GetPositionAtOffset(last.Txt.Length, LogicalDirection.Forward) ?? last.Ptr;
	}

	static double pt2dip(double pt) => pt * 96.0 / 72.0;
	static Thickness thicknonneg(double l, double t, double r, double b) =>
		new Thickness(l < 0 ? 0 : l, t < 0 ? 0 : t, r < 0 ? 0 : r, b < 0 ? 0 : b);
	static double clamp(double v, double lo, double hi) {
		if (v < lo) return lo;
		if (v > hi) return hi;
		return v;
	}
}
