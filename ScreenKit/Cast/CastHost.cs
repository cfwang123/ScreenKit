using System.Windows.Threading;

namespace ScreenKit;

static class CastHost {
	public static CastRecvSrv Recv { get; private set; }
	public static CastSendCli Send { get; private set; }
	public static CastDisc Disc { get; private set; }
	public static CastUsbHost Usb { get; private set; }
	public static bool Exiting { get; private set; }
	public static bool Started { get; private set; }

	static CastViewWindow view;
	static CastWindow set;
	static DispatcherTimer timer;
	static Func<OcrOptions> opts;
	static Action save;
	static bool hidebyuser;

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
		Recv.OnGone = () => ui(() => view?.HideCast());
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
		timer.Tick += (_, _) => {
			if (Recv != null && Recv.Running)
				Disc?.Beacon(CastProto.TCP_PORT);
			Recv?.Tick();
		};
		timer.Start();
		Started = true;
		if (Opt == null || Opt.CastRecvEnabled) {
			try {
				Recv.Start();
				Usb.Start();
				CastAdbFwd.Reverse(CastProto.TCP_PORT, log);
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
		Usb?.Start();
		CastAdbFwd.Reverse(CastProto.TCP_PORT, log);
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
		ui(() => {
			if (hidebyuser) return;
			ensureview();
			if (!view.IsVisible) view.ShowCast();
			view.Push(px, w, h, st);
		});
	}

	static void log(string s) {
		ui(() => set?.AppendLog(s));
	}

	static void ui(Action a) {
		var d = Application.Current?.Dispatcher;
		if (d == null) return;
		if (d.CheckAccess()) a();
		else d.BeginInvoke(a);
	}
}
