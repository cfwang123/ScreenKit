using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

public partial class CastOptWindow : Window {
	public bool Audio => caudio.IsChecked == true;
	public string Src => (esrc.SelectedItem as ComboBoxItem)?.Tag as string ?? "Speakers";

	public CastOptWindow(bool audio, string src) {
		InitializeComponent();
		Title = Loc.T("cast.tab.opt");
		caudio.Content = Loc.T("cast.tab.audio");
		lbsrc.Text = Loc.T("cast.tab.src");
		bok.Content = Loc.T("ok");
		esrc.Items.Add(new ComboBoxItem { Content = Loc.T("cast.tab.src.spk"), Tag = "Speakers" });
		esrc.Items.Add(new ComboBoxItem { Content = Loc.T("cast.tab.src.mic"), Tag = "Mic" });
		esrc.Items.Add(new ComboBoxItem { Content = Loc.T("cast.tab.src.both"), Tag = "MicAndSpeakers" });
		caudio.IsChecked = audio;
		var want = src ?? "Speakers";
		foreach (ComboBoxItem it in esrc.Items)
			if (string.Equals(it.Tag as string, want, StringComparison.OrdinalIgnoreCase))
				esrc.SelectedItem = it;
		if (esrc.SelectedIndex < 0) esrc.SelectedIndex = 0;
		bok.Click += (_, _) => { DialogResult = true; Close(); };
	}

	public void SetTexts(string audioText, string srcText) {
		if (!string.IsNullOrEmpty(audioText)) caudio.Content = audioText;
		if (!string.IsNullOrEmpty(srcText)) lbsrc.Text = srcText;
	}
}
