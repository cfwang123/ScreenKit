using System.Text.RegularExpressions;

namespace ScreenKit;

enum RenameKind {
	Ready,
	Unchanged,
	Invalid,
	Conflict,
}

sealed class RenamePlan {
	public string Source;
	public string From;
	public string To;
	public string Dest;
	public RenameKind Kind;
}

sealed class RenameOptions {
	public string OldPattern = "%1";
	public string NewPattern = "%1";
	public bool MatchCase;
	public bool UseRegex;
	public bool IgnoreExtension;
}

/// <summary>Everything / FastCopy 风格批量改名：%1 捕获，# / ### 编号。</summary>
static class BatchRename {
	public static (string oldPat, string newPat) CommonPattern(IList<string> names, bool ignoreExt) {
		if (names == null || names.Count == 0) return ("%1", "%1");
		var working = new string[names.Count];
		for (var i = 0; i < names.Count; i++)
			working[i] = ignoreExt ? SplitExt(names[i]).stem : names[i];
		var pat = infer(working);
		return (pat, pat);
	}

	static string infer(string[] names) {
		if (names.Length < 2) return "%1";
		if (names.All(n => n == names[0])) return "%1";
		var prefix = commonprefix(names);
		var prefixLen = prefix.Length;
		var suffix = commonsuffix(names, prefixLen);
		if (prefix.Length == 0 && suffix.Length == 0) return "%1";
		return prefix + "%1" + suffix;
	}

	static string commonprefix(string[] names) {
		var prefix = names[0];
		foreach (var name in names) {
			var n = 0;
			var max = Math.Min(prefix.Length, name.Length);
			while (n < max && prefix[n] == name[n]) n++;
			prefix = prefix.Substring(0, n);
			if (prefix.Length == 0) break;
		}
		return prefix;
	}

	static string commonsuffix(string[] names, int prefixLen) {
		var maxLen = names.Min(n => Math.Max(0, n.Length - prefixLen));
		var suffixLen = 0;
		while (suffixLen < maxLen) {
			var expected = names[0][names[0].Length - 1 - suffixLen];
			if (names.All(n => n[n.Length - 1 - suffixLen] == expected))
				suffixLen++;
			else
				break;
		}
		return suffixLen == 0 ? "" : names[0].Substring(names[0].Length - suffixLen);
	}

	public static string ExpandNewName(string template, string[] slots, int index) {
		if (template == null) return "";
		var sb = new System.Text.StringBuilder(template.Length + 8);
		var i = 0;
		while (i < template.Length) {
			if (i + 5 <= template.Length && template.Substring(i, 5) == "{nnn}") {
				sb.Append(index.ToString("000"));
				i += 5;
				continue;
			}
			if (i + 4 <= template.Length && template.Substring(i, 4) == "{nn}") {
				sb.Append(index.ToString("00"));
				i += 4;
				continue;
			}
			if (i + 3 <= template.Length && template.Substring(i, 3) == "{n}") {
				sb.Append(index.ToString());
				i += 3;
				continue;
			}
			if (template[i] == '#') {
				var w = 0;
				while (i + w < template.Length && template[i + w] == '#') w++;
				sb.Append(index.ToString(new string('0', Math.Max(1, w))));
				i += w;
				continue;
			}
			if (template[i] == '%' && i + 1 < template.Length && char.IsDigit(template[i + 1])) {
				var j = i + 1;
				while (j < template.Length && char.IsDigit(template[j])) j++;
				if (int.TryParse(template.Substring(i + 1, j - i - 1), out var slot)
					&& slots != null && slot >= 0 && slot < slots.Length)
					sb.Append(slots[slot] ?? "");
				i = j;
				continue;
			}
			sb.Append(template[i]);
			i++;
		}
		return sb.ToString();
	}

