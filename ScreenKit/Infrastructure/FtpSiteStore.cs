using System.Security.Cryptography;
using System.Text;
using ScreenKit.Json;

namespace ScreenKit;

/// <summary>站点与上次连接位置。放 data.json 的 ftpsites / ftplast 段，不写 config.toml。</summary>
static class FtpSiteStore {
	const string SECTION_SITES = "ftpsites";
	const string SECTION_LAST = "ftplast";

	/// <summary>上次连接：站点名 + 左右栏目录，重开窗口时恢复。</summary>
	public sealed class LastState {
		public string Site { get; set; } = "";
		public string RemoteDir { get; set; } = "";
		public string LocalDir { get; set; } = "";
	}

	/// <summary>读不到或内容坏了都当没有；没名字的条目丢掉。</summary>
	public static List<FtpSite> Load() {
		var list = new List<FtpSite>();
		try {
			var got = DataStore.Get<List<FtpSite>>(SECTION_SITES);
			if (got == null) return list;
			foreach (var it in got) {
				if (it == null || string.IsNullOrWhiteSpace(it.Name)) continue;
				it.Kind = FtpProto.Norm(it.Kind);
				list.Add(it);
			}
		}
		catch { }
		return list;
	}

	public static void Save(List<FtpSite> list) {
		try { DataStore.Put(SECTION_SITES, list ?? new List<FtpSite>()); }
		catch { }
	}

	public static LastState LoadLast() {
		try { return DataStore.Get<LastState>(SECTION_LAST) ?? new LastState(); }
		catch { return new LastState(); }
	}

	public static void SaveLast(LastState st) {
		if (st == null) return;
		try { DataStore.Put(SECTION_LAST, st); }
		catch { }
	}

	/// <summary>密码用当前用户的 DPAPI 加密；换机器或换用户解不开时给空密码。</summary>
	public static string Protect(string pw) {
		if (string.IsNullOrEmpty(pw)) return "";
		try {
			var buf = ProtectedData.Protect(Encoding.UTF8.GetBytes(pw), null, DataProtectionScope.CurrentUser);
			return Convert.ToBase64String(buf);
		}
		catch { return ""; }
	}

	public static string Unprotect(string secret) {
		if (string.IsNullOrEmpty(secret)) return "";
		try {
			var buf = ProtectedData.Unprotect(Convert.FromBase64String(secret), null, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(buf);
		}
		catch { return ""; }
	}
}
