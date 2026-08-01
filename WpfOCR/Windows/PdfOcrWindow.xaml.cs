using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Polygon = System.Windows.Shapes.Polygon;

namespace WpfOCR;

/// <summary>
/// PDF 识别工作台：渲染 → 识别 → 编辑文字 → 存草稿 → 导出可检索 PDF。
/// </summary>
partial class PdfOcrWindow : Window {
	readonly Func<OcrOptions> getOpts;
	readonly OcrRunner runner;

	PdfOcrProject proj;
	PdfPageEdit curPage;
	bool busy;
	bool suppressPageText;
	bool suppressPageNav;
	ObservableCollection<PdfLineEdit> lineView = new();
	CancellationTokenSource ocrCts;

	const double ZMIN = 0.15;
	const double ZMAX = 6.0;
	double zoom = 1.0;
	int imgW, imgH;
	PdfLineEdit hlLine;

	static readonly SolidColorBrush HlFill = brush(0x66, 0x3B, 0x82, 0xF6);
	static readonly SolidColorBrush HlStroke = brush(0xEE, 0x60, 0xA5, 0xFA);

	internal PdfOcrWindow(Func<OcrOptions> optionsFactory, OcrRunner sharedRunner, string openPdfPath = null) {
		InitializeComponent();
		getOpts = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
		runner = sharedRunner ?? throw new ArgumentNullException(nameof(sharedRunner));

		initui();
		// 忙碌时 Esc=取消识别；空闲时 Esc=关闭（Closing 会处理未保存草稿）
		WindowEsc.Attach(this, () => {
			if (busy) {
				try { ocrCts?.Cancel(); } catch { }
				setstatus("正在取消…");
				return;
			}
			Close();
		});
		if (!string.IsNullOrWhiteSpace(openPdfPath) && File.Exists(openPdfPath))
			Loaded += async (_, _) => await openpdfpath(openPdfPath);
	}

	void initui() {
		bopenpdf.Click += async (_, _) => await openpdfdialog();
		bopendraft.Click += async (_, _) => await opendraftdialog();
		bsavedraft.Click += (_, _) => savedraft(false);
		bsavedraftas.Click += (_, _) => savedraft(true);
		brecogall.Click += async (_, _) => await recogall();
		brecogpage.Click += async (_, _) => await recogpage();
		brecogrange.Click += async (_, _) => await recogrange();
		bcancelocr.Click += (_, _) => {
			try { ocrCts?.Cancel(); } catch { }
			setstatus("正在取消…");
		};
		bexport.Click += async (_, _) => await exportpdf();
		bcopyall.Click += (_, _) => copyall();
		bapplytext.Click += (_, _) => applypagetext();

		// 预览缩放 / 翻页
		bzoomin.Click += (_, _) => setzoom(zoom * 1.2);
		bzoomout.Click += (_, _) => setzoom(zoom / 1.2);
		bzoom100.Click += (_, _) => setzoom(1.0);
		bfitw.Click += (_, _) => fitwidth();
		bprev.Click += (_, _) => navpage(-1);
		bnext.Click += (_, _) => navpage(1);
		bjump.Click += (_, _) => jumppage();
		ejumppage.KeyDown += (_, e) => {
			if (e.Key == Key.Enter) { jumppage(); e.Handled = true; }
		};
		scpreview.PreviewMouseWheel += onpreviewwheel;
		pviewport.SizeChanged += (_, _) => {
			// 视口变化时不自动改缩放，仅更新按钮状态
		};

		lstpages.SelectionChanged += (_, _) => {
			if (suppressPageNav) return;
			onpagesel();
		};
		gridlines.CellEditEnding += (_, _) => {
			markdirty();
			syncpagetextfromlines();
		};
		gridlines.RowEditEnding += (_, _) => {
			markdirty();
			syncpagetextfromlines();
		};
		// 点击行：高亮并滚动到对应框（表格禁止排序，顺序=识别顺序）
		gridlines.SelectionChanged += (_, _) => {
			if (gridlines.SelectedItem is PdfLineEdit ln)
				highlightline(ln, scrollTo: true);
		};
		epageText.TextChanged += (_, _) => {
			if (!suppressPageText) markdirty();
		};

		einvisible.Checked += (_, _) => { if (proj != null) { proj.InvisibleText = true; markdirty(); } };
		einvisible.Unchecked += (_, _) => { if (proj != null) { proj.InvisibleText = false; markdirty(); } };

		Closing += onclosing;
		gridlines.ItemsSource = lineView;
		// 确保列不可排序（部分主题会忽略 XAML）
		foreach (var col in gridlines.Columns)
			col.CanUserSort = false;

		setbusy(false);
		refreshchrome();
		updatepagenav();
	}

