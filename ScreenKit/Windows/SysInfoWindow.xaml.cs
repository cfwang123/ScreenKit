using System.Text;

namespace ScreenKit;

sealed class SysBlock {
	public string Title { get; set; }
	public List<UsbPair> Items { get; set; }
}

/// <summary>工具 → 系统信息：系统、USB、串口、打印机和声音设备。</summary>
public partial class SysInfoWindow : Window {
	List<UsbPair> sysrows = new();
	List<UsbDev> usball = new();
	List<UsbDev> comall = new();
	List<UsbDev> prnall = new();
	List<UsbDev> audall = new();
	int sysgen;
	int usbgen;
	int comgen;
	int prngen;
	int audgen;
	bool dead;

	public SysInfoWindow() {
		InitializeComponent();
		applylang();
		initev();
		WindowEsc.Attach(this, Close);
		Closed += (_, _) => dead = true;
		lbsysstat.Text = Loc.T("sys.loading");
		lbusbstat.Text = Loc.T("sys.loading");
		lbcomstat.Text = Loc.T("sys.loading");
		lbprnstat.Text = Loc.T("sys.loading");
		lbaudstat.Text = Loc.T("sys.loading");
		reloadsys();
		reloadusb();
		reloadcom();
		reloadprn();
		reloadaud();
	}

	void initev() {
		bsysrefresh.Click += (_, _) => reloadsys();
		bsyscopy.Click += (_, _) => copypairs(sysrows, lbsysstat);
		busbrefresh.Click += (_, _) => reloadusb();
		busbcopy.Click += (_, _) => copydev(lvusb, lbusbstat);
		bcomrefresh.Click += (_, _) => reloadcom();
		bcomcopy.Click += (_, _) => copydev(lvcom, lbcomstat);
		bprnrefresh.Click += (_, _) => reloadprn();
		bprncopy.Click += (_, _) => copydev(lvprn, lbprnstat);
		baudrefresh.Click += (_, _) => reloadaud();
		baudcopy.Click += (_, _) => copydev(lvaud, lbaudstat);
		bclose.Click += (_, _) => Close();
		eusbfilter.TextChanged += (_, _) => showusb(keep(lvusb));
		ecomfilter.TextChanged += (_, _) => showcom(keep(lvcom));
		eprnfilter.TextChanged += (_, _) => showprn(keep(lvprn));
		eaudfilter.TextChanged += (_, _) => showaud(keep(lvaud));
		lvusb.SelectionChanged += (_, _) => showprops(lvusb, lvusbprop);
		lvcom.SelectionChanged += (_, _) => showprops(lvcom, lvcomprop);
		lvprn.SelectionChanged += (_, _) => showprops(lvprn, lvprnprop);
		lvaud.SelectionChanged += (_, _) => showprops(lvaud, lvaudprop);
	}

