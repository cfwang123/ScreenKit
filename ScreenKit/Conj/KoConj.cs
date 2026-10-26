using System;
using System.Collections.Generic;
using System.Text;

namespace ScreenKit;

/// <summary>韩语动词 / 形容词 / 名词（이다）变形，移植自 Web/tools/u-kverb.js 的 build()。</summary>
static class KoConj {
	/// <summary>韩语音节：初声 / 中声 / 终声索引。</summary>
	private sealed class KoSyl {
		public int Cho;
		public int Jung;
		public int Jong;

		public KoSyl() { }

		public KoSyl(int cho, int jung, int jong) {
			Cho = cho;
			Jung = jung;
			Jong = jong;
		}
	}

	/// <summary>接「으」类词尾时的中间结果；R 表示 ㄹ 末音按需脱落。</summary>
	private sealed class KoStem {
		public List<KoSyl> S;
		public bool Eu;
		public bool R;
	}

	// 中声索引（ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ）
	private const int A = 0, AE = 1, YA = 2, YAE = 3, EO = 4, E = 5, YEO = 6, YE = 7, O = 8, WA = 9, WAE = 10;
	private const int OE = 11, YO = 12, U = 13, WEO = 14, WE = 15, WI = 16, YU = 17, EU = 18, YI = 19, I = 20;
	// 初声索引，只列用到的
	private const int CH_N = 2, CH_DD = 4, CH_R = 5, CH_M = 6, CH_S = 9, CH_NG = 11, CH_CH = 14, CH_P = 17, CH_H = 18;
	// 终声索引
	private const int JG_N = 4, JG_R = 8, JG_M = 16, JG_B = 17, JG_BS = 18, JG_SS = 20, JG_H = 27;

	// ==================== 数据表（与 Web/tools/u-kverb.js 保持一致） ====================
	private static readonly HashSet<string> IRR_D = new() {
		"걷", "깨닫", "듣", "묻", "싣", "일컫",
	};

	private static readonly HashSet<string> IRR_B = new() {
		"가볍", "고맙", "굽", "눕", "덥", "돕", "맵", "무겁", "무섭", "밉", "반갑", "아름답", "어렵", "쉽", "즐겁", "줍", "춥", "가깝", "귀엽",
		"더럽", "부드럽", "어둡", "부럽", "안타깝", "두껍", "뜨겁", "차갑", "외롭", "새롭", "괴롭", "그립", "곱", "아깝", "부끄럽",
	};

	private static readonly HashSet<string> BVERB = new() {
		"돕", "굽", "눕", "줍",
	};

	private static readonly HashSet<string> IRR_S = new() {
		"긋", "낫", "붓", "잇", "짓", "젓",
	};

	private static readonly HashSet<string> IRR_H = new() {
		"그렇", "이렇", "저렇", "어떻", "아무렇", "파랗", "빨갛", "노랗", "하얗", "까맣", "퍼렇", "허옇", "누렇",
	};

	private static readonly HashSet<string> VERB_IDA = new() {
		"보이다", "먹이다", "죽이다", "속이다", "깎이다", "섞이다", "쌓이다", "쓰이다", "트이다", "녹이다", "높이다", "기울이다", "끼이다",
	};

	private static readonly HashSet<string> NOSPLIT = new() {
		"좋아하", "싫어하", "슬퍼하", "기뻐하", "아파하", "예뻐하", "이뻐하", "무서워하", "부러워하", "미워하", "귀여워하", "안타까워하", "그리워하", "외로워하",
		"위하", "대하", "관하", "향하", "의하", "면하",
	};

	private static readonly HashSet<string> ADJ = new() {
		"좋다", "많다", "적다", "크다", "작다", "높다", "낮다", "길다", "짧다", "멀다", "가깝다", "넓다", "좁다", "느리다", "같다", "쉽다", "어렵다",
		"춥다", "덥다", "뜨겁다", "차갑다", "맵다", "달다", "짜다", "시다", "맛없다", "재미없다", "예쁘다", "이쁘다", "아름답다", "귀엽다", "바쁘다", "아프다",
		"슬프다", "기쁘다", "나쁘다", "배고프다", "목마르다", "졸리다", "더럽다", "괜찮다", "싫다", "굵다", "얇다", "두껍다", "가볍다", "무겁다", "밝다",
		"어둡다", "부드럽다", "외롭다", "새롭다", "괴롭다", "부럽다", "그립다", "반갑다", "고맙다", "행복하다", "피곤하다", "건강하다", "친절하다", "조용하다",
		"깨끗하다", "복잡하다", "간단하다", "중요하다", "필요하다", "가능하다", "불가능하다", "위험하다", "안전하다", "편하다", "불편하다", "미안하다", "감사하다",
		"똑똑하다", "답답하다", "확실하다", "분명하다", "충분하다", "부족하다", "유명하다", "다양하다", "심각하다", "약하다", "강하다", "자유롭다", "평화롭다",
		"향기롭다", "해롭다", "이롭다", "게으르다", "빠르다", "다르다", "배부르다", "서투르다", "고프다", "파랗다", "빨갛다", "노랗다", "하얗다", "까맣다",
		"퍼렇다", "허옇다", "누렇다", "푸르다", "노르다", "그렇다", "이렇다", "저렇다", "어떻다", "아무렇다", "없다",
	};

	private static readonly HashSet<string> STATEVERB = new() {
		"앉다", "서다", "눕다", "살다", "죽다", "가다", "오다", "열리다", "닫히다", "놓이다", "쌓이다", "걸리다", "쓰이다", "피다",
	};

	private static readonly Dictionary<string, string> PASSIVE = new() {
		["보다"] = "보이다",
		["먹다"] = "먹히다",
		["잡다"] = "잡히다",
		["읽다"] = "읽히다",
		["쓰다"] = "쓰이다",
		["놓다"] = "놓이다",
		["쌓다"] = "쌓이다",
		["섞다"] = "섞이다",
		["바꾸다"] = "바뀌다",
		["열다"] = "열리다",
		["팔다"] = "팔리다",
		["끊다"] = "끊기다",
		["안다"] = "안기다",
		["감다"] = "감기다",
		["씻다"] = "씻기다",
		["막다"] = "막히다",
		["닫다"] = "닫히다",
		["듣다"] = "들리다",
		["풀다"] = "풀리다",
		["끌다"] = "끌리다",
		["걸다"] = "걸리다",
		["밀다"] = "밀리다",
		["물다"] = "물리다",
		["자르다"] = "잘리다",
		["부르다"] = "불리다",
		["쫓다"] = "쫓기다",
		["빼앗다"] = "빼앗기다",
		["업다"] = "업히다",
		["만들다"] = "만들어지다",
		["고치다"] = "고쳐지다",
	};

	private static readonly Dictionary<string, string[]> GRP = new() {
		["base"] = new[] { "基本", "기본", "Base" },
		["now"] = new[] { "现在", "현재", "Present" },
		["past"] = new[] { "过去", "과거", "Past" },
		["fut"] = new[] { "将来", "미래", "Future" },
		["prog"] = new[] { "进行", "진행", "Progressive" },
		["cmd"] = new[] { "命令", "명령", "Imperative" },
		["prop"] = new[] { "提议", "청유", "Suggest" },
		["neg"] = new[] { "否定", "부정", "Negative" },
		["hon"] = new[] { "尊敬", "존칭", "Honorific" },
		["con"] = new[] { "连接", "연결", "Connective" },
		["mod"] = new[] { "定语", "관형", "Modifier" },
		["wrt"] = new[] { "书面", "문어", "Written" },
		["ext"] = new[] { "其他", "기타", "Other" },
		["gs"] = new[] { "庆尚道", "경상도", "Gyeongsang" },
		["jl"] = new[] { "全罗道", "전라도", "Jeolla" },
		["cc"] = new[] { "忠清道", "충청도", "Chungcheong" },
		["jj"] = new[] { "济州", "제주", "Jeju" },
		["hg"] = new[] { "咸镜道", "함경도", "Hamgyeong" },
		["pg"] = new[] { "平安道", "평안도", "Pyeongan" },
		["kp"] = new[] { "朝鲜文化语", "문화어", "North Korean" },
		["kpb"] = new[] { "朝鲜播音", "보도 문체", "DPRK broadcast" },
		["yb"] = new[] { "延边朝鲜语", "연변어", "Yanbian Korean" },
		["aux"] = new[] { "补助", "보조", "Auxiliary" },
		["voice"] = new[] { "态", "태", "Voice" },
		["style"] = new[] { "文体", "문체", "Style" },
		["mood"] = new[] { "语气", "양태", "Mood" },
	};

