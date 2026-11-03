using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScreenKit;

public enum PacketDirection {
	Unknown,
	Out,
	In
}

/// <summary>一条捕获的 TCP 报文（列表行）。</summary>
public sealed class PacketRow : INotifyPropertyChanged {
	public int No { get; set; }
	public DateTime Time { get; set; }
	public PacketDirection Direction { get; set; }
	public string Src { get; set; }
	public string Dst { get; set; }
	public int SrcPort { get; set; }
	public int DstPort { get; set; }
	public int Length { get; set; }
	public int PayloadLength { get; set; }
	/// <summary>载荷在整帧中的起始偏移。</summary>
	public int PayloadOffset { get; set; }
	public string Protocol { get; set; }
	public string Summary { get; set; }
	public string Flags { get; set; }
	public uint Seq { get; set; }
	public uint Ack { get; set; }
	public byte[] Raw { get; set; }
	public byte[] Payload { get; set; }
	public List<ProtoNode> Tree { get; set; }
	/// <summary>连接键（MakeKey），抓包列表用。</summary>
	public string ConnKey { get; set; }
	/// <summary>归属进程 PID（由连接表反查，未知为 0）。</summary>
	public int Pid { get; set; }
	/// <summary>归属进程名。</summary>
	public string ProcessName { get; set; }

	public string TimeText => Time.ToString("HH:mm:ss.fff");
	public string DirText => Direction switch {
		PacketDirection.Out => "→ 发送",
		PacketDirection.In => "← 接收",
		_ => "?"
	};
	public string InfoText => string.IsNullOrEmpty(Summary) ? Flags : Summary;
	public string SrcEndPoint => $"{Src}:{SrcPort}";
	public string DstEndPoint => $"{Dst}:{DstPort}";
	/// <summary>列表显示：进程名 或 进程名(PID)。</summary>
	public string ProcessText {
		get {
			if (Pid <= 0 && string.IsNullOrEmpty(ProcessName)) return "";
			if (string.IsNullOrEmpty(ProcessName)) return Pid > 0 ? Pid.ToString() : "";
			return Pid > 0 ? $"{ProcessName} ({Pid})" : ProcessName;
		}
	}

	public event PropertyChangedEventHandler PropertyChanged;
	public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

/// <summary>协议解析树节点（Wireshark 风格，带帧内字节范围）。</summary>
public sealed class ProtoNode {
	public string Title { get; set; }
	public string Value { get; set; }
	/// <summary>在整帧 Raw 中的起始偏移；-1 表示无对应字节。</summary>
	public int Offset { get; set; } = -1;
	/// <summary>字段长度（字节）。</summary>
	public int Length { get; set; }
	public List<ProtoNode> Children { get; set; } = new();
	public string Display {
		get {
			var core = string.IsNullOrEmpty(Value) ? Title : $"{Title}: {Value}";
			if (Offset >= 0 && Length > 0)
				return $"{core}  [{Offset}..{Offset + Length - 1}]";
			return core;
		}
	}

	public static ProtoNode Field(string title, string value, int offset, int length) => new() {
		Title = title,
		Value = value,
		Offset = offset,
		Length = length
	};

	public static ProtoNode Layer(string title, int offset, int length) => new() {
		Title = title,
		Offset = offset,
		Length = length
	};
}

/// <summary>十六进制视图：固定布局，支持按字节高亮与点击定位。</summary>
public static class HexViewHelper {
	// 行格式: "0000  XX XX XX XX XX XX XX XX  XX XX XX XX XX XX XX XX  |................|\n"
	// offset区 6, hex区 16*3+1=49, " |" = 2, ascii 16, "|" = 1, 换行
	public const int CharsPerLine = 6 + 49 + 2 + 16 + 1; // 不含换行
	public const int MaxBytes = 65536;

	public static string Format(byte[] data, int maxBytes = 4096) {
		if (data == null || data.Length == 0) return "(无数据)";
		var len = Math.Min(data.Length, maxBytes);
		var sb = new StringBuilder(len * 5);
		for (var i = 0; i < len; i += 16) {
			sb.Append(FormatLine(data, i, len));
			sb.Append('\n');
		}
		if (data.Length > maxBytes)
			sb.Append($"… 共 {data.Length} 字节，仅显示前 {maxBytes}\n");
		return sb.ToString();
	}

	public static string FormatLine(byte[] data, int start, int totalLen) {
		var sb = new StringBuilder(80);
		sb.Append($"{start:X4}  ");
		var line = Math.Min(16, totalLen - start);
		for (var j = 0; j < 16; j++) {
			if (j < line) sb.Append($"{data[start + j]:X2} ");
			else sb.Append("   ");
			if (j == 7) sb.Append(' ');
		}
		sb.Append(" |");
		for (var j = 0; j < line; j++) {
			var b = data[start + j];
			sb.Append(b is >= 32 and < 127 ? (char)b : '.');
		}
		for (var j = line; j < 16; j++) sb.Append(' ');
		sb.Append('|');
		return sb.ToString();
	}

	/// <summary>字节下标 → 文本中 hex 两位的起始字符位置（含换行按 \n 计 1）。</summary>
	public static int ByteToHexCharIndex(int byteIndex) {
		if (byteIndex < 0) return 0;
		var line = byteIndex / 16;
		var col = byteIndex % 16;
		var lineStart = line * (CharsPerLine + 1);
		// "0000  " = 6
		var hexPos = 6 + col * 3 + (col >= 8 ? 1 : 0);
		return lineStart + hexPos;
	}

