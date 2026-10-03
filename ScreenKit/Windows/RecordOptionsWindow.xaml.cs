using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

public partial class RecordOptionsWindow : Window {
	public RecordOptions Result { get; private set; }
	public bool Applied { get; private set; }

	public RecordOptionsWindow(RecordOptions current) {
		InitializeComponent();
		Result = (current ?? new RecordOptions()).Clone();
		Result.Clamp();

		bcancel.Click += (_, _) => { Applied = false; Close(); };
		bok.Click += (_, _) => {
			if (!saveui()) return;
			Applied = true;
			Close();
		};
		WindowEsc.Attach(this, () => { Applied = false; Close(); });
		ecodec.SelectionChanged += (_, _) => synccodec();

		foreach (var hz in RecordOptions.AudioHzChoices) {
			var it = new ComboBoxItem {
				Content = hz == 22050 ? $"{hz}（默认）" : hz.ToString(),
				Tag = hz,
			};
			eaudhz.Items.Add(it);
		}

		loadui(Result);
	}

	void loadui(RecordOptions o) {
		foreach (ComboBoxItem it in ecodec.Items) {
			if (string.Equals(it.Tag as string, o.Codec, StringComparison.OrdinalIgnoreCase)) {
				ecodec.SelectedItem = it;
				break;
			}
		}
		if (ecodec.SelectedItem == null) ecodec.SelectedIndex = 0;
		efps.Text = o.Fps.ToString();
		ecrf.Text = o.Crf.ToString();
		eav1crf.Text = o.Av1Crf.ToString();
		eauden.IsChecked = o.AudioEnabled;
		foreach (ComboBoxItem it in eaudsrc.Items) {
			if (string.Equals(it.Tag as string, o.AudioSource, StringComparison.OrdinalIgnoreCase)) {
				eaudsrc.SelectedItem = it;
				break;
			}
		}
		if (eaudsrc.SelectedItem == null) eaudsrc.SelectedIndex = 0;
		eaudkbps.Text = o.AudioKbps.ToString();
		eaudmono.IsChecked = o.AudioMono;
		eaudhz.SelectedItem = null;
		foreach (ComboBoxItem it in eaudhz.Items) {
			if (it.Tag is int hz && hz == o.AudioHz) {
				eaudhz.SelectedItem = it;
				break;
			}
		}
		if (eaudhz.SelectedItem == null) {
			foreach (ComboBoxItem it in eaudhz.Items) {
				if (it.Tag is int hz && hz == 22050) {
					eaudhz.SelectedItem = it;
					break;
				}
			}
			if (eaudhz.SelectedItem == null && eaudhz.Items.Count > 0)
				eaudhz.SelectedIndex = 0;
		}
		emaxen.IsChecked = o.MaxSizeEnabled;
		eshort.Text = o.ShortPx.ToString();
		synccodec();
		elockasp.IsChecked = o.LockAspectWhileRecording;
		emouse.IsChecked = o.RecordMouse;
		eclickhl.IsChecked = o.HighlightClicks;
	}

	bool saveui() {
		var o = Result;
		o.Codec = (ecodec.SelectedItem as ComboBoxItem)?.Tag as string ?? "x264";
		if (!tryint(efps, "帧率 (FPS)", 5, 60, out var fps)) return false;
		o.Fps = fps;
		if (pcrf.Visibility == Visibility.Visible) {
			if (!tryint(ecrf, "CRF", 0, 51, out var crf)) return false;
			o.Crf = crf;
		}
		if (pav1.Visibility == Visibility.Visible) {
			if (!tryint(eav1crf, "AV1 CRF", 0, 63, out var av1crf)) return false;
			o.Av1Crf = av1crf;
		}
		o.AudioEnabled = eauden.IsChecked == true;
		o.AudioSource = (eaudsrc.SelectedItem as ComboBoxItem)?.Tag as string ?? "Speakers";
		if (!tryint(eaudkbps, "音频码率 (kbps)", 8, 128, out var kbps)) return false;
		o.AudioKbps = kbps;
		o.AudioHz = (eaudhz.SelectedItem as ComboBoxItem)?.Tag is int hz ? hz : 22050;
		o.AudioMono = eaudmono.IsChecked == true;
		o.MaxSizeEnabled = emaxen.IsChecked == true;
		if (!tryint(eshort, "较短边", 16, 16384, out var spx)) return false;
		o.ShortPx = spx;
		o.LockAspectWhileRecording = elockasp.IsChecked == true;
		o.RecordMouse = emouse.IsChecked == true;
		o.HighlightClicks = eclickhl.IsChecked == true;
		o.Clamp();
		return true;
	}

	void synccodec() {
		var tag = (ecodec.SelectedItem as ComboBoxItem)?.Tag as string ?? "x264";
		var o = new RecordOptions { Codec = tag };
		o.Clamp();
		pcrf.Visibility = o.UsesX264Crf ? Visibility.Visible : Visibility.Collapsed;
		pav1.Visibility = o.IsAv1 ? Visibility.Visible : Visibility.Collapsed;
		lbcode.Text = o.IsAv1
			? "AV1 需要 ffmpeg64 含 libsvtav1 / libaom-av1 / librav1e。失败不会改用 x264。质量用下方 AV1 CRF。"
			: o.IsHevc
				? "x265 需要 ffmpeg64 含 libx265。失败不会改用 x264。质量用下方 CRF。"
				: o.IsMf
					? "使用 Windows 自带 Media Foundation 的 H.264，写成 MP4，不用安装 FFmpeg。没有 CRF，码率随分辨率。录制时声音用 44.1kHz 或 48kHz，结束时不再另做一次合成。"
					: o.IsMjpeg
						? "每帧一张 JPEG，写成 AVI，不用安装 FFmpeg。没有 CRF，文件比 H.264 大。超过约 1.9GB 会停止。"
						: "x264 需要 ffmpeg64。质量用下方 CRF。";
	}

	bool tryint(System.Windows.Controls.TextBox box, string name, int min, int max, out int value) {
		value = 0;
		if (!int.TryParse((box.Text ?? "").Trim(), out var v)) {
			MessageBox.Show(this, $"{name} 请填写整数。", "录屏选项",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			box.Focus();
			box.SelectAll();
			return false;
		}
		if (v < min || v > max) {
			MessageBox.Show(this, $"{name} 请填写 {min} ~ {max}。", "录屏选项",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			box.Focus();
			box.SelectAll();
			return false;
		}
		value = v;
		return true;
	}
}
