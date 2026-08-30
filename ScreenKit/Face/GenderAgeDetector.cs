using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>InsightFace genderage.onnx：96×96 → 性别 + 年龄。仅叠加显示。</summary>
sealed class GenderAgeDetector : IDisposable {
	public const string DefaultModelFile = "genderage.onnx";
	const int InputSize = 96;
	const float HalfSize = InputSize / 2f;

	readonly InferenceSession session;
	readonly string inputName;
	readonly string outputName;

	public GenderAgeDetector(string modelPath, TtsComputeMode mode = TtsComputeMode.Cpu) {
		session = FaceOnnx.Open(modelPath, mode, out _);
		inputName = FaceOnnx.InputName(session);
		outputName = session.OutputMetadata.First().Key;
	}

	public GenderAgeResult Predict(Mat bgrImage, FaceBox face) {
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
		if (pred == null || pred.Length < 3)
			throw new InvalidOperationException("genderage 模型输出维度异常");

		int gender = pred[0] > pred[1] ? 0 : 1;
		int age = (int)Math.Round(pred[2] * 100f);
		if (age < 0) age = 0;
		if (age > 120) age = 120;
		return new GenderAgeResult { Gender = gender, Age = age, RawOutput = pred };
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

	float[] run(float[] tensor) {
		var inputDense = new DenseTensor<float>(tensor, new[] { 1, 3, InputSize, InputSize });
		var inputs = new List<NamedOnnxValue> {
			NamedOnnxValue.CreateFromTensor(inputName, inputDense)
		};
		using var results = session.Run(inputs);
		foreach (var r in results) {
			if (r.Name != outputName) continue;
			var dense = r.AsTensor<float>() as DenseTensor<float>;
			if (dense != null)
				return dense.Buffer.ToArray();
		}
		return null;
	}

	public void Dispose() => session?.Dispose();
}
