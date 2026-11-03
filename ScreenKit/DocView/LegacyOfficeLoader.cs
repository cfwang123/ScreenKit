using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NPOI.HSSF.UserModel;
using NPOI.HWPF;
using NPOI.HWPF.UserModel;
using NPOI.SS.UserModel;
using CellAlign = NPOI.SS.UserModel.HorizontalAlignment;
using HwpfParagraph = NPOI.HWPF.UserModel.Paragraph;
using HwpfTable = NPOI.HWPF.UserModel.Table;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;
using WpfTableRow = System.Windows.Documents.TableRow;
using WpfTableRowGroup = System.Windows.Documents.TableRowGroup;
using MediaColor = System.Windows.Media.Color;

namespace ScreenKit.DocView;

/// <summary>
/// 旧版 Office 二进制格式（.doc / .xls）解析，供 DocxViewer / XlsxViewer 复用 UI。
/// </summary>
static class LegacyOfficeLoader {
	const int MAX_ROWS = 10000;
	const int MAX_COLS = 200;

	public static XlsxLoadData PrepareXls(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException("文件不存在", path);
		path = Path.GetFullPath(path);
		var data = new XlsxLoadData {
			Path = path,
			Title = Path.GetFileName(path),
			Sheets = new List<XlsxSheetData>(),
			LegacyBinary = true,
		};
		using var fs = DocFileIo.OpenReadShared(path);
		var wb = new HSSFWorkbook(fs);
		var fmt = new DataFormatter(CultureInfo.InvariantCulture);
		for (var si = 0; si < wb.NumberOfSheets; si++) {
			var sheet = wb.GetSheetAt(si);
			if (sheet == null) continue;
			data.Sheets.Add(new XlsxSheetData {
				Name = sheet.SheetName ?? $"Sheet{si + 1}",
				Model = readsheet(sheet, wb, fmt),
			});
		}
		DocViewLog.Info($"Xls Prepare sheets={data.Sheets.Count} path={path}");
		return data;
	}

	public static void LoadDocInto(FlowDocument flow, Stream stream) {
		if (flow == null) throw new ArgumentNullException(nameof(flow));
		if (stream == null) throw new ArgumentNullException(nameof(stream));
		var doc = new HWPFDocument(stream);
		var range = doc.GetRange();
		if (range == null) return;

		var numPara = range.NumParagraphs;
		for (var i = 0; i < numPara; i++) {
			var p = range.GetParagraph(i);
			if (p == null) continue;
			// GetTable 只接受表格第一段；后续段再调会抛 “not the first one in the table”
			if (p.IsInTable() && addtable(p, ref i))
				continue;
			flow.Blocks.Add(buildhwpparagraph(p));
		}
		return;

		bool addtable(HwpfParagraph para, ref int i) {
			HwpfTable table;
			try {
				table = range.GetTable(para);
			} catch (ArgumentException) {
				return false;
			} catch (Exception ex) {
				DocViewLog.Warn($"doc GetTable: {ex.Message}");
				return false;
			}
			if (table == null) return false;
			WpfTable visual;
			try {
				visual = buildhwptable(table);
			} catch (Exception ex) {
				DocViewLog.Warn($"doc table: {ex.Message}");
				return false;
			}
			flow.Blocks.Add(visual);
			var tableEnd = table.EndOffset;
			while (i + 1 < numPara) {
				HwpfParagraph next;
				try { next = range.GetParagraph(i + 1); }
				catch { break; }
				if (next == null || next.StartOffset >= tableEnd) break;
				i++;
			}
			return true;
		}
	}

	static SheetModel readsheet(ISheet sheet, IWorkbook wb, DataFormatter fmt) {
		var model = new SheetModel { Merges = new List<SheetMerge>() };
		var lastRow = sheet.LastRowNum;
		if (lastRow < 0)
			return emptysheet();

		var maxCol = 0;
		for (var r = 0; r <= lastRow && r < MAX_ROWS; r++) {
			var row = sheet.GetRow(r);
			if (row == null) continue;
			var lc = (int)row.LastCellNum;
			if (lc > maxCol) maxCol = lc;
		}
		if (maxCol > MAX_COLS) maxCol = MAX_COLS;

		for (var i = 0; i < sheet.NumMergedRegions; i++) {
			var reg = sheet.GetMergedRegion(i);
			if (reg == null) continue;
			if (reg.LastColumn + 1 > maxCol) maxCol = reg.LastColumn + 1;
			if (reg.LastRow > lastRow) lastRow = reg.LastRow;
			model.Merges.Add(new SheetMerge {
				R0 = reg.FirstRow,
				C0 = reg.FirstColumn,
				R1 = reg.LastRow,
				C1 = reg.LastColumn,
			});
		}
		if (maxCol < 1) maxCol = 1;

		var nRow = Math.Min(lastRow + 1, MAX_ROWS);
		var nCol = maxCol;

		var colW = new double[nCol];
		for (var c = 0; c < nCol; c++)
			colW[c] = colwidthtodip(sheet.GetColumnWidth(c) / 256.0);
		model.ColWidths = colW;

		var rowH = new double[nRow];
		var cells = new SheetCell[nRow][];
		for (var r = 0; r < nRow; r++) {
			rowH[r] = 20;
			cells[r] = new SheetCell[nCol];
			for (var c = 0; c < nCol; c++)
				cells[r][c] = SheetCell.SharedEmpty;
		}

		for (var r = 0; r < nRow; r++) {
			var row = sheet.GetRow(r);
			if (row != null && row.Height >= 0)
				rowH[r] = rowpttodip(row.Height / 20.0);
			if (row == null) continue;
			for (var c = 0; c < nCol; c++) {
				var cell = row.GetCell(c);
				if (cell == null) continue;
				cells[r][c] = buildcell(cell, wb, fmt);
			}
		}

		applymerges(model.Merges, cells);
		model.RowHeights = rowH;
		model.Cells = cells;
		model.Dense = true;
		XlsxPictures.LoadXls(sheet, model);
		return model;
	}

