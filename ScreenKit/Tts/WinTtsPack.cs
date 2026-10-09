using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace ScreenKit;

sealed class WinElevateCode {
	public string Culture = "";
	public int Code;
}

sealed class WinElevateResult {
	public bool UacDenied;
	public string Error = "";
	public readonly List<WinElevateCode> Codes = new();
}

sealed class WinVoiceLine {
	public string Culture = "";
	public string Name = "";
}

sealed class WinQueryResult {
	public bool UacDenied;
	public string Error = "";
	public string VoiceError = "";
	public readonly Dictionary<string, string> States = new(StringComparer.OrdinalIgnoreCase);
	public readonly List<WinVoiceLine> Voices = new();
}

sealed class WinScriptResult {
	public bool UacDenied;
	public string Error = "";
	public string Log = "";
	public int ExitCode;
}

enum WinSpeechPackKind {
	Tts,
	Asr,
}

/// <summary>Windows 语音功能包 Language.TextToSpeech。查询与 DISM 安装/卸载。</summary>
static class WinTtsPack {
	public const string PREFIX = "Language.TextToSpeech~~~";
	public const string ASR_PREFIX = "Language.Speech~~~";
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

	/// <summary>Windows 10/11 支持离线系统语音识别的区域。</summary>
	public static readonly string[] AsrCultures = {
		"de-DE",
		"en-AU", "en-CA", "en-GB", "en-IN", "en-US",
		"es-ES", "es-MX", "fr-FR", "ja-JP",
		"zh-CN", "zh-TW",
	};

	public static string Capability(string culture, WinSpeechPackKind kind = WinSpeechPackKind.Tts) =>
		$"{prefixof(kind)}{culture}{SUFFIX}";

	public static string AddCmd(string culture, WinSpeechPackKind kind = WinSpeechPackKind.Tts) =>
		$"DISM /Online /Add-Capability /CapabilityName:{Capability(culture, kind)}";

	public static string RemoveCmd(string culture, WinSpeechPackKind kind = WinSpeechPackKind.Tts) =>
		$"DISM /Online /Remove-Capability /CapabilityName:{Capability(culture, kind)}";

