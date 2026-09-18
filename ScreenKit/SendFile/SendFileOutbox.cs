namespace ScreenKit;

sealed class SfOutFile {
	public long Id;
	public string StoreRel = "";
	public string PhoneRel = "";
	public string Name = "";
	public long Size;
}

/// <summary>电脑 → 手机待拉取文件队列（按设备）。</summary>
sealed class SendFileOutbox {
	readonly object gate = new();
	readonly Dictionary<string, List<SfOutFile>> map = new(StringComparer.OrdinalIgnoreCase);
	long nextId = 1;

	public SfOutFile Add(string deviceId, string storeRel, string phoneRel, long size) {
		if (string.IsNullOrWhiteSpace(deviceId)) return null;
		storeRel = (storeRel ?? "").Replace('\\', '/').Trim('/');
		phoneRel = (phoneRel ?? "").Replace('\\', '/').Trim('/');
		if (storeRel.Length == 0 || phoneRel.Length == 0) return null;
		var name = Path.GetFileName(phoneRel);
		if (string.IsNullOrEmpty(name)) name = Path.GetFileName(storeRel);
		lock (gate) {
			if (!map.TryGetValue(deviceId, out var list)) {
				list = new List<SfOutFile>();
				map[deviceId] = list;
			}
			var it = new SfOutFile {
				Id = nextId++,
				StoreRel = storeRel,
				PhoneRel = phoneRel,
				Name = name ?? "",
				Size = size < 0 ? 0 : size,
			};
			list.Add(it);
			return it;
		}
	}

	public List<SfOutFile> List(string deviceId) {
		lock (gate) {
			if (string.IsNullOrWhiteSpace(deviceId) || !map.TryGetValue(deviceId, out var list))
				return new List<SfOutFile>();
			return list.ToList();
		}
	}

	public SfOutFile Remove(string deviceId, long id) {
		lock (gate) {
			if (string.IsNullOrWhiteSpace(deviceId) || !map.TryGetValue(deviceId, out var list))
				return null;
			var i = list.FindIndex(x => x.Id == id);
			if (i < 0) return null;
			var it = list[i];
			list.RemoveAt(i);
			return it;
		}
	}
}
