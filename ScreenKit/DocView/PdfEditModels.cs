using System;
using System.Collections.Generic;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace ScreenKit.DocView;

/// <summary>PDF 编辑对象类型。</summary>
enum PdfEditKind {
	Text = 0,
	Image = 1,
	/// <summary>白底遮罩（用于“盖住”原文再写新字）。</summary>
	Whiteout = 2,
}

/// <summary>
/// 页内编辑对象。坐标：页用户空间点（pt），原点为页左上角、Y 向下（与界面一致）。
/// 保存时再转换为 PDF 左下原点。
/// </summary>
sealed class PdfEditItem {
	public Guid Id = Guid.NewGuid();
	public int Page;
	public PdfEditKind Kind = PdfEditKind.Text;
	/// <summary>距页左 pt。</summary>
	public double X;
	/// <summary>距页顶 pt。</summary>
	public double Y;
	public double W = 120;
	public double H = 24;
	public string Text = "";
	public string FontName = "Microsoft YaHei";
	public double FontSizePt = 12;
	public bool Bold;
	public bool Italic;
	public MediaColor ForeColor = MediaColor.FromRgb(0x11, 0x18, 0x27);
	public MediaColor? BackColor;
	/// <summary>PNG 字节（图片对象）。</summary>
	public byte[] ImagePng;
	public bool Selected;

	public PdfEditItem Clone() {
		return new PdfEditItem {
			Id = Id,
			Page = Page,
			Kind = Kind,
			X = X, Y = Y, W = W, H = H,
			Text = Text,
			FontName = FontName,
			FontSizePt = FontSizePt,
			Bold = Bold,
			Italic = Italic,
			ForeColor = ForeColor,
			BackColor = BackColor,
			ImagePng = ImagePng,
			Selected = Selected,
		};
	}
}

/// <summary>整文档编辑会话数据。</summary>
sealed class PdfEditDoc {
	public readonly List<PdfEditItem> Items = new();
	public bool Dirty;

	public void Clear() {
		Items.Clear();
		Dirty = false;
	}

	public PdfEditItem Find(Guid id) {
		foreach (var it in Items)
			if (it.Id == id) return it;
		return null;
	}

	public void DeselectAll() {
		foreach (var it in Items) it.Selected = false;
	}

	public PdfEditItem SelectedItem {
		get {
			foreach (var it in Items)
				if (it.Selected) return it;
			return null;
		}
	}

	public IEnumerable<PdfEditItem> OnPage(int page) {
		foreach (var it in Items)
			if (it.Page == page) yield return it;
	}
}
