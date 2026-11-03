using System;
using System.Collections.Generic;

namespace ScreenKit.DocView;

/// <summary>
/// 移植层用的界面文案。原 DocviewWPF 用 Loc 表支持中英日韩，
/// 这里只做一件事：把键映射到 ScreenKit 的 Loc 文案，缺键回落到原中文。
/// </summary>
static class DocViewLoc {
	static readonly Dictionary<string, string> Zh = new() {
		["cancel"] = "取消",
		["ok"] = "确定",
		["code_collapse"] = "收起",
		["code_show_all"] = "显示全部",
		["tip_annot_color"] = "颜色",
	};

	public static string T(string key) {
		if (string.IsNullOrEmpty(key)) return "";
		try {
			var s = ScreenKit.Loc.T("docview." + key);
			if (s != null && s != "docview." + key)
				return s;
		}
		catch { }
		return Zh.TryGetValue(key, out var v) ? v : key;
	}

	public static string Tf(string key, params object[] args) {
		var s = T(key);
		try { return string.Format(s, args); }
		catch { return s; }
	}
}
