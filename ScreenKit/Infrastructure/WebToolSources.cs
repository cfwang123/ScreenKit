using System.IO;

namespace ScreenKit;

/// <summary>
/// web 工具脚本合并成 web/tools.js 后，合并前的那份文件就没用了。发布版启动时清掉，
/// 免得旧安装残留的脚本被 mergejs() 又拼回去、菜单和页面对不上。开发目录在
/// config.toml 里写 dev = true，保留这些文件。
/// </summary>
static class WebToolSources {
	/// <summary>旧发布包把工具脚本摊平到 web/ 顶层时留下的文件名。</summary>
	static readonly string[] FlatNames = {
		"00-core.js", "00-home.js", "00-ref.js", "00-hl.js", "00-verb.js",
		"asr.js", "cal.js", "chat.js", "ocr.js", "qr.js", "text.js", "tr.js", "tts.js",
	};

	public static void Clean() {
		var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
		if (!Directory.Exists(root)) return;
		try {
			var dir = Path.Combine(root, "tools");
			if (Directory.Exists(dir)) Directory.Delete(dir, true);
		}
		catch { }
		foreach (var name in FlatNames) remove(Path.Combine(root, name));
		string[] files;
		try { files = Directory.GetFiles(root, "*.js"); }
		catch { return; }
		foreach (var file in files) {
			var name = Path.GetFileName(file);
			if (name.StartsWith("u-", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("00-", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("zz-", StringComparison.OrdinalIgnoreCase))
				remove(file);
		}
	}

	static void remove(string path) {
		try {
			if (File.Exists(path)) File.Delete(path);
		}
		catch { }
	}
}
