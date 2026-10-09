using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace ScreenKit;

/// <summary>发现新版本：版本列表、中英文更新说明、更新或忽略。</summary>
public partial class UpdateNotesWindow : Window {
	readonly List<ReleaseNote> notes;
	bool langlock;
	bool zh = true;

	public UpdateNotesWindow(UpdateInfo info, IList<ReleaseNote> items) {
		InitializeComponent();
		notes = items == null ? new List<ReleaseNote>() : items.Where(n => n != null).ToList();
		info ??= new UpdateInfo();
		var cur = string.IsNullOrWhiteSpace(info.CurrentVersion) ? AppUpdater.CurrentVersion() : info.CurrentVersion;
		var latest = string.IsNullOrWhiteSpace(info.Version) ? "—" : info.Version;
		var name = info.AssetName;
		if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(info.DownloadUrl)) {
			try { name = Path.GetFileName(new Uri(info.DownloadUrl).AbsolutePath); }
			catch { name = Path.GetFileName(info.DownloadUrl); }
		}
		var size = info.SizeBytes > 0 ? FeatureInstaller.FormatBytes(info.SizeBytes) : "";
		Title = Loc.T("update.notes.title");
		lbhead.Text = Loc.T("update.notes.head", cur, latest);
		if (!string.IsNullOrWhiteSpace(name) && size.Length > 0)
			lbfile.Text = Loc.T("update.notes.file", name, size);
		else
			lbfile.Text = name ?? "";
		lbvers.Text = Loc.T("update.notes.versions");
		lbnotes.Text = Loc.T("update.notes.body");
		lbrestart.Text = Loc.T("update.notes.restart");
		bgo.Content = Loc.T("update.notes.go");
		bignore.Content = Loc.T("update.notes.ignore");
		bzh.Content = Loc.T("update.notes.lang.zh");
		ben.Content = Loc.T("update.notes.lang.en");

		elist.ItemsSource = notes;
		elist.SelectionChanged += (_, _) => render();
		bzh.Click += (_, _) => picklang(true);
		ben.Click += (_, _) => picklang(false);
		bgo.Click += (_, _) => { DialogResult = true; };
		bignore.Click += (_, _) => { DialogResult = false; };
		docview.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(onnav));
		WindowEsc.Attach(this, () => {
			try { DialogResult = false; } catch { Close(); }
		});

		if (notes.Count > 0) elist.SelectedIndex = notes.Count - 1;
		picklang(Loc.IsZh);
	}

	void picklang(bool useZh) {
		if (langlock) return;
		langlock = true;
		zh = useZh;
		bzh.IsChecked = useZh;
		ben.IsChecked = !useZh;
		langlock = false;
		render();
	}

	void render() {
		var n = elist?.SelectedItem as ReleaseNote;
		var md = "";
		if (n != null) md = (zh ? n.Chinese : n.English) ?? "";
		var doc = new FlowDocument();
		if (n != null && string.IsNullOrWhiteSpace(md)) {
			MdView.Fill(doc, "");
			var p = new Paragraph(new Run(Loc.T("update.notes.empty"))) {
				Margin = new Thickness(0),
				Foreground = TryFindResource("TextMuted") as Brush ?? Brushes.Gray,
			};
			doc.Blocks.Add(p);
		}
		else {
			MdView.Fill(doc, md);
		}
		docview.Document = doc;
	}

	static void onnav(object sender, RequestNavigateEventArgs e) {
		try {
			if (e.Uri != null) {
				Process.Start(new ProcessStartInfo {
					FileName = e.Uri.AbsoluteUri,
					UseShellExecute = true,
				});
			}
		}
		catch { }
		e.Handled = true;
	}
}
