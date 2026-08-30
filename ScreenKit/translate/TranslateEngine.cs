namespace ScreenKit;

/// <summary>
/// Opus-MT 翻译引擎：进程内 ONNX Runtime（与 OCR 共用 CUDA/DirectML 策略），不依赖 Python。
/// </summary>
sealed class TranslateEngine : IDisposable {
	static readonly object Gate = new();
	readonly Dictionary<string, OpusMtOnnx> loaded = new(StringComparer.OrdinalIgnoreCase);
	/// <summary>dirKey → 加载时的 prefer。</summary>
	readonly Dictionary<string, string> loadedPrefer = new(StringComparer.OrdinalIgnoreCase);
	string lastError = "";
	bool disposed;

	/// <summary>最近一次实际设备：cuda / dml / cpu。</summary>
	public string LastDevice { get; private set; } = "";
	/// <summary>固定 onnx。</summary>
	public string LastBackend { get; private set; } = "onnx";
	public string LastError => lastError;

	public bool IsRunning {
		get {
			lock (Gate) return !disposed && loaded.Count > 0;
		}
	}

	/// <param name="devicePrefer">auto | cuda | cpu | dml</param>
	public bool EnsureLoaded(string dirKey, string modelDir, string devicePrefer = "auto") {
		lock (Gate) {
			if (disposed) {
				lastError = "引擎已释放";
				return false;
			}
			var key = (dirKey ?? "").ToLowerInvariant();
			var prefer = normalizeprefer(devicePrefer);
			var mode = modetofromprefer(prefer);
			if (loaded.TryGetValue(key, out var had)
				&& loadedPrefer.TryGetValue(key, out var hadPref)
				&& string.Equals(hadPref, prefer, StringComparison.OrdinalIgnoreCase)) {
				LastDevice = had.DeviceLabel;
				LastBackend = had.Backend;
				lastError = "";
				return true;
			}
			// prefer 变化：卸掉旧会话
			if (loaded.TryGetValue(key, out var old)) {
				try { old.Dispose(); } catch { }
				loaded.Remove(key);
				loadedPrefer.Remove(key);
			}
			try {
				if (!Directory.Exists(modelDir)) {
					lastError = "模型目录不存在: " + modelDir;
					return false;
				}
				// 仅 ONNX
				var enc = Path.Combine(modelDir, "encoder_model.onnx");
				var dec = Path.Combine(modelDir, "decoder_model.onnx");
				if (!File.Exists(enc) || !File.Exists(dec)) {
					// 旁路 -onnx 目录
					var sibling = modelDir.TrimEnd('\\', '/') + "-onnx";
					if (Directory.Exists(sibling)
						&& File.Exists(Path.Combine(sibling, "encoder_model.onnx"))
						&& File.Exists(Path.Combine(sibling, "decoder_model.onnx")))
						modelDir = sibling;
					else {
						lastError = "需要 ONNX 模型（encoder_model.onnx + decoder_model.onnx），不再支持 Python/PyTorch";
						return false;
					}
				}

				var eng = new OpusMtOnnx(modelDir);
				eng.Load(mode);
				loaded[key] = eng;
				loadedPrefer[key] = prefer;
				LastDevice = eng.DeviceLabel;
				LastBackend = eng.Backend;
				lastError = "";
				CaptureLog.Info($"Translate loaded {key} device={eng.DeviceLabel} prefer={prefer} dir={modelDir}");
				return true;
			}
			catch (Exception ex) {
				lastError = ex.Message;
				CaptureLog.Ex("Translate EnsureLoaded", ex);
				return false;
			}
		}
	}

	/// <summary>UI 计算模式 → 管道 device 字符串。</summary>
	public static string PreferFromMode(TtsComputeMode mode) => mode switch {
		TtsComputeMode.Gpu => "cuda",
		TtsComputeMode.Cpu => "cpu",
		TtsComputeMode.Igpu => "dml",
		_ => "auto",
	};

	static string normalizeprefer(string s) {
		var p = (s ?? "auto").Trim().ToLowerInvariant();
		return p switch {
			"gpu" or "cuda" => "cuda",
			"cpu" => "cpu",
			"igpu" or "dml" or "directml" => "dml",
			_ => "auto",
		};
	}

	static TtsComputeMode modetofromprefer(string s) {
		var p = normalizeprefer(s);
		return p switch {
			"cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"dml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
	}

	public string Translate(string dirKey, string text, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(text)) return "";
		lock (Gate) {
			ct.ThrowIfCancellationRequested();
			var key = (dirKey ?? "").ToLowerInvariant();
			if (!loaded.TryGetValue(key, out var eng))
				throw new InvalidOperationException(lastError.Length > 0 ? lastError : $"未加载模型 {key}");
			var result = eng.Translate(text, maxNewTokens: 256, numBeams: 4);
			LastDevice = eng.DeviceLabel;
			LastBackend = eng.Backend;
			return result;
		}
	}

	/// <summary>切换计算设备时清空已加载会话（同方向需重新 Load）。</summary>
	public void UnloadAll() {
		lock (Gate) {
			foreach (var kv in loaded) {
				try { kv.Value.Dispose(); } catch { }
			}
			loaded.Clear();
			loadedPrefer.Clear();
		}
	}

	public void Dispose() {
		lock (Gate) {
			if (disposed) return;
			disposed = true;
			foreach (var kv in loaded) {
				try { kv.Value.Dispose(); } catch { }
			}
			loaded.Clear();
			loadedPrefer.Clear();
		}
	}
}
