using System.Collections.Concurrent;
using System.Diagnostics;

namespace ScreenKit;

/// <summary>PID → 进程名 / 路径 缓存。</summary>
static class ProcessResolver {
	static readonly ConcurrentDictionary<int, (string name, string path, int expire)> Cache = new();
	const int TTL_MS = 5000;

	public static (string name, string path) Resolve(int pid) {
		if (pid <= 0) return ("System", "");
		var now = Environment.TickCount;
		if (Cache.TryGetValue(pid, out var c) && now - c.expire < TTL_MS)
			return (c.name, c.path);

		string name = $"PID {pid}", path = "";
		try {
			using var p = Process.GetProcessById(pid);
			name = string.IsNullOrEmpty(p.ProcessName) ? name : p.ProcessName;
			try { path = p.MainModule?.FileName ?? ""; } catch { /* 跨权限 */ }
		} catch {
			name = pid == 0 || pid == 4 ? "System" : $"PID {pid}";
		}
		Cache[pid] = (name, path, now);
		return (name, path);
	}

	public static void Clear() => Cache.Clear();
}
