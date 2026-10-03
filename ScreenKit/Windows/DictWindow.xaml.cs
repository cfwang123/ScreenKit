using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenKit;

public sealed class DictRow {
	public long Id { get; set; }
	public string Dict { get; set; } = "";
	public string Word { get; set; } = "";
	public string Title { get; set; } = "";
	public string Preview { get; set; } = "";
	public override string ToString() => Title ?? "";
}

sealed class DictSelRow {
	public long Id { get; set; }
	public string Word { get; set; } = "";
	public string Title { get; set; } = "";
	public string Gloss { get; set; } = "";
	public override string ToString() => Title ?? "";
}

enum DRole { Body, Title, Pron, Pos, Number, Label, ExLabel, Example }

/// <summary>英日韩词典：只读 exe 旁的 dict.db。嵌在主窗口词典页。</summary>
public partial class DictWindow : UserControl {
	static readonly Brush CPron = freeze(110, 110, 110);
	static readonly Brush CPos = freeze(136, 48, 168);
	static readonly Brush CNumber = freeze(210, 85, 20);
	static readonly Brush CLabel = freeze(40, 90, 170);
	static readonly Brush CExLabel = freeze(20, 130, 70);
	static readonly Brush CExample = freeze(45, 120, 75);
	static readonly Brush CSpeak = freeze(40, 120, 190);

	EdgeOnlineTts edge;
	TtsPlayer player;
	DispatcherTimer tick;
	int lastedit;
	bool pending;
	int gen;
	int dgen;
	int sgen;
	bool filling;
	bool speaking;
	bool suppress;
	long wantid;
	string seltext = "";
	string curword = "";
	string curzh = "";
	string curlang = "";
	Point downpt;
	bool dragsel;

	/// <summary>选区「翻译」：主窗打开翻译小窗并填入原文。</summary>
	public Action<string> OnTranslate;
	/// <summary>当前选项。发音读这里的词典引擎和发音人。</summary>
	public Func<OcrOptions> Options;

	public DictWindow() {
		InitializeComponent();
		initui();
	}

	void initui() {
		PreviewKeyDown += (_, e) => {
			if (e.Key != Key.Escape || !psel.IsOpen) return;
			psel.IsOpen = false;
			e.Handled = true;
		};
		tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
		tick.Tick += (_, _) => ontick();
		esearch.TextChanged += (_, _) => {
			if (suppress) return;
			wantid = 0;
			queuesearch();
		};
		elang.SelectionChanged += (_, _) => queuesearch();
		lhits.SelectionChanged += (_, _) => {
			if (filling) return;
			if (lhits.SelectedItem is DictRow row) loaddetail(row);
		};
		lhits.AddHandler(Button.ClickEvent, new RoutedEventHandler(onrowspeak));
		bspeak.Click += (_, _) => _ = speak(curword, speaklang(curlang));
		bspeakzh.Click += (_, _) => _ = speak(curzh, "zh");
		bselspeak.Click += (_, _) => _ = speak(seltext, speaklang(curlang));
		bselsearch.Click += (_, _) => {
			var q = seltext;
			psel.IsOpen = false;
			searchword(q);
		};
		bseltr.Click += (_, _) => {
			var q = seltext;
			psel.IsOpen = false;
			if (q.Length == 0) return;
			if (OnTranslate != null) OnTranslate(q);
			else lbstatus.Text = Loc.T("dict.sel.notr");
		};
		bselcopy.Click += (_, _) => copytext(seltext);
		lsel.PreviewMouseLeftButtonUp += (_, _) => {
			if (lsel.SelectedItem is DictSelRow row) openhit(row);
		};
		edetail.PreviewMouseLeftButtonDown += (_, e) => {
			downpt = e.GetPosition(edetail);
			dragsel = false;
		};
		edetail.PreviewMouseMove += (_, e) => {
			if (e.LeftButton != MouseButtonState.Pressed) return;
			var p = e.GetPosition(edetail);
			if (Math.Abs(p.X - downpt.X) + Math.Abs(p.Y - downpt.Y) > 6) dragsel = true;
		};
		edetail.PreviewMouseLeftButtonUp += (_, _) => {
			if (!dragsel) return;
			Dispatcher.BeginInvoke(new Action(openselpop), DispatcherPriority.Background);
		};
		edetail.PreviewMouseRightButtonUp += (_, e) => {
			e.Handled = true;
			openselpop();
		};
		PreviewMouseDown += (_, e) => {
			if (!psel.IsOpen) return;
			if (e.OriginalSource is DependencyObject d && under(psel.Child, d)) return;
			psel.IsOpen = false;
		};
		elang.SelectedIndex = 0;
		applylang();
		cleardetail();
		return;

		void ontick() {
			if (!pending) { tick.Stop(); return; }
			if (Environment.TickCount - lastedit < 180) return;
			pending = false;
			tick.Stop();
			runsearch();
		}
	}

