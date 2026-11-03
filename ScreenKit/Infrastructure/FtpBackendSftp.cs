using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace ScreenKit;

/// <summary>
/// SFTP 后端。SSH.NET 同步 API，调用方放后台线程。
/// 认证优先私钥（填了 KeyFile），否则密码。
/// </summary>
sealed class SftpBackend : IFtpBackend {
	SftpClient cli;
	string baseDir = "/";

	public bool Connected => cli != null && cli.IsConnected;

	public void Connect(FtpSite site, string password) {
		Dispose();
		var port = site.PortOr(FtpProto.DefPort(FtpProto.Sftp));
		var methods = new List<AuthenticationMethod>();
		var keyFile = (site.KeyFile ?? "").Trim();
		if (keyFile.Length > 0) {
			var kf = new PrivateKeyFile(keyFile, password);
			methods.Add(new PrivateKeyAuthenticationMethod(site.User, kf));
		}
		// 私钥之外也带密码认证，免得私钥没被服务端接受时直接失败
		if ((password ?? "").Length > 0 || methods.Count == 0)
			methods.Add(new PasswordAuthenticationMethod(site.User, password ?? ""));

		var ci = new ConnectionInfo(site.Host, port, site.User, methods.ToArray()) {
			Timeout = TimeSpan.FromSeconds(15)
		};
		cli = new SftpClient(ci);
		cli.Connect();
		try { baseDir = cli.WorkingDirectory; }
		catch { baseDir = "/"; }
		if (string.IsNullOrEmpty(baseDir)) baseDir = "/";
	}

	public void Disconnect() {
		try { if (cli != null && cli.IsConnected) cli.Disconnect(); }
		catch { }
	}

	public void Dispose() {
		try { Disconnect(); } catch { }
		try { cli?.Dispose(); } catch { }
		cli = null;
	}

	public string WorkDir() => baseDir;

	/// <summary>折叠重复的 / 和末尾的 /（根目录除外）。</summary>
	static string norm(string p) {
		var s = p.Replace("//", "/");
		while (s.Contains("//")) s = s.Replace("//", "/");
		if (s.Length > 1) s = s.TrimEnd('/');
		return s.Length == 0 ? "/" : s;
	}

	public string Full(string path) {
		var p = (path ?? "").Trim().Replace('\\', '/');
		if (p.Length == 0) return baseDir;
		if (p.StartsWith("/", StringComparison.Ordinal)) return norm(p);
		// ~ 开头的相对家目录
		if (p == "~") return baseDir;
		if (p.StartsWith("~/", StringComparison.Ordinal)) p = p.Substring(2);
		var b = baseDir.TrimEnd('/');
		return norm((b.Length == 0 ? "" : b) + "/" + p);
	}

	public List<FtpEntry> List(string path) {
		var want = Full(path);
		var items = cli.ListDirectory(want);
		var list = new List<FtpEntry>();
		foreach (var it in items) {
			var name = it.Name ?? "";
			if (name.Length == 0 || name == "." || name == "..") continue;
			var isdir = it.IsDirectory;
			// 符号链接指向目录时按目录展示，方便点进去
			if (it.IsSymbolicLink) {
				try { isdir = cli.GetAttributes(it.FullName).IsDirectory; }
				catch { }
			}
			list.Add(new FtpEntry {
				Name = name,
				Full = norm(want.TrimEnd('/') + "/" + name),
				IsDir = isdir,
				Size = isdir ? 0 : (long)it.Length,
				Modified = it.LastWriteTimeUtc == default ? DateTime.MinValue : it.LastWriteTime,
				Perms = perms(it.Attributes)
			});
		}
		return list;
	}

	/// <summary>把 SftpFile 的权限位拼成 rwxr-xr-x。</summary>
	static string perms(SftpFileAttributes a) {
		try {
			int mode = 0;
			if (a.OwnerCanRead) mode |= 0x100;
			if (a.OwnerCanWrite) mode |= 0x80;
			if (a.OwnerCanExecute) mode |= 0x40;
			if (a.GroupCanRead) mode |= 0x20;
			if (a.GroupCanWrite) mode |= 0x10;
			if (a.GroupCanExecute) mode |= 0x8;
			if (a.OthersCanRead) mode |= 0x4;
			if (a.OthersCanWrite) mode |= 0x2;
			if (a.OthersCanExecute) mode |= 0x1;
			return FtpFmt.Perms(mode);
		}
		catch { return ""; }
	}

	public void MkDir(string path) => cli.CreateDirectory(Full(path));

	public void RmDir(string path) => cli.DeleteDirectory(Full(path));

	public void RmFile(string path) => cli.DeleteFile(Full(path));

	public void Rename(string src, string dst) => cli.RenameFile(Full(src), Full(dst));

	/// <summary>mode 是八进制数值（755）；这里转成 SftpFileAttributes 的位。</summary>
	public void Chmod(string path, int mode) {
		int m = mode >= 100 ? ((mode / 100) & 7) << 6 | ((mode / 10) % 10 & 7) << 3 | (mode % 10 & 7)
			: mode;
		cli.ChangePermissions(Full(path), (short)m);
	}

	public long Size(string path) {
		try {
			var a = cli.GetAttributes(Full(path));
			return a.IsDirectory ? -1 : (long)a.Size;
		}
		catch { return -1; }
	}

	public bool IsDir(string path) {
		try { return cli.GetAttributes(Full(path)).IsDirectory; }
		catch { return false; }
	}

	public Stream OpenRead(string path, long offset, long length) {
		var s = cli.OpenRead(Full(path));
		try {
			if (offset > 0) s.Seek(offset, SeekOrigin.Begin);
		}
		catch { s.Dispose(); throw; }
		return s;
	}

	/// <summary>
	/// offset 为 0 时覆盖写；否则定位到断点继续写。
	/// 只写句柄在 SSH.NET 里不可定位，续传必须用 ReadWrite 打开。
	/// </summary>
	public Stream OpenWrite(string path, long offset) {
		var full = Full(path);
		if (offset <= 0) return cli.OpenWrite(full);
		var s = cli.Open(full, FileMode.Open, FileAccess.ReadWrite);
		try { s.Position = offset; }
		catch { s.Dispose(); throw; }
		return s;
	}

	/// <summary>SFTP 能随机写，长度与断点不符就从头传（半截文件可能内容不对）。</summary>
	public long AlignResume(string path, long want) {
		if (want <= 0) return 0;
		try {
			var a = cli.GetAttributes(Full(path));
			if (a.IsDirectory) return 0;
			return (long)a.Size == want ? want : 0;
		}
		catch { return 0; }
	}
}
