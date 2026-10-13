using System.Diagnostics;

namespace ScreenKit;

/// <summary>工具 → 网络端口占用。</summary>
public partial class PortUseWindow : Window {
	List<PortUse.Row> rows = new();
	int load;

	public PortUseWindow() {
		InitializeComponent();
		applylang();
		brefresh.Click += (_, _) => reload();
		bopen.Click += (_, _) => openpath();
		efilter.TextChanged += (_, _) => paint();
		lv.SelectionChanged += (_, _) => showsel();
		WindowEsc.Attach(this);
		reload();
	}

	sealed class Item {
		public string Proto { get; set; }
		public string Local { get; set; }
		public int LocalPort { get; set; }
		public string Remote { get; set; }
		public string RemotePortText { get; set; }
		public string State { get; set; }
		public int Pid { get; set; }
		public string Proc { get; set; }
		public string Path { get; set; }
	}

	void applylang() {
		Title = Loc.T("port.title");
		lbhint.Text = Loc.T("port.hint");
		lbfilter.Text = Loc.T("port.filter");
		efilter.ToolTip = Loc.T("port.filter.tip");
		colproto.Header = Loc.T("port.col.proto");
		collocal.Header = Loc.T("port.col.local");
		collport.Header = Loc.T("port.col.lport");
		colremote.Header = Loc.T("port.col.remote");
		colrport.Header = Loc.T("port.col.rport");
		colstate.Header = Loc.T("port.col.state");
		colpid.Header = Loc.T("port.col.pid");
		colproc.Header = Loc.T("port.col.proc");
		ToolBtnUi.Set(brefresh, ToolBtnUi.Swap, Loc.T("port.refresh"));
		ToolBtnUi.Set(bopen, ToolBtnUi.Folder, Loc.T("port.open"));
		paint();
	}

	void reload() {
		int token = ++load;
		brefresh.IsEnabled = false;
		Task.Run(() => {
			string err = null;
			List<PortUse.Row> got = null;
			try { got = PortUse.List(); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (token != load) return;
				brefresh.IsEnabled = true;
				if (err != null) {
					lbstat.Text = err;
					return;
				}
				rows = got ?? new List<PortUse.Row>();
				paint();
			});
		});
	}

	void paint() {
		var q = (efilter.Text ?? "").Trim();
		var view = new List<Item>();
		foreach (var r in rows) {
			var it = toitem(r);
			if (q.Length == 0 || match(it, q)) view.Add(it);
		}
		lv.ItemsSource = view;
		lbstat.Text = Loc.T("port.stat", view.Count, rows.Count);
		showsel();
	}

	Item toitem(PortUse.Row r) {
		string state = "";
		if (!string.IsNullOrEmpty(r.State)) {
			var key = "port.state." + r.State;
			state = Loc.T(key);
			if (state == key) state = r.State;
		}
		return new Item {
			Proto = r.Proto,
			Local = r.Local,
			LocalPort = r.LocalPort,
			Remote = r.Remote,
			RemotePortText = r.RemotePort > 0 ? r.RemotePort.ToString() : "",
			State = state,
			Pid = r.Pid,
			Proc = r.Proc,
			Path = r.Path,
		};
	}

	static bool match(Item it, string q) {
		if (it.Proto.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (it.Local.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if ((it.Remote ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if ((it.State ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if ((it.Proc ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if ((it.Path ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (it.LocalPort.ToString().Contains(q)) return true;
		if ((it.RemotePortText ?? "").Contains(q)) return true;
		if (it.Pid.ToString().Contains(q)) return true;
		return false;
	}

	void showsel() {
		if (lv.SelectedItem is not Item it) {
			lbdetail.Text = "";
			bopen.IsEnabled = false;
			return;
		}
		var remote = "";
		if (!string.IsNullOrEmpty(it.Remote))
			remote = "  →  " + it.Remote + (it.RemotePortText.Length > 0 ? ":" + it.RemotePortText : "");
		lbdetail.Text = it.Proto + "  " + it.Local + ":" + it.LocalPort + remote
			+ (it.State.Length > 0 ? "  " + it.State : "")
			+ "\nPID " + it.Pid + "  " + it.Proc
			+ (string.IsNullOrEmpty(it.Path) ? "" : "\n" + it.Path);
		bopen.IsEnabled = !string.IsNullOrEmpty(it.Path) && File.Exists(it.Path);
	}

	void openpath() {
		if (lv.SelectedItem is not Item it || string.IsNullOrEmpty(it.Path) || !File.Exists(it.Path)) {
			lbstat.Text = Loc.T("port.nopath");
			return;
		}
		try {
			Process.Start(new ProcessStartInfo {
				FileName = "explorer.exe",
				Arguments = "/select,\"" + it.Path + "\"",
				UseShellExecute = true,
			});
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
		}
	}
}
