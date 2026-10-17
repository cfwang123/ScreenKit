using System;
using System.Collections.Generic;

namespace ScreenKit;

class ImeEngine {
	readonly HashSet<int> eaten = new HashSet<int>();
	Lang cjk = Lang.Wubi;
	bool shiften;
	bool caps;
	bool trad;
	bool off;
	bool shiftdown;
	bool shiftused;
	bool q1;
	bool q2;
	string code = "";
	int page;
	List<Cand> cands = new List<Cand>();
	readonly Kana kana = new Kana();
	readonly Hangul hang = new Hangul();
	Outcome cur;
	Hot hotzh = Hot.Of(true, true, false, false, Vk.D0 + 1);
	Hot hotja = Hot.Of(true, true, false, false, Vk.D0 + 2);
	Hot hotko = Hot.Of(true, true, false, false, Vk.D0 + 3);
	Hot hotoff = Hot.Of(true, true, false, false, Vk.D0 + 4);
	Hot hotcycle = Hot.Of(true, false, false, false, Vk.Oem3);
	bool usezh = true;
	bool useja = true;
	bool useko = true;
	bool gvimInsertEn;
	bool gvimInsert;

	public ImeEngine() { }

	/// <summary>当前在 gVim 插入/替换模式；中英切换会写入插入记忆。</summary>
	public bool GvimInsert {
		get => gvimInsert;
		set => gvimInsert = value;
	}

	public event Action StateChanged;

	public bool Off => off;
	public Lang Cjk => cjk;
	public View Now => off || caps || shiften ? View.En : cjk == Lang.Ja ? View.Ja : cjk == Lang.Ko ? View.Ko : View.Wubi;
	public bool Horizontal => Now == View.Wubi;
	public bool Trad => trad;
	public int Page => page;
	public int PageCount => cands.Count == 0 ? 0 : (cands.Count + 8) / 9;
	public IList<Cand> PageItems {
		get {
			var list = new List<Cand>();
			int from = page * 9;
			for (int i = from; i < cands.Count && i < from + 9; i++) list.Add(cands[i]);
			return list;
		}
	}
	public string Badge {
		get {
			if (off) return "关";
			switch (Now) {
				case View.Ja: return "日";
				case View.Ko: return "韩";
				case View.En: return "英";
				default: return "五";
			}
		}
	}
	public string Preedit {
		get {
			if (Now == View.En) return "";
			if (Now == View.Wubi) return code;
			if (Now == View.Ja) return kana.Text;
			return hang.Jamo;
		}
	}
	public bool ShowBar => !off && Now != View.En && (Preedit.Length > 0 || cands.Count > 0);
	public bool UseZh => usezh;
	public bool UseJa => useja;
	public bool UseKo => useko;
	public Hot HotZh => hotzh;
	public Hot HotJa => hotja;
	public Hot HotKo => hotko;
	public Hot HotOff => hotoff;
	public Hot HotCycle => hotcycle;

	public void SetCaps(bool on) {
		if (caps == on) return;
		caps = on;
		if (on) clearcomp();
	}

	public void Discard() { clearcomp(); }

	public void LoadMeta(bool tradOn, Lang lang, bool en, bool paused = false) {
		trad = tradOn;
		shiften = en;
		off = paused;
		if (lang == Lang.Ja || lang == Lang.Ko || lang == Lang.Wubi) cjk = lang;
	}

	public void LoadOpts(string zh, string ja, string ko, string paused, string cycle, bool zhOn, bool jaOn, bool koOn) {
		var a = Hot.Parse(zh);
		var b = Hot.Parse(ja);
		var c = Hot.Parse(ko);
		var d = Hot.Parse(paused);
		var e = Hot.Parse(cycle);
		if (a.Ok) hotzh = a;
		if (b.Ok) hotja = b;
		if (c.Ok) hotko = c;
		if (d.Ok) hotoff = d;
		if (e.Ok) hotcycle = e;
		usezh = zhOn;
		useja = jaOn;
		useko = koOn;
	}

