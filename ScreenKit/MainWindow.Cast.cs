using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ScreenKit;

sealed class CastPcRow {
	public string Name;
	public string Ip;
	public int Port;

	public string HostText {
		get {
			var def = 1224;
			try { def = CastHost.TcpPort; } catch { }
			if (Port > 0 && Port != def) return $"{Ip}:{Port}";
			return Ip ?? "";
		}
	}

	public override string ToString() {
		var host = HostText;
		if (string.IsNullOrWhiteSpace(Name) || Name == Ip) return host;
		return $"{Name}  {host}";
	}
}

public partial class MainWindow {
	DispatcherTimer casttimer;
	bool castfill;
	bool castpicking;
	bool castlive;
	int castw, casth;
	string castto = "";

	void initcasttab() {
		fillcastq();
		ccastaudio.IsChecked = CastHost.Audio;
		loadlastcast();
		ecastq.SelectionChanged += (_, _) => {
			if (castfill) return;
			savecastopt();
			if (CastHost.Send != null && CastHost.Send.Running)
				CastHost.Send.ApplyQ(CastHost.CurQ());
		};
		ccastaudio.Checked += (_, _) => savecastopt();
		ccastaudio.Unchecked += (_, _) => savecastopt();
		lcastpeers.SelectionChanged += (_, _) => oncastpeer();
		bcastsaved.Click += (_, _) => showcastsaved();
		bcastscan.Click += async (_, _) => await scancast();
		bcaststart.Click += async (_, _) => await startcast();
		bcaststop.Click += (_, _) => stopcast();
		casttimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
		casttimer.Tick += (_, _) => tickcast();
		casttimer.Start();
		syncastbtns();
	}

	void applycastlang() {
		tabcast.Header = Loc.T("tab.cast");
		lbcastto.Text = Loc.T("cast.tab.to");
		lbcastq.Text = Loc.T("cast.quality");
		lbcasthint.Text = Loc.T("cast.tab.hint");
		ccastaudio.Content = Loc.T("cast.tab.audio");
		bcastsaved.Content = Loc.T("cast.tab.saved");
		bcastscan.Content = Loc.T("cast.tab.scan");
		bcaststart.Content = Loc.T("cast.tab.start");
		bcaststop.Content = Loc.T("cast.tab.stop");
		ecastip.ToolTip = Loc.T("cast.tab.iph");
		var keep = ecastq.SelectedItem as string;
		fillcastq();
		if (!string.IsNullOrEmpty(keep)) ecastq.SelectedItem = keep;
		if (ecastq.SelectedIndex < 0) ecastq.SelectedIndex = 1;
		if (!castlive && string.IsNullOrWhiteSpace(ecastip.Text))
			lbcasttarget.Text = Loc.T("cast.tab.notarget");
		if (!castlive && (lbcaststat.Text == "准备投屏" || string.IsNullOrEmpty(lbcaststat.Text)))
			lbcaststat.Text = Loc.T("cast.tab.ready");
	}

	void fillcastq() {
		castfill = true;
		ecastq.Items.Clear();
		foreach (var q in CastQuality.Presets) ecastq.Items.Add(q.Name);
		ecastq.SelectedItem = CastHost.QualityName;
		if (ecastq.SelectedIndex < 0) ecastq.SelectedIndex = 1;
		castfill = false;
	}

	void savecastopt() {
		if (opt == null || castfill) return;
		opt.CastQuality = ecastq.SelectedItem as string ?? CastQuality.Presets[1].Name;
		opt.CastAudio = ccastaudio.IsChecked == true;
		try { AppConfig.Save(opt); } catch { }
	}

	void loadlastcast() {
		var saved = loadcastsaved();
		if (saved.Count == 0) {
			lbcaststat.Text = Loc.T("cast.tab.ready");
			lbcasttarget.Text = Loc.T("cast.tab.notarget");
			return;
		}
		fillcastip(saved[0]);
		lbcaststat.Text = Loc.T("cast.tab.ready");
	}

