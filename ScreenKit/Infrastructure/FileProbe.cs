using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>只读看一个文件：魔数、版本、图片、媒体、文本编码。</summary>
static class FileProbe {
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
		if (pe(kind) || pe(ext)) version(rep, path);
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
			log($"exe {self.Kind} {val(self, "probe.ver.file")} {e4}");
			if (self.Kind != "exe" || val(self, "probe.ver.product").Length == 0) {
				err("本程序没有版本信息");
				return 1;
			}
		}
		finally {
			try { Directory.Delete(dir, true); } catch { }
		}
		log("=== OK：文件分析 ===");
		return 0;
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

	static void version(Report rep, string path) {
		try {
			var vi = FileVersionInfo.GetVersionInfo(path);
			add(rep, "probe.ver.product", vi.ProductName);
			add(rep, "probe.ver.file", vi.FileVersion);
			add(rep, "probe.ver.company", vi.CompanyName);
			add(rep, "probe.ver.desc", vi.FileDescription);
		}
		catch { }
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
			? $"-v error -show_entries format=duration,bit_rate -show_entries stream=codec_type,codec_name,width,height,bit_rate -of default=noprint_wrappers=1 \"{path}\""
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
		string dur = "", rate = "";
		var vcodec = "";
		var acodec = "";
		var wh = "";
		string cur = "";
		foreach (var raw in blob.Split('\n')) {
			var line = raw.Trim();
			var i = line.IndexOf('=');
			if (i <= 0) continue;
			var k = line.Substring(0, i);
			var v = line.Substring(i + 1).Trim();
			if (k == "duration" && dur.Length == 0 && v.Length > 0 && v != "N/A") dur = seconds(v);
			if (k == "bit_rate" && rate.Length == 0 && cur.Length == 0 && v.Length > 0 && v != "N/A") rate = bits(v);
		}
		foreach (var raw in blob.Split('\n')) {
			var line = raw.Trim();
			if (line.StartsWith("codec_type=")) cur = line.Substring("codec_type=".Length);
			else if (line.StartsWith("codec_name=")) {
				var name = line.Substring("codec_name=".Length);
				if (cur == "video" && vcodec.Length == 0) vcodec = name;
				if (cur == "audio" && acodec.Length == 0) acodec = name;
			}
			else if (line.StartsWith("width=") && cur == "video" && wh.Length == 0) wh = line.Substring("width=".Length);
			else if (line.StartsWith("height=") && cur == "video" && wh.Length > 0 && wh.IndexOf('x') < 0)
				wh = $"{wh}x{line.Substring("height=".Length)}";
		}
		var video = vcodec.Length == 0 ? "" : (wh.Length > 0 ? $"{vcodec} {wh}" : vcodec);
		add(rep, "probe.media.dur", dur);
		add(rep, "probe.media.rate", rate);
		add(rep, "probe.media.v", video);
		add(rep, "probe.media.a", acodec);
	}

	static void parseffmpeg(Report rep, string blob) {
		var dur = "";
		var rate = "";
		var video = "";
		var audio = "";
		foreach (var raw in blob.Split('\n')) {
			var line = raw.Trim();
			var d = line.IndexOf("Duration:", StringComparison.Ordinal);
			if (d >= 0) {
				var rest = line.Substring(d + "Duration:".Length).Trim();
				var comma = rest.IndexOf(',');
				if (comma > 0) dur = rest.Substring(0, comma).Trim();
				var b = rest.IndexOf("bitrate:", StringComparison.Ordinal);
				if (b >= 0) rate = rest.Substring(b + "bitrate:".Length).Trim();
			}
			if (line.IndexOf("Video:", StringComparison.Ordinal) >= 0 && video.Length == 0) video = stream(line, "Video:");
			if (line.IndexOf("Audio:", StringComparison.Ordinal) >= 0 && audio.Length == 0) audio = stream(line, "Audio:");
		}
		add(rep, "probe.media.dur", dur);
		add(rep, "probe.media.rate", rate);
		add(rep, "probe.media.v", video);
		add(rep, "probe.media.a", audio);
	}

	static string stream(string line, string tag) {
		var i = line.IndexOf(tag, StringComparison.Ordinal);
		if (i < 0) return "";
		var rest = line.Substring(i + tag.Length).Trim();
		if (rest.Length > 160) rest = rest.Substring(0, 160);
		return rest;
	}

	static string seconds(string v) {
		if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return v;
		var ts = TimeSpan.FromSeconds(sec);
		return ts.ToString(@"hh\:mm\:ss");
	}

	static string bits(string v) {
		if (!long.TryParse(v, out var n) || n <= 0) return v;
		if (n >= 1000000) return $"{n / 1000000.0:0.##} Mbps";
		if (n >= 1000) return $"{n / 1000.0:0} kbps";
		return $"{n} bps";
	}

	static void wavhead(Report rep, string path) {
		try {
			var b = readhead(path, 44);
			if (b.Length < 44 || !ascii(b, 0, "RIFF") || !ascii(b, 8, "WAVE")) return;
			var rate = BitConverter.ToInt32(b, 24);
			var ch = BitConverter.ToInt16(b, 22);
			var bits = BitConverter.ToInt16(b, 34);
			if (valof(rep, "probe.media.a").Length == 0)
				add(rep, "probe.media.a", $"pcm {rate} Hz {ch}ch {bits}bit");
		}
		catch { }
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
