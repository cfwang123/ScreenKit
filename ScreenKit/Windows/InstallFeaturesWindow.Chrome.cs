using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenKit;

partial class InstallFeaturesWindow {
	readonly ObservableCollection<ChromeRow> chromeRows = new();
	List<ChromePackItem> chromeAll = new();
	bool chromeLoaded;
	bool chromeUiLoading;

	void fillchromekind() {
		var idx = echromekind.SelectedIndex;
		chromeUiLoading = true;
		echromekind.Items.Clear();
		echromekind.Items.Add(new ComboBoxItem { Content = Loc.T("inst.chrome.all"), Tag = "" });
		echromekind.Items.Add(new ComboBoxItem { Content = Loc.T("inst.chrome.tr"), Tag = "tr" });
		echromekind.Items.Add(new ComboBoxItem { Content = Loc.T("inst.chrome.tts"), Tag = "tts" });
		echromekind.SelectedIndex = idx >= 0 && idx < echromekind.Items.Count ? idx : 0;
		chromeUiLoading = false;
		if (lvchrome.View is GridView gv && gv.Columns.Count >= 6) {
			gv.Columns[1].Header = Loc.T("inst.col.model");
			gv.Columns[2].Header = Loc.T("inst.chrome.kind");
			gv.Columns[3].Header = Loc.T("inst.chrome.detail");
			gv.Columns[4].Header = Loc.T("inst.col.size");
			gv.Columns[5].Header = Loc.T("inst.col.state");
		}
	}

	string chromekind() =>
		echromekind.SelectedItem is ComboBoxItem it ? it.Tag as string ?? "" : "";

	async Task loadchrome() {
		if (busy) return;
		setbusy(true);
		try {
			await fetchchrome();
			chromeLoaded = true;
		}
		catch (Exception ex) {
			appendlog(ex.Message);
			setstatus(ex.Message);
		}
		finally {
			setbusy(false);
		}
	}

	async Task fetchchrome() {
		var mode = proxymode();
		var addr = (eproxyaddr.Text ?? "").Trim();
		var token = cts?.Token ?? CancellationToken.None;
		List<ChromePackItem> list = null;
		await Task.Run(() => list = ChromePack.Query(mode, addr, appendlog, token));
		chromeAll = list ?? new List<ChromePackItem>();
		applychromefilter();
		var on = 0;
		foreach (var it in chromeAll)
			if (it.State == FeatureInstallState.Installed) on++;
		lbchromesum.Text = string.Format(Loc.T("inst.chrome.sum"), chromeAll.Count, on);
	}

	string proxymode() {
		if (rproxyoff.IsChecked == true) return "off";
		if (rproxyman.IsChecked == true) return "manual";
		return "system";
	}

	void applychromefilter() {
		var kind = chromekind();
		var only = cchromeonly.IsChecked == true;
		chromeUiLoading = true;
		chromeRows.Clear();
		foreach (var it in chromeAll) {
			if (kind.Length > 0 && it.Kind != kind) continue;
			if (only && it.State == FeatureInstallState.Installed) continue;
			chromeRows.Add(new ChromeRow(it));
		}
		cchromeheader.IsChecked = false;
		chromeUiLoading = false;
	}

	void setchromecheck(bool on) {
		foreach (var r in chromeRows) r.Selected = on;
	}

	async Task runchrome(bool install) {
		if (busy) return;
		foreach (var r in chromeRows) r.Item.Selected = r.Selected;
		var pick = new List<ChromePackItem>();
		foreach (var it in chromeAll) {
			if (!it.Selected) continue;
			if (install && it.State == FeatureInstallState.Installed) continue;
			if (!install && it.State != FeatureInstallState.Installed) continue;
			pick.Add(it);
		}
		if (pick.Count == 0) {
			MessageBox.Show(this, Loc.T(install ? "inst.install.none" : "inst.win.none.del"), Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		cts = new CancellationTokenSource();
		setbusy(true);
		setprogress(0);
		var ok = 0;
		var fail = 0;
		try {
			var mode = proxymode();
			var addr = (eproxyaddr.Text ?? "").Trim();
			await Task.Run(() => ChromePack.Apply(pick, install, mode, addr, appendlog, setprogress, cts.Token));
			ok = pick.Count;
		}
		catch (OperationCanceledException) {
			appendlog(Loc.T("inst.chrome.cancel"));
		}
		catch (Exception ex) {
			fail++;
			appendlog(ex.Message);
			CaptureLog.Ex("ChromePack", ex);
		}
		try { await fetchchrome(); }
		catch (Exception ex) { appendlog(ex.Message); }
		setbusy(false);
		var summary = string.Format(Loc.T(install ? "inst.install.done" : "inst.delete.done"), ok, fail);
		setstatus(summary);
		appendlog(summary);
		try { cts?.Dispose(); } catch { }
		cts = null;
	}

	sealed class ChromeRow : INotifyPropertyChanged {
		public ChromePackItem Item { get; }
		bool selected;

		public ChromeRow(ChromePackItem item) {
			Item = item;
			selected = item.Selected;
		}

		public bool Selected {
			get => selected;
			set {
				if (selected == value) return;
				selected = value;
				Item.Selected = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
			}
		}

		public string Title => Item.Title;
		public string KindText => Item.KindText;
		public string Detail => Item.Detail;
		public string SizeText => Item.SizeText;
		public string StateText => Item.StateText;
		public Brush StateBg => statebg(Item.State);
		public Brush StateFg => statefg(Item.State);

		public void Notify() {
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateText)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateBg)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateFg)));
		}

		public event PropertyChangedEventHandler PropertyChanged;
	}
}