	public void ApplyOpts(bool zhOn, bool jaOn, bool koOn, Hot zh, Hot ja, Hot ko, Hot paused, Hot cycle) {
		usezh = zhOn;
		useja = jaOn;
		useko = koOn;
		if (zh != null) hotzh = zh;
		if (ja != null) hotja = ja;
		if (ko != null) hotko = ko;
		if (paused != null) hotoff = paused;
		if (cycle != null) hotcycle = cycle;
		if (!off && !langon(cjk)) SetOff(true);
		ping();
	}

	public void Enter(Lang lang) {
		if (!langon(lang)) return;
		enter(lang);
	}

	public void SetOff(bool value) {
		if (!value && off) {
			var pick = langon(cjk) ? cjk : firstlang();
			if (pick == null) return;
			cjk = pick.Value;
			ping();
		}
		if (off == value) return;
		off = value;
		if (off) shiften = false;
		clearcomp();
		ping();
	}

	public void Next() { cycle(); }

	public void ToggleEn() {
		if (off) return;
		shiften = !shiften;
		clearcomp();
		if (gvimInsert) gvimInsertEn = shiften;
		ping();
	}

	/// <summary>gVim 关闭系统 IME 时：强制英文，不改插入记忆。</summary>
	public void ForceEnglishForVim() {
		if (off) return;
		if (!shiften) {
			shiften = true;
			clearcomp();
			ping();
			return;
		}
		if (busy()) clearcomp();
	}

	/// <summary>gVim 打开系统 IME 时：恢复上次插入时的中英状态。</summary>
	public void RestoreGvimInsertEn() {
		if (off) return;
		if (shiften != gvimInsertEn) {
			shiften = gvimInsertEn;
			clearcomp();
			ping();
			return;
		}
		if (busy()) clearcomp();
	}

	public string LangCode => cjk == Lang.Ja ? "ja" : cjk == Lang.Ko ? "ko" : "wubi";

	public Outcome Key(int vk, bool down, bool shift, bool ctrl, bool alt, bool win) {
		cur = new Outcome();
		if (down && modehot(vk, shift, ctrl, alt, win)) {
			markeat(vk);
			return cur;
		}
		bool grave = ctrl && !shift && !alt && !win && vk == Vk.Oem3;
		if (off && !(down && grave)) {
			if (!down) {
				if (Vk.IsShift(vk)) { shiftdown = false; shiftused = false; }
				cur.Eat = eaten.Remove(vk);
			}
			return cur;
		}
		if (!down) {
			if (Vk.IsShift(vk)) shiftup();
			cur.Eat = eaten.Remove(vk);
			return cur;
		}
		if (Vk.IsShift(vk)) {
			shiftdown = true;
			shiftused = false;
			return cur;
		}
		if (shift) shiftused = true;
		bool hot = hotkey(vk, shift, ctrl, alt, win);
		if (hot) {
			markeat(vk);
			return cur;
		}
		if (ctrl || alt || win) {
			if (busy()) clearcomp();
			return cur;
		}
		if (Now == View.En) {
			if (vk == Vk.Space) send(" ");
			else {
				var half = Punct.Half(vk, shift);
				if (half != null) send(half);
			}
			if (cur.Texts.Count > 0) markeat(vk);
			return cur;
		}
		dispatch(vk, shift);
		if (cur.Eat) markeat(vk);
		return cur;
	}

	public Outcome Pick(int visible) {
		cur = new Outcome();
		int abs = page * 9 + visible;
		if (abs >= 0 && abs < cands.Count) commitabs(abs);
		cur.Eat = true;
		return cur;
	}

	public Outcome PageBy(int delta) {
		cur = new Outcome();
		turn(delta);
		cur.Eat = true;
		return cur;
	}

