using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace ScreenKit.DocView;

/// <summary>
/// 工作表单元格：打开时已物化文本 + 样式（虚表只做数组下标读取，不再回源解析）。
/// </summary>
sealed class SheetCell {
	public string Text = "";
	public string FontName;
	public double FontSizePt = 11;
	public bool Bold;
	public bool Italic;
	public MediaColor? ForeColor;
	public MediaColor? BackColor;
	/// <summary>边框 ARGB；null 表示用默认网格线。</summary>
	public MediaColor? BorderLeft, BorderRight, BorderTop, BorderBottom;
	public double BorderLeftW = 1, BorderRightW = 1, BorderTopW = 1, BorderBottomW = 1;
	public TextAlignment Align = TextAlignment.Left;
	/// <summary>0=Top 1=Center 2=Bottom。</summary>
	public int VAlign = 1;
	public bool WrapText;
	/// <summary>合并：仅左上角单元格有 &gt;1 的跨度。</summary>
	public int RowSpan = 1;
	public int ColSpan = 1;
	/// <summary>被合并覆盖、不单独绘制。</summary>
	public bool HiddenByMerge;

	/// <summary>无内容、无样式的共享空格（只读使用，禁止写入字段）。</summary>
	public static readonly SheetCell SharedEmpty = new SheetCell();

	public static SheetCell Empty() => new SheetCell();

	/// <summary>复制样式字段（不含 Text / 合并标记），用于列/行默认样式物化。</summary>
	public SheetCell CloneStyle() {
		return new SheetCell {
			FontName = FontName,
			FontSizePt = FontSizePt,
			Bold = Bold,
			Italic = Italic,
			ForeColor = ForeColor,
			BackColor = BackColor,
			BorderLeft = BorderLeft,
			BorderRight = BorderRight,
			BorderTop = BorderTop,
			BorderBottom = BorderBottom,
			BorderLeftW = BorderLeftW,
			BorderRightW = BorderRightW,
			BorderTopW = BorderTopW,
			BorderBottomW = BorderBottomW,
			Align = Align,
			VAlign = VAlign,
			WrapText = WrapText,
		};
	}

	/// <summary>完整复制（含文字与合并标记），用于编辑时从只读空格脱开。</summary>
	public SheetCell CloneFull() {
		var c = CloneStyle();
		c.Text = Text ?? "";
		c.RowSpan = RowSpan;
		c.ColSpan = ColSpan;
		c.HiddenByMerge = HiddenByMerge;
		return c;
	}
}

/// <summary>合并区域（含端点，0-based）。</summary>
sealed class SheetMerge {
	public int R0, C0, R1, C1;
	public bool Contains(int r, int c) => r >= R0 && r <= R1 && c >= C0 && c <= C1;
	public bool IsOrigin(int r, int c) => r == R0 && c == C0;
}

/// <summary>
/// 工作表图片（浮动锚点或铺满单元格）。坐标为未缩放 DIP，与列宽/行高同单位。
/// Kind：0 双格锚，1 单格锚（固定宽高），2 绝对坐标，3 铺满单元格。
/// </summary>
sealed class SheetPicture {
	public byte[] Bytes;
	public string Name;
	public BitmapSource Bitmap;
	public int Kind;
	public int FromCol, FromRow, ToCol, ToRow;
	public double FromX, FromY, ToX, ToY;
	public double AbsX, AbsY, AbsW, AbsH;
	public double Rot;
	public bool FlipH, FlipV;
	public double CropL, CropT, CropR, CropB;
}

/// <summary>
/// 整张工作表内存模型：Cells 为稠密 [行][列]（无 null 槽），列宽/行高 + 合并。
/// 虚表滚动/框选只读本结构，O(1) 下标访问。
/// </summary>
sealed class SheetModel {
	public SheetCell[][] Cells = Array.Empty<SheetCell[]>();
	public double[] ColWidths = Array.Empty<double>();
	public double[] RowHeights = Array.Empty<double>();
	public List<SheetMerge> Merges = new();
	/// <summary>内嵌图片。null 表示没有。</summary>
	public List<SheetPicture> Pictures;
	/// <summary>true 表示 Cells[r][c] 均非 null，可走快速路径。</summary>
	public bool Dense;

