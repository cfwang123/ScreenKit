namespace ScreenKit;

/// <summary>工具 → 相机。拍照、录像、扫码。</summary>
public partial class CamWindow : Window {
	readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
	CamCap.Session cap;
	CamCap.Movie movie;
	WriteableBitmap bmp;
	int openGen;
	int holduntil;
	int rec0;
	bool filling;
	string opened = "";
	bool scanning;
	bool scanbusy;
	int nextscan;
	string scantxt = "";
	string recpath = "";

	public CamWindow() {
		InitializeComponent();
		applylang();
		bdev.Click += (_, _) => filldev();
		edev.SelectionChanged += (_, _) => {
			if (filling || movie != null) return;
			opendev(false);
		};
		bshot.Click += (_, _) => shot();
		brec.Click += (_, _) => rec();
		bscan.Click += (_, _) => scan();
		WindowEsc.Attach(this);
		Closed += (_, _) => shutdown();
		timer.Tick += (_, _) => tick();
		timer.Start();
		filldev();
	}

	void applylang() {
		Title = Loc.T("cam.title");
		lbhint.Text = Loc.T("cam.hint");
		lbdev.Text = Loc.T("cam.dev");
		ckmic.Content = Loc.T("cam.mic");
		ToolBtnUi.Set(bdev, ToolBtnUi.Swap, Loc.T("cam.refresh"));
		paint();
	}

	void paint() {
		var recording = movie != null;
		ToolBtnUi.Set(bshot, "\uE722", Loc.T("cam.shot"));
		ToolBtnUi.Set(brec, recording ? "\uE71A" : "\uE714", recording ? Loc.T("cam.stop") : Loc.T("cam.rec"));
		ToolBtnUi.Set(bscan, "\uE8A1", scanning ? Loc.T("cam.scanstop") : Loc.T("cam.scan"));
		edev.IsEnabled = !recording;
		bdev.IsEnabled = !recording;
		ckmic.IsEnabled = !recording;
	}

	void filldev() {
		var cur = edev.SelectedItem as CamCap.Dev;
		List<CamCap.Dev> list;
		try { list = CamCap.Devices(); }
		catch { list = new List<CamCap.Dev>(); }
		filling = true;
		edev.Items.Clear();
		foreach (var d in list) edev.Items.Add(d);
		if (list.Count == 0) {
			filling = false;
			opendev(true);
			return;
		}
		var pick = 0;
		if (cur != null) {
			for (var i = 0; i < list.Count; i++) {
				if (list[i].Link == cur.Link) pick = i;
			}
		}
		edev.SelectedIndex = pick;
		filling = false;
		opendev(true);
	}

