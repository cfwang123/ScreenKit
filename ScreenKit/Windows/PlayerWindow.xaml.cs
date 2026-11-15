using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScreenKit;

public enum PlayerForm { Normal, Fullscreen }

enum SideTab { List, Study, Subtitle }

enum StudyLang { All, Zh, Ja, Ko, En }

/// <summary>学习面板里的一行：本句命中的词、释义摘要、语种与词区间。</summary>
sealed class StudyWordRow : INotifyPropertyChanged {
	public string Word { get; set; } = "";
	public string Title { get; set; } = "";
	public string Gloss { get; set; } = "";
	public StudyLang Lang { get; set; } = StudyLang.En;
	public int CharStart { get; set; }
	public int CharLen { get; set; }
	public double Start { get; set; }
	public double End { get; set; }
	public bool Exact { get; set; }
	public bool Manual { get; set; }

	bool sounding;
	public bool IsSounding {
		get => sounding;
		set {
			if (sounding == value) return;
			sounding = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSounding)));
		}
	}
	public event PropertyChangedEventHandler PropertyChanged;
}

/// <summary>字幕浏览器里的一行。</summary>
sealed class CueRow : INotifyPropertyChanged {
	public int Index { get; set; }
	public string No { get; set; } = "";
	public string TimeText { get; set; } = "";
	public string Text { get; set; } = "";
	public double Start { get; set; }
	public double End { get; set; }

	bool current;
	bool sounding;
	public bool IsCurrent {
		get => current;
		set {
			if (current == value) return;
			current = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
		}
	}
	public bool IsSounding {
		get => sounding;
		set {
			if (sounding == value) return;
			sounding = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSounding)));
		}
	}
	public event PropertyChangedEventHandler PropertyChanged;
}

/// <summary>界面此刻的唯一描述。除 MediaSession / Playlist 外，UI 不再持有第二份播放状态。</summary>
sealed class PlayerViewState {
	public PlayerForm Mode = PlayerForm.Normal;
	public bool SideOpen = true;
	public bool SideOpenBeforeFull = true;
	public SideTab Tab = SideTab.Study;
	public double SideWidth = 320;

	public bool Paused = true;
	public PlayMode Repeat = PlayMode.Sequential;
	public bool SubtitleOn = true;
	public bool Muted = false;
	public double Volume = 0.8;
	public bool Topmost = false;

	public string StudyQuery = "";
	public StudyLang Lang = StudyLang.All;
	public string ManualWord = "";
	public string SubtitleQuery = "";
	public string ClipKey = "";

	public int LastActivityTick;
	public bool PointerOverBar;
}

/// <summary>媒体播放窗口：大窗口为主可切全屏；右栏分列表 / 学习 / 字幕三个任务域。</summary>
public partial class PlayerWindow : Window {
	// ==================== 状态 ====================
	#region
	const int AUTOHIDEMS = 2500;

	readonly MediaSession session = new();
	readonly Playlist list = new();
	readonly MediaClipPlayer clip = new();
	readonly PlayerViewState state = new();
	readonly OcrOptions opt;

	TimedTextLayer layer;
	TimedTextFile sub;
	TimedTextFile lyric;

	WriteableBitmap bmp;
	long lastseq = -1;
	double lastpos = -1;
	bool seeking;
	bool dragging;
	bool dragsub;
	bool subnudge;
	Point dragfrom;
	Point subfrom;
	double subbottom0;
	double dragl, dragt;
	WindowState prevstate;
	bool fullscreen;
	bool hassavedrect;
	double savedw, savedh, savedl, savedt;
	int lastclick;
	int clickcount;
	string lastcue = "";
	double savedvol = 0.8;
	double paused_at;
	int hudgen;
	SoundPlayer ttsplayer;

	readonly List<StudyWordRow> allwords = new();
	readonly ObservableCollection<StudyWordRow> words = new();
	readonly List<CueRow> allcues = new();
	readonly ObservableCollection<CueRow> cues = new();
	string builtwordsig = "";
	string builtsubq = null;

	DispatcherTimer timer;

	internal Playlist Playlist => list;
	#endregion

	// ==================== 构造 ====================
	#region
	public PlayerWindow(OcrOptions opt = null) {
		InitializeComponent();
		this.opt = opt;
		layer = new TimedTextLayer(ptext);
		lcwords.ItemsSource = words;
		lbcues.ItemsSource = cues;
		lblist.ItemsSource = list.Items;
		wire();
		list.Changed += () => updatelist();
		list.ItemRequested += (item, _) => playitem(item);
		session.Failed += msg => Dispatcher.BeginInvoke(new Action(() => hud(msg)));
		clip.Ended += () => Dispatcher.BeginInvoke(new Action(() => { state.ClipKey = ""; render(); }));
		clip.Failed += msg => Dispatcher.BeginInvoke(new Action(() => hud(msg)));

		AllowDrop = true;
		Drop += ondrop;
		DragOver += ondragover;
		pstage.AllowDrop = true;
		pstage.Drop += stagedrop;
		pstage.DragOver += ondragover;
		tablist.AllowDrop = true;
		tablist.Drop += listdrop;
		tablist.DragOver += ondragover;
		PreviewKeyDown += onkey;
		Loaded += (_, _) => {
			loadopts();
			list.Load();
			updatelist();
			lbtime.Text = "00:00 / 00:00";
			sseek.Value = 0;
			bplay.Content = "▶";
			starttimer();
			applyform(state.Mode);
		};
		Closing += (_, _) => {
			saveopts();
			try { clip.Dispose(); } catch { }
			try { ttsplayer?.Stop(); } catch { }
			try { session.Close(); } catch { }
			try { list.Save(); } catch { }
		};
	}

