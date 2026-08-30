using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>YOLOv8-face / YOLOv5-face：letterbox 640，输出框 + 5 点。</summary>
sealed class YoloFaceDetector : IFaceDetector {
	const int InputSize = 640;
	const float LetterboxPadValue = 114f;

	readonly InferenceSession session;
	readonly string inputName;

	public float ScoreThreshold { get; set; } = 0.5f;
	public float NmsThreshold { get; set; } = 0.45f;

	public YoloFaceDetector(string modelPath, TtsComputeMode mode = TtsComputeMode.Cpu) {
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
		int padW = (InputSize - newW) / 2;
		int padH = (InputSize - newH) / 2;

		var resized = new Mat();
		Cv2.Resize(bgrImage, resized, new OpenCvSharp.Size(newW, newH));
		var padded = new Mat(new OpenCvSharp.Size(InputSize, InputSize), MatType.CV_8UC3, Scalar.All(LetterboxPadValue));
		var roi = new Mat(padded, new OpenCvSharp.Rect(padW, padH, newW, newH));
		resized.CopyTo(roi);
		resized.Dispose();
		roi.Dispose();

		float[] tensor;
		try { tensor = buildinput(padded); }
		finally { padded.Dispose(); }

		var (output, dims) = run(tensor);
		return post(output, dims, scale, padW, padH, origW, origH);
	}

	float[] buildinput(Mat padded) {
		int h = padded.Rows, w = padded.Cols;
		var rgb = new Mat();
		Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGR2RGB);
		var floatMat = new Mat();
		rgb.ConvertTo(floatMat, MatType.CV_32FC3);
		rgb.Dispose();
		return FaceOnnxUtil.Chw(floatMat, h, w);
	}

	(float[] data, int[] dims) run(float[] inputTensor) {
		var inputDense = new DenseTensor<float>(inputTensor, new[] { 1, 3, InputSize, InputSize });
		var inputs = new List<NamedOnnxValue> {
			NamedOnnxValue.CreateFromTensor(inputName, inputDense)
		};
		using var results = session.Run(inputs);
		foreach (var r in results) {
			var dense = r.AsTensor<float>() as DenseTensor<float>
				?? throw new InvalidOperationException("模型输出不是 DenseTensor<float>");
			var dimList = dense.Dimensions;
			var dims = new int[dimList.Length];
			for (int i = 0; i < dims.Length; i++) dims[i] = dimList[i];
			return (dense.Buffer.ToArray(), dims);
		}
		throw new InvalidOperationException("模型未返回输出");
	}

	FaceBox[] post(float[] output, int[] dims, float scale, int padW, int padH, int origW, int origH) {
		if (dims.Length < 3)
			throw new InvalidOperationException("模型输出维度不符合预期: " + string.Join("x", dims));

		int dim1 = dims[1], dim2 = dims[2];
		bool channelFirst = dim1 < dim2;
		int C = channelFirst ? dim1 : dim2;
		int N = channelFirst ? dim2 : dim1;
		const int ConfIdx = 4;
		int kpsDim = C - 5;
		int kpsStride = kpsDim == 15 ? 3 : 2;
		bool hasKps = kpsDim >= 10;
		var candidates = new List<FaceBox>();

		for (int i = 0; i < N; i++) {
			float conf, cx, cy, w, h;
			if (channelFirst) {
				conf = output[ConfIdx * N + i];
				cx = output[0 * N + i];
				cy = output[1 * N + i];
				w = output[2 * N + i];
				h = output[3 * N + i];
			}
			else {
				int baseIdx = i * C;
				conf = output[baseIdx + ConfIdx];
				cx = output[baseIdx + 0];
				cy = output[baseIdx + 1];
				w = output[baseIdx + 2];
				h = output[baseIdx + 3];
			}
			if (conf < ScoreThreshold) continue;

			float x1 = FaceOnnxUtil.Clamp((cx - w * 0.5f - padW) / scale, 0, origW);
			float y1 = FaceOnnxUtil.Clamp((cy - h * 0.5f - padH) / scale, 0, origH);
			float x2 = FaceOnnxUtil.Clamp((cx + w * 0.5f - padW) / scale, 0, origW);
			float y2 = FaceOnnxUtil.Clamp((cy + h * 0.5f - padH) / scale, 0, origH);

			var box = new FaceBox { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Score = conf };
			if (hasKps) {
				var kps = new float[10];
				for (int k = 0; k < 5; k++) {
					int kpsBase = 5 + k * kpsStride;
					float kx, ky;
					if (channelFirst) {
						kx = output[kpsBase * N + i];
						ky = output[(kpsBase + 1) * N + i];
					}
					else {
						kx = output[i * C + kpsBase];
						ky = output[i * C + kpsBase + 1];
					}
					kps[k * 2 + 0] = FaceOnnxUtil.Clamp((kx - padW) / scale, 0, origW);
					kps[k * 2 + 1] = FaceOnnxUtil.Clamp((ky - padH) / scale, 0, origH);
				}
				box.Landmarks = kps;
			}
			else {
				float bw = x2 - x1, bh = y2 - y1;
				box.Landmarks = [
					x1 + 0.30f * bw, y1 + 0.40f * bh,
					x1 + 0.70f * bw, y1 + 0.40f * bh,
					x1 + 0.50f * bw, y1 + 0.55f * bh,
					x1 + 0.38f * bw, y1 + 0.72f * bh,
					x1 + 0.62f * bw, y1 + 0.72f * bh
				];
			}
			candidates.Add(box);
		}
		return FaceOnnxUtil.Nms(candidates, NmsThreshold);
	}

	public void Dispose() => session?.Dispose();
}
