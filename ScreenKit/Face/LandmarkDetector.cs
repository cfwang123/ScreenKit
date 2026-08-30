using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>InsightFace 2d106det / 1k3d68 关键点。仅叠加显示，不参与识别。</summary>
sealed class LandmarkDetector : IDisposable {
	const int InputSize = 192;
	const float HalfSize = InputSize / 2f;

	readonly InferenceSession session;
	readonly string inputName;
	readonly int lmkDim;
	readonly int lmkNum;

	public int LandmarkDim => lmkDim;
	public int LandmarkNum => lmkNum;

	public LandmarkDetector(string modelPath, TtsComputeMode mode = TtsComputeMode.Cpu) {
		session = FaceOnnx.Open(modelPath, mode, out _);
		inputName = FaceOnnx.InputName(session);
		var outMeta = session.OutputMetadata.First();
		var dims = outMeta.Value.Dimensions;
		int outDim = dims[1];
		if (outDim >= 3000) {
			lmkDim = 3;
			lmkNum = 68;
		}
		else {
			lmkDim = 2;
			lmkNum = outDim / 2;
		}
	}

	public float[] Detect(Mat bgrImage, FaceBox face) {
		if (bgrImage == null) throw new ArgumentNullException(nameof(bgrImage));
		if (face == null) throw new ArgumentNullException(nameof(face));

		float w = face.X2 - face.X1;
		float h = face.Y2 - face.Y1;
		float cx = (face.X1 + face.X2) * 0.5f;
		float cy = (face.Y1 + face.Y2) * 0.5f;
		float scale = InputSize / (Math.Max(w, h) * 1.5f);

		using var M = new Mat(2, 3, MatType.CV_32F);
		using var aimg = new Mat();
		M.Set(0, 0, scale); M.Set(0, 1, 0f); M.Set(0, 2, HalfSize - cx * scale);
		M.Set(1, 0, 0f); M.Set(1, 1, scale); M.Set(1, 2, HalfSize - cy * scale);
		Cv2.WarpAffine(bgrImage, aimg, M, new OpenCvSharp.Size(InputSize, InputSize),
			InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(0, 0, 0));

		float[] tensor = buildinput(aimg);
		float[] pred = run(tensor);

		int totalPoints = pred.Length / lmkDim;
		int startIdx = (totalPoints - lmkNum) * lmkDim;
		var pts = new float[lmkNum * lmkDim];
		for (int i = 0; i < lmkNum; i++) {
			int si = startIdx + i * lmkDim;
			pts[i * lmkDim + 0] = (pred[si + 0] + 1f) * HalfSize;
			pts[i * lmkDim + 1] = (pred[si + 1] + 1f) * HalfSize;
			if (lmkDim == 3)
				pts[i * lmkDim + 2] = pred[si + 2] * HalfSize;
		}

		using var IM = new Mat();
		Cv2.InvertAffineTransform(M, IM);
		float im00 = IM.At<float>(0, 0), im01 = IM.At<float>(0, 1), im02 = IM.At<float>(0, 2);
		float im10 = IM.At<float>(1, 0), im11 = IM.At<float>(1, 1), im12 = IM.At<float>(1, 2);
		float imScale = (float)Math.Sqrt(im00 * im00 + im01 * im01);

		var result = new float[lmkNum * lmkDim];
		for (int i = 0; i < lmkNum; i++) {
			float px = pts[i * lmkDim + 0];
			float py = pts[i * lmkDim + 1];
			result[i * lmkDim + 0] = im00 * px + im01 * py + im02;
			result[i * lmkDim + 1] = im10 * px + im11 * py + im12;
			if (lmkDim == 3)
				result[i * lmkDim + 2] = pts[i * lmkDim + 2] * imScale;
		}
		return result;
	}

	float[] buildinput(Mat aimg) {
		int h = aimg.Rows, w = aimg.Cols;
		var rgb = new Mat();
		Cv2.CvtColor(aimg, rgb, ColorConversionCodes.BGR2RGB);
		var floatMat = new Mat();
		rgb.ConvertTo(floatMat, MatType.CV_32FC3);
		rgb.Dispose();
		return FaceOnnxUtil.Chw(floatMat, h, w);
	}

	float[] run(float[] inputTensor) {
		var inputDense = new DenseTensor<float>(inputTensor, new[] { 1, 3, InputSize, InputSize });
		var inputs = new List<NamedOnnxValue> {
			NamedOnnxValue.CreateFromTensor(inputName, inputDense)
		};
		using var results = session.Run(inputs);
		foreach (var r in results) {
			var dense = r.AsTensor<float>() as DenseTensor<float>
				?? throw new InvalidOperationException("模型输出不是 DenseTensor<float>");
			return dense.Buffer.ToArray();
		}
		throw new InvalidOperationException("模型未返回输出");
	}

	public void Dispose() => session?.Dispose();
}
