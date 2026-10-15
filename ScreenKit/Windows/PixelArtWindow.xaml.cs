using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>工具 → 像素画：大格作画，旁边按真实像素预览。</summary>
public partial class PixelArtWindow : Window {
	const int MIN_SIDE = 1;
	const int MAX_SIDE = 128;

	static readonly Color[] Palette = {
		Colors.Black, Colors.White, Color.FromRgb(128, 128, 128), Color.FromRgb(192, 192, 192),
		Color.FromRgb(229, 57, 53), Color.FromRgb(255, 152, 0), Color.FromRgb(253, 216, 53),
		Color.FromRgb(67, 160, 71), Color.FromRgb(0, 172, 193), Color.FromRgb(30, 136, 229),
		Color.FromRgb(94, 53, 177), Color.FromRgb(216, 27, 96), Color.FromRgb(121, 85, 72),
		Color.FromRgb(255, 205, 170),
	};

	Color[] pixels = Array.Empty<Color>();
	int gw = 16;
	int gh = 16;
	Color ink = Colors.Black;
	bool erase;
	bool drawing;
	bool rdraw;
	bool dirty;
	bool haslast;
	int lastx;
	int lasty;
	int cell;
	WriteableBitmap raw;

	public PixelArtWindow() {
		InitializeComponent();
		applylang();
		initev();
		fillpalette();
		newcanvas(16, 16);
		WindowEsc.Attach(this);
	}

	void initev() {
		bnew.Click += (_, _) => applysize();
		b8.Click += (_, _) => setsides(8);
		b16.Click += (_, _) => setsides(16);
		b32.Click += (_, _) => setsides(32);
		b64.Click += (_, _) => setsides(64);
		pcolor.MouseLeftButtonDown += (_, _) => pickcolor();
		berase.Click += (_, _) => {
			erase = !erase;
			paintstat();
		};
		bclear.Click += (_, _) => {
			if (!askdirty()) return;
			fill(Colors.White);
			dirty = false;
			redraw();
		};
		bcopy.Click += (_, _) => copyimg();
		bsave.Click += (_, _) => saveimg();
		bclose.Click += (_, _) => Close();
		eedit.MouseLeftButtonDown += (_, e) => begindraw(e.GetPosition(eedit), false);
		eedit.MouseRightButtonDown += (_, e) => begindraw(e.GetPosition(eedit), true);
		eedit.MouseMove += (_, e) => {
			if (drawing && e.LeftButton == MouseButtonState.Pressed) paintat(e.GetPosition(eedit), false);
			else if (rdraw && e.RightButton == MouseButtonState.Pressed) paintat(e.GetPosition(eedit), true);
		};
		eedit.MouseLeftButtonUp += (_, _) => enddraw(false);
		eedit.MouseRightButtonUp += (_, _) => enddraw(true);
		eedit.LostMouseCapture += (_, _) => {
			drawing = false;
			rdraw = false;
		};
	}

