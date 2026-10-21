using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>GitHub Releases 检查到的最新版本信息。</summary>
public sealed class UpdateInfo {
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

/// <summary>一条 Release 的中英文更新说明。</summary>
public sealed class ReleaseNote {
	public string Version { get; set; }
	public string TagName { get; set; }
	public string Published { get; set; }
	public string Body { get; set; }
	public string English { get; set; }
	public string Chinese { get; set; }
	/// <summary>窗口里的「全部」，正文由各版本拼出来。</summary>
	public bool All { get; set; }
}

/// <summary>GitHub Releases 上最新 Android APK。</summary>
sealed class ApkInfo {
	public string Version { get; set; }
	public string TagName { get; set; }
	public string DownloadUrl { get; set; }
	public string DisplayUrl { get; set; }
	public string AssetName { get; set; }
	public long SizeBytes { get; set; }
	public string HtmlUrl { get; set; }
	public bool HasApk { get; set; }
}

/// <summary>
/// 自更新：查询 GitHub Releases → 下载到 tmp/ → 复制主程序到 tmp/ →
/// 命令行模式解压覆盖安装目录 → 可选重启。
/// </summary>
static class AppUpdater {
	const string REPO_API = "https://api.github.com/repos/cfwang123/ScreenKit/releases/latest";
	const string REPO_API_LIST = "https://api.github.com/repos/cfwang123/ScreenKit/releases?per_page=30";
	const string REPO_API_PAGE = "https://api.github.com/repos/cfwang123/ScreenKit/releases?per_page=100&page=";
	const string REPO_PAGE = "https://github.com/cfwang123/ScreenKit/releases";
	const string UPDATER_EXE = "ScreenKit_updater.exe";

	static readonly HttpClient Http = createhttp();

