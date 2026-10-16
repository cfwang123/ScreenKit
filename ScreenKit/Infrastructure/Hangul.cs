namespace ScreenKit;

/// <summary>Windows 韩语输入法的两套式（KS X 5002）。每个键一个单音，w 是 ㅈ。当前音节随按键改写，完成的音节留在前面。元音不合并成双元音。</summary>
sealed class Hangul {
	string done = "";
	int cho = -1;
	int jung = -1;
	int jong = -1;

	public string Text => done + partial();
	/// <summary>还没提交的当前音节。Esc 只删这一段。</summary>
	public string Partial => partial();

	public void Clear() {
		done = "";
		cho = jung = jong = -1;
	}

	public static string Type(string keys) {
		var h = new Hangul();
		foreach (var ch in keys ?? "") {
			if (ch == '\b') h.Back();
			else h.Push(ch);
		}
		return h.Text;
	}

	public void Back() {
		if (jong >= 0) {
			jong = uncombinejong(jong);
			return;
		}
		if (jung >= 0) {
			jung = -1;
			return;
		}
		if (cho >= 0) {
			cho = -1;
			return;
		}
		if (done.Length > 0) done = done.Substring(0, done.Length - 1);
	}

	public bool Push(char ch) {
		if (!map(ch, out var kind, out var idx)) return false;
		if (kind == 0) pushcons(idx);
		else pushvowel(idx);
		return true;
	}

	void pushcons(int ci) {
		int asjong = chotojong(ci);
		if (cho < 0 && jung < 0) { cho = ci; return; }
		if (cho >= 0 && jung < 0) {
			done += compatcons(cho);
			cho = ci;
			return;
		}
		if (jung >= 0 && jong < 0) {
			if (asjong >= 0) { jong = asjong; return; }
			flush();
			cho = ci;
			return;
		}
		int comb = combinejong(jong, asjong);
		if (asjong >= 0 && comb >= 0) { jong = comb; return; }
		flush();
		cho = ci;
	}

	void pushvowel(int ji) {
		if (cho < 0 && jung < 0) { jung = ji; return; }
		if (cho >= 0 && jung < 0) { jung = ji; return; }
		if (jung >= 0 && jong < 0) {
			flush();
			jung = ji;
			return;
		}
		splitjong(jong, out var keep, out var move);
		jong = keep;
		flush();
		cho = move;
		jung = ji;
	}

	void flush() {
		if (cho >= 0 && jung >= 0) done += syll();
		else if (cho >= 0) done += compatcons(cho);
		else if (jung >= 0) done += compatvowel(jung);
		cho = jung = jong = -1;
	}

	string partial() {
		if (cho >= 0 && jung >= 0) return syll();
		if (cho >= 0) return compatcons(cho).ToString();
		if (jung >= 0) return compatvowel(jung).ToString();
		return "";
	}

	string syll() {
		int j = jong < 0 ? 0 : jong;
		return ((char)(0xAC00 + cho * 588 + jung * 28 + j)).ToString();
	}

	static readonly string lonecons = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
	static char compatcons(int ci) => lonecons[ci];
	static char compatvowel(int ji) => (char)(0x314F + ji);

	static int chotojong(int ci) {
		switch (ci) {
			case 0: return 1;
			case 1: return 2;
			case 2: return 4;
			case 3: return 7;
			case 5: return 8;
			case 6: return 16;
			case 7: return 17;
			case 9: return 19;
			case 10: return 20;
			case 11: return 21;
			case 12: return 22;
			case 14: return 23;
			case 15: return 24;
			case 16: return 25;
			case 17: return 26;
			case 18: return 27;
			default: return -1;
		}
	}

	static int jongtocho(int j) {
		switch (j) {
			case 1: return 0;
			case 2: return 1;
			case 4: return 2;
			case 7: return 3;
			case 8: return 5;
			case 16: return 6;
			case 17: return 7;
			case 19: return 9;
			case 20: return 10;
			case 21: return 11;
			case 22: return 12;
			case 23: return 14;
			case 24: return 15;
			case 25: return 16;
			case 26: return 17;
			case 27: return 18;
			default: return 0;
		}
	}

	static int combinejong(int cur, int add) {
		if (cur == 1 && add == 19) return 3;
		if (cur == 4 && add == 22) return 5;
		if (cur == 4 && add == 27) return 6;
		if (cur == 8 && add == 1) return 9;
		if (cur == 8 && add == 16) return 10;
		if (cur == 8 && add == 17) return 11;
		if (cur == 8 && add == 19) return 12;
		if (cur == 8 && add == 25) return 13;
		if (cur == 8 && add == 26) return 14;
		if (cur == 8 && add == 27) return 15;
		if (cur == 17 && add == 19) return 18;
		return -1;
	}

	static void splitjong(int j, out int keep, out int move) {
		keep = -1;
		int second = -1;
		switch (j) {
			case 3: keep = 1; second = 19; break;
			case 5: keep = 4; second = 22; break;
			case 6: keep = 4; second = 27; break;
			case 9: keep = 8; second = 1; break;
			case 10: keep = 8; second = 16; break;
			case 11: keep = 8; second = 17; break;
			case 12: keep = 8; second = 19; break;
			case 13: keep = 8; second = 25; break;
			case 14: keep = 8; second = 26; break;
			case 15: keep = 8; second = 27; break;
			case 18: keep = 17; second = 19; break;
		}
		move = second >= 0 ? jongtocho(second) : jongtocho(j);
		if (second >= 0) return;
		keep = -1;
	}

	static int uncombinejong(int j) {
		switch (j) {
			case 3: return 1;
			case 5:
			case 6: return 4;
			case 9:
			case 10:
			case 11:
			case 12:
			case 13:
			case 14:
			case 15: return 8;
			case 18: return 17;
			default: return -1;
		}
	}

	static bool map(char ch, out int kind, out int idx) {
		kind = 0;
		idx = 0;
		switch (ch) {
			case 'r': idx = 0; return true;
			case 'R': idx = 1; return true;
			case 's': idx = 2; return true;
			case 'e': idx = 3; return true;
			case 'E': idx = 4; return true;
			case 'f': idx = 5; return true;
			case 'a': idx = 6; return true;
			case 'q': idx = 7; return true;
			case 'Q': idx = 8; return true;
			case 't': idx = 9; return true;
			case 'T': idx = 10; return true;
			case 'd': idx = 11; return true;
			case 'w': idx = 12; return true;
			case 'W': idx = 13; return true;
			case 'c': idx = 14; return true;
			case 'z': idx = 15; return true;
			case 'x': idx = 16; return true;
			case 'v': idx = 17; return true;
			case 'g': idx = 18; return true;
			case 'k': kind = 1; idx = 0; return true;
			case 'o': kind = 1; idx = 1; return true;
			case 'i': kind = 1; idx = 2; return true;
			case 'j': kind = 1; idx = 4; return true;
			case 'p': kind = 1; idx = 5; return true;
			case 'u': kind = 1; idx = 6; return true;
			case 'h': kind = 1; idx = 8; return true;
			case 'y': kind = 1; idx = 12; return true;
			case 'n': kind = 1; idx = 13; return true;
			case 'b': kind = 1; idx = 17; return true;
			case 'm': kind = 1; idx = 18; return true;
			case 'l': kind = 1; idx = 20; return true;
			default: return false;
		}
	}
}