	private static readonly Dictionary<string, string[]> NOTE = new() {
		["v"] = new[] { "动词", "동사", "verb" },
		["a"] = new[] { "形容词", "형용사", "adjective" },
		["itda"] = new[] { "있다", "있다", "있다" },
		["ida"] = new[] { "이다", "이다", "이다" },
		["noun"] = new[] { "名词", "명사", "noun" },
		["anida"] = new[] { "아니다", "아니다", "아니다" },
		["d"] = new[] { "ㄷ 不规则", "ㄷ 불규칙", "ㄷ irregular" },
		["b"] = new[] { "ㅂ 不规则", "ㅂ 불규칙", "ㅂ irregular" },
		["s"] = new[] { "ㅅ 不规则", "ㅅ 불규칙", "ㅅ irregular" },
		["h"] = new[] { "ㅎ 不规则", "ㅎ 불규칙", "ㅎ irregular" },
		["reu"] = new[] { "르 不规则", "르 불규칙", "르 irregular" },
		["eu"] = new[] { "ㅡ 不规则", "ㅡ 불규칙", "ㅡ irregular" },
		["reo"] = new[] { "러 不规则", "러 불규칙", "러 irregular" },
		["hada"] = new[] { "하다", "하다", "hada" },
		["si"] = new[] { "시다", "시다", "sida" },
		["noa"] = new[] { "놓다", "놓다", "놓다" },
		["geot"] = new[] { "ㄷ 不规则，걷다 按「走」", "ㄷ 불규칙, 걷다(걸어요)", "ㄷ irregular, 걷다 as walk" },
		["mut"] = new[] { "ㄷ 不规则，묻다 按「问」", "ㄷ 불규칙, 묻다(물어요)", "ㄷ irregular, 묻다 as ask" },
		["bad"] = new[] { "请输入以 다 结尾的词典形，如 먹다、하다", "다로 끝나는 사전형. 예: 먹다, 하다", "Enter a dictionary form ending in 다, such as 먹다" },
		["dict"] = new[] { "词典", "사전", "dictionary" },
	};

	private static readonly Dictionary<string, string[]> LBL = new() {
		["dict"] = new[] { "词典", "사전", "dictionary" },
		["hap"] = new[] { "敬语", "높임", "formal" },
		["hey"] = new[] { "半敬语", "해요체", "polite" },
		["ban"] = new[] { "非敬语", "반말", "casual" },
		["plain"] = new[] { "书面", "해라체", "plain" },
		["qhap"] = new[] { "敬语疑问", "높임 질문", "formal question" },
		["qplain"] = new[] { "非敬语疑问", "반말 질문", "casual question" },
		["phap"] = new[] { "敬语", "높임", "formal" },
		["phey"] = new[] { "半敬语", "해요체", "polite" },
		["pban"] = new[] { "非敬语", "반말", "casual" },
		["pplain"] = new[] { "书面", "해라체", "plain" },
		["pqhap"] = new[] { "敬语疑问", "높임 질문", "formal question" },
		["pqplain"] = new[] { "非敬语疑问", "반말 질문", "casual question" },
		["fhap"] = new[] { "敬语", "높임", "formal" },
		["fhey"] = new[] { "半敬语", "해요체", "polite" },
		["fban"] = new[] { "非敬语", "반말", "casual" },
		["fplain"] = new[] { "书面", "해라체", "plain" },
		["ghap"] = new[] { "敬语推测", "높임 추측", "formal guess" },
		["ghey"] = new[] { "半敬语推测", "해요 추측", "polite guess" },
		["gban"] = new[] { "非敬语推测", "반말 추측", "casual guess" },
		["gplain"] = new[] { "书面推测", "해라 추측", "plain guess" },
		["lge"] = new[] { "半敬语承诺", "해요 약속", "polite promise" },
		["lkk"] = new[] { "半敬语商量", "해요 의향", "polite suggestion" },
		["prh"] = new[] { "敬语", "높임", "formal" },
		["pry"] = new[] { "半敬语", "해요체", "polite" },
		["prb"] = new[] { "非敬语", "반말", "casual" },
		["prp"] = new[] { "书面", "해라체", "plain" },
		["prpy"] = new[] { "半敬语过去", "해요 과거", "polite past" },
		["prph"] = new[] { "敬语过去", "높임 과거", "formal past" },
		["prpp"] = new[] { "书面过去", "해라 과거", "plain past" },
		["sip"] = new[] { "敬语", "높임", "formal" },
		["seyo"] = new[] { "半敬语", "해요체", "polite" },
		["era"] = new[] { "书面", "해라체", "plain" },
		["cban"] = new[] { "非敬语", "반말", "casual" },
		["jima"] = new[] { "半敬语禁止", "해요 금지", "polite don't" },
		["jimah"] = new[] { "敬语禁止", "높임 금지", "formal don't" },
		["bsida"] = new[] { "敬语", "높임", "formal" },
		["ja"] = new[] { "非敬语", "반말", "casual" },
		["any"] = new[] { "半敬语否定", "해요 부정", "polite negative" },
		["anp"] = new[] { "半敬语否定过去", "해요 부정 과거", "polite negative past" },
		["anhy"] = new[] { "半敬语长否定", "해요 긴 부정", "polite long negative" },
		["anhh"] = new[] { "敬语长否定", "높임 긴 부정", "formal long negative" },
		["anhpy"] = new[] { "半敬语长否过去", "해요 긴 부정 과거", "polite long negative past" },
		["anhpp"] = new[] { "书面长否定", "해라 긴 부정", "plain long negative" },
		["moty"] = new[] { "半敬语不能", "해요 못", "polite can't" },
		["jimot"] = new[] { "半敬语长不能", "해요 긴 못", "polite long can't" },
		["honh"] = new[] { "敬语", "높임", "formal" },
		["hony"] = new[] { "半敬语", "해요체", "polite" },
		["honpy"] = new[] { "半敬语过去", "해요 과거", "polite past" },
		["honph"] = new[] { "敬语过去", "높임 과거", "formal past" },
		["honpp"] = new[] { "书面过去", "해라 과거", "plain past" },
		["honpl"] = new[] { "书面", "해라체", "plain" },
		["honf"] = new[] { "半敬语将来", "해요 미래", "polite future" },
		["go"] = new[] { "并列", "나열", "and" },
		["ase"] = new[] { "然后", "순서", "and then" },
		["myeon"] = new[] { "条件", "조건", "if" },
		["jiman"] = new[] { "转折", "대조", "but" },
		["nikka"] = new[] { "原因", "이유", "because" },
		["ryeo"] = new[] { "打算", "의도", "intend" },
		["wanty"] = new[] { "半敬语想", "해요 싶다", "polite want" },
		["wanth"] = new[] { "敬语想", "높임 싶다", "formal want" },
		["wantb"] = new[] { "非敬语想", "반말 싶다", "casual want" },
		["ayay"] = new[] { "半敬语必须", "해요 해야", "polite must" },
		["aya"] = new[] { "书面必须", "해라 해야", "plain must" },
		["suy"] = new[] { "半敬语可以", "해요 가능", "polite can" },
		["su"] = new[] { "书面可以", "해라 가능", "plain can" },
		["sun"] = new[] { "半敬语不行", "해요 불가", "polite cannot" },
		["ado"] = new[] { "让步", "양보", "even if" },
		["modn"] = new[] { "现在", "현재", "present" },
		["modp"] = new[] { "过去", "과거", "past" },
		["modf"] = new[] { "将来", "미래", "future" },
		["deon"] = new[] { "回想", "회상", "retrospective" },
		["atd"] = new[] { "经历", "경험", "experienced" },
		["neyo"] = new[] { "半敬语感叹", "해요 감탄", "polite reaction" },
		["atne"] = new[] { "过去感叹", "과거 감탄", "past reaction" },
		["jan"] = new[] { "半敬语提醒", "해요 상기", "polite reminder" },
		["jyo"] = new[] { "半敬语确认", "해요 확인", "polite confirm" },
		["jiyo"] = new[] { "半敬语详述", "해요 지요", "polite full confirm" },
		["hyeo"] = new[] { "书面连接", "문어 연결", "written link" },
		["hyeot"] = new[] { "书面过去", "문어 과거", "written past" },
		["hyeos"] = new[] { "书面然后", "문어 순서", "written then" },
		["ji"] = new[] { "中顿", "중지", "pause" },
		["qyes"] = new[] { "判断疑问", "판단 의문", "yes-no question" },
		["qwh"] = new[] { "疑问词疑问", "의문사 의문", "wh-question" },
		["qlight"] = new[] { "轻问", "가벼운 의문", "light question" },
		["cpast"] = new[] { "确认过去", "확인 과거", "confirming past" },
		["fcmd"] = new[] { "敬语命令", "높임 명령", "formal command" },
		["cmd"] = new[] { "命令", "명령", "command" },
		["cfut"] = new[] { "非敬语将来", "반말 미래", "casual future" },
		["must"] = new[] { "必须", "필수", "must" },
		["proh"] = new[] { "禁止", "금지", "prohibition" },
		["peq"] = new[] { "平语", "평대", "familiar" },
		["peqp"] = new[] { "平语过去", "평대 과거", "familiar past" },
		["sug"] = new[] { "劝诱", "청유", "suggestion" },
		["try"] = new[] { "尝试", "시도", "try" },
		["done"] = new[] { "做完", "완료", "completely" },
		["leave"] = new[] { "做好放着", "해 둠", "left done" },
		["prep"] = new[] { "预先做好", "미리 해 둠", "done in advance" },
		["benef"] = new[] { "为别人做", "남을 위해", "for someone" },
		["hbenef"] = new[] { "谦让", "겸양", "humble" },
		["endup"] = new[] { "最终", "결국", "end up" },
		["caus"] = new[] { "使动", "사동", "causative" },
		["turn"] = new[] { "变成", "변화", "become" },
		["becom"] = new[] { "变得", "형용사 변화", "become (adj.)" },
		["simul"] = new[] { "同时", "동시", "while" },
		["assoon"] = new[] { "刚…就", "즉시", "as soon as" },
		["choice"] = new[] { "或者", "선택", "or" },
		["purpose"] = new[] { "以便", "목적", "so that" },
		["afterdoing"] = new[] { "做了之后", "과거 후", "after doing" },
		["midway"] = new[] { "中途", "도중", "midway" },
		["cond"] = new[] { "前提", "전제", "given that" },
		["neunde"] = new[] { "过去转折", "과거 대조", "but (past)" },
		["atda"] = new[] { "回想", "회상", "retrospective" },
		["nayo"] = new[] { "半敬语疑问", "해요 의문", "polite question" },
		["plup"] = new[] { "过去完成", "과거 완료", "pluperfect" },
		["state"] = new[] { "状态", "상태", "state" },
		["pass"] = new[] { "被动", "수동", "passive" },
		["hao"] = new[] { "准敬语", "하오체", "semi-formal" },
		["haq"] = new[] { "准敬语疑问", "하오체 의문", "semi-formal question" },
		["hage"] = new[] { "对等语", "하게체", "familiar" },
		["hagecmd"] = new[] { "对等语命令", "하게체 명령", "familiar command" },
		["hagesug"] = new[] { "对等语劝诱", "하게체 청유", "familiar suggestion" },
		["hearsay"] = new[] { "传闻", "전언", "hearsay" },
		["hearsays"] = new[] { "传闻缩略", "전언 준말", "short hearsay" },
		["conj"] = new[] { "推量", "추량", "conjecture" },
		["guess"] = new[] { "判断（现在）", "판단 현재", "judgment (present)" },
		["guessf"] = new[] { "判断（将来）", "판단 미래", "judgment (future)" },
		["maybe"] = new[] { "也许", "어쩌면", "maybe" },
		["oblig"] = new[] { "义务", "의무", "obligation" },
		["perm"] = new[] { "许可", "허가", "permission" },
		["prohibit"] = new[] { "禁止", "금지", "prohibition" },
		["req"] = new[] { "请求", "요청", "request" },
		["reqp"] = new[] { "恭请", "정중한 요청", "polite request" },
		["intent"] = new[] { "意愿", "의향", "intention" },
		["plan"] = new[] { "打算", "계획", "plan" },
		["decide"] = new[] { "决心", "결심", "decision" },
		["promise"] = new[] { "约定", "약속", "arrangement" },
		["excl"] = new[] { "感叹", "감탄", "exclamation" },
		["confirm"] = new[] { "确认", "확인", "confirming" },
		["kp.hap"] = new[] { "敬语 ㅂ네다", "ㅂ네다", "honorific -pneda" },
		["kp.qhap"] = new[] { "敬语疑问 ㅂ네까", "ㅂ네까", "honorific question -pnekka" },
		["kp.ne"] = new[] { "随意敬语 ㅂ네", "ㅂ네", "mild honorific -pne" },
		["kp.qna"] = new[] { "旧式疑问 ㅂ나", "ㅂ나", "old question -pna" },
		["kp.deda"] = new[] { "亲历 ㅂ데다", "ㅂ데다", "witnessed -pdeda" },
		["kp.degga"] = new[] { "亲历疑问 ㅂ데까", "ㅂ데까", "witnessed question -pdekka" },
		["kp.seda"] = new[] { "劝诱 ㅂ세다", "ㅂ세다", "let us -pseda" },
		["kp.ninda"] = new[] { "断言 습닌다", "습닌다", "assertive -pningda" },
		["kp.sira"] = new[] { "敬语命令 시라", "시라", "honorific imperative -sira" },
		["kp.rayo"] = new[] { "敬语命令 라요", "라요", "honorific imperative -rayo" },
		["kp.giyo"] = new[] { "亲近约定 기요", "기요", "familiar promise -giyo" },
		["kp.soeda"] = new[] { "过去 쇠다", "쇠다", "past -soeda" },
		["kp.soda"] = new[] { "过去 소다", "소다", "past -soda" },
		["kp.gojeo"] = new[] { "-고저 하다", "-고저 하다", "-gojeo hada" },
		["kp.pass"] = new[] { "强调被动 우다", "우다", "emphatic passive -uda" },
		["kp.chap"] = new[] { "敬语 임네다", "임네다", "copula -imneda" },
		["kp.cqhap"] = new[] { "敬语疑问 임네까", "임네까", "copula question -imnekka" },
		["kp.cne"] = new[] { "随意敬语 임네", "임네", "copula mild -imne" },
		["kp.cdeda"] = new[] { "亲历 임데다", "임데다", "copula witnessed -imdeda" },
		["kp.cdegga"] = new[] { "亲历疑问 임데까", "임데까", "copula witnessed question -imdekka" },
		["kp.cseda"] = new[] { "劝诱 임세다", "임세다", "copula let us -imseda" },
		["kp.cninda"] = new[] { "强陈 임닌다", "임닌다", "copula assertive -imninda" },
		["kp.cpast"] = new[] { "过去 이였습니다", "이였습니다", "copula past -iyeotseumnida" },
		["kp.cpast2"] = new[] { "过去 이였다", "이였다", "copula past -iyeotda" },
		["kp.cyayo"] = new[] { "敬语 이야요", "이야요", "copula -iyayo" },
		["kpb.hon"] = new[] { "尊称过去 시였습니다", "시였습니다", "honorific past -siyeotseumnida" },
		["yb.hap"] = new[] { "敬语 ㅁ다", "ㅁ다", "honorific -mda" },
		["yb.qhap"] = new[] { "敬语疑问 ㅁ까", "ㅁ까", "honorific question -mkka" },
		["yb.ndai"] = new[] { "하오 ㄴ다이", "ㄴ다이", "familiar -ndai" },
		["yb.bso"] = new[] { "命令 ㅂ소", "ㅂ소", "imperative -pso" },
		["yb.bseda"] = new[] { "劝诱 ㅂ세다", "ㅂ세다", "let us -pseda" },
		["yb.giyo"] = new[] { "约定 기요", "기요", "promise -giyo" },
		["yb.chap"] = new[] { "敬语 임다", "임다", "copula -imda" },
		["yb.cqhap"] = new[] { "敬语疑问 임까", "임까", "copula question -imkka" },
		["yb.cndai"] = new[] { "하오 인다이", "인다이", "copula familiar -indai" },
		["kpa.hap"] = new[] { "敬语 아닙네다", "아닙네다", "not -apneda" },
		["kpa.qhap"] = new[] { "敬语疑问 아닙네까", "아닙네까", "not question -apnekka" },
		["kpa.past"] = new[] { "过去 아니였습니다", "아니였습니다", "not past -aniyeotseumnida" },
		["kpa.yayo"] = new[] { "敬语 아니야요", "아니야요", "not -aniyayo" },
		["yba.hap"] = new[] { "敬语 아님다", "아님다", "not -amda" },
		["yba.qhap"] = new[] { "敬语疑问 아님까", "아님까", "not question -amkka" },
	};

