using System.IO;
using System.Net.WebSockets;
using System.Threading;

namespace ScreenKit;

/// <summary>把 WebSocket 二进制帧当成双向 Stream，供 SCST 读写。</summary>
sealed class CastWsStream : Stream {
	readonly WebSocket ws;
	readonly object rlock = new();
	readonly object wlock = new();
	byte[] hold = Array.Empty<byte>();
	int holdo;
	volatile bool dead;

	public CastWsStream(WebSocket ws) {
		this.ws = ws ?? throw new ArgumentNullException(nameof(ws));
	}

	public override bool CanRead => true;
	public override bool CanWrite => true;
	public override bool CanSeek => false;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count) {
		if (buffer == null) throw new ArgumentNullException(nameof(buffer));
		if (count <= 0) return 0;
		lock (rlock) {
			while (holdo >= hold.Length) {
				if (dead || ws.State != WebSocketState.Open) return 0;
				if (!pull()) return 0;
			}
			var n = Math.Min(count, hold.Length - holdo);
			Buffer.BlockCopy(hold, holdo, buffer, offset, n);
			holdo += n;
			return n;
		}
	}

	bool pull() {
		using var acc = new MemoryStream();
		var buf = new byte[65536];
		while (true) {
			WebSocketReceiveResult r;
			try {
				r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None)
					.GetAwaiter().GetResult();
			}
			catch {
				dead = true;
				return false;
			}
			if (r.MessageType == WebSocketMessageType.Close) {
				dead = true;
				try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
					.GetAwaiter().GetResult(); } catch { }
				return false;
			}
			if (r.Count > 0) acc.Write(buf, 0, r.Count);
			if (r.EndOfMessage) {
				hold = acc.ToArray();
				holdo = 0;
				if (hold.Length == 0) {
					acc.SetLength(0);
					continue;
				}
				return true;
			}
		}
	}

	public override void Write(byte[] buffer, int offset, int count) {
		if (buffer == null) throw new ArgumentNullException(nameof(buffer));
		if (count <= 0) return;
		byte[] slice;
		if (offset == 0 && count == buffer.Length) slice = buffer;
		else {
			slice = new byte[count];
			Buffer.BlockCopy(buffer, offset, slice, 0, count);
		}
		lock (wlock) {
			if (dead || ws.State != WebSocketState.Open)
				throw new IOException("WebSocket 已关闭");
			try {
				ws.SendAsync(new ArraySegment<byte>(slice), WebSocketMessageType.Binary, true,
					CancellationToken.None).GetAwaiter().GetResult();
			}
			catch (Exception ex) {
				dead = true;
				throw new IOException("WebSocket 发送失败", ex);
			}
		}
	}

	public override void Flush() { }

	public override void Close() {
		dead = true;
		try { ws.Abort(); } catch { }
		base.Close();
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			dead = true;
			try { ws.Abort(); } catch { }
		}
		base.Dispose(disposing);
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}
