using System.Windows.Threading;

namespace ScreenKit;

public partial class CastWindow : Window {
	readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

	public CastWindow() {
		InitializeComponent();
		if (CastHost.WinIcon != null) Icon = CastHost.WinIcon;
		WindowEsc.Attach(this);
		foreach (var q in CastQuality.Presets) eq.Items.Add(q.Name);
		eq.SelectedItem = CastHost.QualityName;
		if (eq.SelectedIndex < 0) eq.SelectedIndex = 1;
		caudio.IsChecked = CastHost.Audio;
		applylang();
		Closing += (_, _) => {
			timer.Stop();
			save();
		};
		timer.Tick += (_, _) => tick();
		timer.Start();
		tick();
		eq.SelectionChanged += eq_SelectionChanged;
	}

	void applylang() {
		Title = Loc.T("cast.title");
		lbq.Text = Loc.T("cast.quality");
		caudio.Content = Loc.T("cast.audio");
		bscan.Content = Loc.T("cast.scan");
		bsend.Content = Loc.T("cast.send");
		bstop.Content = Loc.T("cast.disconnect");
		lbipman.Text = Loc.T("cast.ip");
		bconnect.Content = Loc.T("cast.connect");
	}

	void eq_SelectionChanged(object sender, SelectionChangedEventArgs e) {
		if (!IsLoaded) return;
		save();
		CastHost.ApplyQuality();
		AppendLog($"{Loc.T("cast.quality")} {CastHost.QualityName}");
	}

	public void AppendLog(string s) {
		var line = $"{DateTime.Now:HH:mm:ss} {s}\n";
		elog.AppendText(line);
		if (elog.Text.Length > 20000) elog.Text = elog.Text.Substring(elog.Text.Length - 12000);
		elog.ScrollToEnd();
	}

	void tick() {
		lbip.Text = $"{Loc.T("cast.local")} {CastHost.Name}  {CastNetUtil.LocalIps()}  TCP {CastProto.TCP_PORT}";
		if (CastHost.Send != null && CastHost.Send.Running) lbstat.Text = Loc.T("cast.stat.send");
		else if (CastHost.Recv != null && CastHost.Recv.Busy) lbstat.Text = Loc.T("cast.stat.recv");
		else if (CastHost.Recv != null && CastHost.Recv.Running)
			lbstat.Text = $"{Loc.T("cast.stat.wait")}  {CastHost.Usb?.Status}";
		else lbstat.Text = Loc.T("cast.stat.stop");
		brecv.Content = CastHost.Recv != null && CastHost.Recv.Running
			? Loc.T("cast.recv.stop") : Loc.T("cast.recv.start");
		refreshpeers();
	}

	void refreshpeers() {
		if (CastHost.Disc == null) return;
		var list = CastHost.Disc.Snapshot();
		var keep = lpeers.SelectedItem as string;
		lpeers.Items.Clear();
		foreach (var p in list) {
			if (p.Role == "send") continue;
			var s = $"{p.Name}  {p.Ip}:{p.Tcp}  [{p.Role}]";
			lpeers.Items.Add(s);
			if (s == keep) lpeers.SelectedItem = s;
		}
	}

	void save() {
		var o = CastHost.Opt;
		if (o == null) return;
		o.CastQuality = eq.SelectedItem as string ?? CastQuality.Presets[1].Name;
		o.CastAudio = caudio.IsChecked == true;
		CastHost.SaveOpt();
	}

	void brecv_Click(object sender, RoutedEventArgs e) {
		try {
			if (CastHost.Recv != null && CastHost.Recv.Running) {
				CastHost.StopRecv();
				AppendLog(Loc.T("cast.log.recvoff"));
			}
			else {
				if (!FeaturePrompt.EnsureFfmpeg(this)) return;
				CastHost.StartRecv();
			}
		}
		catch (Exception ex) { AppendLog(ex.Message); }
	}

	void bscan_Click(object sender, RoutedEventArgs e) {
		CastHost.Disc?.Beacon(CastProto.TCP_PORT);
		refreshpeers();
		AppendLog(Loc.T("cast.log.scan", lpeers.Items.Count));
	}

	void bsend_Click(object sender, RoutedEventArgs e) {
		var s = lpeers.SelectedItem as string;
		if (string.IsNullOrEmpty(s)) { AppendLog(Loc.T("cast.log.needpeer")); return; }
		var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		string host = null;
		foreach (var p in parts)
			if (p.Contains(":")) { host = p.Split(':')[0]; break; }
		if (host == null) { AppendLog(Loc.T("cast.log.badip")); return; }
		connect(host, CastProto.TCP_PORT);
	}

	void bconnect_Click(object sender, RoutedEventArgs e) {
		var ip = (eip.Text ?? "").Trim();
		if (ip.Length == 0) { AppendLog(Loc.T("cast.log.needip")); return; }
		var port = CastProto.TCP_PORT;
		var sp = ip.Split(':');
		if (sp.Length == 2 && int.TryParse(sp[1], out var p)) { ip = sp[0]; port = p; }
		connect(ip, port);
	}

	void connect(string ip, int port) {
		try {
			if (!FeaturePrompt.EnsureFfmpeg(this)) return;
			save();
			CastHost.Send.Stop();
			CastHost.Send.Start(ip, port, CastHost.CurQ(), caudio.IsChecked == true);
			lbstat.Text = $"{Loc.T("cast.stat.send")} → {ip}:{port}";
		}
		catch (Exception ex) { AppendLog($"{Loc.T("cast.log.connfail")}: {ex.Message}"); }
	}

	void bstop_Click(object sender, RoutedEventArgs e) => CastHost.Disconnect();
}
