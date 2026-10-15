using System.Text;
using System.Text.Json.Nodes;

namespace ScreenKit;

sealed partial class HttpOcrServer {
	static int chromePackBusy;

	void handlechromepacks(SockCtx ctx) {
		if (!readbody(ctx, out var jo)) return;
		if (Interlocked.CompareExchange(ref chromePackBusy, 1, 0) != 0) {
			writejson(ctx, 200, err(809, "正在处理 Chrome 模型"));
			return;
		}
		var log = new StringBuilder();
		try {
			var probe = false;
			if (ispost(ctx.Request)) {
				var op = (field(ctx, jo, "op") ?? "install").Trim().ToLowerInvariant();
				var install = op is not "remove" and not "uninstall" and not "delete";
				var items = packitems(jo);
				if (items.Count == 0) {
					writejson(ctx, 200, err(802, "没有要处理的模型"));
					return;
				}
				ChromePack.Apply(items, install, HttpProxy.Mode, HttpProxy.Addr,
					line => { if (!string.IsNullOrWhiteSpace(line)) log.AppendLine(line); },
					null, null, null, CancellationToken.None);
			}
			else {
				var q = query(ctx.Request, "probe") ?? "";
				probe = q == "1" || q.Equals("true", StringComparison.OrdinalIgnoreCase);
			}
			var list = ChromePack.Catalog(probe, HttpProxy.Mode, HttpProxy.Addr,
				line => { if (!string.IsNullOrWhiteSpace(line)) log.AppendLine(line); },
				CancellationToken.None);
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["probe"] = probe,
					["log"] = log.ToString().Trim(),
					["items"] = packjson(list, probe),
				},
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(924, ex.Message));
		}
		finally {
			Interlocked.Exchange(ref chromePackBusy, 0);
		}
	}

	static List<ChromePackItem> packitems(JsonObject jo) {
		var items = new List<ChromePackItem>();
		if (jo?["ids"] is not JsonArray arr) return items;
		foreach (var n in arr) {
			string id;
			try { id = n?.GetValue<string>() ?? ""; }
			catch { continue; }
			id = id.Trim();
			if (id.StartsWith("tr:", StringComparison.OrdinalIgnoreCase)) {
				var pair = id.Substring(3).Trim();
				if (pair.Length == 0) continue;
				items.Add(new ChromePackItem { Id = "tr:" + pair, Kind = "tr", Pair = pair, Title = pair });
			}
			else if (id.StartsWith("tts:", StringComparison.OrdinalIgnoreCase)) {
				var lang = id.Substring(4).Trim();
				if (lang.Length == 0) continue;
				items.Add(new ChromePackItem { Id = "tts:" + lang, Kind = "tts", Lang = lang, Title = lang });
			}
		}
		return items;
	}

	static JsonArray packjson(List<ChromePackItem> list, bool probe) {
		var arr = new JsonArray();
		if (list == null) return arr;
		foreach (var it in list) {
			if (it == null) continue;
			var langs = new JsonArray();
			if (it.Langs != null)
				foreach (var c in it.Langs)
					if (!string.IsNullOrEmpty(c)) langs.Add(c);
			arr.Add(new JsonObject {
				["id"] = it.Id ?? "",
				["kind"] = it.Kind ?? "",
				["title"] = it.Title ?? "",
				["detail"] = it.Detail ?? "",
				["size"] = it.Size,
				["sizeText"] = it.SizeText ?? "",
				["state"] = packstate(it, probe),
				["langs"] = langs,
			});
		}
		return arr;
	}

	static string packstate(ChromePackItem it, bool probe) {
		if (!probe && it.Kind == "tts") {
			var known = ChromePack.VoiceInstalled(it.Lang);
			if (known == null) return "unknown";
			return known == true ? "installed" : "missing";
		}
		if (it.State == FeatureInstallState.Installed) return "installed";
		if (it.State == FeatureInstallState.Partial) return "partial";
		return "missing";
	}
}
