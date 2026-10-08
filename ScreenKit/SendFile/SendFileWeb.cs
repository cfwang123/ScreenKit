using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ScreenKit;

/// <summary>网页文件管理：登录会话（内存 token / Cookie；勾选保持登录则落盘）。</summary>
public sealed class SendFileWeb {
	readonly object gate = new();
	readonly Dictionary<string, WebSess> sessions = new(StringComparer.Ordinal);
	readonly Func<OcrOptions> getOpts;
	readonly Action save;
	const int KEEP_SEC = 30 * 24 * 3600;
	const int TEMP_SEC = 12 * 3600;
	const string COOKIE = "sk_web";
	const string SESS_FILE = ".web_sess";
	const string PASS_CHARS = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	public SendFileWeb(Func<OcrOptions> optionsFactory, Action saveCfg) {
		getOpts = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
		save = saveCfg;
		loadkeep();
	}

	/// <summary>密码为空时生成并写入配置。</summary>
	public string EnsurePass() {
		var o = getOpts();
		if (o == null) return "";
		if (!string.IsNullOrWhiteSpace(o.SendFileWebPass))
			return o.SendFileWebPass.Trim();
		o.SendFileWebPass = genpass(8);
		try { save?.Invoke(); } catch { }
		return o.SendFileWebPass;
	}

	public string CurrentPass() {
		var o = getOpts();
		var p = o?.SendFileWebPass ?? "";
		return string.IsNullOrWhiteSpace(p) ? EnsurePass() : p.Trim();
	}

	public string Login(string password, bool keep) {
		var want = CurrentPass();
		if (string.IsNullOrEmpty(want) || !string.Equals(password ?? "", want, StringComparison.Ordinal))
			return null;
		var token = gentoken();
		var now = unixnow();
		lock (gate) {
			prune();
			sessions[token] = new WebSess {
				last = Environment.TickCount,
				keep = keep,
				exp = now + (keep ? KEEP_SEC : TEMP_SEC),
			};
			if (keep) savekeep();
		}
		return token;
	}

	bool match(SfReq req, out string token, out bool keep) {
		token = "";
		keep = false;
		var list = tokensof(req);
		if (list.Count == 0) return false;
		var now = unixnow();
		lock (gate) {
			foreach (var one in list) {
				if (!sessions.TryGetValue(one, out var s) || s == null) continue;
				if (s.exp > 0 && now >= s.exp) {
					sessions.Remove(one);
					if (s.keep) savekeep();
					continue;
				}
				s.last = Environment.TickCount;
				token = one;
				keep = s.keep;
				return true;
			}
			return false;
		}
	}

	static List<string> tokensof(SfReq req) {
		var list = new List<string>();
		if (req == null) return list;
		addtok(list, req.Headers["X-Web-Token"]);
		var auth = req.Headers["Authorization"] ?? "";
		if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			addtok(list, auth.Substring(7));
		addtok(list, cookieof(req.Headers["Cookie"] ?? "", COOKIE));
		return list;
	}

	static void addtok(List<string> list, string raw) {
		var t = (raw ?? "").Trim();
		if (t.Length == 0) return;
		for (var i = 0; i < list.Count; i++) {
			if (string.Equals(list[i], t, StringComparison.Ordinal)) return;
		}
		list.Add(t);
	}

	internal bool Authed(SfReq req) => match(req, out _, out _);

	internal bool Match(SfReq req, out string token, out bool keep) => match(req, out token, out keep);

	internal void Logout(SfReq req) {
		if (req == null) return;
		lock (gate) {
			var had = false;
			foreach (var token in tokensof(req)) {
				if (sessions.Remove(token)) had = true;
			}
			if (had) savekeep();
		}
	}

	public static string SetCookie(string token, bool keep) {
		var basec = $"{COOKIE}={token}; Path=/; HttpOnly; SameSite=Lax";
		if (!keep) return basec;
		var exp = DateTimeOffset.UtcNow.AddSeconds(KEEP_SEC).ToString("R", CultureInfo.InvariantCulture);
		return $"{basec}; Max-Age={KEEP_SEC}; Expires={exp}";
	}

	public static string ClearCookie() =>
		$"{COOKIE}=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT";

	public static bool IsMobileUa(string ua) {
		if (string.IsNullOrEmpty(ua)) return false;
		return ua.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) >= 0
			|| ua.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0
			|| ua.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0
			|| ua.IndexOf("iPad", StringComparison.OrdinalIgnoreCase) >= 0
			|| ua.IndexOf("iPod", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static string cookieof(string hdr, string name) {
		if (string.IsNullOrEmpty(hdr) || string.IsNullOrEmpty(name)) return "";
		foreach (var part in hdr.Split(';')) {
			var s = part.Trim();
			var eq = s.IndexOf('=');
			if (eq <= 0) continue;
			if (!s.Substring(0, eq).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
				continue;
			return s.Substring(eq + 1).Trim();
		}
		return "";
	}

	void prune() {
		var now = unixnow();
		var dead = new List<string>();
		foreach (var kv in sessions) {
			if (kv.Value != null && kv.Value.exp > 0 && now >= kv.Value.exp)
				dead.Add(kv.Key);
		}
		foreach (var k in dead) sessions.Remove(k);
	}

	void loadkeep() {
		try {
			SendFilePaths.EnsureRoot();
			var f = Path.Combine(SendFilePaths.Root(), SESS_FILE);
			if (!File.Exists(f)) return;
			var now = unixnow();
			foreach (var line in File.ReadAllLines(f, Encoding.UTF8)) {
				var p = (line ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (p.Length < 2) continue;
				var tok = p[0].Trim();
				if (tok.Length < 16) continue;
				if (!long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp))
					continue;
				if (exp > 0 && now >= exp) continue;
				sessions[tok] = new WebSess { last = Environment.TickCount, keep = true, exp = exp };
			}
		}
		catch { }
	}

	void savekeep() {
		try {
			SendFilePaths.EnsureRoot();
			var f = Path.Combine(SendFilePaths.Root(), SESS_FILE);
			var now = unixnow();
			var sb = new StringBuilder();
			foreach (var kv in sessions) {
				var s = kv.Value;
				if (s == null || !s.keep) continue;
				if (s.exp > 0 && now >= s.exp) continue;
				sb.Append(kv.Key).Append(' ').Append(s.exp.ToString(CultureInfo.InvariantCulture)).Append('\n');
			}
			if (sb.Length == 0) {
				if (File.Exists(f)) File.Delete(f);
				return;
			}
			File.WriteAllText(f, sb.ToString(), new UTF8Encoding(false));
		}
		catch { }
	}

	static long unixnow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	static string gentoken() {
		var buf = new byte[24];
		using (var rng = RandomNumberGenerator.Create())
			rng.GetBytes(buf);
		var sb = new StringBuilder(buf.Length * 2);
		foreach (var b in buf)
			sb.Append(b.ToString("x2"));
		return sb.ToString();
	}

	static string genpass(int n) {
		if (n < 4) n = 4;
		var buf = new byte[n];
		using (var rng = RandomNumberGenerator.Create())
			rng.GetBytes(buf);
		var sb = new StringBuilder(n);
		for (var i = 0; i < n; i++)
			sb.Append(PASS_CHARS[buf[i] % PASS_CHARS.Length]);
		return sb.ToString();
	}
}

sealed class WebSess {
	public int last;
	public bool keep;
	public long exp;
}
