using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>HTTP 页模板与 GET /api 共用的一条接口。</summary>
sealed class HttpApiItem {
	public string Title { get; set; }
	public string Method { get; set; }
	public string Path { get; set; }
	public string Body { get; set; }
	public string Doc { get; set; }
	public string Gate { get; set; }
	public string Accept { get; set; }
	public bool Tab { get; set; }
	public string Aliases { get; set; }
}

/// <summary>接口目录。说明文字用 lang/ 里已有的 http.doc.*，不另写一套。</summary>
static class HttpApiCatalog {
	public static HttpApiItem[] Items { get; } = build();

	public static string LangOf(string raw) {
		if (string.IsNullOrWhiteSpace(raw)) return Loc.Lang;
		return Loc.Normalize(raw);
	}

	public static JsonArray Langs() {
		var a = new JsonArray();
		foreach (var one in Loc.Languages)
			a.Add(new JsonObject { ["code"] = one.Code, ["name"] = one.Name });
		return a;
	}

	public static JsonArray Apis(OcrOptions o, string lang) {
		var a = new JsonArray();
		foreach (var it in Items) {
			if (!open(it.Gate, o)) continue;
			var doc = Loc.TFor(lang, it.Doc);
			splitdoc(doc, out var summary, out var note, out var parms);
			var method = string.IsNullOrEmpty(it.Accept) ? it.Method : it.Accept;
			var one = new JsonObject {
				["method"] = method ?? "",
				["path"] = pathof(it.Path),
				["example"] = it.Path ?? "",
				["title"] = it.Title ?? "",
				["summary"] = summary,
				["doc"] = doc ?? "",
				["params"] = parms,
			};
			if (!string.IsNullOrEmpty(note)) one["note"] = note;
			if (!string.IsNullOrEmpty(it.Body)) one["body"] = it.Body;
			var aliases = aliasof(it.Aliases);
			if (aliases != null) one["aliases"] = aliases;
			a.Add(one);
		}
		return a;
	}

	static HttpApiItem[] build() {
		var list = new List<HttpApiItem>();
		add(list, "GET /api/status", "GET", "/api/status", "", "http.doc.status", aliases: "/api/health");
		add(list, "GET /api/toast", "GET", "/api/toast?text=你好&ms=1900", "", "http.doc.toast");
		add(list, "POST /api/toast", "POST", "/api/toast", "{\n  \"text\": \"你好\",\n  \"ms\": 1900\n}", "http.doc.toast");
		add(list, "GET /api/zhconv", "GET", "/api/zhconv?text=软件&to=trad", "", "http.doc.zhconv");
		add(list, "POST /api/zhconv", "POST", "/api/zhconv", "{\n  \"text\": \"软件\",\n  \"to\": \"trad\"\n}", "http.doc.zhconv");
		add(list, "GET /api/calendar", "GET", "/api/calendar?date=2024-02-10&cal=lunar", "", "http.doc.calendar");
		add(list, "POST /api/calendar", "POST", "/api/calendar", "{\n  \"date\": \"2024-02-10\",\n  \"cal\": \"lunar\"\n}", "http.doc.calendar");
		add(list, "GET /api/jpyomi", "GET", "/api/jpyomi?text=東京は晴れです", "", "http.doc.jpyomi");
		add(list, "POST /api/jpyomi", "POST", "/api/jpyomi", "{\n  \"text\": \"東京は晴れです\",\n  \"mono\": false\n}", "http.doc.jpyomi");
		add(list, "POST /api/text", "POST", "/api/text", "{\n  \"text\": \"hi\",\n  \"op\": \"b64enc\"\n}", "http.doc.text", accept: "GET/POST");
		add(list, "POST /api/qrmake", "POST", "/api/qrmake", "{\n  \"text\": \"hello\",\n  \"format\": \"qr\",\n  \"encoding\": \"utf8\"\n}", "http.doc.qrmake", accept: "GET/POST");
		add(list, "POST /api/qrscan", "POST", "/api/qrscan", "{\n  \"base64\": \"\"\n}", "http.doc.qrscan");
		add(list, "POST /api/cast/stop", "POST", "/api/cast/stop", "", "http.doc.caststop", accept: "GET/POST");
		add(list, "GET /api", "GET", "/api", "", "http.doc.api");
		add(list, "GET /api/ocr/models", "GET", "/api/ocr/models", "", "http.doc.ocrmodels", "ocr");
		add(list, "GET /api/ocr/get_options", "GET", "/api/ocr/get_options", "", "http.doc.ocropt", "ocr");
		add(list, "POST /api/ocr", "POST", "/api/ocr", "{\n  \"base64\": \"\",\n  \"options\": {}\n}", "http.doc.ocr", "ocr");
		add(list, "POST /api/qr", "POST", "/api/qr", "{\n  \"base64\": \"\",\n  \"format\": \"dict\"\n}", "http.doc.qr", "ocr", aliases: "/api/barcode,/api/barcodes");
		add(list, "GET /api/asr/models", "GET", "/api/asr/models", "", "http.doc.asrmodels", "asr");
		add(list, "POST /api/asr", "POST", "/api/asr", "{\n  \"path\": \"\",\n  \"lang\": \"auto\"\n}", "http.doc.asr", "asr");
		add(list, "GET /api/tts/engines", "GET", "/api/tts/engines", "", "http.doc.ttsengines", "tts");
		add(list, "GET /api/tts/models", "GET", "/api/tts/models", "", "http.doc.ttsmodels", "tts");
		add(list, "GET /api/tts/models?engine", "GET", "/api/tts/models?engine=sapi", "", "http.doc.ttsmodels", "tts");
		add(list, "POST /api/tts", "POST", "/api/tts", "{\n  \"text\": \"你好\"\n}", "http.doc.tts", "tts");
		add(list, "POST /api/tts (SAPI)", "POST", "/api/tts", "{\n  \"text\": \"你好\",\n  \"engine\": \"sapi\"\n}", "http.doc.tts.sapi", "tts");
		add(list, "POST /api/tts (Windows)", "POST", "/api/tts", "{\n  \"text\": \"你好\",\n  \"engine\": \"winrt\"\n}", "http.doc.tts.win", "tts");
		add(list, "POST /api/tts (Edge Online)", "POST", "/api/tts", "{\n  \"text\": \"안녕하세요\",\n  \"engine\": \"edge\",\n  \"voice\": \"edge:ko-KR-SunHiNeural\"\n}", "http.doc.tts.edge", "tts");
		add(list, "POST /api/itn", "POST", "/api/itn", "{\n  \"text\": \"二零二四年一月一日\"\n}", "http.doc.itn", "asr");
		add(list, "POST /api/translate", "POST", "/api/translate", "{\n  \"items\": [\"你好\"],\n  \"src\": \"zh\",\n  \"dst\": \"en\"\n}", "http.doc.translate", "translate", aliases: "/api/translate/batch");
		add(list, "POST /api/chat", "POST", "/api/chat", "{\n  \"text\": \"你好\",\n  \"tts\": false,\n  \"agent\": false\n}", "http.doc.chat", "chat");
		add(list, "POST /api/chat + tts", "POST", "/api/chat", "{\n  \"text\": \"用一句话介绍你自己\",\n  \"tts\": true,\n  \"engine\": \"sapi\"\n}", "http.doc.chat.tts", "chat");
		add(list, "GET /api/face/models", "GET", "/api/face/models", "", "http.doc.facemodels", "face");
		add(list, "POST /api/face", "POST", "/api/face", "{\n  \"base64\": \"\"\n}", "http.doc.face", "face");
		return list.ToArray();
	}

