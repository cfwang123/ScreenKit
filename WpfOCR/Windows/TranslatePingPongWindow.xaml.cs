using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace WpfOCR;

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
		this.rounds = rounds < 1 ? 1 : (rounds > 50 ? 50 : rounds);
		startText = text ?? "";
		fwdLabel = $"{TrLang.Label(forward.SourceLang)}→{TrLang.Label(forward.TargetLang)}";
		revLabel = $"{TrLang.Label(reverse.SourceLang)}→{TrLang.Label(reverse.TargetLang)}";

		Title = $"来回翻译 {this.rounds} 次";
		lbtitle.Text = $"来回翻译 {this.rounds} 次 · {fwdLabel} ⇄ {revLabel}";
		lbhint.Text = $"每一「次」= {fwdLabel} + {revLabel}；共 {this.rounds} 次往返（{this.rounds * 2} 步），下方显示每一步结果。";

		bcancel.Click += (_, _) => {
			try { cts?.Cancel(); } catch { }
			lbstatus.Text = "正在取消…";
		};
		bcopy.Click += (_, _) => {
			try {
				if (string.IsNullOrEmpty(lastText)) {
					lbstatus.Text = "尚无结果可复制";
					return;
				}
				Clipboard.SetText(lastText);
				lbstatus.Text = "已复制最终结果";
			}
			catch (Exception ex) {
				lbstatus.Text = "复制失败: " + ex.Message;
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

		appendui($"【原文】{fwdLabel} ⇄ {revLabel}");
		appendui(text);
		appendui("");
		lbstatus.Text = "加载模型…";

		var swAll = System.Diagnostics.Stopwatch.StartNew();
		try {
			var eng = engine;
			var fwd = forward;
			var rev = reverse;
			var pref = prefer;
			await Task.Run(() => {
				if (!eng.EnsureLoaded(fwd.DirKey, fwd.ModelDir, pref))
					throw new InvalidOperationException("正向模型: " + (eng.LastError ?? "加载失败"));
				ct.ThrowIfCancellationRequested();
				if (!eng.EnsureLoaded(rev.DirKey, rev.ModelDir, pref))
					throw new InvalidOperationException("反向模型: " + (eng.LastError ?? "加载失败"));
			}, ct).ConfigureAwait(true);

			var dev = string.IsNullOrEmpty(engine.LastDevice) ? prefer : engine.LastDevice;
			lbstatus.Text = $"翻译中… · {dev}";

			for (var r = 1; r <= rounds; r++) {
				ct.ThrowIfCancellationRequested();
				// 正向
				lbstatus.Text = $"第 {r}/{rounds} 次 · {fwdLabel}…";
				var t1 = text;
				var out1 = await Task.Run(() => {
					ct.ThrowIfCancellationRequested();
					return eng.Translate(fwd.DirKey, t1, ct);
				}, ct).ConfigureAwait(true);
				out1 = (out1 ?? "").Trim();
				if (out1.Length == 0) out1 = t1;
				text = out1;
				lastText = text;
				var stepFwd = (r - 1) * 2 + 1;
				appendui($"── 第 {r} 次 · 步骤 {stepFwd} · {fwdLabel} ──");
				appendui(text);
				appendui("");
				await Dispatcher.Yield(DispatcherPriority.Background);

				ct.ThrowIfCancellationRequested();
				// 反向
				lbstatus.Text = $"第 {r}/{rounds} 次 · {revLabel}…";
				var t2 = text;
				var out2 = await Task.Run(() => {
					ct.ThrowIfCancellationRequested();
					return eng.Translate(rev.DirKey, t2, ct);
				}, ct).ConfigureAwait(true);
				out2 = (out2 ?? "").Trim();
				if (out2.Length == 0) out2 = t2;
				text = out2;
				lastText = text;
				var stepRev = r * 2;
				appendui($"── 第 {r} 次 · 步骤 {stepRev} · {revLabel} ──");
				appendui(text);
				appendui("");
				await Dispatcher.Yield(DispatcherPriority.Background);
			}

			Completed = true;
			FinalText = text;
			swAll.Stop();
			appendui($"【完成】共 {rounds} 次往返 · {rounds * 2} 步 · {swAll.ElapsedMilliseconds} ms");
			lbstatus.Text = $"完成 · {rounds} 次往返 · {dev} · {swAll.ElapsedMilliseconds} ms";
			bcopy.IsEnabled = true;
		}
		catch (OperationCanceledException) {
			FinalText = lastText;
			appendui("【已取消】");
			lbstatus.Text = "已取消 · 可复制当前结果";
			bcopy.IsEnabled = !string.IsNullOrEmpty(lastText);
		}
		catch (Exception ex) {
			FinalText = lastText;
			CaptureLog.Ex("TranslatePingPong", ex);
			appendui("【失败】" + ex.Message);
			lbstatus.Text = "失败: " + ex.Message;
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