	public void Shutdown() {
		try { tick?.Stop(); } catch { }
		try { player?.Dispose(); } catch { }
		try { edge?.Dispose(); } catch { }
		player = null;
		edge = null;
	}

	public void FocusSearch() {
		ensuredb();
		try {
			esearch.Focus();
			esearch.SelectAll();
		}
		catch { }
	}

	public void ApplyLang() => applylang();

	void applylang() {
		bspeak.Content = Loc.T("dict.speak");
		bspeakzh.Content = Loc.T("dict.speakzh");
		bspeak.ToolTip = Loc.T("dict.speak.tip");
		bspeakzh.ToolTip = Loc.T("dict.speakzh.tip");
		bselspeak.Content = Loc.T("dict.speak");
		bselsearch.Content = Loc.T("dict.sel.search");
		bseltr.Content = Loc.T("dict.sel.translate");
		bselcopy.Content = Loc.T("dict.sel.copy");
		lbselwait.Text = Loc.T("dict.sel.searching");
		setfilter(0, Loc.T("dict.filter.all"));
		setfilter(1, Loc.T("dict.filter.zh"));
		setfilter(2, Loc.T("dict.filter.en"));
		setfilter(3, Loc.T("dict.filter.ja"));
		setfilter(4, Loc.T("dict.filter.ko"));
	}

	void setfilter(int index, string text) {
		if (index < 0 || index >= elang.Items.Count) return;
		if (elang.Items[index] is ComboBoxItem it) it.Content = text;
	}

