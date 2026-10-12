namespace ScreenKit;

/// <summary>工具 → 自动按键：设置要重复的按键或鼠标键，以及开始/停止快捷键。</summary>
public partial class AutoKeyWindow : Window {
	readonly OcrOptions opt;
	readonly Action ontoggle;
	readonly Action onhotkey;
	bool loading = true;
	bool capturing;
	Brush statidle;
	Brush statrun;
	Brush statskip;
	TextBox capbox;
	Button capbtn;
	string capprev = "";

	public AutoKeyWindow(OcrOptions opt, Action ontoggle, Action onhotkey) {
		this.opt = opt ?? new OcrOptions();
		this.ontoggle = ontoggle;
		this.onhotkey = onhotkey;
		InitializeComponent();
		applylang();
		loadfields();
		initev();
		loading = false;
		syncmode();
	}

	public void ShowRun(bool on, long sent, bool skipped) {
		bstart.Content = Loc.T(on ? "autokey.stop" : "autokey.start");
		if (!on) lbstat.Text = Loc.T("autokey.idle");
		else if (skipped) lbstat.Text = Loc.T("autokey.skip", sent);
		else lbstat.Text = Loc.T("autokey.run", sent);
		paintstat(on, skipped);
	}

	void paintstat(bool on, bool skipped) {
		if (statidle == null) {
			statidle = (Brush)FindResource("Danger");
			statrun = (Brush)FindResource("Success");
			statskip = new SolidColorBrush(Color.FromRgb(217, 119, 6));
			statskip.Freeze();
		}
		if (!on) lbstat.Foreground = statidle;
		else if (skipped) lbstat.Foreground = statskip;
		else lbstat.Foreground = statrun;
	}

	void initev() {
		rkey.Checked += (_, _) => onmode();
		rleft.Checked += (_, _) => onmode();
		rright.Checked += (_, _) => onmode();
		rmiddle.Checked += (_, _) => onmode();
		bkeycap.Click += (_, _) => startcapture(ekey, bkeycap);
		bhotcap.Click += (_, _) => startcapture(ehotkey, bhotcap);
		bkeyclear.Click += (_, _) => {
			if (capbox == ekey) stopcapture(false);
			ekey.Text = "";
			commit();
		};
		bhotclear.Click += (_, _) => {
			if (capbox == ehotkey) stopcapture(false);
			ehotkey.Text = "";
			commit();
		};
		einterval.LostFocus += (_, _) => commit();
		ekey.LostFocus += (_, _) => { if (!capturing) commit(); };
		ehotkey.LostFocus += (_, _) => { if (!capturing) commit(); };
		bstart.Click += (_, _) => {
			commit();
			try { ontoggle?.Invoke(); } catch { }
		};
		bclose.Click += (_, _) => Close();
		Closing += (_, _) => {
			stopcapture(true);
			commit();
		};
		PreviewKeyDown += (_, e) => {
			if (e.Key != Key.Escape) return;
			e.Handled = true;
			if (capturing) stopcapture(true);
			else Close();
		};
	}

	void applylang() {
		Title = Loc.T("autokey.title");
		lbhint.Text = Loc.T("autokey.hint");
		lbmode.Text = Loc.T("autokey.mode");
		rkey.Content = Loc.T("autokey.keymode");
		rleft.Content = Loc.T("autokey.left");
		rright.Content = Loc.T("autokey.right");
		rmiddle.Content = Loc.T("autokey.middle");
		lbkey.Text = Loc.T("autokey.key");
		ekey.ToolTip = Loc.T("autokey.key.tip");
		lbhotkey.Text = Loc.T("autokey.hotkey");
		ehotkey.ToolTip = Loc.T("autokey.hotkey.tip");
		lbinterval.Text = Loc.T("autokey.interval");
		lbms.Text = Loc.T("autokey.ms");
		bkeycap.Content = Loc.T("autokey.capture");
		bhotcap.Content = Loc.T("autokey.capture");
		bkeyclear.Content = Loc.T("autokey.clear");
		bhotclear.Content = Loc.T("autokey.clear");
		bstart.Content = Loc.T("autokey.start");
		bclose.Content = Loc.T("close");
		lbstat.Text = Loc.T("autokey.idle");
		paintstat(false, false);
	}

