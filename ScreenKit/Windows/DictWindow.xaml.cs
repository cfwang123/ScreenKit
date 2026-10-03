using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ScreenKit;

public sealed class DictRow {
	public long Id { get; set; }
	public string Head { get; set; } = "";
	public string Meta { get; set; } = "";
	public string Preview { get; set; } = "";
}

/// <summary>英日韩词典：只读 exe 旁的 dict.db。热键再按一次隐藏。</summary>
public partial class DictWindow : Window {
	WinRtTts tts;
	TtsPlayer player;
	DispatcherTimer tick;
	int lastedit;
	bool pending;
	int gen;
	int dgen;
	bool filling;
	bool forceClose;
	bool speaking;
	string curword = "";
	string curzh = "";
	string curlang = "";

	public DictWindow() {
		InitializeComponent();
		initui();
	}

	void initui() {
		WindowEsc.Attach(this, Hide);
		Closing += (_, e) => {
			if (forceClose) return;
			e.Cancel = true;
			Hide();
		};
		tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
		tick.Tick += (_, _) => ontick();
		esearch.TextChanged += (_, _) => queue();
		elang.SelectionChanged += (_, _) => queue();
		lhits.SelectionChanged += (_, _) => {
			if (filling) return;
			if (lhits.SelectedItem is DictRow row) loaddetail(row);
		};
		bspeak.Click += (_, _) => _ = speak(curword, speaklang(curlang));
		bspeakzh.Click += (_, _) => _ = speak(curzh, "zh");
		elang.SelectedIndex = 0;
		applylang();
		return;

		void queue() {
			lastedit = Environment.TickCount;
			pending = true;
			if (!tick.IsEnabled) tick.Start();
		}

		void ontick() {
			if (!pending) { tick.Stop(); return; }
			if (Environment.TickCount - lastedit < 180) return;
			pending = false;
			tick.Stop();
			runsearch();
		}
	}

	public void ForceClose() {
		forceClose = true;
		try { tick?.Stop(); } catch { }
		try { player?.Dispose(); } catch { }
		try { tts?.Dispose(); } catch { }
		player = null;
		tts = null;
		try { Close(); } catch { }
	}

	public void ShowFromHotkey() {
		ensuredb();
		if (!IsVisible) Show();
		if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
		Activate();
		try {
			esearch.Focus();
			esearch.SelectAll();
		}
		catch { }
	}

	public void ApplyLang() => applylang();

	void applylang() {
		Title = Loc.T("dict.title");
		bspeak.Content = Loc.T("dict.speak");
		bspeakzh.Content = Loc.T("dict.speakzh");
		bspeak.ToolTip = Loc.T("dict.speak.tip");
		bspeakzh.ToolTip = Loc.T("dict.speakzh.tip");
		setfilter(0, Loc.T("dict.filter.all"));
		setfilter(1, Loc.T("dict.filter.en"));
		setfilter(2, Loc.T("dict.filter.ja"));
		setfilter(3, Loc.T("dict.filter.ko"));
	}

	void setfilter(int index, string text) {
		if (index < 0 || index >= elang.Items.Count) return;
		if (elang.Items[index] is ComboBoxItem it) it.Content = text;
	}

	void ensuredb() {
		if (DictDb.Ready) {
			if (string.IsNullOrWhiteSpace(lbstatus.Text))
				lbstatus.Text = Loc.T("dict.ready");
			return;
		}
		var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict.db");
		if (!DictDb.Init(path)) {
			lbstatus.Text = DictDb.Error == "missing" ? Loc.T("dict.missing") : DictDb.Error;
			return;
		}
		lbstatus.Text = Loc.T("dict.ready");
	}

