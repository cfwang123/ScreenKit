using System.Runtime.InteropServices.WindowsRuntime;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace ScreenKit;

/// <summary>SAPI / WinRT 发音人统一项（供下拉框绑定与持久化）。</summary>
sealed class SapiVoiceItem {
	/// <summary>下拉显示名。</summary>
	public string DisplayName { get; set; } = "";
	/// <summary>持久化键：sapi:&lt;name&gt; 或 winrt:&lt;id&gt;。</summary>
	public string Key { get; set; } = "";
	/// <summary>原始名（sapi=VoiceInfo.Name；winrt=VoiceInformation.Id）。</summary>
	public string Name { get; set; } = "";
	/// <summary>完整区域性，如 vi-VN。</summary>
	public string Culture { get; set; } = "";
	/// <summary>两位语言码，如 vi。</summary>
	public string Lang { get; set; } = "";
	/// <summary>male / female。</summary>
	public string Gender { get; set; } = "";
	/// <summary>sapi / winrt。</summary>
	public string Source { get; set; } = "";
	public override string ToString() => DisplayName;

	public static string LangOf(string culture) {
		if (string.IsNullOrEmpty(culture)) return "";
		var i = culture.IndexOf('-');
		return (i > 0 ? culture.Substring(0, i) : culture).ToLowerInvariant();
	}
}

/// <summary>WinRT 现代语音合成：枚举 OneCore 神经语音（含越南语等），合成到 float[]。</summary>
sealed class WinRtTts : IDisposable {
	readonly List<SapiVoiceItem> voices = new();
	string selectedVoiceId;
	double rate = 1.0;
	double volume = 1.0;

	public IReadOnlyList<SapiVoiceItem> Voices => voices;

	public WinRtTts() {
		refreshvoices();
	}

	void refreshvoices() {
		voices.Clear();
		try {
			foreach (var v in SpeechSynthesizer.AllVoices.OrderBy(x => x.Language).ThenBy(x => x.DisplayName)) {
				var culture = v.Language ?? "";
				var lang = SapiVoiceItem.LangOf(culture);
				var dn = v.DisplayName ?? "";
				// 显示：名称 · 区域 · 性别
				var g = genderof(v.Gender);
				var gLabel = TtsGender.Label(g);
				var tail = string.IsNullOrEmpty(culture) ? "" : " · " + culture;
				if (!string.IsNullOrEmpty(gLabel)) tail += " · " + gLabel;
				voices.Add(new SapiVoiceItem {
					DisplayName = dn + tail,
					Key = "winrt:" + (v.Id ?? ""),
					Name = v.Id ?? "",
					Culture = culture,
					Lang = lang,
					Gender = g,
					Source = "winrt",
				});
			}
		}
		catch (Exception ex) {
			CaptureLog.Ex("WinRtTts enum", ex);
		}
	}

	static string genderof(VoiceGender g) => g switch {
		VoiceGender.Female => TtsGender.Female,
		VoiceGender.Male => TtsGender.Male,
		_ => "",
	};

	/// <param name="key">winrt:&lt;id&gt;。</param>
	public bool SelectVoice(string key) {
		if (string.IsNullOrEmpty(key)) return false;
		var id = key.StartsWith("winrt:", StringComparison.Ordinal) ? key.Substring(6) : key;
		var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.Id == id);
		if (v == null) return false;
		selectedVoiceId = v.Id;
		return true;
	}

	/// <param name="rate">0.5～6.0，1.0=常速（UI 倍率直接传入）。</param>
	/// <param name="volume">0～100。</param>
	public void SetRateVolume(double rate, int volume) {
		this.rate = Compat.Clamp(rate, 0.5, 6.0);
		this.volume = Compat.Clamp(volume / 100.0, 0.0, 1.0);
	}

	/// <summary>合成文本 -> (float[] samples, sampleRate)。</summary>
	/// <remarks>每次新建 SpeechSynthesizer：跨套间安全（构造与调用同在合成线程）。</remarks>
	public async Task<(float[] samples, int sampleRate)> Synthesize(string text) {
		if (string.IsNullOrWhiteSpace(text))
			return (Array.Empty<float>(), 22050);
		SpeechSynthesisStream stream;
		try {
			using var syn = new SpeechSynthesizer();
			if (!string.IsNullOrEmpty(selectedVoiceId)) {
				var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.Id == selectedVoiceId);
				if (v != null) syn.Voice = v;
			}
			try { syn.Options.SpeakingRate = rate; } catch { }
			try { syn.Options.AudioVolume = volume; } catch { }
			stream = await syn.SynthesizeTextToStreamAsync(text).AsTask().ConfigureAwait(false);
		}
		catch (Exception ex) {
			CaptureLog.Ex("WinRtTts synth", ex);
			throw new InvalidOperationException("WinRT 合成失败: " + ex.Message, ex);
		}
		var size = (int)Math.Min((long)stream.Size, int.MaxValue);
		if (size <= 0)
			return (Array.Empty<float>(), 22050);
		byte[] buf;
		using (var dr = new DataReader(stream.GetInputStreamAt(0))) {
			await dr.LoadAsync((uint)size).AsTask().ConfigureAwait(false);
			buf = new byte[size];
			dr.ReadBytes(buf);
		}
		return wavetofloat(buf);
	}

	static (float[], int) wavetofloat(byte[] wav) {
		try {
			using var ms = new MemoryStream(wav, false);
			using var reader = new WaveFileReader(ms);
			var sr = reader.WaveFormat.SampleRate;
			var sp = reader.ToSampleProvider();
			var list = new List<float>(wav.Length / 4);
			var buf = new float[4096];
			int n;
			while ((n = sp.Read(buf, 0, buf.Length)) > 0) {
				for (int k = 0; k < n; k++)
					list.Add(buf[k]);
			}
			return (list.ToArray(), sr);
		}
		catch (Exception ex) {
			CaptureLog.Ex("WinRtTts wav parse", ex);
			return (Array.Empty<float>(), 22050);
		}
	}

	public void Dispose() {
	}
}
