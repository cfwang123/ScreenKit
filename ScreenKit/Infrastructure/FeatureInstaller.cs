using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>可安装功能项种类。</summary>
public enum FeatureKind {
	OcrRapidCh,
	OcrUmi,
	OcrRapidI18n,
	AsrSenseVoice,
	AsrStreamZipformer,
	AsrWhisperTiny,
	AsrWhisperBase,
	AsrSileroVad,
	CudaGpu,
	DirectMl,
	/// <summary>CPU 推理 ORT（onnxcpu64，约 16MB，未装 GPU/核显时 OCR 需要）。</summary>
	OrtCpu,
	Ffmpeg,
	/// <summary>OpenCvSharpExtern.dll（OCR 必需，约 61MB）。</summary>
	NativeOpenCv,
	/// <summary>libSkiaSharp.dll（PDF 渲染，约 11MB）。</summary>
	NativeSkia,
	/// <summary>pdfium.dll（PDF 渲染，约 6MB，按需）。</summary>
	NativePdfium,
	/// <summary>sherpa-onnx-c-api.dll（ASR/TTS 约 4–5MB；本机 net48 随编译保留，精简包按需安装）。</summary>
	NativeSherpa,
	/// <summary>InsightFace buffalo_l：检测+识别+关键点+性别年龄，约 326MB。</summary>
	FaceInsight,
}

/// <summary>安装探测结果。</summary>
enum FeatureInstallState {
	Missing,
	Partial,
	Installed,
}

/// <summary>安装进度（含下载字节）。</summary>
sealed class InstallProgress {
	/// <summary>当前功能项整体进度 0–1。</summary>
	public double Overall { get; set; }
	/// <summary>已下载/已处理字节（本项累计或当前文件）。</summary>
	public long BytesDone { get; set; }
	/// <summary>总字节；0 表示未知。</summary>
	public long BytesTotal { get; set; }
	/// <summary>当前文件名。</summary>
	public string FileName { get; set; }
	/// <summary>补充说明（复制/解压等）。</summary>
	public string Note { get; set; }

	public string BytesText {
		get {
			if (BytesTotal > 0)
				return FeatureInstaller.FormatBytes(BytesDone) + " / " + FeatureInstaller.FormatBytes(BytesTotal);
			if (BytesDone > 0)
				return FeatureInstaller.FormatBytes(BytesDone) + " / ?";
			return "";
		}
	}
}

/// <summary>目录中一项可安装功能的描述与状态。</summary>
sealed class FeatureItem {
	public FeatureKind Kind { get; set; }
	public string Id { get; set; }
	public string Category { get; set; }
	public string Title { get; set; }
	public string Detail { get; set; }
	public FeatureInstallState State { get; set; }
	public string StateText { get; set; }
	public bool Selected { get; set; }
	/// <summary>安装后需重启进程才生效（GPU/核显运行库）。</summary>
	public bool NeedsRestart { get; set; }
	/// <summary>预期下载/安装体积（字节，约数）。</summary>
	public long SizeBytes { get; set; }
	/// <summary>列表展示用体积文案，如「约 75 MB」或「本地 72.1 MB」。</summary>
	public string SizeText { get; set; }
}

/// <summary>
/// 应用内安装：OCR/ASR 模型、CUDA GPU、核显 DirectML、FFmpeg。
/// 中文系统（界面或系统区域）优先国内镜像（ModelScope / HF 镜像 / ghproxy）。
/// </summary>
static class FeatureInstaller {
	const string OrtGpuVer = "1.27.1";
	const string OrtDmlVer = "1.24.4";
	const string DmlVer = "1.15.4";
	const string MsRapid = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2";
	const string AsrRelease = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models";
	const string FaceBuffaloLZip = "https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip";
	const string FaceBuffaloLHf = "https://huggingface.co/deepinsight/insightface/resolve/main/models/buffalo_l.zip";
	static readonly string[] FaceBuffaloLFiles = [
		"det_10g.onnx", "w600k_r50.onnx", "genderage.onnx", "2d106det.onnx", "1k3d68.onnx",
	];

	static readonly string[] FfmpegUrls = [
		"https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2022-12-31-12-37/ffmpeg-n4.4.3-6-g8cdb37d416-win64-gpl-shared.zip",
		"https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2023-04-30-12-40/ffmpeg-n4.4.4-1-g9541743d0e-win64-gpl-shared.zip",
	];

	static readonly HttpClient Http = createhttp();

	static HttpClient createhttp() {
		try {
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		catch { }
		var c = new HttpClient(HttpProxy.CreateHandler());
		c.Timeout = TimeSpan.FromMinutes(60);
		c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ScreenKit-FeatureInstaller/1.0");
		return c;
	}

	public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
	public static string CacheDir => Path.Combine(BaseDir, "tmp", "install-cache");
	public static string OcrModelsDir => Path.Combine(BaseDir, "ocrmodels");
	public static string AsrModelsDir => Path.Combine(BaseDir, "asrmodels");
	public static string OnnxGpuDir => Path.Combine(BaseDir, "onnxgpu64");
	public static string OnnxDmlDir => Path.Combine(BaseDir, "onnxdml64");
	public static string OnnxCpuDir => Path.Combine(BaseDir, "onnxcpu64");
	public static string FfmpegDir => Path.Combine(BaseDir, "ffmpeg64");
	public static string FaceModelsDir => Path.Combine(BaseDir, "facemodels");

	/// <summary>任一可用 ORT 原生库（CPU / CUDA / DML 包），足够跑 CPU EP 推理。</summary>
	public static bool HasAnyOrtNative() =>
		probecpu() == FeatureInstallState.Installed
		|| File.Exists(Path.Combine(OnnxGpuDir, "onnxruntime.dll"))
		|| File.Exists(Path.Combine(OnnxDmlDir, "onnxruntime.dll"));

	/// <summary>界面中文，或系统 UI/区域为中文时，优先国内镜像。</summary>
	public static bool PreferCnMirrors() {
		if (Loc.IsZh) return true;
		try {
			var ui = CultureInfo.CurrentUICulture;
			if (ui != null && ui.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
				return true;
			var reg = RegionInfo.CurrentRegion;
			if (reg != null && (reg.TwoLetterISORegionName == "CN" || reg.TwoLetterISORegionName == "HK"
				|| reg.TwoLetterISORegionName == "TW" || reg.TwoLetterISORegionName == "MO"
				|| reg.TwoLetterISORegionName == "SG"))
				return true;
		}
		catch { }
		return false;
	}

	public static string MirrorHint() => PreferCnMirrors()
		? Loc.T("inst.mirror.cn")
		: Loc.T("inst.mirror.en");

	/// <summary>
	/// 打开安装窗时的推荐勾选（未装才勾）：
	/// OCR 相关（OpenCV + rapid-ch）+ FFmpeg；语音识别前 2 项；推理加速不勾。
	/// </summary>
	public static readonly FeatureKind[] RecommendedSelect = [
		FeatureKind.NativeOpenCv,
		FeatureKind.OrtCpu,
		FeatureKind.OcrRapidCh,
		FeatureKind.AsrSenseVoice,
		FeatureKind.AsrStreamZipformer,
		FeatureKind.Ffmpeg,
	];

	/// <summary>首次启动向导：推荐项 + OpenCV / CPU ORT / Sherpa / FFmpeg（不含 GPU/核显）。</summary>
	public static readonly FeatureKind[] FirstRunDefaults = [
		FeatureKind.NativeOpenCv,
		FeatureKind.OrtCpu,
		FeatureKind.NativeSherpa,
		FeatureKind.OcrRapidCh,
		FeatureKind.AsrSenseVoice,
		FeatureKind.AsrStreamZipformer,
		FeatureKind.Ffmpeg,
	];

	/// <param name="firstRunDefaults">true：勾选 <see cref="FirstRunDefaults"/>。</param>
	/// <param name="preferSelect">
	/// 非空：使用前提示，仅勾选这些项；
	/// 否则（非 firstRun）：勾选 <see cref="RecommendedSelect"/>（不是「全部未装」）。
	/// </param>
	public static List<FeatureItem> BuildCatalog(bool firstRunDefaults = false, FeatureKind[] preferSelect = null) {
		var list = new List<FeatureItem> {
			make(FeatureKind.NativeOpenCv, "native"),
			make(FeatureKind.NativeSkia, "native"),
			make(FeatureKind.NativePdfium, "native"),
			make(FeatureKind.NativeSherpa, "native"),
			make(FeatureKind.OrtCpu, "native"),
			make(FeatureKind.OcrRapidCh, "ocr"),
			make(FeatureKind.OcrUmi, "ocr"),
			make(FeatureKind.OcrRapidI18n, "ocr"),
			make(FeatureKind.AsrSenseVoice, "asr"),
			make(FeatureKind.AsrStreamZipformer, "asr"),
			make(FeatureKind.AsrWhisperTiny, "asr"),
			make(FeatureKind.AsrWhisperBase, "asr"),
			make(FeatureKind.AsrSileroVad, "asr"),
			make(FeatureKind.FaceInsight, "face"),
			make(FeatureKind.CudaGpu, "accel", true),
			make(FeatureKind.DirectMl, "accel", true),
			make(FeatureKind.Ffmpeg, "media"),
		};
		HashSet<FeatureKind> selectSet;
		if (firstRunDefaults)
			selectSet = new HashSet<FeatureKind>(FirstRunDefaults);
		else if (preferSelect != null && preferSelect.Length > 0)
			selectSet = new HashSet<FeatureKind>(preferSelect);
		else
			// 默认：OCR 仅 rapid-ch；ASR 前 2；加速不勾（见 RecommendedSelect）
			selectSet = new HashSet<FeatureKind>(RecommendedSelect);
		foreach (var it in list) {
			refreshstate(it);
			it.Selected = selectSet.Contains(it.Kind) && it.State != FeatureInstallState.Installed;
		}
		return list;
	}

	static FeatureItem make(FeatureKind kind, string cat, bool restart = false) => new() {
		Kind = kind,
		Id = kind.ToString(),
		Category = cat,
		Title = Loc.T($"feat.{kind}.title"),
		Detail = Loc.T($"feat.{kind}.detail"),
		NeedsRestart = restart,
		SizeBytes = ExpectedSize(kind),
		SizeText = Loc.T("feat.size.about", FormatBytes(ExpectedSize(kind))),
	};

	public static void RefreshState(FeatureItem it) => refreshstate(it);

	/// <summary>探测安装状态（供使用前提示）。</summary>
	public static FeatureInstallState Probe(FeatureKind kind) => probe(kind);

	/// <summary>各功能包预期体积（约数，用于列表与进度分母）。</summary>
	public static long ExpectedSize(FeatureKind kind) => kind switch {
		FeatureKind.NativeOpenCv => NativeRuntime.OpenCvSizeHint,
		FeatureKind.NativeSkia => NativeRuntime.SkiaSizeHint,
		FeatureKind.NativePdfium => NativeRuntime.PdfiumSizeHint,
		FeatureKind.NativeSherpa => NativeRuntime.SherpaSizeHint,
		FeatureKind.OcrRapidCh => 18L * 1024 * 1024,
		FeatureKind.OcrUmi => 78L * 1024 * 1024,
		FeatureKind.OcrRapidI18n => 42L * 1024 * 1024,
		FeatureKind.AsrSenseVoice => 230L * 1024 * 1024,
		FeatureKind.AsrStreamZipformer => 75L * 1024 * 1024,
		FeatureKind.AsrWhisperTiny => 78L * 1024 * 1024,
		FeatureKind.AsrWhisperBase => 148L * 1024 * 1024,
		FeatureKind.AsrSileroVad => 2L * 1024 * 1024,
		// 完整 CUDA 重发行库很大；仅 EP 约数十 MB
		FeatureKind.CudaGpu => 1200L * 1024 * 1024,
		FeatureKind.DirectMl => 18L * 1024 * 1024,
		FeatureKind.OrtCpu => 16L * 1024 * 1024,
		FeatureKind.Ffmpeg => 72L * 1024 * 1024,
		FeatureKind.FaceInsight => 326L * 1024 * 1024,
		_ => 0,
	};

	public static string FormatBytes(long bytes) {
		if (bytes < 0) bytes = 0;
		if (bytes < 1024) return bytes + " B";
		double kb = bytes / 1024.0;
		if (kb < 1024) return kb.ToString("0.#", CultureInfo.InvariantCulture) + " KB";
		double mb = kb / 1024.0;
		if (mb < 1024) return mb.ToString(mb >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture) + " MB";
		double gb = mb / 1024.0;
		return gb.ToString("0.##", CultureInfo.InvariantCulture) + " GB";
	}

	static void refreshstate(FeatureItem it) {
		it.State = probe(it.Kind);
		it.StateText = it.State switch {
			FeatureInstallState.Installed => Loc.T("inst.installed"),
			FeatureInstallState.Partial => Loc.T("inst.partial"),
			_ => Loc.T("inst.missing"),
		};
		// 体积：已装显示本机占用，否则显示预期下载约数
		var onDisk = measuresize(it.Kind);
		if (onDisk > 0 && it.State != FeatureInstallState.Missing) {
			it.SizeBytes = onDisk;
			it.SizeText = Loc.T("feat.size.local", FormatBytes(onDisk));
		}
		else {
			it.SizeBytes = ExpectedSize(it.Kind);
			it.SizeText = "约 " + FormatBytes(it.SizeBytes);
		}
	}

	static long measuresize(FeatureKind kind) {
		try {
			switch (kind) {
			case FeatureKind.NativeOpenCv:
				return filesize(Path.Combine(BaseDir, "OpenCvSharpExtern.dll"));
			case FeatureKind.NativeSkia:
				return filesize(Path.Combine(BaseDir, "libSkiaSharp.dll"));
			case FeatureKind.NativePdfium:
				return filesize(Path.Combine(BaseDir, "pdfium.dll"));
			case FeatureKind.NativeSherpa:
				return filesize(Path.Combine(BaseDir, "sherpa-onnx-c-api.dll"));
			case FeatureKind.OcrRapidCh: return dirsize(Path.Combine(OcrModelsDir, "rapid-ch"));
			case FeatureKind.OcrUmi: return dirsize(Path.Combine(OcrModelsDir, "umi"));
			case FeatureKind.OcrRapidI18n: return dirsize(Path.Combine(OcrModelsDir, "rapid-i18n"));
			case FeatureKind.AsrSenseVoice: return asrdirsize("sense-voice");
			case FeatureKind.AsrStreamZipformer: return asrdirsize("streaming-zipformer");
			case FeatureKind.AsrWhisperTiny: return asrdirsize("whisper-tiny");
			case FeatureKind.AsrWhisperBase: return asrdirsize("whisper-base");
			case FeatureKind.AsrSileroVad: {
				var p = Path.Combine(AsrModelsDir, "silero_vad.onnx");
				return File.Exists(p) ? new FileInfo(p).Length : 0;
			}
			case FeatureKind.CudaGpu: return dirsize(OnnxGpuDir);
			case FeatureKind.DirectMl: return dirsize(OnnxDmlDir);
			case FeatureKind.OrtCpu: return dirsize(OnnxCpuDir);
			case FeatureKind.Ffmpeg: return dirsize(FfmpegDir);
			case FeatureKind.FaceInsight: return dirsize(FaceModelsDir);
			default: return 0;
			}
		}
		catch { return 0; }
	}

	static long filesize(string path) {
		try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
		catch { return 0; }
	}

	static long asrdirsize(string nameHint) {
		if (!Directory.Exists(AsrModelsDir)) return 0;
		long sum = 0;
		foreach (var dir in Directory.GetDirectories(AsrModelsDir)) {
			var name = Path.GetFileName(dir) ?? "";
			if (name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) < 0) continue;
			sum += dirsize(dir);
		}
		return sum;
	}

	static long dirsize(string dir) {
		if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
		long n = 0;
		try {
			foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) {
				try { n += new FileInfo(f).Length; } catch { }
			}
		}
		catch { }
		return n;
	}

