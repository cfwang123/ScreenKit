using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

static class CastProto {
	/// <summary>USB 网络共享时手机监听口（电脑主动连出，不在本机占口）。</summary>
	public const int TCP_PORT = 19519;
	public const string WS_PATH = "/cast";
	public const string USB_PIPE = "ScreenKit.CastUsb";
	public const string USB_PIPE_DOWN = "ScreenKit.CastUsbDown";
	public const string ABSTRACT = "scst";
	public const uint MAGIC = 0x53435354;
	public const byte T_VIDEO = 1, T_AUDIO = 2, T_JSON = 3;

	public static byte[] Pack(byte type, byte[] payload) {
		if (payload == null) payload = Array.Empty<byte>();
		var buf = new byte[9 + payload.Length];
		buf[0] = (byte)((MAGIC >> 24) & 0xFF);
		buf[1] = (byte)((MAGIC >> 16) & 0xFF);
		buf[2] = (byte)((MAGIC >> 8) & 0xFF);
		buf[3] = (byte)(MAGIC & 0xFF);
		buf[4] = type;
		writeu32(buf, 5, (uint)payload.Length);
		if (payload.Length > 0)
			Buffer.BlockCopy(payload, 0, buf, 9, payload.Length);
		return buf;
	}

	public static byte[] PackJson(object obj) {
		var json = JsonSerializer.Serialize(obj);
		return Pack(T_JSON, Encoding.UTF8.GetBytes(json));
	}

	public static bool TryRead(Stream s, out byte type, out byte[] payload) {
		type = 0;
		payload = null;
		var hdr = readfull(s, 9);
		if (hdr == null) return false;
		while (true) {
			uint mag = ((uint)hdr[0] << 24) | ((uint)hdr[1] << 16) | ((uint)hdr[2] << 8) | hdr[3];
			if (mag == MAGIC) break;
			Buffer.BlockCopy(hdr, 1, hdr, 0, 8);
			int b;
			try { b = s.ReadByte(); }
			catch { return false; }
			if (b < 0) return false;
			hdr[8] = (byte)b;
		}
		type = hdr[4];
		int len = (int)readu32(hdr, 5);
		if (len < 0 || len > 8 * 1024 * 1024) return false;
		payload = len == 0 ? Array.Empty<byte>() : readfull(s, len);
		return payload != null;
	}

	public static JsonObject ParseJson(byte[] payload) {
		if (payload == null || payload.Length == 0) return null;
		return JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject;
	}

	public static string Jstr(JsonObject o, string k) {
		if (o == null || string.IsNullOrEmpty(k) || o[k] == null) return "";
		try { return o[k].GetValue<string>() ?? ""; }
		catch { return o[k].ToString() ?? ""; }
	}

	public static int Jint(JsonObject o, string k) {
		if (o == null || string.IsNullOrEmpty(k) || o[k] == null) return 0;
		try { return o[k].GetValue<int>(); }
		catch { }
		try { return (int)o[k].GetValue<long>(); }
		catch { }
		return 0;
	}

	static void writeu32(byte[] b, int o, uint v) {
		b[o] = (byte)(v >> 24);
		b[o + 1] = (byte)(v >> 16);
		b[o + 2] = (byte)(v >> 8);
		b[o + 3] = (byte)v;
	}

	static uint readu32(byte[] b, int o) =>
		((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

	static byte[] readfull(Stream s, int n) {
		var buf = new byte[n];
		int g = 0;
		while (g < n) {
			int r;
			try { r = s.Read(buf, g, n - g); }
			catch { return null; }
			if (r <= 0) return null;
			g += r;
		}
		return buf;
	}
}

sealed class CastDuplexStream : Stream {
	readonly Stream rd;
	readonly Stream wr;

	public CastDuplexStream(Stream rd, Stream wr) {
		this.rd = rd;
		this.wr = wr;
	}

	public override bool CanRead => true;
	public override bool CanWrite => true;
	public override bool CanSeek => false;
	public override long Length => throw new NotSupportedException();
	public override long Position {
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count) => rd.Read(buffer, offset, count);
	public override void Write(byte[] buffer, int offset, int count) {
		wr.Write(buffer, offset, count);
		wr.Flush();
	}
	public override void Flush() => wr.Flush();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();

	protected override void Dispose(bool disposing) {
		if (disposing) {
			try { rd.Dispose(); } catch { }
			try { wr.Dispose(); } catch { }
		}
		base.Dispose(disposing);
	}
}
