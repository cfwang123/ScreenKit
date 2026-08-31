using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

/// <summary>OCR 页：目标语言 + 叠字/结果翻译切换。</summary>
public partial class MainWindow {
	/// <summary>与 last.Lines 对齐的译文；未译行为 null。</summary>
	string[] ocrTrOut;
	string ocrTrDst;
	int ocrTrGen;
	int ocrTrMs;
	bool ocrTrUi;
	CancellationTokenSource ocrTrCts;

	void initocrtranslate() {
		fillocrtrdst();
		etrocrdst.SelectionChanged += (_, _) => onocrtrdst();
		btoggletr.Checked += (_, _) => onocrtrtoggle();
		btoggletr.Unchecked += (_, _) => onocrtrtoggle();
		syncocrtrenabled();
	}

	void fillocrtrdst() {
		ocrTrUi = true;
		try {
			var want = normocrtrdst(opt.OcrTranslateLang);
			etrocrdst.Items.Clear();
			etrocrdst.Items.Add(new ComboBoxItem { Content = Loc.T("ocr.tr.dst.none"), Tag = "" });
			foreach (var c in TrLang.LlmCodes)
				etrocrdst.Items.Add(new ComboBoxItem { Content = TrLang.Label(c), Tag = c });
			ComboBoxItem pick = null;
			foreach (ComboBoxItem it in etrocrdst.Items) {
				var tag = it.Tag as string ?? "";
				if (string.Equals(tag, want, StringComparison.OrdinalIgnoreCase)) {
					pick = it;
					break;
				}
			}
			etrocrdst.SelectedItem = pick ?? etrocrdst.Items[0];
		}
		finally { ocrTrUi = false; }
		syncocrtrenabled();
	}

	static string normocrtrdst(string s) {
		s = TrLang.Normalize(s ?? "");
		return TrLang.IsLlm(s) ? s : "";
	}

	string ocrtrdst() {
		if (etrocrdst.SelectedItem is ComboBoxItem it && it.Tag is string t)
			return normocrtrdst(t);
		return "";
	}

	void onocrtrdst() {
		if (ocrTrUi) return;
		var dst = ocrtrdst();
		opt.OcrTranslateLang = dst;
		try { AppConfig.Save(opt); } catch { }
		ocrTrOut = null;
		ocrTrDst = null;
		ocrTrMs = 0;
		syncocrtrenabled();
		if (string.IsNullOrEmpty(dst)) {
			setocrtrchecked(false);
			refreshocrtrview();
			return;
		}
		if (last == null || last.Lines.Count == 0) return;
		setocrtrchecked(true);
		refreshocrtrview();
		_ = runocrtranslateasync(ocrGen, dst);
	}

	void onocrtrtoggle() {
		if (ocrTrUi) return;
		if (btoggletr.IsChecked == true) {
			var dst = ocrtrdst();
			if (string.IsNullOrEmpty(dst)) {
				setocrtrchecked(false);
				return;
			}
			_ = runocrtranslateasync(ocrGen, dst);
			return;
		}
		refreshocrtrview();
	}

	void setocrtrchecked(bool on) {
		ocrTrUi = true;
		try { btoggletr.IsChecked = on; }
		finally { ocrTrUi = false; }
	}

	void syncocrtrenabled() {
		var on = ocrtrdst().Length > 0;
		try { btoggletr.IsEnabled = on; } catch { }
		if (!on) setocrtrchecked(false);
	}

	void cancelocrtranslate() {
		try { ocrTrCts?.Cancel(); } catch { }
		ocrTrCts = null;
		ocrTrOut = null;
		ocrTrDst = null;
		ocrTrGen = 0;
		ocrTrMs = 0;
	}

	/// <summary>叠字/结果用文本：翻译开且有译文则用译文。</summary>
	string overlaytext(int i) {
		if (last == null || i < 0 || i >= last.Lines.Count) return "";
		if (btoggletr.IsChecked == true && ocrTrOut != null && i < ocrTrOut.Length
			&& !string.IsNullOrEmpty(ocrTrOut[i]))
			return ocrTrOut[i];
		return last.Lines[i].Text ?? "";
	}

