using System.Globalization;
using NAudio.Midi;

namespace ScreenKit;

/// <summary>可编辑的 MIDI 文件。音符单独列出，其余事件原样保留。</summary>
sealed class MidiDoc {
	public int Ticks = 480;
	public List<MidiTrack> Tracks = new();

	public sealed class MidiTrack {
		public string Name = "";
		public List<MidiNote> Notes = new();
		public List<MidiEvent> Other = new();
	}

	public sealed class MidiNote {
		public long Start;
		public int Length = 1;
		public int Channel = 1;
		public int Note = 60;
		public int Velocity = 100;
	}

	public sealed class PlayEv {
		public double Ms;
		public int Raw;
	}

	public static MidiDoc Load(string path) {
		var file = new MidiFile(path, false);
		var doc = new MidiDoc { Ticks = Math.Max(1, file.DeltaTicksPerQuarterNote) };
		for (int t = 0; t < file.Tracks; t++) {
			var track = new MidiTrack();
			var open = new Dictionary<int, List<MidiNote>>();
			foreach (var ev in file.Events[t]) {
				if (MidiEvent.IsEndTrack(ev)) continue;
				if (ev is TextEvent text && text.MetaEventType == MetaEventType.SequenceTrackName && track.Name.Length == 0) {
					track.Name = text.Text ?? "";
					track.Other.Add(ev.Clone());
					continue;
				}
				if (MidiEvent.IsNoteOn(ev) && ev is NoteEvent on) {
					var note = new MidiNote {
						Start = on.AbsoluteTime,
						Channel = on.Channel,
						Note = on.NoteNumber,
						Velocity = on.Velocity,
						Length = doc.Ticks,
					};
					track.Notes.Add(note);
					int key = on.Channel * 128 + on.NoteNumber;
					if (!open.TryGetValue(key, out var q)) open[key] = q = new List<MidiNote>();
					q.Add(note);
					continue;
				}
				if (MidiEvent.IsNoteOff(ev) && ev is NoteEvent off) {
					int key = off.Channel * 128 + off.NoteNumber;
					if (open.TryGetValue(key, out var q) && q.Count > 0) {
						var note = q[0];
						q.RemoveAt(0);
						var len = (int)(off.AbsoluteTime - note.Start);
						note.Length = len < 1 ? 1 : len;
					}
					continue;
				}
				track.Other.Add(ev.Clone());
			}
			doc.Tracks.Add(track);
		}
		if (doc.Tracks.Count == 0) doc.AddTrack();
		return doc;
	}

	public void Save(string path) {
		int type = Tracks.Count > 1 ? 1 : 0;
		var col = new MidiEventCollection(type, Ticks);
		foreach (var track in Tracks) {
			var list = col.AddTrack();
			if (!string.IsNullOrEmpty(track.Name))
				list.Add(new TextEvent(track.Name, MetaEventType.SequenceTrackName, 0));
			foreach (var ev in track.Other) {
				if (MidiEvent.IsNoteOn(ev) || MidiEvent.IsNoteOff(ev) || MidiEvent.IsEndTrack(ev)) continue;
				if (ev is TextEvent text && text.MetaEventType == MetaEventType.SequenceTrackName) continue;
				list.Add(ev);
			}
			foreach (var n in track.Notes) {
				int len = n.Length < 1 ? 1 : n.Length;
				int ch = n.Channel < 1 ? 1 : (n.Channel > 16 ? 16 : n.Channel);
				int note = n.Note < 0 ? 0 : (n.Note > 127 ? 127 : n.Note);
				int vel = n.Velocity < 1 ? 1 : (n.Velocity > 127 ? 127 : n.Velocity);
				long start = n.Start < 0 ? 0 : n.Start;
				list.Add(new NoteEvent(start, ch, MidiCommandCode.NoteOn, note, vel));
				list.Add(new NoteEvent(start + len, ch, MidiCommandCode.NoteOff, note, 0));
			}
			if (list.Count == 0) list.Add(new TextEvent(track.Name ?? "", MetaEventType.SequenceTrackName, 0));
		}
		col.PrepareForExport();
		MidiFile.Export(path, col);
	}

	public void AddTrack() {
		Tracks.Add(new MidiTrack { Name = "Track " + (Tracks.Count + 1) });
	}

