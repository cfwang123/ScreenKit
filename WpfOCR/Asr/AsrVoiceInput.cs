using System.Collections.Concurrent;
using SherpaOnnx;

namespace WpfOCR;

/// <summary>
/// 全局语音输入：流式或离线。自动分句时成句即润色并输出；否则流式边说边出、离线整段停止后一次输出。
/// 按一次热键开始，再按一次结束（toggle）。
/// </summary>
sealed class AsrVoiceInput : IDisposable {
	readonly ConcurrentQueue<float[]> q = new();
	readonly object uttGate = new();
	readonly List<float> utt = new();
	readonly object histGate = new();
	readonly List<string> hist = new();

	AsrMicRecorder mic;
	CancellationTokenSource cts;
	Task loopTask;
	bool disposed;

	// 流式
	bool streamMode;
	AsrStreamEngine streamEng;
	OnlineStream stream;
	string lastPartial = "";
	/// <summary>已注入焦点窗口的前缀（流式增量出字）。</summary>
	string lastInjected = "";
	readonly object streamGate = new();

	/// <summary>离线模式：识别整段波形（后台线程，调用方加锁）。</summary>
	public Func<float[], int, string> Recognize { get; set; }

	/// <summary>识别完成后润色（可选；参数为原文、本轮已输出上文；失败应回原文）。</summary>
	public Func<string, string, string> Polish { get; set; }

	/// <summary>成句时句末补「，」，并立刻润色后输出（流式 endpoint / 离线静音切句）。</summary>
	public bool SplitSentences { get; set; } = true;

	/// <summary>自动分句间隔（秒）：静音达到此时长才切一句，连续说话不切。</summary>
	public int SplitIntervalSec { get; set; } = 5;

	int splitsec() => Compat.Clamp(SplitIntervalSec, 1, 30);

	string gethist() {
		lock (histGate)
			return hist.Count == 0 ? "" : string.Join("\n", hist);
	}

	void addhist(string s) {
		s = (s ?? "").Trim();
		if (s.Length == 0) return;
		lock (histGate) {
			hist.Add(s);
			var n = 0;
			foreach (var x in hist) n += x.Length;
			while (hist.Count > 1 && n > 2000) {
				n -= hist[0].Length;
				hist.RemoveAt(0);
			}
		}
	}

