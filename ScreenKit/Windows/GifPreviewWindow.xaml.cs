using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// GIF 录屏完成后的预览窗：可调最大宽高 fit / 输出帧率 / 调色板，生成预览后保存。
/// 源视频固定 24fps 采集；输出帧率在此抽帧。
/// </summary>
public partial class GifPreviewWindow : Window {
	readonly string videoPath;
	readonly int srcW, srcH, captureFps;
	readonly GifOptions baseOpt;
	string previewPath;
	FileStream previewStream;
	System.Drawing.Image previewImg;
	int genSerial;
	bool busy;
	bool closing;

	public bool Saved { get; private set; }
	public string SavedPath { get; private set; }
	public GifOptions ResultOptions { get; private set; }

	DispatcherTimer debounce;

	public GifPreviewWindow(string videoPath, int srcW, int srcH, int captureFps, GifOptions options = null) {
		InitializeComponent();
		this.videoPath = videoPath;
		this.srcW = Math.Max(16, srcW);
		this.srcH = Math.Max(16, srcH);
		this.captureFps = Compat.Clamp(captureFps <= 0 ? GifOptions.CaptureFps : captureFps, 1, 60);
		baseOpt = (options ?? new GifOptions()).Clone();
		baseOpt.Clamp();

		fillfpsitems();
		selectfps(baseOpt.Fps);
		selectcolors(baseOpt.Colors);

		emaxen.IsChecked = baseOpt.MaxSizeEnabled;
		emaxw.Text = baseOpt.MaxWidth.ToString();
		emaxh.Text = baseOpt.MaxHeight.ToString();
		lbsizehint.Text = $"源尺寸 {this.srcW}×{this.srcH} · 取消勾选则保持原尺寸";
		updateoutsize();

		debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
		debounce.Tick += (_, _) => {
			debounce.Stop();
			regenerate(force: false);
		};
		emaxen.Checked += (_, _) => { updateoutsize(); queuepreview(); };
		emaxen.Unchecked += (_, _) => { updateoutsize(); queuepreview(); };
		emaxw.TextChanged += (_, _) => { updateoutsize(); queuepreview(); };
		emaxh.TextChanged += (_, _) => { updateoutsize(); queuepreview(); };
		efps.SelectionChanged += (_, _) => queuepreview();
		ecolors.SelectionChanged += (_, _) => queuepreview();
		bapply.Click += (_, _) => regenerate(force: true);
		bcancel.Click += (_, _) => { Saved = false; Close(); };
		bsave.Click += (_, _) => onsave();
		Loaded += (_, _) => regenerate(force: true);
		Closed += (_, _) => {
			try { debounce?.Stop(); } catch { }
			cleanuppreview();
		};
		WindowEsc.Attach(this, () => { if (!busy) { Saved = false; Close(); } });
	}

	void fillfpsitems() {
		efps.Items.Clear();
		int[] prefs = { 4, 6, 8, 10, 12, 15, 20, 24 };
		foreach (var f in prefs) {
			if (f > captureFps) continue;
			efps.Items.Add(new ComboBoxItem {
				Content = f == 8 ? $"{f} fps（默认）" : $"{f} fps",
				Tag = f,
			});
		}
		if (efps.Items.Count == 0) {
			efps.Items.Add(new ComboBoxItem { Content = $"{captureFps} fps", Tag = captureFps });
		}
	}

	void selectfps(int fps) {
		fps = Compat.Clamp(fps, 1, captureFps);
		foreach (ComboBoxItem it in efps.Items) {
			if (it.Tag is int n && n == fps) {
				efps.SelectedItem = it;
				return;
			}
		}
		ComboBoxItem best = null;
		var bestD = int.MaxValue;
		foreach (ComboBoxItem it in efps.Items) {
			if (it.Tag is not int n) continue;
			var d = Math.Abs(n - fps);
			if (d < bestD) { bestD = d; best = it; }
		}
		efps.SelectedItem = best ?? (efps.Items.Count > 0 ? efps.Items[0] : null);
	}

	void selectcolors(int colors) {
		foreach (ComboBoxItem it in ecolors.Items) {
			if (it.Tag is string s && int.TryParse(s, out var n) && n == colors) {
				ecolors.SelectedItem = it;
				return;
			}
		}
		ecolors.SelectedIndex = 2;
	}

	int currentcolors() {
		if (ecolors.SelectedItem is ComboBoxItem it && it.Tag is string s && int.TryParse(s, out var n))
			return Compat.Clamp(n, 32, 256);
		return 128;
	}

	int currentfps() {
		if (efps.SelectedItem is ComboBoxItem it && it.Tag is int n)
			return Compat.Clamp(n, 1, captureFps);
		return Compat.Clamp(baseOpt.Fps, 1, captureFps);
	}

	bool tryoutsize(out int ow, out int oh, out string err) {
		ow = srcW;
		oh = srcH;
		err = null;
		if (emaxen.IsChecked != true) {
			lboutsize.Text = $"→ {ow}×{oh}（原尺寸）";
			return true;
		}
		if (!int.TryParse((emaxw.Text ?? "").Trim(), out var maxW) || maxW < 16) {
			err = "最大宽请填写 ≥16 的整数";
			lboutsize.Text = "→ ?";
			return false;
		}
		if (!int.TryParse((emaxh.Text ?? "").Trim(), out var maxH) || maxH < 16) {
			err = "最大高请填写 ≥16 的整数";
			lboutsize.Text = "→ ?";
			return false;
		}
		maxW = Math.Min(7680, maxW);
		maxH = Math.Min(4320, maxH);
		var tmp = new GifOptions {
			MaxSizeEnabled = true,
			MaxWidth = maxW,
			MaxHeight = maxH,
		};
		tmp.FitSize(srcW, srcH, out ow, out oh);
		lboutsize.Text = ow == srcW && oh == srcH
			? $"→ {ow}×{oh}（未缩小）"
			: $"→ {ow}×{oh}";
		return true;
	}

