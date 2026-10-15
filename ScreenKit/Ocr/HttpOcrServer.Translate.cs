using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>HTTP 翻译：GET /api/translate/langs · POST /api/translate · /api/translate/batch。</summary>
sealed partial class HttpOcrServer {
	TranslateEngine httpTr;

	void handletranslatelangs(SockCtx ctx) {
		var o = getOpts?.Invoke() ?? new OcrOptions();
		var chrome = o.ChromeTranslate();
		var llm = AsrLlmClient.IsEndpointReady(o.SelectedTranslateLlm());
		List<TranslateModelInfo> models;
		try { models = TranslateModelScanner.Scan(); }
		catch { models = new List<TranslateModelInfo>(); }
		var wide = llm || chrome;
		var srcs = translatesrcs(models, wide);
		var srcArr = new JsonArray();
		var dst = new JsonObject();
		foreach (var s in srcs) {
			srcArr.Add(s);
			var arr = new JsonArray();
			foreach (var t in translatedsts(models, wide, s)) arr.Add(t);
			dst[s] = arr;
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["llm"] = llm,
				["chrome"] = chrome,
				["src"] = srcArr,
				["dst"] = dst,
			},
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	void handletranslate(SockCtx ctx) {
		JsonObject jo;
		try { jo = readjsonbody(ctx.Request); }
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}
		var o = getOpts?.Invoke() ?? new OcrOptions();
		if (wantchrome(jo, o)) {
			translatechrome(ctx, o, jo);
			return;
		}
		var wantLlm = str(jo, "llm") ?? str(jo, "model");
		var ep = resolveep(o, wantLlm);

		var items = readitems(jo);
		if (items == null || items.Count == 0) {
			writejson(ctx, 200, err(941, "请提供 items 字符串数组，或 text"));
			return;
		}

		var src = TrLang.Normalize(str(jo, "src") ?? str(jo, "from") ?? "");
		var dst = TrLang.Normalize(str(jo, "dst") ?? str(jo, "to") ?? "");
		var dir = str(jo, "dir") ?? str(jo, "tr_dir") ?? "";
		if ((src.Length == 0 || dst.Length == 0) && TrLang.TryParsePair(dir, out var ds, out var dd)) {
			if (src.Length == 0) src = ds;
			if (dst.Length == 0) dst = dd;
		}
		if (src.Length == 0 || dst.Length == 0 || src == TrLang.Auto || dst == TrLang.Auto) {
			var probe = "";
			foreach (var t in items) {
				if (!string.IsNullOrWhiteSpace(t)) { probe = t; break; }
			}
			LangDetect.DetectZhEnPair(probe, out var asrc, out var adst);
			if (src.Length == 0 || src == TrLang.Auto) src = asrc;
			if (dst.Length == 0 || dst == TrLang.Auto) dst = adst;
		}
		if (!AsrLlmClient.IsEndpointReady(ep)) {
			translateonnx(ctx, o, items, src, dst);
			return;
		}

		var chunk = 0;
		if (jo["chunk"] != null && jo["chunk"].GetValueKind() == JsonValueKind.Number)
			chunk = jo["chunk"].GetValue<int>();
		else {
			var cs = str(jo, "chunk");
			if (!string.IsNullOrWhiteSpace(cs) && int.TryParse(cs, out var cv)) chunk = cv;
		}
		chunk = AsrLlmClient.NormBatchChunk(o, chunk);

		var t0 = Environment.TickCount;
		List<string> outs;
		try {
			outs = AsrLlmClient.TranslateBatch(o, items, src, dst, chunk, ep);
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(942, "翻译失败: " + ex.Message));
			return;
		}
		var ms = Math.Max(0, Environment.TickCount - t0);

