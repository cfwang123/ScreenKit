using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 重型原生库按需安装：OpenCvSharpExtern / libSkiaSharp / pdfium / sherpa-onnx-c-api（精简包）。本机 net48 编译保留 Sherpa。
/// 录屏仅用 ffmpeg64。编译时删掉 OpenCV 的 opencv_videoio_ffmpeg，程序不读视频。
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
	const string ZxingDll = "ZXing.dll";
	const string SharpCompressDll = "SharpCompress.dll";
	const string SevenZipDll = "7za.dll";
	const string SqliteDll = "e_sqlite3.dll";
	const string ZxingPkg = "ZXingCpp";
	const string ZxingVer = "0.5.3";
	const string CompressPkg = "SharpCompress";
	const string CompressVer = "0.50.4";
	const string SevenLibPkg = "7z.Libs";
	const string SevenLibVer = "26.3.0";
	const string SqlitePkg = "SQLitePCLRaw.lib.e_sqlite3";
	const string SqliteVer = "2.1.10";
	const string SqliteManagedPkg = "microsoft.data.sqlite.core";
	const string SqliteManagedVer = "8.0.11";
	const string SqliteCorePkg = "sqlitepclraw.core";
	const string SqliteBundlePkg = "sqlitepclraw.bundle_e_sqlite3";
	const string SqliteProviderPkg = "sqlitepclraw.provider.dynamic_cdecl";
	const string SqlitePclVer = "2.1.10";
	const string MysqlPkg = "mysqlconnector";
	const string MysqlVer = "2.5.0";
	const string OraclePkg = "oracle.manageddataaccess";
	const string OracleVer = "21.15.0";
	const string Asn1Pkg = "system.formats.asn1";
	const string Asn1Ver = "8.0.0";
	const string BogusDll = "Bogus.dll";
	const string BogusPkg = "Bogus";
	const string BogusVer = "35.6.5";
	const string NPinyinDll = "NPinyin.dll";
	const string NPinyinPkg = "NPinyin";
	const string NPinyinVer = "0.2.6321.26573";
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
		var c = new HttpClient(HttpProxy.CreateHandler());
		c.Timeout = TimeSpan.FromMinutes(30);
		c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ScreenKit-NativeRuntime/1.0");
		return c;
	}

	public static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

	// ───────── 探测 ─────────

	public static bool HasOpenCv() =>
		File.Exists(Path.Combine(BaseDir, OpenCvExtern))
		|| File.Exists(Path.Combine(BaseDir, "dll", "x64", OpenCvExtern));

	public static bool HasZxing() => File.Exists(Path.Combine(BaseDir, ZxingDll));
	public static bool HasSharpCompress() => File.Exists(Path.Combine(BaseDir, SharpCompressDll));
	public static bool Has7za() => File.Exists(Path.Combine(BaseDir, SevenZipDll));
	public static bool HasSqlite() => File.Exists(Path.Combine(BaseDir, SqliteDll));
	public static bool HasBogus() => File.Exists(Path.Combine(BaseDir, BogusDll));
	public static bool HasNPinyin() => File.Exists(Path.Combine(BaseDir, NPinyinDll));

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
	public static long ZxingSizeHint => 3149312L;
	public static long SharpCompressSizeHint => 2477056L;
	public static long SevenZipSizeHint => 416256L;
	public static long SqliteSizeHint => 1759232L;
	public static long BogusSizeHint => 2429952L;
	public static long NPinyinSizeHint => 76288L;
	public static long SkiaSizeHint => 11L * 1024 * 1024;
	public static long PdfiumSizeHint => 6L * 1024 * 1024;
	public static long SherpaSizeHint => 5L * 1024 * 1024;
	public static long OracleSizeHint => 4750952L;
	public static long MysqlSizeHint => 855400L;
	/// <summary>SQLite 驱动整套（托管 + 原生）。</summary>
	public static long SqliteDriverSizeHint => SqliteSizeHint + 172072 + 51200 + 11776 + 66048;

	// ───────── 确保（缺失则弹窗提示安装，不静默下载） ─────────

	/// <summary>ONNX 识别、人脸、二维码需要 OpenCvSharpExtern。缺失时弹窗提示安装。长截图和 Windows OCR 不用。图片转换只在已安装时多读格式，不弹窗。</summary>
	public static void EnsureOpenCv(IProgress<string> log = null, IProgress<InstallProgress> progress = null) {
		if (opencvOk && HasOpenCv()) return;
		if (HasOpenCv()) {
			lock (gate) opencvOk = true;
			return;
		}
		log?.Report("缺少 OpenCv 运行库");
		if (!FeaturePrompt.EnsureOpenCv())
			throw new MissingFeatureException(
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
			throw new MissingFeatureException(
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
			throw new MissingFeatureException(
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

	public static async Task InstallZxing(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			ZxingPkg, ZxingVer,
			new[] { ("runtimes/win-x64/native/" + ZxingDll, ZxingDll) },
			log, progress, ct, ZxingSizeHint).ConfigureAwait(false);
		copybeside(ZxingDll, Path.Combine("runtimes", "win-x64", "native"));
	}

	public static async Task InstallSharpCompress(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			CompressPkg, CompressVer,
			new[] { ("lib/net48/" + SharpCompressDll, SharpCompressDll) },
			log, progress, ct, SharpCompressSizeHint).ConfigureAwait(false);
	}

	public static async Task Install7za(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			SevenLibPkg, SevenLibVer,
			new[] {
				("bin/x64/" + SevenZipDll, SevenZipDll),
				("bin/License.7za-7zxa.txt", "License.7za.txt"),
			},
			log, progress, ct, SevenZipSizeHint).ConfigureAwait(false);
	}

	/// <summary>SQLite 驱动：原生 e_sqlite3 + Microsoft.Data.Sqlite 与 SQLitePCLRaw 托管库。</summary>
	public static async Task InstallSqlite(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			SqlitePkg, SqliteVer,
			new[] { ("runtimes/win-x64/native/" + SqliteDll, SqliteDll) },
			log, progress, ct, SqliteSizeHint).ConfigureAwait(false);
		await FeatureInstaller.InstallNuGetFiles(SqliteManagedPkg, SqliteManagedVer,
			new[] { ("lib/netstandard2.0/" + DbDriver.SqliteManaged, Path.Combine(BaseDir, DbDriver.SqliteManaged)) },
			log, progress, ct, 180 * 1024).ConfigureAwait(false);
		await FeatureInstaller.InstallNuGetFiles(SqliteCorePkg, SqlitePclVer,
			new[] { ("lib/netstandard2.0/SQLitePCLRaw.core.dll", Path.Combine(BaseDir, "SQLitePCLRaw.core.dll")) },
			log, progress, ct, 60 * 1024).ConfigureAwait(false);
		await FeatureInstaller.InstallNuGetFiles(SqliteBundlePkg, SqlitePclVer,
			new[] { ("lib/net461/SQLitePCLRaw.batteries_v2.dll", Path.Combine(BaseDir, "SQLitePCLRaw.batteries_v2.dll")) },
			log, progress, ct, 20 * 1024).ConfigureAwait(false);
		await FeatureInstaller.InstallNuGetFiles(SqliteProviderPkg, SqlitePclVer,
			new[] {
				("lib/netstandard2.0/SQLitePCLRaw.provider.dynamic_cdecl.dll",
					Path.Combine(BaseDir, "SQLitePCLRaw.provider.dynamic_cdecl.dll")),
			},
			log, progress, ct, 80 * 1024).ConfigureAwait(false);
	}

	/// <summary>Oracle 驱动：Oracle.ManagedDataAccess（21.x 的 net462 版）与它依赖的 System.Formats.Asn1。</summary>
	/// <summary>MySQL 驱动：MySqlConnector 纯托管库（依赖的 Microsoft.Extensions.* 随本应用已带）。</summary>
	public static async Task InstallMysql(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await FeatureInstaller.InstallNuGetFiles(MysqlPkg, MysqlVer,
			new[] { ("lib/net48/" + DbDriver.MysqlManaged, Path.Combine(BaseDir, DbDriver.MysqlManaged)) },
			log, progress, ct, MysqlSizeHint).ConfigureAwait(false);
	}

	public static async Task InstallOracle(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await FeatureInstaller.InstallNuGetFiles(OraclePkg, OracleVer,
			new[] { ("lib/net462/" + DbDriver.OracleManaged, Path.Combine(BaseDir, DbDriver.OracleManaged)) },
			log, progress, ct, OracleSizeHint).ConfigureAwait(false);
		await FeatureInstaller.InstallNuGetFiles(Asn1Pkg, Asn1Ver,
			new[] { ("lib/net462/" + DbDriver.Asn1, Path.Combine(BaseDir, DbDriver.Asn1)) },
			log, progress, ct, 110 * 1024).ConfigureAwait(false);
	}

	public static async Task InstallBogus(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			BogusPkg, BogusVer,
			new[] { ("lib/netstandard2.0/" + BogusDll, BogusDll) },
			log, progress, ct, BogusSizeHint).ConfigureAwait(false);
	}

	public static async Task InstallNPinyin(
		IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		await extractfromnupkg(
			NPinyinPkg, NPinyinVer,
			new[] { ("lib/net20/" + NPinyinDll, NPinyinDll) },
			log, progress, ct, NPinyinSizeHint).ConfigureAwait(false);
	}

	/// <summary>条码 / 二维码需要 ZXing.dll。缺失时弹窗。</summary>
	public static void EnsureZxing() {
		if (HasZxing()) return;
		if (!FeaturePrompt.EnsureZxing())
			throw new MissingFeatureException(
				"未安装条码库（ZXing.dll）。请打开「安装功能」安装「条码 · ZXing」后再试。");
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

	// ───────── 卸载 ─────────

	public static void UninstallOpenCv(IProgress<string> log = null) {
		deletefiles(log, OpenCvExtern,
			Path.Combine("dll", "x64", OpenCvExtern),
			Path.Combine("runtimes", "win-x64", "native", OpenCvExtern));
		opencvOk = false;
	}

	public static void UninstallZxing(IProgress<string> log = null) {
		deletefiles(log, ZxingDll, Path.Combine("runtimes", "win-x64", "native", ZxingDll));
	}

	public static void UninstallSharpCompress(IProgress<string> log = null) =>
		deletefiles(log, SharpCompressDll);

	public static void Uninstall7za(IProgress<string> log = null) =>
		deletefiles(log, SevenZipDll, "SharpSevenZip.dll");

	public static void UninstallSqlite(IProgress<string> log = null) =>
		DbDriver.Uninstall(DbKind.Sqlite, log);

	public static void UninstallBogus(IProgress<string> log = null) =>
		deletefiles(log, BogusDll);

	public static void UninstallNPinyin(IProgress<string> log = null) =>
		deletefiles(log, NPinyinDll);

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
		// 未用代理时国内 CDN 在前；用了代理时 nuget.org 在前
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
		sb.AppendLine($"ZXing: {(HasZxing() ? "OK" : "缺失")}  ({ZxingDll})");
		sb.AppendLine($"SharpCompress: {(HasSharpCompress() ? "OK" : "缺失")}  ({SharpCompressDll})");
		sb.AppendLine($"7za: {(Has7za() ? "OK" : "缺失")}  ({SevenZipDll})");
		sb.AppendLine($"e_sqlite3: {(HasSqlite() ? "OK" : "缺失")}  ({SqliteDll})");
		sb.AppendLine($"Bogus: {(HasBogus() ? "OK" : "缺失")}  ({BogusDll})");
		sb.AppendLine($"NPinyin: {(HasNPinyin() ? "OK" : "缺失")}  ({NPinyinDll})");
		sb.AppendLine($"libSkiaSharp: {(HasSkia() ? "OK" : "缺失")}  ({LibSkia})");
		sb.AppendLine($"pdfium: {(HasPdfium() ? "OK" : "缺失")}  ({Pdfium})");
		sb.AppendLine($"sherpa-onnx-c-api: {(HasSherpa() ? "OK" : "缺失")}  ({SherpaCApi})");
		sb.AppendLine("录屏: 仅 ffmpeg64");
		return sb.ToString();
	}
}
