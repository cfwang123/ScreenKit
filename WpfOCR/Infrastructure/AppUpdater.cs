using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WpfOCR;

/// <summary>GitHub Releases 检查到的最新版本信息。</summary>
sealed class UpdateInfo {
	public string Version { get; set; }
	public string TagName { get; set; }
	public string DownloadUrl { get; set; }
	public string AssetName { get; set; }
	public long SizeBytes { get; set; }
	public string HtmlUrl { get; set; }
	public string Body { get; set; }
	public bool HasUpdate { get; set; }
	public string CurrentVersion { get; set; }
}

/// <summary>
/// 自更新：查询 GitHub Releases → 下载到 tmp/ → 复制主程序到 tmp/ →
/// 命令行模式解压覆盖安装目录 → 可选重启。
/// </summary>
static class AppUpdater {
	const string REPO_API = "https://api.github.com/repos/cfwang123/WpfOCR/releases/latest";
	const string REPO_PAGE = "https://github.com/cfwang123/WpfOCR/releases";
	const string UPDATER_EXE = "WpfOCR_updater.exe";

	static readonly HttpClient Http = createhttp();

	static HttpClient createhttp() {
		try {
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		catch { }
		var c = new HttpClient();
		c.Timeout = TimeSpan.FromMinutes(30);
		c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "WpfOCR-Updater/1.0");
		c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
		return c;
	}

	/// <summary>当前程序版本（InformationalVersion，去 +git 后缀）。</summary>
	public static string CurrentVersion() {
		try {
			var asm = Assembly.GetExecutingAssembly();
			var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			if (!string.IsNullOrWhiteSpace(info)) {
				var s = info.Trim();
				var plus = s.IndexOf('+');
				if (plus > 0) s = s[..plus];
				return s;
			}
			var v = asm.GetName().Version;
			if (v != null) return $"{v.Major}.{v.Minor}.{v.Build}";
		}
		catch { }
		return "0.0.0";
	}

	/// <summary>CLI：是否为应用更新参数。</summary>
	public static bool IsApplyUpdateArgs(string[] args) {
		if (args == null || args.Length == 0) return false;
		foreach (var a in args) {
			if (a is "--apply-update" or "--self-update")
				return true;
		}
		return false;
	}

	/// <summary>CLI 入口：解压覆盖。返回进程退出码。</summary>
	public static int RunApplyUpdate(string[] args) {
		string archive = null;
		string target = null;
		int waitPid = 0;
		bool restart = false;
		try {
			for (int i = 0; i < args.Length; i++) {
				var a = args[i];
				string Next() {
					if (i + 1 >= args.Length) throw new ArgumentException($"参数 {a} 缺少值");
					return args[++i];
				}
				switch (a) {
					case "--apply-update":
					case "--self-update":
						archive = Next();
						break;
					case "--target":
						target = Next();
						break;
					case "--wait-pid":
						waitPid = int.Parse(Next());
						break;
					case "--restart":
						restart = true;
						break;
				}
			}
			if (string.IsNullOrWhiteSpace(archive))
				throw new ArgumentException("缺少 --apply-update <压缩包路径>");
			archive = Path.GetFullPath(archive);
			if (!File.Exists(archive))
				throw new FileNotFoundException("更新包不存在", archive);
			if (string.IsNullOrWhiteSpace(target))
				target = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
			target = Path.GetFullPath(target);
			if (!Directory.Exists(target))
				throw new DirectoryNotFoundException("目标目录不存在: " + target);

			logline($"apply-update archive={archive}");
			logline($"target={target}");
			logline($"wait-pid={waitPid} restart={restart}");

			if (waitPid > 0)
				waitprocess(waitPid, 120_000);

			// 解压到 tmp/update_extract，再覆盖到安装目录（避免半截写入）
			var extractDir = Path.Combine(target, "tmp", "update_extract");
			try {
				if (Directory.Exists(extractDir))
					Directory.Delete(extractDir, true);
			}
			catch { }
			Directory.CreateDirectory(extractDir);

			logline("extract…");
			extractarchive(archive, extractDir);
			var payload = resolvepayload(extractDir);
			logline("payload=" + payload);

			logline("copy overwrite…");
			copytree(payload, target);
			logline("copy done");

			var mainExe = Path.Combine(target, "WpfOCR.exe");
			if (!File.Exists(mainExe))
				throw new FileNotFoundException(
					"更新后未找到主程序（解压内容可能不正确）: " + mainExe);

			// 清理解压目录（保留下载包，过期由 TmpStore 清理）
			try { Directory.Delete(extractDir, true); } catch { }

			if (restart) {
				var exe = mainExe;
				logline("restart " + exe);
				Process.Start(new ProcessStartInfo {
					FileName = exe,
					WorkingDirectory = target,
					UseShellExecute = true,
				});
			}
			logline("ok");
			return 0;
		}
		catch (Exception ex) {
			try { logline("FAIL: " + ex); } catch { }
			try {
				// WinExe 无控制台时写日志；有控制台则打印
				Console.Error.WriteLine(ex.Message);
			}
			catch { }
			return 1;
		}
	}

