using System.Windows;

namespace ScreenKit;

/// <summary>工具 → 日文注音。</summary>
public partial class JpYomiWindow : Window {
	public JpYomiWindow() {
		InitializeComponent();
		applylang();
		bword.Click += (_, _) => run(false);
		bmono.Click += (_, _) => run(true);
		bcopy.Click += (_, _) => copyout();
		bclose.Click += (_, _) => Close();
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("jpyomi.title");
		lbhint.Text = Loc.T("jpyomi.hint");
		lbin.Text = Loc.T("jpyomi.in");
		lbout.Text = Loc.T("jpyomi.out");
		bword.Content = Loc.T("jpyomi.word");
		bmono.Content = Loc.T("jpyomi.mono");
		bcopy.Content = Loc.T("texttool.copy");
		bclose.Content = Loc.T("imgconv.close");
	}

	void run(bool mono) {
		try {
			var res = JpYomi.Convert(ein.Text ?? "", mono);
			if (string.IsNullOrEmpty(res.Ruby)) {
				eout.Text = "";
				lbstat.Text = "";
				return;
			}
			eout.Text = res.Ruby + "\n" + Loc.T("jpyomi.yomi") + " " + res.Yomi;
			lbstat.Text = res.HasYomi ? "" : Loc.T("jpyomi.noyomi");
		}
		catch (Exception ex) {
			eout.Text = "";
			lbstat.Text = Loc.T("texttool.fail", ex.Message);
		}
	}

	void copyout() {
		try { Clipboard.SetText(eout.Text ?? ""); }
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
