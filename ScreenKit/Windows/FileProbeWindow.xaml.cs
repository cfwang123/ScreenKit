using System.Text;

namespace ScreenKit;

/// <summary>工具 → 文件 → 文件分析。魔数、版本、图片、媒体、文本编码。只读。</summary>
public partial class FileProbeWindow : Window {
	int gen;
	FileProbe.Report report;

	sealed class Item {
		public string Name { get; set; }
		public string Value { get; set; }
	}

	public FileProbeWindow() {
		InitializeComponent();
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
		ToolBtnUi.Set(bfile, ToolBtnUi.Browse, Loc.T("probe.file"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("probe.copy"));
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
		if (report != null) {
			foreach (var r in report.Rows) {
				var value = r.Value;
				if (r.Key == "probe.magic") value = kind(value);
				else if (r.Key == "probe.match") value = value == "yes" ? Loc.T("probe.yes") : Loc.T("probe.no");
				else if (r.Key == "probe.ver.type") value = filetype(value);
				else if (r.Key == "probe.pe.arch") value = arch(value);
				else if (r.Key == "probe.pe.subsys") value = subsys(value);
				else if (r.Key == "probe.pe.managed") value = value == "yes" ? Loc.T("probe.yes") : Loc.T("probe.no");
				else if (r.Key == "probe.enc.bom" && value == "none") value = Loc.T("probe.enc.none");
				else if (r.Key == "probe.enc.guess") value = guess(value);
				view.Add(new Item { Name = Loc.T(r.Key), Value = value });
			}
		}
		lv.ItemsSource = view;
		if (err == "empty") lbstat.Text = Loc.T("probe.empty");
		else if (err == "missing") lbstat.Text = Loc.T("probe.missing");
		else if (err != null) lbstat.Text = err;
		else lbstat.Text = Loc.T("systool.stat", view.Count, view.Count);
	}

	void copy() {
		if (lv.ItemsSource is not IEnumerable<Item> items) return;
		var sb = new StringBuilder();
		foreach (var it in items) {
			if (sb.Length > 0) sb.Append('\n');
			sb.Append(it.Name);
			sb.Append('\t');
			sb.Append(it.Value);
		}
		if (sb.Length == 0) return;
		try { Clipboard.SetText(sb.ToString()); } catch { }
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
