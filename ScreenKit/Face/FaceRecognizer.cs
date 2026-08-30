using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>InsightFace 识别：112×112 对齐人脸 → 特征向量。</summary>
sealed class FaceRecognizer : IDisposable {
	const int InputSize = 112;
	const float InputMean = 127.5f;
	const float InputStd = 127.5f;

	static readonly float[,] Template = {
		{ 38.2946f, 51.6963f },
		{ 73.5318f, 51.5014f },
		{ 56.0252f, 71.7366f },
		{ 41.5493f, 92.3655f },
		{ 70.7299f, 92.2041f }
	};

	readonly InferenceSession session;
	readonly string inputName;

	public FaceRecognizer(string modelPath, TtsComputeMode mode = TtsComputeMode.Cpu) {
		session = FaceOnnx.Open(modelPath, mode, out _);
		inputName = FaceOnnx.InputName(session);
	}

	public float[] Extract(Mat bgrImage, FaceBox face) {
		if (face == null) throw new ArgumentNullException(nameof(face));
		return Extract(bgrImage, face.Landmarks);
	}

	public float[] Extract(Mat bgrImage, float[] landmarks5) {
		if (bgrImage == null) throw new ArgumentNullException(nameof(bgrImage));
		if (landmarks5 == null || landmarks5.Length < 10)
			throw new ArgumentException("人脸关键点缺失，无法对齐");
		var aligned = Align(bgrImage, landmarks5);
		try { return extractaligned(aligned); }
		finally { aligned.Dispose(); }
	}

	public Mat Align(Mat bgrImage, float[] landmarks) {
		var srcMat = new Mat(5, 2, MatType.CV_32F);
		var dstMat = new Mat(5, 2, MatType.CV_32F);
		for (int i = 0; i < 5; i++) {
			srcMat.Set(i, 0, landmarks[i * 2 + 0]);
			srcMat.Set(i, 1, landmarks[i * 2 + 1]);
			dstMat.Set(i, 0, Template[i, 0]);
			dstMat.Set(i, 1, Template[i, 1]);
		}
		Mat M;
		try {
			M = Cv2.EstimateAffinePartial2D(srcMat, dstMat);
			if (M == null || M.Empty())
				throw new InvalidOperationException("无法估计人脸对齐变换矩阵");
		}
		finally {
			srcMat.Dispose();
			dstMat.Dispose();
		}
		var aligned = new Mat();
		try {
			Cv2.WarpAffine(bgrImage, aligned, M, new OpenCvSharp.Size(InputSize, InputSize),
				InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(0, 0, 0));
			return aligned;
		}
		finally { M.Dispose(); }
	}

	float[] extractaligned(Mat alignedBgr) {
		if (alignedBgr.Rows != InputSize || alignedBgr.Cols != InputSize)
			throw new ArgumentException($"输入人脸图必须为 {InputSize}x{InputSize}");
		float[] tensor = buildinput(alignedBgr);
		var inputDense = new DenseTensor<float>(tensor, new[] { 1, 3, InputSize, InputSize });
		var inputs = new List<NamedOnnxValue> {
			NamedOnnxValue.CreateFromTensor(inputName, inputDense)
		};
		float[] embedding = null;
		using (var results = session.Run(inputs)) {
			foreach (var r in results) {
				var dense = r.AsTensor<float>() as DenseTensor<float>;
				if (dense != null)
					embedding = dense.Buffer.ToArray();
				break;
			}
		}
		if (embedding == null)
			throw new InvalidOperationException("模型未返回输出");
		l2(embedding);
		return embedding;
	}

	float[] buildinput(Mat alignedBgr) {
		int h = alignedBgr.Rows, w = alignedBgr.Cols;
		var floatMat = new Mat();
		alignedBgr.ConvertTo(floatMat, MatType.CV_32FC3, 1.0 / InputStd, -InputMean / InputStd);
		var rgb = new Mat();
		Cv2.CvtColor(floatMat, rgb, ColorConversionCodes.BGR2RGB);
		floatMat.Dispose();
		return FaceOnnxUtil.Chw(rgb, h, w);
	}

	static void l2(float[] v) {
		double sum = 0;
		for (int i = 0; i < v.Length; i++)
			sum += (double)v[i] * v[i];
		if (sum <= 0) return;
		float norm = (float)Math.Sqrt(sum);
		for (int i = 0; i < v.Length; i++)
			v[i] /= norm;
	}

	public void Dispose() => session?.Dispose();
}
