using System.Windows;

namespace ScreenKit;

/// <summary>工具 → 密码生成器。</summary>
public partial class PasswordWindow : Window {
	public PasswordWindow() {
		InitializeComponent();
		applylang();
		initev();
		WindowEsc.Attach(this);
		gen();
	}

	void initev() {
		bgen.Click += (_, _) => gen();
		bcopy.Click += (_, _) => copy();
		bclose.Click += (_, _) => Close();
		clower.Checked += (_, _) => updatestat();
		clower.Unchecked += (_, _) => updatestat();
		cupper.Checked += (_, _) => updatestat();
		cupper.Unchecked += (_, _) => updatestat();
		cdigit.Checked += (_, _) => updatestat();
		cdigit.Unchecked += (_, _) => updatestat();
		csymbol.Checked += (_, _) => updatestat();
		csymbol.Unchecked += (_, _) => updatestat();
		cnoamb.Checked += (_, _) => updatestat();
		cnoamb.Unchecked += (_, _) => updatestat();
		elen.TextChanged += (_, _) => updatestat();
	}

	void applylang() {
		Title = Loc.T("pwgen.title");
		lblen.Text = Loc.T("pwgen.len");
		lbcount.Text = Loc.T("pwgen.count");
		clower.Content = Loc.T("pwgen.lower");
		cupper.Content = Loc.T("pwgen.upper");
		cdigit.Content = Loc.T("pwgen.digit");
		csymbol.Content = Loc.T("pwgen.symbol");
		cnoamb.Content = Loc.T("pwgen.noamb");
		ceach.Content = Loc.T("pwgen.each");
		lbout.Text = Loc.T("pwgen.out");
		ToolBtnUi.Set(bgen, ToolBtnUi.Play, Loc.T("pwgen.gen"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("pwgen.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
		updatestat();
	}

	PasswordOpts opts() {
		var len = 16;
		if (int.TryParse((elen.Text ?? "").Trim(), out var n))
			len = Compat.Clamp(n, 4, 128);
		var count = 5;
		if (int.TryParse((ecount.Text ?? "").Trim(), out var c))
			count = Compat.Clamp(c, 1, 50);
		return new PasswordOpts {
			Length = len,
			Count = count,
			Lower = clower.IsChecked == true,
			Upper = cupper.IsChecked == true,
			Digit = cdigit.IsChecked == true,
			Symbol = csymbol.IsChecked == true,
			NoAmbiguous = cnoamb.IsChecked == true,
			EachClass = ceach.IsChecked == true,
		};
	}

	void gen() {
		try {
			var o = opts();
			elen.Text = o.Length.ToString();
			ecount.Text = o.Count.ToString();
			var list = PasswordGen.Generate(o);
			eout.Text = string.Join("\r\n", list);
			updatestat();
		}
		catch (Exception ex) {
			eout.Text = "";
			lbstat.Text = Loc.T("pwgen.fail", ex.Message);
		}
	}

	void copy() {
		var s = (eout.Text ?? "").Trim();
		if (s.Length == 0) return;
		try {
			Clipboard.SetText(s);
			lbstat.Text = Loc.T("pwgen.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void updatestat() {
		try {
			var o = opts();
			var bits = PasswordGen.EntropyBits(o, o.Length);
			var pool = PasswordGen.Pool(o).Length;
			lbstat.Text = Loc.T("pwgen.stat", o.Length, pool, bits.ToString("0"));
		}
		catch {
			lbstat.Text = "";
		}
	}
}
