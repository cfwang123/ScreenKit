using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MediaColor = System.Windows.Media.Color;
using A = DocumentFormat.OpenXml.Drawing;
using XFont = DocumentFormat.OpenXml.Spreadsheet.Font;
using XFill = DocumentFormat.OpenXml.Spreadsheet.Fill;
using XBorder = DocumentFormat.OpenXml.Spreadsheet.Border;

namespace ScreenKit.DocView;

/// <summary>
/// XLSX 阅读 + 简单编辑：虚拟表格，支持字体/加粗/颜色/边框；编辑模式可改字/合并/对齐并保存。
/// </summary>
sealed class XlsxViewer : IDocViewer {
	const double MIN_ZOOM = 0.7;
	const double MAX_ZOOM = 2.0;
	const int MAX_ROWS = 10000;
	const int MAX_COLS = 200;

	readonly Grid root;
	readonly TabControl sheets;
	readonly TextBlock empty;
	readonly List<VirtualSheetGrid> grids = new();
	readonly List<string> sheetNames = new();
	double zoom = 1.0;
	int sheetCount;
	bool editMode;
	bool legacyBinary;
	bool dirty;

	public FrameworkElement View => root;
	public string FilePath { get; private set; }
	public string Title { get; private set; }
	public DocKind Kind => DocKind.Xlsx;
	/// <summary>.xls 等旧格式只读预览，不可编辑保存。</summary>
	public bool CanEdit => !legacyBinary;
	public bool EditMode {
		get => editMode;
		set => seteditmode(value);
	}
	public bool IsDirty => dirty;
	public double Zoom {
		get {
			var g = currentgrid();
			return g != null ? g.Zoom : zoom;
		}
	}
	public string StatusText {
		get {
			var name = (sheets.SelectedItem as TabItem)?.Header?.ToString() ?? "-";
			var g = currentgrid();
			var sel = "";
			if (g != null && g.HasSelection) {
				g.GetSelection(out var r0, out var c0, out var r1, out var c1);
				if (r0 == 0 && r1 >= g.Rows - 1 && c0 == c1)
					sel = $"  ·  已选列 {c0 + 1}";
				else if (r0 == 0 && r1 >= g.Rows - 1)
					sel = $"  ·  已选列 {c0 + 1}-{c1 + 1}";
				else if (c0 == 0 && c1 >= g.Cols - 1 && r0 == r1)
					sel = $"  ·  已选行 {r0 + 1}";
				else if (c0 == 0 && c1 >= g.Cols - 1)
					sel = $"  ·  已选行 {r0 + 1}-{r1 + 1}";
				else
					sel = "  ·  已选单元格";
			}
			var ed = editMode ? "  ·  编辑中" : "";
			var d = dirty ? " *" : "";
			var fmt = legacyBinary ? "XLS" : "XLSX";
			return $"{fmt}  {name}{d}  ·  {sheetCount} 表  ·  {(int)(zoom * 100)}%{ed}{sel}";
		}
	}
	public int PageCount => Math.Max(1, sheetCount);
	public int CurrentPage => sheets.SelectedIndex < 0 ? 0 : sheets.SelectedIndex + 1;

	public event Action StatusChanged;
	public event Action EditModeChanged;
	public event Action DirtyChanged;
	public event Action SelectionChanged;

	public XlsxViewer() {
		sheets = new TabControl {
			Background = Brushes.White,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(0),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			VerticalContentAlignment = VerticalAlignment.Stretch,
		};
		sheets.SelectionChanged += (_, _) => {
			// 切换表时同步编辑模式
			foreach (var g in grids)
				if (g != null) g.EditMode = editMode;
			StatusChanged?.Invoke();
			try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
		};

		empty = new TextBlock {
			Text = "无可显示工作表",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(MediaColor.FromRgb(0x6B, 0x72, 0x80)),
			Visibility = Visibility.Collapsed,
		};

		root = new Grid {
			Background = Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
		};
		root.Children.Add(sheets);
		root.Children.Add(empty);
	}

	/// <summary>后台线程可调用：OpenXml 稠密解析，不碰 WPF。</summary>
	public static XlsxLoadData Prepare(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("文件不存在", path);
		path = Path.GetFullPath(path);
		var ext = Path.GetExtension(path).ToLowerInvariant();
		if (ext == ".xls")
			return LegacyOfficeLoader.PrepareXls(path);
		var data = new XlsxLoadData {
			Path = path,
			Title = Path.GetFileName(path),
			Sheets = new List<XlsxSheetData>(),
		};
		using var fs = DocFileIo.OpenReadShared(path);
		using var doc = SpreadsheetDocument.Open(fs, false);
		var wbPart = doc.WorkbookPart;
		if (wbPart?.Workbook?.Sheets == null)
			return data;

		var sstArr = loadsst(wbPart.SharedStringTablePart?.SharedStringTable);
		var styles = loadstyles(wbPart);
		var n = 0;
		foreach (var sheet in wbPart.Workbook.Sheets.Elements<Sheet>()) {
			var id = sheet.Id?.Value;
			if (string.IsNullOrEmpty(id)) continue;
			var part = wbPart.GetPartById(id) as WorksheetPart;
			if (part == null) continue;
			var model = readsheet(part, sstArr, styles);
			data.Sheets.Add(new XlsxSheetData {
				Name = sheet.Name?.Value ?? $"Sheet{n + 1}",
				Model = model,
			});
			n++;
		}
		DocViewLog.Info($"Xlsx Prepare sheets={data.Sheets.Count} path={path}");
		return data;
	}

	public void Load(string path) {
		ApplyPrepared(Prepare(path));
	}

	/// <summary>UI 线程：把后台解析结果装进控件。</summary>
	public void ApplyPrepared(XlsxLoadData data) {
		if (data == null) throw new ArgumentNullException(nameof(data));
		FilePath = data.Path;
		Title = data.Title ?? Path.GetFileName(data.Path);
		sheets.Items.Clear();
		grids.Clear();
		sheetNames.Clear();
		sheetCount = 0;
		editMode = false;
		legacyBinary = data.LegacyBinary;
		setdirty(false);

		if (data.Sheets == null || data.Sheets.Count == 0) {
			empty.Visibility = Visibility.Visible;
			sheets.Visibility = Visibility.Collapsed;
			StatusChanged?.Invoke();
			return;
		}

		empty.Visibility = Visibility.Collapsed;
		sheets.Visibility = Visibility.Visible;
		var pump = 0;
		foreach (var s in data.Sheets) {
			var grid = new VirtualSheetGrid {
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
			};
			grid.SetData(s.Model, zoom);
			grid.ScrollProgressChanged += () => StatusChanged?.Invoke();
			grid.SelectionChanged += () => {
				try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
				StatusChanged?.Invoke();
			};
			grid.ModelEdited += () => {
				setdirty(true);
				StatusChanged?.Invoke();
			};
			grid.ZoomChanged += () => {
				try {
					zoom = clamp(grid.Zoom, MIN_ZOOM, MAX_ZOOM);
					foreach (var og in grids) {
						if (og == null || ReferenceEquals(og, grid)) continue;
						if (Math.Abs(og.Zoom - zoom) > 0.001) og.SetZoom(zoom);
					}
					StatusChanged?.Invoke();
				} catch { /* ignore */ }
			};
			var sname = s.Name ?? $"Sheet{sheetCount + 1}";
			var tab = new TabItem {
				Header = sname,
				Content = grid,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Stretch,
			};
			sheets.Items.Add(tab);
			grids.Add(grid);
			sheetNames.Add(sname);
			sheetCount++;
			UiPump.Every(ref pump, 1);
		}
		sheets.SelectedIndex = 0;
		StatusChanged?.Invoke();
	}

