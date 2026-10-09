using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ScreenKit;

partial class InstallFeaturesWindow {
	readonly ObservableCollection<WinTtsRow> winRows = new();
	readonly List<WinTtsRow> winAll = new();
	Dictionary<string, string> winStates;
	List<SapiVoiceItem> winVoices = new();
	bool winLoaded;
	bool winUiLoading;
	bool winQuerying;
	int winGen;

	async Task loadwin(bool force) {
		if (busy || winQuerying) return;
		if (winLoaded && !force) return;
		var local = new CancellationTokenSource();
		cts = local;
		var gen = ++winGen;
		winQuerying = true;
		if (force) winStates = null;
		applytabbuttons();
		setstatus(Loc.T("inst.win.query"));
		appendlog(Loc.T("inst.win.query"));
		// 先画出语言目录。已装状态和发音人只在进入本页后读取。
		refill();
		string qerr = null;
		var uac = false;
		try {
			var token = local.Token;
			if (dismrunning()) {
				appendlog(Loc.T("inst.win.dismbusy"));
				setstatus(Loc.T("inst.win.dismbusy"));
				while (dismrunning()) {
					await Task.Delay(2000, token).ConfigureAwait(true);
				}
			}
			if (WinTtsPack.IsAdmin()) {
				startvoices(gen);
				winStates = await Task.Run(() => WinTtsPack.QueryStates(token)).ConfigureAwait(true);
			}
			else {
				appendlog(Loc.T("inst.win.query.elevate"));
				setstatus(Loc.T("inst.win.query.elevate"));
				var q = await Task.Run(() => WinTtsPack.QueryElevated(token, null)).ConfigureAwait(true);
				if (q.UacDenied) {
					uac = true;
					appendlog(Loc.T("inst.win.uac"));
					setstatus(Loc.T("inst.win.uac"));
					startvoices(gen);
				}
				else {
					winStates = q.States.Count > 0 ? q.States : null;
					if (q.Voices.Count > 0) {
						var list = new List<SapiVoiceItem>();
						foreach (var v in q.Voices) {
							list.Add(new SapiVoiceItem {
								Culture = v.Culture,
								DisplayName = v.Name,
								Name = v.Name,
								Source = "winrt",
							});
						}
						winVoices = list;
					}
					else startvoices(gen);
					if (q.VoiceError.Length > 0) appendlog(q.VoiceError);
					if (winStates == null) qerr = q.Error.Length > 0 ? q.Error : "empty";
				}
			}
		}
		catch (OperationCanceledException) {
			appendlog(Loc.T("inst.log.cancel"));
			setstatus(Loc.T("inst.log.cancel"));
			return;
		}
		catch (Exception ex) {
			qerr = ex.Message;
			winStates = null;
			CaptureLog.Ex("win tts query", ex);
		}
		finally {
			winQuerying = false;
			if (ReferenceEquals(cts, local)) cts = null;
			try { local.Dispose(); } catch { }
			applytabbuttons();
		}
		refill();
		winLoaded = true;
		if (uac) return;
		if (!string.IsNullOrEmpty(qerr)) {
			appendlog(Loc.T("inst.win.queryfail", qerr));
			setstatus(Loc.T("inst.win.queryfail", qerr));
		}
		else {
			var ninst = winAll.Count(r => r.Installed);
			setstatus(Loc.T("inst.win.ready", winAll.Count, ninst));
			appendlog(Loc.T("inst.win.ready", winAll.Count, ninst));
		}
	}

	static bool dismrunning() {
		try { return Process.GetProcessesByName("dism").Length > 0; }
		catch { return false; }
	}

	void startvoices(int gen) {
		var th = new Thread(() => {
			List<SapiVoiceItem> list = null;
			try {
				using var tts = new WinRtTts();
				list = tts.Voices.ToList();
			}
			catch (Exception ex) {
				CaptureLog.Ex("win voices", ex);
			}
			if (list == null || gen != winGen) return;
			try {
				Dispatcher.BeginInvoke(new Action(() => {
					try {
						if (gen != winGen) return;
						winVoices = list;
						refill();
					}
					catch (Exception ex) { CaptureLog.Ex("win voices ui", ex); }
				}));
			}
			catch { }
		});
		th.SetApartmentState(ApartmentState.STA);
		th.IsBackground = true;
		th.Start();
	}

