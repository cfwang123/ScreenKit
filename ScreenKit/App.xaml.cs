using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;

namespace ScreenKit;

public partial class App : System.Windows.Application {
	// 单实例：同名 Mutex + 激活事件（二次启动时唤起已有窗口）
	const string MUTEX_NAME = "Local\\ScreenKit_SingleInstance";
	const string ACTIVATE_EVENT = "Local\\ScreenKit_Activate";
	const string EXIT_EVENT = "Local\\ScreenKit_RequestExit";
	Mutex singleMutex;
	EventWaitHandle activateEvent;
	EventWaitHandle exitEvent;
	volatile bool exitRequested;

	static void registerenc() =>
		System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

	static bool applyupdate(string[] args) {
		if (!AppUpdater.IsApplyUpdateArgs(args)) return false;
		var code = AppUpdater.RunApplyUpdate(args);
		Environment.Exit(code);
		return true;
	}

	protected override void OnStartup(StartupEventArgs e) {
		var args = e.Args ?? Array.Empty<string>();

		// 自更新应用最先处理：这一步之前不能碰需要额外程序集的代码。
		// 更新进程从 tmp/ 启动，依赖可能还没拷全，这里一加载就直接崩溃，覆盖永远不会发生。
		// 单独成方法：OnStartup 里还要用 CodePages，JIT 会在进方法前就把那个程序集加载进来。
		// 更新进程从 tmp/ 启动时这个程序集未必在，一加载就崩溃，覆盖一步到不了。
		if (applyupdate(args)) return;

		registerenc();

		// 文件 → 重启：等旧进程退出后再打开，不占单实例锁
		if (restartafter(args)) {
			Environment.Exit(0);
			return;
		}

		// 编译脚本请求退出：关掉 HTTP 后结束，不走 taskkill
		if (args.Any(a => a == "--request-exit")) {
			signalrequestexit();
			Environment.Exit(0);
			return;
		}

		// USB AOA：独立进程请求配件并桥接 TCP，主进程绝不碰 LibUsb
		if (args.Any(a => a == "--cast-aoa")) {
			AppConfig.initlogswitch();
			void lg(string s) {
				if (!CaptureLog.Enabled) return;
				try {
					var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
					Directory.CreateDirectory(dir);
					File.AppendAllText(Path.Combine(dir, "cast_sess.txt"),
						$"{DateTime.Now:HH:mm:ss} {s}\n");
				}
				catch { }
			}
			try {
				lg("AOA 助手启动");
				CastUsbHost.RunAoaBridge(lg);
			}
			catch (Exception ex) { lg($"AOA 助手异常: {ex.Message}"); }
			Environment.Exit(0);
			return;
		}

		// 资源管理器菜单：不占单实例锁。粘贴才开进度窗。
		if (FastCopy.IsShell(args)) {
			if (FastCopy.NeedsWindow(args)) {
				ShutdownMode = ShutdownMode.OnExplicitShutdown;
				base.OnStartup(e);
				var code = FastCopy.RunShell(args);
				Shutdown(code);
				return;
			}
			Environment.Exit(FastCopy.RunShell(args));
			return;
		}

		// USB 抓包窗口：由主窗口以管理员身份用 start 拉起，只开这一个窗口，不占单实例锁。
		if (args.Any(a => a == "--usbcap")) {
			AppConfig.initlogswitch();
			base.OnStartup(e);
			var usbwin = new UsbCapWindow();
			MainWindow = usbwin;
			usbwin.Show();
			return;
		}

		// 网络监控窗口：由主窗口以管理员身份用 start 拉起，只开这一个窗口，不占单实例锁。
		if (args.Any(a => a == "--netprobe")) {
			AppConfig.initlogswitch();
			base.OnStartup(e);
			var netwin = new NetProbeWindow();
			MainWindow = netwin;
			netwin.Show();
			return;
		}

		// SQL 方言转换窗口冒烟：只开这个窗口，可带一个脚本文件；UIA 据此检查控件与识别结果
		if (args.Any(a => a == "--test-sqlconv-ui")) {
			AppConfig.initlogswitch();
			base.OnStartup(e);
			var cvw = new SqlConvWindow();
			MainWindow = cvw;
			cvw.Show();
			var ci = Array.IndexOf(args, "--test-sqlconv-ui");
			if (ci + 1 < args.Length && !args[ci + 1].StartsWith("--", StringComparison.Ordinal))
				cvw.SelfTest(args[ci + 1]);
			return;
		}

		// SOAP 调试窗口冒烟：只开这个窗口，便于 UIA 检查。
		if (args.Any(a => a == "--test-soap-ui")) {
			base.OnStartup(e);
			var w = new SoapDebugWindow();
			MainWindow = w;
			w.Show();
			return;
		}

		// USB 抓包解析自测：不启动 GUI，结果写到 usbcap_selftest.log
		if (args.Any(a => a == "--test-usbcap")) {
			var lines = new System.Collections.Generic.List<string>();
			var code = 1;
			try { code = UsbCaptureService.SelfTest(lines.Add); }
			catch (Exception ex) { lines.Add("[ERR] " + ex); }
			try {
				File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usbcap_selftest.log"),
					lines, new UTF8Encoding(false));
			}
			catch { }
			Environment.Exit(code);
			return;
		}

