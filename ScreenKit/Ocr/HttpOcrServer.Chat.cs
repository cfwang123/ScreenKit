using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>
/// HTTP LLM 对话：POST /api/chat。
/// 文本或语音入；文本出；可选 TTS（wav_base64）。
/// </summary>
sealed partial class HttpOcrServer {
	void handlechat(HttpListenerContext ctx) {
		JsonObject jo;
		try { jo = readjsonbody(ctx.Request); }
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}

		var o = getOpts?.Invoke() ?? new OcrOptions();
		var wantLlm = str(jo, "llm") ?? str(jo, "chat_llm");
		LlmEndpoint ep;
		if (!string.IsNullOrWhiteSpace(wantLlm)) {
			ep = o.FindLlm(wantLlm) ?? o.SelectedChatLlm();
		}
		else
			ep = o.SelectedChatLlm();
		if (!AsrLlmClient.IsEndpointReady(ep)) {
			writejson(ctx, 200, err(960, "未配置对话 LLM（参数设置 → LLM接口，或请求指定 llm）"));
			return;
		}

		var t0 = Environment.TickCount;
		var asrMs = 0;
		var llmMs = 0;
		var ttsMs = 0;
		string userText;
		string asrModel = "";

		try {
			userText = (str(jo, "text") ?? str(jo, "message") ?? str(jo, "content") ?? "").Trim();
			var hasAudio = (jo["base64"] != null && jo["base64"].GetValueKind() != JsonValueKind.Null
					&& !string.IsNullOrWhiteSpace(jo["base64"]?.GetValue<string>()))
				|| (jo["path"] != null && jo["path"].GetValueKind() == JsonValueKind.String
					&& !string.IsNullOrWhiteSpace(jo["path"].GetValue<string>()));

			if (userText.Length == 0 && hasAudio) {
				var tAsr = Environment.TickCount;
				userText = chatasr(jo, out asrModel);
				asrMs = Math.Max(0, Environment.TickCount - tAsr);
				userText = (userText ?? "").Trim();
			}

			if (userText.Length == 0) {
				writejson(ctx, 200, err(961, "请提供 text/message，或 base64/path 音频"));
				return;
			}
			if (userText.Length > 8000)
				userText = userText.Substring(0, 8000);

			var history = readchathistory(jo);
			history.Add(("user", userText));
			// Chat 要求以 user 开头：去掉打头的孤立 assistant
			while (history.Count > 0 && history[0].role != "user")
				history.RemoveAt(0);
			if (history.Count == 0 || history[history.Count - 1].role != "user") {
				writejson(ctx, 200, err(961, "对话历史无效（须以 user 开头且末条为 user）"));
				return;
			}

			var wantAgent = jo["agent"] != null
				? asbool(jo["agent"], o.ChatAgent)
				: o.ChatAgent;
			var oChat = o.Clone();
			oChat.ChatLlm = ep.DisplayName;
			oChat.ChatAgent = wantAgent;

			string reply;
			var tLlm = Environment.TickCount;
			try {
				if (wantAgent) {
					var result = LlmAgent.Run(oChat, history, null, null, CancellationToken.None);
					reply = result?.Reply ?? "";
				}
				else {
					reply = AsrLlmClient.Chat(oChat, history);
				}
			}
			catch (Exception ex) {
				writejson(ctx, 200, err(963, "对话失败: " + ex.Message));
				return;
			}
			llmMs = Math.Max(0, Environment.TickCount - tLlm);
			reply = (reply ?? "").Trim();
			if (reply.Length == 0) {
				writejson(ctx, 200, err(963, "LLM 返回空回复"));
				return;
			}

			// 与界面「自动朗读」一致：请求可传 auto_tts/tts；未传则用配置 chat_auto_tts
			var wantTts = resolvechattts(jo, o.ChatAutoTts);

			var data = new JsonObject {
				["text"] = reply,
				["reply"] = reply,
				["user_text"] = userText,
				["llm"] = ep.DisplayName,
				["model"] = ep.Model ?? "",
				["agent"] = wantAgent,
				["auto_tts"] = wantTts,
				["asr_ms"] = asrMs,
				["llm_ms"] = llmMs,
			};
			if (asrModel.Length > 0)
				data["asr_model"] = asrModel;

			if (wantTts) {
				try {
					var tTts = Environment.TickCount;
					var (samples, sr, provider, modelOut, voiceOut, sidOut, engOut) =
						chattts(jo, reply);
					ttsMs = Math.Max(0, Environment.TickCount - tTts);
					if (samples == null || samples.Length == 0) {
						data["tts_error"] = "合成结果为空";
					}
					else {
						var wav = floatstowav(samples, sr);
						data["format"] = "wav";
						data["sample_rate"] = sr;
						data["samples"] = samples.Length;
						data["wav_base64"] = Convert.ToBase64String(wav);
						data["engine"] = engOut;
						data["tts_model"] = modelOut;
						data["voice"] = voiceOut;
						data["speaker_id"] = sidOut;
						data["provider"] = provider;
					}
					data["tts_ms"] = ttsMs;
				}
				catch (Exception ex) {
					data["tts_error"] = ex.Message ?? "TTS 失败";
					data["tts_ms"] = Math.Max(0, Environment.TickCount - t0) - asrMs - llmMs;
				}
			}

			var ms = Math.Max(0, Environment.TickCount - t0);
			data["total_ms"] = ms;
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = data,
				["time"] = ms,
				["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
			});
		}
		catch (Exception ex) {
			var msg = ex.Message ?? "";
			var code = msg.IndexOf("ASR", StringComparison.OrdinalIgnoreCase) >= 0
				|| msg.IndexOf("识别", StringComparison.Ordinal) >= 0
				? 962 : 963;
			writejson(ctx, 200, err(code, msg));
		}
	}

	string chatasr(JsonObject jo, out string modelName) {
		modelName = "";
		if (svc?.AsrEngine == null)
			throw new InvalidOperationException("ASR 引擎不可用，无法识别语音消息");

		byte[] audioBytes = null;
		string pathHint = null;
		if (jo["base64"] != null && jo["base64"].GetValueKind() != JsonValueKind.Null) {
			audioBytes = decodebase64(jo["base64"]?.GetValue<string>() ?? "");
		}
		if ((audioBytes == null || audioBytes.Length == 0)
			&& jo["path"] != null && jo["path"].GetValueKind() == JsonValueKind.String)
			pathHint = jo["path"].GetValue<string>();
		if ((audioBytes == null || audioBytes.Length == 0) && string.IsNullOrWhiteSpace(pathHint))
			throw new InvalidOperationException("语音消息缺少 base64 或 path");

		var wantModel = str(jo, "asr_model") ?? str(jo, "model") ?? "";
		// model 也可能是 LLM 名：仅当像 ASR 包名或明确 asr_model 时选用
		if (string.IsNullOrWhiteSpace(str(jo, "asr_model")) && !string.IsNullOrWhiteSpace(str(jo, "llm")))
			wantModel = "";

		var lang = str(jo, "lang") ?? "auto";
		var useItn = jo["itn"] == null || jo["itn"].GetValueKind() == JsonValueKind.Null
			|| asbool(jo["itn"], true);
		var post = jo["postprocess"] == null || asbool(jo["postprocess"], true);
		var compute = parsecompute(str(jo, "device") ?? str(jo, "compute") ?? "auto");

		var models = svc.ScanAsr?.Invoke() ?? new List<AsrModelInfo>();
		AsrModelInfo model = null;
		if (!string.IsNullOrWhiteSpace(wantModel))
			model = models.FirstOrDefault(m =>
					!m.IsStreaming && string.Equals(m.DisplayName, wantModel, StringComparison.OrdinalIgnoreCase))
				?? models.FirstOrDefault(m =>
					!m.IsStreaming && Compat.Contains(m.DisplayName, wantModel, StringComparison.OrdinalIgnoreCase));
		if (model == null) {
			var opt = getOpts?.Invoke();
			if (!string.IsNullOrEmpty(opt?.AsrModel))
				model = models.FirstOrDefault(m =>
					!m.IsStreaming && string.Equals(m.DisplayName, opt.AsrModel, StringComparison.OrdinalIgnoreCase));
		}
		model ??= models.FirstOrDefault(m => !m.IsStreaming);
		if (model == null)
			throw new InvalidOperationException("无可用离线 ASR 模型");

		modelName = model.DisplayName ?? "";
		string tmpPath = null;
		try {
			if (audioBytes != null && audioBytes.Length > 0) {
				var ext = guessext(audioBytes, jo["filename"]?.GetValue<string>() ?? ".wav");
				tmpPath = Path.Combine(Path.GetTempPath(), "wpocr_chat_" + Guid.NewGuid().ToString("N") + ext);
				File.WriteAllBytes(tmpPath, audioBytes);
				pathHint = tmpPath;
			}
			if (string.IsNullOrWhiteSpace(pathHint) || !File.Exists(pathHint))
				throw new InvalidOperationException("音频文件不存在");

			string text;
			lock (svc.AsrGate ?? new object()) {
				var eng = svc.AsrEngine;
				eng.Mode = compute;
				eng.LoadModel(model, string.IsNullOrWhiteSpace(lang) ? "auto" : lang, useItn);
				var (samples, sr) = AsrAudio.LoadMedia(pathHint);
				text = eng.Recognize(samples, sr) ?? "";
			}
			if (post)
				text = AsrTextNorm.Postprocess(text ?? "");
			if (string.IsNullOrWhiteSpace(text))
				throw new InvalidOperationException("语音识别结果为空");
			return text.Trim();
		}
		finally {
			if (tmpPath != null)
				try { File.Delete(tmpPath); } catch { }
		}
	}

	(float[] samples, int sr, string provider, string model, string voice, int sid, string engine)
		chattts(JsonObject jo, string text) {
		var engineRaw = str(jo, "engine") ?? "";
		var modelName = str(jo, "tts_model") ?? "";
		// 避免把 LLM model 当成 TTS
		if (string.IsNullOrWhiteSpace(modelName) && string.IsNullOrWhiteSpace(str(jo, "llm")))
			modelName = str(jo, "model") ?? "";
		var voice = str(jo, "voice") ?? str(jo, "speaker") ?? "";
		int? sid = null;
		if (jo["speaker_id"] != null) sid = asint(jo["speaker_id"], 0);
		else if (jo["sid"] != null) sid = asint(jo["sid"], 0);
		var sidVal = sid ?? 0;
		var speed = 1f;
		if (jo["speed"] != null) speed = Compat.Clamp(asfloat(jo["speed"], 1f), 0.5f, 2f);
		var volume = 100;
		if (jo["volume"] != null) volume = Compat.Clamp(asint(jo["volume"], 100), 0, 100);
		var compute = parsecompute(str(jo, "device") ?? str(jo, "compute") ?? "auto");

		if (!tryparseengine(engineRaw, out var kind))
			throw new InvalidOperationException("未知 engine（sherpa / sapi / winrt）");
		if (kind == null)
			kind = inferengine(modelName, voice);
		if (kind == TtsEngineKind.Sherpa && svc?.TtsEngine == null)
			throw new InvalidOperationException("TTS 引擎不可用（Sherpa）");

		if (kind == TtsEngineKind.Sapi)
			return synthsapi(text, voice, sid, speed, volume);
		if (kind == TtsEngineKind.WinRt)
			return synthwinrt(text, voice, sid, speed, volume);
		return synthsherpa(text, modelName, sidVal, speed, compute);
	}

	static List<(string role, string content)> readchathistory(JsonObject jo) {
		var list = new List<(string, string)>();
		if (jo["messages"] == null || jo["messages"].GetValueKind() != JsonValueKind.Array)
			return list;
		foreach (var n in jo["messages"].AsArray()) {
			if (n is not JsonObject m) continue;
			var role = (str(m, "role") ?? "").Trim().ToLowerInvariant();
			var content = (str(m, "content") ?? str(m, "text") ?? "").Trim();
			if (role is not "user" and not "assistant") continue;
			if (content.Length == 0) continue;
			list.Add((role, content));
		}
		return list;
	}

	/// <summary>
	/// 解析是否自动 TTS。优先请求字段；都未传则用配置默认值。
	/// 显式 false 可关掉配置默认的自动朗读。
	/// </summary>
	static bool resolvechattts(JsonObject jo, bool configDefault) {
		if (jo == null) return configDefault;
		if (jo["auto_tts"] != null) return asbool(jo["auto_tts"], configDefault);
		if (jo["auto_speak"] != null) return asbool(jo["auto_speak"], configDefault);
		if (jo["tts"] != null) return asbool(jo["tts"], configDefault);
		if (jo["speak"] != null) return asbool(jo["speak"], configDefault);
		return configDefault;
	}
}
