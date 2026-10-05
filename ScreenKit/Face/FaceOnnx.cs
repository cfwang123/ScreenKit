using Microsoft.ML.OnnxRuntime;

namespace ScreenKit;

/// <summary>人脸 ONNX 会话：与 OCR 共用 CUDA / DirectML / CPU。</summary>
static class FaceOnnx {
	public static string LastEp { get; private set; } = "cpu";

	public static InferenceSession Open(string modelPath, TtsComputeMode mode, out string ep) {
		if (!File.Exists(modelPath))
			throw new FileNotFoundException("人脸模型未找到", modelPath);
		Exception last = null;
		foreach (var cand in eplist(mode)) {
		try {
			ensureort(cand);
			ep = cand;
			LastEp = cand;
			return CudaBootstrap.MakeSession(modelPath, cand);
			}
			catch (Exception ex) {
				last = ex;
				CaptureLog.Info($"Face {cand} fail: " + ex.Message);
			}
		}
		throw last ?? new InvalidOperationException("无法创建人脸 ONNX 会话");
	}

	static IEnumerable<string> eplist(TtsComputeMode mode) {
		if (mode == TtsComputeMode.Cpu) {
			yield return "cpu";
			yield break;
		}
		if (mode == TtsComputeMode.Igpu) {
			if (CudaBootstrap.IsDmlReady) yield return "dml";
			yield return "cpu";
			yield break;
		}
		if (mode == TtsComputeMode.Gpu) {
			if (CudaBootstrap.IsGpuReady) yield return "cuda";
			yield return "cpu";
			yield break;
		}
		if (CudaBootstrap.IsGpuReady) yield return "cuda";
		if (CudaBootstrap.IsDmlReady) yield return "dml";
		yield return "cpu";
	}

	static void ensureort(string ep) {
		if (ep == "cuda") {
			try { CudaBootstrap.EnsureGpuLibsLoaded(); } catch { }
			if (!CudaBootstrap.IsGpuReady)
				throw new InvalidOperationException("CUDA 不可用");
			CudaBootstrap.EnsureOrtForDevice(OcrDevice.Gpu);
		}
		else if (ep == "dml") {
			if (!CudaBootstrap.IsDmlReady)
				throw new InvalidOperationException("DirectML 不可用");
			CudaBootstrap.EnsureOrtForDevice(OcrDevice.IntelGpu);
		}
		else {
			CudaBootstrap.EnsureOrtForDevice(OcrDevice.Cpu);
			if (!CudaBootstrap.IsOrtReady)
				throw new InvalidOperationException(
					"无法加载 ONNX Runtime（人脸）。请确认程序目录有 onnxcpu64 / onnxgpu64 / onnxdml64。");
		}
	}

	public static string InputName(InferenceSession session) {
		foreach (var kv in session.InputMetadata)
			return kv.Key;
		throw new InvalidOperationException("模型没有输入节点");
	}

	public static string EpLabel(string ep) => ep switch {
		"cuda" => "GPU",
		"dml" => "核显",
		_ => "CPU",
	};
}
