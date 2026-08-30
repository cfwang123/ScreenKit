using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ScreenKit;

/// <summary>
/// Opus-MT ONNX 推理：encoder + decoder（贪心 / beam），复用 OCR 的 ORT / CUDA / DirectML。
/// </summary>
sealed class OpusMtOnnx : IDisposable {
	readonly object gate = new();
	readonly string modelDir;
	InferenceSession enc;
	InferenceSession dec;
	MarianTokenizer tok;
	bool disposed;

	int startId = 65000;
	int eosId;
	int padId = 65000;
	int forcedEos;
	int cfgMax = 512;

	public string DeviceLabel { get; private set; } = "cpu";
	public string Backend => "onnx";

	public OpusMtOnnx(string modelDir) {
		this.modelDir = Path.GetFullPath(modelDir ?? "");
	}

	public void Load(TtsComputeMode mode) {
		lock (gate) {
			if (disposed) throw new ObjectDisposedException(nameof(OpusMtOnnx));
			DisposeSessions();
			loadgenconfig();
			tok = MarianTokenizer.Load(modelDir);
			// 与 gen config 对齐（pad/eos 以 gen 为准）
			if (eosId == 0) eosId = tok.EosId;
			if (padId == 0 && tok.PadId > 0) padId = tok.PadId;
			if (forcedEos == 0) forcedEos = eosId;

			var encPath = Path.Combine(modelDir, "encoder_model.onnx");
			var decPath = Path.Combine(modelDir, "decoder_model.onnx");
			if (!File.Exists(encPath) || !File.Exists(decPath))
				throw new FileNotFoundException($"缺少 encoder/decoder onnx @ {modelDir}");

			var (e, d, label) = createsessions(encPath, decPath, mode);
			enc = e;
			dec = d;
			DeviceLabel = label;
		}
	}

	void loadgenconfig() {
		var path = Path.Combine(modelDir, "onnx_gen_config.json");
		if (!File.Exists(path)) path = Path.Combine(modelDir, "generation_config.json");
		if (!File.Exists(path)) return;
		try {
			using var doc = JsonDocument.Parse(File.ReadAllText(path));
			var r = doc.RootElement;
			if (r.TryGetProperty("decoder_start_token_id", out var a)) startId = a.GetInt32();
			if (r.TryGetProperty("eos_token_id", out var b)) eosId = b.GetInt32();
			if (r.TryGetProperty("pad_token_id", out var c)) padId = c.GetInt32();
			if (r.TryGetProperty("forced_eos_token_id", out var d)) forcedEos = d.GetInt32();
			if (r.TryGetProperty("max_length", out var m)) cfgMax = m.GetInt32();
		}
		catch { }
	}

	public string Translate(string text, int maxNewTokens = 128, int numBeams = 4, float lengthPenalty = 1f) {
		lock (gate) {
			if (enc == null || dec == null || tok == null)
				throw new InvalidOperationException("模型未加载");
			text = (text ?? "").Trim();
			if (text.Length == 0) return "";
			var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			var outs = new string[lines.Length];
			for (var i = 0; i < lines.Length; i++) {
				if (string.IsNullOrWhiteSpace(lines[i])) {
					outs[i] = "";
					continue;
				}
				outs[i] = one(lines[i], maxNewTokens, numBeams, lengthPenalty);
			}
			return string.Join("\n", outs);
		}
	}

	string one(string text, int maxNewTokens, int numBeams, float lengthPenalty) {
		var ids = tok.Encode(text, maxLen: 512);
		var inputIds = new DenseTensor<long>(new[] { 1, ids.Length });
		var attn = new DenseTensor<long>(new[] { 1, ids.Length });
		for (var i = 0; i < ids.Length; i++) {
			inputIds[0, i] = ids[i];
			attn[0, i] = 1;
		}

		float[] hidden;
		int hiddenDim;
		int srcLen;
		var encInputs = new[] {
			NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
			NamedOnnxValue.CreateFromTensor("attention_mask", attn),
		};
		using (var results = enc.Run(encInputs)) {
			var t = results.First().AsTensor<float>();
			// [1, src, dim]
			srcLen = t.Dimensions[1];
			hiddenDim = t.Dimensions[2];
			hidden = t.ToArray();
		}

		var maxLen = Math.Min(cfgMax, Math.Max(1, maxNewTokens) + 1);
		numBeams = Math.Max(1, numBeams);
		List<int> outIds;
		if (numBeams == 1)
			outIds = greedy(hidden, srcLen, hiddenDim, attn, maxLen);
		else
			outIds = beamsearch(hidden, srcLen, hiddenDim, attn, maxLen, numBeams, lengthPenalty);

		// 去掉 start / eos / pad
		if (outIds.Count > 0 && outIds[0] == startId)
			outIds.RemoveAt(0);
		while (outIds.Count > 0) {
			var last = outIds[outIds.Count - 1];
			if (last == eosId || last == forcedEos || last == padId || last == startId)
				outIds.RemoveAt(outIds.Count - 1);
			else break;
		}
		return tok.Decode(outIds);
	}

