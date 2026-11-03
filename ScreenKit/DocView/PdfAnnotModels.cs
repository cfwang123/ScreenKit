using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace ScreenKit.DocView;

/// <summary>PDF 标注类型（旁路 JSON，不写入 PDF 本体）。</summary>
enum PdfAnnotKind {
	Ink = 0,
	Highlight = 1,
	Text = 2,
	Note = 3,
	Rect = 4,
	Ellipse = 5,
	Line = 6,
	Arrow = 7,
}

/// <summary>页内点（pt，原点页左上、Y 向下）。</summary>
[DataContract]
sealed class PdfAnnotPt {
	[DataMember(Name = "x", Order = 1)]
	public double X;
	[DataMember(Name = "y", Order = 2)]
	public double Y;

	public PdfAnnotPt() { }
	public PdfAnnotPt(double x, double y) { X = x; Y = y; }
}

/// <summary>
/// 单条标注。坐标：页用户空间 pt，原点左上 Y 向下（与界面一致）。
/// 矢量路径用 Points；线段/箭头用 X,Y → X2,Y2；形状/文字用 X,Y,W,H。
/// </summary>
[DataContract]
sealed class PdfAnnotItem {
	[DataMember(Name = "id", Order = 1)]
	public string Id = Guid.NewGuid().ToString("N");

	/// <summary>0-based 页码。</summary>
	[DataMember(Name = "page", Order = 2)]
	public int Page;

	[DataMember(Name = "kind", Order = 3)]
	public string KindName = "ink";

	[DataMember(Name = "x", Order = 4)]
	public double X;
	[DataMember(Name = "y", Order = 5)]
	public double Y;
	[DataMember(Name = "w", Order = 6)]
	public double W = 40;
	[DataMember(Name = "h", Order = 7)]
	public double H = 24;

	/// <summary>线段/箭头终点。</summary>
	[DataMember(Name = "x2", Order = 8)]
	public double X2;
	[DataMember(Name = "y2", Order = 9)]
	public double Y2;

	[DataMember(Name = "color", Order = 10)]
	public string ColorHex = "#E53935";

	[DataMember(Name = "stroke", Order = 11)]
	public double Stroke = 1.6;

	[DataMember(Name = "opacity", Order = 12)]
	public double Opacity = 1.0;

	[DataMember(Name = "text", Order = 13)]
	public string Text = "";

	[DataMember(Name = "font", Order = 14)]
	public string FontName = "Microsoft YaHei";

	[DataMember(Name = "fontSize", Order = 15)]
	public double FontSize = 12;

	[DataMember(Name = "points", Order = 16)]
	public List<PdfAnnotPt> Points = new();

	/// <summary>成组 id；空表示未成组。</summary>
	[DataMember(Name = "groupId", Order = 17)]
	public string GroupId = "";

	/// <summary>文本：true=随内容自动变宽（直到页右缘换行）；用户手动调宽后为 false。</summary>
	[DataMember(Name = "autoWidth", Order = 18)]
	public bool AutoWidth;

	/// <summary>运行时选中态（不序列化）。</summary>
	public bool Selected;

	public PdfAnnotKind Kind {
		get => ParseKind(KindName);
		set => KindName = KindToName(value);
	}

	public MediaColor Color {
		get => ParseColor(ColorHex);
		set => ColorHex = FormatColor(value);
	}

	public PdfAnnotItem Clone(bool newId = true) {
		var c = new PdfAnnotItem {
			Id = newId ? Guid.NewGuid().ToString("N") : Id,
			Page = Page,
			KindName = KindName,
			X = X, Y = Y, W = W, H = H,
			X2 = X2, Y2 = Y2,
			ColorHex = ColorHex,
			Stroke = Stroke,
			Opacity = Opacity,
			Text = Text,
			FontName = FontName,
			FontSize = FontSize,
			GroupId = GroupId ?? "",
			AutoWidth = AutoWidth,
			Selected = false,
		};
		if (Points != null) {
			foreach (var p in Points)
				c.Points.Add(new PdfAnnotPt(p.X, p.Y));
		}
		return c;
	}

