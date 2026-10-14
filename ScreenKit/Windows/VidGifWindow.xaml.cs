using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
	bool dragging;

	public VidGifWindow(OcrOptions o) {
		opt = o ?? new OcrOptions();
		InitializeComponent();
		applylang();
		loadui();
		initev();
		WindowEsc.Attach(this, onesc);
		SourceInitialized += (_, _) => hookdrop();
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
		AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(ondrag), true);
		AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(ondrag), true);
		AddHandler(DragDrop.DragEnterEvent, new DragEventHandler(ondrag), true);
		AddHandler(DragDrop.DragOverEvent, new DragEventHandler(ondrag), true);
		AddHandler(DragDrop.PreviewDragLeaveEvent, new DragEventHandler(ondragleave), true);
		AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(ondrop), true);
		eplay.Loaded += (_, _) => clearchilddrop();
		pdrop.PreviewMouseLeftButtonDown += (_, _) => hidedrag();
		Closing += (_, _) => { tick.Stop(); try { eplay.Stop(); } catch { } if (ready) saveopt(); };
	}

	void applylang() {
		Title = Loc.T("vidgif.title");
		ToolBtnUi.Set(bopen, ToolBtnUi.Browse, Loc.T("vidgif.open"));
		lbfile.Text = Loc.T("vidgif.drop");
		lbdrop.Text = Loc.T("vidgif.drop");
		lbrelease.Text = Loc.T("vidgif.release");
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

	void ondrag(object sender, DragEventArgs e) {
		if (busy || !filedrag(e.Data)) {
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return;
		}
		var allow = e.AllowedEffects;
		if ((allow & DragDropEffects.Copy) != 0) e.Effects = DragDropEffects.Copy;
		else if ((allow & DragDropEffects.Move) != 0) e.Effects = DragDropEffects.Move;
		else e.Effects = allow;
		e.Handled = true;
		if (e.Effects == DragDropEffects.None) return;
		dragging = true;
		showdrag();
	}

	void ondragleave(object sender, DragEventArgs e) {
		dragging = false;
		Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
			if (dragging) return;
			if (insidecursor()) return;
			hidedrag();
		}));
	}

	bool insidecursor() {
		try {
			var p = Mouse.GetPosition(this);
			return p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
		}
		catch { return false; }
	}

	void ondrop(object sender, DragEventArgs e) {
		dragging = false;
		hidedrag();
		e.Handled = true;
		if (busy) return;
		var path = takepath(e.Data);
		if (string.IsNullOrEmpty(path)) {
			lbmsg.Text = Loc.T("vidgif.notvideo");
			return;
		}
		loadfile(path);
	}

	void showdrag() {
		if (pdrop.Visibility == Visibility.Visible) return;
		pdrop.Visibility = Visibility.Visible;
		pview.BorderBrush = (Brush)FindResource("Accent");
		pview.BorderThickness = new Thickness(2);
		lbdrop.Text = Loc.T("vidgif.release");
		lbdrop.Foreground = Brushes.White;
		if (string.IsNullOrEmpty(src)) lbdrop.Visibility = Visibility.Visible;
	}

	void hidedrag() {
		if (pdrop.Visibility != Visibility.Visible) return;
		pdrop.Visibility = Visibility.Collapsed;
		pview.BorderThickness = new Thickness(0);
		lbdrop.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
		lbdrop.Text = Loc.T("vidgif.drop");
	}

	static bool filedrag(IDataObject data) {
		if (data == null) return false;
		try {
			string[] fmts = null;
			try { fmts = data.GetFormats(); }
			catch { return true; }
			if (fmts == null || fmts.Length == 0) return true;
			var text = true;
			foreach (var f in fmts) {
				if (string.IsNullOrEmpty(f)) continue;
				if (f == DataFormats.FileDrop || f == "FileNameW" || f == "FileName"
					|| f == "FileGroupDescriptorW" || f == "FileGroupDescriptor"
					|| f.IndexOf("FileContents", StringComparison.OrdinalIgnoreCase) >= 0
					|| f.IndexOf("Shell IDList", StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
				if (f != DataFormats.Text && f != DataFormats.UnicodeText && f != "Text" && f != "UnicodeText")
					text = false;
			}
			return !text;
		}
		catch { return true; }
	}

	static string takepath(IDataObject data) {
		if (data == null) return "";
		var files = new List<string>();
		try { addfiledrop(data, files); }
		catch { }
		if (files.Count == 0) {
			try { addfilename(data, files); }
			catch { }
		}
		foreach (var f in files) {
			if (VidConvert.IsVideoPath(f) && File.Exists(f)) return f;
		}
		try {
			var virt = takevirtual(data);
			if (!string.IsNullOrEmpty(virt)) return virt;
		}
		catch { }
		return "";
	}

	static void addfiledrop(IDataObject data, List<string> r) {
		object raw;
		try { raw = data.GetData(DataFormats.FileDrop); }
		catch { return; }
		if (raw is string[] arr) r.AddRange(arr);
		else if (raw is string one) r.Add(one);
		else if (raw is System.Collections.IEnumerable en) {
			foreach (var o in en) {
				if (o is string s && !string.IsNullOrWhiteSpace(s)) r.Add(s);
			}
		}
	}

	static void addfilename(IDataObject data, List<string> r) {
		object raw = null;
		try { raw = data.GetData("FileNameW"); }
		catch { }
		if (raw == null) {
			try { raw = data.GetData("FileName"); }
			catch { }
		}
		if (raw is string s) r.Add(s.TrimEnd('\0'));
		else if (raw is string[] a) r.AddRange(a);
	}

	static string takevirtual(IDataObject data) {
		if (!data.GetDataPresent("FileGroupDescriptorW")) return "";
		if (data.GetData("FileGroupDescriptorW") is not MemoryStream desc) return "";
		var buf = desc.ToArray();
		if (buf.Length < 4) return "";
		var count = BitConverter.ToInt32(buf, 0);
		const int descSize = 592;
		const int nameOff = 72;
		if (count < 1 || 4 + descSize > buf.Length) return "";
		var name = Encoding.Unicode.GetString(buf, 4 + nameOff, 520).TrimEnd('\0');
		if (!VidConvert.IsVideoPath(name)) return "";
		if (data.GetData("FileContents") is not Stream content) return "";
		var ext = Path.GetExtension(name);
		if (string.IsNullOrEmpty(ext)) ext = ".mp4";
		var path = TmpStore.NewPath("vidgif", ext);
		using (var fs = File.Create(path))
			content.CopyTo(fs);
		if (new FileInfo(path).Length > 0) return path;
		try { File.Delete(path); } catch { }
		return "";
	}

	void hookdrop() {
		var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
		src?.AddHook(onmsg);
		clearchilddrop();
	}

	IntPtr onmsg(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		const int WM_PARENTNOTIFY = 0x0210;
		const int WM_CREATE = 1;
		if (msg == WM_PARENTNOTIFY && (wParam.ToInt32() & 0xFFFF) == WM_CREATE)
			Dispatcher.BeginInvoke(new Action(clearchilddrop));
		return IntPtr.Zero;
	}

	// 预览控件会建子窗口并自己注册拖放，落在画面上时事件到不了本窗口。
	void clearchilddrop() {
		var hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero) return;
		EnumChildWindows(hwnd, clearchild, IntPtr.Zero);
	}

	static bool clearchild(IntPtr hwnd, IntPtr lp) {
		RevokeDragDrop(hwnd);
		return true;
	}

	[DllImport("user32.dll")]
	static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc cb, IntPtr lp);

	[DllImport("ole32.dll")]
	static extern int RevokeDragDrop(IntPtr hwnd);

	delegate bool EnumChildProc(IntPtr hwnd, IntPtr lp);

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