	void loadfields() {
		var mode = (opt.AutoKeyMode ?? "key").Trim().ToLowerInvariant();
		rleft.IsChecked = mode == "left";
		rright.IsChecked = mode == "right";
		rmiddle.IsChecked = mode == "middle";
		rkey.IsChecked = mode is not ("left" or "right" or "middle");
		ekey.Text = string.IsNullOrWhiteSpace(opt.AutoKeyKey) ? "Space" : opt.AutoKeyKey.Trim();
		ehotkey.Text = opt.AutoKeyHotkey ?? "";
		einterval.Text = AutoKey.ClampInterval(opt.AutoKeyIntervalMs <= 0
			? AutoKey.INTERVAL_DEFAULT : opt.AutoKeyIntervalMs).ToString();
	}

	void onmode() {
		if (loading) return;
		syncmode();
		commit();
	}

	void syncmode() {
		var key = rkey.IsChecked == true;
		ekey.IsEnabled = key;
		bkeycap.IsEnabled = key;
		bkeyclear.IsEnabled = key;
		ekey.Opacity = key ? 1 : 0.55;
	}

	void startcapture(TextBox box, Button btn) {
		if (box == null || btn == null) return;
		if (capturing && capbox == box) {
			stopcapture(true);
			return;
		}
		if (capturing) stopcapture(true);
		capturing = true;
		capbox = box;
		capbtn = btn;
		capprev = box.Text ?? "";
		box.Text = Loc.T("autokey.press");
		box.IsReadOnly = true;
		btn.Content = Loc.T("cancel");
		box.PreviewKeyDown += oncapkey;
		try { box.Focus(); } catch { }
	}

	void oncapkey(object sender, KeyEventArgs e) {
		if (!capturing || capbox == null) return;
		e.Handled = true;
		var key = e.Key == Key.System ? e.SystemKey : e.Key;
		if (key == Key.Escape) {
			stopcapture(true);
			return;
		}
		if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
			or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
			or Key.System)
			return;
		if (key is Key.Back or Key.Delete) {
			capbox.Text = "";
			stopcapture(false);
			commit();
			return;
		}
		var text = GlobalHotkey.Format(Keyboard.Modifiers, key);
		if (string.IsNullOrEmpty(text)) return;
		capbox.Text = text;
		stopcapture(false);
		commit();
	}

	void stopcapture(bool restore) {
		if (!capturing) return;
		var box = capbox;
		var btn = capbtn;
		capturing = false;
		capbox = null;
		capbtn = null;
		if (box != null) {
			box.PreviewKeyDown -= oncapkey;
			box.IsReadOnly = false;
			if (restore || box.Text == Loc.T("autokey.press"))
				box.Text = capprev ?? "";
		}
		if (btn != null) btn.Content = Loc.T("autokey.capture");
		capprev = "";
	}

	void commit() {
		if (loading || opt == null) return;
		var mode = rleft.IsChecked == true ? "left"
			: rright.IsChecked == true ? "right"
			: rmiddle.IsChecked == true ? "middle" : "key";
		var key = (ekey.Text ?? "").Trim();
		if (key == Loc.T("autokey.press")) key = capprev ?? "";
		var hk = (ehotkey.Text ?? "").Trim();
		if (hk == Loc.T("autokey.press")) hk = capprev ?? "";
		var iv = AutoKey.ParseInterval(einterval.Text);
		einterval.Text = iv.ToString();
		var hkChanged = !string.Equals(opt.AutoKeyHotkey ?? "", hk, StringComparison.OrdinalIgnoreCase);
		opt.AutoKeyMode = mode;
		opt.AutoKeyKey = key;
		opt.AutoKeyHotkey = hk;
		opt.AutoKeyIntervalMs = iv;
		try { AppConfig.Save(opt); } catch { }
		if (hkChanged) {
			try { onhotkey?.Invoke(); } catch { }
		}
	}
}
