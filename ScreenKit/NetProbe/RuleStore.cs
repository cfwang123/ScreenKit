using System.IO;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 规则：进程/IP 黑白名单着色，忽略本机 LISTEN。
/// 配置文件 data/rules.ini（简单 key=value，避免依赖 JSON 库）。
/// </summary>
sealed class RuleStore {
	public static RuleStore Instance { get; } = new();

	readonly object gate = new();
	readonly HashSet<string> processBlack = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> processWhite = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> ipBlack = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> ipWhite = new(StringComparer.OrdinalIgnoreCase);

	public bool IgnoreListen { get; set; } = true;
	public bool EnableColorRules { get; set; } = true;

	RuleStore() {
		try { Load(); } catch { }
	}

	public string ConfigPath =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "netmon-rules.ini");

	public void Load() {
		lock (gate) {
			processBlack.Clear();
			processWhite.Clear();
			ipBlack.Clear();
			ipWhite.Clear();
			IgnoreListen = true;
			EnableColorRules = true;
			var path = ConfigPath;
			if (!File.Exists(path)) {
				Save(); // 写默认
				return;
			}
			foreach (var raw in File.ReadAllLines(path, Encoding.UTF8)) {
				var line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
				var eq = line.IndexOf('=');
				if (eq <= 0) continue;
				var key = line.Substring(0, eq).Trim();
				var val = line.Substring(eq + 1).Trim();
				if (key.Equals("IgnoreListen", StringComparison.OrdinalIgnoreCase))
					IgnoreListen = val is "1" or "true" or "yes";
				else if (key.Equals("EnableColor", StringComparison.OrdinalIgnoreCase))
					EnableColorRules = val is "1" or "true" or "yes";
				else if (key.Equals("ProcessBlack", StringComparison.OrdinalIgnoreCase))
					addCsv(processBlack, val);
				else if (key.Equals("ProcessWhite", StringComparison.OrdinalIgnoreCase))
					addCsv(processWhite, val);
				else if (key.Equals("IpBlack", StringComparison.OrdinalIgnoreCase))
					addCsv(ipBlack, val);
				else if (key.Equals("IpWhite", StringComparison.OrdinalIgnoreCase))
					addCsv(ipWhite, val);
			}
		}
	}

	public void Save() {
		lock (gate) {
			var sb = new StringBuilder();
			sb.AppendLine("# 网络监控规则");
			sb.AppendLine("# IgnoreListen=1 隐藏 LISTEN 连接");
			sb.AppendLine("# ProcessBlack/White、IpBlack/White 逗号分隔；黑名单优先于白名单着色");
			sb.AppendLine($"IgnoreListen={(IgnoreListen ? "1" : "0")}");
			sb.AppendLine($"EnableColor={(EnableColorRules ? "1" : "0")}");
			sb.AppendLine("ProcessBlack=" + string.Join(",", processBlack));
			sb.AppendLine("ProcessWhite=" + string.Join(",", processWhite));
			sb.AppendLine("IpBlack=" + string.Join(",", ipBlack));
			sb.AppendLine("IpWhite=" + string.Join(",", ipWhite));
			File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
		}
	}

	static void addCsv(HashSet<string> set, string csv) {
		if (string.IsNullOrEmpty(csv)) return;
		foreach (var p in csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			set.Add(p.Trim());
	}

	public string GetProcessBlackText() { lock (gate) return string.Join(",", processBlack); }
	public string GetProcessWhiteText() { lock (gate) return string.Join(",", processWhite); }
	public string GetIpBlackText() { lock (gate) return string.Join(",", ipBlack); }
	public string GetIpWhiteText() { lock (gate) return string.Join(",", ipWhite); }

	public void SetLists(string procBlack, string procWhite, string ipBlackList, string ipWhiteList, bool ignoreListen, bool enableColor) {
		lock (gate) {
			processBlack.Clear();
			processWhite.Clear();
			ipBlack.Clear();
			ipWhite.Clear();
			addCsv(processBlack, procBlack);
			addCsv(processWhite, procWhite);
			addCsv(ipBlack, ipBlackList);
			addCsv(ipWhite, ipWhiteList);
			IgnoreListen = ignoreListen;
			EnableColorRules = enableColor;
		}
		Save();
	}

	/// <summary>返回标记色：Black / White / null。</summary>
	public string MatchColor(string processName, string remoteIp) {
		if (!EnableColorRules) return null;
		lock (gate) {
			if (!string.IsNullOrEmpty(processName)) {
				foreach (var b in processBlack)
					if (processName.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return "Black";
				foreach (var w in processWhite)
					if (processName.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return "White";
			}
			if (!string.IsNullOrEmpty(remoteIp)) {
				foreach (var b in ipBlack)
					if (remoteIp.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return "Black";
				foreach (var w in ipWhite)
					if (remoteIp.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return "White";
			}
		}
		return null;
	}

	public bool IsProcessBlack(ProcessNetRow p) =>
		p != null && MatchColor(p.ProcessName, null) == "Black";

	public bool IsProcessWhite(ProcessNetRow p) =>
		p != null && MatchColor(p.ProcessName, null) == "White";

	public bool IsConnBlack(ConnectionRow c) =>
		c != null && MatchColor(c.ProcessName, c.RemoteIp) == "Black";

	public bool IsConnWhite(ConnectionRow c) =>
		c != null && MatchColor(c.ProcessName, c.RemoteIp) == "White";
}
