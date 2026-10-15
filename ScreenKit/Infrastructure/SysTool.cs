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
		public string Path = "";
		public string Cmd = "";
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

	public static List<ProcRow> Procs() {
		var list = new List<ProcRow>();
		using var search = new ManagementObjectSearcher(
			"SELECT ProcessId,Name,ExecutablePath,CommandLine,WorkingSetSize FROM Win32_Process");
		foreach (ManagementObject mo in search.Get()) {
			using (mo) {
				var row = new ProcRow();
				row.Pid = toint(mo["ProcessId"]);
				row.Name = text(mo["Name"]);
				row.Path = text(mo["ExecutablePath"]);
				row.Cmd = text(mo["CommandLine"]);
				row.Mem = bytes(toulong(mo["WorkingSetSize"]));
				if (row.Name.Length == 0) row.Name = row.Pid.ToString();
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
