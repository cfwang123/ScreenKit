using System.Collections.Concurrent;
using SherpaOnnx;

namespace WpfOCR;

/// <summary>
/// 全局语音输入：优先流式 OnlineRecognizer（边说边出）；无流式模型时回退离线切句。
/// 按一次热键开始，再按一次结束（toggle）。
/// </summary>
sealed class AsrVoiceInput : IDisposable {
	// —— 离线切句参数 ——
	const int FrameMs = 30;
	const float SpeechRms = 0.015f;
	const int SilenceEndFrames = 18;
	const int MinSpeechFrames = 10;
	const int MaxSpeechFrames = 160;
	const int PreRollFrames = 5;

	readonly ConcurrentQueue<float[]> q = new();
	readonly object uttGate = new();
	readonly List<float> utt = new();
	readonly ConcurrentQueue<float[]> recQ = new();

	AsrMicRecorder mic;
	CancellationTokenSource cts;
	Task loopTask;
	Task recTask;
	bool disposed;
	bool speech;
	int silenceFrames;
	int speechFrames;
	readonly List<float> preRoll = new();

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

	/// <summary>
	/// 流式模式：在后台加载/返回引擎（已 LoadModel）。
	/// 返回 null 则回退离线切句。
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
		while (q.TryDequeue(out _)) { }
		while (recQ.TryDequeue(out _)) { }
		lock (uttGate) {
			utt.Clear();
			preRoll.Clear();
		}
		speech = false;
		silenceFrames = 0;
		speechFrames = 0;

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
			try { ErrorOccurred?.Invoke("流式引擎不可用，改用离线切句: " + ex.Message); } catch { }
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
				? "流式听写中… 边说边出，再按热键结束"
				: "听写中… 再按热键结束");
		}
		catch { }

		if (streamMode)
			loopTask = Task.Run(() => runstream(ct), ct);
		else {
			loopTask = Task.Run(() => runvad(ct), ct);
			recTask = Task.Run(() => runrec(ct), ct);
		}
	}

	public void Stop() {
		if (!IsActive) return;
		IsActive = false;
		try { cts?.Cancel(); } catch { }

		try {
			if (mic != null) {
				mic.SamplesAvailable -= onsamples;
				mic.Stop();
				mic.Dispose();
			}
		}
		catch { }
		mic = null;

		if (streamMode) {
			try { loopTask?.Wait(1500); } catch { }
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
			float[] tail = null;
			lock (uttGate) {
				if (utt.Count >= SampleRate / 10)
					tail = utt.ToArray();
				utt.Clear();
				preRoll.Clear();
			}
			speech = false;
			try { loopTask?.Wait(800); } catch { }
			if (tail != null && tail.Length > 0)
				recQ.Enqueue(tail);

			var t0 = Environment.TickCount;
			while (!recQ.IsEmpty && Environment.TickCount - t0 < 15000) {
				if (recTask == null || recTask.IsCompleted) {
					while (recQ.TryDequeue(out var chunk))
						dorec(chunk);
					break;
				}
				Thread.Sleep(30);
			}
			try { recTask?.Wait(2000); } catch { }
		}

		try { cts?.Dispose(); } catch { }
		cts = null;
		loopTask = null;
		recTask = null;
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
					// toast 显示后处理后文本
					var show = AsrTextNorm.Postprocess(partial);
					try { PartialText?.Invoke(show); } catch { }
					try { StatusChanged?.Invoke("… " + trimshow(show)); } catch { }
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
		// endpoint：补句号、同步窗口，再清空本句状态（下句重新计）
		injectdelta(text ?? "", sentenceEnd: true);
		lastPartial = "";
		lastInjected = "";
		if (IsActive)
			try { StatusChanged?.Invoke("流式听写中…"); } catch { }
	}

	static string trimshow(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		return s.Length > 28 ? s.Substring(s.Length - 28) : s;
	}

	// ───────── 离线切句 ─────────

	void runvad(CancellationToken ct) {
		var frameN = Math.Max(1, SampleRate * FrameMs / 1000);
		var carry = new List<float>(frameN * 2);
		try {
			while (!ct.IsCancellationRequested) {
				if (!q.TryDequeue(out var chunk)) {
					Thread.Sleep(10);
					continue;
				}
				carry.AddRange(chunk);
				while (carry.Count >= frameN && !ct.IsCancellationRequested) {
					var frame = new float[frameN];
					carry.CopyTo(0, frame, 0, frameN);
					carry.RemoveRange(0, frameN);
					onframe(frame);
				}
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke(ex.Message); } catch { }
		}
	}

	void onframe(float[] frame) {
		var rms = rmsOf(frame);
		var isSpeech = rms >= SpeechRms;

		if (!speech) {
			preRoll.AddRange(frame);
			var maxPre = PreRollFrames * frame.Length;
			if (preRoll.Count > maxPre)
				preRoll.RemoveRange(0, preRoll.Count - maxPre);

			if (isSpeech) {
				speech = true;
				speechFrames = 1;
				silenceFrames = 0;
				lock (uttGate) {
					utt.Clear();
					utt.AddRange(preRoll);
					preRoll.Clear();
				}
			}
			return;
		}

		lock (uttGate) utt.AddRange(frame);
		speechFrames++;
		if (isSpeech) silenceFrames = 0;
		else silenceFrames++;

		var endBySilence = silenceFrames >= SilenceEndFrames && speechFrames >= MinSpeechFrames;
		var endByMax = speechFrames >= MaxSpeechFrames;
		if (!endBySilence && !endByMax) return;

		float[] seg = null;
		lock (uttGate) {
			if (utt.Count >= SampleRate / 10)
				seg = utt.ToArray();
			utt.Clear();
		}
		speech = false;
		speechFrames = 0;
		silenceFrames = 0;
		preRoll.Clear();
		if (seg != null)
			recQ.Enqueue(seg);
	}

	void runrec(CancellationToken ct) {
		try {
			while (!ct.IsCancellationRequested || !recQ.IsEmpty) {
				if (!recQ.TryDequeue(out var samples)) {
					if (ct.IsCancellationRequested) break;
					Thread.Sleep(20);
					continue;
				}
				dorec(samples);
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
		text = AsrTextNorm.EnsureSentenceEnd(text);
		if (text.Length == 0) {
			if (IsActive)
				try { StatusChanged?.Invoke("听写中…"); } catch { }
			return;
		}
		try {
			if (!TextInjector.TypeText(text))
				try { ErrorOccurred?.Invoke("无法注入到焦点窗口"); } catch { }
			else
				try { TextInjected?.Invoke(text); } catch { }
		}
		catch (Exception ex) {
			try { ErrorOccurred?.Invoke("注入失败: " + ex.Message); } catch { }
		}
		if (IsActive)
			try { StatusChanged?.Invoke("听写中…"); } catch { }
	}

	static float rmsOf(float[] frame) {
		if (frame == null || frame.Length == 0) return 0;
		double s = 0;
		for (int i = 0; i < frame.Length; i++) {
			var v = frame[i];
			s += v * v;
		}
		return (float)Math.Sqrt(s / frame.Length);
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { Stop(); } catch { }
	}
}
