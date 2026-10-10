using System.Globalization;
using System.Net.Http;
using System.Text;

namespace ScreenKit;

/// <summary>货币换算。向中国银行网页取中间价（中行折算价），缓存 10 分钟。查询失败用内置牌价。</summary>
static class FxRate {
	public const string PAGE = "https://www.boc.cn/sourcedb/whpj/index.html";
	public const int CACHE_MS = 10 * 60 * 1000;
	const string DEFAULT_ASOF = "2026-10-10";

	static readonly (string Id, string Name)[] UNITS = {
		("CNY", "人民币元"), ("jiao", "角"), ("fen", "分"),
		("USD", "美元"), ("HKD", "港币"), ("MOP", "澳门元"), ("EUR", "欧元"), ("GBP", "英镑"),
		("JPY", "日元"), ("KRW", "韩元"), ("AUD", "澳元"), ("CAD", "加元"),
		("SGD", "新加坡元"), ("CHF", "瑞士法郎"), ("THB", "泰铢"), ("TWD", "新台币"),
	};

	/// <summary>1 单位折合人民币。外币是中国银行 2026-10-10 中行折算价除以 100。</summary>
	static readonly (string Id, decimal Cny)[] DEFAULTS = {
		("CNY", 1m), ("jiao", 0.1m), ("fen", 0.01m),
		("USD", 6.733m), ("HKD", 0.858m), ("MOP", 0.8326m), ("EUR", 7.5323m), ("GBP", 8.8872m),
		("JPY", 0.042571m), ("KRW", 0.004996m), ("AUD", 4.6746m), ("CAD", 4.7204m),
		("SGD", 5.246m), ("CHF", 8.0821m), ("THB", 0.1994m), ("TWD", 0.2099m),
	};

	static readonly (string Boc, string Id)[] BOC = {
		("美元", "USD"), ("港币", "HKD"), ("澳门元", "MOP"), ("欧元", "EUR"), ("英镑", "GBP"),
		("日元", "JPY"), ("韩国元", "KRW"), ("澳大利亚元", "AUD"), ("加拿大元", "CAD"),
		("新加坡元", "SGD"), ("瑞士法郎", "CHF"), ("泰国铢", "THB"), ("新台币", "TWD"),
	};

	static readonly object gate = new();
	static Dictionary<string, decimal> cached;
	static string cachedAsof = "";
	static int cachedAt;
	static Func<string> loader = download;

	public static (string Text, bool Live) Quote(decimal amount, string from, string to) {
		var src = canon(from);
		var dst = string.IsNullOrWhiteSpace(to) ? "all" : to.Trim();
		if (!string.Equals(dst, "all", StringComparison.OrdinalIgnoreCase))
			dst = canon(dst);
		if (cashonly(src, dst))
			return (format(amount, src, dst, defrates(), ""), true);
		var got = table();
		var note = got.Live
			? $"中国银行 {got.Asof} 中间价，缓存 10 分钟。"
			: "汇率查询失败，已用中国银行 2026-10-10 中间价。";
		return (format(amount, src, dst, got.Rates, note), got.Live);
	}

	/// <summary>测试时换掉网页下载。load 为空则恢复访问中国银行。</summary>
	internal static void Reset(Func<string> load) {
		lock (gate) {
			cached = null;
			cachedAsof = "";
			loader = load ?? download;
		}
	}

	internal static Dictionary<string, decimal> Parse(string html, out string asof) {
		var parsed = parse(html);
		asof = parsed.Asof;
		return parsed.Rates;
	}

	static (Dictionary<string, decimal> Rates, string Asof, bool Live) table() {
		lock (gate) {
			if (cached != null && fresh())
				return (cached, cachedAsof, true);
			try {
				var html = loader();
				var parsed = parse(html);
				cached = parsed.Rates;
				cachedAsof = parsed.Asof;
				cachedAt = Environment.TickCount;
				return (cached, cachedAsof, true);
			}
			catch {
				return (defrates(), DEFAULT_ASOF, false);
			}
		}
	}

	static bool fresh() {
		var age = unchecked(Environment.TickCount - cachedAt);
		return age >= 0 && age < CACHE_MS;
	}

	static Dictionary<string, decimal> defrates() {
		var d = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
		foreach (var one in DEFAULTS) d[one.Id] = one.Cny;
		return d;
	}

