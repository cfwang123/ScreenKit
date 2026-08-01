using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.ML.OnnxRuntime;

namespace WpfOCR;

/// <summary>
/// ORT 原生库路径 + GPU 按需加载。
/// <list type="bullet">
/// <item>NVIDIA CUDA：onnxgpu64/ + CUDA 运行库</item>
/// <item>Intel 核显等：onnxdml64/（DirectML 版 ORT + DirectML.dll）</item>
/// <item>CUDA 与 DML 的 onnxruntime.dll 互斥，首次建会话时锁定后端；切换需重启进程</item>
/// <item>cuDNN 等大库仅在真正建 CUDA session 时加载</item>
/// </list>
/// </summary>
static class CudaBootstrap {
	const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
	const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
	const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern IntPtr AddDllDirectory(string newDirectory);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool SetDefaultDllDirectories(uint directoryFlags);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern IntPtr LoadLibrary(string lpFileName);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool FreeLibrary(IntPtr hModule);

	[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
	static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string lpModuleName);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

	/// <summary>已加载的 ORT 后端。</summary>
	public enum OrtBackend {
		None,
		Cuda,
		Dml,
	}

	static bool probed;
	static bool gpuLibsLoaded;
	static List<string> searchDirs = new();
	static readonly object gate = new();

	/// <summary>exe 旁是否存在 onnxgpu64 目录。</summary>
	public static bool HasOnnxGpu64Dir { get; private set; }

	/// <summary>exe 旁是否存在 onnxdml64 目录。</summary>
	public static bool HasOnnxDml64Dir { get; private set; }

	/// <summary>文件齐全，可尝试 CUDA EP（CUDA 运行库可按需再载）。</summary>
	public static bool IsGpuReady { get; private set; }

	/// <summary>文件齐全，可尝试 DirectML EP（Intel 核显 / 其它 DX12 GPU）。</summary>
	public static bool IsDmlReady { get; private set; }

	/// <summary>是否已 LoadLibrary 过 onnxruntime.dll。</summary>
	public static bool IsOrtReady { get; private set; }

	/// <summary>当前进程锁定的 ORT 后端。</summary>
	public static OrtBackend LoadedBackend { get; private set; } = OrtBackend.None;

	/// <summary>简短状态，供 UI / 日志。</summary>
	public static string GpuStatus { get; private set; } = "未初始化";

	public static string LastReport { get; set; } = "";

	/// <summary>onnxgpu64 绝对路径（可能不存在）。</summary>
	public static string OnnxGpu64Dir { get; private set; } = "";

	/// <summary>onnxdml64 绝对路径（可能不存在）。</summary>
	public static string OnnxDml64Dir { get; private set; } = "";

	/// <summary>onnxcpu64 绝对路径：仅 CPU EP，不依赖 GPU/核显安装。</summary>
	public static string OnnxCpu64Dir { get; private set; } = "";

	/// <summary>汇总诊断文本（CUDA / DML / 路径 / 最近探测日志）。</summary>
	public static string BuildDiagnostics() {
		Init();
		var sb = new StringBuilder();
		sb.AppendLine("=== WpfOCR / ORT 诊断 ===");
		sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine($"BaseDir: {AppDomain.CurrentDomain.BaseDirectory}");
		sb.AppendLine($"OnnxGpu64Dir: {OnnxGpu64Dir}  exists={HasOnnxGpu64Dir}");
		sb.AppendLine($"OnnxDml64Dir: {OnnxDml64Dir}  exists={HasOnnxDml64Dir}");
		sb.AppendLine($"OnnxCpu64Dir: {OnnxCpu64Dir}  exists={Directory.Exists(OnnxCpu64Dir)}");
		sb.AppendLine($"IsOrtReady: {IsOrtReady}");
		sb.AppendLine($"IsGpuReady (CUDA): {IsGpuReady}");
		sb.AppendLine($"IsDmlReady (DirectML): {IsDmlReady}");
		sb.AppendLine($"LoadedBackend: {LoadedBackend}");
		sb.AppendLine($"GpuStatus: {GpuStatus}");
		var rtMaj = detectbundledcudamajor();
		sb.AppendLine($"Bundled CUDA runtime major: {rtMaj}");
		if (trygetdrivercudaversion(out var dm, out var dn, out var dline))
			sb.AppendLine($"Driver CUDA max: {dm}.{dn}  ({dline})");
		else
			sb.AppendLine("Driver CUDA max: (nvidia-smi 不可用)");
		sb.AppendLine("Note: NuGet sherpa-onnx 无 DirectML；TTS/ASR GPU 仅 CUDA。");
		sb.AppendLine();
		sb.AppendLine("--- 最近探测日志 ---");
		sb.AppendLine(string.IsNullOrEmpty(LastReport) ? "(空)" : LastReport);
		return sb.ToString();
	}

