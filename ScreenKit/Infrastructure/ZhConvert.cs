using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>LCMapStringEx 简体/繁体。繁体按 zh-TW，用字接近台湾。</summary>
static class ZhConvert {
	const uint LCMAP_SIMPLIFIED_CHINESE = 0x02000000;
	const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

	public static string ToTraditional(string text) => map(text, "zh-TW", LCMAP_TRADITIONAL_CHINESE);

	public static string ToSimplified(string text) => map(text, "zh-CN", LCMAP_SIMPLIFIED_CHINESE);

	static string map(string text, string locale, uint flags) {
		if (string.IsNullOrEmpty(text)) return "";
		int n = LCMapStringEx(locale, flags, text, text.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		if (n <= 0) throw new Win32Exception();
		var buf = new char[n];
		int w = LCMapStringEx(locale, flags, text, text.Length, buf, buf.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		if (w <= 0) throw new Win32Exception();
		return new string(buf, 0, w);
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern int LCMapStringEx(
		string locale, uint flags, string src, int srcLen,
		char[] dst, int dstLen, IntPtr ver, IntPtr reserved, IntPtr sort);
}
