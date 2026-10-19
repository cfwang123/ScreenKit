using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenKit;

/// <summary>浮窗翻译：热键呼出/隐藏，不依附主窗。</summary>
public partial class TranslatePopupWindow : Window {
	readonly Func<OcrOptions> getopt;
	readonly TranslateEngine engine;
	readonly Action onengine;
	List<TranslateModelInfo> models = new();
	bool uiLoading;
	bool busy;
	bool forceClose;
	int showgen;
	int speakgen;
	TtsPlayer player;
	CancellationTokenSource cts;

	internal TranslatePopupWindow(Func<OcrOptions> getOpt, TranslateEngine trEngine, Action onEngine = null) {
		getopt = getOpt ?? (() => new OcrOptions());
		engine = trEngine;
		onengine = onEngine;
		InitializeComponent();
		WindowEsc.Attach(this, Hide);
		Closing += (_, e) => {
			if (forceClose) return;
			e.Cancel = true;
			Hide();
		};
		IsVisibleChanged += (_, _) => {
			if (IsVisible) return;
			try { cts?.Cancel(); } catch { }
			speakgen++;
			try { player?.Stop(); } catch { }
		};
		bpaste.Click += (_, _) => pasteclip();
		bcopy.Click += (_, _) => copyout();
		bgo.Click += (_, _) => _ = go();
		bswap.Click += (_, _) => swaplang();
		bspeaksrc.Click += (_, _) => _ = speakside(true);
		bspeakdst.Click += (_, _) => _ = speakside(false);
		esrclng.SelectionChanged += (_, _) => {
			if (uiLoading) return;
			filldst(preserve: true);
			syncautodetect();
		};
		edstlng.SelectionChanged += (_, _) => {
			if (uiLoading) return;
			syncautodetect();
		};
		esrc.TextChanged += (_, _) => syncautodetect();
		etrengine.SelectionChanged += (_, _) => {
			if (uiLoading) return;
			applyenginechoice(save: true);
			var keepSrc = selected(esrclng);
			var keepDst = selected(edstlng);
			fillsrc();
			if (!pick(esrclng, keepSrc) && esrclng.Items.Count > 0)
				esrclng.SelectedIndex = 0;
			filldst(preserve: false);
			if (!pick(edstlng, keepDst) && edstlng.Items.Count > 0)
				edstlng.SelectedIndex = 0;
			try { onengine?.Invoke(); } catch { }
		};
		esrc.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
				e.Handled = true;
				_ = go();
			}
		};
		applylang();
		Reload();
	}

	public void ForceClose() {
		forceClose = true;
		speakgen++;
		try { player?.Stop(); } catch { }
		try { player?.Dispose(); } catch { }
		player = null;
		try { Close(); } catch { }
	}

	/// <summary>热键/菜单呼出：显示；不改原文/译文（首次为空，之后保留上次）。</summary>
	public void ShowFromHotkey() {
		if (!IsVisible) Show();
		if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
		Activate();
		try { esrc.Focus(); } catch { }
	}

	/// <summary>词典选区：打开小窗、填入原文并立即翻译。</summary>
	public void ShowText(string text) {
		ShowFromHotkey();
		esrc.Text = text ?? "";
		edst.Text = "";
		try {
			esrc.Focus();
			esrc.SelectAll();
		}
		catch { }
		var gen = ++showgen;
		_ = showandgo(gen);
	}

	async Task showandgo(int gen) {
		if (busy) {
			try { cts?.Cancel(); } catch { }
			var t0 = Environment.TickCount;
			while (busy && gen == showgen && Environment.TickCount - t0 < 3000)
				await Task.Delay(20).ConfigureAwait(true);
		}
		if (gen != showgen) return;
		await go();
	}

	public void Reload() {
		try { models = TranslateModelScanner.Scan(); }
		catch { models = new List<TranslateModelInfo>(); }
		SyncEngine();
		applylang();
	}

	/// <summary>按已保存的翻译引擎重填下拉，不写配置。</summary>
	public void SyncEngine() {
		var prevSrc = selected(esrclng);
		var prevDst = selected(edstlng);
		fillengine();
		fillsrc();
		if (!pick(esrclng, prevSrc) && esrclng.Items.Count > 0)
			esrclng.SelectedIndex = 0;
		filldst(preserve: false);
		if (!pick(edstlng, prevDst) && edstlng.Items.Count > 0)
			edstlng.SelectedIndex = 0;
		syncautodetect();
	}

	public void ApplyLang() => applylang();

	void applylang() {
		Title = Loc.T("tr.popup.title");
		lbsrc.Text = Loc.T("tr.src.text");
		lbout.Text = Loc.T("tr.dst.text");
		bpaste.Content = Loc.T("tr.paste");
		bpaste.ToolTip = Loc.T("tr.paste.tip");
		bcopy.Content = Loc.T("tr.copy");
		bgo.Content = Loc.T("tr.go");
		bgo.ToolTip = Loc.T("tr.go.tip");
		bswap.ToolTip = Loc.T("tr.swap.tip");
		bspeaksrc.Content = Loc.T("dict.speak");
		bspeakdst.Content = Loc.T("dict.speak");
		bspeaksrc.ToolTip = Loc.T("tr.popup.speak.tip");
		bspeakdst.ToolTip = Loc.T("tr.popup.speak.tip");
		esrclng.ToolTip = Loc.T("tr.src.tip");
		edstlng.ToolTip = Loc.T("tr.dst.tip");
		etrengine.ToolTip = Loc.T("tr.engine.tip");
		foreach (var o in etrengine.Items) {
			if (o is ComboBoxItem it && it.Tag is string s && s.Length == 0)
				it.Content = Loc.T("tr.engine.onnx");
			else if (o is ComboBoxItem it2 && it2.Tag is string s2 && s2 == "chrome")
				it2.Content = Loc.T("tr.engine.chrome");
		}
		if (string.IsNullOrWhiteSpace(lbstatus.Text)
			|| lbstatus.Text == Loc.T("tr.popup.hint")
			|| lbstatus.Text == "Ctrl+Enter 翻译 · Esc 隐藏"
			|| lbstatus.Text == "Ctrl+Enter translate · Esc hide")
			lbstatus.Text = Loc.T("tr.popup.hint");
		var keepSrc = selected(esrclng);
		var keepDst = selected(edstlng);
		fillsrc();
		pick(esrclng, keepSrc);
		filldst(preserve: false);
		pick(edstlng, keepDst);
		syncautodetect();
	}

	/// <summary>原文/译文旁显示实际语言；「自动」时另显示检测方向。</summary>
	void syncautodetect() {
		var auto = selected(esrclng) == TrLang.Auto || selected(edstlng) == TrLang.Auto;
		var ok = resolvepair(out var src, out var dst);
		if (lbsrclang != null)
			lbsrclang.Text = ok ? TrLang.Label(src) : "";
		if (lbdstlang != null)
			lbdstlang.Text = ok ? TrLang.Label(dst) : "";
		if (lbdetect == null) return;
		if (!auto) {
			lbdetect.Visibility = Visibility.Collapsed;
			return;
		}
		lbdetect.Text = Loc.T("tr.auto.detect", TrLang.Label(src), TrLang.Label(dst));
		lbdetect.Visibility = Visibility.Visible;
	}

	bool usechrome() =>
		etrengine?.SelectedItem is ComboBoxItem it && it.Tag is string s
		&& s.Equals("chrome", StringComparison.OrdinalIgnoreCase);

	bool usellm() =>
		etrengine?.SelectedItem is ComboBoxItem it && it.Tag is LlmEndpoint;

	LlmEndpoint currentllm() =>
		etrengine?.SelectedItem is ComboBoxItem it ? it.Tag as LlmEndpoint : null;

	void fillengine() {
		uiLoading = true;
		try {
			var want = (getopt()?.TranslateLlm ?? "").Trim();
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
			ComboBoxItem pickItem = null;
			if (want.Equals("chrome", StringComparison.OrdinalIgnoreCase))
				pickItem = chrome;
			var list = getopt()?.LlmList;
			if (list != null) {
				foreach (var ep in list) {
					if (ep == null) continue;
					var name = ep.DisplayName;
					if (name.Length == 0) continue;
					var it = new ComboBoxItem {
						Content = name,
						Tag = ep,
						ToolTip = string.IsNullOrWhiteSpace(ep.Model) ? name : ep.Model,
					};
					etrengine.Items.Add(it);
					if (pickItem == null &&
						(string.Equals(name, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(ep.Model ?? "", want, StringComparison.OrdinalIgnoreCase)))
						pickItem = it;
				}
			}
			etrengine.SelectedItem = pickItem ?? etrengine.Items[0];
		}
		finally { uiLoading = false; }
	}

	void applyenginechoice(bool save) {
		var o = getopt();
		if (o == null) return;
		var ep = currentllm();
		var next = usechrome() ? "chrome" : (ep != null ? ep.DisplayName : "");
		if (string.Equals((o.TranslateLlm ?? "").Trim(), next, StringComparison.Ordinal)) return;
		o.TranslateLlm = next;
		if (!save) return;
		try { AppConfig.Save(o); }
		catch (Exception ex) { CaptureLog.Ex("tr.popup.engine", ex); }
	}

	void fillsrc() {
		uiLoading = true;
		try {
			var o = getopt() ?? new OcrOptions();
			esrclng.Items.Clear();
			esrclng.Items.Add(new ComboBoxItem {
				Content = TrLang.AutoLabel(o.TranslateAutoNative, o.TranslateAutoLearn),
				Tag = TrLang.Auto,
			});
			var srcs = new List<string>();
			if (!usechrome()) {
				srcs = models.Where(m => m.IsReady)
					.Select(m => m.SourceLang)
					.Where(s => !string.IsNullOrEmpty(s))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();
			}
			if (usellm()) {
				foreach (var s in TrLang.LlmCodes)
					if (!srcs.Any(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)))
						srcs.Add(s);
			}
			else if (usechrome())
				ChromePack.AddInstalledTrLangs(srcs);
			srcs.Sort(TrLang.CompareLlm);
			foreach (var s in srcs)
				esrclng.Items.Add(new ComboBoxItem { Content = TrLang.Label(s), Tag = s });
		}
		finally { uiLoading = false; }
	}

	void filldst(bool preserve) {
		var prev = preserve ? selected(edstlng) : "";
		var src = selected(esrclng);
		uiLoading = true;
		try {
			edstlng.Items.Clear();
			if (src == TrLang.Auto || string.IsNullOrEmpty(src)) {
				var o = getopt() ?? new OcrOptions();
				edstlng.Items.Add(new ComboBoxItem {
					Content = TrLang.AutoLabel(o.TranslateAutoNative, o.TranslateAutoLearn),
					Tag = TrLang.Auto,
				});
				edstlng.SelectedIndex = 0;
				return;
			}
			var tgts = new List<string>();
			if (!usechrome()) {
				tgts = models.Where(m => m.IsReady
						&& string.Equals(m.SourceLang, src, StringComparison.OrdinalIgnoreCase))
					.Select(m => m.TargetLang)
					.Where(t => !string.IsNullOrEmpty(t))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();
			}
			if (usellm()) {
				foreach (var t in TrLang.LlmCodes) {
					if (string.Equals(t, src, StringComparison.OrdinalIgnoreCase)) continue;
					if (!tgts.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
						tgts.Add(t);
				}
			}
			else if (usechrome())
				ChromePack.AddInstalledTrTargets(tgts, src);
			tgts.Sort(TrLang.CompareLlm);
			foreach (var t in tgts)
				edstlng.Items.Add(new ComboBoxItem { Content = TrLang.Label(t), Tag = t });
			if (edstlng.Items.Count == 0) {
				edstlng.Items.Add(new ComboBoxItem {
					Content = Loc.T("lang.none.tgt"),
					Tag = "",
					IsEnabled = false,
				});
				edstlng.SelectedIndex = 0;
				return;
			}
			if (!pick(edstlng, prev))
				edstlng.SelectedIndex = 0;
		}
		finally { uiLoading = false; }
	}

	static string selected(ComboBox box) {
		if (box?.SelectedItem is ComboBoxItem it && it.Tag is string s)
			return TrLang.Normalize(s);
		return "";
	}

	static bool pick(ComboBox box, string code) {
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

	void swaplang() {
		var src = selected(esrclng);
		var dst = selected(edstlng);
		if (src == TrLang.Auto || dst == TrLang.Auto) {
			var o = getopt() ?? new OcrOptions();
			o.AutoTranslatePair(out var nat, out var lrn);
			var probe = esrc?.Text ?? "";
			if (!string.IsNullOrWhiteSpace(edst.Text))
				probe = edst.Text;
			LangDetect.DetectPair(probe, nat, lrn, out var a, out var b);
			pick(esrclng, b);
			filldst(preserve: false);
			pick(edstlng, a);
		}
		else {
			pick(esrclng, dst);
			filldst(preserve: false);
			pick(edstlng, src);
		}
		if (!string.IsNullOrWhiteSpace(edst.Text)) {
			var t = esrc.Text ?? "";
			esrc.Text = edst.Text;
			edst.Text = t;
		}
	}

	async Task speakside(bool src) {
		var box = src ? esrc : edst;
		var text = box?.Text ?? "";
		if (string.IsNullOrWhiteSpace(text)) {
			lbstatus.Text = src ? Loc.T("tr.popup.need_src") : Loc.T("tr.dst.empty");
			return;
		}
		var code = src ? selected(esrclng) : selected(edstlng);
		if (code == TrLang.Auto || string.IsNullOrEmpty(code))
			code = LangDetect.DetectCode(text);
		code = speakcode(code);
		var gen = ++speakgen;
		try {
			player ??= new TtsPlayer();
			var pref = DictTts.For(getopt(), code);
			lbstatus.Text = Loc.T("dict.speaking", TrLang.Label(code));
			var path = await DictTts.EnsureWav(pref, code, text, 1200).ConfigureAwait(true);
			if (gen != speakgen || !IsVisible) return;
			player.PlayFile(path);
		}
		catch (Exception ex) {
			if (gen != speakgen) return;
			CaptureLog.Ex("tr.popup.speak", ex);
			lbstatus.Text = ex.Message == "novoice"
				? Loc.T("dict.novoice", TrLang.Label(code))
				: Loc.T("tr.popup.fail", ex.Message);
		}
	}

	static string speakcode(string code) {
		code = TrLang.Normalize(code);
		if (code == "cht" || code == "yue") return TrLang.Zh;
		return code;
	}

	void pasteclip() {
		string clip = "";
		try {
			if (Clipboard.ContainsText())
				clip = Clipboard.GetText() ?? "";
		}
		catch (Exception ex) {
			lbstatus.Text = Loc.T("tr.paste.fail", ex.Message);
			return;
		}
		clip = (clip ?? "").Trim();
		if (clip.Length == 0) {
			lbstatus.Text = Loc.T("tr.popup.clip_empty");
			return;
		}
		esrc.Text = clip;
		lbstatus.Text = Loc.T("pasted");
	}

	void copyout() {
		try {
			var t = edst.Text ?? "";
			if (t.Length == 0) {
				lbstatus.Text = Loc.T("tr.dst.empty");
				return;
			}
			Clipboard.SetText(t);
			lbstatus.Text = Loc.T("copied");
		}
		catch (Exception ex) {
			lbstatus.Text = Loc.T("pp.copy.fail", ex.Message);
		}
	}

	bool resolvepair(out string src, out string dst) {
		src = selected(esrclng);
		dst = selected(edstlng);
		if (src == TrLang.Auto || dst == TrLang.Auto || string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) {
			var o = getopt() ?? new OcrOptions();
			o.AutoTranslatePair(out var nat, out var lrn);
			LangDetect.DetectPair(esrc?.Text, nat, lrn, out src, out dst);
		}
		return src.Length > 0 && dst.Length > 0;
	}

	async Task go() {
		if (busy) return;
		var textIn = esrc?.Text ?? "";
		if (string.IsNullOrWhiteSpace(textIn)) {
			lbstatus.Text = Loc.T("tr.popup.need_src");
			return;
		}
		if (textIn.Length > 8000) {
			lbstatus.Text = Loc.T("tr.popup.too_long");
			return;
		}
		if (!resolvepair(out var src, out var dst)) {
			lbstatus.Text = Loc.T("tr.popup.need_lang");
			return;
		}
		var o = getopt() ?? new OcrOptions();
		applyenginechoice(save: false);
		var chrome = usechrome();
		var llm = usellm();
		TranslateModelInfo model = null;
		var key = LangDetect.DirKey(src, dst);
		if (chrome && ChromeTts.FindExe(o.ChromeExe) == null) {
			lbstatus.Text = Loc.T("tr.need.chrome");
			return;
		}
		if (!llm && !chrome) {
			model = models.FirstOrDefault(m => m.IsReady
				&& string.Equals(m.DirKey, key, StringComparison.OrdinalIgnoreCase));
			if (model == null || !model.IsReady) {
				lbstatus.Text = Loc.T("tr.popup.need_model", TrLang.Label(src), TrLang.Label(dst));
				return;
			}
			if (engine == null) {
				lbstatus.Text = Loc.T("tr.popup.need_engine");
				return;
			}
		}

		busy = true;
		bgo.IsEnabled = false;
		cts = new CancellationTokenSource();
		var ct = cts.Token;
		var t0 = Environment.TickCount;
		lbstatus.Text = Loc.T("tr.popup.run", TrLang.Label(src), TrLang.Label(dst));
		try {
			string result;
			if (chrome) {
				var s = src;
				var d = dst;
				var exe = o.ChromeExe;
				var idle = o.ChromeIdleSec;
				result = await Task.Run(() => ChromeTr.Translate(textIn, s, d, exe, idle, ct, part => {
					var line = ChromeTr.DownText(part);
					try { Dispatcher.BeginInvoke(new Action(() => lbstatus.Text = line)); }
					catch { }
				}), ct)
					.ConfigureAwait(true);
			}
			else if (llm) {
				var s = src;
				var d = dst;
				result = await Task.Run(() => AsrLlmClient.Translate(o, textIn, s, d, ct), ct)
					.ConfigureAwait(true);
			}
			else {
				var eng = engine;
				var dir = model.ModelDir;
				var dirKey = key;
				var prefer = TranslateEngine.PreferFromMode(computemode(o));
				result = await Task.Run(() => {
					if (!eng.EnsureLoaded(dirKey, dir, prefer))
						throw new InvalidOperationException(eng.LastError ?? Loc.T("tr.load.fail"));
					ct.ThrowIfCancellationRequested();
					return eng.Translate(dirKey, textIn, ct);
				}, ct).ConfigureAwait(true);
			}
			edst.Text = result ?? "";
			var ms = Math.Max(0, Environment.TickCount - t0);
			lbstatus.Text = Loc.T("tr.popup.ok", TrLang.Label(src), TrLang.Label(dst),
				(ms / 1000.0).ToString("0.00"));
		}
		catch (OperationCanceledException) {
			lbstatus.Text = Loc.T("cancelling");
		}
		catch (Exception ex) {
			CaptureLog.Ex("tr.popup", ex);
			lbstatus.Text = Loc.T("tr.popup.fail", ex.Message);
		}
		finally {
			busy = false;
			bgo.IsEnabled = true;
			try { cts?.Dispose(); } catch { }
			cts = null;
		}
	}

	static TtsComputeMode computemode(OcrOptions o) =>
		(o?.TranslateCompute ?? "Auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
}