	/// <summary>查询最新 Release；网络失败抛异常。</summary>
	public static async Task<UpdateInfo> CheckLatestAsync(CancellationToken ct = default) {
		var cur = CurrentVersion();
		var urls = FeatureInstaller.ExpandUrls(REPO_API);
		Exception last = null;
		foreach (var url in urls) {
			ct.ThrowIfCancellationRequested();
			try {
				using var req = new HttpRequestMessage(HttpMethod.Get, url);
				using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
				resp.EnsureSuccessStatusCode();
				var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
				var info = parserrelease(json, cur);
				if (info != null) return info;
			}
			catch (Exception ex) {
				last = ex;
			}
		}
		throw new InvalidOperationException(
			"无法获取更新信息" + (last != null ? "：" + last.Message : ""), last);
	}

	/// <summary>下载更新包到 tmp/，返回本地路径。</summary>
	public static async Task<string> DownloadAsync(
		UpdateInfo info,
		IProgress<InstallProgress> progress,
		IProgress<string> log,
		CancellationToken ct = default) {
		if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
			throw new ArgumentException("无效的更新信息");
		var name = string.IsNullOrWhiteSpace(info.AssetName)
			? "WpfOCR_update" + Path.GetExtension(new Uri(info.DownloadUrl).AbsolutePath)
			: info.AssetName;
		// 去掉路径非法字符
		foreach (var c in Path.GetInvalidFileNameChars())
			name = name.Replace(c, '_');
		var dest = Path.Combine(TmpStore.Root, name);
		var urls = FeatureInstaller.ExpandUrls(info.DownloadUrl);
		await FeatureInstaller.DownloadUrlAsync(
			urls, dest, log, progress, ct, expectedTotal: info.SizeBytes).ConfigureAwait(false);
		if (!File.Exists(dest) || new FileInfo(dest).Length < 64)
			throw new InvalidOperationException("下载失败或文件过小: " + dest);
		return dest;
	}

