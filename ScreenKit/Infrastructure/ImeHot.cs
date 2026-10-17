using System.Collections.Generic;

namespace ScreenKit;

class Hot {
	public int Vk;
	public bool Ctrl;
	public bool Alt;
	public bool Shift;
	public bool Win;

	public bool Ok => Vk != 0;

	public bool Hit(int vk, bool shift, bool ctrl, bool alt, bool win) {
		return Ok && vk == Vk && shift == Shift && ctrl == Ctrl && alt == Alt && win == Win;
	}

	public bool Same(Hot other) {
		if (other == null || !Ok || !other.Ok) return false;
		return Vk == other.Vk && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift && Win == other.Win;
	}

	public static Hot Of(bool ctrl, bool alt, bool shift, bool win, int vk) {
		return new Hot { Ctrl = ctrl, Alt = alt, Shift = shift, Win = win, Vk = vk };
	}

	public string Save() {
		if (!Ok) return "";
		var flags = "";
		if (Ctrl) flags += "c";
		if (Alt) flags += "a";
		if (Shift) flags += "s";
		if (Win) flags += "w";
		return flags + ":" + Vk;
	}

	public static Hot FromCombo(string text) {
		uint mod, vk;
		if (!GlobalHotkey.tryparse(text, out mod, out vk)) return new Hot();
		return Of((mod & 2) != 0, (mod & 1) != 0, (mod & 4) != 0, (mod & 8) != 0, (int)vk);
	}

	public static Hot Parse(string text) {
		var hot = new Hot();
		if (string.IsNullOrEmpty(text)) return hot;
		int colon = text.IndexOf(':');
		if (colon <= 0) return hot;
		int vk;
		if (!int.TryParse(text.Substring(colon + 1), out vk) || vk <= 0) return hot;
		var flags = text.Substring(0, colon);
		hot.Vk = vk;
		hot.Ctrl = flags.IndexOf('c') >= 0;
		hot.Alt = flags.IndexOf('a') >= 0;
		hot.Shift = flags.IndexOf('s') >= 0;
		hot.Win = flags.IndexOf('w') >= 0;
		return hot;
	}

	public string Label() {
		if (!Ok) return "未设置";
		var parts = new List<string>();
		if (Ctrl) parts.Add("Ctrl");
		if (Alt) parts.Add("Alt");
		if (Shift) parts.Add("Shift");
		if (Win) parts.Add("Win");
		parts.Add(Name(Vk));
		return string.Join("+", parts.ToArray());
	}

	public static string Name(int key) {
		if (key >= 0x30 && key <= 0x39) return ((char)key).ToString();
		if (key >= 0x60 && key <= 0x69) return "小键盘" + (key - 0x60);
		if (key >= 0x41 && key <= 0x5A) return ((char)key).ToString();
		if (key >= 0x70 && key <= 0x87) return "F" + (key - 0x6F);
		if (key == 0x20) return "空格";
		if (key == 0xC0) return "`";
		if (key == 0x09) return "Tab";
		if (key == 0x1B) return "Esc";
		return "键" + key;
	}
}