		var arr = new JsonArray();
		var miss = 0;
		for (var i = 0; i < items.Count; i++) {
			var srcText = items[i] ?? "";
			var outText = i < outs.Count ? (outs[i] ?? "") : "";
			if (srcText.Trim().Length > 0 && outText.Trim().Length == 0) miss++;
			arr.Add(new JsonObject {
				["i"] = i + 1,
				["text"] = srcText,
				["out"] = outText,
			});
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["src"] = src,
				["dst"] = dst,
				["model"] = ep.Model ?? "",
				["llm"] = ep.DisplayName ?? "",
				["chunk"] = chunk,
				["count"] = items.Count,
				["miss"] = miss,
				["items"] = arr,
			},
			["time"] = ms,
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	static bool wantchrome(JsonObject jo, OcrOptions o) {
		var e = str(jo, "engine") ?? str(jo, "llm") ?? str(jo, "model") ?? "";
		if (e.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return true;
		return e.Length == 0 && o != null && o.ChromeTranslate();
	}

	void translatechrome(SockCtx ctx, OcrOptions o, JsonObject jo) {
		var items = readitems(jo);
		if (items == null || items.Count == 0) {
			writejson(ctx, 200, err(941, "请提供 items 字符串数组，或 text"));
			return;
		}
		var src = TrLang.Normalize(str(jo, "src") ?? str(jo, "from") ?? "");
		var dst = TrLang.Normalize(str(jo, "dst") ?? str(jo, "to") ?? "");
		var dir = str(jo, "dir") ?? str(jo, "tr_dir") ?? "";
		if ((src.Length == 0 || dst.Length == 0) && TrLang.TryParsePair(dir, out var ds, out var dd)) {
			if (src.Length == 0) src = ds;
			if (dst.Length == 0) dst = dd;
		}
		if (src.Length == 0 || dst.Length == 0 || src == TrLang.Auto || dst == TrLang.Auto) {
			var probe = "";
			foreach (var t in items) {
				if (!string.IsNullOrWhiteSpace(t)) { probe = t; break; }
			}
			LangDetect.DetectZhEnPair(probe, out var asrc, out var adst);
			if (src.Length == 0 || src == TrLang.Auto) src = asrc;
			if (dst.Length == 0 || dst == TrLang.Auto) dst = adst;
		}
		var t0 = Environment.TickCount;
		List<string> outs;
		try {
			outs = ChromeTr.TranslateMany(items, src, dst, o?.ChromeExe, o?.ChromeIdleSec ?? 60, CancellationToken.None);
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(942, "翻译失败: " + ex.Message));
			return;
		}
		var ms = Math.Max(0, Environment.TickCount - t0);
		var arr = new JsonArray();
		var miss = 0;
		for (var i = 0; i < items.Count; i++) {
			var srcText = items[i] ?? "";
			var outText = i < outs.Count ? (outs[i] ?? "") : "";
			if (srcText.Trim().Length > 0 && outText.Trim().Length == 0) miss++;
			arr.Add(new JsonObject {
				["i"] = i + 1,
				["text"] = srcText,
				["out"] = outText,
			});
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["src"] = src,
				["dst"] = dst,
				["model"] = "Chrome",
				["llm"] = "",
				["engine"] = "chrome",
				["count"] = items.Count,
				["miss"] = miss,
				["items"] = arr,
			},
			["time"] = ms,
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	static LlmEndpoint resolveep(OcrOptions o, string want) {
		if (o == null) return null;
		if (!string.IsNullOrWhiteSpace(want)) {
			var hit = o.FindLlm(want);
			if (hit != null) return hit;
		}
		return o.SelectedTranslateLlm() ?? o.SelectedLlm();
	}

	static List<string> readitems(JsonObject jo) {
		var list = new List<string>();
		var node = jo["items"] ?? jo["texts"] ?? jo["list"];
		if (node is JsonArray arr) {
			foreach (var x in arr) {
				if (x == null || x.GetValueKind() == JsonValueKind.Null) {
					list.Add("");
					continue;
				}
				if (x.GetValueKind() == JsonValueKind.String)
					list.Add(x.GetValue<string>() ?? "");
				else if (x is JsonObject xo)
					list.Add(str(xo, "text") ?? str(xo, "src") ?? "");
				else
					list.Add(x.ToString() ?? "");
			}
			return list;
		}
		var one = str(jo, "text");
		if (one != null) {
			list.Add(one);
			return list;
		}
		return list;
	}

	void translateonnx(SockCtx ctx, OcrOptions o, List<string> items, string src, string dst) {
		List<TranslateModelInfo> models;
		try { models = TranslateModelScanner.Scan(); }
		catch { models = new List<TranslateModelInfo>(); }
		var key = LangDetect.DirKey(src, dst);
		var model = models.FirstOrDefault(m => m.IsReady
			&& string.Equals(m.DirKey, key, StringComparison.OrdinalIgnoreCase));
		if (model == null) {
			writejson(ctx, 200, err(940, "未配置翻译 LLM，也没有 " + TrLang.Label(src) + "→" + TrLang.Label(dst) + " 的本地模型"));
			return;
		}
		var eng = svc?.TranslateEngine;
		if (eng == null) {
			if (httpTr == null) httpTr = new TranslateEngine();
			eng = httpTr;
		}
		var prefer = TranslateEngine.PreferFromMode(trcomputemode(o));
		var t0 = Environment.TickCount;
		List<string> outs;
		try {
			outs = new List<string>();
			if (!eng.EnsureLoaded(key, model.ModelDir, prefer))
				throw new InvalidOperationException(eng.LastError ?? "加载模型失败");
			foreach (var item in items)
				outs.Add(eng.Translate(key, item ?? "") ?? "");
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(942, "翻译失败: " + ex.Message));
			return;
		}
		var ms = Math.Max(0, Environment.TickCount - t0);
		var arr = new JsonArray();
		var miss = 0;
		for (var i = 0; i < items.Count; i++) {
			var srcText = items[i] ?? "";
			var outText = i < outs.Count ? (outs[i] ?? "") : "";
			if (srcText.Trim().Length > 0 && outText.Trim().Length == 0) miss++;
			arr.Add(new JsonObject {
				["i"] = i + 1,
				["text"] = srcText,
				["out"] = outText,
			});
		}
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["src"] = src,
				["dst"] = dst,
				["model"] = model.DisplayName ?? key,
				["llm"] = "",
				["engine"] = "onnx",
				["chunk"] = 1,
				["count"] = items.Count,
				["miss"] = miss,
				["items"] = arr,
			},
			["time"] = ms,
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	static List<string> translatesrcs(List<TranslateModelInfo> models, bool llm) {
		var srcs = new List<string> { TrLang.Auto };
		var more = models.Where(m => m.IsReady)
			.Select(m => m.SourceLang)
			.Where(s => !string.IsNullOrEmpty(s))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (llm) {
			foreach (var s in TrLang.LlmCodes)
				if (!more.Any(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)))
					more.Add(s);
		}
		more.Sort(TrLang.CompareLlm);
		srcs.AddRange(more);
		return srcs;
	}

	static List<string> translatedsts(List<TranslateModelInfo> models, bool llm, string src) {
		src = TrLang.Normalize(src);
		if (src == TrLang.Auto || src.Length == 0) return new List<string> { TrLang.Auto };
		var tgts = models.Where(m => m.IsReady
				&& string.Equals(m.SourceLang, src, StringComparison.OrdinalIgnoreCase))
			.Select(m => m.TargetLang)
			.Where(t => !string.IsNullOrEmpty(t))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (llm) {
			foreach (var t in TrLang.LlmCodes) {
				if (string.Equals(t, src, StringComparison.OrdinalIgnoreCase)) continue;
				if (!tgts.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
					tgts.Add(t);
			}
		}
		tgts.Sort(TrLang.CompareLlm);
		return tgts;
	}

	static TtsComputeMode trcomputemode(OcrOptions o) =>
		(o?.TranslateCompute ?? "Auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};

	bool idlehttptr(int limitMs) {
		if (httpTr == null) return false;
		return httpTr.IdleUnload(limitMs);
	}

	void disposehttptr() {
		try { httpTr?.Dispose(); } catch { }
		httpTr = null;
	}

	static string str(JsonObject jo, string key) {
		if (jo == null || jo[key] == null) return null;
		if (jo[key].GetValueKind() == JsonValueKind.String)
			return jo[key].GetValue<string>();
		return null;
	}
}