	static (Dictionary<string, decimal> Rates, string Asof) parse(string html) {
		if (string.IsNullOrWhiteSpace(html)) throw new InvalidOperationException("空页面");
		var rates = defrates();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var asof = "";
		foreach (var cells in rows(html)) {
			if (cells.Length < 6) continue;
			var code = boccode(cells[0]);
			if (code.Length == 0 || seen.Contains(code)) continue;
			if (!decimal.TryParse(cells[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var per100))
				continue;
			if (per100 <= 0) continue;
			seen.Add(code);
			rates[code] = per100 / 100m;
			if (asof.Length == 0 && cells.Length > 6 && cells[6].Length > 0)
				asof = cells[6];
		}
		if (seen.Count < BOC.Length) throw new InvalidOperationException("牌价不全");
		if (asof.Length == 0) asof = DEFAULT_ASOF;
		return (rates, asof);
	}

	static string boccode(string name) {
		name = (name ?? "").Trim();
		foreach (var one in BOC)
			if (one.Boc == name) return one.Id;
		return "";
	}

	static List<string[]> rows(string html) {
		var list = new List<string[]>();
		var i = 0;
		while (i < html.Length) {
			var a = html.IndexOf("<tr", i, StringComparison.OrdinalIgnoreCase);
			if (a < 0) break;
			var b = html.IndexOf("</tr>", a, StringComparison.OrdinalIgnoreCase);
			if (b < 0) break;
			var row = html.Substring(a, b - a);
			var cells = new List<string>();
			var j = 0;
			while (j < row.Length) {
				var c = row.IndexOf("<td", j, StringComparison.OrdinalIgnoreCase);
				if (c < 0) break;
				var gt = row.IndexOf('>', c);
				if (gt < 0) break;
				var d = row.IndexOf("</td>", gt, StringComparison.OrdinalIgnoreCase);
				if (d < 0) break;
				cells.Add(clean(row.Substring(gt + 1, d - gt - 1)));
				j = d + 5;
			}
			if (cells.Count > 0) list.Add(cells.ToArray());
			i = b + 5;
		}
		return list;
	}

	static string clean(string s) {
		var sb = new StringBuilder();
		var tag = false;
		foreach (var ch in s) {
			if (ch == '<') { tag = true; continue; }
			if (ch == '>') { tag = false; continue; }
			if (!tag) sb.Append(ch);
		}
		return sb.ToString().Replace("&nbsp;", " ").Replace("\u00a0", " ").Trim();
	}

	static string format(decimal amount, string from, string to, Dictionary<string, decimal> rates, string note) {
		var lines = new List<string>();
		if (string.Equals(to, "all", StringComparison.OrdinalIgnoreCase)) {
			lines.Add(line(amount, from, from, rates));
			foreach (var one in UNITS) {
				if (string.Equals(one.Id, from, StringComparison.OrdinalIgnoreCase)) continue;
				lines.Add(line(amount, from, one.Id, rates));
			}
		}
		else {
			lines.Add(line(amount, from, from, rates));
			lines.Add(line(amount, from, to, rates));
		}
		if (!string.IsNullOrEmpty(note)) lines.Add(note);
		return string.Join("\n", lines);
	}

	static string line(decimal amount, string from, string to, Dictionary<string, decimal> rates) {
		if (!rates.TryGetValue(from, out var a) || !rates.TryGetValue(to, out var b) || b == 0)
			throw new ArgumentException("请选择单位");
		return trimnum(amount * a / b) + name(to);
	}

	static string name(string id) {
		foreach (var one in UNITS)
			if (string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase)) return one.Name;
		return id;
	}

	static string canon(string id) {
		var s = (id ?? "").Trim();
		if (s.Length == 0) throw new ArgumentException("请选择单位");
		foreach (var one in UNITS)
			if (string.Equals(one.Id, s, StringComparison.OrdinalIgnoreCase)) return one.Id;
		throw new ArgumentException("请选择单位");
	}

	static bool cashonly(string from, string to) {
		if (!iscash(from)) return false;
		if (string.Equals(to, "all", StringComparison.OrdinalIgnoreCase)) return false;
		return iscash(to);
	}

	static bool iscash(string id) =>
		id == "CNY" || id == "jiao" || id == "fen";

	static string trimnum(decimal n) {
		if (n == 0) return "0";
		var neg = n < 0;
		var a = Math.Abs(n);
		string s;
		if (a >= 1000000000000000m || a < 0.00000001m)
			s = ((double)a).ToString("0.######e+0", CultureInfo.InvariantCulture);
		else {
			s = a.ToString("0.##########", CultureInfo.InvariantCulture);
			if (s.IndexOf('.') >= 0) s = s.TrimEnd('0').TrimEnd('.');
		}
		return (neg ? "-" : "") + s;
	}

	static string download() {
		using var http = new HttpClient(HttpProxy.CreateHandler()) { Timeout = TimeSpan.FromSeconds(8) };
		http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
		var bytes = http.GetByteArrayAsync(PAGE).GetAwaiter().GetResult();
		if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("空页面");
		var utf = Encoding.UTF8.GetString(bytes);
		if (utf.IndexOf("美元", StringComparison.Ordinal) >= 0) return utf;
		return Encoding.GetEncoding("gb18030").GetString(bytes);
	}
}
