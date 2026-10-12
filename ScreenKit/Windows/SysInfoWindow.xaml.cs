using System.Text;

namespace ScreenKit;

/// <summary>工具 → 系统信息：系统、USB 设备和串口。</summary>
public partial class SysInfoWindow : Window {
	List<UsbPair> sysrows = new();
	List<UsbDev> usball = new();
	List<UsbDev> comall = new();

	public SysInfoWindow() {
		InitializeComponent();
		applylang();
		initev();
		WindowEsc.Attach(this, Close);
		reloadsys();
		reloadusb();
		reloadcom();
	}

	void initev() {
		bsysrefresh.Click += (_, _) => reloadsys();
		bsyscopy.Click += (_, _) => copypairs(sysrows, lbsysstat);
		busbrefresh.Click += (_, _) => reloadusb();
		busbcopy.Click += (_, _) => copydev(lvusb, lbusbstat);
		bcomrefresh.Click += (_, _) => reloadcom();
		bcomcopy.Click += (_, _) => copydev(lvcom, lbcomstat);
		bclose.Click += (_, _) => Close();
		eusbfilter.TextChanged += (_, _) => showusb(keep(lvusb));
		ecomfilter.TextChanged += (_, _) => showcom(keep(lvcom));
		lvusb.SelectionChanged += (_, _) => showprops(lvusb, lvusbprop);
		lvcom.SelectionChanged += (_, _) => showprops(lvcom, lvcomprop);
	}

	void applylang() {
		Title = Loc.T("sys.title");
		tabsystem.Header = Loc.T("sys.tab.sys");
		tabusb.Header = Loc.T("sys.tab.usb");
		tabcom.Header = Loc.T("sys.tab.com");
		lbsyshint.Text = Loc.T("sys.hint");
		colsysname.Header = Loc.T("usb.col.prop");
		colsysval.Header = Loc.T("usb.col.value");
		ToolBtnUi.Set(bsysrefresh, ToolBtnUi.Swap, Loc.T("usb.refresh"));
		ToolBtnUi.Set(bsyscopy, ToolBtnUi.Copy, Loc.T("usb.copy"));
		lbusbhint.Text = Loc.T("usb.hint");
		lbusbfilter.Text = Loc.T("usb.filter");
		eusbfilter.ToolTip = Loc.T("usb.filter.tip");
		colusbname.Header = Loc.T("usb.col.name");
		colusbvid.Header = Loc.T("usb.col.vid");
		colusbcls.Header = Loc.T("usb.col.cls");
		colusbstatus.Header = Loc.T("usb.col.status");
		colusbprop.Header = Loc.T("usb.col.prop");
		colusbval.Header = Loc.T("usb.col.value");
		ToolBtnUi.Set(busbrefresh, ToolBtnUi.Swap, Loc.T("usb.refresh"));
		ToolBtnUi.Set(busbcopy, ToolBtnUi.Copy, Loc.T("usb.copy"));
		lbcomhint.Text = Loc.T("com.hint");
		lbcomfilter.Text = Loc.T("usb.filter");
		ecomfilter.ToolTip = Loc.T("usb.filter.tip");
		colcomname.Header = Loc.T("usb.col.name");
		colcomport.Header = Loc.T("com.col.com");
		colcomcls.Header = Loc.T("usb.col.cls");
		colcomstatus.Header = Loc.T("usb.col.status");
		colcomprop.Header = Loc.T("usb.col.prop");
		colcomval.Header = Loc.T("usb.col.value");
		ToolBtnUi.Set(bcomrefresh, ToolBtnUi.Swap, Loc.T("usb.refresh"));
		ToolBtnUi.Set(bcomcopy, ToolBtnUi.Copy, Loc.T("usb.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	void reloadsys() {
		try { sysrows = SysInfo.Scan(); }
		catch (Exception ex) {
			sysrows = new List<UsbPair>();
			lvsys.ItemsSource = sysrows;
			lbsysstat.Text = Loc.T("usb.fail", ex.Message);
			return;
		}
		lvsys.ItemsSource = sysrows;
		lbsysstat.Text = sysrows.Count == 0 ? Loc.T("sys.none") : Loc.T("sys.count", sysrows.Count);
	}

	void reloadusb() {
		var id = keep(lvusb);
		try { usball = UsbList.Scan(); }
		catch (Exception ex) {
			usball = new List<UsbDev>();
			lvusb.ItemsSource = usball;
			lvusbprop.ItemsSource = null;
			lbusbstat.Text = Loc.T("usb.fail", ex.Message);
			return;
		}
		showusb(id);
	}

	void reloadcom() {
		var id = keep(lvcom);
		try { comall = UsbList.ScanPorts(); }
		catch (Exception ex) {
			comall = new List<UsbDev>();
			lvcom.ItemsSource = comall;
			lvcomprop.ItemsSource = null;
			lbcomstat.Text = Loc.T("usb.fail", ex.Message);
			return;
		}
		showcom(id);
	}

	void showusb(string keepId) =>
		showdev(usball, eusbfilter.Text, lvusb, lvusbprop, lbusbstat, keepId, "usb.none", "usb.count");

	void showcom(string keepId) =>
		showdev(comall, ecomfilter.Text, lvcom, lvcomprop, lbcomstat, keepId, "com.none", "com.count");

	static void showdev(List<UsbDev> all, string query, ListView list, ListView props, TextBlock stat, string keepId, string noneKey, string countKey) {
		var q = (query ?? "").Trim().ToLowerInvariant();
		var view = new List<UsbDev>();
		foreach (var row in all) {
			if (q.Length == 0 || (row.Filter != null && row.Filter.IndexOf(q, StringComparison.Ordinal) >= 0))
				view.Add(row);
		}
		list.ItemsSource = view;
		UsbDev sel = null;
		if (!string.IsNullOrEmpty(keepId)) {
			foreach (var row in view) {
				if (row.InstanceId == keepId) { sel = row; break; }
			}
		}
		if (sel == null && view.Count > 0) sel = view[0];
		if (sel != null) {
			list.SelectedItem = sel;
			list.ScrollIntoView(sel);
		}
		else showprops(list, props);
		stat.Text = view.Count == 0 ? Loc.T(noneKey) : Loc.T(countKey, view.Count);
	}

	static void showprops(ListView list, ListView props) {
		props.ItemsSource = (list.SelectedItem as UsbDev)?.Props;
	}

	static string keep(ListView list) => (list.SelectedItem as UsbDev)?.InstanceId;

	static void copydev(ListView list, TextBlock stat) {
		var dev = list.SelectedItem as UsbDev;
		if (dev == null || dev.Props == null || dev.Props.Count == 0) return;
		copypairs(dev.Props, stat);
	}

	static void copypairs(List<UsbPair> rows, TextBlock stat) {
		if (rows == null || rows.Count == 0) return;
		var sb = new StringBuilder();
		foreach (var p in rows)
			sb.Append(p.Name).Append('\t').Append(p.Value).Append('\n');
		try {
			Clipboard.SetText(sb.ToString());
			stat.Text = Loc.T("usb.copied");
		}
		catch (Exception ex) {
			stat.Text = Loc.T("usb.fail", ex.Message);
		}
	}
}
