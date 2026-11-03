using System.Collections.Generic;

namespace ScreenKit.DocView;

sealed class PdfOutlineNode {
	public string Title;
	public int PageIndex; // 0-based；-1 表示无目标页
	/// <summary>书签目标点是否带 Y（PDF 用户空间，原点左下、Y 向上，单位 pt）。</summary>
	public bool HasDestY;
	/// <summary>目标点 Y（pt，左下原点）。有值时跳转到页内该高度并置顶。</summary>
	public float DestY;
	/// <summary>距页顶比例 0..1（由 DestY 换算）；无 Y 时为 0（页顶）。</summary>
	public double TopFrac;
	public List<PdfOutlineNode> Children = new();
}
