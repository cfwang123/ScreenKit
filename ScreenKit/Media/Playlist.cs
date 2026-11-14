using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using ScreenKit.Json;

namespace ScreenKit;

enum PlayMode { Sequential, Shuffle, RepeatOne, RepeatAll }

enum PlayTrigger { Manual, AutoNext, UserPrev, UserNext }

sealed class PlaylistItem : INotifyPropertyChanged {
	public string Path = "";
	public string Title = "";
	public string Artist = "";
	double durationSec;
	public double DurationSec {
		get => durationSec;
		set {
			if (Math.Abs(durationSec - value) < 0.001) return;
			durationSec = value;
			raise(nameof(DurationSec));
			raise(nameof(DurText));
			raise(nameof(Display));
		}
	}
	public MediaKind Kind = MediaKind.Unknown;
	bool missing;
	public bool Missing {
		get => missing;
		set {
			if (missing == value) return;
			missing = value;
			raise(nameof(Missing));
			raise(nameof(Display));
		}
	}

	public event PropertyChangedEventHandler PropertyChanged;

	void raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	/// <summary>时长文本，列表右侧显示用；未知时留空。</summary>
	public string DurText => DurationSec > 0
		? (DurationSec >= 3600 ? $"{(int)(DurationSec / 3600)}:{((int)(DurationSec / 60)) % 60:00}:{(int)(DurationSec % 60):00}"
			: $"{(int)(DurationSec / 60)}:{(int)(DurationSec % 60):00}")
		: "";
	public string Display {
		get {
			var name = Title.Length > 0 ? Title : System.IO.Path.GetFileNameWithoutExtension(Path);
			var ext = System.IO.Path.GetExtension(Path);
			if (ext.Length > 1 && Kind != MediaKind.Unknown)
				return name + "  ·" + ext.TrimStart('.').ToLowerInvariant();
			return name;
		}
	}
}

/// <summary>播放列表：拖入追加、洗牌袋、顺序/随机/单曲循环、JSON 持久化。</summary>
sealed class Playlist {
	public readonly ObservableCollection<PlaylistItem> Items = new();

	List<int> bag;
	int bagpos = -1;
	int lastplayed = -1;

	public int Index { get; private set; } = -1;
	public PlayMode Mode { get; set; } = PlayMode.Sequential;
	public event Action Changed;
	public event Action<PlaylistItem, PlayTrigger> ItemRequested;

	public PlaylistItem Current =>
		Index >= 0 && Index < Items.Count ? Items[Index] : null;

	public void RaiseChanged() => Changed?.Invoke();

	// ==================== 增删 ====================
	#region
	public int AddFiles(IEnumerable<string> paths) {
		var n = 0;
		foreach (var p in paths) {
			if (addone(p)) n++;
		}
		if (n > 0) { RaisChangedSafe(); }
		return n;
	}

	/// <summary>目录递归展开后追加；返回实际加入数量。</summary>
	public int AddDirectory(string dir, int maxDepth = 8, int maxCount = 3000) {
		var files = MediaFormat.EnumerateMedia(dir, maxDepth, maxCount);
		return AddFiles(files);
	}

	bool addone(string p) {
		if (string.IsNullOrWhiteSpace(p)) return false;
		var kind = MediaFormat.Kind(p);
		if (kind == MediaKind.Unknown) return false;
		var item = new PlaylistItem {
			Path = p,
			Kind = kind,
			Title = Path.GetFileNameWithoutExtension(p),
			Missing = !File.Exists(p),
		};
		Items.Add(item);
		return true;
	}

	public void RemoveAt(int i) {
		if (i < 0 || i >= Items.Count) return;
		var wascurrent = i == Index;
		Items.RemoveAt(i);
		if (Index > i) Index--;
		else if (wascurrent) {
			if (Index >= Items.Count) Index = Items.Count - 1;
			RaisChangedSafe();
		}
		bag = null;
		RaisChangedSafe();
	}

	public void Clear() {
		Items.Clear();
		Index = -1;
		bag = null;
		bagpos = -1;
		lastplayed = -1;
		RaisChangedSafe();
	}

	/// <summary>移除路径已不存在的项；返回移除数量。</summary>
	public int RemoveMissing() {
		var n = 0;
		for (var i = Items.Count - 1; i >= 0; i--) {
			if (!Items[i].Missing && File.Exists(Items[i].Path)) continue;
			if (i == Index) Index = -1;
			else if (i < Index) Index--;
			Items.RemoveAt(i);
			n++;
		}
		if (n > 0) { bag = null; RaisChangedSafe(); }
		return n;
	}

	/// <summary>路径去重，保留第一次出现；返回移除数量。</summary>
	public int Dedupe() {
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var n = 0;
		for (var i = Items.Count - 1; i >= 0; i--) {
			var key = Items[i].Path;
			if (seen.Contains(key)) {
				if (i == Index) Index = -1;
				else if (i < Index) Index--;
				Items.RemoveAt(i);
				n++;
			}
			else seen.Add(key);
		}
		if (n > 0) { bag = null; RaisChangedSafe(); }
		return n;
	}

	void RaisChangedSafe() {
		try { Changed?.Invoke(); } catch { }
	}
	#endregion

	// ==================== 推进 ====================
	#region
	public void Select(int i) {
		if (Items.Count == 0) { Index = -1; return; }
		if (i < 0) i = 0;
		if (i >= Items.Count) i = Items.Count - 1;
		Index = i;
		lastplayed = i;
		RaisChangedSafe();
	}

