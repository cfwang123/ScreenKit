using System.ComponentModel;
using System.Text;

namespace ScreenKit;

/// <summary>一个保存下来的 FTP/SFTP 站点。密码只存 DPAPI 加密后的 base64。</summary>
sealed class FtpSite {
	public string Name { get; set; } = "";
	/// <summary>ftp / ftps / ftpsi / sftp / tftp。</summary>
	public string Kind { get; set; } = FtpProto.Sftp;
	public string Host { get; set; } = "";
	public int Port { get; set; }
	public string User { get; set; } = "";
	public string Secret { get; set; } = "";
	/// <summary>SFTP 私钥文件路径；填了就不用密码（优先私钥）。</summary>
	public string KeyFile { get; set; } = "";
	/// <summary>老 FTP 服务器的列表编码名，空表示 UTF-8。</summary>
	public string Enc { get; set; } = "";
	public string RemoteDir { get; set; } = "";
	public string LocalDir { get; set; } = "";

	/// <summary>端口为空时按协议取默认值（这里不改 Port，由调用方取）。</summary>
	public int PortOr(int fallback) => Port > 0 && Port <= 65535 ? Port : fallback;
}

/// <summary>协议名与默认端口。</summary>
static class FtpProto {
	public const string Ftp = "ftp";
	public const string Ftps = "ftps";
	public const string FtpsImplicit = "ftpsi";
	public const string Sftp = "sftp";
	public const string Tftp = "tftp";

	public static readonly string[] All = { Ftp, Ftps, FtpsImplicit, Sftp, Tftp };

	public static int DefPort(string kind) => kind switch {
		FtpsImplicit => 990,
		Sftp => 22,
		Tftp => 69,
		_ => 21
	};

	/// <summary>去掉空白和非法字符，认不出就回退 FTP。</summary>
	public static string Norm(string kind) {
		var k = (kind ?? "").Trim().ToLowerInvariant();
		if (k == FtpsImplicit) return FtpsImplicit;
		if (k == "ftps-implicit" || k == "ftpsimplicit" || k == "implicit") return FtpsImplicit;
		if (k == "ftps-explicit" || k == "ftpstls" || k == "explicit") return Ftps;
		foreach (var one in All) if (k == one) return one;
		return Ftp;
	}

	public static bool IsSftp(string kind) => Norm(kind) == Sftp;

	/// <summary>TFTP 只能按文件名收发，没有目录浏览，界面据此改行为。</summary>
	public static bool IsTftp(string kind) => Norm(kind) == Tftp;
}

/// <summary>目录里的一项。SizeText / TimeText / PermText 只给界面绑定用。</summary>
sealed class FtpEntry : INotifyPropertyChanged {
	string perms = "";
	long size;
	DateTime modified;
	string name = "";
	string full = "";
	bool isdir;

	public string Name {
		get => name;
		set { if (name == value) return; name = value; notify(nameof(Name)); }
	}
	public string Full {
		get => full;
		set { if (full == value) return; full = value; }
	}
	public bool IsDir {
		get => isdir;
		set { if (isdir == value) return; isdir = value; notify(nameof(IsDir)); notify(nameof(Icon)); }
	}
	public long Size {
		get => size;
		set { if (size == value) return; size = value; notify(nameof(SizeText)); }
	}
	public DateTime Modified {
		get => modified;
		set { if (modified == value) return; modified = value; notify(nameof(TimeText)); }
	}
	/// <summary>Unix 权限，如 rwxr-xr-x；FTP 服务器不给时为空白。</summary>
	public string Perms {
		get => perms;
		set { if (perms == value) return; perms = value; notify(nameof(Perms)); }
	}
	public string SizeText => IsDir ? "" : FtpFmt.Size(Size);
	public string TimeText => Modified == default ? "" : FtpFmt.Time(Modified);
	public string Icon => IsDir ? "\uE8B7" : "\uE8A5";

	public event PropertyChangedEventHandler PropertyChanged;

	void notify(string prop) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

/// <summary>格式化辅助：文件大小、时间。</summary>
static class FtpFmt {
	public static string Size(long n) {
		if (n < 0) return "";
		if (n < 1024) return $"{n} B";
		double v = n;
		string[] units = { "KB", "MB", "GB", "TB" };
		foreach (var u in units) {
			v /= 1024;
			if (v < 1024) return $"{v:0.#} {u}";
		}
		return $"{v:0.#} PB";
	}

	public static string Time(DateTime t) => t == default ? "" : t.ToString("yyyy-MM-dd HH:mm");

	/// <summary>把 Unix 权限位转成 rwxr-xr-x 这种九字符。</summary>
	public static string Perms(int mode) {
		var sb = new StringBuilder(9);
		int[] bits = { 0x100, 0x80, 0x40, 0x20, 0x10, 0x8, 0x4, 0x2, 0x1 };
		char[] marks = { 'r', 'w', 'x', 'r', 'w', 'x', 'r', 'w', 'x' };
		for (int i = 0; i < 9; i++) sb.Append((mode & bits[i]) != 0 ? marks[i] : '-');
		return sb.ToString();
	}

	public static int ParsePerms(string text) {
		if (string.IsNullOrEmpty(text) || text.Length < 9) return 0;
		int mode = 0;
		int[] bits = { 0x100, 0x80, 0x40, 0x20, 0x10, 0x8, 0x4, 0x2, 0x1 };
		for (int i = 0; i < 9 && i < text.Length; i++)
			if (text[i] != '-') mode |= bits[i];
		return mode;
	}

	/// <summary>把 rwxr-xr-x 转成 755；认不出返回 -1。</summary>
	public static int PermsToOctal(string text) {
		if (string.IsNullOrEmpty(text) || text.Length < 9) return -1;
		int mode = ParsePerms(text);
		if (mode == 0) return -1;
		return ((mode >> 6) & 7) * 100 + ((mode >> 3) & 7) * 10 + (mode & 7);
	}
}
