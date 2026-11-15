using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>字幕/歌词的一行文字，附带字符边界，供蒙版推进和点词定位。</summary>
sealed class TextLineMetrics {
	public string Text = "";
	public double Width;
	public double Height;
	/// <summary>每个字符占的前进宽度累计，长度 = Text.Length + 1。</summary>
	public double[] Bounds;
}

/// <summary>
/// 定时文本视觉层：硬描边 + 已唱/未唱双层，用 Clip 推进进度蒙版。
/// 三层字形必须同字体同宽度，否则换行点不同会叠出双影。
/// </summary>
sealed class TimedTextLayer {
	readonly Grid root;
	readonly Canvas canvas;
	readonly List<LineVisual> lines = new();

	TimedTextCue cue;
	string lasttext;
	double lastp = -1;

	public double FontSize = 32;
	public FontFamily FontFamily = new("Microsoft YaHei UI");
	public Color SungColor = Color.FromRgb(0xFF, 0xD8, 0x4D);
	public Color UnsungColor = Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF);
	public Color OutlineColor = Color.FromArgb(0xCC, 0, 0, 0);
	public Color BackgroundColor = Color.FromArgb(0x66, 0, 0, 0);
	public double BottomMargin = 48;
	public bool Karaoke = true;
	/// <summary>true = 按字吸附；false = 连续扫。</summary>
	public bool SnapToChar = true;
	public double FeatherPx;

	/// <summary>点击定位用：最近一次绘制后的行度量。</summary>
	public List<TextLineMetrics> Metrics { get; } = new();

	/// <summary>某行在层内的左上角坐标，点词自测用。</summary>
	public void LineOrigin(int lineIdx, out double left, out double top) {
		left = 0;
		top = 0;
		if (lineIdx < 0 || lineIdx >= lines.Count) return;
		var l = lines[lineIdx];
		if (l == null) return;
		left = Canvas.GetLeft(l.Box);
		top = Canvas.GetTop(l.Box);
		if (double.IsNaN(left)) left = 0;
		if (double.IsNaN(top)) top = 0;
	}

	/// <summary>当前显示的文字，自测用。</summary>
	public string CueText => lasttext ?? "";
	/// <summary>当前拆出的行数，自测用。</summary>
	public int LineCount => lines.Count;

	/// <summary>排版诊断：每行文字、宽高、左上角坐标，自测用。</summary>
	public string Describe() {
		var sb = new System.Text.StringBuilder();
		sb.Append($"root={root.ActualWidth:0}x{root.ActualHeight:0} canvas={canvas.ActualWidth:0}x{canvas.ActualHeight:0}");
		for (var i = 0; i < lines.Count; i++) {
			var l = lines[i];
			if (l == null) { sb.Append(" | (空行)"); continue; }
			sb.Append($" | [{i}]「{l.Text}」 w={l.Width:0.#} h={l.Height:0.#} at=({Canvas.GetLeft(l.Box):0.#},{Canvas.GetTop(l.Box):0.#})");
		}
		return sb.ToString();
	}

	public TimedTextLayer(Grid host) {
		root = host;
		// Canvas 必须撑满，行位置才按左上角算；居中由每行的 cx 负责
		canvas = new Canvas {
			IsHitTestVisible = false,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
		};
		root.Children.Add(canvas);
		root.SizeChanged += (_, _) => relayout();
	}

	// ==================== 更新 ====================
	public void SetCue(TimedTextCue c, double p) {
		var text = c?.Text ?? "";
		if (!ReferenceEquals(cue, c) || !string.Equals(lasttext, text, StringComparison.Ordinal)) {
			cue = c;
			lasttext = text;
			lastp = -1;
			rebuild();
		}
		SetProgress(p);
	}

	/// <summary>只推进裁剪，不重建控件树；p 变化不足 0.5% 时跳过。</summary>
	public void SetProgress(double p) {
		if (p < 0) p = 0;
		if (p > 1) p = 1;
		if (!Karaoke) p = 1;
		if (Math.Abs(p - lastp) < 0.005 && lastp >= 0) return;
		lastp = p;
		if (lines.Count == 0) return;
		var total = 0.0;
		foreach (var l in lines) total += Math.Max(1, l.Text.Length);
		if (total <= 0) return;
		var used = 0.0;
		foreach (var l in lines) {
			var w = Math.Max(1, l.Text.Length) / total;
			var lp = (p * 1.0 - used) / w;
			if (lp < 0) lp = 0;
			if (lp > 1) lp = 1;
			l.SetProgress(lp);
			used += w;
		}
	}

	// ==================== 构建 ====================
	void rebuild() {
		foreach (var l in lines) l.Detach();
		lines.Clear();
		Metrics.Clear();
		if (string.IsNullOrEmpty(lasttext)) {
			canvas.Children.Clear();
			return;
		}
		canvas.Children.Clear();
		var maxw = Math.Max(120, root.ActualWidth - 80);
		var parts = lasttext.Replace("\r\n", "\n").Split('\n');
		foreach (var raw in parts) {
			var t = raw.TrimEnd();
			if (t.Length == 0) { lines.Add(null); continue; }
			var lv = new LineVisual(canvas, t, this, maxw);
			lines.Add(lv);
			Metrics.Add(lv.Metric);
		}
		relayout();
	}

	/// <summary>按当前可用宽高重新排版：底部居中，字号随窗口高度缩放。</summary>
	public void relayout() {
		if (lines.Count == 0) return;
		var availh = root.ActualHeight;
		if (availh <= 0) return;
		var scale = availh / 720.0;
		if (scale < 0.5) scale = 0.5;
		if (scale > 2.5) scale = 2.5;
		lastscale = scale;
		var fs = Math.Max(12, FontSize * scale);
		lastfs = fs;
		var maxw = Math.Max(120, root.ActualWidth - 80);
		// 音频条这类很矮的容器：把字号压到放得下为止，否则文字会被上边裁掉
		var rows = Math.Max(1, lines.Count);
		var cap = (availh - 8) / (rows * 1.45);
		if (cap > 8 && fs > cap) fs = cap;
		lastfs = fs;
		var totalh = 0.0;
		foreach (var l in lines) {
			if (l == null) { totalh += fs * 0.4; continue; }
			l.Layout(fs, maxw);
			totalh += l.Height + 4;
		}
		place(totalh, fs, availh, scale);
	}

	/// <summary>只按新的底部留白重排位置，不重新测量文字；拖动字幕时用，避免每帧重测。</summary>
	public void reposition() {
		if (lines.Count == 0) return;
		var availh = root.ActualHeight;
		if (availh <= 0) return;
		var fs = lastfs > 0 ? lastfs : FontSize;
		var totalh = 0.0;
		foreach (var l in lines) {
			if (l == null) { totalh += fs * 0.4; continue; }
			totalh += l.Height + 4;
		}
		place(totalh, fs, availh, lastscale > 0 ? lastscale : 1);
	}

	void place(double totalh, double fs, double availh, double scale) {
		// 底部留白最多把整段字顶到容器顶端，不能反过来把字挤出画面
		var cap = Math.Max(0, availh - totalh - 2);
		var margin = Math.Min(BottomMargin * scale, cap);
		var y = availh - margin - totalh;
		if (y < 2) y = 2;
		foreach (var l in lines) {
			if (l == null) { y += fs * 0.4; continue; }
			// 每行按自身宽度居中：canvas 没有显式尺寸，不能用它的 ActualWidth
			var cx = Math.Max(0, (root.ActualWidth - l.Width) / 2);
			l.Place(cx, y);
			y += l.Height + 4;
		}
	}

	/// <summary>当前排版用的缩放倍数：拖动字幕时把像素位移换算回 BottomMargin。</summary>
	public double Scale => lastscale > 0 ? lastscale : 1;
	double lastscale = 1;
	double lastfs;

	/// <summary>容器可视高度，拖动时用作上界。</summary>
	public double ViewHeight => root.ActualHeight;

	// ==================== 命中 ====================
	/// <summary>
	/// 屏幕点 → (行号, 字符下标)。返回 false 表示点在字幕外。
	/// 用每行的字符边界做二分，不重建 FormattedText。
	/// </summary>
	public bool HitTest(Point pt, out int lineIdx, out int charIdx) {
		lineIdx = -1;
		charIdx = -1;
		for (var i = 0; i < lines.Count; i++) {
			var l = lines[i];
			if (l == null) continue;
			var r = l.Bounds;
			if (!r.Contains(pt)) continue;
			lineIdx = i;
			var x = pt.X - r.Left;
			var idx = l.CharAt(x);
			if (idx < 0) return false;
			charIdx = idx;
			return true;
		}
		return false;
	}

	public string TextOf(int lineIdx) =>
		lineIdx >= 0 && lineIdx < lines.Count && lines[lineIdx] != null ? lines[lineIdx].Text : "";

	/// <summary>当前整体文本（多行用 \n 连），点词时按行取。</summary>
	public string LineText(int lineIdx) => TextOf(lineIdx);

	public void Clear() {
		cue = null;
		lasttext = null;
		lastp = -1;
		foreach (var l in lines) l?.Detach();
		lines.Clear();
		Metrics.Clear();
		canvas.Children.Clear();
	}

	// ==================== 单行 ====================
	sealed class LineVisual {
		const int OUTLINE_RING = 4;

		readonly Canvas host;
		readonly Grid box;
		readonly TextBlock unsung;
		readonly RectangleGeometry clip = new();

		public string Text { get; }
		public double Width { get; private set; }
		public double Height { get; private set; }
		/// <summary>排版容器，自测里取坐标用。</summary>
		public Grid Box => box;
		TextBlock sung;
		Rect placed;

		public Rect Bounds => placed;
		public TextLineMetrics Metric { get; } = new();
		double fontsize;
		double[] bounds;

		public LineVisual(Canvas host, string text, TimedTextLayer owner, double maxw) {
			this.host = host;
			this.owner = owner;
			Text = text;
			box = new Grid { IsHitTestVisible = false };
			var ring = new Grid();
			for (var i = 0; i < OUTLINE_RING; i++) {
				var ol = mk(owner, fontsize, maxw);
				ol.Foreground = new SolidColorBrush(owner.OutlineColor);
				ol.RenderTransform = new TranslateTransform(offsetx(i), offsety(i));
				ring.Children.Add(ol);
			}
			unsung = mk(owner, fontsize, maxw);
			unsung.Foreground = new SolidColorBrush(owner.UnsungColor);
			sung = mk(owner, fontsize, maxw);
			sung.Foreground = new SolidColorBrush(owner.SungColor);
			sung.Clip = clip;
			box.Children.Add(ring);
			box.Children.Add(unsung);
			box.Children.Add(sung);
			host.Children.Add(box);
		}

		readonly TimedTextLayer owner;

		static TextBlock mk(TimedTextLayer owner, double fs, double maxw) => new() {
			Text = "",
			FontFamily = owner.FontFamily,
			FontSize = fs > 0 ? fs : 32,
			FontWeight = FontWeights.SemiBold,
			TextWrapping = TextWrapping.Wrap,
			MaxWidth = maxw,
			IsHitTestVisible = false,
		};

		static double offsetx(int i) => i switch { 0 => -1.5, 1 => 1.5, 2 => 0, _ => 0 };
		static double offsety(int i) => i switch { 0 => 0, 1 => 0, 2 => -1.5, _ => 1.5 };

		public void Detach() {
			try { host.Children.Remove(box); } catch { }
		}

		public void Layout(double fs, double maxw) {
			fontsize = fs;
			var ff = owner.FontFamily;
			foreach (var child in ((Grid)box.Children[0]).Children) {
				var tb = (TextBlock)child;
				tb.Text = Text;
				tb.FontSize = fs;
				tb.FontFamily = ff;
				tb.MaxWidth = maxw;
			}
			unsung.Text = Text;
			unsung.FontSize = fs;
			unsung.FontFamily = ff;
			unsung.MaxWidth = maxw;
			sung.Text = Text;
			sung.FontSize = fs;
			sung.FontFamily = ff;
			sung.MaxWidth = maxw;
			// 只量不排：Canvas 会按 Canvas.Left/Top 安排子元素，
			// 这里再调 box.Arrange(Rect(0,0,...)) 会把定位覆盖成左上角，
			// 字幕就会跑到窗口左上角去。
			box.Measure(new Size(maxw, double.PositiveInfinity));
			Width = Math.Min(box.DesiredSize.Width, maxw);
			Height = box.DesiredSize.Height;
			computeBounds(fs);
		}

		/// <summary>逐字符测量，得到精确的字符边界供蒙版吸附和点词定位。</summary>
		void computeBounds(double fs) {
			var tf = new Typeface(owner.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
			bounds = new double[Text.Length + 1];
			bounds[0] = 0;
			var maxw = Math.Max(10, Width);
			for (var i = 1; i <= Text.Length; i++) {
				var pre = Text.Substring(0, i);
				var ptf = new FormattedText(pre, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
					tf, fs, Brushes.White, 1.0);
				ptf.MaxTextWidth = maxw;
				bounds[i] = ptf.WidthIncludingTrailingWhitespace;
			}
			Metric.Text = Text;
			Metric.Width = Width;
			Metric.Height = Height;
			Metric.Bounds = bounds;
		}

		public void Place(double x, double y) {
			Canvas.SetLeft(box, x);
			Canvas.SetTop(box, y);
			placed = new Rect(x, y, Width, Height);
			updateclip();
		}

		public void SetProgress(double p) {
			progress = p;
			updateclip();
		}

		double progress = 1;

		void updateclip() {
			var w = Width * progress;
			if (bounds != null && progress > 0 && progress < 1) {
				// 按字吸附：裁剪宽度对齐到字符边界
				var target = w;
				var best = 0.0;
				for (var i = 0; i < bounds.Length; i++) {
					if (bounds[i] <= target) best = bounds[i];
					else break;
				}
				w = best;
			}
			clip.Rect = new Rect(0, 0, w, Math.Max(1, Height));
		}

		public int CharAt(double x) {
			if (bounds == null || bounds.Length == 0) return -1;
			for (var i = 0; i + 1 < bounds.Length; i++) {
				if (x >= bounds[i] && x < bounds[i + 1]) return i;
			}
			return bounds.Length - 2;
		}
	}
}
