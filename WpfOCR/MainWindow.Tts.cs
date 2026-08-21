using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfOCR;

/// <summary>MainWindow：语音合成 Tab（分段朗读 / 暂停跳转 / 导出 MP3）。</summary>
public partial class MainWindow {
	SapiTts sapiTts;
	WinRtTts winRtTts;
	TtsEngine sherpaTts;
	TtsPlayer ttsPlayer;
	List<TtsModelInfo> ttsModels = new();
	/// <summary>x86 Web 枚举到的 SAPI 发音人缓存（按需刷新）。</summary>
	List<SapiVoiceItem> sapiX86VoicesCache = new();
	bool ttsUiLoading;
	CancellationTokenSource ttsSpeakCts;
	bool ttsSession;
	bool ttsPaused;
	int ttsSkipDelta;
	int ttsSegIndex;
	List<TtsSegment> ttsSegs;
	string ttsUiText;

	void inittts() {
		ttsPlayer = new TtsPlayer();
		try { sapiTts = new SapiTts(); }
		catch (Exception ex) {
			CaptureLog.Ex("SapiTts init", ex);
			sapiTts = null;
		}
		try { winRtTts = new WinRtTts(); }
		catch (Exception ex) {
			CaptureLog.Ex("WinRtTts init", ex);
			winRtTts = null;
		}
		try { sherpaTts = new TtsEngine(); }
		catch (Exception ex) {
			CaptureLog.Ex("TtsEngine init", ex);
			sherpaTts = null;
		}

		ttsUiLoading = true;
		ettsengine.Items.Clear();
		ettsengine.Items.Add(new ComboBoxItem { Content = "SAPI（经典系统语音）", Tag = TtsEngineKind.Sapi });
		ettsengine.Items.Add(new ComboBoxItem {
			Content = "Windows 语音（WinRT / OneCore，含越南语等）",
			Tag = TtsEngineKind.WinRt,
			IsEnabled = winRtTts != null && winRtTts.Voices.Count > 0,
		});
		ettsengine.Items.Add(new ComboBoxItem { Content = "Sherpa-ONNX（离线神经网络）", Tag = TtsEngineKind.Sherpa });
		// 默认：有 WinRT 越南语等现代语音时优先 WinRT，否则 Sherpa，再 SAPI
		if (winRtTts != null && winRtTts.Voices.Count > 0)
			ettsengine.SelectedIndex = 1;
		else if (sherpaTts != null)
			ettsengine.SelectedIndex = 2;
		else
			ettsengine.SelectedIndex = 0;

		ettscompute.Items.Clear();
		ettscompute.Items.Add(new ComboBoxItem { Content = "自动（CUDA→核显→CPU）", Tag = TtsComputeMode.Auto });
		ettscompute.Items.Add(new ComboBoxItem { Content = "GPU（NVIDIA CUDA）", Tag = TtsComputeMode.Gpu });
		ettscompute.Items.Add(new ComboBoxItem { Content = "核显（Intel DirectML）", Tag = TtsComputeMode.Igpu });
		ettscompute.Items.Add(new ComboBoxItem { Content = "CPU", Tag = TtsComputeMode.Cpu });
		ettscompute.SelectedIndex = 0;

		// 语言列表：根据当前引擎可用发音人动态填充
		rebuildttslangcombo(preserve: false);
		ettsgender.Items.Clear();
		ettsgender.Items.Add(new ComboBoxItem { Content = "全部性别", Tag = "" });
		ettsgender.Items.Add(new ComboBoxItem { Content = "女声", Tag = TtsGender.Female });
		ettsgender.Items.Add(new ComboBoxItem { Content = "男声", Tag = TtsGender.Male });
		ettsgender.SelectedIndex = 0;

		ettsrate.Value = 1.0;
		ettsvol.Value = 100;
		lbttsrate.Text = "1.0x";
		lbttsvol.Text = "100";

		ettskbps.Items.Clear();
		foreach (var k in new[] { 32, 48, 64, 96, 128, 160, 192, 256, 320 }) {
			var it = new ComboBoxItem { Content = $"{k} kbps", Tag = k };
			ettskbps.Items.Add(it);
			if (k == 192) ettskbps.SelectedItem = it;
		}
		if (ettskbps.SelectedItem == null && ettskbps.Items.Count > 0)
			ettskbps.SelectedIndex = 0;

		restorettsprefs();

		ettsrate.ValueChanged += (_, _) => {
			lbttsrate.Text = $"{ettsrate.Value:0.0}x";
			if (!ttsUiLoading) savettsprefs();
		};
		ettsvol.ValueChanged += (_, _) => {
			lbttsvol.Text = ((int)ettsvol.Value).ToString();
			if (sapiTts != null) sapiTts.Volume = (int)ettsvol.Value;
			if (!ttsUiLoading) savettsprefs();
		};
		ettsengine.SelectionChanged += (_, _) => {
			if (ttsUiLoading) return;
			rebuildttslangcombo(preserve: true);
			applyttsengineui();
			savettsprefs();
		};
		ettscompute.SelectionChanged += (_, _) => {
			if (ttsUiLoading || sherpaTts == null) return;
			if (ettscompute.SelectedItem is ComboBoxItem it && it.Tag is TtsComputeMode m) {
				sherpaTts.Mode = m;
				try { sherpaTts.UnloadSafe(); } catch { }
				lbttsstatus.Text = "计算设备 → " + it.Content + "（下次合成时加载）";
				savettsprefs();
			}
		};
		ettslang.SelectionChanged += (_, _) => {
			if (ttsUiLoading) return;
			applyttsfilter();
			savettsprefs();
		};
		ettsgender.SelectionChanged += (_, _) => {
			if (ttsUiLoading) return;
			applyttsfilter();
			savettsprefs();
		};
		ettsmodel.SelectionChanged += (_, _) => {
			if (ttsUiLoading) return;
			fillttsspeakers();
			restorettsvoice();
			savettsprefs();
		};
		ettsvoice.SelectionChanged += (_, _) => {
			if (ttsUiLoading) return;
			var eng = currentttsengine();
			if (eng == TtsEngineKind.Sapi && ettsvoice.SelectedItem is SapiVoiceItem sv) {
				try {
					if (sv.Source != "sapi-x86" && !string.IsNullOrEmpty(sv.Name))
						sapiTts?.SelectVoice(sv.Name);
					lbttsstatus.Text = (sv.Source == "sapi-x86" ? "SAPI x86 · " : "SAPI · ") + sv.Name;
				}
				catch (Exception ex) { lbttsstatus.Text = "选语音失败: " + ex.Message; }
			}
			else if (eng == TtsEngineKind.WinRt
				&& ettsvoice.SelectedItem is SapiVoiceItem wi) {
				try {
					winRtTts?.SelectVoice(wi.Key);
					lbttsstatus.Text = "WinRT · " + wi.DisplayName;
				}
				catch (Exception ex) { lbttsstatus.Text = "选语音失败: " + ex.Message; }
			}
			savettsprefs();
		};
		ettskbps.SelectionChanged += (_, _) => {
			if (!ttsUiLoading) savettsprefs();
		};

		bttsspeak.Click += (_, _) => _ = ttsspeakasync();
		bttspause.Click += (_, _) => ttstogglepause();
		bttsprev.Click += (_, _) => ttsjump(-1);
		bttsnext.Click += (_, _) => ttsjump(+1);
		bttsstop.Click += (_, _) => ttsstop();
		bttsexport.Click += (_, _) => _ = ttsexportasync();
		bttsreload.Click += (_, _) => {
			scanttssmodels();
			rebuildttslangcombo(preserve: true);
			applyttsengineui();
			restorettsmodelandvoice();
		};
		scanttssmodels();
		// 扫描完 Sherpa 后再并入模型语言
		rebuildttslangcombo(preserve: true);
		ttsUiLoading = false;
		applyttsengineui();
		restorettsmodelandvoice();
		updatettsctrlui();
	}

