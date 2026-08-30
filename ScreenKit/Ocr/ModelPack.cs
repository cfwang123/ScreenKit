using System.IO;
using System.Text;

namespace ScreenKit;

/// <summary>一组 det/cls/rec/keys 的具体选择（语言或版本变体）。</summary>
sealed class ModelVariant {
	// 属性（非字段）：WPF ComboBox DisplayMemberPath 只能绑定属性
	public string Title { get; set; }
	public string DetFile { get; set; }
	public string ClsFile { get; set; }
	public string RecFile { get; set; }
	public string KeysFile { get; set; }

	public string DetPath(string packDir) => Path.Combine(packDir, DetFile);
	public string ClsPath(string packDir) => Path.Combine(packDir, ClsFile);
	public string RecPath(string packDir) => Path.Combine(packDir, RecFile);
	public string KeysPath(string packDir) => Path.Combine(packDir, KeysFile);

	public void Validate(string packDir) {
		ensure(DetPath(packDir), "det");
		ensure(ClsPath(packDir), "cls");
		ensure(RecPath(packDir), "rec");
		ensure(KeysPath(packDir), "keys");
	}

	static void ensure(string path, string kind) {
		if (!File.Exists(path))
			throw new FileNotFoundException($"模型包缺少 {kind} 文件: {path}");
	}
}

/// <summary>
/// 模型包：ocrmodels 下的一个子目录。
/// 优先读 configs.txt（Umi-OCR 格式）；否则按文件名自动识别单变体。
/// </summary>
sealed class ModelPack {
	// 属性（非字段）：WPF ComboBox DisplayMemberPath 只能绑定属性
	public string Id { get; set; }
	public string DisplayName { get; set; }
	public string Dir { get; set; }
	public List<ModelVariant> Variants { get; set; } = new();

	public ModelVariant FindVariant(string title) {
		if (Variants.Count == 0) return null;
		if (!string.IsNullOrWhiteSpace(title)) {
			var hit = Variants.FirstOrDefault(v =>
				string.Equals(v.Title, title, StringComparison.OrdinalIgnoreCase));
			if (hit != null) return hit;
		}
		return Variants[0];
	}
}

/// <summary>扫描 ocrmodels 根目录，解析 Umi configs.txt / 自动识别。</summary>
static class ModelCatalog {
	/// <summary>显示名映射：目录名 → UI 名。</summary>
	static readonly Dictionary<string, string> DisplayMap = new(StringComparer.OrdinalIgnoreCase) {
		["umi"] = "Umi-OCR（多语言）",
		["rapid-ch"] = "Rapid mobile 简中",
		["rapid-i18n"] = "Rapid 全语种",
		["rapid"] = "Rapid",
	};

	/// <summary>程序目录旁固定文件夹 ocrmodels（仅此路径，不扫其它位置）。</summary>
	public static string ModelsRoot() =>
		Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocrmodels"));

	public static List<ModelPack> Scan(string modelsRoot = null) {
		modelsRoot ??= ModelsRoot();
		var list = new List<ModelPack>();
		// 联接目标丢失时 Exists 可能仍为 true，GetDirectories 会抛 DirectoryNotFoundException
		try {
			if (string.IsNullOrWhiteSpace(modelsRoot) || !Directory.Exists(modelsRoot))
				return list;
		}
		catch {
			return list;
		}

		string[] dirs;
		try {
			dirs = Directory.GetDirectories(modelsRoot);
		}
		catch (Exception ex) {
			CaptureLog.Ex("ModelCatalog.Scan GetDirectories", ex);
			return list;
		}

		foreach (var dir in dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
			var pack = TryLoad(dir);
			if (pack != null) list.Add(pack);
		}

		// 兼容：ocrmodels 根目录直接放 onnx（无子目录）
		if (list.Count == 0) {
			var rootPack = TryLoad(modelsRoot);
			if (rootPack != null) {
				rootPack.Id = "default";
				rootPack.DisplayName = "默认模型";
				list.Add(rootPack);
			}
		}

		// 多语言包优先，其余按显示名
		return list
			.OrderBy(p => p.Id.Equals("rapid-i18n", StringComparison.OrdinalIgnoreCase) ? 0
				: p.Id.Equals("umi", StringComparison.OrdinalIgnoreCase) ? 1
				: p.Id.Equals("rapid-ch", StringComparison.OrdinalIgnoreCase) ? 2
				: 3)
			.ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public static ModelPack Find(string packId, string modelsRoot = null) {
		var packs = Scan(modelsRoot);
		if (packs.Count == 0) return null;
		if (!string.IsNullOrWhiteSpace(packId)) {
			var hit = packs.FirstOrDefault(p =>
				string.Equals(p.Id, packId, StringComparison.OrdinalIgnoreCase));
			if (hit != null) return hit;
		}
		// 默认优先 umi，再 rapid-ch，再第一个
		return packs.FirstOrDefault(p => p.Id.Equals("umi", StringComparison.OrdinalIgnoreCase))
			?? packs.FirstOrDefault(p => p.Id.Equals("rapid-ch", StringComparison.OrdinalIgnoreCase))
			?? packs[0];
	}

	public static ModelPack TryLoad(string dir) {
		if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;

		var variants = LoadConfigs(dir);
		if (variants.Count == 0)
			variants = Autodetect(dir);
		if (variants.Count == 0) return null;

		var id = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrEmpty(id)) id = "default";

		return new ModelPack {
			Id = id,
			DisplayName = DisplayMap.TryGetValue(id, out var dn) ? dn : id,
			Dir = Path.GetFullPath(dir),
			Variants = variants,
		};
	}