	public static bool IsAdmin() {
		try {
			using var id = WindowsIdentity.GetCurrent();
			var prin = new WindowsPrincipal(id);
			return prin.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch { return false; }
	}

	/// <summary>区域 → InstallState 名（Installed / NotPresent / …）。失败抛错。</summary>
	public static Dictionary<string, string> QueryStates(
		CancellationToken ct, WinSpeechPackKind kind = WinSpeechPackKind.Tts) {
		var pattern = kind == WinSpeechPackKind.Asr ? "Language.Speech*" : "Language.TextToSpeech*";
		var script =
			"$ProgressPreference = 'SilentlyContinue'\r\n" +
			"$ErrorActionPreference = 'Stop'\r\n" +
			"[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false\r\n" +
			"$OutputEncoding = [Console]::OutputEncoding\r\n" +
			$"Get-WindowsCapability -Online -Name '{pattern}' | ForEach-Object {{ $_.Name + '|' + [string]$_.State }}\r\n";
		var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		var text = runout("powershell.exe",
			"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc, ct);
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
			var line = raw.Trim();
			var i = line.IndexOf('|');
			if (i <= 0) continue;
			var culture = cultureof(line.Substring(0, i).Trim(), kind);
			if (string.IsNullOrEmpty(culture)) continue;
			map[culture] = line.Substring(i + 1).Trim();
		}
		if (map.Count == 0)
			throw new InvalidOperationException(string.IsNullOrWhiteSpace(text) ? "empty" : text.Trim());
		return map;
	}

	/// <summary>执行 DISM。调用方把 0 与 3010 当作成功。取消时抛 OperationCanceledException。</summary>
	public static int RunDism(bool add, string culture, IProgress<string> log, CancellationToken ct,
		WinSpeechPackKind kind = WinSpeechPackKind.Tts) {
		var verb = add ? "/Online /Add-Capability" : "/Online /Remove-Capability";
		var args = $"{verb} /CapabilityName:{Capability(culture, kind)} /NoRestart /English";
		log?.Report("DISM " + args);
		var (code, _) = exec(Path.Combine(Environment.SystemDirectory, "dism.exe"), args, ct, log);
		return code;
	}

	/// <summary>非管理员：cmd start /wait 拉起 powershell，再 Start-Process -Verb RunAs 跑 DISM。0 与 3010 为成功。</summary>
	public static WinElevateResult RunElevated(bool add, IList<string> cultures, IProgress<string> log,
		CancellationToken ct, WinSpeechPackKind kind = WinSpeechPackKind.Tts) {
		var result = new WinElevateResult();
		if (cultures == null || cultures.Count == 0) {
			result.Error = "none";
			return result;
		}
		var ran = runscript(path => BuildElevateScript(add, cultures, path, kind), log, ct);
		result.UacDenied = ran.UacDenied;
		result.Error = ran.Error;
		fillcodes(ran.Log, result);
		if (!ran.UacDenied && result.Codes.Count == 0 && result.Error.Length == 0 && ran.ExitCode != 0)
			result.Error = "exit " + ran.ExitCode;
		return result;
	}

	/// <summary>非管理员查看已装语音包和发音人。同一次 start / RunAs。</summary>
	public static WinQueryResult QueryElevated(CancellationToken ct, IProgress<string> log,
		WinSpeechPackKind kind = WinSpeechPackKind.Tts) {
		var result = new WinQueryResult();
		var ran = runscript(path => BuildQueryScript(path, kind), log, ct);
		result.UacDenied = ran.UacDenied;
		if (ran.UacDenied) return result;
		parsequery(ran.Log, result, kind);
		if (result.States.Count == 0 && result.Error.Length == 0)
			result.Error = ran.Error.Length > 0 ? ran.Error : (ran.ExitCode != 0 ? "exit " + ran.ExitCode : "empty");
		return result;
	}

	internal static string BuildElevateScript(bool add, IList<string> cultures, string logPath,
		WinSpeechPackKind kind = WinSpeechPackKind.Tts) {
		var verb = add ? "/Add-Capability" : "/Remove-Capability";
		var sb = new StringBuilder();
		sb.AppendLine("$ErrorActionPreference = 'Continue'");
		sb.AppendLine("try { $Host.UI.RawUI.WindowTitle = "
			+ psq(kind == WinSpeechPackKind.Asr ? "ScreenKit Windows ASR" : "ScreenKit Windows TTS") + " } catch {}");
		sb.AppendLine("$log = " + psq(logPath));
		sb.AppendLine("$dism = Join-Path $env:SystemRoot 'System32\\dism.exe'");
		sb.AppendLine("$utf8 = New-Object System.Text.UTF8Encoding $false");
		sb.AppendLine("function Log([string]$line) { [IO.File]::AppendAllText($log, $line + \"`r`n\", $utf8) }");
		sb.AppendLine("$fail = 0");
		foreach (var culture in cultures) {
			var cap = Capability(culture, kind);
			sb.AppendLine("Log " + psq("BEGIN " + culture));
			sb.AppendLine("Write-Host " + psq("DISM /Online " + verb + " /CapabilityName:" + cap));
			sb.AppendLine("try {");
			sb.AppendLine("  $p = Start-Process -FilePath $dism -Wait -PassThru -NoNewWindow -ArgumentList @('/Online'," +
				psq(verb) + ",'/CapabilityName:" + cap + "','/NoRestart','/English')");
			sb.AppendLine("  $code = $p.ExitCode");
			sb.AppendLine("} catch { $code = -1; Write-Host $_.Exception.Message }");
			sb.AppendLine("Log (" + psq("EXIT " + culture + " ") + " + $code)");
			sb.AppendLine("Write-Host (" + psq("EXIT " + culture + " ") + " + $code)");
			sb.AppendLine("if ($code -ne 0 -and $code -ne 3010) { $fail++ }");
		}
		sb.AppendLine("if ($fail -gt 0) { exit 1 }");
		sb.AppendLine("exit 0");
		return sb.ToString();
	}

	internal static string BuildQueryScript(string logPath, WinSpeechPackKind kind = WinSpeechPackKind.Tts) {
		var pattern = kind == WinSpeechPackKind.Asr ? "Language.Speech*" : "Language.TextToSpeech*";
		var sb = new StringBuilder();
		sb.AppendLine("$ErrorActionPreference = 'Stop'");
		sb.AppendLine("try { $Host.UI.RawUI.WindowTitle = "
			+ psq(kind == WinSpeechPackKind.Asr ? "ScreenKit Windows ASR" : "ScreenKit Windows TTS") + " } catch {}");
		sb.AppendLine("$log = " + psq(logPath));
		sb.AppendLine("$utf8 = New-Object System.Text.UTF8Encoding $false");
		sb.AppendLine("function Log([string]$line) { [IO.File]::AppendAllText($log, $line + \"`r`n\", $utf8) }");
		sb.AppendLine("try {");
		sb.AppendLine("  Get-WindowsCapability -Online -Name " + psq(pattern) + " | ForEach-Object {");
		sb.AppendLine("    Log ($_.Name + '|' + [string]$_.State)");
		sb.AppendLine("  }");
		sb.AppendLine("} catch {");
		sb.AppendLine("  Log ('ERR ' + $_.Exception.Message)");
		sb.AppendLine("  exit 1");
		sb.AppendLine("}");
		if (kind == WinSpeechPackKind.Tts) {
			sb.AppendLine("$ErrorActionPreference = 'Continue'");
			sb.AppendLine("try {");
			sb.AppendLine("  $null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType=WindowsRuntime]");
			sb.AppendLine("  foreach ($v in [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices) {");
			sb.AppendLine("    $name = [string]$v.DisplayName");
			sb.AppendLine("    $lang = [string]$v.Language");
			sb.AppendLine("    if ([string]::IsNullOrWhiteSpace($name)) { continue }");
			sb.AppendLine("    Log ('VOICE|' + $lang + '|' + $name)");
			sb.AppendLine("  }");
			sb.AppendLine("} catch {");
			sb.AppendLine("  Log ('VOICEERR ' + $_.Exception.Message)");
			sb.AppendLine("}");
		}
		sb.AppendLine("exit 0");
		return sb.ToString();
	}

	internal static string OuterScript(string ps1, string codeFile) {
		return
			"$ErrorActionPreference = 'Stop'\r\n" +
			"try {\r\n" +
			"  $ps = Join-Path $env:SystemRoot 'System32\\WindowsPowerShell\\v1.0\\powershell.exe'\r\n" +
			"  $p = Start-Process -FilePath $ps -Verb RunAs -Wait -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File'," + psq(ps1) + ")\r\n" +
			"  if ($null -eq $p) { Set-Content -LiteralPath " + psq(codeFile) + " -Value 'UAC' -Encoding ascii; exit 1223 }\r\n" +
			"  Set-Content -LiteralPath " + psq(codeFile) + " -Value ([string]$p.ExitCode) -Encoding ascii\r\n" +
			"  exit $p.ExitCode\r\n" +
			"} catch {\r\n" +
			"  $m = $_.Exception.Message\r\n" +
			"  if ($m -match 'cancel|canceled|cancelled|1223|取消') {\r\n" +
			"    Set-Content -LiteralPath " + psq(codeFile) + " -Value 'UAC' -Encoding ascii\r\n" +
			"    exit 1223\r\n" +
			"  }\r\n" +
			"  Set-Content -LiteralPath " + psq(codeFile) + " -Value ('ERR ' + $m) -Encoding utf8\r\n" +
			"  exit 1\r\n" +
			"}\r\n";
	}

	internal static string LaunchArgs(string outerScript) {
		var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(outerScript ?? ""));
		return "/c start \"ScreenKit\" /min /wait powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand " + enc;
	}