	static HttpClient createhttp() {
		try {
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		catch { }
		var c = new HttpClient(HttpProxy.CreateHandler());
		c.Timeout = TimeSpan.FromMinutes(30);
		c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ScreenKit-Updater/1.0");
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
			droplegacyexe(target);

			var mainExe = findmainexe(target);
			if (!File.Exists(mainExe))
				throw new FileNotFoundException(
					"更新后未找到主程序（解压内容可能不正确）: " + mainExe);

			// 清理解压目录（临时目录里的下载包由 TmpStore 清理）
			try { Directory.Delete(extractDir, true); } catch { }
			// 连不上服务器时放在程序目录的安装包，成功后删掉
			try {
				var arcDir = Path.GetFullPath(Path.GetDirectoryName(archive) ?? "");
				var tgt = Path.GetFullPath(target).TrimEnd('\\', '/');
				if (string.Equals(arcDir.TrimEnd('\\', '/'), tgt, StringComparison.OrdinalIgnoreCase))
					File.Delete(archive);
			}
			catch { }

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

	/// <summary>查询最新 Release；网络失败抛异常。地址空则查 GitHub。</summary>
	public static Task<UpdateInfo> CheckLatestAsync(CancellationToken ct = default) =>
		checklatest(null, ct);

	/// <summary>向指定更新地址查询。空字符串仍查 GitHub。</summary>
	public static Task<UpdateInfo> CheckLatestAsync(string baseUrl, CancellationToken ct = default) =>
		checklatest(baseUrl, ct);

	/// <summary>http 或 https 的绝对地址。</summary>
	public static bool IsUpdateBase(string baseUrl) => resolveexplicit(baseUrl).Length > 0;

	static async Task<UpdateInfo> checklatest(string baseUrl, CancellationToken ct) {
		var cur = CurrentVersion();
		var custom = resolvebase(baseUrl);
		var api = custom.Length > 0 ? custom + "/latest.json" : REPO_API;
		var json = await fetchjson(api, ct, custom.Length > 0).ConfigureAwait(false);
		var info = parserrelease(json, cur);
		if (info != null) return info;
		throw new InvalidOperationException("无法解析更新信息");
	}

	/// <summary>remote 是否比 local 新（与自更新比较规则一致）。</summary>
	public static bool IsNewerVersion(string remote, string local) => isnewer(remote, local);

	/// <summary>规范化版本号（去 v 前缀、取 x.y.z）。</summary>
	public static string NormalizeVersion(string s) => normalizever(s);

	/// <summary>版本比较。a 比 b 旧时为负。</summary>
	public static int CompareVersion(string a, string b) {
		var ha = tryparsever(a, out var va);
		var hb = tryparsever(b, out var vb);
		if (ha && hb) return va.CompareTo(vb);
		if (ha) return 1;
		if (hb) return -1;
		return string.Compare(NormalizeVersion(a) ?? a, NormalizeVersion(b) ?? b, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>把 Release 正文拆成英文和中文。没有语言标题时两边都是全文。</summary>
	public static void SplitNotes(string body, out string english, out string chinese) {
		english = "";
		chinese = "";
		if (string.IsNullOrWhiteSpace(body)) return;
		var text = body.Replace("\r\n", "\n").Replace('\r', '\n');
		var lines = text.Split('\n');
		var pre = new StringBuilder();
		var en = new StringBuilder();
		var zh = new StringBuilder();
		var mode = 0;
		foreach (var line in lines) {
			var kind = langmarker(line);
			if (kind == 1) { mode = 1; continue; }
			if (kind == 2) { mode = 2; continue; }
			var dest = mode == 1 ? en : mode == 2 ? zh : pre;
			dest.AppendLine(line);
		}
		var p = pre.ToString().Trim();
		var e = en.ToString().Trim();
		var z = zh.ToString().Trim();
		if (e.Length == 0 && z.Length == 0) {
			english = chinese = p.Length > 0 ? p : text.Trim();
			return;
		}
		if (p.Length > 0) {
			if (e.Length > 0) e = p + "\n\n" + e;
			if (z.Length > 0) z = p + "\n\n" + z;
		}
		english = e;
		chinese = z;
	}

	/// <summary>用最新版本信息做一条说明（列表接口失败时）。</summary>
	public static ReleaseNote FromUpdate(UpdateInfo info) {
		var body = info?.Body ?? "";
		SplitNotes(body, out var en, out var zh);
		return new ReleaseNote {
			Version = info?.Version ?? "",
			TagName = info?.TagName ?? "",
			Body = body,
			English = en,
			Chinese = zh,
		};
	}

	/// <summary>解析一页 Release JSON，只保留比 current 新的正式版。</summary>
	public static List<ReleaseNote> ParseReleaseList(string json, string currentVersion) {
		return sortnotes(parsenotes(json, currentVersion, out _, out _));
	}

	/// <summary>自建服务器的更新日志。不是该格式时返回 null。含全部版本，不按当前版本过滤。</summary>
	public static List<ReleaseNote> ParseVersionLog(string json) {
		if (!tryversionlog(json, out var list)) return null;
		return list;
	}

	/// <summary>只留比 current 新、且不比 latest 更新的版本。latest 空则不设上限。从旧到新。</summary>
	public static List<ReleaseNote> BetweenVersions(IList<ReleaseNote> src, string current, string latest) {
		var list = new List<ReleaseNote>();
		if (src != null) {
			foreach (var n in src) {
				if (n == null || n.All || string.IsNullOrWhiteSpace(n.Version)) continue;
				if (!isnewer(n.Version, current)) continue;
				if (!string.IsNullOrWhiteSpace(latest) && isnewer(n.Version, latest)) continue;
				list.Add(n);
			}
		}
		return sortnotes(list);
	}

	/// <summary>当前版本之后、到最新版的每条 Release 说明。按版本从旧到新。</summary>
	public static Task<List<ReleaseNote>> ListNewerNotesAsync(string currentVersion, CancellationToken ct = default) =>
		listnotes(currentVersion, null, ct);

	/// <summary>向指定更新地址取说明。空字符串仍查 GitHub。</summary>
	public static Task<List<ReleaseNote>> ListNewerNotesAsync(
		string currentVersion, string baseUrl, CancellationToken ct = default) =>
		listnotes(currentVersion, baseUrl, ct);

	static async Task<List<ReleaseNote>> listnotes(string currentVersion, string baseUrl, CancellationToken ct) {
		var cur = NormalizeVersion(currentVersion) ?? (currentVersion ?? "").Trim();
		var custom = resolvebase(baseUrl);
		if (custom.Length > 0) {
			var json = await fetchverlog(custom, ct).ConfigureAwait(false);
			var log = ParseVersionLog(json);
			if (log != null) return BetweenVersions(log, cur, null);
			return sortnotes(parsenotes(json, cur, out _, out _));
		}
		var all = new List<ReleaseNote>();
		for (int page = 1; page <= 4; page++) {
			ct.ThrowIfCancellationRequested();
			var json = await fetchjson(REPO_API_PAGE + page, ct, false).ConfigureAwait(false);
			var batch = parsenotes(json, cur, out var count, out var hitOlder);
			all.AddRange(batch);
			if (count == 0 || count < 100 || hitOlder) break;
		}
		return sortnotes(all);
	}

	static async Task<string> fetchverlog(string custom, CancellationToken ct) {
		Exception last = null;
		foreach (var name in new[] { "changelog.json", "releases.json" }) {
			try {
				return await fetchjson(custom + "/" + name, ct, true).ConfigureAwait(false);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex) {
				if (ct.IsCancellationRequested) throw;
				last = ex;
			}
		}
		throw last ?? new InvalidOperationException("无法获取更新说明");
	}

	/// <summary>null 读配置。空字符串表示仍用 GitHub。</summary>
	static string resolvebase(string over) {
		if (over != null) return resolveexplicit(over);
		try {
			var o = new OcrOptions();
			AppConfig.LoadInto(o);
			return resolveexplicit(o.UpdateUrl);
		}
		catch { return ""; }
	}

	static string resolveexplicit(string raw) {
		var s = (raw ?? "").Trim();
		if (s.Length == 0) return "";
		if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return "";
		if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return "";
		if (!string.IsNullOrEmpty(u.UserInfo)) return "";
		var path = u.AbsolutePath.TrimEnd('/');
		return u.GetLeftPart(UriPartial.Authority) + path;
	}

	/// <summary>查询最新 Android APK；无 APK 时仍返回发布页链接。</summary>
	public static async Task<ApkInfo> CheckLatestApkAsync(CancellationToken ct = default) {
		var latestJson = await fetchjson(REPO_API, ct).ConfigureAwait(false);
		var latest = parseapkobj(latestJson);
		if (latest != null && latest.HasApk) return latest;
		try {
			var listJson = await fetchjson(REPO_API_LIST, ct).ConfigureAwait(false);
			var fromList = parseapklist(listJson);
			if (fromList != null && fromList.HasApk) return fromList;
		}
		catch { }
		return latest ?? new ApkInfo {
			HtmlUrl = REPO_PAGE,
			DownloadUrl = REPO_PAGE,
			DisplayUrl = REPO_PAGE,
			HasApk = false,
		};
	}

	/// <summary>程序目录中比当前版本新的 screenkit_版本.7z。没有则返回 null。</summary>
	public static string FindOfflinePackage() {
		var dir = AppDomain.CurrentDomain.BaseDirectory;
		var cur = CurrentVersion();
		string best = null;
		string bestVer = null;
		IEnumerable<string> files;
		try { files = Directory.EnumerateFiles(dir, "screenkit_*.7z"); }
		catch { return null; }
		foreach (var f in files) {
			var stem = Path.GetFileNameWithoutExtension(f) ?? "";
			const string prefix = "screenkit_";
			if (stem.Length <= prefix.Length) continue;
			if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
			var ver = stem.Substring(prefix.Length);
			if (!IsNewerVersion(ver, cur)) continue;
			if (best != null && CompareVersion(ver, bestVer) <= 0) continue;
			best = f;
			bestVer = ver;
		}
		return best;
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
			? "ScreenKit_update" + Path.GetExtension(new Uri(info.DownloadUrl).AbsolutePath)
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
			srcExe = findmainexe(baseDir);
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

	static async Task<string> fetchjson(string apiUrl, CancellationToken ct, bool direct = false) {
		// 自建地址只请求这一处。GitHub 仍走国内镜像，失败再试官方。
		var urls = direct ? new[] { apiUrl } : FeatureInstaller.ExpandUrls(apiUrl);
		Exception last = null;
		foreach (var url in urls) {
			ct.ThrowIfCancellationRequested();
			try {
				using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
				attempt.CancelAfter(TimeSpan.FromSeconds(12));
				using var req = new HttpRequestMessage(HttpMethod.Get, url);
				using var resp = await Http.SendAsync(req, attempt.Token).ConfigureAwait(false);
				resp.EnsureSuccessStatusCode();
				var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json)) return json;
			}
			catch (Exception ex) {
				if (ct.IsCancellationRequested) throw;
				last = ex;
			}
		}
		throw new InvalidOperationException(
			"无法获取更新信息" + (last != null ? "：" + last.Message : ""), last);
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
					if (lower.StartsWith("screenkit")) {
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

	static ApkInfo parseapkobj(string json) {
		using var doc = JsonDocument.Parse(json);
		return parseapk(doc.RootElement);
	}

	static ApkInfo parseapklist(string json) {
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.ValueKind != JsonValueKind.Array) return parseapk(root);
		ApkInfo first = null;
		foreach (var el in root.EnumerateArray()) {
			var info = parseapk(el);
			if (info == null) continue;
			first ??= info;
			if (info.HasApk) return info;
		}
		return first;
	}

	static ApkInfo parseapk(JsonElement root) {
		var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
		var html = root.TryGetProperty("html_url", out var h) ? h.GetString() : REPO_PAGE;
		var ver = normalizever(tag);
		if (string.IsNullOrEmpty(ver))
			ver = tag.Trim().TrimStart('v', 'V');

		string assetUrl = null, assetName = null;
		long assetSize = 0;
		if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array) {
			JsonElement? preferred = null;
			JsonElement? anyApk = null;
			foreach (var a in assets.EnumerateArray()) {
				var an = a.TryGetProperty("name", out var anEl) ? anEl.GetString() : null;
				if (string.IsNullOrEmpty(an)) continue;
				var lower = an.ToLowerInvariant();
				if (!lower.EndsWith(".apk")) continue;
				anyApk ??= a;
				if (lower.IndexOf("debug", StringComparison.Ordinal) >= 0) continue;
				if (lower.StartsWith("screenkit") || lower.IndexOf("android", StringComparison.Ordinal) >= 0) {
					preferred = a;
					break;
				}
				preferred ??= a;
			}
			preferred ??= anyApk;
			if (preferred != null) {
				var a = preferred.Value;
				assetName = a.TryGetProperty("name", out var an2) ? an2.GetString() : null;
				assetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
				if (a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s))
					assetSize = s;
			}
		}

		var has = !string.IsNullOrEmpty(assetUrl);
		var page = string.IsNullOrEmpty(html) ? REPO_PAGE : html;
		var dl = has ? assetUrl : page;
		var display = has ? (FeatureInstaller.ExpandUrls(dl).FirstOrDefault() ?? dl) : page;
		return new ApkInfo {
			Version = ver,
			TagName = tag,
			DownloadUrl = dl,
			DisplayUrl = display,
			AssetName = assetName,
			SizeBytes = assetSize,
			HtmlUrl = page,
			HasApk = has,
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

	static int langmarker(string line) {
		if (string.IsNullOrWhiteSpace(line)) return 0;
		var s = line.Trim();
		var heading = false;
		int i = 0;
		while (i < s.Length && s[i] == '#') i++;
		if (i > 0) {
			if (i > 6 || i >= s.Length || s[i] != ' ') return 0;
			s = s[(i + 1)..].Trim();
			heading = true;
		}
		if (s.Length > 4 && s.StartsWith("**", StringComparison.Ordinal) && s.EndsWith("**", StringComparison.Ordinal)) {
			s = s.Substring(2, s.Length - 4).Trim();
			heading = true;
		}
		else if (s.Length > 4 && s.StartsWith("__", StringComparison.Ordinal) && s.EndsWith("__", StringComparison.Ordinal)) {
			s = s.Substring(2, s.Length - 4).Trim();
			heading = true;
		}
		if (!heading) return 0;
		s = s.TrimEnd(':', '：').Trim();
		if (islang(s, "english") || islang(s, "en") || islang(s, "英文")) return 1;
		if (islang(s, "中文") || islang(s, "简体中文") || islang(s, "chinese") || islang(s, "zh")) return 2;
		return 0;
	}

	static bool islang(string s, string token) =>
		string.Equals(s, token, StringComparison.OrdinalIgnoreCase);

	static bool tryversionlog(string json, out List<ReleaseNote> list) {
		list = null;
		if (string.IsNullOrWhiteSpace(json)) return false;
		try {
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return false;
			if (!root.TryGetProperty("versions", out var arr) || arr.ValueKind != JsonValueKind.Array)
				return false;
			list = new List<ReleaseNote>();
			foreach (var el in arr.EnumerateArray()) {
				if (el.ValueKind != JsonValueKind.Object) continue;
				var ver = el.TryGetProperty("version", out var v) ? v.GetString() : null;
				ver = normalizever(ver);
				if (string.IsNullOrEmpty(ver)) continue;
				var date = el.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
				var en = el.TryGetProperty("english", out var e) ? e.GetString() ?? "" : "";
				var zh = el.TryGetProperty("chinese", out var z) ? z.GetString() ?? "" : "";
				var body = new StringBuilder();
				if (en.Length > 0) body.Append("## English\n\n").Append(en.Trim()).Append("\n\n");
				if (zh.Length > 0) body.Append("## 中文\n\n").Append(zh.Trim()).Append('\n');
				list.Add(new ReleaseNote {
					Version = ver,
					TagName = "v" + ver,
					Published = date.Trim(),
					Body = body.ToString().Trim(),
					English = en.Trim(),
					Chinese = zh.Trim(),
				});
			}
			return true;
		}
		catch {
			list = null;
			return false;
		}
	}

	static List<ReleaseNote> parsenotes(string json, string currentVersion, out int count, out bool hitOlder) {
		count = 0;
		hitOlder = false;
		var list = new List<ReleaseNote>();
		if (string.IsNullOrWhiteSpace(json)) return list;
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.ValueKind != JsonValueKind.Array) return list;
		count = root.GetArrayLength();
		var cur = normalizever(currentVersion) ?? currentVersion;
		foreach (var el in root.EnumerateArray()) {
			if (el.ValueKind != JsonValueKind.Object) continue;
			if (jsontrue(el, "draft") || jsontrue(el, "prerelease")) continue;
			var tag = el.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
			var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
			var ver = normalizever(tag);
			if (string.IsNullOrEmpty(ver) && !string.IsNullOrEmpty(name))
				ver = normalizever(name);
			if (string.IsNullOrEmpty(ver))
				ver = tag.Trim().TrimStart('v', 'V');
			if (string.IsNullOrEmpty(ver)) continue;
			if (!isnewer(ver, cur)) {
				hitOlder = true;
				continue;
			}
			var body = el.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
			var pub = el.TryGetProperty("published_at", out var p) ? p.GetString() : null;
			if (!string.IsNullOrEmpty(pub) && pub.Length >= 10) pub = pub.Substring(0, 10);
			SplitNotes(body, out var en, out var zh);
			list.Add(new ReleaseNote {
				Version = ver,
				TagName = tag,
				Published = pub ?? "",
				Body = body,
				English = en,
				Chinese = zh,
			});
		}
		return list;
	}

	static bool jsontrue(JsonElement el, string name) =>
		el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

	static List<ReleaseNote> sortnotes(List<ReleaseNote> src) {
		var map = new Dictionary<string, ReleaseNote>(StringComparer.OrdinalIgnoreCase);
		if (src != null) {
			foreach (var n in src) {
				if (n == null || string.IsNullOrWhiteSpace(n.Version)) continue;
				if (!map.ContainsKey(n.Version)) map[n.Version] = n;
			}
		}
		var list = map.Values.ToList();
		list.Sort((a, b) => CompareVersion(a.Version, b.Version));
		return list;
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
				// 主程序本体已单独复制为 ScreenKit_updater.exe，跳过
				if (name.Equals(AppNames.ExeFile, StringComparison.OrdinalIgnoreCase)) continue;
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

	static bool hasmainexe(string dir) =>
		File.Exists(Path.Combine(dir, AppNames.ExeFile));

	static string findmainexe(string dir) =>
		Path.Combine(dir, AppNames.ExeFile);

	static void droplegacyexe(string dir) {
		if (!File.Exists(Path.Combine(dir, AppNames.ExeFile))) return;
		foreach (var n in new[] { "WpfOCR.exe", "WpfOCR.exe.config", "WpfOCR_updater.exe" }) {
			try { File.Delete(Path.Combine(dir, n)); } catch { }
		}
	}

	/// <summary>若压缩包内只有单层目录（如 ScreenKit/），则进入该层。</summary>
	static string resolvepayload(string extractDir) {
		if (hasmainexe(extractDir)) return extractDir;
		try {
			var dirs = Directory.GetDirectories(extractDir);
			if (dirs.Length == 1 && hasmainexe(dirs[0]))
				return dirs[0];
			var found = Directory.GetFiles(extractDir, AppNames.ExeFile, SearchOption.AllDirectories).FirstOrDefault();
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
				|| relstartswith(rel, "translatemodels") || relstartswith(rel, "facemodels"))
				continue;
			// 保留用户配置
			if (rel.Equals("config.toml", StringComparison.OrdinalIgnoreCase)
				|| rel.Equals("pinyin.user.toml", StringComparison.OrdinalIgnoreCase))
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
