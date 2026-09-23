using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>
/// 安装功能：功能选择（确认即安装/卸载）+ 发音人（TTS）。
/// 发音人单独一页，不在功能选择树里处理。
/// </summary>
partial class InstallFeaturesWindow : Window {
	// 状态徽章色（未安装用强对比，避免与「已安装」混淆）
	static readonly Brush MissBg = freeze(Color.FromRgb(0xFE, 0xE2, 0xE2));
	static readonly Brush MissFg = freeze(Color.FromRgb(0xB9, 0x1C, 0x1C));
	static readonly Brush PartBg = freeze(Color.FromRgb(0xFE, 0xF3, 0xC7));
	static readonly Brush PartFg = freeze(Color.FromRgb(0xB4, 0x53, 0x09));
	static readonly Brush OkBg = freeze(Color.FromRgb(0xD1, 0xFA, 0xE5));
	static readonly Brush OkFg = freeze(Color.FromRgb(0x04, 0x78, 0x57));
	static readonly Brush AddBg = freeze(Color.FromRgb(0xDC, 0xFC, 0xE7));

	readonly List<FeaturePickNode> pickRoots = new();
	readonly List<FeatureItem> featItems = new();

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
		if (openTtsTab) {
			try { tabmain.SelectedItem = tabtts; } catch { }
		}
		else {
			try { tabmain.SelectedItem = tabpick; } catch { }
		}
		WindowEsc.Attach(this, () => {
			if (busy) return;
			Close();
		});
		bconfirm.Click += async (_, _) => await confirmpick();
		breset.Click += (_, _) => resetpick();
		etree.AddHandler(CheckBox.PreviewMouseLeftButtonDownEvent,
			new MouseButtonEventHandler(onpickcheckdown), true);
		etree.AddHandler(CheckBox.ClickEvent, new RoutedEventHandler(onpickclick), true);
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
			applytabbuttons();
			if (tabmain.SelectedItem == tabtts && !ttsLoaded && !busy)
				await loadtts(force: false);
		};
		lvtss.ItemsSource = ttsRows;
		Loaded += async (_, _) => {
			rebuildpick();
			loadfeat();
			applytabbuttons();
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
		tabpick.Header = Loc.T("inst.tab.pick");
		tabtts.Header = Loc.T("inst.tab.tts");
		lbpickhint.Text = Loc.T("inst.pick.hint");
		bconfirm.Content = Loc.T("inst.pick.confirm");
		bconfirm.ToolTip = Loc.T("inst.pick.confirm.tip");
		breset.Content = Loc.T("inst.pick.reset");
		breset.ToolTip = Loc.T("inst.pick.reset.tip");
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

	void applytabbuttons() {
		var onTts = tabmain.SelectedItem == tabtts;
		binstall.IsEnabled = !busy && onTts;
		bdelete.IsEnabled = !busy && onTts;
		bconfirm.IsEnabled = !busy;
		breset.IsEnabled = !busy;
		binstall.IsDefault = onTts && !busy;
		bconfirm.IsDefault = !onTts && !busy;
	}

	// ───────── 功能选择 Tab ─────────

	void rebuildpick() {
		pickRoots.Clear();
		pickRoots.AddRange(FeaturePick.BuildTree());
		applypickdefault();
		etree.ItemsSource = null;
		etree.ItemsSource = pickRoots;
		foreach (var n in pickRoots)
			watchpick(n);
		updatepicksum();
		Dispatcher.BeginInvoke(new Action(() => expandpick(etree)),
			System.Windows.Threading.DispatcherPriority.Loaded);
	}

	void applypickdefault() {
		if (preferSelect != null && preferSelect.Length > 0)
			FeaturePick.ApplyInstalled(pickRoots, extraKinds: preferSelect);
		else if (firstRun)
			FeaturePick.ApplyInstalled(pickRoots, extraIds: FeaturePick.RecommendedIds);
		else
			FeaturePick.ApplyInstalled(pickRoots);
		FeaturePick.RefreshDiff(pickRoots);
	}

	void resetpick() {
		if (busy) return;
		applypickdefault();
		updatepicksum();
	}

	void watchpick(FeaturePickNode n) {
		if (n == null) return;
		n.PropertyChanged += (_, e) => {
			if (e.PropertyName == nameof(FeaturePickNode.IsChecked))
				updatepicksum();
		};
		foreach (var c in n.Children)
			watchpick(c);
	}

	void expandpick(ItemsControl ic) {
		if (ic == null) return;
		foreach (var o in ic.Items) {
			if (ic.ItemContainerGenerator.ContainerFromItem(o) is TreeViewItem tvi) {
				tvi.IsExpanded = true;
				expandpick(tvi);
			}
		}
	}

	void onpickcheckdown(object sender, MouseButtonEventArgs e) {
		if (busy) {
			e.Handled = true;
			return;
		}
		var src = e.OriginalSource as DependencyObject;
		var cb = src as CheckBox ?? findparent<CheckBox>(src);
		if (cb?.DataContext is not FeaturePickNode n || !n.IsGroup) return;
		var allOn = n.Children.Count > 0 && n.Children.All(c => c.IsChecked == true);
		n.SetCheck(!allOn, fromUi: true);
		e.Handled = true;
		updatepicksum();
	}

	void onpickclick(object sender, RoutedEventArgs e) {
		if (busy) return;
		updatepicksum();
	}

	void updatepicksum() {
		if (lbadd == null || lbdel == null) return;
		FeaturePick.DiffSelection(pickRoots, out var addN, out var addSz, out var delN, out var delSz);
		lbadd.Text = Loc.T("inst.pick.delta.add", addN, FeatureInstaller.FormatBytes(addSz));
		lbdel.Text = Loc.T("inst.pick.delta.del", delN, FeatureInstaller.FormatBytes(delSz));
		var muted = (Brush)FindResource("TextMuted");
		if (addN > 0) {
			badd.Background = AddBg;
			lbadd.Foreground = OkFg;
		}
		else {
			badd.Background = Brushes.Transparent;
			lbadd.Foreground = muted;
		}
		if (delN > 0) {
			bdel.Background = MissBg;
			lbdel.Foreground = MissFg;
		}
		else {
			bdel.Background = Brushes.Transparent;
			lbdel.Foreground = muted;
		}
	}

	async Task confirmpick() {
		if (busy) return;
		foreach (var it in featItems)
			FeatureInstaller.RefreshState(it);
		FeaturePick.CollectDelta(pickRoots, out var addKinds, out var delKinds);
		var add = featItems.Where(x => addKinds.Contains(x.Kind)).ToList();
		var del = featItems.Where(x => delKinds.Contains(x.Kind)).ToList();
		if (add.Count == 0 && del.Count == 0) {
			MessageBox.Show(this, Loc.T("inst.pick.nodelta"), Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		await runpick(add, del);
	}

	void loadfeat() {
		featItems.Clear();
		featItems.AddRange(FeatureInstaller.BuildCatalog(
			firstRunDefaults: firstRun,
			preferSelect: firstRun ? null : preferSelect));
		if (firstRun)
			lbmirror.Text = Loc.T("inst.mirror.firstrun") + FeatureInstaller.MirrorHint();
		else if (preferSelect != null && preferSelect.Length > 0)
			lbmirror.Text = Loc.T("inst.mirror.prefer") + FeatureInstaller.MirrorHint();
		else
			lbmirror.Text = Loc.T("inst.mirror.default") + FeatureInstaller.MirrorHint();
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
			FeaturePick.SelectMissing(pickRoots);
			updatepicksum();
		}
	}

	void selectall(bool on) {
		if (busy) return;
		if (tabmain.SelectedItem == tabtts) {
			setttscheckall(on);
			cttsheader.IsChecked = on;
		}
		else {
			FeaturePick.SelectAll(pickRoots, on);
			updatepicksum();
		}
	}

	static T findparent<T>(DependencyObject d) where T : class {
		while (d != null) {
			if (d is T t) return t;
			d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
		}
		return null;
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
		bcancel.IsEnabled = on;
		bmissing.IsEnabled = !on;
		bnone.IsEnabled = !on;
		ball.IsEnabled = !on;
		bttsrefresh.IsEnabled = !on;
		ettslang.IsEnabled = !on;
		cttsmissing.IsEnabled = !on;
		cttssupported.IsEnabled = !on;
		etree.IsEnabled = !on;
		lvtss.IsEnabled = !on;
		applytabbuttons();
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

	void refreshfeat(FeatureItem it) {
		if (it == null) return;
		FeatureInstaller.RefreshState(it);
	}

	void afterpickchange() {
		foreach (var it in featItems)
			refreshfeat(it);
		FeaturePick.RefreshDiff(pickRoots);
		updatepicksum();
	}

	async Task runpick(List<FeatureItem> add, List<FeatureItem> del) {
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
		foreach (var it in add)
			batchTotal += it.SizeBytes > 0 ? it.SizeBytes : FeatureInstaller.ExpectedSize(it.Kind);
		var totalSteps = add.Count + del.Count;
		var step = 0;
		long batchDone = 0;
		appendlog(FeatureInstaller.MirrorHint());
		appendlog(Loc.T("inst.pick.apply.start", add.Count, del.Count, FeatureInstaller.FormatBytes(batchTotal)));
		setbytes(batchTotal > 0
			? FeatureInstaller.FormatBytes(0) + " / " + FeatureInstaller.FormatBytes(batchTotal)
			: "");
		try {
			foreach (var it in del) {
				cts.Token.ThrowIfCancellationRequested();
				setstatus(string.Format(Loc.T("inst.delete.step"), step + 1, totalSteps, it.Title));
				appendlog(string.Format(Loc.T("inst.delete.log"), it.Title));
				try {
					await Task.Run(() => FeatureInstaller.Uninstall(it.Kind, log)).ConfigureAwait(true);
					ok++;
					anyRefresh = true;
					if (it.NeedsRestart) needRestart = true;
					refreshfeat(it);
					appendlog(string.Format(Loc.T("inst.delete.ok"), it.Title));
				}
				catch (OperationCanceledException) {
					appendlog(Loc.T("inst.log.cancel"));
					setstatus(Loc.T("inst.log.cancel"));
					goto done;
				}
				catch (Exception ex) {
					fail++;
					appendlog(string.Format(Loc.T("inst.delete.fail"), ex.Message));
					CaptureLog.Ex("Uninstall " + it.Id, ex);
					refreshfeat(it);
				}
				step++;
				setprogress(totalSteps > 0 ? step / (double)totalSteps : 1);
			}
			for (var i = 0; i < add.Count; i++) {
				cts.Token.ThrowIfCancellationRequested();
				var it = add[i];
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
					refreshfeat(it);
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
					refreshfeat(it);
				}
				batchDone += expect;
				if (batchTotal > 0 && batchDone > batchTotal) batchDone = batchTotal;
				step++;
			}
		done:
			setprogress(1);
			NeedRefresh = anyRefresh && ok > 0;
			NeedRestart = needRestart;
			afterpickchange();
			var summary = string.Format(Loc.T("inst.install.done"), ok, fail);
			setstatus(summary);
			setbytes("");
			appendlog(summary);
			if (needRestart && ok > 0)
				appendlog(Loc.T("inst.restart.hint"));
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

	async Task rundelete() {
		foreach (var r in ttsRows)
			r.Item.Selected = r.Selected;
		var tts = ttsAll.Where(x => x.Selected && x.State == FeatureInstallState.Installed).ToList();
		if (tts.Count == 0) {
			MessageBox.Show(this, Loc.T("inst.delete.none"), Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var names = tts.Select(t => t.Title).Take(12).ToList();
		var more = tts.Count - names.Count;
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
		var total = tts.Count;
		var step = 0;
		try {
			appendlog(string.Format(Loc.T("inst.delete.start"), total));
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
		foreach (var r in ttsRows)
			r.Item.Selected = r.Selected;
		var tts = ttsAll.Where(x => x.Selected && x.State != FeatureInstallState.Installed).ToList();

		if (tts.Count == 0) {
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
		foreach (var it in tts)
			batchTotal += it.SizeBytes;

		appendlog(FeatureInstaller.MirrorHint());
		appendlog(string.Format(Loc.T("inst.install.start"), 0, tts.Count, FeatureInstaller.FormatBytes(batchTotal)));

		var totalSteps = tts.Count;
		var step = 0;
		long batchDone = 0;
		setbytes(batchTotal > 0
			? FeatureInstaller.FormatBytes(0) + " / " + FeatureInstaller.FormatBytes(batchTotal)
			: "");

		try {
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
