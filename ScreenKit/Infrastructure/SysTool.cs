using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>本机只读查询：进程、文件占用、环境变量、启动项、已安装程序。</summary>
static class SysTool {
	public sealed class ProcRow {
		public int Pid;
		public string Name = "";
		public string Mem = "";
		public ulong MemBytes;
		public string Commit = "";
		public ulong CommitBytes;
		public uint Threads;
		public uint Handles;
		public string Start = "";
		public string Path = "";
		public string Cmd = "";
		public int Bits = 64;
		public int Elev = -1;
	}

	public sealed class LockRow {
		public int Pid;
		public string Name = "";
		public string Path = "";
	}

	public sealed class EnvRow {
		public string Scope = "";
		public string Name = "";
		public string Value = "";
	}

	public sealed class StartRow {
		public string Source = "";
		public string Name = "";
		public string Cmd = "";
	}

	public sealed class AppRow {
		public string Name = "";
		public string Ver = "";
		public string Pub = "";
		public string Date = "";
		public string Uninstall = "";
		public string Scope = "";
	}

	public sealed class SvcRow {
		public string Name = "";
		public string Disp = "";
		public string State = "";
		public string Start = "";
		public string Path = "";
	}

	public sealed class PatchRow {
		public string Id = "";
		public string Date = "";
		public string Desc = "";
	}

	public sealed class StoreRow {
		public string Name = "";
		public string Ver = "";
		public string Pub = "";
	}

	public sealed class AssocRow {
		public string Ext = "";
		public string App = "";
		public string Exe = "";
		public string Cmd = "";
	}

	public static List<ProcRow> Procs() {
		var list = new List<ProcRow>();
		using var search = new ManagementObjectSearcher(
			"SELECT ProcessId,Name,ExecutablePath,CommandLine,WorkingSetSize,ThreadCount,HandleCount,CreationDate FROM Win32_Process");
		foreach (ManagementObject mo in search.Get()) {
			using (mo) {
				var row = new ProcRow();
				row.Pid = toint(mo["ProcessId"]);
				row.Name = text(mo["Name"]);
				row.Path = text(mo["ExecutablePath"]);
				row.Cmd = text(mo["CommandLine"]);
				row.MemBytes = toulong(mo["WorkingSetSize"]);
				row.Mem = bytes(row.MemBytes);
				row.Threads = (uint)toulong(mo["ThreadCount"]);
				row.Handles = (uint)toulong(mo["HandleCount"]);
				row.Start = starttime(mo["CreationDate"]);
				if (row.Name.Length == 0) row.Name = row.Pid.ToString();
				procflags(row);
				list.Add(row);
			}
		}
		list.Sort((a, b) => {
			var c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
			if (c != 0) return c;
			return a.Pid - b.Pid;
		});
		return list;
	}