	bool modehot(int vk, bool shift, bool ctrl, bool alt, bool win) {
		if (hotzh.Hit(vk, shift, ctrl, alt, win)) {
			if (usezh) enter(Lang.Wubi);
			return true;
		}
		if (hotja.Hit(vk, shift, ctrl, alt, win)) {
			if (useja) enter(Lang.Ja);
			return true;
		}
		if (hotko.Hit(vk, shift, ctrl, alt, win)) {
			if (useko) enter(Lang.Ko);
			return true;
		}
		if (hotoff.Hit(vk, shift, ctrl, alt, win)) {
			SetOff(true);
			return true;
		}
		return false;
	}

	void enter(Lang lang) {
		off = false;
		shiften = false;
		cjk = lang;
		clearcomp();
		ping();
		ping();
		ping();
	}

	void cycle() {
		shiften = false;
		ping();
		clearcomp();
		if (off) {
			var first = firstlang();
			if (first == null) return;
			off = false;
			cjk = first.Value;
		} else {
			var next = nextlang(cjk);
			if (next == null) off = true;
			else cjk = next.Value;
		}
		ping();
		ping();
	}

	bool langon(Lang lang) {
		if (lang == Lang.Ja) return useja;
		if (lang == Lang.Ko) return useko;
		return usezh;
	}

	Lang? firstlang() {
		if (usezh) return Lang.Wubi;
		if (useja) return Lang.Ja;
		if (useko) return Lang.Ko;
		return null;
	}

	Lang? nextlang(Lang cur) {
		if (cur == Lang.Wubi) {
			if (useja) return Lang.Ja;
			if (useko) return Lang.Ko;
		} else if (cur == Lang.Ja) {
			if (useko) return Lang.Ko;
		}
		return null;
	}

	void markeat(int vk) {
		cur.Eat = true;
		eaten.Add(vk);
	}

	void shiftup() {
		bool pure = shiftdown && !shiftused && !caps;
		shiftdown = false;
		shiftused = false;
		if (!pure) return;
		shiften = !shiften;
		clearcomp();
		if (gvimInsert) gvimInsertEn = shiften;
		ping();
	}

	bool hotkey(int vk, bool shift, bool ctrl, bool alt, bool win) {
		if (alt || win) return false;
		if (ctrl && shift && vk == Vk.F7) {
			clearcomp();
			cur.OpenDict = true;
			return true;
		}
		if (ctrl && shift && vk == Vk.F) {
			trad = !trad;
			ping();
			if (Now == View.Wubi && code.Length > 0) reload(false);
			return true;
		}
		if (hotcycle.Hit(vk, shift, ctrl, alt, win)) {
			cycle();
			return true;
		}
		if (off) return false;
		if (ctrl && !shift && !alt && Vk.IsDigit(vk) && vk != Vk.D0 && Now != View.En && cands.Count > 0) {
			pin(vk - Vk.D0 - 1);
			return true;
		}
		return false;
	}

	void dispatch(int vk, bool shift) {
		if (vk == Vk.Esc) {
			if (!busy()) return;
			clearcomp();
			cur.Eat = true;
			return;
		}
		if (vk == Vk.Back) {
			if (!busy()) return;
			back();
			cur.Eat = true;
			return;
		}
		if (Vk.IsArrow(vk)) {
			if (!busy()) return;
			cur.Eat = true;
			return;
		}
		if (vk == Vk.PageUp || vk == Vk.PageDown) {
			if (cands.Count == 0) return;
			turn(vk == Vk.PageUp ? -1 : 1);
			cur.Eat = true;
			return;
		}
		switch (Now) {
			case View.Wubi: wubi(vk, shift); break;
			case View.Ja: japan(vk, shift); break;
			case View.Ko: korea(vk, shift); break;
		}
	}

	bool busy() => Preedit.Length > 0 || cands.Count > 0;

