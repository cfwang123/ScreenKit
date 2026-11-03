using System.Reflection;

namespace ScreenKit;

/// <summary>
/// FTP/SFTP 工具依赖的第三方库。精简包不带，第一次用「FTP 文件管理」时提示安装
/// （安装功能 → 工具集 → FTP 工具）。
/// 装在 exe 旁的 ftp\ 子目录：System.Formats.Asn1 与数据库 Oracle 驱动共用文件名但版本不同，
/// 放独立目录再按字节加载就不会互相覆盖。
/// </summary>
static class FtpDeps {
	public const string Folder = "ftp";
	public const string FluentFtpDll = "FluentFTP.dll";
	public const string SshNetDll = "Renci.SshNet.dll";
	public const string BouncyDll = "BouncyCastle.Cryptography.dll";
	public const string BclCryptoDll = "Microsoft.Bcl.Cryptography.dll";
	public const string Asn1Dll = "System.Formats.Asn1.dll";

	public static readonly string[] Files = { FluentFtpDll, SshNetDll, BouncyDll, BclCryptoDll, Asn1Dll };

	/// <summary>安装包与包内路径。</summary>
	public static readonly (string Pkg, string Ver, string ZipPath, string File)[] Packed = {
		("fluentftp", "49.0.2", "lib/net472/" + FluentFtpDll, FluentFtpDll),
		("ssh.net", "2026.0.0", "lib/net462/" + SshNetDll, SshNetDll),
		("bouncycastle.cryptography", "2.7.0", "lib/net461/" + BouncyDll, BouncyDll),
		("microsoft.bcl.cryptography", "10.0.10", "lib/net462/" + BclCryptoDll, BclCryptoDll),
		("system.formats.asn1", "10.0.10", "lib/net462/" + Asn1Dll, Asn1Dll),
	};

	/// <summary>约 5.8MB（BouncyCastle 占其中 4.7MB）。</summary>
	public const long SizeHint = 6L * 1024 * 1024;

	public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
	/// <summary>正式安装位置。精简包只认这里。</summary>
	public static string Dir => Path.Combine(BaseDir, Folder);

	/// <summary>
	/// 实际取文件的位置：ftp\ 优先，其次 exe 根目录。
	/// 开发时 NuGet 直接拷到根，发布包走 ftp\；根目录还可能有 Oracle 装的旧版 Asn1，
	/// 所以 ftp\ 存在就用它，避免拿错版本。
	/// </summary>
	public static string PathOf(string file) {
		var sub = Path.Combine(Dir, file);
		if (File.Exists(sub)) return sub;
		var root = Path.Combine(BaseDir, file);
		return File.Exists(root) ? root : sub;
	}

	public static string[] Missing() {
		var list = new List<string>();
		foreach (var f in Files) {
			var p = PathOf(f);
			if (!File.Exists(p) || new FileInfo(p).Length < 1000) list.Add(f);
		}
		return list.ToArray();
	}

	public static bool Installed => Missing().Length == 0;

	/// <summary>缺库就问一句，同意就打开安装窗。返回装好后是否可用。</summary>
	public static bool Ensure(Window owner) {
		if (Installed) return true;
		var ask = Loc.T("ftp.need.detail", string.Join("、", Missing()));
		var title = Loc.T("ftp.need.title");
		var r = owner != null
			? MessageBox.Show(owner, ask, title, MessageBoxButton.YesNo, MessageBoxImage.Information)
			: MessageBox.Show(ask, title, MessageBoxButton.YesNo, MessageBoxImage.Information);
		if (r != MessageBoxResult.Yes) return false;
		FeaturePrompt.OpenInstall(owner, FeatureKind.FtpTools);
		Reset();
		return Installed;
	}

	// ==================== 安装 / 卸载 ====================
	#region
	public static async Task Install(IProgress<string> log, IProgress<InstallProgress> progress,
		CancellationToken ct) {
		Directory.CreateDirectory(Dir);
		for (int i = 0; i < Packed.Length; i++) {
			var (pkg, ver, zip, file) = Packed[i];
			var dest = Path.Combine(Dir, file);
			await FeatureInstaller.InstallNuGetFiles(pkg, ver,
				new[] { (zip, dest) }, log, progress, ct, SizeHint / Packed.Length).ConfigureAwait(false);
		}
		Reset();
	}

	/// <summary>删掉整个 ftp\ 目录。正在传的文件删不掉时返回 false。</summary>
	public static bool Uninstall(IProgress<string> log) {
		Reset();
		try {
			if (!Directory.Exists(Dir)) return true;
			Directory.Delete(Dir, true);
			log?.Report(Loc.T("ftp.deps.deleted"));
			return true;
		}
		catch (Exception ex) {
			log?.Report(Loc.T("ftp.deps.deletefail", ex.Message));
			return false;
		}
	}
	#endregion

	// ==================== 按字节加载 ====================
	#region
	static readonly object gate = new();
	static readonly Dictionary<string, Assembly> loaded = new(StringComparer.OrdinalIgnoreCase);
	static bool hooked;

	/// <summary>
	/// 从 ftp\ 按字节加载程序集。SSH.NET 运行时还会找 BouncyCastle、Asn1 等，
	/// 靠 AssemblyResolve 在 ftp\ 里兜住。用 Load(byte[]) 文件不被锁，卸载能删掉。
	/// </summary>
	public static Assembly Load(string file) {
		lock (gate) {
			if (loaded.TryGetValue(file, out var hit)) return hit;
			hook();
			var path = PathOf(file);
			if (!File.Exists(path))
				throw new MissingFeatureException(Loc.T("ftp.need.detail", file));
			Assembly asm;
			try { asm = Assembly.Load(File.ReadAllBytes(path)); }
			catch (Exception ex) {
				throw new MissingFeatureException(Loc.T("ftp.need.loadfail", file, ex.Message));
			}
			loaded[file] = asm;
			return asm;
		}
	}

	/// <summary>只挂一次：AssemblyResolve 时优先去 ftp\ 找同名 dll。</summary>
	static void hook() {
		if (hooked) return;
		hooked = true;
		AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
			try {
				var name = new AssemblyName(e.Name).Name;
				if (string.IsNullOrEmpty(name)) return null;
				// 只接管我们这五个，别抢主程序自己的同名程序集
				var fileName = name + ".dll";
				if (Array.IndexOf(Files, fileName) < 0) return null;
				lock (gate) {
					if (loaded.TryGetValue(fileName, out var hit)) return hit;
				}
				var p = PathOf(fileName);
				if (!File.Exists(p)) return null;
				var asm = Assembly.Load(File.ReadAllBytes(p));
				lock (gate) loaded[fileName] = asm;
				return asm;
			}
			catch { return null; }
		};
	}

	/// <summary>丢掉缓存，装/卸后调用。已加载的程序集退不回来，新开的传输会用新的。</summary>
	public static void Reset() {
		lock (gate) loaded.Clear();
	}

	/// <summary>
	/// 把五个库全部按字节加载进来。后端代码直接引用 FluentFTP / SSH.NET 的类型，
	/// 一旦 JIT 到那些方法就要能解析到程序集，所以在造后端之前先调这个。
	/// 缺文件抛 MissingFeatureException，由界面提示去装。
	/// </summary>
	public static void LoadAll() {
		foreach (var f in Files) Load(f);
	}
	#endregion
}
