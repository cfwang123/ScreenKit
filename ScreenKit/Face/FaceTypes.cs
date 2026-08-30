using System.Globalization;

namespace ScreenKit;

/// <summary>人脸检测框（含 5 个关键点）。</summary>
sealed class FaceBox {
	public float X1, Y1, X2, Y2;
	public float Score;
	/// <summary>长度 10：(x0,y0 … x4,y4) 左眼/右眼/鼻/左嘴角/右嘴角。</summary>
	public float[] Landmarks;

	public float Area => Math.Max(0, X2 - X1) * Math.Max(0, Y2 - Y1);

	public override string ToString() =>
		$"bbox=({X1:F1},{Y1:F1},{X2:F1},{Y2:F1}) score={Score:F3}";
}

/// <summary>带计时的特征提取结果。</summary>
sealed class FaceExtractResult {
	public float[] Feature;
	public FaceBox Face;
	public int FaceCount;
	public double LoadMs;
	public double DetectMs;
	public double ExtractMs;
	public double TotalMs;
}

/// <summary>性别年龄。Gender: 0=女, 1=男。</summary>
struct GenderAgeResult {
	public int Gender;
	public int Age;
	public float[] RawOutput;
	public string GenderText => Gender == 1 ? "男" : "女";
	public override string ToString() => $"{GenderText} {Age}岁";
}

/// <summary>特征文件读写（FACE_FEAT_V1 文本格式）。</summary>
static class FeatureFile {
	public const string Header = "FACE_FEAT_V1";

	public static void Save(string path, float[] feat) {
		using var sw = new StreamWriter(path);
		var culture = CultureInfo.InvariantCulture;
		sw.WriteLine($"{Header} {feat.Length}");
		for (int i = 0; i < feat.Length; i++) {
			sw.Write(feat[i].ToString("R", culture));
			sw.Write(i == feat.Length - 1 ? Environment.NewLine : " ");
		}
	}

	public static float[] Load(string path) {
		var lines = File.ReadAllLines(path);
		if (lines.Length < 2)
			throw new FormatException("特征文件格式错误：行数不足");
		var headerParts = lines[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (headerParts.Length < 2 || headerParts[0] != Header)
			throw new FormatException("特征文件格式不正确，缺少有效头部 FACE_FEAT_V1");
		int dim = int.Parse(headerParts[1], CultureInfo.InvariantCulture);
		var valueParts = lines[1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (valueParts.Length != dim)
			throw new FormatException($"特征维度不匹配：期望 {dim}，实际 {valueParts.Length}");
		var feat = new float[dim];
		var culture = CultureInfo.InvariantCulture;
		for (int i = 0; i < dim; i++)
			feat[i] = float.Parse(valueParts[i], culture);
		return feat;
	}
}

/// <summary>余弦相似度。特征已 L2 归一化时等于内积，范围 [-1, 1]。</summary>
static class FaceSimilarity {
	public static float Cosine(float[] a, float[] b) {
		if (a == null || b == null || a.Length != b.Length)
			throw new ArgumentException("特征向量长度不一致或为空");
		double dot = 0, na = 0, nb = 0;
		for (int i = 0; i < a.Length; i++) {
			dot += a[i] * b[i];
			na += (double)a[i] * a[i];
			nb += (double)b[i] * b[i];
		}
		if (na <= 0 || nb <= 0) return 0f;
		return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
	}
}
