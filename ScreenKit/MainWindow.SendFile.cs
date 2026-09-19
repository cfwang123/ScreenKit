using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace ScreenKit;

sealed class SfLogRow {
	public string Line { get; set; } = "";
}

sealed class SfJobRow : INotifyPropertyChanged {
	public long Id;
	string title = "", sub = "", pcttext = "";
	int percent;
	Visibility barvis = Visibility.Collapsed;
	public string Title { get => title; set => set(ref title, value, nameof(Title)); }
	public string Sub { get => sub; set => set(ref sub, value, nameof(Sub)); }
	public string PctText { get => pcttext; set => set(ref pcttext, value, nameof(PctText)); }
	public int Percent { get => percent; set => set(ref percent, value, nameof(Percent)); }
	public Visibility BarVis { get => barvis; set => set(ref barvis, value, nameof(BarVis)); }
	public event PropertyChangedEventHandler PropertyChanged;
	void set<T>(ref T field, T value, string name) {
		if (Equals(field, value)) return;
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}

	public void Apply(SfJob j) {
		if (j == null) return;
		Id = j.Id;
		Title = (j.ToPhone ? Loc.T("sf.dir.out") : Loc.T("sf.dir.in")) + "  " + (j.Name ?? "");
		var pct = j.Size > 0 ? (int)(j.Done * 100 / j.Size) : (j.State == SendFileJobs.Done ? 100 : 0);
		if (pct < 0) pct = 0;
		if (pct > 100) pct = 100;
		Percent = pct;
		BarVis = j.State == SendFileJobs.Done || j.State == SendFileJobs.Fail
			? Visibility.Collapsed : Visibility.Visible;
		PctText = j.State == SendFileJobs.Wait ? Loc.T("sf.job.wait")
			: j.State == SendFileJobs.Run ? pct + "%"
			: j.State == SendFileJobs.Fail ? Loc.T("sf.job.fail")
			: Loc.T("sf.job.done");
		Sub = j.State == SendFileJobs.Fail
			? (j.Err ?? "")
			: SizeText(j.Done) + " / " + SizeText(j.Size);
	}

	public static string SizeText(long n) {
		if (n < 1024) return $"{n} B";
		if (n < 1024 * 1024) return $"{n / 1024.0:0.#} KB";
		if (n < 1024L * 1024 * 1024) return $"{n / (1024.0 * 1024):0.#} MB";
		return $"{n / (1024.0 * 1024 * 1024):0.#} GB";
	}
}

/// <summary>MainWindow：文件同步 Tab（拖到手机 / 接收手机文件 / 文本）。</summary>
public partial class MainWindow {
	readonly ObservableCollection<SfJobRow> sfJobs = new();
	readonly ObservableCollection<SfLogRow> sfLogs = new();
	readonly HashSet<long> sfLogged = new();
	System.Windows.Threading.DispatcherTimer sfPhoneTick;
	bool sfInboxHooked;
	bool sfJobsHooked;

