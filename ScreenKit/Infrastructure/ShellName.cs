using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 开始菜单快捷方式的显示名随 Windows 界面语言变化。
/// 字符串 101 由 scripts/stamp-shell-name.py 写进 exe：中文「屏幕截图工具」，英文 ScreenKit。
/// </summary>
static class ShellName {
	public const int STRING_ID = 101;

	public static void Apply() {
		try { apply(false); }
		catch { }
	}

	public static int Test() {
		var en = loadstring(0x0409);
		var zh = loadstring(0x0804);
		Console.WriteLine($"shell-name en={en}");
		Console.WriteLine($"shell-name zh={zh}");
		if (en != "ScreenKit" || zh != "屏幕截图工具") {
			Console.WriteLine("shell-name: 资源字符串不符");
			return 1;
		}
		int n;
		try { n = apply(true); }
		catch (Exception ex) {
			Console.WriteLine("shell-name: " + ex.Message);
			return 1;
		}
		if (n == 0) {
			Console.WriteLine("shell-name: 开始菜单里没有指向本程序的快捷方式");
			return 1;
		}
		var exe = exepath();
		var bad = false;
		foreach (var lnk in shortcuts(exe)) {
			var shown = displayname(lnk);
			Console.WriteLine($"shell-name show={shown} lnk={lnk}");
			if (shown != "屏幕截图工具") bad = true;
		}
		if (bad) {
			Console.WriteLine("shell-name: 当前中文界面下显示名不是「屏幕截图工具」");
			return 1;
		}
		Console.WriteLine("shell-name: ok");
		return 0;
	}

	static int apply(bool log) {
		var exe = exepath();
		var changed = 0;
		foreach (var lnk in shortcuts(exe)) {
			if (already(lnk, exe)) {
				if (log) Console.WriteLine($"shell-name already {lnk}");
				continue;
			}
			var hr = SHSetLocalizedName(lnk, exe, STRING_ID);
			if (hr < 0) throw new InvalidOperationException($"SHSetLocalizedName 0x{hr:X8} {lnk}");
			notify(lnk);
			changed++;
			if (log) Console.WriteLine($"shell-name set {lnk}");
		}
		if (changed > 0) restartstartmenu();
		return shortcuts(exe).Count;
	}

	static List<string> shortcuts(string exe) {
		var list = new List<string>();
		foreach (var root in new[] {
			Environment.GetFolderPath(Environment.SpecialFolder.Programs),
			Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
		}) {
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
			IEnumerable<string> files;
			try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
			catch { continue; }
			foreach (var lnk in files) {
				string target = null;
				try { target = linktarget(lnk); }
				catch { continue; }
				if (samepath(target, exe)) list.Add(lnk);
			}
		}
		return list;
	}

	static string linktarget(string lnk) {
		var t = Type.GetTypeFromProgID("WScript.Shell");
		var shell = Activator.CreateInstance(t);
		var sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
		return sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string;
	}

	static bool already(string lnk, string exe) {
		var sb = new StringBuilder(520);
		int id;
		var hr = SHGetLocalizedName(lnk, sb, sb.Capacity, out id);
		if (hr < 0) return false;
		return id == STRING_ID && samepath(sb.ToString(), exe);
	}

	static void notify(string lnk) {
		SHChangeNotify(0x00002000, 0x0005 | 0x1000, lnk, IntPtr.Zero);
		var dir = Path.GetDirectoryName(lnk);
		if (!string.IsNullOrEmpty(dir))
			SHChangeNotify(0x00001000, 0x0005 | 0x1000, dir, IntPtr.Zero);
	}

	static void restartstartmenu() {
		try {
			foreach (var p in Process.GetProcessesByName("StartMenuExperienceHost")) {
				try { p.Kill(); } catch { }
			}
		}
		catch { }
	}

	static string displayname(string path) {
		var info = new SHFILEINFO();
		var cb = (uint)Marshal.SizeOf(info);
		SHGetFileInfo(path, 0, ref info, cb, 0x00000200);
		return info.szDisplayName ?? "";
	}

	static string loadstring(ushort lang) {
		var h = GetModuleHandle(null);
		var block = (IntPtr)(STRING_ID / 16 + 1);
		var info = FindResourceEx(h, (IntPtr)6, block, lang);
		if (info == IntPtr.Zero) return "";
		var data = LoadResource(h, info);
		var ptr = LockResource(data);
		var size = SizeofResource(h, info);
		if (ptr == IntPtr.Zero || size < 2) return "";
		var bytes = new byte[size];
		Marshal.Copy(ptr, bytes, 0, bytes.Length);
		return slot(bytes, STRING_ID % 16);
	}

	static string slot(byte[] data, int want) {
		var i = 0;
		for (var s = 0; s < 16; s++) {
			if (i + 2 > data.Length) return "";
			var n = data[i] | (data[i + 1] << 8);
			i += 2;
			if (s == want) {
				if (n == 0 || i + n * 2 > data.Length) return "";
				return Encoding.Unicode.GetString(data, i, n * 2);
			}
			i += n * 2;
		}
		return "";
	}

	static string exepath() {
		var p = "";
		try { p = Process.GetCurrentProcess().MainModule?.FileName; }
		catch { }
		if (string.IsNullOrEmpty(p)) p = Assembly.GetExecutingAssembly().Location;
		return Path.GetFullPath(p);
	}

	static bool samepath(string a, string b) {
		if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
		try {
			a = Path.GetFullPath(a.Trim().Trim('"'));
			b = Path.GetFullPath(b.Trim().Trim('"'));
		}
		catch { return false; }
		return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	static extern int SHSetLocalizedName(string pszPath, string pszResModule, int idsRes);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	static extern int SHGetLocalizedName(string pszPath, StringBuilder pszResModule, int cch, out int pidsRes);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	static extern void SHChangeNotify(uint wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string lpModuleName);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr FindResourceEx(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort wLanguage);

	[DllImport("kernel32.dll")]
	static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

	[DllImport("kernel32.dll")]
	static extern IntPtr LockResource(IntPtr hResData);

	[DllImport("kernel32.dll")]
	static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct SHFILEINFO {
		public IntPtr hIcon;
		public int iIcon;
		public uint dwAttributes;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szDisplayName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string szTypeName;
	}
}
