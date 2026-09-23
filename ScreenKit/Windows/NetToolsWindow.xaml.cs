using System.Windows;

namespace ScreenKit;

/// <summary>工具 → 网络工具：Ping / DNS / WHOIS。</summary>
public partial class NetToolsWindow : Window {
	CancellationTokenSource cts;
	bool busy;

	public NetToolsWindow() {
		InitializeComponent();
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
		bstop.Click += (_, _) => {
			try { cts?.Cancel(); } catch { }
		};
		bclear.Click += (_, _) => { eout.Text = ""; lbstat.Text = ""; };
		bcopy.Click += (_, _) => {
			var s = eout.Text ?? "";
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
		bstop.IsEnabled = on;
		ehost.IsEnabled = !on;
		ecount.IsEnabled = !on;
	}

	void append(string line) {
		if (string.IsNullOrEmpty(line)) return;
		if (eout.Text.Length > 0 && !eout.Text.EndsWith("\n") && !eout.Text.EndsWith("\r"))
			eout.AppendText("\r\n");
		eout.AppendText(line.Replace("\n", "\r\n"));
		if (!line.EndsWith("\n"))
			eout.AppendText("\r\n");
		eout.CaretIndex = eout.Text.Length;
		eout.ScrollToEnd();
	}

	async Task run(string kind) {
		if (busy) return;
		var host = (ehost.Text ?? "").Trim();
		if (kind != "locate" && kind != "proxy" && kind != "http" && host.Length == 0) {
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
		eout.Text = "";
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
}
