using System.Text;
using Windows.Globalization;

namespace ScreenKit;

/// <summary>日文句子的系统读音。</summary>
sealed class JpYomiResult {
	public string Ruby { get; set; }
	public string Yomi { get; set; }
	public bool HasYomi { get; set; }
}

/// <summary>JapanesePhoneticAnalyzer。没有同等的汉语拼音接口。</summary>
static class JpYomi {
	public static JpYomiResult Convert(string text, bool mono) {
		var res = new JpYomiResult { Ruby = "", Yomi = "" };
		if (string.IsNullOrEmpty(text)) return res;
		var words = JapanesePhoneticAnalyzer.GetWords(text, mono);
		var ruby = new StringBuilder();
		var yomi = new StringBuilder();
		var any = false;
		foreach (var w in words) {
			var show = w.DisplayText ?? "";
			var read = w.YomiText ?? "";
			if (ruby.Length > 0 && w.IsPhraseStart) ruby.Append(' ');
			ruby.Append(show);
			if (read.Length > 0 && read != show) {
				ruby.Append('（').Append(read).Append('）');
				any = true;
			}
			if (yomi.Length > 0) yomi.Append(' ');
			yomi.Append(read.Length > 0 ? read : show);
			if (read.Length > 0 && read != show) any = true;
		}
		res.Ruby = ruby.ToString();
		res.Yomi = yomi.ToString();
		res.HasYomi = any;
		return res;
	}
}