	void wubi(int vk, bool shift) {
		if (!shift && (vk == Vk.OemMinus || vk == Vk.OemPlus)) {
			if (cands.Count > 0) { turn(vk == Vk.OemMinus ? -1 : 1); cur.Eat = true; return; }
			dropcode();
			send(vk == Vk.OemMinus ? "-" : "=");
			cur.Eat = true;
			return;
		}
		if (Vk.IsLetter(vk) && !shift) {
			char ch = Punct.VkChar(vk, false);
			if (ch == 'z') {
				if (code.Length > 0) cur.Eat = true;
				else send("z");
				cur.Eat = true;
				return;
			}
			letter(ch);
			cur.Eat = true;
			return;
		}
		if (Vk.IsLetter(vk) && shift) {
			dropcode();
			send(Punct.VkChar(vk, true).ToString());
			cur.Eat = true;
			return;
		}
		if (Vk.IsDigit(vk) && !shift) {
			int n = vk - Vk.D0;
			if (n >= 1 && n <= 9 && cands.Count > 0) {
				commitabs(page * 9 + n - 1);
				cur.Eat = true;
				return;
			}
			dropcode();
			send(((char)('0' + n)).ToString());
			cur.Eat = true;
			return;
		}
		if (vk == Vk.Space) {
			if (cands.Count > 0) commitabs(0);
			else if (code.Length > 0) dropcode();
			else send(" ");
			cur.Eat = true;
			return;
		}
		if (vk == Vk.Enter) {
			if (code.Length == 0) return;
			send(code);
			clearcomp();
			cur.Eat = true;
			return;
		}
		var p = Punct.Wubi(vk, shift, ref q1, ref q2);
		if (p != null) {
			if (cands.Count > 0) commitabs(0);
			else dropcode();
			send(p);
			cur.Eat = true;
		}
	}

	void letter(char ch) {
		if (code.Length < 4) {
			code += ch;
			reload(true);
			return;
		}
		commitabs(0);
		code = ch.ToString();
		reload(true);
	}

	void japan(int vk, bool shift) {
		if (!shift && vk == Vk.OemMinus) {
			kana.Choon();
			reloadja();
			cur.Eat = true;
			return;
		}
		if (!shift && vk == Vk.OemPlus) {
			flushja();
			send("=");
			cur.Eat = true;
			return;
		}
		if (Vk.IsLetter(vk) && !shift) {
			kana.Push(Punct.VkChar(vk, false));
			reloadja();
			cur.Eat = true;
			return;
		}
		if (vk == Vk.Space) {
			kana.FinishN();
			if (kana.KanaText.Length > 0) {
				reloadja();
				commitabs(0);
			} else {
				kana.Clear();
				send(" ");
			}
			cur.Eat = true;
			return;
		}
		if (vk == Vk.Enter) {
			if (!busy()) return;
			kana.FinishN();
			send(kana.KanaText.Length > 0 ? kana.KanaText : kana.Text);
			clearcomp();
			cur.Eat = true;
			return;
		}
		if (Vk.IsDigit(vk) && !shift) {
			int n = vk - Vk.D0;
			if (n >= 1 && n <= 9 && cands.Count > 0) {
				commitabs(page * 9 + n - 1);
				cur.Eat = true;
				return;
			}
		}
		var p = Punct.Ja(vk, shift);
		if (p != null && p != "-") {
			if (kana.HasKana) flushja();
			else clearcomp();
			send(p);
			cur.Eat = true;
		}
	}

	void reloadja() {
		cands.Clear();
		page = 0;
		if (kana.KanaText.Length == 0) return;
		cands = ImeLex.QueryJa(kana.KanaText);
	}

	void flushja() {
		kana.FinishN();
		reloadja();
		if (cands.Count > 0) commitabs(0);
		else clearcomp();
	}

