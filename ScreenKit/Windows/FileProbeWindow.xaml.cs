using System.Text;

namespace ScreenKit;

/// <summary>工具 → 文件 → 文件分析。概要，以及 EXE/DLL 的节、导入导出和托管类型。只读。</summary>
public partial class FileProbeWindow : Window {
	int gen;
	FileProbe.Report report;

	sealed class Item {
		public string Name { get; set; }
		public string Value { get; set; }
	}

	sealed class PeRow {
		public string A { get; set; }
		public string B { get; set; }
		public string C { get; set; }
	}

	public FileProbeWindow() {
		InitializeComponent();
		petabs.SelectionChanged += (_, e) => { if (e.Source == petabs) showpe(); };
		epeq.TextChanged += (_, _) => showpe();
		tabs.SelectionChanged += (_, e) => { if (e.Source == tabs) restat(); };
		applylang();
		bfile.Click += (_, _) => pick();
		bcopy.Click += (_, _) => copy();
		epath.KeyDown += (_, e) => {
			if (e.Key == Key.Enter) look();
		};
		AllowDrop = true;
		DragOver += (_, e) => {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		Drop += (_, e) => {
			if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
			epath.Text = files[0];
			look();
		};
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("probe.title");
		lbhint.Text = Loc.T("probe.hint");
		colname.Header = Loc.T("probe.col.name");
		colvalue.Header = Loc.T("probe.col.value");
		tabinfo.Header = Loc.T("probe.tab.info");
		tabpe.Header = Loc.T("probe.tab.pe");
		tabimg.Header = Loc.T("probe.tab.img");
		tabaudio.Header = Loc.T("probe.tab.audio");
		tabvideo.Header = Loc.T("probe.tab.video");
		set2cols(lvimg);
		set2cols(lvaudio);
		set2cols(lvvideo);
		tabpehead.Header = Loc.T("probe.pe.view.head");
		tabpesec.Header = Loc.T("probe.pe.view.sec");
		tabpeimp.Header = Loc.T("probe.pe.view.imp");
		tabpeexp.Header = Loc.T("probe.pe.view.exp");
		tabpenet.Header = Loc.T("probe.pe.view.net");
		tabpeman.Header = Loc.T("probe.pe.view.man");
		lbpeq.Text = Loc.T("probe.pe.filter");
		ToolBtnUi.Set(bfile, ToolBtnUi.Browse, Loc.T("probe.file"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("probe.copy"));
		if (report != null) paint(null);
	}

	ListView peview(int view) {
		switch (view) {
			case 0: return lvhead;
			case 1: return lvsec;
			case 2: return lvimp;
			case 3: return lvexp;
			default: return lvnet;
		}
	}

	void set2cols(ListView list) {
		if (list?.View is not GridView gv || gv.Columns.Count < 2) return;
		gv.Columns[0].Header = Loc.T("probe.col.name");
		gv.Columns[1].Header = Loc.T("probe.col.value");
		gv.Columns[0].Width = 160;
		gv.Columns[1].Width = 560;
	}

	void pick() {
		var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("probe.file") };
		if (dlg.ShowDialog(this) != true) return;
		epath.Text = dlg.FileName;
		look();
	}

	void look() {
		var path = epath.Text ?? "";
		var n = ++gen;
		bfile.IsEnabled = false;
		lbstat.Text = Loc.T("probe.working");
		Task.Run(() => {
			string err = null;
			FileProbe.Report got = null;
			try { got = FileProbe.Look(path, out err); }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != gen) return;
				bfile.IsEnabled = true;
				report = got;
				paint(err);
			});
		});
	}

	void paint(string err) {
		var view = new List<Item>();
		var img = new List<Item>();
		var media = new List<Item>();
		var video = false;
		if (report != null) {
			foreach (var r in report.Rows) {
				var it = new Item { Name = Loc.T(r.Key), Value = rowvalue(r) };
				if (r.Key.StartsWith("probe.img.", StringComparison.Ordinal)) img.Add(it);
				else if (r.Key.StartsWith("probe.media.", StringComparison.Ordinal)) {
					if (isvideokey(r.Key)) video = true;
					media.Add(it);
				}
				else view.Add(it);
			}
		}
		lv.ItemsSource = view;
		lvimg.ItemsSource = img;
		lvaudio.ItemsSource = video ? null : media;
		lvvideo.ItemsSource = video ? media : null;
		tabimg.Visibility = img.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		tabaudio.Visibility = !video && media.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		tabvideo.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
		var pe = report != null && (report.Kind == "exe" || report.Kind == "dll");
		tabpe.Visibility = pe ? Visibility.Visible : Visibility.Collapsed;
		if (tabs.SelectedItem is TabItem cur && cur.Visibility != Visibility.Visible) tabs.SelectedIndex = 0;
		showpe();
		if (err == "empty") lbstat.Text = Loc.T("probe.empty");
		else if (err == "missing") lbstat.Text = Loc.T("probe.missing");
		else if (err != null) lbstat.Text = err;
		else restat();
	}

	static bool isvideokey(string key) =>
		key == "probe.media.res" || key == "probe.media.vcodec" ||
		key == "probe.media.fps" || key == "probe.media.vrate";

	static string rowvalue(FileProbe.Row r) {
		var value = r.Value;
		if (r.Key == "probe.magic") value = kind(value);
		else if (r.Key == "probe.match") value = value == "yes" ? Loc.T("probe.yes") : Loc.T("probe.no");
		else if (r.Key == "probe.ver.type") value = filetype(value);
		else if (r.Key == "probe.pe.arch") value = arch(value);
		else if (r.Key == "probe.pe.subsys") value = subsys(value);
		else if (r.Key == "probe.pe.managed") value = value == "yes" ? Loc.T("probe.yes") : Loc.T("probe.no");
		else if (r.Key == "probe.enc.bom" && value == "none") value = Loc.T("probe.enc.none");
		else if (r.Key == "probe.enc.guess") value = guess(value);
		else if (r.Key == "probe.img.color") value = imgcolor(value);
		else if (r.Key == "probe.img.jpg.mode") value = value == "prog" ? Loc.T("probe.img.jpg.prog") : Loc.T("probe.img.jpg.base");
		else if (r.Key == "probe.img.webp.mode") value = value == "lossless" ? Loc.T("probe.img.webp.lossless") : Loc.T("probe.img.webp.lossy");
		else if (r.Key == "probe.img.interlace") value = value == "yes" ? Loc.T("probe.pe.yes") : Loc.T("probe.pe.no");
		return value;
	}

	void showpe() {
		var view = petabs.SelectedIndex;
		if (view < 0) view = 0;
		var dump = report == null ? null : report.Pe;
		if (view == 5) {
			epetext.Visibility = Visibility.Visible;
			showman(dump);
			return;
		}
		var lvpe = peview(view);
		lvpe.Visibility = Visibility.Visible;
		epetext.Visibility = Visibility.Collapsed;
		var rows = new List<PeRow>();
		if (dump == null) {
			lvpe.ItemsSource = rows;
			return;
		}
		if (dump.Sections.Count == 0 && dump.Error.Length > 0) {
			setcols(lvpe, Loc.T("probe.col.name"), Loc.T("probe.col.value"), "", 160, 560, 0);
			rows.Add(new PeRow { A = Loc.T("probe.pe.bad"), B = dump.Error == "bad" ? "" : dump.Error });
			lvpe.ItemsSource = filt(rows);
			return;
		}
		if (view == 0) {
			setcols(lvpe, Loc.T("probe.col.name"), Loc.T("probe.col.value"), "", 160, 560, 0);
			foreach (var h in dump.Head)
				rows.Add(new PeRow { A = headname(h.Key), B = headval(h.Key, h.Value) });
		}
		else if (view == 1) {
			setcols(lvpe, Loc.T("probe.pe.sec"), Loc.T("probe.pe.col.range"), Loc.T("probe.pe.col.flags"), 120, 420, 220);
			foreach (var s in dump.Sections)
				rows.Add(new PeRow { A = s.Name, B = s.Range, C = s.Flags });
		}
		else if (view == 2) {
			setcols(lvpe, Loc.T("probe.pe.imp.dll"), Loc.T("probe.pe.imp.fn"), Loc.T("probe.pe.col.note"), 180, 320, 200);
			foreach (var i in dump.Imports)
				rows.Add(new PeRow { A = i.Dll, B = i.Name, C = note(i.Note) });
			if (dump.ImportCut) rows.Add(new PeRow { A = Loc.T("probe.pe.more") });
		}
		else if (view == 3) {
			setcols(lvpe, Loc.T("probe.pe.exp.name"), Loc.T("probe.pe.col.ord"), Loc.T("probe.pe.col.note"), 280, 80, 360);
			foreach (var e in dump.Exports) {
				var name = e.Name.Length == 0 ? $"#{e.Ord}" : e.Name;
				rows.Add(new PeRow { A = name, B = e.Ord, C = e.Note });
			}
			if (dump.ExportCut) rows.Add(new PeRow { A = Loc.T("probe.pe.more") });
		}
		else {
			setcols(lvpe, Loc.T("probe.pe.col.kind"), Loc.T("probe.pe.col.a"), Loc.T("probe.pe.col.b"), 100, 280, 380);
			if (!dump.Managed) rows.Add(new PeRow { A = Loc.T("probe.pe.native") });
			else {
				if (dump.Runtime.Length > 0) rows.Add(new PeRow { A = kindlab("clr"), B = dump.Runtime });
				if (dump.Framework.Length > 0) rows.Add(new PeRow { A = kindlab("fx"), B = dump.Framework });
				if (dump.Platform.Length > 0) rows.Add(new PeRow { A = kindlab("flag"), B = dump.Platform });
				if (dump.NetFail.Length > 0) rows.Add(new PeRow { A = Loc.T("probe.pe.netfail"), B = dump.NetFail });
				else {
					foreach (var n in dump.Net)
						rows.Add(new PeRow { A = kindlab(n.Kind), B = n.A, C = n.B });
					if (dump.NetCut) rows.Add(new PeRow { A = Loc.T("probe.pe.more") });
				}
			}
		}
		lvpe.ItemsSource = filt(rows);
	}

	void showman(FileProbe.PeDump dump) {
		var text = dump == null ? "" : dump.Manifest ?? "";
		if (dump != null && dump.Sections.Count == 0 && dump.Error.Length > 0) text = Loc.T("probe.pe.bad");
		else if (text.Length == 0) text = Loc.T("probe.pe.noman");
		var q = (epeq.Text ?? "").Trim();
		if (q.Length > 0 && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) text = "";
		epetext.Text = text;
	}

	List<PeRow> filt(List<PeRow> rows) {
		var q = (epeq.Text ?? "").Trim();
		if (q.Length == 0) return rows;
		var keep = new List<PeRow>();
		foreach (var r in rows) {
			if (has(r.A, q) || has(r.B, q) || has(r.C, q)) keep.Add(r);
		}
		return keep;
	}

	static bool has(string s, string q) {
		return s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static void setcols(ListView lv, string a, string b, string c, double wa, double wb, double wc) {
		if (lv?.View is not GridView gv || gv.Columns.Count < 3) return;
		gv.Columns[0].Header = a;
		gv.Columns[1].Header = b;
		gv.Columns[2].Header = c;
		gv.Columns[0].Width = wa;
		gv.Columns[1].Width = wb;
		gv.Columns[2].Width = wc;
	}

	static string headname(string key) {
		var k = "probe.pe." + key;
		var t = Loc.T(k);
		return t == k ? key : t;
	}

	static string headval(string key, string val) {
		if (key == "sign" && val == "unsigned") return Loc.T("probe.pe.unsigned");
		if (key == "pack" && val == "yes") return Loc.T("probe.pe.pack.yes");
		if (key == "pack" && val == "no") return Loc.T("probe.pe.pack.no");
		if (key == "managed" && (val == "no" || val == "yes")) return val == "yes" ? Loc.T("probe.pe.yes") : Loc.T("probe.pe.no");
		return val ?? "";
	}

	static string note(string n) {
		if (n == "delay") return Loc.T("probe.pe.delay");
		if (n == "ord") return Loc.T("probe.pe.ordinal");
		if (n == "delay,ord") return $"{Loc.T("probe.pe.delay")} {Loc.T("probe.pe.ordinal")}";
		return n ?? "";
	}

	static string kindlab(string k) {
		var key = "probe.pe.kind." + k;
		var t = Loc.T(key);
		return t == key ? k : t;
	}

	void restat() {
		int n;
		var sel = tabs.SelectedItem;
		if (sel == tabpe && tabpe.Visibility == Visibility.Visible) {
			var view = petabs.SelectedIndex;
			if (view < 0) view = 0;
			if (view == 5) n = string.IsNullOrEmpty(epetext.Text) ? 0 : 1;
			else n = peview(view)?.Items.Count ?? 0;
		}
		else if (sel == tabimg) n = lvimg.Items.Count;
		else if (sel == tabaudio) n = lvaudio.Items.Count;
		else if (sel == tabvideo) n = lvvideo.Items.Count;
		else n = lv.Items.Count;
		lbstat.Text = Loc.T("systool.stat", n, n);
	}

	void copy() {
		var sb = new StringBuilder();
		if (tabs.SelectedItem == tabpe && tabpe.Visibility == Visibility.Visible) {
			var view = petabs.SelectedIndex;
			if (view < 0) view = 0;
			if (view == 5) sb.Append(epetext.Text);
			else if (peview(view)?.ItemsSource is System.Collections.IEnumerable items) {
				foreach (var it in items) {
					if (it is not PeRow row) continue;
					if (sb.Length > 0) sb.Append('\n');
					sb.Append(row.A ?? "");
					sb.Append('\t');
					sb.Append(row.B ?? "");
					if (!string.IsNullOrEmpty(row.C)) {
						sb.Append('\t');
						sb.Append(row.C);
					}
				}
			}
		}
		else if (tabs.SelectedItem == tabimg) appenditems(sb, lvimg.ItemsSource as IEnumerable<Item>);
		else if (tabs.SelectedItem == tabaudio) appenditems(sb, lvaudio.ItemsSource as IEnumerable<Item>);
		else if (tabs.SelectedItem == tabvideo) appenditems(sb, lvvideo.ItemsSource as IEnumerable<Item>);
		else appenditems(sb, lv.ItemsSource as IEnumerable<Item>);
		if (sb.Length == 0) return;
		try { Clipboard.SetText(sb.ToString()); } catch { }
	}

	static void appenditems(StringBuilder sb, IEnumerable<Item> list) {
		if (list == null) return;
		foreach (var it in list) {
			if (sb.Length > 0) sb.Append('\n');
			sb.Append(it.Name);
			sb.Append('\t');
			sb.Append(it.Value);
		}
	}

	static string kind(string id) {
		var key = $"probe.kind.{id}";
		var t = Loc.T(key);
		return t == key ? id : t;
	}

	static string guess(string id) {
		var key = $"probe.enc.{id}";
		var t = Loc.T(key);
		return t == key ? id : t;
	}

	static string imgcolor(string id) {
		var key = $"probe.img.color.{id}";
		var t = Loc.T(key);
		return t == key ? id : t;
	}

	static string filetype(string id) {
		var key = $"probe.ver.type.{id}";
		var t = Loc.T(key);
		return t == key ? id : t;
	}

	static string arch(string id) {
		if (id.Length == 0) return id;
		if (id.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return id;
		var key = $"probe.pe.arch.{id}";
		var t = Loc.T(key);
		return t == key ? id : t;
	}

	static string subsys(string id) {
		if (id.Length == 0) return id;
		var key = $"probe.pe.subsys.{id}";
		var t = Loc.T(key);
		return t == key ? id : t;
	}
}