	/// <summary>冻结行数（自顶向下，Excel ySplit）。</summary>
	public int FreezeRows;
	/// <summary>冻结列数（自左向右，Excel xSplit）。</summary>
	public int FreezeCols;

	/// <summary>自动筛选区域（含表头）；未设置为 -1。</summary>
	public int FilterR0 = -1, FilterC0 = -1, FilterR1 = -1, FilterC1 = -1;
	public bool HasFilterRange => FilterR0 >= 0 && FilterC0 >= 0 && FilterR1 >= FilterR0 && FilterC1 >= FilterC0;

	public int Rows => Cells?.Length ?? 0;
	public int Cols => ColWidths?.Length ?? 0;

	/// <summary>O(1) 取格；越界或空槽返回 SharedEmpty（勿修改返回的共享实例）。</summary>
	public SheetCell CellAt(int r, int c) {
		if (r < 0 || c < 0 || Cells == null || r >= Cells.Length) return SheetCell.SharedEmpty;
		var row = Cells[r];
		if (row == null || c >= row.Length) return SheetCell.SharedEmpty;
		return row[c] ?? SheetCell.SharedEmpty;
	}

	/// <summary>若 (r,c) 在合并区内，返回该合并；否则 null。</summary>
	public SheetMerge FindMerge(int r, int c) {
		if (Merges == null || Merges.Count == 0) return null;
		foreach (var m in Merges) {
			if (m != null && m.Contains(r, c)) return m;
		}
		return null;
	}

	/// <summary>点击/选中时归一到合并原点。</summary>
	public void ResolveOrigin(ref int r, ref int c) {
		var m = FindMerge(r, c);
		if (m == null) return;
		r = m.R0;
		c = m.C0;
	}

	/// <summary>确保 (r,c) 可写；SharedEmpty / null 会分配新实例。越界时扩展表。</summary>
	public SheetCell EnsureCell(int r, int c) {
		if (r < 0 || c < 0) return SheetCell.Empty();
		ensurecapacity(r + 1, c + 1);
		var row = Cells[r];
		var cur = row[c];
		if (cur == null || ReferenceEquals(cur, SheetCell.SharedEmpty)) {
			cur = SheetCell.Empty();
			row[c] = cur;
			return cur;
		}
		return cur;
	}

	void ensurecapacity(int needRows, int needCols) {
		if (needRows < 1) needRows = 1;
		if (needCols < 1) needCols = 1;
		var oldRows = Cells?.Length ?? 0;
		var oldCols = ColWidths?.Length ?? 0;
		if (oldCols < needCols) {
			var nw = new double[needCols];
			if (ColWidths != null && ColWidths.Length > 0)
				Array.Copy(ColWidths, nw, ColWidths.Length);
			for (var i = oldCols; i < needCols; i++)
				nw[i] = oldCols > 0 ? ColWidths[Math.Min(oldCols - 1, i)] : 64;
			if (oldCols == 0)
				for (var i = 0; i < needCols; i++) nw[i] = 64;
			ColWidths = nw;
			if (Cells != null) {
				for (var r = 0; r < Cells.Length; r++) {
					var row = Cells[r];
					if (row == null) {
						Cells[r] = new SheetCell[needCols];
						continue;
					}
					if (row.Length >= needCols) continue;
					var nr = new SheetCell[needCols];
					Array.Copy(row, nr, row.Length);
					for (var c = row.Length; c < needCols; c++)
						nr[c] = SheetCell.SharedEmpty;
					Cells[r] = nr;
				}
			}
		}
		if (oldRows < needRows) {
			var nc = Math.Max(needCols, ColWidths?.Length ?? needCols);
			var ncells = new SheetCell[needRows][];
			if (Cells != null)
				Array.Copy(Cells, ncells, oldRows);
			for (var r = oldRows; r < needRows; r++) {
				var row = new SheetCell[nc];
				for (var c = 0; c < nc; c++)
					row[c] = SheetCell.SharedEmpty;
				ncells[r] = row;
			}
			Cells = ncells;
			var nh = new double[needRows];
			if (RowHeights != null && RowHeights.Length > 0)
				Array.Copy(RowHeights, nh, Math.Min(RowHeights.Length, needRows));
			for (var r = oldRows; r < needRows; r++)
				nh[r] = 20;
			if (oldRows == 0)
				for (var r = 0; r < needRows; r++) nh[r] = 20;
			RowHeights = nh;
		}
		Dense = true;
	}