	public static List<RenamePlan> Plan(IList<string> paths, IList<string> fromNames, RenameOptions opt) {
		var list = new List<RenamePlan>();
		if (paths == null || paths.Count == 0) return list;
		opt ??= new RenameOptions();
		Regex rx = null;
		var badPat = false;
		try { rx = compile(opt); }
		catch { badPat = true; }
		var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < paths.Count; i++) {
			var src = paths[i];
			var from = i < fromNames.Count && !string.IsNullOrEmpty(fromNames[i])
				? fromNames[i]
				: Path.GetFileName(src);
			var to = badPat || rx == null ? from : renameone(from, rx, opt, i + 1);
			list.Add(makeplan(src, from, to, taken, badPat));
		}
		return list;
	}

	public static List<RenamePlan> PlanTo(IList<string> paths, IList<string> fromNames, IList<string> toNames) {
		var list = new List<RenamePlan>();
		var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < paths.Count; i++) {
			var src = paths[i];
			var from = i < fromNames.Count ? fromNames[i] : Path.GetFileName(src);
			var to = i < toNames.Count && toNames[i] != null ? toNames[i] : from;
			list.Add(makeplan(src, from, to, taken, false));
		}
		return list;
	}

	static RenamePlan makeplan(string source, string from, string to, HashSet<string> taken, bool invalidPat) {
		from ??= "";
		to ??= "";
		var parent = Path.GetDirectoryName(source) ?? "";
		var dest = Path.Combine(parent, to);
		var kind = RenameKind.Ready;
		if (invalidPat || !IsValidFileName(to))
			kind = RenameKind.Invalid;
		else if (string.Equals(to, from, StringComparison.Ordinal))
			kind = RenameKind.Unchanged;
		else if (File.Exists(dest) || Directory.Exists(dest)) {
			if (!sameitem(source, dest))
				kind = RenameKind.Conflict;
		}
		if (kind == RenameKind.Ready && !taken.Add(to))
			kind = RenameKind.Conflict;
		if (kind != RenameKind.Ready && kind != RenameKind.Unchanged)
			taken.Remove(to);
		else if (kind == RenameKind.Unchanged)
			taken.Add(to);
		return new RenamePlan {
			Source = source,
			From = from,
			To = to,
			Dest = dest,
			Kind = kind,
		};
	}

	static Regex compile(RenameOptions opt) {
		string pattern;
		if (opt.UseRegex) {
			var raw = (opt.OldPattern ?? "").Trim();
			pattern = raw.Length == 0 || raw == "%1" ? "^(.*)$" : opt.OldPattern;
		}
		else {
			var glob = (opt.OldPattern ?? "").Trim();
			if (glob.Length == 0 || glob == "%1") glob = "*";
			else glob = applywild(glob);
			pattern = globtoregex(glob);
		}
		var flags = RegexOptions.CultureInvariant | RegexOptions.Singleline;
		if (!opt.MatchCase) flags |= RegexOptions.IgnoreCase;
		return new Regex(pattern, flags);
	}

	static string applywild(string pattern) {
		var sb = new System.Text.StringBuilder(pattern.Length);
		for (var i = 0; i < pattern.Length; i++) {
			if (pattern[i] == '%' && i + 1 < pattern.Length && pattern[i + 1] >= '1' && pattern[i + 1] <= '9') {
				var wider = i + 2 < pattern.Length && char.IsDigit(pattern[i + 2]);
				if (!wider) {
					sb.Append('*');
					i++;
					continue;
				}
			}
			sb.Append(pattern[i]);
		}
		return sb.ToString();
	}

	static string globtoregex(string glob) {
		var sb = new System.Text.StringBuilder("^");
		foreach (var ch in glob) {
			if (ch == '*') sb.Append("(.*)");
			else if (ch == '?') sb.Append("(.)");
			else sb.Append(Regex.Escape(ch.ToString()));
		}
		sb.Append('$');
		return sb.ToString();
	}

	static (string stem, string ext) SplitExt(string name) {
		if (string.IsNullOrEmpty(name)) return ("", "");
		var dot = name.LastIndexOf('.');
		if (dot <= 0) return (name, "");
		return (name.Substring(0, dot), name.Substring(dot));
	}

	static string renameone(string from, Regex rx, RenameOptions opt, int index) {
		string working, ext;
		if (opt.IgnoreExtension) {
			var p = SplitExt(from);
			working = p.stem;
			ext = p.ext;
		}
		else {
			working = from;
			ext = "";
		}
		var m = rx.Match(working);
		if (!m.Success) return from;
		var slots = new string[Math.Max(2, m.Groups.Count)];
		slots[0] = "";
		slots[1] = working;
		for (var g = 1; g < m.Groups.Count; g++) {
			var v = m.Groups[g].Success ? m.Groups[g].Value : "";
			if (g < slots.Length) slots[g] = v;
		}
		var nw = ExpandNewName(opt.NewPattern ?? "", slots, index);
		if (nw.Length == 0) return from;
		return nw + ext;
	}

	public static bool IsValidFileName(string name) {
		if (string.IsNullOrEmpty(name) || name == "." || name == "..") return false;
		if (name.EndsWith(" ") || name.EndsWith(".")) return false;
		foreach (var ch in name) {
			if (ch is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or '\0')
				return false;
		}
		return true;
	}

	static bool sameitem(string a, string b) {
		try {
			return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
		}
		catch {
			return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}
	}

	public static void Apply(RenamePlan plan) {
		if (plan == null || plan.Kind != RenameKind.Ready)
			throw new InvalidOperationException("not ready");
		if (sameitem(plan.Source, plan.Dest) && plan.From != plan.To) {
			var parent = Path.GetDirectoryName(plan.Source) ?? "";
			var tmp = Path.Combine(parent, ".sk-rename-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + plan.From);
			File.Move(plan.Source, tmp);
			try { File.Move(tmp, plan.Dest); }
			catch {
				try { File.Move(tmp, plan.Source); } catch { }
				throw;
			}
			return;
		}
		if (Directory.Exists(plan.Source) && !File.Exists(plan.Source))
			Directory.Move(plan.Source, plan.Dest);
		else
			File.Move(plan.Source, plan.Dest);
	}
}
