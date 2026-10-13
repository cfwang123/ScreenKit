namespace ScreenKit;

/// <summary>工具 → 字符取模。</summary>
public partial class GlyphModWindow : Window {
	public GlyphModWindow() {
		InitializeComponent();
		fillfonts();
		applylang();
		bmake.Click += (_, _) => make();
		bcopy.Click += (_, _) => copyout();
		bsave.Click += (_, _) => saveout();
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("gm.title");
		lbhint.Text = Loc.T("gm.hint");
		lbfont.Text = Loc.T("gm.font");
		lbsize.Text = Loc.T("gm.size");
		ebold.Content = Loc.T("gm.bold");
		lbout.Text = Loc.T("gm.out");
		bmake.Content = Loc.T("gm.make");
		bcopy.Content = Loc.T("gm.copy");
		bsave.Content = Loc.T("gm.save");
		filltag(escan, tag(escan) ?? "row",
			("row", "gm.scan.row"), ("col", "gm.scan.col"));
		filltag(ebit, tag(ebit) ?? "msb",
			("msb", "gm.bit.msb"), ("lsb", "gm.bit.lsb"));
		filltag(epolar, tag(epolar) ?? "on",
			("on", "gm.polar.on"), ("off", "gm.polar.off"));
		filltag(efmt, tag(efmt) ?? "c",
			("c", "gm.format.c"), ("hex", "gm.format.hex"), ("dots", "gm.format.dots"));
	}

	void fillfonts() {
		if (efont.Items.Count > 0) return;
		foreach (var f in Fonts.SystemFontFamilies.OrderBy(f => f.Source, StringComparer.CurrentCultureIgnoreCase))
			efont.Items.Add(new ComboBoxItem { Content = f.Source, Tag = f.Source });
		pick(efont, "Microsoft YaHei");
		if (efont.SelectedIndex < 0) pick(efont, "Segoe UI");
		if (efont.SelectedIndex < 0 && efont.Items.Count > 0) efont.SelectedIndex = 0;
	}

	static void filltag(ComboBox box, string cur, params (string tag, string key)[] items) {
		box.Items.Clear();
		foreach (var it in items)
			box.Items.Add(new ComboBoxItem { Content = Loc.T(it.key), Tag = it.tag });
		pick(box, cur);
	}

	static string tag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

	static void pick(ComboBox box, string want) {
		for (int i = 0; i < box.Items.Count; i++) {
			if ((box.Items[i] as ComboBoxItem)?.Tag as string == want) {
				box.SelectedIndex = i;
				return;
			}
		}
		if (box.Items.Count > 0 && box.SelectedIndex < 0) box.SelectedIndex = 0;
	}

	void make() {
		var text = etext.Text ?? "";
		if (text.Trim().Length == 0) {
			lbstat.Text = Loc.T("gm.empty");
			return;
		}
		if (!int.TryParse((esize.Text ?? "").Trim(), out var size) || size < GlyphMod.SIZE_MIN || size > GlyphMod.SIZE_MAX) {
			lbstat.Text = Loc.T("gm.badsize", GlyphMod.SIZE_MIN, GlyphMod.SIZE_MAX);
			return;
		}
		var spec = new GlyphMod.Spec {
			Font = tag(efont) ?? "Segoe UI",
			Size = size,
			Bold = ebold.IsChecked == true,
			Column = GlyphMod.NormColumn(tag(escan)),
			Msb = tag(ebit) != "lsb",
			Invert = tag(epolar) == "off",
			Format = GlyphMod.NormFormat(tag(efmt)),
		};
		var sheet = GlyphMod.Build(text, spec);
		eout.Text = GlyphMod.Export(sheet.Glyphs, spec);
		showdots(sheet);
		var bytes = 0;
		foreach (var g in sheet.Glyphs) bytes += g.Data.Length;
		var stat = Loc.T("gm.stat", sheet.Glyphs.Count, bytes);
		if (sheet.Truncated) stat += "  " + Loc.T("gm.limit", GlyphMod.MAX_CHARS);
		if (!string.IsNullOrEmpty(sheet.Error)) stat += "  " + sheet.Error;
		lbstat.Text = stat;
	}

	void showdots(GlyphMod.Sheet sheet) {
		pdots.Children.Clear();
		Brush muted = null;
		Brush border = null;
		try { muted = (Brush)FindResource("TextMuted"); } catch { }
		try { border = (Brush)FindResource("BorderSoft"); } catch { }
		foreach (var g in sheet.Glyphs) {
			var img = new Image {
				Source = dotbmp(g),
				Stretch = Stretch.None,
				SnapsToDevicePixels = true,
			};
			var box = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
			box.Children.Add(new TextBlock {
				Text = g.Ch + "  " + g.W + "×" + g.H,
				Foreground = muted,
				FontSize = 12,
				Margin = new Thickness(0, 0, 0, 4),
			});
			box.Children.Add(new Border {
				BorderBrush = border,
				BorderThickness = new Thickness(1),
				Child = img,
			});
			pdots.Children.Add(box);
		}
	}

	static BitmapSource dotbmp(GlyphMod.Glyph g) {
		int z = Math.Max(2, 64 / Math.Max(1, g.H));
		if (z > 8) z = 8;
		int w = Math.Max(1, g.W * z);
		int h = Math.Max(1, g.H * z);
		var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
		int stride = w * 4;
		var buf = new byte[stride * h];
		for (int y = 0; y < g.H; y++) {
			for (int x = 0; x < g.W; x++) {
				int pi = y * g.W + x;
				bool ink = g.Ink != null && (uint)pi < (uint)g.Ink.Length && g.Ink[pi];
				byte c = ink ? (byte)24 : (byte)248;
				for (int dy = 0; dy < z; dy++) {
					int row = (y * z + dy) * stride + x * z * 4;
					for (int dx = 0; dx < z; dx++) {
						int i = row + dx * 4;
						buf[i] = c;
						buf[i + 1] = c;
						buf[i + 2] = c;
						buf[i + 3] = 255;
					}
				}
			}
		}
		bmp.WritePixels(new Int32Rect(0, 0, w, h), buf, stride, 0);
		bmp.Freeze();
		return bmp;
	}

	void copyout() {
		var text = eout.Text ?? "";
		if (text.Length == 0) return;
		try {
			Clipboard.SetText(text);
			lbstat.Text = Loc.T("gm.copied");
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
		}
	}

	void saveout() {
		var text = eout.Text ?? "";
		if (text.Length == 0) return;
		var c = tag(efmt) == "c";
		var dlg = new Microsoft.Win32.SaveFileDialog {
			FileName = c ? "glyphmod.h" : "glyphmod.txt",
			Filter = c ? "C (*.h)|*.h|Text (*.txt)|*.txt" : "Text (*.txt)|*.txt|C (*.h)|*.h",
		};
		if (dlg.ShowDialog(this) != true) return;
		try {
			File.WriteAllText(dlg.FileName, text, new System.Text.UTF8Encoding(false));
			lbstat.Text = dlg.FileName;
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
		}
	}
}
