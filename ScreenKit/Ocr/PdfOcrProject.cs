using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKit;

/// <summary>单行 OCR 结果（可编辑文字，保留框用于导出文字层）。</summary>
sealed class PdfLineEdit {
	public string Text { get; set; } = "";
	public float Score { get; set; }
	/// <summary>八点：x0,y0 … x3,y3（像素，左上原点）。</summary>
	public float[] Box { get; set; }
	/// <summary>显示用行号（1-based，不序列化）。</summary>
	[JsonIgnore]
	public int LineNo { get; set; }

	public Point2f[] ToBox() {
		if (Box == null || Box.Length < 8) return null;
		return [
			new Point2f(Box[0], Box[1]),
			new Point2f(Box[2], Box[3]),
			new Point2f(Box[4], Box[5]),
			new Point2f(Box[6], Box[7]),
		];
	}

	public static PdfLineEdit FromOcr(OcrLine ln) {
		var e = new PdfLineEdit {
			Text = ln?.Text ?? "",
			Score = ln?.Score ?? 0,
		};
		if (ln?.Box != null && ln.Box.Length >= 4) {
			e.Box = new float[8];
			for (int i = 0; i < 4; i++) {
				e.Box[i * 2] = ln.Box[i].X;
				e.Box[i * 2 + 1] = ln.Box[i].Y;
			}
		}
		return e;
	}

	public OcrLine ToOcrLine() => new() {
		Text = Text ?? "",
		Score = Score,
		Box = ToBox(),
	};
}

/// <summary>PDF 工程中的一页。</summary>
sealed class PdfPageEdit {
	public int Index { get; set; }
	public string ImageFile { get; set; } = "";
	public int Width { get; set; }
	public int Height { get; set; }
	public bool Recognized { get; set; }
	public List<PdfLineEdit> Lines { get; set; } = new();

	[JsonIgnore]
	public string DisplayName =>
		Recognized
			? $"第 {Index + 1} 页 · {Lines?.Count ?? 0} 行"
			: $"第 {Index + 1} 页 · 未识别";

	[JsonIgnore]
	public string PageText {
		get => string.Join(Environment.NewLine, (Lines ?? []).Select(l => l.Text ?? ""));
		set => ApplyPageText(value);
	}

	/// <summary>用整页文本回写各行（行数变化时尽量保留原框）。</summary>
	public void ApplyPageText(string text) {
		text ??= "";
		var parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		Lines ??= new List<PdfLineEdit>();
		var n = Math.Max(parts.Length, Lines.Count);
		var next = new List<PdfLineEdit>(parts.Length);
		for (int i = 0; i < parts.Length; i++) {
			var prev = i < Lines.Count ? Lines[i] : null;
			var box = prev?.Box;
			if (box == null && Lines.Count > 0)
				box = Lines[Math.Min(i, Lines.Count - 1)].Box?.ToArray();
			next.Add(new PdfLineEdit {
				Text = parts[i] ?? "",
				Score = prev?.Score ?? 1f,
				Box = box,
			});
		}
		Lines = next;
		Recognized = true;
	}
}

/// <summary>PDF 识别工程：可编辑、可存草稿、可导出。</summary>
sealed class PdfOcrProject {
	public int Version { get; set; } = 1;
	public string Title { get; set; } = "";
	public string SourcePath { get; set; } = "";
	public int Dpi { get; set; } = 150;
	public bool InvisibleText { get; set; } = true;
	public string SavedAt { get; set; } = "";
	public List<PdfPageEdit> Pages { get; set; } = new();

	/// <summary>草稿目录（含 project.json 与 pages/）。运行时字段，不序列化进 JSON 根外时另存。</summary>
	[JsonIgnore]
	public string DraftDir { get; set; }

	[JsonIgnore]
	public bool Dirty { get; set; }