	static SheetModel emptysheet() {
		const int rows = 30;
		const int cols = 10;
		var model = new SheetModel {
			Merges = new List<SheetMerge>(),
			ColWidths = new double[cols],
			RowHeights = new double[rows],
			Cells = new SheetCell[rows][],
			Dense = true,
		};
		for (var c = 0; c < cols; c++) model.ColWidths[c] = 64;
		for (var r = 0; r < rows; r++) {
			model.RowHeights[r] = 20;
			model.Cells[r] = new SheetCell[cols];
			for (var c = 0; c < cols; c++)
				model.Cells[r][c] = SheetCell.SharedEmpty;
		}
		return model;
	}

	static SheetCell buildcell(ICell cell, IWorkbook wb, DataFormatter fmt) {
		var sc = SheetCell.Empty();
		sc.Text = fmt.FormatCellValue(cell) ?? "";
		var style = cell.CellStyle;
		if (style == null) return sc;
		var font = wb.GetFontAt(style.FontIndex);
		if (font != null) {
			sc.Bold = font.IsBold;
			sc.Italic = font.IsItalic;
			if (font.FontHeightInPoints > 0)
				sc.FontSizePt = font.FontHeightInPoints;
			if (!string.IsNullOrEmpty(font.FontName))
				sc.FontName = font.FontName;
		}
		sc.Align = style.Alignment switch {
			CellAlign.Center => TextAlignment.Center,
			CellAlign.Right => TextAlignment.Right,
			CellAlign.Justify => TextAlignment.Justify,
			_ => TextAlignment.Left,
		};
		sc.WrapText = style.WrapText;
		return sc;
	}

	static void applymerges(List<SheetMerge> merges, SheetCell[][] cells) {
		if (merges == null || cells == null) return;
		foreach (var m in merges) {
			if (m == null || m.R0 < 0 || m.C0 < 0) continue;
			if (m.R0 >= cells.Length || m.C0 >= cells[m.R0].Length) continue;
			var origin = cells[m.R0][m.C0];
			if (ReferenceEquals(origin, SheetCell.SharedEmpty)) {
				origin = SheetCell.Empty();
				cells[m.R0][m.C0] = origin;
			}
			origin.RowSpan = m.R1 - m.R0 + 1;
			origin.ColSpan = m.C1 - m.C0 + 1;
			for (var r = m.R0; r <= m.R1; r++) {
				if (r >= cells.Length) break;
				for (var c = m.C0; c <= m.C1; c++) {
					if (r == m.R0 && c == m.C0) continue;
					if (c >= cells[r].Length) break;
					var cell = cells[r][c];
					if (ReferenceEquals(cell, SheetCell.SharedEmpty)) {
						cell = SheetCell.Empty();
						cells[r][c] = cell;
					}
					cell.HiddenByMerge = true;
				}
			}
		}
	}

	static WpfParagraph buildhwpparagraph(HwpfParagraph p) {
		var text = cleantext(p?.Text);
		if (string.IsNullOrWhiteSpace(text))
			return new WpfParagraph(new Run("\u00A0")) { Margin = new Thickness(0, 2, 0, 2) };

		var para = new WpfParagraph { Margin = new Thickness(0, 0, 0, 6) };
		var run = new Run(text);
		para.Inlines.Add(run);
		return para;
	}

	static WpfTable buildhwptable(HwpfTable table) {
		var wt = new WpfTable {
			CellSpacing = 0,
			Margin = new Thickness(0, 4, 0, 8),
			BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0xD1, 0xD5, 0xDB)),
			BorderThickness = new Thickness(1),
		};
		for (var r = 0; r < table.NumRows; r++) {
			var row = table.GetRow(r);
			if (row == null) continue;
			var tr = new WpfTableRow();
			for (var c = 0; c < row.NumCells(); c++) {
				var cell = row.GetCell(c);
				var text = cleantext(cell?.Text);
				var tc = new WpfTableCell(new WpfParagraph(new Run(string.IsNullOrEmpty(text) ? "\u00A0" : text))) {
					BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0xE5, 0xE7, 0xEB)),
					BorderThickness = new Thickness(0.5),
					Padding = new Thickness(4, 2, 4, 2),
				};
				tr.Cells.Add(tc);
			}
			if (tr.Cells.Count > 0)
				wt.RowGroups.Add(new WpfTableRowGroup { Rows = { tr } });
		}
		return wt;
	}

	static string cleantext(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		return s.Replace('\u0007', '\t').Replace("\r", "").TrimEnd('\r', '\n', '\u0007');
	}

	static double colwidthtodip(double chars) {
		if (chars < 0) chars = 0;
		const double MDW = 7.0;
		var px = Math.Floor((256.0 * chars + Math.Floor(128.0 / MDW)) / 256.0 * MDW);
		if (chars > 0 && px < 1) px = 1;
		return px + (chars > 0 ? 5 : 0);
	}

	static double rowpttodip(double pt) {
		if (pt < 0) pt = 0;
		return pt * 96.0 / 72.0;
	}
}
