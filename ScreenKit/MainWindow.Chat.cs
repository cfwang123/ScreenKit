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
	bool chatBusy;
	bool chatUiLoading;

	void initchat() {
		icchat.ItemsSource = chatItems;
		bchatsend.Click += (_, _) => onchatsend();
		bchatclear.Click += (_, _) => onchatclear();
		echatllm.SelectionChanged += (_, _) => {
			if (chatUiLoading) return;
			savechatllm();
		};
		echatinput.PreviewKeyDown += onchatinputkey;
		fillchatllm();
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

	void onchatinputkey(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter) return;
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
		e.Handled = true;
		onchatsend();
	}

	void onchatclear() {
		if (chatBusy) {
			try { chatCts?.Cancel(); } catch { }
		}
		chatHist.Clear();
		chatItems.Clear();
		setchatstatus(Loc.T("chat.status.idle"));
	}

	void onchatsend() {
		if (chatBusy) {
			try { chatCts?.Cancel(); } catch { }
			return;
		}
		var text = (echatinput.Text ?? "").Trim();
		if (text.Length == 0) return;
		if (text.Length > 8000)
			text = text.Substring(0, 8000);

		var o = opt.Clone();
		var pick = currentchatllm();
		if (pick != null)
			o.ChatLlm = pick.DisplayName;
		if (!AsrLlmClient.IsChatReady(o)) {
			addchatbubble("error", Loc.T("chat.err.nollm"));
			setchatstatus(Loc.T("chat.err.nollm"));
			return;
		}

		echatinput.Clear();
		addchatbubble("user", text);
		chatHist.Add("user", text);
		var history = chatHist.ToMessages();
		var cts = new CancellationTokenSource();
		chatCts = cts;
		setchatbusy(true);
		setchatstatus(Loc.T("chat.status.thinking"));
		_ = Task.Run(() => {
			try {
				var reply = AsrLlmClient.Chat(o, history, cts.Token);
				Dispatcher.BeginInvoke(new Action(() => finishchat(reply, null, cts)));
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
		setchatstatus(Loc.T("chat.status.idle"));
	}

	void addchatbubble(string role, string text) {
		text = text ?? "";
		if (text.Length == 0) return;
		chatItems.Add(new LlmChatBubble {
			Role = role,
			Text = text,
			IsUser = role == "user",
			IsError = role == "error",
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
		bchatclear.Content = Loc.T("chat.clear");
		lbchathint.Text = Loc.T("chat.hint");
		bchatsend.Content = Loc.T(chatBusy ? "chat.stop" : "chat.send");
		if (!chatBusy)
			setchatstatus(Loc.T("chat.status.idle"));
		fillchatllm();
	}
}
