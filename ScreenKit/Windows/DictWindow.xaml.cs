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
	public string Speak { get; set; } = "";
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

/// <summary>英日韩词典：只读 exe 旁的 dict.db。主窗口词典页和独立词典窗口共用这一页。</summary>
public partial class DictWindow : UserControl {
	static readonly Brush CPron = freeze(110, 110, 110);
	static readonly Brush CPos = freeze(136, 48, 168);
	static readonly Brush CNumber = freeze(210, 85, 20);
	static readonly Brush CLabel = freeze(40, 90, 170);
	static readonly Brush CExLabel = freeze(20, 130, 70);
	static readonly Brush CExample = freeze(45, 120, 75);
	static readonly Brush CSpeak = freeze(40, 120, 190);

	TtsPlayer player;
	DispatcherTimer tick;
	int lastedit;
	bool pending;
	bool sqliteasked;
	int gen;
	int dgen;
	int sgen;
	bool filling;
	bool speaking;
	bool suppress;
	long wantid;
	string seltext = "";
	string sellang = "";
	string curword = "";
	string curspeak = "";
	string curlang = "";
	string filter = "";
	ToggleButton[] filts;
	Point downpt;
	bool dragsel;
	Window hostwin;

	/// <summary>选区「翻译」：主窗打开翻译小窗、填入原文并立即翻译。</summary>
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
		lhits.SelectionChanged += (_, _) => {
			if (filling) return;
			if (lhits.SelectedItem is DictRow row) loaddetail(row);
		};
		lhits.AddHandler(Button.ClickEvent, new RoutedEventHandler(onrowspeak));
		bspeak.Click += (_, _) => _ = speak(curspeak, speaklang(curlang));
		bselspeak.Click += (_, _) => _ = speak(seltext, sellang);
		bselsearch.Click += (_, _) => {
			var q = seltext;
			psel.IsOpen = false;
			opensearch(q);
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
		// 双击由文本框自己选中单词，鼠标几乎不动，不会记成拖选。
		edetail.MouseDoubleClick += (_, _) =>
			Dispatcher.BeginInvoke(new Action(openselpop), DispatcherPriority.Background);
		edetail.PreviewMouseRightButtonUp += (_, e) => {
			e.Handled = true;
			openselpop();
		};
		psel.Opened += (_, _) =>
			Dispatcher.BeginInvoke(new Action(placepop), DispatcherPriority.Loaded);
		Loaded += (_, _) => hookhost();
		PreviewMouseDown += (_, e) => {
			if (!psel.IsOpen) return;
			if (e.OriginalSource is DependencyObject d && under(psel.Child, d)) return;
			psel.IsOpen = false;
		};
		filts = new[] { bfiltall, bfiltzh, bfiltja, bfiltko, bfilten };
		wirefilter(bfiltall, "");
		wirefilter(bfiltzh, "zh");
		wirefilter(bfiltja, "ja");
		wirefilter(bfiltko, "ko");
		wirefilter(bfilten, "en");
		bfiltall.IsChecked = true;
		applylang();
		cleardetail();
		return;

		void wirefilter(ToggleButton b, string dict) {
			b.Checked += (_, _) => {
				foreach (var o in filts)
					if (!ReferenceEquals(o, b)) o.IsChecked = false;
				if (filter == dict) return;
				filter = dict;
				queuesearch();
			};
			b.Unchecked += (_, _) => {
				foreach (var o in filts)
					if (o.IsChecked == true) return;
				b.IsChecked = true;
			};
		}

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
		player = null;
	}

	public void FocusSearch() {
		ensuredb();
		try {
			esearch.Focus();
			esearch.SelectAll();
		}
		catch { }
	}

	/// <summary>选区浮层开着时关掉它。没有浮层时返回 false。</summary>
	public bool CloseSel() {
		if (psel == null || !psel.IsOpen) return false;
		psel.IsOpen = false;
		return true;
	}

	internal int HitCount => lhits.Items.Count;

	/// <summary>把外部选区填进搜索框并查询。过长只取前 80 字。id 不为 0 时选中该词条。</summary>
	public void SearchText(string q, long id = 0) {
		q = oneline(q);
		if (q.Length > 80) q = q.Substring(0, 80).Trim();
		searchword(q, id);
	}

	public void ApplyLang() => applylang();

