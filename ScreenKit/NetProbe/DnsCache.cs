using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenKit;

/// <summary>
/// 反向 DNS（PTR via DnsQuery）+ 系统 DNS 正向缓存。
/// 查到域名后 UI 优先以域名展示远程主机。
/// </summary>
static class DnsCache {
	static readonly ConcurrentDictionary<string, CacheItem> ReverseMap = new();
	static readonly ConcurrentDictionary<string, string> IpToHostHint = new();
	static readonly ConcurrentDictionary<string, byte> Pending = new();
	static int lastdnsrefresh;

	const int SUCCESS_TTL_MS = 10 * 60 * 1000;
	const int FAIL_TTL_MS = 60 * 1000;
	const short DNS_TYPE_A = 1;
	const short DNS_TYPE_AAAA = 28;
	const short DNS_TYPE_PTR = 12;
	const int DNS_QUERY_STANDARD = 0;
	const int DnsFreeRecordList = 1;

	sealed class CacheItem {
		public string Host;
		public int ResolvedTick;
		public int TtlMs;
		public bool Failed;
	}

	/// <summary>已解析域名；未完成则异步排队 PTR 并返回空。</summary>
	public static string LookupHost(string ip) {
		if (!isLookupIp(ip)) return "";

		var now = Environment.TickCount;

		if (IpToHostHint.TryGetValue(ip, out var hint) && !string.IsNullOrEmpty(hint) && !isIpLiteral(hint))
			return cleanHost(hint);

		if (ReverseMap.TryGetValue(ip, out var item)) {
			if (Environment.TickCount - item.ResolvedTick < item.TtlMs) {
				if (item.Failed || string.IsNullOrEmpty(item.Host)) return "";
				return item.Host;
			}
			ReverseMap.TryRemove(ip, out _);
		}

		queueReverse(ip);
		return "";
	}

	/// <summary>有域名则 domain:port，否则 ip:port。</summary>
	public static string FormatRemote(string ip, int port) {
		if (string.IsNullOrEmpty(ip)) return port > 0 ? $"*:{port}" : "*";
		var host = LookupHost(ip);
		if (!string.IsNullOrEmpty(host))
			return port > 0 ? $"{host}:{port}" : host;
		return port > 0 ? $"{ip}:{port}" : ip;
	}

	/// <summary>有域名返回域名，否则返回 IP。</summary>
	public static string FormatHostOrIp(string ip) {
		if (string.IsNullOrEmpty(ip)) return "";
		var host = LookupHost(ip);
		return !string.IsNullOrEmpty(host) ? host : ip;
	}

	public static void RefreshSystemDnsCache() {
		if (Environment.TickCount - lastdnsrefresh < 3000) return;
		lastdnsrefresh = Environment.TickCount;
		try { readDnsClientCache(); } catch { }
	}

	public static void Clear() {
		ReverseMap.Clear();
		IpToHostHint.Clear();
		Pending.Clear();
	}

	static void queueReverse(string ip) {
		if (!Pending.TryAdd(ip, 0)) return;
		Task.Run(() => {
			try {
				var host = queryPtr(ip);
				if (string.IsNullOrEmpty(host))
					host = queryPtrViaGetHostEntry(ip);

				var now = Environment.TickCount;
				if (!string.IsNullOrEmpty(host) && !isIpLiteral(host) &&
				    !string.Equals(host, ip, StringComparison.OrdinalIgnoreCase)) {
					var h = cleanHost(host);
					ReverseMap[ip] = new CacheItem {
						Host = h,
						ResolvedTick = now,
						TtlMs = SUCCESS_TTL_MS,
						Failed = false
					};
					IpToHostHint[ip] = h;
				} else {
					ReverseMap[ip] = new CacheItem {
						Host = "",
						ResolvedTick = now,
						TtlMs = FAIL_TTL_MS,
						Failed = true
					};
				}
			} catch {
				ReverseMap[ip] = new CacheItem {
					Host = "",
					ResolvedTick = Environment.TickCount,
					TtlMs = FAIL_TTL_MS,
					Failed = true
				};
			} finally {
				Pending.TryRemove(ip, out _);
			}
		});
	}

	static string queryPtr(string ip) {
		if (!IPAddress.TryParse(ip, out var addr)) return "";
		var qname = toPtrName(addr);
		if (string.IsNullOrEmpty(qname)) return "";

		var status = DnsQuery(qname, DNS_TYPE_PTR, DNS_QUERY_STANDARD, IntPtr.Zero, out var results, IntPtr.Zero);
		if (status != 0 || results == IntPtr.Zero) return "";
		try {
			return readPtrFromRecords(results);
		} finally {
			DnsRecordListFree(results, DnsFreeRecordList);
		}
	}

	static string queryPtrViaGetHostEntry(string ip) {
		try {
			var entry = Dns.GetHostEntry(ip);
			if (string.IsNullOrEmpty(entry.HostName)) return "";
			if (isIpLiteral(entry.HostName)) return "";
			if (string.Equals(entry.HostName, ip, StringComparison.OrdinalIgnoreCase)) return "";
			return entry.HostName;
		} catch {
			return "";
		}
	}

