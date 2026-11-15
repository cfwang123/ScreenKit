using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenKit;

public enum PlayerForm { Large, Fullscreen }

/// <summary>右栏「单词」里的一项：当前句命中的词和它的释义摘要。</summary>
sealed class WordItem {
	public string Word { get; set; } = "";
	public string Preview { get; set; } = "";
}

/// <summary>媒体播放窗口：大窗口为主，可切全屏；音视频共用一条会话。</summary>
public partial class PlayerWindow : Window {
	// ==================== 状态 ====================
	#region
	readonly MediaSession session = new();
	readonly Playlist list = new();
	TimedTextLayer layer;
	TimedTextFile sub;
	TimedTextFile lyric;

	PlayerForm form = PlayerForm.Large;
	WriteableBitmap bmp;
	long lastseq = -1;
	double lastpos = -1;
	bool seeking;
	bool autohide = true;
	int lastmove = Environment.TickCount;
	bool mousein;
	bool dragging;
	Point dragfrom;
	double dragl, dragt;
	WindowState prevstate;
	bool fullscreen;
	int lastclick = 0;
	int clickcount;
	bool sideopen = true;
	string lastcue = "";
	readonly ObservableCollection<WordItem> words = new();
	readonly ObservableCollection<DictRow> dicthits = new();

	DispatcherTimer timer;
	double paused_at;

	internal Playlist Playlist => list;
	#endregion

	// ==================== 构造 ====================
	#region
	public PlayerWindow(OcrOptions opt = null) {
		InitializeComponent();
		this.opt = opt;
		layer = new TimedTextLayer(ptext);
		icwords.ItemsSource = words;
		dictlist.ItemsSource = dicthits;
		wire();
		list.Changed += () => updatelist();
		list.ItemRequested += (item, tr) => playitem(item);
		session.Failed += msg => {
			Dispatcher.BeginInvoke(new Action(() => hud(msg)));
		};
		AllowDrop = true;
		Drop += ondrop;
		DragOver += ondragover;
		PreviewKeyDown += onkey;
		Loaded += (_, _) => {
			loadopts();
			list.Load();
			updatelist();
			// 启动只还原列表，不接着上次的位置播；时间与进度先归零
			lbtime.Text = "00:00 / 00:00";
			sseek.Value = 0;
			bplay.Content = "▶";
			starttimer();
			applyform(form, false);
		};
		Closing += (_, _) => {
			saveopts();
			try { session.Close(); } catch { }
			try { list.Save(); } catch { }
		};
	}

	readonly OcrOptions opt;

	void loadopts() {
		var o = opt;
		svol.Value = o != null && o.PlayVolume > 0 ? o.PlayVolume : 0.8;
		savedvol = svol.Value;
		session.SetVolume((float)svol.Value);
		if (o != null) {
			layer.FontSize = o.PlaySubSize > 0 ? o.PlaySubSize : 32;
			layer.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(o.PlaySubFont) ? "Microsoft YaHei UI" : o.PlaySubFont);
			layer.SungColor = ColorUtil.Parse(o.PlayKaraSung, Color.FromRgb(0xFF, 0xD8, 0x4D));
			layer.UnsungColor = ColorUtil.Parse(o.PlayKaraUnsung, Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
			layer.OutlineColor = ColorUtil.Parse(o.PlaySubOutline, Color.FromArgb(0xCC, 0, 0, 0));
			layer.BottomMargin = o.PlaySubBottom > 0 ? o.PlaySubBottom : 48;
			layer.SnapToChar = string.Equals(o.PlayKaraMode, "char", StringComparison.OrdinalIgnoreCase);
			layer.FeatherPx = o.PlayKaraFeather;
			layer.Karaoke = o.PlaySubOn;
			list.Mode = Playlist.modeparse(o.PlayListMode);
			applymode();
			form = parseform(o.PlayMode);
		}
	}

	void saveopts() {
		var o = opt;
		if (o == null) return;
		o.PlayVolume = svol.Value;
		o.PlayMode = form == PlayerForm.Fullscreen ? "fullscreen" : "large";
		o.PlayListMode = list.Mode switch {
			PlayMode.Shuffle => "shuffle",
			PlayMode.RepeatOne => "repeatone",
			PlayMode.RepeatAll => "repeatall",
			_ => "sequential",
		};
	}

	static PlayerForm parseform(string s) =>
		string.Equals((s ?? "").Trim(), "fullscreen", StringComparison.OrdinalIgnoreCase) ? PlayerForm.Fullscreen : PlayerForm.Large;
	#endregion

