using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenKit;

/// <summary>浮窗翻译：热键呼出/隐藏，不依附主窗。</summary>
public partial class TranslatePopupWindow : Window {
	readonly Func<OcrOptions> getopt;
	readonly TranslateEngine engine;
	List<TranslateModelInfo> models = new();
	bool uiLoading;
	bool busy;
	bool forceClose;
	CancellationTokenSource cts;

	internal TranslatePopupWindow(Func<OcrOptions> getOpt, TranslateEngine trEngine) {
		getopt = getOpt ?? (() => new OcrOptions());
		engine = trEngine;
		InitializeComponent();
		WindowEsc.Attach(this, Hide);
		Closing += (_, e) => {
			if (forceClose) return;
			e.Cancel = true;
			Hide();
		};
		IsVisibleChanged += (_, _) => {
			if (!IsVisible)
				try { cts?.Cancel(); } catch { }
		};
		bpaste.Click += (_, _) => pasteclip();
		bcopy.Click += (_, _) => copyout();
		bgo.Click += (_, _) => _ = go();
		bswap.Click += (_, _) => swaplang();
		esrclng.SelectionChanged += (_, _) => {
			if (uiLoading) return;
			filldst(preserve: true);
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
		try { Close(); } catch { }
	}

	/// <summary>热键/菜单呼出：显示；不改原文/译文（首次为空，之后保留上次）。</summary>
	public void ShowFromHotkey() {
		if (!IsVisible) Show();
		if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
		Activate();
		try { esrc.Focus(); } catch { }
	}

	public void Reload() {
		var prevSrc = selected(esrclng);
		var prevDst = selected(edstlng);
		try { models = TranslateModelScanner.Scan(); }
		catch { models = new List<TranslateModelInfo>(); }
		fillsrc();
		if (!pick(esrclng, prevSrc) && esrclng.Items.Count > 0)
			esrclng.SelectedIndex = 0;
		filldst(preserve: false);
		if (!pick(edstlng, prevDst) && edstlng.Items.Count > 0)
			edstlng.SelectedIndex = 0;
		applylang();
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
		esrclng.ToolTip = Loc.T("tr.src.tip");
		edstlng.ToolTip = Loc.T("tr.dst.tip");
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
	}

	bool usellm() {
		var ep = getopt()?.SelectedTranslateLlm();
		return AsrLlmClient.IsEndpointReady(ep);
	}

	void fillsrc() {
		uiLoading = true;
		try {
			esrclng.Items.Clear();
			esrclng.Items.Add(new ComboBoxItem {
				Content = Loc.T("lang.auto.zhen"),
				Tag = TrLang.Auto,
			});
			var srcs = models.Where(m => m.IsReady)
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
				edstlng.Items.Add(new ComboBoxItem {
					Content = Loc.T("lang.auto.zhen"),
					Tag = TrLang.Auto,
				});
				edstlng.SelectedIndex = 0;
				return;
			}
			var tgts = models.Where(m => m.IsReady
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
			LangDetect.DetectZhEnPair(esrc?.Text, out var a, out var b);
			if (!string.IsNullOrWhiteSpace(edst.Text))
				LangDetect.DetectZhEnPair(edst.Text, out a, out b);
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
		if (src == TrLang.Auto || dst == TrLang.Auto || string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst))
			LangDetect.DetectZhEnPair(esrc?.Text, out src, out dst);
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
		var uiSrc = selected(esrclng);
		var uiDst = selected(edstlng);
		if (uiSrc == TrLang.Auto || uiDst == TrLang.Auto) {
			if (!((src == TrLang.Zh && dst == TrLang.En) || (src == TrLang.En && dst == TrLang.Zh))) {
				lbstatus.Text = Loc.T("tr.popup.auto_zhen");
				return;
			}
		}
		var o = getopt() ?? new OcrOptions();
		var llm = usellm();
		var ep = o.SelectedTranslateLlm();
		TranslateModelInfo model = null;
		var key = LangDetect.DirKey(src, dst);
		if (!llm) {
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
			if (llm) {
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
