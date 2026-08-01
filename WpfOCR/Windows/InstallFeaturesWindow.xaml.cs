using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfOCR;

/// <summary>
/// 安装功能：功能组件 + 发音人（TTS）双 Tab。
/// 发音人支持语言筛选，列表来自 GitHub tts-models 全量包。
/// </summary>
partial class InstallFeaturesWindow : Window {
	readonly List<FeatureItem> featItems = new();
	readonly Dictionary<FeatureItem, CheckBox> featChecks = new();
	readonly Dictionary<FeatureItem, TextBlock> featStates = new();
	readonly Dictionary<FeatureItem, TextBlock> featSizes = new();

	readonly ObservableCollection<TtsRow> ttsRows = new();
	List<TtsInstallItem> ttsAll = new();
	readonly bool firstRun;
	readonly FeatureKind[] preferSelect;
	readonly bool openTtsTab;
	bool ttsLoaded;
	bool ttsUiLoading;
	CancellationTokenSource cts;
	bool busy;

	public bool NeedRefresh { get; private set; }
	public bool NeedRestart { get; private set; }

	/// <param name="firstRun">首次启动：默认勾选推荐组件。</param>
	/// <param name="preferSelect">使用前提示时预勾选的组件。</param>
	/// <param name="openTtsTab">打开时切到发音人 Tab。</param>
	public InstallFeaturesWindow(bool firstRun = false, FeatureKind[] preferSelect = null, bool openTtsTab = false) {
		this.firstRun = firstRun;
		this.preferSelect = preferSelect;
		this.openTtsTab = openTtsTab;
		InitializeComponent();
		if (firstRun) {
			Title = "欢迎使用 — 安装推荐组件";
			try { tabmain.SelectedItem = tabfeat; } catch { }
		}
		else if (openTtsTab) {
			try { tabmain.SelectedItem = tabtts; } catch { }
		}
		else if (preferSelect != null && preferSelect.Length > 0) {
			Title = "安装所需组件";
			try { tabmain.SelectedItem = tabfeat; } catch { }
		}
		WindowEsc.Attach(this, () => {
			if (busy) return;
			Close();
		});
		binstall.Click += async (_, _) => await runinstall();
		bdelete.Click += async (_, _) => await rundelete();
		bcancel.Click += (_, _) => {
			try { cts?.Cancel(); } catch { }
		};
		bclose.Click += (_, _) => {
			if (busy) {
				MessageBox.Show(this, "正在安装，请先取消或等待完成。", Title,
					MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			Close();
		};
		bmissing.Click += (_, _) => selectmissing();
		bnone.Click += (_, _) => selectall(false);
		ball.Click += (_, _) => selectall(true);
		bttsrefresh.Click += async (_, _) => await loadtts(force: true);
		ettslang.SelectionChanged += (_, _) => {
			if (!ttsUiLoading) applyttsfilter();
		};
		cttsmissing.Checked += (_, _) => applyttsfilter();
		cttsmissing.Unchecked += (_, _) => applyttsfilter();
		cttssupported.Checked += (_, _) => applyttsfilter();
		cttssupported.Unchecked += (_, _) => applyttsfilter();
		cttsheader.Checked += (_, _) => setttscheckall(true);
		cttsheader.Unchecked += (_, _) => {
			// 仅在用户点表头时清空；避免筛选重建误触
			if (!ttsUiLoading) setttscheckall(false);
		};
		tabmain.SelectionChanged += async (_, _) => {
			if (tabmain.SelectedItem == tabtts && !ttsLoaded && !busy)
				await loadtts(force: false);
		};
		lvtss.ItemsSource = ttsRows;
		Loaded += async (_, _) => {
			rebuildfeat();
			// 预加载发音人列表（后台）
			_ = loadtts(force: false);
		};
		Closing += (_, e) => {
			if (!busy) return;
			e.Cancel = true;
			MessageBox.Show(this, "正在安装，请先取消或等待完成。", Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
		};
	}

	// ───────── 功能组件 Tab ─────────

	void rebuildfeat() {
		featItems.Clear();
		featChecks.Clear();
		featStates.Clear();
		featSizes.Clear();
		eitems.Children.Clear();
		featItems.AddRange(FeatureInstaller.BuildCatalog(
			firstRunDefaults: firstRun,
			preferSelect: firstRun ? null : preferSelect));
		if (firstRun)
			lbmirror.Text = "首次启动：已默认勾选 OpenCV · Sherpa · rapid-ch · SenseVoice · Zipformer · FFmpeg（推理加速不勾）\n"
				+ FeatureInstaller.MirrorHint();
		else if (preferSelect != null && preferSelect.Length > 0)
			lbmirror.Text = "已按当前功能预勾选所需组件，请点「安装选中」。\n" + FeatureInstaller.MirrorHint();
		else
			lbmirror.Text = "默认勾选：OpenCV · OCR rapid-ch · ASR 前 2 项 · FFmpeg；推理加速不勾。\n"
				+ FeatureInstaller.MirrorHint();

		string lastCat = null;
		foreach (var it in featItems) {
			if (lastCat == null || !string.Equals(lastCat, it.Category, StringComparison.Ordinal)) {
				lastCat = it.Category;
				var catTitle = it.Category switch {
					"native" => "运行库（按需）",
					"ocr" => "OCR 模型",
					"asr" => "语音识别模型",
					"accel" => "推理加速",
					"media" => "媒体组件",
					_ => it.Category,
				};
				eitems.Children.Add(new TextBlock {
					Text = catTitle,
					FontWeight = FontWeights.SemiBold,
					FontSize = 13,
					Margin = new Thickness(0, eitems.Children.Count == 0 ? 0 : 12, 0, 6),
					Foreground = (Brush)FindResource("TextPrimary"),
				});
			}

			var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
			var st = new TextBlock {
				Text = it.StateText,
				Width = 52,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Right,
				FontSize = 12,
				Foreground = statebrush(it.State),
			};
			DockPanel.SetDock(st, Dock.Right);
			featStates[it] = st;
			row.Children.Add(st);

			var sz = new TextBlock {
				Text = it.SizeText ?? "",
				Width = 88,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Right,
				FontSize = 11,
				Margin = new Thickness(0, 0, 8, 0),
				Foreground = (Brush)FindResource("TextMuted"),
			};
			DockPanel.SetDock(sz, Dock.Right);
			featSizes[it] = sz;
			row.Children.Add(sz);

			var cb = new CheckBox {
				IsChecked = it.Selected,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0, 2, 0, 0),
			};
			featChecks[it] = cb;
			cb.Checked += (_, _) => it.Selected = true;
			cb.Unchecked += (_, _) => it.Selected = false;

			var textCol = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
			textCol.Children.Add(new TextBlock {
				Text = it.Title,
				FontSize = 13,
				Foreground = (Brush)FindResource("TextPrimary"),
			});
			textCol.Children.Add(new TextBlock {
				Text = it.Detail + (it.NeedsRestart ? " · 安装后需重启" : ""),
				FontSize = 11,
				TextWrapping = TextWrapping.Wrap,
				Foreground = (Brush)FindResource("TextMuted"),
				Margin = new Thickness(0, 2, 0, 0),
			});
			cb.Content = textCol;
			row.Children.Add(cb);
			eitems.Children.Add(row);
		}
	}

	// ───────── 发音人 Tab ─────────

	async Task loadtts(bool force) {
		if (busy && force) return;
		setstatus("正在加载发音人列表…");
		var log = new Progress<string>(appendlog);
		try {
			bttsrefresh.IsEnabled = false;
			var list = await Task.Run(async () =>
				await TtsInstallCatalog.LoadAllAsync(log, CancellationToken.None, force)
					.ConfigureAwait(false)).ConfigureAwait(true);
			ttsAll = list ?? new List<TtsInstallItem>();
			ttsLoaded = true;
			lbttssource.Text = TtsInstallCatalog.LastSource + " · " + ttsAll.Count + " 个";
			filllangcombo();
			applyttsfilter();
			setstatus($"发音人列表就绪 · {ttsAll.Count} 个 · {TtsInstallCatalog.LastSource}");
		}
		catch (Exception ex) {
			appendlog("加载发音人失败: " + ex.Message);
			setstatus("发音人列表加载失败");
			CaptureLog.Ex("loadtts", ex);
		}
		finally {
			bttsrefresh.IsEnabled = !busy;
		}
	}

	void filllangcombo() {
		ttsUiLoading = true;
		var prev = (ettslang.SelectedItem as ComboBoxItem)?.Tag as string;
		ettslang.Items.Clear();
		foreach (var (code, label) in TtsInstallCatalog.LanguageOptions(ttsAll)) {
			ettslang.Items.Add(new ComboBoxItem {
				Content = label,
				Tag = code ?? "",
			});
		}
		// 恢复选择
		var pick = 0;
		for (var i = 0; i < ettslang.Items.Count; i++) {
			if (ettslang.Items[i] is ComboBoxItem ci
				&& string.Equals(ci.Tag as string, prev, StringComparison.OrdinalIgnoreCase)) {
				pick = i;
				break;
			}
		}
		// 默认中文（若有）
		if (string.IsNullOrEmpty(prev)) {
			for (var i = 0; i < ettslang.Items.Count; i++) {
				if (ettslang.Items[i] is ComboBoxItem ci && (ci.Tag as string) == "zh") {
					pick = i;
					break;
				}
			}
		}
		if (ettslang.Items.Count > 0)
			ettslang.SelectedIndex = pick;
		ttsUiLoading = false;
	}

	void applyttsfilter() {
		ttsUiLoading = true;
		var lang = (ettslang.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
		var onlyMissing = cttsmissing.IsChecked == true;
		var onlySupported = cttssupported.IsChecked == true;

		// 同步 Selected 从现有 rows
		var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var r in ttsRows) {
			if (r.Selected) selectedIds.Add(r.Item.Id);
			// 写回 item
			r.Item.Selected = r.Selected;
		}

		ttsRows.Clear();
		IEnumerable<TtsInstallItem> q = ttsAll;
		if (onlySupported)
			q = q.Where(x => x.AppSupported);
		q = TtsInstallCatalog.Filter(q, lang, onlyMissing);

		foreach (var it in q) {
			// 保持用户已勾选；否则不默认勾选
			it.Selected = selectedIds.Contains(it.Id);
			ttsRows.Add(new TtsRow(it));
		}
		cttsheader.IsChecked = false;
		ttsUiLoading = false;
		lbttssource.Text = $"{TtsInstallCatalog.LastSource} · 显示 {ttsRows.Count}/{ttsAll.Count}";
	}

	void setttscheckall(bool on) {
		foreach (var r in ttsRows) {
			r.Selected = on;
			r.Item.Selected = on;
			r.Notify();
		}
	}

	// ───────── 选择 / 安装 ─────────

	void selectmissing() {
		if (busy) return;
		if (tabmain.SelectedItem == tabtts) {
			foreach (var r in ttsRows) {
				r.Selected = r.Item.State != FeatureInstallState.Installed;
				r.Item.Selected = r.Selected;
				r.Notify();
			}
		}
		else {
			foreach (var it in featItems) {
				it.Selected = it.State != FeatureInstallState.Installed;
				if (featChecks.TryGetValue(it, out var cb))
					cb.IsChecked = it.Selected;
			}
		}
	}

	void selectall(bool on) {
		if (busy) return;
		if (tabmain.SelectedItem == tabtts) {
			setttscheckall(on);
			cttsheader.IsChecked = on;
		}
		else {
			foreach (var it in featItems) {
				it.Selected = on;
				if (featChecks.TryGetValue(it, out var cb))
					cb.IsChecked = on;
			}
		}
	}

	static Brush statebrush(FeatureInstallState st) => st switch {
		FeatureInstallState.Installed => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
		FeatureInstallState.Partial => new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)),
		_ => new SolidColorBrush(Color.FromRgb(0x71, 0x80, 0x96)),
	};

