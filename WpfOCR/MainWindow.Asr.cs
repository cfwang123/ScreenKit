using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace WpfOCR;

/// <summary>MainWindow：语音识别（识别 + 批量字幕 + 全局语音输入）。</summary>
public partial class MainWindow {
	AsrEngine asrEngine;
	AsrStreamEngine asrStreamEngine;
	readonly object asrEngineGate = new();
	readonly object asrStreamGate = new();
	List<AsrModelInfo> asrModels = new();
	bool asrUiLoading;
	AsrLiveCapture asrCap;
	float[] asrPendingSamples;
	int asrPendingSr = 16000;
	string asrPendingPath;

	readonly ObservableCollection<AsrSrtQueueItem> asrtQueue = new();
	CancellationTokenSource asrtCts;
	bool asrtRunning;

	AsrVoiceInput asrVoice;
	VoiceInputHud asrVoiceHud;
	bool asrVoiceBusy;
	int lastVoiceStop;
	DispatcherTimer voiceHkResume;

	// 系统实时字幕（流式 → 识别结果框 + 桌面 OSD）
	bool asrLiveOn;
	bool asrLiveBusy;
	AsrLiveCapture asrLiveCap;
	CancellationTokenSource asrLiveCts;
	Task asrLiveTask;
	readonly List<string> asrLiveLines = new();
	string asrLivePartial = "";
	readonly object asrLiveTextGate = new();
	int lastAsrLiveUiTick;
	AsrCaptionOsdWindow asrLiveOsd;

