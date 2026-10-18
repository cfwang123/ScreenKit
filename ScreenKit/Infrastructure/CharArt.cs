using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>一个控制台格子：字符和前景、背景（0xRRGGBB，-1 表示空）。</summary>
public struct CharCell {
	public char Ch;
	public int Fg;
	public int Bg;
}

/// <summary>字符画：单色 ASCII，以及命令行里的全色块。</summary>
static class CharArt {
	public const int MODE_AUTO = 0;
	public const int MODE_FULL = 1;
	public const int MODE_HALF = 2;
	public const int MODE_QUARTER = 4;
	public const int MODE_EIGHTH = 8;
	// 明暗画法（不是线条）：按亮度取 .:-=+*#%@。界面下拉里的默认档，走 Ascii。
	public const int LINES_SHADE = -1;
	public const int LINES_SIMPLE = 0;
	public const int LINES_ASCII = 1;
	public const int LINES_BOX = 2;
	// 改版前的画法：每个格子只判一个方向。
	public const int LINES_LEGACY = 3;
	// 点阵阴影：按亮度在 · ░ ▒ ▓ █ 之间取一档，不走线条判定。
	public const int LINES_DOTS = 4;
	// 线在格子里的四个连接方向：1 上、2 下、4 左、8 右。
	const int E_U = 1;
	const int E_D = 2;
	const int E_L = 4;
	const int E_R = 8;
	// 整行（整列）都暗到 cut 的这么多倍，才算粗线。
	const int THICK_SPAN = 2;
	const double CELL_ASPECT = 2;
	// 四象限最远一对的通道平方和小于此值才当成一色。差 3 级就要拆开。
	const int SPLIT_DIST = 9;
	const string RAMP = " .:-=+*#%@";
	// 点阵阴影：中点、浅/中/深阴影、实心。Consolas 都有这些字形。
	const string DOTRAMP = " \u00b7\u2591\u2592\u2593\u2588";
	// 四象限位：1 左上、2 右上、4 左下、8 右下。前景是置位的那一侧。
	const string QUAD =
		" \u2598\u259d\u2580\u2596\u258c\u259e\u259b\u2597\u259a\u2590\u259c\u2584\u2599\u259f\u2588";

	const int STD_INPUT_HANDLE = -10;
	const int STD_OUTPUT_HANDLE = -11;
	const uint ENABLE_PROCESSED_INPUT = 0x0001;
	const uint ENABLE_WINDOW_INPUT = 0x0008;
	const uint ENABLE_MOUSE_INPUT = 0x0010;
	const uint ENABLE_EXTENDED_FLAGS = 0x0080;
	const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
	const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
	const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
	const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;
	const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;
	const ushort KEY_EVENT = 0x0001;
	const ushort MOUSE_EVENT = 0x0002;
	const ushort SIZE_EVENT = 0x0004;
	const uint MOUSE_MOVED = 0x0001;
	const uint DOUBLE_CLICK = 0x0002;
	const uint MOUSE_WHEELED = 0x0004;
	const uint FROM_LEFT_1ST = 0x0001;

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr GetStdHandle(int nStdHandle);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GetConsoleMode(IntPtr h, out uint mode);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool SetConsoleMode(IntPtr h, uint mode);

