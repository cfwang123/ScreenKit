using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ScreenKit;

public sealed class SysDeviceScanResult {
	public List<UsbDev> Rows { get; set; } = new();
	public string Error { get; set; } = "";
}

sealed class SysDeviceElevatedResult {
	public List<UsbDev> Rows = new();
	public string Error = "";
	public bool UacDenied;
}

/// <summary>设备查询权限不足时，通过 start + RunAs 调本程序的命令行扫描。</summary>
static class SysDeviceAdmin {
	public static bool IsAdmin() {
		try {
			using var id = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch { return false; }
	}

	public static bool IsAccessDenied(Exception ex) {
		for (var cur = ex; cur != null; cur = cur.InnerException) {
			if (cur is UnauthorizedAccessException) return true;
			if (cur is Win32Exception win && win.NativeErrorCode == 5) return true;
			if (cur is COMException com && com.HResult == unchecked((int)0x80070005)) return true;
			if (cur is ManagementException management &&
				management.ErrorCode == ManagementStatus.AccessDenied) return true;
		}
		return false;
	}

	public static SysDeviceElevatedResult Scan(string kind) {
		var result = new SysDeviceElevatedResult();
		var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
		var output = Path.Combine(TmpStore.Root, "sysdev_" + stamp + ".json");
		var codefile = Path.Combine(TmpStore.Root, "sysdev_" + stamp + ".code");
		try {
			var exe = System.Reflection.Assembly.GetEntryAssembly()?.Location;
			if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) {
				result.Error = Loc.T("dev.admin.noexe");
				return result;
			}
			var childargs = $"--sysdev-scan {kind} \"{output.Replace("\"", "\\\"")}\"";
			var script =
				"$ErrorActionPreference = 'Stop'\r\n" +
				"try {\r\n" +
				"  $p = Start-Process -FilePath " + psq(exe) +
				" -Verb RunAs -Wait -PassThru -ArgumentList " + psq(childargs) + "\r\n" +
				"  Set-Content -LiteralPath " + psq(codefile) +
				" -Value ([string]$p.ExitCode) -Encoding ascii\r\n" +
				"  exit $p.ExitCode\r\n" +
				"} catch {\r\n" +
				"  $m = $_.Exception.Message\r\n" +
				"  if ($m -match 'cancel|canceled|cancelled|1223|取消') {\r\n" +
				"    Set-Content -LiteralPath " + psq(codefile) + " -Value 'UAC' -Encoding ascii\r\n" +
				"    exit 1223\r\n" +
				"  }\r\n" +
				"  Set-Content -LiteralPath " + psq(codefile) +
				" -Value ('ERR ' + $m) -Encoding utf8\r\n" +
				"  exit 1\r\n" +
				"}\r\n";
			var psi = new ProcessStartInfo {
				FileName = "cmd.exe",
				Arguments = WinTtsPack.LaunchArgs(script),
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			using var process = Process.Start(psi);
			if (process == null) {
				result.Error = Loc.T("dev.admin.startfail");
				return result;
			}
			process.WaitForExit();
			var mark = read(codefile).Trim();
			if (mark == "UAC" || process.ExitCode == 1223) {
				result.UacDenied = true;
				result.Error = Loc.T("dev.admin.denied");
				return result;
			}
			if (mark.StartsWith("ERR ", StringComparison.Ordinal)) {
				result.Error = mark.Substring(4).Trim();
				return result;
			}
			if (!File.Exists(output)) {
				result.Error = Loc.T("dev.admin.noresult", process.ExitCode);
				return result;
			}
			var parsed = JsonSerializer.Deserialize<SysDeviceScanResult>(File.ReadAllText(output));
			if (parsed == null) result.Error = Loc.T("dev.admin.badresult");
			else {
				result.Rows = parsed.Rows ?? new List<UsbDev>();
				result.Error = parsed.Error ?? "";
			}
		}
		catch (Exception ex) {
			result.Error = ex.Message;
		}
		finally {
			try { if (File.Exists(output)) File.Delete(output); } catch { }
			try { if (File.Exists(codefile)) File.Delete(codefile); } catch { }
		}
		return result;
	}

	public static int RunChild(string kind, string output) {
		var result = new SysDeviceScanResult();
		var code = 0;
		try { result.Rows = SysDevices.Scan(kind); }
		catch (Exception ex) {
			result.Error = ex.Message;
			code = 1;
		}
		try {
			var dir = Path.GetDirectoryName(output);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			File.WriteAllText(output, JsonSerializer.Serialize(result), new UTF8Encoding(false));
		}
		catch { return 2; }
		return code;
	}

	static string read(string path) {
		try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
		catch { return ""; }
	}

	static string psq(string text) => "'" + (text ?? "").Replace("'", "''") + "'";
}
