using System.Text.Json.Nodes;
using System.Windows.Documents;

namespace ScreenKit;

/// <summary>
/// 词典词条的中间表示：先拼成紧凑 JSON（块/行内，类似 DOM），
/// WPF 词典页、网页词典和阅读器词典再各自按同一份结构画出来。
/// 「仅释义」等开关作为参数传进来，渲染端不再关心词条字段。
/// </summary>
static class DictDoc {
	// 整份文档就是行：每行 [缩进(0/1), 行后距, [行内块…]]；空行就是行内块为空的行。
	// 行内块：["t", 文本, 角色, 语言] / ["k", 文案键, 角色, 后缀, 语言] /
	//         ["a", 文本, 查询词, 语言] / ["v", 朗读文本, 语言]
	public static JsonObject Build(DictEntry e, bool only, string ui) {
		var blocks = new JsonArray();
		var doc = new JsonObject { ["lines"] = blocks };
		if (e == null) return doc;
		var entrylang = DictWindow.speaklang(e.Dict ?? "");
		var zharticle = e.Dict == "zh" && e.Extra.Length > 0;
		var forms = DictWindow.headforms(e);
		var head = new JsonArray();
		for (var i = 0; i < forms.Count; i++) {
			if (i == 0) head.Add(s(forms[i], "t", entrylang));
			else head.Add(s(", " + forms[i], "b", entrylang));
		}
		blocks.Add(p(0, 2, head));
		var reading = e.Pron.Length > 0 ? e.Pron : e.Reading;
		if (reading.Length > 0) blocks.Add(one(DictWindow.bracket(reading), "r", 1, entrylang));
		if (e.Pos.Length > 0) blocks.Add(one(e.Pos, "p", 1, DictWindow.TextLang(e.Pos, entrylang)));
		if (!zharticle) {
			var tags = new List<string>();
			foreach (var u in e.Usage) {
				if (u == null || u.Length == 0 || u == e.Pos) continue;
				tags.Add(u);
			}
			if (tags.Count > 0) {
				var joined = string.Join(" · ", tags);
				var r = new JsonArray { k("dict.lab.tags", "l") };
				r.Add(s(joined, "b", DictWindow.TextLang(joined, entrylang)));
				blocks.Add(p(0, 1, r));
			}
			if (e.Etymology.Length > 0) {
				blocks.Add(oneKey("dict.lab.etym", "l", 0));
				foreach (var line in DictWindow.splitlines(e.Etymology))
					blocks.Add(one(line, "b", 0, DictWindow.TextLang(line, entrylang)));
			}
			foreach (var c in e.Conjugations) {
				var r = new JsonArray { k("dict.lab.conj", "l") };
				var firstpart = true;
				foreach (var part in c.Split('　')) {
					if (!firstpart) r.Add(s("　", "b"));
					firstpart = false;
					r.Add(s(part, "b", DictWindow.TextLang(part, entrylang)));
				}
				blocks.Add(p(0, 0, r));
			}
			if (e.See.Count > 0) {
				var r = new JsonArray { k("dict.lab.see", "l", " ") };
				var first = true;
				foreach (var sw in e.See) {
					if (sw == null || sw.Length == 0) continue;
					if (!first) r.Add(s(" · ", "b"));
					first = false;
					r.Add(link(sw, sw, entrylang));
				}
				blocks.Add(p(0, 1, r));
			}
			senses(e, blocks, entrylang, only, ui);
		}
		if (e.Extra.Length > 0 && (zharticle || e.Dict != "zh")) {
			if (blocks.Count > 0) blocks.Add(gap(6));
			foreach (var line in DictWindow.splitlines(e.Extra))
				blocks.Add(one(line, "b", 0, zharticle ? "zh" : DictWindow.TextLang(line, entrylang)));
		}
		return doc;
	}

