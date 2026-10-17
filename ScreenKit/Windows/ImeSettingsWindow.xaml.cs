using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton = System.Windows.Controls.Button;

namespace ScreenKit;

/// <summary>输入法专用设置窗：切换热键与 gVim 随模式切换。
/// 只写回输入法相关项，其它参数仍在主「参数设置」里。</summary>
public partial class ImeSettingsWindow : Window {
	public OcrOptions Result { get; private set; }
	public bool Applied { get; private set; }

	/// <summary>点「词库…」时抛出，由主窗决定怎么弹出（主窗可能在托盘里）。</summary>
	public event Action Dict;

	WpfTextBox captarget;
	WpfButton capbtn;
	string capprev = "";

	public ImeSettingsWindow(OcrOptions current) {
		InitializeComponent();
		Result = current?.Clone() ?? new OcrOptions();
		inithotkeyui();

		bcancel.Click += (_, _) => { Applied = false; Close(); };
		bdict.Click += (_, _) => { try { Dict?.Invoke(); } catch { } };
		bok.Click += (_, _) => {
			stopcapture(restore: true);
			if (!saveui()) return;
			Applied = true;
			Close();
		};
		// 捕获热键时 Esc 只取消捕获，不关窗
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
		bindhotkey(ehotkeyimecycle, bimecyclecap, bimecycleclear);
		bindhotkey(ehotkeyimewubi, bimewubicap, bimewubiclear);
		bindhotkey(ehotkeyimeja, bimejacap, bimejaclear);
		bindhotkey(ehotkeyimeko, bimekocap, bimekoclear);
		bindhotkey(ehotkeyimeoff, bimeoffcap, bimeoffclear);
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
		Title = Loc.T("set.ime.title");
		bdict.Content = Loc.T("ime.dict.openwin");
		lbhkhintImeCycle.Text = Loc.T("set.hotkey.ime.cycle");
		lbhkhintImeWubi.Text = Loc.T("set.hotkey.ime.wubi");
		lbhkhintImeJa.Text = Loc.T("set.hotkey.ime.ja");
		lbhkhintImeKo.Text = Loc.T("set.hotkey.ime.ko");
		lbhkhintImeOff.Text = Loc.T("set.hotkey.ime.off");
		cbimegvim.Content = Loc.T("set.ime.gvim");
		lbimegvimhint.Text = Loc.T("set.ime.gvim.hint");
		cbimerare.Content = Loc.T("set.ime.rare");
		lbimerarehint.Text = Loc.T("set.ime.rare.hint");
		foreach (var (clear, cap) in new[] {
			(bimecycleclear, bimecyclecap), (bimewubiclear, bimewubicap),
			(bimejaclear, bimejacap), (bimekoclear, bimekocap), (bimeoffclear, bimeoffcap),
		}) {
			clear.Content = Loc.T("set.hotkey.clear");
			clear.ToolTip = Loc.T("set.hotkey.clear.tip");
			cap.Content = Loc.T("set.hotkey.capture");
			cap.ToolTip = Loc.T("set.hotkey.capture.tip");
		}
		bok.Content = Loc.T("ok");
		bcancel.Content = Loc.T("cancel");
		var needdb = ImeLex.DbInstalled ? "" : Loc.T("hangul.needdb");
		lbneeddb.Text = needdb;
		lbneeddb.Visibility = string.IsNullOrWhiteSpace(needdb)
			? Visibility.Collapsed : Visibility.Visible;
	}

	void loadui(OcrOptions o) {
		ehotkeyimecycle.Text = GlobalHotkey.Display(o.HotkeyImeCycle);
		ehotkeyimewubi.Text = GlobalHotkey.Display(o.HotkeyImeWubi);
		ehotkeyimeja.Text = GlobalHotkey.Display(o.HotkeyImeJa);
		ehotkeyimeko.Text = GlobalHotkey.Display(o.HotkeyImeKo);
		ehotkeyimeoff.Text = GlobalHotkey.Display(o.HotkeyImeOff);
		cbimegvim.IsChecked = o.ImeGvimMode;
		cbimerare.IsChecked = o.ImeRare;
	}

	bool saveui() {
		if (!tryreadhotkey(ehotkeyimecycle, Loc.T("set.hotkey.ime.cycle"), out Result.HotkeyImeCycle)) return false;
		if (!tryreadhotkey(ehotkeyimewubi, Loc.T("set.hotkey.ime.wubi"), out Result.HotkeyImeWubi)) return false;
		if (!tryreadhotkey(ehotkeyimeja, Loc.T("set.hotkey.ime.ja"), out Result.HotkeyImeJa)) return false;
		if (!tryreadhotkey(ehotkeyimeko, Loc.T("set.hotkey.ime.ko"), out Result.HotkeyImeKo)) return false;
		if (!tryreadhotkey(ehotkeyimeoff, Loc.T("set.hotkey.ime.off"), out Result.HotkeyImeOff)) return false;
		Result.ImeGvimMode = cbimegvim.IsChecked == true;
		Result.ImeRare = cbimerare.IsChecked == true;
		return true;
	}

	/// <summary>读热键：留空允许（禁用）；非空须能解析。</summary>
	bool tryreadhotkey(WpfTextBox box, string name, out string value) {
		value = (box?.Text ?? "").Trim();
		if (string.IsNullOrEmpty(value)) return true;
		if (GlobalHotkey.tryparse(value, out _, out _)) {
			value = GlobalHotkey.Display(value);
			return true;
		}
		MessageBox.Show(this, Loc.T("set.hotkey.parse", name, value), Loc.T("settings"),
			MessageBoxButton.OK, MessageBoxImage.Warning);
		return false;
	}
}
