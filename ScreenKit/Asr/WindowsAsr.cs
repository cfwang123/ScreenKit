using System.Globalization;
using System.IO;
using System.Speech.Recognition;

namespace ScreenKit;

/// <summary>Windows System.Speech 听写识别；使用系统已安装的语音识别器。</summary>
static class WindowsAsr {
	public static List<AsrModelInfo> Scan() {
		var list = new List<AsrModelInfo>();
		try {
			foreach (var r in SpeechRecognitionEngine.InstalledRecognizers()
				.OrderBy(x => x.Culture?.DisplayName).ThenBy(x => x.Name)) {
				var culture = r.Culture?.Name ?? "";
				var lang = r.Culture?.DisplayName ?? culture;
				list.Add(new AsrModelInfo {
					DisplayName = $"Windows · {lang} · {r.Name}",
					IsWindows = true,
					SystemRecognizerId = r.Id ?? "",
					Culture = culture,
					SampleRate = 16000,
				});
			}
		}
		catch (Exception ex) {
			CaptureLog.Ex("WindowsAsr.Scan", ex);
		}
		return list;
	}

	public static string Recognize(AsrModelInfo model, float[] samples, int sampleRate) =>
		RecognizeDetailed(model, samples, sampleRate).Text;

	public static AsrResult RecognizeDetailed(AsrModelInfo model, float[] samples, int sampleRate,
		Action<AsrResult, double, double> onProgress = null, CancellationToken ct = default) {
		if (model == null || !model.IsWindows)
			throw new ArgumentException("未选择 Windows 系统语音识别器", nameof(model));
		if (samples == null || samples.Length == 0) return AsrResult.Empty;
		if (sampleRate <= 0) sampleRate = 16000;
		ct.ThrowIfCancellationRequested();

		using var wav = makewav(samples, sampleRate);
		using var eng = create(model);
		eng.SetInputToWaveStream(wav);
		var texts = new List<string>();
		var tokens = new List<string>();
		var stamps = new List<float>();
		var durs = new List<float>();
		var totalSec = samples.Length / (double)sampleRate;
		var latin = usespaces(model.Culture);
		RecognitionResult rejected = null;
		eng.SpeechRecognitionRejected += (_, e) => {
			if (e.Result != null && (rejected == null || e.Result.Confidence > rejected.Confidence))
				rejected = e.Result;
		};

		while (true) {
			ct.ThrowIfCancellationRequested();
			RecognitionResult r;
			try { r = eng.Recognize(); }
			catch (InvalidOperationException) when (wav.Position >= wav.Length) { break; }
			if (r == null) break;
			addresult(r);
		}
		ct.ThrowIfCancellationRequested();
		if (texts.Count == 0 && rejected != null)
			addresult(rejected);
		return result(texts, tokens, stamps, durs, latin);
		void addresult(RecognitionResult r) {
			var text = (r?.Text ?? "").Trim();
			if (text.Length == 0) return;
			var token = latin && texts.Count > 0 ? " " + text : text;
			texts.Add(text);
			tokens.Add(token);
			float pos = 0, dur = 0;
			try {
				if (r.Audio != null) {
					pos = (float)Math.Max(0, r.Audio.AudioPosition.TotalSeconds);
					dur = (float)Math.Max(0, r.Audio.Duration.TotalSeconds);
				}
			}
			catch { }
			stamps.Add(pos);
			durs.Add(dur);
			var partial = result(texts, tokens, stamps, durs, latin);
			try { onProgress?.Invoke(partial, Math.Min(totalSec, pos + dur), totalSec); } catch { }
		}
	}

	static SpeechRecognitionEngine create(AsrModelInfo model) {
		SpeechRecognitionEngine eng = null;
		try {
			eng = !string.IsNullOrWhiteSpace(model.SystemRecognizerId)
				? new SpeechRecognitionEngine(model.SystemRecognizerId)
				: new SpeechRecognitionEngine(CultureInfo.GetCultureInfo(model.Culture));
			eng.InitialSilenceTimeout = TimeSpan.FromSeconds(30);
			eng.BabbleTimeout = TimeSpan.Zero;
			eng.EndSilenceTimeout = TimeSpan.FromMilliseconds(500);
			eng.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(900);
			try { eng.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", 0); } catch { }
			eng.LoadGrammar(new DictationGrammar());
			return eng;
		}
		catch (Exception ex) {
			try { eng?.Dispose(); } catch { }
			throw new InvalidOperationException(
				$"Windows 系统语音识别器不可用（{model.Culture}）: {ex.Message}", ex);
		}
	}

	static AsrResult result(List<string> texts, List<string> tokens, List<float> stamps,
		List<float> durs, bool latin) => new() {
		Text = string.Join(latin ? " " : "", texts).Trim(),
		Tokens = tokens.ToArray(),
		Timestamps = stamps.ToArray(),
		Durations = durs.ToArray(),
	};

	static bool usespaces(string culture) {
		var lang = "";
		try { lang = CultureInfo.GetCultureInfo(culture ?? "").TwoLetterISOLanguageName; } catch { }
		return lang is not ("zh" or "ja");
	}

	static MemoryStream makewav(float[] samples, int sampleRate) {
		var ms = new MemoryStream(44 + samples.Length * 2);
		using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) {
			var dataBytes = samples.Length * 2;
			w.Write(new[] { 'R', 'I', 'F', 'F' });
			w.Write(36 + dataBytes);
			w.Write(new[] { 'W', 'A', 'V', 'E' });
			w.Write(new[] { 'f', 'm', 't', ' ' });
			w.Write(16);
			w.Write((short)1);
			w.Write((short)1);
			w.Write(sampleRate);
			w.Write(sampleRate * 2);
			w.Write((short)2);
			w.Write((short)16);
			w.Write(new[] { 'd', 'a', 't', 'a' });
			w.Write(dataBytes);
			foreach (var sample in samples) {
				var v = Compat.Clamp(sample, -1f, 1f);
				w.Write((short)Math.Round(v * (v < 0 ? 32768f : 32767f)));
			}
		}
		ms.Position = 0;
		return ms;
	}
}
