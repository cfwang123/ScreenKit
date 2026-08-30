using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfButton = System.Windows.Controls.Button;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfLine = System.Windows.Shapes.Line;
using WpfPath = System.Windows.Shapes.Path;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfSize = System.Windows.Size;
using Shape = System.Windows.Shapes.Shape;

namespace ScreenKit;

/// <summary>
/// 截图后标注：矩形 / 线 / 箭头 / 文字，保存或复制。
/// </summary>
partial class AnnotateWindow : Window {
	enum Tool { None, Rect, Line, Arrow, Text }

	const string TEXT_TAG = "annotext";

	readonly BitmapSource source;
	readonly List<UIElement> strokes = new();
	Tool tool = Tool.Rect;
	bool drawing;
	Point start;
	Shape draft;
	int imgW, imgH;
	Border selText;
	Border editHost;
	string textEditBackup = "";
	bool textEditIsNew;
	bool textDrag;
	Point textDragMouse, textDragOrigin;

	/// <summary>用户最终确认的图（保存/复制时的合成图）；关闭未操作则为 null。</summary>
	public BitmapSource ResultImage { get; private set; }

	public AnnotateWindow(BitmapSource image) {
		InitializeComponent();
		source = image ?? throw new ArgumentNullException(nameof(image));
		imgW = source.PixelWidth;
		imgH = source.PixelHeight;
		imgview.Source = source;
		imgview.Width = imgW;
		imgview.Height = imgH;
		pstage.Width = imgW;
		pstage.Height = imgH;
		pdraw.Width = imgW;
		pdraw.Height = imgH;

		inittools();
		pdraw.MouseLeftButtonDown += ondown;
		pdraw.MouseMove += onmove;
		pdraw.MouseLeftButtonUp += onup;
		bundo.Click += (_, _) => undo();
		bclear.Click += (_, _) => clearall();
		bsave.Click += (_, _) => savefile();
		bcopy.Click += (_, _) => copyclip();
		bclose.Click += (_, _) => { DialogResult = false; Close(); };
		WindowEsc.Attach(this, () => { DialogResult = false; Close(); });
		settool(Tool.Rect);
		applyannlang();
	}

	void applyannlang() {
		Title = Loc.T("ann.title");
		lbanntools.Text = Loc.T("ann.tools");
		trect.Content = Loc.T("ann.rect");
		trect.ToolTip = Loc.T("ann.rect.tip");
		tline.Content = Loc.T("ann.line");
		tline.ToolTip = Loc.T("ann.line.tip");
		tarrow.Content = Loc.T("ann.arrow");
		tarrow.ToolTip = Loc.T("ann.arrow.tip");
		ttext.Content = Loc.T("ann.text");
		ttext.ToolTip = Loc.T("ann.text.tip");
		lbanncolor.Text = Loc.T("ann.color");
		itannred.Content = Loc.T("ann.red");
		itannorange.Content = Loc.T("ann.orange");
		itannyellow.Content = Loc.T("ann.yellow");
		itanngreen.Content = Loc.T("ann.green");
		itannblue.Content = Loc.T("ann.blue");
		itannpurple.Content = Loc.T("ann.purple");
		itannwhite.Content = Loc.T("ann.white");
		itannblack.Content = Loc.T("ann.black");
		lbannfontsize.Text = Loc.T("ann.fontsize");
		lbannthick.Text = Loc.T("ann.thick");
		bundo.Content = Loc.T("ann.undo");
		bclear.Content = Loc.T("ann.clear");
		bsave.Content = Loc.T("tb.save");
		bcopy.Content = Loc.T("copy");
		bclose.Content = Loc.T("close");
	}

	void inittools() {
		void bind(ToggleButton btn, Tool t) {
			btn.Checked += (_, _) => {
				if (btn.IsChecked == true) settool(t);
			};
		}
		bind(trect, Tool.Rect);
		bind(tline, Tool.Line);
		bind(tarrow, Tool.Arrow);
		bind(ttext, Tool.Text);
	}

	void settool(Tool t) {
		tool = t;
		trect.IsChecked = t == Tool.Rect;
		tline.IsChecked = t == Tool.Line;
		tarrow.IsChecked = t == Tool.Arrow;
		ttext.IsChecked = t == Tool.Text;
		pdraw.Cursor = t == Tool.None ? Cursors.Arrow : Cursors.Cross;
		lbhint.Text = t switch {
			Tool.Rect => "拖拽绘制矩形框",
			Tool.Line => "拖拽绘制直线",
			Tool.Arrow => "拖拽绘制箭头",
			Tool.Text => "点击输入文字 · 拖动移动 · 双击再编辑（无背景）",
			_ => "选择工具后在图上标注",
		};
	}

