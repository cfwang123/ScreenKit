using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ScreenKit;

/// <summary>
/// 快速复制：复制 / 剪切文件和文件夹，并把当前用户的资源管理器右键菜单接到本程序。
/// 多选时资源管理器会每个文件起一个进程，这里在短时间里收成一批。
/// </summary>
static class FastCopy {
	public const int WORKERS_MIN = 1;
	public const int WORKERS_MAX = 32;
	public const int WORKERS_DEF = 8;
	const int BUF = 1 << 20;
	const int MERGE_MS = 2000;
	const int OWNER_WAIT_MS = 450;
	const string VERB = "ScreenKitFc";
	const string PASTE = "ScreenKitFcPaste";
	const string CLEAR = "ScreenKitFcClear";

	public sealed class Job {
		public List<string> Sources = new();
		public string Dest;
		public bool Move;
		public string Conflict = "overwrite";
		public int Workers = WORKERS_DEF;
		public CancellationToken Cancel;
		public Action<Progress> OnProgress;
	}

	public sealed class Progress {
		public string File;
		public long DoneBytes;
		public long TotalBytes;
		public int DoneFiles;
		public int TotalFiles;
	}

	public sealed class Result {
		public int Ok;
		public int Skipped;
		public int Failed;
		public bool Cancelled;
		public string Error;
	}

	sealed class Work {
		public string Src;
		public string Dest;
		public long Size;
		public bool Copied;
	}

	sealed class ClipDto {
		public string Kind { get; set; }
		public List<string> Paths { get; set; }
	}

	[DllImport("user32.dll")]
	static extern short GetAsyncKeyState(int vKey);

	[DllImport("shell32.dll")]
	static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

	const int SHCNE_ASSOCCHANGED = 0x08000000;
	const uint SHCNF_IDLIST = 0x1000;

	public static int NormWorkers(int n) => Compat.Clamp(n <= 0 ? WORKERS_DEF : n, WORKERS_MIN, WORKERS_MAX);

	public static string NormConflict(string raw) {
		raw = (raw ?? "").Trim().ToLowerInvariant();
		if (raw is "skip" or "rename") return raw;
		return "overwrite";
	}

	public static string NormMode(string raw) =>
		string.Equals((raw ?? "").Trim(), "move", StringComparison.OrdinalIgnoreCase) ? "move" : "copy";

	public static bool IsShell(string[] args) {
		if (args == null) return false;
		foreach (var a in args) {
			if (a is "--fc-copy" or "--fc-cut" or "--fc-delete" or "--fc-paths"
				or "--fc-paste" or "--fc-clear" or "--fc-register" or "--fc-unregister")
				return true;
		}
		return false;
	}

	public static bool NeedsWindow(string[] args) {
		if (args == null) return false;
		foreach (var a in args)
			if (a == "--fc-paste") return true;
		return false;
	}

	// ==================== 复制 ====================
	#region

