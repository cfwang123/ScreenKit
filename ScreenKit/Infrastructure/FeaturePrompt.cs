using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 功能依赖缺失时：弹窗说明 → 可选打开「安装功能」并预勾相关项。
/// </summary>
static class FeaturePrompt {
	/// <summary>
	/// 提示用户安装缺失组件。返回 true 表示用户确认并打开了安装窗（不保证已装完）。
	/// </summary>
	public static bool OfferInstall(Window owner, string featureTitle, string detail, params FeatureKind[] kinds) {
		if (kinds == null || kinds.Length == 0) return false;
		var missing = kinds.Where(k => FeatureInstaller.Probe(k) != FeatureInstallState.Installed).ToList();
		if (missing.Count == 0) return true;

		var sb = new StringBuilder();
		sb.AppendLine(Loc.T("inst.need.body", featureTitle));
		sb.AppendLine();
		foreach (var k in missing)
			sb.AppendLine("· " + Loc.T($"feat.{k}.title") + "  (" + FeatureInstaller.FormatBytes(FeatureInstaller.ExpectedSize(k)) + ")");
		if (!string.IsNullOrWhiteSpace(detail)) {
			sb.AppendLine();
			sb.AppendLine(detail);
		}
		sb.AppendLine();
		sb.Append(Loc.T("inst.need.ask"));

		var own = owner ?? Application.Current?.MainWindow;
		var r = own != null
			? MessageBox.Show(own, sb.ToString(), Loc.T("inst.need"), MessageBoxButton.YesNo, MessageBoxImage.Information)
			: MessageBox.Show(sb.ToString(), Loc.T("inst.need"), MessageBoxButton.YesNo, MessageBoxImage.Information);
		if (r != MessageBoxResult.Yes) return false;

		openinstall(own, missing.ToArray());
		// 装完后再看是否就绪
		return missing.All(k => FeatureInstaller.Probe(k) == FeatureInstallState.Installed);
	}

	/// <summary>UI 线程安全：确保某运行库；缺失则提示安装。返回是否已可用。</summary>
	public static bool EnsureKinds(Window owner, string featureTitle, string detail, params FeatureKind[] kinds) {
		if (kinds == null || kinds.Length == 0) return true;
		if (kinds.All(k => FeatureInstaller.Probe(k) == FeatureInstallState.Installed))
			return true;

		// 必须在 UI 线程弹窗
		if (Application.Current?.Dispatcher != null
			&& !Application.Current.Dispatcher.CheckAccess()) {
			var ok = false;
			Application.Current.Dispatcher.Invoke(() => {
				ok = EnsureKinds(owner, featureTitle, detail, kinds);
			}, DispatcherPriority.Normal);
			return ok;
		}

		return OfferInstall(owner, featureTitle, detail, kinds)
			&& kinds.All(k => FeatureInstaller.Probe(k) == FeatureInstallState.Installed);
	}

	public static bool EnsureOpenCv(Window owner = null) =>
		EnsureKinds(owner, Loc.T("feat.prompt.opencv.title"), Loc.T("feat.prompt.opencv.detail"), FeatureKind.NativeOpenCv);

	public static bool EnsurePdf(Window owner = null) =>
		EnsureKinds(owner, Loc.T("feat.prompt.pdf.title"), Loc.T("feat.prompt.pdf.detail"),
			FeatureKind.NativeSkia, FeatureKind.NativePdfium);

	public static bool EnsureFfmpeg(Window owner = null) =>
		EnsureKinds(owner, Loc.T("feat.prompt.ffmpeg.title"), Loc.T("feat.prompt.ffmpeg.detail"),
			FeatureKind.Ffmpeg);

	public static bool EnsureSherpa(Window owner = null) =>
		EnsureKinds(owner, Loc.T("feat.prompt.sherpa.title"), Loc.T("feat.prompt.sherpa.detail"),
			FeatureKind.NativeSherpa);

	/// <summary>OCR 模型包：任一可用即可；全无则提示装 rapid-ch。</summary>
	public static bool EnsureOcrModels(Window owner = null) {
		try {
			var packs = ModelCatalog.Scan();
			if (packs != null && packs.Count > 0) return true;
		}
		catch { }
		return EnsureKinds(owner, Loc.T("feat.prompt.ocr.title"), Loc.T("feat.prompt.ocr.detail"),
			FeatureKind.OcrRapidCh);
	}

	/// <summary>
	/// OCR 推理用 ONNX Runtime：已有 onnxcpu64 / onnxgpu64 / onnxdml64 任一即可；
	/// 全无则提示安装 CPU 包（按需，约 16MB）。
	/// </summary>
	public static bool EnsureOcrOrt(Window owner = null) {
		if (FeatureInstaller.HasAnyOrtNative()) return true;
		return EnsureKinds(owner, Loc.T("feat.prompt.ocr.title"), Loc.T("feat.prompt.ocr.ort.detail"),
			FeatureKind.OrtCpu);
	}

