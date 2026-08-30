using System.Diagnostics;
using System.Windows.Media;
using Microsoft.Win32;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>MainWindow：人脸识别 Tab（InsightFace ONNX）。</summary>
public partial class MainWindow {
	static readonly string[] FaceImageExts = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff"];
	const string FaceNone = "(无)";

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
		efacecompute.Items.Add(new ComboBoxItem { Content = "自动（CUDA→核显→CPU）", Tag = TtsComputeMode.Auto });
		efacecompute.Items.Add(new ComboBoxItem { Content = "GPU（NVIDIA CUDA）", Tag = TtsComputeMode.Gpu });
		efacecompute.Items.Add(new ComboBoxItem { Content = "核显（Intel DirectML）", Tag = TtsComputeMode.Igpu });
		efacecompute.Items.Add(new ComboBoxItem { Content = "CPU", Tag = TtsComputeMode.Cpu });
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
			facelog("已刷新 facemodels");
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

			var lmkItems = new List<string> { FaceNone };
			lmkItems.AddRange(lmk);
			fillfacecombo(efacelmk, lmkItems,
				string.IsNullOrWhiteSpace(opt.FaceLmkModel) ? FaceNone : opt.FaceLmkModel, FaceNone);

			var attrItems = new List<string> { FaceNone };
			attrItems.AddRange(attr);
			var wantAttr = string.IsNullOrWhiteSpace(opt.FaceAttrModel)
				? (attr.Contains(GenderAgeDetector.DefaultModelFile) ? GenderAgeDetector.DefaultModelFile : FaceNone)
				: opt.FaceAttrModel;
			fillfacecombo(efaceattr, attrItems, wantAttr, FaceNone);

