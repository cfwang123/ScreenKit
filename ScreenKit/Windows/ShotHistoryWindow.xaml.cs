using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Path = System.IO.Path;
using WpfPath = System.Windows.Shapes.Path;

namespace ScreenKit;

public sealed class ShotRow : INotifyPropertyChanged {
	public string Full { get; set; }
	public string Name { get; set; }
	public string When { get; set; }
	ImageSource thumb;
	public ImageSource Thumb {
		get => thumb;
		set { thumb = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb))); }
	}
	public event PropertyChangedEventHandler PropertyChanged;
}

/// <summary>截图历史：缩略图、预览平移缩放、标注后另存。</summary>
public partial class ShotHistoryWindow : Window {
	public const int ActCopy = 0;
	public const int ActOcr = 1;
	public const int ActOpen = 2;

	/// <summary>bmp、未标注时的原文件路径（有标注则为 null）、动作。</summary>
	public Func<BitmapSource, string, int, Task<string>> CommitAsync { get; set; }
	public Action<bool, bool, bool> CopyModeChanged { get; set; }

	enum Tool { Rect, Ellipse, Arrow, Pen, Text }
	enum Kind { Rect, Ellipse, Arrow, Pen, Text }

	sealed class Mark {
		public Kind Kind;
		public Color Color;
		public double Thick;
		public double FontSize;
		public string Text;
		public Point A, B;
		public List<Point> Pts;
	}

	readonly ObservableCollection<ShotRow> rows = new();
	readonly List<Mark> marks = new();
	readonly SemaphoreSlim thumbgate = new(4);
	const double ZMIN = 0.05;
	const double ZMAX = 16;

	BitmapSource src;
	string srcpath;
	int loadgen;
	int thumbgen;
	bool suppress;
	bool needfit = true;
	bool drawing;
	bool panning;
	bool saving;
	Tool tool = Tool.Rect;
	Point p0, panstart;
	double pan0x, pan0y;
	Color draftColor;
	double draftThick;
	List<Point> draftPts;
	UIElement draftEl;
	TextBox etext;

	public ShotHistoryWindow() {
		InitializeComponent();
		lv.ItemsSource = rows;
		applylang();
		wire();
		settool(Tool.Rect);
		refreshcopy();
		Reload(null);
	}

	public void OnReopen() {
		ApplyLang();
		if (marks.Count == 0 && etext == null && !saving)
			Reload(null);
	}

	public void Reload(string selectPath) {
		if (string.IsNullOrEmpty(selectPath)) selectPath = srcpath;
		var gen = ++thumbgen;
		var list = listfiles();
		suppress = true;
		rows.Clear();
		foreach (var it in list) {
			rows.Add(new ShotRow {
				Full = it.path,
				Name = Path.GetFileName(it.path),
				When = it.t.ToString("yyyy-MM-dd HH:mm"),
			});
		}
		suppress = false;
		lbempty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		ShotRow pick = null;
		if (!string.IsNullOrEmpty(selectPath)) {
			foreach (var r in rows) {
				if (string.Equals(r.Full, selectPath, StringComparison.OrdinalIgnoreCase)) {
					pick = r;
					break;
				}
			}
		}
		if (pick == null && rows.Count > 0) pick = rows[0];
		if (pick == null) {
			lv.SelectedItem = null;
			clearsrc();
		}
		else if (!ReferenceEquals(lv.SelectedItem, pick))
			lv.SelectedItem = pick;
		else
			_ = loadfull(pick.Full);
		lv.ScrollIntoView(pick);
		foreach (var r in rows)
			_ = loadthumb(r, gen);
	}

	public void ApplyLang() {
		applylang();
		refreshcopy();
		upmeta();
	}