	/// <summary>
	/// 生成变形表。force 取 auto / v / a / n；uiLang 以 zh 开头用中文、ko 开头用韩文，其余英文。
	/// 纯函数：不改入参、无静态可变状态。
	/// </summary>
	public static ConjResult Build(string word, string force, string uiLang) {
		var lang = string.IsNullOrEmpty(uiLang) ? "zh" : uiLang;
		var li = lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? 0
			: lang.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
		word = norm(word);
		if (string.IsNullOrEmpty(force)) force = "auto";
		if (word.Length == 0) return new ConjResult { Ok = true, Rows = new List<ConjRow>() };
		var all = decall(word);
		// 名词类型：词本身按 이다 变化；已经写成 …이다 的也认
		if (force == "n") {
			if (all == null || all.Count == 0) return fail(li);
			if (word == "아니다") return pack(word, "anida", "", anidarows(word, li), li);
			var nw = word == "이다" || (word.Length > 2 && word.EndsWith("이다", StringComparison.Ordinal)) ? word : word + "이다";
			return pack(word, "noun", "", copularows(nw, li), li);
		}
		// 自动：不带动词词尾时按名词 + 이다
		if (all != null && all.Count > 0 && word[word.Length - 1] != '다')
			return pack(word, "noun", "", copularows(word + "이다", li), li);
		if (all == null || word[word.Length - 1] != '다' || all.Count < 2) return fail(li);
		if (word == "아니다") return pack(word, "anida", "", anidarows(word, li), li);
		if (word == "이다" || (iscopula(word) && force != "v"))
			return pack(word, "ida", "", copularows(word, li), li);
		var stem = word.Substring(0, word.Length - 1);
		var syls = all.GetRange(0, all.Count - 1);
		var irr = classify(stem, syls);
		var kind = decidekind(word, stem, syls, irr, force);
		return pack(word, kind, irr, verbrows(word, stem, syls, irr, kind, li), li);
	}

