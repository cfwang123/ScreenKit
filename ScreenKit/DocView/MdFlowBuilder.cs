using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenKit.DocView;

/// <summary>
/// MdDoc → FlowDocument 预览（标题/列表/代码/表/链接可点）。
/// 块级 Paragraph 的 Tag 存 sourceLine0，用于滚动同步。
/// </summary>
static class MdFlowBuilder {
	/// <summary>与 MdHtmlBuilder body 一致：padding:20px 28px 40px；字号 14；line-height 1.55。</summary>
	const double BASE_FS = 14;
	/// <summary>
	/// HTML body 页边（LTRB）。注意：RichTextBox 对 FlowDocument.PagePadding 支持不完整，
	/// 实际留白应设在 previewRtb.Padding；此处仅用于 imgMaxW / 表格列宽。
	/// </summary>
	public const double PAGE_PAD_L = 28;
	public const double PAGE_PAD_R = 28;
	public const double PAGE_PAD_T = 20;
	public const double PAGE_PAD_B = 40;
	/// <summary>与 HTML indentStepPx 一致：列表每 tab 列 ≈ 25px。</summary>
	const int LIST_INDENT_STEP_PX = 25;

	static readonly SolidColorBrush CodeBg = brush(0xF3, 0xF4, 0xF6);
	static readonly SolidColorBrush CodeFg = brush(0x1F, 0x29, 0x37);
	static readonly SolidColorBrush QuoteBar = brush(0x9C, 0xA3, 0xAF);
	static readonly SolidColorBrush QuoteFg = brush(0x4B, 0x55, 0x63);
	static readonly SolidColorBrush LinkFg = brush(0x25, 0x63, 0xEB);
	static readonly SolidColorBrush MarkBg = brush(0xFE, 0xF0, 0x8A);
	static readonly SolidColorBrush HrBrush = brush(0xD1, 0xD5, 0xDB);
	static readonly SolidColorBrush H2Border = brush(0xE5, 0xE7, 0xEB);
	static readonly SolidColorBrush TableBorder = brush(0xD1, 0xD5, 0xDB);
	static readonly SolidColorBrush TableHeadBg = brush(0xF9, 0xFA, 0xFB);
	static readonly SolidColorBrush HeadMuted = brush(0x37, 0x41, 0x51);
	static readonly SolidColorBrush MarkFg = brush(0x37, 0x41, 0x51);
	// HTML: h1 2em / h2 1.55em / h3 1.25em / h4 1.1em / h5,h6 1em @ 14px
	static readonly double[] HeadSizes = { 28, 21.7, 17.5, 15.4, 14, 14 };

	static SolidColorBrush brush(byte r, byte g, byte b) {
		var br = new SolidColorBrush(Color.FromRgb(r, g, b));
		if (br.CanFreeze) br.Freeze();
		return br;
	}