	void initsendfiletab() {
		lvsfjobs.ItemsSource = sfJobs;
		lstsflog.ItemsSource = sfLogs;
		bsfpaste.Click += (_, _) => sfpaste();
		bsfopen.Click += (_, _) => sfopenexplorer();
		bsfapk.Click += (_, _) => showapkinstall();
		psffiles.Drop += onsfdrop;
		psffiles.DragOver += onsfdover;
		psftab.Drop += onsfdrop;
		psftab.DragOver += onsfdover;
		psftab.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) {
				sfpaste();
				e.Handled = true;
			}
		};
		bsfsend.Click += (_, _) => sfsend();
		esfsend.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) {
				sfsend();
				e.Handled = true;
			}
		};
		maintabs.SelectionChanged += (_, _) => {
			if (issftab()) syncsfstatus();
		};
		hookstext();
		hooksfjobs();
		sfPhoneTick = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromSeconds(1),
		};
		sfPhoneTick.Tick += (_, _) => syncsfstatus();
		sfPhoneTick.Start();
		syncsfstatus();
		applysflang();
	}

	bool issftab() => tabsf != null && ReferenceEquals(maintabs.SelectedItem, tabsf);

	void showapkinstall() {
		var w = new ApkInstallWindow(sendFile, opt) { Owner = this };
		w.ShowDialog();
	}

	void hooksfjobs() {
		if (sfJobsHooked || sendFile == null) return;
		sfJobsHooked = true;
		sendFile.Jobs.Changed += () => {
			try { Dispatcher.BeginInvoke(new Action(syncsfjobs)); } catch { }
		};
		syncsfjobs();
	}

	void syncsfjobs() {
		if (sendFile == null) return;
		var snap = sendFile.Jobs.Snapshot();
		var live = snap.Where(j => j.State == SendFileJobs.Wait || j.State == SendFileJobs.Run).ToList();
		if (lvsfjobs != null) {
			for (var i = sfJobs.Count - 1; i >= 0; i--) {
				var id = sfJobs[i].Id;
				if (!live.Any(j => j.Id == id)) sfJobs.RemoveAt(i);
			}
			foreach (var j in live) {
				var row = sfJobs.FirstOrDefault(r => r.Id == j.Id);
				if (row == null) {
					row = new SfJobRow();
					sfJobs.Add(row);
				}
				row.Apply(j);
			}
		}
		foreach (var j in snap) {
			if (j.State != SendFileJobs.Done && j.State != SendFileJobs.Fail) continue;
			if (!sfLogged.Add(j.Id)) continue;
			sfLogs.Insert(0, new SfLogRow { Line = sflogline(j) });
			while (sfLogs.Count > 200)
				sfLogs.RemoveAt(sfLogs.Count - 1);
		}
		if (lbsfdrop != null)
			lbsfdrop.Visibility = sfJobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	static string sflogline(SfJob j) {
		var t = "—";
		try {
			if (j.Unix > 0)
				t = DateTimeOffset.FromUnixTimeSeconds(j.Unix).ToLocalTime().ToString("HH:mm:ss");
		}
		catch { t = DateTime.Now.ToString("HH:mm:ss"); }
		var dir = j.ToPhone ? Loc.T("sf.dir.out") : Loc.T("sf.dir.in");
		var st = j.State == SendFileJobs.Fail
			? Loc.T("sf.job.fail") + (string.IsNullOrWhiteSpace(j.Err) ? "" : " " + j.Err)
			: Loc.T("sf.job.done");
		return $"{t}  {dir}  {j.Name}  {st}  {SfJobRow.SizeText(j.Size)}";
	}

	void hookstext() {
		if (sfInboxHooked || sendFile == null) return;
		sfInboxHooked = true;
		sendFile.Text.InboxArrived += msg => {
			if (msg == null) return;
			try {
				Dispatcher.BeginInvoke(new Action(() => sfaddrow(msg.Text)));
			}
			catch { }
		};
		var last = sendFile.Text.SnapshotInbox().LastOrDefault();
		if (last != null) sfaddrow(last.Text);
	}

	void stopsfwatch() {
		try { sfPhoneTick?.Stop(); } catch { }
		sfPhoneTick = null;
	}

	void sfopenexplorer() {
		try {
			SendFilePaths.EnsureRoot();
			var full = SendFilePaths.Root();
			Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{full}\"", UseShellExecute = true });
		}
		catch (Exception ex) {
			setstatus(ex.Message);
		}
	}

	void onsfdover(object sender, DragEventArgs e) {
		e.Effects = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
			? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void onsfdrop(object sender, DragEventArgs e) {
		e.Handled = true;
		if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (files == null || files.Length == 0) return;
		sfimportpaths(files);
	}

	void sfpaste() {
		try {
			if (Clipboard.ContainsFileDropList()) {
				var list = Clipboard.GetFileDropList();
				if (list != null && list.Count > 0) {
					var paths = new string[list.Count];
					list.CopyTo(paths, 0);
					sfimportpaths(paths);
					return;
				}
			}
		}
		catch { }
		try {
			if (Clipboard.ContainsImage()) {
				var bmp = ImageUtil.Fromclipboard();
				if (bmp != null) {
					sfsaveimage(bmp);
					return;
				}
			}
		}
		catch { }
		try {
			if (Clipboard.ContainsText()) {
				var t = Clipboard.GetText() ?? "";
				if (!string.IsNullOrWhiteSpace(t)) {
					sfpushtext(t.TrimEnd());
					return;
				}
			}
		}
		catch { }
		setstatus(Loc.T("sf.paste.empty"));
	}

	void sfimportpaths(IEnumerable<string> paths) {
		var id = sendFile?.OnlineDeviceId;
		if (string.IsNullOrEmpty(id)) {
			setstatus(Loc.T("sf.nophone"));
			return;
		}
		var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
		if (list == null || list.Count == 0) return;
		setstatus(Loc.T("sf.sending"));
		var svc = sendFile;
		Task.Run(() => {
			var n = 0;
			string err = null;
			try { n = svc.PushFilesToPhone(id, list); }
			catch (Exception ex) { err = ex.Message; }
			try {
				Dispatcher.BeginInvoke(new Action(() => {
					if (n > 0) setstatus(Loc.T("sf.to_phone", n.ToString()));
					else setstatus(err ?? Loc.T("sf.nophone"));
					syncsfstatus();
				}));
			}
			catch { }
		});
	}

	void sfsaveimage(BitmapSource bmp) {
		var id = sendFile?.OnlineDeviceId;
		if (string.IsNullOrEmpty(id)) {
			setstatus(Loc.T("sf.nophone"));
			return;
		}
		var name = "clip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png";
		var destRel = SendFileOps.UniqueRel(".to_phone/" + name, dir: false);
		if (!SendFilePaths.TryResolve(destRel, out var full, out var err)) {
			setstatus(err ?? Loc.T("st.sendfile_fail", "path"));
			return;
		}
		try {
			var dir = Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			ImageUtil.Savefile(bmp, full);
			var n = sendFile.PushStoreToPhone(id, destRel);
			setstatus(n > 0 ? Loc.T("sf.to_phone", n.ToString()) : Loc.T("sf.nophone"));
		}
		catch (Exception ex) {
			setstatus(ex.Message);
		}
	}

	void sfsend() {
		var text = (esfsend.Text ?? "").Trim();
		if (text.Length == 0) return;
		sfpushtext(text);
		esfsend.Clear();
	}

	void sfpushtext(string text) {
		text = text ?? "";
		if (sendFile == null) {
			sfaddrow(text);
			setstatus(Loc.T("st.sendfile_fail", "not started"));
			return;
		}
		var id = sendFile.OnlineDeviceId;
		if (string.IsNullOrEmpty(id))
			id = sendFile.LastDeviceId;
		if (string.IsNullOrEmpty(id))
			id = sendFile.Auth.FirstDeviceId();
		if (string.IsNullOrEmpty(id)) {
			sfaddrow(text);
			MessageBox.Show(this, Loc.T("sendfile.text.nophone"), Loc.T("sendfile.text.title"),
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		sendFile.Text.PushOut(id, text);
		sfaddrow(text);
	}

	void sfaddrow(string full) {
		if (esfmsg == null) return;
		esfmsg.Text = full ?? "";
	}

	void syncsfstatus() {
		if (lbsfstatus == null) return;
		if (opt.SendFileEnabled && sendFile != null && sendFile.IsRunning) {
			var tcp = opt.SendFilePort <= 0 ? 17532 : opt.SendFilePort;
			var udp = opt.SendFileUdpPort <= 0 ? 17531 : opt.SendFileUdpPort;
			var phone = sendFile.PhoneOnline
				? Loc.T("sf.tab.phone.on")
				: Loc.T("sf.tab.phone.off");
			lbsfstatus.Text = Loc.T("sf.tab.on", tcp.ToString(), udp.ToString()) + "  ·  " + phone;
		}
		else
			lbsfstatus.Text = Loc.T("sf.tab.off");
		if (lbsfdrop != null) {
			lbsfdrop.Text = sendFile != null && sendFile.PhoneOnline
				? Loc.T("sf.drop.on")
				: Loc.T("sf.drop.off");
			if (sfJobs.Count > 0) lbsfdrop.Visibility = Visibility.Collapsed;
		}
	}

	void applysflang() {
		try {
			tabsf.Header = Loc.T("tab.sendfile");
			lbsfbrand.Text = Loc.T("sf.tab.brand");
			lbsfhint.Text = Loc.T("sf.tab.hint");
			bsfpaste.Content = Loc.T("sf.tab.paste");
			bsfopen.Content = Loc.T("sf.tab.open");
			if (bsfapk != null) bsfapk.Content = Loc.T("sf.tab.apk");
			lbsftexthint.Text = Loc.T("sf.text.hint");
			bsfsend.Content = Loc.T("sendfile.text.send");
			if (lbsflog != null) lbsflog.Text = Loc.T("sf.log");
			syncsfstatus();
		}
		catch { }
	}
}
