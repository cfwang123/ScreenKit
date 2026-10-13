using NAudio.Midi;

namespace ScreenKit;

/// <summary>MIDI 输出播放。用系统已有的合成器，不另引用库。</summary>
sealed class MidiSynth : IDisposable {
	const string GM_EN =
		"Acoustic Grand Piano|Bright Acoustic Piano|Electric Grand Piano|Honky-tonk Piano|Electric Piano 1|Electric Piano 2|Harpsichord|Clavinet|" +
		"Celesta|Glockenspiel|Music Box|Vibraphone|Marimba|Xylophone|Tubular Bells|Dulcimer|" +
		"Drawbar Organ|Percussive Organ|Rock Organ|Church Organ|Reed Organ|Accordion|Harmonica|Tango Accordion|" +
		"Acoustic Guitar (nylon)|Acoustic Guitar (steel)|Electric Guitar (jazz)|Electric Guitar (clean)|Electric Guitar (muted)|Overdriven Guitar|Distortion Guitar|Guitar Harmonics|" +
		"Acoustic Bass|Electric Bass (finger)|Electric Bass (pick)|Fretless Bass|Slap Bass 1|Slap Bass 2|Synth Bass 1|Synth Bass 2|" +
		"Violin|Viola|Cello|Contrabass|Tremolo Strings|Pizzicato Strings|Orchestral Harp|Timpani|" +
		"String Ensemble 1|String Ensemble 2|Synth Strings 1|Synth Strings 2|Choir Aahs|Voice Oohs|Synth Voice|Orchestra Hit|" +
		"Trumpet|Trombone|Tuba|Muted Trumpet|French Horn|Brass Section|Synth Brass 1|Synth Brass 2|" +
		"Soprano Sax|Alto Sax|Tenor Sax|Baritone Sax|Oboe|English Horn|Bassoon|Clarinet|" +
		"Piccolo|Flute|Recorder|Pan Flute|Blown Bottle|Shakuhachi|Whistle|Ocarina|" +
		"Lead 1 (square)|Lead 2 (sawtooth)|Lead 3 (calliope)|Lead 4 (chiff)|Lead 5 (charang)|Lead 6 (voice)|Lead 7 (fifths)|Lead 8 (bass + lead)|" +
		"Pad 1 (new age)|Pad 2 (warm)|Pad 3 (polysynth)|Pad 4 (choir)|Pad 5 (bowed)|Pad 6 (metallic)|Pad 7 (halo)|Pad 8 (sweep)|" +
		"FX 1 (rain)|FX 2 (soundtrack)|FX 3 (crystal)|FX 4 (atmosphere)|FX 5 (brightness)|FX 6 (goblins)|FX 7 (echoes)|FX 8 (sci-fi)|" +
		"Sitar|Banjo|Shamisen|Koto|Kalimba|Bag pipe|Fiddle|Shanai|" +
		"Tinkle Bell|Agogo|Steel Drums|Woodblock|Taiko Drum|Melodic Tom|Synth Drum|Reverse Cymbal|" +
		"Guitar Fret Noise|Breath Noise|Seashore|Bird Tweet|Telephone Ring|Helicopter|Applause|Gunshot";

