using System.Runtime.InteropServices;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;
namespace ScreenKit;

/// <summary>输入法候选条。不激活。编码在上，候选在下，右侧翻页。点数字或词即可上屏。</summary>
sealed class HangulCand : Window {
	const int GWL_EXSTYLE = -20;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WM_MOUSEACTIVATE = 0x0021;
	const int MA_NOACTIVATE = 3;
	const uint SWP_NOSIZE = 0x0001;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HwndTopmost = new(-1);

	readonly TextBlock pre;
	readonly Border caret;
	readonly StackPanel items;
	readonly TextBlock badge;
	readonly Border box;
	Rect lastCaretPx;

	public event Action<int> Picked;
	public event Action<int> Paged;

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	public HangulCand() {
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		ShowInTaskbar = false;
		ShowActivated = false;
		Topmost = true;
		SizeToContent = SizeToContent.WidthAndHeight;
		ResizeMode = ResizeMode.NoResize;
		Focusable = false;
		FontFamily = new FontFamily("Microsoft YaHei UI");
		box = new Border {
			Background = Brushes.White,
			BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(2),
			Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.18, Color = Colors.Black }
		};
		var root = new Grid();
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		pre = new TextBlock {
			FontSize = 13, FontWeight = FontWeights.Bold,
			Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center
		};
		caret = new Border {
			Width = 1, Height = 13, Margin = new Thickness(1, 0, 0, 0),
			Background = new SolidColorBrush(Color.FromRgb(226, 61, 61)),
			VerticalAlignment = VerticalAlignment.Center
		};
		var typing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		typing.Children.Add(pre);
		typing.Children.Add(caret);
		badge = new TextBlock {
			Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold,
			HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
		};
		var badgeBox = new Border {
			Width = 18, Height = 18, CornerRadius = new CornerRadius(3),
			Background = new SolidColorBrush(Color.FromRgb(61, 181, 74)),
			Margin = new Thickness(10, 0, 0, 0),
			Child = badge, VerticalAlignment = VerticalAlignment.Center
		};
		var top = new Grid { Margin = new Thickness(8, 2, 6, 2) };
		top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		top.Children.Add(typing);
		Grid.SetColumn(badgeBox, 1);
		top.Children.Add(badgeBox);
		var line = new Border {
			Height = 1, Background = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
			Margin = new Thickness(1, 0, 1, 0)
		};
		items = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		var chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
		chrome.Children.Add(pagebtn("‹", -1));
		chrome.Children.Add(pagebtn("›", 1));
		var bottom = new Grid { Margin = new Thickness(10, 4, 6, 5) };
		bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		bottom.Children.Add(items);
		Grid.SetColumn(chrome, 1);
		bottom.Children.Add(chrome);
		Grid.SetRow(line, 1);
		Grid.SetRow(bottom, 2);
		root.Children.Add(top);
		root.Children.Add(line);
		root.Children.Add(bottom);
		box.Child = root;
		Content = box;
		SourceInitialized += (_, _) => {
			var src = (HwndSource)PresentationSource.FromVisual(this);
			if (src == null) return;
			src.AddHook(onmsg);
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd == IntPtr.Zero) return;
			var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
		};
	}

	Button pagebtn(string text, int delta) {
		var b = new Button {
			Content = text, Width = 16, Height = 20, Margin = new Thickness(0),
			Background = Brushes.Transparent, BorderThickness = new Thickness(0),
			Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
			FontSize = 12, Padding = new Thickness(0), Focusable = false
		};
		b.Click += (_, _) => Paged?.Invoke(delta);
		return b;
	}

	public void ShowEngine(ImeEngine s, Rect caretPx) {
		pre.Text = s.Preedit;
		bool typing = s.Preedit.Length > 0;
		pre.Visibility = typing ? Visibility.Visible : Visibility.Collapsed;
		caret.Visibility = typing ? Visibility.Visible : Visibility.Collapsed;
		badge.Text = s.Badge;
		items.Children.Clear();
		int n = 1;
		foreach (var c in s.PageItems) {
			items.Children.Add(candrow(n, c));
			n++;
		}
		if (!s.ShowBar) { HideBar(); return; }
		if (!caretPx.IsEmpty) lastCaretPx = caretPx;
		else if (!lastCaretPx.IsEmpty) caretPx = lastCaretPx;
		if (!IsVisible) Show();
		UpdateLayout();
		place(caretPx);
	}

	public void HideBar() {
		try { if (IsVisible) Hide(); } catch { }
	}

	/// <summary>候选条是不是正显示着（自测用）。</summary>
	public bool Shown => IsVisible;

	TextBlock candrow(int n, Cand c) {
		int index = n;
		var row = new TextBlock {
			Margin = new Thickness(0, 0, 14, 0),
			VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand
		};
		row.Inlines.Add(new Run(n + ".") {
			Foreground = n == 1
				? new SolidColorBrush(Color.FromRgb(226, 61, 61))
				: new SolidColorBrush(Color.FromRgb(153, 153, 153)),
			FontSize = 15
		});
		row.Inlines.Add(new Run(c.Text) {
			Foreground = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
			FontSize = 15
		});
		if (!string.IsNullOrEmpty(c.Extra))
			row.Inlines.Add(new Run(c.Extra) {
				Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
				FontSize = 13
			});
		if (!string.IsNullOrEmpty(c.Comment))
			row.Inlines.Add(new Run("  " + c.Comment) {
				Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
				FontSize = 13
			});
		row.MouseLeftButtonDown += (_, e) => {
			e.Handled = true;
			Picked?.Invoke(index - 1);
		};
		return row;
	}

	/// <summary>离屏先建出来、真渲染一遍：第一次显示要 300ms 上下，赶在打字时会把键盘钩子拖过系统超时，钩子被摘掉就“按键没反应”。</summary>
	public void Warmup() {
		if (!Dispatcher.CheckAccess()) {
			try { Dispatcher.Invoke(new Action(Warmup)); } catch { }
			return;
		}
		try {
			pre.Text = "wubi";
			pre.Visibility = Visibility.Visible;
			caret.Visibility = Visibility.Visible;
			badge.Text = "五";
			items.Children.Clear();
			for (int i = 1; i <= 4; i++)
				items.Children.Add(candrow(i, new Cand { Text = "候选" + i }));
			Show();
			UpdateLayout();
			if (ActualWidth >= 1 && ActualHeight >= 1) {
				var rtb = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
				rtb.Render(box);
			}
			Hide();
			items.Children.Clear();
			pre.Text = "";
		}
		catch { }
	}

	void place(Rect caretPx) {
		int px, py, gap = 4;
		if (caretPx.IsEmpty) {
			var wa = Forms.SystemInformation.WorkingArea;
			px = wa.Left + 24;
			py = wa.Bottom - 80;
		} else {
			px = (int)Math.Round(caretPx.Left);
			py = (int)Math.Round(caretPx.Top);
		}
		if (!IsVisible) {
			Left = -32000;
			Top = -32000;
		}
		UpdateLayout();
		var dip = todip(px, py);
		double w = ActualWidth;
		double h = ActualHeight;
		double left = dip.X;
		double top = dip.Y + gap;
		var area = Forms.Screen.FromPoint(new System.Drawing.Point(px, py)).WorkingArea;
		var tl = todip(area.Left, area.Top);
		var br = todip(area.Right, area.Bottom);
		if (left + w > br.X) left = Math.Max(tl.X, br.X - w);
		if (top + h > br.Y) top = Math.Max(tl.Y, dip.Y - h - 28);
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

	IntPtr onmsg(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == WM_MOUSEACTIVATE) {
			handled = true;
			return (IntPtr)MA_NOACTIVATE;
		}
		return IntPtr.Zero;
	}
}