	public static Result Run(Job job) {
		var res = new Result();
		if (job == null) {
			res.Failed = 1;
			res.Error = "job";
			return res;
		}
		var dest = (job.Dest ?? "").Trim();
		if (dest.Length == 0 || !trydir(dest)) {
			res.Failed = 1;
			res.Error = "dest";
			return res;
		}
		try { Directory.CreateDirectory(dest); }
		catch (Exception ex) {
			res.Failed = 1;
			res.Error = ex.Message;
			return res;
		}
		var conflict = NormConflict(job.Conflict);
		var items = new List<Work>();
		var sweep = new List<string>();
		foreach (var raw in job.Sources ?? new List<string>()) {
			if (job.Cancel.IsCancellationRequested) {
				res.Cancelled = true;
				return res;
			}
			var src = (raw ?? "").Trim().Trim('"');
			if (src.Length == 0) continue;
			plan(src, dest, job.Move, conflict, items, sweep, res);
		}
		if (res.Cancelled) return res;
		if (items.Count == 0 && res.Failed == 0 && res.Skipped == 0) {
			res.Failed = 1;
			res.Error = "empty";
			return res;
		}
		long total = 0;
		foreach (var it in items) total += it.Size;
		int doneFiles = 0;
		long doneBytes = 0;
		int nextui = Environment.TickCount - 1000;
		void ping(string file, bool force) {
			var now = Environment.TickCount;
			if (!force && unchecked(now - nextui) < 80) return;
			nextui = now;
			try {
				job.OnProgress?.Invoke(new Progress {
					File = file ?? "",
					DoneBytes = Interlocked.Read(ref doneBytes),
					TotalBytes = total,
					DoneFiles = Volatile.Read(ref doneFiles),
					TotalFiles = items.Count,
				});
			}
			catch { }
		}
		var po = new ParallelOptions {
			MaxDegreeOfParallelism = NormWorkers(job.Workers),
			CancellationToken = job.Cancel,
		};
		try {
			Parallel.ForEach(items, po, it => {
				job.Cancel.ThrowIfCancellationRequested();
				copyone(it, job.Move, conflict, job.Cancel, res, ref doneBytes, () => ping(it.Src, false));
				if (it.Copied) Interlocked.Increment(ref doneFiles);
				ping(it.Src, false);
			});
		}
		catch (OperationCanceledException) { res.Cancelled = true; }
		catch (AggregateException ex) {
			if (ex.InnerExceptions.All(e => e is OperationCanceledException)) res.Cancelled = true;
			else {
				res.Failed++;
				res.Error ??= ex.InnerException?.Message ?? ex.Message;
			}
		}
		if (job.Move && !res.Cancelled) {
			foreach (var dir in sweep) sweepempty(dir);
		}
		ping("", true);
		return res;
	}

	static void plan(string src, string destDir, bool move, string conflict, List<Work> items, List<string> sweep, Result res) {
		if (File.Exists(src)) {
			var to = Path.Combine(destDir, Path.GetFileName(src));
			if (samepath(src, to)) { res.Skipped++; return; }
			long size = 0;
			try { size = new FileInfo(src).Length; } catch { }
			if (File.Exists(to) && conflict == "skip") { res.Skipped++; return; }
			if (move && samevol(src, destDir) && !File.Exists(to) && !Directory.Exists(to) && trymovefile(src, to, res))
				return;
			items.Add(new Work { Src = full(src), Dest = full(to), Size = size });
			return;
		}
		if (!Directory.Exists(src)) {
			res.Failed++;
			res.Error ??= src;
			return;
		}
		var top = Path.Combine(destDir, Path.GetFileName(src.TrimEnd('\\', '/')));
		if (samepath(src, top) || isinside(src, destDir)) {
			res.Failed++;
			res.Error ??= "inside";
			return;
		}
		if (move && samevol(src, destDir) && !Directory.Exists(top) && !File.Exists(top)) {
			try {
				Directory.CreateDirectory(destDir);
				Directory.Move(src, top);
				res.Ok++;
				return;
			}
			catch { }
		}
		sweep.Add(full(src));
		walkdir(src, top, items, res);
	}

	static bool trymovefile(string src, string dest, Result res) {
		try {
			var parent = Path.GetDirectoryName(dest);
			if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
			File.Move(src, dest);
			res.Ok++;
			return true;
		}
		catch { return false; }
	}

	static void walkdir(string src, string dest, List<Work> items, Result res) {
		try {
			Directory.CreateDirectory(dest);
			foreach (var file in Directory.EnumerateFiles(src)) {
				long size = 0;
				try { size = new FileInfo(file).Length; } catch { }
				var to = Path.Combine(dest, Path.GetFileName(file));
				if (samepath(file, to)) { res.Skipped++; continue; }
				items.Add(new Work { Src = full(file), Dest = full(to), Size = size });
			}
			foreach (var sub in Directory.EnumerateDirectories(src)) {
				FileAttributes attr;
				try { attr = File.GetAttributes(sub); }
				catch { continue; }
				if ((attr & FileAttributes.ReparsePoint) != 0) continue;
				walkdir(sub, Path.Combine(dest, Path.GetFileName(sub)), items, res);
			}
		}
		catch (Exception ex) {
			res.Failed++;
			res.Error ??= ex.Message;
		}
	}