	void applylang() {
		Title = Loc.T("pixel.title");
		lbw.Text = Loc.T("pixel.w");
		lbh.Text = Loc.T("pixel.h");
		lbhint.Text = Loc.T("pixel.hint");
		lbpreview.Text = Loc.T("pixel.preview");
		ew.ToolTip = Loc.T("pixel.side.tip");
		eh.ToolTip = Loc.T("pixel.side.tip");
		pcolor.ToolTip = Loc.T("pixel.color.tip");
		ToolBtnUi.Set(bnew, ToolBtnUi.Add, Loc.T("pixel.new"));
		ToolBtnUi.Set(berase, ToolBtnUi.Clear, Loc.T("pixel.erase"));
		ToolBtnUi.Set(bclear, ToolBtnUi.Clear, Loc.T("imgconv.clear"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("pwgen.copy"));
		ToolBtnUi.Set(bsave, ToolBtnUi.Save, Loc.T("pixel.save"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	void fillpalette() {
		foreach (var c in Palette) pcolors.Children.Add(swatch(c));
		pcolors.Children.Add(swatch(Color.FromArgb(0, 0, 0, 0)));
	}

	Border swatch(Color c) {
		var box = new Border {
			Width = 24,
			Height = 24,
			Margin = new Thickness(0, 0, 4, 4),
			BorderBrush = TryFindResource("BorderSoft") as Brush ?? Brushes.Gray,
			BorderThickness = new Thickness(1),
			Background = c.A == 0 ? Brushes.Transparent : new SolidColorBrush(c),
			Cursor = Cursors.Hand,
			ToolTip = c.A == 0 ? Loc.T("pixel.transparent") : "",
		};
		if (c.A == 0) box.Background = checker();
		box.MouseLeftButtonDown += (_, _) => {
			ink = c;
			erase = false;
			pcolor.Background = c.A == 0 ? checker() : new SolidColorBrush(c);
			paintstat();
		};
		return box;
	}

	static Brush checker() {
		var brush = new DrawingBrush {
			TileMode = TileMode.Tile,
			Viewport = new Rect(0, 0, 8, 8),
			ViewportUnits = BrushMappingMode.Absolute,
		};
		var group = new DrawingGroup();
		group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
		var dark = new GeometryGroup();
		dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 4, 4)));
		dark.Children.Add(new RectangleGeometry(new Rect(4, 4, 4, 4)));
		group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(228, 228, 228)), null, dark));
		brush.Drawing = group;
		return brush;
	}

	void setsides(int n) {
		ew.Text = n.ToString();
		eh.Text = n.ToString();
		applysize();
	}

	void applysize() {
		if (!int.TryParse((ew.Text ?? "").Trim(), out var w)) w = gw;
		if (!int.TryParse((eh.Text ?? "").Trim(), out var h)) h = gh;
		w = Compat.Clamp(w, MIN_SIDE, MAX_SIDE);
		h = Compat.Clamp(h, MIN_SIDE, MAX_SIDE);
		ew.Text = w.ToString();
		eh.Text = h.ToString();
		if (w == gw && h == gh) return;
		if (!askdirty()) return;
		newcanvas(w, h);
	}

	bool askdirty() {
		if (!dirty) return true;
		return MessageBox.Show(this, Loc.T("pixel.dirty"), Title,
			MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
	}

	void newcanvas(int w, int h) {
		gw = w;
		gh = h;
		pixels = new Color[w * h];
		fill(Colors.White);
		dirty = false;
		redraw();
	}

	void fill(Color c) {
		for (var i = 0; i < pixels.Length; i++) pixels[i] = c;
	}

	void pickcolor() {
		var dlg = new HsvColorDialog(ink.A == 0 ? Colors.Black : ink) { Owner = this };
		if (dlg.ShowDialog() != true) return;
		ink = dlg.SelectedColor;
		erase = false;
		pcolor.Background = new SolidColorBrush(ink);
		paintstat();
	}

	void begindraw(Point pos, bool aserase) {
		if (aserase) rdraw = true;
		else drawing = true;
		haslast = false;
		eedit.CaptureMouse();
		paintat(pos, aserase);
	}

	void enddraw(bool aserase) {
		if (aserase) rdraw = false;
		else drawing = false;
		if (drawing || rdraw) return;
		haslast = false;
		if (eedit.IsMouseCaptured) eedit.ReleaseMouseCapture();
	}

	void paintat(Point pos, bool aserase) {
		if (eedit.ActualWidth <= 0 || eedit.ActualHeight <= 0) return;
		int x = (int)Math.Floor(pos.X / eedit.ActualWidth * gw);
		int y = (int)Math.Floor(pos.Y / eedit.ActualHeight * gh);
		x = Compat.Clamp(x, -1, gw);
		y = Compat.Clamp(y, -1, gh);
		if (!haslast) {
			put(x, y, aserase);
			lastx = x;
			lasty = y;
			haslast = true;
			return;
		}
		stroke(lastx, lasty, x, y, aserase);
		lastx = x;
		lasty = y;
	}

	void stroke(int x0, int y0, int x1, int y1, bool aserase) {
		int dx = Math.Abs(x1 - x0);
		int dy = Math.Abs(y1 - y0);
		int sx = x0 < x1 ? 1 : -1;
		int sy = y0 < y1 ? 1 : -1;
		int err = dx - dy;
		while (true) {
			put(x0, y0, aserase);
			if (x0 == x1 && y0 == y1) break;
			int e2 = err * 2;
			if (e2 > -dy) {
				err -= dy;
				x0 += sx;
			}
			if (e2 < dx) {
				err += dx;
				y0 += sy;
			}
		}
	}

	void put(int x, int y, bool aserase) {
		if (x < 0 || y < 0 || x >= gw || y >= gh) return;
		Color c = aserase || erase ? Color.FromArgb(0, 0, 0, 0) : ink;
		int i = y * gw + x;
		if (pixels[i] == c) return;
		pixels[i] = c;
		dirty = true;
		writeone(x, y);
	}

	void writeone(int x, int y) {
		Color c = pixels[y * gw + x];
		raw.WritePixels(new Int32Rect(x, y, 1, 1), new byte[] { c.B, c.G, c.R, c.A }, 4, 0);
		var edit = (WriteableBitmap)eedit.Source;
		byte[] buf = new byte[cell * cell * 4];
		Color grid = Color.FromArgb(56, 0, 0, 0);
		for (int yy = 0; yy < cell; yy++) {
			for (int xx = 0; xx < cell; xx++) {
				Color p = xx == cell - 1 || yy == cell - 1 ? grid : cellcolor(c, x, y);
				int i = (yy * cell + xx) * 4;
				buf[i] = p.B;
				buf[i + 1] = p.G;
				buf[i + 2] = p.R;
				buf[i + 3] = p.A;
			}
		}
		edit.WritePixels(new Int32Rect(x * cell, y * cell, cell, cell), buf, cell * 4, 0);
	}

	static Color cellcolor(Color c, int x, int y) {
		if (c.A == 0) return ((x + y) & 1) == 0 ? Colors.White : Color.FromRgb(228, 228, 228);
		return c;
	}

	void redraw() {
		cell = Math.Max(8, Math.Min(28, 512 / Math.Max(gw, gh)));
		raw = bitmap(gw, gh, (x, y) => pixels[y * gw + x]);
		epreview.Source = raw;
		Color grid = Color.FromArgb(56, 0, 0, 0);
		eedit.Source = bitmap(gw * cell, gh * cell, (x, y) => {
			int px = x / cell;
			int py = y / cell;
			if ((x % cell) == cell - 1 || (y % cell) == cell - 1) return grid;
			return cellcolor(pixels[py * gw + px], px, py);
		});
		lbsize.Text = Loc.T("pixel.size", gw, gh);
		paintstat();
	}

	static WriteableBitmap bitmap(int w, int h, Func<int, int, Color> at) {
		var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
		var buf = new byte[w * h * 4];
		for (var y = 0; y < h; y++) {
			for (var x = 0; x < w; x++) {
				var c = at(x, y);
				var i = (y * w + x) * 4;
				buf[i] = c.B;
				buf[i + 1] = c.G;
				buf[i + 2] = c.R;
				buf[i + 3] = c.A;
			}
		}
		bmp.WritePixels(new Int32Rect(0, 0, w, h), buf, w * 4, 0);
		return bmp;
	}

	void paintstat() {
		lbstat.Text = erase ? Loc.T("pixel.mode.erase") : Loc.T("pixel.mode.pen");
	}

	void copyimg() {
		try {
			Clipboard.SetImage(raw);
			lbstat.Text = Loc.T("nettool.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void saveimg() {
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Filter = "PNG|*.png",
			FileName = "pixel.png",
		};
		if (dlg.ShowDialog(this) != true) return;
		try {
			using var fs = System.IO.File.Create(dlg.FileName);
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(raw));
			enc.Save(fs);
			dirty = false;
			lbstat.Text = Loc.T("pixel.saved");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
