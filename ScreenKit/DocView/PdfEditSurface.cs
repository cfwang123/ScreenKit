using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfBrushes = System.Windows.Media.Brushes;
using IoPath = System.IO.Path;

namespace ScreenKit.DocView;

/// <summary>
/// PDF 编辑叠加层：在页面坐标系上放置/拖动文字与图片。
/// 由 PdfViewer 提供页布局回调。
/// </summary>
sealed class PdfEditSurface : Canvas {
	public enum Tool {
		Select = 0,
		AddText = 1,
		AddImage = 2,
	}

	readonly PdfEditDoc doc;
	readonly Dictionary<Guid, FrameworkElement> hosts = new();
	Func<int, (double Left, double Top, double W, double H)> pageLayout;
	Func<int, (double PtW, double PtH)> pageSizePt;
	Func<WpfPoint, int> hitPage;
	/// <summary>点在空白页上时：尝试捕获 PDF 已有文字/图片为可编辑对象。返回是否已处理。</summary>
	Func<int, double, double, bool> captureExisting;
	Tool tool = Tool.Select;
	Guid dragId = Guid.Empty;
	WpfPoint dragStart;
	double dragOrigX, dragOrigY;
	bool dirtyFlag;
	string pendingImagePath;
	// 默认样式
	public string DefaultFont = "Microsoft YaHei";
	public double DefaultFontSize = 12;
	public bool DefaultBold;
	public bool DefaultItalic;
	public MediaColor DefaultFore = MediaColor.FromRgb(0x11, 0x18, 0x27);

	public PdfEditDoc Doc => doc;
	public bool IsDirty => dirtyFlag || doc.Dirty;
	public event Action Changed;
	public event Action SelectionChanged;
	public Tool CurrentTool {
		get => tool;
		set {
			tool = value;
			Cursor = value == Tool.Select ? Cursors.Arrow : Cursors.Cross;
		}
	}

	public PdfEditSurface(PdfEditDoc editDoc) {
		doc = editDoc ?? new PdfEditDoc();
		Background = WpfBrushes.Transparent;
		IsHitTestVisible = true;
		SnapsToDevicePixels = true;
		MouseLeftButtonDown += ondown;
		MouseMove += onmove;
		MouseLeftButtonUp += onup;
		MouseLeftButtonDown += (_, e) => {
			if (e.ClickCount >= 2) {
				var it = doc.SelectedItem;
				if (it != null && it.Kind == PdfEditKind.Text)
					beginedittext(it);
				e.Handled = true;
			}
		};
	}

	public void SetLayout(
		Func<int, (double Left, double Top, double W, double H)> layout,
		Func<int, (double PtW, double PtH)> sizePt,
		Func<WpfPoint, int> pageAt,
		Func<int, double, double, bool> captureAt = null) {
		pageLayout = layout;
		pageSizePt = sizePt;
		hitPage = pageAt;
		captureExisting = captureAt;
	}

