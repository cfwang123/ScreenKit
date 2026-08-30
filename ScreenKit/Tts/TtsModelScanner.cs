using System.IO;
using System.Text.Json;

namespace ScreenKit;

/// <summary>扫描程序目录 <c>ttsmodels</c> 下的 VITS / Matcha 模型（仅此固定路径）。</summary>
static class TtsModelScanner {
	/// <summary>程序目录旁固定文件夹 ttsmodels。</summary>
	public static string ModelsRoot() =>
		Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ttsmodels"));

	/// <summary>与 <see cref="ModelsRoot"/> 相同（仅程序目录）。</summary>
	public static string ResolveRoot() => ModelsRoot();

	public static List<TtsModelInfo> Scan(string modelsRoot = null) {
		var result = new List<TtsModelInfo>();
		var root = string.IsNullOrWhiteSpace(modelsRoot) ? ModelsRoot() : modelsRoot;
		if (!Directory.Exists(root)) return result;

		var vocoderFiles = Directory.GetFiles(root, "vocos-*.onnx");

		foreach (var dir in Directory.GetDirectories(root)) {
			string[] onnxFiles;
			try { onnxFiles = Directory.GetFiles(dir, "*.onnx"); }
			catch { continue; }

			// Matcha
			var matchaOnnx = onnxFiles.FirstOrDefault(f =>
				Path.GetFileName(f).StartsWith("model-steps-", StringComparison.OrdinalIgnoreCase));
			if (matchaOnnx != null) {
				if (!File.Exists(Path.Combine(dir, "tokens.txt"))) continue;
				var vocoder = vocoderFiles.FirstOrDefault() ?? "";
				var info = new TtsModelInfo {
					DisplayName = Path.GetFileName(dir),
					ModelDir = dir,
					Type = TtsModelType.Matcha,
					OnnxFile = Path.GetFileName(matchaOnnx),
					VocoderPath = vocoder,
					HasLexicon = File.Exists(Path.Combine(dir, "lexicon.txt")),
					HasDictDir = Directory.Exists(Path.Combine(dir, "dict")),
					HasRuleFsts = File.Exists(Path.Combine(dir, "date.fst")),
					Lang = TtsModelInfo.InferLangFromName(Path.GetFileName(dir)),
					Gender = TtsGender.Female,
				};
				info.Speakers.Add(new TtsSpeakerInfo {
					Name = "DataBaker 女声",
					Id = 0,
					Lang = TtsLang.Zh,
					Gender = TtsGender.Female,
					ChineseName = "标贝女声",
				});
				result.Add(info);
				continue;
			}

			// VITS
			var onnx = onnxFiles.FirstOrDefault(f => Path.GetFileName(f) == "model.onnx")
				?? onnxFiles.FirstOrDefault(f => Compat.Contains(f, ".int8.", StringComparison.OrdinalIgnoreCase))
				?? onnxFiles.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
			if (onnx == null) continue;
			if (!File.Exists(Path.Combine(dir, "tokens.txt"))) continue;
			// 排除明显 OCR 包（无 lexicon/dict 且无 speakers json 时仍可能误扫；
			// OCR umi 也有 tokens，但通常没有 lexicon 且目录名不同——额外要求非 det/rec 命名）
			var name = Path.GetFileName(dir) ?? "";
			if (Compat.Contains(name, "PP-OCR", StringComparison.OrdinalIgnoreCase)
				|| Compat.Contains(name, "ppocr", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("umi", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("rapid-ch", StringComparison.OrdinalIgnoreCase))
				continue;

			var vits = new TtsModelInfo {
				DisplayName = name,
				ModelDir = dir,
				Type = TtsModelType.Vits,
				OnnxFile = Path.GetFileName(onnx),
				HasLexicon = File.Exists(Path.Combine(dir, "lexicon.txt")),
				HasDictDir = Directory.Exists(Path.Combine(dir, "dict")),
				HasRuleFsts = File.Exists(Path.Combine(dir, "date.fst")),
				Lang = TtsModelInfo.InferLangFromName(name),
			};

			foreach (var jf in Directory.GetFiles(dir, "*.json")) {
				try {
					using var fs = File.OpenRead(jf);
					using var doc = JsonDocument.Parse(fs);
					if (!doc.RootElement.TryGetProperty("speakers", out var spk)) continue;
					foreach (var kv in spk.EnumerateObject()) {
						var sp = new TtsSpeakerInfo { Name = kv.Name, Id = kv.Value.GetInt32() };
						if (TtsSpeakerInfo.ZhLlNames.TryGetValue(kv.Name, out var cn))
							sp.ChineseName = cn;
						// 继承模型语言（配置可覆盖）
						sp.Lang = TtsLang.Normalize(vits.Lang.Split(',')[0]);
						vits.Speakers.Add(sp);
					}
					break;
				}
				catch { }
			}

			if (vits.Speakers.Count == 0 && Compat.Contains(vits.DisplayName, "aishell3", StringComparison.OrdinalIgnoreCase)) {
				for (int i = 0; i < 174; i++)
					vits.Speakers.Add(new TtsSpeakerInfo {
						Name = $"speaker{i}",
						Id = i,
						Lang = TtsLang.Zh,
					});
			}
			if (vits.Speakers.Count == 0)
				vits.Speakers.Add(new TtsSpeakerInfo {
					Name = "默认",
					Id = 0,
					Lang = TtsLang.Normalize(vits.Lang.Split(',')[0]),
				});

			result.Add(vits);
		}

		// 应用根目录 tts_config.json：volume + 语言/性别 + 发音人中文名
		applyconfig(root, result);
		// 配置未写全时：继承模型默认 + 从名称推断性别
		foreach (var m in result)
			filldefaults(m);
		return result;
	}

	static void filldefaults(TtsModelInfo m) {
		if (string.IsNullOrEmpty(m.Lang))
			m.Lang = TtsModelInfo.InferLangFromName(m.DisplayName);
		var defLang = "";
		if (!string.IsNullOrEmpty(m.Lang)) {
			var parts = m.Lang.Split(new[] { ',', '/', '|', '+' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length > 0) defLang = TtsLang.Normalize(parts[0]);
		}
		var defGender = TtsGender.Normalize(m.Gender);
		foreach (var sp in m.Speakers) {
			if (string.IsNullOrEmpty(sp.Lang) && !string.IsNullOrEmpty(defLang))
				sp.Lang = defLang;
			if (string.IsNullOrEmpty(sp.Gender) && !string.IsNullOrEmpty(defGender))
				sp.Gender = defGender;
			if (string.IsNullOrEmpty(sp.Gender)) {
				var inferred = TtsGender.InferFromText(sp.ChineseName);
				if (string.IsNullOrEmpty(inferred))
					inferred = TtsGender.InferFromText(sp.Name);
				sp.Gender = inferred;
			}
			sp.Lang = TtsLang.Normalize(sp.Lang);
			sp.Gender = TtsGender.Normalize(sp.Gender);
		}
	}

	/// <summary>
	/// 读取 models 根下 tts_config.json。
	/// 模型项：volume / lang / gender / speakers。
	/// speakers 值可为字符串（中文名）或对象 { name, lang, gender }。
	/// </summary>
	static void applyconfig(string root, List<TtsModelInfo> models) {
		if (models == null || models.Count == 0) return;
		var path = Path.Combine(root, "tts_config.json");
		if (!File.Exists(path)) return;
		try {
			using var fs = File.OpenRead(path);
			using var doc = JsonDocument.Parse(fs);
			if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

			foreach (var m in models) {
				var key = m.DisplayName;
				if (string.IsNullOrEmpty(key)) continue;
				if (!doc.RootElement.TryGetProperty(key, out var entry)) continue;
				if (entry.ValueKind != JsonValueKind.Object) continue;

				if (entry.TryGetProperty("volume", out var volEl)
					&& volEl.ValueKind == JsonValueKind.Number) {
					var v = volEl.GetSingle();
					if (v > 0 && !float.IsNaN(v) && !float.IsInfinity(v))
						m.Volume = Compat.Clamp(v, 0.05f, 16f);
				}

				if (entry.TryGetProperty("lang", out var langEl)
					&& langEl.ValueKind == JsonValueKind.String) {
					var lg = langEl.GetString() ?? "";
					if (!string.IsNullOrWhiteSpace(lg))
						m.Lang = lg.Trim();
				}

				if (entry.TryGetProperty("gender", out var genEl)
					&& genEl.ValueKind == JsonValueKind.String) {
					var g = TtsGender.Normalize(genEl.GetString());
					if (!string.IsNullOrEmpty(g))
						m.Gender = g;
				}

				if (entry.TryGetProperty("speakers", out var spkEl)
					&& spkEl.ValueKind == JsonValueKind.Object
					&& m.Speakers != null) {
					// 1) 更新已有发音人
					foreach (var sp in m.Speakers)
						applyspeakerentry(sp, spkEl);
					// 2) 配置中声明但模型 json 未列出的发音人（如 fanchen 的 speaker183）
					mergespeakersfromconfig(m, spkEl);
				}
			}
		}
		catch {
			// 配置损坏不影响扫描
		}
	}

	/// <summary>把 tts_config 里的 speakers 键并入模型（缺则新增）。</summary>
	static void mergespeakersfromconfig(TtsModelInfo m, JsonElement spkEl) {
		var defLang = "";
		if (!string.IsNullOrEmpty(m.Lang)) {
			var parts = m.Lang.Split(new[] { ',', '/', '|', '+' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length > 0) defLang = TtsLang.Normalize(parts[0]);
		}
		foreach (var prop in spkEl.EnumerateObject()) {
			var key = prop.Name;
			// 已有同名
			if (m.Speakers.Any(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase)))
				continue;
			// speaker{N}
			var id = -1;
			if (key.StartsWith("speaker", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(key.Substring("speaker".Length), out var sid))
				id = sid;
			// 对象内 id
			if (id < 0 && prop.Value.ValueKind == JsonValueKind.Object
				&& prop.Value.TryGetProperty("id", out var idEl)
				&& idEl.ValueKind == JsonValueKind.Number)
				id = idEl.GetInt32();
			// 已有同 Id（仅当能解析出 id）
			if (id >= 0 && m.Speakers.Any(s => s.Id == id)) {
				// 已存在：更新属性；「默认」改成配置键名
				var exist = m.Speakers.First(s => s.Id == id);
				if (exist.Name == "默认" || string.IsNullOrEmpty(exist.Name))
					exist.Name = key;
				applyspeakernode(exist, prop.Value);
				continue;
			}
			// 单人模型仅有「默认」且配置给了 speaker0：就地改名，不新增
			if (id == 0 && m.Speakers.Count == 1
				&& (m.Speakers[0].Name == "默认" || m.Speakers[0].Id == 0)) {
				var only = m.Speakers[0];
				only.Name = key;
				only.Id = 0;
				applyspeakernode(only, prop.Value);
				continue;
			}
			if (id < 0) {
				// 非 speakerN 的英文名：若单人则改写，否则按当前最大 id+1
				if (m.Speakers.Count == 1 && m.Speakers[0].Name == "默认") {
					m.Speakers[0].Name = key;
					applyspeakernode(m.Speakers[0], prop.Value);
					continue;
				}
				id = m.Speakers.Count == 0 ? 0 : m.Speakers.Max(s => s.Id) + 1;
			}
			var sp = new TtsSpeakerInfo {
				Name = key,
				Id = id,
				Lang = defLang,
				Gender = TtsGender.Normalize(m.Gender),
			};
			applyspeakernode(sp, prop.Value);
			m.Speakers.Add(sp);
		}
		// 按 Id 排序，便于浏览
		m.Speakers.Sort((a, b) => a.Id.CompareTo(b.Id));
	}

	static void applyspeakerentry(TtsSpeakerInfo sp, JsonElement spkEl) {
		// 按 Name 或 speaker{id}
		if (!trygetspeaker(spkEl, sp.Name, out var node)
			&& !trygetspeaker(spkEl, "speaker" + sp.Id, out node))
			return;
		applyspeakernode(sp, node);
	}

	static void applyspeakernode(TtsSpeakerInfo sp, JsonElement node) {
		if (node.ValueKind == JsonValueKind.String) {
			sp.ChineseName = node.GetString() ?? "";
			var inferred = TtsGender.InferFromText(sp.ChineseName);
			if (!string.IsNullOrEmpty(inferred))
				sp.Gender = inferred;
			return;
		}

		if (node.ValueKind != JsonValueKind.Object) return;

		if (node.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
			sp.ChineseName = nameEl.GetString() ?? "";
		else if (node.TryGetProperty("cn", out nameEl) && nameEl.ValueKind == JsonValueKind.String)
			sp.ChineseName = nameEl.GetString() ?? "";
		else if (node.TryGetProperty("label", out nameEl) && nameEl.ValueKind == JsonValueKind.String)
			sp.ChineseName = nameEl.GetString() ?? "";

		if (node.TryGetProperty("lang", out var le) && le.ValueKind == JsonValueKind.String)
			sp.Lang = TtsLang.Normalize(le.GetString());
		if (node.TryGetProperty("gender", out var ge) && ge.ValueKind == JsonValueKind.String)
			sp.Gender = TtsGender.Normalize(ge.GetString());

		// 仅 name 时再从文案推断性别
		if (string.IsNullOrEmpty(sp.Gender)) {
			var inferred = TtsGender.InferFromText(sp.ChineseName);
			if (!string.IsNullOrEmpty(inferred))
				sp.Gender = inferred;
		}
	}

	static bool trygetspeaker(JsonElement spkEl, string key, out JsonElement node) {
		node = default;
		if (string.IsNullOrEmpty(key)) return false;
		return spkEl.TryGetProperty(key, out node);
	}
}