	static void senses(DictEntry e, JsonArray blocks, string entrylang, bool only, string ui) {
		var n = 1;
		string lastpos = null;
		var anySense = false;
		var linkphrase = e.Dict != "ko";
		var blockstyle = e.Dict == "ko" || e.Dict == "en";
		JsonArray curp = null;
		var idiomopen = false;
		foreach (var sn in e.Senses) {
			if (!sn.Sub) idiomopen = false;
			if (sn.Idiom.Length > 0 || !sn.Sub) curp = null;
			if (sn.Idiom.Length > 0) {
				if (e.Dict == "en") {
					if (!idiomopen) {
						if (anySense) blocks.Add(gap(4));
						blocks.Add(p(0, 0, new JsonArray { k("dict.lab.phrase", "l", "：") }));
						idiomopen = true;
					}
					blocks.Add(p(0, 0, new JsonArray {
						s("· ", "n"),
						link(sn.Idiom, DictWindow.firsttoken(sn.Idiom), entrylang),
					}));
				}
				else {
					if (anySense && !only) blocks.Add(gap(4));
					var r = new JsonArray { k("dict.lab.phrase", "l", " ") };
					if (linkphrase) r.Add(link(sn.Idiom, DictWindow.firsttoken(sn.Idiom), entrylang));
					else r.Add(s(sn.Idiom, "b", entrylang));
					blocks.Add(p(0, 0, r));
				}
			}
			else if (anySense && !sn.Sub && (!only || (e.Dict != "en" && e.Dict != "ja"))) blocks.Add(gap(4));
			anySense = true;
			if (sn.Pos.Length > 0 && sn.Pos != e.Pos && sn.Pos != lastpos) {
				blocks.Add(one("▶ " + sn.Pos, "p", 1, DictWindow.TextLang(sn.Pos, entrylang)));
				lastpos = sn.Pos;
				n = 1;
				curp = null;
			}
			var numbered = false;
			// 返回新建的段落；仅释义时把它记作 curp，后续译文并到同一段。
			JsonArray gloss(string tagkey, string lemma, string def, string lang) {
				if (lemma.Length == 0 && def.Length == 0) return null;
				var text = DictWindow.join2(lemma, def);
				if (only) {
					text = DictWindow.onelines(text);
					if (text.Length == 0) return null;
					if (curp != null) {
						curp.Add(s("; " + text, "b", lang));
						return null;
					}
				}
				var r = new JsonArray();
				var withnum = !numbered && !sn.Sub;
				if (withnum) {
					r.Add(s(n + ". ", "n"));
					numbered = true;
					n++;
				}
				if (tagkey != null) {
					r.Add(k(tagkey, "l", " ", lang));
				}
				r.Add(s(text, "b", lang));
				blocks.Add(p(blockstyle && !withnum ? 1 : 0, 0, r));
				if (only) curp = r;
				return r;
			}
			if (only) {
				var code = ui != "zh" && ui != "en" && ui != "ja" && ui != "ko" ? "en" : (ui ?? "zh");
				foreach (var c in new[] { code, "en", "zh", "ja", "ko" }) {
					if (c == "ko") {
						if (sn.Ko.Length == 0) continue;
						gloss(null, sn.Ko, "", "ko");
						break;
					}
					string lemma, def;
					if (c == "zh") { lemma = sn.Zh; def = sn.ZhDef; }
					else if (c == "en") { lemma = sn.En; def = sn.EnDef; }
					else { lemma = sn.Ja; def = sn.JaDef; }
					if (lemma.Length == 0 && def.Length == 0) continue;
					gloss(null, lemma, def, c);
					break;
				}
			}
			else {
				if (sn.Ko.Length > 0) gloss(null, sn.Ko, "", "ko");
				gloss("dict.lab.zh", sn.Zh, sn.ZhDef, "zh");
				gloss("dict.lab.en", sn.En, sn.EnDef, "en");
				gloss("dict.lab.ja", sn.Ja, sn.JaDef, "ja");
			}
			if (e.Dict == "ko" && sn.Phrases.Count > 0 && sn.Idiom.Length == 0) {
				blocks.Add(gap(4));
				blocks.Add(p(1, 0, new JsonArray { k("dict.lab.phrase", "l", "：") }));
				foreach (var ph in sn.Phrases) {
					if (ph.Text.Length == 0 && ph.Zh.Length == 0) continue;
					var r = new JsonArray { s("· ", "n") };
					if (ph.Text.Length > 0) r.Add(s(ph.Text, "b", entrylang));
					var say = DictWindow.examplesrc(ph.Text);
					if (say.Length > 0) r.Add(spk(say, entrylang));
					if (ph.Zh.Length > 0) {
						r.Add(s("  ", "b"));
						r.Add(s(ph.Zh, "l", "zh"));
					}
					blocks.Add(p(1, 0, r));
				}
			}
			else foreach (var ph in sn.Phrases) {
				if (ph.Text.Length == 0 && ph.Zh.Length == 0) continue;
				var r = new JsonArray { k("dict.lab.phrase", "l", " ") };
				if (linkphrase && ph.Text.Length > 0) r.Add(link(ph.Text, DictWindow.firsttoken(ph.Text), entrylang));
				else r.Add(s(ph.Text, "b", entrylang));
				if (!linkphrase) {
					var say = DictWindow.examplesrc(ph.Text);
					if (say.Length > 0) r.Add(spk(say, entrylang));
				}
				blocks.Add(p(0, 0, r));
				if (ph.Zh.Length > 0) {
					blocks.Add(p(0, 0, new JsonArray {
						k("dict.lab.zh", "l", " ", "zh"),
						s(ph.Zh, "b", "zh"),
					}));
				}
			}
			if (!only) {
				if (sn.Sentences.Count > 0) {
					if (blockstyle && sn.Idiom.Length == 0) blocks.Add(gap(4));
					blocks.Add(p(blockstyle ? 1 : 0, 0, new JsonArray { k("dict.lab.example", "e") }));
				}
				foreach (var ex in sn.Sentences) {
					if (ex.Text.Length == 0 && ex.Zh.Length == 0) continue;
					var r = new JsonArray();
					if (blockstyle) {
						r.Add(s("· ", "n"));
						if (ex.Text.Length > 0) r.Add(s(ex.Text, "x", entrylang));
					}
					else r.Add(s("· " + ex.Text, "x", entrylang));
					var say = DictWindow.examplesrc(ex.Text);
					if (say.Length > 0) r.Add(spk(say, entrylang));
					var same = blockstyle && ex.Zh.Length > 0;
					if (same) {
						r.Add(s("  ", "b"));
						r.Add(s(ex.Zh, "l", "zh"));
					}
					blocks.Add(p(blockstyle ? 1 : 0, 0, r));
					if (ex.Zh.Length > 0 && !same) {
						blocks.Add(p(0, 0, new JsonArray {
							k("dict.lab.zh", "l", " ", "zh"),
							s(ex.Zh, "b", "zh"),
						}));
					}
				}
			}
			if (!sn.Sub && !numbered && sn.Phrases.Count == 0 && sn.Sentences.Count == 0) {
				blocks.Add(p(0, 0, new JsonArray {
					s(n + ". ", "n"),
					k("dict.empty.sense", "r"),
				}));
				n++;
			}
			else if (!sn.Sub && !numbered) {
				blocks.Add(p(0, 0, new JsonArray { s(n + ".", "n") }));
				n++;
			}
		}
	}