	/// <summary>
	/// 启动探测：注册 DLL 搜索路径、检查 CUDA/DML 文件，
	/// 并用<strong>绝对路径</strong>预加载 onnxruntime（防止 System32 旧 stub 抢先占位）。
	/// </summary>
	public static void Init() {
		lock (gate) {
			if (probed) return;
			probed = true;
			var log = new StringBuilder();
			var baseDir = AppDomain.CurrentDomain.BaseDirectory;
			OnnxGpu64Dir = Path.GetFullPath(Path.Combine(baseDir, "onnxgpu64"));
			OnnxDml64Dir = Path.GetFullPath(Path.Combine(baseDir, "onnxdml64"));
			OnnxCpu64Dir = Path.GetFullPath(Path.Combine(baseDir, "onnxcpu64"));
			HasOnnxGpu64Dir = Directory.Exists(OnnxGpu64Dir);
			HasOnnxDml64Dir = Directory.Exists(OnnxDml64Dir);

			log.AppendLine($"base={baseDir}");
			log.AppendLine($"onnxgpu64={OnnxGpu64Dir} exists={HasOnnxGpu64Dir}");
			log.AppendLine($"onnxdml64={OnnxDml64Dir} exists={HasOnnxDml64Dir}");
			log.AppendLine($"onnxcpu64={OnnxCpu64Dir} exists={Directory.Exists(OnnxCpu64Dir)}");

			warnsystem32stub(log);

			searchDirs = buildsearchdirs(baseDir, log);
			setupdllsearch(log);

			// CUDA / DML 文件探测（EP 真正可用另在 EnsureGpuLibsLoaded 验证）
			// 未安装 GPU/核显时二者为 false，OCR 走 CPU EP，只需任意合法 onnxruntime.dll
			IsGpuReady = probecuda(log);
			IsDmlReady = probedml(log);

			// 关键：必须在任何 Microsoft.ML.OnnxRuntime 托管代码触达原生入口前，
			// 用完整路径 LoadLibrary 真实 ORT。否则 DllImport("onnxruntime") 会命中
			// C:\Windows\System32\onnxruntime.dll（约 2KB 旧 stub，无 OrtGetApiBase）。
			// 预加载：不要求已装 GPU/核显，CPU 包即可。
			IsOrtReady = false;
			if (tryloadanyort(log, out var preloaded)) {
				LoadedBackend = preloaded;
				IsOrtReady = true;
				log.AppendLine("Init preloaded ORT: " + backendname(preloaded));
			}
			else
				log.AppendLine("Init: 未预加载 onnxruntime（请检查 onnxcpu64 / onnxgpu64 / onnxdml64）");

			GpuStatus = buildstatus();
			log.AppendLine(GpuStatus);
			LastReport = log.ToString();
		}
	}

	/// <summary>
	/// 按设备确保已加载匹配的 ORT 原生库。
	/// CUDA 与 DML 互斥；已锁定后换后端会抛错（需重启）。
	/// CPU 不要求安装 GPU/核显：任意已加载或可加载的 ORT 均可跑 CPU EP。
	/// </summary>
	public static void EnsureOrtForDevice(OcrDevice device) {
		lock (gate) {
			Init();

			// CPU：只要已有合法 ORT 即可（不区分 CUDA/DML 包）
			if (device == OcrDevice.Cpu && IsOrtReady && LoadedBackend != OrtBackend.None)
				return;

			var want = resolveflavor(device);
			if (LoadedBackend != OrtBackend.None && LoadedBackend != want) {
				// 已加载任意后端时，CPU 会话直接复用
				if (device == OcrDevice.Cpu) return;
				throw new InvalidOperationException(
					$"当前进程已加载 {backendname(LoadedBackend)} 运行时，无法切换到 {backendname(want)}。请重启程序后再选该设备。");
			}
			if (LoadedBackend == want && IsOrtReady) return;

			var log = new StringBuilder(LastReport ?? "");
			log.AppendLine($"--- EnsureOrtForDevice {device} → {want} ---");

			if (device == OcrDevice.Cpu || want == OrtBackend.None) {
				// 未装 GPU/核显：只加载原生库，会话用 CPU EP
				if (!tryloadanyort(log, out var got)) {
					writelog(log);
					throw new InvalidOperationException(
						"无法加载 onnxruntime.dll（CPU 推理也需要原生库）。" +
						"请确认程序目录存在 onnxcpu64、onnxgpu64 或 onnxdml64。" +
						"勿使用 C:\\Windows\\System32\\onnxruntime.dll 旧 stub。");
				}
				LoadedBackend = got;
			}
			else if (want == OrtBackend.Dml) {
				if (!IsDmlReady)
					throw new InvalidOperationException("核显 DirectML 不可用（未安装 onnxdml64，请到「安装功能」安装核显组件，或改用 CPU）");
				if (!loaddmlort(log))
					throw new InvalidOperationException("加载 DirectML 版 onnxruntime 失败，详见 log/cuda_bootstrap.log");
				LoadedBackend = OrtBackend.Dml;
			}
			else {
				// CUDA 版 ORT（也可 CPU EP）；失败再试任意 ORT
				if (!loadcudaort(log)) {
					if (!tryloadanyort(log, out var fallback)) {
						writelog(log);
						throw new InvalidOperationException(
							"加载 onnxruntime 失败。未安装 GPU 时请保留 onnxcpu64；或安装 CUDA/核显组件。");
					}
					LoadedBackend = fallback;
				}
				else
					LoadedBackend = OrtBackend.Cuda;
			}
			IsOrtReady = true;
			GpuStatus = buildstatus();
			log.AppendLine(GpuStatus);
			writelog(log);
		}
	}

