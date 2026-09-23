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
		ToolBtnUi.Set(bb64e, ToolBtnUi.Encode, Loc.T("texttool.b64e"));
		ToolBtnUi.Set(bb64d, ToolBtnUi.Decode, Loc.T("texttool.b64d"));
		ToolBtnUi.Set(burle, ToolBtnUi.Encode, Loc.T("texttool.urle"));
		ToolBtnUi.Set(burld, ToolBtnUi.Decode, Loc.T("texttool.urld"));
		ToolBtnUi.Set(butf8, ToolBtnUi.Encode, Loc.T("texttool.utf8hex"));
		ToolBtnUi.Set(bgbk, ToolBtnUi.Encode, Loc.T("texttool.gbkhex"));
		ToolBtnUi.Set(bfu8, ToolBtnUi.Decode, Loc.T("texttool.fromutf8"));
		ToolBtnUi.Set(bfgbk, ToolBtnUi.Decode, Loc.T("texttool.fromgbk"));
		ToolBtnUi.Set(buesc, ToolBtnUi.Encode, Loc.T("texttool.uesc"));
		ToolBtnUi.Set(buunesc, ToolBtnUi.Decode, Loc.T("texttool.uunesc"));
		ToolBtnUi.Set(bupper, ToolBtnUi.Font, Loc.T("texttool.upper"));
		ToolBtnUi.Set(blower, ToolBtnUi.Font, Loc.T("texttool.lower"));
		ToolBtnUi.Set(btrim, ToolBtnUi.Clear, Loc.T("texttool.trim"));
		ToolBtnUi.Set(bcollapse, ToolBtnUi.Clear, Loc.T("texttool.collapse"));
		ToolBtnUi.Set(bempty, ToolBtnUi.Clear, Loc.T("texttool.noline"));
		ToolBtnUi.Set(bswap, ToolBtnUi.Swap, Loc.T("texttool.swap"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("texttool.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
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
