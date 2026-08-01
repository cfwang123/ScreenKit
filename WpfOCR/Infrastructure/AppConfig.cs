using System.Globalization;
using System.IO;
using System.Text;

namespace WpfOCR;

/// <summary>
/// 应用配置：读写 exe 旁 config.toml。
/// </summary>
static class AppConfig {
	public static string ConfigPath =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.toml");

	public static void LoadInto(OcrOptions o) {
		if (o == null) return;
		var path = ConfigPath;
		if (!File.Exists(path)) {
			// 首次启动：尚未提示安装向导
			o.InstallPromptDone = false;
			try { Save(o); } catch { }
			return;
		}
		try {
			var text = File.ReadAllText(path, Encoding.UTF8);
			var map = parsetoml(text);
			if (map.TryGetValue("model_pack", out var mp) && !string.IsNullOrWhiteSpace(mp))
				o.ModelPackId = mp.Trim();
			if (map.TryGetValue("model_variant", out var mv))
				o.ModelVariant = mv?.Trim() ?? "";
			if (map.TryGetValue("device", out var dev))
				o.Device = parsedevice(dev);
			if (map.TryGetValue("det_limit", out var dl) && int.TryParse(dl, out var detLen))
				o.DetLimitSideLen = Compat.Clamp(detLen, 320, 4096);
			if (map.TryGetValue("det_thresh", out var dt) && float.TryParse(dt, NumberStyles.Float, CultureInfo.InvariantCulture, out var detTh))
				o.DetThresh = Compat.Clamp(detTh, 0.05f, 0.95f);
			if (map.TryGetValue("det_box_thresh", out var dbt) && float.TryParse(dbt, NumberStyles.Float, CultureInfo.InvariantCulture, out var boxTh))
				o.DetBoxThresh = Compat.Clamp(boxTh, 0.05f, 0.95f);
			if (map.TryGetValue("use_cls", out var uc))
				o.UseCls = parsebool(uc, true);
			// 热键允许空字符串 = 禁用（键存在即写入，勿因空白回退默认）
			if (map.TryGetValue("hotkey", out var hk))
				o.Hotkey = (hk ?? "").Trim();
			if (map.TryGetValue("hotkey_snap", out var hks))
				o.HotkeySnap = (hks ?? "").Trim();
			if (map.TryGetValue("hotkey_snap_ocr", out var hkso))
				o.HotkeySnapOcr = (hkso ?? "").Trim();
			if (map.TryGetValue("hotkey_board", out var hkb))
				o.HotkeyBoard = (hkb ?? "").Trim();
			if (map.TryGetValue("hotkey_voice_input", out var hkvi))
				o.HotkeyVoiceInput = (hkvi ?? "").Trim();
			if (map.TryGetValue("hotkey_live_caption", out var hklc))
				o.HotkeyLiveCaption = (hklc ?? "").Trim();
			if (map.TryGetValue("minimize_to_tray", out var mtt))
				o.MinimizeToTray = parsebool(mtt, true);
			if (map.TryGetValue("capture_log", out var cl))
				o.CaptureLog = parsebool(cl, false);
			// 截图历史保留天数：缺省 3；0 / unlimited / 不限 = 不自动删
			if (map.TryGetValue("screenshot_keep_days", out var skd)) {
				var s = (skd ?? "").Trim().Trim('"');
				if (s.Equals("unlimited", StringComparison.OrdinalIgnoreCase)
					|| s == "不限" || s == "-1")
					o.ScreenshotKeepDays = 0;
				else if (int.TryParse(s, out var days))
					o.ScreenshotKeepDays = days < 0 ? 0 : Compat.Clamp(days, 0, 3650);
			}
			if (map.TryGetValue("snap_copy_as_image", out var scai))
				o.SnapCopyAsImage = parsebool(scai, true);
			if (map.TryGetValue("snap_copy_as_file", out var scaf))
				o.SnapCopyAsFile = parsebool(scaf, false);
			// 二选一：仅文件开则文件模式，否则图片
			if (o.SnapCopyAsFile && !o.SnapCopyAsImage) {
				o.SnapCopyAsImage = false;
				o.SnapCopyAsFile = true;
			}
			else {
				o.SnapCopyAsImage = true;
				o.SnapCopyAsFile = false;
			}
			if (map.TryGetValue("ui_lang", out var ul) && !string.IsNullOrWhiteSpace(ul)) {
				var L = ul.Trim().Trim('"').ToLowerInvariant();
				o.UiLang = L is "en" or "en-us" or "english" ? "en" : "zh";
			}
			// 安装向导：键存在则读；旧配置无此键视为已提示（不打扰升级用户）
			if (map.TryGetValue("install_prompt_done", out var ipd))
				o.InstallPromptDone = parsebool(ipd, true);
			else
				o.InstallPromptDone = true;
			if (map.TryGetValue("win_w", out var ww) && double.TryParse(ww, NumberStyles.Float, CultureInfo.InvariantCulture, out var winW))
				o.WinW = winW;
			if (map.TryGetValue("win_h", out var wh) && double.TryParse(wh, NumberStyles.Float, CultureInfo.InvariantCulture, out var winH))
				o.WinH = winH;
			if (map.TryGetValue("win_l", out var wl) && double.TryParse(wl, NumberStyles.Float, CultureInfo.InvariantCulture, out var winL))
				o.WinL = winL;
			if (map.TryGetValue("win_t", out var wt) && double.TryParse(wt, NumberStyles.Float, CultureInfo.InvariantCulture, out var winT))
				o.WinT = winT;
			if (map.TryGetValue("win_max", out var wmax))
				o.WinMax = parsebool(wmax, false);
			if (map.TryGetValue("http_enabled", out var he))
				o.HttpEnabled = parsebool(he, true);
			if (map.TryGetValue("http_host", out var hh) && !string.IsNullOrWhiteSpace(hh))
				o.HttpHost = hh.Trim().Trim('"');
			if (map.TryGetValue("http_port", out var hp) && int.TryParse(hp, out var port))
				o.HttpPort = Compat.Clamp(port, 1, 65535);
			if (map.TryGetValue("service_mode", out var sm))
				o.ServiceMode = parsebool(sm, false);
			if (map.TryGetValue("pdf_invisible_text", out var pit))
				o.PdfInvisibleText = parsebool(pit, true);
			if (map.TryGetValue("pdf_dpi", out var pd) && int.TryParse(pd, out var pdfDpi))
				o.PdfDpi = Compat.Clamp(pdfDpi, 72, 400);
			// 录屏
			o.Record ??= new RecordOptions();
			if (map.TryGetValue("record_codec", out var rc) && !string.IsNullOrWhiteSpace(rc))
				o.Record.Codec = rc.Trim();
			if (map.TryGetValue("record_fps", out var rf) && int.TryParse(rf, out var rFps))
				o.Record.Fps = rFps;
			if (map.TryGetValue("record_crf", out var rcrf) && int.TryParse(rcrf, out var rCrf))
				o.Record.Crf = rCrf;
			if (map.TryGetValue("record_audio", out var ra))
				o.Record.AudioEnabled = parsebool(ra, true);
			if (map.TryGetValue("record_audio_src", out var ras) && !string.IsNullOrWhiteSpace(ras))
				o.Record.AudioSource = ras.Trim();
			if (map.TryGetValue("record_audio_kbps", out var rak) && int.TryParse(rak, out var rAkbps))
				o.Record.AudioKbps = rAkbps;
			if (map.TryGetValue("record_audio_hz", out var rah) && int.TryParse(rah, out var rAhz))
				o.Record.AudioHz = rAhz;
			if (map.TryGetValue("record_audio_mono", out var ram))
				o.Record.AudioMono = parsebool(ram, false);
			if (map.TryGetValue("record_max_size", out var rms))
				o.Record.MaxSizeEnabled = parsebool(rms, false);
			if (map.TryGetValue("record_max_w", out var rmw) && int.TryParse(rmw, out var rMaxW))
				o.Record.MaxWidth = rMaxW;
			if (map.TryGetValue("record_max_h", out var rmh) && int.TryParse(rmh, out var rMaxH))
				o.Record.MaxHeight = rMaxH;
			o.Record.Clamp();
			// TTS
			if (map.TryGetValue("tts_engine", out var te) && !string.IsNullOrWhiteSpace(te))
				o.TtsEngine = te.Trim();
			if (map.TryGetValue("tts_compute", out var tc) && !string.IsNullOrWhiteSpace(tc))
				o.TtsCompute = tc.Trim();
			if (map.TryGetValue("tts_model", out var tm))
				o.TtsModel = (tm ?? "").Trim();
			if (map.TryGetValue("tts_voice", out var tv))
				o.TtsVoice = (tv ?? "").Trim();
			if (map.TryGetValue("tts_lang", out var tl))
				o.TtsLangFilter = (tl ?? "").Trim();
			if (map.TryGetValue("tts_gender", out var tg))
				o.TtsGenderFilter = (tg ?? "").Trim();
			if (map.TryGetValue("tts_rate", out var tr) && double.TryParse(tr, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
				o.TtsRate = Compat.Clamp(rate, 0.5, 2.0);
			if (map.TryGetValue("tts_volume", out var tvol) && int.TryParse(tvol, out var vol))
				o.TtsVolume = Compat.Clamp(vol, 0, 100);
			if (map.TryGetValue("tts_kbps", out var tkb) && int.TryParse(tkb, out var kbps))
				o.TtsKbps = Compat.Clamp(kbps, 32, 320);
			// ASR
			if (map.TryGetValue("asr_model", out var am))
				o.AsrModel = (am ?? "").Trim();
			if (map.TryGetValue("asr_model_stream", out var ams))
				o.AsrModelStream = (ams ?? "").Trim();
			if (map.TryGetValue("asr_compute", out var ac) && !string.IsNullOrWhiteSpace(ac))
				o.AsrCompute = ac.Trim();
			if (map.TryGetValue("asr_lang", out var al) && !string.IsNullOrWhiteSpace(al))
				o.AsrLang = al.Trim();
			if (map.TryGetValue("asr_itn", out var aitn))
				o.AsrItn = parsebool(aitn, true);
			if (map.TryGetValue("asr_audio_source", out var aas) && !string.IsNullOrWhiteSpace(aas))
				o.AsrAudioSource = aas.Trim();
			// 翻译
			if (map.TryGetValue("translate_compute", out var trc) && !string.IsNullOrWhiteSpace(trc))
				o.TranslateCompute = trc.Trim();
			// 桌面字幕 OSD
			o.AsrCaption ??= new AsrCaptionStyle();
			var cap = o.AsrCaption;
			if (map.TryGetValue("asr_cap_x", out var cx) && double.TryParse(cx, NumberStyles.Float, CultureInfo.InvariantCulture, out var capX))
				cap.X = capX;
			if (map.TryGetValue("asr_cap_y", out var cy) && double.TryParse(cy, NumberStyles.Float, CultureInfo.InvariantCulture, out var capY))
				cap.Y = capY;
			if (map.TryGetValue("asr_cap_font", out var cf) && !string.IsNullOrWhiteSpace(cf))
				cap.FontFamily = cf.Trim().Trim('"');
			if (map.TryGetValue("asr_cap_size", out var cs) && double.TryParse(cs, NumberStyles.Float, CultureInfo.InvariantCulture, out var capSz))
				cap.FontSize = Compat.Clamp(capSz, 10, 96);
			if (map.TryGetValue("asr_cap_fg", out var cfgc) && !string.IsNullOrWhiteSpace(cfgc))
				cap.Foreground = cfgc.Trim().Trim('"');
			if (map.TryGetValue("asr_cap_outline", out var col) && !string.IsNullOrWhiteSpace(col))
				cap.Outline = col.Trim().Trim('"');
			if (map.TryGetValue("asr_cap_bg", out var cbg) && !string.IsNullOrWhiteSpace(cbg))
				cap.Background = cbg.Trim().Trim('"');
			if (map.TryGetValue("asr_cap_border", out var cbd) && !string.IsNullOrWhiteSpace(cbd))
				cap.BorderColor = cbd.Trim().Trim('"');
			if (map.TryGetValue("asr_cap_border_th", out var cbt) && double.TryParse(cbt, NumberStyles.Float, CultureInfo.InvariantCulture, out var capBt))
				cap.BorderThickness = Compat.Clamp(capBt, 0, 12);
			if (map.TryGetValue("asr_cap_align", out var cal) && int.TryParse(cal, out var capAl))
				cap.Align = Compat.Clamp(capAl, 0, 2);
			if (map.TryGetValue("asr_cap_w", out var cw) && double.TryParse(cw, NumberStyles.Float, CultureInfo.InvariantCulture, out var capW))
				cap.Width = Compat.Clamp(capW, 80, 4000);
			if (map.TryGetValue("asr_cap_h", out var ch) && double.TryParse(ch, NumberStyles.Float, CultureInfo.InvariantCulture, out var capH))
				cap.Height = Compat.Clamp(capH, 40, 3000);
			if (map.TryGetValue("asr_cap_maxw", out var cmw) && double.TryParse(cmw, NumberStyles.Float, CultureInfo.InvariantCulture, out var capMw))
				cap.MaxWidth = Compat.Clamp(capMw, 100, 4000);
			if (map.TryGetValue("asr_cap_autow", out var caw))
				cap.AutoWidth = parsebool(caw, false);
			if (map.TryGetValue("asr_cap_autoh", out var cah))
				cap.AutoHeight = parsebool(cah, false);
		}
		catch {
			// 配置损坏时用默认，不崩溃
		}
		// 同步诊断日志开关（缺省关闭）：截图 capture.log + 录屏 record_*.log
		applylogswitch(o.CaptureLog);
	}

	/// <summary>统一诊断日志开关（CaptureLog + RecordLog）。</summary>
	public static void applylogswitch(bool on) {
		CaptureLog.Enabled = on;
		RecordLog.Enabled = on;
	}

	public static void Save(OcrOptions o) {
		if (o == null) return;
		var sb = new StringBuilder();
		sb.AppendLine("# WpfOCR config — 可手工编辑，保存后部分项下次识别/重注册热键生效");
		sb.AppendLine();
		sb.AppendLine("[ocr]");
		sb.AppendLine($"model_pack = \"{esc(o.ModelPackId ?? "umi")}\"");
		sb.AppendLine($"model_variant = \"{esc(o.ModelVariant ?? "")}\"");
		sb.AppendLine($"device = \"{o.Device}\"");
		sb.AppendLine($"det_limit = {o.DetLimitSideLen}");
		sb.AppendLine($"det_thresh = {o.DetThresh.ToString(CultureInfo.InvariantCulture)}");
		sb.AppendLine($"det_box_thresh = {o.DetBoxThresh.ToString(CultureInfo.InvariantCulture)}");
		sb.AppendLine($"use_cls = {(o.UseCls ? "true" : "false")}");
		sb.AppendLine();
		sb.AppendLine("[ui]");
		// 空字符串表示禁用热键，禁止写回默认值
		sb.AppendLine($"# 热键留空 = 禁用（默认 Ctrl+Alt+O / Q / W）");
		sb.AppendLine($"hotkey = \"{esc((o.Hotkey ?? "").Trim())}\"");
		sb.AppendLine($"# 截图标注（框选 → 画框/线/箭头/文字 → 保存或复制）");
		sb.AppendLine($"hotkey_snap = \"{esc((o.HotkeySnap ?? "").Trim())}\"");
		sb.AppendLine($"# 截图并识别文字");
		sb.AppendLine($"hotkey_snap_ocr = \"{esc((o.HotkeySnapOcr ?? "").Trim())}\"");
		sb.AppendLine($"# 屏幕画板（全屏冻结后标注；默认空=禁用）");
		sb.AppendLine($"hotkey_board = \"{esc((o.HotkeyBoard ?? "").Trim())}\"");
		sb.AppendLine($"# 语音输入（麦克风听写注入焦点窗口；再按一次结束）");
		sb.AppendLine($"hotkey_voice_input = \"{esc((o.HotkeyVoiceInput ?? "").Trim())}\"");
		sb.AppendLine($"# 系统实时字幕（桌面流式字幕；再按一次结束；默认 Ctrl+Alt+B）");
		sb.AppendLine($"hotkey_live_caption = \"{esc((o.HotkeyLiveCaption ?? "").Trim())}\"");
		sb.AppendLine($"minimize_to_tray = {(o.MinimizeToTray ? "true" : "false")}");
		sb.AppendLine($"# 系统诊断日志（默认 false）：log/capture.log + 录屏 log/record_*.log");
		sb.AppendLine($"capture_log = {(o.CaptureLog ? "true" : "false")}");
		sb.AppendLine($"# 截图历史 screenshots/ 保留天数（默认 3；0=不限）");
		sb.AppendLine($"screenshot_keep_days = {Compat.Clamp(o.ScreenshotKeepDays < 0 ? 0 : o.ScreenshotKeepDays, 0, 3650)}");
		sb.AppendLine($"# 截图完成时剪贴板：复制为图片 / 复制为文件（二选一）");
		var snapFile = o.SnapCopyAsFile && !o.SnapCopyAsImage;
		sb.AppendLine($"snap_copy_as_image = {(!snapFile ? "true" : "false")}");
		sb.AppendLine($"snap_copy_as_file = {(snapFile ? "true" : "false")}");
		sb.AppendLine($"# 界面语言 zh | en");
		var uiLang = string.Equals(o.UiLang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
		sb.AppendLine($"ui_lang = \"{uiLang}\"");
		sb.AppendLine($"# 首次启动安装向导是否已提示过");
		sb.AppendLine($"install_prompt_done = {(o.InstallPromptDone ? "true" : "false")}");
		sb.AppendLine($"# 主窗位置与大小（DIP）");
		sb.AppendLine($"win_w = {o.WinW.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"win_h = {o.WinH.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"win_l = {o.WinL.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"win_t = {o.WinT.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"win_max = {(o.WinMax ? "true" : "false")}");
		sb.AppendLine();
		sb.AppendLine("[http]");
		sb.AppendLine($"http_enabled = {(o.HttpEnabled ? "true" : "false")}");
		sb.AppendLine($"http_host = \"{esc(string.IsNullOrWhiteSpace(o.HttpHost) ? "127.0.0.1" : o.HttpHost)}\"");
		sb.AppendLine($"http_port = {o.HttpPort}");
		sb.AppendLine($"# 服务模式：引擎常驻预热，不主动释放");
		sb.AppendLine($"service_mode = {(o.ServiceMode ? "true" : "false")}");
		sb.AppendLine();
		sb.AppendLine("[pdf]");
		sb.AppendLine($"# PDF 识别后叠加不可见文字层（可检索/复制）");
		sb.AppendLine($"pdf_invisible_text = {(o.PdfInvisibleText ? "true" : "false")}");
		sb.AppendLine($"# 内部光栅 DPI（页面物理尺寸按原 PDF；一般无需改）");
		sb.AppendLine($"pdf_dpi = {Compat.Clamp(o.PdfDpi <= 0 ? 150 : o.PdfDpi, 72, 400)}");
		sb.AppendLine();
		var rec = o.Record ?? new RecordOptions();
		rec.Clamp();
		sb.AppendLine("[record]");
		sb.AppendLine($"# 录屏：x264 / x265");
		sb.AppendLine($"record_codec = \"{esc(rec.Codec)}\"");
		sb.AppendLine($"record_fps = {rec.Fps}");
		sb.AppendLine($"# CRF 0~51，越大体积越小");
		sb.AppendLine($"record_crf = {rec.Crf}");
		sb.AppendLine($"# 声音：record_audio / Speakers|Mic|MicAndSpeakers / kbps 8~128 / Hz 常用 22050 / mono");
		sb.AppendLine($"record_audio = {(rec.AudioEnabled ? "true" : "false")}");
		sb.AppendLine($"record_audio_src = \"{esc(rec.AudioSource)}\"");
		sb.AppendLine($"record_audio_kbps = {rec.AudioKbps}");
		sb.AppendLine($"record_audio_hz = {rec.AudioHz}");
		sb.AppendLine($"record_audio_mono = {(rec.AudioMono ? "true" : "false")}");
		sb.AppendLine($"# 限制输出最大宽高（等比 fit）");
		sb.AppendLine($"record_max_size = {(rec.MaxSizeEnabled ? "true" : "false")}");
		sb.AppendLine($"record_max_w = {rec.MaxWidth}");
		sb.AppendLine($"record_max_h = {rec.MaxHeight}");
		sb.AppendLine();
		sb.AppendLine("[tts]");
		sb.AppendLine("# 引擎 Sapi | Sherpa；计算 Auto | Gpu | Cpu | Igpu");
		sb.AppendLine($"tts_engine = \"{esc(string.IsNullOrWhiteSpace(o.TtsEngine) ? "Sherpa" : o.TtsEngine)}\"");
		sb.AppendLine($"tts_compute = \"{esc(string.IsNullOrWhiteSpace(o.TtsCompute) ? "Auto" : o.TtsCompute)}\"");
		sb.AppendLine($"tts_model = \"{esc(o.TtsModel ?? "")}\"");
		sb.AppendLine($"tts_voice = \"{esc(o.TtsVoice ?? "")}\"");
		sb.AppendLine($"# 筛选 zh|en|空；male|female|空");
		sb.AppendLine($"tts_lang = \"{esc(o.TtsLangFilter ?? "")}\"");
		sb.AppendLine($"tts_gender = \"{esc(o.TtsGenderFilter ?? "")}\"");
		sb.AppendLine($"tts_rate = {o.TtsRate.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"tts_volume = {Compat.Clamp(o.TtsVolume, 0, 100)}");
		sb.AppendLine($"tts_kbps = {Compat.Clamp(o.TtsKbps, 32, 320)}");
		sb.AppendLine();
		sb.AppendLine("[asr]");
		sb.AppendLine("# 语音识别：asr_model=离线/字幕；asr_model_stream=流式语音输入；计算 Auto|Gpu|Cpu|Igpu；语言 auto|zh|en|ja|ko|yue");
		sb.AppendLine($"asr_model = \"{esc(o.AsrModel ?? "")}\"");
		sb.AppendLine($"asr_model_stream = \"{esc(o.AsrModelStream ?? "")}\"");
		sb.AppendLine($"asr_compute = \"{esc(string.IsNullOrWhiteSpace(o.AsrCompute) ? "Auto" : o.AsrCompute)}\"");
		sb.AppendLine($"asr_lang = \"{esc(string.IsNullOrWhiteSpace(o.AsrLang) ? "auto" : o.AsrLang)}\"");
		sb.AppendLine($"asr_itn = {(o.AsrItn ? "true" : "false")}");
		sb.AppendLine($"# 录音/实时字幕声音：Mic | System | MicAndSystem");
		sb.AppendLine($"asr_audio_source = \"{esc(string.IsNullOrWhiteSpace(o.AsrAudioSource) ? "Mic" : o.AsrAudioSource)}\"");
		sb.AppendLine();
		sb.AppendLine("[translate]");
		sb.AppendLine("# 翻译计算设备 Auto | Gpu | Cpu | Igpu（进程内 ONNX，与 OCR 共用 CUDA/DirectML）");
		sb.AppendLine($"translate_compute = \"{esc(string.IsNullOrWhiteSpace(o.TranslateCompute) ? "Auto" : o.TranslateCompute)}\"");
		var cap = o.AsrCaption ?? new AsrCaptionStyle();
		sb.AppendLine("# 桌面实时字幕 OSD：位置/字体/颜色/宽高（#AARRGGBB）");
		if (!double.IsNaN(cap.X))
			sb.AppendLine($"asr_cap_x = {cap.X.ToString("0.##", CultureInfo.InvariantCulture)}");
		if (!double.IsNaN(cap.Y))
			sb.AppendLine($"asr_cap_y = {cap.Y.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"asr_cap_font = \"{esc(cap.FontFamily ?? "Microsoft YaHei UI")}\"");
		sb.AppendLine($"asr_cap_size = {cap.FontSize.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"asr_cap_fg = \"{esc(cap.Foreground ?? "#FFFFFFFF")}\"");
		sb.AppendLine($"asr_cap_outline = \"{esc(cap.Outline ?? "#CC000000")}\"");
		sb.AppendLine($"asr_cap_bg = \"{esc(cap.Background ?? "#66000000")}\"");
		sb.AppendLine($"asr_cap_border = \"{esc(cap.BorderColor ?? "#00000000")}\"");
		sb.AppendLine($"asr_cap_border_th = {cap.BorderThickness.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"asr_cap_align = {Compat.Clamp(cap.Align, 0, 2)}");
		sb.AppendLine($"asr_cap_w = {cap.Width.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"asr_cap_h = {cap.Height.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"asr_cap_maxw = {cap.MaxWidth.ToString("0.##", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"asr_cap_autow = {(cap.AutoWidth ? "true" : "false")}");
		sb.AppendLine($"asr_cap_autoh = {(cap.AutoHeight ? "true" : "false")}");
		File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		applylogswitch(o.CaptureLog);
	}

	static string esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

	static OcrDevice parsedevice(string s) {
		s = (s ?? "").Trim();
		if (s.Equals("Gpu", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("GPU", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("cuda", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("nvidia", StringComparison.OrdinalIgnoreCase))
			return OcrDevice.Gpu;
		if (s.Equals("IntelGpu", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("Intel", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("Dml", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("DirectML", StringComparison.OrdinalIgnoreCase)
			|| s.Equals("核显", StringComparison.OrdinalIgnoreCase))
			return OcrDevice.IntelGpu;
		// Auto 已废弃，旧配置按 CPU
		return OcrDevice.Cpu;
	}

	static bool parsebool(string s, bool def) {
		if (string.IsNullOrWhiteSpace(s)) return def;
		s = s.Trim();
		if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase))
			return true;
		if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase))
			return false;
		return def;
	}

	/// <summary>极简 TOML：忽略节名，收集 key = value（支持引号字符串与裸值）。</summary>
	static Dictionary<string, string> parsetoml(string text) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(text)) return map;
		foreach (var raw in text.Replace("\r\n", "\n").Split('\n')) {
			var line = raw.Trim();
			if (line.Length == 0 || line[0] == '#' || line[0] == '[') continue;
			var eq = line.IndexOf('=');
			if (eq <= 0) continue;
			var key = line[..eq].Trim();
			var val = line[(eq + 1)..].Trim();
			// 去掉行尾注释
			var hash = val.IndexOf('#');
			if (hash >= 0 && !(val.StartsWith("\"") && val.LastIndexOf('"') > 0 && hash > val.LastIndexOf('"')))
				val = val[..hash].Trim();
			if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
				val = val[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
			if (key.Length > 0)
				map[key] = val;
		}
		return map;
	}
}