	// ==================== 事件绑定 ====================
	#region
	void wire() {
		bplay.Click += (_, _) => toggleplay();
		bprev.Click += (_, _) => playprev();
		bnext.Click += (_, _) => playnext(true);
		bfull.Click += (_, _) => setfull(!fullscreen);
		bside.Click += (_, _) => setside(!sideopen);
		btop.Click += (_, _) => togglepin();
		bopen.Click += (_, _) => openfile();
		bsub.Click += (_, _) => togglesub();
		bsubopen.Click += (_, _) => opensub();
		bmute.Click += (_, _) => togglemute();
		bloop.Click += (_, _) => cyclemode();
		baddfile.Click += (_, _) => addfiles();
		badddir.Click += (_, _) => adddir();
		bclearlist.Click += (_, _) => { list.Clear(); updatelist(); };
		bdropmiss.Click += (_, _) => {
			var n = list.RemoveMissing();
			hud(n > 0 ? $"已清理 {n} 项" : "没有失效项");
			updatelist();
		};
		svol.ValueChanged += (_, _) => {
			session.SetVolume((float)svol.Value);
			if (svol.Value > 0) bmute.Content = "🔊";
			savedvol = svol.Value;
		};
		MouseMove += (_, e) => {
			lastmove = Environment.TickCount;
			mousein = true;
			if (autohide) showpanel(true);
			_ = e;
		};
		MouseLeave += (_, _) => {
			mousein = false;
			lastmove = Environment.TickCount;
		};
	}
	#endregion

	// ==================== 定时器 ====================
	#region
	void starttimer() {
		timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
		timer.Tick += (_, _) => ontick();
		timer.Start();
	}

	void ontick() {
		if (session.HasVideo) drawframe();
		var pos = session.PositionSec;
		if (!seeking && Math.Abs(pos - lastpos) > 0.02) {
			lastpos = pos;
			updateclock(pos);
			updatetext(pos);
		}
		if (autohide && !session.IsPaused) checkhide();
		if (session.IsEnded && !session.IsPaused) {
			session.Pause();
			onended();
		}
	}
	#endregion

	// ==================== 画面 ====================
	#region
	void drawframe() {
		var f = session.TakeDue();
		if (f == null && session.IsPaused) f = session.PeekLatest();
		if (f == null) return;
		var seq = session.FrameSeq;
		if (seq == lastseq && !session.IsPaused) return;
		lastseq = seq;
		if (bmp == null || bmp.PixelWidth != f.Width || bmp.PixelHeight != f.Height) {
			bmp = new WriteableBitmap(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null);
			imgvideo.Source = bmp;
			lbempty.Visibility = Visibility.Collapsed;
		}
		bmp.Lock();
		try {
			var back = bmp.BackBuffer;
			var stride = f.Stride;
			var len = Math.Min(stride, bmp.BackBufferStride);
			for (var y = 0; y < f.Height; y++) {
				Marshal.Copy(f.Bgra, y * f.Stride, back + y * bmp.BackBufferStride, len);
			}
			bmp.AddDirtyRect(new Int32Rect(0, 0, f.Width, f.Height));
		}
		finally {
			bmp.Unlock();
		}
	}
	#endregion

	// ==================== 播放控制 ====================
	#region
	public void OpenFile(string path) {
		if (string.IsNullOrWhiteSpace(path)) return;
		if (Directory.Exists(path)) {
			var n = list.AddDirectory(path);
			updatelist();
			hud($"已加入 {n} 项");
			if (list.Current == null && list.Items.Count > 0) playitem(list.Items[0]);
			return;
		}
		if (!MediaFormat.IsMedia(path)) { hud("不是可播放的音视频文件"); return; }
		list.AddFiles(new[] { path });
		updatelist();
		var idx = -1;
		for (var i = 0; i < list.Items.Count; i++)
			if (string.Equals(list.Items[i].Path, path, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
		if (idx >= 0) list.Select(idx);
		playitem(list.Current);
	}

	/// <summary>替换列表并立刻播放：命令行打开、外部调用用这个，避免列表里堆一堆重复项。</summary>
	public void PlaySingle(string path) {
		if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path)) { OpenFile(path); return; }
		if (!MediaFormat.IsMedia(path)) { hud("不是可播放的音视频文件"); return; }
		list.Clear();
		list.AddFiles(new[] { path });
		updatelist();
		list.Select(0);
		playitem(list.Current);
	}