	static FeatureInstallState probe(FeatureKind kind) {
		switch (kind) {
		case FeatureKind.NativeOpenCv:
			return NativeRuntime.HasOpenCv() ? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.NativeSkia:
			return NativeRuntime.HasSkia() ? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.NativePdfium:
			return NativeRuntime.HasPdfium() ? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.NativeSherpa:
			return NativeRuntime.HasSherpa() ? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.OcrRapidCh:
			return ocrpackok(Path.Combine(OcrModelsDir, "rapid-ch"),
				"ch_PP-OCRv4_det_mobile.onnx", "ch_PP-OCRv4_rec_mobile.onnx", "ppocr_keys_v1.txt")
				? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.OcrUmi:
			return probeumi();
		case FeatureKind.OcrRapidI18n:
			return ocrpackok(Path.Combine(OcrModelsDir, "rapid-i18n"),
				"ch_PP-OCRv4_det_mobile.onnx", "en_PP-OCRv4_rec_mobile.onnx", "en_dict.txt")
				? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.AsrSenseVoice:
			return asrdirready(AsrModelsDir, "sense-voice", "model.int8.onnx", "model.onnx")
				? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.AsrStreamZipformer:
			return asrdirready(AsrModelsDir, "streaming-zipformer", "encoder.int8.onnx", "encoder.onnx")
				? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.AsrWhisperTiny:
			return asrdirready(AsrModelsDir, "whisper-tiny", "tiny-encoder.int8.onnx", "tiny-encoder.onnx")
				? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.AsrWhisperBase:
			return asrdirready(AsrModelsDir, "whisper-base", "base-encoder.int8.onnx", "base-encoder.onnx")
				? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.AsrSileroVad:
			return File.Exists(Path.Combine(AsrModelsDir, "silero_vad.onnx"))
				? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.CudaGpu:
			return probecuda();
		case FeatureKind.DirectMl:
			return probedml();
		case FeatureKind.OrtCpu:
			return probecpu();
		case FeatureKind.Ffmpeg:
			return Directory.Exists(FfmpegDir)
				&& Directory.GetFiles(FfmpegDir, "avcodec-*.dll").Length > 0
				? FeatureInstallState.Installed : FeatureInstallState.Missing;
		case FeatureKind.FaceInsight:
			return probeface();
		default:
			return FeatureInstallState.Missing;
		}
	}

	static bool ocrpackok(string dir, params string[] files) {
		if (!Directory.Exists(dir)) return false;
		foreach (var f in files)
			if (!File.Exists(Path.Combine(dir, f))) return false;
		return true;
	}

	/// <summary>umi：至少 det+cls+中文 rec+字典+configs，多语种齐全为 Installed，否则 Partial。</summary>
	static FeatureInstallState probeumi() {
		var dir = Path.Combine(OcrModelsDir, "umi");
		if (!Directory.Exists(dir)) return FeatureInstallState.Missing;
		// 核心：中文 det/cls/rec + keys（兼容 infer 命名或下载后的 server 重命名）
		string[] core = [
			"ch_ppocr_mobile_v2.0_cls_infer.onnx",
			"ch_PP-OCRv4_det_infer.onnx",
			"rec_ch_PP-OCRv4_infer.onnx",
		];
		var hasCore = core.All(f => File.Exists(Path.Combine(dir, f)));
		var hasKeys = File.Exists(Path.Combine(dir, "dict_chinese.txt"))
			|| File.Exists(Path.Combine(dir, "ppocr_keys_v1.txt"));
		var hasCfg = File.Exists(Path.Combine(dir, "configs.txt"));
		if (!hasCore || !hasKeys || !hasCfg) {
			// 残缺
			if (Directory.GetFiles(dir, "*.onnx").Length > 0)
				return FeatureInstallState.Partial;
			return FeatureInstallState.Missing;
		}
		// 多语种 rec 齐全则完整
		string[] multi = [
			"rec_en_PP-OCRv3_infer.onnx",
			"rec_chinese_cht_PP-OCRv3_infer.onnx",
			"rec_japan_PP-OCRv3_infer.onnx",
			"rec_korean_PP-OCRv3_infer.onnx",
			"rec_cyrillic_PP-OCRv3_infer.onnx",
		];
		if (multi.All(f => File.Exists(Path.Combine(dir, f))))
			return FeatureInstallState.Installed;
		return FeatureInstallState.Partial;
	}