	List<int> greedy(float[] hidden, int srcLen, int hiddenDim, DenseTensor<long> attn, int maxLen) {
		var decIds = new List<int> { startId };
		for (var step = 0; step < maxLen - 1; step++) {
			var logits = decoderstep(decIds, hidden, srcLen, hiddenDim, attn);
			if (padId >= 0 && padId < logits.Length && step > 0)
				logits[padId] = -1e9f;
			var next = argmax(logits);
			decIds.Add(next);
			if (next == eosId || next == forcedEos || next == padId) break;
			if (step >= 3) {
				var a = decIds[decIds.Count - 1];
				var b = decIds[decIds.Count - 2];
				var c = decIds[decIds.Count - 3];
				var d = decIds[decIds.Count - 4];
				if (a == b && b == c && c == d) break;
			}
		}
		return decIds;
	}

	List<int> beamsearch(
		float[] hidden, int srcLen, int hiddenDim, DenseTensor<long> attn,
		int maxLen, int numBeams, float lengthPenalty) {
		// open: (cumLogProb, tokens)
		var open = new List<(double score, List<int> toks)> { (0.0, new List<int> { startId }) };
		var finished = new List<(double score, List<int> toks)>();

		for (var step = 0; step < maxLen - 1; step++) {
			if (open.Count == 0) break;
			// 按长度分组 batch
			var byLen = new Dictionary<int, List<(double score, List<int> toks)>>();
			foreach (var b in open) {
				if (!byLen.TryGetValue(b.toks.Count, out var g)) {
					g = new List<(double, List<int>)>();
					byLen[b.toks.Count] = g;
				}
				g.Add(b);
			}

			var allCand = new List<(double score, List<int> toks, bool ended)>();
			foreach (var kv in byLen) {
				var group = kv.Value;
				var bsz = group.Count;
				var tlen = kv.Key;
				var batch = new DenseTensor<long>(new[] { bsz, tlen });
				for (var bi = 0; bi < bsz; bi++)
					for (var t = 0; t < tlen; t++)
						batch[bi, t] = group[bi].toks[t];

				// encoder 侧 tile
				var h = new DenseTensor<float>(new[] { bsz, srcLen, hiddenDim });
				var am = new DenseTensor<long>(new[] { bsz, srcLen });
				for (var bi = 0; bi < bsz; bi++) {
					for (var s = 0; s < srcLen; s++) {
						am[bi, s] = attn[0, s];
						for (var d = 0; d < hiddenDim; d++)
							h[bi, s, d] = hidden[s * hiddenDim + d];
					}
				}

				float[] lastLogits; // [bsz, vocab]
				int vocab;
				var inputs = new[] {
					NamedOnnxValue.CreateFromTensor("decoder_input_ids", batch),
					NamedOnnxValue.CreateFromTensor("encoder_hidden_states", h),
					NamedOnnxValue.CreateFromTensor("encoder_attention_mask", am),
				};
				using (var results = dec.Run(inputs)) {
					var t = results.First().AsTensor<float>();
					// [B, T, V]
					vocab = t.Dimensions[2];
					lastLogits = new float[bsz * vocab];
					for (var bi = 0; bi < bsz; bi++)
						for (var v = 0; v < vocab; v++)
							lastLogits[bi * vocab + v] = t[bi, tlen - 1, v];
				}

				for (var bi = 0; bi < bsz; bi++) {
					var row = new float[vocab];
					Array.Copy(lastLogits, bi * vocab, row, 0, vocab);
					if (padId >= 0 && padId < vocab && step > 0)
						row[padId] = -1e30f;
					var logp = logsoftmax(row);
					var top = topk(logp, numBeams);
					foreach (var (tid, lp) in top) {
						var ns = group[bi].score + lp;
						var ntoks = new List<int>(group[bi].toks) { tid };
						var ended = tid == eosId || tid == forcedEos || tid == padId;
						allCand.Add((ns, ntoks, ended));
					}
				}
			}

			var newOpen = new List<(double score, List<int> toks)>();
			foreach (var c in allCand) {
				if (c.ended)
					finished.Add((lengthscore(c.score, c.toks.Count, lengthPenalty), c.toks));
				else
					newOpen.Add((c.score, c.toks));
			}
			newOpen.Sort((a, b) =>
				lengthscore(b.score, b.toks.Count, lengthPenalty)
					.CompareTo(lengthscore(a.score, a.toks.Count, lengthPenalty)));
			open = newOpen.Count > numBeams ? newOpen.GetRange(0, numBeams) : newOpen;

			if (finished.Count >= numBeams && open.Count > 0) {
				finished.Sort((a, b) => b.score.CompareTo(a.score));
				var bestOpen = lengthscore(open[0].score, open[0].toks.Count, lengthPenalty);
				if (finished[numBeams - 1].score >= bestOpen)
					break;
			}
		}

		foreach (var b in open)
			finished.Add((lengthscore(b.score, b.toks.Count, lengthPenalty), b.toks));
		if (finished.Count == 0)
			return new List<int> { startId };
		finished.Sort((a, b) => b.score.CompareTo(a.score));
		return finished[0].toks;
	}

