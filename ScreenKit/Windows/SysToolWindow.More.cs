using System.Text;

namespace ScreenKit;

public partial class SysToolWindow {
	List<SysTool.SvcRow> svcs = new();
	List<SysTool.PatchRow> patches = new();
	List<SysTool.StoreRow> stores = new();
	List<SysTool.ExtRow> exts = new();
	int svcgen;
	int patchgen;
	int storegen;
	int assocgen;

	sealed class SvcItem {
		public string Name { get; set; }
		public string Disp { get; set; }
		public string State { get; set; }
		public string Start { get; set; }
		public string Path { get; set; }
	}

	sealed class AssocItem {
		public string Ext { get; set; }
		public string Type { get; set; }
		public string App { get; set; }
		public string Exe { get; set; }
		public string Cmd { get; set; }
	}

	sealed class PatchItem {
		public string Id { get; set; }
		public string Date { get; set; }
		public string Desc { get; set; }
	}

	sealed class StoreItem {
		public string Name { get; set; }
		public string Ver { get; set; }
		public string Pub { get; set; }
	}

	void initmore() {
		bsvcrefresh.Click += (_, _) => loadsvc();
		bsvccopy.Click += (_, _) => copysvc();
		esvc.TextChanged += (_, _) => paintsvc();
		lvsvc.SelectionChanged += (_, _) => showsvc();
		bpatchrefresh.Click += (_, _) => loadpatch();
		bpatchcopy.Click += (_, _) => copypatch();
		epatch.TextChanged += (_, _) => paintpatch();
		lvpatch.SelectionChanged += (_, _) => showpatch();
		bstorerefresh.Click += (_, _) => loadstore();
		bstorecopy.Click += (_, _) => copystore();
		estore.TextChanged += (_, _) => paintstore();
		lvstore.SelectionChanged += (_, _) => showstore();
		bassocrefresh.Click += (_, _) => loadassoc();
		bassoccopy.Click += (_, _) => copyassoc();
		eassoc.TextChanged += (_, _) => paintassoc();
		lvassoc.SelectionChanged += (_, _) => showassoc();
	}

	void applymore() {
		tabsvc.Header = Loc.T("systool.tab.svc");
		tabpatch.Header = Loc.T("systool.tab.patch");
		tabstore.Header = Loc.T("systool.tab.store");
		tabassoc.Header = Loc.T("systool.tab.assoc");
		lbsvchint.Text = Loc.T("systool.hint.svc");
		lbpatchhint.Text = Loc.T("systool.hint.patch");
		lbstorehint.Text = Loc.T("systool.hint.store");
		lbassochint.Text = Loc.T("systool.hint.assoc");
		lbsvcfilter.Text = Loc.T("systool.filter");
		lbpatchfilter.Text = Loc.T("systool.filter");
		lbstorefilter.Text = Loc.T("systool.filter");
		lbassocfilter.Text = Loc.T("systool.filter");
		colsvcname.Header = Loc.T("systool.col.name");
		colsvcstate.Header = Loc.T("systool.col.state");
		colsvcstart.Header = Loc.T("systool.col.start");
		colsvcpath.Header = Loc.T("systool.col.path");
		colpatchid.Header = Loc.T("systool.col.hotfix");
		colpatchdate.Header = Loc.T("systool.col.date");
		colpatchdesc.Header = Loc.T("systool.col.desc");
		colstorename.Header = Loc.T("systool.col.name");
		colstorever.Header = Loc.T("systool.col.ver");
		colstorepub.Header = Loc.T("systool.col.pub");
		colassocext.Header = Loc.T("systool.col.ext");
		colassoctype.Header = Loc.T("systool.col.type");
		colassocapp.Header = Loc.T("systool.col.app");
		colassocexe.Header = Loc.T("systool.col.path");
		ToolBtnUi.Set(bsvcrefresh, ToolBtnUi.Swap, Loc.T("systool.refresh"));
		ToolBtnUi.Set(bsvccopy, ToolBtnUi.Copy, Loc.T("systool.copy"));
		ToolBtnUi.Set(bpatchrefresh, ToolBtnUi.Swap, Loc.T("systool.refresh"));
		ToolBtnUi.Set(bpatchcopy, ToolBtnUi.Copy, Loc.T("systool.copy"));
		ToolBtnUi.Set(bstorerefresh, ToolBtnUi.Swap, Loc.T("systool.refresh"));
		ToolBtnUi.Set(bstorecopy, ToolBtnUi.Copy, Loc.T("systool.copy"));
		ToolBtnUi.Set(bassocrefresh, ToolBtnUi.Swap, Loc.T("systool.refresh"));
		ToolBtnUi.Set(bassoccopy, ToolBtnUi.Copy, Loc.T("systool.copy"));
	}

