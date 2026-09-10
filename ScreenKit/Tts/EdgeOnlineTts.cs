using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace ScreenKit;

/// <summary>Microsoft Edge 在线自然语音（非 Azure 正式 API，无密钥，需联网）。</summary>
sealed class EdgeOnlineTts : IDisposable {
	const string TOKEN = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
	const string BASE_URL = "speech.platform.bing.com/consumer/speech/synthesize/readaloud";
	static readonly object CacheGate = new();
	static readonly SemaphoreSlim VoiceLoadGate = new(1, 1);
	static List<SapiVoiceItem> CachedVoices = new();
	static int lastVoiceLoad;
	static long clockSkewSeconds;

	string voice = "zh-CN-XiaoxiaoNeural";
	double rate = 1;
	int volume = 100;

	public static IReadOnlyList<SapiVoiceItem> Voices {
		get {
			lock (CacheGate) return CachedVoices.ToList();
		}
	}

	public async Task<IReadOnlyList<SapiVoiceItem>> LoadVoicesAsync(CancellationToken ct, bool force = false) {
		lock (CacheGate) {
			if (!force && CachedVoices.Count > 0 && Environment.TickCount - lastVoiceLoad < 6 * 60 * 60 * 1000)
				return CachedVoices.ToList();
		}
		await VoiceLoadGate.WaitAsync(ct).ConfigureAwait(false);
		try {
			lock (CacheGate) {
				if (!force && CachedVoices.Count > 0
					&& Environment.TickCount - lastVoiceLoad < 6 * 60 * 60 * 1000)
					return CachedVoices.ToList();
			}
			var list = await loadvoicescore(ct).ConfigureAwait(false);
			lock (CacheGate) {
				CachedVoices = list;
				lastVoiceLoad = Environment.TickCount;
				return CachedVoices.ToList();
			}
		}
		finally {
			VoiceLoadGate.Release();
		}
	}

	public bool SelectVoice(string key) {
		var name = (key ?? "").Trim();
		if (name.StartsWith("edge:", StringComparison.OrdinalIgnoreCase))
			name = name.Substring(5);
		if (string.IsNullOrEmpty(name)) return false;
		var found = Voices.FirstOrDefault(v =>
			string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
		if (found == null) return false;
		voice = found.Name;
		return true;
	}

	public void SetRateVolume(double rate, int volume) {
		this.rate = Compat.Clamp(rate, 0.5, 2.0);
		this.volume = Compat.Clamp(volume, 0, 100);
	}

	public async Task<(float[] samples, int sampleRate)> Synthesize(string text, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(text)) return (Array.Empty<float>(), 24000);
		var parts = new List<(float[] samples, int sampleRate)>();
		foreach (var chunk in splittext(cleantext(text), 4000)) {
			var mp3 = await synthmp3(chunk, ct).ConfigureAwait(false);
			parts.Add(mp3tofloat(mp3));
		}
		if (parts.Count == 0) return (Array.Empty<float>(), 24000);
		var sr = parts[0].sampleRate;
		if (parts.Any(p => p.sampleRate != sr))
			throw new InvalidOperationException("Edge 在线语音返回了不一致的采样率");
		var total = parts.Sum(p => p.samples.Length);
		var samples = new float[total];
		var offset = 0;
		foreach (var part in parts) {
			Array.Copy(part.samples, 0, samples, offset, part.samples.Length);
			offset += part.samples.Length;
		}
		return (samples, sr);
	}

