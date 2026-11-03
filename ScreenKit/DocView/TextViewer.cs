using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenKit.DocView;

/// <summary>
/// 纯文本 / 代码文件：默认只读预览（语法着色），工具栏进入编辑；支持缩放/查找/保存。
/// </summary>
sealed class TextViewer : IDocViewer {
	const double MIN_ZOOM = 0.6;
	const double MAX_ZOOM = 2.5;
	const double BASE_FONT = 14;

	readonly Grid root;
	readonly RichTextBox previewBox;
	readonly TextBox editBox;
	readonly Border host;

	double zoom = 1.0;
	bool editMode;
	bool dirty;
	bool suppressDirty;
	Encoding fileEnc = new UTF8Encoding(false);
	string rawText = "";
	/// <summary>由扩展名推断的语言（cs/python/php…）；text 不着色。</summary>
	string codeLang = "text";

	/// <summary>当前文件编码（状态栏显示 / 切换）。</summary>
	public Encoding FileEncoding => fileEnc;
	public string EncodingName => TextFileIo.DisplayName(fileEnc);

	// find
	string findQuery;
	bool findIgnoreCase = true;
	int findIndex = -1;
	readonly System.Collections.Generic.List<int> findHits = new();

	public FrameworkElement View => root;
	public string FilePath { get; private set; }
	public string Title { get; private set; }
	public DocKind Kind => DocKind.Txt;
	public double Zoom => zoom;
	public string StatusText {
		get {
			var mode = editMode ? "编辑" : "预览";
			var d = dirty ? " *" : "";
			var lines = countlines(rawText);
			var tag = "TXT";
			try {
				var ext = System.IO.Path.GetExtension(FilePath ?? "");
				if (!string.IsNullOrEmpty(ext))
					tag = ext.TrimStart('.').ToUpperInvariant();
			} catch { /* keep TXT */ }
			var hl = !editMode && usehighlight() ? "  ·  高亮" : "";
			return $"{tag}  {mode}{d}  ·  {lines} 行{hl}  ·  {EncodingName}  ·  {(int)(zoom * 100)}%";
		}
	}
	public int PageCount => 1;
	public int CurrentPage => 1;
	public bool HasOutline => false;
	public bool SidePanelVisible => false;

	public bool EditMode {
		get => editMode;
		set => seteditmode(value);
	}
	public bool IsDirty => dirty;

	public event Action StatusChanged;
	public event Action EditModeChanged;
	public event Action DirtyChanged;

