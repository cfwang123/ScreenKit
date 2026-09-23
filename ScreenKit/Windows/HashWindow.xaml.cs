using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ScreenKit;

sealed class HashViewRow : INotifyPropertyChanged {
	public string Path { get; set; }
	public string Name { get; set; }
	public string SizeText { get; set; }
	public string Md5 { get; set; }
	public string Sha1 { get; set; }
	public string Sha256 { get; set; }
	string match;
	public string Match { get => match; set { match = value; On(); } }
	public event PropertyChangedEventHandler PropertyChanged;
	void On([CallerMemberName] string n = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>工具 → 校验哈希。</summary>
public partial class HashWindow : Window {
	readonly ObservableCollection<HashViewRow> rows = new();
	int seq;

	public HashWindow() {
		InitializeComponent();
		lv.ItemsSource = rows;
		applylang();
		initev();
		WindowEsc.Attach(this);
	}

	void initev() {
		badd.Click += (_, _) => addfiles();
		bclear.Click += (_, _) => { rows.Clear(); lbstat.Text = ""; };
		bcopy.Click += (_, _) => copy();
		bclose.Click += (_, _) => Close();
		ecmp.TextChanged += (_, _) => applymatch();
		PreviewDragOver += ondrag;
		Drop += ondrop;
	}

	void applylang() {
		Title = Loc.T("hash.title");
		lbcmp.Text = Loc.T("hash.compare");
		ecmp.ToolTip = Loc.T("hash.compare.tip");
		colname.Header = Loc.T("hash.col.name");
		colsize.Header = Loc.T("hash.col.size");
		colst.Header = Loc.T("hash.col.match");
		ToolBtnUi.Set(badd, ToolBtnUi.Add, Loc.T("hash.add"));
		ToolBtnUi.Set(bclear, ToolBtnUi.Clear, Loc.T("imgconv.clear"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("hash.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	void addfiles() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("hash.add"),
			Multiselect = true,
			Filter = Loc.T("rename.filter"),
		};
		if (ofd.ShowDialog(this) != true) return;
		addpaths(ofd.FileNames);
	}

	void ondrag(object sender, DragEventArgs e) {
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (files != null) addpaths(files);
	}

	void addpaths(IEnumerable<string> paths) {
		var list = new List<string>();
		foreach (var p in paths) {
			if (Directory.Exists(p)) {
				foreach (var f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
					list.Add(f);
			}
			else if (File.Exists(p))
				list.Add(p);
		}
		_ = compute(list);
	}

	async Task compute(List<string> paths) {
		seq++;
		var n = seq;
		lbstat.Text = Loc.T("hash.working");
		foreach (var path in paths) {
			if (n != seq) return;
			if (rows.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
				continue;
			HashRow row;
			try {
				row = await Task.Run(() => HashTool.Compute(path, CancellationToken.None)).ConfigureAwait(true);
			}
			catch (Exception ex) {
				row = new HashRow { Path = path, Name = Path.GetFileName(path), Status = ex.Message };
			}
			if (n != seq) return;
			var vr = new HashViewRow {
				Path = row.Path,
				Name = row.Name,
				SizeText = HashTool.FormatSize(row.Size),
				Md5 = row.Md5,
				Sha1 = row.Sha1,
				Sha256 = row.Sha256,
			};
			vr.Match = matchof(vr);
			rows.Add(vr);
		}
		lbstat.Text = Loc.T("hash.n", rows.Count);
	}

	string matchof(HashViewRow vr) {
		var k = HashTool.MatchKind(new HashRow {
			Md5 = vr.Md5, Sha1 = vr.Sha1, Sha256 = vr.Sha256,
		}, ecmp.Text);
		if (k.Length == 0) return "";
		return Loc.T("hash.match", k);
	}

	void applymatch() {
		foreach (var r in rows)
			r.Match = matchof(r);
	}

	void copy() {
		var it = lv.SelectedItem as HashViewRow ?? rows.FirstOrDefault();
		if (it == null || string.IsNullOrEmpty(it.Sha256)) return;
		try {
			Clipboard.SetText(it.Sha256);
			lbstat.Text = Loc.T("hash.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
