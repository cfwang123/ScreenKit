using System.Text;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace ScreenKit;

sealed class CastUsbHost : IDisposable {
	const int GOOGLE_VID = 0x18D1;
	const int AOA_PID = 0x2D00;
	const int AOA_ADB_PID = 0x2D01;

	Thread th;
	volatile bool stop;
	public Action<string> Log;
	public CastRecvSrv Recv;
	public bool Connected { get; private set; }
	string status = "USB: 未连接";
	public string Status => status;

	public void Start() {
		if (th != null) return;
		stop = false;
		th = new Thread(loop) { IsBackground = true, Name = "cast-usb" };
		th.Start();
	}

	void loop() {
		while (!stop) {
			try { tick(); }
			catch (Exception ex) { status = $"USB: {ex.Message}"; Log?.Invoke(status); }
			if (!stop) Thread.Sleep(1500);
		}
	}

	void tick() {
		if (tryopen(GOOGLE_VID, AOA_PID) || tryopen(GOOGLE_VID, AOA_ADB_PID)) return;
	}

	bool tryopen(int vid, int pid) {
		UsbDevice dev = null;
		try {
			dev = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(vid, pid));
			if (dev == null) return false;
			var whole = dev as IUsbDevice;
			whole?.SetConfiguration(1);
			whole?.ClaimInterface(0);
			if (!openeps(dev, out var rd, out var wr)) {
				dev.Close();
				return false;
			}
			status = $"USB AOA {vid:X4}:{pid:X4}";
			Log?.Invoke(status);
			Connected = true;
			using (var s = new CastUsbBulkStream(rd, wr))
				Recv?.AttachStream(s, "USB");
			return true;
		}
		catch (Exception ex) {
			Log?.Invoke($"USB 打开失败: {ex.Message}");
			return false;
		}
		finally {
			Connected = false;
			try { dev?.Close(); } catch { }
			status = "USB: 未连接";
		}
	}

	static bool openeps(UsbDevice dev, out UsbEndpointReader rd, out UsbEndpointWriter wr) {
		rd = dev.OpenEndpointReader(ReadEndpointID.Ep01, 16384);
		wr = dev.OpenEndpointWriter(WriteEndpointID.Ep01);
		return rd != null && wr != null;
	}

	public void Dispose() {
		stop = true;
		try { UsbDevice.Exit(); } catch { }
		th = null;
	}
}
