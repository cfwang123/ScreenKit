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

		easrvoicesplit.Checked += (_, _) => syncvoicesplitui();
		easrvoicesplit.Unchecked += (_, _) => syncvoicesplitui();

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
			tabsetasr.Header = Loc.T("set.tab.asr");
			tabsetllm.Header = Loc.T("set.tab.llm");
			tabsettr.Header = Loc.T("set.tab.translate");
			tabsetsnap.Header = Loc.T("set.tab.snap");
			tabsethttp.Header = Loc.T("set.tab.http");
			lbsetlang.Text = Loc.T("set.lang");
			lbsetlanghint.Text = Loc.T("set.lang.hint");
			emintray.Content = Loc.T("set.tray");
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
			lbsetllmthink.Text = Loc.T("set.llm.think");
			lbsetllmthinkhint.Text = Loc.T("set.llm.think.hint");
			itllmthinkoff.Content = Loc.T("set.llm.think.off");
			itllmthinklow.Content = Loc.T("set.llm.think.low");
			itllmthinkhigh.Content = Loc.T("set.llm.think.high");
			itllmthinkmax.Content = Loc.T("set.llm.think.max");
			lbsettrhint.Text = Loc.T("set.tr.hint");
			lbsettrllmprompt.Text = Loc.T("set.tr.llm.prompt");
			lbsettrllmprompthint.Text = Loc.T("set.tr.llm.prompt.hint");
			etrllmprompt.ToolTip = Loc.T("set.tr.llm.prompt.tip");
			lbsethttp.Text = Loc.T("set.http");
			lbsethttphint.Text = Loc.T("set.http.hint");
			ehttpen.Content = Loc.T("set.http.enable");
			lbsethttphost.Text = Loc.T("set.http.host");
			lbsethttpport.Text = Loc.T("set.http.port");
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
			lbsetsnap.Text = Loc.T("set.snap");
			lbsetsnaphint.Text = Loc.T("set.snap.hint");
			lbsetsnapfmt.Text = Loc.T("set.snap.fmt");
			lbsnapjpgq.Text = Loc.T("set.snap.jpgq");
			esnapjpgq.ToolTip = Loc.T("set.snap.jpgq.tip");
			esnapmaxen.Content = Loc.T("set.snap.max");
			lbsetsnapmaxw.Text = Loc.T("set.snap.maxw");
			lbsetsnapmaxh.Text = Loc.T("set.snap.maxh");
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
			? OcrOptions.DefaultAsrLlmPrompt : o.AsrLlmPrompt;
		etrllmprompt.Text = string.IsNullOrWhiteSpace(o.TranslateLlmPrompt)
			? OcrOptions.DefaultTranslateLlmPrompt : o.TranslateLlmPrompt;
		emintray.IsChecked = o.MinimizeToTray;
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
		// 保存格式 / jpg 质量 / 最大宽高
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
		esnapmaxen.IsChecked = o.ScreenshotMaxSizeEnabled;
		esnapmaxw.Text = (o.ScreenshotMaxWidth < 16 ? 1920 : o.ScreenshotMaxWidth).ToString();
		esnapmaxh.Text = (o.ScreenshotMaxHeight < 16 ? 1080 : o.ScreenshotMaxHeight).ToString();
		syncsnapfmtenabled();
		esnapfmt.SelectionChanged += (_, _) => syncsnapfmtenabled();
		ehttpen.IsChecked = o.HttpEnabled;
		ehttphost.Text = string.IsNullOrWhiteSpace(o.HttpHost) ? "127.0.0.1" : o.HttpHost;
		ehttpport.Text = o.HttpPort > 0 ? o.HttpPort.ToString() : "1224";
		eservicemode.IsChecked = o.ServiceMode;
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
		Result.AsrLlmPrompt = string.IsNullOrEmpty(prompt) ? OcrOptions.DefaultAsrLlmPrompt : prompt;
		var trPrompt = (etrllmprompt.Text ?? "").Trim();
		Result.TranslateLlmPrompt = string.IsNullOrEmpty(trPrompt)
			? OcrOptions.DefaultTranslateLlmPrompt : trPrompt;
		Result.MinimizeToTray = emintray.IsChecked == true;
		// 截图历史保留：Tag 天数，0=不限
		var keepTag = (esnapkeep.SelectedItem as ComboBoxItem)?.Tag as string ?? "3";
		if (!int.TryParse(keepTag, out var keepDays) || keepDays < 0)
			keepDays = 3;
		Result.ScreenshotKeepDays = keepDays > 3650 ? 3650 : keepDays;
		// 保存格式 / jpg 质量 / 最大宽高
		var fmtTag = (esnapfmt.SelectedItem as ComboBoxItem)?.Tag as string ?? "png";
		Result.ScreenshotFormat = string.Equals(fmtTag, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
		if (!int.TryParse((esnapjpgq.Text ?? "").Trim(), out var jpgQ) || jpgQ < 1 || jpgQ > 100) {
			tabset.SelectedItem = tabsetsnap;
			MessageBox.Show(this, Loc.T("set.jpgq.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		Result.ScreenshotJpgQuality = jpgQ;
		Result.ScreenshotMaxSizeEnabled = esnapmaxen.IsChecked == true;
		if (!tryint(esnapmaxw, Loc.T("set.maxw.name"), 16, 16384, out var smw, tabsetsnap)) return false;
		if (!tryint(esnapmaxh, Loc.T("set.maxh.name"), 16, 16384, out var smh, tabsetsnap)) return false;
		Result.ScreenshotMaxWidth = smw;
		Result.ScreenshotMaxHeight = smh;
		// 三选一
		var asPath = esnapcopypath.IsChecked == true;
		var asFile = !asPath && esnapcopyfile.IsChecked == true;
		Result.SnapCopyAsImage = !asPath && !asFile;
		Result.SnapCopyAsFile = asFile;
		Result.SnapCopyAsPath = asPath;
		Result.HttpEnabled = ehttpen.IsChecked == true;
		var host = (ehttphost.Text ?? "").Trim();
		Result.HttpHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
		if (!int.TryParse((ehttpport.Text ?? "").Trim(), out var port) || port < 1 || port > 65535) {
			tabset.SelectedItem = tabsethttp;
			MessageBox.Show(this, Loc.T("set.http.port.bad"), Loc.T("settings"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		Result.HttpPort = port;
		Result.ServiceMode = eservicemode.IsChecked == true;
		Result.PdfInvisibleText = epdftext.IsChecked == true;
		Result.CaptureLog = ecapturelog.IsChecked == true;
		Result.LlmLog = ellmlog.IsChecked == true;
		// PdfDpi 固定内部默认，不再由界面配置
		if (Result.PdfDpi <= 0) Result.PdfDpi = PdfOcr.DefaultDpi;
		return true;
	}

	static OcrOptions clone(OcrOptions o) => new() {
		ModelPackId = o.ModelPackId,
		ModelVariant = o.ModelVariant,
		ModelsDir = o.ModelsDir,
		UiLang = o.UiLang ?? "zh",
		Device = o.Device,
		DetLimitSideLen = o.DetLimitSideLen,
		DetPadding = o.DetPadding,
		DetThresh = o.DetThresh,
		DetBoxThresh = o.DetBoxThresh,
		DetUnclipRatio = o.DetUnclipRatio,
		DetUseDilation = o.DetUseDilation,
		RecImgH = o.RecImgH,
		RecMaxWidth = o.RecMaxWidth,
		RecAbsMaxWidth = o.RecAbsMaxWidth,
		RecBatchNum = o.RecBatchNum,
		UseCls = o.UseCls,
		Hotkey = o.Hotkey,
		HotkeySnap = o.HotkeySnap,
		HotkeySnapOcr = o.HotkeySnapOcr,
		HotkeyBoard = o.HotkeyBoard,
		HotkeyVoiceInput = o.HotkeyVoiceInput,
		HotkeyLiveCaption = o.HotkeyLiveCaption,
		MinimizeToTray = o.MinimizeToTray,
		HttpEnabled = o.HttpEnabled,
		HttpHost = o.HttpHost,
		HttpPort = o.HttpPort,
		ServiceMode = o.ServiceMode,
		PdfInvisibleText = o.PdfInvisibleText,
		PdfDpi = o.PdfDpi,
		CaptureLog = o.CaptureLog,
		LlmLog = o.LlmLog,
		ScreenshotKeepDays = o.ScreenshotKeepDays,
		ScreenshotFormat = o.ScreenshotFormat ?? "png",
		ScreenshotJpgQuality = o.ScreenshotJpgQuality,
		ScreenshotMaxSizeEnabled = o.ScreenshotMaxSizeEnabled,
		ScreenshotMaxWidth = o.ScreenshotMaxWidth,
		ScreenshotMaxHeight = o.ScreenshotMaxHeight,
		SnapCopyAsImage = o.SnapCopyAsImage,
		SnapCopyAsFile = o.SnapCopyAsFile,
		SnapCopyAsPath = o.SnapCopyAsPath,
		Record = (o.Record ?? new RecordOptions()).Clone(),
		GifRecord = (o.GifRecord ?? new GifOptions()).Clone(),
		InstallPromptDone = o.InstallPromptDone,
		TtsEngine = o.TtsEngine,
		TtsCompute = o.TtsCompute,
		TtsModel = o.TtsModel,
		TtsVoice = o.TtsVoice,
		TtsLangFilter = o.TtsLangFilter,
		TtsGenderFilter = o.TtsGenderFilter,
		TtsRate = o.TtsRate,
		TtsVolume = o.TtsVolume,
		TtsKbps = o.TtsKbps,
		AsrModel = o.AsrModel,
		AsrModelStream = o.AsrModelStream,
		AsrCompute = o.AsrCompute,
		AsrLang = o.AsrLang,
		AsrItn = o.AsrItn,
		AsrAudioSource = o.AsrAudioSource,
		AsrCaption = (o.AsrCaption ?? new AsrCaptionStyle()).Clone(),
		AsrVoiceMode = o.AsrVoiceMode ?? "stream",
		AsrVoicePolish = o.AsrVoicePolish,
		AsrVoiceSplit = o.AsrVoiceSplit,
		AsrVoiceSplitSec = o.AsrVoiceSplitSec,
		AsrLiveMode = o.AsrLiveMode ?? "stream",
		AsrLivePolish = o.AsrLivePolish,
		AsrLiveSplit = o.AsrLiveSplit,
		LlmList = (o.LlmList ?? new List<LlmEndpoint>())
			.Where(x => x != null).Select(x => x.Clone()).ToList(),
		AsrLlm = o.AsrLlm ?? "",
		AsrLlmPrompt = o.AsrLlmPrompt ?? OcrOptions.DefaultAsrLlmPrompt,
		TranslateCompute = o.TranslateCompute,
		TranslateLlm = o.TranslateLlm ?? "",
		TranslateLlmPrompt = o.TranslateLlmPrompt ?? OcrOptions.DefaultTranslateLlmPrompt,
		FaceCompute = o.FaceCompute,
		FaceDetModel = o.FaceDetModel,
		FaceRegModel = o.FaceRegModel,
		FaceLmkModel = o.FaceLmkModel,
		FaceAttrModel = o.FaceAttrModel,
		FaceThreshold = o.FaceThreshold,
		WinW = o.WinW,
		WinH = o.WinH,
		WinL = o.WinL,
		WinT = o.WinT,
		WinMax = o.WinMax,
	};

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
				return;
			}
		}
		ellmthink.SelectedIndex = 1;
	}

	string thinktag() {
		if (ellmthink.SelectedItem is ComboBoxItem x && x.Tag is string t && t.Length > 0)
			return LlmEndpoint.NormThink(t);
		return "low";
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