	List<CastPcRow> loadcastsaved() {
		var list = new List<CastPcRow>();
		var raw = opt?.CastRecent ?? "";
		foreach (var part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) {
			var f = part.Split('|');
			if (f.Length < 2) continue;
			var ip = f[1].Trim();
			if (ip.Length == 0) continue;
			var port = CastHost.TcpPort;
			if (f.Length >= 3 && int.TryParse(f[2], out var p) && p > 0) port = p;
			list.Add(new CastPcRow { Name = f[0].Trim(), Ip = ip, Port = port });
		}
		return list;
	}

	void remembercast(string name, string ip, int port) {
		if (opt == null || string.IsNullOrWhiteSpace(ip)) return;
		var list = loadcastsaved();
		list.RemoveAll(x => string.Equals(x.Ip, ip, StringComparison.OrdinalIgnoreCase) && x.Port == port);
		var n = (name ?? "").Replace("|", " ").Replace(";", " ").Trim();
		if (n.Length == 0) n = ip;
		list.Insert(0, new CastPcRow { Name = n, Ip = ip.Trim(), Port = port });
		if (list.Count > 12) list.RemoveRange(12, list.Count - 12);
		var parts = new List<string>();
		foreach (var x in list) parts.Add($"{x.Name}|{x.Ip}|{x.Port}");
		opt.CastRecent = string.Join(";", parts);
		try { AppConfig.Save(opt); } catch { }
	}

	void fillcastip(CastPcRow row) {
		if (row == null) return;
		ecastip.Text = row.HostText;
		var who = string.IsNullOrWhiteSpace(row.Name) || row.Name == row.Ip ? row.HostText : $"{row.Name} · {row.HostText}";
		lbcasttarget.Text = Loc.T("cast.tab.target", who);
	}

	void oncastpeer() {
		if (lcastpeers.SelectedItem is not CastPcRow row) return;
		fillcastip(row);
		if (!castlive) lbcaststat.Text = Loc.T("cast.tab.ready");
	}

	void showrows(List<CastPcRow> rows) {
		lcastpeers.Items.Clear();
		foreach (var r in rows) lcastpeers.Items.Add(r);
		lcastpeers.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
	}

	void showcastsaved() {
		var saved = loadcastsaved();
		if (saved.Count == 0) {
			lbcaststat.Text = Loc.T("cast.tab.nosaved");
			lcastpeers.Visibility = Visibility.Collapsed;
			return;
		}
		showrows(saved);
		lbcaststat.Text = Loc.T("cast.tab.savedn", saved.Count);
	}

	async Task scancast() {
		if (CastHost.Disc == null) {
			try { if (!CastHost.Started) CastHost.Start(); } catch { }
		}
		lbcaststat.Text = Loc.T("cast.tab.scanning");
		bcastscan.IsEnabled = false;
		try {
			CastHost.Disc?.Scan();
			await Task.Delay(800);
			var found = CastHost.Disc?.Snapshot() ?? new List<CastPeer>();
			var rows = new List<CastPcRow>();
			foreach (var p in found) {
				if (p == null || string.IsNullOrWhiteSpace(p.Ip)) continue;
				if (p.Role == "send") continue;
				rows.Add(new CastPcRow {
					Name = p.Name,
					Ip = p.Ip,
					Port = p.Tcp > 0 ? p.Tcp : CastHost.TcpPort,
				});
			}
			showrows(rows);
			if (rows.Count == 0) {
				lbcaststat.Text = Loc.T("cast.tab.none");
				return;
			}
			CastPcRow hit = null;
			if (trycasttarget(out var ip, out _, out _)) {
				foreach (var r in rows)
					if (string.Equals(r.Ip, ip, StringComparison.OrdinalIgnoreCase)) { hit = r; break; }
			}
			if (rows.Count == 1) hit = rows[0];
			if (hit != null) lcastpeers.SelectedItem = hit;
			else lbcaststat.Text = Loc.T("cast.tab.found", rows.Count);
		}
		finally { bcastscan.IsEnabled = true; }
	}