	void playitem(PlaylistItem item) {
		if (item == null) return;
		if (!File.Exists(item.Path)) {
			item.Missing = true;
			hud("文件不存在: " + Path.GetFileName(item.Path));
			playnext(false);
			return;
		}
		try {
			session.Open(item.Path);
			session.SetVolume((float)svol.Value);
			bmp = null;
			lastseq = -1;
			lastpos = -1;
			imgvideo.Source = null;
			Title = item.Display + " - 媒体播放器";
			lbempty.Visibility = Visibility.Collapsed;
			autotext(item.Path);
			session.Play();
			bplay.Content = "⏸";
			sseek.Value = 0;
			if (item.DurationSec <= 0 && session.DurationSec > 0) {
				item.DurationSec = session.DurationSec;
				list.RaiseChanged();
			}
			updateclock(0);
		}
		catch (Exception ex) {
			hud("打开失败: " + ex.Message);
		}
	}

	void toggleplay() {
		if (list.Current == null) { openfile(); return; }
		session.TogglePause();
		bplay.Content = session.IsPaused ? "▶" : "⏸";
		if (!session.IsPaused) lastmove = Environment.TickCount;
	}

	void playnext(bool byuser) {
		var tr = byuser ? PlayTrigger.UserNext : PlayTrigger.AutoNext;
		var item = list.Advance(tr);
		if (item == null) { hud("列表已结束"); return; }
		playitem(item);
	}

	void playprev() {
		var item = list.Previous();
		if (item != null) playitem(item);
	}

	void onended() {
		if (!list.CanAdvance()) {
			session.Pause();
			bplay.Content = "▶";
			return;
		}
		playnext(false);
	}

