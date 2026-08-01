using System.Windows;
using System.Windows.Controls;

namespace WpfOCR;

/// <summary>MainWindow：翻译 Tab（源语言→目标语言 + Opus-MT Python 管道）。</summary>
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
			Content = "自动（CUDA→核显→CPU）",
			Tag = TtsComputeMode.Auto,
			ToolTip = "进程内 ONNX Runtime，与 OCR 共用 onnxgpu64 / onnxdml64",
		});
		etrcompute.Items.Add(new ComboBoxItem {
			Content = "GPU（NVIDIA CUDA）",
			Tag = TtsComputeMode.Gpu,
			ToolTip = "ONNX Runtime CUDA（onnxgpu64），与 OCR 相同",
		});
		etrcompute.Items.Add(new ComboBoxItem {
			Content = "核显（DirectML）",
			Tag = TtsComputeMode.Igpu,
			ToolTip = "ONNX Runtime DirectML（onnxdml64）",
		});
		etrcompute.Items.Add(new ComboBoxItem { Content = "CPU", Tag = TtsComputeMode.Cpu });
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
		etrcompute.SelectionChanged += (_, _) => {
			if (trUiLoading) return;
			if (etrcompute.SelectedItem is ComboBoxItem it && it.Tag is TtsComputeMode) {
				lbtrstatus.Text = "计算设备 → " + it.Content + "（下次翻译时加载；进程内 ONNX）";
				try { trEngine?.UnloadAll(); } catch { }
				savetrprefs();
			}
		};
		btrreload.Click += (_, _) => {
			scantrmodels();
			filltrlangcombos();
			lbtrstatus.Text = "已刷新模型列表";
		};
		btrgo.Click += async (_, _) => await runtranslate();
		btrpingpong.Click += async (_, _) => await runpingpong();
		btrstop.Click += (_, _) => {
			try { trCts?.Cancel(); } catch { }
			lbtrstatus.Text = "正在取消…";
		};
		btrclear.Click += (_, _) => {
			etrsrc.Text = "";
			etrdst.Text = "";
			lbtrstatus.Text = "已清空";
		};
		btrpaste.Click += (_, _) => {
			try {
				if (Clipboard.ContainsText())
					etrsrc.Text = Clipboard.GetText() ?? "";
				lbtrstatus.Text = "已粘贴";
			}
			catch (Exception ex) { lbtrstatus.Text = "粘贴失败: " + ex.Message; }
		};
		btrfromocr.Click += (_, _) => {
			try {
				var tb = FindName("eresult") as TextBox;
				if (tb != null && !string.IsNullOrWhiteSpace(tb.Text)) {
					etrsrc.Text = tb.Text;
					lbtrstatus.Text = "已填入 OCR 结果";
				}
				else
					lbtrstatus.Text = "OCR 结果为空";
			}
			catch (Exception ex) { lbtrstatus.Text = "读取 OCR 失败: " + ex.Message; }
		};
		btrcopy.Click += (_, _) => {
			try {
				var t = etrdst.Text ?? "";
				if (t.Length == 0) {
					lbtrstatus.Text = "译文为空";
					return;
				}
				Clipboard.SetText(t);
				lbtrstatus.Text = "已复制译文";
			}
			catch (Exception ex) { lbtrstatus.Text = "复制失败: " + ex.Message; }
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
			lbtrstatus.Text = "已交换语言";
		};

		scantrmodels();
		filltrlangcombos();
	}

	void scantrmodels() {
		try {
			trModels = TranslateModelScanner.Scan();
			var root = TranslateModelScanner.ResolveRoot();
			var ready = trModels.Count(m => m.IsReady);
			lbtrhint.Text = ready > 0
				? $"模型：{root} · 可用 {ready} 对 · 进程内 ONNX（CUDA/DML 同 OCR）"
				: $"未找到 ONNX 模型 → {TranslateModelScanner.ModelsRoot()}（需 opus-mt-*-onnx）";
		}
		catch (Exception ex) {
			trModels = new List<TranslateModelInfo>();
			lbtrhint.Text = "扫描失败: " + ex.Message;
		}
	}

	void filltrlangcombos() {
		trUiLoading = true;
		try {
			fillsrclangcombo();
			filldstlangcombo(preserve: false);
			syncmodelcombo();
			if (trModels.Count == 0 || !trModels.Any(m => m.IsReady))
				lbtrstatus.Text = "无翻译模型 · 见 翻译功能TODO.md";
			else
				lbtrstatus.Text = $"就绪 · {trModels.Count(m => m.IsReady)} 对模型";
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
			Content = "自动（中英互译）",
			Tag = TrLang.Auto,
			ToolTip = "按汉字占比判定中/英，目标为另一种语言",
		});
		var srcs = trModels.Where(m => m.IsReady)
			.Select(m => m.SourceLang)
			.Where(s => !string.IsNullOrEmpty(s))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(s => TrLang.Label(s), StringComparer.OrdinalIgnoreCase)
			.ToList();
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
				Content = "自动（中英互译）",
				Tag = TrLang.Auto,
				ToolTip = "与源语言「自动」配对，仅中英",
			});
			etrdstlng.SelectedIndex = 0;
			return;
		}

		var tgts = trModels.Where(m => m.IsReady
				&& string.Equals(m.SourceLang, src, StringComparison.OrdinalIgnoreCase))
			.Select(m => m.TargetLang)
			.Where(t => !string.IsNullOrEmpty(t))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(t => TrLang.Label(t), StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var t in tgts) {
			etrdstlng.Items.Add(new ComboBoxItem {
				Content = TrLang.Label(t),
				Tag = t,
			});
		}
		if (etrdstlng.Items.Count == 0) {
			etrdstlng.Items.Add(new ComboBoxItem {
				Content = "（无可用目标）",
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
			lbtrstatus.Text = "请输入原文";
			return false;
		}
		if (textIn.Length > 8000) {
			lbtrstatus.Text = "原文过长（建议 ≤8000 字），请分段";
			return false;
		}

		resolvepair(out src, out dst, forPick: false);
		var uiSrc = selectedlang(etrsrclng);
		var uiDst = selectedlang(etrdstlng);
		if (uiSrc == TrLang.Auto || uiDst == TrLang.Auto) {
			if (!((src == TrLang.Zh && dst == TrLang.En) || (src == TrLang.En && dst == TrLang.Zh))) {
				lbtrstatus.Text = "自动模式仅支持中英互译";
				return false;
			}
		}

		key = LangDetect.DirKey(src, dst);
		var dirKey = key;
		model = etrmodel.SelectedItem as TranslateModelInfo;
		if (model == null || !model.IsReady
			|| !string.Equals(model.DirKey, dirKey, StringComparison.OrdinalIgnoreCase)) {
			model = trModels.FirstOrDefault(m => m.IsReady
				&& string.Equals(m.DirKey, dirKey, StringComparison.OrdinalIgnoreCase));
		}
		if (model == null || !model.IsReady) {
			lbtrstatus.Text = $"缺少 {TrLang.Label(src)}→{TrLang.Label(dst)} 模型（{dirKey}）";
			return false;
		}
		if (trEngine == null) {
			lbtrstatus.Text = "翻译引擎不可用";
			return false;
		}
		return true;
	}

	async Task runtranslate() {
		if (trBusy) return;
		if (!tryresolveforward(out var src, out var dst, out var model, out var key)) {
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
		lbtrstatus.Text = $"加载 / 翻译中…（{TrLang.Label(src)}→{TrLang.Label(dst)} · {prefer}）";

		try {
			var engine = trEngine;
			var modelDir = model.ModelDir;
			var text = textIn;
			var dirKey = key;
			var result = await Task.Run(() => {
				if (!engine.EnsureLoaded(dirKey, modelDir, prefer))
					throw new InvalidOperationException(engine.LastError ?? "加载失败");
				ct.ThrowIfCancellationRequested();
				return engine.Translate(dirKey, text, ct);
			}, ct).ConfigureAwait(true);

			etrdst.Text = result ?? "";
			var dev = string.IsNullOrEmpty(trEngine.LastDevice) ? prefer : trEngine.LastDevice;
			var be = string.IsNullOrEmpty(trEngine.LastBackend) ? "" : trEngine.LastBackend + "/";
			lbtrstatus.Text = $"完成 · {TrLang.Label(src)}→{TrLang.Label(dst)} · {be}{dev} · {sw.ElapsedMilliseconds} ms"
				+ (string.IsNullOrWhiteSpace(result) ? " · （空译文）" : "");
		}
		catch (OperationCanceledException) {
			lbtrstatus.Text = "已取消";
		}
		catch (Exception ex) {
			CaptureLog.Ex("translate", ex);
			var msg = ex.Message ?? "";
			if (prefer == "cuda" || Compat.Contains(msg, "CUDA", StringComparison.OrdinalIgnoreCase))
				msg += " · 提示：翻译 ONNX 与 OCR 共用 onnxgpu64，请确认 GPU 包/驱动可用";
			lbtrstatus.Text = "失败: " + msg;
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

		var revKey = LangDetect.DirKey(dst, src);
		var reverse = trModels.FirstOrDefault(m => m.IsReady
			&& string.Equals(m.DirKey, revKey, StringComparison.OrdinalIgnoreCase));
		if (reverse == null || !reverse.IsReady) {
			lbtrstatus.Text = $"来回翻译需要双向模型：已有 {key}，缺少 {revKey}";
			return;
		}

		var textIn = (etrsrc?.Text ?? "").Trim();
		var compute = trcurcompute();
		var prefer = TranslateEngine.PreferFromMode(compute);
		savetrprefs();

		trBusy = true;
		btrgo.IsEnabled = false;
		btrpingpong.IsEnabled = false;
		btrstop.IsEnabled = false;
		lbtrstatus.Text = $"来回翻译 {TranslatePingPongWindow.DefaultRounds} 次…";

		try {
			var dlg = new TranslatePingPongWindow(
				trEngine, forward, reverse, textIn, prefer, TranslatePingPongWindow.DefaultRounds);
			attachdialogowner(dlg);
			// 非模态也可用；ShowDialog 阻塞直到关闭，过程在窗内跑
			dlg.ShowDialog();
			if (!string.IsNullOrEmpty(dlg.FinalText)) {
				etrdst.Text = dlg.FinalText;
				lbtrstatus.Text = dlg.Completed
					? $"来回翻译完成 · 已写入译文区 · {TranslatePingPongWindow.DefaultRounds} 次往返"
					: "来回翻译已结束（未完整跑完）· 已写入当前结果";
			}
			else
				lbtrstatus.Text = "来回翻译已关闭";
		}
		catch (Exception ex) {
			CaptureLog.Ex("runpingpong", ex);
			lbtrstatus.Text = "来回翻译失败: " + ex.Message;
		}
		finally {
			trBusy = false;
			btrgo.IsEnabled = true;
			btrpingpong.IsEnabled = true;
			btrstop.IsEnabled = false;
		}
		await Task.CompletedTask;
	}
}

