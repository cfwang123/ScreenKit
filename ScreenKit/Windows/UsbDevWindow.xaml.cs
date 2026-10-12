using System.Text;

namespace ScreenKit;

/// <summary>工具 → USB 设备：列出已连接设备，点一项查看参数。</summary>
public partial class UsbDevWindow : Window {
	List<UsbDev> all = new();

	public UsbDevWindow() {
		InitializeComponent();
		applylang();
		initev();
		WindowEsc.Attach(this, Close);
		reload();
	}

	void initev() {
		brefresh.Click += (_, _) => reload();
		bcopy.Click += (_, _) => copy();
		bclose.Click += (_, _) => Close();
		efilter.TextChanged += (_, _) => showlist(keep());
		lv.SelectionChanged += (_, _) => showprops();
	}

	void applylang() {
		Title = Loc.T("usb.title");
		lbhint.Text = Loc.T("usb.hint");
		lbfilter.Text = Loc.T("usb.filter");
		efilter.ToolTip = Loc.T("usb.filter.tip");
		colname.Header = Loc.T("usb.col.name");
		colvid.Header = Loc.T("usb.col.vid");
		colcls.Header = Loc.T("usb.col.cls");
		colstatus.Header = Loc.T("usb.col.status");
		colprop.Header = Loc.T("usb.col.prop");
		colval.Header = Loc.T("usb.col.value");
		ToolBtnUi.Set(brefresh, ToolBtnUi.Swap, Loc.T("usb.refresh"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("usb.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	void reload() {
		var id = keep();
		try { all = UsbList.Scan(); }
		catch (Exception ex) {
			all = new List<UsbDev>();
			lv.ItemsSource = all;
			lvprop.ItemsSource = null;
			lbstat.Text = Loc.T("usb.fail", ex.Message);
			return;
		}
		showlist(id);
	}

	void showlist(string keepId) {
		var q = (efilter.Text ?? "").Trim().ToLowerInvariant();
		var view = new List<UsbDev>();
		foreach (var row in all) {
			if (q.Length == 0 || (row.Filter != null && row.Filter.IndexOf(q, StringComparison.Ordinal) >= 0))
				view.Add(row);
		}
		lv.ItemsSource = view;
		UsbDev sel = null;
		if (!string.IsNullOrEmpty(keepId)) {
			foreach (var row in view) {
				if (row.InstanceId == keepId) { sel = row; break; }
			}
		}
		if (sel == null && view.Count > 0) sel = view[0];
		if (sel != null) {
			lv.SelectedItem = sel;
			lv.ScrollIntoView(sel);
		}
		else showprops();
		lbstat.Text = view.Count == 0 ? Loc.T("usb.none") : Loc.T("usb.count", view.Count);
	}

	void showprops() {
		lvprop.ItemsSource = (lv.SelectedItem as UsbDev)?.Props;
	}

	string keep() => (lv.SelectedItem as UsbDev)?.InstanceId;

	void copy() {
		var dev = lv.SelectedItem as UsbDev;
		if (dev == null || dev.Props == null || dev.Props.Count == 0) return;
		var sb = new StringBuilder();
		foreach (var p in dev.Props)
			sb.Append(p.Name).Append('\t').Append(p.Value).Append('\n');
		try {
			Clipboard.SetText(sb.ToString());
			lbstat.Text = Loc.T("usb.copied");
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("usb.fail", ex.Message);
		}
	}
}