	Color curcolor() {
		var tag = (ecolor.SelectedItem as ComboBoxItem)?.Tag as string ?? "#EF4444";
		try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(tag); }
		catch { return Colors.Red; }
	}

	double curthick() {
		var tag = (ethick.SelectedItem as ComboBoxItem)?.Tag as string;
		return double.TryParse(tag, out var v) ? v : 3;
	}

	double curfontsize() {
		var tag = (efont.SelectedItem as ComboBoxItem)?.Tag as string;
		return double.TryParse(tag, out var v) ? v : 18;
	}

	Brush strokebrush() => new SolidColorBrush(curcolor());

	void ondown(object sender, MouseButtonEventArgs e) {
		if (e.OriginalSource is DependencyObject od && findtexthost(od) != null)
			return;
		if (tool == Tool.Text) {
			addtext(e.GetPosition(pdraw));
			return;
		}
		committextedit();
		cleartextsel();
		if (tool == Tool.None) return;
		start = e.GetPosition(pdraw);
		drawing = true;
		var br = strokebrush();
		var th = curthick();
		draft = tool switch {
			Tool.Rect => new WpfRectangle {
				Stroke = br, StrokeThickness = th, Fill = Brushes.Transparent,
			},
			Tool.Line or Tool.Arrow => new WpfLine {
				Stroke = br, StrokeThickness = th, StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				X1 = start.X, Y1 = start.Y, X2 = start.X, Y2 = start.Y,
			},
			_ => null,
		};
		if (draft is WpfRectangle rc) {
			Canvas.SetLeft(rc, start.X);
			Canvas.SetTop(rc, start.Y);
			rc.Width = 0;
			rc.Height = 0;
		}
		if (draft != null) {
			pdraw.Children.Add(draft);
			pdraw.CaptureMouse();
		}
	}

	void onmove(object sender, MouseEventArgs e) {
		if (!drawing || draft == null) return;
		var p = e.GetPosition(pdraw);
		if (draft is WpfRectangle rc) {
			var x = Math.Min(p.X, start.X);
			var y = Math.Min(p.Y, start.Y);
			Canvas.SetLeft(rc, x);
			Canvas.SetTop(rc, y);
			rc.Width = Math.Abs(p.X - start.X);
			rc.Height = Math.Abs(p.Y - start.Y);
		}
		else if (draft is WpfLine ln) {
			ln.X2 = p.X;
			ln.Y2 = p.Y;
		}
	}

	void onup(object sender, MouseButtonEventArgs e) {
		if (!drawing) return;
		drawing = false;
		try { pdraw.ReleaseMouseCapture(); } catch { }
		var p = e.GetPosition(pdraw);
		if (draft is WpfLine ln && tool == Tool.Arrow) {
			// 换成带箭头的 Path
			pdraw.Children.Remove(ln);
			var arrow = makearrow(start, p, strokebrush(), curthick());
			if (arrow != null) {
				pdraw.Children.Add(arrow);
				strokes.Add(arrow);
			}
			draft = null;
			return;
		}
		if (draft is WpfRectangle rc && (rc.Width < 2 || rc.Height < 2)) {
			pdraw.Children.Remove(rc);
			draft = null;
			return;
		}
		if (draft is WpfLine line) {
			var dx = line.X2 - line.X1;
			var dy = line.Y2 - line.Y1;
			if (Math.Sqrt(dx * dx + dy * dy) < 2) {
				pdraw.Children.Remove(line);
				draft = null;
				return;
			}
		}
		if (draft != null) {
			strokes.Add(draft);
			draft = null;
		}
	}

	static Border findtexthost(DependencyObject d) {
		while (d != null) {
			if (d is Border b && b.Tag as string == TEXT_TAG)
				return b;
			d = VisualTreeHelper.GetParent(d);
		}
		return null;
	}

	void addtext(Point pos) {
		committextedit();
		cleartextsel();
		var host = new Border {
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			BorderBrush = Brushes.Transparent,
			Padding = new Thickness(0),
			Tag = TEXT_TAG,
			Cursor = Cursors.SizeAll,
			SnapsToDevicePixels = true,
		};
		Canvas.SetLeft(host, pos.X);
		Canvas.SetTop(host, pos.Y);
		pdraw.Children.Add(host);
		strokes.Add(host);
		wiretext(host);
		entertextedit(host, isNew: true);
	}

	void wiretext(Border host) {
		if (host == null) return;
		host.MouseLeftButtonDown += (_, e) => {
			if (editHost == host) return;
			if (e.ClickCount >= 2) {
				entertextedit(host, isNew: false);
				e.Handled = true;
				return;
			}
			committextedit();
			selecttext(host);
			textDrag = true;
			textDragMouse = e.GetPosition(pdraw);
			var lx = Canvas.GetLeft(host);
			var ty = Canvas.GetTop(host);
			if (double.IsNaN(lx)) lx = 0;
			if (double.IsNaN(ty)) ty = 0;
			textDragOrigin = new Point(lx, ty);
			try { host.CaptureMouse(); } catch { }
			e.Handled = true;
		};
		host.MouseMove += (_, e) => {
			if (!textDrag || editHost == host) return;
			if (e.LeftButton != MouseButtonState.Pressed) return;
			var p = e.GetPosition(pdraw);
			Canvas.SetLeft(host, textDragOrigin.X + (p.X - textDragMouse.X));
			Canvas.SetTop(host, textDragOrigin.Y + (p.Y - textDragMouse.Y));
			e.Handled = true;
		};
		host.MouseLeftButtonUp += (_, e) => {
			if (!textDrag) return;
			textDrag = false;
			try { host.ReleaseMouseCapture(); } catch { }
			e.Handled = true;
		};
	}

	void selecttext(Border host) {
		if (host == null) return;
		if (selText != null && selText != host)
			settextselvisual(selText, false);
		selText = host;
		if (editHost != host)
			settextselvisual(host, true);
	}

	void cleartextsel() {
		if (selText == null) return;
		settextselvisual(selText, false);
		selText = null;
	}

	void settextselvisual(Border host, bool on) {
		if (host == null || editHost == host) return;
		if (on) {
			host.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
			host.BorderThickness = new Thickness(1);
			host.Padding = new Thickness(2, 0, 2, 0);
		}
		else {
			host.BorderBrush = Brushes.Transparent;
			host.BorderThickness = new Thickness(0);
			host.Padding = new Thickness(0);
		}
	}

	void entertextedit(Border host, bool isNew) {
		if (host == null || editHost == host) return;
		committextedit();
		selecttext(host);
		settextselvisual(host, false);
		var fg = strokebrush();
		var fs = curfontsize();
		var old = "";
		if (host.Child is TextBlock blk) {
			old = blk.Text ?? "";
			if (blk.Foreground is SolidColorBrush scb) fg = scb;
			fs = blk.FontSize > 0 ? blk.FontSize : fs;
		}
		textEditBackup = old;
		textEditIsNew = isNew;
		editHost = host;
		var box = new WpfTextBox {
			Text = old,
			FontSize = fs,
			Foreground = fg,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			CaretBrush = fg is SolidColorBrush cb ? cb : Brushes.Black,
			Padding = new Thickness(0),
			FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
			FontWeight = FontWeights.SemiBold,
			MinWidth = 48,
			AcceptsReturn = false,
		};
		host.Background = Brushes.Transparent;
		host.BorderBrush = fg;
		host.BorderThickness = new Thickness(1);
		host.Padding = new Thickness(2, 1, 2, 1);
		host.Cursor = Cursors.IBeam;
		host.Child = box;
		Dispatcher.BeginInvoke(new Action(() => {
			try { box.Focus(); box.SelectAll(); } catch { }
		}), System.Windows.Threading.DispatcherPriority.Input);
		box.KeyDown += (_, ke) => {
			if (ke.Key == Key.Enter) { committextedit(); ke.Handled = true; }
			else if (ke.Key == Key.Escape) {
				if (textEditIsNew && string.IsNullOrWhiteSpace(textEditBackup))
					discardtext(host);
				else {
					box.Text = textEditBackup ?? "";
					committextedit();
				}
				ke.Handled = true;
			}
		};
		box.LostKeyboardFocus += (_, _) => {
			if (editHost == host) committextedit();
		};
	}

	void discardtext(Border host) {
		if (host == null) return;
		if (editHost == host) editHost = null;
		if (selText == host) selText = null;
		try { pdraw.Children.Remove(host); } catch { }
		strokes.Remove(host);
	}

	void committextedit() {
		if (editHost == null) return;
		var host = editHost;
		editHost = null;
		var text = "";
		Brush fg = strokebrush();
		var fs = curfontsize();
		if (host.Child is WpfTextBox box) {
			text = (box.Text ?? "").Trim();
			if (box.Foreground != null) fg = box.Foreground;
			if (box.FontSize > 0) fs = box.FontSize;
		}
		if (string.IsNullOrEmpty(text)) {
			discardtext(host);
			return;
		}
		host.Child = new TextBlock {
			Text = text,
			Foreground = fg,
			FontSize = fs,
			FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
			FontWeight = FontWeights.SemiBold,
			Background = Brushes.Transparent,
		};
		host.Background = Brushes.Transparent;
		host.Cursor = Cursors.SizeAll;
		if (selText == host)
			settextselvisual(host, true);
		else {
			host.BorderBrush = Brushes.Transparent;
			host.BorderThickness = new Thickness(0);
			host.Padding = new Thickness(0);
		}
	}

	static UIElement makearrow(Point from, Point to, Brush brush, double thick) {
		var dx = to.X - from.X;
		var dy = to.Y - from.Y;
		var len = Math.Sqrt(dx * dx + dy * dy);
		if (len < 2) return null;
		var ux = dx / len;
		var uy = dy / len;
		var head = Math.Max(10, thick * 4);
		var bx = to.X - ux * head;
		var by = to.Y - uy * head;
		var px = -uy;
		var py = ux;
		var hw = head * 0.45;
		var geo = new PathGeometry();
		var fig = new PathFigure { StartPoint = from, IsClosed = false };
		fig.Segments.Add(new LineSegment(to, true));
		geo.Figures.Add(fig);
		var fig2 = new PathFigure { StartPoint = to, IsClosed = true };
		fig2.Segments.Add(new LineSegment(new Point(bx + px * hw, by + py * hw), true));
		fig2.Segments.Add(new LineSegment(new Point(bx - px * hw, by - py * hw), true));
		geo.Figures.Add(fig2);
		return new WpfPath {
			Data = geo,
			Stroke = brush,
			StrokeThickness = thick,
			Fill = brush,
			StrokeLineJoin = PenLineJoin.Round,
			StrokeStartLineCap = PenLineCap.Round,
		};
	}

	void undo() {
		committextedit();
		if (strokes.Count == 0) return;
		var last = strokes[strokes.Count - 1];
		strokes.RemoveAt(strokes.Count - 1);
		pdraw.Children.Remove(last);
		if (selText == last) selText = null;
		if (editHost == last) editHost = null;
	}

	void clearall() {
		committextedit();
		if (strokes.Count == 0) return;
		if (MessageBox.Show(this, Loc.T("ann.clear.ask"), Loc.T("ann.title"),
			    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			return;
		foreach (var s in strokes)
			pdraw.Children.Remove(s);
		strokes.Clear();
		selText = null;
		editHost = null;
	}

	BitmapSource render() {
		committextedit();
		cleartextsel();
		// 栅格化整页（底图 + 标注层）
		pstage.Measure(new WpfSize(imgW, imgH));
		pstage.Arrange(new Rect(0, 0, imgW, imgH));
		pstage.UpdateLayout();
		var rtb = new RenderTargetBitmap(imgW, imgH, 96, 96, PixelFormats.Pbgra32);
		rtb.Render(pstage);
		rtb.Freeze();
		return ImageUtil.Withdpi(rtb, 96, 96);
	}

	void savefile() {
		var sfd = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T("ann.save"),
			Filter = Loc.T("ann.filter"),
			FileName = $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
			AddExtension = true,
			DefaultExt = ".png",
		};
		if (sfd.ShowDialog(this) != true) return;
		try {
			var bmp = render();
			ImageUtil.Savefile(bmp, sfd.FileName);
			ResultImage = bmp;
			lbhint.Text = string.Format(Loc.T("ann.saved.hint"), sfd.FileName);
			MessageBox.Show(this, string.Format(Loc.T("ann.saved"), sfd.FileName), Loc.T("ann.title"),
				MessageBoxButton.OK, MessageBoxImage.Information);
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Loc.T("ann.save.fail"), MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void copyclip() {
		try {
			var bmp = render();
			ImageUtil.Toclipboard(bmp);
			ResultImage = bmp;
			lbhint.Text = Loc.T("ann.copied.hint");
			MessageBox.Show(this, Loc.T("ann.copied"), Loc.T("ann.title"),
				MessageBoxButton.OK, MessageBoxImage.Information);
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Loc.T("ann.copy.fail"), MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
