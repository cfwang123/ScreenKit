using System.Windows;

namespace ScreenKit;

/// <summary>工具 → 文本小工具。</summary>
public partial class TextToolWindow : Window {
	public TextToolWindow() {
		InitializeComponent();
		applylang();
		initev();
		WindowEsc.Attach(this);
		updatestats();
	}

	void initev() {
		ein.TextChanged += (_, _) => updatestats();
		bb64e.Click += (_, _) => run(TextTools.Base64Enc);
		bb64d.Click += (_, _) => run(TextTools.Base64Dec);
		burle.Click += (_, _) => run(TextTools.UrlEnc);
		burld.Click += (_, _) => run(TextTools.UrlDec);
		butf8.Click += (_, _) => run(TextTools.Utf8Hex);
		bgbk.Click += (_, _) => run(TextTools.GbkHex);
		bfu8.Click += (_, _) => run(TextTools.FromUtf8Hex);
		bfgbk.Click += (_, _) => run(TextTools.FromGbkHex);
		buesc.Click += (_, _) => run(TextTools.UnicodeEsc);
		buunesc.Click += (_, _) => run(TextTools.UnicodeUnesc);
		bupper.Click += (_, _) => run(s => s?.ToUpperInvariant() ?? "");
		blower.Click += (_, _) => run(s => s?.ToLowerInvariant() ?? "");
		btrim.Click += (_, _) => run(s => s?.Trim() ?? "");
		bcollapse.Click += (_, _) => run(TextTools.CollapseWs);
		bempty.Click += (_, _) => run(TextTools.DropEmptyLines);
		bswap.Click += (_, _) => {
			var a = ein.Text;
			ein.Text = eout.Text;
			eout.Text = a;
		};
		bcopy.Click += (_, _) => {
			try { Clipboard.SetText(eout.Text ?? ""); }
			catch (Exception ex) {
				MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		};
		bclose.Click += (_, _) => Close();
	}

	void applylang() {
		Title = Loc.T("texttool.title");
		lbin.Text = Loc.T("texttool.in");
		lbout.Text = Loc.T("texttool.out");
		bb64e.Content = Loc.T("texttool.b64e");
		bb64d.Content = Loc.T("texttool.b64d");
		burle.Content = Loc.T("texttool.urle");
		burld.Content = Loc.T("texttool.urld");
		butf8.Content = Loc.T("texttool.utf8hex");
		bgbk.Content = Loc.T("texttool.gbkhex");
		bfu8.Content = Loc.T("texttool.fromutf8");
		bfgbk.Content = Loc.T("texttool.fromgbk");
		buesc.Content = Loc.T("texttool.uesc");
		buunesc.Content = Loc.T("texttool.uunesc");
		bupper.Content = Loc.T("texttool.upper");
		blower.Content = Loc.T("texttool.lower");
		btrim.Content = Loc.T("texttool.trim");
		bcollapse.Content = Loc.T("texttool.collapse");
		bempty.Content = Loc.T("texttool.noline");
		bswap.Content = Loc.T("texttool.swap");
		bcopy.Content = Loc.T("texttool.copy");
		bclose.Content = Loc.T("imgconv.close");
	}

	void run(Func<string, string> fn) {
		try {
			eout.Text = fn(ein.Text ?? "");
			updatestats();
		}
		catch (Exception ex) {
			eout.Text = "";
			lbstat.Text = Loc.T("texttool.fail", ex.Message);
		}
	}

	void updatestats() {
		var s = TextTools.Stats(ein.Text ?? "");
		lbstat.Text = Loc.T("texttool.stat", s.Chars, s.CharsNoWs, s.Lines, s.Utf8Bytes, s.GbkBytes);
	}
}
