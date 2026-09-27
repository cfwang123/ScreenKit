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
	bool castsuppress;
	bool castpicking;
	bool castlive;
	int castw, casth;
	string castto = "";
	readonly List<CastPcRow> castscan = new();
	CastHud casthud;
	bool castclosing;

	void initcasttab() {
		castfill = true;
		ccastaudio.IsChecked = CastHost.Audio;
		ecastmaxw.Text = (opt?.CastMaxW > 0 ? opt.CastMaxW : 1000).ToString();
		ecastmaxh.Text = (opt?.CastMaxH > 0 ? opt.CastMaxH : 1000).ToString();
		castfill = false;
		loadlastcast();
		refreshcastlist();
		ccastaudio.Checked += (_, _) => savecastopt();
		ccastaudio.Unchecked += (_, _) => savecastopt();
		ecastmaxw.LostFocus += (_, _) => oncastlimit();
		ecastmaxh.LostFocus += (_, _) => oncastlimit();
		lcastpeers.SelectionChanged += (_, _) => oncastpeer();
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
		lbcastbrand.Text = Loc.T("tab.cast");
		lbcastsaved.Text = Loc.T("cast.tab.saved");
		lbcastempty.Text = Loc.T("cast.tab.nosaved");
		lbcastto.Text = Loc.T("cast.tab.to");
		lbcastlimit.Text = Loc.T("cast.tab.limit");
		lbcastlimithint.Text = Loc.T("cast.tab.limit.hint");
		lbcastmaxw.Text = Loc.T("cast.tab.maxw");
		lbcastmaxh.Text = Loc.T("cast.tab.maxh");
		lbcasthint.Text = Loc.T("cast.tab.hint");
		ccastaudio.Content = Loc.T("cast.tab.audio");
		bcastscan.Content = Loc.T("cast.tab.scan");
		bcaststart.Content = Loc.T("cast.tab.start");
		bcaststop.Content = Loc.T("cast.tab.stop");
		ecastip.ToolTip = Loc.T("cast.tab.iph");
		if (!castlive && string.IsNullOrWhiteSpace(ecastip.Text))
			lbcasttarget.Text = Loc.T("cast.tab.notarget");
		if (!castlive && (lbcaststat.Text == "准备投屏" || string.IsNullOrEmpty(lbcaststat.Text)))
			lbcaststat.Text = Loc.T("cast.tab.ready");
	}

	void oncastlimit() {
		if (castfill) return;
		castfill = true;
		ecastmaxw.Text = readcastlimit(ecastmaxw.Text).ToString();
		ecastmaxh.Text = readcastlimit(ecastmaxh.Text).ToString();
		castfill = false;
		savecastopt();
		if (CastHost.Send != null && CastHost.Send.Running)
			CastHost.Send.ApplyQ(castboxq());
	}

	static int readcastlimit(string text) {
		if (!int.TryParse((text ?? "").Trim(), out var n) || n <= 0) n = 1000;
		return Compat.Clamp(n, 160, 8192);
	}

	CastQuality castboxq() {
		var baseq = CastQuality.Presets[1];
		return new CastQuality {
			Name = "box",
			Width = readcastlimit(ecastmaxw.Text),
			Height = readcastlimit(ecastmaxh.Text),
			Fps = baseq.Fps,
			Bitrate = baseq.Bitrate,
			Crf = baseq.Crf,
			BoxFit = true,
		};
	}

	void savecastopt() {
		if (opt == null || castfill) return;
		opt.CastAudio = ccastaudio.IsChecked == true;
		opt.CastMaxW = readcastlimit(ecastmaxw.Text);
		opt.CastMaxH = readcastlimit(ecastmaxh.Text);
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
		refreshcastlist();
	}

	void fillcastip(CastPcRow row) {
		if (row == null) return;
		ecastip.Text = row.HostText;
		var who = string.IsNullOrWhiteSpace(row.Name) || row.Name == row.Ip ? row.HostText : $"{row.Name} · {row.HostText}";
		lbcasttarget.Text = Loc.T("cast.tab.target", who);
	}

	void oncastpeer() {
		if (castsuppress) return;
		if (lcastpeers.SelectedItem is not CastPcRow row) return;
		fillcastip(row);
		if (!castlive) lbcaststat.Text = Loc.T("cast.tab.ready");
	}

	void refreshcastlist() {
		castsuppress = true;
		var rows = loadcastsaved();
		foreach (var s in castscan) {
			if (s == null || string.IsNullOrWhiteSpace(s.Ip)) continue;
			var dup = false;
			foreach (var r in rows)
				if (string.Equals(r.Ip, s.Ip, StringComparison.OrdinalIgnoreCase) && r.Port == s.Port) {
					dup = true;
					break;
				}
			if (!dup) rows.Add(s);
		}
		var keep = lcastpeers.SelectedItem as CastPcRow;
		lcastpeers.Items.Clear();
		foreach (var r in rows) lcastpeers.Items.Add(r);
		lbcastempty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		if (keep != null) {
			foreach (CastPcRow r in lcastpeers.Items)
				if (string.Equals(r.Ip, keep.Ip, StringComparison.OrdinalIgnoreCase) && r.Port == keep.Port) {
					lcastpeers.SelectedItem = r;
					break;
				}
		}
		castsuppress = false;
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
			castscan.Clear();
			foreach (var p in found) {
				if (p == null || string.IsNullOrWhiteSpace(p.Ip)) continue;
				if (p.Role == "send") continue;
				castscan.Add(new CastPcRow {
					Name = p.Name,
					Ip = p.Ip,
					Port = p.Tcp > 0 ? p.Tcp : CastHost.TcpPort,
				});
			}
			refreshcastlist();
			if (castscan.Count == 0) {
				lbcaststat.Text = Loc.T("cast.tab.none");
				return;
			}
			CastPcRow hit = null;
			if (trycasttarget(out var ip, out _, out _)) {
				foreach (var r in castscan)
					if (string.Equals(r.Ip, ip, StringComparison.OrdinalIgnoreCase)) { hit = r; break; }
			}
			if (castscan.Count == 1) hit = castscan[0];
			if (hit != null) {
				foreach (CastPcRow r in lcastpeers.Items)
					if (string.Equals(r.Ip, hit.Ip, StringComparison.OrdinalIgnoreCase) && r.Port == hit.Port) {
						lcastpeers.SelectedItem = r;
						break;
					}
			}
			else lbcaststat.Text = Loc.T("cast.tab.found", castscan.Count);
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
			CastHost.Send.Start(ip, port, castboxq(), ccastaudio.IsChecked == true, rect.Value);
			remembercast(name, ip, port);
			castlive = true;
			castw = rect.Value.Width;
			casth = rect.Value.Height;
			castto = port > 0 && port != CastHost.TcpPort ? $"{ip}:{port}" : ip;
			lbcaststat.Text = Loc.T("cast.tab.sending", castw, casth, castto);
			lbcasttarget.Text = Loc.T("cast.tab.target", $"{name} · {castto}");
			showcasthud(rect.Value, castto);
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
		if (castclosing) return;
		castclosing = true;
		try {
			castlive = false;
			closecasthud();
			try { CastHost.Send?.Stop(); } catch { }
			lbcaststat.Text = Loc.T("cast.tab.stopped");
			syncastbtns();
		}
		finally { castclosing = false; }
	}

	void showcasthud(System.Drawing.Rectangle r, string to) {
		closecasthud();
		var hud = new CastHud(r, to);
		hud.StopRequested += () => Dispatcher.BeginInvoke(new Action(stopcast));
		hud.RegionChanged += nr => {
			try { CastHost.Send?.SetRegion(nr); } catch { }
			castw = nr.Width;
			casth = nr.Height;
		};
		casthud = hud;
		hud.Show();
	}

	void closecasthud() {
		var h = casthud;
		casthud = null;
		if (h == null) return;
		try { h.Close(); } catch { }
	}

	void hidecasthud() {
		try { casthud?.Hide(); } catch { }
	}

	void showcasthudagain() {
		try {
			if (casthud == null) return;
			casthud.Show();
			casthud.Retop();
		}
		catch { }
	}

	void tickcast() {
		var on = CastHost.Send != null && CastHost.Send.Running;
		if (castlive && !on) stopcast();
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