	void onclosing(object sender, CancelEventArgs e) {
		if (busy) {
			e.Cancel = true;
			setstatus("正在处理，请稍候…");
			return;
		}
		if (proj != null && proj.Dirty) {
			var r = MessageBox.Show(this,
				"工程有未保存修改，是否保存草稿？",
				"PDF 工作台",
				MessageBoxButton.YesNoCancel,
				MessageBoxImage.Question,
				MessageBoxResult.Yes);
			if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
			if (r == MessageBoxResult.Yes) {
				try { savedraft(false); }
				catch (Exception ex) {
					MessageBox.Show(this, ex.Message, "存草稿失败", MessageBoxButton.OK, MessageBoxImage.Warning);
					e.Cancel = true;
				}
			}
		}
	}

	// ───────── 打开 ─────────

	async Task openpdfdialog() {
		if (busy) return;
		if (!confirmdiscard()) return;
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = "选择 PDF",
			Filter = "PDF 文件|*.pdf",
			CheckFileExists = true,
		};
		if (ofd.ShowDialog(this) != true) return;
		await openpdfpath(ofd.FileName);
	}

	async Task openpdfpath(string path) {
		if (busy || string.IsNullOrWhiteSpace(path)) return;
		var opt = getOpts() ?? new OcrOptions();
		// 光栅化 DPI 固定内部默认；导出页大小按原 PDF 页面比例
		var dpi = PdfOcr.DefaultDpi;
		var invisible = einvisible.IsChecked == true;
		if (opt != null) {
			invisible = opt.PdfInvisibleText;
			einvisible.IsChecked = invisible;
		}

		var draftDir = PdfOcrProject.NewDraftDir(Path.GetFileNameWithoutExtension(path));
		setbusy(true);
		Exception err = null;
		PdfOcrProject created = null;
		await Task.Run(() => {
			try {
				created = PdfOcr.CreateFromPdf(path, dpi, invisible, draftDir,
					(p, t, m) => Dispatcher.BeginInvoke(new Action(() => {
						setprogress(p, t);
						setstatus(m);
					})));
			}
			catch (Exception ex) { err = ex; }
		});
		setbusy(false);
		if (err != null) {
			setstatus("打开失败: " + err.Message);
			MessageBox.Show(this, err.Message, "打开 PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		bindproject(created);
		try { created.SaveDraft(); } catch { }
		setstatus($"已加载 {created.Pages.Count} 页 · 可「全部识别」或单页识别 · 草稿已自动保存");
		// 默认适应宽度
		Dispatcher.BeginInvoke(new Action(() => fitwidth()), System.Windows.Threading.DispatcherPriority.Loaded);
		var ask = MessageBox.Show(this,
			$"已渲染 {created.Pages.Count} 页。\n\n是否立即全部识别？",
			"PDF 工作台",
			MessageBoxButton.YesNo,
			MessageBoxImage.Question,
			MessageBoxResult.Yes);
		if (ask == MessageBoxResult.Yes)
			await recogall();
	}

	async Task opendraftdialog() {
		if (busy) return;
		if (!confirmdiscard()) return;

		var drafts = PdfOcrProject.ListDrafts();
		if (drafts.Count > 0) {
			var pick = new DraftPickWindow(drafts) { Owner = this };
			if (pick.ShowDialog() == true && !string.IsNullOrEmpty(pick.SelectedDir)) {
				loaddraft(pick.SelectedDir);
				return;
			}
			if (pick.BrowseFolder) {
				// fall through
			}
			else return;
		}

		var dlg = new System.Windows.Forms.FolderBrowserDialog {
			Description = "选择草稿目录（含 project.json）",
		};
		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
		loaddraft(dlg.SelectedPath);
		await Task.CompletedTask;
	}

	void loaddraft(string dir) {
		try {
			var p = PdfOcrProject.LoadDraft(dir);
			bindproject(p);
			einvisible.IsChecked = p.InvisibleText;
			setstatus($"已打开草稿 · {p.Pages.Count} 页 · {p.SavedAt}");
			Dispatcher.BeginInvoke(new Action(() => fitwidth()), System.Windows.Threading.DispatcherPriority.Loaded);
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "打开草稿", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void bindproject(PdfOcrProject p) {
		proj = p;
		suppressPageNav = true;
		lstpages.ItemsSource = null;
		lstpages.ItemsSource = proj.Pages;
		suppressPageNav = false;
		if (proj.Pages.Count > 0) {
			epagefrom.Text = "1";
			epageto.Text = proj.Pages.Count.ToString();
			gotopage(0);
		}
		else {
			epagefrom.Text = "1";
			epageto.Text = "1";
			showpage(null);
		}
		refreshchrome();
		updatepagenav();
	}

	// ───────── 识别 ─────────

	async Task recogall() {
		if (busy || proj == null) return;
		await recogpages(0, proj.Pages.Count - 1, "全部识别");
	}

	async Task recogpage() {
		if (busy || proj == null || curPage == null) return;
		await recogpages(curPage.Index, curPage.Index, "本页识别");
	}

	/// <summary>按页码范围批量识别（1-based，含两端）。</summary>
	async Task recogrange() {
		if (busy || proj == null || proj.Pages.Count == 0) return;
		var n = proj.Pages.Count;
		if (!int.TryParse((epagefrom.Text ?? "").Trim(), out var from) || from < 1)
			from = 1;
		if (!int.TryParse((epageto.Text ?? "").Trim(), out var to) || to < 1)
			to = n;
		from = Compat.Clamp(from, 1, n);
		to = Compat.Clamp(to, 1, n);
		if (from > to) (from, to) = (to, from);
		epagefrom.Text = from.ToString();
		epageto.Text = to.ToString();
		await recogpages(from - 1, to - 1, $"范围 {from}-{to}");
	}

	/// <summary>识别页索引闭区间 [from0, to0]，页间可取消。</summary>
	async Task recogpages(int from0, int to0, string label) {
		if (busy || proj == null) return;
		from0 = Compat.Clamp(from0, 0, proj.Pages.Count - 1);
		to0 = Compat.Clamp(to0, 0, proj.Pages.Count - 1);
		if (from0 > to0) (from0, to0) = (to0, from0);

		try { ocrCts?.Cancel(); } catch { }
		ocrCts = new CancellationTokenSource();
		var ct = ocrCts.Token;
		var opt = snapshotopt();
		setbusy(true);
		Exception err = null;
		var cancelled = false;
		var total = to0 - from0 + 1;
		var done = 0;
		try {
			await Task.Run(() => {
				for (int i = from0; i <= to0; i++) {
					ct.ThrowIfCancellationRequested();
					var page = proj.Pages[i];
					var img = proj.ImagePath(page);
					var step = ++done;
					Dispatcher.BeginInvoke(new Action(() => {
						setprogress(step, total);
						setstatus($"{label} · 第 {i + 1} 页（{step}/{total}）… 可取消");
					}));
					PdfOcr.RecognizePage(page, img, opt, runner);
				}
			}, ct);
		}
		catch (OperationCanceledException) {
			cancelled = true;
		}
		catch (Exception ex) {
			if (ct.IsCancellationRequested) cancelled = true;
			else err = ex;
		}
		setbusy(false);
		if (cancelled || ct.IsCancellationRequested) {
			proj.Dirty = true;
			refreshpagelist();
			if (curPage != null) showpage(curPage);
			setstatus($"{label}已取消 · 已完成 {done}/{total} 页");
			try { proj.SaveDraft(); } catch { }
			refreshchrome();
			return;
		}
		if (err != null) {
			setstatus(label + "失败: " + err.Message);
			MessageBox.Show(this, err.Message, label, MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		proj.Dirty = true;
		refreshpagelist();
		if (from0 == to0)
			showpage(proj.Pages[from0]);
		else
			gotopage(from0);
		setstatus($"{label}完成 · {total} 页");
		try { proj.SaveDraft(); setstatus($"{label}完成 · 草稿已保存"); } catch { }
		refreshchrome();
	}

	// ───────── 草稿 / 导出 ─────────

	void savedraft(bool saveAs) {
		if (proj == null) return;
		applypagetext();
		try {
			if (saveAs || string.IsNullOrWhiteSpace(proj.DraftDir)) {
				var dlg = new System.Windows.Forms.FolderBrowserDialog {
					Description = "选择草稿保存目录（将写入 project.json 与 pages/）",
				};
				if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
				var target = dlg.SelectedPath;
				if (Directory.GetFileSystemEntries(target).Length > 0
					&& !File.Exists(Path.Combine(target, "project.json"))) {
					target = Path.Combine(target, (proj.Title ?? "pdf") + "_draft");
					Directory.CreateDirectory(target);
					Directory.CreateDirectory(Path.Combine(target, "pages"));
					foreach (var pg in proj.Pages) {
						var src = proj.ImagePath(pg);
						var dstRel = Path.Combine("pages", $"{pg.Index:D3}.png");
						var dst = Path.Combine(target, dstRel);
						if (File.Exists(src)) File.Copy(src, dst, true);
						pg.ImageFile = dstRel.Replace('\\', '/');
					}
					proj.DraftDir = target;
				}
				else {
					proj.DraftDir = target;
					Directory.CreateDirectory(Path.Combine(target, "pages"));
				}
			}
			proj.InvisibleText = einvisible.IsChecked == true;
			if (proj.Dpi <= 0) proj.Dpi = PdfOcr.DefaultDpi;
			proj.SaveDraft();
			refreshchrome();
			setstatus($"草稿已保存 · {proj.DraftDir}");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "存草稿", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	async Task exportpdf() {
		if (busy || proj == null) return;
		applypagetext();
		if (proj.Pages.All(p => !p.Recognized && (p.Lines == null || p.Lines.Count == 0))) {
			var r = MessageBox.Show(this, "尚未识别文字，是否先全部识别？", "导出",
				MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
			if (r == MessageBoxResult.Cancel) return;
			if (r == MessageBoxResult.Yes) await recogall();
		}

		proj.InvisibleText = einvisible.IsChecked == true;
		var name = string.IsNullOrEmpty(proj.Title) ? "ocr" : proj.Title;
		var sfd = new Microsoft.Win32.SaveFileDialog {
			Title = "导出可检索 PDF",
			Filter = "PDF 文件|*.pdf",
			FileName = name + "_ocr.pdf",
			DefaultExt = ".pdf",
			AddExtension = true,
			OverwritePrompt = true,
		};
		if (!string.IsNullOrEmpty(proj.SourcePath))
			sfd.InitialDirectory = Path.GetDirectoryName(proj.SourcePath);
		if (sfd.ShowDialog(this) != true) return;
		if (!string.IsNullOrEmpty(proj.SourcePath)
			&& string.Equals(Path.GetFullPath(proj.SourcePath), Path.GetFullPath(sfd.FileName),
				StringComparison.OrdinalIgnoreCase)) {
			MessageBox.Show(this, "输出路径不能与源 PDF 相同。", "导出",
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		setbusy(true);
		Exception err = null;
		var outPath = sfd.FileName;
		await Task.Run(() => {
			try {
				PdfOcr.Export(proj, outPath, (p, t, m) =>
					Dispatcher.BeginInvoke(new Action(() => {
						setprogress(p, t);
						setstatus(m);
					})));
			}
			catch (Exception ex) { err = ex; }
		});
		setbusy(false);
		if (err != null) {
			setstatus("导出失败: " + err.Message);
			MessageBox.Show(this, err.Message, "导出", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		setstatus($"已导出 · {outPath}");
		MessageBox.Show(this,
			$"已导出：\n{outPath}\n\n共 {proj.Pages.Count} 页"
			+ (proj.InvisibleText ? " · 含不可见文字层" : " · 仅图像"),
			"导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	void copyall() {
		if (proj == null) return;
		applypagetext();
		var t = proj.FullText();
		try {
			Clipboard.SetText(t ?? "");
			setstatus("全文已复制");
		}
		catch (Exception ex) {
			setstatus("复制失败: " + ex.Message);
		}
	}

	// ───────── 翻页 / 缩放 / 高亮 ─────────

	void onpagesel() {
		var page = lstpages.SelectedItem as PdfPageEdit;
		showpage(page);
		updatepagenav();
	}

	void navpage(int delta) {
		if (proj == null || proj.Pages.Count == 0) return;
		var idx = curPage?.Index ?? lstpages.SelectedIndex;
		if (idx < 0) idx = 0;
		gotopage(idx + delta);
	}

	void jumppage() {
		if (proj == null || proj.Pages.Count == 0) return;
		if (!int.TryParse((ejumppage.Text ?? "").Trim(), out var n)) {
			setstatus("页码须为数字");
			return;
		}
		gotopage(n - 1);
	}

	void gotopage(int index0) {
		if (proj == null || proj.Pages.Count == 0) return;
		index0 = Compat.Clamp(index0, 0, proj.Pages.Count - 1);
		suppressPageNav = true;
		lstpages.SelectedIndex = index0;
		suppressPageNav = false;
		showpage(proj.Pages[index0]);
		updatepagenav();
		try { lstpages.ScrollIntoView(proj.Pages[index0]); } catch { }
	}

	void updatepagenav() {
		var n = proj?.Pages.Count ?? 0;
		var i = curPage?.Index ?? -1;
		lbpagecount.Text = n > 0 ? $"/ {n} 页" : "/ 0 页";
		if (i >= 0) ejumppage.Text = (i + 1).ToString();
		bprev.IsEnabled = !busy && i > 0;
		bnext.IsEnabled = !busy && n > 0 && i >= 0 && i < n - 1;
		bjump.IsEnabled = !busy && n > 0;
		ejumppage.IsEnabled = !busy && n > 0;
		var canZoom = !busy && curPage != null && imgW > 0;
		bzoomin.IsEnabled = canZoom;
		bzoomout.IsEnabled = canZoom;
		bfitw.IsEnabled = canZoom;
		bzoom100.IsEnabled = canZoom;
	}

	void setzoom(double z) {
		zoom = Compat.Clamp(z, ZMIN, ZMAX);
		tfzoom.ScaleX = zoom;
		tfzoom.ScaleY = zoom;
		lbzoom.Text = $"{zoom * 100:0}%";
	}

	void fitwidth() {
		if (imgW < 1) return;
		// 等布局完成后再量 ViewportWidth
		scpreview.UpdateLayout();
		var vw = scpreview.ViewportWidth;
		if (vw < 8) vw = pviewport.ActualWidth - 8;
		if (vw < 8) return;
		var z = (vw - 4) / imgW;
		setzoom(z);
	}

	void onpreviewwheel(object sender, MouseWheelEventArgs e) {
		if (imgW < 1) return;
		// Ctrl+滚轮缩放；普通滚轮交给 ScrollViewer
		if (Keyboard.Modifiers != ModifierKeys.Control) return;
		var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
		setzoom(zoom * factor);
		e.Handled = true;
	}

	void showpage(PdfPageEdit page) {
		curPage = page;
		hlLine = null;
		lineView.Clear();
		poverlay.Children.Clear();
		if (page == null) {
			imgpage.Source = null;
			imgpage.Width = 0;
			imgpage.Height = 0;
			pstage.Width = 0;
			pstage.Height = 0;
			imgW = imgH = 0;
			lbpagesize.Text = "";
			suppressPageText = true;
			epageText.Text = "";
			suppressPageText = false;
			updatepagenav();
			return;
		}
		// 图像：固定像素尺寸，与 Box 坐标一致
		try {
			var path = proj?.ImagePath(page);
			if (path != null && File.Exists(path)) {
				var bi = new BitmapImage();
				bi.BeginInit();
				bi.CacheOption = BitmapCacheOption.OnLoad;
				bi.UriSource = new Uri(path, UriKind.Absolute);
				bi.EndInit();
				bi.Freeze();
				imgW = bi.PixelWidth;
				imgH = bi.PixelHeight;
				if (page.Width < 1) page.Width = imgW;
				if (page.Height < 1) page.Height = imgH;
				imgpage.Source = bi;
				imgpage.Width = imgW;
				imgpage.Height = imgH;
				pstage.Width = imgW;
				pstage.Height = imgH;
				poverlay.Width = imgW;
				poverlay.Height = imgH;
				lbpagesize.Text = $"{imgW}×{imgH} px";
			}
			else {
				imgpage.Source = null;
				imgW = imgH = 0;
				lbpagesize.Text = "缺图";
			}
		}
		catch {
			imgpage.Source = null;
			imgW = imgH = 0;
		}
		// 行（保持识别顺序，编号 1..n）
		if (page.Lines != null) {
			for (int i = 0; i < page.Lines.Count; i++) {
				page.Lines[i].LineNo = i + 1;
				lineView.Add(page.Lines[i]);
			}
		}
		syncpagetextfromlines();
		updatepagenav();
	}

	/// <summary>高亮文字框并可选滚动到可见区域。</summary>
	void highlightline(PdfLineEdit line, bool scrollTo) {
		hlLine = line;
		poverlay.Children.Clear();
		if (line?.Box == null || line.Box.Length < 8 || imgW < 1) {
			if (line != null)
				setstatus($"第 {curPage?.Index + 1} 页 · 行 {line.LineNo} · 无坐标框");
			return;
		}
		var box = line.Box;
		var pts = new PointCollection {
			new Point(box[0], box[1]),
			new Point(box[2], box[3]),
			new Point(box[4], box[5]),
			new Point(box[6], box[7]),
		};
		var poly = new Polygon {
			Points = pts,
			Fill = HlFill,
			Stroke = HlStroke,
			StrokeThickness = Math.Max(1.5, 2.0 / Math.Max(0.2, zoom)),
		};
		poverlay.Children.Add(poly);

		if (scrollTo)
			scrolltobox(box);

		var t = line.Text ?? "";
		if (t.Length > 24) t = t[..24] + "…";
		setstatus($"第 {curPage?.Index + 1} 页 · 行 {line.LineNo} · {t}");
	}

	void scrolltobox(float[] box) {
		if (box == null || box.Length < 8) return;
		double minX = box[0], maxX = box[0], minY = box[1], maxY = box[1];
		for (int i = 0; i < 4; i++) {
			minX = Math.Min(minX, box[i * 2]);
			maxX = Math.Max(maxX, box[i * 2]);
			minY = Math.Min(minY, box[i * 2 + 1]);
			maxY = Math.Max(maxY, box[i * 2 + 1]);
		}
		// 像素 → 缩放后坐标
		var cx = (minX + maxX) / 2.0 * zoom;
		var cy = (minY + maxY) / 2.0 * zoom;
		scpreview.UpdateLayout();
		var vw = scpreview.ViewportWidth;
		var vh = scpreview.ViewportHeight;
		if (vw < 1 || vh < 1) return;
		var left = Math.Max(0, cx - vw / 2);
		var top = Math.Max(0, cy - vh / 2);
		scpreview.ScrollToHorizontalOffset(left);
		scpreview.ScrollToVerticalOffset(top);
	}

	void syncpagetextfromlines() {
		if (curPage == null) return;
		curPage.Lines = lineView.ToList();
		for (int i = 0; i < curPage.Lines.Count; i++)
			curPage.Lines[i].LineNo = i + 1;
		suppressPageText = true;
		epageText.Text = curPage.PageText;
		suppressPageText = false;
	}

	void applypagetext() {
		if (curPage == null) return;
		curPage.ApplyPageText(epageText.Text ?? "");
		lineView.Clear();
		for (int i = 0; i < curPage.Lines.Count; i++) {
			curPage.Lines[i].LineNo = i + 1;
			lineView.Add(curPage.Lines[i]);
		}
		poverlay.Children.Clear();
		markdirty();
		refreshpagelist();
		setstatus($"已应用整页文本 · 第 {curPage.Index + 1} 页 · {curPage.Lines.Count} 行");
	}

	// ───────── 辅助 ─────────

	OcrOptions snapshotopt() {
		var o = getOpts() ?? new OcrOptions();
		return new OcrOptions {
			ModelPackId = o.ModelPackId,
			ModelVariant = o.ModelVariant,
			ModelsDir = o.ModelsDir,
			Device = o.Device,
			DetLimitSideLen = o.DetLimitSideLen,
			DetPadding = o.DetPadding,
			DetThresh = o.DetThresh,
			DetBoxThresh = o.DetBoxThresh,
			DetUnclipRatio = o.DetUnclipRatio,
			DetUseDilation = o.DetUseDilation,
			RecImgH = o.RecImgH,
			RecMaxWidth = o.RecMaxWidth,
			RecAbsMaxWidth = o.RecAbsMaxWidth,
			RecBatchNum = o.RecBatchNum,
			UseCls = o.UseCls,
			PdfInvisibleText = einvisible.IsChecked == true,
			PdfDpi = PdfOcr.DefaultDpi,
		};
	}

	void markdirty() {
		if (proj == null) return;
		proj.Dirty = true;
		refreshchrome();
	}

	void refreshchrome() {
		var has = proj != null;
		bsavedraft.IsEnabled = has && !busy;
		bsavedraftas.IsEnabled = has && !busy;
		brecogall.IsEnabled = has && !busy;
		brecogpage.IsEnabled = has && curPage != null && !busy;
		brecogrange.IsEnabled = has && !busy;
		epagefrom.IsEnabled = has && !busy;
		epageto.IsEnabled = has && !busy;
		bexport.IsEnabled = has && !busy;
		bcopyall.IsEnabled = has && !busy;
		bapplytext.IsEnabled = has && curPage != null && !busy;
		lbtitle.Text = proj == null
			? "未打开工程"
			: $"{proj.Title} · {proj.Pages.Count} 页"
			  + (string.IsNullOrEmpty(proj.DraftDir) ? "" : " · " + proj.DraftDir);
		lbdirty.Text = proj != null && proj.Dirty ? "● 未保存" : "";
		updatepagenav();
	}

	void refreshpagelist() {
		var idx = lstpages.SelectedIndex;
		suppressPageNav = true;
		lstpages.ItemsSource = null;
		lstpages.ItemsSource = proj?.Pages;
		if (idx >= 0 && proj != null && idx < proj.Pages.Count)
			lstpages.SelectedIndex = idx;
		suppressPageNav = false;
	}

	bool confirmdiscard() {
		if (proj == null || !proj.Dirty) return true;
		var r = MessageBox.Show(this, "当前工程未保存，是否放弃修改？", "PDF 工作台",
			MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
		return r == MessageBoxResult.Yes;
	}

	void setbusy(bool on) {
		busy = on;
		bopenpdf.IsEnabled = !on;
		bopendraft.IsEnabled = !on;
		prog.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
		bcancelocr.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
		bcancelocr.IsEnabled = on;
		if (!on) prog.Value = 0;
		refreshchrome();
	}

	void setprogress(int cur, int total) {
		if (total <= 0) { prog.Value = 0; return; }
		prog.Visibility = Visibility.Visible;
		prog.Value = Compat.Clamp(100.0 * cur / total, 0, 100);
	}

	void setstatus(string s) => lbstatus.Text = s ?? "";

	static SolidColorBrush brush(byte a, byte r, byte g, byte b) {
		var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
		br.Freeze();
		return br;
	}
}

/// <summary>简单草稿选择列表。</summary>
sealed class DraftPickWindow : Window {
	public string SelectedDir { get; private set; }
	public bool BrowseFolder { get; private set; }

	readonly System.Windows.Controls.ListBox list;
	readonly List<(string Dir, string Title, string SavedAt, int Pages)> items;

	public DraftPickWindow(List<(string Dir, string Title, string SavedAt, int Pages)> drafts) {
		items = drafts ?? new();
		Title = "打开草稿";
		Width = 520;
		Height = 420;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = (System.Windows.Media.Brush)FindResource("BgApp");
		var root = new DockPanel { Margin = new Thickness(16) };
		var lb = new TextBlock {
			Text = "本地草稿（%LocalAppData%\\WpfOCR\\drafts）",
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0, 0, 0, 10),
		};
		DockPanel.SetDock(lb, Dock.Top);
		root.Children.Add(lb);

		var buttons = new StackPanel {
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			Margin = new Thickness(0, 12, 0, 0),
		};
		DockPanel.SetDock(buttons, Dock.Bottom);
		var bbrowse = new System.Windows.Controls.Button { Content = "浏览文件夹…", Style = (Style)FindResource("ToolBtn"), Width = 110, Margin = new Thickness(0, 0, 8, 0) };
		var bcancel = new System.Windows.Controls.Button { Content = "取消", Style = (Style)FindResource("ToolBtn"), Width = 88, Margin = new Thickness(0, 0, 8, 0) };
		var bok = new System.Windows.Controls.Button { Content = "打开", Style = (Style)FindResource("PrimaryBtn"), Width = 88 };
		bbrowse.Click += (_, _) => { BrowseFolder = true; DialogResult = false; Close(); };
		bcancel.Click += (_, _) => { DialogResult = false; Close(); };
		bcancel.IsCancel = true;
		bok.Click += (_, _) => ok();
		bok.IsDefault = true;
		buttons.Children.Add(bbrowse);
		buttons.Children.Add(bcancel);
		buttons.Children.Add(bok);
		root.Children.Add(buttons);

		list = new System.Windows.Controls.ListBox { BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSoft") };
		foreach (var d in items)
			list.Items.Add($"{d.Title}  ·  {d.Pages}页  ·  {d.SavedAt}\n{d.Dir}");
		list.MouseDoubleClick += (_, _) => ok();
		root.Children.Add(list);
		Content = root;
		WindowEsc.Attach(this, () => { DialogResult = false; Close(); });
	}

	void ok() {
		var i = list.SelectedIndex;
		if (i < 0 || i >= items.Count) {
			MessageBox.Show(this, "请选择一个草稿", "打开草稿", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		SelectedDir = items[i].Dir;
		DialogResult = true;
		Close();
	}
}
