namespace ScreenKit;

/// <summary>进程架构标签（诊断用）。</summary>
static class ArchBootstrap {
	public static string CurrentLabel => Environment.Is64BitProcess ? "x64" : "x86";
}
