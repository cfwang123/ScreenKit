using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace ScreenKit;

/// <summary>外部划词的查词浮窗。置顶、不激活、不进任务栏。内容和词典里的选区浮窗一样。</summary>
public partial class SelBarWindow : Window {
	const int GWL_EXSTYLE = -20;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WM_MOUSEACTIVATE = 0x0021;
	const int MA_NOACTIVATE = 3;
	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HwndTopmost = new(-1);

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	internal event Action<DictSelRow> HitClick;
	internal event Action SpeakClick;
	internal event Action SearchClick;
	internal event Action CopyClick;
	internal event Action CloseClick;
	internal event Action<bool> SelOnChanged;
	internal event Action<string> LangChanged;
	internal event Action TrGo;
	internal event Action TrSrcChanged;
	internal event Action TrDstChanged;
	internal event Action TrTextChanged;
	internal event Action<bool> TrSpeak;
	internal event Action ReadGo;
	internal event Action ReadStop;

	bool selonsync;
	bool trsync;
	bool trmode;
	bool readmode;
	bool readsync;
	bool readinit;

	bool langsync;
	ToggleButton[] langs;
	const double MINW = 296;
	const double MINH = 168;
	static bool sized;
	static double keepW, keepH, seenW, seenH;
	bool userpos;
	bool dragging;
	Rect anchor;
	Point dragPx;
	double dragLeft, dragTop;

