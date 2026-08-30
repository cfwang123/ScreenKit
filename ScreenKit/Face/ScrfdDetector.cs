using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ScreenKit;

interface IFaceDetector : IDisposable {
	float ScoreThreshold { get; set; }
	float NmsThreshold { get; set; }
	FaceBox[] Detect(Mat bgrImage);
}

/// <summary>SCRFD：InsightFace scrfd_*_kps.onnx，输入 640×640，输出框 + 5 点。</summary>
sealed class ScrfdDetector : IFaceDetector {
	const int InputSize = 640;
	const float InputMean = 127.5f;
	const float InputStd = 128.0f;
	const int NumAnchors = 2;
	static readonly int[] FeatureStrides = { 8, 16, 32 };

	readonly InferenceSession session;
	readonly string inputName;
	readonly Dictionary<(int, int, int), float[]> centerCache = new();

	public float ScoreThreshold { get; set; } = 0.5f;
	public float NmsThreshold { get; set; } = 0.4f;

	public ScrfdDetector(string modelPath, TtsComputeMode mode = TtsComputeMode.Cpu) {
		session = FaceOnnx.Open(modelPath, mode, out _);
		inputName = FaceOnnx.InputName(session);
	}

	public FaceBox[] Detect(Mat bgrImage) {
		if (bgrImage == null) throw new ArgumentNullException(nameof(bgrImage));
		if (bgrImage.Channels() != 3)
			throw new ArgumentException("输入图像必须为 3 通道 BGR");
		if (bgrImage.Empty())
			throw new ArgumentException("输入图像为空");

		int origH = bgrImage.Rows, origW = bgrImage.Cols;
		float scale = Math.Min((float)InputSize / origW, (float)InputSize / origH);
		int newW = (int)Math.Round(origW * scale);
		int newH = (int)Math.Round(origH * scale);

		var resized = new Mat();
		Cv2.Resize(bgrImage, resized, new OpenCvSharp.Size(newW, newH));
		var padded = new Mat(new OpenCvSharp.Size(InputSize, InputSize), MatType.CV_8UC3, Scalar.All(0));
		var roi = new Mat(padded, new OpenCvSharp.Rect(0, 0, newW, newH));
		resized.CopyTo(roi);
		resized.Dispose();
		roi.Dispose();

		float[] tensor;
		try { tensor = buildinput(padded); }
		finally { padded.Dispose(); }

		var outputs = run(tensor);
		return post(outputs, origW, origH, scale);
	}

	float[] buildinput(Mat padded) {
		int h = padded.Rows, w = padded.Cols;
		var floatMat = new Mat();
		padded.ConvertTo(floatMat, MatType.CV_32FC3, 1.0 / InputStd, -InputMean / InputStd);
		var rgb = new Mat();
		Cv2.CvtColor(floatMat, rgb, ColorConversionCodes.BGR2RGB);
		floatMat.Dispose();
		return FaceOnnxUtil.Chw(rgb, h, w);
	}

	float[][] run(float[] inputTensor) {
		var inputDense = new DenseTensor<float>(inputTensor, new[] { 1, 3, InputSize, InputSize });
		var inputs = new List<NamedOnnxValue> {
			NamedOnnxValue.CreateFromTensor(inputName, inputDense)
		};
		using var results = session.Run(inputs);
		var outputs = new float[results.Count][];
		int idx = 0;
		foreach (var r in results) {
			var dense = r.AsTensor<float>() as DenseTensor<float>
				?? throw new InvalidOperationException("模型输出不是 DenseTensor<float>");
			outputs[idx++] = dense.Buffer.ToArray();
		}
		return outputs;
	}