	static readonly JsonSerializerOptions JsonOpt = new() {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public string FullText() {
		var sb = new StringBuilder();
		for (int i = 0; i < Pages.Count; i++) {
			if (i > 0) sb.AppendLine().AppendLine();
			sb.Append(Pages[i].PageText);
		}
		return sb.ToString();
	}

	public string ImagePath(PdfPageEdit page) {
		if (page == null || string.IsNullOrEmpty(DraftDir)) return null;
		var rel = page.ImageFile;
		if (string.IsNullOrEmpty(rel))
			rel = Path.Combine("pages", $"{page.Index:D3}.png");
		return Path.Combine(DraftDir, rel);
	}

	/// <summary>新建工程目录（临时或用户指定）。</summary>
	public static string NewDraftDir(string hintName = null) {
		var root = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"ScreenKit", "drafts");
		Directory.CreateDirectory(root);
		var name = string.IsNullOrWhiteSpace(hintName) ? "pdf" : Sanitize(hintName);
		var dir = Path.Combine(root, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}");
		Directory.CreateDirectory(dir);
		Directory.CreateDirectory(Path.Combine(dir, "pages"));
		return dir;
	}

	static string Sanitize(string s) {
		foreach (var c in Path.GetInvalidFileNameChars())
			s = s.Replace(c, '_');
		if (s.Length > 40) s = s[..40];
		return string.IsNullOrWhiteSpace(s) ? "pdf" : s;
	}

	public void SaveDraft(string dir = null) {
		if (!string.IsNullOrWhiteSpace(dir))
			DraftDir = dir;
		if (string.IsNullOrWhiteSpace(DraftDir))
			throw new InvalidOperationException("草稿目录未设置");

		Directory.CreateDirectory(DraftDir);
		Directory.CreateDirectory(Path.Combine(DraftDir, "pages"));
		SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		// 规范化相对路径
		foreach (var p in Pages) {
			if (string.IsNullOrEmpty(p.ImageFile))
				p.ImageFile = Path.Combine("pages", $"{p.Index:D3}.png").Replace('\\', '/');
			else
				p.ImageFile = p.ImageFile.Replace('\\', '/');
		}
		var json = JsonSerializer.Serialize(this, JsonOpt);
		File.WriteAllText(Path.Combine(DraftDir, "project.json"), json, new UTF8Encoding(false));
		Dirty = false;
	}

	public static PdfOcrProject LoadDraft(string dir) {
		if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
			throw new DirectoryNotFoundException("草稿目录不存在: " + dir);
		var jsonPath = Path.Combine(dir, "project.json");
		if (!File.Exists(jsonPath))
			throw new FileNotFoundException("缺少 project.json", jsonPath);
		var json = File.ReadAllText(jsonPath, Encoding.UTF8);
		var proj = JsonSerializer.Deserialize<PdfOcrProject>(json, JsonOpt)
			?? throw new InvalidOperationException("草稿损坏");
		proj.DraftDir = dir;
		proj.Pages ??= new List<PdfPageEdit>();
		foreach (var p in proj.Pages)
			p.Lines ??= new List<PdfLineEdit>();
		proj.Dirty = false;
		return proj;
	}

	/// <summary>默认草稿列表（LocalAppData\\ScreenKit\\drafts）。</summary>
	public static List<(string Dir, string Title, string SavedAt, int Pages)> ListDrafts() {
		var root = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"ScreenKit", "drafts");
		var list = new List<(string, string, string, int)>();
		if (!Directory.Exists(root)) return list;
		foreach (var dir in Directory.GetDirectories(root).OrderByDescending(d => d)) {
			var jp = Path.Combine(dir, "project.json");
			if (!File.Exists(jp)) continue;
			try {
				var p = LoadDraft(dir);
				list.Add((dir, string.IsNullOrEmpty(p.Title) ? Path.GetFileName(dir) : p.Title,
					p.SavedAt ?? "", p.Pages?.Count ?? 0));
			}
			catch { }
		}
		return list;
	}
}
