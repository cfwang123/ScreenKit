using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace WpfOCR;

/// <summary>
/// 重型原生库按需安装：OpenCvSharpExtern / libSkiaSharp / pdfium / sherpa-onnx-c-api（精简包）。本机 net48 编译保留 Sherpa。
/// 录屏仅用 ffmpeg64，不使用 opencv_videoio_ffmpeg。
/// 从 NuGet 官方/国内 CDN 下载对应版本 nupkg 后解压到程序目录。
/// </summary>
static class NativeRuntime {
	// 与 csproj PackageReference 对齐
	const string OpencvPkg = "opencvsharp4.runtime.win";
	const string OpencvVer = "4.11.0.20250507";
	const string SkiaPkg = "skiasharp.nativeassets.win32";
	const string SkiaVer = "3.119.0";
	const string PdfiumPkg = "bblanchon.pdfium.win32";
	const string PdfiumVer = "139.0.7215";
	const string SherpaPkg = "org.k2fsa.sherpa.onnx.runtime.win-x64";
	const string SherpaVer = "1.13.3";

	const string OpenCvExtern = "OpenCvSharpExtern.dll";
	const string LibSkia = "libSkiaSharp.dll";
	const string Pdfium = "pdfium.dll";
	const string SherpaCApi = "sherpa-onnx-c-api.dll";

	static readonly object gate = new();
	static readonly HttpClient Http = createhttp();
	static bool opencvOk;
	static bool skiaOk;
	static bool sherpaOk;