	const string GM_ZH =
		"大钢琴|亮音钢琴|电钢琴|酒吧钢琴|电钢琴 1|电钢琴 2|羽管键琴|古钢琴|" +
		"钢片琴|钟琴|八音盒|颤音琴|马林巴|木琴|管钟|洋琴|" +
		"击杆风琴|打击风琴|摇滚风琴|教堂风琴|簧风琴|手风琴|口琴|探戈手风琴|" +
		"尼龙吉他|钢弦吉他|爵士电吉他|清音电吉他|闷音电吉他|过载吉他|失真吉他|吉他泛音|" +
		"原声贝斯|指弹电贝斯|拨片电贝斯|无品贝斯|击弦贝斯 1|击弦贝斯 2|合成贝斯 1|合成贝斯 2|" +
		"小提琴|中提琴|大提琴|低音提琴|颤弓弦乐|拨弦|竖琴|定音鼓|" +
		"弦乐合奏 1|弦乐合奏 2|合成弦乐 1|合成弦乐 2|合唱啊|人声哦|合成人声|乐队打击|" +
		"小号|长号|大号|弱音小号|圆号|铜管组|合成铜管 1|合成铜管 2|" +
		"高音萨克斯|中音萨克斯|次中音萨克斯|上低音萨克斯|双簧管|英国管|巴松|单簧管|" +
		"短笛|长笛|竖笛|排箫|吹瓶|尺八|口哨|奥卡利那|" +
		"方波|锯齿波|汽笛风琴|吹管|吉他合成|人声主音|五度|贝斯加主音|" +
		"新世纪|暖音|复音合成|合唱垫|弓弦垫|金属垫|光环|扫弦|" +
		"雨|音轨|水晶|大气|明亮|妖精|回声|科幻|" +
		"西塔琴|班卓|三味线|古筝|卡林巴|风笛|提琴|唢呐|" +
		"铃铛|阿哥哥|钢鼓|木鱼|太鼓|旋律鼓|合成鼓|反向钹|" +
		"品丝噪音|呼吸声|海浪|鸟鸣|电话铃|直升机|掌声|枪声";

	static readonly string[] GMEN = GM_EN.Split('|');
	static readonly string[] GMZH = GM_ZH.Split('|');
	static readonly string[] NOTE = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

	readonly object gate = new object();
	MidiOut midi;
	MidiIn input;
	int channel = 1;
	int program;
	int volume = 100;

	public event Action<int, int, bool> InputNote;

	public static int OutputCount => MidiOut.NumberOfDevices;
	public static int InputCount => MidiIn.NumberOfDevices;

	public static string OutputName(int index) {
		if (index < 0 || index >= MidiOut.NumberOfDevices) return "";
		return MidiOut.DeviceInfo(index).ProductName ?? "";
	}

	public static string InputName(int index) {
		if (index < 0 || index >= MidiIn.NumberOfDevices) return "";
		return MidiIn.DeviceInfo(index).ProductName ?? "";
	}

	public static string InstrumentName(int program) {
		if (program < 0 || program >= GMEN.Length) return "";
		var zh = Loc.IsZh && program < GMZH.Length ? GMZH[program] : GMEN[program];
		return (program + 1).ToString("000") + " " + zh;
	}

	public static int InstrumentCount => GMEN.Length;

	public static string NoteName(int note) {
		if (note < 0 || note > 127) return "";
		return NOTE[note % 12] + (note / 12 - 1);
	}

	/// <summary>电脑键盘相对当前八度 C 的半音。不是琴键返回 -1。</summary>
	public static int KeyOffset(Key key) => key switch {
		Key.Z => 0, Key.S => 1, Key.X => 2, Key.D => 3, Key.C => 4, Key.V => 5,
		Key.G => 6, Key.B => 7, Key.H => 8, Key.N => 9, Key.J => 10, Key.M => 11,
		Key.Q => 12, Key.D2 => 13, Key.W => 14, Key.D3 => 15, Key.E => 16, Key.R => 17,
		Key.D5 => 18, Key.T => 19, Key.D6 => 20, Key.Y => 21, Key.D7 => 22, Key.U => 23,
		_ => -1,
	};

	public void OpenOutput(int index) {
		lock (gate) {
			closeout();
			if (index < 0) return;
			midi = new MidiOut(index);
			send(MidiMessage.ChangePatch(program, channel).RawData);
			applyvol();
		}
	}

	public void OpenInput(int index) {
		lock (gate) {
			closein();
			if (index < 0) return;
			input = new MidiIn(index);
			input.MessageReceived += onmidi;
			input.Start();
		}
	}

	public void SetChannel(int ch) {
		if (ch < 1) ch = 1;
		if (ch > 16) ch = 16;
		lock (gate) {
			if (ch == channel) return;
			alloff();
			channel = ch;
			if (midi != null) send(MidiMessage.ChangePatch(program, channel).RawData);
		}
	}

