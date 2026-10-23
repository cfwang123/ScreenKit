using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>
/// 浏览器朗读有两条路。Google 语音走系统的「通信设备」，蓝牙耳机这时往往是免提。
/// 朗读期间把通信设备改成当前的默认播放设备，结束再改回去。
/// Huihui 这类 Windows 语音走 SAPI 自己的输出，那个输出可以停在喇叭上。
/// 把它改成当前的默认播放设备，并留在那里。已经打开的语音对象要等浏览器重新启动才换设备。
/// </summary>
static class TtsRoute {
	const int RoleComm = 2;
	const string SapiOut = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Speech\AudioOutput";
	const string SapiEnum = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\AudioOutput";
	static readonly Guid PolicyId = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
	static readonly object gate = new();
	static bool held;
	static string saved = "";

	static ITtsPolicy policy() {
		var t = Type.GetTypeFromCLSID(PolicyId);
		return (ITtsPolicy)Activator.CreateInstance(t);
	}

	/// <summary>把 Windows 语音输出对准当前的默认播放设备。</summary>
	public static void Align() {
		lock (gate) {
			try {
				using var en = new MMDeviceEnumerator();
				using var console = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
				alignsapi(console.ID ?? "");
			}
			catch { }
		}
	}

	/// <summary>已经指到默认播放设备时不改通信设备。返回这台设备的名字。</summary>
	public static string Hold() {
		lock (gate) {
			var name = "";
			try {
				using var en = new MMDeviceEnumerator();
				MMDevice console = null;
				try { console = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console); }
				catch { }
				if (console == null) return "";
				name = console.FriendlyName ?? "";
				alignsapi(console.ID ?? "");
				if (held) return name;
				var consoleId = console.ID ?? "";
				var commId = "";
				try {
					using var comm = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
					commId = comm.ID ?? "";
				}
				catch { }
				if (consoleId.Length == 0 || commId.Length == 0
					|| string.Equals(commId, consoleId, StringComparison.OrdinalIgnoreCase)) {
					saved = "";
					held = true;
					return name;
				}
				var pol = policy();
				if (pol.SetDefaultEndpoint(consoleId, RoleComm) != 0) return name;
				saved = commId;
				held = true;
			}
			catch { }
			return name;
		}
	}

	public static void Release() {
		lock (gate) {
			if (!held) return;
			held = false;
			var id = saved;
			saved = "";
			if (id.Length == 0) return;
			try {
				policy().SetDefaultEndpoint(id, RoleComm);
			}
			catch { }
		}
	}

	/// <summary>SAPI 令牌 id 里带有播放设备 id。对上就写成当前用户的默认语音输出。</summary>
	static void alignsapi(string consoleId) {
		if (consoleId.Length == 0) return;
		try {
			var kind = Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory");
			if (kind == null) return;
			dynamic listed = Activator.CreateInstance(kind);
			listed.SetId(SapiEnum, false);
			dynamic tokens = listed.EnumerateTokens(null, null);
			int n = tokens.Count;
			var token = "";
			for (var i = 0; i < n; i++) {
				dynamic t = tokens.Item(i);
				string id = t.Id;
				if (id != null && id.IndexOf(consoleId, StringComparison.OrdinalIgnoreCase) >= 0) {
					token = id;
					break;
				}
			}
			if (token.Length == 0) return;
			dynamic user = Activator.CreateInstance(kind);
			user.SetId(SapiOut, false);
			var cur = "";
			try { cur = (string)user.Default; }
			catch { }
			if (string.Equals(cur, token, StringComparison.OrdinalIgnoreCase)) return;
			user.Default = token;
		}
		catch { }
	}
}

[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITtsPolicy {
	void R0();
	void R1();
	void R2();
	void R3();
	void R4();
	void R5();
	void R6();
	void R7();
	void R8();
	void R9();
	[PreserveSig]
	int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
}
