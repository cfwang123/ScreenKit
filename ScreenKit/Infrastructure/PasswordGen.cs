using System.Security.Cryptography;
using System.Text;

namespace ScreenKit;

sealed class PasswordOpts {
	public int Length = 16;
	public int Count = 5;
	public bool Lower = true;
	public bool Upper = true;
	public bool Digit = true;
	public bool Symbol = true;
	public bool NoAmbiguous;
	public bool EachClass = true;
}

/// <summary>密码生成：RNGCryptoServiceProvider，可选字符集。</summary>
static class PasswordGen {
	const string LowerAll = "abcdefghijklmnopqrstuvwxyz";
	const string UpperAll = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
	const string DigitAll = "0123456789";
	const string SymbolAll = "!@#$%^&*-_=+?~";
	const string Ambiguous = "0OIl1o";

	public static string Pool(PasswordOpts o) {
		o ??= new PasswordOpts();
		var sb = new StringBuilder(80);
		if (o.Lower) sb.Append(LowerAll);
		if (o.Upper) sb.Append(UpperAll);
		if (o.Digit) sb.Append(DigitAll);
		if (o.Symbol) sb.Append(SymbolAll);
		var s = sb.ToString();
		if (!o.NoAmbiguous) return s;
		var t = new StringBuilder(s.Length);
		foreach (var ch in s) {
			if (Ambiguous.IndexOf(ch) < 0) t.Append(ch);
		}
		return t.ToString();
	}

	public static string[] Generate(PasswordOpts o) {
		o ??= new PasswordOpts();
		var n = Compat.Clamp(o.Count <= 0 ? 1 : o.Count, 1, 50);
		var list = new string[n];
		for (var i = 0; i < n; i++)
			list[i] = One(o);
		return list;
	}

	public static string One(PasswordOpts o) {
		o ??= new PasswordOpts();
		var len = Compat.Clamp(o.Length <= 0 ? 16 : o.Length, 4, 128);
		var groups = new List<string>();
		if (o.Lower) groups.Add(filter(LowerAll, o.NoAmbiguous));
		if (o.Upper) groups.Add(filter(UpperAll, o.NoAmbiguous));
		if (o.Digit) groups.Add(filter(DigitAll, o.NoAmbiguous));
		if (o.Symbol) groups.Add(filter(SymbolAll, o.NoAmbiguous));
		groups.RemoveAll(g => g.Length == 0);
		if (groups.Count == 0)
			throw new InvalidOperationException("no charset");
		var pool = string.Concat(groups);
		if (o.EachClass && len < groups.Count)
			len = groups.Count;
		var chars = new char[len];
		using var rng = RandomNumberGenerator.Create();
		var i = 0;
		if (o.EachClass) {
			foreach (var g in groups)
				chars[i++] = g[nextint(rng, g.Length)];
		}
		while (i < len)
			chars[i++] = pool[nextint(rng, pool.Length)];
		for (var k = chars.Length - 1; k > 0; k--) {
			var j = nextint(rng, k + 1);
			(chars[k], chars[j]) = (chars[j], chars[k]);
		}
		return new string(chars);
	}

	public static double EntropyBits(PasswordOpts o, int length) {
		var pool = Pool(o);
		if (pool.Length < 2 || length < 1) return 0;
		return length * Math.Log(pool.Length, 2);
	}

	static string filter(string s, bool noAmb) {
		if (!noAmb) return s;
		var t = new StringBuilder(s.Length);
		foreach (var ch in s) {
			if (Ambiguous.IndexOf(ch) < 0) t.Append(ch);
		}
		return t.ToString();
	}

	static int nextint(RandomNumberGenerator rng, int n) {
		if (n <= 1) return 0;
		var buf = new byte[4];
		var max = uint.MaxValue - uint.MaxValue % (uint)n;
		while (true) {
			rng.GetBytes(buf);
			var v = BitConverter.ToUInt32(buf, 0);
			if (v < max) return (int)(v % (uint)n);
		}
	}
}