	static void copyone(Work it, bool move, string conflict, CancellationToken cancel, Result res, ref long doneBytes, Action ping) {
		var dest = it.Dest;
		try {
			if (File.Exists(dest) || Directory.Exists(dest)) {
				if (conflict == "skip") {
					lock (res) res.Skipped++;
					return;
				}
				if (conflict == "rename") dest = uniquename(dest);
			}
			var parent = Path.GetDirectoryName(dest);
			if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
			if (move && samevol(it.Src, dest) && !File.Exists(dest)) {
				File.Move(it.Src, dest);
				Interlocked.Add(ref doneBytes, it.Size);
				it.Copied = true;
				lock (res) res.Ok++;
				return;
			}
			var tmp = dest + ".skcopy";
			var committed = false;
			try {
				using (var inn = new FileStream(it.Src, FileMode.Open, FileAccess.Read, FileShare.Read, BUF, FileOptions.SequentialScan))
				using (var outt = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, BUF, FileOptions.SequentialScan)) {
					var buf = new byte[BUF];
					int n;
					while ((n = inn.Read(buf, 0, buf.Length)) > 0) {
						cancel.ThrowIfCancellationRequested();
						outt.Write(buf, 0, n);
						Interlocked.Add(ref doneBytes, n);
						ping();
					}
				}
				if (File.Exists(dest)) File.Replace(tmp, dest, null, true);
				else File.Move(tmp, dest);
				committed = true;
				try { File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(it.Src)); } catch { }
				if (move) File.Delete(it.Src);
				it.Copied = true;
				lock (res) res.Ok++;
			}
			finally {
				if (!committed) {
					try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			lock (res) {
				res.Failed++;
				res.Error ??= ex.Message;
			}
		}
	}

	static void sweepempty(string dir) {
		try {
			if (!Directory.Exists(dir)) return;
			foreach (var sub in Directory.GetDirectories(dir)) sweepempty(sub);
			if (Directory.GetFileSystemEntries(dir).Length == 0)
				Directory.Delete(dir);
		}
		catch { }
	}

	static string uniquename(string dest) {
		var dir = Path.GetDirectoryName(dest) ?? "";
		var name = Path.GetFileNameWithoutExtension(dest);
		var ext = Path.GetExtension(dest);
		for (int i = 2; i < 10000; i++) {
			var n = Path.Combine(dir, $"{name} ({i}){ext}");
			if (!File.Exists(n) && !Directory.Exists(n)) return n;
		}
		return dest;
	}

	static bool trydir(string path) {
		try {
			if (File.Exists(path)) return false;
			return true;
		}
		catch { return false; }
	}

	static string full(string path) {
		try { return Path.GetFullPath(path).TrimEnd('\\'); }
		catch { return path ?? ""; }
	}

	static bool samepath(string a, string b) =>
		string.Equals(full(a), full(b), StringComparison.OrdinalIgnoreCase);

	static bool isinside(string parent, string child) {
		var p = full(parent) + "\\";
		return full(child).StartsWith(p, StringComparison.OrdinalIgnoreCase);
	}

