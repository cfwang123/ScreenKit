using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace WpfOCR;

/// <summary>桌面实时字幕样式（非模态，可边改边看）。</summary>
public partial class AsrCaptionStyleDialog : Window {
	static AsrCaptionStyleDialog openInst;

	readonly AsrCaptionStyle style;
	readonly AsrCaptionOsdWindow osdwin;
	readonly AsrCaptionStyle snap;
	readonly Action applylive;
	bool loading;
	bool closed;
	bool cancelled;

	/// <param name="onClosed">对话框关闭后回调（确定/取消/标题栏关）。</param>
	public static void Open(AsrCaptionStyle style, AsrCaptionOsdWindow osdwin, Window owner,
		Action applyLive, Action onClosed = null) {
		if (openInst != null) {
			try {
				if (openInst.WindowState == WindowState.Minimized)
					openInst.WindowState = WindowState.Normal;
				openInst.Activate();
				openInst.Focus();
			}
			catch { }
			return;
		}
		var dlg = new AsrCaptionStyleDialog(style, osdwin, applyLive);
		openInst = dlg;
		if (owner != null && owner.IsLoaded)
			dlg.Owner = owner;
		dlg.Closed += (_, _) => {
			if (ReferenceEquals(openInst, dlg))
				openInst = null;
			try { onClosed?.Invoke(); } catch { }
		};
		dlg.Show();
	}

	public AsrCaptionStyleDialog(AsrCaptionStyle style, AsrCaptionOsdWindow osdwin, Action applyLive) {
		InitializeComponent();
		this.style = style ?? new AsrCaptionStyle();
		this.osdwin = osdwin;
		applylive = applyLive;
		snap = this.style.Clone();
		foreach (var f in Fonts.SystemFontFamilies.OrderBy(x => x.Source))
			efont.Items.Add(f.Source);
		loadui(this.style);

		esize.ValueChanged += (_, _) => {
			lbsize.Text = ((int)esize.Value).ToString();
			preview();
		};
		efont.SelectionChanged += (_, _) => preview();
		efg.TextChanged += (_, _) => { updateswatch(swfg, efg.Text, Colors.White); preview(); };
		eoutline.TextChanged += (_, _) => {
			updateswatch(swoutline, eoutline.Text, Color.FromArgb(0xCC, 0, 0, 0));
			preview();
		};
		ebg.TextChanged += (_, _) => { updateswatch(swbg, ebg.Text, Colors.Transparent); preview(); };
		eborder.TextChanged += (_, _) => { updateswatch(swbd, eborder.Text, Colors.Transparent); preview(); };
		eborderth.ValueChanged += (_, _) => {
			lbborderth.Text = ((int)eborderth.Value).ToString();
			preview();
		};
		emaxw.TextChanged += (_, _) => preview();
		ewidth.TextChanged += (_, _) => preview();
		eheight.TextChanged += (_, _) => preview();
		ralignL.Checked += (_, _) => preview();
		ralignC.Checked += (_, _) => preview();
		ralignR.Checked += (_, _) => preview();
		cautow.Checked += (_, _) => { syncsizeenabled(); preview(); };
		cautow.Unchecked += (_, _) => { syncsizeenabled(); preview(); };
		cautoh.Checked += (_, _) => { syncsizeenabled(); preview(); };
		cautoh.Unchecked += (_, _) => { syncsizeenabled(); preview(); };

		bfg.Click += (_, _) => pickcolor(efg, swfg, Colors.White);
		boutline.Click += (_, _) => pickcolor(eoutline, swoutline, Color.FromArgb(0xCC, 0, 0, 0));
		bbg.Click += (_, _) => pickcolor(ebg, swbg, Color.FromArgb(0x66, 0, 0, 0));
		bborder.Click += (_, _) => {
			pickcolor(eborder, swbd, Colors.Transparent);
			if (eborderth.Value < 0.5) {
				var c = ColorUtil.Parse(eborder.Text, Colors.Transparent);
				if (c.A > 0) {
					eborderth.Value = 1;
					lbborderth.Text = "1";
				}
			}
		};
		swfg.MouseLeftButtonUp += (_, _) => pickcolor(efg, swfg, Colors.White);
		swoutline.MouseLeftButtonUp += (_, _) => pickcolor(eoutline, swoutline, Color.FromArgb(0xCC, 0, 0, 0));
		swbg.MouseLeftButtonUp += (_, _) => pickcolor(ebg, swbg, Color.FromArgb(0x66, 0, 0, 0));
		swbd.MouseLeftButtonUp += (_, _) => {
			pickcolor(eborder, swbd, Colors.Transparent);
			if (eborderth.Value < 0.5) {
				var c = ColorUtil.Parse(eborder.Text, Colors.Transparent);
				if (c.A > 0) {
					eborderth.Value = 1;
					lbborderth.Text = "1";
				}
			}
		};

		bapply.Click += (_, _) => {
			writeback();
			applylive?.Invoke();
		};
		bok.Click += (_, _) => {
			cancelled = false;
			writeback();
			applylive?.Invoke();
			Close();
		};
		bcancel.Click += (_, _) => {
			cancelled = true;
			style.CopyFrom(snap);
			applylive?.Invoke();
			Close();
		};

		Loaded += (_, _) => {
			if (osdwin != null) {
				osdwin.SetEditMode(true);
				osdwin.GeometryChanged += onosdgeometry;
				loading = true;
				try {
					ewidth.Text = ((int)(style.Width > 0 ? style.Width : 720)).ToString();
					eheight.Text = ((int)(style.Height > 0 ? style.Height : 180)).ToString();
				}
				finally {
					loading = false;
				}
			}
		};
		Closed += (_, _) => {
			if (closed) return;
			closed = true;
			if (osdwin != null) {
				osdwin.GeometryChanged -= onosdgeometry;
				if (!cancelled) {
					writeback();
					applylive?.Invoke();
				}
				osdwin.SetEditMode(false);
				osdwin.ApplyStyle();
			}
		};
	}