	void applylang() {
		bspeak.Content = Loc.T("dict.speak");
		bspeak.ToolTip = Loc.T("dict.speak.tip");
		bselspeak.Content = Loc.T("dict.speak");
		bselsearch.Content = Loc.T("dict.sel.search");
		bseltr.Content = Loc.T("dict.sel.translate");
		bselcopy.Content = Loc.T("dict.sel.copy");
		lbselwait.Text = Loc.T("dict.sel.searching");
		bfiltall.Content = Loc.T("dict.filter.all");
		bfiltzh.Content = Loc.T("dict.filter.zh");
		bfiltja.Content = Loc.T("dict.filter.ja");
		bfiltko.Content = Loc.T("dict.filter.ko");
		bfilten.Content = Loc.T("dict.filter.en");
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
			if (DictDb.Error == "sqlite") {
				lbstatus.Text = Loc.T("dict.nosqlite");
				if (!sqliteasked) {
					sqliteasked = true;
					if (FeaturePrompt.EnsureSqlite(Window.GetWindow(this))) {
						sqliteasked = false;
						if (DictDb.Init(path)) {
							lbstatus.Text = Loc.T("dict.ready");
							return;
						}
					}
				}
				return;
			}
			lbstatus.Text = DictDb.Error == "missing" ? Loc.T("dict.missing") : DictDb.Error;
			return;
		}
		lbstatus.Text = Loc.T("dict.ready");
	}

	void runsearch() {
		ensuredb();
		if (!DictDb.Ready) return;
		var q = esearch.Text ?? "";
		var dict = filter ?? "";
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
		var took = new int[1];
		Task.Run(() => {
			var t0 = Environment.TickCount;
			try {
				var found = DictDb.Search(q, dict, 80);
				took[0] = unchecked(Environment.TickCount - t0);
				return found;
			}
			catch (Exception ex) {
				took[0] = unchecked(Environment.TickCount - t0);
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
				showhits(hits, took[0]);
			}));
		});
	}

	void showhits(List<DictHit> hits, int ms) {
		var rows = new List<DictRow>(hits.Count);
		foreach (var h in hits) {
			if (h.Id == 0) continue;
			var word = h.Headword.Length > 0 ? h.Headword : h.Matched;
			var shown = listword(h.Dict, word, h.Kanji);
			var code = listcode(h.Dict);
			var title = code.Length > 0 ? "[" + code + "] " + shown : shown;
			var preview = h.Preview ?? "";
			if (h.Via.Length > 0) {
				var via = Loc.T("dict.via", h.Via);
				preview = preview.Length > 0 ? preview + "  ·  " + via : via;
			}
			rows.Add(new DictRow {
				Id = h.Id,
				Dict = h.Dict ?? "",
				Word = word,
				Speak = h.Dict == "ja" ? DictDb.JaSpeak(word, h.Kanji, h.Reading) : SpeakHead(word, h.Dict),
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
		if (ms < 0) ms = 0;
		lbstatus.Text = Loc.T("dict.count", rows.Count, ms);
		if (rows.Count > 0) loaddetail(rows[pick]);
		else cleardetail();
	}

	void onrowspeak(object sender, RoutedEventArgs e) {
		if (e.OriginalSource is not Button b) return;
		if (b.DataContext is not DictRow row) return;
		e.Handled = true;
		var say = row.Speak;
		if (string.IsNullOrEmpty(say)) say = SpeakHead(row.Word, row.Dict);
		_ = speak(say, speaklang(row.Dict));
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
		curspeak = "";
		curlang = "";
		edetail.Document = newdoc();
	}

	void filldetail(DictEntry e) {
		if (e == null) { cleardetail(); return; }
		psel.IsOpen = false;
		curlang = e.Dict ?? "";
		curword = e.Word.Length > 0 ? e.Word : e.Headword;
		curspeak = curlang == "ja" ? DictDb.JaSpeak(curword, e.Kanji, e.Pron) : SpeakHead(curword, curlang);
		var doc = newdoc();
		var entrylang = speaklang(curlang);
		var head = para(2);
		addrun(head, curword, DRole.Title, entrylang);
		if (e.Kanji.Length > 0 && e.Kanji != curword) addrun(head, "  " + e.Kanji, DRole.Body, entrylang);
		doc.Blocks.Add(head);
		var reading = e.Pron.Length > 0 ? e.Pron : e.Reading;
		if (reading.Length > 0) doc.Blocks.Add(one(bracket(reading), DRole.Pron, 1, entrylang));
		var zharticle = e.Dict == "zh" && e.Extra.Length > 0;
		if (e.Pos.Length > 0) doc.Blocks.Add(one(e.Pos, DRole.Pos, 1, TextLang(e.Pos, entrylang)));
		if (!zharticle) {
			var tags = new List<string>();
			foreach (var u in e.Usage) {
				if (u == null || u.Length == 0 || u == e.Pos) continue;
				tags.Add(u);
			}
			if (tags.Count > 0) {
				var p = para(1);
				addrun(p, Loc.T("dict.lab.tags"), DRole.Label);
				addrun(p, string.Join(" · ", tags), DRole.Body, TextLang(string.Join(" · ", tags), entrylang));
				doc.Blocks.Add(p);
			}
			if (e.Etymology.Length > 0) {
				doc.Blocks.Add(one(Loc.T("dict.lab.etym"), DRole.Label, 0));
				foreach (var line in splitlines(e.Etymology))
					doc.Blocks.Add(one(line, DRole.Body, 0, TextLang(line, entrylang)));
			}
			foreach (var c in e.Conjugations) {
				var p = para(0);
				addrun(p, Loc.T("dict.lab.conj"), DRole.Label);
				var firstpart = true;
				foreach (var part in c.Split('　')) {
					if (!firstpart) addrun(p, "　", DRole.Body);
					firstpart = false;
					addrun(p, part, DRole.Body, TextLang(part, entrylang));
				}
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
					addlink(p, s, s, entrylang);
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
					doc.Blocks.Add(one("▶ " + s.Pos, DRole.Pos, 1, TextLang(s.Pos, entrylang)));
					lastpos = s.Pos;
				}
				var numbered = false;
				void gloss(string tag, string lemma, string def, string lang) {
					if (lemma.Length == 0 && def.Length == 0) return;
					var p = para(0);
					if (!numbered) {
						addrun(p, n + ". ", DRole.Number);
						numbered = true;
						n++;
					}
					if (tag.Length > 0) addrun(p, tag + " ", DRole.Label, lang);
					addrun(p, join2(lemma, def), DRole.Body, lang);
					doc.Blocks.Add(p);
				}
				if (s.Ko.Length > 0) gloss("", s.Ko, "", "ko");
				gloss(Loc.T("dict.lab.zh"), s.Zh, s.ZhDef, "zh");
				gloss(Loc.T("dict.lab.en"), s.En, s.EnDef, "en");
				gloss(Loc.T("dict.lab.ja"), s.Ja, s.JaDef, "ja");
				var linkphrase = e.Dict != "ko";
				foreach (var ph in s.Phrases) {
					if (ph.Text.Length == 0 && ph.Zh.Length == 0) continue;
					var p = para(0);
					addrun(p, Loc.T("dict.lab.phrase") + " ", DRole.Label);
					if (linkphrase && ph.Text.Length > 0) addlink(p, ph.Text, firsttoken(ph.Text), entrylang);
					else addrun(p, ph.Text, DRole.Body, entrylang);
					// 韩语词组读原文，和例句同一颗发音按钮。中文释义在下一行，不读。
					if (!linkphrase) {
						var say = examplesrc(ph.Text);
						if (say.Length > 0) addspeak(p, say, entrylang);
					}
					doc.Blocks.Add(p);
					if (ph.Zh.Length > 0) {
						var z = para(0);
						addrun(z, Loc.T("dict.lab.zh") + " ", DRole.Label, "zh");
						addrun(z, ph.Zh, DRole.Body, "zh");
						doc.Blocks.Add(z);
					}
				}
				if (s.Sentences.Count > 0) doc.Blocks.Add(one(Loc.T("dict.lab.example"), DRole.ExLabel, 0));
				var exlang = entrylang;
				foreach (var ex in s.Sentences) {
					if (ex.Text.Length == 0 && ex.Zh.Length == 0) continue;
					var ep = para(0);
					addrun(ep, "· " + ex.Text, DRole.Example, exlang);
					var say = examplesrc(ex.Text);
					if (say.Length > 0) addspeak(ep, say, exlang);
					doc.Blocks.Add(ep);
					if (ex.Zh.Length > 0) {
						var z = para(0);
						addrun(z, Loc.T("dict.lab.zh") + " ", DRole.Label, "zh");
						addrun(z, ex.Zh, DRole.Body, "zh");
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
			foreach (var line in splitlines(e.Extra))
				doc.Blocks.Add(one(line, DRole.Body, 0, zharticle ? "zh" : TextLang(line, entrylang)));
		}
		edetail.Document = doc;
		edetail.ScrollToHome();
	}

	void openselpop() {
		var text = edetail.Selection == null ? "" : edetail.Selection.Text ?? "";
		text = oneline(text);
		if (text.Length == 0) return;
		seltext = text;
		sellang = langofselection(text);
		var q = text.Length > 80 ? text.Substring(0, 80) : text;
		placepop();
		psel.IsOpen = true;
		showselhits(null, true);
		var g = ++sgen;
		Task.Run(() => {
			try { return DictDb.Search(q, "", 8); }
			catch { return new List<DictHit>(); }
		}).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				if (g != sgen || !psel.IsOpen) return;
				showselhits(t.Result, false);
			}));
		});
	}

	Rect selbox() {
		try {
			var sel = edetail.Selection;
			if (sel == null || sel.IsEmpty) return Rect.Empty;
			var a = sel.Start.GetCharacterRect(LogicalDirection.Forward);
			var b = sel.End.GetCharacterRect(LogicalDirection.Backward);
			if (a.IsEmpty && b.IsEmpty) return Rect.Empty;
			if (a.IsEmpty) return b;
			if (b.IsEmpty) return a;
			var top = Math.Min(a.Top, b.Top);
			var bottom = Math.Max(a.Bottom, b.Bottom);
			var left = a.Top <= b.Top ? a.Left : b.Left;
			var right = Math.Max(a.Right, b.Right);
			return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
		}
		catch { return Rect.Empty; }
	}

	Size popsize() {
		var w = 300.0;
		var h = 72.0;
		if (psel.Child is FrameworkElement fe) {
			fe.Measure(new Size(360, 900));
			if (fe.DesiredSize.Width > 1) w = fe.DesiredSize.Width;
			if (fe.DesiredSize.Height > 1) h = fe.DesiredSize.Height;
		}
		return new Size(w, h);
	}

	void placepop() {
		psel.PlacementTarget = this;
		psel.Placement = PlacementMode.Relative;
		var box = selbox();
		var view = new Rect(0, 0, Math.Max(1, edetail.ActualWidth), Math.Max(1, edetail.ActualHeight));
		var vis = Rect.Intersect(box, view);
		if (vis.IsEmpty) vis = box.IsEmpty ? new Rect(12, 12, 1, 16) : box;
		Point tl, br;
		try {
			tl = edetail.TranslatePoint(vis.TopLeft, this);
			br = edetail.TranslatePoint(vis.BottomRight, this);
		}
		catch { return; }
		var pop = popsize();
		const double gap = 6;
		var yBelow = br.Y + gap;
		var yAbove = tl.Y - pop.Height - gap;
		var fitBelow = yBelow + pop.Height <= ActualHeight - 4;
		var fitAbove = yAbove >= 4;
		double y;
		if (fitBelow) y = yBelow;
		else if (fitAbove) y = yAbove;
		else {
			var roomBelow = ActualHeight - yBelow;
			y = roomBelow >= tl.Y ? yBelow : Math.Max(4, yAbove);
		}
		var x = tl.X;
		if (x + pop.Width > ActualWidth - 8) x = ActualWidth - pop.Width - 8;
		if (x < 8) x = 8;
		if (y < 4) y = 4;
		psel.HorizontalOffset = x;
		psel.VerticalOffset = y;
	}

	/// <summary>浮窗在竖直方向是否压住选区。供命令行检查。</summary>
	internal bool PopOverlapsSel() {
		if (psel == null || !psel.IsOpen) return false;
		var box = selbox();
		if (box.IsEmpty) return false;
		var view = new Rect(0, 0, Math.Max(1, edetail.ActualWidth), Math.Max(1, edetail.ActualHeight));
		var vis = Rect.Intersect(box, view);
		if (vis.IsEmpty) vis = box;
		Point tl, br;
		try {
			tl = edetail.TranslatePoint(vis.TopLeft, this);
			br = edetail.TranslatePoint(vis.BottomRight, this);
		}
		catch { return false; }
		var top = psel.VerticalOffset;
		var bot = top + popsize().Height;
		return bot > tl.Y + 1 && top < br.Y - 1;
	}

	void showselhits(List<DictHit> hits, bool searching) {
		var rows = new List<DictSelRow>();
		if (hits != null) {
			foreach (var h in hits) {
				if (h.Id == 0) continue;
				var word = h.Headword.Length > 0 ? h.Headword : h.Matched;
				var shown = listword(h.Dict, word, h.Kanji);
				var code = listcode(h.Dict);
				rows.Add(new DictSelRow {
					Id = h.Id,
					Word = word,
					Title = code.Length > 0 ? "[" + code + "] " + shown : shown,
					Gloss = h.Preview ?? "",
				});
				if (rows.Count >= 8) break;
			}
		}
		lbselwait.Visibility = searching && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		lsel.ItemsSource = rows;
		lsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		spsel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		if (psel.IsOpen) placepop();
	}

	void hookhost() {
		var w = Window.GetWindow(this);
		if (w == null || ReferenceEquals(w, hostwin)) return;
		if (hostwin != null) hostwin.Deactivated -= onhostoff;
		hostwin = w;
		hostwin.Deactivated += onhostoff;
	}

	void onhostoff(object sender, EventArgs e) {
		if (psel == null || !psel.IsOpen) return;
		var w = hostwin;
		Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
			if (!psel.IsOpen) return;
			if (w != null && w.IsActive) return;
			psel.IsOpen = false;
		}));
	}

	void openhit(DictSelRow row) {
		if (row == null || row.Word.Length == 0) return;
		psel.IsOpen = false;
		opensearch(row.Word, row.Id);
	}

	void opensearch(string q, long id = 0) {
		q = oneline(q);
		if (q.Length == 0) return;
		if (q.Length > 80) q = q.Substring(0, 80).Trim();
		var win = new DictHostWindow(q, Options, OnTranslate, id);
		win.Show();
		win.Activate();
	}

	void searchword(string q, long id = 0) {
		q = oneline(q);
		if (q.Length == 0) return;
		wantid = id;
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
			var path = await DictTts.EnsureWav(pref, lang, text).ConfigureAwait(true);
			player.PlayFile(path);
			lbstatus.Text = Loc.T("dict.speaking", langlabel(lang));
		}
		catch (Exception ex) {
			lbstatus.Text = ex.Message == "novoice"
				? Loc.T("dict.novoice", langlabel(lang))
				: ex.Message;
		}
		finally { speaking = false; }
	}

	/// <summary>日语列表把别表记接在主表记后面。再搜索仍用主表记。发音见 JaSpeak。</summary>
	static string listword(string dict, string word, string kanji) {
		if (dict != "ja") return word ?? "";
		var shown = DictDb.JaForms(word, kanji);
		return shown.Length > 0 ? shown : (word ?? "");
	}

	/// <summary>日语、韩语词头只读到第一个逗号之前。英文和中文整段照读。</summary>
	internal static string SpeakHead(string text, string lang) {
		if (string.IsNullOrEmpty(text)) return "";
		if (lang != "ja" && lang != "ko") return text;
		var i = text.IndexOf(',');
		var j = text.IndexOf('，');
		var cut = i < 0 ? j : j < 0 ? i : Math.Min(i, j);
		if (cut <= 0) return text;
		return text.Substring(0, cut).Trim();
	}

	static string speaklang(string dict) {
		if (dict == "zh" || dict == "ja" || dict == "ko" || dict == "en") return dict;
		return "en";
	}

	/// <summary>划词发音的语言。已标语言的文字按标注。其余按文字：谚文算韩语，没有假名的汉字算中文，拉丁字母算英语。只有日语词条才把假名判成日语。汉、英、韩词条不判日语。</summary>
	string langofselection(string text) {
		var allowJa = speaklang(curlang) == "ja";
		var score = new int[4];
		var untagged = new StringBuilder();
		var any = false;
		var sel = edetail.Selection;
		if (sel != null && !sel.IsEmpty && sel.Start != null && sel.End != null) {
			var seen = new HashSet<Run>();
			var p = sel.Start;
			while (p != null && p.CompareTo(sel.End) < 0) {
				if (p.Parent is Run run && seen.Add(run)) {
					var bit = overlaptext(run, sel);
					if (bit.Length > 0) {
						any = true;
						var i = langindex(run.Tag as string);
						if (i >= 0 && (allowJa || i != 2)) score[i] += bit.Length;
						else untagged.Append(bit);
					}
				}
				var next = p.GetNextContextPosition(LogicalDirection.Forward);
				if (next == null || next.CompareTo(p) <= 0) break;
				p = next;
			}
		}
		addscript(score, any ? untagged.ToString() : text, allowJa);
		return bestlang(score, curlang);
	}

	static string overlaptext(Run run, TextSelection sel) {
		var rs = run.ContentStart;
		var re = run.ContentEnd;
		if (rs == null || re == null) return "";
		var s = sel.Start.CompareTo(rs) > 0 ? sel.Start : rs;
		var e = sel.End.CompareTo(re) < 0 ? sel.End : re;
		if (s.CompareTo(e) >= 0) return "";
		return new TextRange(s, e).Text ?? "";
	}

	/// <summary>没有标注时按文字判断。fallback 不是日语时不返回 ja。判断不出就用 fallback。</summary>
	internal static string TextLang(string text, string fallback) {
		var score = new int[4];
		addscript(score, text, speaklang(fallback) == "ja");
		return bestlang(score, fallback);
	}

	static void addscript(int[] score, string text, bool allowJa) {
		if (string.IsNullOrEmpty(text)) return;
		var han = 0;
		var kana = 0;
		var hang = 0;
		var lat = 0;
		foreach (var c in text) {
			if (iskana(c)) kana++;
			else if (c >= '\uAC00' && c <= '\uD7A3') hang++;
			else if (ishan(c)) han++;
			else if (islatin(c)) lat++;
		}
		if (kana > 0 && allowJa) score[2] += kana + han;
		else score[0] += han;
		score[3] += hang;
		score[1] += lat;
	}

	static string bestlang(int[] score, string fallback) {
		var langs = new[] { "zh", "en", "ja", "ko" };
		var best = -1;
		var n = 0;
		for (var i = 0; i < 4; i++) {
			if (score[i] <= n) continue;
			n = score[i];
			best = i;
		}
		if (best < 0) return speaklang(fallback);
		return langs[best];
	}

	static int langindex(string lang) {
		if (lang == "zh") return 0;
		if (lang == "en") return 1;
		if (lang == "ja") return 2;
		if (lang == "ko") return 3;
		return -1;
	}

	static bool iskana(char c) {
		if (c >= '\u3040' && c <= '\u30FF') return true;
		return c >= '\uFF66' && c <= '\uFF9D';
	}

	static bool ishan(char c) {
		if (c >= '\u4E00' && c <= '\u9FFF') return true;
		return c >= '\u3400' && c <= '\u4DBF';
	}

	static bool islatin(char c) {
		if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z') return true;
		if (c >= '\uFF21' && c <= '\uFF3A') return true;
		return c >= '\uFF41' && c <= '\uFF5A';
	}

	static string listcode(string dict) {
		if (dict == "zh" || dict == "ja" || dict == "ko" || dict == "en") return dict;
		return "";
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

	static Paragraph one(string text, DRole role, double bottom, string lang = null) {
		var p = para(bottom);
		addrun(p, text, role, lang);
		return p;
	}

	static void addrun(Paragraph p, string text, DRole role, string lang = null) {
		if (text == null || text.Length == 0 || p == null) return;
		var run = new Run(text);
		var b = brush(role);
		if (b != null) run.Foreground = b;
		if (role == DRole.Title) run.FontWeight = FontWeights.SemiBold;
		var i = langindex(lang);
		if (i >= 0) run.Tag = lang;
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

	void addlink(Paragraph p, string label, string query, string lang) {
		if (label == null || label.Length == 0 || p == null) return;
		var run = new Run(label);
		var i = langindex(lang);
		if (i >= 0) run.Tag = lang;
		var link = new Hyperlink(run) {
			Foreground = CLabel,
			TextDecorations = null,
		};
		var q = query ?? label;
		link.Click += (_, _) => opensearch(q);
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