	FaceBox[] post(float[][] outputs, int origW, int origH, float scale) {
		int fmc = FeatureStrides.Length;
		bool hasKps = outputs.Length >= fmc * 3;
		var candidates = new List<FaceBox>();

		for (int idx = 0; idx < fmc; idx++) {
			int stride = FeatureStrides[idx];
			float[] scores = outputs[idx];
			float[] bboxPreds = outputs[idx + fmc];
			float[] kpsPreds = hasKps ? outputs[idx + fmc * 2] : null;

			int height = InputSize / stride;
			int width = InputSize / stride;
			float[] anchorCenters = getcenters(height, width, stride);
			int n = anchorCenters.Length / 2;
			int scoreStride = scores.Length / n;
			if (scoreStride < 1) scoreStride = 1;

			for (int i = 0; i < n; i++) {
				float score = scores[i * scoreStride];
				if (score < ScoreThreshold) continue;

				float cx = anchorCenters[i * 2 + 0];
				float cy = anchorCenters[i * 2 + 1];
				float left = bboxPreds[i * 4 + 0] * stride;
				float top = bboxPreds[i * 4 + 1] * stride;
				float right = bboxPreds[i * 4 + 2] * stride;
				float bottom = bboxPreds[i * 4 + 3] * stride;

				float x1 = FaceOnnxUtil.Clamp((cx - left) / scale, 0, origW);
				float y1 = FaceOnnxUtil.Clamp((cy - top) / scale, 0, origH);
				float x2 = FaceOnnxUtil.Clamp((cx + right) / scale, 0, origW);
				float y2 = FaceOnnxUtil.Clamp((cy + bottom) / scale, 0, origH);

				var box = new FaceBox { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Score = score };
				if (hasKps) {
					var kps = new float[10];
					for (int k = 0; k < 5; k++) {
						float dx = kpsPreds[i * 10 + k * 2 + 0] * stride;
						float dy = kpsPreds[i * 10 + k * 2 + 1] * stride;
						kps[k * 2 + 0] = (cx + dx) / scale;
						kps[k * 2 + 1] = (cy + dy) / scale;
					}
					box.Landmarks = kps;
				}
				candidates.Add(box);
			}
		}
		return FaceOnnxUtil.Nms(candidates, NmsThreshold);
	}

	float[] getcenters(int height, int width, int stride) {
		var key = (height, width, stride);
		if (centerCache.TryGetValue(key, out var cached))
			return cached;
		int total = height * width * NumAnchors;
		var centers = new float[total * 2];
		int idx = 0;
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				float cx = x * stride;
				float cy = y * stride;
				for (int a = 0; a < NumAnchors; a++) {
					centers[idx++] = cx;
					centers[idx++] = cy;
				}
			}
		}
		centerCache[key] = centers;
		return centers;
	}

	public void Dispose() => session?.Dispose();
}

static class FaceOnnxUtil {
	public static float[] Chw(Mat rgb, int h, int w) {
		var planes = Cv2.Split(rgb);
		rgb.Dispose();
		try {
			if (planes.Length != 3)
				throw new InvalidOperationException("通道拆分失败");
			var tensor = new float[3 * h * w];
			for (int c = 0; c < 3; c++) {
				var plane = planes[c];
				if (!plane.IsContinuous())
					plane = plane.Clone();
				Marshal.Copy(plane.Data, tensor, c * h * w, h * w);
				if (!ReferenceEquals(plane, planes[c]))
					plane.Dispose();
			}
			return tensor;
		}
		finally {
			foreach (var p in planes) p.Dispose();
		}
	}

	public static float Clamp(float v, float min, float max) {
		if (v < min) return min;
		if (v > max) return max;
		return v;
	}

	public static FaceBox[] Nms(List<FaceBox> boxes, float nmsThresh) {
		if (boxes.Count == 0) return [];
		boxes.Sort((a, b) => b.Score.CompareTo(a.Score));
		var keep = new List<FaceBox>();
		var removed = new bool[boxes.Count];
		for (int i = 0; i < boxes.Count; i++) {
			if (removed[i]) continue;
			keep.Add(boxes[i]);
			for (int j = i + 1; j < boxes.Count; j++) {
				if (removed[j]) continue;
				if (iou(boxes[i], boxes[j]) > nmsThresh)
					removed[j] = true;
			}
		}
		return keep.ToArray();
	}

	static float iou(FaceBox a, FaceBox b) {
		float xx1 = Math.Max(a.X1, b.X1);
		float yy1 = Math.Max(a.Y1, b.Y1);
		float xx2 = Math.Min(a.X2, b.X2);
		float yy2 = Math.Min(a.Y2, b.Y2);
		float w = Math.Max(0, xx2 - xx1);
		float h = Math.Max(0, yy2 - yy1);
		float inter = w * h;
		float union = a.Area + b.Area - inter;
		return union <= 0 ? 0 : inter / union;
	}
}