		// USB 抓包真抓自测：按地址过滤跑几秒，看有没有读到包（需管理员）
		if (args.Any(a => a == "--test-usbcap-live")) {
			var lines = new System.Collections.Generic.List<string>();
			var secs = 5;
			List<int> addrs = new();
			foreach (var a in args) {
				if (a.StartsWith("--secs=", StringComparison.Ordinal) && int.TryParse(a.Substring(7), out var s)) secs = s;
				if (a.StartsWith("--addr=", StringComparison.Ordinal))
					foreach (var part in a.Substring(7).Split(','))
						if (int.TryParse(part.Trim(), out var n)) addrs.Add(n);
			}
			var code = 1;
			try { code = UsbCaptureService.LiveTest(lines.Add, secs, addrs.Count > 0 ? addrs : null); }
			catch (Exception ex) { lines.Add("[ERR] " + ex); }
			try {
				var text = string.Join("\n", lines);
				File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usbcap_live.log"), text, new UTF8Encoding(false));
				Console.Out.Write(text);
			}
			catch { }
			Environment.Exit(code);
			return;
		}

		// SOAP 调试核心自测：不启动 GUI，结果写到 soap_selftest.log
		if (args.Any(a => a == "--test-soap")) {
			var lines = new System.Collections.Generic.List<string>();
			var code = 1;
			try { code = ScreenKit.Soap.SoapSelfTest.Run(lines.Add, s => lines.Add("[ERR] " + s)); }
			catch (Exception ex) { lines.Add("[ERR] " + ex); }
			try {
				File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soap_selftest.log"),
					lines, new UTF8Encoding(false));
			}
			catch { }
			Environment.Exit(code);
			return;
		}

		// 命令行模式：不启动 GUI、不占单实例锁
		if (Cli.IsCli(args)) {
			initcuda();
			var code = Cli.Run(args);
			Shutdown(code);
			return;
		}

		AppConfig.initlogswitch();

		void sesslog(string s) {
			if (!CaptureLog.Enabled) return;
			try {
				var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
				Directory.CreateDirectory(dir);
				File.AppendAllText(Path.Combine(dir, "cast_sess.txt"),
					$"{DateTime.Now:HH:mm:ss} {s}\n");
			}
			catch { }
		}

		try { CastStale.Purge(sesslog); }
		catch (Exception ex) { sesslog($"清残留: {ex.Message}"); }

		// GUI 单实例（其它目录的旧进程已清掉；同目录二次启动仍唤起已有窗口）
		var got = trysingleinstance();
		if (!got && !CastStale.SamePathStillRunning()) {
			try { CastStale.Purge(sesslog); } catch { }
			Thread.Sleep(400);
			got = trysingleinstance();
		}
		if (!got) {
			if (CastStale.SamePathStillRunning()) {
				signalactivate();
				Environment.Exit(0);
				return;
			}
			sesslog("单实例锁仍被占，继续启动");
		}

		ChatRoom.PurgeUploads();
		initcuda();
		installcrashtrap();

		base.OnStartup(e);
		var win = new MainWindow();
		MainWindow = win;
		// --hidden / --tray：启动后不显示主窗口，直接留在托盘。
		// 不调用 Show，只用 EnsureHandle 建出句柄（热键、前台跟踪、托盘菜单都靠它），全程无闪动。
		// 参数设置里勾了「启动后隐藏到托盘」也走同一条路（配置在 MainWindow 构造时已读入）。
		var starttray = args.Any(a => a is "--tray" or "--hidden" or "--start-hidden") || win.StartHiddenToTray;
		if (starttray) win.starthidden();
		else win.Show();
		// --play <文件>：直接打开媒体播放器并播放
		var playIdx = Array.IndexOf(args, "--play");
		var shotIdx = Array.IndexOf(args, "--player-shot");
		if (playIdx >= 0) {
			var playPath = playIdx + 1 < args.Length ? args[playIdx + 1] : null;
			win.Dispatcher.BeginInvoke(new Action(() => {
				win.openplayer();
				var pw0 = Application.Current.Windows.OfType<PlayerWindow>().FirstOrDefault();
				if (pw0 != null && !string.IsNullOrWhiteSpace(playPath)) pw0.PlaySingle(playPath);
			}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		}
		// --form normal|fullscreen：启动后直接切到指定形态
		var formIdx = Array.IndexOf(args, "--form");
		if (playIdx >= 0 && formIdx >= 0 && formIdx + 1 < args.Length) {
			var formName = args[formIdx + 1].ToLowerInvariant();
			win.Dispatcher.BeginInvoke(new Action(() => {
				var pw1 = Application.Current.Windows.OfType<PlayerWindow>().FirstOrDefault();
				if (pw1 == null) return;
				pw1.SetForm(formName == "fullscreen" ? PlayerForm.Fullscreen : PlayerForm.Normal);
			}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		}
		// --player-shot <png> [毫秒] [seek秒] [目标形态] [右栏页签 list|study|subtitle]：自测用，渲染窗口成 PNG 后退出
		if (playIdx >= 0 && shotIdx >= 0 && shotIdx + 1 < args.Length) {
			var shotPath = args[shotIdx + 1];
			var delay = shotIdx + 2 < args.Length && int.TryParse(args[shotIdx + 2], out var d0) ? d0 : 4000;
			var seekTo = shotIdx + 3 < args.Length && double.TryParse(args[shotIdx + 3],
				System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s0) ? s0 : -1;
			var formArg = shotIdx + 4 < args.Length ? args[shotIdx + 4] : "";
			var tabArg = shotIdx + 5 < args.Length ? args[shotIdx + 5].ToLowerInvariant() : "";
			win.Dispatcher.BeginInvoke(new Action(async () => {
				var log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
				Directory.CreateDirectory(log);
				var txt = Path.Combine(log, "player_shot.txt");
				var sb = new System.Text.StringBuilder();
				// 等播放器开出来、字幕加载完
				await System.Threading.Tasks.Task.Delay(600);
				var pw = Application.Current.Windows.OfType<PlayerWindow>().FirstOrDefault();
				if (pw == null) { File.WriteAllText(txt, "NO_PLAYER_WINDOW"); Shutdown(0); return; }
				if (seekTo >= 0) pw.SeekTo(seekTo);
				if (formArg.Length > 0)
					pw.SetForm(formArg.ToLowerInvariant() == "fullscreen" ? PlayerForm.Fullscreen : PlayerForm.Normal);
				if (tabArg.Length > 0)
					pw.SetTab(tabArg == "list" ? SideTab.List : tabArg == "subtitle" ? SideTab.Subtitle : SideTab.Study);
				await System.Threading.Tasks.Task.Delay(delay);
				// --player-subdy <像素>：自测字幕上下拖动（正数向上）
				var subdyArg = Array.IndexOf(args, "--player-subdy");
				if (subdyArg >= 0 && subdyArg + 1 < args.Length
					&& double.TryParse(args[subdyArg + 1], System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out var sdy)) {
					sb.AppendLine("subdy: " + pw.NudgeSubtitle(sdy));
				}
				// --player-seek <秒[,秒…]>：自测用，模拟点进度条跳到指定秒（可连跳多次）
				var seekArg = Array.IndexOf(args, "--player-seek");
				if (seekArg >= 0 && seekArg + 1 < args.Length) {
					foreach (var part in args[seekArg + 1].Split(',')) {
						if (!double.TryParse(part, System.Globalization.NumberStyles.Float,
							System.Globalization.CultureInfo.InvariantCulture, out var sk)) continue;
						sb.AppendLine("seek: " + pw.SeekBySlider(sk));
						await System.Threading.Tasks.Task.Delay(2000);
					}
				}
				var baseName = Path.Combine(Path.GetDirectoryName(shotPath) ?? ".", Path.GetFileNameWithoutExtension(shotPath));
				// --player-word <词>：点一个生词，验证会弹出词典窗口
				var wordArg = Array.IndexOf(args, "--player-word");
				if (wordArg >= 0 && wordArg + 1 < args.Length) {
					sb.AppendLine("word: " + pw.ClickWord(args[wordArg + 1]));
					await System.Threading.Tasks.Task.Delay(1500);
					sb.AppendLine("dict: " + pw.DictState());
					sb.AppendLine(pw.ShotTo($"{baseName}_dict.png"));
				}
				sb.AppendLine(pw.ShotTo(shotPath));
				// 再连拍两张，方便看蒙版推进
				for (var i = 1; i <= 2; i++) {
					await System.Threading.Tasks.Task.Delay(1500);
					sb.AppendLine(pw.ShotTo($"{baseName}_{i}.png"));
				}
				// --player-click stage：在画面区点一下，验证播放/暂停切换
				// --player-click 行,字：点字幕里的字，验证取词与释义
				var clickArg = Array.IndexOf(args, "--player-click");
				if (clickArg >= 0 && clickArg + 1 < args.Length) {
					if (args[clickArg + 1].Equals("stop", StringComparison.OrdinalIgnoreCase)) {
						await System.Threading.Tasks.Task.Delay(1200);
						sb.AppendLine("stop: " + pw.ClickStopAndPlay());
						await System.Threading.Tasks.Task.Delay(1200);
						sb.AppendLine("stop2: " + pw.ClickStage());
						await System.Threading.Tasks.Task.Delay(300);
						sb.AppendLine(pw.ShotTo($"{baseName}_stop.png"));
						File.WriteAllText(txt, sb.ToString());
						Shutdown(0);
						return;
					}
					if (args[clickArg + 1].Equals("stage", StringComparison.OrdinalIgnoreCase)) {
						sb.AppendLine("stage: " + pw.ClickStage());
						await System.Threading.Tasks.Task.Delay(600);
						sb.AppendLine("stage2: " + pw.ClickStage());
						await System.Threading.Tasks.Task.Delay(300);
						sb.AppendLine(pw.ShotTo($"{baseName}_click.png"));
						File.WriteAllText(txt, sb.ToString());
						Shutdown(0);
						return;
					}
					var parts = args[clickArg + 1].Split(',');
					var li = parts.Length > 0 && int.TryParse(parts[0], out var l0) ? l0 : 0;
					var ci = parts.Length > 1 && int.TryParse(parts[1], out var c0) ? c0 : 0;
					sb.AppendLine("click: " + pw.ClickChar(li, ci));
					await System.Threading.Tasks.Task.Delay(600);
					sb.AppendLine(pw.ShotTo($"{baseName}_click.png"));
				}
				File.WriteAllText(txt, sb.ToString());
				Shutdown(0);
			}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		}
		if (args.Any(a => a == "--test-ui-toast")) {
			win.Dispatcher.BeginInvoke(new Action(() => {
				UiToast.Show(win, Loc.T("sf.recv.clip", "toast-test.bin"));
			}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		}
		if (args.Any(a => a == "--test-sf-marquee")) {
			var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sf-marquee-test.log");
			win.Dispatcher.BeginInvoke(new Action(() => {
				win.sfmarqueeuitest(logPath);
			}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		}
		if (args.Any(a => a == "--test-sf-menu")) {
			var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sf-menu-test.log");
			win.Dispatcher.BeginInvoke(new Action(() => {
				win.sfmenutest(logPath);
			}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		}
	}

	protected override void OnExit(ExitEventArgs e) {
		exitRequested = true;
		try { activateEvent?.Set(); } catch { }
		try { activateEvent?.Dispose(); } catch { }
		activateEvent = null;
		try { exitEvent?.Dispose(); } catch { }
		exitEvent = null;
		try {
			if (singleMutex != null) {
				try { singleMutex.ReleaseMutex(); } catch { }
				singleMutex.Dispose();
			}
		}
		catch { }
		singleMutex = null;
		base.OnExit(e);
	}

	// 兜底：任何线程的未处理异常都留一份堆栈，别让用户只看到闪退
	void installcrashtrap() {
		AppDomain.CurrentDomain.UnhandledException += (_, e) => crashlog(e.ExceptionObject as Exception, "AppDomain");
		System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => {
			crashlog(e.Exception, "Task");
			try { e.SetObserved(); } catch { }
		};
		DispatcherUnhandledException += (_, e) => {
			crashlog(e.Exception, "Dispatcher");
			e.Handled = true;
		};
	}

	static int crashat;
	static int crashcnt;

	static void crashlog(Exception ex, string src) {
		if (ex == null) return;
		try {
			var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(dir);
			var path = Path.Combine(dir, "crash.txt");
			// 同一波异常会连发，10 秒内最多记 3 条
			var now = Environment.TickCount;
			if (crashat != 0 && unchecked(now - crashat) < 10000) {
				if (++crashcnt > 3) return;
			}
			else {
				crashat = now;
				crashcnt = 0;
			}
			var head = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{src}] v{typeof(App).Assembly.GetName().Version}";
			File.AppendAllText(path, $"{head}\n{ex}\n\n", new UTF8Encoding(false));
		}
		catch { }
	}

	static void initcuda() {
		try {
			CudaBootstrap.Init();
		}
		catch (Exception ex) {
			try { CudaBootstrap.MarkGpuFailed(ex.Message); } catch { }
			try {
				if (string.IsNullOrEmpty(CudaBootstrap.LastReport))
					CudaBootstrap.LastReport = ex.ToString();
			}
			catch { }
		}
		if (!CaptureLog.Enabled) return;
		try {
			var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(logDir);
			var body = (CudaBootstrap.LastReport ?? "") + "\nGpuStatus=" + CudaBootstrap.GpuStatus
				+ "\nIsGpuReady=" + CudaBootstrap.IsGpuReady;
			File.WriteAllText(Path.Combine(logDir, "cuda_bootstrap.log"), body, Encoding.UTF8);
		}
		catch { }
	}

	bool trysingleinstance() {
		try {
			activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ACTIVATE_EVENT);
			exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EXIT_EVENT);
			singleMutex = new Mutex(true, MUTEX_NAME, out var created);
			if (!created) {
				try { activateEvent.Dispose(); } catch { }
				activateEvent = null;
				try { exitEvent.Dispose(); } catch { }
				exitEvent = null;
				try { singleMutex.Dispose(); } catch { }
				singleMutex = null;
				return false;
			}
			_ = Task.Run(waitactivate);
			return true;
		}
		catch {
			return true;
		}
	}

	static void signalactivate() {
		try {
			using var ev = EventWaitHandle.OpenExisting(ACTIVATE_EVENT);
			ev.Set();
		}
		catch { }
	}

	static bool restartafter(string[] args) {
		if (args == null) return false;
		for (int i = 0; i < args.Length; i++) {
			if (args[i] != "--restart-after") continue;
			var pid = 0;
			if (i + 1 < args.Length) int.TryParse(args[i + 1], out pid);
			if (pid > 0) {
				try {
					using var p = Process.GetProcessById(pid);
					if (!p.HasExited) p.WaitForExit(20000);
				}
				catch { }
			}
			Thread.Sleep(400);
			var exe = "";
			try { exe = Process.GetCurrentProcess().MainModule?.FileName ?? ""; } catch { }
			if (exe.Length == 0)
				exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenKit.exe");
			var dir = Path.GetDirectoryName(exe) ?? AppDomain.CurrentDomain.BaseDirectory;
			Process.Start(new ProcessStartInfo {
				FileName = exe,
				WorkingDirectory = dir,
				UseShellExecute = true,
			});
			return true;
		}
		return false;
	}

	static void signalrequestexit() {
		try {
			using var ev = EventWaitHandle.OpenExisting(EXIT_EVENT);
			ev.Set();
		}
		catch { }
	}

	void waitactivate() {
		while (!exitRequested) {
			WaitHandle activate = activateEvent;
			WaitHandle exit = exitEvent;
			if (activate == null) break;
			var handles = exit == null ? new[] { activate } : new[] { activate, exit };
			int i;
			try { i = WaitHandle.WaitAny(handles, 500); }
			catch {
				if (exitRequested) break;
				continue;
			}
			if (i == WaitHandle.WaitTimeout || exitRequested) continue;
			try {
				if (i == 1) Dispatcher.BeginInvoke(new Action(requestexit));
				else Dispatcher.BeginInvoke(new Action(activatemain));
			}
			catch { }
			if (i == 1) break;
		}
	}

	void requestexit() {
		try {
			if (MainWindow is MainWindow w) w.RequestExit();
		}
		catch { }
	}

	void activatemain() {
		try {
			var w = MainWindow;
			if (w == null) return;
			if (!w.IsVisible) w.Show();
			if (w.WindowState == WindowState.Minimized)
				w.WindowState = WindowState.Normal;
			w.Activate();
			w.Topmost = true;
			w.Topmost = false;
			w.Focus();
		}
		catch { }
	}
}