	string dopolish(string text) {
		try {
			var polished = Polish(text, gethist());
			if (!string.IsNullOrWhiteSpace(polished))
				return AsrTextNorm.Postprocess(polished.Trim());
		}
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke("润色失败，使用原文: " + ex.Message); } catch { }
		}
		return text;
	}

	/// <summary>
	/// 流式模式：在后台加载/返回引擎（已 LoadModel）。
	/// 返回 null 则回退离线整段识别。
	/// </summary>
	public Func<AsrStreamEngine> ResolveStreamEngine { get; set; }

	public int SampleRate { get; private set; } = 16000;
	public bool IsActive { get; private set; }
	public bool IsStreamingMode => streamMode;

	public event Action<bool> ActiveChanged;
	public event Action<string> StatusChanged;
	public event Action<string> ErrorOccurred;
	public event Action<string> TextInjected;
	public event Action<string> PartialText;
	/// <summary>一句已注入焦点窗口（润色后或无需润色）。</summary>
	public event Action TextCommitted;

	public void Toggle() {
		if (IsActive) Stop();
		else Start();
	}

	public void Start() {
		if (disposed) throw new ObjectDisposedException(nameof(AsrVoiceInput));
		if (IsActive) return;

		streamMode = false;
		streamEng = null;
		stream = null;
		lastPartial = "";
		lastInjected = "";
		lock (histGate) hist.Clear();
		while (q.TryDequeue(out _)) { }
		lock (uttGate) utt.Clear();

		// 尝试流式
		try {
			if (ResolveStreamEngine != null) {
				streamEng = ResolveStreamEngine();
				if (streamEng != null && streamEng.IsLoaded) {
					stream = streamEng.CreateStream();
					streamMode = true;
					SampleRate = streamEng.FeatSampleRate > 0 ? streamEng.FeatSampleRate : 16000;
				}
			}
		}
		catch (Exception ex) {
			streamMode = false;
			streamEng = null;
			stream = null;
			try { ErrorOccurred?.Invoke("流式引擎不可用，改用离线整段识别: " + ex.Message); } catch { }
		}

		if (!streamMode && Recognize == null)
			throw new InvalidOperationException("未设置识别回调，且无流式模型");

		cts = new CancellationTokenSource();
		var ct = cts.Token;
		if (!streamMode) SampleRate = 16000;

		mic = new AsrMicRecorder(SampleRate);
		mic.SamplesAvailable += onsamples;
		mic.Start(streamOnly: true);

		IsActive = true;
		try { ActiveChanged?.Invoke(true); } catch { }
		try {
			StatusChanged?.Invoke(streamMode
				? (SplitSentences ? "流式听写中… 成句即输出，再按热键结束" : "流式听写中… 边说边出，再按热键结束")
				: (SplitSentences ? "离线听写中… 成句即输出，再按热键结束" : "离线听写中… 再按热键结束并输出"));
		}
		catch { }

		if (streamMode)
			loopTask = Task.Run(() => runstream(ct), ct);
		else if (SplitSentences)
			loopTask = Task.Run(() => runofflinesplit(ct), ct);
		else
			loopTask = Task.Run(() => runcollect(ct), ct);
	}

	public void Stop() {
		if (!IsActive) return;
		try {
			if (mic != null) {
				mic.Stop();
				mic.SamplesAvailable -= onsamples;
				mic.Dispose();
			}
		}
		catch { }
		mic = null;
		try { cts?.Cancel(); } catch { }
		IsActive = false;
		var waitMs = SplitSentences ? 20000 : 1500;

		if (streamMode) {
			try { loopTask?.Wait(waitMs); } catch { }
			// 收尾：InputFinished + 最后一句
			try {
				lock (streamGate) {
					if (stream != null && streamEng != null) {
						streamEng.InputFinished(stream);
						var text = streamEng.GetText(stream);
						commitfinal(text);
						try { stream.Dispose(); } catch { }
						stream = null;
					}
				}
			}
			catch (Exception ex) {
				try { ErrorOccurred?.Invoke("流式收尾失败: " + ex.Message); } catch { }
			}
		}
		else {
			try { loopTask?.Wait(waitMs); } catch { }
			while (q.TryDequeue(out var leftover)) {
				lock (uttGate) utt.AddRange(leftover);
			}
			float[] all = null;
			lock (uttGate) {
				if (utt.Count >= SampleRate / 10)
					all = utt.ToArray();
				utt.Clear();
			}
			if (all != null && all.Length > 0)
				dorec(all);
		}

		try { cts?.Dispose(); } catch { }
		cts = null;
		loopTask = null;
		streamMode = false;
		// 不 Dispose 共享 streamEng（由 MainWindow 持有）
		streamEng = null;

		try { StatusChanged?.Invoke("语音输入已结束"); } catch { }
		try { ActiveChanged?.Invoke(false); } catch { }
	}

	void onsamples(float[] chunk) {
		if (!IsActive || chunk == null || chunk.Length == 0) return;
		q.Enqueue(chunk);
	}

	// ───────── 流式 ─────────

	void runstream(CancellationToken ct) {
		try {
			while (!ct.IsCancellationRequested) {
				if (!q.TryDequeue(out var chunk)) {
					Thread.Sleep(10);
					continue;
				}
				// 尽量合并队列，减少 Decode 次数
				var list = new List<float>(chunk);
				while (q.TryDequeue(out var more))
					list.AddRange(more);
				var samples = list.ToArray();

				string partial = null;
				var hitEnd = false;
				string finalText = null;
				lock (streamGate) {
					if (streamEng == null || stream == null) continue;
					streamEng.AcceptAndDecode(stream, samples, SampleRate);
					partial = streamEng.GetText(stream);
					if (streamEng.IsEndpoint(stream)) {
						hitEnd = true;
						finalText = partial;
						streamEng.Reset(stream);
					}
				}
				if (!string.IsNullOrEmpty(partial) && !string.Equals(partial, lastPartial, StringComparison.Ordinal)) {
					lastPartial = partial;
					var show = AsrTextNorm.Postprocess(partial);
					try { PartialText?.Invoke(show); } catch { }
					try { StatusChanged?.Invoke("… " + trimshow(show)); } catch { }
					// 自动分句：等成句后再润色并一次性输出，不把半句打进焦点窗
					if (!SplitSentences)
						injectdelta(partial);
				}
				if (hitEnd)
					commitfinal(finalText);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke(ex.Message); } catch { }
		}
	}

	/// <summary>
	/// 流式同步焦点窗口文本：
	/// - 结果变长且兼容：只补后缀
	/// - 结果缩短：Backspace 删掉多余
	/// - 前文改写（如「2022年」→「2022/01/02」）：删掉分歧部分再补新内容
	/// </summary>
	/// <param name="sentenceEnd">endpoint 断句时补逗号「，」</param>
	void injectdelta(string full, bool sentenceEnd = false) {
		full = AsrTextNorm.Postprocess((full ?? "").Trim());
		if (sentenceEnd)
			full = AsrTextNorm.EnsureSentenceEnd(full);
		try {
			if (string.IsNullOrEmpty(full)) {
				if (!string.IsNullOrEmpty(lastInjected)) {
					if (!TextInjector.Backspace(lastInjected.Length))
						try { ErrorOccurred?.Invoke("无法撤回已注入文字"); } catch { }
					else
						lastInjected = "";
				}
				return;
			}

			if (string.IsNullOrEmpty(lastInjected)) {
				if (TextInjector.TypeText(full)) {
					lastInjected = full;
					try { TextInjected?.Invoke(full); } catch { }
				}
				else
					try { ErrorOccurred?.Invoke("无法注入到焦点窗口"); } catch { }
				return;
			}

			if (string.Equals(full, lastInjected, StringComparison.Ordinal))
				return;

			// 公共前缀：尽量少删，只改分歧段
			var common = commonprefixlen(lastInjected, full);
			var del = lastInjected.Length - common;
			var add = common < full.Length ? full.Substring(common) : "";

			if (del > 0) {
				if (!TextInjector.Backspace(del)) {
					try { ErrorOccurred?.Invoke("无法撤回已注入文字"); } catch { }
					return;
				}
			}
			if (add.Length > 0) {
				if (!TextInjector.TypeText(add)) {
					lastInjected = full.Substring(0, common);
					try { ErrorOccurred?.Invoke("无法注入到焦点窗口"); } catch { }
					return;
				}
				try { TextInjected?.Invoke(add); } catch { }
			}
			lastInjected = full;
		}
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke("注入失败: " + ex.Message); } catch { }
		}
	}

	static int commonprefixlen(string a, string b) {
		if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
		var n = Math.Min(a.Length, b.Length);
		var i = 0;
		while (i < n && a[i] == b[i]) i++;
		return i;
	}

	void commitfinal(string text) {
		var done = AsrTextNorm.Postprocess((text ?? "").Trim());
		if (done.Length == 0) {
			lastPartial = "";
			return;
		}
		// 自动分句：成句立刻润色再输出
		if (SplitSentences && Polish != null)
			done = dopolish(done);
		injectdelta(done, sentenceEnd: SplitSentences);
		lastPartial = "";
		lastInjected = "";
		addhist(done);
		try { TextCommitted?.Invoke(); } catch { }
		if (IsActive)
			try { StatusChanged?.Invoke("流式听写中…"); } catch { }
	}

	static string trimshow(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		return s.Length > 28 ? s.Substring(s.Length - 28) : s;
	}

	// ───────── 离线：自动分句则静音切句，一句一识别/润色/输出 ─────────

	void runofflinesplit(CancellationToken ct) {
		var spoke = false;
		var sil = 0;
		var sec = splitsec();
		var silNeed = Math.Max(SampleRate * sec, 1);
		var minUtt = Math.Max(SampleRate * 4 / 10, 1);
		try {
			while (!ct.IsCancellationRequested) {
				if (!q.TryDequeue(out var chunk)) {
					Thread.Sleep(10);
					continue;
				}
				var list = new List<float>(chunk);
				while (q.TryDequeue(out var more))
					list.AddRange(more);
				var samples = list.ToArray();
				lock (uttGate) utt.AddRange(samples);
				var e = rms(samples);
				if (e >= 0.012f) {
					spoke = true;
					sil = 0;
				}
				else if (spoke) {
					sil += samples.Length;
				}
				float[] wave = null;
				lock (uttGate) {
					if (spoke && sil >= silNeed && utt.Count >= minUtt) {
						wave = utt.ToArray();
						utt.Clear();
						spoke = false;
						sil = 0;
					}
				}
				if (wave != null)
					dorec(wave);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke(ex.Message); } catch { }
		}
	}

	static float rms(float[] samples) {
		if (samples == null || samples.Length == 0) return 0;
		double s = 0;
		foreach (var x in samples)
			s += x * x;
		return (float)Math.Sqrt(s / samples.Length);
	}

	// ───────── 离线整段 ─────────

	void runcollect(CancellationToken ct) {
		try {
			while (!ct.IsCancellationRequested) {
				if (!q.TryDequeue(out var chunk)) {
					Thread.Sleep(10);
					continue;
				}
				lock (uttGate) utt.AddRange(chunk);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke(ex.Message); } catch { }
		}
	}

	void dorec(float[] samples) {
		if (samples == null || samples.Length < SampleRate / 10) return;
		string text = null;
		try {
			try { StatusChanged?.Invoke("识别中…"); } catch { }
			text = Recognize?.Invoke(samples, SampleRate);
		}
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke("识别失败: " + ex.Message); } catch { }
			return;
		}
		text = AsrTextNorm.Postprocess((text ?? "").Trim());
		if (text.Length == 0) return;
		if (Polish != null)
			text = dopolish(text);
		if (SplitSentences)
			text = AsrTextNorm.EnsureSentenceEnd(text);
		try {
			if (!TextInjector.TypeText(text))
				try { ErrorOccurred?.Invoke("无法注入到焦点窗口"); } catch { }
			else {
				try { TextInjected?.Invoke(text); } catch { }
				addhist(text);
				try { TextCommitted?.Invoke(); } catch { }
			}
		}
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke("注入失败: " + ex.Message); } catch { }
		}
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Stop(); } catch { }
	}
}
