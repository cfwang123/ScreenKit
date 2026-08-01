using System.Diagnostics;
using System.Text;

namespace WpfOCR;

/// <summary>
/// 调用 WeText C++ <c>processor_pipe.exe</c>（OpenFst）做中文 ITN。
/// 进程常驻：stdin 一行原文 → stdout 一行归一化结果。
/// 目录约定：exe 旁 <c>wetext/processor_pipe.exe</c> 与
/// <c>wetext/zh/itn/zh_itn_tagger.fst</c>、<c>zh_itn_verbalizer.fst</c>。
/// </summary>
static class WetextItn {
	static readonly object Gate = new();
	static Process proc;
	static StreamWriter stdin;
	static StreamReader stdout;
	static string lastError = "";
	static bool probed;
	static bool available;

	public static string LastError => lastError;
	public static bool IsAvailable {
		get {
			ensure();
			return available;
		}
	}

	/// <summary>ITN；不可用或失败时返回原文。</summary>
	public static string Normalize(string text) {
		if (string.IsNullOrEmpty(text)) return text ?? "";
		// 单行协议，去掉换行
		text = text.Replace("\r", " ").Replace("\n", " ").Trim();
		if (text.Length == 0) return text;

		lock (Gate) {
			if (!ensure()) return text;
			try {
				stdin.Write(text);
				stdin.Write('\n');
				stdin.Flush();
				var line = stdout.ReadLine();
				if (line == null) {
					lastError = "wetext 管道已关闭";
					kill();
					return text;
				}
				return line;
			}
			catch (Exception ex) {
				lastError = ex.Message;
				try { CaptureLog.Info("WetextItn: " + ex.Message); } catch { }
				kill();
				return text;
			}
		}
	}

	static bool ensure() {
		if (available && proc != null && !proc.HasExited) return true;
		if (probed && !available && proc == null) {
			// 已探测失败且未恢复：允许再试一次若文件后来放好了
		}
		probed = true;
		kill();

		var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wetext");
		var exe = Path.Combine(root, "processor_pipe.exe");
		var tagger = Path.Combine(root, "zh", "itn", "zh_itn_tagger.fst");
		var verbalizer = Path.Combine(root, "zh", "itn", "zh_itn_verbalizer.fst");
		if (!File.Exists(exe) || !File.Exists(tagger) || !File.Exists(verbalizer)) {
			lastError = "缺少 wetext/processor_pipe.exe 或 zh_itn_*.fst";
			available = false;
			return false;
		}

		try {
			var psi = new ProcessStartInfo {
				FileName = exe,
				Arguments = $"--tagger=\"{tagger}\" --verbalizer=\"{verbalizer}\"",
				WorkingDirectory = root,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = new UTF8Encoding(false),
				// net48 无 StandardInputEncoding，用 BaseStream + StreamWriter UTF8
			};
			proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
			proc.ErrorDataReceived += (_, e) => {
				if (!string.IsNullOrEmpty(e.Data))
					try { CaptureLog.Info("wetext stderr: " + e.Data); } catch { }
			};
			if (!proc.Start()) {
				lastError = "无法启动 processor_pipe.exe";
				available = false;
				return false;
			}
			proc.BeginErrorReadLine();
			stdin = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false)) {
				AutoFlush = true,
				NewLine = "\n",
			};
			stdout = proc.StandardOutput;
			// 预热：空等会挂；用短句
			stdin.Write("测试\n");
			stdin.Flush();
			var warm = stdout.ReadLine();
			if (warm == null) {
				lastError = "wetext 预热无响应";
				kill();
				available = false;
				return false;
			}
			available = true;
			lastError = "";
			try { CaptureLog.Info("WetextItn ready: " + exe); } catch { }
			return true;
		}
		catch (Exception ex) {
			lastError = ex.Message;
			kill();
			available = false;
			return false;
		}
	}

	static void kill() {
		try { stdin?.Dispose(); } catch { }
		stdin = null;
		try { stdout?.Dispose(); } catch { }
		stdout = null;
		try {
			if (proc != null && !proc.HasExited)
				proc.Kill();
		}
		catch { }
		try { proc?.Dispose(); } catch { }
		proc = null;
		available = false;
	}

	public static void Shutdown() {
		lock (Gate) kill();
	}
}
