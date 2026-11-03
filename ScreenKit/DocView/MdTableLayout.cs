using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ScreenKit.DocView;

/// <summary>
/// Markdown 表格列宽（对齐 nvim mdview/render.lua）：
/// 1) 单元格 need：英文 1、中文/CJK 2（strdisplaywidth）；列取所有格 max
/// 2) 含图片：图片 need = tableW / 列数（至少 1/ncol）
/// 3) need 总和 ≤ 可用 → 严格按 need（短表不撑满窗口，尽量不换行）
/// 4) 否则短列优先按 need 钉死（need ≤ 当前均分份额）；剩余列按 need 比例分剩余宽
/// </summary>
static class MdTableLayout {
	static readonly FontFamily UiFont = new FontFamily("Segoe UI, 微软雅黑, Microsoft YaHei UI, sans-serif");

	/// <summary>半角字符约合 DIP（与预览 14px 正文字号配套）。</summary>
	public const double DEFAULT_UNIT_DIP = 7.7; // ~14 * 0.55

	/// <summary>类似 vim strdisplaywidth / mdview str_width：半角 1、全角/CJK 2、Tab 4。</summary>
	public static int StrDisplayWidth(string s) {
		if (string.IsNullOrEmpty(s)) return 0;
		var w = 0;
		for (var i = 0; i < s.Length; i++) {
			var ch = s[i];
			if (ch == '\t') {
				w += 4;
				continue;
			}
			if (char.IsHighSurrogate(ch)) {
				w += 2;
				if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
				continue;
			}
			if (ch < 0x80) {
				if (!char.IsControl(ch)) w += 1;
				continue;
			}
			if (IsWide(ch)) w += 2;
			else w += 1;
		}
		return w;
	}

	static bool IsWide(char ch) {
		if (ch >= 0x1100 && ch <= 0x115F) return true;
		if (ch >= 0x2E80 && ch <= 0xA4CF) return true;
		if (ch >= 0xAC00 && ch <= 0xD7A3) return true;
		if (ch >= 0xF900 && ch <= 0xFAFF) return true;
		if (ch >= 0xFE10 && ch <= 0xFE6F) return true;
		if (ch >= 0xFF00 && ch <= 0xFF60) return true;
		if (ch >= 0xFFE0 && ch <= 0xFFE6) return true;
		if (ch >= 0x3000 && ch <= 0x303F) return true;
		return false;
	}

