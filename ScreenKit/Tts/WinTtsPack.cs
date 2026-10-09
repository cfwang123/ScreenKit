using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace ScreenKit;

sealed class WinElevateCode {
	public string Culture = "";
	public WinSpeechPackKind Kind;
	public int Code;
}

sealed class WinElevateResult {
	public bool UacDenied;
	public string Error = "";
	public readonly List<WinElevateCode> Codes = new();
	public readonly Dictionary<WinSpeechPackKind, Dictionary<string, string>> States = new();
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

sealed class WinPackRequest {
	public WinSpeechPackKind Kind;
	public string Culture = "";
}

sealed class WinAllQueryResult {
	public bool UacDenied;
	public string Error = "";
	public string VoiceError = "";
	public readonly Dictionary<WinSpeechPackKind, Dictionary<string, string>> States = new();
	public readonly List<WinVoiceLine> Voices = new();

	public Dictionary<string, string> StateMap(WinSpeechPackKind kind) {
		if (States.TryGetValue(kind, out var map)) return map;
		map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		States[kind] = map;
		return map;
	}
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
	Ocr,
}

/// <summary>Windows 语音功能包 Language.TextToSpeech。查询与 DISM 安装/卸载。</summary>
static class WinTtsPack {
	public const string PREFIX = "Language.TextToSpeech~~~";
	public const string ASR_PREFIX = "Language.Speech~~~";
	public const string OCR_PREFIX = "Language.OCR~~~";
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
		var pattern = patternof(kind);
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

	public static WinElevateResult RunElevated(
		bool add, IList<WinPackRequest> requests, IProgress<string> log, CancellationToken ct) {
		var result = new WinElevateResult();
		if (requests == null || requests.Count == 0) {
			result.Error = "none";
			return result;
		}
		var ran = runscript(path => BuildElevateScript(add, requests, path), log, ct);
		result.UacDenied = ran.UacDenied;
		result.Error = ran.Error;
		fillcodes(ran.Log, result);
		copystates(ran.Log, result.States);
		if (!ran.UacDenied && result.Codes.Count == 0 && result.Error.Length == 0 && ran.ExitCode != 0)
			result.Error = "exit " + ran.ExitCode;
		return result;
	}

	/// <summary>只查询列出的功能包。调用方已是管理员。</summary>
	public static Dictionary<WinSpeechPackKind, Dictionary<string, string>> QueryNamedStates(
		IList<WinPackRequest> requests, CancellationToken ct) {
		var result = new WinAllQueryResult();
		if (requests == null || requests.Count == 0) return result.States;
		var text = runpowershell(BuildNamedQueryBody(requests), ct);
		parseallquery(text, result);
		if (result.States.Count == 0 && result.Error.Length > 0)
			throw new InvalidOperationException(result.Error);
		return result.States;
	}

