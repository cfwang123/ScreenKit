using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton = System.Windows.Controls.Button;

namespace ScreenKit;

public partial class SettingsWindow : Window {
	public OcrOptions Result { get; private set; }
	public bool Applied { get; private set; }

	readonly List<ModelPack> packs;
	readonly ObservableCollection<LlmEndpoint> llms = new();
	WpfTextBox captarget;
	WpfButton capbtn;
	string capprev = "";
	bool llmsync;

	public SettingsWindow(OcrOptions current) {
		InitializeComponent();
		Result = clone(current);
		packs = ModelCatalog.Scan();

		epack.ItemsSource = packs;
		epack.SelectionChanged += (_, _) => onpackchanged();

		edetlen.ValueChanged += (_, _) => lbdetlen.Text = ((int)edetlen.Value).ToString();
		edetth.ValueChanged += (_, _) => lbdetth.Text = edetth.Value.ToString("0.00");
		eboxth.ValueChanged += (_, _) => lbboxth.Text = eboxth.Value.ToString("0.00");
		evariant.SelectionChanged += (_, _) => updatehint();

		inithotkeyui();

		ellmlist.ItemsSource = llms;
		easrllm.ItemsSource = llms;
		ellmthink.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
			new TextChangedEventHandler(onllmthinktext));

		easrvoicesplit.Checked += (_, _) => syncvoicesplitui();
		easrvoicesplit.Unchecked += (_, _) => syncvoicesplitui();
		basrllmpromptreset.Click += (_, _) =>
			easrllmprompt.Text = OcrOptions.DefaultPolishPrompt();
		btrllmpromptreset.Click += (_, _) =>
			etrllmprompt.Text = OcrOptions.DefaultTranslatePrompt();
		bchatllmpromptreset.Click += (_, _) =>
			echatllmprompt.Text = OcrOptions.DefaultChatLlmPrompt();
		bsfunpair.Click += (_, _) => unpairselected();

		bcancel.Click += (_, _) => { Applied = false; Close(); };
		bok.Click += (_, _) => {
			stopcapture(restore: true);
			if (!saveui()) return;
			Applied = true;
			Close();
		};
		// 捕获热键时 Esc 取消捕获，不关窗
		WindowEsc.Attach(this, () => {
			if (captarget != null) {
				stopcapture(restore: true);
				return;
			}
			Applied = false;
			Close();
		});

