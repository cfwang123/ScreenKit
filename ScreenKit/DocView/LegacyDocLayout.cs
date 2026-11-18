using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NPOI.HWPF;
using NPOI.HWPF.UserModel;
using WpfParagraph = System.Windows.Documents.Paragraph;
using HwpfRange = NPOI.HWPF.UserModel.Range;

namespace ScreenKit.DocView;

/// <summary>
/// .doc 的图文混排：Word/WPS 把图片和文本框都存成锚定图形（Escher），正文只剩 U+0008 占位符，
/// 只按段落读会是一片空白。这里按锚点顺序取图形，twips 坐标换成行/列，再渲染成每页一张图内嵌。
/// 用整页位图而不是表格：XamlPackage 克隆会丢非图片的 UIElement，位图能完整带到每页的 RichTextBox。
/// </summary>
static class LegacyDocLayout {
	/// <summary>1 磅 = 20 twips；1 twip = 96/1440 DIP。</summary>
	const double TWIP2DIP = 96.0 / 1440.0;
	/// <summary>渲染倍率：放大后仍清晰。</summary>
	const double RENDER_SCALE = 2.0;
	/// <summary>同一行图形的顶边容差（twips）。</summary>
	const int ROW_TOL = 900;
	/// <summary>标签与图片的配对上限（twips）。</summary>
	const int LABEL_MAXDY = 2400;
	const int LABEL_MAXDX = 2600;

	sealed class DocShape {
		public int Page;
		public int Left, Top, Right, Bottom;
		public byte[] Pic;
		public string Text = "";
		public bool IsPic => Pic != null && Pic.Length > 0;
		public int CenterX => (Left + Right) / 2;
	}

	sealed class DocRow {
		public List<DocShape> Cells = new();
	}

	/// <summary>正文除了占位符没有别的字（正文有字时说明不是纯图形排版）。</summary>
	public static bool BodyEmpty(HWPFDocument doc) {
		try {
			var range = doc?.GetRange();
			if (range == null) return true;
			var text = range.Text;
			if (string.IsNullOrEmpty(text)) return true;
			for (var i = 0; i < text.Length; i++) {
				var ch = text[i];
				if (ch == '\b' || ch == '\r' || ch == '\f' || ch == ' ' || ch == '\t' || ch == '\u0007') continue;
				return false;
			}
			return true;
		} catch {
			return false;
		}
	}

	/// <summary>
	/// 给每页追加一个内嵌图块（按内容区尺寸出图）。没有锚定图形时返回 false。
	/// </summary>
	public static bool TryAppend(FlowDocument flow, HWPFDocument doc, double contentW, double contentH, double fontSize) {
		if (flow == null || doc == null) return false;
		if (contentW < 80 || contentH < 80) return false;
		List<DocShape> items;
		try {
			items = collect(doc);
		} catch (Exception ex) {
			DocViewLog.Warn($"doc 图形读取失败: {ex.Message}");
			return false;
		}
		if (items.Count == 0) return false;

		var pages = layout(items, contentW, contentH, fontSize);
		var added = 0;
		foreach (var canvas in pages) {
			var bmp = render(canvas, contentW, contentH);
			if (bmp == null) continue;
			var para = new WpfParagraph(new InlineUIContainer(new Image {
				Source = bmp,
				Width = contentW,
				Height = contentH,
				Stretch = Stretch.Fill,
				SnapsToDevicePixels = true,
			})) {
				Margin = new Thickness(0),
				LineHeight = 1,
			};
			if (added > 0) para.BreakPageBefore = true;
			flow.Blocks.Add(para);
			added++;
		}
		DocViewLog.Info($"doc 图形版式 pages={added} shapes={items.Count}");
		return added > 0;
	}

