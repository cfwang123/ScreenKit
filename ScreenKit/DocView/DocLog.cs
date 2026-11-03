using System;
using System.IO;
using System.Text;

namespace ScreenKit.DocView;

/// <summary>简单文件日志，目录：exe 旁 logs/。</summary>
static class DocLog {
	static readonly object Gate = new();
	static string logDir;
	static string logFile;

	public static void Info(string msg) => write("I", msg, null);
	public static void Warn(string msg) => write("W", msg, null);
	public static void Error(string msg, Exception ex = null) => write("E", msg, ex);

	static void write(string level, string msg, Exception ex) {
		try {
			ensure();
			var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
			if (ex != null)
				line += $" | {ex.GetType().Name}: {ex.Message}\n{ex}";
			line += "\n";
			lock (Gate)
				File.AppendAllText(logFile, line, Encoding.UTF8);
		} catch {
			// 日志失败不影响业务
		}
	}

	static void ensure() {
		if (logFile != null) return;
		lock (Gate) {
			if (logFile != null) return;
			var baseDir = AppDomain.CurrentDomain.BaseDirectory;
			logDir = Path.Combine(baseDir, "logs");
			Directory.CreateDirectory(logDir);
			logFile = Path.Combine(logDir, $"docviewwpf_{DateTime.Now:yyyyMMdd}.log");
		}
	}
}
