using System.IO;
using System.Text;
using System.Text.Json;

namespace ScreenKit;

/// <summary>最近 1000 次 LLM HTTP 请求。exe 旁 log/llm-calls.jsonl。不含 API Key。</summary>
static class LlmCalls {
	public const int CAP = 1000;
	const int BODYCAP = 200_000;

	static readonly object gate = new();
	static readonly List<LlmCall> rows = new();
	static string path;
	static bool loaded;
	static long seq;

	public static event Action Changed;

	public static void Add(string url, string requestJson, int code, int ms, string response, Exception error) {
		var row = new LlmCall {
			Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			Model = readmodel(requestJson),
			Url = safeurl(url),
			Code = code,
			Ms = ms < 0 ? 0 : ms,
			Request = clip(requestJson),
			Response = clip(response),
			Error = error == null ? "" : clip(error.Message),
		};
		readusage(response, out var prompt, out var completion, out var total);
		row.Prompt = prompt;
		row.Completion = completion;
		row.Total = total;
		lock (gate) {
			ensureloaded();
			row.Id = ++seq;
			rows.Add(row);
			try { append(row); } catch { }
			if (rows.Count > CAP) {
				rows.RemoveRange(0, rows.Count - CAP);
				try { rewrite(); } catch { }
			}
		}
		try { Changed?.Invoke(); } catch { }
	}

	public static LlmCall[] Latest() {
		lock (gate) {
			ensureloaded();
			var n = rows.Count;
			var a = new LlmCall[n];
			for (var i = 0; i < n; i++) a[i] = rows[n - 1 - i];
			return a;
		}
	}

	/// <summary>测试用：改写入文件并清空内存。</summary>
	internal static void UseFile(string file) {
		lock (gate) {
			path = file;
			loaded = true;
			rows.Clear();
			seq = 0;
			try {
				var dir = Path.GetDirectoryName(file);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
				if (File.Exists(file)) File.Delete(file);
			}
			catch { }
		}
	}

	static void ensureloaded() {
		if (loaded) return;
		loaded = true;
		ensurepath();
		if (!File.Exists(path)) return;
		foreach (var line in File.ReadLines(path)) {
			if (line.Length == 0) continue;
			try {
				var row = JsonSerializer.Deserialize<LlmCall>(line);
				if (row == null) continue;
				rows.Add(row);
				if (row.Id > seq) seq = row.Id;
			}
			catch { }
		}
		if (rows.Count > CAP) rows.RemoveRange(0, rows.Count - CAP);
	}

	static void ensurepath() {
		if (path != null) return;
		var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
		try { Directory.CreateDirectory(dir); } catch { }
		path = Path.Combine(dir, "llm-calls.jsonl");
	}

	static void append(LlmCall row) {
		ensurepath();
		File.AppendAllText(path, JsonSerializer.Serialize(row) + "\n", new UTF8Encoding(false));
	}

	static void rewrite() {
		ensurepath();
		var tmp = path + ".tmp";
		using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false))) {
			foreach (var row in rows)
				w.WriteLine(JsonSerializer.Serialize(row));
		}
		if (File.Exists(path)) File.Delete(path);
		File.Move(tmp, path);
	}

	static string readmodel(string json) {
		if (string.IsNullOrEmpty(json)) return "";
		try {
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
				return m.GetString() ?? "";
		}
		catch { }
		return "";
	}

	static void readusage(string json, out int prompt, out int completion, out int total) {
		prompt = -1;
		completion = -1;
		total = -1;
		if (string.IsNullOrEmpty(json)) return;
		try {
			using var doc = JsonDocument.Parse(json);
			if (!doc.RootElement.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
				return;
			prompt = num(u, "prompt_tokens");
			if (prompt < 0) prompt = num(u, "input_tokens");
			completion = num(u, "completion_tokens");
			if (completion < 0) completion = num(u, "output_tokens");
			total = num(u, "total_tokens");
			if (total < 0 && prompt >= 0 && completion >= 0) total = prompt + completion;
		}
		catch { }
	}

	static int num(JsonElement o, string name) {
		if (!o.TryGetProperty(name, out var v)) return -1;
		if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
		return -1;
	}

	static string safeurl(string url) {
		url = (url ?? "").Trim();
		var i = url.IndexOf('?');
		if (i >= 0) url = url.Substring(0, i);
		return url;
	}

	static string clip(string s) {
		s = s ?? "";
		if (s.Length <= BODYCAP) return s;
		return s.Substring(0, BODYCAP) + "\n…";
	}
}

sealed class LlmCall {
	public long Id { get; set; }
	public string Time { get; set; } = "";
	public string Model { get; set; } = "";
	public string Url { get; set; } = "";
	public int Code { get; set; }
	public int Ms { get; set; }
	public int Prompt { get; set; } = -1;
	public int Completion { get; set; } = -1;
	public int Total { get; set; } = -1;
	public string Request { get; set; } = "";
	public string Response { get; set; } = "";
	public string Error { get; set; } = "";
}