	static async Task<List<SapiVoiceItem>> loadvoicescore(CancellationToken ct) {
		for (var attempt = 0; attempt < 2; attempt++) {
			var version = edgeversion();
			var url = $"https://{BASE_URL}/voices/list?trustedclienttoken={TOKEN}"
				+ $"&Sec-MS-GEC={makegec()}&Sec-MS-GEC-Version=1-{version}";
			using var handler = HttpProxy.CreateHandler();
			using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
			using var req = new HttpRequestMessage(HttpMethod.Get, url);
			addheaders(req.Headers, version);
			using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
			if (resp.StatusCode == HttpStatusCode.Forbidden && attempt == 0 && adjustclock(resp.Headers.Date))
				continue;
			resp.EnsureSuccessStatusCode();
			var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
			using var doc = JsonDocument.Parse(json);
			var list = new List<SapiVoiceItem>();
			foreach (var item in doc.RootElement.EnumerateArray()) {
				var name = jsonstr(item, "ShortName");
				var locale = jsonstr(item, "Locale");
				if (string.IsNullOrEmpty(name)) continue;
				var gender = TtsGender.Normalize(jsonstr(item, "Gender"));
				var simpleName = name;
				if (!string.IsNullOrEmpty(locale) && simpleName.StartsWith(locale + "-", StringComparison.OrdinalIgnoreCase))
					simpleName = simpleName.Substring(locale.Length + 1);
				if (simpleName.EndsWith("Neural", StringComparison.OrdinalIgnoreCase))
					simpleName = simpleName.Substring(0, simpleName.Length - 6);
				var tail = string.IsNullOrEmpty(locale) ? "" : " · " + locale;
				var genderLabel = TtsGender.Label(gender);
				if (!string.IsNullOrEmpty(genderLabel)) tail += " · " + genderLabel;
				list.Add(new SapiVoiceItem {
					DisplayName = simpleName + tail,
					Key = "edge:" + name,
					Name = name,
					Culture = locale,
					Lang = SapiVoiceItem.LangOf(locale),
					Gender = gender,
					Source = "edge",
				});
			}
			return list.OrderBy(v => v.Culture).ThenBy(v => v.Name).ToList();
		}
		throw new InvalidOperationException("Edge 在线语音目录请求失败");
	}

	async Task<byte[]> synthmp3(string text, CancellationToken ct) {
		var version = edgeversion();
		var connectionId = Guid.NewGuid().ToString("N");
		var url = $"wss://{BASE_URL}/edge/v1?TrustedClientToken={TOKEN}"
			+ $"&ConnectionId={connectionId}&Sec-MS-GEC={makegec()}&Sec-MS-GEC-Version=1-{version}";
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(60));
		using var ws = new EdgeWebSocket();
		await ws.ConnectAsync(
			new Uri(url), useragent(version),
			"chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold",
			"muid=" + Guid.NewGuid().ToString("N").ToUpperInvariant() + ";",
			timeout.Token).ConfigureAwait(false);

		var timestamp = timestamptext();
		var config = $"X-Timestamp:{timestamp}\r\n"
			+ "Content-Type:application/json; charset=utf-8\r\n"
			+ "Path:speech.config\r\n\r\n"
			+ "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":"
			+ "{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},"
			+ "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";
		await ws.SendTextAsync(config, timeout.Token).ConfigureAwait(false);

		var ratePercent = (int)Math.Round((rate - 1) * 100);
		var volumePercent = volume - 100;
		var ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>"
			+ $"<voice name='{SecurityElement.Escape(voice)}'><prosody pitch='+0Hz' "
			+ $"rate='{signed(ratePercent)}%' volume='{signed(volumePercent)}%'>"
			+ SecurityElement.Escape(text) + "</prosody></voice></speak>";
		var request = $"X-RequestId:{Guid.NewGuid():N}\r\n"
			+ "Content-Type:application/ssml+xml\r\n"
			+ $"X-Timestamp:{timestamp}Z\r\nPath:ssml\r\n\r\n{ssml}";
		await ws.SendTextAsync(request, timeout.Token).ConfigureAwait(false);

