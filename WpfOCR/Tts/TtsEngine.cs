using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SherpaOnnx;

namespace WpfOCR;

/// <summary>Sherpa-ONNX Offline TTS（VITS / Matcha），支持 CUDA / DirectML 核显 / CPU。</summary>
sealed class TtsEngine : IDisposable {
	OfflineTts tts;
	string modelDir;
	string loadedKey;
	bool disposed;
	bool autoCudaOk = true;
	TtsComputeMode mode = TtsComputeMode.Auto;
	/// <summary>当前模型是否自带 number.fst（有则勿再手转数字，交给 FST）。</summary>
	bool hasNumberFst;
	/// <summary>当前模型是否有 lexicon/dict（中文前端）。</summary>
	bool hasFrontend;
	/// <summary>tts_config.json 的 volume 增益。</summary>
	float volumeGain = 1f;

	static readonly string[] DigitCn = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

	static readonly Dictionary<char, string> LetterCn = new() {
		['A'] = "诶", ['a'] = "诶", ['B'] = "币", ['b'] = "币",
		['C'] = "西", ['c'] = "西", ['D'] = "地", ['d'] = "地",
		['E'] = "伊", ['e'] = "伊", ['F'] = "诶付", ['f'] = "诶付",
		['G'] = "寄", ['g'] = "寄", ['H'] = "诶赤", ['h'] = "诶赤",
		['I'] = "爱", ['i'] = "爱", ['J'] = "杰", ['j'] = "杰",
		['K'] = "剋", ['k'] = "剋", ['L'] = "艾尔", ['l'] = "艾尔",
		['M'] = "艾姆", ['m'] = "艾姆", ['N'] = "恩", ['n'] = "恩",
		['O'] = "欧", ['o'] = "欧", ['P'] = "批", ['p'] = "批",
		['Q'] = "丘", ['q'] = "丘", ['R'] = "阿尔", ['r'] = "阿尔",
		['S'] = "艾斯", ['s'] = "艾斯", ['T'] = "替", ['t'] = "替",
		['U'] = "优", ['u'] = "优", ['V'] = "维", ['v'] = "维",
		['W'] = "大不溜", ['w'] = "大不溜", ['X'] = "挨克斯", ['x'] = "挨克斯",
		['Y'] = "外", ['y'] = "外", ['Z'] = "贼", ['z'] = "贼",
	};

	public int SampleRate { get; private set; }
	public string Provider { get; private set; } = "cpu";
	public string GpuFallbackReason { get; private set; }
	public bool IsLoaded => tts != null;

	public TtsComputeMode Mode {
		get => mode;
		set {
			if (mode == value) return;
			mode = value;
			loadedKey = null;
			if (value == TtsComputeMode.Auto)
				autoCudaOk = true;
		}
	}

	public void UnloadSafe() => Unload();

	public static bool ProbeCuda(out string reason) {
		reason = "";
		try {
			try { CudaBootstrap.Init(); } catch { }
			// 文件齐全后再尝试真正加载 CUDA EP（会写 log/cuda_bootstrap.log）
			try { CudaBootstrap.EnsureGpuLibsLoaded(); } catch (Exception ex) {
				reason = ex.Message;
				return false;
			}
			if (!CudaBootstrap.IsGpuReady) {
				reason = CudaBootstrap.GpuStatus ?? "CUDA 未就绪";
				return false;
			}
			var hCuda = LoadLibrary("nvcuda.dll");
			if (hCuda == IntPtr.Zero) {
				reason = "无法加载 nvcuda.dll（请安装 NVIDIA 驱动）";
				return false;
			}
			FreeLibrary(hCuda);
			reason = "OK";
			return true;
		}
		catch (Exception ex) {
			reason = ex.Message;
			return false;
		}
	}

	/// <summary>
	/// 探测 onnxdml64 文件（OCR DirectML 可用）。
	/// 注意：NuGet <c>org.k2fsa.sherpa.onnx</c> 未编译 DirectML，Sherpa TTS/ASR 无法用核显。
	/// </summary>
	public static bool ProbeDml(out string reason) {
		reason = "";
		try {
			try { CudaBootstrap.Init(); } catch { }
			if (!CudaBootstrap.IsDmlReady) {
				reason = "DirectML 不可用（缺少 onnxdml64）";
				return false;
			}
			reason = "OK(文件)";
			return true;
		}
		catch (Exception ex) {
			reason = ex.Message;
			return false;
		}
	}