	public TextViewer() {
		// 标准 RTB：只读预览 + 语法着色（不用 Emoji.Wpf，避免大文件拖慢）
		previewBox = new RichTextBox {
			FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI Emoji, 微软雅黑, monospace"),
			FontSize = BASE_FONT,
			IsReadOnly = true,
			IsDocumentEnabled = true,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(0),
			Background = Brushes.White,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			AcceptsTab = false,
		};
		previewBox.Document = new FlowDocument {
			PagePadding = new Thickness(16, 12, 16, 20),
			Background = Brushes.White,
		};
		editBox = new TextBox {
			FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI Emoji, 微软雅黑, monospace"),
			FontSize = BASE_FONT,
			TextWrapping = TextWrapping.NoWrap,
			AcceptsReturn = true,
			AcceptsTab = true,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(16),
			Background = Brushes.White,
			Visibility = Visibility.Collapsed,
		};
		editBox.TextChanged += (_, _) => {
			if (suppressDirty) return;
			rawText = editBox.Text ?? "";
			setdirty(true);
			StatusChanged?.Invoke();
		};
		editBox.PreviewMouseWheel += onwheel;
		previewBox.PreviewMouseWheel += onwheel;

		host = new Border {
			Background = Brushes.White,
			Child = previewBox,
		};
		root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)) };
		root.Children.Add(host);
		applyzoom();
	}

	public void Load(string path) {
		var r = TextFileIo.Load(path);
		FilePath = System.IO.Path.GetFullPath(path);
		Title = System.IO.Path.GetFileName(path);
		codeLang = MdFlowBuilder.LangFromPath(FilePath);
		fileEnc = r.Encoding ?? new UTF8Encoding(false);
		rawText = r.Text ?? "";
		suppressDirty = true;
		try {
			editBox.Text = rawText;
			applypreviewhl();
		} finally { suppressDirty = false; }
		setdirty(false);
		seteditmode(false);
		DocViewLog.Info($"Text Load lang={codeLang} lines={countlines(rawText)} enc={fileEnc.WebName} path={FilePath}");
		StatusChanged?.Invoke();
	}

	public void Save() {
		if (editMode)
			rawText = editBox.Text ?? "";
		TextFileIo.Save(FilePath, rawText, fileEnc);
		// 同步预览高亮
		suppressDirty = true;
		try { applypreviewhl(); }
		finally { suppressDirty = false; }
		setdirty(false);
		DocViewLog.Info($"Text Save path={FilePath}");
		StatusChanged?.Invoke();
	}

	void seteditmode(bool on) {
		if (editMode == on) return;
		if (on) {
			suppressDirty = true;
			try { editBox.Text = rawText; }
			finally { suppressDirty = false; }
			host.Child = editBox;
			editBox.Visibility = Visibility.Visible;
			editMode = true;
			try { editBox.Focus(); } catch { /* ignore */ }
		} else {
			if (editMode)
				rawText = editBox.Text ?? "";
			applypreviewhl();
			host.Child = previewBox;
			editBox.Visibility = Visibility.Collapsed;
			editMode = false;
		}
		try { EditModeChanged?.Invoke(); } catch { /* ignore */ }
		StatusChanged?.Invoke();
	}

	/// <summary>是否对当前文件做语法着色（扩展名有规则且体积未超限）。</summary>
	bool usehighlight() {
		if (string.IsNullOrEmpty(codeLang) || codeLang == "text" || codeLang == "txt" || codeLang == "log")
			return false;
		var n = rawText?.Length ?? 0;
		if (n > MdFlowBuilder.CODE_HL_MAX_CHARS) return false;
		if (countlines(rawText) > MdFlowBuilder.CODE_HL_MAX_LINES) return false;
		return true;
	}

	void applypreviewhl() {
		try {
			var fs = BASE_FONT * zoom;
			var fd = MdFlowBuilder.BuildCodeDocument(rawText ?? "", codeLang, fs, lineNumbers: true);
			previewBox.Document = fd;
		} catch (Exception ex) {
			DocViewLog.Warn($"Text preview HL: {ex.Message}");
			try {
				previewBox.Document = new FlowDocument(new Paragraph(new Run(rawText ?? ""))) {
					FontFamily = previewBox.FontFamily,
					FontSize = BASE_FONT * zoom,
					PagePadding = new Thickness(12, 10, 16, 20),
					Background = Brushes.White,
				};
			} catch { /* ignore */ }
		}
	}

	/// <summary>按指定编码从磁盘重载（丢弃未保存修改）。</summary>
	public void ReloadWithEncoding(Encoding enc) {
		if (enc == null || string.IsNullOrEmpty(FilePath)) return;
		var r = TextFileIo.LoadWithEncoding(FilePath, enc);
		fileEnc = r.Encoding ?? enc;
		rawText = r.Text ?? "";
		suppressDirty = true;
		try {
			editBox.Text = rawText;
			if (!editMode) applypreviewhl();
		} finally { suppressDirty = false; }
		setdirty(false);
		DocViewLog.Info($"Text reload enc={TextFileIo.DisplayName(fileEnc)} path={FilePath}");
		StatusChanged?.Invoke();
	}

	void setdirty(bool d) {
		if (dirty == d) return;
		dirty = d;
		try { DirtyChanged?.Invoke(); } catch { /* ignore */ }
	}

	public void SetZoom(double z) {
		zoom = clamp(z, MIN_ZOOM, MAX_ZOOM);
		applyzoom();
		// 字号变了需重建 Run（FontSize 在 Document 构建时写入）
		if (!editMode) applypreviewhl();
		StatusChanged?.Invoke();
	}
	public void ZoomBy(double factor) => SetZoom(zoom * factor);
	public void ZoomIn() => SetZoom(zoom * 1.15);
	public void ZoomOut() => SetZoom(zoom / 1.15);
	public void ZoomFitWidth() => SetZoom(1.0);
	public void ZoomFitPage() => SetZoom(1.0);
	public void RotateBy(int deltaQuarterTurns) { /* txt 不旋转 */ }
	public void GoPrevPage() { }
	public void GoNextPage() { }
	public void GoToPage(int page1Based) { }
	public void SetSidePanelVisible(bool show) { }

	public void CaptureViewState(out double h, out double v, out double z, out int sheetOrPage) {
		z = zoom;
		sheetOrPage = 1;
		if (editMode) {
			h = 0;
			v = editBox.GetFirstVisibleLineIndex();
		} else {
			h = previewBox.HorizontalOffset;
			v = previewBox.VerticalOffset;
		}
	}

	public void RestoreViewState(double h, double v, double z, int sheetOrPage) {
		if (z > 0.05) SetZoom(z);
		try {
			root.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
				try {
					if (editMode) {
						var line = (int)Math.Max(0, v);
						if (line < editBox.LineCount)
							editBox.ScrollToLine(line);
					} else {
						previewBox.ScrollToHorizontalOffset(h);
						previewBox.ScrollToVerticalOffset(v);
					}
				} catch { /* ignore */ }
			}));
		} catch { /* ignore */ }
	}

	public bool TryCopySelection() {
		try {
			if (editMode) {
				if (string.IsNullOrEmpty(editBox.SelectedText)) return false;
				Clipboard.SetText(editBox.SelectedText);
				return true;
			}
			// 预览 RTB：选区复制
			var sel = previewBox.Selection;
			if (sel == null || sel.IsEmpty) return false;
			var t = sel.Text;
			if (string.IsNullOrEmpty(t)) return false;
			Clipboard.SetText(t);
			return true;
		} catch { return false; }
	}

	public FindResult Find(string text, bool forward, bool ignoreCase, bool restart = false, bool fromView = false) {
		if (string.IsNullOrEmpty(text)) {
			ClearFind();
			return FindResult.Miss();
		}
		try {
			if (restart || findQuery != text || findIgnoreCase != ignoreCase || findHits.Count == 0)
				rebuildfind(text, ignoreCase);
			if (findHits.Count == 0) return FindResult.Miss();
			if (findIndex < 0)
				findIndex = forward ? 0 : findHits.Count - 1;
			else
				findIndex = forward
					? (findIndex + 1) % findHits.Count
					: (findIndex - 1 + findHits.Count) % findHits.Count;
			jumptofind(findIndex);
			return FindResult.Hit(findIndex + 1, findHits.Count);
		} catch {
			return FindResult.Miss(findHits.Count);
		}
	}

	public void ClearFind() {
		findQuery = null;
		findHits.Clear();
		findIndex = -1;
	}

	void rebuildfind(string text, bool ignoreCase) {
		findQuery = text;
		findIgnoreCase = ignoreCase;
		findHits.Clear();
		findIndex = -1;
		var src = editMode ? (editBox.Text ?? "") : rawText;
		if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(text)) return;
		var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var i = 0;
		while (i < src.Length) {
			var j = src.IndexOf(text, i, cmp);
			if (j < 0) break;
			findHits.Add(j);
			i = j + Math.Max(1, text.Length);
		}
	}

	void jumptofind(int idx) {
		if (idx < 0 || idx >= findHits.Count || string.IsNullOrEmpty(findQuery)) return;
		var start = findHits[idx];
		var len = findQuery.Length;
		if (!editMode) {
			// 进入编辑以便选中高亮
			seteditmode(true);
			suppressDirty = true;
			try { editBox.Text = rawText; }
			finally { suppressDirty = false; }
		}
		try {
			editBox.Focus();
			editBox.Select(start, len);
			var line = editBox.GetLineIndexFromCharacterIndex(start);
			if (line >= 0) editBox.ScrollToLine(line);
		} catch { /* ignore */ }
	}

	void applyzoom() {
		var fs = BASE_FONT * zoom;
		previewBox.FontSize = fs;
		editBox.FontSize = fs;
	}

	void onwheel(object sender, MouseWheelEventArgs e) {
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) {
			if (e.Delta > 0) ZoomIn();
			else ZoomOut();
			e.Handled = true;
		}
	}

	public void Dispose() {
		ClearFind();
		rawText = null;
	}

	static int countlines(string s) {
		if (string.IsNullOrEmpty(s)) return 0;
		var n = 1;
		foreach (var c in s)
			if (c == '\n') n++;
		return n;
	}

	static double clamp(double v, double a, double b) {
		if (v < a) return a;
		if (v > b) return b;
		return v;
	}
}
