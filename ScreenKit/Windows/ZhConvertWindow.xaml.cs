using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

/// <summary>工具 → 简繁转换。上简体，下繁体。</summary>
public partial class ZhConvertWindow : Window {
	TextBox last;

	public ZhConvertWindow() {
		InitializeComponent();
		last = etrad;
		applylang();
		bsimp.Click += (_, _) => tosimp();
		btrad.Click += (_, _) => totrad();
		bcopy.Click += (_, _) => copyout();
		bclose.Click += (_, _) => Close();
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("zhconv.title");
		lbhint.Text = Loc.T("zhconv.hint");
		lbsimp.Text = Loc.T("zhconv.simp");
		lbtrad.Text = Loc.T("zhconv.trad");
		bsimp.Content = Loc.T("zhconv.tosimp");
		btrad.Content = Loc.T("zhconv.totrad");
		bcopy.Content = Loc.T("texttool.copy");
		bclose.Content = Loc.T("imgconv.close");
	}

	void tosimp() {
		try {
			var src = etrad.Text ?? "";
			var dst = ZhConvert.ToSimplified(src);
			esimp.Text = dst;
			last = esimp;
			stat(dst, src);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("texttool.fail", ex.Message);
		}
	}

	void totrad() {
		try {
			var src = esimp.Text ?? "";
			var dst = ZhConvert.ToTraditional(src);
			etrad.Text = dst;
			last = etrad;
			stat(src, dst);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("texttool.fail", ex.Message);
		}
	}

	void stat(string simp, string trad) {
		var line = Loc.T("zhconv.stat", simp.Length, trad.Length);
		if (simp == trad && simp.Length > 0)
			line = Loc.T("zhconv.same") + " · " + line;
		lbstat.Text = line;
	}

	void copyout() {
		try { Clipboard.SetText(last?.Text ?? ""); }
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
