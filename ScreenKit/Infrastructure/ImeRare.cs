using System.Text;

namespace ScreenKit;

/// <summary>生僻字判定：词库没有词频的行、编不进 GBK 的字（补充平面、扩展区、区外符号），
/// 以及 GB2312 七千字表外又很少用的字。</summary>
static class ImeRare {
	/// <summary>GB2312 表外但用得多就不算生僻：啰(-3080)、囧(-5970)、喆(-7360)、堃(-1370) 这些后加常用字和
	/// 人名用字比这更“热”；比这更冷的才当生僻：芓(-201)、龘(-110)、玱(-69)、蘛(-30)。</summary>
	const int busy = -2000;

	static readonly Encoding gbk = Encoding.GetEncoding(936);
	static readonly Dictionary<char, bool> gbkcache = new();
	static readonly Dictionary<char, bool> g2312cache = new();

	/// <summary>候选项算不算生僻。rank 传 null 表示没有词频数据（用户自己选的、导入的词），只看字能不能编进 GBK。</summary>
	public static bool Is(string text, int? rank) {
		if (string.IsNullOrEmpty(text)) return false;
		if (rank.HasValue && (rank.Value == 0 || rank.Value >= 1000000)) return true;
		foreach (var ch in text) if (cold(ch, rank)) return true;
		return false;
	}

	static bool cold(char ch, int? rank) {
		if (!han(ch)) return false;         // 标点、字母、假名不算生僻
		if (!ingbk(ch)) return true;        // 编不进 GBK：CJK 补充平面、扩展区
		if (!rank.HasValue) return false;   // 用户自己的词没有词频，字表外也留着
		if (rank.Value <= busy) return false;
		return !ing2312(ch);
	}

	static bool han(char ch) =>
		(ch >= 0x3400 && ch <= 0x9FFF) || (ch >= 0xF900 && ch <= 0xFAFF) || char.IsSurrogate(ch);

	static bool ingbk(char ch) {
		bool v;
		if (gbkcache.TryGetValue(ch, out v)) return v;
		try {
			v = gbk.GetBytes(new[] { ch }).Length == 2;   // 编不进去会退回 '?'
		}
		catch { v = false; }
		gbkcache[ch] = v;
		return v;
	}

	/// <summary>GBK 编码落在 0xB0-0xF7 前导、0xA1-0xFE 尾字节那段，就是 GB2312 的 6763 个汉字。</summary>
	static bool ing2312(char ch) {
		bool v;
		if (g2312cache.TryGetValue(ch, out v)) return v;
		v = false;
		try {
			var b = gbk.GetBytes(new[] { ch });
			if (b.Length == 2) v = b[0] >= 0xB0 && b[0] <= 0xF7 && b[1] >= 0xA1 && b[1] <= 0xFE;
		}
		catch { }
		g2312cache[ch] = v;
		return v;
	}
}
