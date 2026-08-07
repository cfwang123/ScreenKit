using System.Windows;
using System.Windows.Controls;

namespace WpfOCR;

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
		emaxw.Text = o.MaxWidth.ToString();
		emaxh.Text = o.MaxHeight.ToString();
		elockasp.IsChecked = o.LockAspectWhileRecording;
	}

	bool saveui() {
		var o = Result;
		o.Codec = (ecodec.SelectedItem as ComboBoxItem)?.Tag as string ?? "x264";
		if (!tryint(efps, "帧率 (FPS)", 5, 60, out var fps)) return false;
		if (!tryint(ecrf, "CRF", 0, 51, out var crf)) return false;
		o.Fps = fps;
		o.Crf = crf;
		o.AudioEnabled = eauden.IsChecked == true;
		o.AudioSource = (eaudsrc.SelectedItem as ComboBoxItem)?.Tag as string ?? "Speakers";
		if (!tryint(eaudkbps, "音频码率 (kbps)", 8, 128, out var kbps)) return false;
		o.AudioKbps = kbps;
		o.AudioHz = (eaudhz.SelectedItem as ComboBoxItem)?.Tag is int hz ? hz : 22050;
		o.AudioMono = eaudmono.IsChecked == true;
		o.MaxSizeEnabled = emaxen.IsChecked == true;
		if (!tryint(emaxw, "最大宽", 16, 16384, out var mw)) return false;
		if (!tryint(emaxh, "最大高", 16, 16384, out var mh)) return false;
		o.MaxWidth = mw;
		o.MaxHeight = mh;
		o.LockAspectWhileRecording = elockasp.IsChecked == true;
		o.Clamp();
		return true;
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