	static string toPtrName(IPAddress addr) {
		if (addr.AddressFamily == AddressFamily.InterNetwork) {
			var b = addr.GetAddressBytes();
			return $"{b[3]}.{b[2]}.{b[1]}.{b[0]}.in-addr.arpa";
		}
		if (addr.AddressFamily == AddressFamily.InterNetworkV6) {
			var b = addr.GetAddressBytes();
			var sb = new StringBuilder(72);
			for (var i = b.Length - 1; i >= 0; i--) {
				var v = b[i];
				sb.AppendFormat("{0:x}.", v & 0xF);
				sb.AppendFormat("{0:x}.", v >> 4);
			}
			sb.Append("ip6.arpa");
			return sb.ToString();
		}
		return "";
	}

	// DNS_RECORDW 固定头（x64 = 32 字节）
	[StructLayout(LayoutKind.Sequential)]
	struct DnsRecordHeader {
		public IntPtr pNext;
		public IntPtr pName;
		public short wType;
		public short wDataLength;
		public int Flags;
		public int dwTtl;
		public int dwReserved;
	}

	static string readPtrFromRecords(IntPtr p) {
		var hdrSize = Marshal.SizeOf<DnsRecordHeader>();
		var guard = 0;
		while (p != IntPtr.Zero && guard++ < 64) {
			var hdr = Marshal.PtrToStructure<DnsRecordHeader>(p);
			if (hdr.wType == DNS_TYPE_PTR) {
				var dataPtr = IntPtr.Add(p, hdrSize);
				var namePtr = Marshal.ReadIntPtr(dataPtr);
				if (namePtr != IntPtr.Zero) {
					var name = Marshal.PtrToStringUni(namePtr);
					if (!string.IsNullOrEmpty(name)) return name.TrimEnd('.');
				}
			}
			p = hdr.pNext;
		}
		return "";
	}

	[StructLayout(LayoutKind.Sequential)]
	struct DnsCacheEntry {
		public IntPtr pNext;
		public IntPtr pszName;
		public short wType;
		public short wDataLength;
		public int dwFlags;
	}

	[DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, ExactSpelling = true)]
	static extern int DnsQuery(string pszName, short wType, int options, IntPtr pExtra,
		out IntPtr ppQueryResults, IntPtr pReserved);

	[DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
	static extern void DnsRecordListFree(IntPtr pRecordList, int freeType);

	[DllImport("dnsapi.dll", EntryPoint = "DnsGetCacheDataTable", CharSet = CharSet.Unicode)]
	static extern bool DnsGetCacheDataTable(out IntPtr ppEntry);

	[DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
	static extern void DnsFree(IntPtr pData, int freeType);

	static void readDnsClientCache() {
		if (!DnsGetCacheDataTable(out var pEntry) || pEntry == IntPtr.Zero) return;
		try {
			var cur = pEntry;
			var guard = 0;
			while (cur != IntPtr.Zero && guard++ < 8000) {
				var e = Marshal.PtrToStructure<DnsCacheEntry>(cur);
				var name = e.pszName != IntPtr.Zero ? Marshal.PtrToStringUni(e.pszName) : null;
				if (!string.IsNullOrEmpty(name) && e.wType is DNS_TYPE_A or DNS_TYPE_AAAA)
					resolveForwardHint(name);
				cur = e.pNext;
			}
		} finally {
			try { DnsFree(pEntry, DnsFreeRecordList); } catch { }
		}
	}

	static void resolveForwardHint(string host) {
		host = cleanHost(host);
		if (string.IsNullOrEmpty(host) || isIpLiteral(host)) return;
		Task.Run(() => {
			try {
				foreach (var a in Dns.GetHostAddresses(host)) {
					if (a.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
						continue;
					var ip = a.ToString();
					IpToHostHint[ip] = host;
					// 正向解析到的域名通常比 PTR 更贴近用户访问名
					ReverseMap[ip] = new CacheItem {
						Host = host,
						ResolvedTick = Environment.TickCount,
						TtlMs = SUCCESS_TTL_MS,
						Failed = false
					};
				}
			} catch { }
		});
	}

	static bool isLookupIp(string ip) {
		if (string.IsNullOrEmpty(ip)) return false;
		if (ip is "0.0.0.0" or "::" or "127.0.0.1" or "::1") return false;
		if (!IPAddress.TryParse(ip, out var a)) return false;
		if (IPAddress.IsLoopback(a)) return false;
		return true;
	}

	static bool isIpLiteral(string s) =>
		!string.IsNullOrEmpty(s) && IPAddress.TryParse(s, out _);

	static string cleanHost(string h) {
		if (string.IsNullOrEmpty(h)) return "";
		return h.Trim().TrimEnd('.');
	}
}