	static bool samevol(string a, string b) {
		try {
			var ra = Path.GetPathRoot(full(a));
			var rb = Path.GetPathRoot(full(b));
			return !string.IsNullOrEmpty(ra) && string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}

	#endregion

	// ==================== 选择合并 ====================
	#region

	static List<string> claim(string kind, IList<string> paths, int waitMs) {
		var add = new List<string>();
		if (paths != null) {
			foreach (var p in paths) {
				var s = (p ?? "").Trim().Trim('"');
				if (s.Length == 0) continue;
				add.Add(s);
			}
		}
		withmutex(dataMutex(kind), () => mergefile(kind, add));
		Mutex own = null;
		var got = false;
		try {
			own = new Mutex(false, ownMutex(kind));
			try { got = own.WaitOne(0); }
			catch (AbandonedMutexException) { got = true; }
		}
		catch { return null; }
		if (!got) {
			own.Dispose();
			return null;
		}
		try {
			if (waitMs > 0) Thread.Sleep(waitMs);
			List<string> batch = null;
			withmutex(dataMutex(kind), () => {
				batch = readfile(kind);
				try { File.Delete(selfile(kind)); } catch { }
			});
			return batch ?? new List<string>();
		}
		finally {
			try { own.ReleaseMutex(); } catch { }
			own.Dispose();
		}
	}

	static void mergefile(string kind, List<string> add) {
		var now = Environment.TickCount;
		var list = readfile(kind);
		var path = selfile(kind);
		int old = 0;
		var fresh = true;
		if (File.Exists(path)) {
			try {
				var first = File.ReadLines(path).FirstOrDefault();
				fresh = int.TryParse(first, out old);
			}
			catch { fresh = false; }
		}
		if (!fresh || unchecked(now - old) < 0 || unchecked(now - old) > MERGE_MS)
			list.Clear();
		foreach (var p in add) {
			if (list.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) continue;
			list.Add(p);
		}
		writefile(kind, now, list);
	}

	static List<string> readfile(string kind) {
		var list = new List<string>();
		var path = selfile(kind);
		if (!File.Exists(path)) return list;
		try {
			var lines = File.ReadAllLines(path);
			for (int i = 1; i < lines.Length; i++) {
				var s = lines[i];
				if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
			}
		}
		catch { }
		return list;
	}

	static void writefile(string kind, int tick, List<string> paths) {
		var sb = new StringBuilder();
		sb.Append(tick);
		foreach (var p in paths) {
			sb.Append('\n');
			sb.Append(p);
		}
		Directory.CreateDirectory(datadir());
		File.WriteAllText(selfile(kind), sb.ToString(), new UTF8Encoding(false));
	}

	static void withmutex(string name, Action act) {
		using var m = new Mutex(false, name);
		try { m.WaitOne(8000); }
		catch (AbandonedMutexException) { }
		try { act(); }
		finally { try { m.ReleaseMutex(); } catch { } }
	}

	static string datadir() {
		var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
		var dir = Path.Combine(root, "ScreenKit");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static string selfile(string kind) => Path.Combine(datadir(), "fc-sel-" + kind + ".txt");
	static string dataMutex(string kind) => "Local\\ScreenKit_FcData_" + kind;
	static string ownMutex(string kind) => "Local\\ScreenKit_Fc_" + kind;

	#endregion

	// ==================== 剪贴板与菜单 ====================
	#region

	public static int RunShell(string[] args) {
		loadlang();
		string verb = null;
		var paths = new List<string>();
		if (args != null) {
			foreach (var a in args) {
				if (a is "--fc-copy" or "--fc-cut" or "--fc-delete" or "--fc-paths"
					or "--fc-paste" or "--fc-clear" or "--fc-register" or "--fc-unregister")
					verb ??= a;
				else if (!string.IsNullOrWhiteSpace(a) && !a.StartsWith("-"))
					paths.Add(a.Trim().Trim('"'));
			}
		}
		try {
			switch (verb) {
				case "--fc-register":
					Register();
					return 0;
				case "--fc-unregister":
					Unregister();
					return 0;
				case "--fc-clear":
					setclip(null, null);
					return 0;
				case "--fc-copy":
				case "--fc-cut":
					return shellclip(verb == "--fc-cut" ? "move" : "copy", paths);
				case "--fc-delete":
					return shelldelete(paths);
				case "--fc-paths":
					return shellpaths(paths);
				case "--fc-paste":
					return shellpaste(paths.Count > 0 ? paths[0] : "");
				default:
					return 2;
			}
		}
		catch (Exception ex) {
			fclog(verb + " " + ex.Message);
			tell(ex.Message);
			return 1;
		}
	}

	static int shellclip(string kind, List<string> paths) {
		var batch = claim(kind, paths, OWNER_WAIT_MS);
		if (batch == null) return 0;
		if (batch.Count == 0) return 2;
		setclip(kind, batch);
		fclog($"{kind} {batch.Count}");
		return 0;
	}

	static int shelldelete(List<string> paths) {
		var batch = claim("delete", paths, OWNER_WAIT_MS);
		if (batch == null) return 0;
		if (batch.Count == 0) return 0;
		try {
			RecycleBin.Send(batch);
			fclog($"delete {batch.Count}");
			return 0;
		}
		catch (Exception ex) {
			fclog("delete " + ex.Message);
			tell(ex.Message);
			return 1;
		}
	}

	static int shellpaths(List<string> paths) {
		var batch = claim("paths", paths, OWNER_WAIT_MS);
		if (batch == null) return 0;
		if (batch.Count == 0) return 2;
		var text = PathText(batch, shift());
		for (int i = 0; i < 8; i++) {
			try {
				Clipboard.SetText(text);
				return 0;
			}
			catch { Thread.Sleep(40); }
		}
		return 1;
	}

	static int shellpaste(string dest) {
		dest = (dest ?? "").Trim().Trim('"');
		if (dest.Length == 0 || !Directory.Exists(dest)) {
			tell(Loc.T("fc.nodest"));
			return 2;
		}
		ClipDto clip = null;
		var keep = shift();
		withmutex("Local\\ScreenKit_FcClip", () => clip = readclip());
		if (clip == null || clip.Paths == null || clip.Paths.Count == 0) {
			tell(Loc.T("fc.clip.empty"));
			return 2;
		}
		if (!keep) setclip(null, null);
		var opt = loadopt();
		var job = new Job {
			Dest = dest,
			Move = string.Equals(clip.Kind, "move", StringComparison.OrdinalIgnoreCase),
			Conflict = NormConflict(opt.FastCopyConflict),
			Workers = NormWorkers(opt.FastCopyWorkers),
			Sources = clip.Paths,
		};
		var win = new FastCopyProgWindow(job);
		win.ShowDialog();
		fclog($"paste code={win.Code} ok={win.Ok} fail={win.Failed}");
		return win.Code;
	}

	public static void Register() {
		loadlang();
		using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
		if (root == null) throw new IOException("HKCU");
		foreach (var rel in new[] { @"*\shell", @"Directory\shell", @"Drive\shell" })
			writecascade(root, rel + "\\" + VERB);
		syncbg(hasclip());
		notify();
	}

	public static void Unregister() {
		using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
		if (root == null) return;
		foreach (var rel in new[] {
			@"*\shell\" + VERB,
			@"Directory\shell\" + VERB,
			@"Drive\shell\" + VERB,
			@"Directory\Background\shell\" + PASTE,
			@"Directory\Background\shell\" + CLEAR,
			@"Drive\Background\shell\" + PASTE,
			@"Drive\Background\shell\" + CLEAR,
		})
			deltree(root, rel);
		notify();
	}

	public static bool MenuExists() {
		try {
			using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\" + VERB);
			return k != null;
		}
		catch { return false; }
	}

	public static bool IsRegistered() {
		try {
			using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\" + VERB + @"\shell\2copy\command");
			var cmd = k?.GetValue("") as string ?? "";
			var exe = exepath();
			return cmd.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0
				&& cmd.IndexOf("--fc-copy", StringComparison.Ordinal) >= 0;
		}
		catch { return false; }
	}

	static void writecascade(RegistryKey root, string path) {
		using var key = root.CreateSubKey(path);
		key.SetValue("MUIVerb", Loc.T("fc.menu"));
		key.SetValue("SubCommands", "");
		key.SetValue("Position", "Top");
		key.SetValue("NeverDefault", "");
		try { key.SetValue("Icon", exepath()); } catch { }
		writechild(key, "1cut", Loc.T("fc.menucut"), "--fc-cut");
		writechild(key, "2copy", Loc.T("fc.menucopy"), "--fc-copy");
		writechild(key, "3delete", Loc.T("fc.menudel"), "--fc-delete");
		writechild(key, "4paths", Loc.T("fc.menupath"), "--fc-paths");
	}

	static void writechild(RegistryKey parent, string name, string label, string flag) {
		using var key = parent.CreateSubKey(@"shell\" + name);
		key.SetValue("MUIVerb", label);
		key.SetValue("MultiSelectModel", "Document");
		key.SetValue("NeverDefault", "");
		using var cmd = key.CreateSubKey("command");
		cmd.SetValue("", command(flag));
	}

	static void syncbg(bool show) {
		using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
		if (root == null) return;
		foreach (var scope in new[] { @"Directory\Background\shell", @"Drive\Background\shell" }) {
			if (!show) {
				deltree(root, scope + "\\" + PASTE);
				deltree(root, scope + "\\" + CLEAR);
				continue;
			}
			writebg(root, scope + "\\" + PASTE, Loc.T("fc.menupaste"), "--fc-paste \"%V\"");
			writebg(root, scope + "\\" + CLEAR, Loc.T("fc.menuclear"), "--fc-clear");
		}
	}

	static void writebg(RegistryKey root, string path, string label, string args) {
		using var key = root.CreateSubKey(path);
		key.SetValue("MUIVerb", label);
		key.SetValue("Position", "Top");
		key.SetValue("NeverDefault", "");
		using var cmd = key.CreateSubKey("command");
		cmd.SetValue("", "\"" + exepath() + "\" " + args);
	}

	static string command(string flag) => "\"" + exepath() + "\" " + flag + " \"%1\"";

	static string exepath() {
		try {
			var p = Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrEmpty(p)) return p;
		}
		catch { }
		return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenKit.exe");
	}

	static void deltree(RegistryKey root, string sub) {
		try { root.DeleteSubKeyTree(sub, false); }
		catch { }
	}

	static void notify() {
		try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
		catch { }
	}

	static void setclip(string kind, List<string> paths) {
		withmutex("Local\\ScreenKit_FcClip", () => {
			var path = clippath();
			if (paths == null || paths.Count == 0) {
				try { if (File.Exists(path)) File.Delete(path); } catch { }
			}
			else {
				var dto = new ClipDto { Kind = kind, Paths = paths };
				File.WriteAllText(path, JsonSerializer.Serialize(dto), new UTF8Encoding(false));
			}
		});
		try {
			syncbg(paths != null && paths.Count > 0);
			notify();
		}
		catch (Exception ex) { fclog("menu " + ex.Message); }
	}

	static bool hasclip() {
		var clip = readclip();
		return clip?.Paths != null && clip.Paths.Count > 0;
	}

	static ClipDto readclip() {
		try {
			var path = clippath();
			if (!File.Exists(path)) return null;
			return JsonSerializer.Deserialize<ClipDto>(File.ReadAllText(path, Encoding.UTF8));
		}
		catch { return null; }
	}

	static string clippath() => Path.Combine(datadir(), "fc-clip.json");

	static bool shift() => (GetAsyncKeyState(0x10) & 0x8000) != 0;

	static string PathText(IList<string> paths, bool relative) {
		var list = new List<string>();
		if (paths != null) {
			foreach (var p in paths) {
				var s = full(p);
				if (s.Length > 0) list.Add(s);
			}
		}
		if (list.Count == 0) return "";
		if (relative) {
			var baseDir = commonparent(list);
			if (!string.IsNullOrEmpty(baseDir)) {
				var lines = new List<string>();
				var prefix = baseDir.TrimEnd('\\') + "\\";
				foreach (var p in list) {
					if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
						lines.Add(p.Substring(prefix.Length));
					else lines.Add(p);
				}
				return string.Join("\r\n", lines);
			}
		}
		return string.Join("\r\n", list);
	}

	static string commonparent(List<string> paths) {
		var prefix = Path.GetDirectoryName(paths[0]);
		if (string.IsNullOrEmpty(prefix)) return null;
		foreach (var path in paths) {
			while (!string.IsNullOrEmpty(prefix) && !under(prefix, path)) {
				var next = Path.GetDirectoryName(prefix.TrimEnd('\\'));
				if (string.IsNullOrEmpty(next) || samepath(next, prefix)) return null;
				prefix = next;
			}
		}
		return prefix;
	}

	static bool under(string dir, string path) {
		var d = full(dir) + "\\";
		var p = full(path);
		return p.StartsWith(d, StringComparison.OrdinalIgnoreCase) || samepath(dir, path);
	}

	static void loadlang() {
		var opt = loadopt();
		try { Loc.SetFromConfig(opt.UiLang); } catch { }
	}

	static OcrOptions loadopt() {
		var opt = new OcrOptions();
		try { AppConfig.LoadInto(opt); } catch { }
		return opt;
	}

	static void tell(string text) {
		if (string.IsNullOrEmpty(text)) return;
		try { MessageBox.Show(text, Loc.T("fc.title")); }
		catch { }
	}

	static void fclog(string s) {
		try {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(dir);
			File.AppendAllText(Path.Combine(dir, "fc.txt"),
				$"{DateTime.Now:HH:mm:ss} {s}\n", new UTF8Encoding(false));
		}
		catch { }
	}

	#endregion

	// ==================== 自测 ====================
	#region

	public static int SelfTest(Action<string> log) {
		void say(string s) { try { log?.Invoke(s); } catch { } }
		var root = Path.Combine(Path.GetTempPath(), "sk-fc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try {
			var src = Path.Combine(root, "src");
			var dst = Path.Combine(root, "dst");
			Directory.CreateDirectory(Path.Combine(src, "sub"));
			File.WriteAllText(Path.Combine(src, "a.txt"), "hello");
			File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "xyz");
			var job = new Job { Dest = dst, Workers = 4, Conflict = "overwrite" };
			job.Sources.Add(Path.Combine(src, "a.txt"));
			job.Sources.Add(Path.Combine(src, "sub"));
			var r = Run(job);
			if (r.Failed != 0 || r.Ok < 2) return bad(say, $"copy ok={r.Ok} fail={r.Failed} {r.Error}");
			if (File.ReadAllText(Path.Combine(dst, "a.txt")) != "hello") return bad(say, "content");
			if (File.ReadAllText(Path.Combine(dst, "sub", "b.txt")) != "xyz") return bad(say, "sub");
			File.WriteAllText(Path.Combine(dst, "a.txt"), "keep");
			job.Conflict = "skip";
			job.Sources.Clear();
			job.Sources.Add(Path.Combine(src, "a.txt"));
			r = Run(job);
			if (r.Skipped < 1 || File.ReadAllText(Path.Combine(dst, "a.txt")) != "keep")
				return bad(say, "skip");
			job.Conflict = "rename";
			r = Run(job);
			if (!File.Exists(Path.Combine(dst, "a (2).txt"))) return bad(say, "rename");
			var msrc = Path.Combine(root, "m");
			Directory.CreateDirectory(msrc);
			File.WriteAllText(Path.Combine(msrc, "c.txt"), "m");
			var mj = new Job { Dest = Path.Combine(root, "md"), Move = true, Workers = 2 };
			mj.Sources.Add(Path.Combine(msrc, "c.txt"));
			r = Run(mj);
			if (File.Exists(Path.Combine(msrc, "c.txt")) || File.ReadAllText(Path.Combine(root, "md", "c.txt")) != "m")
				return bad(say, "move");
			var tree = Path.Combine(root, "tree");
			Directory.CreateDirectory(Path.Combine(tree, "n"));
			File.WriteAllText(Path.Combine(tree, "n", "d.txt"), "d");
			var dj = new Job { Dest = Path.Combine(root, "tdst"), Move = true };
			dj.Sources.Add(tree);
			r = Run(dj);
			if (Directory.Exists(tree) || File.ReadAllText(Path.Combine(root, "tdst", "tree", "n", "d.txt")) != "d")
				return bad(say, "movedir");
			var kind = "t" + Guid.NewGuid().ToString("N").Substring(0, 8);
			List<string> got = null;
			int nulls = 0;
			var t1 = Task.Run(() => {
				var x = claim(kind, new[] { "A" }, 350);
				if (x == null) Interlocked.Increment(ref nulls);
				else got = x;
			});
			Thread.Sleep(40);
			var t2 = Task.Run(() => {
				var x = claim(kind, new[] { "B" }, 350);
				if (x == null) Interlocked.Increment(ref nulls);
				else got = x;
			});
			Task.WaitAll(t1, t2);
			if (nulls != 1 || got == null || got.Count != 2 || !got.Contains("A") || !got.Contains("B"))
				return bad(say, $"claim nulls={nulls} n={got?.Count}");
			var rel = PathText(new[] { @"C:\a\one.txt", @"C:\a\two.txt" }, true);
			if (rel != "one.txt\r\ntwo.txt") return bad(say, "rel " + rel);
			var cmd = command("--fc-copy");
			if (cmd.IndexOf("--fc-copy", StringComparison.Ordinal) < 0 || cmd.IndexOf("%1", StringComparison.Ordinal) < 0)
				return bad(say, "cmd");
			say("=== OK：快速复制");
			return 0;
		}
		catch (Exception ex) {
			return bad(say, ex.Message);
		}
		finally {
			try { Directory.Delete(root, true); } catch { }
			try { File.Delete(selfile("t")); } catch { }
		}
	}

	static int bad(Action<string> say, string msg) {
		say("FAIL " + msg);
		return 1;
	}

	#endregion
}