	/// <summary>
	/// 将主程序复制到 tmp/，启动命令行解压覆盖，然后退出当前进程。
	/// 调用后当前进程应视为已结束（Environment.Exit）。
	/// </summary>
	public static void LaunchUpdaterAndExit(string archivePath) {
		if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
			throw new FileNotFoundException("更新包不存在", archivePath);

		var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
		var tmp = TmpStore.Root;
		Directory.CreateDirectory(tmp);

		var srcExe = Process.GetCurrentProcess().MainModule?.FileName;
		if (string.IsNullOrEmpty(srcExe) || !File.Exists(srcExe))
			srcExe = Path.Combine(baseDir, "WpfOCR.exe");
		if (!File.Exists(srcExe))
			throw new FileNotFoundException("找不到主程序", srcExe);

		var updaterExe = Path.Combine(tmp, UPDATER_EXE);
		File.Copy(srcExe, updaterExe, true);
		// 绑定重定向配置
		var srcCfg = srcExe + ".config";
		if (File.Exists(srcCfg)) {
			try { File.Copy(srcCfg, updaterExe + ".config", true); } catch { }
		}
		// 复制根目录托管/原生 DLL，保证从 tmp 启动时程序集探测成功
		copyupdaterdeps(baseDir, tmp);

		var pid = Process.GetCurrentProcess().Id;
		var args = $"--apply-update \"{archivePath}\" --target \"{baseDir.TrimEnd('\\', '/')}\" --wait-pid {pid} --restart";
		logline($"launch updater: {updaterExe} {args}");

		var psi = new ProcessStartInfo {
			FileName = updaterExe,
			Arguments = args,
			WorkingDirectory = tmp,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		var p = Process.Start(psi);
		if (p == null)
			throw new InvalidOperationException("无法启动更新进程");

		// 硬退出：释放文件锁，不走关窗确认
		Environment.Exit(0);
	}

	// ───────── 解析 Release JSON ─────────

	static UpdateInfo parserrelease(string json, string currentVersion) {
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
		var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
		var html = root.TryGetProperty("html_url", out var h) ? h.GetString() : REPO_PAGE;
		var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
		var ver = normalizever(tag);
		if (string.IsNullOrEmpty(ver) && !string.IsNullOrEmpty(name))
			ver = normalizever(name);
		if (string.IsNullOrEmpty(ver))
			ver = tag.Trim().TrimStart('v', 'V');

		string assetUrl = null, assetName = null;
		long assetSize = 0;
		if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array) {
			JsonElement? preferred = null;
			foreach (var a in assets.EnumerateArray()) {
				var an = a.TryGetProperty("name", out var anEl) ? anEl.GetString() : null;
				if (string.IsNullOrEmpty(an)) continue;
				var lower = an.ToLowerInvariant();
				if (lower.EndsWith(".7z") || lower.EndsWith(".zip")) {
					// 优先 WpfOCR_*.7z / WpfOCR_*.zip
					if (lower.StartsWith("wpfocr")) {
						preferred = a;
						break;
					}
					preferred ??= a;
				}
			}
			if (preferred != null) {
				var a = preferred.Value;
				assetName = a.TryGetProperty("name", out var an2) ? an2.GetString() : null;
				assetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
				if (a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s))
					assetSize = s;
			}
		}

		if (string.IsNullOrEmpty(assetUrl))
			throw new InvalidOperationException("最新 Release 没有可用的 .7z/.zip 安装包");

		var cur = normalizever(currentVersion) ?? currentVersion;
		var has = isnewer(ver, cur);

