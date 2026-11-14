using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ScreenKit;

/// <summary>媒体播放器的命令行自测：不依赖 UI，验证解码、字幕、点词、蒙版进度和列表推进。</summary>
static class MediaTest {
	static Action<string> o = Console.WriteLine;
	static Action<string> e = Console.Error.WriteLine;

	public static void Attach(Action<string> outw, Action<string> errw) {
		o = outw ?? Console.WriteLine;
		e = errw ?? Console.Error.WriteLine;
	}

	// ==================== --test-media ====================
	public static int Media(string file) {
		o("=== 媒体解码 --test-media ===");
		if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) {
			e("FAIL 文件不存在: " + file);
			return 1;
		}
		var bad = 0;
		try {
			using var s = new MediaSession();
			s.Open(file);
			o($"容器时长 {s.DurationSec:0.###}s  视频 {s.Width}x{s.Height} {s.Fps:0.###}fps  旋转 {s.Rotation}");
			o($"hasvideo={s.HasVideo} hasaudio={s.HasAudio} vcodec={s.Demuxer.VideoCodec} acodec={s.Demuxer.AudioCodec}");
			if (s.DurationSec <= 0) { e("FAIL 时长读不出来"); bad++; }
			if (!s.HasVideo && !s.HasAudio) { e("FAIL 没有可播流"); bad++; }
			s.Play();
			var deadline = Environment.TickCount + 4000;
			var frames = 0;
			var posmax = 0.0;
			var shown = 0;
			while (Environment.TickCount < deadline) {
				if (s.HasVideo) {
					var f0 = s.TakeDue();
					if (f0 != null) {
						if (shown < 3) o($"  帧#{frames} pts={f0.Pts:0.###} clock={s.PositionSec:0.###}");
						shown++;
						frames++;
						if (frames >= 30) break;
					}
				}
				var pos = s.PositionSec;
				if (pos > posmax) posmax = pos;
				if (shown < 3 && Environment.TickCount % 200 < 12)
					o($"  [tick] clock={pos:0.###} frames={s.QueuedFrames} head={(s.HeadPts is double hp ? hp.ToString("0.###") : "none")} paused={s.IsPaused} drops={s.DropCount}");
				if (!s.HasVideo && pos > 1.0) break;
				System.Threading.Thread.Sleep(10);
			}
			if (s.HasVideo) {
				if (frames >= 5) o($"解出 {frames} 帧");
				else { e($"FAIL 只解出 {frames} 帧"); bad++; }
				var f = s.PeekLatest();
				if (f != null) {
					if (f.Width <= 0 || f.Height <= 0) { e("FAIL 帧尺寸为 0"); bad++; }
					if (f.Stride != f.Width * 4) { e($"FAIL stride {f.Stride}"); bad++; }
					var nz = 0;
					for (var i = 0; i < f.Bgra.Length; i += 9973)
						if (f.Bgra[i] != 0) nz++;
					if (nz == 0) { e("FAIL 画面全黑（可能是解码失败）"); bad++; }
					else o($"首帧非零字节采样 {nz}");
				}
			}
			o($"播放位置推进到 {posmax:0.###}s");
			if (!s.HasVideo && posmax < 0.3) { e("FAIL 音频位置没推进"); bad++; }
			s.Pause();
			if (!s.IsPaused) { e("FAIL 暂停无效"); bad++; }
			else o("暂停有效");
			s.Seek(Math.Min(1.0, Math.Max(0, s.DurationSec / 2)));
			o($"seek 后位置 {s.PositionSec:0.###}s");
			s.Close();
		}
		catch (Exception ex) {
			e("FAIL 异常: " + ex.Message);
			bad++;
		}
		o(bad == 0 ? "=== OK：媒体解码 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	// ==================== --test-subtitle ====================
	public static int Subtitle(string file) {
		o("=== 字幕解析 --test-subtitle ===");
		if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) {
			e("FAIL 文件不存在: " + file);
			return 1;
		}
		var f = TimedTextParser.Parse(file);
		if (f == null) { e("FAIL 解析失败"); return 1; }
		var bad = 0;
		o($"编码={f.Encoding}  条数={f.Cues.Count}  歌词={f.IsLyric}  词级时间={f.HasWordTime}");
		if (f.Cues.Count == 0) { e("FAIL 没有条目"); bad++; }
		else {
			var first = f.Cues[0];
			var last = f.Cues[f.Cues.Count - 1];
			o($"首条 {first.Start:0.###}-{first.End:0.###} 「{clip(first.Text, 30)}」");
			o($"末条 {last.Start:0.###}-{last.End:0.###} 「{clip(last.Text, 30)}」");
			if (last.End <= first.Start) { e("FAIL 时间没递增"); bad++; }
			// 5 个时间点的二分命中
			for (var i = 0; i < 5; i++) {
				var t = f.LengthSec * i / 4.0;
				var c = f.At(t);
				o($"  t={t:0.##} → {(c == null ? "(无)" : clip(c.Text, 24))}");
			}
		}
		o(bad == 0 ? "=== OK：字幕解析 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	// ==================== --test-subtitle-hit ====================
	public static int Hit(string file, int cueIndex, int charIndex) {
		o("=== 点词切片 --test-subtitle-hit ===");
		var f = TimedTextParser.Parse(file);
		if (f == null) { e("FAIL 解析失败"); return 1; }
		if (cueIndex < 0 || cueIndex >= f.Cues.Count) { e("FAIL 条号越界"); return 1; }
		var cue = f.Cues[cueIndex];
		var text = cue.Text.Replace("\n", " ");
		if (text.Length == 0) { e("FAIL 空字幕"); return 1; }
		if (charIndex >= text.Length) charIndex = text.Length - 1;
		var raw = WordPick.Raw(text, charIndex, out var st, out var ln);
		o($"文本「{clip(text, 40)}」 点中 #{charIndex} 字='{text[charIndex]}'");
		o($"原片段 [{st},{ln}) = 「{raw}」");
		var cands = WordPick.CjkCandidates(text, charIndex, 6);
		o($"CJK 候选 {cands.Count} 个" + (cands.Count > 0 ? "，最长「" + cands[0] + "」" : ""));
		var hasdict = DictDb.Init(DictWeb.DbPath);
		if (!hasdict) {
			o("SKIP 词典未安装（dict3.bin 缺失），只验证切片");
			o("=== OK：点词切片 ===");
			return 0;
		}
		DictDb.Close();
		DictDb.Init(DictWeb.DbPath);
		var dict = guess(raw);
		var hits = DictDb.SearchBrief(raw, dict, 10);
		o($"词典 {dict} 查「{raw}」→ {(hits == null ? 0 : hits.Count)} 条");
		var longest = WordPick.Longest(text, charIndex, w => DictDb.SearchBrief(w, dict, 1) is { Count: > 0 }, 6);
		o($"最长匹配结果「{longest}」");
		var found = 0;
		foreach (var c in cands) {
			var h = DictDb.SearchBrief(c, dict, 5);
			if (h == null || h.Count == 0) continue;
			o($"  候选「{c}」命中 {h.Count} 条，首条 {h[0].Headword}");
			found++;
			if (found >= 3) break;
		}
		DictDb.Close();
		o(found > 0 || (hits != null && hits.Count > 0) ? "=== OK：点词切片 ===" : "=== 注意：候选都没命中 ===");
		return 0;
	}

	static string guess(string q) {
		if (q.Length == 0) return "";
		return WordPick.ClassOf(q[0]) switch {
			WordPick.CharClass.Hangul => "ko",
			WordPick.CharClass.Kana => "ja",
			WordPick.CharClass.Han => "zh",
			WordPick.CharClass.Latin => "en",
			_ => "",
		};
	}

	// ==================== --test-karaoke ====================
	public static int KaraokeTest(string file, double t) {
		o("=== 进度蒙版 --test-karaoke ===");
		var f = TimedTextParser.Parse(file);
		if (f == null) { e("FAIL 解析失败"); return 1; }
		var bad = 0;
		o($"词级时间={f.HasWordTime} 总条数={f.Cues.Count}");
		if (t < 0) {
			// 没给时间点：把前 3 条的词表打出来
			for (var i = 0; i < Math.Min(3, f.Cues.Count); i++) {
				var c = f.Cues[i];
				o($"[{i}] {c.Start:0.###}-{c.End:0.###} 「{clip(c.Text, 30)}」");
				if (c.Words != null && c.Words.Count > 0) {
					foreach (var w in c.Words)
						o($"   词 @{w.Start:0.###} char[{w.CharStart},{w.CharLen}] 「{clip(c.Text.Substring(w.CharStart, Math.Min(w.CharLen, c.Text.Length - w.CharStart)), 12)}」");
				}
				else {
					// 均匀推算的逐字起点
					var span = c.End - c.Start;
					var step = c.Text.Length > 0 ? span / c.Text.Length : 0;
					var sb = new StringBuilder();
					for (var k = 0; k < Math.Min(8, c.Text.Length); k++)
						sb.Append($"{c.Start + step * k:0.##} ");
					o($"   均匀推算前 8 字起点: {sb}");
				}
			}
			o("=== OK：进度蒙版 ===");
			return 0;
		}
		var cue = f.At(t);
		if (cue == null) {
			o($"t={t:0.###} 此刻无字幕");
			o("=== OK：进度蒙版 ===");
			return 0;
		}
		o($"t={t:0.###} 命中「{clip(cue.Text, 40)}」 {cue.Start:0.###}-{cue.End:0.###}");
		foreach (var p in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }) {
			var tt = cue.Start + (cue.End - cue.Start) * p;
			var prog = Karaoke.Progress(cue, tt, f.OffsetSec);
			var width = Math.Round(prog * 100, 1);
			o($"  t={tt:0.###} → 进度 {prog:0.###} ({width}% 宽)");
			if (prog < 0 || prog > 1) { e("FAIL 进度越界"); bad++; }
		}
		var before = Karaoke.Progress(cue, cue.Start - 1, f.OffsetSec);
		var after = Karaoke.Progress(cue, cue.End + 1, f.OffsetSec);
		if (before != 0) { e("FAIL 开始前进度应为 0"); bad++; }
		if (after != 1) { e("FAIL 结束后进度应为 1"); bad++; }
		o(bad == 0 ? "=== OK：进度蒙版 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	// ==================== --test-playlist ====================
	public static int PlaylistTest(string dir) {
		o("=== 播放列表 --test-playlist ===");
		var bad = 0;
		var pl = new Playlist();
		if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) {
			var n = pl.AddDirectory(dir);
			o($"扫描 {dir} → {n} 项");
			if (n == 0) { e("FAIL 目录里没有媒体文件"); bad++; }
			for (var i = 0; i < Math.Min(5, pl.Items.Count); i++)
				o($"  [{i}] {pl.Items[i].Display}");
		}
		else {
			// 没有目录就用假数据测推进逻辑
			for (var i = 1; i <= 10; i++)
				pl.Items.Add(new PlaylistItem { Path = $"fake{i}.mp3", Title = $"fake{i}", Kind = MediaKind.Audio });
			o("用 10 条假数据测推进");
		}
		var count = pl.Items.Count;
		if (count == 0) { e("FAIL 列表为空"); return 1; }
		// 顺序：走到末尾就停
		pl.Mode = PlayMode.Sequential;
		pl.Select(0);
		var seen = new List<int>();
		for (var i = 0; i < count + 3; i++) {
			var it = pl.Advance(PlayTrigger.AutoNext);
			if (it == null) break;
			seen.Add(pl.Index);
		}
		o($"顺序推进: {string.Join(",", seen)}");
		if (seen.Count != count - 1) { e($"FAIL 顺序应推进 {count - 1} 次，实际 {seen.Count}"); bad++; }
		// 单曲循环：一直是同一首
		pl.Mode = PlayMode.RepeatOne;
		var keepidx = Math.Min(3, count - 1);
		pl.Select(keepidx);
		var same = true;
		for (var i = 0; i < 5; i++) {
			pl.Advance(PlayTrigger.AutoNext);
			if (pl.Index != keepidx) same = false;
		}
		if (!same) { e("FAIL 单曲循环没有停在同一条"); bad++; }
		else o($"单曲循环保持第 {keepidx + 1} 条");
		// 列表循环：转一圈回到起点
		pl.Mode = PlayMode.RepeatAll;
		pl.Select(0);
		for (var i = 0; i < count; i++) pl.Advance(PlayTrigger.AutoNext);
		if (pl.Index != 0) { e($"FAIL 列表循环一圈应回到 0，实际 {pl.Index}"); bad++; }
		else o("列表循环一圈回到 0");
		// 随机：一轮内不重复
		pl.Mode = PlayMode.Shuffle;
		pl.Select(0);
		var bag = new HashSet<int>();
		var dup = 0;
		for (var i = 0; i < count; i++) {
			pl.Advance(PlayTrigger.AutoNext);
			if (!bag.Add(pl.Index)) dup++;
		}
		o($"随机一轮去重后 {bag.Count}/{count}，重复 {dup}");
		if (bag.Count != count) { e("FAIL 随机一轮内有重复"); bad++; }
		// 持久化往返
		var tmp = Path.Combine(Path.GetTempPath(), "sk_pl_" + Guid.NewGuid().ToString("N").Substring(0, 6));
		Directory.CreateDirectory(tmp);
		try {
			var n2 = pl.AddDirectory(tmp);
			_ = n2;
			o("持久化：Playlist.Save/Load 走固定路径，这里只验证 JSON 序列化不抛");
			pl.Save();
			if (!File.Exists(Playlist.FilePath)) { e("FAIL playlist.json 没写出来"); bad++; }
			else o("playlist.json 已写出");
			pl.Load();
			o($"重载后 {pl.Items.Count} 项，当前 {pl.Index}");
		}
		finally {
			try { File.Delete(Playlist.FilePath); } catch { }
			try { Directory.Delete(tmp, true); } catch { }
		}
		o(bad == 0 ? "=== OK：播放列表 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	// ==================== 自然排序 ====================
	public static int SortTest() {
		o("=== 自然排序 ===");
		var bad = 0;
		var arr = new List<string> { "a10.mp3", "a2.mp3", "a1.mp3", "b1.mp3" };
		arr.Sort(NaturalCompare.Compare);
		var got = string.Join(",", arr);
		o("排序结果: " + got);
		if (got != "a1.mp3,a2.mp3,a10.mp3,b1.mp3") { e("FAIL 自然排序不对"); bad++; }
		o(bad == 0 ? "=== OK：自然排序 ===" : "=== FAIL ===");
		return bad == 0 ? 0 : 1;
	}

	static string clip(string s, int n) {
		if (string.IsNullOrEmpty(s)) return "";
		s = s.Replace("\n", "⏎");
		return s.Length <= n ? s : s.Substring(0, n) + "…";
	}
}