	void opendev(bool force) {
		var dev = edev.SelectedItem as CamCap.Dev;
		if (dev == null) {
			opened = "";
			dropcap();
			hold(Loc.T("cam.nodev"));
			return;
		}
		if (!force && opened == dev.Link) return;
		opened = dev.Link;
		var old = cap;
		cap = null;
		bmp = null;
		eprev.Source = null;
		var gen = ++openGen;
		var link = dev.Link;
		lbstat.Text = Loc.T("cam.opening");
		holduntil = Environment.TickCount - 1;
		Task.Run(() => {
			try { old?.Dispose(); } catch { }
			if (gen != openGen) return;
			CamCap.Session s = null;
			string err = null;
			try { s = CamCap.Session.Open(link); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.BeginInvoke(new Action(() => {
				if (gen != openGen) {
					if (s != null) Task.Run(() => { try { s.Dispose(); } catch { } });
					return;
				}
				cap = s;
				if (err != null) hold(Loc.T("cam.fail", err));
			}));
		});
	}

	void dropcap() {
		var s = cap;
		cap = null;
		bmp = null;
		eprev.Source = null;
		openGen++;
		if (s != null) Task.Run(() => { try { s.Dispose(); } catch { } });
	}

	void tick() {
		var shown = false;
		var w = 0;
		var h = 0;
		if (cap != null && cap.TryCopy(out var bgra, out w, out h)) {
			draw(bgra, w, h);
			shown = true;
			if (movie != null && w == movie.Width && h == movie.Height) {
				try { movie.Write(bgra, w * 4); }
				catch (Exception ex) {
					stoprec();
					hold(Loc.T("cam.recfail", ex.Message));
					return;
				}
			}
		}
		else if (cap != null && !string.IsNullOrEmpty(cap.Error)) {
			hold(Loc.T("cam.fail", cap.Error));
		}
		if (movie != null) {
			var ms = Environment.TickCount - rec0;
			if (ms < 0) ms = 0;
			lbstat.Text = movie.WantedMic && !movie.HasMic
				? Loc.T("cam.recording.mute", VoiceRec.Clock(ms))
				: Loc.T("cam.recording", VoiceRec.Clock(ms));
		}
		else if (shown && Environment.TickCount - holduntil >= 0)
			lbstat.Text = Loc.T("cam.preview", w, h);
		doscan(shown);
	}

	void draw(byte[] bgra, int w, int h) {
		if (bmp == null || bmp.PixelWidth != w || bmp.PixelHeight != h) {
			bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
			eprev.Source = bmp;
		}
		bmp.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), bgra, w * 4, 0);
	}

	void shot() {
		if (cap == null || !cap.TryCopy(out var bgra, out var w, out var h)) {
			hold(Loc.T("cam.nofrm"));
			return;
		}
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Filter = Loc.T("cam.filter.photo"),
			FileName = "cam-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
			DefaultExt = ".jpg",
		};
		if (dlg.ShowDialog(this) != true) return;
		try {
			savepic(bgra, w, h, dlg.FileName, dlg.FilterIndex == 2);
			hold(Loc.T("cam.saved", dlg.FileName));
		}
		catch (Exception ex) {
			hold(Loc.T("cam.fail", ex.Message));
		}
	}

	static void savepic(byte[] bgra, int w, int h, string path, bool png) {
		var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
		src.Freeze();
		BitmapEncoder enc = png
			? new PngBitmapEncoder()
			: new JpegBitmapEncoder { QualityLevel = 90 };
		enc.Frames.Add(BitmapFrame.Create(src));
		using var fs = File.Create(path);
		enc.Save(fs);
	}

	void rec() {
		if (movie != null) {
			var path = recpath;
			stoprec();
			hold(Loc.T("cam.saved", path));
			return;
		}
		if (cap == null || !cap.TryCopy(out var bgra, out var w, out var h)) {
			hold(Loc.T("cam.nofrm"));
			return;
		}
		if (w < 16 || h < 16) {
			hold(Loc.T("cam.recfail", w + "x" + h));
			return;
		}
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Filter = Loc.T("cam.filter.video"),
			FileName = "cam-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mp4",
			DefaultExt = ".mp4",
		};
		if (dlg.ShowDialog(this) != true) return;
		try {
			movie = new CamCap.Movie(dlg.FileName, w, h, ckmic.IsChecked == true);
			recpath = dlg.FileName;
			rec0 = Environment.TickCount;
			movie.Write(bgra, w * 4);
		}
		catch (Exception ex) {
			stoprec();
			hold(Loc.T("cam.recfail", ex.Message));
			return;
		}
		paint();
	}

	void stoprec() {
		var m = movie;
		movie = null;
		if (m != null) {
			try { m.Dispose(); } catch { }
		}
		paint();
	}

	void scan() {
		if (scanning) {
			scanning = false;
			paint();
			return;
		}
		if (!FeaturePrompt.EnsureOpenCv(this) || !FeaturePrompt.EnsureZxing(this)) return;
		scanning = true;
		nextscan = 0;
		paint();
	}

	void doscan(bool shown) {
		if (!scanning || scanbusy || !shown || cap == null) return;
		if (nextscan != 0 && Environment.TickCount - nextscan < 0) return;
		if (!cap.TryCopy(out var bgra, out var w, out var h)) return;
		BitmapSource src;
		try {
			src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
			src.Freeze();
		}
		catch { return; }
		scanbusy = true;
		nextscan = Environment.TickCount + 400;
		Task.Run(() => {
			string text = null;
			string err = null;
			try { text = QrScan.Run(src).FullText; }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.BeginInvoke(new Action(() => {
				scanbusy = false;
				if (!scanning) return;
				if (err != null) hold(Loc.T("cam.fail", err));
				else if (!string.IsNullOrEmpty(text) && text != scantxt) {
					scantxt = text;
					ecode.Text = text;
					hold(Loc.T("cam.scanning"));
				}
			}));
		});
	}

	void hold(string text) {
		lbstat.Text = text ?? "";
		holduntil = Environment.TickCount + 2500;
	}

	void shutdown() {
		try { timer.Stop(); } catch { }
		scanning = false;
		opened = "";
		try { stoprec(); } catch { }
		try { dropcap(); } catch { }
	}
}