	void loadsvc() {
		var n = ++svcgen;
		bsvcrefresh.IsEnabled = false;
		lbsvcstat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			List<SysTool.SvcRow> got = null;
			try { got = SysTool.Services(); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != svcgen) return;
				bsvcrefresh.IsEnabled = true;
				if (err != null) {
					lbsvcstat.Text = err;
					return;
				}
				svcs = got ?? new List<SysTool.SvcRow>();
				paintsvc();
			});
		});
	}

	void paintsvc() {
		var q = (esvc.Text ?? "").Trim();
		var view = new List<SvcItem>();
		foreach (var r in svcs) {
			var it = new SvcItem {
				Name = r.Name, Disp = r.Disp, State = label($"systool.svc.{r.State.ToLowerInvariant()}", r.State),
				Start = label($"systool.start.{r.Start.ToLowerInvariant()}", r.Start), Path = r.Path,
			};
			if (q.Length == 0 || has(q, it.Disp, it.Name, it.State, it.Start, it.Path)) view.Add(it);
		}
		fill(lvsvc, view);
		lbsvcstat.Text = Loc.T("systool.stat", view.Count, svcs.Count);
		showsvc();
	}

	void showsvc() {
		if (lvsvc.SelectedItem is not SvcItem it) {
			esvcdetail.Text = "";
			return;
		}
		esvcdetail.Text = join(it.Disp, it.Name, it.State, it.Start, it.Path);
	}

	void copysvc() {
		if (lvsvc.SelectedItem is not SvcItem it) return;
		setclip(join(it.Disp, it.Name, it.State, it.Start, it.Path));
	}

	void loadpatch() {
		var n = ++patchgen;
		bpatchrefresh.IsEnabled = false;
		lbpatchstat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			List<SysTool.PatchRow> got = null;
			try { got = SysTool.Patches(); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != patchgen) return;
				bpatchrefresh.IsEnabled = true;
				if (err != null) {
					lbpatchstat.Text = err;
					return;
				}
				patches = got ?? new List<SysTool.PatchRow>();
				paintpatch();
			});
		});
	}

	void paintpatch() {
		var q = (epatch.Text ?? "").Trim();
		var view = new List<PatchItem>();
		foreach (var r in patches) {
			var it = new PatchItem { Id = r.Id, Date = r.Date, Desc = r.Desc };
			if (q.Length == 0 || has(q, it.Id, it.Date, it.Desc)) view.Add(it);
		}
		fill(lvpatch, view);
		lbpatchstat.Text = Loc.T("systool.stat", view.Count, patches.Count);
		showpatch();
	}

	void showpatch() {
		if (lvpatch.SelectedItem is not PatchItem it) {
			epatchdetail.Text = "";
			return;
		}
		epatchdetail.Text = join(it.Id, it.Date, it.Desc);
	}

	void copypatch() {
		if (lvpatch.SelectedItem is not PatchItem it) return;
		setclip(join(it.Id, it.Date, it.Desc));
	}

	void loadstore() {
		var n = ++storegen;
		bstorerefresh.IsEnabled = false;
		lbstorestat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			List<SysTool.StoreRow> got = null;
			try { got = SysTool.Store(out err); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != storegen) return;
				bstorerefresh.IsEnabled = true;
				stores = got ?? new List<SysTool.StoreRow>();
				paintstore();
				if (err != null) lbstorestat.Text = err;
			});
		});
	}

	void paintstore() {
		var q = (estore.Text ?? "").Trim();
		var view = new List<StoreItem>();
		foreach (var r in stores) {
			var it = new StoreItem { Name = r.Name, Ver = r.Ver, Pub = r.Pub };
			if (q.Length == 0 || has(q, it.Name, it.Ver, it.Pub)) view.Add(it);
		}
		fill(lvstore, view);
		lbstorestat.Text = Loc.T("systool.stat", view.Count, stores.Count);
		showstore();
	}

	void showstore() {
		if (lvstore.SelectedItem is not StoreItem it) {
			estoredetail.Text = "";
			return;
		}
		estoredetail.Text = join(it.Name, it.Ver, it.Pub);
	}

	void copystore() {
		if (lvstore.SelectedItem is not StoreItem it) return;
		setclip(join(it.Name, it.Ver, it.Pub));
	}

	void loadassoc() {
		var n = ++assocgen;
		bassocrefresh.IsEnabled = false;
		lbassocstat.Text = Loc.T("systool.working");
		Task.Run(() => {
			string err = null;
			List<SysTool.ExtRow> got = null;
			try { got = SysTool.Exts(); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != assocgen) return;
				bassocrefresh.IsEnabled = true;
				if (err != null) {
					lbassocstat.Text = err;
					return;
				}
				exts = got ?? new List<SysTool.ExtRow>();
				paintassoc();
			});
		});
	}

	void paintassoc() {
		var q = (eassoc.Text ?? "").Trim();
		var view = new List<AssocItem>();
		foreach (var r in exts) {
			var it = new AssocItem { Ext = r.Ext, Type = r.Type, App = r.App, Exe = r.Exe, Cmd = r.Cmd };
			if (q.Length == 0 || has(q, it.Ext, it.Type, it.App, it.Exe, it.Cmd)) view.Add(it);
		}
		fill(lvassoc, view);
		lbassocstat.Text = Loc.T("systool.stat", view.Count, exts.Count);
		showassoc();
	}

	void showassoc() {
		if (lvassoc.SelectedItem is not AssocItem it) {
			eassocdetail.Text = "";
			return;
		}
		eassocdetail.Text = join(
			$"{it.Ext}  {it.Type}",
			$"{Loc.T("systool.assoc.app")}  {it.App}",
			$"{Loc.T("systool.assoc.exe")}  {it.Exe}",
			$"{Loc.T("systool.assoc.cmd")}  {it.Cmd}");
	}

	void copyassoc() {
		if (lvassoc.SelectedItem is not AssocItem it) return;
		setclip(join($"{it.Ext}  {it.Type}", it.App, it.Exe, it.Cmd));
	}

	static string label(string key, string raw) {
		var t = Loc.T(key);
		return t == key ? raw : t;
	}
}
