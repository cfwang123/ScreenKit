using System.Globalization;
using System.Text;

namespace ScreenKit;

/// <summary>字符取模：按字体和字号把每个字画成点阵，再按行或按列打成字节。</summary>
public static class GlyphMod {
	public const int SIZE_MIN = 8;
	public const int SIZE_MAX = 128;
	public const int MAX_CHARS = 128;

	public sealed class Spec {
		public string Font = "Microsoft YaHei";
		public double Size = 16;
		public bool Bold;
		/// <summary>true 逐列，false 逐行。</summary>
		public bool Column;
		/// <summary>true 时一行或一列的第一个点是字节最高位。</summary>
		public bool Msb = true;
		/// <summary>true 阴码：整字节取反。</summary>
		public bool Invert;
		/// <summary>c、hex、dots。</summary>
		public string Format = "c";
		/// <summary>点阵内平移。正 X 向右，正 Y 向下。超出格子的点裁掉。</summary>
		public int OffX;
		public int OffY;
	}

	public sealed class Glyph {
		public string Ch = "";
		public int Cp;
		public int W;
		public int H;
		public bool[] Ink = Array.Empty<bool>();
		public byte[] Data = Array.Empty<byte>();
	}

	public sealed class Sheet {
		public List<Glyph> Glyphs = new();
		public bool Truncated;
		public string Error;
	}

	public static string NormFormat(string raw) {
		raw = (raw ?? "").Trim().ToLowerInvariant();
		if (raw is "hex" or "dots") return raw;
		return "c";
	}

	public static bool NormColumn(string raw) =>
		string.Equals((raw ?? "").Trim(), "col", StringComparison.OrdinalIgnoreCase);

	public static Sheet Build(string text, Spec spec) {
		var sheet = new Sheet();
		spec ??= new Spec();
		var size = spec.Size;
		if (size < SIZE_MIN) size = SIZE_MIN;
		if (size > SIZE_MAX) size = SIZE_MAX;
		var face = new Typeface(
			new FontFamily(string.IsNullOrWhiteSpace(spec.Font) ? "Segoe UI" : spec.Font),
			FontStyles.Normal,
			spec.Bold ? FontWeights.Bold : FontWeights.Regular,
			FontStretches.Normal);
		var chars = takechars(text);
		trim(chars, sheet);
		var dx = clampoff(spec.OffX);
		var dy = clampoff(spec.OffY);
		foreach (var ch in chars) {
			try {
				var g = raster(ch, face, size);
				if (dx != 0 || dy != 0) g.Ink = Move(g.Ink, g.W, g.H, dx, dy);
				g.Data = Pack(g.Ink, g.W, g.H, spec.Column, spec.Msb, spec.Invert);
				sheet.Glyphs.Add(g);
			}
			catch (Exception ex) {
				sheet.Error ??= ex.Message;
			}
		}
		return sheet;
	}

	public static string Export(IList<Glyph> glyphs, Spec spec) {
		spec ??= new Spec();
		var list = glyphs ?? new List<Glyph>();
		var fmt = NormFormat(spec.Format);
		if (fmt == "hex") return exporthex(list, spec);
		if (fmt == "dots") return exportdots(list);
		return exportc(list, spec);
	}

	/// <summary>逐行从左到右、从上到下；逐列从上到下、从左到右。阴码是阳码整字节取反，补位一起反。</summary>
	public static byte[] Pack(bool[] ink, int w, int h, bool column, bool msb, bool invert) {
		if (w <= 0 || h <= 0) return Array.Empty<byte>();
		int stride = column ? (h + 7) / 8 : (w + 7) / 8;
		int n = column ? stride * w : stride * h;
		var data = new byte[n];
		bool on(int x, int y) {
			if ((uint)x >= (uint)w || (uint)y >= (uint)h || ink == null) return false;
			int i = y * w + x;
			return (uint)i < (uint)ink.Length && ink[i];
		}
		if (!column) {
			for (int y = 0; y < h; y++) {
				for (int x = 0; x < w; x++) {
					if (!on(x, y)) continue;
					int bit = msb ? 7 - (x & 7) : (x & 7);
					data[y * stride + (x >> 3)] |= (byte)(1 << bit);
				}
			}
		}
		else {
			for (int x = 0; x < w; x++) {
				for (int y = 0; y < h; y++) {
					if (!on(x, y)) continue;
					int bit = msb ? 7 - (y & 7) : (y & 7);
					data[x * stride + (y >> 3)] |= (byte)(1 << bit);
				}
			}
		}
		if (invert)
			for (int i = 0; i < data.Length; i++) data[i] = (byte)~data[i];
		return data;
	}

