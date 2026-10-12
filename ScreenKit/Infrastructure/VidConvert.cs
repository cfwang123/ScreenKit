using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ScreenKit;

/// <summary>批量视频转换：ffmpeg 转封装/编码，可限制较短边。</summary>
static class VidConvert {
	const int MAXFILES = 500;
	const long MINEXE = 2_000_000;
	public const string OUTBESIDE = "beside";
	public const string OUTOTHER = "other";
	public const string OUTREPLACE = "replace";
	public const string OUTRECYCLE = "recycle";

	static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase) {
		".mp4", ".m4v", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".flv",
		".ts", ".mts", ".m2ts", ".mpeg", ".mpg", ".3gp", ".ogv", ".asf",
	};

	public static bool IsVideoPath(string path) {
		var ext = Path.GetExtension(path ?? "");
		return Exts.Contains(ext);
	}

	public static string NormFmt(string fmt) {
		var f = (fmt ?? "mp4").Trim().ToLowerInvariant();
		if (f is "mkv") return "mkv";
		if (f is "webm") return "webm";
		return "mp4";
	}

	public static int CrfMax(string fmt) => NormFmt(fmt) == "webm" ? 63 : 51;

	public static int NormCrf(string fmt, int crf) {
		if (crf < 0) crf = 23;
		return Compat.Clamp(crf, 0, CrfMax(fmt));
	}

	public static string NormOutMode(string mode) {
		var m = (mode ?? "").Trim().ToLowerInvariant();
		if (m is "other" or "replace" or "recycle") return m;
		return OUTBESIDE;
	}

	public static bool IsReplace(string mode) {
		mode = NormOutMode(mode);
		return mode is OUTREPLACE or OUTRECYCLE;
	}

	public static List<string> Collect(IEnumerable<string> paths) {
		var r = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (paths == null) return r;
		foreach (var p in paths)
			add(p);
		return r;

		void add(string p) {
			if (string.IsNullOrWhiteSpace(p) || r.Count >= MAXFILES) return;
			try { p = Path.GetFullPath(p); }
			catch { return; }
			if (Directory.Exists(p)) {
				string[] files;
				try { files = Directory.GetFiles(p, "*", SearchOption.AllDirectories); }
				catch { return; }
				foreach (var f in files) {
					if (r.Count >= MAXFILES) return;
					addfile(f);
				}
				return;
			}
			addfile(p);
		}

		void addfile(string f) {
			if (r.Count >= MAXFILES) return;
			if (string.IsNullOrWhiteSpace(f) || !File.Exists(f) || !IsVideoPath(f)) return;
			string full;
			try { full = Path.GetFullPath(f); }
			catch { return; }
			if (!seen.Add(full)) return;
			r.Add(full);
		}
	}

	public static string MakeOutPath(string src, string fmt, string mode, string customDir, ISet<string> reserved) {
		fmt = NormFmt(fmt);
		mode = NormOutMode(mode);
		var ext = "." + fmt;
		var name = Path.GetFileNameWithoutExtension(src);
		if (string.IsNullOrWhiteSpace(name)) name = "video";
		string dir;
		if (mode == OUTOTHER) {
			dir = (customDir ?? "").Trim();
			if (dir.Length == 0)
				throw new InvalidOperationException(Loc.T("vidconv.nodir"));
		}
		else if (IsReplace(mode)) {
			dir = Path.GetDirectoryName(src);
			if (string.IsNullOrEmpty(dir))
				dir = AppDomain.CurrentDomain.BaseDirectory;
		}
		else {
			var srcDir = Path.GetDirectoryName(src);
			if (string.IsNullOrEmpty(srcDir))
				srcDir = AppDomain.CurrentDomain.BaseDirectory;
			dir = Path.Combine(srcDir, "output");
		}
		var path = Path.Combine(dir, name + ext);
		if (IsReplace(mode) && string.Equals(path, src, StringComparison.OrdinalIgnoreCase)) {
			reserved?.Add(path);
			return path;
		}
		path = unique(path, reserved);
		reserved?.Add(path);
		return path;
	}

	/// <summary>较短边超过 limit 时等比缩小到该边，宽高取偶数，不放大。</summary>
	public static void Fit(int w, int h, int limit, out int ow, out int oh) {
		ow = w;
		oh = h;
		if (w < 2 || h < 2 || limit < 16) return;
		var m = Math.Min(w, h);
		if (m <= limit) return;
		var scale = limit / (double)m;
		ow = even((int)Math.Round(w * scale));
		oh = even((int)Math.Round(h * scale));
	}

	public static string FindExe() {
		var cands = new List<string> {
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg64", "ffmpeg.exe"),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
			@"C:\bin\ffmpeg.exe",
		};
		try {
			var path = Environment.GetEnvironmentVariable("PATH") ?? "";
			foreach (var dir in path.Split(Path.PathSeparator)) {
				if (string.IsNullOrWhiteSpace(dir)) continue;
				cands.Add(Path.Combine(dir.Trim(), "ffmpeg.exe"));
			}
		}
		catch { }
		foreach (var c in cands) {
			try {
				if (File.Exists(c) && new FileInfo(c).Length >= MINEXE)
					return c;
			}
			catch { }
		}
		return null;
	}

	public static List<string> Args(string src, string dst, string fmt, int crf,
		bool shortEn, int shortPx, bool audio, bool hasAudio, int w, int h) {
		fmt = NormFmt(fmt);
		crf = NormCrf(fmt, crf);
		var args = new List<string> {
			"-y", "-hide_banner", "-loglevel", "error",
			"-i", src,
			"-map", "0:v:0",
		};
		var keepA = audio && hasAudio;
		if (keepA) args.AddRange(new[] { "-map", "0:a:0?" });
		if (shortEn && shortPx >= 16 && w >= 2 && h >= 2) {
			Fit(w, h, shortPx, out var ow, out var oh);
			if (ow != w || oh != h)
				args.AddRange(new[] { "-vf", "scale=" + ow + ":" + oh });
		}
		else if (shortEn && shortPx >= 16 && (w < 2 || h < 2)) {
			var n = shortPx.ToString(CultureInfo.InvariantCulture);
			var vf = "scale=w='trunc(iw*min(1\\," + n + "/min(iw\\,ih))/2)*2':h='trunc(ih*min(1\\," + n + "/min(iw\\,ih))/2)*2'";
			args.AddRange(new[] { "-vf", vf });
		}
		if (fmt == "webm") {
			args.AddRange(new[] {
				"-c:v", "libvpx-vp9", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-b:v", "0",
				"-deadline", "good", "-cpu-used", "2", "-row-mt", "1", "-pix_fmt", "yuv420p",
			});
			if (keepA) args.AddRange(new[] { "-c:a", "libopus", "-b:a", "96k" });
		}
		else {
			args.AddRange(new[] {
				"-c:v", "libx264", "-crf", crf.ToString(CultureInfo.InvariantCulture),
				"-preset", "medium", "-pix_fmt", "yuv420p",
			});
			if (keepA) args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k" });
			if (fmt == "mp4") args.AddRange(new[] { "-movflags", "+faststart" });
		}
		if (!keepA) args.Add("-an");
		args.Add(dst);
		return args;
	}

	public static bool Probe(string exe, string path, out int w, out int h, out bool audio) {
		w = 0;
		h = 0;
		audio = false;
		if (string.IsNullOrWhiteSpace(exe) || !File.Exists(path)) return false;
		var err = "";
		var code = run(exe, new List<string> { "-hide_banner", "-i", path }, CancellationToken.None, 20000, out err);
		if (string.IsNullOrEmpty(err) && code == 0) return false;
		var vm = Regex.Match(err ?? "", @"Video:\s.*?(\d{2,5})x(\d{2,5})");
		if (!vm.Success) return false;
		w = int.Parse(vm.Groups[1].Value, CultureInfo.InvariantCulture);
		h = int.Parse(vm.Groups[2].Value, CultureInfo.InvariantCulture);
		audio = Regex.IsMatch(err, @"Audio:");
		return w >= 2 && h >= 2;
	}

	public static void ConvertOne(string src, string dst, string fmt, int crf,
		bool shortEn, int shortPx, bool audio, string mode, CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
			throw new FileNotFoundException(src);
		ct.ThrowIfCancellationRequested();
		var exe = FindExe();
		if (string.IsNullOrEmpty(exe))
			throw new InvalidOperationException(Loc.T("vidconv.noffmpeg"));
		fmt = NormFmt(fmt);
		crf = NormCrf(fmt, crf);
		mode = NormOutMode(mode);
		var probed = Probe(exe, src, out var w, out var h, out var hasAudio);
		if (!probed) {
			w = 0;
			h = 0;
			hasAudio = audio;
		}
		var dir = Path.GetDirectoryName(dst);
		if (string.IsNullOrEmpty(dir))
			dir = Path.GetDirectoryName(src);
		Directory.CreateDirectory(dir);
		var tmp = maketmp(dst);
		var args = Args(src, tmp, fmt, crf, shortEn, shortPx, audio, hasAudio, w, h);
		var code = run(exe, args, ct, 0, out var err);
		ct.ThrowIfCancellationRequested();
		if (code != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length == 0) {
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
			var line = lastline(err);
			if (string.IsNullOrWhiteSpace(line)) line = "ffmpeg " + code;
			throw new InvalidOperationException(line);
		}
		place(src, dst, tmp, mode);
	}

	static void place(string src, string dst, string tmp, string mode) {
		var same = string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase);
		if (same) {
			var bak = src + ".skvidbak";
			try { if (File.Exists(bak)) File.Delete(bak); } catch { }
			File.Move(src, bak);
			try {
				File.Move(tmp, dst);
				if (mode == OUTRECYCLE) ImgConvert.RecycleFile(bak);
				else File.Delete(bak);
			}
			catch {
				if (!File.Exists(dst) && File.Exists(bak))
					try { File.Move(bak, dst); } catch { }
				try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
				throw;
			}
			return;
		}
		if (File.Exists(dst)) File.Delete(dst);
		File.Move(tmp, dst);
		if (!IsReplace(mode)) return;
		if (mode == OUTRECYCLE) ImgConvert.RecycleFile(src);
		else File.Delete(src);
	}

	static string unique(string path, ISet<string> reserved) {
		if (!taken(path, reserved)) return path;
		var dir = Path.GetDirectoryName(path) ?? "";
		var name = Path.GetFileNameWithoutExtension(path);
		var ext = Path.GetExtension(path);
		for (var i = 2; i < 10000; i++) {
			var p = Path.Combine(dir, name + "_" + i + ext);
			if (!taken(p, reserved)) return p;
		}
		return path;
	}

	static bool taken(string path, ISet<string> reserved) {
		if (reserved != null && reserved.Contains(path)) return true;
		return File.Exists(path);
	}

	static string maketmp(string dst) {
		var dir = Path.GetDirectoryName(dst) ?? "";
		var name = Path.GetFileNameWithoutExtension(dst);
		var ext = Path.GetExtension(dst);
		for (var i = 0; i < 10000; i++) {
			var suffix = i == 0 ? "" : i.ToString(CultureInfo.InvariantCulture);
			var p = Path.Combine(dir, name + ".__skvid__" + suffix + ext);
			if (!File.Exists(p)) return p;
		}
		return dst + ".skvidtmp";
	}

	static int even(int n) {
		if (n < 2) return 2;
		return n - (n & 1);
	}

	static string lastline(string err) {
		if (string.IsNullOrWhiteSpace(err)) return "";
		var lines = err.Replace("\r", "").Split('\n');
		for (var i = lines.Length - 1; i >= 0; i--) {
			var s = lines[i].Trim();
			if (s.Length == 0) continue;
			if (s.Length > 400) s = s.Substring(0, 400);
			return s;
		}
		return "";
	}

	static int run(string exe, List<string> args, CancellationToken ct, int timeoutMs, out string err) {
		var psi = new ProcessStartInfo {
			FileName = exe,
			Arguments = join(args),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
		};
		using var p = Process.Start(psi);
		if (p == null) {
			err = "ffmpeg";
			return -1;
		}
		var errTask = p.StandardError.ReadToEndAsync();
		var outTask = p.StandardOutput.ReadToEndAsync();
		var start = Environment.TickCount;
		while (!p.WaitForExit(200)) {
			if (ct.IsCancellationRequested) {
				try { p.Kill(); } catch { }
				ct.ThrowIfCancellationRequested();
			}
			if (timeoutMs > 0 && unchecked(Environment.TickCount - start) > timeoutMs) {
				try { p.Kill(); } catch { }
				break;
			}
		}
		try { p.WaitForExit(2000); } catch { }
		err = "";
		try { err = errTask.GetAwaiter().GetResult(); } catch { }
		try {
			var o = outTask.GetAwaiter().GetResult();
			if (!string.IsNullOrWhiteSpace(o)) err = err + "\n" + o;
		}
		catch { }
		return p.HasExited ? p.ExitCode : -1;
	}

	static string join(List<string> args) {
		var sb = new StringBuilder();
		for (var i = 0; i < args.Count; i++) {
			if (i > 0) sb.Append(' ');
			sb.Append(quote(args[i] ?? ""));
		}
		return sb.ToString();
	}

	static string quote(string s) {
		if (s.Length == 0) return "\"\"";
		if (s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return s;
		return "\"" + s.Replace("\"", "\\\"") + "\"";
	}
}