	void setbusy(bool on) {
		busy = on;
		binstall.IsEnabled = !on;
		bdelete.IsEnabled = !on;
		bcancel.IsEnabled = on;
		bmissing.IsEnabled = !on;
		bnone.IsEnabled = !on;
		ball.IsEnabled = !on;
		bttsrefresh.IsEnabled = !on;
		ettslang.IsEnabled = !on;
		cttsmissing.IsEnabled = !on;
		cttssupported.IsEnabled = !on;
		foreach (var cb in featChecks.Values)
			cb.IsEnabled = !on;
		lvtss.IsEnabled = !on;
	}

	void appendlog(string line) {
		if (string.IsNullOrEmpty(line)) return;
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.Invoke(() => appendlog(line));
			return;
		}
		var ts = DateTime.Now.ToString("HH:mm:ss");
		elog.AppendText($"[{ts}] {line}\n");
		elog.ScrollToEnd();
	}

	void setprogress(double v) {
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.Invoke(() => setprogress(v));
			return;
		}
		if (v < 0) v = 0;
		if (v > 1) v = 1;
		pbar.Value = v;
		lbprog.Text = $"{(int)(v * 100)}%";
	}

	void setstatus(string s) {
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.Invoke(() => setstatus(s));
			return;
		}
		lbstatus.Text = s ?? "";
	}

	void setbytes(string s) {
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.Invoke(() => setbytes(s));
			return;
		}
		lbbytes.Text = s ?? "";
	}

	void applyitemprogress(int itemIndex, int itemCount, long batchDone, long batchTotal, InstallProgress p) {
		if (p == null) return;
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.Invoke(() => applyitemprogress(itemIndex, itemCount, batchDone, batchTotal, p));
			return;
		}
		var baseP = itemIndex / (double)itemCount;
		var span = 1.0 / itemCount;
		var overall = baseP + Math.Max(0, Math.Min(1, p.Overall)) * span;
		setprogress(overall);
		// 合计 = 已完成项 + 当前项已下；总大小 = 全部勾选预估
		var done = batchDone + Math.Max(0L, p.BytesDone);
		if (batchTotal > 0 || done > 0 || p.BytesTotal > 0) {
			var total = batchTotal > 0 ? batchTotal : Math.Max(done, batchDone + Math.Max(0L, p.BytesTotal));
			var bytes = FeatureInstaller.FormatBytes(done)
				+ " / "
				+ (total > 0 ? FeatureInstaller.FormatBytes(total) : "?");
			if (!string.IsNullOrEmpty(p.FileName))
				bytes += " · " + p.FileName;
			if (!string.IsNullOrEmpty(p.Note))
				bytes += " · " + p.Note;
			setbytes(bytes);
		}
		else if (!string.IsNullOrEmpty(p.Note)) {
			setbytes(p.Note + (string.IsNullOrEmpty(p.FileName) ? "" : " · " + p.FileName));
		}
	}

	void refreshfeatui(FeatureItem it) {
		FeatureInstaller.RefreshState(it);
		if (featStates.TryGetValue(it, out var st)) {
			st.Text = it.StateText;
			st.Foreground = statebrush(it.State);
		}
		if (featSizes.TryGetValue(it, out var sz))
			sz.Text = it.SizeText ?? "";
		if (featChecks.TryGetValue(it, out var cb)) {
			if (it.State == FeatureInstallState.Installed) {
				cb.IsChecked = false;
				it.Selected = false;
			}
			else
				it.Selected = cb.IsChecked == true;
		}
	}

	async Task rundelete() {
		foreach (var r in ttsRows)
			r.Item.Selected = r.Selected;
		// 功能：勾选且已安装/部分
		var feats = featItems.Where(x => x.Selected
			&& x.State is FeatureInstallState.Installed or FeatureInstallState.Partial).ToList();
		var tts = ttsAll.Where(x => x.Selected && x.State == FeatureInstallState.Installed).ToList();
		if (feats.Count == 0 && tts.Count == 0) {
			MessageBox.Show(this, "请勾选要删除的「已安装」组件或发音人。", Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var names = feats.Select(f => f.Title).Concat(tts.Select(t => t.Title)).Take(12).ToList();
		var more = feats.Count + tts.Count - names.Count;
		var msg = "将永久删除以下已安装项（不可恢复）：\n\n· "
			+ string.Join("\n· ", names)
			+ (more > 0 ? $"\n· … 另 {more} 项" : "")
			+ "\n\n确定删除？";
		if (MessageBox.Show(this, msg, "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning)
			!= MessageBoxResult.Yes)
			return;

		var log = new Progress<string>(appendlog);
		setbusy(true);
		setprogress(0);
		setbytes("");
		var ok = 0;
		var fail = 0;
		var total = feats.Count + tts.Count;
		var step = 0;
		try {
			appendlog($"开始删除 {total} 项…");
			foreach (var it in feats) {
				setstatus($"删除 ({step + 1}/{total}) {it.Title}");
				appendlog("── 删除 " + it.Title);
				try {
					await Task.Run(() => FeatureInstaller.Uninstall(it.Kind, log)).ConfigureAwait(true);
					ok++;
					NeedRefresh = true;
					if (it.NeedsRestart) NeedRestart = true;
					refreshfeatui(it);
					appendlog("已删除: " + it.Title);
				}
				catch (Exception ex) {
					fail++;
					appendlog("删除失败: " + ex.Message);
					CaptureLog.Ex("Uninstall " + it.Id, ex);
					refreshfeatui(it);
				}
				step++;
				setprogress(step / (double)total);
			}
			foreach (var it in tts) {
				setstatus($"删除 ({step + 1}/{total}) {it.Title}");
				appendlog("── 删除 TTS " + it.Title);
				try {
					await Task.Run(() => TtsInstallCatalog.Uninstall(it, log)).ConfigureAwait(true);
					ok++;
					NeedRefresh = true;
					TtsInstallCatalog.RefreshState(it);
					it.Selected = false;
					foreach (var r in ttsRows) {
						if (r.Item.Id == it.Id) {
							r.SyncFromItem();
							r.Notify();
						}
					}
					appendlog("已删除: " + it.Title);
				}
				catch (Exception ex) {
					fail++;
					appendlog("删除失败: " + ex.Message);
					CaptureLog.Ex("UninstallTts " + it.Id, ex);
				}
				step++;
				setprogress(step / (double)total);
			}
			if (tts.Count > 0) applyttsfilter();
			var summary = $"删除完成：成功 {ok} · 失败 {fail}";
			setstatus(summary);
			appendlog(summary);
			MessageBox.Show(this, summary, Title, MessageBoxButton.OK,
				fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
		}
		finally {
			setbusy(false);
		}
	}

	async Task runinstall() {
		// 合并两页勾选项
		var feats = featItems.Where(x => x.Selected && x.State != FeatureInstallState.Installed).ToList();
		// TTS：从 ttsAll 取 Selected（筛选外勾选也保留）
		foreach (var r in ttsRows)
			r.Item.Selected = r.Selected;
		var tts = ttsAll.Where(x => x.Selected && x.State != FeatureInstallState.Installed).ToList();

		if (feats.Count == 0 && tts.Count == 0) {
			MessageBox.Show(this, "请先勾选要安装的项目（功能组件或发音人）。", Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		cts = new CancellationTokenSource();
		setbusy(true);
		setprogress(0);
		setbytes("");
		var ok = 0;
		var fail = 0;
		var needRestart = false;
		var anyRefresh = false;
		var log = new Progress<string>(appendlog);

		long batchTotal = 0;
		foreach (var it in feats)
			batchTotal += it.SizeBytes > 0 ? it.SizeBytes : FeatureInstaller.ExpectedSize(it.Kind);
		foreach (var it in tts)
			batchTotal += it.SizeBytes;

		appendlog(FeatureInstaller.MirrorHint());
		appendlog($"开始安装 功能 {feats.Count} + 发音人 {tts.Count} · 合计约 {FeatureInstaller.FormatBytes(batchTotal)}");

		var totalSteps = feats.Count + tts.Count;
		var step = 0;
		// 已完成项的预估字节；当前项下载量叠加上去后显示「已下 / 合计」
		long batchDone = 0;
		setbytes(batchTotal > 0
			? FeatureInstaller.FormatBytes(0) + " / " + FeatureInstaller.FormatBytes(batchTotal)
			: "");

		try {
			// 功能组件
			for (var i = 0; i < feats.Count; i++) {
				cts.Token.ThrowIfCancellationRequested();
				var it = feats[i];
				var expect = it.SizeBytes > 0 ? it.SizeBytes : FeatureInstaller.ExpectedSize(it.Kind);
				setstatus($"({step + 1}/{totalSteps}) {it.Title} · 约 {FeatureInstaller.FormatBytes(expect)}");
				if (batchTotal > 0)
					setbytes(FeatureInstaller.FormatBytes(batchDone) + " / " + FeatureInstaller.FormatBytes(batchTotal));
				appendlog("── " + it.Title + " · " + (it.SizeText ?? ""));
				var idx = step;
				var doneBase = batchDone;
				var itemProg = new Progress<InstallProgress>(p =>
					applyitemprogress(idx, totalSteps, doneBase, batchTotal, p));
				try {
					await Task.Run(async () => {
						await FeatureInstaller.InstallAsync(it.Kind, log, itemProg, cts.Token)
							.ConfigureAwait(false);
					}, cts.Token).ConfigureAwait(true);
					ok++;
					anyRefresh = true;
					if (it.NeedsRestart) needRestart = true;
					refreshfeatui(it);
					appendlog("成功: " + it.Title);
				}
				catch (OperationCanceledException) {
					appendlog("已取消");
					setstatus("已取消");
					goto done;
				}
				catch (Exception ex) {
					fail++;
					appendlog("错误: " + ex.Message);
					CaptureLog.Ex("InstallFeatures " + it.Id, ex);
					refreshfeatui(it);
				}
				batchDone += expect;
				if (batchTotal > 0 && batchDone > batchTotal) batchDone = batchTotal;
				step++;
			}

			// 发音人
			for (var i = 0; i < tts.Count; i++) {
				cts.Token.ThrowIfCancellationRequested();
				var it = tts[i];
				var expect = it.SizeBytes;
				setstatus($"({step + 1}/{totalSteps}) {it.Title} · {it.SizeText}");
				if (batchTotal > 0)
					setbytes(FeatureInstaller.FormatBytes(batchDone) + " / " + FeatureInstaller.FormatBytes(batchTotal));
				appendlog("── TTS " + it.Title + " · " + it.SizeText + " · " + it.LangLabel);
				var idx = step;
				var doneBase = batchDone;
				var itemProg = new Progress<InstallProgress>(p =>
					applyitemprogress(idx, totalSteps, doneBase, batchTotal, p));
				try {
					await Task.Run(async () => {
						await TtsInstallCatalog.InstallAsync(it, log, itemProg, cts.Token)
							.ConfigureAwait(false);
					}, cts.Token).ConfigureAwait(true);
					ok++;
					anyRefresh = true;
					TtsInstallCatalog.RefreshState(it);
					it.Selected = false;
					// 更新 UI 行
					foreach (var r in ttsRows) {
						if (r.Item.Id == it.Id) {
							r.SyncFromItem();
							r.Notify();
						}
					}
					appendlog("成功: " + it.Title);
				}
				catch (OperationCanceledException) {
					appendlog("已取消");
					setstatus("已取消");
					goto done;
				}
				catch (Exception ex) {
					fail++;
					appendlog("错误: " + ex.Message);
					CaptureLog.Ex("InstallTts " + it.Id, ex);
					TtsInstallCatalog.RefreshState(it);
				}
				batchDone += expect;
				if (batchTotal > 0 && batchDone > batchTotal) batchDone = batchTotal;
				step++;
			}

		done:
			setprogress(1);
			NeedRefresh = anyRefresh && ok > 0;
			NeedRestart = needRestart;
			var summary = $"完成：成功 {ok} · 失败 {fail}";
			setstatus(summary);
			setbytes("");
			appendlog(summary);
			if (needRestart && ok > 0)
				appendlog("提示：GPU / 核显 运行库已更新，请重启程序后生效。");
			// 刷新 TTS 筛选显示
			if (tts.Count > 0)
				applyttsfilter();

			if (ok > 0) {
				var msg = summary;
				if (needRestart)
					msg += "\n\nGPU / 核显组件需重启程序后才能使用。";
				MessageBox.Show(this, msg, Title, MessageBoxButton.OK,
					fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
			}
			else if (fail > 0) {
				MessageBox.Show(this, "所选项目均未安装成功，请查看日志。", Title,
					MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
		finally {
			setbusy(false);
			try { cts?.Dispose(); } catch { }
			cts = null;
		}
	}

	/// <summary>ListView 行：可通知勾选。</summary>
	sealed class TtsRow : INotifyPropertyChanged {
		public TtsInstallItem Item { get; }
		bool selected;

		public TtsRow(TtsInstallItem item) {
			Item = item;
			selected = item.Selected;
		}

		public bool Selected {
			get => selected;
			set {
				if (selected == value) return;
				selected = value;
				Item.Selected = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
			}
		}

		public string Title => Item.Title;
		public string LangLabel => Item.LangLabel;
		public string Engine => Item.Engine;
		public string SizeText => Item.SizeText;
		public string StateText => Item.StateText;

		public void SyncFromItem() {
			selected = Item.Selected;
		}

		public void Notify() {
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateText)));
		}

		public event PropertyChangedEventHandler PropertyChanged;
	}
}
