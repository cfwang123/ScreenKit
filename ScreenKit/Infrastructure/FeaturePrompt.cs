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
		sb.AppendLine($"使用「{featureTitle}」需要先安装：");
		sb.AppendLine();
		foreach (var k in missing)
			sb.AppendLine("· " + kindlabel(k) + "  （约 " + FeatureInstaller.FormatBytes(FeatureInstaller.ExpectedSize(k)) + "）");
		if (!string.IsNullOrWhiteSpace(detail)) {
			sb.AppendLine();
			sb.AppendLine(detail);
		}
		sb.AppendLine();
		sb.Append("是否打开「安装功能」窗口？");

		var own = owner ?? Application.Current?.MainWindow;
		var r = own != null
			? MessageBox.Show(own, sb.ToString(), "需要安装组件", MessageBoxButton.YesNo, MessageBoxImage.Information)
			: MessageBox.Show(sb.ToString(), "需要安装组件", MessageBoxButton.YesNo, MessageBoxImage.Information);
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
		EnsureKinds(owner, "图像 / OCR", "安装后即可截图识别、长截图等。", FeatureKind.NativeOpenCv);

	public static bool EnsurePdf(Window owner = null) =>
		EnsureKinds(owner, "PDF 工作台", "需要 Skia 与 PDFium 两个运行库。",
			FeatureKind.NativeSkia, FeatureKind.NativePdfium);

	public static bool EnsureFfmpeg(Window owner = null) =>
		EnsureKinds(owner, "录屏 / 音视频", "将 FFmpeg 4.4 shared 装到程序目录 ffmpeg64/。",
			FeatureKind.Ffmpeg);

	public static bool EnsureSherpa(Window owner = null) =>
		EnsureKinds(owner, "语音识别 / 语音合成", "需要 sherpa-onnx-c-api.dll（约 4–5 MB）。",
			FeatureKind.NativeSherpa);

	/// <summary>OCR 模型包：任一可用即可；全无则提示装 rapid-ch。</summary>
	public static bool EnsureOcrModels(Window owner = null) {
		try {
			var packs = ModelCatalog.Scan();
			if (packs != null && packs.Count > 0) return true;
		}
		catch { }
		return EnsureKinds(owner, "文字识别", "请安装 OCR 模型包（推荐 rapid-ch）。",
			FeatureKind.OcrRapidCh);
	}

	/// <summary>
	/// OCR 推理用 ONNX Runtime：已有 onnxcpu64 / onnxgpu64 / onnxdml64 任一即可；
	/// 全无则提示安装 CPU 包（按需，约 16MB）。
	/// </summary>
	public static bool EnsureOcrOrt(Window owner = null) {
		if (FeatureInstaller.HasAnyOrtNative()) return true;
		return EnsureKinds(owner, "文字识别",
			"需要 ONNX Runtime 原生库才能识别。未安装 GPU/核显时请安装 CPU 包（onnxcpu64）。",
			FeatureKind.OrtCpu);
	}

	/// <summary>ASR：无模型时提示安装 SenseVoice + 流式 Zipformer。</summary>
	public static bool EnsureAsrModels(Window owner = null) {
		try {
			var list = AsrModelScanner.Scan();
			if (list != null && list.Count > 0) return true;
		}
		catch { }
		return EnsureKinds(owner, "语音识别", "请安装离线与/或流式 ASR 模型。",
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
			? MessageBox.Show(own,
				"未找到 TTS 发音人模型。\n请打开「安装功能 → 发音人」下载模型到 ttsmodels。\n\n是否现在打开？",
				"需要安装组件", MessageBoxButton.YesNo, MessageBoxImage.Information)
			: MessageBox.Show(
				"未找到 TTS 发音人模型。\n是否打开安装窗口？",
				"需要安装组件", MessageBoxButton.YesNo, MessageBoxImage.Information);
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
		var msg = "未找到翻译 ONNX 模型。\n请将 opus-mt-zh-en-onnx / opus-mt-en-zh-onnx 放到程序目录 translatemodels/。";
		if (own != null)
			MessageBox.Show(own, msg, "需要安装组件", MessageBoxButton.OK, MessageBoxImage.Information);
		else
			MessageBox.Show(msg, "需要安装组件", MessageBoxButton.OK, MessageBoxImage.Information);
		return false;
	}

	/// <summary>人脸：无检测+识别 ONNX 时提示安装 buffalo_l。</summary>
	public static bool EnsureFaceModels(Window owner = null) {
		try {
			if (FaceModels.IsReady()) return true;
		}
		catch { }
		return EnsureKinds(owner, "人脸识别",
			"将 InsightFace buffalo_l 装到程序目录 facemodels/（检测+识别，可选关键点与性别年龄）。",
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
			MessageBox.Show(ex.Message, "安装功能", MessageBoxButton.OK, MessageBoxImage.Warning);
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