	/// <summary>每页一张 Canvas：图片按行/列均分，标签居中放在图上。</summary>
	static List<Canvas> layout(List<DocShape> items, double contentW, double contentH, double fontSize) {
		var pages = new List<Canvas>();
		if (fontSize <= 0) fontSize = 14;
		// 列数取整篇最多的一行，保证各页图大小一致（最后一页常常只有两三个）
		var groups = items.GroupBy(x => x.Page).OrderBy(x => x.Key)
			.Select(g => grouprolls(g.ToList()))
			.Where(rows => rows.Count > 0)
			.ToList();
		var ncol = groups.Count == 0 ? 1 : groups.Max(rows => rows.Max(r => r.Cells.Count));
		var colW = contentW / Math.Max(1, ncol);

		foreach (var rows in groups) {
			// 每行高度 = 标签 + 图，先按原图比例算出，超高再整体缩
			var rowh = new List<double>();
			foreach (var r in rows) {
				var hmax = 0.0;
				foreach (var c in r.Cells) {
					var ih = c.IsPic ? imgheight(c, colW) : 0;
					hmax = Math.Max(hmax, ih + labelheight(c, colW, fontSize));
				}
				rowh.Add(Math.Max(hmax, fontSize * 1.8));
			}
			var totalH = rowh.Sum();
			var scale = totalH > contentH ? contentH / totalH : 1.0;

			var canvas = new Canvas {
				Width = contentW,
				Height = contentH,
				ClipToBounds = true,
				Background = Brushes.Transparent,
			};
			var y = 0.0;
			for (var ri = 0; ri < rows.Count; ri++) {
				var row = rows[ri];
				for (var ci = 0; ci < row.Cells.Count; ci++) {
					var c = row.Cells[ci];
					var cx = ci * colW + colW / 2;
					var cy = y;
					if (!string.IsNullOrEmpty(c.Text)) {
						var label = makelabel(c, colW, fontSize, scale);
						label.Measure(new Size(labelwidth(colW), double.PositiveInfinity));
						Canvas.SetLeft(label, cx - labelwidth(colW) / 2);
						Canvas.SetTop(label, cy);
						canvas.Children.Add(label);
						cy += label.DesiredSize.Height + 4 * scale;
					}
					if (c.IsPic) {
						var img = tryimage(c, colW);
						if (img != null) {
							Canvas.SetLeft(img, cx - img.Width / 2);
							Canvas.SetTop(img, cy);
							canvas.Children.Add(img);
						}
					}
				}
				y += rowh[ri] * scale;
			}
			pages.Add(canvas);
		}
		return pages;
	}

	/// <summary>图片按比例缩放，宽度不超过格宽。</summary>
	static double imgheight(DocShape c, double colW) {
		var w = Math.Max(1, c.Right - c.Left);
		var h = Math.Max(1, c.Bottom - c.Top);
		var iw = Math.Min(colW - 16, w * TWIP2DIP * 1.1);
		if (iw < 24) iw = 24;
		return iw * h / w;
	}

	/// <summary>标签宽度：不超过一格，避免相邻标签压在一起。</summary>
	static double labelwidth(double colW) => Math.Max(40, colW - 2);

	/// <summary>标签占用的高度（含与图之间的空隙）。</summary>
	static double labelheight(DocShape c, double colW, double fontSize) {
		if (string.IsNullOrEmpty(c.Text)) return 0;
		var tb = makelabel(c, colW, fontSize, 1);
		tb.Measure(new Size(labelwidth(colW), double.PositiveInfinity));
		return tb.DesiredSize.Height + 4;
	}

	static TextBlock makelabel(DocShape c, double colW, double fontSize, double scale) => new() {
		Text = c.Text,
		FontSize = Math.Max(6, fontSize * 0.85 * scale),
		TextAlignment = TextAlignment.Center,
		TextWrapping = TextWrapping.Wrap,
		Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
		Width = labelwidth(colW),
	};

	static List<DocRow> grouprolls(List<DocShape> items) {
		var rows = new List<DocRow>();
		foreach (var p in items.Where(x => x.IsPic).OrderBy(x => x.Top).ThenBy(x => x.Left)) {
			var row = rows.FirstOrDefault(r => Math.Abs(r.Cells[0].Top - p.Top) <= ROW_TOL);
			if (row == null) {
				row = new DocRow();
				rows.Add(row);
			}
			row.Cells.Add(p);
		}
		foreach (var t in items.Where(x => !x.IsPic)) {
			DocShape best = null;
			var bestscore = int.MaxValue;
			foreach (var r in rows)
			foreach (var p in r.Cells) {
				var dy = p.Top - t.Top;
				var dx = Math.Abs(p.CenterX - t.CenterX);
				if (dy < -200 || dy > LABEL_MAXDY || dx > LABEL_MAXDX) continue;
				var score = dx + Math.Abs(dy) * 3;
				if (score < bestscore) { bestscore = score; best = p; }
			}
			if (best == null) {
				var row = new DocRow();
				row.Cells.Add(t);
				rows.Add(row);
				continue;
			}
			best.Text = best.Text.Length > 0 ? best.Text + "\n" + t.Text : t.Text;
			best.Left = Math.Min(best.Left, t.Left);
		}
		foreach (var r in rows)
			r.Cells.Sort((a, b) => a.Left.CompareTo(b.Left));
		rows.Sort((a, b) => a.Cells.Min(x => x.Top).CompareTo(b.Cells.Min(x => x.Top)));
		return rows;
	}

