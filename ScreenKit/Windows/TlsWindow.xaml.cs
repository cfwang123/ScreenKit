using System.Text;

namespace ScreenKit;

/// <summary>工具 → 网络工具 → TLS 证书检查。</summary>
public partial class TlsWindow : Window {
	List<TlsPair> pairs = new();
	List<TlsCertRow> chain = new();
	List<string> notes = new();
	int run;
	CancellationTokenSource cts;
	bool dead;

	public TlsWindow() {
		InitializeComponent();
		applylang();
		check();
		bcheck.Click += (_, _) => check();
		bcopy.Click += (_, _) => copy();
		etarget.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			check();
		};
		lvchain.SelectionChanged += (_, _) => showcert();
		WindowEsc.Attach(this);
		Closed += (_, _) => {
			dead = true;
			try { cts?.Cancel(); } catch { }
			try { cts?.Dispose(); } catch { }
			cts = null;
		};
		Loaded += (_, _) => {
			etarget.Focus();
			etarget.SelectAll();
		};
	}

	void applylang() {
		Title = Loc.T("tls.title");
		lbhint.Text = Loc.T("tls.hint");
		lbtarget.Text = Loc.T("tls.target");
		etarget.ToolTip = Loc.T("tls.target.tip");
		lbinfo.Text = Loc.T("tls.info.header");
		colinfoname.Header = Loc.T("tls.col.name");
		colinfoval.Header = Loc.T("tls.col.value");
		colidx.Header = Loc.T("tls.col.idx");
		colsubject.Header = Loc.T("tls.col.subject");
		colstate.Header = Loc.T("tls.col.state");
		ToolBtnUi.Set(bcheck, ToolBtnUi.Encode, Loc.T("tls.check"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("tls.copy"));
	}

	void check() {
		var text = (etarget.Text ?? "").Trim();
		if (text.Length == 0) {
			// 没填地址就只显示空列表，不弹错误
			lbstat.Text = Loc.T("tls.err.empty");
			return;
		}
		try { cts?.Cancel(); } catch { }
		try { cts?.Dispose(); } catch { }
		cts = new CancellationTokenSource();
		var token = cts.Token;
		var gen = ++run;
		bcheck.IsEnabled = false;
		lbstat.Text = Loc.T("tls.running");
		pairs = new List<TlsPair>();
		chain = new List<TlsCertRow>();
		notes = new List<string>();
		swaplist(false);
		Task.Run(() => {
			var gotpairs = new List<TlsPair>();
			var gotchain = new List<TlsCertRow>();
			var gotnotes = new List<string>();
			try { TlsCheck.Run(text, token, gotpairs, gotchain, gotnotes); }
			catch (Exception ex) { gotnotes.Add(ex.Message); }
			Dispatcher.Invoke(() => {
				if (dead || gen != run) return;
				bcheck.IsEnabled = true;
				pairs = gotpairs;
				chain = gotchain;
				notes = gotnotes;
				swaplist(true);
			});
		});
	}

	void swaplist(bool pick) {
		lvinfo.ItemsSource = null;
		lvinfo.ItemsSource = pairs;
		lvchain.ItemsSource = null;
		lvchain.ItemsSource = chain;
		if (!pick) {
			edetail.Text = "";
			lbstat.Text = Loc.T("tls.running");
			return;
		}
		if (chain.Count > 0) {
			lvchain.SelectedIndex = 0;
			lvchain.ScrollIntoView(lvchain.SelectedItem);
		}
		else {
			edetail.Text = "";
			showcert();
		}
		var stat = Loc.T("tls.stat", pairs.Count, chain.Count);
		if (notes.Count > 0) stat += " · " + string.Join(" · ", notes);
		lbstat.Text = stat;
	}

	void showcert() {
		if (lvchain.SelectedItem is not TlsCertRow row) {
			edetail.Text = "";
			return;
		}
		var sb = new StringBuilder();
		sb.Append(Loc.T("tls.col.subject")).Append(": ").Append(row.Subject).Append('\n');
		sb.Append(Loc.T("tls.p.issuer")).Append(": ").Append(row.Issuer).Append('\n');
		if (!string.IsNullOrEmpty(row.State))
			sb.Append(Loc.T("tls.col.state")).Append(": ").Append(row.State).Append('\n');
		if (!string.IsNullOrEmpty(row.NotBefore))
			sb.Append(Loc.T("tls.p.notbefore")).Append(": ").Append(row.NotBefore).Append('\n');
		if (!string.IsNullOrEmpty(row.NotAfter))
			sb.Append(Loc.T("tls.p.notafter")).Append(": ").Append(row.NotAfter).Append('\n');
		if (!string.IsNullOrEmpty(row.San))
			sb.Append(Loc.T("tls.p.san")).Append(": ").Append(row.San).Append('\n');
		if (!string.IsNullOrEmpty(row.SigAlg))
			sb.Append(Loc.T("tls.p.sigalg")).Append(": ").Append(row.SigAlg).Append('\n');
		if (!string.IsNullOrEmpty(row.KeyAlg))
			sb.Append(Loc.T("tls.p.key")).Append(": ").Append(row.KeyAlg).Append('\n');
		if (!string.IsNullOrEmpty(row.Serial))
			sb.Append(Loc.T("tls.p.serial")).Append(": ").Append(row.Serial).Append('\n');
		if (!string.IsNullOrEmpty(row.Fingerprint))
			sb.Append(Loc.T("tls.p.fingerprint")).Append(": ").Append(row.Fingerprint).Append('\n');
		if (!string.IsNullOrEmpty(row.Extensions))
			sb.Append(Loc.T("tls.p.extensions")).Append(": ").Append(row.Extensions).Append('\n');
		if (!string.IsNullOrEmpty(row.Pem)) sb.Append('\n').Append(row.Pem);
		edetail.Text = sb.ToString();
		edetail.ScrollToHome();
	}

	void copy() {
		var sb = new StringBuilder();
		if (pairs.Count > 0) {
			sb.Append("## ").Append(Loc.T("tls.info.header")).Append("\r\n");
			foreach (var p in pairs) sb.Append(p.Name).Append("\t\t").Append(p.Value).Append("\r\n");
			sb.Append("\r\n");
		}
		if (chain.Count > 0) {
			sb.Append("## ").Append(Loc.T("tls.col.subject")).Append("\r\n");
			// 选中了单张就只复制那张的 PEM，否则复制整条链
			if (lvchain.SelectedItem is TlsCertRow sel && !string.IsNullOrEmpty(sel.Pem)) {
				sb.Append(sel.Pem);
			}
			else {
				foreach (var c in chain) {
					if (string.IsNullOrEmpty(c.Pem)) continue;
					sb.Append(c.Pem);
				}
			}
		}
		if (notes.Count > 0) {
			sb.Append("## ").Append(Loc.T("tls.col.state")).Append("\r\n");
			foreach (var n in notes) sb.Append(n).Append("\r\n");
		}
		if (sb.Length == 0) {
			lbstat.Text = Loc.T("tls.nothing");
			return;
		}
		try {
			Clipboard.SetText(sb.ToString());
			lbstat.Text = Loc.T("tls.copied");
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
		}
	}
}

