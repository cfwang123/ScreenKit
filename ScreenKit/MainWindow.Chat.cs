using System.Collections.ObjectModel;
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

	void initchat() {
		chatPlayer = new TtsPlayer();
		icchat.ItemsSource = chatItems;
		bchatsend.Click += (_, _) => onchatsend();
		bchatclear.Click += (_, _) => onchatclear();
		if (bchatmic != null) bchatmic.Click += (_, _) => onchatmic();
		if (bchatspeak != null) bchatspeak.Click += (_, _) => onchatspeak();
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
		setchatstatus(Loc.T("chat.status.idle"));
	}

	void onchatmic() {
		if (chatBusy) {
			setchatstatus(Loc.T("chat.status.thinking"));
			return;
		}
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
		var cts = new CancellationTokenSource();
		chatCts = cts;
		setchatbusy(true);
		setchatstatus(Loc.T("chat.status.thinking"));
		_ = Task.Run(() => {
			try {
				if (useAgent) {
					var result = LlmAgent.Run(o, history,
						st => Dispatcher.BeginInvoke(new Action(() => {
							if (cts == chatCts) setchatstatus(st);
						})),
						note => Dispatcher.BeginInvoke(new Action(() => {
							if (cts == chatCts) addchatbubble("tool", note);
						})),
						cts.Token);
					Dispatcher.BeginInvoke(new Action(() => finishchat(result?.Reply, null, cts)));
				}
				else {
					var reply = AsrLlmClient.Chat(o, history, cts.Token);
					Dispatcher.BeginInvoke(new Action(() => finishchat(reply, null, cts)));
				}
			}
			catch (OperationCanceledException) {
				Dispatcher.BeginInvoke(new Action(() => finishchat(null, "cancel", cts)));
			}
			catch (Exception ex) {
				Dispatcher.BeginInvoke(new Action(() => finishchat(null, ex.Message, cts)));
			}
		});
	}

	void finishchat(string reply, string err, CancellationTokenSource cts) {
		if (cts != chatCts) return;
		setchatbusy(false);
		chatCts = null;
		if (err == "cancel") {
			setchatstatus(Loc.T("chat.status.idle"));
			return;
		}
		if (!string.IsNullOrEmpty(err)) {
			addchatbubble("error", err);
			setchatstatus(err);
			return;
		}
		reply = (reply ?? "").Trim();
		if (reply.Length == 0) {
			addchatbubble("error", Loc.T("chat.err.empty"));
			setchatstatus(Loc.T("chat.err.empty"));
			return;
		}
		addchatbubble("assistant", reply);
		chatHist.Add("assistant", reply);
		chatLastAssistant = reply;
		setchatstatus(Loc.T("chat.status.idle"));
		if (echatautotts?.IsChecked == true)
			_ = chatspeakasync(reply);
	}

	async Task chatspeakasync(string text) {
		text = (text ?? "").Trim();
		if (text.Length == 0) return;
		stopchattts();
		chatTtsCts = new CancellationTokenSource();
		var ct = chatTtsCts.Token;
		chatSpeaking = true;
		updatechatmicui();
		try {
			await speaktextasync(text, chatPlayer, ct, s => {
				if (!ct.IsCancellationRequested) setchatstatus(s);
			}).ConfigureAwait(true);
			if (!ct.IsCancellationRequested)
				setchatstatus(Loc.T("chat.status.idle"));
		}
		catch (OperationCanceledException) {
			setchatstatus(Loc.T("chat.status.idle"));
		}
		catch (Exception ex) {
			CaptureLog.Ex("chatspeak", ex);
			setchatstatus(ex.Message);
		}
		finally {
			chatSpeaking = false;
			updatechatmicui();
		}
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
		updatechatmicui();
		if (!chatBusy && !chatSpeaking && !(asrVoiceChatCapture && asrVoice != null && asrVoice.IsActive))
			setchatstatus(Loc.T("chat.status.idle"));
		fillchatllm();
	}
}
