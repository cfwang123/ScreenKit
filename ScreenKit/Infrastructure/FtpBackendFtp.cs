using System.Security.Authentication;
using System.Text;
using FluentFTP;

namespace ScreenKit;

/// <summary>
/// FTP / FTPS 后端。用 FluentFTP 的同步 API，调用方负责把它放到后台线程。
/// 显式 FTPS 走 AUTH TLS，隐式 FTPS 直接 TLS 握手；证书默认只记录不校验（自签服务器很常见），
/// 靠连接时的指纹提示让用户自己判断。
/// </summary>
sealed class FtpBackend : IFtpBackend {
	FtpClient cli;
	string baseDir = "/";

	public bool Connected => cli != null && cli.IsConnected;

	public void Connect(FtpSite site, string password) {
		Dispose();
		var kind = FtpProto.Norm(site.Kind);
		var port = site.PortOr(FtpProto.DefPort(kind));
		var cfg = new FtpConfig {
			EncryptionMode = kind switch {
				FtpProto.FtpsImplicit => FtpEncryptionMode.Implicit,
				FtpProto.Ftps => FtpEncryptionMode.Explicit,
				_ => FtpEncryptionMode.None
			},
			DataConnectionType = FtpDataConnectionType.AutoPassive,
			// 自签/过期证书很常见，先连上，把证书信息留给界面提示
			ValidateAnyCertificate = true,
			ConnectTimeout = 15000,
			ReadTimeout = 30000,
			DataConnectionConnectTimeout = 15000,
			DataConnectionReadTimeout = 30000
		};
		cli = new FtpClient(site.Host, new System.Net.NetworkCredential(site.User, password), port, cfg, null);
		// 老服务器列表是 GBK 时，站点里配 enc
		var enc = (site.Enc ?? "").Trim();
		if (enc.Length > 0) {
			try { cli.Encoding = Encoding.GetEncoding(enc); }
			catch { cli.Encoding = Encoding.UTF8; }
		}
		else cli.Encoding = Encoding.UTF8;
		cli.Connect();
		try { baseDir = cli.GetWorkingDirectory(); }
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

	/// <summary>把相对路径接到工作目录上；已经绝对的原样返回。</summary>
	public string Full(string path) {
		var p = (path ?? "").Trim().Replace('\\', '/');
		if (p.Length == 0) return baseDir;
		if (p.StartsWith("/", StringComparison.Ordinal)) return norm(p);
		var b = baseDir.TrimEnd('/');
		return norm(b + "/" + p);
	}

	/// <summary>折叠重复的 / 和末尾的 /（根目录除外）。</summary>
	static string norm(string p) {
		var s = p.Replace("//", "/");
		while (s.Contains("//")) s = s.Replace("//", "/");
		if (s.Length > 1) s = s.TrimEnd('/');
		return s.Length == 0 ? "/" : s;
	}

	public List<FtpEntry> List(string path) {
		var want = Full(path);
		var items = cli.GetListing(want);
		var list = new List<FtpEntry>();
		foreach (var it in items) {
			if (it == null) continue;
			var isdir = it.Type == FtpObjectType.Directory;
			var name = it.Name ?? "";
			// 有些服务器把完整路径塞进 Name
			var slash = name.LastIndexOf('/');
			if (slash >= 0) name = name.Substring(slash + 1);
			if (name.Length == 0 || name == "." || name == "..") continue;
			// 链接按目标类型展示，但删/改仍作用于链接本身，这里统一当文件处理
			var e = new FtpEntry {
				Name = name,
				Full = norm(want.TrimEnd('/') + "/" + name),
				IsDir = isdir,
				Size = isdir ? 0 : it.Size,
				Modified = it.Modified == default ? DateTime.MinValue : it.Modified,
				Perms = it.RawPermissions ?? ""
			};
			list.Add(e);
		}
		return list;
	}

	public void MkDir(string path) => cli.CreateDirectory(Full(path));

	public void RmDir(string path) => cli.DeleteDirectory(Full(path));

	public void RmFile(string path) => cli.DeleteFile(Full(path));

	public void Rename(string src, string dst) => cli.Rename(Full(src), Full(dst));

	public void Chmod(string path, int mode) => cli.SetFilePermissions(Full(path), mode);

	public long Size(string path) {
		try { return cli.GetFileSize(Full(path), -1); }
		catch { return -1; }
	}

	public bool IsDir(string path) {
		try { return cli.DirectoryExists(Full(path)); }
		catch { return false; }
	}

	public Stream OpenRead(string path, long offset, long length) =>
		cli.OpenRead(Full(path), FtpDataType.Binary, offset < 0 ? 0 : offset, length > 0 ? length : -1);

	/// <summary>
	/// offset 为 0 时新建，否则用 APPE 追加到已有内容后面。
	/// FTP 没有 REST 写定位，APPE 是唯一能续传上传的方式，前提是远端长度正好等于 offset。
	/// </summary>
	public Stream OpenWrite(string path, long offset) {
		var full = Full(path);
		if (offset <= 0) return cli.OpenWrite(full, FtpDataType.Binary, true);
		return cli.OpenAppend(full, FtpDataType.Binary, true);
	}

	/// <summary>FTP 用 APPE 追加，远端长度与断点不符就从头传。</summary>
	public long AlignResume(string path, long want) {
		if (want <= 0) return 0;
		try {
			var cur = cli.GetFileSize(Full(path), -1);
			return cur == want ? want : 0;
		}
		catch { return 0; }
	}
}