	public SelBarWindow() {
		InitializeComponent();
		langs = new[] { bselfall, bselfzh, bselfja, bselfko, bselfen, bselftr, bselread };
		wirelang(bselfall, "");
		wirelang(bselfzh, "zh");
		wirelang(bselfja, "ja");
		wirelang(bselfko, "ko");
		wirelang(bselfen, "en");
		wirelang(bselftr, "tr");
		wirelang(bselread, "read");
		bselspeak.Click += (_, _) => SpeakClick?.Invoke();
		bselsearch.Click += (_, _) => SearchClick?.Invoke();
		bselcopy.Click += (_, _) => CopyClick?.Invoke();
		btrgo.Click += (_, _) => TrGo?.Invoke();
		btrspeaksrc.Click += (_, _) => TrSpeak?.Invoke(true);
		btrspeakdst.Click += (_, _) => TrSpeak?.Invoke(false);
		etrsrc.SelectionChanged += (_, _) => { if (!trsync) TrSrcChanged?.Invoke(); };
		etrdst.SelectionChanged += (_, _) => { if (!trsync) TrDstChanged?.Invoke(); };
		etrsrctext.TextChanged += (_, _) => { if (!trsync) TrTextChanged?.Invoke(); };
		etrsrctext.LostKeyboardFocus += (_, _) => restorenonactivate();
		ereadtext.LostKeyboardFocus += (_, _) => restorenonactivate();
		ereadeng.SelectionChanged += (_, _) => { if (!readsync && readinit) onreadeng(); };
		ereadlang.SelectionChanged += (_, _) => { if (!readsync && readinit) FillReadVoices(); };
		breadgo.Click += (_, _) => ReadGo?.Invoke();
		breadstop.Click += (_, _) => ReadStop?.Invoke();
		ereadrate.ValueChanged += (_, _) => breadrate.Content = ratelabel(ereadrate.Value);
		breadrate.PreviewMouseLeftButtonDown += (_, e) => {
			if (!poprate.IsOpen) return;
			poprate.IsOpen = false;
			e.Handled = true;
		};
		breadrate.Click += (_, _) => {
			if (!poprate.IsOpen) poprate.IsOpen = true;
		};
		poprate.Opened += (_, _) => clearnoactivate();
		poprate.Closed += (_, _) => restorenonactivate();
		bselclose.Click += (_, _) => CloseClick?.Invoke();
		cbselon.Checked += (_, _) => { if (!selonsync) SelOnChanged?.Invoke(true); };
		cbselon.Unchecked += (_, _) => { if (!selonsync) SelOnChanged?.Invoke(false); };
		lsel.PreviewMouseWheel += (_, e) => scrolllist(lsel, e);
		bgrip.DragDelta += (_, e) => gripmove(e.HorizontalChange, e.VerticalChange);
		PreviewMouseLeftButtonDown += dragdown;
		PreviewMouseMove += dragmove;
		PreviewMouseLeftButtonUp += dragup;
		lsel.PreviewMouseLeftButtonUp += (_, e) => {
			var row = rowat(e.OriginalSource as DependencyObject);
			if (row != null) HitClick?.Invoke(row);
		};
		ApplyText();
		ShowRows(null, true);
		SourceInitialized += (_, _) => {
			var src = (HwndSource)PresentationSource.FromVisual(this);
			src.AddHook(onmsg);
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd == IntPtr.Zero) return;
			var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
		};
	}

	public void ApplyText() {
		bselspeak.ToolTip = Loc.T("dict.speak");
		bselsearch.Content = Loc.T("dict.sel.search");
		bselcopy.Content = Loc.T("dict.sel.copy");
		bselftr.Content = Loc.T("dict.sel.translate");
		bselread.Content = Loc.T("tts.speak");
		breadgo.Content = Loc.T("tts.speak");
		breadstop.Content = Loc.T("tts.stop");
		breadrate.ToolTip = Loc.T("tts.rate");
		btrgo.Content = Loc.T("tr.go");
		btrspeaksrc.ToolTip = Loc.T("tr.popup.speak.tip");
		btrspeakdst.ToolTip = Loc.T("tr.popup.speak.tip");
		lbsrc.Text = Loc.T("tr.src.text");
		lbdst.Text = Loc.T("tr.dst.text");
		etrsrc.ToolTip = Loc.T("tr.src.tip");
		etrdst.ToolTip = Loc.T("tr.dst.tip");
		bselclose.ToolTip = Loc.T("dict.sel.close");
		cbselon.Content = Loc.T("dict.selbar");
		cbselon.ToolTip = Loc.T("dict.selbar.tip");
		bgrip.ToolTip = Loc.T("dict.sel.resize");
		lbselwait.Text = Loc.T("dict.sel.searching");
		lbselempty.Text = Loc.T("dict.sel.empty");
		bselfall.Content = Loc.T("dict.filter.all");
		bselfzh.Content = Loc.T("dict.lab.zh");
		bselfja.Content = Loc.T("dict.lab.ja");
		bselfko.Content = Loc.T("dict.lab.ko");
		bselfen.Content = Loc.T("dict.lab.en");
	}

	internal void SetLang(string dict) {
		langsync = true;
		try {
			bselfall.IsChecked = string.IsNullOrEmpty(dict);
			bselfzh.IsChecked = dict == "zh";
			bselfja.IsChecked = dict == "ja";
			bselfko.IsChecked = dict == "ko";
			bselfen.IsChecked = dict == "en";
			bselftr.IsChecked = dict == "tr";
			bselread.IsChecked = dict == "read";
		}
		finally { langsync = false; }
	}

	internal string SearchLabel => bselsearch.Content as string ?? "";

	internal void SetSelOn(bool on) {
		selonsync = true;
		try { cbselon.IsChecked = on; }
		finally { selonsync = false; }
	}

	internal void ShowRows(List<DictSelRow> rows, bool searching) {
		if (trmode || readmode) return;
		if (rows == null) rows = new List<DictSelRow>();
		lbselwait.Visibility = searching && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		lbselempty.Visibility = !searching && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		lsel.ItemsSource = rows;
		lsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		if (rows.Count > 0) lsel.ScrollIntoView(rows[0]);
	}

	internal void ShowDict() {
		trmode = false;
		readmode = false;
		gdict.Visibility = Visibility.Visible;
		gtr.Visibility = Visibility.Collapsed;
		gread.Visibility = Visibility.Collapsed;
		poprate.IsOpen = false;
	}

	internal void ShowTr(string text, bool setText) {
		trmode = true;
		readmode = false;
		gdict.Visibility = Visibility.Collapsed;
		gtr.Visibility = Visibility.Visible;
		gread.Visibility = Visibility.Collapsed;
		poprate.IsOpen = false;
		if (!setText) return;
		trsync = true;
		try { etrsrctext.Text = text ?? ""; }
		finally { trsync = false; }
	}

	internal void SetTrLabels(string src, string dst) {
		lbsrclang.Text = src ?? "";
		lbdstlang.Text = dst ?? "";
	}

	internal string TrSrcCode => combo(etrsrc);
	internal string TrDstCode => combo(etrdst);
	internal void ShowRead(string text, bool setText, string langHint) {
		trmode = false;
		readmode = true;
		gdict.Visibility = Visibility.Collapsed;
		gtr.Visibility = Visibility.Collapsed;
		gread.Visibility = Visibility.Visible;
		if (!readinit) fillread(langHint);
		if (!setText) return;
		readsync = true;
		try { ereadtext.Text = text ?? ""; }
		finally { readsync = false; }
	}

	internal void HighlightRead(int start, int end) {
		if (end <= start) return;
		try {
			clearnoactivate();
			Activate();
			ereadtext.Focus();
			ereadtext.Select(start, end - start);
			scrollread(start, end);
		}
		catch { }
	}

	internal double ReadRate => DictTts.NormRate(ereadrate.Value);

	static string ratelabel(double v) {
		v = DictTts.NormRate(v);
		var n = (int)Math.Round(v * 10);
		if (n % 10 == 0) return $"{n / 10}x";
		return $"{n / 10}.{n % 10}x";
	}

	void clearnoactivate() {
		var hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero) return;
		var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
		SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_NOACTIVATE);
	}

	internal void ClearReadMark() {
		try {
			var c = ereadtext.CaretIndex;
			ereadtext.Select(c, 0);
		}
		catch { }
		restorenonactivate();
	}

	void scrollread(int start, int end) {
		try {
			ereadtext.UpdateLayout();
			var r0 = ereadtext.GetRectFromCharacterIndex(start);
			if (r0 == Rect.Empty) return;
			var last = Math.Max(start, end - 1);
			var r1 = ereadtext.GetRectFromCharacterIndex(last, true);
			var top = r0.Top;
			var bottom = r1 == Rect.Empty ? r0.Bottom : Math.Max(r0.Bottom, r1.Bottom);
			var viewH = ereadtext.ViewportHeight;
			if (viewH <= 1) viewH = ereadtext.ActualHeight;
			const double pad = 2;
			if (top >= -pad && bottom <= viewH + pad) return;
			var next = ereadtext.VerticalOffset + top;
			if (next < 0) next = 0;
			ereadtext.ScrollToVerticalOffset(next);
		}
		catch { }
	}

	internal string ReadText => ereadtext.Text ?? "";
	internal string ReadEngine => tagof(ereadeng);
	internal string ReadLang => tagof(ereadlang);
	internal string ReadVoice => tagof(ereadvoice);

	void onreadeng() {
		var auto = DictTts.NormEngine(ReadEngine) == DictTts.AUTO;
		ereadvoice.IsEnabled = !auto;
		breadrate.IsEnabled = !auto;
		ereadvoice.ToolTip = auto ? Loc.T("dict.sel.read.auto.tip") : null;
		breadrate.ToolTip = auto ? Loc.T("dict.sel.read.auto.tip") : Loc.T("tts.rate");
		if (auto) {
			if (poprate.IsOpen) poprate.IsOpen = false;
			ereadvoice.Items.Clear();
			return;
		}
		FillReadVoices();
	}

	internal void FillReadVoices() {
		if (DictTts.NormEngine(ReadEngine) == DictTts.AUTO) {
			onreadeng();
			return;
		}
		var lang = ReadLang;
		if (lang != "zh" && lang != "en" && lang != "ja" && lang != "ko") lang = "zh";
		var eng = DictTts.NormEngine(ReadEngine);
		var use = eng;
		var keep = ReadVoice;
		var items = DictTts.Voices(use, lang);
		readsync = true;
		try {
			ereadvoice.Items.Clear();
			foreach (var it in items)
				ereadvoice.Items.Add(new ComboBoxItem { Content = it.Label, Tag = it.Id ?? "" });
			if (!pickraw(ereadvoice, keep) && ereadvoice.Items.Count > 0) ereadvoice.SelectedIndex = 0;
		}
		finally { readsync = false; }
	}

	void fillread(string langHint) {
		readsync = true;
		try {
			ereadeng.Items.Clear();
			addtag(ereadeng, Loc.T("dict.tts.auto"), DictTts.AUTO);
			addtag(ereadeng, Loc.T("dict.tts.onnx"), DictTts.ONNX);
			addtag(ereadeng, Loc.T("dict.tts.sapi"), DictTts.SAPI);
			addtag(ereadeng, Loc.T("dict.tts.winrt"), DictTts.WINRT);
			addtag(ereadeng, Loc.T("dict.tts.edge"), DictTts.EDGE);
			addtag(ereadeng, Loc.T("dict.tts.chrome"), DictTts.CHROME);
			pickraw(ereadeng, DictTts.AUTO);
			ereadlang.Items.Clear();
			addtag(ereadlang, Loc.T("dict.filter.zh"), "zh");
			addtag(ereadlang, Loc.T("dict.filter.en"), "en");
			addtag(ereadlang, Loc.T("dict.filter.ja"), "ja");
			addtag(ereadlang, Loc.T("dict.filter.ko"), "ko");
			var lang = langHint ?? "";
			if (lang != "zh" && lang != "en" && lang != "ja" && lang != "ko") lang = "zh";
			pickraw(ereadlang, lang);
		}
		finally { readsync = false; }
		readinit = true;
		onreadeng();
	}

	static void addtag(ComboBox box, string label, string tag) {
		box.Items.Add(new ComboBoxItem { Content = label, Tag = tag ?? "" });
	}

	static string tagof(ComboBox box) {
		if (box?.SelectedItem is ComboBoxItem it && it.Tag is string s) return s;
		return "";
	}

	static bool pickraw(ComboBox box, string tag) {
		if (box == null) return false;
		tag = tag ?? "";
		foreach (ComboBoxItem it in box.Items) {
			if (it.Tag is string t && string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) {
				box.SelectedItem = it;
				return true;
			}
		}
		return false;
	}

	internal string TrSrcText => etrsrctext.Text ?? "";
	internal string TrDstText => etrdsttext.Text ?? "";

	internal void SetTrOut(string text) {
		etrdsttext.Text = text ?? "";
	}

	internal void SetTrLangs(List<string> srcs, List<string> dsts, bool keep) {
		var srcKeep = keep ? combo(etrsrc) : TrLang.Auto;
		var dstKeep = keep ? combo(etrdst) : TrLang.Auto;
		if (dstKeep.Length == 0) dstKeep = TrLang.Auto;
		trsync = true;
		try {
			etrsrc.Items.Clear();
			etrsrc.Items.Add(new ComboBoxItem { Content = Loc.T("lang.auto"), Tag = TrLang.Auto });
			if (srcs != null) {
				foreach (var s in srcs) {
					if (string.IsNullOrEmpty(s) || s == TrLang.Auto) continue;
					etrsrc.Items.Add(new ComboBoxItem { Content = TrLang.Label(s), Tag = s });
				}
			}
			if (!pick(etrsrc, srcKeep) && etrsrc.Items.Count > 0) etrsrc.SelectedIndex = 0;
			filldst(dsts, dstKeep);
		}
		finally { trsync = false; }
	}

	internal void SetTrTargets(List<string> dsts, string prefer) {
		trsync = true;
		try { filldst(dsts, prefer ?? ""); }
		finally { trsync = false; }
	}

	void filldst(List<string> dsts, string prefer) {
		etrdst.Items.Clear();
		etrdst.Items.Add(new ComboBoxItem { Content = Loc.T("lang.auto"), Tag = TrLang.Auto });
		if (dsts != null) {
			foreach (var s in dsts) {
				if (string.IsNullOrEmpty(s) || s == TrLang.Auto) continue;
				etrdst.Items.Add(new ComboBoxItem { Content = TrLang.Label(s), Tag = s });
			}
		}
		if (!pick(etrdst, prefer)) etrdst.SelectedIndex = 0;
	}

	static string combo(ComboBox box) {
		if (box?.SelectedItem is ComboBoxItem it && it.Tag is string s) return TrLang.Normalize(s);
		return "";
	}

	static bool pick(ComboBox box, string code) {
		if (box == null || string.IsNullOrEmpty(code)) return false;
		code = TrLang.Normalize(code);
		foreach (ComboBoxItem it in box.Items) {
			if (it.Tag is string t && string.Equals(TrLang.Normalize(t), code, StringComparison.OrdinalIgnoreCase)) {
				box.SelectedItem = it;
				return true;
			}
		}
		return false;
	}

	void wirelang(ToggleButton b, string dict) {
		b.Checked += (_, _) => {
			if (langsync) return;
			langsync = true;
			foreach (var o in langs)
				if (!ReferenceEquals(o, b)) o.IsChecked = false;
			langsync = false;
			LangChanged?.Invoke(dict);
		};
		b.Unchecked += (_, _) => {
			if (langsync) return;
			foreach (var o in langs)
				if (o.IsChecked == true) return;
			langsync = true;
			b.IsChecked = true;
			langsync = false;
		};
	}

	static void scrolllist(ListBox box, MouseWheelEventArgs e) {
		var sv = scrollof(box);
		if (sv == null) return;
		sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
		e.Handled = true;
	}

	static ScrollViewer scrollof(DependencyObject d) {
		if (d == null) return null;
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) {
			var c = VisualTreeHelper.GetChild(d, i);
			if (c is ScrollViewer sv) return sv;
			var inner = scrollof(c);
			if (inner != null) return inner;
		}
		return null;
	}

	internal void UseSize(int w, int h) {
		if (sized) return;
		if (w < MINW || h < MINH || w > 2400 || h > 1800) return;
		sized = true;
		keepW = w;
		keepH = h;
	}

	internal void CopySize(out double w, out double h) {
		if (sized) {
			w = keepW;
			h = keepH;
			return;
		}
		w = seenW;
		h = seenH;
	}

	/// <summary>本次运行里用户拖过的宽高。没拖过则仍按内容撑开。</summary>
	void applysize() {
		if (!sized) return;
		SizeToContent = SizeToContent.Manual;
		bdpop.MaxWidth = double.PositiveInfinity;
		lsel.MaxHeight = double.PositiveInfinity;
		rowhits.Height = new GridLength(1, GridUnitType.Star);
		Width = keepW;
		Height = keepH;
	}

	void gripmove(double dx, double dy) {
		if (!sized) {
			SizeToContent = SizeToContent.Manual;
			bdpop.MaxWidth = double.PositiveInfinity;
			lsel.MaxHeight = double.PositiveInfinity;
			rowhits.Height = new GridLength(1, GridUnitType.Star);
			Width = Math.Max(MINW, ActualWidth);
			Height = Math.Max(MINH, ActualHeight);
		}
		var w = Math.Max(MINW, Width + dx);
		var h = Math.Max(MINH, Height + dy);
		Width = w;
		Height = h;
		sized = true;
		keepW = w;
		keepH = h;
	}

	void dragdown(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton != MouseButton.Left) return;
		if (dragblocked(e.OriginalSource as DependencyObject)) return;
		dragging = true;
		var p = Forms.Cursor.Position;
		dragPx = new Point(p.X, p.Y);
		dragLeft = Left;
		dragTop = Top;
		CaptureMouse();
		e.Handled = true;
	}

	void dragmove(object sender, MouseEventArgs e) {
		if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
		var p = Forms.Cursor.Position;
		var src = PresentationSource.FromVisual(this);
		var m = src?.CompositionTarget == null ? Matrix.Identity : src.CompositionTarget.TransformFromDevice;
		var mx = m.M11 == 0 ? 1 : m.M11;
		var my = m.M22 == 0 ? 1 : m.M22;
		Left = dragLeft + (p.X - dragPx.X) * mx;
		Top = dragTop + (p.Y - dragPx.Y) * my;
		userpos = true;
	}

	void dragup(object sender, MouseButtonEventArgs e) {
		if (!dragging) return;
		dragging = false;
		try { ReleaseMouseCapture(); } catch { }
	}

	static bool dragblocked(DependencyObject d) {
		while (d != null) {
			if (d is Button or ToggleButton or CheckBox or Thumb or TextBox or ComboBox
				or ListBoxItem or ScrollBar or RepeatButton)
				return true;
			d = VisualTreeHelper.GetParent(d);
		}
		return false;
	}

	static bool samebox(Rect a, Rect b) {
		return Math.Abs(a.Left - b.Left) < 4 && Math.Abs(a.Top - b.Top) < 4
			&& Math.Abs(a.Width - b.Width) < 4 && Math.Abs(a.Height - b.Height) < 4;
	}

	/// <summary>浮窗放在选区物理矩形下方。放不下时改到上方。不激活，也不盖住选区。拖动过则保持位置，直到选区换了。</summary>
	public void ShowAt(Rect px) {
		if (px.Width < 1) px.Width = 1;
		if (px.Height < 1) px.Height = 1;
		var was = IsVisible;
		applysize();
		if (!was) {
			userpos = false;
			Left = -32000;
			Top = -32000;
			Show();
		}
		UpdateLayout();
		if (was && userpos && samebox(anchor, px)) {
			anchor = px;
			return;
		}
		userpos = false;
		anchor = px;
		if (!sized && ActualWidth >= MINW && ActualHeight >= MINH) {
			seenW = ActualWidth;
			seenH = ActualHeight;
		}
		int apx = (int)Math.Round(px.Left);
		int apy = (int)Math.Round(px.Bottom);
		var dip = todip(apx, apy);
		double w = ActualWidth;
		double h = ActualHeight;
		const int gap = 6;
		double left = dip.X;
		double top = dip.Y + gap;
		var area = Forms.Screen.FromPoint(new System.Drawing.Point(apx, apy)).WorkingArea;
		var tl = todip(area.Left, area.Top);
		var br = todip(area.Right, area.Bottom);
		if (top + h > br.Y) {
			var above = todip(apx, (int)Math.Round(px.Top)).Y - h - gap;
			if (above >= tl.Y) top = above;
			else top = Math.Max(tl.Y, dip.Y - h - gap);
		}
		if (left + w > br.X) left = Math.Max(tl.X, br.X - w);
		if (left < tl.X) left = tl.X;
		if (top < tl.Y) top = tl.Y;
		Left = left;
		Top = top;
	}

	Point todip(int px, int py) {
		var src = PresentationSource.FromVisual(this);
		if (src?.CompositionTarget == null) return new Point(px, py);
		return src.CompositionTarget.TransformFromDevice.Transform(new Point(px, py));
	}

	internal Rect ScreenPx() {
		var hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rc)) return Rect.Empty;
		return new Rect(rc.Left, rc.Top, Math.Max(0, rc.Right - rc.Left), Math.Max(0, rc.Bottom - rc.Top));
	}

	internal static bool HasNoActivate(IntPtr hwnd) {
		if (hwnd == IntPtr.Zero) return false;
		return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_NOACTIVATE) != 0;
	}

	static DictSelRow rowat(DependencyObject d) {
		while (d != null && d is not ListBoxItem)
			d = VisualTreeHelper.GetParent(d);
		return d is ListBoxItem li ? li.Content as DictSelRow : null;
	}

	IntPtr onmsg(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == WM_MOUSEACTIVATE) {
			if ((trmode && overbox(etrsrctext)) || (readmode && overbox(ereadtext))) {
				var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
				SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_NOACTIVATE);
				return IntPtr.Zero;
			}
			handled = true;
			return (IntPtr)MA_NOACTIVATE;
		}
		return IntPtr.Zero;
	}

	void restorenonactivate() {
		var hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd == IntPtr.Zero) return;
		var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
		SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
	}

	bool overbox(FrameworkElement el) {
		if (el == null || !el.IsVisible) return false;
		try {
			var p = el.PointFromScreen(new Point(Forms.Cursor.Position.X, Forms.Cursor.Position.Y));
			return p.X >= 0 && p.Y >= 0 && p.X <= el.ActualWidth && p.Y <= el.ActualHeight;
		}
		catch { return false; }
	}

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int Left, Top, Right, Bottom;
	}
}

/// <summary>词头列最多占所在行的一半。</summary>
public sealed class SelHalfWidth : System.Windows.Data.IValueConverter {
	public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
		var w = value is double d ? d : 0;
		if (w < 16) return 160.0;
		return Math.Max(48.0, w * 0.5 - 8);
	}

	public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
		return System.Windows.Data.Binding.DoNothing;
	}
}

/// <summary>划词翻译页的可选语言。Native 是母语。</summary>
sealed class SelTrPack {
	public List<string> Sources = new();
	public List<string> Targets = new();
	public string Native = "zh";
}
