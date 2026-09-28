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

/// <summary>裸 TCP 上的 WebSocket（服务端不掩码），把二进制帧还原成 SCST 字节流。</summary>
sealed class CastRawWsStream : Stream {
	readonly Stream inner;
	readonly object rlock = new();
	readonly object wlock = new();
	readonly MemoryStream msg = new();
	byte[] pre;
	int preo;
	byte[] hold = Array.Empty<byte>();
	int holdo;
	volatile bool dead;

	public CastRawWsStream(Stream inner, byte[] prefetch) {
		this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
		pre = prefetch ?? Array.Empty<byte>();
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
				if (dead) return 0;
				if (!pull()) return 0;
			}
			var n = Math.Min(count, hold.Length - holdo);
			Buffer.BlockCopy(hold, holdo, buffer, offset, n);
			holdo += n;
			return n;
		}
	}

	bool pull() {
		msg.SetLength(0);
		while (true) {
			var h = new byte[2];
			if (!readfull(h)) { dead = true; return false; }
			var op = h[0] & 0x0F;
			var fin = (h[0] & 0x80) != 0;
			long length = h[1] & 0x7F;
			var masked = (h[1] & 0x80) != 0;
			if (length == 126) {
				var ext = new byte[2];
				if (!readfull(ext)) { dead = true; return false; }
				length = (ext[0] << 8) | ext[1];
			}
			else if (length == 127) {
				var ext = new byte[8];
				if (!readfull(ext)) { dead = true; return false; }
				length = 0;
				for (var i = 0; i < 8; i++) length = (length << 8) | ext[i];
			}
			if (length < 0 || length > 8 * 1024 * 1024) { dead = true; return false; }
			byte[] mask = null;
			if (masked) {
				mask = new byte[4];
				if (!readfull(mask)) { dead = true; return false; }
			}
			var data = length == 0 ? Array.Empty<byte>() : new byte[length];
			if (data.Length > 0 && !readfull(data)) { dead = true; return false; }
			if (mask != null) {
				for (var i = 0; i < data.Length; i++)
					data[i] = (byte)(data[i] ^ mask[i & 3]);
			}
			if (op == 8) { dead = true; return false; }
			if (op == 9) { writeframe(0x8A, data); continue; }
			if (op == 10) continue;
			if (op is not (0 or 1 or 2)) continue;
			if (data.Length > 0) msg.Write(data, 0, data.Length);
			if (!fin) continue;
			hold = msg.ToArray();
			holdo = 0;
			if (hold.Length == 0) continue;
			return true;
		}
	}

	bool readfull(byte[] buf) {
		var g = 0;
		while (g < buf.Length) {
			var n = readraw(buf, g, buf.Length - g);
			if (n <= 0) return false;
			g += n;
		}
		return true;
	}

	int readraw(byte[] buf, int off, int count) {
		if (preo < pre.Length) {
			var c = Math.Min(count, pre.Length - preo);
			Buffer.BlockCopy(pre, preo, buf, off, c);
			preo += c;
			return c;
		}
		try { return inner.Read(buf, off, count); }
		catch { return 0; }
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
			if (dead) throw new IOException("WebSocket 已关闭");
			writeframe(0x82, slice);
		}
	}

	void writeframe(int opcode, byte[] payload) {
		payload ??= Array.Empty<byte>();
		var len = payload.Length;
		byte[] head;
		if (len < 126)
			head = new[] { (byte)opcode, (byte)len };
		else if (len <= 0xFFFF)
			head = new[] { (byte)opcode, (byte)126, (byte)(len >> 8), (byte)len };
		else {
			head = new byte[10];
			head[0] = (byte)opcode;
			head[1] = 127;
			var n = (ulong)len;
			for (var i = 9; i >= 2; i--) {
				head[i] = (byte)n;
				n >>= 8;
			}
		}
		lock (wlock) {
			inner.Write(head, 0, head.Length);
			if (len > 0) inner.Write(payload, 0, len);
			try { inner.Flush(); } catch { }
		}
	}

	public override void Flush() {
		try { inner.Flush(); } catch { }
	}

	public override void Close() {
		dead = true;
		try { inner.Close(); } catch { }
		base.Close();
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			dead = true;
			try { inner.Close(); } catch { }
		}
		base.Dispose(disposing);
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}
