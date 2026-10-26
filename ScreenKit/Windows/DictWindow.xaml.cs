using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenKit;

public sealed class DictRow : INotifyPropertyChanged {
	string title = "", preview = "", word = "", speak = "";
	public long Id { get; set; }
	public string Dict { get; set; } = "";
	public string Via { get; set; } = "";
	/// <summary>标题和预览已从词条读回，不再重复读盘。</summary>
	public bool Meta { get; set; }
	public string Word { get => word; set => notify(ref word, value, nameof(Word)); }
	public string Speak { get => speak; set => notify(ref speak, value, nameof(Speak)); }
	public string Title { get => title; set => notify(ref title, value, nameof(Title)); }
	public string Preview { get => preview; set => notify(ref preview, value, nameof(Preview)); }
	public event PropertyChangedEventHandler PropertyChanged;

	void notify(ref string field, string value, string name) {
		if (field == value) return;
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}

	public override string ToString() => Title ?? "";
}

/// <summary>形态变化页的一行：组标题行，或「形态名 / 变形」两列。</summary>
public sealed class ConjItem {
	public string G { get; set; } = "";
	public string L { get; set; } = "";
	public string V { get; set; } = "";
	public Visibility HeadVis { get; set; } = Visibility.Collapsed;
	public Visibility RowVis { get; set; } = Visibility.Visible;
}

/// <summary>词条能变形的词性：动词、形容词、名词；日语动词再带五段/一段/サ変/カ変。</summary>
sealed class ConjCat {
	public string Cat = "", Kind = "", Sub = "";
}

/// <summary>一个「形态变化」标签页：自己的过滤框、计数和列表。</summary>
sealed class ConjPane {
	public TabItem Tab;
	public TextBlock Meta;
	public TextBox Q;
	public TextBlock Cnt;
	public ListBox List;
	public List<ConjRow> Rows;
}

sealed class DictSelRow {
	public long Id { get; set; }
	public string Word { get; set; } = "";
	public string Title { get; set; } = "";
	public string Gloss { get; set; } = "";
	public override string ToString() => Title ?? "";
}

enum DRole { Body, Title, Pron, Pos, Number, Label, ExLabel, Example }

