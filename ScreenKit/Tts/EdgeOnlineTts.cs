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

	/// <summary>直接用 ShortName，不要求已经拉过在线语音表。</summary>
	public void SetVoiceName(string shortName) {
		var name = (shortName ?? "").Trim();
		if (name.StartsWith("edge:", StringComparison.OrdinalIgnoreCase))
			name = name.Substring(5);
		if (name.Length == 0) return;
		voice = name;
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
				+ $"&Sec-MS-GEC={makegec()}&Sec-MS-GEC-Version=2-{version}";
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
			+ $"&ConnectionId={connectionId}&Sec-MS-GEC={makegec()}&Sec-MS-GEC-Version=2-{version}";
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
		var ticks = (unix + 11644473600L) * 10000000L;
		var raw = Encoding.ASCII.GetBytes(ticks.ToString(CultureInfo.InvariantCulture) + TOKEN);
		var digest = gecsha(raw);
		geccs(raw, digest, out var a, out var b);
		var cs = new byte[8];
		Array.Copy(BitConverter.GetBytes(a), 0, cs, 0, 4);
		Array.Copy(BitConverter.GetBytes(b), 0, cs, 4, 4);
		return BitConverter.ToString(cs).Replace("-", "") + "-" + BitConverter.ToString(digest).Replace("-", "");
	}

	static readonly uint[] GEC_IV = {
		0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19,
	};
	static readonly uint[] GEC_STATE = {
		0xFD9FBEA2, 0x91243CE7, 0xBE499094, 0xCD2E1E0A, 0x6C006F49, 0x04BD35AB, 0xFEB0A7E4, 0x03EFFBD2,
	};
	static readonly uint[] GEC_SCHED = {
		0xEB24BF9E, 0x1558691F, 0xA0C09EE2, 0x0CFA32BA, 0xA622EE8C, 0xD1DD9491, 0xC0DD25A0, 0xF9E70AB6,
		0xF0C31573, 0x7058CE4E, 0x7188E606, 0x00E5AB33, 0xD258B060, 0x72766564,
	};
	static readonly uint[] GEC_K = {
		0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
		0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
		0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
		0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
		0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
		0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
		0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
		0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
	};
	static readonly uint[] GEC_CS_C = {
		0xE82E91B1, 0x44DE24C6, 0x3BEB65BC, 0xC89C5933, 0x8D1EC211, 0x314EF5D5, 0xE92491B1,
		0xF0A5B9D5, 0x06064CF3, 0xDA79FA60, 0xBED20560, 0x89F9C9F8, 0x35FA38C6, 0xCB49E3C6,
	};
	static readonly uint[] GEC_CS_O = {
		0xB32700D0, 0x88DFF51D, 0x29514038, 0xB328CBDF, 0xD405B4D5, 0x26E55F3C, 0x607801C6,
		0x82B2C1FB, 0xFA51A3DA, 0x207CC464, 0x3C3C74F3, 0xD2D14B61, 0xBB04A2C1, 0xC5626167,
	};

	static byte[] gecsha(byte[] data) {
		var length = 57 + data.Length;
		var pad = pymod(55 - length, 64);
		var padded = new byte[data.Length - 7 + 1 + pad + 8];
		Array.Copy(data, 7, padded, 0, data.Length - 7);
		padded[data.Length - 7] = 0x80;
		var bits = (ulong)length * 8;
		for (var i = 0; i < 8; i++) padded[padded.Length - 1 - i] = (byte)(bits >> (8 * i));
		var state = gecinit(data);
		for (var offset = 0; offset < padded.Length; offset += 64) {
			var words = new uint[64];
			for (var i = 0; i < 16; i++)
				words[i] = ((uint)padded[offset + i * 4] << 24) | ((uint)padded[offset + i * 4 + 1] << 16)
					| ((uint)padded[offset + i * 4 + 2] << 8) | padded[offset + i * 4 + 3];
			gecexpand(words, 16);
			var working = gecrounds(state, words, 0, false);
			for (var i = 0; i < 8; i++) state[i] += working[i];
		}
		var digest = new byte[32];
		for (var i = 0; i < 8; i++) {
			digest[i * 4] = (byte)(state[i] >> 24);
			digest[i * 4 + 1] = (byte)(state[i] >> 16);
			digest[i * 4 + 2] = (byte)(state[i] >> 8);
			digest[i * 4 + 3] = (byte)state[i];
		}
		return digest;
	}

	static uint[] gecinit(byte[] data) {
		var words = new List<uint>();
		for (var i = 0; i < 14; i++) words.Add(0);
		words.Add(((uint)data[0] << 16) | ((uint)data[1] << 8) | data[2]);
		words.Add(((uint)data[3] << 24) | ((uint)data[4] << 16) | ((uint)data[5] << 8) | data[6]);
		for (var index = 16; index < 30; index++) {
			uint value = GEC_SCHED[index - 16];
			if (index - 2 >= 14)
				value += index == 16 ? gecsig1(words[index - 2]) ^ 0x000B92C0 : gecsig1(words[index - 2]);
			if (index - 7 >= 14) value += words[index - 7];
			if (index - 15 >= 14)
				value += index == 29 ? gecsig0(words[index - 15]) ^ 0x059C0B80 : gecsig0(words[index - 15]);
			words.Add(value);
		}
		var extra = (long)gecsig1(words[28]) + words[23] + gecsig0(words[15]) + words[14]
			+ 0xC9DC06A7L - 0x9BDC06A7L;
		words.Add((uint)extra);
		var block = words.ToArray();
		Array.Resize(ref block, 64);
		gecexpand(block, 31);
		var working = gecrounds(GEC_STATE, block, 14, true);
		var state = new uint[8];
		for (var i = 0; i < 8; i++) state[i] = GEC_IV[i] + working[i];
		return state;
	}

	static uint[] gecrounds(uint[] state, uint[] words, int first, bool blockk) {
		uint a = state[0], b = state[1], c = state[2], d = state[3];
		uint e = state[4], f = state[5], g = state[6], h = state[7];
		for (var index = first; index < 64; index++) {
			var k = blockk && index == 14 ? 0xC9DC06A7 : GEC_K[index];
			ulong rotE = (ulong)e * 0x100000001UL;
			ulong rotA = (ulong)a * 0x100000001UL;
			ulong t1 = h + ((rotE >> 6) ^ (rotE >> 11) ^ (rotE >> 25))
				+ (uint)(g ^ (e & (f ^ g))) + k + words[index];
			ulong t2 = ((rotA >> 2) ^ (rotA >> 13) ^ (rotA >> 22))
				+ (uint)((a & b) | (c & (a | b)));
			var na = (uint)(t1 + t2);
			var ne = (uint)(d + t1);
			h = g; g = f; f = e; e = ne; d = c; c = b; b = a; a = na;
		}
		return new[] { a, b, c, d, e, f, g, h };
	}

	static void gecexpand(uint[] words, int first) {
		for (var index = first; index < 64; index++) {
			uint x = words[index - 2], y = words[index - 15];
			ulong s1 = ((ulong)x >> 17) ^ ((ulong)x << 15) ^ ((ulong)x >> 19) ^ ((ulong)x << 13) ^ ((ulong)x >> 10);
			ulong s0 = ((ulong)y >> 7) ^ ((ulong)y << 25) ^ ((ulong)y >> 18) ^ ((ulong)y << 14) ^ ((ulong)y >> 3);
			words[index] = (uint)(s1 + words[index - 7] + s0 + words[index - 16]);
		}
	}

	static uint gecsig0(uint value) =>
		(value >> 7) ^ (value << 25) ^ (value >> 18) ^ (value << 14) ^ (value >> 3);

	static uint gecsig1(uint value) =>
		(value >> 17) ^ (value << 15) ^ (value >> 19) ^ (value << 13) ^ (value >> 10);

	static void geccs(byte[] data, byte[] digest, out uint left, out uint right) {
		uint keyA = (uint)(digest[0] | (digest[1] << 8) | (digest[2] << 16) | (digest[3] << 24)) | 1;
		uint keyB = (uint)(digest[4] | (digest[5] << 8) | (digest[6] << 16) | (digest[7] << 24)) | 1;
		uint facA = keyA * 0x82893933;
		uint facB = keyB * 0x82893933;
		var terms = new List<uint>();
		var offsets = new List<uint>();
		for (var i = 0; i < GEC_CS_C.Length; i++) {
			terms.Add(GEC_CS_C[i] * (i % 2 == 0 ? facA : facB));
			offsets.Add(GEC_CS_O[i]);
		}
		var src = new byte[data.Length + 1];
		Array.Copy(data, 0, src, 1, data.Length);
		var n = src.Length;
		if (n % 4 != 0) n += 4 - n % 4;
		if ((n / 4) % 2 != 0) n += 4;
		var even = new byte[n];
		Array.Copy(src, even, src.Length);
		for (var index = 0; index < even.Length / 4; index++) {
			uint word = even[index * 4] | ((uint)even[index * 4 + 1] << 8)
				| ((uint)even[index * 4 + 2] << 16) | ((uint)even[index * 4 + 3] << 24);
			uint term = word * (index % 2 == 0 ? keyA : keyB);
			uint offset = gecrol(word) * (index % 2 == 0 ? 0xEF0569FB : 0xC31713DB);
			if (index == 0) {
				term += 0xF2F90B1A * facA;
				offset += 0x0B1A0000;
			}
			terms.Add(term);
			offsets.Add(offset);
		}
		uint swapAcc = 0, swapLast = 0, state = 0, acc = 0;
		for (var index = 0; index < terms.Count; index += 2) {
			uint x = terms[index], y = terms[index + 1];
			uint t = gecrol(offsets[index] + x);
			uint q = t * 0x76F50F34;
			swapAcc += gecrol(q) * 0xC3EFEA97 + q;
			t = gecrol(offsets[index + 1] + y);
			q = t * 0x1881CE3C;
			swapLast = gecrol(q) * 0x35BD1EC9 + q;
			swapAcc += swapLast;
			state = state * keyA + x;
			state = gecrol(state) * 0xCF98B111;
			state = gecrol(state) * 0x87085B9F;
			state = gecrol(state) * 0x12CEB96D;
			state = gecrol(state) * 0x257E1D83;
			acc += state;
			t = state * keyB + y;
			t = gecrol(t) * 0xA27416F5;
			t = gecrol(t) * 0xD38396FF;
			state = gecrol(gecrol(t) * 0x7C932B89) * 0xBFA49F69;
			acc += state;
		}
		left = swapLast ^ state;
		right = swapAcc ^ acc;
	}

	static uint gecrol(uint value) => (value << 16) | (value >> 16);

	static int pymod(int n, int m) {
		var r = n % m;
		return r < 0 ? r + m : r;
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