			var root = FaceModels.ModelsRoot();
			lbfacehint.Text = onnx.Count > 0
				? $"模型：{root} · {onnx.Count} 个 ONNX。拖入图片或 .feat；两侧均有特征时自动比对。"
				: $"未找到模型 → {root}（将 InsightFace ONNX 放到程序旁 facemodels/）";
			if (lbfacestatus != null)
				lbfacestatus.Text = onnx.Count > 0 ? "就绪" : "未找到 facemodels";
		}
		catch (Exception ex) {
			lbfacehint.Text = "扫描失败: " + ex.Message;
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

	void facesetstatus(bool left, string status, string source) {
		var lbStat = left ? lbfacestatL : lbfacestatR;
		var lbSrc = left ? lbfacesrcL : lbfacesrcR;
		lbStat.Text = status;
		lbSrc.Text = source;
		var ok = status.StartsWith("特征: 已生成");
		var bad = status.Contains("未") || status.Contains("失败") || status.Contains("出错");
		lbStat.Foreground = new SolidColorBrush(ok
			? Color.FromRgb(0, 140, 80)
			: bad ? Color.FromRgb(200, 50, 50) : Color.FromRgb(200, 120, 40));
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
		facesetstatus(left, "特征: 未生成", "来源: —");
		updatefacesim(false);
		facelog((left ? "左侧" : "右侧") + " 已清除");
	}

	void clearfacesides() {
		faceFeatL = faceFeatR = null;
		imgfaceL.Source = null;
		imgfaceR.Source = null;
		lbfacehintL.Visibility = Visibility.Visible;
		lbfacehintR.Visibility = Visibility.Visible;
		facesetstatus(true, "特征: 未生成", "来源: —");
		facesetstatus(false, "特征: 未生成", "来源: —");
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
			MessageBox.Show(this, (left ? "左侧" : "右侧") + "尚未生成特征，无法保存。", "提示");
			return;
		}
		var dlg = new SaveFileDialog {
			Filter = "特征文件 (*.feat)|*.feat|所有文件 (*.*)|*.*",
			FileName = (left ? "face_left" : "face_right") + ".feat"
		};
		if (dlg.ShowDialog(this) != true) return;
		try {
			FeatureFile.Save(dlg.FileName, feat);
			MessageBox.Show(this, "已保存特征到:\n" + dlg.FileName, "保存成功");
		}
		catch (Exception ex) {
			MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	void facepaste(bool left) {
		if (left && faceBusyL) return;
		if (!left && faceBusyR) return;
		BitmapSource img = null;
		try { img = ImageUtil.Fromclipboard(); } catch { }
		if (img == null) {
			MessageBox.Show(this, "剪贴板中没有图片，请先复制或截图。", "提示");
			return;
		}
		_ = faceloadbitmap(img, left, "剪贴板粘贴");
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
			facesetstatus(left, "特征: 不支持的文件类型", "来源: " + Path.GetFileName(path));
			return;
		}
		await faceloadfile(path, left);
	}

	void faceloadfeat(string path, bool left) {
		var side = left ? "左侧" : "右侧";
		var sw = Stopwatch.StartNew();
		try {
			var feat = FeatureFile.Load(path);
			sw.Stop();
			if (left) faceFeatL = feat; else faceFeatR = feat;
			if (left) { imgfaceL.Source = null; lbfacehintL.Text = "特征文件\n（无图像）"; lbfacehintL.Visibility = Visibility.Visible; }
			else { imgfaceR.Source = null; lbfacehintR.Text = "特征文件\n（无图像）"; lbfacehintR.Visibility = Visibility.Visible; }
			facesetstatus(left, "特征: 已生成", "来源: 特征文件 " + Path.GetFileName(path));
			facelog($"{side} 加载特征文件: {Path.GetFileName(path)} → {feat.Length}维 ({sw.Elapsed.TotalMilliseconds:F1}ms)");
			updatefacesim(true);
		}
		catch (Exception ex) {
			facesetstatus(left, "特征: 读取失败", "错误: " + ex.Message);
			facelog(side + " 特征文件读取失败: " + ex.Message);
			if (left) faceFeatL = null; else faceFeatR = null;
			updatefacesim(false);
		}
	}

	async Task faceloadfile(string path, bool left, string displayName = null) {
		if (left && faceBusyL) return;
		if (!left && faceBusyR) return;
		facesetbusy(left, true);
		var side = left ? "左侧" : "右侧";
		var name = displayName ?? Path.GetFileName(path);
		facesetstatus(left, "特征: 识别中…", "来源: " + name);
		facelog(side + " 开始处理: " + name);

		try {
			if (!FeaturePrompt.EnsureOpenCv(this)) {
				facesetstatus(left, "特征: 缺少 OpenCV", "请安装 OpenCV 运行库");
				return;
			}
			if (!FeaturePrompt.EnsureOcrOrt(this)) {
				facesetstatus(left, "特征: 缺少 ONNX Runtime", "请安装 CPU/GPU/核显组件");
				return;
			}
			if (!FeaturePrompt.EnsureFaceModels(this)) {
				facesetstatus(left, "特征: 缺少人脸模型", "请安装 InsightFace buffalo_l");
				return;
			}

			Mat mat = null;
			try {
				mat = Cv2.ImRead(path, ImreadModes.Color);
				if (mat == null || mat.Empty())
					throw new IOException("无法读取图片");
			}
			catch (Exception ex) {
				facesetstatus(left, "特征: 图片读取失败", "错误: " + ex.Message);
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
		facesetstatus(left, "特征: 识别中…", "来源: " + name);
		facelog((left ? "左侧" : "右侧") + " 开始处理: " + name);
		try {
			if (!FeaturePrompt.EnsureOpenCv(this)) {
				facesetstatus(left, "特征: 缺少 OpenCV", "请安装 OpenCV 运行库");
				return;
			}
			if (!FeaturePrompt.EnsureOcrOrt(this)) {
				facesetstatus(left, "特征: 缺少 ONNX Runtime", "请安装 CPU/GPU/核显组件");
				return;
			}
			if (!FeaturePrompt.EnsureFaceModels(this)) {
				facesetstatus(left, "特征: 缺少人脸模型", "请安装 InsightFace buffalo_l");
				return;
			}
			Mat mat;
			try { mat = ImageUtil.Tobgr(src); }
			catch (Exception ex) {
				facesetstatus(left, "特征: 图片读取失败", "错误: " + ex.Message);
				return;
			}
			await facerunmat(mat, left, name);
		}
		finally { facesetbusy(left, false); }
	}

	async Task facerunmat(Mat mat, bool left, string name) {
		var side = left ? "左侧" : "右侧";
		var lmkName = efacelmk.SelectedItem as string ?? "";
		var attrName = efaceattr.SelectedItem as string ?? "";
		var detName = efacedet.SelectedItem as string ?? "";
		var regName = efacereg.SelectedItem as string ?? "";
		bool runLmk = lmkName != "" && lmkName != FaceNone;
		bool runAttr = attrName != "" && attrName != FaceNone;
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
			facesetstatus(left, "特征: 识别出错", "错误: " + err);
			facelog(side + " 识别出错: " + err);
			if (left) faceFeatL = null; else faceFeatR = null;
			showfaceimg(left, ImageUtil.Frombgr(mat));
			mat.Dispose();
			updatefacesim(false);
			return;
		}

		if (result == null || result.Feature == null || result.Face == null) {
			int n = result != null ? result.FaceCount : 0;
			facesetstatus(left, "特征: 未检测到人脸", "来源: " + name);
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
		facesetstatus(left, "特征: 已生成",
			$"来源: {name} (InsightFace/{ep}, 置信度 {result.Face.Score:F2}{attrText})");
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
		if (string.IsNullOrEmpty(modelFileName) || modelFileName == FaceNone) return;
		if (faceLmk != null && faceLmkName == modelFileName) return;
		disposefacelmk();
		var path = FaceModels.PathOf(modelFileName);
		if (!File.Exists(path)) return;
		faceLmk = new LandmarkDetector(path, mode);
		faceLmkName = modelFileName;
	}

	void ensurefaceattr(string modelFileName, TtsComputeMode mode) {
		if (string.IsNullOrEmpty(modelFileName) || modelFileName == FaceNone) return;
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
			lbfacesim.Text = "相似度: （等待两侧特征）";
			lbfacesim.Foreground = new SolidColorBrush(Colors.Gray);
			return;
		}
		var sw = Stopwatch.StartNew();
		float sim;
		try { sim = FaceSimilarity.Cosine(faceFeatL, faceFeatR); }
		catch (Exception ex) {
			lbfacesim.Text = "相似度: 无法对比（" + ex.Message + "）";
			lbfacesim.Foreground = new SolidColorBrush(Color.FromRgb(200, 50, 50));
			return;
		}
		sw.Stop();
		float thresh = (float)efacethresh.Value;
		bool match = sim >= thresh;
		lbfacesim.Text = $"相似度: {sim:F4}  →  {(match ? "同一人" : "不同人")} (阈值 {thresh:F2})";
		lbfacesim.Foreground = new SolidColorBrush(match
			? Color.FromRgb(0, 140, 80) : Color.FromRgb(200, 50, 50));
		if (logCompare)
			facelog($"对比特征值: {sim:F4} → {(match ? "同一人" : "不同人")} ({sw.Elapsed.TotalMilliseconds:F1}ms)");
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
