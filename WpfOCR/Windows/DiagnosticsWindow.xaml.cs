using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace WpfOCR;

/// <summary>诊断页：CUDA / DirectML / 路径 / 多显示器 DPI。</summary>
partial class DiagnosticsWindow : Window {
	readonly Func<string> extraReport;

	internal DiagnosticsWindow(Func<string> appExtraReport = null) {
		InitializeComponent();
		extraReport = appExtraReport;
		brefresh.Click += (_, _) => refresh();
		bcopy.Click += (_, _) => {
			try {
				Clipboard.SetText(elog.Text ?? "");
			}
			catch (Exception ex) {
				MessageBox.Show(this, ex.Message, "复制", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		};
		bopenlog.Click += (_, _) => openlogdir();
		bclose.Click += (_, _) => Close();
		WindowEsc.Attach(this);
		Loaded += (_, _) => refresh();
	}

	void refresh() {
		var sb = new StringBuilder();
		try { sb.AppendLine(CudaBootstrap.BuildDiagnostics()); }
		catch (Exception ex) { sb.AppendLine("CudaBootstrap: " + ex); }
		sb.AppendLine();
		try { sb.AppendLine(NativeRuntime.StatusReport()); }
		catch (Exception ex) { sb.AppendLine("NativeRuntime: " + ex); }
		sb.AppendLine();
		try { sb.AppendLine(ScreenDpi.BuildReport()); }
		catch (Exception ex) { sb.AppendLine("ScreenDpi: " + ex); }
		sb.AppendLine();
		sb.AppendLine("=== 路径 ===");
		sb.AppendLine($"Config: {AppConfig.ConfigPath}");
		sb.AppendLine($"ModelsRoot: {ModelCatalog.ModelsRoot()}");
		sb.AppendLine($"exists ocrmodels: {Directory.Exists(ModelCatalog.ModelsRoot())}");
		var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
		sb.AppendLine($"LogDir: {logDir}");
		var cudaLog = Path.Combine(logDir, "cuda_bootstrap.log");
		if (File.Exists(cudaLog)) {
			sb.AppendLine();
			sb.AppendLine("--- log/cuda_bootstrap.log (尾部) ---");
			try {
				var all = File.ReadAllText(cudaLog, Encoding.UTF8);
				if (all.Length > 4000) all = all[^4000..];
				sb.AppendLine(all);
			}
			catch (Exception ex) { sb.AppendLine(ex.Message); }
		}
		if (extraReport != null) {
			sb.AppendLine();
			try { sb.AppendLine(extraReport()); }
			catch (Exception ex) { sb.AppendLine("App: " + ex.Message); }
		}
		elog.Text = sb.ToString();
		elog.CaretIndex = 0;
		elog.ScrollToHome();
	}

	void openlogdir() {
		try {
			var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(logDir);
			Process.Start(new ProcessStartInfo {
				FileName = logDir,
				UseShellExecute = true,
			});
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, "打开日志目录", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