	public static int ByteToHexCharLength(int byteCount) {
		// 粗略：每字节约 3 字符；多字节跨行时由调用方分段
		return Math.Max(2, byteCount * 3 - 1);
	}

	/// <summary>根据文本字符位置反查字节下标；点在非数据区返回 null。</summary>
	public static int? CharIndexToByte(string text, int charIndex, int dataLen) {
		if (string.IsNullOrEmpty(text) || charIndex < 0 || dataLen <= 0) return null;
		if (charIndex > text.Length) charIndex = text.Length;

		// 按行解析
		var pos = 0;
		var lineIdx = 0;
		while (pos <= text.Length) {
			var nl = text.IndexOf('\n', pos);
			var lineEnd = nl < 0 ? text.Length : nl;
			var lineLen = lineEnd - pos;
			if (charIndex >= pos && charIndex <= lineEnd) {
				var col = charIndex - pos;
				// 行内容
				if (col < 6) {
					// 点在偏移区 → 本行第 0 字节
					var b0 = lineIdx * 16;
					return b0 < dataLen ? b0 : null;
				}
				// hex 区: 6 .. 6+49
				if (col >= 6 && col < 6 + 49) {
					var rel = col - 6;
					// 每 3 字符一字节，第 8 字节后多 1 空格
					// layout: [XX ]x8 + [ ] + [XX ]x8
					int bcol;
					if (rel <= 8 * 3) {
						bcol = rel / 3;
						if (bcol > 7) bcol = 7;
					} else {
						var rel2 = rel - 8 * 3 - 1;
						if (rel2 < 0) bcol = 7;
						else {
							bcol = 8 + rel2 / 3;
							if (bcol > 15) bcol = 15;
						}
					}
					var bi = lineIdx * 16 + bcol;
					return bi < dataLen ? bi : (dataLen > 0 ? dataLen - 1 : null);
				}
				// ascii 区: after " |"
				var asciiStart = 6 + 49 + 2;
				if (col >= asciiStart && col < asciiStart + 16) {
					var bcol = col - asciiStart;
					var bi = lineIdx * 16 + bcol;
					return bi < dataLen ? bi : null;
				}
				return null;
			}
			if (nl < 0) break;
			pos = nl + 1;
			lineIdx++;
			if (lineIdx * 16 >= dataLen) break;
		}
		return null;
	}

	/// <summary>在 RichTextBox 中渲染带高亮的 hex。</summary>
	public static void Render(System.Windows.Controls.RichTextBox box, byte[] data, int hlStart, int hlLen, int maxBytes = 4096) {
		if (box == null) return;
		if (box.Document == null)
			box.Document = new FlowDocument();
		box.Document.Blocks.Clear();
		if (data == null || data.Length == 0) {
			box.Document.Blocks.Add(new Paragraph(new Run("(无数据)")));
			return;
		}
		var len = Math.Min(data.Length, maxBytes);
		var para = new Paragraph {
			Margin = new Thickness(0),
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12,
			LineHeight = 18
		};
		var hlEnd = hlStart >= 0 && hlLen > 0 ? hlStart + hlLen : -1;
		var brush = new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A));
		if (brush.CanFreeze) brush.Freeze();
		var brushFg = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
		if (brushFg.CanFreeze) brushFg.Freeze();

		for (var i = 0; i < len; i += 16) {
			var lineBytes = Math.Min(16, len - i);
			// 偏移
			para.Inlines.Add(new Run($"{i:X4}  ") { Foreground = Brushes.Gray });
			// hex
			for (var j = 0; j < 16; j++) {
				if (j == 8) para.Inlines.Add(new Run(" "));
				if (j < lineBytes) {
					var bi = i + j;
					var run = new Run($"{data[bi]:X2} ");
					if (hlStart >= 0 && bi >= hlStart && bi < hlEnd) {
						run.Background = brush;
						run.Foreground = brushFg;
						run.FontWeight = FontWeights.SemiBold;
					}
					para.Inlines.Add(run);
				} else {
					para.Inlines.Add(new Run("   "));
				}
			}
			para.Inlines.Add(new Run(" |") { Foreground = Brushes.Gray });
			for (var j = 0; j < 16; j++) {
				if (j < lineBytes) {
					var bi = i + j;
					var b = data[bi];
					var ch = b is >= 32 and < 127 ? (char)b : '.';
					var run = new Run(ch.ToString());
					if (hlStart >= 0 && bi >= hlStart && bi < hlEnd) {
						run.Background = brush;
						run.Foreground = brushFg;
						run.FontWeight = FontWeights.SemiBold;
					}
					para.Inlines.Add(run);
				} else {
					para.Inlines.Add(new Run(" "));
				}
			}
			para.Inlines.Add(new Run("|\n") { Foreground = Brushes.Gray });
		}
		if (data.Length > maxBytes)
			para.Inlines.Add(new Run($"… 共 {data.Length} 字节，仅显示前 {maxBytes}\n") { Foreground = Brushes.Gray });
		box.Document.Blocks.Add(para);
	}
}
