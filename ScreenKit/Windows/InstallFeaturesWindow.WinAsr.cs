using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace ScreenKit;

partial class InstallFeaturesWindow {
	readonly ObservableCollection<WinAsrRow> winasrRows = new();
	readonly List<WinAsrRow> winasrAll = new();
	Dictionary<string, string> winasrStates;
	bool winasrLoaded;
	bool winasrUiLoading;
	bool winasrQuerying;

	async Task loadwinasr(bool force) {
		if (busy || winasrQuerying) return;
		if (winasrLoaded && !force) return;
		var local = new CancellationTokenSource();
		cts = local;
		winasrQuerying = true;
		if (force) winasrStates = null;
		applytabbuttons();
		setstatus(Loc.T("inst.winasr.query"));
		appendlog(Loc.T("inst.winasr.query"));
		refillwinasr();
		string qerr = null;
		var uac = false;
		try {
			var token = local.Token;
			if (dismrunning()) {
				appendlog(Loc.T("inst.win.dismbusy"));
				setstatus(Loc.T("inst.win.dismbusy"));
				while (dismrunning())
					await Task.Delay(2000, token).ConfigureAwait(true);
			}
			if (WinTtsPack.IsAdmin()) {
				winasrStates = await Task.Run(() =>
					WinTtsPack.QueryStates(token, WinSpeechPackKind.Asr)).ConfigureAwait(true);
			}
			else {
				appendlog(Loc.T("inst.win.query.elevate"));
				setstatus(Loc.T("inst.win.query.elevate"));
				var q = await Task.Run(() =>
					WinTtsPack.QueryElevated(token, null, WinSpeechPackKind.Asr)).ConfigureAwait(true);
				if (q.UacDenied) {
					uac = true;
					appendlog(Loc.T("inst.win.uac"));
					setstatus(Loc.T("inst.win.uac"));
				}
				else {
					winasrStates = q.States.Count > 0 ? q.States : null;
					if (winasrStates == null) qerr = q.Error.Length > 0 ? q.Error : "empty";
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
			winasrStates = null;
			CaptureLog.Ex("win asr query", ex);
		}
		finally {
			winasrQuerying = false;
			if (ReferenceEquals(cts, local)) cts = null;
			try { local.Dispose(); } catch { }
			applytabbuttons();
		}
		refillwinasr();
		winasrLoaded = true;
		if (uac) return;
		if (!string.IsNullOrEmpty(qerr)) {
			appendlog(Loc.T("inst.win.queryfail", qerr));
			setstatus(Loc.T("inst.win.queryfail", qerr));
		}
		else {
			var ninst = winasrAll.Count(r => r.PackInstalled);
			var nrec = winasrAll.Count(r => r.HasRecognizer);
			setstatus(Loc.T("inst.winasr.ready", winasrAll.Count, ninst, nrec));
			appendlog(Loc.T("inst.winasr.ready", winasrAll.Count, ninst, nrec));
		}
	}

	void refillwinasr() {
		var sel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var r in winasrAll)
			if (r.Selected) sel.Add(r.Culture);
		var only = cwinasronly != null && cwinasronly.IsChecked == true;
		fillwinasr(winasrStates, WindowsAsr.Scan());
		winasrUiLoading = true;
		foreach (var r in winasrAll)
			if (sel.Contains(r.Culture)) r.Selected = true;
		winasrUiLoading = false;
		if (only) applywinasrfilter();
		else refreshwinasrcmd();
	}

	void fillwinasr(Dictionary<string, string> states, List<AsrModelInfo> models) {
		var recognizers = recognizertext(models);
		var cultures = new List<string>(WinTtsPack.AsrCultures);
		addcultures(cultures, states?.Keys);
		addcultures(cultures, recognizers.Keys);
		winasrUiLoading = true;
		foreach (var old in winasrAll) old.PropertyChanged -= onwinasrrow;
		winasrAll.Clear();
		foreach (var culture in cultures) {
			var st = rowstate(states, culture);
			var row = new WinAsrRow(culture, st,
				recognizers.TryGetValue(culture, out var names) ? names : "");
			row.PropertyChanged += onwinasrrow;
			winasrAll.Add(row);
		}
		winasrAll.Sort((a, b) => {
			var c = b.PackInstalled.CompareTo(a.PackInstalled);
			if (c != 0) return c;
			c = b.HasRecognizer.CompareTo(a.HasRecognizer);
			if (c != 0) return c;
			return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
		});
		winasrUiLoading = false;
		applywinasrfilter();
	}

	static Dictionary<string, string> recognizertext(List<AsrModelInfo> models) {
		var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var model in models ?? []) {
			var culture = (model.Culture ?? "").Trim();
			if (culture.Length == 0) continue;
			if (!map.TryGetValue(culture, out var list)) {
				list = new List<string>();
				map[culture] = list;
			}
			var name = model.DisplayName ?? "";
			var cut = name.LastIndexOf(" · ", StringComparison.Ordinal);
			if (cut >= 0 && cut + 3 < name.Length) name = name.Substring(cut + 3);
			if (name.Length > 0 && !list.Contains(name)) list.Add(name);
		}
		var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var kv in map)
			text[kv.Key] = string.Join(", ", kv.Value);
		return text;
	}

	void onwinasrrow(object sender, PropertyChangedEventArgs e) {
		if (winasrUiLoading) return;
		if (e.PropertyName == nameof(WinAsrRow.Selected)) refreshwinasrcmd();
	}

	void applywinasrfilter() {
		winasrUiLoading = true;
		winasrRows.Clear();
		var only = cwinasronly.IsChecked == true;
		foreach (var r in winasrAll) {
			if (only && !r.Installed) continue;
			winasrRows.Add(r);
		}
		cwinasrheader.IsChecked = false;
		winasrUiLoading = false;
		refreshwinasrcmd();
	}

	void setwinasrcheck(bool on) {
		winasrUiLoading = true;
		foreach (var r in winasrRows) r.Selected = on;
		winasrUiLoading = false;
		refreshwinasrcmd();
	}

	void refreshwinasrcmd() {
		var sel = winasrAll.Where(r => r.Selected).ToList();
		if (sel.Count == 0) {
			ewinasrcmd.Text = "";
			return;
		}
		var sb = new StringBuilder();
		foreach (var r in sel) {
			if (sb.Length > 0) sb.AppendLine();
			sb.AppendLine(r.AddCmd);
			sb.AppendLine(r.RemoveCmd);
		}
		ewinasrcmd.Text = sb.ToString().TrimEnd();
	}

	void copywinasrcmd() {
		var text = ewinasrcmd.Text ?? "";
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

	async Task runwinasr(bool install) {
		if (busy) return;
		var rows = winasrAll.Where(r => r.Selected && (install
			? !r.PackInstalled
			: r.PackInstalled)).ToList();
		if (rows.Count == 0) {
			MessageBox.Show(this, Loc.T(install ? "inst.win.none.add" : "inst.win.none.del"), Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var names = string.Join("\n· ", rows.Select(r => r.Title));
		var ask = Loc.T(install ? "inst.winasr.confirm.add" : "inst.winasr.confirm.del", names);
		if (MessageBox.Show(this, ask, Title,
				MessageBoxButton.YesNo, install ? MessageBoxImage.Question : MessageBoxImage.Warning)
			!= MessageBoxResult.Yes)
			return;
		var admin = WinTtsPack.IsAdmin();
		cts = new CancellationTokenSource();
		var log = new Progress<string>(line => onwinasrlog(line, rows));
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
						var code = await Task.Run(() => WinTtsPack.RunDism(
							install, r.Culture, log, token, WinSpeechPackKind.Asr)).ConfigureAwait(true);
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
						CaptureLog.Ex("win asr dism", ex);
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
					result = await Task.Run(() => WinTtsPack.RunElevated(
						install, cultures, log, token, WinSpeechPackKind.Asr)).ConfigureAwait(true);
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
					CaptureLog.Ex("win asr elevate", ex);
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
						var title = rows.FirstOrDefault(r =>
							string.Equals(r.Culture, one.Culture, StringComparison.OrdinalIgnoreCase))?.Title
							?? one.Culture;
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
					if (ok + fail > 0) setprogress(1);
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
			NeedRestart = true;
			appendlog(Loc.T("inst.winasr.restart"));
			MessageBox.Show(this, summary + "\n\n" + Loc.T("inst.winasr.restart"), Title,
				MessageBoxButton.OK, fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
			await loadwinasr(true);
		}
		else if (fail > 0) {
			MessageBox.Show(this, summary, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void onwinasrlog(string line, List<WinAsrRow> rows) {
		if (line != null && line.StartsWith("BEGIN ", StringComparison.Ordinal)) {
			var culture = line.Substring(6).Trim();
			var row = rows.FirstOrDefault(r =>
				string.Equals(r.Culture, culture, StringComparison.OrdinalIgnoreCase));
			var index = row == null ? 0 : rows.IndexOf(row);
			setstatus(Loc.T("inst.win.step", index + 1, rows.Count, row?.Title ?? culture));
			return;
		}
		if (line != null && line.StartsWith("EXIT ", StringComparison.Ordinal)) return;
		appendlog(line);
	}

	sealed class WinAsrRow : INotifyPropertyChanged {
		readonly WinPackState state;
		bool selected;

		public WinAsrRow(string culture, WinPackState state, string recognizers) {
			Culture = culture ?? "";
			this.state = state;
			Recognizers = string.IsNullOrEmpty(recognizers) ? Loc.T("inst.winasr.norecognizer") : recognizers;
			Title = titleof(Culture);
			Capability = WinTtsPack.Capability(Culture, WinSpeechPackKind.Asr);
			AddCmd = WinTtsPack.AddCmd(Culture, WinSpeechPackKind.Asr);
			RemoveCmd = WinTtsPack.RemoveCmd(Culture, WinSpeechPackKind.Asr);
			HasRecognizer = !string.IsNullOrEmpty(recognizers);
			if (state == WinPackState.Installed && HasRecognizer) {
				StateText = Loc.T("inst.win.installed");
				StateBg = OkBg;
				StateFg = OkFg;
			}
			else if (state == WinPackState.Installed) {
				StateText = Loc.T("inst.winasr.needrestart");
				StateBg = PartBg;
				StateFg = PartFg;
			}
			else if (HasRecognizer) {
				StateText = Loc.T("inst.winasr.recognizeronly");
				StateBg = PartBg;
				StateFg = PartFg;
			}
			else if (state == WinPackState.Missing) {
				StateText = Loc.T("inst.win.missing");
				StateBg = MissBg;
				StateFg = MissFg;
			}
			else {
				StateText = Loc.T("inst.win.unknown");
				StateBg = PartBg;
				StateFg = PartFg;
			}
		}

		public string Culture { get; }
		public string Title { get; }
		public string Recognizers { get; }
		public string Capability { get; }
		public string AddCmd { get; }
		public string RemoveCmd { get; }
		public bool HasRecognizer { get; }
		public bool PackInstalled => state == WinPackState.Installed;
		public bool Installed => PackInstalled || HasRecognizer;
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
			return culture;
		}
	}
}
