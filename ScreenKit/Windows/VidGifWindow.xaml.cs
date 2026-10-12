using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → 视频转 GIF。</summary>
public partial class VidGifWindow : Window {
	readonly OcrOptions opt;
	readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
	CancellationTokenSource cts;
	string src = "";
	int srcW;
	int srcH;
	double duration;
	bool ready;
	bool busy;
	bool scrub;
	bool playing;

	public VidGifWindow(OcrOptions o) {
		opt = o ?? new OcrOptions();
		InitializeComponent();
		applylang();
		loadui();
		initev();
		WindowEsc.Attach(this, onesc);
		ready = true;
	}

	void initev() {
		bopen.Click += (_, _) => pick();
		bplay.Click += (_, _) => toggleplay();
		bstart.Click += (_, _) => estart.Text = secnow().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
		bend.Click += (_, _) => eend.Text = secnow().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
		bgo.Click += (_, _) => _ = go();
		bcancel.Click += (_, _) => cts?.Cancel();
		bclose.Click += (_, _) => Close();
		eplay.MediaOpened += (_, _) => onopened();
		eplay.MediaEnded += (_, _) => { playing = false; syncplay(); };
		eplay.MediaFailed += (_, ev) => lbmsg.Text = ev.ErrorException?.Message ?? "";
		tick.Tick += (_, _) => ontick();
		tick.Start();
		eslider.PreviewMouseDown += (_, _) => scrub = true;
		eslider.PreviewMouseUp += (_, _) => { scrub = false; seek(eslider.Value); };
		eslider.ValueChanged += (_, _) => { if (scrub) seek(eslider.Value); };
		PreviewDragOver += ondragover;
		Drop += ondrop;
		Closing += (_, _) => { tick.Stop(); try { eplay.Stop(); } catch { } if (ready) saveopt(); };
	}

	void applylang() {
		Title = Loc.T("vidgif.title");
		ToolBtnUi.Set(bopen, ToolBtnUi.Browse, Loc.T("vidgif.open"));
		lbfile.Text = Loc.T("vidgif.drop");
		lbdrop.Text = Loc.T("vidgif.drop");
		lbstart.Text = Loc.T("vidgif.start");
		lbend.Text = Loc.T("vidgif.end");
		ToolBtnUi.Set(bstart, null, Loc.T("vidgif.setstart"));
		ToolBtnUi.Set(bend, null, Loc.T("vidgif.setend"));
		lbfps.Text = Loc.T("vidgif.fps");
		lbcolors.Text = Loc.T("vidgif.colors");
		eshorten.Content = Loc.T("vidgif.short");
		lbshort.Text = Loc.T("vidgif.shortpx");
		ToolBtnUi.Set(bgo, ToolBtnUi.Play, Loc.T("vidgif.go"));
		ToolBtnUi.Set(bcancel, ToolBtnUi.Cancel, Loc.T("vidgif.cancel"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("vidgif.close"));
		syncplay();
	}

	void loadui() {
		efps.Text = VidGif.NormFps(opt.VidGifFps).ToString();
		var colors = VidGif.NormColors(opt.VidGifColors);
		foreach (var item in ecolors.Items) {
			if (item is ComboBoxItem ci && ci.Tag is string tag && int.TryParse(tag, out var n) && n == colors) {
				ecolors.SelectedItem = ci;
				break;
			}
		}
		eshorten.IsChecked = opt.VidGifShortEnabled;
		eshort.Text = (opt.VidGifShortPx < 16 ? 480 : opt.VidGifShortPx).ToString();
	}

	void saveopt() {
		if (int.TryParse((efps.Text ?? "").Trim(), out var fps))
			opt.VidGifFps = VidGif.NormFps(fps);
		if ((ecolors.SelectedItem as ComboBoxItem)?.Tag is string tag && int.TryParse(tag, out var colors))
			opt.VidGifColors = VidGif.NormColors(colors);
		opt.VidGifShortEnabled = eshorten.IsChecked == true;
		if (int.TryParse((eshort.Text ?? "").Trim(), out var sp) && sp >= 16 && sp <= 7680)
			opt.VidGifShortPx = sp;
	}

	void pick() {
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Filter = Loc.T("vidgif.filter"),
			Multiselect = false,
		};
		if (dlg.ShowDialog(this) == true) loadfile(dlg.FileName);
	}

	void ondragover(object sender, DragEventArgs e) {
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (files == null) return;
		foreach (var f in files) {
			if (VidConvert.IsVideoPath(f)) { loadfile(f); return; }
		}
	}

	void loadfile(string path) {
		if (busy || string.IsNullOrWhiteSpace(path)) return;
		src = path;
		lbfile.Text = System.IO.Path.GetFileName(path);
		lbdrop.Visibility = Visibility.Collapsed;
		srcW = 0;
		srcH = 0;
		duration = 0;
		if (FfmpegGifEncode.TryProbe(path, out var w, out var h, out var dur, out _, out var err)) {
			srcW = w;
			srcH = h;
			duration = dur;
		}
		else lbmsg.Text = err ?? "";
		try {
			eplay.Stop();
			eplay.Source = new Uri(path);
			eplay.Play();
			playing = true;
		}
		catch (Exception ex) {
			lbmsg.Text = ex.Message;
		}
		estart.Text = "0";
		var end = duration > 0 ? Math.Min(duration, 5) : 5;
		eend.Text = end.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
		if (duration > 0) eslider.Maximum = duration;
		syncplay();
		lbpos.Text = clock(0) + " / " + clock(duration);
	}