	/// <summary>把点在格子里平移。正 dx 向右，正 dy 向下。移出格子的点丢掉。</summary>
	public static bool[] Move(bool[] ink, int w, int h, int dx, int dy) {
		if (w <= 0 || h <= 0) return Array.Empty<bool>();
		if ((dx == 0 && dy == 0) || ink == null) return ink ?? new bool[w * h];
		var dst = new bool[w * h];
		for (int y = 0; y < h; y++) {
			int sy = y - dy;
			if ((uint)sy >= (uint)h) continue;
			for (int x = 0; x < w; x++) {
				int sx = x - dx;
				if ((uint)sx >= (uint)w) continue;
				int si = sy * w + sx;
				if ((uint)si < (uint)ink.Length && ink[si]) dst[y * w + x] = true;
			}
		}
		return dst;
	}

	static int clampoff(int n) {
		if (n < -SIZE_MAX) return -SIZE_MAX;
		if (n > SIZE_MAX) return SIZE_MAX;
		return n;
	}

	static Glyph raster(string ch, Typeface face, double size) {
		int h = Math.Max(1, (int)Math.Ceiling(size));
		if (face.TryGetGlyphTypeface(out var gt)) return rasterglyph(ch, gt, size, h);
		return rastertext(ch, face, size, h);
	}

	static Glyph rasterglyph(string ch, GlyphTypeface gt, double size, int h) {
		int cp = char.ConvertToUtf32(ch, 0);
		if (!gt.CharacterToGlyphMap.TryGetValue(cp, out ushort index)) index = 0;
		double adv = gt.AdvanceWidths[index] * size;
		int w = Math.Max(1, (int)Math.Ceiling(adv));
		var run = new GlyphRun(gt, 0, false, size, 1f, new ushort[] { index },
			new Point(0, gt.Baseline * size), new double[] { adv },
			null, null, null, null, null, null);
		var dv = new DrawingVisual();
		using (var dc = dv.RenderOpen())
			dc.DrawGlyphRun(Brushes.Black, run);
		return readbmp(ch, cp, w, h, dv);
	}

	static Glyph rastertext(string ch, Typeface face, double size, int h) {
		var ft = new FormattedText(ch, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
			face, size, Brushes.Black, 1.0);
		int w = Math.Max(1, (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace));
		var dv = new DrawingVisual();
		using (var dc = dv.RenderOpen())
			dc.DrawText(ft, new Point(0, 0));
		return readbmp(ch, char.ConvertToUtf32(ch, 0), w, h, dv);
	}

	static Glyph readbmp(string ch, int cp, int w, int h, DrawingVisual dv) {
		var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
		bmp.Render(dv);
		int stride = w * 4;
		var buf = new byte[stride * h];
		bmp.CopyPixels(buf, stride, 0);
		var ink = new bool[w * h];
		for (int i = 0; i < ink.Length; i++)
			ink[i] = buf[i * 4 + 3] >= 128;
		return new Glyph { Ch = ch, Cp = cp, W = w, H = h, Ink = ink };
	}

	static void trim(List<string> chars, Sheet sheet) {
		if (chars == null || chars.Count <= MAX_CHARS) return;
		sheet.Truncated = true;
		chars.RemoveRange(MAX_CHARS, chars.Count - MAX_CHARS);
	}

