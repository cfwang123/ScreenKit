using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WpfOCR;

/// <summary>OpenAI 兼容 Chat Completions：离线听写句末润色。</summary>
static class AsrLlmClient {
	const int TimeoutMs = 12_000;
	const int MaxCtx = 1200;
	const string ProxyAddr = "http://127.0.0.1:7897";
	const string CtxHint = "若提供上文，请结合上文纠正同音字、专有名词与指代；只输出「待润色」这一句的结果，不要重复上文、不要解释。";

	public static bool IsConfigured(OcrOptions o) =>
		o != null
		&& !string.IsNullOrWhiteSpace(o.AsrLlmUrl)
		&& !string.IsNullOrWhiteSpace(o.AsrLlmModel);

	/// <summary>已配置则请求润色；失败或未配置返回原文。context 为本轮已输出上文。</summary>
	public static string Polish(OcrOptions o, string text, string context = "", CancellationToken ct = default) {
		text = (text ?? "").Trim();
		if (text.Length == 0 || !IsConfigured(o)) return text;
		ct.ThrowIfCancellationRequested();
		try {
			var outText = call(o, text, context, ct);
			if (string.IsNullOrWhiteSpace(outText)) return text;
			outText = outText.Trim();
			if (outText.Length == 0) return text;
			outText = stripthink(outText);
			if (string.IsNullOrWhiteSpace(outText)) return text;
			return outText.Trim();
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex) {
			try { CaptureLog.Ex("asr llm polish", ex); } catch { }
			return text;
		}
	}

	static string call(OcrOptions o, string text, string context, CancellationToken ct) {
		var url = normalizeurl(o.AsrLlmUrl);
		var model = (o.AsrLlmModel ?? "").Trim();
		var prompt = (o.AsrLlmPrompt ?? "").Trim();
		if (string.IsNullOrEmpty(prompt))
			prompt = OcrOptions.DefaultAsrLlmPrompt;
		context = clipend(context, MaxCtx);
		if (context.Length > 0)
			prompt = $"{prompt}\n{CtxHint}";
		var user = context.Length == 0
			? text
			: $"上文：\n{context}\n\n待润色：\n{text}";
		var payload = JsonSerializer.Serialize(new {
			model,
			temperature = 0.2,
			messages = new object[] {
				new { role = "system", content = prompt },
				new { role = "user", content = user },
			},
		});

		using var handler = makehandler(url);
		using var http = new HttpClient(handler) {
			Timeout = TimeSpan.FromMilliseconds(TimeoutMs),
		};
		using var req = new HttpRequestMessage(HttpMethod.Post, url);
		req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
		req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		var token = (o.AsrLlmToken ?? "").Trim();
		if (token.Length > 0)
			req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var resp = http.SendAsync(req, ct).GetAwaiter().GetResult();
		var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
		if (!resp.IsSuccessStatusCode) {
			var snippet = body.Length > 240 ? body.Substring(0, 240) : body;
			throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {snippet}");
		}
		return extractcontent(body);
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
		if (!c0.TryGetProperty("message", out var msg))
			throw new InvalidOperationException("响应无 message");
		if (!msg.TryGetProperty("content", out var content)
			|| content.ValueKind != JsonValueKind.String)
			throw new InvalidOperationException("响应无 content");
		return stripthink(content.GetString() ?? "");
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
