using System.Text;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>网页词典。查询、详情和导出与词典窗口用同一套 dict2.db。</summary>
static class DictWeb {
	public static string DbPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict2.db");

	public static bool Ensure() {
		if (DictDb.Ready) return true;
		return DictDb.Init(DbPath);
	}

	public static int Limit(OcrOptions o) {
		var n = 1000;
		if (o != null && o.DictLimit > 0) n = o.DictLimit;
		if (n < 1) n = 1;
		if (n > DictDb.SEARCH_MAX) n = DictDb.SEARCH_MAX;
		return n;
	}

	public static string NormDict(string dict) {
		dict = (dict ?? "").Trim().ToLowerInvariant();
		if (dict == "zh" || dict == "en" || dict == "ja" || dict == "ko") return dict;
		return "";
	}

	public static string NormLang(string lang) {
		lang = (lang ?? "").Trim().ToLowerInvariant();
		if (lang == "zh" || lang == "en" || lang == "ja" || lang == "ko") return lang;
		return "en";
	}

	public static List<DictHit> Hits(OcrOptions o, string q, string dict, int limit) {
		if (!Ensure()) return null;
		if (limit < 1) limit = Limit(o);
		return DictDb.Search(q ?? "", NormDict(dict), limit);
	}

	public static JsonObject HitNode(DictHit h) {
		var word = h.Headword.Length > 0 ? h.Headword : h.Matched;
		var shown = h.Dict == "ja" ? DictDb.JaForms(word, h.Kanji) : word;
		if (string.IsNullOrEmpty(shown)) shown = word ?? "";
		var code = h.Dict == "zh" || h.Dict == "ja" || h.Dict == "ko" || h.Dict == "en" ? h.Dict : "";
		var title = code.Length > 0 ? "[" + code + "] " + shown : shown;
		var speak = h.Dict == "ja" ? DictDb.JaSpeak(word, h.Kanji, h.Reading) : DictWindow.SpeakHead(word, h.Dict);
		return new JsonObject {
			["id"] = h.Id,
			["dict"] = h.Dict ?? "",
			["word"] = word ?? "",
			["title"] = title,
			["preview"] = h.Preview ?? "",
			["via"] = h.Via ?? "",
			["speak"] = speak ?? "",
		};
	}

	public static JsonObject EntryNode(DictEntry e) {
		if (e == null) return null;
		var word = e.Word.Length > 0 ? e.Word : e.Headword;
		var usage = new JsonArray();
		foreach (var u in e.Usage) {
			if (string.IsNullOrEmpty(u) || u == e.Pos) continue;
			usage.Add(u);
		}
		var see = new JsonArray();
		foreach (var s in e.See)
			if (!string.IsNullOrEmpty(s)) see.Add(s);
		var conj = new JsonArray();
		foreach (var c in e.Conjugations)
			if (!string.IsNullOrEmpty(c)) conj.Add(c);
		var senses = new JsonArray();
		foreach (var s in e.Senses) senses.Add(sense(s));
		var speak = e.Dict == "ja" ? DictDb.JaSpeak(word, e.Kanji, e.Pron) : DictWindow.SpeakHead(word, e.Dict);
		return new JsonObject {
			["id"] = e.Id,
			["dict"] = e.Dict ?? "",
			["word"] = word ?? "",
			["kanji"] = e.Kanji ?? "",
			["pron"] = e.Pron ?? "",
			["reading"] = e.Reading ?? "",
			["pos"] = e.Pos ?? "",
			["extra"] = e.Extra ?? "",
			["etymology"] = e.Etymology ?? "",
			["usage"] = usage,
			["see"] = see,
			["conjugations"] = conj,
			["senses"] = senses,
			["speak"] = speak ?? "",
		};
	}

