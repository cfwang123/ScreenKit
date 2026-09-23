namespace ScreenKit;

public sealed class SendFileMsg {
	public long Id;
	public string Text = "";
	public long Unix;
}

/// <summary>文本同步队列：手机 POST 进电脑收件箱；电脑发送进各设备待拉取队列。不碰系统剪贴板。</summary>
public sealed class SendFileText {
	readonly object gate = new();
	readonly List<SendFileMsg> inbox = new();
	readonly Dictionary<string, List<SendFileMsg>> outbox = new(StringComparer.OrdinalIgnoreCase);
	long nextId = 1;
	const int MAXKEEP = 200;

	public event Action<SendFileMsg> InboxArrived;

	public SendFileMsg PushInbox(string text) {
		var msg = make(text);
		lock (gate) {
			inbox.Add(msg);
			trim(inbox);
		}
		try { InboxArrived?.Invoke(msg); } catch { }
		return msg;
	}

	public void PushOut(string deviceId, string text) {
		if (string.IsNullOrWhiteSpace(deviceId)) return;
		var msg = make(text);
		lock (gate) {
			if (!outbox.TryGetValue(deviceId, out var list)) {
				list = new List<SendFileMsg>();
				outbox[deviceId] = list;
			}
			list.Add(msg);
			trim(list);
		}
	}

	public List<SendFileMsg> PullOut(string deviceId, long since) {
		lock (gate) {
			if (!outbox.TryGetValue(deviceId, out var list) || list.Count == 0)
				return new List<SendFileMsg>();
			return list.Where(x => x.Id > since).ToList();
		}
	}

	public List<SendFileMsg> SnapshotInbox() {
		lock (gate) return inbox.ToList();
	}

	SendFileMsg make(string text) {
		lock (gate) {
			return new SendFileMsg {
				Id = nextId++,
				Text = text ?? "",
				Unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			};
		}
	}

	static void trim(List<SendFileMsg> list) {
		if (list.Count <= MAXKEEP) return;
		list.RemoveRange(0, list.Count - MAXKEEP);
	}
}
