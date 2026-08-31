using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 来回翻译 N 次：A→B→A→…，逐步显示中间结果。
/// 每一「次」= 正向一次 + 反向一次（共 2N 次推理）。
/// </summary>
partial class TranslatePingPongWindow : Window {
	public const int DefaultRounds = 20;

	/// <summary>全部完成后的最终文本；取消/失败则为 null 或已完成部分。</summary>
	public string FinalText { get; private set; }
	/// <summary>是否完整跑完（未取消且无异常）。</summary>
	public bool Completed { get; private set; }

	readonly TranslateEngine engine;
	readonly TranslateModelInfo forward;
	readonly TranslateModelInfo reverse;
	readonly string prefer;
	readonly int rounds;
	readonly string startText;
	readonly string fwdLabel;
	readonly string revLabel;
	readonly Func<string, bool, CancellationToken, string> stepFn;

	CancellationTokenSource cts;
	bool running;
	string lastText = "";

	internal TranslatePingPongWindow(
		TranslateEngine engine,
		TranslateModelInfo forward,
		TranslateModelInfo reverse,
		string text,
		string prefer,
		int rounds = DefaultRounds) {
		InitializeComponent();
		this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
		this.forward = forward ?? throw new ArgumentNullException(nameof(forward));
		this.reverse = reverse ?? throw new ArgumentNullException(nameof(reverse));
		this.prefer = prefer ?? "auto";
		this.rounds = clamprounds(rounds);
		startText = text ?? "";
		fwdLabel = $"{TrLang.Label(forward.SourceLang)}→{TrLang.Label(forward.TargetLang)}";
		revLabel = $"{TrLang.Label(reverse.SourceLang)}→{TrLang.Label(reverse.TargetLang)}";
		stepFn = null;
		bindchrome();
	}

	/// <summary>LLM 来回翻译：step(text, reverse, ct)。</summary>
	internal TranslatePingPongWindow(
		string text,
		string fwdLabel,
		string revLabel,
		int rounds,
		Func<string, bool, CancellationToken, string> step) {
		InitializeComponent();
		engine = null;
		forward = null;
		reverse = null;
		prefer = "llm";
		this.rounds = clamprounds(rounds);
		startText = text ?? "";
		this.fwdLabel = fwdLabel ?? "";
		this.revLabel = revLabel ?? "";
		stepFn = step ?? throw new ArgumentNullException(nameof(step));
		bindchrome();
	}

	static int clamprounds(int rounds) =>
		rounds < 1 ? 1 : (rounds > 50 ? 50 : rounds);

	void bindchrome() {
		Title = Loc.T("pp.title", rounds);
		lbtitle.Text = Loc.T("pp.sub", rounds, fwdLabel, revLabel);
		lbhint.Text = Loc.T("pp.hint", fwdLabel, revLabel, rounds, rounds * 2);
		bcancel.Content = Loc.T("cancel");
		bcancel.ToolTip = Loc.T("pp.cancel.tip");
		bcopy.Content = Loc.T("pp.copy");
		bcopy.ToolTip = Loc.T("pp.copy.tip");
		bclose.Content = Loc.T("pp.close");

		bcancel.Click += (_, _) => {
			try { cts?.Cancel(); } catch { }
			lbstatus.Text = Loc.T("cancelling");
		};
		bcopy.Click += (_, _) => {
			try {
				if (string.IsNullOrEmpty(lastText)) {
					lbstatus.Text = Loc.T("pp.empty");
					return;
				}
				Clipboard.SetText(lastText);
				lbstatus.Text = Loc.T("pp.copied");
			}
			catch (Exception ex) {
				lbstatus.Text = Loc.T("pp.copy.fail", ex.Message);
			}
		};
		bclose.Click += (_, _) => Close();
		WindowEsc.Attach(this, () => {
			if (running) {
				try { cts?.Cancel(); } catch { }
				return;
			}
			Close();
		});
		Loaded += async (_, _) => await runasync();
		Closing += (_, e) => {
			if (running) {
				try { cts?.Cancel(); } catch { }
			}
		};
	}