	string resulttext() {
		if (last == null || last.Lines.Count == 0) return "";
		var n = last.Lines.Count;
		var parts = new string[n];
		for (int i = 0; i < n; i++)
			parts[i] = overlaytext(i);
		return string.Join(Environment.NewLine, parts);
	}

	void applyresulttext() {
		if (last == null) return;
		var t = resulttext();
		if (string.Equals(eresult.Text ?? "", t, StringComparison.Ordinal)) return;
		double off = 0;
		try { off = eresult.VerticalOffset; } catch { }
		lineOff = null;
		lineOffSrc = null;
		eresult.Text = t;
		try { eresult.ScrollToVerticalOffset(off); } catch { }
	}

	void applyocrmeta() {
		if (string.IsNullOrEmpty(ocrMetaBase)) return;
		ocrMetaText = ocrTrMs > 0
			? $"{ocrMetaBase} · 翻译 {(ocrTrMs / 1000.0):0.00}s"
			: ocrMetaBase;
		syncresultmetafromtab();
	}

	void refreshocrtrview() {
		applyresulttext();
		applyocrmeta();
		drawoverlay();
		if (hasselection()) queuesyncresultfromimg();
	}

	/// <summary>OCR 出结果后：已选目标语言则默认打开翻译并替换叠字/结果。</summary>
	Task maybeocrtranslateasync() {
		var dst = ocrtrdst();
		if (string.IsNullOrEmpty(dst) || last == null || last.Lines.Count == 0) {
			ocrTrOut = null;
			ocrTrMs = 0;
			setocrtrchecked(false);
			syncocrtrenabled();
			return Task.CompletedTask;
		}
		syncocrtrenabled();
		setocrtrchecked(true);
		if (btoggletext.IsChecked != true) btoggletext.IsChecked = true;
		return runocrtranslateasync(ocrGen, dst);
	}

	async Task runocrtranslateasync(int gen, string dst) {
		dst = normocrtrdst(dst);
		if (string.IsNullOrEmpty(dst) || last == null) return;
		if (ocrTrOut != null && ocrTrGen == gen && string.Equals(ocrTrDst, dst, StringComparison.Ordinal)
			&& ocrTrOut.Length == last.Lines.Count) {
			refreshocrtrview();
			return;
		}
		var ep = opt.SelectedTranslateLlm() ?? opt.SelectedLlm();
		if (!AsrLlmClient.IsEndpointReady(ep)) {
			setstatus(Loc.T("st.ocr_tr_need_llm"));
			refreshocrtrview();
			return;
		}
		var items = new List<string>(last.Lines.Count);
		foreach (var ln in last.Lines)
			items.Add(ln.Text ?? "");
		var probe = string.Join("", items);
		var src = LangDetect.DetectCode(probe);
		if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) {
			setstatus(Loc.T("st.ocr_tr_same"));
			refreshocrtrview();
			return;
		}
		try { ocrTrCts?.Cancel(); } catch { }
		var cts = new CancellationTokenSource();
		ocrTrCts = cts;
		setstatus(Loc.T("st.ocr_tr_run"));
		List<string> outs;
		var t0 = Environment.TickCount;
		try {
			var o = opt;
			outs = await Task.Run(() => AsrLlmClient.TranslateBatch(o, items, src, dst, 8, ep, cts.Token),
				cts.Token).ConfigureAwait(true);
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (Exception ex) {
			if (gen != ocrGen) return;
			setstatus(Loc.T("st.ocr_tr_fail", ex.Message));
			return;
		}
		if (gen != ocrGen || cts.IsCancellationRequested) return;
		ocrTrOut = outs?.ToArray();
		ocrTrDst = dst;
		ocrTrGen = gen;
		ocrTrMs = Math.Max(0, Environment.TickCount - t0);
		var n = 0;
		if (ocrTrOut != null) {
			foreach (var t in ocrTrOut)
				if (!string.IsNullOrWhiteSpace(t)) n++;
		}
		var sec = (ocrTrMs / 1000.0).ToString("0.00");
		setstatus(Loc.T("st.ocr_tr_ok", n, sec));
		refreshocrtrview();
	}
}
