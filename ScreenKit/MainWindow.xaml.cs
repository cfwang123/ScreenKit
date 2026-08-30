using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
namespace ScreenKit;

public partial class MainWindow : Window {
	OcrOptions opt = new();
	OcrResult last;
	QrResult lastQr;
	BitmapSource curimg;
	bool busy;
	/// <summary>当前忙碌类型：ocr / qr（仅 busy 时有意义）。</summary>
	string busyKind;
	/// <summary>当前图是否已对 OCR / 二维码各尝试识别过一次。</summary>
	bool ocrDoneForImg;
	bool qrDoneForImg;
	/// <summary>各结果 Tab 缓存的 meta 文案（切换 Tab 时恢复）。</summary>
	string ocrMetaText = "推理 — · 端到端 — | 置信度 —";
	string qrMetaText = "条码 —";
	bool panning;
	Point panstart;
	double pan0x, pan0y;
	const double ZMIN = 0.05;
	const double ZMAX = 16;

	// 文字选区：单击整行，拖选字符级；空白处平移
	// 光标位置 (line, ch)，ch ∈ [0, text.Length]，选区为文档序 [anchor, caret)
	int ancLine = -1, ancCh;
	int curLine = -1, curCh;
	bool selecting;
	/// <summary>本次按下后是否已拖动（拖动=部分选，未拖=整行）。</summary>
	bool selDragged;
	/// <summary>图上按下时的 (行, 字符) 与舞台坐标。</summary>
	int downLine, downCh;
	Point downStage;
	/// <summary>结果区按下中 / 是否已拖动 / 按下坐标。</summary>
	bool textMouseDown;
	bool textSelDragged;
	Point textDownPt;
	/// <summary>图↔文本选区互相同步时防回环。</summary>
	bool syncingSel;
	/// <summary>OCR 各行在 eresult.Text 中的起始偏移（按实际文本建，兼容 \n / \r\n）。</summary>
	int[] lineOff;
	string lineOffSrc;
	const double SEL_DRAG_PX = 4;
	// Umi 风格：50% 黑底 + 白字；选中用半透明高亮（可叠在行内局部）
	static readonly SolidColorBrush BoxFill = brush(0x80, 0x00, 0x00, 0x00);
	static readonly SolidColorBrush BoxStroke = brush(0x40, 0xFF, 0xFF, 0xFF);
	static readonly SolidColorBrush TextFg = brush(0xFF, 0xFF, 0xFF, 0xFF);
	static readonly SolidColorBrush SelFill = brush(0x99, 0x3B, 0x82, 0xF6);
	static readonly SolidColorBrush SelStroke = brush(0xE6, 0x60, 0xA5, 0xFA);

	List<ModelPack> packs = new();
	bool modelUiLoading;
	TrayIcon tray;
	GlobalHotkey hotkey;       // 主窗呼出/隐藏
	GlobalHotkey hotkeySnap;   // 截图标注
	GlobalHotkey hotkeySnapOcr;// 截图识别
	GlobalHotkey hotkeyBoard;  // 屏幕画板
	GlobalHotkey hotkeyVoice;  // 语音输入
	GlobalHotkey hotkeyLive;   // 系统实时字幕
	HttpOcrServer httpServer;
	readonly OcrRunner runner = new();
	bool forceExit;
	bool capturing; // 防止热键重入（框选/标注遮罩）
	/// <summary>同步「复制为图片/文件/路径」勾选时防重入。</summary>
	bool snapCopyUi;
	/// <summary>当前录屏 HUD；非 null 时热键截图走录制区域快照。</summary>
	RecordHud activeRecordHud;
	CancellationTokenSource ocrCts;
	/// <summary>OCR 轮次；连点截图时丢弃过期识别结果。</summary>
	int ocrGen;
	/// <summary>托盘菜单打开瞬间主窗是否可见（菜单关闭会误激活主窗，不能用点击后状态）。</summary>
	bool trayMenuMainVisible;

	public MainWindow() {
		InitializeComponent();
		trysetwindowicon();
		// 先读配置再装模型栏
		AppConfig.LoadInto(opt);
		Loc.SetFromConfig(opt.UiLang);
		syncsnapcopyopts();
		restorewindowbounds();
		// 启动时按配置清理过期截图历史
		try { ImageUtil.CleanupScreenshots(opt.ScreenshotKeepDays); } catch { }
		applydefaultmodel();
		initmodelbar();
		inittoolbar();
		applylang();
		initviewport();
		inittts();
		initasr();
		inittranslate();
		initface();
		initkeys();
		inittray();
		inithotkey();
		inithttpserver();
		// 服务模式：启动后后台预热，引擎常驻
		if (opt.ServiceMode)
			tryservicewarmup("启动预热");
		StateChanged += onstatechanged;
		Closing += onclosing;
		// 调整大小/移动后延迟写入（真正退出时也会再存）
		LocationChanged += (_, _) => scheduleboundsave();
		SizeChanged += (_, _) => scheduleboundsave();
		// 首次启动：空闲时弹出安装向导
		if (!opt.InstallPromptDone)
			Loaded += onfirstinstallprompt;
	}

	void onfirstinstallprompt(object sender, RoutedEventArgs e) {
		Loaded -= onfirstinstallprompt;
		// 等主窗完全显示后再弹
		Dispatcher.BeginInvoke(new Action(() => {
			try { showfirstinstall(); }
			catch (Exception ex) { CaptureLog.Ex("first install prompt", ex); }
		}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
	}

	void showfirstinstall() {
		if (opt.InstallPromptDone) return;
		try {
			var win = new InstallFeaturesWindow(firstRun: true);
			attachdialogowner(win);
			win.ShowDialog();
			// 刷新模型列表（与菜单「安装功能」一致）
			if (win.NeedRefresh || win.NeedRestart) {
				try {
					modelUiLoading = true;
					packs = ModelCatalog.Scan();
					epack.ItemsSource = packs;
					var pack = packs.FirstOrDefault(p =>
							string.Equals(p.Id, opt.ModelPackId, StringComparison.OrdinalIgnoreCase))
						?? packs.FirstOrDefault();
					if (pack != null) {
						epack.SelectedItem = pack;
						fillvariants(pack, opt.ModelVariant);
					}
					modelUiLoading = false;
				}
				catch (Exception ex) {
					modelUiLoading = false;
					CaptureLog.Ex("first install refresh ocr", ex);
				}
				try { scanasrmodels(); } catch (Exception ex) { CaptureLog.Ex("first install refresh asr", ex); }
				try { scanttssmodels(); } catch (Exception ex) { CaptureLog.Ex("first install refresh tts", ex); }
				try { scantrmodels(); } catch (Exception ex) { CaptureLog.Ex("first install refresh tr", ex); }
				try { scanfacemodels(); } catch (Exception ex) { CaptureLog.Ex("first install refresh face", ex); }
				try { refreshdeviceui(); } catch { }
				if (win.NeedRestart)
					setstatus("推荐组件已安装 · 请重启程序以加载 GPU/核显运行库");
				else if (win.NeedRefresh)
					setstatus("推荐组件安装完成 · 模型列表已刷新");
			}
		}
		catch (Exception ex) {
			CaptureLog.Ex("showfirstinstall", ex);
		}
		finally {
			// 无论是否安装，标记已提示，避免每次启动都弹
			opt.InstallPromptDone = true;
			try { AppConfig.Save(opt); } catch { }
		}
	}

	System.Windows.Threading.DispatcherTimer boundsSaveTimer;

	void scheduleboundsave() {
		if (forceExit) return;
		if (WindowState == WindowState.Minimized) return;
		boundsSaveTimer ??= new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromMilliseconds(600),
		};
		boundsSaveTimer.Stop();
		boundsSaveTimer.Tick -= onboundsavetick;
		boundsSaveTimer.Tick += onboundsavetick;
		boundsSaveTimer.Start();
	}

	void onboundsavetick(object sender, EventArgs e) {
		try { boundsSaveTimer?.Stop(); } catch { }
		try {
			savewindowbounds();
			AppConfig.Save(opt);
		}
		catch { }
	}

	void savewindowbounds() {
		try {
			if (WindowState == WindowState.Maximized) {
				opt.WinMax = true;
				var rb = RestoreBounds;
				if (rb.Width >= MinWidth && rb.Height >= MinHeight) {
					opt.WinW = rb.Width;
					opt.WinH = rb.Height;
					opt.WinL = rb.Left;
					opt.WinT = rb.Top;
				}
			}
			else if (WindowState == WindowState.Normal) {
				opt.WinMax = false;
				if (Width >= MinWidth && Height >= MinHeight) {
					opt.WinW = Width;
					opt.WinH = Height;
					opt.WinL = Left;
					opt.WinT = Top;
				}
			}
			// Minimized：保持上次 Normal/Max 记录
		}
		catch { }
	}

	void restorewindowbounds() {
		try {
			var w = opt.WinW;
			var h = opt.WinH;
			if (w < MinWidth || h < MinHeight) {
				// 无有效记录：居中默认大小
				WindowStartupLocation = WindowStartupLocation.CenterScreen;
				return;
			}
			WindowStartupLocation = WindowStartupLocation.Manual;
			Width = w;
			Height = h;
			// 保证至少一部分落在虚拟屏内
			var va = SystemParameters.VirtualScreenLeft;
			var vt = SystemParameters.VirtualScreenTop;
			var vw = SystemParameters.VirtualScreenWidth;
			var vh = SystemParameters.VirtualScreenHeight;
			var l = opt.WinL;
			var t = opt.WinT;
			if (double.IsNaN(l) || double.IsNaN(t)
				|| l > va + vw - 80 || t > vt + vh - 40
				|| l + w < va + 40 || t + h < vt + 40) {
				l = va + Math.Max(0, (vw - w) / 2);
				t = vt + Math.Max(0, (vh - h) / 2);
			}
			Left = l;
			Top = t;
			if (opt.WinMax)
				WindowState = WindowState.Maximized;
		}
		catch {
			WindowStartupLocation = WindowStartupLocation.CenterScreen;
		}
	}

	void trysetwindowicon() {
		try {
			var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
			if (!File.Exists(path)) return;
			Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
				new Uri(path, UriKind.Absolute));
		}
		catch { }
	}

	void inithttpserver() {
		try {
			httpServer = new HttpOcrServer(() => snapshotopt(), runner);
			// ASR/TTS 在 initasr/inittts 之后调用本方法，可注入共享引擎
			httpServer.SetServices(new HttpApiServices {
				GetOpts = () => snapshotopt(),
				OcrRunner = runner,
				AsrEngine = asrEngine,
				AsrGate = asrEngineGate,
				TtsEngine = sherpaTts,
				TtsGate = new object(),
				ScanAsr = () => AsrModelScanner.Scan(),
				ScanTts = () => TtsModelScanner.Scan(),
			});
			if (opt.HttpEnabled)
				starthttp();
		}
		catch (Exception ex) {
			setstatus(Loc.T("st.http_init_fail", ex.Message));
		}
	}

	void starthttp() {
		if (httpServer == null) return;
		try {
			httpServer.Start(opt.HttpHost, opt.HttpPort);
			setstatus(Loc.T("st.http_ok", opt.HttpHost, opt.HttpPort));
		}
		catch (Exception ex) {
			setstatus(Loc.T("st.http_fail", ex.Message));
		}
	}

	void restarthttp() {
		try { httpServer?.Stop(); } catch { }
		if (opt.HttpEnabled) starthttp();
	}

	/// <summary>供 HTTP 服务复制当前 OCR 参数（后台线程可调，勿触碰 UI）。</summary>
	OcrOptions snapshotopt() {
		// 只读 opt 字段快照；顶栏变更时 applymodelchoice 已写回 opt
		var o = opt;
		return new OcrOptions {
			ModelPackId = o.ModelPackId,
			ModelVariant = o.ModelVariant,
			ModelsDir = o.ModelsDir,
			Device = o.Device,
			DetLimitSideLen = o.DetLimitSideLen,
			DetPadding = o.DetPadding,
			DetThresh = o.DetThresh,
			DetBoxThresh = o.DetBoxThresh,
			DetUnclipRatio = o.DetUnclipRatio,
			DetUseDilation = o.DetUseDilation,
			RecImgH = o.RecImgH,
			RecMaxWidth = o.RecMaxWidth,
			RecAbsMaxWidth = o.RecAbsMaxWidth,
			RecBatchNum = o.RecBatchNum,
			UseCls = o.UseCls,
		};
	}

	// ───────── 托盘 / 热键 ─────────

	void inittray() {
		try {
			tray = new TrayIcon(this);
			// 托盘右侧快捷键文案：主窗 / 截图识别 / 截图标注 / 屏幕画板 / 语音输入
			tray.HotkeyProvider = () => (
				fmttrayhk(opt.Hotkey),
				fmttrayhk(opt.HotkeySnapOcr),
				fmttrayhk(opt.HotkeySnap),
				fmttrayhk(opt.HotkeyBoard),
				fmttrayhk(opt.HotkeyVoiceInput));
			// 记下点菜单前主窗是否真的在前台（菜单关闭会误激活隐藏的主窗）
			tray.MenuOpening += () => {
				trayMenuMainVisible = IsVisible && WindowState != WindowState.Minimized;
			};
			// 截图识别：成功后弹出主窗、切到「截图识别」页并显示结果
			tray.OcrRequested += () => Dispatcher.BeginInvoke(new Action(() => {
				// 菜单关闭瞬间可能把托盘主窗拉起：若点菜单前是隐藏的，立刻藏回再截
				if (!trayMenuMainVisible)
					keepmainhidden();
				_ = captureasync(hideMain: false, showMainAfter: true,
					mainWasVisibleOverride: trayMenuMainVisible);
			}));
			// 截图标注：与截图识别相同，不唤起主窗
			tray.SnapRequested += () => Dispatcher.BeginInvoke(new Action(() => {
				if (!trayMenuMainVisible)
					keepmainhidden();
				_ = snapannotateasync(restoreUi: false, showMainAfter: false,
					mainWasVisibleOverride: trayMenuMainVisible);
			}));
			// 屏幕画板：不唤起主窗
			tray.BoardRequested += () => Dispatcher.BeginInvoke(new Action(() => {
				if (!trayMenuMainVisible)
					keepmainhidden();
				_ = screenboardasync(restoreUi: false, showMainAfter: false,
					mainWasVisibleOverride: trayMenuMainVisible);
			}));
			tray.VoiceRequested += () => Dispatcher.BeginInvoke(new Action(() => {
				if (!trayMenuMainVisible)
					keepmainhidden();
				toggleasrvoice();
			}));
			tray.ClipboardOcrRequested += () => _ = hotkeyclipboardasync();
			tray.ClipboardAsFileRequested += () => Dispatcher.BeginInvoke(new Action(() => {
				if (tray != null) tray.showwindow();
				saveimage();
			}));
			tray.PdfRequested += () => Dispatcher.BeginInvoke(new Action(() => openpdfworkbench()));
			// 截图历史：打开 screenshots 文件夹，不唤起主窗
			tray.SnapshotsRequested += () => Dispatcher.BeginInvoke(new Action(opensnapshotsfolder));
			// 录屏 / 录屏参数 / 系统参数：不唤起主窗，只打开对应功能窗
			tray.RecordRequested += () => Dispatcher.BeginInvoke(new Action(startrecord));
			tray.RecordOptionsRequested += () => Dispatcher.BeginInvoke(new Action(openrecordoptions));
			tray.GifRecordRequested += () => Dispatcher.BeginInvoke(new Action(startgifrecord));
			tray.GifRecordOptionsRequested += () => Dispatcher.BeginInvoke(new Action(opengifrecordoptions));
			tray.SettingsRequested += () => Dispatcher.BeginInvoke(new Action(opensettings));
			tray.ForceExitRequested += () => {
				forceExit = true;
				try { Close(); } catch { }
			};
			tray.SnapCopyOptionsChanged += (asImg, asFile, asPath) => Dispatcher.BeginInvoke(new Action(() => {
				applysnapcopyopts(asImg, asFile, asPath, fromTray: true);
			}));
			tray.SetSnapCopyOptions(opt.SnapCopyAsImage, opt.SnapCopyAsFile, opt.SnapCopyAsPath);
			tray.ApplyHotkeys();
		}
		catch (Exception ex) {
			setstatus(Loc.T("st.tray_fail", ex.Message));
		}
	}

	void syncsnapcopyopts() {
		ImageUtil.CurrentScreenshotKeepDays = opt.ScreenshotKeepDays;
		var fmt = (opt.ScreenshotFormat ?? "png").Trim().ToLowerInvariant();
		ImageUtil.CurrentScreenshotFormat = fmt is "jpg" or "jpeg" ? "jpg" : "png";
		ImageUtil.CurrentScreenshotJpgQuality = Compat.Clamp(
			opt.ScreenshotJpgQuality <= 0 ? 92 : opt.ScreenshotJpgQuality, 1, 100);
		ImageUtil.CurrentScreenshotMaxSizeEnabled = opt.ScreenshotMaxSizeEnabled;
		ImageUtil.CurrentScreenshotMaxWidth = Math.Max(16, opt.ScreenshotMaxWidth);
		ImageUtil.CurrentScreenshotMaxHeight = Math.Max(16, opt.ScreenshotMaxHeight);
		ImageUtil.CurrentSnapCopyAsImage = opt.SnapCopyAsImage;
		ImageUtil.CurrentSnapCopyAsFile = opt.SnapCopyAsFile;
		ImageUtil.CurrentSnapCopyAsPath = opt.SnapCopyAsPath;
	}

