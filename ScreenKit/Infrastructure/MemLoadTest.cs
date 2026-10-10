using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>每个功能单独进程冷启动加载，打印提交大小、专用工作集、工作集。</summary>
static class MemLoadTest {
	const int ProcessVmCounters = 3;
	const int CHILD_MS = 360000;

	static readonly string[] Names = { "base", "ocr", "tr", "asr", "asrstream", "tts", "dict", "face" };

	[DllImport("ntdll.dll")]
	static extern int NtQueryInformationProcess(IntPtr process, int pic, ref VmCounters info, int size, out int ret);

	[StructLayout(LayoutKind.Sequential)]
	struct VmCounters {
		public UIntPtr PeakVirtualSize;
		public UIntPtr VirtualSize;
		public uint PageFaultCount;
		public UIntPtr PeakWorkingSetSize;
		public UIntPtr WorkingSetSize;
		public UIntPtr QuotaPeakPagedPoolUsage;
		public UIntPtr QuotaPagedPoolUsage;
		public UIntPtr QuotaPeakNonPagedPoolUsage;
		public UIntPtr QuotaNonPagedPoolUsage;
		public UIntPtr PagefileUsage;
		public UIntPtr PeakPagefileUsage;
		public UIntPtr PrivateUsage;
		public UIntPtr PrivateWorkingSetSize;
		public ulong SharedCommitUsage;
	}

	struct Snap {
		public long Ws;
		public long Pws;
		public long Priv;
		public long Gc;
	}

	public static int Run(string feat, string device) {
		if (string.IsNullOrEmpty(feat) || feat == "all" || (isdev(feat) && string.IsNullOrEmpty(device)))
			return runall(isdev(feat) ? normdev(feat) : normdev(device));
		return runone(feat.Trim().ToLowerInvariant(), normdev(string.IsNullOrEmpty(device) ? "cpu" : device));
	}

	static int runall(string dev) {
		try {
			var path = reportpath();
			if (File.Exists(path)) File.Delete(path);
		}
		catch { }
		say("=== 分功能内存 --test-mem-load " + dev + " ===");
		say("每项单独进程。privMB 是提交，pwsMB 是专用工作集，wsMB 是工作集，dprivMB 是相对本进程加载前的提交增量。");
		var exe = Process.GetCurrentProcess().MainModule.FileName;
		var bad = 0;
		foreach (var name in Names) {
			var row = spawn(exe, name, dev);
			say(row);
			if (row.IndexOf("fail=", StringComparison.Ordinal) >= 0) bad++;
		}
		say(bad == 0 ? "=== OK：分功能内存 ===" : "=== FAIL：分功能内存 ===");
		return bad == 0 ? 0 : 1;
	}

	static string spawn(string exe, string name, string dev) {
		var psi = new ProcessStartInfo {
			FileName = exe,
			Arguments = "--test-mem-load " + name + " " + dev,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
		};
		psi.EnvironmentVariables["MEMLOAD_CHILD"] = "1";
		try {
			using var p = Process.Start(psi);
			if (p == null) return line(name, dev, "", 0, new Snap(), 0, "fail=no-process");
			var stdout = "";
			var stderr = "";
			var tOut = new Thread(() => { try { stdout = p.StandardOutput.ReadToEnd(); } catch { } });
			var tErr = new Thread(() => { try { stderr = p.StandardError.ReadToEnd(); } catch { } });
			tOut.IsBackground = true;
			tErr.IsBackground = true;
			tOut.Start();
			tErr.Start();
			if (!p.WaitForExit(CHILD_MS)) {
				try { p.Kill(); } catch { }
				tOut.Join(2000);
				tErr.Join(2000);
				return line(name, dev, "", 0, new Snap(), 0, "fail=timeout");
			}
			tOut.Join();
			tErr.Join();
			var hit = pickline(stdout);
			if (hit.Length > 0) return hit;
			var err = clean(stderr);
			if (err == "-") err = "fail=exit-" + p.ExitCode;
			else if (err.IndexOf("fail=", StringComparison.Ordinal) < 0) err = "fail=" + err;
			return line(name, dev, "", 0, new Snap(), 0, err);
		}
		catch (Exception ex) {
			return line(name, dev, "", 0, new Snap(), 0, "fail=" + clean(ex.Message));
		}
	}

