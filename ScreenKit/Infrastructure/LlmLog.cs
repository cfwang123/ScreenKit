using System.IO;
using System.Text;

namespace ScreenKit;

/// <summary>LLM 请求日志：exe 旁 log/llm.log。由 config.toml 的 llm_log 控制，默认关闭。</summary>
static class LlmLog {
	static readonly object gate = new();
	static string path;

	public static bool Enabled { get; set; }

	public static string LogPath {
		get {
			ensurepath();
			return path;
		}
	}

	static void ensurepath() {
		if (path != null) return;
		var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
		try { Directory.CreateDirectory(dir); } catch { }
		path = Path.Combine(dir, "llm.log");
	}

	public static void Info(string msg) {
		if (!Enabled) return;
		ensurepath();
		lock (gate) {
			try {
				File.AppendAllText(path,
					DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine,
					Encoding.UTF8);
			}
			catch { }
		}
	}

	public static void Ex(string where, Exception ex) {
		if (!Enabled) return;
		Info(where + " EX: " + ex);
	}
}