	/// <summary>非管理员只复查列出的功能包。同一次 start / RunAs。</summary>
	public static WinAllQueryResult QueryNamedElevated(
		IList<WinPackRequest> requests, CancellationToken ct, IProgress<string> log) {
		var result = new WinAllQueryResult();
		if (requests == null || requests.Count == 0) return result;
		var ran = runscript(path => BuildNamedQueryScript(requests, path), log, ct);
		result.UacDenied = ran.UacDenied;
		if (ran.UacDenied) return result;
		parseallquery(ran.Log, result);
		if (result.States.Count == 0 && result.Error.Length == 0)
			result.Error = ran.Error.Length > 0 ? ran.Error : (ran.ExitCode != 0 ? "exit " + ran.ExitCode : "empty");
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

	public static Dictionary<WinSpeechPackKind, Dictionary<string, string>> QueryAllStates(CancellationToken ct) {
		var text = runpowershell(BuildAllQueryBody(includeVoices: false), ct);
		var result = new WinAllQueryResult();
		parseallquery(text, result);
		if (result.States.Count == 0)
			throw new InvalidOperationException(string.IsNullOrWhiteSpace(text) ? "empty" : text.Trim());
		return result.States;
	}

	public static WinAllQueryResult QueryAllElevated(CancellationToken ct, IProgress<string> log) {
		var result = new WinAllQueryResult();
		var ran = runscript(BuildAllQueryScript, log, ct);
		result.UacDenied = ran.UacDenied;
		if (ran.UacDenied) return result;
		parseallquery(ran.Log, result);
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
			+ psq(titleof(kind)) + " } catch {}");
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

	internal static string BuildElevateScript(bool add, IList<WinPackRequest> requests, string logPath) {
		var verb = add ? "/Add-Capability" : "/Remove-Capability";
		var sb = new StringBuilder();
		sb.AppendLine("$ErrorActionPreference = 'Continue'");
		sb.AppendLine("try { $Host.UI.RawUI.WindowTitle = 'ScreenKit Windows OCR/Speech' } catch {}");
		sb.AppendLine("$log = " + psq(logPath));
		sb.AppendLine("$dism = Join-Path $env:SystemRoot 'System32\\dism.exe'");
		sb.AppendLine("$utf8 = New-Object System.Text.UTF8Encoding $false");
		sb.AppendLine("function Log([string]$line) { [IO.File]::AppendAllText($log, $line + \"`r`n\", $utf8) }");
		sb.AppendLine("$fail = 0");
		foreach (var request in requests) {
			var key = request.Kind + "|" + request.Culture;
			var cap = Capability(request.Culture, request.Kind);
			sb.AppendLine("Log " + psq("BEGIN " + key));
			sb.AppendLine("Write-Host " + psq("DISM /Online " + verb + " /CapabilityName:" + cap));
			sb.AppendLine("try {");
			sb.AppendLine("  $p = Start-Process -FilePath $dism -Wait -PassThru -NoNewWindow -ArgumentList @('/Online'," +
				psq(verb) + ",'/CapabilityName:" + cap + "','/NoRestart','/English')");
			sb.AppendLine("  $code = $p.ExitCode");
			sb.AppendLine("} catch { $code = -1; Write-Host $_.Exception.Message }");
			sb.AppendLine("Log (" + psq("EXIT " + key + " ") + " + $code)");
			sb.AppendLine("Write-Host (" + psq("EXIT " + key + " ") + " + $code)");
			sb.AppendLine("if ($code -ne 0 -and $code -ne 3010) { $fail++ }");
		}
		appendcapabilityquery(sb, requests, "Log");
		sb.AppendLine("if ($fail -gt 0) { exit 1 }");
		sb.AppendLine("exit 0");
		return sb.ToString();
	}

	internal static string BuildNamedQueryBody(IList<WinPackRequest> requests) {
		var sb = new StringBuilder();
		sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
		sb.AppendLine("$ErrorActionPreference = 'Continue'");
		sb.AppendLine("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false");
		sb.AppendLine("$OutputEncoding = [Console]::OutputEncoding");
		appendcapabilityquery(sb, requests, null);
		sb.AppendLine("exit 0");
		return sb.ToString();
	}

	internal static string BuildNamedQueryScript(IList<WinPackRequest> requests, string logPath) {
		var sb = new StringBuilder();
		sb.AppendLine("$ErrorActionPreference = 'Continue'");
		sb.AppendLine("try { $Host.UI.RawUI.WindowTitle = 'ScreenKit Windows OCR/Speech' } catch {}");
		sb.AppendLine("$log = " + psq(logPath));
		sb.AppendLine("$utf8 = New-Object System.Text.UTF8Encoding $false");
		sb.AppendLine("function Log([string]$line) { [IO.File]::AppendAllText($log, $line + \"`r`n\", $utf8) }");
		appendcapabilityquery(sb, requests, "Log");
		sb.AppendLine("exit 0");
		return sb.ToString();
	}

	internal static WinAllQueryResult ParseAllQuery(string text) {
		var result = new WinAllQueryResult();
		parseallquery(text, result);
		return result;
	}

	static void appendcapabilityquery(StringBuilder sb, IList<WinPackRequest> requests, string logFunction) {
		if (requests == null) return;
		foreach (var request in requests) {
			if (request == null || string.IsNullOrWhiteSpace(request.Culture)) continue;
			var name = Capability(request.Culture, request.Kind);
			sb.AppendLine("try {");
			sb.AppendLine("  Get-WindowsCapability -Online -Name " + psq(name) + " | ForEach-Object {");
			emitscript(sb, "'STATE ' + $_.Name + '|' + [string]$_.State", logFunction, "    ");
			sb.AppendLine("  }");
			sb.AppendLine("} catch {");
			emitscript(sb, "'ERR ' + $_.Exception.Message", logFunction, "  ");
			sb.AppendLine("}");
		}
	}

	static void copystates(string text, Dictionary<WinSpeechPackKind, Dictionary<string, string>> into) {
		if (into == null) return;
		var parsed = new WinAllQueryResult();
		parseallquery(text, parsed);
		foreach (var kv in parsed.States) into[kv.Key] = kv.Value;
	}

	internal static string BuildQueryScript(string logPath, WinSpeechPackKind kind = WinSpeechPackKind.Tts) {
		var pattern = patternof(kind);
		var sb = new StringBuilder();
		sb.AppendLine("$ErrorActionPreference = 'Stop'");
		sb.AppendLine("try { $Host.UI.RawUI.WindowTitle = "
			+ psq(titleof(kind)) + " } catch {}");
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

	internal static string BuildAllQueryScript(string logPath) {
		var sb = new StringBuilder();
		sb.AppendLine("$ErrorActionPreference = 'Stop'");
		sb.AppendLine("try { $Host.UI.RawUI.WindowTitle = 'ScreenKit Windows OCR/Speech' } catch {}");
		sb.AppendLine("$log = " + psq(logPath));
		sb.AppendLine("$utf8 = New-Object System.Text.UTF8Encoding $false");
		sb.AppendLine("function Log([string]$line) { [IO.File]::AppendAllText($log, $line + \"`r`n\", $utf8) }");
		sb.Append(BuildAllQueryBody(includeVoices: true, logFunction: "Log"));
		return sb.ToString();
	}

	static string BuildAllQueryBody(bool includeVoices, string logFunction = null) {
		var sb = new StringBuilder();
		foreach (var kind in new[] { WinSpeechPackKind.Ocr, WinSpeechPackKind.Tts, WinSpeechPackKind.Asr }) {
			sb.AppendLine("try {");
			sb.AppendLine("  Get-WindowsCapability -Online -Name " + psq(patternof(kind)) + " | ForEach-Object {");
			emitscript(sb, "$_.Name + '|' + [string]$_.State", logFunction, "    ");
			sb.AppendLine("  }");
			sb.AppendLine("} catch {");
			emitscript(sb, "'ERR ' + $_.Exception.Message", logFunction, "  ");
			sb.AppendLine("  exit 1");
			sb.AppendLine("}");
		}
		if (includeVoices) {
			sb.AppendLine("$ErrorActionPreference = 'Continue'");
			sb.AppendLine("try {");
			sb.AppendLine("  $null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType=WindowsRuntime]");
			sb.AppendLine("  foreach ($v in [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices) {");
			sb.AppendLine("    $name = [string]$v.DisplayName");
			sb.AppendLine("    $lang = [string]$v.Language");
			sb.AppendLine("    if ([string]::IsNullOrWhiteSpace($name)) { continue }");
			emitscript(sb, "'VOICE|' + $lang + '|' + $name", logFunction, "    ");
			sb.AppendLine("  }");
			sb.AppendLine("} catch {");
			emitscript(sb, "'VOICEERR ' + $_.Exception.Message", logFunction, "  ");
			sb.AppendLine("}");
		}
		sb.AppendLine("exit 0");
		return sb.ToString();
	}

	static void emitscript(StringBuilder sb, string expression, string logFunction, string indent) {
		if (string.IsNullOrEmpty(logFunction))
			sb.AppendLine(indent + "Write-Output (" + expression + ")");
		else
			sb.AppendLine(indent + logFunction + " (" + expression + ")");
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
			var key = rest.Substring(0, sp).Trim();
			if (!int.TryParse(rest.Substring(sp + 1).Trim(), out var code)) continue;
			var kind = WinSpeechPackKind.Tts;
			var culture = key;
			var bar = key.IndexOf('|');
			if (bar > 0 && Enum.TryParse(key.Substring(0, bar), true, out WinSpeechPackKind parsed)) {
				kind = parsed;
				culture = key.Substring(bar + 1);
			}
			result.Codes.Add(new WinElevateCode { Kind = kind, Culture = culture, Code = code });
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

	static void parseallquery(string text, WinAllQueryResult result) {
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
				if (culture.Length > 0 && name.Length > 0)
					result.Voices.Add(new WinVoiceLine { Culture = culture, Name = name });
				continue;
			}
			if (line.StartsWith("STATE ", StringComparison.Ordinal))
				line = line.Substring(6).Trim();
			var i = line.IndexOf('|');
			if (i <= 0) continue;
			var capability = line.Substring(0, i).Trim();
			foreach (var kind in new[] { WinSpeechPackKind.Ocr, WinSpeechPackKind.Tts, WinSpeechPackKind.Asr }) {
				var culture = cultureof(capability, kind);
				if (culture.Length == 0) continue;
				result.StateMap(kind)[culture] = line.Substring(i + 1).Trim();
				break;
			}
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

	static string prefixof(WinSpeechPackKind kind) => kind switch {
		WinSpeechPackKind.Asr => ASR_PREFIX,
		WinSpeechPackKind.Ocr => OCR_PREFIX,
		_ => PREFIX,
	};

	static string patternof(WinSpeechPackKind kind) => kind switch {
		WinSpeechPackKind.Asr => "Language.Speech*",
		WinSpeechPackKind.Ocr => "Language.OCR*",
		_ => "Language.TextToSpeech*",
	};

	static string titleof(WinSpeechPackKind kind) => kind switch {
		WinSpeechPackKind.Asr => "ScreenKit Windows ASR",
		WinSpeechPackKind.Ocr => "ScreenKit Windows OCR",
		_ => "ScreenKit Windows TTS",
	};

	static string runpowershell(string script, CancellationToken ct) {
		var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script ?? ""));
		return runout("powershell.exe",
			"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc, ct);
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