	/// <summary>
	/// Umi-OCR configs.txt：每块 5 行（空行分隔）
	/// title / det / cls / rec / keys
	/// </summary>
	static List<ModelVariant> LoadConfigs(string dir) {
		var path = Path.Combine(dir, "configs.txt");
		var list = new List<ModelVariant>();
		if (!File.Exists(path)) return list;

		string text;
		try {
			text = File.ReadAllText(path, Encoding.UTF8);
		}
		catch {
			return list;
		}

		// 兼容 CRLF / 去掉 BOM
		text = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
		var parts = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in parts) {
			var items = part.Split('\n')
				.Select(l => l.Trim())
				.Where(l => l.Length > 0)
				.ToArray();
			if (items.Length < 5) continue;
			var v = new ModelVariant {
				Title = items[0],
				DetFile = items[1],
				ClsFile = items[2],
				RecFile = items[3],
				KeysFile = items[4],
			};
			// 跳过文件不齐的变体
			if (!File.Exists(Path.Combine(dir, v.DetFile))) continue;
			if (!File.Exists(Path.Combine(dir, v.RecFile))) continue;
			if (!File.Exists(Path.Combine(dir, v.KeysFile))) continue;
			// cls 可缺，后面用可选逻辑；这里要求存在以与 Umi 一致
			if (!File.Exists(Path.Combine(dir, v.ClsFile))) continue;
			list.Add(v);
		}
		return list;
	}

	/// <summary>无 configs 时按文件名猜测一组默认模型。</summary>
	static List<ModelVariant> Autodetect(string dir) {
		var onnx = Directory.GetFiles(dir, "*.onnx", SearchOption.TopDirectoryOnly);
		if (onnx.Length == 0) return new List<ModelVariant>();

		string pick(params string[] patterns) {
			foreach (var pat in patterns) {
				var hits = Directory.GetFiles(dir, pat, SearchOption.TopDirectoryOnly);
				// 优先不含 server 的短名，再任意
				var ordered = hits.OrderBy(f => f.Length).ToArray();
				if (ordered.Length > 0) return Path.GetFileName(ordered[0]);
			}
			return null;
		}

		var det = pick("*det*.onnx", "*_det_*.onnx");
		var rec = pick("rec_*.onnx", "*rec*.onnx", "*_rec_*.onnx");
		var cls = pick("*cls*.onnx", "*_cls_*.onnx");
		var keys = pick("dict_chinese.txt", "ppocr_keys_v1.txt", "dict_*.txt", "*keys*.txt", "keys.txt");

		if (det == null || rec == null || keys == null) return new List<ModelVariant>();
		cls ??= pick("*.onnx"); // 兜底不应发生
		if (cls == null) return new List<ModelVariant>();

		return new List<ModelVariant> {
			new() {
				Title = "默认",
				DetFile = det,
				ClsFile = cls,
				RecFile = rec,
				KeysFile = keys,
			},
		};
	}
}