	/// <param name="onLink">预览内点击链接时回调原始 href（#锚点 / 相对 md / http…）。</param>
	/// <param name="baseFilePath">当前 md 路径，用于解析相对图片。</param>
	/// <param name="embedImages">true 时内嵌显示本地/网络图片（Typora/预览）；false 仅文字链。</param>
	public static FlowDocument Build(MdDoc doc, double pageWidth = 720, Action<string> onLink = null,
		string baseFilePath = null, bool embedImages = true) {
		var pageW = pageWidth > 100 ? pageWidth : 720;
		// 字号/行高对齐 HTML；页边由宿主 RTB.Padding 承担（PagePadding 在 RTB 内几乎无效）
		var fd = new FlowDocument {
			FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji, 微软雅黑, Microsoft YaHei UI, sans-serif"),
			FontSize = BASE_FS,
			LineHeight = BASE_FS * 1.55,
			PagePadding = new Thickness(0),
			TextAlignment = TextAlignment.Left,
			ColumnWidth = double.PositiveInfinity,
			// pageW 应为「内容区宽」= RTB.ActualWidth - 左右 Padding
			PageWidth = pageW,
			Background = Brushes.White,
			Foreground = brush(0x11, 0x18, 0x27),
			Tag = onLink,
		};
		var imgMaxW = Math.Max(120, pageW);
		if (doc == null || doc.Blocks == null || doc.Blocks.Count == 0) {
			fd.Blocks.Add(new Paragraph(new Run("(空文档)")) {
				Foreground = QuoteFg,
			});
			return fd;
		}

		var tabSize = 3;
		try {
			tabSize = DocViewOptions.Current?.MdTabSize ?? 3;
			if (tabSize < 1) tabSize = 1;
			if (tabSize > 8) tabSize = 8;
		} catch { tabSize = 3; }

		// 有序序号：按缩进列宽分档（与 HTML olCount 一致）
		var olCount = new System.Collections.Generic.Dictionary<int, int>();

		var headAutoNum = true;
		try { headAutoNum = DocViewOptions.Current?.MdHeadingAutoNumber ?? true; } catch { /* keep true */ }
		var headNum = headAutoNum ? new MdHeadingNumber() : null;

		foreach (var b in doc.Blocks) {
			if (b.Kind != MdBlockKind.ListItem)
				olCount.Clear();

			switch (b.Kind) {
				case MdBlockKind.Blank:
					break;
				case MdBlockKind.Heading: {
					var lv = Math.Max(1, Math.Min(6, b.Level));
					// margin:1.1em 0 .5em
					var p = new Paragraph {
						FontSize = HeadSizes[lv - 1],
						FontWeight = FontWeights.SemiBold,
						LineHeight = HeadSizes[lv - 1] * 1.3,
						Margin = new Thickness(0, BASE_FS * 1.1, 0, BASE_FS * 0.5),
						Tag = b.SourceLine0,
					};
					if (lv >= 5)
						p.Foreground = HeadMuted;
					// h1/h2 底边（HTML border-bottom + padding-bottom）
					if (lv == 1) {
						p.BorderBrush = HrBrush;
						p.BorderThickness = new Thickness(0, 0, 0, 1);
						p.Padding = new Thickness(0, 0, 0, BASE_FS * 0.35);
					} else if (lv == 2) {
						p.BorderBrush = H2Border;
						p.BorderThickness = new Thickness(0, 0, 0, 1);
						p.Padding = new Thickness(0, 0, 0, BASE_FS * 0.3);
					}
					if (headNum != null && !string.IsNullOrWhiteSpace(b.Text)) {
						p.Inlines.Add(new Run(headNum.Next(lv) + " ") {
							Foreground = QuoteFg,
							FontWeight = FontWeights.SemiBold,
						});
					}
					appendspans(p.Inlines, b.Spans, b.Text, onLink, baseFilePath, embedImages, imgMaxW);
					fd.Blocks.Add(p);
					break;
				}
				case MdBlockKind.Paragraph: {
					// 整段仅一张图：块级大图
					if (embedImages && b.Spans != null && b.Spans.Count == 1 && b.Spans[0].Kind == "image") {
						var block = tryimageblock(b.Spans[0], baseFilePath, imgMaxW, b.SourceLine0, onLink);
						if (block != null) {
							fd.Blocks.Add(block);
							break;
						}
					}
					// p{margin:0 0 .75em}
					var p = new Paragraph {
						Margin = new Thickness(0, 0, 0, BASE_FS * 0.75),
						Tag = b.SourceLine0,
					};
					appendspans(p.Inlines, b.Spans, b.Text, onLink, baseFilePath, embedImages, imgMaxW);
					fd.Blocks.Add(p);
					break;
				}
				case MdBlockKind.Quote: {
					// blockquote: margin .75em 0 1em; padding .55em 1em; border-left 4px
					var p = new Paragraph {
						Margin = new Thickness(0, BASE_FS * 0.75, 0, BASE_FS),
						Padding = new Thickness(BASE_FS, BASE_FS * 0.55, BASE_FS, BASE_FS * 0.55),
						BorderBrush = QuoteBar,
						BorderThickness = new Thickness(4, 0, 0, 0),
						Foreground = QuoteFg,
						FontStyle = FontStyles.Italic,
						Background = CodeBg,
						Tag = b.SourceLine0,
					};
					appendspans(p.Inlines, b.Spans, b.Text, onLink, baseFilePath, embedImages, imgMaxW);
					fd.Blocks.Add(p);
					break;
				}
				case MdBlockKind.Code: {
					fd.Blocks.Add(buildcodeblock(b));
					break;
				}
				case MdBlockKind.Hr: {
					// hr: border-top 2px; margin 1.25em 0
					var p = new Paragraph {
						BorderBrush = HrBrush,
						BorderThickness = new Thickness(0, 0, 0, 2),
						Margin = new Thickness(0, BASE_FS * 1.25, 0, BASE_FS * 1.25),
						Padding = new Thickness(0),
						Tag = b.SourceLine0,
					};
					p.Inlines.Add(new Run(" ") { FontSize = 1 });
					fd.Blocks.Add(p);
					break;
				}
				case MdBlockKind.ListItem: {
					// 扁平列表：与 HTML .mdli + padding-left 一致（不用 WPF List 嵌套）
					var cols = Math.Max(0, b.Level);
					// 清更深档序号
					if (olCount.Count > 0) {
						var kill = new System.Collections.Generic.List<int>();
						foreach (var kv in olCount)
							if (kv.Key > cols) kill.Add(kv.Key);
						foreach (var k in kill) olCount.Remove(k);
					}
					string mark;
					if (b.Ordered) {
						if (!olCount.TryGetValue(cols, out var n)) n = 0;
						n++;
						olCount[cols] = n;
						mark = n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
					} else {
						mark = "•";
					}
					var pad = (int)Math.Round(cols * (double)LIST_INDENT_STEP_PX / tabSize);
					// .mdli margin-top/bottom .12em; line-height 1.45
					var p = new Paragraph {
						Margin = new Thickness(pad, BASE_FS * 0.12, 0, BASE_FS * 0.12),
						LineHeight = BASE_FS * 1.45,
						Tag = b.SourceLine0,
					};
					// .mdmark min-width ~1.25em + margin-right .4em
					p.Inlines.Add(new Run(mark + "  ") {
						Foreground = MarkFg,
						FontWeight = FontWeights.Normal,
					});
					if (b.TaskChecked.HasValue) {
						p.Inlines.Add(new Run(b.TaskChecked.Value ? "☑ " : "☐ ") {
							Foreground = MarkFg,
						});
					}
					appendspans(p.Inlines, b.Spans, b.Text, onLink, baseFilePath, embedImages, imgMaxW);
					fd.Blocks.Add(p);
					break;
				}
				case MdBlockKind.Table: {
					var table = buildtable(b, onLink, baseFilePath, embedImages, imgMaxW, pageW);
					table.Tag = b.SourceLine0;
					fd.Blocks.Add(table);
					break;
				}
				case MdBlockKind.Html: {
					var p = new Paragraph {
						FontFamily = new FontFamily("Consolas, monospace"),
						FontSize = 12,
						Foreground = QuoteFg,
						Background = TableHeadBg,
						// .htmlblock padding 8px
						Padding = new Thickness(8),
						Margin = new Thickness(0, BASE_FS * 0.5, 0, BASE_FS * 0.9),
						Tag = b.SourceLine0,
					};
					p.Inlines.Add(new Run(b.Text ?? ""));
					fd.Blocks.Add(p);
					break;
				}
				case MdBlockKind.HtmlImg: {
					var href = b.Text ?? (b.Spans != null && b.Spans.Count > 0 ? b.Spans[0].Href : null);
					var maxW = imgMaxW;
					if (b.ImgWidthPx != null && b.ImgWidthPx.Value > 0)
						maxW = Math.Min(imgMaxW, b.ImgWidthPx.Value);
					var el = tryloadimage(href, baseFilePath, maxW);
					if (el is Image imgEl) {
						if (b.ImgWidthPx != null && b.ImgWidthPx.Value > 0) {
							imgEl.Width = b.ImgWidthPx.Value;
							imgEl.MaxWidth = b.ImgWidthPx.Value;
						}
						if (b.ImgHeightPx != null && b.ImgHeightPx.Value > 0)
							imgEl.Height = b.ImgHeightPx.Value;
						imgEl.Stretch = Stretch.Uniform;
					}
					// figure.htmlimg margin .5em 0 .9em
					if (el != null) {
						fd.Blocks.Add(new BlockUIContainer(el) {
							Margin = new Thickness(0, BASE_FS * 0.5, 0, BASE_FS * 0.9),
							Tag = b.SourceLine0,
						});
					} else {
						var p = new Paragraph {
							Tag = b.SourceLine0,
							Margin = new Thickness(0, BASE_FS * 0.5, 0, BASE_FS * 0.9),
						};
						p.Inlines.Add(new Run("[img] " + (href ?? "")) { Foreground = QuoteFg });
						fd.Blocks.Add(p);
					}
					break;
				}
				case MdBlockKind.Details: {
					// details.mddetails margin .5em 0 .9em; padding .35em .85em .55em
					var exp = new Expander {
						Header = string.IsNullOrEmpty(b.Summary) ? "Details" : b.Summary,
						IsExpanded = b.DetailsOpen,
						Margin = new Thickness(0, BASE_FS * 0.5, 0, BASE_FS * 0.9),
						Padding = new Thickness(BASE_FS * 0.85, BASE_FS * 0.35, BASE_FS * 0.85, BASE_FS * 0.55),
						BorderBrush = brush(0xE5, 0xE7, 0xEB),
						BorderThickness = new Thickness(1),
						Background = brush(0xFA, 0xFA, 0xFA),
					};
					var innerDoc = new MdDoc { Blocks = b.Children ?? new System.Collections.Generic.List<MdBlock>() };
					var innerFd = Build(innerDoc, pageW, onLink, baseFilePath, embedImages);
					// 内层不再叠一层页边
					innerFd.PagePadding = new Thickness(0, 4, 0, 4);
					var host = new RichTextBox {
						Document = innerFd,
						IsReadOnly = true,
						IsDocumentEnabled = true,
						BorderThickness = new Thickness(0),
						Background = Brushes.Transparent,
						HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
						VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
					};
					exp.Content = host;
					fd.Blocks.Add(new BlockUIContainer(exp) { Tag = b.SourceLine0 });
					break;
				}
			}
		}
		return fd;
	}

