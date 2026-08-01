using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace WpfOCR;

/// <summary>
/// 系统诊断日志（截图侧）：exe 旁 log/capture.log。
/// 由 config.toml 的 capture_log 控制，默认关闭；与 RecordLog 共用开关。
/// </summary>
static class CaptureLog {
	static readonly object gate = new();
	static string path;

	/// <summary>是否写入 log/capture.log；默认 false。</summary>
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
		path = Path.Combine(dir, "capture.log");
	}

	public static void SessionStart(string tag) {
		if (!Enabled) return;
		ensurepath();
		lock (gate) {
			try {
				var sb = new StringBuilder();
				sb.AppendLine();
				sb.AppendLine("======== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + tag + " ========");
				File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
			}
			catch { }
		}
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

	public static string Bmp(BitmapSource b) {
		if (b == null) return "null";
		try {
			return $"{b.PixelWidth}x{b.PixelHeight} dpi={b.DpiX:0.#}/{b.DpiY:0.#} fmt={b.Format} frozen={b.IsFrozen}";
		}
		catch (Exception ex) {
			return "err:" + ex.Message;
		}
	}
}