	// ==================== 音节工具 ====================

	private static KoSyl sy(char ch) {
		var c = ch - 0xAC00;
		return new KoSyl(c / 588, c % 588 / 28, c % 28);
	}

	private static char chof(KoSyl o) =>
		(char)(0xAC00 + (o.Cho * 21 + o.Jung) * 28 + o.Jong);

	private static List<KoSyl> cp(List<KoSyl> a) {
		var o = new List<KoSyl>(a.Count);
		foreach (var s in a) o.Add(new KoSyl(s.Cho, s.Jung, s.Jong));
		return o;
	}

	private static string jn(List<KoSyl> a) {
		var sb = new StringBuilder(a.Count);
		foreach (var s in a) sb.Append(chof(s));
		return sb.ToString();
	}

	private static List<KoSyl> decall(string s) {
		var o = new List<KoSyl>(s.Length);
		foreach (var ch in s) {
			var c = ch - 0xAC00;
			if (c < 0 || c > 0xD7A3 - 0xAC00) return null;
			o.Add(new KoSyl(c / 588, c % 588 / 28, c % 28));
		}
		return o;
	}

	private static bool isah(int v) => v == A || v == YA || v == O || v == YO || v == WA;

	private static bool endssyl(List<KoSyl> syls, int cho, int jung, int jong) {
		var last = syls[syls.Count - 1];
		return last.Cho == cho && last.Jung == jung && last.Jong == jong;
	}

	private static bool ishada(List<KoSyl> syls) => endssyl(syls, CH_H, A, 0);

	private static bool iseu(List<KoSyl> syls) {
		var last = syls[syls.Count - 1];
		return last.Jung == EU && last.Jong == 0;
	}

	private static bool isreu(List<KoSyl> syls) =>
		syls.Count >= 2 && endssyl(syls, CH_R, EU, 0);

	private static bool pair(List<KoSyl> syls, int cho, int jung) {
		if (syls.Count < 2) return false;
		var a = syls[syls.Count - 2];
		return endssyl(syls, CH_R, EU, 0) && a.Cho == cho && a.Jung == jung && a.Jong == 0;
	}

	private static bool isreo(List<KoSyl> syls) => pair(syls, CH_P, U) || pair(syls, CH_N, O);

	private static bool isreuex(List<KoSyl> syls) => pair(syls, CH_DD, A) || pair(syls, CH_CH, I);

	private static bool endsrop(List<KoSyl> syls) => endssyl(syls, CH_R, O, JG_B);

	private static bool isautoh(List<KoSyl> syls) {
		if (syls.Count < 2) return false;
		var last = syls[syls.Count - 1];
		if (last.Jong != JG_H) return false;
		return last.Jung == A || last.Jung == EO || last.Jung == YA || last.Jung == YEO;
	}

	private static bool issi(List<KoSyl> syls) {
		if (!endssyl(syls, CH_S, I, 0)) return false;
		if (syls.Count >= 2) {
			var p = syls[syls.Count - 2];
			if (p.Cho == CH_M && p.Jung == A && p.Jong == 0) return false;
		}
		return true;
	}

	private static bool isyi(KoSyl syl) => syl.Cho == CH_NG && syl.Jung == I && syl.Jong == 0;

	private static string classify(string stem, List<KoSyl> syls) {
		if (stem == "놓") return "noa";
		if (ishada(syls)) return "hada";
		if (isreo(syls)) return "reo";
		if (isreu(syls) && !isreuex(syls)) return "reu";
		if (iseu(syls)) return "eu";
		if (IRR_D.Contains(stem)) return "d";
		if (IRR_B.Contains(stem) || endsrop(syls)) return "b";
		if (IRR_S.Contains(stem)) return "s";
		if (IRR_H.Contains(stem) || isautoh(syls)) return "h";
		if (issi(syls)) return "si";
		return "reg";
	}

	private static bool endsiss(List<KoSyl> syls) => endssyl(syls, CH_NG, I, JG_SS);

	private static bool endseobs(List<KoSyl> syls) => endssyl(syls, CH_NG, EO, JG_BS);

	private static string decidekind(string word, string stem, List<KoSyl> syls, string irr, string force) {
		if (endsiss(syls)) return "itda";
		if (force == "v") return "v";
		if (force == "a") return "a";
		if (ADJ.Contains(word) || endseobs(syls)) return "a";
		if (irr == "h" || irr == "reo") return "a";
		if (irr == "b" && !BVERB.Contains(stem)) return "a";
		return "v";
	}

	private static bool iscopula(string word) {
		if (VERB_IDA.Contains(word)) return false;
		if (word == "이다") return true;
		if (word.Length < 3) return false;
		return isyi(sy(word[word.Length - 2]));
	}

	// ==================== 活用 ====================

	private static string ae(List<KoSyl> syls, string irr) {
		List<KoSyl> s;
		KoSyl last;
		int jung;
		if (irr == "hada") {
			s = cp(syls);
			s[s.Count - 1].Jung = AE;
			return jn(s);
		}
		if (irr == "noa") {
			s = cp(syls);
			s[s.Count - 1].Jong = 0;
			s[s.Count - 1].Jung = WA;
			return jn(s);
		}
		if (irr == "reo") {
			s = cp(syls);
			s.Add(sy('러'));
			return jn(s);
		}
		if (irr == "reu") return aereu(syls);
		if (irr == "eu") return aeeu(syls);
		if (irr == "d") {
			s = cp(syls);
			s[s.Count - 1].Jong = JG_R;
			return addae(s);
		}
		if (irr == "b") {
			s = cp(syls);
			last = s[s.Count - 1];
			jung = last.Jung;
			last.Jong = 0;
			s.Add(sy(jung == O ? '와' : '워'));
			return jn(s);
		}
		if (irr == "s") {
			s = cp(syls);
			s[s.Count - 1].Jong = 0;
			s.Add(sy(isah(s[s.Count - 1].Jung) ? '아' : '어'));
			return jn(s);
		}
		if (irr == "h") {
			s = cp(syls);
			last = s[s.Count - 1];
			last.Jong = 0;
			if (last.Jung == A || last.Jung == EO) last.Jung = AE;
			else if (last.Jung == YA) last.Jung = YAE;
			else if (last.Jung == YEO) last.Jung = YE;
			return jn(s);
		}
		return addae(cp(syls));
	}

	private static string addae(List<KoSyl> s) {
		var last = s[s.Count - 1];
		if (last.Jong != 0) {
			s.Add(sy(isah(last.Jung) ? '아' : '어'));
			return jn(s);
		}
		return addopen(s);
	}

	private static string addopen(List<KoSyl> s) {
		var last = s[s.Count - 1];
		var v = last.Jung;
		if (v == A || v == AE || v == YA || v == EO || v == E || v == YEO || v == YAE || v == YE || v == WA || v == WEO || v == WAE)
			return jn(s);
		if (v == O) { last.Jung = WA; return jn(s); }
		if (v == U) { last.Jung = WEO; return jn(s); }
		if (v == OE) { last.Jung = WAE; return jn(s); }
		if (v == I) { last.Jung = YEO; return jn(s); }
		s.Add(sy(isah(v) ? '아' : '어'));
		return jn(s);
	}

	private static string aeeu(List<KoSyl> syls) {
		var s = cp(syls);
		var i = s.Count - 1;
		var yang = i > 0 && (s[i - 1].Jung == A || s[i - 1].Jung == O);
		s[i].Jung = yang ? A : EO;
		return jn(s);
	}

	private static string aereu(List<KoSyl> syls) {
		var s = cp(syls);
		var prev = s[s.Count - 2];
		var yang = prev.Jung == A || prev.Jung == O;
		if (prev.Jong == 0) prev.Jong = JG_R;
		s[s.Count - 1].Jung = yang ? A : EO;
		s[s.Count - 1].Jong = 0;
		return jn(s);
	}

	private static string spokenae(List<KoSyl> syls, string irr, bool yo) {
		if (irr != "si") return ae(syls, irr);
		var s = cp(syls);
		s[s.Count - 1].Jung = yo ? E : YEO;
		return jn(s);
	}

	private static string pastof(string ba) {
		var s = decall(ba);
		s[s.Count - 1].Jong = JG_SS;
		return jn(s);
	}

	private static string paststem(List<KoSyl> syls, string irr, string ba) {
		if (irr != "si") return pastof(ba);
		var s = cp(syls);
		s[s.Count - 1].Jung = YEO;
		s[s.Count - 1].Jong = JG_SS;
		return jn(s);
	}

	private static KoStem eustem(List<KoSyl> syls, string irr) {
		var s = cp(syls);
		var last = s[s.Count - 1];
		if (irr == "d") {
			last.Jong = JG_R;
			return new KoStem { S = s, Eu = true, R = false };
		}
		if (irr == "b") {
			last.Jong = 0;
			s.Add(sy('우'));
			return new KoStem { S = s, Eu = false, R = false };
		}
		if (irr == "s") {
			last.Jong = 0;
			return new KoStem { S = s, Eu = true, R = false };
		}
		if (irr == "h") {
			last.Jong = 0;
			return new KoStem { S = s, Eu = false, R = false };
		}
		if (last.Jong == 0) return new KoStem { S = s, Eu = false, R = false };
		if (last.Jong == JG_R) return new KoStem { S = s, Eu = false, R = true };
		return new KoStem { S = s, Eu = true, R = false };
	}