	void updateoutsize() {
		tryoutsize(out _, out _, out _);
	}

	void queuepreview() {
		if (!IsLoaded || closing) return;
		debounce.Stop();
		debounce.Start();
	}

	void setcontrolsenabled(bool on) {
		bapply.IsEnabled = on;
		emaxen.IsEnabled = on;
		emaxw.IsEnabled = on;
		emaxh.IsEnabled = on;
		efps.IsEnabled = on;
		ecolors.IsEnabled = on;
	}

	void regenerate(bool force) {
		if (closing) return;
		if (busy && !force) return;
		if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath)) {
			lbstatus.Text = "临时视频丢失，无法生成预览。";
			bsave.IsEnabled = false;
			return;
		}
		if (!tryoutsize(out var ow, out var oh, out var sizeErr)) {
			lbstatus.Text = sizeErr;
			bsave.IsEnabled = false;
			return;
		}

		var colors = currentcolors();
		var outFps = currentfps();
		var serial = ++genSerial;
		busy = true;
		bsave.IsEnabled = false;
		setcontrolsenabled(false);
		lbempty.Visibility = Visibility.Visible;
		lbempty.Text = "正在生成预览…";
		lbstatus.Text = $"编码中 · {ow}×{oh} · {colors}色 · {outFps}fps（源 {captureFps}）";

		var outPath = TmpStore.NewPath("gifprev", ".gif");
		var cap = captureFps;
		Task.Run(() => {
			string err = null;
			try {
				FfmpegGifEncode.FromVideo(videoPath, outPath, ow, oh, outFps, colors, cap);
			}
			catch (Exception ex) {
				err = ex.Message;
				RecordLog.Ex("gif_preview", ex);
			}
			Dispatcher.BeginInvoke(new Action(() => {
				if (closing || serial != genSerial) {
					try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
					return;
				}
				busy = false;
				setcontrolsenabled(true);
				if (!string.IsNullOrEmpty(err)) {
					lbempty.Text = "预览失败";
					lbstatus.Text = "失败: " + err;
					bsave.IsEnabled = false;
					return;
				}
				showpreview(outPath, ow, oh, colors, outFps);
			}));
		});
	}

	void showpreview(string path, int ow, int oh, int colors, int outFps) {
		cleanuppreview(keepPath: false);
		previewPath = path;
		try {
			previewStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			previewImg = System.Drawing.Image.FromStream(previewStream);
			epreview.Image = previewImg;
			lbempty.Visibility = Visibility.Collapsed;
			var len = new FileInfo(path).Length;
			lbstatus.Text = $"预览就绪 · {ow}×{oh} · {colors}色 · {outFps}fps · {fmtbytes(len)}";
			bsave.IsEnabled = true;
		}
		catch (Exception ex) {
			lbempty.Visibility = Visibility.Visible;
			lbempty.Text = "无法显示预览";
			lbstatus.Text = ex.Message;
			bsave.IsEnabled = false;
		}
	}

	void onsave() {
		if (busy || string.IsNullOrEmpty(previewPath) || !File.Exists(previewPath)) {
			MessageBox.Show(this, "请先生成预览。", "GIF 预览",
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var sfd = new Microsoft.Win32.SaveFileDialog {
			Title = "保存 GIF",
			Filter = "GIF 动画|*.gif",
			FileName = $"gif_{DateTime.Now:yyyyMMdd_HHmmss}.gif",
			DefaultExt = ".gif",
			AddExtension = true,
			OverwritePrompt = true,
		};
		if (sfd.ShowDialog(this) != true) return;
		try {
			var dest = sfd.FileName;
			var dir = Path.GetDirectoryName(dest);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			cleanuppreview(keepPath: true);
			File.Copy(previewPath, dest, overwrite: true);
			Saved = true;
			SavedPath = dest;
			ResultOptions = baseOpt.Clone();
			ResultOptions.Colors = currentcolors();
			ResultOptions.Fps = currentfps();
			ResultOptions.MaxSizeEnabled = emaxen.IsChecked == true;
			if (int.TryParse((emaxw.Text ?? "").Trim(), out var mw) && mw >= 16)
				ResultOptions.MaxWidth = mw;
			if (int.TryParse((emaxh.Text ?? "").Trim(), out var mh) && mh >= 16)
				ResultOptions.MaxHeight = mh;
			ResultOptions.Clamp();
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "explorer.exe",
					Arguments = $"/select,\"{dest}\"",
					UseShellExecute = true,
				});
			}
			catch { }
			Close();
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "保存失败",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			if (!string.IsNullOrEmpty(previewPath) && File.Exists(previewPath))
				showpreview(previewPath, 0, 0, currentcolors(), currentfps());
		}
	}

	void cleanuppreview(bool keepPath = false) {
		try { epreview.Image = null; } catch { }
		try { previewImg?.Dispose(); } catch { }
		previewImg = null;
		try { previewStream?.Dispose(); } catch { }
		previewStream = null;
		if (!keepPath && !string.IsNullOrEmpty(previewPath)) {
			try { File.Delete(previewPath); } catch { }
			previewPath = null;
		}
	}

	protected override void OnClosing(System.ComponentModel.CancelEventArgs e) {
		closing = true;
		base.OnClosing(e);
	}

	static string fmtbytes(long n) {
		if (n < 1024) return $"{n} B";
		if (n < 1024 * 1024) return $"{n / 1024.0:0.#} KB";
		return $"{n / (1024.0 * 1024):0.##} MB";
	}
}
