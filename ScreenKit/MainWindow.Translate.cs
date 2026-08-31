using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

/// <summary>MainWindow：翻译 Tab（本地 ONNX 或 LLM）。</summary>
public partial class MainWindow {
	TranslateEngine trEngine;
	List<TranslateModelInfo> trModels = new();
	bool trUiLoading;
	CancellationTokenSource trCts;
	bool trBusy;

	void inittranslate() {
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
		etrengine.SelectionChanged += (_, _) => {
			if (trUiLoading) return;
			synctrengineui();
			filltrlangcombos();
			savetrprefs();
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
		btrgo.Click += async (_, _) => await runtranslate();
		btrpingpong.Click += async (_, _) => await runpingpong();
		btrstop.Click += (_, _) => {
			try { trCts?.Cancel(); } catch { }
			lbtrstatus.Text = Loc.T("cancelling");
		};
		btrclear.Click += (_, _) => {
			etrsrc.Text = "";
			etrdst.Text = "";
			lbtrstatus.Text = Loc.T("cleared");
		};
		btrpaste.Click += (_, _) => {
			try {
				if (Clipboard.ContainsText())
					etrsrc.Text = Clipboard.GetText() ?? "";
				lbtrstatus.Text = Loc.T("pasted");
			}
			catch (Exception ex) { lbtrstatus.Text = Loc.T("tr.paste.fail", ex.Message); }
		};
		btrfromocr.Click += (_, _) => {
			try {
				var tb = FindName("eresult") as TextBox;
				if (tb != null && !string.IsNullOrWhiteSpace(tb.Text)) {
					etrsrc.Text = tb.Text;
					lbtrstatus.Text = Loc.T("tr.ocr.filled");
				}
				else
					lbtrstatus.Text = Loc.T("tr.ocr.empty");
			}
			catch (Exception ex) { lbtrstatus.Text = Loc.T("tr.ocr.fail", ex.Message); }
		};
		btrcopy.Click += (_, _) => {
			try {
				var t = etrdst.Text ?? "";
				if (t.Length == 0) {
					lbtrstatus.Text = Loc.T("tr.dst.empty");
					return;
				}
				Clipboard.SetText(t);
				lbtrstatus.Text = Loc.T("copied");
			}
			catch (Exception ex) { lbtrstatus.Text = Loc.T("pp.copy.fail", ex.Message); }
		};
		btrswap.Click += (_, _) => {
			// 交换源/目标语言（自动保持自动）；有译文则对调文本
			var src = selectedlang(etrsrclng);
			var dst = selectedlang(etrdstlng);
			if (src == TrLang.Auto || dst == TrLang.Auto) {
				// 自动：按当前原文检测后显式设为反向
				LangDetect.DetectZhEnPair(etrsrc?.Text, out var a, out var b);
				// 交换：原文若已是译文区则用译文检测
				if (!string.IsNullOrWhiteSpace(etrdst.Text))
					LangDetect.DetectZhEnPair(etrdst.Text, out a, out b);
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
		trPopup = new TranslatePopupWindow(() => opt, trEngine);
		trPopup.Closed += (_, _) => trPopup = null;
	}

	void showtranslatepopup() {
		ensuretranslatepopup();
		trPopup.ShowFromHotkey();
	}

	void toggletranslatepopup() {
		ensuretranslatepopup();
		if (trPopup.IsVisible && trPopup.IsActive) {
			trPopup.Hide();
			return;
		}
		if (trPopup.IsVisible) {
			trPopup.Activate();
			return;
		}
		trPopup.ShowFromHotkey();
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
			ComboBoxItem pick = null;
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

	bool usellm() =>
		etrengine?.SelectedItem is ComboBoxItem it && it.Tag is LlmEndpoint;

	LlmEndpoint currenttrllm() =>
		etrengine?.SelectedItem is ComboBoxItem it ? it.Tag as LlmEndpoint : null;

	void synctrengineui() {
		var llm = usellm();
		etrcompute.IsEnabled = !llm;
		etrmodel.IsEnabled = !llm;
		etrcompute.Opacity = llm ? 0.45 : 1;
		etrmodel.Opacity = llm ? 0.45 : 1;
		if (llm) {
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
			if (usellm()) {
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
		// 自动：仅中英互译
		etrsrclng.Items.Add(new ComboBoxItem {
			Content = Loc.T("lang.auto.zhen"),
			Tag = TrLang.Auto,
			ToolTip = Loc.T("tr.src.tip"),
		});
		var srcs = trModels.Where(m => m.IsReady)
			.Select(m => m.SourceLang)
			.Where(s => !string.IsNullOrEmpty(s))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (usellm()) {
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
				Content = Loc.T("lang.auto.zhen"),
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
		if (usellm()) {
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
			resolvepair(out var src, out var dst, forPick: true);
			List<TranslateModelInfo> match;
			if (src == TrLang.Auto || dst == TrLang.Auto) {
				// 自动：展示中英双向模型
				match = trModels.Where(m => m.IsReady && iszhenpair(m)).ToList();
			}
			else {
				var key = LangDetect.DirKey(src, dst);
				match = trModels.Where(m => m.IsReady
					&& string.Equals(m.DirKey, key, StringComparison.OrdinalIgnoreCase)).ToList();
				if (match.Count == 0)
					match = trModels.Where(m => m.IsReady
						&& string.Equals(m.SourceLang, src, StringComparison.OrdinalIgnoreCase)
						&& string.Equals(m.TargetLang, dst, StringComparison.OrdinalIgnoreCase)).ToList();
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
	}

	static bool iszhenpair(TranslateModelInfo m) {
		if (m == null) return false;
		var a = m.SourceLang;
		var b = m.TargetLang;
		return (a == TrLang.Zh && b == TrLang.En) || (a == TrLang.En && b == TrLang.Zh);
	}

	/// <summary>解析当前 UI 源/目标；自动时按文本检测中英。</summary>
	void resolvepair(out string src, out string dst, bool forPick) {
		src = selectedlang(etrsrclng);
		dst = selectedlang(etrdstlng);
		if (src == TrLang.Auto || dst == TrLang.Auto || string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) {
			// 自动仅中英
			LangDetect.DetectZhEnPair(forPick ? etrsrc?.Text : etrsrc?.Text, out src, out dst);
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
			opt.TranslateLlm = ep != null ? ep.DisplayName : "";
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

		resolvepair(out src, out dst, forPick: false);
		var uiSrc = selectedlang(etrsrclng);
		var uiDst = selectedlang(etrdstlng);
		if (uiSrc == TrLang.Auto || uiDst == TrLang.Auto) {
			if (!((src == TrLang.Zh && dst == TrLang.En) || (src == TrLang.En && dst == TrLang.Zh))) {
				lbtrstatus.Text = Loc.T("tr.popup.auto_zhen");
				return false;
			}
		}

		key = LangDetect.DirKey(src, dst);
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
			if (usellm()) return;
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
		var ep = currenttrllm();
		lbtrstatus.Text = llm
			? Loc.T("tr.run.llm", TrLang.Label(src), TrLang.Label(dst), ep.DisplayName)
			: Loc.T("tr.run.onnx", TrLang.Label(src), TrLang.Label(dst), prefer);

		try {
			string result;
			if (llm) {
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
			if (llm)
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
			if (!llm && (prefer == "cuda" || Compat.Contains(msg, "CUDA", StringComparison.OrdinalIgnoreCase)))
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
					(t, rev, ct) => AsrLlmClient.Translate(o, t, rev ? d0 : s0, rev ? s0 : d0, ct));
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
			lbtrstatus.Text = dlg.Completed
				? Loc.T("tr.pp.done", TranslatePingPongWindow.DefaultRounds)
				: Loc.T("tr.pp.partial");
		}
		else
			lbtrstatus.Text = Loc.T("tr.pp.closed");
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
		lbtrsrctext.Text = Loc.T("tr.src.text");
		btrpaste.Content = Loc.T("tr.paste");
		btrpaste.ToolTip = Loc.T("tr.paste.tip");
		btrfromocr.Content = Loc.T("tr.fromocr");
		btrfromocr.ToolTip = Loc.T("tr.fromocr.tip");
		btrclear.Content = Loc.T("tr.clear");
		lbtrdsttext.Text = Loc.T("tr.dst.text");
		btrcopy.Content = Loc.T("tr.copy");
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
		}
		var keep = trUiLoading;
		trUiLoading = true;
		try { filltrlangcombos(); }
		finally { trUiLoading = keep; }
		synctrengineui();
	}
}

