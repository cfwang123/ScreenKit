using System.Windows;

namespace ScreenKit;

/// <summary>工具 → 简繁转换。</summary>
public partial class ZhConvertWindow : Window {
	public ZhConvertWindow() {
		InitializeComponent();
		applylang();
		btrad.Click += (_, _) => run(ZhConvert.ToTraditional);
		bsimp.Click += (_, _) => run(ZhConvert.ToSimplified);
		bswap.Click += (_, _) => {
			var a = ein.Text;
			ein.Text = eout.Text;
			eout.Text = a;
			updatestat();
		};
		bcopy.Click += (_, _) => copyout();
		bclose.Click += (_, _) => Close();
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("zhconv.title");
		lbhint.Text = Loc.T("zhconv.hint");
		lbin.Text = Loc.T("zhconv.in");
		lbout.Text = Loc.T("zhconv.out");
		btrad.Content = Loc.T("zhconv.totrad");
		bsimp.Content = Loc.T("zhconv.tosimp");
		bswap.Content = Loc.T("zhconv.swap");
		bcopy.Content = Loc.T("texttool.copy");
		bclose.Content = Loc.T("imgconv.close");
	}

	void run(Func<string, string> fn) {
		try {
			var src = ein.Text ?? "";
			var dst = fn(src);
			eout.Text = dst;
			lbstat.Text = dst == src && src.Length > 0
				? Loc.T("zhconv.same") + " · " + Loc.T("zhconv.stat", src.Length, dst.Length)
				: Loc.T("zhconv.stat", src.Length, dst.Length);
		}
		catch (Exception ex) {
			eout.Text = "";
			lbstat.Text = Loc.T("texttool.fail", ex.Message);
		}
	}

	void updatestat() {
		lbstat.Text = Loc.T("zhconv.stat", (ein.Text ?? "").Length, (eout.Text ?? "").Length);
	}

	void copyout() {
		try { Clipboard.SetText(eout.Text ?? ""); }
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
