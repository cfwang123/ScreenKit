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
	private const int JG_N = 4, JG_R = 8, JG_RM = 10, JG_M = 16, JG_B = 17, JG_BS = 18, JG_SS = 20, JG_H = 27;

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

	// 短使动只有固定说法。보이다 既是 보다 的被动也是使动，两行都列。
	private static readonly Dictionary<string, string> CAUSATIVE = new() {
		["먹다"] = "먹이다",
		["보다"] = "보이다",
		["앉다"] = "앉히다",
		["읽다"] = "읽히다",
		["웃다"] = "웃기다",
		["맡다"] = "맡기다",
		["벗다"] = "벗기다",
		["신다"] = "신기다",
		["알다"] = "알리다",
		["울다"] = "울리다",
		["놀다"] = "놀리다",
		["돌다"] = "돌리다",
		["자다"] = "재우다",
		["서다"] = "세우다",
		["타다"] = "태우다",
		["깨다"] = "깨우다",
		["죽다"] = "죽이다",
		["남다"] = "남기다",
		["입다"] = "입히다",
		["속다"] = "속이다",
		["끓다"] = "끓이다",
		["붙다"] = "붙이다",
		["숨다"] = "숨기다",
		["익다"] = "익히다",
		["높다"] = "높이다",
		["낮다"] = "낮추다",
		["넓다"] = "넓히다",
		["좁다"] = "좁히다",
		["밝다"] = "밝히다",
	};

	// 心理形容词才有 -아/어하다。크다 这类没有。
	private static readonly HashSet<string> EMOTION = new() {
		"좋다", "싫다", "슬프다", "기쁘다", "아프다", "예쁘다", "이쁘다", "무섭다", "부럽다", "밉다", "귀엽다",
		"안타깝다", "그립다", "외롭다", "부끄럽다", "재미없다",
	};

	// 词典形本身换词的尊敬，只在查原词时列出。
	private static readonly Dictionary<string, string> HONSUP = new() {
		["있다"] = "계시다 · 계세요",
		["먹다"] = "드시다 · 드세요 / 잡수시다 · 잡수세요",
		["자다"] = "주무시다 · 주무세요",
		["묻다"] = "여쭙다 · 여쭤요",
		["보다"] = "뵙다 · 봬요",
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
		["arch"] = new[] { "古代语", "고어", "Classical" },
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
		["ellip"] = new[] { "省略", "준말", "Contraction" },
		["talk"] = new[] { "口语", "구어", "Colloquial" },
		["role"] = new[] { "角色语", "역할어", "Character" },
		["circle"] = new[] { "小圈子", "은어", "In-group" },
		["rough"] = new[] { "粗俗", "속어", "Vulgar" },
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
		["wasgo"] = new[] { "本来要", "할 예정이었음", "was going to" },
		["pguess"] = new[] { "过去推测", "과거 추측", "past guess" },
		["ifpast"] = new[] { "当时如果", "과거 가정", "if (past)" },
		["nde"] = new[] { "现在转折", "현재 대조", "but (present)" },
		["before"] = new[] { "之前", "전", "before" },
		["after"] = new[] { "之后", "고 나서", "after" },
		["after2"] = new[] { "过去之后", "ㄴ 후에", "after (past)" },
		["when"] = new[] { "时候", "때", "when" },
		["during"] = new[] { "期间", "동안", "during" },
		["more"] = new[] { "越…越", "수록", "the more" },
		["stateas"] = new[] { "保持着", "채로", "as is" },
		["whileat"] = new[] { "顺便", "김에", "while at it" },
		["becgi"] = new[] { "因为", "기 때문에", "because" },
		["becwind"] = new[] { "由于", "바람에", "because of" },
		["becfault"] = new[] { "怪于", "탓에", "due to" },
		["becthanks"] = new[] { "多亏", "덕분에", "thanks to" },
		["geolkkeol"] = new[] { "大概", "걸요", "soft guess" },
		["tende"] = new[] { "料想", "텐데", "would be" },
		["riga"] = new[] { "不可能", "리가 없다", "no way" },
		["teullim"] = new[] { "肯定是", "틀림없다", "must be" },
		["moyang"] = new[] { "看样子", "모양이다", "looks like" },
		["deut"] = new[] { "似乎", "듯하다", "seems" },
		["suhae"] = new[] { "只好", "수밖에", "no choice" },
		["man"] = new[] { "值得", "만하다", "worth" },
		["pyeon"] = new[] { "偏向", "편이다", "tend to" },
		["swip"] = new[] { "容易", "기 쉽다", "easy to" },
		["eoryeop"] = new[] { "难于", "기 어렵다", "hard to" },
		["gon"] = new[] { "常常", "곤 하다", "habitually" },
		["beop"] = new[] { "理当", "법이다", "bound to" },
		["maryeon"] = new[] { "总是", "마련이다", "always" },
		["cheok"] = new[] { "假装", "척하다", "pretend" },
		["qquote"] = new[] { "疑问引用", "냐고", "quoted question" },
		["cquote"] = new[] { "命令引用", "라고", "quoted command" },
		["squote"] = new[] { "劝诱引用", "자고", "quoted suggestion" },
		["dquote"] = new[] { "定语引用", "다는", "quoted modifier" },
		["jana"] = new[] { "非敬语提醒", "반말 잖아", "casual reminder" },
		["geodeun"] = new[] { "非敬语解释", "반말 거든", "casual reason" },
		["lgeban"] = new[] { "非敬语承诺", "반말 약속", "casual promise" },
		["geojyo"] = new[] { "就是", "거죠", "you know" },
		["geonde"] = new[] { "情况是", "건데요", "the thing is" },
		["result"] = new[] { "结果状态", "결과", "resulting state" },
		["goon"] = new[] { "渐渐下去", "어 가다", "go on" },
		["comeon"] = new[] { "渐渐过来", "어 오다", "come to" },
		["naeda"] = new[] { "终于做成", "내다", "manage to" },
		["gonada"] = new[] { "做完之后", "고 나다", "after finishing" },
		["want3"] = new[] { "别人想", "싶어하다", "someone wants" },
		["gajigo"] = new[] { "所以", "가지고", "and so" },
		["gatgo"] = new[] { "所以缩略", "갖고", "and so (short)" },
		["look"] = new[] { "显得", "보이다", "looks" },
		["emot"] = new[] { "表现出", "어하다", "show the feeling" },
		["causlex"] = new[] { "使动词", "사동사", "causative verb" },
		["sup"] = new[] { "替换尊敬", "특수 존칭", "suppletive honorific" },
		["soseo"] = new[] { "祈请 소서", "소서", "beseech" },
		["saop"] = new[] { "极敬 사옵니다", "사옵니다", "archaic formal" },
		["naida"] = new[] { "极敬 나이다", "나이다", "archaic plain" },
		["rira"] = new[] { "将然 리라", "리라", "literary future" },
		["nira"] = new[] { "解释 니라", "니라", "literary explanation" },
		["mae"] = new[] { "文言原因", "매", "literary because" },
		["myeo"] = new[] { "文言同时", "며", "literary while" },
		["nora"] = new[] { "宣告 노라", "노라", "literary declare" },
		["iraneun"] = new[] { "叫做", "이라는", "called" },
		["inji"] = new[] { "是否", "인지", "whether" },
		["injul"] = new[] { "以为是", "인 줄", "thought that" },
		["igido"] = new[] { "也是", "이기도", "also is" },
		["imyeonseo"] = new[] { "既是", "이면서", "while being" },
		["idaga"] = new[] { "一度是", "이다가", "was, then" },
		["fullae"] = new[] { "未缩略", "본말", "uncontracted" },
		["geoda"] = new[] { "缩略 거다", "거다", "geoda" },
		["geotcon"] = new[] { "东西缩略", "건/걸/게", "thing (short)" },
		["geotfut"] = new[] { "将来东西", "ㄹ 거/걸/게", "future thing" },
		["geoya"] = new[] { "是…的", "거야", "geoya" },
		["geoyeyo"] = new[] { "是…的敬", "거예요", "geoyeyo" },
		["geoji"] = new[] { "是…的呀", "거지", "geoji" },
		["dae"] = new[] { "传闻短", "대", "dae" },
		["rae"] = new[] { "命令传闻", "래", "rae" },
		["jae"] = new[] { "劝诱传闻", "재", "jae" },
		["nyae"] = new[] { "疑问传闻", "냬", "nyae" },
		["rago"] = new[] { "口语打算", "ㄹ라고", "colloquial intent" },
		["qni"] = new[] { "疑问 니", "니", "ni" },
		["qnip"] = new[] { "过去疑问", "었니", "past ni" },
		["jiq"] = new[] { "确认 지", "지", "ji" },
		["jima2"] = new[] { "禁止 지 마", "지 마", "don't" },
		["lrae"] = new[] { "意愿短", "ㄹ래", "llae" },
		["lgeol"] = new[] { "推测短", "ㄹ걸", "lgeol" },
		["geodeunyo"] = new[] { "解释 거든요", "거든요", "geodeunyo" },
		["ndeyo"] = new[] { "转折 는데요", "는데요", "neundeyo" },
		["guna"] = new[] { "发觉 구나", "구나", "guna" },
		["gun"] = new[] { "发觉 군", "군", "gun" },
		["geol"] = new[] { "原来 걸", "걸", "geol" },
		["danikka"] = new[] { "强调", "다니까", "danikka" },
		["ranikka"] = new[] { "强调命令", "라니까", "ranikka" },
		["tryban"] = new[] { "试试", "봐", "try (casual)" },
		["gu"] = new[] { "并列短", "구", "gu" },
		["guyo"] = new[] { "并列短敬", "구요", "guyo" },
		["euro"] = new[] { "所以 므로", "므로", "meuro" },
		["euna"] = new[] { "书面转折", "으나", "euna" },
		["euni"] = new[] { "书面原因", "으니", "euni" },
		["doe"] = new[] { "虽则 되", "되", "doe" },
		["gie"] = new[] { "因为 기에", "기에", "gie" },
		["mieum"] = new[] { "名词化 음", "음", "nominal 음" },
		["gi"] = new[] { "名词化 기", "기", "nominal 기" },
		["neunji"] = new[] { "是否 는지", "는지", "neunji" },
		["neunjip"] = new[] { "过去是否", "었는지", "past neunji" },
		["lji"] = new[] { "将来是否", "ㄹ지", "lji" },
		["neunba"] = new[] { "鉴于 바", "바", "neunba" },
		["jira"] = new[] { "文言原因 지라", "지라", "jira" },
		["doda"] = new[] { "文言感叹", "도다", "doda" },
		["jieoda"] = new[] { "文言命令 지어다", "지어다", "jieoda" },
		["haeuuna"] = new[] { "书面转折 하였으나", "하였으나", "hayeoseuna" },
		["haeumyeo"] = new[] { "书面同时 하였으며", "하였으며", "hayeoseumyeo" },
		["haeumeuro"] = new[] { "书面原因 하였으므로", "하였으므로", "hayeoseumeuro" },
		["rini"] = new[] { "古文 리니", "리니", "rini" },
		["riroda"] = new[] { "古文 리로다", "리로다", "riroda" },
		["eotdoda"] = new[] { "古文 었도다", "었도다", "eotdoda" },
		["rio"] = new[] { "古文 리오", "리오", "rio" },
		["geoneul"] = new[] { "古文 거늘", "거늘", "geoneul" },
		["geoniwa"] = new[] { "古文 거니와", "거니와", "geoniwa" },
		["geonmaneun"] = new[] { "古文 건마는", "건마는", "geonmaneun" },
		["goja"] = new[] { "古文 고자", "고자", "goja" },
		["jindae"] = new[] { "古文 ㄹ진대", "ㄹ진대", "jindae" },
		["sera"] = new[] { "古文 ㄹ세라", "ㄹ세라", "sera" },
		["sae"] = new[] { "古文 ㄹ새", "ㄹ새", "sae" },
		["noni"] = new[] { "古文 노니", "노니", "noni" },
		["jinjeo"] = new[] { "古文 ㄹ진저", "ㄹ진저", "jinjeo" },
		["eura"] = new[] { "古文命令 으라", "으라", "eura" },
		["saopso"] = new[] { "古文祈请 사옵소서", "사옵소서", "saopsoseo" },
		["guryeo"] = new[] { "老人 구려", "구려", "guryeo" },
		["gumeon"] = new[] { "老人 구먼", "구먼", "gumeon" },
		["negeu"] = new[] { "老人 네그려", "네그려", "negeuryeo" },
		["aegyo"] = new[] { "撒娇 여/염", "여/염", "aegyo" },
		["nyang"] = new[] { "猫 냥", "냥", "nyang" },
		["neunyo"] = new[] { "古装疑问", "느뇨", "neunyo" },
		["riikka"] = new[] { "古装 리이까", "리이까", "riikka" },
		["saopka"] = new[] { "古装 사옵나이까", "사옵나이까", "saopnaikka" },
		["jirong"] = new[] { "孩子 지롱", "지롱", "jirong" },
		["yong"] = new[] { "网络 용", "용", "yong" },
		["sam"] = new[] { "网络 삼", "삼", "sam" },
		["sem"] = new[] { "网络 셈", "셈", "sem" },
		["dang"] = new[] { "网络 당", "당", "dang" },
		["geoim"] = new[] { "网络 거임", "거임", "geoim" },
		["imda"] = new[] { "撒娇 임다", "임다", "imda" },
		["nya"] = new[] { "粗问 냐", "냐", "nya" },
		["masem"] = new[] { "粗禁 마셈", "마셈", "masem" },
		["llla"] = new[] { "吓唬 ㄹ라", "ㄹ라", "lla" },
		["cheo"] = new[] { "脏强调 쳐", "쳐", "cheo" },
		["daeda"] = new[] { "一个劲儿", "대다", "daeda" },
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

	// 해요体末尾的 요 换成撒娇或网络用的 여、염、용
	private static string yoswap(string hey, string tail) => repend(hey, "요", tail);

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
		add("plup", "했었어요", past + "었어요");
		add("wasgo", "ㄹ 거였어요", rl + " 거였어요");
		add("pguess", "았겠어요", past + "겠어요");
		add("ifpast", "았더라면", past + "더라면");
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
			if (HONSUP.TryGetValue(word, out var sup)) add("sup", "替换尊敬", sup);
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
			add("after", "고 나서", jn(syls) + "고 나서");
			add("after2", "ㄴ 후에", nieunword(syls, irr) + " 후에");
			add("stateas", "ㄴ 채로", nieunword(syls, irr) + " 채로");
			add("whileat", "는 김에", neun(syls) + " 김에");
		}
		var nowmod = modnow(syls, irr, kind);
		add("nde", "는데", neundeform(syls, irr, kind, ""));
		add("before", "기 전에", jn(syls) + "기 전에");
		add("when", "ㄹ 때", rl + " 때");
		add("during", "는 동안", nowmod + " 동안");
		add("more", "ㄹ수록", rl + "수록");
		add("becgi", "기 때문에", jn(syls) + "기 때문에");
		add("becwind", "는 바람에", nowmod + " 바람에");
		add("becfault", "는 탓에", nowmod + " 탓에");
		add("becthanks", "는 덕분에", nowmod + " 덕분에");
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
		add("jana", "잖아", jn(syls) + "잖아");
		add("geodeun", "거든", jn(syls) + "거든");
		add("geojyo", "는 거죠", nowmod + " 거죠");
		add("geonde", "는 건데요", nowmod + " 건데요");
		if (verb) add("lgeban", "ㄹ게", rl + "게");
		if (irr == "hada") {
			var hy = stem.Substring(0, stem.Length - 1) + "하여";
			cur = g("wrt");
			add("hyeo", "하여", hy);
			add("hyeot", "하였다", stem.Substring(0, stem.Length - 1) + "하였다");
			add("hyeos", "하여서", hy + "서");
		}
		// 文言：和口语同形的不单列。소서·사옵니다·나이다·노라 只给动词。
		cur = g("wrt");
		if (verb) {
			add("soseo", "소서", pluseu(st, "소서", true));
			add("saop", "사옵니다", saop(syls));
			add("naida", "나이다", dropr(syls, "나이다"));
			add("nora", "노라", dropr(syls, "노라"));
		}
		add("rira", "리라", pluseu(st, "리라", true));
		add("nira", "니라", pluseu(st, "니라", true));
		add("mae", "매", pluseu(st, "매", true));
		add("myeo", "며", pluseu(st, "며", false));
		add("euro", "므로", pluseu(st, "므로", true));
		add("euna", "으나", pluseu(st, "나", true));
		add("euni", "으니", pluseu(st, "니", true));
		add("doe", "되", pluseu(st, "되", true));
		add("gie", "기에", jn(syls) + "기에");
		add("mieum", "음", mieum(syls, irr));
		add("gi", "기", jn(syls) + "기");
		add("neunji", "는지", kind == "a" && !eobs ? nieunword(syls, irr) + "지" : dropr(syls, "는지"));
		add("neunjip", "었는지", past + "는지");
		add("lji", "ㄹ지", rl + "지");
		add("neunba", "바", nowmod + "바");
		add("jira", "지라", verb ? nieunword(syls, irr) + "지라 / " + dropr(syls, "는지라") : (kind == "a" && !eobs ? nieunword(syls, irr) + "지라" : dropr(syls, "는지라")));
		add("doda", "도다", verb ? neun(syls) + "도다" : jn(syls) + "도다");
		add("jieoda", "지어다", rl + "지어다");
		if (irr == "hada") {
			var hw = stem.Substring(0, stem.Length - 1);
			add("haeuuna", "하였으나", hw + "하였으나");
			add("haeumyeo", "하였으며", hw + "하였으며");
			add("haeumeuro", "하였으므로", hw + "하였으므로");
		}
		// 古代语：ㄹ 词干的 건마는 作 련마는；노니 前 ㄹ 脱落。으라 只给动词，ㄹ 词干保留 ㄹ。
		cur = g("arch");
		add("rini", "리니", pluseu(st, "리니", true));
		add("riroda", "리로다", pluseu(st, "리로다", true));
		add("eotdoda", "었도다", irr == "hada" ? stem.Substring(0, stem.Length - 1) + "하였도다" : past + "도다");
		add("rio", "리오", pluseu(st, "리오", true));
		add("geoneul", "거늘", jn(syls) + "거늘");
		add("geoniwa", "거니와", jn(syls) + "거니와");
		add("geonmaneun", "건마는", geonman(syls));
		add("goja", "고자", jn(syls) + "고자");
		add("jindae", "ㄹ진대", rl + "진대");
		add("sera", "ㄹ세라", rl + "세라");
		add("sae", "ㄹ새", rl + "새");
		add("noni", "노니", dropr(syls, "노니"));
		add("jinjeo", "ㄹ진저", rl + "진저");
		if (verb) add("eura", "으라", pluseu(st, "라", false));
		add("saopso", "사옵소서", repend(saop(syls), "니다", "소서"));
		// 补助动词：试、做完、放着、预先、为别人、最终，以及结果、渐变、缩略
		cur = g("aux");
		if (verb) {
			add("try", "보다", ba + " 봤어요");
			add("done", "버리다", ba + " 버렸어요");
			add("leave", "놓다", ba + " 놨어요");
			add("prep", "두다", ba + " 뒀어요");
			add("benef", "주다", ba + " 줬어요");
			add("hbenef", "드리다", ba + " 드렸어요");
			add("endup", "고 말다", jn(syls) + "고 말았어요");
			add("result", "아 있다", ba + " 있어요");
			add("naeda", "아 내다", ba + " 냈어요");
			add("gonada", "고 나다", jn(syls) + "고 났어요");
			add("want3", "고 싶어하다", jn(syls) + "고 싶어해요");
		}
		if (verb || adj) {
			add("goon", "아 가다", ba + " 가요");
			add("comeon", "아 오다", ba + " 와요");
			add("gajigo", "아 가지고", ba + " 가지고");
			add("gatgo", "아 갖고", ba + " 갖고");
		}
		// 态：使动、变成、形容词的「变得」、显得、心理形容词的 어하다、固定说法的被动和使动
		cur = g("voice");
		add("caus", "게 하다", jn(syls) + "게 했어요");
		add("turn", "게 되다", jn(syls) + "게 되었어요");
		if (adj) add("becom", "아/어지다", ba + "졌어요");
		if (adj) add("look", "아 보이다", ba + " 보여요");
		if (adj && EMOTION.Contains(word)) add("emot", "아하다", $"{ba}하다 · {ba}해요");
		add("pass", "被动", passivetext(word));
		add("causlex", "使动", causetext(word));
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
			add("cquote", "라고", pluseu(st, "라고", true) + " / " + jn(syls) + "지 말라고");
			add("squote", "자고", jn(syls) + "자고 / " + jn(syls) + "지 말자고");
		}
		add("geolkkeol", "ㄹ걸요", rl + "걸요");
		add("tende", "ㄹ 텐데", rl + " 텐데요");
		add("riga", "ㄹ 리가 없다", rl + " 리가 없어요");
		add("teullim", "는 게 틀림없다", nowmod + " 게 틀림없어요");
		add("moyang", "는 모양이다", nowmod + " 모양이에요");
		add("deut", "는 듯하다", nowmod + " 듯해요");
		add("suhae", "ㄹ 수밖에 없다", rl + " 수밖에 없어요");
		add("man", "ㄹ 만하다", rl + " 만해요");
		add("pyeon", "는 편이다", nowmod + " 편이에요");
		add("swip", "기 쉽다", jn(syls) + "기 쉬워요");
		add("eoryeop", "기 어렵다", jn(syls) + "기 어려워요");
		add("gon", "곤 하다", jn(syls) + "곤 해요");
		add("beop", "는 법이다", nowmod + " 법이에요");
		add("maryeon", "기 마련이다", jn(syls) + "기 마련이에요");
		add("cheok", "는 척하다", nowmod + " 척해요");
		add("qquote", "냐고", quotenya(syls, irr, kind) + " / " + past + "느냐고");
		add("dquote", "다는", repend(plainnow, "다", "다는") + " / " + past + "다는");
		// 省略：것 的缩略和带空格的「将来的东西」与承诺形 ㄹ게 分开
		var full = fullpolite(syls, irr);
		cur = g("ellip");
		if (full.Length > 0) add("fullae", "未缩略", full);
		if (irr == "hada") add("fullae", "未缩略", stem.Substring(0, stem.Length - 1) + "하여요 · " + stem.Substring(0, stem.Length - 1) + "하였어요");
		add("geoda", "거다", rl + " 거다");
		add("geotcon", "건/걸/게", nowmod + " 건 / " + nowmod + " 걸 / " + nowmod + " 게");
		add("geotfut", "ㄹ 거", rl + " 거 / " + rl + " 걸 / " + rl + " 게");
		add("geoya", "거야", nowmod + " 거야");
		add("geoyeyo", "거예요", nowmod + " 거예요");
		add("geoji", "거지", nowmod + " 거지");
		add("dae", "대", repend(plainnow, "다", "대"));
		if (verb) {
			add("rae", "래", pluseu(st, "래", false));
			add("jae", "재", jn(syls) + "재");
			add("ranikka", "라니까", pluseu(st, "라니까", false));
		}
		add("nyae", "냬", kind == "a" && !eobs ? pluseu(st, "냬", true) : dropr(syls, "냬"));
		add("rago", "ㄹ라고", rl + "라고");
		// 口语。发觉：动词用 는구나，形容词和 있다 用 구나
		var gutail = verb ? neun(syls) : jn(syls);
		cur = g("talk");
		add("qni", "니", dropr(syls, "니?"));
		add("qnip", "었니", past + "니?");
		add("jiq", "지", jn(syls) + "지?");
		if (verb || adj || itda) add("jima2", "지 마", jn(syls) + "지 마");
		add("lrae", "ㄹ래", rl + "래");
		add("lgeol", "ㄹ걸", rl + "걸");
		add("geodeunyo", "거든요", jn(syls) + "거든요");
		add("ndeyo", "는데요", neundeform(syls, irr, kind, "요"));
		add("guna", "구나", gutail + "구나");
		add("gun", "군", gutail + "군");
		add("geol", "걸", nowmod + "걸");
		add("danikka", "다니까", repend(plainnow, "다", "다니까"));
		if (verb || adj || itda) add("tryban", "봐", ba + " 봐");
		add("gu", "구", jn(syls) + "구");
		add("guyo", "구요", jn(syls) + "구요");
		// 角色语、网络小圈子、粗俗词尾。구려/구먼 与 구나 同接；네그려、삼、셈 在 ㄹ 后脱落。
		cur = g("role");
		add("guryeo", "구려", gutail + "구려");
		add("gumeon", "구먼", gutail + "구먼");
		add("negeu", "네그려", dropr(syls, "네그려"));
		add("aegyo", "여/염", yoswap(hey, "여") + " / " + yoswap(hey, "염"));
		add("nyang", "냥", repend(plainnow, "다", "다냥"));
		add("neunyo", "느뇨", kind == "a" && !eobs ? pluseu(st, "뇨", true) : dropr(syls, "느뇨"));
		add("riikka", "리이까", pluseu(st, "리이까", true));
		add("saopka", "사옵나이까", repend(saop(syls), "니다", "나이까"));
		add("jirong", "지롱", past + "지롱");
		cur = g("circle");
		add("yong", "용", yoswap(hey, "용"));
		add("sam", "삼", dropr(syls, "삼"));
		add("sem", "셈", dropr(syls, "셈"));
		add("dang", "당", repend(word, "다", "당") + " / " + past + "당");
		add("geoim", "거임", nowmod + " 거임");
		add("imda", "임다", mieum(syls, irr) + "다");
		cur = g("rough");
		add("nya", "냐", dropr(syls, "냐?") + " / " + past + "냐?");
		if (verb || adj || itda) add("masem", "마셈", jn(syls) + "지 마셈");
		add("llla", "ㄹ라", rl + "라");
		if (verb || adj || itda) add("cheo", "쳐", "쳐" + ba);
		if (verb) add("daeda", "대다", ba + " 대");
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

	// 固定的被动、使动：词典形和 해体形
	private static string lexhey(string pv) {
		var all = decall(pv);
		if (all == null || all.Count < 2 || pv[pv.Length - 1] != '다') return pv;
		var syls = all.GetRange(0, all.Count - 1);
		var stem = pv.Substring(0, pv.Length - 1);
		var irr = classify(stem, syls);
		return $"{pv} · {spokenae(syls, irr, false)}요";
	}

	private static string passivetext(string word) =>
		PASSIVE.TryGetValue(word, out var pv) ? lexhey(pv) : "";

	private static string causetext(string word) =>
		CAUSATIVE.TryGetValue(word, out var cv) ? lexhey(cv) : "";

	// 现在定语：动词、있다、없다 用 는，其余形容词用 ㄴ
	private static string modnow(List<KoSyl> syls, string irr, string kind) {
		if (kind == "a" && !endseobs(syls)) return nieunword(syls, irr);
		return neun(syls);
	}

	// 现在转折：形容词 ㄴ데，动词和 있다/없다 는데
	private static string neundeform(List<KoSyl> syls, string irr, string kind, string tail) {
		if (kind == "a" && !endseobs(syls)) return nieunword(syls, irr) + "데" + tail;
		return dropr(syls, "는데" + tail);
	}

	// 疑问引用：形容词 (으)냐고，动词和 있다/없다 느냐고
	private static string quotenya(List<KoSyl> syls, string irr, string kind) {
		if (kind == "a" && !endseobs(syls)) return pluseu(eustem(syls, irr), "냐고", true);
		return dropr(syls, "느냐고");
	}

	// 건마는 / 련마는：ㄹ 词干保留 ㄹ 再接 련마는，其余接 건마는
	private static string geonman(List<KoSyl> syls) {
		var s = cp(syls);
		var last = s[s.Count - 1];
		return jn(s) + (last.Jong == JG_R ? "련마는" : "건마는");
	}

	// 하소서体 -(사)옵니다：开音节和 ㄹ 用 옵니다，其余闭音节用 사옵니다。ㄷ 在辅音前不变。
	private static string saop(List<KoSyl> syls) {
		var s = cp(syls);
		var last = s[s.Count - 1];
		if (last.Jong == JG_R) last.Jong = 0;
		return jn(s) + (last.Jong == 0 ? "옵니다" : "사옵니다");
	}

	// 名词化 -음。闭音节加 음，开音节收 ㅁ，ㄹ 收成 ㄻ。不规则先按接 으 的词干变。
	private static string mieum(List<KoSyl> syls, string irr) {
		var st = eustem(syls, irr);
		var s = cp(st.S);
		if (st.Eu) return jn(s) + "음";
		var last = s[s.Count - 1];
		if (last.Jong == JG_R) {
			last.Jong = JG_RM;
			return jn(s);
		}
		if (last.Jong == 0) {
			last.Jong = JG_M;
			return jn(s);
		}
		return jn(s) + "음";
	}

	// ㅏ/ㅓ 没有并进前一音节的说法：보아요、주어요、되어요、쓰어요。和缩略不同才列出，并带上过去。
	private static string fullpolite(List<KoSyl> syls, string irr) {
		if (irr != "reg" && irr != "eu") return "";
		var last = syls[syls.Count - 1];
		if (last.Jong != 0) return "";
		string yo;
		if (irr == "eu") {
			if (syls.Count != 1) return "";
			yo = jn(syls) + "어요";
		} else if (last.Jung == O) yo = jn(syls) + "아요";
		else if (last.Jung == U || last.Jung == OE || last.Jung == I) yo = jn(syls) + "어요";
		else return "";
		if (yo == ae(syls, irr) + "요") return "";
		return yo + " · " + fullpast(yo);
	}

	private static string fullpast(string yo) {
		if (yo.EndsWith("아요", StringComparison.Ordinal)) return yo.Substring(0, yo.Length - 2) + "았어요";
		if (yo.EndsWith("어요", StringComparison.Ordinal)) return yo.Substring(0, yo.Length - 2) + "었어요";
		return "";
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
		add("plup", "었었어요", copart(noun, bat, "었었어요", "였었어요"));
		add("wasgo", "일 거였어요", noun + "일 거였어요");
		add("pguess", "었겠어요", copart(noun, bat, "었겠어요", "였겠어요"));
		add("ifpast", "었더라면", copart(noun, bat, "었더라면", "였더라면"));
		cur = g("fut");
		add("fhey", "ㄹ 거예요", noun + "일 거예요");
		add("fhap", "ㄹ 겁니다", noun + "일 겁니다");
		add("fplain", "ㄹ 것이다", noun + "일 것이다");
		cur = g("con");
		add("myeon", "면", copart(noun, bat, "면", "면"));
		add("go", "고", copart(noun, bat, "고", "고"));
		add("jiman", "지만", copart(noun, bat, "지만", "지만"));
		add("ji", "지", copart(noun, bat, "지", "지"));
		add("nde", "인데", noun + "인데");
		add("before", "기 전에", noun + "이기 전에");
		add("when", "ㄹ 때", noun + "일 때");
		add("during", "인 동안", noun + "인 동안");
		add("more", "일수록", noun + "일수록");
		add("becgi", "기 때문에", noun + "이기 때문에");
		add("igido", "이기도", noun + "이기도 해요");
		add("imyeonseo", "이면서", copart(noun, bat, "면서", "면서"));
		add("idaga", "이다가", copart(noun, bat, "다가", "다가"));
		cur = g("mod");
		add("modn", "ㄴ", noun + "인");
		add("modf", "ㄹ", noun + "일");
		add("deon", "던", copart(noun, bat, "던", "던"));
		add("atd", "았던", copart(noun, bat, "었던", "였던"));
		add("iraneun", "이라는", bat ? noun + "이라는" : noun + "라는");
		add("inji", "인지", noun + "인지");
		add("injul", "인 줄", noun + "인 줄 알아요");
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
		add("geolkkeol", "일걸요", noun + "일걸요");
		add("tende", "일 텐데", noun + "일 텐데요");
		add("riga", "일 리가 없다", noun + "일 리가 없어요");
		add("teullim", "인 게 틀림없다", noun + "인 게 틀림없어요");
		add("moyang", "인 모양이다", noun + "인 모양이에요");
		add("deut", "인 듯하다", noun + "인 듯해요");
		add("pyeon", "인 편이다", noun + "인 편이에요");
		add("cheok", "인 척하다", noun + "인 척해요");
		add("qquote", "냐고", copart(noun, bat, "냐고", "냐고") + " / " + copart(noun, bat, "었느냐고", "였느냐고"));
		add("dquote", "다는", (bat ? noun + "이라는" : noun + "라는") + " / " + (bat ? noun + "이었다는" : noun + "였다는"));
		cur = g("ext");
		add("jana", "잖아", copart(noun, bat, "잖아", "잖아"));
		add("geodeun", "거든", copart(noun, bat, "거든", "거든"));
		add("geojyo", "인 거죠", noun + "인 거죠");
		add("geonde", "인 건데요", noun + "인 건데요");
		cur = g("wrt");
		add("rira", "이리라", noun + "이리라");
		add("nira", "이니라", noun + "이니라");
		add("myeo", "이며", copart(noun, bat, "며", "며"));
		add("saop", "이옵니다", copart(noun, bat, "옵니다", "옵니다"));
		add("naida", "이나이다", copart(noun, bat, "나이다", "나이다"));
		add("euro", "므로", copart(noun, bat, "므로", "므로"));
		add("euna", "으나", copart(noun, bat, "나", "나"));
		add("euni", "으니", copart(noun, bat, "니", "니"));
		add("doe", "되", copart(noun, bat, "되", "되"));
		add("gie", "기에", noun + "이기에");
		add("mieum", "음", noun + "임");
		add("gi", "기", noun + "이기");
		add("neunjip", "었는지", copart(noun, bat, "었는지", "였는지"));
		add("lji", "ㄹ지", noun + "일지");
		add("neunba", "바", noun + "인바");
		add("jira", "지라", noun + "인지라");
		add("doda", "도다", copart(noun, bat, "로다", "로다"));
		add("jieoda", "지어다", noun + "일지어다");
		cur = g("arch");
		add("rini", "리니", noun + "이리니");
		add("riroda", "리로다", noun + "이리로다");
		add("eotdoda", "었도다", copart(noun, bat, "었도다", "였도다"));
		add("rio", "리오", noun + "이리오");
		add("geoneul", "거늘", copart(noun, bat, "거늘", "거늘"));
		add("geoniwa", "거니와", copart(noun, bat, "거니와", "거니와"));
		add("geonmaneun", "건마는", copart(noun, bat, "건마는", "건마는"));
		add("goja", "고자", copart(noun, bat, "고자", "고자"));
		add("jindae", "ㄹ진대", noun + "일진대");
		add("sera", "ㄹ세라", noun + "일세라");
		add("sae", "ㄹ새", noun + "일새");
		add("noni", "노니", copart(noun, bat, "노니", "노니"));
		add("jinjeo", "ㄹ진저", noun + "일진저");
		add("saopso", "사옵소서", copart(noun, bat, "옵소서", "옵소서"));
		cur = g("ellip");
		add("geoda", "거다", noun + "일 거다");
		add("geotcon", "건/걸/게", noun + "인 건 / " + noun + "인 걸 / " + noun + "인 게");
		add("geotfut", "ㄹ 거", noun + "일 거 / " + noun + "일 걸 / " + noun + "일 게");
		add("geoya", "거야", noun + "인 거야");
		add("geoyeyo", "거예요", noun + "인 거예요");
		add("geoji", "거지", noun + "인 거지");
		add("dae", "대", copart(noun, bat, "래", "래"));
		add("nyae", "냬", copart(noun, bat, "냬", "냬"));
		add("rago", "ㄹ라고", noun + "일라고");
		cur = g("talk");
		add("qni", "니", copart(noun, bat, "니?", "니?"));
		add("qnip", "었니", copart(noun, bat, "었니?", "였니?"));
		add("jiq", "지", copart(noun, bat, "지?", "지?"));
		add("lrae", "ㄹ래", noun + "일래");
		add("lgeol", "ㄹ걸", noun + "일걸");
		add("geodeunyo", "거든요", copart(noun, bat, "거든요", "거든요"));
		add("ndeyo", "는데요", noun + "인데요");
		add("guna", "구나", copart(noun, bat, "구나", "구나"));
		add("gun", "군", copart(noun, bat, "군", "군"));
		add("geol", "걸", noun + "인걸");
		add("danikka", "다니까", copart(noun, bat, "라니까", "라니까"));
		add("gu", "구", copart(noun, bat, "구", "구"));
		add("guyo", "구요", copart(noun, bat, "구요", "구요"));
		var nhey = copart(noun, bat, "에요", "예요");
		cur = g("role");
		add("guryeo", "구려", copart(noun, bat, "구려", "구려"));
		add("gumeon", "구먼", copart(noun, bat, "구먼", "구먼"));
		add("negeu", "네그려", copart(noun, bat, "네그려", "네그려"));
		add("aegyo", "여/염", yoswap(nhey, "여") + " / " + yoswap(nhey, "염"));
		add("nyang", "냥", copart(noun, bat, "다냥", "다냥"));
		add("neunyo", "느뇨", copart(noun, bat, "뇨", "뇨"));
		add("riikka", "리이까", copart(noun, bat, "리이까", "리이까"));
		add("saopka", "사옵나이까", copart(noun, bat, "옵나이까", "옵나이까"));
		add("jirong", "지롱", copart(noun, bat, "었지롱", "였지롱"));
		cur = g("circle");
		add("yong", "용", yoswap(nhey, "용"));
		add("sam", "삼", copart(noun, bat, "삼", "삼"));
		add("sem", "셈", copart(noun, bat, "셈", "셈"));
		add("dang", "당", copart(noun, bat, "당", "당") + " / " + copart(noun, bat, "었당", "였당"));
		add("geoim", "거임", noun + "인 거임");
		add("imda", "임다", noun + "임다");
		cur = g("rough");
		add("nya", "냐", copart(noun, bat, "냐?", "냐?") + " / " + copart(noun, bat, "었냐?", "였냐?"));
		add("llla", "ㄹ라", noun + "일라");
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
		add("plup", "었었어요", "아니었었어요");
		add("wasgo", "일 거였어요", "아닐 거였어요");
		add("pguess", "었겠어요", "아니었겠어요");
		add("ifpast", "었더라면", "아니었더라면");
		cur = g("fut");
		add("fhey", "ㄹ 거예요", "아닐 거예요");
		add("fhap", "ㄹ 겁니다", "아닐 겁니다");
		add("fplain", "ㄹ 것이다", "아닐 것이다");
		cur = g("con");
		add("myeon", "면", "아니면");
		add("go", "고", "아니고");
		add("jiman", "지만", "아니지만");
		add("ji", "지", "아니지");
		add("nde", "인데", "아닌데");
		add("before", "기 전에", "아니기 전에");
		add("when", "ㄹ 때", "아닐 때");
		add("during", "인 동안", "아닌 동안");
		add("more", "일수록", "아닐수록");
		add("becgi", "기 때문에", "아니기 때문에");
		add("igido", "이기도", "아니기도 해요");
		cur = g("mod");
		add("modn", "ㄴ", "아닌");
		add("modf", "ㄹ", "아닐");
		add("deon", "던", "아니던");
		add("atd", "았던", "아니었던");
		cur = g("ext");
		add("jana", "잖아", "아니잖아");
		add("geodeun", "거든", "아니거든");
		add("geojyo", "인 거죠", "아닌 거죠");
		add("geonde", "인 건데요", "아닌 건데요");
		cur = g("mood");
		add("geolkkeol", "ㄹ걸요", "아닐걸요");
		add("tende", "ㄹ 텐데", "아닐 텐데요");
		add("riga", "ㄹ 리가 없다", "아닐 리가 없어요");
		add("teullim", "는 게 틀림없다", "아닌 게 틀림없어요");
		add("moyang", "는 모양이다", "아닌 모양이에요");
		add("deut", "는 듯하다", "아닌 듯해요");
		add("pyeon", "는 편이다", "아닌 편이에요");
		add("cheok", "는 척하다", "아닌 척해요");
		add("qquote", "냐고", "아니냐고 / 아니었느냐고");
		add("dquote", "다는", "아니라는 / 아니었다는");
		cur = g("wrt");
		add("rira", "리라", "아니리라");
		add("nira", "니라", "아니니라");
		add("myeo", "며", "아니며");
		add("euro", "므로", "아니므로");
		add("euna", "으나", "아니나");
		add("euni", "으니", "아니니");
		add("doe", "되", "아니되");
		add("gie", "기에", "아니기에");
		add("mieum", "음", "아님");
		add("gi", "기", "아니기");
		add("neunji", "는지", "아닌지");
		add("neunjip", "었는지", "아니었는지");
		add("lji", "ㄹ지", "아닐지");
		add("neunba", "바", "아닌바");
		add("jira", "지라", "아닌지라");
		add("doda", "도다", "아니로다");
		add("jieoda", "지어다", "아닐지어다");
		cur = g("arch");
		add("rini", "리니", "아니리니");
		add("riroda", "리로다", "아니리로다");
		add("eotdoda", "었도다", "아니었도다");
		add("rio", "리오", "아니리오");
		add("geoneul", "거늘", "아니거늘");
		add("geoniwa", "거니와", "아니거니와");
		add("geonmaneun", "건마는", "아니건마는");
		add("goja", "고자", "아니고자");
		add("jindae", "ㄹ진대", "아닐진대");
		add("sera", "ㄹ세라", "아닐세라");
		add("sae", "ㄹ새", "아닐새");
		add("noni", "노니", "아니노니");
		add("jinjeo", "ㄹ진저", "아닐진저");
		add("saopso", "사옵소서", "아니옵소서");
		cur = g("ellip");
		add("geoda", "거다", "아닐 거다");
		add("geotcon", "건/걸/게", "아닌 건 / 아닌 걸 / 아닌 게");
		add("geotfut", "ㄹ 거", "아닐 거 / 아닐 걸 / 아닐 게");
		add("geoya", "거야", "아닌 거야");
		add("geoyeyo", "거예요", "아닌 거예요");
		add("geoji", "거지", "아닌 거지");
		add("dae", "대", "아니래");
		add("nyae", "냬", "아니냬");
		add("rago", "ㄹ라고", "아닐라고");
		cur = g("talk");
		add("qni", "니", "아니니?");
		add("qnip", "었니", "아니었니?");
		add("jiq", "지", "아니지?");
		add("lrae", "ㄹ래", "아닐래");
		add("lgeol", "ㄹ걸", "아닐걸");
		add("geodeunyo", "거든요", "아니거든요");
		add("ndeyo", "는데요", "아닌데요");
		add("guna", "구나", "아니구나");
		add("gun", "군", "아니군");
		add("geol", "걸", "아닌걸");
		add("danikka", "다니까", "아니라니까");
		add("gu", "구", "아니구");
		add("guyo", "구요", "아니구요");
		cur = g("role");
		add("guryeo", "구려", "아니구려");
		add("gumeon", "구먼", "아니구먼");
		add("negeu", "네그려", "아니네그려");
		add("aegyo", "여/염", "아니에여 / 아니에염");
		add("nyang", "냥", "아니다냥");
		add("neunyo", "느뇨", "아니뇨");
		add("riikka", "리이까", "아니리이까");
		add("saopka", "사옵나이까", "아니옵나이까");
		add("jirong", "지롱", "아니었지롱");
		cur = g("circle");
		add("yong", "용", "아니에용");
		add("sam", "삼", "아니삼");
		add("sem", "셈", "아니셈");
		add("dang", "당", "아니당 / 아니었당");
		add("geoim", "거임", "아닌 거임");
		add("imda", "임다", "아님다");
		cur = g("rough");
		add("nya", "냐", "아니냐? / 아니었냐?");
		add("llla", "ㄹ라", "아닐라");
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
