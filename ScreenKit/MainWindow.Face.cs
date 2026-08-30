using System.Diagnostics;
using System.Windows.Media;
using Microsoft.Win32;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>MainWindow：人脸识别 Tab（InsightFace ONNX）。</summary>
public partial class MainWindow {
	static readonly string[] FaceImageExts = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff"];
	static string FaceNoneLabel => Loc.T("face.none");
	static bool isfacenone(string s) => string.IsNullOrEmpty(s) || s == FaceNoneLabel || s == "(无)" || s == "(none)";
	enum FaceStatTone { Ok, Warn, Bad }
	static string faceside(bool left) => Loc.T(left ? "face.side.L" : "face.side.R");

	FacePipeline facePipe;
	LandmarkDetector faceLmk;
	GenderAgeDetector faceAttr;
	string faceLmkName = "";
	string faceAttrName = "";
	readonly object faceLock = new();
	float[] faceFeatL, faceFeatR;
	bool faceBusyL, faceBusyR;
	bool faceUiLoading;
	List<string> faceTemps = new();

	void initface() {
		faceUiLoading = true;
		efacecompute.Items.Clear();
		efacecompute.Items.Add(new ComboBoxItem { Content = Loc.Compute(TtsComputeMode.Auto), Tag = TtsComputeMode.Auto });
		efacecompute.Items.Add(new ComboBoxItem { Content = Loc.Compute(TtsComputeMode.Gpu), Tag = TtsComputeMode.Gpu });
		efacecompute.Items.Add(new ComboBoxItem { Content = Loc.Compute(TtsComputeMode.Igpu), Tag = TtsComputeMode.Igpu });
		efacecompute.Items.Add(new ComboBoxItem { Content = Loc.Compute(TtsComputeMode.Cpu), Tag = TtsComputeMode.Cpu });
		var wantComp = parsefacecompute(opt.FaceCompute);
		foreach (ComboBoxItem it in efacecompute.Items) {
			if (it.Tag is TtsComputeMode m && m == wantComp) {
				efacecompute.SelectedItem = it;
				break;
			}
		}
		if (efacecompute.SelectedItem == null) efacecompute.SelectedIndex = 0;

		efacethresh.Value = Compat.Clamp(opt.FaceThreshold, 0.2, 0.9);
		lbfacethresh.Text = efacethresh.Value.ToString("0.00");

		scanfacemodels();
		wirefaceui();
		faceUiLoading = false;
	}

	void wirefaceui() {
		efacedet.SelectionChanged += (_, _) => onfacemodelchanged();
		efacereg.SelectionChanged += (_, _) => onfacemodelchanged();
		efacelmk.SelectionChanged += (_, _) => {
			if (faceUiLoading) return;
			disposefacelmk();
			clearfacesides();
			savefaceprefs();
			facelog($"关键点模型 → {efacelmk.SelectedItem}");
		};
		efaceattr.SelectionChanged += (_, _) => {
			if (faceUiLoading) return;
			disposefaceattr();
			clearfacesides();
			savefaceprefs();
			facelog($"属性模型 → {efaceattr.SelectedItem}");
		};
		efacecompute.SelectionChanged += (_, _) => {
			if (faceUiLoading) return;
			disposefaceall();
			clearfacesides();
			savefaceprefs();
			facelog($"推理设备 → {((efacecompute.SelectedItem as ComboBoxItem)?.Content)}");
		};
		efacethresh.ValueChanged += (_, _) => {
			lbfacethresh.Text = efacethresh.Value.ToString("0.00");
			updatefacesim(false);
			if (!faceUiLoading) savefaceprefs();
		};
		bfacereload.Click += (_, _) => {
			scanfacemodels();
			facelog(Loc.T("face.reloaded"));
		};
		bfacepasteL.Click += (_, _) => facepaste(true);
		bfacepasteR.Click += (_, _) => facepaste(false);
		bfaceclearL.Click += (_, _) => faceclear(true);
		bfaceclearR.Click += (_, _) => faceclear(false);
		bfacesaveL.Click += (_, _) => facesavefeat(true);
		bfacesaveR.Click += (_, _) => facesavefeat(false);
		bfaceswap.Click += (_, _) => faceswap();

		wirefacedrop(pfaceleft, true);
		wirefacedrop(pfaceright, false);
		wirefacedrop(imgfaceL, true);
		wirefacedrop(imgfaceR, false);
	}

