using System;
using System.Text.RegularExpressions;

namespace ScreenKit.Soap;

/// <summary>
/// 模拟响应里的动态占位符替换，方便返回“会变”的数据：
/// {{now}} {{utcnow}} {{guid}} {{tick}}。未识别的占位符原样保留。
/// </summary>
static class Template {
	static readonly Regex token = new(@"\{\{\s*([a-zA-Z]+)\s*\}\}", RegexOptions.Compiled);

	public static string expand(string text) {
		if (string.IsNullOrEmpty(text) || text.IndexOf("{{", StringComparison.Ordinal) < 0) return text;
		return token.Replace(text, m => {
			switch (m.Groups[1].Value.ToLowerInvariant()) {
				case "now": return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff");
				case "utcnow": return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
				case "guid": return Guid.NewGuid().ToString();
				case "tick": return Environment.TickCount.ToString();
				default: return m.Value;
			}
		});
	}
}