	void loadopts() {
		var o = opt;
		state.Volume = o != null && o.PlayVolume > 0 ? o.PlayVolume : 0.8;
		savedvol = state.Volume;
		svol.Value = state.Volume;
		session.SetVolume((float)state.Volume);
		if (o == null) return;
		layer.FontSize = o.PlaySubSize > 0 ? o.PlaySubSize : 32;
		layer.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(o.PlaySubFont) ? "Microsoft YaHei UI" : o.PlaySubFont);
		layer.SungColor = ColorUtil.Parse(o.PlayKaraSung, Color.FromRgb(0xFF, 0xD8, 0x4D));
		layer.UnsungColor = ColorUtil.Parse(o.PlayKaraUnsung, Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
		layer.OutlineColor = ColorUtil.Parse(o.PlaySubOutline, Color.FromArgb(0xCC, 0, 0, 0));
		// 0 是用户拖到最底的真实值，配置里有该键才读得到；没该键时字段保持默认 48
		layer.BottomMargin = o.PlaySubBottom >= 0 ? o.PlaySubBottom : 48;
		layer.SnapToChar = string.Equals(o.PlayKaraMode, "char", StringComparison.OrdinalIgnoreCase);
		layer.FeatherPx = o.PlayKaraFeather;
		layer.Karaoke = true;
		state.SubtitleOn = o.PlaySubOn;
		list.Mode = Playlist.modeparse(o.PlayListMode);
		state.Repeat = list.Mode;
		state.Mode = string.Equals((o.PlayMode ?? "").Trim(), "fullscreen", StringComparison.OrdinalIgnoreCase)
			? PlayerForm.Fullscreen : PlayerForm.Normal;
	}

	void saveopts() {
		var o = opt;
		if (o == null) return;
		o.PlayVolume = state.Volume;
		o.PlayMode = state.Mode == PlayerForm.Fullscreen ? "fullscreen" : "normal";
		o.PlaySubOn = state.SubtitleOn;
		o.PlaySubBottom = (int)Math.Round(Math.Max(0, layer.BottomMargin));
		o.PlayListMode = state.Repeat switch {
			PlayMode.Shuffle => "shuffle",
			PlayMode.RepeatOne => "repeatone",
			PlayMode.RepeatAll => "repeatall",
			_ => "sequential",
		};
	}
	#endregion

	// ==================== 事件绑定 ====================
	#region
	void wire() {
		bplay.Click += (_, _) => toggleplay();
		bprev.Click += (_, _) => playprev();
		bnext.Click += (_, _) => playnext(true);
		bfull.Click += (_, _) => togglefull();
		bside.Click += (_, _) => toggleside();
		btop.Click += (_, _) => togglepin();
		bopen.Click += (_, _) => openfile();
		bsub.Click += (_, _) => togglesub();
		bsubopen.Click += (_, _) => opensub();
		bmute.Click += (_, _) => togglemute();
		bcopy.Click += (_, _) => copyword();

		rbseq.Checked += (_, _) => setmode(PlayMode.Sequential);
		rbshuf.Checked += (_, _) => setmode(PlayMode.Shuffle);
		rbone.Checked += (_, _) => setmode(PlayMode.RepeatOne);
		rball.Checked += (_, _) => setmode(PlayMode.RepeatAll);
		baddfile.Click += (_, _) => addfiles();
		badddir.Click += (_, _) => adddir();
		bclearlist.Click += (_, _) => { list.Clear(); updatelist(); };
		bdropmiss.Click += (_, _) => {
			var n = list.RemoveMissing();
			hud(n > 0 ? $"已清理 {n} 项" : "没有失效项");
			updatelist();
		};

		svol.ValueChanged += (_, _) => {
			state.Volume = svol.Value;
			state.Muted = svol.Value <= 0.0001;
			session.SetVolume((float)state.Volume);
			clip.Volume = (float)state.Volume;
			if (!state.Muted) savedvol = state.Volume;
			lbvol.Text = (int)Math.Round(state.Volume * 100) + "%";
		};

		// 全屏时控制条随活动自动隐藏，任何鼠标移动都算一次活动
		MouseMove += (_, _) => { state.LastActivityTick = Environment.TickCount; };

		pbar.MouseEnter += (_, _) => { state.PointerOverBar = true; };
		pbar.MouseLeave += (_, _) => { state.PointerOverBar = false; };

		tabside.SelectionChanged += (_, _) => {
			var want = tabside.SelectedItem == tablist ? SideTab.List
				: tabside.SelectedItem == tabsubtitle ? SideTab.Subtitle : SideTab.Study;
			if (state.Tab == want) return;
			state.Tab = want;
			render();
		};
		popmode.Closed += (_, _) => { bmode.IsChecked = false; };
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
		if (session.IsEnded && !session.IsPaused) {
			session.Pause();
			state.Paused = true;
			onended();
		}
		render();
	}