	/// <summary>粗去 ** * ` []() 等，便于量「可见文字」宽。</summary>
	public static string StripInlineMarkers(string text) {
		if (string.IsNullOrEmpty(text)) return "";
		var s = text;
		s = System.Text.RegularExpressions.Regex.Replace(s, @"!\[([^\]]*)\]\([^)]*\)", "$1");
		s = System.Text.RegularExpressions.Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");
		s = s.Replace("**", "").Replace("__", "");
		s = s.Replace("~~", "").Replace("==", "");
		s = s.Replace("`", "");
		s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<!\w)[*_](?!\w)", "");
		return s;
	}

	/// <summary>用预览字体量单行文本像素宽（不含 padding）。</summary>
	public static double MeasureTextDip(string text, double fontSize, bool bold = false) {
		var plain = StripInlineMarkers(text ?? "");
		if (plain.Length == 0) return 8;
		try {
			var weight = bold ? FontWeights.SemiBold : FontWeights.Normal;
			var typeface = new Typeface(UiFont, FontStyles.Normal, weight, FontStretches.Normal);
#pragma warning disable CS0618 // net48 旧 FormattedText 重载
			var ft = new FormattedText(
				plain,
				CultureInfo.CurrentCulture,
				FlowDirection.LeftToRight,
				typeface,
				fontSize > 1 ? fontSize : 14,
				Brushes.Black);
#pragma warning restore CS0618
			return Math.Max(8, ft.WidthIncludingTrailingWhitespace);
		} catch {
			// 回退：显示列 × 较宽系数（原 0.55 偏小易换行）
			return Math.Max(8, StrDisplayWidth(plain) * Math.Max(4.0, fontSize * 0.72));
		}
	}

	/// <summary>
	/// 单元格内容 need（DIP，FormattedText 备用路径）：文字像素 + padding；
	/// 含图时至少 tableAvail/ncol。
	/// </summary>
	public static double CellContentNeedDip(string text, double tableAvailDip, int ncol, double fontSize,
		bool bold = false, double cellPadH = 16) {
		text = text ?? "";
		var hasImg = text.IndexOf("![", StringComparison.Ordinal) >= 0
			|| text.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0;
		var w = MeasureTextDip(text, fontSize, bold) + cellPadH;
		if (hasImg) {
			// mdview：图列至少 table_w/ncol
			var share = Math.Max(40, tableAvailDip / Math.Max(1, ncol));
			w = Math.Max(w, share);
		}
		return Math.Max(28, w);
	}

	/// <summary>各列内容 need（DIP）。header 行按粗体量宽。</summary>
	public static double[] ContentNeedsDip(IList<string[]> rows, int ncol, double tableAvailDip, double fontSize,
		double cellPadH = 16) {
		var need = new double[ncol];
		for (var c = 0; c < ncol; c++) need[c] = 28;
		if (rows == null) return need;
		for (var ri = 0; ri < rows.Count; ri++) {
			var row = rows[ri];
			if (row == null) continue;
			var bold = ri == 0;
			for (var c = 0; c < ncol; c++) {
				var cell = c < row.Length ? row[c] : "";
				need[c] = Math.Max(need[c], CellContentNeedDip(cell, tableAvailDip, ncol, fontSize, bold, cellPadH));
			}
		}
		return need;
	}

	/// <summary>
	/// mdview cell_content_need：显示列单位。
	/// 文字 = StrDisplayWidth(去标记)；含图 = max(文字, tableW/ncol)。
	/// </summary>
	public static int CellContentNeed(string text, int tableW, int ncol) {
		text = text ?? "";
		var hasImg = text.IndexOf("![", StringComparison.Ordinal) >= 0
			|| text.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0;
		var plain = StripInlineMarkers(text);
		var w = Math.Max(1, StrDisplayWidth(plain));
		if (hasImg) {
			// 图片宽度按 1/列数 预留（mdview share = floor(table_w/ncol)）
			var share = Math.Max(4, tableW / Math.Max(1, ncol));
			w = Math.Max(w, share);
		}
		return w;
	}

	/// <summary>各列 need = 该列所有单元格 max（mdview table_content_needs）。</summary>
	public static int[] ContentNeeds(IList<string[]> rows, int ncol, int tableW) {
		var need = new int[ncol];
		for (var c = 0; c < ncol; c++) need[c] = 1;
		if (rows == null) return need;
		foreach (var row in rows) {
			if (row == null) continue;
			for (var c = 0; c < ncol; c++) {
				var cell = c < row.Length ? row[c] : "";
				need[c] = Math.Max(need[c], CellContentNeed(cell, tableW, ncol));
			}
		}
		return need;
	}

	/// <summary>
	/// 预览用：显示列 need → DIP 列宽。
	/// need 总和 ≤ 可用 → 按内容宽（表不撑满窗口）；否则短列钉死、长列分剩余铺满。
	/// 用 FormattedText 量宽，避免 E2026108 等短列被单位估算偏窄后挤断。
	/// </summary>
	public static double[] AllocateColumnsDip(IList<string[]> rows, int ncol, double pageWidthDip,
		double unitDip = DEFAULT_UNIT_DIP, double pagePadH = 56, double cellPadH = 16) {
		if (ncol <= 0) return Array.Empty<double>();
		if (unitDip < 4) unitDip = DEFAULT_UNIT_DIP;
		var availDip = Math.Max(ncol * 28.0, pageWidthDip - pagePadH);
		var needDip = ContentNeedsDip(rows, ncol, availDip, fontSize: 14, cellPadH);
		// 边框/亚像素余量，短列钉死后仍够一字不拆
		for (var c = 0; c < needDip.Length; c++)
			needDip[c] = Math.Max(28, needDip[c] + 4);
		return AllocateFillNeedDip(needDip, availDip);
	}

	/// <summary>
	/// 列是否适合 nowrap：该列所有格去标记后无空白，且显示宽度 ≤ 24（项目号等）。
	/// </summary>
	public static bool[] ShortNoWrapColumns(IList<string[]> rows, int ncol, int maxDisp = 24) {
		var nowrap = new bool[ncol];
		for (var c = 0; c < ncol; c++) nowrap[c] = true;
		if (rows == null || ncol <= 0) return nowrap;
		for (var c = 0; c < ncol; c++) {
			var maxW = 0;
			foreach (var row in rows) {
				if (row == null) continue;
				var cell = c < row.Length ? row[c] : "";
				var plain = StripInlineMarkers(cell ?? "");
				if (plain.Length == 0) continue;
				for (var i = 0; i < plain.Length; i++) {
					if (char.IsWhiteSpace(plain[i])) {
						nowrap[c] = false;
						break;
					}
				}
				if (!nowrap[c]) break;
				maxW = Math.Max(maxW, StrDisplayWidth(plain));
			}
			if (nowrap[c] && maxW > maxDisp)
				nowrap[c] = false;
		}
		return nowrap;
	}

	/// <summary>
	/// need 总和 ≤ avail → 原样返回（表宽=内容宽）；超出则短列钉死、长列分剩余铺满 avail。
	/// </summary>
	public static double[] AllocateFillNeedDip(double[] needDip, double availDip) {
		var ncol = needDip?.Length ?? 0;
		if (ncol == 0) return Array.Empty<double>();
		availDip = Math.Max(ncol * 28.0, availDip);

		var idealDip = new double[ncol];
		double sumIdeal = 0;
		for (var c = 0; c < ncol; c++) {
			idealDip[c] = Math.Max(28, needDip[c]);
			sumIdeal += idealDip[c];
		}

		// 放得下：不撑满窗口，表宽=内容总宽
		if (sumIdeal <= availDip + 0.5) {
			var copy = new double[ncol];
			Array.Copy(idealDip, copy, ncol);
			return copy;
		}

		// 压缩：优先保住短列
		var order = new int[ncol];
		for (var c = 0; c < ncol; c++) order[c] = c;
		Array.Sort(order, (a, b) => idealDip[a].CompareTo(idealDip[b]));
		var colW = new double[ncol];
		var pinned = new bool[ncol];
		var remain = availDip;
		var left = ncol;
		foreach (var c in order) {
			var n = idealDip[c];
			var fair = remain / Math.Max(1, left);
			if (n <= fair + 0.01) {
				colW[c] = n;
				pinned[c] = true;
				remain -= n;
				left--;
			}
		}
		var flex = new List<int>();
		double flexNeed = 0;
		for (var c = 0; c < ncol; c++) {
			if (!pinned[c]) {
				flex.Add(c);
				flexNeed += idealDip[c];
			}
		}
		if (flex.Count == 0) {
			if (remain > 0.5) colW[ncol - 1] += remain;
			return colW;
		}
		remain = Math.Max(flex.Count * 28.0, remain);
		double used = 0;
		for (var i = 0; i < flex.Count; i++) {
			var c = flex[i];
			double w;
			if (i == flex.Count - 1)
				w = Math.Max(28, remain - used);
			else {
				w = Math.Max(28, Math.Floor(remain * idealDip[c] / Math.Max(1, flexNeed)));
				used += w;
			}
			colW[c] = w;
		}
		double total = 0;
		for (var c = 0; c < ncol; c++)
			total += colW[c] > 0 ? colW[c] : 28;
		if (Math.Abs(total - availDip) > 0.5) {
			var last = flex[flex.Count - 1];
			colW[last] = Math.Max(28, colW[last] + (availDip - total));
		}
		return colW;
	}

	/// <summary>
	/// 总宽固定为 availDip：短列按 need 钉死（不换行），剩余宽度给长列按 need 比例。
	/// </summary>
	public static double[] AllocateFillDip(int[] need, double availDip, double unitDip = DEFAULT_UNIT_DIP,
		double cellPadH = 16) {
		var ncol = need?.Length ?? 0;
		if (ncol == 0) return Array.Empty<double>();
		if (unitDip < 4) unitDip = DEFAULT_UNIT_DIP;
		availDip = Math.Max(ncol * 28.0, availDip);

		var idealDip = new double[ncol];
		for (var c = 0; c < ncol; c++) {
			var u = Math.Max(1, need != null && c < need.Length ? need[c] : 1);
			idealDip[c] = Math.Max(28, u * unitDip + cellPadH);
		}
		return AllocateFillNeedDip(idealDip, availDip);
	}

	/// <summary>mdview allocate：整数显示列单位（minCol=1）。</summary>
	public static int[] Allocate(int[] need, int avail) {
		var d = AllocateDip(ToDouble(need), avail, minCol: 1);
		var r = new int[d.Length];
		for (var i = 0; i < d.Length; i++)
			r[i] = Math.Max(1, (int)Math.Round(d[i]));
		// 修正舍入总和（仅压缩场景）
		var sumNeed = 0;
		if (need != null)
			for (var i = 0; i < need.Length; i++) sumNeed += Math.Max(1, need[i]);
		if (sumNeed > avail && r.Length > 0) {
			var sum = 0;
			for (var i = 0; i < r.Length; i++) sum += r[i];
			if (sum != avail)
				r[r.Length - 1] = Math.Max(1, r[r.Length - 1] + (avail - sum));
		}
		return r;
	}

	static double[] ToDouble(int[] a) {
		if (a == null) return Array.Empty<double>();
		var d = new double[a.Length];
		for (var i = 0; i < a.Length; i++) d[i] = a[i];
		return d;
	}

	/// <summary>
	/// mdview allocate_table_widths（像素/任意单位浮点版）。
	/// need 总和 ≤ avail → 按内容；否则短列优先、长列分剩余。
	/// </summary>
	public static double[] AllocateDip(double[] need, double avail, double minCol = 8) {
		var ncol = need?.Length ?? 0;
		if (ncol == 0) return Array.Empty<double>();
		if (minCol < 1) minCol = 1;
		avail = Math.Max(ncol * minCol, avail);

		var ideal = new double[ncol];
		double sumNeed = 0;
		for (var c = 0; c < ncol; c++) {
			ideal[c] = Math.Max(minCol, need[c]);
			sumNeed += ideal[c];
		}

		// 全部放得下：按内容实际需要，尽量避免换行
		if (sumNeed <= avail + 0.5) {
			var copy = new double[ncol];
			Array.Copy(ideal, copy, ncol);
			return copy;
		}

		var order = new int[ncol];
		for (var c = 0; c < ncol; c++) order[c] = c;
		Array.Sort(order, (a, b) => ideal[a].CompareTo(ideal[b]));

		var colW = new double[ncol];
		var assigned = new bool[ncol];
		var remain = avail;
		var left = ncol;

		foreach (var c in order) {
			var n = ideal[c];
			var fair = remain / Math.Max(1, left);
			if (n <= fair + 0.01) {
				colW[c] = n;
				assigned[c] = true;
				remain -= n;
				left--;
			}
		}

		var flexIdx = new List<int>();
		double flexNeedSum = 0;
		for (var c = 0; c < ncol; c++) {
			if (!assigned[c]) {
				flexIdx.Add(c);
				flexNeedSum += ideal[c];
			}
		}

		if (flexIdx.Count == 0) {
			if (remain > 0.5)
				colW[ncol - 1] += remain;
			return colW;
		}

		remain = Math.Max(flexIdx.Count * minCol, remain);
		double usedFlex = 0;
		for (var i = 0; i < flexIdx.Count; i++) {
			var c = flexIdx[i];
			var n = ideal[c];
			double w;
			if (i == flexIdx.Count - 1)
				w = Math.Max(minCol, remain - usedFlex);
			else {
				w = Math.Max(minCol, Math.Floor(remain * n / Math.Max(1, flexNeedSum)));
				usedFlex += w;
			}
			colW[c] = w;
		}

		double total = 0;
		for (var c = 0; c < ncol; c++)
			total += colW[c] > 0 ? colW[c] : minCol;

		if (total < avail - 0.5) {
			var last = flexIdx[flexIdx.Count - 1];
			colW[last] += avail - total;
		} else if (total > avail + 0.5) {
			var over = total - avail;
			for (var i = flexIdx.Count - 1; i >= 0 && over > 0.5; i--) {
				var c = flexIdx[i];
				var cut = Math.Min(over, Math.Max(0, colW[c] - minCol));
				colW[c] -= cut;
				over -= cut;
			}
		}
		return colW;
	}

	/// <summary>页面可用宽（扣除文档左右边距）。</summary>
	public static double AvailableDip(double pageWidthDip, double pagePadH = 56) {
		return Math.Max(120, pageWidthDip - pagePadH);
	}

	// —— 旧 API 兼容自检 ——
	public static double UnitsToDip(int units, double fontSize, double cellPadH = 16) {
		var unit = Math.Max(4.0, fontSize * 0.72);
		return Math.Max(24, units * unit + cellPadH);
	}

	public static int AvailableUnits(double pageWidthDip, int ncol, double fontSize, double pagePadH = 56) {
		var unit = Math.Max(4.0, fontSize * 0.72);
		var contentDip = Math.Max(ncol * unit, pageWidthDip - pagePadH);
		var borderUnits = ncol + 1;
		var avail = (int)Math.Floor(contentDip / unit) - borderUnits;
		return Math.Max(ncol, avail);
	}
}