	static JsonArray p(double indent, double bottom, JsonArray runs) =>
		new() { indent > 0 ? 1 : 0, (int)bottom, runs };

	static JsonArray gap(double h) => p(0, h, new JsonArray());

	static JsonArray one(string text, string role, double bottom, string lang = null) =>
		p(0, bottom, new JsonArray { s(text, role, lang) });

	static JsonArray oneKey(string key, string role, double bottom, string suffix = null) =>
		p(0, bottom, new JsonArray { k(key, role, suffix) });

	static JsonArray s(string text, string role, string lang = null) =>
		new() { "t", text ?? "", role ?? "b", lang ?? "" };

	static JsonArray k(string key, string role, string suffix = null, string lang = null) =>
		new() { "k", key, role ?? "l", suffix ?? "", lang ?? "" };

	static JsonArray link(string text, string query, string lang) =>
		new() { "a", text ?? "", query ?? text ?? "", lang ?? "" };

	static JsonArray spk(string text, string lang) =>
		new() { "v", text ?? "", lang ?? "" };
}

/// <summary>把 DictDoc 的 JSON 画进 WPF 的 FlowDocument。</summary>
static class DictDocView {
	public static void Fill(FlowDocument doc, JsonNode node, Action<string> onlink, Action<string, string> onspk) {
		doc.Blocks.Clear();
		if (node?["lines"] is not JsonArray lines) return;
		foreach (var ln in lines) {
			if (ln is not JsonArray a || a.Count < 3) continue;
			var p = new Paragraph {
				Margin = new Thickness(num(a[0]) > 0 ? 16 : 0, 0, 0, num(a[1])),
				LineHeight = 18,
			};
			if (a[2] is JsonArray runs) {
				foreach (var rn in runs) addruns(p, rn as JsonArray, onlink, onspk);
			}
			doc.Blocks.Add(p);
		}
	}

