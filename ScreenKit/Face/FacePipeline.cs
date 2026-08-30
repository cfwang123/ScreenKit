using System.Diagnostics;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>检测 → 对齐 → 人脸特征。关键点/属性由界面层单独叠加。</summary>
sealed class FacePipeline : IDisposable {
	readonly IFaceDetector detector;
	readonly FaceRecognizer recognizer;

	public string EpLabel { get; }

	public FacePipeline(string detModelPath, string regModelPath, float detThresh, TtsComputeMode mode) {
		IFaceDetector det = null;
		try {
			det = createdetector(detModelPath, detThresh, mode);
			recognizer = new FaceRecognizer(regModelPath, mode);
			detector = det;
			EpLabel = FaceOnnx.EpLabel(FaceOnnx.LastEp);
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

	void extract(Mat image, FaceExtractResult result) {
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
		recognizer?.Dispose();
		detector?.Dispose();
	}
}