	static void writelog(StringBuilder log) {
		LastReport = log.ToString();
		try {
			var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(logDir);
			File.WriteAllText(Path.Combine(logDir, "cuda_bootstrap.log"), LastReport, Encoding.UTF8);
		}
		catch { }
	}

	/// <summary>
	/// 真正使用 CUDA GPU 前调用：预加载 CUDA/cuDNN + CUDA EP。
	/// CPU / DML 路径不要调用。
	/// 若 EP 加载失败会将 <see cref="IsGpuReady"/> 置 false 并写入原因。
	/// </summary>
	public static void EnsureGpuLibsLoaded() {
		lock (gate) {
			Init();
			// 已成功加载过则跳过；失败后允许再次尝试（例如用户升级驱动后）
			if (gpuLibsLoaded && IsGpuReady) return;

			var log = new StringBuilder(LastReport ?? "");
			log.AppendLine("--- EnsureGpuLibsLoaded ---");

			// 驱动 CUDA 能力 vs 随包运行库主版本
			var runtimeMajor = detectbundledcudamajor();
			int drvMaj = 0, drvMin = 0;
			if (trygetdrivercudaversion(out drvMaj, out drvMin, out var drvRaw)) {
				log.AppendLine($"Driver CUDA max: {drvMaj}.{drvMin} ({drvRaw})");
				log.AppendLine($"Bundled CUDA runtime major: {runtimeMajor}");
				if (runtimeMajor > 0 && drvMaj > 0 && runtimeMajor > drvMaj) {
					IsGpuReady = false;
					gpuLibsLoaded = false;
					GpuStatus =
						$"驱动最高支持 CUDA {drvMaj}.{drvMin}，但 onnxgpu64 为 CUDA {runtimeMajor} 运行库；请升级 NVIDIA 驱动，或改用与驱动匹配的 CUDA{drvMaj} ORT GPU 包";
					log.AppendLine(GpuStatus);
					LastReport = log.ToString();
					writelogfile();
					return;
				}
			}
			else {
				log.AppendLine("Driver CUDA version: (无法探测 nvidia-smi)");
			}

			try { EnsureOrtForDevice(OcrDevice.Gpu); }
			catch (Exception ex) {
				IsGpuReady = false;
				gpuLibsLoaded = false;
				GpuStatus = "加载 CUDA 版 onnxruntime 失败: " + ex.Message;
				log.AppendLine(GpuStatus);
				LastReport = log.ToString();
				writelogfile();
				return;
			}

			setupdllsearch(log);

			var has13 = runtimeMajor >= 13
				|| FindInDirs(searchDirs, "cudart64_13.dll") != null
				|| FindInDirs(searchDirs, "cublasLt64_13.dll") != null;
			string[] preload = has13
				? [
					"cudart64_13.dll",
					"nvJitLink_130_0.dll",
					"cublasLt64_13.dll",
					"cublas64_13.dll",
					"cufft64_12.dll", "cufft64_11.dll",
					"cudnn_ops64_9.dll", "cudnn_graph64_9.dll", "cudnn_cnn64_9.dll",
					"cudnn_engines_precompiled64_9.dll", "cudnn_engines_runtime_compiled64_9.dll",
					"cudnn_engines_tensor_ir64_9.dll", "cudnn_ext64_9.dll",
					"cudnn_heuristic64_9.dll", "cudnn_adv64_9.dll", "cudnn64_9.dll",
					"onnxruntime_providers_shared.dll",
					"onnxruntime_providers_cuda.dll",
				]
				: [
					"cudart64_12.dll",
					"nvJitLink_120_0.dll",
					"cublasLt64_12.dll",
					"cublas64_12.dll",
					"cufft64_11.dll", "cufft64_12.dll",
					"cudnn_ops64_9.dll", "cudnn_graph64_9.dll", "cudnn64_9.dll",
					"onnxruntime_providers_shared.dll",
					"onnxruntime_providers_cuda.dll",
				];
			log.AppendLine($"CUDA major prefer: {(has13 ? "13" : "12")}");
			var gpuName = trygetgpuname();
			if (!string.IsNullOrEmpty(gpuName))
				log.AppendLine("GPU: " + gpuName);
			var criticalFail = 0;
			var epFailCode = 0;
			var epPath = "";
			foreach (var name in preload) {
				var full = FindInDirs(searchDirs, name);
				if (full == null) {
					// engines / ext 可选；cudart / providers 必需
					log.AppendLine($"Missing: {name}");
					if (iscriticallib(name)) criticalFail++;
					continue;
				}
				// providers_cuda 先不预载：部分环境 LoadLibrary 会 1114，但 ORT Append 仍可能成功
				if (name.IndexOf("providers_cuda", StringComparison.OrdinalIgnoreCase) >= 0) {
					epPath = full;
					log.AppendLine($"Defer load: {name} (由 ORT 注册时加载)");
					continue;
				}
				try {
					var h = LoadLibrary(full);
					var ok = h != IntPtr.Zero;
					var err = ok ? 0 : Marshal.GetLastWin32Error();
					log.AppendLine($"Load {(ok ? "OK" : $"FAIL({err})")}: {name}");
					if (!ok && iscriticallib(name))
						criticalFail++;
				}
				catch (Exception ex) {
					log.AppendLine($"Load ERR {name}: {ex.Message}");
					if (iscriticallib(name)) criticalFail++;
				}
			}

			// 真正验证：用 ORT 注册 CUDA EP（与 OCR/推理路径一致）
			if (criticalFail == 0) {
				if (tryregistercudaep(log, out epFailCode)) {
					IsGpuReady = true;
					GpuStatus = string.IsNullOrEmpty(gpuName)
						? "GPU 已加载 (onnxgpu64 CUDA)"
						: $"GPU 已加载 · {gpuName}";
					gpuLibsLoaded = true;
				}
				else {
					IsGpuReady = false;
					gpuLibsLoaded = false;
					// 回退：再试一次显式 LoadLibrary，便于日志
					if (!string.IsNullOrEmpty(epPath)) {
						var h = LoadLibrary(epPath);
						var err = h != IntPtr.Zero ? 0 : Marshal.GetLastWin32Error();
						log.AppendLine($"Fallback LoadLibrary providers_cuda: {(h != IntPtr.Zero ? "OK" : "FAIL(" + err + ")")}");
						if (err != 0) epFailCode = err;
					}
					if (epFailCode == 1114)
						GpuStatus = buildEpInitFailStatus(runtimeMajor, drvMaj, drvMin, gpuName);
					else
						GpuStatus = "CUDA EP 注册失败，将使用 CPU（详见 log/cuda_bootstrap.log）";
				}
			}
			else {
				IsGpuReady = false;
				gpuLibsLoaded = false;
				GpuStatus = "GPU 运行库加载失败，将使用 CPU（详见 log/cuda_bootstrap.log）";
			}
			log.AppendLine($"IsGpuReady={IsGpuReady} {GpuStatus}");
			LastReport = log.ToString();
			writelogfile();
		}
	}

