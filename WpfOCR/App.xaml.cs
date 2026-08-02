using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace WpfOCR;

public partial class App : System.Windows.Application {
	// 单实例：同名 Mutex + 激活事件（二次启动时唤起已有窗口）
	const string MUTEX_NAME = "Local\\WpfOCR_SingleInstance";
	const string ACTIVATE_EVENT = "Local\\WpfOCR_Activate";
	Mutex singleMutex;
	EventWaitHandle activateEvent;
	volatile bool exitRequested;

	protected override void OnStartup(StartupEventArgs e) {
		// 自更新应用：尽早处理，不初始化 CUDA / 不占单实例锁 / 不启动 GUI
		if (AppUpdater.IsApplyUpdateArgs(e.Args)) {
			var code = AppUpdater.RunApplyUpdate(e.Args);
			// 硬退出，避免走 WPF 关窗路径
			Environment.Exit(code);
			return;
		}

		// 命令行模式：不启动 GUI、不占单实例锁
		if (Cli.IsCli(e.Args)) {
			initcuda();
			var code = Cli.Run(e.Args);
			Shutdown(code);
			return;
		}

		// GUI 单实例：必须在创建任何窗口之前判定
		// 已在运行 → 只通知主实例显示，本进程立刻退出（勿 Shutdown，避免误走关窗确认）
		if (!trysingleinstance()) {
			signalactivate();
			// 硬退出：不进入 WPF 关窗/OnExit 路径，否则可能弹出「确认退出？」
			Environment.Exit(0);
			return;
		}

		initcuda();

		base.OnStartup(e);
		// 不用 StartupUri，仅主实例创建主窗口，避免二次进程误建窗
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
		// 必须在任何 ORT 加载前检测/准备 GPU；失败只记日志，不崩溃
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

	/// <returns>true=本进程为主实例；false=已有实例在运行。</returns>
	bool trysingleinstance() {
		try {
			// 先建/开激活事件，再抢 Mutex
			activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ACTIVATE_EVENT);
			singleMutex = new Mutex(true, MUTEX_NAME, out var created);
			if (!created) {
				try { activateEvent.Dispose(); } catch { }
				activateEvent = null;
				try { singleMutex.Dispose(); } catch { }
				singleMutex = null;
				return false;
			}
			// 后台等待二次启动信号 → 弹出主窗口
			_ = Task.Run(waitactivate);
			return true;
		}
		catch {
			// 单实例失败时不挡启动
			return true;
		}
	}

	/// <summary>通知已运行的主实例显示窗口（二次进程调用）。</summary>
	static void signalactivate() {
		try {
			using var ev = EventWaitHandle.OpenExisting(ACTIVATE_EVENT);
			ev.Set();
		}
		catch {
			// 主实例尚未建好事件时忽略
		}
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
			// 仅显示/置前，绝不 Close
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
