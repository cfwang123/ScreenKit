namespace WpfOCR;

/// <summary>扫描程序目录 <c>translatemodels</c>（Opus-MT ONNX，仅此固定路径）。</summary>
static class TranslateModelScanner {
	/// <summary>程序目录旁固定文件夹 translatemodels。</summary>
	public static string ModelsRoot() =>
		Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "translatemodels"));

	/// <summary>与 <see cref="ModelsRoot"/> 相同（仅程序目录）。</summary>
	public static string ResolveRoot() => ModelsRoot();

	public static List<TranslateModelInfo> Scan(string modelsRoot = null) {
		var root = string.IsNullOrWhiteSpace(modelsRoot) ? ModelsRoot() : modelsRoot;
		var list = new List<TranslateModelInfo>();
		if (!Directory.Exists(root)) return list;

		// 仅进程内 ONNX（不再加载 PyTorch / Python）
		tryadd(list, root, "opus-mt-zh-en-onnx", "zh", "en", preferOnnx: true);
		tryadd(list, root, "opus-mt-en-zh-onnx", "en", "zh", preferOnnx: true);
		// 若目录名无 -onnx 但内含 encoder/decoder.onnx 也收录
		tryadd(list, root, "opus-mt-zh-en", "zh", "en", preferOnnx: true);
		tryadd(list, root, "opus-mt-en-zh", "en", "zh", preferOnnx: true);

		foreach (var dir in Directory.GetDirectories(root)) {
			var name = Path.GetFileName(dir) ?? "";
			var full = Path.GetFullPath(dir);
			if (list.Any(m => string.Equals(m.ModelDir, full, StringComparison.OrdinalIgnoreCase)))
				continue;
			// 解析语言对（去掉 -onnx 后缀再 parse）
			var keyName = name;
			if (keyName.EndsWith("-onnx", StringComparison.OrdinalIgnoreCase))
				keyName = keyName.Substring(0, keyName.Length - 5);
			if (!TrLang.TryParsePair(keyName, out var src, out var dst)
				&& !TrLang.TryParsePair(name, out src, out dst))
				continue;
			tryaddpath(list, dir, src, dst, name);
		}

		// 同方向：ONNX 优先，去掉重复 PyTorch（若已有 onnx）
		var best = new Dictionary<string, TranslateModelInfo>(StringComparer.OrdinalIgnoreCase);
		foreach (var m in list.OrderByDescending(x => x.IsOnnx).ThenBy(x => x.DisplayName)) {
			if (!m.IsReady) continue;
			if (!best.ContainsKey(m.DirKey))
				best[m.DirKey] = m;
		}
		// 未就绪的也保留提示
		foreach (var m in list) {
			if (m.IsReady) continue;
			if (!best.ContainsKey(m.DirKey))
				best[m.DirKey] = m;
		}
		return best.Values
			.OrderBy(m => m.SourceLang, StringComparer.OrdinalIgnoreCase)
			.ThenBy(m => m.TargetLang, StringComparer.OrdinalIgnoreCase)
			.ThenByDescending(m => m.IsOnnx)
			.ToList();
	}

	static void tryadd(List<TranslateModelInfo> list, string root, string folder, string src, string dst, bool preferOnnx) {
		var dir = Path.Combine(root, folder);
		if (!Directory.Exists(dir)) return;
		tryaddpath(list, dir, src, dst, folder);
	}

	static void tryaddpath(List<TranslateModelInfo> list, string dir, string src, string dst, string name) {
		src = TrLang.Normalize(src);
		dst = TrLang.Normalize(dst);
		var key = $"{src}-{dst}";
		var onnx = isonnx(dir);
		// 仅 ONNX 就绪；纯 PT 目录标记缺文件
		var ready = onnx && isreadyonnx(dir);
		var tag = onnx ? "ONNX" : "需导出ONNX";
		list.Add(new TranslateModelInfo {
			DirKey = key,
			SourceLang = src,
			TargetLang = dst,
			DisplayName = $"{TrLang.Label(src)} → {TrLang.Label(dst)} ({name}) [{tag}]",
			ModelDir = Path.GetFullPath(dir),
			IsReady = ready,
			IsOnnx = onnx,
		});
	}

	static bool isonnx(string dir) {
		if (File.Exists(Path.Combine(dir, "encoder_model.onnx"))
			&& File.Exists(Path.Combine(dir, "decoder_model.onnx")))
			return true;
		var b = Path.Combine(dir, "backend.txt");
		if (File.Exists(b)) {
			try {
				return Compat.Contains(File.ReadAllText(b), "onnx", StringComparison.OrdinalIgnoreCase);
			}
			catch { }
		}
		var name = Path.GetFileName(dir) ?? "";
		return name.EndsWith("-onnx", StringComparison.OrdinalIgnoreCase);
	}

	static bool isreadyonnx(string dir) {
		if (!File.Exists(Path.Combine(dir, "encoder_model.onnx"))) return false;
		if (!File.Exists(Path.Combine(dir, "decoder_model.onnx"))) return false;
		// tokenizer
		if (!File.Exists(Path.Combine(dir, "source.spm"))) return false;
		if (!File.Exists(Path.Combine(dir, "target.spm"))) return false;
		if (!hasany(dir, "vocab.json", "vocab.txt")) return false;
		return true;
	}

	static bool isreadypt(string dir) {
		if (!File.Exists(Path.Combine(dir, "pytorch_model.bin"))) return false;
		if (!File.Exists(Path.Combine(dir, "source.spm"))) return false;
		if (!File.Exists(Path.Combine(dir, "target.spm"))) return false;
		if (!hasany(dir, "config.json", "config.txt")) return false;
		if (!hasany(dir, "vocab.json", "vocab.txt")) return false;
		return true;
	}

	static bool hasany(string dir, params string[] names) {
		foreach (var n in names)
			if (File.Exists(Path.Combine(dir, n))) return true;
		return false;
	}
}
