(function(){
	var exp = null;
	if (typeof module !== "undefined" && module.exports) exp = module.exports;

	var CHO = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
	var JUNG = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
	var JONG = ["", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ"];
	var A = vi("ㅏ"), AE = vi("ㅐ"), YA = vi("ㅑ"), YAE = vi("ㅒ"), EO = vi("ㅓ"), E = vi("ㅔ");
	var YEO = vi("ㅕ"), YE = vi("ㅖ"), O = vi("ㅗ"), WA = vi("ㅘ"), WAE = vi("ㅙ"), OE = vi("ㅚ");
	var YO = vi("ㅛ"), U = vi("ㅜ"), WEO = vi("ㅝ"), EU = vi("ㅡ"), I = vi("ㅣ");
	var IEUNG = ci("ㅇ"), HIEUT = ci("ㅎ"), RIEULC = ci("ㄹ"), SIOT = ci("ㅅ"), MIEUM = ci("ㅁ");
	var PIEUPC = ci("ㅍ"), NIEUNC = ci("ㄴ"), TTIEUT = ci("ㄸ"), CHIEUT = ci("ㅊ");
	var J_R = zi("ㄹ"), J_B = zi("ㅂ"), J_N = zi("ㄴ"), J_SS = zi("ㅆ"), J_H = zi("ㅎ"), J_BS = zi("ㅄ");
	var IRR_D = { "걷": 1, "깨닫": 1, "듣": 1, "묻": 1, "싣": 1, "일컫": 1 };
	var IRR_B = {
		"가볍": 1, "고맙": 1, "굽": 1, "눕": 1, "덥": 1, "돕": 1, "맵": 1, "무겁": 1, "무섭": 1, "밉": 1,
		"반갑": 1, "아름답": 1, "어렵": 1, "쉽": 1, "즐겁": 1, "줍": 1, "춥": 1, "가깝": 1, "귀엽": 1,
		"더럽": 1, "부드럽": 1, "어둡": 1, "부럽": 1, "안타깝": 1, "두껍": 1, "뜨겁": 1, "차갑": 1,
		"외롭": 1, "새롭": 1, "괴롭": 1, "그립": 1, "곱": 1, "아깝": 1, "부끄럽": 1
	};
	var BVERB = { "돕": 1, "굽": 1, "눕": 1, "줍": 1 };
	var IRR_S = { "긋": 1, "낫": 1, "붓": 1, "잇": 1, "짓": 1, "젓": 1 };
	var IRR_H = {
		"그렇": 1, "이렇": 1, "저렇": 1, "어떻": 1, "아무렇": 1, "파랗": 1, "빨갛": 1, "노랗": 1,
		"하얗": 1, "까맣": 1, "퍼렇": 1, "허옇": 1, "누렇": 1
	};
	var VERB_IDA = {
		"보이다": 1, "먹이다": 1, "죽이다": 1, "속이다": 1, "깎이다": 1, "섞이다": 1, "쌓이다": 1,
		"쓰이다": 1, "트이다": 1, "녹이다": 1, "높이다": 1, "기울이다": 1, "끼이다": 1
	};
	var NOSPLIT = {
		"좋아하": 1, "싫어하": 1, "슬퍼하": 1, "기뻐하": 1, "아파하": 1, "예뻐하": 1, "이뻐하": 1,
		"무서워하": 1, "부러워하": 1, "미워하": 1, "귀여워하": 1, "안타까워하": 1, "그리워하": 1,
		"외로워하": 1, "위하": 1, "대하": 1, "관하": 1, "향하": 1, "의하": 1, "면하": 1
	};
	var ADJ = {
		"좋다": 1, "많다": 1, "적다": 1, "크다": 1, "작다": 1, "높다": 1, "낮다": 1, "길다": 1, "짧다": 1,
		"멀다": 1, "가깝다": 1, "넓다": 1, "좁다": 1, "느리다": 1, "같다": 1, "쉽다": 1, "어렵다": 1,
		"춥다": 1, "덥다": 1, "뜨겁다": 1, "차갑다": 1, "맵다": 1, "달다": 1, "짜다": 1, "시다": 1,
		"맛없다": 1, "재미없다": 1, "예쁘다": 1, "이쁘다": 1, "아름답다": 1, "귀엽다": 1, "바쁘다": 1,
		"아프다": 1, "슬프다": 1, "기쁘다": 1, "나쁘다": 1, "배고프다": 1, "목마르다": 1, "졸리다": 1,
		"더럽다": 1, "괜찮다": 1, "싫다": 1, "굵다": 1, "얇다": 1, "두껍다": 1, "가볍다": 1, "무겁다": 1,
		"밝다": 1, "어둡다": 1, "부드럽다": 1, "외롭다": 1, "새롭다": 1, "괴롭다": 1, "부럽다": 1,
		"그립다": 1, "반갑다": 1, "고맙다": 1, "행복하다": 1, "피곤하다": 1, "건강하다": 1, "친절하다": 1,
		"조용하다": 1, "깨끗하다": 1, "복잡하다": 1, "간단하다": 1, "중요하다": 1, "필요하다": 1,
		"가능하다": 1, "불가능하다": 1, "위험하다": 1, "안전하다": 1, "편하다": 1, "불편하다": 1,
		"미안하다": 1, "감사하다": 1, "똑똑하다": 1, "답답하다": 1, "확실하다": 1, "분명하다": 1,
		"충분하다": 1, "부족하다": 1, "유명하다": 1, "다양하다": 1, "심각하다": 1, "약하다": 1, "강하다": 1,
		"자유롭다": 1, "평화롭다": 1, "향기롭다": 1, "해롭다": 1, "이롭다": 1, "게으르다": 1, "빠르다": 1,
		"다르다": 1, "배부르다": 1, "서투르다": 1, "고프다": 1, "파랗다": 1, "빨갛다": 1, "노랗다": 1,
		"하얗다": 1, "까맣다": 1, "퍼렇다": 1, "허옇다": 1, "누렇다": 1, "푸르다": 1, "노르다": 1,
		"그렇다": 1, "이렇다": 1, "저렇다": 1, "어떻다": 1, "아무렇다": 1, "없다": 1
	};
	var GRP = {
		base: ["基本", "기본", "Base"],
		now: ["现在", "현재", "Present"],
		past: ["过去", "과거", "Past"],
		fut: ["将来", "미래", "Future"],
		prog: ["进行", "진행", "Progressive"],
		cmd: ["命令", "명령", "Imperative"],
		prop: ["提议", "청유", "Suggest"],
		neg: ["否定", "부정", "Negative"],
		hon: ["尊敬", "존칭", "Honorific"],
		con: ["连接", "연결", "Connective"],
		mod: ["定语", "관형", "Modifier"],
		wrt: ["书面", "문어", "Written"],
		ext: ["其他", "기타", "Other"],
		gs: ["庆尚道", "경상도", "Gyeongsang"],
		jl: ["全罗道", "전라도", "Jeolla"],
		cc: ["忠清道", "충청도", "Chungcheong"],
		jj: ["济州", "제주", "Jeju"],
		hg: ["咸镜道", "함경도", "Hamgyeong"],
		pg: ["平安道", "평안도", "Pyeongan"]
	};
	var NOTE = {
		v: ["动词", "동사", "verb"],
		a: ["形容词", "형용사", "adjective"],
		itda: ["있다", "있다", "있다"],
		ida: ["이다", "이다", "이다"],
		anida: ["아니다", "아니다", "아니다"],
		d: ["ㄷ 不规则", "ㄷ 불규칙", "ㄷ irregular"],
		b: ["ㅂ 不规则", "ㅂ 불규칙", "ㅂ irregular"],
		s: ["ㅅ 不规则", "ㅅ 불규칙", "ㅅ irregular"],
		h: ["ㅎ 不规则", "ㅎ 불규칙", "ㅎ irregular"],
		reu: ["르 不规则", "르 불규칙", "르 irregular"],
		eu: ["ㅡ 不规则", "ㅡ 불규칙", "ㅡ irregular"],
		reo: ["러 不规则", "러 불규칙", "러 irregular"],
		hada: ["하다", "하다", "hada"],
		si: ["시다", "시다", "sida"],
		noa: ["놓다", "놓다", "놓다"],
		geot: ["ㄷ 不规则，걷다 按「走」", "ㄷ 불규칙, 걷다(걸어요)", "ㄷ irregular, 걷다 as walk"],
		mut: ["ㄷ 不规则，묻다 按「问」", "ㄷ 불규칙, 묻다(물어요)", "ㄷ irregular, 묻다 as ask"],
		bad: ["请输入以 다 结尾的词典形，如 먹다、하다", "다로 끝나는 사전형. 예: 먹다, 하다", "Enter a dictionary form ending in 다, such as 먹다"],
		dict: ["词典", "사전", "dictionary"]
	};
	var LBL = {
		dict: ["词典", "사전", "dictionary"],
		hap: ["敬语", "높임", "formal"],
		hey: ["半敬语", "해요체", "polite"],
		ban: ["非敬语", "반말", "casual"],
		plain: ["书面", "해라체", "plain"],
		qhap: ["敬语疑问", "높임 질문", "formal question"],
		qplain: ["非敬语疑问", "반말 질문", "casual question"],
		phap: ["敬语", "높임", "formal"],
		phey: ["半敬语", "해요체", "polite"],
		pban: ["非敬语", "반말", "casual"],
		pplain: ["书面", "해라체", "plain"],
		pqhap: ["敬语疑问", "높임 질문", "formal question"],
		pqplain: ["非敬语疑问", "반말 질문", "casual question"],
		fhap: ["敬语", "높임", "formal"],
		fhey: ["半敬语", "해요체", "polite"],
		fban: ["非敬语", "반말", "casual"],
		fplain: ["书面", "해라체", "plain"],
		ghap: ["敬语推测", "높임 추측", "formal guess"],
		ghey: ["半敬语推测", "해요 추측", "polite guess"],
		gban: ["非敬语推测", "반말 추측", "casual guess"],
		gplain: ["书面推测", "해라 추측", "plain guess"],
		lge: ["半敬语承诺", "해요 약속", "polite promise"],
		lkk: ["半敬语商量", "해요 의향", "polite suggestion"],
		prh: ["敬语", "높임", "formal"],
		pry: ["半敬语", "해요체", "polite"],
		prb: ["非敬语", "반말", "casual"],
		prp: ["书面", "해라체", "plain"],
		prpy: ["半敬语过去", "해요 과거", "polite past"],
		prph: ["敬语过去", "높임 과거", "formal past"],
		prpp: ["书面过去", "해라 과거", "plain past"],
		sip: ["敬语", "높임", "formal"],
		seyo: ["半敬语", "해요체", "polite"],
		era: ["书面", "해라체", "plain"],
		cban: ["非敬语", "반말", "casual"],
		jima: ["半敬语禁止", "해요 금지", "polite don't"],
		jimah: ["敬语禁止", "높임 금지", "formal don't"],
		bsida: ["敬语", "높임", "formal"],
		ja: ["非敬语", "반말", "casual"],
		any: ["半敬语否定", "해요 부정", "polite negative"],
		anp: ["半敬语否定过去", "해요 부정 과거", "polite negative past"],
		anhy: ["半敬语长否定", "해요 긴 부정", "polite long negative"],
		anhh: ["敬语长否定", "높임 긴 부정", "formal long negative"],
		anhpy: ["半敬语长否过去", "해요 긴 부정 과거", "polite long negative past"],
		anhpp: ["书面长否定", "해라 긴 부정", "plain long negative"],
		moty: ["半敬语不能", "해요 못", "polite can't"],
		jimot: ["半敬语长不能", "해요 긴 못", "polite long can't"],
		honh: ["敬语", "높임", "formal"],
		hony: ["半敬语", "해요체", "polite"],
		honpy: ["半敬语过去", "해요 과거", "polite past"],
		honph: ["敬语过去", "높임 과거", "formal past"],
		honpp: ["书面过去", "해라 과거", "plain past"],
		honpl: ["书面", "해라체", "plain"],
		honf: ["半敬语将来", "해요 미래", "polite future"],
		go: ["并列", "나열", "and"],
		ase: ["然后", "순서", "and then"],
		myeon: ["条件", "조건", "if"],
		jiman: ["转折", "대조", "but"],
		nikka: ["原因", "이유", "because"],
		ryeo: ["打算", "의도", "intend"],
		wanty: ["半敬语想", "해요 싶다", "polite want"],
		wanth: ["敬语想", "높임 싶다", "formal want"],
		wantb: ["非敬语想", "반말 싶다", "casual want"],
		ayay: ["半敬语必须", "해요 해야", "polite must"],
		aya: ["书面必须", "해라 해야", "plain must"],
		suy: ["半敬语可以", "해요 가능", "polite can"],
		su: ["书面可以", "해라 가능", "plain can"],
		sun: ["半敬语不行", "해요 불가", "polite cannot"],
		ado: ["让步", "양보", "even if"],
		modn: ["现在", "현재", "present"],
		modp: ["过去", "과거", "past"],
		modf: ["将来", "미래", "future"],
		deon: ["回想", "회상", "retrospective"],
		atd: ["经历", "경험", "experienced"],
		neyo: ["半敬语感叹", "해요 감탄", "polite reaction"],
		atne: ["过去感叹", "과거 감탄", "past reaction"],
		jan: ["半敬语提醒", "해요 상기", "polite reminder"],
		jyo: ["半敬语确认", "해요 확인", "polite confirm"],
		jiyo: ["半敬语详述", "해요 지요", "polite full confirm"],
		hyeo: ["书面连接", "문어 연결", "written link"],
		hyeot: ["书面过去", "문어 과거", "written past"],
		hyeos: ["书面然后", "문어 순서", "written then"],
		ji: ["中顿", "중지", "pause"],
		qyes: ["判断疑问", "판단 의문", "yes-no question"],
		qwh: ["疑问词疑问", "의문사 의문", "wh-question"],
		qlight: ["轻问", "가벼운 의문", "light question"],
		cpast: ["确认过去", "확인 과거", "confirming past"],
		fcmd: ["敬语命令", "높임 명령", "formal command"],
		cmd: ["命令", "명령", "command"],
		cfut: ["非敬语将来", "반말 미래", "casual future"],
		must: ["必须", "필수", "must"],
		proh: ["禁止", "금지", "prohibition"],
		peq: ["平语", "평대", "familiar"],
		peqp: ["平语过去", "평대 과거", "familiar past"],
		sug: ["劝诱", "청유", "suggestion"]
	};
	var PICKG = {
		common: ["常见", "자주", "Common"],
		irr: ["特殊变化", "불규칙", "Irregular"]
	};
	var PICKS = [
		["common", "먹다", "吃", "", "eat"],
		["common", "가다", "去", "", "go"],
		["common", "오다", "来", "", "come"],
		["common", "하다", "做", "", "do"],
		["common", "되다", "成为", "", "become"],
		["common", "보다", "看", "", "see"],
		["common", "주다", "给", "", "give"],
		["common", "사다", "买", "", "buy"],
		["common", "만나다", "见面", "", "meet"],
		["common", "읽다", "读", "", "read"],
		["common", "알다", "知道", "", "know"],
		["common", "살다", "住", "", "live"],
		["common", "만들다", "制作", "", "make"],
		["common", "마시다", "喝", "", "drink"],
		["common", "배우다", "学习", "", "learn"],
		["common", "기다리다", "等待", "", "wait"],
		["common", "공부하다", "学习", "", "study"],
		["common", "좋아하다", "喜欢", "", "like"],
		["common", "일하다", "工作", "", "work"],
		["common", "닫다", "关", "", "close"],
		["common", "믿다", "相信", "", "believe"],
		["common", "잡다", "抓", "", "catch"],
		["common", "웃다", "笑", "", "laugh"],
		["irr", "듣다", "听 · ㄷ", "ㄷ", "listen · ㄷ"],
		["irr", "걷다", "走 · ㄷ", "ㄷ", "walk · ㄷ"],
		["irr", "묻다", "问 · ㄷ", "ㄷ", "ask · ㄷ"],
		["irr", "돕다", "帮 · ㅂ", "ㅂ", "help · ㅂ"],
		["irr", "춥다", "冷 · ㅂ", "ㅂ", "cold · ㅂ"],
		["irr", "짓다", "建 · ㅅ", "ㅅ", "build · ㅅ"],
		["irr", "낫다", "愈 · ㅅ", "ㅅ", "recover · ㅅ"],
		["irr", "그렇다", "那样 · ㅎ", "ㅎ", "so · ㅎ"],
		["irr", "파랗다", "蓝 · ㅎ", "ㅎ", "blue · ㅎ"],
		["irr", "어떻다", "怎样 · ㅎ", "ㅎ", "how · ㅎ"],
		["irr", "모르다", "不知 · 르", "르", "not know · 르"],
		["irr", "부르다", "叫 · 르", "르", "call · 르"],
		["irr", "빠르다", "快 · 르", "르", "fast · 르"],
		["irr", "따르다", "跟随 · ㅡ", "ㅡ", "follow · ㅡ"],
		["irr", "쓰다", "写 · ㅡ", "ㅡ", "write · ㅡ"],
		["irr", "예쁘다", "漂亮 · ㅡ", "ㅡ", "pretty · ㅡ"],
		["irr", "아프다", "疼 · ㅡ", "ㅡ", "hurt · ㅡ"],
		["irr", "모으다", "收集 · ㅡ", "ㅡ", "gather · ㅡ"],
		["irr", "푸르다", "青绿 · 러", "러", "green · 러"],
		["irr", "놓다", "放", "", "put"],
		["irr", "가시다", "去 · 敬语", "높임", "go · honorific"],
		["irr", "계시다", "在 · 敬语", "높임", "stay · honorific"],
		["irr", "있다", "有", "", "exist"],
		["irr", "없다", "没有", "", "not exist"],
		["irr", "행복하다", "幸福 · 하다", "하다", "happy · 하다"],
		["irr", "학생이다", "是学生", "", "be a student"],
		["irr", "아니다", "不是", "", "not be"]
	];
	var clip = null;

	if (exp) {
		exp.build = build;
		exp.picks = PICKS;
		return;
	}
	if (typeof SK === "undefined" || !SK.regutil) return;

	SK.regutil("u-kverb", {
		title: "韩语动词变化",
		go: "显示",
		live: true,
		own: true,
		noGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open,
		relang: open,
		run: run
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function body(){
		return '<p class="hint" id="kv-hint"></p>'
			+ '<div class="row" id="kv-row">'
			+ '<label id="kv-word">' + SK.combo("kv-in", "먹다", "词典形，如 먹다、하다、예쁘다") + "</label>"
			+ '<label id="kv-type"><select id="kv-sel">'
			+ '<option value="auto"></option>'
			+ '<option value="v"></option>'
			+ '<option value="a"></option>'
			+ "</select></label>"
			+ "</div>"
			+ '<div id="kv-out"></div>';
	}

	function open(){
		var hint = F.id("kv-hint");
		if (hint) {
			F.seta(hint, "data-k", "web.kverb.hint");
			hint.textContent = tx("web.kverb.hint", "输入词典形（以 다 结尾）。自动区分动词和形容词，也可以改类型。每条旁边的喇叭听发音。걷다 按「走」，묻다 按「问」。名词加 이다（如 학생이다）按词尾变化。");
		}
		var title = F.id("box-title");
		if (title) title.textContent = tx("web.tool.kverb", "韩语动词变化");
		setlab("kv-word", "web.kverb.word", "动词");
		setlab("kv-type", "web.kverb.type", "类型");
		SK.combofill("kv-in", pickgroups());
		setopt("auto", "web.kverb.auto", "自动");
		setopt("v", "web.kverb.verb", "动词");
		setopt("a", "web.kverb.adj", "形容词");
		var box = F.id("kv-in");
		if (box) {
			F.seta(box, "data-kph", "web.kverb.ph");
			box.placeholder = tx("web.kverb.ph", "词典形，如 먹다、하다、예쁘다");
		}
		var out = F.id("kv-out");
		if (out && !F.attr(out, "data-bind")) {
			F.seta(out, "data-bind", "1");
			out.onclick = onout;
		}
		run();
	}

	function pickgroups(){
		var lang = langcode();
		var groups = [];
		var cur = null;
		var i, row, gloss, name;
		for (i = 0; i < PICKS.length; i++) {
			row = PICKS[i];
			name = lp(lang, PICKG[row[0]]);
			if (!cur || cur.g !== name) {
				cur = { g: name, items: [] };
				groups.push(cur);
			}
			gloss = lang === "zh" ? row[2] : lang === "ko" ? row[3] : row[4];
			cur.items.push({ v: row[1], t: gloss ? row[1] + "  " + gloss : row[1] });
		}
		return groups;
	}

	function onout(ev){
		var btn = ev.target;
		var out = F.id("kv-out");
		while (btn && btn !== out && btn.tagName !== "BUTTON") btn = btn.parentNode;
		if (!btn || btn === out) return;
		var text = F.attr(btn, "data-v") || "";
		if (text) speak(text);
	}

	function speak(text){
		SK.stopwav();
		if (clip) {
			try { clip.pause(); } catch (e) {}
			clip = null;
		}
		var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=ko");
		clip = audio;
		audio.onerror = function(){
			if (clip === audio) SK.msg("box-msg", tx("web.kverb.nospeak", "无法发音"), true);
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (clip === audio) SK.msg("box-msg", tx("web.kverb.nospeak", "无法发音"), true);
		});
	}

	function paint(word, note, rows){
		var out = F.id("kv-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; return; }
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(word) + "</b><span>" + esc(note) + "</span></div><table class=\"jv-tab\">";
		var g = "";
		var buf = [];
		var i;
		for (i = 0; i < rows.length; i++) {
			if (rows[i].g !== g) {
				html += flush(buf, tip);
				buf = [];
				g = rows[i].g;
				html += '<tr><td class="kv-g" colspan="4">' + esc(g) + "</td></tr>";
			}
			buf.push(rows[i]);
		}
		html += flush(buf, tip) + "</table>";
		out.innerHTML = html;
	}

	function flush(buf, tip){
		var html = "";
		var j;
		for (j = 0; j < buf.length; j += 2) {
			html += "<tr>" + cell(buf[j], tip);
			html += j + 1 < buf.length ? cell(buf[j + 1], tip) : '<td class="jv-k"></td><td class="jv-v"></td>';
			html += "</tr>";
		}
		return html;
	}

	function cell(item, tip){
		return '<td class="jv-k">' + esc(item.name) + '</td><td class="jv-v"><span>' + esc(item.text) + "</span>"
			+ '<button type="button" class="jv-spk" data-v="' + esc(item.text) + '" title="' + tip + '"><i class="fa-solid fa-volume-high"></i></button></td>';
	}

	function esc(s){
		return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	function setlab(id, key, fb){
		var el = F.id(id);
		if (!el) return;
		var text = tx(key, fb);
		if (el.childNodes.length && el.childNodes[0].nodeType === 3) el.childNodes[0].nodeValue = text;
		else el.insertBefore(document.createTextNode(text), el.firstChild);
	}

	function setopt(value, key, fb){
		var sel = F.id("kv-sel");
		if (!sel) return;
		var i;
		for (i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === value) sel.options[i].textContent = tx(key, fb);
		}
	}

	function run(){
		var box = F.id("kv-in");
		var sel = F.id("kv-sel");
		var word = norm(box ? box.value : "");
		var force = sel ? sel.value : "auto";
		if (!word) {
			paint("", "", []);
			SK.msg("box-msg", "");
			return;
		}
		var built = build(word, force, langcode());
		if (!built.ok) {
			paint("", "", []);
			SK.msg("box-msg", built.err, true);
			return;
		}
		paint(built.word, built.note, built.rows);
		SK.msg("box-msg", "");
	}

	function langcode(){
		var sel = F.id("lang");
		return sel && sel.value ? sel.value : "zh";
	}

	function ci(ch){ return CHO.indexOf(ch); }
	function vi(ch){ return JUNG.indexOf(ch); }
	function zi(ch){ return JONG.indexOf(ch); }

	function sy(ch){
		var c = ch.charCodeAt(0) - 0xAC00;
		return { cho: Math.floor(c / 588), jung: Math.floor((c % 588) / 28), jong: c % 28 };
	}

	function chOf(o){
		return String.fromCharCode(0xAC00 + (o.cho * 21 + o.jung) * 28 + o.jong);
	}

	function cp(a){
		var o = [];
		var i;
		for (i = 0; i < a.length; i++) o.push({ cho: a[i].cho, jung: a[i].jung, jong: a[i].jong });
		return o;
	}

	function jn(a){
		var s = "";
		var i;
		for (i = 0; i < a.length; i++) s += chOf(a[i]);
		return s;
	}

	function decAll(s){
		var o = [];
		var i, c;
		for (i = 0; i < s.length; i++) {
			c = s.charCodeAt(i);
			if (c < 0xAC00 || c > 0xD7A3) return null;
			c -= 0xAC00;
			o.push({ cho: Math.floor(c / 588), jung: Math.floor((c % 588) / 28), jong: c % 28 });
		}
		return o;
	}

	function isAh(v){
		return v === A || v === YA || v === O || v === YO || v === WA;
	}

	function endsSyl(syls, cho, jung, jong){
		var last = syls[syls.length - 1];
		return last.cho === cho && last.jung === jung && last.jong === jong;
	}

	function isHada(syls){
		return endsSyl(syls, HIEUT, A, 0);
	}

	function isEu(syls){
		var last = syls[syls.length - 1];
		return last.jung === EU && last.jong === 0;
	}

	function isReu(syls){
		return syls.length >= 2 && endsSyl(syls, RIEULC, EU, 0);
	}

	function pair(syls, cho, jung){
		if (syls.length < 2) return false;
		var a = syls[syls.length - 2];
		return endsSyl(syls, RIEULC, EU, 0) && a.cho === cho && a.jung === jung && a.jong === 0;
	}

	function isReo(syls){
		return pair(syls, PIEUPC, U) || pair(syls, NIEUNC, O);
	}

	function isReuEx(syls){
		return pair(syls, TTIEUT, A) || pair(syls, CHIEUT, I);
	}

	function endsRop(syls){
		return endsSyl(syls, RIEULC, O, J_B);
	}

	function isAutoH(syls){
		if (syls.length < 2) return false;
		var last = syls[syls.length - 1];
		if (last.jong !== J_H) return false;
		return last.jung === A || last.jung === EO || last.jung === YA || last.jung === YEO;
	}

	function isSi(syls){
		if (!endsSyl(syls, SIOT, I, 0)) return false;
		if (syls.length >= 2) {
			var p = syls[syls.length - 2];
			if (p.cho === MIEUM && p.jung === A && p.jong === 0) return false;
		}
		return true;
	}

	function isYi(syl){
		return syl.cho === IEUNG && syl.jung === I && syl.jong === 0;
	}

	function classify(stem, syls){
		if (stem === "놓") return "noa";
		if (isHada(syls)) return "hada";
		if (isReo(syls)) return "reo";
		if (isReu(syls) && !isReuEx(syls)) return "reu";
		if (isEu(syls)) return "eu";
		if (IRR_D[stem]) return "d";
		if (IRR_B[stem] || endsRop(syls)) return "b";
		if (IRR_S[stem]) return "s";
		if (IRR_H[stem] || isAutoH(syls)) return "h";
		if (isSi(syls)) return "si";
		return "reg";
	}

	function endsIss(syls){ return endsSyl(syls, IEUNG, I, J_SS); }
	function endsEobs(syls){ return endsSyl(syls, IEUNG, EO, J_BS); }

	function decideKind(word, stem, syls, irr, force){
		if (endsIss(syls)) return "itda";
		if (force === "v") return "v";
		if (force === "a") return "a";
		if (ADJ[word] || endsEobs(syls)) return "a";
		if (irr === "h" || irr === "reo") return "a";
		if (irr === "b" && !BVERB[stem]) return "a";
		return "v";
	}

	function isCopula(word){
		if (VERB_IDA[word]) return false;
		if (word === "이다") return true;
		if (word.length < 3) return false;
		return isYi(sy(word.charAt(word.length - 2)));
	}

	function ae(syls, irr){
		var s, last, jung;
		if (irr === "hada") {
			s = cp(syls);
			s[s.length - 1].jung = AE;
			return jn(s);
		}
		if (irr === "noa") {
			s = cp(syls);
			s[s.length - 1].jong = 0;
			s[s.length - 1].jung = WA;
			return jn(s);
		}
		if (irr === "reo") {
			s = cp(syls);
			s.push(sy("러"));
			return jn(s);
		}
		if (irr === "reu") return aeReu(syls);
		if (irr === "eu") return aeEu(syls);
		if (irr === "d") {
			s = cp(syls);
			s[s.length - 1].jong = J_R;
			return addAe(s);
		}
		if (irr === "b") {
			s = cp(syls);
			last = s[s.length - 1];
			jung = last.jung;
			last.jong = 0;
			s.push(sy(jung === O ? "와" : "워"));
			return jn(s);
		}
		if (irr === "s") {
			s = cp(syls);
			s[s.length - 1].jong = 0;
			s.push(sy(isAh(s[s.length - 1].jung) ? "아" : "어"));
			return jn(s);
		}
		if (irr === "h") {
			s = cp(syls);
			last = s[s.length - 1];
			last.jong = 0;
			if (last.jung === A || last.jung === EO) last.jung = AE;
			else if (last.jung === YA) last.jung = YAE;
			else if (last.jung === YEO) last.jung = YE;
			return jn(s);
		}
		return addAe(cp(syls));
	}

	function addAe(s){
		var last = s[s.length - 1];
		if (last.jong !== 0) {
			s.push(sy(isAh(last.jung) ? "아" : "어"));
			return jn(s);
		}
		return addOpen(s);
	}

	function addOpen(s){
		var last = s[s.length - 1];
		var v = last.jung;
		if (v === A || v === AE || v === YA || v === EO || v === E || v === YEO || v === YAE || v === YE || v === WA || v === WEO || v === WAE)
			return jn(s);
		if (v === O) { last.jung = WA; return jn(s); }
		if (v === U) { last.jung = WEO; return jn(s); }
		if (v === OE) { last.jung = WAE; return jn(s); }
		if (v === I) { last.jung = YEO; return jn(s); }
		s.push(sy(isAh(v) ? "아" : "어"));
		return jn(s);
	}

	function aeEu(syls){
		var s = cp(syls);
		var i = s.length - 1;
		var prev = i > 0 ? s[i - 1] : null;
		var yang = prev && (prev.jung === A || prev.jung === O);
		s[i].jung = yang ? A : EO;
		return jn(s);
	}

	function aeReu(syls){
		var s = cp(syls);
		var prev = s[s.length - 2];
		var yang = prev.jung === A || prev.jung === O;
		if (prev.jong === 0) prev.jong = J_R;
		s[s.length - 1].jung = yang ? A : EO;
		s[s.length - 1].jong = 0;
		return jn(s);
	}

	function spokenAe(syls, irr, yo){
		if (irr !== "si") return ae(syls, irr);
		var s = cp(syls);
		s[s.length - 1].jung = yo ? E : YEO;
		return jn(s);
	}

	function pastOf(ba){
		var s = decAll(ba);
		s[s.length - 1].jong = J_SS;
		return jn(s);
	}

	function pastStem(syls, irr, ba){
		if (irr !== "si") return pastOf(ba);
		var s = cp(syls);
		s[s.length - 1].jung = YEO;
		s[s.length - 1].jong = J_SS;
		return jn(s);
	}

	function euStem(syls, irr){
		var s = cp(syls);
		var last = s[s.length - 1];
		if (irr === "d") {
			last.jong = J_R;
			return { s: s, eu: true, r: false };
		}
		if (irr === "b") {
			last.jong = 0;
			s.push(sy("우"));
			return { s: s, eu: false, r: false };
		}
		if (irr === "s") {
			last.jong = 0;
			return { s: s, eu: true, r: false };
		}
		if (irr === "h") {
			last.jong = 0;
			return { s: s, eu: false, r: false };
		}
		if (last.jong === 0) return { s: s, eu: false, r: false };
		if (last.jong === J_R) return { s: s, eu: false, r: true };
		return { s: s, eu: true, r: false };
	}

	function applyDrop(st, drop){
		if (!(drop && st.r)) return st;
		var s = cp(st.s);
		s[s.length - 1].jong = 0;
		return { s: s, eu: st.eu, r: false };
	}

	function plusEu(st, tail, drop){
		st = applyDrop(st, drop);
		return jn(st.s) + (st.eu ? "으" : "") + tail;
	}

	function hapsho(syls){
		var s = cp(syls);
		var last = s[s.length - 1];
		if (last.jong === 0 || last.jong === J_R) {
			last.jong = J_B;
			return jn(s) + "니다";
		}
		return jn(s) + "습니다";
	}

	function neunda(syls, plainDa, word){
		if (plainDa) return word;
		var s = cp(syls);
		var last = s[s.length - 1];
		if (last.jong === 0 || last.jong === J_R) {
			if (last.jong === J_R) last.jong = 0;
			last.jong = J_N;
			return jn(s) + "다";
		}
		return jn(s) + "는다";
	}

	function rieulWord(syls, irr){
		var st = euStem(syls, irr);
		if (st.eu) return jn(st.s) + "을";
		var s = cp(st.s);
		s[s.length - 1].jong = J_R;
		return jn(s);
	}

	function nieunWord(syls, irr){
		var st = euStem(syls, irr);
		if (st.eu) return jn(st.s) + "은";
		st = applyDrop(st, true);
		var s = cp(st.s);
		s[s.length - 1].jong = J_N;
		return jn(s);
	}

	function neun(syls){
		var s = cp(syls);
		if (s[s.length - 1].jong === J_R) s[s.length - 1].jong = 0;
		return jn(s) + "는";
	}

	function dropR(syls, tail){
		var s = cp(syls);
		if (s[s.length - 1].jong === J_R) s[s.length - 1].jong = 0;
		return jn(s) + tail;
	}

	function qPlain(syls, irr, kind){
		if (kind === "a" && !endsEobs(syls)) return plusEu(euStem(syls, irr), "냐", true);
		return dropR(syls, "느냐");
	}

	function seyoForm(syls, irr){
		if (irr === "si") return spokenAe(syls, irr, true) + "요";
		return plusEu(euStem(syls, irr), "세요", true);
	}

	function sipnio(syls, irr){
		if (irr === "si") {
			var s = cp(syls);
			s.pop();
			return jn(s) + "십시오";
		}
		return plusEu(euStem(syls, irr), "십시오", true);
	}

	function bsida(syls, irr){
		var s;
		if (irr === "si") {
			s = cp(syls);
			s[s.length - 1].jong = J_B;
			return jn(s) + "시다";
		}
		var st = applyDrop(euStem(syls, irr), true);
		if (st.eu) return jn(st.s) + "읍시다";
		s = cp(st.s);
		s[s.length - 1].jong = J_B;
		return jn(s) + "시다";
	}

	function hon(syls, irr){
		if (jn(syls) === "있") return decAll("계시");
		if (irr === "hada") {
			var s = cp(syls);
			s.push(sy("시"));
			return s;
		}
		var st = applyDrop(euStem(syls, irr), true);
		var out = cp(st.s);
		if (st.eu) out.push(sy("으"));
		out.push(sy("시"));
		return out;
	}

	function honHey(hs){
		var s = cp(hs);
		s[s.length - 1].jung = E;
		return jn(s) + "요";
	}

	function honAt(hs){
		var s = cp(hs);
		s[s.length - 1].jung = YEO;
		s[s.length - 1].jong = J_SS;
		return jn(s);
	}

	function honHap(hs){
		var s = cp(hs);
		s[s.length - 1].jong = J_B;
		return jn(s) + "니다";
	}

	function honPlain(hs){
		var s = cp(hs);
		s[s.length - 1].jong = J_N;
		return jn(s) + "다";
	}

	function honFut(hs){
		var s = cp(hs);
		s[s.length - 1].jong = J_R;
		return jn(s);
	}

	function splitHa(stem, irr){
		if (irr !== "hada" || NOSPLIT[stem]) return "";
		var syls = decAll(stem);
		if (!syls || syls.length < 2) return "";
		var prev = syls[syls.length - 2];
		if (prev.cho === IEUNG && prev.jong === 0 && (prev.jung === A || prev.jung === EO || prev.jung === WA || prev.jung === WEO || prev.jung === YEO))
			return "";
		return stem.slice(0, -1);
	}

	function wrapNeg(stem, irr, full, mark){
		var head = splitHa(stem, irr);
		if (!head || full.indexOf(head) !== 0) return mark + " " + full;
		return head + " " + mark + " " + full.slice(head.length);
	}

	function lp(lang, row){
		var i = lang === "zh" ? 0 : lang === "ko" ? 1 : 2;
		return row[i];
	}

	function rowName(id, name, lang){
		var lab = LBL[id];
		return lab ? lp(lang, lab) : name;
	}

	function noteOf(kind, irr, stem, lang){
		var k = lp(lang, NOTE[kind] || NOTE.v);
		var tag = "";
		if (stem === "걷") tag = lp(lang, NOTE.geot);
		else if (stem === "묻") tag = lp(lang, NOTE.mut);
		else if (irr && irr !== "reg" && NOTE[irr]) tag = lp(lang, NOTE[irr]);
		return tag ? k + " · " + tag : k;
	}

	function norm(s){
		s = String(s || "").replace(/^\s+|\s+$/g, "");
		var i = s.search(/\s/);
		if (i >= 0) s = s.slice(0, i);
		return s.replace(/[.?。．!！,，]+$/g, "");
	}

	function build(word, force, lang){
		word = norm(word);
		lang = lang || "zh";
		force = force || "auto";
		if (!word) return { ok: true, rows: [], note: "", word: "", kind: "", irr: "" };
		var all = decAll(word);
		if (!all || word.charAt(word.length - 1) !== "다" || all.length < 2)
			return { ok: false, err: lp(lang, NOTE.bad), rows: [] };
		if (word === "아니다") return pack(word, "anida", "", anidaRows(word, lang), lang);
		if (word === "이다" || (isCopula(word) && force !== "v"))
			return pack(word, "ida", "", copulaRows(word, lang), lang);
		var stem = word.slice(0, -1);
		var syls = all.slice(0, -1);
		var irr = classify(stem, syls);
		var kind = decideKind(word, stem, syls, irr, force);
		return pack(word, kind, irr, verbRows(word, stem, syls, irr, kind, lang), lang);
	}

	function pack(word, kind, irr, rows, lang){
		var stem = word === "이다" || word === "아니다" ? "" : word.slice(0, -1);
		if (kind === "ida" || kind === "anida") stem = "";
		return { ok: true, word: word, kind: kind, irr: irr, note: noteOf(kind, irr, stem, lang), rows: rows };
	}

	function verbRows(word, stem, syls, irr, kind, lang){
		var g = function(key){ return lp(lang, GRP[key]); };
		var rows = [];
		var cur = "";
		function add(id, name, text){
			if (!text) return;
			rows.push({ id: id, g: cur, name: rowName(id, name, lang), text: text });
		}
		var yo = spokenAe(syls, irr, true);
		var ba = spokenAe(syls, irr, false);
		var hey = yo + "요";
		var past = pastStem(syls, irr, ba);
		var st = euStem(syls, irr);
		var plainDa = kind === "a" || kind === "itda" || stem === "계시";
		var verb = kind === "v";
		var adj = kind === "a";
		var rl = rieulWord(syls, irr);
		var eobs = endsEobs(syls);
		var hs = irr === "si" ? null : hon(syls, irr);
		cur = g("base");
		add("dict", lp(lang, NOTE.dict), word);
		cur = g("now");
		add("hap", "합니다", hapsho(syls));
		add("hey", "해요", hey);
		add("ban", "해", ba);
		add("plain", plainDa ? "다" : "는다", neunda(syls, plainDa, word));
		add("qhap", "합니까", dropR(syls, "습니까"));
		add("qplain", adj && !eobs ? "냐" : "느냐", qPlain(syls, irr, kind));
		cur = g("past");
		add("phap", "했습니다", past + "습니다");
		add("phey", "했어요", past + "어요");
		add("pban", "했어", past + "어");
		add("pplain", "했다", past + "다");
		add("pqhap", "했습니까", past + "습니까");
		add("pqplain", "했느냐", past + "느냐");
		cur = g("fut");
		add("fhap", "ㄹ 겁니다", rl + " 겁니다");
		add("fhey", "ㄹ 거예요", rl + " 거예요");
		add("fban", "ㄹ 거야", rl + " 거야");
		add("fplain", "ㄹ 것이다", rl + " 것이다");
		add("ghap", "겠습니다", jn(syls) + "겠습니다");
		add("ghey", "겠어요", jn(syls) + "겠어요");
		add("gban", "겠어", jn(syls) + "겠어");
		add("gplain", "겠다", jn(syls) + "겠다");
		if (verb) add("lge", "ㄹ게요", rl + "게요");
		add("lkk", "ㄹ까요", rl + "까요");
		if (verb) {
			cur = g("prog");
			add("prh", "고 있습니다", jn(syls) + "고 있습니다");
			add("pry", "고 있어요", jn(syls) + "고 있어요");
			add("prb", "고 있어", jn(syls) + "고 있어");
			add("prp", "고 있다", jn(syls) + "고 있다");
			add("prpy", "고 있었어요", jn(syls) + "고 있었어요");
			add("prph", "고 있었습니다", jn(syls) + "고 있었습니다");
			add("prpp", "고 있었다", jn(syls) + "고 있었다");
			cur = g("cmd");
			add("sip", "십시오", sipnio(syls, irr));
			add("seyo", "세요", seyoForm(syls, irr));
			add("era", "어라", ba + "라");
			add("cban", "해", ba);
			add("jima", "지 마세요", jn(syls) + "지 마세요");
			add("jimah", "지 마십시오", jn(syls) + "지 마십시오");
			cur = g("prop");
			add("bsida", "ㅂ시다", bsida(syls, irr));
			add("ja", "자", jn(syls) + "자");
		}
		cur = g("neg");
		add("any", "안 해요", wrapNeg(stem, irr, hey, "안"));
		add("anp", "안 했어요", wrapNeg(stem, irr, past + "어요", "안"));
		add("anhy", "지 않아요", jn(syls) + "지 않아요");
		add("anhh", "지 않습니다", jn(syls) + "지 않습니다");
		add("anhpy", "지 않았어요", jn(syls) + "지 않았어요");
		add("anhpp", "지 않았다", jn(syls) + "지 않았다");
		if (verb) {
			add("moty", "못 해요", wrapNeg(stem, irr, hey, "못"));
			add("jimot", "지 못해요", jn(syls) + "지 못해요");
		}
		if (hs) {
			var hat = honAt(hs);
			cur = g("hon");
			add("honh", "십니다", honHap(hs));
			add("hony", "세요", honHey(hs));
			add("honpy", "셨어요", hat + "어요");
			add("honph", "셨습니다", hat + "습니다");
			add("honpp", "셨다", hat + "다");
			add("honpl", "신다", honPlain(hs));
			add("honf", "실 거예요", honFut(hs) + " 거예요");
		}
		cur = g("con");
		add("go", "고", jn(syls) + "고");
		add("ase", "아서", ba + "서");
		add("myeon", "면", plusEu(st, "면", false));
		add("jiman", "지만", jn(syls) + "지만");
		add("nikka", "니까", plusEu(st, "니까", true));
		if (verb) {
			add("ryeo", "려고", plusEu(st, "려고", false));
			add("wanty", "고 싶어요", jn(syls) + "고 싶어요");
			add("wanth", "고 싶습니다", jn(syls) + "고 싶습니다");
			add("wantb", "고 싶어", jn(syls) + "고 싶어");
		}
		add("ayay", "아야 해요", ba + "야 해요");
		add("aya", "아야 하다", ba + "야 하다");
		add("suy", "ㄹ 수 있어요", rl + " 수 있어요");
		add("su", "ㄹ 수 있다", rl + " 수 있다");
		add("sun", "ㄹ 수 없어요", rl + " 수 없어요");
		add("ado", "아도", ba + "도");
		cur = g("mod");
		if (adj && !eobs) add("modn", "ㄴ", nieunWord(syls, irr));
		else add("modn", "는", neun(syls));
		if (verb) add("modp", "ㄴ", nieunWord(syls, irr));
		add("modf", "ㄹ", rl);
		add("deon", "던", jn(syls) + "던");
		add("atd", "았던", past + "던");
		cur = g("ext");
		add("neyo", "네요", dropR(syls, "네요"));
		add("atne", "았네요", past + "네요");
		add("jan", "잖아요", dropR(syls, "잖아요"));
		add("jyo", "죠", jn(syls) + "죠");
		add("jiyo", "지요", jn(syls) + "지요");
		if (irr === "hada") {
			var hy = stem.slice(0, -1) + "하여";
			cur = g("wrt");
			add("hyeo", "하여", hy);
			add("hyeot", "하였다", stem.slice(0, -1) + "하였다");
			add("hyeos", "하여서", hy + "서");
		}
		// 方言：与标准语不同的常用说法，每个方言一组
		var dia = dialectRows(syls, irr, kind, {
			hey: hey, ba: ba, past: past, st: st, rl: rl
		}, lang);
		for (var di = 0; di < dia.length; di++) rows.push(dia[di]);
		return rows;
	}

	// -ㅂ네다 / -읍네다 这类以 ㅂ 结尾的方言尾：开音节加 ㅂ，闭音节加 읍
	function hapN(syls, tail){
		var s = cp(syls);
		var last = s[s.length - 1];
		if (last.jong === 0 || last.jong === J_R) {
			last.jong = J_B;
			return jn(s) + tail;
		}
		return jn(s) + "읍" + tail;
	}

	// 济州语 -수다 / -수꽈：动词接 해체形（먹어수다、가수다），形容词和 있다 接词干（좋수다）
	function jejuBase(syls, kind, irr, ba){
		if (kind === "v" && irr !== "hada") return ba;
		return jn(syls);
	}

	// 把标准语 의문형 -느냐/-냐 换成方言的 -나/-노/-냐
	function qdial(q, end){
		return q.replace(/느냐$/, end).replace(/냐$/, end);
	}

	function dialectRows(syls, irr, kind, f, lang){
		var rows = [];
		var g = function(id){ return lp(lang, GRP[id]); };
		function add(gid, key, text){
			if (text) rows.push({ id: "dia." + gid + "." + key, g: g(gid), name: rowName(key, key, lang), text: text });
		}
		var q = qPlain(syls, irr, kind);
		var haps = hapsho(syls);
		// 경상도：해요체 -예、합쇼체 -ㅁ니더、판단 -나 / 의문사 -노
		add("gs", "hey", f.hey.replace(/요$/, "예"));
		add("gs", "prpy", f.past + "어예");
		add("gs", "qyes", qdial(q, "나") + "?");
		add("gs", "qwh", qdial(q, "노") + "?");
		add("gs", "hap", haps.replace(/다$/, "더"));
		// 전라도：높임 -라우/-지라우、의문 -냐、确认 -당께、命令 -시소
		add("jl", "hey", f.ba + "라우");
		add("jl", "prph", f.past + "지라우");
		add("jl", "qplain", qdial(q, "냐") + "?");
		add("jl", "cpast", f.past + "당께");
		add("jl", "fcmd", plusEu(f.st, "시소", true));
		// 충청도：해요체 -유/-슈、의문 -냐、将来 -ㄹ겨、轻问 -남
		add("cc", "hey", f.hey.replace(/요$/, "유"));
		add("cc", "prpy", f.past + "슈");
		add("cc", "qplain", qdial(q, "냐") + "?");
		add("cc", "cfut", f.rl + "겨");
		add("cc", "qlight", f.past + "남?");
		// 제주：합쇼체 -수다/-수꽈、必须 -아/어사、禁止 -지 말앙
		var jj = jejuBase(syls, kind, irr, f.ba);
		add("jj", "hap", jj + "수다");
		add("jj", "qhap", jj + "수꽈?");
		add("jj", "must", f.ba + "사");
		add("jj", "proh", jn(syls) + "지 말앙");
		// 함경도：敬语过去 -슴다、平语过去 -슴메、确认过去 -지비、劝诱 -기요、敬语命令 -ㅂ소
		add("hg", "prph", f.past + "슴다");
		add("hg", "peqp", f.past + "슴메");
		add("hg", "cpast", f.past + "지비");
		add("hg", "sug", jn(syls) + "기요");
		add("hg", "fcmd", hapN(syls, "소"));
		// 평안도：敬语 -ㅂ네다/-ㅂ네까、劝诱 -ㅂ세다、命令 -라우、敬语过去 -수다
		add("pg", "hap", hapN(syls, "네다"));
		add("pg", "qhap", hapN(syls, "네까?"));
		add("pg", "sug", hapN(syls, "세다"));
		add("pg", "cmd", plusEu(f.st, "라우", true));
		add("pg", "prph", f.past + "수다");
		return rows;
	}

	function copArt(noun, bat, withI, open){
		if (!noun) return "이" + withI;
		return noun + (bat ? "이" + withI : open);
	}

	function copulaRows(word, lang){
		var g = function(key){ return lp(lang, GRP[key]); };
		var noun = word === "이다" ? "" : word.slice(0, -2);
		var bat = true;
		if (noun) {
			var last = decAll(noun);
			bat = !!(last && last[last.length - 1].jong);
		}
		var rows = [];
		var cur = "";
		function add(id, name, text){
			rows.push({ id: id, g: cur, name: rowName(id, name, lang), text: text });
		}
		cur = g("base");
		add("dict", lp(lang, NOTE.dict), word);
		cur = g("now");
		add("hap", "입니다", noun + "입니다");
		add("hey", "예요", copArt(noun, bat, "에요", "예요"));
		add("ban", "야", copArt(noun, bat, "야", "야"));
		add("plain", "이다", noun ? noun + "이다" : "이다");
		cur = g("past");
		add("phap", "었습니다", copArt(noun, bat, "었습니다", "였습니다"));
		add("phey", "었어요", copArt(noun, bat, "었어요", "였어요"));
		add("pban", "었어", copArt(noun, bat, "었어", "였어"));
		add("pplain", "었다", copArt(noun, bat, "었다", "였다"));
		cur = g("fut");
		add("fhey", "ㄹ 거예요", noun + "일 거예요");
		add("fhap", "ㄹ 겁니다", noun + "일 겁니다");
		add("fplain", "ㄹ 것이다", noun + "일 것이다");
		cur = g("con");
		add("myeon", "면", copArt(noun, bat, "면", "면"));
		add("go", "고", copArt(noun, bat, "고", "고"));
		add("jiman", "지만", copArt(noun, bat, "지만", "지만"));
		add("ji", "지", copArt(noun, bat, "지", "지"));
		cur = g("mod");
		add("modn", "ㄴ", noun + "인");
		add("modf", "ㄹ", noun + "일");
		add("deon", "던", copArt(noun, bat, "던", "던"));
		add("atd", "았던", copArt(noun, bat, "었던", "였던"));
		cur = g("hon");
		add("seyo", "세요", copArt(noun, bat, "세요", "세요"));
		add("honh", "십니다", noun + "이십니다");
		add("honpy", "셨어요", copArt(noun, bat, "셨어요", "셨어요"));
		cur = g("neg");
		add("any", "아니에요", noun ? noun + (bat ? "이" : "가") + " 아니에요" : "아니에요");
		return rows;
	}

	function anidaRows(word, lang){
		var g = function(key){ return lp(lang, GRP[key]); };
		var rows = [];
		var cur = g("base");
		function add(id, name, text){
			rows.push({ id: id, g: cur, name: rowName(id, name, lang), text: text });
		}
		add("dict", lp(lang, NOTE.dict), word);
		cur = g("now");
		add("hap", "입니다", "아닙니다");
		add("hey", "예요", "아니에요");
		add("ban", "야", "아니야");
		add("plain", "이다", "아니다");
		cur = g("past");
		add("phap", "었습니다", "아니었습니다");
		add("phey", "었어요", "아니었어요");
		add("pban", "었어", "아니었어");
		add("pplain", "었다", "아니었다");
		cur = g("fut");
		add("fhey", "ㄹ 거예요", "아닐 거예요");
		add("fhap", "ㄹ 겁니다", "아닐 겁니다");
		add("fplain", "ㄹ 것이다", "아닐 것이다");
		cur = g("con");
		add("myeon", "면", "아니면");
		add("go", "고", "아니고");
		add("jiman", "지만", "아니지만");
		add("ji", "지", "아니지");
		cur = g("mod");
		add("modn", "ㄴ", "아닌");
		add("modf", "ㄹ", "아닐");
		add("deon", "던", "아니던");
		add("atd", "았던", "아니었던");
		return rows;
	}
})();