	static string pickline(string text) {
		if (string.IsNullOrEmpty(text)) return "";
		foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
			var s = raw.Trim();
			if (s.StartsWith("MEMLOAD ", StringComparison.Ordinal)) return s;
		}
		return "";
	}

	static int runone(string name, string dev) {
		var before = sample();
		var model = "";
		var note = "";
		IDisposable hold = null;
		var t0 = Environment.TickCount;
		try {
			switch (name) {
				case "base":
					model = "-";
					break;
				case "ocr":
					hold = loadocr(dev, out model, out note);
					break;
				case "winocr":
					loadwinocr(out model, out note);
					break;
				case "tr":
					hold = loadtr(dev, out model, out note);
					break;
				case "asr":
					hold = loadasr(dev, false, out model, out note);
					break;
				case "asrstream":
					hold = loadasr(dev, true, out model, out note);
					break;
				case "tts":
					hold = loadtts(dev, out model, out note);
					break;
				case "dict":
					loaddict(out model, out note);
					break;
				case "face":
					hold = loadface(dev, out model, out note);
					break;
				default:
					note = "fail=unknown";
					break;
			}
		}
		catch (Exception ex) {
			note = "fail=" + ex.Message;
		}
		var ms = unchecked(Environment.TickCount - t0);
		var after = sample();
		var text = line(name, dev, model, ms, after, after.Priv - before.Priv, note);
		if (Environment.GetEnvironmentVariable("MEMLOAD_CHILD") == "1")
			Console.WriteLine(text);
		else
			say(text);
		try { hold?.Dispose(); } catch { }
		if (name == "dict") {
			try { DictDb.Close(); } catch { }
		}
		return note.StartsWith("fail", StringComparison.Ordinal) ? 1 : 0;
	}

	static void loadwinocr(out string model, out string note) {
		note = "";
		var opt = new OcrOptions { ModelPackId = WinOcr.PackId };
		try {
			var saved = new OcrOptions();
			AppConfig.LoadInto(saved);
			if (!string.IsNullOrEmpty(saved.WinOcrLangs)) opt.WinOcrLangs = saved.WinOcrLangs;
		}
		catch { }
		var png = Convert.FromBase64String(
			"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
		var r = WinOcr.RecognizeBytes(opt, png);
		model = "winocr/" + (string.IsNullOrEmpty(r?.ModelLabel) ? "Windows" : r.ModelLabel);
	}

	static IDisposable loadocr(string dev, out string model, out string note) {
		note = "";
		var pack = ModelCatalog.Find(null);
		if (pack == null) {
			model = "";
			note = "skip=no-ocr-model";
			return null;
		}
		var opt = new OcrOptions {
			Device = ocrdev(dev),
			UseCls = true,
			ModelPackId = pack.Id,
			ModelsDir = pack.Dir,
		};
		var v = pack.FindVariant(null);
		if (v != null) opt.ModelVariant = v.Title;
		var eng = new OcrEngine(opt);
		model = pack.Id + "/" + eng.DeviceUsed;
		return eng;
	}

	static IDisposable loadtr(string dev, out string model, out string note) {
		note = "";
		var models = TranslateModelScanner.Scan();
		var m = models.FirstOrDefault(x => x.IsReady
			&& string.Equals(x.DirKey, "zh-en", StringComparison.OrdinalIgnoreCase));
		if (m == null) m = models.FirstOrDefault(x => x.IsReady);
		if (m == null) {
			model = "";
			note = "skip=no-tr-model";
			return null;
		}
		var eng = new TranslateEngine();
		var prefer = dev == "auto" ? "auto" : dev;
		if (!eng.EnsureLoaded(m.DirKey, m.ModelDir, prefer)) {
			model = m.DirKey;
			note = "fail=" + eng.LastError;
			eng.Dispose();
			return null;
		}
		model = m.DirKey + "/" + eng.LastDevice;
		return eng;
	}

	static IDisposable loadasr(string dev, bool stream, out string model, out string note) {
		note = "";
		var list = AsrModelScanner.Scan().Where(m => m.IsStreaming == stream && !m.IsWindows).ToList();
		var pick = picklightest(list, m => m.ModelDir);
		if (pick == null) {
			model = "";
			note = stream ? "skip=no-asr-stream" : "skip=no-asr-model";
			return null;
		}
		var mode = computemode(dev);
		if (stream) {
			var eng = new AsrStreamEngine { Mode = mode };
			eng.LoadModel(pick);
			model = Path.GetFileName(pick.ModelDir) + "/" + eng.Provider;
			note = "pick=lightest";
			return eng;
		}
		var off = new AsrEngine { Mode = mode };
		off.LoadModel(pick, "auto", true);
		model = Path.GetFileName(pick.ModelDir) + "/" + off.Provider;
		note = "pick=lightest";
		return off;
	}

	static IDisposable loadtts(string dev, out string model, out string note) {
		note = "";
		var list = TtsModelScanner.Scan();
		var pick = picklightest(list, m => m.ModelDir);
		if (pick == null) {
			model = "";
			note = "skip=no-tts-model";
			return null;
		}
		var eng = new TtsEngine { Mode = computemode(dev) };
		eng.LoadModel(pick);
		model = pick.DisplayName + "/" + eng.Provider;
		note = "pick=lightest";
		return eng;
	}

	static void loaddict(out string model, out string note) {
		note = "";
		var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict2.db");
		if (!File.Exists(path)) {
			model = "";
			note = "skip=no-dict2.db";
			return;
		}
		if (!DictDb.Init(path)) {
			model = "dict2.db";
			note = "fail=" + (DictDb.Error ?? "init");
			return;
		}
		var n = 0;
		try { n = DictDb.Search("hello", "", 3).Count; } catch { }
		var len = new FileInfo(path).Length;
		model = "dict2.db/" + (len / (1024 * 1024)) + "MB/hits=" + n;
	}

	static IDisposable loadface(string dev, out string model, out string note) {
		note = "";
		var onnx = FaceModels.ListOnnx();
		var det = onnx.FirstOrDefault(FaceModels.IsDetFile);
		var reg = onnx.FirstOrDefault(FaceModels.IsRegFile);
		if (det == null || reg == null) {
			model = "";
			note = "skip=no-face-model";
			return null;
		}
		var pipe = new FacePipeline(FaceModels.PathOf(det), FaceModels.PathOf(reg), 0.5f, computemode(dev));
		model = det + "+" + reg + "/" + pipe.EpLabel;
		return pipe;
	}

	static T picklightest<T>(List<T> list, Func<T, string> dir) where T : class {
		T best = null;
		long size = long.MaxValue;
		if (list == null) return null;
		foreach (var item in list) {
			var n = MemUsage.DirWeight(dir(item));
			if (best == null || n < size) {
				best = item;
				size = n;
			}
		}
		return best;
	}

	static TtsComputeMode computemode(string dev) {
		if (dev == "cuda") return TtsComputeMode.Gpu;
		if (dev == "dml") return TtsComputeMode.Igpu;
		if (dev == "auto") return TtsComputeMode.Auto;
		return TtsComputeMode.Cpu;
	}

	static OcrDevice ocrdev(string dev) {
		if (dev == "cuda") return OcrDevice.Gpu;
		if (dev == "dml") return OcrDevice.IntelGpu;
		if (dev == "auto" && CudaBootstrap.IsGpuReady) return OcrDevice.Gpu;
		return OcrDevice.Cpu;
	}

	static Snap sample() {
		using var p = Process.GetCurrentProcess();
		p.Refresh();
		long pws = -1;
		try {
			var info = new VmCounters();
			if (NtQueryInformationProcess(p.Handle, ProcessVmCounters, ref info, Marshal.SizeOf(typeof(VmCounters)), out _) == 0)
				pws = (long)info.PrivateWorkingSetSize;
		}
		catch { }
		return new Snap {
			Ws = p.WorkingSet64,
			Pws = pws,
			Priv = p.PrivateMemorySize64,
			Gc = GC.GetTotalMemory(false),
		};
	}

	static void say(string s) {
		try { Console.WriteLine(s); } catch { }
		try { File.AppendAllText(reportpath(), s + "\n", new System.Text.UTF8Encoding(false)); } catch { }
	}

	static string reportpath() {
		var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
		try { Directory.CreateDirectory(dir); } catch { }
		return Path.Combine(dir, "mem-load.txt");
	}

	static string line(string name, string dev, string model, int ms, Snap snap, long dpriv, string note) {
		return "MEMLOAD"
			+ " name=" + name
			+ " dev=" + dev
			+ " model=" + clean(model)
			+ " loadMs=" + ms
			+ " privMB=" + mb(snap.Priv)
			+ " pwsMB=" + (snap.Pws < 0 ? "-1" : mb(snap.Pws))
			+ " wsMB=" + mb(snap.Ws)
			+ " gcMB=" + mb(snap.Gc)
			+ " dprivMB=" + mb(dpriv)
			+ " note=" + clean(note);
	}

	static string mb(long bytes) {
		var neg = bytes < 0;
		var v = neg ? -bytes : bytes;
		var n = (v + 512 * 1024) / (1024 * 1024);
		if (neg && n == 0) return "0";
		return (neg ? "-" : "") + n.ToString();
	}

	static string clean(string s) {
		if (string.IsNullOrEmpty(s)) return "-";
		s = s.Replace("\r", " ").Replace("\n", " ").Trim();
		if (s.Length > 180) s = s.Substring(0, 180);
		return s.Length == 0 ? "-" : s;
	}

	static bool isdev(string s) {
		switch ((s ?? "").Trim().ToLowerInvariant()) {
			case "cpu":
			case "gpu":
			case "cuda":
			case "nvidia":
			case "dml":
			case "igpu":
			case "intel":
			case "directml":
			case "auto":
				return true;
			default:
				return false;
		}
	}

	static string normdev(string dev) {
		switch ((dev ?? "").Trim().ToLowerInvariant()) {
			case "gpu":
			case "cuda":
			case "nvidia":
				return "cuda";
			case "dml":
			case "igpu":
			case "intel":
			case "directml":
				return "dml";
			case "auto":
				return "auto";
			default:
				return "cpu";
		}
	}
}