	/// <summary>控制条/顶栏是否可见：不存成字段，每帧由 state 现算。</summary>
	bool controlsvisible(PlayerViewState s) =>
		s.Mode == PlayerForm.Normal
		|| s.Paused
		|| s.PointerOverBar
		|| Environment.TickCount - s.LastActivityTick < AUTOHIDEMS;
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
			var len = Math.Min(f.Stride, bmp.BackBufferStride);
			for (var y = 0; y < f.Height; y++)
				Marshal.Copy(f.Bgra, y * f.Stride, back + y * bmp.BackBufferStride, len);
			bmp.AddDirtyRect(new Int32Rect(0, 0, f.Width, f.Height));
		}
		finally {
			bmp.Unlock();
		}
	}
	#endregion

	// ==================== 唯一渲染出口 ====================
	#region
	/// <summary>唯一允许把 state 写到控件的地方。事件处理只改 state 再调用它。</summary>
	void render() {
		var s = state;
		var full = s.Mode == PlayerForm.Fullscreen;
		var bars = controlsvisible(s);
		var showside = !full && s.SideOpen;

		rowHead.Height = full ? new GridLength(0) : GridLength.Auto;
		phead.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
		colSide.Width = new GridLength(showside ? s.SideWidth : 0);
		pside.Visibility = showside ? Visibility.Visible : Visibility.Collapsed;
		spside.Visibility = showside ? Visibility.Visible : Visibility.Collapsed;
		fsHint.Visibility = full ? Visibility.Visible : Visibility.Collapsed;

		tabside.SelectedItem = s.Tab == SideTab.List ? tablist : s.Tab == SideTab.Subtitle ? tabsubtitle : tabstudy;
		bside.IsChecked = s.SideOpen;
		bside.ToolTip = s.SideOpen ? "收起侧栏 (H)" : "展开侧栏 (H)";

		pbar.Visibility = bars ? Visibility.Visible : Visibility.Collapsed;
		Cursor = full && !bars ? Cursors.None : Cursors.Arrow;

		bplay.Content = s.Paused ? "▶" : "⏸";
		bplay.ToolTip = s.Paused ? "播放 (空格)" : "暂停 (空格)";
		bsub.IsChecked = s.SubtitleOn;
		btop.IsChecked = s.Topmost;
		bmute.IsChecked = s.Muted;
		svol.Value = s.Muted ? 0 : s.Volume;
		lbvol.Text = (int)Math.Round((s.Muted ? 0 : s.Volume) * 100) + "%";
		lbmode.Text = "播放模式：" + modename(s.Repeat);
		bmode.ToolTip = "播放模式（当前：" + modename(s.Repeat) + "）";
		rbseq.IsChecked = s.Repeat == PlayMode.Sequential;
		rbshuf.IsChecked = s.Repeat == PlayMode.Shuffle;
		rbone.IsChecked = s.Repeat == PlayMode.RepeatOne;
		rball.IsChecked = s.Repeat == PlayMode.RepeatAll;
		lbtitle.Text = list.Current != null ? list.Current.Display : "媒体播放器";

		langall.IsChecked = s.Lang == StudyLang.All;
		langzh.IsChecked = s.Lang == StudyLang.Zh;
		langja.IsChecked = s.Lang == StudyLang.Ja;
		langko.IsChecked = s.Lang == StudyLang.Ko;
		langen.IsChecked = s.Lang == StudyLang.En;

		updwordlist();
		updcuelist();
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
			clip.Stop();
			state.ClipKey = "";
			session.Open(item.Path);
			session.SetVolume((float)state.Volume);
			bmp = null;
			lastseq = -1;
			lastpos = -1;
			imgvideo.Source = null;
			Title = item.Display + " - 媒体播放器";
			lbtitle.Text = item.Display;
			lbempty.Visibility = Visibility.Collapsed;
			autotext(item.Path);
			session.Play();
			state.Paused = false;
			sseek.Value = 0;
			if (item.DurationSec <= 0 && session.DurationSec > 0) {
				item.DurationSec = session.DurationSec;
				list.RaiseChanged();
			}
			updateclock(0);
			render();
		}
		catch (Exception ex) {
			hud("打开失败: " + ex.Message);
		}
	}

	void toggleplay() {
		if (list.Current == null) { openfile(); return; }
		session.TogglePause();
		state.Paused = session.IsPaused;
		if (state.Paused) stopclip();
		state.LastActivityTick = Environment.TickCount;
		render();
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
			state.Paused = true;
			render();
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
		lblistcount.Text = $"{list.Items.Count} 项";
		probedurations();
		render();
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
		Task.Run(() => {
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
		if (lblist.SelectedItem is not PlaylistItem item) return;
		var idx = list.Items.IndexOf(item);
		if (idx >= 0) list.Select(idx);
		playitem(item);
	}

	void setmode(PlayMode m) {
		if (state.Repeat == m) return;
		state.Repeat = m;
		list.Mode = m;
		hud("播放模式：" + modename(m));
		render();
	}

	string modename(PlayMode m) => m switch {
		PlayMode.Shuffle => "随机播放",
		PlayMode.RepeatOne => "单曲循环",
		PlayMode.RepeatAll => "列表循环",
		_ => "顺序播放",
	};

	void modetoggle(object sender, RoutedEventArgs e) {
		popmode.IsOpen = bmode.IsChecked == true;
	}

	void ondragover(object sender, DragEventArgs e) {
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	// 播放区：清空列表，只留这一个并播放
	void stagedrop(object sender, DragEventArgs e) {
		if (dropfiles(e) is not string[] paths) return;
		e.Handled = true;
		var media = firstmedia(paths);
		if (media.Length == 0) { hud("不是可播放的音视频文件"); return; }
		list.Clear();
		addall(media);
		updatelist();
		if (list.Items.Count == 0) { hud("不是可播放的音视频文件"); return; }
		list.Select(0);
		playitem(list.Current);
	}

	// 列表区：追加到末尾
	void listdrop(object sender, DragEventArgs e) {
		if (dropfiles(e) is not string[] paths) return;
		e.Handled = true;
		var n = addall(paths);
		updatelist();
		hud($"已加入 {n} 项");
	}

	void ondrop(object sender, DragEventArgs e) {
		if (dropfiles(e) is not string[] paths) return;
		var replace = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		if (replace) list.Clear();
		var n = addall(paths);
		updatelist();
		hud($"已加入 {n} 项");
		if (list.Current == null && list.Items.Count > 0) {
			list.Select(0);
			playitem(list.Current);
		}
		e.Handled = true;
	}

	string[] dropfiles(DragEventArgs e) {
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
		return e.Data.GetData(DataFormats.FileDrop) as string[];
	}

	int addall(string[] paths) {
		var n = 0;
		foreach (var p in paths) {
			if (Directory.Exists(p)) n += list.AddDirectory(p);
			else if (MediaFormat.IsMedia(p)) n += list.AddFiles(new[] { p });
		}
		return n;
	}

	// 目录和媒体文件照收，其它过滤掉
	string[] firstmedia(string[] paths) {
		var keep = new List<string>();
		foreach (var p in paths) {
			if (Directory.Exists(p) || MediaFormat.IsMedia(p)) keep.Add(p);
		}
		return keep.ToArray();
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
		lastcue = "";
		allwords.Clear();
		words.Clear();
		buildcurcue(null);
		buildcuelist();
		builtwordsig = "";
		render();
	}

	void togglesub() {
		state.SubtitleOn = !state.SubtitleOn;
		hud(state.SubtitleOn ? "字幕开启" : "字幕关闭");
		lastpos = -1;
		render();
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
		lastcue = "";
		buildcuelist();
		hud($"已加载 {f.Cues.Count} 条（{f.Encoding}）");
		render();
	}

	void updatetext(double pos) {
		var cue = sub?.At(pos) ?? lyric?.At(pos);
		var source = sub?.At(pos) != null ? sub : lyric;
		if (cue == null) {
			layer.SetCue(null, 0);
			if (lastcue.Length > 0) {
				lastcue = "";
				allwords.Clear();
				words.Clear();
				buildcurcue(null);
				builtwordsig = "";
			}
			updcurrentcue(pos);
			return;
		}
		var p = state.SubtitleOn ? Karaoke.Progress(cue, pos, source?.OffsetSec ?? 0) : 1.0;
		layer.SetCue(state.SubtitleOn ? cue : null, p);
		if (!string.Equals(cue.Text, lastcue, StringComparison.Ordinal)) {
			lastcue = cue.Text;
			buildwords(cue);
			buildcurcue(cue);
			builtwordsig = "";
		}
		updcurrentcue(pos);
	}
	#endregion

	// ==================== 本句生词表 ====================
	#region
	void buildwords(TimedTextCue cue) {
		allwords.Clear();
		if (cue == null) { applylang(); return; }
		if (!DictDb.Ready && !DictDb.Init(DictWeb.DbPath)) { applylang(); return; }
		var text = cue.Text;
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var i = 0;
		while (i < text.Length && allwords.Count < 80) {
			var cls = WordPick.ClassOf(text[i]);
			if (cls == WordPick.CharClass.Latin) {
				var s0 = i;
				while (i < text.Length && WordPick.ClassOf(text[i]) == WordPick.CharClass.Latin) i++;
				addword(text.Substring(s0, i - s0), s0, i - s0, cue, seen);
				continue;
			}
			if (cls is not WordPick.CharClass.Han and not WordPick.CharClass.Kana and not WordPick.CharClass.Hangul) {
				i++;
				continue;
			}
			var dict = guessdict(text.Substring(i, 1));
			var best = WordPick.Longest(text, i, w => DictDb.SearchBrief(w, dict, 1) is { Count: > 0 }, 6);
			if (best.Length > 0) {
				addword(best, i, best.Length, cue, seen);
				i += best.Length;
			}
			else i++;
		}
		applylang();
	}

	void addword(string word, int charStart, int charLen, TimedTextCue cue, HashSet<string> seen) {
		if (word.Length == 0 || !seen.Add(word)) return;
		var dict = guessdict(word);
		var (title, gloss) = lookup(word, dict);
		var (st, en, ex) = wordrange(cue, charStart, charLen);
		allwords.Add(new StudyWordRow {
			Word = word,
			Title = title,
			Gloss = gloss,
			Lang = langof(dict),
			CharStart = charStart,
			CharLen = charLen,
			Start = st,
			End = en,
			Exact = ex,
		});
	}

	/// <summary>
	/// 查一个词的释义摘要。标题保留原文写法（大小写/活用形），
	/// 释义只认精确或长度相近的词条，避免 "makes" 被前缀匹配成 "makeshift"。
	/// </summary>
	static (string title, string gloss) lookup(string word, string dict) {
		var hits = DictDb.Search(word, dict, 8);
		if (hits == null || hits.Count == 0) return (word, "未收录");
		// 同名多条（is / IS）先认写法一致的，再放宽到忽略大小写，最后才接受前缀
		DictHit pick = null;
		foreach (var h in hits) {
			if (string.Equals((h.Headword ?? "").Trim(), word, StringComparison.Ordinal)) { pick = h; break; }
		}
		if (pick == null)
			foreach (var h in hits) {
				if (string.Equals((h.Headword ?? "").Trim(), word, StringComparison.OrdinalIgnoreCase)) { pick = h; break; }
			}
		if (pick == null)
			foreach (var h in hits) {
				var head = (h.Headword ?? "").Trim();
				if (head.Length > word.Length && head.Length <= word.Length + 3 && head.StartsWith(word, StringComparison.OrdinalIgnoreCase)) { pick = h; break; }
			}
		if (pick == null) return (word, "未收录");
		if (!string.IsNullOrEmpty(pick.Preview)) return (word, pick.Preview);
		return (word, string.IsNullOrEmpty(pick.Reading) ? "" : "/" + pick.Reading + "/");
	}

	/// <summary>词的起止时间：有词级时间就用，没有就按字符位置在 cue 内均匀摊。</summary>
	(double start, double end, bool exact) wordrange(TimedTextCue cue, int ci, int cl) {
		if (cue.Words != null && cue.Words.Count > 0) {
			for (var i = 0; i < cue.Words.Count; i++) {
				var w = cue.Words[i];
				if (ci >= w.CharStart && ci < w.CharStart + w.CharLen) {
					var st = w.Start;
					var en = i + 1 < cue.Words.Count ? cue.Words[i + 1].Start : cue.End;
					if (en <= st) en = st + 0.3;
					return (st, en, true);
				}
			}
		}
		var len = Math.Max(1, cue.Text.Length);
		var p0 = clampi(ci, 0, len) / (double)len;
		var p1 = clampi(ci + cl, 0, len) / (double)len;
		var s0 = cue.Start + (cue.End - cue.Start) * p0;
		var s1 = cue.Start + (cue.End - cue.Start) * p1;
		if (s1 <= s0) s1 = s0 + 0.25;
		return (s0, s1, false);
	}

	static double clampi(int v, int a, int b) => v < a ? a : v > b ? b : v;

	void applylang() {
		words.Clear();
		var lang = state.Lang;
		foreach (var r in allwords)
			if (lang == StudyLang.All || r.Lang == lang) words.Add(r);
		if (state.ManualWord.Length > 0 && !words.Any(x => x.Word == state.ManualWord)) {
			var dict = guessdict(state.ManualWord);
			var (title, gloss) = DictDb.Ready ? lookup(state.ManualWord, dict) : (state.ManualWord, "");
			words.Insert(0, new StudyWordRow {
				Word = state.ManualWord,
				Title = title,
				Gloss = gloss,
				Lang = langof(dict),
				Manual = true,
			});
		}
		updwordmeta();
	}

	void updwordlist() {
		var sig = state.ManualWord + "|" + (int)state.Lang + "|" + lastcue;
		if (sig != builtwordsig) {
			builtwordsig = sig;
			applylang();
		}
		var clipword = state.ClipKey.StartsWith("w:", StringComparison.Ordinal) || state.ClipKey.StartsWith("tts:", StringComparison.Ordinal)
			? state.ClipKey.Substring(state.ClipKey.IndexOf(':') + 1) : "";
		foreach (var r in words) r.IsSounding = clipword.Length > 0 && r.Word == clipword;
		var sel = words.FirstOrDefault(x => x.Word == state.StudyQuery);
		if (!ReferenceEquals(lcwords.SelectedItem, sel)) lcwords.SelectedItem = sel;
		if (sel != null) lcwords.ScrollIntoView(sel);
	}

	void updwordmeta() {
		lbwordcount.Text = words.Count + " 条";
		lbwordhint.Visibility = words.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		lbwordhint.Text = lastcue.Length == 0 ? "换到有字幕的位置，这里会列出本句所有生词和释义" : "这一句没有查到词";
		var exact = allwords.Count > 0 && allwords.All(x => x.Exact);
		lbwordmeta.Text = words.Count > 0 ? (exact ? "词级时间" : "按均匀推算") : "";
	}

	void buildcurcue(TimedTextCue cue) {
		lbcurcue.Inlines.Clear();
		if (cue == null) {
			lbcurcue.Inlines.Add(new Run("（当前位置没有字幕）") { Foreground = Brushes.Gray });
			return;
		}
		var text = cue.Text;
		var pos = 0;
		foreach (var w in allwords.OrderBy(x => x.CharStart)) {
			if (w.CharStart < pos || w.CharStart >= text.Length) continue;
			if (w.CharStart > pos) lbcurcue.Inlines.Add(new Run(text.Substring(pos, w.CharStart - pos)));
			var take = Math.Min(w.CharLen, text.Length - w.CharStart);
			if (take <= 0) continue;
			var run = new Run(text.Substring(w.CharStart, take)) { Tag = w.Word, Cursor = Cursors.Hand };
			var captured = w.Word;
			run.MouseLeftButtonDown += (_, e) => {
				selectword(captured);
				e.Handled = true;
			};
			lbcurcue.Inlines.Add(run);
			pos = w.CharStart + take;
		}
		if (pos < text.Length) lbcurcue.Inlines.Add(new Run(text.Substring(pos)));
	}
	#endregion

	// ==================== 字幕浏览器 ====================
	#region
	void buildcuelist() {
		allcues.Clear();
		var f = sub ?? lyric;
		if (f != null) {
			for (var i = 0; i < f.Cues.Count; i++) {
				var c = f.Cues[i];
				allcues.Add(new CueRow {
					Index = i,
					No = (i + 1).ToString(),
					TimeText = fmt(c.Start),
					Text = c.Text.Replace('\n', ' '),
					Start = c.Start,
					End = c.End,
				});
			}
		}
		builtsubq = null;
		applysubq();
	}

	void applysubq() {
		cues.Clear();
		var q = state.SubtitleQuery.Trim();
		foreach (var c in allcues)
			if (q.Length == 0 || c.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) cues.Add(c);
		lbsubcount.Text = $"{cues.Count} / {allcues.Count} 条";
		lbcuetip.Visibility = allcues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	void updcuelist() {
		if (builtsubq != state.SubtitleQuery) {
			builtsubq = state.SubtitleQuery;
			applysubq();
		}
		var clipcue = state.ClipKey.StartsWith("cue:", StringComparison.Ordinal)
			? int.TryParse(state.ClipKey.Substring(4), out var ci) ? ci : -1 : -1;
		foreach (var c in cues) c.IsSounding = c.Index == clipcue;
	}

	void updcurrentcue(double pos) {
		foreach (var c in allcues) c.IsCurrent = pos >= c.Start && pos < c.End;
	}
	#endregion

	// ==================== 查词 ====================
	#region
	void selectword(string word) {
		if (string.IsNullOrEmpty(word)) return;
		if (state.StudyQuery == word) return;
		state.StudyQuery = word;
		state.Tab = SideTab.Study;
		if (state.Mode == PlayerForm.Normal && !state.SideOpen) state.SideOpen = true;
		render();
	}

	/// <summary>按坐标命中字幕里的词；命中返回 true（只选词，不触发新的词典查询）。</summary>
	bool trypickword(Point pt) {
		var f = sub != null && sub.At(session.PositionSec) != null ? sub : lyric;
		if (f == null) return false;
		var cue = f.At(session.PositionSec);
		if (cue == null) return false;
		if (!layer.HitTest(pt, out var li, out var ci)) return false;
		var line = layer.TextOf(li);
		if (string.IsNullOrEmpty(line) || ci >= line.Length) return false;
		var raw = WordPick.Raw(line, ci, out _, out _);
		if (raw.Length == 0) return false;
		var word = raw;
		if (WordPick.ClassOf(line[ci]) is WordPick.CharClass.Han or WordPick.CharClass.Kana or WordPick.CharClass.Hangul) {
			if (!DictDb.Ready && !DictDb.Init(DictWeb.DbPath)) {
				hud("词典未安装，请到「安装功能」下载 dict3.bin");
				return true;
			}
			var dict = guessdict(line.Substring(ci, 1));
			var best = WordPick.Longest(line, ci, w => DictDb.SearchBrief(w, dict, 1) is { Count: > 0 }, 6);
			if (best.Length > 0) word = best;
		}
		selectword(word);
		return true;
	}

	void dictqchanged(object sender, TextChangedEventArgs e) {
		var q = (edictq.Text ?? "").Trim();
		if (state.ManualWord == q) return;
		state.ManualWord = q;
		if (q.Length > 0) state.StudyQuery = q;
		builtwordsig = "";
		render();
	}

	void copyword() {
		if (string.IsNullOrEmpty(state.StudyQuery)) { hud("还没有选中词"); return; }
		try {
			Clipboard.SetText(state.StudyQuery);
			hud("已复制：" + state.StudyQuery);
		}
		catch { hud("复制失败"); }
	}

	static string guessdict(string q) {
		if (string.IsNullOrEmpty(q)) return "";
		return WordPick.ClassOf(q[0]) switch {
			WordPick.CharClass.Hangul => "ko",
			WordPick.CharClass.Kana => "ja",
			WordPick.CharClass.Han => "zh",
			WordPick.CharClass.Latin => "en",
			_ => "",
		};
	}

	static StudyLang langof(string dict) => dict switch {
		"zh" => StudyLang.Zh,
		"ja" => StudyLang.Ja,
		"ko" => StudyLang.Ko,
		_ => StudyLang.En,
	};

	static string guesslang(string w) {
		if (string.IsNullOrEmpty(w)) return "en";
		return WordPick.ClassOf(w[0]) switch {
			WordPick.CharClass.Hangul => "ko",
			WordPick.CharClass.Kana => "ja",
			WordPick.CharClass.Han => "zh",
			_ => "en",
		};
	}

	static string langname(StudyLang l) => l switch {
		StudyLang.Zh => "中",
		StudyLang.Ja => "日",
		StudyLang.Ko => "韩",
		StudyLang.En => "英",
		_ => "全部",
	};

	void langclick(object sender, RoutedEventArgs e) {
		if (sender is not ToggleButton tb || tb.Tag is not string tag) return;
		state.Lang = tag switch {
			"Zh" => StudyLang.Zh,
			"Ja" => StudyLang.Ja,
			"Ko" => StudyLang.Ko,
			"En" => StudyLang.En,
			_ => StudyLang.All,
		};
		builtwordsig = "";
		render();
	}

	void wordselected(object sender, SelectionChangedEventArgs e) {
		if (lcwords.SelectedItem is StudyWordRow r) selectword(r.Word);
	}

	void cueselected(object sender, SelectionChangedEventArgs e) {
		// 字幕行选中只作视觉，不联动播放；点行播原声走 cueclipclick
	}

	void subqchanged(object sender, TextChangedEventArgs e) {
		var q = esubq.Text ?? "";
		if (state.SubtitleQuery == q) return;
		state.SubtitleQuery = q;
		builtsubq = null;
		render();
	}
	#endregion

	// ==================== 发音 ====================
	#region
	void wordclipclick(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement fe && fe.Tag is string w) playwordclip(w);
	}

	void wordttsclick(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement fe && fe.Tag is string w) playwordtts(w);
	}

	void cueclipclick(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement fe && fe.Tag is int i) playcue(i);
	}

	void cuejumpclick(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement fe && fe.Tag is int i) jumpcue(i);
	}

	void playwordclip(string word) {
		var r = allwords.FirstOrDefault(x => x.Word == word);
		var cur = list.Current;
		if (r == null || r.Manual) { hud("手动查的词没有对应片段，可用 TTS"); return; }
		if (cur == null) { hud("还没有播放文件"); return; }
		stopclip();
		state.ClipKey = "w:" + word;
		clip.Volume = (float)(state.Muted ? 0 : state.Volume);
		clip.Play(cur.Path, r.Start, r.End);
		hud($"原声：{word}  {fmt(r.Start)}–{fmt(r.End)}" + (r.Exact ? "" : " · 按均匀推算"));
		render();
	}

	void playwordtts(string word) {
		var r = allwords.FirstOrDefault(x => x.Word == word);
		var code = guesslang(word);
		var name = r != null ? langname(r.Lang) : "自动";
		stopclip();
		state.ClipKey = "tts:" + word;
		hud($"TTS 合成：{word}（{name}）");
		render();
		Task.Run(() => {
			try {
				var path = DictWeb.SpeakFile(opt, code, word);
				if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
					Dispatcher.BeginInvoke(new Action(() => hud("TTS 没有生成声音")));
					return;
				}
				Dispatcher.BeginInvoke(new Action(() => {
					try {
						ttsplayer?.Stop();
						ttsplayer = new SoundPlayer(path);
						ttsplayer.Play();
					}
					catch (Exception ex) { hud("TTS 播放失败: " + ex.Message); }
				}));
				Thread.Sleep(1200);
			}
			catch (Exception ex) {
				Dispatcher.BeginInvoke(new Action(() => hud("TTS 失败: " + ex.Message)));
			}
			finally {
				Dispatcher.BeginInvoke(new Action(() => {
					if (state.ClipKey.StartsWith("tts:", StringComparison.Ordinal)) state.ClipKey = "";
					render();
				}));
			}
		});
	}

	void playcue(int index) {
		var c = allcues.FirstOrDefault(x => x.Index == index);
		var cur = list.Current;
		if (c == null) return;
		if (cur == null) { hud("还没有播放文件"); return; }
		stopclip();
		state.ClipKey = "cue:" + index;
		clip.Volume = (float)(state.Muted ? 0 : state.Volume);
		clip.Play(cur.Path, c.Start, c.End);
		hud($"原声：第 {index + 1} 条  {fmt(c.Start)}–{fmt(c.End)}");
		render();
	}

	void jumpcue(int index) {
		var c = allcues.FirstOrDefault(x => x.Index == index);
		if (c == null || list.Current == null) return;
		session.Seek(c.Start);
		if (state.Paused) {
			session.Play();
			state.Paused = false;
		}
		lastpos = -1;
		stopclip();
		hud("跳到第 " + (index + 1) + " 条 " + fmt(c.Start));
		render();
	}

	void stopclip() {
		clip.Stop();
		state.ClipKey = "";
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
		stopclip();
	}

	void seekchanged(object sender, RoutedEventArgs e) {
		if (!seeking) return;
		var dur = session.DurationSec;
		if (dur <= 0) return;
		lbtime.Text = $"{fmt(sseek.Value * dur)} / {fmt(dur)}";
	}
	#endregion

	// ==================== 窗口形态 ====================
	#region
	public void SetForm(PlayerForm f) => applyform(f);

	void applyform(PlayerForm f) {
		var full = f == PlayerForm.Fullscreen;
		state.Mode = f;
		if (full) {
			if (!fullscreen && WindowState == WindowState.Normal) {
				savedw = Width;
				savedh = Height;
				savedl = Left;
				savedt = Top;
				hassavedrect = true;
			}
			prevstate = WindowState;
			fullscreen = true;
			WindowStyle = WindowStyle.None;
			ResizeMode = ResizeMode.NoResize;
			WindowState = WindowState.Normal;
			var (l, t, w, h) = ScreenDpi.VirtualScreenPixels();
			ScreenDpi.PinWindowToPhysical(this, l, t, w, h);
			Topmost = true;
			state.SideOpenBeforeFull = state.SideOpen;
			state.SideOpen = false;
			state.LastActivityTick = Environment.TickCount;
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
			Topmost = state.Topmost;
			state.SideOpen = state.SideOpenBeforeFull;
			state.LastActivityTick = Environment.TickCount;
		}
		layer.relayout();
		render();
		Dispatcher.BeginInvoke(new Action(() => {
			layer.relayout();
			if (session.HasVideo) drawframe();
		}), DispatcherPriority.Loaded);
	}

	void togglefull() => applyform(state.Mode == PlayerForm.Fullscreen ? PlayerForm.Normal : PlayerForm.Fullscreen);

	void toggleside() {
		if (state.Mode == PlayerForm.Fullscreen) { hud("全屏下侧栏不可用，先退出全屏 (Esc / F)"); return; }
		state.SideOpen = !state.SideOpen;
		render();
	}

	void togglepin() {
		state.Topmost = !state.Topmost;
		Topmost = state.Topmost;
		hud(state.Topmost ? "已置顶" : "取消置顶");
		render();
	}
	#endregion

	// ==================== 鼠标与键盘 ====================
	#region
	void stagedown(object sender, MouseButtonEventArgs e) {
		var now = Environment.TickCount;
		if (now - lastclick < 400) clickcount++;
		else clickcount = 1;
		lastclick = now;
		dragfrom = e.GetPosition(this);
		// 在字幕文字上按下：这次拖动改字幕上下位置，不动窗口
		subfrom = e.GetPosition(ptext);
		if (layer.HitTest(subfrom, out _, out _)) {
			dragsub = true;
			dragging = false;
			subbottom0 = layer.BottomMargin;
			return;
		}
		dragsub = false;
		dragging = true;
		dragl = Left;
		dragt = Top;
		e.Handled = false;
	}

	void stagemove(object sender, MouseEventArgs e) {
		if (e.LeftButton != MouseButtonState.Pressed) { dragging = false; dragsub = false; return; }
		if (dragsub) {
			var dy = e.GetPosition(ptext).Y - subfrom.Y;
			if (Math.Abs(dy) < 3) return;
			setsubbottom(subbottom0 - dy / layer.Scale);
			return;
		}
		if (!dragging) return;
		if (fullscreen) return;
		var p = e.GetPosition(this);
		var dx = p.X - dragfrom.X;
		var dy2 = p.Y - dragfrom.Y;
		if (Math.Abs(dx) < 4 && Math.Abs(dy2) < 4) return;
		try {
			Left = dragl + dx;
			Top = dragt + dy2;
		}
		catch { }
	}

	/// <summary>改字幕底部留白并重排位置，只在纵向移动，水平仍居中。</summary>
	void setsubbottom(double v) {
		if (double.IsNaN(v)) return;
		var h = layer.ViewHeight;
		var max = Math.Min(400, Math.Max(0, h * 0.75));
		if (v < 0) v = 0;
		if (v > max) v = max;
		layer.BottomMargin = v;
		layer.reposition();
		subnudge = true;
	}

	void stageup(object sender, MouseButtonEventArgs e) {
		var moved = false;
		var p = e.GetPosition(this);
		if (Math.Abs(p.X - dragfrom.X) >= 4 || Math.Abs(p.Y - dragfrom.Y) >= 4) moved = true;
		if (dragsub) {
			dragsub = false;
			if (subnudge) {
				subnudge = false;
				if (opt != null) opt.PlaySubBottom = (int)Math.Round(Math.Max(0, layer.BottomMargin));
				hud($"字幕位置 底部留白 {(int)Math.Round(layer.BottomMargin)}");
				clickcount = 0;
				return;
			}
			// 按在字幕上但没拖动：当普通单击处理
		}
		dragging = false;
		if (moved) return;
		var pt = e.GetPosition(ptext);
		if (trypickword(pt)) { clickcount = 0; return; }
		// 画面区单击即播放/暂停。双击会先走两次单击（状态一来一回不变），再切全屏，
		// 所以这里不做延时判定，点击响应是即时的。
		stageclick();
		if (clickcount >= 2) {
			clickcount = 0;
			togglefull();
			return;
		}
		clickcount = 0;
	}

	/// <summary>画面区单击：播放 / 暂停。没打开文件时不响应，避免空白舞台乱弹打开对话框。</summary>
	void stageclick() {
		if (list.Current == null) return;
		toggleplay();
		hud(state.Paused ? "已暂停" : "播放中");
	}

	void stagewheel(object sender, MouseWheelEventArgs e) {
		if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
			var off = (sub?.OffsetSec ?? 0) + (e.Delta > 0 ? 0.05 : -0.05);
			if (sub != null) sub.OffsetSec = off;
			if (lyric != null) lyric.OffsetSec = off;
			hud($"字幕延迟 {off * 1000:+0;-0;0} ms");
			return;
		}
		setvol(state.Volume + (e.Delta > 0 ? 0.05 : -0.05));
	}

	void onkey(object sender, KeyEventArgs e) {
		if (edictq.IsKeyboardFocusWithin || esubq.IsKeyboardFocusWithin) {
			if (e.Key == Key.Enter) { Keyboard.ClearFocus(); e.Handled = true; }
			return;
		}
		switch (e.Key) {
			case Key.Space: toggleplay(); e.Handled = true; break;
			case Key.Left: seekrel(-5); e.Handled = true; break;
			case Key.Right: seekrel(5); e.Handled = true; break;
			case Key.Up: setvol(state.Volume + 0.05); e.Handled = true; break;
			case Key.Down: setvol(state.Volume - 0.05); e.Handled = true; break;
			case Key.Escape:
				if (fullscreen) applyform(PlayerForm.Normal);
				else Close();
				e.Handled = true;
				break;
			case Key.F: togglefull(); e.Handled = true; break;
			case Key.S: togglesub(); e.Handled = true; break;
			case Key.H: toggleside(); e.Handled = true; break;
			case Key.N: playnext(true); e.Handled = true; break;
			case Key.P: playprev(); e.Handled = true; break;
			case Key.M: togglemute(); e.Handled = true; break;
			case Key.D1: settab(SideTab.List); e.Handled = true; break;
			case Key.D2: settab(SideTab.Study); e.Handled = true; break;
			case Key.D3: settab(SideTab.Subtitle); e.Handled = true; break;
		}
	}

	void settab(SideTab t) {
		state.Tab = t;
		render();
	}

	void seekrel(double delta) {
		var dur = session.DurationSec;
		if (dur <= 0) return;
		var target = session.PositionSec + delta;
		if (target < 0) target = 0;
		if (target > dur) target = dur;
		session.Seek(target);
		lastpos = -1;
		stopclip();
		hud(fmt(target));
	}

	void togglemute() {
		if (!state.Muted) {
			paused_at = state.Volume > 0 ? state.Volume : 0.8;
			state.Muted = true;
			state.Volume = 0;
		}
		else {
			state.Muted = false;
			state.Volume = paused_at > 0 ? paused_at : 0.8;
		}
		session.SetVolume((float)state.Volume);
		clip.Volume = (float)state.Volume;
		hud(state.Muted ? "已静音" : "取消静音");
		render();
	}

	void setvol(double v) {
		v = v < 0 ? 0 : v > 1 ? 1 : v;
		state.Volume = v;
		state.Muted = v <= 0.0001;
		session.SetVolume((float)v);
		clip.Volume = (float)v;
		hud($"音量 {(int)Math.Round(v * 100)}%");
		render();
	}
	#endregion

	// ==================== 杂项 ====================
	#region
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
	#endregion

	// ==================== 自测 ====================
	#region
	/// <summary>自测用：模拟点击字幕第 lineIdx 行第 charIdx 个字，返回取到的词和释义。</summary>
	public string ClickChar(int lineIdx, int charIdx) {
		if (layer.Metrics.Count == 0) return "无字幕";
		if (lineIdx < 0 || lineIdx >= layer.Metrics.Count) lineIdx = 0;
		var l = layer.Metrics[lineIdx];
		if (l.Bounds == null || charIdx < 0 || charIdx + 1 >= l.Bounds.Length) return "字符越界";
		var line = layer.TextOf(lineIdx);
		if (string.IsNullOrEmpty(line) || charIdx >= line.Length) return "空行";
		var x = (l.Bounds[charIdx] + l.Bounds[charIdx + 1]) / 2;
		var y = l.Height / 2;
		layer.LineOrigin(lineIdx, out var left, out var top);
		var pt = new Point(left + x, top + y);
		var before = state.StudyQuery;
		trypickword(pt);
		var got = state.StudyQuery;
		var row = allwords.FirstOrDefault(r => r.Word == got);
		var outText = row != null ? row.Title + " " + row.Gloss : "（本句未列出：" + got + "）";
		if (outText.Length > 70) outText = outText.Substring(0, 70) + "…";
		return $"行{lineIdx}字{charIdx}='{line[charIdx]}' 之前='{before}' 取词='{got}' 释义='{outText}'";
	}

	/// <summary>自测用：跳到指定秒并刷新字幕与生词表。</summary>
	public void SeekTo(double sec) {
		if (sec < 0) return;
		session.Seek(sec);
		lastpos = -1;
		updatetext(session.PositionSec);
		updateclock(session.PositionSec);
		render();
	}

	/// <summary>自测用：切右栏页签。</summary>
	internal void SetTab(SideTab t) {
		state.Tab = t;
		render();
	}

	/// <summary>自测用：模拟在画面区点一下，回传点击前后的播放状态。</summary>
	public string ClickStage() {
		var before = state.Paused;
		stageclick();
		render();
		return $"paused {before}->{state.Paused} hasfile={list.Current != null} icon={bplay.Content}";
	}

	/// <summary>自测用：把字幕整体上下移动 dy 像素（正数向上），返回移动后的底部留白。</summary>
	public string NudgeSubtitle(double dy) {
		subbottom0 = layer.BottomMargin;
		var y0 = layer.BottomMargin;
		layer.BottomMargin = Math.Max(0, y0 + dy);
		layer.reposition();
		return $"bottom {y0:0.#} -> {layer.BottomMargin:0.#} scale={layer.Scale:0.##} viewh={layer.ViewHeight:0.#}";
	}

	/// <summary>自测用：把整个窗口渲染成 PNG，并回传界面上关键状态。</summary>
	public string ShotTo(string path) {
		var scale = ScreenDpi.SystemScale();
		if (scale <= 0) scale = 1;
		var w = (int)Math.Round(ActualWidth * scale);
		var h = (int)Math.Round(ActualHeight * scale);
		var info = $"form={state.Mode} size={ActualWidth:0}x{ActualHeight:0} dpi={scale:0.##} " +
			$"rowHead={rowHead.ActualHeight:0} side={(state.SideOpen ? "on" : "off")} tabs={(tabside.SelectedItem as TabItem)?.Header} " +
			$"pbar={pbar.Visibility} phead={phead.Visibility} words={words.Count}/{allwords.Count} cues={cues.Count}/{allcues.Count} " +
			$"cue={(layer.CueText is { Length: > 0 } s ? s : "(空)")} lines={layer.LineCount} " +
			$"sub={state.SubtitleOn} pos={session.PositionSec:0.###} hasvideo={session.HasVideo} " +
			$"subn={sub?.Cues.Count ?? 0} lyricn={lyric?.Cues.Count ?? 0} clip={state.ClipKey} lang={state.Lang} " +
			$"query={state.StudyQuery} cur={allcues.FirstOrDefault(c => c.IsCurrent)?.No ?? "-"} " +
			$"subbottom={layer.BottomMargin:0.#}";
		try {
			UpdateLayout();
			layer.relayout();
			UpdateLayout();
			var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
			rtb.Render(proot);
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(rtb));
			using var fs = File.Create(path);
			enc.Save(fs);
			info += $" saved={path}";
		}
		catch (Exception ex) {
			info += " FAIL " + ex.Message;
		}
		return info;
	}
	#endregion
}

