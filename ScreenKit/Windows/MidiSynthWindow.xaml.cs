using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → MIDI 合成器。弹琴，并打开、修改、保存 MIDI 文件。</summary>
public partial class MidiSynthWindow : Window {
	const int SRC_KEY = 1;
	const int SRC_MOUSE = 2;
	const int SRC_IN = 4;

	readonly MidiSynth synth = new();
	readonly MidiWave wave = new();
	readonly float[] samples = new float[2048];
	readonly int[] held = new int[128];
	readonly Dictionary<int, Button> keys = new();
	WriteableBitmap wavebmp;
	byte[] wavepix;
	MidiDoc doc = new();
	string path = "";
	bool dirty;
	bool filling;
	int octave = 4;
	DispatcherTimer timer;
	Stopwatch clock;
	List<MidiDoc.PlayEv> play;
	int playAt;

	public MidiSynthWindow() {
		InitializeComponent();
		doc.AddTrack();
		doc.Tempo = 120;
		applylang();
		filldevices();
		fillprog();
		fillch();
		buildkeys();
		showtracks();
		synth.InputNote += oninput;
		eout.SelectionChanged += (_, _) => { if (!filling) openout(); };
		ein.SelectionChanged += (_, _) => { if (!filling) openin(); };
		eprog.SelectionChanged += (_, _) => { if (!filling) synth.SetProgram(eprog.SelectedIndex); };
		ech.SelectionChanged += (_, _) => { if (!filling) synth.SetChannel(ech.SelectedIndex + 1); };
		evol.ValueChanged += (_, _) => {
			int v = (int)evol.Value;
			lbvolv.Text = v.ToString();
			synth.SetVolume(v);
		};
		bstop.Click += (_, _) => releaseall();
		boctdn.Click += (_, _) => setoctave(octave - 1);
		boctup.Click += (_, _) => setoctave(octave + 1);
		bopen.Click += (_, _) => openfile();
		bsave.Click += (_, _) => save(false);
		bsaveas.Click += (_, _) => save(true);
		bplay.Click += (_, _) => playfile();
		bstopfile.Click += (_, _) => stopfile();
		etempo.LostFocus += (_, _) => { applytempo(); };
		etrack.SelectionChanged += (_, _) => { if (!filling) shownotes(); };
		baddtr.Click += (_, _) => { doc.AddTrack(); mark(); showtracks(); };
		bdeltr.Click += (_, _) => deltrack();
		baddn.Click += (_, _) => addnote();
		bdeln.Click += (_, _) => delnote();
		bapply.Click += (_, _) => applynote();
		lv.SelectionChanged += (_, _) => loadnote();
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
		timer.Tick += (_, _) => ontick();
		Closing += onclose;
		WindowEsc.Attach(this);
		synth.SetVolume(100);
		openout();
		wave.Start();
		timer.Start();
	}

	sealed class Row {
		public string Start { get; set; }
		public string Length { get; set; }
		public int Note { get; set; }
		public string Name { get; set; }
		public int Velocity { get; set; }
		public int Channel { get; set; }
	}

