using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace WpfOCR;

/// <summary>
/// Windows SAPI 语音合成：朗读 / 停 / 导出 WAV→MP3。
/// </summary>
sealed class SapiTts : IDisposable {
	readonly SpeechSynthesizer syn = new();
	bool disposed;

	public SapiTts() {
		try {
			syn.SetOutputToDefaultAudioDevice();
		}
		catch { }
	}

	public IReadOnlyList<VoiceInfo> Voices {
		get {
			try {
				return syn.GetInstalledVoices()
					.Where(v => v.Enabled)
					.Select(v => v.VoiceInfo)
					.ToList();
			}
			catch {
				return Array.Empty<VoiceInfo>();
			}
		}
	}

	public string CurrentVoiceName {
		get {
			try { return syn.Voice?.Name ?? ""; }
			catch { return ""; }
		}
	}

	/// <summary>语速 -10～10。</summary>
	public int Rate {
		get => syn.Rate;
		set => syn.Rate = Compat.Clamp(value, -10, 10);
	}

	/// <summary>音量 0～100。</summary>
	public int Volume {
		get => syn.Volume;
		set => syn.Volume = Compat.Clamp(value, 0, 100);
	}

	public bool IsSpeaking {
		get {
			try { return syn.State == SynthesizerState.Speaking; }
			catch { return false; }
		}
	}

	public void SelectVoice(string name) {
		if (string.IsNullOrWhiteSpace(name)) return;
		try { syn.SelectVoice(name); }
		catch (Exception ex) {
			throw new InvalidOperationException("无法选择语音: " + ex.Message, ex);
		}
	}

	public void SpeakAsync(string text) {
		if (string.IsNullOrWhiteSpace(text)) return;
		try {
			syn.SpeakAsyncCancelAll();
			syn.SetOutputToDefaultAudioDevice();
			syn.SpeakAsync(text);
		}
		catch (Exception ex) {
			throw new InvalidOperationException("朗读失败: " + ex.Message, ex);
		}
	}

	public void Stop() {
		try { syn.SpeakAsyncCancelAll(); } catch { }
	}

	/// <summary>合成到 WAV 文件（同步）。</summary>
	public string ExportWav(string text, string outWavPath) {
		if (string.IsNullOrWhiteSpace(text))
			throw new ArgumentException("文本为空");
		if (string.IsNullOrWhiteSpace(outWavPath))
			throw new ArgumentException("输出路径无效");
		var dir = Path.GetDirectoryName(outWavPath);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		try {
			syn.SpeakAsyncCancelAll();
			syn.SetOutputToWaveFile(outWavPath);
			syn.Speak(text);
		}
		finally {
			try { syn.SetOutputToDefaultAudioDevice(); } catch { }
		}
		if (!File.Exists(outWavPath) || new FileInfo(outWavPath).Length < 100)
			throw new InvalidOperationException("SAPI 未生成有效 WAV");
		return outWavPath;
	}

	/// <summary>
	/// 合成到 MP3。先 SAPI 写临时 WAV，再 MediaFoundation / ffmpeg 转 MP3。
	/// </summary>
	/// <returns>最终输出路径（.mp3 或失败时的 .wav）。</returns>
	public string ExportMp3(string text, string outMp3Path, int kbps = 192) {
		if (string.IsNullOrWhiteSpace(outMp3Path))
			throw new ArgumentException("输出路径无效");

		kbps = Compat.Clamp(kbps, 32, 320);
		var dir = Path.GetDirectoryName(outMp3Path);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);

		var wavPath = TmpStore.NewPath("tts", ".wav");
		ExportWav(text, wavPath);

		// 1) MediaFoundation → MP3
		try {
			wavToMp3Mf(wavPath, outMp3Path, kbps * 1000);
			if (File.Exists(outMp3Path) && new FileInfo(outMp3Path).Length > 100) {
				try { File.Delete(wavPath); } catch { }
				return outMp3Path;
			}
		}
		catch (Exception ex) {
			CaptureLog.Ex("SapiTts MF mp3", ex);
		}

		// 2) ffmpeg
		try {
			if (wavToMp3Ffmpeg(wavPath, outMp3Path, kbps)) {
				try { File.Delete(wavPath); } catch { }
				return outMp3Path;
			}
		}
		catch (Exception ex) {
			CaptureLog.Ex("SapiTts ffmpeg mp3", ex);
		}

		// 失败：落到同名 wav
		var fallback = Path.ChangeExtension(outMp3Path, ".wav");
		try {
			if (File.Exists(fallback)) File.Delete(fallback);
			File.Move(wavPath, fallback);
			return fallback;
		}
		catch {
			return wavPath;
		}
	}

	/// <summary>将已有 WAV 转为 MP3（供 Sherpa 导出复用）。失败返回 wav 旁 .wav。</summary>
	public static string ConvertWavToMp3(string wavPath, string mp3Path, int kbps = 192) {
		kbps = Compat.Clamp(kbps, 32, 320);
		try {
			wavToMp3Mf(wavPath, mp3Path, kbps * 1000);
			if (File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 100)
				return mp3Path;
		}
		catch (Exception ex) {
			CaptureLog.Ex("ConvertWavToMp3 MF", ex);
		}
		try {
			if (wavToMp3Ffmpeg(wavPath, mp3Path, kbps))
				return mp3Path;
		}
		catch (Exception ex) {
			CaptureLog.Ex("ConvertWavToMp3 ffmpeg", ex);
		}
		var fallback = Path.ChangeExtension(mp3Path, ".wav");
		try {
			if (File.Exists(fallback)) File.Delete(fallback);
			File.Copy(wavPath, fallback, true);
			return fallback;
		}
		catch {
			return wavPath;
		}
	}

	static void wavToMp3Mf(string wav, string mp3, int bitRate) {
		MediaFoundationApi.Startup();
		using var reader = new AudioFileReader(wav);
		MediaFoundationEncoder.EncodeToMp3(reader, mp3, bitRate);
	}

	static bool wavToMp3Ffmpeg(string wav, string mp3, int kbps) {
		var exe = findffmpeg();
		if (exe == null) return false;
		var args = $"-y -i \"{wav}\" -codec:a libmp3lame -b:a {kbps}k \"{mp3}\"";
		var psi = new ProcessStartInfo {
			FileName = exe,
			Arguments = args,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
		};
		using var p = Process.Start(psi);
		if (p == null) return false;
		p.StandardError.ReadToEnd();
		p.WaitForExit(120_000);
		return p.ExitCode == 0 && File.Exists(mp3) && new FileInfo(mp3).Length > 100;
	}

	static string findffmpeg() {
		var cands = new[] {
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg64", "ffmpeg.exe"),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
			@"C:\bin\ffmpeg.exe",
		};
		foreach (var c in cands) {
			try {
				if (File.Exists(c) && new FileInfo(c).Length > 5_000_000)
					return c;
			}
			catch { }
		}
		try {
			var path = Environment.GetEnvironmentVariable("PATH") ?? "";
			foreach (var dir in path.Split(Path.PathSeparator)) {
				var f = Path.Combine(dir.Trim(), "ffmpeg.exe");
				if (File.Exists(f) && new FileInfo(f).Length > 5_000_000)
					return f;
			}
		}
		catch { }
		return null;
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { syn.SpeakAsyncCancelAll(); } catch { }
		try { syn.Dispose(); } catch { }
	}
}
