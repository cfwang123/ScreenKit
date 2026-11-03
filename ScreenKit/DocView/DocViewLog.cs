using System;
using System.IO;
using System.Text;

namespace ScreenKit.DocView;

/// <summary>文档阅读器的诊断日志，目录：exe 旁 log/。失败不影响阅读。</summary>
static class DocViewLog {
	static readonly object Gate = new();
	static string logFile;

	/// <summary>是否写入 log/docview_日期.log；由 config.toml 的 capture_log 控制，默认关闭。</summary>
	public static bool Enabled { get; set; }

	public static void Info(string msg) => write("I", msg, null);
	public static void Warn(string msg) => write("W", msg, null);
	public static void Error(string msg, Exception ex = null) => write("E", msg, ex);

	static void write(string level, string msg, Exception ex) {
		if (!Enabled) return;
		try {
			ensure();
			var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
			if (ex != null)
				line += $" | {ex.GetType().Name}: {ex.Message}\n{ex}";
			line += "\n";
			lock (Gate)
				File.AppendAllText(logFile, line, Encoding.UTF8);
		}
		catch {
			// 日志失败不影响业务
		}
	}

	static void ensure() {
		if (logFile != null) return;
		lock (Gate) {
			if (logFile != null) return;
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(dir);
			logFile = Path.Combine(dir, $"docview_{DateTime.Now:yyyyMMdd}.log");
		}
	}
}
