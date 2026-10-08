using OpenCvSharp;

namespace ScreenKit;

/// <summary>
/// 全局共享 OCR 引擎：主窗口与 HTTP API 共用同一套 session，避免双倍模型内存。
/// </summary>
sealed class OcrRunner : IDisposable {
	readonly object gate = new();
	OcrEngine eng;
	string engKey = "";
	int lastuse;
	bool disposed;

	public string ModelLabel {
		get { lock (gate) return eng?.ModelLabel; }
	}

	public string DeviceUsed {
		get { lock (gate) return eng?.DeviceUsed; }
	}

	public bool HasEngine {
		get { lock (gate) return eng != null; }
	}

	public bool TryMem(out string name, out string device, out long bytes) {
		lock (gate) {
			name = eng?.ModelLabel ?? "";
			device = eng?.DeviceUsed ?? "";
			bytes = eng?.WeightBytes ?? 0;
			return eng != null;
		}
	}

	/// <summary>立刻释放当前 OCR 会话。调用方在后台线程执行。</summary>
	public void UnloadNow() {
		OcrEngine old;
		lock (gate) {
			old = eng;
			eng = null;
			engKey = "";
			lastuse = 0;
		}
		try { old?.Dispose(); } catch { }
	}

	public OcrResult Run(OcrOptions opt, Mat bgr) {
		Compat.ThrowIfDisposed(disposed, this);
		NativeRuntime.EnsureOpenCv();
		if (WinOcr.Is(opt)) {
			dropengine();
			return WinOcr.Recognize(opt, bgr);
		}
		lock (gate) {
			var loadMs = ensure(opt);
			var r = eng.Run(bgr);
			if (r != null) r.LoadMs = loadMs;
			return r;
		}
	}

	public OcrResult Run(OcrOptions opt, string imagePath) {
		Compat.ThrowIfDisposed(disposed, this);
		NativeRuntime.EnsureOpenCv();
		if (WinOcr.Is(opt)) {
			dropengine();
			if (!File.Exists(imagePath))
				throw new FileNotFoundException("图像不存在", imagePath);
			using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
			if (mat.Empty())
				throw new InvalidOperationException($"无法读取图像: {imagePath}");
			return WinOcr.Recognize(opt, mat);
		}
		lock (gate) {
			var loadMs = ensure(opt);
			var r = eng.Run(imagePath);
			if (r != null) r.LoadMs = loadMs;
			return r;
		}
	}

	/// <summary>
	/// 预热：按当前参数加载/保持引擎。服务模式下启动与改参后调用，保证常驻。
	/// </summary>
	/// <returns>本次新建引擎耗时 ms；已缓存仅为同步 runtime 参数则为 0。</returns>
	public int Warmup(OcrOptions opt) {
		NativeRuntime.EnsureOpenCv();
		Compat.ThrowIfDisposed(disposed, this);
		if (WinOcr.Is(opt)) {
			dropengine();
			return 0;
		}
		lock (gate) return ensure(opt);
	}

	/// <summary>服务模式保活：有会话时刷新空闲计时。</summary>
	public void TouchIdle() {
		lock (gate) {
			if (eng != null) lastuse = Environment.TickCount;
		}
	}

	/// <summary>空闲超过 limitMs 则后台释放。服务模式由调用方跳过。</summary>
	public bool IdleUnload(int limitMs) {
		OcrEngine old;
		lock (gate) {
			if (eng == null || !OnnxIdle.Due(lastuse, limitMs)) return false;
			old = eng;
			eng = null;
			engKey = "";
			lastuse = 0;
		}
		_ = Task.Run(() => { try { old.Dispose(); } catch { } });
		return true;
	}

	/// <summary>参数/模型变更后丢弃缓存，下次识别再加载。服务模式请改用 <see cref="Warmup"/>。</summary>
	public void Invalidate() {
		OcrEngine old;
		lock (gate) {
			old = eng;
			eng = null;
			engKey = "";
		}
		// ORT Dispose 可能很慢，勿阻塞 UI/HTTP 线程
		if (old != null)
			_ = Task.Run(() => { try { old.Dispose(); } catch { } });
	}

	void dropengine() {
		OcrEngine old;
		lock (gate) {
			old = eng;
			eng = null;
			engKey = "";
			lastuse = Environment.TickCount;
		}
		if (old != null)
			_ = Task.Run(() => { try { old.Dispose(); } catch { } });
	}

	/// <returns>本次新建引擎耗时 ms；已缓存则为 0。</returns>
	int ensure(OcrOptions opt) {
		if (opt == null) throw new ArgumentNullException(nameof(opt));
		var k = sessionkey(opt);
		if (eng != null && engKey == k) {
			// 边长/阈值等不重建 session，只改推理参数
			eng.ApplyRuntime(opt);
			lastuse = Environment.TickCount;
			return 0;
		}
		OcrEngine old = eng;
		eng = null;
		var t0 = Environment.TickCount;
		eng = new OcrEngine(opt);
		var loadMs = Math.Max(0, Environment.TickCount - t0);
		engKey = k;
		lastuse = Environment.TickCount;
		if (old != null)
			_ = Task.Run(() => { try { old.Dispose(); } catch { } });
		return loadMs;
	}

	/// <summary>仅影响 ONNX session 的键；边长/阈值等 runtime 参数不触发重建。</summary>
	static string sessionkey(OcrOptions o) =>
		$"{o.ModelPackId}|{o.ModelVariant}|{o.ModelsDir}|{o.Device}";

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		Invalidate(); // 后台释放，不阻塞
	}
}