	private static KoStem applydrop(KoStem st, bool drop) {
		if (!(drop && st.R)) return st;
		var s = cp(st.S);
		s[s.Count - 1].Jong = 0;
		return new KoStem { S = s, Eu = st.Eu, R = false };
	}

	private static string pluseu(KoStem st, string tail, bool drop) {
		st = applydrop(st, drop);
		return jn(st.S) + (st.Eu ? "으" : "") + tail;
	}

	private static string hapsho(List<KoSyl> syls) {
		var s = cp(syls);
		var last = s[s.Count - 1];
		if (last.Jong == 0 || last.Jong == JG_R) {
			last.Jong = JG_B;
			return jn(s) + "니다";
		}
		return jn(s) + "습니다";
	}

	private static string neunda(List<KoSyl> syls, bool plainDa, string word) {
		if (plainDa) return word;
		var s = cp(syls);
		var last = s[s.Count - 1];
		if (last.Jong == 0 || last.Jong == JG_R) {
			if (last.Jong == JG_R) last.Jong = 0;
			last.Jong = JG_N;
			return jn(s) + "다";
		}
		return jn(s) + "는다";
	}

	private static string rieulword(List<KoSyl> syls, string irr) {
		var st = eustem(syls, irr);
		if (st.Eu) return jn(st.S) + "을";
		var s = cp(st.S);
		s[s.Count - 1].Jong = JG_R;
		return jn(s);
	}

	private static string nieunword(List<KoSyl> syls, string irr) {
		var st = eustem(syls, irr);
		if (st.Eu) return jn(st.S) + "은";
		st = applydrop(st, true);
		var s = cp(st.S);
		s[s.Count - 1].Jong = JG_N;
		return jn(s);
	}

	private static string neun(List<KoSyl> syls) {
		var s = cp(syls);
		if (s[s.Count - 1].Jong == JG_R) s[s.Count - 1].Jong = 0;
		return jn(s) + "는";
	}

	private static string dropr(List<KoSyl> syls, string tail) {
		var s = cp(syls);
		if (s[s.Count - 1].Jong == JG_R) s[s.Count - 1].Jong = 0;
		return jn(s) + tail;
	}

	private static string qplain(List<KoSyl> syls, string irr, string kind) {
		if (kind == "a" && !endseobs(syls)) return pluseu(eustem(syls, irr), "냐", true);
		return dropr(syls, "느냐");
	}

	private static string seyoform(List<KoSyl> syls, string irr) {
		if (irr == "si") return spokenae(syls, irr, true) + "요";
		return pluseu(eustem(syls, irr), "세요", true);
	}

	private static string sipnio(List<KoSyl> syls, string irr) {
		if (irr == "si") {
			var s = cp(syls);
			s.RemoveAt(s.Count - 1);
			return jn(s) + "십시오";
		}
		return pluseu(eustem(syls, irr), "십시오", true);
	}

	private static string bsida(List<KoSyl> syls, string irr) {
		if (irr == "si") {
			var s = cp(syls);
			s[s.Count - 1].Jong = JG_B;
			return jn(s) + "시다";
		}
		var st = applydrop(eustem(syls, irr), true);
		if (st.Eu) return jn(st.S) + "읍시다";
		var s2 = cp(st.S);
		s2[s2.Count - 1].Jong = JG_B;
		return jn(s2) + "시다";
	}

	private static List<KoSyl> hon(List<KoSyl> syls, string irr) {
		if (jn(syls) == "있") return decall("계시");
		if (irr == "hada") {
			var s = cp(syls);
			s.Add(sy('시'));
			return s;
		}
		var st = applydrop(eustem(syls, irr), true);
		var output = cp(st.S);
		if (st.Eu) output.Add(sy('으'));
		output.Add(sy('시'));
		return output;
	}

	private static string honhey(List<KoSyl> hs) {
		var s = cp(hs);
		s[s.Count - 1].Jung = E;
		return jn(s) + "요";
	}

	private static string honat(List<KoSyl> hs) {
		var s = cp(hs);
		s[s.Count - 1].Jung = YEO;
		s[s.Count - 1].Jong = JG_SS;
		return jn(s);
	}

	private static string honhap(List<KoSyl> hs) {
		var s = cp(hs);
		s[s.Count - 1].Jong = JG_B;
		return jn(s) + "니다";
	}

	private static string honplain(List<KoSyl> hs) {
		var s = cp(hs);
		s[s.Count - 1].Jong = JG_N;
		return jn(s) + "다";
	}

	private static string honfut(List<KoSyl> hs) {
		var s = cp(hs);
		s[s.Count - 1].Jong = JG_R;
		return jn(s);
	}

	private static string splitha(string stem, string irr) {
		if (irr != "hada" || NOSPLIT.Contains(stem)) return "";
		var syls = decall(stem);
		if (syls == null || syls.Count < 2) return "";
		var prev = syls[syls.Count - 2];
		if (prev.Cho == CH_NG && prev.Jong == 0 && (prev.Jung == A || prev.Jung == EO || prev.Jung == WA || prev.Jung == WEO || prev.Jung == YEO))
			return "";
		return stem.Substring(0, stem.Length - 1);
	}

	private static string wrapneg(string stem, string irr, string full, string mark) {
		var head = splitha(stem, irr);
		if (head.Length == 0 || !full.StartsWith(head, StringComparison.Ordinal)) return mark + " " + full;
		return head + " " + mark + " " + full.Substring(head.Length);
	}

	// ==================== 标签 ====================

	private static string lp(int li, string[] row) => row[li];

	private static string rowname(string id, string name, int li) =>
		LBL.TryGetValue(id, out var lab) ? lp(li, lab) : name;

	private static string noteof(string kind, string irr, string stem, int li) {
		var k = lp(li, NOTE.TryGetValue(kind, out var note) ? note : NOTE["v"]);
		var tag = "";
		if (stem == "걷") tag = lp(li, NOTE["geot"]);
		else if (stem == "묻") tag = lp(li, NOTE["mut"]);
		else if (!string.IsNullOrEmpty(irr) && irr != "reg" && NOTE.TryGetValue(irr, out var nt)) tag = lp(li, nt);
		return tag.Length > 0 ? $"{k} · {tag}" : k;
	}

	private static string norm(string s) {
		s = (s ?? "").Trim();
		for (var i = 0; i < s.Length; i++) {
			if (!char.IsWhiteSpace(s[i])) continue;
			s = s.Substring(0, i);
			break;
		}
		return s.TrimEnd('.', '?', '。', '．', '!', '！', ',', '，');
	}

	private static string repend(string s, string suffix, string rep) =>
		s.EndsWith(suffix, StringComparison.Ordinal) ? s.Substring(0, s.Length - suffix.Length) + rep : s;

	// ==================== 结果打包 ====================

	private static ConjResult fail(int li) => new() {
		Ok = false,
		Err = lp(li, NOTE["bad"]),
		Rows = new List<ConjRow>()
	};

	private static ConjResult pack(string word, string kind, string irr, List<ConjRow> rows, int li) {
		var stem = word == "이다" || word == "아니다" ? "" : word.Substring(0, word.Length - 1);
		if (kind == "ida" || kind == "anida") stem = "";
		return new ConjResult {
			Ok = true,
			Head = word,
			Name = kind,
			Irr = irr,
			Note = noteof(kind, irr, stem, li),
			Rows = rows
		};
	}

	// ==================== 动词 / 形容词 ====================

