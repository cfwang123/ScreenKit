using System.Text;
using System.Text.Json;

namespace ScreenKit;

/// <summary>桌面便笺。文字或待办存在 exe 旁的 note.json，关掉再开仍在。</summary>
public sealed class DeskNote {
	public bool Todo { get; set; }
	public string Text { get; set; } = "";
	public List<DeskNoteItem> Items { get; set; } = new();
	public double Left { get; set; }
	public double Top { get; set; }
	public double Width { get; set; }
	public double Height { get; set; }
	public bool HasPos { get; set; }

	public static string DefaultPath =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "note.json");

	public static DeskNote Load(string path) {
		try {
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new DeskNote();
			var note = JsonSerializer.Deserialize<DeskNote>(File.ReadAllText(path));
			if (note == null) return new DeskNote();
			if (note.Text == null) note.Text = "";
			if (note.Items == null) note.Items = new List<DeskNoteItem>();
			for (var i = 0; i < note.Items.Count; i++) {
				if (note.Items[i] == null) note.Items[i] = new DeskNoteItem();
				else if (note.Items[i].Text == null) note.Items[i].Text = "";
			}
			return note;
		}
		catch {
			return new DeskNote();
		}
	}

	public void Save(string path) {
		if (string.IsNullOrWhiteSpace(path)) return;
		var json = JsonSerializer.Serialize(this, Opt);
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(path, json, new UTF8Encoding(false));
	}

	public static List<DeskNoteItem> Parse(string text) {
		var list = new List<DeskNoteItem>();
		var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		foreach (var line in lines) {
			var item = new DeskNoteItem { Text = line };
			if (line.Length >= 3 && line[0] == '[' && line[2] == ']') {
				var mark = line[1];
				if (mark == 'x' || mark == 'X' || mark == ' ') {
					item.Done = mark == 'x' || mark == 'X';
					item.Text = line.Length > 3 && line[3] == ' ' ? line.Substring(4) : line.Substring(3);
				}
			}
			list.Add(item);
		}
		return list;
	}

	public static string Format(IList<DeskNoteItem> items) {
		if (items == null || items.Count == 0) return "";
		var sb = new StringBuilder();
		for (var i = 0; i < items.Count; i++) {
			if (i > 0) sb.Append('\n');
			var it = items[i] ?? new DeskNoteItem();
			sb.Append(it.Done ? "[x] " : "[ ] ");
			sb.Append(it.Text ?? "");
		}
		return sb.ToString();
	}

	static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
}

public sealed class DeskNoteItem {
	public string Text { get; set; } = "";
	public bool Done { get; set; }
}
