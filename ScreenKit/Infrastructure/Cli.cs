using System.Reflection;
using System.Windows.Threading;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ScreenKit;

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

	[DllImport("user32.dll")]
	static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

	[DllImport("user32.dll")]
	static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

	[DllImport("kernel32.dll")]
	static extern uint GetCurrentThreadId();

	public static bool IsCli(string[] args) {
		if (args == null || args.Length == 0) return false;
		foreach (var a in args) {
			if (a is "--image" or "-i" or "--probe-cuda" or "--list-models" or "--list-tts"
				or "--list-sapi"
				or "--probe-tts-gender" or "--snap" or "--snap-all" or "--record-snap"
				or "--test-tts-sherpa" or "--test-edge-tts" or "--list-edge-tts"
				or "--test-capture-during-record" or "--test-overlay-during-record"
				or "--test-overlay-layout" or "--test-overlay-span-adj"
				or "--test-record-avsync" or "--test-gif-record" or "--test-record-codec"
				or "--test-record-cursor"
				or "--test-clipboard-path"
				or "--test-sendfile"
				or "--test-lang"
				or "--test-apk-qr"
				or "--test-img-convert" or "--test-qr-make" or "--test-rename"
				or "--test-hash" or "--test-texttool" or "--test-pwgen" or "--test-nettool"
				or "--test-update-notes"
				or "--test-zhconv" or "--test-wincal" or "--test-jpyomi"
				or "--test-wintop"
				or "--test-win-tts"
				or "--test-mem"
				or "--test-ort-lazy"
				or "--test-ort-release"
				or "--test-dict-search" or "--test-dict-sel" or "--test-dict-word" or "--test-dict-tts"
				or "--test-dict-host" or "--test-dict-tr"
				or "--test-cast" or "--test-cast-recv" or "--test-aoa"
				or "--test-llm-continue"
				or "--test-llm-chat"
				or "--test-llm-agent"
				or "--test-llm-log"
				or "--test-http-tts"
				or "--test-http-chat"
				or "--test-face-overlay"
				or "--help" or "-h" or "/?"
				or "--toast"
				or "--zhconv" or "--calendar" or "--jpyomi"
				or "--asr" or "--list-asr"
				or "--translate" or "--translate-file" or "--list-translate"
				or "--list-install" or "--list-tts-install"
				or "--list-face"
				or "--apply-update" or "--self-update")
				return true;
		}
		return false;
	}

	public static int Run(string[] args) {
		// 自更新：不初始化 CUDA（由 App 入口优先处理；此处兜底）
		if (AppUpdater.IsApplyUpdateArgs(args))
			return AppUpdater.RunApplyUpdate(args);

		try {
			var o = new OcrOptions();
			AppConfig.LoadInto(o);
			HttpProxy.ApplyFrom(o);
		}
		catch { }
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
		bool doTestRecordCursor = false;
		bool doTestClipboardPath = false;
		string testRecordCodec = "av1";
		int testRecordRepeat = 1;
		string recordRegion = null; // L,T,W,H 物理像素
		int recordSnapWaitMs = 800;
		int recordAvsyncSec = 10;
		bool noCls = false;
		bool probeTtsGender = false;
		bool doTestTtsSherpa = false;
		bool doTestEdgeTts = false;
		string testTtsModel = null;
		string testEdgeVoice = "zh-CN-XiaoxiaoNeural";
		string testTtsText = "안녕하세요. 한국어 음성 합성 테스트입니다.";
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
					case "--test-overlay-layout":
						return runtestoverlaylayout();
					case "--test-overlay-span-adj":
						return runtestoverlayspanadj();
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
					case "--test-record-cursor":
						doTestRecordCursor = true;
						break;
					case "--test-clipboard-path":
						doTestClipboardPath = true;
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
				case "--list-edge-tts":
					return listedgettvoices();
				case "--list-asr":
					return listasr();
				case "--list-face":
					return listface();
				case "--test-face-overlay":
					return testfaceoverlay();
				case "--test-llm-continue":
					return runtestllmcontinue();
				case "--test-llm-log":
					return testllmlog();
				case "--test-llm-chat":
					return runtestllmchat();
				case "--test-llm-agent":
					return runtestllmagent();
				case "--test-http-tts":
					return runtesthttptts();
				case "--test-http-chat":
					return runtesthttpchat();
				case "--test-sendfile":
					return testsendfile();
				case "--test-lang":
					return testlang();
				case "--test-apk-qr":
					return testapkqr();
				case "--test-img-convert":
					return testimgconvert();
				case "--test-qr-make":
					return testqrmake();
				case "--test-rename":
					return testrename();
				case "--test-hash":
					return testhash();
				case "--test-texttool":
					return testtexttool();
				case "--test-pwgen":
					return testpwgen();
				case "--test-nettool":
					return testnettool();
				case "--test-update-notes":
					return testupdatenotes();
				case "--test-zhconv":
					return testzhconv();
				case "--test-wincal":
					return testwincal();
				case "--test-jpyomi":
					return testjpyomi();
				case "--test-wintop":
					return testwintop();
				case "--test-win-tts":
					return testwintts();
				case "--test-mem":
					return testmem();
				case "--test-ort-lazy":
					return testortlazy();
				case "--test-ort-release":
					return testortrelease();
				case "--test-dict-search":
					if (i + 1 < args.Length && args[i + 1].Length > 0 && args[i + 1][0] != '-')
						return testdict(Next());
					return testdict(null);
				case "--test-dict-sel":
					return testselcopy();
				case "--test-dict-word":
					return testdictword();
				case "--test-dict-host":
					return testdicthost();
				case "--test-dict-tr":
					return testdicttr();
				case "--test-dict-tts":
					return DictTts.TestCache();
				case "--test-cast":
					return testcast();
				case "--test-cast-recv":
					return testcastrecv();
				case "--test-aoa":
					return testaoa();
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
					case "--test-tts-sherpa":
						doTestTtsSherpa = true;
						testTtsModel = Next();
						break;
					case "--test-edge-tts":
						doTestEdgeTts = true;
						if (i + 1 < args.Length && args[i + 1].Length > 0 && args[i + 1][0] != '-')
							testEdgeVoice = Next();
						break;
					case "--tts-text":
						testTtsText = Next();
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
					case "--toast":
						return runtoast(args, i);
					case "--zhconv":
						return runzhconv(args, i);
					case "--calendar":
						return runcalendar(args, i);
					case "--jpyomi":
						return runjpyomi(args, i);
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

		if (doTestRecordCursor) {
			try {
				return RecordCursorTest.Run(snapOut, Out);
			}
			catch (Exception ex) {
				Err($"录屏鼠标叠加测试失败: {ex.Message}");
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

		if (doTestClipboardPath) {
			try {
				return runtestclipboardpath();
			}
			catch (Exception ex) {
				Err($"剪贴板路径测试失败: {ex.Message}");
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

		if (doTestTtsSherpa) {
			try {
				return runtestttssherpa(testTtsModel, testTtsText, device);
			}
			catch (Exception ex) {
				Err($"Sherpa TTS 测试失败: {ex.Message}");
				Err(ex.ToString());
				return 1;
			}
		}

		if (doTestEdgeTts) {
			try {
				return runtestedgetts(testEdgeVoice, testTtsText);
			}
			catch (Exception ex) {
				Err($"Edge 在线 TTS 测试失败: {ex.Message}");
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

		OcrResult result;
		if (WinOcr.Is(opt)) {
			Out($"Windows OCR 语言: {opt.WinOcrLangs}");
			var t0 = Environment.TickCount;
			result = WinOcr.RecognizeFile(opt, image);
			result.LoadMs = 0;
			Out($"会话就绪: model={result.ModelLabel}, device={result.DeviceUsed}, load={Environment.TickCount - t0}ms");
		}
		else {
			var tLoad0 = Environment.TickCount;
			using var engine = new OcrEngine(opt);
			var loadMs = Environment.TickCount - tLoad0;
			Out($"会话就绪: model={engine.ModelLabel}, device={engine.DeviceUsed}, load={loadMs}ms");
			result = engine.Run(image);
			result.LoadMs = loadMs;
		}

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
		try {
			var opt = new OcrOptions();
			AppConfig.LoadInto(opt);
			Loc.SetFromConfig(opt.UiLang);
		}
		catch { }
		var packs = ModelCatalog.Scan();
		Out("=== 可用模型包 ===");
		if (packs.Count == 0)
			Out("(无 ONNX 模型包)");
		foreach (var p in packs) {
			Out($"[{p.Id}] {p.DisplayName}");
			Out($"  目录: {p.Dir}");
			foreach (var v in p.Variants)
				Out($"  - {v.DisplayName}  (det={v.DetFile}, rec={v.RecFile})");
		}
		var win = WinOcr.Pack();
		Out($"[{win.Id}] {win.DisplayName}");
		var langs = WinOcr.Languages();
		if (langs.Count == 0)
			Out("  (本机无 Windows OCR 语言)");
		else {
			foreach (var lang in langs)
				Out($"  - {lang.Tag}  {lang.Name}");
		}
		if (packs.Count == 0 && langs.Count == 0) {
			Err("未发现模型包");
			return 1;
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

	static int runtestttssherpa(string modelName, string text, string device) {
		var models = TtsModelScanner.Scan();
		var model = models.FirstOrDefault(m =>
			string.Equals(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase))
			?? models.FirstOrDefault(m => Compat.Contains(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
		if (model == null) throw new ArgumentException("未找到 TTS 模型: " + modelName);
		var mode = device switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
		Out($"模型: {model.DisplayName} · lang={model.Lang} · request={mode}");
		using var engine = new TtsEngine { Mode = mode };
		engine.LoadModel(model);
		var (samples, sampleRate) = engine.Synthesize(text, 0, 1f, false);
		if (samples == null || samples.Length == 0 || sampleRate <= 0)
			throw new InvalidOperationException("合成结果为空");
		Out($"OK provider={engine.Provider} sampleRate={sampleRate} samples={samples.Length} "
			+ $"seconds={samples.Length / (double)sampleRate:F2}");
		if (!string.IsNullOrEmpty(engine.GpuFallbackReason))
			Out("Fallback: " + engine.GpuFallbackReason);
		var before = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64;
		engine.UnloadSafe();
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		CudaBootstrap.ReleaseNow();
		var after = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64;
		Out($"mem heavy={CudaBootstrap.HeavyMapped()} priv={before} -> {after}");
		if (CudaBootstrap.HeavyMapped()) {
			Err("FAIL: CUDA 库仍映射");
			return 2;
		}
		return 0;
	}

	static int listedgettvoices() {
		using var edge = new EdgeOnlineTts();
		var voices = edge.LoadVoicesAsync(CancellationToken.None).GetAwaiter().GetResult();
		Out($"=== Edge 在线语音 · Count={voices.Count} ===");
		foreach (var voice in voices)
			Out($"{voice.Key} · {voice.Culture} · {voice.Gender}");
		return voices.Count > 0 ? 0 : 1;
	}

	static int runtestedgetts(string voice, string text) {
		using var edge = new EdgeOnlineTts();
		var voices = edge.LoadVoicesAsync(CancellationToken.None).GetAwaiter().GetResult();
		if (!edge.SelectVoice(voice))
			throw new ArgumentException($"未找到 Edge 在线语音: {voice}（目录共 {voices.Count} 个）");
		edge.SetRateVolume(1, 100);
		var (samples, sampleRate) = edge.Synthesize(text).GetAwaiter().GetResult();
		if (samples == null || samples.Length == 0 || sampleRate <= 0)
			throw new InvalidOperationException("合成结果为空");
		Out($"OK voice={voice} sampleRate={sampleRate} samples={samples.Length} "
			+ $"seconds={samples.Length / (double)sampleRate:F2}");
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
		list.AddRange(WindowsAsr.Scan());
		Out($"Count={list.Count}");
		if (list.Count == 0) {
			Err("未发现 Sherpa 模型或 Windows 系统语音识别器");
			return 1;
		}
		foreach (var m in list) {
			if (m.IsWindows)
				Out($"  [Windows] {m.DisplayName} culture={m.Culture} id={m.SystemRecognizerId}");
			else
				Out($"  [{m.Type}] {m.DisplayName} sr={m.SampleRate} dir={m.ModelDir}");
		}
		return 0;
	}

	static int listface() {
		Out("=== 人脸模型扫描 ===");
		Out($"ModelsRoot={FaceModels.ModelsRoot()}");
		Out($"Exists={Directory.Exists(FaceModels.ModelsRoot())}");
		var onnx = FaceModels.ListOnnx();
		Out($"Count={onnx.Count}");
		if (onnx.Count == 0) {
			Err("未发现人脸 ONNX（请放到程序目录 facemodels）");
			return 1;
		}
		Out("检测: " + string.Join(", ", FaceModels.DetModels(onnx)));
		Out("识别: " + string.Join(", ", FaceModels.RegModels(onnx)));
		Out("关键点: " + string.Join(", ", FaceModels.LmkModels(onnx)));
		Out("属性: " + string.Join(", ", FaceModels.AttrModels(onnx)));
		foreach (var n in onnx)
			Out("  " + n);
		return 0;
	}

	/// <summary>用人脸叠加同一套 CJK 字体写「女 22岁」，对照 Hershey（会变成 ??）。</summary>
	static int testfaceoverlay() {
		Out("=== 人脸叠加中文 --test-face-overlay ===");
		NativeRuntime.EnsureOpenCv();
		const string label = "女 22岁";
		var bg = new OpenCvSharp.Scalar(30, 30, 30);
		using var cjk = new OpenCvSharp.Mat(80, 400, OpenCvSharp.MatType.CV_8UC3, bg);
		using var hershey = new OpenCvSharp.Mat(80, 400, OpenCvSharp.MatType.CV_8UC3, bg);
		var fontName = ImageUtil.Putcjk(cjk, label, 16, 56) ?? "";
		Out("font=" + fontName);
		bool cjkFont = fontName.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0
			|| fontName.Contains("雅黑") || fontName.Contains("黑体")
			|| fontName.IndexOf("SimHei", StringComparison.OrdinalIgnoreCase) >= 0;
		if (!cjkFont) {
			Err("未找到中文字体（微软雅黑/黑体），实际=" + fontName);
			return 1;
		}
		OpenCvSharp.Cv2.PutText(hershey, label, new OpenCvSharp.Point(16, 56),
			OpenCvSharp.HersheyFonts.HersheySimplex, 1.2, new OpenCvSharp.Scalar(80, 220, 255), 2);
		int cjkInk = countbgrink(cjk, 30);
		int hersheyInk = countbgrink(hershey, 30);
		Out($"cjkInk={cjkInk} hersheyInk={hersheyInk}");
		var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
		Directory.CreateDirectory(dir);
		var cjkPath = Path.Combine(dir, "face_overlay_cjk.png");
		var hersheyPath = Path.Combine(dir, "face_overlay_hershey.png");
		OpenCvSharp.Cv2.ImWrite(cjkPath, cjk);
		OpenCvSharp.Cv2.ImWrite(hersheyPath, hershey);
		Out("saved " + cjkPath);
		Out("saved " + hersheyPath);
		if (cjkInk < 200) {
			Err("CJK 叠加像素过少，中文可能未画出");
			return 1;
		}
		if (cjkInk <= hersheyInk) {
			Err("CJK 叠加不比 Hershey 更密，可能仍在用西文字体");
			return 1;
		}
		Out("=== OK：人脸叠加中文 ===");
		return 0;
	}

	static int countbgrink(OpenCvSharp.Mat bgr, int bg) {
		int n = 0;
		for (int y = 0; y < bgr.Rows; y++) {
			for (int x = 0; x < bgr.Cols; x++) {
				var p = bgr.At<OpenCvSharp.Vec3b>(y, x);
				if (Math.Abs(p.Item0 - bg) > 8 || Math.Abs(p.Item1 - bg) > 8 || Math.Abs(p.Item2 - bg) > 8)
					n++;
			}
		}
		return n;
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
		var tree = FeaturePick.BuildTree();
		FeaturePick.ApplyInstalled(tree);
		FeaturePick.RefreshDiff(tree);
		FeaturePick.DiffSelection(tree, out var addN, out var addSz, out var delN, out var delSz);
		Out($"Pick installed add={addN}/{FeatureInstaller.FormatBytes(addSz)} del={delN}/{FeatureInstaller.FormatBytes(delSz)}");
		foreach (var n in FeaturePick.Leaves(tree))
			Out($"  pick [{(n.IsChecked == true ? "x" : " ")}] {n.Diff,-4} {n.SizeLabel,-12} {n.Title}  ({n.Id})");
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
		var optJa = TtsInstallCatalog.LanguageOptions(list).Any(x => x.Code == "ja");
		var jaN = list.Count(x => x.AppSupported && TtsLang.Match(x.Lang, "ja"));
		Out($"option-ja={optJa} ja-supported={jaN}");
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
		models.AddRange(WindowsAsr.Scan());
		if (models.Count == 0) {
			Err("未发现 ASR 模型（用 --list-asr 查看）");
			return 1;
		}
		AsrModelInfo model = null;
		if (!string.IsNullOrWhiteSpace(modelHint))
			model = models.FirstOrDefault(m => Compat.Contains(m.DisplayName, modelHint, StringComparison.OrdinalIgnoreCase));
		model ??= WindowsAsr.PickOffline(models, "");

		Out($"模型: [{(model.IsWindows ? "Windows" : model.Type.ToString())}] {model.DisplayName}");
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

		var t0 = Environment.TickCount;
		string text;
		var tRec = Environment.TickCount;
		if (model.IsWindows) {
			Out($"系统识别器: {model.Culture} · provider=Windows");
			text = WindowsAsr.Recognize(model, samples, sr);
		}
		else {
			using var engine = new AsrEngine();
			engine.Mode = compute;
			var tLoad = Environment.TickCount;
			engine.LoadModel(model, lang, !noItn);
			var loadMs = Environment.TickCount - tLoad;
			Out($"模型加载: {loadMs}ms · provider={engine.Provider}"
				+ (engine.GpuFallbackReason != null ? $" · 回退: {engine.GpuFallbackReason}" : ""));
			tRec = Environment.TickCount;
			text = engine.Recognize(samples, sr);
		}
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
			ModelPackId = string.IsNullOrWhiteSpace(packId) ? "winocr" : packId,
			ModelVariant = variant ?? "",
		};
		if (WinOcr.Is(opt)) {
			opt.ModelPackId = WinOcr.PackId;
			opt.ModelsDir = "";
			try {
				var saved = new OcrOptions();
				AppConfig.LoadInto(saved);
				opt.WinOcrLangs = saved.WinOcrLangs ?? "";
			}
			catch { }
			if (detLimit.HasValue) opt.DetLimitSideLen = detLimit.Value;
			return opt;
		}
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

	/// <summary>跨屏虚拟选区进入标注，检查副屏是否显示缩放手柄。</summary>
	static int runtestoverlayspanadj() {
		AppConfig.applylogswitch(true);
		CaptureLog.SessionStart("CLI --test-overlay-span-adj");
		Out("=== 跨屏选区拖动 --test-overlay-span-adj ===");
		var screens = System.Windows.Forms.Screen.AllScreens
			.Where(s => s.Bounds.Width > 8 && s.Bounds.Height > 8)
			.ToArray();
		if (screens.Length < 2) {
			Out("SKIP 需要至少两块屏");
			return 0;
		}
		var a = screens[0].Bounds;
		var b = screens[1].Bounds;
		var left = Math.Min(a.Left + a.Width / 4, b.Left + b.Width / 4);
		var right = Math.Max(a.Right - a.Width / 4, b.Right - b.Width / 4);
		var top = Math.Max(a.Top, b.Top) + 40;
		var bot = Math.Min(a.Bottom, b.Bottom) - 40;
		if (bot - top < 80) {
			top = Math.Min(a.Top, b.Top) + 20;
			bot = top + 160;
		}
		var rw = Math.Max(80, right - left);
		var rh = Math.Max(80, bot - top);
		Out($"span virt=({left},{top},{rw},{rh}) screens={screens.Length}");

		var bad = 0;
		var dumped = false;
		var t1 = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromMilliseconds(800)
		};
		t1.Tick += (_, __) => {
			t1.Stop();
			try {
				var n = CaptureOverlay.TestCommitSpan(left, top, rw, rh, Out);
				Out("guests-with-handles=" + n);
				dumped = true;
				if (n < 1) {
					Err("FAIL: 跨屏副屏没有缩放手柄");
					bad++;
				}
			}
			catch (Exception ex) {
				Err("commit span EX: " + ex);
				bad++;
			}
		};
		var t2 = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromMilliseconds(1800)
		};
		t2.Tick += (_, __) => {
			t2.Stop();
			try {
				var n = 0;
				var app = Application.Current;
				if (app != null) {
					foreach (Window w in app.Windows) {
						if (w is CaptureOverlay) {
							try { w.Close(); n++; } catch { }
						}
					}
				}
				Out("auto-close CaptureOverlay n=" + n);
			}
			catch (Exception ex) { Out("auto-close EX: " + ex.Message); }
		};
		t1.Start();
		t2.Start();
		try {
			var result = CaptureOverlay.Run(annotate: true);
			Out($"Run done Confirmed={result.Confirmed}");
		}
		catch (Exception ex) {
			Err("Run EX: " + ex);
			bad++;
		}
		if (!dumped) {
			Err("FAIL: 未进入标注");
			bad++;
		}
		Out(bad == 0 ? "=== overlay-span-adj 完成 ===" : "=== overlay-span-adj 失败 ===");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>弹出截屏遮罩约 1.6s，把各屏 HWND 与 Bounds 写入 cli_last.log / capture.log。</summary>
	static int runtestoverlaylayout() {
		AppConfig.applylogswitch(true);
		CaptureLog.SessionStart("CLI --test-overlay-layout");
		Out("=== 截屏遮罩布局 --test-overlay-layout ===");
		Out(ScreenDpi.BuildReport());

		var bad = 0;
		var esc = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromMilliseconds(1600)
		};
		esc.Tick += (_, __) => {
			esc.Stop();
			try {
				var i = 0;
				foreach (var s in System.Windows.Forms.Screen.AllScreens) {
					i++;
					Out($"Screen#{i} {(s.Primary ? "Primary" : "Sec")} Bounds={s.Bounds} scale={ScreenDpi.GetMonitorScale(s.Bounds.Left + 1, s.Bounds.Top + 1):0.###}");
				}
				var app = Application.Current;
				if (app != null) {
					foreach (Window w in app.Windows) {
						if (w is not CaptureOverlay) continue;
						var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
						var hs = ScreenDpi.WindowScale(hwnd);
						Out($"overlay LeftTop={w.Left:0},{w.Top:0} DIP={w.Width:0.#}x{w.Height:0.#} Actual={w.ActualWidth:0.#}x{w.ActualHeight:0.#} hwndScale={hs:0.###}");
					}
				}
			}
			catch (Exception ex) { Out("layout dump EX: " + ex.Message); }
			try {
				var n = 0;
				var app = Application.Current;
				if (app != null) {
					foreach (Window w in app.Windows) {
						if (w is CaptureOverlay) {
							try { w.Close(); n++; } catch { }
						}
					}
				}
				Out("auto-close CaptureOverlay n=" + n);
			}
			catch (Exception ex) { Out("auto-close EX: " + ex.Message); }
		};
		esc.Start();
		try {
			var result = CaptureOverlay.Run();
			Out($"Run done Confirmed={result.Confirmed}");
		}
		catch (Exception ex) {
			Err("Run EX: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== overlay-layout 完成，详见 log/capture.log ===" : "=== overlay-layout 失败 ===");
		return bad == 0 ? 0 : 1;
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

	/// <summary>HTTP /api/chat：无 LLM 时期望 960；有配置则可带 tts=sapi。</summary>
	static int runtesthttpchat() {
		Out("=== HTTP LLM 对话 --test-http-chat ===");
		var bad = 0;
		void fail(string m) {
			Err("FAIL " + m);
			bad++;
		}
		HttpOcrServer srv = null;
		OcrRunner runner = null;
		try {
			var cfg = new OcrOptions();
			AppConfig.LoadInto(cfg);
			runner = new OcrRunner();
			srv = new HttpOcrServer(() => cfg, runner);
			srv.SetServices(new HttpApiServices {
				GetOpts = () => cfg,
				ScanTts = () => new List<TtsModelInfo>(),
				ScanAsr = () => new List<AsrModelInfo>(),
			});
			var port = 0;
			for (var p = 18770; p <= 18774; p++) {
				try {
					srv.Start("127.0.0.1", p);
					port = p;
					break;
				}
				catch (Exception ex) {
					Out($"bind {p} fail: {ex.Message}");
				}
			}
			if (port == 0) {
				fail("无法绑定 127.0.0.1:18770-18774");
				return 1;
			}
			var baseUrl = $"http://127.0.0.1:{port}";
			Out("listen " + baseUrl);
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

			var st = Task.Run(() => http.GetStringAsync(baseUrl + "/api/status").GetAwaiter().GetResult())
				.GetAwaiter().GetResult();
			Out("status " + (st.Length > 300 ? st.Substring(0, 300) + "…" : st));
			using (var sd = JsonDocument.Parse(st)) {
				if (sd.RootElement.GetProperty("code").GetInt32() != 100)
					fail("status code");
				if (!sd.RootElement.GetProperty("data").TryGetProperty("llm_chat", out _))
					fail("status missing llm_chat");
				else
					Out("llm_chat=" + sd.RootElement.GetProperty("data").GetProperty("llm_chat"));
			}

			// 空请求 → 960 或 961
			var emptyBody = new StringContent("{}", Encoding.UTF8, "application/json");
			var emptyResp = Task.Run(() => http.PostAsync(baseUrl + "/api/chat", emptyBody).GetAwaiter().GetResult())
				.GetAwaiter().GetResult();
			var emptyJson = emptyResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			Out("empty " + emptyJson);
			using (var ed = JsonDocument.Parse(emptyJson)) {
				var code = ed.RootElement.GetProperty("code").GetInt32();
				if (code is not 960 and not 961)
					fail("empty expect 960/961 got " + code);
				else
					Out("empty OK code=" + code);
			}

			if (AsrLlmClient.IsChatReady(cfg)) {
				Out("--- live chat (LLM configured) ---");
				var body = new StringContent(
					"{\"text\":\"用三个字回答：你好\",\"tts\":true,\"engine\":\"sapi\",\"agent\":false}",
					Encoding.UTF8, "application/json");
				var resp = Task.Run(() => http.PostAsync(baseUrl + "/api/chat", body).GetAwaiter().GetResult())
					.GetAwaiter().GetResult();
				var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				Out("chat " + (json.Length > 500 ? json.Substring(0, 500) + "…" : json));
				using var cd = JsonDocument.Parse(json);
				if (cd.RootElement.GetProperty("code").GetInt32() != 100)
					fail("live chat code");
				else {
					var data = cd.RootElement.GetProperty("data");
					var text = data.GetProperty("text").GetString() ?? "";
					if (text.Length == 0) fail("live chat empty text");
					else Out("reply OK len=" + text.Length);
					if (data.TryGetProperty("wav_base64", out var b64)
						&& b64.ValueKind == JsonValueKind.String
						&& (b64.GetString()?.Length ?? 0) > 100)
						Out("tts wav_base64 OK");
					else if (data.TryGetProperty("tts_error", out var te))
						Out("tts_error (可接受): " + te);
					else
						Out("WARN: no wav_base64");
				}
			}
			else
				Out("SKIP live chat（未配置 chat LLM）");
		}
		catch (Exception ex) {
			fail(ex.Message);
			Err(ex.ToString());
		}
		finally {
			try { srv?.Dispose(); } catch { }
			try { runner?.Dispose(); } catch { }
		}
		Out(bad == 0 ? "=== OK：HTTP chat ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>HTTP /api/tts：SAPI、Windows 与 Edge 在线合成 WAV。</summary>
	static int runtesthttptts() {
		Out("=== HTTP TTS SAPI/WinRT/Edge --test-http-tts ===");
		var bad = 0;
		void fail(string m) {
			Err("FAIL " + m);
			bad++;
		}
		HttpOcrServer srv = null;
		OcrRunner runner = null;
		try {
			runner = new OcrRunner();
			srv = new HttpOcrServer(() => new OcrOptions(), runner);
			srv.SetServices(new HttpApiServices {
				GetOpts = () => new OcrOptions(),
				ScanTts = () => new List<TtsModelInfo>(),
			});
			var port = 0;
			for (var p = 18765; p <= 18769; p++) {
				try {
					srv.Start("127.0.0.1", p);
					port = p;
					break;
				}
				catch (Exception ex) {
					Out($"bind {p} fail: {ex.Message}");
				}
			}
			if (port == 0) {
				fail("无法绑定 127.0.0.1:18765-18769");
				return 1;
			}
			var baseUrl = $"http://127.0.0.1:{port}";
			Out("listen " + baseUrl);
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };

			var modelsJson = Task.Run(() =>
				http.GetStringAsync(baseUrl + "/api/tts/models").GetAwaiter().GetResult())
				.GetAwaiter().GetResult();
			Out("models " + (modelsJson.Length > 400 ? modelsJson.Substring(0, 400) + "…" : modelsJson));
			using var doc = JsonDocument.Parse(modelsJson);
			if (doc.RootElement.GetProperty("code").GetInt32() != 100)
				fail("models code != 100");
			var hasSapi = false;
			var hasWin = false;
			var hasEdge = false;
			if (doc.RootElement.TryGetProperty("data", out var data)
				&& data.ValueKind == JsonValueKind.Array) {
				foreach (var m in data.EnumerateArray()) {
					var eng = m.TryGetProperty("engine", out var e) ? e.GetString() ?? "" : "";
					if (eng == "sapi") hasSapi = true;
					if (eng == "winrt") hasWin = true;
					if (eng == "edge") hasEdge = true;
				}
			}
			Out($"hasSapi={hasSapi} hasWinrt={hasWin} hasEdge={hasEdge}");
			if (!hasSapi && !hasWin) fail("models 无 SAPI / Windows 条目");
			if (!hasEdge) fail("models 无 Edge Online 条目");
			var again = Task.Run(() =>
				http.GetStringAsync(baseUrl + "/api/tts/models").GetAwaiter().GetResult())
				.GetAwaiter().GetResult();
			using (var againDoc = JsonDocument.Parse(again)) {
				if (!againDoc.RootElement.TryGetProperty("cached", out var cachedEl) || !cachedEl.GetBoolean())
					fail("第二次 /api/tts/models 未用缓存");
			}
			var engines = Task.Run(() =>
				http.GetStringAsync(baseUrl + "/api/tts/engines").GetAwaiter().GetResult())
				.GetAwaiter().GetResult();
			using (var engDoc = JsonDocument.Parse(engines)) {
				if (engDoc.RootElement.GetProperty("code").GetInt32() != 100)
					fail("engines code != 100");
				var n = engDoc.RootElement.GetProperty("data").GetArrayLength();
				if (n != 4) fail("engines 不是 4 个");
			}
			var one = Task.Run(() =>
				http.GetStringAsync(baseUrl + "/api/tts/models?engine=edge").GetAwaiter().GetResult())
				.GetAwaiter().GetResult();
			using (var oneDoc = JsonDocument.Parse(one)) {
				if (!oneDoc.RootElement.TryGetProperty("cached", out var oneCached) || !oneCached.GetBoolean())
					fail("engine=edge 未走全量缓存");
				foreach (var m in oneDoc.RootElement.GetProperty("data").EnumerateArray()) {
					var eng = m.GetProperty("engine").GetString() ?? "";
					if (eng != "edge") fail("engine=edge 混入了 " + eng);
				}
			}

			if (hasSapi) testhttpttspost(http, baseUrl, "sapi", fail);
			if (hasWin) testhttpttspost(http, baseUrl, "winrt", fail);
			if (hasEdge) testhttpttspost(http, baseUrl, "edge", fail);
			if (hasSapi || hasWin) {
				var fb = httpttspost(http, baseUrl, "{\"text\":\"你好\"}");
				using var dFb = JsonDocument.Parse(fb);
				if (dFb.RootElement.GetProperty("code").GetInt32() != 100)
					fail("省略 engine 回落失败 " + fb);
				else Out("omit engine OK " + dFb.RootElement.GetProperty("data").GetProperty("engine"));
			}

			var unknown = httpttspost(http, baseUrl, "{\"text\":\"hi\",\"engine\":\"nope\"}");
			using (var d2 = JsonDocument.Parse(unknown)) {
				if (d2.RootElement.GetProperty("code").GetInt32() != 925)
					fail("未知 engine 应为 925 got=" + unknown);
				else Out("unknown engine 925 OK");
			}
		}
		catch (Exception ex) {
			fail(ex.ToString());
		}
		finally {
			try { srv?.Dispose(); } catch { }
			try { runner?.Dispose(); } catch { }
		}
		Out(bad == 0 ? "=== OK：HTTP TTS SAPI/WinRT/Edge ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static void testhttpttspost(HttpClient http, string baseUrl, string engine, Action<string> fail) {
		var json = httpttspost(http, baseUrl, $"{{\"text\":\"你好\",\"engine\":\"{engine}\"}}");
		using var doc = JsonDocument.Parse(json);
		var code = doc.RootElement.GetProperty("code").GetInt32();
		if (code != 100) {
			fail($"{engine} code={code} {json}");
			return;
		}
		var data = doc.RootElement.GetProperty("data");
		var b64 = data.GetProperty("wav_base64").GetString() ?? "";
		var wav = Convert.FromBase64String(b64);
		if (wav.Length < 100 || wav[0] != (byte)'R' || wav[1] != (byte)'I') {
			fail($"{engine} wav 不是 RIFF len={wav.Length}");
			return;
		}
		Out($"{engine} OK wav={wav.Length} sr={data.GetProperty("sample_rate")} provider={data.GetProperty("provider")}");
	}

	static string httpttspost(HttpClient http, string baseUrl, string json) {
		return Task.Run(() => {
			using var content = new StringContent(json, Encoding.UTF8, "application/json");
			using var resp = http.PostAsync(baseUrl + "/api/tts", content).GetAwaiter().GetResult();
			return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
		}).GetAwaiter().GetResult();
	}

	/// <summary>Agent 沙箱路径、tool_call 解析、读写/脚本（默认不去网）。</summary>
	static int runtestllmagent() {
		Out("=== LLM Agent --test-llm-agent ===");
		var bad = 0;
		void fail(string m) {
			Err("FAIL " + m);
			bad++;
		}

		_ = LlmAgentPaths.Root;
		Out("root=" + LlmAgentPaths.Root);
		if (!LlmAgentPaths.TryResolve("a/b.txt", out var fullOk, out var relOk, out var errOk) || errOk != null)
			fail("resolve a/b.txt: " + errOk);
		else
			Out("resolve ok rel=" + relOk + " full=" + fullOk);
		if (LlmAgentPaths.TryResolve("../x.txt", out _, out _, out var errEsc))
			fail("resolve ../x.txt should fail");
		else
			Out("escape rejected: " + errEsc);
		if (LlmAgentPaths.TryResolve(@"C:\Windows\notepad.exe", out _, out _, out var errAbs))
			fail("resolve absolute outside should fail");
		else
			Out("abs outside rejected: " + errAbs);

		var calls = LlmAgentTools.ParseCalls(
			"先搜一下 <tool_call>{\"name\":\"web_search\",\"arguments\":{\"query\":\"ScreenKit\"}}</tool_call>\n" +
			"<tool_call>{\"name\":\"list_dir\",\"arguments\":{\"path\":\".\"}}</tool_call> 再答");
		if (calls.Count != 2) fail("ParseCalls count=" + calls.Count);
		else {
			Out("ParseCalls n=2 names=" + calls[0].Name + "," + calls[1].Name);
			if (calls[0].Name != "web_search") fail("name0");
			if (calls[1].Name != "list_dir") fail("name1");
		}

		// 天气查询（wttr.in，需联网/代理）
		try {
			var wx = LlmAgentTools.Execute(new LlmToolCall {
				Name = "web_search",
				ArgsJson = "{\"query\":\"太仓天气\"}",
			}, CancellationToken.None);
			Out("weather search: " + clipcli(wx, 200));
			if (wx == null || (wx.IndexOf("weather:", StringComparison.OrdinalIgnoreCase) < 0
				&& wx.IndexOf("°C", StringComparison.Ordinal) < 0
				&& wx.IndexOf("detail:", StringComparison.OrdinalIgnoreCase) < 0))
				Out("WARN: weather search 无温度结果（检查代理/网络）");
			else
				Out("weather search OK");
		}
		catch (Exception ex) {
			Out("WARN weather: " + ex.Message);
		}
		var stripped = LlmAgentTools.StripCalls(
			"<tool_call>{\"name\":\"x\",\"arguments\":{}}</tool_call>hello");
		if (stripped != "hello") fail("StripCalls got=" + stripped);

		var writeCall = new LlmToolCall {
			Name = "write_file",
			ArgsJson = "{\"path\":\"agent_test_note.txt\",\"content\":\"hello-agent\"}",
		};
		var wr = LlmAgentTools.Execute(writeCall, CancellationToken.None);
		Out("write: " + wr);
		if (wr == null || !wr.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
			fail("write_file: " + wr);
		var rd = LlmAgentTools.Execute(new LlmToolCall {
			Name = "read_file",
			ArgsJson = "{\"path\":\"agent_test_note.txt\"}",
		}, CancellationToken.None);
		Out("read: " + clipcli(rd, 80));
		if (rd == null || rd.IndexOf("hello-agent", StringComparison.Ordinal) < 0)
			fail("read_file mismatch");
		var ls = LlmAgentTools.Execute(new LlmToolCall {
			Name = "list_dir",
			ArgsJson = "{\"path\":\".\"}",
		}, CancellationToken.None);
		Out("list: " + clipcli(ls, 120));
		if (ls == null || ls.IndexOf("agent_test_note.txt", StringComparison.Ordinal) < 0)
			fail("list_dir missing note");

		// 可选：本机 python
		try {
			File.WriteAllText(Path.Combine(LlmAgentPaths.Root, "agent_echo.py"),
				"import sys\nprint('pyok', sys.argv[1] if len(sys.argv)>1 else '')\n",
				Encoding.UTF8);
			var py = LlmAgentTools.Execute(new LlmToolCall {
				Name = "run_script",
				ArgsJson = "{\"path\":\"agent_echo.py\",\"args\":[\"hi\"]}",
			}, CancellationToken.None);
			Out("run_script py: " + clipcli(py, 160));
			if (py != null && py.IndexOf("pyok", StringComparison.Ordinal) >= 0)
				Out("python script OK");
			else
				Out("SKIP/WARN python script (python 可能未在 PATH): " + clipcli(py, 120));
		}
		catch (Exception ex) {
			Out("SKIP python: " + ex.Message);
		}

		Out(bad == 0 ? "=== OK：LLM Agent 沙箱/解析 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static string clipcli(string s, int max) {
		s = s ?? "";
		if (s.Length <= max) return s;
		return s.Substring(0, max) + "…";
	}

	/// <summary>最近 1000 条 LLM 请求：用量、地址去查询串、落盘顺序（不去网）。</summary>
	static int testllmlog() {
		var dir = Path.Combine(Path.GetTempPath(), "sk-llmlog-" + Guid.NewGuid().ToString("N"));
		var file = Path.Combine(dir, "llm-calls.jsonl");
		try {
			LlmCalls.UseFile(file);
			LlmCalls.Add("https://api.example/v1/chat/completions?key=secret",
				"{\"model\":\"demo\"}", 200, 12,
				"{\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":5,\"total_tokens\":8}}", null);
			var one = LlmCalls.Latest();
			if (one.Length != 1) {
				Out($"fail count {one.Length}");
				return 2;
			}
			if (one[0].Model != "demo" || one[0].Ms != 12 || one[0].Code != 200) {
				Out($"fail row model={one[0].Model} ms={one[0].Ms} code={one[0].Code}");
				return 3;
			}
			if (one[0].Prompt != 3 || one[0].Completion != 5 || one[0].Total != 8) {
				Out($"fail usage {one[0].Prompt}/{one[0].Completion}/{one[0].Total}");
				return 4;
			}
			if (one[0].Url.IndexOf('?') >= 0 || one[0].Url.IndexOf("key=", StringComparison.Ordinal) >= 0) {
				Out($"fail url {one[0].Url}");
				return 5;
			}
			LlmCalls.Add("https://api.example/v1/chat", "{\"model\":\"alt\"}", 200, 1,
				"{\"usage\":{\"input_tokens\":1,\"output_tokens\":2}}", null);
			var alt = LlmCalls.Latest()[0];
			if (alt.Prompt != 1 || alt.Completion != 2 || alt.Total != 3) {
				Out($"fail alt usage {alt.Prompt}/{alt.Completion}/{alt.Total}");
				return 6;
			}
			LlmCalls.Add("https://api.example/v1/chat", "{\"model\":\"err\"}", 0, 4, "",
				new InvalidOperationException("down"));
			var err = LlmCalls.Latest()[0];
			if (err.Error != "down" || err.Prompt != -1) {
				Out($"fail err {err.Error} prompt={err.Prompt}");
				return 7;
			}
			LlmCalls.UseFile(file);
			for (var i = 0; i < 1005; i++)
				LlmCalls.Add("https://api.example/v1/chat", $"{{\"model\":\"m{i}\"}}", 200, i, "{}", null);
			var all = LlmCalls.Latest();
			if (all.Length != LlmCalls.CAP) {
				Out($"fail cap {all.Length}");
				return 8;
			}
			if (all[0].Model != "m1004" || all[LlmCalls.CAP - 1].Model != "m5") {
				Out($"fail order newest={all[0].Model} oldest={all[LlmCalls.CAP - 1].Model}");
				return 9;
			}
			var lines = File.ReadAllLines(file);
			if (lines.Length != LlmCalls.CAP) {
				Out($"fail file {lines.Length}");
				return 10;
			}
			var first = JsonSerializer.Deserialize<LlmCall>(lines[0]);
			var last = JsonSerializer.Deserialize<LlmCall>(lines[lines.Length - 1]);
			if (first == null || last == null || first.Model != "m5" || last.Model != "m1004") {
				Out($"fail disk {(first == null ? "" : first.Model)}..{(last == null ? "" : last.Model)}");
				return 11;
			}
			var shown = LlmCalls.Pretty("{\"content\":\"\\u8BF7\\u5C06\"}");
			if (shown.IndexOf("请将", StringComparison.Ordinal) < 0 || shown.IndexOf("\\u8BF7", StringComparison.Ordinal) >= 0) {
				Out($"fail pretty {shown}");
				return 12;
			}
			Out($"llm log ok n={all.Length}");
			return 0;
		}
		finally {
			try {
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			catch { }
		}
	}

	/// <summary>LLM 对话历史裁剪与续写数组形状（不去网）。</summary>
	static int runtestllmchat() {
		Out("=== LLM 对话 --test-llm-chat ===");
		var bad = 0;
		void fail(string m) {
			Err("FAIL " + m);
			bad++;
		}
		if (!AsrLlmClient.IsOpenCodeUrl("https://opencode.ai/zen/go/v1/chat/completions"))
			fail("IsOpenCodeUrl go");
		if (!AsrLlmClient.IsOpenCodeUrl("https://opencode.ai/zen/v1/chat/completions"))
			fail("IsOpenCodeUrl zen");
		if (AsrLlmClient.IsOpenCodeUrl("https://api.siliconflow.cn/v1/chat/completions"))
			fail("IsOpenCodeUrl should be false for siliconflow");
		else
			Out("IsOpenCodeUrl OK");
		var h = new LlmChatHistory();
		for (var i = 1; i <= 40; i++) {
			h.Add("user", $"u{i}");
			h.Add("assistant", $"a{i}");
		}
		var snap = h.Snapshot();
		if (snap.Count > LlmChatHistory.MAXTURNS)
			fail($"turns {snap.Count} > {LlmChatHistory.MAXTURNS}");
		if (snap.Count == 0) fail("snapshot empty");
		if (snap[0].Role != "user") fail("trim 后须以 user 开头 got=" + snap[0].Role);
		if (snap[snap.Count - 1].Role != "assistant") fail("末条应为 assistant");
		var msgs = h.ToMessages();
		if (msgs.Count != snap.Count) fail("ToMessages count");
		if (msgs[0].role != "user" || msgs[msgs.Count - 1].role != "assistant")
			fail("ToMessages roles");

		h.Clear();
		if (h.Count != 0) fail("Clear");
		h.Add("assistant", "orphan");
		h.Add("user", "u1");
		h.Add("user", "u2");
		h.Add("assistant", "a2");
		for (var i = 0; i < 40; i++) {
			h.Add("user", new string('x', 400));
			h.Add("assistant", new string('y', 400));
		}
		snap = h.Snapshot();
		if (snap.Count > LlmChatHistory.MAXTURNS) fail("MAXTURNS after long");
		if (snap.Count > 0 && snap[0].Role != "user")
			fail("连续 user 裁剪后不得 assistant-first got=" + snap[0].Role);

		object orig = new object[] {
			new { role = "system", content = "s" },
			new { role = "user", content = "u1" },
			new { role = "assistant", content = "a1" },
			new { role = "user", content = "u2" },
		};
		var c1 = AsrLlmClient.AppendContinue(orig, "p1") as object[];
		var c2 = AsrLlmClient.AppendContinue(orig, "p2") as object[];
		if (c1 == null || c1.Length != 6) fail("AppendContinue len1=" + (c1?.Length ?? -1));
		if (c2 == null || c2.Length != 6) fail("AppendContinue 须拷贝原始数组 len2=" + (c2?.Length ?? -1));
		var acc = orig;
		acc = AsrLlmClient.AppendContinue(acc, "p1");
		acc = AsrLlmClient.AppendContinue(orig, "p1p2") as object[];
		if (acc is not object[] a3 || a3.Length != 6)
			fail("二次续写仍应对原始拷贝");

		Out(bad == 0 ? "=== OK：对话历史与续写 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>LLM 超长续写：finish_reason 判定与片段拼接（不去网）。</summary>
	static int runtestllmcontinue() {
		Out("=== LLM 超长续写 --test-llm-continue ===");
		var bad = 0;
		void fail(string m) {
			Err("FAIL " + m);
			bad++;
		}
		if (!AsrLlmClient.FinishIsTruncated("length")) fail("length 应为截断");
		if (!AsrLlmClient.FinishIsTruncated("max_tokens")) fail("max_tokens 应为截断");
		if (!AsrLlmClient.FinishIsTruncated("MAX_OUTPUT_TOKENS")) fail("max_output_tokens 应为截断");
		if (AsrLlmClient.FinishIsTruncated("stop")) fail("stop 不应为截断");
		if (AsrLlmClient.FinishIsTruncated("")) fail("空 finish 不应为截断");

		var merged = AsrLlmClient.MergeContinue("abcdefghijklMNOPQRST", "MNOPQRSTuvwxyz");
		if (merged != "abcdefghijklMNOPQRSTuvwxyz") fail("重叠拼接 got=" + merged);
		merged = AsrLlmClient.MergeContinue("hello", " world");
		if (merged != "hello world") fail("无重叠拼接 got=" + merged);
		merged = AsrLlmClient.MergeContinue("", "only");
		if (merged != "only") fail("空 acc got=" + merged);

		var json = "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"role\":\"assistant\",\"content\":\"HELLO\"}}]}";
		var (text, finish) = AsrLlmClient.ParseChoice(json);
		if (text != "HELLO") fail("ParseChoice text got=" + text);
		if (!AsrLlmClient.FinishIsTruncated(finish)) fail("ParseChoice finish=" + finish);

		json = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"done\"}}]}";
		(text, finish) = AsrLlmClient.ParseChoice(json);
		if (text != "done" || AsrLlmClient.FinishIsTruncated(finish))
			fail($"ParseChoice stop text={text} finish={finish}");

		var numbered = AsrLlmClient.ParseNumbered("1. Hello\n2. World\n3. Third", 3);
		if (numbered.Count != 3 || numbered[0] != "Hello" || numbered[1] != "World" || numbered[2] != "Third")
			fail("ParseNumbered 1-3 got=" + string.Join("|", numbered));
		numbered = AsrLlmClient.ParseNumbered("1. only first\n3. skipped two", 3);
		if (numbered[0] != "only first" || numbered[1] != null || numbered[2] != "skipped two")
			fail("ParseNumbered miss got=" + string.Join("|", numbered.Select(x => x ?? "(null)")));

		Out(bad == 0 ? "=== OK：续写判定与拼接 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	/// <summary>
	/// 先放入位图再「复制为路径」，确认剪贴板只剩路径文本、无残留图。
	/// 残留 CF_DIB 时部分输入框会把图粘成「▀」。
	/// </summary>
	static int runtestclipboardpath() {
		int code = 1;
		Exception threadEx = null;
		var t = new Thread(() => {
			try { code = runtestclipboardpathCore(); }
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

	static int runtestclipboardpathCore() {
		Out("=== 剪贴板复制为路径 --test-clipboard-path ===");
		CaptureLog.Enabled = true;
		var bad = 0;
		// 小图：正确性
		var bmp = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
		bmp.Freeze();
		for (var n = 1; n <= 3; n++) {
			Out($"--- round {n} save-as-path after image ---");
			try { ImageUtil.Toclipboard(bmp); }
			catch (Exception ex) {
				Out("pre Toclipboard: " + ex.Message);
			}
			Out("pre formats: " + ImageUtil.ClipboardFormatList());

			string path;
			try {
				path = ImageUtil.SaveScreenshotAndCopy(bmp, "clippath",
					copyAsImage: false, copyAsFile: false, copyAsPath: true);
			}
			catch (Exception ex) {
				Err($"SaveScreenshotAndCopy fail: {ex.Message}");
				bad++;
				continue;
			}
			Out("saved " + path);
			Out("post formats: " + ImageUtil.ClipboardFormatList());
			var onlyPath = false;
			try { onlyPath = ImageUtil.ClipboardIsPathOnly(path); }
			catch (Exception ex) { Out("ClipboardIsPathOnly: " + ex.Message); }
			Out($"post ClipboardIsPathOnly={onlyPath}");
			if (!onlyPath) {
				Err("FAIL: 剪贴板不是纯路径文本（仍含位图/文件，粘贴会变成 ▀）");
				bad++;
			}
			else Out("round OK");
		}

		Out("--- recopy last as path after image ---");
		try { ImageUtil.Toclipboard(bmp); }
		catch (Exception ex) { Out("pre Toclipboard: " + ex.Message); }
		Out("pre formats: " + ImageUtil.ClipboardFormatList());
		string recopy = null;
		try { recopy = ImageUtil.RecopyLastScreenshot(false, false, true); }
		catch (Exception ex) {
			Err("RecopyLastScreenshot fail: " + ex.Message);
			bad++;
		}
		Out("recopy " + recopy);
		Out("post formats: " + ImageUtil.ClipboardFormatList());
		var recopyOk = false;
		try {
			recopyOk = !string.IsNullOrWhiteSpace(recopy) && ImageUtil.ClipboardIsPathOnly(recopy);
		}
		catch (Exception ex) { Out("ClipboardIsPathOnly: " + ex.Message); }
		if (!recopyOk) {
			Err("FAIL: 重新复制为路径未成功");
			bad++;
		}
		else Out("recopy OK");

		// 大图：延迟位图后改路径，clip 阶段应仍很快（不再 OleFlush 旧图）
		Out("--- large delayed-image then path (timing) ---");
		var big = new WriteableBitmap(3840, 2160, 96, 96, PixelFormats.Bgra32, null);
		big.Freeze();
		var tImg = Environment.TickCount;
		try { ImageUtil.Toclipboard(big); }
		catch (Exception ex) { Out("large Toclipboard: " + ex.Message); }
		Out($"large Toclipboard(persist:false) {Environment.TickCount - tImg}ms");
		Out("pre formats: " + ImageUtil.ClipboardFormatList());
		string bigPath = null;
		var tPath = Environment.TickCount;
		try {
			bigPath = ImageUtil.SaveScreenshotAndCopy(big, "clippathbig",
				copyAsImage: false, copyAsFile: false, copyAsPath: true);
		}
		catch (Exception ex) {
			Err("large SaveScreenshotAndCopy fail: " + ex.Message);
			bad++;
		}
		var pathTotal = Environment.TickCount - tPath;
		Out($"large save-as-path total={pathTotal}ms path={bigPath}");
		Out("post formats: " + ImageUtil.ClipboardFormatList());
		var bigOk = false;
		try {
			bigOk = !string.IsNullOrWhiteSpace(bigPath) && ImageUtil.ClipboardIsPathOnly(bigPath);
		}
		catch (Exception ex) { Out("ClipboardIsPathOnly: " + ex.Message); }
		if (!bigOk) {
			Err("FAIL: 大图后复制为路径未成功");
			bad++;
		}
		else Out("large path OK");
		// 对比：延迟大图后 Clipboard.SetText（旧路径，常会 OleFlush 旧图）vs Win32 CopyPath
		var dummy = bigPath ?? @"C:\tmp\dummy.png";
		try { ImageUtil.Toclipboard(big); } catch { }
		var tSetText = Environment.TickCount;
		try { Clipboard.SetText(dummy + ".settext"); }
		catch (Exception ex) { Out("Clipboard.SetText after large: " + ex.Message); }
		var setTextMs = Environment.TickCount - tSetText;
		Out($"Clipboard.SetText after delayed large image: {setTextMs}ms (旧路径对照)");

		try { ImageUtil.Toclipboard(big); } catch { }
		var tClip = Environment.TickCount;
		try { ImageUtil.CopyPathToClipboard(dummy); }
		catch (Exception ex) {
			Err("CopyPathToClipboard after large image: " + ex.Message);
			bad++;
		}
		var clipOnly = Environment.TickCount - tClip;
		Out($"CopyPathToClipboard after delayed large image: {clipOnly}ms");
		if (clipOnly >= 2000) {
			Err($"FAIL: 路径写入过慢 {clipOnly}ms（疑似仍 Flush 旧位图）");
			bad++;
		}
		else Out("path-after-image timing OK");

		Out(bad == 0 ? "=== OK：复制为路径不残留位图 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testimgconvert() {
		Out("=== 图片格式转换 --test-img-convert ===");
		var bad = 0;
		var dir = Path.Combine(Path.GetTempPath(), "sk_imgconv_" + Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(dir);
		try {
			var src = Path.Combine(dir, "src.png");
			const int w = 200, h = 100;
			var pixels = new byte[w * h * 4];
			for (var i = 0; i < pixels.Length; i += 4) {
				var n = i / 4;
				var x = n % w;
				var y = n / w;
				pixels[i] = (byte)((x * 13 + y * 7) & 255);
				pixels[i + 1] = (byte)((x * 5 + y * 17) & 255);
				pixels[i + 2] = (byte)((x * 29 + y * 3) & 255);
				pixels[i + 3] = 255;
			}
			var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
			bmp.Freeze();
			ImageUtil.Savefile(bmp, src);
			if (!File.Exists(src)) {
				Err("FAIL: 源 png 未写出");
				return 1;
			}

			var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var dst = ImgConvert.MakeOutPath(src, "jpg", true, null, reserved);
			var expectDir = Path.Combine(dir, "output");
			if (!dst.StartsWith(expectDir, StringComparison.OrdinalIgnoreCase)) {
				Err("FAIL: 输出不在 output/ got=" + dst);
				bad++;
			}
			ImgConvert.ConvertOne(src, dst, "jpg", 60, true, 100, 100, 90, false, CancellationToken.None);
			if (!File.Exists(dst)) {
				Err("FAIL: jpg 未写出 " + dst);
				bad++;
			}
			else {
				using var outImg = new System.Drawing.Bitmap(dst);
				if (outImg.Width != 50 || outImg.Height != 100) {
					Err($"FAIL: 尺寸 {outImg.Width}x{outImg.Height} 期望 50x100");
					bad++;
				}
				else Out($"jpg size OK {outImg.Width}x{outImg.Height}");
				var shortDst = Path.Combine(dir, "short.jpg");
				ImgConvert.ConvertOne(src, shortDst, "jpg", 60, true, 40, 0, 90, false, CancellationToken.None,
					keepOrigEn: false, keepOrigPct: 80, shortSide: true);
				using (var sh = new System.Drawing.Bitmap(shortDst)) {
					if (sh.Width != 40 || sh.Height != 80) {
						Err($"FAIL: 较短边 {sh.Width}x{sh.Height} 期望 40x80");
						bad++;
					}
					else Out("short side 40x80 OK");
				}
				var stay = ImgConvert.Encode(src, "png", 60, true, 150, 0, 0, false, CancellationToken.None, shortSide: true);
				using (var msStay = new MemoryStream(stay))
				using (var stayImg = new System.Drawing.Bitmap(msStay)) {
					if (stayImg.Width != 200 || stayImg.Height != 100) {
						Err($"FAIL: 较短边不放大 {stayImg.Width}x{stayImg.Height}");
						bad++;
					}
					else Out("short side no upscale OK");
				}
				var photo = Path.Combine(dir, "photo.png");
				File.Copy(src, photo, true);
				if (!ImgConvert.FitPhoto(photo, "png", 60, true, 80)) {
					Err("FAIL: 拍照较短边应缩小");
					bad++;
				}
				else {
					using var ph = new System.Drawing.Bitmap(photo);
					if (ph.Width != 160 || ph.Height != 80) {
						Err($"FAIL: 拍照较短边 {ph.Width}x{ph.Height} 期望 160x80");
						bad++;
					}
					else Out("photo short side 160x80 OK");
				}
				var photoStay = Path.Combine(dir, "photo_stay.png");
				File.Copy(src, photoStay, true);
				if (ImgConvert.FitPhoto(photoStay, "png", 60, true, 150)) {
					Err("FAIL: 拍照较短边已不超过时不应再压");
					bad++;
				}
				else Out("photo short side skip OK");
				var len = new FileInfo(dst).Length;
				if (len < 80) {
					Err("FAIL: jpg 过小 " + len);
					bad++;
				}
				else Out($"jpg {len} bytes");
			}

			reserved.Clear();
			var dst2 = ImgConvert.MakeOutPath(src, "png", false, dir, reserved);
			ImgConvert.ConvertOne(src, dst2, "png", 60, false, 1920, 1080, 0, true, CancellationToken.None);
			using (var out2 = new System.Drawing.Bitmap(dst2)) {
				if (out2.Width != 200 || out2.Height != 100) {
					Err($"FAIL: png 尺寸 {out2.Width}x{out2.Height}");
					bad++;
				}
				else Out("png size OK 200x100");
			}

			var q20 = ImgConvert.Encode(src, "jpg", 20, false, 1920, 1080, 0, false, CancellationToken.None);
			var q90 = ImgConvert.Encode(src, "jpg", 90, false, 1920, 1080, 0, false, CancellationToken.None);
			if (q20.Length * 5 / 4 >= q90.Length) {
				Err($"FAIL: jpg 质量 20 ({q20.Length}) 应明显小于质量 90 ({q90.Length})");
				bad++;
			}
			else Out($"jpg quality size OK 20={q20.Length} < 90={q90.Length}");
			var keepDst = Path.Combine(dir, "outkeep", "keep.jpg");
			Directory.CreateDirectory(Path.GetDirectoryName(keepDst));
			var usedOrig = ImgConvert.ConvertOne(src, keepDst, "jpg", 100, false, 1920, 1080, 0, false,
				CancellationToken.None, keepOrigEn: true, keepOrigPct: 80);
			var enc100 = ImgConvert.Encode(src, "jpg", 100, false, 1920, 1080, 0, false, CancellationToken.None);
			var origLen = new FileInfo(src).Length;
			Out($"keep-orig: orig={origLen} jpg100={enc100.Length} usedOrig={usedOrig}");
			if (enc100.Length * 100L >= origLen * 80 && !usedOrig) {
				Err("FAIL: jpg 体积 ≥ 原图 80% 应使用原图");
				bad++;
			}
			if (usedOrig) {
				var keepPath = Path.Combine(Path.GetDirectoryName(keepDst), Path.GetFileName(src));
				if (!File.Exists(keepPath)) {
					Err("FAIL: 原图未复制到 " + keepPath);
					bad++;
				}
				else Out("keep-orig copied OK");
			}
			var rotKeep = ImgConvert.ConvertOne(src, Path.Combine(dir, "outkeep", "rot.jpg"), "jpg", 100, false, 1920, 1080, 90, false,
				CancellationToken.None, keepOrigEn: true, keepOrigPct: 80);
			if (rotKeep) {
				Err("FAIL: 旋转后仍应写出新图");
				bad++;
			}
			else Out("rotate still writes new file OK");

			var repDir = Path.Combine(dir, "rep");
			Directory.CreateDirectory(repDir);
			var repSrc = Path.Combine(repDir, "photo.png");
			File.Copy(src, repSrc);
			var reservedRep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { repSrc };
			var repDst = ImgConvert.MakeOutPath(repSrc, "jpg", ImgConvert.OUTREPLACE, null, reservedRep);
			var expectJpg = Path.Combine(repDir, "photo.jpg");
			if (!string.Equals(repDst, expectJpg, StringComparison.OrdinalIgnoreCase)) {
				Err("FAIL: replace dest " + repDst);
				bad++;
			}
			ImgConvert.ConvertOne(repSrc, repDst, "jpg", 60, false, 1920, 1080, 0, false,
				CancellationToken.None, keepOrigEn: false, keepOrigPct: 80, ImgConvert.OUTREPLACE);
			if (File.Exists(repSrc)) {
				Err("FAIL: replace 后源 png 应已删除");
				bad++;
			}
			else Out("replace deleted png OK");
			if (!File.Exists(repDst)) {
				Err("FAIL: replace jpg 未写出");
				bad++;
			}
			else Out("replace jpg OK");

			var sameSrc = Path.Combine(repDir, "same.jpg");
			File.Copy(repDst, sameSrc);
			var reservedSame = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sameSrc };
			var sameDst = ImgConvert.MakeOutPath(sameSrc, "jpg", ImgConvert.OUTREPLACE, null, reservedSame);
			if (!string.Equals(sameDst, sameSrc, StringComparison.OrdinalIgnoreCase)) {
				Err("FAIL: 同后缀 replace dest 应为源路径 " + sameDst);
				bad++;
			}
			ImgConvert.ConvertOne(sameSrc, sameDst, "jpg", 20, false, 1920, 1080, 0, false,
				CancellationToken.None, keepOrigEn: false, keepOrigPct: 80, ImgConvert.OUTREPLACE);
			if (!File.Exists(sameSrc)) {
				Err("FAIL: 同后缀 replace 后文件应仍在");
				bad++;
			}
			else Out("same-ext replace OK");

			var keepRep = Path.Combine(repDir, "keep.png");
			File.Copy(src, keepRep);
			var keepEnc = ImgConvert.Encode(keepRep, "jpg", 100, false, 1920, 1080, 0, false, CancellationToken.None);
			var keepLen = new FileInfo(keepRep).Length;
			var keepDst2 = ImgConvert.MakeOutPath(keepRep, "jpg", ImgConvert.OUTREPLACE, null,
				new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keepRep });
			var keepUsed2 = ImgConvert.ConvertOne(keepRep, keepDst2, "jpg", 100, false, 1920, 1080, 0, false,
				CancellationToken.None, keepOrigEn: true, keepOrigPct: 80, ImgConvert.OUTREPLACE);
			if (keepEnc.Length * 100L >= keepLen * 80) {
				if (!keepUsed2) {
					Err("FAIL: replace+keepOrig 应跳过");
					bad++;
				}
				if (!File.Exists(keepRep)) {
					Err("FAIL: replace+keepOrig 不应删除源文件");
					bad++;
				}
				if (File.Exists(keepDst2)) {
					Err("FAIL: replace+keepOrig 不应写出新图");
					bad++;
				}
				else Out("replace keepOrig skip OK");
			}

			var recFile = Path.Combine(dir, "recycle_me.bin");
			File.WriteAllText(recFile, "recycle-test");
			ImgConvert.RecycleFile(recFile);
			if (File.Exists(recFile)) {
				Err("FAIL: RecycleFile 后文件仍在");
				bad++;
			}
			else Out("RecycleFile OK");

			var rot = ImgConvert.Encode(src, "png", 60, true, 100, 100, 90, false, CancellationToken.None);
			using var ms = new MemoryStream(rot);
			using var rotImg = new System.Drawing.Bitmap(ms);
			if (rotImg.Width != 50 || rotImg.Height != 100) {
				Err($"FAIL: Encode 预览尺寸 {rotImg.Width}x{rotImg.Height} 期望 50x100");
				bad++;
			}
			else Out("Encode preview size OK 50x100");
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		finally {
			try { Directory.Delete(dir, true); } catch { }
		}
		Out(bad == 0 ? "=== OK：图片格式转换 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testqrmake() {
		Out("=== 二维码生成 --test-qr-make ===");
		var bad = 0;
		try {
			var utf = QrMake.Encode("你好 ScreenKit", "qr", "utf8", 6, true);
			var gbk = QrMake.Encode("你好 ScreenKit", "qr", "gbk", 6, true);
			Out($"utf8 {utf.PixelWidth}x{utf.PixelHeight} gbk {gbk.PixelWidth}x{gbk.PixelHeight}");
			if (utf.PixelHeight <= utf.PixelWidth) {
				Err("FAIL: 图下应有原文，高度应大于宽度");
				bad++;
			}
			var nocap = QrMake.Encode("hello", "qr", "utf8", 6, false);
			if (nocap.PixelHeight >= utf.PixelHeight) {
				Err("FAIL: 无标题图不应更高");
				bad++;
			}
			var c128 = QrMake.Encode("ABC-123", "code128", "utf8", 4, true);
			Out($"code128 {c128.PixelWidth}x{c128.PixelHeight}");
			var hexBytes = QrMake.ParseHex("68 65 6c 6c 6f");
			if (hexBytes.Length != 5 || hexBytes[0] != 0x68) {
				Err("FAIL: hex 解析");
				bad++;
			}
			var hx = QrMake.Encode("68656c6c6f", "qr", "hex", 6, false);
			var hello = QrMake.Encode("hello", "qr", "utf8", 6, false);
			Out($"hex {hx.PixelWidth}x{hx.PixelHeight} hello {hello.PixelWidth}x{hello.PixelHeight}");
			if (hx.PixelWidth != hello.PixelWidth) {
				Err("FAIL: hex 与 utf8 hello 模块尺寸应相同");
				bad++;
			}
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：二维码生成 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testrename() {
		Out("=== 批量重命名 --test-rename ===");
		var bad = 0;
		var dir = Path.Combine(Path.GetTempPath(), "sk_rename_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(dir);
		try {
			var a = Path.Combine(dir, "img_001.jpg");
			var b = Path.Combine(dir, "img_002.jpg");
			File.WriteAllText(a, "a");
			File.WriteAllText(b, "b");
			var names = new[] { "img_001.jpg", "img_002.jpg" };
			var (oldp, newp) = BatchRename.CommonPattern(names, false);
			Out($"pattern old={oldp} new={newp}");
			if (oldp.IndexOf("%1", StringComparison.Ordinal) < 0) {
				Err("FAIL: 公共模式应含 %1");
				bad++;
			}
			var opt = new RenameOptions {
				OldPattern = "img_%1.jpg",
				NewPattern = "pic_###.jpg",
			};
			var plans = BatchRename.Plan(new[] { a, b }, names, opt);
			if (plans.Count != 2 || plans[0].To != "pic_001.jpg" || plans[1].To != "pic_002.jpg") {
				Err("FAIL: 编号展开 " + string.Join(",", plans.Select(p => p.To)));
				bad++;
			}
			foreach (var p in plans) {
				if (p.Kind == RenameKind.Ready) BatchRename.Apply(p);
			}
			if (!File.Exists(Path.Combine(dir, "pic_001.jpg")) || File.Exists(a)) {
				Err("FAIL: 未改名到 pic_001.jpg");
				bad++;
			}
			var opt2 = new RenameOptions {
				OldPattern = "2026-09%1 %2",
				NewPattern = "%2",
			};
			var longName = "2026-09-23 阴阳师直播（画饼续命）第一集（骨质发落 / 未知窥探 / 伪人 / 阴）.mp4";
			var p2 = BatchRename.Plan(new[] { Path.Combine(dir, "t.mp4") }, new[] { longName }, opt2);
			if (p2.Count != 1 || p2[0].To != "阴阳师直播（画饼续命）第一集（骨质发落 / 未知窥探 / 伪人 / 阴）.mp4") {
				Err("FAIL: %1 最短匹配 %2 应为标题全文，得到 " + (p2.Count == 0 ? "(空)" : p2[0].To));
				bad++;
			}
			else Out("%1 最短 / %2 标题 OK");
			var miss = BatchRename.Plan(
				new[] { Path.Combine(dir, "t2.mp4") },
				new[] { "2026-03-21 阴阳师直播.mp4" },
				opt2);
			if (miss.Count != 1 || miss[0].To != "2026-03-21 阴阳师直播.mp4") {
				Err("FAIL: 旧式不匹配时应保持原名 " + (miss.Count == 0 ? "(空)" : miss[0].To));
				bad++;
			}
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		finally {
			try { Directory.Delete(dir, true); } catch { }
		}
		Out(bad == 0 ? "=== OK：批量重命名 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testhash() {
		Out("=== 校验哈希 --test-hash ===");
		var bad = 0;
		var path = Path.Combine(Path.GetTempPath(), "sk_hash_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bin");
		try {
			File.WriteAllBytes(path, Encoding.UTF8.GetBytes("ScreenKit"));
			var row = HashTool.Compute(path, CancellationToken.None);
			Out($"md5={row.Md5} sha256={row.Sha256}");
			if (row.Sha256.Length != 64 || row.Md5.Length != 32) {
				Err("FAIL: 哈希长度");
				bad++;
			}
			if (HashTool.MatchKind(row, row.Sha256) != "SHA-256") {
				Err("FAIL: 比对 SHA-256");
				bad++;
			}
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		finally {
			try { File.Delete(path); } catch { }
		}
		Out(bad == 0 ? "=== OK：校验哈希 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testtexttool() {
		Out("=== 文本小工具 --test-texttool ===");
		var bad = 0;
		try {
			var s = "你好 ABC";
			var b64 = TextTools.Base64Enc(s);
			if (TextTools.Base64Dec(b64) != s) {
				Err("FAIL: base64 往返");
				bad++;
			}
			var hex = TextTools.GbkHex(s);
			if (TextTools.FromGbkHex(hex) != s) {
				Err("FAIL: gbk hex 往返 " + hex);
				bad++;
			}
			if (TextTools.UrlDec(TextTools.UrlEnc("a b")) != "a b") {
				Err("FAIL: url");
				bad++;
			}
			var st = TextTools.Stats("a\nb");
			if (st.Lines != 2 || st.Chars != 3) {
				Err($"FAIL: stats lines={st.Lines} chars={st.Chars}");
				bad++;
			}
			var pretty = TextTools.JsonPretty("{\"a\":1,\"v\":[[0,\"k\",true,null]]}");
			Out("json pretty:\n" + pretty);
			if (pretty.IndexOf("[0,\"k\",true,null]", StringComparison.Ordinal) < 0
				|| pretty.IndexOf("\n\t\"v\":[\n", StringComparison.Ordinal) < 0) {
				Err("FAIL: json 短数组应在一行，外层展开");
				bad++;
			}
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：文本小工具 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testpwgen() {
		Out("=== 密码生成器 --test-pwgen ===");
		var bad = 0;
		try {
			var o = new PasswordOpts { Length = 16, Count = 8, Lower = true, Upper = true, Digit = true, Symbol = true, EachClass = true };
			var list = PasswordGen.Generate(o);
			if (list.Length != 8) {
				Err("FAIL: count");
				bad++;
			}
			var set = new HashSet<string>();
			foreach (var p in list) {
				Out(p);
				if (p.Length != 16) {
					Err("FAIL: length " + p);
					bad++;
				}
				if (!p.Any(char.IsLower) || !p.Any(char.IsUpper) || !p.Any(char.IsDigit)) {
					Err("FAIL: missing class " + p);
					bad++;
				}
				if (!set.Add(p)) {
					Err("FAIL: duplicate " + p);
					bad++;
				}
			}
			o.NoAmbiguous = true;
			o.Count = 1;
			var one = PasswordGen.One(o);
			if (one.IndexOfAny("0OIl1o".ToCharArray()) >= 0) {
				Err("FAIL: ambiguous " + one);
				bad++;
			}
			else Out("no-amb OK " + one);
			try {
				PasswordGen.One(new PasswordOpts { Lower = false, Upper = false, Digit = false, Symbol = false });
				Err("FAIL: empty charset should throw");
				bad++;
			}
			catch (InvalidOperationException) { Out("empty charset OK"); }
			var parsed = WordLex.Parse(
				"```json\n[{\"lang\":\"zh\",\"native\":\"密码\",\"latin\":\"mi ma\"}," +
				"{\"lang\":\"ja\",\"native\":\"パスワード\",\"latin\":\"pasuwaado\"}," +
				"{\"lang\":\"ko\",\"native\":\"비밀번호\",\"latin\":\"bimilbeonho\"}]\n```");
			if (parsed.Count != 3 || parsed[0].Code != "zh" || parsed[1].Latin != "pasuwaado"
				|| parsed[2].Native != "비밀번호") {
				Err("FAIL: WordLex.Parse " + string.Join(",", parsed.Select(r => r.Code + "=" + r.Latin)));
				bad++;
			}
			else Out("WordLex.Parse OK");
			var ancient = WordLex.Parse(
				"[{\"lang\":\"wenyan\",\"native\":\"密語\",\"latin\":\"mi yu\"}," +
				"{\"lang\":\"greek\",\"native\":\"κρυπτός\",\"latin\":\"kryptos\"}]");
			if (ancient.Count != 2 || ancient[0].Code != "lzh" || ancient[1].Code != "grc"
				|| ancient[0].Latin != "mi yu") {
				Err("FAIL: WordLex.ancient " + string.Join(",", ancient.Select(r => r.Code + "=" + r.Latin)));
				bad++;
			}
			else Out("WordLex.ancient OK");
			var vars = PasswordVariant.Parse(
				"```json\n[{\"pw\":\"Mima#2024\",\"note\":\"拼音加年份\"}," +
				"{\"password\":\"pasuwaado!\",\"note\":\"日语罗马字\"}," +
				"{\"pw\":\"BlueHorse#99\",\"note\":\"same as seed\"}]\n```",
				"BlueHorse#99");
			if (vars.Count != 2 || vars[0].Password != "Mima#2024" || vars[1].Password != "pasuwaado!"
				|| vars[0].Note != "拼音加年份") {
				Err("FAIL: PasswordVariant.Parse " +
					string.Join(",", vars.Select(r => r.Password)));
				bad++;
			}
			else Out("PasswordVariant.Parse OK");
			var wrap = PasswordVariant.Parse(
				"{\"variants\":[{\"pw\":\"a\",\"note\":\"x\"},{\"pw\":\"a\",\"note\":\"dup\"}," +
				"{\"pw\":\"\",\"note\":\"empty\"},{\"variant\":\"b_c\",\"how\":\"join\"}]}");
			if (wrap.Count != 2 || wrap[0].Password != "a" || wrap[1].Password != "b_c"
				|| wrap[1].Note != "join") {
				Err("FAIL: PasswordVariant.wrap " +
					string.Join(",", wrap.Select(r => r.Password + "=" + r.Note)));
				bad++;
			}
			else Out("PasswordVariant.wrap OK");
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：密码生成器 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testnettool() {
		Out("=== 网络工具 --test-nettool ===");
		var bad = 0;
		try {
			var dns = Task.Run(() => NetTools.Resolve("127.0.0.1", CancellationToken.None)).GetAwaiter().GetResult();
			Out(dns);
			if (dns.IndexOf("127.0.0.1", StringComparison.Ordinal) < 0) {
				Err("FAIL: 127.0.0.1 解析");
				bad++;
			}
			var lines = new List<string>();
			Task.Run(() => NetTools.Ping("127.0.0.1", 1, 2000, s => lines.Add(s), CancellationToken.None))
				.GetAwaiter().GetResult();
			Out(string.Join("\n", lines));
			if (!lines.Any(s => s.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0)) {
				Err("FAIL: ping 127.0.0.1");
				bad++;
			}
			var osm = NetTools.MapOsm(39.9087, 116.3975);
			var amap = NetTools.MapAmap(39.9087, 116.3975);
			var london = NetTools.MapAmap(51.5074, -0.1278);
			Out(osm);
			Out(amap);
			if (osm.IndexOf("openstreetmap.org", StringComparison.Ordinal) < 0
				|| osm.IndexOf("39.9087", StringComparison.Ordinal) < 0
				|| osm.IndexOf("116.3975", StringComparison.Ordinal) < 0) {
				Err("FAIL: OpenStreetMap 链接");
				bad++;
			}
			if (amap.IndexOf("uri.amap.com", StringComparison.Ordinal) < 0
				|| amap.IndexOf("116.3975", StringComparison.Ordinal) >= 0) {
				Err("FAIL: 高德国内坐标应偏离 WGS84");
				bad++;
			}
			if (london.IndexOf("51.5074", StringComparison.Ordinal) < 0
				|| london.IndexOf("-0.1278", StringComparison.Ordinal) < 0) {
				Err("FAIL: 境外高德应保持 WGS84");
				bad++;
			}
			var geo = new List<string>();
			using (var geoCts = new CancellationTokenSource(25000)) {
				Task.Run(() => NetTools.Geolocate(s => geo.Add(s), geoCts.Token)).GetAwaiter().GetResult();
			}
			Out(string.Join("\n", geo));
			if (!geo.Any(s => s.StartsWith("权限:", StringComparison.Ordinal))) {
				Err("FAIL: 系统定位无权限行");
				bad++;
			}
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：网络工具 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testzhconv() {
		Out("=== 简繁转换 --test-zhconv ===");
		var bad = 0;
		try {
			var trad = ZhConvert.ToTraditional("国发");
			var simp = ZhConvert.ToSimplified(trad);
			Out("trad=" + trad);
			Out("simp=" + simp);
			if (trad != "國發") {
				Err("FAIL: 国发 未转为 國發");
				bad++;
			}
			if (simp.IndexOf('国') < 0) {
				Err("FAIL: 繁体未转回 国");
				bad++;
			}
			var soft = ZhConvert.ToTraditional("软件");
			Out("软件=" + soft);
			if (soft != "軟件") {
				Err("FAIL: 软件 未转为 軟件");
				bad++;
			}
			bad += testzhconvhttp();
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：简繁转换 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testzhconvhttp() {
		HttpOcrServer srv = null;
		try {
			srv = new HttpOcrServer(() => new OcrOptions(), new OcrRunner());
			var port = 0;
			for (var p = 18770; p <= 18774; p++) {
				try {
					srv.Start("127.0.0.1", p);
					port = p;
					break;
				}
				catch (Exception ex) {
					Out($"bind {p} fail: {ex.Message}");
				}
			}
			if (port == 0) {
				Err("FAIL: 无法绑定简繁 HTTP");
				return 1;
			}
			var url = $"http://127.0.0.1:{port}/api/zhconv?text={Uri.EscapeDataString("软件")}&to=trad";
			var json = Task.Run(() => new HttpClient().GetStringAsync(url).GetAwaiter().GetResult()).GetAwaiter().GetResult();
			Out("http " + json);
			if (json.IndexOf("軟件", StringComparison.Ordinal) < 0) {
				Err("FAIL: HTTP 查询串未按 UTF-8 解码");
				return 1;
			}
			var home = Task.Run(() => new HttpClient().GetStringAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult()).GetAwaiter().GetResult();
			if (home == null || home.IndexOf("简繁转换", StringComparison.Ordinal) < 0
				|| home.IndexOf("二维码", StringComparison.Ordinal) < 0
				|| home.IndexOf("文字识别", StringComparison.Ordinal) < 0
				|| home.IndexOf("语音合成", StringComparison.Ordinal) < 0
				|| home.IndexOf("语音识别", StringComparison.Ordinal) < 0
				|| home.IndexOf("id=\"tool-q\"", StringComparison.Ordinal) < 0
				|| home.IndexOf("data-cat=\"all\"", StringComparison.Ordinal) < 0) {
				Err("FAIL: GET / 不是工具页");
				return 1;
			}
			var modelsUrl = $"http://127.0.0.1:{port}/api/ocr/models";
			var models = Task.Run(() => new HttpClient().GetStringAsync(modelsUrl).GetAwaiter().GetResult()).GetAwaiter().GetResult();
			Out("ocr-models " + (models == null ? "" : models.Substring(0, Math.Min(180, models.Length))));
			if (models == null || models.IndexOf("winocr", StringComparison.Ordinal) < 0) {
				Err("FAIL: /api/ocr/models 未列出 Windows OCR");
				return 1;
			}
			var textUrl = $"http://127.0.0.1:{port}/api/text";
			var textBody = new StringContent("{\"text\":\"hi\",\"op\":\"b64enc\"}", Encoding.UTF8, "application/json");
			var textJson = Task.Run(() => new HttpClient().PostAsync(textUrl, textBody).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult()).GetAwaiter().GetResult();
			Out("text " + textJson);
			if (textJson == null || textJson.IndexOf("aGk=", StringComparison.Ordinal) < 0) {
				Err("FAIL: /api/text 未返回 Base64");
				return 1;
			}
			var qrUrl = $"http://127.0.0.1:{port}/api/qrmake";
			var qrBody = new StringContent("{\"text\":\"hello\",\"format\":\"qr\",\"encoding\":\"utf8\"}", Encoding.UTF8, "application/json");
			var qrJson = Task.Run(() => new HttpClient().PostAsync(qrUrl, qrBody).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult()).GetAwaiter().GetResult();
			var pngAt = qrJson == null ? -1 : qrJson.IndexOf("iVBORw0KGgo", StringComparison.Ordinal);
			if (pngAt < 0) {
				Err("FAIL: /api/qrmake 未返回 PNG");
				return 1;
			}
			var pngEnd = qrJson.IndexOf('"', pngAt);
			var png = qrJson.Substring(pngAt, pngEnd - pngAt);
			var scanUrl = $"http://127.0.0.1:{port}/api/qrscan";
			var scanBody = new StringContent($"{{\"base64\":\"{png}\",\"format\":\"text\"}}", Encoding.UTF8, "application/json");
			var scanJson = Task.Run(() => new HttpClient().PostAsync(scanUrl, scanBody).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult()).GetAwaiter().GetResult();
			Out("qrscan " + scanJson);
			if (scanJson == null || scanJson.IndexOf("hello", StringComparison.Ordinal) < 0) {
				Err("FAIL: /api/qrscan 未读出 hello");
				return 1;
			}
			var stamp = SendFileWebPages.BootStamp;
			if (string.IsNullOrEmpty(stamp)
				|| home.IndexOf("/sk/tools.js?" + stamp, StringComparison.Ordinal) < 0
				|| home.IndexOf("/sk/tools.css?" + stamp, StringComparison.Ordinal) < 0) {
				Err("FAIL: 工具页样式或脚本没有本次启动版本");
				return 1;
			}
			using (var css = Task.Run(() => new HttpClient().GetAsync($"http://127.0.0.1:{port}/sk/tools.css")).GetAwaiter().GetResult()) {
				var cc = css.Headers.CacheControl;
				if (cc == null || cc.MaxAge != TimeSpan.FromHours(1)) {
					Err("FAIL: 工具页样式缓存不是 1 小时");
					return 1;
				}
			}
			if (home.IndexOf("/sk/fa.css?", StringComparison.Ordinal) < 0) {
				Err("FAIL: 工具页没有 Font Awesome");
				return 1;
			}
			using (var font = Task.Run(() => new HttpClient().GetAsync($"http://127.0.0.1:{port}/sk/fa-solid-900.woff2")).GetAwaiter().GetResult()) {
				if (!font.IsSuccessStatusCode || font.Content.Headers.ContentLength.GetValueOrDefault() < 1000) {
					Err("FAIL: Font Awesome 字体");
					return 1;
				}
			}
			using (var page = Task.Run(() => new HttpClient().GetAsync($"http://127.0.0.1:{port}/")).GetAwaiter().GetResult()) {
				var cc = page.Headers.CacheControl;
				if (cc == null || !cc.NoStore) {
					Err("FAIL: 工具页不应缓存");
					return 1;
				}
			}
			return 0;
		}
		catch (Exception ex) {
			Err("FAIL: HTTP 简繁 " + ex.Message);
			return 1;
		}
		finally {
			try { srv?.Stop(); } catch { }
			try { srv?.Dispose(); } catch { }
		}
	}

	static int testwincal() {
		Out("=== 历法 --test-wincal ===");
		var bad = 0;
		try {
			var cny = WinCal.Query(new DateTime(2024, 2, 10), Windows.Globalization.CalendarIdentifiers.ChineseLunar, "zh-CN");
			Out($"cny era={cny.Era} year={cny.Year} month={cny.Month} day={cny.Day} ganzhi={cny.Ganzhi} leap={cny.LeapMonth}");
			if (cny.Ganzhi != "甲辰") {
				Err("FAIL: 2024-02-10 干支不是甲辰");
				bad++;
			}
			if (cny.Month.IndexOf('正') < 0 || cny.Day != "初一" || cny.LeapMonth) {
				Err("FAIL: 2024-02-10 不是正月初一");
				bad++;
			}
			var leap = WinCal.Query(new DateTime(2023, 3, 22), Windows.Globalization.CalendarIdentifiers.ChineseLunar, "zh-CN");
			Out($"leap month={leap.Month} day={leap.Day} leap={leap.LeapMonth} months={leap.MonthCount}");
			if (!leap.LeapMonth || leap.Month != "闰二月" || leap.Day != "初一") {
				Err("FAIL: 2023-03-22 不是闰二月初一");
				bad++;
			}
			var greg = WinCal.Query(new DateTime(2024, 2, 10), Windows.Globalization.CalendarIdentifiers.Gregorian, "zh-CN");
			Out($"greg year={greg.YearNum} month={greg.MonthNum} day={greg.DayNum}");
			if (greg.YearNum != 2024 || greg.MonthNum != 2 || greg.DayNum != 10) {
				Err("FAIL: 公历日期不对");
				bad++;
			}
			bad += testwincalmta();
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：历法 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testwincalmta() {
		Exception err = null;
		string month = "";
		var t = new Thread(() => {
			try {
				var info = WinCal.Query(new DateTime(2024, 2, 10), Windows.Globalization.CalendarIdentifiers.ChineseLunar, "zh-CN");
				month = info.Month ?? "";
				if (info.Ganzhi != "甲辰") err = new InvalidOperationException("MTA 干支不是甲辰");
			}
			catch (Exception ex) { err = ex; }
		});
		t.IsBackground = true;
		t.SetApartmentState(ApartmentState.MTA);
		t.Start();
		t.Join();
		Out("mta month=" + month);
		if (err != null) {
			Err("FAIL: MTA 历法 " + err.Message);
			return 1;
		}
		return 0;
	}

	static int testjpyomi() {
		Out("=== 日文注音 --test-jpyomi ===");
		var bad = 0;
		try {
			var res = JpYomi.Convert("東京は晴れです", false);
			Out("ruby=" + res.Ruby);
			Out("yomi=" + res.Yomi);
			var all = (res.Ruby ?? "") + (res.Yomi ?? "");
			if (all.IndexOf("とうきょう", StringComparison.Ordinal) < 0
				&& all.IndexOf("トウキョウ", StringComparison.Ordinal) < 0) {
				Err("FAIL: 東京 没有读音");
				bad++;
			}
			Exception mtaErr = null;
			string mtaYomi = "";
			var t = new Thread(() => {
				try {
					var mta = JpYomi.Convert("東京は晴れです", false);
					mtaYomi = mta.Yomi ?? "";
				}
				catch (Exception ex) { mtaErr = ex; }
			});
			t.IsBackground = true;
			t.SetApartmentState(ApartmentState.MTA);
			t.Start();
			t.Join();
			Out("mta yomi=" + mtaYomi);
			if (mtaErr != null || mtaYomi.IndexOf("とうきょう", StringComparison.Ordinal) < 0) {
				Err("FAIL: MTA 日文注音 " + (mtaErr != null ? mtaErr.Message : mtaYomi));
				bad++;
			}
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：日文注音 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testmem() {
		Out("=== 内存占用 --test-mem ===");
		var code = MemUsage.SelfTest();
		var p = MemUsage.Read();
		Out($"ws={p.WorkingSet} priv={p.PrivateBytes} gc={p.GcBytes} self={code}");
		if (code != 0) Err("FAIL self=" + code);
		return code;
	}

	static int testortlazy() {
		Out("=== ORT 按需加载 --test-ort-lazy ===");
		var code = CudaBootstrap.TestLazy(out var detail);
		Out(detail ?? "");
		if (code != 0) Err("FAIL code=" + code);
		else Out("=== OK：启动未加载，使用后可再建会话 ===");
		return code;
	}

	static int testortrelease() {
		Out("=== ORT 释放 --test-ort-release ===");
		var code = CudaBootstrap.TestRelease(out var detail);
		Out(detail ?? "");
		if (code != 0) Err("FAIL code=" + code);
		else Out("=== OK：CUDA 库已卸，会话仍可建 ===");
		return code;
	}

	static int testwintts() {
		Out("=== Windows 语音提权 --test-win-tts ===");
		var add = WinTtsPack.BuildElevateScript(true, new[] { "ja-JP" }, "C:\\tmp\\a.log");
		var remove = WinTtsPack.BuildElevateScript(false, new[] { "ko-KR" }, "C:\\tmp\\a.log");
		if (add.IndexOf("/Add-Capability", StringComparison.Ordinal) < 0
			|| add.IndexOf("Language.TextToSpeech~~~ja-JP~0.0.1.0", StringComparison.Ordinal) < 0
			|| add.IndexOf("BEGIN ja-JP", StringComparison.Ordinal) < 0) {
			Err("FAIL: 安装脚本缺少 ja-JP");
			Out(add);
			return 1;
		}
		if (remove.IndexOf("/Remove-Capability", StringComparison.Ordinal) < 0
			|| remove.IndexOf("Language.TextToSpeech~~~ko-KR~0.0.1.0", StringComparison.Ordinal) < 0
			|| remove.IndexOf("/Add-Capability", StringComparison.Ordinal) >= 0) {
			Err("FAIL: 卸载脚本不是 Remove");
			Out(remove);
			return 1;
		}
		var outer = WinTtsPack.OuterScript("C:\\tmp\\wintts.ps1", "C:\\tmp\\wintts.code");
		if (outer.IndexOf("Start-Process", StringComparison.Ordinal) < 0
			|| outer.IndexOf("-Verb RunAs", StringComparison.Ordinal) < 0
			|| outer.IndexOf("-File", StringComparison.Ordinal) < 0) {
			Err("FAIL: 外层脚本没有 RunAs");
			Out(outer);
			return 1;
		}
		var launch = WinTtsPack.LaunchArgs(outer);
		const string head = "/c start \"ScreenKit\" /min /wait powershell.exe ";
		if (!launch.StartsWith(head, StringComparison.Ordinal)
			|| launch.IndexOf("-EncodedCommand ", StringComparison.Ordinal) < 0) {
			Err("FAIL: 启动参数不是 start /wait");
			Out(launch);
			return 1;
		}
		var enc = launch.Substring(launch.IndexOf("-EncodedCommand ", StringComparison.Ordinal) + "-EncodedCommand ".Length);
		string decoded;
		try { decoded = Encoding.Unicode.GetString(Convert.FromBase64String(enc.Trim())); }
		catch (Exception ex) {
			Err("FAIL: EncodedCommand " + ex.Message);
			return 1;
		}
		if (decoded != outer) {
			Err("FAIL: 编码后的脚本对不上");
			return 1;
		}
		var query = WinTtsPack.BuildQueryScript("C:\\tmp\\a.log");
		if (query.IndexOf("Get-WindowsCapability", StringComparison.Ordinal) < 0
			|| query.IndexOf("Language.TextToSpeech", StringComparison.Ordinal) < 0
			|| query.IndexOf("AllVoices", StringComparison.Ordinal) < 0
			|| query.IndexOf("VOICE|", StringComparison.Ordinal) < 0) {
			Err("FAIL: 查询脚本缺少语音包或发音人");
			Out(query);
			return 1;
		}
		var asrAdd = WinTtsPack.BuildElevateScript(
			true, new[] { "en-US" }, "C:\\tmp\\a.log", WinSpeechPackKind.Asr);
		var asrQuery = WinTtsPack.BuildQueryScript("C:\\tmp\\a.log", WinSpeechPackKind.Asr);
		if (asrAdd.IndexOf("Language.Speech~~~en-US~0.0.1.0", StringComparison.Ordinal) < 0
			|| asrAdd.IndexOf("ScreenKit Windows ASR", StringComparison.Ordinal) < 0
			|| asrQuery.IndexOf("Language.Speech*", StringComparison.Ordinal) < 0
			|| asrQuery.IndexOf("AllVoices", StringComparison.Ordinal) >= 0) {
			Err("FAIL: Windows ASR 安装或查询脚本不正确");
			Out(asrAdd);
			Out(asrQuery);
			return 1;
		}
		var ocrAdd = WinTtsPack.BuildElevateScript(
			true, new[] { "ja-JP" }, "C:\\tmp\\a.log", WinSpeechPackKind.Ocr);
		var ocrQuery = WinTtsPack.BuildQueryScript("C:\\tmp\\a.log", WinSpeechPackKind.Ocr);
		if (ocrAdd.IndexOf("Language.OCR~~~ja-JP~0.0.1.0", StringComparison.Ordinal) < 0
			|| ocrAdd.IndexOf("ScreenKit Windows OCR", StringComparison.Ordinal) < 0
			|| ocrQuery.IndexOf("Language.OCR*", StringComparison.Ordinal) < 0
			|| ocrQuery.IndexOf("AllVoices", StringComparison.Ordinal) >= 0) {
			Err("FAIL: Windows OCR 安装或查询脚本不正确");
			Out(ocrAdd);
			Out(ocrQuery);
			return 1;
		}
		var allQuery = WinTtsPack.BuildAllQueryScript("C:\\tmp\\a.log");
		if (allQuery.IndexOf("Language.OCR*", StringComparison.Ordinal) < 0
			|| allQuery.IndexOf("Language.TextToSpeech*", StringComparison.Ordinal) < 0
			|| allQuery.IndexOf("Language.Speech*", StringComparison.Ordinal) < 0
			|| allQuery.IndexOf("AllVoices", StringComparison.Ordinal) < 0) {
			Err("FAIL: 合并查询脚本没有同时查询 OCR/TTS/ASR");
			Out(allQuery);
			return 1;
		}
		var mixed = WinTtsPack.BuildElevateScript(true, new[] {
			new WinPackRequest { Kind = WinSpeechPackKind.Ocr, Culture = "ja-JP" },
			new WinPackRequest { Kind = WinSpeechPackKind.Tts, Culture = "ko-KR" },
			new WinPackRequest { Kind = WinSpeechPackKind.Asr, Culture = "en-US" },
		}, "C:\\tmp\\a.log");
		if (mixed.IndexOf("Language.OCR~~~ja-JP~0.0.1.0", StringComparison.Ordinal) < 0
			|| mixed.IndexOf("Language.TextToSpeech~~~ko-KR~0.0.1.0", StringComparison.Ordinal) < 0
			|| mixed.IndexOf("Language.Speech~~~en-US~0.0.1.0", StringComparison.Ordinal) < 0
			|| mixed.IndexOf("BEGIN Ocr|ja-JP", StringComparison.Ordinal) < 0
			|| mixed.IndexOf("BEGIN Tts|ko-KR", StringComparison.Ordinal) < 0
			|| mixed.IndexOf("BEGIN Asr|en-US", StringComparison.Ordinal) < 0
			|| mixed.IndexOf("Get-WindowsCapability -Online -Name 'Language.OCR~~~ja-JP~0.0.1.0'", StringComparison.Ordinal) < 0
			|| mixed.IndexOf("Get-WindowsCapability -Online -Name 'Language.TextToSpeech~~~ko-KR~0.0.1.0'", StringComparison.Ordinal) < 0
			|| mixed.IndexOf("Get-WindowsCapability -Online -Name 'Language.Speech~~~en-US~0.0.1.0'", StringComparison.Ordinal) < 0) {
			Err("FAIL: 混合安装脚本没有在一次提权中包含 OCR/TTS/ASR，或没有复查这些功能");
			Out(mixed);
			return 1;
		}
		var named = WinTtsPack.BuildNamedQueryBody(new[] {
			new WinPackRequest { Kind = WinSpeechPackKind.Ocr, Culture = "ja-JP" },
		});
		if (named.IndexOf("Get-WindowsCapability -Online -Name 'Language.OCR~~~ja-JP~0.0.1.0'", StringComparison.Ordinal) < 0
			|| named.IndexOf("Language.OCR*", StringComparison.Ordinal) >= 0
			|| named.IndexOf("Language.Speech*", StringComparison.Ordinal) >= 0
			|| named.IndexOf("Language.TextToSpeech*", StringComparison.Ordinal) >= 0) {
			Err("FAIL: 单项复查脚本不是精确功能名");
			Out(named);
			return 1;
		}
		var parsed = WinTtsPack.ParseAllQuery("STATE Language.OCR~~~ja-JP~0.0.1.0|Installed\r\nEXIT Ocr|ja-JP 0\r\n");
		if (!parsed.States.TryGetValue(WinSpeechPackKind.Ocr, out var ocrStates)
			|| !ocrStates.TryGetValue("ja-JP", out var ocrState)
			|| !string.Equals(ocrState, "Installed", StringComparison.OrdinalIgnoreCase)) {
			Err("FAIL: 复查结果没有解析到 ja-JP OCR");
			return 1;
		}
		if (InstallFeaturesWindow.WinLanguageRank("zh-CN") != 0
			|| InstallFeaturesWindow.WinLanguageRank("en-US") != 1
			|| InstallFeaturesWindow.WinLanguageRank("ja-JP") != 2
			|| InstallFeaturesWindow.WinLanguageRank("ko-KR") != 3
			|| InstallFeaturesWindow.WinLanguageRank("fr-FR") != 4) {
			Err("FAIL: Windows 语言排序不是中、英、日、韩、其它");
			return 1;
		}
		Out("admin=" + WinTtsPack.IsAdmin());
		Out("=== OK：TTS/ASR/OCR 合并查询与混合操作只需一次 RunAs ===");
		return 0;
	}

	static int testwintop() {
		Out("=== 窗口管理 --test-wintop ===");
		var bad = 0;
		var h = IntPtr.Zero;
		try {
			if (!WinTop.TryParse("0x10", out var parsed) || parsed.ToInt64() != 0x10) {
				Err("FAIL: parse 0x10");
				bad++;
			}
			if (!WinTop.TryParse("16", out var dec) || dec.ToInt64() != 16) {
				Err("FAIL: parse decimal");
				bad++;
			}
			if (WinTop.SetTop(IntPtr.Zero, true, out _)) {
				Err("FAIL: zero hwnd");
				bad++;
			}
			h = WinTop.CreateProbe();
			if (h == IntPtr.Zero) {
				Err("FAIL: CreateProbe " + Marshal.GetLastWin32Error());
				bad++;
			}
			else {
				Out("probe " + h.ToInt64().ToString("X"));
				if (WinTop.IsTop(h)) {
					Err("FAIL: probe starts topmost");
					bad++;
				}
				if (!WinTop.SetTop(h, true, out var e1) || !WinTop.IsTop(h)) {
					Err("FAIL: set top err=" + e1);
					bad++;
				}
				if (!WinTop.SetTop(h, false, out var e2) || WinTop.IsTop(h)) {
					Err("FAIL: clear top err=" + e2);
					bad++;
				}
			}
			var list = WinTop.List();
			Out("windows " + list.Count);
			if (list.Count == 0) {
				Err("FAIL: empty list");
				bad++;
			}
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		finally {
			WinTop.DestroyProbe(h);
		}
		Out(bad == 0 ? "=== OK：窗口管理 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testcast() {
		Out("=== 投屏 --test-cast ===");
		var bad = 0;
		try {
			var hello = CastProto.PackJson(new { cmd = "hello", name = "t", w = 1280, h = 720 });
			if (hello == null || hello.Length < 9) {
				Err("FAIL: PackJson");
				bad++;
			}
			using (var ms = new MemoryStream(hello)) {
				if (!CastProto.TryRead(ms, out var type, out var payload) || type != CastProto.T_JSON) {
					Err("FAIL: TryRead json");
					bad++;
				}
				else {
					var o = CastProto.ParseJson(payload);
					if (CastProto.Jstr(o, "cmd") != "hello" || CastProto.Jint(o, "w") != 1280) {
						Err("FAIL: ParseJson hello");
						bad++;
					}
					else Out("pack/unpack hello ok");
				}
			}
			var nal = new byte[] { 0, 0, 0, 1, 0x65 };
			var vp = CastProto.Pack(CastProto.T_VIDEO, nal);
			using (var ms = new MemoryStream(vp)) {
				if (!CastProto.TryRead(ms, out var type, out var payload) || type != CastProto.T_VIDEO
					|| payload == null || payload.Length != nal.Length) {
					Err("FAIL: TryRead video");
					bad++;
				}
				else Out("pack/unpack video ok");
			}
			var q = CastQuality.ByName("均衡 720p");
			q.Fit(1080, 2400, out var ow, out var oh);
			Out($"Fit 1080x2400 → {ow}x{oh}");
			if (ow % 16 != 0 || oh % 16 != 0 || Math.Min(ow, oh) != 720) {
				Err("FAIL: Fit 窄边 720");
				bad++;
			}
			q.Fit(1920, 1080, out ow, out oh);
			Out($"Fit 1920x1080 → {ow}x{oh}");
			if (ow > 1920 || oh > 1080) {
				Err("FAIL: Fit 不放大");
				bad++;
			}
			if (FfmpegLoader.TryInit(out var ferr)) {
				var enc = new CastVideoEncoder(64, 64, CastQuality.Presets[0]);
				try {
					var bgra = new byte[64 * 64 * 4];
					for (int i = 0; i < bgra.Length; i += 4) { bgra[i] = 80; bgra[i + 1] = 40; bgra[i + 2] = 200; bgra[i + 3] = 255; }
					var packed = enc.EncodeBgra(bgra, 64 * 4);
					Out(packed != null && packed.Length > 0
						? $"x264 encode {packed.Length} bytes ok"
						: "x264 encode returned empty (may need more frames)");
				}
				finally { enc.Dispose(); }
			}
			else Out("skip encode: " + (ferr ?? "no ffmpeg64"));
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：投屏 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int testcastrecv() {
		Out("=== 投屏 --test-cast-recv ===");
		var hello = 0;
		var srv = new CastRecvSrv();
		srv.Log = Out;
		srv.OnHello = (_, _) => Interlocked.Increment(ref hello);
		HttpListener http = null;
		ClientWebSocket cws = null;
		try {
			srv.Start();
			if (!srv.Running) {
				Err("FAIL: 接收未启动");
				return 1;
			}
			Out(srv.BindText);
			var port = 18765;
			http = new HttpListener();
			http.Prefixes.Add($"http://127.0.0.1:{port}/");
			http.Start();
			_ = Task.Run(() => {
				var ctx = http.GetContext();
				if (!ctx.Request.IsWebSocketRequest) {
					Err("FAIL: 非 WebSocket 请求");
					return;
				}
				var wsctx = ctx.AcceptWebSocketAsync(null).GetAwaiter().GetResult();
				srv.AttachStream(new CastWsStream(wsctx.WebSocket), "test");
			});
			cws = new ClientWebSocket();
			var uri = new Uri($"ws://127.0.0.1:{port}{CastProto.WS_PATH}");
			if (!cws.ConnectAsync(uri, CancellationToken.None).Wait(4000)
				|| cws.State != WebSocketState.Open) {
				Err("FAIL: WebSocket 连不上 " + uri);
				return 1;
			}
			var buf = CastProto.PackJson(new { cmd = "hello", name = "t", w = 64, h = 64, via = "wifi" });
			cws.SendAsync(new ArraySegment<byte>(buf), WebSocketMessageType.Binary, true,
				CancellationToken.None).Wait(2000);
			var rbuf = new byte[65536];
			var rec = cws.ReceiveAsync(new ArraySegment<byte>(rbuf), CancellationToken.None);
			if (!rec.Wait(4000)) {
				Err("FAIL: 未收到 hello 应答");
				return 1;
			}
			var got = rec.Result;
			if (got.MessageType != WebSocketMessageType.Binary || got.Count < 9) {
				Err("FAIL: hello 应答无效");
				return 1;
			}
			using (var ms = new MemoryStream(rbuf, 0, got.Count)) {
				if (!CastProto.TryRead(ms, out var type, out var payload) || type != CastProto.T_JSON) {
					Err("FAIL: hello 应答不是 JSON 包");
					return 1;
				}
				if (CastProto.Jstr(CastProto.ParseJson(payload), "cmd") != "hello") {
					Err("FAIL: 应答 cmd 不是 hello");
					return 1;
				}
			}
			Out("hello 往返 ok");
			var t0 = Environment.TickCount;
			while (hello == 0 && unchecked(Environment.TickCount - t0) < 4000)
				Thread.Sleep(50);
			if (hello == 0) {
				Err("FAIL: 本机 /cast hello 未触发开窗路径");
				return 1;
			}
			Out("本机 hello 开窗路径 ok");
			Out("=== OK：投屏接收 ===");
			return 0;
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			return 1;
		}
		finally {
			try { cws?.Abort(); } catch { }
			try { http?.Abort(); } catch { }
			try { srv.Dispose(); } catch { }
		}
	}

	static int testaoa() {
		Out("=== 投屏 --test-aoa ===");
		CastUsbHost.Dump(Out);
		Out("=== OK：USB 枚举 ===");
		return 0;
	}

	static int testdict(string one) {
		var forms = DictDb.JaForms("其奴", "そいつ, そやつ, すやつ'out-dated or obsolete kana usage'");
		if (forms != "其奴, そいつ, そやつ") {
			Out("ja forms " + forms);
			return 6;
		}
		var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict.db");
		if (!DictDb.Init(path)) {
			if (DictDb.Error == "missing")
				Out("dict.db not found beside exe");
			else if (DictDb.Error == "sqlite")
				Out("e_sqlite3.dll is not installed. Install SQLite from Help → Install features.");
			else
				Out("dict open fail: " + DictDb.Error);
			return 2;
		}
		long len;
		using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			len = fs.Length;
		var ticks = File.GetLastWriteTimeUtc(path).Ticks;
		string[] qs = string.IsNullOrWhiteSpace(one) ? new[] { "学生", "hello" } : new[] { one };
		foreach (var q in qs) {
			var hits = DictDb.Search(q, "", 12);
			Out("q=" + q + " n=" + hits.Count);
			var shown = 0;
			foreach (var h in hits) {
				var label = h.Dict == "ja" ? DictDb.JaForms(h.Headword, h.Kanji) : h.Headword;
				Out("  " + h.Dict + " " + label + " | " + h.Preview);
				if (++shown >= 5) break;
			}
			if (hits.Count == 0) {
				Out("no hits");
				return 3;
			}
			var top = hits[0];
			var entry = DictDb.Get(top.Id);
			if (entry == null) {
				Out("get miss id=" + top.Id);
				return 4;
			}
			Out("  detail " + entry.Word + " zh=" + (entry.ZhSpeak().Length > 0 ? "yes" : "no"));
		}
		var phraseEntry = kophrases();
		if (phraseEntry == null) {
			Out("ko phrase miss");
			return 7;
		}
		var page = new DictWindow();
		typeof(DictWindow).GetMethod("filldetail", BindingFlags.Instance | BindingFlags.NonPublic)
			.Invoke(page, new object[] { phraseEntry });
		var phrases = 0;
		var spoken = 0;
		var zhSpoken = 0;
		var examples = 0;
		var exSpoken = 0;
		var lab = Loc.T("dict.lab.phrase") + " ";
		var zh = Loc.T("dict.lab.zh") + " ";
		foreach (var b in page.edetail.Document.Blocks) {
			if (b is not System.Windows.Documents.Paragraph p) continue;
			var text = new System.Windows.Documents.TextRange(p.ContentStart, p.ContentEnd).Text ?? "";
			var spk = hasspeak(p);
			if (text.StartsWith(lab, StringComparison.Ordinal)) {
				phrases++;
				if (spk) spoken++;
			}
			else if (text.StartsWith("· ", StringComparison.Ordinal)) {
				examples++;
				if (spk) exSpoken++;
			}
			else if (text.StartsWith(zh, StringComparison.Ordinal) && spk)
				zhSpoken++;
		}
		Out($"ko {phraseEntry.Word} phrases={phrases} speak={spoken} examples={examples} exSpeak={exSpoken} zhSpeak={zhSpoken}");
		if (phrases == 0 || spoken != phrases || zhSpoken != 0 || (examples > 0 && exSpoken != examples)) {
			Out("phrase speak mismatch");
			return 7;
		}
		DictEntry run = null;
		foreach (var h in DictDb.Search("run", "en", 8)) {
			if (h.Dict == "en" && h.Headword == "run") { run = DictDb.Get(h.Id); break; }
		}
		if (run == null) {
			Out("run miss");
			return 8;
		}
		var idioms = 0;
		var subs = 0;
		var sents = 0;
		var maxsent = 0;
		var come = false;
		foreach (var s in run.Senses) {
			if (s.Idiom.Length > 0) idioms++;
			if (s.Idiom == "come running") come = true;
			if (s.Sub) subs++;
			sents += s.Sentences.Count;
			if (s.Sentences.Count > maxsent) maxsent = s.Sentences.Count;
		}
		Out($"run senses={run.Senses.Count} idioms={idioms} subs={subs} sents={sents} maxsent={maxsent}");
		if (!come || idioms < 60 || run.Senses.Count < 170 || subs < 100 || sents < 100 || maxsent < 7) {
			Out("run truncated");
			return 8;
		}
		long len2;
		using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			len2 = fs.Length;
		if (len2 != len || File.GetLastWriteTimeUtc(path).Ticks != ticks) {
			Out("dict.db changed");
			return 5;
		}
		Out("dict ok readonly bytes=" + len);
		return 0;

		static DictEntry kophrases() {
			var hits = DictDb.Search("one", "ko", 80);
			foreach (var h in hits) {
				var e = DictDb.Get(h.Id);
				if (e == null) continue;
				foreach (var s in e.Senses) {
					if (s.Phrases.Count > 0) return e;
				}
			}
			return null;
		}

		static bool hasspeak(System.Windows.Documents.Paragraph p) {
			foreach (var inline in p.Inlines) {
				if (inline is not System.Windows.Documents.Hyperlink link) continue;
				var t = new System.Windows.Documents.TextRange(link.ContentStart, link.ContentEnd).Text ?? "";
				if (t.IndexOf('\uE767') >= 0) return true;
			}
			return false;
		}
	}

	static int testdicthost() {
		Out("=== 词典新窗口 --test-dict-host ===");
		if (Application.Current != null)
			Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		DictHostWindow win = null;
		try {
			win = new DictHostWindow("hello", () => new OcrOptions(), _ => { });
			win.Show();
		}
		catch (Exception ex) {
			Out("show " + ex);
			return 1;
		}
		var n = waithits(win, 8000);
		var title = win.Title ?? "";
		Out($"title={title} hits={n}");
		if (n <= 0 || title.IndexOf("hello", StringComparison.OrdinalIgnoreCase) < 0) {
			try { win.Close(); } catch { }
			Err("FAIL: 新词典窗口没有查出 hello");
			return 1;
		}
		var laid = false;
		var t0 = Environment.TickCount;
		while (Environment.TickCount - t0 < 8000) {
			pump();
			if (win.host.edetail.Document != null && win.host.edetail.Document.Blocks.Count > 0) {
				laid = true;
				break;
			}
		}
		if (!laid) {
			try { win.Close(); } catch { }
			Err("FAIL: 词典详情没有排版");
			return 3;
		}
		var startp = win.host.edetail.Document.ContentStart;
		var endp = startp;
		for (var i = 0; i < 240; i++) {
			var npos = endp.GetNextInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
			if (npos == null) break;
			endp = npos;
		}
		win.host.edetail.Selection.Select(startp, endp);
		typeof(DictWindow).GetMethod("openselpop", BindingFlags.Instance | BindingFlags.NonPublic)
			.Invoke(win.host, null);
		for (var i = 0; i < 6; i++) pump();
		var popOpen = win.host.psel.IsOpen;
		var overlap = win.host.PopOverlapsSel();
		Out($"selpop open={popOpen} overlap={overlap} y={win.host.psel.VerticalOffset:0}");
		if (!popOpen || overlap) {
			try { win.Close(); } catch { }
			Err("FAIL: 划词浮窗挡住选中文字");
			return 4;
		}
		win.host.psel.IsOpen = false;
		var dev = System.Windows.Input.Mouse.PrimaryDevice;
		if (dev == null) {
			try { win.Close(); } catch { }
			Err("FAIL: 没有鼠标设备，无法模拟双击选词");
			return 6;
		}
		var dbl = new System.Windows.Input.MouseButtonEventArgs(dev, Environment.TickCount, System.Windows.Input.MouseButton.Left) {
			RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent,
		};
		win.host.edetail.RaiseEvent(dbl);
		for (var i = 0; i < 6; i++) pump();
		var dblOpen = win.host.psel.IsOpen;
		Out($"dblclick open={dblOpen}");
		if (!dblOpen) {
			try { win.Close(); } catch { }
			Err("FAIL: 双击选词没有弹出划词浮窗");
			return 6;
		}
		var probe = new Window {
			Width = 240,
			Height = 140,
			Title = "dict-pop-off",
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
		};
		probe.Show();
		probe.Activate();
		for (var i = 0; i < 8; i++) pump();
		var hidden = !win.host.psel.IsOpen;
		Out($"away active={win.IsActive} hidden={hidden}");
		try { probe.Close(); } catch { }
		if (!hidden) {
			try { win.Close(); } catch { }
			Err("FAIL: 窗口失活后划词浮窗还在");
			return 5;
		}
		var box = win.host.esearch.Text ?? "";
		DictHostWindow other = null;
		try {
			typeof(DictWindow).GetMethod("openhit", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(win.host, new object[] { new DictSelRow { Word = "world", Id = 0 } });
			other = otherdict(win);
		}
		catch (Exception ex) {
			Out("openhit " + ex.Message);
		}
		var n2 = other == null ? 0 : waithits(other, 8000);
		var title2 = other == null ? "" : (other.Title ?? "");
		var stayed = (win.host.esearch.Text ?? "") == box && box.IndexOf("hello", StringComparison.OrdinalIgnoreCase) >= 0;
		Out($"sel=world title2={title2} hits2={n2} stayed={stayed}");
		try { if (other != null) other.Close(); } catch { }
		try { win.Close(); } catch { }
		if (other == null || n2 <= 0 || !stayed || title2.IndexOf("world", StringComparison.OrdinalIgnoreCase) < 0) {
			Err("FAIL: 划词词条没有在新窗口查询");
			return 2;
		}
		Out("=== OK：选词搜索与划词词条都打开词典窗口 ===");
		return 0;

		static void pump() {
			var frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
		}

		static int waithits(DictHostWindow d, int ms) {
			var start = Environment.TickCount;
			var hits = 0;
			while (Environment.TickCount - start < ms) {
				pump();
				hits = d.HitCount;
				if (hits > 0) break;
			}
			return hits;
		}

		static DictHostWindow otherdict(params DictHostWindow[] skip) {
			foreach (Window w in Application.Current.Windows) {
				if (w is not DictHostWindow d) continue;
				var known = false;
				if (skip != null) {
					foreach (var s in skip) {
						if (ReferenceEquals(s, d)) known = true;
					}
				}
				if (!known) return d;
			}
			return null;
		}
	}

	static int testdicttr() {
		Out("=== 划词翻译立即开始 --test-dict-tr ===");
		if (Application.Current != null)
			Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		TranslatePopupWindow win = null;
		try {
			win = new TranslatePopupWindow(() => new OcrOptions(), null);
			win.ShowText("hello");
		}
		catch (Exception ex) {
			Out("show " + ex);
			return 1;
		}
		var hint = Loc.T("tr.popup.hint");
		var start = Environment.TickCount;
		var status = "";
		while (Environment.TickCount - start < 2500) {
			var frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
			status = win.lbstatus.Text ?? "";
			if (status.Length > 0 && status != hint) break;
		}
		var src = win.esrc.Text ?? "";
		Out("src=[" + src + "] status=[" + status + "]");
		try { win.ForceClose(); } catch { }
		if (src != "hello") return 2;
		if (status.Length == 0 || status == hint) return 3;
		return 0;
	}

	static int testdictword() {
		string[][] rows = {
			new[] { "字", "字" },
			new[] { "中国", "中国" },
			new[] { "中华人民", "中华人民" },
			new[] { "中华人民共", "" },
			new[] { "a", "a" },
			new[] { "hello", "hello" },
			new[] { "don't", "don't" },
			new[] { "well-known", "well-known" },
			new[] { new string('b', 20), new string('b', 20) },
			new[] { new string('b', 21), "" },
			new[] { " hello ", "hello" },
			new[] { "「中国」", "中国" },
			new[] { "hello.", "hello" },
			new[] { "你好。", "你好" },
			new[] { "hello world", "" },
			new[] { "你好，世界", "" },
			new[] { "你好hello", "" },
			new[] { "123", "" },
			new[] { "こんにちは", "こんにちは" },
			new[] { "食べる", "食べる" },
			new[] { "안녕하세요", "안녕하세요" },
			new[] { new string('あ', 12), new string('あ', 12) },
			new[] { new string('あ', 13), "" },
			new[] { new string('한', 8), new string('한', 8) },
			new[] { new string('한', 9), "" },
			new[] { "", "" },
			new[] { "Ｈｅｌｌｏ", "Hello" },
		};
		var bad = 0;
		foreach (var row in rows) {
			var got = DictClip.Word(row[0]);
			if (got == row[1]) continue;
			Out("fail in=[" + row[0] + "] got=[" + got + "] want=[" + row[1] + "]");
			bad++;
		}
		string[][] heads = {
			new[] { "당번, 당番", "ko", "당번" },
			new[] { "食べる，たべる", "ja", "食べる" },
			new[] { "하나", "ko", "하나" },
			new[] { ",앞", "ko", ",앞" },
			new[] { "hello, world", "en", "hello, world" },
			new[] { "一，二", "zh", "一，二" },
		};
		foreach (var row in heads) {
			var got = DictWindow.SpeakHead(row[0], row[1]);
			if (got == row[2]) continue;
			Out("fail head in=[" + row[0] + "] got=[" + got + "] want=[" + row[2] + "]");
			bad++;
		}
		string[][] kana = {
			new[] { "破瓜", "はか", "", "はか" },
			new[] { "食べる", "喰べる, たべる", "", "たべる" },
			new[] { "テレビ", "", "", "テレビ" },
			new[] { "其奴", "そいつ, そやつ, すやつ'out-dated or obsolete kana usage'", "", "そいつ" },
			new[] { "々", "のま, ノマ", "", "のま" },
			new[] { "日本", "にほん, にっぽん", "", "にほん" },
			new[] { "こんにちは", "今日は", "", "こんにちは" },
		};
		foreach (var row in kana) {
			var got = DictDb.JaSpeak(row[0], row[1], row[2]);
			if (got == row[3]) continue;
			Out("fail ja in=[" + row[0] + "] got=[" + got + "] want=[" + row[3] + "]");
			bad++;
		}
		string[][] langs = {
			new[] { "女孩十六岁，青春期", "ja", "zh" },
			new[] { "to eat", "ja", "en" },
			new[] { "age 16 (of a girl), pubescence", "ja", "en" },
			new[] { "はか", "ja", "ja" },
			new[] { "食べる", "ja", "ja" },
			new[] { "破瓜", "ja", "zh" },
			new[] { "안녕하세요", "en", "ko" },
			new[] { "", "ja", "ja" },
			new[] { "123", "ko", "ko" },
			new[] { "はか", "zh", "zh" },
			new[] { "食べる", "ko", "zh" },
			new[] { "テレビ", "en", "en" },
			new[] { "hello はか", "zh", "en" },
		};
		foreach (var row in langs) {
			var got = DictWindow.TextLang(row[0], row[1]);
			if (got == row[2]) continue;
			Out("fail lang in=[" + row[0] + "] got=[" + got + "] want=[" + row[2] + "]");
			bad++;
		}
		if (bad != 0) return 1;
		Out("dict word ok n=" + rows.Length);
		return 0;
	}

	static int testselcopy() {
		System.Windows.Forms.Form form = null;
		System.Windows.Forms.TextBox box = null;
		var ready = new ManualResetEvent(false);
		Exception fail = null;
		var t = new Thread(() => {
			try {
				form = new System.Windows.Forms.Form {
					Text = "dictsel",
					Width = 320,
					Height = 80,
					StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
				};
				box = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill, Text = "hello" };
				form.Controls.Add(box);
				form.Shown += (_, _) => {
					try {
						var fg = GetForegroundWindow();
						var cur = GetCurrentThreadId();
						var fgThread = GetWindowThreadProcessId(fg, out _);
						var attached = fgThread != 0 && fgThread != cur && AttachThreadInput(cur, fgThread, true);
						form.Activate();
						SetForegroundWindow(form.Handle);
						box.Focus();
						box.Select(0, 5);
						if (attached) AttachThreadInput(cur, fgThread, false);
					}
					catch (Exception ex) { fail = ex; }
					ready.Set();
				};
				System.Windows.Forms.Application.Run(form);
			}
			catch (Exception ex) { fail = ex; ready.Set(); }
		});
		t.SetApartmentState(ApartmentState.STA);
		t.Start();
		if (!ready.WaitOne(4000) || fail != null || form == null || box == null) {
			try { form?.BeginInvoke(new Action(() => form.Close())); } catch { }
			Out(fail != null ? fail.Message : "sel timeout");
			return 1;
		}
		var got = TextInjector.CopySelection();
		form.Invoke(new Action(() => box.Select(0, 0)));
		var none = TextInjector.CopySelection();
		try { form.Invoke(new Action(() => form.Close())); } catch { }
		t.Join(2000);
		Out("got=[" + got + "] none=[" + none + "]");
		if (got != "hello") return 2;
		if (none.Length != 0) return 3;
		return 0;
	}

	static int runtoast(string[] args, int at) {
		string text = null;
		var ms = 1900;
		for (var j = at + 1; j < args.Length; j++) {
			var a = args[j] ?? "";
			if (a is "--ms" or "--duration") {
				if (j + 1 >= args.Length || !int.TryParse(args[++j], out ms)) {
					Err("缺少显示毫秒数");
					return 2;
				}
				continue;
			}
			if (a.StartsWith("-")) {
				Err("未知参数 " + a);
				return 2;
			}
			text = text == null ? a : text + " " + a;
		}
		if (string.IsNullOrWhiteSpace(text)) {
			Err("用法: ScreenKit --toast <文字> [--ms 1900]");
			return 2;
		}
		if (ms < 800) ms = 800;
		if (ms > 8000) ms = 8000;
		var disp = System.Windows.Application.Current?.Dispatcher;
		if (disp == null) {
			Err("无法显示 Toast");
			return 1;
		}
		disp.Invoke(() => UiToast.Show(null, text, ms));
		var frame = new DispatcherFrame();
		var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms + 150) };
		timer.Tick += (_, _) => {
			timer.Stop();
			frame.Continue = false;
		};
		timer.Start();
		Dispatcher.PushFrame(frame);
		Out("toast " + ms + "ms");
		return 0;
	}

	static int runzhconv(string[] args, int at) {
		string text = null;
		var trad = true;
		for (var j = at + 1; j < args.Length; j++) {
			var a = args[j] ?? "";
			if (a is "--trad" or "--traditional") { trad = true; continue; }
			if (a is "--simp" or "--simplified") { trad = false; continue; }
			if (a.StartsWith("-")) {
				Err("未知参数 " + a);
				return 2;
			}
			text = text == null ? a : text + " " + a;
		}
		if (string.IsNullOrEmpty(text)) {
			Err("用法: ScreenKit --zhconv <文字> [--trad|--simp]");
			return 2;
		}
		try {
			Out(trad ? ZhConvert.ToTraditional(text) : ZhConvert.ToSimplified(text));
			return 0;
		}
		catch (Exception ex) {
			Err(ex.Message);
			return 1;
		}
	}

	static int runcalendar(string[] args, int at) {
		string dateText = null;
		string cal = null;
		for (var j = at + 1; j < args.Length; j++) {
			var a = args[j] ?? "";
			if (a is "--cal" or "--calendar-id") {
				if (j + 1 >= args.Length) {
					Err("缺少历法名");
					return 2;
				}
				cal = args[++j];
				continue;
			}
			if (a.StartsWith("-")) {
				Err("未知参数 " + a);
				return 2;
			}
			dateText = a;
		}
		if (!WinCal.TryDate(dateText, out var date)) {
			Err("日期应为 yyyy-MM-dd");
			return 2;
		}
		var id = WinCal.FindId(cal);
		if (id == null) {
			Err("未知历法");
			return 2;
		}
		try {
			var info = WinCal.Query(date, id, "zh-CN");
			Out("cal=" + WinCal.Alias(id));
			Out("gregorian=" + info.Gregorian);
			if (!string.IsNullOrEmpty(info.Ganzhi)) Out("ganzhi=" + info.Ganzhi);
			Out("year=" + (string.IsNullOrEmpty(info.Year) ? info.YearNum.ToString() : info.Year));
			Out("month=" + info.Month);
			Out("day=" + info.Day);
			Out("week=" + info.Week);
			Out("leap=" + (info.LeapMonth ? "true" : "false"));
			return 0;
		}
		catch (Exception ex) {
			Err(ex.Message);
			return 1;
		}
	}

	static int runjpyomi(string[] args, int at) {
		string text = null;
		var mono = false;
		for (var j = at + 1; j < args.Length; j++) {
			var a = args[j] ?? "";
			if (a is "--mono") { mono = true; continue; }
			if (a.StartsWith("-")) {
				Err("未知参数 " + a);
				return 2;
			}
			text = text == null ? a : text + " " + a;
		}
		if (string.IsNullOrWhiteSpace(text)) {
			Err("用法: ScreenKit --jpyomi <日文> [--mono]");
			return 2;
		}
		try {
			var res = JpYomi.Convert(text, mono);
			Out(res.Ruby ?? "");
			Out(res.Yomi ?? "");
			return 0;
		}
		catch (Exception ex) {
			Err(ex.Message);
			return 1;
		}
	}

	static void printhelp() {
		Out("""
ScreenKit CLI — Umi-OCR / Rapid PP-OCR + onnxgpu64（exe: ScreenKit.exe）

用法:
  ScreenKit --toast <文字> [--ms 1900]
  ScreenKit --zhconv <文字> [--trad|--simp]
  ScreenKit --calendar [yyyy-MM-dd] [--cal lunar]
  ScreenKit --jpyomi <日文> [--mono]
  ScreenKit --image <路径> [选项]
  ScreenKit --snap [--out <目录>]
  ScreenKit --record-snap [--region L,T,W,H] [--wait-ms 800] [--out <目录>]
  ScreenKit --test-capture-during-record [--region L,T,W,H] [--out <目录>]
  ScreenKit --test-overlay-during-record [--region L,T,W,H]
  ScreenKit --test-overlay-layout
  ScreenKit --test-overlay-span-adj
  ScreenKit --test-record-avsync [--seconds 10] [--region L,T,W,H] [--out <目录>]
  ScreenKit --test-gif-record [--seconds 2] [--region L,T,W,H] [--out <目录>]
  ScreenKit --test-record-codec [av1|x264|x265|mf] [--seconds 2] [--repeat 2] [--region L,T,W,H] [--out <目录>]
  ScreenKit --test-record-cursor [--out <目录>]
  ScreenKit --test-clipboard-path
  ScreenKit --test-img-convert
  ScreenKit --test-qr-make
  ScreenKit --test-rename
  ScreenKit --test-hash
  ScreenKit --test-texttool
  ScreenKit --test-pwgen
  ScreenKit --test-nettool
  ScreenKit --test-update-notes
  ScreenKit --test-zhconv
  ScreenKit --test-wincal
  ScreenKit --test-jpyomi
  ScreenKit --test-wintop
  ScreenKit --test-win-tts
  ScreenKit --test-mem
  ScreenKit --test-dict-search
  ScreenKit --test-dict-sel
  ScreenKit --test-dict-word
  ScreenKit --test-dict-tts
  ScreenKit --test-cast
  ScreenKit --test-cast-recv
  ScreenKit --test-llm-continue
  ScreenKit --test-llm-chat
  ScreenKit --test-llm-agent
  ScreenKit --test-llm-log
  ScreenKit --test-http-tts
  ScreenKit --test-http-chat
  ScreenKit --test-face-overlay
  ScreenKit --test-tts-sherpa <模型名> [--tts-text "文本"]
  ScreenKit --test-edge-tts [发音人] [--tts-text "文本"]
  ScreenKit --list-edge-tts
  ScreenKit --list-models
  ScreenKit --list-tts
  ScreenKit --list-sapi
  ScreenKit --list-asr
  ScreenKit --list-face
  ScreenKit --list-install
  ScreenKit --list-tts-install
  ScreenKit --asr <音频> [--asr-model 名] [--asr-lang auto|zh|en|ja|ko|yue] [--asr-device auto|gpu|cpu|igpu] [--asr-no-itn]
  ScreenKit --probe-tts-gender [-d auto|gpu|cpu] [--only-model 名] [--no-write-config]
  ScreenKit --probe-cuda
  ScreenKit --list-translate
  ScreenKit --translate "文本" [--tr-dir zh-en|en-zh] [-d auto|gpu|cpu|igpu]
  ScreenKit --translate-file <utf8文本文件> [--tr-dir zh-en|en-zh] [-d auto|gpu|cpu|igpu]
  ScreenKit --apply-update <压缩包> --target <安装目录> [--wait-pid PID] [--restart]
  ScreenKit --list-sapi           本进程 SAPI +（x64 时）x86host Web 发音人
  x86host.exe                  独立 32 位 SAPI Web（空闲 60s；主程序按需拉起）
  ScreenKit --help

参数:
  -i, --image     待识别图片路径
  -d, --device    auto(默认) | gpu | cpu | igpu
  -p, --pack      模型包 Id（winocr / umi / rapid-ch，默认 winocr）
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
      --test-overlay-layout  弹出截屏遮罩并记录各屏 HWND/DPI（自动关闭）
      --test-overlay-span-adj  跨屏选区进入标注，检查副屏手柄（自动关闭）
      --test-record-avsync  有声0.1s/静音0.1s循环→录N秒→分析音画同步
      --test-gif-record  录制低帧率无声 GIF 数秒并校验文件头
      --test-record-codec  用 ScreenRecorder 短录并探测视频 codec（av1|x264|x265|mf，默认 av1；mjpeg 读入后按 mf）
      --test-record-cursor  画点击高亮圈并叠加当前光标，写出 PNG
      --test-clipboard-path  先放位图再复制为路径；含 4K 延迟图后改路径计时
      --test-sendfile  sendfile 路径沙箱与列出/上传/删除；网页登录与公开下载
      --test-lang      读取 lang/*.toml，语言列表按中、英、日、韩、西、法、葡、俄、德、其它排序
      --test-apk-qr  生成本机 APK 下载二维码并回读；HTTP GET /apk
      --test-img-convert  写测试 png，转 jpg（旋转90 + 框 100×100、较短边 40）、替换源文件、回收站
      --test-qr-make  生成 UTF-8/GBK 二维码（图下原文）与 Code128
      --test-rename  Everything 风格 %1 / ### 批量改名
      --test-hash  计算并比对 SHA-256
      --test-texttool  Base64 / URL / GBK 十六进制往返
      --test-pwgen  生成密码（长度、每类字符、排除易混）；单词译音 / 变体 JSON 解析
      --test-nettool  localhost 解析、ping 127.0.0.1、系统定位权限
      --test-update-notes  更新说明的中英文拆分、版本列表和简单 Markdown
      --test-zhconv  LCMapStringEx 简繁（国发 / 软件）
      --test-wincal  农历甲辰正月与 2023 闰二月
      --test-jpyomi  東京 的系统读音
      --test-wintop  枚举顶层窗口，并对探测窗设置/取消固定在前面
      --test-win-tts  非管理员时 start /wait 再 RunAs 安装、卸载或查看 Windows 语音（不弹 UAC）
      --test-mem  进程内存读取，以及模型文件大小统计
      --test-ort-lazy  启动不加载 ONNX；第一次 Ensure 才映射，释放后仍可建会话
      --test-ort-release  加载 CUDA 库后释放，大库应卸掉且仍可建会话
      --test-dict-search  只读查询 exe 旁 dict.db（默认 学生 与 hello）；韩语词组行有发音按钮
      --test-dict-sel  前台文本框选中 hello，Ctrl+C 读回
      --test-dict-word  剪贴板单词判定（汉字 1–4 / 英文 1–20 字母 / 日语 / 韩语）
      --test-dict-host  选词搜索打开独立词典窗口并查出 hello；双击选词弹出浮窗；划词词条再开窗口；浮窗不挡选区，失活即关
      --test-dict-tr  词典划词翻译打开小窗后立即开始翻译
      --test-dict-tts  词典发音缓存保留 1 天，语速换算；自动优先 Windows 语音否则 Edge
      --test-cast  投屏协议打包/拆包与画质 Fit（有 ffmpeg64 时编一帧）
      --test-cast-recv  HTTP /cast hello 往返必须进本进程（WiFi/ADB 弹窗路径）
      --test-aoa  列出 LibUsb 可见的 WinUSB 设备并探测 AOA GET_PROTOCOL
      --test-llm-continue  截断 finish_reason 与续写拼接（不去网）
      --test-llm-chat  对话历史裁剪与续写数组形状（不去网）
      --test-llm-log  最近 1000 条 LLM 请求：用量、地址去查询串、落盘顺序（不去网）
      --test-llm-agent  Agent 沙箱路径、tool_call 解析、读写/脚本（不去网）
      --test-http-tts  HTTP /api/tts 走 SAPI 与 Windows 语音，校验 WAV
      --test-http-chat  HTTP /api/chat（无 LLM 时期望 960/961；有配置可测 TTS）
      --test-face-overlay  用人脸叠加字体写「女 22岁」，对照 Hershey 的 ??
      --test-tts-sherpa  用 CPU 加载指定 Sherpa 模型并完成一次合成
      --test-edge-tts  联网调用 Edge 自然语音并校验返回音频
      --tts-text   TTS 测试文本（默认韩语测试句）
      --repeat    --test-record-codec 连续次数（默认 1）
      --seconds   --test-record-avsync / --test-gif-record / --test-record-codec 录制秒数
      --region    物理像素区域 L,T,W,H（默认主屏中心）
      --wait-ms   开录后等待毫秒再截（默认 800）
  -o, --out       --snap / --record-snap / --test-*-record / --test-gif-record 输出目录
      --list-models 列出可用 OCR 模型包与变体
      --list-tts    列出 TTS（Sherpa）模型
      --list-sapi   列出 SAPI（x64 会按需启动 x86host.exe Web 合并 32 位音）
      --list-edge-tts  联网列出 Edge 自然语音
      --list-asr    列出 ASR（Sherpa）语音识别模型
      --list-face   列出人脸 ONNX（程序旁 facemodels）
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
      --restart     覆盖后启动 ScreenKit.exe

示例:
  ScreenKit -i test.png -d gpu
  ScreenKit -i a.jpg -p rapid-ch -d cpu
  ScreenKit --snap
  ScreenKit --snap -o D:\tmp\shots
  ScreenKit --record-snap
  ScreenKit --test-capture-during-record
  ScreenKit --test-record-avsync
  ScreenKit --test-record-avsync --seconds 10 -o log\record_avsync
  ScreenKit --test-gif-record --seconds 2 -o log\gif_record
  ScreenKit --test-record-codec av1 --repeat 2 --seconds 2 -o log\record_codec
  ScreenKit --test-record-codec mf --seconds 2
  ScreenKit --test-record-cursor -o log\record_cursor
  ScreenKit --test-clipboard-path
  ScreenKit --test-img-convert
  ScreenKit --test-qr-make
  ScreenKit --test-rename
  ScreenKit --test-hash
  ScreenKit --test-texttool
  ScreenKit --test-pwgen
  ScreenKit --test-nettool
  ScreenKit --test-update-notes
  ScreenKit --test-zhconv
  ScreenKit --test-wincal
  ScreenKit --test-jpyomi
  ScreenKit --test-wintop
  ScreenKit --test-win-tts
  ScreenKit --test-mem
  ScreenKit --test-dict-search
  ScreenKit --test-dict-sel
  ScreenKit --test-dict-word
  ScreenKit --test-dict-tts
  ScreenKit --test-cast
  ScreenKit --test-cast-recv
  ScreenKit --test-llm-continue
  ScreenKit --test-llm-chat
  ScreenKit --test-llm-agent
  ScreenKit --test-llm-log
  ScreenKit --test-http-tts
  ScreenKit --test-http-chat
  ScreenKit --test-face-overlay
  ScreenKit --test-tts-sherpa vits-mimic3-ko_KO-kss_low
  ScreenKit --test-edge-tts ko-KR-SunHiNeural
  ScreenKit --record-snap --region 100,100,800,600 -o log\record_snap
  ScreenKit --list-models
  ScreenKit --list-tts
  ScreenKit --list-asr
  ScreenKit --list-face
  ScreenKit --asr test.wav
  ScreenKit --asr test.wav --asr-model sense-voice --asr-lang zh --asr-device cpu
  ScreenKit --probe-tts-gender -d gpu
  ScreenKit --probe-tts-gender --only-model vits-zh-aishell3 -d cpu
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

	static int testapkqr() {
		const string sample = "http://192.168.1.8:1224/apk";
		BitmapSource bmp;
		try {
			bmp = QrMake.Encode(sample, 6);
		}
		catch (Exception ex) {
			Err("apk-qr encode: " + ex.Message);
			return 1;
		}
		if (bmp == null || bmp.PixelWidth < 21 || bmp.PixelHeight < 21) {
			Err("apk-qr encode: 图像过小");
			return 1;
		}
		Out($"apk-qr encode {bmp.PixelWidth}x{bmp.PixelHeight}");
		var ips = ApkHost.LanIPv4s();
		Out("apk-qr ips " + (ips.Count == 0 ? "(none)" : string.Join(" ", ips)));
		var pngPath = Path.Combine(TmpStore.Root, "apk_qr_test.png");
		try {
			using var fs = File.Create(pngPath);
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(bmp));
			enc.Save(fs);
			Out("apk-qr png=" + pngPath);
		}
		catch (Exception ex) {
			Err("apk-qr save: " + ex.Message);
			return 1;
		}
		if (NativeRuntime.HasOpenCv()) {
			try {
				var bytes = File.ReadAllBytes(pngPath);
				var r = QrScan.Run(bytes);
				var text = r?.Codes?.FirstOrDefault()?.Text ?? "";
				if (!string.Equals(text, sample, StringComparison.Ordinal)) {
					Err("apk-qr decode mismatch: " + text);
					return 1;
				}
				Out("apk-qr decode ok");
			}
			catch (Exception ex) {
				Err("apk-qr decode: " + ex.Message);
				return 1;
			}
		}
		else
			Out("apk-qr decode skipped (no OpenCV)");

		var dummy = Path.Combine(TmpStore.Root, "apk", "screenkit1.0.0.apk");
		Directory.CreateDirectory(Path.GetDirectoryName(dummy));
		var payload = Encoding.UTF8.GetBytes("PK\x03\x04screenkit-apk-test");
		File.WriteAllBytes(dummy, payload);
		ApkHost.SetFileForTest(dummy);
		var port = 27532;
		var o = new OcrOptions { SendFileEnabled = true, SendFilePort = port, SendFileUdpPort = 27531 };
		SendFileServer sv = null;
		try {
			sv = new SendFileServer(() => o, () => { });
			sv.Start();
			if (!sv.IsRunning) {
				Err("apk-qr http: server not running");
				return 1;
			}
			var url = "http://127.0.0.1:" + port + "/apk";
			Out("apk-qr GET " + url);
			byte[] got;
			using (var http = new HttpClient(HttpProxy.CreateHandler()) { Timeout = TimeSpan.FromSeconds(8) })
				got = Task.Run(() => http.GetByteArrayAsync(url)).GetAwaiter().GetResult();
			if (got == null || got.Length != payload.Length || !got.SequenceEqual(payload)) {
				Err("apk-qr http mismatch len=" + (got == null ? -1 : got.Length));
				return 1;
			}
			Out("apk-qr http ok");
		}
		catch (Exception ex) {
			Err("apk-qr http: " + ex.Message);
			return 1;
		}
		finally {
			try { sv?.Dispose(); } catch { }
			ApkHost.SetFileForTest(null);
		}
		Out("apk-qr ok");
		return 0;
	}

	static int testupdatenotes() {
		var old = Loc.Lang;
		try {
			AppUpdater.SplitNotes("plain line\nsecond", out var en, out var zh);
			if (en != zh || en.IndexOf("second", StringComparison.Ordinal) < 0) {
				Err("update-notes: 无语言标题时应两边相同");
				return 1;
			}
			AppUpdater.SplitNotes("### English\n\n- Hello\n\n#### Changed\n\n- x\n\n### 中文\n\n- 你好\n", out en, out zh);
			if (en.IndexOf("Hello", StringComparison.Ordinal) < 0 || en.IndexOf("Changed", StringComparison.Ordinal) < 0
				|| en.IndexOf("你好", StringComparison.Ordinal) >= 0) {
				Err("update-notes: 英文段不符");
				return 1;
			}
			if (zh.IndexOf("你好", StringComparison.Ordinal) < 0 || zh.IndexOf("Hello", StringComparison.Ordinal) >= 0) {
				Err("update-notes: 中文段不符");
				return 1;
			}
			if (AppUpdater.CompareVersion("1.0.2", "1.0.10") >= 0) {
				Err("update-notes: 1.0.2 应旧于 1.0.10");
				return 1;
			}
			var json = """
[
  {"tag_name":"v1.0.5","draft":false,"prerelease":false,"published_at":"2026-09-01T00:00:00Z","body":"### 中文\n\n- 仅中文\n"},
  {"tag_name":"v1.0.3","draft":false,"prerelease":false,"published_at":"2026-08-30T00:00:00Z","body":"### English\n\n- Fixed the **tray**.\n\n#### Changed\n\n- One more.\n\nSee [notes](https://example.com/rel).\n\n```\ncode line\n```\n\n### 中文\n\n- 修复托盘。\n"},
  {"tag_name":"v1.0.2","published_at":"2026-08-26T00:00:00Z","body":"### English\n\n- older en\n\n### 中文\n\n- 旧版中文\n"},
  {"tag_name":"v1.0.1","published_at":"2026-08-07T00:00:00Z","body":"### English\n\n- current\n\n### 中文\n\n- 当前\n"},
  {"tag_name":"v9.9.9","draft":true,"body":"### English\n\n- draft\n"},
  {"tag_name":"v1.0.4","prerelease":true,"body":"### English\n\n- pre\n"}
]
""";
			var list = AppUpdater.ParseReleaseList(json, "1.0.1");
			if (list.Count != 3 || list[0].Version != "1.0.2" || list[1].Version != "1.0.3" || list[2].Version != "1.0.5") {
				Err("update-notes: 列表 " + string.Join(",", list.Select(n => n.Version)));
				return 1;
			}
			if (list[1].English.IndexOf("One more", StringComparison.Ordinal) < 0
				|| list[1].Chinese.IndexOf("修复托盘", StringComparison.Ordinal) < 0
				|| list[1].English.IndexOf("修复托盘", StringComparison.Ordinal) >= 0) {
				Err("update-notes: 1.0.3 正文拆分不符");
				return 1;
			}
			if (list[0].Published != "2026-08-26") {
				Err("update-notes: 日期 " + list[0].Published);
				return 1;
			}
			Loc.Lang = "zh";
			var info = new UpdateInfo {
				Version = "1.0.5",
				CurrentVersion = "1.0.1",
				AssetName = "screenkit_1.0.5.7z",
				SizeBytes = 2048,
				HasUpdate = true,
			};
			var w = new UpdateNotesWindow(info, list);
			w.Show();
			try {
				if (w.elist.Items.Count != 3 || w.elist.SelectedIndex != 2) {
					Err("update-notes: 窗口列表选中不符");
					return 1;
				}
				if (w.lbhead.Text.IndexOf("1.0.1", StringComparison.Ordinal) < 0
					|| w.lbhead.Text.IndexOf("1.0.5", StringComparison.Ordinal) < 0) {
					Err("update-notes: 标题 " + w.lbhead.Text);
					return 1;
				}
				if (w.lbfile.Text.IndexOf("screenkit_1.0.5.7z", StringComparison.Ordinal) < 0) {
					Err("update-notes: 文件名 " + w.lbfile.Text);
					return 1;
				}
				var text = notestext(w);
				if (text.IndexOf("仅中文", StringComparison.Ordinal) < 0) {
					Err("update-notes: 最新中文 " + text);
					return 1;
				}
				w.elist.SelectedIndex = 1;
				text = notestext(w);
				if (text.IndexOf("修复托盘", StringComparison.Ordinal) < 0 || text.IndexOf("Fixed", StringComparison.Ordinal) >= 0) {
					Err("update-notes: 选中中文 " + text);
					return 1;
				}
				w.ben.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
				text = notestext(w);
				if (text.IndexOf("Fixed the tray", StringComparison.Ordinal) < 0
					|| text.IndexOf("One more", StringComparison.Ordinal) < 0
					|| text.IndexOf("code line", StringComparison.Ordinal) < 0
					|| text.IndexOf("修复托盘", StringComparison.Ordinal) >= 0) {
					Err("update-notes: 英文渲染 " + text);
					return 1;
				}
				if (!noteshaslink(w.docview.Document) || !noteshasbold(w.docview.Document)) {
					Err("update-notes: 链接或加粗未渲染");
					return 1;
				}
				w.elist.SelectedIndex = 0;
				text = notestext(w);
				if (text.IndexOf("older en", StringComparison.Ordinal) < 0) {
					Err("update-notes: 旧版英文 " + text);
					return 1;
				}
				w.bzh.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
				text = notestext(w);
				if (text.IndexOf("旧版中文", StringComparison.Ordinal) < 0) {
					Err("update-notes: 切回中文 " + text);
					return 1;
				}
				w.elist.SelectedIndex = 2;
				w.ben.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
				text = notestext(w);
				if (text.IndexOf("这个版本没有这段说明", StringComparison.Ordinal) < 0) {
					Err("update-notes: 缺英文 " + text);
					return 1;
				}
				if (!w.bgo.IsEnabled || !w.bignore.IsEnabled) {
					Err("update-notes: 按钮不可用");
					return 1;
				}
			}
			finally {
				try { w.Close(); } catch { }
			}
			Out("update-notes ok");
			return 0;
		}
		catch (Exception ex) {
			Err("update-notes: " + ex);
			return 1;
		}
		finally {
			try { Loc.Lang = old; } catch { }
		}

		static string notestext(UpdateNotesWindow w) {
			var d = w.docview.Document;
			if (d == null) return "";
			return new System.Windows.Documents.TextRange(d.ContentStart, d.ContentEnd).Text ?? "";
		}

		static bool noteshaslink(System.Windows.Documents.FlowDocument d) {
			if (d == null) return false;
			foreach (var b in d.Blocks)
				if (notewalk(b, false)) return true;
			return false;
		}

		static bool noteshasbold(System.Windows.Documents.FlowDocument d) {
			if (d == null) return false;
			foreach (var b in d.Blocks)
				if (notewalk(b, true)) return true;
			return false;
		}

		static bool notewalk(System.Windows.Documents.Block b, bool bold) {
			if (b is System.Windows.Documents.Paragraph p) {
				foreach (var inline in p.Inlines) {
					if (!bold && inline is System.Windows.Documents.Hyperlink) return true;
					if (bold && inline is System.Windows.Documents.Run run
						&& run.FontWeight == FontWeights.SemiBold
						&& (run.Text ?? "").IndexOf("tray", StringComparison.Ordinal) >= 0)
						return true;
				}
			}
			if (b is System.Windows.Documents.List list) {
				foreach (var item in list.ListItems)
					foreach (var bb in item.Blocks)
						if (notewalk(bb, bold)) return true;
			}
			if (b is System.Windows.Documents.Section sec) {
				foreach (var bb in sec.Blocks)
					if (notewalk(bb, bold)) return true;
			}
			return false;
		}
	}

	static int testlang() {
		Loc.SetRootForTest(null);
		Loc.Reload();
		var live = Loc.Languages;
		if (live.Count < 4 || live[0].Code != "zh" || live[1].Code != "en" || live[2].Code != "ja" || live[3].Code != "ko") {
			Err("lang: 程序目录应先列出 zh、en、ja、ko，实际 " + string.Join(",", live.Select(x => x.Code)));
			return 1;
		}
		if (live[0].Name != "中文" || live[1].Name != "English" || live[2].Name != "日本語" || live[3].Name != "한국어") {
			Err("lang: 显示名不符 " + live[0].Name + " / " + live[1].Name + " / " + live[2].Name + " / " + live[3].Name);
			return 1;
		}
		Loc.Lang = "zh";
		if (Loc.T("app.title") != "屏幕截图工具") {
			Err("lang: 中文 app.title 不符");
			return 1;
		}
		var body = Loc.T("feat.prompt.tts.body");
		if (body.IndexOf('\n') < 0) {
			Err("lang: 多行文案未还原换行");
			return 1;
		}
		Loc.Lang = "en";
		if (Loc.T("app.title") != "ScreenKit" || !Loc.IsEn) {
			Err("lang: 英文 app.title 不符");
			return 1;
		}
		Loc.Lang = "ja";
		if (Loc.T("app.title") != "ScreenKit" || Loc.T("feat.prompt.tts.body").IndexOf('\n') < 0 || Loc.T("stbar.none") != "モデル未読み込み") {
			Err("lang: 日文文案不符");
			return 1;
		}
		Loc.Lang = "ko";
		if (Loc.T("app.title") != "ScreenKit" || Loc.T("stbar.none") != "불러온 모델 없음" || Loc.T("menu.file") != "파일(_F)") {
			Err("lang: 韩文文案不符");
			return 1;
		}
		Loc.Lang = "zh";
		var dir = Path.Combine(Path.GetTempPath(), "sk_lang_" + Guid.NewGuid().ToString("N"));
		try {
			Directory.CreateDirectory(dir);
			File.WriteAllText(Path.Combine(dir, "fr.toml"), "name = \"Français\"\n[text]\napp.title = \"Bonjour\"\n", new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(dir, "ko.toml"), "name = \"한국어\"\n[text]\napp.title = \"안녕\"\n", new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(dir, "ja.toml"), "name = \"日本語\"\n[text]\nonly.ja = \"こんにちは\"\n", new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(dir, "zh.toml"), "name = \"中文\"\n[text]\napp.title = \"中\"\n", new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(dir, "en.toml"), "name = \"English\"\n[text]\napp.title = \"En\"\n", new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(dir, "de.toml"), "name = \"Deutsch\"\n[text]\napp.title = \"De\"\n", new UTF8Encoding(false));
			Loc.SetRootForTest(dir);
			Loc.Reload();
			var codes = string.Join(",", Loc.Languages.Select(x => x.Code));
			if (codes != "zh,en,ja,ko,fr,de") {
				Err("lang: 排序不符 " + codes);
				return 1;
			}
			Loc.Lang = "ja";
			if (Loc.T("only.ja") != "こんにちは" || Loc.T("app.title") != "中") {
				Err("lang: 日文缺键未回退中文");
				return 1;
			}
			if (Loc.Normalize("english") != "en" || Loc.Normalize("korean") != "ko") {
				Err("lang: 别名未映射");
				return 1;
			}
			Out("lang ok " + codes);
			return 0;
		}
		catch (Exception ex) {
			Err("lang: " + ex.Message);
			return 1;
		}
		finally {
			Loc.SetRootForTest(null);
			Loc.Reload();
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	static int testsendfile() {
		var dir = Path.Combine(Path.GetTempPath(), "sk_sf_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try {
			SendFilePaths.SetRootForTest(dir);
			SendFilePaths.EnsureRoot();
			if (SendFilePaths.TryResolve("..\\..\\Windows", out _, out _)) {
				Err("sendfile: 目录穿越应失败");
				return 1;
			}
			if (SendFilePaths.TryResolve("C:\\Windows\\notepad.exe", out _, out _)) {
				Err("sendfile: 绝对路径应失败");
				return 1;
			}
			if (!SendFilePaths.TryResolve("a/b.txt", out var full, out var err)) {
				Err("sendfile: 相对路径失败 " + err);
				return 1;
			}
			using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("hello")))
				SendFileOps.SaveStream("a/b.txt", ms);
			if (!File.Exists(full)) {
				Err("sendfile: 上传后文件不存在");
				return 1;
			}
			SendFileOps.Mkdir("sub");
			var items = SendFileOps.List("", false);
			if (items == null || items.Count < 1) {
				Err("sendfile: list 为空");
				return 1;
			}
			var deep = SendFileOps.List("", true);
			if (deep == null) {
				Err("sendfile: deep list 失败");
				return 1;
			}
			SendFileOps.Rename("a/b.txt", "a/c.txt");
			if (!SendFilePaths.TryResolve("a/c.txt", out var renamed, out _) || !File.Exists(renamed)) {
				Err("sendfile: 改名失败");
				return 1;
			}
			SendFileOps.Delete("a/c.txt");
			if (File.Exists(renamed)) {
				Err("sendfile: 删除失败");
				return 1;
			}
			Out("sendfile path/ops ok");
			if (testsendfileweb() != 0) return 1;
			return 0;
		}
		finally {
			SendFilePaths.SetRootForTest(null);
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	static int testsendfileweb() {
		var dir = Path.Combine(Path.GetTempPath(), "sk_sfw_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		SendFileServer sv = null;
		var port = 27533;
		try {
			SendFilePaths.SetRootForTest(dir);
			SendFilePaths.EnsureRoot();
			var o = new OcrOptions {
				SendFileEnabled = true,
				SendFilePort = port,
				SendFileUdpPort = 27534,
				SendFileWebPass = "webtest",
			};
			sv = new SendFileServer(() => o, () => { });
			sv.Auth.AutoAccept = true;
			sv.Start();
			if (!sv.IsRunning) {
				Err("sendfile-web: server not running");
				return 1;
			}
			var baseUrl = "http://127.0.0.1:" + port;
			var cookies = new System.Net.CookieContainer();
			using var handler = new HttpClientHandler {
				UseProxy = false,
				UseCookies = true,
				CookieContainer = cookies,
			};
			using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
			using (var pageRes = Task.Run(() => http.GetAsync(baseUrl + "/files?pc=1")).GetAwaiter().GetResult()) {
				var cc = pageRes.Headers.CacheControl;
				if (cc == null || !cc.NoStore) {
					Err("sendfile-web: /files 页面不应缓存");
					return 1;
				}
			}
			using (var cssRes = Task.Run(() => http.GetAsync(baseUrl + "/web/d.css")).GetAwaiter().GetResult()) {
				var cc = cssRes.Headers.CacheControl;
				if (cc == null || cc.MaxAge != TimeSpan.FromHours(1)) {
					Err("sendfile-web: /web/d.css 缓存不是 1 小时");
					return 1;
				}
			}
			var page = Task.Run(() => http.GetStringAsync(baseUrl + "/files?pc=1")).GetAwaiter().GetResult();
			if (page == null || page.IndexOf("sfweb", StringComparison.Ordinal) < 0
				|| page.IndexOf("id=\"btext\"", StringComparison.Ordinal) < 0
				|| page.IndexOf("id=\"textdlg\"", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: /files 未返回页面");
				return 1;
			}
			var mpage = Task.Run(() => http.GetStringAsync(baseUrl + "/m")).GetAwaiter().GetResult();
			if (mpage == null || mpage.IndexOf("sfweb", StringComparison.Ordinal) < 0
				|| mpage.IndexOf("id=\"btext\"", StringComparison.Ordinal) < 0
				|| mpage.IndexOf("id=\"textdlg\"", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: /m 未返回页面");
				return 1;
			}
			var apkUp = Task.Run(() => http.GetStringAsync(baseUrl + "/api/web/apk-update")).GetAwaiter().GetResult();
			if (apkUp == null || apkUp.IndexOf("\"code\":100", StringComparison.Ordinal) < 0
				|| apkUp.IndexOf("\"ok\":", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: apk-update 失败: " + apkUp);
				return 1;
			}
			var listNaked = getbody(http, baseUrl + "/api/web/list");
			if (listNaked == null || listNaked.IndexOf("\"code\":401", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: 未登录 list 应 401: " + listNaked);
				return 1;
			}
			var textNaked = getbody(http, baseUrl + "/api/web/text");
			if (textNaked == null || textNaked.IndexOf("\"code\":401", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: 未登录 text 应 401: " + textNaked);
				return 1;
			}
			var bad = postjson(http, baseUrl + "/api/web/login", "{\"password\":\"nope\"}");
			if (bad == null || bad.IndexOf("\"code\":401", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: 错密码应 401: " + bad);
				return 1;
			}
			var okLogin = postjson(http, baseUrl + "/api/web/login", "{\"password\":\"webtest\"}");
			if (okLogin == null || okLogin.IndexOf("\"code\":100", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: 登录失败: " + okLogin);
				return 1;
			}
			var listed = Task.Run(() => http.GetStringAsync(baseUrl + "/api/web/list")).GetAwaiter().GetResult();
			if (listed == null || listed.IndexOf("\"code\":100", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: 登录后 list 失败: " + listed);
				return 1;
			}
			var pushed = postjson(http, baseUrl + "/api/web/text", "{\"text\":\"hello-web-text\"}");
			if (pushed == null || pushed.IndexOf("hello-web-text", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: 推送文本失败: " + pushed);
				return 1;
			}
			var gotText = getbody(http, baseUrl + "/api/web/text");
			if (gotText == null || gotText.IndexOf("hello-web-text", StringComparison.Ordinal) < 0
				|| sv.Text.Draft != "hello-web-text") {
				Err("sendfile-web: 读取文本不符: " + gotText);
				return 1;
			}
			var cleared = postjson(http, baseUrl + "/api/web/text", "{\"text\":\"\"}");
			if (cleared == null || cleared.IndexOf("\"text\":\"\"", StringComparison.Ordinal) < 0
				|| sv.Text.Draft != "") {
				Err("sendfile-web: 清空文本失败: " + cleared);
				return 1;
			}
			using (var up = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/web/upload?path=web.txt")) {
				up.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello-web"));
				var upResp = Task.Run(() => http.SendAsync(up)).GetAwaiter().GetResult();
				var upBody = Task.Run(() => upResp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
				if (upBody == null || upBody.IndexOf("\"code\":100", StringComparison.Ordinal) < 0) {
					Err("sendfile-web: 上传失败: " + upBody);
					return 1;
				}
			}
			using (var naked = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) }) {
				var got = Task.Run(() => naked.GetByteArrayAsync(baseUrl + "/f/web.txt")).GetAwaiter().GetResult();
				var text = Encoding.UTF8.GetString(got ?? Array.Empty<byte>());
				if (text != "hello-web") {
					Err("sendfile-web: 公开下载不符: " + text);
					return 1;
				}
			}
			SendFileOps.Mkdir("sub");
			using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("in-sub")))
				SendFileOps.SaveStream("sub/a.txt", ms);
			var zipBytes = Task.Run(() => http.GetByteArrayAsync(baseUrl + "/api/web/zip?path=sub")).GetAwaiter().GetResult();
			if (zipBytes == null || zipBytes.Length < 4 || zipBytes[0] != (byte)'P' || zipBytes[1] != (byte)'K') {
				Err("sendfile-web: zip 不是 PK");
				return 1;
			}
			var keepLogin = postjson(http, baseUrl + "/api/web/login", "{\"password\":\"webtest\",\"keep\":true}");
			if (keepLogin == null || keepLogin.IndexOf("\"keep\":true", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: keep 登录失败: " + keepLogin);
				return 1;
			}
			var cookie = SendFileWeb.SetCookie("abc", true);
			if (cookie.IndexOf("Max-Age=", StringComparison.Ordinal) < 0
				|| cookie.IndexOf("Expires=", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: keep Cookie 缺少过期时间");
				return 1;
			}
			if (!File.Exists(Path.Combine(dir, ".web_sess"))) {
				Err("sendfile-web: keep 未落盘");
				return 1;
			}
			try { sv.Dispose(); } catch { }
			sv = null;
			sv = new SendFileServer(() => o, () => { });
			sv.Auth.AutoAccept = true;
			sv.Start();
			if (!sv.IsRunning) {
				Err("sendfile-web: 重启后未监听");
				return 1;
			}
			var meKeep = getbody(http, baseUrl + "/api/web/me");
			if (meKeep == null || meKeep.IndexOf("\"code\":100", StringComparison.Ordinal) < 0) {
				Err("sendfile-web: 重启后保持登录失败: " + meKeep);
				return 1;
			}
			using (var stale = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/web/me")) {
				stale.Headers.TryAddWithoutValidation("X-Web-Token", "0123456789abcdef0123456789abcdef");
				var staleResp = Task.Run(() => http.SendAsync(stale)).GetAwaiter().GetResult();
				var staleBody = Task.Run(() => staleResp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
				if (staleBody == null || staleBody.IndexOf("\"code\":100", StringComparison.Ordinal) < 0) {
					Err("sendfile-web: 过期 token 挡住了保持登录: " + staleBody);
					return 1;
				}
			}
			if (testsfquery(sv) != 0) return 1;
			Out("sendfile-web ok");
			return 0;
		}
		catch (Exception ex) {
			Err("sendfile-web: " + ex.Message);
			return 1;
		}
		finally {
			try { sv?.Dispose(); } catch { }
			SendFilePaths.SetRootForTest(null);
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	/// <summary>走 HttpListener（与正式 HTTP 口相同），查询串里的中文必须按 UTF-8 解开。</summary>
	static int testsfquery(SendFileServer sv) {
		var names = new[] {
			".to_phone/XPlayer v2.9.0.0 高级版.apk",
			".to_phone/[Android] XPlayer播放器v2.9.0.0 高级版 [Hybase.com]黑域基地.apk",
		};
		var payload = Encoding.UTF8.GetBytes("apk-ok");
		foreach (var name in names) {
			using var ms = new MemoryStream(payload);
			SendFileOps.SaveStream(name, ms);
		}
		var port = 27535;
		var listener = new HttpListener();
		listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
		try { listener.Start(); }
		catch (Exception ex) {
			Err("sendfile-query: listener " + ex.Message);
			return 1;
		}
		try {
			foreach (var name in names) {
				var q = Uri.EscapeDataString(name).Replace("%20", "+");
				var raw = sfqueryget(listener, sv, port, "/d?path=" + q);
				if (raw == null) return 1;
				var text = Encoding.UTF8.GetString(raw);
				var sep = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
				var body = sep >= 0 ? text.Substring(sep + 4) : text;
				if (body.IndexOf("apk-ok", StringComparison.Ordinal) < 0) {
					Err("sendfile-query: " + name + " => " + body);
					return 1;
				}
			}
			Out("sendfile-query utf8 ok");
			return 0;
		}
		finally {
			try { listener.Stop(); } catch { }
			try { listener.Close(); } catch { }
		}
	}

	static byte[] sfqueryget(HttpListener listener, SendFileServer sv, int port, string pathAndQuery) {
		Exception ex = null;
		byte[] got = null;
		var task = Task.Run(() => {
			try {
				using var tcp = new TcpClient("127.0.0.1", port);
				var req = "GET " + pathAndQuery + " HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n";
				var buf = Encoding.ASCII.GetBytes(req);
				tcp.GetStream().Write(buf, 0, buf.Length);
				using var ms = new MemoryStream();
				tcp.GetStream().CopyTo(ms);
				got = ms.ToArray();
			}
			catch (Exception e) { ex = e; }
		});
		var ar = listener.BeginGetContext(null, null);
		if (!ar.AsyncWaitHandle.WaitOne(8000)) {
			Err("sendfile-query: 无请求 " + pathAndQuery);
			try { task.Wait(1000); } catch { }
			return null;
		}
		var ctx = listener.EndGetContext(ar);
		if (!sv.TryHandle(ctx)) {
			Err("sendfile-query: TryHandle false " + pathAndQuery);
			try { ctx.Response.Abort(); } catch { }
			try { task.Wait(2000); } catch { }
			return null;
		}
		if (!task.Wait(8000)) {
			Err("sendfile-query: 读响应超时 " + pathAndQuery);
			return null;
		}
		if (got == null) {
			Err("sendfile-query: " + (ex != null ? ex.Message : "empty") + " " + pathAndQuery);
			return null;
		}
		return got;
	}

	static string getbody(HttpClient http, string url) {
		var resp = Task.Run(() => http.GetAsync(url)).GetAwaiter().GetResult();
		return Task.Run(() => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
	}

	static string postjson(HttpClient http, string url, string json) {
		using var req = new HttpRequestMessage(HttpMethod.Post, url);
		req.Content = new StringContent(json ?? "{}", Encoding.UTF8, "application/json");
		var resp = Task.Run(() => http.SendAsync(req)).GetAwaiter().GetResult();
		return Task.Run(() => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
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