	void queuesearch() {
		lastedit = Environment.TickCount;
		pending = true;
		if (tick != null && !tick.IsEnabled) tick.Start();
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
			cleardetail();
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
			var word = h.Headword.Length > 0 ? h.Headword : h.Matched;
			var lang = langlabel(h.Dict);
			var title = lang.Length > 0 ? "[" + lang + "] " + word : word;
			var preview = h.Preview ?? "";
			if (h.Via.Length > 0) {
				var via = Loc.T("dict.via", h.Via);
				preview = preview.Length > 0 ? preview + "  ·  " + via : via;
			}
			rows.Add(new DictRow {
				Id = h.Id,
				Dict = h.Dict ?? "",
				Word = word,
				Title = title,
				Preview = preview,
			});
		}
		var pick = 0;
		if (wantid != 0) {
			for (var i = 0; i < rows.Count; i++) {
				if (rows[i].Id != wantid) continue;
				pick = i;
				break;
			}
			wantid = 0;
		}
		filling = true;
		lhits.ItemsSource = rows;
		if (rows.Count > 0) lhits.SelectedIndex = pick;
		filling = false;
		lbstatus.Text = Loc.T("dict.count", rows.Count);
		if (rows.Count > 0) loaddetail(rows[pick]);
		else cleardetail();
	}

	void onrowspeak(object sender, RoutedEventArgs e) {
		if (e.OriginalSource is not Button b) return;
		if (b.DataContext is not DictRow row) return;
		e.Handled = true;
		_ = speak(row.Word, speaklang(row.Dict));
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

	void cleardetail() {
		psel.IsOpen = false;
		curword = "";
		curzh = "";
		curlang = "";
		edetail.Document = newdoc();
	}

	void filldetail(DictEntry e) {
		if (e == null) { cleardetail(); return; }
		psel.IsOpen = false;
		curlang = e.Dict ?? "";
		curword = e.Word.Length > 0 ? e.Word : e.Headword;
		curzh = e.ZhSpeak();
		var doc = newdoc();
		var head = para(2);
		addrun(head, curword, DRole.Title);
		if (e.Kanji.Length > 0 && e.Kanji != curword) addrun(head, "  " + e.Kanji, DRole.Body);
		doc.Blocks.Add(head);
		var reading = e.Pron.Length > 0 ? e.Pron : e.Reading;
		if (reading.Length > 0) doc.Blocks.Add(one(bracket(reading), DRole.Pron, 1));
		var zharticle = e.Dict == "zh" && e.Extra.Length > 0;
		if (e.Pos.Length > 0) doc.Blocks.Add(one(e.Pos, DRole.Pos, 1));
		if (!zharticle) {
			var tags = new List<string>();
			foreach (var u in e.Usage) {
				if (u == null || u.Length == 0 || u == e.Pos) continue;
				tags.Add(u);
			}
			if (tags.Count > 0) {
				var p = para(1);
				addrun(p, Loc.T("dict.lab.tags"), DRole.Label);
				addrun(p, string.Join(" · ", tags), DRole.Body);
				doc.Blocks.Add(p);
			}
			if (e.Etymology.Length > 0) {
				doc.Blocks.Add(one(Loc.T("dict.lab.etym"), DRole.Label, 0));
				foreach (var line in splitlines(e.Etymology)) doc.Blocks.Add(one(line, DRole.Body, 0));
			}
			foreach (var c in e.Conjugations) {
				var p = para(0);
				addrun(p, Loc.T("dict.lab.conj"), DRole.Label);
				addrun(p, c, DRole.Body);
				doc.Blocks.Add(p);
			}
			if (e.See.Count > 0) {
				var p = para(1);
				addrun(p, Loc.T("dict.lab.see") + " ", DRole.Label);
				var first = true;
				foreach (var s in e.See) {
					if (s == null || s.Length == 0) continue;
					if (!first) addrun(p, " · ", DRole.Body);
					first = false;
					addlink(p, s, s);
				}
				doc.Blocks.Add(p);
			}
			var n = 1;
			string lastpos = null;
			var anySense = false;
			foreach (var s in e.Senses) {
				if (anySense) doc.Blocks.Add(para(4));
				anySense = true;
				if (s.Pos.Length > 0 && s.Pos != e.Pos && s.Pos != lastpos) {
					doc.Blocks.Add(one("▶ " + s.Pos, DRole.Pos, 1));
					lastpos = s.Pos;
				}
				var numbered = false;
				void gloss(string tag, string lemma, string def) {
					if (lemma.Length == 0 && def.Length == 0) return;
					var p = para(0);
					if (!numbered) {
						addrun(p, n + ". ", DRole.Number);
						numbered = true;
						n++;
					}
					if (tag.Length > 0) addrun(p, tag + " ", DRole.Label);
					addrun(p, join2(lemma, def), DRole.Body);
					doc.Blocks.Add(p);
				}
				if (s.Ko.Length > 0) gloss("", s.Ko, "");
				gloss(Loc.T("dict.lab.zh"), s.Zh, s.ZhDef);
				gloss(Loc.T("dict.lab.en"), s.En, s.EnDef);
				gloss(Loc.T("dict.lab.ja"), s.Ja, s.JaDef);
				var linkphrase = e.Dict != "ko";
				foreach (var ph in s.Phrases) {
					if (ph.Text.Length == 0 && ph.Zh.Length == 0) continue;
					var p = para(0);
					addrun(p, Loc.T("dict.lab.phrase") + " ", DRole.Label);
					if (linkphrase && ph.Text.Length > 0) addlink(p, ph.Text, firsttoken(ph.Text));
					else addrun(p, ph.Text, DRole.Body);
					doc.Blocks.Add(p);
					if (ph.Zh.Length > 0) {
						var z = para(0);
						addrun(z, Loc.T("dict.lab.zh") + " ", DRole.Label);
						addrun(z, ph.Zh, DRole.Body);
						doc.Blocks.Add(z);
					}
				}
				if (s.Sentences.Count > 0) doc.Blocks.Add(one(Loc.T("dict.lab.example"), DRole.ExLabel, 0));
				var exlang = speaklang(e.Dict);
				foreach (var ex in s.Sentences) {
					if (ex.Text.Length == 0 && ex.Zh.Length == 0) continue;
					var ep = para(0);
					addrun(ep, "· " + ex.Text, DRole.Example);
					var say = examplesrc(ex.Text);
					if (say.Length > 0) addspeak(ep, say, exlang);
					doc.Blocks.Add(ep);
					if (ex.Zh.Length > 0) {
						var z = para(0);
						addrun(z, Loc.T("dict.lab.zh") + " ", DRole.Label);
						addrun(z, ex.Zh, DRole.Body);
						doc.Blocks.Add(z);
					}
				}
				if (!numbered && s.Phrases.Count == 0 && s.Sentences.Count == 0) {
					var p = para(0);
					addrun(p, n + ". ", DRole.Number);
					n++;
					addrun(p, Loc.T("dict.empty.sense"), DRole.Pron);
					doc.Blocks.Add(p);
				}
				else if (!numbered) {
					doc.Blocks.Add(one(n + ".", DRole.Number, 0));
					n++;
				}
			}
		}
		if (e.Extra.Length > 0 && (zharticle || e.Dict != "zh")) {
			if (doc.Blocks.Count > 0) doc.Blocks.Add(para(6));
			foreach (var line in splitlines(e.Extra)) doc.Blocks.Add(one(line, DRole.Body, 0));
		}
		edetail.Document = doc;
		edetail.ScrollToHome();
	}

	void openselpop() {
		var text = edetail.Selection == null ? "" : edetail.Selection.Text ?? "";
		text = oneline(text);
		if (text.Length == 0) return;
		if (text.Length > 80) text = text.Substring(0, 80);
		seltext = text;
		placepop();
		psel.IsOpen = true;
		showselhits(null, true);
		var g = ++sgen;
		var q = text;
		Task.Run(() => {
			try { return DictDb.Ready ? DictDb.Search(q, "", 8) : new List<DictHit>(); }
			catch { return new List<DictHit>(); }
		}).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				if (g != sgen || !psel.IsOpen) return;
				showselhits(t.Result, false);
			}));
		});
	}

	void placepop() {
		psel.PlacementTarget = this;
		psel.Placement = PlacementMode.Relative;
		var rect = new Rect(12, 12, 0, 16);
		try {
			if (edetail.Selection != null)
				rect = edetail.Selection.Start.GetCharacterRect(LogicalDirection.Forward);
		}
		catch { }
		var below = new Point(12, 40);
		var above = new Point(12, 24);
		try {
			below = edetail.TranslatePoint(new Point(rect.Left, rect.Bottom + 4), this);
			above = edetail.TranslatePoint(new Point(rect.Left, rect.Top), this);
		}
		catch { }
		var x = below.X;
		var y = below.Y;
		const double popW = 360;
		const double popH = 280;
		if (x + popW > ActualWidth - 8) x = ActualWidth - popW - 8;
		if (x < 8) x = 8;
		if (y + popH > ActualHeight - 8) {
			var up = above.Y - popH - 4;
			y = up >= 8 ? up : Math.Max(8, ActualHeight - popH - 8);
		}
		if (y < 8) y = 8;
		psel.HorizontalOffset = x;
		psel.VerticalOffset = y;
	}

	void showselhits(List<DictHit> hits, bool searching) {
		var rows = new List<DictSelRow>();
		if (hits != null) {
			foreach (var h in hits) {
				if (h.Id == 0) continue;
				var word = h.Headword.Length > 0 ? h.Headword : h.Matched;
				var lang = langlabel(h.Dict);
				rows.Add(new DictSelRow {
					Id = h.Id,
					Word = word,
					Title = lang.Length > 0 ? "[" + lang + "] " + word : word,
					Gloss = h.Preview ?? "",
				});
				if (rows.Count >= 8) break;
			}
		}
		lbselwait.Visibility = searching && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		lsel.ItemsSource = rows;
		lsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		spsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	void openhit(DictSelRow row) {
		if (row == null || row.Word.Length == 0) return;
		psel.IsOpen = false;
		wantid = row.Id;
		if ((esearch.Text ?? "") == row.Word) runsearch();
		else {
			suppress = true;
			esearch.Text = row.Word;
			suppress = false;
			queuesearch();
		}
	}

	void searchword(string q) {
		q = oneline(q);
		if (q.Length == 0) return;
		wantid = 0;
		if ((esearch.Text ?? "") == q) runsearch();
		else {
			suppress = true;
			esearch.Text = q;
			suppress = false;
			queuesearch();
		}
	}

	static void copytext(string text) {
		if (string.IsNullOrEmpty(text)) return;
		try { Clipboard.SetText(text); }
		catch { }
	}

	async Task speak(string text, string lang) {
		if (speaking) return;
		if (string.IsNullOrWhiteSpace(text)) {
			lbstatus.Text = lang == "zh" ? Loc.T("dict.nozh") : Loc.T("dict.noword");
			return;
		}
		speaking = true;
		try {
			if (player == null) player = new TtsPlayer();
			var pref = DictTts.For(Options != null ? Options() : null, lang);
			float[] samples = null;
			var sr = 0;
			if (pref.Engine != DictTts.EDGE) {
				var got = await Task.Run(() => DictTts.SynthSapi(text, lang, pref.Sapi)).ConfigureAwait(true);
				if (!got.missing && got.samples != null && got.samples.Length > 0) {
					samples = got.samples;
					sr = got.sampleRate;
				}
				else if (pref.Engine == DictTts.SAPI && !got.missing)
					throw new InvalidOperationException(Loc.T("dict.novoice", langlabel(lang)));
			}
			if (samples == null || samples.Length == 0) {
				if (edge == null) edge = new EdgeOnlineTts();
				edge.SetVoiceName(DictTts.EdgeName(lang, pref.Edge));
				edge.SetRateVolume(1, 100);
				var got = await edge.Synthesize(text).ConfigureAwait(true);
				samples = got.samples;
				sr = got.sampleRate;
			}
			if (samples == null || samples.Length == 0) {
				lbstatus.Text = Loc.T("dict.novoice", langlabel(lang));
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

	static string speaklang(string dict) {
		if (dict == "zh" || dict == "ja" || dict == "ko" || dict == "en") return dict;
		return "en";
	}

	static string langlabel(string dict) {
		if (dict == "en") return Loc.T("dict.filter.en");
		if (dict == "ja") return Loc.T("dict.filter.ja");
		if (dict == "ko") return Loc.T("dict.filter.ko");
		if (dict == "zh") return Loc.T("dict.filter.zh");
		return "";
	}

	static FlowDocument newdoc() {
		return new FlowDocument {
			PagePadding = new Thickness(2),
			FontSize = 13,
			FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
		};
	}

	static Paragraph para(double bottom) {
		return new Paragraph { Margin = new Thickness(0, 0, 0, bottom), LineHeight = 18 };
	}

	static Paragraph one(string text, DRole role, double bottom) {
		var p = para(bottom);
		addrun(p, text, role);
		return p;
	}

	static void addrun(Paragraph p, string text, DRole role) {
		if (text == null || text.Length == 0 || p == null) return;
		var run = new Run(text);
		var b = brush(role);
		if (b != null) run.Foreground = b;
		if (role == DRole.Title) run.FontWeight = FontWeights.SemiBold;
		p.Inlines.Add(run);
	}

	void addspeak(Paragraph p, string text, string lang) {
		if (p == null || string.IsNullOrEmpty(text)) return;
		p.Inlines.Add(new Run(" "));
		var run = new Run("\uE767") {
			FontFamily = new FontFamily("Segoe MDL2 Assets"),
			FontSize = 13,
			Foreground = CSpeak,
		};
		var link = new Hyperlink(run) {
			Foreground = CSpeak,
			TextDecorations = null,
		};
		var say = text;
		var voice = lang;
		link.Click += (_, _) => _ = speak(say, voice);
		p.Inlines.Add(link);
	}

	void addlink(Paragraph p, string label, string query) {
		if (label == null || label.Length == 0 || p == null) return;
		var link = new Hyperlink(new Run(label)) {
			Foreground = CLabel,
			TextDecorations = null,
		};
		var q = query ?? label;
		link.Click += (_, _) => searchword(q);
		p.Inlines.Add(link);
	}

	static Brush brush(DRole role) {
		if (role == DRole.Pron) return CPron;
		if (role == DRole.Pos) return CPos;
		if (role == DRole.Number) return CNumber;
		if (role == DRole.Label) return CLabel;
		if (role == DRole.ExLabel) return CExLabel;
		if (role == DRole.Example) return CExample;
		return null;
	}

	static SolidColorBrush freeze(byte r, byte g, byte b) {
		var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		brush.Freeze();
		return brush;
	}

	static string bracket(string s) {
		if (s.Length == 0) return s;
		if (s[0] == '[' || s[0] == '［') return s;
		return "[" + s + "]";
	}

	static string join2(string a, string b) {
		if (string.IsNullOrEmpty(a)) return b ?? "";
		if (string.IsNullOrEmpty(b)) return a;
		return a + "  " + b;
	}

	static string examplesrc(string text) {
		var t = (text ?? "").Trim().TrimStart('·').Trim();
		var i = t.IndexOf("  ", StringComparison.Ordinal);
		if (i > 0) t = t.Substring(0, i).Trim();
		return t;
	}

	static string firsttoken(string phrase) {
		var t = (phrase ?? "").Trim();
		var i = t.IndexOf(' ');
		if (i < 0) i = t.IndexOf('　');
		if (i <= 0) return t;
		return t.Substring(0, i);
	}

	static List<string> splitlines(string text) {
		var list = new List<string>();
		if (string.IsNullOrEmpty(text)) return list;
		var parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		foreach (var p in parts) list.Add(p);
		return list;
	}

	static string oneline(string text) {
		if (string.IsNullOrWhiteSpace(text)) return "";
		text = text.Replace("🔊", "").Replace("\uE767", "");
		var sb = new StringBuilder(text.Length);
		var sp = false;
		foreach (var c in text) {
			if (c == '\r' || c == '\n' || c == '\t' || c == ' ') {
				sp = sb.Length > 0;
				continue;
			}
			if (sp) { sb.Append(' '); sp = false; }
			sb.Append(c);
		}
		return sb.ToString();
	}

	static bool under(DependencyObject root, DependencyObject node) {
		while (node != null) {
			if (node == root) return true;
			DependencyObject next = null;
			try { next = VisualTreeHelper.GetParent(node); } catch { }
			if (next == null) next = LogicalTreeHelper.GetParent(node);
			node = next;
		}
		return false;
	}
}