	void restorettsprefs() {
		try {
			var engName = (opt.TtsEngine ?? "").Trim();
			var wantEng = engName.Equals("WinRt", StringComparison.OrdinalIgnoreCase)
				|| engName.Equals("Windows", StringComparison.OrdinalIgnoreCase)
				|| engName.Equals("OneCore", StringComparison.OrdinalIgnoreCase)
				? TtsEngineKind.WinRt
				: engName.Equals("Sapi", StringComparison.OrdinalIgnoreCase)
					? TtsEngineKind.Sapi
					: TtsEngineKind.Sherpa;
			if (wantEng == TtsEngineKind.Sherpa && sherpaTts == null)
				wantEng = winRtTts != null && winRtTts.Voices.Count > 0 ? TtsEngineKind.WinRt : TtsEngineKind.Sapi;
			if (wantEng == TtsEngineKind.WinRt && (winRtTts == null || winRtTts.Voices.Count == 0))
				wantEng = TtsEngineKind.Sapi;
			foreach (ComboBoxItem it in ettsengine.Items) {
				if (it.Tag is TtsEngineKind k && k == wantEng) {
					ettsengine.SelectedItem = it;
					break;
				}
			}
			var wantComp = (opt.TtsCompute ?? "Auto").Trim().ToLowerInvariant() switch {
				"gpu" or "cuda" => TtsComputeMode.Gpu,
				"cpu" => TtsComputeMode.Cpu,
				"igpu" or "dml" or "directml" => TtsComputeMode.Igpu,
				_ => TtsComputeMode.Auto,
			};
			foreach (ComboBoxItem it in ettscompute.Items) {
				if (it.Tag is TtsComputeMode m && m == wantComp) {
					ettscompute.SelectedItem = it;
					break;
				}
			}
			// 先按引擎重填语言列表，再恢复选中
			rebuildttslangcombo(preserve: false);
			selectcombobytag(ettslang, TtsLang.Normalize(opt.TtsLangFilter));
			selectcombobytag(ettsgender, TtsGender.Normalize(opt.TtsGenderFilter));
			ettsrate.Value = Compat.Clamp(opt.TtsRate <= 0 ? 1.0 : opt.TtsRate, 0.5, 2.0);
			ettsvol.Value = Compat.Clamp(opt.TtsVolume, 0, 100);
			lbttsrate.Text = $"{ettsrate.Value:0.0}x";
			lbttsvol.Text = ((int)ettsvol.Value).ToString();
			var kbps = Compat.Clamp(opt.TtsKbps <= 0 ? 192 : opt.TtsKbps, 32, 320);
			foreach (ComboBoxItem it in ettskbps.Items) {
				if (it.Tag is int k && k == kbps) {
					ettskbps.SelectedItem = it;
					break;
				}
			}
		}
		catch (Exception ex) {
			CaptureLog.Ex("restorettsprefs", ex);
		}
	}

