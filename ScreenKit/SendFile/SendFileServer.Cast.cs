using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ScreenKit;

public sealed partial class SendFileServer {
	const string WS_MAGIC = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

	void castupgrade(NetworkStream ns, SfCtx ctx) {
		var up = ctx.Request.Headers["Upgrade"] ?? "";
		var key = (ctx.Request.Headers["Sec-WebSocket-Key"] ?? "").Trim();
		if (key.Length == 0 || up.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) < 0) {
			writeraw(ns, 426, "Upgrade Required");
			return;
		}
		if (CastHost.Recv == null || !CastHost.Recv.Running) {
			writeraw(ns, 503, "Service Unavailable");
			log("cast ws: 投屏接收未开");
			return;
		}
		var accept = wsaccept(key);
		var resp = "HTTP/1.1 101 Switching Protocols\r\n"
			+ "Upgrade: websocket\r\n"
			+ "Connection: Upgrade\r\n"
			+ "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
		var bytes = Encoding.ASCII.GetBytes(resp);
		ns.Write(bytes, 0, bytes.Length);
		ns.Flush();
		using var st = new CastRawWsStream(ns, ctx.Request.Prefetch);
		try { CastHost.Recv.AttachStream(st, "lan"); }
		catch (Exception ex) { log("cast ws: " + ex.Message); }
	}

	static string wsaccept(string key) {
		using var sha = SHA1.Create();
		var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(key + WS_MAGIC));
		return Convert.ToBase64String(hash);
	}
}
