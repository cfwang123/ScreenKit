using System.Diagnostics;

namespace ScreenKit;

/// <summary>输入法词库窗口：五笔 / 日语 / 韩语的用户词。程序旁 dict/ 下的文本是词库本体，
/// 导入导出都走这个窗口（Ctrl+Shift+F7、输入法托盘菜单、输入法设置窗都能打开）。</summary>
public partial class ImeDictWindow : Window {
	sealed class Item {
		public string Key { get; set; }
		public string Text { get; set; }
		public ImeDictEntry Src { get; set; }
	}

	List<ImeDictEntry> rows = new();
	string kind = "wubi";

	/// <summary>自测用：列表里现在有几条。</summary>
	internal int RowCount => rows.Count;

	public ImeDictWindow() {
		InitializeComponent();
		ekind.ItemsSource = new[] {
			Loc.T("ime.dict.kind.wubi"), Loc.T("ime.dict.kind.ja"), Loc.T("ime.dict.kind.ko")
		};
		ekind.SelectedIndex = 0;
		ekind.SelectionChanged += (_, _) => {
			var i = ekind.SelectedIndex;
			if (i < 0 || i >= ImeDict.Kinds.Length) return;
			kind = ImeDict.Kinds[i];
			applylang();
			if (IsLoaded) reload();
		};
		efilter.TextChanged += (_, _) => paint();
		bimport.Click += (_, _) => import();
		bexport.Click += (_, _) => export();
		bopen.Click += (_, _) => openfolder();
		bdel.Click += (_, _) => del();
		bclear.Click += (_, _) => clear();
		bclose.Click += (_, _) => Close();
		WindowEsc.Attach(this);		applylang();
		Loaded += (_, _) => {
			ImeDict.SyncAll();
			reload();
		};
	}

	void applylang() {
		Title = Loc.T("ime.dict.title");
		lbfilter.Text = Loc.T("ime.dict.filter");
		efilter.ToolTip = Loc.T("ime.dict.filter.tip");
		colkey.Header = Loc.T("ime.dict.col.key." + kind);
		colword.Header = Loc.T("ime.dict.col.word");
		colkey.Width = kind == "ko" ? 0 : 150;
		ToolBtnUi.Set(bimport, ToolBtnUi.Browse, Loc.T("ime.dict.import"));
		ToolBtnUi.Set(bexport, ToolBtnUi.Save, Loc.T("ime.dict.export"));
		ToolBtnUi.Set(bopen, ToolBtnUi.Folder, Loc.T("ime.dict.open"));
		ToolBtnUi.Set(bdel, ToolBtnUi.Delete, Loc.T("ime.dict.del"));
		ToolBtnUi.Set(bclear, ToolBtnUi.Clear, Loc.T("ime.dict.clear"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("close"));
		var hint = Loc.T("ime.dict.hint") + "\n" + Loc.T("ime.dict.file", ImeDict.FileOf(kind));
		lbhint.Text = hint;
		if (!ImeLex.DbInstalled) lbstat.Text = Loc.T("hangul.needdb");
	}

	/// <summary>只列 dict/ 文本里那份自定义词库：打字时选用过的词只影响候选排序，不写进文本，这里也不列。</summary>
	void reload() {
		int bad;
		var set = new HashSet<string>();
		foreach (var e in ImeDict.Read(kind, out bad)) set.Add((e.Key ?? "") + "\n" + e.Text);
		rows = new List<ImeDictEntry>();
		foreach (var e in ImeDict.List(kind)) {
			if (set.Contains((e.Key ?? "") + "\n" + e.Text)) rows.Add(e);
		}
		paint();
	}

	void paint() {
		var q = (efilter.Text ?? "").Trim();
		var view = new List<Item>();
		foreach (var e in rows) {
			if (q.Length > 0 && !hit(e, q)) continue;
			view.Add(new Item { Key = e.Key, Text = e.Text, Src = e });
		}
		lv.ItemsSource = view;
		lbcount.Text = Loc.T("ime.dict.count", view.Count, rows.Count);
		var ok = ImeLex.DbInstalled;
		bimport.IsEnabled = ok;
		bexport.IsEnabled = ok;
		bdel.IsEnabled = ok;
		bclear.IsEnabled = ok;
	}

	static bool hit(ImeDictEntry e, string q) {
		if (!string.IsNullOrEmpty(e.Key) && e.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (!string.IsNullOrEmpty(e.Text) && e.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return false;
	}

	void import() {
		if (!ImeLex.DbInstalled) return;
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("ime.dict.import"),
			Filter = Loc.T("ime.dict.filter.txt"),
		};
		if (dlg.ShowDialog(this) != true) return;
		int total, bad;
		int add = ImeDict.Import(kind, dlg.FileName, out total, out bad);
		reload();
		var msg = Loc.T("ime.dict.imported", add, total);
		if (bad > 0) msg += "  " + Loc.T("ime.dict.skipped", bad);
		lbstat.Text = msg;
	}

	void export() {
		if (!ImeLex.DbInstalled) return;
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T("ime.dict.export"),
			FileName = ImeDict.Kinds[Math.Max(ekind.SelectedIndex, 0)] + ".txt",
			Filter = Loc.T("ime.dict.filter.txt"),
			DefaultExt = ".txt",
		};
		if (dlg.ShowDialog(this) != true) return;
		int n = ImeDict.Export(kind, dlg.FileName);
		lbstat.Text = Loc.T("ime.dict.exported", n);
	}

	void openfolder() {
		try {
			var dir = ImeDict.DirOf;
			Directory.CreateDirectory(dir);
			var file = ImeDict.FileOf(kind);
			Process.Start(new ProcessStartInfo {
				FileName = "explorer.exe",
				Arguments = File.Exists(file) ? "/select,\"" + file + "\"" : "\"" + dir + "\"",
				UseShellExecute = true,
			});
		}
		catch (Exception ex) { lbstat.Text = ex.Message; }
	}

	void del() {
		if (lv.SelectedItem is not Item it || it.Src == null) return;
		ImeDict.Remove(kind, it.Src);
		reload();
		lbstat.Text = Loc.T("ime.dict.deleted", it.Text);
	}

	void clear() {
		if (!ImeLex.DbInstalled) return;
		var name = Loc.T("ime.dict.kind." + kind);
		if (MessageBox.Show(this, Loc.T("ime.dict.clearcfm", name), Loc.T("ime.dict.title"),
			MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
		ImeDict.Clear(kind);
		reload();
		lbstat.Text = Loc.T("ime.dict.cleared", name);
	}
}