	/// <summary>写入 opt + ImageUtil，并同步主菜单/托盘（三选一）；切换后按新方式重复制上次截图。</summary>
	void applysnapcopyopts(bool asImg, bool asFile, bool asPath, bool fromTray = false) {
		// 三选一：路径 > 文件 > 图片
		if (asPath) {
			opt.SnapCopyAsImage = false;
			opt.SnapCopyAsFile = false;
			opt.SnapCopyAsPath = true;
		}
		else if (asFile && !asImg) {
			opt.SnapCopyAsImage = false;
			opt.SnapCopyAsFile = true;
			opt.SnapCopyAsPath = false;
		}
		else {
			opt.SnapCopyAsImage = true;
			opt.SnapCopyAsFile = false;
			opt.SnapCopyAsPath = false;
		}
		syncsnapcopyopts();
		setsnapcopyui(opt.SnapCopyAsImage, opt.SnapCopyAsFile, opt.SnapCopyAsPath, skipTray: fromTray);
		try { AppConfig.Save(opt); } catch { }
		// 菜单/托盘切换复制方式：立刻用新方式重写剪贴板（不新建 screenshots/ 文件）
		recopylastsnap();
	}

	/// <summary>按当前配置把上次截图再写入剪贴板；无历史则提示。</summary>
	void recopylastsnap() {
		try {
			var path = ImageUtil.RecopyLastScreenshot();
			if (string.IsNullOrWhiteSpace(path)) {
				setstatus(Loc.T("st.snap_recopy_none"));
				return;
			}
			var name = Path.GetFileName(path);
			if (opt.SnapCopyAsPath)
				setstatus(Loc.T("st.snap_recopy_path", name));
			else if (opt.SnapCopyAsFile && !opt.SnapCopyAsImage)
				setstatus(Loc.T("st.snap_recopy_file", name));
			else
				setstatus(Loc.T("st.snap_recopy_img", name));
		}
		catch (Exception ex) {
			setstatus(Loc.T("st.snap_recopy_fail", ex.Message));
		}
	}

	void onmenusnapcopy(object sender, RoutedEventArgs e) {
		if (snapCopyUi) return;
		// 点哪项就选哪项（radio；不可全关）
		if (sender == mnsnapcopypath)
			applysnapcopyopts(asImg: false, asFile: false, asPath: true);
		else if (sender == mnsnapcopyfile)
			applysnapcopyopts(asImg: false, asFile: true, asPath: false);
		else
			applysnapcopyopts(asImg: true, asFile: false, asPath: false);
	}

	void setsnapcopyui(bool asImg, bool asFile, bool asPath, bool skipTray = false) {
		var pathMode = asPath && !asImg && !asFile;
		var fileMode = !pathMode && asFile && !asImg;
		var imgMode = !pathMode && !fileMode;
		snapCopyUi = true;
		try {
			// Icon 内 RadioButton 显示单选状态（非 MenuItem 勾选）
			if (rbsnapcopyimg != null) rbsnapcopyimg.IsChecked = imgMode;
			if (rbsnapcopyfile != null) rbsnapcopyfile.IsChecked = fileMode;
			if (rbsnapcopypath != null) rbsnapcopypath.IsChecked = pathMode;
			if (!skipTray)
				try { tray?.SetSnapCopyOptions(imgMode, fileMode, pathMode); } catch { }
		}
		finally { snapCopyUi = false; }
	}

	static string fmttrayhk(string s) =>
		string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

	void inithotkey() {
		try {
			hotkey = new GlobalHotkey(this, 0x7001);
			// 主热键：切换主窗显示/隐藏
			hotkey.Fired += () => Dispatcher.BeginInvoke(new Action(hotkeytogglewindow));
			hotkeySnap = new GlobalHotkey(this, 0x7002);
			// 热键：结束后不唤起主窗；录屏中同样可用（会短暂挂起录屏 HUD）
			hotkeySnap.Fired += () => Dispatcher.BeginInvoke(new Action(() =>
				_ = snapannotateasync(restoreUi: false, showMainAfter: false)));
			hotkeySnapOcr = new GlobalHotkey(this, 0x7003);
			// 热键截图识别：框选后弹出主窗、切到 tab1 并显示识别结果
			hotkeySnapOcr.Fired += () => Dispatcher.BeginInvoke(new Action(() =>
				_ = captureasync(hideMain: false, showMainAfter: true)));
			hotkeyBoard = new GlobalHotkey(this, 0x7006);
			// 屏幕画板：不唤起主窗；录屏中同样可用
			hotkeyBoard.Fired += () => Dispatcher.BeginInvoke(new Action(() =>
				_ = screenboardasync(restoreUi: false, showMainAfter: false)));
			hotkeyVoice = new GlobalHotkey(this, 0x7004);
			hotkeyVoice.Fired += () => Dispatcher.BeginInvoke(new Action(() => {
				try {
					CaptureLog.Info("hotkeyVoice Fired");
					toggleasrvoice(fromHotkey: true);
				}
				catch (Exception ex) {
					CaptureLog.Ex("hotkeyVoice Fired", ex);
					try { setstatus("语音输入热键失败: " + ex.Message); } catch { }
					// 仅浮层，不弹托盘右下角
					try {
						if (asrVoiceHud == null) {
							asrVoiceHud = new VoiceInputHud();
							asrVoiceHud.Closed += (_, _) => asrVoiceHud = null;
						}
						asrVoiceHud.SetStatus("热键失败: " + ex.Message);
						if (!asrVoiceHud.IsVisible) asrVoiceHud.Show();
					}
					catch { }
				}
			}));
			hotkeyLive = new GlobalHotkey(this, 0x7005);
			hotkeyLive.Fired += () => Dispatcher.BeginInvoke(new Action(() => {
				try {
					CaptureLog.Info("hotkeyLive Fired");
					// 尽量切到语音识别页，方便看到状态
					try { maintabs.SelectedItem = tabasr; } catch { }
					try { asrsubtabs.SelectedItem = tabasrrec; } catch { }
					asrtogglelive();
				}
				catch (Exception ex) {
					CaptureLog.Ex("hotkeyLive Fired", ex);
					try { setstatus("实时字幕热键失败: " + ex.Message); } catch { }
				}
			}));
			// 句柄就绪后再注册
			SourceInitialized += (_, _) => registerhotkey();
			Loaded += (_, _) => {
				// 任一热键未注册则重试（含语音输入 / 实时字幕）
				var need = (hotkey != null && !string.IsNullOrWhiteSpace(opt.Hotkey) && !hotkey.IsRegistered)
					|| (hotkeySnap != null && !string.IsNullOrWhiteSpace(opt.HotkeySnap) && !hotkeySnap.IsRegistered)
					|| (hotkeySnapOcr != null && !string.IsNullOrWhiteSpace(opt.HotkeySnapOcr) && !hotkeySnapOcr.IsRegistered)
					|| (hotkeyBoard != null && !string.IsNullOrWhiteSpace(opt.HotkeyBoard) && !hotkeyBoard.IsRegistered)
					|| (hotkeyVoice != null && !string.IsNullOrWhiteSpace(opt.HotkeyVoiceInput) && !hotkeyVoice.IsRegistered)
					|| (hotkeyLive != null && !string.IsNullOrWhiteSpace(opt.HotkeyLiveCaption) && !hotkeyLive.IsRegistered);
				if (need)
					registerhotkey();
			};
		}
		catch (Exception ex) {
			setstatus(Loc.T("st.hotkey_fail", ex.Message));
		}
	}

	void registerhotkey() {
		var errs = new List<string>();
		// 呼出/隐藏 · 截图标注 · 截图识别 · 语音输入；空字符串 = 禁用（不注册）
		if (hotkey != null) {
			hotkey.Attach();
			if (!hotkey.Register(opt.Hotkey)) errs.Add(hotkey.LastError);
		}
		if (hotkeySnap != null) {
			hotkeySnap.Attach();
			if (!hotkeySnap.Register(opt.HotkeySnap)) errs.Add(hotkeySnap.LastError);
		}
		if (hotkeySnapOcr != null) {
			hotkeySnapOcr.Attach();
			if (!hotkeySnapOcr.Register(opt.HotkeySnapOcr)) errs.Add(hotkeySnapOcr.LastError);
		}
		if (hotkeyBoard != null) {
			hotkeyBoard.Attach();
			if (!hotkeyBoard.Register(opt.HotkeyBoard)) errs.Add(hotkeyBoard.LastError);
		}
		if (hotkeyVoice != null) {
			hotkeyVoice.Attach();
			if (!hotkeyVoice.Register(opt.HotkeyVoiceInput)) errs.Add(hotkeyVoice.LastError);
		}
		if (hotkeyLive != null) {
			hotkeyLive.Attach();
			if (!hotkeyLive.Register(opt.HotkeyLiveCaption)) errs.Add(hotkeyLive.LastError);
		}
		if (errs.Count > 0) {
			var msg = string.Join(" · ", errs);
			setstatus(msg);
			try { tray?.ShowToast("热键注册失败", msg, 5000); } catch { }
			try { CaptureLog.Info("registerhotkey fail: " + msg); } catch { }
		}
		else {
			string fmt(string s) => string.IsNullOrWhiteSpace(s) ? (Loc.IsEn ? "off" : "关") : s;
			setstatus(Loc.T("st.ready_hotkeys",
				fmt(hotkey?.CurrentHotkey),
				fmt(hotkeySnap?.CurrentHotkey),
				fmt(hotkeySnapOcr?.CurrentHotkey),
				fmt(hotkeyVoice?.CurrentHotkey),
				fmt(hotkeyLive?.CurrentHotkey)));
			try {
				CaptureLog.Info("registerhotkey ok voice=" + (hotkeyVoice?.CurrentHotkey ?? "")
					+ " live=" + (hotkeyLive?.CurrentHotkey ?? "")
					+ " regV=" + (hotkeyVoice?.IsRegistered == true)
					+ " regL=" + (hotkeyLive?.IsRegistered == true));
			}
			catch { }
		}
		updatehotkeymenutext();
	}

	void onstatechanged(object sender, EventArgs e) {
		// 仅最小化 → 隐藏到托盘（关闭按钮走确认退出）
		if (!opt.MinimizeToTray) return;
		if (WindowState == WindowState.Minimized) {
			try { Hide(); } catch { }
		}
	}

	void onclosing(object sender, System.ComponentModel.CancelEventArgs e) {
		// 点关闭 / Alt+F4：隐藏到托盘，不退出（仅「文件→退出」或托盘退出）
		if (!forceExit) {
			e.Cancel = true;
			try {
				savewindowbounds();
				savettsprefs();
				saveasrprefs();
				savefaceprefs();
			}
			catch { }
			try { Hide(); } catch { }
			return;
		}
		// 真正退出：快速结束进程，避免 ORT Dispose 卡 UI
		e.Cancel = true;
		exitfast();
	}

	/// <summary>
	/// 轻量清理后立刻结束进程。ORT InferenceSession.Dispose 在 UI 线程可阻塞 10s+，
	/// 进程退出由系统回收原生内存，无需同步释放。
	/// </summary>
	void exitfast() {
		try { Hide(); } catch { }
		try { disposeTts(); } catch { }
		try { disposeAsr(); } catch { }
		try { trEngine?.Dispose(); } catch { }
		try {
			savewindowbounds();
			savettsprefs();
			saveasrprefs();
			savetrprefs();
			savefaceprefs();
			cleanupfacetemps();
		}
		catch { }
		try { httpServer?.Stop(); } catch { }
		try { hotkey?.Dispose(); } catch { }
		try { hotkeySnap?.Dispose(); } catch { }
		try { hotkeySnapOcr?.Dispose(); } catch { }
		try { hotkeyVoice?.Dispose(); } catch { }
		try { hotkeyLive?.Dispose(); } catch { }
		try { tray?.Dispose(); } catch { }
		// 不在此 Dispose runner/ORT
		try { Environment.Exit(0); } catch { }
	}

	/// <summary>全局热键：切换主窗呼出 / 隐藏（与托盘单击一致）。</summary>
	void hotkeytogglewindow() {
		try {
			if (tray != null) {
				tray.togglewindow();
				return;
			}
			if (IsVisible && WindowState != WindowState.Minimized)
				Hide();
			else {
				Show();
				if (WindowState == WindowState.Minimized)
					WindowState = WindowState.Normal;
				Activate();
			}
		}
		catch (Exception ex) {
			setstatus($"热键切换窗口失败: {ex.Message}");
		}
	}

	/// <summary>托盘菜单：显示窗口并从剪贴板识别。</summary>
	async Task hotkeyclipboardasync() {
		try {
			if (tray != null) tray.showwindow();
			else {
				Show();
				if (WindowState == WindowState.Minimized)
					WindowState = WindowState.Normal;
				Activate();
			}
			if (busy) {
				setstatus("正在识别中…");
				return;
			}
			setstatus("托盘 · 从剪贴板识别…");
			await pasteasync();
		}
		catch (Exception ex) {
			setstatus($"剪贴板识别失败: {ex.Message}");
		}
	}

	// ───────── 顶栏：模型 / 语言 / 设备 ─────────

	void initmodelbar() {
		packs = ModelCatalog.Scan();
		modelUiLoading = true;
		epack.ItemsSource = packs;
		// 选中当前包
		var pack = packs.FirstOrDefault(p =>
				string.Equals(p.Id, opt.ModelPackId, StringComparison.OrdinalIgnoreCase))
			?? packs.FirstOrDefault();
		if (pack != null) {
			epack.SelectedItem = pack;
			fillvariants(pack, opt.ModelVariant);
		}
		// 设备（无 GPU 时禁用 GPU 项）
		refreshdeviceui();
		foreach (ComboBoxItem it in edevice.Items) {
			if ((string)it.Tag == opt.Device.ToString()) {
				if (it.IsEnabled) edevice.SelectedItem = it;
				break;
			}
		}
		if (edevice.SelectedItem == null || (edevice.SelectedItem is ComboBoxItem cur && !cur.IsEnabled)) {
			// 默认选 CPU（始终可用）
			foreach (ComboBoxItem it in edevice.Items) {
				if ((string)it.Tag == "Cpu") {
					edevice.SelectedItem = it;
					opt.Device = OcrDevice.Cpu;
					break;
				}
			}
		}
		// 加速后端不可用时回退 CPU
		if (!CudaBootstrap.IsGpuReady && opt.Device == OcrDevice.Gpu)
			opt.Device = OcrDevice.Cpu;
		if (!CudaBootstrap.IsDmlReady && opt.Device == OcrDevice.IntelGpu)
			opt.Device = OcrDevice.Cpu;
		// 检测边长
		selectdetlen(opt.DetLimitSideLen);
		modelUiLoading = false;
		if (!string.IsNullOrWhiteSpace(CudaBootstrap.GpuStatus))
			setstatus(CudaBootstrap.GpuStatus);

		epack.SelectionChanged += (_, _) => {
			if (modelUiLoading) return;
			var p = epack.SelectedItem as ModelPack;
			if (p == null) return;
			modelUiLoading = true;
			fillvariants(p, null);
			modelUiLoading = false;
			applymodelchoice(reload: true);
		};
		evariant.SelectionChanged += (_, _) => {
			if (modelUiLoading) return;
			applymodelchoice(reload: true);
		};
		edevice.SelectionChanged += (_, _) => {
			if (modelUiLoading) return;
			applymodelchoice(reload: true);
		};
		// 边长只影响推理参数，无需重建 session
		edetlen.SelectionChanged += (_, _) => {
			if (modelUiLoading) return;
			applymodelchoice(reload: false);
			setstatus($"检测边长上限 → {opt.DetLimitSideLen}（下次识别生效）");
		};
	}

	void refreshdeviceui() {
		foreach (ComboBoxItem it in edevice.Items) {
			var tag = it.Tag as string;
			if (tag == "Gpu") {
				it.IsEnabled = CudaBootstrap.IsGpuReady;
				it.Content = CudaBootstrap.IsGpuReady ? "GPU" : "GPU(不可用)";
			}
			else if (tag == "IntelGpu") {
				it.IsEnabled = CudaBootstrap.IsDmlReady;
				it.Content = CudaBootstrap.IsDmlReady ? "核显" : "核显(不可用)";
			}
		}
	}

