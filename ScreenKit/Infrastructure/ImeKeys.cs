namespace ScreenKit;

static class Vk {
	public const int Shift = 0x10;
	public const int LShift = 0xA0;
	public const int RShift = 0xA1;
	public const int Ctrl = 0x11;
	public const int Alt = 0x12;
	public const int Caps = 0x14;
	public const int Space = 0x20;
	public const int Enter = 0x0D;
	public const int Esc = 0x1B;
	public const int Back = 0x08;
	public const int Tab = 0x09;
	public const int PageUp = 0x21;
	public const int PageDown = 0x22;
	public const int Left = 0x25;
	public const int Up = 0x26;
	public const int Right = 0x27;
	public const int Down = 0x28;
	public const int D0 = 0x30;
	public const int A = 0x41;
	public const int F = 0x46;
	public const int F7 = 0x76;
	public const int LWin = 0x5B;
	public const int RWin = 0x5C;
	public const int Oem1 = 0xBA;
	public const int OemPlus = 0xBB;
	public const int OemComma = 0xBC;
	public const int OemMinus = 0xBD;
	public const int OemPeriod = 0xBE;
	public const int Oem2 = 0xBF;
	public const int Oem3 = 0xC0;
	public const int Oem4 = 0xDB;
	public const int Oem5 = 0xDC;
	public const int Oem6 = 0xDD;
	public const int Oem7 = 0xDE;

	public static bool IsShift(int vk) => vk == Shift || vk == LShift || vk == RShift;
	public static bool IsLetter(int vk) => vk >= A && vk <= 0x5A;
	public static bool IsDigit(int vk) => vk >= D0 && vk <= 0x39;
	public static bool IsArrow(int vk) => vk >= Left && vk <= Down;
}

static class Punct {
	public static string Wubi(int vk, bool shift, ref bool q1, ref bool q2) {
		if (!shift && vk == Vk.OemComma) return "，";
		if (shift && vk == Vk.OemComma) return "《";
		if (!shift && vk == Vk.OemPeriod) return "。";
		if (shift && vk == Vk.OemPeriod) return "》";
		if (!shift && vk == Vk.Oem5) return "、";
		if (shift && vk == Vk.Oem5) return "|";
		if (!shift && vk == Vk.Oem1) return "；";
		if (shift && vk == Vk.Oem1) return "：";
		if (!shift && vk == Vk.Oem7) { q1 = !q1; return q1 ? "‘" : "’"; }
		if (shift && vk == Vk.Oem7) { q2 = !q2; return q2 ? "“" : "”"; }
		if (!shift && vk == Vk.Oem2) return "/";
		if (shift && vk == Vk.Oem2) return "？";
		if (!shift && vk == Vk.Oem4) return "【";
		if (shift && vk == Vk.Oem4) return "『";
		if (!shift && vk == Vk.Oem6) return "】";
		if (shift && vk == Vk.Oem6) return "』";
		if (!shift && vk == Vk.Oem3) return "·";
		if (shift && vk == Vk.Oem3) return "～";
		if (shift && vk == Vk.OemMinus) return "——";
		if (shift && vk == Vk.OemPlus) return "+";
		if (shift && vk == Vk.D0 + 1) return "！";
		if (shift && vk == Vk.D0 + 2) return "@";
		if (shift && vk == Vk.D0 + 3) return "#";
		if (shift && vk == Vk.D0 + 4) return "￥";
		if (shift && vk == Vk.D0 + 5) return "%";
		if (shift && vk == Vk.D0 + 6) return "……";
		if (shift && vk == Vk.D0 + 7) return "&";
		if (shift && vk == Vk.D0 + 8) return "*";
		if (shift && vk == Vk.D0 + 9) return "（";
		if (shift && vk == Vk.D0) return "）";
		return null;
	}

	public static string Half(int vk, bool shift) {
		if (Vk.IsLetter(vk)) return VkChar(vk, shift).ToString();
		if (Vk.IsDigit(vk) && !shift) return ((char)('0' + vk - Vk.D0)).ToString();
		if (shift && vk == Vk.D0 + 1) return "!";
		if (shift && vk == Vk.D0 + 2) return "@";
		if (shift && vk == Vk.D0 + 3) return "#";
		if (shift && vk == Vk.D0 + 4) return "$";
		if (shift && vk == Vk.D0 + 5) return "%";
		if (shift && vk == Vk.D0 + 6) return "^";
		if (shift && vk == Vk.D0 + 7) return "&";
		if (shift && vk == Vk.D0 + 8) return "*";
		if (shift && vk == Vk.D0 + 9) return "(";
		if (shift && vk == Vk.D0) return ")";
		if (!shift && vk == Vk.OemComma) return ",";
		if (shift && vk == Vk.OemComma) return "<";
		if (!shift && vk == Vk.OemPeriod) return ".";
		if (shift && vk == Vk.OemPeriod) return ">";
		if (!shift && vk == Vk.OemMinus) return "-";
		if (shift && vk == Vk.OemMinus) return "_";
		if (!shift && vk == Vk.OemPlus) return "=";
		if (shift && vk == Vk.OemPlus) return "+";
		if (!shift && vk == Vk.Oem1) return ";";
		if (shift && vk == Vk.Oem1) return ":";
		if (!shift && vk == Vk.Oem7) return "'";
		if (shift && vk == Vk.Oem7) return "\"";
		if (!shift && vk == Vk.Oem2) return "/";
		if (shift && vk == Vk.Oem2) return "?";
		if (!shift && vk == Vk.Oem4) return "[";
		if (shift && vk == Vk.Oem4) return "{";
		if (!shift && vk == Vk.Oem6) return "]";
		if (shift && vk == Vk.Oem6) return "}";
		if (!shift && vk == Vk.Oem5) return "\\";
		if (shift && vk == Vk.Oem5) return "|";
		if (!shift && vk == Vk.Oem3) return "`";
		if (shift && vk == Vk.Oem3) return "~";
		return null;
	}

	public static string Ja(int vk, bool shift) {
		if (!shift && vk == Vk.OemComma) return "、";
		if (!shift && vk == Vk.OemPeriod) return "。";
		if (shift && vk == Vk.Oem2) return "？";
		if (shift && vk == Vk.D0 + 1) return "！";
		return Half(vk, shift);
	}

	public static char VkChar(int vk, bool shift) {
		char c = (char)('a' + vk - Vk.A);
		return shift ? char.ToUpper(c) : c;
	}
}