	void applylang() {
		Title = title();
		lbhint.Text = Loc.T("midi.hint");
		lbout.Text = Loc.T("midi.out");
		lbin.Text = Loc.T("midi.in");
		lbprog.Text = Loc.T("midi.prog");
		lbch.Text = Loc.T("midi.ch");
		lbvol.Text = Loc.T("midi.vol");
		lboct.Text = Loc.T("midi.oct");
		lbtempo.Text = Loc.T("midi.tempo");
		lbtrack.Text = Loc.T("midi.track");
		lbedstart.Text = Loc.T("midi.start");
		lbedlen.Text = Loc.T("midi.len");
		lbednote.Text = Loc.T("midi.pitch");
		lbedvel.Text = Loc.T("midi.vel");
		lbedch.Text = Loc.T("midi.ch");
		colstart.Header = Loc.T("midi.start");
		collen.Header = Loc.T("midi.len");
		colnote.Header = Loc.T("midi.pitch");
		colname.Header = Loc.T("midi.notename");
		colvel.Header = Loc.T("midi.vel");
		colch.Header = Loc.T("midi.ch");
		ToolBtnUi.Set(bstop, ToolBtnUi.Cancel, Loc.T("midi.stop"));
		ToolBtnUi.Set(boctdn, ToolBtnUi.Down, "");
		ToolBtnUi.Set(boctup, ToolBtnUi.Up, "");
		ToolBtnUi.Set(bopen, ToolBtnUi.Folder, Loc.T("midi.open"));
		ToolBtnUi.Set(bsave, ToolBtnUi.Save, Loc.T("midi.save"));
		ToolBtnUi.Set(bsaveas, ToolBtnUi.Save, Loc.T("midi.saveas"));
		ToolBtnUi.Set(bplay, ToolBtnUi.Play, Loc.T("midi.play"));
		ToolBtnUi.Set(bstopfile, ToolBtnUi.Cancel, Loc.T("midi.stopfile"));
		ToolBtnUi.Set(baddtr, ToolBtnUi.Add, Loc.T("midi.addtrack"));
		ToolBtnUi.Set(bdeltr, ToolBtnUi.Delete, Loc.T("midi.deltrack"));
		ToolBtnUi.Set(baddn, ToolBtnUi.Add, Loc.T("midi.addnote"));
		ToolBtnUi.Set(bdeln, ToolBtnUi.Delete, Loc.T("midi.delnote"));
		ToolBtnUi.Set(bapply, ToolBtnUi.Save, Loc.T("midi.apply"));
		lboctv.Text = "C" + octave;
	}

	string title() {
		var name = string.IsNullOrEmpty(path) ? Loc.T("midi.title") : System.IO.Path.GetFileName(path);
		return name + (dirty ? " *" : "");
	}

	void mark() {
		dirty = true;
		Title = title();
	}

