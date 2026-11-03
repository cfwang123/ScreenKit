using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;

namespace ScreenKit;

/// <summary>一种语言的行注释、块注释和扩展名。</summary>
sealed class CodeLang {
	public string Name;
	public string[] Line;
	public string[][] Block;
	public string[] Ext;
	/// <summary>Markdown 这类文件，围栏里的代码另算一种语言。</summary>
	public bool Embed;
}

/// <summary>一个文件的行数。</summary>
sealed class CodeFileRow {
	public string Path;
	public string Name;
	public string Lang;
	public int Lines;
	public int Code;
	public int Comments;
	public int Blanks;
	public string Note;
}

/// <summary>一种语言的合计，含子语言。</summary>
sealed class CodeLangRow {
	public string Name;
	public bool Child;
	public int Files;
	public int Lines;
	public int Code;
	public int Comments;
	public int Blanks;
}

/// <summary>一次统计的结果。</summary>
sealed class CodeStatResult {
	public List<CodeLangRow> Langs = new();
	public List<CodeFileRow> Files = new();
	public int Skipped;
	public string Error;
}

/// <summary>按语言统计代码行。做法接近 tokei：空行、注释、代码分开算，Markdown 围栏里的代码另列。</summary>
static class CodeStat {
	const int MAXBYTES = 8 * 1024 * 1024;
	const int MAXFILES = 200000;
	const int WORKERS = 8;

	static readonly string[] SKIPDIR = {
		".git", ".svn", ".hg", ".vs", ".idea", "node_modules", "bin", "obj",
		"packages", "dist", "build", "target", "__pycache__", ".venv", "venv",
	};

	static readonly HashSet<string> BINEXT = new(StringComparer.OrdinalIgnoreCase) {
		".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff",
		".mp3", ".mp4", ".wav", ".flac", ".ogg", ".avi", ".mkv", ".mov", ".wmv",
		".zip", ".7z", ".rar", ".gz", ".tar", ".bz2", ".xz", ".dll", ".exe", ".pdb",
		".so", ".dylib", ".lib", ".obj", ".o", ".a", ".woff", ".woff2", ".ttf", ".otf", ".eot",
		".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".db", ".sqlite",
		".bin", ".dat", ".pak", ".nupkg", ".snupkg",
	};

