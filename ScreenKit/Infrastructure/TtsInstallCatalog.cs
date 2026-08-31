using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>可下载的 TTS / 发音人模型项（来自 sherpa-onnx tts-models 发布页）。</summary>
sealed class TtsInstallItem {
	public string Id { get; set; }
	/// <summary>显示名（去扩展名）。</summary>
	public string Title { get; set; }
	/// <summary>归档文件名，如 vits-zh-aishell3.tar.bz2。</summary>
	public string ArchiveName { get; set; }
	/// <summary>GitHub 官方下载地址。</summary>
	public string DownloadUrl { get; set; }
	/// <summary>语言代码：zh / en / ja / multi / de …</summary>
	public string Lang { get; set; }
	/// <summary>语言显示。</summary>
	public string LangLabel { get; set; }
	/// <summary>引擎类型：vits / matcha / piper / kokoro / kitten / other。</summary>
	public string Engine { get; set; }
	public long SizeBytes { get; set; }
	public string SizeText { get; set; }
	public FeatureInstallState State { get; set; }
	public string StateText { get; set; }
	public bool Selected { get; set; }
	/// <summary>当前应用扫描器是否通常能识别（VITS/Matcha）。</summary>
	public bool AppSupported { get; set; }
	public string Detail { get; set; }
}

/// <summary>
/// 发音人（TTS 模型）安装目录：从 GitHub tts-models 拉取全部可下载包，
/// 支持语言筛选；中文环境优先国内镜像下载。
/// </summary>
static class TtsInstallCatalog {
	const string ReleaseApi = "https://api.github.com/repos/k2-fsa/sherpa-onnx/releases/tags/tts-models";
	const string ReleaseDl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models";
	const int CacheHours = 24;

	static readonly HttpClient Http = createhttp();
	static List<TtsInstallItem> cachedAll;
	static string lastSource = "";

	static HttpClient createhttp() {
		var c = new HttpClient(HttpProxy.CreateHandler());
		c.Timeout = TimeSpan.FromMinutes(5);
		c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ScreenKit-TtsInstall/1.0");
		c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
		return c;
	}

	public static string TtsModelsDir =>
		Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ttsmodels"));

	public static string LastSource => lastSource;

	/// <summary>异步加载全部可下载模型（网络 → 缓存 → 内置精选）。</summary>
	public static async Task<List<TtsInstallItem>> LoadAllAsync(
		IProgress<string> log, CancellationToken ct, bool forceRefresh = false) {
		if (!forceRefresh && cachedAll != null && cachedAll.Count > 0) {
			foreach (var it in cachedAll) refreshstate(it);
			return clone(cachedAll);
		}

		List<TtsInstallItem> list = null;
		try {
			list = await fetchfromgithub(log, ct).ConfigureAwait(false);
			if (list != null && list.Count > 0) {
				savecache(list);
				lastSource = "GitHub tts-models（在线）";
			}
		}
		catch (Exception ex) {
			log?.Report("拉取 GitHub 列表失败: " + ex.Message);
		}

		if (list == null || list.Count == 0) {
			list = loadcache();
			if (list != null && list.Count > 0)
				lastSource = "本地缓存";
		}
		if (list == null || list.Count == 0) {
			list = builtinfallback();
			lastSource = "内置精选（离线）";
			log?.Report("使用内置精选列表（网络与缓存均不可用）");
		}

		foreach (var it in list) refreshstate(it);
		cachedAll = list;
		return clone(list);
	}

	static List<TtsInstallItem> clone(List<TtsInstallItem> src) {
		var list = new List<TtsInstallItem>(src.Count);
		foreach (var x in src) {
			list.Add(new TtsInstallItem {
				Id = x.Id,
				Title = x.Title,
				ArchiveName = x.ArchiveName,
				DownloadUrl = x.DownloadUrl,
				Lang = x.Lang,
				LangLabel = x.LangLabel,
				Engine = x.Engine,
				SizeBytes = x.SizeBytes,
				SizeText = x.SizeText,
				State = x.State,
				StateText = x.StateText,
				Selected = x.Selected,
				AppSupported = x.AppSupported,
				Detail = x.Detail,
			});
		}
		return list;
	}