	void korea(int vk, bool shift) {
		if (!shift && (vk == Vk.OemMinus || vk == Vk.OemPlus)) {
			if (cands.Count > 0) { turn(vk == Vk.OemMinus ? -1 : 1); cur.Eat = true; return; }
			hang.Clear();
			cands.Clear();
			send(vk == Vk.OemMinus ? "-" : "=");
			cur.Eat = true;
			return;
		}
		if (Vk.IsLetter(vk)) {
			hang.Push(Punct.VkChar(vk, shift));
			reloadkr();
			cur.Eat = true;
			return;
		}
		if (vk == Vk.Space) {
			if (hang.Ready && cands.Count > 0) {
				commitabs(0);
				send(" ");
			} else if (hang.Text.Length == 0) send(" ");
			else {
				hang.Clear();
				cands.Clear();
			}
			cur.Eat = true;
			return;
		}
		if (vk == Vk.Enter) {
			if (hang.Text.Length == 0) return;
			send(hang.Text);
			clearcomp();
			cur.Eat = true;
			return;
		}
		if (Vk.IsDigit(vk) && !shift) {
			int n = vk - Vk.D0;
			if (n >= 1 && n <= 9 && cands.Count > 0) {
				commitabs(page * 9 + n - 1);
				cur.Eat = true;
				return;
			}
		}
		var p = Punct.Half(vk, shift);
		if (p != null) {
			if (cands.Count > 0) commitabs(0);
			else hang.Clear();
			send(p);
			cur.Eat = true;
		}
	}

	void reloadkr() {
		cands.Clear();
		page = 0;
		var typed = hang.Text;
		if (typed.Length == 0) return;
		cands.Add(new Cand { Text = typed, Raw = typed, Comment = ImeLex.Hanja(typed) });
		var lo = new List<string>();
		var hi = new List<string>();
		hang.Spans(lo, hi);
		foreach (var p in ImeLex.QueryKr(lo, hi, typed)) cands.Add(p);
	}

	void back() {
		if (Now == View.Wubi) {
			if (code.Length == 0) return;
			code = code.Substring(0, code.Length - 1);
			reload(false);
			return;
		}
		if (Now == View.Ja) {
			kana.Back();
			reloadja();
			return;
		}
		hang.Back();
		reloadkr();
	}

	void dropcode() {
		code = "";
		cands.Clear();
		page = 0;
	}

	void reload(bool auto) {
		if (code.Length == 0) {
			cands.Clear();
			page = 0;
			return;
		}
		cands = ImeLex.QueryWubi(code, trad);
		if (page >= PageCount) page = 0;
		if (!auto) return;
		if (code.Length == 4 && cands.Count == 1) commitabs(0);
		else if (code.Length == 4 && cands.Count == 0) dropcode();
	}

	void commitabs(int abs) {
		if (abs < 0 || abs >= cands.Count) return;
		var c = cands[abs];
		var vw = Now;
		var text = c.Text;
		var raw = c.Raw;
		var cd = c.Code;
		clearcomp();
		send(text);
		if (vw == View.Wubi && raw != null && raw.Length > 0) ImeLex.TouchWubi(cd, raw);
		if (vw == View.Ko && abs > 0) ImeLex.TouchKr(text);
		if (vw == View.Ja && JaCand.HasKanji(raw)) ImeLex.TouchJa(cd, raw);
	}

	void pin(int visible) {
		int abs = page * 9 + visible;
		if (abs < 0 || abs >= cands.Count) return;
		var c = cands[abs];
		if (Now == View.Wubi) ImeLex.TogglePinWubi(c.Code, c.Raw);
		else if (Now == View.Ko) ImeLex.TogglePinKr(c.Text);
		else if (Now == View.Ja) ImeLex.TogglePinJa(c.Code, c.Raw);
		else return;
		if (Now == View.Wubi) reload(false);
		else if (Now == View.Ja) reloadja();
		else reloadkr();
	}

	void turn(int delta) {
		if (PageCount <= 0) return;
		int n = page + delta;
		if (n < 0) n = 0;
		if (n >= PageCount) n = PageCount - 1;
		page = n;
	}

	void clearcomp() {
		code = "";
		page = 0;
		cands.Clear();
		kana.Clear();
		hang.Clear();
	}

	void ping() { try { StateChanged?.Invoke(); } catch { } }

	void send(string s) {
		if (!string.IsNullOrEmpty(s)) cur.Texts.Add(s);
	}
}
