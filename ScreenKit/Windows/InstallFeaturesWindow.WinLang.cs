using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ScreenKit;

partial class InstallFeaturesWindow {
	readonly ObservableCollection<WinLangNode> winlangRoots = new();
	Dictionary<WinSpeechPackKind, Dictionary<string, string>> winlangStates;
	List<SapiVoiceItem> winlangVoices = new();
	bool winlangLoaded;
	bool winlangUiLoading;
	bool winlangQuerying;

	enum WinPackState { Unknown, Missing, Installed }

	static bool dismrunning() {
		try { return Process.GetProcessesByName("dism").Length > 0; }
		catch { return false; }
	}

	static void addcultures(List<string> cultures, IEnumerable<string> extra) {
		if (extra == null) return;
		foreach (var culture in extra) {
			if (string.IsNullOrWhiteSpace(culture)) continue;
			if (!cultures.Any(x => string.Equals(x, culture, StringComparison.OrdinalIgnoreCase)))
				cultures.Add(culture.Trim());
		}
	}

	static WinPackState rowstate(Dictionary<string, string> states, string culture) {
		if (states == null) return WinPackState.Unknown;
		if (!states.TryGetValue(culture, out var raw)) return WinPackState.Missing;
		if (string.Equals(raw, "Installed", StringComparison.OrdinalIgnoreCase)) return WinPackState.Installed;
		return WinPackState.Missing;
	}