		using var audio = new MemoryStream();
		var done = false;
		while (!done && ws.IsOpen) {
			var (type, data) = await ws.ReceiveAsync(timeout.Token).ConfigureAwait(false);
			if (type == 8) break;
			if (type == 1) {
				var message = Encoding.UTF8.GetString(data);
				if (header(message, "Path").Equals("turn.end", StringComparison.OrdinalIgnoreCase))
					done = true;
				continue;
			}
			if (data.Length < 2) continue;
			var headerLength = (data[0] << 8) | data[1];
			var start = 2 + headerLength;
			if (headerLength < 0 || start > data.Length)
				throw new InvalidOperationException("Edge 在线语音返回了无效音频帧");
			if (start < data.Length)
				audio.Write(data, start, data.Length - start);
		}
		if (audio.Length == 0) throw new InvalidOperationException("Edge 在线语音未返回音频");
		try { await ws.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
		return audio.ToArray();
	}

	static (float[] samples, int sampleRate) mp3tofloat(byte[] mp3) {
		using var ms = new MemoryStream(mp3, false);
		using var reader = new Mp3FileReader(ms);
		var provider = reader.ToSampleProvider();
		var samples = new List<float>(mp3.Length * 2);
		var buf = new float[8192];
		int n;
		while ((n = provider.Read(buf, 0, buf.Length)) > 0) {
			for (var i = 0; i < n; i++) samples.Add(buf[i]);
		}
		if (samples.Count == 0) throw new InvalidOperationException("Edge 在线语音 MP3 无采样");
		return (samples.ToArray(), provider.WaveFormat.SampleRate);
	}

	static IEnumerable<string> splittext(string text, int maxBytes) {
		var rest = text;
		while (Encoding.UTF8.GetByteCount(rest) > maxBytes) {
			var chars = Math.Min(rest.Length, maxBytes);
			while (chars > 1 && Encoding.UTF8.GetByteCount(rest.Substring(0, chars)) > maxBytes) chars--;
			var split = rest.LastIndexOfAny(new[] { '\n', '。', '！', '？', '.', '!', '?', ' ' }, chars - 1, chars);
			if (split < chars / 2) split = chars;
			var part = rest.Substring(0, split).Trim();
			if (part.Length > 0) yield return part;
			rest = rest.Substring(split).Trim();
		}
		if (rest.Length > 0) yield return rest;
	}

	static string cleantext(string text) {
		var chars = text.ToCharArray();
		for (var i = 0; i < chars.Length; i++) {
			var code = chars[i];
			if (code <= '\b' || code is '\v' or '\f' || (code >= 14 && code <= 31))
				chars[i] = ' ';
		}
		return new string(chars);
	}

	static string edgeversion() {
		foreach (var path in new[] {
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
		}) {
			try {
				if (!File.Exists(path)) continue;
				var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
				if (!string.IsNullOrWhiteSpace(version)) return version.Split(' ')[0];
			}
			catch { }
		}
		return "143.0.3650.75";
	}

