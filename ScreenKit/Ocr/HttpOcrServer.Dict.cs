using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ScreenKit;

sealed partial class HttpOcrServer {
	void handledictpage(SockCtx ctx, string path) {
		string name;
		string mime;
		if (path == "/sk/dict.css") {
			name = "dict.css";
			mime = "text/css; charset=utf-8";
		}
		else if (path == "/sk/dict.js") {
			name = "dict.js";
			mime = "application/javascript; charset=utf-8";
		}
		else {
			name = "dict.html";
			mime = "text/html; charset=utf-8";
		}
		var html = name == "dict.html";
		sendwebasset(ctx, name, mime, html);
	}

	bool dicton(SockCtx ctx) {
		var o = getOpts?.Invoke() ?? new OcrOptions();
		return apiok(ctx, o.ModDict);
	}

	void handledictsearch(SockCtx ctx) {
		if (!dicton(ctx)) return;
		if (!isget(ctx.Request)) {
			writejson(ctx, 405, err(805, "dict 仅支持 GET"));
			return;
		}
		var q = query(ctx.Request, "q") ?? "";
		var dict = DictWeb.NormDict(query(ctx.Request, "dict"));
		var limit = 0;
		var raw = query(ctx.Request, "limit");
		if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var n)) limit = n;
		var o = getOpts?.Invoke();
		if (limit < 1) limit = DictWeb.Limit(o);
		try {
			if (!DictWeb.Ensure()) {
				writejson(ctx, 200, dicterr());
				return;
			}
			var t0 = Environment.TickCount;
			var hits = DictDb.Search(q, dict, limit);
			var ms = Environment.TickCount - t0;
			if (ms < 0) ms = 0;
			var arr = new JsonArray();
			foreach (var h in hits) {
				if (h.Id == 0) continue;
				arr.Add(DictWeb.HitNode(h));
			}
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["ms"] = ms,
					["hits"] = arr,
				},
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(500, ex.Message));
		}
	}

	void handledictentry(SockCtx ctx) {
		if (!dicton(ctx)) return;
		if (!isget(ctx.Request)) {
			writejson(ctx, 405, err(805, "dict/entry 仅支持 GET"));
			return;
		}
		var raw = query(ctx.Request, "id") ?? "";
		if (!long.TryParse(raw, out var id) || id <= 0) {
			writejson(ctx, 200, err(802, "请提供词条 id"));
			return;
		}
		try {
			if (!DictWeb.Ensure()) {
				writejson(ctx, 200, dicterr());
				return;
			}
			var e = DictDb.Get(id);
			if (e == null) {
				writejson(ctx, 200, err(802, "没有这条词"));
				return;
			}
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = DictWeb.EntryNode(e),
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(500, ex.Message));
		}
	}

	void handledictspeak(SockCtx ctx) {
		if (!dicton(ctx)) return;
		if (!isget(ctx.Request)) {
			writejson(ctx, 405, err(805, "dict/speak 仅支持 GET"));
			return;
		}
		var text = query(ctx.Request, "text") ?? "";
		var lang = DictWeb.NormLang(query(ctx.Request, "lang"));
		if (string.IsNullOrWhiteSpace(text)) {
			writejson(ctx, 200, new JsonObject { ["code"] = 802, ["error"] = "noword", ["data"] = "noword" });
			return;
		}
		// Chrome 发音是这台机器在说话，局域网请求不接；其它引擎照常返回 WAV。
		if (!fromlocal(ctx) && DictWeb.SpeakUsesChrome(getOpts?.Invoke(), lang)) {
			writejson(ctx, 403, err(403, "Chrome 语音只允许本机调用"));
			return;
		}
		try {
			var path = DictWeb.SpeakFile(getOpts?.Invoke(), lang, text);
			var bytes = File.ReadAllBytes(path);
			var res = ctx.Response;
			res.StatusCode = 200;
			res.ContentType = "audio/wav";
			res.ContentLength64 = bytes.Length;
			res.Headers["Cache-Control"] = "no-store";
			res.Headers["Access-Control-Allow-Origin"] = "*";
			res.OutputStream.Write(bytes, 0, bytes.Length);
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
		catch (Exception ex) {
			var key = ex.Message == "novoice" ? "novoice" : "";
			if (key.Length > 0)
				writejson(ctx, 200, new JsonObject { ["code"] = 802, ["error"] = key, ["data"] = key });
			else
				writejson(ctx, 200, err(802, ex.Message));
		}
	}

	void handledictexport(SockCtx ctx) {
		if (!dicton(ctx)) return;
		if (!isget(ctx.Request)) {
			writejson(ctx, 405, err(805, "dict/export 仅支持 GET"));
			return;
		}
		var q = query(ctx.Request, "q") ?? "";
		if (string.IsNullOrWhiteSpace(q)) {
			writejson(ctx, 200, err(802, Loc.T("dict.export.empty")));
			return;
		}
		var dict = DictWeb.NormDict(query(ctx.Request, "dict"));
		var kind = (query(ctx.Request, "fmt") ?? "txt").Trim().ToLowerInvariant();
		if (kind != "csv" && kind != "html" && kind != "md") kind = "txt";
		try {
			if (!DictWeb.Ensure()) {
				writejson(ctx, 200, dicterr());
				return;
			}
			var ms = new MemoryStream();
			var n = DictWeb.Export(ms, kind, q, dict);
			var bytes = ms.ToArray();
			var mime = kind == "html" ? "text/html; charset=utf-8"
				: kind == "csv" ? "text/csv; charset=utf-8"
				: "text/plain; charset=utf-8";
			var name = DictWeb.FileName(q, kind);
			var res = ctx.Response;
			res.StatusCode = 200;
			res.ContentType = mime;
			res.ContentLength64 = bytes.Length;
			res.Headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(name);
			res.Headers["X-Dict-Count"] = n.ToString(CultureInfo.InvariantCulture);
			res.Headers["Cache-Control"] = "no-store";
			res.Headers["Access-Control-Allow-Origin"] = "*";
			res.Headers["Access-Control-Expose-Headers"] = "X-Dict-Count";
			res.OutputStream.Write(bytes, 0, bytes.Length);
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(500, ex.Message));
		}
	}

	static JsonObject dicterr() {
		var key = DictDb.Error == "missing" ? "missing" : "fail";
		var msg = key == "missing" ? Loc.T("dict.missing") : DictDb.Error;
		return new JsonObject { ["code"] = 802, ["error"] = key, ["data"] = msg ?? "" };
	}
}