	void refill() {
		var sel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var r in winAll)
			if (r.Selected) sel.Add(r.Culture);
		var only = cwinonly != null && cwinonly.IsChecked == true;
		fillwin(winStates, winVoices);
		winUiLoading = true;
		foreach (var r in winAll)
			if (sel.Contains(r.Culture)) r.Selected = true;
		winUiLoading = false;
		if (only) applywinfilter();
		else refreshwincmd();
	}

	void fillwin(Dictionary<string, string> states, List<SapiVoiceItem> voices) {
		var voiceText = voicetext(voices);
		var cultures = new List<string>(WinTtsPack.Cultures);
		addcultures(cultures, states?.Keys);
		addcultures(cultures, voiceText.Keys);
		winUiLoading = true;
		foreach (var old in winAll) old.PropertyChanged -= onwinrow;
		winAll.Clear();
		foreach (var culture in cultures) {
			var st = rowstate(states, culture);
			var row = new WinTtsRow(culture, st, voiceText.TryGetValue(culture, out var names) ? names : "");
			row.PropertyChanged += onwinrow;
			winAll.Add(row);
		}
		winAll.Sort((a, b) => {
			var c = b.Installed.CompareTo(a.Installed);
			if (c != 0) return c;
			c = b.HasVoice.CompareTo(a.HasVoice);
			if (c != 0) return c;
			return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
		});
		winUiLoading = false;
		applywinfilter();
	}

	void onwinrow(object sender, PropertyChangedEventArgs e) {
		if (winUiLoading) return;
		if (e.PropertyName == nameof(WinTtsRow.Selected)) refreshwincmd();
	}

	static void addcultures(List<string> cultures, IEnumerable<string> extra) {
		if (extra == null) return;
		foreach (var c in extra) {
			if (string.IsNullOrWhiteSpace(c)) continue;
			var hit = false;
			foreach (var have in cultures) {
				if (string.Equals(have, c, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
			}
			if (!hit) cultures.Add(c.Trim());
		}
	}

	static Dictionary<string, string> voicetext(List<SapiVoiceItem> voices) {
		var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		if (voices == null) return new Dictionary<string, string>();
		foreach (var v in voices) {
			var culture = (v.Culture ?? "").Trim();
			if (culture.Length == 0) continue;
			if (!map.TryGetValue(culture, out var list)) {
				list = new List<string>();
				map[culture] = list;
			}
			var name = v.DisplayName ?? "";
			var cut = name.IndexOf(" · ", StringComparison.Ordinal);
			if (cut > 0) name = name.Substring(0, cut);
			if (name.Length == 0 || list.Contains(name)) continue;
			list.Add(name);
		}
		var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var kv in map)
			text[kv.Key] = string.Join(", ", kv.Value);
		return text;
	}

	static WinPackState rowstate(Dictionary<string, string> states, string culture) {
		if (states == null) return WinPackState.Unknown;
		if (!states.TryGetValue(culture, out var raw)) return WinPackState.Missing;
		if (string.Equals(raw, "Installed", StringComparison.OrdinalIgnoreCase)) return WinPackState.Installed;
		if (string.Equals(raw, "NotPresent", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(raw, "Removed", StringComparison.OrdinalIgnoreCase))
			return WinPackState.Missing;
		return WinPackState.Unknown;
	}

	void applywinfilter() {
		winUiLoading = true;
		winRows.Clear();
		var only = cwinonly.IsChecked == true;
		foreach (var r in winAll) {
			if (only && !r.Installed) continue;
			winRows.Add(r);
		}
		cwinheader.IsChecked = false;
		winUiLoading = false;
		refreshwincmd();
	}

	void setwincheck(bool on) {
		winUiLoading = true;
		foreach (var r in winRows) r.Selected = on;
		winUiLoading = false;
		refreshwincmd();
	}

	void refreshwincmd() {
		var sel = winAll.Where(r => r.Selected).ToList();
		if (sel.Count == 0) {
			ewincmd.Text = "";
			return;
		}
		var sb = new StringBuilder();
		foreach (var r in sel)
			sb.AppendLine(r.AddCmd);
		sb.AppendLine();
		foreach (var r in sel)
			sb.AppendLine(r.RemoveCmd);
		ewincmd.Text = sb.ToString().TrimEnd();
	}

	void copywincmd() {
		var text = ewincmd.Text ?? "";
		if (text.Length == 0) {
			setstatus(Loc.T("inst.win.none"));
			return;
		}
		try {
			Clipboard.SetText(text);
			setstatus(Loc.T("inst.win.copied"));
		}
		catch (Exception ex) {
			appendlog(ex.Message);
		}
	}

	async Task runwin(bool install) {
		if (busy) return;
		var rows = winAll.Where(r => r.Selected && (install
			? r.State != WinPackState.Installed
			: r.State != WinPackState.Missing)).ToList();
		if (rows.Count == 0) {
			MessageBox.Show(this, Loc.T(install ? "inst.win.none.add" : "inst.win.none.del"), Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var names = string.Join("\n· ", rows.Select(r => r.Title));
		var ask = Loc.T(install ? "inst.win.confirm.add" : "inst.win.confirm.del", names);
		if (MessageBox.Show(this, ask, Title,
				MessageBoxButton.YesNo, install ? MessageBoxImage.Question : MessageBoxImage.Warning)
			!= MessageBoxResult.Yes)
			return;
		var admin = WinTtsPack.IsAdmin();
		cts = new CancellationTokenSource();
		var log = new Progress<string>(line => onwinlog(line, rows));
		setbusy(true);
		setprogress(0);
		setbytes("");
		var ok = 0;
		var fail = 0;
		var token = cts.Token;
		try {
			if (admin) {
				for (var i = 0; i < rows.Count; i++) {
					token.ThrowIfCancellationRequested();
					var r = rows[i];
					setstatus(Loc.T("inst.win.step", i + 1, rows.Count, r.Title));
					appendlog(install ? r.AddCmd : r.RemoveCmd);
					try {
						var code = await Task.Run(() => WinTtsPack.RunDism(install, r.Culture, log, token))
							.ConfigureAwait(true);
						if (code == 0 || code == 3010) {
							ok++;
							appendlog(Loc.T("inst.log.ok", r.Title) + " exit " + code);
						}
						else {
							fail++;
							appendlog(Loc.T("inst.log.err", "exit " + code));
						}
					}
					catch (OperationCanceledException) {
						appendlog(Loc.T("inst.log.cancel"));
						setstatus(Loc.T("inst.log.cancel"));
						break;
					}
					catch (Exception ex) {
						fail++;
						appendlog(Loc.T("inst.log.err", ex.Message));
						CaptureLog.Ex("win tts dism", ex);
					}
					setprogress((i + 1) / (double)rows.Count);
				}
			}
			else {
				appendlog(Loc.T("inst.win.elevate"));
				setstatus(Loc.T("inst.win.elevate"));
				foreach (var r in rows)
					appendlog(install ? r.AddCmd : r.RemoveCmd);
				WinElevateResult result;
				try {
					var cultures = rows.Select(r => r.Culture).ToList();
					result = await Task.Run(() => WinTtsPack.RunElevated(install, cultures, log, token))
						.ConfigureAwait(true);
				}
				catch (OperationCanceledException) {
					appendlog(Loc.T("inst.log.cancel"));
					appendlog(Loc.T("inst.win.elevate.cancel"));
					setstatus(Loc.T("inst.log.cancel"));
					result = null;
				}
				catch (Exception ex) {
					fail = rows.Count;
					appendlog(Loc.T("inst.win.elevate.fail", ex.Message));
					CaptureLog.Ex("win tts elevate", ex);
					result = null;
				}
				if (result != null && result.UacDenied) {
					appendlog(Loc.T("inst.win.uac"));
					setstatus(Loc.T("inst.win.uac"));
				}
				else if (result != null) {
					if (result.Error.Length > 0)
						appendlog(Loc.T("inst.win.elevate.fail", result.Error));
					foreach (var one in result.Codes) {
						var title = one.Culture;
						foreach (var r in rows) {
							if (!string.Equals(r.Culture, one.Culture, StringComparison.OrdinalIgnoreCase)) continue;
							title = r.Title;
							break;
						}
						if (one.Code == 0 || one.Code == 3010) {
							ok++;
							appendlog(Loc.T("inst.log.ok", title) + " exit " + one.Code);
						}
						else {
							fail++;
							appendlog(Loc.T("inst.log.err", title + " exit " + one.Code));
						}
					}
					if (result.Codes.Count == 0 && result.Error.Length > 0)
						fail = rows.Count;
					if (ok + fail > 0)
						setprogress(1);
				}
			}
		}
		finally {
			setbusy(false);
			try { cts?.Dispose(); } catch { }
			cts = null;
		}
		if (ok == 0 && fail == 0) return;
		var summary = Loc.T("inst.win.done", ok, fail);
		setstatus(summary);
		appendlog(summary);
		if (ok > 0) {
			appendlog(Loc.T("inst.win.restart"));
			MessageBox.Show(this, summary + "\n\n" + Loc.T("inst.win.restart"), Title,
				MessageBoxButton.OK, fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
			await loadwin(true);
		}
		else if (fail > 0) {
			MessageBox.Show(this, summary, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void onwinlog(string line, List<WinTtsRow> rows) {
		if (line != null && line.StartsWith("BEGIN ", StringComparison.Ordinal)) {
			var culture = line.Substring(6).Trim();
			var title = culture;
			var index = 0;
			for (var i = 0; i < rows.Count; i++) {
				if (!string.Equals(rows[i].Culture, culture, StringComparison.OrdinalIgnoreCase)) continue;
				title = rows[i].Title;
				index = i;
				break;
			}
			setstatus(Loc.T("inst.win.step", index + 1, rows.Count, title));
			return;
		}
		if (line != null && line.StartsWith("EXIT ", StringComparison.Ordinal)) return;
		appendlog(line);
	}

	enum WinPackState { Unknown, Missing, Installed }

	sealed class WinTtsRow : INotifyPropertyChanged {
		readonly WinPackState state;
		bool selected;

		public WinTtsRow(string culture, WinPackState state, string voices) {
			Culture = culture ?? "";
			this.state = state;
			Voices = string.IsNullOrEmpty(voices) ? Loc.T("inst.win.novoice") : voices;
			Title = titleof(Culture);
			Capability = WinTtsPack.Capability(Culture);
			AddCmd = WinTtsPack.AddCmd(Culture);
			RemoveCmd = WinTtsPack.RemoveCmd(Culture);
			HasVoice = !string.IsNullOrEmpty(voices);
			switch (state) {
			case WinPackState.Installed:
				StateText = Loc.T("inst.win.installed");
				StateBg = OkBg;
				StateFg = OkFg;
				break;
			case WinPackState.Missing:
				StateText = Loc.T("inst.win.missing");
				StateBg = MissBg;
				StateFg = MissFg;
				break;
			default:
				StateText = Loc.T("inst.win.unknown");
				StateBg = PartBg;
				StateFg = PartFg;
				break;
			}
		}

		public string Culture { get; }
		public string Title { get; }
		public string Voices { get; }
		public string Capability { get; }
		public string AddCmd { get; }
		public string RemoveCmd { get; }
		public bool HasVoice { get; }
		public bool Installed => state == WinPackState.Installed;
		public WinPackState State => state;
		public string StateText { get; }
		public Brush StateBg { get; }
		public Brush StateFg { get; }

		public bool Selected {
			get => selected;
			set {
				if (selected == value) return;
				selected = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;

		static string titleof(string culture) {
			try {
				var name = new CultureInfo(culture).DisplayName;
				if (!string.IsNullOrEmpty(name)) return $"{name} · {culture}";
			}
			catch { }
			var lang = TtsLang.DisplayName(SapiVoiceItem.LangOf(culture));
			if (string.IsNullOrEmpty(lang) || lang == culture) return culture;
			return $"{lang} · {culture}";
		}
	}
}