	[DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
	static extern bool ReadConsoleInput(IntPtr h, [Out] INPUT_RECORD[] buf, int n, out int read);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GetConsoleScreenBufferInfo(IntPtr h, out CONSOLE_SCREEN_BUFFER_INFO info);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool SetConsoleScreenBufferSize(IntPtr h, COORD size);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool SetConsoleWindowInfo(IntPtr h, bool absolute, ref SMALL_RECT rect);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetConsoleTitle(string title);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetCurrentConsoleFontEx(IntPtr h, bool maximumWindow, ref CONSOLE_FONT_INFOEX info);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct CONSOLE_FONT_INFOEX {
		public int cbSize;
		public int Font;
		public short FontWidth;
		public short FontHeight;
		public int Family;
		public int Weight;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string FaceName;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct COORD {
		public short X;
		public short Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct SMALL_RECT {
		public short Left;
		public short Top;
		public short Right;
		public short Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct CONSOLE_SCREEN_BUFFER_INFO {
		public COORD Size;
		public COORD Cursor;
		public ushort Attributes;
		public SMALL_RECT Window;
		public COORD MaximumWindowSize;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct KEY_EVENT_RECORD {
		public int KeyDown;
		public ushort Repeat;
		public ushort VirtualKey;
		public ushort VirtualScan;
		public char Char;
		public uint ControlKeyState;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct MOUSE_EVENT_RECORD {
		public short X;
		public short Y;
		public uint ButtonState;
		public uint ControlKeyState;
		public uint EventFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct SIZE_RECORD {
		public short X;
		public short Y;
	}

	[StructLayout(LayoutKind.Explicit)]
	struct INPUT_RECORD {
		[FieldOffset(0)] public ushort EventType;
		[FieldOffset(4)] public KEY_EVENT_RECORD Key;
		[FieldOffset(4)] public MOUSE_EVENT_RECORD Mouse;
		[FieldOffset(4)] public SIZE_RECORD Size;
	}

	public static byte[] Bgra(BitmapSource src, out int w, out int h) {
		var fmt = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
		fmt.Freeze();
		w = fmt.PixelWidth;
		h = fmt.PixelHeight;
		if (w < 1 || h < 1) {
			w = 1;
			h = 1;
			return new byte[] { 0, 0, 0, 255 };
		}
		var stride = w * 4;
		var buf = new byte[stride * h];
		fmt.CopyPixels(buf, stride, 0);
		return buf;
	}

	/// <summary>按格子里的明暗走向画线。threshold 为 0 时很淡的边和落在格子交界上的线也保留，越高越只留更明显的线。lineset 选画法：基本 - | / \ +、丰富 ASCII、制表符，或改版前每格只判一个方向的旧画法。</summary>
	public static string Lines(byte[] px, int w, int h, int cols, bool invert, int threshold, int lineset = LINES_SIMPLE) {
		if (px == null || w < 1 || h < 1) return "";
		if (cols < 1) cols = 1;
		if (cols > 400) cols = 400;
		if (threshold < 0) threshold = 0;
		if (threshold > 255) threshold = 255;
		if (lineset == LINES_LEGACY) return legacylines(px, w, h, cols, invert, threshold);
		var rows = gridrows(w, h, cols);
		var ncell = rows * cols;
		var lums = new int[ncell];
		var masks = new int[ncell];
		var thicks = new bool[ncell];
		var diags = new byte[ncell];
		var s = new int[9];
		var cut = linecut(threshold);
		for (var y = 0; y < rows; y++) {
			var y0 = h * (y / (double)rows);
			var y1 = h * ((y + 1) / (double)rows);
			for (var x = 0; x < cols; x++) {
				var x0 = w * (x / (double)cols);
				var x1 = w * ((x + 1) / (double)cols);
				var sum = 0;
				for (var sy = 0; sy < 3; sy++) {
					var ya = y0 + (y1 - y0) * sy / 3;
					var yb = y0 + (y1 - y0) * (sy + 1) / 3;
					for (var sx = 0; sx < 3; sx++) {
						var xa = x0 + (x1 - x0) * sx / 3;
						var xb = x0 + (x1 - x0) * (sx + 1) / 3;
						var lum = lumof(area(px, w, h, xa, xb, ya, yb), invert);
						s[sy * 3 + sx] = lum;
						sum += lum;
					}
				}
				var i = y * cols + x;
				lums[i] = sum / 9;
				masks[i] = cellmask(s, cut, out var dg);
				if (masks[i] != 0) thicks[i] = thickline(s, masks[i], cut);
				else diags[i] = dg;
			}
		}
		// 线正好落在格子交界上时，格子内部没有方向，改看比相邻格子暗的那一侧。
		for (var y = 0; y < rows; y++) {
			for (var x = 0; x < cols; x++) {
				var i = y * cols + x;
				if (masks[i] != 0 || diags[i] != 0) continue;
				masks[i] = edgeseam(lums, x, y, cols, rows, cut, out var seam);
				if (masks[i] == 0 && seam != 0) diags[i] = seam;
			}
		}
		var chars = new char[ncell];
		for (var i = 0; i < ncell; i++) {
			if (masks[i] != 0) chars[i] = lineglyph(masks[i], thicks[i], lineset);
			else if (diags[i] != 0) chars[i] = diagchar(diags[i], lineset);
			else chars[i] = ' ';
		}
		var sb = new StringBuilder(rows * (cols + 1));
		for (var y = 0; y < rows; y++) {
			sb.Append(chars, y * cols, cols);
			if (y + 1 < rows) sb.Append('\n');
		}
		return sb.ToString();
	}

	/// <summary>改版前的画法：每个格子只判一个方向，输出 - | / \ 和 +。落在格子交界上的线另用 edgech 补。</summary>
	static string legacylines(byte[] px, int w, int h, int cols, bool invert, int threshold) {
		var rows = gridrows(w, h, cols);
		var ncell = rows * cols;
		var lums = new int[ncell];
		var chars = new char[ncell];
		var s = new int[9];
		var cut = linecut(threshold);
		for (var y = 0; y < rows; y++) {
			var y0 = h * (y / (double)rows);
			var y1 = h * ((y + 1) / (double)rows);
			for (var x = 0; x < cols; x++) {
				var x0 = w * (x / (double)cols);
				var x1 = w * ((x + 1) / (double)cols);
				var sum = 0;
				for (var sy = 0; sy < 3; sy++) {
					var ya = y0 + (y1 - y0) * sy / 3;
					var yb = y0 + (y1 - y0) * (sy + 1) / 3;
					for (var sx = 0; sx < 3; sx++) {
						var xa = x0 + (x1 - x0) * sx / 3;
						var xb = x0 + (x1 - x0) * (sx + 1) / 3;
						var lum = lumof(area(px, w, h, xa, xb, ya, yb), invert);
						s[sy * 3 + sx] = lum;
						sum += lum;
					}
				}
				var i = y * cols + x;
				lums[i] = sum / 9;
				chars[i] = linech(s, cut);
			}
		}
		for (var y = 0; y < rows; y++) {
			for (var x = 0; x < cols; x++) {
				var i = y * cols + x;
				if (chars[i] == ' ') chars[i] = edgech(lums, x, y, cols, rows, cut);
			}
		}
		var sb = new StringBuilder(rows * (cols + 1));
		for (var y = 0; y < rows; y++) {
			sb.Append(chars, y * cols, cols);
			if (y + 1 < rows) sb.Append('\n');
		}
		return sb.ToString();
	}

	/// <summary>9 格亮度。中间一行更暗是横线，中间一列更暗是竖线，两条斜线谁更暗决定 / 或 \。cut 是亮度差，格子内按三格合计比较。</summary>
	static char linech(int[] s, int cut) {
		int at(int i) => s[i] < 0 ? 255 : s[i];
		int dark(int a, int b, int c) => 765 - (at(a) + at(b) + at(c));
		var top = dark(0, 1, 2);
		var mid = dark(3, 4, 5);
		var bot = dark(6, 7, 8);
		var left = dark(0, 3, 6);
		var cen = dark(1, 4, 7);
		var right = dark(2, 5, 8);
		var slash = dark(2, 4, 6);
		var back = dark(0, 4, 8);
		var hScore = Math.Max(top, Math.Max(mid, bot)) - Math.Min(top, Math.Min(mid, bot));
		var vScore = Math.Max(left, Math.Max(cen, right)) - Math.Min(left, Math.Min(cen, right));
		var diag = Math.Abs(slash - back);
		var floor = cut * 3;
		if (hScore >= floor && vScore >= floor && hScore * 3 >= diag * 2 && vScore * 3 >= diag * 2)
			return '+';
		if (diag >= hScore && diag >= vScore && diag >= floor)
			return slash > back ? '/' : '\\';
		if (hScore >= vScore && hScore >= floor) return '-';
		if (vScore >= floor) return '|';
		return ' ';
	}

	/// <summary>格子内部没有对比时，看比相邻格子更暗的一侧。线正好落在交界上也会画出来，而且只占更暗的那一格。</summary>
	static char edgech(int[] lum, int x, int y, int cols, int rows, int cut) {
		int at(int xx, int yy) {
			if (xx < 0) xx = 0;
			if (yy < 0) yy = 0;
			if (xx >= cols) xx = cols - 1;
			if (yy >= rows) yy = rows - 1;
			return lum[yy * cols + xx];
		}
		var here = at(x, y);
		int brighter(int xx, int yy) {
			var d = at(xx, yy) - here;
			if (d < 0) return 0;
			return d;
		}
		var ax = Math.Max(brighter(x - 1, y), brighter(x + 1, y));
		var ay = Math.Max(brighter(x, y - 1), brighter(x, y + 1));
		var slash = Math.Max(brighter(x + 1, y - 1), brighter(x - 1, y + 1));
		var back = Math.Max(brighter(x - 1, y - 1), brighter(x + 1, y + 1));
		var diag = Math.Abs(slash - back);
		if (ax >= cut && ay >= cut && ax * 3 >= diag * 2 && ay * 3 >= diag * 2)
			return '+';
		if (diag >= ax && diag >= ay && diag >= cut)
			return slash > back ? '/' : '\\';
		if (ay >= ax && ay >= cut) return '-';
		if (ax >= cut) return '|';
		return ' ';
	}

	/// <summary>纯 ASCII。字符格子大约两倍高，按列数算出行数。不勾选反色时越暗越密，勾选后反过来。threshold 以下留空。</summary>
	public static string Ascii(byte[] px, int w, int h, int cols, bool invert, int threshold) =>
		ramp(px, w, h, cols, invert, threshold, RAMP);

	/// <summary>点阵阴影：按亮度在点、浅/中/深阴影、实心之间取一档，越暗越密。</summary>
	public static string ShadeDots(byte[] px, int w, int h, int cols, bool invert, int threshold) =>
		ramp(px, w, h, cols, invert, threshold, DOTRAMP);

	/// <summary>按亮度在给定的字符序列上取一档。threshold 以下留空。</summary>
	static string ramp(byte[] px, int w, int h, int cols, bool invert, int threshold, string chars) {
		if (px == null || w < 1 || h < 1) return "";
		if (cols < 1) cols = 1;
		if (cols > 400) cols = 400;
		if (threshold < 0) threshold = 0;
		if (threshold > 255) threshold = 255;
		var rows = gridrows(w, h, cols);
		var sb = new StringBuilder(rows * (cols + 1));
		for (var y = 0; y < rows; y++) {
			var y0 = h * (y / (double)rows);
			var y1 = h * ((y + 1) / (double)rows);
			for (var x = 0; x < cols; x++) {
				var x0 = w * (x / (double)cols);
				var x1 = w * ((x + 1) / (double)cols);
				var c = sample(px, w, h, (x0 + x1) / 2, (y0 + y1) / 2);
				var lum = c < 0 ? 0 : (red(c) * 2126 + green(c) * 7152 + blue(c) * 722) / 10000;
				if (!invert) lum = 255 - lum;
				var i = 0;
				if (lum > threshold) {
					var span = 255 - threshold;
					if (span < 1) span = 1;
					i = (lum - threshold) * (chars.Length - 1) / span;
					if (i >= chars.Length) i = chars.Length - 1;
				}
				sb.Append(chars[i]);
			}
			if (y + 1 < rows) sb.Append('\n');
		}
		return sb.ToString();
	}

	/// <summary>1/8 到 7/8 是从左往右加宽的块，8 是整块，0 是空格。</summary>
	public static char Eighth(int k) {
		if (k <= 0) return ' ';
		if (k >= 8) return '\u2588';
		return (char)(0x2590 - k);
	}

	public static void Fit(int imgW, int imgH, int cols, int rows,
		out double cellW, out double cellH, out double ox, out double oy) {
		if (imgW < 1) imgW = 1;
		if (imgH < 1) imgH = 1;
		if (cols < 1) cols = 1;
		if (rows < 1) rows = 1;
		var px = imgW / (double)cols;
		var py = imgH / (double)rows / CELL_ASPECT;
		if (py > px) px = py;
		if (px < 1e-6) px = 1e-6;
		cellW = px;
		cellH = px * CELL_ASPECT;
		ox = (imgW - cols * cellW) / 2;
		oy = (imgH - rows * cellH) / 2;
	}

	/// <summary>以格子 (col,row) 的中心为不动点缩放。factor 小于 1 是放大。</summary>
	public static void Zoom(double cellW, double cellH, double ox, double oy, int col, int row, double factor,
		out double ncellW, out double ncellH, out double nox, out double noy) {
		if (factor < 0.25) factor = 0.25;
		if (factor > 4) factor = 4;
		var ix = ox + (col + 0.5) * cellW;
		var iy = oy + (row + 0.5) * cellH;
		ncellW = cellW * factor;
		if (ncellW < 1.0 / 64) ncellW = 1.0 / 64;
		ncellH = ncellW * CELL_ASPECT;
		nox = ix - (col + 0.5) * ncellW;
		noy = iy - (row + 0.5) * ncellH;
	}

	public static void Pan(double ox, double oy, double cellW, double cellH, int dx, int dy,
		out double nox, out double noy) {
		nox = ox - dx * cellW;
		noy = oy - dy * cellH;
	}

	public static CharCell Pick(byte[] px, int w, int h, double x0, double x1, double y0, double y1, int mode) {
		var cell = new CharCell { Ch = ' ', Fg = -1, Bg = -1 };
		if (px == null || w < 1 || h < 1) return cell;
		if (x1 < x0) { var t = x0; x0 = x1; x1 = t; }
		if (y1 < y0) { var t = y0; y0 = y1; y1 = t; }
		var s = new int[16];
		var n = 0;
		for (var row = 0; row < 2; row++) {
			var ya = y0 + (y1 - y0) * row / 2;
			var yb = y0 + (y1 - y0) * (row + 1) / 2;
			for (var col = 0; col < 8; col++) {
				var xa = x0 + (x1 - x0) * col / 8;
				var xb = x0 + (x1 - x0) * (col + 1) / 8;
				var c = area(px, w, h, xa, xb, ya, yb);
				s[row * 8 + col] = c;
				if (c >= 0) n++;
			}
		}
		if (n == 0) return cell;
		int fg, bg;
		if (mode == MODE_HALF) half(s, out cell.Ch, out fg, out bg);
		else if (mode == MODE_EIGHTH) eighth(s, out cell.Ch, out fg, out bg);
		else if (mode == MODE_QUARTER) quarter(s, out cell.Ch, out fg, out bg);
		else if (mode == MODE_AUTO) autosplit(s, out cell.Ch, out fg, out bg);
		else {
			var colc = new int[8];
			for (var col = 0; col < 8; col++) colc[col] = mix(s[col], s[8 + col]);
			cell.Ch = '\u2588';
			fg = avg(colc, 0, 8);
			bg = fg;
		}
		if (fg < 0) return cell;
		if (bg < 0) bg = fg;
		cell.Fg = fg;
		cell.Bg = bg;
		return cell;
	}

	public static void Open(string path) {
		if (string.IsNullOrWhiteSpace(path)) return;
		var exe = Process.GetCurrentProcess().MainModule?.FileName;
		if (string.IsNullOrWhiteSpace(exe)) return;
		// cmd 自己开一个控制台。直接挂到 PowerShell 上时，读按键会立刻返回，窗口一闪就关。
		var inner = $"{quote(exe)} --charart {quote(path)}";
		Process.Start(new ProcessStartInfo {
			FileName = "cmd.exe",
			Arguments = $"/s /c \"{inner}\"",
			UseShellExecute = true,
		});
	}

	public static int View(string path) {
		try {
			var o = new OcrOptions();
			AppConfig.LoadInto(o);
			if (!string.IsNullOrWhiteSpace(o.UiLang)) Loc.Lang = o.UiLang;
		}
		catch { }
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
			Console.Error.WriteLine(Loc.T("charart.bad"));
			pause();
			return 1;
		}
		byte[] px;
		int w, h;
		try {
			px = Bgra(ImageUtil.Fromfile(path), out w, out h);
		}
		catch (Exception ex) {
			Console.Error.WriteLine(ex.Message);
			pause();
			return 1;
		}
		try {
			view(px, w, h, Path.GetFileName(path));
		}
		catch (Exception ex) {
			Console.Error.WriteLine(ex.Message);
			pause();
			return 1;
		}
		return 0;
	}

	public static int Test(Action<string> ok, Action<string> bad) {
		if (Eighth(1) != '\u258f' || Eighth(2) != '\u258e' || Eighth(3) != '\u258d' ||
			Eighth(4) != '\u258c' || Eighth(5) != '\u258b' || Eighth(6) != '\u258a' ||
			Eighth(7) != '\u2589' || Eighth(8) != '\u2588' || Eighth(0) != ' ') {
			bad("charart eighth glyph");
			return 1;
		}
		var split = solid(8, 2, 0, 0, 255);
		fill(split, 8, 2, 0, 4, 255, 0, 0);
		var edge = Pick(split, 8, 2, 0, 8, 0, 2, MODE_EIGHTH);
		if (edge.Ch != '\u258c' || edge.Fg != 0xFF0000 || edge.Bg != 0x0000FF) {
			bad($"charart eighth split U+{(int)edge.Ch:X4} {edge.Fg:X6} {edge.Bg:X6}");
			return 1;
		}
		var thin = solid(8, 2, 0, 0, 0);
		fill(thin, 8, 2, 0, 1, 0, 255, 0);
		var one = Pick(thin, 8, 2, 0, 8, 0, 2, MODE_EIGHTH);
		if (one.Ch != Eighth(1)) {
			bad($"charart 1/8 U+{(int)one.Ch:X4}");
			return 1;
		}
		var autoOne = Pick(thin, 8, 2, 0, 8, 0, 2, MODE_AUTO);
		if (autoOne.Ch != Eighth(1)) {
			bad($"charart auto 1/8 U+{(int)autoOne.Ch:X4}");
			return 1;
		}
		var three = solid(8, 2, 0, 0, 0);
		fill(three, 8, 2, 0, 3, 255, 128, 0);
		var tcell = Pick(three, 8, 2, 0, 8, 0, 2, MODE_EIGHTH);
		if (tcell.Ch != Eighth(3)) {
			bad($"charart 3/8 U+{(int)tcell.Ch:X4}");
			return 1;
		}
		var autoThree = Pick(three, 8, 2, 0, 8, 0, 2, MODE_AUTO);
		if (autoThree.Ch != Eighth(3)) {
			bad($"charart auto 3/8 U+{(int)autoThree.Ch:X4}");
			return 1;
		}
		var seven = solid(8, 2, 0, 0, 0);
		fill(seven, 8, 2, 0, 7, 255, 255, 0);
		var scell = Pick(seven, 8, 2, 0, 8, 0, 2, MODE_EIGHTH);
		if (scell.Ch != Eighth(7)) {
			bad($"charart 7/8 U+{(int)scell.Ch:X4}");
			return 1;
		}
		var halfpx = solid(4, 2, 255, 255, 255);
		for (var i = 4 * 4; i < halfpx.Length; i += 4) {
			halfpx[i] = 0;
			halfpx[i + 1] = 0;
			halfpx[i + 2] = 0;
			halfpx[i + 3] = 255;
		}
		var half = Pick(halfpx, 4, 2, 0, 4, 0, 2, MODE_HALF);
		if (half.Ch != '\u2580' || half.Fg != 0xFFFFFF || half.Bg != 0x000000) {
			bad($"charart half U+{(int)half.Ch:X4} {half.Fg:X6} {half.Bg:X6}");
			return 1;
		}
		var qpx = solid(2, 2, 0, 0, 255);
		qpx[0] = 0; qpx[1] = 0; qpx[2] = 255; qpx[3] = 255;
		var quad = Pick(qpx, 2, 2, 0, 2, 0, 2, MODE_QUARTER);
		if (quad.Ch != '\u2598') {
			bad($"charart quarter U+{(int)quad.Ch:X4}");
			return 1;
		}
		var white = new byte[4 * 2 * 4];
		fill(white, 4, 2, 0, 4, 255, 255, 255);
		var black = new byte[4 * 2 * 4];
		fill(black, 4, 2, 0, 4, 0, 0, 0);
		var aw = Ascii(white, 4, 2, 4, false, 0);
		var ab = Ascii(black, 4, 2, 4, false, 0);
		if (ab.IndexOf('@') < 0 || aw.Trim().Length != 0 || !isascii(aw) || !isascii(ab)) {
			bad("charart ascii");
			return 1;
		}
		var sd = ShadeDots(black, 4, 2, 4, false, 0);
		var sw = ShadeDots(white, 4, 2, 4, false, 0);
		var sm = ShadeDots(solid(4, 2, 90, 90, 90), 4, 2, 4, false, 0);
		if (sd.IndexOf('\u2588') < 0 || sw.Trim().Length != 0 || sm.IndexOf('\u2592') < 0) {
			bad("charart dots " + sd.Replace("\n", "/") + "|" + sm.Replace("\n", "/"));
			return 1;
		}
		var pale = solid(4, 2, 200, 200, 200);
		var cut = Ascii(pale, 4, 2, 4, false, 128);
		var hi = Ascii(black, 4, 2, 4, false, 255);
		var kept = Ascii(black, 4, 2, 4, false, 128);
		if (cut.Trim().Length != 0 || hi.Trim().Length != 0 || kept.IndexOf('@') < 0) {
			bad("charart threshold");
			return 1;
		}
		var hz = solid(9, 18, 255, 255, 255);
		paintrow(hz, 9, 8, 0, 0, 0);
		paintrow(hz, 9, 9, 0, 0, 0);
		var hzt = Lines(hz, 9, 18, 3, false, 0);
		if (countch(hzt, '-') < 3 || countch(hzt, '|') != 0) {
			bad("charart line h " + hzt.Replace("\n", "/"));
			return 1;
		}
		var vt = solid(9, 18, 255, 255, 255);
		paintcol(vt, 9, 18, 4, 0, 0, 0);
		var vtt = Lines(vt, 9, 18, 3, false, 0);
		if (countch(vtt, '|') < 3 || countch(vtt, '-') != 0) {
			bad("charart line v " + vtt.Replace("\n", "/"));
			return 1;
		}
		var dg = solid(9, 18, 255, 255, 255);
		paintdiag(dg, 3, 3, 3, 6, true);
		var dgt = Lines(dg, 9, 18, 3, false, 0);
		if (countch(dgt, '/') < 6) {
			bad("charart line / " + dgt.Replace("\n", "/"));
			return 1;
		}
		var bk = solid(9, 18, 255, 255, 255);
		paintdiag(bk, 3, 3, 3, 6, false);
		var bkt = Lines(bk, 9, 18, 3, false, 0);
		if (countch(bkt, '\\') < 6) {
			bad("charart line \\ " + bkt.Replace("\n", "/"));
			return 1;
		}
		var flat = Lines(solid(9, 18, 255, 255, 255), 9, 18, 3, false, 0);
		if (flat.Trim().Length != 0) {
			bad("charart line blank");
			return 1;
		}
		var faint = solid(9, 18, 255, 255, 255);
		paintrow(faint, 9, 9, 200, 200, 200);
		if (Lines(faint, 9, 18, 3, false, 255).Trim().Length != 0) {
			bad("charart line faint");
			return 1;
		}
		if (lineglyph(E_L | E_R, false, LINES_ASCII) != '-' || lineglyph(E_L | E_R, true, LINES_ASCII) != '=' ||
			lineglyph(E_U | E_D, false, LINES_ASCII) != '|' || lineglyph(E_U | E_D, true, LINES_ASCII) != '!' ||
			lineglyph(E_R | E_D, false, LINES_ASCII) != '(' || lineglyph(E_L | E_U, false, LINES_ASCII) != ']') {
			bad("charart ascii glyphs");
			return 1;
		}
		if (lineglyph(E_L | E_R | E_D, false, LINES_ASCII) != 'v' || lineglyph(E_U | E_D | E_R, false, LINES_ASCII) != '>' ||
			lineglyph(E_L | E_R | E_U | E_D, false, LINES_ASCII) != '#' ||
			diagchar(1, LINES_BOX) != '╱' || diagchar(2, LINES_BOX) != '╲' ||
			diagchar(1, LINES_ASCII) != '/' || diagchar(2, LINES_ASCII) != '\\') {
			bad("charart ascii tees");
			return 1;
		}
		if (lineglyph(E_L | E_R, false, LINES_BOX) != '─' || lineglyph(E_U | E_D, true, LINES_BOX) != '║' ||
			lineglyph(E_R | E_D, false, LINES_BOX) != '┌' || lineglyph(E_L | E_R | E_U | E_D, false, LINES_BOX) != '┼' ||
			lineglyph(E_L | E_R | E_U | E_D, true, LINES_BOX) != '╬' || lineglyph(E_L, false, LINES_BOX) != '╴') {
			bad("charart box glyphs");
			return 1;
		}
		if (Lines(solid(9, 18, 255, 255, 255), 9, 18, 3, false, 0, LINES_BOX).Trim().Length != 0) {
			bad("charart box blank");
			return 1;
		}
		var boxh = Lines(hz, 9, 18, 3, false, 0, LINES_BOX);
		if (countch(boxh, '─') < 3 || countch(boxh, '┼') != 0) {
			bad("charart box h " + boxh.Replace("\n", "/"));
			return 1;
		}
		var boxv = Lines(vt, 9, 18, 3, false, 0, LINES_BOX);
		if (countch(boxv, '│') < 3) {
			bad("charart box v " + boxv.Replace("\n", "/"));
			return 1;
		}
		var corner = solid(9, 18, 255, 255, 255);
		for (var cy = 9; cy < 18; cy++) ink(corner, 9, 4, cy);
		for (var cx = 4; cx < 9; cx++) ink(corner, 9, cx, 9);
		var cornbox = Lines(corner, 9, 18, 3, false, 0, LINES_BOX);
		var cornasc = Lines(corner, 9, 18, 3, false, 0, LINES_ASCII);
		if (countch(cornbox, '┌') < 1 || countch(cornasc, '(') < 1) {
			bad("charart corner " + cornbox.Replace("\n", "/") + " | " + cornasc.Replace("\n", "/"));
			return 1;
		}
		var wide = solid(9, 18, 255, 255, 255);
		for (var wy = 8; wy < 12; wy++) paintrow(wide, 9, wy, 0, 0, 0);
		var widebox = Lines(wide, 9, 18, 3, false, 0, LINES_BOX);
		var wideasc = Lines(wide, 9, 18, 3, false, 0, LINES_ASCII);
		if (countch(widebox, '═') < 3 || countch(wideasc, '=') < 3) {
			bad("charart thick " + widebox.Replace("\n", "/") + " | " + wideasc.Replace("\n", "/"));
			return 1;
		}
		var oldh = Lines(hz, 9, 18, 3, false, 0, LINES_LEGACY);
		var oldv = Lines(vt, 9, 18, 3, false, 0, LINES_LEGACY);
		var oldd = Lines(dg, 9, 18, 3, false, 0, LINES_LEGACY);
		var oldc = Lines(corner, 9, 18, 3, false, 0, LINES_LEGACY);
		if (countch(oldh, '-') < 3 || countch(oldh, '|') != 0 ||
			countch(oldv, '|') < 3 || countch(oldv, '-') != 0 ||
			countch(oldd, '/') < 6 || countch(oldc, '+') < 1) {
			bad("charart legacy " + oldh.Replace("\n", "/") + " | " + oldv.Replace("\n", "/") + " | " + oldd.Replace("\n", "/") + " | " + oldc.Replace("\n", "/"));
			return 1;
		}
		Fit(100, 50, 50, 20, out var fw, out var fh, out var fox, out var foy);
		if (Math.Abs(fw - 2) > 1e-6 || Math.Abs(fh - 4) > 1e-6 || Math.Abs(fox) > 1e-6 || Math.Abs(foy - (-15)) > 1e-6) {
			bad($"charart fit {fw} {fh} {fox} {foy}");
			return 1;
		}
		Zoom(2, 4, 0, 0, 10, 5, 0.5, out var zw, out var zh, out var zox, out var zoy);
		if (Math.Abs(zw - 1) > 1e-6 || Math.Abs(zh - 2) > 1e-6 || Math.Abs(zox - 10.5) > 1e-6 || Math.Abs(zoy - 11) > 1e-6) {
			bad($"charart zoom {zw} {zh} {zox} {zoy}");
			return 1;
		}
		Pan(10, 20, 2, 4, 3, -1, out var pox, out var poy);
		if (Math.Abs(pox - 4) > 1e-6 || Math.Abs(poy - 24) > 1e-6) {
			bad($"charart pan {pox} {poy}");
			return 1;
		}
		var soft = solid(8, 2, 40, 80, 40);
		fill(soft, 8, 2, 0, 4, 40, 100, 40);
		var softEdge = Pick(soft, 8, 2, 0, 8, 0, 2, MODE_AUTO);
		if (softEdge.Ch != '\u258c') {
			bad($"charart soft eighth U+{(int)softEdge.Ch:X4}");
			return 1;
		}
		var band = solid(4, 2, 100, 100, 100);
		for (var i = 4 * 4; i < band.Length; i += 4) {
			band[i] = 100;
			band[i + 1] = 100;
			band[i + 2] = 120;
			band[i + 3] = 255;
		}
		var softHalf = Pick(band, 4, 2, 0, 4, 0, 2, MODE_AUTO);
		if (softHalf.Ch != '\u2580') {
			bad($"charart soft half U+{(int)softHalf.Ch:X4}");
			return 1;
		}
		var flatpx = solid(4, 2, 180, 90, 30);
		var flatCell = Pick(flatpx, 4, 2, 0, 4, 0, 2, MODE_AUTO);
		if (flatCell.Ch != ' ' || flatCell.Fg != 0xB45A1E || flatCell.Bg != 0xB45A1E) {
			bad($"charart flat U+{(int)flatCell.Ch:X4} {flatCell.Fg:X6}");
			return 1;
		}
		var close = solid(4, 2, 100, 100, 100);
		fill(close, 4, 2, 2, 4, 100, 102, 100);
		var closeCell = Pick(close, 4, 2, 0, 4, 0, 2, MODE_AUTO);
		if (closeCell.Ch != ' ') {
			bad($"charart under split U+{(int)closeCell.Ch:X4}");
			return 1;
		}
		var grad = solid(2, 2, 200, 100, 70);
		grad[0] = 40; grad[1] = 100; grad[2] = 200; grad[3] = 255;
		var gcell = Pick(grad, 2, 2, 0, 2, 0, 2, MODE_AUTO);
		if (gcell.Ch != '\u2598') {
			bad($"charart gradient U+{(int)gcell.Ch:X4}");
			return 1;
		}
		var fullCell = Pick(grad, 2, 2, 0, 2, 0, 2, MODE_FULL);
		if (fullCell.Ch != '\u2588') {
			bad($"charart full U+{(int)fullCell.Ch:X4}");
			return 1;
		}
		ok("charart OK");
		ok("charart eighth 1/8 3/8 1/2 7/8 OK");
		ok("charart half quarter ascii fit zoom OK");
		ok("charart soft split keeps half and eighth");
		ok("charart auto quadrant split");
		ok("charart line sets basic ascii box");
		ok("charart legacy line mode");
		ok("charart shade dots mode");
		return 0;
	}

	static void view(byte[] px, int w, int h, string name) {
		var hout = GetStdHandle(STD_OUTPUT_HANDLE);
		var hin = GetStdHandle(STD_INPUT_HANDLE);
		if (GetConsoleMode(hout, out var omode))
			SetConsoleMode(hout, (omode | ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN) & ~ENABLE_WRAP_AT_EOL_OUTPUT);
		if (GetConsoleMode(hin, out var imode)) {
			imode |= ENABLE_EXTENDED_FLAGS | ENABLE_MOUSE_INPUT | ENABLE_WINDOW_INPUT | ENABLE_PROCESSED_INPUT;
			imode &= ~ENABLE_QUICK_EDIT_MODE;
			SetConsoleMode(hin, imode);
		}
		var font = new CONSOLE_FONT_INFOEX {
			FontHeight = 16,
			Weight = 400,
			FaceName = "Consolas",
		};
		font.cbSize = Marshal.SizeOf(font);
		SetCurrentConsoleFontEx(hout, false, ref font);
		try { SetConsoleTitle(Loc.T("charart.title") + " — " + name); } catch { }
		Console.Out.Write("\x1b[?1049h\x1b[?25l\x1b[?7l");
		Console.Out.Flush();
		var quit = false;
		ConsoleCancelEventHandler oncancel = (_, e) => { e.Cancel = true; quit = true; };
		Console.CancelKeyPress += oncancel;
		var mode = MODE_AUTO;
		double cellW = 1, cellH = 2, ox = 0, oy = 0;
		var fitted = false;
		var drag = false;
		var downX = 0;
		var downY = 0;
		var downOx = 0.0;
		var downOy = 0.0;
		var recs = new INPUT_RECORD[64];
		try {
			while (!quit) {
				if (!GetConsoleScreenBufferInfo(hout, out var info)) break;
				var cols = info.Window.Right - info.Window.Left + 1;
				var rows = info.Window.Bottom - info.Window.Top + 1;
				if (info.Size.X != cols || info.Size.Y != rows || info.Window.Left != 0 || info.Window.Top != 0) {
					fitbuffer(hout, cols, rows);
					if (!GetConsoleScreenBufferInfo(hout, out info)) break;
					cols = info.Window.Right - info.Window.Left + 1;
					rows = info.Window.Bottom - info.Window.Top + 1;
				}
				if (cols < 4) cols = 4;
				if (rows < 3) rows = 3;
				var picRows = rows - 1;
				if (!fitted) {
					Fit(w, h, cols, picRows, out cellW, out cellH, out ox, out oy);
					fitted = true;
				}
				clamp(w, h, cols, picRows, ref cellW, ref cellH, ref ox, ref oy);
				paint(px, w, h, cols, picRows, cellW, cellH, ox, oy, mode, name);
				if (!ReadConsoleInput(hin, recs, recs.Length, out var n) || n <= 0) break;
				for (var i = 0; i < n && !quit; i++) {
					var ev = recs[i];
					if (ev.EventType == SIZE_EVENT) continue;
					if (ev.EventType == MOUSE_EVENT) {
						var mx = ev.Mouse.X - info.Window.Left;
						var my = ev.Mouse.Y - info.Window.Top;
						if (mx < 0) mx = 0;
						if (my < 0) my = 0;
						if (mx >= cols) mx = cols - 1;
						if (my >= picRows) my = picRows - 1;
						if ((ev.Mouse.EventFlags & DOUBLE_CLICK) != 0) {
							Fit(w, h, cols, picRows, out cellW, out cellH, out ox, out oy);
							drag = false;
							continue;
						}
						if ((ev.Mouse.EventFlags & MOUSE_WHEELED) != 0) {
							var delta = (short)(ev.Mouse.ButtonState >> 16);
							var notches = delta / 120;
							if (notches == 0) notches = delta > 0 ? 1 : -1;
							var factor = Math.Pow(1.1, -notches);
							Zoom(cellW, cellH, ox, oy, mx, my, factor, out cellW, out cellH, out ox, out oy);
							continue;
						}
						var left = (ev.Mouse.ButtonState & FROM_LEFT_1ST) != 0;
						if ((ev.Mouse.EventFlags & MOUSE_MOVED) == 0 && left) {
							drag = true;
							downX = ev.Mouse.X;
							downY = ev.Mouse.Y;
							downOx = ox;
							downOy = oy;
						}
						else if (!left) drag = false;
						else if (drag) {
							var dx = ev.Mouse.X - downX;
							var dy = ev.Mouse.Y - downY;
							Pan(downOx, downOy, cellW, cellH, dx, dy, out ox, out oy);
						}
						continue;
					}
					if (ev.EventType != KEY_EVENT || ev.Key.KeyDown == 0) continue;
					var ch = ev.Key.Char;
					var rep = ev.Key.Repeat < 1 ? 1 : ev.Key.Repeat;
					if (ch == 'q' || ch == 'Q' || ev.Key.VirtualKey == 0x1B) { quit = true; break; }
					if (ch == '1') mode = MODE_FULL;
					else if (ch == '2') mode = MODE_HALF;
					else if (ch == '4') mode = MODE_QUARTER;
					else if (ch == '8') mode = MODE_EIGHTH;
					else if (ch == '0') mode = MODE_AUTO;
					else if (ch == 'f' || ch == 'F')
						Fit(w, h, cols, picRows, out cellW, out cellH, out ox, out oy);
					else if (ch == '+' || ch == '=')
						Zoom(cellW, cellH, ox, oy, cols / 2, picRows / 2, 1 / 1.1, out cellW, out cellH, out ox, out oy);
					else if (ch == '-' || ch == '_')
						Zoom(cellW, cellH, ox, oy, cols / 2, picRows / 2, 1.1, out cellW, out cellH, out ox, out oy);
					else if (ev.Key.VirtualKey == 0x25)
						Pan(ox, oy, cellW, cellH, -4 * rep, 0, out ox, out oy);
					else if (ev.Key.VirtualKey == 0x27)
						Pan(ox, oy, cellW, cellH, 4 * rep, 0, out ox, out oy);
					else if (ev.Key.VirtualKey == 0x26)
						Pan(ox, oy, cellW, cellH, 0, -2 * rep, out ox, out oy);
					else if (ev.Key.VirtualKey == 0x28)
						Pan(ox, oy, cellW, cellH, 0, 2 * rep, out ox, out oy);
				}
			}
		}
		finally {
			Console.CancelKeyPress -= oncancel;
			try {
				Console.Out.Write("\x1b[0m\x1b[?7h\x1b[?25h\x1b[?1049l");
				Console.Out.Flush();
			}
			catch { }
		}
	}

	static void paint(byte[] px, int w, int h, int cols, int picRows,
		double cellW, double cellH, double ox, double oy, int mode, string name) {
		var sb = new StringBuilder(cols * picRows * 12 + 80);
		var fg = -2;
		var bg = -2;
		sb.Append("\x1b[H");
		for (var y = 0; y < picRows; y++) {
			var y0 = oy + y * cellH;
			var y1 = y0 + cellH;
			sb.Append($"\x1b[{y + 1};1H");
			for (var x = 0; x < cols; x++) {
				var x0 = ox + x * cellW;
				var cell = Pick(px, w, h, x0, x0 + cellW, y0, y1, mode);
				if (cell.Fg < 0) {
					if (fg != -1 || bg != -1) {
						sb.Append("\x1b[0m");
						fg = -1;
						bg = -1;
					}
					sb.Append(' ');
					continue;
				}
				if (cell.Fg != fg) {
					sb.Append($"\x1b[38;2;{red(cell.Fg)};{green(cell.Fg)};{blue(cell.Fg)}m");
					fg = cell.Fg;
				}
				if (cell.Bg != bg) {
					sb.Append($"\x1b[48;2;{red(cell.Bg)};{green(cell.Bg)};{blue(cell.Bg)}m");
					bg = cell.Bg;
				}
				sb.Append(cell.Ch);
			}
		}
		Fit(w, h, cols, picRows, out var fitW, out _, out _, out _);
		var zoom = fitW / cellW;
		if (zoom < 0.01) zoom = 0.01;
		var label = Loc.T("charart.view", $"{zoom:0.00}x", modename(mode), name ?? "");
		sb.Append($"\x1b[{picRows + 1};1H\x1b[0m\x1b[7m");
		sb.Append(fitwidth(label, cols));
		sb.Append("\x1b[0m");
		Console.Out.Write(sb.ToString());
		Console.Out.Flush();
	}

	static string modename(int mode) {
		if (mode == MODE_FULL) return Loc.T("charart.mode.1");
		if (mode == MODE_HALF) return Loc.T("charart.mode.2");
		if (mode == MODE_QUARTER) return Loc.T("charart.mode.4");
		if (mode == MODE_EIGHTH) return Loc.T("charart.mode.8");
		return Loc.T("charart.mode.0");
	}

	static void clamp(int w, int h, int cols, int rows, ref double cellW, ref double cellH, ref double ox, ref double oy) {
		var maxW = Math.Max(w, h);
		if (maxW < 1) maxW = 1;
		if (cellW > maxW) cellW = maxW;
		if (cellW < 1.0 / 64) cellW = 1.0 / 64;
		cellH = cellW * CELL_ASPECT;
		var viewW = cols * cellW;
		var viewH = rows * cellH;
		if (ox < -viewW + cellW) ox = -viewW + cellW;
		if (oy < -viewH + cellH) oy = -viewH + cellH;
		if (ox > w - cellW) ox = w - cellW;
		if (oy > h - cellH) oy = h - cellH;
	}

	static void half(int[] s, out char ch, out int fg, out int bg) {
		var top = new int[8];
		var bot = new int[8];
		for (var i = 0; i < 8; i++) {
			top[i] = s[i];
			bot[i] = s[8 + i];
		}
		fg = avg(top, 0, 8);
		bg = avg(bot, 0, 8);
		ch = '\u2580';
		if (fg < 0 && bg >= 0) { ch = '\u2584'; fg = bg; bg = 0; }
	}

	static void autosplit(int[] s, out char ch, out int fg, out int bg) {
		quarter(s, out var qch, out var qfg, out var qbg);
		eighth(s, out var ech, out var efg, out var ebg);
		var k = eighthof(ech);
		// 正中的半块四分块已经能画。只有边界落在 1/8～3/8 或 5/8～7/8，而且确实更贴时才换。
		if (k >= 1 && k <= 7 && k != 4 && efg >= 0 && ebg >= 0 && dist(efg, ebg) >= SPLIT_DIST
			&& glypherr(s, ech, efg, ebg) < glypherr(s, qch, qfg, qbg)) {
			ch = ech;
			fg = efg;
			bg = ebg;
			return;
		}
		ch = qch;
		fg = qfg;
		bg = qbg;
	}

	static long glypherr(int[] s, char ch, int fg, int bg) {
		if (fg < 0) return long.MaxValue / 4;
		if (bg < 0) bg = fg;
		var k = eighthof(ch);
		if (k >= 0) return sideerr(s, 0, k, fg) + sideerr(s, k, 8, bg);
		var mask = QUAD.IndexOf(ch);
		if (mask <= 0) return sideerr(s, 0, 8, bg);
		long e = 0;
		for (var q = 0; q < 4; q++) {
			var color = (mask & (1 << q)) != 0 ? fg : bg;
			var row = q / 2;
			var col0 = (q % 2) * 4;
			for (var c = 0; c < 4; c++) {
				var i = row * 8 + col0 + c;
				if (s[i] >= 0) e += dist(s[i], color);
			}
		}
		return e;
	}

	static int eighthof(char ch) {
		if (ch == ' ') return 0;
		if (ch == '\u2588') return 8;
		if (ch >= '\u2589' && ch <= '\u258f') return 0x2590 - ch;
		return -1;
	}

	static void eighth(int[] s, out char ch, out int fg, out int bg) {
		var col = new int[8];
		for (var i = 0; i < 8; i++) col[i] = mix(s[i], s[8 + i]);
		fg = avg(col, 0, 8);
		bg = fg;
		var bestE = sideerr(s, 0, 8, fg);
		var bestK = 8;
		for (var k = 1; k <= 7; k++) {
			var left = avg(col, 0, k);
			var right = avg(col, k, 8);
			if (left < 0 || right < 0) continue;
			var e = sideerr(s, 0, k, left) + sideerr(s, k, 8, right);
			if (e < bestE) {
				bestE = e;
				bestK = k;
				fg = left;
				bg = right;
			}
		}
		ch = Eighth(bestK);
	}

	static long sideerr(int[] s, int a, int b, int color) {
		if (color < 0) return 0;
		long e = 0;
		for (var c = a; c < b; c++) {
			if (s[c] >= 0) e += dist(s[c], color);
			if (s[8 + c] >= 0) e += dist(s[8 + c], color);
		}
		return e;
	}

	static void quarter(int[] s, out char ch, out int fg, out int bg) {
		var q = new int[4];
		q[0] = avg4(s, 0, 0);
		q[1] = avg4(s, 0, 4);
		q[2] = avg4(s, 1, 0);
		q[3] = avg4(s, 1, 4);
		var bi = 0;
		var bj = 1;
		long best = -1;
		for (var i = 0; i < 4; i++) {
			if (q[i] < 0) continue;
			for (var j = i + 1; j < 4; j++) {
				if (q[j] < 0) continue;
				var d = dist(q[i], q[j]);
				if (d > best) { best = d; bi = i; bj = j; }
			}
		}
		if (best < SPLIT_DIST) {
			fg = avg(q, 0, 4);
			bg = fg;
			ch = ' ';
			return;
		}
		var mask = 0;
		long nr = 0, ng = 0, nb = 0, mr = 0, mg = 0, mb = 0;
		var n = 0;
		var m = 0;
		for (var i = 0; i < 4; i++) {
			if (q[i] < 0) continue;
			if (dist(q[i], q[bi]) <= dist(q[i], q[bj])) {
				mask |= 1 << i;
				nr += red(q[i]); ng += green(q[i]); nb += blue(q[i]); n++;
			} else {
				mr += red(q[i]); mg += green(q[i]); mb += blue(q[i]); m++;
			}
		}
		if (n == 0 || m == 0) {
			fg = avg(q, 0, 4);
			bg = fg;
			ch = ' ';
			return;
		}
		fg = (int)((nr / n) << 16) | (int)((ng / n) << 8) | (int)(nb / n);
		bg = (int)((mr / m) << 16) | (int)((mg / m) << 8) | (int)(mb / m);
		ch = mask == 0 || mask == 15 ? ' ' : QUAD[mask];
	}

	static int avg4(int[] s, int row, int col0) {
		var tmp = new int[4];
		for (var i = 0; i < 4; i++) tmp[i] = s[row * 8 + col0 + i];
		return avg(tmp, 0, 4);
	}

	static int area(byte[] px, int w, int h, double x0, double x1, double y0, double y1) {
		if (x1 < x0) { var t = x0; x0 = x1; x1 = t; }
		if (y1 < y0) { var t = y0; y0 = y1; y1 = t; }
		var xa = (int)Math.Floor(x0);
		var xb = (int)Math.Floor(x1 - 1e-6);
		var ya = (int)Math.Floor(y0);
		var yb = (int)Math.Floor(y1 - 1e-6);
		if (xa < 0) xa = 0;
		if (ya < 0) ya = 0;
		if (xb >= w) xb = w - 1;
		if (yb >= h) yb = h - 1;
		if (xa > xb || ya > yb) return -1;
		long r = 0, g = 0, b = 0;
		var n = 0;
		for (var y = ya; y <= yb; y++) {
			for (var x = xa; x <= xb; x++) {
				var i = (y * w + x) * 4;
				if (i < 0 || i + 3 >= px.Length) continue;
				if (px[i + 3] < 16) continue;
				b += px[i];
				g += px[i + 1];
				r += px[i + 2];
				n++;
			}
		}
		if (n == 0) return -1;
		return ((int)(r / n) << 16) | ((int)(g / n) << 8) | (int)(b / n);
	}

	static int sample(byte[] px, int w, int h, double x, double y) {
		if (x < 0 || y < 0 || x >= w || y >= h) return -1;
		var ix = (int)x;
		var iy = (int)y;
		if (ix >= w) ix = w - 1;
		if (iy >= h) iy = h - 1;
		var i = (iy * w + ix) * 4;
		if (i < 0 || i + 3 >= px.Length) return -1;
		if (px[i + 3] < 16) return -1;
		return (px[i + 2] << 16) | (px[i + 1] << 8) | px[i];
	}

	static int avg(int[] c, int a, int b) {
		long r = 0, g = 0, bl = 0;
		var n = 0;
		for (var i = a; i < b; i++) {
			if (c[i] < 0) continue;
			r += red(c[i]);
			g += green(c[i]);
			bl += blue(c[i]);
			n++;
		}
		if (n == 0) return -1;
		return ((int)(r / n) << 16) | ((int)(g / n) << 8) | (int)(bl / n);
	}

	static int mix(int a, int b) {
		if (a < 0) return b;
		if (b < 0) return a;
		return (((red(a) + red(b)) / 2) << 16) | (((green(a) + green(b)) / 2) << 8) | ((blue(a) + blue(b)) / 2);
	}

	static int gridrows(int w, int h, int cols) {
		var rows = (int)Math.Round(h / (double)w * cols / CELL_ASPECT);
		if (rows < 1) rows = 1;
		if (rows > 400) rows = 400;
		return rows;
	}

	static int lumof(int c, bool invert) {
		if (c < 0) return 255;
		var lum = (red(c) * 2126 + green(c) * 7152 + blue(c) * 722) / 10000;
		if (invert) lum = 255 - lum;
		return lum;
	}

	/// <summary>阈值 0 仍保留很淡的边。返回 4～255，越大越只留更明显的线。</summary>
	static int linecut(int threshold) {
		var cut = 4 + threshold;
		if (cut > 255) cut = 255;
		return cut;
	}

	/// <summary>四边中点比同边两角都暗多少，够 cut 就在 mask 上记一位：1 上、2 下、4 左、8 右。斜线擦过角落里的一点时，两角里总有一个同样暗，不会当成线从那边出去。</summary>
	static int edgemask(int[] s, int cut) {
		var mask = 0;
		if (edgeof(s, 0, 1, 2) >= cut) mask |= E_U;
		if (edgeof(s, 6, 7, 8) >= cut) mask |= E_D;
		if (edgeof(s, 0, 3, 6) >= cut) mask |= E_L;
		if (edgeof(s, 2, 5, 8) >= cut) mask |= E_R;
		return mask;
	}

	/// <summary>同边两角平均减去中点亮度：中点是线、两角是背景时为正。</summary>
	static int edgeof(int[] s, int a, int mid, int c) => (lit(s, a) + lit(s, c)) / 2 - lit(s, mid);

	/// <summary>格子里的连接方向。两条斜线的明暗差比横竖都大时按斜线算，此时返回 0，方向写在 diag：1 是 /，2 是 \。</summary>
	static int cellmask(int[] s, int cut, out byte diag) {
		diag = 0;
		int dark(int a, int b, int c) => 765 - (lit(s, a) + lit(s, b) + lit(s, c));
		var top = dark(0, 1, 2);
		var mid = dark(3, 4, 5);
		var bot = dark(6, 7, 8);
		var lf = dark(0, 3, 6);
		var ce = dark(1, 4, 7);
		var rt = dark(2, 5, 8);
		var hScore = Math.Max(top, Math.Max(mid, bot)) - Math.Min(top, Math.Min(mid, bot));
		var vScore = Math.Max(lf, Math.Max(ce, rt)) - Math.Min(lf, Math.Min(ce, rt));
		var slash = dark(2, 4, 6);
		var back = dark(0, 4, 8);
		var d = Math.Abs(slash - back);
		if (d >= hScore && d >= vScore && d >= cut * 3) {
			diag = slash > back ? (byte)1 : (byte)2;
			return 0;
		}
		return edgemask(s, cut);
	}

	/// <summary>横线的上（下）行、竖线的左（右）列也整排暗，说明线更宽，用粗线字符。</summary>
	static bool thickline(int[] s, int mask, int cut) {
		var hi = 0;
		for (var i = 0; i < 9; i++) {
			var v = lit(s, i);
			if (v > hi) hi = v;
		}
		var span = cut * THICK_SPAN;
		bool dark(int i) => hi - lit(s, i) >= span;
		if ((mask & (E_L | E_R)) != 0 && (dark(0) && dark(1) && dark(2) || dark(6) && dark(7) && dark(8)))
			return true;
		if ((mask & (E_U | E_D)) != 0 && (dark(0) && dark(3) && dark(6) || dark(2) && dark(5) && dark(8)))
			return true;
		return false;
	}

	/// <summary>按连接方向选字符。基本是 - | / \ +；丰富 ASCII 另有 = ! ( ) [ ] v ^ &lt; &gt; #；制表符用 ─ │ ┌ ┐ └ ┘ ├ ┤ ┬ ┴ ┼，粗线换双线。</summary>
	static char lineglyph(int mask, bool thick, int lineset) {
		if (lineset == LINES_BOX) {
			switch (mask) {
				case E_L | E_R: return thick ? '═' : '─';
				case E_U | E_D: return thick ? '║' : '│';
				case E_R | E_D: return thick ? '╔' : '┌';
				case E_L | E_D: return thick ? '╗' : '┐';
				case E_R | E_U: return thick ? '╚' : '└';
				case E_L | E_U: return thick ? '╝' : '┘';
				case E_L | E_R | E_D: return thick ? '╦' : '┬';
				case E_L | E_R | E_U: return thick ? '╩' : '┴';
				case E_U | E_D | E_R: return thick ? '╠' : '├';
				case E_U | E_D | E_L: return thick ? '╣' : '┤';
				case E_L | E_R | E_U | E_D: return thick ? '╬' : '┼';
				case E_L: return '╴';
				case E_R: return '╶';
				case E_U: return '╵';
				case E_D: return '╷';
			}
			return ' ';
		}
		if (lineset == LINES_ASCII) {
			switch (mask) {
				case E_L | E_R: return thick ? '=' : '-';
				case E_U | E_D: return thick ? '!' : '|';
				case E_R | E_D: return '(';
				case E_L | E_D: return ')';
				case E_R | E_U: return '[';
				case E_L | E_U: return ']';
				case E_L | E_R | E_U | E_D: return '#';
				case E_L | E_R | E_D: return 'v';
				case E_L | E_R | E_U: return '^';
				case E_U | E_D | E_R: return '>';
				case E_U | E_D | E_L: return '<';
				case E_L:
				case E_R: return '-';
				case E_U:
				case E_D: return '|';
			}
			return ' ';
		}
		var h = (mask & (E_L | E_R)) != 0;
		var v = (mask & (E_U | E_D)) != 0;
		if (h && v) return '+';
		if (h) return '-';
		if (v) return '|';
		return ' ';
	}

	static char diagchar(byte d, int lineset) {
		if (lineset == LINES_BOX) return d == 1 ? '╱' : '╲';
		return d == 1 ? '/' : '\\';
	}

	/// <summary>格子内部没有对比时，看比相邻格子更暗的一侧。线正好落在交界上也会画出来，而且只占更暗的那一格。</summary>
	static int edgeseam(int[] lum, int x, int y, int cols, int rows, int cut, out byte diag) {
		diag = 0;
		int at(int xx, int yy) {
			if (xx < 0) xx = 0;
			if (yy < 0) yy = 0;
			if (xx >= cols) xx = cols - 1;
			if (yy >= rows) yy = rows - 1;
			return lum[yy * cols + xx];
		}
		var here = at(x, y);
		int brighter(int xx, int yy) {
			var d = at(xx, yy) - here;
			if (d < 0) return 0;
			return d;
		}
		var ax = Math.Max(brighter(x - 1, y), brighter(x + 1, y));
		var ay = Math.Max(brighter(x, y - 1), brighter(x, y + 1));
		var slash = Math.Max(brighter(x + 1, y - 1), brighter(x - 1, y + 1));
		var back = Math.Max(brighter(x - 1, y - 1), brighter(x + 1, y + 1));
		var span = Math.Abs(slash - back);
		if (ax >= cut && ay >= cut && ax * 3 >= span * 2 && ay * 3 >= span * 2)
			return E_L | E_R | E_U | E_D;
		if (span >= ax && span >= ay && span >= cut) {
			diag = slash > back ? (byte)1 : (byte)2;
			return 0;
		}
		if (ay >= ax && ay >= cut) return E_L | E_R;
		if (ax >= cut) return E_U | E_D;
		return 0;
	}

	static int lit(int[] s, int i) => s[i] < 0 ? 255 : s[i];

	static int countch(string s, char ch) {
		var n = 0;
		if (s == null) return 0;
		foreach (var c in s)
			if (c == ch) n++;
		return n;
	}

	static void paintrow(byte[] px, int w, int y, int r, int g, int b) {
		for (var x = 0; x < w; x++) {
			var i = (y * w + x) * 4;
			px[i] = (byte)b;
			px[i + 1] = (byte)g;
			px[i + 2] = (byte)r;
			px[i + 3] = 255;
		}
	}

	static void paintcol(byte[] px, int w, int h, int x, int r, int g, int b) {
		for (var y = 0; y < h; y++) {
			var i = (y * w + x) * 4;
			px[i] = (byte)b;
			px[i + 1] = (byte)g;
			px[i + 2] = (byte)r;
			px[i + 3] = 255;
		}
	}

	static void ink(byte[] px, int w, int x, int y) {
		var i = (y * w + x) * 4;
		px[i] = 0;
		px[i + 1] = 0;
		px[i + 2] = 0;
		px[i + 3] = 255;
	}

	static void paintdiag(byte[] px, int cols, int rows, int cw, int ch, bool slash) {
		var w = cols * cw;
		for (var cy = 0; cy < rows; cy++) {
			for (var cx = 0; cx < cols; cx++) {
				for (var t = 0; t < cw; t++) {
					var x = cx * cw + t;
					var ly = slash ? (ch - 1 - t * (ch - 1) / (cw - 1)) : t * (ch - 1) / (cw - 1);
					ink(px, w, x, cy * ch + ly);
					if (ly + 1 < ch) ink(px, w, x, cy * ch + ly + 1);
				}
			}
		}
	}

	static int red(int c) => (c >> 16) & 255;
	static int green(int c) => (c >> 8) & 255;
	static int blue(int c) => c & 255;

	static void fitbuffer(IntPtr hout, int cols, int rows) {
		if (cols < 4) cols = 4;
		if (rows < 3) rows = 3;
		var rect = new SMALL_RECT { Left = 0, Top = 0, Right = (short)(cols - 1), Bottom = (short)(rows - 1) };
		SetConsoleWindowInfo(hout, true, ref rect);
		SetConsoleScreenBufferSize(hout, new COORD { X = (short)cols, Y = (short)rows });
	}

	static long dist(int a, int b) {
		var dr = red(a) - red(b);
		var dg = green(a) - green(b);
		var db = blue(a) - blue(b);
		return dr * dr + dg * dg + db * db;
	}

	static byte[] solid(int w, int h, int r, int g, int b) {
		var px = new byte[w * h * 4];
		fill(px, w, h, 0, w, r, g, b);
		return px;
	}

	static void fill(byte[] px, int w, int h, int x0, int x1, int r, int g, int b) {
		for (var y = 0; y < h; y++) {
			for (var x = x0; x < x1 && x < w; x++) {
				var i = (y * w + x) * 4;
				px[i] = (byte)b;
				px[i + 1] = (byte)g;
				px[i + 2] = (byte)r;
				px[i + 3] = 255;
			}
		}
	}

	static bool isascii(string s) {
		foreach (var ch in s)
			if (ch != '\n' && ch > 127) return false;
		return true;
	}

	static string quote(string path) {
		if (path.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return path;
		return "\"" + path.Replace("\"", "\\\"") + "\"";
	}

	static string fitwidth(string s, int cols) {
		if (cols < 1 || string.IsNullOrEmpty(s)) return new string(' ', Math.Max(cols, 0));
		var sb = new StringBuilder();
		var n = 0;
		foreach (var ch in s) {
			var wch = ch < 128 ? 1 : 2;
			if (n + wch > cols) break;
			sb.Append(ch);
			n += wch;
		}
		while (n < cols) { sb.Append(' '); n++; }
		return sb.ToString();
	}

	static void pause() {
		try { Console.ReadKey(true); } catch { }
	}
}
