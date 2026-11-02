using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>录音机窗口的实时电平：麦克风和系统环回各开一路共享采集，只算峰值，不写盘。</summary>
sealed class VoiceMeter : IDisposable {
	volatile int micLv;
	volatile int sysLv;
	bool micOpen;
	bool sysOpen;
	WasapiCapture cap;
	WasapiLoopbackCapture loop;
	WaveFormat capFmt;
	WaveFormat loopFmt;
	MMDeviceEnumerator en;
	MMDevice dev;
	string micId = "";
	bool disposed;

	public int MicLevel => micLv;
	public int SysLevel => sysLv;
	public bool MicOpen => micOpen;
	public bool SysOpen => sysOpen;
	public string SysName { get; private set; } = "";

	public void StartSys() {
		if (sysOpen || disposed) return;
		sysname();
		WasapiLoopbackCapture lp = null;
		try {
			lp = new WasapiLoopbackCapture();
			loopFmt = lp.WaveFormat;
			lp.DataAvailable += onsys;
			lp.StartRecording();
		}
		catch {
			try { lp?.Dispose(); } catch { }
			loopFmt = null;
			sysLv = 0;
			return;
		}
		loop = lp;
		sysOpen = true;
	}

	public void StopSys() {
		var lp = loop;
		loop = null;
		if (lp != null) {
			try { lp.DataAvailable -= onsys; } catch { }
			try { lp.StopRecording(); } catch { }
			try { lp.Dispose(); } catch { }
		}
		loopFmt = null;
		sysOpen = false;
		sysLv = 0;
	}

	/// <summary>默认播放设备换了就重开系统环回，电平块跟着新设备走。</summary>
	public void RefreshSys() {
		if (disposed) return;
		string name = VoiceRec.DefaultRenderName();
		if (sysOpen && name == SysName) return;
		StopSys();
		StartSys();
	}

	public void StartMic(string deviceId) {
		if (string.IsNullOrWhiteSpace(deviceId)) {
			StopMic();
			return;
		}
		if (micOpen && micId == deviceId) return;
		StopMic();
		if (disposed) return;
		WasapiCapture c = null;
		MMDeviceEnumerator e = null;
		MMDevice d = null;
		try {
			e = new MMDeviceEnumerator();
			d = e.GetDevice(deviceId);
			c = new WasapiCapture(d);
			capFmt = c.WaveFormat;
			c.DataAvailable += onmic;
			c.StartRecording();
		}
		catch {
			try { c?.Dispose(); } catch { }
			try { d?.Dispose(); } catch { }
			try { e?.Dispose(); } catch { }
			capFmt = null;
			return;
		}
		cap = c;
		en = e;
		dev = d;
		micId = deviceId;
		micOpen = true;
	}

	public void StopMic() {
		var c = cap;
		cap = null;
		if (c != null) {
			try { c.DataAvailable -= onmic; } catch { }
			try { c.StopRecording(); } catch { }
			try { c.Dispose(); } catch { }
		}
		try { dev?.Dispose(); } catch { }
		dev = null;
		try { en?.Dispose(); } catch { }
		en = null;
		capFmt = null;
		micId = "";
		micOpen = false;
		micLv = 0;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		StopMic();
		StopSys();
	}

	void onsys(object sender, WaveInEventArgs e) {
		var f = loopFmt;
		if (f == null || e.BytesRecorded <= 0) return;
		sysLv = VoiceRec.LevelOf(peak(e.Buffer, e.BytesRecorded, f));
	}

	void onmic(object sender, WaveInEventArgs e) {
		var f = capFmt;
		if (f == null || e.BytesRecorded <= 0) return;
		micLv = VoiceRec.LevelOf(peak(e.Buffer, e.BytesRecorded, f));
	}

	static float peak(byte[] buf, int bytes, WaveFormat fmt) {
		var mono = VoiceRec.ToMono(buf, bytes, fmt);
		float p = 0;
		for (int i = 0; i < mono.Length; i++) {
			float a = mono[i] < 0 ? -mono[i] : mono[i];
			if (a > p) p = a;
		}
		return p;
	}

	void sysname() {
		try { SysName = VoiceRec.DefaultRenderName(); } catch { }
	}
}