	/// <summary>Sherpa 是否可用 DirectML（官方 Windows NuGet 目前为否）。</summary>
	public static bool ProbeSherpaDml(out string reason) {
		reason = "NuGet sherpa-onnx 未启用 DirectML（选核显会静默落 CPU；仅 CUDA/CPU 有效）";
		return false;
	}

	public void LoadModel(TtsModelInfo model) {
		if (model == null) throw new ArgumentNullException(nameof(model));
		var key = $"{model.ModelDir}|{model.OnnxFile}|{model.VocoderPath}|{model.Type}|{mode}";
		if (tts != null && loadedKey == key) return;

		Unload();
		modelDir = model.ModelDir;
		GpuFallbackReason = null;
		hasNumberFst = File.Exists(Path.Combine(modelDir, "number.fst"));
		hasFrontend = model.HasLexicon || model.HasDictDir
			|| File.Exists(Path.Combine(modelDir, "lexicon.txt"));
		volumeGain = model.Volume > 0 && !float.IsNaN(model.Volume) && !float.IsInfinity(model.Volume)
			? Compat.Clamp(model.Volume, 0.05f, 16f)
			: 1f;

		if (model.Type == TtsModelType.Matcha)
			loadMatcha(model);
		else
			loadVits(model);

		try {
			var warm = tts.GenerateWithConfig("预热", new OfflineTtsGenerationConfig { Speed = 1f }, null);
			TtsAudioFix.Free(warm);
		}
		catch { }

		loadedKey = key;
	}

	void loadVits(TtsModelInfo model) {
		var vits = new OfflineTtsVitsModelConfig {
			Model = Path.Combine(modelDir, model.OnnxFile),
			Tokens = Path.Combine(modelDir, "tokens.txt"),
		};
		if (model.HasLexicon)
			vits.Lexicon = Path.Combine(modelDir, "lexicon.txt");
		if (model.HasDictDir)
			vits.DictDir = Path.Combine(modelDir, "dict");
		var mcfg = new OfflineTtsModelConfig { Vits = vits, NumThreads = 4 };
		createTts(mcfg);
	}

	void loadMatcha(TtsModelInfo model) {
		var matcha = new OfflineTtsMatchaModelConfig {
			AcousticModel = Path.Combine(modelDir, model.OnnxFile),
			Vocoder = model.VocoderPath,
			Tokens = Path.Combine(modelDir, "tokens.txt"),
		};
		if (model.HasLexicon)
			matcha.Lexicon = Path.Combine(modelDir, "lexicon.txt");
		if (model.HasDictDir)
			matcha.DictDir = Path.Combine(modelDir, "dict");
		var mcfg = new OfflineTtsModelConfig { Matcha = matcha, NumThreads = 4 };
		createTts(mcfg);
	}

	void createTts(OfflineTtsModelConfig mcfg) {
		var cudaOk = ProbeCuda(out var cudaReason);
		// 官方 NuGet sherpa 无 DirectML，勿尝试（否则会静默 CPU 却报 directml）
		var dmlOk = ProbeSherpaDml(out var dmlReason);
		var reasons = new List<string>();

		var tryList = mode switch {
			TtsComputeMode.Cpu => Array.Empty<string>(),
			TtsComputeMode.Gpu => new[] { "cuda" },
			TtsComputeMode.Igpu => Array.Empty<string>(), // 见 dmlReason
			// Auto：仅 CUDA → CPU（Sherpa 无 DML）
			_ => autoCudaOk && cudaOk ? new[] { "cuda" } : Array.Empty<string>(),
		};

		if (mode == TtsComputeMode.Auto && tryList.Length == 0) {
			if (!cudaOk) reasons.Add("CUDA: " + cudaReason);
			reasons.Add("DML: " + dmlReason);
		}
		else if (mode == TtsComputeMode.Gpu && !cudaOk)
			reasons.Add("CUDA: " + cudaReason);
		else if (mode == TtsComputeMode.Igpu)
			reasons.Add("DML: " + dmlReason);

		foreach (var prov in tryList) {
			try {
				prepareort(prov);
				tts = buildTts(ref mcfg, prov);
				if (tts != null && tts.SampleRate > 0) {
					Provider = "cuda";
					SampleRate = tts.SampleRate;
					GpuFallbackReason = null;
					return;
				}
			}
			catch (Exception ex) {
				if (mode == TtsComputeMode.Auto && prov == "cuda")
					autoCudaOk = false;
				try { tts?.Dispose(); } catch { }
				tts = null;
				var msg = $"{prov}: {ex.GetType().Name}: {ex.Message}";
				if (ex.InnerException != null)
					msg += " | " + ex.InnerException.Message;
				reasons.Add(msg);
				try { CudaBootstrap.MarkGpuFailed(ex.Message); } catch { }
				CaptureLog.Ex("TtsEngine.createTts " + prov, ex);
			}
		}

		// CPU 回退
		if (reasons.Count > 0)
			GpuFallbackReason = string.Join(" | ", reasons);
		tts = buildTts(ref mcfg, "cpu");
		Provider = "cpu";
		SampleRate = tts.SampleRate;
	}

