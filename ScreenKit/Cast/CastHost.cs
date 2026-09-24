using System.Threading;
using System.Windows.Threading;

namespace ScreenKit;

static class CastHost {
	public static CastRecvSrv Recv { get; private set; }
	public static CastSendCli Send { get; private set; }
	public static CastDisc Disc { get; private set; }
	public static CastUsbHost Usb { get; private set; }
	public static CastUsbScan UsbScan { get; private set; }
	public static bool Exiting { get; private set; }
	public static bool Started { get; private set; }

	static CastViewWindow view;
	static CastWindow set;
	static DispatcherTimer timer;
	static Func<OcrOptions> opts;
	static Action save;
	static bool hidebyuser;
	static int viewgen;
	static ImageSource winicon;
	static readonly object framegate = new();
	static readonly byte[][] framebuf = new byte[2][];
	static int frameready = -1;
	static int framebusy = -1;
	static int framew, frameh, framest;
	static bool frameposted;
	static int lastadb;
	static int lastaoa;
	static bool usbWant;

	public static ImageSource WinIcon {
		get {
			if (winicon != null) return winicon;
			try {
				var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "cast.ico");
				if (!File.Exists(p)) return null;
				var bmp = new BitmapImage();
				bmp.BeginInit();
				bmp.UriSource = new Uri(p);
				bmp.CacheOption = BitmapCacheOption.OnLoad;
				bmp.EndInit();
				bmp.Freeze();
				winicon = bmp;
			}
			catch { }
			return winicon;
		}
	}

	public static string Name => Environment.MachineName;
	public static OcrOptions Opt => opts?.Invoke();
	public static string QualityName =>
		string.IsNullOrEmpty(Opt?.CastQuality) ? CastQuality.Presets[1].Name : Opt.CastQuality;
	public static bool Audio => Opt?.CastAudio != false;
	public static bool ViewFill => Opt?.CastViewFill == true;

	public static void SetViewFill(bool fill) {
		if (Opt != null) Opt.CastViewFill = fill;
		SaveOpt();
		view?.ApplyScale();
	}

	public static void Init(Func<OcrOptions> getopt, Action saveopt) {
		opts = getopt;
		save = saveopt;
	}

	public static void Start() {
		if (Started) return;
		Exiting = false;
		Recv = new CastRecvSrv();
		Send = new CastSendCli();
		Disc = new CastDisc("pc", () => Name);
		Usb = new CastUsbHost { Recv = Recv };
		UsbScan = new CastUsbScan {
			Recv = Recv,
			Log = log,
			ExtraIps = () => {
				var l = new List<string>();
				if (Disc == null) return l;
				foreach (var p in Disc.Snapshot())
					if (p.Role == "send") l.Add(p.Ip);
				return l;
			}
		};
		Recv.Log = log;
		Send.Log = log;
		Disc.Log = log;
		Usb.Log = log;
		Recv.OnFrame = onframe;
		Recv.OnHello = (n, via) => {
			hidebyuser = false;
			Interlocked.Increment(ref viewgen);
			ui(() => {
				try {
					ensureview();
					view.SetName(n, via);
					view.ShowCast();
					log($"开窗 {view.Title} vis={view.IsVisible} top={view.Topmost} " +
						$"{view.Left:0},{view.Top:0} {view.Width:0}x{view.Height:0} {view.PlaceText}");
				}
				catch (Exception ex) { log($"开窗失败: {ex.Message}"); }
			}, prio: DispatcherPriority.Send);
		};
		Recv.OnSrc = (dw, dh) => ui(() => {
			ensureview();
			view.SetSrc(dw, dh);
		});
		Recv.OnGone = () => {
			var g = viewgen;
			ui(() => hidecastif(g));
		};
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
		timer.Tick += (_, _) => ThreadPool.QueueUserWorkItem(_ => {
			try {
				if (Recv != null && Recv.Running)
					Disc?.Beacon(CastProto.TCP_PORT);
				Recv?.Tick();
				UsbScan?.Tick();
				var now = Environment.TickCount;
				if (now - lastadb > 8000 || lastadb == 0) {
					lastadb = now;
					if (!CastUsbHost.HelperBusy())
						CastAdbFwd.Reverse(CastProto.TCP_PORT, log);
				}
				if (usbWant && (now - lastaoa > 8000 || lastaoa == 0)) {
					lastaoa = now;
					CastUsbHost.SpawnAoaHelper(log);
				}
			}
			catch { }
		});
		timer.Start();
		Started = true;
		if (Opt == null || Opt.CastRecvEnabled) {
			try {
				Recv.Start();
			}
			catch (Exception ex) { log(ex.Message); }
			EnableUsbHost();
		}
	}

	public static void Stop() {
		if (Exiting) return;
		Exiting = true;
		timer?.Stop();
		usbWant = false;
		CastUsbHost.StopAoaHelper(log);
		try { Usb?.Dispose(); } catch { }
		try { Recv?.Dispose(); } catch { }
		try { Send?.Dispose(); } catch { }
		try { Disc?.Dispose(); } catch { }
		try { view?.Close(); } catch { }
		view = null;
		set = null;
		Started = false;
	}

	public static void EnableUsbHost() {
		usbWant = true;
		lastaoa = Environment.TickCount;
		if (CastUsbHost.HelperBusy()) return;
		log("已启用 USB 配件主机（独立进程请求 AOA 并桥接）");
		CastUsbHost.SpawnAoaHelper(log);
	}

	public static void ShowSet() {
		if (!Started) Start();
		if (set != null) {
			if (set.WindowState == WindowState.Minimized)
				set.WindowState = WindowState.Normal;
			set.Show();
			set.Activate();
			return;
		}
		var w = new CastWindow();
		w.ShowInTaskbar = true;
		w.Owner = null;
		w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		w.Closed += (_, _) => {
			if (ReferenceEquals(set, w)) set = null;
		};
		set = w;
		w.Show();
		w.Activate();
	}

	public static void ShowView() {
		if (view == null || !view.IsVisible) {
			ensureview();
			view.ShowCast();
		}
		else view.Activate();
	}

	public static string ViaTag(string via) {
		if (string.IsNullOrEmpty(via)) return Loc.T("cast.via.net");
		if (via.IndexOf("adb", StringComparison.OrdinalIgnoreCase) >= 0) return Loc.T("cast.via.adb");
		if (via.StartsWith("usb", StringComparison.OrdinalIgnoreCase)) return Loc.T("cast.via.usb");
		return Loc.T("cast.via.net");
	}

	public static void CloseCast() {
		hidebyuser = true;
		ui(() => view?.HideCast());
		Recv?.Kick();
		log("已关闭画面并断开投屏");
	}

	public static void Disconnect() {
		Send?.Stop();
		log("已断开发送");
	}

	public static CastQuality CurQ() => CastQuality.ByName(QualityName);

	public static void ApplyQuality() {
		var q = CurQ();
		if (Send != null && Send.Running) Send.ApplyQ(q);
		Recv?.SendJson(new { cmd = "quality", name = q.Name });
		log($"画质改为 {q.Name}");
	}

	public static void SaveOpt() {
		try { save?.Invoke(); } catch { }
	}

	public static void StartRecv() {
		if (!Started) Start();
		if (Recv == null || !Recv.Running)
			Recv.Start();
		if (Opt != null) Opt.CastRecvEnabled = true;
		SaveOpt();
		EnableUsbHost();
	}

	public static void StopRecv() {
		usbWant = false;
		CastUsbHost.StopAoaHelper(log);
		Recv?.Stop();
		if (Opt != null) Opt.CastRecvEnabled = false;
		SaveOpt();
	}

	static void hidecastif(int g) {
		if (g != viewgen) return;
		if (Recv != null && Recv.Busy) return;
		lock (framegate) {
			frameready = -1;
			frameposted = false;
		}
		view?.HideCast();
		log("关窗");
	}

	static void ensureview() {
		if (view != null) return;
		view = new CastViewWindow();
		view.ShowInTaskbar = true;
		view.Owner = null;
		view.WindowStartupLocation = WindowStartupLocation.CenterScreen;
	}

	static void onframe(byte[] px, int w, int h, int st) {
		if (hidebyuser) return;
		if (px == null || w <= 0 || h <= 0 || st < w * 4) return;
		var n = (h - 1) * st + w * 4;
		if (px.Length < n) return;
		lock (framegate) {
			var i = framebusy == 0 ? 1 : 0;
			var buf = framebuf[i];
			if (buf == null || buf.Length < n) {
				buf = new byte[n];
				framebuf[i] = buf;
			}
			Buffer.BlockCopy(px, 0, buf, 0, n);
			framew = w;
			frameh = h;
			framest = st;
			frameready = i;
			if (frameposted) return;
			frameposted = true;
		}
		ui(flushframe);
	}

	static void flushframe() {
		while (true) {
			if (hidebyuser) {
				lock (framegate) {
					frameready = -1;
					frameposted = false;
					framebusy = -1;
				}
				return;
			}
			byte[] px;
			int w, h, st;
			lock (framegate) {
				var i = frameready;
				if (i < 0) {
					frameposted = false;
					framebusy = -1;
					return;
				}
				frameready = -1;
				framebusy = i;
				px = framebuf[i];
				w = framew;
				h = frameh;
				st = framest;
			}
			try {
				if (hidebyuser || px == null) continue;
				ensureview();
				if (!view.IsVisible) view.ShowCast();
				view.Push(px, w, h, st);
			}
			catch { }
		}
	}

	static void log(string s) {
		try {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(dir);
			File.AppendAllText(Path.Combine(dir, "cast_sess.txt"),
				$"{DateTime.Now:HH:mm:ss} {s}\n");
		}
		catch { }
		ui(() => set?.AppendLog(s));
	}

	static void ui(Action a, bool sync = false, DispatcherPriority prio = DispatcherPriority.Normal) {
		var d = Application.Current?.Dispatcher;
		if (d == null) {
			try { a(); } catch { }
			return;
		}
		if (d.CheckAccess()) a();
		else if (sync) try { d.Invoke(a, prio); } catch { }
		else d.BeginInvoke(a, prio);
	}
}
