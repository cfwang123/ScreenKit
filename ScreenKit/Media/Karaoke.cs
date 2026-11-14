using System;
using System.Collections.Generic;

namespace ScreenKit;

/// <summary>
/// 一条 cue 在时刻 t 的已唱比例。有词级时间就按词推进，没有就按权重均匀推算。
/// 权重：CJK 每字 1.0，拉丁词按音节数，空白标点 0。
/// </summary>
static class Karaoke {
	/// <summary>返回 0..1；cue 为空或已结束返回 1，还没开始返回 0。</summary>
	public static double Progress(TimedTextCue cue, double tSec, double offsetSec) {
		if (cue == null) return 0;
		var t = tSec - offsetSec;
		if (t <= cue.Start) return 0;
		if (t >= cue.End) return 1;
		var span = cue.End - cue.Start;
		if (span <= 0) return 1;
		if (cue.Words != null && cue.Words.Count > 0) {
			var p = wordprogress(cue, t);
			// 词级时间不覆盖整段时，剩余部分按线性补齐，避免唱完最后一个词就停在半截
			var last = cue.Words[cue.Words.Count - 1];
			var tailstart = last.Start;
			if (t > tailstart) return Math.Max(p, (t - cue.Start) / span);
			return p;
		}
		return weighted(cue.Text, cue.Start, cue.End, t);
	}

	static double wordprogress(TimedTextCue cue, double t) {
		var ws = cue.Words;
		var idx = -1;
		for (var i = 0; i < ws.Count; i++) {
			if (ws[i].Start <= t) idx = i;
			else break;
		}
		if (idx < 0) return 0;
		var cur = ws[idx];
		var nextstart = idx + 1 < ws.Count ? ws[idx + 1].Start : cue.End;
		var wspan = nextstart - cur.Start;
		var inner = wspan > 0 ? (t - cur.Start) / wspan : 1.0;
		if (inner < 0) inner = 0;
		if (inner > 1) inner = 1;
		var n = Math.Max(1, cue.Text.Length);
		var done = 0.0;
		for (var i = 0; i < idx; i++) done += Math.Max(1, ws[i].CharLen);
		done += Math.Max(1, cur.CharLen) * inner;
		// 用字符数而不是词数，整体进度才和裁剪宽度一致
		var cut = cur.CharStart + cur.CharLen * inner;
		_ = n;
		_ = done;
		return Math.Min(1.0, cut / n);
	}

	/// <summary>无词级时间：按权重把 [start,end] 摊到每个字上。</summary>
	static double weighted(string text, double start, double end, double t) {
		if (string.IsNullOrEmpty(text)) return 1;
		var weights = new double[text.Length];
		var total = 0.0;
		for (var i = 0; i < text.Length;) {
			var cls = WordPick.ClassOf(text[i]);
			if (cls == WordPick.CharClass.Han || cls == WordPick.CharClass.Kana || cls == WordPick.CharClass.Hangul) {
				weights[i] = 1.0;
				total += 1.0;
				i++;
			}
			else if (cls == WordPick.CharClass.Latin || cls == WordPick.CharClass.Digit) {
				var j = i;
				while (j < text.Length && (WordPick.ClassOf(text[j]) == cls || WordPick.ClassOf(text[j]) == WordPick.CharClass.Latin)) j++;
				var syllables = Math.Max(1, CountSyllables(text.Substring(i, j - i)));
				for (var k = i; k < j; k++) weights[k] = (double)syllables / (j - i);
				total += syllables;
				i = j;
			}
			else {
				weights[i] = 0;
				i++;
			}
		}
		if (total <= 0) return 1;
		var span = end - start;
		if (span <= 0) return 1;
		var elapsed = (t - start) / span * total;
		var acc = 0.0;
		for (var i = 0; i < weights.Length; i++) {
			if (acc + weights[i] >= elapsed) {
				if (weights[i] <= 0) return (double)i / text.Length;
				var inner = (elapsed - acc) / weights[i];
				return Math.Min(1.0, (i + inner) / text.Length);
			}
			acc += weights[i];
		}
		return 1;
	}

	/// <summary>连续元音组数粗估音节，最少 1。</summary>
	static int CountSyllables(string w) {
		var n = 0;
		var invowel = false;
		foreach (var c in w.ToLowerInvariant()) {
			var vowel = c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
			if (vowel && !invowel) n++;
			invowel = vowel;
		}
		if (n <= 0) n = 1;
		// 结尾的 e 通常不发音
		var lower = w.ToLowerInvariant();
		if (n > 1 && lower.EndsWith("e", StringComparison.Ordinal)) n--;
		return n;
	}
}
