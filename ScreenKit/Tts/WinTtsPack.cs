using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace ScreenKit;

/// <summary>Windows 语音功能包 Language.TextToSpeech。查询与 DISM 安装/卸载。</summary>
static class WinTtsPack {
	public const string PREFIX = "Language.TextToSpeech~~~";
	public const string SUFFIX = "~0.0.1.0";

	/// <summary>本机曾扫到的 TextToSpeech 区域。查询结果里多出来的区域也会进列表。</summary>
	public static readonly string[] Cultures = {
		"ar-EG", "ar-SA", "bg-BG", "ca-ES", "cs-CZ", "da-DK",
		"de-AT", "de-CH", "de-DE", "el-GR",
		"en-AU", "en-CA", "en-GB", "en-IE", "en-IN", "en-US",
		"es-ES", "es-MX", "fi-FI", "fr-CA", "fr-CH", "fr-FR",
		"he-IL", "hi-IN", "hr-HR", "hu-HU", "id-ID", "it-IT",
		"ja-JP", "ko-KR", "ms-MY", "nb-NO", "nl-BE", "nl-NL",
		"pl-PL", "pt-BR", "pt-PT", "ro-RO", "ru-RU",
		"sk-SK", "sl-SI", "sv-SE", "ta-IN", "th-TH", "tr-TR",
		"vi-VN", "zh-CN", "zh-HK", "zh-TW",
	};

	public static string Capability(string culture) => $"{PREFIX}{culture}{SUFFIX}";

	public static string AddCmd(string culture) =>
		$"DISM /Online /Add-Capability /CapabilityName:{Capability(culture)}";

	public static string RemoveCmd(string culture) =>
		$"DISM /Online /Remove-Capability /CapabilityName:{Capability(culture)}";

	public static bool IsAdmin() {
		try {
			using var id = WindowsIdentity.GetCurrent();
			var prin = new WindowsPrincipal(id);
			return prin.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch { return false; }
	}

	/// <summary>区域 → InstallState 名（Installed / NotPresent / …）。失败抛错。</summary>
	public static Dictionary<string, string> QueryStates(CancellationToken ct) {
		var script =
			"$ProgressPreference = 'SilentlyContinue'\r\n" +
			"$ErrorActionPreference = 'Stop'\r\n" +
			"[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false\r\n" +
			"$OutputEncoding = [Console]::OutputEncoding\r\n" +
			"Get-WindowsCapability -Online -Name 'Language.TextToSpeech*' | ForEach-Object { $_.Name + '|' + [string]$_.State }\r\n";
		var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		var text = runout("powershell.exe",
			"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc, ct);
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
			var line = raw.Trim();
			var i = line.IndexOf('|');
			if (i <= 0) continue;
			var culture = cultureof(line.Substring(0, i).Trim());
			if (string.IsNullOrEmpty(culture)) continue;
			map[culture] = line.Substring(i + 1).Trim();
		}
		if (map.Count == 0)
			throw new InvalidOperationException(string.IsNullOrWhiteSpace(text) ? "empty" : text.Trim());
		return map;
	}

	/// <summary>执行 DISM。调用方把 0 与 3010 当作成功。取消时抛 OperationCanceledException。</summary>
	public static int RunDism(bool add, string culture, IProgress<string> log, CancellationToken ct) {
		var verb = add ? "/Online /Add-Capability" : "/Online /Remove-Capability";
		var args = $"{verb} /CapabilityName:{Capability(culture)} /NoRestart /English";
		log?.Report("DISM " + args);
		var (code, _) = exec(Path.Combine(Environment.SystemDirectory, "dism.exe"), args, ct, log);
		return code;
	}

	static string cultureof(string name) {
		if (!name.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase)) return "";
		var rest = name.Substring(PREFIX.Length);
		var j = rest.IndexOf('~');
		if (j <= 0) return "";
		return rest.Substring(0, j);
	}

	static string runout(string file, string args, CancellationToken ct) {
		var (code, text) = exec(file, args, ct, null);
		if (code != 0 && string.IsNullOrWhiteSpace(text))
			throw new InvalidOperationException("exit " + code);
		return text ?? "";
	}

	static (int code, string text) exec(string file, string args, CancellationToken ct, IProgress<string> log) {
		var psi = new ProcessStartInfo {
			FileName = file,
			Arguments = args,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		using var p = new Process { StartInfo = psi };
		var sb = new StringBuilder();
		p.OutputDataReceived += (_, e) => online(e.Data);
		p.ErrorDataReceived += (_, e) => online(e.Data);
		if (!p.Start()) throw new InvalidOperationException("start failed");
		p.BeginOutputReadLine();
		p.BeginErrorReadLine();
		using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
			p.WaitForExit();
		if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
		string text;
		lock (sb) text = sb.ToString();
		return (p.ExitCode, text);

		void online(string data) {
			if (string.IsNullOrEmpty(data)) return;
			lock (sb) sb.AppendLine(data);
			log?.Report(data);
		}
	}
}
