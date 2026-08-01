using System.IO;
using SherpaOnnx;

namespace WpfOCR;

/// <summary>Sherpa-ONNX 离线语音识别（SenseVoice / Paraformer / Transducer / Whisper）。</summary>
sealed class AsrEngine : IDisposable {
	OfflineRecognizer recognizer;
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

	public void LoadModel(AsrModelInfo model, string language = "auto", bool useItn = true) {
		if (model == null) throw new ArgumentNullException(nameof(model));
		if (model.IsStreaming)
			throw new InvalidOperationException(
				"该模型为流式包，请用于语音输入热键，或在文件识别中选离线模型: " + model.DisplayName);
		var key = $"{model.ModelDir}|{model.Type}|{language}|{useItn}|{mode}";
		if (recognizer != null && loadedKey == key) return;

		Unload();
		FeatSampleRate = model.SampleRate > 0 ? model.SampleRate : 16000;
		var mcfg = buildModelConfig(model, language, useItn);
		createRecognizer(ref mcfg);
		loadedKey = key;
	}

	static OfflineModelConfig buildModelConfig(AsrModelInfo model, string language, bool useItn) {
		var dir = model.ModelDir;
		var mcfg = new OfflineModelConfig {
			Tokens = Path.Combine(dir, model.TokensFile),
			NumThreads = 4,
			Debug = 0,
			Provider = "cpu",
		};
		switch (model.Type) {
			case AsrModelType.SenseVoice:
				mcfg.SenseVoice = new OfflineSenseVoiceModelConfig {
					Model = Path.Combine(dir, model.ModelFile),
					Language = string.IsNullOrWhiteSpace(language) ? "auto" : language,
					UseInverseTextNormalization = useItn ? 1 : 0,
				};
				break;
			case AsrModelType.Paraformer:
				mcfg.Paraformer = new OfflineParaformerModelConfig {
					Model = Path.Combine(dir, model.ModelFile),
				};
				break;
			case AsrModelType.Transducer:
				mcfg.Transducer = new OfflineTransducerModelConfig {
					Encoder = Path.Combine(dir, model.EncoderFile),
					Decoder = Path.Combine(dir, model.DecoderFile),
					Joiner = Path.Combine(dir, model.JoinerFile),
				};
				break;
			case AsrModelType.Whisper:
				mcfg.Whisper = new OfflineWhisperModelConfig {
					Encoder = Path.Combine(dir, model.EncoderFile),
					Decoder = Path.Combine(dir, model.DecoderFile),
					Language = string.IsNullOrWhiteSpace(language) || language == "auto" ? "" : language,
					Task = "transcribe",
				};
				break;
			case AsrModelType.ZipformerCtc:
				mcfg.ZipformerCtc = new OfflineZipformerCtcModelConfig {
					Model = Path.Combine(dir, model.ModelFile),
				};
				break;
			default:
				throw new NotSupportedException("不支持的 ASR 模型类型: " + model.Type);
		}
		return mcfg;
	}