	/// <summary>加载对应 ORT 原生库。</summary>
	static void prepareort(string provider) {
		if (provider == "cuda") {
			CudaBootstrap.EnsureGpuLibsLoaded();
			if (!CudaBootstrap.IsGpuReady)
				throw new InvalidOperationException(
					CudaBootstrap.GpuStatus ?? "CUDA 运行库不可用");
		}
		else if (provider == "directml")
			throw new InvalidOperationException(
				"Sherpa 当前构建不支持 DirectML，请改用 CUDA 或 CPU");
	}

	OfflineTts buildTts(ref OfflineTtsModelConfig mcfg, string provider) {
		// sherpa-onnx：cpu | cuda | directml …
		mcfg.Provider = provider;
		var cfg = new OfflineTtsConfig { Model = mcfg };
		applyRuleFsts(ref cfg);
		return new OfflineTts(cfg);
	}

	void applyRuleFsts(ref OfflineTtsConfig cfg) {
		// 顺序参考 sherpa-onnx 中文模型：多音字 → 日期 → 数字 → 电话
		var fsts = new List<string>();
		foreach (var f in new[] {
			"new_heteronym.fst", "heteronym.fst",
			"date.fst", "number.fst", "phone.fst",
		}) {
			var p = Path.Combine(modelDir, f);
			if (File.Exists(p) && !fsts.Contains(p))
				fsts.Add(p);
		}
		if (fsts.Count > 0)
			cfg.RuleFsts = string.Join(",", fsts);
		var farPath = Path.Combine(modelDir, "rule.far");
		if (File.Exists(farPath))
			cfg.RuleFars = farPath;
	}

	/// <param name="applyVolume">false 时不做 tts_config volume 增益（音高探测用，避免削波）。</param>
	public (float[] samples, int sampleRate) Synthesize(string text, int sid = 0, float speed = 1f, bool applyVolume = true) {
		if (tts == null) throw new InvalidOperationException("模型未加载");
		// 长数字串一律逐位读；短数字：有 number.fst 则留给 FST，否则转中文
		var normalized = NormalizeText(text, preferFstNumbers: hasNumberFst, convertLetters: true);
		var genCfg = new OfflineTtsGenerationConfig {
			Sid = sid,
			Speed = speed,
			SilenceScale = 0.2f,
		};
		var audio = tts.GenerateWithConfig(normalized, genCfg, null);
		if (audio == null || audio.Samples == null || audio.Samples.Length == 0) {
			TtsAudioFix.Free(audio);
			throw new Exception("合成失败：输出为空");
		}
		var samples = new float[audio.Samples.Length];
		audio.Samples.CopyTo(samples, 0);
		var sr = audio.SampleRate;
		TtsAudioFix.Free(audio);
		// tts_config.json volume：线性放大，并硬限幅防削波爆音
		if (applyVolume && Math.Abs(volumeGain - 1f) > 1e-4f)
			applygain(samples, volumeGain);
		return (samples, sr);
	}

	static void applygain(float[] samples, float gain) {
		if (samples == null || samples.Length == 0) return;
		for (int i = 0; i < samples.Length; i++) {
			var v = samples[i] * gain;
			if (v > 1f) v = 1f;
			else if (v < -1f) v = -1f;
			samples[i] = v;
		}
	}