	void wire() {
		lv.SelectionChanged += (_, _) => {
			if (suppress) return;
			if (lv.SelectedItem is ShotRow row) _ = loadfull(row.Full);
			else clearsrc();
		};
		bfolder.Click += (_, _) => openfolder();
		trect.Checked += (_, _) => { if (trect.IsChecked == true) settool(Tool.Rect); };
		tellipse.Checked += (_, _) => { if (tellipse.IsChecked == true) settool(Tool.Ellipse); };
		tarrow.Checked += (_, _) => { if (tarrow.IsChecked == true) settool(Tool.Arrow); };
		tpen.Checked += (_, _) => { if (tpen.IsChecked == true) settool(Tool.Pen); };
		ttext.Checked += (_, _) => { if (ttext.IsChecked == true) settool(Tool.Text); };
		bundo.Click += (_, _) => undo();
		bcancel.Click += (_, _) => dropmarks();
		bok.Click += async (_, _) => await commit(ActCopy);
		bocr.Click += async (_, _) => await commit(ActOcr);
		bopen.Click += async (_, _) => await commit(ActOpen);
		bcopydrop.Click += (_, _) => pcopyas.IsOpen = !pcopyas.IsOpen;
		mncopyimg.Click += (_, _) => pickmode(true, false, false);
		mncopyfile.Click += (_, _) => pickmode(false, true, false);
		mncopypath.Click += (_, _) => pickmode(false, false, true);
		pviewport.MouseWheel += (_, e) => onwheel(e);
		pviewport.MouseLeftButtonDown += (_, e) => onleftdown(e);
		pviewport.MouseLeftButtonUp += (_, e) => onleftup(e);
		pviewport.MouseRightButtonDown += (_, e) => onrightdown(e);
		pviewport.MouseRightButtonUp += (_, e) => onrightup(e);
		pviewport.MouseMove += (_, e) => onmove(e);
		pviewport.MouseDown += (_, e) => { if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left) onfitclick(e); };
		pviewport.SizeChanged += (_, _) => { if (needfit) fit(); };
		PreviewKeyDown += (_, e) => onkey(e);
		Activated += (_, _) => refreshcopy();
	}

	void applylang() {
		Title = Loc.T("hist.title");
		lbtitle.Text = Loc.T("hist.title");
		lbempty.Text = Loc.T("hist.empty");
		bfolder.Content = Loc.T("hist.folder");
		bfolder.ToolTip = Loc.T("hist.folder.tip");
		trect.ToolTip = Loc.T("hist.rect");
		tellipse.ToolTip = Loc.T("hist.ellipse");
		tarrow.ToolTip = Loc.T("hist.arrow");
		tpen.ToolTip = Loc.T("hist.pen");
		ttext.ToolTip = Loc.T("hist.text");
		bundo.ToolTip = Loc.T("hist.undo");
		bcancel.ToolTip = Loc.T("hist.cancel.tip");
		bok.ToolTip = Loc.T("hist.ok.tip");
		bocr.ToolTip = Loc.T("hist.ocr.tip");
		bopen.ToolTip = Loc.T("hist.open.tip");
		bcopydrop.ToolTip = Loc.T("hist.copydrop.tip");
		mncopyimg.Content = Loc.T("overlay.copy.img");
		mncopyfile.Content = Loc.T("overlay.copy.file");
		mncopypath.Content = Loc.T("overlay.copy.path");
	}

	void openfolder() {
		try { ImageUtil.OpenScreenshotsFolder(); }
		catch (Exception ex) { lbstat.Text = ex.Message; }
	}

	static List<(string path, DateTime t)> listfiles() {
		var list = new List<(string path, DateTime t)>();
		var dir = ImageUtil.ScreenshotsDir;
		if (!Directory.Exists(dir)) return list;
		foreach (var f in Directory.EnumerateFiles(dir)) {
			if (!ImageUtil.IsImagePath(f)) continue;
			DateTime t;
			try { t = File.GetLastWriteTime(f); }
			catch { continue; }
			list.Add((f, t));
		}
		list.Sort((a, b) => b.t.CompareTo(a.t));
		return list;
	}

	async Task loadthumb(ShotRow row, int gen) {
		if (row == null) return;
		try {
			await thumbgate.WaitAsync().ConfigureAwait(true);
			BitmapSource bmp = null;
			try {
				if (gen != thumbgen) return;
				var full = row.Full;
				bmp = await Task.Run(() => mkthumb(full)).ConfigureAwait(true);
			}
			finally {
				try { thumbgate.Release(); } catch { }
			}
			if (bmp == null || gen != thumbgen) return;
			row.Thumb = bmp;
		}
		catch { }
	}

	static BitmapSource mkthumb(string full) {
		try {
			using var fs = File.OpenRead(full);
			var bi = new BitmapImage();
			bi.BeginInit();
			bi.CacheOption = BitmapCacheOption.OnLoad;
			bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bi.DecodePixelWidth = 160;
			bi.StreamSource = fs;
			bi.EndInit();
			bi.Freeze();
			return bi;
		}
		catch { return null; }
	}

	async Task loadfull(string path) {
		var g = ++loadgen;
		dropmarks();
		BitmapSource bmp = null;
		try { bmp = await Task.Run(() => ImageUtil.Fromfile(path)).ConfigureAwait(true); }
		catch { }
		if (g != loadgen || bmp == null) return;
		src = bmp;
		srcpath = path;
		imgview.Source = bmp;
		imgview.Width = bmp.PixelWidth;
		imgview.Height = bmp.PixelHeight;
		pdraw.Width = bmp.PixelWidth;
		pdraw.Height = bmp.PixelHeight;
		pstage.Width = bmp.PixelWidth;
		pstage.Height = bmp.PixelHeight;
		needfit = true;
		fit();
		upmeta();
		syncbuttons();
	}

	void clearsrc() {
		++loadgen;
		dropmarks();
		src = null;
		srcpath = null;
		imgview.Source = null;
		lbmeta.Text = "";
		syncbuttons();
	}

	void syncbuttons() {
		var on = src != null && !saving;
		bok.IsEnabled = on;
		bocr.IsEnabled = on;
		bopen.IsEnabled = on;
		bcopydrop.IsEnabled = on;
		bundo.IsEnabled = on;
		bcancel.IsEnabled = on && (marks.Count > 0 || etext != null);
		trect.IsEnabled = on;
		tellipse.IsEnabled = on;
		tarrow.IsEnabled = on;
		tpen.IsEnabled = on;
		ttext.IsEnabled = on;
	}

	void upmeta() {
		if (src == null) { lbmeta.Text = ""; return; }
		var name = string.IsNullOrEmpty(srcpath) ? "" : Path.GetFileName(srcpath);
		var z = (int)Math.Round(tfscale.ScaleX * 100);
		lbmeta.Text = Loc.T("hist.meta", name, src.PixelWidth, src.PixelHeight, z);
	}

	void fit() {
		if (src == null) return;
		var vw = pviewport.ActualWidth;
		var vh = pviewport.ActualHeight;
		if (vw < 8 || vh < 8) return;
		var s = Math.Min(vw / src.PixelWidth, vh / src.PixelHeight);
		s = Compat.Clamp(s, ZMIN, ZMAX);
		tfscale.ScaleX = s;
		tfscale.ScaleY = s;
		tfpan.X = (vw - src.PixelWidth * s) / 2;
		tfpan.Y = (vh - src.PixelHeight * s) / 2;
		upmeta();
	}

	void onwheel(MouseWheelEventArgs e) {
		if (src == null) return;
		var pos = e.GetPosition(pviewport);
		var old = tfscale.ScaleX;
		var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
		var nz = Compat.Clamp(old * factor, ZMIN, ZMAX);
		var dx = pos.X - tfpan.X;
		var dy = pos.Y - tfpan.Y;
		tfpan.X = pos.X - dx * (nz / old);
		tfpan.Y = pos.Y - dy * (nz / old);
		tfscale.ScaleX = nz;
		tfscale.ScaleY = nz;
		needfit = false;
		upmeta();
		e.Handled = true;
	}

	Point topixel(Point vp) {
		var s = tfscale.ScaleX;
		if (s < 0.0001) s = 1;
		return new Point((vp.X - tfpan.X) / s, (vp.Y - tfpan.Y) / s);
	}

	bool inside(Point p) {
		if (src == null) return false;
		return p.X >= 0 && p.Y >= 0 && p.X <= src.PixelWidth && p.Y <= src.PixelHeight;
	}

	Point clamp(Point p) {
		if (src == null) return p;
		var x = Compat.Clamp(p.X, 0, src.PixelWidth);
		var y = Compat.Clamp(p.Y, 0, src.PixelHeight);
		return new Point(x, y);
	}

	void onleftdown(MouseButtonEventArgs e) {
		if (src == null || saving) return;
		if (e.ClickCount >= 2) return;
		var pix = topixel(e.GetPosition(pviewport));
		if (!inside(pix)) {
			startpan(e.GetPosition(pviewport));
			e.Handled = true;
			return;
		}
		if (tool == Tool.Text) {
			begintext(clamp(pix));
			e.Handled = true;
			return;
		}
		committext();
		drawing = true;
		p0 = clamp(pix);
		draftColor = curcolor();
		draftThick = curthick();
		draftPts = new List<Point> { p0 };
		pviewport.CaptureMouse();
		e.Handled = true;
	}

	void onleftup(MouseButtonEventArgs e) {
		if (panning && e.ChangedButton == MouseButton.Left) {
			panning = false;
			if (pviewport.IsMouseCaptured) pviewport.ReleaseMouseCapture();
			e.Handled = true;
			return;
		}
		if (!drawing) return;
		drawing = false;
		if (pviewport.IsMouseCaptured) pviewport.ReleaseMouseCapture();
		finishdraft(clamp(topixel(e.GetPosition(pviewport))));
		e.Handled = true;
	}

	void onrightdown(MouseButtonEventArgs e) {
		if (src == null) return;
		startpan(e.GetPosition(pviewport));
		e.Handled = true;
	}

	void onrightup(MouseButtonEventArgs e) {
		if (!panning) return;
		panning = false;
		if (pviewport.IsMouseCaptured) pviewport.ReleaseMouseCapture();
		e.Handled = true;
	}

	void startpan(Point vp) {
		drawing = false;
		cleardraft();
		panning = true;
		panstart = vp;
		pan0x = tfpan.X;
		pan0y = tfpan.Y;
		pviewport.Cursor = Cursors.SizeAll;
		pviewport.CaptureMouse();
	}

	void onmove(MouseEventArgs e) {
		if (panning) {
			var p = e.GetPosition(pviewport);
			tfpan.X = pan0x + (p.X - panstart.X);
			tfpan.Y = pan0y + (p.Y - panstart.Y);
			needfit = false;
			return;
		}
		if (drawing) {
			updatedraft(clamp(topixel(e.GetPosition(pviewport))));
			return;
		}
		if (src == null) { pviewport.Cursor = Cursors.Arrow; return; }
		var pix = topixel(e.GetPosition(pviewport));
		if (!inside(pix)) pviewport.Cursor = Cursors.Arrow;
		else if (tool == Tool.Text) pviewport.Cursor = Cursors.IBeam;
		else pviewport.Cursor = Cursors.Cross;
	}

	void onfitclick(MouseButtonEventArgs e) {
		if (marks.Count > 0 && issmall(marks[marks.Count - 1])) {
			marks.RemoveAt(marks.Count - 1);
			redraw();
		}
		drawing = false;
		cleardraft();
		needfit = true;
		fit();
		e.Handled = true;
	}

	void onkey(KeyEventArgs e) {
		if (Keyboard.FocusedElement is TextBox) {
			if (e.Key == Key.Escape) { canceltext(); e.Handled = true; }
			else if (e.Key == Key.Enter) { committext(); e.Handled = true; }
			return;
		}
		if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) {
			undo();
			e.Handled = true;
			return;
		}
		if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.None) {
			cyclecopy();
			e.Handled = true;
			return;
		}
		if (e.Key == Key.Enter) {
			_ = commit(ActCopy);
			e.Handled = true;
			return;
		}
		if (e.Key == Key.Escape) {
			if (marks.Count > 0 || etext != null) dropmarks();
			else Close();
			e.Handled = true;
		}
	}

	void settool(Tool t) {
		tool = t;
		trect.IsChecked = t == Tool.Rect;
		tellipse.IsChecked = t == Tool.Ellipse;
		tarrow.IsChecked = t == Tool.Arrow;
		tpen.IsChecked = t == Tool.Pen;
		ttext.IsChecked = t == Tool.Text;
	}

	Color curcolor() {
		if (cryellow.IsChecked == true) return (Color)ColorConverter.ConvertFromString("#FFC300");
		if (crgreen.IsChecked == true) return (Color)ColorConverter.ConvertFromString("#07C160");
		if (crblue.IsChecked == true) return (Color)ColorConverter.ConvertFromString("#10AEFF");
		if (crwhite.IsChecked == true) return Colors.White;
		return (Color)ColorConverter.ConvertFromString("#FA5151");
	}

	double curthick() {
		if (ethick.SelectedItem is ComboBoxItem it && it.Tag is string s &&
			double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
			return v;
		return 3;
	}

	double curfont() {
		if (efont.SelectedItem is ComboBoxItem it && it.Tag is string s &&
			double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
			return v;
		return 18;
	}

	static Brush solid(Color c) {
		var b = new SolidColorBrush(c);
		b.Freeze();
		return b;
	}

	void updatedraft(Point p) {
		if (tool == Tool.Pen) {
			if (draftPts == null) draftPts = new List<Point>();
			if (draftPts.Count == 0 || dist(draftPts[draftPts.Count - 1], p) >= 1.2)
				draftPts.Add(p);
			if (draftEl is Polyline pl) pl.Points = new PointCollection(draftPts);
			else {
				cleardraft();
				pl = new Polyline {
					Stroke = solid(draftColor),
					StrokeThickness = draftThick,
					StrokeStartLineCap = PenLineCap.Round,
					StrokeEndLineCap = PenLineCap.Round,
					StrokeLineJoin = PenLineJoin.Round,
					Points = new PointCollection(draftPts),
					IsHitTestVisible = false,
				};
				draftEl = pl;
				pdraw.Children.Add(pl);
			}
			return;
		}
		cleardraft();
		var br = solid(draftColor);
		if (tool == Tool.Arrow) {
			draftEl = new WpfPath {
				Stroke = br,
				Fill = br,
				StrokeThickness = draftThick,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
				Data = arrowgeo(p0, p, draftThick),
				IsHitTestVisible = false,
			};
			pdraw.Children.Add(draftEl);
			return;
		}
		var r = norm(p0, p);
		Shape sh = tool == Tool.Ellipse
			? new Ellipse()
			: new Rectangle();
		sh.Width = Math.Max(1, r.Width);
		sh.Height = Math.Max(1, r.Height);
		sh.Stroke = br;
		sh.StrokeThickness = draftThick;
		sh.Fill = Brushes.Transparent;
		sh.IsHitTestVisible = false;
		Canvas.SetLeft(sh, r.X);
		Canvas.SetTop(sh, r.Y);
		draftEl = sh;
		pdraw.Children.Add(sh);
	}

	void finishdraft(Point p) {
		var kind = tool == Tool.Ellipse ? Kind.Ellipse
			: tool == Tool.Arrow ? Kind.Arrow
			: tool == Tool.Pen ? Kind.Pen
			: Kind.Rect;
		var m = new Mark { Kind = kind, Color = draftColor, Thick = draftThick, A = p0, B = p, Pts = draftPts };
		cleardraft();
		if (issmall(m)) { redraw(); return; }
		marks.Add(m);
		redraw();
		syncbuttons();
	}

	static bool issmall(Mark m) {
		if (m == null) return true;
		if (m.Kind == Kind.Pen) return m.Pts == null || m.Pts.Count < 2;
		if (m.Kind == Kind.Text) return string.IsNullOrWhiteSpace(m.Text);
		return dist(m.A, m.B) < 2;
	}

	static double dist(Point a, Point b) {
		var dx = a.X - b.X;
		var dy = a.Y - b.Y;
		return Math.Sqrt(dx * dx + dy * dy);
	}

	static Rect norm(Point a, Point b) {
		var x = Math.Min(a.X, b.X);
		var y = Math.Min(a.Y, b.Y);
		return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
	}

	void cleardraft() {
		if (draftEl != null) {
			pdraw.Children.Remove(draftEl);
			draftEl = null;
		}
		draftPts = null;
	}

	void begintext(Point p) {
		committext();
		var fs = curfont();
		etext = new TextBox {
			FontSize = fs,
			FontFamily = new FontFamily("Microsoft YaHei UI"),
			Foreground = solid(curcolor()),
			Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
			BorderBrush = solid(curcolor()),
			BorderThickness = new Thickness(1),
			MinWidth = 80,
			Padding = new Thickness(4, 2, 4, 2),
			AcceptsReturn = false,
		};
		Canvas.SetLeft(etext, p.X);
		Canvas.SetTop(etext, p.Y);
		pdraw.Children.Add(etext);
		pdraw.IsHitTestVisible = true;
		etext.Loaded += (_, _) => { try { etext?.Focus(); } catch { } };
		syncbuttons();
	}

	void committext() {
		if (etext == null) return;
		var t = etext.Text ?? "";
		var pos = new Point(Canvas.GetLeft(etext), Canvas.GetTop(etext));
		var fs = etext.FontSize;
		var col = etext.Foreground is SolidColorBrush sb ? sb.Color : curcolor();
		pdraw.Children.Remove(etext);
		etext = null;
		pdraw.IsHitTestVisible = false;
		if (!string.IsNullOrWhiteSpace(t)) {
			marks.Add(new Mark {
				Kind = Kind.Text, Text = t, A = pos, FontSize = fs, Color = col, Thick = 1,
			});
		}
		redraw();
		syncbuttons();
	}

	void canceltext() {
		if (etext == null) return;
		pdraw.Children.Remove(etext);
		etext = null;
		pdraw.IsHitTestVisible = false;
		syncbuttons();
	}

	void dropmarks() {
		canceltext();
		marks.Clear();
		cleardraft();
		drawing = false;
		redraw();
		syncbuttons();
	}

	void undo() {
		if (etext != null) { canceltext(); return; }
		if (marks.Count == 0) return;
		marks.RemoveAt(marks.Count - 1);
		redraw();
		syncbuttons();
	}

	void redraw() {
		pdraw.Children.Clear();
		foreach (var m in marks) {
			var el = makevisual(m);
			if (el != null) pdraw.Children.Add(el);
		}
		if (etext != null) pdraw.Children.Add(etext);
	}

	UIElement makevisual(Mark m) {
		var br = solid(m.Color);
		if (m.Kind == Kind.Text) {
			var tb = new TextBlock {
				Text = m.Text ?? "",
				FontSize = m.FontSize > 0 ? m.FontSize : 18,
				Foreground = br,
				FontFamily = new FontFamily("Microsoft YaHei UI"),
				IsHitTestVisible = false,
			};
			Canvas.SetLeft(tb, m.A.X);
			Canvas.SetTop(tb, m.A.Y);
			return tb;
		}
		if (m.Kind == Kind.Pen) {
			if (m.Pts == null || m.Pts.Count < 2) return null;
			return new Polyline {
				Points = new PointCollection(m.Pts),
				Stroke = br,
				StrokeThickness = m.Thick,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
				IsHitTestVisible = false,
			};
		}
		if (m.Kind == Kind.Arrow)
			return new WpfPath {
				Stroke = br,
				Fill = br,
				StrokeThickness = m.Thick,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
				Data = arrowgeo(m.A, m.B, m.Thick),
				IsHitTestVisible = false,
			};
		var r = norm(m.A, m.B);
		Shape sh = m.Kind == Kind.Ellipse ? new Ellipse() : new Rectangle();
		sh.Width = Math.Max(1, r.Width);
		sh.Height = Math.Max(1, r.Height);
		sh.Stroke = br;
		sh.StrokeThickness = m.Thick;
		sh.Fill = Brushes.Transparent;
		sh.IsHitTestVisible = false;
		Canvas.SetLeft(sh, r.X);
		Canvas.SetTop(sh, r.Y);
		return sh;
	}

	static Geometry arrowgeo(Point a, Point b, double thick) {
		var g = new StreamGeometry();
		using (var gc = g.Open()) {
			gc.BeginFigure(a, false, false);
			gc.LineTo(b, true, true);
			var ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
			var len = Math.Max(8, thick * 3.5);
			var p1 = new Point(b.X - len * Math.Cos(ang - 0.45), b.Y - len * Math.Sin(ang - 0.45));
			var p2 = new Point(b.X - len * Math.Cos(ang + 0.45), b.Y - len * Math.Sin(ang + 0.45));
			gc.BeginFigure(p1, true, true);
			gc.LineTo(b, true, true);
			gc.LineTo(p2, true, true);
		}
		g.Freeze();
		return g;
	}

	BitmapSource flatten() {
		committext();
		if (src == null) return null;
		if (marks.Count == 0) return src;
		var w = src.PixelWidth;
		var h = src.PixelHeight;
		var dv = new DrawingVisual();
		using (var dc = dv.RenderOpen()) {
			dc.DrawImage(src, new Rect(0, 0, w, h));
			foreach (var m in marks) drawmark(dc, m);
		}
		var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
		rtb.Render(dv);
		if (rtb.CanFreeze) rtb.Freeze();
		return rtb;
	}

	static void drawmark(DrawingContext dc, Mark m) {
		var br = solid(m.Color);
		if (m.Kind == Kind.Text) {
			var ft = new FormattedText(
				m.Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
				new Typeface("Microsoft YaHei UI"), m.FontSize > 0 ? m.FontSize : 18, br, 1.0);
			dc.DrawText(ft, m.A);
			return;
		}
		var pen = new Pen(br, Math.Max(1, m.Thick)) {
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round,
			LineJoin = PenLineJoin.Round,
		};
		pen.Freeze();
		if (m.Kind == Kind.Pen) {
			if (m.Pts == null || m.Pts.Count < 2) return;
			var g = new StreamGeometry();
			using (var gc = g.Open()) {
				gc.BeginFigure(m.Pts[0], false, false);
				for (var i = 1; i < m.Pts.Count; i++) gc.LineTo(m.Pts[i], true, false);
			}
			g.Freeze();
			dc.DrawGeometry(null, pen, g);
			return;
		}
		if (m.Kind == Kind.Arrow) {
			dc.DrawGeometry(br, pen, arrowgeo(m.A, m.B, m.Thick));
			return;
		}
		var r = norm(m.A, m.B);
		if (m.Kind == Kind.Ellipse)
			dc.DrawEllipse(null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
		else
			dc.DrawRectangle(null, pen, r);
	}

	async Task commit(int act) {
		if (src == null || saving) return;
		saving = true;
		syncbuttons();
		try {
			var bmp = flatten();
			if (bmp == null) return;
			var reuse = marks.Count == 0 ? srcpath : null;
			string path = null;
			if (CommitAsync != null) path = await CommitAsync(bmp, reuse, act).ConfigureAwait(true);
			if (string.IsNullOrEmpty(path)) return;
			var name = Path.GetFileName(path);
			var same = !string.IsNullOrEmpty(reuse)
				&& string.Equals(path, reuse, StringComparison.OrdinalIgnoreCase);
			if (same) {
				lbstat.Text = act == ActOpen ? Loc.T("hist.opened", name)
					: act == ActOcr ? Loc.T("hist.reused", name)
					: Loc.T("hist.copied", name);
				return;
			}
			lbstat.Text = Loc.T("hist.saved", name);
			Reload(path);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("hist.fail", ex.Message);
		}
		finally {
			saving = false;
			syncbuttons();
		}
	}

	void pickmode(bool asImg, bool asFile, bool asPath) {
		pcopyas.IsOpen = false;
		try { CopyModeChanged?.Invoke(asImg, asFile, asPath); } catch { }
		refreshcopy();
	}

	void cyclecopy() {
		var pathMode = ImageUtil.CurrentSnapCopyAsPath
			&& !ImageUtil.CurrentSnapCopyAsImage && !ImageUtil.CurrentSnapCopyAsFile;
		var fileMode = !pathMode && ImageUtil.CurrentSnapCopyAsFile && !ImageUtil.CurrentSnapCopyAsImage;
		if (pathMode) pickmode(true, false, false);
		else if (fileMode) pickmode(false, false, true);
		else pickmode(false, true, false);
	}

	void refreshcopy() {
		var pathMode = ImageUtil.CurrentSnapCopyAsPath
			&& !ImageUtil.CurrentSnapCopyAsImage && !ImageUtil.CurrentSnapCopyAsFile;
		var fileMode = !pathMode && ImageUtil.CurrentSnapCopyAsFile && !ImageUtil.CurrentSnapCopyAsImage;
		var img = Loc.T("overlay.copy.img");
		var file = Loc.T("overlay.copy.file");
		var path = Loc.T("overlay.copy.path");
		lbcopymode.Text = pathMode ? path : (fileMode ? file : img);
		mncopyimg.Content = (pathMode || fileMode ? "   " : "✓ ") + img;
		mncopyfile.Content = (fileMode ? "✓ " : "   ") + file;
		mncopypath.Content = (pathMode ? "✓ " : "   ") + path;
	}
}
