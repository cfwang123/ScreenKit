namespace ScreenKit;

/// <summary>工具 → 录音机。</summary>
public partial class VoiceRecWindow : Window {
	readonly VoiceRec.Session session = new();
	readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
	string mode = "idle";
	int playat;

	public VoiceRecWindow() {
		InitializeComponent();
		session.Level += onlevel;
		session.Error += onerror;
		applylang();
		filldev();
		cmic.Checked += (_, _) => paint();
		cmic.Unchecked += (_, _) => paint();
		csys.Checked += (_, _) => paint();
		csys.Unchecked += (_, _) => paint();
		bdev.Click += (_, _) => filldev();
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
			try { session.Dispose(); } catch { }
		};
		timer.Tick += (_, _) => tick();
		timer.Start();
		paint();
	}

	void applylang() {
		Title = Loc.T("vrec.title");
		lbhint.Text = Loc.T("vrec.hint");
		cmic.Content = Loc.T("vrec.dev");
		csys.Content = Loc.T("vrec.sys");
		ToolBtnUi.Set(bdev, ToolBtnUi.Swap, Loc.T("vrec.refresh"));
		paint();
	}

	void filldev() {
		var cur = edev.SelectedItem as VoiceRec.Dev;
		List<VoiceRec.Dev> list;
		try { list = VoiceRec.Devices(); }
		catch { list = new List<VoiceRec.Dev>(); }
		edev.Items.Clear();
		foreach (var d in list) edev.Items.Add(d);
		if (list.Count == 0) {
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
		if (list.Count == 0) cmic.IsChecked = false;
		paint();
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
			plevel.Value = 0;
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
			plevel.Value = 0;
			paint();
			return;
		}
		if (mode == "pause") rec();
	}

	void stop() {
		session.Pause();
		session.StopPlay();
		mode = "idle";
		plevel.Value = 0;
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
			plevel.Value = 0;
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
			plevel.Value = 0;
			paint();
			lbstat.Text = Loc.T("vrec.opened", path);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("vrec.openfail", ex.Message);
		}
	}

	void tick() {
		if (mode == "play" && unchecked(Environment.TickCount - playat) > 200 && session.PlayEnded()) {
			session.StopPlay();
			mode = "idle";
			paint();
			return;
		}
		showtime();
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

	void onlevel(int peak) {
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.BeginInvoke(() => onlevel(peak));
			return;
		}
		if (mode != "rec") return;
		plevel.Value = peak;
	}

	void onerror(string message) {
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.BeginInvoke(() => onerror(message));
			return;
		}
		try { session.Pause(); } catch { }
		mode = "idle";
		plevel.Value = 0;
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