	/// <summary>
	/// 按当前引擎枚举可用语言，填入 ettslang（全部 + 各语言）。
	/// </summary>
	void rebuildttslangcombo(bool preserve) {
		var prev = "";
		if (preserve && ettslang.SelectedItem is ComboBoxItem cur && cur.Tag is string ps)
			prev = ps ?? "";
		if (string.IsNullOrEmpty(prev))
			prev = TtsLang.Normalize(opt.TtsLangFilter);

		var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		try {
			if (sapiTts != null) {
				foreach (var v in sapiTts.Voices) {
					var lg = TtsLang.Normalize(v.Culture?.TwoLetterISOLanguageName ?? v.Culture?.Name ?? "");
					if (!string.IsNullOrEmpty(lg)) set.Add(lg);
				}
			}
			// 缓存的 x86 音也可贡献语言筛选项（不在此拉起服务）
			foreach (var v in sapiX86VoicesCache ?? Enumerable.Empty<SapiVoiceItem>()) {
				var lg = TtsLang.Normalize(v.Lang);
				if (!string.IsNullOrEmpty(lg)) set.Add(lg);
			}
		}
		catch { }
		try {
			if (winRtTts != null) {
				foreach (var v in winRtTts.Voices) {
					var lg = TtsLang.Normalize(v.Lang);
					if (!string.IsNullOrEmpty(lg)) set.Add(lg);
				}
			}
		}
		catch { }
		try {
			foreach (var m in ttsModels ?? Enumerable.Empty<TtsModelInfo>()) {
				foreach (var p in (m.Lang ?? "").Split(new[] { ',', '/', '|', '+' }, StringSplitOptions.RemoveEmptyEntries)) {
					var lg = TtsLang.Normalize(p);
					if (!string.IsNullOrEmpty(lg)) set.Add(lg);
				}
				if (m.Speakers == null) continue;
				foreach (var s in m.Speakers) {
					var lg = TtsLang.Normalize(s.Lang);
					if (!string.IsNullOrEmpty(lg)) set.Add(lg);
				}
			}
		}
		catch { }

		// 无数据时给常见默认
		if (set.Count == 0) {
			set.Add(TtsLang.Zh);
			set.Add(TtsLang.En);
			set.Add(TtsLang.Vi);
		}

		var wasLoading = ttsUiLoading;
		ttsUiLoading = true;
		try {
			ettslang.Items.Clear();
			ettslang.Items.Add(new ComboBoxItem { Content = "全部语言", Tag = "" });
			foreach (var lg in set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
				ettslang.Items.Add(new ComboBoxItem { Content = TtsLang.DisplayName(lg), Tag = lg });
			selectcombobytag(ettslang, prev);
		}
		finally {
			ttsUiLoading = wasLoading;
		}
	}

	static void selectcombobytag(ComboBox cb, string tag) {
		tag ??= "";
		foreach (ComboBoxItem it in cb.Items) {
			if (it.Tag is string s && string.Equals(s, tag, StringComparison.OrdinalIgnoreCase)) {
				cb.SelectedItem = it;
				return;
			}
		}
		if (cb.Items.Count > 0) cb.SelectedIndex = 0;
	}

	void restorettsmodelandvoice() {
		ttsUiLoading = true;
		try {
			var eng = currentttsengine();
			if (eng == TtsEngineKind.Sherpa
				&& !string.IsNullOrEmpty(opt.TtsModel)
				&& ettsmodel.ItemsSource is System.Collections.IEnumerable src) {
				TtsModelInfo found = null;
				foreach (var o in src) {
					if (o is TtsModelInfo m && string.Equals(m.DisplayName, opt.TtsModel, StringComparison.OrdinalIgnoreCase)) {
						found = m;
						break;
					}
				}
				if (found != null)
					ettsmodel.SelectedItem = found;
				// 仅 Sherpa 填模型发音人；SAPI/WinRT 已在 applyttsengineui 填好，勿再 fillttsspeakers 清空
				fillttsspeakers();
			}
			else if (eng == TtsEngineKind.Sapi) {
				// 若列表仍空（init 顺序竞态），补填
				if (ettsvoice.Items.Count == 0)
					fillsapivoices();
			}
			else if (eng == TtsEngineKind.WinRt) {
				if (ettsvoice.Items.Count == 0)
					fillwinrtvoices();
			}
			restorettsvoice();
		}
		finally {
			ttsUiLoading = false;
		}
	}

	void restorettsvoice() {
		if (string.IsNullOrEmpty(opt.TtsVoice)) return;
		var want = opt.TtsVoice;
		var eng = currentttsengine();
		if (eng is TtsEngineKind.Sapi or TtsEngineKind.WinRt) {
			foreach (var item in ettsvoice.Items) {
				if (item is SapiVoiceItem wi
					&& (string.Equals(wi.Key, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(wi.Name, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals("sapi:" + wi.Name, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals("sapi-x86:" + wi.Name, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals("winrt:" + wi.Name, want, StringComparison.OrdinalIgnoreCase))) {
					ettsvoice.SelectedItem = item;
					return;
				}
			}
		}
		else {
			foreach (var item in ettsvoice.Items) {
				if (item is TtsSpeakerInfo sp
					&& (string.Equals(sp.Name, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(sp.ChineseName, want, StringComparison.OrdinalIgnoreCase)
						|| string.Equals("speaker" + sp.Id, want, StringComparison.OrdinalIgnoreCase))) {
					ettsvoice.SelectedItem = item;
					return;
				}
			}
		}
	}

	void savettsprefs() {
		try {
			opt.TtsEngine = currentttsengine() switch {
				TtsEngineKind.Sapi => "Sapi",
				TtsEngineKind.WinRt => "WinRt",
				_ => "Sherpa",
			};
			if (ettscompute.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode cm)
				opt.TtsCompute = cm.ToString();
			else
				opt.TtsCompute = "Auto";
			opt.TtsModel = ettsmodel.SelectedItem is TtsModelInfo mi ? mi.DisplayName : (opt.TtsModel ?? "");
			if (ettsvoice.SelectedItem is TtsSpeakerInfo sp)
				opt.TtsVoice = sp.Name;
			else if (ettsvoice.SelectedItem is SapiVoiceItem wi)
				opt.TtsVoice = wi.Key;
			else if (ettsvoice.SelectedItem is System.Speech.Synthesis.VoiceInfo vi)
				opt.TtsVoice = vi.Name;
			if (ettslang.SelectedItem is ComboBoxItem li && li.Tag is string ls)
				opt.TtsLangFilter = ls ?? "";
			if (ettsgender.SelectedItem is ComboBoxItem gi && gi.Tag is string gs)
				opt.TtsGenderFilter = gs ?? "";
			opt.TtsRate = ettsrate.Value;
			opt.TtsVolume = (int)ettsvol.Value;
			opt.TtsKbps = selectedttskbps();
			AppConfig.Save(opt);
		}
		catch (Exception ex) {
			CaptureLog.Ex("savettsprefs", ex);
		}
	}

	void updatettsctrlui() {
		try {
			bttspause.IsEnabled = ttsSession;
			bttsprev.IsEnabled = ttsSession;
			bttsnext.IsEnabled = ttsSession;
			bttspause.Content = ttsPaused ? "继续" : "暂停";
			bttsspeak.IsEnabled = !ttsSession || ttsPaused;
			bttsspeak.Content = ttsSession && ttsPaused ? "继续" : "朗读";
		}
		catch { }
	}

	TtsEngineKind currentttsengine() {
		if (ettsengine.SelectedItem is ComboBoxItem it && it.Tag is TtsEngineKind k)
			return k;
		return TtsEngineKind.Sapi;
	}

	void scanttssmodels() {
		try {
			ttsModels = TtsModelScanner.Scan();
			var root = TtsModelScanner.ResolveRoot();
			lbttshint.Text = ttsModels.Count > 0
				? $"模型根目录：{root} · 共 {ttsModels.Count} 个"
				: $"未找到模型。请将 VITS/Matcha 放到：{root}";
		}
		catch (Exception ex) {
			ttsModels = new List<TtsModelInfo>();
			lbttshint.Text = "扫描模型失败: " + ex.Message;
		}
	}

	void ttsfilterwant(out string wantLang, out string wantGender) {
		wantLang = "";
		wantGender = "";
		if (ettslang.SelectedItem is ComboBoxItem li && li.Tag is string ls)
			wantLang = ls ?? "";
		if (ettsgender.SelectedItem is ComboBoxItem gi && gi.Tag is string gs)
			wantGender = gs ?? "";
	}

	static bool modelmatchesfilter(TtsModelInfo m, string wantLang, string wantGender) {
		if (m == null) return false;
		var langOk = string.IsNullOrEmpty(wantLang) || TtsLang.Match(m.Lang, wantLang)
			|| (m.Speakers != null && m.Speakers.Any(s => TtsLang.Match(s.Lang, wantLang)));
		if (!langOk) return false;
		if (string.IsNullOrEmpty(wantGender)) return true;
		if (TtsGender.Match(m.Gender, wantGender)) return true;
		if (m.Speakers != null && m.Speakers.Any(s => TtsGender.Match(s.Gender, wantGender)))
			return true;
		if (string.IsNullOrEmpty(m.Gender)
			&& (m.Speakers == null || m.Speakers.All(s => string.IsNullOrEmpty(s.Gender))))
			return true;
		return false;
	}

	static bool speakermatches(TtsModelInfo m, TtsSpeakerInfo s, string wantLang, string wantGender) {
		if (s == null) return false;
		if (!string.IsNullOrEmpty(wantLang)) {
			var ok = TtsLang.Match(s.Lang, wantLang) || TtsLang.Match(m?.Lang ?? "", wantLang);
			if (!ok) return false;
		}
		if (!string.IsNullOrEmpty(wantGender)) {
			if (!string.IsNullOrEmpty(s.Gender) && !TtsGender.Match(s.Gender, wantGender))
				return false;
			if (string.IsNullOrEmpty(s.Gender) && !string.IsNullOrEmpty(m?.Gender)
				&& !TtsGender.Match(m.Gender, wantGender))
				return false;
		}
		return true;
	}

	void applyttsfilter() {
		var eng = currentttsengine();
		if (eng == TtsEngineKind.Sapi) {
			fillsapivoices();
			return;
		}
		if (eng == TtsEngineKind.WinRt) {
			fillwinrtvoices();
			return;
		}
		var prev = ettsmodel.SelectedItem as TtsModelInfo;
		ttsUiLoading = true;
		try {
			ttsfilterwant(out var wantLang, out var wantGender);
			var filtered = ttsModels.Where(m => modelmatchesfilter(m, wantLang, wantGender)).ToList();
			ettsmodel.DisplayMemberPath = "DisplayName";
			ettsmodel.ItemsSource = null;
			ettsmodel.ItemsSource = filtered;
			if (filtered.Count == 0)
				ettsmodel.SelectedItem = null;
			else if (prev != null && filtered.Contains(prev))
				ettsmodel.SelectedItem = prev;
			else
				ettsmodel.SelectedItem = filtered[0];
		}
		finally {
			ttsUiLoading = false;
		}
		fillttsspeakers();
	}

	void applyttsengineui() {
		ttsUiLoading = true;
		try {
			var eng = currentttsengine();
			var sherpa = eng == TtsEngineKind.Sherpa;
			ettscompute.IsEnabled = sherpa;
			ettsmodel.IsEnabled = sherpa;
			// SAPI / WinRT 用音量滑块；Sherpa 走 tts_config volume
			ettsvol.IsEnabled = !sherpa;
			if (!sherpa)
				ettsmodel.ItemsSource = null;
		}
		finally {
			ttsUiLoading = false;
		}

		var eng2 = currentttsengine();
		if (eng2 == TtsEngineKind.Sherpa) {
			applyttsfilter();
			var cuda = TtsEngine.ProbeCuda(out var cudaReason);
			TtsEngine.ProbeSherpaDml(out var dmlReason);
			var root = TtsModelScanner.ResolveRoot();
			if (ttsModels.Count == 0)
				lbttsstatus.Text = "Sherpa · 无模型 · " + root;
			else {
				var shown = (ettsmodel.ItemsSource as System.Collections.ICollection)?.Count ?? ettsmodel.Items.Count;
				if (cuda)
					lbttsstatus.Text = $"Sherpa · {shown}/{ttsModels.Count} 模型 · CUDA 可用";
				else
					lbttsstatus.Text =
						$"Sherpa · {shown}/{ttsModels.Count} 模型 · CUDA 不可用: {cudaReason} · 核显: {dmlReason}";
			}
		}
		else if (eng2 == TtsEngineKind.WinRt) {
			fillwinrtvoices();
			lbttsstatus.Text = winRtTts == null || winRtTts.Voices.Count == 0
				? "WinRT 语音不可用（需 Windows 10+ 并安装语言语音包）"
				: $"WinRT · {ettsvoice.Items.Count}/{winRtTts.Voices.Count} 个语音（OneCore/神经）";
		}
		else {
			// 先填本机 x64，再异步合并 x86 Web（按需启动）
			fillsapivoices();
			var n = ettsvoice.Items.Count;
			lbttsstatus.Text = sapiTts == null && n == 0
				? "SAPI 不可用"
				: $"SAPI · {n} 个语音" + (SapiX86Client.ExeAvailable ? " · 正在拉取 x86…" : "");
			if (SapiX86Client.ExeAvailable)
				_ = mergesapix86async();
		}
	}

	/// <summary>后台拉 x86 发音人并合并到下拉（不挡 UI）。</summary>
	async Task mergesapix86async() {
		List<SapiVoiceItem> x86 = null;
		string err = null;
		try {
			x86 = await Task.Run(() => SapiX86Client.ListVoices().ToList()).ConfigureAwait(true);
			sapiX86VoicesCache = x86 ?? new List<SapiVoiceItem>();
		}
		catch (Exception ex) {
			err = ex.Message;
			CaptureLog.Ex("SAPI x86 list", ex);
		}
		if (currentttsengine() != TtsEngineKind.Sapi) return;
		var prevKey = ettsvoice.SelectedItem is SapiVoiceItem cur ? cur.Key : null;
		fillsapivoices();
		if (!string.IsNullOrEmpty(prevKey)) {
			foreach (var item in ettsvoice.Items) {
				if (item is SapiVoiceItem wi && string.Equals(wi.Key, prevKey, StringComparison.OrdinalIgnoreCase)) {
					ettsvoice.SelectedItem = item;
					break;
				}
			}
		}
		var nX86 = sapiX86VoicesCache?.Count ?? 0;
		if (!string.IsNullOrEmpty(err))
			lbttsstatus.Text = $"SAPI · {ettsvoice.Items.Count} 个 · x86 失败: {err}";
		else
			lbttsstatus.Text = $"SAPI · {ettsvoice.Items.Count} 个语音（含 x86 Web {nX86}）";
	}

	void fillsapivoices() {
		ttsUiLoading = true;
		try {
			ettsvoice.Items.Clear();
			ettsvoice.DisplayMemberPath = "DisplayName";
			ttsfilterwant(out var wantLang, out var wantGender);
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			// 本机 x64 SAPI
			if (sapiTts != null) {
				foreach (var v in sapiTts.Voices) {
					if (!sapivoicematches(v, wantLang, wantGender)) continue;
					var name = v.Name ?? "";
					if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
					var culture = v.Culture?.Name ?? "";
					var lang = (v.Culture?.TwoLetterISOLanguageName ?? "").ToLowerInvariant();
					var gender = v.Gender switch {
						System.Speech.Synthesis.VoiceGender.Female => TtsGender.Female,
						System.Speech.Synthesis.VoiceGender.Male => TtsGender.Male,
						_ => "",
					};
					var gLabel = TtsGender.Label(gender);
					var tail = string.IsNullOrEmpty(culture) ? "" : " · " + culture;
					if (!string.IsNullOrEmpty(gLabel)) tail += " · " + gLabel;
					ettsvoice.Items.Add(new SapiVoiceItem {
						DisplayName = name + tail,
						Key = "sapi:" + name,
						Name = name,
						Culture = culture,
						Lang = lang,
						Gender = gender,
						Source = "sapi",
					});
				}
			}

			// x86 独有（同名优先本机）
			foreach (var v in sapiX86VoicesCache ?? Enumerable.Empty<SapiVoiceItem>()) {
				if (!winrtvoicematches(v, wantLang, wantGender)) continue;
				if (string.IsNullOrEmpty(v.Name) || seen.Contains(v.Name)) continue;
				seen.Add(v.Name);
				ettsvoice.Items.Add(v);
			}

			if (ettsvoice.Items.Count > 0) {
				SapiVoiceItem zh = null;
				foreach (SapiVoiceItem it in ettsvoice.Items) {
					if ((it.Culture ?? "").StartsWith("zh", StringComparison.OrdinalIgnoreCase)
						|| it.Lang == TtsLang.Zh
						|| Compat.Contains(it.Name ?? "", "Chinese", StringComparison.OrdinalIgnoreCase)) {
						zh = it;
						break;
					}
				}
				ettsvoice.SelectedItem = zh ?? ettsvoice.Items[0];
			}
		}
		finally {
			ttsUiLoading = false;
		}
	}

	void fillwinrtvoices() {
		ttsUiLoading = true;
		try {
			ettsvoice.Items.Clear();
			ettsvoice.DisplayMemberPath = "DisplayName";
			if (winRtTts == null) return;
			ttsfilterwant(out var wantLang, out var wantGender);
			foreach (var v in winRtTts.Voices) {
				if (!winrtvoicematches(v, wantLang, wantGender)) continue;
				ettsvoice.Items.Add(v);
			}
			if (ettsvoice.Items.Count > 0) {
				// 优先越南语，其次中文，否则第一项
				SapiVoiceItem pick = null;
				foreach (SapiVoiceItem v in ettsvoice.Items) {
					if (v.Lang == TtsLang.Vi) { pick = v; break; }
				}
				if (pick == null) {
					foreach (SapiVoiceItem v in ettsvoice.Items) {
						if (v.Lang == TtsLang.Zh) { pick = v; break; }
					}
				}
				ettsvoice.SelectedItem = pick ?? ettsvoice.Items[0];
				if (ettsvoice.SelectedItem is SapiVoiceItem sel)
					winRtTts.SelectVoice(sel.Key);
			}
		}
		finally {
			ttsUiLoading = false;
		}
	}

	static bool winrtvoicematches(SapiVoiceItem v, string wantLang, string wantGender) {
		if (v == null) return false;
		if (!string.IsNullOrEmpty(wantLang) && !TtsLang.Match(v.Lang, wantLang)
			&& !TtsLang.Match(v.Culture, wantLang))
			return false;
		if (!string.IsNullOrEmpty(wantGender) && !string.IsNullOrEmpty(v.Gender)
			&& !TtsGender.Match(v.Gender, wantGender))
			return false;
		return true;
	}

	static bool sapivoicematches(System.Speech.Synthesis.VoiceInfo v, string wantLang, string wantGender) {
		if (!string.IsNullOrEmpty(wantLang)) {
			var cul = (v.Culture?.TwoLetterISOLanguageName ?? v.Culture?.Name ?? "").ToLowerInvariant();
			var name = v.Name ?? "";
			var ok = wantLang == TtsLang.Zh
				? cul.StartsWith("zh") || Compat.Contains(name, "Chinese", StringComparison.OrdinalIgnoreCase)
					|| Compat.Contains(name, "中文", StringComparison.Ordinal)
				: wantLang == TtsLang.En
					? cul.StartsWith("en") || Compat.Contains(name, "English", StringComparison.OrdinalIgnoreCase)
					: true;
			if (!ok) return false;
		}
		if (!string.IsNullOrEmpty(wantGender)) {
			var g = v.Gender;
			var ok = wantGender == TtsGender.Female
				? g == System.Speech.Synthesis.VoiceGender.Female
				: wantGender == TtsGender.Male
					? g == System.Speech.Synthesis.VoiceGender.Male
					: true;
			if (!ok) return false;
		}
		return true;
	}

	void fillttsspeakers() {
		ttsUiLoading = true;
		try {
			ettsvoice.Items.Clear();
			ettsvoice.DisplayMemberPath = "DisplayName";
			if (ettsmodel.SelectedItem is not TtsModelInfo m) return;
			ttsfilterwant(out var wantLang, out var wantGender);
			IEnumerable<TtsSpeakerInfo> q = m.Speakers;
			if (!string.IsNullOrEmpty(wantLang) || !string.IsNullOrEmpty(wantGender))
				q = m.Speakers.Where(s => speakermatches(m, s, wantLang, wantGender));
			foreach (var s in q)
				ettsvoice.Items.Add(s);
			if (ettsvoice.Items.Count > 0)
				ettsvoice.SelectedIndex = 0;
		}
		finally {
			ttsUiLoading = false;
		}
	}

	void ttsstop() {
		ttsSkipDelta = 0;
		ttsPaused = false;
		try { ttsSpeakCts?.Cancel(); } catch { }
		try { sapiTts?.Stop(); } catch { }
		try { ttsPlayer?.Stop(); } catch { }
		ttsSession = false;
		ttsSegs = null;
		clearttshighlight();
		try { ettstext.IsReadOnly = false; } catch { }
		lbttsstatus.Text = "已停止";
		updatettsctrlui();
	}

	void ttstogglepause() {
		if (!ttsSession) return;
		if (ttsPaused) {
			ttsPaused = false;
			try { ttsPlayer?.Resume(); } catch { }
			lbttsstatus.Text = $"继续 · {ttsSegIndex + 1}/{(ttsSegs?.Count ?? 0)}";
		}
		else {
			ttsPaused = true;
			try { ttsPlayer?.Pause(); } catch { }
			lbttsstatus.Text = $"已暂停 · {ttsSegIndex + 1}/{(ttsSegs?.Count ?? 0)}";
		}
		updatettsctrlui();
	}

	void ttsjump(int delta) {
		if (!ttsSession || ttsSegs == null || ttsSegs.Count == 0) return;
		if (ttsPaused) {
			ttsPaused = false;
			try { ttsPlayer?.Resume(); } catch { }
		}
		ttsSkipDelta = delta;
		try { ttsPlayer?.StopSegment(); } catch { }
		updatettsctrlui();
	}

	static string formatms(int ms) {
		if (ms < 1000) return $"{ms}ms";
		return $"{ms / 1000.0:0.00}s";
	}

	void highlightttssegment(TtsSegment seg, string uiText) {
		if (seg == null || string.IsNullOrEmpty(uiText)) return;
		var (uiStart, uiEnd) = TtsTextSplitter.MapToUiOffsets(uiText, seg.Start, seg.End);
		if (uiEnd <= uiStart) return;
		try {
			ettstext.Focus();
			ettstext.Select(uiStart, uiEnd - uiStart);
			scrollttshighlighttotop(uiStart, uiEnd);
		}
		catch { }
	}

	void clearttshighlight() {
		try {
			var c = ettstext.CaretIndex;
			ettstext.Select(c, 0);
		}
		catch { }
	}

	void scrollttshighlighttotop(int uiStart, int uiEnd) {
		try {
			ettstext.UpdateLayout();
			var r0 = ettstext.GetRectFromCharacterIndex(uiStart);
			if (r0 == Rect.Empty) return;
			var last = Math.Max(uiStart, uiEnd - 1);
			var r1 = ettstext.GetRectFromCharacterIndex(last, true);
			var top = r0.Top;
			var bottom = r1 == Rect.Empty ? r0.Bottom : Math.Max(r0.Bottom, r1.Bottom);
			var viewH = ettstext.ViewportHeight;
			if (viewH <= 1) viewH = ettstext.ActualHeight;
			const double pad = 2;
			if (top >= -pad && bottom <= viewH + pad) return;
			var next = ettstext.VerticalOffset + top;
			if (next < 0) next = 0;
			ettstext.ScrollToVerticalOffset(next);
		}
		catch { }
	}

	async Task ttsspeakasync() {
		if (ttsSession && ttsPaused) {
			ttstogglepause();
			return;
		}
		if (ttsSession && !ttsPaused) return;

		// Sherpa 需要运行库 + ttsmodels
		if (currentttsengine() == TtsEngineKind.Sherpa) {
			if (!FeaturePrompt.EnsureSherpa(this)) {
				lbttsstatus.Text = "未安装 Sherpa 运行库";
				return;
			}
			if (ettsmodel.SelectedItem is not TtsModelInfo && !FeaturePrompt.EnsureTtsModels(this)) {
				lbttsstatus.Text = "未安装发音人模型";
				return;
			}
			if (ettsmodel.SelectedItem is not TtsModelInfo) {
				try { scanttssmodels(); } catch { }
				if (ettsmodel.SelectedItem is not TtsModelInfo) {
					lbttsstatus.Text = "请选择模型";
					return;
				}
			}
		}

		var uiText = ettstext.Text ?? "";
		var segments = TtsTextSplitter.Split(uiText);
		if (segments.Count == 0) {
			lbttsstatus.Text = "请输入要朗读的文本";
			return;
		}
		try { ttsSpeakCts?.Cancel(); } catch { }
		ttsSpeakCts = new CancellationTokenSource();
		var ct = ttsSpeakCts.Token;
		ttsSegs = segments;
		ttsUiText = uiText;
		ttsSegIndex = 0;
		ttsSkipDelta = 0;
		ttsPaused = false;
		ttsSession = true;
		updatettsctrlui();

		var eng = currentttsengine();
		var t0 = Environment.TickCount;
		SapiVoiceItem sapiItem = ettsvoice.SelectedItem as SapiVoiceItem;
		var useX86Sapi = eng == TtsEngineKind.Sapi && sapiItem != null && sapiItem.Source == "sapi-x86";
		string sapiVoice = eng == TtsEngineKind.Sapi && sapiItem != null && sapiItem.Source != "sapi-x86"
			? sapiItem.Name : null;
		string winRtKey = eng == TtsEngineKind.WinRt && sapiItem != null ? sapiItem.Key : null;
		var rateUi = ettsrate.Value;
		var volUi = (int)ettsvol.Value;
		var sapiRate = (int)Math.Round((rateUi - 1.0) * 10);
		var model = ettsmodel.SelectedItem as TtsModelInfo;
		var sid = ettsvoice.SelectedItem is TtsSpeakerInfo sp ? sp.Id : 0;
		var compute = TtsComputeMode.Auto;
		if (ettscompute.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode m)
			compute = m;

		ettstext.IsReadOnly = true;
		var loadMs = 0;
		var synthMs = 0;
		string provider = "";
		string fallback = null;
		try {
			savettsprefs();
			if (eng == TtsEngineKind.Sapi) {
				if (useX86Sapi) {
					if (!SapiX86Client.ExeAvailable) { lbttsstatus.Text = "无 x86host.exe，无法用 x86 音"; return; }
				}
				else {
					if (sapiTts == null) { lbttsstatus.Text = "SAPI 不可用"; return; }
					if (!string.IsNullOrEmpty(sapiVoice))
						sapiTts.SelectVoice(sapiVoice);
					sapiTts.Rate = sapiRate;
					sapiTts.Volume = volUi;
				}
			}
			else if (eng == TtsEngineKind.WinRt) {
				if (winRtTts == null) { lbttsstatus.Text = "WinRT 语音不可用"; return; }
				if (!string.IsNullOrEmpty(winRtKey))
					winRtTts.SelectVoice(winRtKey);
				winRtTts.SetRateVolume(rateUi, volUi);
			}
			else {
				if (sherpaTts == null) { lbttsstatus.Text = "Sherpa 引擎不可用"; return; }
				if (model == null) { lbttsstatus.Text = "请选择模型"; return; }
				lbttsstatus.Text = "加载模型…";
				await Task.Run(() => {
					var tLoad = Environment.TickCount;
					sherpaTts.Mode = compute;
					sherpaTts.LoadModel(model);
					loadMs = Math.Max(0, Environment.TickCount - tLoad);
					provider = sherpaTts.Provider;
					fallback = sherpaTts.GpuFallbackReason;
					if (!string.IsNullOrEmpty(fallback))
						CaptureLog.Info("TTS GPU fallback: " + fallback);
				}, ct).ConfigureAwait(true);
			}

			// ① 先合成全部段落（不播放）
			var parts = new List<(TtsSegment seg, float[] samples, int sr)>(segments.Count);
			for (var si = 0; si < segments.Count; si++) {
				ct.ThrowIfCancellationRequested();
				ttsSegIndex = si;
				var seg = segments[si];
				highlightttssegment(seg, uiText);
				lbttsstatus.Text = eng switch {
					TtsEngineKind.Sapi => useX86Sapi
						? $"SAPI x86 合成 {si + 1}/{segments.Count}…"
						: $"SAPI 合成 {si + 1}/{segments.Count}…",
					TtsEngineKind.WinRt => $"WinRT 合成 {si + 1}/{segments.Count}…",
					_ => $"Sherpa 合成 {si + 1}/{segments.Count}…",
				};
				updatettsctrlui();

				float[] samples = null;
				var sr = 22050;
				var tSyn = Environment.TickCount;
				if (eng == TtsEngineKind.WinRt) {
					(samples, sr) = await winRtTts.Synthesize(seg.Text).ConfigureAwait(true);
				}
				else {
					await Task.Run(() => {
						if (eng == TtsEngineKind.Sapi) {
							if (useX86Sapi) {
								(samples, sr) = SapiX86Client.SynthToFloat(
									seg.Text, sapiItem.Name, sapiRate, volUi);
							}
							else {
								var wav = TmpStore.NewPath("tts_play", ".wav");
								try {
									sapiTts.ExportWav(seg.Text, wav);
									using var reader = new NAudio.Wave.AudioFileReader(wav);
									sr = reader.WaveFormat.SampleRate;
									var list = new List<float>();
									var buf = new float[4096];
									int n;
									while ((n = reader.Read(buf, 0, buf.Length)) > 0) {
										for (int k = 0; k < n; k++) list.Add(buf[k]);
									}
									samples = list.ToArray();
								}
								finally {
									try { if (File.Exists(wav)) File.Delete(wav); } catch { }
								}
							}
						}
						else {
							(samples, sr) = sherpaTts.Synthesize(seg.Text, sid, (float)rateUi);
						}
					}, ct).ConfigureAwait(true);
				}
				synthMs += Math.Max(0, Environment.TickCount - tSyn);
				if (samples != null && samples.Length > 0)
					parts.Add((seg, samples, sr));
			}

			// ② 合成一结束立刻显示完成统计（不等播放）
			var totalMs = Math.Max(0, Environment.TickCount - t0);
			var tip = string.IsNullOrEmpty(fallback) ? "" : " · GPU回退: " + fallback;
			var doneText = eng switch {
				TtsEngineKind.Sapi => (useX86Sapi ? "SAPI x86" : "SAPI")
					+ $" 完成 · {parts.Count} 段 · 合成 {formatms(synthMs)} · 合计 {formatms(totalMs)}",
				TtsEngineKind.WinRt => $"WinRT 完成 · {parts.Count} 段 · 合成 {formatms(synthMs)} · 合计 {formatms(totalMs)}",
				_ => $"Sherpa 完成 · {parts.Count} 段 · {provider} · 加载 {formatms(loadMs)} · 合成 {formatms(synthMs)} · 合计 {formatms(totalMs)}{tip}",
			};
			lbttsstatus.Text = doneText;
			updatettsctrlui();

			// ③ 再顺序播放；状态保持「完成」，仅高亮当前段
			var i = 0;
			while (i < parts.Count) {
				ct.ThrowIfCancellationRequested();
				while (ttsPaused && !ct.IsCancellationRequested)
					await Task.Delay(50, ct).ConfigureAwait(true);
				ct.ThrowIfCancellationRequested();

				ttsSegIndex = i;
				var (seg, samples, sr) = parts[i];
				// 保持完成文案，附带播放进度
				lbttsstatus.Text = $"{doneText} · 播放 {i + 1}/{parts.Count}";
				highlightttssegment(seg, uiText);
				updatettsctrlui();
				try {
					await ttsPlayer.PlayAsync(samples, sr, ct).ConfigureAwait(true);
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception ex) {
					CaptureLog.Ex("tts play segment", ex);
				}

				if (ttsSkipDelta != 0) {
					var next = i + ttsSkipDelta;
					ttsSkipDelta = 0;
					i = Compat.Clamp(next, 0, parts.Count - 1);
					if (next >= parts.Count) break;
					continue;
				}
				i++;
			}
			// 播放结束：去掉「播放 n/n」后缀，只留完成统计
			if (!ct.IsCancellationRequested)
				lbttsstatus.Text = doneText;
		}
		catch (OperationCanceledException) {
			if (lbttsstatus.Text != "已停止")
				lbttsstatus.Text = "已停止";
		}
		catch (Exception ex) {
			var ms = Math.Max(0, Environment.TickCount - t0);
			lbttsstatus.Text = $"朗读失败 ({formatms(ms)}): {ex.Message}";
			MessageBox.Show(this, ex.Message, "语音合成", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		finally {
			ttsSession = false;
			ttsPaused = false;
			ttsSkipDelta = 0;
			clearttshighlight();
			ettstext.IsReadOnly = false;
			updatettsctrlui();
		}
	}

	int selectedttskbps() {
		if (ettskbps.SelectedItem is ComboBoxItem it && it.Tag is int k)
			return Compat.Clamp(k, 32, 320);
		return 192;
	}

	async Task ttsexportasync() {
		var uiText = ettstext.Text ?? "";
		var segments = TtsTextSplitter.Split(uiText);
		if (segments.Count == 0) {
			lbttsstatus.Text = "请输入要导出的文本";
			return;
		}
		var sfd = new Microsoft.Win32.SaveFileDialog {
			Title = "导出 MP3",
			Filter = "MP3 音频|*.mp3",
			FileName = $"tts_{DateTime.Now:yyyyMMdd_HHmmss}.mp3",
			DefaultExt = ".mp3",
			AddExtension = true,
			OverwritePrompt = true,
		};
		if (sfd.ShowDialog(this) != true) return;
		var path = sfd.FileName;
		var eng = currentttsengine();
		var rateUi = ettsrate.Value;
		var volUi = (int)ettsvol.Value;
		var kbps = selectedttskbps();
		SapiVoiceItem sapiItem = ettsvoice.SelectedItem as SapiVoiceItem;
		var useX86Sapi = eng == TtsEngineKind.Sapi && sapiItem != null && sapiItem.Source == "sapi-x86";
		string sapiVoice = eng == TtsEngineKind.Sapi && sapiItem != null && sapiItem.Source != "sapi-x86"
			? sapiItem.Name : null;
		string winRtKey = eng == TtsEngineKind.WinRt && sapiItem != null ? sapiItem.Key : null;
		var sapiRate = (int)Math.Round((rateUi - 1.0) * 10);
		var model = ettsmodel.SelectedItem as TtsModelInfo;
		var sid = ettsvoice.SelectedItem is TtsSpeakerInfo sp ? sp.Id : 0;
		var compute = TtsComputeMode.Auto;
		if (ettscompute.SelectedItem is ComboBoxItem ci && ci.Tag is TtsComputeMode m)
			compute = m;
		var segs = segments;
		var totalChars = segs.Sum(s => s.Text?.Length ?? 0);

		var t0 = Environment.TickCount;
		TtsExportProgressWindow progDlg = null;
		try {
			if (ttsSession) ttsstop();
			lbttsstatus.Text = $"分段合成导出 MP3（{segs.Count} 段 · {kbps} kbps）…";
			bttsexport.IsEnabled = false;
			bttsspeak.IsEnabled = false;

			progDlg = new TtsExportProgressWindow { Owner = this };
			progDlg.Show();
			var ct = progDlg.Token;
			progDlg.Report("prepare", 0, segs.Count, 0, totalChars);

			string outPath = null;
			var loadMs = 0;
			var synthMs = 0;

			await Task.Run(async () => {
				var parts = new List<float[]>(segs.Count);
				var sr = 22050;
				var doneChars = 0;
				void throwif() => ct.ThrowIfCancellationRequested();

				if (eng == TtsEngineKind.Sapi) {
					if (useX86Sapi) {
						if (!SapiX86Client.ExeAvailable)
							throw new InvalidOperationException("无 x86host.exe");
					}
					else {
						if (sapiTts == null) throw new InvalidOperationException("SAPI 不可用");
						if (!string.IsNullOrEmpty(sapiVoice))
							sapiTts.SelectVoice(sapiVoice);
						sapiTts.Rate = sapiRate;
						sapiTts.Volume = volUi;
					}
					var tSynAll = Environment.TickCount;
					for (var i = 0; i < segs.Count; i++) {
						throwif();
						progDlg.Report("synth", i, segs.Count, doneChars, totalChars);
						if (useX86Sapi) {
							var (samples, srate) = SapiX86Client.SynthToFloat(
								segs[i].Text, sapiItem.Name, sapiRate, volUi);
							sr = srate;
							if (samples != null && samples.Length > 0) parts.Add(samples);
						}
						else {
							var wav = TmpStore.NewPath("tts_exp", ".wav");
							try {
								sapiTts.ExportWav(segs[i].Text, wav);
								throwif();
								using var reader = new NAudio.Wave.AudioFileReader(wav);
								sr = reader.WaveFormat.SampleRate;
								var list = new List<float>();
								var buf = new float[4096];
								int n;
								while ((n = reader.Read(buf, 0, buf.Length)) > 0) {
									for (int k = 0; k < n; k++) list.Add(buf[k]);
								}
								if (list.Count > 0) parts.Add(list.ToArray());
							}
							finally {
								try { if (File.Exists(wav)) File.Delete(wav); } catch { }
							}
						}
						doneChars += segs[i].Text?.Length ?? 0;
						progDlg.Report("synth", i + 1, segs.Count, doneChars, totalChars);
					}
					synthMs = Math.Max(0, Environment.TickCount - tSynAll);
				}
				else if (eng == TtsEngineKind.WinRt) {
					if (winRtTts == null) throw new InvalidOperationException("WinRT 语音不可用");
					if (!string.IsNullOrEmpty(winRtKey))
						winRtTts.SelectVoice(winRtKey);
					winRtTts.SetRateVolume(rateUi, volUi);
					var tSynAll = Environment.TickCount;
					for (var i = 0; i < segs.Count; i++) {
						throwif();
						progDlg.Report("synth", i, segs.Count, doneChars, totalChars);
						var (samples, srate) = await winRtTts.Synthesize(segs[i].Text).ConfigureAwait(false);
						throwif();
						sr = srate;
						if (samples != null && samples.Length > 0)
							parts.Add(samples);
						doneChars += segs[i].Text?.Length ?? 0;
						progDlg.Report("synth", i + 1, segs.Count, doneChars, totalChars);
					}
					synthMs = Math.Max(0, Environment.TickCount - tSynAll);
				}
				else {
					if (sherpaTts == null) throw new InvalidOperationException("Sherpa 不可用");
					if (model == null) throw new InvalidOperationException("请选择模型");
					progDlg.Report("prepare", 0, segs.Count, 0, totalChars);
					var tLoad = Environment.TickCount;
					sherpaTts.Mode = compute;
					sherpaTts.LoadModel(model);
					loadMs = Math.Max(0, Environment.TickCount - tLoad);
					throwif();
					var speed = (float)rateUi;
					var tSynAll = Environment.TickCount;
					for (var i = 0; i < segs.Count; i++) {
						throwif();
						progDlg.Report("synth", i, segs.Count, doneChars, totalChars);
						var (samples, srate) = sherpaTts.Synthesize(segs[i].Text, sid, speed);
						throwif();
						sr = srate;
						if (samples != null && samples.Length > 0)
							parts.Add(samples);
						doneChars += segs[i].Text?.Length ?? 0;
						progDlg.Report("synth", i + 1, segs.Count, doneChars, totalChars);
					}
					synthMs = Math.Max(0, Environment.TickCount - tSynAll);
				}

				throwif();
				if (parts.Count == 0)
					throw new InvalidOperationException("合成失败：无有效音频段");
				progDlg.Report("merge", segs.Count, segs.Count, totalChars, totalChars);
				var merged = TtsPlayer.Concat(parts, sr, gapSec: 0.12f);
				throwif();
				progDlg.Report("encode", segs.Count, segs.Count, totalChars, totalChars);
				var wavOut = TmpStore.NewPath("tts_merge", ".wav");
				try {
					TtsPlayer.SaveWav(wavOut, merged, sr);
					throwif();
					outPath = SapiTts.ConvertWavToMp3(wavOut, path, kbps);
				}
				finally {
					try { if (File.Exists(wavOut)) File.Delete(wavOut); } catch { }
				}
				if (ct.IsCancellationRequested && !string.IsNullOrEmpty(outPath)) {
					try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
					throw new OperationCanceledException(ct);
				}
			}, ct).ConfigureAwait(true);

			progDlg.Report("done", segs.Count, segs.Count, totalChars, totalChars);
			var totalMs = Math.Max(0, Environment.TickCount - t0);
			var timePart = eng == TtsEngineKind.Sherpa
				? $"加载 {formatms(loadMs)} · 合成 {formatms(synthMs)} · 合计 {formatms(totalMs)}"
				: $"合成 {formatms(synthMs)} · 合计 {formatms(totalMs)}";
			var isMp3 = string.Equals(Path.GetExtension(outPath), ".mp3", StringComparison.OrdinalIgnoreCase);
			lbttsstatus.Text = isMp3
				? $"已导出 MP3 {kbps}k · {segs.Count} 段 · {timePart} · {outPath}"
				: $"已导出（MP3 转换失败，已存 WAV）· {segs.Count} 段 · {timePart} · {outPath}";
			setstatus(lbttsstatus.Text);
			progDlg.ForceClose();
			progDlg = null;
			MessageBox.Show(this,
				(isMp3 ? $"已导出 MP3（{kbps} kbps，{segs.Count} 段）：\n" : "已保存（未能转 MP3，输出为 WAV）：\n")
				+ outPath + "\n\n" + timePart,
				"语音合成", MessageBoxButton.OK,
				isMp3 ? MessageBoxImage.Information : MessageBoxImage.Warning);
		}
		catch (OperationCanceledException) {
			lbttsstatus.Text = "已取消合成";
			try {
				if (File.Exists(path) && new FileInfo(path).Length < 200)
					File.Delete(path);
			}
			catch { }
		}
		catch (Exception ex) {
			var ms = Math.Max(0, Environment.TickCount - t0);
			if (ex is AggregateException ae && ae.InnerException != null)
				ex = ae.InnerException;
			if (ex is OperationCanceledException)
				lbttsstatus.Text = "已取消合成";
			else {
				lbttsstatus.Text = $"导出失败 ({formatms(ms)}): {ex.Message}";
				MessageBox.Show(this, ex.Message, "导出音频", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
		finally {
			try { progDlg?.ForceClose(); } catch { }
			bttsexport.IsEnabled = true;
			bttsspeak.IsEnabled = true;
			updatettsctrlui();
		}
	}

	void disposeTts() {
		try { ttsstop(); } catch { }
		try { sapiTts?.Dispose(); } catch { }
		try { winRtTts?.Dispose(); } catch { }
		try { sherpaTts?.Dispose(); } catch { }
		try { ttsPlayer?.Dispose(); } catch { }
	}
}
