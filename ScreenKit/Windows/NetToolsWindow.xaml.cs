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

	public NetToolsWindow() {
		InitializeComponent();
		linkbrush = TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;
		applylang();
		initev();
		WindowEsc.Attach(this);
		setbusy(false);
	}

	void initev() {
		bping.Click += (_, _) => _ = run("ping");
		bdns.Click += (_, _) => _ = run("dns");
		bwhois.Click += (_, _) => _ = run("whois");
		btrace.Click += (_, _) => _ = run("trace");
		blocate.Click += (_, _) => _ = run("locate");
		bproxy.Click += (_, _) => _ = run("proxy");
		bhttp.Click += (_, _) => _ = run("http");
		bgeo.Click += (_, _) => _ = run("geo");
		bmap.Click += (_, _) => openmap();
		bstop.Click += (_, _) => {
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
		lbhost.Text = Loc.T("nettool.host");
		lbcount.Text = Loc.T("nettool.count");
		ehost.ToolTip = Loc.T("nettool.host.tip");
		ToolBtnUi.Set(bping, ToolBtnUi.Play, Loc.T("nettool.ping"));
		ToolBtnUi.Set(bdns, ToolBtnUi.Encode, Loc.T("nettool.dns"));
		ToolBtnUi.Set(bwhois, ToolBtnUi.Font, Loc.T("nettool.whois"));
		ToolBtnUi.Set(btrace, ToolBtnUi.Up, Loc.T("nettool.trace"));
		ToolBtnUi.Set(blocate, ToolBtnUi.Encode, Loc.T("nettool.locate"));
		ToolBtnUi.Set(bproxy, ToolBtnUi.Font, Loc.T("nettool.proxy"));
		ToolBtnUi.Set(bhttp, ToolBtnUi.Play, Loc.T("nettool.http"));
		ToolBtnUi.Set(bgeo, ToolBtnUi.Pin, Loc.T("nettool.geo"));
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
		bwhois.IsEnabled = !on;
		btrace.IsEnabled = !on;
		blocate.IsEnabled = !on;
		bproxy.IsEnabled = !on;
		bhttp.IsEnabled = !on;
		bgeo.IsEnabled = !on;
		bmap.IsEnabled = !on && hasmap;
		bstop.IsEnabled = on;
		ehost.IsEnabled = !on;
		ecount.IsEnabled = !on;
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
		if (kind != "locate" && kind != "proxy" && kind != "http" && kind != "geo" && host.Length == 0) {
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
				var s = await NetTools.Resolve(host, token).ConfigureAwait(true);
				append(s);
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