	void initasr() {
		try { asrEngine = new AsrEngine(); }
		catch (Exception ex) {
			CaptureLog.Ex("AsrEngine init", ex);
			asrEngine = null;
		}
		try { asrStreamEngine = new AsrStreamEngine(); }
		catch (Exception ex) {
			CaptureLog.Ex("AsrStreamEngine init", ex);
			asrStreamEngine = null;
		}

		asrUiLoading = true;
		easrcompute.Items.Clear();
		easrcompute.Items.Add(new ComboBoxItem { Content = "自动（CUDA→核显→CPU）", Tag = TtsComputeMode.Auto });
		easrcompute.Items.Add(new ComboBoxItem { Content = "GPU（NVIDIA CUDA）", Tag = TtsComputeMode.Gpu });
		easrcompute.Items.Add(new ComboBoxItem { Content = "核显（Intel DirectML）", Tag = TtsComputeMode.Igpu });
		easrcompute.Items.Add(new ComboBoxItem { Content = "CPU", Tag = TtsComputeMode.Cpu });
		easrcompute.SelectedIndex = 0;

		easrlang.Items.Clear();
		foreach (var (label, tag) in new[] {
			("自动", "auto"), ("中文", "zh"), ("英文", "en"),
			("日文", "ja"), ("韩文", "ko"), ("粤语", "yue"),
		})
			easrlang.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
		easrlang.SelectedIndex = 0;

		var wantComp = (opt.AsrCompute ?? "Auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
		foreach (ComboBoxItem it in easrcompute.Items) {
			if (it.Tag is TtsComputeMode m && m == wantComp) {
				easrcompute.SelectedItem = it;
				break;
			}
		}
		selectcombobytag(easrlang, string.IsNullOrWhiteSpace(opt.AsrLang) ? "auto" : opt.AsrLang);
		casritn.IsChecked = opt.AsrItn;
		selectcombobytag(easrsource, string.IsNullOrWhiteSpace(opt.AsrAudioSource) ? "Mic" : opt.AsrAudioSource);

		easrcompute.SelectionChanged += (_, _) => {
			if (asrUiLoading) return;
			if (easrcompute.SelectedItem is ComboBoxItem it && it.Tag is TtsComputeMode m) {
				if (asrEngine != null) asrEngine.Mode = m;
				if (asrStreamEngine != null) asrStreamEngine.Mode = m;
				try { asrEngine?.UnloadSafe(); } catch { }
				try { asrStreamEngine?.UnloadSafe(); } catch { }
				lbasrstatus.Text = "计算设备 → " + it.Content + "（下次识别时加载）";
				saveasrprefs();
			}
		};
		easrlang.SelectionChanged += (_, _) => {
			if (asrUiLoading) return;
			try { asrEngine?.UnloadSafe(); } catch { }
			saveasrprefs();
		};
		casritn.Checked += (_, _) => {
			if (!asrUiLoading) {
				try { asrEngine?.UnloadSafe(); } catch { }
				saveasrprefs();
			}
		};
		casritn.Unchecked += (_, _) => {
			if (!asrUiLoading) {
				try { asrEngine?.UnloadSafe(); } catch { }
				saveasrprefs();
			}
		};
		easrmodel.SelectionChanged += (_, _) => {
			if (asrUiLoading) return;
			try { asrEngine?.UnloadSafe(); } catch { }
			saveasrprefs();
		};
		easrmodelstream.SelectionChanged += (_, _) => {
			if (asrUiLoading) return;
			try { asrStreamEngine?.UnloadSafe(); } catch { }
			saveasrprefs();
		};

		// —— 识别子页 ——
		basropen.Click += (_, _) => asropenfile();
		basrrec.Click += (_, _) => asrtogglerec();
		basrstop.Click += (_, _) => _ = asrstoprecandrunasync();
		basrrun.Click += (_, _) => _ = asrrunasync();
		basrlive.Click += (_, _) => asrtogglelive();
		basrlivestyle.Click += (_, _) => openasrcaptionstyle();
		easrsource.SelectionChanged += (_, _) => {
			if (asrUiLoading) return;
			saveasrprefs();
		};
		basrreload.Click += (_, _) => {
			scanasrmodels();
			fillasrmodels();
		};
		basrclear.Click += (_, _) => {
			if (asrLiveOn) {
				lbasrstatus.Text = "实时字幕进行中，请先停止后再清空";
				return;
			}
			easrtext.Text = "";
			lbasrstatus.Text = "已清空";
		};
		basrcopy.Click += (_, _) => {
			var t = easrtext.Text ?? "";
			if (string.IsNullOrEmpty(t)) {
				lbasrstatus.Text = "无内容可复制";
				return;
			}
			try {
				Clipboard.SetText(t);
				lbasrstatus.Text = "已复制识别结果";
			}
			catch (Exception ex) { lbasrstatus.Text = "复制失败: " + ex.Message; }
		};

		// —— 字幕队列 ——
		easrtlist.ItemsSource = asrtQueue;
		basrtadd.Click += (_, _) => asrtaddfiles();
		basrtremove.Click += (_, _) => asrtremoveselected();
		basrtclear.Click += (_, _) => {
			if (asrtRunning) return;
			asrtQueue.Clear();
			asrtrefreshcount();
			lbasrtdetail.Text = "列表已清空。";
		};
		basrtbrowse.Click += (_, _) => asrtbrowseoutdir();
		casrtsamedir.Checked += (_, _) => asrtoutdirmode();
		casrtsamedir.Unchecked += (_, _) => asrtoutdirmode();
		basrtstart.Click += (_, _) => _ = asrtbatchasync();
		basrtstop.Click += (_, _) => {
			try { asrtCts?.Cancel(); } catch { }
			basrtstop.IsEnabled = false;
			basrtstop.Content = "中止中…";
			lbasrtdetail.Text = "正在中止…";
			lbasrstatus.Text = "字幕任务中止中…";
		};

		easrtlist.AllowDrop = true;
		easrtlist.PreviewDragOver += (_, e) => {
			if (hasasrmediadrop(e.Data)) {
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;
			}
		};
		easrtlist.Drop += (_, e) => {
			var paths = pickasrmediapaths(e.Data);
			if (paths.Count == 0) return;
			e.Handled = true;
			asrtaddpaths(paths);
		};

		// 窗口级拖入音视频
		PreviewDragOver += asronpreviewdragover;
		PreviewDrop += asronpreviewdrop;

		scanasrmodels();
		fillasrmodels();
		asrtoutdirmode();
		asrtrefreshcount();
		initasrvoice();
		asrUiLoading = false;
	}

	void initasrvoice() {
		asrVoice = new AsrVoiceInput();
		asrVoice.Recognize = asrvoicerecognize;
		asrVoice.Polish = asrvoicepolish;
		asrVoice.ResolveStreamEngine = asrvoiceresolvestream;
		asrVoice.SplitSentences = opt.AsrVoiceSplit;
		asrVoice.SplitIntervalSec = opt.AsrVoiceSplitSec;
		asrVoice.ActiveChanged += active => Dispatcher.BeginInvoke(new Action(() => {
			if (active) showasrvoicehud();
			else hideasrvoicehud();
		}));
		asrVoice.StatusChanged += s => Dispatcher.BeginInvoke(new Action(() => {
			try { applyvoicehudmsg(s); } catch { }
			try { setstatus(s); } catch { }
		}));
		asrVoice.ErrorOccurred += s => Dispatcher.BeginInvoke(new Action(() => {
			try { asrVoiceHud?.SetStatus(s); } catch { }
			try { setstatus(s); } catch { }
			CaptureLog.Info("AsrVoice: " + s);
		}));
		asrVoice.TextInjected += t => {
			CaptureLog.Info("AsrVoice inject: " + (t.Length > 40 ? t.Substring(0, 40) + "…" : t));
		};
		asrVoice.TextCommitted += () => Dispatcher.BeginInvoke(new Action(() => {
			try { asrVoiceHud?.SetDetail("", ""); } catch { }
		}));
		asrVoice.PartialText += t => Dispatcher.BeginInvoke(new Action(() => {
			try { asrVoiceHud?.SetDetail("", t); } catch { }
		}));
	}

	/// <summary>全局热键 / 菜单：切换语音输入（设置中可选流式或离线）。</summary>
	void toggleasrvoice(bool fromHotkey = false) {
		try {
			CaptureLog.Info("toggleasrvoice enter busy=" + asrVoiceBusy
				+ " active=" + (asrVoice != null && asrVoice.IsActive)
				+ " fromHk=" + fromHotkey
				+ " voiceHk=" + (opt.HotkeyVoiceInput ?? "")
				+ " voiceReg=" + (hotkeyVoice != null && hotkeyVoice.IsRegistered));
		}
		catch { }

		if (asrVoiceBusy) {
			notifyvoice("语音输入忙，请稍候…", err: true);
			return;
		}
		if (asrVoice != null && asrVoice.IsActive) {
			asrVoiceBusy = true;
			try {
				// 先注销，避免结束时仍按着热键 / 注入文字再次 WM_HOTKEY 立刻重开
				suspendvoicehotkey();
				if (!asrVoice.IsStreamingMode) {
					notifyvoice("识别中…", showHud: true);
					try { Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render); } catch { }
				}
				asrVoice.Stop();
				lastVoiceStop = Environment.TickCount;
				notifyvoice("语音输入已结束");
			}
			catch (Exception ex) {
				notifyvoice("结束失败: " + ex.Message, err: true);
			}
			finally {
				asrVoiceBusy = false;
				resumevoicehotkeywhenclear();
			}
			return;
		}

		if (fromHotkey) {
			var sinceStop = unchecked((uint)(Environment.TickCount - lastVoiceStop));
			if (lastVoiceStop != 0 && sinceStop < 400) {
				CaptureLog.Info("toggleasrvoice ignore restart debounce " + sinceStop);
				return;
			}
		}

		if (asrtRunning) {
			notifyvoice("字幕批量进行中，无法启动语音输入", err: true);
			return;
		}

		// 必须在 UI 线程解析模型，避免后台线程碰 ComboBox
		AsrModelInfo streamModel = null;
		AsrModelInfo offlineModel = null;
		var hasStream = tryresolvestreammodel(out streamModel);
		var hasOffline = tryresolveofflinemodel(out offlineModel);
		if (!FeaturePrompt.EnsureSherpa(this)) {
			notifyvoice("未安装 Sherpa 运行库", err: true);
			return;
		}
		if (!hasStream && !hasOffline) {
			if (FeaturePrompt.EnsureAsrModels(this)) {
				try { scanasrmodels(); fillasrmodels(); } catch { }
				hasStream = tryresolvestreammodel(out streamModel);
				hasOffline = tryresolveofflinemodel(out offlineModel);
			}
			if (!hasStream && !hasOffline) {
				notifyvoice("无可用 ASR 模型，请安装到 asrmodels", err: true);
				return;
			}
		}
		var wantStream = asrvoicewantstream();
		if (!wantStream && !hasOffline) {
			notifyvoice("离线听写需要离线模型（SenseVoice 等），请安装或改选流式", err: true);
			return;
		}
		if ((!wantStream || !hasStream) && asrEngine == null) {
			notifyvoice("ASR 引擎不可用", err: true);
			return;
		}

		// 主界面录音 / 实时字幕占用时先停
		if (asrLiveOn) stopasrlive(finalFlush: false);
		if (asrCap != null && asrCap.IsRecording) {
			try {
				asrCap.Stop();
				asrCap.Dispose();
			}
			catch { }
			asrCap = null;
			basrrec.Content = "录音";
			basrstop.IsEnabled = false;
			basrrun.IsEnabled = true;
		}

		asrVoiceBusy = true;
		var compute = asrcurcompute();
		var preferStream = wantStream && hasStream && asrStreamEngine != null && streamModel != null;
		var lang = string.IsNullOrWhiteSpace(opt.AsrLang) ? "auto" : opt.AsrLang;
		var useItn = opt.AsrItn;
		var streamCopy = streamModel;
		var offlineCopy = offlineModel;

		// 立刻反馈（主窗隐藏时也要看到）
		var loadingTip = preferStream ? "语音输入 · 加载流式模型…" : "语音输入 · 加载离线模型…";
		notifyvoice(loadingTip, showHud: true);

		Task.Run(() => {
			Exception loadEx = null;
			var usedStream = false;
			if (preferStream) {
				try {
					lock (asrStreamGate) {
						asrStreamEngine.Mode = compute;
						asrStreamEngine.LoadModel(streamCopy);
					}
					usedStream = true;
				}
				catch (Exception ex) {
					loadEx = ex;
					CaptureLog.Ex("voice stream LoadModel", ex);
					// 尝试离线回退
					if (offlineCopy != null && asrEngine != null) {
						try {
							lock (asrEngineGate) {
								asrEngine.Mode = compute;
								asrEngine.LoadModel(offlineCopy, lang, useItn);
							}
							usedStream = false;
							loadEx = null;
						}
						catch (Exception ex2) {
							loadEx = ex2;
							CaptureLog.Ex("voice offline fallback LoadModel", ex2);
						}
					}
				}
			}
			else if (offlineCopy != null && asrEngine != null) {
				try {
					lock (asrEngineGate) {
						asrEngine.Mode = compute;
						asrEngine.LoadModel(offlineCopy, lang, useItn);
					}
				}
				catch (Exception ex) {
					loadEx = ex;
					CaptureLog.Ex("voice offline LoadModel", ex);
				}
			}
			return (loadEx, usedStream);
		}).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				try {
					if (t.IsFaulted) {
						var msg = t.Exception?.GetBaseException()?.Message ?? "加载失败";
						notifyvoice("启动失败: " + msg, err: true, showHud: true);
						schedulehidevoicehud(2800);
						return;
					}
					var (loadEx, _) = t.Result;
					if (loadEx != null) {
						notifyvoice("启动失败: " + loadEx.Message, err: true, showHud: true);
						schedulehidevoicehud(3200);
						return;
					}
					// ResolveStreamEngine 用已缓存模型，勿再碰 UI
					_voiceStreamModel = streamCopy;
					_voiceOfflineModel = offlineCopy;
					asrVoice.SplitSentences = opt.AsrVoiceSplit;
					asrVoice.SplitIntervalSec = opt.AsrVoiceSplitSec;
					asrVoice.Start();
					var modeTip = asrVoice.IsStreamingMode ? "流式" : "离线";
					var hk = string.IsNullOrWhiteSpace(opt.HotkeyVoiceInput)
						? "热键" : opt.HotkeyVoiceInput.Trim();
					var ok = $"{modeTip}听写中 · 再按 {hk} 结束";
					notifyvoice(ok, showHud: true);
					showasrvoicehud();
				}
				catch (Exception ex) {
					CaptureLog.Ex("toggleasrvoice start", ex);
					notifyvoice("启动失败: " + ex.Message, err: true, showHud: true);
					schedulehidevoicehud(3200);
				}
				finally {
					asrVoiceBusy = false;
				}
			}));
		}, TaskScheduler.Default);
	}

	void suspendvoicehotkey() {
		try { voiceHkResume?.Stop(); } catch { }
		try { hotkeyVoice?.Unregister(); } catch { }
	}

	void resumevoicehotkeywhenclear() {
		if (voiceHkResume == null) {
			voiceHkResume = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
			voiceHkResume.Tick += (_, _) => {
				var hk = (opt.HotkeyVoiceInput ?? "").Trim();
				if (!string.IsNullOrEmpty(hk) && GlobalHotkey.IsComboDown(hk)) return;
				try { voiceHkResume.Stop(); } catch { }
				if (string.IsNullOrEmpty(hk) || hotkeyVoice == null) return;
				try {
					hotkeyVoice.Attach();
					hotkeyVoice.Register(hk);
				}
				catch (Exception ex) {
					CaptureLog.Ex("resumevoicehotkey", ex);
				}
			};
		}
		try { voiceHkResume.Stop(); } catch { }
		voiceHkResume.Start();
	}

	AsrModelInfo _voiceStreamModel;
	AsrModelInfo _voiceOfflineModel;

	/// <summary>Start 时回调：返回已加载的流式引擎；失败或设置为离线则返回 null。</summary>
	AsrStreamEngine asrvoiceresolvestream() {
		if (!asrvoicewantstream()) return null;
		if (asrStreamEngine == null) return null;
		var model = _voiceStreamModel;
		if (model == null || !model.IsStreaming) {
			if (!tryresolvestreammodel(out model)) return null;
		}
		var compute = (opt.AsrCompute ?? "Auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
		lock (asrStreamGate) {
			if (!asrStreamEngine.IsLoaded) {
				asrStreamEngine.Mode = compute;
				asrStreamEngine.LoadModel(model);
			}
			return asrStreamEngine.IsLoaded ? asrStreamEngine : null;
		}
	}

	/// <summary>状态栏 + 可选浮层 toast（主窗隐藏时也能看见；不弹托盘右下角通知）。</summary>
	void notifyvoice(string msg, bool err = false, bool showHud = false) {
		try { setstatus(msg); } catch { }
		try {
			CaptureLog.Info((err ? "AsrVoice ERR: " : "AsrVoice: ") + msg);
		}
		catch { }
		// 语音输入只用浮层 toast，不弹右下角托盘气泡
		if (showHud) {
			try {
				if (asrVoiceHud == null) {
					asrVoiceHud = new VoiceInputHud();
					asrVoiceHud.Closed += (_, _) => asrVoiceHud = null;
				}
				applyvoicehudmsg(msg);
				if (!asrVoiceHud.IsVisible)
					asrVoiceHud.Show();
			}
			catch (Exception ex) {
				CaptureLog.Ex("notifyvoice hud", ex);
			}
		}
	}

	/// <summary>「识别中/润色中」只写第二行，避免盖掉第一行听写提示。</summary>
	void applyvoicehudmsg(string s) {
		if (asrVoiceHud == null) return;
		s = s ?? "";
		if (s.StartsWith("识别中") || s.StartsWith("润色中")) {
			asrVoiceHud.SetDetail(s.StartsWith("识别中") ? "识别中 · Esc 停止" : s.TrimEnd('。', '.', '…', ' ').Trim(), "");
			return;
		}
		if (s.StartsWith("已中止")) {
			asrVoiceHud.SetDetail("已中止", "");
			return;
		}
		if (s.StartsWith("…") || s.StartsWith("...")) {
			asrVoiceHud.SetDetail("", s.TrimStart('.', '…', ' '));
			return;
		}
		asrVoiceHud.SetStatus(s);
	}

	void schedulehidevoicehud(int delayMs) {
		var ms = delayMs < 500 ? 500 : delayMs;
		var t = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromMilliseconds(ms),
		};
		t.Tick += (_, _) => {
			try { t.Stop(); } catch { }
			// 若已正式进入听写则勿关
			if (asrVoice != null && asrVoice.IsActive) return;
			hideasrvoicehud();
		};
		t.Start();
	}

	string asrvoicerecognize(float[] samples, int sr) {
		if (asrEngine == null) return "";
		var model = _voiceOfflineModel;
		if (model == null || model.IsStreaming) {
			// 后台线程勿碰 UI 控件
			if (asrModels == null || asrModels.Count == 0) return "";
			model = asrModels.FirstOrDefault(x => !x.IsStreaming);
			if (model == null) return "";
		}
		var lang = string.IsNullOrWhiteSpace(opt.AsrLang) ? "auto" : opt.AsrLang;
		var useItn = opt.AsrItn;
		var compute = (opt.AsrCompute ?? "Auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
		lock (asrEngineGate) {
			asrEngine.Mode = compute;
			asrEngine.LoadModel(model, lang, useItn);
			return asrEngine.Recognize(samples, sr) ?? "";
		}
	}

	/// <summary>设置：stream=流式听写；offline/离线=整段录音，停止后一次性输出。</summary>
	bool asrvoicewantstream() {
		return !asrismodeoffline(opt.AsrVoiceMode);
	}

	bool asrlivewantstream() {
		return !asrismodeoffline(opt.AsrLiveMode);
	}

	static bool asrismodeoffline(string m) {
		m = (m ?? "").Trim();
		return m.Equals("offline", StringComparison.OrdinalIgnoreCase)
			|| m.Equals("off", StringComparison.OrdinalIgnoreCase)
			|| m == "离线";
	}

	string asrvoicepolish(string text, string context, CancellationToken ct) {
		if (!opt.AsrVoicePolish || !AsrLlmClient.IsConfigured(opt)) return text;
		text = (text ?? "").Trim();
		if (text.Length == 0) return text;
		showpolishhud(text);
		return AsrLlmClient.Polish(opt, text, context, ct);
	}

	void showpolishhud(string original) {
		try {
			void paint() {
				if (asrVoiceHud == null) {
					asrVoiceHud = new VoiceInputHud();
					asrVoiceHud.Closed += (_, _) => asrVoiceHud = null;
				}
				asrVoiceHud.SetPolish(original);
				if (!asrVoiceHud.IsVisible)
					asrVoiceHud.Show();
				asrVoiceHud.UpdateLayout();
			}
			if (Dispatcher.CheckAccess())
				paint();
			else
				Dispatcher.Invoke(new Action(paint));
			Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
		}
		catch (Exception ex) {
			CaptureLog.Ex("showpolishhud", ex);
		}
	}

	bool tryresolveasrmodel(out AsrModelInfo model) {
		// 兼容旧调用：默认解析离线模型
		return tryresolveofflinemodel(out model);
	}

	bool tryresolvestreammodel(out AsrModelInfo model) {
		model = null;
		if (asrModels == null || asrModels.Count == 0) {
			try { scanasrmodels(); } catch { }
		}
		if (asrModels == null) return false;
		AsrModelInfo sel = null;
		try {
			if (Dispatcher.CheckAccess() && easrmodelstream?.SelectedItem is AsrModelInfo m)
				sel = m;
		}
		catch { }
		if (sel != null && sel.IsStreaming) {
			model = sel;
			return true;
		}
		// 配置名 → 流式列表；兼容旧版 asr_model 曾存流式名
		var want = !string.IsNullOrEmpty(opt.AsrModelStream) ? opt.AsrModelStream : opt.AsrModel;
		if (!string.IsNullOrEmpty(want)) {
			var byName = asrModels.FirstOrDefault(x => x.IsStreaming
				&& string.Equals(x.DisplayName, want, StringComparison.OrdinalIgnoreCase));
			if (byName != null) {
				model = byName;
				return true;
			}
		}
		model = asrModels.FirstOrDefault(x => x.IsStreaming);
		return model != null;
	}

	bool tryresolveofflinemodel(out AsrModelInfo model) {
		model = null;
		if (asrModels == null || asrModels.Count == 0) {
			try { scanasrmodels(); } catch { }
		}
		if (asrModels == null) return false;
		AsrModelInfo sel = null;
		try {
			if (Dispatcher.CheckAccess() && easrmodel?.SelectedItem is AsrModelInfo m)
				sel = m;
		}
		catch { }
		if (sel != null && !sel.IsStreaming) {
			model = sel;
			return true;
		}
		if (!string.IsNullOrEmpty(opt.AsrModel)) {
			var byName = asrModels.FirstOrDefault(x => !x.IsStreaming
				&& string.Equals(x.DisplayName, opt.AsrModel, StringComparison.OrdinalIgnoreCase));
			if (byName != null) {
				model = byName;
				return true;
			}
		}
		model = asrModels.FirstOrDefault(x => !x.IsStreaming);
		return model != null;
	}

	string asrcurlang() {
		if (easrlang?.SelectedItem is ComboBoxItem li && li.Tag is string ls && !string.IsNullOrWhiteSpace(ls))
			return ls;
		return string.IsNullOrWhiteSpace(opt.AsrLang) ? "auto" : opt.AsrLang;
	}

	TtsComputeMode asrcurcompute() {
		if (easrcompute?.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode m)
			return m;
		return (opt.AsrCompute ?? "Auto").Trim().ToLowerInvariant() switch {
			"gpu" or "cuda" => TtsComputeMode.Gpu,
			"cpu" => TtsComputeMode.Cpu,
			"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
			_ => TtsComputeMode.Auto,
		};
	}

	void showasrvoicehud() {
		try {
			if (asrVoiceHud == null) {
				asrVoiceHud = new VoiceInputHud();
				asrVoiceHud.Closed += (_, _) => asrVoiceHud = null;
			}
			var mode = asrVoice != null && asrVoice.IsStreamingMode ? "流式" : "离线";
			var tip = string.IsNullOrWhiteSpace(opt.HotkeyVoiceInput)
				? $"{mode}语音输入中…"
				: $"{mode}语音输入中… 再按 {opt.HotkeyVoiceInput.Trim()} 结束";
			asrVoiceHud.SetStatus(tip);
			asrVoiceHud.SetDetail("", "");
			if (!asrVoiceHud.IsVisible)
				asrVoiceHud.Show();
		}
		catch (Exception ex) {
			CaptureLog.Ex("showasrvoicehud", ex);
		}
	}

	void hideasrvoicehud() {
		try {
			if (asrVoiceHud != null) {
				asrVoiceHud.Close();
				asrVoiceHud = null;
			}
		}
		catch { }
	}

	void asronpreviewdragover(object sender, DragEventArgs e) {
		if (hasasrmediadrop(e.Data)) {
			e.Effects = DragDropEffects.Copy;
			e.Handled = true;
		}
	}

	void asronpreviewdrop(object sender, DragEventArgs e) {
		var paths = pickasrmediapaths(e.Data);
		if (paths.Count == 0) return;
		e.Handled = true;
		try { maintabs.SelectedItem = tabasr; } catch { }

		// 多文件或已在字幕页 → 进字幕队列；单文件在识别页 → 加载识别
		var onSrt = asrsubtabs?.SelectedItem == tabasrsrt;
		if (onSrt || paths.Count > 1) {
			try { asrsubtabs.SelectedItem = tabasrsrt; } catch { }
			asrtaddpaths(paths);
		}
		else {
			try { asrsubtabs.SelectedItem = tabasrrec; } catch { }
			_ = asrloadpathasync(paths[0]);
		}
	}

	static bool hasasrmediadrop(IDataObject data) => pickasrmediapaths(data).Count > 0;

	static string pickasrmediapath(IDataObject data) {
		var list = pickasrmediapaths(data);
		return list.Count > 0 ? list[0] : null;
	}

	static List<string> pickasrmediapaths(IDataObject data) {
		var r = new List<string>();
		if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return r;
		var files = data.GetData(DataFormats.FileDrop) as string[];
		if (files == null) return r;
		foreach (var f in files) {
			if (string.IsNullOrWhiteSpace(f) || !File.Exists(f)) continue;
			if (AsrAudio.IsMediaPath(f)) r.Add(f);
		}
		return r;
	}

	void scanasrmodels() {
		try {
			asrModels = AsrModelScanner.Scan();
			var root = AsrModelScanner.ResolveRoot();
			lbasrhint.Text = asrModels.Count > 0
				? $"模型：{root} · {asrModels.Count} 个"
				: $"未找到模型 → {AsrModelScanner.ModelsRoot()}";
		}
		catch (Exception ex) {
			asrModels = new List<AsrModelInfo>();
			lbasrhint.Text = "扫描失败: " + ex.Message;
		}
	}

	void fillasrmodels() {
		asrUiLoading = true;
		try {
			var offline = asrModels.Where(m => !m.IsStreaming).ToList();
			var stream = asrModels.Where(m => m.IsStreaming).ToList();

			easrmodel.DisplayMemberPath = "ListName";
			easrmodel.ItemsSource = null;
			easrmodel.ItemsSource = offline;

			easrmodelstream.DisplayMemberPath = "ListName";
			easrmodelstream.ItemsSource = null;
			easrmodelstream.ItemsSource = stream;

			if (asrModels.Count == 0) {
				easrmodel.SelectedItem = null;
				easrmodelstream.SelectedItem = null;
				lbasrstatus.Text = "无 ASR 模型 · 见 TODO.md";
				return;
			}

			// 字幕/文件识别：离线模型
			AsrModelInfo pickOff = null;
			if (!string.IsNullOrEmpty(opt.AsrModel))
				pickOff = offline.FirstOrDefault(m =>
					string.Equals(m.DisplayName, opt.AsrModel, StringComparison.OrdinalIgnoreCase));
			// 兼容：旧配置 asr_model 若是流式名则忽略，改选第一个离线
			pickOff ??= offline.Count > 0 ? offline[0] : null;
			easrmodel.SelectedItem = pickOff;

			// 流式语音输入
			AsrModelInfo pickSt = null;
			var wantSt = !string.IsNullOrEmpty(opt.AsrModelStream) ? opt.AsrModelStream : null;
			// 兼容旧配置：asr_model 曾是流式名
			if (wantSt == null && !string.IsNullOrEmpty(opt.AsrModel)) {
				var legacy = stream.FirstOrDefault(m =>
					string.Equals(m.DisplayName, opt.AsrModel, StringComparison.OrdinalIgnoreCase));
				if (legacy != null) wantSt = opt.AsrModel;
			}
			if (!string.IsNullOrEmpty(wantSt))
				pickSt = stream.FirstOrDefault(m =>
					string.Equals(m.DisplayName, wantSt, StringComparison.OrdinalIgnoreCase));
			pickSt ??= stream.Count > 0 ? stream[0] : null;
			easrmodelstream.SelectedItem = pickSt;

			lbasrstatus.Text = $"就绪 · 离线 {offline.Count} · 流式 {stream.Count}";
		}
		finally {
			asrUiLoading = false;
		}
	}

	void saveasrprefs() {
		try {
			opt.AsrModel = easrmodel.SelectedItem is AsrModelInfo m ? m.DisplayName : (opt.AsrModel ?? "");
			opt.AsrModelStream = easrmodelstream.SelectedItem is AsrModelInfo sm
				? sm.DisplayName : (opt.AsrModelStream ?? "");
			if (easrcompute.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode cm)
				opt.AsrCompute = cm.ToString();
			if (easrlang.SelectedItem is ComboBoxItem li && li.Tag is string ls)
				opt.AsrLang = string.IsNullOrWhiteSpace(ls) ? "auto" : ls;
			opt.AsrItn = casritn.IsChecked == true;
			if (easrsource.SelectedItem is ComboBoxItem si && si.Tag is string ss)
				opt.AsrAudioSource = string.IsNullOrWhiteSpace(ss) ? "Mic" : ss;
			AppConfig.Save(opt);
		}
		catch (Exception ex) {
			CaptureLog.Ex("saveasrprefs", ex);
		}
	}

	AsrAudioSource asrcursource() {
		if (easrsource?.SelectedItem is ComboBoxItem it && it.Tag is string s)
			return AsrLiveCapture.ParseSource(s);
		return AsrLiveCapture.ParseSource(opt.AsrAudioSource);
	}

	void asropenfile() {
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = "选择音频或视频文件",
			Filter =
				"音视频|*.wav;*.mp3;*.flac;*.m4a;*.ogg;*.opus;*.wma;*.aac;*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.flv;*.ts;*.mpeg;*.mpg|" +
				"音频|*.wav;*.mp3;*.flac;*.m4a;*.ogg;*.opus;*.wma;*.aac|" +
				"视频|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.flv;*.ts;*.mpeg;*.mpg|" +
				"所有文件|*.*",
			CheckFileExists = true,
		};
		if (ofd.ShowDialog(this) != true) return;
		_ = asrloadpathasync(ofd.FileName);
	}

	async Task asrloadpathasync(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
			lbasrstatus.Text = "文件不存在";
			return;
		}
		var isVideo = AsrAudio.IsVideoPath(path);
		if (isVideo && !FeaturePrompt.EnsureFfmpeg(this)) {
			lbasrstatus.Text = "未安装 FFmpeg，无法提取视频音轨";
			return;
		}
		setasruiBusy(true);
		try {
			lbasrstatus.Text = isVideo ? "正在用 FFmpeg DLL 提取音轨…" : "正在加载音频…";
			lbasrfile.Text = Path.GetFileName(path) + " · 加载中…";
			var (samples, sr) = await Task.Run(() => AsrAudio.LoadMedia(path)).ConfigureAwait(true);
			asrPendingSamples = samples;
			asrPendingSr = sr;
			asrPendingPath = path;
			var sec = samples.Length / (double)Math.Max(1, sr);
			var kind = isVideo ? "视频" : "音频";
			lbasrfile.Text = $"{Path.GetFileName(path)} · {kind} · {sr}Hz · {sec:0.00}s";
			lbasrstatus.Text = "已加载，可点「识别」";
		}
		catch (Exception ex) {
			asrPendingSamples = null;
			asrPendingPath = null;
			lbasrfile.Text = "未选择文件";
			lbasrstatus.Text = "打开失败: " + ex.Message;
			MessageBox.Show(this, ex.Message, "打开音视频", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		finally {
			setasruiBusy(false);
		}
	}

	void asrtogglerec() {
		if (asrCap != null && asrCap.IsRecording) {
			_ = asrstoprecandrunasync();
			return;
		}
		if (asrLiveOn) {
			lbasrstatus.Text = "实时字幕进行中，请先停止";
			return;
		}
		if (asrVoice != null && asrVoice.IsActive) {
			lbasrstatus.Text = "语音输入进行中，请先结束听写";
			return;
		}
		try {
			var src = asrcursource();
			asrCap?.Dispose();
			asrCap = new AsrLiveCapture(src, 16000);
			asrCap.Start();
			basrrec.Content = "结束并识别";
			basrstop.IsEnabled = true;
			basrrun.IsEnabled = false;
			basrlive.IsEnabled = false;
			easrsource.IsEnabled = false;
			var label = AsrLiveCapture.SourceLabel(src);
			lbasrstatus.Text = $"录音中（{label}）… 再点「结束并识别」或「停止录音」";
			lbasrfile.Text = $"{label}录音中…";
			saveasrprefs();
		}
		catch (Exception ex) {
			lbasrstatus.Text = "无法开始录音: " + ex.Message;
			MessageBox.Show(this, ex.Message, "录音", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	async Task asrstoprecandrunasync() {
		float[] samples = null;
		var sr = 16000;
		var srcLabel = "录音";
		try {
			if (asrCap != null) {
				srcLabel = AsrLiveCapture.SourceLabel(asrCap.Source);
				samples = asrCap.Stop();
				sr = asrCap.SampleRate;
				asrCap.Dispose();
				asrCap = null;
			}
		}
		catch (Exception ex) {
			lbasrstatus.Text = "停止录音失败: " + ex.Message;
		}
		basrrec.Content = "录音";
		basrstop.IsEnabled = false;
		basrrun.IsEnabled = true;
		basrlive.IsEnabled = true;
		easrsource.IsEnabled = true;
		if (samples == null || samples.Length < sr / 10) {
			lbasrstatus.Text = "录音过短或为空";
			lbasrfile.Text = "未选择文件";
			return;
		}
		asrPendingSamples = samples;
		asrPendingSr = sr;
		asrPendingPath = null;
		var sec = samples.Length / (double)sr;
		lbasrfile.Text = $"{srcLabel}录音 · {sr}Hz · {sec:0.00}s";
		await asrrunasync();
	}

	// ───────── 系统实时字幕 ─────────

	void asrtogglelive() {
		if (asrLiveBusy) {
			lbasrstatus.Text = "实时字幕忙，请稍候…";
			return;
		}
		if (asrLiveOn) {
			stopasrlive(finalFlush: true);
			return;
		}
		startasrlive();
	}

	void startasrlive() {
		if (asrCap != null && asrCap.IsRecording) {
			lbasrstatus.Text = "正在录音，请先结束录音";
			return;
		}
		if (asrVoice != null && asrVoice.IsActive) {
			lbasrstatus.Text = "语音输入进行中，请先结束听写";
			return;
		}
		if (asrtRunning) {
			lbasrstatus.Text = "字幕批量进行中，无法启动实时字幕";
			return;
		}
		if (!FeaturePrompt.EnsureSherpa(this)) {
			lbasrstatus.Text = "未安装 Sherpa 运行库";
			return;
		}

		var wantStream = asrlivewantstream();
		if (wantStream)
			startasrlivestream();
		else
			startasrliveoffline();
	}

	void startasrlivestream() {
		if (asrStreamEngine == null) {
			lbasrstatus.Text = "流式 ASR 引擎不可用";
			return;
		}
		if (!tryresolvestreammodel(out var streamModel) || streamModel == null) {
			if (FeaturePrompt.EnsureAsrModels(this)) {
				try { scanasrmodels(); fillasrmodels(); } catch { }
				tryresolvestreammodel(out streamModel);
			}
			if (streamModel == null) {
				lbasrstatus.Text = "请选择流式模型（语音识别页「流式」栏）";
				MessageBox.Show(this,
					"实时字幕当前为流式模式，需要流式模型（Online Zipformer 等）。\n请在「安装功能」安装，或在参数设置中改选离线模型。",
					"系统实时字幕", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
		}

		var src = asrcursource();
		var compute = asrcurcompute();
		var modelCopy = streamModel;
		var srcLabel = AsrLiveCapture.SourceLabel(src);
		asrLiveBusy = true;
		lbasrstatus.Text = $"实时字幕 · 加载流式模型…（{srcLabel}）";
		basrlive.IsEnabled = false;
		saveasrprefs();

		Task.Run(() => {
			lock (asrStreamGate) {
				asrStreamEngine.Mode = compute;
				asrStreamEngine.LoadModel(modelCopy);
			}
		}).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				try {
					if (t.IsFaulted) {
						var msg = t.Exception?.GetBaseException()?.Message ?? "加载失败";
						lbasrstatus.Text = "实时字幕启动失败: " + msg;
						return;
					}
					if (asrStreamEngine == null || !asrStreamEngine.IsLoaded) {
						lbasrstatus.Text = "流式模型未加载";
						return;
					}

					var sr = asrStreamEngine.FeatSampleRate > 0 ? asrStreamEngine.FeatSampleRate : 16000;
					asrLiveCap?.Dispose();
					asrLiveCap = new AsrLiveCapture(src, sr);
					var q = new System.Collections.Concurrent.ConcurrentQueue<float[]>();
					asrLiveCap.SamplesAvailable += chunk => {
						if (chunk != null && chunk.Length > 0) q.Enqueue(chunk);
					};
					asrLiveCap.Start(streamOnly: true);

					lock (asrLiveTextGate) {
						asrLiveLines.Clear();
						asrLivePartial = "";
					}
					// 识别结果框：保留旧内容，新会话另起一段
					var prev = easrtext.Text ?? "";
					if (prev.Length > 0 && !prev.EndsWith("\n") && !prev.EndsWith("\r"))
						prev += "\n";
					asrLiveTextPrefix = prev;
					easrtext.Text = prev;
					lastAsrLiveUiTick = 0;
					asrLiveCts = new CancellationTokenSource();
					var ct = asrLiveCts.Token;
					var eng = asrStreamEngine;
					var sampleRate = sr;

					asrLiveTask = Task.Run(() => runasrlivestream(eng, q, sampleRate, ct), ct);
					asrLiveOn = true;
					var deviceLabel = asrproviderlabel(asrStreamEngine?.Provider);
					showasrcaptionosd();
					basrlive.Content = "停止字幕";
					basrlive.IsEnabled = true;
					basrrec.IsEnabled = false;
					basrstop.IsEnabled = false;
					basrrun.IsEnabled = false;
					basropen.IsEnabled = false;
					easrsource.IsEnabled = false;
					// 计算设备仅状态栏；桌面字幕窗不显示
					lbasrfile.Text = $"实时字幕 · {srcLabel}";
					var hk = string.IsNullOrWhiteSpace(opt.HotkeyLiveCaption)
						? "" : opt.HotkeyLiveCaption.Trim();
					var hkTip = string.IsNullOrEmpty(hk) ? "点「停止字幕」结束" : $"再按 {hk} 或点「停止字幕」结束";
					lbasrstatus.Text =
						$"实时字幕中（{srcLabel} · {deviceLabel}）… {hkTip}";
					CaptureLog.Info(
						$"AsrLive start src={src} model={modelCopy.DisplayName} sr={sr} device={deviceLabel} provider={asrStreamEngine?.Provider}");
				}
				catch (Exception ex) {
					CaptureLog.Ex("startasrlive", ex);
					lbasrstatus.Text = "实时字幕启动失败: " + ex.Message;
					try { asrLiveCap?.Dispose(); } catch { }
					asrLiveCap = null;
					MessageBox.Show(this, ex.Message, "系统实时字幕", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
				finally {
					asrLiveBusy = false;
					if (!asrLiveOn) {
						basrlive.IsEnabled = true;
						basrlive.Content = "系统实时字幕";
					}
				}
			}));
		}, TaskScheduler.Default);
	}

	void startasrliveoffline() {
		if (asrEngine == null) {
			lbasrstatus.Text = "离线 ASR 引擎不可用";
			return;
		}
		if (!tryresolveofflinemodel(out var offlineModel) || offlineModel == null) {
			if (FeaturePrompt.EnsureAsrModels(this)) {
				try { scanasrmodels(); fillasrmodels(); } catch { }
				tryresolveofflinemodel(out offlineModel);
			}
			if (offlineModel == null) {
				lbasrstatus.Text = "请选择离线模型（语音识别页「离线」栏）";
				MessageBox.Show(this,
					"实时字幕当前为离线模式，需要离线模型（SenseVoice 等）。\n请在「安装功能」安装，或在参数设置中改选流式模型。",
					"系统实时字幕", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
		}

		var src = asrcursource();
		var compute = asrcurcompute();
		var modelCopy = offlineModel;
		var lang = string.IsNullOrWhiteSpace(opt.AsrLang) ? "auto" : opt.AsrLang;
		var useItn = opt.AsrItn;
		var srcLabel = AsrLiveCapture.SourceLabel(src);
		asrLiveBusy = true;
		lbasrstatus.Text = $"实时字幕 · 加载离线模型…（{srcLabel}）";
		basrlive.IsEnabled = false;
		saveasrprefs();

		Task.Run(() => {
			lock (asrEngineGate) {
				asrEngine.Mode = compute;
				asrEngine.LoadModel(modelCopy, lang, useItn);
			}
		}).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				try {
					if (t.IsFaulted) {
						var msg = t.Exception?.GetBaseException()?.Message ?? "加载失败";
						lbasrstatus.Text = "实时字幕启动失败: " + msg;
						return;
					}
					if (asrEngine == null || !asrEngine.IsLoaded) {
						lbasrstatus.Text = "离线模型未加载";
						return;
					}

					var sr = asrEngine.FeatSampleRate > 0 ? asrEngine.FeatSampleRate : 16000;
					asrLiveCap?.Dispose();
					asrLiveCap = new AsrLiveCapture(src, sr);
					var q = new System.Collections.Concurrent.ConcurrentQueue<float[]>();
					asrLiveCap.SamplesAvailable += chunk => {
						if (chunk != null && chunk.Length > 0) q.Enqueue(chunk);
					};
					asrLiveCap.Start(streamOnly: true);

					lock (asrLiveTextGate) {
						asrLiveLines.Clear();
						asrLivePartial = "";
					}
					var prev = easrtext.Text ?? "";
					if (prev.Length > 0 && !prev.EndsWith("\n") && !prev.EndsWith("\r"))
						prev += "\n";
					asrLiveTextPrefix = prev;
					easrtext.Text = prev;
					lastAsrLiveUiTick = 0;
					asrLiveCts = new CancellationTokenSource();
					var ct = asrLiveCts.Token;
					var sampleRate = sr;

					asrLiveTask = Task.Run(() => runasrliveoffline(q, sampleRate, ct), ct);
					asrLiveOn = true;
					var deviceLabel = asrproviderlabel(asrEngine?.Provider);
					showasrcaptionosd();
					basrlive.Content = "停止字幕";
					basrlive.IsEnabled = true;
					basrrec.IsEnabled = false;
					basrstop.IsEnabled = false;
					basrrun.IsEnabled = false;
					basropen.IsEnabled = false;
					easrsource.IsEnabled = false;
					lbasrfile.Text = $"实时字幕 · 离线 · {srcLabel}";
					var hk = string.IsNullOrWhiteSpace(opt.HotkeyLiveCaption)
						? "" : opt.HotkeyLiveCaption.Trim();
					var hkTip = string.IsNullOrEmpty(hk) ? "点「停止字幕」结束" : $"再按 {hk} 或点「停止字幕」结束";
					lbasrstatus.Text =
						$"实时字幕中（离线 · {srcLabel} · {deviceLabel}）… {hkTip}";
					CaptureLog.Info(
						$"AsrLive offline start src={src} model={modelCopy.DisplayName} sr={sr} device={deviceLabel}");
				}
				catch (Exception ex) {
					CaptureLog.Ex("startasrliveoffline", ex);
					lbasrstatus.Text = "实时字幕启动失败: " + ex.Message;
					try { asrLiveCap?.Dispose(); } catch { }
					asrLiveCap = null;
					MessageBox.Show(this, ex.Message, "系统实时字幕", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
				finally {
					asrLiveBusy = false;
					if (!asrLiveOn) {
						basrlive.IsEnabled = true;
						basrlive.Content = "系统实时字幕";
					}
				}
			}));
		}, TaskScheduler.Default);
	}

	void runasrliveoffline(
		System.Collections.Concurrent.ConcurrentQueue<float[]> q,
		int sampleRate,
		CancellationToken ct) {
		var utt = new List<float>();
		var spoke = false;
		var sil = 0;
		var silNeed = Math.Max(sampleRate * 7 / 10, 1);
		var minUtt = Math.Max(sampleRate * 4 / 10, 1);
		var maxUtt = sampleRate * 12;
		try {
			while (!ct.IsCancellationRequested) {
				if (!q.TryDequeue(out var chunk)) {
					Thread.Sleep(10);
					continue;
				}
				var list = new List<float>(chunk);
				while (q.TryDequeue(out var more))
					list.AddRange(more);
				var samples = list.ToArray();
				utt.AddRange(samples);
				var e = asrrms(samples);
				if (e >= 0.012f) {
					spoke = true;
					sil = 0;
				}
				else if (spoke) {
					sil += samples.Length;
				}
				if ((spoke && sil >= silNeed && utt.Count >= minUtt) || utt.Count >= maxUtt)
					asrliveofflineflush(utt, sampleRate, ref spoke, ref sil);
			}
			if (utt.Count >= minUtt)
				asrliveofflineflush(utt, sampleRate, ref spoke, ref sil);
			pushasrliveui(force: true);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			CaptureLog.Ex("runasrliveoffline", ex);
			try {
				Dispatcher.BeginInvoke(new Action(() => {
					lbasrstatus.Text = "实时字幕出错: " + ex.Message;
				}));
			}
			catch { }
		}
	}

	void asrliveofflineflush(List<float> utt, int sampleRate, ref bool spoke, ref int sil) {
		if (utt == null || utt.Count == 0) {
			spoke = false;
			sil = 0;
			return;
		}
		var wave = utt.ToArray();
		utt.Clear();
		spoke = false;
		sil = 0;
		try {
			lock (asrLiveTextGate) asrLivePartial = "识别中…";
			pushasrliveui(force: true);
			string raw = null;
			if (asrEngine != null) {
				lock (asrEngineGate)
					raw = asrEngine.Recognize(wave, sampleRate);
			}
			var done = asrlivefinishline(raw);
			if (done.Length > 0) {
				lock (asrLiveTextGate) {
					asrLiveLines.Add(done);
					asrLivePartial = "";
				}
			}
			else {
				lock (asrLiveTextGate) asrLivePartial = "";
			}
			pushasrliveui(force: true);
		}
		catch (Exception ex) {
			CaptureLog.Ex("asrliveofflineflush", ex);
			lock (asrLiveTextGate) asrLivePartial = "";
			pushasrliveui(force: true);
		}
	}

	static float asrrms(float[] samples) {
		if (samples == null || samples.Length == 0) return 0;
		double s = 0;
		foreach (var x in samples)
			s += x * x;
		return (float)Math.Sqrt(s / samples.Length);
	}

	/// <summary>实时字幕成句：可选润色，再按设置补句末标点。</summary>
	string asrlivefinishline(string raw) {
		var text = AsrTextNorm.Postprocess((raw ?? "").Trim());
		if (text.Length == 0) return "";
		if (opt.AsrLivePolish && AsrLlmClient.IsConfigured(opt)) {
			try {
				lock (asrLiveTextGate) asrLivePartial = "润色中… " + text;
				pushasrliveui(force: true);
				string ctx;
				lock (asrLiveTextGate)
					ctx = asrLiveLines.Count == 0 ? "" : string.Join("\n", asrLiveLines);
				var polished = AsrLlmClient.Polish(opt, text, ctx);
				if (!string.IsNullOrWhiteSpace(polished))
					text = AsrTextNorm.Postprocess(polished.Trim());
			}
			catch (Exception ex) {
				CaptureLog.Ex("asrlive polish", ex);
			}
		}
		if (opt.AsrLiveSplit)
			text = AsrTextNorm.EnsureSentenceEnd(text);
		return text;
	}

	void runasrlivestream(
		AsrStreamEngine eng,
		System.Collections.Concurrent.ConcurrentQueue<float[]> q,
		int sampleRate,
		CancellationToken ct) {
		SherpaOnnx.OnlineStream stream = null;
		try {
			lock (asrStreamGate)
				stream = eng.CreateStream();
			var lastPartial = "";
			while (!ct.IsCancellationRequested) {
				if (!q.TryDequeue(out var chunk)) {
					Thread.Sleep(10);
					continue;
				}
				var list = new List<float>(chunk);
				while (q.TryDequeue(out var more))
					list.AddRange(more);
				var samples = list.ToArray();

				string partial = null;
				var hitEnd = false;
				string finalText = null;
				lock (asrStreamGate) {
					if (stream == null) continue;
					eng.AcceptAndDecode(stream, samples, sampleRate);
					partial = eng.GetText(stream);
					if (eng.IsEndpoint(stream)) {
						hitEnd = true;
						finalText = partial;
						eng.Reset(stream);
					}
				}

				if (!string.IsNullOrEmpty(partial)
					&& !string.Equals(partial, lastPartial, StringComparison.Ordinal)) {
					lastPartial = partial;
					var show = AsrTextNorm.Postprocess(partial);
					lock (asrLiveTextGate) asrLivePartial = show;
					pushasrliveui(force: false);
				}
				if (hitEnd) {
					var done = asrlivefinishline(finalText);
					if (done.Length > 0) {
						lock (asrLiveTextGate) {
							asrLiveLines.Add(done);
							asrLivePartial = "";
						}
						lastPartial = "";
						pushasrliveui(force: true);
					}
					else {
						lock (asrLiveTextGate) asrLivePartial = "";
						lastPartial = "";
					}
				}
			}

			// 收尾
			try {
				lock (asrStreamGate) {
					if (stream != null) {
						eng.InputFinished(stream);
						var text = eng.GetText(stream);
						var done = asrlivefinishline(text);
						if (done.Length > 0) {
							lock (asrLiveTextGate) {
								asrLiveLines.Add(done);
								asrLivePartial = "";
							}
						}
					}
				}
			}
			catch { }
			pushasrliveui(force: true);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			CaptureLog.Ex("runasrlivestream", ex);
			try {
				Dispatcher.BeginInvoke(new Action(() => {
					lbasrstatus.Text = "实时字幕出错: " + ex.Message;
				}));
			}
			catch { }
		}
		finally {
			try { stream?.Dispose(); } catch { }
		}
	}

	void pushasrliveui(bool force) {
		var now = Environment.TickCount;
		if (!force && lastAsrLiveUiTick != 0 && now - lastAsrLiveUiTick < 80)
			return;
		lastAsrLiveUiTick = now;
		List<string> linesCopy;
		string partial;
		lock (asrLiveTextGate) {
			linesCopy = asrLiveLines.Count > 0 ? asrLiveLines.ToList() : new List<string>();
			partial = asrLivePartial ?? "";
		}
		try {
			Dispatcher.BeginInvoke(new Action(() => {
				if (!asrLiveOn && !force) return;
				// 识别结果：会话前缀 + 各行
				var body = string.Join("\n", linesCopy);
				if (!string.IsNullOrEmpty(partial)) {
					if (body.Length > 0) body += "\n";
					body += partial;
				}
				// 保留会话前已有文本：用 marker 不够稳，直接整框同步当前会话段
				// 简化：实时会话中结果框只显示本次会话内容；停止时若有旧内容已在 start 时换行分隔
				// 实际上 start 时已把旧文保留并加换行，这里改为追加会话：
				// 用 prefix 保存 start 时的文本
				var prefix = asrLiveTextPrefix ?? "";
				var show = prefix + body;
				easrtext.Text = show;
				try { easrtext.CaretIndex = easrtext.Text.Length; } catch { }
				try { easrtext.ScrollToEnd(); } catch { }
				try {
					if (asrLiveOsd != null && asrLiveOsd.IsVisible)
						asrLiveOsd.SetContent(linesCopy, partial);
				}
				catch { }
			}));
		}
		catch { }
	}

	string asrLiveTextPrefix = "";

	/// <summary>流式/离线引擎 Provider → 界面标签。</summary>
	static string asrproviderlabel(string provider) {
		var p = (provider ?? "").Trim().ToLowerInvariant();
		if (p is "cuda" or "gpu" or "nvidia") return "GPU";
		if (p is "dml" or "directml" or "igpu") return "核显";
		if (string.IsNullOrEmpty(p) || p == "cpu") return "CPU";
		return p.ToUpperInvariant();
	}

	void showasrcaptionosd() {
		try {
			opt.AsrCaption ??= new AsrCaptionStyle();
			if (asrLiveOsd == null) {
				asrLiveOsd = new AsrCaptionOsdWindow(opt.AsrCaption, () => {
					try { AppConfig.Save(opt); } catch { }
				});
				asrLiveOsd.Closed += (_, _) => { asrLiveOsd = null; };
			}
			asrLiveOsd.Clear();
			asrLiveOsd.ApplyStyle();
			if (!asrLiveOsd.IsVisible)
				asrLiveOsd.Show();
		}
		catch (Exception ex) {
			CaptureLog.Ex("showasrcaptionosd", ex);
		}
	}

	void hideasrcaptionosd(bool dispose) {
		try {
			if (asrLiveOsd == null) return;
			if (dispose) {
				try { asrLiveOsd.Close(); } catch { }
				asrLiveOsd = null;
			}
			else {
				try { asrLiveOsd.Hide(); } catch { }
			}
		}
		catch { }
	}

	void openasrcaptionstyle() {
		try {
			opt.AsrCaption ??= new AsrCaptionStyle();
			var previewOnly = !asrLiveOn;
			if (asrLiveOsd == null) {
				asrLiveOsd = new AsrCaptionOsdWindow(opt.AsrCaption, () => {
					try { AppConfig.Save(opt); } catch { }
				});
				asrLiveOsd.Closed += (_, _) => { asrLiveOsd = null; };
			}
			if (previewOnly) {
				asrLiveOsd.SetContent(
					new[] { "桌面实时字幕预览", "可拖动位置，拖边缘改大小" },
					"流式识别中…");
			}
			if (!asrLiveOsd.IsVisible)
				asrLiveOsd.Show();
			AsrCaptionStyleDialog.Open(opt.AsrCaption, asrLiveOsd, this,
				applyLive: () => {
					try {
						asrLiveOsd?.ApplyStyle();
						AppConfig.Save(opt);
					}
					catch { }
				},
				onClosed: () => {
					// 仅预览时关样式窗后收起 OSD；实时字幕中保持
					if (!asrLiveOn)
						hideasrcaptionosd(dispose: true);
				});
			lbasrstatus.Text = "桌面字幕样式…";
		}
		catch (Exception ex) {
			CaptureLog.Ex("openasrcaptionstyle", ex);
			MessageBox.Show(this, ex.Message, "字幕样式", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void stopasrlive(bool finalFlush) {
		if (!asrLiveOn && asrLiveCap == null) return;
		asrLiveBusy = true;
		try {
			asrLiveOn = false;
			try { asrLiveCts?.Cancel(); } catch { }
			try {
				if (asrLiveCap != null) {
					asrLiveCap.Stop();
					asrLiveCap.Dispose();
				}
			}
			catch { }
			asrLiveCap = null;
			try { asrLiveTask?.Wait(2000); } catch { }
			asrLiveTask = null;
			try { asrLiveCts?.Dispose(); } catch { }
			asrLiveCts = null;

			if (finalFlush) {
				List<string> linesCopy;
				string partial;
				lock (asrLiveTextGate) {
					linesCopy = asrLiveLines.ToList();
					partial = asrLivePartial ?? "";
				}
				if (!string.IsNullOrEmpty(partial)
					&& !partial.StartsWith("识别中")
					&& !partial.StartsWith("润色中")) {
					var p = opt.AsrLiveSplit
						? AsrTextNorm.EnsureSentenceEnd(partial.Trim())
						: partial.Trim();
					if (p.Length > 0) linesCopy.Add(p);
				}
				var body = string.Join("\n", linesCopy);
				easrtext.Text = (asrLiveTextPrefix ?? "") + body;
				try { easrtext.ScrollToEnd(); } catch { }
				try { asrLiveOsd?.SetContent(linesCopy, ""); } catch { }
			}

			// 保留 OSD 片刻显示最后内容，或直接隐藏
			hideasrcaptionosd(dispose: false);
			try { asrLiveOsd?.Hide(); } catch { }

			basrlive.Content = "系统实时字幕";
			basrlive.IsEnabled = true;
			basrrec.IsEnabled = true;
			basrrun.IsEnabled = true;
			basropen.IsEnabled = true;
			easrsource.IsEnabled = true;
			lbasrstatus.Text = finalFlush ? "实时字幕已结束" : "实时字幕已停止";
			CaptureLog.Info("AsrLive stop");
			try { AppConfig.Save(opt); } catch { }
		}
		finally {
			asrLiveBusy = false;
		}
	}

	async Task asrrunasync() {
		if (asrLiveOn) {
			lbasrstatus.Text = "实时字幕进行中，请先停止";
			return;
		}
		if (asrVoice != null && asrVoice.IsActive) {
			lbasrstatus.Text = "语音输入进行中，请先结束听写";
			return;
		}
		if (!FeaturePrompt.EnsureSherpa(this)) {
			lbasrstatus.Text = "未安装 Sherpa 运行库";
			return;
		}
		if (asrEngine == null) {
			lbasrstatus.Text = "ASR 引擎不可用";
			return;
		}
		var model = easrmodel.SelectedItem as AsrModelInfo;
		if (model == null) {
			if (FeaturePrompt.EnsureAsrModels(this)) {
				try { scanasrmodels(); fillasrmodels(); } catch { }
				model = easrmodel.SelectedItem as AsrModelInfo;
			}
			if (model == null) {
				lbasrstatus.Text = "请选择离线模型或安装到 asrmodels";
				return;
			}
		}
		if (model.IsStreaming) {
			lbasrstatus.Text = "离线模型列表异常（选到了流式包），请刷新后重选";
			return;
		}
		if (asrPendingSamples == null || asrPendingSamples.Length == 0) {
			lbasrstatus.Text = "请先打开音视频文件或录音";
			return;
		}

		var lang = "auto";
		if (easrlang.SelectedItem is ComboBoxItem li && li.Tag is string ls)
			lang = ls;
		var useItn = casritn.IsChecked == true;
		var compute = TtsComputeMode.Auto;
		if (easrcompute.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode m)
			compute = m;
		var samples = asrPendingSamples;
		var sr = asrPendingSr;
		var modelCopy = model;

		setasruiBusy(true);
		var t0 = Environment.TickCount;
		try {
			saveasrprefs();
			lbasrstatus.Text = "加载模型 / 识别中…";
			string text = null;
			string provider = "cpu";
			string fallback = null;
			var loadMs = 0;
			var recMs = 0;
			await Task.Run(() => {
				var tLoad = Environment.TickCount;
				lock (asrEngineGate) {
					asrEngine.Mode = compute;
					asrEngine.LoadModel(modelCopy, lang, useItn);
					loadMs = Math.Max(0, Environment.TickCount - tLoad);
					provider = asrEngine.Provider;
					fallback = asrEngine.GpuFallbackReason;
					var tRec = Environment.TickCount;
					text = asrEngine.Recognize(samples, sr);
					recMs = Math.Max(0, Environment.TickCount - tRec);
				}
				// wetext ITN + 逐位数字
				if (!string.IsNullOrEmpty(text))
					text = AsrTextNorm.Postprocess(text);
			}).ConfigureAwait(true);

			easrtext.Text = text ?? "";
			var total = Math.Max(0, Environment.TickCount - t0);
			var audioSec = samples.Length / (double)Math.Max(1, sr);
			var tip = string.IsNullOrEmpty(fallback) ? "" : " · GPU回退: " + fallback;
			lbasrstatus.Text =
				$"完成 · {provider} · 音频 {audioSec:0.00}s · 加载 {formatms(loadMs)} · 识别 {formatms(recMs)} · 合计 {formatms(total)}{tip}";
			if (!string.IsNullOrEmpty(fallback))
				CaptureLog.Info("ASR GPU fallback: " + fallback);
			if (string.IsNullOrWhiteSpace(text))
				lbasrstatus.Text += " · （无文本）";
		}
		catch (Exception ex) {
			var ms = Math.Max(0, Environment.TickCount - t0);
			lbasrstatus.Text = $"识别失败 ({formatms(ms)}): {ex.Message}";
			MessageBox.Show(this, ex.Message, "语音识别", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		finally {
			setasruiBusy(false);
		}
	}

	// ───────── 批量字幕 ─────────

	void asrtoutdirmode() {
		var same = casrtsamedir.IsChecked == true;
		easrtoutdir.IsEnabled = !same && !asrtRunning;
		basrtbrowse.IsEnabled = !same && !asrtRunning;
	}

	void asrtbrowseoutdir() {
		using var dlg = new System.Windows.Forms.FolderBrowserDialog {
			Description = "选择字幕输出目录",
			ShowNewFolderButton = true,
		};
		if (!string.IsNullOrWhiteSpace(easrtoutdir.Text) && Directory.Exists(easrtoutdir.Text))
			dlg.SelectedPath = easrtoutdir.Text;
		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
		easrtoutdir.Text = dlg.SelectedPath;
	}

	void asrtaddfiles() {
		if (asrtRunning) return;
		var ofd = new Microsoft.Win32.OpenFileDialog {
			Title = "添加音视频（可多选）",
			Filter =
				"音视频|*.wav;*.mp3;*.flac;*.m4a;*.ogg;*.opus;*.wma;*.aac;*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.flv;*.ts;*.mpeg;*.mpg|" +
				"所有文件|*.*",
			Multiselect = true,
			CheckFileExists = true,
		};
		if (ofd.ShowDialog(this) != true) return;
		asrtaddpaths(ofd.FileNames.ToList());
	}

	void asrtaddpaths(IEnumerable<string> paths) {
		if (paths == null || asrtRunning) return;
		var n = 0;
		foreach (var p in paths) {
			if (string.IsNullOrWhiteSpace(p) || !File.Exists(p) || !AsrAudio.IsMediaPath(p))
				continue;
			var full = Path.GetFullPath(p);
			if (asrtQueue.Any(x => string.Equals(x.Path, full, StringComparison.OrdinalIgnoreCase)))
				continue;
			asrtQueue.Add(new AsrSrtQueueItem(full));
			n++;
		}
		asrtrefreshcount();
		if (n > 0)
			lbasrtdetail.Text = $"已添加 {n} 个文件，共 {asrtQueue.Count} 个。";
	}

	void asrtremoveselected() {
		if (asrtRunning) return;
		var sel = easrtlist.SelectedItems.Cast<AsrSrtQueueItem>().ToList();
		foreach (var it in sel)
			asrtQueue.Remove(it);
		asrtrefreshcount();
	}

	void asrtrefreshcount() {
		lbasrtcount.Text = asrtQueue.Count + " 个";
	}

	string asrtresolveoutdir(string srcPath) {
		if (casrtsamedir.IsChecked == true) {
			var d = Path.GetDirectoryName(srcPath);
			return string.IsNullOrEmpty(d) ? "." : d;
		}
		var custom = (easrtoutdir.Text ?? "").Trim();
		if (string.IsNullOrEmpty(custom))
			throw new InvalidOperationException("请选择输出目录，或勾选「输出到源文件同目录」");
		if (!Directory.Exists(custom))
			Directory.CreateDirectory(custom);
		return custom;
	}

	string asrtoutpath(string srcPath) {
		var dir = asrtresolveoutdir(srcPath);
		return Path.Combine(dir, Path.GetFileNameWithoutExtension(srcPath) + ".srt");
	}

	async Task asrtbatchasync() {
		if (asrtRunning) return;
		if (asrVoice != null && asrVoice.IsActive) {
			lbasrstatus.Text = "语音输入进行中，请先结束听写";
			return;
		}
		if (asrEngine == null) {
			lbasrstatus.Text = "ASR 引擎不可用";
			return;
		}
		var model = easrmodel.SelectedItem as AsrModelInfo;
		if (model == null) {
			if (FeaturePrompt.EnsureAsrModels(this)) {
				try { scanasrmodels(); fillasrmodels(); } catch { }
				model = easrmodel.SelectedItem as AsrModelInfo;
			}
			if (model == null) {
				lbasrstatus.Text = "请选择离线模型或安装到 asrmodels";
				return;
			}
		}
		if (model.IsStreaming) {
			lbasrstatus.Text = "离线模型列表异常（选到了流式包），请刷新后重选";
			return;
		}
		if (asrtQueue.Count == 0) {
			lbasrstatus.Text = "请先添加音视频文件";
			lbasrtdetail.Text = "列表为空，请拖入或点「添加…」。";
			return;
		}
		if (casrtsamedir.IsChecked != true) {
			var d = (easrtoutdir.Text ?? "").Trim();
			if (string.IsNullOrEmpty(d)) {
				MessageBox.Show(this, "请选择输出目录，或勾选「输出到源文件同目录」。",
					"生成字幕", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
		}

		var lang = "auto";
		if (easrlang.SelectedItem is ComboBoxItem li && li.Tag is string ls)
			lang = ls;
		var useItn = casritn.IsChecked == true;
		var compute = TtsComputeMode.Auto;
		if (easrcompute.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode m)
			compute = m;
		var modelCopy = model;
		var jobs = asrtQueue.ToList();

		saveasrprefs();
		asrtRunning = true;
		asrtCts = new CancellationTokenSource();
		var ct = asrtCts.Token;
		setasrtbusy(true);
		setasruiBusy(true);
		basrtstop.Content = "中止";
		basrtstop.IsEnabled = true;

		var ok = 0;
		var fail = 0;
		var t0 = Environment.TickCount;
		try {
			// 重置状态
			foreach (var j in jobs) {
				j.Status = "等待";
				j.Detail = "";
				j.FilePct = 0;
			}
			pasrtfile.Value = 0;
			pasrttotal.Value = 0;
			lbasrtfilepct.Text = "0%";
			lbasrttotalpct.Text = $"0 / {jobs.Count}";
			lbasrtlatest.Text = "最新：—";
			lbasrstatus.Text = "批量生成字幕…";

			// 先加载模型一次
			lbasrtfile.Text = "加载模型…";
			lbasrtdetail.Text = "正在加载识别模型…";
			string provider = "cpu";
			await Task.Run(() => {
				ct.ThrowIfCancellationRequested();
				lock (asrEngineGate) {
					asrEngine.Mode = compute;
					asrEngine.LoadModel(modelCopy, lang, useItn);
					provider = asrEngine.Provider;
				}
			}, ct).ConfigureAwait(true);

			for (var i = 0; i < jobs.Count; i++) {
				ct.ThrowIfCancellationRequested();
				var job = jobs[i];
				easrtlist.SelectedItem = job;
				easrtlist.ScrollIntoView(job);
				job.Status = "处理中";
				job.Detail = "加载音轨…";
				job.FilePct = 0;
				lbasrtfile.Text = job.FileName;
				lbasrtdetail.Text = $"文件 {i + 1}/{jobs.Count} · 加载…";
				pasrtfile.Value = 0;
				lbasrtfilepct.Text = "0%";
				updaterasrttotal(i, jobs.Count, 0);

				try {
					var src = job.Path;
					var outPath = asrtoutpath(src);
					job.Detail = "解码…";

					await Task.Run(() => {
						ct.ThrowIfCancellationRequested();
						var (samples, sr) = AsrAudio.LoadMedia(src);
						ct.ThrowIfCancellationRequested();
						var audioSec = samples.Length / (double)Math.Max(1, sr);

						void report(AsrResult partial, double posSec, double totalSec) {
							ct.ThrowIfCancellationRequested();
							var list = AsrSrt.FromResult(partial, totalSec > 0 ? totalSec : audioSec);
							var nCue = list.Count;
							var nChar = 0;
							string latest = "";
							for (int k = 0; k < list.Count; k++) {
								var t = list[k].Text ?? "";
								nChar += t.Length;
								if (t.Length > 0) latest = t;
							}
							var pct = totalSec > 0
								? Compat.Clamp(posSec / totalSec * 100.0, 0, 100)
								: 0;
							var posShow = AsrSrt.FormatTs(posSec) + " / " + AsrSrt.FormatTs(totalSec);
							var latestShow = latest;
							try {
								Dispatcher.BeginInvoke(new Action(() => {
									job.Status = "识别中";
									job.Detail = $"{nCue} 句 · {nChar} 字 · {posShow}";
									job.FilePct = pct;
									pasrtfile.Value = pct;
									lbasrtfilepct.Text = $"{pct:0.0}% · {posShow}";
									lbasrtdetail.Text =
										$"文件 {i + 1}/{jobs.Count} · {nCue} 句 · {nChar} 字 · {posShow}";
									if (!string.IsNullOrEmpty(latestShow)) {
										var s = latestShow.Length > 80
											? latestShow.Substring(0, 80) + "…"
											: latestShow;
										lbasrtlatest.Text = "最新：" + s;
									}
									updaterasrttotal(i, jobs.Count, pct);
									lbasrstatus.Text =
										$"字幕 {i + 1}/{jobs.Count} · {job.FileName} · {pct:0}% · {provider}";
								}));
							}
							catch { }
						}

						AsrResult result;
						lock (asrEngineGate)
							result = asrEngine.RecognizeLong(samples, sr, 25f, report, ct);
						ct.ThrowIfCancellationRequested();
						var cues = AsrSrt.FromResult(result, audioSec);
						AsrSrt.Save(outPath, cues);
						var n = cues?.Count ?? 0;
						var chars = cues?.Sum(c => c.Text?.Length ?? 0) ?? 0;
						try {
							Dispatcher.BeginInvoke(new Action(() => {
								job.Status = "完成";
								job.Detail = $"{n} 句 · {chars} 字 · {Path.GetFileName(outPath)}";
								job.FilePct = 100;
								pasrtfile.Value = 100;
								lbasrtfilepct.Text = "100%";
							}));
						}
						catch { }
					}, ct).ConfigureAwait(true);

					ok++;
					updaterasrttotal(i + 1, jobs.Count, 0);
				}
				catch (OperationCanceledException) {
					job.Status = "已取消";
					job.Detail = "";
					throw;
				}
				catch (Exception ex) {
					fail++;
					job.Status = "失败";
					job.Detail = ex.Message;
					CaptureLog.Ex("asrtbatch " + job.FileName, ex);
				}
			}

			var ms = Math.Max(0, Environment.TickCount - t0);
			pasrttotal.Value = 100;
			lbasrttotalpct.Text = $"{jobs.Count} / {jobs.Count}";
			lbasrtdetail.Text = $"全部完成 · 成功 {ok} · 失败 {fail} · 用时 {formatms(ms)} · {provider}";
			lbasrstatus.Text = $"字幕批次完成 · 成功 {ok} · 失败 {fail} · {formatms(ms)}";
			lbasrtfile.Text = "—";
		}
		catch (OperationCanceledException) {
			lbasrtdetail.Text = $"已中止 · 成功 {ok} · 失败 {fail}";
			lbasrstatus.Text = "已中止批量字幕";
			foreach (var j in jobs.Where(x => x.Status is "等待" or "处理中" or "识别中")) {
				if (j.Status != "完成" && j.Status != "失败")
					j.Status = "已取消";
			}
		}
		catch (Exception ex) {
			var msg = ex is AggregateException ae && ae.InnerException != null
				? ae.InnerException.Message : ex.Message;
			if (ex is AggregateException aex && aex.InnerException is OperationCanceledException) {
				lbasrstatus.Text = "已中止批量字幕";
				lbasrtdetail.Text = $"已中止 · 成功 {ok} · 失败 {fail}";
			}
			else {
				lbasrstatus.Text = "批量字幕失败: " + msg;
				lbasrtdetail.Text = msg;
				MessageBox.Show(this, msg, "生成字幕", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
		finally {
			asrtRunning = false;
			try { asrtCts?.Dispose(); } catch { }
			asrtCts = null;
			basrtstop.Content = "中止";
			basrtstop.IsEnabled = false;
			setasrtbusy(false);
			setasruiBusy(false);
			asrtoutdirmode();
		}
	}

	void updaterasrttotal(int doneFiles, int totalFiles, double currentFilePct) {
		if (totalFiles <= 0) {
			pasrttotal.Value = 0;
			lbasrttotalpct.Text = "0 / 0";
			return;
		}
		// 已完成文件 + 当前文件内比例
		var frac = (doneFiles + currentFilePct / 100.0) / totalFiles;
		var pct = Compat.Clamp(frac * 100.0, 0, 100);
		pasrttotal.Value = pct;
		lbasrttotalpct.Text = $"{doneFiles} / {totalFiles} · {pct:0.0}%";
	}

	void setasrtbusy(bool busy) {
		basrtadd.IsEnabled = !busy;
		basrtremove.IsEnabled = !busy;
		basrtclear.IsEnabled = !busy;
		basrtstart.IsEnabled = !busy;
		casrtsamedir.IsEnabled = !busy;
		easrtlist.IsEnabled = !busy;
		if (!busy) asrtoutdirmode();
	}

	void setasruiBusy(bool busy) {
		basrrun.IsEnabled = !busy;
		basropen.IsEnabled = !busy;
		basrrec.IsEnabled = !busy;
		basrreload.IsEnabled = !busy;
		if (!asrLiveOn)
			basrlive.IsEnabled = !busy;
		if (!busy && asrCap != null && asrCap.IsRecording) {
			basrrun.IsEnabled = false;
			basrlive.IsEnabled = false;
		}
		if (!busy && asrLiveOn) {
			basrrun.IsEnabled = false;
			basrrec.IsEnabled = false;
			basropen.IsEnabled = false;
			basrlive.IsEnabled = true;
		}
	}

	void disposeAsr() {
		try { voiceHkResume?.Stop(); } catch { }
		voiceHkResume = null;
		try { asrtCts?.Cancel(); } catch { }
		try { stopasrlive(finalFlush: false); } catch { }
		try { hideasrcaptionosd(dispose: true); } catch { }
		try {
			if (asrVoice != null && asrVoice.IsActive)
				asrVoice.Stop();
		}
		catch { }
		try { asrVoice?.Dispose(); } catch { }
		asrVoice = null;
		try { hideasrvoicehud(); } catch { }
		try { asrCap?.Dispose(); } catch { }
		asrCap = null;
		try { asrLiveCap?.Dispose(); } catch { }
		asrLiveCap = null;
		try { asrStreamEngine?.Dispose(); } catch { }
		try { asrEngine?.Dispose(); } catch { }
		try { WetextItn.Shutdown(); } catch { }
	}
}