	static string makegec() {
		var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Interlocked.Read(ref clockSkewSeconds);
		unix -= unix % 300;
		var ticks = checked((unix + 11644473600L) * 10000000L);
		using var sha = SHA256.Create();
		var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(ticks.ToString(CultureInfo.InvariantCulture) + TOKEN));
		return BitConverter.ToString(hash).Replace("-", "");
	}

	static bool adjustclock(DateTimeOffset? serverDate) {
		if (serverDate == null) return false;
		Interlocked.Exchange(ref clockSkewSeconds,
			serverDate.Value.ToUnixTimeSeconds() - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
		return true;
	}

	static void addheaders(System.Net.Http.Headers.HttpRequestHeaders headers, string version) {
		headers.TryAddWithoutValidation("User-Agent", useragent(version));
		headers.TryAddWithoutValidation("Accept-Encoding", "identity");
		headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
		headers.TryAddWithoutValidation("Cookie", "muid=" + Guid.NewGuid().ToString("N").ToUpperInvariant() + ";");
	}

	static string useragent(string version) {
		var major = version.Split('.')[0];
		return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
			+ $"(KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36 Edg/{major}.0.0.0";
	}

	static string timestamptext() =>
		DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss", CultureInfo.InvariantCulture)
		+ " GMT+0000 (Coordinated Universal Time)";

	static string signed(int value) => value >= 0 ? "+" + value.ToString(CultureInfo.InvariantCulture)
		: value.ToString(CultureInfo.InvariantCulture);

	static string header(string message, string name) {
		foreach (var line in (message ?? "").Split(new[] { "\r\n" }, StringSplitOptions.None)) {
			var i = line.IndexOf(':');
			if (i <= 0 || !line.Substring(0, i).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
			return line.Substring(i + 1).Trim();
		}
		return "";
	}

	static string jsonstr(JsonElement item, string name) =>
		item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

	sealed class EdgeWebSocket : IDisposable {
		TcpClient tcp;
		Stream stream;
		bool open;

		public bool IsOpen => open;

		public async Task ConnectAsync(Uri uri, string userAgent, string origin, string cookie, CancellationToken ct) {
			tcp = new TcpClient();
			var connectUri = HttpProxy.Need(uri) ? HttpProxy.Instance.GetProxy(uri) : uri;
			using (ct.Register(() => {
				try { tcp.Close(); } catch { }
			})) {
				await tcp.ConnectAsync(connectUri.Host, connectUri.Port).ConfigureAwait(false);
				var network = tcp.GetStream();
				if (connectUri != uri) {
					var connect = $"CONNECT {uri.Host}:{uri.Port} HTTP/1.1\r\n"
						+ $"Host: {uri.Host}:{uri.Port}\r\nProxy-Connection: Keep-Alive\r\n\r\n";
					await write(network, Encoding.ASCII.GetBytes(connect), ct).ConfigureAwait(false);
					var proxyResponse = await readheaders(network, ct).ConfigureAwait(false);
					if (!proxyResponse.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)
						&& !proxyResponse.StartsWith("HTTP/1.0 200", StringComparison.OrdinalIgnoreCase))
						throw new InvalidOperationException("Edge 在线语音代理 CONNECT 失败: " + firstline(proxyResponse));
				}

				var ssl = new SslStream(network, false);
				stream = ssl;
				await ssl.AuthenticateAsClientAsync(
					uri.Host, null, SslProtocols.Tls12, checkCertificateRevocation: true).ConfigureAwait(false);
				var keyBytes = new byte[16];
				using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(keyBytes);
				var key = Convert.ToBase64String(keyBytes);
				var path = uri.PathAndQuery;
				var request = $"GET {path} HTTP/1.1\r\nHost: {uri.Host}\r\n"
					+ "Connection: Upgrade\r\nUpgrade: websocket\r\n"
					+ $"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n"
					+ $"User-Agent: {userAgent}\r\nOrigin: {origin}\r\nCookie: {cookie}\r\n"
					+ "Pragma: no-cache\r\nCache-Control: no-cache\r\n\r\n";
				await write(stream, Encoding.ASCII.GetBytes(request), ct).ConfigureAwait(false);
				var response = await readheaders(stream, ct).ConfigureAwait(false);
				if (!response.StartsWith("HTTP/1.1 101", StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException("Edge 在线语音 WebSocket 握手失败: " + firstline(response));
				var accept = header(response, "Sec-WebSocket-Accept");
				using var sha = SHA1.Create();
				var expected = Convert.ToBase64String(sha.ComputeHash(
					Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
				if (!string.Equals(accept, expected, StringComparison.Ordinal))
					throw new InvalidOperationException("Edge 在线语音 WebSocket 握手校验失败");
				open = true;
			}
		}

		public Task SendTextAsync(string text, CancellationToken ct) =>
			sendframe(1, Encoding.UTF8.GetBytes(text), ct);

		public async Task<(int type, byte[] data)> ReceiveAsync(CancellationToken ct) {
			using var output = new MemoryStream();
			var messageType = 0;
			while (true) {
				var head = await readexact(stream, 2, ct).ConfigureAwait(false);
				var final = (head[0] & 0x80) != 0;
				var type = head[0] & 0x0F;
				var masked = (head[1] & 0x80) != 0;
				ulong length = (uint)(head[1] & 0x7F);
				if (length == 126) {
					var ext = await readexact(stream, 2, ct).ConfigureAwait(false);
					length = (uint)((ext[0] << 8) | ext[1]);
				}
				else if (length == 127) {
					var ext = await readexact(stream, 8, ct).ConfigureAwait(false);
					length = 0;
					for (var i = 0; i < 8; i++) length = (length << 8) | ext[i];
				}
				if (length > int.MaxValue) throw new InvalidOperationException("Edge 在线语音 WebSocket 帧过大");
				var mask = masked ? await readexact(stream, 4, ct).ConfigureAwait(false) : null;
				var data = await readexact(stream, (int)length, ct).ConfigureAwait(false);
				if (masked) {
					for (var i = 0; i < data.Length; i++) data[i] ^= mask[i & 3];
				}
				if (type == 9) {
					await sendframe(10, data, ct).ConfigureAwait(false);
					continue;
				}
				if (type == 8) {
					open = false;
					return (8, data);
				}
				if (type is 1 or 2) messageType = type;
				if (type is 0 or 1 or 2) output.Write(data, 0, data.Length);
				if (final) return (messageType, output.ToArray());
			}
		}

		public async Task CloseAsync(CancellationToken ct) {
			if (!open) return;
			try { await sendframe(8, new byte[] { 3, 232 }, ct).ConfigureAwait(false); }
			finally { open = false; }
		}

		async Task sendframe(int type, byte[] data, CancellationToken ct) {
			data ??= Array.Empty<byte>();
			using var frame = new MemoryStream();
			frame.WriteByte((byte)(0x80 | type));
			if (data.Length < 126)
				frame.WriteByte((byte)(0x80 | data.Length));
			else if (data.Length <= ushort.MaxValue) {
				frame.WriteByte(0xFE);
				frame.WriteByte((byte)(data.Length >> 8));
				frame.WriteByte((byte)data.Length);
			}
			else {
				frame.WriteByte(0xFF);
				var len = (ulong)data.Length;
				for (var shift = 56; shift >= 0; shift -= 8)
					frame.WriteByte((byte)(len >> shift));
			}
			var mask = new byte[4];
			using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(mask);
			frame.Write(mask, 0, mask.Length);
			for (var i = 0; i < data.Length; i++)
				frame.WriteByte((byte)(data[i] ^ mask[i & 3]));
			await write(stream, frame.ToArray(), ct).ConfigureAwait(false);
		}

		static async Task<string> readheaders(Stream input, CancellationToken ct) {
			using var output = new MemoryStream();
			var last = 0;
			while (output.Length < 32768) {
				var one = await readexact(input, 1, ct).ConfigureAwait(false);
				output.WriteByte(one[0]);
				last = (last << 8) | one[0];
				if (last == 0x0D0A0D0A) return Encoding.ASCII.GetString(output.ToArray());
			}
			throw new InvalidOperationException("WebSocket HTTP 响应头过大");
		}

		static async Task<byte[]> readexact(Stream input, int count, CancellationToken ct) {
			var data = new byte[count];
			var offset = 0;
			while (offset < count) {
				var n = await input.ReadAsync(data, offset, count - offset, ct).ConfigureAwait(false);
				if (n <= 0) throw new EndOfStreamException("Edge 在线语音连接提前关闭");
				offset += n;
			}
			return data;
		}

		static async Task write(Stream output, byte[] data, CancellationToken ct) {
			await output.WriteAsync(data, 0, data.Length, ct).ConfigureAwait(false);
			await output.FlushAsync(ct).ConfigureAwait(false);
		}

		static string firstline(string headers) {
			var i = (headers ?? "").IndexOf("\r\n", StringComparison.Ordinal);
			return i < 0 ? headers ?? "" : headers.Substring(0, i);
		}

		public void Dispose() {
			open = false;
			try { stream?.Dispose(); } catch { }
			try { tcp?.Close(); } catch { }
		}
	}

	public void Dispose() {
	}
}
