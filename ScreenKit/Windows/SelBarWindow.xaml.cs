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
	internal event Action TrClick;
	internal event Action CopyClick;
	internal event Action CloseClick;
	internal event Action TurnOffClick;
	internal event Action<string> LangChanged;

	bool langsync;
	ToggleButton[] langs;
	const double MINW = 296;
	const double MINH = 168;
	static bool sized;
	static double keepW, keepH;

	public SelBarWindow() {
		InitializeComponent();
		langs = new[] { bselfall, bselfzh, bselfja, bselfko, bselfen };
		wirelang(bselfall, "");
		wirelang(bselfzh, "zh");
		wirelang(bselfja, "ja");
		wirelang(bselfko, "ko");
		wirelang(bselfen, "en");
		bselspeak.Click += (_, _) => SpeakClick?.Invoke();
		bselsearch.Click += (_, _) => SearchClick?.Invoke();
		bseltr.Click += (_, _) => TrClick?.Invoke();
		bselcopy.Click += (_, _) => CopyClick?.Invoke();
		bselclose.Click += (_, _) => CloseClick?.Invoke();
		bselturnoff.Click += (_, _) => TurnOffClick?.Invoke();
		lsel.PreviewMouseWheel += (_, e) => scrolllist(lsel, e);
		bgrip.DragDelta += (_, e) => gripmove(e.HorizontalChange, e.VerticalChange);
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
		bselspeak.Content = Loc.T("dict.speak");
		bselsearch.Content = Loc.T("dict.sel.search");
		bseltr.Content = Loc.T("dict.sel.translate");
		bselcopy.Content = Loc.T("dict.sel.copy");
		bselclose.ToolTip = Loc.T("dict.sel.close");
		bselturnoff.Content = Loc.T("dict.sel.turnoff");
		bgrip.ToolTip = Loc.T("dict.sel.resize");
		lbselwait.Text = Loc.T("dict.sel.searching");
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
		}
		finally { langsync = false; }
	}

	internal string SearchLabel => bselsearch.Content as string ?? "";

	internal void ShowRows(List<DictSelRow> rows, bool searching) {
		if (rows == null) rows = new List<DictSelRow>();
		lbselwait.Visibility = searching && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		lsel.ItemsSource = rows;
		lsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		spsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		if (rows.Count > 0) lsel.ScrollIntoView(rows[0]);
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

	/// <summary>浮窗放在选区物理矩形下方。放不下时改到上方。不激活，也不盖住选区。</summary>
	public void ShowAt(Rect px) {
		if (px.Width < 1) px.Width = 1;
		if (px.Height < 1) px.Height = 1;
		applysize();
		if (!IsVisible) {
			Left = -32000;
			Top = -32000;
			Show();
		}
		UpdateLayout();
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
			handled = true;
			return (IntPtr)MA_NOACTIVATE;
		}
		return IntPtr.Zero;
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
