using System.IO;

namespace WpfOCR;

/// <summary>扫描程序目录 <c>asrmodels</c> 下的离线 / 流式 ASR 模型（仅此固定路径）。</summary>
static class AsrModelScanner {
	/// <summary>程序目录旁固定文件夹 asrmodels。</summary>
	public static string ModelsRoot() =>
		Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "asrmodels"));

	/// <summary>与 <see cref="ModelsRoot"/> 相同（仅程序目录）。</summary>
	public static string ResolveRoot() => ModelsRoot();

	/// <param name="modelsRoot">指定则只扫该目录；空则扫程序目录 asrmodels。</param>
	public static List<AsrModelInfo> Scan(string modelsRoot = null) {
		var root = string.IsNullOrWhiteSpace(modelsRoot) ? ModelsRoot() : modelsRoot;
		return scanone(root)
			.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	static List<AsrModelInfo> scanone(string root) {
		var result = new List<AsrModelInfo>();
		if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;

		// 支持：1) asrmodels/子目录/  2) 文件直接放在 asrmodels 根（解压未建子夹）
		var dirs = new List<string>();
		if (findtokens(root) != null
			&& (File.Exists(Path.Combine(root, "model.int8.onnx"))
				|| File.Exists(Path.Combine(root, "model.onnx"))))
			dirs.Add(root);

		foreach (var dir in Directory.GetDirectories(root)) {
			var name = Path.GetFileName(dir) ?? "";
			// 跳过明显 TTS 目录、归档、测试音频夹
			if (name.StartsWith("vits-", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("matcha-", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("_", StringComparison.Ordinal)
				|| Compat.Contains(name, "tts", StringComparison.OrdinalIgnoreCase)
				|| Compat.Contains(name, "vits", StringComparison.OrdinalIgnoreCase)
				|| Compat.Contains(name, "matcha", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("test_wavs", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("_archives", StringComparison.OrdinalIgnoreCase))
				continue;
			if (findtokens(dir) == null)
				continue;
			dirs.Add(dir);
		}

		foreach (var dir in dirs) {
			var name = string.Equals(Path.GetFullPath(dir), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
				? (inferrootname(dir) ?? "asrmodel")
				: (Path.GetFileName(dir) ?? "asrmodel");
			var info = detect(dir, name);
			if (info != null)
				result.Add(info);
		}
		return result;
	}

	/// <summary>tokens.txt 或 Whisper 的 tiny-tokens.txt 等。</summary>
	static string findtokens(string dir) {
		var t = Path.Combine(dir, "tokens.txt");
		if (File.Exists(t)) return t;
		try {
			return Directory.GetFiles(dir, "*-tokens.txt")
				.Concat(Directory.GetFiles(dir, "*tokens.txt"))
				.OrderBy(x => x.Length)
				.FirstOrDefault();
		}
		catch {
			return null;
		}
	}

	/// <summary>根目录模型：从 README 首行或默认 SenseVoice 名推断显示名。</summary>
	static string inferrootname(string dir) {
		try {
			var readme = Path.Combine(dir, "README.md");
			if (File.Exists(readme)) {
				var first = File.ReadLines(readme).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
				if (!string.IsNullOrWhiteSpace(first) && first.Length < 120
					&& Compat.Contains(first, "sense", StringComparison.OrdinalIgnoreCase))
					return first.Trim().Trim('#', ' ');
			}
		}
		catch { }
		// 有 model.int8 + tokens 且体积像 SenseVoice
		if (File.Exists(Path.Combine(dir, "model.int8.onnx")))
			return "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8";
		return "asrmodel";
	}

	static AsrModelInfo detect(string dir, string name) {
		var tokensPath = findtokens(dir);
		if (tokensPath == null) return null;
		var tokensFile = Path.GetFileName(tokensPath);

		// SenseVoice：model.int8.onnx / model.onnx，目录名常含 sense-voice
		var sense = firstexist(dir, "model.int8.onnx", "model.onnx");
		// Whisper 包常为 tiny-encoder.int8.onnx；Zipformer 为 encoder.int8.onnx
		var enc = firstonnx(dir, "encoder");
		var dec = firstonnx(dir, "decoder");
		var join = firstonnx(dir, "joiner");

		var isSense = Compat.Contains(name, "sense-voice", StringComparison.OrdinalIgnoreCase)
			|| Compat.Contains(name, "sensevoice", StringComparison.OrdinalIgnoreCase);
		var isWhisper = Compat.Contains(name, "whisper", StringComparison.OrdinalIgnoreCase)
			|| (enc != null && Compat.Contains(Path.GetFileName(enc), "tiny-", StringComparison.OrdinalIgnoreCase));
		var isPara = Compat.Contains(name, "paraformer", StringComparison.OrdinalIgnoreCase);
		var isStreaming = isstreamingname(name, enc, dec, join, sense);

		if (isSense && sense != null && !isStreaming) {
			return new AsrModelInfo {
				DisplayName = name,
				ModelDir = dir,
				Type = AsrModelType.SenseVoice,
				ModelFile = Path.GetFileName(sense),
				TokensFile = tokensFile,
				SampleRate = 16000,
			};
		}

		if (isWhisper && enc != null && dec != null) {
			return new AsrModelInfo {
				DisplayName = name,
				ModelDir = dir,
				Type = AsrModelType.Whisper,
				EncoderFile = Path.GetFileName(enc),
				DecoderFile = Path.GetFileName(dec),
				TokensFile = tokensFile,
				SampleRate = 16000,
				IsStreaming = false,
			};
		}

		if (enc != null && dec != null && join != null) {
			return new AsrModelInfo {
				DisplayName = name,
				ModelDir = dir,
				Type = AsrModelType.Transducer,
				EncoderFile = Path.GetFileName(enc),
				DecoderFile = Path.GetFileName(dec),
				JoinerFile = Path.GetFileName(join),
				TokensFile = tokensFile,
				SampleRate = 16000,
				IsStreaming = isStreaming,
			};
		}

		if ((isPara || sense != null) && sense != null && enc == null) {
			// 单 model.onnx + tokens → Paraformer 或 SenseVoice 或 流式 CTC
			var type = isSense ? AsrModelType.SenseVoice
				: isPara ? AsrModelType.Paraformer
				: AsrModelType.Paraformer;
			if (!isPara && !isSense) {
				if (isStreaming)
					type = AsrModelType.ZipformerCtc;
				else
					type = AsrModelType.SenseVoice;
			}
			return new AsrModelInfo {
				DisplayName = name,
				ModelDir = dir,
				Type = type,
				ModelFile = Path.GetFileName(sense),
				TokensFile = tokensFile,
				SampleRate = 16000,
				IsStreaming = isStreaming && type == AsrModelType.ZipformerCtc,
			};
		}

		// Zipformer CTC 等单模型
		if (sense != null) {
			return new AsrModelInfo {
				DisplayName = name,
				ModelDir = dir,
				Type = AsrModelType.ZipformerCtc,
				ModelFile = Path.GetFileName(sense),
				TokensFile = tokensFile,
				SampleRate = 16000,
				IsStreaming = isStreaming,
			};
		}
		return null;
	}

	static bool isstreamingname(string name, string enc, string dec, string join, string model) {
		if (Compat.Contains(name, "streaming", StringComparison.OrdinalIgnoreCase))
			return true;
		// 文件名含 chunk-16 等典型流式导出标记
		foreach (var p in new[] { enc, dec, join, model }) {
			if (string.IsNullOrEmpty(p)) continue;
			var f = Path.GetFileName(p);
			if (Compat.Contains(f, "chunk-", StringComparison.OrdinalIgnoreCase)
				|| Compat.Contains(f, "chunk_", StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	static string firstexist(string dir, params string[] names) {
		foreach (var n in names) {
			var p = Path.Combine(dir, n);
			if (File.Exists(p)) return p;
		}
		return null;
	}

	/// <summary>
	/// 按角色找 onnx：优先 *encoder.int8.onnx / encoder.int8.onnx，再 float32。
	/// 兼容 Whisper 的 tiny-encoder.int8.onnx。
	/// </summary>
	static string firstonnx(string dir, string role) {
		// 精确常见名
		var exact = firstexist(dir,
			$"{role}.int8.onnx", $"{role}.onnx",
			$"{role}-epoch-99-avg-1.int8.onnx", $"{role}-epoch-99-avg-1.onnx");
		if (exact != null) return exact;
		try {
			var files = Directory.GetFiles(dir, "*.onnx")
				.Where(f => {
					var n = Path.GetFileName(f) ?? "";
					return Compat.Contains(n, role, StringComparison.OrdinalIgnoreCase);
				})
				.ToList();
			if (files.Count == 0) return null;
			// 优先 int8，再较短文件名
			return files
				.OrderBy(f => Compat.Contains(Path.GetFileName(f), "int8", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
				.ThenBy(f => Path.GetFileName(f)?.Length ?? 0)
				.FirstOrDefault();
		}
		catch {
			return null;
		}
	}
}