	float[] decoderstep(List<int> decIds, float[] hidden, int srcLen, int hiddenDim, DenseTensor<long> attn) {
		var tlen = decIds.Count;
		var batch = new DenseTensor<long>(new[] { 1, tlen });
		for (var t = 0; t < tlen; t++) batch[0, t] = decIds[t];
		var h = new DenseTensor<float>(hidden, new[] { 1, srcLen, hiddenDim });
		var inputs = new[] {
			NamedOnnxValue.CreateFromTensor("decoder_input_ids", batch),
			NamedOnnxValue.CreateFromTensor("encoder_hidden_states", h),
			NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attn),
		};
		using var results = dec.Run(inputs);
		var tens = results.First().AsTensor<float>();
		var vocab = tens.Dimensions[2];
		var row = new float[vocab];
		for (var v = 0; v < vocab; v++)
			row[v] = tens[0, tlen - 1, v];
		return row;
	}

	static double[] logsoftmax(float[] x) {
		var n = x.Length;
		var r = new double[n];
		double max = x[0];
		for (var i = 1; i < n; i++) if (x[i] > max) max = x[i];
		double sum = 0;
		for (var i = 0; i < n; i++) {
			r[i] = Math.Exp(x[i] - max);
			sum += r[i];
		}
		var inv = 1.0 / (sum + 1e-12);
		for (var i = 0; i < n; i++)
			r[i] = Math.Log(r[i] * inv + 1e-12);
		return r;
	}

	static List<(int id, double lp)> topk(double[] logp, int k) {
		k = Math.Min(k, logp.Length);
		// 部分排序
		var idx = new int[logp.Length];
		for (var i = 0; i < idx.Length; i++) idx[i] = i;
		Array.Sort(idx, (a, b) => logp[b].CompareTo(logp[a]));
		var list = new List<(int, double)>(k);
		for (var i = 0; i < k; i++)
			list.Add((idx[i], logp[idx[i]]));
		return list;
	}

	static int argmax(float[] x) {
		var bi = 0;
		var bv = x[0];
		for (var i = 1; i < x.Length; i++) {
			if (x[i] > bv) { bv = x[i]; bi = i; }
		}
		return bi;
	}

	static double lengthscore(double cum, int length, float lengthPenalty) {
		length = Math.Max(1, length);
		if (lengthPenalty == 0) return cum;
		var penalty = Math.Pow((5.0 + length) / 6.0, lengthPenalty);
		return cum / penalty;
	}

	static (InferenceSession enc, InferenceSession dec, string label) createsessions(
		string encPath, string decPath, TtsComputeMode mode) {
		// 明确 CPU
		if (mode == TtsComputeMode.Cpu)
			return makecpu(encPath, decPath);

		if (mode == TtsComputeMode.Igpu) {
			if (!CudaBootstrap.IsDmlReady) {
				CaptureLog.Info("Translate: 核显未安装 → CPU");
				return makecpu(encPath, decPath);
			}
			try {
				return tryep(encPath, decPath, "dml", "dml");
			}
			catch (Exception ex) {
				CaptureLog.Info("Translate DML fail → CPU: " + ex.Message);
				return makecpu(encPath, decPath);
			}
		}

		if (mode == TtsComputeMode.Gpu) {
			if (!CudaBootstrap.IsGpuReady) {
				CaptureLog.Info("Translate: GPU 未安装 → CPU");
				return makecpu(encPath, decPath);
			}
			try {
				return tryep(encPath, decPath, "cuda", "cuda");
			}
			catch (Exception ex) {
				CaptureLog.Info("Translate CUDA fail → CPU: " + ex.Message);
				return makecpu(encPath, decPath);
			}
		}

		// Auto：CUDA → DML → CPU
		if (CudaBootstrap.IsGpuReady) {
			try { return tryep(encPath, decPath, "cuda", "cuda"); }
			catch (Exception ex) {
				CaptureLog.Info("Translate Auto CUDA fail: " + ex.Message);
				try { CudaBootstrap.MarkGpuFailed(ex.Message); } catch { }
			}
		}
		if (CudaBootstrap.IsDmlReady) {
			try { return tryep(encPath, decPath, "dml", "dml"); }
			catch (Exception ex) {
				CaptureLog.Info("Translate Auto DML fail: " + ex.Message);
			}
		}
		return makecpu(encPath, decPath);
	}

	static (InferenceSession enc, InferenceSession dec, string label) tryep(
		string encPath, string decPath, string ep, string label) {
		InferenceSession e = null, d = null;
		try {
			if (ep == "cuda") {
				try { CudaBootstrap.EnsureGpuLibsLoaded(); } catch { }
				if (!CudaBootstrap.IsGpuReady)
					throw new InvalidOperationException("CUDA 不可用");
				CudaBootstrap.EnsureOrtForDevice(OcrDevice.Gpu);
			}
			else if (ep == "dml") {
				if (!CudaBootstrap.IsDmlReady)
					throw new InvalidOperationException("DirectML 不可用");
				CudaBootstrap.EnsureOrtForDevice(OcrDevice.IntelGpu);
			}
			e = makesession(encPath, ep);
			d = makesession(decPath, ep);
			return (e, d, label);
		}
		catch {
			try { e?.Dispose(); } catch { }
			try { d?.Dispose(); } catch { }
			throw;
		}
	}

	static (InferenceSession enc, InferenceSession dec, string label) makecpu(string encPath, string decPath) {
		try {
			CudaBootstrap.EnsureOrtForDevice(OcrDevice.Cpu);
		}
		catch (Exception ex) {
			throw new InvalidOperationException(
				"无法加载 ONNX Runtime（翻译 CPU）。请检查 onnxcpu64 / onnxgpu64 / onnxdml64。" +
				" 详情: " + ex.Message, ex);
		}
		if (!CudaBootstrap.IsOrtReady)
			throw new InvalidOperationException("ONNX Runtime 未就绪（翻译）。");
		return (makesession(encPath, "cpu"), makesession(decPath, "cpu"), "cpu");
	}

	static InferenceSession makesession(string modelPath, string ep) {
		var so = new SessionOptions();
		so.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
		so.EnableMemoryPattern = true;
		so.EnableCpuMemArena = true;
		var threads = Math.Max(1, Environment.ProcessorCount);
		so.IntraOpNumThreads = threads;
		so.InterOpNumThreads = 1;
		if (ep == "cuda") {
			try { so.AppendExecutionProvider_CUDA(0); }
			catch (Exception ex) {
				so.Dispose();
				throw new InvalidOperationException($"Append CUDA EP 失败: {ex.Message}", ex);
			}
		}
		else if (ep == "dml") {
			try { so.AppendExecutionProvider_DML(0); }
			catch (Exception ex) {
				so.Dispose();
				throw new InvalidOperationException($"Append DirectML EP 失败: {ex.Message}", ex);
			}
		}
		try {
			return new InferenceSession(modelPath, so);
		}
		catch {
			try { so.Dispose(); } catch { }
			throw;
		}
	}

	void DisposeSessions() {
		try { enc?.Dispose(); } catch { }
		try { dec?.Dispose(); } catch { }
		enc = null;
		dec = null;
		tok = null;
	}

	public void Dispose() {
		lock (gate) {
			if (disposed) return;
			disposed = true;
			DisposeSessions();
		}
	}
}
