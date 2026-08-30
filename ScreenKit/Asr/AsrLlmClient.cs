using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScreenKit;

/// <summary>OpenAI 兼容 Chat Completions：听写润色、LLM 翻译。</summary>
static class AsrLlmClient {
	const int TimeoutMs = 12_000;
	const int TranslateTimeoutMs = 90_000;
	const int MaxCtx = 1200;
	const string ProxyAddr = "http://127.0.0.1:7897";
	const string CtxHint = "若提供上文，请结合上文纠正同音字、专有名词与指代；只输出「待润色」这一句的结果，不要重复上文、不要解释。";

	public static bool IsEndpointReady(LlmEndpoint ep) =>
		ep != null
		&& !string.IsNullOrWhiteSpace(ep.Url)
		&& !string.IsNullOrWhiteSpace(ep.Model);

	public static bool IsConfigured(OcrOptions o) => IsEndpointReady(o?.SelectedLlm());

	public static bool IsTranslateReady(OcrOptions o) => IsEndpointReady(o?.SelectedTranslateLlm());

	/// <summary>已配置则请求润色；失败或未配置返回原文。context 为本轮已输出上文。</summary>
	public static string Polish(OcrOptions o, string text, string context = "", CancellationToken ct = default) {
		text = (text ?? "").Trim();
		if (text.Length == 0 || !IsConfigured(o)) return text;
		ct.ThrowIfCancellationRequested();
		try {
			var outText = call(o, text, context, ct);
			if (string.IsNullOrWhiteSpace(outText)) {
				LlmLog.Info("extract empty → keep original");
				return text;
			}
			outText = outText.Trim();
			if (outText.Length == 0) return text;
			outText = stripthink(outText);
			if (string.IsNullOrWhiteSpace(outText)) {
				LlmLog.Info("strip think empty → keep original");
				return text;
			}
			return outText.Trim();
		}
		catch (OperationCanceledException) {
			if (ct.IsCancellationRequested) throw;
			LlmLog.Info("http timeout → keep original");
			return text;
		}
		catch (Exception ex) {
			LlmLog.Ex("asr llm polish", ex);
			try { CaptureLog.Ex("asr llm polish", ex); } catch { }
			return text;
		}
	}

	static string call(OcrOptions o, string text, string context, CancellationToken ct) {
		var ep = o.SelectedLlm();
		if (ep == null) return text;
		var url = normalizeurl(ep.Url);
		var model = (ep.Model ?? "").Trim();
		var prompt = (o.AsrLlmPrompt ?? "").Trim();
		if (string.IsNullOrEmpty(prompt))
			prompt = OcrOptions.DefaultAsrLlmPrompt;
		context = clipend(context, MaxCtx);
		if (context.Length > 0)
			prompt = $"{prompt}\n{CtxHint}";
		var user = context.Length == 0
			? text
			: $"上文：\n{context}\n\n待润色：\n{text}";
		LlmLog.Info($"polish model={model} url={url} think={LlmEndpoint.NormThink(ep.Think)} inLen={text.Length} ctxLen={context.Length}");
		return complete(ep, prompt, user, TimeoutMs, ct);
	}

	/// <summary>LLM 翻译；失败抛异常。src/dst 为语言代码（zh/en/ja/ko）。</summary>
	public static string Translate(OcrOptions o, string text, string src, string dst, CancellationToken ct = default) {
		text = (text ?? "").Trim();
		if (text.Length == 0) return text;
		var ep = o?.SelectedTranslateLlm();
		if (!IsEndpointReady(ep))
			throw new InvalidOperationException("未配置翻译 LLM（需 URL 与模型 id）");
		ct.ThrowIfCancellationRequested();
		var prompt = (o.TranslateLlmPrompt ?? "").Trim();
		if (string.IsNullOrEmpty(prompt))
			prompt = OcrOptions.DefaultTranslateLlmPrompt;
		var srcL = TrLang.Label(src);
		var dstL = TrLang.Label(dst);
		prompt = prompt.Replace("{src}", srcL).Replace("{dst}", dstL)
			.Replace("{source}", srcL).Replace("{target}", dstL);
		LlmLog.Info($"translate {src}->{dst} model={ep.Model} think={LlmEndpoint.NormThink(ep.Think)} inLen={text.Length}");
		var outText = complete(ep, prompt, text, TranslateTimeoutMs, ct);
		if (string.IsNullOrWhiteSpace(outText))
			throw new InvalidOperationException("LLM 返回空译文");
		outText = stripthink(outText.Trim());
		if (string.IsNullOrWhiteSpace(outText))
			throw new InvalidOperationException("LLM 译文去掉思考块后为空");
		return outText.Trim();
	}

