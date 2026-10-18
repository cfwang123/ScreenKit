using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenKit;

/// <summary>MainWindow：翻译 Tab（本地 ONNX 或 LLM）。</summary>
public partial class MainWindow {
	TranslateEngine trEngine;
	List<TranslateModelInfo> trModels = new();
	bool trUiLoading;
	CancellationTokenSource trCts;
	bool trBusy;
	TtsPlayer trSpeakPlayer;
	int trSpeakGen;

	void inittranslate() {
		dicthost.OnTranslate = text => {
			ensuretranslatepopup();
			trPopup.ShowText(text);
		};
		dicthost.Options = () => opt;
		try { trEngine = new TranslateEngine(); }
		catch (Exception ex) {
			CaptureLog.Ex("TranslateEngine init", ex);
			trEngine = null;
		}

		trUiLoading = true;
		etrcompute.Items.Clear();
		etrcompute.Items.Add(new ComboBoxItem {
			Content = Loc.Compute(TtsComputeMode.Auto),
			Tag = TtsComputeMode.Auto,
			ToolTip = Loc.T("tr.compute.tip"),
		});
		etrcompute.Items.Add(new ComboBoxItem {
			Content = Loc.Compute(TtsComputeMode.Gpu),
			Tag = TtsComputeMode.Gpu,
			ToolTip = Loc.T("tr.compute.tip"),
		});
		etrcompute.Items.Add(new ComboBoxItem {
			Content = Loc.Compute(TtsComputeMode.Igpu),
			Tag = TtsComputeMode.Igpu,
			ToolTip = Loc.T("tr.compute.tip"),
		});
		etrcompute.Items.Add(new ComboBoxItem { Content = Loc.Compute(TtsComputeMode.Cpu), Tag = TtsComputeMode.Cpu });
		var wantComp = (opt.TranslateCompute ?? "Auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
		foreach (ComboBoxItem it in etrcompute.Items) {
			if (it.Tag is TtsComputeMode m && m == wantComp) {
				etrcompute.SelectedItem = it;
				break;
			}
		}
		if (etrcompute.SelectedItem == null) etrcompute.SelectedIndex = 0;
		filltrengine();
		trUiLoading = false;

		etrsrclng.SelectionChanged += (_, _) => {
			if (trUiLoading) return;
			filldstlangcombo(preserve: true);
			syncmodelcombo();
		};
		etrdstlng.SelectionChanged += (_, _) => {
			if (trUiLoading) return;
			syncmodelcombo();
		};
		etrsrc.TextChanged += (_, _) => syncautodetect();
		etrengine.SelectionChanged += (_, _) => {
			if (trUiLoading) return;
			synctrengineui();
			filltrlangcombos();
			savetrprefs();
			try { trPopup?.SyncEngine(); } catch { }
		};
		etrcompute.SelectionChanged += (_, _) => {
			if (trUiLoading) return;
			if (etrcompute.SelectedItem is ComboBoxItem it && it.Tag is TtsComputeMode) {
				lbtrstatus.Text = Loc.T("tr.dev.changed", it.Content);
				try { trEngine?.UnloadAll(); } catch { }
				savetrprefs();
			}
		};
		btrreload.Click += (_, _) => {
			scantrmodels();
			filltrlangcombos();
			lbtrstatus.Text = usellm() ? Loc.T("tr.refreshed.llm") : Loc.T("tr.refreshed");
		};
		lktrchromepage.Click += (_, _) => opentrchromepage();
		btrgo.Click += async (_, _) => await runtranslate();
		tabtr.PreviewKeyDown += (_, e) => {
			if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
			e.Handled = true;
			_ = runtranslate();
		};
		btrpingpong.Click += async (_, _) => await runpingpong();
		btrstop.Click += (_, _) => {
			try { trCts?.Cancel(); } catch { }
			lbtrstatus.Text = Loc.T("cancelling");
		};
		btrspeaksrc.Click += async (_, _) => await speaktrside(true);
		btrspeakdst.Click += async (_, _) => await speaktrside(false);
		btrswap.Click += (_, _) => {
			// 交换源/目标语言（自动保持自动）；有译文则对调文本
			var src = selectedlang(etrsrclng);
			var dst = selectedlang(etrdstlng);
			if (src == TrLang.Auto || dst == TrLang.Auto) {
				// 自动：按当前原文检测后显式设为反向
				opt.AutoTranslatePair(out var nat, out var lrn);
				var probe = etrsrc?.Text ?? "";
				// 交换：原文若已是译文区则用译文检测
				if (!string.IsNullOrWhiteSpace(etrdst.Text))
					probe = etrdst.Text;
				LangDetect.DetectPair(probe, nat, lrn, out var a, out var b);
				picklang(etrsrclng, b);
				filldstlangcombo(preserve: false);
				picklang(etrdstlng, a);
			}
			else {
				picklang(etrsrclng, dst);
				filldstlangcombo(preserve: false);
				// 若新源下没有原源作目标，尽量选原源
				picklang(etrdstlng, src);
			}
			if (!string.IsNullOrWhiteSpace(etrdst.Text)) {
				var t = etrsrc.Text ?? "";
				etrsrc.Text = etrdst.Text;
				etrdst.Text = t;
			}
			syncmodelcombo();
			lbtrstatus.Text = Loc.T("tr.swap.done");
		};

		scantrmodels();
		filltrlangcombos();
		synctrengineui();
	}

	/// <summary>设置窗保存 LLM 列表后刷新翻译引擎下拉。</summary>
	void refreshtrllm() {
		filltrengine();
		filltrlangcombos();
		synctrengineui();
		try { trPopup?.Reload(); } catch { }
	}

	void ensuretranslatepopup() {
		if (trPopup != null) return;
		trPopup = new TranslatePopupWindow(() => opt, trEngine, () => {
			filltrengine();
			filltrlangcombos();
			synctrengineui();
		});
		trPopup.Closed += (_, _) => trPopup = null;
	}

	void showtranslatepopup() {
		if (!opt.ModTranslate) return;
		ensuretranslatepopup();
		trPopup.ShowFromHotkey();
	}

	void toggletranslatepopup() {
		if (!opt.ModTranslate) {
			try { trPopup?.Hide(); } catch { }
			return;
		}
		ensuretranslatepopup();
		if (trPopup.IsVisible) {
			trPopup.Hide();
			return;
		}
		trPopup.ShowFromHotkey();
	}

	void showdicttab() => opendicttab();

	void toggledicttab() {
		if (!opt.ModDict) return;
		if (mainwindowforeground()) {
			if (tray != null) tray.hidewindow();
			else try { Hide(); } catch { }
			return;
		}
		opendicttab(null);
		var q = "";
		try { q = DictClip.Word(ImageUtil.ClipboardUnicodeText()); }
		catch (Exception ex) { try { CaptureLog.Ex("dict clip", ex); } catch { } }
		if (q.Length == 0) return;
		var word = q;
		Dispatcher.BeginInvoke(new Action(() => {
			try { dicthost.SearchText(word); } catch { }
		}), System.Windows.Threading.DispatcherPriority.Background);
	}

	void opendicttab(string query = null) {
		if (!opt.ModDict || !opt.TabDictVisible) return;
		if (tray != null) tray.showwindow();
		else {
			if (!IsVisible) Show();
			if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
			try { Activate(); } catch { }
		}
		selectmaintab(tabdict);
		var q = query;
		Dispatcher.BeginInvoke(new Action(() => {
			try { dicthost.FocusSearch(); } catch { }
			if (!string.IsNullOrWhiteSpace(q))
				try { dicthost.SearchText(q); } catch { }
		}), System.Windows.Threading.DispatcherPriority.Background);
	}

	void filltrengine() {
		trUiLoading = true;
		try {
			var want = (opt.TranslateLlm ?? "").Trim();
			etrengine.Items.Clear();
			etrengine.Items.Add(new ComboBoxItem {
				Content = Loc.T("tr.engine.onnx"),
				Tag = "",
				ToolTip = Loc.T("tr.hint"),
			});
			var chrome = new ComboBoxItem {
				Content = Loc.T("tr.engine.chrome"),
				Tag = "chrome",
				ToolTip = Loc.T("tr.hint.chrome"),
			};
			etrengine.Items.Add(chrome);
			ComboBoxItem pick = null;
			if (want.Equals("chrome", StringComparison.OrdinalIgnoreCase))
				pick = chrome;
			if (opt.LlmList != null) {
				foreach (var ep in opt.LlmList) {
					if (ep == null) continue;
					var name = ep.DisplayName;
					if (name.Length == 0) continue;
					var it = new ComboBoxItem {
						Content = name,
						Tag = ep,
						ToolTip = string.IsNullOrWhiteSpace(ep.Model) ? name : ep.Model,
					};
					etrengine.Items.Add(it);
					if (pick == null &&
						(string.Equals(name, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(ep.Model ?? "", want, StringComparison.OrdinalIgnoreCase)))
						pick = it;
				}
			}
			etrengine.SelectedItem = pick ?? etrengine.Items[0];
		}
		finally { trUiLoading = false; }
	}

	/// <summary>用本机 Chrome 打开「Chrome 模型」页，手动安装或卸载语言包。</summary>
	void opentrchromepage() {
		var exe = ChromeTts.FindExe(opt.ChromeExe);
		if (string.IsNullOrEmpty(exe)) {
			lbtrstatus.Text = Loc.T("tr.need.chrome");
			return;
		}
		var port = opt.HttpPort > 0 ? opt.HttpPort : 1224;
		var url = "http://127.0.0.1:" + port + "/chrome";
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
				FileName = exe,
				Arguments = "\"" + url + "\"",
				UseShellExecute = false,
			});
		}
		catch (Exception ex) {
			showwarnmsg(ex.Message, Loc.T("tab.translate"));
		}
	}

	bool usechrome() =>
		etrengine?.SelectedItem is ComboBoxItem it && it.Tag is string s
		&& s.Equals("chrome", StringComparison.OrdinalIgnoreCase);

	bool usellm() =>
		etrengine?.SelectedItem is ComboBoxItem it && it.Tag is LlmEndpoint;

	LlmEndpoint currenttrllm() =>
		etrengine?.SelectedItem is ComboBoxItem it ? it.Tag as LlmEndpoint : null;

	void synctrengineui() {
		var llm = usellm();
		var chrome = usechrome();
		var local = !llm && !chrome;
		etrcompute.IsEnabled = local;
		etrmodel.IsEnabled = local;
		etrcompute.Opacity = local ? 1 : 0.45;
		etrmodel.Opacity = local ? 1 : 0.45;
		if (chrome) {
			lbtrhint.Text = Loc.T("tr.hint.chrome");
		}
		else if (llm) {
			var ep = currenttrllm();
			var n = ep != null ? ep.DisplayName : "";
			lbtrhint.Text = Loc.T("tr.hint.llm", n);
		}
		else {
			var ready = trModels.Count(m => m.IsReady);
			lbtrhint.Text = ready > 0
				? Loc.T("tr.hint.onnx", TranslateModelScanner.ResolveRoot(), ready)
				: Loc.T("tr.hint.onnx.none", TranslateModelScanner.ModelsRoot());
		}
	}

	void scantrmodels() {
		try {
			trModels = TranslateModelScanner.Scan();
			synctrengineui();
		}
		catch (Exception ex) {
			trModels = new List<TranslateModelInfo>();
			lbtrhint.Text = Loc.T("tr.scan.fail", ex.Message);
		}
	}

	void filltrlangcombos() {
		trUiLoading = true;
		try {
			fillsrclangcombo();
			filldstlangcombo(preserve: false);
			syncmodelcombo();
			if (usechrome())
				lbtrstatus.Text = Loc.T("tr.ready.chrome");
			else if (usellm()) {
				var ep = currenttrllm();
				lbtrstatus.Text = Loc.T("tr.ready.llm", ep != null ? ep.DisplayName : "");
			}
			else if (trModels.Count == 0 || !trModels.Any(m => m.IsReady))
				lbtrstatus.Text = Loc.T("tr.ready.none");
			else
				lbtrstatus.Text = Loc.T("tr.ready.onnx", trModels.Count(m => m.IsReady));
		}
		finally {
			trUiLoading = false;
		}
	}

	void fillsrclangcombo() {
		var prev = selectedlang(etrsrclng);
		etrsrclng.Items.Clear();
		// 自动：母语 ↔ 学习语言
		etrsrclng.Items.Add(new ComboBoxItem {
			Content = TrLang.AutoLabel(opt.TranslateAutoNative, opt.TranslateAutoLearn),
			Tag = TrLang.Auto,
			ToolTip = Loc.T("tr.src.tip"),
		});
		var srcs = trModels.Where(m => m.IsReady)
			.Select(m => m.SourceLang)
			.Where(s => !string.IsNullOrEmpty(s))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (usellm() || usechrome()) {
			foreach (var s in TrLang.LlmCodes)
				if (!srcs.Any(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)))
					srcs.Add(s);
		}
		srcs.Sort(TrLang.CompareLlm);
		foreach (var s in srcs) {
			etrsrclng.Items.Add(new ComboBoxItem {
				Content = TrLang.Label(s),
				Tag = s,
			});
		}
		if (!picklang(etrsrclng, prev) && etrsrclng.Items.Count > 0)
			etrsrclng.SelectedIndex = 0;
	}

	void filldstlangcombo(bool preserve) {
		var prev = preserve ? selectedlang(etrdstlng) : "";
		var src = selectedlang(etrsrclng);
		etrdstlng.Items.Clear();

		if (src == TrLang.Auto || string.IsNullOrEmpty(src)) {
			etrdstlng.Items.Add(new ComboBoxItem {
				Content = TrLang.AutoLabel(opt.TranslateAutoNative, opt.TranslateAutoLearn),
				Tag = TrLang.Auto,
				ToolTip = Loc.T("tr.dst.tip"),
			});
			etrdstlng.SelectedIndex = 0;
			return;
		}

		var tgts = trModels.Where(m => m.IsReady
				&& string.Equals(m.SourceLang, src, StringComparison.OrdinalIgnoreCase))
			.Select(m => m.TargetLang)
			.Where(t => !string.IsNullOrEmpty(t))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (usellm() || usechrome()) {
			foreach (var t in TrLang.LlmCodes) {
				if (string.Equals(t, src, StringComparison.OrdinalIgnoreCase)) continue;
				if (!tgts.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
					tgts.Add(t);
			}
		}
		tgts.Sort(TrLang.CompareLlm);

		foreach (var t in tgts) {
			etrdstlng.Items.Add(new ComboBoxItem {
				Content = TrLang.Label(t),
				Tag = t,
			});
		}
		if (etrdstlng.Items.Count == 0) {
			etrdstlng.Items.Add(new ComboBoxItem {
				Content = Loc.T("lang.none.tgt"),
				Tag = "",
				IsEnabled = false,
			});
			etrdstlng.SelectedIndex = 0;
			return;
		}
		if (!picklang(etrdstlng, prev))
			etrdstlng.SelectedIndex = 0;
	}

	static string selectedlang(ComboBox box) {
		if (box?.SelectedItem is ComboBoxItem it && it.Tag is string s)
			return TrLang.Normalize(s);
		return "";
	}

	static bool picklang(ComboBox box, string code) {
		if (box == null || string.IsNullOrEmpty(code)) return false;
		code = TrLang.Normalize(code);
		foreach (ComboBoxItem it in box.Items) {
			if (it.Tag is string t && string.Equals(TrLang.Normalize(t), code, StringComparison.OrdinalIgnoreCase)) {
				box.SelectedItem = it;
				return true;
			}
		}
		return false;
	}

	void syncmodelcombo() {
		trUiLoading = true;
		try {
			var uiSrc = selectedlang(etrsrclng);
			var uiDst = selectedlang(etrdstlng);
			List<TranslateModelInfo> match;
			if (uiSrc == TrLang.Auto || uiDst == TrLang.Auto) {
				// 自动：展示母语 ↔ 学习语言的模型
				opt.AutoTranslatePair(out var nat, out var lrn);
				match = trModels.Where(m => m.IsReady && isautopair(m, nat, lrn)).ToList();
			}
			else {
				var key = LangDetect.DirKey(uiSrc, uiDst);
				match = trModels.Where(m => m.IsReady
					&& string.Equals(m.DirKey, key, StringComparison.OrdinalIgnoreCase)).ToList();
				if (match.Count == 0)
					match = trModels.Where(m => m.IsReady
						&& string.Equals(m.SourceLang, uiSrc, StringComparison.OrdinalIgnoreCase)
						&& string.Equals(m.TargetLang, uiDst, StringComparison.OrdinalIgnoreCase)).ToList();
			}
			etrmodel.ItemsSource = null;
			etrmodel.DisplayMemberPath = "ListName";
			etrmodel.ItemsSource = match.Count > 0 ? match : trModels.Where(m => m.IsReady).ToList();
			etrmodel.SelectedItem = match.FirstOrDefault(m => m.IsReady)
				?? trModels.FirstOrDefault(m => m.IsReady);
		}
		finally {
			trUiLoading = false;
		}
		syncautodetect();
	}

	/// <summary>原文/译文旁显示实际语言；「自动」时另显示检测方向。</summary>
	void syncautodetect() {
		var auto = selectedlang(etrsrclng) == TrLang.Auto || selectedlang(etrdstlng) == TrLang.Auto;
		resolvepair(out var src, out var dst);
		if (lbtrsrclang != null)
			lbtrsrclang.Text = src.Length > 0 ? TrLang.Label(src) : "";
		if (lbtrdstlang != null)
			lbtrdstlang.Text = dst.Length > 0 ? TrLang.Label(dst) : "";
		if (lbtrdetect == null) return;
		if (!auto) {
			lbtrdetect.Visibility = Visibility.Collapsed;
			return;
		}
		lbtrdetect.Text = Loc.T("tr.auto.detect", TrLang.Label(src), TrLang.Label(dst));
		lbtrdetect.Visibility = Visibility.Visible;
	}

	static bool isautopair(TranslateModelInfo m, string nat, string lrn) {
		if (m == null) return false;
		var a = m.SourceLang;
		var b = m.TargetLang;
		return (a == nat && b == lrn) || (a == lrn && b == nat);
	}

	/// <summary>解析当前 UI 源/目标；自动时按母语/学习语言检测。</summary>
	void resolvepair(out string src, out string dst) {
		src = selectedlang(etrsrclng);
		dst = selectedlang(etrdstlng);
		if (src == TrLang.Auto || dst == TrLang.Auto || string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) {
			opt.AutoTranslatePair(out var nat, out var lrn);
			LangDetect.DetectPair(etrsrc?.Text, nat, lrn, out src, out dst);
		}
	}

	TtsComputeMode trcurcompute() {
		if (etrcompute?.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode m)
			return m;
		return (opt.TranslateCompute ?? "Auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
	}

	void savetrprefs() {
		try {
			if (etrcompute.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode cm)
				opt.TranslateCompute = cm.ToString();
			var ep = currenttrllm();
			opt.TranslateLlm = usechrome() ? "chrome" : (ep != null ? ep.DisplayName : "");
			AppConfig.Save(opt);
		}
		catch (Exception ex) {
			CaptureLog.Ex("savetrprefs", ex);
		}
	}

	/// <summary>解析 UI 方向与正向模型；失败时写 lbtrstatus 并返回 false。</summary>
	bool tryresolveforward(out string src, out string dst, out TranslateModelInfo model, out string key) {
		src = dst = key = "";
		model = null;
		var textIn = etrsrc?.Text ?? "";
		if (string.IsNullOrWhiteSpace(textIn)) {
			lbtrstatus.Text = Loc.T("tr.popup.need_src");
			return false;
		}
		if (textIn.Length > 8000) {
			lbtrstatus.Text = Loc.T("tr.popup.too_long");
			return false;
		}

		resolvepair(out src, out dst);
		key = LangDetect.DirKey(src, dst);
		if (usechrome()) {
			if (ChromeTts.FindExe(opt.ChromeExe) == null) {
				lbtrstatus.Text = Loc.T("tr.need.chrome");
				return false;
			}
			model = null;
			return true;
		}
		if (usellm()) {
			var ep = currenttrllm();
			if (!AsrLlmClient.IsEndpointReady(ep)) {
				lbtrstatus.Text = Loc.T("tr.need.llm");
				return false;
			}
			model = null;
			return true;
		}

		var dirKey = key;
		model = etrmodel.SelectedItem as TranslateModelInfo;
		if (model == null || !model.IsReady
			|| !string.Equals(model.DirKey, dirKey, StringComparison.OrdinalIgnoreCase)) {
			model = trModels.FirstOrDefault(m => m.IsReady
				&& string.Equals(m.DirKey, dirKey, StringComparison.OrdinalIgnoreCase));
		}
		if (model == null || !model.IsReady) {
			lbtrstatus.Text = Loc.T("tr.need.model", TrLang.Label(src), TrLang.Label(dst), dirKey);
			return false;
		}
		if (trEngine == null) {
			lbtrstatus.Text = Loc.T("tr.popup.need_engine");
			return false;
		}
		return true;
	}

	async Task runtranslate() {
		if (trBusy) return;
		if (!tryresolveforward(out var src, out var dst, out var model, out var key)) {
			if (usellm() || usechrome()) return;
			if (!FeaturePrompt.EnsureTranslateModels(this))
				return;
			try { scantrmodels(); } catch { }
			if (!tryresolveforward(out src, out dst, out model, out key))
				return;
		}

		var textIn = etrsrc?.Text ?? "";
		var compute = trcurcompute();
		var prefer = TranslateEngine.PreferFromMode(compute);
		savetrprefs();

		trBusy = true;
		btrgo.IsEnabled = false;
		btrpingpong.IsEnabled = false;
		btrstop.IsEnabled = true;
		trCts = new CancellationTokenSource();
		var ct = trCts.Token;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var llm = usellm();
		var chrome = usechrome();
		var ep = currenttrllm();
		lbtrstatus.Text = chrome
			? Loc.T("tr.run.chrome", TrLang.Label(src), TrLang.Label(dst))
			: llm
			? Loc.T("tr.run.llm", TrLang.Label(src), TrLang.Label(dst), ep.DisplayName)
			: Loc.T("tr.run.onnx", TrLang.Label(src), TrLang.Label(dst), prefer);

		try {
			string result;
			if (chrome) {
				var text = textIn;
				var s = src;
				var d = dst;
				var exe = opt.ChromeExe;
				var idle = opt.ChromeIdleSec;
				result = await Task.Run(() => ChromeTr.Translate(text, s, d, exe, idle, ct, chromedown(lbtrstatus)), ct)
					.ConfigureAwait(true);
			}
			else if (llm) {
				var o = opt;
				var text = textIn;
				var s = src;
				var d = dst;
				result = await Task.Run(() => AsrLlmClient.Translate(o, text, s, d, ct), ct)
					.ConfigureAwait(true);
			}
			else {
				var engine = trEngine;
				var modelDir = model.ModelDir;
				var text = textIn;
				var dirKey = key;
				result = await Task.Run(() => {
					if (!engine.EnsureLoaded(dirKey, modelDir, prefer))
						throw new InvalidOperationException(engine.LastError ?? Loc.T("tr.load.fail"));
					ct.ThrowIfCancellationRequested();
					return engine.Translate(dirKey, text, ct);
				}, ct).ConfigureAwait(true);
			}

			etrdst.Text = result ?? "";
			var empty = string.IsNullOrWhiteSpace(result) ? Loc.T("tr.empty.out") : "";
			if (chrome)
				lbtrstatus.Text = Loc.T("tr.ok.chrome", TrLang.Label(src), TrLang.Label(dst),
					sw.ElapsedMilliseconds) + empty;
			else if (llm)
				lbtrstatus.Text = Loc.T("tr.ok.llm", TrLang.Label(src), TrLang.Label(dst), ep.DisplayName,
					sw.ElapsedMilliseconds) + empty;
			else {
				var dev = string.IsNullOrEmpty(trEngine.LastDevice) ? prefer : trEngine.LastDevice;
				var be = string.IsNullOrEmpty(trEngine.LastBackend) ? "" : trEngine.LastBackend + "/";
				lbtrstatus.Text = Loc.T("tr.ok.onnx", TrLang.Label(src), TrLang.Label(dst), be + dev,
					sw.ElapsedMilliseconds) + empty;
			}
		}
		catch (OperationCanceledException) {
			lbtrstatus.Text = Loc.T("tr.cancelled");
		}
		catch (Exception ex) {
			CaptureLog.Ex("translate", ex);
			var msg = ex.Message ?? "";
			if (!llm && !chrome && (prefer == "cuda" || Compat.Contains(msg, "CUDA", StringComparison.OrdinalIgnoreCase)))
				msg += Loc.T("tr.cuda.hint");
			lbtrstatus.Text = Loc.T("tr.fail", msg);
		}
		finally {
			trBusy = false;
			btrgo.IsEnabled = true;
			btrpingpong.IsEnabled = true;
			btrstop.IsEnabled = false;
			try { trCts?.Dispose(); } catch { }
			trCts = null;
		}
	}

	/// <summary>来回翻译 20 次：弹出窗口逐步显示 A→B→A… 过程。</summary>
	async Task runpingpong() {
		if (trBusy) return;
		if (!tryresolveforward(out var src, out var dst, out var forward, out var key))
			return;

		var textIn = (etrsrc?.Text ?? "").Trim();
		savetrprefs();

		if (usechrome()) {
			var fwdLabel = $"{TrLang.Label(src)}→{TrLang.Label(dst)}";
			var revLabel = $"{TrLang.Label(dst)}→{TrLang.Label(src)}";
			var exe = opt.ChromeExe;
			var idle = opt.ChromeIdleSec;
			var s0 = src;
			var d0 = dst;
			trBusy = true;
			btrgo.IsEnabled = false;
			btrpingpong.IsEnabled = false;
			btrstop.IsEnabled = false;
			lbtrstatus.Text = Loc.T("tr.pp.run.llm", TranslatePingPongWindow.DefaultRounds);
			try {
				var dlg = new TranslatePingPongWindow(
					textIn, fwdLabel, revLabel, TranslatePingPongWindow.DefaultRounds,
					(t, rev, ct, progress) => ChromeTr.Translate(t, rev ? d0 : s0, rev ? s0 : d0, exe, idle, ct, progress));
				attachdialogowner(dlg);
				dlg.ShowDialog();
				applypingpongresult(dlg);
			}
			catch (Exception ex) {
				CaptureLog.Ex("runpingpong", ex);
				lbtrstatus.Text = Loc.T("tr.pp.fail", ex.Message);
			}
			finally {
				trBusy = false;
				btrgo.IsEnabled = true;
				btrpingpong.IsEnabled = true;
				btrstop.IsEnabled = false;
			}
			await Task.CompletedTask;
			return;
		}

		if (usellm()) {
			var fwdLabel = $"{TrLang.Label(src)}→{TrLang.Label(dst)}";
			var revLabel = $"{TrLang.Label(dst)}→{TrLang.Label(src)}";
			var o = opt;
			var s0 = src;
			var d0 = dst;
			trBusy = true;
			btrgo.IsEnabled = false;
			btrpingpong.IsEnabled = false;
			btrstop.IsEnabled = false;
			lbtrstatus.Text = Loc.T("tr.pp.run.llm", TranslatePingPongWindow.DefaultRounds);
			try {
				var dlg = new TranslatePingPongWindow(
					textIn, fwdLabel, revLabel, TranslatePingPongWindow.DefaultRounds,
					(t, rev, ct, _) => AsrLlmClient.Translate(o, t, rev ? d0 : s0, rev ? s0 : d0, ct));
				attachdialogowner(dlg);
				dlg.ShowDialog();
				applypingpongresult(dlg);
			}
			catch (Exception ex) {
				CaptureLog.Ex("runpingpong", ex);
				lbtrstatus.Text = Loc.T("tr.pp.fail", ex.Message);
			}
			finally {
				trBusy = false;
				btrgo.IsEnabled = true;
				btrpingpong.IsEnabled = true;
				btrstop.IsEnabled = false;
			}
			await Task.CompletedTask;
			return;
		}

		var revKey = LangDetect.DirKey(dst, src);
		var reverse = trModels.FirstOrDefault(m => m.IsReady
			&& string.Equals(m.DirKey, revKey, StringComparison.OrdinalIgnoreCase));
		if (reverse == null || !reverse.IsReady) {
			lbtrstatus.Text = Loc.T("tr.pp.need.rev", key, revKey);
			return;
		}

		var compute = trcurcompute();
		var prefer = TranslateEngine.PreferFromMode(compute);

		trBusy = true;
		btrgo.IsEnabled = false;
		btrpingpong.IsEnabled = false;
		btrstop.IsEnabled = false;
		lbtrstatus.Text = Loc.T("tr.pp.run", TranslatePingPongWindow.DefaultRounds);

		try {
			var dlg = new TranslatePingPongWindow(
				trEngine, forward, reverse, textIn, prefer, TranslatePingPongWindow.DefaultRounds);
			attachdialogowner(dlg);
			dlg.ShowDialog();
			applypingpongresult(dlg);
		}
		catch (Exception ex) {
			CaptureLog.Ex("runpingpong", ex);
			lbtrstatus.Text = Loc.T("tr.pp.fail", ex.Message);
		}
		finally {
			trBusy = false;
			btrgo.IsEnabled = true;
			btrpingpong.IsEnabled = true;
			btrstop.IsEnabled = false;
		}
		await Task.CompletedTask;
	}

	void applypingpongresult(TranslatePingPongWindow dlg) {
		if (!string.IsNullOrEmpty(dlg.FinalText)) {
			etrdst.Text = dlg.FinalText;
			if (dlg.EarlyStopped)
				lbtrstatus.Text = Loc.T("tr.pp.stable", dlg.CompletedSteps);
			else
				lbtrstatus.Text = dlg.Completed
					? Loc.T("tr.pp.done", TranslatePingPongWindow.DefaultRounds)
					: Loc.T("tr.pp.partial");
		}
		else
			lbtrstatus.Text = Loc.T("tr.pp.closed");
	}

	Action<double> chromedown(TextBlock lb) => part => {
		var line = ChromeTr.DownText(part);
		try { Dispatcher.BeginInvoke(new Action(() => { if (lb != null) lb.Text = line; })); }
		catch { }
	};

	/// <summary>朗读原文（src=true）或译文；语言取当前方向，自动时取检测结果。</summary>
	async Task speaktrside(bool src) {
		var text = ((src ? etrsrc?.Text : etrdst?.Text) ?? "").Trim();
		if (text.Length == 0) {
			lbtrstatus.Text = src ? Loc.T("tr.popup.need_src") : Loc.T("tr.dst.empty");
			return;
		}
		var gen = ++trSpeakGen;
		var code = "";
		try {
			resolvepair(out var s, out var d);
			code = speakcode(src ? s : d);
			if (code.Length == 0) {
				lbtrstatus.Text = Loc.T("tr.popup.need_lang");
				return;
			}
			try { trSpeakPlayer?.Stop(); } catch { }
			lbtrstatus.Text = Loc.T("dict.speaking", TrLang.Label(code));
			trSpeakPlayer ??= new TtsPlayer();
			var pref = DictTts.For(opt, code);
			var path = await DictTts.EnsureWav(pref, code, text, 1200).ConfigureAwait(true);
			if (gen != trSpeakGen) return;
			trSpeakPlayer.PlayFile(path);
		}
		catch (Exception ex) {
			if (gen != trSpeakGen) return;
			CaptureLog.Ex("tr.speak", ex);
			lbtrstatus.Text = ex.Message == "novoice"
				? Loc.T("dict.novoice", TrLang.Label(code))
				: Loc.T("tr.popup.fail", ex.Message);
		}
	}

	static string speakcode(string code) {
		code = TrLang.Normalize(code);
		if (code == "cht" || code == "yue") return TrLang.Zh;
		return code;
	}

	void applytrlang() {
		lbtrbrand.Text = Loc.T("tab.translate");
		if (string.IsNullOrWhiteSpace(lbtrstatus.Text) || lbtrstatus.Text == "就绪" || lbtrstatus.Text == Loc.T("ready"))
			lbtrstatus.Text = Loc.T("ready");
		lbtrsrc.Text = Loc.T("tr.src");
		etrsrclng.ToolTip = Loc.T("tr.src.tip");
		lbtrdst.Text = Loc.T("tr.dst");
		etrdstlng.ToolTip = Loc.T("tr.dst.tip");
		lbtrengine.Text = Loc.T("tts.engine");
		etrengine.ToolTip = Loc.T("tr.engine.tip");
		btrreload.Content = Loc.T("reload.models");
		btrreload.ToolTip = Loc.T("tr.reload.tip");
		lbtrmodel.Text = Loc.T("tr.model");
		etrmodel.ToolTip = Loc.T("tr.model.tip");
		lbtrcompute.Text = Loc.T("tr.compute");
		etrcompute.ToolTip = Loc.T("tr.compute.tip");
		lbtrhint.Text = Loc.T("tr.hint");
		rntrchromepage.Text = Loc.T("tr.chrome.install");
		lktrchromepage.ToolTip = Loc.T("tr.chrome.install.tip");
		lbtrsrctext.Text = Loc.T("tr.src.text");
		btrspeaksrc.ToolTip = Loc.T("tr.speak.src");
		lbtrdsttext.Text = Loc.T("tr.dst.text");
		btrspeakdst.ToolTip = Loc.T("tr.speak.dst");
		btrgo.Content = Loc.T("tr.go");
		btrgo.ToolTip = Loc.T("tr.go.tip");
		btrpingpong.Content = Loc.T("tr.pingpong");
		btrpingpong.ToolTip = Loc.T("tr.pingpong.tip");
		btrswap.Content = Loc.T("tr.swap");
		btrswap.ToolTip = Loc.T("tr.swap.tip");
		btrstop.Content = Loc.T("cancel");
		applycomputebox(etrcompute);
		foreach (var o in etrengine.Items) {
			if (o is ComboBoxItem it && it.Tag is string s && s.Length == 0)
				it.Content = Loc.T("tr.engine.onnx");
			else if (o is ComboBoxItem it2 && it2.Tag is string s2 && s2 == "chrome")
				it2.Content = Loc.T("tr.engine.chrome");
		}
		var keep = trUiLoading;
		trUiLoading = true;
		try { filltrlangcombos(); }
		finally { trUiLoading = keep; }
		synctrengineui();
	}
}

