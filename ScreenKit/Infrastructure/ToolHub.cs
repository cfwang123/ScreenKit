using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>
/// 工具页的一张卡片。Kind 决定双击后怎么打开：
/// web 走浏览器工具页，tab 切到主界面已有的页，act 调主窗口里的打开方法。
/// </summary>
sealed class ToolCard {
	public string Id;
	public string Cat;
	public string Kind;
	public string Open;
	public string NameKey;
	public string TipKey;
	public string Glyph;
	public string Search;
}

/// <summary>主界面「工具」页和菜单共用的工具目录。</summary>
static class ToolHub {
	public const string Fav = "fav";
	public const string All = "all";

	public static readonly (string Id, string NameKey, string Glyph)[] Cats = {
		(Fav, "web.cat.fav", "\uE734"),
		(All, "web.cat.all", "\uE8FD"),
		("text", "web.cat.text", "\uE8D2"),
		("calc", "web.cat.calc", "\uE8EF"),
		("speech", "web.cat.speech", "\uE767"),
		("time", "web.cat.time", "\uE787"),
		("image", "web.cat.image", "\uEB9F"),
		("info", "web.cat.info", "\uE946"),
		("talk", "web.cat.talk", "\uE8F2"),
		("net", "menu.netgroup", "\uE839"),
		("files", "web.cat.files", "\uE8B7"),
		("sys", "menu.cat.sys", "\uE770"),
	};

