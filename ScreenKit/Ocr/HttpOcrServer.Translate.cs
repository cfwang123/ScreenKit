using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>HTTP 翻译：POST /api/translate · /api/translate/batch。</summary>
sealed partial class HttpOcrServer {
	void handletranslate(HttpListenerContext ctx) {
		JsonObject jo;
		try { jo = readjsonbody(ctx.Request); }
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}
		var o = getOpts?.Invoke() ?? new OcrOptions();
		var wantLlm = str(jo, "llm") ?? str(jo, "model");
		var ep = resolveep(o, wantLlm);
		if (!AsrLlmClient.IsEndpointReady(ep)) {
			writejson(ctx, 200, err(940, "未配置翻译 LLM（参数设置 → LLM接口，并指定 translate_llm 或 llm）"));
			return;
		}

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

		var chunk = 8;
		if (jo["chunk"] != null && jo["chunk"].GetValueKind() == JsonValueKind.Number)
			chunk = jo["chunk"].GetValue<int>();
		else {
			var cs = str(jo, "chunk");
			if (!string.IsNullOrWhiteSpace(cs) && int.TryParse(cs, out var cv)) chunk = cv;
		}

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
				["chunk"] = Compat.Clamp(chunk <= 0 ? 8 : chunk, 1, 10),
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

	static string str(JsonObject jo, string key) {
		if (jo == null || jo[key] == null) return null;
		if (jo[key].GetValueKind() == JsonValueKind.String)
			return jo[key].GetValue<string>();
		return null;
	}
}
