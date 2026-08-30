using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>
/// 安装功能：功能组件 + 发音人（TTS）双 Tab。
/// 发音人支持语言筛选，列表来自 GitHub tts-models 全量包。
/// </summary>
partial class InstallFeaturesWindow : Window {
	// 状态徽章色（未安装用强对比，避免与「已安装」混淆）
	static readonly Brush MissBg = freeze(Color.FromRgb(0xFE, 0xE2, 0xE2));
	static readonly Brush MissFg = freeze(Color.FromRgb(0xB9, 0x1C, 0x1C));
	static readonly Brush PartBg = freeze(Color.FromRgb(0xFE, 0xF3, 0xC7));
	static readonly Brush PartFg = freeze(Color.FromRgb(0xB4, 0x53, 0x09));
	static readonly Brush OkBg = freeze(Color.FromRgb(0xD1, 0xFA, 0xE5));
	static readonly Brush OkFg = freeze(Color.FromRgb(0x04, 0x78, 0x57));

	readonly List<FeatureItem> featItems = new();
	readonly Dictionary<FeatureItem, CheckBox> featChecks = new();
	readonly Dictionary<FeatureItem, TextBlock> featStates = new();
	readonly Dictionary<FeatureItem, Border> featStateBadges = new();
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

	static SolidColorBrush freeze(Color c) {
		var b = new SolidColorBrush(c);
		if (b.CanFreeze) b.Freeze();
		return b;
	}

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
		applyinstlang();
		if (firstRun) {
			try { tabmain.SelectedItem = tabfeat; } catch { }
		}
		else if (openTtsTab) {
			try { tabmain.SelectedItem = tabtts; } catch { }
		}
		else if (preferSelect != null && preferSelect.Length > 0) {
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
				MessageBox.Show(this, Loc.T("inst.busy"), Title,
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
			MessageBox.Show(this, Loc.T("inst.busy"), Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
		};
	}

	void applyinstlang() {
		if (firstRun) Title = Loc.T("inst.welcome");
		else if (preferSelect != null && preferSelect.Length > 0) Title = Loc.T("inst.need.title");
		else Title = Loc.T("inst.title");
		lbtitle.Text = Title;
		tabfeat.Header = Loc.T("inst.tab.feat");
		tabtts.Header = Loc.T("inst.tab.tts");
		lbfeathint.Text = Loc.T("inst.feat.hint");
		lblegmiss.Text = Loc.T("inst.missing");
		lblegpart.Text = Loc.T("inst.partial");
		lblegok.Text = Loc.T("inst.installed");
		lbttshint.Text = Loc.T("inst.tts.hint");
		lbttslang.Text = Loc.T("inst.tts.lang");
		cttsmissing.Content = Loc.T("inst.tts.onlymissing");
		cttssupported.Content = Loc.T("inst.tts.onlysupported");
		cttssupported.ToolTip = Loc.T("inst.tts.onlysupported.tip");
		bttsrefresh.Content = Loc.T("inst.tts.refresh");
		cttsheader.ToolTip = Loc.T("inst.tts.selectall");
		bmissing.Content = Loc.T("inst.sel.missing");
		bmissing.ToolTip = Loc.T("inst.sel.missing.tip");
		bnone.Content = Loc.T("inst.sel.none");
		ball.Content = Loc.T("inst.sel.all");
		bcancel.Content = Loc.T("cancel");
		bdelete.Content = Loc.T("inst.delete");
		bdelete.ToolTip = Loc.T("inst.delete.tip");
		binstall.Content = Loc.T("inst.install");
		bclose.Content = Loc.T("close");
		if (lvtss.View is GridView gv && gv.Columns.Count >= 6) {
			gv.Columns[1].Header = Loc.T("inst.col.model");
			gv.Columns[2].Header = Loc.T("inst.col.lang");
			gv.Columns[3].Header = Loc.T("inst.col.engine");
			gv.Columns[4].Header = Loc.T("inst.col.size");
			gv.Columns[5].Header = Loc.T("inst.col.state");
		}
		if (string.IsNullOrWhiteSpace(lbstatus.Text) || lbstatus.Text == "就绪" || lbstatus.Text == Loc.T("ready"))
			lbstatus.Text = Loc.T("ready");
	}

	// ───────── 功能组件 Tab ─────────

	void rebuildfeat() {
		featItems.Clear();
		featChecks.Clear();
		featStates.Clear();
		featStateBadges.Clear();
		featSizes.Clear();
		eitems.Children.Clear();
		featItems.AddRange(FeatureInstaller.BuildCatalog(
			firstRunDefaults: firstRun,
			preferSelect: firstRun ? null : preferSelect));
		if (firstRun)
			lbmirror.Text = Loc.T("inst.mirror.firstrun") + FeatureInstaller.MirrorHint();
		else if (preferSelect != null && preferSelect.Length > 0)
			lbmirror.Text = Loc.T("inst.mirror.prefer") + FeatureInstaller.MirrorHint();
		else
			lbmirror.Text = Loc.T("inst.mirror.default") + FeatureInstaller.MirrorHint();

		string lastCat = null;
		foreach (var it in featItems) {
			if (lastCat == null || !string.Equals(lastCat, it.Category, StringComparison.Ordinal)) {
				lastCat = it.Category;
				var catTitle = it.Category switch {
					"native" => Loc.T("feat.cat.native.opt"),
					"ocr" => Loc.T("feat.cat.ocr"),
					"asr" => Loc.T("feat.cat.asr"),
					"face" => Loc.T("feat.cat.face"),
					"accel" => Loc.T("feat.cat.accel"),
					"media" => Loc.T("feat.cat.media"),
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

			// 仅状态徽章着色（不改整行背景/边框）
			var st = new TextBlock {
				Text = it.StateText,
				FontSize = 11,
				FontWeight = FontWeights.SemiBold,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			var badge = new Border {
				Child = st,
				CornerRadius = new CornerRadius(4),
				Padding = new Thickness(8, 3, 8, 3),
				Margin = new Thickness(8, 0, 0, 0),
				VerticalAlignment = VerticalAlignment.Center,
				MinWidth = 56,
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			DockPanel.SetDock(badge, Dock.Right);
			featStates[it] = st;
			featStateBadges[it] = badge;
			row.Children.Add(badge);

			var sz = new TextBlock {
				Text = it.SizeText ?? "",
				Width = 88,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Right,
				FontSize = 11,
				Margin = new Thickness(0, 0, 4, 0),
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
				Text = it.Detail + (it.NeedsRestart ? Loc.T("inst.restart.suffix") : ""),
				FontSize = 11,
				TextWrapping = TextWrapping.Wrap,
				Foreground = (Brush)FindResource("TextMuted"),
				Margin = new Thickness(0, 2, 0, 0),
			});
			cb.Content = textCol;
			row.Children.Add(cb);
			applyfeatbadgestyle(it);
			eitems.Children.Add(row);
		}
		updatefeatsum();
	}

	void updatefeatsum() {
		if (lbfeatsum == null) return;
		var miss = featItems.Count(x => x.State == FeatureInstallState.Missing);
		var part = featItems.Count(x => x.State == FeatureInstallState.Partial);
		var ok = featItems.Count(x => x.State == FeatureInstallState.Installed);
		if (miss + part > 0)
			lbfeatsum.Text = string.Format(Loc.T("inst.feat.sum"), featItems.Count, miss, part, ok);
		else
			lbfeatsum.Text = string.Format(Loc.T("inst.feat.allok"), featItems.Count);
		lbfeatsum.Foreground = miss + part > 0 ? MissFg : OkFg;
	}

	void applyfeatbadgestyle(FeatureItem it) {
		if (!featStateBadges.TryGetValue(it, out var badge)) return;
		if (!featStates.TryGetValue(it, out var st)) return;
		st.Text = it.StateText ?? "";
		st.Foreground = statefg(it.State);
		badge.Background = statebg(it.State);
	}

	// ───────── 发音人 Tab ─────────

	async Task loadtts(bool force) {
		if (busy && force) return;
		setstatus(Loc.T("inst.tts.loading"));
		var log = new Progress<string>(appendlog);
		try {
			bttsrefresh.IsEnabled = false;
			var list = await Task.Run(async () =>
				await TtsInstallCatalog.LoadAllAsync(log, CancellationToken.None, force)
					.ConfigureAwait(false)).ConfigureAwait(true);
			ttsAll = list ?? new List<TtsInstallItem>();
			ttsLoaded = true;
			lbttssource.Text = string.Format(Loc.T("inst.tts.source"), TtsInstallCatalog.LastSource, ttsAll.Count);
			filllangcombo();
			applyttsfilter();
			setstatus(string.Format(Loc.T("inst.tts.ready"), ttsAll.Count, TtsInstallCatalog.LastSource));
		}
		catch (Exception ex) {
			appendlog(string.Format(Loc.T("inst.tts.loadfail.log"), ex.Message));
			setstatus(Loc.T("inst.tts.loadfail"));
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
		var miss = ttsRows.Count(r => r.IsMissing);
		var ok = ttsRows.Count - miss;
		lbttssource.Text = miss > 0
			? string.Format(Loc.T("inst.tts.filter"), TtsInstallCatalog.LastSource, ttsRows.Count, ttsAll.Count, miss, ok)
			: string.Format(Loc.T("inst.tts.filter.all"), TtsInstallCatalog.LastSource, ttsRows.Count, ttsAll.Count);
		lbttssource.Foreground = miss > 0 ? MissFg : (Brush)FindResource("TextMuted");
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

	static Brush statefg(FeatureInstallState st) => st switch {
		FeatureInstallState.Installed => OkFg,
		FeatureInstallState.Partial => PartFg,
		_ => MissFg,
	};

	static Brush statebg(FeatureInstallState st) => st switch {
		FeatureInstallState.Installed => OkBg,
		FeatureInstallState.Partial => PartBg,
		_ => MissBg,
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
		applyfeatbadgestyle(it);
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
		updatefeatsum();
	}

	async Task rundelete() {
		foreach (var r in ttsRows)
			r.Item.Selected = r.Selected;
		// 功能：勾选且已安装/部分
		var feats = featItems.Where(x => x.Selected
			&& x.State is FeatureInstallState.Installed or FeatureInstallState.Partial).ToList();
		var tts = ttsAll.Where(x => x.Selected && x.State == FeatureInstallState.Installed).ToList();
		if (feats.Count == 0 && tts.Count == 0) {
			MessageBox.Show(this, Loc.T("inst.delete.none"), Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var names = feats.Select(f => f.Title).Concat(tts.Select(t => t.Title)).Take(12).ToList();
		var more = feats.Count + tts.Count - names.Count;
		var msg = string.Format(Loc.T("inst.delete.confirm"),
			string.Join("\n· ", names),
			more > 0 ? string.Format(Loc.T("inst.delete.more"), more) : "");
		if (MessageBox.Show(this, msg, Loc.T("inst.delete.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
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
			appendlog(string.Format(Loc.T("inst.delete.start"), total));
			foreach (var it in feats) {
				setstatus(string.Format(Loc.T("inst.delete.step"), step + 1, total, it.Title));
				appendlog(string.Format(Loc.T("inst.delete.log"), it.Title));
				try {
					await Task.Run(() => FeatureInstaller.Uninstall(it.Kind, log)).ConfigureAwait(true);
					ok++;
					NeedRefresh = true;
					if (it.NeedsRestart) NeedRestart = true;
					refreshfeatui(it);
					appendlog(string.Format(Loc.T("inst.delete.ok"), it.Title));
				}
				catch (Exception ex) {
					fail++;
					appendlog(string.Format(Loc.T("inst.delete.fail"), ex.Message));
					CaptureLog.Ex("Uninstall " + it.Id, ex);
					refreshfeatui(it);
				}
				step++;
				setprogress(step / (double)total);
			}
			foreach (var it in tts) {
				setstatus(string.Format(Loc.T("inst.delete.step"), step + 1, total, it.Title));
				appendlog(string.Format(Loc.T("inst.delete.tts.log"), it.Title));
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
					appendlog(string.Format(Loc.T("inst.delete.ok"), it.Title));
				}
				catch (Exception ex) {
					fail++;
					appendlog(string.Format(Loc.T("inst.delete.fail"), ex.Message));
					CaptureLog.Ex("UninstallTts " + it.Id, ex);
				}
				step++;
				setprogress(step / (double)total);
			}
			if (tts.Count > 0) applyttsfilter();
			var summary = string.Format(Loc.T("inst.delete.done"), ok, fail);
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
			MessageBox.Show(this, Loc.T("inst.install.none"), Title,
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
		appendlog(string.Format(Loc.T("inst.install.start"), feats.Count, tts.Count, FeatureInstaller.FormatBytes(batchTotal)));

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
				setstatus(string.Format(Loc.T("inst.install.step"), step + 1, totalSteps, it.Title, FeatureInstaller.FormatBytes(expect)));
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
					appendlog(string.Format(Loc.T("inst.log.ok"), it.Title));
				}
				catch (OperationCanceledException) {
					appendlog(Loc.T("inst.log.cancel"));
					setstatus(Loc.T("inst.log.cancel"));
					goto done;
				}
				catch (Exception ex) {
					fail++;
					appendlog(string.Format(Loc.T("inst.log.err"), ex.Message));
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
					appendlog(string.Format(Loc.T("inst.log.ok"), it.Title));
				}
				catch (OperationCanceledException) {
					appendlog(Loc.T("inst.log.cancel"));
					setstatus(Loc.T("inst.log.cancel"));
					goto done;
				}
				catch (Exception ex) {
					fail++;
					appendlog(string.Format(Loc.T("inst.log.err"), ex.Message));
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
			var summary = string.Format(Loc.T("inst.install.done"), ok, fail);
			setstatus(summary);
			setbytes("");
			appendlog(summary);
			if (needRestart && ok > 0)
				appendlog(Loc.T("inst.restart.hint"));
			// 刷新 TTS 筛选显示
			if (tts.Count > 0)
				applyttsfilter();

			if (ok > 0) {
				var msg = summary;
				if (needRestart)
					msg += Loc.T("inst.restart.msg");
				MessageBox.Show(this, msg, Title, MessageBoxButton.OK,
					fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
			}
			else if (fail > 0) {
				MessageBox.Show(this, Loc.T("inst.install.allfail"), Title,
					MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
		finally {
			setbusy(false);
			try { cts?.Dispose(); } catch { }
			cts = null;
		}
	}

	/// <summary>ListView 行：可通知勾选与状态样式。</summary>
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
		public bool IsMissing => Item.State == FeatureInstallState.Missing;
		public Brush StateBg => statebg(Item.State);
		public Brush StateFg => statefg(Item.State);

		public void SyncFromItem() {
			selected = Item.Selected;
		}

		public void Notify() {
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateText)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMissing)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateBg)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateFg)));
		}

		public event PropertyChangedEventHandler PropertyChanged;
	}
}