	// WPF 工具。顺序跟「工具」菜单一致，图标用 Segoe MDL2。
	static readonly ToolCard[] desktop = {
		new() { Id = "w-trpopup", Cat = "text", Kind = "act", Open = "trpopup", NameKey = "menu.translate.popup", TipKey = "menu.translate.popup.tip", Glyph = "\uE8C1" },
		new() { Id = "w-texttool", Cat = "text", Kind = "act", Open = "texttool", NameKey = "menu.texttool", TipKey = "menu.texttool.tip", Glyph = "\uE8C1" },
		new() { Id = "w-mdread", Cat = "text", Kind = "act", Open = "mdread", NameKey = "menu.mdread", TipKey = "menu.mdread.tip", Glyph = "\uE736" },
		new() { Id = "w-doc2md", Cat = "text", Kind = "act", Open = "doc2md", NameKey = "menu.doc2md", TipKey = "menu.doc2md.tip", Glyph = "\uE8A5" },
		new() { Id = "w-pwgen", Cat = "text", Kind = "act", Open = "pwgen", NameKey = "menu.pwgen", TipKey = "menu.pwgen.tip", Glyph = "\uE8D7" },
		new() { Id = "w-note", Cat = "text", Kind = "act", Open = "note", NameKey = "menu.note", TipKey = "menu.note.tip", Glyph = "\uE70B" },
		new() { Id = "w-zhconv", Cat = "text", Kind = "act", Open = "zhconv", NameKey = "menu.zhconv", TipKey = "menu.zhconv.tip", Glyph = "\uE8C1" },
		new() { Id = "w-pinyin", Cat = "text", Kind = "act", Open = "pinyin", NameKey = "menu.pinyin", TipKey = "menu.pinyin.tip", Glyph = "\uE8C1" },
		new() { Id = "w-jpyomi", Cat = "text", Kind = "act", Open = "jpyomi", NameKey = "menu.jpyomi", TipKey = "menu.jpyomi.tip", Glyph = "\uE8C1" },
		new() { Id = "w-hash", Cat = "text", Kind = "act", Open = "hash", NameKey = "menu.hash", TipKey = "menu.hash.tip", Glyph = "\uE73E" },
		new() { Id = "w-scicalc", Cat = "calc", Kind = "act", Open = "scicalc", NameKey = "menu.scicalc", TipKey = "menu.scicalc.tip", Glyph = "\uE8EF" },
		new() { Id = "w-voicerec", Cat = "speech", Kind = "act", Open = "voicerec", NameKey = "menu.voicerec", TipKey = "menu.voicerec.tip", Glyph = "\uE720" },
		new() { Id = "w-midi", Cat = "speech", Kind = "act", Open = "midi", NameKey = "menu.midisynth", TipKey = "menu.midisynth.tip", Glyph = "\uE189" },
		new() { Id = "w-wincal", Cat = "time", Kind = "act", Open = "wincal", NameKey = "menu.wincal", TipKey = "menu.wincal.tip", Glyph = "\uE787" },
		new() { Id = "w-pdf", Cat = "image", Kind = "act", Open = "pdf", NameKey = "menu.pdf", TipKey = "menu.pdf.tip", Glyph = "\uEA90" },
		new() { Id = "w-imgconv", Cat = "image", Kind = "act", Open = "imgconv", NameKey = "menu.imgconv", TipKey = "menu.imgconv.tip", Glyph = "\uEB9F" },
		new() { Id = "w-charart", Cat = "image", Kind = "act", Open = "charart", NameKey = "menu.charart", TipKey = "menu.charart.tip", Glyph = "\uE8D2" },
		new() { Id = "w-pixel", Cat = "image", Kind = "act", Open = "pixel", NameKey = "menu.pixel", TipKey = "menu.pixel.tip", Glyph = "\uE91B" },
		new() { Id = "w-vidconv", Cat = "image", Kind = "act", Open = "vidconv", NameKey = "menu.vidconv", TipKey = "menu.vidconv.tip", Glyph = "\uE714" },
		new() { Id = "w-vidgif", Cat = "image", Kind = "act", Open = "vidgif", NameKey = "menu.vidgif", TipKey = "menu.vidgif.tip", Glyph = "\uE714" },
		new() { Id = "w-qrmake", Cat = "image", Kind = "act", Open = "qrmake", NameKey = "menu.qrmake", TipKey = "menu.qrmake.tip", Glyph = "\uED14" },
		new() { Id = "w-cam", Cat = "image", Kind = "act", Open = "cam", NameKey = "menu.cam", TipKey = "menu.cam.tip", Glyph = "\uE722" },
		new() { Id = "w-rtspsrv", Cat = "image", Kind = "act", Open = "rtspsrv", NameKey = "menu.rtspsrv", TipKey = "menu.rtspsrv.tip", Glyph = "\uE7F4" },
		new() { Id = "w-glyphmod", Cat = "image", Kind = "act", Open = "glyphmod", NameKey = "menu.glyphmod", TipKey = "menu.glyphmod.tip", Glyph = "\uE8D2" },
		new() { Id = "w-cast", Cat = "image", Kind = "tab", Open = "cast", NameKey = "menu.cast", TipKey = "menu.cast.tip", Glyph = "\uE7F4" },
		new() { Id = "w-dict", Cat = "info", Kind = "tab", Open = "dict", NameKey = "menu.dict", TipKey = "menu.dict.tip", Glyph = "\uE82D" },
		new() { Id = "w-net", Cat = "net", Kind = "act", Open = "net", NameKey = "menu.net", TipKey = "menu.net.tip", Glyph = "\uE839" },
		new() { Id = "w-httpdbg", Cat = "net", Kind = "act", Open = "httpdbg", NameKey = "menu.httpdbg", TipKey = "menu.httpdbg.tip", Glyph = "\uE774" },
		new() { Id = "w-mqtt", Cat = "net", Kind = "act", Open = "mqtt", NameKey = "menu.mqtt", TipKey = "menu.mqtt.tip", Glyph = "\uEC05" },
		new() { Id = "w-udpdbg", Cat = "net", Kind = "act", Open = "udpdbg", NameKey = "menu.udpdbg", TipKey = "menu.udpdbg.tip", Glyph = "\uE839" },
		new() { Id = "w-soapdbg", Cat = "net", Kind = "act", Open = "soapdbg", NameKey = "menu.soapdbg", TipKey = "menu.soapdbg.tip", Glyph = "\uE774" },
		new() { Id = "w-portuse", Cat = "net", Kind = "act", Open = "portuse", NameKey = "menu.portuse", TipKey = "menu.portuse.tip", Glyph = "\uE839" },
		new() { Id = "w-netmon", Cat = "net", Kind = "act", Open = "netmon", NameKey = "menu.netmon", TipKey = "menu.netmon.tip", Glyph = "\uE9D9" },
		new() { Id = "w-usbcap", Cat = "net", Kind = "act", Open = "usbcap", NameKey = "menu.usbcap", TipKey = "menu.usbcap.tip", Glyph = "\uE88E" },
		new() { Id = "w-tls", Cat = "net", Kind = "act", Open = "tls", NameKey = "menu.tls", TipKey = "menu.tls.tip", Glyph = "\uE72E" },
		new() { Id = "w-ftp", Cat = "net", Kind = "act", Open = "ftp", NameKey = "menu.ftp", TipKey = "menu.ftp.tip", Glyph = "\uE8B7" },
		new() { Id = "w-sql", Cat = "net", Kind = "act", Open = "sql", NameKey = "menu.sql", TipKey = "menu.sql.tip", Glyph = "\uE8F1" },
		new() { Id = "w-files", Cat = "files", Kind = "act", Open = "files", NameKey = "tray.webfiles", TipKey = "web.files", Glyph = "\uE8B7" },
		new() { Id = "w-rename", Cat = "files", Kind = "act", Open = "rename", NameKey = "menu.rename", TipKey = "menu.rename.tip", Glyph = "\uE8AC" },
		new() { Id = "w-fastcopy", Cat = "files", Kind = "act", Open = "fastcopy", NameKey = "menu.fastcopy", TipKey = "menu.fastcopy.tip", Glyph = "\uE8C8" },
		new() { Id = "w-fileprobe", Cat = "files", Kind = "act", Open = "fileprobe", NameKey = "menu.fileprobe", TipKey = "menu.fileprobe.tip", Glyph = "\uE7C3" },
		new() { Id = "w-codestat", Cat = "files", Kind = "act", Open = "codestat", NameKey = "menu.codestat", TipKey = "menu.codestat.tip", Glyph = "\uE943" },
		new() { Id = "w-sendfile", Cat = "files", Kind = "tab", Open = "sendfile", NameKey = "tab.sendfile", TipKey = "tab.sendfile", Glyph = "\uE898" },
		new() { Id = "w-ftpsrv", Cat = "files", Kind = "tab", Open = "ftpsrv", NameKey = "ftpsrv.tab", TipKey = "ftpsrv.tab", Glyph = "\uE774" },
		new() { Id = "w-sys", Cat = "sys", Kind = "act", Open = "sys", NameKey = "menu.sysinfo", TipKey = "menu.sysinfo.tip", Glyph = "\uE770" },
		new() { Id = "w-systool", Cat = "sys", Kind = "act", Open = "systool", NameKey = "menu.systool", TipKey = "menu.systool.tip", Glyph = "\uE90F" },
		new() { Id = "w-perfmon", Cat = "sys", Kind = "act", Open = "perfmon", NameKey = "menu.perfmon", TipKey = "menu.perfmon.tip", Glyph = "\uE9D9" },
		new() { Id = "w-wintop", Cat = "sys", Kind = "act", Open = "wintop", NameKey = "menu.wintop", TipKey = "menu.wintop.tip", Glyph = "\uE737" },
		new() { Id = "w-autokey", Cat = "sys", Kind = "act", Open = "autokey", NameKey = "menu.autokey", TipKey = "menu.autokey.tip", Glyph = "\uE765" },
	};

