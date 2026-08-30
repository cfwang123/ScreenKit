using System.IO;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 录屏专用日志：开启时每次录制新建 log/record_yyyyMMdd_HHmmss.log。
/// 由 config.toml 的 capture_log 控制，默认关闭。
/// </summary>
static class RecordLog {
	static readonly object gate = new();
	static string path;
	static bool active;

	/// <summary>是否写入录屏日志；默认 false（与 CaptureLog 同源开关）。</summary>
	public static bool Enabled { get; set; }

	/// <summary>当前会话日志路径；未开始时为 null。</summary>
	public static string LogPath {
		get { lock (gate) return path; }
	}

	public static bool IsActive {
		get { lock (gate) return active; }
	}

	/// <summary>开始新会话（覆盖绑定到新文件）。关闭时为 no-op。</summary>
	public static void Begin(string tag = "record") {
		if (!Enabled) {
			lock (gate) {
				active = false;
				path = null;
			}
			return;
		}
		lock (gate) {
			try {
				var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
				Directory.CreateDirectory(dir);
				path = Path.Combine(dir, $"record_{DateTime.Now:yyyyMMdd_HHmmss}.log");
				active = true;
				var sb = new StringBuilder();
				sb.AppendLine();
				sb.AppendLine("======== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
					+ " RECORD " + tag + " ========");
				sb.AppendLine("log=" + path);
				sb.AppendLine("exe=" + AppDomain.CurrentDomain.BaseDirectory);
				File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
			}
			catch {
				active = false;
				path = null;
			}
		}
	}

	public static void End(string note = null) {
		if (!Enabled) {
			lock (gate) active = false;
			return;
		}
		Info("=== session end" + (string.IsNullOrEmpty(note) ? "" : ": " + note) + " ===");
		// 保留 path 便于 UI 提示；下次 Begin 换新文件
		lock (gate) active = false;
	}

	public static void Info(string msg) {
		if (!Enabled) return;
		lock (gate) {
			if (string.IsNullOrEmpty(path)) return;
			try {
				File.AppendAllText(path,
					DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine,
					Encoding.UTF8);
			}
			catch { }
		}
		// 同步到 CaptureLog（同源开关时一并写入 capture.log）
		try { CaptureLog.Info("[rec] " + msg); } catch { }
	}

	public static void Ex(string where, Exception ex) {
		Info(where + " EX: " + (ex?.ToString() ?? "null"));
	}

	public static void Step(string step, string detail = null) {
		if (string.IsNullOrEmpty(detail))
			Info("[" + step + "]");
		else
			Info("[" + step + "] " + detail);
	}

	public static string FileInfo(string filePath) {
		try {
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
				return filePath + " (missing)";
			var fi = new FileInfo(filePath);
			return $"{filePath} size={fi.Length} mtime={fi.LastWriteTime:HH:mm:ss}";
		}
		catch (Exception ex) {
			return filePath + " (err " + ex.Message + ")";
		}
	}
}
