namespace ScreenKit;

/// <summary>一条对话消息（user / assistant）。</summary>
sealed class LlmChatTurn {
	public string Role = "";
	public string Text = "";
}

/// <summary>气泡绑定项（含界面错误/工具泡，不进历史）。</summary>
sealed class LlmChatBubble {
	public string Role { get; set; } = "";
	public string Text { get; set; } = "";
	public bool IsUser { get; set; }
	public bool IsError { get; set; }
	public bool IsTool { get; set; }
}

/// <summary>进程内单会话。最多 60 条（30 轮），总字数 ≤ 12000。不落盘。</summary>
sealed class LlmChatHistory {
	public const int MAXTURNS = 60;
	public const int MAXCHARS = 12000;
	readonly List<LlmChatTurn> turns = new();
	readonly object gate = new();

	public int Count {
		get { lock (gate) return turns.Count; }
	}

	public void Add(string role, string text) {
		role = (role ?? "").Trim().ToLowerInvariant();
		text = (text ?? "").Trim();
		if (role is not "user" and not "assistant") return;
		if (text.Length == 0) return;
		lock (gate) {
			turns.Add(new LlmChatTurn { Role = role, Text = text });
			trimto();
		}
	}

	public void Clear() {
		lock (gate) turns.Clear();
	}

	public List<LlmChatTurn> Snapshot() {
		lock (gate) {
			return turns.Select(t => new LlmChatTurn { Role = t.Role, Text = t.Text }).ToList();
		}
	}

	public List<(string role, string content)> ToMessages() {
		lock (gate) {
			var list = new List<(string, string)>(turns.Count);
			foreach (var t in turns) {
				if (t.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(t.Text))
					list.Add((t.Role, t.Text));
			}
			return list;
		}
	}

	void trimto() {
		while (turns.Count > 0 && turns[0].Role == "assistant")
			turns.RemoveAt(0);
		while (over()) {
			if (turns.Count == 0) break;
			if (turns[0].Role != "user") {
				turns.RemoveAt(0);
				continue;
			}
			turns.RemoveAt(0);
			if (turns.Count > 0 && turns[0].Role == "assistant")
				turns.RemoveAt(0);
			while (turns.Count > 0 && turns[0].Role == "assistant")
				turns.RemoveAt(0);
		}
	}

	bool over() {
		if (turns.Count > MAXTURNS) return true;
		var n = 0;
		foreach (var t in turns) n += t.Text.Length;
		return n > MAXCHARS;
	}
}
