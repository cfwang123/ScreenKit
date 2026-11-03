using System.Text;
using ScreenKit.Json;

namespace ScreenKit;

/// <summary>
/// 文档阅读器的历史文件与上次打开的标签。存在 data.json 的 mdread 段，
/// 不进 config.toml：这是界面状态，跟着阅读器走。
/// </summary>
sealed class MdReadState {
	/// <summary>data.json 里这一段的名字。</summary>
	public const string SECTION = "mdread";

	/// <summary>历史文件，最近的在前，最多 <see cref="HISTORY_MAX"/> 条。</summary>
	public List<string> History { get; set; } = new();

	/// <summary>上次关闭窗口时开着的文件，按标签顺序；下次开窗按它恢复。</summary>
	public List<string> Tabs { get; set; } = new();

	/// <summary>上次看的是第几个标签（0-based）。</summary>
	public int ActiveTab { get; set; }

	/// <summary>菜单「历史文件」最多显示几条。</summary>
	public const int HISTORY_MAX = 10;

	public static string DefaultPath => DataStore.StorePath;

	/// <summary>读状态；没存过或坏了都当空状态，不打扰用户。</summary>
	public static MdReadState Load(string path = null) {
		try {
			MdReadState st;
			if (path == null) st = DataStore.Get<MdReadState>(SECTION);
			else {
				if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new MdReadState();
				st = JSONF.Parse(File.ReadAllText(path)).ToObject<MdReadState>();
			}
			st ??= new MdReadState();
			st.History = clean(st.History, HISTORY_MAX);
			st.Tabs = clean(st.Tabs, int.MaxValue);
			if (st.ActiveTab < 0) st.ActiveTab = 0;
			return st;
		}
		catch {
			return new MdReadState();
		}
	}

	public void Save(string path = null) {
		if (path == null) {
			DataStore.Put(SECTION, this);
			return;
		}
		try {
			var json = JSONF.FromObject(this).ToJSONString(0);
			var dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			File.WriteAllText(path, json, new UTF8Encoding(false));
		}
		catch { /* 存不下来不影响使用 */ }
	}

	/// <summary>把一个文件提到历史最前面；已在里面就移位，不重复。</summary>
	public void Touch(string path) {
		if (string.IsNullOrWhiteSpace(path)) return;
		path = path.Trim();
		if (!File.Exists(path)) return;
		History.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
		History.Insert(0, path);
		if (History.Count > HISTORY_MAX)
			History.RemoveRange(HISTORY_MAX, History.Count - HISTORY_MAX);
	}

	/// <summary>记下当前开着的标签与选中的那个。</summary>
	public void RememberTabs(IEnumerable<string> paths, int active) {
		Tabs = clean(paths?.ToList(), int.MaxValue);
		ActiveTab = Math.Max(0, active);
	}

	/// <summary>去掉空项和重复项（按顺序保留第一次出现），截到 max 条。</summary>
	static List<string> clean(List<string> src, int max) {
		var list = new List<string>();
		if (src == null) return list;
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in src) {
			if (string.IsNullOrWhiteSpace(p)) continue;
			var s = p.Trim();
			if (!seen.Add(s)) continue;
			list.Add(s);
			if (list.Count >= max) break;
		}
		return list;
	}
}