	static string complete(LlmEndpoint ep, string prompt, string user, int timeoutMs, CancellationToken ct) {
		var url = normalizeurl(ep.Url);
		var model = (ep.Model ?? "").Trim();
		var key = (ep.Key ?? "").Trim();
		var think = LlmEndpoint.NormThink(ep.Think);
		var json = JsonSerializer.Serialize(makepayload(model, prompt, user, think));
		LlmLog.Info("req " + clip(json, 4000));
		var (code, body, ms) = postjson(url, key, json, timeoutMs, ct);
		LlmLog.Info($"resp {code} {ms}ms len={body.Length} {clip(body, 4000)}");
		if (code == 400 && think == "off" && mustthink(body)) {
			think = "low";
			json = JsonSerializer.Serialize(makepayload(model, prompt, user, think));
			LlmLog.Info("retry think=low (model forbids off) " + clip(json, 4000));
			(code, body, ms) = postjson(url, key, json, timeoutMs, ct);
			LlmLog.Info($"resp {code} {ms}ms len={body.Length} {clip(body, 4000)}");
		}
		if (code == 400) {
			json = JsonSerializer.Serialize(makepayload(model, prompt, user, null));
			LlmLog.Info("retry without think fields " + clip(json, 4000));
			(code, body, ms) = postjson(url, key, json, timeoutMs, ct);
			LlmLog.Info($"resp {code} {ms}ms len={body.Length} {clip(body, 4000)}");
		}
		if (code < 200 || code >= 300) {
			var snippet = body.Length > 240 ? body.Substring(0, 240) : body;
			throw new InvalidOperationException($"HTTP {code}: {snippet}");
		}
		var extracted = extractcontent(body);
		LlmLog.Info("extracted " + clip(extracted, 500));
		return extracted;
	}

