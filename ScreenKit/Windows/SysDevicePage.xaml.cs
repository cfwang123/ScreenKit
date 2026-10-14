using System.Text;
using System.Windows.Automation;

namespace ScreenKit;

/// <summary>系统信息窗口中可复用的设备列表页。</summary>
public partial class SysDevicePage : UserControl {
	List<UsbDev> all = new();
	string nonekey;
	string countkey;

	internal event Action RefreshRequested;

	internal string SelectedId => (lvdev.SelectedItem as UsbDev)?.InstanceId;

	public SysDevicePage() {
		InitializeComponent();
		brefresh.Click += (_, _) => RefreshRequested?.Invoke();
		bcopy.Click += (_, _) => copy();
		efilter.TextChanged += (_, _) => show(SelectedId);
		lvdev.SelectionChanged += (_, _) => lvprop.ItemsSource = (lvdev.SelectedItem as UsbDev)?.Props;
	}

	internal void Init(string prefix, string hintKey, string noneKey, string countKey) {
		nonekey = noneKey;
		countkey = countKey;
		lbhint.Text = Loc.T(hintKey);
		lbfilter.Text = Loc.T("usb.filter");
		efilter.ToolTip = Loc.T("dev.filter.tip");
		colname.Header = Loc.T("usb.col.name");
		colkind.Header = Loc.T("dev.col.kind");
		colinfo.Header = Loc.T("dev.col.info");
		colstatus.Header = Loc.T("usb.col.status");
		colprop.Header = Loc.T("usb.col.prop");
		colvalue.Header = Loc.T("usb.col.value");
		ToolBtnUi.Set(brefresh, ToolBtnUi.Swap, Loc.T("usb.refresh"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("usb.copy"));
		AutomationProperties.SetAutomationId(efilter, prefix + ".filter");
		AutomationProperties.SetAutomationId(brefresh, prefix + ".refresh");
		AutomationProperties.SetAutomationId(bcopy, prefix + ".copy");
		AutomationProperties.SetAutomationId(lvdev, prefix + ".list");
		AutomationProperties.SetAutomationId(lvprop, prefix + ".detail");
		AutomationProperties.SetAutomationId(lbstat, prefix + ".stat");
	}

	internal void Loading() => lbstat.Text = Loc.T("sys.loading");

	internal void Failed(string error) {
		all = new List<UsbDev>();
		lvdev.ItemsSource = all;
		lvprop.ItemsSource = null;
		lbstat.Text = Loc.T("usb.fail", error);
	}

	internal void SetRows(List<UsbDev> rows, string keepId) {
		all = rows ?? new List<UsbDev>();
		show(keepId);
	}

	void show(string keepId) {
		var q = (efilter.Text ?? "").Trim().ToLowerInvariant();
		var rows = q.Length == 0 ? all : all.Where(x =>
			(x.Filter ?? "").IndexOf(q, StringComparison.Ordinal) >= 0).ToList();
		lvdev.ItemsSource = rows;
		UsbDev selected = null;
		if (!string.IsNullOrEmpty(keepId)) {
			foreach (var row in rows) {
				if (row.InstanceId == keepId) { selected = row; break; }
			}
		}
		if (selected == null && rows.Count > 0) selected = rows[0];
		if (selected != null) {
			lvdev.SelectedItem = selected;
			lvdev.ScrollIntoView(selected);
		}
		else lvprop.ItemsSource = null;
		lbstat.Text = rows.Count == 0 ? Loc.T(nonekey) : Loc.T(countkey, rows.Count);
	}

	void copy() {
		var row = lvdev.SelectedItem as UsbDev;
		if (row?.Props == null || row.Props.Count == 0) return;
		var sb = new StringBuilder();
		foreach (var prop in row.Props)
			sb.Append(prop.Name).Append('\t').Append(prop.Value).Append('\n');
		try {
			Clipboard.SetText(sb.ToString());
			lbstat.Text = Loc.T("usb.copied");
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("usb.fail", ex.Message);
		}
	}
}