	/// <summary>
	/// 合成前文本规范化。
	/// 长数字/序列号：一律逐位读（一二三…），避免 number.fst 当成超大整数。
	/// 短数字：preferFstNumbers 时保留阿拉伯数字给 FST；否则中文自然读法。
	/// </summary>
	/// <param name="preferFstNumbers">true=有 number.fst，短数字不手转。</param>
	public static string NormalizeText(string text, bool preferFstNumbers = false, bool convertLetters = true) {
		if (string.IsNullOrEmpty(text)) return text;
		text = text.Replace("\u200b", "").Replace("\ufeff", "");
		var sb = new StringBuilder(text.Length * 2);
		int i = 0;
		while (i < text.Length) {
			var c = text[i];
			if (c >= '0' && c <= '9') {
				int start = i;
				while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
				var digits = text.Substring(start, i - start);
				appenddigits(sb, digits, preferFstNumbers);
			}
			else if (convertLetters && LetterCn.TryGetValue(c, out var cn)) {
				if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]))
					sb.Append(' ');
				sb.Append(cn);
				i++;
				while (i < text.Length && LetterCn.ContainsKey(text[i])) {
					sb.Append(' ');
					sb.Append(LetterCn[text[i]]);
					i++;
				}
			}
			else if (c is '\r' or '\n' or '\t') {
				if (sb.Length > 0 && sb[sb.Length - 1] is not ('，' or '。' or ',' or '.' or ' '))
					sb.Append('，');
				i++;
				while (i < text.Length && text[i] is '\r' or '\n' or '\t' or ' ')
					i++;
			}
			else {
				sb.Append(c);
				i++;
			}
		}
		return sb.ToString();
	}

	/// <summary>
	/// 数字读法：≥5 位或前导零 → 逐位（一 二 三…）；
	/// 否则短数字交 FST 或自然数读法。
	/// </summary>
	static void appenddigits(StringBuilder sb, string digits, bool preferFstNumbers) {
		if (string.IsNullOrEmpty(digits)) return;
		// 序列号 / 超长串：必须逐位读
		if (looksLikeSerial(digits)) {
			appenddigitbydigit(sb, digits);
			return;
		}
		// 短数字：有 number.fst 则保留 1234 给 FST
		if (preferFstNumbers) {
			sb.Append(digits);
			return;
		}
		if (digits.Length == 1)
			sb.Append(DigitCn[digits[0] - '0']);
		else
			sb.Append(numToCn(digits));
	}

	static void appenddigitbydigit(StringBuilder sb, string digits) {
		for (int k = 0; k < digits.Length; k++) {
			if (k > 0) sb.Append(' ');
			var d = digits[k];
			if (d >= '0' && d <= '9')
				sb.Append(DigitCn[d - '0']);
			else
				sb.Append(d);
		}
	}

	/// <summary>≥5 位连续数字，或前导零多位 → 按序列号逐位读。</summary>
	static bool looksLikeSerial(string digits) {
		if (digits.Length >= 5) return true;
		if (digits.Length > 1 && digits[0] == '0') return true;
		return false;
	}

	static string numToCn(string num) {
		var len = num.Length;
		if (len == 0) return "";
		if (len == 1) return DigitCn[num[0] - '0'];
		var units = new[] { "", "十", "百", "千" };
		var sb = new StringBuilder();
		var hasZero = false;
		for (int i = 0; i < len; i++) {
			int d = num[i] - '0';
			int pos = len - 1 - i;
			if (d == 0) { hasZero = true; continue; }
			if (hasZero) { sb.Append('零'); hasZero = false; }
			if (pos == 1 && d == 1 && (len == 2 || i == 0))
				sb.Append(units[pos]);
			else {
				sb.Append(DigitCn[d]);
				if (pos > 0) sb.Append(units[pos]);
			}
		}
		return sb.ToString();
	}

	void Unload() {
		var t = tts;
		tts = null;
		modelDir = null;
		loadedKey = null;
		hasNumberFst = false;
		hasFrontend = false;
		volumeGain = 1f;
		TtsAudioFix.FreeTts(t);
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Unload();
	}

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern IntPtr LoadLibrary(string lpFileName);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool FreeLibrary(IntPtr hModule);
}