	void runsearch() {
		ensuredb();
		if (!DictDb.Ready) return;
		var q = esearch.Text ?? "";
		var dict = "";
		if (elang.SelectedItem is ComboBoxItem it && it.Tag is string tag) dict = tag;
		if (string.IsNullOrWhiteSpace(q)) {
			gen++;
			filling = true;
			lhits.ItemsSource = null;
			filling = false;
			edetail.Clear();
			curword = "";
			curzh = "";
			curlang = "";
			lbstatus.Text = Loc.T("dict.ready");
			return;
		}
		var g = ++gen;
		lbstatus.Text = Loc.T("dict.searching");
		Task.Run(() => {
			try { return DictDb.Search(q, dict, 80); }
			catch (Exception ex) {
				return new List<DictHit> { new DictHit { Preview = ex.Message, Headword = "" } };
			}
		}).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				if (g != gen) return;
				var hits = t.Result ?? new List<DictHit>();
				if (hits.Count == 1 && hits[0].Id == 0 && hits[0].Headword.Length == 0 && hits[0].Preview.Length > 0) {
					lbstatus.Text = hits[0].Preview;
					return;
				}
				showhits(hits);
			}));
		});
	}

	void showhits(List<DictHit> hits) {
		var rows = new List<DictRow>(hits.Count);
		foreach (var h in hits) {
			if (h.Id == 0) continue;
			var meta = new StringBuilder();
			meta.Append(langlabel(h.Dict));
			if (h.Reading.Length > 0) meta.Append(" · ").Append(h.Reading);
			if (h.Pos.Length > 0) meta.Append(" · ").Append(h.Pos);
			if (h.Via.Length > 0) meta.Append(" · ").Append(Loc.T("dict.via", h.Via));
			rows.Add(new DictRow {
				Id = h.Id,
				Head = h.Headword.Length > 0 ? h.Headword : h.Matched,
				Meta = meta.ToString(),
				Preview = h.Preview ?? "",
			});
		}
		filling = true;
		lhits.ItemsSource = rows;
		if (rows.Count > 0) lhits.SelectedIndex = 0;
		filling = false;
		lbstatus.Text = Loc.T("dict.count", rows.Count);
		if (rows.Count > 0) loaddetail(rows[0]);
		else {
			edetail.Clear();
			curword = "";
			curzh = "";
			curlang = "";
		}
	}

	void loaddetail(DictRow row) {
		if (row == null) return;
		var g = ++dgen;
		var id = row.Id;
		Task.Run(() => {
			try { return DictDb.Get(id); }
			catch { return null; }
		}).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				if (g != dgen) return;
				filldetail(t.Result);
			}));
		});
	}

	void filldetail(DictEntry e) {
		if (e == null) {
			edetail.Clear();
			curword = "";
			curzh = "";
			curlang = "";
			return;
		}
		curlang = e.Dict ?? "";
		curword = e.Word.Length > 0 ? e.Word : e.Headword;
		curzh = e.ZhSpeak();
		var sb = new StringBuilder();
		sb.Append(curword);
		if (e.Kanji.Length > 0 && e.Kanji != curword) sb.Append("  ").Append(e.Kanji);
		sb.AppendLine();
		var meta = new List<string>();
		var lang = langlabel(e.Dict);
		if (lang.Length > 0) meta.Add(lang);
		var reading = e.Pron.Length > 0 ? e.Pron : e.Reading;
		if (reading.Length > 0) meta.Add(reading);
		if (e.Pos.Length > 0) meta.Add(e.Pos);
		if (meta.Count > 0) sb.AppendLine(string.Join(" · ", meta));
		if (e.Usage.Count > 0) sb.AppendLine(string.Join(" · ", e.Usage));
		sb.AppendLine();
		var n = 1;
		string lastpos = null;
		foreach (var s in e.Senses) {
			if (s.Pos.Length > 0 && s.Pos != lastpos && s.Pos != e.Pos) {
				sb.AppendLine(s.Pos);
				lastpos = s.Pos;
			}
			sb.Append(n++).Append(". ");
			var any = false;
			any |= appendbit(sb, s.Zh, s.ZhDef, any);
			any |= appendbit(sb, s.En, s.EnDef, any);
			if (s.Ko.Length > 0) {
				if (any) sb.Append("  ");
				sb.Append(s.Ko);
				any = true;
			}
			any |= appendbit(sb, s.Ja, s.JaDef, any);
			if (!any) sb.Append(Loc.T("dict.empty.sense"));
			sb.AppendLine();
			foreach (var p in s.Phrases) linepair(sb, "  · ", p.Text, p.Zh);
			foreach (var p in s.Sentences) linepair(sb, "  ", p.Text, p.Zh);
		}
		if (e.Conjugations.Count > 0) {
			sb.AppendLine();
			sb.AppendLine(Loc.T("dict.conj"));
			foreach (var c in e.Conjugations) sb.AppendLine(c);
		}
		if (e.Etymology.Length > 0) {
			sb.AppendLine();
			sb.AppendLine(e.Etymology);
		}
		if (e.See.Count > 0) {
			sb.AppendLine();
			sb.AppendLine(Loc.T("dict.see", string.Join(" · ", e.See)));
		}
		if (e.Extra.Length > 0) {
			sb.AppendLine();
			sb.AppendLine(e.Extra);
		}
		edetail.Text = sb.ToString().TrimEnd();
		edetail.ScrollToHome();
	}

	static bool appendbit(StringBuilder sb, string lemma, string def, bool started) {
		if (lemma.Length == 0 && def.Length == 0) return false;
		if (started) sb.Append("  ");
		if (lemma.Length > 0) sb.Append(lemma);
		if (def.Length > 0) {
			if (lemma.Length > 0) sb.Append("  ");
			sb.Append(def);
		}
		return true;
	}

	static void linepair(StringBuilder sb, string indent, string a, string b) {
		if (a.Length == 0 && b.Length == 0) return;
		sb.Append(indent);
		sb.Append(a);
		if (b.Length > 0) {
			if (a.Length > 0) sb.Append("  ");
			sb.Append(b);
		}
		sb.AppendLine();
	}

	async Task speak(string text, string lang) {
		if (speaking) return;
		if (string.IsNullOrWhiteSpace(text)) {
			lbstatus.Text = lang == "zh" ? Loc.T("dict.nozh") : Loc.T("dict.noword");
			return;
		}
		speaking = true;
		try {
			if (tts == null) tts = new WinRtTts();
			if (player == null) player = new TtsPlayer();
			var voice = pickvoice(lang);
			if (voice == null) {
				lbstatus.Text = Loc.T("dict.novoice", lang);
				return;
			}
			tts.SelectVoice(voice.Key);
			var (samples, sr) = await tts.Synthesize(text);
			if (samples == null || samples.Length == 0) {
				lbstatus.Text = Loc.T("dict.novoice", lang);
				return;
			}
			player.Play(samples, sr);
			lbstatus.Text = Loc.T("dict.speaking", langlabel(lang));
		}
		catch (Exception ex) {
			lbstatus.Text = ex.Message;
		}
		finally { speaking = false; }
	}

	SapiVoiceItem pickvoice(string lang) {
		if (tts == null || string.IsNullOrEmpty(lang)) return null;
		SapiVoiceItem fallback = null;
		foreach (var v in tts.Voices) {
			if (!string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase)) continue;
			if (fallback == null) fallback = v;
			if (lang == "zh" && v.Culture != null &&
				v.Culture.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase))
				return v;
		}
		return fallback;
	}

	static string speaklang(string dict) {
		if (dict == "ja" || dict == "ko" || dict == "en") return dict;
		return "en";
	}

	static string langlabel(string dict) {
		if (dict == "en") return Loc.T("dict.filter.en");
		if (dict == "ja") return Loc.T("dict.filter.ja");
		if (dict == "ko") return Loc.T("dict.filter.ko");
		if (dict == "zh") return Loc.T("dict.filter.zh");
		return "";
	}
}
