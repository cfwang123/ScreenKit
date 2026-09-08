using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ScreenKit;

public partial class MainWindow {
	readonly LlmChatHistory chatHist = new();
	readonly ObservableCollection<LlmChatBubble> chatItems = new();
	CancellationTokenSource chatCts;
	CancellationTokenSource chatTtsCts;
	TtsPlayer chatPlayer;
	string chatLastAssistant = "";
	bool chatBusy;
	bool chatSpeaking;
	bool chatUiLoading;
	int chatRound;
	/// <summary>对话麦开始聆听 TickCount；0=非语音轮。</summary>
	int chatAsrStart;
	/// <summary>本轮待计入的 ASR 用时（聆听+识别，ms）；发送后清零。</summary>
	int chatPendingAsrMs;

	void initchat() {
		chatPlayer = new TtsPlayer();
		icchat.ItemsSource = chatItems;
		bchatsend.Click += (_, _) => onchatsend();
		bchatclear.Click += (_, _) => onchatclear();
		if (bchatmic != null) bchatmic.Click += (_, _) => onchatmic();
		if (bchatspeak != null) bchatspeak.Click += (_, _) => onchatspeak();
		if (bchatlogclear != null) bchatlogclear.Click += (_, _) => clearchatlog();
		echatllm.SelectionChanged += (_, _) => {
			if (chatUiLoading) return;
			savechatllm();
		};
		if (echatagent != null) {
			echatagent.IsChecked = opt.ChatAgent;
			echatagent.Checked += (_, _) => savechatagent(true);
			echatagent.Unchecked += (_, _) => savechatagent(false);
		}
		if (echatautotts != null) {
			echatautotts.IsChecked = opt.ChatAutoTts;
			echatautotts.Checked += (_, _) => savechatautotts(true);
			echatautotts.Unchecked += (_, _) => savechatautotts(false);
		}
		echatinput.PreviewKeyDown += onchatinputkey;
		fillchatllm();
		updatechatmicui();
		setchatstatus(Loc.T("chat.status.idle"));
	}

	void fillchatllm() {
		chatUiLoading = true;
		try {
			var want = (opt.ChatLlm ?? "").Trim();
			if (want.Length == 0)
				want = (opt.AsrLlm ?? "").Trim();
			echatllm.Items.Clear();
			ComboBoxItem pick = null;
			if (opt.LlmList != null) {
				foreach (var ep in opt.LlmList) {
					if (ep == null) continue;
					var name = ep.DisplayName;
					if (name.Length == 0) continue;
					var it = new ComboBoxItem {
						Content = name,
						Tag = ep,
						ToolTip = string.IsNullOrWhiteSpace(ep.Model) ? name : ep.Model,
					};
					echatllm.Items.Add(it);
					if (pick == null &&
						(string.Equals(name, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(ep.Model ?? "", want, StringComparison.OrdinalIgnoreCase)))
						pick = it;
				}
			}
			if (echatllm.Items.Count == 0) {
				echatllm.Items.Add(new ComboBoxItem {
					Content = Loc.T("chat.llm.none"),
					Tag = null,
					IsEnabled = false,
				});
				echatllm.SelectedIndex = 0;
			}
			else
				echatllm.SelectedItem = pick ?? echatllm.Items[0];
			if (echatagent != null)
				echatagent.IsChecked = opt.ChatAgent;
			if (echatautotts != null)
				echatautotts.IsChecked = opt.ChatAutoTts;
		}
		finally { chatUiLoading = false; }
	}

	LlmEndpoint currentchatllm() =>
		echatllm?.SelectedItem is ComboBoxItem it ? it.Tag as LlmEndpoint : null;

	void savechatllm() {
		try {
			var ep = currentchatllm();
			opt.ChatLlm = ep != null ? ep.DisplayName : "";
			AppConfig.Save(opt);
		}
		catch (Exception ex) {
			CaptureLog.Ex("savechatllm", ex);
		}
	}

	void savechatagent(bool on) {
		if (chatUiLoading) return;
		try {
			opt.ChatAgent = on;
			AppConfig.Save(opt);
		}
		catch (Exception ex) {
			CaptureLog.Ex("savechatagent", ex);
		}
	}

	void savechatautotts(bool on) {
		if (chatUiLoading) return;
		try {
			opt.ChatAutoTts = on;
			AppConfig.Save(opt);
		}
		catch (Exception ex) {
			CaptureLog.Ex("savechatautotts", ex);
		}
	}

	void onchatinputkey(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter) return;
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
		e.Handled = true;
		onchatsend();
	}

