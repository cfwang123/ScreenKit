using System.Reflection;
using System.Runtime.InteropServices;
using SherpaOnnx;

namespace WpfOCR;

/// <summary>
/// 修正 sherpa-onnx 中 Destroy 调用约定：原生 Cdecl，托管误标 Winapi 时 Dispose 可能崩溃。
/// </summary>
static class TtsAudioFix {
	const string LIB = "sherpa-onnx-c-api.dll";

	[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
	static extern void SherpaOnnxDestroyOfflineTtsGeneratedAudio(IntPtr handle);

	[DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
	static extern void SherpaOnnxDestroyOfflineTts(IntPtr handle);

	static readonly FieldInfo AudioHandleField = typeof(OfflineTtsGeneratedAudio)
		.GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance);

	static readonly FieldInfo TtsHandleField = typeof(OfflineTts)
		.GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance);

	public static void Free(OfflineTtsGeneratedAudio audio) {
		if (audio == null || AudioHandleField == null) return;
		try {
			var hr = (HandleRef)AudioHandleField.GetValue(audio);
			AudioHandleField.SetValue(audio, new HandleRef(audio, IntPtr.Zero));
			GC.SuppressFinalize(audio);
			if (hr.Handle != IntPtr.Zero && !Environment.HasShutdownStarted)
				SherpaOnnxDestroyOfflineTtsGeneratedAudio(hr.Handle);
		}
		catch { }
	}

	public static void FreeTts(OfflineTts tts) {
		if (tts == null) return;
		if (TtsHandleField == null) {
			try { tts.Dispose(); } catch { }
			return;
		}
		try {
			var hr = (HandleRef)TtsHandleField.GetValue(tts);
			TtsHandleField.SetValue(tts, new HandleRef(tts, IntPtr.Zero));
			GC.SuppressFinalize(tts);
			if (hr.Handle != IntPtr.Zero && !Environment.HasShutdownStarted)
				SherpaOnnxDestroyOfflineTts(hr.Handle);
		}
		catch {
			try { tts.Dispose(); } catch { }
		}
	}
}
