using System.Text;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace ScreenKit;

sealed class CastUsbHost : IDisposable {
	const int GOOGLE_VID = 0x18D1;
	const int AOA_PID = 0x2D00;
	const int AOA_ADB_PID = 0x2D01;
	const byte GET_PROTOCOL = 51;
	const byte SEND_STRING = 52;
	const byte START = 53;
	const string MFR = "screencast";
	const string MODEL = "screencast";

	Thread th;
	volatile bool stop;
	int lastaoa;
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
		if (Environment.TickCount - lastaoa < 4000 && lastaoa != 0) return;
		lastaoa = Environment.TickCount;
		tryaoa();
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

	void tryaoa() {
		UsbRegDeviceList all;
		try { all = UsbDevice.AllDevices; }
		catch { return; }
		if (all == null) return;
		foreach (UsbRegistry reg in all) {
			if (stop) return;
			UsbDevice dev = null;
			try {
				if (!reg.Open(out dev) || dev == null) continue;
				if (!getproto(dev, out var proto) || proto < 1) {
					dev.Close();
					continue;
				}
				Log?.Invoke($"USB AOA 协议 v{proto} {reg.Name}");
				sendstr(dev, 0, MFR);
				sendstr(dev, 1, MODEL);
				sendstr(dev, 2, "ScreenKit USB");
				sendstr(dev, 3, "1.0");
				sendstr(dev, 4, "https://screencast.local");
				sendstr(dev, 5, "0001");
				startaoa(dev);
				status = "USB: 已请求配件，等待手机授权";
				Log?.Invoke("已请求手机进入 USB 配件（无需 USB 调试）");
			}
			catch (Exception ex) { Log?.Invoke($"AOA 切换: {ex.Message}"); }
			finally { try { dev?.Close(); } catch { } }
		}
	}

	static bool getproto(UsbDevice dev, out short proto) {
		proto = 0;
		var buf = new byte[2];
		var pkt = new UsbSetupPacket(
			(byte)(UsbCtrlFlags.Direction_In | UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Device),
			GET_PROTOCOL, 0, 0, 2);
		if (!dev.ControlTransfer(ref pkt, buf, 2, out var n) || n < 2) return false;
		proto = (short)(buf[0] | (buf[1] << 8));
		return proto >= 1;
	}

	static void sendstr(UsbDevice dev, short index, string s) {
		var data = Encoding.UTF8.GetBytes(s + "\0");
		var pkt = new UsbSetupPacket(
			(byte)(UsbCtrlFlags.Direction_Out | UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Device),
			SEND_STRING, 0, index, (short)data.Length);
		dev.ControlTransfer(ref pkt, data, data.Length, out _);
	}

	static void startaoa(UsbDevice dev) {
		var pkt = new UsbSetupPacket(
			(byte)(UsbCtrlFlags.Direction_Out | UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Device),
			START, 0, 0, 0);
		dev.ControlTransfer(ref pkt, null, 0, out _);
	}

	public void Dispose() {
		stop = true;
		try { UsbDevice.Exit(); } catch { }
		th = null;
	}
}