	static HttpClient createhttp() {
		try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
		var c = new HttpClient();
		c.Timeout = TimeSpan.FromMinutes(30);
		c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "WpfOCR-NativeRuntime/1.0");
		return c;
	}

	public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

	// ───────── 探测 ─────────

	public static bool HasOpenCv() =>
		File.Exists(Path.Combine(BaseDir, OpenCvExtern))
		|| File.Exists(Path.Combine(BaseDir, "dll", "x64", OpenCvExtern));

	public static bool HasSkia() =>
		File.Exists(Path.Combine(BaseDir, LibSkia))
		|| File.Exists(Path.Combine(BaseDir, "x64", LibSkia));

	public static bool HasPdfium() =>
		File.Exists(Path.Combine(BaseDir, Pdfium))
		|| File.Exists(Path.Combine(BaseDir, "x64", Pdfium));

	/// <summary>PDF 需要 Skia + PDFium 齐全。</summary>
	public static bool HasSkiaPdf() => HasSkia() && HasPdfium();

	public static bool HasSherpa() =>
		File.Exists(Path.Combine(BaseDir, SherpaCApi))
		|| File.Exists(Path.Combine(BaseDir, "runtimes", "win-x64", "native", SherpaCApi));

	public static long OpenCvSizeHint => 61L * 1024 * 1024;
	public static long SkiaSizeHint => 11L * 1024 * 1024;
	public static long PdfiumSizeHint => 6L * 1024 * 1024;
	public static long SherpaSizeHint => 5L * 1024 * 1024;
	public static long SkiaPdfSizeHint => SkiaSizeHint + PdfiumSizeHint;

	// ───────── 确保（缺失则弹窗提示安装，不静默下载） ─────────

	/// <summary>OCR / 长截图等需要 OpenCvSharpExtern。缺失时弹窗提示安装。</summary>
	public static void EnsureOpenCv(IProgress<string> log = null, IProgress<InstallProgress> progress = null) {
		if (opencvOk && HasOpenCv()) return;
		if (HasOpenCv()) {
			lock (gate) opencvOk = true;
			return;
		}
		log?.Report("缺少 OpenCv 运行库");
		if (!FeaturePrompt.EnsureOpenCv())
			throw new InvalidOperationException(
				"未安装 OpenCV 运行库。请打开「安装功能」安装「OpenCV 运行库」后再试。");
		lock (gate) opencvOk = HasOpenCv();
	}

	/// <summary>PDF 光栅需要 Skia + PDFium。缺失时弹窗提示安装。</summary>
	public static void EnsureSkiaPdf(IProgress<string> log = null, IProgress<InstallProgress> progress = null) {
		if (skiaOk && HasSkiaPdf()) return;
		if (HasSkiaPdf()) {
			lock (gate) skiaOk = true;
			return;
		}
		log?.Report("缺少 PDF 渲染库（Skia / PDFium）");
		if (!FeaturePrompt.EnsurePdf())
			throw new InvalidOperationException(
				"未安装 PDF 渲染库。请打开「安装功能」安装 Skia 与 PDFium 后再试。");
		lock (gate) skiaOk = HasSkiaPdf();
	}

	/// <summary>ASR / TTS Sherpa 需要 sherpa-onnx-c-api.dll。缺失时弹窗提示安装。</summary>
	public static void EnsureSherpa(IProgress<string> log = null, IProgress<InstallProgress> progress = null) {
		if (sherpaOk && HasSherpa()) return;
		if (HasSherpa()) {
			lock (gate) sherpaOk = true;
			return;
		}
		log?.Report("缺少 sherpa-onnx-c-api.dll");
		if (!FeaturePrompt.EnsureSherpa())
			throw new InvalidOperationException(
				"未安装 Sherpa 运行库（sherpa-onnx-c-api.dll）。请打开「安装功能」安装后再试。");
		lock (gate) sherpaOk = HasSherpa();
	}

	// ───────── 安装（供安装向导 / 按需） ─────────

	public static async Task InstallOpenCv(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			OpencvPkg, OpencvVer,
			new[] {
				("runtimes/win-x64/native/" + OpenCvExtern, OpenCvExtern),
			},
			log, progress, ct, OpenCvSizeHint).ConfigureAwait(false);
		// 兼容 OpenCvSharp 部分加载路径
		copybeside(OpenCvExtern, Path.Combine("dll", "x64"));
		opencvOk = HasOpenCv();
	}

	public static async Task InstallSkia(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			SkiaPkg, SkiaVer,
			new[] {
				("runtimes/win-x64/native/" + LibSkia, LibSkia),
			},
			log, progress, ct, SkiaSizeHint).ConfigureAwait(false);
		copybeside(LibSkia, "x64");
		skiaOk = HasSkiaPdf();
	}

	public static async Task InstallPdfium(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			PdfiumPkg, PdfiumVer,
			new[] {
				("runtimes/win-x64/native/" + Pdfium, Pdfium),
			},
			log, progress, ct, PdfiumSizeHint).ConfigureAwait(false);
		copybeside(Pdfium, "x64");
		skiaOk = HasSkiaPdf();
	}

	public static async Task InstallSherpa(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			SherpaPkg, SherpaVer,
			new[] {
				("runtimes/win-x64/native/" + SherpaCApi, SherpaCApi),
			},
			log, progress, ct, SherpaSizeHint).ConfigureAwait(false);
		// 部分加载路径会查 runtimes/win-x64/native
		copybeside(SherpaCApi, Path.Combine("runtimes", "win-x64", "native"));
		sherpaOk = HasSherpa();
	}

	/// <summary>兼容旧调用：同时装 Skia + PDFium。</summary>
	public static async Task InstallSkiaPdf(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await InstallSkia(log, progress, ct).ConfigureAwait(false);
		await InstallPdfium(log, progress, ct).ConfigureAwait(false);
	}

	// ───────── 卸载 ─────────

	public static void UninstallOpenCv(IProgress<string> log = null) {
		deletefiles(log, OpenCvExtern,
			Path.Combine("dll", "x64", OpenCvExtern),
			Path.Combine("runtimes", "win-x64", "native", OpenCvExtern));
		opencvOk = false;
	}

	public static void UninstallSkia(IProgress<string> log = null) {
		deletefiles(log, LibSkia,
			Path.Combine("x64", LibSkia),
			Path.Combine("runtimes", "win-x64", "native", LibSkia));
		skiaOk = false;
	}

	public static void UninstallPdfium(IProgress<string> log = null) {
		deletefiles(log, Pdfium,
			Path.Combine("x64", Pdfium),
			Path.Combine("runtimes", "win-x64", "native", Pdfium));
		skiaOk = false;
	}

	public static void UninstallSherpa(IProgress<string> log = null) {
		deletefiles(log, SherpaCApi,
			Path.Combine("runtimes", "win-x64", "native", SherpaCApi));
		sherpaOk = false;
	}

	static void deletefiles(IProgress<string> log, params string[] relativePaths) {
		foreach (var rel in relativePaths) {
			try {
				var p = Path.IsPathRooted(rel) ? rel : Path.Combine(BaseDir, rel);
				if (!File.Exists(p)) continue;
				File.Delete(p);
				log?.Report("已删除 " + Path.GetFileName(p));
			}
			catch (Exception ex) {
				log?.Report("删除失败 " + rel + ": " + ex.Message);
			}
		}
	}

	static void copybeside(string fileName, string subDir) {
		var src = Path.Combine(BaseDir, fileName);
		if (!File.Exists(src)) return;
		try {
			var destDir = Path.Combine(BaseDir, subDir);
			Directory.CreateDirectory(destDir);
			var dest = Path.Combine(destDir, fileName);
			if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(src).Length)
				File.Copy(src, dest, true);
		}
		catch { }
	}

	// ───────── NuGet nupkg 下载解压 ─────────

	static async Task extractfromnupkg(
		string packageId, string version,
		(string ZipPath, string DestName)[] files,
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct,
		long sizeHint) {
		// 已齐则跳过
		if (files.All(f => File.Exists(Path.Combine(BaseDir, f.DestName)))) {
			log?.Report("已存在: " + string.Join(", ", files.Select(f => f.DestName)));
			progress?.Report(new InstallProgress { Overall = 1, Note = "已存在" });
			return;
		}

		// 1) 优先本机 NuGet 缓存（开发机）
		var local = trylocalnuget(packageId, version, files, log);
		if (local) {
			progress?.Report(new InstallProgress { Overall = 1, Note = "本地 NuGet 缓存" });
			return;
		}

		// 2) 下载 nupkg
		Directory.CreateDirectory(FeatureInstaller.CacheDir);
		var nupkg = Path.Combine(FeatureInstaller.CacheDir, $"{packageId}.{version}.nupkg");
		var urls = nugeturls(packageId, version);
		log?.Report($"下载 {packageId} {version} …");
		await FeatureInstaller.DownloadUrlAsync(urls, nupkg, log, progress, ct, sizeHint)
			.ConfigureAwait(false);

		// 3) 从 zip 抽文件
		progress?.Report(new InstallProgress { Overall = 0.95, Note = "解压原生库…", FileName = Path.GetFileName(nupkg) });
		using (var zs = File.OpenRead(nupkg))
		using (var zip = new ZipArchive(zs, ZipArchiveMode.Read)) {
			foreach (var (zipPath, destName) in files) {
				var entry = zip.GetEntry(zipPath)
					?? zip.Entries.FirstOrDefault(e =>
						e.FullName.Replace('\\', '/').EndsWith(destName, StringComparison.OrdinalIgnoreCase));
				if (entry == null)
					throw new InvalidOperationException($"nupkg 中未找到 {destName}");
				var dest = Path.Combine(BaseDir, destName);
				using (var src = entry.Open())
				using (var dst = File.Create(dest))
					src.CopyTo(dst);
				log?.Report($"写出 {destName} ({FeatureInstaller.FormatBytes(new FileInfo(dest).Length)})");
			}
		}
		progress?.Report(new InstallProgress { Overall = 1, Note = "完成" });
	}

	static bool trylocalnuget(string packageId, string version,
		(string ZipPath, string DestName)[] files, IProgress<string> log) {
		try {
			var roots = new List<string>();
			var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			if (!string.IsNullOrWhiteSpace(env)) roots.Add(env);
			roots.Add(Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				".nuget", "packages"));
			var id = packageId.ToLowerInvariant();
			foreach (var root in roots) {
				var pkgDir = Path.Combine(root, id, version);
				if (!Directory.Exists(pkgDir)) continue;
				var ok = true;
				foreach (var (rel, destName) in files) {
					var src = Path.Combine(pkgDir, rel.Replace('/', Path.DirectorySeparatorChar));
					if (!File.Exists(src)) {
						// 宽松：按文件名搜
						var hits = Directory.GetFiles(pkgDir, destName, SearchOption.AllDirectories);
						if (hits.Length == 0) { ok = false; break; }
						src = hits[0];
					}
					var dest = Path.Combine(BaseDir, destName);
					File.Copy(src, dest, true);
					log?.Report($"本地 NuGet → {destName}");
				}
				if (ok) return true;
			}
		}
		catch (Exception ex) {
			log?.Report("本地 NuGet 不可用: " + ex.Message);
		}
		return false;
	}

	static string[] nugeturls(string packageId, string version) {
		var id = packageId.ToLowerInvariant();
		var file = $"{id}.{version}.nupkg";
		// 国内 Azure CDN 优先
		var cn = $"https://nuget.cdn.azure.cn/v3-flatcontainer/{id}/{version}/{file}";
		var global = $"https://api.nuget.org/v3-flatcontainer/{id}/{version}/{file}";
		return FeatureInstaller.PreferCnMirrors()
			? new[] { cn, global }
			: new[] { global, cn };
	}

	/// <summary>诊断文本。</summary>
	public static string StatusReport() {
		var sb = new StringBuilder();
		sb.AppendLine("=== 原生运行库（按需） ===");
		sb.AppendLine($"OpenCvSharpExtern: {(HasOpenCv() ? "OK" : "缺失")}  ({OpenCvExtern})");
		sb.AppendLine($"libSkiaSharp: {(HasSkia() ? "OK" : "缺失")}  ({LibSkia})");
		sb.AppendLine($"pdfium: {(HasPdfium() ? "OK" : "缺失")}  ({Pdfium})");
		sb.AppendLine($"sherpa-onnx-c-api: {(HasSherpa() ? "OK" : "缺失")}  ({SherpaCApi})");
		sb.AppendLine("录屏: 仅 ffmpeg64（不使用 opencv_videoio_ffmpeg）");
		return sb.ToString();
	}
}
