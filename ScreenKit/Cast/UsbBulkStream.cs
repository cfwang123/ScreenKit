using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace ScreenKit;

sealed class CastUsbBulkStream : Stream {
	readonly UsbEndpointReader reader;
	readonly UsbEndpointWriter writer;
	readonly int timeout;

	public CastUsbBulkStream(UsbEndpointReader reader, UsbEndpointWriter writer, int timeout = 30000) {
		this.reader = reader;
		this.writer = writer;
		this.timeout = timeout;
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
		var slice = offset == 0 && count == buffer.Length ? buffer : new byte[count];
		while (true) {
			var ec = reader.Read(slice, timeout, out var n);
			if (ec == ErrorCode.IoTimedOut || (ec == ErrorCode.None && n == 0))
				continue;
			if (ec != ErrorCode.None)
				throw new IOException($"USB 读失败 {ec}");
			if (n <= 0) continue;
			if (slice != buffer) Buffer.BlockCopy(slice, 0, buffer, offset, n);
			return n;
		}
	}

	public override void Write(byte[] buffer, int offset, int count) {
		var slice = offset == 0 && count == buffer.Length ? buffer : new byte[count];
		if (slice != buffer) Buffer.BlockCopy(buffer, offset, slice, 0, count);
		var left = count;
		var o = 0;
		while (left > 0) {
			var chunk = Math.Min(left, 16384);
			byte[] part;
			if (o == 0 && chunk == slice.Length) part = slice;
			else {
				part = new byte[chunk];
				Buffer.BlockCopy(slice, o, part, 0, chunk);
			}
			var ec = writer.Write(part, timeout, out var n);
			if (ec != ErrorCode.None) throw new IOException($"USB 写失败 {ec}");
			if (n <= 0) throw new IOException("USB 写 0 字节");
			o += n;
			left -= n;
		}
	}

	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}
