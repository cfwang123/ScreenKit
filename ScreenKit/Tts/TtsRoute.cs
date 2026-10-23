using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>
/// 浏览器朗读走系统的「通信设备」。蓝牙耳机这时往往是免提，不是默认播放用的立体声。
/// 朗读期间把通信设备改成当前的默认播放设备，结束再改回去。
/// </summary>
static class TtsRoute {
	const int RoleComm = 2;
	static readonly Guid PolicyId = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
	static readonly object gate = new();
	static bool held;
	static string saved = "";

	static ITtsPolicy policy() {
		var t = Type.GetTypeFromCLSID(PolicyId);
		return (ITtsPolicy)Activator.CreateInstance(t);
	}

	/// <summary>已经指到默认播放设备时不改。返回这台设备的名字。</summary>
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
