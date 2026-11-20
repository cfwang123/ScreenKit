namespace ScreenKit;

/// <summary>记住退出时性能监控、桌面便笺是否开着，下次启动照样开回来。</summary>
public partial class MainWindow {
	readonly StartupState startup = StartupState.Load();

	/// <summary>启动后把上次开着的独立小窗恢复出来</summary>
	void restorestartup() {
		if (!startup.PerfMon && !startup.Note) return;
		// 等主窗第一帧落定再开，免得两个窗口抢前台
		Dispatcher.BeginInvoke(new Action(() => {
			if (forceExit) return;
			try { if (startup.PerfMon) openperfmon(); } catch { }
			try { if (startup.Note) opennote(); } catch { }
		}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
	}

	/// <summary>退出前记下这两个窗口是否还开着</summary>
	void savestartup() {
		try {
			startup.PerfMon = perfMonWin != null;
			startup.Note = noteWin != null;
			startup.Save();
		}
		catch { }
	}
}