	/// <summary>按路径点重算包围盒（Ink/Highlight）。</summary>
	public void RecalcBoundsFromPoints() {
		if (Points == null || Points.Count == 0) return;
		var minX = double.MaxValue;
		var minY = double.MaxValue;
		var maxX = double.MinValue;
		var maxY = double.MinValue;
		foreach (var p in Points) {
			if (p.X < minX) minX = p.X;
			if (p.Y < minY) minY = p.Y;
			if (p.X > maxX) maxX = p.X;
			if (p.Y > maxY) maxY = p.Y;
		}
		var pad = Math.Max(2, Stroke);
		X = minX - pad;
		Y = minY - pad;
		W = Math.Max(4, maxX - minX + pad * 2);
		H = Math.Max(4, maxY - minY + pad * 2);
	}

	/// <summary>线段/箭头按端点重算包围盒。</summary>
	public void RecalcBoundsFromLine() {
		var minX = Math.Min(X, X2);
		var minY = Math.Min(Y, Y2);
		var maxX = Math.Max(X, X2);
		var maxY = Math.Max(Y, Y2);
		var pad = Math.Max(4, Stroke * 2);
		// 保留端点；包围盒单独用 W/H 时保持 X,Y 为起点
		// 命中用 min/max
		_ = minX; _ = minY; _ = maxX; _ = maxY; _ = pad;
		W = Math.Max(4, Math.Abs(X2 - X));
		H = Math.Max(4, Math.Abs(Y2 - Y));
	}

	public static string KindToName(PdfAnnotKind k) => k switch {
		PdfAnnotKind.Highlight => "highlight",
		PdfAnnotKind.Text => "text",
		PdfAnnotKind.Note => "note",
		PdfAnnotKind.Rect => "rect",
		PdfAnnotKind.Ellipse => "ellipse",
		PdfAnnotKind.Line => "line",
		PdfAnnotKind.Arrow => "arrow",
		_ => "ink",
	};

	public static PdfAnnotKind ParseKind(string s) {
		if (string.IsNullOrWhiteSpace(s)) return PdfAnnotKind.Ink;
		switch (s.Trim().ToLowerInvariant()) {
			case "highlight": case "hl": return PdfAnnotKind.Highlight;
			case "text": return PdfAnnotKind.Text;
			case "note": case "comment": return PdfAnnotKind.Note;
			case "rect": case "rectangle": return PdfAnnotKind.Rect;
			case "ellipse": case "oval": case "circle": return PdfAnnotKind.Ellipse;
			case "line": return PdfAnnotKind.Line;
			case "arrow": return PdfAnnotKind.Arrow;
			default: return PdfAnnotKind.Ink;
		}
	}

	public static MediaColor ParseColor(string hex) {
		if (string.IsNullOrWhiteSpace(hex)) return MediaColor.FromRgb(0xE5, 0x39, 0x35);
		var s = hex.Trim();
		if (s.StartsWith("#")) s = s.Substring(1);
		try {
			if (s.Length == 6) {
				var r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
				var g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
				var b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
				return MediaColor.FromRgb(r, g, b);
			}
			if (s.Length == 8) {
				var a = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
				var r = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
				var g = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
				var b = byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber);
				return MediaColor.FromArgb(a, r, g, b);
			}
		} catch { /* ignore */ }
		return MediaColor.FromRgb(0xE5, 0x39, 0x35);
	}

	public static string FormatColor(MediaColor c) =>
		c.A == 255
			? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
			: $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}

/// <summary>标注文档根（与 PDF 同目录的 JSON）。</summary>
[DataContract]
sealed class PdfAnnotFile {
	[DataMember(Name = "version", Order = 1)]
	public int Version = 1;

	[DataMember(Name = "app", Order = 2)]
	public string App = "ScreenKit";

	[DataMember(Name = "source", Order = 3)]
	public string Source = "";

	[DataMember(Name = "items", Order = 4)]
	public List<PdfAnnotItem> Items = new();
}

/// <summary>标注会话 + JSON 读写。</summary>
sealed class PdfAnnotDoc {
	public readonly List<PdfAnnotItem> Items = new();
	public bool Dirty;
	public string AnnotPath;

	public void Clear() {
		Items.Clear();
		Dirty = false;
		AnnotPath = null;
	}