	void filldevices() {
		filling = true;
		eout.Items.Clear();
		int pick = 0;
		for (int i = 0; i < MidiSynth.OutputCount; i++) {
			var name = MidiSynth.OutputName(i);
			eout.Items.Add(new ComboBoxItem { Content = name, Tag = i });
			if (name.IndexOf("GS", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("Wavetable", StringComparison.OrdinalIgnoreCase) >= 0)
				pick = i;
		}
		if (eout.Items.Count > 0) eout.SelectedIndex = pick;
		ein.Items.Clear();
		ein.Items.Add(new ComboBoxItem { Content = Loc.T("midi.none"), Tag = -1 });
		for (int i = 0; i < MidiSynth.InputCount; i++)
			ein.Items.Add(new ComboBoxItem { Content = MidiSynth.InputName(i), Tag = i });
		ein.SelectedIndex = 0;
		filling = false;
	}

	void fillprog() {
		filling = true;
		eprog.Items.Clear();
		for (int i = 0; i < MidiSynth.InstrumentCount; i++)
			eprog.Items.Add(new ComboBoxItem { Content = MidiSynth.InstrumentName(i), Tag = i });
		eprog.SelectedIndex = 0;
		filling = false;
	}

	void fillch() {
		filling = true;
		ech.Items.Clear();
		for (int i = 1; i <= 16; i++)
			ech.Items.Add(new ComboBoxItem { Content = i.ToString(), Tag = i });
		ech.SelectedIndex = 0;
		filling = false;
	}

	void openout() {
		int index = eout.SelectedIndex;
		try {
			synth.OpenOutput(index);
			lbstat.Text = index >= 0 ? MidiSynth.OutputName(index) : Loc.T("midi.nodev");
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
		}
	}

	void openin() {
		int index = -1;
		if (ein.SelectedItem is ComboBoxItem item && item.Tag is int n) index = n;
		try { synth.OpenInput(index); }
		catch (Exception ex) { lbstat.Text = ex.Message; }
	}

	void buildkeys() {
		ppiano.Children.Clear();
		keys.Clear();
		int white = 0;
		for (int i = 0; i < 24; i++) {
			int pc = i % 12;
			bool black = pc is 1 or 3 or 6 or 8 or 10;
			var b = new Button {
				Tag = i,
				Focusable = false,
				FontSize = 11,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Bottom,
				Padding = new Thickness(0, 0, 0, 6),
			};
			int off = i;
			b.PreviewMouseLeftButtonDown += (_, e) => { b.CaptureMouse(); down(basenote() + off, SRC_MOUSE, 100); e.Handled = true; };
			b.PreviewMouseLeftButtonUp += (_, e) => { up(basenote() + off, SRC_MOUSE); b.ReleaseMouseCapture(); e.Handled = true; };
			b.LostMouseCapture += (_, _) => up(basenote() + off, SRC_MOUSE);
			if (!black) {
				Canvas.SetLeft(b, white * 36);
				b.Width = 36;
				b.Height = 150;
				b.Content = (pc == 0) ? "C" + (octave + i / 12) : "";
				Panel.SetZIndex(b, 0);
				white++;
			}
			else {
				Canvas.SetLeft(b, white * 36 - 13);
				b.Width = 24;
				b.Height = 94;
				b.Background = Brushes.Black;
				b.Foreground = Brushes.White;
				b.BorderBrush = Brushes.Black;
				Panel.SetZIndex(b, 1);
			}
			keys[i] = b;
			ppiano.Children.Add(b);
		}
		ppiano.Width = white * 36;
	}

	int basenote() => (octave + 1) * 12;

	void setoctave(int value) {
		if (value < 1) value = 1;
		if (value > 7) value = 7;
		if (value == octave) return;
		releaseall();
		octave = value;
		lboctv.Text = "C" + octave;
		buildkeys();
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e) {
		if (Keyboard.FocusedElement is TextBox) return;
		int off = MidiSynth.KeyOffset(e.Key);
		if (off < 0) return;
		e.Handled = true;
		if (e.IsRepeat) return;
		down(basenote() + off, SRC_KEY, 100);
	}

	protected override void OnPreviewKeyUp(KeyEventArgs e) {
		int off = MidiSynth.KeyOffset(e.Key);
		if (off < 0) return;
		e.Handled = true;
		up(basenote() + off, SRC_KEY);
	}

	void oninput(int note, int vel, bool on) {
		Dispatcher.BeginInvoke(new Action(() => {
			if (on) down(note, SRC_IN, vel);
			else up(note, SRC_IN);
		}));
	}

	void down(int note, int src, int vel) {
		if (note < 0 || note > 127) return;
		int was = held[note];
		held[note] |= src;
		if (was == 0) synth.NoteOn(note, vel);
		light(note, true);
	}

	void up(int note, int src) {
		if (note < 0 || note > 127) return;
		if ((held[note] & src) == 0) return;
		held[note] &= ~src;
		if (held[note] == 0) {
			synth.NoteOff(note);
			light(note, false);
		}
	}

	void releaseall() {
		for (int i = 0; i < 128; i++) {
			if (held[i] == 0) continue;
			held[i] = 0;
			synth.NoteOff(i);
			light(i, false);
		}
		synth.AllOff();
	}

	void light(int note, bool on) {
		int off = note - basenote();
		if (!keys.TryGetValue(off, out var b)) return;
		int pc = off % 12;
		bool black = pc is 1 or 3 or 6 or 8 or 10;
		if (on) b.Background = new SolidColorBrush(Color.FromRgb(214, 196, 150));
		else if (black) b.Background = Brushes.Black;
		else b.ClearValue(BackgroundProperty);
	}

	void showtracks() {
		filling = true;
		int sel = etrack.SelectedIndex;
		etrack.Items.Clear();
		for (int i = 0; i < doc.Tracks.Count; i++) {
			var name = doc.Tracks[i].Name;
			if (string.IsNullOrEmpty(name)) name = Loc.T("midi.track") + " " + (i + 1);
			etrack.Items.Add(new ComboBoxItem { Content = (i + 1) + "  " + name, Tag = i });
		}
		if (etrack.Items.Count == 0) doc.AddTrack();
		if (sel < 0 || sel >= etrack.Items.Count) sel = 0;
		etrack.SelectedIndex = sel;
		etempo.Text = doc.Tempo.ToString("0.##", CultureInfo.InvariantCulture);
		filling = false;
		shownotes();
		Title = title();
	}

	MidiDoc.MidiTrack current() {
		int i = etrack.SelectedIndex;
		if (i < 0 || i >= doc.Tracks.Count) return null;
		return doc.Tracks[i];
	}

	void shownotes() {
		var track = current();
		int sel = lv.SelectedIndex;
		var view = new List<Row>();
		if (track != null) {
			foreach (var n in track.Notes)
				view.Add(new Row {
					Start = beats(n.Start),
					Length = beats(n.Length),
					Note = n.Note,
					Name = MidiSynth.NoteName(n.Note),
					Velocity = n.Velocity,
					Channel = n.Channel,
				});
		}
		lv.ItemsSource = view;
		if (sel >= 0 && sel < view.Count) lv.SelectedIndex = sel;
		lbstat.Text = Loc.T("midi.notestat", view.Count);
	}

	string beats(long ticks) =>
		(ticks / (double)Math.Max(1, doc.Ticks)).ToString("0.###", CultureInfo.InvariantCulture);

	void loadnote() {
		var track = current();
		int i = lv.SelectedIndex;
		if (track == null || i < 0 || i >= track.Notes.Count) return;
		var n = track.Notes[i];
		estart.Text = beats(n.Start);
		elen.Text = beats(n.Length);
		enote.Text = n.Note.ToString();
		evel.Text = n.Velocity.ToString();
		echnote.Text = n.Channel.ToString();
	}

	void applynote() {
		var track = current();
		int i = lv.SelectedIndex;
		if (track == null || i < 0 || i >= track.Notes.Count) return;
		if (!trybeat(estart.Text, out var start)) { lbstat.Text = Loc.T("midi.bad"); return; }
		if (!trybeat(elen.Text, out var len) || len <= 0) { lbstat.Text = Loc.T("midi.bad"); return; }
		if (!int.TryParse((enote.Text ?? "").Trim(), out var note)) { lbstat.Text = Loc.T("midi.bad"); return; }
		if (!int.TryParse((evel.Text ?? "").Trim(), out var vel)) { lbstat.Text = Loc.T("midi.bad"); return; }
		if (!int.TryParse((echnote.Text ?? "").Trim(), out var ch)) { lbstat.Text = Loc.T("midi.bad"); return; }
		if (note < 0) note = 0;
		if (note > 127) note = 127;
		if (vel < 1) vel = 1;
		if (vel > 127) vel = 127;
		if (ch < 1) ch = 1;
		if (ch > 16) ch = 16;
		var n = track.Notes[i];
		n.Start = (long)Math.Round(start * doc.Ticks);
		n.Length = Math.Max(1, (int)Math.Round(len * doc.Ticks));
		n.Note = note;
		n.Velocity = vel;
		n.Channel = ch;
		mark();
		shownotes();
	}

	static bool trybeat(string text, out double value) =>
		double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0;

	void addnote() {
		var track = current();
		if (track == null) return;
		long start = 0;
		foreach (var n in track.Notes)
			if (n.Start + n.Length > start) start = n.Start + n.Length;
		int ch = ech.SelectedIndex + 1;
		if (ch < 1) ch = 1;
		track.Notes.Add(new MidiDoc.MidiNote {
			Start = start,
			Length = doc.Ticks,
			Note = basenote(),
			Velocity = 100,
			Channel = ch,
		});
		mark();
		shownotes();
		lv.SelectedIndex = track.Notes.Count - 1;
	}

	void delnote() {
		var track = current();
		int i = lv.SelectedIndex;
		if (track == null || i < 0 || i >= track.Notes.Count) return;
		track.Notes.RemoveAt(i);
		mark();
		shownotes();
	}

	void deltrack() {
		if (doc.Tracks.Count <= 1) return;
		int i = etrack.SelectedIndex;
		if (i < 0 || i >= doc.Tracks.Count) return;
		doc.Tracks.RemoveAt(i);
		mark();
		showtracks();
	}

	bool applytempo() {
		if (!double.TryParse((etempo.Text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm)) {
			lbstat.Text = Loc.T("midi.bad");
			return false;
		}
		var before = doc.Tempo;
		doc.Tempo = bpm;
		etempo.Text = doc.Tempo.ToString("0.##", CultureInfo.InvariantCulture);
		if (Math.Abs(doc.Tempo - before) > 0.05) mark();
		return true;
	}

	void openfile() {
		if (!askdirty()) return;
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Filter = "MIDI (*.mid;*.midi)|*.mid;*.midi",
		};
		if (dlg.ShowDialog(this) != true) return;
		try {
			stopfile();
			doc = MidiDoc.Load(dlg.FileName);
			path = dlg.FileName;
			dirty = false;
			showtracks();
			lbstat.Text = path;
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
		}
	}

	bool save(bool saveas) {
		if (!applytempo()) return false;
		var target = path;
		if (saveas || string.IsNullOrEmpty(target)) {
			var dlg = new Microsoft.Win32.SaveFileDialog {
				Filter = "MIDI (*.mid)|*.mid",
				FileName = string.IsNullOrEmpty(path) ? "song.mid" : System.IO.Path.GetFileName(path),
			};
			if (dlg.ShowDialog(this) != true) return false;
			target = dlg.FileName;
		}
		try {
			doc.Save(target);
			path = target;
			dirty = false;
			Title = title();
			lbstat.Text = path;
			return true;
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
			return false;
		}
	}

	bool askdirty() {
		if (!dirty) return true;
		var r = MessageBox.Show(this, Loc.T("midi.dirty"), Loc.T("midi.title"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
		if (r == MessageBoxResult.Cancel) return false;
		if (r == MessageBoxResult.No) return true;
		return save(false);
	}

	void playfile() {
		stopfile();
		play = doc.Timeline();
		playAt = 0;
		clock = Stopwatch.StartNew();
		timer.Start();
	}

	void stopfile() {
		play = null;
		try { synth.AllOff(); } catch { }
		for (int i = 0; i < 128; i++) held[i] = 0;
		foreach (var b in keys.Values) {
			int off = (int)b.Tag;
			int pc = off % 12;
			bool black = pc is 1 or 3 or 6 or 8 or 10;
			if (black) b.Background = Brushes.Black;
			else b.ClearValue(BackgroundProperty);
		}
	}

	void ontick() {
		paintwave();
		if (play == null || clock == null) return;
		double now = clock.Elapsed.TotalMilliseconds;
		while (playAt < play.Count && play[playAt].Ms <= now) {
			synth.Send(play[playAt].Raw);
			playAt++;
		}
		if (playAt >= play.Count) stopfile();
	}

	void onclose(object sender, System.ComponentModel.CancelEventArgs e) {
		if (!askdirty()) {
			e.Cancel = true;
			return;
		}
		timer?.Stop();
		stopfile();
		try { wave.Dispose(); } catch { }
		try { synth.Dispose(); } catch { }
	}

	void paintwave() {
		double wdip = pwave.ActualWidth;
		double hdip = pwave.ActualHeight;
		if (wdip < 4 || hdip < 4) return;
		int w = (int)wdip;
		int h = (int)hdip;
		if (wavebmp == null || wavebmp.PixelWidth != w || wavebmp.PixelHeight != h) {
			wavebmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
			ewave.Source = wavebmp;
			wavepix = new byte[wavebmp.BackBufferStride * h];
		}
		wave.Copy(samples);
		int stride = wavebmp.BackBufferStride;
		if (wavepix == null || wavepix.Length < stride * h) wavepix = new byte[stride * h];
		MidiWave.Paint(wavepix, w, h, stride, samples);
		wavebmp.WritePixels(new Int32Rect(0, 0, w, h), wavepix, stride, 0);
	}
}
