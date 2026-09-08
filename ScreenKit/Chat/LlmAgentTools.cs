using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>一条待执行的工具调用。</summary>
sealed class LlmToolCall {
	public string Name = "";
	public string ArgsJson = "{}";
	public string Raw = "";
}

/// <summary>Agent 工具实现（沙箱 tmp/llm/ + 网页搜索/抓取）。</summary>
static class LlmAgentTools {
	public const int MaxFileBytes = 256 * 1024;
	public const int MaxScriptOut = 32 * 1024;
	public const int ScriptTimeoutMs = 30_000;
	public const int FetchTimeoutMs = 15_000;
	public const int SearchTimeoutMs = 20_000;

	static readonly Regex ToolCallRe = new(
		@"<tool_call>\s*(\{[\s\S]*?\})\s*</tool_call>",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);
	static readonly Regex TagRe = new(@"<[^>]+>", RegexOptions.Compiled);
	static readonly Regex WsRe = new(@"[ \t]+\n", RegexOptions.Compiled);
	static readonly object gate = new();
	static Process running;

	/// <summary>取消时杀掉正在跑的脚本。</summary>
	public static void CancelRunning() {
		Process p;
		lock (gate) { p = running; running = null; }
		if (p == null) return;
		try {
			if (!p.HasExited) p.Kill();
		}
		catch { }
		try { p.Dispose(); } catch { }
	}

	public static List<LlmToolCall> ParseCalls(string text) {
		var list = new List<LlmToolCall>();
		text = text ?? "";
		foreach (Match m in ToolCallRe.Matches(text)) {
			var json = m.Groups[1].Value.Trim();
			var call = new LlmToolCall { Raw = m.Value, ArgsJson = "{}" };
			try {
				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;
				if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
					call.Name = (n.GetString() ?? "").Trim();
				else if (root.TryGetProperty("tool", out var t) && t.ValueKind == JsonValueKind.String)
					call.Name = (t.GetString() ?? "").Trim();
				if (root.TryGetProperty("arguments", out var a))
					call.ArgsJson = a.ValueKind == JsonValueKind.String
						? (a.GetString() ?? "{}")
						: a.GetRawText();
				else if (root.TryGetProperty("args", out var a2))
					call.ArgsJson = a2.ValueKind == JsonValueKind.String
						? (a2.GetString() ?? "{}")
						: a2.GetRawText();
				else
					call.ArgsJson = "{}";
			}
			catch {
				call.Name = "";
				call.ArgsJson = json;
			}
			if (call.Name.Length == 0 && list.Count == 0 && json.Length > 0)
				call.Name = "?";
			if (call.Name.Length > 0)
				list.Add(call);
		}
		return list;
	}

	/// <summary>去掉 tool_call 块后的可见正文（用于最终回复）。</summary>
	public static string StripCalls(string text) {
		if (string.IsNullOrEmpty(text)) return "";
		return ToolCallRe.Replace(text, "").Trim();
	}

