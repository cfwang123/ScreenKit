using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>
/// x64 客户端：按需启动同目录 <c>x86host.exe</c>（独立 32 位 SAPI Web），拉发音人 / 合成 WAV。
/// 服务空闲 60s 自关；下次调用再拉起。
/// </summary>
static class SapiX86Client {
	public const int DefaultPort = 17886;
	public const int DefaultIdleMs = 60_000;
	const int StartWaitMs = 12_000;
	const int HttpTimeoutMs = 120_000;

	static readonly object Gate = new();
	static readonly HttpClient Http = createhttp();
	static int cachedPort;

	/// <summary>状态文件：与 x86host 约定，写在主程序目录 log/ 下。</summary>
	public static string StatePath =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "sapi_x86_server.json");

	static HttpClient createhttp() {
		var c = new HttpClient { Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs) };
		return c;
	}

	/// <summary>同目录 x86host.exe；不存在返回 null。</summary>
	public static string FindExe() {
		var baseDir = AppDomain.CurrentDomain.BaseDirectory;
		var cands = new[] {
			Path.Combine(baseDir, "x86host.exe"),
			Path.Combine(baseDir, "x86host", "x86host.exe"),
			Path.Combine(baseDir, "x86", "x86host.exe"),
		};
		foreach (var c in cands) {
			try {
				if (File.Exists(c)) return Path.GetFullPath(c);
			}
			catch { }
		}
		return null;
	}

	public static bool ExeAvailable => !string.IsNullOrEmpty(FindExe());

	/// <summary>确保服务可用，返回 base URL。</summary>
	public static string EnsureServer() {
		lock (Gate) {
			if (tryprobe(out var port)) {
				cachedPort = port;
				return baseurl(port);
			}
			var exe = FindExe();
			if (string.IsNullOrEmpty(exe))
				throw new InvalidOperationException(
					"未找到 x86host.exe。请编译: dotnet build x86host/x86host.csproj -c Release");

			var psi = new ProcessStartInfo {
				FileName = exe,
				Arguments = $"--port {DefaultPort}",
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = Path.GetDirectoryName(exe) ?? AppDomain.CurrentDomain.BaseDirectory,
			};
			Process p;
			try {
				p = Process.Start(psi);
			}
			catch (Exception ex) {
				throw new InvalidOperationException("启动 x86host.exe 失败: " + ex.Message, ex);
			}
			if (p == null)
				throw new InvalidOperationException("启动 x86host.exe 失败（Process.Start 返回 null）");

			var t0 = Environment.TickCount;
			while (unchecked(Environment.TickCount - t0) < StartWaitMs) {
				if (tryprobe(out port)) {
					cachedPort = port;
					return baseurl(port);
				}
				if (p.HasExited) {
					if (tryprobe(out port)) {
						cachedPort = port;
						return baseurl(port);
					}
					throw new InvalidOperationException(
						$"x86host.exe 已退出 exit={p.ExitCode}，SAPI x86 服务未就绪");
				}
				Thread.Sleep(150);
			}
			throw new InvalidOperationException("等待 x86host SAPI 服务超时");
		}
	}

	/// <summary>枚举 x86 可见的 SAPI 发音人。</summary>
	public static IReadOnlyList<SapiVoiceItem> ListVoices() {
		var url = EnsureServer() + "/api/sapi/voices";
		var json = Http.GetStringAsync(url).GetAwaiter().GetResult();
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
			throw new InvalidOperationException(root.TryGetProperty("error", out var e) ? e.GetString() : "list failed");

		var list = new List<SapiVoiceItem>();
		if (!root.TryGetProperty("voices", out var arr) || arr.ValueKind != JsonValueKind.Array)
			return list;
		foreach (var v in arr.EnumerateArray()) {
			var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
			if (string.IsNullOrWhiteSpace(name)) continue;
			var culture = v.TryGetProperty("culture", out var c) ? c.GetString() ?? "" : "";
			var lang = v.TryGetProperty("lang", out var lg) ? lg.GetString() ?? "" : "";
			if (string.IsNullOrEmpty(lang))
				lang = SapiVoiceItem.LangOf(culture);
			var gender = v.TryGetProperty("gender", out var g) ? g.GetString() ?? "" : "";
			var gLabel = TtsGender.Label(gender);
			var tail = string.IsNullOrEmpty(culture) ? "" : " · " + culture;
			if (!string.IsNullOrEmpty(gLabel)) tail += " · " + gLabel;
			tail += " · x86";
			list.Add(new SapiVoiceItem {
				DisplayName = name + tail,
				Key = "sapi-x86:" + name,
				Name = name,
				Culture = culture,
				Lang = lang,
				Gender = gender,
				Source = "sapi-x86",
			});
		}
		return list;
	}

	public static byte[] SynthWav(string text, string voiceName, int rate, int volume) {
		if (string.IsNullOrWhiteSpace(text))
			throw new ArgumentException("文本为空");
		var url = EnsureServer() + "/api/sapi/synth";
		var payload = JsonSerializer.Serialize(new {
			text,
			voice = voiceName ?? "",
			rate,
			volume,
		});
		using var content = new StringContent(payload, Encoding.UTF8, "application/json");
		using var resp = Http.PostAsync(url, content).GetAwaiter().GetResult();
		var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
		if (!resp.IsSuccessStatusCode) {
			var msg = Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>());
			throw new InvalidOperationException($"x86host 合成失败 HTTP {(int)resp.StatusCode}: {msg}");
		}
		var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
		if (ct.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
			throw new InvalidOperationException("x86host 合成失败: " + Encoding.UTF8.GetString(bytes));
		if (bytes == null || bytes.Length < 100)
			throw new InvalidOperationException("x86host 合成结果无效");
		return bytes;
	}

	public static string SynthToWavFile(string text, string voiceName, int rate, int volume) {
		var bytes = SynthWav(text, voiceName, rate, volume);
		var path = TmpStore.NewPath("sapi_x86_cli", ".wav");
		File.WriteAllBytes(path, bytes);
		return path;
	}

	public static (float[] samples, int sampleRate) SynthToFloat(string text, string voiceName, int rate, int volume) {
		var wav = SynthToWavFile(text, voiceName, rate, volume);
		try {
			using var reader = new AudioFileReader(wav);
			var sr = reader.WaveFormat.SampleRate;
			var list = new List<float>();
			var buf = new float[4096];
			int n;
			while ((n = reader.Read(buf, 0, buf.Length)) > 0) {
				for (var k = 0; k < n; k++) list.Add(buf[k]);
			}
			return (list.ToArray(), sr);
		}
		finally {
			try { if (File.Exists(wav)) File.Delete(wav); } catch { }
		}
	}

	static string baseurl(int port) => $"http://127.0.0.1:{port}";

	static bool tryprobe(out int port) {
		port = 0;
		if (cachedPort > 0 && ping(cachedPort)) {
			port = cachedPort;
			return true;
		}
		try {
			if (File.Exists(StatePath)) {
				var json = File.ReadAllText(StatePath, Encoding.UTF8);
				using var doc = JsonDocument.Parse(json);
				if (doc.RootElement.TryGetProperty("port", out var p) && p.TryGetInt32(out var sp) && sp > 0) {
					if (ping(sp)) {
						port = sp;
						return true;
					}
				}
			}
		}
		catch { }
		if (ping(DefaultPort)) {
			port = DefaultPort;
			return true;
		}
		return false;
	}

	static bool ping(int port) {
		try {
			using var cts = new CancellationTokenSource(1500);
			var url = $"http://127.0.0.1:{port}/api/sapi/status";
			using var resp = Http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
			return resp.IsSuccessStatusCode;
		}
		catch {
			return false;
		}
	}
}