	static List<string> takechars(string text) {
		var list = new List<string>();
		var seen = new HashSet<int>();
		text ??= "";
		for (int i = 0; i < text.Length; i++) {
			string ch;
			if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) {
				ch = text.Substring(i, 2);
				i++;
			}
			else {
				var c = text[i];
				if (char.IsControl(c)) continue;
				ch = c.ToString();
			}
			int cp = char.ConvertToUtf32(ch, 0);
			if (!seen.Add(cp)) continue;
			list.Add(ch);
		}
		return list;
	}

	static string exportc(IList<Glyph> glyphs, Spec spec) {
		var sb = new StringBuilder();
		var font = (spec.Font ?? "").Replace("\r", " ").Replace("\n", " ");
		sb.Append("/* font=").Append(font);
		sb.Append(" size=").Append(spec.Size.ToString("0.##", CultureInfo.InvariantCulture));
		sb.Append(" bold=").Append(spec.Bold ? "1" : "0");
		sb.Append(" scan=").Append(spec.Column ? "col" : "row");
		sb.Append(" bit=").Append(spec.Msb ? "msb" : "lsb");
		sb.Append(" polar=").Append(spec.Invert ? "off" : "on");
		sb.Append(" off=").Append(spec.OffX).Append(",").Append(spec.OffY);
		sb.Append(" */\n");
		sb.Append("#pragma once\n#include <stdint.h>\n\n");
		foreach (var g in glyphs) {
			sb.Append("/* U+").Append(g.Cp.ToString("X4"));
			sb.Append(" ").Append((g.Ch ?? "").Replace("*/", ""));
			sb.Append("  ").Append(g.W).Append("x").Append(g.H);
			sb.Append("  ").Append(g.Data.Length).Append(" */\n");
			sb.Append("static const uint8_t glyph_").Append(g.Cp.ToString("X"));
			sb.Append("[] = {\n");
			appendbytes(sb, g.Data);
			sb.Append("\n};\n\n");
		}
		sb.Append("typedef struct GlyphModItem {\n");
		sb.Append("\tuint32_t cp;\n\tuint16_t w;\n\tuint16_t h;\n\tconst uint8_t *bits;\n");
		sb.Append("} GlyphModItem;\n\n");
		sb.Append("static const GlyphModItem glyphmod_table[] = {\n");
		foreach (var g in glyphs) {
			sb.Append("\t{ 0x").Append(g.Cp.ToString("X")).Append("u, ");
			sb.Append(g.W).Append(", ").Append(g.H).Append(", glyph_").Append(g.Cp.ToString("X")).Append(" },\n");
		}
		sb.Append("};\n");
		sb.Append("static const unsigned glyphmod_count = ").Append(glyphs.Count).Append(";\n");
		return sb.ToString();
	}

	static string exporthex(IList<Glyph> glyphs, Spec spec) {
		var sb = new StringBuilder();
		sb.Append("# font=").Append((spec.Font ?? "").Replace("\r", " ").Replace("\n", " "));
		sb.Append(" size=").Append(spec.Size.ToString("0.##", CultureInfo.InvariantCulture));
		sb.Append(" scan=").Append(spec.Column ? "col" : "row");
		sb.Append(" bit=").Append(spec.Msb ? "msb" : "lsb");
		sb.Append(" polar=").Append(spec.Invert ? "off" : "on");
		sb.Append(" off=").Append(spec.OffX).Append(",").Append(spec.OffY).Append("\n");
		foreach (var g in glyphs) {
			sb.Append("U+").Append(g.Cp.ToString("X4")).Append(" ").Append(g.Ch);
			sb.Append(" ").Append(g.W).Append("x").Append(g.H).Append("\n");
			for (int i = 0; i < g.Data.Length; i++) {
				if (i > 0) sb.Append(i % 16 == 0 ? "\n" : " ");
				sb.Append(g.Data[i].ToString("X2"));
			}
			sb.Append("\n");
		}
		return sb.ToString();
	}

	static string exportdots(IList<Glyph> glyphs) {
		var sb = new StringBuilder();
		foreach (var g in glyphs) {
			sb.Append("U+").Append(g.Cp.ToString("X4")).Append(" ").Append(g.Ch);
			sb.Append(" ").Append(g.W).Append("x").Append(g.H).Append("\n");
			for (int y = 0; y < g.H; y++) {
				for (int x = 0; x < g.W; x++) {
					int i = y * g.W + x;
					bool ink = g.Ink != null && (uint)i < (uint)g.Ink.Length && g.Ink[i];
					sb.Append(ink ? '#' : '.');
				}
				sb.Append("\n");
			}
			sb.Append("\n");
		}
		return sb.ToString();
	}

	static void appendbytes(StringBuilder sb, byte[] data) {
		if (data == null || data.Length == 0) {
			sb.Append("\t0");
			return;
		}
		for (int i = 0; i < data.Length; i++) {
			if (i % 16 == 0) {
				if (i > 0) sb.Append(",\n");
				sb.Append("\t");
			}
			else sb.Append(", ");
			sb.Append("0x").Append(data[i].ToString("x2"));
		}
	}

	public static int SelfTest(Action<string> log) {
		void say(string s) { try { log?.Invoke(s); } catch { } }
		try {
			var ink = new bool[8 * 8];
			ink[0] = true;
			ink[1] = true;
			var row = Pack(ink, 8, 8, false, true, false);
			if (row.Length != 8 || row[0] != 0xC0) return bad(say, "row msb " + row[0].ToString("x2"));
			var lsb = Pack(ink, 8, 8, false, false, false);
			if (lsb[0] != 0x03) return bad(say, "row lsb");
			var col = Pack(ink, 8, 8, true, true, false);
			if (col.Length != 8 || col[0] != 0x80 || col[1] != 0x80) return bad(say, "col msb");
			var inv = Pack(ink, 8, 8, false, true, true);
			if (inv[0] != unchecked((byte)~0xC0) || inv[1] != 0xFF) return bad(say, "invert");
			var wide = new bool[10];
			wide[8] = true;
			var wrow = Pack(wide, 10, 1, false, true, false);
			if (wrow.Length != 2 || wrow[1] != 0x80) return bad(say, "stride");
			if (NormFormat("nope") != "c" || NormFormat("HEX") != "hex") return bad(say, "fmt");
			var dot = new bool[] { true, false, false, false };
			var right = Move(dot, 2, 2, 1, 0);
			if (right[0] || !right[1]) return bad(say, "movex");
			var down = Move(dot, 2, 2, 0, 1);
			if (down[0] || !down[2]) return bad(say, "movey");
			var outx = Move(dot, 2, 2, -1, 0);
			if (outx[0] || outx[1] || outx[2] || outx[3]) return bad(say, "moveout");
			var spec = new Spec { Font = "Segoe UI", Size = 16, Format = "c" };
			var sheet = Build("AA A", spec);
			if (!string.IsNullOrEmpty(sheet.Error)) return bad(say, sheet.Error);
			if (sheet.Glyphs.Count != 2) return bad(say, "count " + sheet.Glyphs.Count);
			var a = sheet.Glyphs[0];
			if (a.Cp != 0x41 || a.W < 1 || a.H < 1) return bad(say, "glyph A");
			int dots = 0;
			foreach (var bit in a.Ink)
				if (bit) dots++;
			if (dots < 1) return bad(say, "blank A");
			int stride = (a.W + 7) / 8;
			if (a.Data.Length != stride * a.H) return bad(say, "len");
			if (a.H != 16) return bad(say, "h " + a.H);
			if (sheet.Glyphs[1].Cp != 0x20) return bad(say, "space");
			var zh = Build("中", new Spec { Font = "Microsoft YaHei", Size = 16, Format = "dots" });
			if (!string.IsNullOrEmpty(zh.Error)) return bad(say, zh.Error);
			if (zh.Glyphs.Count != 1 || zh.Glyphs[0].Cp != 0x4E2D || zh.Glyphs[0].H != 16)
				return bad(say, "zhong");
			int zdots = 0;
			foreach (var bit in zh.Glyphs[0].Ink)
				if (bit) zdots++;
			if (zdots < 8) return bad(say, "zhong ink " + zdots);
			var shifted = Build("中", new Spec { Font = "Microsoft YaHei", Size = 16, OffX = 64 });
			if (!string.IsNullOrEmpty(shifted.Error) || shifted.Glyphs.Count != 1) return bad(say, shifted.Error ?? "off");
			int left = 0;
			foreach (var bit in shifted.Glyphs[0].Ink)
				if (bit) left++;
			if (left != 0 || shifted.Glyphs[0].W != zh.Glyphs[0].W) return bad(say, "offx " + left);
			var code = Export(sheet.Glyphs, spec);
			if (code.IndexOf("uint8_t", StringComparison.Ordinal) < 0 || code.IndexOf("glyph_41", StringComparison.Ordinal) < 0)
				return bad(say, "c");
			if (code.IndexOf("glyphmod_count = 2", StringComparison.Ordinal) < 0) return bad(say, "table");
			spec.Format = "dots";
			var art = Export(sheet.Glyphs, spec);
			if (art.IndexOf("#", StringComparison.Ordinal) < 0) return bad(say, "dots");
			var many = new StringBuilder();
			for (int i = 0; i < MAX_CHARS + 2; i++) many.Append((char)(0x4E00 + i));
			var extra = takechars(many.ToString());
			if (extra.Count != MAX_CHARS + 2) return bad(say, "chars " + extra.Count);
			var cut = new Sheet();
			trim(extra, cut);
			if (!cut.Truncated || extra.Count != MAX_CHARS) return bad(say, "trunc");
			say("=== OK：字符取模");
			return 0;
		}
		catch (Exception ex) {
			return bad(say, ex.Message);
		}
	}

	static int bad(Action<string> say, string msg) {
		say("FAIL " + msg);
		return 1;
	}
}
