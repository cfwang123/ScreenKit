using System.Diagnostics;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>检测 → 对齐 → 人脸特征。关键点/属性由界面层单独叠加。</summary>
sealed class FacePipeline : IDisposable {
	readonly object gate = new();
	readonly IFaceDetector detector;
	readonly FaceRecognizer recognizer;
	int lastuse;
	int busy;
	bool released;

	public string EpLabel { get; }
	public string MemName { get; }
	public long WeightBytes { get; }
	public bool IsLive { get { lock (gate) return !released; } }

	public FacePipeline(string detModelPath, string regModelPath, float detThresh, TtsComputeMode mode) {
		IFaceDetector det = null;
		try {
			det = createdetector(detModelPath, detThresh, mode);
			recognizer = new FaceRecognizer(regModelPath, mode);
			detector = det;
			EpLabel = FaceOnnx.EpLabel(FaceOnnx.LastEp);
			MemName = Path.GetFileName(detModelPath) + " + " + Path.GetFileName(regModelPath);
			WeightBytes = MemUsage.FileWeight(detModelPath) + MemUsage.FileWeight(regModelPath);
			lastuse = Environment.TickCount;
		}
		catch {
			det?.Dispose();
			recognizer?.Dispose();
			throw;
		}
	}

	static IFaceDetector createdetector(string detModelPath, float detThresh, TtsComputeMode mode) {
		string name = Path.GetFileName(detModelPath).ToLowerInvariant();
		if (name.Contains("yolo"))
			return new YoloFaceDetector(detModelPath, mode) { ScoreThreshold = detThresh };
		return new ScrfdDetector(detModelPath, mode) { ScoreThreshold = detThresh };
	}

	public FaceExtractResult ExtractTimed(string imagePath) {
		var totalSw = Stopwatch.StartNew();
		var result = new FaceExtractResult();
		var loadSw = Stopwatch.StartNew();
		var image = Cv2.ImRead(imagePath, ImreadModes.Color);
		if (image == null || image.Empty()) {
			image?.Dispose();
			throw new IOException($"无法读取图片: {imagePath}");
		}
		loadSw.Stop();
		result.LoadMs = loadSw.Elapsed.TotalMilliseconds;
		try {
			extract(image, result);
		}
		finally { image.Dispose(); }
		totalSw.Stop();
		result.TotalMs = totalSw.Elapsed.TotalMilliseconds;
		return result;
	}

	public FaceExtractResult ExtractTimed(Mat image) {
		var totalSw = Stopwatch.StartNew();
		var result = new FaceExtractResult();
		extract(image, result);
		totalSw.Stop();
		result.TotalMs = totalSw.Elapsed.TotalMilliseconds;
		return result;
	}

	public void TouchIdle() {
		lock (gate) {
			if (!released) lastuse = Environment.TickCount;
		}
	}

	public bool IdleUnload(int limitMs) {
		lock (gate) {
			if (released || busy > 0 || !OnnxIdle.Due(lastuse, limitMs)) return false;
			released = true;
			disposeinner();
			lastuse = 0;
			return true;
		}
	}

	/// <summary>正在推理时返回 false，不释放。</summary>
	public bool TryRelease() {
		lock (gate) {
			if (released) return true;
			if (busy > 0) return false;
			released = true;
			disposeinner();
			lastuse = 0;
			return true;
		}
	}

	void extract(Mat image, FaceExtractResult result) {
		lock (gate) {
			if (released) throw new ObjectDisposedException(nameof(FacePipeline));
			busy++;
		}
		try {
			extractcore(image, result);
		}
		finally {
			lock (gate) {
				if (busy > 0) busy--;
				if (!released) lastuse = Environment.TickCount;
			}
		}
	}

	void extractcore(Mat image, FaceExtractResult result) {
		var detectSw = Stopwatch.StartNew();
		var faces = detector.Detect(image);
		detectSw.Stop();
		result.DetectMs = detectSw.Elapsed.TotalMilliseconds;
		result.FaceCount = faces == null ? 0 : faces.Length;
		if (faces == null || faces.Length == 0) return;

		var best = selectlargest(faces);
		result.Face = best;
		var extractSw = Stopwatch.StartNew();
		result.Feature = recognizer.Extract(image, best);
		extractSw.Stop();
		result.ExtractMs = extractSw.Elapsed.TotalMilliseconds;
	}

	static FaceBox selectlargest(FaceBox[] faces) {
		var best = faces[0];
		float bestArea = best.Area;
		for (int i = 1; i < faces.Length; i++) {
			if (faces[i].Area > bestArea) {
				best = faces[i];
				bestArea = faces[i].Area;
			}
		}
		return best;
	}

	public void Dispose() {
		lock (gate) {
			if (released) return;
			released = true;
			disposeinner();
			lastuse = 0;
		}
	}

	void disposeinner() {
		recognizer?.Dispose();
		detector?.Dispose();
	}
}
