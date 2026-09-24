using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenKit;

/// <summary>工具 → 密码生成器。</summary>
public partial class PasswordWindow : Window {
	readonly OcrOptions opt;
	readonly ObservableCollection<WordLexRow> lexRows = new();
	readonly ObservableCollection<PasswordVariantRow> varRows = new();
	CancellationTokenSource lexCts;
	CancellationTokenSource varCts;
	bool lexBusy;
	bool varBusy;
	bool lexUiLoading;

	public PasswordWindow(OcrOptions options) {
		opt = options ?? new OcrOptions();
		InitializeComponent();
		lvlex.ItemsSource = lexRows;
		lvvar.ItemsSource = varRows;
		loadui();
		applylang();
		filllexllm();
		initev();
		WindowEsc.Attach(this);
		Closing += (_, _) => {
			try { lexCts?.Cancel(); } catch { }
			try { varCts?.Cancel(); } catch { }
			saveui();
		};
		gen();
	}

	void loadui() {
		elen.Text = Compat.Clamp(opt.PwLen <= 0 ? 16 : opt.PwLen, 4, 128).ToString();
		ecount.Text = Compat.Clamp(opt.PwCount <= 0 ? 5 : opt.PwCount, 1, 50).ToString();
		clower.IsChecked = opt.PwLower;
		cupper.IsChecked = opt.PwUpper;
		cdigit.IsChecked = opt.PwDigit;
		csymbol.IsChecked = opt.PwSymbol;
		cnoamb.IsChecked = opt.PwNoAmbiguous;
		ceach.IsChecked = opt.PwEachClass;
	}

	void saveui() {
		var o = opts();
		opt.PwLen = o.Length;
		opt.PwCount = o.Count;
		opt.PwLower = o.Lower;
		opt.PwUpper = o.Upper;
		opt.PwDigit = o.Digit;
		opt.PwSymbol = o.Symbol;
		opt.PwNoAmbiguous = o.NoAmbiguous;
		opt.PwEachClass = o.EachClass;
		var ep = currentlexllm();
		opt.PwLexLlm = ep != null ? ep.DisplayName : "";
		try { AppConfig.Save(opt); } catch { }
	}