	void selectdetlen(int value) {
		ComboBoxItem hit = null;
		foreach (ComboBoxItem it in edetlen.Items) {
			if (it.Tag is string s && int.TryParse(s, out var v) && v == value) {
				hit = it;
				break;
			}
		}
		if (hit != null) edetlen.SelectedItem = hit;
		else {
			// 不在列表中则插入一项
			var extra = new ComboBoxItem { Content = value.ToString(), Tag = value.ToString() };
			edetlen.Items.Add(extra);
			edetlen.SelectedItem = extra;
		}
	}

	void fillvariants(ModelPack pack, string preferTitle) {
		evariant.ItemsSource = pack.Variants;
		if (pack.Variants.Count == 0) {
			evariant.SelectedIndex = -1;
			return;
		}
		if (!string.IsNullOrWhiteSpace(preferTitle)) {
			var hit = pack.Variants.FirstOrDefault(v =>
				string.Equals(v.Title, preferTitle, StringComparison.OrdinalIgnoreCase));
			if (hit != null) {
				evariant.SelectedItem = hit;
				return;
			}
		}
		evariant.SelectedIndex = 0;
	}

	void applymodelchoice(bool reload) {
		var pack = epack.SelectedItem as ModelPack;
		var variant = evariant.SelectedItem as ModelVariant;
		if (pack != null) {
			opt.ModelPackId = pack.Id;
			opt.ModelsDir = pack.Dir;
		}
		if (variant != null)
			opt.ModelVariant = variant.Title;

		var tag = (edevice.SelectedItem as ComboBoxItem)?.Tag as string ?? "Cpu";
		// 选了不可用后端 → CPU
		if (tag == "Gpu" && !CudaBootstrap.IsGpuReady) tag = "Cpu";
		if (tag == "IntelGpu" && !CudaBootstrap.IsDmlReady) tag = "Cpu";
		opt.Device = tag switch {
			"Gpu" => OcrDevice.Gpu,
			"IntelGpu" => OcrDevice.IntelGpu,
			_ => OcrDevice.Cpu,
		};

		var detTag = (edetlen.SelectedItem as ComboBoxItem)?.Tag as string;
		if (int.TryParse(detTag, out var detLen) && detLen >= 320)
			opt.DetLimitSideLen = detLen;

		try { AppConfig.Save(opt); } catch { }
		if (!reload) return;
		var dev = opt.Device.ToString();
		var label = variant != null ? $"{pack?.DisplayName} · {variant.Title}" : (pack?.DisplayName ?? "");
		if (opt.ServiceMode) {
			// 服务模式：立即热切换并保持常驻，不先清空引擎
			tryservicewarmup($"已切换 · {label} · {dev} · 边长{opt.DetLimitSideLen}");
		}
		else {
			try { runner.Invalidate(); } catch { }
			setstatus($"已切换 · {label} · {dev} · 边长{opt.DetLimitSideLen}，下次识别时加载");
		}
	}

	/// <summary>服务模式：后台预热/热切换引擎，始终保持加载状态。</summary>
	void tryservicewarmup(string reasonPrefix) {
		if (!opt.ServiceMode) return;
		if (string.IsNullOrWhiteSpace(opt.ModelsDir) || !Directory.Exists(opt.ModelsDir))
			applydefaultmodel();
		var snap = snapshotopt();
		var prefix = string.IsNullOrWhiteSpace(reasonPrefix) ? "服务模式" : reasonPrefix;
		setstatus($"{prefix} · 正在预热引擎…");
		_ = Task.Run(() => {
			try {
				var loadMs = runner.Warmup(snap);
				var label = runner.ModelLabel ?? "";
				var dev = runner.DeviceUsed ?? "";
				Dispatcher.BeginInvoke(new Action(() => {
					if (loadMs > 0)
						setstatus($"{prefix} · 已预热 · {label} · {dev} · 加载 {loadMs}ms");
					else
						setstatus($"{prefix} · 引擎常驻 · {label} · {dev}");
				}));
			}
			catch (Exception ex) {
				Dispatcher.BeginInvoke(new Action(() =>
					setstatus($"{prefix} · 预热失败: {ex.Message}")));
			}
		});
	}

	/// <summary>设置窗口关闭后，把 opt 同步回顶栏下拉框。</summary>
	void syncmodelbarfromopt() {
		modelUiLoading = true;
		try {
			var pack = packs.FirstOrDefault(p =>
					string.Equals(p.Id, opt.ModelPackId, StringComparison.OrdinalIgnoreCase))
				?? packs.FirstOrDefault(p =>
					!string.IsNullOrEmpty(opt.ModelsDir)
					&& string.Equals(p.Dir, opt.ModelsDir, StringComparison.OrdinalIgnoreCase));
			if (pack != null) {
				epack.SelectedItem = pack;
				fillvariants(pack, opt.ModelVariant);
			}
			foreach (ComboBoxItem it in edevice.Items) {
				if ((string)it.Tag == opt.Device.ToString()) {
					edevice.SelectedItem = it;
					break;
				}
			}
			selectdetlen(opt.DetLimitSideLen);
		}
		finally {
			modelUiLoading = false;
		}
	}

	// ───────── 菜单 / 工具栏 ─────────

	void inittoolbar() {
		// 图区小按钮
		// 主窗内点截图：不隐藏主窗（冻结画面会带上主窗；需要无主窗时用热键）
		bcapture.Click += async (_, _) => await captureasync(hideMain: false);
		bsnap.Click += async (_, _) => await snapannotateasync(restoreUi: false);
		bpaste.Click += async (_, _) => await pasteasync();
		bcopyimg.Click += (_, _) => copyimage();
		bsaveclip.Click += (_, _) => saveimage();
		btoggletext.Checked += (_, _) => {
			if (mntoggletext.IsChecked != true) mntoggletext.IsChecked = true;
			drawoverlay();
		};
		btoggletext.Unchecked += (_, _) => {
			if (mntoggletext.IsChecked != false) mntoggletext.IsChecked = false;
			clearselection();
			drawoverlay();
			syncresultfromimg();
		};
		// 文件菜单
		mnpaste.Click += async (_, _) => await pasteasync();
		mnsaveclip.Click += (_, _) => saveimage();
		mnpdf.Click += (_, _) => openpdfworkbench();
		mnexit.Click += (_, _) => {
			forceExit = true;
			try { Close(); } catch { }
		};
		// 截图菜单
		mncapture.Click += async (_, _) => await captureasync(hideMain: false);
		mnsnap.Click += async (_, _) => await snapannotateasync(restoreUi: false);
		mnboard.Click += async (_, _) => await screenboardasync(restoreUi: false);
		mnvoice.Click += (_, _) => toggleasrvoice();
		mnlongshot.Click += async (_, _) => await longshotasync();
		mnsnapshots.Click += (_, _) => opensnapshotsfolder();
		mnsnapcopyimg.Click += onmenusnapcopy;
		mnsnapcopyfile.Click += onmenusnapcopy;
		mnsnapcopypath.Click += onmenusnapcopy;
		setsnapcopyui(opt.SnapCopyAsImage, opt.SnapCopyAsFile, opt.SnapCopyAsPath);
		mnrecord.Click += (_, _) => startrecord();
		mnrecordopt.Click += (_, _) => openrecordoptions();
		mngifrecord.Click += (_, _) => startgifrecord();
		mngifrecordopt.Click += (_, _) => opengifrecordoptions();
		// 编辑菜单
		mncopytext.Click += (_, _) => copytext();
		mncopyimg.Click += (_, _) => copyimage();
		mntoggletext.Checked += (_, _) => {
			if (btoggletext.IsChecked != true) btoggletext.IsChecked = true;
			drawoverlay();
		};
		mntoggletext.Unchecked += (_, _) => {
			if (btoggletext.IsChecked != false) btoggletext.IsChecked = false;
			clearselection();
			drawoverlay();
			syncresultfromimg();
		};
		mncancelocr.Click += (_, _) => cancelocr();
		// 工具菜单
		mnsettings.Click += (_, _) => opensettings();
		mninstall.Click += (_, _) => openinstallfeatures();
		mndiag.Click += (_, _) => opendiag();
		mnlangzh.Click += (_, _) => setlang("zh");
		mnlangen.Click += (_, _) => setlang("en");
		mnupdate.Click += (_, _) => openupdate();
		mnabout.Click += (_, _) => openabout();
		// 结果区快捷复制 / 识别中取消
		bcopy.Click += (_, _) => copytext();
		bcancelocrpanel.Click += (_, _) => cancelocr();
		// 结果子 Tab：进入时若该图尚未识别过则识别 1 次
		tabresult.SelectionChanged += async (_, e) => {
			if (!ReferenceEquals(e.Source, tabresult)) return;
			syncresultmetafromtab();
			drawoverlay();
			await ensureactivetabasync();
		};
		updatehotkeymenutext();
	}

	void setlang(string code) {
		var L = string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
		if (string.Equals(opt.UiLang, L, StringComparison.OrdinalIgnoreCase) && Loc.Lang == L)
			return;
		opt.UiLang = L;
		Loc.Lang = L;
		try { AppConfig.Save(opt); } catch { }
		applylang();
		setstatus(Loc.T("st.lang", L == "en" ? "English" : "中文"));
	}

	void applylang() {
		try {
			var arch = Environment.Is64BitProcess ? "" : " · x86";
			Title = $"{Loc.T("app.title")} v{AppUpdater.CurrentVersion()}{arch}";
			lbbrand.Text = Loc.T("app.brand");

			// 菜单
			mnfile.Header = Loc.T("menu.file");
			mncap.Header = Loc.T("menu.capture");
			mnedit.Header = Loc.T("menu.edit");
			mntools.Header = Loc.T("menu.tools");
			mnlang.Header = Loc.T("menu.lang");
			mnlangzh.Header = Loc.T("menu.lang.zh");
			mnlangen.Header = Loc.T("menu.lang.en");
			mnlangzh.IsChecked = Loc.IsZh;
			mnlangen.IsChecked = Loc.IsEn;

			mnpaste.Header = Loc.T("menu.paste");
			mnpaste.ToolTip = Loc.T("menu.paste.tip");
			mnsaveclip.Header = Loc.T("menu.saveimg");
			mnsaveclip.ToolTip = Loc.T("menu.saveimg.tip");
			mnpdf.Header = Loc.T("menu.pdf");
			mnpdf.ToolTip = Loc.T("menu.pdf.tip");
			mnexit.Header = Loc.T("menu.exit");

			mncapture.Header = Loc.T("menu.ocr");
			mncapture.ToolTip = Loc.T("menu.ocr.tip");
			mnsnap.Header = Loc.T("menu.snap");
			mnsnap.ToolTip = Loc.T("menu.snap.tip");
			mnboard.Header = Loc.T("menu.board");
			mnboard.ToolTip = Loc.T("menu.board.tip");
			mnvoice.Header = Loc.T("menu.voice");
			mnvoice.ToolTip = Loc.T("menu.voice.tip");
			mnlongshot.Header = Loc.T("menu.longshot");
			mnlongshot.ToolTip = Loc.T("menu.longshot.tip");
			mnsnapshots.Header = Loc.T("menu.snapshots");
			mnsnapshots.ToolTip = Loc.T("menu.snapshots.tip");
			mnsnapcopyimg.Header = Loc.T("menu.snapcopyimg");
			mnsnapcopyfile.Header = Loc.T("menu.snapcopyfile");
			mnsnapcopypath.Header = Loc.T("menu.snapcopypath");
			mnrecord.Header = Loc.T("menu.record");
			mnrecord.ToolTip = Loc.T("menu.record.tip");
			mnrecordopt.Header = Loc.T("menu.recordopt");
			mnrecordopt.ToolTip = Loc.T("menu.recordopt.tip");
			mngifrecord.Header = Loc.T("menu.gifrecord");
			mngifrecord.ToolTip = Loc.T("menu.gifrecord.tip");
			mngifrecordopt.Header = Loc.T("menu.gifrecordopt");
			mngifrecordopt.ToolTip = Loc.T("menu.gifrecordopt.tip");

			mncopytext.Header = Loc.T("menu.copytext");
			mncopytext.ToolTip = Loc.T("menu.copytext.tip");
			mncopyimg.Header = Loc.T("menu.copyimg");
			mncopyimg.ToolTip = Loc.T("menu.copyimg.tip");
			mntoggletext.Header = Loc.T("menu.toggletext");
			mntoggletext.ToolTip = Loc.T("menu.toggletext.tip");
			mncancelocr.Header = Loc.T("menu.cancelocr");
			mncancelocr.ToolTip = Loc.T("menu.cancelocr.tip");

			mnsettings.Header = Loc.T("menu.settings");
			mnsettings.ToolTip = Loc.T("menu.settings.tip");
			mninstall.Header = Loc.T("menu.install");
			mninstall.ToolTip = Loc.T("menu.install.tip");
			mndiag.Header = Loc.T("menu.diag");
			mndiag.ToolTip = Loc.T("menu.diag.tip");
			mnupdate.Header = Loc.T("menu.update");
			mnupdate.ToolTip = Loc.T("menu.update.tip");
			mnabout.Header = Loc.T("menu.about");
			mnabout.ToolTip = Loc.T("menu.about.tip");

			tabocr.Header = Loc.T("tab.ocr");
			tabtts.Header = Loc.T("tab.tts");
			tabasr.Header = Loc.T("tab.asr");
			tabtr.Header = Loc.T("tab.translate");

			// 顶栏
			lbpack.Text = Loc.T("label.pack");
			lbvariant.Text = Loc.T("label.variant");
			lbdevice.Text = Loc.T("label.device");
			lbdetlen.Text = Loc.T("label.detlen");
			epack.ToolTip = Loc.T("label.pack.tip");
			evariant.ToolTip = Loc.T("label.variant.tip");
			edevice.ToolTip = Loc.T("label.device.tip");
			lbdetlen.ToolTip = Loc.T("label.detlen.tip");
			edetlen.ToolTip = Loc.T("label.detlen.tip");
			edevgpu.Content = Loc.T("device.gpu");
			edevgpu.ToolTip = Loc.T("device.gpu.tip");
			edevigpu.Content = Loc.T("device.igpu");
			edevigpu.ToolTip = Loc.T("device.igpu.tip");
			edevcpu.Content = Loc.T("device.cpu");

			// 工具栏
			tbcapture.Text = Loc.T("tb.ocr");
			bcapture.ToolTip = Loc.T("tb.ocr.tip");
			tbsnap.Text = Loc.T("tb.snap");
			bsnap.ToolTip = Loc.T("tb.snap.tip");
			tbpaste.Text = Loc.T("tb.paste");
			bpaste.ToolTip = Loc.T("tb.paste.tip");
			tbcopyimg.Text = Loc.T("tb.copyimg");
			bcopyimg.ToolTip = Loc.T("tb.copyimg.tip");
			tbsave.Text = Loc.T("tb.save");
			bsaveclip.ToolTip = Loc.T("tb.save.tip");
			tbtext.Text = Loc.T("tb.text");
			btoggletext.ToolTip = Loc.T("tb.text.tip");
			lbimgsize.ToolTip = Loc.T("img.size.tip");
			lbzoom.ToolTip = Loc.T("img.zoom.tip");

			// 图区 / 结果
			if (curimg == null)
				lbhint.Text = Loc.T("hint.empty");
			lbresulttitle.Text = Loc.T("result.title");
			tabresultocr.Header = Loc.T("result.tab.ocr");
			tabresultqr.Header = Loc.T("result.tab.qr");
			lbocrrunningbadge.Text = Loc.T("result.running");
			if (string.IsNullOrWhiteSpace(ocrMetaText) || ocrMetaText.StartsWith("推理") || ocrMetaText.StartsWith("Infer"))
				ocrMetaText = Loc.T("result.meta");
			if (string.IsNullOrWhiteSpace(qrMetaText)
				|| qrMetaText.StartsWith("条码") || qrMetaText.StartsWith("二维码")
				|| qrMetaText.StartsWith("QR") || qrMetaText.StartsWith("Barcode"))
				qrMetaText = Loc.T("result.qr.meta");
			syncresultmetafromtab();
			if (!busy || busyKind != "qr") {
				lbocrruntitle.Text = Loc.T("ocr.running");
				lbocrrunhint.Text = Loc.T("ocr.running.hint");
			}
			else {
				lbocrruntitle.Text = Loc.T("qr.running");
				lbocrrunhint.Text = Loc.T("qr.running.hint");
			}
			bcancelocrpanel.Content = Loc.T("ocr.cancel");
			bcancelocrpanel.ToolTip = Loc.T("ocr.cancel.tip");
			bcopy.Content = Loc.T("copy");
			bcopy.ToolTip = Loc.T("copy.tip");

			updatehotkeymenutext();
			try { applyfacelang(); } catch { }
			try { tray?.ApplyLang(); } catch { }
		}
		catch { }
	}

	void updatehotkeymenutext() {
		try {
			var cap = fmttrayhk(opt.HotkeySnapOcr);
			var snap = fmttrayhk(opt.HotkeySnap);
			var board = fmttrayhk(opt.HotkeyBoard);
			var voice = fmttrayhk(opt.HotkeyVoiceInput);
			mncapture.InputGestureText = cap;
			mnsnap.InputGestureText = snap;
			mnboard.InputGestureText = board;
			mnvoice.InputGestureText = voice;
			try { tray?.ApplyHotkeys(); } catch { }
		}
		catch { }
	}

