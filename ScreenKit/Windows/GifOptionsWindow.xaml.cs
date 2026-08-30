using System.Windows;

namespace ScreenKit;

public partial class GifOptionsWindow : Window {
	public GifOptions Result { get; private set; }
	public bool Applied { get; private set; }

	public GifOptionsWindow(GifOptions current) {
		InitializeComponent();
		Result = (current ?? new GifOptions()).Clone();
		Result.Clamp();

		bcancel.Click += (_, _) => { Applied = false; Close(); };
		bok.Click += (_, _) => {
			if (!saveui()) return;
			Applied = true;
			Close();
		};
		WindowEsc.Attach(this, () => { Applied = false; Close(); });

		loadui(Result);
	}

	void loadui(GifOptions o) {
		efps.Text = o.Fps.ToString();
		emaxen.IsChecked = o.MaxSizeEnabled;
		emaxw.Text = o.MaxWidth.ToString();
		emaxh.Text = o.MaxHeight.ToString();
	}

	bool saveui() {
		if (!parseint(efps.Text, "默认输出帧率", 1, GifOptions.CaptureFps, out var fps)) return false;
		var maxEn = emaxen.IsChecked == true;
		var maxW = 1280;
		var maxH = 720;
		if (maxEn) {
			if (!parseint(emaxw.Text, "最大宽", 16, 7680, out maxW)) return false;
			if (!parseint(emaxh.Text, "最大高", 16, 4320, out maxH)) return false;
		}
		Result = new GifOptions {
			Fps = fps,
			MaxSizeEnabled = maxEn,
			MaxWidth = maxW,
			MaxHeight = maxH,
			Colors = Result.Colors,
			ScalePercent = Result.ScalePercent,
		};
		Result.Clamp();
		return true;
	}

	bool parseint(string text, string name, int min, int max, out int value) {
		value = 0;
		if (!int.TryParse((text ?? "").Trim(), out value)) {
			MessageBox.Show(this, $"{name} 请填写整数。", "GIF 录屏选项",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		if (value < min || value > max) {
			MessageBox.Show(this, $"{name} 请填写 {min} ~ {max}。", "GIF 录屏选项",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		return true;
	}
}