	bool trycasttarget(out string ip, out int port, out string name) {
		ip = "";
		port = CastHost.TcpPort;
		name = "";
		var t = (ecastip.Text ?? "").Trim();
		if (t.Length == 0) return false;
		var sp = t.Split(':');
		if (sp.Length == 2 && int.TryParse(sp[1], out var p) && p > 0) {
			ip = sp[0].Trim();
			port = p;
		}
		else ip = t;
		if (ip.Length == 0) return false;
		name = ip;
		if (lcastpeers.SelectedItem is CastPcRow row &&
			string.Equals(row.Ip, ip, StringComparison.OrdinalIgnoreCase))
			name = string.IsNullOrWhiteSpace(row.Name) ? ip : row.Name;
		else {
			foreach (var s in loadcastsaved())
				if (string.Equals(s.Ip, ip, StringComparison.OrdinalIgnoreCase) && s.Port == port) {
					if (!string.IsNullOrWhiteSpace(s.Name)) name = s.Name;
					break;
				}
		}
		return true;
	}

	async Task startcast() {
		if (CastHost.Send != null && CastHost.Send.Running) return;
		if (capturing || activeRecordHud != null) {
			lbcaststat.Text = Loc.T("cast.tab.busy");
			return;
		}
		if (!trycasttarget(out var ip, out var port, out var name)) {
			lbcaststat.Text = Loc.T("cast.tab.needip");
			return;
		}
		if (!FeaturePrompt.EnsureFfmpeg(this)) return;
		System.Drawing.Rectangle? rect = null;
		var was = IsVisible && WindowState != WindowState.Minimized;
		try {
			capturing = true;
			castpicking = true;
			syncastbtns();
			lbcaststat.Text = Loc.T("cast.tab.pickstat");
			try { Hide(); } catch { WindowState = WindowState.Minimized; }
			await Task.Delay(40);
			rect = RecordRegionPicker.Pick(Loc.T("cast.tab.pick"));
		}
		catch (Exception ex) {
			lbcaststat.Text = ex.Message;
			rect = null;
		}
		finally {
			capturing = false;
			castpicking = false;
			if (was) {
				try {
					Show();
					if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
					Activate();
				}
				catch { }
			}
			syncastbtns();
		}
		if (rect == null || rect.Value.Width < 16 || rect.Value.Height < 16) {
			lbcaststat.Text = Loc.T("cast.tab.cancel");
			return;
		}
		try {
			savecastopt();
			if (CastHost.Send == null) {
				try { CastHost.Start(); } catch (Exception ex) {
					lbcaststat.Text = ex.Message;
					return;
				}
			}
			if (CastHost.Send == null) {
				lbcaststat.Text = Loc.T("cast.tab.fail");
				return;
			}
			CastHost.Send.Stop();
			CastHost.Send.Start(ip, port, CastHost.CurQ(), ccastaudio.IsChecked == true, rect.Value);
			remembercast(name, ip, port);
			castlive = true;
			castw = rect.Value.Width;
			casth = rect.Value.Height;
			castto = port > 0 && port != CastHost.TcpPort ? $"{ip}:{port}" : ip;
			lbcaststat.Text = Loc.T("cast.tab.sending", castw, casth, castto);
			lbcasttarget.Text = Loc.T("cast.tab.target", $"{name} · {castto}");
		}
		catch (Exception ex) {
			castlive = false;
			try { CastHost.Send?.Stop(); } catch { }
			lbcaststat.Text = $"{Loc.T("cast.log.connfail")}: {ex.Message}";
			try {
				MessageBox.Show(this, ex.Message, Loc.T("cast.tab.fail"),
					MessageBoxButton.OK, MessageBoxImage.Warning);
			}
			catch { }
		}
		syncastbtns();
	}

	void stopcast() {
		castlive = false;
		try { CastHost.Send?.Stop(); } catch { }
		lbcaststat.Text = Loc.T("cast.tab.stopped");
		syncastbtns();
	}

	void tickcast() {
		var on = CastHost.Send != null && CastHost.Send.Running;
		if (castlive && !on) {
			castlive = false;
			lbcaststat.Text = Loc.T("cast.tab.stopped");
		}
		else if (castlive && on && castw > 0)
			lbcaststat.Text = Loc.T("cast.tab.sending", castw, casth, castto);
		syncastbtns();
	}

	void syncastbtns() {
		var on = CastHost.Send != null && CastHost.Send.Running;
		bcaststart.IsEnabled = !on && !castpicking;
		bcaststop.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
	}
}