		applylanglabels();
		loadui(Result);
	}

	void inithotkeyui() {
		bindhotkey(ehotkey, bhkcap, bhkclear);
		bindhotkey(ehotkeysnap, bhksnapcap, bhksnapclear);
		bindhotkey(ehotkeysnapocr, bhkocrcap, bhkocrclear);
		bindhotkey(ehotkeyboard, bhkboardcap, bhkboardclear);
		bindhotkey(ehotkeyvoice, bhkvoicecap, bhkvoiceclear);
		bindhotkey(ehotkeylive, bhklivecap, bhkliveclear);
		bindhotkey(ehotkeytr, bhktrcap, bhktrclear);
		bindhotkey(ehotkeysnapcopy, bhksnapcopycap, bhksnapcopyclear);
		bindhotkey(ehotkeydict, bhkdictcap, bhkdictclear);
	}

	void bindhotkey(WpfTextBox box, WpfButton cap, WpfButton clear) {
		if (box == null || cap == null || clear == null) return;
		clear.Click += (_, _) => {
			if (captarget == box) stopcapture(restore: false);
			box.Text = "";
		};
		cap.Click += (_, _) => startcapture(box, cap);
	}

	void startcapture(WpfTextBox box, WpfButton btn) {
		if (box == null || btn == null) return;
		if (captarget != null && captarget != box)
			stopcapture(restore: true);
		if (captarget == box) {
			// 再次点捕获 = 取消
			stopcapture(restore: true);
			return;
		}
		captarget = box;
		capbtn = btn;
		capprev = box.Text ?? "";
		box.Text = Loc.T("set.hotkey.press");
		box.IsReadOnly = true;
		btn.Content = Loc.T("cancel");
		box.PreviewKeyDown += oncapturekeydown;
		box.LostKeyboardFocus += oncapturelost;
		try { box.Focus(); } catch { }
	}

	void oncapturelost(object sender, KeyboardFocusChangedEventArgs e) {
		// 焦点离开捕获框：若仍在捕获中则取消（点到捕获按钮会再次 start，可先 stop）
		if (captarget == null) return;
		// 延迟一帧：允许点「取消」按钮
		Dispatcher.BeginInvoke(new Action(() => {
			if (captarget == null) return;
			// 仍在捕获且焦点不在目标框
			if (!captarget.IsKeyboardFocusWithin)
				stopcapture(restore: true);
		}), System.Windows.Threading.DispatcherPriority.Input);
	}

	void oncapturekeydown(object sender, KeyEventArgs e) {
		if (captarget == null) return;
		e.Handled = true;
		var key = e.Key == Key.System ? e.SystemKey : e.Key;
		if (key == Key.Escape) {
			stopcapture(restore: true);
			return;
		}
		// 仅修饰键：继续等主键
		if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
			or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
			or Key.System)
			return;
		// 单独 Backspace/Delete 清空
		if (key is Key.Back or Key.Delete) {
			captarget.Text = "";
			stopcapture(restore: false);
			return;
		}
		var text = GlobalHotkey.Format(Keyboard.Modifiers, key);
		if (string.IsNullOrEmpty(text)) return;
		captarget.Text = text;
		stopcapture(restore: false);
	}

	void stopcapture(bool restore) {
		if (captarget == null) return;
		var box = captarget;
		var btn = capbtn;
		box.PreviewKeyDown -= oncapturekeydown;
		box.LostKeyboardFocus -= oncapturelost;
		box.IsReadOnly = false;
		if (restore)
			box.Text = capprev ?? "";
		else if (box.Text == Loc.T("set.hotkey.press") || box.Text == "请按下快捷键…")
			box.Text = capprev ?? "";
		if (btn != null) btn.Content = Loc.T("set.hotkey.capture");
		captarget = null;
		capbtn = null;
		capprev = "";
	}

	void applylanglabels() {
		try {
			Title = Loc.T("set.title");
			tabsetgen.Header = Loc.T("set.tab.general");
			tabsetocr.Header = Loc.T("set.tab.ocr");
			tabsethk.Header = Loc.T("set.tab.hotkey");
			tabsetdict.Header = Loc.T("tab.dict");
			tabsetasr.Header = Loc.T("set.tab.asr");
			tabsetllm.Header = Loc.T("set.tab.llm");
			tabsettr.Header = Loc.T("set.tab.translate");
			tabsetsnap.Header = Loc.T("set.tab.snap");
			tabsethttp.Header = Loc.T("set.tab.http");
			lbsetlang.Text = Loc.T("set.lang");
			lbsetlanghint.Text = Loc.T("set.lang.hint");
			emintray.Content = Loc.T("set.tray");
			lbsetmod.Text = Loc.T("set.mod");
			lbsetmodhint.Text = Loc.T("set.mod.hint");
			emodocr.Content = Loc.T("tab.ocr");
			emodtts.Content = Loc.T("tab.tts");
			emodasr.Content = Loc.T("tab.asr");
			emodtranslate.Content = Loc.T("tab.translate");
			emodchat.Content = Loc.T("tab.chat");
			emodface.Content = Loc.T("tab.face");
			emoddict.Content = Loc.T("tab.dict");
			lbsettabsvisible.Text = Loc.T("set.tabs.visible");
			lbsettabsvisiblehint.Text = Loc.T("set.tabs.visible.hint");
			etabocrvisible.Content = Loc.T("tab.ocr");
			etabttsvisible.Content = Loc.T("tab.tts");
			etabasrvisible.Content = Loc.T("tab.asr");
			etabchatvisible.Content = Loc.T("tab.chat");
			etabtranslatevisible.Content = Loc.T("tab.translate");
			etabfacevisible.Content = Loc.T("tab.face");
			etabhttpvisible.Content = Loc.T("tab.http");
			etabsfvisible.Content = Loc.T("tab.sendfile");
			etabcastvisible.Content = Loc.T("tab.cast");
			etabdictvisible.Content = Loc.T("tab.dict");
			lbsetupdate.Text = Loc.T("set.update");
			lbsetupdatedaysunit.Text = Loc.T("set.update.unit");
			lbsetupdatehint.Text = Loc.T("set.update.hint");
			eupdatedays.ToolTip = Loc.T("set.update.hint");
			lbsetproxy.Text = Loc.T("set.proxy");
			eproxyen.Content = Loc.T("set.proxy.enable");
			lbsetproxyaddr.Text = Loc.T("set.proxy.addr");
			lbsetproxyhint.Text = Loc.T("set.proxy.hint");
			eproxyaddr.ToolTip = Loc.T("set.proxy.hint");
			lbsetlog.Text = Loc.T("set.log");
			ecapturelog.Content = Loc.T("set.log.enable");
			lbsetloghint.Text = Loc.T("set.log.hint");
			lbsetpack.Text = Loc.T("set.pack");
			lbsetvariant.Text = Loc.T("set.variant");
			lbsetdevice.Text = Loc.T("set.device");
			itdevgpu.Content = Loc.T("set.device.gpu");
			itdevigpu.Content = Loc.T("set.device.igpu");
			itdevcpu.Content = Loc.T("set.device.cpu");
			lbsetdevicehint.Text = Loc.T("set.device.hint");
			lbsetdetlen.Text = Loc.T("set.detlen");
			lbsetdetlenhint.Text = Loc.T("set.detlen.hint");
			lbsetdetth.Text = Loc.T("set.detth");
			lbsetboxth.Text = Loc.T("set.boxth");
			eusecls.Content = Loc.T("set.cls");
			eservicemode.Content = Loc.T("set.service");
			lbsetservicehint.Text = Loc.T("set.service.hint");
			lbsetonnxidle.Text = Loc.T("set.onnx.idle");
			lbsetonnxidlehint.Text = Loc.T("set.onnx.idle.hint");
			lbsetpdf.Text = Loc.T("set.pdf");
			epdftext.Content = Loc.T("set.pdf.text");
			lbsetpdfhint.Text = Loc.T("set.pdf.hint");
			lbsethotkey.Text = Loc.T("set.hotkey");
			lbsethotkeyhint.Text = Loc.T("set.hotkey.hint");
			lbhkhintmain.Text = Loc.T("set.hotkey.main");
			lbhkhintsnap.Text = Loc.T("set.hotkey.snap");
			lbhkhintocr.Text = Loc.T("set.hotkey.ocr");
			lbhkhintboard.Text = Loc.T("set.hotkey.board");
			lbhkhintvoice.Text = Loc.T("set.hotkey.voice");
			lbhkhintlive.Text = Loc.T("set.hotkey.live");
			lbhkhinttr.Text = Loc.T("set.hotkey.translate");
			lbhkhintsnapcopy.Text = Loc.T("set.hotkey.snapcopy");
			lbhkhintDict.Text = Loc.T("set.hotkey.dict");
			lbdicttts.Text = Loc.T("set.dict.tts");
			lbdictttshint.Text = Loc.T("set.dict.tts.hint");
			lbdictzh.Text = Loc.T("dict.filter.zh");
			lbdicten.Text = Loc.T("dict.filter.en");
			lbdictja.Text = Loc.T("dict.filter.ja");
			lbdictko.Text = Loc.T("dict.filter.ko");
			setdictrowlabels();
			lbsetasrmode.Text = Loc.T("set.asr.mode");
			lbsetasrmodehint.Text = Loc.T("set.asr.mode.hint");
			easrvoicestream.Content = Loc.T("set.asr.mode.stream");
			easrvoiceoffline.Content = Loc.T("set.asr.mode.offline");
			easrvoicepolish.Content = Loc.T("set.asr.voice.polish");
			easrvoicesplit.Content = Loc.T("set.asr.voice.split");
			easrvoicesplit.ToolTip = Loc.T("set.asr.voice.split.tip");
			lbsetasrvoicesplitsec.Text = Loc.T("set.asr.voice.split.sec");
			easrvoicesplitsec.ToolTip = Loc.T("set.asr.voice.split.sec.tip");
			lbsetasrlive.Text = Loc.T("set.asr.live");
			lbsetasrlivehint.Text = Loc.T("set.asr.live.hint");
			easrlivestream.Content = Loc.T("set.asr.live.stream");
			easrliveoffline.Content = Loc.T("set.asr.live.offline");
			easrlivepolish.Content = Loc.T("set.asr.live.polish");
			easrlivesplit.Content = Loc.T("set.asr.live.split");
			lbsetasrllm.Text = Loc.T("set.asr.llm");
			lbsetasrllmhint.Text = Loc.T("set.asr.llm.hint");
			lbsetasrllmpick.Text = Loc.T("set.asr.llm.pick");
			lbsetasrllmprompt.Text = Loc.T("set.asr.llm.prompt");
			basrllmpromptreset.Content = Loc.T("set.prompt.default");
			basrllmpromptreset.ToolTip = Loc.T("set.prompt.default.tip");
			lbsetchat.Text = Loc.T("set.chat");
			lbsetchathint.Text = Loc.T("set.chat.hint");
			lbsetchatllmprompt.Text = Loc.T("set.chat.llm.prompt");
			bchatllmpromptreset.Content = Loc.T("set.prompt.default");
			bchatllmpromptreset.ToolTip = Loc.T("set.prompt.default.tip");
			lbsetllmhint.Text = Loc.T("set.llm.hint");
			ellmlog.Content = Loc.T("set.llm.log");
			lbsetllmloghint.Text = Loc.T("set.llm.log.hint");
			bllmadd.Content = Loc.T("set.llm.add");
			bllmcopy.Content = Loc.T("set.llm.copy");
			bllmdel.Content = Loc.T("set.llm.del");
			lbsetllmname.Text = Loc.T("set.llm.name");
			lbsetllmurl.Text = Loc.T("set.llm.url");
			lbsetllmkey.Text = Loc.T("set.llm.key");
			lbsetllmmodel.Text = Loc.T("set.llm.model");
			lbsetllmmodelhint.Text = Loc.T("set.llm.model.hint");
			ellmmodel.ToolTip = Loc.T("set.llm.model.tip");
			lbsetllmthink.Text = Loc.T("set.llm.think");
			lbsetllmthinkhint.Text = Loc.T("set.llm.think.hint");
			itllmthinkoff.Content = Loc.T("set.llm.think.off");
			itllmthinklow.Content = Loc.T("set.llm.think.low");
			itllmthinkmedium.Content = Loc.T("set.llm.think.medium");
			itllmthinkhigh.Content = Loc.T("set.llm.think.high");
			itllmthinkmax.Content = Loc.T("set.llm.think.max");
			lbsettrhint.Text = Loc.T("set.tr.hint");
			lbsettrllmbatch.Text = Loc.T("set.tr.llm.batch");
			lbsettrllmbatchhint.Text = Loc.T("set.tr.llm.batch.hint");
			lbsettrllmprompt.Text = Loc.T("set.tr.llm.prompt");
			btrllmpromptreset.Content = Loc.T("set.prompt.default");
			btrllmpromptreset.ToolTip = Loc.T("set.prompt.default.tip");
			lbsettrllmprompthint.Text = Loc.T("set.tr.llm.prompt.hint");
			etrllmprompt.ToolTip = Loc.T("set.tr.llm.prompt.tip");
			lbsethttp.Text = Loc.T("set.http");
			lbsethttphint.Text = Loc.T("set.http.hint");
			ehttpen.Content = Loc.T("set.http.enable");
			lbsethttpmod.Text = Loc.T("set.http.mod");
			lbsethttpmodhint.Text = Loc.T("set.http.mod.hint");
			ehttpocr.Content = Loc.T("tab.ocr");
			ehttptts.Content = Loc.T("tab.tts");
			ehttpasr.Content = Loc.T("tab.asr");
			ehttptranslate.Content = Loc.T("tab.translate");
			ehttpchat.Content = Loc.T("tab.chat");
			ehttpface.Content = Loc.T("tab.face");
			ehttplan.Content = Loc.T("set.http.lan");
			lbsethttplanhint.Text = Loc.T("set.http.lan.hint");
			lbsethttpport.Text = Loc.T("set.http.port");
			lbsetsf.Text = Loc.T("set.sendfile");
			lbsetsfhint.Text = Loc.T("set.sendfile.hint");
			esfen.Content = Loc.T("set.sendfile.enable");
			lbsetcast.Text = Loc.T("set.cast");
			lbsetcasthint.Text = Loc.T("set.cast.hint");
			ecasten.Content = Loc.T("set.cast.enable");
			lbsetsfname.Text = Loc.T("set.sendfile.name");
			lbsetsfwebpass.Text = Loc.T("set.sendfile.webpass");
			lbsetsfwebpasshint.Text = Loc.T("set.sendfile.webpass.hint");
			lbsetsfphoto.Text = Loc.T("set.sendfile.photo");
			lbsetsfphotohint.Text = Loc.T("set.sendfile.photo.hint");
			lbsetsfphotofmt.Text = Loc.T("set.sendfile.photo.fmt");
			lbsetsfphotoq.Text = Loc.T("set.sendfile.photo.q");
			esfphotolimit.Content = Loc.T("set.sendfile.photo.limit");
			lbsetsfphotomax.Text = Loc.T("set.sendfile.photo.max");
			lbsetsfporthint.Text = Loc.T("set.sendfile.shared");
			lbsetsfudp.Text = Loc.T("set.sendfile.udp");
			lbsetsfdev.Text = Loc.T("set.sendfile.devices");
			bsfunpair.Content = Loc.T("set.sendfile.unpair");
			bok.Content = Loc.T("set.ok");
			bcancel.Content = Loc.T("set.cancel");
			bhkclear.Content = Loc.T("set.hotkey.clear");
			bhkcap.Content = Loc.T("set.hotkey.capture");
			bhksnapclear.Content = Loc.T("set.hotkey.clear");
			bhksnapcap.Content = Loc.T("set.hotkey.capture");
			bhkocrclear.Content = Loc.T("set.hotkey.clear");
			bhkocrcap.Content = Loc.T("set.hotkey.capture");
			bhkboardclear.Content = Loc.T("set.hotkey.clear");
			bhkboardcap.Content = Loc.T("set.hotkey.capture");
			bhkvoiceclear.Content = Loc.T("set.hotkey.clear");
			bhkvoicecap.Content = Loc.T("set.hotkey.capture");
			bhkliveclear.Content = Loc.T("set.hotkey.clear");
			bhklivecap.Content = Loc.T("set.hotkey.capture");
			bhktrclear.Content = Loc.T("set.hotkey.clear");
			bhktrcap.Content = Loc.T("set.hotkey.capture");
			bhksnapcopyclear.Content = Loc.T("set.hotkey.clear");
			bhksnapcopycap.Content = Loc.T("set.hotkey.capture");
			bhkclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhksnapclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhksnapcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkocrclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkocrcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkboardclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkboardcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkvoiceclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkvoicecap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkliveclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhklivecap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhktrclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhktrcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhksnapcopyclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhksnapcopycap.ToolTip = Loc.T("set.hotkey.capture.tip");
			lbsetsnap.Text = Loc.T("set.snap");
			lbsetsnaphint.Text = Loc.T("set.snap.hint");
			lbsetsnapfmt.Text = Loc.T("set.snap.fmt");
			lbsnapjpgq.Text = Loc.T("set.snap.jpgq");
			esnapjpgq.ToolTip = Loc.T("set.snap.jpgq.tip");
			esnapshorten.Content = Loc.T("set.snap.max");
			lbsetsnapshort.Text = Loc.T("set.snap.short");
			lbsetsnapmaxhint.Text = Loc.T("set.snap.max.hint");
			lbsetsnapcopy.Text = Loc.T("set.snap.copy");
			lbsetsnapcopyhint.Text = Loc.T("set.snap.copy.hint");
			esnapcopyimg.Content = Loc.T("set.snap.copy.img");
			esnapcopyfile.Content = Loc.T("set.snap.copy.file");
			esnapcopypath.Content = Loc.T("set.snap.copy.path");
			foreach (ComboBoxItem it in esnapkeep.Items) {
				var tag = (it.Tag as string) ?? "";
				it.Content = Loc.T("set.keep." + tag);
			}
			foreach (ComboBoxItem it in esnapfmt.Items) {
				var tag = ((it.Tag as string) ?? "").ToLowerInvariant();
				it.Content = tag == "jpg" ? Loc.T("set.fmt.jpg") : Loc.T("set.fmt.png");
			}
		}
		catch { }
	}

	void loadui(OcrOptions o) {
		// 界面语言
		foreach (ComboBoxItem it in euilang.Items) {
			var tag = (it.Tag as string) ?? "";
			var want = string.Equals(o.UiLang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
			if (string.Equals(tag, want, StringComparison.OrdinalIgnoreCase)) {
				euilang.SelectedItem = it;
				break;
			}
		}
		if (euilang.SelectedItem == null) euilang.SelectedIndex = 0;

		if (packs.Count == 0) {
			lbmodelhint.Text = Loc.IsEn
				? "No model packs found (ocrmodels/umi or ocrmodels/rapid-ch next to exe)"
				: "未发现模型包（程序目录 ocrmodels/umi 或 ocrmodels/rapid-ch）";
		}
		else {
			var pack = packs.FirstOrDefault(p =>
					string.Equals(p.Id, o.ModelPackId, StringComparison.OrdinalIgnoreCase))
				?? packs[0];
			epack.SelectedItem = pack;
			// onpackchanged 会填变体；再选中目标变体
			var want = o.ModelVariant;
			if (!string.IsNullOrWhiteSpace(want) && evariant.ItemsSource is IEnumerable<ModelVariant> vs) {
				var hit = vs.FirstOrDefault(v =>
					string.Equals(v.Title, want, StringComparison.OrdinalIgnoreCase));
				if (hit != null) evariant.SelectedItem = hit;
			}
		}

		foreach (ComboBoxItem it in edevice.Items) {
			if ((string)it.Tag == o.Device.ToString()) {
				edevice.SelectedItem = it;
				break;
			}
		}
		if (edevice.SelectedItem == null) edevice.SelectedIndex = 0;
		edetlen.Value = o.DetLimitSideLen;
		lbdetlen.Text = o.DetLimitSideLen.ToString();
		edetth.Value = o.DetThresh;
		lbdetth.Text = o.DetThresh.ToString("0.00");
		eboxth.Value = o.DetBoxThresh;
		lbboxth.Text = o.DetBoxThresh.ToString("0.00");
		eusecls.IsChecked = o.UseCls;
		// 空 = 禁用，原样显示，勿填默认
		ehotkey.Text = o.Hotkey ?? "";
		ehotkeysnap.Text = o.HotkeySnap ?? "";
		ehotkeysnapocr.Text = o.HotkeySnapOcr ?? "";
		ehotkeyboard.Text = o.HotkeyBoard ?? "";
		ehotkeyvoice.Text = o.HotkeyVoiceInput ?? "";
		ehotkeylive.Text = o.HotkeyLiveCaption ?? "";
		ehotkeytr.Text = o.HotkeyTranslate ?? "";
		ehotkeysnapcopy.Text = o.HotkeySnapCopy ?? "";
		ehotkeydict.Text = o.HotkeyDict ?? "";
		loaddicttts(o);
		var voiceOffline = string.Equals((o.AsrVoiceMode ?? "").Trim(), "offline", StringComparison.OrdinalIgnoreCase)
			|| string.Equals((o.AsrVoiceMode ?? "").Trim(), "离线", StringComparison.OrdinalIgnoreCase);
		easrvoiceoffline.IsChecked = voiceOffline;
		easrvoicestream.IsChecked = !voiceOffline;
		easrvoicepolish.IsChecked = o.AsrVoicePolish;
		easrvoicesplit.IsChecked = o.AsrVoiceSplit;
		easrvoicesplitsec.Text = Compat.Clamp(o.AsrVoiceSplitSec, 1, 30).ToString();
		syncvoicesplitui();
		var liveOffline = string.Equals((o.AsrLiveMode ?? "").Trim(), "offline", StringComparison.OrdinalIgnoreCase)
			|| string.Equals((o.AsrLiveMode ?? "").Trim(), "离线", StringComparison.OrdinalIgnoreCase);
		easrliveoffline.IsChecked = liveOffline;
		easrlivestream.IsChecked = !liveOffline;
		easrlivepolish.IsChecked = o.AsrLivePolish;
		easrlivesplit.IsChecked = o.AsrLiveSplit;
		loadllms(o);
		easrllmprompt.Text = string.IsNullOrWhiteSpace(o.AsrLlmPrompt)
			? OcrOptions.DefaultPolishPrompt() : o.AsrLlmPrompt;
		etrllmprompt.Text = string.IsNullOrWhiteSpace(o.TranslateLlmPrompt)
			? OcrOptions.DefaultTranslatePrompt() : o.TranslateLlmPrompt;
		etrllmbatch.Text = Compat.Clamp(o.TranslateLlmBatch <= 0 ? 8 : o.TranslateLlmBatch,
			1, OcrOptions.TranslateLlmBatchMax).ToString();
		echatllmprompt.Text = string.IsNullOrWhiteSpace(o.ChatLlmPrompt)
			? OcrOptions.DefaultChatLlmPrompt() : o.ChatLlmPrompt;
		emintray.IsChecked = o.MinimizeToTray;
		emodocr.IsChecked = o.ModOcr;
		emodtts.IsChecked = o.ModTts;
		emodasr.IsChecked = o.ModAsr;
		emodtranslate.IsChecked = o.ModTranslate;
		emodchat.IsChecked = o.ModChat;
		emodface.IsChecked = o.ModFace;
		emoddict.IsChecked = o.ModDict;
		etabocrvisible.IsChecked = o.TabOcrVisible;
		etabttsvisible.IsChecked = o.TabTtsVisible;
		etabasrvisible.IsChecked = o.TabAsrVisible;
		etabchatvisible.IsChecked = o.TabChatVisible;
		etabtranslatevisible.IsChecked = o.TabTranslateVisible;
		etabfacevisible.IsChecked = o.TabFaceVisible;
		etabhttpvisible.IsChecked = o.TabHttpVisible;
		etabsfvisible.IsChecked = o.TabSendFileVisible;
		etabcastvisible.IsChecked = o.TabCastVisible;
		etabdictvisible.IsChecked = o.TabDictVisible;
		eupdatedays.Text = Compat.Clamp(o.UpdateCheckDays, 0, 3650).ToString();
		eproxyen.IsChecked = o.HttpProxyEnabled;
		eproxyaddr.Text = string.IsNullOrWhiteSpace(o.HttpProxyAddr) ? "127.0.0.1:7897" : o.HttpProxyAddr;
		syncproxyui();
		eproxyen.Checked -= onproxyen;
		eproxyen.Unchecked -= onproxyen;
		eproxyen.Checked += onproxyen;
		eproxyen.Unchecked += onproxyen;
		// 三选一：路径 > 文件 > 图片
		var asPath = o.SnapCopyAsPath && !o.SnapCopyAsImage && !o.SnapCopyAsFile;
		var asFile = !asPath && o.SnapCopyAsFile && !o.SnapCopyAsImage;
		esnapcopyimg.IsChecked = !asPath && !asFile;
		esnapcopyfile.IsChecked = asFile;
		esnapcopypath.IsChecked = asPath;
		// 截图历史保留天数
		var keep = o.ScreenshotKeepDays < 0 ? 0 : o.ScreenshotKeepDays;
		ComboBoxItem keepHit = null;
		foreach (ComboBoxItem it in esnapkeep.Items) {
			var tag = (it.Tag as string) ?? "";
			if (int.TryParse(tag, out var d) && d == keep) {
				keepHit = it;
				break;
			}
		}
		if (keepHit != null)
			esnapkeep.SelectedItem = keepHit;
		else if (keep == 0) {
			// 选「不限」
			foreach (ComboBoxItem it in esnapkeep.Items) {
				if ((it.Tag as string) == "0") { esnapkeep.SelectedItem = it; break; }
			}
		}
		else {
			// 自定义天数：尽量贴近或默认 3
			esnapkeep.SelectedIndex = 1;
		}
		// 保存格式 / jpg 质量 / 较短边
		var fmt = (o.ScreenshotFormat ?? "png").Trim().ToLowerInvariant();
		var wantJpg = fmt is "jpg" or "jpeg";
		foreach (ComboBoxItem it in esnapfmt.Items) {
			var tag = (it.Tag as string) ?? "";
			if (string.Equals(tag, wantJpg ? "jpg" : "png", StringComparison.OrdinalIgnoreCase)) {
				esnapfmt.SelectedItem = it;
				break;
			}
		}
		if (esnapfmt.SelectedItem == null) esnapfmt.SelectedIndex = 0;
		var jq = o.ScreenshotJpgQuality <= 0 ? 92 : Compat.Clamp(o.ScreenshotJpgQuality, 1, 100);
		esnapjpgq.Text = jq.ToString();
		esnapshorten.IsChecked = o.ScreenshotShortEnabled;
		esnapshort.Text = (o.ScreenshotShortPx < 16 ? 1080 : o.ScreenshotShortPx).ToString();
		syncsnapfmtenabled();
		esnapfmt.SelectionChanged += (_, _) => syncsnapfmtenabled();
		ehttpen.IsChecked = o.HttpEnabled;
		ehttpocr.IsChecked = o.HttpOcr;
		ehttptts.IsChecked = o.HttpTts;
		ehttpasr.IsChecked = o.HttpAsr;
		ehttptranslate.IsChecked = o.HttpTranslate;
		ehttpchat.IsChecked = o.HttpChat;
		ehttpface.IsChecked = o.HttpFace;
		ehttplan.IsChecked = o.HttpLan;
		ehttpport.Text = o.HttpPort > 0 ? o.HttpPort.ToString() : "1224";
		eservicemode.IsChecked = o.ServiceMode;
		eonnxidle.Text = OnnxIdle.ClampMin(o.OnnxUnloadMin).ToString();
		esfen.IsChecked = o.SendFileEnabled;
		ecasten.IsChecked = o.CastRecvEnabled;
		esfname.Text = o.SendFileName ?? "";
		esfwebpass.Text = o.SendFileWebPass ?? "";
		var photoJpg = !string.Equals(o.PhotoFmt, "png", StringComparison.OrdinalIgnoreCase);
		foreach (ComboBoxItem it in esfphotofmt.Items) {
			var tag = (it.Tag as string) ?? "";
			if (string.Equals(tag, photoJpg ? "jpg" : "png", StringComparison.OrdinalIgnoreCase)) {
				esfphotofmt.SelectedItem = it;
				break;
			}
		}
		if (esfphotofmt.SelectedItem == null) esfphotofmt.SelectedIndex = 0;
		esfphotoq.Text = (o.PhotoJpgQuality <= 0 ? 60 : Compat.Clamp(o.PhotoJpgQuality, 1, 100)).ToString();
		esfphotolimit.IsChecked = o.PhotoLimitSize;
		esfphotomax.Text = (o.PhotoMaxPx <= 0 ? 2000 : Compat.Clamp(o.PhotoMaxPx, 64, 16000)).ToString();
		esfudp.Text = (o.SendFileUdpPort > 0 ? o.SendFileUdpPort : 17531).ToString();
		refreshsfdev();
		epdftext.IsChecked = o.PdfInvisibleText;
		ecapturelog.IsChecked = o.CaptureLog;
		ellmlog.IsChecked = o.LlmLog;
		updatehint();
	}

	void onpackchanged() {
		var pack = epack.SelectedItem as ModelPack;
		if (pack == null) {
			evariant.ItemsSource = null;
			return;
		}
		evariant.ItemsSource = pack.Variants;
		evariant.SelectedIndex = pack.Variants.Count > 0 ? 0 : -1;
		updatehint();
	}

	void updatehint() {
		var pack = epack.SelectedItem as ModelPack;
		var v = evariant.SelectedItem as ModelVariant;
		if (pack == null || v == null) {
			lbmodelhint.Text = "";
			return;
		}
		lbmodelhint.Text = $"det={v.DetFile}  ·  rec={v.RecFile}  ·  keys={v.KeysFile}";
	}

	bool saveui() {
		var pack = epack.SelectedItem as ModelPack;
		var variant = evariant.SelectedItem as ModelVariant;
		if (pack == null || variant == null) {
			tabset.SelectedItem = tabsetocr;
			MessageBox.Show(this, Loc.T("set.need.pack"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}

		Result.ModelPackId = pack.Id;
		Result.ModelVariant = variant.Title;
		Result.ModelsDir = pack.Dir;

		var langTag = (euilang.SelectedItem as ComboBoxItem)?.Tag as string ?? "zh";
		Result.UiLang = string.Equals(langTag, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";

		var tag = (edevice.SelectedItem as ComboBoxItem)?.Tag as string ?? "Cpu";
		Result.Device = tag switch {
			"Gpu" => OcrDevice.Gpu,
			"IntelGpu" => OcrDevice.IntelGpu,
			_ => OcrDevice.Cpu,
		};
		Result.DetLimitSideLen = (int)edetlen.Value;
		Result.DetThresh = (float)edetth.Value;
		Result.DetBoxThresh = (float)eboxth.Value;
		Result.UseCls = eusecls.IsChecked == true;
		// 热键留空 = 禁用；非空才校验格式
		if (!tryreadhotkey(ehotkey, Loc.T("set.hotkey.main"), out Result.Hotkey, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeysnap, Loc.T("set.hotkey.snap"), out Result.HotkeySnap, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeysnapocr, Loc.T("set.hotkey.ocr"), out Result.HotkeySnapOcr, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeyboard, Loc.T("set.hotkey.board"), out Result.HotkeyBoard, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeyvoice, Loc.T("set.hotkey.voice"), out Result.HotkeyVoiceInput, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeylive, Loc.T("set.hotkey.live"), out Result.HotkeyLiveCaption, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeytr, Loc.T("set.hotkey.translate"), out Result.HotkeyTranslate, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeysnapcopy, Loc.T("set.hotkey.snapcopy"), out Result.HotkeySnapCopy, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeydict, Loc.T("set.hotkey.dict"), out Result.HotkeyDict, tabsethk)) return false;
		savedicttts();
		Result.AsrVoiceMode = easrvoiceoffline.IsChecked == true ? "offline" : "stream";
		Result.AsrVoicePolish = easrvoicepolish.IsChecked == true;
		Result.AsrVoiceSplit = easrvoicesplit.IsChecked == true;
		if (!tryint(easrvoicesplitsec, Loc.T("set.asr.voice.split.sec"), 1, 30, out var splitSec, tabsetasr)) return false;
		Result.AsrVoiceSplitSec = splitSec;
		Result.AsrLiveMode = easrliveoffline.IsChecked == true ? "offline" : "stream";
		Result.AsrLivePolish = easrlivepolish.IsChecked == true;
		Result.AsrLiveSplit = easrlivesplit.IsChecked == true;
		flushllm();
		Result.LlmList = llms.Select(x => x.Clone())
			.Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.Url)
				|| !string.IsNullOrWhiteSpace(x.Key) || !string.IsNullOrWhiteSpace(x.Model))
			.ToList();
		var pick = easrllm.SelectedItem as LlmEndpoint;
		Result.AsrLlm = pick != null ? pick.DisplayName : "";
		var prompt = (easrllmprompt.Text ?? "").Trim();
		Result.AsrLlmPrompt = string.IsNullOrEmpty(prompt) ? OcrOptions.DefaultPolishPrompt() : prompt;
		var chatPrompt = (echatllmprompt.Text ?? "").Trim();
		Result.ChatLlmPrompt = string.IsNullOrEmpty(chatPrompt)
			? OcrOptions.DefaultChatLlmPrompt() : chatPrompt;
		var trPrompt = (etrllmprompt.Text ?? "").Trim();
		Result.TranslateLlmPrompt = string.IsNullOrEmpty(trPrompt)
			? OcrOptions.DefaultTranslatePrompt() : trPrompt;
		if (!tryint(etrllmbatch, Loc.T("set.tr.llm.batch"), 1, OcrOptions.TranslateLlmBatchMax,
			out var trBatch, tabsettr)) return false;
		Result.TranslateLlmBatch = trBatch;
		Result.MinimizeToTray = emintray.IsChecked == true;
		Result.ModOcr = emodocr.IsChecked == true;
		Result.ModTts = emodtts.IsChecked == true;
		Result.ModAsr = emodasr.IsChecked == true;
		Result.ModTranslate = emodtranslate.IsChecked == true;
		Result.ModChat = emodchat.IsChecked == true;
		Result.ModFace = emodface.IsChecked == true;
		Result.ModDict = emoddict.IsChecked == true;
		Result.TabOcrVisible = etabocrvisible.IsChecked == true;
		Result.TabTtsVisible = etabttsvisible.IsChecked == true;
		Result.TabAsrVisible = etabasrvisible.IsChecked == true;
		Result.TabChatVisible = etabchatvisible.IsChecked == true;
		Result.TabTranslateVisible = etabtranslatevisible.IsChecked == true;
		Result.TabFaceVisible = etabfacevisible.IsChecked == true;
		Result.TabHttpVisible = etabhttpvisible.IsChecked == true;
		Result.TabSendFileVisible = etabsfvisible.IsChecked == true;
		Result.TabCastVisible = etabcastvisible.IsChecked == true;
		Result.TabDictVisible = etabdictvisible.IsChecked == true;
		if (!tryint(eupdatedays, Loc.T("set.update"), 0, 3650, out var updDays, tabsetgen)) return false;
		Result.UpdateCheckDays = updDays;
		Result.HttpProxyEnabled = eproxyen.IsChecked == true;
		var proxyAddr = (eproxyaddr.Text ?? "").Trim();
		Result.HttpProxyAddr = string.IsNullOrWhiteSpace(proxyAddr) ? "127.0.0.1:7897" : proxyAddr;
		// 截图历史保留：Tag 天数，0=不限
		var keepTag = (esnapkeep.SelectedItem as ComboBoxItem)?.Tag as string ?? "3";
		if (!int.TryParse(keepTag, out var keepDays) || keepDays < 0)
			keepDays = 3;
		Result.ScreenshotKeepDays = keepDays > 3650 ? 3650 : keepDays;
		// 保存格式 / jpg 质量 / 较短边
		var fmtTag = (esnapfmt.SelectedItem as ComboBoxItem)?.Tag as string ?? "png";
		Result.ScreenshotFormat = string.Equals(fmtTag, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
		if (!int.TryParse((esnapjpgq.Text ?? "").Trim(), out var jpgQ) || jpgQ < 1 || jpgQ > 100) {
			tabset.SelectedItem = tabsetsnap;
			MessageBox.Show(this, Loc.T("set.jpgq.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		Result.ScreenshotJpgQuality = jpgQ;
		Result.ScreenshotShortEnabled = esnapshorten.IsChecked == true;
		if (!tryint(esnapshort, Loc.T("set.snap.short.name"), 16, 16384, out var sshort, tabsetsnap)) return false;
		Result.ScreenshotShortPx = sshort;
		// 三选一
		var asPath = esnapcopypath.IsChecked == true;
		var asFile = !asPath && esnapcopyfile.IsChecked == true;
		Result.SnapCopyAsImage = !asPath && !asFile;
		Result.SnapCopyAsFile = asFile;
		Result.SnapCopyAsPath = asPath;
		Result.HttpEnabled = ehttpen.IsChecked == true;
		Result.HttpOcr = ehttpocr.IsChecked == true;
		Result.HttpTts = ehttptts.IsChecked == true;
		Result.HttpAsr = ehttpasr.IsChecked == true;
		Result.HttpTranslate = ehttptranslate.IsChecked == true;
		Result.HttpChat = ehttpchat.IsChecked == true;
		Result.HttpFace = ehttpface.IsChecked == true;
		Result.HttpLan = ehttplan.IsChecked == true;
		if (!int.TryParse((ehttpport.Text ?? "").Trim(), out var port) || port < 1 || port > 65535) {
			tabset.SelectedItem = tabsethttp;
			MessageBox.Show(this, Loc.T("set.http.port.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		Result.HttpPort = port;
		Result.ServiceMode = eservicemode.IsChecked == true;
		if (!tryint(eonnxidle, Loc.T("set.onnx.idle.name"), 0, OnnxIdle.MaxMin, out var idleMin, tabsethttp))
			return false;
		Result.OnnxUnloadMin = idleMin;
		Result.SendFileEnabled = esfen.IsChecked == true;
		Result.CastRecvEnabled = ecasten.IsChecked == true;
		Result.SendFileName = (esfname.Text ?? "").Trim();
		Result.SendFileWebPass = (esfwebpass.Text ?? "").Trim();
		var photoTag = ((esfphotofmt.SelectedItem as ComboBoxItem)?.Tag as string) ?? "jpg";
		Result.PhotoFmt = string.Equals(photoTag, "png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
		if (!tryint(esfphotoq, Loc.T("set.sendfile.photo.q"), 1, 100, out var photoQ, tabsethttp)) return false;
		Result.PhotoJpgQuality = photoQ;
		Result.PhotoLimitSize = esfphotolimit.IsChecked == true;
		if (!tryint(esfphotomax, Loc.T("set.sendfile.photo.max"), 64, 16000, out var photoMax, tabsethttp))
			return false;
		Result.PhotoMaxPx = photoMax;
		Result.SendFilePort = Result.HttpPort <= 0 ? 1224 : Result.HttpPort;
		if (!int.TryParse((esfudp.Text ?? "").Trim(), out var sfUdp) || sfUdp < 1 || sfUdp > 65535) {
			tabset.SelectedItem = tabsethttp;
			MessageBox.Show(this, Loc.T("set.sendfile.port.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		Result.SendFileUdpPort = sfUdp;
		Result.PdfInvisibleText = epdftext.IsChecked == true;
		Result.CaptureLog = ecapturelog.IsChecked == true;
		Result.LlmLog = ellmlog.IsChecked == true;
		// PdfDpi 固定内部默认，不再由界面配置
		if (Result.PdfDpi <= 0) Result.PdfDpi = PdfOcr.DefaultDpi;
		return true;
	}

	static OcrOptions clone(OcrOptions o) => o?.Clone() ?? new();

	void refreshsfdev() {
		esfdev.Items.Clear();
		var list = Result.SendFileDevices;
		if (list == null || list.Count == 0) {
			esfdev.Items.Add(Loc.T("set.sendfile.none"));
			bsfunpair.IsEnabled = false;
			return;
		}
		bsfunpair.IsEnabled = true;
		foreach (var d in list) {
			if (d == null || string.IsNullOrWhiteSpace(d.Id)) continue;
			var label = string.IsNullOrWhiteSpace(d.Name) ? d.Id : $"{d.Name}  ({d.Id})";
			esfdev.Items.Add(new ListBoxItem { Content = label, Tag = d.Id });
		}
	}

	void unpairselected() {
		if (esfdev.SelectedItem is not ListBoxItem it) return;
		var id = it.Tag as string;
		if (string.IsNullOrWhiteSpace(id) || Result.SendFileDevices == null) return;
		Result.SendFileDevices.RemoveAll(d =>
			d != null && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
		refreshsfdev();
	}

	void loadllms(OcrOptions o) {
		llmsync = true;
		try {
			llms.Clear();
			if (o.LlmList != null) {
				foreach (var it in o.LlmList)
					if (it != null) llms.Add(it.Clone());
			}
			var want = (o.AsrLlm ?? "").Trim();
			LlmEndpoint pick = null;
			if (want.Length > 0)
				pick = llms.FirstOrDefault(x =>
						string.Equals(x.DisplayName, want, StringComparison.OrdinalIgnoreCase))
					?? llms.FirstOrDefault(x =>
						string.Equals(x.Model, want, StringComparison.OrdinalIgnoreCase));
			if (pick == null && llms.Count > 0) pick = llms[0];
			if (pick != null) {
				ellmlist.SelectedItem = pick;
				easrllm.SelectedItem = pick;
			}
			else {
				ellmlist.SelectedItem = null;
				easrllm.SelectedItem = null;
			}
		}
		finally { llmsync = false; }
		fillllmeditor();
	}

	void fillllmeditor() {
		llmsync = true;
		try {
			var it = ellmlist.SelectedItem as LlmEndpoint;
			var on = it != null;
			ellmname.IsEnabled = on;
			ellmurl.IsEnabled = on;
			ellmkey.IsEnabled = on;
			ellmmodel.IsEnabled = on;
			ellmthink.IsEnabled = on;
			bllmdel.IsEnabled = on;
			bllmcopy.IsEnabled = on;
			ellmname.Text = it?.Name ?? "";
			ellmurl.Text = it?.Url ?? "";
			ellmkey.Password = it?.Key ?? "";
			ellmmodel.Text = it?.Model ?? "";
			selectthink(it?.Think);
		}
		finally { llmsync = false; }
	}

	void selectthink(string think) {
		var want = LlmEndpoint.NormThink(think);
		foreach (ComboBoxItem x in ellmthink.Items) {
			if ((x.Tag as string) == want) {
				ellmthink.SelectedItem = x;
				ellmthink.Text = x.Content as string ?? want;
				return;
			}
		}
		ellmthink.SelectedIndex = -1;
		ellmthink.Text = want;
	}

	string thinktag() {
		var text = (ellmthink.Text ?? "").Trim();
		foreach (ComboBoxItem x in ellmthink.Items) {
			var content = x.Content as string ?? "";
			var tag = x.Tag as string ?? "";
			if (text.Equals(content, StringComparison.OrdinalIgnoreCase)
				|| text.Equals(tag, StringComparison.OrdinalIgnoreCase))
				return LlmEndpoint.NormThink(tag.Length > 0 ? tag : content);
		}
		return LlmEndpoint.NormThink(text);
	}

	void flushllm() {
		var it = ellmlist.SelectedItem as LlmEndpoint;
		if (it == null || llmsync) return;
		it.Name = (ellmname.Text ?? "").Trim();
		it.Url = (ellmurl.Text ?? "").Trim();
		it.Key = ellmkey.Password ?? "";
		it.Model = (ellmmodel.Text ?? "").Trim();
		it.Think = thinktag();
	}

	void onllmthink(object sender, SelectionChangedEventArgs e) {
		if (llmsync) return;
		flushllm();
	}

	void onllmthinktext(object sender, TextChangedEventArgs e) {
		if (llmsync) return;
		flushllm();
	}

	void refreshllmdisp() {
		var listSel = ellmlist.SelectedItem;
		var asrSel = easrllm.SelectedItem;
		ellmlist.Items.Refresh();
		easrllm.Items.Refresh();
		if (listSel != null) ellmlist.SelectedItem = listSel;
		if (asrSel != null) easrllm.SelectedItem = asrSel;
	}

	void onllmselect(object sender, SelectionChangedEventArgs e) {
		if (llmsync) return;
		fillllmeditor();
	}

	void onllmadd(object sender, RoutedEventArgs e) {
		flushllm();
		var it = new LlmEndpoint();
		llms.Add(it);
		ellmlist.SelectedItem = it;
		if (easrllm.SelectedItem == null) easrllm.SelectedItem = it;
		fillllmeditor();
		try { ellmmodel.Focus(); } catch { }
	}

	void onllmcopy(object sender, RoutedEventArgs e) {
		flushllm();
		var src = ellmlist.SelectedItem as LlmEndpoint;
		if (src == null) return;
		var it = src.Clone();
		var baseName = it.DisplayName;
		if (baseName.Length == 0) baseName = Loc.IsEn ? "Untitled" : "未命名";
		it.Name = nextllmname(baseName);
		var i = llms.IndexOf(src);
		if (i >= 0 && i + 1 <= llms.Count) llms.Insert(i + 1, it);
		else llms.Add(it);
		ellmlist.SelectedItem = it;
		fillllmeditor();
		try { ellmname.Focus(); } catch { }
	}

	string nextllmname(string baseName) {
		var n = 2;
		var name = $"{baseName} 副本";
		if (Loc.IsEn) name = $"{baseName} copy";
		while (llms.Any(x => string.Equals(x.DisplayName, name, StringComparison.OrdinalIgnoreCase))) {
			name = Loc.IsEn ? $"{baseName} copy {n}" : $"{baseName} 副本{n}";
			n++;
		}
		return name;
	}

	void onllmdel(object sender, RoutedEventArgs e) {
		var it = ellmlist.SelectedItem as LlmEndpoint;
		if (it == null) return;
		var i = llms.IndexOf(it);
		var asrWas = easrllm.SelectedItem as LlmEndpoint;
		llms.Remove(it);
		if (llms.Count > 0)
			ellmlist.SelectedItem = llms[Math.Min(Math.Max(i, 0), llms.Count - 1)];
		if (asrWas == it)
			easrllm.SelectedItem = llms.Count > 0 ? llms[0] : null;
		fillllmeditor();
	}

	void onllmfield(object sender, TextChangedEventArgs e) {
		if (llmsync) return;
		flushllm();
		if (sender == ellmname) refreshllmdisp();
	}

	void onllmkey(object sender, RoutedEventArgs e) {
		if (llmsync) return;
		flushllm();
	}

	void onllmmodel(object sender, TextChangedEventArgs e) {
		if (llmsync) return;
		var it = ellmlist.SelectedItem as LlmEndpoint;
		if (it == null) return;
		var newM = (ellmmodel.Text ?? "").Trim();
		var name = (ellmname.Text ?? "").Trim();
		if (name.Length == 0 || string.Equals(name, it.Model, StringComparison.Ordinal)) {
			llmsync = true;
			try { ellmname.Text = newM; }
			finally { llmsync = false; }
			it.Name = newM;
		}
		it.Model = newM;
		it.Url = (ellmurl.Text ?? "").Trim();
		it.Key = ellmkey.Password ?? "";
		refreshllmdisp();
	}

	void onproxyen(object sender, RoutedEventArgs e) => syncproxyui();

	void syncproxyui() {
		var on = eproxyen.IsChecked == true;
		eproxyaddr.IsEnabled = on;
		lbsetproxyaddr.Opacity = on ? 1 : 0.45;
		eproxyaddr.Opacity = on ? 1 : 0.55;
	}

	bool dictvoicehook;
	bool dictloading;
	DictTtsLang holdzh;
	DictTtsLang holden;
	DictTtsLang holdja;
	DictTtsLang holdko;
	readonly Dictionary<ComboBox, string> dictlasteng = new();

	void hookdictvoice() {
		if (dictvoicehook) return;
		dictvoicehook = true;
		hookone(edictzheng, edictzhvoice, edictzhrate, pdictzhvoice, () => holdzh);
		hookone(edicteneng, edictenvoice, edictenrate, pdictenvoice, () => holden);
		hookone(edictjaeng, edictjavoice, edictjarate, pdictjavoice, () => holdja);
		hookone(edictkoeng, edictkovoice, edictkorate, pdictkovoice, () => holdko);
		return;
		void hookone(ComboBox eng, ComboBox voice, ComboBox rate, DockPanel voiceRow, Func<DictTtsLang> hold) {
			eng.SelectionChanged += (_, _) => ondicteng(eng, voice, rate, voiceRow, hold());
			voice.SelectionChanged += (_, _) => {
				if (dictloading) return;
				writevoice(hold(), engtag(eng), tagof(voice));
			};
			rate.SelectionChanged += (_, _) => {
				if (dictloading) return;
				hold().Rate = rateof(rate);
			};
		}
	}

	void ondicteng(ComboBox eng, ComboBox voice, ComboBox rate, DockPanel voiceRow, DictTtsLang hold) {
		if (dictloading || hold == null) return;
		var next = engtag(eng);
		if (dictlasteng.TryGetValue(eng, out var prev) && prev != next)
			writevoice(hold, prev, tagof(voice));
		dictlasteng[eng] = next;
		hold.Engine = next;
		filldictvoice(voice, voiceRow, next, langofeng(eng), hold);
	}

	static string langofeng(ComboBox eng) {
		if (ReferenceEquals(eng, null)) return "en";
		var name = eng.Name ?? "";
		if (name.Contains("zh")) return "zh";
		if (name.Contains("ja")) return "ja";
		if (name.Contains("ko")) return "ko";
		return "en";
	}

	void setdictrowlabels() {
		var eng = Loc.T("set.dict.engine");
		var voice = Loc.T("set.dict.voice");
		var rate = Loc.T("set.dict.rate");
		lbdictzheng.Text = eng;
		lbdicteneng.Text = eng;
		lbdictjaeng.Text = eng;
		lbdictkoeng.Text = eng;
		lbdictzhvoice.Text = voice;
		lbdictenvoice.Text = voice;
		lbdictjavoice.Text = voice;
		lbdictkovoice.Text = voice;
		lbdictzhrate.Text = rate;
		lbdictenrate.Text = rate;
		lbdictjarate.Text = rate;
		lbdictkorate.Text = rate;
	}

	void loaddicttts(OcrOptions o) {
		hookdictvoice();
		holdzh = DictTts.For(o, "zh");
		holden = DictTts.For(o, "en");
		holdja = DictTts.For(o, "ja");
		holdko = DictTts.For(o, "ko");
		filldictlang(edictzheng, edictzhvoice, edictzhrate, pdictzhvoice, "zh", holdzh);
		filldictlang(edicteneng, edictenvoice, edictenrate, pdictenvoice, "en", holden);
		filldictlang(edictjaeng, edictjavoice, edictjarate, pdictjavoice, "ja", holdja);
		filldictlang(edictkoeng, edictkovoice, edictkorate, pdictkovoice, "ko", holdko);
	}

	void savedicttts() {
		flushdict(edictzheng, edictzhvoice, edictzhrate, holdzh);
		flushdict(edicteneng, edictenvoice, edictenrate, holden);
		flushdict(edictjaeng, edictjavoice, edictjarate, holdja);
		flushdict(edictkoeng, edictkovoice, edictkorate, holdko);
		Result.DictTtsZh = holdzh.Clone();
		Result.DictTtsEn = holden.Clone();
		Result.DictTtsJa = holdja.Clone();
		Result.DictTtsKo = holdko.Clone();
	}

	void flushdict(ComboBox eng, ComboBox voice, ComboBox rate, DictTtsLang hold) {
		if (hold == null) return;
		var kind = engtag(eng);
		hold.Engine = kind;
		hold.Rate = rateof(rate);
		writevoice(hold, kind, tagof(voice));
	}

	void filldictlang(ComboBox eng, ComboBox voice, ComboBox rate, DockPanel voiceRow, string lang, DictTtsLang pref) {
		if (pref == null) pref = DictTtsLang.Make(DictTts.DefaultEdge(lang));
		dictloading = true;
		try {
			filltag(eng, new[] {
				(Loc.T("dict.tts.auto"), DictTts.AUTO),
				(Loc.T("dict.tts.onnx"), DictTts.ONNX),
				(Loc.T("dict.tts.sapi"), DictTts.SAPI),
				(Loc.T("dict.tts.winrt"), DictTts.WINRT),
				(Loc.T("dict.tts.edge"), DictTts.EDGE),
			}, DictTts.NormEngine(pref.Engine));
			var rates = new List<(string, string)>();
			foreach (var step in DictTts.RateSteps) {
				var tag = step.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
				rates.Add((tag + "×", tag));
			}
			var wantRate = DictTts.NormRate(pref.Rate).ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
			var haveRate = false;
			foreach (var it in rates) {
				if (string.Equals(it.Item2, wantRate, StringComparison.Ordinal)) haveRate = true;
			}
			if (!haveRate) rates.Add((wantRate + "×", wantRate));
			filltag(rate, rates, wantRate);
			dictlasteng[eng] = engtag(eng);
			filldictvoice(voice, voiceRow, engtag(eng), lang, pref);
		}
		finally { dictloading = false; }
	}

	void filldictvoice(ComboBox voice, DockPanel voiceRow, string engine, string lang, DictTtsLang pref) {
		var kind = DictTts.NormEngine(engine);
		voiceRow.Visibility = kind == DictTts.AUTO ? Visibility.Collapsed : Visibility.Visible;
		if (kind == DictTts.AUTO) return;
		var items = DictTts.Voices(kind, lang);
		var want = voiceid(pref, kind, lang);
		var have = false;
		foreach (var it in items) {
			if (string.Equals(it.Id, want, StringComparison.OrdinalIgnoreCase)) have = true;
		}
		if (want.Length > 0 && !have) items.Add((want, want));
		var was = dictloading;
		dictloading = true;
		try { filltag(voice, items, want); }
		finally { dictloading = was; }
	}

	static string voiceid(DictTtsLang pref, string engine, string lang) {
		if (pref == null) return "";
		if (engine == DictTts.ONNX) return pref.Onnx ?? "";
		if (engine == DictTts.SAPI) return pref.Sapi ?? "";
		if (engine == DictTts.WINRT) return pref.WinRt ?? "";
		if (engine == DictTts.EDGE) return DictTts.EdgeName(lang, pref.Edge);
		return "";
	}

	static void writevoice(DictTtsLang hold, string engine, string id) {
		if (hold == null) return;
		id = id ?? "";
		if (engine == DictTts.ONNX) hold.Onnx = id;
		else if (engine == DictTts.SAPI) hold.Sapi = id;
		else if (engine == DictTts.WINRT) hold.WinRt = id;
		else if (engine == DictTts.EDGE && id.Length > 0) hold.Edge = id;
	}

	static string engtag(ComboBox eng) =>
		DictTts.NormEngine((eng?.SelectedItem as ComboBoxItem)?.Tag as string);

	static string tagof(ComboBox box) =>
		(box?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

	static double rateof(ComboBox rate) {
		var tag = tagof(rate);
		if (double.TryParse(tag, System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var n))
			return DictTts.NormRate(n);
		return 1;
	}

	static void filltag(ComboBox box, IEnumerable<(string Label, string Tag)> items, string selected) {
		box.Items.Clear();
		ComboBoxItem hit = null;
		foreach (var it in items) {
			var row = new ComboBoxItem { Content = it.Label, Tag = it.Tag ?? "" };
			box.Items.Add(row);
			if (hit == null && string.Equals((string)row.Tag, selected ?? "", StringComparison.OrdinalIgnoreCase))
				hit = row;
		}
		if (hit != null) box.SelectedItem = hit;
		else if (box.Items.Count > 0) box.SelectedIndex = 0;
	}

	void syncvoicesplitui() {
		var on = easrvoicesplit.IsChecked == true;
		easrvoicesplitsec.IsEnabled = on;
		lbsetasrvoicesplitsec.Opacity = on ? 1 : 0.45;
		easrvoicesplitsec.Opacity = on ? 1 : 0.55;
	}

	/// <summary>JPG 质量输入：仅 jpg 格式可编辑。</summary>
	void syncsnapfmtenabled() {
		var jpg = string.Equals(
			(esnapfmt.SelectedItem as ComboBoxItem)?.Tag as string, "jpg",
			StringComparison.OrdinalIgnoreCase);
		esnapjpgq.IsEnabled = jpg;
		lbsnapjpgq.Opacity = jpg ? 1 : 0.45;
		esnapjpgq.Opacity = jpg ? 1 : 0.55;
	}

	/// <summary>读整数输入框，失败弹窗；可选切到对应 Tab。</summary>
	bool tryint(WpfTextBox box, string name, int min, int max, out int value, TabItem tab = null) {
		value = 0;
		if (!int.TryParse((box?.Text ?? "").Trim(), out var n) || n < min || n > max) {
			if (tab != null) tabset.SelectedItem = tab;
			MessageBox.Show(this, Loc.T("set.int.range", name, min, max), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		value = n;
		return true;
	}

	/// <summary>读热键：留空允许（禁用）；非空须能解析。可选切到对应 Tab。</summary>
	bool tryreadhotkey(System.Windows.Controls.TextBox box, string name, out string value, TabItem tab = null) {
		value = (box?.Text ?? "").Trim();
		if (string.IsNullOrEmpty(value)) return true;
		if (GlobalHotkey.tryparse(value, out _, out _)) return true;
		if (tab != null) tabset.SelectedItem = tab;
		MessageBox.Show(this, Loc.T("set.hotkey.parse", name, value), Loc.T("settings"),
			MessageBoxButton.OK, MessageBoxImage.Warning);
		return false;
	}
}