	// ───────── viewport：文字上拖选 / 空白处平移 ─────────

	void initviewport() {
		// 视口可聚焦：图→文同步时临时 Focus 结果区后可交还
		pviewport.Focusable = true;

		// 文本区：拖选实时同步；单击整行必须等 TextBox 自己落完 caret 后再做（见 finishtextsel）
		eresult.SelectionChanged += (_, _) => {
			if (syncingSel) return;
			if (textMouseDown) {
				// 按下过程中：仅在已拖动且有选区时同步，避免单击中间态把图上高亮清掉
				if (textSelDragged && eresult.SelectionLength > 0)
					syncimgfromresult();
				return;
			}
			// 键盘选区 / 其它
			if (eresult.SelectionLength > 0)
				syncimgfromresult();
		};
		eresult.PreviewMouseLeftButtonDown += (_, e) => {
			textMouseDown = true;
			textSelDragged = false;
			textDownPt = e.GetPosition(eresult);
		};
		eresult.PreviewMouseMove += (_, e) => {
			if (!textMouseDown || e.LeftButton != MouseButtonState.Pressed) return;
			if (textSelDragged) return;
			var p = e.GetPosition(eresult);
			var dx = p.X - textDownPt.X;
			var dy = p.Y - textDownPt.Y;
			if (dx * dx + dy * dy >= SEL_DRAG_PX * SEL_DRAG_PX)
				textSelDragged = true;
		};
		// Preview 过早：TextBox 随后还会改 caret/选区，必须延迟到其处理完成
		eresult.PreviewMouseLeftButtonUp += (_, _) => {
			var dragged = textSelDragged;
			textMouseDown = false;
			Dispatcher.BeginInvoke(new Action(() => finishtextsel(dragged)),
				System.Windows.Threading.DispatcherPriority.Input);
		};
		eresult.LostMouseCapture += (_, _) => { textMouseDown = false; };
		eresult.TextChanged += (_, _) => { lineOff = null; lineOffSrc = null; };

		pviewport.MouseWheel += (_, e) => {
			if (curimg == null) return;
			var pos = e.GetPosition(pviewport);
			var old = tfscale.ScaleX;
			var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
			var nz = Compat.Clamp(old * factor, ZMIN, ZMAX);
			var dx = pos.X - tfpan.X;
			var dy = pos.Y - tfpan.Y;
			tfpan.X = pos.X - dx * (nz / old);
			tfpan.Y = pos.Y - dy * (nz / old);
			tfscale.ScaleX = nz;
			tfscale.ScaleY = nz;
			updatezoomlabel();
			e.Handled = true;
		};

		pviewport.MouseLeftButtonDown += (_, e) => {
			if (curimg == null || busy) return;
			var vp = e.GetPosition(pviewport);
			var stagePt = e.GetPosition(pstage);
			var hit = mntoggletext.IsChecked == true && last != null
				? hittestchar(stagePt, allowNearest: false)
				: (-1, 0);

			selecting = false;
			panning = false;
			selDragged = false;

			if (hit.Item1 >= 0) {
				// 按下：先整行预览；拖动后改为从落点字符起的部分选
				selecting = true;
				downLine = hit.Item1;
				downCh = hit.Item2;
				downStage = stagePt;
				selectline(downLine);
				pviewport.Cursor = Cursors.IBeam;
				drawoverlay();
				// 延迟同步：避免 MouseDown 处理中 Focus/Select 被当前事件冲掉
				queuesyncresultfromimg();
				setstatus($"选区 · 单击整行 · 拖动选部分 · Ctrl+C 复制");
			}
			else {
				// 空白：平移；并清选区
				panning = true;
				panstart = vp;
				pan0x = tfpan.X;
				pan0y = tfpan.Y;
				pviewport.Cursor = Cursors.SizeAll;
				if (hasselection()) {
					clearselection();
					drawoverlay();
					queuesyncresultfromimg();
				}
			}

			pviewport.CaptureMouse();
			e.Handled = true;
		};

		pviewport.MouseMove += (_, e) => {
			// 未按下：悬停在叠层文字上显示 IBeam
			if (!pviewport.IsMouseCaptured) {
				updatehovercursor(e.GetPosition(pstage));
				return;
			}

			if (selecting && downLine >= 0 && last != null) {
				pviewport.Cursor = Cursors.IBeam;
				var stagePt = e.GetPosition(pstage);
				if (!selDragged) {
					var dx = stagePt.X - downStage.X;
					var dy = stagePt.Y - downStage.Y;
					if (dx * dx + dy * dy < SEL_DRAG_PX * SEL_DRAG_PX)
						return;
					selDragged = true;
					// 进入拖选：锚点改回按下时的字符位置
					ancLine = downLine;
					ancCh = downCh;
				}
				var hit = hittestchar(stagePt, allowNearest: true);
				if (hit.Item1 >= 0) {
					var ol = curLine;
					var oc = curCh;
					curLine = hit.Item1;
					curCh = hit.Item2;
					if (ol != curLine || oc != curCh) {
						drawoverlay();
						queuesyncresultfromimg();
						setstatus($"已选中 {selcount()} 字 · Ctrl+C 复制");
					}
				}
				return;
			}

			if (panning) {
				pviewport.Cursor = Cursors.SizeAll;
				var p = e.GetPosition(pviewport);
				tfpan.X = pan0x + (p.X - panstart.X);
				tfpan.Y = pan0y + (p.Y - panstart.Y);
			}
		};

		pviewport.MouseLeftButtonUp += (_, e) => {
			if (selecting) {
				selecting = false;
				try { pviewport.ReleaseMouseCapture(); } catch { }
				// 单击未拖动：保持整行（按下时已 selectline）
				if (!selDragged && downLine >= 0 && last != null
					&& downLine < last.Lines.Count)
					selectline(downLine);
				// 拖选但未形成有效区间：至少落到整行
				else if (selDragged && !hasselection() && downLine >= 0
					&& last != null && downLine < last.Lines.Count)
					selectline(downLine);

				drawoverlay();
				// 抬起后再同步一次，保证最终选区写入 TextBox
				queuesyncresultfromimg();
				if (hasselection())
					setstatus($"已选中 {selcount()} 字 · Ctrl+C 复制选中");
				updatehovercursor(e.GetPosition(pstage));
				e.Handled = true;
				return;
			}

			endpan();
			e.Handled = true;
		};

		pviewport.MouseLeave += (_, _) => {
			if (panning && !selecting) endpan();
		};

		pviewport.Drop += async (_, e) => {
			if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
			var files = e.Data.GetData(DataFormats.FileDrop) as string[];
			if (files == null || files.Length == 0) return;
			var pdf = files.FirstOrDefault(f => ispdf(f));
			if (pdf != null) {
				openpdfworkbench(pdf);
				return;
			}
			var path = files.FirstOrDefault(f => isimage(f));
			if (path == null) return;
			await loadfileasync(path);
		};
		pviewport.DragOver += (_, e) => {
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
				? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};

		void endpan() {
			panning = false;
			selecting = false;
			try { pviewport.ReleaseMouseCapture(); } catch { }
			updatehovercursor(Mouse.GetPosition(pstage));
		}
	}

	/// <summary>根据指针是否在叠层文字上切换 IBeam / Hand。</summary>
	void updatehovercursor(Point stagePt) {
		if (busy || curimg == null || mntoggletext.IsChecked != true || last == null) {
			pviewport.Cursor = Cursors.Hand;
			return;
		}
		var hit = hittestchar(stagePt, allowNearest: false);
		pviewport.Cursor = hit.Item1 >= 0 ? Cursors.IBeam : Cursors.Hand;
	}