	void createRecognizer(ref OfflineModelConfig mcfg) {
		var cudaOk = TtsEngine.ProbeCuda(out var cudaReason);
		// NuGet sherpa 无 DirectML
		var dmlOk = TtsEngine.ProbeSherpaDml(out var dmlReason);
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
				recognizer = new OfflineRecognizer(new OfflineRecognizerConfig {
					FeatConfig = new FeatureConfig { SampleRate = FeatSampleRate, FeatureDim = 80 },
					ModelConfig = mcfg,
					DecodingMethod = "greedy_search",
				});
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
				CaptureLog.Ex("AsrEngine.createRecognizer " + prov, ex);
			}
		}

		if (reasons.Count > 0)
			GpuFallbackReason = string.Join(" | ", reasons);
		mcfg.Provider = "cpu";
		recognizer = new OfflineRecognizer(new OfflineRecognizerConfig {
			FeatConfig = new FeatureConfig { SampleRate = FeatSampleRate, FeatureDim = 80 },
			ModelConfig = mcfg,
			DecodingMethod = "greedy_search",
		});
		Provider = "cpu";
	}

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

	/// <summary>识别 float 波形（-1~1），采样率需与模型一致或由调用方重采样。</summary>
	public string Recognize(float[] samples, int sampleRate) {
		return RecognizeDetailed(samples, sampleRate).Text;
	}

	/// <summary>识别并返回 token / 时间戳（用于 SRT）。</summary>
	public AsrResult RecognizeDetailed(float[] samples, int sampleRate) {
		if (recognizer == null) throw new InvalidOperationException("模型未加载");
		if (samples == null || samples.Length == 0) return AsrResult.Empty;
		if (sampleRate != FeatSampleRate)
			samples = AsrAudio.Resample(samples, sampleRate, FeatSampleRate);
		using var stream = recognizer.CreateStream();
		stream.AcceptWaveform(FeatSampleRate, samples);
		recognizer.Decode(stream);
		var r = stream.Result;
		if (r == null) return AsrResult.Empty;
		return new AsrResult {
			Text = r.Text?.Trim() ?? "",
			Tokens = r.Tokens ?? Array.Empty<string>(),
			Timestamps = r.Timestamps ?? Array.Empty<float>(),
			Durations = r.Durations ?? Array.Empty<float>(),
		};
	}

	/// <summary>
	/// 长音频分块识别（默认 25s 一块），时间戳叠加块偏移，便于生成字幕。
	/// <paramref name="onChunk"/>：每块完成后回调（累计结果、当前已处理到的秒数、总时长秒）。
	/// <paramref name="ct"/>：块与块之间可取消（当前块解码中无法打断）。
	/// </summary>
	public AsrResult RecognizeLong(float[] samples, int sampleRate, float chunkSec = 25f,
		Action<AsrResult, double, double> onChunk = null, CancellationToken ct = default) {
		if (recognizer == null) throw new InvalidOperationException("模型未加载");
		if (samples == null || samples.Length == 0) return AsrResult.Empty;
		ct.ThrowIfCancellationRequested();
		if (sampleRate != FeatSampleRate) {
			samples = AsrAudio.Resample(samples, sampleRate, FeatSampleRate);
			sampleRate = FeatSampleRate;
		}
		if (chunkSec < 5f) chunkSec = 5f;
		var chunkN = Math.Max(1, (int)(chunkSec * sampleRate));
		var totalSec = samples.Length / (double)sampleRate;

		// 短音频直接整段
		if (samples.Length <= chunkN + sampleRate) {
			ct.ThrowIfCancellationRequested();
			var one = RecognizeDetailed(samples, sampleRate);
			try { onChunk?.Invoke(one, totalSec, totalSec); } catch { }
			ct.ThrowIfCancellationRequested();
			return one;
		}

		var texts = new List<string>();
		var tokens = new List<string>();
		var stamps = new List<float>();
		var durs = new List<float>();
		for (int off = 0; off < samples.Length; off += chunkN) {
			ct.ThrowIfCancellationRequested();
			var n = Math.Min(chunkN, samples.Length - off);
			if (n < sampleRate / 20) break; // <50ms 忽略
			var slice = new float[n];
			Array.Copy(samples, off, slice, 0, n);
			var part = RecognizeDetailed(slice, sampleRate);
			ct.ThrowIfCancellationRequested();
			var t0 = off / (float)sampleRate;
			if (!string.IsNullOrWhiteSpace(part.Text))
				texts.Add(part.Text.Trim());
			if (part.Tokens != null && part.Timestamps != null
			    && part.Tokens.Length > 0 && part.Timestamps.Length == part.Tokens.Length) {
				for (int i = 0; i < part.Tokens.Length; i++) {
					tokens.Add(part.Tokens[i] ?? "");
					stamps.Add(part.Timestamps[i] + t0);
					if (part.Durations != null && i < part.Durations.Length)
						durs.Add(part.Durations[i]);
					else
						durs.Add(0f);
				}
			}
			else if (!string.IsNullOrWhiteSpace(part.Text)) {
				// 无 token 时轴：按标点切成多段，均分本块时长，避免整块一条跨很长静音
				var chunkDur = n / (float)sampleRate;
				var pieces = splitTextForTimestamps(part.Text.Trim());
				if (pieces.Count == 0) {
					tokens.Add(part.Text.Trim());
					stamps.Add(t0);
					durs.Add(chunkDur);
				}
				else {
					var totalW = pieces.Sum(p => Math.Max(1, p.Length));
					float cursor = t0;
					foreach (var p in pieces) {
						var frac = Math.Max(1, p.Length) / (float)totalW;
						var d = Math.Max(0.15f, chunkDur * frac);
						tokens.Add(p);
						stamps.Add(cursor);
						durs.Add(d);
						cursor += d;
					}
				}
			}

			var posSec = Math.Min(totalSec, (off + n) / (double)sampleRate);
			var partial = new AsrResult {
				Text = string.Join("", texts).Trim(),
				Tokens = tokens.ToArray(),
				Timestamps = stamps.ToArray(),
				Durations = durs.ToArray(),
			};
			try { onChunk?.Invoke(partial, posSec, totalSec); } catch { }
		}
		ct.ThrowIfCancellationRequested();
		return new AsrResult {
			Text = string.Join("", texts).Trim(),
			Tokens = tokens.ToArray(),
			Timestamps = stamps.ToArray(),
			Durations = durs.ToArray(),
		};
	}

	/// <summary>无时轴时粗切：按中英文标点拆句，供分块时间分配。</summary>
	static List<string> splitTextForTimestamps(string text) {
		var list = new List<string>();
		if (string.IsNullOrWhiteSpace(text)) return list;
		var sb = new System.Text.StringBuilder();
		foreach (var ch in text) {
			sb.Append(ch);
			if (ch is '。' or '！' or '？' or '；' or '，' or '、' or ',' or '.' or '!' or '?' or ';' or '\n') {
				var s = sb.ToString().Trim().TrimEnd('，', '、', ',');
				if (s.Length > 0) list.Add(s);
				sb.Clear();
			}
		}
		var tail = sb.ToString().Trim();
		if (tail.Length > 0) list.Add(tail);
		if (list.Count == 0) list.Add(text.Trim());
		return list;
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
