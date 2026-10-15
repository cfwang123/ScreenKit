using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace ScreenKit;

/// <summary>工具 → 网络工具：Ping / DNS / WHOIS。</summary>
public partial class NetToolsWindow : Window {
	static readonly Regex UrlRe = new(@"https?://\S+",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	CancellationTokenSource cts;
	bool busy;
	bool hasmap;
	double maplat;
	double maplon;
	Brush linkbrush;
	readonly ObservableCollection<PortRow> psrows = new();
	readonly ObservableCollection<IpRow> iprows = new();

	public NetToolsWindow() {
		InitializeComponent();
		lvps.ItemsSource = psrows;
		lvip.ItemsSource = iprows;
		linkbrush = TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;
		applylang();
		initev();
		WindowEsc.Attach(this);
		setbusy(false);
	}

	void initev() {
		bping.Click += (_, _) => _ = run("ping");
		bdns.Click += (_, _) => _ = run("dns");
		bdnsrec.Click += (_, _) => _ = run("dnsrec");
		bport.Click += (_, _) => _ = run("port");
		blocal.Click += (_, _) => _ = run("local");
		bwake.Click += (_, _) => _ = run("wol");
		barp.Click += (_, _) => _ = run("arp");
		barprev.Click += (_, _) => _ = run("arprev");
		bwhois.Click += (_, _) => _ = run("whois");
		btrace.Click += (_, _) => _ = run("trace");
		blocate.Click += (_, _) => _ = run("locate");
		bproxy.Click += (_, _) => _ = run("proxy");
		bipcity.Click += (_, _) => _ = run("ipcity");
		bmac.Click += (_, _) => _ = run("mac");
		busb.Click += (_, _) => _ = run("usb");
		bhttp.Click += (_, _) => _ = run("http");
		bgeo.Click += (_, _) => _ = run("geo");
		bscan.Click += (_, _) => { tabs.SelectedItem = tabip; _ = runiscan(); };
		bdiscover.Click += (_, _) => _ = run("discover");
		bpstart.Click += (_, _) => _ = runpscan();
		bpcommon.Click += (_, _) => epsports.Text = NetTools.CommonPortText();
		bistart.Click += (_, _) => _ = runiscan();
		biport.Click += (_, _) => toport();
		lvip.MouseDoubleClick += (_, _) => toport();
		bmap.Click += (_, _) => openmap();
		bstop.Click += (_, _) => {
			try { cts?.Cancel(); } catch { }
		};
		bpstop.Click += (_, _) => {
			try { cts?.Cancel(); } catch { }
		};
		bistop.Click += (_, _) => {
			try { cts?.Cancel(); } catch { }
		};
		bclear.Click += (_, _) => { clearout(); lbstat.Text = ""; clearmap(); };
		bcopy.Click += (_, _) => {
			var s = outtext();
			if (s.Length == 0) return;
			try { Clipboard.SetText(s); lbstat.Text = Loc.T("nettool.copied"); }
			catch (Exception ex) {
				MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		};
		bclose.Click += (_, _) => Close();
		bpsclear.Click += (_, _) => { psrows.Clear(); psbar.Value = 0; lbpsstat.Text = ""; };
		bpscopy.Click += (_, _) => {
			if (psrows.Count == 0) return;
			var sb = new System.Text.StringBuilder();
			foreach (var row in psrows)
				sb.Append(row.Port).Append('\t').Append(row.Kind).Append('\t').Append(row.Banner).Append('\n');
			var s = sb.ToString();
			try { Clipboard.SetText(s); lbpsstat.Text = Loc.T("nettool.copied"); }
			catch (Exception ex) {
				MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		};
		bpsclose.Click += (_, _) => Close();
		biclear.Click += (_, _) => { iprows.Clear(); isbar.Value = 0; lbistat.Text = ""; };
		bicopy.Click += (_, _) => {
			if (iprows.Count == 0) return;
			var sb = new System.Text.StringBuilder();
			foreach (var row in iprows) sb.Append(row.Name).Append('\t').Append(row.Ip).Append('\t').Append(row.Mac).Append('\n');
			try { Clipboard.SetText(sb.ToString()); lbistat.Text = Loc.T("nettool.copied"); }
			catch (Exception ex) {
				MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		};
		biclose.Click += (_, _) => Close();
		lvip.PreviewMouseRightButtonDown += (_, e) => pickiprow(e.OriginalSource as DependencyObject);
		mnipname.Click += (_, _) => copyip("name");
		mnipaddr.Click += (_, _) => copyip("ip");
		mnipmac.Click += (_, _) => copyip("mac");
		if (lvip.ContextMenu != null) {
			lvip.ContextMenu.Opened += (_, _) => {
				var on = lvip.SelectedItem is IpRow;
				mnipname.IsEnabled = on;
				mnipaddr.IsEnabled = on;
				mnipmac.IsEnabled = on;
			};
		}
		epsip.KeyDown += (_, e) => {
			if (e.Key == System.Windows.Input.Key.Enter && !busy) {
				_ = runpscan();
				e.Handled = true;
			}
		};
		epsports.KeyDown += (_, e) => {
			if (e.Key == System.Windows.Input.Key.Enter && !busy) {
				_ = runpscan();
				e.Handled = true;
			}
		};
		ehost.KeyDown += (_, e) => {
			if (e.Key == System.Windows.Input.Key.Enter && !busy) {
				_ = run("ping");
				e.Handled = true;
			}
		};
		Closing += (_, _) => {
			try { cts?.Cancel(); } catch { }
		};
	}

	void applylang() {
		Title = Loc.T("nettool.title");
		tabdiag.Header = Loc.T("nettool.tab.diag");
		tabip.Header = Loc.T("nettool.tab.ip");
		tabpscan.Header = Loc.T("nettool.tab.pscan");
		colipname.Header = Loc.T("nettool.ip.col.name");
		colipaddr.Header = Loc.T("nettool.ip.col.ip");
		colipmac.Header = Loc.T("nettool.ip.col.mac");
		mnipname.Header = Loc.T("nettool.ip.copy.name");
		mnipaddr.Header = Loc.T("nettool.ip.copy.ip");
		mnipmac.Header = Loc.T("nettool.ip.copy.mac");
		ToolBtnUi.Set(bistart, ToolBtnUi.Play, Loc.T("nettool.pscan.start"));
		ToolBtnUi.Set(bistop, ToolBtnUi.Cancel, Loc.T("nettool.stop"));
		ToolBtnUi.Set(biport, ToolBtnUi.Browse, Loc.T("nettool.scan.toport"));
		ToolBtnUi.Set(biclear, ToolBtnUi.Clear, Loc.T("imgconv.clear"));
		ToolBtnUi.Set(bicopy, ToolBtnUi.Copy, Loc.T("pwgen.copy"));
		ToolBtnUi.Set(biclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
		lbpsip.Text = Loc.T("nettool.pscan.ip");
		lbpsports.Text = Loc.T("nettool.pscan.ports");
		epsip.ToolTip = Loc.T("nettool.pscan.ip.tip");
		epsports.ToolTip = Loc.T("nettool.pscan.ports.tip");
		colpsport.Header = Loc.T("nettool.pscan.col.port");
		colpskind.Header = Loc.T("nettool.pscan.col.kind");
		colpsbanner.Header = Loc.T("nettool.pscan.col.banner");
		lbptimeout.Text = Loc.T("nettool.pscan.timeout");
		eptimeout.ToolTip = Loc.T("nettool.pscan.timeout.tip");
		ToolBtnUi.Set(bpcommon, ToolBtnUi.Font, Loc.T("nettool.pscan.common"));
		ToolBtnUi.Set(bpstart, ToolBtnUi.Play, Loc.T("nettool.pscan.start"));
		ToolBtnUi.Set(bpstop, ToolBtnUi.Cancel, Loc.T("nettool.stop"));
		ToolBtnUi.Set(bpsclear, ToolBtnUi.Clear, Loc.T("imgconv.clear"));
		ToolBtnUi.Set(bpscopy, ToolBtnUi.Copy, Loc.T("pwgen.copy"));
		ToolBtnUi.Set(bpsclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
		lbhost.Text = Loc.T("nettool.host");
		lbcount.Text = Loc.T("nettool.count");
		ehost.ToolTip = Loc.T("nettool.host.tip");
		ToolBtnUi.Set(bping, ToolBtnUi.Play, Loc.T("nettool.ping"));
		ToolBtnUi.Set(bdns, ToolBtnUi.Encode, Loc.T("nettool.dns"));
		ToolBtnUi.Set(bdnsrec, ToolBtnUi.Font, Loc.T("nettool.dnsrec"));
		ToolBtnUi.Set(bport, ToolBtnUi.Play, Loc.T("nettool.port"));
		ToolBtnUi.Set(blocal, ToolBtnUi.Browse, Loc.T("nettool.local"));
		ToolBtnUi.Set(bwake, ToolBtnUi.Up, Loc.T("nettool.wake"));
		ToolBtnUi.Set(barp, ToolBtnUi.Encode, Loc.T("nettool.arp"));
		ToolBtnUi.Set(barprev, ToolBtnUi.Browse, Loc.T("nettool.arprev"));
		ToolBtnUi.Set(bwhois, ToolBtnUi.Font, Loc.T("nettool.whois"));
		ToolBtnUi.Set(btrace, ToolBtnUi.Up, Loc.T("nettool.trace"));
		ToolBtnUi.Set(blocate, ToolBtnUi.Encode, Loc.T("nettool.locate"));
		ToolBtnUi.Set(bproxy, ToolBtnUi.Font, Loc.T("nettool.proxy"));
		ToolBtnUi.Set(bipcity, ToolBtnUi.Pin, Loc.T("nettool.ipcity"));
		ToolBtnUi.Set(bmac, ToolBtnUi.Font, Loc.T("nettool.mac"));
		ToolBtnUi.Set(busb, ToolBtnUi.Font, Loc.T("nettool.usb"));
		ToolBtnUi.Set(bhttp, ToolBtnUi.Play, Loc.T("nettool.http"));
		ToolBtnUi.Set(bgeo, ToolBtnUi.Pin, Loc.T("nettool.geo"));
		ToolBtnUi.Set(bscan, ToolBtnUi.Browse, Loc.T("nettool.scan"));
		ToolBtnUi.Set(bdiscover, ToolBtnUi.Browse, Loc.T("nettool.discover"));
		ToolBtnUi.Set(bmap, ToolBtnUi.Browse, Loc.T("nettool.map"));
		bmap.ToolTip = Loc.T("nettool.map.tip");
		ToolBtnUi.Set(bstop, ToolBtnUi.Cancel, Loc.T("nettool.stop"));
		ToolBtnUi.Set(bclear, ToolBtnUi.Clear, Loc.T("imgconv.clear"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("pwgen.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	void setbusy(bool on) {
		busy = on;
		bping.IsEnabled = !on;
		bdns.IsEnabled = !on;
		bdnsrec.IsEnabled = !on;
		bport.IsEnabled = !on;
		blocal.IsEnabled = !on;
		bwake.IsEnabled = !on;
		barp.IsEnabled = !on;
		barprev.IsEnabled = !on;
		bwhois.IsEnabled = !on;
		btrace.IsEnabled = !on;
		blocate.IsEnabled = !on;
		bproxy.IsEnabled = !on;
		bipcity.IsEnabled = !on;
		bmac.IsEnabled = !on;
		busb.IsEnabled = !on;
		bhttp.IsEnabled = !on;
		bgeo.IsEnabled = !on;
		bscan.IsEnabled = !on;
		bdiscover.IsEnabled = !on;
		bpstart.IsEnabled = !on;
		bpcommon.IsEnabled = !on;
		bistart.IsEnabled = !on;
		biport.IsEnabled = !on;
		bmap.IsEnabled = !on && hasmap;
		bstop.IsEnabled = on;
		bpstop.IsEnabled = on;
		bistop.IsEnabled = on;
		ehost.IsEnabled = !on;
		ecount.IsEnabled = !on;
		epsip.IsEnabled = !on;
		epsports.IsEnabled = !on;
		eptimeout.IsEnabled = !on;
	}

	void clearout() => eout.Document.Blocks.Clear();

	string outtext() {
		var range = new TextRange(eout.Document.ContentStart, eout.Document.ContentEnd);
		return range.Text ?? "";
	}

	void append(string line) {
		if (string.IsNullOrEmpty(line)) return;
		var text = line.Replace("\r\n", "\n").Replace('\r', '\n');
		if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
		foreach (var row in text.Split('\n'))
			addrow(row);
		eout.ScrollToEnd();
	}

	void addrow(string row) {
		var p = new Paragraph { Margin = new Thickness(0) };
		var i = 0;
		foreach (Match m in UrlRe.Matches(row ?? "")) {
			if (m.Index > i)
				p.Inlines.Add(new Run(row.Substring(i, m.Index - i)));
			addlink(p, m.Value);
			i = m.Index + m.Length;
		}
		if (row != null && i < row.Length)
			p.Inlines.Add(new Run(row.Substring(i)));
		if (p.Inlines.Count == 0)
			p.Inlines.Add(new Run(""));
		eout.Document.Blocks.Add(p);
	}

	void addlink(Paragraph p, string url) {
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
			p.Inlines.Add(new Run(url));
			return;
		}
		var link = new Hyperlink(new Run(url)) {
			NavigateUri = uri,
			Foreground = linkbrush,
			TextDecorations = TextDecorations.Underline,
			Cursor = Cursors.Hand,
		};
		link.RequestNavigate += onlink;
		p.Inlines.Add(link);
	}

	void onlink(object sender, RequestNavigateEventArgs e) {
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
				FileName = e.Uri.AbsoluteUri,
				UseShellExecute = true,
			});
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		e.Handled = true;
	}

	async Task run(string kind) {
		if (busy) return;
		var host = (ehost.Text ?? "").Trim();
		if (kind != "locate" && kind != "proxy" && kind != "http" && kind != "geo" && kind != "scan"
			&& kind != "ipcity" && kind != "local" && kind != "discover" && host.Length == 0) {
			lbstat.Text = Loc.T("nettool.empty");
			ehost.Focus();
			return;
		}
		var count = 4;
		if (int.TryParse((ecount.Text ?? "").Trim(), out var n))
			count = Compat.Clamp(n, 1, 20);
		ecount.Text = count.ToString();
		cts = new CancellationTokenSource();
		var token = cts.Token;
		setbusy(true);
		lbstat.Text = Loc.T("nettool.busy");
		clearout();
		try {
			if (kind == "ping") {
				await NetTools.Ping(host, count, 3000, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "dns") {
				await NetTools.Resolve(host, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "dnsrec") {
				await NetTools.DnsRecords(host, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "port") {
				await NetTools.PortProbe(host, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "local") {
				await Task.Run(() => NetTools.LocalNetwork(s => Dispatcher.Invoke(() => append(s))), token)
					.ConfigureAwait(true);
			}
			else if (kind == "wol") {
				await Task.Run(() => NetTools.Wake(host, s => Dispatcher.Invoke(() => append(s))), token)
					.ConfigureAwait(true);
			}
			else if (kind == "arp") {
				await NetTools.ArpResolve(host, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "arprev") {
				await NetTools.ArpReverse(host, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "trace") {
				await NetTools.Trace(host, 30, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "locate") {
				await NetTools.Locate(s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "proxy") {
				await NetTools.HttpLocate(s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "ipcity") {
				clearmap();
				var hit = await NetTools.IpCity(host, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
				if (hit.HasPos) setmap(hit.Lat, hit.Lon);
			}
			else if (kind == "mac") {
				await NetTools.MacVendor(host, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "usb") {
				await NetTools.UsbVendor(host, s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "http") {
				await NetTools.HttpSpeedLocate(s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "geo") {
				clearmap();
				var fix = await NetTools.Geolocate(s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
				if (fix.Ok) setmap(fix.Lat, fix.Lon);
			}
			else if (kind == "scan") {
				await NetTools.SearchComputers(s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else if (kind == "discover") {
				await NetTools.DiscoverComputers(s => Dispatcher.Invoke(() => append(s)), token)
					.ConfigureAwait(true);
			}
			else {
				var s = await NetTools.Whois(host, token).ConfigureAwait(true);
				append(s);
			}
			lbstat.Text = Loc.T("nettool.done");
		}
		catch (OperationCanceledException) {
			append(Loc.T("nettool.cancelled"));
			lbstat.Text = Loc.T("nettool.cancelled");
		}
		catch (Exception ex) {
			append(ex.Message);
			lbstat.Text = Loc.T("nettool.fail", ex.Message);
		}
		finally {
			try { cts.Dispose(); } catch { }
			cts = null;
			setbusy(false);
		}
	}

	async Task runpscan() {
		if (busy) return;
		var host = (epsip.Text ?? "").Trim();
		if (host.Length == 0) {
			lbpsstat.Text = Loc.T("nettool.pscan.emptyip");
			epsip.Focus();
			return;
		}
		if (!NetTools.TryParsePorts(epsports.Text, out var ports, out var err)) {
			lbpsstat.Text = err == "toomany" ? Loc.T("nettool.pscan.toomany", 65535)
				: err == "empty" ? Loc.T("nettool.pscan.emptyports")
				: Loc.T("nettool.pscan.badports");
			epsports.Focus();
			return;
		}
		cts = new CancellationTokenSource();
		var token = cts.Token;
		setbusy(true);
		lbpsstat.Text = Loc.T("nettool.pscan.begin", host, ports.Count);
		psrows.Clear();
		psbar.Minimum = 0;
		psbar.Maximum = ports.Count;
		psbar.Value = 0;
		var timeout = 400;
		if (int.TryParse((eptimeout.Text ?? "").Trim(), out var ms))
			timeout = Compat.Clamp(ms, 100, 5000);
		eptimeout.Text = timeout.ToString();
		try {
			await NetTools.PortScan(host, ports, _ => { }, token,
				(done, total, open) => Dispatcher.Invoke(() => {
					psbar.Maximum = Math.Max(1, total);
					psbar.Value = done;
					lbpsstat.Text = Loc.T("nettool.pscan.progress", done, total, open);
				}),
				(port, kind, banner) => Dispatcher.Invoke(() => addport(port, kind, banner)),
				timeout)
				.ConfigureAwait(true);
			lbpsstat.Text = psrows.Count == 0 ? Loc.T("nettool.pscan.none") : Loc.T("nettool.pscan.found", psrows.Count);
		}
		catch (OperationCanceledException) {
			lbpsstat.Text = Loc.T("nettool.cancelled");
		}
		catch (Exception ex) {
			lbpsstat.Text = Loc.T("nettool.fail", ex.Message);
		}
		finally {
			try { cts.Dispose(); } catch { }
			cts = null;
			setbusy(false);
		}
	}

	async Task runiscan() {
		if (busy) return;
		cts = new CancellationTokenSource();
		var token = cts.Token;
		setbusy(true);
		iprows.Clear();
		isbar.Minimum = 0;
		isbar.Maximum = 1;
		isbar.Value = 0;
		lbistat.Text = Loc.T("nettool.busy");
		try {
			var rows = await NetTools.SearchComputers(
				s => Dispatcher.Invoke(() => lbistat.Text = s),
				token,
				(done, total) => Dispatcher.Invoke(() => {
					isbar.Maximum = Math.Max(1, total);
					isbar.Value = done;
					if (total > 0) lbistat.Text = Loc.T("nettool.scan.progress", done, total);
				}),
				row => Dispatcher.Invoke(() => showhost(row, false)),
				row => Dispatcher.Invoke(() => showhost(row, true))).ConfigureAwait(true);
			if (iprows.Count == 0) {
				foreach (var row in rows) showhost(row, true);
			}
			lbistat.Text = rows.Count == 0 ? Loc.T("nettool.scan.none") : Loc.T("nettool.scan.found", rows.Count);
		}
		catch (OperationCanceledException) {
			lbistat.Text = Loc.T("nettool.cancelled");
		}
		catch (Exception ex) {
			lbistat.Text = Loc.T("nettool.fail", ex.Message);
		}
		finally {
			try { cts.Dispose(); } catch { }
			cts = null;
			setbusy(false);
		}
	}

	void pickiprow(DependencyObject src) {
		while (src != null && src is not ListViewItem)
			src = VisualTreeHelper.GetParent(src);
		if (src is not ListViewItem item) return;
		item.IsSelected = true;
		item.Focus();
	}

	void copyip(string kind) {
		if (lvip.SelectedItem is not IpRow row) {
			lbistat.Text = Loc.T("nettool.scan.pick");
			return;
		}
		var text = kind == "ip" ? row.Ip : kind == "mac" ? row.Mac : row.Name;
		if (kind == "name" && text == "-") text = "";
		if (string.IsNullOrEmpty(text)) return;
		try {
			Clipboard.SetText(text);
			lbistat.Text = Loc.T("nettool.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void showhost(LanComputer row, bool named) {
		if (row == null || string.IsNullOrEmpty(row.Ip)) return;
		IpRow item = null;
		foreach (var it in iprows) {
			if (it.Ip == row.Ip) { item = it; break; }
		}
		if (item == null) {
			item = new IpRow { Ip = row.Ip, Mac = row.Mac ?? "", Name = "-" };
			iprows.Add(item);
		}
		if (!named) return;
		var name = (row.HostName ?? "").Trim();
		item.Name = name.Length == 0 ? "-" : name;
	}

	void toport() {
		if (lvip.SelectedItem is not IpRow row || string.IsNullOrEmpty(row.Ip)) {
			lbistat.Text = Loc.T("nettool.scan.pick");
			return;
		}
		epsip.Text = row.Ip;
		tabs.SelectedItem = tabpscan;
	}

	void addport(int port, string kind, string banner) {
		var row = new PortRow { Port = port, Kind = kind, Banner = banner ?? "" };
		var i = 0;
		while (i < psrows.Count && psrows[i].Port < port) i++;
		if (i < psrows.Count && psrows[i].Port == port) return;
		psrows.Insert(i, row);
	}

	void setmap(double lat, double lon) {
		hasmap = true;
		maplat = lat;
		maplon = lon;
		bmap.IsEnabled = !busy;
	}

	void clearmap() {
		hasmap = false;
		bmap.IsEnabled = false;
	}

	void openmap() {
		if (!hasmap) return;
		var url = NetTools.MapAmap(maplat, maplon);
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
				FileName = url,
				UseShellExecute = true,
			});
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}

sealed class PortRow {
	public int Port { get; set; }
	public string Kind { get; set; }
	public string Banner { get; set; }
}

sealed class IpRow : INotifyPropertyChanged {
	string name = "-";
	public string Name {
		get => name;
		set {
			if (name == value) return;
			name = value ?? "";
			On();
		}
	}
	public string Ip { get; set; }
	public string Mac { get; set; }
	public event PropertyChangedEventHandler PropertyChanged;
	void On([CallerMemberName] string n = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
