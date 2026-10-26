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
			ChromeTts.ClientProfile(ctx.Request?.RemoteEndPoint?.Port ?? 0, out var profile, out var profileKind, out var browserPid);
			// 模型页就在程序这份配置的 Chrome 里开着时，装包要接上它，不能把窗口关掉。
			if (browserPid > 0 && profileKind == "app") ChromeHost.PageOpened(browserPid);
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
					["profile"] = profile,
					["profileKind"] = profileKind,
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

	static readonly object voiceGate = new();
	static int voiceJobId;
	static string voiceState = "idle";
	static string voiceLang = "";
	static string voiceNote = "";
	static double voiceFrac = -1;
	static long voiceDone;
	static long voiceTotal;
	static string voiceError = "";
	static CancellationTokenSource voiceCts;

	void handlevoice(SockCtx ctx) {
		if (!readbody(ctx, out var jo)) return;
		if (!ispost(ctx.Request)) {
			writejson(ctx, 200, okvoice());
			return;
		}
		var op = (field(ctx, jo, "op") ?? "start").Trim().ToLowerInvariant();
		if (op is "cancel" or "stop") {
			cancelvoice(asint(jo?["id"], 0));
			writejson(ctx, 200, okvoice());
			return;
		}
		var lang = ChromePack.SpeakLang(field(ctx, jo, "lang") ?? "");
		if (lang.Length == 0) {
			writejson(ctx, 200, err(802, "没有语言"));
			return;
		}
		string busy;
		lock (voiceGate) {
			if (voiceState == "run") {
				busy = "run";
			}
			else if (ChromePack.VoiceInstalled(lang) == true) {
				voiceState = "installed";
				voiceLang = lang;
				voiceNote = "";
				voiceError = "";
				voiceFrac = 1;
				busy = "installed";
			}
			else if (Interlocked.CompareExchange(ref chromePackBusy, 1, 0) != 0) {
				busy = "pack";
			}
			else {
				voiceJobId++;
				voiceState = "run";
				voiceLang = lang;
				voiceNote = Loc.T("tts.chrome.voice.check");
				voiceError = "";
				voiceFrac = -1;
				voiceDone = 0;
				voiceTotal = 0;
				voiceCts?.Dispose();
				voiceCts = new CancellationTokenSource();
				var id = voiceJobId;
				var token = voiceCts.Token;
				var mode = HttpProxy.Mode;
				var addr = HttpProxy.Addr;
				ThreadPool.QueueUserWorkItem(_ => runvoice(id, lang, mode, addr, token));
				busy = "run";
			}
		}
		if (busy == "pack") {
			writejson(ctx, 200, err(809, "正在处理 Chrome 模型"));
			return;
		}
		writejson(ctx, 200, okvoice());
	}

	static void cancelvoice(int id) {
		lock (voiceGate) {
			if (voiceState != "run") return;
			if (id != 0 && id != voiceJobId) return;
			voiceNote = Loc.T("update.cancelling");
			try { voiceCts?.Cancel(); } catch { }
		}
	}

	static void runvoice(int id, string lang, string mode, string addr, CancellationToken token) {
		try {
			var saved = new OcrOptions();
			try { AppConfig.LoadInto(saved); } catch { }
			var exe = ChromeTts.FindExe(saved.ChromeExe);
			if (string.IsNullOrEmpty(exe))
				throw new InvalidOperationException(Loc.T("tts.chrome.missing"));
			ChromePack.EnsureVoice(lang, exe, mode, addr, (note, frac, done, total) => {
				lock (voiceGate) {
					if (id != voiceJobId) return;
					if (!string.IsNullOrWhiteSpace(note)) voiceNote = note;
					voiceFrac = frac;
					voiceDone = done;
					voiceTotal = total;
				}
			}, token);
			lock (voiceGate) {
				if (id != voiceJobId) return;
				if (token.IsCancellationRequested) voiceState = "cancel";
				else if (ChromePack.VoiceInstalled(lang) == true) {
					voiceState = "installed";
					voiceFrac = 1;
				}
				else {
					voiceState = "error";
					voiceError = Loc.T("tts.chrome.voice.need", lang);
				}
			}
		}
		catch (OperationCanceledException) {
			lock (voiceGate) {
				if (id == voiceJobId) voiceState = "cancel";
			}
		}
		catch (Exception ex) {
			lock (voiceGate) {
				if (id != voiceJobId) return;
				voiceState = "error";
				voiceError = ex.Message;
			}
		}
		finally {
			Interlocked.Exchange(ref chromePackBusy, 0);
		}
	}

	static JsonObject okvoice() {
		lock (voiceGate) {
			return new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["id"] = voiceJobId,
					["state"] = voiceState,
					["lang"] = voiceLang,
					["note"] = voiceNote ?? "",
					["frac"] = voiceFrac,
					["done"] = voiceDone,
					["total"] = voiceTotal,
					["error"] = voiceError ?? "",
				},
			};
		}
	}
}