	public void SetProgram(int prog) {
		if (prog < 0) prog = 0;
		if (prog > 127) prog = 127;
		lock (gate) {
			program = prog;
			if (midi != null) send(MidiMessage.ChangePatch(program, channel).RawData);
		}
	}

	public void SetVolume(int vol) {
		if (vol < 0) vol = 0;
		if (vol > 127) vol = 127;
		lock (gate) {
			volume = vol;
			applyvol();
		}
	}

	public void NoteOn(int note, int velocity) {
		if (note < 0 || note > 127) return;
		if (velocity < 1) velocity = 1;
		if (velocity > 127) velocity = 127;
		lock (gate) {
			if (midi == null) return;
			send(MidiMessage.StartNote(note, velocity, channel).RawData);
		}
	}

	public void NoteOff(int note) {
		if (note < 0 || note > 127) return;
		lock (gate) {
			if (midi == null) return;
			send(MidiMessage.StopNote(note, 0, channel).RawData);
		}
	}

	public void Send(int raw) {
		lock (gate) {
			if (midi == null || raw == 0) return;
			send(raw);
		}
	}

	public void AllOff() {
		lock (gate) alloff();
	}

	public void Dispose() {
		lock (gate) {
			alloff();
			closein();
			closeout();
		}
	}

	public static int SelfTest(Action<string> log) {
		void say(string s) { try { log?.Invoke(s); } catch { } }
		MidiSynth synth = null;
		try {
			if (GMEN.Length != 128 || GMZH.Length != 128) return bad(say, "gm " + GMEN.Length + " " + GMZH.Length);
			if (NoteName(60) != "C4" || NoteName(61) != "C#4") return bad(say, "name");
			if (KeyOffset(Key.Z) != 0 || KeyOffset(Key.S) != 1 || KeyOffset(Key.Q) != 12 || KeyOffset(Key.A) != -1)
				return bad(say, "key");
			if (OutputCount > 0) {
				synth = new MidiSynth();
				synth.OpenOutput(0);
				synth.NoteOn(60, 1);
				synth.NoteOff(60);
				synth.Dispose();
				synth = null;
			}
			if (MidiDoc.SelfTest(say) != 0) return 1;
			say("=== OK：MIDI 合成器");
			return 0;
		}
		catch (Exception ex) {
			return bad(say, ex.Message);
		}
		finally {
			try { synth?.Dispose(); } catch { }
		}
	}

	void applyvol() {
		if (midi == null) return;
		int v = volume * 0xFFFF / 127;
		midi.Volume = v | (v << 16);
		send(MidiMessage.ChangeControl(7, volume, channel).RawData);
	}

	void alloff() {
		if (midi == null) return;
		for (int n = 0; n < 128; n++) send(MidiMessage.StopNote(n, 0, channel).RawData);
		send(MidiMessage.ChangeControl(123, 0, channel).RawData);
	}

	void send(int raw) {
		try { midi.Send(raw); }
		catch { }
	}

	void closeout() {
		if (midi == null) return;
		try { alloff(); } catch { }
		try { midi.Dispose(); } catch { }
		midi = null;
	}

	void closein() {
		if (input == null) return;
		try { input.Stop(); } catch { }
		try { input.MessageReceived -= onmidi; } catch { }
		try { input.Dispose(); } catch { }
		input = null;
	}

	void onmidi(object sender, MidiInMessageEventArgs e) {
		int raw = e.RawMessage;
		int cmd = raw & 0xF0;
		int note = (raw >> 8) & 0x7F;
		int vel = (raw >> 16) & 0x7F;
		if (cmd == 0x90 && vel > 0) InputNote?.Invoke(note, vel, true);
		else if (cmd == 0x80 || (cmd == 0x90 && vel == 0)) InputNote?.Invoke(note, 0, false);
	}

	static int bad(Action<string> say, string msg) {
		say("FAIL " + msg);
		return 1;
	}
}
