namespace ScreenKit;

/// <summary>扫描程序目录 <c>facemodels</c> 下的 InsightFace ONNX。</summary>
static class FaceModels {
	public static string ModelsRoot() =>
		Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "facemodels"));

	public static List<string> ListOnnx() {
		var root = ModelsRoot();
		if (!Directory.Exists(root)) return new();
		try {
			return Directory.GetFiles(root, "*.onnx")
				.Select(Path.GetFileName)
				.Where(n => !string.IsNullOrEmpty(n))
				.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch {
			return new();
		}
	}

	public static List<string> DetModels(List<string> onnx) {
		var det = onnx.Where(f => {
			var n = f.ToLowerInvariant();
			return (n.Contains("scrfd") || n.Contains("det") || n.Contains("yolo"))
				&& !n.Contains("2d106det") && !n.Contains("1k3d68");
		}).ToList();
		return det.Count > 0 ? det : onnx;
	}

	public static List<string> RegModels(List<string> onnx) {
		var det = new HashSet<string>(DetModels(onnx), StringComparer.OrdinalIgnoreCase);
		var reg = onnx.Where(f => {
			var n = f.ToLowerInvariant();
			return !det.Contains(f)
				&& !n.Contains("1k3d68") && !n.Contains("2d106det")
				&& !n.Contains("yolo") && !n.Contains("genderage");
		}).ToList();
		return reg.Count > 0 ? reg : onnx;
	}

	public static List<string> LmkModels(List<string> onnx) =>
		onnx.Where(f => {
			var n = f.ToLowerInvariant();
			return n.Contains("2d106det") || n.Contains("1k3d68");
		}).ToList();

	public static List<string> AttrModels(List<string> onnx) =>
		onnx.Where(f => f.ToLowerInvariant().Contains("genderage")).ToList();

	public static bool IsDetFile(string fileName) {
		var n = (fileName ?? "").ToLowerInvariant();
		if (n.Contains("2d106det") || n.Contains("1k3d68") || n.Contains("genderage"))
			return false;
		return n.Contains("scrfd") || n.Contains("yolo") || n.Contains("det");
	}

	public static bool IsRegFile(string fileName) {
		var n = (fileName ?? "").ToLowerInvariant();
		if (IsDetFile(fileName) || n.Contains("1k3d68") || n.Contains("2d106det")
			|| n.Contains("genderage") || n.Contains("yolo"))
			return false;
		return n.Contains("w600k") || n.Contains("glint") || n.Contains("ms1mv")
			|| n.Contains("r50") || n.Contains("r100") || n.Contains("mbf");
	}

	/// <summary>facemodels 下是否已有检测+识别各至少 1 个 ONNX。</summary>
	public static bool IsReady() {
		var onnx = ListOnnx();
		return onnx.Any(IsDetFile) && onnx.Any(IsRegFile);
	}

	public static string PathOf(string fileName) =>
		Path.Combine(ModelsRoot(), fileName ?? "");
}