	static JsonObject sense(DictSense s) {
		var phrases = new JsonArray();
		foreach (var ph in s.Phrases) {
			if (ph == null) continue;
			if (ph.Text.Length == 0 && ph.Zh.Length == 0) continue;
			phrases.Add(new JsonObject { ["text"] = ph.Text ?? "", ["zh"] = ph.Zh ?? "" });
		}
		var sentences = new JsonArray();
		foreach (var ex in s.Sentences) {
			if (ex == null) continue;
			if (ex.Text.Length == 0 && ex.Zh.Length == 0) continue;
			sentences.Add(new JsonObject { ["text"] = ex.Text ?? "", ["zh"] = ex.Zh ?? "" });
		}
		return new JsonObject {
			["pos"] = s.Pos ?? "",
			["ko"] = s.Ko ?? "",
			["zh"] = s.Zh ?? "",
			["zhDef"] = s.ZhDef ?? "",
			["en"] = s.En ?? "",
			["enDef"] = s.EnDef ?? "",
			["ja"] = s.Ja ?? "",
			["jaDef"] = s.JaDef ?? "",
			["idiom"] = s.Idiom ?? "",
			["sub"] = s.Sub,
			["phrases"] = phrases,
			["sentences"] = sentences,
		};
	}

	public static string SpeakFile(OcrOptions o, string lang, string text) {
		lang = NormLang(lang);
		var pref = DictTts.For(o, lang);
		return DictTts.EnsureWav(pref, lang, text).GetAwaiter().GetResult();
	}

	/// <summary>写出这次搜索的全部词条。返回条数。kind 为 txt、csv、html、md。</summary>
	public static int Export(Stream dest, string kind, string q, string dict) {
		if (!Ensure()) return -1;
		kind = kind == "csv" || kind == "html" || kind == "md" ? kind : "txt";
		var ids = DictDb.SearchIds(q ?? "", NormDict(dict));
		var table = kind == "html" || kind == "md";
		var enc = new UTF8Encoding(kind == "csv");
		using var w = new StreamWriter(dest, enc, 1024, leaveOpen: true);
		if (kind == "html") {
			w.Write("<!DOCTYPE html>\r\n<html>\r\n<head><meta charset=\"utf-8\"><title>");
			w.Write(DictWindow.ExportHtml((q ?? "").Trim()));
			w.Write("</title>\r\n");
			w.Write(DictWindow.ExportCss);
			w.Write("</head>\r\n<body>\r\n");
		}
		else if (kind == "md") w.Write(DictWindow.ExportCss);
		else if (kind == "csv") w.Write("dict,headword,detail\r\n");
		if (table) {
			w.Write("<table>\r\n<thead><tr><th class=\"w\">");
			w.Write(DictWindow.ExportHtml(Loc.T("dict.export.col.word")));
			w.Write("</th><th class=\"d\">");
			w.Write(DictWindow.ExportHtml(Loc.T("dict.export.col.detail")));
			w.Write("</th></tr></thead>\r\n<tbody>\r\n");
		}
		var n = 0;
		foreach (var id in ids) {
			var e = DictDb.Get(id);
			if (e == null) continue;
			var text = DictWindow.ExportPlain(e);
			var word = e.Word.Length > 0 ? e.Word : e.Headword;
			if (kind == "txt") {
				if (n > 0) w.Write("\r\n\r\n");
				w.Write(text);
			}
			else if (kind == "csv") {
				w.Write(DictWindow.ExportCsv(e.Dict));
				w.Write(',');
				w.Write(DictWindow.ExportCsv(word));
				w.Write(',');
				w.Write(DictWindow.ExportCsv(text));
				w.Write("\r\n");
			}
			else {
				w.Write("<tr><td class=\"w\">");
				w.Write(DictWindow.ExportHtml(word));
				w.Write("</td><td class=\"d\">");
				w.Write(DictWindow.ExportHtml(text));
				w.Write("</td></tr>\r\n");
			}
			n++;
		}
		if (table) w.Write("</tbody></table>\r\n");
		if (kind == "html") w.Write("</body>\r\n</html>\r\n");
		w.Flush();
		return n;
	}

	public static string FileName(string q, string kind) {
		var sb = new StringBuilder();
		foreach (var c in (q ?? "").Trim()) {
			if (c < ' ' || "\\/:*?\"<>|".IndexOf(c) >= 0) continue;
			sb.Append(c);
			if (sb.Length >= 40) break;
		}
		var ext = kind == "csv" || kind == "html" || kind == "md" ? kind : "txt";
		return (sb.Length == 0 ? "dict" : sb.ToString()) + "." + ext;
	}
}
