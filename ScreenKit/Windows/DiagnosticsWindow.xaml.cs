using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>诊断页：CUDA / DirectML / 路径 / 多显示器 DPI。</summary>
partial class DiagnosticsWindow : Window {
	static readonly SolidColorBrush OkBrush = freeze(0x1B, 0x7A, 0x32);
	static readonly SolidColorBrush BadBrush = freeze(0xC6, 0x28, 0x28);
	static readonly SolidColorBrush OffBrush = freeze(0x5F, 0x6B, 0x7A);
	static readonly Regex StatRe = new(@"(?<=:\s|=\s?)(True|False|OK)|缺失|不可用|失败", RegexOptions.Compiled);

	readonly Func<string> extraReport;
	string plain = "";

	internal DiagnosticsWindow(Func<string> appExtraReport = null) {
		InitializeComponent();
		extraReport = appExtraReport;
		brefresh.Click += (_, _) => refresh();
		bcopy.Click += (_, _) => {
			try {
				Clipboard.SetText(plain ?? "");
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
		sb.AppendLine($"FaceModels: {FaceModels.ModelsRoot()}");
		sb.AppendLine($"exists facemodels: {Directory.Exists(FaceModels.ModelsRoot())}");
		sb.AppendLine($"face ready: {FaceModels.IsReady()}");
		try {
			var onnx = FaceModels.ListOnnx();
			sb.AppendLine($"face onnx: {onnx.Count}");
			foreach (var n in onnx)
				sb.AppendLine("  " + n);
		}
		catch (Exception ex) { sb.AppendLine("face list: " + ex.Message); }
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
		showreport(sb.ToString());
	}

	void showreport(string raw) {
		raw ??= "";
		var doc = new FlowDocument {
			FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
			FontSize = 12,
			PagePadding = new Thickness(0),
		};
		var para = new Paragraph { Margin = new Thickness(0) };
		var sb = new StringBuilder();
		var i = 0;
		foreach (Match m in StatRe.Matches(raw)) {
			add(raw.Substring(i, m.Index - i), null);
			var tok = m.Value;
			if (tok is "True" or "OK") add("正常", OkBrush);
			else if (tok == "False" && optionaloff(raw, m.Index)) add("未启用", OffBrush);
			else if (tok is "False" or "缺失") add("不正常", BadBrush);
			else add(tok, BadBrush);
			i = m.Index + m.Length;
		}
		add(raw.Substring(i), null);
		doc.Blocks.Add(para);
		elog.Document = doc;
		plain = sb.ToString();
		elog.ScrollToHome();

		void add(string s, Brush brush) {
			if (s.Length == 0) return;
			sb.Append(s);
			var run = new Run(s);
			if (brush != null) {
				run.Foreground = brush;
				run.FontWeight = FontWeights.SemiBold;
			}
			para.Inlines.Add(run);
		}
	}

	/// <summary>关掉或还没装上的可选项，不是故障。</summary>
	static bool optionaloff(string raw, int at) {
		var start = raw.LastIndexOf('\n', Math.Max(0, at - 1));
		start = start < 0 ? 0 : start + 1;
		var line = raw.Substring(start, at - start);
		if (line.Contains("ServiceMode")) return true;
		if (line.Contains("IsOrtReady")) return true;
		if (line.Contains("IsGpuReady")) return true;
		if (line.Contains("IsDmlReady")) return true;
		if (line.Contains("Runner.HasEngine")) return true;
		if (line.IndexOf("onnxgpu64", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (line.IndexOf("onnxdml64", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (line.IndexOf("face ready", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (line.IndexOf("facemodels", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (line.Contains("FaceModels")) return true;
		return false;
	}

	static SolidColorBrush freeze(byte r, byte g, byte b) {
		var br = new SolidColorBrush(Color.FromRgb(r, g, b));
		br.Freeze();
		return br;
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
