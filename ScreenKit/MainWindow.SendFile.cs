using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace ScreenKit;

sealed class SfTextRow {
	public string Line { get; set; } = "";
	public string Full { get; set; } = "";
}

/// <summary>MainWindow：文件同步 Tab（拖到手机 / 接收手机文件 / 文本）。</summary>
public partial class MainWindow {
	readonly ObservableCollection<SfTextRow> sfMsgs = new();
	System.Windows.Threading.DispatcherTimer sfPhoneTick;
	bool sfInboxHooked;

	void initsendfiletab() {
		lstsfmsgs.ItemsSource = sfMsgs;
		bsfpaste.Click += (_, _) => sfpaste();
		bsfopen.Click += (_, _) => sfopenexplorer();
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
		sfPhoneTick = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromSeconds(1),
		};
		sfPhoneTick.Tick += (_, _) => syncsfstatus();
		sfPhoneTick.Start();
		syncsfstatus();
		applysflang();
	}

	bool issftab() => tabsf != null && ReferenceEquals(maintabs.SelectedItem, tabsf);

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
		foreach (var m in sendFile.Text.SnapshotInbox())
			if (m != null) sfaddrow(m.Text);
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
		full = full ?? "";
		sfMsgs.Insert(0, new SfTextRow { Line = sfoneline(full), Full = full });
		while (sfMsgs.Count > 200)
			sfMsgs.RemoveAt(sfMsgs.Count - 1);
	}

	static string sfoneline(string s) {
		s = (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
		var i = s.IndexOf('\n');
		if (i >= 0) s = s.Substring(0, i) + "…";
		return s;
	}

	void onsfcopy(object sender, RoutedEventArgs e) {
		var row = (sender as FrameworkElement)?.Tag as SfTextRow
			?? (sender as Button)?.DataContext as SfTextRow;
		if (row == null) return;
		try {
			Clipboard.SetText(row.Full ?? "");
		}
		catch (Exception ex) {
			MessageBox.Show(this, Loc.T("st.copy_fail", ex.Message), Loc.T("sendfile.text.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
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
		if (lbsfdrop != null)
			lbsfdrop.Text = sendFile != null && sendFile.PhoneOnline
				? Loc.T("sf.drop.on")
				: Loc.T("sf.drop.off");
	}

	void applysflang() {
		try {
			tabsf.Header = Loc.T("tab.sendfile");
			lbsfbrand.Text = Loc.T("sf.tab.brand");
			lbsfhint.Text = Loc.T("sf.tab.hint");
			bsfpaste.Content = Loc.T("sf.tab.paste");
			bsfopen.Content = Loc.T("sf.tab.open");
			lbsftexthint.Text = Loc.T("sf.text.hint");
			bsfsend.Content = Loc.T("sendfile.text.send");
			syncsfstatus();
		}
		catch { }
	}
}
