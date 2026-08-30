using System.IO;
using SherpaOnnx;

namespace ScreenKit;

/// <summary>Sherpa-ONNX 流式语音识别（Online Zipformer Transducer / CTC）。</summary>
sealed class AsrStreamEngine : IDisposable {
	OnlineRecognizer recognizer;
	string loadedKey;
	bool disposed;
	bool autoCudaOk = true;
	TtsComputeMode mode = TtsComputeMode.Auto;

	public string Provider { get; private set; } = "cpu";
	public string GpuFallbackReason { get; private set; }
	public bool IsLoaded => recognizer != null;
	public int FeatSampleRate { get; private set; } = 16000;

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

	public void LoadModel(AsrModelInfo model) {
		if (model == null) throw new ArgumentNullException(nameof(model));
		if (!model.IsStreaming)
			throw new InvalidOperationException("非流式模型，请用离线引擎: " + model.DisplayName);
		var key = $"{model.ModelDir}|{model.Type}|stream|{mode}";
		if (recognizer != null && loadedKey == key) return;

		Unload();
		FeatSampleRate = model.SampleRate > 0 ? model.SampleRate : 16000;
		createRecognizer(model);
		loadedKey = key;
	}

	void createRecognizer(AsrModelInfo model) {
		var dir = model.ModelDir;
		var mcfg = new OnlineModelConfig {
			Tokens = Path.Combine(dir, model.TokensFile),
			NumThreads = 2,
			Debug = 0,
			Provider = "cpu",
			ModelType = "",
			ModelingUnit = "cjkchar",
		};
		switch (model.Type) {
			case AsrModelType.Transducer:
				mcfg.Transducer = new OnlineTransducerModelConfig {
					Encoder = Path.Combine(dir, model.EncoderFile),
					Decoder = Path.Combine(dir, model.DecoderFile),
					Joiner = Path.Combine(dir, model.JoinerFile),
				};
				// 多数包可自动探测；仅在名称明确 zipformer2 时指定
				if (Compat.Contains(model.DisplayName, "zipformer2", StringComparison.OrdinalIgnoreCase))
					mcfg.ModelType = "zipformer2";
				break;
			case AsrModelType.ZipformerCtc:
				mcfg.Zipformer2Ctc = new OnlineZipformer2CtcModelConfig {
					Model = Path.Combine(dir, model.ModelFile),
				};
				break;
			default:
				throw new NotSupportedException("不支持的流式模型类型: " + model.Type);
		}

		var cudaOk = TtsEngine.ProbeCuda(out var cudaReason);
		var dmlReason = "Sherpa 当前构建不支持 DirectML";
		var reasons = new List<string>();
		var tryList = mode switch {
			TtsComputeMode.Cpu => Array.Empty<string>(),
			TtsComputeMode.Gpu => new[] { "cuda" },
			TtsComputeMode.Igpu => Array.Empty<string>(),
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
				mcfg.Provider = prov;
				recognizer = new OnlineRecognizer(buildConfig(mcfg));
				Provider = "cuda";
				GpuFallbackReason = null;
				return;
			}
			catch (Exception ex) {
				if (mode == TtsComputeMode.Auto && prov == "cuda")
					autoCudaOk = false;
				try { recognizer?.Dispose(); } catch { }
				recognizer = null;
				var msg = $"{prov}: {ex.Message}";
				if (ex.InnerException != null) msg += " | " + ex.InnerException.Message;
				reasons.Add(msg);
				try { CudaBootstrap.MarkGpuFailed(ex.Message); } catch { }
				CaptureLog.Ex("AsrStreamEngine.create " + prov, ex);
			}
		}

		if (reasons.Count > 0)
			GpuFallbackReason = string.Join(" | ", reasons);
		mcfg.Provider = "cpu";
		recognizer = new OnlineRecognizer(buildConfig(mcfg));
		Provider = "cpu";
	}

	static OnlineRecognizerConfig buildConfig(OnlineModelConfig mcfg) => new() {
		FeatConfig = new FeatureConfig { SampleRate = 16000, FeatureDim = 80 },
		ModelConfig = mcfg,
		DecodingMethod = "greedy_search",
		MaxActivePaths = 4,
		EnableEndpoint = 1,
		// 听写：静音稍短就出句
		Rule1MinTrailingSilence = 2.4f,
		Rule2MinTrailingSilence = 0.7f,
		Rule3MinUtteranceLength = 20f,
		HotwordsScore = 1.5f,
	};

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

	public OnlineStream CreateStream() {
		if (recognizer == null) throw new InvalidOperationException("流式模型未加载");
		return recognizer.CreateStream();
	}

	/// <summary>送入波形并尽可能 Decode（可能多次）。</summary>
	public void AcceptAndDecode(OnlineStream stream, float[] samples, int sampleRate) {
		if (recognizer == null || stream == null) return;
		if (samples == null || samples.Length == 0) return;
		if (sampleRate != FeatSampleRate && sampleRate > 0)
			samples = AsrAudio.Resample(samples, sampleRate, FeatSampleRate);
		stream.AcceptWaveform(FeatSampleRate, samples);
		while (recognizer.IsReady(stream))
			recognizer.Decode(stream);
	}

	public void InputFinished(OnlineStream stream) {
		if (stream == null) return;
		stream.InputFinished();
		if (recognizer == null) return;
		while (recognizer.IsReady(stream))
			recognizer.Decode(stream);
	}

	public bool IsEndpoint(OnlineStream stream) =>
		recognizer != null && stream != null && recognizer.IsEndpoint(stream);

	public string GetText(OnlineStream stream) {
		if (recognizer == null || stream == null) return "";
		var r = recognizer.GetResult(stream);
		return r?.Text?.Trim() ?? "";
	}

	public void Reset(OnlineStream stream) {
		if (recognizer == null || stream == null) return;
		recognizer.Reset(stream);
	}

	void Unload() {
		try { recognizer?.Dispose(); } catch { }
		recognizer = null;
		loadedKey = null;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Unload();
	}
}
