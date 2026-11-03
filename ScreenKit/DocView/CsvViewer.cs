using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace ScreenKit.DocView;

/// <summary>CSV / TSV 只读表格预览（复用 VirtualSheetGrid）。</summary>
sealed class CsvViewer : IDocViewer {
	readonly Grid root;
	readonly VirtualSheetGrid grid;
	double zoom = 1.0;
	string filePath;
	char sep = ',';

	public FrameworkElement View => root;
	public string FilePath => filePath;
	public string Title { get; private set; }
	public DocKind Kind => DocKind.Csv;
	public double Zoom => zoom;
	public string StatusText {
		get {
			var tag = sep == '\t' ? "TSV" : "CSV";
			return $"{tag}  ·  {grid.Rows} 行 × {grid.Cols} 列  ·  {(int)(zoom * 100)}%";
		}
	}
	public int PageCount => 1;
	public int CurrentPage => 1;
	public bool HasOutline => false;
	public bool SidePanelVisible => false;

	public event Action StatusChanged;

	public CsvViewer() {
		grid = new VirtualSheetGrid();
		grid.EditMode = false;
		grid.ZoomChanged += () => {
			zoom = grid.Zoom;
			StatusChanged?.Invoke();
		};
		grid.ScrollProgressChanged += () => StatusChanged?.Invoke();
		root = new Grid { Background = Brushes.White };
		root.Children.Add(grid);
	}

	public void Load(string path) {
		path = Path.GetFullPath(path);
		filePath = path;
		Title = Path.GetFileName(path);
		sep = path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
		var r = TextFileIo.Load(path);
		var model = ParseToModel(r.Text ?? "", sep);
		// 首行作冻结表头
		if (model.Rows > 0) model.FreezeRows = 1;
		grid.SetData(model, zoom);
		DocViewLog.Info($"Csv Load rows={model.Rows} cols={model.Cols} sep={(sep == '\t' ? "TAB" : ",")} path={path}");
		StatusChanged?.Invoke();
	}

	public static SheetModel ParseToModel(string text, char delimiter) {
		var rows = Parse(text, delimiter);
		var model = new SheetModel { Dense = true };
		if (rows.Count == 0) {
			model.Cells = new[] { new[] { SheetCell.Empty() } };
			model.ColWidths = new[] { 80.0 };
			model.RowHeights = new[] { 20.0 };
			return model;
		}
		var maxC = 1;
		foreach (var row in rows)
			if (row != null && row.Count > maxC) maxC = row.Count;
		var cells = new SheetCell[rows.Count][];
		var rowH = new double[rows.Count];
		var colW = new double[maxC];
		for (var c = 0; c < maxC; c++) colW[c] = 96;
		for (var r = 0; r < rows.Count; r++) {
			rowH[r] = 20;
			var line = rows[r] ?? new List<string>();
			var arr = new SheetCell[maxC];
			for (var c = 0; c < maxC; c++) {
				var t = c < line.Count ? (line[c] ?? "") : "";
				arr[c] = new SheetCell { Text = t };
				// 粗算列宽
				var need = 24 + Math.Min(280, t.Length * 8);
				if (need > colW[c]) colW[c] = need;
			}
			if (r == 0) {
				foreach (var cell in arr) {
					cell.Bold = true;
					cell.BackColor = MediaColor.FromRgb(0xF3, 0xF4, 0xF6);
				}
			}
			cells[r] = arr;
		}
		model.Cells = cells;
		model.ColWidths = colW;
		model.RowHeights = rowH;
		return model;
	}

	/// <summary>RFC4180 风格：引号、逗号/Tab、\r\n。</summary>
	public static List<List<string>> Parse(string text, char delimiter) {
		var result = new List<List<string>>();
		if (text == null) return result;
		var row = new List<string>();
		var sb = new StringBuilder();
		var i = 0;
		var inQ = false;
		while (i < text.Length) {
			var ch = text[i];
			if (inQ) {
				if (ch == '"') {
					if (i + 1 < text.Length && text[i + 1] == '"') {
						sb.Append('"');
						i += 2;
						continue;
					}
					inQ = false;
					i++;
					continue;
				}
				sb.Append(ch);
				i++;
				continue;
			}
			if (ch == '"') {
				inQ = true;
				i++;
				continue;
			}
			if (ch == delimiter) {
				row.Add(sb.ToString());
				sb.Clear();
				i++;
				continue;
			}
			if (ch == '\r') {
				i++;
				if (i < text.Length && text[i] == '\n') i++;
				row.Add(sb.ToString());
				sb.Clear();
				result.Add(row);
				row = new List<string>();
				continue;
			}
			if (ch == '\n') {
				i++;
				row.Add(sb.ToString());
				sb.Clear();
				result.Add(row);
				row = new List<string>();
				continue;
			}
			sb.Append(ch);
			i++;
		}
		// 最后一格
		if (sb.Length > 0 || row.Count > 0 || inQ) {
			row.Add(sb.ToString());
			result.Add(row);
		}
		// 去掉文件末尾纯空行
		while (result.Count > 0) {
			var last = result[result.Count - 1];
			if (last.Count == 1 && string.IsNullOrEmpty(last[0]))
				result.RemoveAt(result.Count - 1);
			else break;
		}
		return result;
	}

	public void SetZoom(double z) {
		grid.SetZoom(z);
		zoom = grid.Zoom;
		StatusChanged?.Invoke();
	}
	public void ZoomBy(double factor) => SetZoom(zoom * factor);
	public void ZoomIn() => SetZoom(zoom * 1.15);
	public void ZoomOut() => SetZoom(zoom / 1.15);
	public void ZoomFitWidth() => SetZoom(1.0);
	public void ZoomFitPage() => SetZoom(1.0);
	public void RotateBy(int deltaQuarterTurns) { }
	public void GoPrevPage() { }
	public void GoNextPage() { }
	public void GoToPage(int page1Based) { }
	public void SetSidePanelVisible(bool show) { }

	public void CaptureViewState(out double h, out double v, out double z, out int sheetOrPage) {
		grid.GetScrollOffset(out h, out v);
		z = zoom;
		sheetOrPage = 1;
	}

	public void RestoreViewState(double h, double v, double z, int sheetOrPage) {
		if (z > 0.05) SetZoom(z);
		grid.SetScrollOffset(h, v);
	}

	public bool TryCopySelection() => grid.TryCopySelection();

	public FindResult Find(string text, bool forward, bool ignoreCase, bool restart = false, bool fromView = false) =>
		grid.Find(text, forward, ignoreCase, restart, fromView);

	public void ClearFind() => grid.ClearFind();

	public void Dispose() {
		try { grid.ClearFind(); } catch { /* ignore */ }
	}
}
