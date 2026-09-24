using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
	public Action<string> Log;
	public CastRecvSrv Recv;
	public bool Connected { get; private set; }
	string status = "USB: 未连接";
	public string Status => status;
	int lastaoa;

	bool aoaReq;

	public void Start() => Start(true);

	public void StartAccessoryOnly() => Start(false);

	void Start(bool requestAoa) {
		if (th != null) return;
		aoaReq = requestAoa;
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
		try {
			if (tryopen(GOOGLE_VID, AOA_PID) || tryopen(GOOGLE_VID, AOA_ADB_PID)) return;
			if (!aoaReq) return;
			var now = Environment.TickCount;
			if (lastaoa == 0 || now - lastaoa > 3000) {
				lastaoa = now;
				tryaoa();
			}
		}
		catch (Exception ex) {
			status = $"USB: {ex.Message}";
		}
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

	static readonly int[] PhoneVids = {
		0x18D1, 0x2717, 0x04E8, 0x22D9, 0x2A70, 0x0E8D, 0x12D1, 0x2A45,
		0x19D2, 0x0B05, 0x0489, 0x05C6, 0x0FCE, 0x1004, 0x04C5, 0x2A47,
	};

	void tryaoa() => RequestAoaOnce(Log);

	public static void RequestAoaOnce(Action<string> log) {
		var any = false;
		foreach (var vid in PhoneVids) {
			UsbDevice dev = null;
			try {
				dev = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(vid));
				if (dev == null) continue;
				any = true;
				if (!getproto(dev, out var proto) || proto < 1) {
					log?.Invoke($"USB VID {vid:X4} 不支持 AOA");
					continue;
				}
				log?.Invoke($"USB AOA 协议 v{proto} VID {vid:X4}");
				sendstr(dev, 0, MFR);
				sendstr(dev, 1, MODEL);
				sendstr(dev, 2, "ScreenKit USB");
				sendstr(dev, 3, "1.0");
				sendstr(dev, 4, "https://screencast.local");
				sendstr(dev, 5, "0001");
				startaoa(dev);
				log?.Invoke("已请求手机进入 USB 配件（无需网络/USB 调试）");
				return;
			}
			catch (Exception ex) { log?.Invoke($"AOA {vid:X4}: {ex.Message}"); }
			finally { try { dev?.Close(); } catch { } }
		}
		if (!any)
			log?.Invoke("未找到可切换配件的手机 USB 设备（WinUSB/libusb 打不开当前模式）");
	}

	public static void SpawnAoaHelper(Action<string> log) {
		try {
			using (var mx = new Mutex(false, "Local\\ScreenKit_CastAoa")) {
				if (!mx.WaitOne(0, false)) return;
				try { mx.ReleaseMutex(); } catch { }
			}
			var exe = Environment.GetCommandLineArgs().FirstOrDefault()
				?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
			if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) {
				exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenKit.exe");
			}
			if (!File.Exists(exe)) {
				log?.Invoke("AOA 助手：找不到 ScreenKit.exe");
				return;
			}
			var psi = new System.Diagnostics.ProcessStartInfo {
				FileName = exe,
				Arguments = "--cast-aoa",
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
				WorkingDirectory = Path.GetDirectoryName(exe) ?? AppDomain.CurrentDomain.BaseDirectory,
			};
			System.Diagnostics.Process.Start(psi);
		}
		catch (Exception ex) { log?.Invoke($"AOA 助手: {ex.Message}"); }
	}

	public static void RunAoaBridge(Action<string> log) {
		bool created;
		using var mx = new Mutex(true, "Local\\ScreenKit_CastAoa", out created);
		if (!created) {
			log?.Invoke("AOA 助手已在运行");
			return;
		}
		try {
			log?.Invoke("AOA 助手：请求配件并桥接到本机 19519");
			var t0 = Environment.TickCount;
			var lastreq = 0;
			while (Environment.TickCount - t0 < 90000) {
				try {
					if (trybridge(log)) return;
				}
				catch (Exception ex) { log?.Invoke($"AOA 桥接: {ex.Message}"); }
				var now = Environment.TickCount;
				if (lastreq == 0 || now - lastreq > 3000) {
					lastreq = now;
					try { RequestAoaOnce(log); }
					catch (Exception ex) { log?.Invoke($"AOA 请求: {ex.Message}"); }
				}
				Thread.Sleep(800);
			}
			log?.Invoke("AOA 助手超时：手机未进入配件。可改用通知栏 USB 网络共享，或 USB 投屏(adb)");
		}
		finally {
			try { mx.ReleaseMutex(); } catch { }
			try { UsbDevice.Exit(); } catch { }
		}
	}

	static bool trybridge(Action<string> log) {
		UsbDevice dev = null;
		try {
			dev = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(GOOGLE_VID, AOA_PID))
				?? UsbDevice.OpenUsbDevice(new UsbDeviceFinder(GOOGLE_VID, AOA_ADB_PID));
			if (dev == null) return false;
			var whole = dev as IUsbDevice;
			whole?.SetConfiguration(1);
			whole?.ClaimInterface(0);
			if (!openeps(dev, out var rd, out var wr)) {
				dev.Close();
				return false;
			}
			log?.Invoke("USB AOA 已打开，连接本机接收");
			using var usb = new CastUsbBulkStream(rd, wr);
			using var cli = new TcpClient();
			cli.NoDelay = true;
			cli.Connect("127.0.0.1", CastProto.TCP_PORT);
			using var ns = cli.GetStream();
			log?.Invoke("USB AOA 已桥接到 127.0.0.1:19519");
			pump(usb, ns);
			log?.Invoke("USB AOA 会话结束");
			return true;
		}
		finally {
			try { dev?.Close(); } catch { }
		}
	}

	static void pump(Stream a, Stream b) {
		using var cts = new CancellationTokenSource();
		var t1 = new Thread(() => copy(a, b, cts)) { IsBackground = true, Name = "aoa-u2t" };
		var t2 = new Thread(() => copy(b, a, cts)) { IsBackground = true, Name = "aoa-t2u" };
		t1.Start();
		t2.Start();
		t1.Join();
		try { cts.Cancel(); } catch { }
		t2.Join(800);
	}

	static void copy(Stream src, Stream dst, CancellationTokenSource cts) {
		var buf = new byte[16384];
		try {
			while (!cts.IsCancellationRequested) {
				var n = src.Read(buf, 0, buf.Length);
				if (n <= 0) break;
				dst.Write(buf, 0, n);
				dst.Flush();
			}
		}
		catch { }
		try { cts.Cancel(); } catch { }
		try { dst.Flush(); } catch { }
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
		th = null;
	}
}