	/// <summary>语言筛选选项（code, display）。</summary>
	public static List<(string Code, string Label)> LanguageOptions(IEnumerable<TtsInstallItem> items) {
		var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var it in items ?? Enumerable.Empty<TtsInstallItem>()) {
			if (!string.IsNullOrEmpty(it.Lang))
				set.Add(it.Lang);
		}
		var list = new List<(string, string)> { ("", "全部语言") };
		// 常用语言置顶
		string[] prefer = ["zh", "en", "zh,en", "ja", "ko", "vi", "yue", "multi"];
		foreach (var p in prefer) {
			if (set.Remove(p))
				list.Add((p, langdisplay(p)));
		}
		foreach (var c in set)
			list.Add((c, langdisplay(c)));
		return list;
	}

	public static IEnumerable<TtsInstallItem> Filter(
		IEnumerable<TtsInstallItem> items, string lang, bool onlyMissing) {
		foreach (var it in items ?? Enumerable.Empty<TtsInstallItem>()) {
			if (onlyMissing && it.State == FeatureInstallState.Installed) continue;
			if (!string.IsNullOrEmpty(lang)) {
				if (!TtsLang.Match(it.Lang, lang)
					&& !string.Equals(it.Lang, lang, StringComparison.OrdinalIgnoreCase)
					&& !(lang == "multi" && (it.Lang == "multi" || it.Lang.Contains(","))))
					continue;
			}
			yield return it;
		}
	}

	public static void RefreshState(TtsInstallItem it) => refreshstate(it);

	static void refreshstate(TtsInstallItem it) {
		var dir = Path.Combine(TtsModelsDir, it.Id);
		if (isinstalled(it.Id)) {
			it.State = FeatureInstallState.Installed;
			it.StateText = "已安装";
			var sz = dirsize(dir);
			if (sz > 0) {
				it.SizeBytes = sz;
				it.SizeText = "本地 " + FeatureInstaller.FormatBytes(sz);
			}
			else
				it.SizeText = FeatureInstaller.FormatBytes(it.SizeBytes);
		}
		else {
			it.State = FeatureInstallState.Missing;
			it.StateText = "未安装";
			it.SizeText = "约 " + FeatureInstaller.FormatBytes(it.SizeBytes);
		}
		// 发音人列表不自动勾选未安装项，由用户手动选择
		it.Selected = false;
	}

	static bool isinstalled(string modelId) {
		if (string.IsNullOrEmpty(modelId)) return false;
		var root = TtsModelsDir;
		if (!Directory.Exists(root)) return false;
		// 精确目录
		var exact = Path.Combine(root, modelId);
		if (Directory.Exists(exact) && hasmodelfiles(exact)) return true;
		// 兼容：去掉 sherpa-onnx- 前缀或带日期
		foreach (var d in Directory.GetDirectories(root)) {
			var name = Path.GetFileName(d) ?? "";
			if (string.Equals(name, modelId, StringComparison.OrdinalIgnoreCase))
				return hasmodelfiles(d);
			// vits-zh-aishell3 vs vits-icefall-zh-aishell3
			if (name.IndexOf(modelId, StringComparison.OrdinalIgnoreCase) >= 0
				|| modelId.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) {
				if (hasmodelfiles(d)) return true;
			}
		}
		return false;
	}

	static bool hasmodelfiles(string dir) {
		try {
			if (!Directory.Exists(dir)) return false;
			if (!File.Exists(Path.Combine(dir, "tokens.txt"))) {
				// matcha 可能用 tiny-tokens 等；宽松：有 onnx 即可
				return Directory.GetFiles(dir, "*.onnx").Length > 0;
			}
			return Directory.GetFiles(dir, "*.onnx").Length > 0;
		}
		catch { return false; }
	}

	static long dirsize(string dir) {
		if (!Directory.Exists(dir)) return 0;
		long n = 0;
		try {
			foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) {
				try { n += new FileInfo(f).Length; } catch { }
			}
		}
		catch { }
		return n;
	}

	/// <summary>安装一项：下载 tar.bz2 并解压到 ttsmodels。</summary>
	public static async Task InstallAsync(
		TtsInstallItem item,
		IProgress<string> log,
		IProgress<InstallProgress> progress,
		CancellationToken ct) {
		if (item == null) throw new ArgumentNullException(nameof(item));
		Directory.CreateDirectory(TtsModelsDir);
		Directory.CreateDirectory(FeatureInstaller.CacheDir);

		if (isinstalled(item.Id)) {
			log?.Report("已存在: " + item.Id);
			progress?.Report(new InstallProgress { Overall = 1, Note = "已安装" });
			return;
		}

		var archivePath = Path.Combine(FeatureInstaller.CacheDir, item.ArchiveName);
		var urls = FeatureInstaller.ExpandUrls(item.DownloadUrl);
		log?.Report("下载 " + item.ArchiveName + " (" + FeatureInstaller.FormatBytes(item.SizeBytes) + ")");
		await FeatureInstaller.DownloadUrlAsync(urls, archivePath, log, progress, ct, item.SizeBytes)
			.ConfigureAwait(false);

		log?.Report("解压到 ttsmodels …");
		var len = File.Exists(archivePath) ? new FileInfo(archivePath).Length : item.SizeBytes;
		progress?.Report(new InstallProgress {
			Overall = 0.92, BytesDone = len, BytesTotal = len,
			FileName = item.ArchiveName, Note = "解压中…",
		});
		FeatureInstaller.ExtractArchive(archivePath, TtsModelsDir, log);

		// Matcha 需要 vocoder：若根目录无 vocos，尝试从包内或已知 URL
		if (item.Engine == "matcha")
			await ensurevocoder(log, progress, ct).ConfigureAwait(false);

		// Piper 常需 espeak-ng-data
		if (item.Engine == "piper")
			await ensureespeak(log, progress, ct).ConfigureAwait(false);

		if (!isinstalled(item.Id)) {
			// 解压后目录名可能与 Id 略有差异
			log?.Report("警告: 解压后未精确匹配目录 " + item.Id + "，请检查 ttsmodels");
		}
		progress?.Report(new InstallProgress {
			Overall = 1, BytesDone = len, BytesTotal = len, Note = "完成",
		});
		log?.Report("完成: " + item.Title);
	}

	/// <summary>删除已安装的发音人模型目录。</summary>
	public static void Uninstall(TtsInstallItem item, IProgress<string> log) {
		if (item == null) return;
		var root = TtsModelsDir;
		if (!Directory.Exists(root)) {
			log?.Report("无 ttsmodels 目录");
			return;
		}
		// 精确 Id
		var exact = Path.Combine(root, item.Id);
		if (Directory.Exists(exact)) {
			try {
				Directory.Delete(exact, true);
				log?.Report("已删除 " + exact);
				return;
			}
			catch (Exception ex) {
				log?.Report("删除失败: " + ex.Message);
				throw;
			}
		}
		// 模糊匹配（目录名包含 Id）
		var any = false;
		foreach (var dir in Directory.GetDirectories(root)) {
			var name = Path.GetFileName(dir) ?? "";
			if (!string.Equals(name, item.Id, StringComparison.OrdinalIgnoreCase)
				&& name.IndexOf(item.Id, StringComparison.OrdinalIgnoreCase) < 0
				&& item.Id.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
				continue;
			try {
				Directory.Delete(dir, true);
				log?.Report("已删除 " + dir);
				any = true;
			}
			catch (Exception ex) {
				log?.Report("删除失败 " + dir + ": " + ex.Message);
				throw;
			}
		}
		if (!any)
			log?.Report("未找到可删目录: " + item.Id);
	}

	static async Task ensurevocoder(IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		var dest = Path.Combine(TtsModelsDir, "vocos-22khz-univ.onnx");
		if (File.Exists(dest) && new FileInfo(dest).Length > 1000) {
			log?.Report("vocoder 已存在");
			return;
		}
		// 部分 matcha 包内可能已有；否则尝试发布页（若无则跳过）
		var urls = FeatureInstaller.ExpandUrls(
			$"{ReleaseDl}/vocos-22khz-univ.onnx",
			"https://huggingface.co/csukuangfj/sherpa-onnx-models/resolve/main/vocos-22khz-univ.onnx");
		try {
			log?.Report("下载 Matcha 所需 vocoder …");
			await FeatureInstaller.DownloadUrlAsync(urls, dest, log, progress, ct, 50L * 1024 * 1024)
				.ConfigureAwait(false);
		}
		catch (Exception ex) {
			log?.Report("vocoder 下载失败（Matcha 可能无法合成）: " + ex.Message);
		}
	}

	static async Task ensureespeak(IProgress<string> log, IProgress<InstallProgress> progress, CancellationToken ct) {
		var dir = Path.Combine(TtsModelsDir, "espeak-ng-data");
		if (Directory.Exists(dir) && Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length > 10)
			return;
		var archive = Path.Combine(FeatureInstaller.CacheDir, "espeak-ng-data.tar.bz2");
		var urls = FeatureInstaller.ExpandUrls($"{ReleaseDl}/espeak-ng-data.tar.bz2");
		try {
			log?.Report("下载 Piper 依赖 espeak-ng-data …");
			await FeatureInstaller.DownloadUrlAsync(urls, archive, log, progress, ct, 8L * 1024 * 1024)
				.ConfigureAwait(false);
			FeatureInstaller.ExtractArchive(archive, TtsModelsDir, log);
		}
		catch (Exception ex) {
			log?.Report("espeak-ng-data 下载失败: " + ex.Message);
		}
	}

	// ───────── 索引拉取 ─────────

	static async Task<List<TtsInstallItem>> fetchfromgithub(IProgress<string> log, CancellationToken ct) {
		log?.Report("GET " + ReleaseApi);
		// API 优先直连 GitHub（代理常对 api.github.com 返回 403）；失败再试代理
		var apis = new[] {
			ReleaseApi,
			"https://ghfast.top/" + ReleaseApi,
			"https://mirror.ghproxy.com/" + ReleaseApi,
		};

		Exception last = null;
		foreach (var api in apis) {
			ct.ThrowIfCancellationRequested();
			try {
				using var req = new HttpRequestMessage(HttpMethod.Get, api);
				req.Headers.TryAddWithoutValidation("User-Agent", "ScreenKit-TtsInstall/1.0");
				using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
				resp.EnsureSuccessStatusCode();
				var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
				var list = parseassets(json);
				if (list.Count > 0) {
					log?.Report($"获取到 {list.Count} 个 TTS 包");
					return list;
				}
			}
			catch (Exception ex) {
				last = ex;
				log?.Report("API 失败: " + api + " — " + ex.Message);
			}
		}
		if (last != null) throw last;
		return new List<TtsInstallItem>();
	}

	static List<TtsInstallItem> parseassets(string json) {
		var list = new List<TtsInstallItem>();
		using var doc = JsonDocument.Parse(json);
		if (!doc.RootElement.TryGetProperty("assets", out var assets))
			return list;
		foreach (var a in assets.EnumerateArray()) {
			var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
			if (string.IsNullOrEmpty(name)) continue;
			// 只收 tar.bz2 模型包；跳过 espeak 依赖（安装 piper 时自动拉）
			if (!name.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase)) continue;
			if (name.Equals("espeak-ng-data.tar.bz2", StringComparison.OrdinalIgnoreCase)) continue;

			long size = 0;
			if (a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number)
				size = sz.GetInt64();
			var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
			if (string.IsNullOrEmpty(url))
				url = $"{ReleaseDl}/{name}";

			var id = name.Substring(0, name.Length - ".tar.bz2".Length);
			var eng = inferengine(id);
			var lang = inferlang(id);
			var item = new TtsInstallItem {
				Id = id,
				Title = id,
				ArchiveName = name,
				DownloadUrl = url,
				Lang = lang,
				LangLabel = langdisplay(lang),
				Engine = eng,
				SizeBytes = size,
				SizeText = "约 " + FeatureInstaller.FormatBytes(size),
				AppSupported = eng is "vits" or "matcha" or "piper",
				Detail = eng + (eng is "vits" or "matcha" or "piper" ? "" : " · 当前引擎可能未接"),
			};
			list.Add(item);
		}
		// 排序：中文/多语优先，再按引擎、名称
		return list
			.OrderBy(x => langrank(x.Lang))
			.ThenBy(x => x.Engine, StringComparer.OrdinalIgnoreCase)
			.ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	static int langrank(string lang) {
		lang = (lang ?? "").ToLowerInvariant();
		if (lang == "zh") return 0;
		if (lang == "zh,en" || lang == "multi") return 1;
		if (lang == "en") return 2;
		if (lang == "ja") return 3;
		if (lang == "ko") return 4;
		if (lang == "vi") return 5;
		if (lang == "yue") return 6;
		return 50;
	}

	static string inferengine(string id) {
		var n = (id ?? "").ToLowerInvariant();
		if (n.StartsWith("matcha")) return "matcha";
		if (n.Contains("piper")) return "piper";
		if (n.StartsWith("kokoro") || n.Contains("kokoro")) return "kokoro";
		if (n.StartsWith("kitten") || n.Contains("kitten")) return "kitten";
		if (n.Contains("zipvoice")) return "zipvoice";
		if (n.Contains("supertonic") || n.Contains("pocket-tts")) return "other";
		if (n.StartsWith("vits") || n.Contains("vits") || n.Contains("coqui") || n.Contains("mimic3") || n.Contains("mms-") || n.Contains("icefall"))
			return "vits";
		return "other";
	}

	/// <summary>从归档名推断语言代码。</summary>
	public static string InferLang(string id) => inferlang(id);

	static string inferlang(string id) {
		if (string.IsNullOrEmpty(id)) return "";
		var n = id.ToLowerInvariant().Replace('.', '-');

		// 显式双语 / 多语
		if (n.Contains("zh_en") || n.Contains("zh-en") || n.Contains("melo-tts-zh")
			|| n.Contains("multi-lang") || n.Contains("multilang"))
			return n.Contains("multi") ? "multi" : "zh,en";
		if (n.Contains("multi")) return "multi";

		// Piper / locale: xx_YY
		var m = Regex.Match(n, @"(?:^|[-_])([a-z]{2})_([a-z]{2})(?:[-_]|$)");
		if (m.Success) {
			var primary = m.Groups[1].Value;
			if (primary == "zh") return "zh";
			if (primary == "en") return "en";
			if (primary == "ja") return "ja";
			if (primary == "ko") return "ko";
			if (primary == "vi") return "vi";
			return primary;
		}

		// cantonese
		if (n.Contains("cantonese") || n.Contains("yue") || n.Contains("-nan") || n.Contains("mms-nan"))
			return n.Contains("cantonese") || n.Contains("yue") ? "yue" : "nan";

		// 中文关键词
		if (n.Contains("aishell") || n.Contains("baker") || n.Contains("fanchen")
			|| n.Contains("-zh-") || n.Contains("_zh_") || n.StartsWith("vits-zh")
			|| n.Contains("zh-ll") || n.Contains("zh_ll") || n.Contains("huayan")
			|| n.Contains("chaowen") || n.Contains("xiao_ya") || n.Contains("xiaomai"))
			return "zh";

		// 英文
		if (n.Contains("ljspeech") || n.Contains("vctk") || n.Contains("-en-") || n.Contains("_en_")
			|| n.Contains("en_us") || n.Contains("en_gb") || n.Contains("english")
			|| n.Contains("glados") && n.Contains("en")
			|| n.StartsWith("kokoro-en") || n.StartsWith("kitten-") && n.Contains("-en-")
			|| n.Contains("mms-eng"))
			return "en";

		// 其它语言 token
		var parts = n.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var p in parts) {
			if (p.Length == 2) {
				if (p is "zh" or "en" or "ja" or "ko" or "vi" or "de" or "fr" or "es" or "it"
					or "pt" or "ru" or "uk" or "pl" or "nl" or "sv" or "da" or "fi" or "no"
					or "tr" or "ar" or "fa" or "hi" or "th" or "id" or "cs" or "sk" or "ro"
					or "hu" or "el" or "bg" or "hr" or "sr" or "sl" or "lt" or "lv" or "et"
					or "ca" or "eu" or "cy" or "ga" or "mt" or "is" or "ka" or "kk" or "uz"
					or "ne" or "bn" or "ta" or "te" or "ml" or "gu" or "pa" or "ur" or "sw")
					return p;
			}
			if (p is "jpn" or "japanese") return "ja";
			if (p is "kor" or "korean") return "ko";
			if (p is "vie" or "vietnamese") return "vi";
			if (p is "deu" or "german") return "de";
			if (p is "fra" or "french") return "fr";
			if (p is "spa" or "spanish") return "es";
			if (p is "rus" or "russian") return "ru";
			if (p is "ukr" or "ukrainian") return "uk";
			if (p is "tha" or "thai") return "th";
		}
		return "other";
	}

	static string langdisplay(string lang) {
		lang = (lang ?? "").Trim().ToLowerInvariant();
		if (string.IsNullOrEmpty(lang)) return "未知";
		if (lang == "multi") return "多语 (multi)";
		if (lang == "zh,en") return "中英 (zh,en)";
		if (lang == "other") return "其它";
		return TtsLang.DisplayName(lang);
	}

	// ───────── 缓存 ─────────

	static string cachepath() =>
		Path.Combine(FeatureInstaller.CacheDir, "tts-models-index.json");

	static void savecache(List<TtsInstallItem> list) {
		try {
			Directory.CreateDirectory(FeatureInstaller.CacheDir);
			var arr = list.Select(x => new {
				x.Id, x.Title, x.ArchiveName, x.DownloadUrl, x.Lang, x.Engine, x.SizeBytes, x.AppSupported,
			}).ToList();
			var json = JsonSerializer.Serialize(arr, new JsonSerializerOptions { WriteIndented = false });
			File.WriteAllText(cachepath(), json, Encoding.UTF8);
		}
		catch { }
	}

	static List<TtsInstallItem> loadcache() {
		try {
			var path = cachepath();
			if (!File.Exists(path)) return null;
			var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
			if (age.TotalHours > CacheHours * 7) // 过期一周仍可读
				{ /* still use */ }
			var json = File.ReadAllText(path, Encoding.UTF8);
			using var doc = JsonDocument.Parse(json);
			var list = new List<TtsInstallItem>();
			foreach (var el in doc.RootElement.EnumerateArray()) {
				var id = el.GetProperty("Id").GetString();
				var arch = el.GetProperty("ArchiveName").GetString();
				var size = el.TryGetProperty("SizeBytes", out var s) ? s.GetInt64() : 0;
				var lang = el.TryGetProperty("Lang", out var l) ? l.GetString() : inferlang(id);
				var eng = el.TryGetProperty("Engine", out var e) ? e.GetString() : inferengine(id);
				list.Add(new TtsInstallItem {
					Id = id,
					Title = el.TryGetProperty("Title", out var t) ? t.GetString() : id,
					ArchiveName = arch,
					DownloadUrl = el.TryGetProperty("DownloadUrl", out var u)
						? u.GetString()
						: $"{ReleaseDl}/{arch}",
					Lang = lang,
					LangLabel = langdisplay(lang),
					Engine = eng,
					SizeBytes = size,
					SizeText = "约 " + FeatureInstaller.FormatBytes(size),
					AppSupported = eng is "vits" or "matcha" or "piper",
					Detail = eng,
				});
			}
			return list.Count > 0 ? list : null;
		}
		catch { return null; }
	}

	/// <summary>离线兜底：常用中英模型。</summary>
	static List<TtsInstallItem> builtinfallback() {
		var names = new (string arch, long size)[] {
			("vits-zh-aishell3.tar.bz2", 146922607),
			("sherpa-onnx-vits-zh-ll.tar.bz2", 118810709),
			("vits-melo-tts-zh_en.tar.bz2", 167006755),
			("matcha-icefall-zh-baker.tar.bz2", 75463442),
			("vits-zh-hf-fanchen-C.tar.bz2", 119326431),
			("vits-zh-hf-eula.tar.bz2", 120562119),
			("vits-zh-hf-theresa.tar.bz2", 120596617),
			("vits-piper-zh_CN-huayan-medium.tar.bz2", 67255926),
			("vits-piper-en_US-lessac-medium.tar.bz2", 67200000),
			("vits-piper-en_GB-alba-medium.tar.bz2", 67200000),
			("vits-coqui-en-ljspeech.tar.bz2", 115418583),
			("matcha-icefall-en_US-ljspeech.tar.bz2", 76741121),
			("vits-piper-ja_JP-nai-medium.tar.bz2", 67200000),
			("vits-piper-ko_KR-kss-medium.tar.bz2", 67200000),
			("vits-piper-vi_VN-vais1000-medium.tar.bz2", 67154040),
		};
		var list = new List<TtsInstallItem>();
		foreach (var (arch, size) in names) {
			var id = arch.Replace(".tar.bz2", "");
			var eng = inferengine(id);
			var lang = inferlang(id);
			list.Add(new TtsInstallItem {
				Id = id,
				Title = id,
				ArchiveName = arch,
				DownloadUrl = $"{ReleaseDl}/{arch}",
				Lang = lang,
				LangLabel = langdisplay(lang),
				Engine = eng,
				SizeBytes = size,
				SizeText = "约 " + FeatureInstaller.FormatBytes(size),
				AppSupported = eng is "vits" or "matcha" or "piper",
				Detail = eng,
			});
		}
		return list;
	}
}