	void initev() {
		bgen.Click += (_, _) => gen();
		bcopy.Click += (_, _) => copy();
		bclose.Click += (_, _) => Close();
		clower.Checked += (_, _) => updatestat();
		clower.Unchecked += (_, _) => updatestat();
		cupper.Checked += (_, _) => updatestat();
		cupper.Unchecked += (_, _) => updatestat();
		cdigit.Checked += (_, _) => updatestat();
		cdigit.Unchecked += (_, _) => updatestat();
		csymbol.Checked += (_, _) => updatestat();
		csymbol.Unchecked += (_, _) => updatestat();
		cnoamb.Checked += (_, _) => updatestat();
		cnoamb.Unchecked += (_, _) => updatestat();
		elen.TextChanged += (_, _) => updatestat();
		elexllm.SelectionChanged += (_, _) => onllmchange(elexllm, evarllm);
		evarllm.SelectionChanged += (_, _) => onllmchange(evarllm, elexllm);
		blex.Click += (_, _) => golex();
		blexcopy.Click += (_, _) => copylex(latinOnly: false);
		blexlat.Click += (_, _) => copylex(latinOnly: true);
		lvlex.MouseDoubleClick += (_, _) => copysel();
		eword.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) {
				golex();
				e.Handled = true;
			}
		};
		bvar.Click += (_, _) => govar();
		bvarcopy.Click += (_, _) => copyvar();
		lvvar.MouseDoubleClick += (_, _) => copyvarsel();
		evar.PreviewKeyDown += (_, e) => {
			if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) {
				govar();
				e.Handled = true;
			}
		};
	}

	void applylang() {
		Title = Loc.T("pwgen.title");
		tabrand.Header = Loc.T("pwgen.tab.rand");
		tablex.Header = Loc.T("pwgen.tab.lex");
		tabvar.Header = Loc.T("pwgen.tab.var");
		lblen.Text = Loc.T("pwgen.len");
		lbcount.Text = Loc.T("pwgen.count");
		clower.Content = Loc.T("pwgen.lower");
		cupper.Content = Loc.T("pwgen.upper");
		cdigit.Content = Loc.T("pwgen.digit");
		csymbol.Content = Loc.T("pwgen.symbol");
		cnoamb.Content = Loc.T("pwgen.noamb");
		ceach.Content = Loc.T("pwgen.each");
		lbout.Text = Loc.T("pwgen.out");
		lblexhint.Text = Loc.T("pwgen.lex.hint");
		lblexllm.Text = Loc.T("pwgen.lex.llm");
		lbword.Text = Loc.T("pwgen.lex.word");
		collexlang.Header = Loc.T("pwgen.lex.col.lang");
		collexnat.Header = Loc.T("pwgen.lex.col.native");
		collexlat.Header = Loc.T("pwgen.lex.col.latin");
		ToolBtnUi.Set(bgen, ToolBtnUi.Play, Loc.T("pwgen.gen"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("pwgen.copy"));
		ToolBtnUi.Set(blex, ToolBtnUi.Play, Loc.T("pwgen.lex.go"));
		ToolBtnUi.Set(blexcopy, ToolBtnUi.Copy, Loc.T("pwgen.copy"));
		ToolBtnUi.Set(blexlat, ToolBtnUi.Copy, Loc.T("pwgen.lex.latin"));
		lbvarhint.Text = Loc.T("pwgen.var.hint");
		lbvarllm.Text = Loc.T("pwgen.lex.llm");
		lbvarcount.Text = Loc.T("pwgen.count");
		lbvarpw.Text = Loc.T("pwgen.var.pw");
		colvarpw.Header = Loc.T("pwgen.var.col.pw");
		colvarnote.Header = Loc.T("pwgen.var.col.note");
		ToolBtnUi.Set(bvar, ToolBtnUi.Play, Loc.T("pwgen.var.go"));
		ToolBtnUi.Set(bvarcopy, ToolBtnUi.Copy, Loc.T("pwgen.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
		updatestat();
	}

	PasswordOpts opts() {
		var len = 16;
		if (int.TryParse((elen.Text ?? "").Trim(), out var n))
			len = Compat.Clamp(n, 4, 128);
		var count = 5;
		if (int.TryParse((ecount.Text ?? "").Trim(), out var c))
			count = Compat.Clamp(c, 1, 50);
		return new PasswordOpts {
			Length = len,
			Count = count,
			Lower = clower.IsChecked == true,
			Upper = cupper.IsChecked == true,
			Digit = cdigit.IsChecked == true,
			Symbol = csymbol.IsChecked == true,
			NoAmbiguous = cnoamb.IsChecked == true,
			EachClass = ceach.IsChecked == true,
		};
	}

	void gen() {
		try {
			var o = opts();
			elen.Text = o.Length.ToString();
			ecount.Text = o.Count.ToString();
			var list = PasswordGen.Generate(o);
			eout.Text = string.Join("\r\n", list);
			saveui();
			updatestat();
		}
		catch (Exception ex) {
			eout.Text = "";
			lbstat.Text = Loc.T("pwgen.fail", ex.Message);
		}
	}

	void copy() {
		var s = (eout.Text ?? "").Trim();
		if (s.Length == 0) return;
		try {
			Clipboard.SetText(s);
			lbstat.Text = Loc.T("pwgen.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void updatestat() {
		try {
			var o = opts();
			var bits = PasswordGen.EntropyBits(o, o.Length);
			var pool = PasswordGen.Pool(o).Length;
			lbstat.Text = Loc.T("pwgen.stat", o.Length, pool, bits.ToString("0"));
		}
		catch {
			lbstat.Text = "";
		}
	}

	void filllexllm() {
		lexUiLoading = true;
		try {
			fillllmbox(elexllm);
			fillllmbox(evarllm);
		}
		finally { lexUiLoading = false; }
	}

	void fillllmbox(ComboBox box) {
		if (box == null) return;
		var want = (opt.PwLexLlm ?? "").Trim();
		if (want.Length == 0)
			want = (opt.TranslateLlm ?? "").Trim();
		if (want.Length == 0)
			want = (opt.ChatLlm ?? "").Trim();
		if (want.Length == 0)
			want = (opt.AsrLlm ?? "").Trim();
		box.Items.Clear();
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
				box.Items.Add(it);
				if (pick == null &&
					(string.Equals(name, want, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(ep.Model ?? "", want, StringComparison.OrdinalIgnoreCase)))
					pick = it;
			}
		}
		if (box.Items.Count == 0) {
			box.Items.Add(new ComboBoxItem {
				Content = Loc.T("chat.llm.none"),
				Tag = null,
				IsEnabled = false,
			});
			box.SelectedIndex = 0;
		}
		else
			box.SelectedItem = pick ?? box.Items[0];
	}

	LlmEndpoint currentlexllm() => currentllm(elexllm) ?? currentllm(evarllm);

	LlmEndpoint currentllm(ComboBox box) {
		var it = box?.SelectedItem as ComboBoxItem;
		return it?.Tag as LlmEndpoint;
	}

	void onllmchange(ComboBox src, ComboBox dst) {
		if (lexUiLoading) return;
		try {
			var ep = currentllm(src);
			opt.PwLexLlm = ep != null ? ep.DisplayName : "";
			AppConfig.Save(opt);
			if (dst == null) return;
			var want = opt.PwLexLlm ?? "";
			lexUiLoading = true;
			try {
				foreach (ComboBoxItem it in dst.Items) {
					if (it.Tag is not LlmEndpoint e) continue;
					if (string.Equals(e.DisplayName, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(e.Model ?? "", want, StringComparison.OrdinalIgnoreCase)) {
						dst.SelectedItem = it;
						break;
					}
				}
			}
			finally { lexUiLoading = false; }
		}
		catch { }
	}

	async void golex() {
		if (lexBusy) return;
		var word = (eword.Text ?? "").Trim();
		if (word.Length == 0) {
			lblexstat.Text = Loc.T("pwgen.lex.need");
			try { eword.Focus(); } catch { }
			return;
		}
		lexBusy = true;
		blex.IsEnabled = false;
		lblexstat.Text = Loc.T("pwgen.lex.busy");
		try { lexCts?.Cancel(); } catch { }
		lexCts = new CancellationTokenSource();
		var ct = lexCts.Token;
		var o = opt;
		var ep = currentlexllm();
		try {
			var rows = await Task.Run(() => WordLex.Translate(o, word, ep, ct), ct).ConfigureAwait(true);
			if (ct.IsCancellationRequested) return;
			lexRows.Clear();
			foreach (var r in rows)
				lexRows.Add(r);
			lblexstat.Text = Loc.T("pwgen.lex.stat", rows.Count);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			lblexstat.Text = Loc.T("pwgen.fail", ex.Message);
		}
		finally {
			lexBusy = false;
			blex.IsEnabled = true;
		}
	}

	void copysel() {
		var row = lvlex.SelectedItem as WordLexRow;
		if (row == null) return;
		var s = (row.Latin ?? "").Trim();
		if (s.Length == 0) s = (row.Native ?? "").Trim();
		if (s.Length == 0) return;
		try {
			Clipboard.SetText(s);
			lblexstat.Text = Loc.T("pwgen.copied");
		}
		catch { }
	}

	void copylex(bool latinOnly) {
		var lines = new List<string>();
		IEnumerable<WordLexRow> src = lvlex.SelectedItems.Count > 0
			? lvlex.SelectedItems.OfType<WordLexRow>()
			: lexRows;
		foreach (var r in src) {
			if (r == null) continue;
			if (latinOnly) {
				if (!string.IsNullOrWhiteSpace(r.Latin))
					lines.Add(r.Latin.Trim());
			}
			else {
				var a = (r.Lang ?? "").Trim();
				var b = (r.Native ?? "").Trim();
				var c = (r.Latin ?? "").Trim();
				lines.Add($"{a}\t{b}\t{c}".Trim());
			}
		}
		if (lines.Count == 0) return;
		try {
			Clipboard.SetText(string.Join("\r\n", lines));
			lblexstat.Text = Loc.T("pwgen.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	int varcount() {
		if (int.TryParse((evarcount.Text ?? "").Trim(), out var n))
			return Compat.Clamp(n, PasswordVariant.MinCount, PasswordVariant.MaxCount);
		return PasswordVariant.DefaultCount;
	}

	async void govar() {
		if (varBusy) return;
		var seed = (evar.Text ?? "").Trim();
		if (seed.Length == 0) {
			lbvarstat.Text = Loc.T("pwgen.var.need");
			try { evar.Focus(); } catch { }
			return;
		}
		var n = varcount();
		evarcount.Text = n.ToString();
		varBusy = true;
		bvar.IsEnabled = false;
		lbvarstat.Text = Loc.T("pwgen.var.busy");
		try { varCts?.Cancel(); } catch { }
		varCts = new CancellationTokenSource();
		var ct = varCts.Token;
		var o = opt;
		var ep = currentllm(evarllm) ?? currentlexllm();
		try {
			var rows = await Task.Run(() => PasswordVariant.Generate(o, seed, n, ep, ct), ct)
				.ConfigureAwait(true);
			if (ct.IsCancellationRequested) return;
			varRows.Clear();
			foreach (var r in rows)
				varRows.Add(r);
			lbvarstat.Text = Loc.T("pwgen.var.stat", rows.Count);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			lbvarstat.Text = Loc.T("pwgen.fail", ex.Message);
		}
		finally {
			varBusy = false;
			bvar.IsEnabled = true;
		}
	}

	void copyvarsel() {
		var row = lvvar.SelectedItem as PasswordVariantRow;
		if (row == null) return;
		var s = (row.Password ?? "").Trim();
		if (s.Length == 0) return;
		try {
			Clipboard.SetText(s);
			lbvarstat.Text = Loc.T("pwgen.copied");
		}
		catch { }
	}

	void copyvar() {
		var lines = new List<string>();
		IEnumerable<PasswordVariantRow> src = lvvar.SelectedItems.Count > 0
			? lvvar.SelectedItems.OfType<PasswordVariantRow>()
			: varRows;
		foreach (var r in src) {
			if (r == null) continue;
			if (!string.IsNullOrWhiteSpace(r.Password))
				lines.Add(r.Password.Trim());
		}
		if (lines.Count == 0) return;
		try {
			Clipboard.SetText(string.Join("\r\n", lines));
			lbvarstat.Text = Loc.T("pwgen.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