	private static List<ConjRow> verbrows(string word, string stem, List<KoSyl> syls, string irr, string kind, int li) {
		var rows = new List<ConjRow>();
		var cur = "";
		var yo = spokenae(syls, irr, true);
		var ba = spokenae(syls, irr, false);
		var hey = yo + "요";
		var past = paststem(syls, irr, ba);
		var st = eustem(syls, irr);
		var plainDa = kind == "a" || kind == "itda" || stem == "계시";
		var verb = kind == "v";
		var adj = kind == "a";
		var rl = rieulword(syls, irr);
		var eobs = endseobs(syls);
		var hs = irr == "si" ? null : hon(syls, irr);
		string g(string key) => lp(li, GRP[key]);
		void add(string id, string name, string text) {
			if (string.IsNullOrEmpty(text)) return;
			rows.Add(new ConjRow(rowname(id, name, li), text, cur));
		}
		string quad(string a, string n, string pa, string pn) => $"{a} / {n} / {pa} / {pn}";
		string pairneg(string a, string n) => $"{a} / {n}";
		cur = g("base");
		add("dict", lp(li, NOTE["dict"]), word);
		cur = g("now");
		add("hap", "합니다", hapsho(syls));
		add("hey", "해요", hey);
		add("ban", "해", ba);
		add("plain", plainDa ? "다" : "는다", neunda(syls, plainDa, word));
		add("qhap", "합니까", repend(hapsho(syls), "니다", "니까"));
		add("qplain", adj && !eobs ? "냐" : "느냐", qplain(syls, irr, kind));
		add("nayo", "나요", (adj && !eobs ? nieunword(syls, irr) + "가요" : dropr(syls, "나요")) + "?");
		cur = g("past");
		add("phap", "했습니다", past + "습니다");
		add("phey", "했어요", past + "어요");
		add("pban", "했어", past + "어");
		add("pplain", "했다", past + "다");
		add("pqhap", "했습니까", past + "습니까");
		add("pqplain", "했느냐", past + "느냐");
		if (verb) add("plup", "했었어요", past + "었어요");
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
			if (STATEVERB.Contains(word)) add("state", "상태", ba + " 있어요");
			cur = g("cmd");
			add("sip", "십시오", sipnio(syls, irr));
			add("seyo", "세요", seyoform(syls, irr));
			add("era", "어라", ba + "라");
			add("cban", "해", ba);
			add("jima", "지 마세요", jn(syls) + "지 마세요");
			add("jimah", "지 마십시오", jn(syls) + "지 마십시오");
			cur = g("prop");
			add("bsida", "ㅂ시다", bsida(syls, irr));
			add("ja", "자", jn(syls) + "자");
		}
		cur = g("neg");
		add("any", "안 해요", wrapneg(stem, irr, hey, "안"));
		add("anp", "안 했어요", wrapneg(stem, irr, past + "어요", "안"));
		add("anhy", "지 않아요", jn(syls) + "지 않아요");
		add("anhh", "지 않습니다", jn(syls) + "지 않습니다");
		add("anhpy", "지 않았어요", jn(syls) + "지 않았어요");
		add("anhpp", "지 않았다", jn(syls) + "지 않았다");
		if (verb) {
			add("moty", "못 해요", wrapneg(stem, irr, hey, "못"));
			add("jimot", "지 못해요", jn(syls) + "지 못해요");
		}
		if (hs != null) {
			var hat = honat(hs);
			cur = g("hon");
			add("honh", "십니다", honhap(hs));
			add("hony", "세요", honhey(hs));
			add("honpy", "셨어요", hat + "어요");
			add("honph", "셨습니다", hat + "습니다");
			add("honpp", "셨다", hat + "다");
			add("honpl", "신다", honplain(hs));
			add("honf", "실 거예요", honfut(hs) + " 거예요");
		}
		cur = g("con");
		add("go", "고", jn(syls) + "고");
		add("ase", "아서", ba + "서");
		add("myeon", "면", pluseu(st, "면", false));
		add("jiman", "지만", jn(syls) + "지만");
		add("nikka", "니까", pluseu(st, "니까", true));
		if (verb) {
			add("ryeo", "려고", pluseu(st, "려고", false));
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
		add("simul", "면서", pluseu(st, "면서", false));
		add("choice", "거나", jn(syls) + "거나");
		add("neunde", "았는데", past + "는데");
		add("purpose", "도록", jn(syls) + "도록");
		add("afterdoing", "았더니", past + "더니");
		add("cond", "거든", jn(syls) + "거든");
		if (verb) {
			add("assoon", "자마자", jn(syls) + "자마자");
			add("nikka", "느라고", dropr(syls, "느라고"));
			add("midway", "다가", jn(syls) + "다가");
			add("ase", "고서", jn(syls) + "고서");
		}
		cur = g("mod");
		if (adj && !eobs) add("modn", "ㄴ", nieunword(syls, irr));
		else add("modn", "는", neun(syls));
		if (verb) add("modp", "ㄴ", nieunword(syls, irr));
		add("modf", "ㄹ", rl);
		add("deon", "던", jn(syls) + "던");
		add("atd", "았던", past + "던");
		cur = g("ext");
		add("neyo", "네요", dropr(syls, "네요"));
		add("atne", "았네요", past + "네요");
		add("jan", "잖아요", dropr(syls, "잖아요"));
		add("jyo", "죠", jn(syls) + "죠");
		add("jiyo", "지요", jn(syls) + "지요");
		add("deon", "았더라고요", past + "더라고요");
		add("atda", "았더라", past + "더라");
		if (irr == "hada") {
			var hy = stem.Substring(0, stem.Length - 1) + "하여";
			cur = g("wrt");
			add("hyeo", "하여", hy);
			add("hyeot", "하였다", stem.Substring(0, stem.Length - 1) + "하였다");
			add("hyeos", "하여서", hy + "서");
		}
		// 补助动词：试、做完、放着、预先、为别人、最终
		cur = g("aux");
		if (verb) {
			add("try", "보다", ba + " 봤어요");
			add("done", "버리다", ba + " 버렸어요");
			add("leave", "놓다", ba + " 놨어요");
			add("prep", "두다", ba + " 뒀어요");
			add("benef", "주다", ba + " 줬어요");
			add("hbenef", "드리다", ba + " 드렸어요");
			add("endup", "고 말다", jn(syls) + "고 말았어요");
		}
		// 态：使动、变成、形容词的「变得」、固定说法的被动
		cur = g("voice");
		add("caus", "게 하다", jn(syls) + "게 했어요");
		add("turn", "게 되다", jn(syls) + "게 되었어요");
		if (adj) add("becom", "아/어지다", ba + "졌어요");
		add("pass", "被动", passivetext(word));
		// 文体：하오체 / 하게체
		cur = g("style");
		add("hao", "하오체", haoform(syls));
		add("haq", "하오체 의문", haoform(syls) + "?");
		add("hage", "하게체", jn(syls) + "네");
		if (verb) {
			add("hagecmd", "하게체 명령", jn(syls) + "게");
			add("hagesug", "하게체 청유", jn(syls) + "세");
		}
		// 语气：传闻、推量、判断、义务、许可、请求、意愿、感叹；肯定与否定同格并列
		var plainnow = neunda(syls, plainDa, word);
		var itda = kind == "itda";
		var negnow = itda ? "없다" : jn(syls) + (verb ? "지 않는다" : "지 않다");
		var negpast = itda ? "없었" : jn(syls) + "지 않았";
		var negfut = itda ? "없을" : jn(syls) + "지 않을";
		var negguess = itda ? "없나 봐요" : jn(syls) + (adj ? "지 않은가 봐요" : "지 않나 봐요");
		var negguessp = itda ? "없는 것 같아요" : jn(syls) + (adj ? "지 않은 것 같아요" : "지 않는 것 같아요");
		var negexcl = itda ? "없군요" : jn(syls) + (adj ? "지 않은군요" : "지 않는군요");
		cur = g("mood");
		add("hearsay", "다고 해요", quad(plainnow + "고 해요", negnow + "고 해요", past + "다고 해요", negpast + "다고 해요"));
		add("hearsays", "대요", quad(repend(plainnow, "다", "대요"), repend(negnow, "다", "대요"), past + "대요", negpast + "대요"));
		add("conj", "나 봐요", quad(adj && !eobs ? nieunword(syls, irr) + "가 봐요" : dropr(syls, "나 봐요"), negguess, past + "나 봐요", negpast + "나 봐요"));
		add("guess", "는 것 같아요", quad((adj && !eobs ? nieunword(syls, irr) : neun(syls)) + " 것 같아요", negguessp, past + "던 것 같아요", negpast + "던 것 같아요"));
		add("guessf", "ㄹ 것 같아요", pairneg(rl + " 것 같아요", negfut + " 것 같아요"));
		add("maybe", "ㄹ지도 몰라요", quad(rl + "지도 몰라요", negfut + "지도 몰라요", past + "을지도 몰라요", negpast + "을지도 몰라요"));
		add("oblig", "아야 돼요", pairneg(ba + "야 돼요", itda ? "없어야 돼요" : jn(syls) + "지 않아야 돼요"));
		add("perm", "아도 돼요", quad(ba + "도 돼요", itda ? "없어도 돼요" : jn(syls) + "지 않아도 돼요", ba + "도 됐어요", itda ? "없어도 됐어요" : jn(syls) + "지 않아도 됐어요"));
		add("prohibit", "으면 안 돼요", pluseu(st, "면 안 돼요", false));
		add("excl", "는군요", quad((adj || itda ? jn(syls) : neun(syls)) + "군요", negexcl, past + "군요", negpast + "군요"));
		add("confirm", "다면서요?", quad(plainnow + "면서요?", negnow + "면서요?", past + "다면서요?", negpast + "다면서요?"));
		if (verb) {
			add("req", "아 주세요", pairneg(ba + " 주세요", jn(syls) + "지 말아 주세요"));
			add("reqp", "아 주시겠어요?", pairneg(ba + " 주시겠어요?", jn(syls) + "지 말아 주시겠어요?"));
			add("intent", "ㄹ래요", pairneg(rl + "래요", jn(syls) + "지 않을래요"));
			add("plan", "려고 해요", quad(pluseu(st, "려고 해요", false), jn(syls) + "지 않으려고 해요", pluseu(st, "려고 했어요", false), jn(syls) + "지 않으려고 했어요"));
			add("decide", "아야겠어요", pairneg(ba + "야겠어요", jn(syls) + "지 않아야겠어요"));
			add("promise", "기로 해요", quad(jn(syls) + "기로 해요", jn(syls) + "지 않기로 해요", jn(syls) + "기로 했어요", jn(syls) + "지 않기로 했어요"));
		}
		rows.AddRange(dialectrows(syls, irr, kind, li));
		rows.AddRange(koreanrows(word, syls, irr, kind, li));
		return rows;
	}