	async Task runasync() {
		if (running) return;
		running = true;
		Completed = false;
		FinalText = null;
		bcopy.IsEnabled = false;
		bcancel.IsEnabled = true;
		cts = new CancellationTokenSource();
		var ct = cts.Token;
		var sb = new StringBuilder();
		var text = startText.Trim();
		lastText = text;

		void appendui(string line) {
			sb.AppendLine(line);
			elog.Text = sb.ToString();
			elog.CaretIndex = elog.Text.Length;
			elog.ScrollToEnd();
		}

		appendui(Loc.T("pp.src", fwdLabel, revLabel));
		appendui(text);
		appendui("");
		lbstatus.Text = engine != null ? Loc.T("pp.load") : Loc.T("pp.llm.run");

		var swAll = System.Diagnostics.Stopwatch.StartNew();
		try {
			var eng = engine;
			var fwd = forward;
			var rev = reverse;
			var pref = prefer;
			var step = stepFn;
			if (eng != null) {
				await Task.Run(() => {
					if (!eng.EnsureLoaded(fwd.DirKey, fwd.ModelDir, pref))
						throw new InvalidOperationException(Loc.T("pp.fwd.err", eng.LastError ?? Loc.T("tr.load.fail")));
					ct.ThrowIfCancellationRequested();
					if (!eng.EnsureLoaded(rev.DirKey, rev.ModelDir, pref))
						throw new InvalidOperationException(Loc.T("pp.rev.err", eng.LastError ?? Loc.T("tr.load.fail")));
				}, ct).ConfigureAwait(true);
			}

			var dev = eng != null
				? (string.IsNullOrEmpty(engine.LastDevice) ? prefer : engine.LastDevice)
				: "LLM";
			lbstatus.Text = Loc.T("pp.run.dev", dev);

			for (var r = 1; r <= rounds; r++) {
				ct.ThrowIfCancellationRequested();
				lbstatus.Text = Loc.T("pp.round", r, rounds, fwdLabel);
				var t1 = text;
				var out1 = await Task.Run(() => {
					ct.ThrowIfCancellationRequested();
					if (step != null) return step(t1, false, ct);
					return eng.Translate(fwd.DirKey, t1, ct);
				}, ct).ConfigureAwait(true);
				out1 = (out1 ?? "").Trim();
				if (out1.Length == 0) out1 = t1;
				text = out1;
				lastText = text;
				var stepFwd = (r - 1) * 2 + 1;
				appendui(Loc.T("pp.step", r, stepFwd, fwdLabel));
				appendui(text);
				appendui("");
				await Dispatcher.Yield(DispatcherPriority.Background);

				ct.ThrowIfCancellationRequested();
				lbstatus.Text = Loc.T("pp.round", r, rounds, revLabel);
				var t2 = text;
				var out2 = await Task.Run(() => {
					ct.ThrowIfCancellationRequested();
					if (step != null) return step(t2, true, ct);
					return eng.Translate(rev.DirKey, t2, ct);
				}, ct).ConfigureAwait(true);
				out2 = (out2 ?? "").Trim();
				if (out2.Length == 0) out2 = t2;
				text = out2;
				lastText = text;
				var stepRev = r * 2;
				appendui(Loc.T("pp.step", r, stepRev, revLabel));
				appendui(text);
				appendui("");
				await Dispatcher.Yield(DispatcherPriority.Background);
			}

			Completed = true;
			FinalText = text;
			swAll.Stop();
			appendui(Loc.T("pp.ok.log", rounds, rounds * 2, swAll.ElapsedMilliseconds));
			lbstatus.Text = Loc.T("pp.ok", rounds, dev, swAll.ElapsedMilliseconds);
			bcopy.IsEnabled = true;
		}
		catch (OperationCanceledException) {
			FinalText = lastText;
			appendui(Loc.T("pp.cancelled.log"));
			lbstatus.Text = Loc.T("pp.cancelled");
			bcopy.IsEnabled = !string.IsNullOrEmpty(lastText);
		}
		catch (Exception ex) {
			FinalText = lastText;
			CaptureLog.Ex("TranslatePingPong", ex);
			appendui(Loc.T("pp.fail.log", ex.Message));
			lbstatus.Text = Loc.T("pp.fail", ex.Message);
			bcopy.IsEnabled = !string.IsNullOrEmpty(lastText);
		}
		finally {
			running = false;
			bcancel.IsEnabled = false;
			try { cts?.Dispose(); } catch { }
			cts = null;
		}
	}
}
