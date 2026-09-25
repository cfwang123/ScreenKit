using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LibUsbDotNet;
using LibUsbDotNet.Info;
using LibUsbDotNet.Main;
using LibUsbDotNet.WinUsb;
using Microsoft.Win32;

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
	const string AOA_GUID = "{dee824ef-729b-4a0e-9c14-b7117d33a817}";
	static readonly string WinUsbInf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"..\INF\winusb.inf");

	Thread th;
	volatile bool stop;
	public Action<string> Log;
	public CastRecvSrv Recv;
	public bool Connected { get; private set; }
	string status = "USB: 未连接";
	public string Status => status;
	int lastaoa;

	bool aoaReq;
	const string AOA_BRIDGE_MX = "Local\\ScreenKit_CastAoaBridge";

	public static bool AoaBridgeActive() {
		try {
			using var mx = Mutex.OpenExisting(AOA_BRIDGE_MX);
			if (!mx.WaitOne(0)) return true;
			mx.ReleaseMutex();
		}
		catch (WaitHandleCannotBeOpenedException) { }
		catch { }
		return false;
	}

	public static bool AccessoryPresent() {
		try {
			foreach (var id in usbpresent()) {
				var u = id.ToUpperInvariant();
				if (u.Contains("VID_18D1") && (u.Contains("PID_2D00") || u.Contains("PID_2D01")))
					return true;
			}
		}
		catch { }
		return false;
	}

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
			if (tryopenreg()) return;
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

	bool tryopenreg() {
		foreach (var r in aoaregs()) {
			if (tryopen(r)) return true;
		}
		return false;
	}

	bool tryopen(UsbRegistry reg) {
		UsbDevice dev = null;
		try {
			if (!reg.Open(out dev) || dev == null) return false;
			var whole = dev as IUsbDevice;
			whole?.SetConfiguration(1);
			whole?.ClaimInterface(0);
			if (!openeps(dev, out var rd, out var wr)) {
				dev.Close();
				return false;
			}
			status = $"USB AOA {reg.Vid:X4}:{reg.Pid:X4}";
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
		rd = null;
		wr = null;
		byte? inId = null;
		byte? outId = null;
		try {
			var cfgs = dev.Configs;
			if (cfgs != null) {
				foreach (UsbConfigInfo cfg in cfgs) {
					foreach (UsbInterfaceInfo iface in cfg.InterfaceInfoList) {
						byte? a = null;
						byte? b = null;
						foreach (UsbEndpointInfo ep in iface.EndpointInfoList) {
							var d = ep.Descriptor;
							if ((d.Attributes & 3) != 2) continue;
							if ((d.EndpointID & 0x80) != 0) a = d.EndpointID;
							else b = d.EndpointID;
						}
						if (a != null && b != null) {
							if (iface.Descriptor.InterfaceID == 0) {
								inId = a;
								outId = b;
								break;
							}
							if (inId == null) {
								inId = a;
								outId = b;
							}
						}
					}
					if (inId != null) break;
				}
			}
		}
		catch { }
		if (inId != null && outId != null) {
			try {
				rd = dev.OpenEndpointReader((ReadEndpointID)inId.Value, 16384);
				wr = dev.OpenEndpointWriter((WriteEndpointID)outId.Value);
				if (rd != null && wr != null) return true;
			}
			catch { }
			rd = null;
			wr = null;
		}
		ReadEndpointID[] rins = { ReadEndpointID.Ep01, ReadEndpointID.Ep02, ReadEndpointID.Ep03 };
		WriteEndpointID[] wins = { WriteEndpointID.Ep01, WriteEndpointID.Ep02, WriteEndpointID.Ep03 };
		foreach (var r in rins) {
			foreach (var w in wins) {
				try {
					var reader = dev.OpenEndpointReader(r, 16384);
					var writer = dev.OpenEndpointWriter(w);
					if (reader != null && writer != null) {
						rd = reader;
						wr = writer;
						return true;
					}
				}
				catch { }
			}
		}
		return false;
	}

	void tryaoa() => RequestAoaOnce(Log);

	public static void Dump(Action<string> log) {
		try {
			log?.Invoke($"HasWinUsb={UsbDevice.HasWinUsbDriver} HasLibUsb={UsbDevice.HasLibUsbDriver}");
		}
		catch (Exception ex) { log?.Invoke($"Has*: {ex.Message}"); }
		dumpList(log, "AllDevices", UsbDevice.AllDevices);
		dumpList(log, "AllWinUsb", UsbDevice.AllWinUsbDevices);
		dumpList(log, "AllLibUsb", UsbDevice.AllLibUsbDevices);
		foreach (var g in IfGuids) {
			try {
				if (!WinUsbRegistry.GetWinUsbRegistryList(g, out var wr) || wr == null) continue;
				log?.Invoke($"WinUsbRegistry {g:B} {wr.Count}");
				foreach (var r in wr) {
					UsbDevice d = null;
					try {
						var ok = r.Open(out d);
						short proto = 0;
						var aoa = ok && d != null && getproto(d, out proto) && proto >= 1;
						log?.Invoke($"  wr {r.Vid:X4}:{r.Pid:X4} open={ok} aoa={(aoa ? $"v{proto}" : "no")} {r.Name} {shortpath(r.SymbolicName)}");
					}
					catch (Exception ex) { log?.Invoke($"  wr {r.Vid:X4}:{r.Pid:X4} {ex.Message}"); }
					finally { try { d?.Close(); } catch { } }
				}
			}
			catch (Exception ex) { log?.Invoke($"WinUsbRegistry {g:B}: {ex.Message}"); }
		}
		foreach (var iid in usbpresent()) {
			if (iid.IndexOf("VID_18D1", StringComparison.OrdinalIgnoreCase) < 0
				&& iid.IndexOf("VID_2717", StringComparison.OrdinalIgnoreCase) < 0)
				continue;
			log?.Invoke($"  pnp {iid} svc={devsvc(iid)}");
		}
		foreach (var path in winusbpaths()) {
			log?.Invoke($"  iface {shortpath(path)}");
			tryopenpath(path, log, start: false);
		}
	}

	static void dumpList(Action<string> log, string title, UsbRegDeviceList all) {
		if (all == null) { log?.Invoke($"{title} null"); return; }
		log?.Invoke($"{title} {all.Count}");
		foreach (UsbRegistry reg in all) {
			UsbDevice dev = null;
			try {
				var ok = reg.Open(out dev);
				short proto = 0;
				var aoa = ok && dev != null && getproto(dev, out proto) && proto >= 1;
				log?.Invoke($"  {reg.Vid:X4}:{reg.Pid:X4} open={ok} aoa={(aoa ? $"v{proto}" : "no")} {reg.Name}");
			}
			catch (Exception ex) {
				log?.Invoke($"  {reg.Vid:X4}:{reg.Pid:X4} {reg.Name} {ex.Message}");
			}
			finally { try { dev?.Close(); } catch { } }
		}
	}

	static string shortpath(string p) {
		if (string.IsNullOrEmpty(p) || p.Length < 80) return p;
		return p.Substring(0, 76) + "...";
	}

	public static bool RequestAoaOnce(Action<string> log) {
		var switched = false;
		foreach (var g in IfGuids) {
			List<WinUsbRegistry> wr = null;
			try { WinUsbRegistry.GetWinUsbRegistryList(g, out wr); }
			catch { }
			if (wr == null) continue;
			foreach (var r in wr) {
				if (isaeapid(r.Vid, r.Pid)) continue;
				UsbDevice dev = null;
				try {
					if (!r.Open(out dev) || dev == null) continue;
					if (switchaoa(dev, log, $"{r.Vid:X4}:{r.Pid:X4} {r.Name}")) {
						switched = true;
						break;
					}
				}
				catch (Exception ex) { log?.Invoke($"AOA {r.Vid:X4}:{r.Pid:X4}: {ex.Message}"); }
				finally { try { dev?.Close(); } catch { } }
			}
			if (switched) break;
		}
		if (!switched) {
			foreach (var path in winusbpaths()) {
				if (tryopenpath(path, log, start: true)) {
					switched = true;
					break;
				}
			}
		}
		if (!switched)
			log?.Invoke("未找到可切换配件的 USB 口（需 WinUSB，通常是 ADB Interface；MTP 无法发 AOA）");
		return switched;
	}

	static bool isaeapid(int vid, int pid) =>
		vid == GOOGLE_VID && (pid == AOA_PID || pid == AOA_ADB_PID);

	static bool switchaoa(UsbDevice dev, Action<string> log, string tag) {
		if (!getproto(dev, out var proto) || proto < 1) return false;
		log?.Invoke($"USB AOA 协议 v{proto} {tag}");
		sendstr(dev, 0, MFR);
		sendstr(dev, 1, MODEL);
		sendstr(dev, 2, "ScreenKit USB");
		sendstr(dev, 3, "1.0");
		sendstr(dev, 4, "https://screencast.local");
		sendstr(dev, 5, "0001");
		startaoa(dev);
		log?.Invoke("已请求手机进入 USB 配件");
		return true;
	}

	static int lastbind;

	static void bindaoa(Action<string> log) {
		var need = false;
		foreach (var iid in usbpresent()) {
			if (!isaeaiid(iid)) continue;
			if (!string.Equals(devsvc(iid), "WINUSB", StringComparison.OrdinalIgnoreCase)) {
				need = true;
				break;
			}
		}
		if (!need) return;
		var now = Environment.TickCount;
		if (lastbind != 0 && now - lastbind < 8000) return;
		lastbind = now;
		foreach (var iid in usbpresent()) {
			if (!isaeaiid(iid)) continue;
			setguid(iid, log);
			var svc = devsvc(iid);
			if (string.Equals(svc, "WINUSB", StringComparison.OrdinalIgnoreCase)) continue;
			log?.Invoke($"配件口 {svc} → WinUSB(ADB 节) {iid}");
			if (forcewinusb(iid, log))
				Thread.Sleep(800);
		}
	}

	static bool isaeaiid(string iid) {
		if (string.IsNullOrEmpty(iid)) return false;
		var u = iid.ToUpperInvariant();
		if (u.IndexOf("VID_18D1&PID_2D00", StringComparison.Ordinal) >= 0
			&& u.IndexOf("MI_01", StringComparison.Ordinal) < 0)
			return true;
		return u.IndexOf("VID_18D1&PID_2D01&MI_00", StringComparison.Ordinal) >= 0;
	}

	static string devsvc(string iid) {
		try {
			using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + iid);
			return k?.GetValue("Service") as string ?? "";
		}
		catch { return ""; }
	}

	static void setguid(string iid, Action<string> log) {
		try {
			var path = @"SYSTEM\CurrentControlSet\Enum\" + iid + @"\Device Parameters";
			using var k = Registry.LocalMachine.CreateSubKey(path);
			if (k == null) return;
			k.SetValue("DeviceInterfaceGUID", AOA_GUID, RegistryValueKind.String);
			k.SetValue("DeviceInterfaceGUIDs", new[] { AOA_GUID }, RegistryValueKind.MultiString);
		}
		catch (Exception ex) { log?.Invoke($"写配件 GUID: {ex.Message}"); }
	}

	static bool forcewinusb(string iid, Action<string> log) {
		var aoaInf = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winusb-aoa.inf");
		if (tryforcewinusb(iid, Path.GetFullPath(WinUsbInf), log)) return true;
		if (File.Exists(aoaInf) && tryforcewinusb(iid, Path.GetFullPath(aoaInf), log)) return true;
		return false;
	}

	static bool tryforcewinusb(string iid, string inf, Action<string> log) {
		if (string.IsNullOrEmpty(inf) || !File.Exists(inf)) {
			log?.Invoke($"无 {inf}");
			return false;
		}
		var h = SetupDiGetClassDevsEnum(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
		if (h == IntPtr.Zero || h == new IntPtr(-1)) {
			log?.Invoke("SetupDiGetClassDevs 失败");
			return false;
		}
		try {
			var data = new SpDevinfoData();
			data.CbSize = Marshal.SizeOf(typeof(SpDevinfoData));
			var found = false;
			for (var i = 0; SetupDiEnumDeviceInfo(h, i, ref data); i++) {
				var sb = new StringBuilder(1024);
				if (!SetupDiGetDeviceInstanceId(h, ref data, sb, sb.Capacity, IntPtr.Zero)) continue;
				if (!string.Equals(sb.ToString(), iid, StringComparison.OrdinalIgnoreCase)) {
					data = new SpDevinfoData { CbSize = Marshal.SizeOf(typeof(SpDevinfoData)) };
					continue;
				}
				found = true;
				break;
			}
			if (!found) {
				log?.Invoke($"未找到实例 {iid}");
				return false;
			}
			var par = new SpDevInstallParams();
			par.CbSize = Marshal.SizeOf(typeof(SpDevInstallParams));
			if (!SetupDiGetDeviceInstallParams(h, ref data, ref par)) {
				log?.Invoke($"GetDeviceInstallParams {Marshal.GetLastWin32Error()}");
				return false;
			}
			par.Flags |= DI_ENUMSINGLEINF | DI_NOBROWSE;
			par.FlagsEx |= DI_FLAGSEX_ALLOWEXCLUDEDDRVS;
			par.DriverPath = inf;
			if (!SetupDiSetDeviceInstallParams(h, ref data, ref par)) {
				log?.Invoke($"SetDeviceInstallParams {Marshal.GetLastWin32Error()}");
				return false;
			}
			if (!SetupDiBuildDriverInfoList(h, ref data, SPDIT_CLASSDRIVER)) {
				log?.Invoke($"BuildDriverInfoList {Marshal.GetLastWin32Error()}");
				return false;
			}
			SpDrvinfoData pick = default;
			var best = 0;
			for (var j = 0; ; j++) {
				var drv = new SpDrvinfoData();
				drv.CbSize = Marshal.SizeOf(typeof(SpDrvinfoData));
				if (!SetupDiEnumDriverInfo(h, ref data, SPDIT_CLASSDRIVER, j, ref drv)) break;
				var score = drvscore(drv.Description, inf);
				if (score <= best) continue;
				pick = drv;
				best = score;
			}
			if (best == 0) {
				log?.Invoke($"{Path.GetFileName(inf)} 里没有可用的 WinUSB 设备项");
				return false;
			}
			if (!SetupDiSetSelectedDriver(h, ref data, ref pick)) {
				log?.Invoke($"SetSelectedDriver {Marshal.GetLastWin32Error()}");
				return false;
			}
			if (!DiInstallDevice(IntPtr.Zero, h, ref data, ref pick, DIIDFLAG_NOFINISHINSTALLUI, out var reboot)) {
				log?.Invoke($"DiInstallDevice {Marshal.GetLastWin32Error()}");
				return false;
			}
			log?.Invoke($"已把配件口换成 inbox WinUSB{(reboot ? "（可能需重启）" : "")}");
			return true;
		}
		catch (Exception ex) {
			log?.Invoke($"forcewinusb: {ex.Message}");
			return false;
		}
		finally { SetupDiDestroyDeviceInfoList(h); }
	}

	static int drvscore(string desc, string inf) {
		if (string.IsNullOrEmpty(desc)) return 0;
		if (desc.IndexOf("ADB", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
		var aoa = inf != null && inf.IndexOf("winusb-aoa", StringComparison.OrdinalIgnoreCase) >= 0;
		if (aoa && desc.IndexOf("ScreenKit", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
		if (aoa && desc.IndexOf("WinUSB", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
		return 0;
	}

	public static bool HelperBusy() {
		try {
			using var mx = Mutex.OpenExisting("Local\\ScreenKit_CastAoa");
			if (!mx.WaitOne(0)) return true;
			mx.ReleaseMutex();
		}
		catch (WaitHandleCannotBeOpenedException) { }
		catch { }
		return false;
	}

	public static void WaitForHelper(int ms = 4000) {
		var t0 = Environment.TickCount;
		while (unchecked(Environment.TickCount - t0) < ms) {
			if (HelperBusy()) return;
			Thread.Sleep(40);
		}
	}

	static void killstale(Action<string> log) {
		var self = Process.GetCurrentProcess().Id;
		foreach (var p in Process.GetProcessesByName("ScreenKit")) {
			if (p.Id == self) continue;
			try {
				log?.Invoke($"结束残留 ScreenKit pid={p.Id}");
				p.Kill();
			}
			catch { }
		}
	}

	public static void SpawnAoaHelper(Action<string> log) {
		try {
			if (HelperBusy()) return;
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
			log?.Invoke("AOA 助手已启动");
		}
		catch (Exception ex) { log?.Invoke($"AOA 助手: {ex.Message}"); }
	}

	public static void StopAoaHelper(Action<string> log) => killstale(log);

	public static void RunAoaBridge(Action<string> log) {
		bool created;
		using var mx = new Mutex(true, "Local\\ScreenKit_CastAoa", out created);
		if (!created) {
			log?.Invoke("AOA 助手已在运行");
			return;
		}
		try {
			log?.Invoke("AOA 助手：等待手机 USB 投屏");
			var hasAcc = false;
			foreach (var iid in usbpresent()) {
				if (isaeaiid(iid)) { hasAcc = true; break; }
			}
			if (!hasAcc) {
				CastAdbFwd.KillServer(log);
				Thread.Sleep(400);
			}
			foreach (var iid in usbpresent()) {
				if (iid.IndexOf("VID_18D1", StringComparison.OrdinalIgnoreCase) < 0
					&& iid.IndexOf("VID_2717", StringComparison.OrdinalIgnoreCase) < 0)
					continue;
				log?.Invoke($"pnp {iid} svc={devsvc(iid)}");
			}
			var lastreq = 0;
			while (true) {
				try { bindaoa(log); }
				catch (Exception ex) { log?.Invoke($"绑定 WinUSB: {ex.Message}"); }
				try {
					if (trybridge(log)) {
						log?.Invoke("USB AOA 会话结束，继续等待");
						continue;
					}
				}
				catch (Exception ex) { log?.Invoke($"AOA 桥接: {ex.Message}"); }
				var now = Environment.TickCount;
				var due = lastreq == 0 || now - lastreq > 8000;
				if (due && aoaregs().Count == 0) {
					var acc = false;
					foreach (var iid in usbpresent()) {
						if (isaeaiid(iid)) { acc = true; break; }
					}
					if (!acc) {
						lastreq = now;
						CastAdbFwd.KillServer(log);
						Thread.Sleep(400);
						try { RequestAoaOnce(log); }
						catch (Exception ex) { log?.Invoke($"AOA 请求: {ex.Message}"); }
					}
					else lastreq = now;
				}
				Thread.Sleep(500);
			}
		}
		finally {
			try { mx.ReleaseMutex(); } catch { }
			try { UsbDevice.Exit(); } catch { }
		}
	}

	static bool trybridge(Action<string> log) {
		foreach (var r in aoaregs()) {
			UsbDevice dev = null;
			try {
				if (!r.Open(out dev) || dev == null) {
					log?.Invoke($"AOA 打开失败 {r.Vid:X4}:{r.Pid:X4} {r.Name}");
					continue;
				}
				var whole = dev as IUsbDevice;
				whole?.SetConfiguration(1);
				whole?.ClaimInterface(0);
				if (!openeps(dev, out var rd, out var wr)) {
					log?.Invoke($"AOA 无 bulk 端点 {r.Vid:X4}:{r.Pid:X4} {r.Name}");
					dev.Close();
					dev = null;
					continue;
				}
				byte inEp = 0, outEp = 0;
				try { inEp = rd.EndpointInfo.Descriptor.EndpointID; } catch { }
				try { outEp = wr.EndpointInfo.Descriptor.EndpointID; } catch { }
				log?.Invoke($"USB AOA 已打开 {r.Vid:X4}:{r.Pid:X4} {r.Name} {shortpath(r.SymbolicName)} in={inEp:X2} out={outEp:X2}，等待手机发包");
				Mutex bridgeMx = null;
				var bridgeHeld = false;
				try {
					bridgeMx = new Mutex(false, AOA_BRIDGE_MX);
					bridgeHeld = bridgeMx.WaitOne(8000);
					if (!bridgeHeld) {
						log?.Invoke("AOA 桥接互斥超时");
						dev.Close();
						dev = null;
						continue;
					}
					using var usb = new CastUsbBulkStream(rd, wr, 400);
					usb.IdleMs = 0;
					var first = new byte[16384];
					var n = usb.Read(first, 0, first.Length);
					if (n <= 0) {
						log?.Invoke("AOA 读到空包");
						dev.Close();
						dev = null;
						continue;
					}
					usb.IdleMs = 0;
					using var up = new NamedPipeClientStream(".", CastProto.USB_PIPE, PipeDirection.Out);
					using var down = new NamedPipeClientStream(".", CastProto.USB_PIPE_DOWN, PipeDirection.In);
					up.Connect(4000);
					down.Connect(4000);
					log?.Invoke("USB AOA 已桥接到命名管道");
					usb.IdleMs = 2000;
					pump(usb, up, down, first, n, log);
					log?.Invoke("USB AOA 会话结束");
					return true;
				}
				finally {
					if (bridgeHeld && bridgeMx != null) {
						try { bridgeMx.ReleaseMutex(); } catch { }
					}
					try { bridgeMx?.Dispose(); } catch { }
				}
			}
			catch (Exception ex) {
				log?.Invoke($"AOA 桥接 {r.Vid:X4}:{r.Pid:X4}: {ex.Message}");
			}
			finally {
				try { dev?.Close(); } catch { }
			}
		}
		return false;
	}

	static List<WinUsbRegistry> aoaregs() {
		List<WinUsbRegistry> list = new();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var g in IfGuids) {
			List<WinUsbRegistry> wr = null;
			try { WinUsbRegistry.GetWinUsbRegistryList(g, out wr); }
			catch { }
			if (wr == null) continue;
			foreach (var r in wr) {
				if (!isaeapid(r.Vid, r.Pid)) continue;
				if (isadbchild(r)) continue;
				if (!iswinusbiface(r)) continue;
				var key = $"{r.Vid:X4}:{r.Pid:X4}:{r.SymbolicName}";
				if (!seen.Add(key)) continue;
				list.Add(r);
			}
		}
		return list;
	}

	static bool isadbchild(UsbRegistry r) {
		if (r.Pid != AOA_ADB_PID) return false;
		var s = $"{r.SymbolicName} {r.Name}";
		return s.IndexOf("MI_01", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static bool iswinusbiface(UsbRegistry r) {
		var iid = iidfromsym(r.SymbolicName);
		if (!string.IsNullOrEmpty(iid) &&
			string.Equals(devsvc(iid), "WINUSB", StringComparison.OrdinalIgnoreCase))
			return true;
		var s = $"{r.SymbolicName} {r.Name}";
		foreach (var present in usbpresent()) {
			if (!isaeaiid(present)) continue;
			if (!string.Equals(devsvc(present), "WINUSB", StringComparison.OrdinalIgnoreCase))
				continue;
			if (s.IndexOf(present, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			if (s.IndexOf(present.Replace('\\', '#'), StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
		}
		return false;
	}

	static string iidfromsym(string sym) {
		if (string.IsNullOrEmpty(sym)) return "";
		var s = sym.Trim();
		if (s.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
			s = s.Substring(4);
		var ix = s.IndexOf("#{");
		if (ix >= 0) s = s.Substring(0, ix);
		return s.Replace('#', '\\');
	}

	static bool tryquickhello(Stream usb, byte[] first, int n, Action<string> log) {
		if (first == null || n < 14) return false;
		uint mag = ((uint)first[0] << 24) | ((uint)first[1] << 16) | ((uint)first[2] << 8) | first[3];
		if (mag != CastProto.MAGIC || first[4] != CastProto.T_JSON) return false;
		try {
			var q = CastProto.PackJson(new { cmd = "hello", name = CastHost.Name });
			usb.Write(q, 0, q.Length);
			usb.Flush();
			log?.Invoke($"AOA 即时 hello {q.Length}B");
			return true;
		}
		catch (Exception ex) {
			log?.Invoke($"AOA 即时 hello {ex.Message}");
			return false;
		}
	}

	static void pump(Stream usb, Stream up, Stream down, byte[] first, int n, Action<string> log) {
		using var cts = new CancellationTokenSource();
		if (first != null && n > 0) {
			tryquickhello(usb, first, n, log);
			up.Write(first, 0, n);
			up.Flush();
		}
		var reply = new byte[16384];
		var n2 = 0;
		try { n2 = down.Read(reply, 0, reply.Length); }
		catch (Exception ex) { log?.Invoke($"AOA 读回包 {ex.Message}"); }
		log?.Invoke($"AOA 回包 {n2}B");
		if (n2 > 0) {
			try {
				usb.Write(reply, 0, n2);
				usb.Flush();
				log?.Invoke($"AOA 已写 hello 回包 {n2}B");
			}
			catch (Exception ex) { log?.Invoke($"AOA 写回包 {ex.Message}"); }
		}
		var t1 = new Thread(() => copy(usb, up, cts)) { IsBackground = true, Name = "aoa-u2p" };
		t1.Start();
		var t2 = new Thread(() => copy(down, usb, cts)) { IsBackground = true, Name = "aoa-p2u" };
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

	static bool tryopenpath(string path, Action<string> log, bool start) {
		if (string.IsNullOrEmpty(path)) return false;
		if (path.IndexOf("VID_18D1&PID_2D0", StringComparison.OrdinalIgnoreCase) >= 0 && start)
			return false;
		WinUsbDevice dev = null;
		try {
			if (!WinUsbDevice.Open(path, out dev) || dev == null) return false;
			if (!getproto(dev, out var proto) || proto < 1) {
				if (!start) log?.Invoke($"    open ok aoa=no");
				return false;
			}
			log?.Invoke($"USB AOA 协议 v{proto} {shortpath(path)}");
			if (!start) return true;
			return switchaoa(dev, log, shortpath(path));
		}
		catch (Exception ex) {
			log?.Invoke($"path {shortpath(path)}: {ex.Message}");
			return false;
		}
		finally { try { dev?.Close(); } catch { } }
	}

	static readonly Guid[] IfGuids = {
		new Guid("88BAE032-5A81-49f0-BC3D-A4FF138216D6"),
		new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED"),
		new Guid("F72FE0D4-CBCB-407D-8814-9ED673D0DD6B"),
		new Guid("AE18AA20-7F6A-11D4-97DD-00010229B959"),
		new Guid("DEE824EF-729B-4A0E-9C14-B7117D33A817"),
	};

	static List<string> winusbpaths() {
		var list = new List<string>();
		foreach (var g in IfGuids) {
			try {
				foreach (var p in ifaces(g))
					if (!list.Contains(p)) list.Add(p);
			}
			catch { }
		}
		return list;
	}

	static List<string> usbpresent() {
		var list = new List<string>();
		var h = SetupDiGetClassDevsEnum(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
		if (h == IntPtr.Zero || h == new IntPtr(-1)) return list;
		try {
			var data = new SpDevinfoData();
			data.CbSize = Marshal.SizeOf(typeof(SpDevinfoData));
			for (var i = 0; SetupDiEnumDeviceInfo(h, i, ref data); i++) {
				var sb = new StringBuilder(1024);
				if (SetupDiGetDeviceInstanceId(h, ref data, sb, sb.Capacity, IntPtr.Zero))
					list.Add(sb.ToString());
				data = new SpDevinfoData { CbSize = Marshal.SizeOf(typeof(SpDevinfoData)) };
			}
		}
		finally { SetupDiDestroyDeviceInfoList(h); }
		return list;
	}

	static List<string> ifaces(Guid guid) {
		var list = new List<string>();
		var h = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
		if (h == IntPtr.Zero || h == new IntPtr(-1)) return list;
		try {
			var data = new SpInterfaceData();
			data.CbSize = Marshal.SizeOf(typeof(SpInterfaceData));
			for (uint i = 0; SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref guid, i, ref data); i++) {
				var need = 0;
				SetupDiGetDeviceInterfaceDetail(h, ref data, IntPtr.Zero, 0, ref need, IntPtr.Zero);
				if (need <= 0) continue;
				var buf = Marshal.AllocHGlobal(need);
				try {
					var cb = IntPtr.Size == 8 ? 8 : 6;
					Marshal.WriteInt32(buf, cb);
					if (!SetupDiGetDeviceInterfaceDetail(h, ref data, buf, need, ref need, IntPtr.Zero))
						continue;
					var path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
					if (!string.IsNullOrEmpty(path)) list.Add(path);
				}
				finally { Marshal.FreeHGlobal(buf); }
			}
		}
		finally { SetupDiDestroyDeviceInfoList(h); }
		return list;
	}

	const int DIGCF_PRESENT = 2;
	const int DIGCF_ALLCLASSES = 4;
	const int DIGCF_DEVICEINTERFACE = 16;
	const int SPDIT_CLASSDRIVER = 1;
	const int DI_ENUMSINGLEINF = 0x00010000;
	const int DI_NOBROWSE = 0x00000001;
	const int DI_FLAGSEX_ALLOWEXCLUDEDDRVS = 0x00000800;
	const int DIIDFLAG_NOFINISHINSTALLUI = 2;

	[StructLayout(LayoutKind.Sequential)]
	struct SpInterfaceData {
		public int CbSize;
		public Guid InterfaceClassGuid;
		public int Flags;
		public IntPtr Reserved;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct SpDevinfoData {
		public int CbSize;
		public Guid ClassGuid;
		public int DevInst;
		public IntPtr Reserved;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct SpDevInstallParams {
		public int CbSize;
		public int Flags;
		public int FlagsEx;
		public IntPtr HwndParent;
		public IntPtr InstallMsgHandler;
		public IntPtr InstallMsgHandlerContext;
		public IntPtr FileQueue;
		public IntPtr ClassInstallReserved;
		public int Reserved;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string DriverPath;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct SpDrvinfoData {
		public int CbSize;
		public int DriverType;
		public IntPtr Reserved;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string Description;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string MfgName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string ProviderName;
		public long DriverDate;
		public ulong DriverVersion;
	}

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern IntPtr SetupDiGetClassDevs(ref Guid cls, IntPtr enumr, IntPtr parent, int flags);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
	static extern IntPtr SetupDiGetClassDevsEnum(IntPtr cls, string enumr, IntPtr parent, int flags);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiEnumDeviceInfo(IntPtr h, int index, ref SpDevinfoData data);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetupDiGetDeviceInstanceId(IntPtr h, ref SpDevinfoData data, StringBuilder id, int size, IntPtr need);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetupDiGetDeviceInstallParams(IntPtr h, ref SpDevinfoData data, ref SpDevInstallParams par);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetupDiSetDeviceInstallParams(IntPtr h, ref SpDevinfoData data, ref SpDevInstallParams par);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiBuildDriverInfoList(IntPtr h, ref SpDevinfoData data, int type);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetupDiEnumDriverInfo(IntPtr h, ref SpDevinfoData data, int type, int index, ref SpDrvinfoData drv);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetupDiSetSelectedDriver(IntPtr h, ref SpDevinfoData data, ref SpDrvinfoData drv);

	[DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool DiInstallDevice(IntPtr hwnd, IntPtr devs, ref SpDevinfoData data, ref SpDrvinfoData drv, int flags, out bool reboot);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr info, ref Guid guid, uint index, ref SpInterfaceData data);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SpInterfaceData data, IntPtr detail, int detailSize, ref int need, IntPtr devInfo);

	[DllImport("setupapi.dll", SetLastError = true)]
	static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

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
		var empty = Array.Empty<byte>();
		var pkt = new UsbSetupPacket(
			(byte)(UsbCtrlFlags.Direction_Out | UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Device),
			START, 0, 0, 0);
		dev.ControlTransfer(ref pkt, empty, 0, out _);
	}

	public void Dispose() {
		stop = true;
		th = null;
	}
}