	public PdfAnnotItem Find(string id) {
		if (string.IsNullOrEmpty(id)) return null;
		foreach (var it in Items)
			if (it.Id == id) return it;
		return null;
	}

	public void DeselectAll() {
		foreach (var it in Items) it.Selected = false;
	}

	public PdfAnnotItem SelectedItem {
		get {
			foreach (var it in Items)
				if (it.Selected) return it;
			return null;
		}
	}

	public List<PdfAnnotItem> SelectedItems {
		get {
			var list = new List<PdfAnnotItem>();
			foreach (var it in Items)
				if (it.Selected) list.Add(it);
			return list;
		}
	}

	public int SelectedCount {
		get {
			var n = 0;
			foreach (var it in Items)
				if (it.Selected) n++;
			return n;
		}
	}

	public IEnumerable<PdfAnnotItem> OnPage(int page) {
		foreach (var it in Items)
			if (it.Page == page) yield return it;
	}

	/// <summary>选中与 item 同组的全部（无组则仅自身）。</summary>
	public void SelectWithGroup(PdfAnnotItem item, bool additive = false) {
		if (item == null) return;
		if (!additive) DeselectAll();
		if (!string.IsNullOrEmpty(item.GroupId)) {
			foreach (var it in Items)
				if (it.GroupId == item.GroupId) it.Selected = true;
		} else {
			item.Selected = true;
		}
	}

	public static string PathForPdf(string pdfPath) {
		if (string.IsNullOrWhiteSpace(pdfPath)) return null;
		return pdfPath + ".annot.json";
	}

	public void LoadForPdf(string pdfPath) {
		Clear();
		AnnotPath = PathForPdf(pdfPath);
		if (string.IsNullOrEmpty(AnnotPath) || !File.Exists(AnnotPath)) return;
		try {
			using var fs = File.OpenRead(AnnotPath);
			var ser = new DataContractJsonSerializer(typeof(PdfAnnotFile));
			var data = ser.ReadObject(fs) as PdfAnnotFile;
			if (data?.Items == null) return;
			foreach (var it in data.Items) {
				if (it == null) continue;
				if (string.IsNullOrEmpty(it.Id))
					it.Id = Guid.NewGuid().ToString("N");
				if (it.Points == null) it.Points = new List<PdfAnnotPt>();
				Items.Add(it);
			}
			Dirty = false;
			DocViewLog.Info($"PdfAnnot.Load path={AnnotPath} items={Items.Count}");
		} catch (Exception ex) {
			DocViewLog.Error("PdfAnnot.Load", ex);
		}
	}

	public bool Save(string pdfPath = null) {
		try {
			if (!string.IsNullOrWhiteSpace(pdfPath))
				AnnotPath = PathForPdf(pdfPath);
			if (string.IsNullOrEmpty(AnnotPath)) return false;

			// 无标注且文件不存在：无需写空文件
			if (Items.Count == 0) {
				if (File.Exists(AnnotPath)) {
					try { File.Delete(AnnotPath); } catch { /* ignore */ }
				}
				Dirty = false;
				return true;
			}

			var data = new PdfAnnotFile {
				Version = 1,
				App = "ScreenKit",
				Source = Path.GetFileName(pdfPath ?? AnnotPath.Replace(".annot.json", "")),
				Items = new List<PdfAnnotItem>(),
			};
			foreach (var it in Items)
				data.Items.Add(it.Clone(newId: false));

			var dir = Path.GetDirectoryName(AnnotPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			var tmp = AnnotPath + ".tmp";
			using (var fs = File.Create(tmp)) {
				using var writer = JsonReaderWriterFactory.CreateJsonWriter(
					fs, Encoding.UTF8, ownsStream: false, indent: true);
				var ser = new DataContractJsonSerializer(typeof(PdfAnnotFile));
				ser.WriteObject(writer, data);
				writer.Flush();
			}
			if (File.Exists(AnnotPath)) File.Delete(AnnotPath);
			File.Move(tmp, AnnotPath);
			Dirty = false;
			DocViewLog.Info($"PdfAnnot.Save path={AnnotPath} items={Items.Count}");
			return true;
		} catch (Exception ex) {
			DocViewLog.Error("PdfAnnot.Save", ex);
			return false;
		}
	}
}