	static string starttime(object v) {
		try {
			if (v is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss");
			var s = v?.ToString() ?? "";
			// CIM_DATETIME：20261027091203.123456+480
			if (s.Length >= 14 && char.IsDigit(s[0]))
				return $"{s.Substring(0, 4)}-{s.Substring(4, 2)}-{s.Substring(6, 2)} {s.Substring(8, 2)}:{s.Substring(10, 2)}:{s.Substring(12, 2)}";
		}
		catch { }
		return "";
	}

	// 平台位数与是否提权。64 位系统上 WOW64 进程算 32 位；打不开的进程只给系统位数。
	static void procflags(ProcRow row) {
		row.Bits = Environment.Is64BitOperatingSystem ? 64 : 32;
		var h = OpenProcess(0x1000, false, row.Pid);
		if (h == IntPtr.Zero) return;
		try {
			if (IsWow64Process(h, out var wow) && wow) row.Bits = 32;
			meminfo(row, h);
			if (!OpenProcessToken(h, 0x0008, out var tok)) return;
			try {
				if (GetTokenInformation(tok, 20, out var elev, 4, out _)) row.Elev = elev != 0 ? 1 : 0;
			}
			finally { CloseHandle(tok); }
		}
		finally { CloseHandle(h); }
	}

	// 提交大小：任务管理器「提交大小」对应的 pagefile 用量。
	static void meminfo(ProcRow row, IntPtr h) {
		var pmc = new PMC();
		pmc.cb = (uint)Marshal.SizeOf(typeof(PMC));
		if (!GetProcessMemoryInfo(h, ref pmc, (int)pmc.cb)) return;
		row.CommitBytes = (ulong)pmc.PagefileUsage.ToInt64();
		row.Commit = bytes(row.CommitBytes);
	}

	public static List<LockRow> Locks(string path, out string err) {
		err = null;
		var list = new List<LockRow>();
		if (string.IsNullOrWhiteSpace(path)) {
			err = "empty";
			return list;
		}
		path = path.Trim().Trim('"');
		if (!File.Exists(path) && !Directory.Exists(path)) {
			err = "missing";
			return list;
		}
		var full = Path.GetFullPath(path);
		var key = new StringBuilder(33);
		var rc = RmStartSession(out var handle, 0, key);
		if (rc != 0) {
			err = $"rm {rc}";
			return list;
		}
		try {
			rc = RmRegisterResources(handle, 1, new[] { full }, 0, null, 0, null);
			if (rc != 0) {
				err = $"rm {rc}";
				return list;
			}
			uint needed = 0;
			uint count = 0;
			uint reasons = 0;
			rc = RmGetList(handle, out needed, ref count, null, ref reasons);
			if (rc == 0) return list;
			if (rc != 234) {
				err = $"rm {rc}";
				return list;
			}
			count = needed;
			var info = new RM_PROCESS_INFO[count];
			rc = RmGetList(handle, out needed, ref count, info, ref reasons);
			if (rc != 0 && rc != 234) {
				err = $"rm {rc}";
				return list;
			}
			var n = (int)count;
			if (n > info.Length) n = info.Length;
			for (var i = 0; i < n; i++) {
				var pid = info[i].Process.dwProcessId;
				var row = new LockRow {
					Pid = pid,
					Name = (info[i].strAppName ?? "").TrimEnd('\0'),
				};
				try {
					using var p = Process.GetProcessById(pid);
					row.Path = p.MainModule?.FileName ?? "";
					if (row.Name.Length == 0) row.Name = p.ProcessName;
				}
				catch { }
				if (row.Name.Length == 0) row.Name = pid.ToString();
				list.Add(row);
			}
		}
		finally {
			RmEndSession(handle);
		}
		return list;
	}

	public static List<EnvRow> Env(out string warn) {
		warn = null;
		var list = new List<EnvRow>();
		addenv(list, EnvironmentVariableTarget.User, "user");
		try { addenv(list, EnvironmentVariableTarget.Machine, "machine"); }
		catch (Exception ex) { warn = ex.Message; }
		list.Sort((a, b) => {
			var c = string.Compare(a.Scope, b.Scope, StringComparison.Ordinal);
			if (c != 0) return c;
			return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
		});
		return list;
	}

	public static List<StartRow> Startup() {
		var list = new List<StartRow>();
		readrun(list, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "run.user");
		readrun(list, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "run.machine");
		readrun(list, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "run.machine32");
		readfolder(list, Environment.SpecialFolder.Startup, "folder.user");
		readfolder(list, Environment.SpecialFolder.CommonStartup, "folder.common");
		readtasks(list);
		return list;
	}

	public static List<AppRow> Apps() {
		var list = new List<AppRow>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		readapps(list, seen, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "app.user");
		readapps(list, seen, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "app.machine");
		readapps(list, seen, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "app.machine32");
		list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	public static List<SvcRow> Services() {
		var list = new List<SvcRow>();
		using var search = new ManagementObjectSearcher(
			"SELECT Name, DisplayName, State, StartMode, PathName FROM Win32_Service");
		foreach (ManagementObject mo in search.Get()) {
			using (mo) {
				var row = new SvcRow {
					Name = text(mo["Name"]),
					Disp = text(mo["DisplayName"]),
					State = text(mo["State"]),
					Start = text(mo["StartMode"]),
					Path = text(mo["PathName"]),
				};
				if (row.Disp.Length == 0) row.Disp = row.Name;
				if (row.Name.Length == 0) continue;
				list.Add(row);
			}
		}
		list.Sort((a, b) => string.Compare(a.Disp, b.Disp, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	public static List<PatchRow> Patches() {
		var list = new List<PatchRow>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using var search = new ManagementObjectSearcher(
			"SELECT HotFixID, InstalledOn, Description FROM Win32_QuickFixEngineering");
		foreach (ManagementObject mo in search.Get()) {
			using (mo) {
				var id = text(mo["HotFixID"]);
				if (id.Length == 0 || !seen.Add(id)) continue;
				list.Add(new PatchRow {
					Id = id,
					Date = patchdate(mo["InstalledOn"]),
					Desc = text(mo["Description"]),
				});
			}
		}
		list.Sort((a, b) => string.Compare(b.Date, a.Date, StringComparison.Ordinal));
		return list;
	}

	public static List<StoreRow> Store(out string err) {
		err = null;
		var list = new List<StoreRow>();
		var psi = new ProcessStartInfo {
			FileName = "powershell.exe",
			Arguments = "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Get-AppxPackage | Where-Object { -not $_.IsFramework } | ForEach-Object { $_.Name + [char]9 + $_.Version + [char]9 + $_.PublisherDisplayName }\"",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
		};
		try {
			using var p = Process.Start(psi);
			if (p == null) {
				err = "powershell";
				return list;
			}
			var text = p.StandardOutput.ReadToEnd();
			if (!p.WaitForExit(25000)) {
				try { p.Kill(); } catch { }
				err = "timeout";
				return list;
			}
			foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
				var parts = line.Split('\t');
				if (parts.Length < 1) continue;
				var name = parts[0].Trim();
				if (name.Length == 0) continue;
				list.Add(new StoreRow {
					Name = name,
					Ver = parts.Length > 1 ? parts[1].Trim() : "",
					Pub = parts.Length > 2 ? parts[2].Trim() : "",
				});
			}
		}
		catch (Exception ex) {
			err = ex.Message;
		}
		list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	public sealed class ExtRow {
		public string Ext = "";
		public string Type = "";
		public string App = "";
		public string Exe = "";
		public string Cmd = "";
	}

	public static AssocRow Assoc(string ext, out string err) {
		err = null;
		var row = new AssocRow();
		ext = (ext ?? "").Trim().Trim('"');
		if (ext.Length == 0) {
			err = "empty";
			return row;
		}
		if (ext[0] != '.') ext = "." + ext;
		row.Ext = ext;
		row.App = assocstr(4, ext);
		row.Exe = assocstr(2, ext);
		row.Cmd = assocstr(1, ext);
		if (row.App.Length == 0 && row.Exe.Length == 0 && row.Cmd.Length == 0) err = "none";
		return row;
	}

	// 列出 HKCR 里注册的全部扩展名，逐个取生效的默认程序（含用户设置 UserChoice）。
	public static List<ExtRow> Exts() {
		var list = new List<ExtRow>();
		string[] names;
		try { names = Registry.ClassesRoot.GetSubKeyNames(); }
		catch { return list; }
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var name in names) {
			if (name.Length < 2 || name.Length > 32) continue;
			if (name[0] != '.') continue;
			if (name.IndexOf(' ') >= 0 || name.IndexOf('\\') >= 0) continue;
			if (!seen.Add(name)) continue;
			var row = new ExtRow { Ext = name };
			row.App = assocstr(4, name);
			row.Exe = assocstr(2, name);
			row.Cmd = assocstr(1, name);
			if (row.App.Length == 0 && row.Exe.Length == 0 && row.Cmd.Length == 0) continue;
			row.Type = assocstr(3, name);
			if (row.Type.Length == 0) row.Type = progid(name);
			list.Add(row);
		}
		list.Sort((a, b) => string.Compare(a.Ext, b.Ext, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	static string progid(string ext) {
		try {
			using var key = Registry.ClassesRoot.OpenSubKey(ext);
			return (key?.GetValue(null) as string ?? "").Trim();
		}
		catch { return ""; }
	}

	public sealed class MenuRow {
		public string Scope = "";
		public string Place = "";
		public string Name = "";
		public string Cmd = "";
		public string Company = "";
		public string Clsid = "";
		public string Key = "";
	}

	// 右键菜单：外壳扩展入口（全部用户 + 当前用户，HKCR 会掩盖 HKCU 的同名项，所以两边分开读）。
	static readonly string[] MENUROOTS = {
		@"*\shellex\ContextMenuHandlers",
		@"AllFilesystemObjects\shellex\ContextMenuHandlers",
		@"Directory\shellex\ContextMenuHandlers",
		@"Directory\Background\shellex\ContextMenuHandlers",
		@"Drive\shellex\ContextMenuHandlers",
		@"Folder\shellex\ContextMenuHandlers",
		@"DesktopBackground\shellex\ContextMenuHandlers",
	};

	// 静态命令（shell\verb）：按动作读，只列通用项。
	static readonly string[][] MENUVERBS = {
		new[] { "files", @"*\shell" },
		new[] { "any", @"AllFilesystemObjects\shell" },
		new[] { "files", @"SystemFileAssociations\*\shell" },
		new[] { "folder", @"Directory\shell" },
		new[] { "folder", @"AllFilesystemObjects\shell" },
		new[] { "folderbg", @"Directory\Background\shell" },
		new[] { "drive", @"Drive\shell" },
		new[] { "desktop", @"DesktopBackground\shell" },
	};

	public static List<MenuRow> Menus() {
		var list = new List<MenuRow>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		scanmenus(list, seen, Registry.LocalMachine, @"Software\Classes\", "machine");
		scanmenus(list, seen, Registry.CurrentUser, @"Software\Classes\", "user");
		foreach (var v in MENUVERBS) {
			scanverbs(list, seen, Registry.LocalMachine, @"Software\Classes\",
				v[1], "machine", v[0]);
			scanverbs(list, seen, Registry.CurrentUser, @"Software\Classes\",
				v[1], "user", v[0]);
		}
		list.Sort((a, b) => {
			var c = string.Compare(a.Place, b.Place, StringComparison.OrdinalIgnoreCase);
			if (c != 0) return c;
			c = string.Compare(a.Scope, b.Scope, StringComparison.OrdinalIgnoreCase);
			if (c != 0) return c;
			return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
		});
		return list;
	}

	static void scanmenus(List<MenuRow> list, HashSet<string> seen, RegistryKey root, string prefix, string scope) {
		foreach (var rel in MENUROOTS) {
			try {
				using var key = root.OpenSubKey(prefix + rel);
				if (key == null) continue;
				foreach (var name in key.GetSubKeyNames()) {
					var path = rel + @"\" + name;
					if (!seen.Add(scope + "|" + path)) continue;
					var row = new MenuRow {
						Scope = scope, Place = placeof(rel), Name = name,
						Key = (scope == "user" ? @"HKCU\" : @"HKLM\") + path,
					};
					using (var hk = key.OpenSubKey(name))
						row.Clsid = (hk?.GetValue(null) as string ?? "").Trim();
					row.Cmd = clsidpath(row.Clsid);
					row.Company = descr(row.Cmd);
					if (row.Name.Length == 0) row.Name = clsidname(row.Clsid);
					list.Add(row);
				}
			}
			catch { }
		}
	}

	static void scanverbs(List<MenuRow> list, HashSet<string> seen, RegistryKey root, string prefix,
			string rel, string scope, string place) {
		try {
			using var key = root.OpenSubKey(prefix + rel);
			if (key == null) return;
			foreach (var name in key.GetSubKeyNames()) {
				var path = rel + @"\" + name;
				if (!seen.Add(scope + "|" + path)) continue;
				var row = new MenuRow {
					Scope = scope, Place = place, Name = verbname(key, name),
					Key = (scope == "user" ? @"HKCU\" : @"HKLM\") + path,
				};
				using (var vk = key.OpenSubKey(name + @"\command"))
					row.Cmd = expand(vk?.GetValue(null) as string ?? "");
				var exe = exefrom(row.Cmd);
				if (exe.Length > 0) row.Company = descr(exe);
				if (row.Name.Length == 0) row.Name = name;
				list.Add(row);
			}
		}
		catch { }
	}

	static string placeof(string rel) {
		if (rel.StartsWith(@"Directory\Background", StringComparison.OrdinalIgnoreCase)) return "folderbg";
		if (rel.StartsWith("Directory", StringComparison.OrdinalIgnoreCase)) return "folder";
		if (rel.StartsWith("Folder", StringComparison.OrdinalIgnoreCase)) return "folder";
		if (rel.StartsWith("Drive", StringComparison.OrdinalIgnoreCase)) return "drive";
		if (rel.StartsWith("DesktopBackground", StringComparison.OrdinalIgnoreCase)) return "desktop";
		if (rel.StartsWith("AllFilesystemObjects", StringComparison.OrdinalIgnoreCase)) return "any";
		return "files";
	}

	// 菜单显示名：优先 MUIVerb，其次默认值，都没有就用键名。
	static string verbname(RegistryKey key, string name) {
		try {
			using var k = key.OpenSubKey(name);
			var s = k?.GetValue("MUIVerb") as string ?? "";
			if (s.Length == 0) s = k?.GetValue(null) as string ?? "";
			s = s.Trim();
			if (s.StartsWith("@", StringComparison.Ordinal)) s = resstr(s);
			return s;
		}
		catch { return ""; }
	}

	// @dll,-123 / @dll,#123 取本地化文本，读不到就留空。
	static string resstr(string s) {
		var h = IntPtr.Zero;
		try {
			var comma = s.LastIndexOf(',');
			if (comma < 0) return "";
			var file = expand(s.Substring(1, comma - 1));
			var id = s.Substring(comma + 1).TrimStart('#');
			if (!int.TryParse(id, out var idx) || !File.Exists(file)) return "";
			h = LoadLibraryEx(file, IntPtr.Zero, 0x00000002);
			if (h == IntPtr.Zero) return "";
			var buf = new StringBuilder(2048);
			var got = LoadString(h, idx, buf, buf.Capacity);
			return got > 0 ? buf.ToString() : "";
		}
		catch { return ""; }
		finally {
			if (h != IntPtr.Zero) CloseHandle(h);
		}
	}

	static string clsidpath(string clsid) {
		if (clsid.Length == 0) return "";
		if (clsid[0] != '{') return expand(clsid);
		return expand(dllof(clsid));
	}

	static string dllof(string clsid) {
		try {
			using var k = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid + @"\InprocServer32");
			return k?.GetValue(null) as string ?? "";
		}
		catch { return ""; }
	}

	static string clsidname(string clsid) {
		if (clsid.Length == 0) return "";
		try {
			using var k = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid);
			return (k?.GetValue(null) as string ?? "").Trim();
		}
		catch { return ""; }
	}

	// 命令行里的 exe：取第一个 .exe 令牌，运行库形式（rundll32 等）取不到就留空。
	static string exefrom(string cmd) {
		if (cmd.Length == 0) return "";
		var s = cmd.Trim();
		if (s.StartsWith("\"", StringComparison.Ordinal)) {
			var end = s.IndexOf('"', 1);
			return end > 1 ? s.Substring(1, end - 1) : "";
		}
		var i = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return "";
		return s.Substring(0, i + 4).Trim();
	}

	static string expand(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		try { return Environment.ExpandEnvironmentVariables(s.Trim().Trim('"')); }
		catch { return s; }
	}

	// 文件版本信息里的产品名／公司，用来大致看出是哪个程序挂的菜单。
	static string descr(string path) {
		if (string.IsNullOrEmpty(path)) return "";
		try {
			var v = FileVersionInfo.GetVersionInfo(path);
			if (!string.IsNullOrEmpty(v.ProductName)) return v.ProductName;
			return v.CompanyName ?? "";
		}
		catch { return ""; }
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern int LoadString(IntPtr h, int id, StringBuilder buf, int n);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr LoadLibraryEx(string file, IntPtr h, int flags);

	public static int Test(Action<string> log, Action<string> err) {
		log("=== 系统工具 --test-systool ===");
		var procs = Procs();
		log($"proc={procs.Count}");
		if (procs.Count < 8) {
			err("进程太少");
			return 1;
		}
		var self = Process.GetCurrentProcess().Id;
		var hit = false;
		foreach (var p in procs)
			if (p.Pid == self) hit = true;
		if (!hit) {
			err("进程列表没有本进程");
			return 1;
		}
		var selfrow = procs.Find(x => x.Pid == self);
		log($"self bits={selfrow.Bits} elev={selfrow.Elev}");
		if (selfrow.Bits != (Environment.Is64BitOperatingSystem ? 64 : 32) || selfrow.Elev < 0) {
			err("本进程的平台/权限不对");
			return 1;
		}
		log($"self start={selfrow.Start} threads={selfrow.Threads} handles={selfrow.Handles} commit={selfrow.Commit}");
		if (selfrow.Start.Length == 0 || selfrow.Threads == 0 || selfrow.Handles == 0 || selfrow.CommitBytes == 0) {
			err("本进程的启动时间/线程/句柄/提交大小不对");
			return 1;
		}
		var env = Env(out var warn);
		log($"env={env.Count} {warn}");
		if (env.Count < 1) {
			err("没有环境变量");
			return 1;
		}
		var apps = Apps();
		log($"app={apps.Count}");
		if (apps.Count < 3) {
			err("已安装程序太少");
			return 1;
		}
		var start = Startup();
		log($"start={start.Count}");
		var path = Path.Combine(Path.GetTempPath(), "sk-lock-" + Guid.NewGuid().ToString("N"));
		try {
			using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None)) {
				var locks = Locks(path, out var lockerr);
				log($"lock={locks.Count} {lockerr}");
				var mine = false;
				foreach (var row in locks)
					if (row.Pid == self) mine = true;
				if (!mine) {
					err("没有查到占用该文件的本进程");
					return 1;
				}
			}
		}
		finally {
			try { File.Delete(path); } catch { }
		}
		var miss = Locks(path + ".nope", out var misserr);
		if (miss.Count != 0 || misserr != "missing") {
			err("不存在的路径应记为 missing");
			return 1;
		}
		var svcs = Services();
		log($"svc={svcs.Count}");
		if (svcs.Count < 8) {
			err("服务太少");
			return 1;
		}
		var svcpath = false;
		foreach (var s in svcs)
			if (s.Path.Length > 0) svcpath = true;
		if (!svcpath) {
			err("服务没有可执行路径");
			return 1;
		}
		var patches = Patches();
		log($"patch={patches.Count}");
		var store = Store(out var storeerr);
		log($"store={store.Count} {storeerr}");
		if (storeerr != null) {
			err($"商店应用 {storeerr}");
			return 1;
		}
		if (store.Count < 3) {
			err("商店应用太少");
			return 1;
		}
		var assoc = Assoc(".txt", out var assocerr);
		log($"assoc={assoc.App} {assoc.Exe} {assocerr}");
		if (assocerr != null || assoc.Exe.Length == 0) {
			err("txt 没有默认程序");
			return 1;
		}
		var exts = Exts();
		log($"ext={exts.Count}");
		if (exts.Count < 50) {
			err("文件关联太少");
			return 1;
		}
		var menus = Menus();
		log($"menu={menus.Count}");
		if (menus.Count < 10) {
			err("右键菜单项太少");
			return 1;
		}
		log("=== OK：系统工具 ===");
		return 0;
	}

	static void addenv(List<EnvRow> list, EnvironmentVariableTarget target, string scope) {
		var raw = Environment.GetEnvironmentVariables(target);
		foreach (System.Collections.DictionaryEntry e in raw) {
			var name = e.Key as string ?? "";
			if (name.Length == 0) continue;
			list.Add(new EnvRow { Scope = scope, Name = name, Value = e.Value as string ?? "" });
		}
	}

	static void readrun(List<StartRow> list, RegistryKey root, string path, string source) {
		try {
			using var key = root.OpenSubKey(path);
			if (key == null) return;
			foreach (var name in key.GetValueNames()) {
				var cmd = key.GetValue(name) as string ?? "";
				if (name.Length == 0 && cmd.Length == 0) continue;
				list.Add(new StartRow { Source = source, Name = name, Cmd = cmd });
			}
		}
		catch { }
	}

	static void readfolder(List<StartRow> list, Environment.SpecialFolder folder, string source) {
		try {
			var dir = Environment.GetFolderPath(folder);
			if (dir.Length == 0 || !Directory.Exists(dir)) return;
			foreach (var file in Directory.GetFiles(dir)) {
				var name = Path.GetFileName(file);
				if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
				list.Add(new StartRow { Source = source, Name = name, Cmd = linkof(file) });
			}
		}
		catch { }
	}

	static string linkof(string file) {
		if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return file;
		try {
			var t = Type.GetTypeFromProgID("WScript.Shell");
			if (t == null) return file;
			dynamic shell = Activator.CreateInstance(t);
			dynamic sc = shell.CreateShortcut(file);
			var target = (string)sc.TargetPath ?? "";
			var args = (string)sc.Arguments ?? "";
			if (target.Length == 0) return file;
			if (args.Length == 0) return target;
			return $"{target} {args}";
		}
		catch { return file; }
	}

	static void readtasks(List<StartRow> list) {
		var th = new Thread(() => {
			try { readtaskssta(list); }
			catch { }
		});
		th.SetApartmentState(ApartmentState.STA);
		th.Start();
		th.Join();
	}

	static void readtaskssta(List<StartRow> list) {
		var t = Type.GetTypeFromProgID("Schedule.Service");
		if (t == null) return;
		dynamic svc = Activator.CreateInstance(t);
		svc.Connect();
		walktasks(list, svc.GetFolder("\\"));
	}

	static void walktasks(List<StartRow> list, dynamic folder) {
		try {
			dynamic tasks = folder.GetTasks(1);
			int n = tasks.Count;
			for (var i = 1; i <= n; i++) {
				try { onetask(list, tasks[i]); }
				catch { }
			}
		}
		catch { }
		try {
			dynamic subs = folder.GetFolders(0);
			int n = subs.Count;
			for (var i = 1; i <= n; i++) {
				try { walktasks(list, subs[i]); }
				catch { }
			}
		}
		catch { }
	}

	static void onetask(List<StartRow> list, dynamic task) {
		dynamic def = task.Definition;
		dynamic triggers = def.Triggers;
		var boot = false;
		var logon = false;
		int n = triggers.Count;
		for (var i = 1; i <= n; i++) {
			int kind = triggers[i].Type;
			if (kind == 8) boot = true;
			if (kind == 9) logon = true;
		}
		if (!boot && !logon) return;
		var name = (string)task.Name ?? "";
		var path = "";
		try { path = (string)task.Path ?? ""; } catch { }
		if (path.Length > 0) name = path;
		var cmd = "";
		try {
			dynamic actions = def.Actions;
			if (actions.Count >= 1) {
				dynamic act = actions[1];
				var exe = (string)act.Path ?? "";
				var args = "";
				try { args = (string)act.Arguments ?? ""; } catch { }
				cmd = args.Length == 0 ? exe : $"{exe} {args}";
			}
		}
		catch { }
		if (logon) list.Add(new StartRow { Source = "task.logon", Name = name, Cmd = cmd });
		if (boot) list.Add(new StartRow { Source = "task.boot", Name = name, Cmd = cmd });
	}

	static void readapps(List<AppRow> list, HashSet<string> seen, RegistryKey root, string path, string scope) {
		try {
			using var key = root.OpenSubKey(path);
			if (key == null) return;
			foreach (var subname in key.GetSubKeyNames()) {
				try {
					using var sub = key.OpenSubKey(subname);
					if (sub == null) continue;
					var name = sub.GetValue("DisplayName") as string;
					if (string.IsNullOrWhiteSpace(name)) continue;
					if (sub.GetValue("SystemComponent") is int sys && sys == 1) continue;
					var parent = sub.GetValue("ParentKeyName") as string;
					if (!string.IsNullOrWhiteSpace(parent)) continue;
					var ver = sub.GetValue("DisplayVersion") as string ?? "";
					var id = $"{name}\n{ver}";
					if (!seen.Add(id)) continue;
					var date = sub.GetValue("InstallDate") as string ?? "";
					if (date.Length == 8) date = $"{date.Substring(0, 4)}-{date.Substring(4, 2)}-{date.Substring(6, 2)}";
					list.Add(new AppRow {
						Name = name.Trim(),
						Ver = ver.Trim(),
						Pub = (sub.GetValue("Publisher") as string ?? "").Trim(),
						Date = date.Trim(),
						Uninstall = (sub.GetValue("UninstallString") as string ?? "").Trim(),
						Scope = scope,
					});
				}
				catch { }
			}
		}
		catch { }
	}

	static string assocstr(int kind, string ext) {
		var buf = new StringBuilder(1024);
		var n = buf.Capacity;
		var rc = AssocQueryString(0, kind, ext, null, buf, ref n);
		if (rc != 0) return "";
		return buf.ToString().Trim();
	}

	static string patchdate(object v) {
		if (v == null) return "";
		var s = v.ToString().Trim();
		if (s.Length >= 8 && char.IsDigit(s[0])) {
			var digits = 0;
			foreach (var c in s) {
				if (!char.IsDigit(c)) break;
				digits++;
			}
			if (digits >= 8)
				return $"{s.Substring(0, 4)}-{s.Substring(4, 2)}-{s.Substring(6, 2)}";
		}
		if (DateTime.TryParse(s, out var dt)) return dt.ToString("yyyy-MM-dd");
		return s;
	}

	static string text(object v) => v as string ?? "";

	static int toint(object v) {
		if (v == null) return 0;
		try { return Convert.ToInt32(v); }
		catch { return 0; }
	}

	static ulong toulong(object v) {
		if (v == null) return 0;
		try { return Convert.ToUInt64(v); }
		catch { return 0; }
	}

	static string bytes(ulong n) {
		const double kb = 1024;
		const double mb = 1024 * 1024;
		const double gb = 1024 * 1024 * 1024;
		if (n >= gb) return $"{n / gb:0.0} GB";
		if (n >= mb) return $"{n / mb:0} MB";
		if (n >= kb) return $"{n / kb:0} KB";
		return $"{n} B";
	}

	[DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
	static extern int AssocQueryString(int flags, int str, string assoc, string extra, StringBuilder pszOut, ref int pcchOut);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool IsWow64Process(IntPtr h, out bool wow);

	[DllImport("advapi32.dll", SetLastError = true)]
	static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);

	[DllImport("advapi32.dll", SetLastError = true)]
	static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);

	[DllImport("kernel32.dll")]
	static extern bool CloseHandle(IntPtr h);

	[DllImport("psapi.dll")]
	static extern bool GetProcessMemoryInfo(IntPtr h, ref PMC pmc, int cb);

	[StructLayout(LayoutKind.Sequential)]
	struct PMC {
		public uint cb;
		public uint PageFaultCount;
		public IntPtr PeakWorkingSetSize;
		public IntPtr WorkingSetSize;
		public IntPtr QuotaPeakPagedPoolUsage;
		public IntPtr QuotaPagedPoolUsage;
		public IntPtr QuotaPeakNonPagedPoolUsage;
		public IntPtr QuotaNonPagedPoolUsage;
		public IntPtr PagefileUsage;
		public IntPtr PeakPagefileUsage;
		public IntPtr PrivateUsage;
	}

	[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
	static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

	[DllImport("rstrtmgr.dll")]
	static extern int RmEndSession(uint pSessionHandle);

	[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
	static extern int RmRegisterResources(
		uint pSessionHandle,
		uint nFiles,
		string[] rgsFileNames,
		uint nApplications,
		RM_UNIQUE_PROCESS[] rgApplications,
		uint nServices,
		string[] rgsServiceNames);

	[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
	static extern int RmGetList(
		uint dwSessionHandle,
		out uint pnProcInfoNeeded,
		ref uint pnProcInfo,
		[In, Out] RM_PROCESS_INFO[] rgAffectedApps,
		ref uint lpdwRebootReasons);

	[StructLayout(LayoutKind.Sequential)]
	struct RM_UNIQUE_PROCESS {
		public int dwProcessId;
		public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct RM_PROCESS_INFO {
		public RM_UNIQUE_PROCESS Process;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string strAppName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string strServiceShortName;
		public uint ApplicationType;
		public uint AppStatus;
		public uint TSSessionId;
		[MarshalAs(UnmanagedType.Bool)]
		public bool bRestartable;
	}
}