	/// <summary>think：off 关闭；low/high/max 开启并设 reasoning_effort；null 不带思考字段。</summary>
	static Dictionary<string, object> makepayload(string model, string prompt, string user, string think) {
		var p = new Dictionary<string, object> {
			["model"] = model,
			["temperature"] = 0.2,
			["messages"] = new object[] {
				new { role = "system", content = prompt },
				new { role = "user", content = user },
			},
		};
		if (string.IsNullOrEmpty(think)) return p;
		if (think == "off") {
			p["thinking"] = new Dictionary<string, string> { ["type"] = "disabled" };
			p["enable_thinking"] = false;
			p["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = false };
			return p;
		}
		p["thinking"] = new Dictionary<string, string> { ["type"] = "enabled" };
		p["reasoning_effort"] = think;
		return p;
	}

	static bool mustthink(string body) {
		body = (body ?? "").ToLowerInvariant();
		return body.IndexOf("cannot be disabled", StringComparison.Ordinal) >= 0
			|| body.IndexOf("always engages in thinking", StringComparison.Ordinal) >= 0
			|| (body.IndexOf("low", StringComparison.Ordinal) >= 0
				&& body.IndexOf("high", StringComparison.Ordinal) >= 0
				&& body.IndexOf("max", StringComparison.Ordinal) >= 0
				&& body.IndexOf("thinking", StringComparison.Ordinal) >= 0);
	}

	static (int code, string body, int ms) postjson(string url, string key, string json, int timeoutMs, CancellationToken ct) {
		var t0 = Environment.TickCount;
		using var handler = makehandler(url);
		using var http = new HttpClient(handler) {
			Timeout = TimeSpan.FromMilliseconds(timeoutMs > 0 ? timeoutMs : TimeoutMs),
		};
		using var req = new HttpRequestMessage(HttpMethod.Post, url);
		req.Content = new StringContent(json, Encoding.UTF8, "application/json");
		req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		if (key.Length > 0)
			req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
		using var resp = http.SendAsync(req, ct).GetAwaiter().GetResult();
		var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
		var ms = unchecked(Environment.TickCount - t0);
		return ((int)resp.StatusCode, body, ms);
	}

	static string clipend(string s, int max) {
		s = (s ?? "").Trim();
		if (s.Length <= max) return s;
		s = s.Substring(s.Length - max);
		var cut = s.IndexOfAny(new[] { '\n', '。', '！', '？', '；' });
		if (cut >= 0 && cut < s.Length - 8)
			s = s.Substring(cut + 1);
		return s.Trim();
	}

	static string normalizeurl(string raw) {
		var s = (raw ?? "").Trim();
		if (s.Length == 0) return s;
		s = s.TrimEnd('/');
		if (s.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
			return s;
		if (s.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
			|| s.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
			return s + "/chat/completions";
		return s + "/v1/chat/completions";
	}

	static HttpClientHandler makehandler(string url) {
		var h = new HttpClientHandler();
		if (needproxy(url)) {
			h.Proxy = new WebProxy(ProxyAddr);
			h.UseProxy = true;
		}
		return h;
	}

	static bool needproxy(string url) {
		if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
		var host = u.Host ?? "";
		if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
			|| host == "127.0.0.1" || host == "::1" || host == "[::1]")
			return false;
		if (IPAddress.TryParse(host, out var ip) && isprivate(ip))
			return false;
		return true;
	}

	static bool isprivate(IPAddress ip) {
		if (IPAddress.IsLoopback(ip)) return true;
		var b = ip.GetAddressBytes();
		if (b.Length == 4) {
			if (b[0] == 10) return true;
			if (b[0] == 192 && b[1] == 168) return true;
			if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
		}
		return false;
	}

	static string extractcontent(string json) {
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (!root.TryGetProperty("choices", out var choices)
			|| choices.ValueKind != JsonValueKind.Array
			|| choices.GetArrayLength() == 0)
			throw new InvalidOperationException("响应无 choices");
		var c0 = choices[0];
		if (c0.TryGetProperty("message", out var msg)) {
			var text = readtext(msg, "content");
			if (text.Length > 0) return stripthink(text);
			LlmLog.Info("message.content empty kind=" +
				(msg.TryGetProperty("content", out var c) ? c.ValueKind.ToString() : "missing"));
			return "";
		}
		if (c0.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
			return stripthink(t.GetString() ?? "");
		throw new InvalidOperationException("响应无 message");
	}

	static string readtext(JsonElement msg, string name) {
		if (!msg.TryGetProperty(name, out var el)) return "";
		if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
		if (el.ValueKind == JsonValueKind.Null) return "";
		if (el.ValueKind != JsonValueKind.Array) return "";
		var sb = new StringBuilder();
		foreach (var p in el.EnumerateArray()) {
			if (p.ValueKind == JsonValueKind.String)
				sb.Append(p.GetString());
			else if (p.ValueKind == JsonValueKind.Object
				&& p.TryGetProperty("text", out var tx)
				&& tx.ValueKind == JsonValueKind.String)
				sb.Append(tx.GetString());
		}
		return sb.ToString();
	}

	static string clip(string s, int max) {
		s = s ?? "";
		if (s.Length <= max) return s;
		return s.Substring(0, max) + $"…({s.Length})";
	}

	/// <summary>去掉推理模型泄漏的 think 块、孤立闭合标签、整段 markdown 围栏。</summary>
	static string stripthink(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		s = strippair(s, "<think>", "</think>");
		s = strippair(s, "<thinking>", "</thinking>");
		s = strippair(s, "<reason>", "</reason>");
		s = strippair(s, "<reasoning>", "</reasoning>");
		s = strippair(s, "<redacted_thinking>", "</redacted_thinking>");
		s = afterlast(s, "</think>");
		s = afterlast(s, "</thinking>");
		s = cutci(s, "<think>");
		s = cutci(s, "<thinking>");
		s = stripfence(s);
		return s.Trim();
	}

	static string cutci(string s, string token) {
		while (true) {
			var i = s.IndexOf(token, StringComparison.OrdinalIgnoreCase);
			if (i < 0) return s;
			s = s.Remove(i, token.Length);
		}
	}

	static string strippair(string s, string open, string close) {
		while (true) {
			var i = s.IndexOf(open, StringComparison.OrdinalIgnoreCase);
			if (i < 0) break;
			var j = s.IndexOf(close, i + open.Length, StringComparison.OrdinalIgnoreCase);
			if (j < 0) {
				s = s.Substring(0, i);
				break;
			}
			s = s.Remove(i, j + close.Length - i);
		}
		return s;
	}

	/// <summary>仅闭合标签泄漏时，取最后一次闭合之后的正文。</summary>
	static string afterlast(string s, string close) {
		var j = s.LastIndexOf(close, StringComparison.OrdinalIgnoreCase);
		if (j < 0) return s;
		return s.Substring(j + close.Length);
	}

	static string stripfence(string s) {
		s = (s ?? "").Trim();
		if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
		var end = s.LastIndexOf("```", StringComparison.Ordinal);
		if (end <= 2) return s;
		s = s.Substring(3, end - 3).Trim();
		var nl = s.IndexOf('\n');
		if (nl > 0 && nl < 20) {
			var lang = s.Substring(0, nl).Trim();
			if (lang.Length > 0 && lang.Length < 16 && lang.IndexOf(' ') < 0)
				s = s.Substring(nl + 1).Trim();
		}
		return s;
	}
}