	/// <summary>用 SessionOptions 注册 CUDA EP，验证 providers 是否真正可用。</summary>
	static bool tryregistercudaep(StringBuilder log, out int win32Hint) {
		win32Hint = 0;
		try {
			using (var so = new SessionOptions()) {
				so.AppendExecutionProvider_CUDA(0);
				log.AppendLine("AppendExecutionProvider_CUDA(0) OK");
			}
			return true;
		}
		catch (Exception ex) {
			log.AppendLine("AppendExecutionProvider_CUDA FAIL: " + ex.Message);
			if (ex.InnerException != null)
				log.AppendLine("  inner: " + ex.InnerException.Message);
			// 从消息里猜 Win32
			var m = System.Text.RegularExpressions.Regex.Match(ex.Message ?? "", @"\b(1114|126|127|193)\b");
			if (m.Success) int.TryParse(m.Value, out win32Hint);
			if (win32Hint == 0) win32Hint = 1114;
			return false;
		}
	}

	static string trygetgpuname() {
		try {
			var psi = new System.Diagnostics.ProcessStartInfo {
				FileName = "nvidia-smi",
				Arguments = "--query-gpu=name --format=csv,noheader",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using var p = System.Diagnostics.Process.Start(psi);
			if (p == null) return "";
			var o = (p.StandardOutput.ReadToEnd() ?? "").Trim();
			if (!p.WaitForExit(5000)) {
				try { p.Kill(); } catch { }
				return "";
			}
			var line = o.Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			return line.Length > 0 ? line[0].Trim() : "";
		}
		catch { return ""; }
	}

	static bool iscriticallib(string name) {
		if (string.IsNullOrEmpty(name)) return false;
		if (name.StartsWith("cudart", StringComparison.OrdinalIgnoreCase)) return true;
		if (name.StartsWith("cublas", StringComparison.OrdinalIgnoreCase)) return true;
		// providers_cuda 改由 ORT 注册探测，不作为预载 critical
		if (name.IndexOf("providers_shared", StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return false;
	}

	static int detectbundledcudamajor() {
		if (FindInDirs(searchDirs, "cudart64_13.dll") != null
			|| File.Exists(Path.Combine(OnnxGpu64Dir, "cudart64_13.dll")))
			return 13;
		if (FindInDirs(searchDirs, "cudart64_12.dll") != null
			|| File.Exists(Path.Combine(OnnxGpu64Dir, "cudart64_12.dll")))
			return 12;
		if (FindInDirs(searchDirs, "cudart64_11.dll") != null)
			return 11;
		return 0;
	}

	/// <summary>
	/// 从 nvidia-smi 解析驱动支持的最高 CUDA 版本（如 12.6）。
	/// </summary>
	public static bool trygetdrivercudaversion(out int major, out int minor, out string rawLine) {
		major = 0;
		minor = 0;
		rawLine = "";
		try {
			var psi = new System.Diagnostics.ProcessStartInfo {
				FileName = "nvidia-smi",
				Arguments = "",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using var p = System.Diagnostics.Process.Start(psi);
			if (p == null) return false;
			var output = p.StandardOutput.ReadToEnd();
			if (!p.WaitForExit(8000)) {
				try { p.Kill(); } catch { }
				return false;
			}
			// 例: | NVIDIA-SMI 560.94 Driver Version: 560.94 CUDA Version: 12.6 |
			foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
				var idx = line.IndexOf("CUDA Version:", StringComparison.OrdinalIgnoreCase);
				if (idx < 0) continue;
				rawLine = line.Trim();
				var rest = line.Substring(idx + "CUDA Version:".Length).Trim();
				// 取第一个 x.y
				var m = System.Text.RegularExpressions.Regex.Match(rest, @"(\d+)\.(\d+)");
				if (!m.Success) continue;
				major = int.Parse(m.Groups[1].Value);
				minor = int.Parse(m.Groups[2].Value);
				return true;
			}
		}
		catch { }
		return false;
	}

	static string buildEpInitFailStatus(int runtimeMajor, int drvMaj, int drvMin, string gpuName = null) {
		var sb = new StringBuilder();
		sb.Append("CUDA EP 初始化失败(Win32 1114)");
		if (!string.IsNullOrEmpty(gpuName))
			sb.Append(" · ").Append(gpuName);
		if (drvMaj > 0 && runtimeMajor > drvMaj)
			sb.Append($"：驱动最高 CUDA {drvMaj}.{drvMin}，包内为 CUDA {runtimeMajor}，请升级驱动");
		else if (drvMaj > 0) {
			sb.Append($"：驱动 CUDA {drvMaj}.{drvMin}");
			// 仅在未识别到现代卡时提示 Maxwell
			var modern = !string.IsNullOrEmpty(gpuName)
				&& (gpuName.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0
					|| gpuName.IndexOf("GTX 16", StringComparison.OrdinalIgnoreCase) >= 0
					|| gpuName.IndexOf("GTX 10", StringComparison.OrdinalIgnoreCase) >= 0
					|| gpuName.IndexOf("A40", StringComparison.OrdinalIgnoreCase) >= 0
					|| gpuName.IndexOf("A10", StringComparison.OrdinalIgnoreCase) >= 0);
			if (!modern && string.IsNullOrEmpty(gpuName))
				sb.Append("；若 GPU 过旧（Maxwell 等）新版 ORT 可能不支持");
			else
				sb.Append("；onnxruntime_providers_cuda.dll 未能加载（请查 log/cuda_bootstrap.log：VC 运行库/驱动/包是否匹配）");
		}
		else
			sb.Append("：请检查驱动 / CUDA 运行库 / 是否安装 VC++ x64 可再发行组件");
		return sb.ToString();
	}

	static void writelogfile() {
		try {
			var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
			Directory.CreateDirectory(logDir);
			File.WriteAllText(Path.Combine(logDir, "cuda_bootstrap.log"), LastReport ?? "", Encoding.UTF8);
		}
		catch { }
	}

	/// <summary>运行时再标记 CUDA GPU 不可用（例如 ORT 建会话失败）。</summary>
	public static void MarkGpuFailed(string reason) {
		lock (gate) {
			IsGpuReady = false;
			GpuStatus = string.IsNullOrWhiteSpace(reason)
				? "CUDA GPU 初始化失败，已回退"
				: $"CUDA GPU 失败: {reason}";
			try {
				LastReport = (LastReport ?? "") + "\n[runtime] " + GpuStatus;
			}
			catch { }
		}
	}

	/// <summary>运行时标记 DirectML 不可用。</summary>
	public static void MarkDmlFailed(string reason) {
		lock (gate) {
			IsDmlReady = false;
			GpuStatus = string.IsNullOrWhiteSpace(reason)
				? "核显 DirectML 初始化失败，已回退"
				: $"核显 DirectML 失败: {reason}";
			try {
				LastReport = (LastReport ?? "") + "\n[runtime] " + GpuStatus;
			}
			catch { }
		}
	}

	static OrtBackend resolveflavor(OcrDevice device) {
		return device switch {
			OcrDevice.IntelGpu => OrtBackend.Dml,
			OcrDevice.Gpu => OrtBackend.Cuda,
			// CPU：沿用已加载；否则有啥用啥（无加速包 → None，由 tryloadanyort 找 onnxcpu64）
			_ => LoadedBackend != OrtBackend.None ? LoadedBackend
				: File.Exists(Path.Combine(OnnxGpu64Dir, "onnxruntime.dll")) || IsGpuReady
					? OrtBackend.Cuda
					: File.Exists(Path.Combine(OnnxDml64Dir, "onnxruntime.dll")) || IsDmlReady
						? OrtBackend.Dml
						: OrtBackend.None,
		};
	}

	static string backendname(OrtBackend b) => b switch {
		OrtBackend.Cuda => "NVIDIA CUDA",
		OrtBackend.Dml => "Intel/核显 DirectML",
		_ => "CPU",
	};

	static string buildstatus() {
		var parts = new List<string>();
		if (IsGpuReady) parts.Add("CUDA可用");
		if (IsDmlReady) parts.Add("核显DML可用");
		if (parts.Count == 0) {
			if (!IsOrtReady && !File.Exists(Path.Combine(OnnxCpu64Dir, "onnxruntime.dll"))
				&& !File.Exists(Path.Combine(OnnxGpu64Dir, "onnxruntime.dll"))
				&& !File.Exists(Path.Combine(OnnxDml64Dir, "onnxruntime.dll")))
				return "找不到 onnxruntime（请检查 onnxcpu64）";
			return "加速未安装 · 使用 CPU";
		}
		var loaded = LoadedBackend == OrtBackend.None ? "未加载" : backendname(LoadedBackend);
		return $"加速: {string.Join(" · ", parts)} · 当前:{loaded}";
	}

	static bool probecuda(StringBuilder log) {
		if (!HasOnnxGpu64Dir) {
			log.AppendLine("CUDA: no onnxgpu64");
			return false;
		}
		string[] required = [
			"onnxruntime.dll",
			"onnxruntime_providers_cuda.dll",
			"onnxruntime_providers_shared.dll",
		];
		var missing = new List<string>();
		foreach (var name in required) {
			if (FindInDirs(searchDirs, name) == null
				&& !File.Exists(Path.Combine(OnnxGpu64Dir, name)))
				missing.Add(name);
		}
		var hasCudart = FindInDirs(searchDirs, "cudart64_13.dll") != null
			|| FindInDirs(searchDirs, "cudart64_12.dll") != null
			|| File.Exists(Path.Combine(OnnxGpu64Dir, "cudart64_13.dll"))
			|| File.Exists(Path.Combine(OnnxGpu64Dir, "cudart64_12.dll"));
		if (!hasCudart) missing.Add("cudart64_12/13.dll");
		if (missing.Count > 0) {
			log.AppendLine("CUDA incomplete: " + string.Join(", ", missing));
			return false;
		}
		log.AppendLine("CUDA probe OK");
		return true;
	}

	static bool probedml(StringBuilder log) {
		if (!HasOnnxDml64Dir) {
			log.AppendLine("DML: no onnxdml64");
			return false;
		}
		var ort = Path.Combine(OnnxDml64Dir, "onnxruntime.dll");
		var dml = Path.Combine(OnnxDml64Dir, "DirectML.dll");
		// DirectML.dll 也可来自系统；ORT 必须是 DML 版
		if (!File.Exists(ort)) {
			log.AppendLine("DML: missing onnxruntime.dll in onnxdml64");
			return false;
		}
		if (!File.Exists(dml)) {
			// 系统目录常见，仍允许尝试
			log.AppendLine("DML: DirectML.dll not in onnxdml64 (will try system)");
		}
		log.AppendLine("DML probe OK");
		return true;
	}

	/// <summary>
	/// 加载任意可用 ORT（不要求 GPU/核显安装）。顺序：GPU 包 → CPU 包 → 搜索路径 → DML 包。
	/// </summary>
	static bool tryloadanyort(StringBuilder log, out OrtBackend which) {
		which = OrtBackend.None;
		// 1) CUDA 版 ORT（含 onnxcpu64 / onnxgpu64）
		if (loadcudaort(log)) {
			// 若实际来自 DML 目录则标 Dml，否则标 Cuda（CPU 包与 GPU 包同源）
			var byName = modulepath(GetModuleHandle("onnxruntime.dll")) ?? "";
			if (byName.IndexOf("onnxdml64", StringComparison.OrdinalIgnoreCase) >= 0)
				which = OrtBackend.Dml;
			else
				which = OrtBackend.Cuda;
			return true;
		}
		// 2) DML 版
		if (loaddmlort(log)) {
			which = OrtBackend.Dml;
			return true;
		}
		return false;
	}

	static bool loadcudaort(StringBuilder log) {
		// 优先完整 CUDA 包，再 CPU 专用包，再其它路径；均可仅用 CPU EP
		var candidates = new List<string>();
		if (HasOnnxGpu64Dir)
			candidates.Add(Path.Combine(OnnxGpu64Dir, "onnxruntime.dll"));
		if (!string.IsNullOrEmpty(OnnxCpu64Dir))
			candidates.Add(Path.Combine(OnnxCpu64Dir, "onnxruntime.dll"));
		var found = FindInDirs(searchDirs, "onnxruntime.dll");
		if (found != null) candidates.Add(found);
		var baseDir = AppDomain.CurrentDomain.BaseDirectory;
		candidates.Add(Path.Combine(baseDir, "onnxruntime.dll"));
		candidates.Add(Path.Combine(baseDir, "runtimes", "win-x64", "native", "onnxruntime.dll"));
		// 未装 GPU 时也可用 DML 包跑 CPU EP
		if (HasOnnxDml64Dir)
			candidates.Add(Path.Combine(OnnxDml64Dir, "onnxruntime.dll"));
		return loadortfrom(candidates, log);
	}

	static bool loaddmlort(StringBuilder log) {
		// 先载 DirectML.dll，再载 DML 版 ORT
		var dmlCandidates = new List<string>();
		if (HasOnnxDml64Dir)
			dmlCandidates.Add(Path.Combine(OnnxDml64Dir, "DirectML.dll"));
		dmlCandidates.Add(Path.Combine(Environment.SystemDirectory, "DirectML.dll"));
		// SysWOW64 不适合 x64；x64 系统目录即可
		foreach (var p in dmlCandidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
			if (!File.Exists(p)) continue;
			try {
				var h = LoadLibrary(p);
				log.AppendLine($"DirectML {(h != IntPtr.Zero ? "OK" : "FAIL")}: {p}");
				if (h != IntPtr.Zero) break;
			}
			catch (Exception ex) {
				log.AppendLine($"DirectML ERR {p}: {ex.Message}");
			}
		}

		var candidates = new List<string>();
		if (HasOnnxDml64Dir)
			candidates.Add(Path.Combine(OnnxDml64Dir, "onnxruntime.dll"));
		return loadortfrom(candidates, log);
	}

	static bool loadortfrom(IEnumerable<string> candidates, StringBuilder log) {
		// 若 System32 旧 stub 已被短名加载，先尽量卸掉，否则 GetModuleHandle/DllImport 一直指向 stub
		evictbadortmodule(log);

		foreach (var full in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
			if (string.IsNullOrWhiteSpace(full) || !File.Exists(full)) {
				log.AppendLine($"ORT miss: {full}");
				continue;
			}
			try {
				var h = LoadLibrary(full);
				if (h == IntPtr.Zero) {
					log.AppendLine($"ORT Load FAIL({Marshal.GetLastWin32Error()}): {full}");
					continue;
				}
				// 必须导出 OrtGetApiBase；System32 2KB stub 没有此入口
				var api = GetProcAddress(h, "OrtGetApiBase");
				if (api == IntPtr.Zero) {
					log.AppendLine($"ORT reject (no OrtGetApiBase): {full}");
					try { FreeLibrary(h); } catch { }
					continue;
				}
				var loadedPath = modulepath(h);
				log.AppendLine($"ORT Load OK: {full}");
				if (!string.IsNullOrEmpty(loadedPath) &&
					!string.Equals(loadedPath, full, StringComparison.OrdinalIgnoreCase))
					log.AppendLine($"ORT mapped path: {loadedPath}");
				// 再次确认短名解析到的是我们刚载的模块（而非 System32 stub）
				var byName = GetModuleHandle("onnxruntime.dll");
				var byNamePath = modulepath(byName);
				var byNameApi = byName != IntPtr.Zero ? GetProcAddress(byName, "OrtGetApiBase") : IntPtr.Zero;
				log.AppendLine($"ORT short-name → {byNamePath ?? "(null)"} api={(byNameApi != IntPtr.Zero ? "OK" : "MISSING")}");
				if (byNameApi == IntPtr.Zero) {
					log.AppendLine("ORT short-name still points to bad module; evict+retry load");
					evictbadortmodule(log);
					h = LoadLibrary(full);
					if (h == IntPtr.Zero || GetProcAddress(h, "OrtGetApiBase") == IntPtr.Zero) {
						log.AppendLine("ORT re-load after evict failed: " + full);
						continue;
					}
					// 短名仍坏则无法被托管 DllImport 使用
					byName = GetModuleHandle("onnxruntime.dll");
					byNameApi = byName != IntPtr.Zero ? GetProcAddress(byName, "OrtGetApiBase") : IntPtr.Zero;
					if (byNameApi == IntPtr.Zero) {
						log.AppendLine(
							"FATAL: 进程内 onnxruntime 短名仍无 OrtGetApiBase。" +
							"请删除或重命名 C:\\Windows\\System32\\onnxruntime.dll（旧 stub）后重启程序。");
						continue;
					}
				}
				var shared = Path.Combine(Path.GetDirectoryName(full) ?? "", "onnxruntime_providers_shared.dll");
				if (File.Exists(shared)) {
					var hs = LoadLibrary(shared);
					log.AppendLine($"providers_shared {(hs != IntPtr.Zero ? "OK" : "FAIL")}: {shared}");
				}
				return true;
			}
			catch (Exception ex) {
				log.AppendLine($"ORT Load ERR {full}: {ex.Message}");
			}
		}
		return false;
	}

	/// <summary>检测 System32 里无用的 onnxruntime stub 并记日志。</summary>
	static void warnsystem32stub(StringBuilder log) {
		try {
			var sys = Path.Combine(Environment.SystemDirectory, "onnxruntime.dll");
			if (!File.Exists(sys)) return;
			long len = 0;
			try { len = new FileInfo(sys).Length; } catch { }
			// 正常 ORT > 5MB；System32 旧 stub 约 2–3KB
			if (len > 0 && len < 100_000) {
				log.AppendLine(
					$"WARNING: 发现可疑 {sys} ({len} bytes)。" +
					"若抢先被加载会导致 OrtGetApiBase 找不到；本程序会优先用绝对路径加载 onnxgpu64/onnxdml64。");
			}
		}
		catch { }
	}

	static string modulepath(IntPtr h) {
		if (h == IntPtr.Zero) return null;
		try {
			var sb = new StringBuilder(1024);
			var n = GetModuleFileName(h, sb, sb.Capacity);
			return n > 0 ? sb.ToString() : null;
		}
		catch {
			return null;
		}
	}

	/// <summary>
	/// 若当前「onnxruntime.dll」模块没有 OrtGetApiBase（System32 stub），尝试 FreeLibrary 卸掉。
	/// </summary>
	static void evictbadortmodule(StringBuilder log) {
		try {
			var h = GetModuleHandle("onnxruntime.dll");
			if (h == IntPtr.Zero) return;
			if (GetProcAddress(h, "OrtGetApiBase") != IntPtr.Zero) return;
			var path = modulepath(h) ?? "?";
			log.AppendLine($"ORT evict bad module: {path}");
			// 引用计数可能 >1，多卸几次
			for (var i = 0; i < 8; i++) {
				if (!FreeLibrary(h)) break;
				h = GetModuleHandle("onnxruntime.dll");
				if (h == IntPtr.Zero) {
					log.AppendLine("ORT bad module unloaded");
					return;
				}
				if (GetProcAddress(h, "OrtGetApiBase") != IntPtr.Zero) return;
			}
			if (GetModuleHandle("onnxruntime.dll") != IntPtr.Zero)
				log.AppendLine("ORT bad module still resident (may need process restart)");
		}
		catch (Exception ex) {
			log.AppendLine("ORT evict ERR: " + ex.Message);
		}
	}

	static void setupdllsearch(StringBuilder log) {
		try {
			SetDefaultDllDirectories(
				LOAD_LIBRARY_SEARCH_DEFAULT_DIRS |
				LOAD_LIBRARY_SEARCH_USER_DIRS |
				LOAD_LIBRARY_SEARCH_APPLICATION_DIR);
		}
		catch (Exception ex) {
			log.AppendLine($"SetDefaultDllDirectories: {ex.Message}");
		}

		try {
			var path = Environment.GetEnvironmentVariable("PATH") ?? "";
			var prepend = string.Join(Path.PathSeparator.ToString(), searchDirs);
			if (!string.IsNullOrEmpty(prepend))
				Environment.SetEnvironmentVariable("PATH", prepend + Path.PathSeparator + path);
		}
		catch (Exception ex) {
			log.AppendLine($"PATH prepend: {ex.Message}");
		}

		foreach (var d in searchDirs) {
			try {
				var h = AddDllDirectory(d);
				log.AppendLine($"AddDllDirectory {(h != IntPtr.Zero ? "OK" : "FAIL")}: {d}");
			}
			catch (Exception ex) {
				log.AppendLine($"AddDllDirectory ERR {d}: {ex.Message}");
			}
		}
	}

	static List<string> buildsearchdirs(string baseDir, StringBuilder log) {
		var dirs = new List<string>();
		void Add(string d) {
			if (string.IsNullOrWhiteSpace(d)) return;
			try { d = Path.GetFullPath(d); } catch { return; }
			if (!Directory.Exists(d)) return;
			if (dirs.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase))) return;
			dirs.Add(d);
		}

		// CUDA / CPU / DML 目录都进搜索路径（LoadLibrary 用绝对路径锁定版本）
		Add(OnnxGpu64Dir);
		Add(OnnxCpu64Dir);
		Add(OnnxDml64Dir);
		Add(baseDir);
		Add(Path.Combine(baseDir, "runtimes", "win-x64", "native"));

		var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
		if (!string.IsNullOrEmpty(cudaPath))
			Add(Path.Combine(cudaPath, "bin"));

		try {
			var toolkitRoot = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
			if (Directory.Exists(toolkitRoot)) {
				foreach (var ver in Directory.GetDirectories(toolkitRoot).OrderByDescending(x => x))
					Add(Path.Combine(ver, "bin"));
			}
		}
		catch { }

		log.AppendLine($"searchDirs={dirs.Count}");
		foreach (var d in dirs) log.AppendLine($"  dir: {d}");
		return dirs;
	}

	static string FindInDirs(List<string> dirs, string fileName) {
		foreach (var d in dirs) {
			var p = Path.Combine(d, fileName);
			if (File.Exists(p)) return p;
		}
		return null;
	}
}
