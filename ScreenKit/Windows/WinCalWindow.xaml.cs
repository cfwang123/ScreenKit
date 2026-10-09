using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

/// <summary>工具 → 历法。</summary>
public partial class WinCalWindow : Window {
	bool loading;

	public WinCalWindow() {
		InitializeComponent();
		applylang();
		edate.SelectedDate = DateTime.Today;
		edate.SelectedDateChanged += (_, _) => refresh();
		esys.SelectionChanged += (_, _) => refresh();
		btoday.Click += (_, _) => {
			edate.SelectedDate = DateTime.Today;
			refresh();
		};
		bcopy.Click += (_, _) => copyout();
		bclose.Click += (_, _) => Close();
		WindowEsc.Attach(this);
		refresh();
	}

	void applylang() {
		loading = true;
		Title = Loc.T("wincal.title");
		lbhint.Text = Loc.T("wincal.hint");
		lbdate.Text = Loc.T("wincal.date");
		btoday.Content = Loc.T("wincal.today");
		lbsys.Text = Loc.T("wincal.sys");
		bcopy.Content = Loc.T("texttool.copy");
		bclose.Content = Loc.T("imgconv.close");
		var keep = (esys.SelectedItem as ComboBoxItem)?.Tag as string;
		esys.Items.Clear();
		foreach (var one in WinCal.Systems()) {
			var item = new ComboBoxItem { Content = Loc.T(one.Key), Tag = one.Id };
			esys.Items.Add(item);
			if (keep != null && keep == one.Id) esys.SelectedItem = item;
		}
		if (esys.SelectedIndex < 0 && esys.Items.Count > 0) esys.SelectedIndex = 0;
		loading = false;
	}

	void refresh() {
		if (loading) return;
		var date = edate.SelectedDate ?? DateTime.Today;
		var id = (esys.SelectedItem as ComboBoxItem)?.Tag as string;
		try {
			var info = WinCal.Query(date, id, WinCal.LangTag(Loc.Lang));
			eout.Text = format(info);
			lbstat.Text = "";
		}
		catch (Exception ex) {
			eout.Text = "";
			lbstat.Text = Loc.T("texttool.fail", ex.Message);
		}
	}

	static string format(WinCalInfo info) {
		var sb = new StringBuilder();
		if (!string.IsNullOrEmpty(info.Ganzhi))
			sb.AppendLine(Loc.T("wincal.ganzhi", info.Ganzhi));
		sb.AppendLine(Loc.T("wincal.era", info.Era, info.EraNum));
		sb.AppendLine(Loc.T("wincal.year", info.Year, info.YearNum));
		var leap = "";
		if (info.LeapMonth) leap = Loc.T("wincal.leap");
		else if (info.MonthCount > 12) leap = Loc.T("wincal.noleap");
		sb.AppendLine(Loc.T("wincal.month", info.Month, info.MonthNum, info.MonthCount, leap));
		sb.AppendLine(Loc.T("wincal.day", info.Day, info.DayNum));
		sb.AppendLine(Loc.T("wincal.week", info.Week));
		sb.Append(Loc.T("wincal.greg", info.Gregorian));
		return sb.ToString();
	}

	void copyout() {
		try { Clipboard.SetText(eout.Text ?? ""); }
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