	// -ㅂ네다 / -읍네다 这类以 ㅂ 结尾的方言尾：开音节加 ㅂ，闭音节加 읍
	private static string hapn(List<KoSyl> syls, string tail) {
		var s = cp(syls);
		var last = s[s.Count - 1];
		if (last.Jong == 0 || last.Jong == JG_R) {
			last.Jong = JG_B;
			return jn(s) + tail;
		}
		return jn(s) + "읍" + tail;
	}

	// 济州语 -수다 / -수꽈：动词接 해体形（먹어수다、가수다），形容词和 있다 接词干（좋수다）
	private static string jejubase(List<KoSyl> syls, string kind, string irr, string ba) =>
		kind == "v" && irr != "hada" ? ba : jn(syls);

	// 把标准语 의문형 -느냐/-냐 换成方言的 -나/-노/-냐
	private static string qdial(string q, string end) => repend(repend(q, "느냐", end), "냐", end);

	// 하오체：开音节接 -오（가오），闭音节接 -소（먹소）
	private static string haoform(List<KoSyl> syls) {
		var last = syls[syls.Count - 1];
		return jn(syls) + (last.Jong == 0 ? "오" : "소");
	}

	// 形态被动只有固定说法，给出词典形和 해体形
	private static string passivetext(string word) {
		if (!PASSIVE.TryGetValue(word, out var pv)) return "";
		var all = decall(pv);
		if (all == null || all.Count < 2 || pv[pv.Length - 1] != '다') return pv;
		var syls = all.GetRange(0, all.Count - 1);
		var stem = pv.Substring(0, pv.Length - 1);
		var irr = classify(stem, syls);
		return $"{pv} · {spokenae(syls, irr, false)}요";
	}

	private static List<ConjRow> dialectrows(List<KoSyl> syls, string irr, string kind, int li) {
		var rows = new List<ConjRow>();
		var hey = spokenae(syls, irr, true) + "요";
		var ba = spokenae(syls, irr, false);
		var past = paststem(syls, irr, ba);
		var st = eustem(syls, irr);
		var rl = rieulword(syls, irr);
		var q = qplain(syls, irr, kind);
		var haps = hapsho(syls);
		var verbonly = kind == "v";
		string g(string id) => lp(li, GRP[id]);
		void add(string gid, string key, string text) {
			if (string.IsNullOrEmpty(text)) return;
			rows.Add(new ConjRow(rowname(key, key, li), text, g(gid)));
		}
		// 경상도：해요体 -예、합쇼体 -ㅁ니더、判断 -나 / 疑问词 -노
		add("gs", "hey", repend(hey, "요", "예"));
		add("gs", "prpy", past + "어예");
		add("gs", "qyes", qdial(q, "나") + "?");
		add("gs", "qwh", qdial(q, "노") + "?");
		add("gs", "hap", repend(haps, "다", "더"));
		// 전라도：높임 -라우/-지라우、의문 -냐、确认 -당께、命令 -시소
		add("jl", "hey", ba + "라우");
		add("jl", "prph", past + "지라우");
		add("jl", "qplain", qdial(q, "냐") + "?");
		add("jl", "cpast", past + "당께");
		if (verbonly) add("jl", "fcmd", pluseu(st, "시소", true));
		// 충청도：해요体 -유/-슈、의문 -냐、将来 -ㄹ겨、轻问 -남
		add("cc", "hey", repend(hey, "요", "유"));
		add("cc", "prpy", past + "슈");
		add("cc", "qplain", qdial(q, "냐") + "?");
		add("cc", "cfut", rl + "겨");
		add("cc", "qlight", past + "남?");
		// 제주：합쇼体 -수다/-수꽈、必须 -아/어사、禁止 -지 말앙
		var jj = jejubase(syls, kind, irr, ba);
		add("jj", "hap", jj + "수다");
		add("jj", "qhap", jj + "수꽈?");
		add("jj", "must", ba + "사");
		add("jj", "proh", jn(syls) + "지 말앙");
		// 함경도：敬语过去 -슴다、平语过去 -슴메、确认过去 -지비、劝诱 -기요、敬语命令 -ㅂ소
		add("hg", "prph", past + "슴다");
		add("hg", "peqp", past + "슴메");
		add("hg", "cpast", past + "지비");
		if (verbonly) {
			add("hg", "sug", jn(syls) + "기요");
			add("hg", "fcmd", hapn(syls, "소"));
		}
		// 평안도：敬语 -ㅂ네다/-ㅂ네까、劝诱 -ㅂ세다、命令 -라우、敬语过去 -수다
		add("pg", "hap", hapn(syls, "네다"));
		add("pg", "qhap", hapn(syls, "네까?"));
		if (verbonly) {
			add("pg", "sug", hapn(syls, "세다"));
			add("pg", "cmd", pluseu(st, "라우", true));
		}
		add("pg", "prph", past + "수다");
		return rows;
	}

	// 文化语 -ㅂ네다 类：开音节和 ㄹ 结尾加 ㅂ，其它闭音节加 습（갑네다 / 먹습네다 / 삽네다）
	private static string kpend(List<KoSyl> syls, string tail) {
		var s = cp(syls);
		var last = s[s.Count - 1];
		if (last.Jong == 0 || last.Jong == JG_R) {
			last.Jong = JG_B;
			return jn(s) + tail;
		}
		return jn(s) + "습" + tail;
	}

	// 延边 -ㅁ다 / -ㅁ까：开音节和 ㄹ 结尾加 ㅁ，其它闭音节加 슴（감다 / 먹슴다 / 삼다）
	private static string ybend(List<KoSyl> syls, string tail) {
		var s = cp(syls);
		var last = s[s.Count - 1];
		if (last.Jong == 0 || last.Jong == JG_R) {
			last.Jong = JG_M;
			return jn(s) + tail;
		}
		return jn(s) + "슴" + tail;
	}

	// 延边 하오体 -ㄴ다이 / -는다이，取法与 -는 / -은 相同
	private static string ybndai(List<KoSyl> syls) {
		var s = cp(syls);
		var last = s[s.Count - 1];
		if (last.Jong == 0 || last.Jong == JG_R) {
			last.Jong = JG_N;
			return jn(s) + "다이";
		}
		return jn(s) + "는다이";
	}

	// 文化语把被动的 이 · 히 · 리 · 기 后再加 우：먹히다 → 먹히우다，只给与标准语不同的强调形
	private static string kppassive(string word) {
		if (!PASSIVE.TryGetValue(word, out var pv)) return "";
		var u = repend(repend(repend(repend(pv, "이다", "이우다"), "히다", "히우다"), "리다", "리우다"), "기다", "기우다");
		return u == pv ? "" : u;
	}

	// 朝鲜文化语、朝鲜播音体、延边朝鲜语三组词尾
	private static List<ConjRow> koreanrows(string word, List<KoSyl> syls, string irr, string kind, int li) {
		var rows = new List<ConjRow>();
		var past = paststem(syls, irr, spokenae(syls, irr, false));
		var st = eustem(syls, irr);
		var hs = irr == "si" ? null : hon(syls, irr);
		var verb = kind == "v";
		void add(string gid, string key, string text) {
			if (string.IsNullOrEmpty(text)) return;
			rows.Add(new ConjRow(rowname(gid + "." + key, key, li), text, lp(li, GRP[gid])));
		}
		// 文化语：ㅂ네다 系、ㅂ데다、ㅂ세다、시라 / 라요、기요、쇠다 / 소다、고저；与标准语同形的자요·더랬-不列
		add("kp", "hap", kpend(syls, "네다"));
		add("kp", "qhap", kpend(syls, "네까?"));
		add("kp", "ne", kpend(syls, "네"));
		add("kp", "qna", kpend(syls, "나?"));
		add("kp", "deda", kpend(syls, "데다"));
		add("kp", "degga", kpend(syls, "데까?"));
		// -습닌다 只接在辅音结尾的词干后（ㄹ 除外），开音节没有这个说法
		var nlast = syls[syls.Count - 1];
		if (nlast.Jong != 0 && nlast.Jong != JG_R) add("kp", "ninda", jn(syls) + "습닌다");
		if (verb) {
			add("kp", "seda", kpend(syls, "세다"));
			add("kp", "sira", pluseu(st, "시라", true));
			add("kp", "rayo", pluseu(st, "라요", true));
			add("kp", "giyo", jn(syls) + "기요");
			add("kp", "gojeo", jn(syls) + "고저 합니다");
		}
		add("kp", "soeda", past + "쇠다");
		add("kp", "soda", past + "소다");
		add("kp", "pass", kppassive(word));
		// 朝鲜播音：합쇼体 与标准语同形，只列 -시였습니다 一系
		if (hs != null) add("kpb", "hon", jn(hs) + "였습니다 / " + jn(hs) + "였습니까?");
		// 延边：-ㅁ다 / 슴다、-ㄴ다이、-ㅂ소、-ㅂ세다、-기요
		add("yb", "hap", ybend(syls, "다"));
		add("yb", "qhap", ybend(syls, "까?"));
		add("yb", "ndai", ybndai(syls));
		if (verb) {
			add("yb", "bso", hapn(syls, "소"));
			add("yb", "bseda", hapn(syls, "세다"));
			add("yb", "giyo", jn(syls) + "기요");
		}
		return rows;
	}

