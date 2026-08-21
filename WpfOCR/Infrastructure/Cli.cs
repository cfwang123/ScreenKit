using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WpfOCR;

/// <summary>命令行模式：OCR 图像 / 探测 CUDA（无 GUI）。</summary>
static class Cli {
	const int ATTACH_PARENT_PROCESS = -1;
	const int STD_OUTPUT_HANDLE = -11;
	const int STD_ERROR_HANDLE = -12;

	static StreamWriter log;

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool AttachConsole(int dwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool AllocConsole();

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr GetStdHandle(int nStdHandle);

	public static bool IsCli(string[] args) {
		if (args == null || args.Length == 0) return false;
		foreach (var a in args) {
			if (a is "--image" or "-i" or "--probe-cuda" or "--list-models" or "--list-tts"
				or "--list-sapi"
				or "--probe-tts-gender" or "--snap" or "--snap-all" or "--record-snap"
				or "--test-capture-during-record" or "--test-overlay-during-record"
				or "--test-record-avsync" or "--test-gif-record" or "--test-record-codec"
				or "--help" or "-h" or "/?"
				or "--asr" or "--list-asr"
				or "--translate" or "--translate-file" or "--list-translate"
				or "--list-install" or "--list-tts-install"
				or "--apply-update" or "--self-update")
				return true;
		}
		return false;
	}

	public static int Run(string[] args) {
		// 自更新：不初始化 CUDA（由 App 入口优先处理；此处兜底）
		if (AppUpdater.IsApplyUpdateArgs(args))
			return AppUpdater.RunApplyUpdate(args);

		try { CudaBootstrap.Init(); } catch { }

		ensureconsole();
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		try {
			Console.OutputEncoding = new UTF8Encoding(false);
			Console.InputEncoding = new UTF8Encoding(false);
		}
		catch { }

		try {
			var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli_last.log");
			log = new StreamWriter(logPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)) { AutoFlush = true };
			Out($"# cli_last.log {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
			Out($"# cwd={Environment.CurrentDirectory}");
			Out($"# base={AppDomain.CurrentDomain.BaseDirectory}");
			Out($"# args={string.Join(" ", args)}");
			Out($"# arch={ArchBootstrap.CurrentLabel} Is64BitProcess={Environment.Is64BitProcess}");
		}
		catch { }

		string image = null;
		string models = null;
		string packId = null;
		string variant = null;
		string device = "auto";
		string snapOut = null;
		bool doSnap = false;
		bool doRecordSnap = false;
		bool doTestCaptureDuringRecord = false;
		bool doTestOverlayDuringRecord = false;
		bool doTestRecordAvsync = false;
		bool doTestGifRecord = false;
		bool doTestRecordCodec = false;
		string testRecordCodec = "av1";
		int testRecordRepeat = 1;
		string recordRegion = null; // L,T,W,H 物理像素
		int recordSnapWaitMs = 800;
		int recordAvsyncSec = 10;
		bool noCls = false;
		bool probeTtsGender = false;
		bool writeConfig = true;
		string onlyTtsModel = null;
		int? detLimit = null;
		int? detPad = null;
		float? boxThresh = null;
		float? detThresh = null;
		string asrAudio = null;
		string asrModel = null;
		string asrLang = "auto";
		string asrDevice = "auto";
		bool asrNoItn = false;
		string translateText = null;
		string translateDir = "zh-en";

		try {
			for (int i = 0; i < args.Length; i++) {
				var a = args[i];
				string Next() {
					if (i + 1 >= args.Length) throw new ArgumentException($"参数 {a} 缺少值");
					return args[++i];
				}
				switch (a) {
					case "--image": case "-i": image = Next(); break;
					case "--models": case "-m": models = Next(); break;
					case "--pack": case "-p": packId = Next(); break;
					case "--variant": case "-v": variant = Next(); break;
					case "--device": case "-d": device = Next().ToLowerInvariant(); break;
					case "--no-cls": noCls = true; break;
					case "--det-limit": detLimit = int.Parse(Next()); break;
					case "--det-pad": detPad = int.Parse(Next()); break;
					case "--box-thresh": boxThresh = float.Parse(Next()); break;
					case "--det-thresh": detThresh = float.Parse(Next()); break;
					case "--snap": case "--snap-all":
						doSnap = true;
						break;
					case "--record-snap":
						doRecordSnap = true;
						break;
					case "--test-capture-during-record":
						doTestCaptureDuringRecord = true;
						break;
					case "--test-overlay-during-record":
						doTestOverlayDuringRecord = true;
						break;
					case "--test-record-avsync":
						doTestRecordAvsync = true;
						break;
					case "--test-gif-record":
						doTestGifRecord = true;
						break;
					case "--test-record-codec":
						doTestRecordCodec = true;
						if (i + 1 < args.Length && args[i + 1].Length > 0 && args[i + 1][0] != '-')
							testRecordCodec = Next();
						break;
					case "--repeat":
						testRecordRepeat = int.Parse(Next());
						break;
					case "--region":
						recordRegion = Next();
						break;
					case "--wait-ms":
						recordSnapWaitMs = int.Parse(Next());
						break;
					case "--seconds":
						recordAvsyncSec = int.Parse(Next());
						break;
					case "--out": case "-o":
						snapOut = Next();
						break;
					case "--list-models":
					return listmodels();
				case "--list-tts":
					return listtts();
				case "--list-sapi":
					return listsapi();
				case "--list-asr":
					return listasr();
				case "--list-install":
					return listinstall();
				case "--list-tts-install":
					return listttsinstall();
				case "--asr":
					asrAudio = Next();
					break;
				case "--asr-model":
					asrModel = Next();
					break;
				case "--asr-lang":
					asrLang = Next();
					break;
				case "--asr-device":
					asrDevice = Next().ToLowerInvariant();
					break;
				case "--asr-no-itn":
					asrNoItn = true;
					break;
				case "--probe-tts-gender":
						probeTtsGender = true;
						break;
					case "--no-write-config":
						writeConfig = false;
						break;
					case "--only-model":
						onlyTtsModel = Next();
						break;
					case "--probe-cuda":
						return probecuda();
					case "--list-translate":
						return listtranslate();
					case "--translate":
						translateText = Next();
						break;
					case "--translate-file":
						translateText = File.ReadAllText(Next(), Encoding.UTF8);
						break;
					case "--tr-dir":
						translateDir = Next().ToLowerInvariant();
						break;
					case "--help": case "-h": case "/?":
						printhelp();
						return 0;
					default:
						if (a.StartsWith("-")) {
							Err($"未知参数: {a}");
							printhelp();
							return 2;
						}
						// 位置参数当图片
						image ??= a;
						break;
				}
			}
		}
		catch (Exception ex) {
			Err(ex.Message);
			printhelp();
			return 2;
		}

		if (doSnap) {
			try {
				return runsnap(snapOut);
			}
			catch (Exception ex) {
				Err($"截图失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (doRecordSnap) {
			try {
				return runrecordsnap(snapOut, recordRegion, recordSnapWaitMs);
			}
			catch (Exception ex) {
				Err($"录屏截图失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (doTestCaptureDuringRecord) {
			try {
				return runtestcaptureduringrecord(snapOut, recordRegion, recordSnapWaitMs);
			}
			catch (Exception ex) {
				Err($"录屏中截屏管线测试失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (doTestOverlayDuringRecord) {
			try {
				return runtestoverlayduringrecord(recordRegion, recordSnapWaitMs);
			}
			catch (Exception ex) {
				Err($"录屏中遮罩测试失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (doTestRecordAvsync) {
			try {
				return runtestrecordavsync(snapOut, recordRegion, recordAvsyncSec);
			}
			catch (Exception ex) {
				Err($"录屏音画同步测试失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (doTestGifRecord) {
			try {
				// --seconds 未指定时默认录 2 秒（勿复用 avsync 默认 10）
				var gifSec = args.Any(a => a == "--seconds") ? Math.Max(1, recordAvsyncSec) : 2;
				return runtestgifrecord(snapOut, recordRegion, gifSec);
			}
			catch (Exception ex) {
				Err($"GIF 录屏测试失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (doTestRecordCodec) {
			try {
				var sec = args.Any(a => a == "--seconds") ? Math.Max(1, recordAvsyncSec) : 2;
				return RecordCodecTest.Run(testRecordCodec, snapOut, recordRegion, sec, testRecordRepeat, Out);
			}
			catch (Exception ex) {
				Err($"录屏编码测试失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (probeTtsGender) {
			try {
				return probeTtsGenderRun(device, writeConfig, onlyTtsModel);
			}
			catch (Exception ex) {
				Err($"TTS 性别探测失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
		}

		if (translateText != null) {
			try {
				return runtranslate(translateText, translateDir, device);
			}
			catch (Exception ex) {
				Err($"翻译失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (!string.IsNullOrWhiteSpace(asrAudio)) {
			try {
				return runasr(asrAudio, asrModel, asrLang, asrDevice, asrNoItn);
			}
			catch (Exception ex) {
				Err($"ASR 错误: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
			finally {
				try { log?.Dispose(); } catch { }
			}
		}

		if (string.IsNullOrWhiteSpace(image)) {
			printhelp();
			return 2;
		}

		try {
			return runocr(image, models, packId, variant, device, noCls, detLimit, detPad, boxThresh, detThresh);
		}
		catch (Exception ex) {
			Err($"错误: {ex.Message}");
			Err(ex.ToString());
			return 1;
		}
		finally {
			try { log?.Dispose(); } catch { }
		}
	}

	static int runocr(string image, string models, string packId, string variant, string device, bool noCls,
		int? detLimit, int? detPad, float? boxThresh, float? detThresh) {
		image = Path.GetFullPath(image);
		if (!File.Exists(image)) {
			Err($"图像不存在: {image}");
			return 1;
		}

		var opt = buildoptions(models, packId, variant, device, noCls, detLimit, detPad, boxThresh, detThresh);
		Out($"模型包: {opt.ModelPackId}");
		Out($"变体: {opt.ModelVariant}");
		Out($"模型目录: {opt.ModelsDir}");
		Out($"图像: {image}");
		Out($"设备: {device}");
		Out($"det: limit={opt.DetLimitSideLen} pad={opt.DetPadding} thresh={opt.DetThresh} box={opt.DetBoxThresh} dilate={opt.DetUseDilation}");

		var tLoad0 = Environment.TickCount;
		using var engine = new OcrEngine(opt);
		var loadMs = Environment.TickCount - tLoad0;
		Out($"会话就绪: model={engine.ModelLabel}, device={engine.DeviceUsed}, load={loadMs}ms");

		var result = engine.Run(image);
		result.LoadMs = loadMs;

		Out($"识别完成: lines={result.Lines.Count}, infer={result.InferMs}ms, device={result.DeviceUsed}");
		Out("---");
		if (result.Lines.Count == 0) {
			Out("(无文本)");
		}
		else {
			for (int i = 0; i < result.Lines.Count; i++) {
				var ln = result.Lines[i];
				var box = string.Join(" ", ln.Box.Select(p => $"{p.X:F0},{p.Y:F0}"));
				Out($"[{i}] score={ln.Score:F3} box=[{box}] {ln.Text}");
			}
			Out("---");
			Out(result.FullText);
		}
		return 0;
	}

	static int listmodels() {
		var packs = ModelCatalog.Scan();
		if (packs.Count == 0) {
			Err("未发现模型包");
			return 1;
		}
		Out("=== 可用模型包 ===");
		foreach (var p in packs) {
			Out($"[{p.Id}] {p.DisplayName}");
			Out($"  目录: {p.Dir}");
			foreach (var v in p.Variants)
				Out($"  - {v.Title}  (det={v.DetFile}, rec={v.RecFile})");
		}
		return 0;
	}

	static int listtts() {
		Out("=== TTS 模型扫描 ===");
		Out($"ModelsRoot={TtsModelScanner.ModelsRoot()}");
		Out($"Exists={Directory.Exists(TtsModelScanner.ModelsRoot())}");
		var list = TtsModelScanner.Scan();
		Out($"Count={list.Count}");
		if (list.Count == 0) {
			Err("未发现 TTS 模型（请放到程序目录 ttsmodels）");
			return 1;
		}
		foreach (var m in list) {
			Out($"  [{m.Type}] {m.DisplayName} lang={m.Lang} gender={m.Gender} vol={m.Volume} speakers={m.Speakers.Count}");
			foreach (var s in m.Speakers.Take(8))
				Out($"      id={s.Id} {s.DisplayName}  [lang={s.Lang} gender={s.Gender}]");
			if (m.Speakers.Count > 8)
				Out($"      ... +{m.Speakers.Count - 8} more");
		}
		return 0;
	}

	/// <summary>列出本进程 SAPI +（x64 时）经 x86 Web 的 32 位发音人。</summary>
	static int listsapi() {
		Out("=== SAPI 本进程语音 ===");
		Out($"ProcessArch={ArchBootstrap.CurrentLabel} Is64BitProcess={Environment.Is64BitProcess}");
		var nLocal = 0;
		try {
			using var sapi = new SapiTts();
			var voices = sapi.Voices;
			nLocal = voices.Count;
			Out($"Count={nLocal}");
			foreach (var v in voices) {
				var cult = v.Culture?.Name ?? "";
				Out($"  [local] {v.Name}  culture={cult} gender={v.Gender} age={v.Age}");
			}
		}
		catch (Exception ex) {
			Err("本进程枚举失败: " + ex.Message);
		}

		if (Environment.Is64BitProcess) {
			Out("=== SAPI x86 Web 语音（按需启动 x86host.exe）===");
			if (!SapiX86Client.ExeAvailable) {
				Out("未找到 x86host.exe，跳过 x86 列表");
			}
			else {
				try {
					var x86 = SapiX86Client.ListVoices();
					Out($"Count={x86.Count}");
					foreach (var v in x86)
						Out($"  [x86] {v.Name}  culture={v.Culture} gender={v.Gender} lang={v.Lang}");
				}
				catch (Exception ex) {
					Err("x86host 枚举失败: " + ex.Message);
				}
			}
		}
		return nLocal > 0 ? 0 : 1;
	}

	static int listasr() {
		Out("=== ASR 模型扫描 ===");
		Out($"ModelsRoot={AsrModelScanner.ModelsRoot()}");
		Out($"Exists={Directory.Exists(AsrModelScanner.ModelsRoot())}");
		var list = AsrModelScanner.Scan();
		Out($"Count={list.Count}");
		if (list.Count == 0) {
			Err("未发现 ASR 模型（请放到程序目录 asrmodels）");
			return 1;
		}
		foreach (var m in list)
			Out($"  [{m.Type}] {m.DisplayName} sr={m.SampleRate} dir={m.ModelDir}");
		return 0;
	}

	/// <summary>列出应用内「安装功能」目录项与安装状态、镜像策略。</summary>
	static int listinstall() {
		Out("=== 安装功能目录 ===");
		Out(FeatureInstaller.MirrorHint());
		Out($"PreferCn={FeatureInstaller.PreferCnMirrors()}");
		Out($"BaseDir={FeatureInstaller.BaseDir}");
		// 样例：GitHub URL 展开顺序
		var sample = FeatureInstaller.ExpandUrls(
			"https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx");
		Out("GitHub URL 展开:");
		foreach (var u in sample)
			Out("  " + u);
		var list = FeatureInstaller.BuildCatalog();
		Out($"Items={list.Count}");
		foreach (var it in list)
			Out($"  [{it.StateText}] {(it.Selected ? "x" : " ")} {it.SizeText,-12} {it.Title}  ({it.Id})");
		return 0;
	}

	/// <summary>列出可下载 TTS 发音人包（GitHub tts-models）。</summary>
	static int listttsinstall() {
		Out("=== 发音人安装目录 (tts-models) ===");
		var log = new Progress<string>(s => Out("  . " + s));
		List<TtsInstallItem> list;
		try {
			list = TtsInstallCatalog.LoadAllAsync(log, CancellationToken.None, forceRefresh: false)
				.GetAwaiter().GetResult();
		}
		catch (Exception ex) {
			Err(ex.Message);
			return 1;
		}
		Out($"Source={TtsInstallCatalog.LastSource}");
		Out($"Count={list.Count}");
		var byLang = list.GroupBy(x => x.Lang).OrderBy(g => g.Key);
		foreach (var g in byLang)
			Out($"  lang[{g.Key}]={g.Count()}");
		var show = list.Where(x => x.Lang is "zh" or "zh,en" or "en" or "multi").Take(30);
		Out("--- sample zh/en ---");
		foreach (var it in show)
			Out($"  [{it.StateText}] {it.SizeText,-12} {it.LangLabel,-14} {it.Engine,-8} {it.Title}");
		return 0;
	}

	/// <summary>命令行 ASR：加载模型识别音频文件，输出文本与耗时。</summary>
	static int runasr(string audioPath, string modelHint, string lang, string device, bool noItn) {
		audioPath = Path.GetFullPath(audioPath);
		if (!File.Exists(audioPath)) {
			Err($"音频不存在: {audioPath}");
			return 1;
		}

		var models = AsrModelScanner.Scan();
		if (models.Count == 0) {
			Err("未发现 ASR 模型（用 --list-asr 查看）");
			return 1;
		}
		AsrModelInfo model = null;
		if (!string.IsNullOrWhiteSpace(modelHint))
			model = models.FirstOrDefault(m => Compat.Contains(m.DisplayName, modelHint, StringComparison.OrdinalIgnoreCase));
		model ??= models[0];

		Out($"模型: [{model.Type}] {model.DisplayName}");
		Out($"音频: {audioPath}");
		Out($"语言: {lang} · ITN: {!noItn} · 设备: {device}");

		var compute = device switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			"cpu" => TtsComputeMode.Cpu,
			_ => TtsComputeMode.Auto,
		};

		var (samples, sr) = AsrAudio.LoadFile(audioPath);
		var audioSec = samples.Length / (double)Math.Max(1, sr);
		Out($"音频: {sr}Hz · {audioSec:0.00}s · {samples.Length} samples");

		using var engine = new AsrEngine();
		engine.Mode = compute;
		var t0 = Environment.TickCount;
		var tLoad = Environment.TickCount;
		engine.LoadModel(model, lang, !noItn);
		var loadMs = Environment.TickCount - tLoad;
		Out($"模型加载: {loadMs}ms · provider={engine.Provider}"
			+ (engine.GpuFallbackReason != null ? $" · 回退: {engine.GpuFallbackReason}" : ""));

		var tRec = Environment.TickCount;
		var text = engine.Recognize(samples, sr);
		var recMs = Environment.TickCount - tRec;
		var totalMs = Environment.TickCount - t0;
		Out($"识别耗时: {recMs}ms · 合计: {totalMs}ms");
		Out("---");
		Out(string.IsNullOrWhiteSpace(text) ? "(无文本)" : text);
		return 0;
	}

	/// <summary>合成短句，按 F0 判定所有发音人男女，默认写回 tts_config.json。</summary>
	static int probeTtsGenderRun(string device, bool writeConfig, string onlyModel) {
		Out("=== TTS 发音人性别探测（F0 音高）===");
		var report = TtsGenderProbe.Run(
			writeConfig: writeConfig,
			device: device,
			onlyModel: onlyModel,
			log: Out);
		Out($"汇总 ok={report.OkCount} fail={report.FailCount} config={report.ConfigPath} wrote={report.WroteConfig}");
		// 按模型统计男女
		foreach (var g in report.Items.Where(i => string.IsNullOrEmpty(i.Error))
			.GroupBy(i => i.Model)) {
			var male = g.Count(x => x.Gender == TtsGender.Male);
			var female = g.Count(x => x.Gender == TtsGender.Female);
			Out($"  {g.Key}: 男={male} 女={female}");
		}
		return report.FailCount > 0 && report.OkCount == 0 ? 1 : 0;
	}

	static int probecuda() {
		Out("=== CUDA / ORT 探测 ===");
		Out($"HasOnnxGpu64Dir={CudaBootstrap.HasOnnxGpu64Dir}");
		Out($"IsGpuReady={CudaBootstrap.IsGpuReady}");
		Out($"GpuStatus={CudaBootstrap.GpuStatus}");
		Out(CudaBootstrap.LastReport);
		if (!CudaBootstrap.IsGpuReady) {
			Err("GPU 不可用，将不会使用 CUDA EP");
			return 1;
		}
		try {
			var opt = buildoptions(null, null, null, "gpu", false);
			Out($"试建 GPU session: pack={opt.ModelPackId} variant={opt.ModelVariant}");
			var t0 = Environment.TickCount;
			using var engine = new OcrEngine(opt);
			Out($"OK model={engine.ModelLabel}, device={engine.DeviceUsed}, load={Environment.TickCount - t0}ms");
			return 0;
		}
		catch (Exception ex) {
			Err($"GPU 失败: {ex.Message}");
			Err(ex.ToString());
			return 1;
		}
	}

	static int listtranslate() {
		Out("=== 翻译 ONNX 模型 ===");
		Out($"ModelsRoot={TranslateModelScanner.ModelsRoot()}");
		Out($"Exists={Directory.Exists(TranslateModelScanner.ModelsRoot())}");
		var list = TranslateModelScanner.Scan();
		if (list.Count == 0) {
			Err("未找到模型");
			return 1;
		}
		foreach (var m in list)
			Out($"  [{(m.IsReady ? "OK" : "--")}] {m.DirKey}  onnx={m.IsOnnx}  {m.ModelDir}");
		return list.Any(m => m.IsReady) ? 0 : 1;
	}

	static int runtranslate(string text, string dirKey, string device) {
		Out("=== 翻译（进程内 ONNX）===");
		dirKey = (dirKey ?? "zh-en").Trim().ToLowerInvariant();
		var prefer = (device ?? "auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" or "nvidia" => "cuda",
			"intel" or "igpu" or "dml" or "directml" => "dml",
			"cpu" => "cpu",
			_ => "auto",
		};
		var models = TranslateModelScanner.Scan();
		var m = models.FirstOrDefault(x => x.IsReady
			&& string.Equals(x.DirKey, dirKey, StringComparison.OrdinalIgnoreCase));
		if (m == null) {
			Err($"缺少就绪模型 {dirKey}。可用:");
			foreach (var x in models) Err("  " + x.DirKey + " ready=" + x.IsReady);
			return 1;
		}
		Out($"model={m.ModelDir}");
		Out($"prefer={prefer}");
		Out($"text={text}");
		using var eng = new TranslateEngine();
		var t0 = Environment.TickCount;
		if (!eng.EnsureLoaded(dirKey, m.ModelDir, prefer)) {
			Err("加载失败: " + eng.LastError);
			return 1;
		}
		Out($"loaded device={eng.LastDevice} backend={eng.LastBackend} loadMs={Environment.TickCount - t0}");
		t0 = Environment.TickCount;
		var outText = eng.Translate(dirKey, text);
		Out($"out={outText}");
		Out($"inferMs={Environment.TickCount - t0} device={eng.LastDevice}");
		return 0;
	}

	static OcrOptions buildoptions(string models, string packId, string variant, string device, bool noCls,
		int? detLimit = null, int? detPad = null, float? boxThresh = null, float? detThresh = null) {
		var dev = device switch {
			"gpu" or "cuda" or "nvidia" => OcrDevice.Gpu,
			"intel" or "intelgpu" or "dml" or "directml" => OcrDevice.IntelGpu,
			_ => OcrDevice.Cpu,
		};

		var opt = new OcrOptions {
			Device = dev,
			UseCls = !noCls,
			ModelPackId = string.IsNullOrWhiteSpace(packId) ? "umi" : packId,
			ModelVariant = variant ?? "",
		};
		if (detLimit.HasValue) opt.DetLimitSideLen = detLimit.Value;
		if (detPad.HasValue) opt.DetPadding = detPad.Value;
		if (boxThresh.HasValue) opt.DetBoxThresh = boxThresh.Value;
		if (detThresh.HasValue) opt.DetThresh = detThresh.Value;

		if (!string.IsNullOrWhiteSpace(models)) {
			var p = Path.GetFullPath(models);
			if (!Directory.Exists(p))
				throw new DirectoryNotFoundException($"模型目录不存在: {p}");
			opt.ModelsDir = p;
			var pack = ModelCatalog.TryLoad(p);
			if (pack != null) {
				opt.ModelPackId = pack.Id;
				var v = pack.FindVariant(variant);
				if (v != null) opt.ModelVariant = v.Title;
			}
			return opt;
		}

		var found = ModelCatalog.Find(opt.ModelPackId);
		if (found == null)
			throw new DirectoryNotFoundException("找不到模型包，请用 --list-models 查看或 --models 指定目录");
		opt.ModelPackId = found.Id;
		opt.ModelsDir = found.Dir;
		var vv = found.FindVariant(variant);
		if (vv != null) opt.ModelVariant = vv.Title;
		return opt;
	}

	/// <summary>
	/// 有声 0.1s / 静音 0.1s 循环播放 → 录屏 N 秒 → 分析音画时长与脉冲周期是否漂移。
	/// </summary>
	static int runtestrecordavsync(string outDir, string regionArg, int seconds) {
		int code = 1;
		Exception threadEx = null;
		var t = new Thread(() => {
			try {
				code = RecordAvSyncTest.Run(outDir, regionArg, seconds, Out);
			}
			catch (Exception ex) {
				threadEx = ex;
				code = 1;
			}
		});
		t.SetApartmentState(ApartmentState.STA);
		t.IsBackground = false;
		t.Start();
		t.Join();
		if (threadEx != null) {
			Err(threadEx.ToString());
			return 1;
		}
		return code;
	}

	/// <summary>
	/// GUI：开录 + RecordHud → SuspendForCapture → CaptureOverlay.Run（约 1.2s 后 ESC）。
	/// 在已有 WPF Application（App.OnStartup → Cli.Run）的 UI 线程上执行，勿新建 Application。
	/// </summary>
	static int runtestoverlayduringrecord(string regionArg, int waitMs) {
		AppConfig.applylogswitch(true);
		CaptureLog.SessionStart("CLI --test-overlay-during-record");
		RecordLog.Begin("CLI-overlay-during-record");
		Out("=== 录屏中弹出截图遮罩测试 --test-overlay-during-record ===");

		System.Drawing.Rectangle region;
		if (!string.IsNullOrWhiteSpace(regionArg)) {
			var parts = regionArg.Split(new[] { ',', 'x', 'X', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			region = new System.Drawing.Rectangle(
				int.Parse(parts[0]), int.Parse(parts[1]),
				int.Parse(parts[2]), int.Parse(parts[3]));
		}
		else {
			var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
			var w = Math.Min(640, s.Width / 2 * 2);
			var h = Math.Min(360, s.Height / 2 * 2);
			region = new System.Drawing.Rectangle(
				s.Left + (s.Width - w) / 2, s.Top + (s.Height - h) / 2, w, h);
		}
		if (region.Width % 2 != 0) region.Width--;
		if (region.Height % 2 != 0) region.Height--;
		Out($"region={region.X},{region.Y} {region.Width}x{region.Height}");

		RecordHud hud = null;
		var exitCode = 1;
		try {
			var ro = new RecordOptions { Fps = 10, Crf = 40, AudioEnabled = false, Codec = "x264" };
			ro.Clamp();
			hud = new RecordHud(region, ro);
			hud.Show();
			// 模拟点「开始」
			var onstart = typeof(RecordHud).GetMethod("onstart",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			onstart?.Invoke(hud, null);
			Out("HUD shown + onstart invoked");
			// 泵几帧让 HUD 布局
			var pump = new System.Windows.Threading.DispatcherFrame();
			var pumpT = new System.Windows.Threading.DispatcherTimer {
				Interval = TimeSpan.FromMilliseconds(Math.Max(300, waitMs))
			};
			pumpT.Tick += (_, __) => { pumpT.Stop(); pump.Continue = false; };
			pumpT.Start();
			System.Windows.Threading.Dispatcher.PushFrame(pump);

			hud.SuspendForCapture();
			Out("SuspendForCapture done, IsVisible=" + hud.IsVisible);

			// 遮罩弹出后用关窗取消（比 SendKeys 可靠：CLI 无焦点时 ESC 丢）
			var esc = new System.Windows.Threading.DispatcherTimer {
				Interval = TimeSpan.FromMilliseconds(1200)
			};
			esc.Tick += (_, __) => {
				esc.Stop();
				try {
					var app = System.Windows.Application.Current;
					var n = 0;
					if (app != null) {
						foreach (Window w in app.Windows) {
							if (w is CaptureOverlay) {
								try { w.Close(); n++; } catch { }
							}
						}
					}
					Out("auto-close CaptureOverlay windows n=" + n);
				}
				catch (Exception ex) { Out("auto-close EX: " + ex.Message); }
			};
			esc.Start();

			var t0 = Environment.TickCount;
			var cap = CaptureOverlay.Run(annotate: false);
			var cost = Environment.TickCount - t0;
			Out($"CaptureOverlay.Run done cost={cost}ms Confirmed={cap.Confirmed} img={CaptureLog.Bmp(cap.Image)}");

			hud.ResumeAfterCapture();
			Out("ResumeAfterCapture done, IsVisible=" + hud.IsVisible);

			if (cost >= 400) {
				Out("=== OK：录屏中 CaptureOverlay 可弹出并正常关闭 ===");
				exitCode = 0;
			}
			else {
				Out("=== FAIL：Overlay 返回过快，可能未真正显示 ===");
				exitCode = 1;
			}
		}
		catch (Exception ex) {
			Err(ex.ToString());
			exitCode = 1;
		}
		finally {
			try { hud?.Close(); } catch { }
			try { RecordLog.End(exitCode == 0 ? "ok" : "fail"); } catch { }
		}
		return exitCode;
	}

	/// <summary>
	/// 模拟「录屏过程中仍可截图识别」：开录 → 用 CaptureOverlay 同款多屏冻结抓图 → 停录。
	/// 验证录制中 DXGI/GDI 冻结链路可用（不测 UI 交互）。
	/// </summary>
	static int runtestcaptureduringrecord(string outDir, string regionArg, int waitMs) {
		int code = 1;
		Exception threadEx = null;
		var t = new Thread(() => {
			try { code = runtestcaptureduringrecordCore(outDir, regionArg, waitMs); }
			catch (Exception ex) { threadEx = ex; code = 1; }
		});
		t.SetApartmentState(ApartmentState.STA);
		t.Start();
		t.Join();
		if (threadEx != null) {
			Err(threadEx.ToString());
			return 1;
		}
		return code;
	}

	static int runtestcaptureduringrecordCore(string outDir, string regionArg, int waitMs) {
		AppConfig.applylogswitch(true);
		CaptureLog.SessionStart("CLI --test-capture-during-record");
		RecordLog.Begin("CLI-capture-during-record");
		if (string.IsNullOrWhiteSpace(outDir))
			outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "capture_during_record");
		outDir = Path.GetFullPath(outDir);
		Directory.CreateDirectory(outDir);

		Out("=== 录屏中截图识别管线测试 --test-capture-during-record ===");
		Out(ScreenDpi.BuildReport());
		Out($"输出: {outDir}");

		System.Drawing.Rectangle region;
		if (!string.IsNullOrWhiteSpace(regionArg)) {
			var parts = regionArg.Split(new[] { ',', 'x', 'X', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 4) {
				Err("--region 格式: L,T,W,H");
				return 2;
			}
			region = new System.Drawing.Rectangle(
				int.Parse(parts[0]), int.Parse(parts[1]),
				int.Parse(parts[2]), int.Parse(parts[3]));
		}
		else {
			var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
			var w = Math.Min(800, s.Width / 2 * 2);
			var h = Math.Min(450, s.Height / 2 * 2);
			region = new System.Drawing.Rectangle(
				s.Left + (s.Width - w) / 2, s.Top + (s.Height - h) / 2, w, h);
		}
		if (region.Width % 2 != 0) region.Width--;
		if (region.Height % 2 != 0) region.Height--;
		Out($"record region={region.X},{region.Y} {region.Width}x{region.Height}");

		ScreenRecorder rec = null;
		string videoPath = null;
		var bad = 0;
		try {
			var opt = new RecordOptions { Fps = 10, Crf = 40, AudioEnabled = false, Codec = "x264" };
			opt.Clamp();
			rec = new ScreenRecorder(region, RecordAudioMode.Off, opt);
			videoPath = rec.TempPath;
			rec.Start();
			Out("recorder started: " + rec.Backend);
			var t0 = Environment.TickCount;
			while (Environment.TickCount - t0 < Math.Max(300, waitMs))
				Thread.Sleep(40);

			// 与 CaptureOverlay.Run 相同：并行冻结各显示器
			Out("--- freeze all monitors while recording (CaptureOverlay path) ---");
			var screens = System.Windows.Forms.Screen.AllScreens
				.Where(s => s.Bounds.Width > 0 && s.Bounds.Height > 0)
				.ToArray();
			var freezes = new System.Windows.Media.Imaging.BitmapSource[screens.Length];
			var tCap = Environment.TickCount;
			System.Threading.Tasks.Parallel.For(0, screens.Length, i => {
				try {
					freezes[i] = CaptureOverlay.CaptureMonitor(screens[i], out _, out _);
				}
				catch (Exception ex) {
					Out($"  mon#{i + 1} EX: {ex.Message}");
				}
			});
			Out($"parallel freeze cost={Environment.TickCount - tCap}ms screens={screens.Length}");

			for (var i = 0; i < screens.Length; i++) {
				var bmp = freezes[i];
				if (bmp == null) {
					Out($"  mon#{i + 1} FAIL null");
					bad++;
					continue;
				}
				var path = Path.Combine(outDir, $"freeze_mon{i + 1}_{bmp.PixelWidth}x{bmp.PixelHeight}.png");
				ImageUtil.Savefile(bmp, path);
				var nb = sampleNonBlack(bmp);
				Out($"  mon#{i + 1} {bmp.PixelWidth}x{bmp.PixelHeight} nonBlack={nb:P1} -> {path}");
				if (nb < 0.05) bad++;
			}

			// 主路径：录制区域仍应可抓
			var still = ScreenRecorder.CaptureRegion(region);
			if (still == null) {
				Out("CaptureRegion FAIL");
				bad++;
			}
			else {
				var p = Path.Combine(outDir, $"region_{still.PixelWidth}x{still.PixelHeight}.png");
				ImageUtil.Savefile(still, p);
				Out($"region still nonBlack={sampleNonBlack(still):P1} -> {p}");
			}
		}
		finally {
			try { rec?.Dispose(); } catch { }
			try {
				if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
					File.Delete(videoPath);
			}
			catch { }
		}

		Out(bad == 0
			? "=== OK：录屏中截图冻结管线可用；GUI 侧录屏时截图识别/标注应挂起 HUD 后走 CaptureOverlay ==="
			: $"=== FAIL bad={bad} ===");
		RecordLog.End(bad == 0 ? "ok" : "fail");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>
	/// 模拟「录屏过程中截图」：开录 → CaptureStill → 存盘/剪贴板 → 停录。
	/// 用于无 GUI 自测抓图链路。
	/// </summary>
	static int runrecordsnap(string outDir, string regionArg, int waitMs) {
		// 剪贴板需 STA
		int code = 1;
		Exception threadEx = null;
		var t = new Thread(() => {
			try {
				code = runrecordsnapCore(outDir, regionArg, waitMs);
			}
			catch (Exception ex) {
				threadEx = ex;
				code = 1;
			}
		});
		t.SetApartmentState(ApartmentState.STA);
		t.IsBackground = false;
		t.Start();
		t.Join();
		if (threadEx != null) {
			Err(threadEx.ToString());
			return 1;
		}
		return code;
	}

	static int runrecordsnapCore(string outDir, string regionArg, int waitMs) {
		AppConfig.applylogswitch(true);
		CaptureLog.SessionStart("CLI --record-snap");
		RecordLog.Begin("CLI-record-snap");
		if (string.IsNullOrWhiteSpace(outDir))
			outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "record_snap");
		outDir = Path.GetFullPath(outDir);
		Directory.CreateDirectory(outDir);

		Out("=== 录屏中截图测试 --record-snap ===");
		Out(ScreenDpi.BuildReport());
		Out($"输出目录: {outDir}");
		Out($"waitMs={waitMs}");

		System.Drawing.Rectangle region;
		if (!string.IsNullOrWhiteSpace(regionArg)) {
			// L,T,W,H
			var parts = regionArg.Split(new[] { ',', 'x', 'X', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 4) {
				Err("--region 格式: L,T,W,H（物理像素）");
				return 2;
			}
			region = new System.Drawing.Rectangle(
				int.Parse(parts[0]), int.Parse(parts[1]),
				int.Parse(parts[2]), int.Parse(parts[3]));
		}
		else {
			// 主屏中心 640x360
			var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
			var w = Math.Min(640, s.Width / 2 * 2);
			var h = Math.Min(360, s.Height / 2 * 2);
			region = new System.Drawing.Rectangle(
				s.Left + (s.Width - w) / 2,
				s.Top + (s.Height - h) / 2,
				w, h);
		}
		if (region.Width % 2 != 0) region.Width--;
		if (region.Height % 2 != 0) region.Height--;
		Out($"region={region.X},{region.Y} {region.Width}x{region.Height}");

		var pathStill = Path.Combine(outDir, $"while_rec_{DateTime.Now:HHmmss}.png");
		var pathPlain = Path.Combine(outDir, $"plain_{DateTime.Now:HHmmss}.png");

		Out("--- A) 未开录 CaptureRegion ---");
		var plain = RecordSnap.Capture(region, pathPlain);
		Out($"  ok={plain.Ok} {plain.Width}x{plain.Height} nonBlack={plain.NonBlack:P1} clip={plain.ClipboardOk}");
		Out($"  path={plain.Path}");
		if (!string.IsNullOrEmpty(plain.Error)) Out($"  err={plain.Error}");

		Out("--- B) 录制中 CaptureStill（与 HUD 相同）---");
		var recSnap = RecordSnap.CaptureWhileRecording(region, pathStill, waitMs, Out);
		Out($"  ok={recSnap.Ok} {recSnap.Width}x{recSnap.Height} nonBlack={recSnap.NonBlack:P1} clip={recSnap.ClipboardOk}");
		Out($"  path={recSnap.Path}");
		if (!string.IsNullOrEmpty(recSnap.Error)) Out($"  err={recSnap.Error}");

		var bad = 0;
		if (!plain.Ok || plain.NonBlack < 0.05) bad++;
		if (!recSnap.Ok || recSnap.NonBlack < 0.05) bad++;

		Out(bad == 0
			? "=== record-snap 完成 OK（抓图链路正常，若 GUI 仍不能截图则是 HUD 点击/命中问题）==="
			: $"=== record-snap 完成，失败项={bad} ===");
		Out($"RecordLog={RecordLog.LogPath}");
		Out($"cli_last.log + log/capture.log");
		RecordLog.End(bad == 0 ? "ok" : "fail");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>命令行截各显示器全屏，保存 PNG 并写诊断（不弹 UI）。</summary>
	static int runsnap(string outDir) {
		CaptureLog.SessionStart("CLI --snap");
		if (string.IsNullOrWhiteSpace(outDir))
			outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "snap");
		outDir = Path.GetFullPath(outDir);
		Directory.CreateDirectory(outDir);

		Out("=== 截屏测试 --snap ===");
		Out(ScreenDpi.BuildReport());
		Out($"输出目录: {outDir}");

		var screens = System.Windows.Forms.Screen.AllScreens;
		var i = 0;
		var bad = 0;
		foreach (var s in screens) {
			i++;
			var b = s.Bounds;
			Out($"--- Screen#{i} {(s.Primary ? "Primary" : "Sec")} {s.DeviceName} Bounds={b} ---");
			try {
				var bmp = CaptureOverlay.CaptureMonitor(s, out var pw, out var ph);
				var path = Path.Combine(outDir, $"mon{i}_{(s.Primary ? "pri" : "sec")}_{pw}x{ph}.png");
				ImageUtil.Savefile(bmp, path);
				// 采样非黑
				var nb = sampleNonBlack(bmp);
				Out($"  capture: {pw}x{ph} nonBlack~{nb:P1} -> {path}");
				CaptureLog.Info($"CLI snap mon#{i} {pw}x{ph} nonBlack~{nb:P1} {path}");
				if (nb < 0.15) {
					Out($"  WARN: nonBlack 过低，可能仍是半屏黑图");
					bad++;
				}
				// 再按四象限采样，便于看是否挤在左上
				var q = sampleQuads(bmp);
				Out($"  quads nonBlack L-top={q[0]:P0} R-top={q[1]:P0} L-bot={q[2]:P0} R-bot={q[3]:P0}");
			}
			catch (Exception ex) {
				Err($"  FAIL: {ex.Message}");
				CaptureLog.Ex($"CLI snap mon#{i}", ex);
				bad++;
			}
		}

		// 虚拟全屏
		try {
			var vs = CaptureOverlay.CaptureVirtualScreen(out var vw, out var vh);
			var path = Path.Combine(outDir, $"virtual_{vw}x{vh}.png");
			ImageUtil.Savefile(vs, path);
			Out($"VirtualScreen: {vw}x{vh} nonBlack~{sampleNonBlack(vs):P1} -> {path}");
		}
		catch (Exception ex) {
			Err("VirtualScreen FAIL: " + ex.Message);
			bad++;
		}

		Out(bad == 0 ? "=== snap 完成 OK ===" : $"=== snap 完成，警告/失败 {bad} ===");
		Out($"详见 log/capture.log");
		return bad == 0 ? 0 : 1;
	}

	static double sampleNonBlack(System.Windows.Media.Imaging.BitmapSource src) {
		if (src == null) return 0;
		var w = src.PixelWidth;
		var h = src.PixelHeight;
		if (w < 1 || h < 1) return 0;
		var stride = w * 4;
		var px = new byte[stride * h];
		var bgra = src;
		if (src.Format != System.Windows.Media.PixelFormats.Bgra32)
			bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
		bgra.CopyPixels(px, stride, 0);
		long nb = 0, n = 0;
		var step = Math.Max(4, (w * h / 3000) * 4);
		if (step % 4 != 0) step = (step / 4) * 4;
		for (int i = 0; i + 3 < px.Length; i += step) {
			n++;
			if (px[i] > 12 || px[i + 1] > 12 || px[i + 2] > 12) nb++;
		}
		return n > 0 ? nb / (double)n : 0;
	}

	/// <summary>四象限非黑比例：LT RT LB RB</summary>
	static double[] sampleQuads(System.Windows.Media.Imaging.BitmapSource src) {
		var r = new double[4];
		if (src == null) return r;
		var w = src.PixelWidth;
		var h = src.PixelHeight;
		var stride = w * 4;
		var px = new byte[stride * h];
		var bgra = src;
		if (src.Format != System.Windows.Media.PixelFormats.Bgra32)
			bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
		bgra.CopyPixels(px, stride, 0);
		var midX = w / 2;
		var midY = h / 2;
		long[] nb = new long[4], n = new long[4];
		var step = Math.Max(1, Math.Min(w, h) / 80);
		for (int y = 0; y < h; y += step) {
			for (int x = 0; x < w; x += step) {
				var i = y * stride + x * 4;
				var lit = px[i] > 12 || px[i + 1] > 12 || px[i + 2] > 12;
				var q = (y < midY ? 0 : 2) + (x < midX ? 0 : 1);
				n[q]++;
				if (lit) nb[q]++;
			}
		}
		for (int q = 0; q < 4; q++)
			r[q] = n[q] > 0 ? nb[q] / (double)n[q] : 0;
		return r;
	}

	/// <summary>开录 GIF 数秒 → 保存 → 校验文件头。</summary>
	static int runtestgifrecord(string outDir, string regionArg, int seconds) {
		int code = 1;
		Exception threadEx = null;
		var t = new Thread(() => {
			try { code = runtestgifrecordCore(outDir, regionArg, seconds); }
			catch (Exception ex) { threadEx = ex; code = 1; }
		});
		t.SetApartmentState(ApartmentState.STA);
		t.Start();
		t.Join();
		if (threadEx != null) {
			Err(threadEx.ToString());
			return 1;
		}
		return code;
	}

	static int runtestgifrecordCore(string outDir, string regionArg, int seconds) {
		AppConfig.applylogswitch(true);
		CaptureLog.SessionStart("CLI --test-gif-record");
		RecordLog.Begin("CLI-gif-record");
		if (string.IsNullOrWhiteSpace(outDir))
			outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "gif_record");
		outDir = Path.GetFullPath(outDir);
		Directory.CreateDirectory(outDir);

		Out("=== GIF 录屏测试 --test-gif-record ===");
		seconds = Compat.Clamp(seconds, 1, 30);
		Out($"秒数={seconds}");

		System.Drawing.Rectangle region;
		if (!string.IsNullOrWhiteSpace(regionArg)) {
			var parts = regionArg.Split(new[] { ',', 'x', 'X', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 4) {
				Err("--region 格式: L,T,W,H");
				return 2;
			}
			region = new System.Drawing.Rectangle(
				int.Parse(parts[0]), int.Parse(parts[1]),
				int.Parse(parts[2]), int.Parse(parts[3]));
		}
		else {
			var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
			var w = Math.Min(640, s.Width);
			var h = Math.Min(360, s.Height);
			region = new System.Drawing.Rectangle(
				s.Left + (s.Width - w) / 2, s.Top + (s.Height - h) / 2, w, h);
		}
		Out($"region={region.X},{region.Y} {region.Width}x{region.Height}");

		var opt = new GifOptions {
			Fps = 8,
			MaxSizeEnabled = true,
			MaxWidth = 640,
			MaxHeight = 360,
		};
		opt.Clamp();
		GifScreenRecorder rec = null;
		try {
			rec = new GifScreenRecorder(region, opt);
			Out("Start… backend pending");
			rec.Start();
			Out("backend=" + (rec.Backend ?? ""));
			Thread.Sleep(seconds * 1000);
			Out($"elapsed={rec.Elapsed} Stop…");
			rec.Stop();
			rec.WaitFinalize();
			var src = rec.VideoPath;
			if (string.IsNullOrEmpty(src) || !File.Exists(src)) {
				Err("临时视频不存在");
				return 1;
			}
			Out($"video={src} bytes={new FileInfo(src).Length} captureFps={rec.Fps}");
			var dest = Path.Combine(outDir, $"gif_test_{DateTime.Now:yyyyMMdd_HHmmss}.gif");
			GifOptions.SizeByScale(rec.SrcWidth, rec.SrcHeight, 100, out var ow, out var oh);
			if (opt.MaxSizeEnabled)
				opt.FitSize(rec.SrcWidth, rec.SrcHeight, out ow, out oh);
			Out($"encode GIF {ow}x{oh} outFps=8 colors=128 (src={rec.Fps})…");
			FfmpegGifEncode.FromVideo(src, dest, ow, oh, 8, 128, rec.Fps);
			var len = new FileInfo(dest).Length;
			Out($"saved={dest} bytes={len}");
			// GIF89a / GIF87a
			var hdr = new byte[6];
			using (var fs = File.OpenRead(dest))
				if (fs.Read(hdr, 0, 6) < 6) {
					Err("GIF 头过短");
					return 1;
				}
			var sig = Encoding.ASCII.GetString(hdr);
			Out("header=" + sig);
			if (sig != "GIF89a" && sig != "GIF87a") {
				Err("不是有效 GIF 头: " + sig);
				return 1;
			}
			if (len < 64) {
				Err("GIF 过小");
				return 1;
			}
			Out("=== OK：GIF 录屏可用 ===");
			return 0;
		}
		finally {
			try { rec?.DiscardTemps(); } catch { }
			try { rec?.Dispose(); } catch { }
			RecordLog.End("cli-gif");
		}
	}

	static void printhelp() {
		Out("""
WpfOCR CLI — Umi-OCR / Rapid PP-OCR + onnxgpu64

用法:
  WpfOCR --image <路径> [选项]
  WpfOCR --snap [--out <目录>]
  WpfOCR --record-snap [--region L,T,W,H] [--wait-ms 800] [--out <目录>]
  WpfOCR --test-capture-during-record [--region L,T,W,H] [--out <目录>]
  WpfOCR --test-overlay-during-record [--region L,T,W,H]
  WpfOCR --test-record-avsync [--seconds 10] [--region L,T,W,H] [--out <目录>]
  WpfOCR --test-gif-record [--seconds 2] [--region L,T,W,H] [--out <目录>]
  WpfOCR --test-record-codec [av1|x264|x265] [--seconds 2] [--repeat 2] [--region L,T,W,H] [--out <目录>]
  WpfOCR --list-models
  WpfOCR --list-tts
  WpfOCR --list-sapi
  WpfOCR --list-asr
  WpfOCR --list-install
  WpfOCR --list-tts-install
  WpfOCR --asr <音频> [--asr-model 名] [--asr-lang auto|zh|en|ja|ko|yue] [--asr-device auto|gpu|cpu|igpu] [--asr-no-itn]
  WpfOCR --probe-tts-gender [-d auto|gpu|cpu] [--only-model 名] [--no-write-config]
  WpfOCR --probe-cuda
  WpfOCR --list-translate
  WpfOCR --translate "文本" [--tr-dir zh-en|en-zh] [-d auto|gpu|cpu|igpu]
  WpfOCR --translate-file <utf8文本文件> [--tr-dir zh-en|en-zh] [-d auto|gpu|cpu|igpu]
  WpfOCR --apply-update <压缩包> --target <安装目录> [--wait-pid PID] [--restart]
  WpfOCR --list-sapi           本进程 SAPI +（x64 时）x86host Web 发音人
  x86host.exe                  独立 32 位 SAPI Web（空闲 60s；主程序按需拉起）
  WpfOCR --help

参数:
  -i, --image     待识别图片路径
  -d, --device    auto(默认) | gpu | cpu | igpu
  -p, --pack      模型包 Id（umi / rapid-ch，默认 umi）
  -v, --variant   语言/变体标题（configs.txt 中的名称）
  -m, --models    直接指定模型目录（覆盖 --pack）
      --no-cls    跳过方向分类
      --det-limit  检测边长上限（默认 1024）
      --det-pad    检测前 padding（默认 50）
      --det-thresh 二值化阈值（默认 0.3）
      --box-thresh 框置信阈值（默认 0.5）
      --snap       截各显示器全屏到 log/snap/（诊断多屏/DPI）
      --record-snap  开录→CaptureStill→存盘（诊断抓图）
      --test-capture-during-record  开录中走 CaptureOverlay 同款多屏冻结
      --test-overlay-during-record  开录+HUD 挂起后弹出截图遮罩（自动 ESC）
      --test-record-avsync  有声0.1s/静音0.1s循环→录N秒→分析音画同步
      --test-gif-record  录制低帧率无声 GIF 数秒并校验文件头
      --test-record-codec  用 ScreenRecorder 短录并探测视频 codec（默认 av1）
      --repeat    --test-record-codec 连续次数（默认 1）
      --seconds   --test-record-avsync / --test-gif-record / --test-record-codec 录制秒数
      --region    物理像素区域 L,T,W,H（默认主屏中心）
      --wait-ms   开录后等待毫秒再截（默认 800）
  -o, --out       --snap / --record-snap / --test-*-record / --test-gif-record 输出目录
      --list-models 列出可用 OCR 模型包与变体
      --list-tts    列出 TTS（Sherpa）模型
      --list-sapi   列出 SAPI（x64 会按需启动 x86host.exe Web 合并 32 位音）
      --list-asr    列出 ASR（Sherpa）语音识别模型
      --asr         识别音频文件（wav/mp3/flac 等），输出文本
      --asr-model   指定 ASR 模型名（模糊匹配，默认第一个）
      --asr-lang    识别语言（auto/zh/en/ja/ko/yue，默认 auto）
      --asr-device  ASR 计算设备（auto/gpu/cpu/igpu，默认 auto）
      --asr-no-itn  禁用逆文本归一化
      --probe-tts-gender  为全部发音人合成短句，按 F0 判定男女并写 tts_config
      --only-model  仅探测指定模型目录名（配合 --probe-tts-gender）
      --no-write-config  只打印结果，不写 tts_config.json
      --probe-cuda  仅探测 GPU 会话是否可用
      --list-translate  列出 Opus-MT ONNX 翻译模型
      --translate   进程内 ONNX 翻译（无需 Python）
      --tr-dir      翻译方向 zh-en / en-zh（默认 zh-en）
      --apply-update  解压更新包并覆盖 --target 目录（自更新用）
      --target      安装目录（配合 --apply-update）
      --wait-pid    等待指定进程退出后再覆盖
      --restart     覆盖后启动 WpfOCR.exe

示例:
  WpfOCR -i test.png -d gpu
  WpfOCR -i a.jpg -p rapid-ch -d cpu
  WpfOCR --snap
  WpfOCR --snap -o D:\tmp\shots
  WpfOCR --record-snap
  WpfOCR --test-capture-during-record
  WpfOCR --test-record-avsync
  WpfOCR --test-record-avsync --seconds 10 -o log\record_avsync
  WpfOCR --test-gif-record --seconds 2 -o log\gif_record
  WpfOCR --test-record-codec av1 --repeat 2 --seconds 2 -o log\record_codec
  WpfOCR --record-snap --region 100,100,800,600 -o log\record_snap
  WpfOCR --list-models
  WpfOCR --list-tts
  WpfOCR --list-asr
  WpfOCR --asr test.wav
  WpfOCR --asr test.wav --asr-model sense-voice --asr-lang zh --asr-device cpu
  WpfOCR --probe-tts-gender -d gpu
  WpfOCR --probe-tts-gender --only-model vits-zh-aishell3 -d cpu
""");
	}

	static void ensureconsole() {
		if (!AttachConsole(ATTACH_PARENT_PROCESS))
			AllocConsole();
		// 重新绑定 stdout/stderr：用 GetStdHandle 获取控制台句柄，避免 .NET 缓存旧流
		try {
			var hOut = GetStdHandle(STD_OUTPUT_HANDLE);
			var fsOut = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(hOut, false), FileAccess.Write);
			Console.SetOut(new StreamWriter(fsOut, new UTF8Encoding(false)) { AutoFlush = true });
			var hErr = GetStdHandle(STD_ERROR_HANDLE);
			var fsErr = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(hErr, false), FileAccess.Write);
			Console.SetError(new StreamWriter(fsErr, new UTF8Encoding(false)) { AutoFlush = true });
		}
		catch { }
	}

	static void Out(string s) {
		try { Console.WriteLine(s); } catch { }
		try { log?.WriteLine(s); } catch { }
	}

	static void Err(string s) {
		try { Console.Error.WriteLine(s); } catch { }
		try { log?.WriteLine("[ERR] " + s); } catch { }
	}
}