	/// <summary>当前项自然播完后该干什么；返回 false 表示列表结束。</summary>
	public bool CanAdvance() {
		if (Items.Count == 0) return false;
		switch (Mode) {
			case PlayMode.RepeatOne:
				return true;
			case PlayMode.Sequential:
				return Index >= 0 && Index + 1 < Items.Count;
			case PlayMode.RepeatAll:
			case PlayMode.Shuffle:
				return true;
			default:
				return false;
		}
	}

	/// <summary>按模式推进到下一首；失败返回 null。</summary>
	public PlaylistItem Advance(PlayTrigger tr) {
		var item = next(tr);
		if (item == null) return null;
		ItemRequested?.Invoke(item, tr);
		return item;
	}

	public PlaylistItem next(PlayTrigger tr) {
		if (Items.Count == 0) return null;
		switch (Mode) {
			case PlayMode.RepeatOne:
				if (Index < 0) Index = 0;
				return Items[Index];
			case PlayMode.Shuffle: {
				var i = nextshuffle();
				if (i < 0) return null;
				Index = i;
				lastplayed = i;
				RaisChangedSafe();
				return Items[i];
			}
			case PlayMode.RepeatAll: {
				var i = Index + 1;
				if (i >= Items.Count) i = 0;
				Index = i;
				lastplayed = i;
				RaisChangedSafe();
				return Items[i];
			}
			default: {
				var i = Index + 1;
				if (i < 0) i = 0;
				if (i >= Items.Count) return null;
				Index = i;
				lastplayed = i;
				RaisChangedSafe();
				return Items[i];
			}
		}
	}

	/// <summary>上一首：随机模式走洗牌袋上一格。</summary>
	public PlaylistItem Previous() {
		if (Items.Count == 0) return null;
		if (Mode == PlayMode.Shuffle && bag != null && bagpos > 1) {
			bagpos -= 2;
			var i = bag[bagpos++];
			Index = i;
			lastplayed = i;
			RaisChangedSafe();
			return Items[i];
		}
		var p = Index - 1;
		if (p < 0) p = Mode == PlayMode.RepeatAll ? Items.Count - 1 : 0;
		Index = p;
		lastplayed = p;
		RaisChangedSafe();
		return Items[p];
	}

	/// <summary>洗牌袋：一轮内不重复，且首项不撞上一轮刚播完的那首。</summary>
	int nextshuffle() {
		if (bag == null || bagpos >= bag.Count || bagpos < 0) {
			bag = new List<int>(Items.Count);
			for (var i = 0; i < Items.Count; i++) bag.Add(i);
			var rnd = new Random(Environment.TickCount ^ Items.Count);
			for (var i = bag.Count - 1; i > 0; i--) {
				var j = rnd.Next(i + 1);
				(bag[i], bag[j]) = (bag[j], bag[i]);
			}
			// 新一轮第一首别和上一轮末尾重复
			if (lastplayed >= 0 && bag.Count > 1 && bag[0] == lastplayed) {
				var j = rnd.Next(1, bag.Count);
				(bag[0], bag[j]) = (bag[j], bag[0]);
			}
			bagpos = 0;
		}
		return bag[bagpos++];
	}
	#endregion

	// ==================== 持久化 ====================
	#region
	public static string FilePath =>
		System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playlist.json");

	public void Save() {
		try {
			var items = JSONF.newArray();
			foreach (var it in Items) {
				items.Add(JSONF.newObject(
					("path", JSONF.newData(it.Path)),
					("title", JSONF.newData(it.Title)),
					("artist", JSONF.newData(it.Artist)),
					("dur", JSONF.newData(it.DurationSec))));
			}
			var root = JSONF.newObject(
				("index", JSONF.newData(Index)),
				("mode", JSONF.newData(modename(Mode))),
				("items", items));
			File.WriteAllText(FilePath, root.ToString(), new UTF8Encoding(false));
		}
		catch { }
	}

	public void Load() {
		try {
			if (!File.Exists(FilePath)) return;
			var text = File.ReadAllText(FilePath, Encoding.UTF8);
			var root = JSONF.Parse(text);
			if (root == null || root.Type != JSONF.NodeType.Object) return;
			Items.Clear();
			var arr = root["items"];
			if (arr.IsNotNull && arr.Type == JSONF.NodeType.Array) {
				for (var i = 0; i < arr.Count; i++) {
					var jo = arr[i];
					if (jo.IsNull || jo.Type != JSONF.NodeType.Object) continue;
					var p = jo["path"].Value ?? "";
					if (p.Length == 0) continue;
					Items.Add(new PlaylistItem {
						Path = p,
						Title = jo["title"].IsNotNull ? jo["title"].Value : System.IO.Path.GetFileNameWithoutExtension(p),
						Artist = jo["artist"].IsNotNull ? jo["artist"].Value : "",
						DurationSec = jo["dur"].IsNotNull ? jo["dur"].AsDouble : 0,
						Kind = MediaFormat.Kind(p),
						Missing = !File.Exists(p),
					});
				}
			}
			Mode = modeparse(root["mode"].IsNotNull ? root["mode"].Value : "sequential");
			var idx = root["index"].IsNull ? -1 : root["index"].AsInt;
			if (idx >= 0 && idx < Items.Count) Index = idx;
			else Index = Items.Count > 0 ? 0 : -1;
			RaisChangedSafe();
		}
		catch { }
	}

	static string modename(PlayMode m) => m switch {
		PlayMode.Shuffle => "shuffle",
		PlayMode.RepeatOne => "repeatone",
		PlayMode.RepeatAll => "repeatall",
		_ => "sequential",
	};

	public static PlayMode modeparse(string s) => (s ?? "").Trim().ToLowerInvariant() switch {
		"shuffle" => PlayMode.Shuffle,
		"repeatone" => PlayMode.RepeatOne,
		"repeatall" => PlayMode.RepeatAll,
		_ => PlayMode.Sequential,
	};
	#endregion
}