	void wirefacedrop(UIElement el, bool left) {
		if (el == null) return;
		el.AllowDrop = true;
		el.PreviewDragOver += (_, e) => {
			if (hasfacefiledrop(e.Data)) {
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;
			}
		};
		el.Drop += (_, e) => {
			var path = pickfacepath(e.Data);
			if (path == null) return;
			e.Handled = true;
			_ = faceloadpath(path, left);
		};
	}

	void scanfacemodels() {
		faceUiLoading = true;
		try {
			var onnx = FaceModels.ListOnnx();
			var det = FaceModels.DetModels(onnx);
			var reg = FaceModels.RegModels(onnx);
			var lmk = FaceModels.LmkModels(onnx);
			var attr = FaceModels.AttrModels(onnx);

			fillfacecombo(efacedet, det, opt.FaceDetModel, "scrfd_10g_kps.onnx");
			fillfacecombo(efacereg, reg, opt.FaceRegModel, "glint360k_r100.onnx");

			var lmkItems = new List<string> { FaceNoneLabel };
			lmkItems.AddRange(lmk);
			fillfacecombo(efacelmk, lmkItems,
				string.IsNullOrWhiteSpace(opt.FaceLmkModel) ? FaceNoneLabel : opt.FaceLmkModel, FaceNoneLabel);

			var attrItems = new List<string> { FaceNoneLabel };
			attrItems.AddRange(attr);
			var wantAttr = string.IsNullOrWhiteSpace(opt.FaceAttrModel)
				? (attr.Contains(GenderAgeDetector.DefaultModelFile) ? GenderAgeDetector.DefaultModelFile : FaceNoneLabel)
				: opt.FaceAttrModel;
			fillfacecombo(efaceattr, attrItems, wantAttr, FaceNoneLabel);

			var root = FaceModels.ModelsRoot();
			lbfacehint.Text = onnx.Count > 0
				? string.Format(Loc.T("face.hint.found"), root, onnx.Count)
				: string.Format(Loc.T("face.hint.missing"), root);
			if (lbfacestatus != null)
				lbfacestatus.Text = onnx.Count > 0 ? Loc.T("ready") : Loc.T("face.status.nomodels");
		}
		catch (Exception ex) {
			lbfacehint.Text = string.Format(Loc.T("face.scan.fail"), ex.Message);
		}
		finally { faceUiLoading = false; }
	}

	static void fillfacecombo(ComboBox box, List<string> items, string prefer, string fallback) {
		box.Items.Clear();
		foreach (var m in items) box.Items.Add(m);
		if (box.Items.Count == 0) return;
		int idx = box.Items.IndexOf(prefer);
		if (idx < 0) idx = box.Items.IndexOf(fallback);
		box.SelectedIndex = idx >= 0 ? idx : 0;
	}

	void onfacemodelchanged() {
		if (faceUiLoading) return;
		disposefacepipe();
		clearfacesides();
		savefaceprefs();
		facelog($"模型切换 → 检测 {efacedet.SelectedItem} | 识别 {efacereg.SelectedItem}");
	}

	static TtsComputeMode parsefacecompute(string s) => (s ?? "Auto").Trim().ToLowerInvariant() switch {
		"gpu" or "cuda" => TtsComputeMode.Gpu,
		"cpu" => TtsComputeMode.Cpu,
		"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
		_ => TtsComputeMode.Auto,
	};

	TtsComputeMode facecurcompute() {
		if (efacecompute?.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode m)
			return m;
		return parsefacecompute(opt.FaceCompute);
	}

