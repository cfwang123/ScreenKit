using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>只读看一个文件：魔数、版本、图片、媒体、文本编码。EXE/DLL 另有节、导入导出和托管类型。</summary>
static partial class FileProbe {
	public sealed class Row {
		public string Key = "";
		public string Value = "";
	}

	public sealed class Report {
		public string Kind = "";
		public bool Known;
		public bool Match;
		public bool Compared;
		public readonly List<Row> Rows = new();
		public PeDump Pe;
	}

	public static Report Look(string path, out string err) {
		err = null;
		var rep = new Report();
		path = (path ?? "").Trim().Trim('"');
		if (path.Length == 0) {
			err = "empty";
			return rep;
		}
		if (!File.Exists(path)) {
			err = "missing";
			return rep;
		}
		var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
		byte[] head;
		try { head = readhead(path, 8192); }
		catch (Exception ex) {
			err = ex.Message;
			return rep;
		}
		var kind = magic(head);
		rep.Kind = kind;
		rep.Known = kind != "unknown";
		var expect = extkind(ext);
		if (rep.Known && expect.Length > 0) {
			rep.Compared = true;
			rep.Match = kind == expect || (kind == "zip" && zipok(ext)) || (pe(kind) && pe(expect));
		}
		add(rep, "probe.ext", ext.Length == 0 ? "" : "." + ext);
		add(rep, "probe.magic", kind);
		if (rep.Compared) add(rep, "probe.match", rep.Match ? "yes" : "no");
		if (!pe(kind) && !pe(ext)) filesize(rep, path);
		if (pe(kind) || pe(ext)) {
			version(rep, path, rep.Kind);
			peinfo(rep, path);
		}
		if (pe(kind)) rep.Pe = pedump(path);
		if (image(kind) || image(ext)) picture(rep, path);
		if (media(kind) || media(ext)) movie(rep, path);
		if (textlike(kind, ext, head)) encoding(rep, path, head);
		return rep;
	}