	/// <summary>代码块：顶栏语言 + 复制按钮，正文可选中并带简易着色。</summary>
	static Block buildcodeblock(MdBlock b) {
		var codeText = b.Text ?? "";
		var lang = b.Lang ?? "";

		var btn = new Button {
			Content = "复制",
			Padding = new Thickness(10, 2, 10, 2),
			Margin = new Thickness(4, 0, 0, 0),
			FontSize = 11,
			Cursor = Cursors.Hand,
			ToolTip = "复制代码到剪贴板",
			Background = Brushes.White,
			BorderBrush = TableBorder,
			BorderThickness = new Thickness(1),
			VerticalAlignment = VerticalAlignment.Center,
		};
		btn.Click += (_, __) => {
			try {
				Clipboard.SetText(codeText);
				btn.Content = "已复制";
				var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
				t.Tick += (s, e2) => {
					btn.Content = "复制";
					t.Stop();
				};
				t.Start();
			} catch {
				btn.Content = "失败";
			}
		};

		var head = new DockPanel { Margin = new Thickness(8, 6, 8, 2), LastChildFill = true };
		DockPanel.SetDock(btn, Dock.Right);
		head.Children.Add(btn);
		head.Children.Add(new TextBlock {
			Text = string.IsNullOrEmpty(lang) ? "code" : lang,
			Foreground = QuoteFg,
			FontSize = 11,
			FontStyle = FontStyles.Italic,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(2, 0, 0, 0),
		});

		// 只读 RichTextBox：保留着色 + 可选中
		var codePara = new Paragraph {
			Margin = new Thickness(0),
			Padding = new Thickness(0),
			LineHeight = 18,
			FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New, monospace"),
			FontSize = 12.5,
			Foreground = CodeFg,
		};
		appendcode(codePara.Inlines, codeText, lang);
		var codeDoc = new FlowDocument(codePara) {
			PagePadding = new Thickness(0),
			TextAlignment = TextAlignment.Left,
			ColumnWidth = double.PositiveInfinity,
		};
		var rtb = new RichTextBox {
			Document = codeDoc,
			IsReadOnly = true,
			IsDocumentEnabled = true,
			BorderThickness = new Thickness(0),
			Background = Brushes.Transparent,
			Padding = new Thickness(10, 4, 10, 10),
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI Emoji, Courier New, monospace"),
			FontSize = 12.5,
			Foreground = CodeFg,
			Focusable = true,
		};

		var stack = new StackPanel();
		stack.Children.Add(head);
		stack.Children.Add(rtb);

		// .codewrap margin:.5em 0 .9em
		var border = new Border {
			Background = CodeBg,
			BorderBrush = TableBorder,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(4),
			Margin = new Thickness(0, BASE_FS * 0.5, 0, BASE_FS * 0.9),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Child = stack,
			Tag = codeText, // 备用
		};

		return new BlockUIContainer(border) {
			Tag = b.SourceLine0,
			Margin = new Thickness(0),
			Padding = new Thickness(0),
		};
	}

	static Block tryimageblock(MdSpan sp, string baseFilePath, double maxW, int sourceLine0, Action<string> onLink) {
		var el = tryloadimage(sp?.Href, baseFilePath, maxW);
		if (el == null) return null;
		var cap = string.IsNullOrEmpty(sp.Text) ? null : sp.Text;
		var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
		stack.Children.Add(el);
		if (!string.IsNullOrEmpty(cap)) {
			stack.Children.Add(new TextBlock {
				Text = cap,
				FontSize = 12,
				Foreground = QuoteFg,
				TextAlignment = TextAlignment.Center,
				Margin = new Thickness(0, 4, 0, 0),
			});
		}
		// figure{margin:.6em 0 .9em}
		return new BlockUIContainer(new Border {
			Child = stack,
			Margin = new Thickness(0, BASE_FS * 0.6, 0, BASE_FS * 0.9),
			HorizontalAlignment = HorizontalAlignment.Stretch,
		}) { Tag = sourceLine0 };
	}

	/// <summary>加载本地/网络图（预览 FlowDocument 与 Typora 源码区共用）。</summary>
	public static FrameworkElement TryLoadImage(string href, string baseFilePath, double maxW) =>
		tryloadimage(href, baseFilePath, maxW);

	/// <summary>Typora 源码区嵌表（只读视觉，样式对齐预览）。</summary>
	public static Table BuildEditorTable(MdBlock b, string baseFilePath, double pageW) {
		var imgMax = pageW > 200 ? Math.Min(420, pageW * 0.45) : 200;
		var table = buildtable(b, null, baseFilePath, embedImages: true, imgMaxW: imgMax, pageW: pageW);
		table.CellSpacing = 0;
		table.Margin = new Thickness(0, 4, 0, 8);
		return table;
	}

