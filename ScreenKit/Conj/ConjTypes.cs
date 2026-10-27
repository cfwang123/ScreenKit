using System.Collections.Generic;

namespace ScreenKit;

/// <summary>一条词形：标签、变形结果、分组名。分组为空表示基础形。</summary>
sealed class ConjRow {
	public string L;
	public string V;
	public string G;

	public ConjRow() { }

	public ConjRow(string l, string v, string g = "") {
		L = l;
		V = v;
		G = g;
	}
}

/// <summary>日/韩/英动词变形的结果，和网页端 build() 的返回值对应。</summary>
sealed class ConjResult {
	public bool Ok;
	public string Err = "";
	public string Head = "";
	public string Name = "";
	public string Note = "";
	public string Irr = "";
	/// <summary>v 动词、a 形容词、n 名词。网页据此决定要不要显示宾语。</summary>
	public string Kind = "";
	public List<ConjRow> Rows = new();
}