	static void addruns(Paragraph p, JsonArray r, Action<string> onlink, Action<string, string> onspk) {
		if (r == null || r.Count < 2) return;
		var kind = (string)r[0];
		if (kind == "t") {
			addrun(p, str(r, 1), role(str(r, 2)), str(r, 3));
			return;
		}
		if (kind == "k") {
			var text = Loc.T(str(r, 1)) + str(r, 3);
			addrun(p, text, role(str(r, 2)), str(r, 4));
			return;
		}
		if (kind == "a") {
			var run = new Run(str(r, 1));
			var lang = str(r, 3);
			if (DictWindow.langindex(lang) >= 0) run.Tag = lang;
			var link = new Hyperlink(run) {
				Foreground = DictWindow.CLabel,
				TextDecorations = null,
			};
			var q = str(r, 2);
			link.Click += (_, _) => onlink(q);
			p.Inlines.Add(link);
			return;
		}
		if (kind == "v") {
			p.Inlines.Add(new Run(" "));
			var run = new Run("\uE767") {
				FontFamily = new FontFamily("Segoe MDL2 Assets"),
				FontSize = 13,
				Foreground = DictWindow.CSpeak,
			};
			var link = new Hyperlink(run) {
				Foreground = DictWindow.CSpeak,
				TextDecorations = null,
			};
			var text = str(r, 1);
			var lang = str(r, 2);
			link.Click += (_, _) => onspk(text, lang);
			p.Inlines.Add(link);
		}
	}

	static void addrun(Paragraph p, string text, Brush brush, string lang) {
		if (string.IsNullOrEmpty(text)) return;
		var run = new Run(text);
		if (brush != null) run.Foreground = brush;
		if (DictWindow.langindex(lang) >= 0) run.Tag = lang;
		p.Inlines.Add(run);
	}

	static Brush role(string code) {
		if (code == "t") return null;
		if (code == "r") return DictWindow.CPron;
		if (code == "p") return DictWindow.CPos;
		if (code == "n") return DictWindow.CNumber;
		if (code == "l") return DictWindow.CLabel;
		if (code == "x") return DictWindow.CExample;
		if (code == "e") return DictWindow.CExLabel;
		return null;
	}

	static string str(JsonArray a, int i) {
		if (i >= a.Count || a[i] == null) return "";
		if (a[i] is JsonValue v) return v.ToString();
		return "";
	}

	static double num(JsonNode n) {
		if (n is JsonValue v && v.TryGetValue<double>(out var d)) return d;
		if (n is JsonValue vi && vi.TryGetValue<int>(out var i)) return i;
		return 0;
	}
}