	public static string Execute(LlmToolCall call, CancellationToken ct) {
		if (call == null) return "error: empty call";
		var name = (call.Name ?? "").Trim().ToLowerInvariant();
		LlmLog.Info($"agent tool={name}");
		try {
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgsJson) ? "{}" : call.ArgsJson);
			var args = doc.RootElement;
			return name switch {
				"web_search" => websearch(args, ct),
				"web_fetch" => webfetch(args, ct),
				"list_dir" => listdir(args),
				"read_file" => readfile(args),
				"write_file" => writefile(args),
				"run_script" => runscript(args, ct),
				_ => "error: unknown tool '" + call.Name + "'. Use web_search, web_fetch, list_dir, read_file, write_file, run_script.",
			};
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex) {
			LlmLog.Ex("agent tool " + name, ex);
			return "error: " + ex.Message;
		}
	}

	public static string ShortLabel(LlmToolCall call) {
		if (call == null) return "tool";
		var name = (call.Name ?? "tool").Trim();
		try {
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgsJson) ? "{}" : call.ArgsJson);
			var a = doc.RootElement;
			if (name.Equals("web_search", StringComparison.OrdinalIgnoreCase))
				return name + ": " + clip(str(a, "query"), 40);
			if (name.Equals("web_fetch", StringComparison.OrdinalIgnoreCase))
				return name + ": " + clip(str(a, "url"), 50);
			if (name.Equals("read_file", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("write_file", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("run_script", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("list_dir", StringComparison.OrdinalIgnoreCase))
				return name + ": " + clip(str(a, "path", ".") , 40);
		}
		catch { }
		return name;
	}

	static string websearch(JsonElement args, CancellationToken ct) {
		var q = str(args, "query");
		if (q.Length == 0) return "error: query required";
		var count = intval(args, "count", 5);
		if (count < 1) count = 1;
		if (count > 8) count = 8;
		ct.ThrowIfCancellationRequested();
		var url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(q);
		string html;
		try {
			html = httpget(url, SearchTimeoutMs, ct);
		}
		catch (Exception ex) {
			return "error: search failed: " + ex.Message;
		}
		var items = parseddg(html, count);
		if (items.Count == 0)
			return "no results (parse empty). Try another query or web_fetch a known URL.";
		var sb = new StringBuilder();
		for (var i = 0; i < items.Count; i++) {
			var (title, link, snippet) = items[i];
			sb.Append(i + 1).Append(". ").Append(title).Append('\n');
			sb.Append("   ").Append(link).Append('\n');
			if (snippet.Length > 0)
				sb.Append("   ").Append(snippet).Append('\n');
		}
		return sb.ToString().TrimEnd();
	}

	static List<(string title, string link, string snippet)> parseddg(string html, int count) {
		var list = new List<(string, string, string)>();
		if (string.IsNullOrEmpty(html)) return list;
		// result__a href + 文本；result__snippet
		var linkRe = new Regex(
			@"<a[^>]*class=""[^""]*result__a[^""]*""[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>",
			RegexOptions.IgnoreCase);
		var snipRe = new Regex(
			@"class=""[^""]*result__snippet[^""]*""[^>]*>([\s\S]*?)</(?:a|td|div)",
			RegexOptions.IgnoreCase);
		var links = linkRe.Matches(html);
		var snips = snipRe.Matches(html);
		for (var i = 0; i < links.Count && list.Count < count; i++) {
			var href = decode(links[i].Groups[1].Value.Trim());
			var title = plain(links[i].Groups[2].Value);
			if (title.Length == 0) continue;
			href = unwrapddg(href);
			if (href.Length == 0 || !href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				continue;
			var snip = i < snips.Count ? plain(snips[i].Groups[1].Value) : "";
			list.Add((clip(title, 120), href, clip(snip, 200)));
		}
		return list;
	}

	static string unwrapddg(string href) {
		// //duckduckgo.com/l/?uddg=...
		try {
			if (href.StartsWith("//", StringComparison.Ordinal))
				href = "https:" + href;
			if (href.IndexOf("uddg=", StringComparison.OrdinalIgnoreCase) >= 0) {
				var u = new Uri(href, UriKind.Absolute);
				var q = u.Query.TrimStart('?');
				foreach (var part in q.Split('&')) {
					var kv = part.Split(new[] { '=' }, 2);
					if (kv.Length == 2 && kv[0].Equals("uddg", StringComparison.OrdinalIgnoreCase))
						return Uri.UnescapeDataString(kv[1].Replace("+", " "));
				}
			}
		}
		catch { }
		return href;
	}

	static string webfetch(JsonElement args, CancellationToken ct) {
		var url = str(args, "url");
		if (url.Length == 0) return "error: url required";
		if (!Uri.TryCreate(url, UriKind.Absolute, out var u)
			|| (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
			return "error: url must be http(s)";
		var max = intval(args, "max_chars", 8000);
		if (max < 500) max = 500;
		if (max > 20000) max = 20000;
		ct.ThrowIfCancellationRequested();
		string body;
		try {
			body = httpget(url, FetchTimeoutMs, ct);
		}
		catch (Exception ex) {
			return "error: fetch failed: " + ex.Message;
		}
		var text = plain(body);
		text = WsRe.Replace(text, "\n");
		text = Regex.Replace(text, @"\n{3,}", "\n\n");
		if (text.Length > max)
			return text.Substring(0, max) + $"\n…(truncated {text.Length} chars)";
		return text.Length == 0 ? "(empty)" : text;
	}

	static string listdir(JsonElement args) {
		var path = str(args, "path", ".");
		if (!LlmAgentPaths.TryResolve(path, out var full, out var rel, out var err))
			return "error: " + err;
		if (!Directory.Exists(full))
			return "error: not a directory: " + rel;
		var sb = new StringBuilder();
		sb.Append("dir: ").Append(rel).Append('\n');
		try {
			foreach (var d in Directory.EnumerateDirectories(full)) {
				sb.Append("[dir]  ").Append(Path.GetFileName(d)).Append('\n');
			}
			foreach (var f in Directory.EnumerateFiles(full)) {
				long len = 0;
				try { len = new FileInfo(f).Length; } catch { }
				sb.Append("[file] ").Append(Path.GetFileName(f)).Append(" (").Append(len).Append(" bytes)\n");
			}
		}
		catch (Exception ex) {
			return "error: " + ex.Message;
		}
		return sb.ToString().TrimEnd();
	}

	static string readfile(JsonElement args) {
		var path = str(args, "path");
		if (path.Length == 0) return "error: path required";
		if (!LlmAgentPaths.TryResolve(path, out var full, out var rel, out var err))
			return "error: " + err;
		if (!File.Exists(full))
			return "error: file not found: " + rel;
		byte[] bytes;
		try { bytes = File.ReadAllBytes(full); }
		catch (Exception ex) { return "error: " + ex.Message; }
		var truncated = false;
		if (bytes.Length > MaxFileBytes) {
			var slice = new byte[MaxFileBytes];
			Buffer.BlockCopy(bytes, 0, slice, 0, MaxFileBytes);
			bytes = slice;
			truncated = true;
		}
		string text;
		try { text = Encoding.UTF8.GetString(bytes); }
		catch { return "error: not utf-8 text"; }
		if (truncated)
			text += $"\n…(truncated at {MaxFileBytes} bytes, file={rel})";
		return text;
	}

	static string writefile(JsonElement args) {
		var path = str(args, "path");
		if (path.Length == 0) return "error: path required";
		if (!args.TryGetProperty("content", out var cEl))
			return "error: content required";
		var content = cEl.ValueKind == JsonValueKind.String ? (cEl.GetString() ?? "") : cEl.GetRawText();
		var bytes = Encoding.UTF8.GetBytes(content);
		if (bytes.Length > MaxFileBytes)
			return $"error: content too large ({bytes.Length} > {MaxFileBytes})";
		if (!LlmAgentPaths.TryResolve(path, out var full, out var rel, out var err))
			return "error: " + err;
		try {
			var dir = Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			File.WriteAllBytes(full, bytes);
		}
		catch (Exception ex) {
			return "error: " + ex.Message;
		}
		return $"ok wrote {rel} ({bytes.Length} bytes)";
	}

	static string runscript(JsonElement args, CancellationToken ct) {
		var path = str(args, "path");
		if (path.Length == 0) return "error: path required";
		if (!LlmAgentPaths.TryResolve(path, out var full, out var rel, out var err))
			return "error: " + err;
		if (!File.Exists(full))
			return "error: script not found: " + rel;
		var ext = Path.GetExtension(full)?.ToLowerInvariant() ?? "";
		if (ext is not ".py" and not ".ps1" and not ".bat" and not ".cmd")
			return "error: allowed extensions: .py .ps1 .bat .cmd";
		var argList = parseargs(args);
		string fileName;
		string arguments;
		if (ext == ".py") {
			fileName = "python";
			arguments = "-X utf8 \"" + full + "\"" + argsuffix(argList);
		}
		else if (ext == ".ps1") {
			fileName = "powershell";
			arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + full + "\"" + argsuffix(argList);
		}
		else {
			fileName = full;
			arguments = argjoin(argList);
		}
		ct.ThrowIfCancellationRequested();
		var psi = new ProcessStartInfo {
			FileName = fileName,
			Arguments = arguments,
			WorkingDirectory = LlmAgentPaths.Root,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};
		Process proc = null;
		try {
			proc = Process.Start(psi);
			if (proc == null) return "error: failed to start process";
			lock (gate) running = proc;
			var stdout = new StringBuilder();
			var stderr = new StringBuilder();
			proc.OutputDataReceived += (_, e) => {
				if (e.Data != null) appendcap(stdout, e.Data);
			};
			proc.ErrorDataReceived += (_, e) => {
				if (e.Data != null) appendcap(stderr, e.Data);
			};
			proc.BeginOutputReadLine();
			proc.BeginErrorReadLine();
			var t0 = Environment.TickCount;
			while (!proc.HasExited) {
				ct.ThrowIfCancellationRequested();
				if (unchecked(Environment.TickCount - t0) >= ScriptTimeoutMs) {
					try { proc.Kill(); } catch { }
					return $"error: timeout after {ScriptTimeoutMs}ms\nstdout:\n{stdout}\nstderr:\n{stderr}";
				}
				Thread.Sleep(40);
			}
			proc.WaitForExit(1000);
			var code = proc.ExitCode;
			var sb = new StringBuilder();
			sb.Append("exit=").Append(code).Append(" script=").Append(rel).Append('\n');
			if (stdout.Length > 0) sb.Append("stdout:\n").Append(stdout).Append('\n');
			if (stderr.Length > 0) sb.Append("stderr:\n").Append(stderr).Append('\n');
			return sb.ToString().TrimEnd();
		}
		catch (OperationCanceledException) {
			try { proc?.Kill(); } catch { }
			throw;
		}
		catch (Exception ex) {
			return "error: " + ex.Message;
		}
		finally {
			lock (gate) {
				if (running == proc) running = null;
			}
			try { proc?.Dispose(); } catch { }
		}
	}

	static void appendcap(StringBuilder sb, string line) {
		if (sb.Length >= MaxScriptOut) return;
		sb.AppendLine(line);
		if (sb.Length > MaxScriptOut) {
			sb.Length = MaxScriptOut;
			sb.Append("…(truncated)");
		}
	}

	static List<string> parseargs(JsonElement args) {
		var list = new List<string>();
		if (!args.TryGetProperty("args", out var a)) return list;
		if (a.ValueKind == JsonValueKind.Array) {
			foreach (var x in a.EnumerateArray()) {
				if (x.ValueKind == JsonValueKind.String) list.Add(x.GetString() ?? "");
				else list.Add(x.GetRawText());
			}
			return list;
		}
		if (a.ValueKind == JsonValueKind.String) {
			var s = (a.GetString() ?? "").Trim();
			if (s.Length == 0) return list;
			foreach (Match m in Regex.Matches(s, @"[^\s""]+|""[^""]*""")) {
				var t = m.Value;
				if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"')
					t = t.Substring(1, t.Length - 2);
				list.Add(t);
			}
		}
		return list;
	}

	static string argsuffix(List<string> args) {
		if (args == null || args.Count == 0) return "";
		return " " + argjoin(args);
	}

	static string argjoin(List<string> args) {
		if (args == null || args.Count == 0) return "";
		var sb = new StringBuilder();
		foreach (var a in args) {
			if (sb.Length > 0) sb.Append(' ');
			if (string.IsNullOrEmpty(a)) { sb.Append("\"\""); continue; }
			if (a.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0)
				sb.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
			else
				sb.Append(a);
		}
		return sb.ToString();
	}

	static string httpget(string url, int timeoutMs, CancellationToken ct) {
		using var handler = HttpProxy.CreateHandler();
		using var http = new HttpClient(handler) {
			Timeout = TimeSpan.FromMilliseconds(timeoutMs),
		};
		using var req = new HttpRequestMessage(HttpMethod.Get, url);
		req.Headers.TryAddWithoutValidation("User-Agent",
			"Mozilla/5.0 (compatible; ScreenKit/1.0; +local-agent)");
		using var resp = http.SendAsync(req, ct).GetAwaiter().GetResult();
		var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
		if (!resp.IsSuccessStatusCode)
			throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
		return body;
	}

	static string str(JsonElement args, string name, string fallback = "") {
		if (!args.TryGetProperty(name, out var el)) return fallback;
		if (el.ValueKind == JsonValueKind.String) return (el.GetString() ?? "").Trim();
		if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
		return fallback;
	}

	static int intval(JsonElement args, string name, int fallback) {
		if (!args.TryGetProperty(name, out var el)) return fallback;
		if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
		if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n)) return n;
		return fallback;
	}

	static string plain(string html) {
		if (string.IsNullOrEmpty(html)) return "";
		var s = decode(TagRe.Replace(html, " "));
		s = Regex.Replace(s, @"\s+", " ").Trim();
		return s;
	}

	static string decode(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		return System.Net.WebUtility.HtmlDecode(s);
	}

	static string clip(string s, int max) {
		s = s ?? "";
		if (s.Length <= max) return s;
		return s.Substring(0, max) + "…";
	}
}