	void onchatclear() {
		stopchattts();
		if (chatBusy) {
			try { chatCts?.Cancel(); } catch { }
			try { LlmAgentTools.CancelRunning(); } catch { }
		}
		if (asrVoiceChatCapture && asrVoice != null && asrVoice.IsActive)
			try { togglechatvoice(); } catch { }
		chatHist.Clear();
		chatItems.Clear();
		chatLastAssistant = "";
		chatRound = 0;
		chatPendingAsrMs = 0;
		chatAsrStart = 0;
		clearchatlog();
		setchatstatus(Loc.T("chat.status.idle"));
	}

	void onchatmic() {
		if (chatBusy) {
			setchatstatus(Loc.T("chat.status.thinking"));
			return;
		}
		// 开始聆听时打点；结束成句在 onchatutterance 算 asr 用时
		if (!(asrVoiceChatCapture && asrVoice != null && asrVoice.IsActive))
			chatAsrStart = Environment.TickCount;
		togglechatvoice();
	}

	void onchatspeak() {
		if (chatSpeaking) {
			stopchattts();
			setchatstatus(Loc.T("chat.status.idle"));
			return;
		}
		var text = chatLastAssistant;
		if (string.IsNullOrWhiteSpace(text)) {
			setchatstatus(Loc.T("chat.err.notts"));
			return;
		}
		_ = chatspeakasync(text);
	}

	/// <summary>ASR CaptureOnly 成句 → 填入并发送。</summary>
	void onchatutterance(string text) {
		text = (text ?? "").Trim();
		if (text.Length == 0) return;
		if (chatAsrStart != 0) {
			chatPendingAsrMs = Math.Max(0, Environment.TickCount - chatAsrStart);
			chatAsrStart = 0;
			addchatlog($"asr 成句 {chatPendingAsrMs}ms · {clipchatlog(text, 40)}");
		}
		if (chatBusy) {
			// 识别忙时先塞进输入框，不打断当前回复
			echatinput.Text = string.IsNullOrWhiteSpace(echatinput.Text)
				? text : (echatinput.Text.TrimEnd() + " " + text);
			setchatstatus(Loc.T("chat.status.thinking"));
			return;
		}
		echatinput.Text = text;
		// 成句后停麦再发送，避免占麦
		if (asrVoiceChatCapture && asrVoice != null && asrVoice.IsActive) {
			try { togglechatvoice(); } catch { }
		}
		onchatsend();
	}

	void updatechatmicui() {
		if (bchatmic == null) return;
		var on = asrVoiceChatCapture && asrVoice != null && asrVoice.IsActive;
		bchatmic.Content = Loc.T(on ? "chat.mic.on" : "chat.mic");
		bchatmic.IsEnabled = !chatBusy;
		if (bchatspeak != null) {
			bchatspeak.Content = Loc.T(chatSpeaking ? "chat.speak.stop" : "chat.speak");
			bchatspeak.IsEnabled = !chatBusy || chatSpeaking;
		}
	}

	void onchatsend() {
		if (chatBusy) {
			try { chatCts?.Cancel(); } catch { }
			try { LlmAgentTools.CancelRunning(); } catch { }
			stopchattts();
			return;
		}
		var text = (echatinput.Text ?? "").Trim();
		if (text.Length == 0) return;
		if (text.Length > 8000)
			text = text.Substring(0, 8000);

		stopchattts();
		var o = opt.Clone();
		var pick = currentchatllm();
		if (pick != null)
			o.ChatLlm = pick.DisplayName;
		o.ChatAgent = echatagent?.IsChecked == true;
		if (!AsrLlmClient.IsChatReady(o)) {
			addchatbubble("error", Loc.T("chat.err.nollm"));
			setchatstatus(Loc.T("chat.err.nollm"));
			return;
		}

		echatinput.Clear();
		addchatbubble("user", text);
		chatHist.Add("user", text);
		var history = chatHist.ToMessages();
		var useAgent = o.ChatAgent;
		var asrMs = chatPendingAsrMs;
		chatPendingAsrMs = 0;
		chatRound++;
		var round = chatRound;
		var cts = new CancellationTokenSource();
		chatCts = cts;
		setchatbusy(true);
		setchatstatus(Loc.T("chat.status.thinking"));
		var llm0 = Environment.TickCount;
		_ = Task.Run(() => {
			try {
				string reply;
				if (useAgent) {
					var result = LlmAgent.Run(o, history,
						st => Dispatcher.BeginInvoke(new Action(() => {
							if (cts == chatCts) setchatstatus(st);
						})),
						note => Dispatcher.BeginInvoke(new Action(() => {
							if (cts == chatCts) addchatbubble("tool", note);
						})),
						cts.Token);
					reply = result?.Reply;
				}
				else {
					reply = AsrLlmClient.Chat(o, history, cts.Token);
				}
				var llmMs = Math.Max(0, Environment.TickCount - llm0);
				Dispatcher.BeginInvoke(new Action(() =>
					finishchat(reply, null, cts, round, asrMs, llmMs, useAgent)));
			}
			catch (OperationCanceledException) {
				var llmMs = Math.Max(0, Environment.TickCount - llm0);
				Dispatcher.BeginInvoke(new Action(() =>
					finishchat(null, "cancel", cts, round, asrMs, llmMs, useAgent)));
			}
			catch (Exception ex) {
				var llmMs = Math.Max(0, Environment.TickCount - llm0);
				Dispatcher.BeginInvoke(new Action(() =>
					finishchat(null, ex.Message, cts, round, asrMs, llmMs, useAgent)));
			}
		});
	}

