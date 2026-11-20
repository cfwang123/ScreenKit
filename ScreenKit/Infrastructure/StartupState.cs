namespace ScreenKit;

/// <summary>上次退出时哪些独立窗口是开着的，下次启动照着打开。存在 data.json 的 startup 段。</summary>
sealed class StartupState {
	public bool PerfMon { get; set; }
	public bool Note { get; set; }

	public static StartupState Load() {
		try { return DataStore.Get<StartupState>("startup") ?? new StartupState(); }
		catch { return new StartupState(); }
	}

	public void Save() {
		try { DataStore.Put("startup", this); }
		catch { }
	}
}