	public void SetZoom(double z) {
		zoom = clamp(z, MIN_ZOOM, MAX_ZOOM);
		// 工具栏缩放：同步全部表；Ctrl+滚轮在 VirtualSheetGrid 内按鼠标锚点缩放
		foreach (var g in grids)
			g.SetZoom(zoom);
		StatusChanged?.Invoke();
	}

	public void ZoomBy(double factor) => SetZoom(Zoom * factor);
	public void ZoomIn() => SetZoom(Zoom * 1.15);
	public void ZoomOut() => SetZoom(Zoom / 1.15);
	public void ZoomFitWidth() => SetZoom(1.0);
	public void ZoomFitPage() => SetZoom(1.0);
	public void RotateBy(int deltaQuarterTurns) { /* XLSX 不旋转 */ }

	public void GoPrevPage() {
		if (sheets.SelectedIndex > 0) sheets.SelectedIndex--;
	}
	public void GoNextPage() {
		if (sheets.SelectedIndex < sheets.Items.Count - 1) sheets.SelectedIndex++;
	}
	public void GoToPage(int page1Based) {
		if (sheets.Items.Count == 0) return;
		var i = page1Based - 1;
		if (i < 0) i = 0;
		if (i >= sheets.Items.Count) i = sheets.Items.Count - 1;
		sheets.SelectedIndex = i;
	}

	public bool HasOutline => false;
	public bool SidePanelVisible => false;
	public void SetSidePanelVisible(bool show) { /* 无侧栏 */ }

	public void CaptureViewState(out double h, out double v, out double z, out int sheetOrPage) {
		h = 0; v = 0;
		z = zoom;
		sheetOrPage = sheets.SelectedIndex >= 0 ? sheets.SelectedIndex : 0;
		var g = currentgrid();
		if (g != null)
			g.GetScrollOffset(out h, out v);
	}