	void savefaceprefs() {
		try {
			opt.FaceCompute = facecurcompute().ToString();
			opt.FaceDetModel = efacedet.SelectedItem as string ?? "";
			opt.FaceRegModel = efacereg.SelectedItem as string ?? "";
			opt.FaceLmkModel = efacelmk.SelectedItem as string ?? "";
			opt.FaceAttrModel = efaceattr.SelectedItem as string ?? "";
			opt.FaceThreshold = (float)efacethresh.Value;
			AppConfig.Save(opt);
		}
		catch (Exception ex) { CaptureLog.Ex("savefaceprefs", ex); }
	}

	void facelog(string msg) {
		var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n";
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.BeginInvoke(() => facelog(msg));
			return;
		}
		efacelog.AppendText(line);
		efacelog.CaretIndex = efacelog.Text.Length;
		efacelog.ScrollToEnd();
	}

	void facesetstatus(bool left, string status, string source, FaceStatTone tone = FaceStatTone.Warn) {
		var lbStat = left ? lbfacestatL : lbfacestatR;
		var lbSrc = left ? lbfacesrcL : lbfacesrcR;
		lbStat.Text = status;
		lbSrc.Text = source;
		lbStat.Foreground = new SolidColorBrush(tone switch {
			FaceStatTone.Ok => Color.FromRgb(0, 140, 80),
			FaceStatTone.Bad => Color.FromRgb(200, 50, 50),
			_ => Color.FromRgb(200, 120, 40),
		});
	}

	void facesetbusy(bool left, bool busy) {
		if (left) faceBusyL = busy; else faceBusyR = busy;
		bfacepasteL.IsEnabled = !faceBusyL;
		bfaceclearL.IsEnabled = !faceBusyL;
		bfacepasteR.IsEnabled = !faceBusyR;
		bfaceclearR.IsEnabled = !faceBusyR;
		bfaceswap.IsEnabled = !faceBusyL && !faceBusyR;
	}

	void faceclear(bool left) {
		if (left) faceFeatL = null; else faceFeatR = null;
		if (left) {
			imgfaceL.Source = null;
			lbfacehintL.Visibility = Visibility.Visible;
		}
		else {
			imgfaceR.Source = null;
			lbfacehintR.Visibility = Visibility.Visible;
		}
		facesetstatus(left, Loc.T("face.feat.none"), Loc.T("face.src"));
		updatefacesim(false);
		facelog(string.Format(Loc.T("face.cleared"), faceside(left)));
	}

	void clearfacesides() {
		faceFeatL = faceFeatR = null;
		imgfaceL.Source = null;
		imgfaceR.Source = null;
		lbfacehintL.Visibility = Visibility.Visible;
		lbfacehintR.Visibility = Visibility.Visible;
		facesetstatus(true, Loc.T("face.feat.none"), Loc.T("face.src"));
		facesetstatus(false, Loc.T("face.feat.none"), Loc.T("face.src"));
		updatefacesim(false);
	}

	void faceswap() {
		(faceFeatL, faceFeatR) = (faceFeatR, faceFeatL);
		(imgfaceL.Source, imgfaceR.Source) = (imgfaceR.Source, imgfaceL.Source);
		(lbfacehintL.Visibility, lbfacehintR.Visibility) = (lbfacehintR.Visibility, lbfacehintL.Visibility);
		var s = lbfacestatL.Text; lbfacestatL.Text = lbfacestatR.Text; lbfacestatR.Text = s;
		var src = lbfacesrcL.Text; lbfacesrcL.Text = lbfacesrcR.Text; lbfacesrcR.Text = src;
		var fg = lbfacestatL.Foreground; lbfacestatL.Foreground = lbfacestatR.Foreground; lbfacestatR.Foreground = fg;
		updatefacesim();
	}

	void facesavefeat(bool left) {
		var feat = left ? faceFeatL : faceFeatR;
		if (feat == null) {
			MessageBox.Show(this, string.Format(Loc.T("face.nofeat.save"), faceside(left)), Loc.T("face.tip"));
			return;
		}
		var dlg = new SaveFileDialog {
			Filter = Loc.T("face.feat.filter"),
			FileName = (left ? "face_left" : "face_right") + ".feat"
		};
		if (dlg.ShowDialog(this) != true) return;
		try {
			FeatureFile.Save(dlg.FileName, feat);
			MessageBox.Show(this, string.Format(Loc.T("face.saved"), dlg.FileName), Loc.T("face.save.ok"));
		}
		catch (Exception ex) {
			MessageBox.Show(this, string.Format(Loc.T("face.save.fail"), ex.Message), Loc.T("face.err.title"), MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	void facepaste(bool left) {
		if (left && faceBusyL) return;
		if (!left && faceBusyR) return;
		BitmapSource img = null;
		try { img = ImageUtil.Fromclipboard(); } catch { }
		if (img == null) {
			MessageBox.Show(this, Loc.T("face.noclip"), Loc.T("face.tip"));
			return;
		}
		_ = faceloadbitmap(img, left, Loc.T("face.src.clipboard"));
	}

	static bool hasfacefiledrop(IDataObject data) => pickfacepath(data) != null;

	static string pickfacepath(IDataObject data) {
		if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return null;
		var files = data.GetData(DataFormats.FileDrop) as string[];
		if (files == null || files.Length == 0) return null;
		foreach (var f in files) {
			if (string.IsNullOrWhiteSpace(f) || !File.Exists(f)) continue;
			var ext = Path.GetExtension(f).ToLowerInvariant();
			if (ext is ".feat" or ".txt" || isfaceimg(ext))
				return f;
		}
		return null;
	}

	static bool isfaceimg(string ext) {
		foreach (var e in FaceImageExts)
			if (e == ext) return true;
		return false;
	}

	async Task faceloadpath(string path, bool left) {
		var ext = Path.GetExtension(path).ToLowerInvariant();
		if (ext is ".feat" or ".txt") {
			faceloadfeat(path, left);
			return;
		}
		if (!isfaceimg(ext)) {
			facesetstatus(left, Loc.T("face.feat.badtype"), string.Format(Loc.T("face.src.name"), Path.GetFileName(path)), FaceStatTone.Bad);
			return;
		}
		await faceloadfile(path, left);
	}

	void faceloadfeat(string path, bool left) {
		var side = faceside(left);
		var sw = Stopwatch.StartNew();
		try {
			var feat = FeatureFile.Load(path);
			sw.Stop();
			if (left) faceFeatL = feat; else faceFeatR = feat;
			if (left) { imgfaceL.Source = null; lbfacehintL.Text = Loc.T("face.featfile.hint"); lbfacehintL.Visibility = Visibility.Visible; }
			else { imgfaceR.Source = null; lbfacehintR.Text = Loc.T("face.featfile.hint"); lbfacehintR.Visibility = Visibility.Visible; }
			facesetstatus(left, Loc.T("face.feat.done"), string.Format(Loc.T("face.src.featfile"), Path.GetFileName(path)), FaceStatTone.Ok);
			facelog($"{side} 加载特征文件: {Path.GetFileName(path)} → {feat.Length}维 ({sw.Elapsed.TotalMilliseconds:F1}ms)");
			updatefacesim(true);
		}
		catch (Exception ex) {
			facesetstatus(left, Loc.T("face.feat.readfail"), string.Format(Loc.T("face.err.prefix"), ex.Message), FaceStatTone.Bad);
			facelog(side + " 特征文件读取失败: " + ex.Message);
			if (left) faceFeatL = null; else faceFeatR = null;
			updatefacesim(false);
		}
	}

	async Task faceloadfile(string path, bool left, string displayName = null) {
		if (left && faceBusyL) return;
		if (!left && faceBusyR) return;
		facesetbusy(left, true);
		var side = faceside(left);
		var name = displayName ?? Path.GetFileName(path);
		facesetstatus(left, Loc.T("face.feat.working"), string.Format(Loc.T("face.src.name"), name));
		facelog(side + " 开始处理: " + name);

		try {
			if (!FeaturePrompt.EnsureOpenCv(this)) {
				facesetstatus(left, Loc.T("face.feat.noopencv"), Loc.T("face.need.opencv"), FaceStatTone.Bad);
				return;
			}
			if (!FeaturePrompt.EnsureOcrOrt(this)) {
				facesetstatus(left, Loc.T("face.feat.noonnx"), Loc.T("face.need.onnx"), FaceStatTone.Bad);
				return;
			}
			if (!FeaturePrompt.EnsureFaceModels(this)) {
				facesetstatus(left, Loc.T("face.feat.nomodel"), Loc.T("face.need.model"), FaceStatTone.Bad);
				return;
			}

			Mat mat = null;
			try {
				mat = Cv2.ImRead(path, ImreadModes.Color);
				if (mat == null || mat.Empty())
					throw new IOException("无法读取图片");
			}
			catch (Exception ex) {
				facesetstatus(left, Loc.T("face.feat.imgfail"), string.Format(Loc.T("face.err.prefix"), ex.Message), FaceStatTone.Bad);
				facelog(side + " 图片读取失败: " + ex.Message);
				if (left) faceFeatL = null; else faceFeatR = null;
				return;
			}

			await facerunmat(mat, left, name);
		}
		finally { facesetbusy(left, false); }
	}

	async Task faceloadbitmap(BitmapSource src, bool left, string name) {
		if (left && faceBusyL) return;
		if (!left && faceBusyR) return;
		facesetbusy(left, true);
		facesetstatus(left, Loc.T("face.feat.working"), string.Format(Loc.T("face.src.name"), name));
		facelog(faceside(left) + " 开始处理: " + name);
		try {
			if (!FeaturePrompt.EnsureOpenCv(this)) {
				facesetstatus(left, Loc.T("face.feat.noopencv"), Loc.T("face.need.opencv"), FaceStatTone.Bad);
				return;
			}
			if (!FeaturePrompt.EnsureOcrOrt(this)) {
				facesetstatus(left, Loc.T("face.feat.noonnx"), Loc.T("face.need.onnx"), FaceStatTone.Bad);
				return;
			}
			if (!FeaturePrompt.EnsureFaceModels(this)) {
				facesetstatus(left, Loc.T("face.feat.nomodel"), Loc.T("face.need.model"), FaceStatTone.Bad);
				return;
			}
			Mat mat;
			try { mat = ImageUtil.Tobgr(src); }
			catch (Exception ex) {
				facesetstatus(left, Loc.T("face.feat.imgfail"), string.Format(Loc.T("face.err.prefix"), ex.Message), FaceStatTone.Bad);
				return;
			}
			await facerunmat(mat, left, name);
		}
		finally { facesetbusy(left, false); }
	}

	async Task facerunmat(Mat mat, bool left, string name) {
		var side = faceside(left);
		var lmkName = efacelmk.SelectedItem as string ?? "";
		var attrName = efaceattr.SelectedItem as string ?? "";
		var detName = efacedet.SelectedItem as string ?? "";
		var regName = efacereg.SelectedItem as string ?? "";
		bool runLmk = !isfacenone(lmkName);
		bool runAttr = !isfacenone(attrName);
		var mode = facecurcompute();

		FaceExtractResult result = null;
		float[] overlayPts = null;
		int overlayDim = 0, overlayNum = 0;
		double landmarkMs = 0, genderAgeMs = 0;
		GenderAgeResult? genderAge = null;
		string err = null;
		string ep = "";

		try {
			await Task.Run(() => {
				try {
					lock (faceLock) {
						ensurefacepipe(mode, detName, regName);
						ep = facePipe.EpLabel;
						result = facePipe.ExtractTimed(mat);
						if (runLmk && result?.Face != null) {
							var sw = Stopwatch.StartNew();
							ensurefacelmk(lmkName, mode);
							if (faceLmk != null) {
								overlayPts = faceLmk.Detect(mat, result.Face);
								overlayDim = faceLmk.LandmarkDim;
								overlayNum = faceLmk.LandmarkNum;
							}
							sw.Stop();
							landmarkMs = sw.Elapsed.TotalMilliseconds;
						}
						if (runAttr && result?.Face != null) {
							var sw = Stopwatch.StartNew();
							ensurefaceattr(attrName, mode);
							if (faceAttr != null)
								genderAge = faceAttr.Predict(mat, result.Face);
							sw.Stop();
							genderAgeMs = sw.Elapsed.TotalMilliseconds;
						}
					}
				}
				catch (Exception ex) { err = ex.Message; }
			});
		}
		catch (Exception ex) { err = ex.Message; }

		if (err != null) {
			facesetstatus(left, Loc.T("face.feat.err"), string.Format(Loc.T("face.err.prefix"), err), FaceStatTone.Bad);
			facelog(side + " 识别出错: " + err);
			if (left) faceFeatL = null; else faceFeatR = null;
			showfaceimg(left, ImageUtil.Frombgr(mat));
			mat.Dispose();
			updatefacesim(false);
			return;
		}

		if (result == null || result.Feature == null || result.Face == null) {
			int n = result != null ? result.FaceCount : 0;
			facesetstatus(left, Loc.T("face.feat.noface"), string.Format(Loc.T("face.src.name"), name), FaceStatTone.Warn);
			facelog($"{side} 加载 {result?.LoadMs:F0}ms | 检测 {n} 个 {result?.DetectMs:F0}ms → 未检测到人脸");
			if (left) faceFeatL = null; else faceFeatR = null;
			showfaceimg(left, ImageUtil.Frombgr(mat));
			mat.Dispose();
			updatefacesim(false);
			return;
		}

		drawfaceoverlay(mat, result.Face, overlayPts, overlayDim, overlayNum, genderAge);
		showfaceimg(left, ImageUtil.Frombgr(mat));
		mat.Dispose();

		if (left) faceFeatL = result.Feature; else faceFeatR = result.Feature;
		var attrText = genderAge.HasValue ? ", " + genderAge.Value : "";
		facesetstatus(left, Loc.T("face.feat.done"),
			string.Format(Loc.T("face.src.detail"), name, ep, result.Face.Score, attrText), FaceStatTone.Ok);
		facelog($"{side} 加载图片: {result.LoadMs:F0}ms");
		facelog($"{side} 检测人脸: {result.FaceCount}个, 置信度 {result.Face.Score:F2} ({result.DetectMs:F0}ms)");
		facelog($"{side} 特征提取: {result.Feature.Length} 维 ({result.ExtractMs:F0}ms) 后端={ep}");
		if (overlayPts != null)
			facelog($"{side} 关键点叠加: {overlayNum}点 {overlayDim}D ({landmarkMs:F0}ms)");
		if (genderAge.HasValue) {
			var ga = genderAge.Value;
			string raw = ga.RawOutput != null && ga.RawOutput.Length >= 3
				? $" 原始=[{ga.RawOutput[0]:F3},{ga.RawOutput[1]:F3},{ga.RawOutput[2]:F3}]"
				: "";
			facelog($"{side} 性别年龄: {ga}{raw} ({genderAgeMs:F0}ms)");
		}
		facelog($"{side} 完成: 总计 {result.TotalMs:F0}ms");
		updatefacesim(true);
	}

	void showfaceimg(bool left, BitmapSource bmp) {
		if (left) {
			imgfaceL.Source = bmp;
			lbfacehintL.Visibility = bmp == null ? Visibility.Visible : Visibility.Collapsed;
		}
		else {
			imgfaceR.Source = bmp;
			lbfacehintR.Visibility = bmp == null ? Visibility.Visible : Visibility.Collapsed;
		}
	}

	void ensurefacepipe(TtsComputeMode mode, string detName, string regName) {
		if (facePipe != null) return;
		if (string.IsNullOrWhiteSpace(detName)) detName = opt.FaceDetModel;
		if (string.IsNullOrWhiteSpace(regName)) regName = opt.FaceRegModel;
		if (string.IsNullOrWhiteSpace(detName) || string.IsNullOrWhiteSpace(regName))
			throw new InvalidOperationException("请先选择检测/识别模型（facemodels）");
		var detPath = FaceModels.PathOf(detName);
		var regPath = FaceModels.PathOf(regName);
		if (!File.Exists(detPath))
			throw new FileNotFoundException("人脸检测模型未找到: " + detPath, detPath);
		if (!File.Exists(regPath))
			throw new FileNotFoundException("人脸识别模型未找到: " + regPath, regPath);
		facePipe = new FacePipeline(detPath, regPath, 0.5f, mode);
		Dispatcher.BeginInvoke(() => facelog($"ONNX 流水线已创建: {detName} | {regName} | {facePipe.EpLabel}"));
	}

	void ensurefacelmk(string modelFileName, TtsComputeMode mode) {
		if (isfacenone(modelFileName)) return;
		if (faceLmk != null && faceLmkName == modelFileName) return;
		disposefacelmk();
		var path = FaceModels.PathOf(modelFileName);
		if (!File.Exists(path)) return;
		faceLmk = new LandmarkDetector(path, mode);
		faceLmkName = modelFileName;
	}

	void ensurefaceattr(string modelFileName, TtsComputeMode mode) {
		if (isfacenone(modelFileName)) return;
		if (faceAttr != null && faceAttrName == modelFileName) return;
		disposefaceattr();
		var path = FaceModels.PathOf(modelFileName);
		if (!File.Exists(path)) return;
		faceAttr = new GenderAgeDetector(path, mode);
		faceAttrName = modelFileName;
	}

	void disposefacepipe() {
		try { facePipe?.Dispose(); } catch { }
		facePipe = null;
	}

	void disposefacelmk() {
		try { faceLmk?.Dispose(); } catch { }
		faceLmk = null;
		faceLmkName = "";
	}

	void disposefaceattr() {
		try { faceAttr?.Dispose(); } catch { }
		faceAttr = null;
		faceAttrName = "";
	}

	void disposefaceall() {
		disposefacepipe();
		disposefacelmk();
		disposefaceattr();
	}

	void updatefacesim(bool logCompare = false) {
		if (faceFeatL == null || faceFeatR == null) {
			lbfacesim.Text = Loc.T("face.sim.wait");
			lbfacesim.Foreground = new SolidColorBrush(Colors.Gray);
			return;
		}
		var sw = Stopwatch.StartNew();
		float sim;
		try { sim = FaceSimilarity.Cosine(faceFeatL, faceFeatR); }
		catch (Exception ex) {
			lbfacesim.Text = string.Format(Loc.T("face.sim.err"), ex.Message);
			lbfacesim.Foreground = new SolidColorBrush(Color.FromRgb(200, 50, 50));
			return;
		}
		sw.Stop();
		float thresh = (float)efacethresh.Value;
		bool match = sim >= thresh;
		lbfacesim.Text = string.Format(Loc.T("face.sim.fmt"), sim, match ? Loc.T("face.sim.same") : Loc.T("face.sim.diff"), thresh);
		lbfacesim.Foreground = new SolidColorBrush(match
			? Color.FromRgb(0, 140, 80) : Color.FromRgb(200, 50, 50));
		if (logCompare)
			facelog($"{sim:F4} → {(match ? Loc.T("face.sim.same") : Loc.T("face.sim.diff"))} ({sw.Elapsed.TotalMilliseconds:F1}ms)");
	}

	static void drawfaceoverlay(Mat bgr, FaceBox face, float[] dense, int lmkDim, int lmkNum,
		GenderAgeResult? genderAge) {
		if (bgr == null || face == null) return;
		int w = bgr.Cols;
		int pen = Math.Max(2, w / 250);
		int dot = Math.Max(2, w / 180);
		var green = new Scalar(80, 200, 0);
		var pink = new Scalar(160, 80, 255);
		var blue = new Scalar(255, 120, 50);
		Cv2.Rectangle(bgr,
			new OpenCvSharp.Point((int)face.X1, (int)face.Y1),
			new OpenCvSharp.Point((int)face.X2, (int)face.Y2),
			green, pen);

		if (genderAge.HasValue)
			ImageUtil.Putcjk(bgr, genderAge.Value.ToString(), face.X1, face.Y1);

		if (dense != null && lmkNum > 0 && lmkDim >= 2) {
			int r = Math.Max(1, w / (lmkNum > 20 ? 320 : 180));
			for (int i = 0; i < lmkNum; i++) {
				int px = (int)dense[i * lmkDim];
				int py = (int)dense[i * lmkDim + 1];
				Cv2.Circle(bgr, new OpenCvSharp.Point(px, py), r, pink, -1);
			}
		}
		else if (face.Landmarks != null) {
			for (int i = 0; i < 5; i++) {
				int px = (int)face.Landmarks[i * 2];
				int py = (int)face.Landmarks[i * 2 + 1];
				Cv2.Circle(bgr, new OpenCvSharp.Point(px, py), dot, blue, -1);
			}
		}
	}

	void applyfacelang() {
		try {
			tabface.Header = Loc.T("tab.face");
			lbfacebrand.Text = Loc.T("tab.face");
			if (string.IsNullOrWhiteSpace(lbfacestatus.Text) || lbfacestatus.Text == "就绪" || lbfacestatus.Text == Loc.T("ready")
				|| lbfacestatus.Text == Loc.T("face.status.nomodels"))
				lbfacestatus.Text = Loc.T("ready");
			lbfacedet.Text = Loc.T("face.det");
			efacedet.ToolTip = Loc.T("face.det.tip");
			lbfacerec.Text = Loc.T("face.rec");
			efacereg.ToolTip = Loc.T("face.rec.tip");
			lbfacecompute.Text = Loc.T("face.compute");
			efacecompute.ToolTip = Loc.T("face.compute.tip");
			lbfacelmk.Text = Loc.T("face.lmk");
			efacelmk.ToolTip = Loc.T("face.lmk.tip");
			lbfaceattr.Text = Loc.T("face.attr");
			efaceattr.ToolTip = Loc.T("face.attr.tip");
			bfacereload.Content = Loc.T("reload.models");
			bfacereload.ToolTip = Loc.T("face.reload.tip");
			lbfacehint.Text = Loc.T("face.hint");
			updatefacesim(false);
			lbfaceleft.Text = Loc.T("face.left");
			lbfaceright.Text = Loc.T("face.right");
			bfacepasteL.Content = Loc.T("face.paste");
			bfacepasteR.Content = Loc.T("face.paste");
			bfacepasteL.ToolTip = Loc.T("face.paste.tip");
			bfacepasteR.ToolTip = Loc.T("face.paste.tip");
			bfaceclearL.Content = Loc.T("face.clear");
			bfaceclearR.Content = Loc.T("face.clear");
			bfacesaveL.Content = Loc.T("face.savefeat");
			bfacesaveR.Content = Loc.T("face.savefeat");
			bfacesaveL.ToolTip = Loc.T("face.savefeat.L.tip");
			bfacesaveR.ToolTip = Loc.T("face.savefeat.R.tip");
			if (imgfaceL.Source == null) lbfacehintL.Text = Loc.T("face.drop");
			if (imgfaceR.Source == null) lbfacehintR.Text = Loc.T("face.drop");
			bfaceswap.Content = Loc.T("face.swap");
			lbfacethreshlabel.Text = Loc.T("face.thresh");
			if ((lbfacesim.Text ?? "").Contains("等待") || (lbfacesim.Text ?? "").Contains("waiting"))
				lbfacesim.Text = Loc.T("face.sim.wait");
			applycomputebox(efacecompute);
		}
		catch { }
	}

	void cleanupfacetemps() {
		foreach (var f in faceTemps) {
			try { File.Delete(f); } catch { }
		}
		faceTemps.Clear();
	}
}