	static readonly CodeLang[] LANGS = {
		new() { Name = "C", Ext = new[] { ".c", ".h" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "C++", Ext = new[] { ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx", ".inl" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "C#", Ext = new[] { ".cs" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "Java", Ext = new[] { ".java" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "Kotlin", Ext = new[] { ".kt", ".kts" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "Go", Ext = new[] { ".go" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "Rust", Ext = new[] { ".rs" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "JavaScript", Ext = new[] { ".js", ".mjs", ".cjs", ".jsx" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "TypeScript", Ext = new[] { ".ts", ".tsx", ".mts", ".cts" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "Python", Ext = new[] { ".py" }, Line = new[] { "#" }, Block = new[] { pair("\"\"\"", "\"\"\""), pair("'''", "'''") } },
		new() { Name = "Ruby", Ext = new[] { ".rb" }, Line = new[] { "#" }, Block = new[] { pair("=begin", "=end") } },
		new() { Name = "PHP", Ext = new[] { ".php" }, Line = new[] { "//", "#" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "Perl", Ext = new[] { ".pl", ".pm" }, Line = new[] { "#" }, Block = new[] { pair("=pod", "=cut") } },
		new() { Name = "Lua", Ext = new[] { ".lua" }, Line = new[] { "--" }, Block = new[] { pair("--[[", "]]") } },
		new() { Name = "Bash", Ext = new[] { ".sh", ".bash" }, Line = new[] { "#" } },
		new() { Name = "Batch", Ext = new[] { ".bat", ".cmd" }, Line = new[] { "rem ", "REM ", "::" } },
		new() { Name = "PowerShell", Ext = new[] { ".ps1", ".psm1", ".psd1" }, Line = new[] { "#" }, Block = new[] { pair("<#", "#>") } },
		new() { Name = "SQL", Ext = new[] { ".sql" }, Line = new[] { "--" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "HTML", Ext = new[] { ".html", ".htm" }, Block = new[] { pair("<!--", "-->") } },
		new() { Name = "XML", Ext = new[] { ".xml", ".csproj", ".vbproj", ".fsproj", ".props", ".targets", ".config", ".resx", ".xaml" }, Block = new[] { pair("<!--", "-->") } },
		new() { Name = "CSS", Ext = new[] { ".css" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "JSON", Ext = new[] { ".json" } },
		new() { Name = "YAML", Ext = new[] { ".yml", ".yaml" }, Line = new[] { "#" } },
		new() { Name = "TOML", Ext = new[] { ".toml" }, Line = new[] { "#" } },
		new() { Name = "Markdown", Ext = new[] { ".md", ".markdown" }, Embed = true },
		new() { Name = "MSBuild", Ext = new[] { ".proj" }, Block = new[] { pair("<!--", "-->") } },
		new() { Name = "VB", Ext = new[] { ".vb" }, Line = new[] { "'" } },
		new() { Name = "F#", Ext = new[] { ".fs", ".fsx" }, Line = new[] { "//" }, Block = new[] { pair("(*", "*)") } },
		new() { Name = "Swift", Ext = new[] { ".swift" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "R", Ext = new[] { ".r" }, Line = new[] { "#" } },
		new() { Name = "MATLAB", Ext = new[] { ".m" }, Line = new[] { "%" }, Block = new[] { pair("%{", "%}") } },
		new() { Name = "Assembly", Ext = new[] { ".asm", ".s" }, Line = new[] { ";", "#" } },
		new() { Name = "Haskell", Ext = new[] { ".hs" }, Line = new[] { "--" }, Block = new[] { pair("{-", "-}") } },
		new() { Name = "Scala", Ext = new[] { ".scala" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "Dart", Ext = new[] { ".dart" }, Line = new[] { "//" }, Block = new[] { pair("/*", "*/") } },
		new() { Name = "Vue", Ext = new[] { ".vue" }, Block = new[] { pair("<!--", "-->") } },
		new() { Name = "INI", Ext = new[] { ".ini", ".cfg" }, Line = new[] { ";", "#" } },
		new() { Name = "Makefile", Ext = new[] { ".mk" }, Line = new[] { "#" } },
	};

	static readonly Dictionary<string, CodeLang> BYEXT = buildmap();

	static Dictionary<string, CodeLang> buildmap() {
		var map = new Dictionary<string, CodeLang>(StringComparer.OrdinalIgnoreCase);
		foreach (var lang in LANGS)
			foreach (var ext in lang.Ext)
				if (!map.ContainsKey(ext)) map[ext] = lang;
		return map;
	}

	static string[] pair(string a, string b) => new[] { a, b };

	/// <summary>统计这些目录和文件。rules 是额外的忽略规则，一行一条，写法同 .gitignore。
	/// progress 回传阶段（scan / count）、已处理数和当前路径，由调用方节流。</summary>
	public static CodeStatResult Run(IEnumerable<string> paths, bool useGitignore, IEnumerable<string> rules,
		CancellationToken token, Action<string, int, string> progress = null) {
		var result = new CodeStatResult();
		var files = new ConcurrentBag<string>();
		var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
		var skipped = 0;
		var scanned = 0;
		var counted = 0;
		try {
			var roots = new List<string>();
			foreach (var raw in paths) {
				token.ThrowIfCancellationRequested();
				var path = (raw ?? "").Trim().Trim('"');
				if (path.Length == 0) continue;
				if (Directory.Exists(path) || File.Exists(path)) roots.Add(path);
				else Interlocked.Increment(ref skipped);
			}
			report(progress, "scan", 0, "");
			Parallel.ForEach(roots, new ParallelOptions { MaxDegreeOfParallelism = WORKERS, CancellationToken = token }, path => {
				if (Directory.Exists(path))
					walk(path, path, useGitignore, rules, files, seen, ref skipped, ref scanned, progress, token);
				else
					addfile(path, Path.GetDirectoryName(path), useGitignore, rules, files, seen, ref skipped, ref scanned, progress);
			});
			var rows = new ConcurrentBag<CodeFileRow>();
			var total = files.Count;
			report(progress, "count", 0, total.ToString());
			Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = WORKERS, CancellationToken = token }, file => {
				var row = countfile(file, token);
				var n = Interlocked.Increment(ref counted);
				if (row == null) Interlocked.Increment(ref skipped);
				else rows.Add(row);
				if (n == total || n % 20 == 0) report(progress, "count", n, file);
			});
			result.Skipped = skipped;
			var bag = new Dictionary<string, CodeLangRow>(StringComparer.Ordinal);
			var kids = new Dictionary<string, CodeLangRow>(StringComparer.Ordinal);
			foreach (var row in rows) {
				result.Files.Add(row);
				addlang(bag, row.Lang, row, false);
				if (row.Note == null) continue;
				foreach (var sub in parseembed(row))
					addlang(kids, row.Lang + "\t" + sub.Lang, sub, true);
			}
			foreach (var row in bag.Values.OrderByDescending(r => r.Lines).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) {
				result.Langs.Add(row);
				foreach (var kid in kids.Values.Where(k => k.Name.StartsWith(row.Name + "\t", StringComparison.Ordinal))
					.OrderByDescending(k => k.Lines)) {
					kid.Name = kid.Name.Substring(row.Name.Length + 1);
					result.Langs.Add(kid);
				}
			}
			result.Files.Sort((a, b) => b.Lines.CompareTo(a.Lines));
		}
		catch (OperationCanceledException) {
			result.Error = "canceled";
		}
		catch (Exception ex) {
			result.Error = ex.Message;
		}
		return result;
	}

	static void report(Action<string, int, string> progress, string phase, int n, string path) {
		try { progress?.Invoke(phase, n, path ?? ""); } catch { }
	}

	static void addlang(Dictionary<string, CodeLangRow> bag, string name, CodeFileRow row, bool child) {
		if (!bag.TryGetValue(name, out var sum)) {
			sum = new CodeLangRow { Name = name, Child = child };
			bag[name] = sum;
		}
		sum.Files++;
		sum.Lines += row.Lines;
		sum.Code += row.Code;
		sum.Comments += row.Comments;
		sum.Blanks += row.Blanks;
	}

	static List<CodeFileRow> parseembed(CodeFileRow row) {
		var list = new List<CodeFileRow>();
		if (string.IsNullOrEmpty(row.Note)) return list;
		foreach (var part in row.Note.Split('\n')) {
			var bits = part.Split('\t');
			if (bits.Length != 4) continue;
			list.Add(new CodeFileRow {
				Lang = bits[0],
				Lines = int.Parse(bits[1]),
				Code = int.Parse(bits[2]),
				Comments = int.Parse(bits[3]),
			});
		}
		return list;
	}

	static void walk(string root, string dir, bool useGitignore, IEnumerable<string> rules,
		ConcurrentBag<string> files, ConcurrentDictionary<string, byte> seen, ref int skipped, ref int scanned,
		Action<string, int, string> progress, CancellationToken token) {
		var subs = new List<string>();
		var here = new List<string>();
		try {
			foreach (var path in Directory.EnumerateFileSystemEntries(dir)) {
				token.ThrowIfCancellationRequested();
				if (seen.Count >= MAXFILES) return;
				if (Directory.Exists(path)) subs.Add(path);
				else here.Add(path);
			}
		}
		catch { Interlocked.Increment(ref skipped); return; }
		var keep = new List<string>();
		foreach (var path in subs) {
			var name = Path.GetFileName(path);
			if (skipdir(name) || ignored(root, path, true, useGitignore, rules)) {
				Interlocked.Increment(ref skipped);
				continue;
			}
			keep.Add(path);
		}
		foreach (var path in here)
			addfile(path, root, useGitignore, rules, files, seen, ref skipped, ref scanned, progress);
		if (keep.Count <= 1) {
			foreach (var path in keep)
				walk(root, path, useGitignore, rules, files, seen, ref skipped, ref scanned, progress, token);
			return;
		}
		var bad = 0;
		Parallel.ForEach(keep, new ParallelOptions { MaxDegreeOfParallelism = WORKERS, CancellationToken = token }, path => {
			var n = 0;
			var m = 0;
			walk(root, path, useGitignore, rules, files, seen, ref n, ref m, progress, token);
			if (n != 0) Interlocked.Add(ref bad, n);
		});
		if (bad != 0) Interlocked.Add(ref skipped, bad);
	}

	static void addfile(string path, string root, bool useGitignore, IEnumerable<string> rules,
		ConcurrentBag<string> files, ConcurrentDictionary<string, byte> seen, ref int skipped, ref int scanned,
		Action<string, int, string> progress) {
		if (seen.Count >= MAXFILES) return;
		string full;
		try { full = Path.GetFullPath(path); }
		catch { Interlocked.Increment(ref skipped); return; }
		if (!seen.TryAdd(full, 0)) return;
		var ext = Path.GetExtension(path);
		if (BINEXT.Contains(ext) || !BYEXT.ContainsKey(ext)) { Interlocked.Increment(ref skipped); return; }
		try {
			if (new FileInfo(path).Length > MAXBYTES) { Interlocked.Increment(ref skipped); return; }
		}
		catch { Interlocked.Increment(ref skipped); return; }
		if (ignored(root, path, false, useGitignore, rules)) { Interlocked.Increment(ref skipped); return; }
		files.Add(path);
		var n = Interlocked.Increment(ref scanned);
		if (n % 50 == 0) report(progress, "scan", n, path);
	}

	static bool skipdir(string name) {
		foreach (var s in SKIPDIR)
			if (string.Equals(name, s, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	static readonly ConcurrentDictionary<string, string[]> GITRULES = new(StringComparer.OrdinalIgnoreCase);

	static bool ignored(string root, string path, bool dir, bool useGitignore, IEnumerable<string> rules) {
		var rel = relpath(root, path);
		if (useGitignore && gitignored(root, path, dir)) return true;
		return rules != null && matchrules(rel, dir, rules);
	}

	static bool gitignored(string root, string path, bool dir) {
		var dirpath = dir ? path : Path.GetDirectoryName(path);
		while (!string.IsNullOrEmpty(dirpath)) {
			var lines = gitrules(dirpath);
			if (lines.Length > 0 && matchrules(relpath(dirpath, path), dir, lines)) return true;
			if (string.Equals(Path.GetFullPath(dirpath), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
				break;
			var parent = Path.GetDirectoryName(dirpath);
			if (string.Equals(parent, dirpath, StringComparison.OrdinalIgnoreCase)) break;
			dirpath = parent;
		}
		return false;
	}

	static string[] gitrules(string dir) {
		return GITRULES.GetOrAdd(dir, d => {
			var file = Path.Combine(d, ".gitignore");
			if (!File.Exists(file)) return Array.Empty<string>();
			try { return File.ReadAllLines(file); }
			catch { return Array.Empty<string>(); }
		});
	}

	/// <summary>gitignore 的常用写法：*、?、**、目录尾斜杠、! 取反、# 注释。</summary>
	public static bool matchrules(string rel, bool dir, IEnumerable<string> rules) {
		var path = (rel ?? "").Replace('\\', '/').Trim('/');
		var hit = false;
		foreach (var raw in rules) {
			var line = (raw ?? "").Trim();
			if (line.Length == 0 || line[0] == '#') continue;
			var neg = line[0] == '!';
			if (neg) line = line.Substring(1).Trim();
			var onlydir = line.EndsWith("/");
			if (onlydir) line = line.TrimEnd('/');
			if (line.Length == 0) continue;
			var anchored = line.IndexOf('/') >= 0;
			line = line.Trim('/');
			if (!anchored) line = "**/" + line;
			if (onlydir) line += "/**";
			if (glob(line, path)) hit = !neg;
		}
		return hit;
	}

	static bool glob(string pat, string text) {
		return globat(pat, 0, text, 0);
	}

	static bool globat(string pat, int pi, string text, int ti) {
		while (pi < pat.Length) {
			if (pi + 1 < pat.Length && pat[pi] == '*' && pat[pi + 1] == '*') {
				pi += 2;
				if (pi < pat.Length && pat[pi] == '/') pi++;
				if (pi >= pat.Length) return true;
				for (var i = ti; i <= text.Length; i++)
					if ((i == ti || text[i - 1] == '/') && globat(pat, pi, text, i)) return true;
				return false;
			}
			if (pat[pi] == '*') {
				pi++;
				for (var i = ti; i <= text.Length && (i == ti || text[i - 1] != '/'); i++)
					if (globat(pat, pi, text, i)) return true;
				return false;
			}
			if (ti >= text.Length) return false;
			if (pat[pi] == '?') {
				if (text[ti] == '/') return false;
				pi++; ti++;
				continue;
			}
			if (char.ToLowerInvariant(pat[pi]) != char.ToLowerInvariant(text[ti])) return false;
			pi++; ti++;
		}
		return ti == text.Length;
	}

	static string relpath(string root, string path) {
		try {
			var r = Path.GetFullPath(root).TrimEnd('\\', '/');
			var p = Path.GetFullPath(path);
			if (p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)
				|| p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase))
				return p.Substring(r.Length + 1);
		}
		catch { }
		return Path.GetFileName(path);
	}

	static CodeFileRow countfile(string path, CancellationToken token) {
		var ext = Path.GetExtension(path);
		if (!BYEXT.TryGetValue(ext, out var lang)) return null;
		string text;
		try { text = readtext(path); }
		catch { return null; }
		var row = new CodeFileRow {
			Path = path,
			Name = Path.GetFileName(path),
			Lang = lang.Name,
		};
		var lines = splitlines(text);
		string open = null, close = null;
		var fence = false;
		string fenceLang = null;
		var embed = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
		var embedCode = 0;
		var embedBlank = 0;
		foreach (var line in lines) {
			token.ThrowIfCancellationRequested();
			row.Lines++;
			var trim = line.Trim();
			if (lang.Embed && isfence(trim)) {
				fence = !fence;
				fenceLang = fence ? fencelang(trim) : null;
				row.Comments++;
				continue;
			}
			if (lang.Embed && fence) {
				var sub = fencelangname(fenceLang);
				if (!embed.TryGetValue(sub, out var n)) embed[sub] = n = new int[4];
				n[0]++;
				if (trim.Length == 0) { n[1]++; embedBlank++; }
				else { n[2]++; embedCode++; }
				row.Comments++;
				continue;
			}
			if (trim.Length == 0 && open == null) { row.Blanks++; continue; }
			var rest = trim;
			var comment = open != null;
			var code = false;
			while (rest.Length > 0) {
				if (open != null) {
					var at = rest.IndexOf(close, StringComparison.Ordinal);
					if (at < 0) { rest = ""; break; }
					rest = rest.Substring(at + close.Length).TrimStart();
					open = null;
					continue;
				}
				var cut = nextcut(lang, rest, out var kind, out var mark);
				if (cut < 0) { if (rest.Length > 0) code = true; break; }
				if (cut > 0) code = true;
				if (kind == 1) { rest = ""; break; }
				open = mark;
				close = blockclose(lang, mark);
				rest = rest.Substring(cut + mark.Length).TrimStart();
				comment = true;
			}
			if (code) row.Code++;
			else if (comment || open != null) row.Comments++;
			else row.Blanks++;
		}
		if (embed.Count > 0) {
			var sb = new StringBuilder();
			foreach (var kv in embed) {
				if (sb.Length > 0) sb.Append('\n');
				sb.Append(kv.Key).Append('\t').Append(kv.Value[0]).Append('\t')
					.Append(kv.Value[2]).Append('\t').Append(kv.Value[3]);
			}
			row.Note = sb.ToString();
			row.Code += embedCode;
			row.Comments -= embedCode + embedBlank;
			row.Blanks += embedBlank;
			if (row.Comments < 0) row.Comments = 0;
		}
		return row;
	}

	static int nextcut(CodeLang lang, string rest, out int kind, out string mark) {
		kind = 0;
		mark = null;
		var best = -1;
		if (lang.Line != null) {
			foreach (var s in lang.Line) {
				var at = rest.IndexOf(s, StringComparison.Ordinal);
				if (at >= 0 && (best < 0 || at < best)) { best = at; kind = 1; mark = s; }
			}
		}
		if (lang.Block != null) {
			foreach (var b in lang.Block) {
				var at = rest.IndexOf(b[0], StringComparison.Ordinal);
				if (at >= 0 && (best < 0 || at < best)) { best = at; kind = 2; mark = b[0]; }
			}
		}
		return best;
	}

	static string blockclose(CodeLang lang, string open) {
		foreach (var b in lang.Block)
			if (b[0] == open) return b[1];
		return open;
	}

	static bool isfence(string trim) =>
		trim.StartsWith("```", StringComparison.Ordinal) || trim.StartsWith("~~~", StringComparison.Ordinal);

	static string fencelang(string trim) {
		var mark = trim.StartsWith("```", StringComparison.Ordinal) ? "```" : "~~~";
		return trim.Substring(mark.Length).Trim().Split(' ')[0];
	}

	static string fencelangname(string tag) {
		var t = (tag ?? "").Trim().ToLowerInvariant();
		return t switch {
			"cs" or "csharp" => "C#",
			"js" or "javascript" => "JavaScript",
			"ts" or "typescript" => "TypeScript",
			"py" or "python" => "Python",
			"ps1" or "powershell" or "pwsh" => "PowerShell",
			"sh" or "bash" or "shell" => "BASH",
			"json" => "JSON",
			"yml" or "yaml" => "YAML",
			"xml" => "XML",
			"html" => "HTML",
			"css" => "CSS",
			"sql" => "SQL",
			"lua" => "Lua",
			"cpp" or "c++" => "C++",
			"c" => "C",
			"java" => "Java",
			"go" => "Go",
			"rs" or "rust" => "Rust",
			"rb" or "ruby" => "Ruby",
			"" => "Text",
			_ => tag,
		};
	}

	static List<string> splitlines(string text) {
		var list = new List<string>();
		var i = 0;
		while (i < text.Length) {
			var j = text.IndexOf('\n', i);
			if (j < 0) j = text.Length;
			var end = j > i && text[j - 1] == '\r' ? j - 1 : j;
			list.Add(text.Substring(i, end - i));
			i = j + 1;
		}
		if (text.EndsWith("\n", StringComparison.Ordinal) && list.Count > 0 && list[list.Count - 1].Length == 0)
			list.RemoveAt(list.Count - 1);
		return list;
	}

	static string readtext(string path) {
		var bytes = File.ReadAllBytes(path);
		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
		if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
		try {
			return new UTF8Encoding(false, true).GetString(bytes);
		}
		catch {
			return Encoding.GetEncoding(936).GetString(bytes);
		}
	}
}