	static FrameworkElement tryloadimage(string href, string baseFilePath, double maxW) {
		if (string.IsNullOrWhiteSpace(href)) return null;
		href = href.Trim();
		try {
			BitmapImage bmp = null;
			string localPath = null;
			if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
				|| href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
				bmp = new BitmapImage();
				bmp.BeginInit();
				bmp.UriSource = new Uri(href, UriKind.Absolute);
				bmp.CacheOption = BitmapCacheOption.OnLoad;
				bmp.EndInit();
			} else {
				var path = MdViewer.ResolveHrefPath(baseFilePath, href);
				if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
				localPath = path;
				bmp = new BitmapImage();
				bmp.BeginInit();
				bmp.UriSource = new Uri(path, UriKind.Absolute);
				bmp.CacheOption = BitmapCacheOption.OnLoad;
				bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				bmp.EndInit();
			}
			if (bmp.CanFreeze) bmp.Freeze();
			var img = new Image {
				Source = bmp,
				MaxWidth = maxW > 80 ? maxW : 400,
				MaxHeight = 420,
				Stretch = Stretch.Uniform,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 2, 0, 2),
				SnapsToDevicePixels = true,
				Cursor = System.Windows.Input.Cursors.Hand,
				ToolTip = "双击预览 · 右键复制/保存",
			};
			ImageOverlay.Wire(img, bmp, localPath);
			return img;
		} catch {
			return null;
		}
	}

	/// <summary>
	/// 表格列宽（mdview）：短列钉死；need 总和 ≤ 页宽 → 像素列（表不撑满）；否则 Star 铺满并换行。
	/// </summary>
	static Table buildtable(MdBlock b, Action<string> onLink, string baseFilePath, bool embedImages,
		double imgMaxW, double pageW) {
		// table margin:.5em 0 .9em；th,td padding:6px 8px
		var table = new Table {
			CellSpacing = 0,
			BorderBrush = TableBorder,
			BorderThickness = new Thickness(1),
			Margin = new Thickness(0, BASE_FS * 0.5, 0, BASE_FS * 0.9),
		};
		if (b.TableRows == null || b.TableRows.Count == 0) return table;
		var cols = 0;
		foreach (var r in b.TableRows)
			if (r != null && r.Length > cols) cols = r.Length;
		if (cols <= 0) return table;

		const double cellPadH = 16; // 左右 Padding 8+8
		// pageW 已是去掉 RTB 左右 padding 后的内容宽；
		// Table 外边框（左右各1px）在列宽之外额外占宽，扣除以免超出页面
		var availDip = Math.Max(cols * 28.0, pageW - 2);
		var colDip = MdTableLayout.AllocateColumnsDip(
			b.TableRows, cols, pageW,
			unitDip: MdTableLayout.DEFAULT_UNIT_DIP,
			pagePadH: 2,
			cellPadH: cellPadH,
			fontSize: BASE_FS);

		if (colDip == null || colDip.Length != cols) {
			for (var c = 0; c < cols; c++)
				table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
		} else {
			double colSum = 0;
			for (var c = 0; c < cols; c++)
				colSum += colDip[c] > 20 ? colDip[c] : 40;
			var shrink = colSum < availDip - 0.5;
			for (var c = 0; c < cols; c++) {
				var w = colDip[c] > 20 ? colDip[c] : 40;
				// 短表：固定像素宽（不撑满 PageWidth）；长表：Star 比例铺满
				table.Columns.Add(new TableColumn {
					Width = shrink
						? new GridLength(w, GridUnitType.Pixel)
						: new GridLength(w, GridUnitType.Star),
				});
			}
			try { DocViewLog.Info($"buildtable: pageW={pageW} cols={cols} colSum={colSum} avail={availDip} shrink={shrink}"); } catch { }
		}

		var rg = new TableRowGroup();
		table.RowGroups.Add(rg);
		for (var ri = 0; ri < b.TableRows.Count; ri++) {
			var row = new TableRow();
			if (ri == 0) row.Background = TableHeadBg;
			var cells = b.TableRows[ri] ?? Array.Empty<string>();
			for (var ci = 0; ci < cols; ci++) {
				var txt = ci < cells.Length ? cells[ci] : "";
				var colW = colDip != null && ci < colDip.Length ? colDip[ci] : availDip / cols;
				var colImgMax = Math.Max(40, Math.Min(imgMaxW, colW - cellPadH));
				var p = new Paragraph {
					Margin = new Thickness(0),
					Padding = new Thickness(0),
					TextAlignment = TextAlignment.Left,
					KeepTogether = false,
				};
				if (ri == 0) p.FontWeight = FontWeights.SemiBold;
				appendspans(p.Inlines, MdParser.ParseInlines(txt), txt, onLink, baseFilePath, embedImages, colImgMax);
				var align = "left";
				if (b.TableAlign != null && ci < b.TableAlign.Count)
					align = b.TableAlign[ci];
				p.TextAlignment = align == "center" ? TextAlignment.Center
					: align == "right" ? TextAlignment.Right : TextAlignment.Left;
				var cell = new TableCell(p) {
					BorderBrush = TableBorder,
					BorderThickness = new Thickness(0.5),
					Padding = new Thickness(8, 6, 8, 6),
				};
				row.Cells.Add(cell);
			}
			rg.Rows.Add(row);
		}
		return table;
	}

	static void appendspans(InlineCollection inlines, System.Collections.Generic.List<MdSpan> spans, string fallback,
		Action<string> onLink, string baseFilePath, bool embedImages, double imgMaxW) {
		if (spans == null || spans.Count == 0) {
			if (!string.IsNullOrEmpty(fallback))
				inlines.Add(new Run(fallback));
			return;
		}
		foreach (var sp in spans) {
			if (sp == null) continue;
			switch (sp.Kind) {
				case "bold":
					inlines.Add(new Bold(new Run(sp.Text ?? "")));
					break;
				case "italic":
					inlines.Add(new Italic(new Run(sp.Text ?? "")));
					break;
				case "code":
					inlines.Add(new Run(sp.Text ?? "") {
						FontFamily = new FontFamily("Consolas, monospace"),
						Background = CodeBg,
						Foreground = CodeFg,
					});
					break;
				case "strike": {
					var r = new Run(sp.Text ?? "");
					r.TextDecorations = TextDecorations.Strikethrough;
					inlines.Add(r);
					break;
				}
				case "mark":
					inlines.Add(new Run(sp.Text ?? "") { Background = MarkBg });
					break;
				case "softbr":
					inlines.Add(new LineBreak());
					break;
				case "link": {
					var href = sp.Href ?? "";
					var link = new Hyperlink(new Run(sp.Text ?? href)) {
						NavigateUri = tryuri(href),
						Foreground = LinkFg,
						ToolTip = tipfor(href),
						Tag = href,
						Cursor = System.Windows.Input.Cursors.Hand,
					};
					link.RequestNavigate += (s, e) => {
						e.Handled = true;
						// 优先 Tag 原始 href（非 mdlink:// 伪 URI）
						var h = (s as Hyperlink)?.Tag as string;
						if (string.IsNullOrEmpty(h))
							h = e.Uri?.OriginalString;
						if (!string.IsNullOrEmpty(h))
							onLink?.Invoke(h);
					};
					inlines.Add(link);
					break;
				}
				case "image": {
					var href = sp.Href ?? "";
					var label = string.IsNullOrEmpty(sp.Text) ? "image" : sp.Text;
					if (embedImages) {
						// imgMaxW 由调用方限制（正文≈页宽；表格内≈本列宽）
						var img = tryloadimage(href, baseFilePath, Math.Max(40, imgMaxW));
						if (img != null) {
							img.MaxHeight = Math.Min(200, Math.Max(48, imgMaxW));
							inlines.Add(new InlineUIContainer(img) {
								BaselineAlignment = BaselineAlignment.Center,
							});
							if (!string.IsNullOrEmpty(sp.Text))
								inlines.Add(new Run(" " + label) { Foreground = QuoteFg, FontSize = 11 });
							break;
						}
					}
					var link = new Hyperlink(new Run("🖼 " + label)) {
						NavigateUri = tryuri(href),
						Foreground = LinkFg,
						ToolTip = href,
						Tag = href,
						Cursor = Cursors.Hand,
					};
					link.RequestNavigate += (s, e) => {
						e.Handled = true;
						var h = (s as Hyperlink)?.Tag as string ?? e.Uri?.OriginalString;
						if (!string.IsNullOrEmpty(h))
							onLink?.Invoke(h);
					};
					inlines.Add(link);
					break;
				}
				default:
					inlines.Add(new Run(sp.Text ?? ""));
					break;
			}
		}
	}

	/// <summary>代码语法高亮（编辑区围栏代码块 / 预览 Flow 共用）。</summary>
	public static void AppendCode(InlineCollection inlines, string code, string lang) =>
		appendcode(inlines, code, lang);

	/// <summary>整文件代码预览：按扩展名着色；过大则纯文本。</summary>
	public const int CODE_HL_MAX_CHARS = 250_000;
	public const int CODE_HL_MAX_LINES = 12_000;

	/// <summary>根据文件路径推断语言标记（供 TextViewer / 围栏共用）。</summary>
	public static string LangFromPath(string path) {
		if (string.IsNullOrWhiteSpace(path)) return "";
		var ext = System.IO.Path.GetExtension(path);
		if (string.IsNullOrEmpty(ext)) return "";
		switch (ext.ToLowerInvariant()) {
			case ".cs": return "cs";
			case ".py":
			case ".pyw": return "python";
			case ".php": return "php";
			case ".js":
			case ".mjs":
			case ".cjs": return "javascript";
			case ".ts": return "typescript";
			case ".jsx": return "jsx";
			case ".tsx": return "tsx";
			case ".lua": return "lua";
			case ".html":
			case ".htm": return "html";
			case ".css":
			case ".scss":
			case ".less": return "css";
			case ".json": return "json";
			case ".xml": return "xml";
			case ".sql": return "sql";
			case ".sh":
			case ".bash":
			case ".zsh": return "bash";
			case ".bat":
			case ".cmd":
			case ".ps1": return "bash";
			case ".java":
			case ".kt": return "java";
			case ".go": return "go";
			case ".rs": return "rust";
			case ".c":
			case ".h": return "c";
			case ".cpp":
			case ".cc":
			case ".cxx":
			case ".hpp":
			case ".hxx": return "cpp";
			case ".rb": return "ruby";
			case ".pl": return "perl";
			case ".r": return "r";
			case ".yaml":
			case ".yml": return "yaml";
			case ".toml":
			case ".ini":
			case ".cfg":
			case ".conf": return "ini";
			case ".md":
			case ".markdown": return "markdown";
			case ".txt":
			case ".log":
			case ".text": return "text";
			default: return ext.TrimStart('.').ToLowerInvariant();
		}
	}

	/// <summary>构建只读代码预览 FlowDocument（语法着色；可选行号）。</summary>
	public static FlowDocument BuildCodeDocument(string code, string lang, double fontSize = 14,
		bool lineNumbers = true) {
		if (fontSize < 8) fontSize = 8;
		var lh = fontSize * 1.45;
		var fd = new FlowDocument {
			FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI Emoji, 微软雅黑, monospace"),
			FontSize = fontSize,
			LineHeight = lh,
			PagePadding = new Thickness(12, 10, 16, 20),
			TextAlignment = TextAlignment.Left,
			ColumnWidth = double.PositiveInfinity,
			Background = Brushes.White,
		};
		code ??= "";
		var text = code.Replace("\r\n", "\n").Replace('\r', '\n');
		var parts = text.Length == 0 ? new[] { "" } : text.Split('\n');
		var lineCount = parts.Length;
		// 过大：纯文本 + 可选行号
		var noHl = code.Length > CODE_HL_MAX_CHARS || lineCount > CODE_HL_MAX_LINES
			|| string.IsNullOrEmpty(lang) || lang == "text" || lang == "txt" || lang == "log"
			|| lang == "plaintext";
		var numW = lineCount.ToString().Length;
		if (numW < 3) numW = 3;
		var numBrush = brush(0x9C, 0xA3, 0xAF);
		for (var i = 0; i < parts.Length; i++) {
			var p = new Paragraph {
				Margin = new Thickness(0),
				Padding = new Thickness(0),
				LineHeight = lh,
				FontFamily = fd.FontFamily,
				FontSize = fontSize,
			};
			if (lineNumbers) {
				var label = (i + 1).ToString().PadLeft(numW) + "  ";
				p.Inlines.Add(new Run(label) {
					Foreground = numBrush,
					FontSize = fontSize * 0.92,
				});
			}
			if (noHl)
				p.Inlines.Add(new Run(parts[i]) { Foreground = CodeFg });
			else {
				appendcode(p.Inlines, parts[i], lang);
				if (p.Inlines.Count <= (lineNumbers ? 1 : 0))
					p.Inlines.Add(new Run("") { Foreground = CodeFg });
			}
			fd.Blocks.Add(p);
		}
		if (fd.Blocks.Count == 0)
			fd.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0) });
		return fd;
	}

	static void appendcode(InlineCollection inlines, string code, string lang) {
		if (string.IsNullOrEmpty(code)) {
			inlines.Add(new Run(""));
			return;
		}
		var style = CodestyleFor(lang);
		var keywords = style.Keywords;
		var i = 0;
		while (i < code.Length) {
			// 模板/反引号字符串（JS）
			if (style.BacktickString && code[i] == '`') {
				var j = i + 1;
				while (j < code.Length) {
					if (code[j] == '\\' && j + 1 < code.Length) { j += 2; continue; }
					if (code[j] == '`') { j++; break; }
					j++;
				}
				inlines.Add(new Run(code.Substring(i, j - i)) { Foreground = StrBrush });
				i = j;
				continue;
			}
			// 字符串 " ' 
			if (code[i] == '"' || code[i] == '\'') {
				var q = code[i];
				var j = i + 1;
				// 原始字符串 @""（C#）简化：@"..."
				if (q == '"' && i > 0 && code[i - 1] == '@') {
					// @ 已作为独立字符输出；此处吃掉字符串
				}
				while (j < code.Length) {
					if (code[j] == '\\' && j + 1 < code.Length) { j += 2; continue; }
					if (code[j] == q) { j++; break; }
					j++;
				}
				inlines.Add(new Run(code.Substring(i, j - i)) { Foreground = StrBrush });
				i = j;
				continue;
			}
			// 块注释 /* */
			if (style.SlashComment && code[i] == '/' && i + 1 < code.Length && code[i + 1] == '*') {
				var j = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
				j = j < 0 ? code.Length : j + 2;
				inlines.Add(new Run(code.Substring(i, j - i)) { Foreground = CmtBrush, FontStyle = FontStyles.Italic });
				i = j;
				continue;
			}
			// 行注释 //
			if (style.SlashComment && code[i] == '/' && i + 1 < code.Length && code[i + 1] == '/') {
				var j = code.IndexOf('\n', i);
				if (j < 0) j = code.Length;
				inlines.Add(new Run(code.Substring(i, j - i)) { Foreground = CmtBrush, FontStyle = FontStyles.Italic });
				i = j;
				continue;
			}
			// -- 注释（SQL/Lua）
			if (style.DashComment && code[i] == '-' && i + 1 < code.Length && code[i + 1] == '-') {
				var j = code.IndexOf('\n', i);
				if (j < 0) j = code.Length;
				inlines.Add(new Run(code.Substring(i, j - i)) { Foreground = CmtBrush, FontStyle = FontStyles.Italic });
				i = j;
				continue;
			}
			// # 注释（python/shell）
			if (style.HashComment && code[i] == '#'
				&& (i == 0 || code[i - 1] == '\n' || char.IsWhiteSpace(code[i - 1]))) {
				var j = code.IndexOf('\n', i);
				if (j < 0) j = code.Length;
				inlines.Add(new Run(code.Substring(i, j - i)) { Foreground = CmtBrush, FontStyle = FontStyles.Italic });
				i = j;
				continue;
			}
			// 标识符 / 关键字
			if (char.IsLetter(code[i]) || code[i] == '_' || code[i] == '$') {
				var j = i + 1;
				while (j < code.Length && (char.IsLetterOrDigit(code[j]) || code[j] == '_' || code[j] == '$'))
					j++;
				var w = code.Substring(i, j - i);
				if (keywords != null && keywords.Contains(w))
					inlines.Add(new Run(w) { Foreground = KwBrush, FontWeight = FontWeights.SemiBold });
				else if (style.Types != null && style.Types.Contains(w))
					inlines.Add(new Run(w) { Foreground = TypeBrush });
				else
					inlines.Add(new Run(w) { Foreground = CodeFg });
				i = j;
				continue;
			}
			// 数字（含 0x / 后缀）
			if (char.IsDigit(code[i])
				|| (code[i] == '.' && i + 1 < code.Length && char.IsDigit(code[i + 1]))) {
				var j = i + 1;
				if (code[i] == '0' && j < code.Length && (code[j] == 'x' || code[j] == 'X')) {
					j++;
					while (j < code.Length && (char.IsDigit(code[j])
						|| (code[j] >= 'a' && code[j] <= 'f')
						|| (code[j] >= 'A' && code[j] <= 'F')))
						j++;
				} else {
					while (j < code.Length && (char.IsDigit(code[j]) || code[j] == '.' || code[j] == '_'
						|| code[j] == 'e' || code[j] == 'E' || code[j] == '+' || code[j] == '-'))
						j++;
					while (j < code.Length && (code[j] == 'f' || code[j] == 'F' || code[j] == 'd'
						|| code[j] == 'D' || code[j] == 'L' || code[j] == 'l' || code[j] == 'u' || code[j] == 'U'))
						j++;
				}
				inlines.Add(new Run(code.Substring(i, j - i)) { Foreground = NumBrush });
				i = j;
				continue;
			}
			// 其它符号
			inlines.Add(new Run(code[i].ToString()) { Foreground = CodeFg });
			i++;
		}
	}

	static readonly SolidColorBrush KwBrush = brush(0x7C, 0x3A, 0xED);
	static readonly SolidColorBrush TypeBrush = brush(0x0E, 0x74, 0x90);
	static readonly SolidColorBrush StrBrush = brush(0x05, 0x96, 0x69);
	static readonly SolidColorBrush CmtBrush = brush(0x9C, 0xA3, 0xAF);
	static readonly SolidColorBrush NumBrush = brush(0xD9, 0x77, 0x06);

	struct CodeStyle {
		public System.Collections.Generic.HashSet<string> Keywords;
		public System.Collections.Generic.HashSet<string> Types;
		public bool SlashComment;
		public bool HashComment;
		public bool DashComment;
		public bool BacktickString;
	}

	static CodeStyle CodestyleFor(string lang) {
		lang = (lang ?? "").Trim().ToLowerInvariant();
		// 去常见前缀
		if (lang.StartsWith("language-", StringComparison.Ordinal))
			lang = lang.Substring("language-".Length);
		switch (lang) {
			case "cs":
			case "csharp":
			case "c#":
				return new CodeStyle { Keywords = CsKw, Types = CsTypes, SlashComment = true };
			case "js":
			case "javascript":
			case "ts":
			case "typescript":
			case "jsx":
			case "tsx":
				return new CodeStyle {
					Keywords = JsKw, Types = JsTypes, SlashComment = true, BacktickString = true,
				};
			case "py":
			case "python":
				return new CodeStyle { Keywords = PyKw, HashComment = true };
			case "php":
				return new CodeStyle {
					Keywords = PhpKw, Types = PhpTypes, SlashComment = true, HashComment = true,
				};
			case "lua":
				return new CodeStyle { Keywords = LuaKw, DashComment = true };
			case "css":
			case "scss":
			case "less":
				return new CodeStyle { Keywords = CssKw, Types = CssTypes, SlashComment = true };
			case "json":
				return new CodeStyle { Keywords = JsonKw };
			case "sql":
				return new CodeStyle { Keywords = SqlKw, DashComment = true, SlashComment = true };
			case "bash":
			case "sh":
			case "shell":
			case "zsh":
			case "bat":
			case "cmd":
			case "ps1":
			case "powershell":
				return new CodeStyle { Keywords = BashKw, HashComment = true };
			case "java":
			case "kotlin":
			case "kt":
				return new CodeStyle { Keywords = JavaKw, Types = JavaTypes, SlashComment = true };
			case "go":
			case "golang":
				return new CodeStyle { Keywords = GoKw, Types = GoTypes, SlashComment = true };
			case "rs":
			case "rust":
				return new CodeStyle { Keywords = RustKw, Types = RustTypes, SlashComment = true };
			case "c":
			case "cpp":
			case "c++":
			case "h":
			case "hpp":
			case "hxx":
			case "cc":
			case "cxx":
				return new CodeStyle { Keywords = CKw, Types = CTypes, SlashComment = true };
			case "xml":
			case "html":
			case "htm":
			case "vue":
			case "svelte":
				return new CodeStyle { Keywords = HtmlKw, SlashComment = true };
			case "yaml":
			case "yml":
			case "ini":
			case "toml":
			case "conf":
			case "cfg":
				return new CodeStyle { Keywords = CommonKw, HashComment = true };
			case "text":
			case "txt":
			case "log":
			case "plaintext":
				return new CodeStyle();
			default:
				return new CodeStyle {
					Keywords = CommonKw, SlashComment = true, HashComment = true, DashComment = true,
					BacktickString = true,
				};
		}
	}

	static readonly System.Collections.Generic.HashSet<string> CommonKw = new(StringComparer.Ordinal) {
		"if", "else", "for", "while", "return", "function", "var", "let", "const", "class", "new",
		"true", "false", "null", "break", "continue", "switch", "case", "default", "try", "catch",
		"throw", "import", "export", "from", "async", "await",
	};
	static readonly System.Collections.Generic.HashSet<string> CsKw = new(StringComparer.Ordinal) {
		"if", "else", "for", "foreach", "while", "do", "return", "class", "struct", "interface", "namespace",
		"using", "public", "private", "protected", "internal", "static", "void", "var", "new", "true", "false",
		"null", "this", "base", "async", "await", "override", "virtual", "sealed", "abstract", "partial",
		"readonly", "const", "get", "set", "init", "record", "where", "select", "from", "in", "out", "ref",
		"is", "as", "typeof", "nameof", "switch", "case", "default", "break", "continue", "try", "catch",
		"finally", "throw", "lock", "fixed", "unsafe", "checked", "unchecked", "delegate", "event", "yield",
		"when", "and", "or", "not",
	};
	static readonly System.Collections.Generic.HashSet<string> CsTypes = new(StringComparer.Ordinal) {
		"int", "string", "bool", "byte", "char", "decimal", "double", "float", "long", "object", "short",
		"uint", "ulong", "ushort", "nint", "nuint", "Task", "List", "Dictionary", "IEnumerable", "Action",
		"Func", "Span", "ReadOnlySpan", "StringBuilder", "DateTime", "TimeSpan", "Exception",
	};
	static readonly System.Collections.Generic.HashSet<string> JsKw = new(StringComparer.Ordinal) {
		"if", "else", "for", "while", "do", "return", "function", "var", "let", "const", "class", "new",
		"true", "false", "null", "undefined", "async", "await", "import", "export", "from", "default",
		"this", "typeof", "of", "in", "instanceof", "break", "continue", "switch", "case", "try", "catch",
		"finally", "throw", "yield", "extends", "super", "static", "get", "set", "delete", "void", "with",
	};
	static readonly System.Collections.Generic.HashSet<string> JsTypes = new(StringComparer.Ordinal) {
		"string", "number", "boolean", "object", "symbol", "bigint", "any", "never", "unknown", "void",
		"Array", "Promise", "Map", "Set", "Record", "Partial", "Readonly",
	};
	static readonly System.Collections.Generic.HashSet<string> PyKw = new(StringComparer.Ordinal) {
		"if", "elif", "else", "for", "while", "return", "def", "class", "import", "from", "as", "True",
		"False", "None", "with", "try", "except", "finally", "yield", "lambda", "pass", "break", "continue",
		"in", "not", "and", "or", "is", "global", "nonlocal", "assert", "raise", "del", "async", "await",
	};
	static readonly System.Collections.Generic.HashSet<string> PhpKw = new(StringComparer.Ordinal) {
		"if", "else", "elseif", "endif", "for", "foreach", "endfor", "while", "endwhile", "do", "return",
		"function", "class", "interface", "trait", "namespace", "use", "as", "public", "private", "protected",
		"static", "final", "abstract", "const", "var", "new", "true", "false", "null", "TRUE", "FALSE", "NULL",
		"this", "self", "parent", "echo", "print", "require", "require_once", "include", "include_once",
		"try", "catch", "finally", "throw", "switch", "case", "default", "break", "continue", "goto",
		"instanceof", "insteadof", "extends", "implements", "clone", "isset", "unset", "empty", "array",
		"list", "callable", "global", "static", "yield", "match", "fn", "readonly", "enum",
	};
	static readonly System.Collections.Generic.HashSet<string> PhpTypes = new(StringComparer.Ordinal) {
		"int", "float", "string", "bool", "boolean", "array", "object", "void", "mixed", "iterable",
		"callable", "resource", "null", "true", "false", "self", "parent", "static",
	};
	static readonly System.Collections.Generic.HashSet<string> CssKw = new(StringComparer.Ordinal) {
		"important", "media", "keyframes", "from", "to", "and", "or", "not", "only", "supports", "charset",
		"import", "namespace", "font-face", "layer", "container",
	};
	static readonly System.Collections.Generic.HashSet<string> CssTypes = new(StringComparer.Ordinal) {
		"px", "em", "rem", "vh", "vw", "vmin", "vmax", "pt", "cm", "mm", "in", "deg", "rad", "s", "ms",
		"fr", "ch", "ex", "auto", "none", "inherit", "initial", "unset", "solid", "dashed", "dotted",
		"block", "inline", "flex", "grid", "absolute", "relative", "fixed", "sticky", "hidden", "visible",
		"bold", "normal", "center", "left", "right", "top", "bottom", "wrap", "nowrap", "row", "column",
	};
	static readonly System.Collections.Generic.HashSet<string> LuaKw = new(StringComparer.Ordinal) {
		"if", "then", "else", "elseif", "end", "for", "while", "do", "function", "local", "return", "true",
		"false", "nil", "and", "or", "not", "in", "repeat", "until", "break",
	};
	static readonly System.Collections.Generic.HashSet<string> JsonKw = new(StringComparer.Ordinal) {
		"true", "false", "null",
	};
	static readonly System.Collections.Generic.HashSet<string> SqlKw = new(StringComparer.OrdinalIgnoreCase) {
		"select", "from", "where", "and", "or", "not", "insert", "into", "values", "update", "set", "delete",
		"create", "table", "index", "view", "drop", "alter", "join", "left", "right", "inner", "outer", "on",
		"group", "by", "order", "having", "as", "in", "is", "null", "like", "between", "limit", "offset",
		"union", "all", "distinct", "case", "when", "then", "else", "end", "exists", "count", "sum", "avg",
		"min", "max", "primary", "key", "foreign", "references", "constraint", "default", "true", "false",
	};
	static readonly System.Collections.Generic.HashSet<string> BashKw = new(StringComparer.Ordinal) {
		"if", "then", "else", "elif", "fi", "for", "while", "do", "done", "case", "esac", "in", "function",
		"return", "exit", "export", "local", "readonly", "declare", "set", "unset", "echo", "printf", "test",
		"true", "false", "source", "shift", "break", "continue", "select", "until",
	};
	static readonly System.Collections.Generic.HashSet<string> JavaKw = new(StringComparer.Ordinal) {
		"if", "else", "for", "while", "do", "return", "class", "interface", "enum", "extends", "implements",
		"public", "private", "protected", "static", "final", "void", "new", "true", "false", "null", "this",
		"super", "import", "package", "try", "catch", "finally", "throw", "throws", "break", "continue",
		"switch", "case", "default", "synchronized", "volatile", "abstract", "native", "strictfp", "assert",
		"instanceof", "var", "record", "sealed", "permits", "yield",
	};
	static readonly System.Collections.Generic.HashSet<string> JavaTypes = new(StringComparer.Ordinal) {
		"int", "long", "short", "byte", "char", "float", "double", "boolean", "String", "Object", "List",
		"Map", "Set", "Optional", "Stream", "Exception", "Override",
	};
	static readonly System.Collections.Generic.HashSet<string> GoKw = new(StringComparer.Ordinal) {
		"if", "else", "for", "range", "return", "func", "var", "const", "type", "struct", "interface",
		"package", "import", "map", "chan", "go", "defer", "select", "case", "default", "switch", "break",
		"continue", "fallthrough", "goto", "true", "false", "nil", "make", "new", "len", "cap", "append",
	};
	static readonly System.Collections.Generic.HashSet<string> GoTypes = new(StringComparer.Ordinal) {
		"int", "int8", "int16", "int32", "int64", "uint", "uint8", "uint16", "uint32", "uint64", "float32",
		"float64", "string", "bool", "byte", "rune", "error", "any",
	};
	static readonly System.Collections.Generic.HashSet<string> RustKw = new(StringComparer.Ordinal) {
		"if", "else", "for", "while", "loop", "return", "fn", "let", "mut", "const", "struct", "enum",
		"impl", "trait", "pub", "use", "mod", "crate", "self", "super", "where", "match", "as", "in",
		"ref", "move", "async", "await", "true", "false", "break", "continue", "type", "static", "unsafe",
	};
	static readonly System.Collections.Generic.HashSet<string> RustTypes = new(StringComparer.Ordinal) {
		"i8", "i16", "i32", "i64", "i128", "u8", "u16", "u32", "u64", "u128", "f32", "f64", "bool", "char",
		"str", "String", "Vec", "Option", "Result", "Box", "Self",
	};
	static readonly System.Collections.Generic.HashSet<string> CKw = new(StringComparer.Ordinal) {
		"if", "else", "for", "while", "do", "return", "switch", "case", "default", "break", "continue",
		"goto", "struct", "union", "enum", "typedef", "sizeof", "static", "extern", "const", "volatile",
		"register", "auto", "void", "signed", "unsigned", "true", "false", "nullptr", "class", "public",
		"private", "protected", "virtual", "template", "typename", "namespace", "using", "new", "delete",
		"try", "catch", "throw", "this",
	};
	static readonly System.Collections.Generic.HashSet<string> CTypes = new(StringComparer.Ordinal) {
		"int", "char", "short", "long", "float", "double", "bool", "size_t", "uint8_t", "uint16_t",
		"uint32_t", "uint64_t", "int8_t", "int16_t", "int32_t", "int64_t", "wchar_t", "string", "vector",
	};
	static readonly System.Collections.Generic.HashSet<string> HtmlKw = new(StringComparer.OrdinalIgnoreCase) {
		"html", "head", "body", "div", "span", "script", "style", "link", "meta", "title", "p", "a", "img",
		"ul", "ol", "li", "table", "tr", "td", "th", "form", "input", "button", "class", "id", "href", "src",
	};

	static string tipfor(string href) {
		if (string.IsNullOrEmpty(href)) return "";
		if (href.StartsWith("#", StringComparison.Ordinal))
			return "章节: " + href;
		if (IsMdHref(href))
			return "打开 Markdown: " + href;
		return href;
	}

	/// <summary>是否 Markdown 文件链接（可带 #锚点）。</summary>
	public static bool IsMdHref(string href) {
		if (string.IsNullOrWhiteSpace(href)) return false;
		var path = href.Trim();
		var hash = path.IndexOf('#');
		if (hash == 0) return false; // 纯锚点
		if (hash > 0) path = path.Substring(0, hash);
		if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
			return false;
		var ext = System.IO.Path.GetExtension(path);
		return string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(ext, ".markdown", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(ext, ".mdown", StringComparison.OrdinalIgnoreCase);
	}

	static Uri tryuri(string href) {
		if (string.IsNullOrWhiteSpace(href)) return null;
		// 相对路径 / 锚点：用伪 scheme，保证 Hyperlink 可点并发 RequestNavigate
		if (href.StartsWith("#", StringComparison.Ordinal)) {
			try { return new Uri("mdlink://anchor/" + Uri.EscapeDataString(href.Substring(1))); }
			catch { return null; }
		}
		if (Uri.TryCreate(href, UriKind.Absolute, out var abs)
			&& (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps
				|| abs.Scheme == Uri.UriSchemeFile || abs.Scheme == Uri.UriSchemeMailto))
			return abs;
		// 相对 md/路径
		try { return new Uri("mdlink://path/" + Uri.EscapeDataString(href)); }
		catch { return null; }
	}

	/// <summary>按源行查找 FlowDocument 中第一个 Tag 匹配的 Block。</summary>
	public static Block FindBlockBySourceLine(FlowDocument fd, int sourceLine0) {
		if (fd == null) return null;
		Block best = null;
		var bestLine = int.MinValue;
		foreach (var b in fd.Blocks) {
			walk(b, sourceLine0, ref best, ref bestLine);
		}
		return best;
	}

	static void walk(Block b, int target, ref Block best, ref int bestLine) {
		if (b == null) return;
		if (b.Tag is int ln && ln <= target && ln >= bestLine) {
			best = b;
			bestLine = ln;
		}
		if (b is List list) {
			foreach (var li in list.ListItems)
				foreach (var c in li.Blocks)
					walk(c, target, ref best, ref bestLine);
		} else if (b is Section sec) {
			foreach (var c in sec.Blocks)
				walk(c, target, ref best, ref bestLine);
		}
	}
}