	static bool asrdirready(string root, string nameHint, params string[] modelFiles) {
		if (!Directory.Exists(root)) return false;
		foreach (var dir in Directory.GetDirectories(root)) {
			var name = Path.GetFileName(dir) ?? "";
			if (name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) < 0) continue;
			var hasModel = modelFiles.Any(f => File.Exists(Path.Combine(dir, f)));
			var hasTokens = File.Exists(Path.Combine(dir, "tokens.txt"))
				|| Directory.GetFiles(dir, "*-tokens.txt").Length > 0
				|| File.Exists(Path.Combine(dir, "tiny-tokens.txt"))
				|| File.Exists(Path.Combine(dir, "base-tokens.txt"));
			if (hasModel && hasTokens) return true;
		}
		return false;
	}

	static FeatureInstallState probecuda() {
		var ort = Path.Combine(OnnxGpuDir, "onnxruntime.dll");
		var ep = Path.Combine(OnnxGpuDir, "onnxruntime_providers_cuda.dll");
		var shared = Path.Combine(OnnxGpuDir, "onnxruntime_providers_shared.dll");
		if (!File.Exists(ort) || !File.Exists(ep) || !File.Exists(shared))
			return FeatureInstallState.Missing;
		var hasCudart = File.Exists(Path.Combine(OnnxGpuDir, "cudart64_13.dll"))
			|| File.Exists(Path.Combine(OnnxGpuDir, "cudart64_12.dll"));
		return hasCudart ? FeatureInstallState.Installed : FeatureInstallState.Partial;
	}

	static FeatureInstallState probedml() {
		var ort = Path.Combine(OnnxDmlDir, "onnxruntime.dll");
		if (!File.Exists(ort)) return FeatureInstallState.Missing;
		var dml = Path.Combine(OnnxDmlDir, "DirectML.dll");
		return File.Exists(dml) ? FeatureInstallState.Installed : FeatureInstallState.Partial;
	}

	static FeatureInstallState probecpu() {
		var ort = Path.Combine(OnnxCpuDir, "onnxruntime.dll");
		if (!File.Exists(ort)) return FeatureInstallState.Missing;
		// 过滤 System32 级 stub / 损坏文件
		try {
			if (new FileInfo(ort).Length < 1_000_000)
				return FeatureInstallState.Partial;
		}
		catch {
			return FeatureInstallState.Partial;
		}
		return FeatureInstallState.Installed;
	}

	/// <summary>安装一项；通过 log / progress（含字节）回报。</summary>
	public static async Task InstallAsync(
		FeatureKind kind,
		IProgress<string> log,
		IProgress<InstallProgress> progress,
		CancellationToken ct) {
		switch (kind) {
		case FeatureKind.NativeOpenCv:
			await NativeRuntime.InstallOpenCv(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.NativeSkia:
			await NativeRuntime.InstallSkia(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.NativePdfium:
			await NativeRuntime.InstallPdfium(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.NativeSherpa:
			await NativeRuntime.InstallSherpa(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.OcrRapidCh:
			await installrapidch(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.OcrUmi:
			await installumi(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.OcrRapidI18n:
			await installrapidi18n(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.AsrSenseVoice:
			await installasrarchive(log, progress, ct,
				"sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17.tar.bz2",
				"sense-voice", ExpectedSize(kind)).ConfigureAwait(false);
			break;
		case FeatureKind.AsrStreamZipformer:
			await installasrarchive(log, progress, ct,
				"sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30.tar.bz2",
				"streaming-zipformer", ExpectedSize(kind)).ConfigureAwait(false);
			break;
		case FeatureKind.AsrWhisperTiny:
			await installasrarchive(log, progress, ct,
				"sherpa-onnx-whisper-tiny.tar.bz2", "whisper-tiny", ExpectedSize(kind)).ConfigureAwait(false);
			break;
		case FeatureKind.AsrWhisperBase:
			await installasrarchive(log, progress, ct,
				"sherpa-onnx-whisper-base.tar.bz2", "whisper-base", ExpectedSize(kind)).ConfigureAwait(false);
			break;
		case FeatureKind.AsrSileroVad:
			await installsilero(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.CudaGpu:
			installcuda(log, progress);
			break;
		case FeatureKind.DirectMl:
			await installdml(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.OrtCpu:
			await installortcpu(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.Ffmpeg:
			await installffmpeg(log, progress, ct).ConfigureAwait(false);
			break;
		case FeatureKind.FaceInsight:
			await installfaceinsight(log, progress, ct).ConfigureAwait(false);
			break;
		default:
			throw new InvalidOperationException("未知功能: " + kind);
		}
	}

	/// <summary>删除已安装组件（文件/目录）。</summary>
	public static void Uninstall(FeatureKind kind, IProgress<string> log) {
		switch (kind) {
		case FeatureKind.NativeOpenCv:
			NativeRuntime.UninstallOpenCv(log);
			break;
		case FeatureKind.NativeSkia:
			NativeRuntime.UninstallSkia(log);
			break;
		case FeatureKind.NativePdfium:
			NativeRuntime.UninstallPdfium(log);
			break;
		case FeatureKind.NativeSherpa:
			NativeRuntime.UninstallSherpa(log);
			break;
		case FeatureKind.OcrRapidCh:
			deletedir(Path.Combine(OcrModelsDir, "rapid-ch"), log);
			break;
		case FeatureKind.OcrUmi:
			deletedir(Path.Combine(OcrModelsDir, "umi"), log);
			break;
		case FeatureKind.OcrRapidI18n:
			deletedir(Path.Combine(OcrModelsDir, "rapid-i18n"), log);
			break;
		case FeatureKind.AsrSenseVoice:
			deleteasrbyhint("sense-voice", log);
			break;
		case FeatureKind.AsrStreamZipformer:
			deleteasrbyhint("streaming-zipformer", log);
			break;
		case FeatureKind.AsrWhisperTiny:
			deleteasrbyhint("whisper-tiny", log);
			break;
		case FeatureKind.AsrWhisperBase:
			deleteasrbyhint("whisper-base", log);
			break;
		case FeatureKind.AsrSileroVad:
			deletefile(Path.Combine(AsrModelsDir, "silero_vad.onnx"), log);
			break;
		case FeatureKind.CudaGpu:
			deletedir(OnnxGpuDir, log);
			break;
		case FeatureKind.DirectMl:
			deletedir(OnnxDmlDir, log);
			break;
		case FeatureKind.OrtCpu:
			deletedir(OnnxCpuDir, log);
			break;
		case FeatureKind.Ffmpeg:
			deletedir(FfmpegDir, log);
			break;
		case FeatureKind.FaceInsight:
			uninstallface(log);
			break;
		default:
			throw new InvalidOperationException("未知功能: " + kind);
		}
	}

	static void deleteasrbyhint(string nameHint, IProgress<string> log) {
		if (!Directory.Exists(AsrModelsDir)) {
			log?.Report("无 asrmodels 目录");
			return;
		}
		foreach (var dir in Directory.GetDirectories(AsrModelsDir)) {
			var name = Path.GetFileName(dir) ?? "";
			if (name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) < 0) continue;
			deletedir(dir, log);
		}
	}

	static void deletedir(string dir, IProgress<string> log) {
		if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) {
			log?.Report("跳过（不存在）: " + dir);
			return;
		}
		try {
			Directory.Delete(dir, true);
			log?.Report("已删除目录 " + dir);
		}
		catch (Exception ex) {
			log?.Report("删除目录失败 " + dir + ": " + ex.Message);
			throw;
		}
	}

	static void deletefile(string path, IProgress<string> log) {
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
			log?.Report("跳过（不存在）: " + path);
			return;
		}
		try {
			File.Delete(path);
			log?.Report("已删除 " + path);
		}
		catch (Exception ex) {
			log?.Report("删除失败 " + path + ": " + ex.Message);
			throw;
		}
	}

	static void reportprog(IProgress<InstallProgress> progress, double overall,
		long done = 0, long total = 0, string file = null, string note = null) {
		progress?.Report(new InstallProgress {
			Overall = overall < 0 ? 0 : (overall > 1 ? 1 : overall),
			BytesDone = done,
			BytesTotal = total,
			FileName = file,
			Note = note,
		});
	}

	// ───────── OCR ─────────

	static async Task installrapidch(IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		var dst = Path.Combine(OcrModelsDir, "rapid-ch");
		Directory.CreateDirectory(dst);
		if (trylocalcopy("rapid-ch", dst, log)) {
			writerapidchconfigs(dst);
			reportprog(progress, 1, note: "本地复制完成");
			return;
		}
		var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
			["ch_PP-OCRv4_det_mobile.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/det/ch_PP-OCRv4_det_mobile.onnx",
				"https://github.com/GreatV/oar-ocr/releases/download/v0.3.0/pp-ocrv4_mobile_det.onnx"),
			["ch_PP-OCRv4_rec_mobile.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/rec/ch_PP-OCRv4_rec_mobile.onnx",
				"https://github.com/GreatV/oar-ocr/releases/download/v0.3.0/pp-ocrv4_mobile_rec.onnx"),
			["ch_ppocr_mobile_v2.0_cls_mobile.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_mobile.onnx"),
			["ppocr_keys_v1.txt"] = ExpandUrls(
				$"{MsRapid}/paddle/PP-OCRv4/rec/ch_PP-OCRv4_rec_mobile/ppocr_keys_v1.txt",
				"https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/release/2.7/ppocr/utils/ppocr_keys_v1.txt"),
		};
		await downloadmap(map, dst, log, progress, ExpectedSize(FeatureKind.OcrRapidCh), ct).ConfigureAwait(false);
		writerapidchconfigs(dst);
		log?.Report("OCR rapid-ch 完成");
	}

	/// <summary>
	/// Umi 多语言包：优先本地种子；否则从 ModelScope RapidOCR 下载 server/mobile 并重命名为 Umi 文件名。
	/// （历史 install.ps1 仅支持本地种子，故此前未进「安装功能」列表。）
	/// </summary>
	static async Task installumi(IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		var dst = Path.Combine(OcrModelsDir, "umi");
		Directory.CreateDirectory(dst);
		if (trylocalcopy("umi", dst, log)) {
			// 种子若无 configs 则补写
			if (!File.Exists(Path.Combine(dst, "configs.txt")))
				writeumiconfigs(dst);
			else
				writeumipackjson(dst);
			// 字典别名
			ensuredictalias(dst, "ppocr_keys_v1.txt", "dict_chinese.txt");
			reportprog(progress, 1, note: "本地复制完成");
			if (probeumi() == FeatureInstallState.Missing)
				throw new InvalidOperationException("本地 umi 种子不完整");
			log?.Report("OCR umi 完成（本地）");
			return;
		}

		log?.Report("下载 Umi 风格模型（ModelScope RapidOCR → 重命名）…");
		// dest 文件名 → 源 URL（ModelScope）；下载后直接写到 dest 名
		var files = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
			// det / cls / 中文 rec（server 质量更接近历史 umi infer）
			["ch_PP-OCRv4_det_infer.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/det/ch_PP-OCRv4_det_server.onnx",
				$"{MsRapid}/onnx/PP-OCRv4/det/ch_PP-OCRv4_det_mobile.onnx"),
			["ch_ppocr_mobile_v2.0_cls_infer.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_mobile.onnx"),
			["rec_ch_PP-OCRv4_infer.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/rec/ch_PP-OCRv4_rec_server.onnx",
				$"{MsRapid}/onnx/PP-OCRv4/rec/ch_PP-OCRv4_rec_mobile.onnx"),
			["dict_chinese.txt"] = ExpandUrls(
				$"{MsRapid}/paddle/PP-OCRv4/rec/ch_PP-OCRv4_rec_mobile/ppocr_keys_v1.txt"),
			// 多语种 rec + dict（mobile 包，文件名按 Umi 约定）
			["rec_en_PP-OCRv3_infer.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/rec/en_PP-OCRv4_rec_mobile.onnx"),
			["dict_en.txt"] = ExpandUrls(
				$"{MsRapid}/paddle/PP-OCRv4/rec/en_PP-OCRv4_rec_mobile/en_dict.txt"),
			["rec_chinese_cht_PP-OCRv3_infer.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/rec/chinese_cht_PP-OCRv3_rec_mobile.onnx"),
			["dict_chinese_cht.txt"] = ExpandUrls(
				$"{MsRapid}/paddle/PP-OCRv4/rec/chinese_cht_PP-OCRv3_rec_mobile/chinese_cht_dict.txt"),
			["rec_japan_PP-OCRv3_infer.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile.onnx"),
			["dict_japan.txt"] = ExpandUrls(
				$"{MsRapid}/paddle/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile/japan_dict.txt"),
			["rec_korean_PP-OCRv3_infer.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/rec/korean_PP-OCRv4_rec_mobile.onnx"),
			["dict_korean.txt"] = ExpandUrls(
				$"{MsRapid}/paddle/PP-OCRv4/rec/korean_PP-OCRv4_rec_mobile/korean_dict.txt"),
			["rec_cyrillic_PP-OCRv3_infer.onnx"] = ExpandUrls(
				$"{MsRapid}/onnx/PP-OCRv4/rec/cyrillic_PP-OCRv3_rec_mobile.onnx"),
			["dict_cyrillic.txt"] = ExpandUrls(
				$"{MsRapid}/paddle/PP-OCRv4/rec/cyrillic_PP-OCRv3_rec_mobile/cyrillic_dict.txt"),
		};
		await downloadmap(files, dst, log, progress, ExpectedSize(FeatureKind.OcrUmi), ct).ConfigureAwait(false);
		// 兼容：部分工具找 ppocr_keys_v1
		ensuredictalias(dst, "dict_chinese.txt", "ppocr_keys_v1.txt");
		// 可选：det-v3 变体用同一 det 复制一份（configs 里可引用）
		var detV4 = Path.Combine(dst, "ch_PP-OCRv4_det_infer.onnx");
		var detV3 = Path.Combine(dst, "ch_PP-OCRv3_det_infer.onnx");
		if (File.Exists(detV4) && !File.Exists(detV3)) {
			File.Copy(detV4, detV3, true);
			log?.Report("复制 det-v4 → ch_PP-OCRv3_det_infer.onnx（兼容 configs 变体）");
		}
		writeumiconfigs(dst);
		if (probeumi() == FeatureInstallState.Missing)
			throw new InvalidOperationException("umi 下载后仍不完整，请查看日志");
		log?.Report("OCR umi 完成（ModelScope）");
	}

	static void ensuredictalias(string dir, string from, string to) {
		var a = Path.Combine(dir, from);
		var b = Path.Combine(dir, to);
		if (File.Exists(a) && !File.Exists(b)) {
			try { File.Copy(a, b, true); } catch { }
		}
	}

	static void writeumiconfigs(string dir) {
		// 与程序旁现有 umi/configs.txt 结构一致；无独立 det-v3 时两行都指向可用 det
		var hasDetV3 = File.Exists(Path.Combine(dir, "ch_PP-OCRv3_det_infer.onnx"));
		var detV3 = hasDetV3 ? "ch_PP-OCRv3_det_infer.onnx" : "ch_PP-OCRv4_det_infer.onnx";
		var sb = new StringBuilder();
		void block(string title, string det, string rec, string keys) {
			sb.AppendLine(title);
			sb.AppendLine(det);
			sb.AppendLine("ch_ppocr_mobile_v2.0_cls_infer.onnx");
			sb.AppendLine(rec);
			sb.AppendLine(keys);
			sb.AppendLine();
		}
		block("简体中文 (det-v4)", "ch_PP-OCRv4_det_infer.onnx", "rec_ch_PP-OCRv4_infer.onnx", "dict_chinese.txt");
		block("简体中文 (det-v3)", detV3, "rec_ch_PP-OCRv4_infer.onnx", "dict_chinese.txt");
		if (File.Exists(Path.Combine(dir, "rec_en_PP-OCRv3_infer.onnx")))
			block("English", detV3, "rec_en_PP-OCRv3_infer.onnx", "dict_en.txt");
		if (File.Exists(Path.Combine(dir, "rec_chinese_cht_PP-OCRv3_infer.onnx")))
			block("繁體中文", detV3, "rec_chinese_cht_PP-OCRv3_infer.onnx", "dict_chinese_cht.txt");
		if (File.Exists(Path.Combine(dir, "rec_japan_PP-OCRv3_infer.onnx")))
			block("日本語", detV3, "rec_japan_PP-OCRv3_infer.onnx", "dict_japan.txt");
		if (File.Exists(Path.Combine(dir, "rec_korean_PP-OCRv3_infer.onnx")))
			block("한국어", detV3, "rec_korean_PP-OCRv3_infer.onnx", "dict_korean.txt");
		if (File.Exists(Path.Combine(dir, "rec_cyrillic_PP-OCRv3_infer.onnx")))
			block("Русский", detV3, "rec_cyrillic_PP-OCRv3_infer.onnx", "dict_cyrillic.txt");
		File.WriteAllText(Path.Combine(dir, "configs.txt"), sb.ToString().TrimEnd() + "\n", new UTF8Encoding(false));
		writeumipackjson(dir);
	}

	static void writeumipackjson(string dir) {
		writepackjson(dir, "Umi-OCR（多语言）", "Umi-OCR (multilingual)", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["简体中文 (det-v4)"] = "Simplified Chinese (det-v4)",
			["简体中文 (det-v3)"] = "Simplified Chinese (det-v3)",
			["English"] = "English",
			["繁體中文"] = "Traditional Chinese",
			["日本語"] = "Japanese",
			["한국어"] = "Korean",
			["Русский"] = "Russian",
		});
	}

	static async Task installrapidi18n(IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		var dst = Path.Combine(OcrModelsDir, "rapid-i18n");
		Directory.CreateDirectory(dst);
		if (trylocalcopy("rapid-i18n", dst, log)) {
			// 仍补全可能缺失文件
		}
		// 可从 rapid-ch 复用
		var ch = Path.Combine(OcrModelsDir, "rapid-ch");
		foreach (var name in new[] {
			"ch_PP-OCRv4_det_mobile.onnx", "ch_ppocr_mobile_v2.0_cls_mobile.onnx",
			"ch_PP-OCRv4_rec_mobile.onnx", "ppocr_keys_v1.txt",
		}) {
			var to = Path.Combine(dst, name);
			var from = Path.Combine(ch, name);
			if (!File.Exists(to) && File.Exists(from)) {
				File.Copy(from, to, true);
				log?.Report($"复用 rapid-ch/{name}");
			}
		}
		var files = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
			["ch_PP-OCRv4_det_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/det/ch_PP-OCRv4_det_mobile.onnx"),
			["ch_ppocr_mobile_v2.0_cls_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_mobile.onnx"),
			["ch_PP-OCRv4_rec_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/rec/ch_PP-OCRv4_rec_mobile.onnx"),
			["ppocr_keys_v1.txt"] = ExpandUrls($"{MsRapid}/paddle/PP-OCRv4/rec/ch_PP-OCRv4_rec_mobile/ppocr_keys_v1.txt"),
			["en_PP-OCRv4_rec_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/rec/en_PP-OCRv4_rec_mobile.onnx"),
			["en_dict.txt"] = ExpandUrls($"{MsRapid}/paddle/PP-OCRv4/rec/en_PP-OCRv4_rec_mobile/en_dict.txt"),
			["chinese_cht_PP-OCRv3_rec_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/rec/chinese_cht_PP-OCRv3_rec_mobile.onnx"),
			["chinese_cht_dict.txt"] = ExpandUrls($"{MsRapid}/paddle/PP-OCRv4/rec/chinese_cht_PP-OCRv3_rec_mobile/chinese_cht_dict.txt"),
			["japan_PP-OCRv4_rec_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile.onnx"),
			["japan_dict.txt"] = ExpandUrls($"{MsRapid}/paddle/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile/japan_dict.txt"),
			["korean_PP-OCRv4_rec_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/rec/korean_PP-OCRv4_rec_mobile.onnx"),
			["korean_dict.txt"] = ExpandUrls($"{MsRapid}/paddle/PP-OCRv4/rec/korean_PP-OCRv4_rec_mobile/korean_dict.txt"),
			["cyrillic_PP-OCRv3_rec_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/rec/cyrillic_PP-OCRv3_rec_mobile.onnx"),
			["cyrillic_dict.txt"] = ExpandUrls($"{MsRapid}/paddle/PP-OCRv4/rec/cyrillic_PP-OCRv3_rec_mobile/cyrillic_dict.txt"),
			["latin_PP-OCRv3_rec_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/rec/latin_PP-OCRv3_rec_mobile.onnx"),
			["latin_dict.txt"] = ExpandUrls($"{MsRapid}/paddle/PP-OCRv4/rec/latin_PP-OCRv3_rec_mobile/latin_dict.txt"),
			["arabic_PP-OCRv4_rec_mobile.onnx"] = ExpandUrls($"{MsRapid}/onnx/PP-OCRv4/rec/arabic_PP-OCRv4_rec_mobile.onnx"),
			["arabic_dict.txt"] = ExpandUrls($"{MsRapid}/paddle/PP-OCRv4/rec/arabic_PP-OCRv4_rec_mobile/arabic_dict.txt"),
		};
		await downloadmap(files, dst, log, progress, ExpectedSize(FeatureKind.OcrRapidI18n), ct).ConfigureAwait(false);
		writerapidi18nconfigs(dst);
		log?.Report("OCR rapid-i18n 完成");
	}

	static void writerapidchconfigs(string dir) {
		var lines = new[] {
			"简体中文 mobile",
			"ch_PP-OCRv4_det_mobile.onnx",
			"ch_ppocr_mobile_v2.0_cls_mobile.onnx",
			"ch_PP-OCRv4_rec_mobile.onnx",
			"ppocr_keys_v1.txt",
		};
		File.WriteAllLines(Path.Combine(dir, "configs.txt"), lines, new UTF8Encoding(false));
		writepackjson(dir, "Rapid mobile 简中", "Rapid mobile Simplified Chinese",
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["简体中文 mobile"] = "Simplified Chinese (mobile)",
			});
	}

	static void writerapidi18nconfigs(string dir) {
		var block = @"简体中文
ch_PP-OCRv4_det_mobile.onnx
ch_ppocr_mobile_v2.0_cls_mobile.onnx
ch_PP-OCRv4_rec_mobile.onnx
ppocr_keys_v1.txt

English
ch_PP-OCRv4_det_mobile.onnx
ch_ppocr_mobile_v2.0_cls_mobile.onnx
en_PP-OCRv4_rec_mobile.onnx
en_dict.txt

繁體中文
ch_PP-OCRv4_det_mobile.onnx
ch_ppocr_mobile_v2.0_cls_mobile.onnx
chinese_cht_PP-OCRv3_rec_mobile.onnx
chinese_cht_dict.txt

日本語
ch_PP-OCRv4_det_mobile.onnx
ch_ppocr_mobile_v2.0_cls_mobile.onnx
japan_PP-OCRv4_rec_mobile.onnx
japan_dict.txt

한국어
ch_PP-OCRv4_det_mobile.onnx
ch_ppocr_mobile_v2.0_cls_mobile.onnx
korean_PP-OCRv4_rec_mobile.onnx
korean_dict.txt

Русский / Cyrillic
ch_PP-OCRv4_det_mobile.onnx
ch_ppocr_mobile_v2.0_cls_mobile.onnx
cyrillic_PP-OCRv3_rec_mobile.onnx
cyrillic_dict.txt

Latin (FR/DE/ES/…)
ch_PP-OCRv4_det_mobile.onnx
ch_ppocr_mobile_v2.0_cls_mobile.onnx
latin_PP-OCRv3_rec_mobile.onnx
latin_dict.txt

Arabic
ch_PP-OCRv4_det_mobile.onnx
ch_ppocr_mobile_v2.0_cls_mobile.onnx
arabic_PP-OCRv4_rec_mobile.onnx
arabic_dict.txt
";
		File.WriteAllText(Path.Combine(dir, "configs.txt"), block.Trim() + "\n", new UTF8Encoding(false));
		writepackjson(dir, "Rapid 全语种", "Rapid all-languages",
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["简体中文"] = "Simplified Chinese",
				["English"] = "English",
				["繁體中文"] = "Traditional Chinese",
				["日本語"] = "Japanese",
				["한국어"] = "Korean",
				["Русский / Cyrillic"] = "Russian / Cyrillic",
				["Latin (FR/DE/ES/…)"] = "Latin (FR/DE/ES/…)",
				["Arabic"] = "Arabic",
			});
	}

	/// <summary>模型包旁 pack.json：name / nameEn / variants（英文显示名）。</summary>
	static void writepackjson(string dir, string nameZh, string nameEn, Dictionary<string, string> variantEn) {
		if (string.IsNullOrWhiteSpace(dir)) return;
		try {
			Directory.CreateDirectory(dir);
			var node = new JsonObject {
				["name"] = nameZh ?? "",
				["nameEn"] = nameEn ?? "",
			};
			var vs = new JsonObject();
			if (variantEn != null) {
				foreach (var kv in variantEn)
					vs[kv.Key] = kv.Value;
			}
			node["variants"] = vs;
			var opts = new JsonSerializerOptions {
				WriteIndented = true,
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			};
			File.WriteAllText(Path.Combine(dir, "pack.json"),
				node.ToJsonString(opts) + "\n", new UTF8Encoding(false));
		}
		catch (Exception ex) {
			CaptureLog.Ex("writepackjson " + dir, ex);
		}
	}

	// ───────── ASR ─────────

	static async Task installasrarchive(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct,
		string archiveName, string nameHint, long expectedBytes) {
		if (probebyhint(nameHint)) {
			log?.Report($"已存在，跳过: {nameHint}");
			reportprog(progress, 1, note: "已存在");
			return;
		}

		Directory.CreateDirectory(AsrModelsDir);
		Directory.CreateDirectory(CacheDir);
		var zipPath = Path.Combine(CacheDir, archiveName);
		// GitHub release + HF 备选（经 ExpandUrls 做国内代理）
		var primary = new List<string> { $"{AsrRelease}/{archiveName}" };
		if (archiveName.IndexOf("sense-voice", StringComparison.OrdinalIgnoreCase) >= 0)
			primary.Add($"https://huggingface.co/csukuangfj/sherpa-onnx-models/resolve/main/{archiveName}");
		var urls = ExpandUrls(primary.ToArray());

		await downloadfirst(urls, zipPath, log, progress, ct,
			expectedTotal: expectedBytes, overallWeight: 0.9).ConfigureAwait(false);
		log?.Report("解压 " + archiveName + " …");
		var len = File.Exists(zipPath) ? new FileInfo(zipPath).Length : expectedBytes;
		reportprog(progress, 0.92, len, len, archiveName, "解压中…");
		extractarchive(zipPath, AsrModelsDir, log);
		reportprog(progress, 1, len, len, archiveName, "解压完成");
		if (!probebyhint(nameHint))
			throw new InvalidOperationException($"解压后未识别到模型（期望含 {nameHint}），请检查 asrmodels");
		log?.Report("ASR " + nameHint + " 完成");
	}

	static bool probebyhint(string nameHint) {
		if (nameHint.IndexOf("sense", StringComparison.OrdinalIgnoreCase) >= 0)
			return asrdirready(AsrModelsDir, "sense-voice", "model.int8.onnx", "model.onnx");
		if (nameHint.IndexOf("zipformer", StringComparison.OrdinalIgnoreCase) >= 0)
			return asrdirready(AsrModelsDir, "streaming-zipformer", "encoder.int8.onnx", "encoder.onnx");
		if (nameHint.IndexOf("tiny", StringComparison.OrdinalIgnoreCase) >= 0)
			return asrdirready(AsrModelsDir, "whisper-tiny", "tiny-encoder.int8.onnx", "tiny-encoder.onnx");
		if (nameHint.IndexOf("base", StringComparison.OrdinalIgnoreCase) >= 0)
			return asrdirready(AsrModelsDir, "whisper-base", "base-encoder.int8.onnx", "base-encoder.onnx");
		return false;
	}

	static async Task installsilero(IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		Directory.CreateDirectory(AsrModelsDir);
		var dest = Path.Combine(AsrModelsDir, "silero_vad.onnx");
		if (File.Exists(dest) && new FileInfo(dest).Length > 1000) {
			log?.Report("silero_vad.onnx 已存在");
			reportprog(progress, 1, note: "已存在");
			return;
		}
		var urls = ExpandUrls($"{AsrRelease}/silero_vad.onnx");
		await downloadfirst(urls, dest, log, progress, ct,
			expectedTotal: ExpectedSize(FeatureKind.AsrSileroVad)).ConfigureAwait(false);
		log?.Report("silero_vad.onnx 完成");
	}

	// ───────── 人脸 InsightFace ─────────

	static FeatureInstallState probeface() {
		try {
			if (FaceModels.IsReady()) return FeatureInstallState.Installed;
			var n = FaceModels.ListOnnx().Count;
			return n > 0 ? FeatureInstallState.Partial : FeatureInstallState.Missing;
		}
		catch { return FeatureInstallState.Missing; }
	}

	static async Task installfaceinsight(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		if (FaceModels.IsReady()) {
			log?.Report("已有检测+识别模型，跳过");
			reportprog(progress, 1, note: "已存在");
			return;
		}

		Directory.CreateDirectory(FaceModelsDir);
		var dstFull = Path.GetFullPath(FaceModelsDir);
		foreach (var c in libcandidates("WPF_OCR_FACE_LIB", "facemodels")) {
			ct.ThrowIfCancellationRequested();
			if (!Directory.Exists(c)) continue;
			var srcFull = Path.GetFullPath(c);
			if (string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase)) continue;
			var files = Directory.GetFiles(c, "*.onnx");
			if (files.Length == 0) continue;
			log?.Report("从本地复制人脸模型: " + c);
			long done = 0, total = 0;
			foreach (var f in files) {
				try { total += new FileInfo(f).Length; } catch { }
			}
			foreach (var f in files) {
				var name = Path.GetFileName(f);
				File.Copy(f, Path.Combine(FaceModelsDir, name), true);
				try { done += new FileInfo(f).Length; } catch { }
				reportprog(progress, total > 0 ? 0.2 * done / total : 0.2, done, total, name, "本地复制");
			}
			if (FaceModels.IsReady()) {
				reportprog(progress, 1, done, total > 0 ? total : done, note: "本地复制完成");
				log?.Report("人脸模型完成（本地）");
				return;
			}
		}

		Directory.CreateDirectory(CacheDir);
		var zipPath = Path.Combine(CacheDir, "buffalo_l.zip");
		var expect = ExpectedSize(FeatureKind.FaceInsight);
		var urls = ExpandUrls(FaceBuffaloLZip, FaceBuffaloLHf);
		await downloadfirst(urls, zipPath, log, progress, ct,
			expectedTotal: expect, overallWeight: 0.9).ConfigureAwait(false);

		var unpack = Path.Combine(CacheDir, "face-buffalo_l-unpack");
		if (Directory.Exists(unpack)) {
			try { Directory.Delete(unpack, true); } catch { }
		}
		Directory.CreateDirectory(unpack);
		log?.Report("解压 buffalo_l.zip …");
		var zlen = File.Exists(zipPath) ? new FileInfo(zipPath).Length : expect;
		reportprog(progress, 0.92, zlen, zlen, "buffalo_l.zip", "解压中…");
		ZipFile.ExtractToDirectory(zipPath, unpack);
		int copied = 0;
		foreach (var f in Directory.GetFiles(unpack, "*.onnx", SearchOption.AllDirectories)) {
			var name = Path.GetFileName(f);
			File.Copy(f, Path.Combine(FaceModelsDir, name), true);
			copied++;
			log?.Report("人脸模型 " + name + " (" + FormatBytes(new FileInfo(f).Length) + ")");
		}
		reportprog(progress, 1, zlen, zlen, "buffalo_l.zip", "完成");
		if (!FaceModels.IsReady())
			throw new InvalidOperationException(
				copied == 0
					? "zip 中未找到 onnx，请检查 buffalo_l.zip"
					: "解压后仍缺少检测或识别模型（期望 det_10g / w600k_r50）");
		log?.Report("人脸 buffalo_l 完成");
	}

	static void uninstallface(IProgress<string> log) {
		foreach (var name in FaceBuffaloLFiles)
			deletefile(Path.Combine(FaceModelsDir, name), log);
		foreach (var extra in new[] { "det_500m.onnx", "w600k_mbf.onnx" })
			deletefile(Path.Combine(FaceModelsDir, extra), log);
	}

	// ───────── GPU / DML / FFmpeg ─────────

	static void installcuda(IProgress<string> log, IProgress<InstallProgress> progress) {
		Directory.CreateDirectory(OnnxGpuDir);
		reportprog(progress, 0.1, note: "准备 CUDA…");

		// 1) 完整本地库
		foreach (var c in libcandidates("WPF_OCR_CUDA_LIB", "onnxgpu64")) {
			var probe = Path.Combine(c, "onnxruntime_providers_cuda.dll");
			if (!File.Exists(probe)) continue;
			log?.Report("从本地库复制完整 CUDA 包: " + c);
			long total = 0, done = 0;
			var files = Directory.GetFiles(c);
			foreach (var f in files) {
				try { total += new FileInfo(f).Length; } catch { }
			}
			foreach (var f in files) {
				var name = Path.GetFileName(f);
				File.Copy(f, Path.Combine(OnnxGpuDir, name), true);
				try { done += new FileInfo(f).Length; } catch { }
				reportprog(progress, total > 0 ? 0.1 + 0.9 * done / total : 1, done, total, name, "本地复制");
			}
			reportprog(progress, 1, done, total > 0 ? total : done, note: "本地复制完成");
			log?.Report("onnxgpu64 完成（完整包）");
			return;
		}

		// 2) NuGet ORT CUDA EP
		var nuget = nugetroot();
		var ortNative = Path.Combine(nuget, "microsoft.ml.onnxruntime.gpu.windows", OrtGpuVer,
			"runtimes", "win-x64", "native");
		var ep = Path.Combine(ortNative, "onnxruntime_providers_cuda.dll");
		if (!File.Exists(ep)) {
			log?.Report("NuGet 中无 GPU 包，尝试 dotnet restore…");
			trydotnetrestore(log);
		}
		if (!File.Exists(ep))
			throw new InvalidOperationException(
				$"未找到 Microsoft.ML.OnnxRuntime.Gpu.Windows {OrtGpuVer}。请在已还原 NuGet 的开发机上安装，或设置 WPF_OCR_CUDA_LIB。");

		long copyDone = 0, copyTotal = 0;
		foreach (var f in new[] {
			"onnxruntime.dll", "onnxruntime_providers_cuda.dll", "onnxruntime_providers_shared.dll",
		}) {
			var src = Path.Combine(ortNative, f);
			if (File.Exists(src))
				try { copyTotal += new FileInfo(src).Length; } catch { }
		}
		foreach (var f in new[] {
			"onnxruntime.dll", "onnxruntime_providers_cuda.dll", "onnxruntime_providers_shared.dll",
		}) {
			var src = Path.Combine(ortNative, f);
			if (File.Exists(src)) {
				File.Copy(src, Path.Combine(OnnxGpuDir, f), true);
				try { copyDone += new FileInfo(src).Length; } catch { }
				log?.Report("复制 " + f);
				reportprog(progress, 0.5, copyDone, copyTotal > 0 ? copyTotal : ExpectedSize(FeatureKind.CudaGpu), f, "NuGet 复制");
			}
		}

		// 尝试再拷本地 cudart
		foreach (var c in libcandidates("WPF_OCR_CUDA_LIB", "onnxgpu64")) {
			foreach (var pattern in new[] { "cudart64_*.dll", "cublas*.dll", "cufft*.dll", "cudnn*.dll", "nvJitLink*.dll" }) {
				try {
					foreach (var f in Directory.GetFiles(c, pattern)) {
						File.Copy(f, Path.Combine(OnnxGpuDir, Path.GetFileName(f)), true);
						try { copyDone += new FileInfo(f).Length; copyTotal += new FileInfo(f).Length; } catch { }
					}
				}
				catch { }
			}
		}

		var hasCudart = File.Exists(Path.Combine(OnnxGpuDir, "cudart64_13.dll"))
			|| File.Exists(Path.Combine(OnnxGpuDir, "cudart64_12.dll"));
		if (!hasCudart) {
			log?.Report("警告: 仅安装了 ORT CUDA EP，缺少 cudart/cudnn。");
			log?.Report("请设置 WPF_OCR_CUDA_LIB 指向完整 onnxgpu64，或安装 CUDA Toolkit + cuDNN 后重试。");
			var readme = Path.Combine(OnnxGpuDir, "README.txt");
			File.WriteAllText(readme,
				"onnxgpu64\n=========\nContains ONNX Runtime CUDA EP.\n" +
				"Full GPU needs CUDA runtime DLLs (cudart/cublas/cufft/cudnn).\n" +
				"Set WPF_OCR_CUDA_LIB and reinstall CUDA module.\n",
				new UTF8Encoding(false));
		}
		else
			log?.Report("已附带 CUDA 运行库");
		reportprog(progress, 1, copyDone, copyTotal > 0 ? copyTotal : copyDone, note: "完成");
		log?.Report("onnxgpu64 安装结束（GPU 模块需重启程序后生效）");
	}

	static async Task installdml(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		Directory.CreateDirectory(OnnxDmlDir);
		reportprog(progress, 0.1, note: "准备 DirectML…");
		// 本地 NuGet 缓存优先，否则从 CDN 下载 nupkg（精简发布包无本机缓存）
		await extractnupkgfiles(
			"microsoft.ml.onnxruntime.directml", OrtDmlVer,
			new[] {
				("runtimes/win-x64/native/onnxruntime.dll", Path.Combine(OnnxDmlDir, "onnxruntime.dll")),
				("runtimes/win-x64/native/onnxruntime_providers_shared.dll",
					Path.Combine(OnnxDmlDir, "onnxruntime_providers_shared.dll")),
			},
			log, progress, ct, ExpectedSize(FeatureKind.DirectMl) * 8 / 10).ConfigureAwait(false);
		try {
			await extractnupkgfiles(
				"microsoft.ai.directml", DmlVer,
				new[] {
					("bin/x64-win/DirectML.dll", Path.Combine(OnnxDmlDir, "DirectML.dll")),
				},
				log, progress, ct, 18L * 1024 * 1024).ConfigureAwait(false);
		}
		catch (Exception ex) {
			log?.Report("DirectML.dll 下载失败（系统目录可能仍可用）: " + ex.Message);
		}
		if (!File.Exists(Path.Combine(OnnxDmlDir, "onnxruntime.dll")))
			throw new InvalidOperationException("onnxdml64 安装失败");
		reportprog(progress, 1, note: "完成");
		log?.Report("onnxdml64 完成（核显模块需重启程序后生效）");
	}

	/// <summary>
	/// 安装 CPU 用 ORT 到 onnxcpu64。
	/// 优先本机 NuGet / 已有 onnxgpu64；否则下载 Gpu.Windows nupkg（与托管 1.27.1 同源，仅抽主 DLL）。
	/// </summary>
	static async Task installortcpu(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		Directory.CreateDirectory(OnnxCpuDir);
		reportprog(progress, 0.05, note: "准备 ONNX Runtime CPU…");

		// 1) 开发机：本地库 / NuGet 缓存直接拷
		string srcDir = null;
		foreach (var c in libcandidates("WPF_OCR_CUDA_LIB", "onnxgpu64")) {
			if (File.Exists(Path.Combine(c, "onnxruntime.dll"))) { srcDir = c; break; }
		}
		if (srcDir == null) {
			var nuget = nugetroot();
			var ortGpu = Path.Combine(nuget, "microsoft.ml.onnxruntime.gpu.windows", OrtGpuVer,
				"runtimes", "win-x64", "native");
			var ortDml = Path.Combine(nuget, "microsoft.ml.onnxruntime.directml", OrtDmlVer,
				"runtimes", "win-x64", "native");
			if (File.Exists(Path.Combine(ortGpu, "onnxruntime.dll"))) srcDir = ortGpu;
			else if (File.Exists(Path.Combine(ortDml, "onnxruntime.dll"))) srcDir = ortDml;
		}
		if (srcDir != null) {
			long done = 0, total = 0;
			var files = new List<string> { "onnxruntime.dll" };
			if (File.Exists(Path.Combine(srcDir, "onnxruntime_providers_shared.dll")))
				files.Add("onnxruntime_providers_shared.dll");
			foreach (var f in files)
				try { total += new FileInfo(Path.Combine(srcDir, f)).Length; } catch { }
			foreach (var f in files) {
				var src = Path.Combine(srcDir, f);
				if (!File.Exists(src)) continue;
				File.Copy(src, Path.Combine(OnnxCpuDir, f), true);
				try { done += new FileInfo(src).Length; } catch { }
				log?.Report("本地复制 " + f);
				reportprog(progress, 0.2 + 0.7 * (total > 0 ? done / (double)total : 0.5), done, total, f);
			}
		}
		else {
			// 2) 精简发布：从 NuGet CDN 下载 nupkg 解压
			log?.Report("本机无 ORT 缓存，从 NuGet 下载…");
			await extractnupkgfiles(
				"microsoft.ml.onnxruntime.gpu.windows", OrtGpuVer,
				new[] {
					("runtimes/win-x64/native/onnxruntime.dll", Path.Combine(OnnxCpuDir, "onnxruntime.dll")),
					("runtimes/win-x64/native/onnxruntime_providers_shared.dll",
						Path.Combine(OnnxCpuDir, "onnxruntime_providers_shared.dll")),
				},
				log, progress, ct, ExpectedSize(FeatureKind.OrtCpu)).ConfigureAwait(false);
		}

		if (probecpu() != FeatureInstallState.Installed)
			throw new InvalidOperationException("onnxcpu64 安装失败（onnxruntime.dll 无效）");
		reportprog(progress, 1, note: "完成");
		log?.Report("onnxcpu64 完成（CPU 推理可用；若曾加载失败请重启程序）");
	}

	/// <summary>从本机 NuGet 缓存或 CDN nupkg 解出指定条目到目标路径。</summary>
	static async Task extractnupkgfiles(
		string packageId, string version,
		(string ZipPath, string DestPath)[] files,
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct,
		long sizeHint) {
		if (files.All(f => File.Exists(f.DestPath) && new FileInfo(f.DestPath).Length > 1000)) {
			log?.Report("已存在: " + string.Join(", ", files.Select(f => Path.GetFileName(f.DestPath))));
			return;
		}

		// 本地缓存
		try {
			var roots = new List<string>();
			var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			if (!string.IsNullOrWhiteSpace(env)) roots.Add(env);
			roots.Add(Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"));
			var id = packageId.ToLowerInvariant();
			foreach (var root in roots) {
				var pkgDir = Path.Combine(root, id, version);
				if (!Directory.Exists(pkgDir)) continue;
				var allOk = true;
				foreach (var (rel, dest) in files) {
					var src = Path.Combine(pkgDir, rel.Replace('/', Path.DirectorySeparatorChar));
					if (!File.Exists(src)) {
						var name = Path.GetFileName(dest);
						var hits = Directory.GetFiles(pkgDir, name, SearchOption.AllDirectories);
						if (hits.Length == 0) { allOk = false; break; }
						src = hits[0];
					}
					Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? ".");
					File.Copy(src, dest, true);
					log?.Report("本地 NuGet → " + Path.GetFileName(dest));
				}
				if (allOk) {
					reportprog(progress, 0.9, note: "本地 NuGet");
					return;
				}
			}
		}
		catch (Exception ex) {
			log?.Report("本地 NuGet: " + ex.Message);
		}

		Directory.CreateDirectory(CacheDir);
		var nupkg = Path.Combine(CacheDir, $"{packageId.ToLowerInvariant()}.{version}.nupkg");
		var file = $"{packageId.ToLowerInvariant()}.{version}.nupkg";
		var idLow = packageId.ToLowerInvariant();
		var cn = $"https://nuget.cdn.azure.cn/v3-flatcontainer/{idLow}/{version}/{file}";
		var global = $"https://api.nuget.org/v3-flatcontainer/{idLow}/{version}/{file}";
		var urls = PreferCnMirrors() ? new[] { cn, global } : new[] { global, cn };
		log?.Report($"下载 {packageId} {version} …");
		await DownloadUrlAsync(urls, nupkg, log, progress, ct, sizeHint).ConfigureAwait(false);

		reportprog(progress, 0.92, file: Path.GetFileName(nupkg), note: "解压…");
		using (var zs = File.OpenRead(nupkg))
		using (var zip = new ZipArchive(zs, ZipArchiveMode.Read)) {
			foreach (var (zipPath, dest) in files) {
				var entry = zip.GetEntry(zipPath)
					?? zip.Entries.FirstOrDefault(e =>
						e.FullName.Replace('\\', '/').EndsWith(
							Path.GetFileName(dest).Replace('\\', '/'),
							StringComparison.OrdinalIgnoreCase));
				if (entry == null) {
					// providers_shared 可选
					if (dest.IndexOf("providers_shared", StringComparison.OrdinalIgnoreCase) >= 0) {
						log?.Report("跳过（包内无）: " + Path.GetFileName(dest));
						continue;
					}
					throw new InvalidOperationException("nupkg 中未找到 " + Path.GetFileName(dest));
				}
				Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? ".");
				using (var src = entry.Open())
				using (var dst = File.Create(dest))
					src.CopyTo(dst);
				log?.Report($"写出 {Path.GetFileName(dest)} ({FormatBytes(new FileInfo(dest).Length)})");
			}
		}
	}

	static async Task installffmpeg(IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		Directory.CreateDirectory(FfmpegDir);
		if (Directory.GetFiles(FfmpegDir, "avcodec-*.dll").Length > 0) {
			log?.Report("ffmpeg64 已存在");
			reportprog(progress, 1, note: "已存在");
			return;
		}
		foreach (var c in libcandidates("WPF_OCR_FFMPEG_LIB", "ffmpeg64")) {
			if (!Directory.Exists(c)) continue;
			if (Directory.GetFiles(c, "avcodec-*.dll").Length == 0) continue;
			log?.Report("从本地复制 FFmpeg: " + c);
			long done = 0, total = 0;
			var files = Directory.GetFiles(c);
			foreach (var f in files) {
				try { total += new FileInfo(f).Length; } catch { }
			}
			foreach (var f in files) {
				File.Copy(f, Path.Combine(FfmpegDir, Path.GetFileName(f)), true);
				try { done += new FileInfo(f).Length; } catch { }
				reportprog(progress, total > 0 ? done / (double)total : 1, done, total, Path.GetFileName(f), "本地复制");
			}
			reportprog(progress, 1, done, total, note: "本地复制完成");
			log?.Report("ffmpeg64 完成（本地）");
			return;
		}

		Directory.CreateDirectory(CacheDir);
		var zipPath = Path.Combine(CacheDir, "ffmpeg-4.4-win64-gpl-shared.zip");
		var urls = ExpandUrls(FfmpegUrls);
		await downloadfirst(urls, zipPath, log, progress, ct,
			expectedTotal: ExpectedSize(FeatureKind.Ffmpeg), overallWeight: 0.9).ConfigureAwait(false);

		var extract = Path.Combine(CacheDir, "ffmpeg-extract");
		if (Directory.Exists(extract)) {
			try { Directory.Delete(extract, true); } catch { }
		}
		Directory.CreateDirectory(extract);
		log?.Report("解压 FFmpeg zip…");
		var zlen = File.Exists(zipPath) ? new FileInfo(zipPath).Length : 0;
		reportprog(progress, 0.92, zlen, zlen, Path.GetFileName(zipPath), "解压中…");
		ZipFile.ExtractToDirectory(zipPath, extract);
		var av = Directory.GetFiles(extract, "avcodec-*.dll", SearchOption.AllDirectories).FirstOrDefault();
		if (av == null)
			throw new InvalidOperationException("zip 中未找到 avcodec-*.dll");
		var binDir = Path.GetDirectoryName(av);
		foreach (var f in Directory.GetFiles(binDir))
			File.Copy(f, Path.Combine(FfmpegDir, Path.GetFileName(f)), true);
		reportprog(progress, 1, zlen, zlen, Path.GetFileName(av), "完成");
		log?.Report("ffmpeg64 完成 ← " + Path.GetFileName(av));
	}

	// ───────── 下载 / 镜像 ─────────

	/// <summary>根据是否国内环境展开 URL 列表（优先顺序）。</summary>
	public static string[] ExpandUrls(params string[] primaries) {
		var ordered = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		void add(string u) {
			if (string.IsNullOrWhiteSpace(u)) return;
			u = u.Trim();
			if (seen.Add(u)) ordered.Add(u);
		}

		var cn = PreferCnMirrors();
		foreach (var p in primaries) {
			if (string.IsNullOrWhiteSpace(p)) continue;
			var primary = p.Trim();

			// ModelScope 本身已是国内
			if (primary.IndexOf("modelscope.cn", StringComparison.OrdinalIgnoreCase) >= 0) {
				add(primary);
				continue;
			}

			// HuggingFace → hf-mirror
			if (primary.IndexOf("huggingface.co", StringComparison.OrdinalIgnoreCase) >= 0) {
				var mir = primary.Replace("https://huggingface.co/", "https://hf-mirror.com/")
					.Replace("http://huggingface.co/", "https://hf-mirror.com/");
				if (cn) { add(mir); add(primary); }
				else { add(primary); add(mir); }
				continue;
			}

			// GitHub → 代理
			if (primary.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0
				|| primary.IndexOf("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) >= 0) {
				var proxies = new[] {
					"https://ghfast.top/" + primary,
					"https://mirror.ghproxy.com/" + primary,
					"https://ghproxy.net/" + primary,
				};
				if (cn) {
					foreach (var x in proxies) add(x);
					add(primary);
				}
				else {
					add(primary);
					foreach (var x in proxies) add(x);
				}
				continue;
			}

			add(primary);
		}
		return ordered.ToArray();
	}

	/// <summary>
	/// 多文件下载。用 expectedPackage 作总大小分母；已有文件计入已下载。
	/// </summary>
	static async Task downloadmap(
		Dictionary<string, string[]> map, string dst,
		IProgress<string> log, IProgress<InstallProgress> progress,
		long expectedPackage, CancellationToken ct) {
		var keys = map.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
		// 先累计已有体积
		long already = 0;
		foreach (var name in keys) {
			var dest = Path.Combine(dst, name);
			if (File.Exists(dest)) {
				try { already += new FileInfo(dest).Length; } catch { }
			}
		}
		// 估算总大小：至少 expectedPackage，且不小于已有
		var packageTotal = Math.Max(expectedPackage, already);
		long done = already;
		reportprog(progress, packageTotal > 0 ? done / (double)packageTotal : 0, done, packageTotal, note: "准备下载…");

		foreach (var name in keys) {
			ct.ThrowIfCancellationRequested();
			var dest = Path.Combine(dst, name);
			if (File.Exists(dest) && new FileInfo(dest).Length > 32) {
				log?.Report("已有 " + name + " (" + FormatBytes(new FileInfo(dest).Length) + ")");
				reportprog(progress, packageTotal > 0 ? Math.Min(0.99, done / (double)packageTotal) : 0,
					done, packageTotal, name, "跳过");
				continue;
			}

			var baseDone = done;
			await downloadfirst(map[name], dest, log, progress, ct,
				expectedTotal: 0,
				overallWeight: 1,
				// 映射到整包：baseDone + fileRead 作为 BytesDone
				mapPackageDone: baseDone,
				mapPackageTotal: packageTotal).ConfigureAwait(false);

			if (File.Exists(dest)) {
				try {
					var len = new FileInfo(dest).Length;
					// 新下载部分：若 baseDone 未含此文件，加上
					done = baseDone + len;
					if (done > packageTotal)
						packageTotal = done;
				}
				catch { done = baseDone; }
			}
			reportprog(progress, packageTotal > 0 ? Math.Min(0.99, done / (double)packageTotal) : 0,
				done, packageTotal, name);
		}
		reportprog(progress, 1, done, packageTotal > 0 ? packageTotal : done, note: "下载完成");
	}

	/// <param name="expectedTotal">单文件预期大小（0 则用 Content-Length）。</param>
	/// <param name="overallWeight">本下载在 0–1 进度中的权重上限（默认 1）。</param>
	/// <param name="mapPackageDone">多文件包：本文件开始前已完成字节。</param>
	/// <param name="mapPackageTotal">多文件包：整包总字节。</param>
	static async Task downloadfirst(
		string[] urls, string dest,
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct,
		long expectedTotal = 0,
		double overallWeight = 1,
		long mapPackageDone = -1,
		long mapPackageTotal = 0) {
		var fileName = Path.GetFileName(dest);
		if (File.Exists(dest) && new FileInfo(dest).Length > 32) {
			var len = new FileInfo(dest).Length;
			log?.Report("缓存/已有: " + fileName + " (" + FormatBytes(len) + ")");
			if (mapPackageDone >= 0)
				reportprog(progress, mapPackageTotal > 0 ? Math.Min(1, (mapPackageDone + len) / (double)mapPackageTotal) : 1,
					mapPackageDone + len, mapPackageTotal, fileName, "已有");
			else
				reportprog(progress, overallWeight, len, len, fileName, "已有");
			return;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? ".");
		Exception last = null;
		foreach (var url in urls) {
			ct.ThrowIfCancellationRequested();
			try {
				log?.Report("GET " + url);
				await downloadfile(url, dest, progress, ct, fileName, expectedTotal, overallWeight,
					mapPackageDone, mapPackageTotal).ConfigureAwait(false);
				if (File.Exists(dest) && new FileInfo(dest).Length > 32) {
					var len = new FileInfo(dest).Length;
					if (mapPackageDone >= 0)
						reportprog(progress,
							mapPackageTotal > 0 ? Math.Min(1, (mapPackageDone + len) / (double)mapPackageTotal) : 1,
							mapPackageDone + len, mapPackageTotal > 0 ? mapPackageTotal : mapPackageDone + len,
							fileName, "完成");
					else
						reportprog(progress, overallWeight, len, len, fileName, "完成");
					return;
				}
			}
			catch (Exception ex) {
				last = ex;
				log?.Report("失败: " + ex.Message);
				try { if (File.Exists(dest)) File.Delete(dest); } catch { }
				try {
					var partial = dest + ".partial";
					if (File.Exists(partial)) File.Delete(partial);
				}
				catch { }
			}
		}
		throw new InvalidOperationException(
			"下载失败: " + fileName + (last != null ? " — " + last.Message : ""));
	}

	static async Task downloadfile(
		string url, string dest,
		IProgress<InstallProgress> progress, CancellationToken ct,
		string fileName, long expectedTotal, double overallWeight,
		long mapPackageDone, long mapPackageTotal) {
		var partial = dest + ".partial";
		var lastReport = 0L;
		using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false)) {
			resp.EnsureSuccessStatusCode();
			var contentLen = resp.Content.Headers.ContentLength ?? -1;
			var fileTotal = contentLen > 0 ? contentLen : expectedTotal;
			using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
			using (var fs = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true)) {
				var buf = new byte[81920];
				long read = 0;
				int n;
				while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0) {
					await fs.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
					read += n;
					// 节流：每 256KB 或末尾再刷 UI
					if (read - lastReport < 256 * 1024 && (fileTotal <= 0 || read < fileTotal))
						continue;
					lastReport = read;

					if (mapPackageDone >= 0) {
						// 多文件包进度
						var pkgDone = mapPackageDone + read;
						var pkgTotal = mapPackageTotal;
						if (fileTotal > 0 && mapPackageDone + fileTotal > pkgTotal)
							pkgTotal = mapPackageDone + fileTotal;
						if (pkgTotal < pkgDone) pkgTotal = pkgDone;
						var overall = pkgTotal > 0 ? Math.Min(0.99, pkgDone / (double)pkgTotal) : 0;
						// BytesDone/Total 展示「整包」已下/总，并带当前文件名
						reportprog(progress, overall, pkgDone, pkgTotal, fileName);
					}
					else {
						var overall = fileTotal > 0
							? Math.Min(0.99, overallWeight * read / (double)fileTotal)
							: 0;
						reportprog(progress, overall, read, fileTotal > 0 ? fileTotal : 0, fileName);
					}
				}
			}
		}
		if (File.Exists(dest)) File.Delete(dest);
		File.Move(partial, dest);
	}

	// ───────── 公开下载 / 解压（发音人等复用） ─────────

	/// <summary>下载 URL 列表中的第一个成功项到 dest（支持镜像展开后的多地址）。</summary>
	public static Task DownloadUrlAsync(
		string[] urls, string dest,
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct,
		long expectedTotal = 0) =>
		downloadfirst(urls, dest, log, progress, ct, expectedTotal: expectedTotal);

	/// <summary>解压 zip / tar.bz2 到目标目录。</summary>
	public static void ExtractArchive(string archive, string destDir, IProgress<string> log) =>
		extractarchive(archive, destDir, log);

	// ───────── 解压 ─────────

	static void extractarchive(string archive, string destDir, IProgress<string> log) {
		Directory.CreateDirectory(destDir);
		var ext = Path.GetExtension(archive).ToLowerInvariant();
		if (ext == ".zip") {
			ZipFile.ExtractToDirectory(archive, destDir);
			return;
		}
		// tar.bz2 / tar.gz：Windows 10+ tar
		var tar = findtar();
		if (tar == null)
			throw new InvalidOperationException("系统无 tar，无法解压 " + Path.GetFileName(archive));
		var psi = new ProcessStartInfo {
			FileName = tar,
			Arguments = $"-xf \"{archive}\" -C \"{destDir}\"",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		using (var p = Process.Start(psi)) {
			if (p == null) throw new InvalidOperationException("无法启动 tar");
			var err = p.StandardError.ReadToEnd();
			var stdout = p.StandardOutput.ReadToEnd();
			if (!p.WaitForExit(600_000)) {
				try { p.Kill(); } catch { }
				throw new InvalidOperationException("tar 解压超时");
			}
			if (p.ExitCode != 0)
				throw new InvalidOperationException("tar 失败 exit=" + p.ExitCode + " " + err);
			if (!string.IsNullOrWhiteSpace(stdout))
				log?.Report(stdout.Trim());
		}
	}

	static string findtar() {
		var cmd = Path.Combine(Environment.SystemDirectory, "tar.exe");
		if (File.Exists(cmd)) return cmd;
		try {
			var psi = new ProcessStartInfo {
				FileName = "where",
				Arguments = "tar",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				CreateNoWindow = true,
			};
			using var p = Process.Start(psi);
			if (p == null) return null;
			var o = p.StandardOutput.ReadToEnd();
			p.WaitForExit(3000);
			var line = o.Replace("\r", "\n").Split('\n')
				.Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
			return string.IsNullOrEmpty(line) ? null : line;
		}
		catch { return null; }
	}

	// ───────── 本地种子 / NuGet ─────────

	static bool trylocalcopy(string packId, string dst, IProgress<string> log) {
		foreach (var root in localseeds()) {
			var src = Path.Combine(root, packId);
			if (!Directory.Exists(src)) continue;
			var onnxs = Directory.GetFiles(src, "*.onnx");
			if (onnxs.Length == 0) continue;
			log?.Report("从本地复制 " + packId + " ← " + src);
			copytree(src, dst);
			return true;
		}
		return false;
	}

	static IEnumerable<string> localseeds() {
		yield return OcrModelsDir;
		yield return Path.Combine(BaseDir, "models");
		var up = Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", ".."));
		yield return Path.Combine(up, "tmp", "build_snap", "ocrmodels");
		yield return Path.Combine(up, "tmp", "build_snap", "models");
		yield return Path.Combine(up, "ocrmodels");
	}

	static void copytree(string src, string dst) {
		Directory.CreateDirectory(dst);
		foreach (var f in Directory.GetFiles(src))
			File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
		foreach (var d in Directory.GetDirectories(src)) {
			var name = Path.GetFileName(d);
			copytree(d, Path.Combine(dst, name));
		}
	}

	static IEnumerable<string> libcandidates(string envName, string subPath) {
		var env = Environment.GetEnvironmentVariable(envName);
		if (!string.IsNullOrWhiteSpace(env))
			yield return env;
		yield return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Library", "dll", subPath);
		// 常见中央库（存在才有用）
		var lib = @"D:\VS_Projects\Library\dll\" + subPath;
		if (Directory.Exists(lib))
			yield return lib;
	}

	static string nugetroot() {
		var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
		if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
			return env;
		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".nuget", "packages");
	}

	static void trydotnetrestore(IProgress<string> log) {
		try {
			// 定位 csproj：开发布局
			var candidates = new[] {
				Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "ScreenKit.csproj")),
				Path.GetFullPath(Path.Combine(BaseDir, "ScreenKit.csproj")),
				Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "WpfOCR.csproj")),
				Path.GetFullPath(Path.Combine(BaseDir, "WpfOCR.csproj")),
			};
			string csproj = null;
			foreach (var c in candidates)
				if (File.Exists(c)) { csproj = c; break; }
			if (csproj == null) {
				log?.Report("未找到 csproj，跳过 restore");
				return;
			}
			var psi = new ProcessStartInfo {
				FileName = "dotnet",
				Arguments = $"restore \"{csproj}\" --nologo",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using var p = Process.Start(psi);
			if (p == null) return;
			p.StandardOutput.ReadToEnd();
			p.StandardError.ReadToEnd();
			p.WaitForExit(120_000);
			log?.Report("dotnet restore exit=" + p.ExitCode);
		}
		catch (Exception ex) {
			log?.Report("restore 失败: " + ex.Message);
		}
	}
}