	void applylang() {
		Title = Loc.T("sys.title");
		tabsystem.Header = Loc.T("sys.tab.sys");
		tabusb.Header = Loc.T("sys.tab.usb");
		tabcom.Header = Loc.T("sys.tab.com");
		tabprn.Header = Loc.T("sys.tab.prn");
		tabaud.Header = Loc.T("sys.tab.aud");
		lbsyshint.Text = Loc.T("sys.hint");
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
		lbprnhint.Text = Loc.T("prn.hint");
		lbprnfilter.Text = Loc.T("usb.filter");
		eprnfilter.ToolTip = Loc.T("usb.filter.tip");
		colprnname.Header = Loc.T("usb.col.name");
		colprnport.Header = Loc.T("prn.p.port");
		colprndrv.Header = Loc.T("prn.p.driver");
		colprnstatus.Header = Loc.T("usb.col.status");
		colprnprop.Header = Loc.T("usb.col.prop");
		colprnval.Header = Loc.T("usb.col.value");
		ToolBtnUi.Set(bprnrefresh, ToolBtnUi.Swap, Loc.T("usb.refresh"));
		ToolBtnUi.Set(bprncopy, ToolBtnUi.Copy, Loc.T("usb.copy"));
		lbaudhint.Text = Loc.T("aud.hint");
		lbaudfilter.Text = Loc.T("usb.filter");
		eaudfilter.ToolTip = Loc.T("usb.filter.tip");
		colaudname.Header = Loc.T("usb.col.name");
		colaudkind.Header = Loc.T("aud.p.kind");
		colauddef.Header = Loc.T("aud.p.roles");
		colaudstatus.Header = Loc.T("usb.col.status");
		colaudprop.Header = Loc.T("usb.col.prop");
		colaudval.Header = Loc.T("usb.col.value");
		ToolBtnUi.Set(baudrefresh, ToolBtnUi.Swap, Loc.T("usb.refresh"));
		ToolBtnUi.Set(baudcopy, ToolBtnUi.Copy, Loc.T("usb.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	void reloadsys() {
		var gen = ++sysgen;
		lbsysstat.Text = Loc.T("sys.loading");
		Task.Run(() => {
			List<UsbPair> rows;
			string err = null;
			try { rows = SysInfo.Scan(); }
			catch (Exception ex) {
				rows = new List<UsbPair>();
				err = ex.Message;
			}
			apply(gen, () => sysgen, () => {
				sysrows = rows;
				icsys.ItemsSource = sysblocks(rows);
				lbsysstat.Text = err != null ? Loc.T("usb.fail", err)
					: sysrows.Count == 0 ? Loc.T("sys.none") : Loc.T("sys.count", sysrows.Count);
			});
		});
	}

	void reloadusb() {
		var id = keep(lvusb);
		var gen = ++usbgen;
		lbusbstat.Text = Loc.T("sys.loading");
		Task.Run(() => {
			List<UsbDev> rows = null;
			string err = null;
			try { rows = UsbList.Scan(); }
			catch (Exception ex) { err = ex.Message; }
			apply(gen, () => usbgen, () => {
				if (err != null) {
					usball = new List<UsbDev>();
					lvusb.ItemsSource = usball;
					lvusbprop.ItemsSource = null;
					lbusbstat.Text = Loc.T("usb.fail", err);
					return;
				}
				usball = rows;
				showusb(id);
			});
		});
	}

	void reloadcom() {
		var id = keep(lvcom);
		var gen = ++comgen;
		lbcomstat.Text = Loc.T("sys.loading");
		Task.Run(() => {
			List<UsbDev> rows = null;
			string err = null;
			try { rows = UsbList.ScanPorts(); }
			catch (Exception ex) { err = ex.Message; }
			apply(gen, () => comgen, () => {
				if (err != null) {
					comall = new List<UsbDev>();
					lvcom.ItemsSource = comall;
					lvcomprop.ItemsSource = null;
					lbcomstat.Text = Loc.T("usb.fail", err);
					return;
				}
				comall = rows;
				showcom(id);
			});
		});
	}

	void reloadprn() {
		var id = keep(lvprn);
		var gen = ++prngen;
		lbprnstat.Text = Loc.T("sys.loading");
		Task.Run(() => {
			List<UsbDev> rows = null;
			string err = null;
			try { rows = SysDev.ScanPrinters(); }
			catch (Exception ex) { err = ex.Message; }
			apply(gen, () => prngen, () => {
				if (err != null) {
					prnall = new List<UsbDev>();
					lvprn.ItemsSource = prnall;
					lvprnprop.ItemsSource = null;
					lbprnstat.Text = Loc.T("usb.fail", err);
					return;
				}
				prnall = rows;
				showprn(id);
			});
		});
	}

	void reloadaud() {
		var id = keep(lvaud);
		var gen = ++audgen;
		lbaudstat.Text = Loc.T("sys.loading");
		Task.Run(() => {
			List<UsbDev> rows = null;
			string err = null;
			try { rows = SysDev.ScanAudio(); }
			catch (Exception ex) { err = ex.Message; }
			apply(gen, () => audgen, () => {
				if (err != null) {
					audall = new List<UsbDev>();
					lvaud.ItemsSource = audall;
					lvaudprop.ItemsSource = null;
					lbaudstat.Text = Loc.T("usb.fail", err);
					return;
				}
				audall = rows;
				showaud(id);
			});
		});
	}

	void apply(int gen, Func<int> current, Action fill) {
		Dispatcher.BeginInvoke(new Action(() => {
			if (dead || gen != current()) return;
			fill();
		}));
	}

	void showusb(string keepId) =>
		showdev(usball, eusbfilter.Text, lvusb, lvusbprop, lbusbstat, keepId, "usb.none", "usb.count");

	void showcom(string keepId) =>
		showdev(comall, ecomfilter.Text, lvcom, lvcomprop, lbcomstat, keepId, "com.none", "com.count");

	void showprn(string keepId) =>
		showdev(prnall, eprnfilter.Text, lvprn, lvprnprop, lbprnstat, keepId, "prn.none", "prn.count");

	void showaud(string keepId) =>
		showdev(audall, eaudfilter.Text, lvaud, lvaudprop, lbaudstat, keepId, "aud.none", "aud.count");

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

	static List<SysBlock> sysblocks(List<UsbPair> rows) {
		var blocks = new List<SysBlock>();
		SysBlock cur = null;
		foreach (var p in rows) {
			var name = p.Name ?? "";
			var cut = name.IndexOf(" · ", StringComparison.Ordinal);
			var title = cut < 0 ? name : name.Substring(0, cut);
			var prop = cut < 0 ? name : name.Substring(cut + 3);
			if (cur == null || !string.Equals(cur.Title, title, StringComparison.Ordinal)) {
				cur = new SysBlock { Title = title, Items = new List<UsbPair>() };
				blocks.Add(cur);
			}
			cur.Items.Add(new UsbPair { Name = prop, Value = p.Value });
		}
		return blocks;
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