	void onosdgeometry() {
		if (loading || closed) return;
		loading = true;
		try {
			ewidth.Text = ((int)style.Width).ToString();
			eheight.Text = ((int)style.Height).ToString();
			cautow.IsChecked = false;
			cautoh.IsChecked = false;
			syncsizeenabled();
		}
		finally {
			loading = false;
		}
	}

	void pickcolor(System.Windows.Controls.TextBox box, System.Windows.Controls.Border swatch, Color fallback) {
		var cur = ColorUtil.Parse(box.Text, fallback);
		try {
			var dlg = new HsvColorDialog(cur) { Owner = this };
			if (dlg.ShowDialog() != true) return;
			box.Text = ColorUtil.ToHex(dlg.SelectedColor);
			updateswatch(swatch, box.Text, fallback);
			// 选了有色边框但粗细为 0 时自动给 1
			if (ReferenceEquals(box, eborder) && eborderth.Value < 0.5 && dlg.SelectedColor.A > 0) {
				eborderth.Value = 1;
				lbborderth.Text = "1";
			}
			preview();
		}
		catch (Exception ex) {
			CaptureLog.Ex("pickcolor", ex);
		}
	}

	static void updateswatch(System.Windows.Controls.Border sw, string hex, Color fallback) {
		var c = ColorUtil.Parse(hex, fallback);
		sw.Background = new SolidColorBrush(c);
	}

	void loadui(AsrCaptionStyle o) {
		loading = true;
		try {
			efont.SelectedItem = o.FontFamily;
			if (efont.SelectedItem == null && efont.Items.Count > 0)
				efont.SelectedIndex = 0;
			esize.Value = o.FontSize;
			lbsize.Text = ((int)o.FontSize).ToString();
			efg.Text = o.Foreground ?? "#FFFFFFFF";
			eoutline.Text = o.Outline ?? "#CC000000";
			ebg.Text = o.Background ?? "#66000000";
			eborder.Text = o.BorderColor ?? "#00000000";
			eborderth.Value = o.BorderThickness;
			lbborderth.Text = ((int)o.BorderThickness).ToString();
			emaxw.Text = ((int)o.MaxWidth).ToString();
			ewidth.Text = ((int)(o.Width > 0 ? o.Width : 720)).ToString();
			eheight.Text = ((int)(o.Height > 0 ? o.Height : 180)).ToString();
			cautow.IsChecked = o.AutoWidth;
			cautoh.IsChecked = o.AutoHeight;
			ralignL.IsChecked = o.Align == 0;
			ralignC.IsChecked = o.Align == 1 || (o.Align != 0 && o.Align != 2);
			ralignR.IsChecked = o.Align == 2;
			updateswatch(swfg, efg.Text, Colors.White);
			updateswatch(swoutline, eoutline.Text, Color.FromArgb(0xCC, 0, 0, 0));
			updateswatch(swbg, ebg.Text, Colors.Transparent);
			updateswatch(swbd, eborder.Text, Colors.Transparent);
			syncsizeenabled();
		}
		finally {
			loading = false;
		}
	}

	void syncsizeenabled() {
		ewidth.IsEnabled = cautow.IsChecked != true;
		eheight.IsEnabled = cautoh.IsChecked != true;
	}

	void writeback() {
		style.FontFamily = efont.SelectedItem as string ?? style.FontFamily;
		style.FontSize = esize.Value;
		style.Foreground = string.IsNullOrWhiteSpace(efg.Text) ? "#FFFFFFFF" : efg.Text.Trim();
		style.Outline = string.IsNullOrWhiteSpace(eoutline.Text) ? "#CC000000" : eoutline.Text.Trim();
		style.Background = string.IsNullOrWhiteSpace(ebg.Text) ? "#66000000" : ebg.Text.Trim();
		style.BorderColor = string.IsNullOrWhiteSpace(eborder.Text) ? "#00000000" : eborder.Text.Trim();
		style.BorderThickness = eborderth.Value;
		if (double.TryParse(emaxw.Text, out var mw) && mw >= 100)
			style.MaxWidth = mw;
		if (double.TryParse(ewidth.Text, out var w) && w >= 80)
			style.Width = w;
		if (double.TryParse(eheight.Text, out var h) && h >= 40)
			style.Height = h;
		style.AutoWidth = cautow.IsChecked == true;
		style.AutoHeight = cautoh.IsChecked == true;
		if (ralignL.IsChecked == true) style.Align = 0;
		else if (ralignR.IsChecked == true) style.Align = 2;
		else style.Align = 1;
	}

	void preview() {
		if (loading) return;
		writeback();
		applylive?.Invoke();
	}
}