	// 网页工具没有可嵌入的图标字体，按分类给一个 MDL2 图标。
	static readonly Dictionary<string, string> catglyph = new() {
		["text"] = "\uE8D2", ["calc"] = "\uE8EF", ["speech"] = "\uE767", ["time"] = "\uE787",
		["image"] = "\uEB9F", ["info"] = "\uE946", ["talk"] = "\uE8F2", ["files"] = "\uE8B7",
	};

	static List<ToolCard> webcache;
	static int webstamp;

	public static List<ToolCard> Cards() {
		var list = new List<ToolCard>(desktop.Length + 80);
		list.AddRange(desktop);
		list.AddRange(webcards());
		return list;
	}

	public static ToolCard Find(string id) {
		if (string.IsNullOrEmpty(id)) return null;
		foreach (var c in desktop)
			if (c.Id == id) return c;
		foreach (var c in webcards())
			if (c.Id == id) return c;
		return null;
	}

	public static string CatName(string id) {
		foreach (var cat in Cats)
			if (cat.Id == id) return strip(Loc.T(cat.NameKey));
		return id ?? "";
	}

	public static string NameOf(ToolCard c) {
		if (c == null) return "";
		return strip(Loc.T(c.NameKey));
	}

	public static string TipOf(ToolCard c) {
		if (c == null) return "";
		var tip = c.TipKey == c.NameKey ? "" : Loc.T(c.TipKey);
		if (string.IsNullOrEmpty(tip) || tip == c.TipKey) tip = "";
		return strip(tip);
	}