	public double Tempo {
		get {
			foreach (var track in Tracks)
				foreach (var ev in track.Other)
					if (ev is TempoEvent tempo && tempo.MicrosecondsPerQuarterNote > 0) return tempo.Tempo;
			return 120;
		}
		set {
			var bpm = value;
			if (bpm < 20) bpm = 20;
			if (bpm > 400) bpm = 400;
			if (Tracks.Count == 0) AddTrack();
			var us = (int)Math.Round(60000000.0 / bpm);
			foreach (var track in Tracks) {
				for (int i = track.Other.Count - 1; i >= 0; i--)
					if (track.Other[i] is TempoEvent) track.Other.RemoveAt(i);
			}
			Tracks[0].Other.Add(new TempoEvent(us, 0));
		}
	}

	public List<PlayEv> Timeline() {
		var tempos = new List<TempoEvent>();
		foreach (var track in Tracks)
			foreach (var ev in track.Other)
				if (ev is TempoEvent tempo) tempos.Add(tempo);
		tempos.Sort((a, b) => a.AbsoluteTime.CompareTo(b.AbsoluteTime));
		var list = new List<PlayEv>();
		foreach (var track in Tracks) {
			foreach (var n in track.Notes) {
				int ch = n.Channel < 1 ? 1 : (n.Channel > 16 ? 16 : n.Channel);
				int note = n.Note < 0 ? 0 : (n.Note > 127 ? 127 : n.Note);
				int vel = n.Velocity < 1 ? 1 : (n.Velocity > 127 ? 127 : n.Velocity);
				long start = n.Start < 0 ? 0 : n.Start;
				int len = n.Length < 1 ? 1 : n.Length;
				list.Add(new PlayEv { Ms = ms(start, tempos), Raw = MidiMessage.StartNote(note, vel, ch).RawData });
				list.Add(new PlayEv { Ms = ms(start + len, tempos), Raw = MidiMessage.StopNote(note, 0, ch).RawData });
			}
			foreach (var ev in track.Other) {
				if (ev is TempoEvent || ev is TextEvent || ev.CommandCode == MidiCommandCode.MetaEvent) continue;
				if (ev.CommandCode == MidiCommandCode.Sysex) continue;
				int raw = ev.GetAsShortMessage();
				if (raw == 0) continue;
				list.Add(new PlayEv { Ms = ms(ev.AbsoluteTime, tempos), Raw = raw });
			}
		}
		list.Sort((a, b) => a.Ms.CompareTo(b.Ms));
		return list;
	}

	public static int SelfTest(Action<string> say) {
		var path = Path.Combine(Path.GetTempPath(), "screenkit-midi-test.mid");
		try {
			var doc = new MidiDoc();
			doc.AddTrack();
			doc.Tempo = 140;
			doc.Tracks[0].Notes.Add(new MidiNote { Start = 0, Length = 480, Channel = 1, Note = 64, Velocity = 90 });
			doc.Tracks[0].Other.Add(new PatchChangeEvent(0, 1, 12));
			doc.Save(path);
			var back = Load(path);
			if (back.Tracks.Count != 1 || back.Tracks[0].Notes.Count != 1) return bad(say, "load");
			var n = back.Tracks[0].Notes[0];
			if (n.Note != 64 || n.Length != 480 || n.Velocity != 90 || n.Channel != 1) return bad(say, "note");
			if (Math.Abs(back.Tempo - 140) > 0.2) return bad(say, "tempo " + back.Tempo.ToString(CultureInfo.InvariantCulture));
			bool patch = false;
			foreach (var ev in back.Tracks[0].Other)
				if (ev is PatchChangeEvent pc && pc.Patch == 12) patch = true;
			if (!patch) return bad(say, "patch");
			var line = back.Timeline();
			if (line.Count < 2 || line[0].Ms > line[1].Ms) return bad(say, "time");
			return 0;
		}
		catch (Exception ex) {
			return bad(say, ex.Message);
		}
		finally {
			try { if (File.Exists(path)) File.Delete(path); } catch { }
		}
	}

	double ms(long tick, List<TempoEvent> tempos) {
		if (tick < 0) tick = 0;
		long at = 0;
		int us = 500000;
		double acc = 0;
		foreach (var tempo in tempos) {
			if (tempo.AbsoluteTime >= tick) break;
			if (tempo.AbsoluteTime > at)
				acc += (tempo.AbsoluteTime - at) * (double)us / Ticks / 1000.0;
			at = tempo.AbsoluteTime;
			if (tempo.MicrosecondsPerQuarterNote > 0) us = tempo.MicrosecondsPerQuarterNote;
		}
		acc += (tick - at) * (double)us / Ticks / 1000.0;
		return acc;
	}

	static int bad(Action<string> say, string msg) {
		say("FAIL " + msg);
		return 1;
	}
}