	/// <summary>合并选区（含端点）。返回是否有改动。</summary>
	public bool MergeRange(int r0, int c0, int r1, int c1) {
		if (r0 > r1) { var t = r0; r0 = r1; r1 = t; }
		if (c0 > c1) { var t = c0; c0 = c1; c1 = t; }
		if (r0 < 0 || c0 < 0) return false;
		if (r1 == r0 && c1 == c0) return false;
		ensurecapacity(r1 + 1, c1 + 1);
		// 去掉与本区相交的旧合并
		if (Merges == null) Merges = new List<SheetMerge>();
		for (var i = Merges.Count - 1; i >= 0; i--) {
			var m = Merges[i];
			if (m == null || m.R1 < r0 || m.R0 > r1 || m.C1 < c0 || m.C0 > c1) continue;
			unmergeone(m);
			Merges.RemoveAt(i);
		}
		var origin = EnsureCell(r0, c0);
		// 其它格文字并入原点，并标记从属
		for (var r = r0; r <= r1; r++) {
			for (var c = c0; c <= c1; c++) {
				if (r == r0 && c == c0) continue;
				var cell = EnsureCell(r, c);
				if (string.IsNullOrEmpty(origin.Text) && !string.IsNullOrEmpty(cell.Text))
					origin.Text = cell.Text;
				cell.Text = "";
				cell.HiddenByMerge = true;
				cell.RowSpan = 1;
				cell.ColSpan = 1;
				// 写回数组，防止 EnsureCell 前是 SharedEmpty 引用未替换
				Cells[r][c] = cell;
			}
		}
		origin.HiddenByMerge = false;
		origin.RowSpan = r1 - r0 + 1;
		origin.ColSpan = c1 - c0 + 1;
		Cells[r0][c0] = origin;
		Merges.Add(new SheetMerge { R0 = r0, C0 = c0, R1 = r1, C1 = c1 });
		return true;
	}

	/// <summary>取消与选区相交的合并。返回是否有改动。</summary>
	public bool UnmergeRange(int r0, int c0, int r1, int c1) {
		if (Merges == null || Merges.Count == 0) return false;
		if (r0 > r1) { var t = r0; r0 = r1; r1 = t; }
		if (c0 > c1) { var t = c0; c0 = c1; c1 = t; }
		var changed = false;
		for (var i = Merges.Count - 1; i >= 0; i--) {
			var m = Merges[i];
			if (m == null || m.R1 < r0 || m.R0 > r1 || m.C1 < c0 || m.C0 > c1) continue;
			unmergeone(m);
			Merges.RemoveAt(i);
			changed = true;
		}
		return changed;
	}

	void unmergeone(SheetMerge m) {
		if (m == null) return;
		for (var r = m.R0; r <= m.R1; r++) {
			for (var c = m.C0; c <= m.C1; c++) {
				if (r < 0 || c < 0 || r >= Rows || c >= Cols) continue;
				var cell = EnsureCell(r, c);
				cell.HiddenByMerge = false;
				cell.RowSpan = 1;
				cell.ColSpan = 1;
			}
		}
	}
}