	void openfile() {
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Title = "打开音视频文件",
			Filter = "音视频|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.ts;*.mpg;*.mpeg;*.rmvb;*.mp3;*.wav;*.flac;*.m4a;*.ogg;*.opus;*.wma;*.aac;*.ape|所有文件|*.*",
			Multiselect = true,
		};
		if (dlg.ShowDialog(this) != true) return;
		list.AddFiles(dlg.FileNames);
		updatelist();
		if (list.Current == null && dlg.FileNames.Length > 0) OpenFile(dlg.FileNames[0]);
	}

	void addfiles() {
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Title = "添加文件",
			Filter = "音视频|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.ts;*.mpg;*.mpeg;*.rmvb;*.mp3;*.wav;*.flac;*.m4a;*.ogg;*.opus;*.wma;*.aac;*.ape|所有文件|*.*",
			Multiselect = true,
		};
		if (dlg.ShowDialog(this) != true) return;
		var n = list.AddFiles(dlg.FileNames);
		updatelist();
		hud($"已加入 {n} 项");
	}

	void adddir() {
		var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择要加入列表的文件夹" };
		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
		var n = list.AddDirectory(dlg.SelectedPath);
		updatelist();
		hud($"已加入 {n} 项");
	}
	#endregion

	// ==================== 播放列表 ====================
	#region
	void updatelist() {
		lblist.ItemsSource = list.Items;
		lblistcount.Text = $"{list.Items.Count} 项";
		probedurations();
	}

	/// <summary>后台补全未知时长：只探测还没有 DurationSec 的项，依次排队，不阻塞 UI。</summary>
	void probedurations() {
		var todo = new List<string>();
		foreach (var it in list.Items) {
			if (it.DurationSec > 0 || it.Missing) continue;
			if (todo.Contains(it.Path)) continue;
			todo.Add(it.Path);
			if (todo.Count >= 40) break;
		}
		if (todo.Count == 0) return;
		System.Threading.Tasks.Task.Run(() => {
			foreach (var p in todo) {
				var dur = 0.0;
				try {
					using var d = new MediaDemuxer(p);
					dur = d.DurationSec;
				}
				catch { continue; }
				if (dur <= 0) continue;
				var path = p;
				var secs = dur;
				Dispatcher.BeginInvoke(new Action(() => {
					var hit = false;
					foreach (var it in list.Items)
						if (string.Equals(it.Path, path, StringComparison.OrdinalIgnoreCase)) { it.DurationSec = secs; hit = true; }
					if (hit) list.RaiseChanged();
				}));
			}
		});
	}

	void listdouble(object sender, MouseButtonEventArgs e) {
		var item = lblist.SelectedItem as PlaylistItem;
		if (item == null) return;
		var idx = list.Items.IndexOf(item);
		if (idx >= 0) list.Select(idx);
		playitem(item);
	}

	void cyclemode() {
		list.Mode = list.Mode switch {
			PlayMode.Sequential => PlayMode.Shuffle,
			PlayMode.Shuffle => PlayMode.RepeatOne,
			PlayMode.RepeatOne => PlayMode.RepeatAll,
			_ => PlayMode.Sequential,
		};
		applymode();
		hud("播放模式: " + bloop.Content);
	}

	void applymode() {
		bloop.Content = list.Mode switch {
			PlayMode.Shuffle => "随机",
			PlayMode.RepeatOne => "单曲",
			PlayMode.RepeatAll => "循环",
			_ => "顺序",
		};
	}

	void ondragover(object sender, DragEventArgs e) {
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void ondrop(object sender, DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (paths == null || paths.Length == 0) return;
		var replace = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		if (replace) list.Clear();
		var n = 0;
		var first = "";
		foreach (var p in paths) {
			if (Directory.Exists(p)) n += list.AddDirectory(p);
			else if (MediaFormat.IsMedia(p)) {
				if (first.Length == 0) first = p;
				n += list.AddFiles(new[] { p });
			}
		}
		updatelist();
		hud($"已加入 {n} 项");
		if (list.Current == null) {
			if (first.Length > 0) OpenFile(first);
			else if (list.Items.Count > 0) {
				list.Select(0);
				playitem(list.Current);
			}
		}
		e.Handled = true;
	}
	#endregion

	// ==================== 字幕与歌词 ====================
	#region
	void autotext(string media) {
		sub = null;
		lyric = null;
		var lrc = TimedTextFinder.FindLyric(media);
		if (lrc != null) lyric = TimedTextParser.Parse(lrc);
		var any = TimedTextFinder.Find(media);
		if (any != null) {
			var f = TimedTextParser.Parse(any);
			if (f != null) {
				if (f.IsLyric) lyric ??= f;
				else sub = f;
			}
		}
		layer.Clear();
	}

	void togglesub() {
		if (layer.Karaoke) {
			layer.Karaoke = false;
			hud("字幕关闭");
		}
		else if (!layer.Karaoke) {
			layer.Karaoke = true;
			hud("字幕开启");
		}
	}

	void opensub() {
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Title = "打开字幕/歌词",
			Filter = "字幕|*.srt;*.vtt;*.ass;*.ssa|歌词|*.lrc|所有文件|*.*",
		};
		if (dlg.ShowDialog(this) != true) return;
		var f = TimedTextParser.Parse(dlg.FileName);
		if (f == null) { hud("无法解析这个字幕文件"); return; }
		if (f.IsLyric) lyric = f;
		else sub = f;
		layer.Clear();
		hud($"已加载 {f.Cues.Count} 条（{f.Encoding}）");
	}

	void updatetext(double pos) {
		var cue = sub?.At(pos) ?? lyric?.At(pos);
		var source = sub?.At(pos) != null ? sub : lyric;
		if (cue == null) {
			layer.SetCue(null, 0);
			if (lastcue.Length > 0) {
				lastcue = "";
				refreshwords("");
			}
			return;
		}
		var p = layer.Karaoke ? Karaoke.Progress(cue, pos, source?.OffsetSec ?? 0) : 1.0;
		layer.SetCue(cue, p);
		// 换句才重排单词，别每次进度跳动都查一遍词典
		if (!string.Equals(cue.Text, lastcue, StringComparison.Ordinal)) {
			lastcue = cue.Text;
			refreshwords(cue.Text);
		}
	}
	#endregion

	// ==================== 查词 ====================
	#region
	void dictqchanged(object sender, TextChangedEventArgs e) {
		lookup(edictq.Text.Trim());
	}

	void lookup(string q) {
		q = (q ?? "").Trim();
		dicthits.Clear();
		if (q.Length == 0) {
			lbouthint.Text = "点字幕或单词里的词，这里显示释义";
			return;
		}
		if (!DictDb.Ready && !DictDb.Init(DictWeb.DbPath)) {
			lbouthint.Text = "词典未安装，请到「安装功能」下载 dict3.bin";
			return;
		}
		var dict = guessdict(q);
		// 显示用 Search（带释义）；SearchBrief 只有词头，不能拿来展示
		var hits = DictDb.Search(q, dict, 30);
		if (hits == null || hits.Count == 0) {
			lbouthint.Text = "没有找到「" + q + "」";
			return;
		}
		lbouthint.Text = $"{q} · {hits.Count} 条";
		foreach (var h in hits) {
			dicthits.Add(new DictRow {
				Id = h.Id,
				Word = q,
				Title = h.Kanji.Length > 0 && h.Kanji != h.Headword ? $"{h.Headword}（{h.Kanji}）" : h.Headword,
				Preview = h.Preview,
			});
		}
	}

	/// <summary>当前这句字幕拆词：CJK 走词典最长匹配，拉丁按整词查，命中才列出来。</summary>
	void refreshwords(string text) {
		words.Clear();
		lbwordcount.Text = "";
		if (string.IsNullOrWhiteSpace(text)) {
			lbwordhint.Visibility = Visibility.Visible;
			lbwordhint.Text = "这一句没有字幕";
			return;
		}
		if (!DictDb.Ready && !DictDb.Init(DictWeb.DbPath)) {
			lbwordhint.Visibility = Visibility.Visible;
			lbwordhint.Text = "词典未安装，请到「安装功能」下载 dict3.bin";
			return;
		}
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var i = 0;
		while (i < text.Length && words.Count < 60) {
			var cls = WordPick.ClassOf(text[i]);
			if (cls == WordPick.CharClass.Latin) {
				var s = i;
				while (i < text.Length && WordPick.ClassOf(text[i]) == WordPick.CharClass.Latin) i++;
				addword(text.Substring(s, i - s), seen);
				continue;
			}
			if (cls != WordPick.CharClass.Han && cls != WordPick.CharClass.Kana && cls != WordPick.CharClass.Hangul) {
				i++;
				continue;
			}
			var dict = guessdict(text.Substring(i, 1));
			var best = WordPick.Longest(text, i, w => DictDb.SearchBrief(w, dict, 1) is { Count: > 0 }, 6);
			if (best.Length > 0) {
				addword(best, seen);
				i += best.Length;
			}
			else i++;
		}
		lbwordhint.Visibility = words.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		if (words.Count == 0) lbwordhint.Text = "这句里没有查到词";
		lbwordcount.Text = words.Count > 0 ? $"{words.Count} 词" : "";
	}

	/// <summary>查一次词头，把释义摘要带给单词小片；同一个词只留一个。</summary>
	void addword(string word, HashSet<string> seen) {
		if (word.Length == 0 || !seen.Add(word)) return;
		var brief = DictDb.SearchBrief(word, guessdict(word), 1);
		var prev = brief != null && brief.Count > 0 ? brief[0].Preview : "";
		words.Add(new WordItem { Word = word, Preview = prev ?? "" });
	}

	/// <summary>点单词小片：填进词典页并切过去。</summary>
	void wordclick(object sender, RoutedEventArgs e) {
		if (sender is not FrameworkElement fe || fe.Tag is not string w || w.Length == 0) return;
		tabside.SelectedItem = tabdict;
		setside(true);
		if (string.Equals(edictq.Text, w, StringComparison.Ordinal)) lookup(w);
		else edictq.Text = w;
	}


	static string guessdict(string q) {
		if (q.Length == 0) return "";
		var c = q[0];
		var cls = WordPick.ClassOf(c);
		return cls switch {
			WordPick.CharClass.Hangul => "ko",
			WordPick.CharClass.Kana => "ja",
			WordPick.CharClass.Han => "zh",
			WordPick.CharClass.Latin => "en",
			_ => "",
		};
	}

	void pickword(Point pt) {
		var f = sub != null && sub.At(session.PositionSec) != null ? sub : lyric;
		if (f == null) return;
		var cue = f.At(session.PositionSec);
		if (cue == null) return;
		// 定位落在哪一行、哪个字
		if (!layer.HitTest(pt, out var li, out var ci)) return;
		var line = layer.TextOf(li);
		if (string.IsNullOrEmpty(line)) return;
		var raw = WordPick.Raw(line, ci, out _, out _);
		if (raw.Length == 0) return;
		var word = raw;
		// CJK 走词典最长匹配：整窗内长优先，点「书」在「图书馆里」出「图书馆」
		if (WordPick.ClassOf(line[ci]) is WordPick.CharClass.Han or WordPick.CharClass.Kana or WordPick.CharClass.Hangul) {
			if (!DictDb.Ready && !DictDb.Init(DictWeb.DbPath)) {
				lbouthint.Text = "词典未安装，请到「安装功能」下载 dict3.bin";
				tabside.SelectedItem = tabdict;
				setside(true);
				return;
			}
			var dict = guessdict(line.Substring(ci, 1));
			var best = WordPick.Longest(line, ci, w => DictDb.SearchBrief(w, dict, 1) is { Count: > 0 }, 6);
			if (best.Length > 0) word = best;
		}
		tabside.SelectedItem = tabdict;
		setside(true);
		if (string.Equals(edictq.Text, word, StringComparison.Ordinal)) lookup(word);
		else edictq.Text = word;
	}
	#endregion

	// ==================== 时钟与进度 ====================
	#region
	void updateclock(double pos) {
		var loaded = session.HasVideo || session.HasAudio;
		var dur = session.DurationSec > 0 ? session.DurationSec : loaded ? list.Current?.DurationSec ?? 0 : 0;
		lbtime.Text = loaded ? $"{fmt(pos)} / {(dur > 0 ? fmt(dur) : "--:--")}" : "00:00 / 00:00";
		if (!seeking && dur > 0) {
			var v = pos / dur;
			sseek.Value = v < 0 ? 0 : v > 1 ? 1 : v;
		}
	}

	static string fmt(double sec) {
		if (sec < 0 || double.IsNaN(sec)) sec = 0;
		var t = (int)Math.Round(sec);
		var h = t / 3600;
		var m = t % 3600 / 60;
		var s = t % 60;
		return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
	}

	void seekstart(object sender, RoutedEventArgs e) {
		seeking = true;
	}

	void seekdone(object sender, RoutedEventArgs e) {
		seeking = false;
		var dur = session.DurationSec;
		if (dur <= 0) return;
		session.Seek(sseek.Value * dur);
		lastpos = -1;
	}

	void seekchanged(object sender, RoutedEventArgs e) {
		if (!seeking) return;
		var dur = session.DurationSec;
		if (dur <= 0) return;
		var pos = sseek.Value * dur;
		lbtime.Text = $"{fmt(pos)} / {fmt(dur)}";
	}
	#endregion

	// ==================== 窗口形态 ====================
	#region


	public void SetForm(PlayerForm f) => applyform(f, true);

	/// <summary>自测用：模拟点击字幕第 lineIdx 行第 charIdx 个字，返回取到的词和释义摘要。</summary>
	public string ClickChar(int lineIdx, int charIdx) {
		if (layer.Metrics.Count == 0) return "无字幕";
		if (lineIdx < 0 || lineIdx >= layer.Metrics.Count) lineIdx = 0;
		var l = layer.Metrics[lineIdx];
		if (l.Bounds == null || charIdx < 0 || charIdx + 1 >= l.Bounds.Length) return "字符越界";
		var line = layer.TextOf(lineIdx);
		if (string.IsNullOrEmpty(line)) return "空行";
		var x = (l.Bounds[charIdx] + l.Bounds[charIdx + 1]) / 2;
		var y = l.Height / 2;
		layer.LineOrigin(lineIdx, out var left, out var top);
		var pt = new Point(left + x, top + y);
		var before = edictq.Text;
		pickword(pt);
		var got = edictq.Text;
		var outText = dicthits.Count > 0
			? (dicthits[0].Title + " " + dicthits[0].Preview).Replace("\n", " ")
			: lbouthint.Text ?? "";
		if (outText.Length > 70) outText = outText.Substring(0, 70) + "…";
		return $"行{lineIdx}字{charIdx}='{line[charIdx]}' 之前='{before}' 取词='{got}' 释义='{outText}'";
	}

	/// <summary>自测用：跳到指定秒并刷新字幕。</summary>
	public void SeekTo(double sec) {
		if (sec < 0) return;
		session.Seek(sec);
		lastpos = -1;
		updatetext(session.PositionSec);
		updateclock(session.PositionSec);
	}


	/// <summary>离开 bar/small 前记住的窗口尺寸，切回大窗口时还原。</summary>
	bool hassavedrect;
	double savedw, savedh, savedl, savedt;



	/// <summary>只有大窗口与全屏两种形态：全屏藏标题栏和侧栏，控制条随鼠标收起。</summary>
	void applyform(PlayerForm f, bool animate) {
		_ = animate;
		form = f;
		var full = f == PlayerForm.Fullscreen;
		if (full) {
			if (!fullscreen) {
				prevstate = WindowState;
				if (WindowState == WindowState.Normal) {
					savedw = Width;
					savedh = Height;
					savedl = Left;
					savedt = Top;
					hassavedrect = true;
				}
			}
			fullscreen = true;
			WindowStyle = WindowStyle.None;
			ResizeMode = ResizeMode.NoResize;
			WindowState = WindowState.Normal;
			var (l, t, w, h) = ScreenDpi.VirtualScreenPixels();
			ScreenDpi.PinWindowToPhysical(this, l, t, w, h);
			Topmost = true;
			rowHead.Height = new GridLength(0);
			phead.Visibility = Visibility.Collapsed;
			pside.Visibility = Visibility.Collapsed;
			spside.Visibility = Visibility.Collapsed;
			colSide.Width = new GridLength(0);
			autohide = true;
			showpanel(false);
			bfull.Content = "退出全屏";
		}
		else {
			fullscreen = false;
			WindowStyle = WindowStyle.SingleBorderWindow;
			ResizeMode = ResizeMode.CanResize;
			WindowState = WindowState.Normal;
			if (hassavedrect) {
				try {
					Left = savedl;
					Top = savedt;
					Width = savedw;
					Height = savedh;
				}
				catch { }
			}
			if (prevstate == WindowState.Maximized) WindowState = WindowState.Maximized;
			Topmost = topmost;
			rowHead.Height = new GridLength(1, GridUnitType.Auto);
			phead.Visibility = Visibility.Visible;
			autohide = false;
			showpanel(true);
			setside(sideopen);
			bfull.Content = "全屏";
		}
		layer.relayout();
		// 尺寸刚改过，ActualWidth/Height 要等一轮布局才更新；补一次排版，否则字幕按旧高度定位
		Dispatcher.BeginInvoke(new Action(() => {
			layer.relayout();
			if (session.HasVideo) drawframe();
		}), DispatcherPriority.Loaded);
	}

	void setfull(bool on) => applyform(on ? PlayerForm.Fullscreen : PlayerForm.Large, true);

	/// <summary>右侧栏显隐；全屏时强制收起，退出全屏按 sideopen 还原。</summary>
	void setside(bool open) {
		sideopen = open;
		var show = open && !fullscreen;
		pside.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		spside.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		colSide.Width = new GridLength(show ? 320 : 0);
		bside.Content = open ? "收起侧栏" : "侧栏";
	}


	bool topmost;
	void togglepin() {
		topmost = !topmost;
		Topmost = topmost;
		btop.Content = topmost ? "取消置顶" : "置顶";
		hud(topmost ? "已置顶" : "已取消置顶");
	}

	/// <summary>面板显隐；fullscreen/small 下靠鼠标活动自动收起。</summary>
	/// <summary>只有全屏才自动收起控制条。</summary>
	void showpanel(bool show) {
		if (!fullscreen) {
			pbar.Visibility = Visibility.Visible;
			Cursor = Cursors.Arrow;
			return;
		}
		pbar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		Cursor = show ? Cursors.Arrow : Cursors.None;
	}

	void checkhide() {
		if (!autohide || !mousein) return;
		if (Environment.TickCount - lastmove < 2500) return;
		// 鼠标还在控制条上就别收
		if (pbar.IsMouseOver) return;
		showpanel(false);
	}
	#endregion

	// ==================== 鼠标与键盘 ====================
	#region
	void stagedown(object sender, MouseButtonEventArgs e) {
		var now = Environment.TickCount;
		if (now - lastclick < 400) clickcount++;
		else clickcount = 1;
		lastclick = now;
		dragging = true;
		dragfrom = e.GetPosition(this);
		dragl = Left;
		dragt = Top;
		e.Handled = false;
	}

	void stagemove(object sender, MouseEventArgs e) {
		if (!dragging || e.LeftButton != MouseButtonState.Pressed) { dragging = false; return; }
		if (fullscreen) return;
		var p = e.GetPosition(this);
		var dx = p.X - dragfrom.X;
		var dy = p.Y - dragfrom.Y;
		if (Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
		try {
			Left = dragl + dx;
			Top = dragt + dy;
		}
		catch { }
	}

	void stageup(object sender, MouseButtonEventArgs e) {
		var moved = false;
		var p = e.GetPosition(this);
		if (Math.Abs(p.X - dragfrom.X) >= 4 || Math.Abs(p.Y - dragfrom.Y) >= 4) moved = true;
		dragging = false;
		if (moved) return;
		if (clickcount >= 2) {
			clickcount = 0;
			setfull(!fullscreen);
			return;
		}
		// 单击：字幕区点词
		var pt = e.GetPosition(ptext);
		pickword(pt);
	}

	void stagewheel(object sender, MouseWheelEventArgs e) {
		var step = e.Delta > 0 ? 0.05f : -0.05f;
		if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
			var off = (sub?.OffsetSec ?? 0) + (e.Delta > 0 ? 0.05 : -0.05);
			if (sub != null) sub.OffsetSec = off;
			if (lyric != null) lyric.OffsetSec = off;
			hud($"字幕延迟 {off * 1000:+0;-0;0} ms");
			return;
		}
		var v = (float)(svol.Value + step);
		if (v < 0) v = 0;
		if (v > 1) v = 1;
		svol.Value = v;
		hud($"音量 {(int)(v * 100)}%");
	}

	void onkey(object sender, KeyEventArgs e) {
		switch (e.Key) {
			case Key.Space: toggleplay(); e.Handled = true; break;
			case Key.Left: seekrel(-5); e.Handled = true; break;
			case Key.Right: seekrel(5); e.Handled = true; break;
			case Key.Up: { var v = Math.Min(1.0, svol.Value + 0.05); svol.Value = v; e.Handled = true; break; }
			case Key.Down: { var v = Math.Max(0.0, svol.Value - 0.05); svol.Value = v; e.Handled = true; break; }
			case Key.Escape:
				if (fullscreen) applyform(PlayerForm.Large, true);
				else Close();
				e.Handled = true;
				break;
			case Key.F: setfull(!fullscreen); e.Handled = true; break;
			case Key.S: togglesub(); e.Handled = true; break;
			case Key.H: setside(!sideopen); e.Handled = true; break;
			case Key.N: playnext(true); e.Handled = true; break;
			case Key.P: playprev(); e.Handled = true; break;
			case Key.M: togglemute(); e.Handled = true; break;
		}
	}

	void seekrel(double delta) {
		var dur = session.DurationSec;
		if (dur <= 0) return;
		var target = session.PositionSec + delta;
		if (target < 0) target = 0;
		if (target > dur) target = dur;
		session.Seek(target);
		lastpos = -1;
		hud($"{fmt(target)}");
	}

	void togglemute() {
		if (svol.Value > 0) {
			paused_at = svol.Value;
			svol.Value = 0;
			bmute.Content = "🔇";
		}
		else {
			svol.Value = paused_at > 0 ? paused_at : 0.8;
			bmute.Content = "🔊";
		}
	}
	#endregion

	// ==================== 杂项 ====================
	#region
	int hudgen;
	void hud(string msg) {
		lbhud.Text = msg;
		phud.Visibility = Visibility.Visible;
		var gen = ++hudgen;
		var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
		t.Tick += (_, _) => {
			t.Stop();
			if (gen == hudgen) phud.Visibility = Visibility.Collapsed;
		};
		t.Start();
	}

	double savedvol = 0.8;

	/// <summary>自测用：把整个窗口渲染成 PNG，并回传界面上关键状态。不依赖屏幕截图。</summary>
	public string ShotTo(string path) {
		var scale = ScreenDpi.SystemScale();
		if (scale <= 0) scale = 1;
		var w = (int)Math.Round(ActualWidth * scale);
		var h = (int)Math.Round(ActualHeight * scale);
		var info = $"form={form} size={ActualWidth:0}x{ActualHeight:0} dpi={scale:0.##} " +
			$"rowHead={rowHead.ActualHeight:0} side={(sideopen ? "on" : "off")} tabs={(tabside.SelectedItem as TabItem)?.Header} " +
			$"pbar={pbar.Visibility} phead={phead.Visibility} words={words.Count} hits={dicthits.Count} " +
			$"cue={(layer.CueText is { Length: > 0 } s ? s : "(空)")} lines={layer.LineCount} " +
			$"kara={layer.Karaoke} pos={session.PositionSec:0.###} hasvideo={session.HasVideo} " +
			$"sub={sub?.Path ?? "(无)"} subn={sub?.Cues.Count ?? 0} lyric={lyric?.Path ?? "(无)"} " +
			$"lay={layer.Describe()}";
		try {
			UpdateLayout();
			// 先按当前实际尺寸补一次排版：窗口刚改过尺寸时，之前的排版是按旧高度算的
			layer.relayout();
			UpdateLayout();
			var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
			// 渲染根 Grid 而不是 Window：窗口带 Opacity/Topmost 时 RTB 会走图层合成，
			// 图形位置和实际显示对不上；proot 才是内容本身。
			rtb.Render(proot);
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(rtb));
			using var fs = File.Create(path);
			enc.Save(fs);
			info += $" saved={path}";
			// 附带导出一份"字幕行在屏幕上的实际位置"，用于核对排版是否正确
			try {
				layer.LineOrigin(0, out var lx, out var ly);
				if (layer.Metrics.Count > 0 && !double.IsNaN(lx) && !double.IsNaN(ly)) {
					var m = layer.Metrics[0];
					var pt = ptext.PointToScreen(new Point(lx, ly));
					info += $" line0screen={pt.X:0},{pt.Y:0} size=({(int)m.Width}x{(int)m.Height})";
				}
			}
			catch { }
		}
		catch (Exception ex) {
			info += " FAIL " + ex.Message;
		}
		return info;
	}
	#endregion
}