	async Task loadwinlang(bool force) {
		if (busy || winlangQuerying) return;
		if (winlangLoaded && !force) return;
		var local = new CancellationTokenSource();
		cts = local;
		winlangQuerying = true;
		if (force) winlangStates = null;
		applytabbuttons();
		setstatus(Loc.T("inst.winlang.query"));
		appendlog(Loc.T("inst.winlang.query"));
		refillwinlang();
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
			var voices = scanwinvoices();
			if (WinTtsPack.IsAdmin()) {
				winlangStates = await Task.Run(() => WinTtsPack.QueryAllStates(token)).ConfigureAwait(true);
				winlangVoices = await voices.ConfigureAwait(true);
			}
			else {
				appendlog(Loc.T("inst.win.query.elevate"));
				setstatus(Loc.T("inst.win.query.elevate"));
				var q = await Task.Run(() => WinTtsPack.QueryAllElevated(token, null)).ConfigureAwait(true);
				if (q.UacDenied) {
					uac = true;
					appendlog(Loc.T("inst.win.uac"));
					setstatus(Loc.T("inst.win.uac"));
					winlangVoices = await voices.ConfigureAwait(true);
				}
				else {
					winlangStates = q.States.Count > 0 ? q.States : null;
					winlangVoices = q.Voices.Count > 0 ? voiceitems(q.Voices) : await voices.ConfigureAwait(true);
					if (q.VoiceError.Length > 0) appendlog(q.VoiceError);
					if (winlangStates == null) qerr = q.Error.Length > 0 ? q.Error : "empty";
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
			winlangStates = null;
			CaptureLog.Ex("win language query", ex);
		}
		finally {
			winlangQuerying = false;
			if (ReferenceEquals(cts, local)) cts = null;
			try { local.Dispose(); } catch { }
			applytabbuttons();
		}
		refillwinlang();
		winlangLoaded = true;
		if (uac) return;
		if (!string.IsNullOrEmpty(qerr)) {
			appendlog(Loc.T("inst.win.queryfail", qerr));
			setstatus(Loc.T("inst.win.queryfail", qerr));
		}
		else {
			var leaves = winlangleaves().ToList();
			var ninst = leaves.Count(r => r.PackInstalled);
			var navail = leaves.Count(r => r.HasRuntime);
			setstatus(Loc.T("inst.winlang.ready", winlangRoots.Count, ninst, navail));
			appendlog(Loc.T("inst.winlang.ready", winlangRoots.Count, ninst, navail));
		}
	}

	static Task<List<SapiVoiceItem>> scanwinvoices() {
		var done = new TaskCompletionSource<List<SapiVoiceItem>>();
		var th = new Thread(() => {
			try {
				using var tts = new WinRtTts();
				done.TrySetResult(tts.Voices.ToList());
			}
			catch (Exception ex) {
				CaptureLog.Ex("win language voices", ex);
				done.TrySetResult(new List<SapiVoiceItem>());
			}
		});
		th.SetApartmentState(ApartmentState.STA);
		th.IsBackground = true;
		th.Start();
		return done.Task;
	}

	static List<SapiVoiceItem> voiceitems(List<WinVoiceLine> voices) {
		var list = new List<SapiVoiceItem>();
		foreach (var v in voices ?? []) {
			list.Add(new SapiVoiceItem {
				Culture = v.Culture,
				DisplayName = v.Name,
				Name = v.Name,
				Source = "winrt",
			});
		}
		return list;
	}

	void refillwinlang() {
		var checks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var expanded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		foreach (var root in winlangRoots) {
			expanded[root.Culture] = root.IsExpanded;
			foreach (var child in root.Children)
				if (child.IsChecked == true) checks.Add(nodekey(child.Kind, child.Culture));
		}
		var voices = langdetails(winlangVoices?.Select(v => (v.Culture, v.DisplayName)));
		var recognizers = langdetails(WindowsAsr.Scan().Select(x => (x.Culture, trimengine(x.DisplayName))));
		var ocr = langdetails(WinOcr.Languages().Select(x => (normalizeocrtag(x.Tag), x.Name)));
		var cultures = new List<string>();
		if (winlangStates != null)
			foreach (var map in winlangStates.Values) addcultures(cultures, map.Keys);
		addcultures(cultures, voices.Keys);
		addcultures(cultures, recognizers.Keys);
		addcultures(cultures, ocr.Keys);
		cultures.Sort(compareculture);
		winlangUiLoading = true;
		foreach (var root in winlangRoots) unwatchwinlang(root);
		winlangRoots.Clear();
		foreach (var culture in cultures) {
			var root = buildlangroot(culture, ocr, voices, recognizers);
			if (expanded.TryGetValue(culture, out var wasExpanded))
				root.IsExpanded = wasExpanded;
			foreach (var child in root.Children)
				child.SetCheck(checks.Contains(nodekey(child.Kind, child.Culture)), false);
			watchwinlang(root);
			winlangRoots.Add(root);
		}
		etreewinlang.ItemsSource = null;
		etreewinlang.ItemsSource = winlangRoots;
		winlangUiLoading = false;
		applywinlangfilter();
	}

	WinLangNode buildlangroot(string culture,
		Dictionary<string, string> ocr, Dictionary<string, string> voices,
		Dictionary<string, string> recognizers) {
		var root = WinLangNode.Group(culture, languagetitle(culture));
		root.Children.Add(buildlangleaf(culture, WinSpeechPackKind.Ocr,
			Loc.T("inst.winlang.ocr"), detailof(ocr, culture)));
		root.Children.Add(buildlangleaf(culture, WinSpeechPackKind.Tts,
			Loc.T("inst.winlang.tts"), detailof(voices, culture)));
		root.Children.Add(buildlangleaf(culture, WinSpeechPackKind.Asr,
			Loc.T("inst.winlang.asr"), detailof(recognizers, culture)));
		foreach (var child in root.Children) child.Parent = root;
		root.RefreshGroup();
		root.IsExpanded = root.Children.Any(x => x.PackInstalled || x.HasRuntime);
		return root;
	}

	WinLangNode buildlangleaf(string culture, WinSpeechPackKind kind, string title, string detail) {
		var map = statemap(kind);
		var known = map != null;
		var supported = !known || map.ContainsKey(culture) || detail.Length > 0;
		var state = rowstate(map, culture);
		return WinLangNode.Leaf(culture, kind, title,
			detail.Length > 0 ? detail : Loc.T("inst.winlang.notavailable"),
			state, supported, detail.Length > 0);
	}

	Dictionary<string, string> statemap(WinSpeechPackKind kind) {
		if (winlangStates != null && winlangStates.TryGetValue(kind, out var map)) return map;
		return null;
	}

	static string detailof(Dictionary<string, string> map, string culture) =>
		map.TryGetValue(culture, out var text) ? text : "";

	static Dictionary<string, string> langdetails(IEnumerable<(string Culture, string Name)> source) {
		var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var one in source ?? []) {
			var culture = (one.Culture ?? "").Trim();
			var name = (one.Name ?? "").Trim();
			if (culture.Length == 0 || name.Length == 0) continue;
			if (!lists.TryGetValue(culture, out var names)) {
				names = new List<string>();
				lists[culture] = names;
			}
			if (!names.Contains(name)) names.Add(name);
		}
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in lists)
			result[pair.Key] = string.Join(", ", pair.Value);
		return result;
	}

	static string trimengine(string displayName) {
		var name = displayName ?? "";
		var cut = name.LastIndexOf(" · ", StringComparison.Ordinal);
		return cut >= 0 && cut + 3 < name.Length ? name.Substring(cut + 3) : name;
	}

	static string normalizeocrtag(string tag) {
		var value = (tag ?? "").Trim();
		if (value.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("zh-CHS", StringComparison.OrdinalIgnoreCase))
			return "zh-CN";
		if (value.StartsWith("zh-Hant-HK", StringComparison.OrdinalIgnoreCase))
			return "zh-HK";
		if (value.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("zh-CHT", StringComparison.OrdinalIgnoreCase))
			return "zh-TW";
		if (value.Equals("ja", StringComparison.OrdinalIgnoreCase)) return "ja-JP";
		if (value.Equals("ko", StringComparison.OrdinalIgnoreCase)) return "ko-KR";
		return value;
	}

	static int compareculture(string a, string b) {
		var ar = WinLanguageRank(a);
		var br = WinLanguageRank(b);
		if (ar != br) return ar.CompareTo(br);
		return string.Compare(languagetitle(a), languagetitle(b), StringComparison.CurrentCulture);
	}

	internal static int WinLanguageRank(string culture) {
		var lang = (culture ?? "").Split('-')[0].ToLowerInvariant();
		return lang switch {
			"zh" => 0,
			"en" => 1,
			"ja" => 2,
			"ko" => 3,
			_ => 4,
		};
	}

	static string languagetitle(string culture) {
		try {
			var name = new CultureInfo(culture).DisplayName;
			if (!string.IsNullOrEmpty(name)) return $"{name} · {culture}";
		}
		catch { }
		return culture;
	}

	void watchwinlang(WinLangNode node) {
		node.PropertyChanged += onwinlangnode;
		foreach (var child in node.Children) watchwinlang(child);
	}

	void unwatchwinlang(WinLangNode node) {
		node.PropertyChanged -= onwinlangnode;
		foreach (var child in node.Children) unwatchwinlang(child);
	}

	void onwinlangnode(object sender, PropertyChangedEventArgs e) {
		if (winlangUiLoading) return;
		if (e.PropertyName != nameof(WinLangNode.IsChecked)) return;
		refreshwinlangcmd();
	}

	void applywinlangfilter() {
		var only = cwinlangonly.IsChecked == true;
		if (!only) {
			etreewinlang.ItemsSource = winlangRoots;
		}
		else {
			etreewinlang.ItemsSource = winlangRoots.Where(x => x.Installed).ToList();
		}
		winlangUiLoading = true;
		cwinlangheader.IsChecked = false;
		winlangUiLoading = false;
		refreshwinlangcmd();
	}

	void setwinlangcheck(bool on) {
		winlangUiLoading = true;
		foreach (var root in visiblewinlangroots())
			root.SetCheck(on, true);
		winlangUiLoading = false;
		refreshwinlangcmd();
	}

	IEnumerable<WinLangNode> visiblewinlangroots() =>
		(etreewinlang.ItemsSource as IEnumerable<WinLangNode>) ?? winlangRoots;

	IEnumerable<WinLangNode> winlangleaves() =>
		winlangRoots.SelectMany(x => x.Children);

	void refreshwinlangcmd() {
		var selected = winlangleaves().Where(x => x.IsChecked == true && x.CanSelect).ToList();
		if (selected.Count == 0) {
			ewinlangcmd.Text = "";
			return;
		}
		var sb = new StringBuilder();
		foreach (var row in selected)
			sb.AppendLine(WinTtsPack.AddCmd(row.Culture, row.Kind));
		sb.AppendLine();
		foreach (var row in selected)
			sb.AppendLine(WinTtsPack.RemoveCmd(row.Culture, row.Kind));
		ewinlangcmd.Text = sb.ToString().TrimEnd();
	}

	void copywinlangcmd() {
		var text = ewinlangcmd.Text ?? "";
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

	async Task runwinlang(bool install) {
		if (busy) return;
		var rows = winlangleaves().Where(x => x.IsChecked == true && x.CanSelect
			&& (install ? !x.PackInstalled : x.PackInstalled)).ToList();
		rows = orderactions(rows, install).ToList();
		if (rows.Count == 0) {
			MessageBox.Show(this, Loc.T(install ? "inst.win.none.add" : "inst.win.none.del"), Title,
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var names = string.Join("\n· ", rows.Select(x => x.Parent.Title + " · " + x.Title));
		var ask = Loc.T(install ? "inst.winlang.confirm.add" : "inst.winlang.confirm.del", names);
		if (MessageBox.Show(this, ask, Title,
				MessageBoxButton.YesNo, install ? MessageBoxImage.Question : MessageBoxImage.Warning)
			!= MessageBoxResult.Yes)
			return;
		cts = new CancellationTokenSource();
		var log = new Progress<string>(line => onwinlanglog(line, rows));
		setbusy(true);
		setprogress(0);
		setbytes("");
		var ok = 0;
		var fail = 0;
		var token = cts.Token;
		try {
			if (WinTtsPack.IsAdmin()) {
				for (var i = 0; i < rows.Count; i++) {
					token.ThrowIfCancellationRequested();
					var row = rows[i];
					setstatus(Loc.T("inst.win.step", i + 1, rows.Count, row.Parent.Title + " · " + row.Title));
					try {
						var code = await Task.Run(() => WinTtsPack.RunDism(
							install, row.Culture, log, token, row.Kind)).ConfigureAwait(true);
						if (code == 0 || code == 3010) ok++;
						else fail++;
						appendlog(code == 0 || code == 3010
							? Loc.T("inst.log.ok", row.Title) + " exit " + code
							: Loc.T("inst.log.err", row.Title + " exit " + code));
					}
					catch (OperationCanceledException) {
						appendlog(Loc.T("inst.log.cancel"));
						break;
					}
					catch (Exception ex) {
						fail++;
						appendlog(Loc.T("inst.log.err", ex.Message));
						CaptureLog.Ex("win language dism", ex);
					}
					setprogress((i + 1) / (double)rows.Count);
				}
			}
			else {
				appendlog(Loc.T("inst.win.elevate"));
				var requests = rows.Select(x => new WinPackRequest {
					Kind = x.Kind,
					Culture = x.Culture,
				}).ToList();
				WinElevateResult result;
				try {
					result = await Task.Run(() =>
						WinTtsPack.RunElevated(install, requests, log, token)).ConfigureAwait(true);
				}
				catch (OperationCanceledException) {
					appendlog(Loc.T("inst.log.cancel"));
					appendlog(Loc.T("inst.win.elevate.cancel"));
					result = null;
				}
				catch (Exception ex) {
					fail = rows.Count;
					appendlog(Loc.T("inst.win.elevate.fail", ex.Message));
					CaptureLog.Ex("win language elevate", ex);
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
						var row = rows.FirstOrDefault(x => x.Kind == one.Kind
							&& string.Equals(x.Culture, one.Culture, StringComparison.OrdinalIgnoreCase));
						var title = row == null ? one.Culture : row.Parent.Title + " · " + row.Title;
						if (one.Code == 0 || one.Code == 3010) ok++;
						else fail++;
						appendlog(one.Code == 0 || one.Code == 3010
							? Loc.T("inst.log.ok", title) + " exit " + one.Code
							: Loc.T("inst.log.err", title + " exit " + one.Code));
					}
					if (result.Codes.Count == 0 && result.Error.Length > 0) fail = rows.Count;
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
			winlangLoaded = false;
			MessageBox.Show(this, summary + "\n\n" + Loc.T("inst.winlang.restart"), Title,
				MessageBoxButton.OK, fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
		}
		else if (fail > 0) {
			MessageBox.Show(this, summary, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	static IEnumerable<WinLangNode> orderactions(List<WinLangNode> rows, bool install) {
		if (install)
			return rows.OrderBy(x => x.Culture).ThenBy(x => installrank(x.Kind));
		return rows.OrderBy(x => x.Culture).ThenBy(x => removerank(x.Kind));
	}

	static int installrank(WinSpeechPackKind kind) => kind switch {
		WinSpeechPackKind.Ocr => 0,
		WinSpeechPackKind.Tts => 1,
		_ => 2,
	};

	static int removerank(WinSpeechPackKind kind) => kind switch {
		WinSpeechPackKind.Asr => 0,
		WinSpeechPackKind.Tts => 1,
		_ => 2,
	};

	void onwinlanglog(string line, List<WinLangNode> rows) {
		if (line != null && line.StartsWith("BEGIN ", StringComparison.Ordinal)) {
			var key = line.Substring(6).Trim();
			var row = rows.FirstOrDefault(x => nodekey(x.Kind, x.Culture)
				.Equals(key, StringComparison.OrdinalIgnoreCase));
			var index = row == null ? 0 : rows.IndexOf(row);
			setstatus(Loc.T("inst.win.step", index + 1, rows.Count,
				row == null ? key : row.Parent.Title + " · " + row.Title));
			return;
		}
		if (line != null && line.StartsWith("EXIT ", StringComparison.Ordinal)) return;
		appendlog(line);
	}

	static string nodekey(WinSpeechPackKind kind, string culture) => kind + "|" + culture;

	enum WinLangPackState { Unknown, Missing, Installed, Unsupported }

	sealed class WinLangNode : INotifyPropertyChanged {
		bool? check;
		bool expanded;

		public static WinLangNode Group(string culture, string title) => new() {
			Culture = culture,
			Title = title,
			IsGroup = true,
			CanSelect = true,
			StateBg = PartBg,
			StateFg = PartFg,
		};

		public static WinLangNode Leaf(string culture, WinSpeechPackKind kind, string title,
			string detail, WinPackState raw, bool supported, bool hasRuntime) {
			var state = !supported ? WinLangPackState.Unsupported : raw switch {
				WinPackState.Installed => WinLangPackState.Installed,
				WinPackState.Missing => WinLangPackState.Missing,
				_ => WinLangPackState.Unknown,
			};
			var node = new WinLangNode {
				Culture = culture,
				Kind = kind,
				Title = title,
				Detail = detail,
				State = state,
				CanSelect = supported,
				HasRuntime = hasRuntime,
				Capability = WinTtsPack.Capability(culture, kind),
			};
			node.applystate();
			return node;
		}

		public WinLangNode Parent { get; set; }
		public ObservableCollection<WinLangNode> Children { get; } = new();
		public string Culture { get; private set; }
		public WinSpeechPackKind Kind { get; private set; }
		public string Title { get; private set; }
		public string Detail { get; private set; } = "";
		public string Capability { get; private set; } = "";
		public bool IsGroup { get; private set; }
		public bool CanSelect { get; private set; }
		public bool HasRuntime { get; private set; }
		public WinLangPackState State { get; private set; }
		public bool PackInstalled => State == WinLangPackState.Installed;
		public bool Installed => PackInstalled || HasRuntime;
		public string StateText { get; private set; } = "";
		public Brush StateBg { get; private set; }
		public Brush StateFg { get; private set; }

		public bool? IsChecked {
			get => check;
			set => SetCheck(value == true, true);
		}

		public bool IsExpanded {
			get => expanded;
			set {
				if (expanded == value) return;
				expanded = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
			}
		}

		public void SetCheck(bool value, bool cascade) {
			if (!CanSelect) value = false;
			if (check != value) {
				check = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
			}
			if (cascade && IsGroup) {
				foreach (var child in Children)
					child.SetCheck(value, false);
			}
			Parent?.RefreshCheck();
		}

		public void RefreshCheck() {
			if (!IsGroup) return;
			var selectable = Children.Where(x => x.CanSelect).ToList();
			var on = selectable.Count(x => x.IsChecked == true);
			bool? next = on == 0 ? false : on == selectable.Count ? true : null;
			if (check != next) {
				check = next;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
			}
		}

		public void RefreshGroup() {
			if (!IsGroup) return;
			var installed = Children.Count(x => x.PackInstalled);
			var available = Children.Count(x => x.HasRuntime);
			Detail = Loc.T("inst.winlang.group.detail", installed, available);
			if (installed == Children.Count) {
				StateText = Loc.T("inst.win.installed");
				StateBg = OkBg;
				StateFg = OkFg;
			}
			else if (installed > 0 || available > 0) {
				StateText = Loc.T("inst.partial");
				StateBg = PartBg;
				StateFg = PartFg;
			}
			else {
				StateText = Loc.T("inst.win.missing");
				StateBg = MissBg;
				StateFg = MissFg;
			}
			RefreshCheck();
		}

		void applystate() {
			if (State == WinLangPackState.Installed && HasRuntime) {
				StateText = Loc.T("inst.win.installed");
				StateBg = OkBg;
				StateFg = OkFg;
			}
			else if (State == WinLangPackState.Installed) {
				StateText = Loc.T("inst.winlang.needrestart");
				StateBg = PartBg;
				StateFg = PartFg;
			}
			else if (State == WinLangPackState.Unsupported) {
				StateText = Loc.T("inst.winlang.unsupported");
				StateBg = Brushes.Transparent;
				StateFg = PartFg;
			}
			else if (HasRuntime) {
				StateText = Loc.T("inst.winlang.available");
				StateBg = PartBg;
				StateFg = PartFg;
			}
			else if (State == WinLangPackState.Missing) {
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

		public event PropertyChangedEventHandler PropertyChanged;
	}
}