	void finishchat(string reply, string err, CancellationTokenSource cts,
		int round, int asrMs, int llmMs, bool agent) {
		if (cts != chatCts) return;
		setchatbusy(false);
		chatCts = null;
		if (err == "cancel") {
			addchatlog($"#{round} 取消 · llm={llmMs}ms" + (asrMs > 0 ? $" asr={asrMs}ms" : ""));
			setchatstatus(Loc.T("chat.status.idle"));
			return;
		}
		if (!string.IsNullOrEmpty(err)) {
			addchatbubble("error", err);
			addchatlog($"#{round} 失败 · llm={llmMs}ms" + (asrMs > 0 ? $" asr={asrMs}ms" : "")
				+ " · " + clipchatlog(err, 60));
			setchatstatus(err);
			return;
		}
		reply = (reply ?? "").Trim();
		if (reply.Length == 0) {
			addchatbubble("error", Loc.T("chat.err.empty"));
			addchatlog($"#{round} 空回复 · llm={llmMs}ms" + (asrMs > 0 ? $" asr={asrMs}ms" : ""));
			setchatstatus(Loc.T("chat.err.empty"));
			return;
		}
		addchatbubble("assistant", reply);
		chatHist.Add("assistant", reply);
		chatLastAssistant = reply;
		setchatstatus(Loc.T("chat.status.idle"));
		if (echatautotts?.IsChecked == true)
			_ = chatspeakasync(reply, round, asrMs, llmMs, agent);
		else {
			logchatround(round, asrMs, llmMs, ttsMs: 0, agent, reply);
		}
	}

	async Task chatspeakasync(string text, int round = 0, int asrMs = 0, int llmMs = 0,
		bool agent = false) {
		text = (text ?? "").Trim();
		if (text.Length == 0) return;
		stopchattts();
		chatTtsCts = new CancellationTokenSource();
		var ct = chatTtsCts.Token;
		chatSpeaking = true;
		updatechatmicui();
		var tts0 = Environment.TickCount;
		try {
			await speaktextasync(text, chatPlayer, ct, s => {
				if (!ct.IsCancellationRequested) setchatstatus(s);
			}).ConfigureAwait(true);
			var ttsMs = Math.Max(0, Environment.TickCount - tts0);
			if (round > 0)
				logchatround(round, asrMs, llmMs, ttsMs, agent, text);
			else
				addchatlog($"朗读 {ttsMs}ms · {clipchatlog(text, 40)}");
			if (!ct.IsCancellationRequested)
				setchatstatus(Loc.T("chat.status.idle"));
		}
		catch (OperationCanceledException) {
			var ttsMs = Math.Max(0, Environment.TickCount - tts0);
			if (round > 0)
				addchatlog($"#{round} 朗读取消 · llm={llmMs}ms tts={ttsMs}ms"
					+ (asrMs > 0 ? $" asr={asrMs}ms" : ""));
			setchatstatus(Loc.T("chat.status.idle"));
		}
		catch (Exception ex) {
			CaptureLog.Ex("chatspeak", ex);
			var ttsMs = Math.Max(0, Environment.TickCount - tts0);
			if (round > 0)
				addchatlog($"#{round} 朗读失败 · tts={ttsMs}ms · {clipchatlog(ex.Message, 40)}");
			setchatstatus(ex.Message);
		}
		finally {
			chatSpeaking = false;
			updatechatmicui();
		}
	}