	static void add(List<HttpApiItem> list, string title, string method, string path, string body, string doc, string gate = "", string accept = "", bool tab = true, string aliases = "") {
		list.Add(new HttpApiItem {
			Title = title,
			Method = method,
			Path = path,
			Body = body,
			Doc = doc,
			Gate = gate ?? "",
			Accept = accept ?? "",
			Tab = tab,
			Aliases = aliases ?? "",
		});
	}

	static bool open(string gate, OcrOptions o) {
		if (o == null || string.IsNullOrEmpty(gate)) return true;
		return gate switch {
			"ocr" => o.HttpOcr,
			"asr" => o.HttpAsr,
			"tts" => o.HttpTts,
			"translate" => o.HttpTranslate,
			"chat" => o.HttpChat,
			"face" => o.HttpFace,
			_ => true,
		};
	}

	static string pathof(string path) {
		if (string.IsNullOrEmpty(path)) return "";
		var i = path.IndexOf('?');
		return i < 0 ? path : path.Substring(0, i);
	}

	static JsonArray aliasof(string raw) {
		if (string.IsNullOrWhiteSpace(raw)) return null;
		var a = new JsonArray();
		foreach (var part in raw.Split(',')) {
			var s = part.Trim();
			if (s.Length > 0) a.Add(s);
		}
		return a.Count == 0 ? null : a;
	}

	static void splitdoc(string doc, out string summary, out string note, out JsonArray parms) {
		summary = "";
		note = "";
		parms = new JsonArray();
		if (string.IsNullOrEmpty(doc)) return;
		var head = new List<string>();
		var tail = new List<string>();
		var seen = false;
		foreach (var raw in doc.Replace("\r\n", "\n").Split('\n')) {
			var line = raw.Trim();
			if (line.Length == 0) continue;
			if (splitparam(line, out var name, out var desc)) {
				seen = true;
				parms.Add(new JsonObject { ["name"] = name, ["desc"] = desc });
			}
			else if (!seen) head.Add(line);
			else tail.Add(line);
		}
		summary = string.Join("\n", head);
		note = string.Join("\n", tail);
		if (summary.Length == 0) summary = doc.Trim();
	}

	static bool splitparam(string line, out string name, out string desc) {
		name = "";
		desc = "";
		var cut = line.IndexOf('：');
		if (cut < 0) {
			cut = line.IndexOf(':');
			if (cut >= 0 && cut + 1 < line.Length && line[cut + 1] == '/') return false;
		}
		if (cut <= 0) return false;
		name = line.Substring(0, cut).Trim();
		desc = line.Substring(cut + 1).Trim();
		if (name.Length == 0 || name.Length > 60 || desc.Length == 0) return false;
		if (name.IndexOf('。') >= 0 || name.IndexOf('，') >= 0) return false;
		if (name.IndexOf('.') >= 0 && name.IndexOf(' ') >= 0) return false;
		var spaces = 0;
		for (var i = 0; i < name.Length; i++) {
			var c = name[i];
			if (c == ' ') {
				spaces++;
				continue;
			}
			if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '/') continue;
			return false;
		}
		return spaces <= 6;
	}
}
