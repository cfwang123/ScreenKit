using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace X86Host;

/// <summary>
/// 独立 32 位进程：仅提供 SAPI Web 服务（发音人列表 / 合成 WAV）。
/// 无第三方包依赖，可与 ScreenKit.exe 同目录单文件旁路运行。
/// 默认空闲 60 秒无请求后自动退出。
/// </summary>
static class Program {
	public const int DefaultPort = 17886;
	public const int DefaultIdleMs = 60_000;
	public const string MutexName = "Local\\ScreenKit_SapiX86Server";

	static string StatePath =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "sapi_x86_server.json");

	static readonly JavaScriptSerializer Json = new() { MaxJsonLength = 20_000_000 };

	[STAThread]
	static int Main(string[] args) {
		args ??= Array.Empty<string>();
		if (hasflag(args, "--help") || hasflag(args, "-h") || hasflag(args, "/?")) {
			printhelp();
			return 0;
		}
		if (hasflag(args, "--list-sapi"))
			return listsapi();
		return runserver(args);
	}

	static bool hasflag(string[] args, string flag) {
		foreach (var a in args) {
			if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	static void printhelp() {
		Console.WriteLine("""
x86host — 32 位 SAPI Web（仅此功能，无 GUI）

用法:
  x86host.exe
  x86host.exe --port 17886
  x86host.exe --idle-ms 60000
  x86host.exe --list-sapi
  x86host.exe --help

Web（仅 127.0.0.1，默认空闲 60s 退出）:
  GET  /api/sapi/status
  GET  /api/sapi/voices
  POST /api/sapi/synth   JSON { "text","voice","rate","volume" } → audio/wav
  POST /api/sapi/shutdown
""");
	}

	static int listsapi() {
		try {
			using var syn = new SpeechSynthesizer();
			var voices = syn.GetInstalledVoices()
				.Where(v => v.Enabled)
				.Select(v => v.VoiceInfo)
				.ToList();
			Console.WriteLine("=== SAPI（x86host）===");
			Console.WriteLine($"Is64BitProcess={Environment.Is64BitProcess} Count={voices.Count}");
			foreach (var v in voices)
				Console.WriteLine($"  {v.Name}  culture={v.Culture?.Name} gender={v.Gender} age={v.Age}");
			return voices.Count > 0 ? 0 : 1;
		}
		catch (Exception ex) {
			Console.Error.WriteLine(ex.Message);
			return 1;
		}
	}

	static int runserver(string[] args) {
		var port = DefaultPort;
		var idleMs = DefaultIdleMs;
		try {
			for (var i = 0; i < args.Length; i++) {
				var a = args[i];
				if (a is "--port" or "-port") {
					if (i + 1 < args.Length && int.TryParse(args[++i], out var p))
						port = clamp(p, 1, 65535);
				}
				else if (a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase)
					&& int.TryParse(a.Substring("--port=".Length), out var p2))
					port = clamp(p2, 1, 65535);
				else if (a is "--idle-ms" or "--idle") {
					if (i + 1 < args.Length && int.TryParse(args[++i], out var idle))
						idleMs = clamp(idle, 5_000, 3_600_000);
				}
			}
		}
		catch { }

		if (Environment.Is64BitProcess) {
			Console.Error.WriteLine("x86host 必须编译为 32 位（PlatformTarget=x86）。");
			return 2;
		}

		if (tryexisting(out var existPort)) {
			Console.WriteLine($"SAPI 服务已在运行 port={existPort}");
			return 0;
		}

		Mutex mtx = null;
		try {
			mtx = new Mutex(true, MutexName, out var created);
			if (!created) {
				Thread.Sleep(200);
				if (tryexisting(out existPort)) {
					Console.WriteLine($"SAPI 服务已在运行 port={existPort}");
					return 0;
				}
				Console.Error.WriteLine("无法获取服务锁");
				return 3;
			}
		}
		catch (Exception ex) {
			Console.Error.WriteLine("Mutex 失败: " + ex.Message);
			return 3;
		}

		HttpListener listener = null;
		var lastreq = Environment.TickCount;
		var stop = new ManualResetEvent(false);
		SpeechSynthesizer syn = null;

		try {
			syn = new SpeechSynthesizer();
			try { syn.SetOutputToDefaultAudioDevice(); } catch { }

			var bound = 0;
			Exception lastBind = null;
			for (var tryPort = port; tryPort < port + 20 && tryPort <= 65535; tryPort++) {
				try {
					var l = new HttpListener();
					l.Prefixes.Add($"http://127.0.0.1:{tryPort}/");
					l.Start();
					listener = l;
					bound = tryPort;
					break;
				}
				catch (Exception ex) {
					lastBind = ex;
					try { listener?.Close(); } catch { }
					listener = null;
				}
			}
			if (listener == null || bound == 0) {
				Console.Error.WriteLine("监听失败: " + (lastBind?.Message ?? "unknown"));
				return 4;
			}

			writestate(bound, Process.GetCurrentProcess().Id);
			Console.WriteLine($"x86host SAPI Web http://127.0.0.1:{bound}/ idle={idleMs}ms");

			_ = Task.Run(() => idleloop(idleMs, () => lastreq, stop, listener));

			while (!stop.WaitOne(0)) {
				HttpListenerContext ctx;
				try {
					ctx = listener.GetContext();
				}
				catch (HttpListenerException) { break; }
				catch (ObjectDisposedException) { break; }
				catch {
					if (stop.WaitOne(0)) break;
					continue;
				}
				lastreq = Environment.TickCount;
				try {
					handle(ctx, syn, idleMs, () => lastreq = Environment.TickCount, stop);
				}
				catch (Exception ex) {
					try { writejson(ctx, 500, err(ex.Message)); } catch { }
				}
			}
			return 0;
		}
		finally {
			try { stop.Set(); } catch { }
			try { listener?.Abort(); } catch { }
			try { listener?.Close(); } catch { }
			try { syn?.Dispose(); } catch { }
			try { clearstate(); } catch { }
			try {
				mtx?.ReleaseMutex();
				mtx?.Dispose();
			}
			catch { }
		}
	}

	static void idleloop(int idleMs, Func<int> getLast, ManualResetEvent stop, HttpListener listener) {
		while (!stop.WaitOne(500)) {
			var elapsed = unchecked(Environment.TickCount - getLast());
			if (elapsed >= idleMs) {
				Console.WriteLine($"空闲 {idleMs}ms，退出");
				stop.Set();
				try { listener.Abort(); } catch { }
				return;
			}
		}
	}

	static void handle(HttpListenerContext ctx, SpeechSynthesizer syn, int idleMs, Action touch, ManualResetEvent stop) {
		var req = ctx.Request;
		if (string.Equals(req.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase)) {
			writecors(ctx, 204);
			return;
		}

		var path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
		if (path.Length == 0) path = "/";

		if (path is "/api/sapi/status" or "/api/status" or "/api") {
			if (!isget(req)) { writejson(ctx, 405, err("仅支持 GET")); return; }
			touch();
			writejson(ctx, 200, new Dictionary<string, object> {
				["ok"] = true,
				["service"] = "x86host",
				["arch"] = "x86",
				["idleLimitMs"] = idleMs,
				["pid"] = Process.GetCurrentProcess().Id,
			});
			return;
		}

		if (path is "/api/sapi/voices" or "/api/sapi/list") {
			if (!isget(req)) { writejson(ctx, 405, err("仅支持 GET")); return; }
			touch();
			writejson(ctx, 200, buildvoices(syn));
			return;
		}

		if (path is "/api/sapi/synth" or "/api/sapi/speak") {
			if (!ispost(req)) { writejson(ctx, 405, err("仅支持 POST")); return; }
			touch();
			handlesynth(ctx, syn);
			return;
		}

		if (path is "/api/sapi/shutdown") {
			writejson(ctx, 200, new Dictionary<string, object> { ["ok"] = true, ["shutdown"] = true });
			stop.Set();
			try { ctx.Response.Close(); } catch { }
			Environment.Exit(0);
			return;
		}

		if (path is "/" or "/help") {
			writejson(ctx, 200, new Dictionary<string, object> {
				["service"] = "x86host SAPI",
				["endpoints"] = new[] {
					"GET /api/sapi/status",
					"GET /api/sapi/voices",
					"POST /api/sapi/synth",
					"POST /api/sapi/shutdown",
				},
			});
			return;
		}

		writejson(ctx, 404, err("not found: " + path));
	}

	static void handlesynth(HttpListenerContext ctx, SpeechSynthesizer syn) {
		Dictionary<string, object> body;
		try {
			body = readjson(ctx.Request);
		}
		catch (Exception ex) {
			writejson(ctx, 400, err("JSON 无效: " + ex.Message));
			return;
		}

		var text = getstr(body, "text") ?? getstr(body, "content") ?? "";
		if (string.IsNullOrWhiteSpace(text)) {
			writejson(ctx, 400, err("text 为空"));
			return;
		}
		var voice = getstr(body, "voice") ?? getstr(body, "name") ?? "";
		var rate = clamp(getint(body, "rate", 0), -10, 10);
		var volume = clamp(getint(body, "volume", 100), 0, 100);

		var tmpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
		try { Directory.CreateDirectory(tmpDir); } catch { }
		var wav = Path.Combine(tmpDir, $"sapi_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");
		try {
			if (!string.IsNullOrWhiteSpace(voice))
				syn.SelectVoice(voice);
			syn.Rate = rate;
			syn.Volume = volume;
			try { syn.SpeakAsyncCancelAll(); } catch { }
			syn.SetOutputToWaveFile(wav);
			try {
				syn.Speak(text);
			}
			finally {
				try { syn.SetOutputToDefaultAudioDevice(); } catch { }
			}
			var bytes = File.ReadAllBytes(wav);
			if (bytes.Length < 100)
				throw new InvalidOperationException("合成结果过短");
			writebytes(ctx, 200, "audio/wav", bytes);
		}
		catch (Exception ex) {
			writejson(ctx, 500, err("合成失败: " + ex.Message));
		}
		finally {
			try { if (File.Exists(wav)) File.Delete(wav); } catch { }
		}
	}

	static Dictionary<string, object> buildvoices(SpeechSynthesizer syn) {
		var arr = new List<object>();
		IEnumerable<VoiceInfo> voices;
		try {
			voices = syn.GetInstalledVoices()
				.Where(v => v.Enabled)
				.Select(v => v.VoiceInfo)
				.ToList();
		}
		catch {
			voices = Array.Empty<VoiceInfo>();
		}
		foreach (var v in voices) {
			arr.Add(new Dictionary<string, object> {
				["name"] = v.Name ?? "",
				["culture"] = v.Culture?.Name ?? "",
				["lang"] = (v.Culture?.TwoLetterISOLanguageName ?? "").ToLowerInvariant(),
				["gender"] = genderstr(v.Gender),
				["age"] = v.Age.ToString(),
			});
		}
		return new Dictionary<string, object> {
			["ok"] = true,
			["service"] = "x86host",
			["arch"] = "x86",
			["count"] = arr.Count,
			["voices"] = arr,
		};
	}

	static string genderstr(VoiceGender g) => g switch {
		VoiceGender.Female => "female",
		VoiceGender.Male => "male",
		_ => "",
	};

	static bool tryexisting(out int port) {
		port = 0;
		try {
			if (!File.Exists(StatePath)) return false;
			var json = File.ReadAllText(StatePath, Encoding.UTF8);
			// 轻量解析 port / pid
			var pm = Regex.Match(json, "\"port\"\\s*:\\s*(\\d+)");
			if (!pm.Success || !int.TryParse(pm.Groups[1].Value, out port))
				return false;
			var pid = 0;
			var pidm = Regex.Match(json, "\"pid\"\\s*:\\s*(\\d+)");
			if (pidm.Success) int.TryParse(pidm.Groups[1].Value, out pid);
			if (pid > 0) {
				try {
					var proc = Process.GetProcessById(pid);
					if (proc.HasExited) return false;
				}
				catch {
					return false;
				}
			}
			try {
				var req = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{port}/api/sapi/status");
				req.Method = "GET";
				req.Timeout = 1500;
				using var resp = (HttpWebResponse)req.GetResponse();
				if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300)
					return true;
			}
			catch {
				return false;
			}
		}
		catch {
			return false;
		}
		return false;
	}

	static void writestate(int port, int pid) {
		var dir = Path.GetDirectoryName(StatePath);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		var json = Json.Serialize(new Dictionary<string, object> {
			["port"] = port,
			["pid"] = pid,
			["arch"] = "x86",
			["service"] = "x86host",
			["started"] = DateTime.Now.ToString("o"),
		});
		File.WriteAllText(StatePath, json, new UTF8Encoding(false));
	}

	static void clearstate() {
		try {
			if (File.Exists(StatePath)) File.Delete(StatePath);
		}
		catch { }
	}

	static int clamp(int v, int min, int max) =>
		v < min ? min : (v > max ? max : v);

	static bool isget(HttpListenerRequest req) =>
		string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);

	static bool ispost(HttpListenerRequest req) =>
		string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

	static Dictionary<string, object> readjson(HttpListenerRequest req) {
		using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
		var s = sr.ReadToEnd();
		if (string.IsNullOrWhiteSpace(s)) return new Dictionary<string, object>();
		var obj = Json.DeserializeObject(s);
		if (obj is Dictionary<string, object> d) return d;
		// JavaScriptSerializer 可能返回 Dictionary<string, object> 嵌套
		if (obj is IDictionary<string, object> id) {
			var r = new Dictionary<string, object>();
			foreach (var kv in id) r[kv.Key] = kv.Value;
			return r;
		}
		return new Dictionary<string, object>();
	}

	static string getstr(Dictionary<string, object> d, string key) {
		if (d == null || !d.TryGetValue(key, out var v) || v == null) return null;
		return Convert.ToString(v);
	}

	static int getint(Dictionary<string, object> d, string key, int def) {
		if (d == null || !d.TryGetValue(key, out var v) || v == null) return def;
		try {
			if (v is int i) return i;
			if (v is long l) return (int)l;
			if (v is double db) return (int)Math.Round(db);
			if (v is decimal m) return (int)Math.Round(m);
			if (int.TryParse(Convert.ToString(v), out var n)) return n;
			if (double.TryParse(Convert.ToString(v), out var f)) return (int)Math.Round(f);
		}
		catch { }
		return def;
	}

	static Dictionary<string, object> err(string msg) => new() {
		["ok"] = false,
		["error"] = msg ?? "",
	};

	static void writecors(HttpListenerContext ctx, int status) {
		var res = ctx.Response;
		res.StatusCode = status;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
		res.Headers["Access-Control-Allow-Headers"] = "Content-Type";
		res.ContentLength64 = 0;
		try { res.Close(); } catch { }
	}

	static void writejson(HttpListenerContext ctx, int status, object body) {
		var bytes = Encoding.UTF8.GetBytes(Json.Serialize(body));
		var res = ctx.Response;
		res.StatusCode = status;
		res.ContentType = "application/json; charset=utf-8";
		res.ContentEncoding = Encoding.UTF8;
		res.ContentLength64 = bytes.Length;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		try {
			res.OutputStream.Write(bytes, 0, bytes.Length);
		}
		finally {
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
	}

	static void writebytes(HttpListenerContext ctx, int status, string contentType, byte[] bytes) {
		var res = ctx.Response;
		res.StatusCode = status;
		res.ContentType = contentType ?? "application/octet-stream";
		res.ContentLength64 = bytes.Length;
		res.Headers["Access-Control-Allow-Origin"] = "*";
		try {
			res.OutputStream.Write(bytes, 0, bytes.Length);
		}
		finally {
			try { res.OutputStream.Close(); } catch { }
			try { res.Close(); } catch { }
		}
	}
}