	/// <summary>收藏只留当前还在的卡片，顺序不变。没有的 id 直接丢掉。</summary>
	public static List<string> KeepFav(IEnumerable<string> ids) {
		var keep = new List<string>();
		if (ids == null) return keep;
		var seen = new HashSet<string>();
		foreach (var raw in ids) {
			var id = (raw ?? "").Trim();
			if (id.Length == 0 || !seen.Add(id)) continue;
			if (Find(id) == null) continue;
			keep.Add(id);
		}
		return keep;
	}

	static string strip(string text) {
		var s = text ?? "";
		var i = s.IndexOf("(_", StringComparison.Ordinal);
		if (i >= 0 && i + 4 == s.Length && s[s.Length - 1] == ')')
			s = s.Substring(0, i);
		if (s.EndsWith("…")) s = s.Substring(0, s.Length - 1);
		return s.Trim();
	}

	static List<ToolCard> webcards() {
		var stamp = Environment.TickCount / 2000;
		if (webcache != null && stamp == webstamp) return webcache;
		webstamp = stamp;
		webcache = parseweb();
		return webcache;
	}

	static List<ToolCard> parseweb() {
		var list = new List<ToolCard>();
		var js = TrayIcon.CatalogJs();
		if (js.Length == 0) return list;
		var a = js.IndexOf("div#cards.cards", StringComparison.Ordinal);
		var b = js.IndexOf("nav#cats", StringComparison.Ordinal);
		if (a < 0 || b <= a) return list;
		var region = js.Substring(a, b - a);
		var rx = new Regex("\"data-(?:tool|page|link|popup)\":\"([^\"]+)\"(?:(?!\"data-(?:tool|page|link|popup)\":)[\\s\\S])*?\"data-cat\":\"([^\"]+)\"(?:(?!\"data-(?:tool|page|link|popup)\":)[\\s\\S])*?\"data-k\":\"([^\"]+)\"\\s*,\\s*c:\"([^\"]*)\"");
		var seen = new HashSet<string>();
		foreach (Match m in rx.Matches(region)) {
			var id = m.Groups[1].Value;
			if (id.Length == 0 || id == "home" || !seen.Add(id)) continue;
			var key = m.Groups[3].Value;
			var name = m.Groups[4].Value;
			list.Add(new ToolCard {
				Id = id,
				Cat = m.Groups[2].Value,
				Kind = "web",
				Open = id,
				NameKey = key.StartsWith("web.", StringComparison.Ordinal) ? key : "",
				TipKey = "",
				Glyph = catglyph.TryGetValue(m.Groups[2].Value, out var g) ? g : "\uE774",
				Search = name,
			});
			if (string.IsNullOrEmpty(list[list.Count - 1].NameKey))
				list[list.Count - 1].Search = name;
		}
		return list;
	}
}
