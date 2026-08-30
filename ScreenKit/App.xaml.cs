using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace ScreenKit;

public partial class App : System.Windows.Application {
	// 单实例：同名 Mutex + 激活事件（二次启动时唤起已有窗口）
	const string MUTEX_NAME = "Local\\ScreenKit_SingleInstance";
	const string ACTIVATE_EVENT = "Local\\ScreenKit_Activate";
	Mutex singleMutex;
	EventWaitHandle activateEvent;
	volatile bool exitRequested;

	protected override void OnStartup(StartupEventArgs e) {
		var args = e.Args ?? Array.Empty<string>();

		// 自更新应用：尽早处理，不初始化 CUDA / 不占单实例锁 / 不启动 GUI
		if (AppUpdater.IsApplyUpdateArgs(args)) {
			var code = AppUpdater.RunApplyUpdate(args);
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

		// GUI 单实例
		if (!trysingleinstance()) {
			signalactivate();
			Environment.Exit(0);
			return;
		}

		initcuda();

		base.OnStartup(e);
		var win = new MainWindow();
		MainWindow = win;
		win.Show();
	}

	protected override void OnExit(ExitEventArgs e) {
		exitRequested = true;
		try { activateEvent?.Set(); } catch { }
		try { activateEvent?.Dispose(); } catch { }
		activateEvent = null;
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
			singleMutex = new Mutex(true, MUTEX_NAME, out var created);
			if (!created) {
				try { activateEvent.Dispose(); } catch { }
				activateEvent = null;
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

	void waitactivate() {
		while (!exitRequested) {
			try {
				if (activateEvent == null) break;
				if (!activateEvent.WaitOne(500)) continue;
				if (exitRequested) break;
				try {
					Dispatcher.BeginInvoke(new Action(activatemain));
				}
				catch { }
			}
			catch {
				if (exitRequested) break;
			}
		}
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
