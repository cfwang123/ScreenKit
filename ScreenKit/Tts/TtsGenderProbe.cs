using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>
/// 对全部 TTS 发音人合成短句，用 F0 判定男女，并可写回 tts_config.json。
/// </summary>
static class TtsGenderProbe {
	/// <summary>短测试句：含元音便于估计音高。</summary>
	public const string TestText = "你好，今天天气真不错。";

	public sealed class SpeakerProbe {
		public string Model { get; set; } = "";
		public string SpeakerKey { get; set; } = "";
		public int Sid { get; set; }
		public string ChineseName { get; set; } = "";
		public float F0 { get; set; }
		public int VoicedFrames { get; set; }
		public string Gender { get; set; } = "";
		public string PrevGender { get; set; } = "";
		public string Error { get; set; } = "";
		public int Ms { get; set; }
	}

	public sealed class ProbeReport {
		public List<SpeakerProbe> Items { get; } = new();
		public string ConfigPath { get; set; } = "";
		public bool WroteConfig { get; set; }
		public int OkCount => Items.Count(i => string.IsNullOrEmpty(i.Error) && !string.IsNullOrEmpty(i.Gender));
		public int FailCount => Items.Count(i => !string.IsNullOrEmpty(i.Error));
	}

	/// <param name="writeConfig">是否合并写回 tts_config.json</param>
	/// <param name="device">cpu / gpu / igpu / auto</param>
	/// <param name="onlyModel">只测某一模型目录名，空=全部</param>
	/// <param name="log">进度日志</param>
	public static ProbeReport Run(
		bool writeConfig = true,
		string device = "auto",
		string onlyModel = null,
		Action<string> log = null) {
		void L(string s) => log?.Invoke(s);
		var report = new ProbeReport();
		var models = TtsModelScanner.Scan();
		if (models.Count == 0) {
			L("未找到 TTS 模型");
			return report;
		}
		if (!string.IsNullOrWhiteSpace(onlyModel)) {
			models = models
				.Where(m => string.Equals(m.DisplayName, onlyModel, StringComparison.OrdinalIgnoreCase)
					|| Compat.Contains(m.DisplayName ?? "", onlyModel, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (models.Count == 0) {
				L($"未匹配模型: {onlyModel}");
				return report;
			}
		}

		var mode = parseMode(device);
		var totalSpk = models.Sum(m => m.Speakers?.Count ?? 0);
		L($"模型 {models.Count} 个，发音人共 {totalSpk}，设备={mode}，测试句「{TestText}」");
		L($"F0 阈值: ≥{TtsPitchGender.FemaleThresholdHz:0}Hz → 女，否则 → 男");

		using var engine = new TtsEngine { Mode = mode };
		var n = 0;
		foreach (var m in models) {
			if (m.Speakers == null || m.Speakers.Count == 0) continue;
			L($"── 加载 {m.DisplayName} ({m.Speakers.Count} 人) ──");
			try {
				engine.LoadModel(m);
				L($"  provider={engine.Provider} sr={engine.SampleRate}");
			}
			catch (Exception ex) {
				L($"  加载失败: {ex.Message}");
				foreach (var sp in m.Speakers) {
					report.Items.Add(new SpeakerProbe {
						Model = m.DisplayName,
						SpeakerKey = sp.Name,
						Sid = sp.Id,
						ChineseName = sp.ChineseName,
						PrevGender = sp.Gender,
						Error = "模型加载失败: " + ex.Message,
					});
				}
				continue;
			}

			foreach (var sp in m.Speakers) {
				n++;
				var item = new SpeakerProbe {
					Model = m.DisplayName,
					SpeakerKey = sp.Name,
					Sid = sp.Id,
					ChineseName = sp.ChineseName,
					PrevGender = sp.Gender ?? "",
				};
				var t0 = Environment.TickCount;
				try {
					// 不做 volume 增益，避免削波干扰基频估计
					var (samples, sr) = engine.Synthesize(TestText, sp.Id, 1.15f, applyVolume: false);
					var ana = TtsPitchGender.Analyze(samples, sr);
					if (!ana.Ok) {
						// 换短句再试
						(samples, sr) = engine.Synthesize("一二三四五六七", sp.Id, 1f, applyVolume: false);
						ana = TtsPitchGender.Analyze(samples, sr);
					}
					if (!ana.Ok && TtsLang.Match(m.Lang, TtsLang.En)) {
						(samples, sr) = engine.Synthesize("Hello, how are you today?", sp.Id, 1f, applyVolume: false);
						ana = TtsPitchGender.Analyze(samples, sr);
					}
					item.Ms = unchecked(Environment.TickCount - t0);
					if (!ana.Ok) {
						item.Error = "无法估计 F0（无浊音帧）";
						L($"  [{n}/{totalSpk}] {m.DisplayName}/{sp.Name} FAIL {item.Error} ({item.Ms}ms)");
					}
					else {
						item.F0 = ana.MedianF0;
						item.VoicedFrames = ana.VoicedFrames;
						item.Gender = ana.Gender;
						sp.Gender = ana.Gender; // 同步内存
						var changed = !string.Equals(item.PrevGender, item.Gender, StringComparison.OrdinalIgnoreCase);
						L($"  [{n}/{totalSpk}] {m.DisplayName}/{sp.Name} F0={item.F0:0.0}Hz frames={item.VoicedFrames} → {item.Gender}"
							+ (changed && !string.IsNullOrEmpty(item.PrevGender) ? $" (was {item.PrevGender})" : "")
							+ $" ({item.Ms}ms)");
					}
				}
				catch (Exception ex) {
					item.Ms = unchecked(Environment.TickCount - t0);
					item.Error = ex.GetType().Name + ": " + ex.Message;
					L($"  [{n}/{totalSpk}] {m.DisplayName}/{sp.Name} ERR {item.Error} ({item.Ms}ms)");
				}
				report.Items.Add(item);
			}
		}

		var root = TtsModelScanner.ResolveRoot();
		var cfgPath = Path.Combine(root, "tts_config.json");
		report.ConfigPath = cfgPath;
		if (writeConfig) {
			try {
				writeconfig(cfgPath, models, report.Items);
				report.WroteConfig = true;
				L($"已写回: {cfgPath}");
			}
			catch (Exception ex) {
				L($"写配置失败: {ex.Message}");
			}
		}
		L($"完成: ok={report.OkCount} fail={report.FailCount}");
		return report;
	}

	static TtsComputeMode parseMode(string device) {
		var d = (device ?? "auto").Trim().ToLowerInvariant();
		return d switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			"cpu" => TtsComputeMode.Cpu,
			_ => TtsComputeMode.Auto,
		};
	}

	/// <summary>配置键：默认/空 → speaker{id}；保留 suyingxue 等具名。</summary>
	static string configspeakerkey(SpeakerProbe it) {
		var k = it.SpeakerKey ?? "";
		if (string.IsNullOrEmpty(k) || k == "默认"
			|| k.StartsWith("DataBaker", StringComparison.OrdinalIgnoreCase))
			return "speaker" + it.Sid;
		return k;
	}

	/// <summary>合并探测结果到 tts_config.json（保留 volume/name/lang 等）。</summary>
	static void writeconfig(string path, List<TtsModelInfo> models, List<SpeakerProbe> items) {
		JsonObject root;
		if (File.Exists(path)) {
			var text = File.ReadAllText(path, Encoding.UTF8);
			root = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
		}
		else {
			root = new JsonObject();
		}

		// 按模型聚合探测结果
		var byModel = items
			.Where(i => string.IsNullOrEmpty(i.Error) && !string.IsNullOrEmpty(i.Gender))
			.GroupBy(i => i.Model, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

		foreach (var m in models) {
			if (!byModel.TryGetValue(m.DisplayName, out var list) || list.Count == 0)
				continue;

			if (root[m.DisplayName] is not JsonObject entry) {
				entry = new JsonObject();
				root[m.DisplayName] = entry;
			}

			// volume / lang 从模型回填（若配置缺）
			if (entry["volume"] == null && m.Volume > 0)
				entry["volume"] = m.Volume;
			if (entry["lang"] == null && !string.IsNullOrEmpty(m.Lang))
				entry["lang"] = m.Lang;

			if (entry["speakers"] is not JsonObject spkObj) {
				spkObj = new JsonObject();
				entry["speakers"] = spkObj;
			}

			// 单人模型：写模型级 gender
			if (list.Count == 1 && m.Speakers.Count <= 1) {
				entry["gender"] = list[0].Gender;
			}

			foreach (var it in list) {
				var key = configspeakerkey(it);
				JsonObject spEntry;
				if (spkObj[key] is JsonObject existing) {
					spEntry = existing;
				}
				else if (spkObj[key] is JsonValue jv && jv.TryGetValue<string>(out var oldName)) {
					// 旧字符串格式 → 对象
					spEntry = new JsonObject { ["name"] = oldName };
					spkObj[key] = spEntry;
				}
				else {
					spEntry = new JsonObject();
					spkObj[key] = spEntry;
				}

				// 中文名：优先已有 name，否则探测前的 ChineseName
				if (spEntry["name"] == null && !string.IsNullOrEmpty(it.ChineseName))
					spEntry["name"] = it.ChineseName;
				spEntry["gender"] = it.Gender;
				// 诊断：基频保留一位小数，便于复查
				spEntry["f0"] = Math.Round(it.F0, 1);
				if (spEntry["lang"] == null) {
					var sp = m.Speakers.FirstOrDefault(s => s.Id == it.Sid || s.Name == it.SpeakerKey);
					if (sp != null && !string.IsNullOrEmpty(sp.Lang))
						spEntry["lang"] = sp.Lang;
					else if (!string.IsNullOrEmpty(m.Lang)) {
						var parts = m.Lang.Split(',');
						if (parts.Length > 0)
							spEntry["lang"] = parts[0].Trim();
					}
				}
			}

			// 去掉「默认」等临时键（已合并到 speakerN）
			var drop = spkObj
				.Select(kv => kv.Key)
				.Where(k => k == "默认" || k.StartsWith("DataBaker", StringComparison.OrdinalIgnoreCase))
				.ToList();
			foreach (var k in drop)
				spkObj.Remove(k);
		}

		var opts = new JsonSerializerOptions(JsonSerializerOptions.Default) {
			WriteIndented = true,
			Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		};
		var json = root.ToJsonString(opts);
		// 统一换行
		json = json.Replace("\r\n", "\n");
		if (!json.EndsWith("\n")) json += "\n";
		File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}
}
