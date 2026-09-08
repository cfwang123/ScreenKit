using System.Text;

namespace ScreenKit;

/// <summary>Agent 一轮结果（最终助手正文 + 可选工具摘要）。</summary>
sealed class LlmAgentResult {
	public string Reply = "";
	public List<string> ToolNotes = new();
}

/// <summary>
/// 简单文本协议 Agent：解析 &lt;tool_call&gt;…&lt;/tool_call&gt;，在 tmp/llm/ 沙箱执行工具后回灌模型。
/// 工具模式不做 length 续写。
/// </summary>
static class LlmAgent {
	public const int MaxRounds = 6;
	public const int TimeoutMs = 120_000;

	const string ToolPromptZh =
		"\n\n你可以使用工具。需要搜索、读写文件或运行脚本时，先输出一个或多个：" +
		"<tool_call>{\"name\":\"工具名\",\"arguments\":{…}}</tool_call>\n" +
		"可用工具：\n" +
		"- web_search: {\"query\":\"…\",\"count\":5}\n" +
		"- web_fetch: {\"url\":\"https://…\",\"max_chars\":8000}\n" +
		"- list_dir: {\"path\":\".\"}  （仅程序目录 tmp/llm/）\n" +
		"- read_file: {\"path\":\"相对路径\"}\n" +
		"- write_file: {\"path\":\"相对路径\",\"content\":\"…\"}\n" +
		"- run_script: {\"path\":\"script.py|.ps1|.bat|.cmd\",\"args\":[…]}\n" +
		"规则：只能访问 tmp/llm/；不要编造未返回的搜索结果；最终回答不要再包 tool_call。";

	const string ToolPromptEn =
		"\n\nYou can use tools. When you need search, files, or scripts, output one or more:\n" +
		"<tool_call>{\"name\":\"tool_name\",\"arguments\":{…}}</tool_call>\n" +
		"Tools:\n" +
		"- web_search: {\"query\":\"…\",\"count\":5}\n" +
		"- web_fetch: {\"url\":\"https://…\",\"max_chars\":8000}\n" +
		"- list_dir: {\"path\":\".\"}  (sandbox: app tmp/llm/ only)\n" +
		"- read_file: {\"path\":\"relative\"}\n" +
		"- write_file: {\"path\":\"relative\",\"content\":\"…\"}\n" +
		"- run_script: {\"path\":\"script.py|.ps1|.bat|.cmd\",\"args\":[…]}\n" +
		"Rules: only tmp/llm/; do not invent search results; final answer must not wrap tool_call.";

	/// <param name="onStatus">UI 状态回调（任意线程）。</param>
	/// <param name="onTool">每执行完一个工具回调摘要（任意线程）。</param>
	public static LlmAgentResult Run(
		OcrOptions o,
		IReadOnlyList<(string role, string content)> history,
		Action<string> onStatus,
		Action<string> onTool,
		CancellationToken ct) {
		var ep = o?.SelectedChatLlm();
		if (!AsrLlmClient.IsEndpointReady(ep))
			throw new InvalidOperationException("未配置对话 LLM（需 URL 与模型 id）");

		_ = LlmAgentPaths.Root; // ensure dir
		var prompt = (o.ChatLlmPrompt ?? "").Trim();
		if (prompt.Length == 0)
			prompt = OcrOptions.DefaultChatLlmPrompt();
		prompt += Loc.IsEn ? ToolPromptEn : ToolPromptZh;

		var msgs = new List<object> { new { role = "system", content = prompt } };
		var first = "";
		var last = "";
		if (history != null) {
			foreach (var (role, content) in history) {
				var r = (role ?? "").Trim().ToLowerInvariant();
				if (r is not "user" and not "assistant") continue;
				var t = (content ?? "").Trim();
				if (t.Length == 0) continue;
				if (first.Length == 0) first = r;
				last = r;
				msgs.Add(new { role = r, content = t });
			}
		}
		if (msgs.Count < 2 || first != "user" || last != "user")
			throw new InvalidOperationException("对话历史须以 user 开头且末条为 user");

		var notes = new List<string>();
		var wall0 = Environment.TickCount;
		onStatus?.Invoke(Loc.T("chat.status.thinking"));

		for (var round = 0; round < MaxRounds; round++) {
			ct.ThrowIfCancellationRequested();
			var used = unchecked(Environment.TickCount - wall0);
			var left = TimeoutMs - used;
			if (left < 3000)
				throw new TimeoutException("Agent 总超时");
			var roundMs = Math.Min(60_000, left);
			LlmLog.Info($"agent round={round} msgs={msgs.Count} left={left}ms");
			onStatus?.Invoke(Loc.T("chat.status.thinking"));
			var raw = AsrLlmClient.ChatOnce(ep, msgs.ToArray(), roundMs, 0.7f, ct);
			raw = (raw ?? "").Trim();
			if (raw.Length == 0)
				throw new InvalidOperationException("LLM 返回空回复");

			var calls = LlmAgentTools.ParseCalls(raw);
			if (calls.Count == 0) {
				var reply = LlmAgentTools.StripCalls(raw).Trim();
				if (reply.Length == 0) reply = raw;
				return new LlmAgentResult { Reply = reply, ToolNotes = notes };
			}

			// 把本轮 assistant（含 tool_call）写入上下文
			msgs.Add(new { role = "assistant", content = raw });
			var resultSb = new StringBuilder();
			foreach (var call in calls) {
				ct.ThrowIfCancellationRequested();
				var label = LlmAgentTools.ShortLabel(call);
				onStatus?.Invoke(Loc.T("chat.status.tool", label));
				onTool?.Invoke(label);
				var result = LlmAgentTools.Execute(call, ct);
				notes.Add(label);
				resultSb.Append("<tool_result name=\"")
					.Append(xmlattr(call.Name))
					.Append("\">\n")
					.Append(result ?? "")
					.Append("\n</tool_result>\n");
			}
			msgs.Add(new {
				role = "user",
				content = "工具结果如下，请根据结果继续；若已足够则给出最终回答（不要再包 tool_call）。\n"
					+ resultSb.ToString().TrimEnd(),
			});
		}

		throw new InvalidOperationException($"工具轮次用尽（{MaxRounds}），请缩小问题或关闭工具后重试");
	}

	static string xmlattr(string s) {
		return (s ?? "").Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
	}
}