	void onopened() {
		if (eplay.NaturalDuration.HasTimeSpan) {
			var sec = eplay.NaturalDuration.TimeSpan.TotalSeconds;
			if (sec > duration) duration = sec;
			eslider.Maximum = Math.Max(0.1, duration);
		}
		if (eplay.NaturalVideoWidth > 0) {
			srcW = eplay.NaturalVideoWidth;
			srcH = eplay.NaturalVideoHeight;
		}
		eplay.Pause();
		playing = false;
		syncplay();
		lbpos.Text = clock(0) + " / " + clock(duration);
	}

	void toggleplay() {
		if (eplay.Source == null) return;
		if (playing) {
			eplay.Pause();
			playing = false;
		}
		else {
			eplay.Play();
			playing = true;
		}
		syncplay();
	}

	void syncplay() {
		ToolBtnUi.Set(bplay, ToolBtnUi.Play, Loc.T(playing ? "vidgif.pause" : "vidgif.play"));
	}

	void ontick() {
		if (scrub || eplay.Source == null) return;
		var sec = 0.0;
		try { sec = eplay.Position.TotalSeconds; } catch { return; }
		if (!scrub) eslider.Value = Math.Min(eslider.Maximum, sec);
		lbpos.Text = clock(sec) + " / " + clock(duration);
	}

	void seek(double sec) {
		if (eplay.Source == null) return;
		try { eplay.Position = TimeSpan.FromSeconds(Math.Max(0, sec)); } catch { }
	}

	double secnow() {
		try { return eplay.Position.TotalSeconds; }
		catch { return 0; }
	}

	static string clock(double sec) {
		if (sec < 0 || double.IsNaN(sec)) sec = 0;
		var t = TimeSpan.FromSeconds(sec);
		return t.TotalHours >= 1
			? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
			: $"{t.Minutes}:{t.Seconds:00}";
	}

	void onesc() {
		if (busy) cts?.Cancel();
		else Close();
	}

	void setbusy(bool on) {
		busy = on;
		bopen.IsEnabled = !on;
		bgo.IsEnabled = !on;
		efps.IsEnabled = !on;
		ecolors.IsEnabled = !on;
		eshorten.IsEnabled = !on;
		eshort.IsEnabled = !on;
		estart.IsEnabled = !on;
		eend.IsEnabled = !on;
		bstart.IsEnabled = !on;
		bend.IsEnabled = !on;
		bcancel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
	}

	async Task go() {
		if (busy) return;
		if (string.IsNullOrEmpty(src) || !System.IO.File.Exists(src)) {
			MessageBox.Show(this, Loc.T("vidgif.empty"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		saveopt();
		if (!int.TryParse((efps.Text ?? "").Trim(), out var fps) || fps < 1 || fps > 30) {
			MessageBox.Show(this, Loc.T("vidgif.fps.bad"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var shortEn = eshorten.IsChecked == true;
		var sp = opt.VidGifShortPx < 16 ? 480 : opt.VidGifShortPx;
		if (shortEn && (!int.TryParse((eshort.Text ?? "").Trim(), out sp) || sp < 16 || sp > 7680)) {
			MessageBox.Show(this, Loc.T("vidgif.short.bad"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (!double.TryParse((estart.Text ?? "").Trim(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var start)
			|| !double.TryParse((eend.Text ?? "").Trim(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var end)) {
			MessageBox.Show(this, Loc.T("vidgif.badtime"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (srcW < 2 || duration <= 0) {
			if (!FfmpegGifEncode.TryProbe(src, out srcW, out srcH, out duration, out _, out var perr)) {
				MessageBox.Show(this, string.IsNullOrEmpty(perr) ? Loc.T("vidgif.noffmpeg") : perr, Title,
					MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
		}
		if (!VidGif.TryRange(duration, start, end, out start, out end, out var why)) {
			var key = why == "long" ? "vidgif.toolong" : "vidgif.badtime";
			var text = why == "long" ? string.Format(Loc.T(key), (int)VidGif.MAXSEC) : Loc.T(key);
			MessageBox.Show(this, text, Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (!FeaturePrompt.EnsureFfmpeg(this) || !FfmpegLoader.TryInit(out _)) {
			MessageBox.Show(this, Loc.T("vidgif.noffmpeg"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var colors = VidGif.NormColors(opt.VidGifColors);
		int ow, oh;
		if (shortEn) VidConvert.Fit(srcW, srcH, sp, out ow, out oh);
		else { ow = Math.Max(16, srcW); oh = Math.Max(16, srcH); }
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Filter = "GIF|*.gif",
			FileName = System.IO.Path.GetFileNameWithoutExtension(src) + ".gif",
			AddExtension = true,
			DefaultExt = ".gif",
		};
		if (dlg.ShowDialog(this) != true) return;
		setbusy(true);
		cts = new CancellationTokenSource();
		var token = cts.Token;
		var dest = dlg.FileName;
		lbmsg.Text = Loc.T("vidgif.saving");
		try {
			await Task.Run(() => VidGif.Convert(src, dest, ow, oh, fps, colors, start, end, token), token);
			lbmsg.Text = Loc.T("vidgif.ok") + " " + dest;
		}
		catch (OperationCanceledException) {
			lbmsg.Text = Loc.T("vidgif.st.cancel");
		}
		catch (Exception ex) {
			lbmsg.Text = ex.Message;
		}
		finally {
			setbusy(false);
			cts.Dispose();
			cts = null;
		}
	}
}