	/// <summary>ASR：无模型时提示安装 SenseVoice + 流式 Zipformer。</summary>
	public static bool EnsureAsrModels(Window owner = null) {
		try {
			var list = AsrModelScanner.Scan();
			if (list != null && list.Count > 0) return true;
		}
		catch { }
		return EnsureKinds(owner, Loc.T("feat.prompt.asr.title"), Loc.T("feat.prompt.asr.detail"),
			FeatureKind.AsrSenseVoice, FeatureKind.AsrStreamZipformer);
	}

	/// <summary>发音人：打开安装窗 TTS Tab 提示。</summary>
	public static bool EnsureTtsModels(Window owner = null) {
		try {
			var list = TtsModelScanner.Scan();
			if (list != null && list.Count > 0) return true;
		}
		catch { }

		var own = owner ?? Application.Current?.MainWindow;
		var r = own != null
			? MessageBox.Show(own, Loc.T("feat.prompt.tts.body"), Loc.T("inst.need"), MessageBoxButton.YesNo, MessageBoxImage.Information)
			: MessageBox.Show(Loc.T("feat.prompt.tts.body.short"), Loc.T("inst.need"), MessageBoxButton.YesNo, MessageBoxImage.Information);
		if (r != MessageBoxResult.Yes) return false;
		openinstall(own, null, openTtsTab: true);
		try {
			var list = TtsModelScanner.Scan();
			return list != null && list.Count > 0;
		}
		catch { return false; }
	}

	/// <summary>翻译模型。</summary>
	public static bool EnsureTranslateModels(Window owner = null) {
		try {
			var list = TranslateModelScanner.Scan();
			if (list != null && list.Any(m => m.IsReady)) return true;
		}
		catch { }
		var own = owner ?? Application.Current?.MainWindow;
		var msg = Loc.T("feat.prompt.tr.body");
		if (own != null)
			MessageBox.Show(own, msg, Loc.T("inst.need"), MessageBoxButton.OK, MessageBoxImage.Information);
		else
			MessageBox.Show(msg, Loc.T("inst.need"), MessageBoxButton.OK, MessageBoxImage.Information);
		return false;
	}

	/// <summary>人脸：无检测+识别 ONNX 时提示安装 buffalo_l。</summary>
	public static bool EnsureFaceModels(Window owner = null) {
		try {
			if (FaceModels.IsReady()) return true;
		}
		catch { }
		return EnsureKinds(owner, Loc.T("feat.prompt.face.title"), Loc.T("feat.prompt.face.detail"),
			FeatureKind.FaceInsight);
	}

	static void openinstall(Window owner, FeatureKind[] preferKinds, bool openTtsTab = false) {
		try {
			var win = new InstallFeaturesWindow(firstRun: false, preferSelect: preferKinds, openTtsTab: openTtsTab);
			if (owner != null) {
				try {
					win.Owner = owner;
					win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
				}
				catch {
					win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
				}
			}
			else
				win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
			win.ShowDialog();
			// 通知主窗刷新
			if (win.NeedRefresh && owner is MainWindow mw)
				mw.AfterFeatureInstall(win.NeedRestart);
		}
		catch (Exception ex) {
			CaptureLog.Ex("FeaturePrompt.openinstall", ex);
			MessageBox.Show(ex.Message, Loc.T("inst.open.fail"), MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	static string kindlabel(FeatureKind k) => k switch {
		FeatureKind.NativeOpenCv => "OpenCV 运行库",
		FeatureKind.NativeSkia => "Skia 渲染库",
		FeatureKind.NativePdfium => "PDFium (pdfium.dll)",
		FeatureKind.NativeSherpa => "Sherpa (sherpa-onnx-c-api.dll)",
		FeatureKind.Ffmpeg => "FFmpeg (ffmpeg64)",
		FeatureKind.OcrRapidCh => "OCR 模型 rapid-ch",
		FeatureKind.OcrUmi => "OCR 模型 umi",
		FeatureKind.OcrRapidI18n => "OCR 模型 rapid-i18n",
		FeatureKind.AsrSenseVoice => "ASR SenseVoice",
		FeatureKind.AsrStreamZipformer => "ASR 流式 Zipformer",
		FeatureKind.AsrWhisperTiny => "ASR Whisper tiny",
		FeatureKind.AsrWhisperBase => "ASR Whisper base",
		FeatureKind.CudaGpu => "NVIDIA CUDA (onnxgpu64)",
		FeatureKind.DirectMl => "核显 DirectML (onnxdml64)",
		FeatureKind.OrtCpu => "ONNX Runtime CPU (onnxcpu64)",
		FeatureKind.FaceInsight => "人脸 InsightFace buffalo_l",
		_ => k.ToString(),
	};
}
