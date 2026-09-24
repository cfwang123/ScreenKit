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
	static ImageSource winicon;
	static readonly object framegate = new();
	static readonly byte[][] framebuf = new byte[2][];
	static int frameready = -1;
	static int framebusy = -1;
	static int framew, frameh, framest;
	static bool frameposted;
	static int lastadb;

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
		UsbScan = new CastUsbScan { Recv = Recv, Log = log };
		Recv.Log = log;
		Send.Log = log;
		Disc.Log = log;
		Usb.Log = log;
		Recv.OnFrame = onframe;
		Recv.OnHello = n => ui(() => {
			hidebyuser = false;
			ensureview();
			view.SetName(n);
			view.ShowCast();
		});
		Recv.OnSrc = (dw, dh) => ui(() => {
			ensureview();
			view.SetSrc(dw, dh);
		});
		Recv.OnGone = () => {
			ui(() => {
				if (Recv != null && Recv.Busy) return;
				hidebyuser = true;
				lock (framegate) {
					frameready = -1;
					frameposted = false;
				}
				view?.HideCast();
			});
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
					CastAdbFwd.Reverse(CastProto.TCP_PORT, log);
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
		}
	}

	public static void Stop() {
		if (Exiting) return;
		Exiting = true;
		timer?.Stop();
		try { Usb?.Dispose(); } catch { }
		try { Recv?.Dispose(); } catch { }
		try { Send?.Dispose(); } catch { }
		try { Disc?.Dispose(); } catch { }
		try { view?.Close(); } catch { }
		view = null;
		set = null;
		Started = false;
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

	public static void CloseCast() {
		hidebyuser = true;
		view?.HideCast();
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
		if (Recv != null && Recv.Running) return;
		Recv.Start();
		if (Opt != null) Opt.CastRecvEnabled = true;
		SaveOpt();
	}

	public static void StopRecv() {
		Recv?.Stop();
		if (Opt != null) Opt.CastRecvEnabled = false;
		SaveOpt();
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

	static void ui(Action a) {
		var d = Application.Current?.Dispatcher;
		if (d == null) return;
		if (d.CheckAccess()) a();
		else d.BeginInvoke(a);
	}
}