	// 名词（이다）在文化语和延边语里的说法
	private static List<ConjRow> koreancopularows(string noun, int li) {
		var rows = new List<ConjRow>();
		void add(string gid, string key, string text) {
			if (string.IsNullOrEmpty(text)) return;
			rows.Add(new ConjRow(rowname(gid + "." + key, key, li), text, lp(li, GRP[gid])));
		}
		add("kp", "chap", noun + "입네다");
		add("kp", "cqhap", noun + "입네까?");
		add("kp", "cne", noun + "입네");
		add("kp", "cdeda", noun + "입데다");
		add("kp", "cdegga", noun + "입데까?");
		add("kp", "cseda", noun + "입세다");
		add("kp", "cninda", noun + "입닌다");
		add("kp", "cpast", noun + "이였습니다");
		add("kp", "cpast2", noun + "이였다");
		add("kp", "cyayo", noun + "이야요");
		add("yb", "chap", noun + "임다");
		add("yb", "cqhap", noun + "임까?");
		add("yb", "cndai", noun + "인다이");
		return rows;
	}

	// 名词（이다）的方言说法：只列证据明确的四种
	private static List<ConjRow> copuladialect(string noun, bool bat, int li) {
		var rows = new List<ConjRow>();
		string g(string id) => lp(li, GRP[id]);
		void add(string gid, string key, string text) {
			if (string.IsNullOrEmpty(text)) return;
			rows.Add(new ConjRow(rowname(key, key, li), text, g(gid)));
		}
		// 경상도：-데이 / -이었데이 / -이가?
		add("gs", "hey", noun + (bat ? "이데이" : "데이"));
		add("gs", "prpy", noun + (bat ? "이었데이" : "였데이"));
		add("gs", "qyes", noun + (bat ? "이가?" : "가?"));
		// 전라도：-라우 / -이었지라우 / -이냐?
		add("jl", "hey", noun + (bat ? "이라우" : "라우"));
		add("jl", "prph", noun + (bat ? "이었지라우" : "였지라우"));
		add("jl", "qplain", noun + (bat ? "이냐?" : "냐?"));
		// 충청도：-유 / -이었슈 / -이냐?
		add("cc", "hey", noun + (bat ? "이유" : "유"));
		add("cc", "prpy", noun + (bat ? "이었슈" : "였슈"));
		add("cc", "qplain", noun + (bat ? "이냐?" : "냐?"));
		// 제주：-이우다 / -이우꽈?
		add("jj", "hap", noun + (bat ? "이우다" : "우다"));
		add("jj", "qhap", noun + (bat ? "이우꽈?" : "우꽈?"));
		return rows;
	}

	private static string copart(string noun, bool bat, string withi, string open) {
		if (noun.Length == 0) return "이" + withi;
		return noun + (bat ? "이" + withi : open);
	}

	private static List<ConjRow> copularows(string word, int li) {
		var noun = word == "이다" ? "" : word.Substring(0, word.Length - 2);
		var bat = true;
		if (noun.Length > 0) {
			var last = decall(noun);
			bat = last != null && last.Count > 0 && last[last.Count - 1].Jong != 0;
		}
		var rows = new List<ConjRow>();
		var cur = "";
		string g(string key) => lp(li, GRP[key]);
		void add(string id, string name, string text) {
			rows.Add(new ConjRow(rowname(id, name, li), text, cur));
		}
		string quad(string a, string n, string pa, string pn) => $"{a} / {n} / {pa} / {pn}";
		cur = g("base");
		add("dict", lp(li, NOTE["dict"]), word);
		cur = g("now");
		add("hap", "입니다", noun + "입니다");
		add("hey", "예요", copart(noun, bat, "에요", "예요"));
		add("ban", "야", copart(noun, bat, "야", "야"));
		add("plain", "이다", noun.Length > 0 ? noun + "이다" : "이다");
		cur = g("past");
		add("phap", "었습니다", copart(noun, bat, "었습니다", "였습니다"));
		add("phey", "었어요", copart(noun, bat, "었어요", "였어요"));
		add("pban", "었어", copart(noun, bat, "었어", "였어"));
		add("pplain", "었다", copart(noun, bat, "었다", "였다"));
		cur = g("fut");
		add("fhey", "ㄹ 거예요", noun + "일 거예요");
		add("fhap", "ㄹ 겁니다", noun + "일 겁니다");
		add("fplain", "ㄹ 것이다", noun + "일 것이다");
		cur = g("con");
		add("myeon", "면", copart(noun, bat, "면", "면"));
		add("go", "고", copart(noun, bat, "고", "고"));
		add("jiman", "지만", copart(noun, bat, "지만", "지만"));
		add("ji", "지", copart(noun, bat, "지", "지"));
		cur = g("mod");
		add("modn", "ㄴ", noun + "인");
		add("modf", "ㄹ", noun + "일");
		add("deon", "던", copart(noun, bat, "던", "던"));
		add("atd", "았던", copart(noun, bat, "었던", "였던"));
		cur = g("hon");
		add("seyo", "세요", copart(noun, bat, "세요", "세요"));
		add("honh", "십니다", noun + "이십니다");
		add("honpy", "셨어요", copart(noun, bat, "셨어요", "셨어요"));
		cur = g("neg");
		add("any", "아니에요", noun.Length > 0 ? noun + (bat ? "이" : "가") + " 아니에요" : "아니에요");
		cur = g("mood");
		var p = bat ? "이" : "가";
		var nop = noun + p + " 아니";
		add("hearsay", "다고 해요", quad(noun + (bat ? "이라고" : "라고") + " 해요", nop + "라고 해요", noun + (bat ? "이었다고" : "였다고") + " 해요", nop + "었다고 해요"));
		add("hearsays", "래요", quad(noun + (bat ? "이래요" : "래요"), nop + "래요", noun + (bat ? "이었대요" : "였대요"), nop + "었대요"));
		add("conj", "인가 봐요", quad(noun + "인가 봐요", noun + p + " 아닌가 봐요", noun + (bat ? "이었나" : "였나") + " 봐요", nop + "었나 봐요"));
		add("guess", "인 것 같아요", quad(noun + "인 것 같아요", noun + p + " 아닌 것 같아요", noun + (bat ? "이었던" : "였던") + " 것 같아요", nop + "었던 것 같아요"));
		add("guessf", "일 것 같아요", noun + "일 것 같아요 / " + noun + p + " 아닐 것 같아요");
		add("maybe", "일지도 몰라요", quad(noun + "일지도 몰라요", noun + p + " 아닐지도 몰라요", noun + (bat ? "이었을지도" : "였을지도") + " 몰라요", nop + "었을지도 몰라요"));
		add("oblig", "이어야 돼요", noun + (bat ? "이어야" : "여야") + " 돼요 / " + nop + "어야 돼요");
		add("perm", "아도 돼요", quad(noun + (bat ? "이어도" : "여도") + " 돼요", nop + "어도 돼요", noun + (bat ? "이어도" : "여도") + " 됐어요", nop + "어도 됐어요"));
		add("prohibit", "으면 안 돼요", noun + (bat ? "이면" : "면") + " 안 돼요");
		add("excl", "군요", quad(noun + (bat ? "이군요" : "군요"), nop + "군요", noun + (bat ? "이었군요" : "였군요"), nop + "었군요"));
		add("confirm", "다면서요?", quad(noun + (bat ? "이라면서요?" : "라면서요?"), nop + "라면서요?", noun + (bat ? "이었다면서요?" : "였다면서요?"), nop + "었다면서요?"));
		rows.AddRange(copuladialect(noun, bat, li));
		rows.AddRange(koreancopularows(noun, li));
		return rows;
	}

	private static List<ConjRow> anidarows(string word, int li) {
		var rows = new List<ConjRow>();
		var cur = "";
		string g(string key) => lp(li, GRP[key]);
		void add(string id, string name, string text) {
			rows.Add(new ConjRow(rowname(id, name, li), text, cur));
		}
		cur = g("base");
		add("dict", lp(li, NOTE["dict"]), word);
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
		cur = g("kp");
		add("kpa.hap", "敬语 아닙네다", "아닙네다");
		add("kpa.qhap", "敬语疑问 아닙네까", "아닙네까?");
		add("kpa.past", "过去 아니였습니다", "아니였습니다");
		add("kpa.yayo", "敬语 아니야요", "아니야요");
		cur = g("yb");
		add("yba.hap", "敬语 아님다", "아님다");
		add("yba.qhap", "敬语疑问 아님까", "아님까?");
		return rows;
	}
}