/// <summary>英日韩词典：只读 exe 旁的 dict3.bin。主窗口词典页和独立词典窗口共用这一页。</summary>
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
	int gen;
	int dgen;
	bool filling;
	bool speaking;
	bool suppress;
	long wantid;
	string curword = "";
	string curspeak = "";
	string curlang = "";
	string filter = "";
	string selfilter = "";
	ToggleButton[] filts;
	Point downpt;
	bool dragsel;
	Window hostwin;
	SelBarWindow outwin;
	int ogen;
	int trgen;
	bool trinit;
	string trkeep;
	string trsel;
	CancellationTokenSource trcts;
	int readgen;
	string readsel;
	bool readon;
	CancellationTokenSource readcts;
	string outtext = "";
	string outlang = "";
	Rect outbox;
	bool selbarsync;
	bool outdict;
	bool keepscroll;
	List<ConjCat> concats = new();
	List<ConjPane> conjpanes = new();
	int conjgen;

	/// <summary>选区「翻译」：主窗打开翻译小窗、填入原文并立即翻译。</summary>
	public Action<string> OnTranslate;
	/// <summary>划词浮窗翻译页的语言列表。参数是当前源语言，auto 表示还没指定。</summary>
	internal static Func<string, SelTrPack> TrPack;
	/// <summary>划词浮窗翻译。src 为 auto 时按文字识别，dst 为母语或用户选的语言。</summary>
	internal static Func<string, string, string, CancellationToken, Task<string>> TrRun;
	/// <summary>词典页上的划词浮窗开关。</summary>
	public Action<bool> SelBarChanged;
	/// <summary>当前选项。发音读这里的词典引擎和发音人。</summary>
	public Func<OcrOptions> Options;

	public DictWindow() {
		InitializeComponent();
		initui();
	}

	void initui() {
		PreviewKeyDown += (_, e) => {
			if (e.Key != Key.Escape || !CloseSel()) return;
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
		dtabc.SelectionChanged += (_, _) => {
			if (dtabc.SelectedIndex > 0) loadconj(dtabc.SelectedIndex - 1);
		};
		lhits.AddHandler(Button.ClickEvent, new RoutedEventHandler(onrowspeak));
		bspeak.Click += (_, _) => _ = speak(curspeak, speaklang(curlang));
		bexport.Click += (_, _) => exporthits();
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
		Loaded += (_, _) => {
			hookhost();
			SelBar.WatchDetail(edetail);
		};
		Unloaded += (_, _) => SelBar.UnwatchDetail(edetail);
		IsVisibleChanged += (_, _) => {
			if (!IsVisible && !reading()) HideOutside();
		};
		PreviewMouseDown += (_, _) => {
			if (reading()) return;
			HideOutside();
		};
		cbselbar.Checked += (_, _) => {
			if (!selbarsync) SelBarChanged?.Invoke(true);
		};
		cbselbar.Unchecked += (_, _) => {
			if (!selbarsync) SelBarChanged?.Invoke(false);
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
				keepscroll = true;
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
		HideOutside();
		SelBar.UnwatchDetail(edetail);
		try { outwin?.Close(); } catch { }
		outwin = null;
	}

	public void SetSelBarChecked(bool on) {
		selbarsync = true;
		try { cbselbar.IsChecked = on; }
		finally { selbarsync = false; }
		try { outwin?.SetSelOn(on); } catch { }
	}

	internal void HideSelSwitch() {
		cbselbar.Visibility = Visibility.Collapsed;
	}

	public void FocusSearch() {
		try {
			esearch.Focus();
			esearch.SelectAll();
		}
		catch { }
	}

	/// <summary>划词浮窗开着时关掉它。没有浮窗时返回 false。</summary>
	public bool CloseSel() {
		if (outwin == null || !outwin.IsVisible) return false;
		HideOutside();
		try { SelBar.DismissShown(); } catch { }
		return true;
	}

	internal bool SelShown => outwin != null && outwin.IsVisible;

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
		bexport.Content = Loc.T("dict.export");
		bexport.ToolTip = Loc.T("dict.export.tip");
		cbselbar.Content = Loc.T("dict.selbar");
		cbselbar.ToolTip = Loc.T("dict.selbar.tip");
		try { outwin?.ApplyText(); } catch { }
		bfiltall.Content = Loc.T("dict.filter.all");
		bfiltzh.Content = Loc.T("dict.filter.zh");
		bfiltja.Content = Loc.T("dict.filter.ja");
		bfiltko.Content = Loc.T("dict.filter.ko");
		bfilten.Content = Loc.T("dict.filter.en");
		retitles();
	}

	void retitles() {
		if (lhits.ItemsSource is not IEnumerable<DictRow> rows) return;
		foreach (var row in rows) row.Title = listtitle(row.Dict, stripcode(row.Title));
	}

	int searchlimit() {
		var n = 1000;
		try {
			var o = Options != null ? Options() : null;
			if (o != null && o.DictLimit > 0) n = o.DictLimit;
		}
		catch { }
		if (n < 1) n = 1;
		if (n > DictDb.SEARCH_MAX) n = DictDb.SEARCH_MAX;
		return n;
	}

	void queuesearch() {
		lastedit = Environment.TickCount;
		pending = true;
		if (tick != null && !tick.IsEnabled) tick.Start();
	}

	void ensuredb() {
		var o = Options != null ? Options() : null;
		if (o != null && !o.ModDict) {
			try { DictDb.Close(); } catch { }
			return;
		}
		if (DictDb.Ready) {
			if (string.IsNullOrWhiteSpace(lbstatus.Text))
				lbstatus.Text = Loc.T("dict.ready");
			return;
		}
		var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict3.bin");
		if (!DictDb.Init(path)) {
			lbstatus.Text = DictDb.Error == "missing" ? Loc.T("dict.missing") : DictDb.Error;
			return;
		}
		lbstatus.Text = Loc.T("dict.ready");
	}

	void runsearch() {
		var q = esearch.Text ?? "";
		var dict = filter ?? "";
		var keep = keepscroll;
		keepscroll = false;
		if (string.IsNullOrWhiteSpace(q)) {
			gen++;
			filling = true;
			lhits.ItemsSource = null;
			filling = false;
			cleardetail();
			if (DictDb.Ready) lbstatus.Text = Loc.T("dict.ready");
			return;
		}
		ensuredb();
		if (!DictDb.Ready) return;
		var g = ++gen;
		lbstatus.Text = Loc.T("dict.searching");
		var took = new int[1];
		var limit = searchlimit();
		Task.Run(() => {
			var t0 = Environment.TickCount;
			try {
				var found = DictDb.SearchBrief(q, dict, limit);
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
				showhits(hits, took[0], keep);
			}));
		});
	}

	void showhits(List<DictHit> hits, int ms, bool keep) {
		var keepid = keep && lhits.SelectedItem is DictRow old ? old.Id : 0L;
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
			var meta = h.Headword.Length > 0;
			// 标题和预览还没读的行先用一个不换行空格占住第二行，免得回填时整行变高、列表跳动。
			if (!meta && preview.Length == 0) preview = "\u00A0";
			rows.Add(new DictRow {
				Id = h.Id,
				Dict = h.Dict ?? "",
				Word = word,
				Speak = h.Dict == "ja" ? DictDb.JaSpeak(word, h.Kanji, h.Reading) : SpeakHead(word, h.Dict),
				Title = title,
				Preview = preview,
				Via = h.Via ?? "",
				Meta = meta,
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
		else if (keepid != 0) {
			for (var i = 0; i < rows.Count; i++) {
				if (rows[i].Id != keepid) continue;
				pick = i;
				break;
			}
		}
		filling = true;
		lhits.ItemsSource = rows;
		if (rows.Count > 0) lhits.SelectedIndex = pick;
		filling = false;
		if (ms < 0) ms = 0;
		lbstatus.Text = Loc.T("dict.count", rows.Count, ms);
		if (rows.Count > 0) loaddetail(rows[pick]);
		else cleardetail();
		keepview(rows, pick, keepid, keep);
	}

	void onrowload(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement fe && fe.DataContext is DictRow row) fillrow(row);
	}

	/// <summary>行第一次出现时才读标题和预览，读完直接更新这一行。</summary>
	void fillrow(DictRow row) {
		if (row == null || row.Meta) return;
		row.Meta = true;
		if (!DictDb.HitMeta(row.Id, out var word, out var kanji, out var pron, out var preview)) return;
		var shown = listword(row.Dict, word, kanji);
		var code = listcode(row.Dict);
		row.Word = word;
		row.Title = code.Length > 0 ? "[" + code + "] " + shown : shown;
		var pv = preview;
		if (row.Via.Length > 0) {
			var via = Loc.T("dict.via", row.Via);
			pv = pv.Length > 0 ? pv + "  ·  " + via : via;
		}
		row.Preview = pv;
		row.Speak = row.Dict == "ja" ? DictDb.JaSpeak(word, kanji, pron) : SpeakHead(word, row.Dict);
	}

	/// <summary>换词典语言后把列表放回原处：原来那条词条还在新结果里就仍选中它、滚到它；不在就回到顶部。</summary>
	void keepview(List<DictRow> rows, int pick, long keepid, bool keep) {
		if (!keep) return;
		var same = keepid != 0 && pick > 0 && pick < rows.Count && rows[pick].Id == keepid;
		Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
			try {
				if (same) {
					lhits.ScrollIntoView(rows[pick]);
					return;
				}
				var sv = scrollof(lhits);
				if (sv != null) sv.ScrollToVerticalOffset(0);
			}
			catch { }
		}));
	}

	static ScrollViewer scrollof(DependencyObject d) {
		if (d == null) return null;
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) {
			var c = VisualTreeHelper.GetChild(d, i);
			if (c is ScrollViewer sv) return sv;
			var inner = scrollof(c);
			if (inner != null) return inner;
		}
		return null;
	}

	void onrowspeak(object sender, RoutedEventArgs e) {
		if (e.OriginalSource is not Button b) return;
		if (b.DataContext is not DictRow row) return;
		e.Handled = true;
		var say = row.Speak;
		if (string.IsNullOrEmpty(say)) say = SpeakHead(row.Word, row.Dict);
		_ = speak(say, speaklang(row.Dict));
	}

	/// <summary>形态变化页每行的发音：读这一行的变形结果。</summary>
	void onconjspeak(object sender, RoutedEventArgs e) {
		if (e.OriginalSource is not Button b) return;
		if (b.DataContext is not ConjItem item) return;
		e.Handled = true;
		if (string.IsNullOrEmpty(item.V)) return;
		_ = speak(item.V, speaklang(curlang));
	}

	int exportgen;
	bool exporting;
	const string EXPORT_TABLE_CSS =
		"<style>\r\n" +
		"table,thead,tbody{display:block;width:100%}\r\n" +
		"tr{display:flex;width:100%;border-bottom:1px solid #ccc}\r\n" +
		"th,td{padding:6px 10px;text-align:left;vertical-align:top}\r\n" +
		".w{flex:0 0 auto;border-right:1px solid #ccc}\r\n" +
		".d{flex:1;white-space:pre-wrap;min-width:0}\r\n" +
		"thead .w,thead .d{font-weight:bold;background:#f3f3f3}\r\n" +
		"</style>\r\n";

	void exporthits() {
		if (exporting) return;
		ensuredb();
		if (!DictDb.Ready) return;
		var q = esearch.Text ?? "";
		if (string.IsNullOrWhiteSpace(q)) {
			lbstatus.Text = Loc.T("dict.export.empty");
			return;
		}
		var dict = filter ?? "";
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Filter = "txt (*.txt)|*.txt|csv (*.csv)|*.csv|html (*.html)|*.html|markdown (*.md)|*.md",
			FileName = exportname(q),
			AddExtension = true,
			DefaultExt = ".txt",
			OverwritePrompt = true,
		};
		if (dlg.ShowDialog() != true) return;
		var path = dlg.FileName;
		var kind = exportkind(path);
		exporting = true;
		bexport.IsEnabled = false;
		var gen = ++exportgen;
		lbstatus.Text = Loc.T("dict.export.search");
		Task.Run(() => writeexport(path, kind, q, dict, gen));
	}

	void writeexport(string path, string kind, string q, string dict, int gen) {
		try {
			var ids = DictDb.SearchIds(q, dict);
			var table = kind == "html" || kind == "md";
			var enc = new UTF8Encoding(kind == "csv");
			using (var w = new StreamWriter(path, false, enc)) {
				if (kind == "html") {
					w.Write("<!DOCTYPE html>\r\n<html>\r\n<head><meta charset=\"utf-8\"><title>");
					w.Write(htmlesc(q.Trim()));
					w.Write("</title>\r\n");
					w.Write(EXPORT_TABLE_CSS);
					w.Write("</head>\r\n<body>\r\n");
				}
				else if (kind == "md") w.Write(EXPORT_TABLE_CSS);
				else if (kind == "csv") w.Write("dict,headword,detail\r\n");
				if (table) {
					w.Write("<table>\r\n<thead><tr><th class=\"w\">");
					w.Write(htmlesc(Loc.T("dict.export.col.word")));
					w.Write("</th><th class=\"d\">");
					w.Write(htmlesc(Loc.T("dict.export.col.detail")));
					w.Write("</th></tr></thead>\r\n<tbody>\r\n");
				}
				var n = 0;
				foreach (var id in ids) {
					if (gen != exportgen) return;
					var e = DictDb.Get(id);
					if (e == null) continue;
					var text = detailplain(e);
					var word = e.Word.Length > 0 ? e.Word : e.Headword;
					if (kind == "txt") {
						if (n > 0) w.Write("\r\n\r\n");
						w.Write(text);
					}
					else if (kind == "csv") {
						w.Write(csvcell(e.Dict));
						w.Write(',');
						w.Write(csvcell(word));
						w.Write(',');
						w.Write(csvcell(text));
						w.Write("\r\n");
					}
					else {
						w.Write("<tr><td class=\"w\">");
						w.Write(htmlesc(word));
						w.Write("</td><td class=\"d\">");
						w.Write(htmlesc(text));
						w.Write("</td></tr>\r\n");
					}
					n++;
					if (n % 20 == 0 || n == ids.Count) {
						var done = n;
						var total = ids.Count;
						Dispatcher.BeginInvoke(new Action(() => {
							if (gen != exportgen) return;
							lbstatus.Text = Loc.T("dict.export.busy", done, total);
						}));
					}
				}
				if (table) w.Write("</tbody></table>\r\n");
				if (kind == "html") w.Write("</body>\r\n</html>\r\n");
				var count = n;
				Dispatcher.BeginInvoke(new Action(() => {
					if (gen != exportgen) return;
					lbstatus.Text = Loc.T("dict.export.done", count);
				}));
			}
		}
		catch (Exception ex) {
			var msg = ex.Message;
			Dispatcher.BeginInvoke(new Action(() => {
				if (gen != exportgen) return;
				lbstatus.Text = Loc.T("dict.export.fail", msg);
			}));
		}
		finally {
			Dispatcher.BeginInvoke(new Action(() => {
				if (gen != exportgen) return;
				exporting = false;
				bexport.IsEnabled = true;
			}));
		}
	}

	static string exportkind(string path) {
		var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
		if (ext == ".csv") return "csv";
		if (ext == ".html" || ext == ".htm") return "html";
		if (ext == ".md" || ext == ".markdown") return "md";
		return "txt";
	}

	static string exportname(string q) {
		var sb = new StringBuilder();
		foreach (var c in (q ?? "").Trim()) {
			if (c < ' ' || "\\/:*?\"<>|".IndexOf(c) >= 0) continue;
			sb.Append(c);
			if (sb.Length >= 40) break;
		}
		return sb.Length == 0 ? "dict" : sb.ToString();
	}

	static string csvcell(string s) {
		s = s ?? "";
		if (s.IndexOf('"') < 0 && s.IndexOf(',') < 0 && s.IndexOf('\r') < 0 && s.IndexOf('\n') < 0) return s;
		return "\"" + s.Replace("\"", "\"\"") + "\"";
	}

	static string htmlesc(string s) {
		if (string.IsNullOrEmpty(s)) return "";
		return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
	}

	internal static string ExportPlain(DictEntry e) => detailplain(e);

	internal static string ExportHtml(string s) => htmlesc(s);

	internal static string ExportCsv(string s) => csvcell(s);

	internal const string ExportCss = EXPORT_TABLE_CSS;

	static string detailplain(DictEntry e) {
		if (e == null) return "";
		var lines = new List<string>();
		var word = e.Word.Length > 0 ? e.Word : e.Headword;
		var head = word;
		if (e.Kanji.Length > 0 && e.Kanji != word) head += "  " + e.Kanji;
		lines.Add(head);
		var reading = e.Pron.Length > 0 ? e.Pron : e.Reading;
		if (reading.Length > 0) lines.Add(bracket(reading));
		var zharticle = e.Dict == "zh" && e.Extra.Length > 0;
		if (e.Pos.Length > 0) lines.Add(e.Pos);
		if (!zharticle) {
			var tags = new List<string>();
			foreach (var u in e.Usage) {
				if (u == null || u.Length == 0 || u == e.Pos) continue;
				tags.Add(u);
			}
			if (tags.Count > 0) lines.Add(Loc.T("dict.lab.tags") + string.Join(" · ", tags));
			if (e.Etymology.Length > 0) {
				lines.Add(Loc.T("dict.lab.etym"));
				foreach (var line in splitlines(e.Etymology)) lines.Add(line);
			}
			foreach (var c in e.Conjugations) lines.Add(Loc.T("dict.lab.conj") + c);
			if (e.See.Count > 0) {
				var see = new StringBuilder(Loc.T("dict.lab.see") + " ");
				var first = true;
				foreach (var s in e.See) {
					if (s == null || s.Length == 0) continue;
					if (!first) see.Append(" · ");
					first = false;
					see.Append(s);
				}
				lines.Add(see.ToString());
			}
			var n = 1;
			string lastpos = null;
			var anySense = false;
			foreach (var s in e.Senses) {
				if (s.Idiom.Length > 0) {
					if (anySense) lines.Add("");
					lines.Add(Loc.T("dict.lab.phrase") + " " + s.Idiom);
				}
				else if (anySense && !s.Sub) lines.Add("");
				anySense = true;
				if (s.Pos.Length > 0 && s.Pos != e.Pos && s.Pos != lastpos) {
					lines.Add("▶ " + s.Pos);
					lastpos = s.Pos;
					n = 1;
				}
				var numbered = false;
				void gloss(string tag, string lemma, string def) {
					if (lemma.Length == 0 && def.Length == 0) return;
					var p = "";
					if (!numbered && !s.Sub) {
						p = n + ". ";
						numbered = true;
						n++;
					}
					if (tag.Length > 0) p += tag + " ";
					p += join2(lemma, def);
					lines.Add(p);
				}
				if (s.Ko.Length > 0) gloss("", s.Ko, "");
				gloss(Loc.T("dict.lab.zh"), s.Zh, s.ZhDef);
				gloss(Loc.T("dict.lab.en"), s.En, s.EnDef);
				gloss(Loc.T("dict.lab.ja"), s.Ja, s.JaDef);
				foreach (var ph in s.Phrases) {
					if (ph.Text.Length == 0 && ph.Zh.Length == 0) continue;
					lines.Add(Loc.T("dict.lab.phrase") + " " + ph.Text);
					if (ph.Zh.Length > 0) lines.Add(Loc.T("dict.lab.zh") + " " + ph.Zh);
				}
				if (s.Sentences.Count > 0) lines.Add(Loc.T("dict.lab.example"));
				foreach (var ex in s.Sentences) {
					if (ex.Text.Length == 0 && ex.Zh.Length == 0) continue;
					lines.Add("· " + ex.Text);
					if (ex.Zh.Length > 0) lines.Add(Loc.T("dict.lab.zh") + " " + ex.Zh);
				}
				if (!s.Sub && !numbered && s.Phrases.Count == 0 && s.Sentences.Count == 0)
					lines.Add(n++ + ". " + Loc.T("dict.empty.sense"));
				else if (!s.Sub && !numbered) lines.Add(n++ + ".");
			}
		}
		if (e.Extra.Length > 0 && (zharticle || e.Dict != "zh")) {
			if (lines.Count > 0) lines.Add("");
			foreach (var line in splitlines(e.Extra)) lines.Add(line);
		}
		return string.Join("\r\n", lines);
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
		HideOutside();
		curword = "";
		curspeak = "";
		curlang = "";
		concats.Clear();
		for (var i = conjpanes.Count - 1; i >= 0; i--) dtabc.Items.Remove(conjpanes[i].Tab);
		conjpanes.Clear();
		dtabc.SelectedIndex = 0;
		edetail.Document = newdoc();
	}

	void filldetail(DictEntry e) {
		if (e == null) { cleardetail(); return; }
		HideOutside();
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
			var linkphrase = e.Dict != "ko";
			foreach (var s in e.Senses) {
				if (s.Idiom.Length > 0) {
					if (anySense) doc.Blocks.Add(para(4));
					var ip = para(0);
					addrun(ip, Loc.T("dict.lab.phrase") + " ", DRole.Label);
					if (linkphrase) addlink(ip, s.Idiom, firsttoken(s.Idiom), entrylang);
					else addrun(ip, s.Idiom, DRole.Body, entrylang);
					doc.Blocks.Add(ip);
				}
				else if (anySense && !s.Sub) doc.Blocks.Add(para(4));
				anySense = true;
				if (s.Pos.Length > 0 && s.Pos != e.Pos && s.Pos != lastpos) {
					doc.Blocks.Add(one("▶ " + s.Pos, DRole.Pos, 1, TextLang(s.Pos, entrylang)));
					lastpos = s.Pos;
					n = 1;
				}
				var numbered = false;
				void gloss(string tag, string lemma, string def, string lang) {
					if (lemma.Length == 0 && def.Length == 0) return;
					var p = para(0);
					if (!numbered && !s.Sub) {
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
				if (!s.Sub && !numbered && s.Phrases.Count == 0 && s.Sentences.Count == 0) {
					var p = para(0);
					addrun(p, n + ". ", DRole.Number);
					n++;
					addrun(p, Loc.T("dict.empty.sense"), DRole.Pron);
					doc.Blocks.Add(p);
				}
				else if (!s.Sub && !numbered) {
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
		buildtabs(e);
	}

	// 详情栏的标签：词条详情 + 该词条能变形的词性各一个。
	void buildtabs(DictEntry e) {
		concats = catsof(e);
		conjgen++;
		for (var i = conjpanes.Count - 1; i >= 0; i--) dtabc.Items.Remove(conjpanes[i].Tab);
		conjpanes.Clear();
		tabinfo.Header = Loc.T("dict.tab.info");
		for (var i = 0; i < concats.Count; i++) addconjtab(tablabel(concats[i]));
		dtabc.SelectedIndex = 0;
	}

	void addconjtab(string header) {
		var n = conjpanes.Count;
		var pane = new ConjPane();
		pane.Tab = new TabItem { Header = header };
		pane.Tab.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "dict.tab." + (n + 1));
		var dock = new DockPanel();
		var top = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
		pane.Meta = new TextBlock {
			TextWrapping = TextWrapping.Wrap,
			FontSize = 12,
			Foreground = (Brush)FindResource("TextMuted"),
			Margin = new Thickness(0, 0, 0, 4),
		};
		pane.Meta.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "dict.conj.meta." + n);
		top.Children.Add(pane.Meta);
		var row = new DockPanel();
		pane.Cnt = new TextBlock {
			Margin = new Thickness(8, 0, 0, 0),
			VerticalAlignment = VerticalAlignment.Center,
			FontSize = 12,
			Foreground = (Brush)FindResource("TextMuted"),
		};
		pane.Cnt.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "dict.conj.count." + n);
		pane.Q = new TextBox {
			Height = 28,
			FontSize = 13,
			Padding = new Thickness(8, 2, 8, 2),
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		pane.Q.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "dict.conj.search." + n);
		var ci = n;
		pane.Q.TextChanged += (_, _) => fillconj(ci);
		DockPanel.SetDock(pane.Cnt, Dock.Right);
		row.Children.Add(pane.Cnt);
		row.Children.Add(pane.Q);
		top.Children.Add(row);
		DockPanel.SetDock(top, Dock.Top);
		dock.Children.Add(top);
		pane.List = new ListBox {
			BorderThickness = new Thickness(0),
			ItemTemplate = (DataTemplate)FindResource("ConjRowTemplate"),
			ItemContainerStyle = (Style)FindResource("ConjItemStyle"),
		};
		pane.List.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "dict.conj.list." + n);
		pane.List.AddHandler(Button.ClickEvent, new RoutedEventHandler(onconjspeak));
		ScrollViewer.SetHorizontalScrollBarVisibility(pane.List, ScrollBarVisibility.Disabled);
		dock.Children.Add(pane.List);
		pane.Tab.Content = dock;
		conjpanes.Add(pane);
		dtabc.Items.Add(pane.Tab);
	}

	static string tablabel(ConjCat c) => c.Cat switch {
		"v" => Loc.T("dict.tab.verb"),
		"a" => Loc.T("dict.tab.adj"),
		_ => Loc.T("dict.tab.noun"),
	};

	// 词条能变形的词性。同一词性只出一个标签；日语动词按词典词性分五段/一段/サ変/カ変。
	static List<ConjCat> catsof(DictEntry e) {
		var outl = new List<ConjCat>();
		var texts = new List<string>();
		if (e.Pos.Length > 0) texts.Add(e.Pos);
		foreach (var s in e.Senses) if (s.Pos.Length > 0) texts.Add(s.Pos);
		var lang = (e.Dict ?? "").ToLowerInvariant();

		void put(string cat, string kind, string sub) {
			var hit = outl.Find(x => x.Cat == cat);
			if (hit != null) {
				if (hit.Kind.Length == 0 && kind.Length > 0) { hit.Kind = kind; hit.Sub = sub; }
				return;
			}
			outl.Add(new ConjCat { Cat = cat, Kind = kind, Sub = sub });
		}
		foreach (var t in texts) {
			if (lang == "ja") {
				if (Has(t, "kuru verb")) put("v", "3", "kuru");
				else if (Has(t, "suru verb") || Has(t, "aux. verb suru")) put("v", "3", "suru");
				else if (Has(t, "ichidan verb")) put("v", "2", "");
				else if (Has(t, "godan verb")) put("v", "1", "");
				if (Has(t, "adjectival nouns")) put("a", "an", "");
				else if (Has(t, "adjective")) put("a", "ai", "");
				if (Has(t, "noun")) put("n", "", "");
			}
			else if (lang == "ko") {
				if (Has(t, "verb") || Has(t, "동사")) put("v", "", "");
				if (Has(t, "adjective") || Has(t, "형용사")) put("a", "", "");
				if (Has(t, "noun") || Has(t, "명사")) put("n", "", "");
			}
			else if (lang == "en") {
				var paren = t.TrimStart().StartsWith("(", StringComparison.Ordinal);
				var part = Has(t, "past") || Has(t, "participle") || Has(t, "present");
				if (Has(t, "verb") || (paren && part)) put("v", "", "");
				if (Has(t, "adjective") || (paren && !part)) put("a", "", "");
				if (Has(t, "noun")) put("n", "", "");
			}
		}
		return outl;
	}

	static bool Has(string s, string bit) => s.IndexOf(bit, StringComparison.OrdinalIgnoreCase) >= 0;

	// 词条头可能带 ¹²³ 之类的序号，变形要用干净的词形。
	static string conjbase(string s) =>
		(s ?? "").TrimEnd("⁰¹²³⁴⁵⁶⁷⁸⁹₀₁₂₃₄₅₆₇₈₉①②③④⑤⑥⑦⑧⑨⑩".ToCharArray()).Trim();

	// 形态页顶上那行：实际判定的类型和特殊变化（1类/2类/3类、い/な形容词、不规则说明）。
	static string conjmeta(ConjResult r) {
		var parts = new List<string>();
		var name = r.Name ?? "";
		if (name.Length > 0 && name != "v" && name != "a" && name != "n") parts.Add(name);
		if ((r.Note == null || r.Note.Length == 0) && (r.Irr ?? "").Length > 0) parts.Add(r.Irr);
		if (r.Note != null && r.Note.Length > 0) parts.Add(r.Note);
		return string.Join(" · ", parts);
	}

	void loadconj(int ci) {
		if (ci < 0 || ci >= conjpanes.Count || ci >= concats.Count) return;
		var pane = conjpanes[ci];
		if (pane.Rows != null) return;
		var cat = concats[ci];
		var lang = curlang;
		var ja = lang == "ja";
		var word = conjbase(curword);
		if (ja && cat.Sub == "suru" && !word.EndsWith("する", StringComparison.Ordinal)) word += "する";
		var g = ++conjgen;
		pane.Cnt.Text = "";
		Task.Run(() => {
			try { return ConjApi.Build(lang, word, ja ? cat.Kind : "", "", ja ? "" : "", ja ? "" : cat.Cat, Loc.Lang); }
			catch { return null; }
		}).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() => {
			if (g != conjgen) return;
			var r = t.Result;
			if (r == null || !r.Ok) {
				pane.Cnt.Text = r != null && r.Err != null && r.Err.Length > 0 ? r.Err : Loc.T("dict.conj.fail");
				return;
			}
			pane.Rows = r.Rows;
			pane.Meta.Text = conjmeta(r);
			fillconj(ci);
		})));
	}

	void fillconj(int ci) {
		if (ci < 0 || ci >= conjpanes.Count) return;
		var pane = conjpanes[ci];
		if (pane.Rows == null) return;
		var k = (pane.Q.Text ?? "").Trim().ToLowerInvariant();
		var items = new List<ConjItem>();
		string last = null;
		var n = 0;
		foreach (var r in pane.Rows) {
			if (k.Length > 0 && !(r.L + " " + r.V).ToLowerInvariant().Contains(k)) continue;
			n++;
			if (r.G != last) {
				last = r.G;
				if (!string.IsNullOrEmpty(last))
					items.Add(new ConjItem { G = last, HeadVis = Visibility.Visible, RowVis = Visibility.Collapsed });
			}
			items.Add(new ConjItem { L = r.L, V = r.V });
		}
		if (n == 0) items.Add(new ConjItem { L = Loc.T("dict.conj.empty") });
		pane.List.ItemsSource = items;
		pane.Cnt.Text = string.Format(Loc.T("dict.conj.count"), n, pane.Rows.Count);
	}

	void openselpop() {
		if (reading()) return;
		var text = edetail.Selection == null ? "" : edetail.Selection.Text ?? "";
		text = oneline(text);
		if (text.Length == 0) return;
		ShowOutside(text, selpx(), langofselection(text));
		outdict = true;
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

	/// <summary>选区的屏幕物理像素。空选区用当前指针。</summary>
	Rect selpx() {
		var box = selbox();
		if (box.IsEmpty) {
			Point p;
			try { p = Mouse.GetPosition(edetail); }
			catch { p = new Point(8, 8); }
			box = new Rect(p.X, p.Y, 2, 18);
		}
		try {
			// PointToScreen 已是屏幕物理像素，不要再乘 DPI。
			var tl = edetail.PointToScreen(box.TopLeft);
			var br = edetail.PointToScreen(box.BottomRight);
			return new Rect(Math.Min(tl.X, br.X), Math.Min(tl.Y, br.Y),
				Math.Max(1, Math.Abs(br.X - tl.X)), Math.Max(1, Math.Abs(br.Y - tl.Y)));
		}
		catch { return new Rect(0, 0, 2, 18); }
	}

	internal Rect SelScreenPx() => selpx();

	internal Rect PopupScreenPx() {
		if (outwin == null || !outwin.IsVisible) return Rect.Empty;
		return outwin.ScreenPx();
	}

	/// <summary>浮窗是否压住选区。供命令行检查。</summary>
	internal bool PopOverlapsSel() {
		if (outwin == null || !outwin.IsVisible) return false;
		var sel = selpx();
		var pop = outwin.ScreenPx();
		if (sel.IsEmpty || pop.IsEmpty) return false;
		var v = pop.Top < sel.Bottom - 1 && pop.Bottom > sel.Top + 1;
		var h = pop.Left < sel.Right - 1 && pop.Right > sel.Left + 1;
		return v && h;
	}

	/// <summary>划词查词浮窗。lang 为空时按文字判断发音语言。</summary>
	public void ShowOutside(string text, Rect screenPx, string lang = null) {
		text = oneline(text);
		if (reading()) return;
		if (text.Length == 0) {
			HideOutside();
			return;
		}
		outdict = false;
		ensureout();
		outtext = text;
		outlang = string.IsNullOrEmpty(lang) ? langoutside(text) : lang;
		outbox = screenPx;
		outwin.ApplyText();
		outwin.SetLang(selfilter);
		searchoutside();
	}

	public void HideOutside() {
		ogen++;
		outdict = false;
		if (SelBar.Hold || (selfilter ?? "") == "read") {
			stopread();
			SelBar.Hold = false;
		}
		try {
			if (outwin != null && outwin.IsVisible) outwin.Hide();
		}
		catch { }
	}

	public void SaveSelSize(OcrOptions o) {
		if (o == null || outwin == null) return;
		outwin.CopySize(out var w, out var h);
		if (w < 296 || h < 168) return;
		if (w > 2400) w = 2400;
		if (h > 1800) h = 1800;
		o.SelBarW = (int)Math.Round(w);
		o.SelBarH = (int)Math.Round(h);
	}

	bool reading() {
		return (selfilter ?? "") == "read" && outwin != null && outwin.IsVisible;
	}

	void turnoffselbar() {
		HideOutside();
		if (selbarsync) return;
		SelBarChanged?.Invoke(false);
		try { UiToast.Show(null, Loc.T("dict.selbar.off")); } catch { }
	}

	public IntPtr OutsideHwnd() {
		try {
			if (outwin == null || !outwin.IsVisible) return IntPtr.Zero;
			return new WindowInteropHelper(outwin).Handle;
		}
		catch { return IntPtr.Zero; }
	}

	void ensureout() {
		if (outwin != null) return;
		outwin = new SelBarWindow();
		outwin.HitClick += row => {
			if (row == null || row.Word.Length == 0) return;
			HideOutside();
			opensearch(row.Word, row.Id);
		};
		outwin.SpeakClick += () => _ = speak(outtext, outlang);
		outwin.SearchClick += () => {
			var q = outtext;
			HideOutside();
			opensearch(q);
		};
		outwin.TrGo += () => _ = runseltr();
		outwin.TrSrcChanged += () => {
			filltrdst();
			refreshtrlabels();
		};
		outwin.TrDstChanged += () => refreshtrlabels();
		outwin.TrTextChanged += () => refreshtrlabels();
		outwin.TrSpeak += src => speaktr(src);
		outwin.ReadGo += () => _ = readloop();
		outwin.ReadStop += () => stopread();
		outwin.CopyClick += () => copytext(outtext);
		outwin.CloseClick += () => {
			try { SelBar.DismissShown(); } catch { }
		};
		outwin.SelOnChanged += on => {
			if (on) SelBarChanged?.Invoke(true);
			else turnoffselbar();
		};
		outwin.SetSelOn(cbselbar.IsChecked == true);
		var saved = Options != null ? Options() : null;
		if (saved != null) outwin.UseSize(saved.SelBarW, saved.SelBarH);
		outwin.LangChanged += dict => {
			dict = dict ?? "";
			if (selfilter == dict) return;
			selfilter = dict;
			syncself();
			refreshsel();
		};
		outwin.ApplyText();
		outwin.SetLang(selfilter);
	}

	void syncself() {
		try { outwin?.SetLang(selfilter); } catch { }
	}

	void refreshsel() {
		if (outwin != null && outwin.IsVisible) searchoutside();
	}

	void searchoutside() {
		if (outwin == null) return;
		var text = outtext ?? "";
		if ((selfilter ?? "") == "read") {
			ogen++;
			trgen++;
			try { trcts?.Cancel(); } catch { }
			SelBar.Hold = true;
			showread(text);
			outwin.ShowAt(outbox);
			return;
		}
		if (SelBar.Hold) {
			SelBar.Hold = false;
			stopread();
		}
		if ((selfilter ?? "") == "tr") {
			ogen++;
			showtr(text);
			outwin.ShowAt(outbox);
			return;
		}
		trgen++;
		try { trcts?.Cancel(); } catch { }
		outwin.ShowDict();
		var q = text.Length > 80 ? text.Substring(0, 80) : text;
		var dict = selfilter ?? "";
		if (!SelWord(q)) {
			ogen++;
			outwin.ShowRows(null, false);
			outwin.ShowAt(outbox);
			return;
		}
		outwin.ShowRows(null, true);
		outwin.ShowAt(outbox);
		var g = ++ogen;
		Task.Run(() => searchsel(q, dict)).ContinueWith(t => {
			Dispatcher.BeginInvoke(new Action(() => {
				if (g != ogen || outwin == null || !outwin.IsVisible) return;
				List<DictHit> hits = null;
				try { hits = t.Result; } catch { }
				outwin.ShowRows(selrows(hits), false);
				outwin.ShowAt(outbox);
			}));
		});
	}

	void showtr(string text) {
		text = text ?? "";
		if (trkeep != null && string.Equals(text, trsel, StringComparison.Ordinal)) {
			outwin.ShowTr(text, false);
			refreshtrlabels();
			return;
		}
		trsel = text;
		var keep = trinit;
		trinit = true;
		var src = keep ? outwin.TrSrcCode : TrLang.Auto;
		var pack = TrPack != null ? TrPack(src) : new SelTrPack();
		outwin.SetTrLangs(pack.Sources, pack.Targets, keep);
		outwin.ShowTr(text, true);
		refreshtrlabels();
		_ = runseltr();
	}

	void refreshtrlabels() {
		if (outwin == null) return;
		var o = Options != null ? Options() : null;
		var nat = TrLang.Zh;
		var lrn = TrLang.En;
		if (o != null) o.AutoTranslatePair(out nat, out lrn);
		LangDetect.ResolveSel(outwin.TrSrcText, outwin.TrSrcCode, outwin.TrDstCode, nat, lrn, out var src, out var dst);
		outwin.SetTrLabels(TrLang.Label(src), TrLang.Label(dst));
	}

	void filltrdst() {
		if (outwin == null || TrPack == null) return;
		var pack = TrPack(outwin.TrSrcCode);
		var prefer = outwin.TrDstCode;
		if (prefer.Length == 0) prefer = TrLang.Auto;
		outwin.SetTrTargets(pack.Targets, prefer);
	}

	async Task runseltr() {
		if (outwin == null) return;
		var gen = ++trgen;
		try { trcts?.Cancel(); } catch { }
		trcts = new CancellationTokenSource();
		var ct = trcts.Token;
		var text = outwin.TrSrcText ?? "";
		var src = outwin.TrSrcCode;
		var dst = outwin.TrDstCode;
		if (TrRun == null) {
			outwin.SetTrOut(Loc.T("dict.sel.notr"));
			return;
		}
		var o = Options != null ? Options() : null;
		var nat = TrLang.Zh;
		var lrn = TrLang.En;
		if (o != null) o.AutoTranslatePair(out nat, out lrn);
		LangDetect.ResolveSel(text, src, dst, nat, lrn, out var shownSrc, out var shownDst);
		outwin.SetTrOut(Loc.T("tr.popup.run", TrLang.Label(shownSrc), TrLang.Label(shownDst)));
		try {
			var result = await TrRun(text, src, dst, ct);
			if (gen != trgen || outwin == null) return;
			outwin.SetTrOut(result ?? "");
			trkeep = text;
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			if (gen != trgen || outwin == null) return;
			outwin.SetTrOut(ex.Message ?? "");
		}
	}

	void speaktr(bool src) {
		if (outwin == null) return;
		var text = src ? outwin.TrSrcText : outwin.TrDstText;
		var code = src ? outwin.TrSrcCode : outwin.TrDstCode;
		if (code == TrLang.Auto || code.Length == 0) code = LangDetect.DetectCode(text);
		code = TrLang.Normalize(code);
		if (code == "cht" || code == "yue") code = "zh";
		_ = speak(text, code);
	}

	/// <summary>划词查询。库还没打开时先按词典页的路径打开，再按浮窗语言查最多 100 条。</summary>
	List<DictHit> searchsel(string q, string dict) {
		try {
			var o = Options != null ? Options() : null;
			if (o != null && !o.ModDict) return new List<DictHit>();
			if (!DictDb.Ready) DictDb.Init(DictWeb.DbPath);
			return DictDb.Search(q, dict ?? "", 100);
		}
		catch { return new List<DictHit>(); }
	}

	string langoutside(string text) {
		var score = new int[4];
		addscript(score, text, true);
		return bestlang(score, "en");
	}

	static List<DictSelRow> selrows(List<DictHit> hits) {
		var rows = new List<DictSelRow>();
		if (hits == null) return rows;
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
			if (rows.Count >= 100) break;
		}
		return rows;
	}

	void hookhost() {
		var w = Window.GetWindow(this);
		if (w == null || ReferenceEquals(w, hostwin)) return;
		if (hostwin != null) {
			hostwin.Deactivated -= onhostoff;
			hostwin.PreviewMouseDown -= onhostdown;
		}
		hostwin = w;
		hostwin.Deactivated += onhostoff;
		hostwin.PreviewMouseDown += onhostdown;
	}

	void onhostdown(object sender, MouseButtonEventArgs e) {
		if (!outdict) return;
		HideOutside();
	}

	void showread(string text) {
		text = text ?? "";
		if (readon && string.Equals(text, readsel, StringComparison.Ordinal)) {
			outwin.ShowRead(text, false, outlang);
			return;
		}
		readsel = text;
		readon = true;
		outwin.ShowRead(text, true, outlang);
	}

	void stopread() {
		readgen++;
		try { readcts?.Cancel(); } catch { }
		try { player?.Stop(); } catch { }
		try { outwin?.ClearReadMark(); } catch { }
	}

	async Task readloop() {
		if (outwin == null) return;
		var gen = ++readgen;
		try { readcts?.Cancel(); } catch { }
		try { player?.Stop(); } catch { }
		readcts = new CancellationTokenSource();
		var ct = readcts.Token;
		var text = outwin.ReadText ?? "";
		var lang = outwin.ReadLang;
		if (lang != "zh" && lang != "en" && lang != "ja" && lang != "ko") lang = "zh";
		var eng = DictTts.NormEngine(outwin.ReadEngine);
		var lines = TtsTextSplitter.Split(text);
		if (lines.Count == 0) return;
		if (player == null) player = new TtsPlayer();
		var o = Options != null ? Options() : null;
		var saved = eng == DictTts.AUTO ? DictTts.For(o, lang) : null;
		var use = saved != null ? DictTts.ResolveEngine(saved.Engine, lang) : eng;
		var voice = saved != null ? DictTts.VoiceOf(saved, lang, use) : (outwin.ReadVoice ?? "");
		var rate = saved != null ? saved.Rate : outwin.ReadRate;
		foreach (var line in lines) {
			if (gen != readgen || ct.IsCancellationRequested || outwin == null) return;
			var (a, b) = TtsTextSplitter.MapToUiOffsets(text, line.Start, line.End);
			outwin.HighlightRead(a, b);
			if (use == DictTts.CHROME) {
				try { await speakchromeline(o, lang, voice, line.Text, rate, ct).ConfigureAwait(true); }
				catch (OperationCanceledException) { return; }
				catch (Exception ex) {
					if (gen == readgen) {
						outwin?.ClearReadMark();
						try { UiToast.Show(outwin, ex.Message); } catch { }
					}
					return;
				}
				continue;
			}
			DictTtsLang pref;
			if (saved != null) pref = saved.Clone();
			else {
				pref = DictTts.For(o, lang);
				pref.Rate = rate;
				pref.Engine = use;
				if (use == DictTts.ONNX) pref.Onnx = voice;
				else if (use == DictTts.SAPI) pref.Sapi = voice;
				else if (use == DictTts.WINRT) pref.WinRt = voice;
				else pref.Edge = voice;
			}
			string path;
			try { path = await DictTts.EnsureWav(pref, lang, line.Text, 2000).ConfigureAwait(true); }
			catch (OperationCanceledException) { return; }
			catch { continue; }
			if (gen != readgen || ct.IsCancellationRequested) return;
			try { await player.PlayFileAsync(path, ct).ConfigureAwait(true); }
			catch (OperationCanceledException) { return; }
		}
		if (gen == readgen) outwin?.ClearReadMark();
	}

	async Task speakchromeline(OcrOptions o, string lang, string voice, string text, double rate, CancellationToken ct) {
		try {
			await Task.Run(() => chromeread(o, lang, voice, text, rate, ct)).ConfigureAwait(true);
		}
		catch (InvalidOperationException ex) when (chromemiss(ex)) {
			await ensurechromeread(o, lang, voice, ct).ConfigureAwait(true);
			await Task.Run(() => chromeread(o, lang, voice, text, rate, ct)).ConfigureAwait(true);
		}
	}

	static bool chromemiss(Exception ex) {
		var m = ex?.Message ?? "";
		return m.IndexOf("没有发出声音", StringComparison.Ordinal) >= 0
			|| m.IndexOf("not-allowed", StringComparison.OrdinalIgnoreCase) >= 0
			|| m.IndexOf("language-unavailable", StringComparison.OrdinalIgnoreCase) >= 0
			|| m.IndexOf("synthesis-failed", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	/// <summary>这一语言的离线包还没装时，弹出进度并安装。已安装则马上返回。</summary>
	async Task ensurechromeread(OcrOptions o, string lang, string voice, CancellationToken ct) {
		var pack = chromepack(lang, voice);
		var exe = ChromeTts.FindExe(o?.ChromeExe);
		if (pack.Length == 0 || string.IsNullOrEmpty(exe)) return;
		if (ChromePack.VoiceInstalled(pack) == true) return;
		ct.ThrowIfCancellationRequested();
		var prog = new ChromeVoiceProgressWindow { Topmost = true };
		var owner = Window.GetWindow(this);
		if (owner != null) prog.Owner = owner;
		using var reg = ct.Register(() => prog.RequestCancel());
		Exception fail = null;
		var mode = HttpProxy.Mode;
		var addr = HttpProxy.Addr;
		prog.Show();
		var task = Task.Run(() => {
			try {
				ChromePack.EnsureVoice(pack, exe, mode, addr, (note, frac, done, total) => {
					try { prog.Report(note, frac, done, total); } catch { }
				}, prog.Token);
			}
			catch (Exception ex) { fail = ex; }
		});
		try { await task.ConfigureAwait(true); }
		finally { prog.ForceClose(); }
		if (prog.WasCancelled || ct.IsCancellationRequested)
			throw new OperationCanceledException(ct);
		if (fail != null) throw fail;
	}

	static string chromepack(string lang, string voice) {
		var culture = ChromePack.SpeakLang(lang);
		var name = (voice ?? "").Trim();
		foreach (var v in ChromeTts.Voices) {
			if (name.Length > 0) {
				if (!string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
				if (!string.IsNullOrEmpty(v.Culture)) culture = v.Culture;
				break;
			}
			if (!string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase)) continue;
			if (!string.IsNullOrEmpty(v.Culture)) culture = v.Culture;
			break;
		}
		return ChromePack.SpeakLang(culture);
	}

	static void chromeread(OcrOptions o, string lang, string voice, string text, double rate, CancellationToken ct) {
		var exe = ChromeTts.FindExe(o?.ChromeExe);
		if (string.IsNullOrEmpty(exe))
			throw new InvalidOperationException(Loc.T("tts.chrome.missing"));
		var name = (voice ?? "").Trim();
		var culture = ChromePack.SpeakLang(lang);
		SapiVoiceItem pick = null;
		foreach (var v in ChromeTts.Voices) {
			if (name.Length > 0) {
				if (!string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
				pick = v;
				break;
			}
			if (!string.Equals(v.Lang, lang, StringComparison.OrdinalIgnoreCase)) continue;
			pick = v;
			break;
		}
		if (pick != null) {
			name = pick.Name;
			if (!string.IsNullOrEmpty(pick.Culture)) culture = pick.Culture;
		}
		var idle = ChromeTts.ClampIdle(o == null ? 60 : o.ChromeIdleSec);
		ChromeTts.Speak(text, name, culture, DictTts.NormRate(rate), 1, exe, idle, ct);
	}

	void onhostoff(object sender, EventArgs e) {
		if (!outdict || reading()) return;
		var w = hostwin;
		Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
			if (!outdict) return;
			if (w != null && w.IsActive) return;
			if (outwin != null && outwin.IsActive) return;
			HideOutside();
		}));
	}

	void openhit(DictSelRow row) {
		if (row == null || row.Word.Length == 0) return;
		HideOutside();
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
			var o = Options != null ? Options() : null;
			var pref = DictTts.For(o, lang);
			var use = DictTts.ResolveEngine(pref.Engine, lang);
			if (use == DictTts.CHROME) {
				await Task.Run(() => chromeread(o, lang, DictTts.VoiceOf(pref, lang, use), text, pref.Rate, CancellationToken.None)).ConfigureAwait(true);
				lbstatus.Text = Loc.T("dict.speaking", langlabel(lang));
				return;
			}
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

	/// <summary>像一个词才查库。含单词里不会出现的标点，或长过单词，直接弹出、不查。</summary>
	internal static bool SelWord(string text) {
		if (string.IsNullOrEmpty(text)) return false;
		var n = 0;
		var cjk = false;
		foreach (var c in text) {
			n++;
			if (n > 48) return false;
			if (!selchar(c, ref cjk)) return false;
		}
		if (cjk && n > 20) return false;
		return true;
	}

	static bool selchar(char c, ref bool cjk) {
		if (c == ' ' || c == '\u3000') return true;
		if (ishan(c) || iskana(c) || ishangul(c) || c == '々') {
			cjk = true;
			return true;
		}
		if (char.IsLetter(c) || char.IsDigit(c)) return true;
		if (c == '\'' || c == '\u2019' || c == '\uFF07') return true;
		if (c == '-' || c == '\u2010' || c == '\u2011' || c == '\uFF0D') return true;
		if (c == '\u00B7' || c == '\u30FB' || c == '\uFF65') return true;
		return false;
	}

	static bool ishangul(char c) {
		if (c >= '\uAC00' && c <= '\uD7A3') return true;
		return c >= '\u1100' && c <= '\u11FF' || c >= '\u3130' && c <= '\u318F';
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
		if (dict != "zh" && dict != "ja" && dict != "ko" && dict != "en") return "";
		if (!Loc.IsZh) return dict;
		if (dict == "zh") return Loc.T("dict.lab.zh");
		if (dict == "en") return Loc.T("dict.lab.en");
		if (dict == "ja") return Loc.T("dict.lab.ja");
		return Loc.T("dict.lab.ko");
	}

	static string listtitle(string dict, string shown) {
		var code = listcode(dict);
		if (code.Length == 0) return shown ?? "";
		return "[" + code + "] " + (shown ?? "");
	}

	static string stripcode(string title) {
		if (title != null && title.Length > 3 && title[0] == '[') {
			var end = title.IndexOf("] ", StringComparison.Ordinal);
			if (end > 0 && end < 8) return title.Substring(end + 2);
		}
		return title ?? "";
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
}
