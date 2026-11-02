using System.Windows.Shapes;

namespace ScreenKit;

/// <summary>工具 → 录音机。</summary>
public partial class VoiceRecWindow : Window {
	readonly VoiceRec.Session session = new();
	readonly VoiceMeter meter = new();
	readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
	string mode = "idle";
	int playat;
	int nextsys;
	double micshow;
	double sysshow;

	public VoiceRecWindow() {
		InitializeComponent();
		session.Error += onerror;
		applylang();
		filldev();
		meter.StartSys();
		syncmic();
		syncnames();
		cmic.Checked += (_, _) => { syncmic(); paint(); };
		cmic.Unchecked += (_, _) => { syncmic(); paint(); };
		csys.Checked += (_, _) => paint();
		csys.Unchecked += (_, _) => paint();
		edev.SelectionChanged += (_, _) => { syncmic(); syncnames(); paint(); };
		bdev.Click += (_, _) => { filldev(); syncnames(); };
		brec.Click += (_, _) => rec();
		bpause.Click += (_, _) => pause();
		bstop.Click += (_, _) => stop();
		bplay.Click += (_, _) => play();
		bopen.Click += (_, _) => openwav();
		bsave.Click += (_, _) => savewav();
		AllowDrop = true;
		PreviewDragOver += ondrag;
		Drop += ondrop;
		WindowEsc.Attach(this);
		Closed += (_, _) => {
			try { timer.Stop(); } catch { }
			try { meter.Dispose(); } catch { }
			try { session.Dispose(); } catch { }
		};
		nextsys = Environment.TickCount;
		timer.Tick += (_, _) => tick();
		timer.Start();
		paint();
	}

	void applylang() {
		Title = Loc.T("vrec.title");
		lbhint.Text = Loc.T("vrec.hint");
		cmic.Content = Loc.T("vrec.dev");
		csys.Content = Loc.T("vrec.sys");
		lbmiccap.Text = Loc.T("vrec.dev");
		lbsyscap.Text = Loc.T("vrec.syscap");
		ToolBtnUi.Set(bdev, ToolBtnUi.Swap, Loc.T("vrec.refresh"));
		paint();
	}

	void filldev() {
		var cur = edev.SelectedItem as VoiceRec.Dev;
		bool wasEmpty = edev.Items.Count == 0;
		List<VoiceRec.Dev> list;
		try { list = VoiceRec.Devices(); }
		catch { list = new List<VoiceRec.Dev>(); }
		edev.Items.Clear();
		foreach (var d in list) edev.Items.Add(d);
		if (list.Count == 0) {
			cmic.IsChecked = false;
			syncmic();
			syncnames();
			paint();
			return;
		}
		int pick = 0;
		if (cur != null) {
			for (int i = 0; i < list.Count; i++) {
				if (list[i].Id == cur.Id) pick = i;
			}
		}
		edev.SelectedIndex = pick;
		if (wasEmpty) cmic.IsChecked = true;
		syncmic();
		syncnames();
		paint();
	}

	/// <summary>只让电平表跟着当前勾选和选中的设备走，录音时另开的一路共享采集互不影响。</summary>
	void syncmic() {
		var dev = edev.SelectedItem as VoiceRec.Dev;
		if (cmic.IsChecked == true && dev != null) meter.StartMic(dev.Id);
		else meter.StopMic();
	}

	void syncnames() {
		var dev = edev.SelectedItem as VoiceRec.Dev;
		lbmicdev.Text = dev == null ? Loc.T("vrec.nodev") : dev.Name;
		string sys = meter.SysName;
		lbsysdev.Text = sys.Length == 0 ? Loc.T("vrec.noplay") : sys;
	}

	void rec() {
		bool useMic = cmic.IsChecked == true;
		bool useSys = csys.IsChecked == true;
		var dev = edev.SelectedItem as VoiceRec.Dev;
		if ((useMic && dev == null) || (!useMic && !useSys)) {
			lbstat.Text = Loc.T("vrec.nodev");
			return;
		}
		try {
			string id = useMic ? dev.Id : null;
			if (mode == "pause") session.Resume(id, useSys);
			else session.StartNew(id, useSys);
			mode = "rec";
		}
		catch (Exception ex) {
			mode = "idle";
			paint();
			lbstat.Text = Loc.T("vrec.fail", ex.Message);
			return;
		}
		paint();
	}

	void pause() {
		if (mode == "rec") {
			session.Pause();
			mode = "pause";
			paint();
			return;
		}
		if (mode == "pause") rec();
	}

	void stop() {
		session.Pause();
		session.StopPlay();
		mode = "idle";
		paint();
	}

	void play() {
		if (session.ByteCount < 2) {
			lbstat.Text = Loc.T("vrec.empty");
			return;
		}
		try {
			session.Play();
			mode = "play";
			playat = Environment.TickCount;
		}
		catch (Exception ex) {
			mode = "idle";
			paint();
			lbstat.Text = Loc.T("vrec.fail", ex.Message);
			return;
		}
		paint();
	}

