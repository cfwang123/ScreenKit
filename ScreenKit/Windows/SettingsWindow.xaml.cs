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
		packs.Insert(0, WinOcr.Pack());

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
		bsnapdir.Click += (_, _) => browsesnapdir();
		bsetchromebrowse.Click += (_, _) => browsechrome();
		bsetchromedetect.Click += (_, _) => detectchrome();
		if (System.IO.Directory.Exists(@"D:\")) {
			esnapdir.Items.Add(ImageUtil.ScreenshotDirShortText);
			esnapdir.Items.Add(ImageUtil.ScreenshotDirDefaultText);
		}
		else {
			esnapdir.Items.Add(ImageUtil.ScreenshotDirDefaultText);
			esnapdir.Items.Add(ImageUtil.ScreenshotDirShortText);
		}
		esnapname.Items.Add(ImageUtil.ScreenshotNameDefault);
		esnapname.Items.Add("shot_yyyyMMdd_HHmmss_fff");
		esnapname.Items.Add("shot_yyyyMMdd_HHmmss_rrr");
		esnapname.Items.Add("####");
		esnapname.Items.Add("yyyyMMdd_###");

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
		bindhotkey(ehotkeyscreensnap, bhkscreensnapcap, bhkscreensnapclear);
		bindhotkey(ehotkeyvoice, bhkvoicecap, bhkvoiceclear);
		bindhotkey(ehotkeylive, bhklivecap, bhkliveclear);
		bindhotkey(ehotkeytr, bhktrcap, bhktrclear);
		bindhotkey(ehotkeysnapcopy, bhksnapcopycap, bhksnapcopyclear);
		bindhotkey(ehotkeydict, bhkdictcap, bhkdictclear);
		bindhotkey(ehotkeydictsel, bhkdictselcap, bhkdictselclear);
		bindhotkey(ehotkeyimecycle, bhkimecyclecap, bhkimecycleclear);
		bindhotkey(ehotkeyimewubi, bhkimewubicap, bhkimewubiclear);
		bindhotkey(ehotkeyimeja, bhkimejacap, bhkimejaclear);
		bindhotkey(ehotkeyimeko, bhkimekocap, bhkimekoclear);
		bindhotkey(ehotkeyimeoff, bhkimeoffcap, bhkimeoffclear);
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
			lbsetchrome.Text = Loc.T("tts.chrome.path");
			bsetchromebrowse.Content = Loc.T("tts.chrome.browse");
			bsetchromedetect.Content = Loc.T("tts.chrome.detect");
			esetchromepath.ToolTip = Loc.T("tts.chrome.tip");
			lbsetchromeidle.Text = Loc.T("tts.chrome.idle");
			esetchromeidle.ToolTip = Loc.T("tts.chrome.idle.tip");
			lbsetchromehint.Text = Loc.T("set.chrome.hint");
			emintray.Content = Loc.T("set.tray");
			lbsettabsvisible.Text = Loc.T("set.tabs.visible");
			lbsettabsvisiblehint.Text = Loc.T("set.tabs.visible.hint");
			etabocrvisible.Content = Loc.T("tab.ocr");
			etabttsvisible.Content = Loc.T("tab.tts");
			etabasrvisible.Content = Loc.T("tab.asr");
			etabchatvisible.Content = Loc.T("tab.chat");
			etabllmlogvisible.Content = Loc.T("tab.llmlog");
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
			rsetproxysys.Content = Loc.T("set.proxy.system");
			rsetproxyman.Content = Loc.T("set.proxy.manual");
			rsetproxyoff.Content = Loc.T("set.proxy.off");
			lbsetproxyaddr.Text = Loc.T("set.proxy.addr");
			lbsetproxyhint.Text = Loc.T("set.proxy.hint");
			eproxyaddr.ToolTip = Loc.T("set.proxy.hint");
			lbsetlog.Text = Loc.T("set.log");
			ecapturelog.Content = Loc.T("set.log.enable");
			lbsetloghint.Text = Loc.T("set.log.hint");
			lbsetpack.Text = Loc.T("set.pack");
			lbsetvariant.Text = Loc.T("set.variant");
			lbsetwinlang.Text = Loc.T("ocr.win.lang");
			ewinlang.ToolTip = Loc.T("ocr.win.lang.tip");
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
			lbhkhintscreensnap.Text = Loc.T("set.hotkey.screensnap.hint");
			lbhkhintvoice.Text = Loc.T("set.hotkey.voice");
			lbhkhintlive.Text = Loc.T("set.hotkey.live");
			lbhkhinttr.Text = Loc.T("set.hotkey.translate");
			lbhkhintsnapcopy.Text = Loc.T("set.hotkey.snapcopy");
			lbhkhintDict.Text = Loc.T("set.hotkey.dict");
			lbhkhintImeCycle.Text = Loc.T("set.hotkey.ime.cycle");
			lbhkhintImeWubi.Text = Loc.T("set.hotkey.ime.wubi");
			lbhkhintImeJa.Text = Loc.T("set.hotkey.ime.ja");
			lbhkhintImeKo.Text = Loc.T("set.hotkey.ime.ko");
			lbhkhintImeOff.Text = Loc.T("set.hotkey.ime.off");
			if (cbimegvim != null) cbimegvim.Content = Loc.T("set.ime.gvim");
			if (lbimegvimhint != null) lbimegvimhint.Text = Loc.T("set.ime.gvim.hint");
			if (cbimerare != null) cbimerare.Content = Loc.T("set.ime.rare");
			if (lbimerarehint != null) lbimerarehint.Text = Loc.T("set.ime.rare.hint");
			lbdictlimit.Text = Loc.T("set.dict.limit");
			lbdictlimithint.Text = Loc.T("set.dict.limit.hint");
			lbdicthold.Text = Loc.T("set.dict.dbhold");
			lbdictholdhint.Text = Loc.T("set.dict.dbhold.hint");
			cbdictselbar.Content = Loc.T("dict.selbar");
			lbdictselbarhint.Text = Loc.T("set.dict.selbar.hint");
			lbhkhintDictSel.Text = Loc.T("set.hotkey.dictsel");
			lbdicttts.Text = Loc.T("set.dict.tts");
			lbdictttshint.Text = Loc.T("set.dict.tts.hint");
			lbdictlang.Text = Loc.T("set.dict.lang");
			lbdicteng.Text = Loc.T("set.dict.engine");
			lbdictvoice.Text = Loc.T("set.dict.voice");
			lbdictrate.Text = Loc.T("set.dict.rate");
			bdictttsadd.Content = Loc.T("set.dict.tts.add");
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
			lbsettrauto.Text = Loc.T("set.tr.auto");
			lbsettrnative.Text = Loc.T("set.tr.auto.native");
			lbsettrlearn.Text = Loc.T("set.tr.auto.learn");
			lbsettrnativehint.Text = Loc.T("set.tr.auto.hint");
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
			ehttprtsp.Content = Loc.T("set.http.rtsp");
			ehttplan.Content = Loc.T("set.http.lan");
			lbsethttplanhint.Text = Loc.T("set.http.lan.hint");
			lbsethttpport.Text = Loc.T("set.http.port");
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
			bhkscreensnapclear.Content = Loc.T("set.hotkey.clear");
			bhkscreensnapcap.Content = Loc.T("set.hotkey.capture");
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
			bhkscreensnapclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkscreensnapcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkvoiceclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkvoicecap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkliveclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhklivecap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhktrclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhktrcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhksnapcopyclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhksnapcopycap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkdictclear.Content = Loc.T("set.hotkey.clear");
			bhkdictcap.Content = Loc.T("set.hotkey.capture");
			bhkdictselclear.Content = Loc.T("set.hotkey.clear");
			bhkdictselcap.Content = Loc.T("set.hotkey.capture");
			bhkimecycleclear.Content = Loc.T("set.hotkey.clear");
			bhkimecyclecap.Content = Loc.T("set.hotkey.capture");
			bhkimewubiclear.Content = Loc.T("set.hotkey.clear");
			bhkimewubicap.Content = Loc.T("set.hotkey.capture");
			bhkimejaclear.Content = Loc.T("set.hotkey.clear");
			bhkimejacap.Content = Loc.T("set.hotkey.capture");
			bhkimekoclear.Content = Loc.T("set.hotkey.clear");
			bhkimekocap.Content = Loc.T("set.hotkey.capture");
			bhkimeoffclear.Content = Loc.T("set.hotkey.clear");
			bhkimeoffcap.Content = Loc.T("set.hotkey.capture");
			bhkdictclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkdictcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkdictselclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkdictselcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkimecycleclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkimecyclecap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkimewubiclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkimewubicap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkimejaclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkimejacap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkimekoclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkimekocap.ToolTip = Loc.T("set.hotkey.capture.tip");
			bhkimeoffclear.ToolTip = Loc.T("set.hotkey.clear.tip");
			bhkimeoffcap.ToolTip = Loc.T("set.hotkey.capture.tip");
			lbsetsnap.Text = Loc.T("set.snap");
			lbsetsnapdir.Text = Loc.T("set.snap.dir");
			esnapdir.ToolTip = Loc.T("set.snap.dir.tip");
			lbsetsnapdirhint.Text = Loc.T("set.snap.dir.tip");
			bsnapdir.Content = Loc.T("set.snap.dir.browse");
			lbsetsnapname.Text = Loc.T("set.snap.name");
			esnapname.ToolTip = Loc.T("set.snap.name.tip");
			lbsetsnapnamehint.Text = Loc.T("set.snap.name.tip");
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
			esnappathslash.Content = Loc.T("set.snap.path.slash");
			esnappathslash.ToolTip = Loc.T("set.snap.path.slash.tip");
			lbsetsnapslashhint.Text = Loc.T("set.snap.path.slash.tip");
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

	void filluilang(string selected) {
		var want = Loc.Normalize(selected);
		euilang.Items.Clear();
		ComboBoxItem pick = null;
		foreach (var one in Loc.Languages) {
			var it = new ComboBoxItem { Content = one.Name, Tag = one.Code };
			euilang.Items.Add(it);
			if (string.Equals(one.Code, want, StringComparison.OrdinalIgnoreCase))
				pick = it;
		}
		if (pick != null) euilang.SelectedItem = pick;
		else if (euilang.Items.Count > 0) euilang.SelectedIndex = 0;
	}

	void filltrlangboxes(OcrOptions o) {
		o.AutoTranslatePair(out var nat, out var lrn);
		filltrlangbox(esettrnative, nat);
		filltrlangbox(esettrlearn, lrn);
	}

	void filltrlangbox(ComboBox box, string want) {
		box.Items.Clear();
		ComboBoxItem pick = null;
		foreach (var c in TrLang.LlmCodes) {
			var it = new ComboBoxItem { Content = TrLang.Label(c), Tag = c };
			box.Items.Add(it);
			if (string.Equals(c, want, StringComparison.OrdinalIgnoreCase)) pick = it;
		}
		box.SelectedItem = pick ?? box.Items[0];
	}

	void loadui(OcrOptions o) {
		filluilang(o.UiLang);
		esetchromepath.Text = o.ChromeExe ?? "";
		esetchromeidle.Text = ChromeTts.ClampIdle(o.ChromeIdleSec).ToString();

		if (packs.Count == 0) {
			lbmodelhint.Text = Loc.IsEn
				? "No model packs found (ocrmodels/umi or ocrmodels/rapid-ch next to exe)"
				: "未发现模型包（程序目录 ocrmodels/umi 或 ocrmodels/rapid-ch）";
		}
		else {
			var pack = packs.FirstOrDefault(p =>
					string.Equals(p.Id, o.ModelPackId, StringComparison.OrdinalIgnoreCase))
				?? packs.FirstOrDefault(p => WinOcr.IsId(p.Id))
				?? packs[0];
			epack.SelectedItem = pack;
			// onpackchanged 会填变体；再选中目标变体
			var want = o.ModelVariant;
			if (!WinOcr.IsId(pack.Id) && !string.IsNullOrWhiteSpace(want)
				&& evariant.ItemsSource is IEnumerable<ModelVariant> vs) {
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
		ehotkey.Text = GlobalHotkey.Display(o.Hotkey);
		ehotkeysnap.Text = GlobalHotkey.Display(o.HotkeySnap);
		ehotkeysnapocr.Text = GlobalHotkey.Display(o.HotkeySnapOcr);
		ehotkeyboard.Text = GlobalHotkey.Display(o.HotkeyBoard);
		ehotkeyscreensnap.Text = GlobalHotkey.Display(o.HotkeyScreenSnap);
		ehotkeyvoice.Text = GlobalHotkey.Display(o.HotkeyVoiceInput);
		ehotkeylive.Text = GlobalHotkey.Display(o.HotkeyLiveCaption);
		ehotkeytr.Text = GlobalHotkey.Display(o.HotkeyTranslate);
		ehotkeysnapcopy.Text = GlobalHotkey.Display(o.HotkeySnapCopy);
		ehotkeydict.Text = GlobalHotkey.Display(o.HotkeyDict);
		ehotkeyimecycle.Text = GlobalHotkey.Display(o.HotkeyImeCycle);
		ehotkeyimewubi.Text = GlobalHotkey.Display(o.HotkeyImeWubi);
		ehotkeyimeja.Text = GlobalHotkey.Display(o.HotkeyImeJa);
		ehotkeyimeko.Text = GlobalHotkey.Display(o.HotkeyImeKo);
		ehotkeyimeoff.Text = GlobalHotkey.Display(o.HotkeyImeOff);
		if (cbimegvim != null) cbimegvim.IsChecked = o.ImeGvimMode;
		if (cbimerare != null) cbimerare.IsChecked = o.ImeRare;
		ehotkeydictsel.Text = GlobalHotkey.Display(o.HotkeyDictSel);
		cbdictselbar.IsChecked = o.DictSelBar;
		edictlimit.Text = Compat.Clamp(o.DictLimit < 1 ? 1000 : o.DictLimit, 1, DictDb.SEARCH_MAX).ToString();
		edicthold.Text = OnnxIdle.ClampMin(o.DictDbHoldMin).ToString();
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
		filltrlangboxes(o);
		echatllmprompt.Text = string.IsNullOrWhiteSpace(o.ChatLlmPrompt)
			? OcrOptions.DefaultChatLlmPrompt() : o.ChatLlmPrompt;
		emintray.IsChecked = o.MinimizeToTray;
		etabocrvisible.IsChecked = o.TabOcrVisible && o.ModOcr;
		etabttsvisible.IsChecked = o.TabTtsVisible && o.ModTts;
		etabasrvisible.IsChecked = o.TabAsrVisible && o.ModAsr;
		etabchatvisible.IsChecked = o.TabChatVisible && o.ModChat;
		etabllmlogvisible.IsChecked = o.TabLlmLogVisible;
		etabtranslatevisible.IsChecked = o.TabTranslateVisible && o.ModTranslate;
		etabfacevisible.IsChecked = o.TabFaceVisible && o.ModFace;
		etabhttpvisible.IsChecked = o.TabHttpVisible;
		etabsfvisible.IsChecked = o.TabSendFileVisible;
		etabcastvisible.IsChecked = o.TabCastVisible;
		etabdictvisible.IsChecked = o.TabDictVisible && o.ModDict;
		eupdatedays.Text = Compat.Clamp(o.UpdateCheckDays, 0, 3650).ToString();
		var proxyMode = HttpProxy.NormalizeMode(string.IsNullOrWhiteSpace(o.HttpProxyMode)
			? (o.HttpProxyEnabled ? "manual" : "system") : o.HttpProxyMode);
		rsetproxysys.IsChecked = proxyMode == "system";
		rsetproxyman.IsChecked = proxyMode == "manual";
		rsetproxyoff.IsChecked = proxyMode == "off";
		eproxyaddr.Text = string.IsNullOrWhiteSpace(o.HttpProxyAddr) ? "127.0.0.1:7897" : o.HttpProxyAddr;
		syncproxyui();
		rsetproxysys.Checked -= onproxyen;
		rsetproxyman.Checked -= onproxyen;
		rsetproxyoff.Checked -= onproxyen;
		rsetproxysys.Checked += onproxyen;
		rsetproxyman.Checked += onproxyen;
		rsetproxyoff.Checked += onproxyen;
		// 三选一：路径 > 文件 > 图片
		var asPath = o.SnapCopyAsPath && !o.SnapCopyAsImage && !o.SnapCopyAsFile;
		var asFile = !asPath && o.SnapCopyAsFile && !o.SnapCopyAsImage;
		esnapcopyimg.IsChecked = !asPath && !asFile;
		esnapcopyfile.IsChecked = asFile;
		esnapcopypath.IsChecked = asPath;
		esnappathslash.IsChecked = o.SnapPathSlash;
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
		var fmt = (o.ScreenshotFormat ?? "jpg").Trim().ToLowerInvariant();
		var wantJpg = fmt is "jpg" or "jpeg";
		foreach (ComboBoxItem it in esnapfmt.Items) {
			var tag = (it.Tag as string) ?? "";
			if (string.Equals(tag, wantJpg ? "jpg" : "png", StringComparison.OrdinalIgnoreCase)) {
				esnapfmt.SelectedItem = it;
				break;
			}
		}
		if (esnapfmt.SelectedItem == null) esnapfmt.SelectedIndex = 0;
		var jq = o.ScreenshotJpgQuality <= 0 ? 60 : Compat.Clamp(o.ScreenshotJpgQuality, 1, 100);
		esnapjpgq.Text = jq.ToString();
		esnapshorten.IsChecked = o.ScreenshotShortEnabled;
		esnapshort.Text = (o.ScreenshotShortPx < 16 ? 1080 : o.ScreenshotShortPx).ToString();
		esnapdir.Text = ImageUtil.DisplayScreenshotDir(o.ScreenshotDir);
		try { esnapname.Text = ImageUtil.NormScreenshotName(o.ScreenshotName); }
		catch { esnapname.Text = ImageUtil.ScreenshotNameDefault; }
		syncsnapfmtenabled();
		esnapfmt.SelectionChanged += (_, _) => syncsnapfmtenabled();
		ehttpen.IsChecked = o.HttpEnabled;
		ehttpocr.IsChecked = o.HttpOcr;
		ehttptts.IsChecked = o.HttpTts;
		ehttpasr.IsChecked = o.HttpAsr;
		ehttptranslate.IsChecked = o.HttpTranslate;
		ehttpchat.IsChecked = o.HttpChat;
		ehttpface.IsChecked = o.HttpFace;
		ehttprtsp.IsChecked = o.HttpRtsp;
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
		var win = WinOcr.IsId(pack?.Id);
		var show = win ? Visibility.Visible : Visibility.Collapsed;
		var hide = win ? Visibility.Collapsed : Visibility.Visible;
		psetwin.Visibility = show;
		lbsetvariant.Visibility = hide;
		evariant.Visibility = hide;
		psetonnx.Visibility = hide;
		psetth.Visibility = hide;
		lbsetdetlen.Visibility = hide;
		lbsetdetlenhint.Visibility = hide;
		if (edetlen.Parent is System.Windows.UIElement detrow) detrow.Visibility = hide;
		if (win) {
			var langs = WinOcr.Choices();
			ewinlang.ItemsSource = langs;
			var tag = WinOcr.SelectedTag(Result);
			ewinlang.SelectedItem = langs.FirstOrDefault(x =>
				string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));
			showpackhint();
			return;
		}
		if (pack == null) {
			evariant.ItemsSource = null;
			lbmodelhint.Text = "";
			return;
		}
		evariant.ItemsSource = pack.Variants;
		evariant.SelectedIndex = pack.Variants.Count > 0 ? 0 : -1;
		showpackhint();
	}

	void updatehint() => showpackhint();

	void showpackhint() {
		var pack = epack.SelectedItem as ModelPack;
		if (pack == null) {
			lbmodelhint.Text = "";
			return;
		}
		var trait = packhint(pack.Id);
		if (!WinOcr.IsId(pack.Id)) {
			lbmodelhint.Text = trait;
			return;
		}
		var langs = ewinlang.ItemsSource as System.Collections.ICollection;
		lbmodelhint.Text = langs == null || langs.Count == 0
			? Loc.T("ocr.win.empty") + "\n" + trait
			: trait;
	}

	static string packhint(string id) {
		string key;
		if (WinOcr.IsId(id)) key = "ocr.win.hint";
		else if (string.Equals(id, "umi", StringComparison.OrdinalIgnoreCase)) key = "ocr.pack.umi";
		else if (string.Equals(id, "rapid-ch", StringComparison.OrdinalIgnoreCase)) key = "ocr.pack.rapidch";
		else if (string.Equals(id, "rapid-i18n", StringComparison.OrdinalIgnoreCase)) key = "ocr.pack.rapidi18n";
		else if (string.Equals(id, "rapid", StringComparison.OrdinalIgnoreCase)) key = "ocr.pack.rapid";
		else key = "ocr.pack.onnx";
		return Loc.T(key);
	}

	bool saveui() {
		var pack = epack.SelectedItem as ModelPack;
		var variant = evariant.SelectedItem as ModelVariant;
		if (pack == null) {
			tabset.SelectedItem = tabsetocr;
			MessageBox.Show(this, Loc.T("set.need.pack"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		if (WinOcr.IsId(pack.Id)) {
			var one = ewinlang.SelectedItem as WinOcrLang;
			if (one == null || string.IsNullOrWhiteSpace(one.Tag)) {
				tabset.SelectedItem = tabsetocr;
				MessageBox.Show(this, Loc.T("ocr.win.need"), Loc.T("settings"),
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return false;
			}
			Result.ModelPackId = WinOcr.PackId;
			Result.ModelsDir = "";
			Result.WinOcrLangs = one.Tag;
		}
		else if (variant == null) {
			tabset.SelectedItem = tabsetocr;
			MessageBox.Show(this, Loc.T("set.need.pack"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		else {
			Result.ModelPackId = pack.Id;
			Result.ModelVariant = variant.Title;
			Result.ModelsDir = pack.Dir;
		}

		var langTag = (euilang.SelectedItem as ComboBoxItem)?.Tag as string ?? Loc.Zh;
		Result.UiLang = Loc.Normalize(langTag);

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
		if (!tryreadhotkey(ehotkeyscreensnap, Loc.T("set.hotkey.screensnap"), out Result.HotkeyScreenSnap, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeyvoice, Loc.T("set.hotkey.voice"), out Result.HotkeyVoiceInput, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeylive, Loc.T("set.hotkey.live"), out Result.HotkeyLiveCaption, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeytr, Loc.T("set.hotkey.translate"), out Result.HotkeyTranslate, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeysnapcopy, Loc.T("set.hotkey.snapcopy"), out Result.HotkeySnapCopy, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeydict, Loc.T("set.hotkey.dict"), out Result.HotkeyDict, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeyimecycle, Loc.T("set.hotkey.ime.cycle"), out Result.HotkeyImeCycle, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeyimewubi, Loc.T("set.hotkey.ime.wubi"), out Result.HotkeyImeWubi, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeyimeja, Loc.T("set.hotkey.ime.ja"), out Result.HotkeyImeJa, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeyimeko, Loc.T("set.hotkey.ime.ko"), out Result.HotkeyImeKo, tabsethk)) return false;
		if (!tryreadhotkey(ehotkeyimeoff, Loc.T("set.hotkey.ime.off"), out Result.HotkeyImeOff, tabsethk)) return false;
		Result.ImeGvimMode = cbimegvim != null && cbimegvim.IsChecked == true;
		Result.ImeRare = cbimerare != null && cbimerare.IsChecked == true;
		if (!tryreadhotkey(ehotkeydictsel, Loc.T("set.hotkey.dictsel"), out Result.HotkeyDictSel, tabsetdict)) return false;
		Result.DictSelBar = cbdictselbar.IsChecked == true;
		if (!tryint(edictlimit, Loc.T("set.dict.limit"), 1, DictDb.SEARCH_MAX, out var dictLimit, tabsetdict)) return false;
		Result.DictLimit = dictLimit;
		if (!tryint(edicthold, Loc.T("set.dict.dbhold"), 0, OnnxIdle.MaxMin, out var dictHold, tabsetdict)) return false;
		Result.DictDbHoldMin = dictHold;
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
		TrLang.NormalizeAuto((esettrnative.SelectedItem as ComboBoxItem)?.Tag as string,
			(esettrlearn.SelectedItem as ComboBoxItem)?.Tag as string, out var trAutoNative, out var trAutoLearn);
		Result.TranslateAutoNative = trAutoNative;
		Result.TranslateAutoLearn = trAutoLearn;
		Result.MinimizeToTray = emintray.IsChecked == true;
		Result.TabOcrVisible = Result.ModOcr = etabocrvisible.IsChecked == true;
		Result.TabTtsVisible = Result.ModTts = etabttsvisible.IsChecked == true;
		Result.TabAsrVisible = Result.ModAsr = etabasrvisible.IsChecked == true;
		Result.TabChatVisible = Result.ModChat = etabchatvisible.IsChecked == true;
		Result.TabLlmLogVisible = etabllmlogvisible.IsChecked == true;
		Result.TabTranslateVisible = Result.ModTranslate = etabtranslatevisible.IsChecked == true;
		Result.TabFaceVisible = Result.ModFace = etabfacevisible.IsChecked == true;
		Result.TabHttpVisible = etabhttpvisible.IsChecked == true;
		Result.TabSendFileVisible = etabsfvisible.IsChecked == true;
		Result.TabCastVisible = etabcastvisible.IsChecked == true;
		Result.TabDictVisible = Result.ModDict = etabdictvisible.IsChecked == true;
		if (!tryint(eupdatedays, Loc.T("set.update"), 0, 3650, out var updDays, tabsetgen)) return false;
		Result.UpdateCheckDays = updDays;
		Result.HttpProxyMode = rsetproxyman.IsChecked == true ? "manual"
			: rsetproxyoff.IsChecked == true ? "off" : "system";
		Result.HttpProxyEnabled = Result.HttpProxyMode == "manual";
		var proxyAddr = (eproxyaddr.Text ?? "").Trim();
		Result.HttpProxyAddr = string.IsNullOrWhiteSpace(proxyAddr) ? "127.0.0.1:7897" : proxyAddr;
		// 截图历史保留：Tag 天数，0=不限
		var keepTag = (esnapkeep.SelectedItem as ComboBoxItem)?.Tag as string ?? "3";
		if (!int.TryParse(keepTag, out var keepDays) || keepDays < 0)
			keepDays = 3;
		Result.ScreenshotKeepDays = keepDays > 3650 ? 3650 : keepDays;
		// 保存格式 / jpg 质量 / 较短边
		var fmtTag = (esnapfmt.SelectedItem as ComboBoxItem)?.Tag as string ?? "jpg";
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
		if (!applysnapdir()) return false;
		try { Result.ScreenshotName = ImageUtil.NormScreenshotName(esnapname.Text); }
		catch {
			tabset.SelectedItem = tabsetsnap;
			MessageBox.Show(this, Loc.T("set.snap.name.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		// 三选一
		var asPath = esnapcopypath.IsChecked == true;
		var asFile = !asPath && esnapcopyfile.IsChecked == true;
		Result.SnapCopyAsImage = !asPath && !asFile;
		Result.SnapCopyAsFile = asFile;
		Result.SnapCopyAsPath = asPath;
		Result.SnapPathSlash = esnappathslash.IsChecked == true;
		Result.HttpEnabled = ehttpen.IsChecked == true;
		Result.HttpOcr = ehttpocr.IsChecked == true;
		Result.HttpTts = ehttptts.IsChecked == true;
		Result.HttpAsr = ehttpasr.IsChecked == true;
		Result.HttpTranslate = ehttptranslate.IsChecked == true;
		Result.HttpChat = ehttpchat.IsChecked == true;
		Result.HttpFace = ehttpface.IsChecked == true;
		Result.HttpRtsp = ehttprtsp.IsChecked == true;
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
		if (!Result.TabSendFileVisible) Result.SendFileEnabled = false;
		if (!Result.TabCastVisible) Result.CastRecvEnabled = false;
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
		Result.ChromeExe = (esetchromepath.Text ?? "").Trim();
		if (!int.TryParse((esetchromeidle.Text ?? "").Trim(), out var chromeIdle))
			chromeIdle = Result.ChromeIdleSec;
		Result.ChromeIdleSec = ChromeTts.ClampIdle(chromeIdle);
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
		var on = rsetproxyman.IsChecked == true;
		eproxyaddr.IsEnabled = on;
		lbsetproxyaddr.Opacity = on ? 1 : 0.45;
		eproxyaddr.Opacity = on ? 1 : 0.55;
	}

	bool dictloading;
	readonly List<DictRow> dictrows = new();
	readonly Dictionary<ComboBox, string> dictlasteng = new();

	sealed class DictRow {
		public string Lang;
		public DictTtsLang Pref;
		public ComboBox Eng;
		public ComboBox Voice;
		public ComboBox Rate;
	}

	void loaddicttts(OcrOptions o) {
		dictrows.Clear();
		pdicttts.Children.Clear();
		var map = DictTts.CopyMap(o?.DictTtsMap);
		foreach (var lang in DictTts.LangsOf(map)) {
			if (!map.TryGetValue(lang, out var pref) || pref == null)
				pref = DictTtsLang.Make(DictTts.DefaultEdge(lang));
			adddictrow(lang, pref);
		}
		filldictadd();
	}

	void adddictrow(string lang, DictTtsLang pref) {
		var row = new DictRow { Lang = lang, Pref = pref };
		var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
		var lab = new TextBlock {
			Text = TtsLang.DisplayName(lang),
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis,
		};
		Grid.SetColumn(lab, 0);
		var eng = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 6, 0) };
		Grid.SetColumn(eng, 1);
		var voice = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 6, 0) };
		Grid.SetColumn(voice, 2);
		var rate = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 4, 0) };
		Grid.SetColumn(rate, 3);
		row.Eng = eng;
		row.Voice = voice;
		row.Rate = rate;
		grid.Children.Add(lab);
		grid.Children.Add(eng);
		grid.Children.Add(voice);
		grid.Children.Add(rate);
		if (!DictTts.IsCoreLang(lang)) {
			var del = new Button {
				Content = "×",
				Width = 28,
				Height = 28,
				ToolTip = Loc.T("set.dict.tts.del"),
			};
			Grid.SetColumn(del, 4);
			del.Click += (_, _) => removedictrow(row, grid);
			grid.Children.Add(del);
		}
		eng.SelectionChanged += (_, _) => ondicteng(row);
		voice.SelectionChanged += (_, _) => {
			if (dictloading) return;
			writevoice(row.Pref, engtag(eng), tagof(voice));
		};
		rate.SelectionChanged += (_, _) => {
			if (dictloading) return;
			row.Pref.Rate = rateof(rate);
		};
		pdicttts.Children.Add(grid);
		dictrows.Add(row);
		filldictlang(row);
	}

	void removedictrow(DictRow row, Grid grid) {
		dictrows.Remove(row);
		pdicttts.Children.Remove(grid);
		dictlasteng.Remove(row.Eng);
		filldictadd();
	}

	void ondictttsadd(object sender, RoutedEventArgs e) {
		var typed = (edictttsadd.Text ?? "").Trim();
		var lang = "";
		if (edictttsadd.SelectedItem is ComboBoxItem it && it.Tag is string tag) {
			var label = it.Content as string ?? "";
			if (typed.Length == 0
				|| string.Equals(typed, label, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(typed, tag, StringComparison.OrdinalIgnoreCase))
				lang = DictTts.NormLang(tag);
		}
		if (lang.Length == 0)
			lang = DictTts.NormLang(typed);
		if (lang.Length == 0) return;
		foreach (var row in dictrows) {
			if (string.Equals(row.Lang, lang, StringComparison.OrdinalIgnoreCase)) return;
		}
		adddictrow(lang, DictTtsLang.Make(DictTts.DefaultEdge(lang)));
		filldictadd();
		edictttsadd.Text = "";
	}

	void filldictadd() {
		string[] extra = { "vi", "yue", "fr", "de", "es", "pt", "ru", "it" };
		var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in dictrows) have.Add(row.Lang);
		edictttsadd.Items.Clear();
		foreach (var code in extra) {
			if (have.Contains(code)) continue;
			edictttsadd.Items.Add(new ComboBoxItem { Content = TtsLang.DisplayName(code), Tag = code });
		}
		if (edictttsadd.Items.Count > 0) edictttsadd.SelectedIndex = 0;
	}

	void savedicttts() {
		var map = new Dictionary<string, DictTtsLang>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in dictrows) {
			flushdict(row.Eng, row.Voice, row.Rate, row.Pref);
			map[row.Lang] = row.Pref.Clone();
		}
		DictTts.EnsureMap(map);
		Result.DictTtsMap = map;
	}

	void flushdict(ComboBox eng, ComboBox voice, ComboBox rate, DictTtsLang hold) {
		if (hold == null) return;
		var kind = engtag(eng);
		hold.Engine = kind;
		hold.Rate = rateof(rate);
		writevoice(hold, kind, tagof(voice));
	}

	void ondicteng(DictRow row) {
		if (dictloading || row?.Pref == null) return;
		var next = engtag(row.Eng);
		if (dictlasteng.TryGetValue(row.Eng, out var prev) && prev != next)
			writevoice(row.Pref, prev, tagof(row.Voice));
		dictlasteng[row.Eng] = next;
		row.Pref.Engine = next;
		filldictvoice(row);
	}

	void filldictlang(DictRow row) {
		var pref = row.Pref ?? DictTtsLang.Make(DictTts.DefaultEdge(row.Lang));
		row.Pref = pref;
		dictloading = true;
		try {
			filltag(row.Eng, new[] {
				(Loc.T("dict.tts.auto"), DictTts.AUTO),
				(Loc.T("dict.tts.onnx"), DictTts.ONNX),
				(Loc.T("dict.tts.sapi"), DictTts.SAPI),
				(Loc.T("dict.tts.winrt"), DictTts.WINRT),
				(Loc.T("dict.tts.edge"), DictTts.EDGE),
				(Loc.T("dict.tts.chrome"), DictTts.CHROME),
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
			filltag(row.Rate, rates, wantRate);
			dictlasteng[row.Eng] = engtag(row.Eng);
			filldictvoice(row);
		}
		finally { dictloading = false; }
	}

	void filldictvoice(DictRow row) {
		var kind = engtag(row.Eng);
		var auto = kind == DictTts.AUTO;
		row.Voice.IsEnabled = !auto;
		if (auto) {
			row.Voice.Items.Clear();
			return;
		}
		var items = DictTts.Voices(kind, row.Lang);
		var want = voiceid(row.Pref, kind, row.Lang);
		var have = false;
		foreach (var it in items) {
			if (string.Equals(it.Id, want, StringComparison.OrdinalIgnoreCase)) have = true;
		}
		if (want.Length > 0 && !have) items.Add((want, want));
		var was = dictloading;
		dictloading = true;
		try { filltag(row.Voice, items, want); }
		finally { dictloading = was; }
	}

	static string voiceid(DictTtsLang pref, string engine, string lang) {
		if (pref == null) return "";
		if (engine == DictTts.ONNX) return pref.Onnx ?? "";
		if (engine == DictTts.SAPI) return pref.Sapi ?? "";
		if (engine == DictTts.WINRT) return pref.WinRt ?? "";
		if (engine == DictTts.EDGE) return DictTts.EdgeName(lang, pref.Edge);
		if (engine == DictTts.CHROME) return pref.Chrome ?? "";
		return "";
	}

	static void writevoice(DictTtsLang hold, string engine, string id) {
		if (hold == null) return;
		id = id ?? "";
		if (engine == DictTts.ONNX) hold.Onnx = id;
		else if (engine == DictTts.SAPI) hold.Sapi = id;
		else if (engine == DictTts.WINRT) hold.WinRt = id;
		else if (engine == DictTts.CHROME) hold.Chrome = id;
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

	void browsechrome() {
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("tts.chrome.browse"),
			Filter = "chrome.exe|chrome.exe|*.exe|*.exe",
			FileName = "chrome.exe",
		};
		if (dlg.ShowDialog(this) != true) return;
		esetchromepath.Text = dlg.FileName;
	}

	void detectchrome() {
		var path = ChromeTts.FindExe("");
		if (string.IsNullOrEmpty(path)) {
			MessageBox.Show(this, Loc.T("tts.chrome.detect.none"), Loc.T("tts.chrome.detect"),
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		esetchromepath.Text = path;
	}

	void browsesnapdir() {
		using var dlg = new System.Windows.Forms.FolderBrowserDialog {
			Description = Loc.T("set.snap.dir.tip"),
			ShowNewFolderButton = true,
		};
		try {
			var cur = ImageUtil.ResolveScreenshotDir(esnapdir.Text);
			if (System.IO.Directory.Exists(cur)) dlg.SelectedPath = cur;
		}
		catch { }
		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
		var picked = dlg.SelectedPath ?? "";
		try {
			var full = System.IO.Path.GetFullPath(picked);
			var def = ImageUtil.ResolveScreenshotDir("");
			if (string.Equals(full, def, StringComparison.OrdinalIgnoreCase)) {
				esnapdir.Text = ImageUtil.DisplayScreenshotDir("");
				return;
			}
			esnapdir.Text = ImageUtil.DisplayScreenshotDir(full);
		}
		catch { esnapdir.Text = picked; }
	}

	/// <summary>保存位置：与当前默认目录相同则留空；其它目录须能创建。</summary>
	bool applysnapdir() {
		string norm;
		try { norm = ImageUtil.NormScreenshotDir(esnapdir.Text); }
		catch {
			tabset.SelectedItem = tabsetsnap;
			MessageBox.Show(this, Loc.T("set.snap.dir.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		if (norm.Length == 0) {
			Result.ScreenshotDir = "";
			return true;
		}
		string full;
		try { full = ImageUtil.ResolveScreenshotDir(norm); }
		catch { full = null; }
		try {
			var defFull = ImageUtil.ResolveScreenshotDir("");
			if (!string.IsNullOrEmpty(full) &&
				string.Equals(full, defFull, StringComparison.OrdinalIgnoreCase)) {
				Result.ScreenshotDir = "";
				return true;
			}
		}
		catch { }
		if (string.IsNullOrEmpty(full)) {
			tabset.SelectedItem = tabsetsnap;
			MessageBox.Show(this, Loc.T("set.snap.dir.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		try { System.IO.Directory.CreateDirectory(full); }
		catch {
			tabset.SelectedItem = tabsetsnap;
			MessageBox.Show(this, Loc.T("set.snap.dir.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		try { Result.ScreenshotDir = ImageUtil.NormScreenshotDir(full); }
		catch { Result.ScreenshotDir = norm; }
		return true;
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
		if (GlobalHotkey.tryparse(value, out _, out _)) {
			value = GlobalHotkey.Display(value);
			return true;
		}
		if (tab != null) tabset.SelectedItem = tab;
		MessageBox.Show(this, Loc.T("set.hotkey.parse", name, value), Loc.T("settings"),
			MessageBoxButton.OK, MessageBoxImage.Warning);
		return false;
	}
}
