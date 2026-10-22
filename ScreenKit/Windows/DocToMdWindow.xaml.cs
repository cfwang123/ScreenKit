using System.Text;
using System.Windows;

namespace ScreenKit;

/// <summary>文档转 Markdown：docx 解析段落/标题/表格，PDF 用 pdfium 提文字。</summary>
public partial class DocToMdWindow : Window {
	int gen;
	bool busy;
	string md = "";
	string src = "";
	string imgdir = "images";
	List<DocToMd.Image> images = new();

	public DocToMdWindow() {
		InitializeComponent();
		applylang();
		bfile.Click += (_, _) => pick();
		bgo.Click += (_, _) => convert();
		bsave.Click += (_, _) => save();
		bcopy.Click += (_, _) => copy();
		bclose.Click += (_, _) => Close();
		epath.KeyDown += (_, e) => {
			if (e.Key == System.Windows.Input.Key.Enter) convert();
		};
		Drop += (_, e) => drop(e);
		DragOver += (_, e) => {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("doc2md.title");
		ToolBtnUi.Set(bfile, ToolBtnUi.Browse, Loc.T("doc2md.file"));
		ToolBtnUi.Set(bgo, ToolBtnUi.Encode, Loc.T("doc2md.convert"));
		ToolBtnUi.Set(bsave, ToolBtnUi.Save, Loc.T("doc2md.save"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("doc2md.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("doc2md.close"));
		lbimg.Text = Loc.T("doc2md.imgdir");
		lbimgtip.Text = Loc.T("doc2md.imgdir.tip");
		if (string.IsNullOrWhiteSpace(eimgdir.Text)) eimgdir.Text = "images";
		if (!busy) setstat(Loc.T("doc2md.ready"));
	}

	void setstat(string text) => lbstat.Text = text ?? "";

	void pick() {
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("doc2md.file"),
			Filter = Loc.T("doc2md.filter"),
		};
		if (dlg.ShowDialog(this) != true) return;
		epath.Text = dlg.FileName;
		convert();
	}

	void drop(DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files) || files.Length == 0) return;
		e.Handled = true;
		var ext = Path.GetExtension(files[0]).ToLowerInvariant();
		if (ext != ".docx" && ext != ".pdf") {
			setstat(Loc.T("doc2md.errtype"));
			return;
		}
		epath.Text = files[0];
		convert();
	}

	void convert() {
		if (busy) return;
		var path = (epath.Text ?? "").Trim().Trim('"');
		if (!File.Exists(path)) {
			setstat(Loc.T("doc2md.nofile"));
			return;
		}
		var ext = Path.GetExtension(path).ToLowerInvariant();
		if (ext != ".docx" && ext != ".pdf") {
			setstat(Loc.T("doc2md.errtype"));
			return;
		}
		if (ext == ".pdf" && !NativeRuntime.HasPdfium()) {
			if (!FeaturePrompt.EnsureKinds(this, Loc.T("feat.prompt.pdf.title"),
				Loc.T("feat.prompt.pdf.detail"), FeatureKind.NativePdfium)) {
				setstat(Loc.T("doc2md.nopdfium"));
				return;
			}
		}
		if (!DocToMd.ValidDir(eimgdir.Text, path, out var dir)) {
			setstat(Loc.T("doc2md.badimgdir"));
			return;
		}
		var n = ++gen;
		busy = true;
		bfile.IsEnabled = false;
		bgo.IsEnabled = false;
		bsave.IsEnabled = false;
		bcopy.IsEnabled = false;
		setstat(Loc.T("doc2md.working"));
		Task.Run(() => {
			DocToMd.Result got = null;
			string err = null;
			try { got = DocToMd.Convert(path, dir); }
			catch (DllNotFoundException) { err = "doc2md.nopdfium"; }
			catch (Exception ex) { err = ex.Message; }
			Dispatcher.Invoke(() => {
				if (n != gen) return;
				busy = false;
				bfile.IsEnabled = true;
				bgo.IsEnabled = true;
				bsave.IsEnabled = true;
				bcopy.IsEnabled = true;
				if (err != null) {
					setstat(Loc.T(err));
					return;
				}
				src = path;
				md = got.Markdown ?? "";
				images = got.Images;
				imgdir = got.ImageDir;
				eout.Text = md;
				if (md.Trim().Length == 0) {
					setstat(Loc.T("doc2md.empty"));
					return;
				}
				var stat = got.Kind == "pdf"
					? string.Format(Loc.T("doc2md.done.pdf"), got.Pages, md.Length)
					: string.Format(Loc.T("doc2md.done"), md.Length);
				if (images.Count > 0) stat += string.Format(Loc.T("doc2md.imgpart"), images.Count);
				setstat(stat);
			});
		});
	}

	void save() {
		if (md.Length == 0) return;
		var name = Path.GetFileNameWithoutExtension(src.Length > 0 ? src : epath.Text ?? "");
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T("doc2md.saveas"),
			Filter = "Markdown (*.md)|*.md|" + Loc.T("doc2md.allfiles") + " (*.*)|*.*",
			FileName = (name.Length > 0 ? name : "document") + ".md",
		};
		if (src.Length > 0) {
			try { dlg.InitialDirectory = Path.GetDirectoryName(src); }
			catch { }
		}
		if (dlg.ShowDialog(this) != true) return;
		try {
			if (images.Count > 0) {
				var folder = Path.Combine(Path.GetDirectoryName(dlg.FileName) ?? "",
					imgdir.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(folder);
				foreach (var img in images)
					File.WriteAllBytes(Path.Combine(folder, img.Name), img.Data);
			}
			File.WriteAllText(dlg.FileName, md, new UTF8Encoding(false));
			setstat(images.Count > 0
				? string.Format(Loc.T("doc2md.saved.images"), dlg.FileName, images.Count)
				: string.Format(Loc.T("doc2md.saved"), dlg.FileName));
		}
		catch (Exception ex) {
			setstat(ex.Message);
		}
	}

	void copy() {
		if (md.Length == 0) return;
		try {
			Clipboard.SetText(md);
			setstat(Loc.T("doc2md.copied"));
		}
		catch (Exception ex) {
			setstat(ex.Message);
		}
	}
}