	static BitmapSource render(Canvas canvas, double w, double h) {
		try {
			canvas.Measure(new Size(w, h));
			canvas.Arrange(new Rect(0, 0, w, h));
			canvas.UpdateLayout();
			var rtb = new RenderTargetBitmap(
				(int)Math.Ceiling(w * RENDER_SCALE), (int)Math.Ceiling(h * RENDER_SCALE),
				96 * RENDER_SCALE, 96 * RENDER_SCALE, PixelFormats.Pbgra32);
			rtb.Render(canvas);
			// 转 PNG 再读回：内嵌图块要在 XamlPackage 克隆后仍然可见
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(rtb));
			using var ms = new MemoryStream();
			enc.Save(ms);
			ms.Position = 0;
			var bmp = new BitmapImage();
			bmp.BeginInit();
			bmp.StreamSource = ms;
			bmp.CacheOption = BitmapCacheOption.OnLoad;
			bmp.EndInit();
			bmp.Freeze();
			return bmp;
		} catch (Exception ex) {
			DocViewLog.Warn($"doc 图形渲染失败: {ex.Message}");
			return null;
		}
	}

	/// <summary>按锚点顺序取图形，并把文本框正文按顺序配给没有图片数据的图形。</summary>
	static List<DocShape> collect(HWPFDocument doc) {
		var items = new List<DocShape>();
		var main = doc.GetRange();
		if (main == null) return items;
		var od = doc.GetOfficeDrawingsMain();
		var shapes = od?.GetOfficeDrawings();
		if (shapes == null || shapes.Count == 0) return items;

		var known = new HashSet<int>();
		foreach (var d in shapes) {
			if (d == null) continue;
			known.Add(d.GetShapeId());
		}

		var story = textboxtext(doc);
		var si = 0;
		var text = main.Text;
		var page = 0;
		for (var off = main.StartOffset; off < main.EndOffset; off++) {
			var ch = text[off - main.StartOffset];
			// 分页符：其后的图形锚在下一页
			if (ch == '\f') { page++; continue; }
			if (ch != '\b') continue;
			OfficeDrawing d;
			try { d = od.GetOfficeDrawingAt(off); }
			catch { continue; }
			if (d == null || !known.Contains(d.GetShapeId())) continue;
			var it = new DocShape {
				Page = page,
				Left = d.GetRectangleLeft(), Top = d.GetRectangleTop(),
				Right = d.GetRectangleRight(), Bottom = d.GetRectangleBottom(),
				Pic = picbytes(d),
			};
			if (!it.IsPic) {
				if (si < story.Count) it.Text = story[si];
				si++;
			}
			items.Add(it);
		}
		return items;
	}

	/// <summary>文本框正文：只保留有字的段落，按段落顺序。</summary>
	static List<string> textboxtext(HWPFDocument doc) {
		var list = new List<string>();
		HwpfRange range;
		try { range = doc.GetMainTextboxRange(); }
		catch { return list; }
		if (range == null) return list;
		var n = range.NumParagraphs;
		for (var i = 0; i < n; i++) {
			string t;
			try { t = range.GetParagraph(i)?.Text ?? ""; }
			catch { continue; }
			t = (t ?? "").Replace("\r", "").Trim();
			if (t.Length > 0) list.Add(t);
		}
		return list;
	}

	static byte[] picbytes(OfficeDrawing d) {
		try {
			var b = d.GetPictureData();
			return b != null && b.Length > 0 ? b : null;
		} catch {
			return null;
		}
	}

	static Image tryimage(DocShape c, double colW) {
		try {
			var bmp = new BitmapImage();
			bmp.BeginInit();
			bmp.StreamSource = new MemoryStream(c.Pic);
			bmp.CacheOption = BitmapCacheOption.OnLoad;
			bmp.EndInit();
			bmp.Freeze();
			var w = Math.Max(1, c.Right - c.Left);
			var h = Math.Max(1, c.Bottom - c.Top);
			var iw = Math.Min(colW - 16, w * TWIP2DIP * 1.1);
			if (iw < 24) iw = 24;
			return new Image {
				Source = bmp,
				Width = iw,
				Height = iw * h / w,
				Stretch = Stretch.Fill,
				SnapsToDevicePixels = true,
			};
		} catch {
			return null;
		}
	}
}