		return new UpdateInfo {
			Version = ver,
			TagName = tag,
			DownloadUrl = assetUrl,
			AssetName = assetName,
			SizeBytes = assetSize,
			HtmlUrl = html ?? REPO_PAGE,
			Body = body,
			HasUpdate = has,
			CurrentVersion = cur,
		};
	}

	static string normalizever(string s) {
		if (string.IsNullOrWhiteSpace(s)) return null;
		s = s.Trim();
		// 去 v 前缀
		if (s.Length > 1 && (s[0] == 'v' || s[0] == 'V') && char.IsDigit(s[1]))
			s = s[1..];
		// 取 x.y.z
		var m = Regex.Match(s, @"\d+(?:\.\d+){0,3}");
		return m.Success ? m.Value : s;
	}

	/// <summary>remote 是否比 local 新。</summary>
	static bool isnewer(string remote, string local) {
		if (!tryparsever(remote, out var r)) return !string.Equals(remote, local, StringComparison.OrdinalIgnoreCase);
		if (!tryparsever(local, out var l)) return true;
		return r > l;
	}

	static bool tryparsever(string s, out Version v) {
		v = null;
		if (string.IsNullOrWhiteSpace(s)) return false;
		s = normalizever(s) ?? s;
		// Version 需要至少 Major.Minor
		var parts = s.Split('.');
		if (parts.Length == 1) s += ".0";
		return Version.TryParse(s, out v);
	}

	// ───────── 复制 / 解压 ─────────

	static void copyupdaterdeps(string baseDir, string tmpDir) {
		try {
			foreach (var f in Directory.EnumerateFiles(baseDir)) {
				var name = Path.GetFileName(f);
				if (string.IsNullOrEmpty(name)) continue;
				var ext = Path.GetExtension(name).ToLowerInvariant();
				// 主程序本体已单独复制为 WpfOCR_updater.exe，跳过
				if (name.Equals("WpfOCR.exe", StringComparison.OrdinalIgnoreCase)) continue;
				if (name.Equals(UPDATER_EXE, StringComparison.OrdinalIgnoreCase)) continue;
				if (ext is not (".dll" or ".config" or ".json" or ".exe")) continue;
				// 跳过体积大的独立工具，更新进程不需要
				if (name.StartsWith("processor_", StringComparison.OrdinalIgnoreCase)) continue;
				try {
					File.Copy(f, Path.Combine(tmpDir, name), true);
				}
				catch { }
			}
		}
		catch { }
	}

	static void waitprocess(int pid, int timeoutMs) {
		try {
			Process p = null;
			try { p = Process.GetProcessById(pid); } catch { return; }
			if (p == null || p.HasExited) return;
			if (!p.WaitForExit(timeoutMs))
				throw new TimeoutException($"等待进程 {pid} 退出超时");
		}
		catch (ArgumentException) {
			// 进程已不存在
		}
		// 给文件句柄释放留一点时间
		var until = Environment.TickCount + 800;
		while (Environment.TickCount - until < 0)
			Thread.Sleep(50);
	}

	static void extractarchive(string archive, string destDir) {
		Directory.CreateDirectory(destDir);
		var ext = Path.GetExtension(archive).ToLowerInvariant();
		if (ext == ".zip") {
			ZipFile.ExtractToDirectory(archive, destDir);
			return;
		}
		if (ext is ".7z" or ".rar") {
			var seven = find7z();
			if (seven == null)
				throw new InvalidOperationException(
					"未找到 7-Zip（7z.exe），无法解压 " + Path.GetFileName(archive)
					+ "。请安装 7-Zip，或使用 .zip 格式的发布包。");
			// -o 与路径之间无空格
			var psi = new ProcessStartInfo {
				FileName = seven,
				Arguments = $"x \"{archive}\" -o\"{destDir}\" -y -bb0",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using var p = Process.Start(psi);
			if (p == null) throw new InvalidOperationException("无法启动 7z");
			var stdout = p.StandardOutput.ReadToEnd();
			var err = p.StandardError.ReadToEnd();
			if (!p.WaitForExit(600_000)) {
				try { p.Kill(); } catch { }
				throw new InvalidOperationException("7z 解压超时");
			}
			if (p.ExitCode != 0)
				throw new InvalidOperationException(
					"7z 解压失败 exit=" + p.ExitCode + " " + (err ?? stdout));
			return;
		}
		throw new InvalidOperationException("不支持的更新包格式: " + ext);
	}

	static string find7z() {
		// 优先固定安装路径（避免 where 命中 7z.cmd 包装脚本）
		string[] candidates = [
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
			@"C:\Program Files\7-Zip\7z.exe",
			@"C:\Program Files (x86)\7-Zip\7z.exe",
			@"C:\bin\7z.exe",
			@"C:\bin\7za.exe",
		];
		foreach (var c in candidates) {
			if (File.Exists(c)) return c;
		}
		string[] names = ["7z.exe", "7za.exe"];
		foreach (var n in names) {
			try {
				var psi = new ProcessStartInfo {
					FileName = "where.exe",
					Arguments = n,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					CreateNoWindow = true,
				};
				using var p = Process.Start(psi);
				if (p == null) continue;
				var all = p.StandardOutput.ReadToEnd();
				p.WaitForExit(3000);
				if (string.IsNullOrWhiteSpace(all)) continue;
				foreach (var line in all.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) {
					var path = line.Trim();
					if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
					// 只要真正的 exe，不要 .cmd/.bat 包装
					if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
					return path;
				}
			}
			catch { }
		}
		return null;
	}

	/// <summary>若压缩包内只有单层目录（如 WpfOCR/），则进入该层。</summary>
	static string resolvepayload(string extractDir) {
		var exe = Path.Combine(extractDir, "WpfOCR.exe");
		if (File.Exists(exe)) return extractDir;
		try {
			var dirs = Directory.GetDirectories(extractDir);
			if (dirs.Length == 1) {
				var sub = dirs[0];
				if (File.Exists(Path.Combine(sub, "WpfOCR.exe")))
					return sub;
			}
			// 任意子树找 WpfOCR.exe
			var found = Directory.GetFiles(extractDir, "WpfOCR.exe", SearchOption.AllDirectories).FirstOrDefault();
			if (found != null)
				return Path.GetDirectoryName(found) ?? extractDir;
		}
		catch { }
		return extractDir;
	}

	static void copytree(string srcDir, string dstDir) {
		foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)) {
			var rel = file.Substring(srcDir.Length).TrimStart('\\', '/');
			if (string.IsNullOrEmpty(rel)) continue;
			// 不覆盖用户数据目录
			if (relstartswith(rel, "ocrmodels") || relstartswith(rel, "asrmodels")
				|| relstartswith(rel, "ttsmodels") || relstartswith(rel, "tmp")
				|| relstartswith(rel, "log") || relstartswith(rel, "screenshots")
				|| relstartswith(rel, "onnxgpu64") || relstartswith(rel, "onnxdml64")
				|| relstartswith(rel, "onnxcpu64") || relstartswith(rel, "ffmpeg64")
				|| relstartswith(rel, "translatemodels"))
				continue;
			// 保留用户配置
			if (rel.Equals("config.toml", StringComparison.OrdinalIgnoreCase))
				continue;

			var dest = Path.Combine(dstDir, rel);
			var parent = Path.GetDirectoryName(dest);
			if (!string.IsNullOrEmpty(parent))
				Directory.CreateDirectory(parent);
			// 带重试：偶发杀毒/句柄延迟
			copywithretry(file, dest, 8);
		}
	}

	static bool relstartswith(string rel, string folder) =>
		rel.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)
		|| rel.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
		|| rel.Equals(folder, StringComparison.OrdinalIgnoreCase);

	static void copywithretry(string src, string dest, int tries) {
		Exception last = null;
		for (int i = 0; i < tries; i++) {
			try {
				File.Copy(src, dest, true);
				return;
			}
			catch (Exception ex) {
				last = ex;
				Thread.Sleep(150 + i * 100);
			}
		}
		throw new IOException("覆盖失败: " + dest + " — " + last?.Message, last);
	}

	static void logline(string msg) {
		try {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
			// 更新器在 tmp/ 下运行时，日志写到目标侧较难；写自身目录与上级 tmp
			var paths = new[] {
				Path.Combine(dir, "update_apply.log"),
				Path.Combine(dir, "tmp", "update_apply.log"),
			};
			var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}{Environment.NewLine}";
			foreach (var p in paths) {
				try {
					var parent = Path.GetDirectoryName(p);
					if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
					File.AppendAllText(p, line, Encoding.UTF8);
				}
				catch { }
			}
		}
		catch { }
	}
}