	void logchatround(int round, int asrMs, int llmMs, int ttsMs, bool agent, string reply) {
		var sb = new StringBuilder();
		sb.Append('#').Append(round);
		if (agent) sb.Append(" agent");
		sb.Append(" · llm=").Append(llmMs).Append("ms");
		if (asrMs > 0) sb.Append(" asr=").Append(asrMs).Append("ms");
		if (ttsMs > 0) sb.Append(" tts=").Append(ttsMs).Append("ms");
		var total = llmMs + Math.Max(0, asrMs) + Math.Max(0, ttsMs);
		sb.Append(" total=").Append(total).Append("ms");
		sb.Append(" · ").Append(clipchatlog(reply, 36));
		addchatlog(sb.ToString());
	}

	void addchatlog(string line) {
		if (echatlog == null) return;
		line = (line ?? "").Trim();
		if (line.Length == 0) return;
		var stamp = DateTime.Now.ToString("HH:mm:ss");
		var row = stamp + "  " + line;
		try {
			if (echatlog.Text.Length > 0)
				echatlog.AppendText(Environment.NewLine + row);
			else
				echatlog.Text = row;
			svchatlog?.ScrollToEnd();
		}
		catch { }
		try { CaptureLog.Info("chat " + line); } catch { }
	}

	void clearchatlog() {
		try { if (echatlog != null) echatlog.Text = ""; } catch { }
	}

	static string clipchatlog(string s, int max) {
		s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
		if (s.Length <= max) return s;
		return s.Substring(0, max) + "…";
	}

	void stopchattts() {
		try { chatTtsCts?.Cancel(); } catch { }
		try { chatPlayer?.Stop(); } catch { }
		chatSpeaking = false;
		updatechatmicui();
	}

	void addchatbubble(string role, string text) {
		text = text ?? "";
		if (text.Length == 0) return;
		chatItems.Add(new LlmChatBubble {
			Role = role,
			Text = text,
			IsUser = role == "user",
			IsError = role == "error",
			IsTool = role == "tool",
		});
		Dispatcher.BeginInvoke(new Action(() => {
			try { svchat.ScrollToEnd(); } catch { }
		}), DispatcherPriority.Background);
	}

	void setchatbusy(bool on) {
		chatBusy = on;
		bchatsend.Content = Loc.T(on ? "chat.stop" : "chat.send");
		bchatclear.IsEnabled = !on;
		echatllm.IsEnabled = !on;
		if (echatagent != null) echatagent.IsEnabled = !on;
		if (echatautotts != null) echatautotts.IsEnabled = !on;
		updatechatmicui();
	}

	void setchatstatus(string s) {
		if (lbchatstatus != null)
			lbchatstatus.Text = s ?? "";
	}

	void applychatlang() {
		if (tabchat == null) return;
		tabchat.Header = Loc.T("tab.chat");
		lbchatbrand.Text = Loc.T("tab.chat");
		lbchatllm.Text = Loc.T("chat.llm");
		if (echatagent != null) {
			echatagent.Content = Loc.T("chat.agent");
			echatagent.ToolTip = Loc.T("chat.agent.tip");
		}
		if (echatautotts != null) {
			echatautotts.Content = Loc.T("chat.autotts");
			echatautotts.ToolTip = Loc.T("chat.autotts.tip");
		}
		bchatclear.Content = Loc.T("chat.clear");
		lbchathint.Text = Loc.T("chat.hint");
		bchatsend.Content = Loc.T(chatBusy ? "chat.stop" : "chat.send");
		if (bchatmic != null) bchatmic.ToolTip = Loc.T("chat.mic.tip");
		if (bchatspeak != null) bchatspeak.ToolTip = Loc.T("chat.speak.tip");
		if (lbchatlog != null) lbchatlog.Text = Loc.T("chat.log");
		if (bchatlogclear != null) bchatlogclear.Content = Loc.T("chat.log.clear");
		updatechatmicui();
		if (!chatBusy && !chatSpeaking && !(asrVoiceChatCapture && asrVoice != null && asrVoice.IsActive))
			setchatstatus(Loc.T("chat.status.idle"));
		fillchatllm();
	}
}
