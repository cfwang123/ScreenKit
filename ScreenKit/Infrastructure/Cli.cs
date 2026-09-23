using System.IO;
using System.Net.Http;
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
				or "--test-apk-qr"
				or "--test-img-convert" or "--test-qr-make" or "--test-rename"
				or "--test-hash" or "--test-texttool" or "--test-pwgen" or "--test-nettool"
				or "--test-llm-continue"
				or "--test-llm-chat"
				or "--test-llm-agent"
				or "--test-http-tts"
				or "--test-http-chat"
				or "--test-face-overlay"
				or "--help" or "-h" or "/?"
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
		try {
			var opt = new OcrOptions();
			AppConfig.LoadInto(opt);
			Loc.SetFromConfig(opt.UiLang);
		}
		catch { }
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
				Out($"  - {v.DisplayName}  (det={v.DetFile}, rec={v.RecFile})");
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
		Out($"Count={list.Count}");
		if (list.Count == 0) {
			Err("未发现 ASR 模型（请放到程序目录 asrmodels）");
			return 1;
		}
		foreach (var m in list)
			Out($"  [{m.Type}] {m.DisplayName} sr={m.SampleRate} dir={m.ModelDir}");
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
		}
		catch (Exception ex) {
			Err("FAIL: " + ex);
			bad++;
		}
		Out(bad == 0 ? "=== OK：网络工具 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static void printhelp() {
		Out("""
ScreenKit CLI — Umi-OCR / Rapid PP-OCR + onnxgpu64（exe: ScreenKit.exe）

用法:
  ScreenKit --image <路径> [选项]
  ScreenKit --snap [--out <目录>]
  ScreenKit --record-snap [--region L,T,W,H] [--wait-ms 800] [--out <目录>]
  ScreenKit --test-capture-during-record [--region L,T,W,H] [--out <目录>]
  ScreenKit --test-overlay-during-record [--region L,T,W,H]
  ScreenKit --test-overlay-layout
  ScreenKit --test-overlay-span-adj
  ScreenKit --test-record-avsync [--seconds 10] [--region L,T,W,H] [--out <目录>]
  ScreenKit --test-gif-record [--seconds 2] [--region L,T,W,H] [--out <目录>]
  ScreenKit --test-record-codec [av1|x264|x265] [--seconds 2] [--repeat 2] [--region L,T,W,H] [--out <目录>]
  ScreenKit --test-record-cursor [--out <目录>]
  ScreenKit --test-clipboard-path
  ScreenKit --test-img-convert
  ScreenKit --test-qr-make
  ScreenKit --test-rename
  ScreenKit --test-hash
  ScreenKit --test-texttool
  ScreenKit --test-pwgen
  ScreenKit --test-nettool
  ScreenKit --test-llm-continue
  ScreenKit --test-llm-chat
  ScreenKit --test-llm-agent
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
      --test-overlay-layout  弹出截屏遮罩并记录各屏 HWND/DPI（自动关闭）
      --test-overlay-span-adj  跨屏选区进入标注，检查副屏手柄（自动关闭）
      --test-record-avsync  有声0.1s/静音0.1s循环→录N秒→分析音画同步
      --test-gif-record  录制低帧率无声 GIF 数秒并校验文件头
      --test-record-codec  用 ScreenRecorder 短录并探测视频 codec（默认 av1）
      --test-record-cursor  画点击高亮圈并叠加当前光标，写出 PNG
      --test-clipboard-path  先放位图再复制为路径；含 4K 延迟图后改路径计时
      --test-sendfile  sendfile 路径沙箱与列出/上传/删除（临时目录，不弹配对）
      --test-apk-qr  生成本机 APK 下载二维码并回读；HTTP GET /apk
      --test-img-convert  写测试 png，转 jpg（旋转90 + 限制 100×100）、替换源文件、回收站
      --test-qr-make  生成 UTF-8/GBK 二维码（图下原文）与 Code128
      --test-rename  Everything 风格 %1 / ### 批量改名
      --test-hash  计算并比对 SHA-256
      --test-texttool  Base64 / URL / GBK 十六进制往返
      --test-pwgen  生成密码（长度、每类字符、排除易混）
      --test-nettool  localhost 解析与 ping 127.0.0.1
      --test-llm-continue  截断 finish_reason 与续写拼接（不去网）
      --test-llm-chat  对话历史裁剪与续写数组形状（不去网）
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
  ScreenKit --test-record-cursor -o log\record_cursor
  ScreenKit --test-clipboard-path
  ScreenKit --test-img-convert
  ScreenKit --test-qr-make
  ScreenKit --test-rename
  ScreenKit --test-hash
  ScreenKit --test-texttool
  ScreenKit --test-pwgen
  ScreenKit --test-nettool
  ScreenKit --test-llm-continue
  ScreenKit --test-llm-chat
  ScreenKit --test-llm-agent
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
		const string sample = "http://192.168.1.8:17532/apk";
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
			SendFileOps.Delete("a/b.txt");
			if (File.Exists(full)) {
				Err("sendfile: 删除失败");
				return 1;
			}
			Out("sendfile path/ops ok");
			return 0;
		}
		finally {
			SendFilePaths.SetRootForTest(null);
			try { Directory.Delete(dir, true); } catch { }
		}
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