	public static int Test(Action<string> log, Action<string> err) {
		log("=== 文件分析 --test-fileprobe ===");
		var dir = Path.Combine(Path.GetTempPath(), "sk-probe-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try {
			var fake = Path.Combine(dir, "a.jpg");
			File.WriteAllBytes(fake, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 });
			var png = Look(fake, out var e1);
			log($"png-as-jpg {png.Kind} match={png.Match} {e1}");
			if (e1 != null || png.Kind != "png" || png.Match) {
				err("改后缀的 PNG 应判为不匹配");
				return 1;
			}
			var utf = Path.Combine(dir, "a.txt");
			File.WriteAllBytes(utf, new byte[] { 0xEF, 0xBB, 0xBF, 0x61 });
			var u = Look(utf, out var e2);
			log($"utf8 {val(u, "probe.enc.bom")} {val(u, "probe.enc.guess")} {e2}");
			if (val(u, "probe.enc.bom") != "UTF-8" || val(u, "probe.enc.guess") != "utf8") {
				err("UTF-8 BOM 没认出来");
				return 1;
			}
			var gbk = Path.Combine(dir, "b.txt");
			File.WriteAllBytes(gbk, new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 });
			var g = Look(gbk, out var e3);
			log($"gbk {val(g, "probe.enc.guess")} {e3}");
			if (val(g, "probe.enc.guess") != "gbk") {
				err("GBK 没认出来");
				return 1;
			}
			var self = Look(Process.GetCurrentProcess().MainModule.FileName, out var e4);
			log($"exe {self.Kind} arch={val(self, "probe.pe.arch")} clr={val(self, "probe.pe.clr")} fx={val(self, "probe.pe.framework")} {e4}");
			if (self.Kind != "exe" || val(self, "probe.pe.arch") != "x64") {
				err("本程序应为 x64 exe");
				return 1;
			}
			if (val(self, "probe.pe.managed") != "yes" || val(self, "probe.pe.framework").Length == 0) {
				err("本程序缺少 .NET 目标框架信息");
				return 1;
			}
			if (self.Pe == null) {
				err("exe 没有分析页数据");
				return 1;
			}
			log($"pe sec={self.Pe.Sections.Count} imp={self.Pe.Imports.Count} exp={self.Pe.Exports.Count} net={self.Pe.Net.Count} fx={self.Pe.Framework} plat={self.Pe.Platform} sign={self.Pe.Sign} fail={self.Pe.NetFail} err={self.Pe.Error}");
			if (self.Pe.Net.Count > 0) log($"net0 {self.Pe.Net[0].Kind} {self.Pe.Net[0].A}");
			if (self.Pe.Error.Length > 0 || self.Pe.NetFail.Length > 0 || self.Pe.Sections.Count == 0) {
				err("exe 的 PE 分析不完整");
				return 1;
			}
			if (!sechas(self.Pe, ".text")) {
				err("exe 应有 .text");
				return 1;
			}
			if (self.Pe.Framework.IndexOf("4.8", StringComparison.Ordinal) < 0 || self.Pe.Platform.Length == 0) {
				err("exe 的目标框架或平台没解析出来");
				return 1;
			}
			if (!nethas(self.Pe, "asm", "ScreenKit") || !nethas(self.Pe, "type", "MainWindow")) {
				err("exe 的程序集或公开类型没解析出来");
				return 1;
			}
			var kpath = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
			var kd = Look(kpath, out var e7);
			log($"k32 {kd.Kind} arch={val(kd, "probe.pe.arch")} exp={kd.Pe?.Exports.Count} imp={kd.Pe?.Imports.Count} sign={kd.Pe?.Sign} {e7}");
			if (e7 != null || kd.Kind != "dll" || kd.Pe == null || val(kd, "probe.pe.arch") != "x64") {
				err("kernel32 应为 x64 dll");
				return 1;
			}
			if (!exphas(kd.Pe, "GetProcAddress") || !impdll(kd.Pe, "ntdll.dll")) {
				err("kernel32 的导入或导出没解析出来");
				return 1;
			}
			if (kd.Pe.Sign.Length == 0 || kd.Pe.Sign == "unsigned" || kd.Pe.Sign.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) < 0) {
				err("kernel32 的签名没读到");
				return 1;
			}
			var wav = Path.Combine(dir, "t.wav");
			writetestwav(wav, 8000, 1, 16, 16000);
			var w = Look(wav, out var e5);
			log($"wav dur={val(w, "probe.media.dur")} codec={val(w, "probe.media.acodec")} {e5}");
			if (val(w, "probe.media.dur") != "00:00:01.000" || val(w, "probe.media.acodec").Length == 0) {
				err("WAV 时长或编码未解析");
				return 1;
			}
			var bin = Path.Combine(dir, "s.bin");
			File.WriteAllBytes(bin, new byte[1500]);
			var sz = Look(bin, out var e6);
			log($"size {val(sz, "probe.file.size")} {e6}");
			if (val(sz, "probe.file.size") != "1 KB (1500 B)") {
				err("文件大小格式不对");
				return 1;
			}
		}
		finally {
			try { Directory.Delete(dir, true); } catch { }
		}
		log("=== OK：文件分析 ===");
		return 0;
	}

	static void writetestwav(string path, int sampleRate, short channels, short bits, int dataBytes) {
		var byteRate = sampleRate * channels * bits / 8;
		var block = (short)(channels * bits / 8);
		using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
		using var w = new BinaryWriter(fs);
		w.Write(Encoding.ASCII.GetBytes("RIFF"));
		w.Write(36 + dataBytes);
		w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
		w.Write(16);
		w.Write((short)1);
		w.Write(channels);
		w.Write(sampleRate);
		w.Write(byteRate);
		w.Write(block);
		w.Write(bits);
		w.Write(Encoding.ASCII.GetBytes("data"));
		w.Write(dataBytes);
		w.Write(new byte[dataBytes]);
	}

	static bool sechas(PeDump d, string name) {
		if (d == null) return false;
		foreach (var s in d.Sections)
			if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	static bool impdll(PeDump d, string dll) {
		if (d == null) return false;
		foreach (var i in d.Imports)
			if (string.Equals(i.Dll, dll, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	static bool exphas(PeDump d, string name) {
		if (d == null) return false;
		foreach (var e in d.Exports)
			if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	static bool nethas(PeDump d, string kind, string part) {
		if (d == null) return false;
		foreach (var n in d.Net) {
			if (n.Kind != kind) continue;
			if ((n.A ?? "").IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			if ((n.B ?? "").IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		}
		return false;
	}

	static string val(Report rep, string key) {
		foreach (var r in rep.Rows)
			if (r.Key == key) return r.Value ?? "";
		return "";
	}

	static void add(Report rep, string key, string value) {
		value = (value ?? "").Trim();
		if (value.Length == 0) return;
		rep.Rows.Add(new Row { Key = key, Value = value });
	}

	static void add0(Report rep, string key, string value) {
		rep.Rows.Add(new Row { Key = key, Value = (value ?? "").Trim() });
	}

	static byte[] readhead(string path, int n) {
		using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		var buf = new byte[n];
		var got = fs.Read(buf, 0, n);
		if (got == buf.Length) return buf;
		var cut = new byte[got];
		Buffer.BlockCopy(buf, 0, cut, 0, got);
		return cut;
	}

	static string magic(byte[] b) {
		var n = b.Length;
		if (n >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
		if (n >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
		if (n >= 6 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F') return "gif";
		if (n >= 2 && b[0] == (byte)'B' && b[1] == (byte)'M') return "bmp";
		if (n >= 12 && ascii(b, 0, "RIFF") && ascii(b, 8, "WEBP")) return "webp";
		if (n >= 12 && ascii(b, 0, "RIFF") && ascii(b, 8, "WAVE")) return "wav";
		if (n >= 12 && ascii(b, 0, "RIFF") && ascii(b, 8, "AVI ")) return "avi";
		if (n >= 4 && ((b[0] == (byte)'I' && b[1] == (byte)'I') || (b[0] == (byte)'M' && b[1] == (byte)'M'))) return "tif";
		if (n >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) return "ico";
		if (n >= 5 && ascii(b, 0, "%PDF")) return "pdf";
		if (n >= 6 && b[0] == 0x37 && b[1] == 0x7A && b[2] == 0xBC && b[3] == 0xAF) return "7z";
		if (n >= 7 && ascii(b, 0, "Rar!")) return "rar";
		if (n >= 2 && b[0] == 0x1F && b[1] == 0x8B) return "gz";
		if (n >= 4 && ascii(b, 0, "PK")) return "zip";
		if (n >= 12 && ascii(b, 4, "ftyp")) return "mp4";
		if (n >= 4 && ascii(b, 0, "fLaC")) return "flac";
		if (n >= 4 && ascii(b, 0, "OggS")) return "ogg";
		if (n >= 3 && ascii(b, 0, "ID3")) return "mp3";
		if (n >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return "mp3";
		if (n >= 16 && ascii(b, 0, "SQLite format 3")) return "sqlite";
		if (n >= 4 && ascii(b, 0, "MSCF")) return "cab";
		var peKind = pekind(b);
		if (peKind.Length > 0) return peKind;
		return "unknown";
	}

	static string pekind(byte[] b) {
		if (b.Length < 64 || b[0] != (byte)'M' || b[1] != (byte)'Z') return "";
		var off = BitConverter.ToInt32(b, 0x3C);
		if (off < 0 || off + 24 > b.Length) return "exe";
		if (b[off] != (byte)'P' || b[off + 1] != (byte)'E' || b[off + 2] != 0 || b[off + 3] != 0) return "exe";
		var chars = BitConverter.ToUInt16(b, off + 22);
		if ((chars & 0x2000) != 0) return "dll";
		return "exe";
	}

	static bool ascii(byte[] b, int at, string s) {
		if (at + s.Length > b.Length) return false;
		for (var i = 0; i < s.Length; i++)
			if (b[at + i] != (byte)s[i]) return false;
		return true;
	}

	static string extkind(string ext) {
		if (ext == "jpeg") return "jpg";
		if (ext == "tiff") return "tif";
		if (ext == "mpeg" || ext == "mpg" || ext == "m4v" || ext == "mov" || ext == "m4a") return "mp4";
		if (ext == "jpg" || ext == "png" || ext == "gif" || ext == "bmp" || ext == "webp" || ext == "tif"
			|| ext == "ico" || ext == "pdf" || ext == "zip" || ext == "7z" || ext == "rar" || ext == "gz"
			|| ext == "exe" || ext == "dll" || ext == "wav" || ext == "avi" || ext == "mp4" || ext == "mp3"
			|| ext == "flac" || ext == "ogg" || ext == "sqlite" || ext == "cab")
			return ext;
		if (zipok(ext)) return "zip";
		if (ext == "sys") return "exe";
		return "";
	}

	static bool zipok(string ext) =>
		ext == "docx" || ext == "xlsx" || ext == "pptx" || ext == "odt" || ext == "jar" || ext == "apk" || ext == "epub";

	static bool pe(string k) => k == "exe" || k == "dll";

	static bool image(string k) =>
		k == "png" || k == "jpg" || k == "gif" || k == "bmp" || k == "webp" || k == "tif" || k == "ico";

	static bool media(string k) =>
		k == "mp4" || k == "mp3" || k == "wav" || k == "flac" || k == "ogg" || k == "avi" || k == "webp";

	static void version(Report rep, string path, string kind) {
		long size = 0;
		DateTime mtime = default;
		try {
			var fi = new FileInfo(path);
			size = fi.Length;
			mtime = fi.LastWriteTime;
		}
		catch { }
		try {
			var vi = FileVersionInfo.GetVersionInfo(path);
			add0(rep, "probe.ver.desc", vi.FileDescription);
			add(rep, "probe.ver.type", kind == "dll" ? "dll" : "exe");
			add0(rep, "probe.ver.file", vi.FileVersion);
			add0(rep, "probe.ver.product", vi.ProductName);
			add0(rep, "probe.ver.productver", vi.ProductVersion);
			add0(rep, "probe.ver.copyright", vi.LegalCopyright);
			if (size > 0) add(rep, "probe.file.size", formatsize(size));
			if (mtime != default) add(rep, "probe.file.mtime", mtime.ToString("G"));
			add0(rep, "probe.ver.lang", vi.Language);
			add0(rep, "probe.ver.orig", vi.OriginalFilename);
			add(rep, "probe.ver.company", vi.CompanyName);
			add(rep, "probe.ver.internal", vi.InternalName);
			add(rep, "probe.ver.trademark", vi.LegalTrademarks);
			add(rep, "probe.ver.comments", vi.Comments);
		}
		catch { }
	}

	static void filesize(Report rep, string path) {
		try {
			var len = new FileInfo(path).Length;
			add(rep, "probe.file.size", formatsize(len));
		}
		catch { }
	}

	static string formatsize(long n) {
		if (n < 0) n = 0;
		if (n < 1024) return $"{n} B";
		string human;
		if (n < 1024 * 1024) human = $"{(n + 511) / 1024} KB";
		else if (n < 1024L * 1024 * 1024) human = $"{n / (1024.0 * 1024):0.##} MB";
		else human = $"{n / (1024.0 * 1024 * 1024):0.##} GB";
		return $"{human} ({n} B)";
	}

	static void peinfo(Report rep, string path) {
		byte[] buf;
		try {
			var len = new FileInfo(path).Length;
			var cap = (int)Math.Min(len, 64L * 1024 * 1024);
			if (cap < 512) return;
			buf = readhead(path, cap);
		}
		catch { return; }
		if (buf.Length < 64 || buf[0] != (byte)'M' || buf[1] != (byte)'Z') return;
		var pe = BitConverter.ToInt32(buf, 0x3C);
		if (pe < 0 || pe + 248 > buf.Length) return;
		if (buf[pe] != (byte)'P' || buf[pe + 1] != (byte)'E') return;
		var machine = BitConverter.ToUInt16(buf, pe + 4);
		add(rep, "probe.pe.arch", pemachine(machine));
		var opt = pe + 24;
		var sizeOpt = BitConverter.ToUInt16(buf, pe + 20);
		if (opt + sizeOpt > buf.Length) return;
		var magic = BitConverter.ToUInt16(buf, opt);
		var pe32 = magic == 0x10b;
		var pe64 = magic == 0x20b;
		if (!pe32 && !pe64) return;
		var sub = BitConverter.ToUInt16(buf, opt + 68);
		add(rep, "probe.pe.subsys", pesubsys(sub));
		var dd = opt + (pe64 ? 112 : 96);
		if (dd + 8 * 15 + 8 > buf.Length) return;
		var clrRva = BitConverter.ToUInt32(buf, dd + 8 * 14);
		var clrSz = BitConverter.ToUInt32(buf, dd + 8 * 14 + 4);
		if (clrRva == 0 || clrSz < 72) {
			add(rep, "probe.pe.managed", "no");
			return;
		}
		add(rep, "probe.pe.managed", "yes");
		var clrOff = rvatooff(buf, pe, sizeOpt, clrRva);
		if (clrOff >= 0 && clrOff + 16 <= buf.Length) {
			var maj = BitConverter.ToUInt16(buf, clrOff + 4);
			var min = BitConverter.ToUInt16(buf, clrOff + 6);
			var metaVer = "";
			var metaRva = BitConverter.ToUInt32(buf, clrOff + 8);
			var metaOff = rvatooff(buf, pe, sizeOpt, metaRva);
			if (metaOff >= 0 && metaOff + 32 <= buf.Length && ascii(buf, metaOff, "BSJB")) {
				var verLen = BitConverter.ToInt32(buf, metaOff + 12);
				if (verLen > 0 && verLen < 256 && metaOff + 16 + verLen <= buf.Length)
					metaVer = Encoding.UTF8.GetString(buf, metaOff + 16, verLen).Trim('\0');
			}
			if (metaVer.Length > 0) add(rep, "probe.pe.clr", metaVer.TrimStart('v'));
			else add(rep, "probe.pe.clr", clrver(maj, min));
		}
		var fx = targetfx(buf);
		if (fx.Length > 0) add(rep, "probe.pe.framework", fx);
	}

	static string clrver(ushort maj, ushort min) {
		if (maj == 2 && min == 5) return "4.0";
		if (maj == 2 && min == 0) return "2.0";
		return $"{maj}.{min}";
	}

	static string pemachine(ushort m) {
		switch (m) {
			case 0x014c: return "x86";
			case 0x8664: return "x64";
			case 0xAA64: return "arm64";
			case 0x01c0: return "arm";
			case 0x01c4: return "armnt";
			case 0x0200: return "ia64";
			default: return $"0x{m:X4}";
		}
	}

	static string pesubsys(ushort s) {
		switch (s) {
			case 2: return "gui";
			case 3: return "console";
			case 1: return "native";
			default: return s == 0 ? "" : $"subsys{s}";
		}
	}

	static int rvatooff(byte[] buf, int pe, ushort sizeOpt, uint rva) {
		var nsec = BitConverter.ToUInt16(buf, pe + 6);
		var sec = pe + 24 + sizeOpt;
		for (var i = 0; i < nsec; i++) {
			var at = sec + i * 40;
			if (at + 40 > buf.Length) break;
			var va = BitConverter.ToUInt32(buf, at + 12);
			var vsz = BitConverter.ToUInt32(buf, at + 8);
			var raw = BitConverter.ToUInt32(buf, at + 20);
			var rawsz = BitConverter.ToUInt32(buf, at + 16);
			var span = Math.Max(vsz, rawsz);
			if (span == 0) span = vsz > 0 ? vsz : rawsz;
			if (rva < va || rva >= va + span) continue;
			if (raw == 0) return -1;
			return (int)(raw + (rva - va));
		}
		return -1;
	}

	static string targetfx(byte[] b) {
		var fx = readfx(b, ".NETFramework,Version=v", ".NET Framework ");
		if (fx.Length > 0) return fx;
		fx = readfx(b, ".NETCoreApp,Version=v", ".NET ");
		if (fx.Length > 0) return fx;
		return readfx(b, ".NETStandard,Version=v", ".NET Standard ");
	}

	static string readfx(byte[] b, string mark, string prefix) {
		var pat = Encoding.ASCII.GetBytes(mark);
		var i = indexof(b, pat, 0);
		if (i < 0) return "";
		var j = i + pat.Length;
		var k = j;
		while (k < b.Length && (b[k] == (byte)'.' || (b[k] >= (byte)'0' && b[k] <= (byte)'9'))) k++;
		if (k <= j) return "";
		return prefix + Encoding.ASCII.GetString(b, j, k - j);
	}

	static int indexof(byte[] b, byte[] pat, int start) {
		if (pat.Length == 0 || start >= b.Length) return -1;
		var end = b.Length - pat.Length;
		for (var i = start; i <= end; i++) {
			var ok = true;
			for (var j = 0; j < pat.Length; j++)
				if (b[i + j] != pat[j]) { ok = false; break; }
			if (ok) return i;
		}
		return -1;
	}

	static void picture(Report rep, string path) {
		try {
			using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
			add(rep, "probe.img.w", frame.PixelWidth.ToString());
			add(rep, "probe.img.h", frame.PixelHeight.ToString());
			if (frame.Metadata is BitmapMetadata meta) {
				try { add(rep, "probe.img.time", meta.DateTaken); } catch { }
				add(rep, "probe.img.make", qstr(meta, "/app1/ifd/{ushort=271}"));
				add(rep, "probe.img.model", qstr(meta, "/app1/ifd/{ushort=272}"));
				add(rep, "probe.img.gps", gps(meta));
			}
		}
		catch { }
	}

	static string qstr(BitmapMetadata meta, string query) {
		try {
			var v = meta.GetQuery(query);
			return v == null ? "" : v.ToString().Trim().Trim('\0');
		}
		catch { return ""; }
	}

	static string gps(BitmapMetadata meta) {
		var lat = ration(meta, "/app1/ifd/gps/{ushort=2}");
		var ns = qstr(meta, "/app1/ifd/gps/{ushort=1}");
		var lon = ration(meta, "/app1/ifd/gps/{ushort=4}");
		var ew = qstr(meta, "/app1/ifd/gps/{ushort=3}");
		if (lat.Length == 0 && lon.Length == 0) return "";
		return $"{lat} {ns}, {lon} {ew}".Trim().Trim(',');
	}

	static string ration(BitmapMetadata meta, string query) {
		try {
			var v = meta.GetQuery(query);
			if (v is ulong[] u && u.Length >= 6) {
				return $"{deg(u[0], u[1])}° {deg(u[2], u[3])}' {deg(u[4], u[5])}\"";
			}
			if (v == null) return "";
			return v.ToString().Trim();
		}
		catch { return ""; }
	}

	static string deg(ulong num, ulong den) {
		if (den == 0) return num.ToString();
		return ((double)num / den).ToString("0.###", CultureInfo.InvariantCulture);
	}

	static void movie(Report rep, string path) {
		var exe = probeexe();
		if (exe == null) {
			wavhead(rep, path);
			return;
		}
		var ffprobe = exe.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
			? Path.Combine(Path.GetDirectoryName(exe) ?? "", "ffprobe.exe")
			: exe;
		if (!File.Exists(ffprobe)) ffprobe = exe;
		var args = ffprobe.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase)
			? $"-v error -show_entries format=duration,bit_rate -show_entries stream=codec_type,codec_name,width,height,bit_rate,sample_rate,channels,r_frame_rate -of default=noprint_wrappers=1 \"{path}\""
			: $"-hide_banner -i \"{path}\"";
		string stdout, stderr;
		if (!run(ffprobe, args, out stdout, out stderr)) {
			wavhead(rep, path);
			return;
		}
		var blob = stdout + "\n" + stderr;
		if (ffprobe.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase)) parseprobe(rep, blob);
		else parseffmpeg(rep, blob);
		if (valof(rep, "probe.media.dur").Length == 0) wavhead(rep, path);
	}

	static string valof(Report rep, string key) {
		foreach (var r in rep.Rows)
			if (r.Key == key) return r.Value;
		return "";
	}

	static void parseprobe(Report rep, string blob) {
		string dur = "", fmtRate = "";
		var vcodec = "";
		var acodec = "";
		var vw = "";
		var vh = "";
		var vrate = "";
		var arate = "";
		var asample = "";
		var ach = "";
		var fps = "";
		var cur = "";
		foreach (var raw in blob.Split('\n')) {
			var line = raw.Trim();
			var i = line.IndexOf('=');
			if (i <= 0) continue;
			var k = line.Substring(0, i);
			var v = line.Substring(i + 1).Trim();
			if (v.Length == 0 || v == "N/A") continue;
			if (k == "codec_type") cur = v;
			else if (k == "codec_name") {
				if (cur == "video" && vcodec.Length == 0) vcodec = v;
				if (cur == "audio" && acodec.Length == 0) acodec = v;
			}
			else if (k == "width" && cur == "video" && vw.Length == 0) vw = v;
			else if (k == "height" && cur == "video" && vh.Length == 0) vh = v;
			else if (k == "bit_rate") {
				var br = formatbps(v);
				if (cur == "video" && vrate.Length == 0) vrate = br;
				else if (cur == "audio" && arate.Length == 0) arate = br;
				else if (fmtRate.Length == 0) fmtRate = br;
			}
			else if (k == "sample_rate" && cur == "audio" && asample.Length == 0) asample = v;
			else if (k == "channels" && cur == "audio" && ach.Length == 0) ach = v;
			else if (k == "r_frame_rate" && cur == "video" && fps.Length == 0) fps = framerate(v);
			else if (k == "duration" && dur.Length == 0) dur = durms(v);
		}
		mediarows(rep, dur, fmtRate, vcodec, acodec, vw, vh, vrate, arate, asample, ach, fps);
	}

	static void parseffmpeg(Report rep, string blob) {
		var dur = "";
		var rate = "";
		var vline = "";
		var aline = "";
		foreach (var raw in blob.Split('\n')) {
			var line = raw.Trim();
			var d = line.IndexOf("Duration:", StringComparison.Ordinal);
			if (d >= 0) {
				var rest = line.Substring(d + "Duration:".Length).Trim();
				var comma = rest.IndexOf(',');
				if (comma > 0) dur = durfromffmpeg(rest.Substring(0, comma).Trim());
				var b = rest.IndexOf("bitrate:", StringComparison.Ordinal);
				if (b >= 0) rate = rest.Substring(b + "bitrate:".Length).Trim();
			}
			if (line.IndexOf("Video:", StringComparison.Ordinal) >= 0 && vline.Length == 0) vline = stream(line, "Video:");
			if (line.IndexOf("Audio:", StringComparison.Ordinal) >= 0 && aline.Length == 0) aline = stream(line, "Audio:");
		}
		var vcodec = "";
		var vw = "";
		var vh = "";
		var fps = "";
		if (vline.Length > 0) {
			var wh = vline.IndexOf("x", StringComparison.Ordinal);
			if (wh > 0) {
				var sp = vline.LastIndexOf(' ', wh);
				if (sp >= 0 && wh - sp > 1) {
					vcodec = vline.Substring(0, sp).Trim();
					var size = vline.Substring(sp + 1);
					var x = size.IndexOf('x');
					if (x > 0) {
						vw = size.Substring(0, x);
						var tail = size.Substring(x + 1);
						var sp2 = tail.IndexOf(' ');
						vh = sp2 > 0 ? tail.Substring(0, sp2) : tail;
						var fp = tail.IndexOf("fps", StringComparison.Ordinal);
						if (fp > 0) fps = tail.Substring(0, fp).Trim();
					}
				}
			}
			if (vcodec.Length == 0) vcodec = vline.Split(' ')[0];
		}
		var acodec = aline.Length == 0 ? "" : aline.Split(',')[0].Trim();
		mediarows(rep, dur, rate, vcodec, acodec, vw, vh, "", "", "", "", fps);
	}

	static void mediarows(Report rep, string dur, string fmtRate, string vcodec, string acodec, string vw, string vh, string vrate, string arate, string asample, string ach, string fps) {
		add(rep, "probe.media.dur", dur);
		if (vw.Length > 0 && vh.Length > 0) add(rep, "probe.media.res", $"{vw}x{vh}");
		add(rep, "probe.media.vcodec", vcodec);
		add(rep, "probe.media.acodec", acodec);
		var sample = audiosample(asample, ach);
		add(rep, "probe.media.sample", sample);
		add(rep, "probe.media.rate", fmtRate);
		add(rep, "probe.media.vrate", vrate);
		add(rep, "probe.media.arate", arate);
		add(rep, "probe.media.fps", fps);
	}

	static string audiosample(string hz, string ch) {
		if (hz.Length == 0 && ch.Length == 0) return "";
		if (hz.Length > 0 && ch.Length > 0) return $"{hz} Hz, {ch} ch";
		if (hz.Length > 0) return $"{hz} Hz";
		return $"{ch} ch";
	}

	static string stream(string line, string tag) {
		var i = line.IndexOf(tag, StringComparison.Ordinal);
		if (i < 0) return "";
		var rest = line.Substring(i + tag.Length).Trim();
		if (rest.Length > 160) rest = rest.Substring(0, 160);
		return rest;
	}

	static string durms(string v) {
		if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return v;
		var ms = (long)Math.Round(sec * 1000.0, MidpointRounding.AwayFromZero);
		var ts = TimeSpan.FromMilliseconds(ms);
		return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
	}

	static string durfromffmpeg(string s) {
		if (s.IndexOf('.') < 0 && s.IndexOf(':') >= 0) {
			if (TimeSpan.TryParse(s, out var ts)) return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.000";
			return s;
		}
		var dot = s.LastIndexOf('.');
		if (dot < 0) return durms(s);
		var head = s.Substring(0, dot);
		var frac = s.Substring(dot + 1);
		if (!TimeSpan.TryParse(head, out var t)) return s;
		var ms = 0;
		if (frac.Length == 1 && int.TryParse(frac, out var d1)) ms = d1 * 100;
		else if (frac.Length == 2 && int.TryParse(frac, out var d2)) ms = d2 * 10;
		else if (frac.Length >= 3 && int.TryParse(frac.Substring(0, 3), out var d3)) ms = d3;
		var total = TimeSpan.FromMilliseconds(t.TotalMilliseconds + ms);
		return $"{total.Hours:D2}:{total.Minutes:D2}:{total.Seconds:D2}.{total.Milliseconds:D3}";
	}

	static string framerate(string v) {
		var slash = v.IndexOf('/');
		if (slash <= 0) return v;
		if (!double.TryParse(v.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return v;
		if (!double.TryParse(v.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var den) || den == 0) return v;
		return $"{num / den:0.###}";
	}

	static string formatbps(string v) {
		if (!long.TryParse(v, out var n) || n <= 0) return v;
		if (n >= 1000000) return $"{n / 1000000.0:0.##} Mbps";
		if (n >= 1000) return $"{n / 1000.0:0} kbps";
		return $"{n} bps";
	}

	static void wavhead(Report rep, string path) {
		try {
			if (!wavinfo(path, out var rate, out var ch, out var bits, out var dataBytes)) return;
			if (valof(rep, "probe.media.dur").Length == 0) {
				var bps = rate * ch * (bits / 8);
				if (bps > 0 && dataBytes > 0) add(rep, "probe.media.dur", durms(((double)dataBytes / bps).ToString(CultureInfo.InvariantCulture)));
			}
			if (valof(rep, "probe.media.acodec").Length == 0) add(rep, "probe.media.acodec", $"pcm_s{bits}le");
			if (valof(rep, "probe.media.sample").Length == 0) add(rep, "probe.media.sample", audiosample(rate.ToString(), ch.ToString()));
			if (valof(rep, "probe.media.rate").Length == 0) {
				var bps = rate * ch * bits;
				if (bps > 0) add(rep, "probe.media.rate", formatbps(bps.ToString()));
			}
		}
		catch { }
	}

	static bool wavinfo(string path, out int rate, out int ch, out int bits, out long dataBytes) {
		rate = 0;
		ch = 0;
		bits = 0;
		dataBytes = 0;
		try {
			using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			var b = new byte[44];
			if (fs.Read(b, 0, 44) < 44 || !ascii(b, 0, "RIFF") || !ascii(b, 8, "WAVE")) return false;
			ch = BitConverter.ToInt16(b, 22);
			rate = BitConverter.ToInt32(b, 24);
			bits = BitConverter.ToInt16(b, 34);
			fs.Position = 12;
			var hdr = new byte[8];
			while (fs.Read(hdr, 0, 8) == 8) {
				var id = Encoding.ASCII.GetString(hdr, 0, 4);
				var sz = BitConverter.ToInt32(hdr, 4);
				if (id == "data") {
					dataBytes = sz;
					return rate > 0 && ch > 0 && bits > 0;
				}
				if (sz < 0) break;
				fs.Position += sz + (sz & 1);
			}
		}
		catch { return false; }
		return rate > 0 && ch > 0 && bits > 0;
	}

	static bool run(string exe, string args, out string stdout, out string stderr) {
		stdout = "";
		stderr = "";
		try {
			var psi = new ProcessStartInfo {
				FileName = exe,
				Arguments = args,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8,
			};
			using var p = Process.Start(psi);
			if (p == null) return false;
			stdout = p.StandardOutput.ReadToEnd();
			stderr = p.StandardError.ReadToEnd();
			if (!p.WaitForExit(8000)) {
				try { p.Kill(); } catch { }
				return false;
			}
			return true;
		}
		catch { return false; }
	}

	static string probeexe() {
		var ffmpeg = VidConvert.FindExe();
		if (ffmpeg == null) return null;
		var dir = Path.GetDirectoryName(ffmpeg) ?? "";
		var probe = Path.Combine(dir, "ffprobe.exe");
		if (File.Exists(probe)) return probe;
		return ffmpeg;
	}

	static bool textlike(string kind, string ext, byte[] head) {
		if (kind != "unknown") return false;
		if (ext == "txt" || ext == "csv" || ext == "json" || ext == "xml" || ext == "log"
			|| ext == "md" || ext == "cs" || ext == "html" || ext == "htm" || ext == "toml" || ext == "ini")
			return true;
		var n = Math.Min(head.Length, 512);
		if (n == 0) return false;
		var odd = 0;
		for (var i = 0; i < n; i++) {
			if (head[i] == 0) return false;
			if (head[i] < 9 || (head[i] > 13 && head[i] < 32)) odd++;
		}
		return odd * 10 < n;
	}

	static void encoding(Report rep, string path, byte[] head) {
		byte[] buf;
		try {
			using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			buf = new byte[Math.Min(256 * 1024, (int)Math.Min(fs.Length, int.MaxValue))];
			var got = fs.Read(buf, 0, buf.Length);
			if (got < buf.Length) {
				var cut = new byte[got];
				Buffer.BlockCopy(buf, 0, cut, 0, got);
				buf = cut;
			}
		}
		catch { buf = head; }
		var bom = "none";
		var skip = 0;
		if (buf.Length >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) { bom = "UTF-8"; skip = 3; }
		else if (buf.Length >= 2 && buf[0] == 0xFF && buf[1] == 0xFE) { bom = "UTF-16 LE"; skip = 2; }
		else if (buf.Length >= 2 && buf[0] == 0xFE && buf[1] == 0xFF) { bom = "UTF-16 BE"; skip = 2; }
		add(rep, "probe.enc.bom", bom);
		if (bom.StartsWith("UTF-16")) {
			add(rep, "probe.enc.guess", "utf16");
			return;
		}
		if (bom == "UTF-8") {
			add(rep, "probe.enc.guess", "utf8");
			return;
		}
		var body = buf;
		if (skip > 0 && skip < buf.Length) {
			body = new byte[buf.Length - skip];
			Buffer.BlockCopy(buf, skip, body, 0, body.Length);
		}
		if (body.Length == 0) return;
		var ascii = true;
		foreach (var c in body)
			if (c >= 128) ascii = false;
		if (ascii) {
			add(rep, "probe.enc.guess", "ascii");
			return;
		}
		if (utf8ok(body)) {
			add(rep, "probe.enc.guess", "utf8");
			return;
		}
		if (gbkok(body)) {
			add(rep, "probe.enc.guess", "gbk");
			return;
		}
		add(rep, "probe.enc.guess", "unknown");
	}

	static bool utf8ok(byte[] b) {
		var i = 0;
		while (i < b.Length) {
			var c = b[i];
			int need;
			if (c <= 0x7F) { i++; continue; }
			if (c >= 0xC2 && c <= 0xDF) need = 1;
			else if (c >= 0xE0 && c <= 0xEF) need = 2;
			else if (c >= 0xF0 && c <= 0xF4) need = 3;
			else return false;
			if (i + need >= b.Length) return false;
			for (var k = 1; k <= need; k++)
				if ((b[i + k] & 0xC0) != 0x80) return false;
			i += need + 1;
		}
		return true;
	}

	static bool gbkok(byte[] b) {
		try {
			var enc = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
			enc.GetString(b);
			return true;
		}
		catch { return false; }
	}
}