	void openwav() {
		if (mode == "rec") return;
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Filter = Loc.T("vrec.filter"),
			DefaultExt = ".wav",
		};
		if (dlg.ShowDialog(this) != true) return;
		loadpath(dlg.FileName);
	}

	void savewav() {
		if (session.ByteCount < 2) {
			lbstat.Text = Loc.T("vrec.empty");
			return;
		}
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Filter = Loc.T("vrec.filter"),
			DefaultExt = ".wav",
			FileName = "rec-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".wav",
		};
		if (dlg.ShowDialog(this) != true) return;
		try {
			var f = session.Format;
			VoiceRec.WriteWav(dlg.FileName, session.CopyPcm(), f.SampleRate, f.Channels);
			lbstat.Text = Loc.T("vrec.saved", dlg.FileName);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("vrec.fail", ex.Message);
		}
	}

	void loadpath(string path) {
		try {
			session.StopPlay();
			if (mode == "rec" || mode == "pause") session.Pause();
			var pcm = VoiceRec.LoadPcm(path, out var rate, out var ch);
			session.Load(pcm, rate, ch);
			mode = "idle";
			paint();
			lbstat.Text = Loc.T("vrec.opened", path);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("vrec.openfail", ex.Message);
		}
	}

	void tick() {
		if (Environment.TickCount - nextsys >= 2000) {
			nextsys = Environment.TickCount;
			string before = meter.SysName;
			meter.RefreshSys();
			if (meter.SysName != before) syncnames();
		}
		meters();
		if (mode == "play" && unchecked(Environment.TickCount - playat) > 200 && session.PlayEnded()) {
			session.StopPlay();
			mode = "idle";
			paint();
			return;
		}
		showtime();
	}

	void meters() {
		int tm = meter.MicOpen ? meter.MicLevel : 0;
		int ts = meter.SysOpen ? meter.SysLevel : 0;
		micshow = follow(micshow, tm);
		sysshow = follow(sysshow, ts);
		pmic.Value = micshow;
		psys.Value = sysshow;
		showstate(lbmicstate, dotmic, meter.MicOpen, micshow, "vrec.micoff");
		showstate(lbsysstate, dotsys, meter.SysOpen, sysshow, "vrec.nocap");
	}

	/// <summary>电平上升立刻跟随，下降按每 50ms 七格回落，避免方块跳动。</summary>
	static double follow(double cur, int target) {
		if (target >= cur) return target;
		double next = cur - 7;
		if (next < target) next = target;
		return next < 0 ? 0 : next;
	}

	void showstate(TextBlock lb, Ellipse dot, bool open, double level, string offkey) {
		if (!open) {
			lb.Text = Loc.T(offkey);
			dot.Fill = (Brush)FindResource("BorderSoft");
			return;
		}
		bool sound = level >= 6;
		lb.Text = Loc.T(sound ? "vrec.sound" : "vrec.silent");
		dot.Fill = (Brush)FindResource(sound ? "Success" : "BorderSoft");
	}

	void showtime() {
		var f = session.Format;
		int ms = mode == "play"
			? session.PlayMs()
			: VoiceRec.DurationMs(session.ByteCount, f.SampleRate, f.Channels);
		lbtime.Text = VoiceRec.Clock(ms);
	}

	void paint() {
		bool rec = mode == "rec";
		bool hold = mode == "pause";
		bool busy = rec || hold;
		bool has = session.ByteCount >= 2;
		bool useMic = cmic.IsChecked == true && edev.Items.Count > 0;
		bool useSys = csys.IsChecked == true;
		bool can = (useMic && edev.SelectedItem != null) || useSys;
		cmic.IsEnabled = !rec && edev.Items.Count > 0;
		csys.IsEnabled = !rec;
		edev.IsEnabled = !rec && cmic.IsChecked == true && edev.Items.Count > 0;
		bdev.IsEnabled = !rec;
		brec.IsEnabled = can && mode != "rec";
		bpause.IsEnabled = busy;
		bstop.IsEnabled = busy || mode == "play";
		bplay.IsEnabled = has && !rec;
		bopen.IsEnabled = !rec;
		bsave.IsEnabled = has && !rec;
		ToolBtnUi.Set(brec, "\uE720", Loc.T("vrec.rec"));
		ToolBtnUi.Set(bpause, "\uE769", Loc.T(hold ? "vrec.resume" : "vrec.pause"));
		ToolBtnUi.Set(bstop, "\uE71A", Loc.T("vrec.stop"));
		ToolBtnUi.Set(bplay, ToolBtnUi.Play, Loc.T("vrec.play"));
		ToolBtnUi.Set(bopen, ToolBtnUi.Folder, Loc.T("vrec.open"));
		ToolBtnUi.Set(bsave, ToolBtnUi.Save, Loc.T("vrec.save"));
		if (rec) lbstat.Text = Loc.T("vrec.recording");
		else if (hold) lbstat.Text = Loc.T("vrec.paused");
		else if (mode == "play") lbstat.Text = Loc.T("vrec.playing");
		else if (!can) lbstat.Text = Loc.T(edev.Items.Count == 0 ? "vrec.nodev" : "vrec.nosrc");
		else lbstat.Text = Loc.T("vrec.idle");
		showtime();
	}

	void onerror(string message) {
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.BeginInvoke(() => onerror(message));
			return;
		}
		try { session.Pause(); } catch { }
		mode = "idle";
		paint();
		lbstat.Text = Loc.T("vrec.fail", message ?? "");
	}

	void ondrag(object sender, DragEventArgs e) {
		e.Effects = wavdrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		var path = firstwav(e.Data);
		if (path.Length == 0 || mode == "rec") return;
		loadpath(path);
	}

	static bool wavdrop(IDataObject data) => firstwav(data).Length > 0;

	static string firstwav(IDataObject data) {
		if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return "";
		var files = data.GetData(DataFormats.FileDrop) as string[];
		if (files == null) return "";
		foreach (var f in files) {
			if (string.IsNullOrWhiteSpace(f)) continue;
			if (f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return f;
		}
		return "";
	}
}