	/// <summary>外部（PdfViewer）加入已捕获的编辑项并刷新界面。</summary>
	public void AdoptItem(PdfEditItem it) {
		if (it == null) return;
		doc.DeselectAll();
		it.Selected = true;
		if (!doc.Items.Contains(it))
			doc.Items.Add(it);
		ensurehost(it);
		placehost(it);
		SetDirty(true);
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void SetDirty(bool d) {
		dirtyFlag = d;
		doc.Dirty = d;
		try { Changed?.Invoke(); } catch { /* ignore */ }
	}

	public void ClearSelection() {
		doc.DeselectAll();
		refreshchrome();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public PdfEditItem Selected => doc.SelectedItem;

	public void Relayout() {
		// 根据页布局重放所有 host
		foreach (var it in doc.Items)
			placehost(it);
	}

	public void RebuildAll() {
		Children.Clear();
		hosts.Clear();
		foreach (var it in doc.Items)
			ensurehost(it);
		Relayout();
	}

	public PdfEditItem AddTextAt(int page, double xPt, double yPt, string text = "文本") {
		var it = new PdfEditItem {
			Page = page,
			Kind = PdfEditKind.Text,
			X = xPt,
			Y = yPt,
			W = Math.Max(40, text.Length * DefaultFontSize * 0.6),
			H = DefaultFontSize * 1.6,
			Text = text ?? "文本",
			FontName = DefaultFont,
			FontSizePt = DefaultFontSize,
			Bold = DefaultBold,
			Italic = DefaultItalic,
			ForeColor = DefaultFore,
		};
		doc.DeselectAll();
		it.Selected = true;
		doc.Items.Add(it);
		ensurehost(it);
		placehost(it);
		SetDirty(true);
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
		return it;
	}

	public PdfEditItem AddImageAt(int page, double xPt, double yPt, string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
		byte[] png;
		try {
			// 统一成 PNG
			var bmp = new BitmapImage();
			bmp.BeginInit();
			bmp.UriSource = new Uri(IoPath.GetFullPath(path));
			bmp.CacheOption = BitmapCacheOption.OnLoad;
			bmp.EndInit();
			bmp.Freeze();
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(bmp));
			using var ms = new MemoryStream();
			enc.Save(ms);
			png = ms.ToArray();
			var wPt = bmp.PixelWidth * 72.0 / 96.0;
			var hPt = bmp.PixelHeight * 72.0 / 96.0;
			// 限制初始大小
			var max = 240.0;
			if (wPt > max || hPt > max) {
				var s = Math.Min(max / wPt, max / hPt);
				wPt *= s;
				hPt *= s;
			}
			var it = new PdfEditItem {
				Page = page,
				Kind = PdfEditKind.Image,
				X = xPt,
				Y = yPt,
				W = wPt,
				H = hPt,
				ImagePng = png,
			};
			doc.DeselectAll();
			it.Selected = true;
			doc.Items.Add(it);
			ensurehost(it);
			placehost(it);
			SetDirty(true);
			try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
			return it;
		} catch (Exception ex) {
			DocViewLog.Error("AddImageAt", ex);
			return null;
		}
	}

	public void DeleteSelected() {
		var it = doc.SelectedItem;
		if (it == null) return;
		doc.Items.Remove(it);
		if (hosts.TryGetValue(it.Id, out var el)) {
			Children.Remove(el);
			hosts.Remove(it.Id);
		}
		SetDirty(true);
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void ApplyStyleToSelected(Action<PdfEditItem> action) {
		var it = doc.SelectedItem;
		if (it == null || action == null) return;
		action(it);
		refreshhost(it);
		placehost(it);
		SetDirty(true);
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void ArmAddImage(string path) {
		pendingImagePath = path;
		CurrentTool = Tool.AddImage;
	}

	void ensurehost(PdfEditItem it) {
		if (hosts.ContainsKey(it.Id)) {
			refreshhost(it);
			return;
		}
		FrameworkElement el;
		if (it.Kind == PdfEditKind.Image) {
			var img = new System.Windows.Controls.Image {
				Stretch = Stretch.Fill,
				SnapsToDevicePixels = true,
			};
			try {
				if (it.ImagePng != null) {
					using var ms = new MemoryStream(it.ImagePng);
					var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
					img.Source = dec.Frames[0];
				}
			} catch { /* ignore */ }
			var border = new Border {
				Child = img,
				BorderBrush = WpfBrushes.Transparent,
				BorderThickness = new Thickness(1),
				Background = WpfBrushes.Transparent,
				Tag = it.Id,
				Cursor = Cursors.SizeAll,
			};
			el = border;
		} else {
			var tb = new TextBox {
				Text = it.Text ?? "",
				FontSize = Math.Max(9, it.FontSizePt * 96.0 / 72.0),
				FontFamily = new FontFamily(string.IsNullOrWhiteSpace(it.FontName)
					? "Microsoft YaHei UI" : it.FontName),
				FontWeight = it.Bold ? FontWeights.Bold : FontWeights.Normal,
				FontStyle = it.Italic ? FontStyles.Italic : FontStyles.Normal,
				Foreground = new SolidColorBrush(it.ForeColor),
				Background = it.BackColor.HasValue
					? new SolidColorBrush(it.BackColor.Value)
					: WpfBrushes.Transparent,
				BorderThickness = new Thickness(0),
				Padding = new Thickness(2, 0, 2, 0),
				AcceptsReturn = true,
				TextWrapping = TextWrapping.Wrap,
				Tag = it.Id,
			};
			tb.TextChanged += (_, _) => {
				if (tb.Tag is Guid id) {
					var item = doc.Find(id);
					if (item != null && item.Text != tb.Text) {
						item.Text = tb.Text;
						SetDirty(true);
					}
				}
			};
			var border = new Border {
				Child = tb,
				BorderBrush = WpfBrushes.Transparent,
				BorderThickness = new Thickness(1),
				Background = it.Kind == PdfEditKind.Whiteout
					? WpfBrushes.White
					: WpfBrushes.Transparent,
				Tag = it.Id,
				Cursor = Cursors.SizeAll,
			};
			el = border;
		}
		el.MouseLeftButtonDown += onhostdown;
		Children.Add(el);
		hosts[it.Id] = el;
		refreshchrome();
	}

	void refreshhost(PdfEditItem it) {
		if (!hosts.TryGetValue(it.Id, out var el)) return;
		if (el is Border b && b.Child is TextBox tb) {
			if (tb.Text != (it.Text ?? "")) tb.Text = it.Text ?? "";
			tb.FontSize = Math.Max(9, it.FontSizePt * 96.0 / 72.0);
			try {
				tb.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(it.FontName)
					? "Microsoft YaHei UI" : it.FontName);
			} catch { /* ignore */ }
			tb.FontWeight = it.Bold ? FontWeights.Bold : FontWeights.Normal;
			tb.FontStyle = it.Italic ? FontStyles.Italic : FontStyles.Normal;
			tb.Foreground = new SolidColorBrush(it.ForeColor);
			if (it.BackColor.HasValue)
				tb.Background = new SolidColorBrush(it.BackColor.Value);
		}
		refreshchrome();
	}

	void refreshchrome() {
		foreach (var kv in hosts) {
			var it = doc.Find(kv.Key);
			if (it == null || kv.Value is not Border b) continue;
			b.BorderBrush = it.Selected
				? new SolidColorBrush(MediaColor.FromRgb(0x25, 0x63, 0xEB))
				: WpfBrushes.Transparent;
			b.BorderThickness = new Thickness(it.Selected ? 1.5 : 1);
		}
	}

	void placehost(PdfEditItem it) {
		if (pageLayout == null || pageSizePt == null) return;
		if (!hosts.TryGetValue(it.Id, out var el)) return;
		var (left, top, pw, ph) = pageLayout(it.Page);
		var (ptW, ptH) = pageSizePt(it.Page);
		if (ptW < 1) ptW = 1;
		if (ptH < 1) ptH = 1;
		var sx = pw / ptW;
		var sy = ph / ptH;
		Canvas.SetLeft(el, left + it.X * sx);
		Canvas.SetTop(el, top + it.Y * sy);
		el.Width = Math.Max(8, it.W * sx);
		el.Height = Math.Max(8, it.H * sy);
	}

	void onhostdown(object sender, MouseButtonEventArgs e) {
		if (sender is not FrameworkElement fe || fe.Tag is not Guid id) return;
		var it = doc.Find(id);
		if (it == null) return;
		doc.DeselectAll();
		it.Selected = true;
		refreshchrome();
		try { SelectionChanged?.Invoke(); } catch { /* ignore */ }
		// 开始拖动（点在 TextBox 内且要编辑时不拖——双击编辑）
		if (e.ClickCount == 1 && tool == Tool.Select) {
			// 若点在 TextBox 且已聚焦，不抢拖动
			if (e.OriginalSource is TextBox) {
				e.Handled = true;
				return;
			}
			dragId = id;
			dragStart = e.GetPosition(this);
			dragOrigX = it.X;
			dragOrigY = it.Y;
			CaptureMouse();
			e.Handled = true;
		}
	}

	void ondown(object sender, MouseButtonEventArgs e) {
		if (e.Handled) return;
		var pt = e.GetPosition(this);
		if (hitPage == null || pageSizePt == null || pageLayout == null) return;
		var page = hitPage(pt);
		if (page < 0) return;
		var (left, top, pw, ph) = pageLayout(page);
		var (ptW, ptH) = pageSizePt(page);
		if (ptW < 1 || ptH < 1 || pw < 1 || ph < 1) return;
		var xPt = (pt.X - left) * ptW / pw;
		var yPt = (pt.Y - top) * ptH / ph;
		if (xPt < 0 || yPt < 0 || xPt > ptW || yPt > ptH) {
			// 点在页外：取消选择
			if (tool == Tool.Select) {
				ClearSelection();
			}
			return;
		}

		if (tool == Tool.AddText) {
			var it = AddTextAt(page, xPt, yPt, "文本");
			if (it != null) beginedittext(it);
			CurrentTool = Tool.Select;
			e.Handled = true;
			return;
		}
		if (tool == Tool.AddImage) {
			if (!string.IsNullOrEmpty(pendingImagePath)) {
				AddImageAt(page, xPt, yPt, pendingImagePath);
				pendingImagePath = null;
			}
			CurrentTool = Tool.Select;
			e.Handled = true;
			return;
		}
		// 选择工具点在空白页：尝试捕获 PDF 已有文字/图片
		if (tool == Tool.Select && e.OriginalSource == this) {
			if (captureExisting != null && captureExisting(page, xPt, yPt)) {
				e.Handled = true;
				return;
			}
			ClearSelection();
			e.Handled = true;
		}
	}

	void onmove(object sender, MouseEventArgs e) {
		if (dragId == Guid.Empty || e.LeftButton != MouseButtonState.Pressed) return;
		var it = doc.Find(dragId);
		if (it == null) return;
		var pt = e.GetPosition(this);
		if (pageLayout == null || pageSizePt == null) return;
		var (left, top, pw, ph) = pageLayout(it.Page);
		var (ptW, ptH) = pageSizePt(it.Page);
		if (ptW < 1 || ptH < 1 || pw < 1 || ph < 1) return;
		var dx = (pt.X - dragStart.X) * ptW / pw;
		var dy = (pt.Y - dragStart.Y) * ptH / ph;
		it.X = Math.Max(0, dragOrigX + dx);
		it.Y = Math.Max(0, dragOrigY + dy);
		placehost(it);
		SetDirty(true);
		e.Handled = true;
	}

	void onup(object sender, MouseButtonEventArgs e) {
		if (dragId != Guid.Empty) {
			dragId = Guid.Empty;
			try { ReleaseMouseCapture(); } catch { /* ignore */ }
			e.Handled = true;
		}
	}

	void beginedittext(PdfEditItem it) {
		if (!hosts.TryGetValue(it.Id, out var el)) return;
		if (el is Border b && b.Child is TextBox tb) {
			tb.Focus();
			tb.SelectAll();
		}
	}
}