	static WinScriptResult runscript(Func<string, string> build, IProgress<string> log, CancellationToken ct) {
		var ran = new WinScriptResult();
		var dir = TmpStore.Root;
		var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
		var ps1 = Path.Combine(dir, "wintts_" + stamp + ".ps1");
		var logfile = Path.Combine(dir, "wintts_" + stamp + ".log");
		var codefile = Path.Combine(dir, "wintts_" + stamp + ".code");
		try {
			File.WriteAllText(ps1, build(logfile), new UTF8Encoding(true));
			var outer = OuterScript(ps1, codefile);
			var args = LaunchArgs(outer);
			log?.Report("start \"ScreenKit\" /min /wait powershell.exe -Verb RunAs");
			var psi = new ProcessStartInfo {
				FileName = "cmd.exe",
				Arguments = args,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			using var p = new Process { StartInfo = psi };
			if (!p.Start()) throw new InvalidOperationException("start failed");
			var pos = 0;
			using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } })) {
				while (!p.HasExited) {
					pos = drain(logfile, pos, log);
					if (ct.IsCancellationRequested) break;
					if (p.WaitForExit(400)) break;
				}
			}
			if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
			if (!p.HasExited) p.WaitForExit();
			drain(logfile, pos, log);
			ran.ExitCode = p.ExitCode;
			ran.Log = readall(logfile);
			var mark = readall(codefile).Trim();
			if (mark == "UAC" || p.ExitCode == 1223) ran.UacDenied = true;
			else if (mark.StartsWith("ERR ", StringComparison.Ordinal)) ran.Error = mark.Substring(4).Trim();
			return ran;
		}
		finally {
			try { if (File.Exists(ps1)) File.Delete(ps1); } catch { }
			try { if (File.Exists(codefile)) File.Delete(codefile); } catch { }
			try { if (File.Exists(logfile)) File.Delete(logfile); } catch { }
		}
	}

	static string psq(string s) => "'" + (s ?? "").Replace("'", "''") + "'";

	static int drain(string path, int pos, IProgress<string> log) {
		var text = readall(path);
		if (text.Length <= pos) return pos;
		var fresh = text.Substring(pos);
		foreach (var raw in fresh.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
			var line = raw.Trim();
			if (line.Length == 0) continue;
			log?.Report(line);
		}
		return text.Length;
	}

	static void fillcodes(string text, WinElevateResult result) {
		if (string.IsNullOrEmpty(text)) return;
		foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
			var line = raw.Trim();
			if (!line.StartsWith("EXIT ", StringComparison.Ordinal)) continue;
			var rest = line.Substring(5).Trim();
			var sp = rest.LastIndexOf(' ');
			if (sp <= 0) continue;
			var culture = rest.Substring(0, sp).Trim();
			if (!int.TryParse(rest.Substring(sp + 1).Trim(), out var code)) continue;
			result.Codes.Add(new WinElevateCode { Culture = culture, Code = code });
		}
	}

	static void parsequery(string text, WinQueryResult result, WinSpeechPackKind kind) {
		if (string.IsNullOrEmpty(text)) return;
		foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
			var line = raw.Trim();
			if (line.StartsWith("ERR ", StringComparison.Ordinal)) {
				result.Error = line.Substring(4).Trim();
				continue;
			}
			if (line.StartsWith("VOICEERR ", StringComparison.Ordinal)) {
				result.VoiceError = line.Substring(9).Trim();
				continue;
			}
			if (line.StartsWith("VOICE|", StringComparison.Ordinal)) {
				var rest = line.Substring(6);
				var sp = rest.IndexOf('|');
				if (sp <= 0) continue;
				var culture = rest.Substring(0, sp).Trim();
				var name = rest.Substring(sp + 1).Trim();
				if (culture.Length == 0 || name.Length == 0) continue;
				result.Voices.Add(new WinVoiceLine { Culture = culture, Name = name });
				continue;
			}
			var i = line.IndexOf('|');
			if (i <= 0) continue;
			var key = cultureof(line.Substring(0, i).Trim(), kind);
			if (string.IsNullOrEmpty(key)) continue;
			result.States[key] = line.Substring(i + 1).Trim();
		}
	}

	static string readall(string path) {
		try {
			if (!File.Exists(path)) return "";
			using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var sr = new StreamReader(fs, Encoding.UTF8);
			return sr.ReadToEnd();
		}
		catch { return ""; }
	}

	static string cultureof(string name, WinSpeechPackKind kind) {
		var prefix = prefixof(kind);
		if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
		var rest = name.Substring(prefix.Length);
		var j = rest.IndexOf('~');
		if (j <= 0) return "";
		return rest.Substring(0, j);
	}

	static string prefixof(WinSpeechPackKind kind) =>
		kind == WinSpeechPackKind.Asr ? ASR_PREFIX : PREFIX;

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