	void initkeys() {
		PreviewKeyDown += async (_, e) => {
			// 焦点在可编辑文本时，交给 TextBox 默认行为（翻译/TTS/ASR 等框 Ctrl+V 粘贴文字）
			if (istextinputfocused()) {
				// 仍允许 Esc 清 OCR 图上选区（不抢 TextBox 内编辑）
				return;
			}
			if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V) {
				await pasteasync();
				e.Handled = true;
			}
			else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.A) {
				await captureasync(hideMain: false);
				e.Handled = true;
			}
			else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A
				&& !eresult.IsKeyboardFocusWithin) {
				// 全选叠加文字
				if (last != null && last.Lines.Count > 0) {
					ancLine = 0;
					ancCh = 0;
					curLine = last.Lines.Count - 1;
					curCh = last.Lines[curLine].Text?.Length ?? 0;
					drawoverlay();
					syncresultfromimg();
					setstatus($"已全选 {selcount()} 字 · Ctrl+C 复制");
					e.Handled = true;
				}
			}
			else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C
				&& !eresult.IsKeyboardFocusWithin) {
				copytext();
				e.Handled = true;
			}
			else if (e.Key == Key.Escape) {
				if (busy) {
					cancelocr();
					e.Handled = true;
				}
				else if (hasselection()) {
					clearselection();
					drawoverlay();
					syncresultfromimg();
					setstatus("已取消选区");
					e.Handled = true;
				}
			}
		};
	}

	/// <summary>焦点在 TextBox / 可编辑 ComboBox 等时，不抢 Ctrl+V/C/A。</summary>
	static bool istextinputfocused() {
		var fe = Keyboard.FocusedElement;
		if (fe is System.Windows.Controls.Primitives.TextBoxBase) return true;
		if (fe is PasswordBox) return true;
		if (fe is ComboBox cb && cb.IsEditable && cb.IsKeyboardFocusWithin) return true;
		// 可编辑 ComboBox 内部 TextBox
		if (fe is DependencyObject d) {
			var p = d;
			for (var i = 0; i < 8 && p != null; i++) {
				if (p is System.Windows.Controls.Primitives.TextBoxBase) return true;
				p = System.Windows.Media.VisualTreeHelper.GetParent(p) as DependencyObject
					?? (p is FrameworkElement fre ? fre.Parent as DependencyObject : null);
			}
		}
		return false;
	}

	// ───────── selection（字符级） ─────────

	bool hasselection() {
		if (last == null || last.Lines.Count == 0) return false;
		if (ancLine < 0 || curLine < 0) return false;
		ordercarets(out var sl, out var sc, out var el, out var ec);
		if (sl != el) return true;
		return sc != ec;
	}

	int selcount() {
		if (!hasselection()) return 0;
		return getselectedtext().Replace("\r", "").Replace("\n", "").Length;
	}

	void clearselection() {
		ancLine = curLine = -1;
		ancCh = curCh = 0;
		selecting = false;
		selDragged = false;
	}

	/// <summary>选中 OCR 第 line 行整行。</summary>
	void selectline(int line) {
		if (last == null || last.Lines.Count == 0) return;
		line = Compat.Clamp(line, 0, last.Lines.Count - 1);
		var len = (last.Lines[line].Text ?? "").Length;
		ancLine = line;
		ancCh = 0;
		curLine = line;
		curCh = len;
	}

	void ordercarets(out int sl, out int sc, out int el, out int ec) {
		sl = ancLine; sc = ancCh; el = curLine; ec = curCh;
		if (sl > el || (sl == el && sc > ec)) {
			(sl, el) = (el, sl);
			(sc, ec) = (ec, sc);
		}
	}

	/// <summary>
	/// 文本区鼠标抬起后：单击→整行；拖选→按实际选区同步图上。
	/// 必须延迟调用，避免被 TextBox 自己的点击处理覆盖。
	/// </summary>
	void finishtextsel(bool dragged) {
		if (syncingSel || last == null || last.Lines.Count == 0) return;
		if (!ensurelineoff()) return;

		if (dragged) {
			if (eresult.SelectionLength > 0)
				syncimgfromresult();
			return;
		}

		// 单击：按 caret 落点所在 OCR 行整行选中
		var text = eresult.Text ?? "";
		if (text.Length == 0) return;
		var idx = eresult.SelectionLength > 0
			? eresult.SelectionStart
			: eresult.CaretIndex;
		if (idx >= text.Length) idx = text.Length - 1;
		if (idx < 0) return;
		// 落在换行上时退到上一可见字符
		while (idx > 0 && (text[idx] == '\r' || text[idx] == '\n'))
			idx--;
		if (text[idx] == '\r' || text[idx] == '\n') return;

		if (!trylineat(idx, out var line)) return;
		selectline(line);
		drawoverlay();
		syncresultfromimg();
		if (hasselection())
			setstatus($"已选中 {selcount()} 字 · Ctrl+C 复制选中");
	}

	/// <summary>
	/// 按当前 eresult.Text 与 OCR 行内容建立行起始偏移。
	/// 兼容 \n / \r\n，避免 FullText 与 TextBox 换行不一致导致同步失效。
	/// </summary>
	bool ensurelineoff() {
		if (last == null || last.Lines.Count == 0) {
			lineOff = null;
			lineOffSrc = null;
			return false;
		}
		var text = eresult.Text ?? "";
		if (lineOff != null && lineOff.Length == last.Lines.Count
			&& string.Equals(lineOffSrc, text, StringComparison.Ordinal))
			return true;

		var n = last.Lines.Count;
		var starts = new int[n];
		var pos = 0;
		for (int i = 0; i < n; i++) {
			var line = last.Lines[i].Text ?? "";
			if (i > 0) {
				if (pos >= text.Length) { lineOff = null; return false; }
				// 消费一行分隔符
				if (text[pos] == '\r') {
					pos++;
					if (pos < text.Length && text[pos] == '\n') pos++;
				}
				else if (text[pos] == '\n') {
					pos++;
				}
				else {
					lineOff = null;
					return false;
				}
			}
			if (line.Length > 0) {
				if (pos + line.Length > text.Length) { lineOff = null; return false; }
				for (int k = 0; k < line.Length; k++) {
					if (text[pos + k] != line[k]) {
						lineOff = null;
						return false;
					}
				}
			}
			starts[i] = pos;
			pos += line.Length;
		}
		lineOff = starts;
		lineOffSrc = text;
		return true;
	}

	/// <summary>OCR 行/字符 → 结果 TextBox 字符偏移；失败返回 -1。</summary>
	int textoffset(int line, int ch) {
		if (!ensurelineoff()) return -1;
		var n = last.Lines.Count;
		line = Compat.Clamp(line, 0, n - 1);
		var tlen = (last.Lines[line].Text ?? "").Length;
		return lineOff[line] + Compat.Clamp(ch, 0, tlen);
	}

	/// <summary>结果 TextBox 字符偏移 → (行, 字符光标)。</summary>
	(int line, int ch) textpos(int offset) {
		if (!ensurelineoff()) return (-1, 0);
		var n = last.Lines.Count;
		var text = eresult.Text ?? "";
		offset = Compat.Clamp(offset, 0, text.Length);
		for (int i = 0; i < n; i++) {
			var tlen = (last.Lines[i].Text ?? "").Length;
			var lineStart = lineOff[i];
			var lineEnd = lineStart + tlen;
			var nextStart = i + 1 < n ? lineOff[i + 1] : text.Length;
			// 本行正文 + 行后换行都归本行（换行算在行末）
			if (offset < nextStart || i == n - 1) {
				if (offset <= lineEnd)
					return (i, Compat.Clamp(offset - lineStart, 0, tlen));
				return (i, tlen);
			}
		}
		var lastLen = (last.Lines[n - 1].Text ?? "").Length;
		return (n - 1, lastLen);
	}

	/// <summary>字符索引落在哪一行 OCR 正文上。</summary>
	bool trylineat(int charIndex, out int line) {
		line = -1;
		if (!ensurelineoff()) return false;
		var a = textpos(charIndex);
		if (a.line < 0) return false;
		line = a.line;
		return true;
	}

	/// <summary>延迟到当前输入事件结束后再同步，避免 Focus/Select 被 MouseDown 冲掉。</summary>
	void queuesyncresultfromimg() {
		Dispatcher.BeginInvoke(new Action(syncresultfromimg),
			System.Windows.Threading.DispatcherPriority.Input);
	}

	/// <summary>
	/// 图上选区 → 右侧结果文本高亮。
	/// WPF TextBox 在从未获得键盘焦点时 Select 往往不生效（点一次文本区后才正常），
	/// 因此设选区前必须先 Focus，再尽量把焦点交还。
	/// </summary>
	void syncresultfromimg() {
		if (syncingSel || last == null) return;
		syncingSel = true;
		var prevFocus = Keyboard.FocusedElement;
		var needRestore = false;
		try {
			if (!hasselection()) {
				if (eresult.SelectionLength > 0) {
					needRestore = focusresultforselect();
					eresult.Select(eresult.CaretIndex, 0);
				}
				return;
			}
			if (!ensurelineoff()) return;

			ordercarets(out var sl, out var sc, out var el, out var ec);
			var start = textoffset(sl, sc);
			var end = textoffset(el, ec);
			if (start < 0 || end < 0) return;
			if (end < start) (start, end) = (end, start);
			var textLen = (eresult.Text ?? "").Length;
			start = Compat.Clamp(start, 0, textLen);
			end = Compat.Clamp(end, 0, textLen);
			var len = Math.Max(0, end - start);

			needRestore = focusresultforselect();
			// 始终 Select：未获焦时 SelectionStart/Length 读数可能不准，不能依赖相等判断跳过
			eresult.Select(start, len);
			try {
				var lineIdx = eresult.GetLineIndexFromCharacterIndex(start);
				if (lineIdx >= 0) eresult.ScrollToLine(lineIdx);
			}
			catch { }
		}
		catch { }
		finally {
			syncingSel = false;
			// 图上拖选时不要抢走后续鼠标逻辑；选区已设好，非活动高亮仍可见
			if (needRestore)
				restoreresultfocus(prevFocus);
		}
	}

	/// <summary>让 eresult 获得键盘焦点以便 Select 生效。返回是否需要交还焦点。</summary>
	bool focusresultforselect() {
		if (eresult.IsKeyboardFocusWithin) return false;
		try {
			// 允许在未显示时也能拿到焦点（部分布局下 Focus 会失败）
			if (!eresult.Focus()) {
				eresult.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Input);
				eresult.Focus();
			}
		}
		catch { }
		return true;
	}

	void restoreresultfocus(IInputElement prev) {
		try {
			// 正在图上选字：焦点回到视口，避免 TextBox 吃掉后续拖选
			if (selecting || pviewport.IsMouseCaptured) {
				if (!pviewport.Focusable) pviewport.Focusable = true;
				pviewport.Focus();
				return;
			}
			if (prev != null && !ReferenceEquals(prev, eresult) && prev is UIElement ue) {
				ue.Focus();
				return;
			}
			// 否则留在结果区，便于直接 Ctrl+C
		}
		catch { }
	}

	/// <summary>右侧结果选区 → 图上高亮。</summary>
	void syncimgfromresult() {
		if (syncingSel || last == null || last.Lines.Count == 0) return;
		if (!ensurelineoff()) {
			// 文本已被改写，无法映射：保留图上旧选区，不强行清空
			return;
		}

		var start = eresult.SelectionStart;
		var len = eresult.SelectionLength;
		if (len <= 0) {
			// 无文本选区时不主动清图（单击整行由 finishtextsel 负责）
			return;
		}

		var a = textpos(start);
		var b = textpos(start + len);
		if (a.line < 0 || b.line < 0) return;

		// 已等价则跳过
		if (hasselection()) {
			ordercarets(out var sl, out var sc, out var el, out var ec);
			if (sl == a.line && sc == a.ch && el == b.line && ec == b.ch)
				return;
			if (ancLine == a.line && ancCh == a.ch && curLine == b.line && curCh == b.ch)
				return;
		}
		else if (ancLine == a.line && ancCh == a.ch && curLine == b.line && curCh == b.ch) {
			return;
		}

		ancLine = a.line;
		ancCh = a.ch;
		curLine = b.line;
		curCh = b.ch;
		drawoverlay();
		setstatus($"已选中 {selcount()} 字 · Ctrl+C 复制选中");
	}

	string getselectedtext() {
		if (!hasselection()) return "";
		ordercarets(out var sl, out var sc, out var el, out var ec);
		var sb = new StringBuilder();
		if (sl == el) {
			var t = last.Lines[sl].Text ?? "";
			sc = Compat.Clamp(sc, 0, t.Length);
			ec = Compat.Clamp(ec, 0, t.Length);
			if (ec > sc) sb.Append(t, sc, ec - sc);
			return sb.ToString();
		}
		for (int i = sl; i <= el; i++) {
			var t = last.Lines[i].Text ?? "";
			if (i == sl) {
				sc = Compat.Clamp(sc, 0, t.Length);
				if (sc < t.Length) sb.Append(t, sc, t.Length - sc);
			}
			else if (i == el) {
				ec = Compat.Clamp(ec, 0, t.Length);
				if (ec > 0) {
					if (sb.Length > 0) sb.AppendLine();
					sb.Append(t, 0, ec);
				}
			}
			else {
				if (sb.Length > 0) sb.AppendLine();
				sb.Append(t);
			}
		}
		return sb.ToString();
	}

	void copytext() {
		try {
			string text;
			string tip;
			if (isresultqrtab()) {
				// 二维码 Tab：优先 TextBox 选区，否则全文
				if (eqrresult.SelectionLength > 0) {
					text = eqrresult.SelectedText ?? "";
					tip = $"已复制选中 {text.Length} 字";
				}
				else {
					text = eqrresult.Text ?? "";
					tip = "已复制条码结果";
				}
			}
			else if (hasselection()) {
				text = getselectedtext();
				tip = $"已复制选中 {selcount()} 字";
			}
			else {
				text = eresult.Text ?? "";
				tip = "已复制全部结果";
			}
			if (string.IsNullOrEmpty(text)) {
				setstatus("没有可复制的文字");
				return;
			}
			Clipboard.SetText(text);
			setstatus(tip);
		}
		catch (Exception ex) {
			setstatus($"复制失败: {ex.Message}");
		}
	}

	bool isresultqrtab() {
		try { return ReferenceEquals(tabresult.SelectedItem, tabresultqr); }
		catch { return false; }
	}

	void syncresultmetafromtab() {
		try {
			lbmeta.Text = isresultqrtab() ? qrMetaText : ocrMetaText;
		}
		catch { }
	}

	/// <summary>
	/// 按当前结果 Tab（OCR / 条码）识别：未对该图识别过则识别一次。
	/// 截图/粘贴等不强制切换 Tab，保持用户当前选择。
	/// </summary>
	async Task ensureactivetabasync(int? wallStartTick = null, bool focusResult = true) {
		if (curimg == null || busy) return;
		if (isresultqrtab()) {
			if (qrDoneForImg) return;
			await runqrasync(curimg, wallStartTick, focusResult, setImg: false);
		}
		else {
			if (ocrDoneForImg) return;
			await runocrasync(curimg, wallStartTick, focusResult, setImg: false);
		}
	}

	/// <summary>
	/// 命中 (行, 字符光标)。字符光标 ch ∈ [0, len]，沿行框宽度比例估算。
	/// allowNearest：拖选时行间空隙吸附最近行。
	/// </summary>
	(int line, int ch) hittestchar(Point stagePt, bool allowNearest) {
		if (last == null || last.Lines.Count == 0) return (-1, 0);
		var x = (float)stagePt.X;
		var y = (float)stagePt.Y;
		const float pad = 6f;

		var best = -1;
		var bestDist = float.MaxValue;
		for (int i = 0; i < last.Lines.Count; i++) {
			var line = last.Lines[i];
			if (line.Box == null || line.Box.Length < 4) continue;
			if (pointinbox(x, y, line.Box, pad))
				return (i, charat(line, x, y));
			var cx = (line.Box[0].X + line.Box[1].X + line.Box[2].X + line.Box[3].X) / 4f;
			var cy = (line.Box[0].Y + line.Box[1].Y + line.Box[2].Y + line.Box[3].Y) / 4f;
			var d = Math.Abs(x - cx) + Math.Abs(y - cy);
			if (d < bestDist) {
				bestDist = d;
				best = i;
			}
		}
		if (allowNearest && selecting && best >= 0)
			return (best, charat(last.Lines[best], x, y));
		return (-1, 0);
	}

	/// <summary>将舞台坐标投影到行框局部 x，按宽度比例得到字符光标。</summary>
	static int charat(OcrLine line, float x, float y) {
		var text = line.Text ?? "";
		var len = text.Length;
		if (len == 0 || line.Box == null || line.Box.Length < 4) return 0;
		var p0 = line.Box[0];
		var p1 = line.Box[1];
		var p3 = line.Box[3];
		// 局部基：origin=p0，u=p1-p0，v=p3-p0
		var ux = p1.X - p0.X;
		var uy = p1.Y - p0.Y;
		var vx = p3.X - p0.X;
		var vy = p3.Y - p0.Y;
		var ulen2 = ux * ux + uy * uy;
		if (ulen2 < 1e-4f) return 0;
		var dx = x - p0.X;
		var dy = y - p0.Y;
		// 投影到 u 方向 [0,1]
		var t = (dx * ux + dy * uy) / ulen2;
		t = Compat.Clamp(t, 0f, 1f);
		// 光标：四舍五入到最近字符边界
		var ch = (int)Math.Round(t * len);
		return Compat.Clamp(ch, 0, len);
	}

	static bool pointinbox(float x, float y, Point2f[] box, float pad) {
		var minX = box.Min(p => p.X) - pad;
		var maxX = box.Max(p => p.X) + pad;
		var minY = box.Min(p => p.Y) - pad;
		var maxY = box.Max(p => p.Y) + pad;
		return x >= minX && x <= maxX && y >= minY && y <= maxY;
	}

	static Point2f lerp(Point2f a, Point2f b, float t) =>
		new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

	// ───────── actions ─────────

	/// <summary>
	/// 截图并立即 OCR。
	/// <paramref name="hideMain"/>：true=先藏主窗再截；false=不隐藏主窗。
	/// <paramref name="showMainAfter"/>：true=成功后唤起主窗；false=托盘后台识别（全程不 Show/Activate/Focus 主窗）。
	/// </summary>
	async Task captureasync(bool hideMain, bool showMainAfter = true, bool? mainWasVisibleOverride = null) {
		// 仅防截图重入；OCR busy 时仍允许再截（否则第一次识别中第二次热键会静默失效）
		if (capturing) {
			CaptureLog.Info("captureasync SKIP capturing=true");
			return;
		}
		// 遮罩/菜单关闭后系统可能把主窗拉回前台；托盘路径用 override 记「点菜单前」状态
		var mainWasVisible = mainWasVisibleOverride
			?? (IsVisible && WindowState != WindowState.Minimized);
		var hud = activeRecordHud;
		try {
			capturing = true;
			// 录屏中：挂起 HUD（隐藏+暂停），再走完整截图识别
			if (hud != null) {
				CaptureLog.Info("captureasync suspend RecordHud");
				hud.SuspendForCapture();
				await Task.Delay(40);
			}
			CaptureLog.Info($"captureasync start hideMain={hideMain} showMainAfter={showMainAfter} wasVis={mainWasVisible} busy={busy} recording={(hud != null)}");
			var bmp = await capturescreenasync(hideMain);
			CaptureLog.Info($"captureasync got bmp={CaptureLog.Bmp(bmp)}");
			if (bmp == null) {
				// Esc 取消：不唤起主窗，保持焦点在原应用
				setstatus("已取消截图");
				CaptureLog.Info("captureasync cancelled/null");
				if (!mainWasVisible)
					keepmainhidden();
				return;
			}
			// 写入 screenshots/ 并按配置复制到剪贴板
			try {
				var path = ImageUtil.SaveScreenshotAndCopy(bmp, "ocr");
				CaptureLog.Info("captureasync saved " + path);
			}
			catch (Exception ex) { CaptureLog.Ex("captureasync SaveScreenshot", ex); }
			// 成功：切到截图识别页；showMainAfter 时弹出主窗，否则藏回托盘
			try { maintabs.SelectedItem = tabocr; } catch { }
			if (showMainAfter)
				bringtofront();
			else if (!mainWasVisible)
				// 遮罩关闭瞬间系统常把主窗拉前台，后台识别时立刻藏回
				keepmainhidden();
			try {
				setimage(bmp);
				CaptureLog.Info($"captureasync setimage ok cur={CaptureLog.Bmp(curimg)} lb={lbimgsize?.Text}");
			}
			catch (Exception ex) {
				CaptureLog.Ex("captureasync setimage", ex);
			}
			var kind = isresultqrtab() ? "条码" : "OCR";
			setstatus($"截图 {bmp.PixelWidth}×{bmp.PixelHeight} · {kind}识别中…");
			var wall0 = Environment.TickCount;
			// 图已上屏；保持 OCR/条码 Tab 当前选择，只跑当前 Tab 的识别
			await ensureactivetabasync(wall0, focusResult: showMainAfter);
			CaptureLog.Info("captureasync ensureactivetab done");
			if (showMainAfter) {
				// 识别结束再确保主窗在前台并停在 tab1（长推理时用户可能切走）
				try { maintabs.SelectedItem = tabocr; } catch { }
				bringtofront();
			}
			else if (!mainWasVisible)
				keepmainhidden();
		}
		catch (Exception ex) {
			CaptureLog.Ex("captureasync", ex);
			try { maintabs.SelectedItem = tabocr; } catch { }
			if (showMainAfter)
				bringtofront();
			else if (!mainWasVisible)
				keepmainhidden();
			setstatus($"截图识别失败: {ex.Message}");
			if (showMainAfter)
				MessageBox.Show(this, ex.Message, "截图识别", MessageBoxButton.OK, MessageBoxImage.Warning);
			else
				showwarnmsg(ex.Message, "截图识别");
		}
		finally {
			try { hud?.ResumeAfterCapture(); } catch { }
			capturing = false;
		}
	}

	/// <summary>托盘后台截图：遮罩/菜单关闭可能把主窗拉起，强制藏回托盘。</summary>
	void keepmainhidden() {
		try { Hide(); } catch { }
	}

	void openrecordoptions() {
		try {
			opt.Record ??= new RecordOptions();
			var dlg = new RecordOptionsWindow(opt.Record);
			attachdialogowner(dlg);
			dlg.ShowDialog();
			if (!dlg.Applied) return;
			opt.Record = dlg.Result;
			try { AppConfig.Save(opt); } catch { }
			var o = opt.Record;
			setstatus("录屏选项已保存 · " + o.SummaryText());
		}
		catch (Exception ex) {
			showwarnmsg(ex.Message, "录屏选项");
		}
	}

	void opengifrecordoptions() {
		try {
			opt.GifRecord ??= new GifOptions();
			var dlg = new GifOptionsWindow(opt.GifRecord);
			attachdialogowner(dlg);
			dlg.ShowDialog();
			if (!dlg.Applied) return;
			opt.GifRecord = dlg.Result;
			try { AppConfig.Save(opt); } catch { }
			var o = opt.GifRecord;
			setstatus("GIF 录屏选项已保存 · " + o.SummaryText());
		}
		catch (Exception ex) {
			showwarnmsg(ex.Message, "GIF 录屏选项");
		}
	}

	/// <summary>录屏：选区 → 红框 HUD → 开始/暂停/停止 → 保存或丢弃。</summary>
	void startrecord() {
		if (capturing || activeRecordHud != null) {
			setstatus("请等待当前截图/录屏结束");
			return;
		}
		// 录屏依赖 ffmpeg64
		if (!FeaturePrompt.EnsureFfmpeg(this)) {
			setstatus("未安装 FFmpeg，已取消录屏");
			return;
		}
		try {
			// 仅选区阶段占用 capturing；HUD 显示后释放，以便录屏中截图识别/标注
			capturing = true;
			setstatus("录屏 · 请单击窗口或拖拽框选区域…");
			TmpStore.CleanupExpired();
			var rect = RecordRegionPicker.Pick();
			if (rect == null || rect.Value.Width < 16) {
				setstatus("已取消录屏");
				capturing = false;
				return;
			}
			var r = rect.Value;
			var ro = (opt.Record ?? new RecordOptions()).Clone();
			ro.Clamp();
			setstatus($"录屏区域 {r.Width}×{r.Height} · {ro.Codec} · {ro.Fps}fps · {ro.CrfLabel}");
			var hud = new RecordHud(r, ro);
			activeRecordHud = hud;
			capturing = false;
			hud.Closed += (_, _) => {
				if (activeRecordHud == hud) activeRecordHud = null;
			};
			hud.Finished += () => {
				try {
					if (activeRecordHud == hud) activeRecordHud = null;
					if (hud.Saved && !string.IsNullOrEmpty(hud.SavedPath))
						setstatus("录屏已保存: " + hud.SavedPath);
					else if (hud.Completed)
						setstatus("录屏已结束（未保存）");
					else
						setstatus("已取消录屏");
				}
				catch { }
				finally {
					if (activeRecordHud == hud) activeRecordHud = null;
				}
			};
			hud.Show();
		}
		catch (Exception ex) {
			activeRecordHud = null;
			capturing = false;
			setstatus("录屏失败: " + ex.Message);
			showwarnmsg(ex.Message, "录屏");
		}
	}

	/// <summary>GIF 录屏：选区 → HUD → 低帧率无声 GIF。</summary>
	void startgifrecord() {
		if (capturing || activeRecordHud != null) {
			setstatus("请等待当前截图/录屏结束");
			return;
		}
		if (!FeaturePrompt.EnsureFfmpeg(this)) {
			setstatus("未安装 FFmpeg，已取消 GIF 录屏");
			return;
		}
		try {
			capturing = true;
			setstatus("GIF 录屏 · 请单击窗口或拖拽框选区域…");
			TmpStore.CleanupExpired();
			var rect = RecordRegionPicker.Pick("GIF 录屏：单击窗口或拖拽框选 · Esc 取消");
			if (rect == null || rect.Value.Width < 16) {
				setstatus("已取消 GIF 录屏");
				capturing = false;
				return;
			}
			var r = rect.Value;
			var go = (opt.GifRecord ?? new GifOptions()).Clone();
			go.Clamp();
			setstatus($"GIF 录屏区域 {r.Width}×{r.Height} · 采{GifOptions.CaptureFps}fps · 默认出{go.Fps}fps");
			var hud = new RecordHud(r, go);
			activeRecordHud = hud;
			capturing = false;
			hud.Closed += (_, _) => {
				if (activeRecordHud == hud) activeRecordHud = null;
			};
			hud.Finished += () => {
				try {
					if (activeRecordHud == hud) activeRecordHud = null;
					if (hud.Saved && !string.IsNullOrEmpty(hud.SavedPath))
						setstatus("GIF 已保存: " + hud.SavedPath);
					else if (hud.Completed)
						setstatus("GIF 录屏已结束（未保存）");
					else
						setstatus("已取消 GIF 录屏");
				}
				catch { }
				finally {
					if (activeRecordHud == hud) activeRecordHud = null;
				}
			};
			hud.Show();
		}
		catch (Exception ex) {
			activeRecordHud = null;
			capturing = false;
			setstatus("GIF 录屏失败: " + ex.Message);
			showwarnmsg(ex.Message, "GIF 录屏");
		}
	}

	/// <summary>长截图：点选窗口 → 自动滚动拼接 → 上屏（不做 OCR）。</summary>
	async Task longshotasync() {
		if (capturing) return;
		if (!FeaturePrompt.EnsureOpenCv(this)) {
			setstatus("未安装 OpenCV，已取消长截图");
			return;
		}
		try {
			capturing = true;
			setstatus("长截图 · 请单击目标窗口…");
			// 不隐藏主窗；选窗遮罩置顶即可
			await Task.Delay(40);

			var pick = ScrollCapture.PickWindow();
			if (pick == null || pick.Hwnd == IntPtr.Zero) {
				setstatus("已取消长截图");
				return;
			}
			bringtofront();

			var title = string.IsNullOrWhiteSpace(pick.Title) ? "窗口" : pick.Title;
			if (title.Length > 40) title = title[..40] + "…";
			setstatus($"长截图 · {title} · 滚动中…");

			BitmapSource bmp;
			try {
				bmp = await ScrollCapture.CaptureLongAsync(
					pick.Hwnd,
					msg => Dispatcher.BeginInvoke(new Action(() => setstatus(msg))),
					CancellationToken.None);
			}
			catch (Exception ex) {
				setstatus("长截图失败: " + ex.Message);
				MessageBox.Show(this, ex.Message, "长截图", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			if (bmp == null) {
				setstatus("长截图失败：无图像");
				return;
			}

			// 仅上屏，不跑识别（进入 OCR/条码 Tab 时再各识别 1 次）
			clearselection();
			setimage(bmp);
			ocrMetaText = "长截图 · 未识别（切换到 OCR 页签可识别）";
			qrMetaText = Loc.T("result.qr.meta");
			syncresultmetafromtab();
			lbtime.Text = DateTime.Now.ToString("HH:mm:ss");
			drawoverlay();
			setstatus($"长截图完成 {bmp.PixelWidth}×{bmp.PixelHeight}");
		}
		catch (Exception ex) {
			bringtofront();
			setstatus("长截图失败: " + ex.Message);
			MessageBox.Show(this, ex.Message, "长截图", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		finally {
			capturing = false;
		}
	}

	/// <summary>
	/// 屏幕画板：冻结全屏后直接标注（跳过框选）。
	/// <paramref name="restoreUi"/>：true=藏主窗后恢复；false=热键/托盘（不隐藏主窗）。
	/// <paramref name="showMainAfter"/>：false=托盘后台（全程不弹主窗）。
	/// </summary>
	async Task screenboardasync(bool restoreUi, bool showMainAfter = true, bool? mainWasVisibleOverride = null) {
		if (capturing) return;
		BitmapSource resultImg = null;
		var confirmed = false;
		var wantOcr = false;
		var mainWasVisible = mainWasVisibleOverride
			?? (IsVisible && WindowState != WindowState.Minimized);
		var hud = activeRecordHud;
		try {
			capturing = true;
			if (hud != null) {
				CaptureLog.Info("screenboard suspend RecordHud");
				hud.SuspendForCapture();
			}
			if (restoreUi) {
				try { Hide(); } catch { WindowState = WindowState.Minimized; }
				await Task.Delay(40);
			}
			else if (hud != null) {
				await Task.Delay(40);
			}
			CaptureLog.Info($"screenboard start restoreUi={restoreUi} showMainAfter={showMainAfter} wasVis={mainWasVisible} recording={(hud != null)}");
			var cap = CaptureOverlay.RunBoard();
			confirmed = cap.Confirmed;
			wantOcr = cap.WantOcr;
			resultImg = cap.Image;
			CaptureLog.Info($"screenboard result confirmed={confirmed} wantOcr={wantOcr} img={CaptureLog.Bmp(resultImg)}");
			if (!confirmed)
				setstatus("已取消屏幕画板");
			else
				setstatus(wantOcr ? "屏幕画板 · 识别中…" : "屏幕画板完成");
		}
		catch (Exception ex) {
			CaptureLog.Ex("screenboard", ex);
			setstatus($"屏幕画板失败: {ex.Message}");
			try {
				if (showMainAfter && IsVisible && WindowState != WindowState.Minimized)
					MessageBox.Show(this, ex.Message, "屏幕画板", MessageBoxButton.OK, MessageBoxImage.Warning);
				else
					MessageBox.Show(ex.Message, "屏幕画板", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
			catch { }
		}
		finally {
			try { hud?.ResumeAfterCapture(); } catch { }
			capturing = false;
			if (!showMainAfter && !mainWasVisible)
				keepmainhidden();
			if (confirmed && resultImg != null) {
				await afterannotateasync(resultImg, wantOcr, "屏幕画板",
					showMainAfter: showMainAfter, mainWasVisible: mainWasVisible);
			}
			else {
				// Esc 取消：不唤起主窗
				CaptureLog.Info($"screenboard NO show confirmed={confirmed} imgnull={resultImg == null}");
			}
		}
	}

	/// <summary>
	/// 标注/画板确认后：上屏；可选识别。仅在用户确认后调用（取消不弹主窗）。
	/// 点「OCR」时按结果区当前 OCR / 条码 Tab 识别，不切换结果 Tab。
	/// 仅完成/复制（未识别）时仍尊重 showMainAfter。
	/// </summary>
	async Task afterannotateasync(BitmapSource resultImg, bool wantOcr, string label,
		bool showMainAfter = true, bool mainWasVisible = true) {
		clearselection();
		try {
			setimage(resultImg);
			CaptureLog.Info($"{label} setimage ok {CaptureLog.Bmp(curimg)}");
		}
		catch (Exception ex) { CaptureLog.Ex(label + " setimage", ex); }
		lbtime.Text = DateTime.Now.ToString("HH:mm:ss");
		if (wantOcr) {
			try { maintabs.SelectedItem = tabocr; } catch { }
			var kind = isresultqrtab() ? "条码" : "OCR";
			if (isresultqrtab())
				qrMetaText = $"{label} · {kind}识别中…";
			else
				ocrMetaText = $"{label} · {kind}识别中…";
			syncresultmetafromtab();
			drawoverlay();
			setstatus($"{label} {resultImg.PixelWidth}×{resultImg.PixelHeight} · {kind}识别中…");
			bringtofront();
			var wall0 = Environment.TickCount;
			await ensureactivetabasync(wall0, focusResult: true);
			try { maintabs.SelectedItem = tabocr; } catch { }
			bringtofront();
		}
		else {
			ocrMetaText = label + " · 未识别";
			syncresultmetafromtab();
			drawoverlay();
			setstatus($"{label}已显示 · {resultImg.PixelWidth}×{resultImg.PixelHeight}");
			if (showMainAfter)
				// 确认完成时前置主窗，便于查看结果
				restoretotopifvisible();
			else if (!mainWasVisible)
				keepmainhidden();
		}
	}

	void opensnapshotsfolder() {
		try {
			ImageUtil.OpenScreenshotsFolder();
			setstatus("已打开截图历史 · " + ImageUtil.ScreenshotsDir);
		}
		catch (Exception ex) {
			setstatus("打开截图历史失败: " + ex.Message);
			MessageBox.Show(this, ex.Message, "截图历史", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	/// <summary>
	/// 微信式截图标注。
	/// <paramref name="restoreUi"/>：true=工具栏（藏主窗 → 结束后恢复并显示图）；false=热键（不隐藏主窗）。
	/// <paramref name="showMainAfter"/>：false=托盘后台（全程不弹主窗）。
	/// </summary>
	async Task snapannotateasync(bool restoreUi, bool showMainAfter = true, bool? mainWasVisibleOverride = null) {
		if (capturing) return;
		BitmapSource resultImg = null;
		var confirmed = false;
		var wantOcr = false;
		var mainWasVisible = mainWasVisibleOverride
			?? (IsVisible && WindowState != WindowState.Minimized);
		var hud = activeRecordHud;
		try {
			capturing = true;
			// 录屏中：挂起 HUD，再走完整截图标注
			if (hud != null) {
				CaptureLog.Info("snapannotate suspend RecordHud");
				hud.SuspendForCapture();
			}
			// 仅工具栏点击时隐藏主窗；热键不隐藏
			if (restoreUi) {
				try { Hide(); } catch { WindowState = WindowState.Minimized; }
				await Task.Delay(40);
			}
			else if (hud != null) {
				await Task.Delay(40);
			}
			CaptureLog.Info($"snapannotate start restoreUi={restoreUi} showMainAfter={showMainAfter} wasVis={mainWasVisible} recording={(hud != null)}");
			var cap = CaptureOverlay.Run(annotate: true);
			confirmed = cap.Confirmed;
			wantOcr = cap.WantOcr;
			resultImg = cap.Image;
			CaptureLog.Info($"snapannotate result confirmed={confirmed} wantOcr={wantOcr} img={CaptureLog.Bmp(resultImg)}");
			if (!confirmed)
				setstatus("已取消截图标注");
			else
				setstatus(wantOcr ? "截图标注 · 识别中…" : "截图标注完成");
		}
		catch (Exception ex) {
			CaptureLog.Ex("snapannotate", ex);
			setstatus($"截图标注失败: {ex.Message}");
			try {
				if (showMainAfter && IsVisible && WindowState != WindowState.Minimized)
					MessageBox.Show(this, ex.Message, "截图标注", MessageBoxButton.OK, MessageBoxImage.Warning);
				else
					MessageBox.Show(ex.Message, "截图标注", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
			catch { }
		}
		finally {
			try { hud?.ResumeAfterCapture(); } catch { }
			capturing = false;
			// 遮罩关闭后系统可能拉起主窗
			if (!showMainAfter && !mainWasVisible)
				keepmainhidden();
			if (confirmed && resultImg != null) {
				await afterannotateasync(resultImg, wantOcr, "截图标注",
					showMainAfter: showMainAfter, mainWasVisible: mainWasVisible);
			}
			else {
				// Esc 取消：不唤起/前置主窗
				CaptureLog.Info($"snapannotate NO show confirmed={confirmed} imgnull={resultImg == null}");
			}
		}
	}

	/// <summary>框选截图；<paramref name="hideMain"/> 为 true 时先隐藏主窗。</summary>
	async Task<BitmapSource> capturescreenasync(bool hideMain) {
		if (hideMain) {
			try { Hide(); } catch { WindowState = WindowState.Minimized; }
			await Task.Delay(40);
		}
		var cap = CaptureOverlay.Run(annotate: false);
		CaptureLog.Info($"capturescreenasync confirmed={cap.Confirmed} img={CaptureLog.Bmp(cap.Image)}");
		// 取消不恢复主窗（避免 Esc 后抢焦点）；成功由 captureasync.bringtofront 负责
		if (!cap.Confirmed) return null;
		return cap.Image;
	}

	void bringtofront() {
		try {
			Show();
			if (WindowState == WindowState.Minimized)
				WindowState = WindowState.Normal;
			Activate();
			// 全屏 Topmost 截图层关闭后，强制拉回前台
			Topmost = true;
			Topmost = false;
		}
		catch { }
	}

	/// <summary>
	/// 主窗可见时挂 Owner 并居中于主窗；隐藏/最小化（托盘）时不设 Owner，
	/// 避免 ShowDialog 把主窗一并拉起，对话框改居中屏幕。
	/// </summary>
	void attachdialogowner(Window dlg) {
		if (dlg == null) return;
		try {
			if (IsVisible && WindowState != WindowState.Minimized) {
				dlg.Owner = this;
				dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
			}
			else {
				dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
			}
		}
		catch {
			try { dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; } catch { }
		}
	}

	/// <summary>警告框：主窗可见时以主窗为 Owner，否则独立弹出以免唤起主窗。</summary>
	void showwarnmsg(string msg, string title) {
		try {
			if (IsVisible && WindowState != WindowState.Minimized)
				MessageBox.Show(this, msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
			else
				MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		catch {
			try { MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
		}
	}

	/// <summary>
	/// 主窗仍可见时拉回 Z 序前台（不强制 Show 隐藏/托盘窗）。
	/// 用于热键截图：遮罩 Topmost 关闭后主窗常被压到最底层。
	/// </summary>
	void restoretotopifvisible() {
		if (!IsVisible) return;
		try {
			if (WindowState == WindowState.Minimized)
				WindowState = WindowState.Normal;
			Activate();
			Topmost = true;
			Topmost = false;
			Focus();
		}
		catch { }
	}

	void copyimage() {
		if (curimg == null) {
			setstatus("当前没有可复制的图片");
			return;
		}
		try {
			// 写入 screenshots/ 并以文件形式复制到剪贴板（资源管理器可 Ctrl+V）
			var path = ImageUtil.SaveScreenshotAndCopyAsFile(curimg, "copy");
			setstatus("已复制为文件 · " + Path.GetFileName(path));
		}
		catch (Exception ex) {
			setstatus($"复制图片失败: {ex.Message}");
		}
	}

	/// <summary>将图片区当前显示的图片保存为文件。</summary>
	void saveimage() {
		if (curimg == null) {
			setstatus("当前没有可保存的图片");
			MessageBox.Show(this, "图片区没有图片，请先截图、粘贴或打开图片。", "保存图片",
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		try {
			var sfd = new Microsoft.Win32.SaveFileDialog {
				Title = "保存图片",
				Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp",
				FileName = $"img_{DateTime.Now:yyyyMMdd_HHmmss}.png",
				DefaultExt = ".png",
				AddExtension = true,
				OverwritePrompt = true,
			};
			if (sfd.ShowDialog(this) != true) return;
			ImageUtil.Savefile(curimg, sfd.FileName);
			setstatus("图片已保存: " + sfd.FileName);
		}
		catch (Exception ex) {
			setstatus($"保存图片失败: {ex.Message}");
			MessageBox.Show(this, ex.Message, "保存图片", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	async Task pasteasync() {
		if (busy) return;
		// 端到端：读剪贴板 → 按当前结果 Tab 识别（不强制切 OCR）
		var wall0 = Environment.TickCount;
		try {
			// 支持：位图 / 资源管理器复制的 png 等文件路径
			var bmp = ImageUtil.Fromclipboard();
			if (bmp == null) {
				setstatus("剪贴板中没有图片（可复制图片内容，或复制 png/jpg 文件后粘贴）");
				return;
			}
			setimage(bmp);
			await ensureactivetabasync(wall0);
		}
		catch (Exception ex) {
			setstatus($"粘贴失败: {ex.Message}");
		}
	}

	async Task loadfileasync(string path) {
		if (busy) return;
		if (ispdf(path)) {
			openpdfworkbench(path);
			return;
		}
		var wall0 = Environment.TickCount;
		try {
			var bmp = ImageUtil.Fromfile(path);
			setimage(bmp);
			await ensureactivetabasync(wall0);
		}
		catch (Exception ex) {
			setstatus($"打开失败: {ex.Message}");
		}
	}

	/// <summary>打开独立 PDF 识别工作台（可编辑、存草稿、导出）。</summary>
	void openpdfworkbench(string pdfPath = null) {
		try {
			if (!FeaturePrompt.EnsurePdf(this)) {
				setstatus("未安装 PDF 渲染库，已取消");
				return;
			}
			if (!FeaturePrompt.EnsureOpenCv(this) || !FeaturePrompt.EnsureOcrModels(this)
				|| !FeaturePrompt.EnsureOcrOrt(this)) {
				setstatus("未安装 OCR 依赖，PDF 识别不可用");
				return;
			}
			applymodelchoice(reload: false);
			if (string.IsNullOrWhiteSpace(opt.ModelsDir) || !Directory.Exists(opt.ModelsDir))
				applydefaultmodel();
			var win = new PdfOcrWindow(() => snapshotopt(), runner, pdfPath) {
				Owner = this,
			};
			win.Show();
			setstatus("已打开 PDF 识别工作台");
		}
		catch (Exception ex) {
			setstatus($"打开 PDF 工作台失败: {ex.Message}");
			MessageBox.Show(this, ex.Message, "PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void openabout() {
		try {
			var dlg = new AboutWindow { Owner = this };
			dlg.ShowDialog();
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Loc.T("about.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	async void openupdate() {
		UpdateProgressWindow prog = null;
		try {
			prog = new UpdateProgressWindow { Owner = this };
			prog.Show();
			prog.Report("check", 0);

			var info = await AppUpdater.CheckLatestAsync(prog.Token).ConfigureAwait(true);
			if (prog.WasCancelled) return;

			if (!info.HasUpdate) {
				prog.ForceClose();
				prog = null;
				MessageBox.Show(this,
					Loc.T("update.latest", info.CurrentVersion, info.Version),
					Loc.T("update.title"),
					MessageBoxButton.OK,
					MessageBoxImage.Information);
				return;
			}

			prog.ForceClose();
			prog = null;

			var sizeText = info.SizeBytes > 0
				? FeatureInstaller.FormatBytes(info.SizeBytes)
				: "—";
			var ask = Loc.T("update.found",
				info.CurrentVersion,
				info.Version,
				info.AssetName ?? Path.GetFileName(info.DownloadUrl),
				sizeText);
			var r = MessageBox.Show(this, ask, Loc.T("update.title"),
				MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (r != MessageBoxResult.Yes) return;

			prog = new UpdateProgressWindow { Owner = this };
			prog.Show();
			prog.Report("download", 0, Loc.T("update.downloading"));

			var log = new Progress<string>(s => {
				try { prog?.Report("download", -1, s); } catch { }
			});
			var dl = new Progress<InstallProgress>(p => {
				try { prog?.ReportInstall(p); } catch { }
			});
			var archive = await AppUpdater.DownloadAsync(info, dl, log, prog.Token)
				.ConfigureAwait(true);
			if (prog.WasCancelled) return;

			prog.Report("prepare", 0.98, Loc.T("update.preparing"));
			// 关闭进度窗再启动更新器（主进程即将退出）
			prog.ForceClose();
			prog = null;

			AppUpdater.LaunchUpdaterAndExit(archive);
		}
		catch (OperationCanceledException) {
			// 用户取消
		}
		catch (Exception ex) {
			try { prog?.ForceClose(); } catch { }
			prog = null;
			MessageBox.Show(this,
				Loc.T("update.fail", ex.Message),
				Loc.T("update.title"),
				MessageBoxButton.OK,
				MessageBoxImage.Warning);
		}
		finally {
			try { prog?.ForceClose(); } catch { }
		}
	}

	void opensettings() {
		// 打开前把顶栏当前选择写回 opt
		applymodelchoice(reload: false);
		var dlg = new SettingsWindow(opt);
		attachdialogowner(dlg);
		dlg.ShowDialog();
		if (!dlg.Applied) return;
		var old = opt;
		opt = dlg.Result;
		syncsnapcopyopts();
		try { refreshtrllm(); } catch { }
		setsnapcopyui(opt.SnapCopyAsImage, opt.SnapCopyAsFile, opt.SnapCopyAsPath);
		try { ImageUtil.CleanupScreenshots(opt.ScreenshotKeepDays); } catch { }
		// 界面语言
		if (!string.Equals(old.UiLang, opt.UiLang, StringComparison.OrdinalIgnoreCase)) {
			Loc.SetFromConfig(opt.UiLang);
			applylang();
		}
		syncmodelbarfromopt();
		try { AppConfig.Save(opt); } catch { }
		// 热键变更则重注册
		if (!string.Equals(old.Hotkey, opt.Hotkey, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(old.HotkeySnap, opt.HotkeySnap, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(old.HotkeySnapOcr, opt.HotkeySnapOcr, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(old.HotkeyBoard, opt.HotkeyBoard, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(old.HotkeyVoiceInput, opt.HotkeyVoiceInput, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(old.HotkeyLiveCaption, opt.HotkeyLiveCaption, StringComparison.OrdinalIgnoreCase))
			registerhotkey();
		// HTTP 端口/开关变更则重启
		if (old.HttpEnabled != opt.HttpEnabled || old.HttpPort != opt.HttpPort
			|| !string.Equals(old.HttpHost, opt.HttpHost, StringComparison.OrdinalIgnoreCase))
			restarthttp();

		// session 级变更：模型/设备；runtime 级：边长/阈值/cls（不拆 session）
		var needSessionReload = old.Device != opt.Device
			|| !string.Equals(old.ModelPackId, opt.ModelPackId, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(old.ModelVariant, opt.ModelVariant, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(old.ModelsDir, opt.ModelsDir, StringComparison.OrdinalIgnoreCase);
		var needRuntimeSync = old.DetLimitSideLen != opt.DetLimitSideLen
			|| Math.Abs(old.DetThresh - opt.DetThresh) > 1e-6
			|| Math.Abs(old.DetBoxThresh - opt.DetBoxThresh) > 1e-6
			|| old.UseCls != opt.UseCls;
		var serviceOn = opt.ServiceMode && !old.ServiceMode;
		var serviceOff = !opt.ServiceMode && old.ServiceMode;

		var httpInfo = opt.HttpEnabled ? $" · HTTP :{opt.HttpPort}" : "";
		var modeInfo = opt.ServiceMode ? " · 服务模式" : "";

		if (opt.ServiceMode) {
			// 常驻：session/runtime 变更或刚打开服务模式 → 预热，不 Invalidate
			if (needSessionReload || needRuntimeSync || serviceOn || !runner.HasEngine)
				tryservicewarmup($"参数已保存 · 热键 {opt.Hotkey}{httpInfo}{modeInfo}");
			else
				setstatus($"参数已保存 · 热键 {opt.Hotkey}{httpInfo}{modeInfo}");
		}
		else {
			// 非服务模式：session 变更才丢弃；runtime 下次识别时 ApplyRuntime
			if (needSessionReload) {
				try { runner.Invalidate(); } catch { }
			}
			else if (needRuntimeSync && runner.HasEngine) {
				// 已加载则同步 runtime，避免下次识别用旧阈值
				try { runner.Warmup(snapshotopt()); } catch { }
			}
			if (serviceOff)
				setstatus($"参数已保存 · 热键 {opt.Hotkey}{httpInfo} · 已关闭服务模式（引擎保持至下次改模型）");
			else
				setstatus($"参数已保存 · 热键 {opt.Hotkey}{httpInfo}");
		}
	}

	// ───────── ocr ─────────

	/// <param name="wallStartTick">端到端起点（TickCount）；默认从本方法开始计。</param>
	/// <param name="focusResult">false=托盘后台识别，结果上屏但不抢焦点/不弹主窗。</param>
	/// <param name="setImg">true=先 setimage（新图会重置两 Tab 识别状态）；false=图已上屏仅跑 OCR。</param>
	async Task runocrasync(BitmapSource bmp, int? wallStartTick = null, bool focusResult = true, bool setImg = true) {
		if (bmp == null) return;
		// 依赖：OpenCV + OCR 模型 + ORT（未装 GPU/核显时需 onnxcpu64）
		if (!FeaturePrompt.EnsureOpenCv(this)) {
			setstatus("未安装 OpenCV，已取消识别");
			ocrDoneForImg = true;
			return;
		}
		if (!FeaturePrompt.EnsureOcrModels(this)) {
			setstatus("未安装 OCR 模型，已取消识别");
			ocrDoneForImg = true;
			return;
		}
		if (!FeaturePrompt.EnsureOcrOrt(this)) {
			setstatus("未安装 ONNX Runtime，已取消识别");
			ocrDoneForImg = true;
			return;
		}
		var wall0 = wallStartTick ?? Environment.TickCount;

		// 新图优先：取消上一轮；禁止因 busy 直接 return（否则截图成功却不 setimage）
		try { ocrCts?.Cancel(); } catch { }
		ocrCts = new CancellationTokenSource();
		var ct = ocrCts.Token;
		var gen = System.Threading.Interlocked.Increment(ref ocrGen);

		// 先 busy，避免 SelectionChanged 在 busy 前再触发 ensure
		busy = true;
		busyKind = "ocr";
		// 先显示截图，再进识别（即使用户连截两次也能立刻看到最新图）
		// 不强制切换结果 Tab，保持用户当前 OCR/条码 选择
		if (setImg)
			setimage(bmp);
		setbusyui(true);
		setstatus("识别中…（可点「取消识别」）");
		var dev = opt.Device switch {
			OcrDevice.Gpu => "GPU",
			OcrDevice.IntelGpu => "核显",
			_ => "CPU",
		};
		var pack = string.IsNullOrWhiteSpace(opt.ModelVariant) ? (opt.ModelPackId ?? "模型") : opt.ModelVariant;
		ocrMetaText = $"识别中 · {pack} · {dev} · 边长{opt.DetLimitSideLen}";
		syncresultmetafromtab();
		lbtime.Text = DateTime.Now.ToString("HH:mm:ss");
		lbocrruntitle.Text = Loc.T("ocr.running");
		lbocrrunhint.Text = $"{pack} · {dev} · 边长 {opt.DetLimitSideLen}\n检测 → 方向 → 识别";
		eresult.Text = "";
		last = null;
		clearselection();
		drawoverlay();

		OcrResult result = null;
		Exception error = null;
		var cancelled = false;
		if (string.IsNullOrWhiteSpace(opt.ModelsDir) || !Directory.Exists(opt.ModelsDir))
			applydefaultmodel();
		var snap = snapshotopt();
		try {
			await Task.Run(() => {
				ct.ThrowIfCancellationRequested();
				var had = runner.HasEngine;
				using var mat = ImageUtil.Tobgr(bmp);
				ct.ThrowIfCancellationRequested();
				// ORT 推理为同步阻塞，取消在返回后生效并丢弃结果
				result = runner.Run(snap, mat);
				ct.ThrowIfCancellationRequested();
				if (!had && result != null && !ct.IsCancellationRequested) {
					Dispatcher.Invoke(() => {
						if (gen == ocrGen)
							setstatus($"模型已加载 · {runner.ModelLabel} · {runner.DeviceUsed} · 加载 {result.LoadMs}ms");
					});
				}
			}, ct);
		}
		catch (OperationCanceledException) {
			cancelled = true;
		}
		catch (Exception ex) {
			if (ct.IsCancellationRequested) cancelled = true;
			else error = ex;
		}
		var wallMs = Math.Max(0, Environment.TickCount - wall0);

		// 已被更新的一轮识别取代：勿清 busy / 勿覆盖 UI
		if (gen != ocrGen) return;

		busy = false;
		busyKind = null;
		setbusyui(false);
		// 无论成败都记一次，避免同一图反复自动识别
		ocrDoneForImg = true;

		if (cancelled || ct.IsCancellationRequested) {
			setstatus("已取消识别");
			ocrMetaText = "已取消";
			syncresultmetafromtab();
			await ensureactivetabasync();
			return;
		}
		if (error != null) {
			setstatus($"识别失败: {error.Message}");
			ocrMetaText = "识别失败: " + error.Message;
			syncresultmetafromtab();
			if (focusResult && IsVisible && WindowState != WindowState.Minimized)
				MessageBox.Show(this, error.ToString(), "OCR 错误", MessageBoxButton.OK, MessageBoxImage.Error);
			else
				showwarnmsg(error.ToString(), "OCR 错误");
			await ensureactivetabasync();
			return;
		}

		last = result;
		showresult(result, wallMs, focusResult: focusResult);
		// 识别过程中若切到二维码 Tab，结束后补跑一次
		await ensureactivetabasync();
	}

	/// <summary>当前图扫条码/二维码一次；结果写入「条码」Tab。</summary>
	async Task runqrasync(BitmapSource bmp, int? wallStartTick = null, bool focusResult = true, bool setImg = true) {
		if (bmp == null) return;
		if (!FeaturePrompt.EnsureOpenCv(this)) {
			setstatus("未安装 OpenCV，已取消条码识别");
			qrDoneForImg = true;
			return;
		}
		var wall0 = wallStartTick ?? Environment.TickCount;

		try { ocrCts?.Cancel(); } catch { }
		ocrCts = new CancellationTokenSource();
		var ct = ocrCts.Token;
		var gen = System.Threading.Interlocked.Increment(ref ocrGen);

		// 先 busy，避免 SelectionChanged 重复 ensure；不强制切换结果 Tab
		busy = true;
		busyKind = "qr";
		if (setImg)
			setimage(bmp);
		setbusyui(true);
		setstatus("识别条码中…（可点「取消识别」）");
		qrMetaText = "识别中 · 条码";
		syncresultmetafromtab();
		lbtime.Text = DateTime.Now.ToString("HH:mm:ss");
		lbocrruntitle.Text = Loc.T("qr.running");
		lbocrrunhint.Text = Loc.T("qr.running.hint");
		eqrresult.Text = "";
		lastQr = null;
		drawoverlay();

		QrResult result = null;
		Exception error = null;
		var cancelled = false;
		try {
			await Task.Run(() => {
				ct.ThrowIfCancellationRequested();
				result = QrScan.Run(bmp);
				ct.ThrowIfCancellationRequested();
			}, ct);
		}
		catch (OperationCanceledException) {
			cancelled = true;
		}
		catch (Exception ex) {
			if (ct.IsCancellationRequested) cancelled = true;
			else error = ex;
		}
		var wallMs = Math.Max(0, Environment.TickCount - wall0);

		if (gen != ocrGen) return;

		busy = false;
		busyKind = null;
		setbusyui(false);
		qrDoneForImg = true;

		if (cancelled || ct.IsCancellationRequested) {
			setstatus("已取消条码识别");
			qrMetaText = "已取消";
			syncresultmetafromtab();
			await ensureactivetabasync();
			return;
		}
		if (error != null) {
			setstatus($"条码识别失败: {error.Message}");
			qrMetaText = "识别失败: " + error.Message;
			syncresultmetafromtab();
			if (focusResult && IsVisible && WindowState != WindowState.Minimized)
				MessageBox.Show(this, error.ToString(), "条码", MessageBoxButton.OK, MessageBoxImage.Error);
			else
				showwarnmsg(error.ToString(), "条码");
			await ensureactivetabasync();
			return;
		}

		lastQr = result;
		showqrresult(result, wallMs, focusResult: focusResult);
		await ensureactivetabasync();
	}

	void cancelocr() {
		if (!busy) return;
		try { ocrCts?.Cancel(); } catch { }
		setstatus("正在取消识别…");
	}

	void opendiag() {
		try {
			var win = new DiagnosticsWindow(appExtraReport) { Owner = this };
			win.ShowDialog();
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "诊断", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void openinstallfeatures() {
		try {
			var win = new InstallFeaturesWindow();
			attachdialogowner(win);
			win.ShowDialog();
			if (win.NeedRefresh || win.NeedRestart)
				AfterFeatureInstall(win.NeedRestart);
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Loc.T("menu.install"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	/// <summary>安装窗关闭后刷新模型列表（供菜单与使用前提示共用）。</summary>
	internal void AfterFeatureInstall(bool needRestart) {
		try {
			modelUiLoading = true;
			packs = ModelCatalog.Scan();
			epack.ItemsSource = packs;
			var pack = packs.FirstOrDefault(p =>
					string.Equals(p.Id, opt.ModelPackId, StringComparison.OrdinalIgnoreCase))
				?? packs.FirstOrDefault();
			if (pack != null) {
				epack.SelectedItem = pack;
				fillvariants(pack, opt.ModelVariant);
			}
			modelUiLoading = false;
		}
		catch (Exception ex) {
			modelUiLoading = false;
			CaptureLog.Ex("install refresh ocr", ex);
		}
		try { scanasrmodels(); } catch (Exception ex) { CaptureLog.Ex("install refresh asr", ex); }
		try { scanttssmodels(); } catch (Exception ex) { CaptureLog.Ex("install refresh tts", ex); }
		try { scantrmodels(); } catch (Exception ex) { CaptureLog.Ex("install refresh tr", ex); }
		try { scanfacemodels(); } catch (Exception ex) { CaptureLog.Ex("install refresh face", ex); }
		try { refreshdeviceui(); } catch { }
		if (needRestart)
			setstatus("功能已安装 · 请重启程序以加载 GPU/核显运行库");
		else
			setstatus("功能安装完成 · 模型列表已刷新");
	}

	string appExtraReport() {
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("=== 应用状态 ===");
		sb.AppendLine($"Device: {opt.Device}");
		sb.AppendLine($"ModelPack: {opt.ModelPackId} / {opt.ModelVariant}");
		sb.AppendLine($"ModelsDir: {opt.ModelsDir}");
		sb.AppendLine($"DetLimit: {opt.DetLimitSideLen}");
		sb.AppendLine($"ServiceMode: {opt.ServiceMode}");
		sb.AppendLine($"Hotkey toggle window: {opt.Hotkey}");
		sb.AppendLine($"Hotkey snap: {opt.HotkeySnap}");
		sb.AppendLine($"Hotkey snap OCR: {opt.HotkeySnapOcr}");
		sb.AppendLine($"Hotkey board: {opt.HotkeyBoard}");
		sb.AppendLine($"Hotkey voice input: {opt.HotkeyVoiceInput}");
		sb.AppendLine($"Hotkey live caption: {opt.HotkeyLiveCaption}");
		sb.AppendLine($"HTTP: {(opt.HttpEnabled ? $"{opt.HttpHost}:{opt.HttpPort}" : "off")}");
		sb.AppendLine($"FaceModels: {FaceModels.ModelsRoot()} exists={Directory.Exists(FaceModels.ModelsRoot())}");
		sb.AppendLine($"FaceDet={opt.FaceDetModel} FaceReg={opt.FaceRegModel} FaceCompute={opt.FaceCompute}");
		sb.AppendLine($"Runner.HasEngine: {runner.HasEngine}");
		sb.AppendLine($"Runner.DeviceUsed: {runner.DeviceUsed}");
		sb.AppendLine($"Runner.ModelLabel: {runner.ModelLabel}");
		if (curimg != null)
			sb.AppendLine($"CurrentImage: {curimg.PixelWidth}x{curimg.PixelHeight}");
		return sb.ToString();
	}

	/// <param name="focusResult">false 时不 Focus 结果框（避免托盘后台识别把主窗拉到前台）。</param>
	void showresult(OcrResult r, int wallMs, bool focusResult = true) {
		if (r == null) return;
		lineOff = null;
		lineOffSrc = null;
		eresult.Text = r.FullText;
		// 预热 TextBox 焦点链，避免首次从图上选字时 Select 无效
		// 主窗隐藏或托盘后台识别时禁止 Focus（Focus 会 Activate 主窗）
		if (focusResult && !isresultqrtab() && IsVisible && WindowState != WindowState.Minimized) {
			try {
				var prev = Keyboard.FocusedElement;
				if (eresult.Focus()) {
					eresult.Select(0, 0);
					if (prev is UIElement ue && !ReferenceEquals(ue, eresult))
						ue.Focus();
					else {
						if (!pviewport.Focusable) pviewport.Focusable = true;
						pviewport.Focus();
					}
				}
			}
			catch { }
		}
		var conf = r.Lines.Count > 0 ? r.Lines.Average(x => x.Score) : 0;
		var model = string.IsNullOrEmpty(r.ModelLabel) ? "" : $" | {r.ModelLabel}";
		var res = curimg != null ? $" | {curimg.PixelWidth}×{curimg.PixelHeight}" : "";
		// 推理 = 模型管线 det/cls/rec；端到端 = 粘贴/打开/截图取图 → 界面出结果
		var inferS = (Math.Max(0, r.InferMs) / 1000.0).ToString("0.00");
		var wallS = (Math.Max(0, wallMs) / 1000.0).ToString("0.00");
		var loadPart = r.LoadMs > 0 ? $" · 加载 {(r.LoadMs / 1000.0):0.00}s" : "";
		// 详细耗时/置信度等只写右侧识别结果区，顶部状态不重复
		ocrMetaText = $"推理 {inferS}s · 端到端 {wallS}s{loadPart} | 置信度 {conf:0.00} | {r.DeviceUsed}{model}{res} | {r.Lines.Count} 行 | 边长{opt.DetLimitSideLen}";
		syncresultmetafromtab();
		lbtime.Text = DateTime.Now.ToString("HH:mm:ss");
		setstatus("完成");
		drawoverlay();
		// 结果面板出来后左侧视口尺寸已稳定，再 fit 一次保证居中
		schedulefit();
	}

	void showqrresult(QrResult r, int wallMs, bool focusResult = true) {
		if (r == null) return;
		eqrresult.Text = r.FullText;
		if (string.IsNullOrWhiteSpace(eqrresult.Text))
			eqrresult.Text = Loc.T("result.qr.empty");
		if (focusResult && isresultqrtab() && IsVisible && WindowState != WindowState.Minimized) {
			try {
				var prev = Keyboard.FocusedElement;
				if (eqrresult.Focus()) {
					eqrresult.Select(0, 0);
					if (prev is UIElement ue && !ReferenceEquals(ue, eqrresult))
						ue.Focus();
					else {
						if (!pviewport.Focusable) pviewport.Focusable = true;
						pviewport.Focus();
					}
				}
			}
			catch { }
		}
		var inferS = (Math.Max(0, r.InferMs) / 1000.0).ToString("0.00");
		var wallS = (Math.Max(0, wallMs) / 1000.0).ToString("0.00");
		var res = curimg != null ? $" | {curimg.PixelWidth}×{curimg.PixelHeight}" : "";
		qrMetaText = $"推理 {inferS}s · 端到端 {wallS}s | {r.DecodedCount} 个码{res}";
		syncresultmetafromtab();
		lbtime.Text = DateTime.Now.ToString("HH:mm:ss");
		setstatus(r.DecodedCount > 0 ? $"条码完成 · {r.DecodedCount} 个" : "条码完成 · 未检出");
		drawoverlay();
		schedulefit();
	}

	// ───────── image / overlay ─────────

	void setimage(BitmapSource bmp) {
		CaptureLog.Info($"setimage in={CaptureLog.Bmp(bmp)}");
		if (bmp == null) {
			CaptureLog.Info("setimage SKIP null");
			return;
		}
		bmp = ImageUtil.Withdpi(bmp, 96, 96);
		CaptureLog.Info($"setimage after Withdpi={CaptureLog.Bmp(bmp)}");
		curimg = bmp;
		// 新图：OCR / 二维码各重置为未识别，结果区清空
		ocrDoneForImg = false;
		qrDoneForImg = false;
		last = null;
		lastQr = null;
		try {
			eresult.Text = "";
			eqrresult.Text = "";
		}
		catch { }
		ocrMetaText = Loc.T("result.meta");
		qrMetaText = Loc.T("result.qr.meta");
		syncresultmetafromtab();
		imgview.Source = bmp;
		imgview.Width = bmp.PixelWidth;
		imgview.Height = bmp.PixelHeight;
		pstage.Width = bmp.PixelWidth;
		pstage.Height = bmp.PixelHeight;
		// 原始分辨率（像素）
		lbimgsize.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}";
		lbhint.Visibility = Visibility.Collapsed;
		CaptureLog.Info($"setimage done view={imgview.Width:0}x{imgview.Height:0} stage={pstage.Width:0}x{pstage.Height:0}");
		// 重置上次平移/缩放，避免叠在旧 pan 上
		tfscale.ScaleX = 1;
		tfscale.ScaleY = 1;
		tfpan.X = 0;
		tfpan.Y = 0;
		schedulefit();
	}

	/// <summary>立即 + 布局完成后 fit（截图/粘贴后视口常未就绪）。</summary>
	void schedulefit() {
		fitview();
		Dispatcher.BeginInvoke(new Action(fitview), System.Windows.Threading.DispatcherPriority.Loaded);
		Dispatcher.BeginInvoke(new Action(fitview), System.Windows.Threading.DispatcherPriority.ContextIdle);
	}

	/// <summary>将图片等比缩放并居中显示在图片区（fit）。</summary>
	void fitview() {
		if (curimg == null || curimg.PixelWidth < 1 || curimg.PixelHeight < 1) return;
		pviewport.UpdateLayout();
		var vw = pviewport.ActualWidth;
		var vh = pviewport.ActualHeight;
		// 布局未完成时不要用假尺寸算缩放
		if (vw < 16 || vh < 16) return;
		var iw = (double)curimg.PixelWidth;
		var ih = (double)curimg.PixelHeight;
		// 等比完整落入视口，略留边
		var s = Math.Min(vw / iw, vh / ih) * 0.96;
		s = Compat.Clamp(s, ZMIN, ZMAX);
		tfscale.ScaleX = s;
		tfscale.ScaleY = s;
		// Scale 在前、Translate 在后：平移用缩放后尺寸
		tfpan.X = (vw - iw * s) / 2;
		tfpan.Y = (vh - ih * s) / 2;
		updatezoomlabel();
	}

	void updatezoomlabel() {
		lbzoom.Text = $"{tfscale.ScaleX * 100:0}%";
	}

	void drawoverlay() {
		poverlay.Children.Clear();
		if (curimg == null) return;
		poverlay.Width = curimg.PixelWidth;
		poverlay.Height = curimg.PixelHeight;

		// 二维码 Tab：画绿色四边形
		if (isresultqrtab()) {
			drawqroverlay();
			return;
		}

		if (last == null) return;
		if (mntoggletext.IsChecked != true) return;

		var hasSel = hasselection();
		int sl = 0, sc = 0, el = 0, ec = 0;
		if (hasSel) ordercarets(out sl, out sc, out el, out ec);

		for (int i = 0; i < last.Lines.Count; i++) {
			var line = last.Lines[i];
			if (line.Box == null || line.Box.Length < 4) continue;
			if (string.IsNullOrEmpty(line.Text)) continue;

			var p0 = line.Box[0];
			var p1 = line.Box[1];
			var p2 = line.Box[2];
			var p3 = line.Box[3];
			var w = Math.Max(2.0, dist(p0, p1));
			var h = Math.Max(2.0, dist(p0, p3));
			var angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * 180.0 / Math.PI;
			var len = line.Text.Length;

			// 行内选区比例 [t0,t1) ∈ [0,1]
			float t0 = 0, t1 = 0;
			var partial = false;
			if (hasSel && i >= sl && i <= el && len > 0) {
				partial = true;
				if (sl == el && i == sl) {
					t0 = sc / (float)len;
					t1 = ec / (float)len;
				}
				else if (i == sl) {
					t0 = sc / (float)len;
					t1 = 1f;
				}
				else if (i == el) {
					t0 = 0f;
					t1 = ec / (float)len;
				}
				else {
					t0 = 0f;
					t1 = 1f;
				}
				if (t1 < t0) (t0, t1) = (t1, t0);
				if (t1 - t0 < 1e-4f) partial = false;
			}

			// 底：半透明黑 + 白字（Umi）
			var tb = new TextBlock {
				Text = line.Text,
				Foreground = TextFg,
				FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
				FontWeight = FontWeights.Medium,
				TextAlignment = TextAlignment.Left,
				TextWrapping = TextWrapping.NoWrap,
				Padding = new Thickness(1, 0, 1, 0),
				VerticalAlignment = System.Windows.VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
			};
			var box = new Border {
				Width = w,
				Height = h,
				Background = BoxFill,
				BorderBrush = BoxStroke,
				BorderThickness = new Thickness(0.5),
				IsHitTestVisible = false,
				SnapsToDevicePixels = true,
				Child = new Viewbox {
					Stretch = Stretch.Fill,
					StretchDirection = StretchDirection.Both,
					Child = tb,
				},
				RenderTransformOrigin = new Point(0, 0),
				RenderTransform = new RotateTransform(angle),
			};
			Canvas.SetLeft(box, p0.X);
			Canvas.SetTop(box, p0.Y);
			poverlay.Children.Add(box);

			// 选区高亮：行内局部四边形（可从句中到句中）
			if (partial) {
				var tl = lerp(p0, p1, t0);
				var tr = lerp(p0, p1, t1);
				var br = lerp(p3, p2, t1);
				var bl = lerp(p3, p2, t0);
				var poly = new System.Windows.Shapes.Polygon {
					Points = new PointCollection {
						new Point(tl.X, tl.Y),
						new Point(tr.X, tr.Y),
						new Point(br.X, br.Y),
						new Point(bl.X, bl.Y),
					},
					Fill = SelFill,
					Stroke = SelStroke,
					StrokeThickness = 1.0,
					IsHitTestVisible = false,
				};
				poverlay.Children.Add(poly);
			}
		}
	}

	void drawqroverlay() {
		if (lastQr?.Codes == null || lastQr.Codes.Count == 0) return;
		var stroke = brush(0xE6, 0x10, 0xB9, 0x81);
		var fill = brush(0x33, 0x10, 0xB9, 0x81);
		foreach (var code in lastQr.Codes) {
			if (code?.Box == null || code.Box.Length < 4) continue;
			var poly = new System.Windows.Shapes.Polygon {
				Points = new PointCollection {
					new Point(code.Box[0].X, code.Box[0].Y),
					new Point(code.Box[1].X, code.Box[1].Y),
					new Point(code.Box[2].X, code.Box[2].Y),
					new Point(code.Box[3].X, code.Box[3].Y),
				},
				Fill = fill,
				Stroke = stroke,
				StrokeThickness = 2.0,
				IsHitTestVisible = false,
			};
			poverlay.Children.Add(poly);
			if (string.IsNullOrEmpty(code.Text)) continue;
			// 左上角附近简短预览
			var minX = code.Box.Min(p => p.X);
			var minY = code.Box.Min(p => p.Y);
			var body = code.Text.Length > 36 ? code.Text.Substring(0, 36) + "…" : code.Text;
			var typ = string.IsNullOrEmpty(code.Type) ? "" : ("[" + code.Type + "] ");
			var preview = typ + body;
			var tb = new TextBlock {
				Text = preview,
				Foreground = brush(0xFF, 0x06, 0x5F, 0x46),
				Background = brush(0xE6, 0xEC, 0xFD, 0xF5),
				FontSize = 12,
				FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
				Padding = new Thickness(4, 2, 4, 2),
				IsHitTestVisible = false,
				TextWrapping = TextWrapping.NoWrap,
			};
			Canvas.SetLeft(tb, minX);
			Canvas.SetTop(tb, Math.Max(0, minY - 22));
			poverlay.Children.Add(tb);
		}
	}

	static double dist(Point2f a, Point2f b) {
		var dx = a.X - b.X;
		var dy = a.Y - b.Y;
		return Math.Sqrt(dx * dx + dy * dy);
	}

	static SolidColorBrush brush(byte a, byte r, byte g, byte b) {
		var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
		br.Freeze();
		return br;
	}

	// ───────── ui helpers ─────────

	Storyboard ocrSpinSb;

	void setbusyui(bool on) {
		// 图区按钮
		bcapture.IsEnabled = !on;
		bsnap.IsEnabled = !on;
		bpaste.IsEnabled = !on;
		bcopyimg.IsEnabled = !on;
		bsaveclip.IsEnabled = !on;
		// 菜单
		mncapture.IsEnabled = !on;
		mnsnap.IsEnabled = !on;
		mnboard.IsEnabled = !on;
		mnlongshot.IsEnabled = !on;
		mnsnapshots.IsEnabled = !on;
		mnrecord.IsEnabled = !on;
		mnrecordopt.IsEnabled = !on;
		mngifrecord.IsEnabled = !on;
		mngifrecordopt.IsEnabled = !on;
		mnpaste.IsEnabled = !on;
		mncopyimg.IsEnabled = !on;
		mnsaveclip.IsEnabled = !on;
		mnpdf.IsEnabled = !on;
		mnsettings.IsEnabled = !on;
		mninstall.IsEnabled = !on;
		mndiag.IsEnabled = !on;
		epack.IsEnabled = !on;
		evariant.IsEnabled = !on;
		edevice.IsEnabled = !on;
		edetlen.IsEnabled = !on;
		mncancelocr.IsEnabled = on;
		bcopy.IsEnabled = !on;
		// 右侧「正在识别中」面板
		try {
			pocrrunning.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
			bocrrunningbadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
			if (on) {
				pocrbar.IsIndeterminate = true;
				startocrspin();
			}
			else {
				pocrbar.IsIndeterminate = false;
				stopocrspin();
			}
		}
		catch { }
	}

	void startocrspin() {
		try {
			stopocrspin();
			ocrSpinSb = new Storyboard();
			var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) {
				RepeatBehavior = RepeatBehavior.Forever,
			};
			Storyboard.SetTarget(anim, tfocrspin);
			Storyboard.SetTargetProperty(anim, new PropertyPath(RotateTransform.AngleProperty));
			ocrSpinSb.Children.Add(anim);
			ocrSpinSb.Begin();
		}
		catch { }
	}

	void stopocrspin() {
		try {
			ocrSpinSb?.Stop();
			ocrSpinSb = null;
			if (tfocrspin != null) tfocrspin.Angle = 0;
		}
		catch { }
	}

	void setstatus(string s) {
		lbstatus.Text = s;
	}

	static bool isimage(string path) {
		var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
		return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tif" or ".tiff";
	}

	static bool ispdf(string path) {
		var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
		return ext == ".pdf";
	}

	void applydefaultmodel() {
		var pack = ModelCatalog.Find(opt.ModelPackId);
		if (pack == null) {
			opt.ModelsDir = System.IO.Path.Combine(
				AppDomain.CurrentDomain.BaseDirectory, "models", "umi");
			return;
		}
		opt.ModelPackId = pack.Id;
		opt.ModelsDir = pack.Dir;
		var v = pack.FindVariant(opt.ModelVariant);
		if (v != null) opt.ModelVariant = v.Title;
	}
}
