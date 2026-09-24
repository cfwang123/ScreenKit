using System.Security.Cryptography;
using System.Text;

namespace ScreenKit;

/// <summary>网页文件管理：登录会话（内存 token / Cookie）。</summary>
public sealed class SendFileWeb {
	readonly object gate = new();
	readonly Dictionary<string, int> sessions = new(StringComparer.Ordinal);
	readonly Func<OcrOptions> getOpts;
	readonly Action save;
	const int SESSION_MS = 7 * 24 * 3600 * 1000;
	const string COOKIE = "sk_web";
	const string PASS_CHARS = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	public SendFileWeb(Func<OcrOptions> optionsFactory, Action saveCfg) {
		getOpts = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
		save = saveCfg;
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

	public string Login(string password) {
		var want = CurrentPass();
		if (string.IsNullOrEmpty(want) || !string.Equals(password ?? "", want, StringComparison.Ordinal))
			return null;
		var token = gentoken();
		lock (gate) {
			prune();
			sessions[token] = Environment.TickCount;
		}
		return token;
	}

	public void Logout(string token) {
		if (string.IsNullOrEmpty(token)) return;
		lock (gate) sessions.Remove(token);
	}

	internal bool Authed(SfReq req) {
		var token = tokenof(req);
		if (string.IsNullOrEmpty(token)) return false;
		lock (gate) {
			if (!sessions.TryGetValue(token, out var last)) return false;
			if (unchecked(Environment.TickCount - last) >= SESSION_MS) {
				sessions.Remove(token);
				return false;
			}
			sessions[token] = Environment.TickCount;
			return true;
		}
	}

	internal static string TokenOf(SfReq req) => tokenof(req);

	public static string CookieName => COOKIE;

	public static string SetCookie(string token) =>
		$"{COOKIE}={token}; Path=/; HttpOnly; SameSite=Lax; Max-Age=604800";

	public static string ClearCookie() =>
		$"{COOKIE}=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0";

	public static bool IsMobileUa(string ua) {
		if (string.IsNullOrEmpty(ua)) return false;
		return ua.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) >= 0
			|| ua.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0
			|| ua.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0
			|| ua.IndexOf("iPad", StringComparison.OrdinalIgnoreCase) >= 0
			|| ua.IndexOf("iPod", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	static string tokenof(SfReq req) {
		if (req == null) return "";
		var h = req.Headers["X-Web-Token"] ?? "";
		if (h.Length > 0) return h.Trim();
		var auth = req.Headers["Authorization"] ?? "";
		if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
			var t = auth.Substring(7).Trim();
			if (t.Length > 0) return t;
		}
		return cookieof(req.Headers["Cookie"] ?? "", COOKIE);
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
		var now = Environment.TickCount;
		var dead = new List<string>();
		foreach (var kv in sessions) {
			if (unchecked(now - kv.Value) >= SESSION_MS)
				dead.Add(kv.Key);
		}
		foreach (var k in dead) sessions.Remove(k);
	}

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