	public void RestoreViewState(double h, double v, double z, int sheetOrPage) {
		if (z > 0.05 && Math.Abs(z - zoom) > 0.001)
			SetZoom(z);
		if (sheetOrPage >= 0 && sheetOrPage < sheets.Items.Count)
			sheets.SelectedIndex = sheetOrPage;
		try {
			root.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => {
				try {
					var g = currentgrid();
					g?.SetScrollOffset(h, v);
				} catch { /* ignore */ }
			}));
		} catch { /* ignore */ }
	}

	public bool TryCopySelection() {
		var g = currentgrid();
		return g != null && g.TryCopySelection();
	}

	public FindResult Find(string text, bool forward, bool ignoreCase, bool restart = false, bool fromView = false) {
		var g = currentgrid();
		if (g == null) return FindResult.Miss();
		return g.Find(text, forward, ignoreCase, restart, fromView);
	}

	public void ClearFind() {
		foreach (var g in grids)
			g?.ClearFind();
	}

	public void Dispose() {
		sheets.Items.Clear();
		grids.Clear();
		sheetNames.Clear();
	}

	// ---------- 编辑模式 ----------
	void seteditmode(bool on) {
		if (legacyBinary && on) return;
		if (editMode == on) return;
		editMode = on;
		foreach (var g in grids)
			if (g != null) g.EditMode = on;
		try { EditModeChanged?.Invoke(); } catch { /* ignore */ }
		StatusChanged?.Invoke();
	}

	void setdirty(bool d) {
		if (dirty == d) return;
		dirty = d;
		try { DirtyChanged?.Invoke(); } catch { /* ignore */ }
	}

	public SheetCell PeekSelectionStyle() => currentgrid()?.PeekSelectionStyle();

	public bool HasSelection => currentgrid()?.HasSelection == true;

	public bool MergeCells() => currentgrid()?.MergeSelection() == true;

	public bool UnmergeCells() => currentgrid()?.UnmergeSelection() == true;

	public bool SetAlign(TextAlignment align) =>
		currentgrid()?.ApplyToSelection((cell, _, _) => cell.Align = align) == true;

	public bool SetVAlign(int valign) =>
		currentgrid()?.ApplyToSelection((cell, _, _) => {
			if (valign < 0) valign = 0;
			if (valign > 2) valign = 2;
			cell.VAlign = valign;
		}) == true;

	public bool SetBold(bool bold) =>
		currentgrid()?.ApplyToSelection((cell, _, _) => cell.Bold = bold) == true;

	public bool SetItalic(bool italic) =>
		currentgrid()?.ApplyToSelection((cell, _, _) => cell.Italic = italic) == true;

	public bool ToggleBold() {
		var st = PeekSelectionStyle();
		return SetBold(!(st?.Bold ?? false));
	}

	public bool ToggleItalic() {
		var st = PeekSelectionStyle();
		return SetItalic(!(st?.Italic ?? false));
	}

	public bool SetFontName(string name) {
		if (string.IsNullOrWhiteSpace(name)) return false;
		return currentgrid()?.ApplyToSelection((cell, _, _) => cell.FontName = name.Trim()) == true;
	}

	public bool SetFontSizePt(double pt) {
		if (pt < 6) pt = 6;
		if (pt > 72) pt = 72;
		return currentgrid()?.ApplyToSelection((cell, _, _) => cell.FontSizePt = pt) == true;
	}

	public bool SetForeColor(MediaColor? color) =>
		currentgrid()?.ApplyToSelection((cell, _, _) => cell.ForeColor = color) == true;

	public bool SetBackColor(MediaColor? color) =>
		currentgrid()?.ApplyToSelection((cell, _, _) => cell.BackColor = color) == true;

	public bool SetWrap(bool wrap) => currentgrid()?.SetWrapSelection(wrap) == true;

	public bool ToggleWrap() => currentgrid()?.ToggleWrapSelection() == true;

	/// <summary>选中行整行自动换行。</summary>
	public bool SetWrapRows(bool wrap) => currentgrid()?.SetWrapRows(wrap) == true;

	/// <summary>选中列整列自动换行。</summary>
	public bool SetWrapCols(bool wrap) => currentgrid()?.SetWrapCols(wrap) == true;

	/// <summary>是否正在单元格内联编辑。</summary>
	public bool IsEditingCell => currentgrid()?.IsEditingCell == true;

	/// <summary>方向键移动选区。</summary>
	public void MoveSelectionBy(int dr, int dc) {
		var g = currentgrid();
		if (g == null) return;
		g.MoveSelectionBy(dr, dc);
		StatusChanged?.Invoke();
	}

	/// <summary>Shift+方向键块选。</summary>
	public void ExtendSelectionBy(int dr, int dc) {
		var g = currentgrid();
		if (g == null) return;
		g.ExtendSelectionBy(dr, dc);
		StatusChanged?.Invoke();
	}

	/// <summary>保存到原路径（或指定路径）。简单重写工作簿（值+样式+合并）。</summary>
	public void Save(string path = null) {
		if (legacyBinary)
			throw new InvalidOperationException("XLS 格式仅支持预览，请另存为 XLSX 后编辑");
		path = string.IsNullOrWhiteSpace(path) ? FilePath : Path.GetFullPath(path);
		if (string.IsNullOrWhiteSpace(path))
			throw new InvalidOperationException("无保存路径");
		var list = new List<(string Name, SheetModel Model)>();
		for (var i = 0; i < grids.Count; i++) {
			var g = grids[i];
			if (g?.Model == null) continue;
			var name = i < sheetNames.Count ? sheetNames[i] : $"Sheet{i + 1}";
			list.Add((name, g.Model));
		}
		if (list.Count == 0)
			throw new InvalidOperationException("没有可保存的工作表");
		var dir = Path.GetDirectoryName(path);
		var tmp = Path.Combine(string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir,
			Path.GetFileNameWithoutExtension(path) + ".~tmp.xlsx");
		try {
			if (File.Exists(tmp)) File.Delete(tmp);
			writexlsx(tmp, list);
			// 替换目标：先删再移，避免被占用
			if (File.Exists(path)) {
				var bak = path + ".bak";
				try { if (File.Exists(bak)) File.Delete(bak); } catch { /* ignore */ }
				try { File.Replace(tmp, path, bak); try { File.Delete(bak); } catch { /* ignore */ } }
				catch {
					File.Copy(tmp, path, true);
					try { File.Delete(tmp); } catch { /* ignore */ }
				}
			} else {
				File.Move(tmp, path);
			}
			FilePath = path;
			Title = Path.GetFileName(path);
			setdirty(false);
			StatusChanged?.Invoke();
			DocViewLog.Info($"Xlsx Save ok path={path} sheets={list.Count}");
		} catch (Exception ex) {
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
			DocViewLog.Error("Xlsx Save", ex);
			throw;
		}
	}

	VirtualSheetGrid currentgrid() {
		if (sheets.SelectedItem is TabItem ti && ti.Content is VirtualSheetGrid g)
			return g;
		var i = sheets.SelectedIndex;
		if (i >= 0 && i < grids.Count) return grids[i];
		return null;
	}

	// ---------- 写出 XLSX ----------
	static void writexlsx(string path, List<(string Name, SheetModel Model)> sheetsData) {
		using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
		var wbPart = doc.AddWorkbookPart();
		wbPart.Workbook = new Workbook();
		var stylesPart = wbPart.AddNewPart<WorkbookStylesPart>();
		var styleMap = buildstylesheet(stylesPart, sheetsData);

		var sstPart = wbPart.AddNewPart<SharedStringTablePart>();
		var sst = new SharedStringTable();
		var sstIndex = new Dictionary<string, int>(StringComparer.Ordinal);
		int addsst(string text) {
			text ??= "";
			if (sstIndex.TryGetValue(text, out var ix)) return ix;
			ix = sstIndex.Count;
			sstIndex[text] = ix;
			sst.AppendChild(new SharedStringItem(new Text(text)));
			return ix;
		}

		var sheetsEl = new Sheets();
		uint sheetId = 1;
		foreach (var (name, model) in sheetsData) {
			var wsp = wbPart.AddNewPart<WorksheetPart>();
			wsp.Worksheet = buildworksheet(model, styleMap, addsst);
			var relId = wbPart.GetIdOfPart(wsp);
			var safeName = sanitizesheetname(name, (int)sheetId);
			sheetsEl.Append(new Sheet {
				Name = safeName,
				SheetId = sheetId,
				Id = relId,
			});
			sheetId++;
		}
		sst.Count = (uint)sstIndex.Count;
		sst.UniqueCount = (uint)sstIndex.Count;
		sstPart.SharedStringTable = sst;
		wbPart.Workbook.Append(sheetsEl);
		wbPart.Workbook.Save();
	}

	static string sanitizesheetname(string name, int fallbackId) {
		if (string.IsNullOrWhiteSpace(name)) name = $"Sheet{fallbackId}";
		var bad = new[] { ':', '\\', '/', '?', '*', '[', ']' };
		foreach (var ch in bad)
			name = name.Replace(ch, '_');
		if (name.Length > 31) name = name.Substring(0, 31);
		return name;
	}

	sealed class StyleKey : IEquatable<StyleKey> {
		public string FontName;
		public double FontSizePt;
		public bool Bold, Italic;
		public int? ForeArgb, BackArgb;
		public int Align; // 0 L 1 C 2 R 3 J
		public int VAlign;
		public bool Wrap;

		public bool Equals(StyleKey o) {
			if (o == null) return false;
			return FontName == o.FontName && FontSizePt == o.FontSizePt
				&& Bold == o.Bold && Italic == o.Italic
				&& ForeArgb == o.ForeArgb && BackArgb == o.BackArgb
				&& Align == o.Align && VAlign == o.VAlign && Wrap == o.Wrap;
		}
		public override bool Equals(object obj) => Equals(obj as StyleKey);
		public override int GetHashCode() {
			unchecked {
				var h = (FontName ?? "").GetHashCode();
				h = h * 31 + FontSizePt.GetHashCode();
				h = h * 31 + (Bold ? 1 : 0) + (Italic ? 2 : 0);
				h = h * 31 + (ForeArgb ?? 0) + (BackArgb ?? 0) * 397;
				h = h * 31 + Align + VAlign * 8 + (Wrap ? 64 : 0);
				return h;
			}
		}
	}

	static StyleKey stylekeyof(SheetCell c) {
		int align = 0;
		if (c.Align == TextAlignment.Center) align = 1;
		else if (c.Align == TextAlignment.Right) align = 2;
		else if (c.Align == TextAlignment.Justify) align = 3;
		return new StyleKey {
			FontName = string.IsNullOrWhiteSpace(c.FontName) ? "Calibri" : c.FontName,
			FontSizePt = c.FontSizePt > 1 ? c.FontSizePt : 11,
			Bold = c.Bold,
			Italic = c.Italic,
			ForeArgb = c.ForeColor.HasValue ? argb(c.ForeColor.Value) : (int?)null,
			BackArgb = c.BackColor.HasValue ? argb(c.BackColor.Value) : (int?)null,
			Align = align,
			VAlign = c.VAlign,
			Wrap = c.WrapText,
		};
	}

	static int argb(MediaColor c) => (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;

	static string hexrgb(int argbVal) {
		var r = (argbVal >> 16) & 0xFF;
		var g = (argbVal >> 8) & 0xFF;
		var b = argbVal & 0xFF;
		return $"{r:X2}{g:X2}{b:X2}";
	}

	static Dictionary<StyleKey, uint> buildstylesheet(WorkbookStylesPart stylesPart, List<(string Name, SheetModel Model)> sheetsData) {
		var map = new Dictionary<StyleKey, uint>();
		var keys = new List<StyleKey>();
		void consider(SheetCell cell) {
			if (cell == null || ReferenceEquals(cell, SheetCell.SharedEmpty)) return;
			if (cell.HiddenByMerge) return;
			var k = stylekeyof(cell);
			if (map.ContainsKey(k)) return;
			map[k] = (uint)keys.Count; // temp index into cellXfs after defaults
			keys.Add(k);
		}
		foreach (var (_, model) in sheetsData) {
			if (model?.Cells == null) continue;
			foreach (var row in model.Cells) {
				if (row == null) continue;
				foreach (var cell in row)
					consider(cell);
			}
		}

		var fonts = new DocumentFormat.OpenXml.Spreadsheet.Fonts();
		fonts.Append(new XFont( // 0 default
			new FontSize { Val = 11 },
			new DocumentFormat.OpenXml.Spreadsheet.Color { Theme = 1 },
			new FontName { Val = "Calibri" },
			new FontFamilyNumbering { Val = 2 }));
		var fills = new Fills();
		fills.Append(new XFill(new PatternFill { PatternType = PatternValues.None })); // 0
		fills.Append(new XFill(new PatternFill { PatternType = PatternValues.Gray125 })); // 1 required
		var borders = new Borders();
		borders.Append(new XBorder( // 0 default
			new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()));

		var cellXfs = new CellFormats();
		// xf 0 default
		cellXfs.Append(new CellFormat { FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 });

		var fontIndex = new Dictionary<string, uint>(StringComparer.Ordinal);
		fontIndex["Calibri|11|n|n|"] = 0;
		var fillIndex = new Dictionary<int, uint>(); // back argb -> fill id

		uint ensurefont(StyleKey k) {
			var fk = $"{k.FontName}|{k.FontSizePt:0.##}|{(k.Bold ? "B" : "n")}|{(k.Italic ? "I" : "n")}|{k.ForeArgb?.ToString() ?? ""}";
			if (fontIndex.TryGetValue(fk, out var id)) return id;
			id = (uint)fonts.ChildElements.Count;
			var f = new XFont();
			f.Append(new FontSize { Val = k.FontSizePt });
			if (k.ForeArgb.HasValue)
				f.Append(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = hexrgb(k.ForeArgb.Value) });
			else
				f.Append(new DocumentFormat.OpenXml.Spreadsheet.Color { Theme = 1 });
			f.Append(new FontName { Val = k.FontName });
			if (k.Bold) f.Append(new Bold());
			if (k.Italic) f.Append(new Italic());
			fonts.Append(f);
			fontIndex[fk] = id;
			return id;
		}

		uint ensurefill(StyleKey k) {
			if (!k.BackArgb.HasValue) return 0;
			if (fillIndex.TryGetValue(k.BackArgb.Value, out var id)) return id;
			id = (uint)fills.ChildElements.Count;
			var pf = new PatternFill { PatternType = PatternValues.Solid };
			pf.Append(new ForegroundColor { Rgb = hexrgb(k.BackArgb.Value) });
			pf.Append(new BackgroundColor { Indexed = 64 });
			fills.Append(new XFill(pf));
			fillIndex[k.BackArgb.Value] = id;
			return id;
		}

		// remap: cellXfs index = 1 + i for keys[i]
		var finalMap = new Dictionary<StyleKey, uint>();
		for (var i = 0; i < keys.Count; i++) {
			var k = keys[i];
			var fi = ensurefont(k);
			var filli = ensurefill(k);
			var xf = new CellFormat {
				FontId = fi,
				FillId = filli,
				BorderId = 0,
				FormatId = 0,
				ApplyFont = true,
				ApplyFill = filli > 0,
				ApplyAlignment = true,
			};
			var al = new Alignment { WrapText = k.Wrap };
			if (k.Align == 1) al.Horizontal = HorizontalAlignmentValues.Center;
			else if (k.Align == 2) al.Horizontal = HorizontalAlignmentValues.Right;
			else if (k.Align == 3) al.Horizontal = HorizontalAlignmentValues.Justify;
			else al.Horizontal = HorizontalAlignmentValues.Left;
			if (k.VAlign == 0) al.Vertical = VerticalAlignmentValues.Top;
			else if (k.VAlign == 2) al.Vertical = VerticalAlignmentValues.Bottom;
			else al.Vertical = VerticalAlignmentValues.Center;
			xf.Append(al);
			cellXfs.Append(xf);
			finalMap[k] = (uint)(i + 1);
		}

		fonts.Count = (uint)fonts.ChildElements.Count;
		fills.Count = (uint)fills.ChildElements.Count;
		borders.Count = (uint)borders.ChildElements.Count;
		cellXfs.Count = (uint)cellXfs.ChildElements.Count;

		var ss = new Stylesheet();
		ss.Append(fonts);
		ss.Append(fills);
		ss.Append(borders);
		ss.Append(new CellStyleFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0 }));
		ss.Append(cellXfs);
		stylesPart.Stylesheet = ss;
		stylesPart.Stylesheet.Save();
		return finalMap;
	}

	static Worksheet buildworksheet(SheetModel model, Dictionary<StyleKey, uint> styleMap, Func<string, int> addsst) {
		var sheetData = new SheetData();
		var nRow = model?.Rows ?? 0;
		var nCol = model?.Cols ?? 0;
		for (var r = 0; r < nRow; r++) {
			var row = new Row { RowIndex = (uint)(r + 1) };
			if (model.RowHeights != null && r < model.RowHeights.Length && model.RowHeights[r] > 0) {
				// 行高：Excel 点 ≈ DIP * 72/96
				row.Height = model.RowHeights[r] * 72.0 / 96.0;
				row.CustomHeight = true;
			}
			var any = false;
			for (var c = 0; c < nCol; c++) {
				var sc = model.CellAt(r, c);
				if (sc.HiddenByMerge) continue;
				var text = sc.Text ?? "";
				var hasStyle = !isdefaultstyle(sc);
				if (text.Length == 0 && !hasStyle && sc.RowSpan <= 1 && sc.ColSpan <= 1)
					continue;
				any = true;
				var cell = new Cell { CellReference = colname(c) + (r + 1) };
				if (hasStyle || text.Length > 0) {
					var k = stylekeyof(sc);
					if (styleMap.TryGetValue(k, out var sid))
						cell.StyleIndex = sid;
				}
				if (text.Length > 0) {
					var ix = addsst(text);
					cell.DataType = CellValues.SharedString;
					cell.CellValue = new CellValue(ix.ToString(CultureInfo.InvariantCulture));
				}
				row.Append(cell);
			}
			if (any || (model.RowHeights != null && r < model.RowHeights.Length))
				sheetData.Append(row);
		}

		var ws = new Worksheet();
		// 列宽
		if (nCol > 0 && model.ColWidths != null) {
			var cols = new Columns();
			for (var c = 0; c < nCol; c++) {
				var dip = model.ColWidths[c] > 1 ? model.ColWidths[c] : 64;
				// Excel 列宽近似：字符宽，粗略 dip/7
				var w = Math.Max(3, dip / 7.0);
				cols.Append(new Column {
					Min = (uint)(c + 1),
					Max = (uint)(c + 1),
					Width = w,
					CustomWidth = true,
				});
			}
			ws.Append(cols);
		}
		ws.Append(sheetData);
		// 合并
		if (model?.Merges != null && model.Merges.Count > 0) {
			var mc = new MergeCells();
			foreach (var m in model.Merges) {
				if (m == null || (m.R0 == m.R1 && m.C0 == m.C1)) continue;
				var refs = colname(m.C0) + (m.R0 + 1) + ":" + colname(m.C1) + (m.R1 + 1);
				mc.Append(new MergeCell { Reference = refs });
			}
			if (mc.ChildElements.Count > 0) {
				mc.Count = (uint)mc.ChildElements.Count;
				ws.Append(mc);
			}
		}
		return ws;
	}

	static string colname(int c0) {
		// 0 -> A
		var n = c0 + 1;
		var s = "";
		while (n > 0) {
			n--;
			s = (char)('A' + n % 26) + s;
			n /= 26;
		}
		return s;
	}

	// ---------- 读表 + 样式 ----------
	sealed class StyleBook {
		public List<SheetCell> Formats = new(); // 按 CellFormat 下标
	}

	static StyleBook loadstyles(WorkbookPart wb) {
		var book = new StyleBook();
		var ss = wb.WorkbookStylesPart?.Stylesheet;
		if (ss == null) {
			book.Formats.Add(SheetCell.Empty());
			return book;
		}

		var fonts = ss.Fonts?.Elements<XFont>().ToList() ?? new List<XFont>();
		var fills = ss.Fills?.Elements<XFill>().ToList() ?? new List<XFill>();
		var borders = ss.Borders?.Elements<XBorder>().ToList() ?? new List<XBorder>();
		var xfs = ss.CellFormats?.Elements<CellFormat>().ToList() ?? new List<CellFormat>();

		// 主题色（简化映射，常见 Office 主题）
		var theme = loadthemecolors(wb);

		if (xfs.Count == 0) {
			book.Formats.Add(SheetCell.Empty());
			return book;
		}

		foreach (var xf in xfs) {
			var sc = SheetCell.Empty();
			// Font
			var fi = (int)(xf.FontId?.Value ?? 0);
			if (fi >= 0 && fi < fonts.Count) {
				var f = fonts[fi];
				sc.Bold = f.Bold != null && (f.Bold.Val == null || f.Bold.Val.Value);
				sc.Italic = f.Italic != null && (f.Italic.Val == null || f.Italic.Val.Value);
				if (f.FontSize?.Val != null)
					sc.FontSizePt = f.FontSize.Val.Value;
				if (f.FontName?.Val != null)
					sc.FontName = f.FontName.Val.Value;
				else if (f.FontName == null) {
					var fn = f.GetFirstChild<FontName>();
					if (fn?.Val != null) sc.FontName = fn.Val.Value;
				}
				sc.ForeColor = resolvecolor(f.Color, theme, defaultDark: true);
			}
			// Fill（Solid 时颜色在 ForegroundColor）
			var filli = (int)(xf.FillId?.Value ?? 0);
			if (filli >= 0 && filli < fills.Count) {
				var fill = fills[filli];
				var pf = fill.PatternFill;
				if (pf != null) {
					var pt = pf.PatternType?.Value;
					// None 视为无底；Gray125 是默认空填充
					if (pt != null && pt != PatternValues.None && pt != PatternValues.Gray125) {
						sc.BackColor = resolvecolor(pf.ForegroundColor, theme, defaultDark: false)
							?? resolvecolor(pf.BackgroundColor, theme, defaultDark: false);
					}
				}
			}
			// Border
			var bi = (int)(xf.BorderId?.Value ?? 0);
			if (bi >= 0 && bi < borders.Count) {
				var b = borders[bi];
				applyborder(b.LeftBorder, theme, ref sc.BorderLeft, ref sc.BorderLeftW);
				applyborder(b.RightBorder, theme, ref sc.BorderRight, ref sc.BorderRightW);
				applyborder(b.TopBorder, theme, ref sc.BorderTop, ref sc.BorderTopW);
				applyborder(b.BottomBorder, theme, ref sc.BorderBottom, ref sc.BorderBottomW);
			}
			// Align + Wrap
			var al = xf.Alignment;
			if (al != null) {
				if (al.Horizontal != null) {
					var h = al.Horizontal.Value;
					if (h == HorizontalAlignmentValues.Center || h == HorizontalAlignmentValues.CenterContinuous)
						sc.Align = TextAlignment.Center;
					else if (h == HorizontalAlignmentValues.Right)
						sc.Align = TextAlignment.Right;
					else if (h == HorizontalAlignmentValues.Justify || h == HorizontalAlignmentValues.Distributed)
						sc.Align = TextAlignment.Justify;
					else
						sc.Align = TextAlignment.Left;
				}
				if (al.Vertical != null) {
					var v = al.Vertical.Value;
					if (v == VerticalAlignmentValues.Top) sc.VAlign = 0;
					else if (v == VerticalAlignmentValues.Bottom) sc.VAlign = 2;
					else sc.VAlign = 1; // Center / Justify / Distributed
				}
				if (al.WrapText != null && al.WrapText.HasValue)
					sc.WrapText = al.WrapText.Value;
				else if (al.WrapText != null)
					sc.WrapText = true; // 元素存在且无 val 视为 true
			}
			book.Formats.Add(sc);
		}
		return book;
	}

	static void applyborder(BorderPropertiesType side, MediaColor[] theme,
		ref MediaColor? color, ref double width) {
		if (side == null) return;
		if (side.Style == null || !side.Style.HasValue) return;
		var st = side.Style.Value;
		if (st == BorderStyleValues.None) return;
		width = borderwidth(st);
		color = resolvecolor(side.Color, theme, defaultDark: true) ?? MediaColor.FromRgb(0, 0, 0);
	}

	static double borderwidth(BorderStyleValues st) {
		if (st == BorderStyleValues.Thick) return 2.2;
		if (st == BorderStyleValues.Medium || st == BorderStyleValues.MediumDashed
			|| st == BorderStyleValues.MediumDashDot || st == BorderStyleValues.MediumDashDotDot)
			return 1.6;
		if (st == BorderStyleValues.Hair || st == BorderStyleValues.Dotted)
			return 0.6;
		return 1.0;
	}

	static MediaColor[] loadthemecolors(WorkbookPart wb) {
		// SpreadsheetML color@theme 索引（与 DrawingML 的 dk1/lt1 顺序不同）：
		// 0=lt1 1=dk1 2=lt2 3=dk2 4..9=accent1..6 10=hlink 11=folHlink
		// Excel 默认正文色常为 theme=1（dk1/黑），若按 Drawing 序会误成白色。
		var theme = new MediaColor[12];
		theme[0] = MediaColor.FromRgb(0xFF, 0xFF, 0xFF); // lt1
		theme[1] = MediaColor.FromRgb(0x00, 0x00, 0x00); // dk1
		theme[2] = MediaColor.FromRgb(0xEE, 0xEC, 0xE1); // lt2
		theme[3] = MediaColor.FromRgb(0x1F, 0x49, 0x7D); // dk2
		theme[4] = MediaColor.FromRgb(0x4F, 0x81, 0xBD);
		theme[5] = MediaColor.FromRgb(0xC0, 0x50, 0x4D);
		theme[6] = MediaColor.FromRgb(0x9B, 0xBB, 0x59);
		theme[7] = MediaColor.FromRgb(0x80, 0x64, 0xA2);
		theme[8] = MediaColor.FromRgb(0x4B, 0xAC, 0xC6);
		theme[9] = MediaColor.FromRgb(0xF7, 0x96, 0x46);
		theme[10] = MediaColor.FromRgb(0x00, 0x00, 0xFF);
		theme[11] = MediaColor.FromRgb(0x80, 0x00, 0x80);
		try {
			var scheme = wb.ThemePart?.Theme?.ThemeElements?.ColorScheme;
			if (scheme == null) return theme;
			foreach (var el in scheme.ChildElements) {
				var name = el.LocalName;
				var idx = name switch {
					"lt1" => 0,
					"dk1" => 1,
					"lt2" => 2,
					"dk2" => 3,
					"accent1" => 4,
					"accent2" => 5,
					"accent3" => 6,
					"accent4" => 7,
					"accent5" => 8,
					"accent6" => 9,
					"hlink" => 10,
					"folHlink" => 11,
					_ => -1,
				};
				if (idx < 0) continue;
				var srgb = el.Descendants<A.RgbColorModelHex>().FirstOrDefault()?.Val?.Value;
				if (string.IsNullOrEmpty(srgb))
					srgb = el.Descendants<A.SystemColor>().FirstOrDefault()?.LastColor?.Value;
				var c = parsergb(srgb);
				if (c.HasValue) theme[idx] = c.Value;
			}
		} catch { /* 用默认主题 */ }
		return theme;
	}

	/// <summary>解析 ColorType（Rgb / Theme / Indexed / Auto + Tint）。
	/// 适用于 Color / ForegroundColor / BackgroundColor。</summary>
	static MediaColor? resolvecolor(ColorType color, MediaColor[] theme, bool defaultDark) {
		if (color == null) return null;
		if (color.Rgb != null && color.Rgb.HasValue) {
			var c = parsergb(color.Rgb.Value);
			if (c.HasValue) return applytint(c.Value, color.Tint);
		}
		if (color.Theme != null && color.Theme.HasValue) {
			var ti = (int)color.Theme.Value;
			if (ti >= 0 && ti < theme.Length)
				return applytint(theme[ti], color.Tint);
		}
		if (color.Indexed != null && color.Indexed.HasValue) {
			var c = indexedcolor((int)color.Indexed.Value);
			if (c.HasValue) return applytint(c.Value, color.Tint);
		}
		if (color.Auto != null && color.Auto.HasValue && color.Auto.Value)
			return defaultDark ? MediaColor.FromRgb(0, 0, 0) : MediaColor.FromRgb(0xFF, 0xFF, 0xFF);
		return null;
	}

	static MediaColor applytint(MediaColor c, DoubleValue tint) {
		if (tint == null || !tint.HasValue) return c;
		var t = tint.Value;
		if (Math.Abs(t) < 1e-9) return c;
		// Excel tint: >0 变浅，<0 变深
		double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
		if (t < 0) {
			var k = 1 + t;
			r *= k; g *= k; b *= k;
		} else {
			r = r * (1 - t) + t;
			g = g * (1 - t) + t;
			b = b * (1 - t) + t;
		}
		return MediaColor.FromRgb(tobyte(r), tobyte(g), tobyte(b));
	}

	static byte tobyte(double v) {
		if (v < 0) v = 0;
		if (v > 1) v = 1;
		return (byte)Math.Round(v * 255);
	}

	static MediaColor? parsergb(string hex) {
		if (string.IsNullOrEmpty(hex)) return null;
		hex = hex.Trim();
		if (hex.StartsWith("#")) hex = hex.Substring(1);
		// AARRGGBB or RRGGBB
		try {
			if (hex.Length == 8) {
				var a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
				var r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
				var g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
				var b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
				return MediaColor.FromArgb(a, r, g, b);
			}
			if (hex.Length == 6) {
				var r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
				var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
				var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
				return MediaColor.FromRgb(r, g, b);
			}
		} catch { /* ignore */ }
		return null;
	}

	static MediaColor? indexedcolor(int idx) {
		// 常用 Excel 索引调色板（前 64）
		var pal = new[] {
			0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
			0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
			0x800000, 0x008000, 0x000080, 0x808000, 0x800080, 0x008080, 0xC0C0C0, 0x808080,
			0x9999FF, 0x993366, 0xFFFFCC, 0xCCFFFF, 0x660066, 0xFF8080, 0x0066CC, 0xCCCCFF,
			0x000080, 0xFF00FF, 0xFFFF00, 0x00FFFF, 0x800080, 0x800000, 0x008080, 0x0000FF,
			0x00CCFF, 0xCCFFFF, 0xCCFFCC, 0xFFFF99, 0x99CCFF, 0xFF99CC, 0xCC99FF, 0xFFCC99,
			0x3366FF, 0x33CCCC, 0x99CC00, 0xFFCC00, 0xFF9900, 0xFF6600, 0x666699, 0x969696,
			0x003366, 0x339966, 0x003300, 0x333300, 0x993300, 0x993366, 0x333399, 0x333333,
		};
		if (idx < 0 || idx >= pal.Length) {
			if (idx == 64) return MediaColor.FromRgb(0, 0, 0); // system fg
			if (idx == 65) return MediaColor.FromRgb(0xFF, 0xFF, 0xFF);
			return null;
		}
		var v = pal[idx];
		return MediaColor.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
	}

	/// <summary>SharedStringTable → string[]，打开时一次扫完，供全表 O(1) 取文本。</summary>
	static string[] loadsst(SharedStringTable sst) {
		if (sst == null) return Array.Empty<string>();
		var list = new List<string>(Math.Max(16, (int)(sst.UniqueCount?.Value ?? 0)));
		foreach (var item in sst.Elements<SharedStringItem>()) {
			// 富文本 si 可能多段 r/t，InnerText 已拼接
			list.Add(item.InnerText ?? "");
		}
		return list.ToArray();
	}

	/// <summary>
	/// 读入工作表：把使用范围内全部单元格的文本+样式物化到稠密二维数组。
	/// 虚表之后只读内存，不再碰 OpenXML / 共享字符串表。
	/// </summary>
	static SheetModel readsheet(WorksheetPart part, string[] sst, StyleBook styles) {
		var model = new SheetModel();
		var ws = part.Worksheet;
		if (ws == null) return model;

		// 默认列宽/行高（Excel 字符宽 / 磅）
		var defColChars = 8.43;
		var defRowPt = 15.0;
		var sfp = ws.GetFirstChild<SheetFormatProperties>();
		if (sfp != null) {
			if (sfp.DefaultColumnWidth != null && sfp.DefaultColumnWidth.HasValue)
				defColChars = sfp.DefaultColumnWidth.Value;
			if (sfp.DefaultRowHeight != null && sfp.DefaultRowHeight.HasValue)
				defRowPt = sfp.DefaultRowHeight.Value;
		}
		var defColDip = colcharstodip(defColChars);
		var defRowDip = rowpttodip(defRowPt);

		// 合并：先解析以扩展行列范围
		var merges = new List<SheetMerge>();
		var mergeRoot = ws.Elements<MergeCells>().FirstOrDefault();
		if (mergeRoot != null) {
			foreach (var mc in mergeRoot.Elements<MergeCell>()) {
				if (tryparsemerge(mc.Reference?.Value, out var m))
					merges.Add(m);
			}
		}
		model.Merges = merges;

		var sheetData = ws.GetFirstChild<SheetData>();
		var rowEls = sheetData?.Elements<Row>().Take(MAX_ROWS).ToList() ?? new List<Row>();

		var maxCol = 0;
		var maxR = -1;
		foreach (var row in rowEls) {
			if (row.RowIndex != null) {
				var ri = (int)row.RowIndex.Value - 1;
				if (ri > maxR) maxR = ri;
			}
			foreach (var cell in row.Elements<Cell>()) {
				var col = colindex(cell.CellReference?.Value);
				if (col > maxCol) maxCol = col;
			}
		}
		// cols 定义可能比数据更宽
		foreach (var col in ws.Elements<Columns>().SelectMany(x => x.Elements<Column>())) {
			var mx = (int)(col.Max?.Value ?? 0) - 1;
			if (mx > maxCol) maxCol = mx;
		}
		foreach (var m in merges) {
			if (m.C1 > maxCol) maxCol = m.C1;
			if (m.R1 > maxR) maxR = m.R1;
		}
		if (maxCol < 0) maxCol = 0;
		if (maxCol >= MAX_COLS) maxCol = MAX_COLS - 1;

		// 空表：默认网格（稠密 SharedEmpty）
		if (maxR < 0 && rowEls.Count == 0) {
			const int EMPTY_ROWS = 30;
			const int EMPTY_COLS_MIN = 10;
			var nCols = Math.Max(EMPTY_COLS_MIN, maxCol + 1);
			if (nCols > MAX_COLS) nCols = MAX_COLS;
			var colWEmpty = new double[nCols];
			for (var i = 0; i < nCols; i++) colWEmpty[i] = defColDip;
			foreach (var col in ws.Elements<Columns>().SelectMany(x => x.Elements<Column>())) {
				var min = (int)(col.Min != null && col.Min.HasValue ? col.Min.Value : 1u) - 1;
				var max = (int)(col.Max != null && col.Max.HasValue ? col.Max.Value : (uint)(min + 2)) - 1;
				if (min < 0) min = 0;
				if (max >= nCols) max = nCols - 1;
				double wDip;
				if (col.Width != null && col.Width.HasValue)
					wDip = colcharstodip(col.Width.Value);
				else
					wDip = defColDip;
				if (col.Hidden != null && col.Hidden.HasValue && col.Hidden.Value)
					wDip = 0;
				for (var c = min; c <= max; c++)
					colWEmpty[c] = wDip;
			}
			var rowHEmpty = new double[EMPTY_ROWS];
			for (var i = 0; i < EMPTY_ROWS; i++) rowHEmpty[i] = defRowDip;
			var cellsEmpty = new SheetCell[EMPTY_ROWS][];
			for (var r = 0; r < EMPTY_ROWS; r++) {
				cellsEmpty[r] = new SheetCell[nCols];
				for (var c = 0; c < nCols; c++)
					cellsEmpty[r][c] = SheetCell.SharedEmpty;
			}
			model.ColWidths = colWEmpty;
			model.RowHeights = rowHEmpty;
			model.Cells = cellsEmpty;
			model.Dense = true;
			XlsxPictures.Load(part, model);
			DocViewLog.Info($"readsheet empty rows={model.Rows} cols={model.Cols} pics={model.Pictures?.Count ?? 0}");
			return model;
		}
		if (maxR < 0) maxR = Math.Max(0, rowEls.Count - 1);
		if (maxR >= MAX_ROWS) maxR = MAX_ROWS - 1;

		var nCol = maxCol + 1;
		var nRow = maxR + 1;

		// 列宽 + 列默认样式索引（-1=无）
		var colW = new double[nCol];
		var colStyle = new int[nCol];
		for (var i = 0; i < nCol; i++) {
			colW[i] = defColDip;
			colStyle[i] = -1;
		}
		foreach (var col in ws.Elements<Columns>().SelectMany(x => x.Elements<Column>())) {
			var min = (int)(col.Min != null && col.Min.HasValue ? col.Min.Value : 1u) - 1;
			var max = (int)(col.Max != null && col.Max.HasValue ? col.Max.Value : (uint)(min + 2)) - 1;
			if (min < 0) min = 0;
			if (max > maxCol) max = maxCol;
			double wDip;
			if (col.Width != null && col.Width.HasValue)
				wDip = colcharstodip(col.Width.Value);
			else
				wDip = defColDip;
			if (col.Hidden != null && col.Hidden.HasValue && col.Hidden.Value)
				wDip = 0;
			var si = -1;
			if (col.Style != null && col.Style.HasValue)
				si = (int)col.Style.Value;
			for (var c = min; c <= max; c++) {
				colW[c] = wDip;
				if (si >= 0) colStyle[c] = si;
			}
		}
		model.ColWidths = colW;

		// 稠密网格：result[r][c] 最终全部非 null（内容格 / 样式格 / SharedEmpty）
		var rowH = new double[nRow];
		var rowStyle = new int[nRow];
		var result = new SheetCell[nRow][];
		for (var r = 0; r < nRow; r++) {
			rowH[r] = defRowDip;
			rowStyle[r] = -1;
			result[r] = new SheetCell[nCol]; // 槽初值 null，写完再填默认样式
		}

		// 无文本、仅样式的格共享实例（只读）
		var styleCache = new Dictionary<int, SheetCell>();
		SheetCell styledempty(int si) {
			if (si < 0) return SheetCell.SharedEmpty;
			if (styleCache.TryGetValue(si, out var cached)) return cached;
			var fmt = getformat(styles, si);
			if (fmt == null || isdefaultstyle(fmt)) {
				styleCache[si] = SheetCell.SharedEmpty;
				return SheetCell.SharedEmpty;
			}
			var inst = fmt.CloneStyle();
			styleCache[si] = inst;
			return inst;
		}

		var nextAnon = 0;
		foreach (var row in rowEls) {
			int ridx;
			if (row.RowIndex != null)
				ridx = (int)row.RowIndex.Value - 1;
			else {
				ridx = nextAnon++;
			}
			if (ridx < 0 || ridx > maxR) continue;

			if (row.Height != null && row.Height.HasValue)
				rowH[ridx] = rowpttodip(row.Height.Value);
			if (row.Hidden != null && row.Hidden.HasValue && row.Hidden.Value)
				rowH[ridx] = 0;

			var rsi = -1;
			if (row.StyleIndex != null && row.StyleIndex.HasValue)
				rsi = (int)row.StyleIndex.Value;
			rowStyle[ridx] = rsi;

			foreach (var cell in row.Elements<Cell>()) {
				var col = colindex(cell.CellReference?.Value);
				if (col < 0 || col > maxCol) continue;
				// 样式优先级：单元格 > 行 > 列 > 0
				var csi = -1;
				if (cell.StyleIndex != null && cell.StyleIndex.HasValue)
					csi = (int)cell.StyleIndex.Value;
				else if (rsi >= 0)
					csi = rsi;
				else if (colStyle[col] >= 0)
					csi = colStyle[col];
				else
					csi = 0;
				result[ridx][col] = buildcell(cell, sst, styles, csi);
			}
		}
		model.RowHeights = rowH;

		// 空槽物化：行样式 > 列样式 > SharedEmpty（有 <c> 的格已非 null）
		for (var r = 0; r < nRow; r++) {
			var row = result[r];
			var rsi = rowStyle[r];
			for (var c = 0; c < nCol; c++) {
				if (row[c] != null) continue;
				var si = rsi >= 0 ? rsi : colStyle[c];
				row[c] = styledempty(si);
			}
		}

		// 标注合并（从格/原点必须可写实例，不能改 SharedEmpty 或共享样式）
		foreach (var m in merges) {
			var r0 = clampi(m.R0, 0, maxR);
			var r1 = clampi(m.R1, 0, maxR);
			var c0 = clampi(m.C0, 0, maxCol);
			var c1 = clampi(m.C1, 0, maxCol);
			m.R0 = r0; m.R1 = r1; m.C0 = c0; m.C1 = c1;

			var origin = ensurewritable(result[r0][c0], styleCache);
			result[r0][c0] = origin;
			origin.RowSpan = r1 - r0 + 1;
			origin.ColSpan = c1 - c0 + 1;

			for (var r = r0; r <= r1; r++) {
				for (var c = c0; c <= c1; c++) {
					if (r == r0 && c == c0) continue;
					var slave = ensurewritable(result[r][c], styleCache);
					if (string.IsNullOrEmpty(origin.Text) && !string.IsNullOrEmpty(slave.Text)) {
						origin.Text = slave.Text;
						slave.Text = "";
					}
					slave.HiddenByMerge = true;
					result[r][c] = slave;
				}
			}
		}

		model.Cells = result;
		model.Dense = true;
		readfreezeandfilter(ws, model, nRow, nCol);
		XlsxPictures.Load(part, model);
		DocViewLog.Info($"readsheet dense rows={model.Rows} cols={model.Cols} merges={merges.Count} freeze={model.FreezeRows}x{model.FreezeCols} filter={model.HasFilterRange} pics={model.Pictures?.Count ?? 0}");
		return model;
	}

	/// <summary>解析冻结窗格 + AutoFilter 区域。</summary>
	static void readfreezeandfilter(Worksheet ws, SheetModel model, int nRow, int nCol) {
		if (ws == null || model == null) return;
		try {
			var views = ws.GetFirstChild<SheetViews>();
			var view = views?.Elements<SheetView>().FirstOrDefault();
			var pane = view?.GetFirstChild<Pane>();
			if (pane != null) {
				// Excel：xSplit=冻结列数，ySplit=冻结行数（可为小数，取整）
				if (pane.HorizontalSplit != null && pane.HorizontalSplit.HasValue)
					model.FreezeCols = Math.Max(0, (int)Math.Floor(pane.HorizontalSplit.Value + 0.001));
				if (pane.VerticalSplit != null && pane.VerticalSplit.HasValue)
					model.FreezeRows = Math.Max(0, (int)Math.Floor(pane.VerticalSplit.Value + 0.001));
				// State=Frozen / FrozenSplit 才算冻结；split 也可能仅是拆分
				var st = pane.State?.Value;
				if (st != null && st != PaneStateValues.Frozen && st != PaneStateValues.FrozenSplit) {
					// 普通 split 不当作冻结
					if (st == PaneStateValues.Split) {
						model.FreezeRows = 0;
						model.FreezeCols = 0;
					}
				}
			}
			if (model.FreezeRows > nRow) model.FreezeRows = nRow;
			if (model.FreezeCols > nCol) model.FreezeCols = nCol;
		} catch (Exception ex) {
			DocViewLog.Warn($"read freeze: {ex.Message}");
		}

		try {
			// 仅文件内真实 AutoFilter 才启用筛选（无则 Filter*=-1，网格不画 ▼）
			var af = ws.GetFirstChild<AutoFilter>();
			var reff = af?.Reference?.Value;
			if (!string.IsNullOrEmpty(reff) && tryparseref(reff, out var r0, out var c0, out var r1, out var c1)) {
				model.FilterR0 = clampi(r0, 0, Math.Max(0, nRow - 1));
				model.FilterR1 = clampi(r1, 0, Math.Max(0, nRow - 1));
				model.FilterC0 = clampi(c0, 0, Math.Max(0, nCol - 1));
				model.FilterC1 = clampi(c1, 0, Math.Max(0, nCol - 1));
			}
		} catch (Exception ex) {
			DocViewLog.Warn($"read autofilter: {ex.Message}");
		}
	}

	/// <summary>解析 A1 或 A1:D10。</summary>
	static bool tryparseref(string reff, out int r0, out int c0, out int r1, out int c1) {
		r0 = c0 = r1 = c1 = 0;
		if (string.IsNullOrWhiteSpace(reff)) return false;
		var parts = reff.Split(':');
		if (!tryparsecell(parts[0], out r0, out c0)) return false;
		if (parts.Length == 1) {
			r1 = r0; c1 = c0;
			return true;
		}
		if (!tryparsecell(parts[1], out r1, out c1)) return false;
		if (r1 < r0) { var t = r0; r0 = r1; r1 = t; }
		if (c1 < c0) { var t = c0; c0 = c1; c1 = t; }
		return true;
	}

	static bool tryparsecell(string a1, out int r, out int c) {
		r = c = 0;
		if (string.IsNullOrWhiteSpace(a1)) return false;
		a1 = a1.Trim().Trim('$');
		var i = 0;
		while (i < a1.Length && char.IsLetter(a1[i])) i++;
		if (i == 0 || i >= a1.Length) return false;
		c = 0;
		for (var k = 0; k < i; k++)
			c = c * 26 + (char.ToUpperInvariant(a1[k]) - 'A' + 1);
		c--;
		if (!int.TryParse(a1.Substring(i), out var row) || row < 1) return false;
		r = row - 1;
		return r >= 0 && c >= 0;
	}

	/// <summary>保证返回可写实例（复制 SharedEmpty / 共享样式缓存）。</summary>
	static SheetCell ensurewritable(SheetCell sc, Dictionary<int, SheetCell> styleCache) {
		if (sc == null || ReferenceEquals(sc, SheetCell.SharedEmpty))
			return SheetCell.Empty();
		if (styleCache != null) {
			foreach (var kv in styleCache) {
				if (ReferenceEquals(kv.Value, sc)) {
					var n = sc.CloneStyle();
					n.Text = sc.Text ?? "";
					return n;
				}
			}
		}
		return sc;
	}

	static SheetCell getformat(StyleBook styles, int si) {
		if (styles == null || styles.Formats == null || si < 0 || si >= styles.Formats.Count)
			return null;
		return styles.Formats[si];
	}

	static bool isdefaultstyle(SheetCell f) {
		if (f == null) return true;
		if (f.Bold || f.Italic || f.WrapText) return false;
		if (f.ForeColor.HasValue || f.BackColor.HasValue) return false;
		if (f.BorderLeft.HasValue || f.BorderRight.HasValue || f.BorderTop.HasValue || f.BorderBottom.HasValue)
			return false;
		if (f.Align != TextAlignment.Left || f.VAlign != 1) return false;
		if (!string.IsNullOrEmpty(f.FontName) && f.FontSizePt != 11) return false;
		return string.IsNullOrEmpty(f.FontName) || f.FontSizePt == 11;
	}

	static void applyformat(SheetCell sc, StyleBook styles, int si) {
		var f = getformat(styles, si);
		if (f == null) return;
		sc.FontName = f.FontName;
		sc.FontSizePt = f.FontSizePt;
		sc.Bold = f.Bold;
		sc.Italic = f.Italic;
		sc.ForeColor = f.ForeColor;
		sc.BackColor = f.BackColor;
		sc.BorderLeft = f.BorderLeft;
		sc.BorderRight = f.BorderRight;
		sc.BorderTop = f.BorderTop;
		sc.BorderBottom = f.BorderBottom;
		sc.BorderLeftW = f.BorderLeftW;
		sc.BorderRightW = f.BorderRightW;
		sc.BorderTopW = f.BorderTopW;
		sc.BorderBottomW = f.BorderBottomW;
		sc.Align = f.Align;
		sc.VAlign = f.VAlign;
		sc.WrapText = f.WrapText;
	}

	static SheetCell buildcell(Cell cell, string[] sst, StyleBook styles, int styleIndex) {
		var sc = SheetCell.Empty();
		sc.Text = celltext(cell, sst);
		if (styleIndex < 0) styleIndex = 0;
		applyformat(sc, styles, styleIndex);
		return sc;
	}

	/// <summary>Excel 列宽（字符数）→ DIP（96dpi 像素近似）。</summary>
	static double colcharstodip(double chars) {
		if (chars < 0) chars = 0;
		// ECMA / Excel：maxDigitWidth≈7（Calibri 11）
		const double MDW = 7.0;
		var px = Math.Floor((256.0 * chars + Math.Floor(128.0 / MDW)) / 256.0 * MDW);
		if (px < 0) px = 0;
		// 额外 +5 像素边距（Excel 列宽像素公式的一部分简化）
		if (chars > 0 && px < 1) px = 1;
		return px + (chars > 0 ? 5 : 0);
	}

	/// <summary>行高（磅）→ DIP。</summary>
	static double rowpttodip(double pt) {
		if (pt < 0) pt = 0;
		return pt * 96.0 / 72.0;
	}

	static bool tryparsemerge(string refs, out SheetMerge m) {
		m = null;
		if (string.IsNullOrWhiteSpace(refs)) return false;
		var parts = refs.Split(':');
		if (parts.Length == 1) {
			if (!tryparsecellref(parts[0], out var r, out var c)) return false;
			m = new SheetMerge { R0 = r, C0 = c, R1 = r, C1 = c };
			return true;
		}
		if (parts.Length != 2) return false;
		if (!tryparsecellref(parts[0], out var r0, out var c0)) return false;
		if (!tryparsecellref(parts[1], out var r1, out var c1)) return false;
		if (r1 < r0) { var t = r0; r0 = r1; r1 = t; }
		if (c1 < c0) { var t = c0; c0 = c1; c1 = t; }
		m = new SheetMerge { R0 = r0, C0 = c0, R1 = r1, C1 = c1 };
		return true;
	}

	static bool tryparsecellref(string cellRef, out int row0, out int col0) {
		row0 = col0 = -1;
		if (string.IsNullOrEmpty(cellRef)) return false;
		col0 = colindex(cellRef);
		var i = 0;
		while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
		if (i >= cellRef.Length) return false;
		if (!int.TryParse(cellRef.Substring(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r1))
			return false;
		row0 = r1 - 1;
		return row0 >= 0 && col0 >= 0;
	}

	static int clampi(int v, int lo, int hi) {
		if (v < lo) return lo;
		if (v > hi) return hi;
		return v;
	}

	static string celltext(Cell cell, string[] sst) {
		var raw = cell.CellValue?.InnerText ?? "";
		if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString) {
			if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
				&& sst != null && idx >= 0 && idx < sst.Length)
				return sst[idx] ?? "";
			return raw;
		}
		if (cell.DataType != null && cell.DataType.Value == CellValues.Boolean)
			return raw == "1" ? "TRUE" : "FALSE";
		if (cell.DataType != null && cell.DataType.Value == CellValues.InlineString)
			return cell.InlineString?.Text?.Text ?? cell.InnerText ?? "";
		return raw;
	}

	static int colindex(string cellRef) {
		if (string.IsNullOrEmpty(cellRef)) return -1;
		var n = 0;
		foreach (var ch in cellRef) {
			if (ch >= 'A' && ch <= 'Z')
				n = n * 26 + (ch - 'A' + 1);
			else if (ch >= 'a' && ch <= 'z')
				n = n * 26 + (ch - 'a' + 1);
			else
				break;
		}
		return n - 1;
	}

	static double clamp(double v, double lo, double hi) {
		if (v < lo) return lo;
		if (v > hi) return hi;
		return v;
	}
}

/// <summary>后台解析结果（无 WPF 对象）。</summary>
sealed class XlsxLoadData {
	public string Path;
	public string Title;
	public List<XlsxSheetData> Sheets;
	/// <summary>true 表示 .xls 等旧格式，只读。</summary>
	public bool LegacyBinary;
}

sealed class XlsxSheetData {
	public string Name;
	public SheetModel Model;
}
