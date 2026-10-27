using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>
/// 英语词形变化：动词的十六种时态、否定、疑问、被动、情态、虚拟、祈使、附加疑问、
/// 使役与地区变体，形容词的比较级和名词的复数。对照 Web/tools/u-everb.js 的 build()。
/// 纯函数：不读文件、不联网、无可变静态状态。
/// </summary>
static class EnConj {
	// [原形, 过去式, 过去分词, 中文释义, 英文释义]
	static readonly string[][] IR = {
		new[] { "be", "was / were", "been", "是；存在", "be" },
		new[] { "have", "had", "had", "有", "have" },
		new[] { "do", "did", "done", "做", "do" },
		new[] { "go", "went", "gone", "去；走", "go" },
		new[] { "come", "came", "come", "来", "come" },
		new[] { "get", "got", "got / gotten", "得到；变得", "get" },
		new[] { "make", "made", "made", "做；制造", "make" },
		new[] { "take", "took", "taken", "拿；取", "take" },
		new[] { "see", "saw", "seen", "看见", "see" },
		new[] { "give", "gave", "given", "给", "give" },
		new[] { "write", "wrote", "written", "写", "write" },
		new[] { "speak", "spoke", "spoken", "说话", "speak" },
		new[] { "begin", "began", "begun", "开始", "begin" },
		new[] { "run", "ran", "run", "跑；经营", "run" },
		new[] { "swim", "swam", "swum", "游泳", "swim" },
		new[] { "teach", "taught", "taught", "教", "teach" },
		new[] { "buy", "bought", "bought", "买", "buy" },
		new[] { "think", "thought", "thought", "想；认为", "think" },
		new[] { "lie", "lay", "lain", "躺；卧", "lie down" }
	};

	static readonly Dictionary<string, string[]> LAB = new() {
		["title"] = new[] { "英语词形变化", "English word forms" },
		["hint"] = new[] {
			"输入英语动词原形、形容词或名词，类型默认自动：先查不规则的动词、形容词、名词表，再按词尾判断，认不出时按动词处理，也可以改类型。动词列基本形式、十六种时态、否定、疑问、被动、情态、虚拟、祈使、附加疑问、使役和地区变体；形容词列比较级、最高级、比较句型、副词和名词形式；名词列复数、冠词、所有格和主谓一致。斜杠两边是不同写法，表里用第一个。",
			"Enter a verb, adjective or noun. Auto checks the irregular verb, adjective and noun tables, then the ending; if unsure it treats the word as a verb, and you can change the type. Verbs list the basic forms, sixteen tenses, negatives, questions, passive, modals, subjunctive, imperative, tag questions, causative and regional variants. Adjectives list comparative and superlative forms, comparison patterns, the adverb and the noun. Nouns list plural, articles, possessives and subject-verb agreement. Slashes separate variants; the tables use the first."
		},
		["word"] = new[] { "单词", "Word" },
		["ph"] = new[] { "动词原形、形容词或名词，如 walk、beautiful、child", "A verb, adjective or noun, e.g. walk, beautiful, child" },
		["reg"] = new[] { "规则动词", "Regular verb" },
		["irr"] = new[] { "不规则动词", "Irregular verb" },
		["bad"] = new[] { "只支持一个英语单词，可带小品词，如 walk、beautiful、child", "One English word, with an optional particle, e.g. walk, beautiful or child" },
		["modal"] = new[] { "情态动词没有这些时态变化", "Modal verbs have no such tense forms" },
		["from"] = new[] { "由 %s 识别", "from %s" },
		["be"] = new[] { "be 的进行时、完成进行时和被动语态极少用", "be rarely uses continuous, perfect continuous or passive forms" },
		["g_form"] = new[] { "基本形式", "Basic forms" },
		["g_pres"] = new[] { "现在时", "Present" },
		["g_past"] = new[] { "过去时", "Past" },
		["g_fut"] = new[] { "将来时", "Future" },
		["g_fip"] = new[] { "过去将来时", "Future in the past" },
		["g_neg"] = new[] { "否定式", "Negative" },
		["g_q"] = new[] { "疑问式", "Questions" },
		["g_pas"] = new[] { "被动语态（及物动词）", "Passive (transitive)" },
		["g_oth"] = new[] { "其他形式", "Other forms" },
		["f_base"] = new[] { "原形", "Base form" },
		["f_s3"] = new[] { "第三人称单数", "3rd person singular" },
		["f_ing"] = new[] { "现在分词", "Present participle" },
		["f_past"] = new[] { "过去式", "Past" },
		["f_pp"] = new[] { "过去分词", "Past participle" },
		["t_pres"] = new[] { "一般现在时", "Simple present" },
		["t_pcont"] = new[] { "现在进行时", "Present continuous" },
		["t_ppf"] = new[] { "现在完成时", "Present perfect" },
		["t_ppc"] = new[] { "现在完成进行时", "Present perfect continuous" },
		["t_past"] = new[] { "一般过去时", "Simple past" },
		["t_pastc"] = new[] { "过去进行时", "Past continuous" },
		["t_pastp"] = new[] { "过去完成时", "Past perfect" },
		["t_pastpc"] = new[] { "过去完成进行时", "Past perfect continuous" },
		["t_fut"] = new[] { "一般将来时", "Simple future" },
		["t_futc"] = new[] { "将来进行时", "Future continuous" },
		["t_futp"] = new[] { "将来完成时", "Future perfect" },
		["t_futpc"] = new[] { "将来完成进行时", "Future perfect continuous" },
		["t_fips"] = new[] { "一般过去将来时", "Simple future in the past" },
		["t_fipc"] = new[] { "过去将来进行时", "Future in the past continuous" },
		["t_fipp"] = new[] { "过去将来完成时", "Future in the past perfect" },
		["t_fippc"] = new[] { "过去将来完成进行时", "Future in the past perfect continuous" },
		["n_pres"] = new[] { "一般现在时否定", "Simple present negative" },
		["n_past"] = new[] { "一般过去时否定", "Simple past negative" },
		["n_pcont"] = new[] { "现在进行时否定", "Present continuous negative" },
		["n_ppf"] = new[] { "现在完成时否定", "Present perfect negative" },
		["n_fut"] = new[] { "一般将来时否定", "Simple future negative" },
		["q_pres"] = new[] { "一般现在时疑问", "Simple present question" },
		["q_past"] = new[] { "一般过去时疑问", "Simple past question" },
		["q_pcont"] = new[] { "现在进行时疑问", "Present continuous question" },
		["q_ppf"] = new[] { "现在完成时疑问", "Present perfect question" },
		["q_fut"] = new[] { "一般将来时疑问", "Simple future question" },
		["p_pres"] = new[] { "被动 · 一般现在时", "Passive · simple present" },
		["p_past"] = new[] { "被动 · 一般过去时", "Passive · simple past" },
		["p_pcont"] = new[] { "被动 · 现在进行时", "Passive · present continuous" },
		["p_pastc"] = new[] { "被动 · 过去进行时", "Passive · past continuous" },
		["p_ppf"] = new[] { "被动 · 现在完成时", "Passive · present perfect" },
		["p_pastp"] = new[] { "被动 · 过去完成时", "Passive · past perfect" },
		["p_fut"] = new[] { "被动 · 一般将来时", "Passive · simple future" },
		["p_futp"] = new[] { "被动 · 将来完成时", "Passive · future perfect" },
		["p_fips"] = new[] { "被动 · 过去将来时", "Passive · future in the past" },
		["g_mod"] = new[] { "情态动词", "Modal verbs" },
		["g_sub"] = new[] { "虚拟与条件", "Subjunctive & conditionals" },
		["g_imp"] = new[] { "祈使、劝诱与请求", "Imperative & requests" },
		["g_tag"] = new[] { "附加疑问句", "Tag questions" },
		["g_cau"] = new[] { "使役与允许", "Causative & permission" },
		["g_dia"] = new[] { "地区变体", "Regional variants" },
		["m_can"] = new[] { "能力·可能（can / could）", "Ability & possibility (can / could)" },
		["m_may"] = new[] { "许可·推测（may / might）", "Permission & speculation (may / might)" },
		["m_must"] = new[] { "必须（must）", "Obligation (must)" },
		["m_haveto"] = new[] { "不得不（have to）", "Necessity (have to)" },
		["m_should"] = new[] { "建议（should / ought to）", "Advice (should / ought to)" },
		["m_will"] = new[] { "意愿·将来（will / would）", "Will & future (will / would)" },
		["m_need"] = new[] { "需要·敢（need / dare）", "Need & dare" },
		["m_able"] = new[] { "能力短语（be able to）", "Ability phrase (be able to)" },
		["m_pas"] = new[] { "情态被动（及物动词）", "Modal passive (transitive)" },
		["s_pres"] = new[] { "现在虚拟（建议、要求）", "Present subjunctive (mandative)" },
		["s_irr"] = new[] { "与现在事实相反", "Unreal present" },
		["s_past"] = new[] { "与过去事实相反", "Unreal past" },
		["s_mix"] = new[] { "混合虚拟", "Mixed conditional" },
		["s_wish"] = new[] { "wish 愿望", "wish" },
		["s_time"] = new[] { "would rather · it is time", "would rather · it is time" },
		["s_opt"] = new[] { "祈愿（may）", "Optative (may)" },
		["s_inv"] = new[] { "倒装条件", "Inverted condition" },
		["s_sub"] = new[] { "虚拟式（英式多用 should）", "Mandative subjunctive" },
		["i_pos"] = new[] { "肯定祈使", "Affirmative imperative" },
		["i_neg"] = new[] { "否定祈使", "Negative imperative" },
		["i_hort"] = new[] { "劝诱（let's）", "Hortative (let's)" },
		["i_shall"] = new[] { "提议（shall）", "Suggestions with shall" },
		["i_req"] = new[] { "请求（would / could you）", "Requests (would / could you)" },
		["i_mind"] = new[] { "委婉请求（would you mind）", "Polite request (would you mind)" },
		["i_adv"] = new[] { "建议（why don't you / had better）", "Suggestion (why don't you / had better)" },
		["u_pres"] = new[] { "一般现在时", "Simple present" },
		["u_past"] = new[] { "一般过去时", "Simple past" },
		["u_cont"] = new[] { "现在进行时", "Present continuous" },
		["u_perf"] = new[] { "现在完成时", "Present perfect" },
		["u_fut"] = new[] { "一般将来时", "Simple future" },
		["u_mod"] = new[] { "情态动词", "With a modal" },
		["c_make"] = new[] { "使役（make / have / get）", "Causative (make / have / get)" },
		["c_let"] = new[] { "允许（let / allow / permit）", "Permission (let / allow / permit)" },
		["d_bre"] = new[] { "英式", "British" },
		["d_ame"] = new[] { "美式", "American" },
		["d_aus"] = new[] { "澳新（多同英式）", "Australian & NZ (mostly British)" },
		["o_inf"] = new[] { "不定式", "Infinitive" },
		["o_pinf"] = new[] { "完成式不定式", "Perfect infinitive" },
		["o_ger"] = new[] { "动名词", "Gerund" },
		["o_pcp"] = new[] { "完成式分词", "Perfect participle" },
		["o_pi"] = new[] { "被动式不定式", "Passive infinitive" },
		["t_auto"] = new[] { "自动", "Auto" },
		["t_v"] = new[] { "动词", "Verb" },
		["t_a"] = new[] { "形容词", "Adjective" },
		["t_n"] = new[] { "名词", "Noun" },
		["type"] = new[] { "类型", "Type" },
		["g_a_form"] = new[] { "基本形式", "Basic forms" },
		["a_base"] = new[] { "原级", "Positive" },
		["a_comp"] = new[] { "比较级", "Comparative" },
		["a_sup"] = new[] { "最高级", "Superlative" },
		["a_rule"] = new[] { "变化规则", "Pattern" },
		["ar_er"] = new[] { "加 -er / -est", "add -er / -est" },
		["ar_e"] = new[] { "加 -r / -st（e 结尾）", "add -r / -st (ends in e)" },
		["ar_y"] = new[] { "变 y 为 i 再加 -er / -est", "y → ier / iest" },
		["ar_dbl"] = new[] { "双写末尾辅音再加 -er / -est", "double the final consonant" },
		["ar_more"] = new[] { "前面加 more / most", "more / most" },
		["ar_irr"] = new[] { "不规则比较级", "irregular" },
		["g_a_cmp"] = new[] { "比较句型", "Comparison" },
		["a_as"] = new[] { "原级比较（as ... as）", "as ... as" },
		["a_notas"] = new[] { "否定比较（not as / so ... as）", "not as / so ... as" },
		["a_than"] = new[] { "比较级 + than", "comparative + than" },
		["a_most"] = new[] { "最高级（the ... in / of）", "the ... in / of" },
		["a_less"] = new[] { "降级比较（less / least）", "less / least" },
		["a_deg"] = new[] { "程度修饰（much / far / even）", "much / far / even" },
		["a_and"] = new[] { "越来越（比较级 + and + 比较级）", "comparative and comparative" },
		["g_a_oth"] = new[] { "其他形式", "Other forms" },
		["a_adv"] = new[] { "副词形式", "Adverb" },
		["a_noun"] = new[] { "名词形式", "Noun" },
		["a_grad"] = new[] { "可分级性", "Gradability" },
		["grad_yes"] = new[] { "可以分级，能比较", "Gradable, can be compared" },
		["grad_no"] = new[] { "一般不用比较级、最高级", "Usually not compared" },
		["g_n_form"] = new[] { "基本形式", "Basic forms" },
		["n_sg"] = new[] { "单数", "Singular" },
		["n_pl"] = new[] { "复数", "Plural" },
		["n_poss"] = new[] { "所有格（单数）", "Possessive (singular)" },
		["n_possp"] = new[] { "所有格（复数）", "Possessive (plural)" },
		["g_n_art"] = new[] { "冠词与数量", "Articles & quantity" },
		["n_a"] = new[] { "不定冠词", "Indefinite article" },
		["n_the"] = new[] { "定冠词", "Definite article" },
		["n_count"] = new[] { "可数性", "Countability" },
		["n_qty"] = new[] { "数量", "Quantity" },
		["g_n_syn"] = new[] { "句法", "Syntax" },
		["n_agree"] = new[] { "主谓一致（be）", "Subject-verb agreement (be)" },
		["n_vagree"] = new[] { "主谓一致（实义动词）", "Agreement (lexical verb)" },
		["n_there"] = new[] { "there be", "there be" },
		["n_this"] = new[] { "this / these", "this / these" },
		["n_pron"] = new[] { "代词", "Pronoun" },
		["n_rule"] = new[] { "复数规则", "Plural pattern" },
		["count_yes"] = new[] { "可数", "Countable" },
		["count_no"] = new[] { "不可数", "Uncountable" },
		["uncount_dash"] = new[] { "—（不可数，没有复数）", "— (uncountable, no plural)" },
		["uncount_art"] = new[] { "—（不加 a / an）", "— (no a / an)" },
		["pl_s"] = new[] { "加 -s", "add -s" },
		["pl_es"] = new[] { "加 -es（s、x、z、ch、sh 结尾）", "add -es (after s, x, z, ch, sh)" },
		["pl_ies"] = new[] { "变 y 为 i 再加 -es（辅音 + y）", "y → ies (consonant + y)" },
		["pl_ves"] = new[] { "变 f / fe 为 v 再加 -es", "f / fe → ves" },
		["pl_oes"] = new[] { "加 -es（部分 o 结尾）", "add -es (some words ending in o)" },
		["pl_irr"] = new[] { "不规则复数", "Irregular plural" },
		["pl_same"] = new[] { "单复数同形", "Same singular and plural" },
		["pl_no"] = new[] { "不可数，没有复数", "Uncountable, no plural" },
		["no_plural"] = new[] { "（没有复数）", " (no plural)" },
		["n_sing_verb"] = new[] { "—（按单数）", "— (takes a singular verb)" },
		["reg_a"] = new[] { "规则形容词", "Regular adjective" },
		["irr_a"] = new[] { "不规则形容词", "Irregular adjective" },
		["reg_n"] = new[] { "规则名词", "Regular noun" },
		["irr_n"] = new[] { "不规则名词", "Irregular noun" },
		["g_ton"] = new[] { "语体与语气", "Register & tone" },
		["t_formal"] = new[] { "正式书面", "Formal" },
		["t_neutral"] = new[] { "中性", "Neutral" },
		["t_informal"] = new[] { "口语", "Informal" },
		["t_emph"] = new[] { "强调（do / does）", "Emphatic do" },
		["t_inv"] = new[] { "强调倒装", "Emphatic inversion" },
		["t_excl"] = new[] { "感叹", "Exclamation" },
		["t_hedge"] = new[] { "委婉、试探", "Hedged" },
		["t_insist"] = new[] { "坚决、命令", "Insistent" },
		["t_regret"] = new[] { "后悔、责备", "Regret" },
		["t_wish"] = new[] { "愿望", "Wish" },
		["t_deduct"] = new[] { "推测", "Deduction" },
		["t_hear"] = new[] { "据说、应当", "Hearsay & supposed" },
		["g_deg"] = new[] { "程度与语气", "Degree & tone" },
		["d_deg"] = new[] { "程度", "Degree" },
		["d_formal"] = new[] { "正式", "Formal" },
		["d_informal"] = new[] { "口语", "Informal" },
		["d_emph"] = new[] { "强调", "Emphasis" },
		["d_hedge"] = new[] { "弱化、委婉", "Hedged" },
		["d_excl"] = new[] { "感叹", "Exclamation" },
		["d_enough"] = new[] { "足够、过度", "Enough / too" },
		["g_ntone"] = new[] { "语体与数量", "Register & quantity" },
		["q_formal"] = new[] { "正式", "Formal" },
		["q_neutral"] = new[] { "中性", "Neutral" },
		["q_informal"] = new[] { "口语", "Informal" },
		["q_small"] = new[] { "少量", "A few" },
		["q_excl"] = new[] { "感叹", "Exclamation" },
		["pos"] = new[] { "肯定", "Positive" },
		["neg"] = new[] { "否定", "Negative" },
		["d_stmt"] = new[] { "陈述", "Statement" },
		["d_deduct"] = new[] { "推测", "Deduction" },
		["d_hear"] = new[] { "据说", "Hearsay" },
		["q_stmt"] = new[] { "陈述", "Statement" },
		["q_deduct"] = new[] { "推测", "Deduction" },
		["q_hear"] = new[] { "据说", "Hearsay" }
	};

	// 词性表只取词本身，中文释义只在网页下拉里用。
	static readonly HashSet<string> ADJSET = new() {
		"good", "bad", "big", "small", "long", "short", "tall", "high", "low", "old",
		"new", "young", "hot", "cold", "warm", "cool", "fast", "slow", "easy", "hard",
		"difficult", "happy", "sad", "angry", "busy", "free", "full", "empty", "clean",
		"dirty", "rich", "poor", "strong", "weak", "heavy", "light", "dark", "bright", "quiet",
		"loud", "beautiful", "ugly", "pretty", "handsome", "nice", "kind", "friendly",
		"careful", "useful", "useless", "important", "interesting", "boring", "tired", "excited",
		"famous", "popular", "common", "rare", "cheap", "expensive", "safe", "dangerous",
		"healthy", "sick", "hungry", "thirsty", "sleepy", "afraid", "ready", "sure",
		"true", "false", "real", "right", "wrong", "simple", "modern", "ancient", "wide",
		"narrow", "deep", "shallow", "thick", "thin", "soft", "rough", "smooth", "sharp",
		"dull", "wet", "dry", "fresh", "sweet", "sour", "bitter", "salty", "spicy",
		"delicious", "terrible", "wonderful", "perfect", "possible", "impossible",
		"necessary", "different", "similar", "special", "usual", "unusual", "serious",
		"funny", "lucky", "proud", "shy", "brave", "calm", "polite", "rude",
		"honest", "clever", "smart", "stupid", "lazy", "generous", "selfish", "curious",
		"creative", "patient", "nasty", "guilty", "wise", "still", "even"
	};

	static readonly HashSet<string> NOUNSET = new() {
		"dog", "cat", "book", "car", "house", "city", "country", "school", "teacher",
		"student", "family", "friend", "money", "time", "day", "year", "month", "week",
		"hour", "minute", "hand", "eye", "head", "food", "air", "music", "art",
		"science", "math", "history", "information", "advice", "news", "furniture", "luggage",
		"equipment", "homework", "bread", "milk", "sugar", "salt", "rice", "coffee", "tea",
		"weather", "health", "happiness", "beauty", "nature", "world", "earth", "sun",
		"moon", "star", "sky", "sea", "river", "mountain", "forest", "animal", "bird",
		"horse", "cow", "pig", "chicken", "apple", "orange", "banana", "egg", "cake",
		"table", "chair", "door", "window", "room", "street", "road", "park",
		"hospital", "bank", "station", "airport", "computer", "phone", "internet", "email",
		"photo", "video", "game", "sport", "team", "language", "question", "problem",
		"idea", "word", "story", "baby", "lady", "party", "company", "key",
		"toy", "boy", "way", "box", "bus", "glass", "knife", "leaf",
		"wife", "life", "half", "shelf", "wolf", "potato", "tomato", "hero",
		"piano", "radio", "zoo", "capital", "material", "holiday", "memory", "industry",
		"society", "theory", "victory", "policy", "quality", "safety", "difficulty",
		"opportunity", "activity", "reality", "ability", "university", "community",
		"property", "security", "authority", "celebrity", "energy", "salary", "plate",
		"village", "message", "picture", "tree"
	};

	const string VERBWORDS = "walk work study play live like love open close watch help want need call ask " +
		"answer talk look use move turn start stop finish wait try carry clean wash cook drive " +
		"ride fly swim sing dance jump climb push pull buy sell pay cost eat drink sleep wake " +
		"read write draw paint cut build fix make give take get put bring send run come go do " +
		"have be see hear feel smell taste touch think know understand remember forget learn " +
		"teach tell say speak sit stand lie lay rise raise fall drop rain snow shine blow grow " +
		"plant water feed catch throw kick hit hold keep leave arrive enter return visit travel " +
		"park phone email text book end change hope show meet win lose spend save find mean " +
		"become seem sound remain stay continue happen appear share create design develop " +
		"produce test check click type print download upload search agree manage measure " +
		"comment insist consist resist exist discuss express impress address practice notice " +
		"promise surprise exercise decide include provide improve reduce increase allow " +
		"prevent protect repair repeat report require suggest support provide";

	static readonly HashSet<string> VERBSET = new(VERBWORDS.Split(' '));

	static readonly HashSet<string> MODALS = new() {
		"can", "could", "may", "might", "must", "shall", "should", "will", "would", "ought"
	};

	// 短语动词只接这类小品词或介词，避免把两个单词的短语当成动词。
	static readonly HashSet<string> PARTS = new() {
		"up", "down", "off", "on", "in", "out", "away", "back",
		"over", "around", "about", "through", "along", "across",
		"apart", "aside", "by", "for", "with", "after", "at",
		"into", "onto", "upon", "from", "of", "against", "together",
		"forward", "toward", "towards"
	};

	// 多音节但重音在末音节，-ing / -ed 要双写末尾辅音。
	static readonly HashSet<string> DOUBLE = new() {
		"begin", "prefer", "refer", "occur", "admit", "permit", "commit",
		"submit", "omit", "transmit", "equip", "infer", "confer", "defer",
		"deter", "abhor", "allot", "regret", "forget", "upset", "reset",
		"preset", "inset", "input", "output", "offset", "beset", "control",
		"patrol", "propel", "expel", "compel", "rebel", "excel", "acquit",
		"quiz", "quit", "format", "kidnap", "embed", "transfer", "enrol",
		"fulfil", "distil", "instil"
	};

	const string VOW = "aeiou";

	// 英式会双写词尾 l、美式不双写的常用动词（travel / cancel / model 等）。
	static readonly HashSet<string> LDOUBLE = new() {
		"travel", "cancel", "model", "label", "signal", "total", "level",
		"tunnel", "channel", "quarrel", "dial", "spiral", "jewel", "fuel",
		"marvel", "shovel", "pedal", "rival", "panel", "funnel"
	};

	// 美式保留原形、英式多用 -ted 的几个动词。
	static readonly HashSet<string> AME_BARE = new() { "fit", "quit", "wet", "wed", "bet", "knit", "sweat" };

	// 这些词的多种写法不是英式/美式差别（含义或旧体不同），不放进地区变体。
	static readonly HashSet<string> NO_DIA = new() {
		"light", "shine", "hang", "wake", "swell", "sew", "sow", "strew",
		"shear", "show", "tread", "wax", "gild", "gird", "geld", "clothe",
		"cleave", "chide", "abide", "beseech", "bereave", "heave", "dare",
		"alight", "strive", "speed"
	};

	static readonly HashSet<string> AME_SPECIAL = new() { "gotten", "dove", "proven", "snuck" };

	static readonly Dictionary<string, string[]> ADJIRR = new() {
		["good"] = new[] { "better", "best", "好" },
		["well"] = new[] { "better", "best", "健康；好" },
		["bad"] = new[] { "worse", "worst", "坏" },
		["ill"] = new[] { "worse", "worst", "生病" },
		["far"] = new[] { "farther / further", "farthest / furthest", "远" },
		["little"] = new[] { "less", "least", "少；小" },
		["much"] = new[] { "more", "most", "多" },
		["many"] = new[] { "more", "most", "许多" },
		["old"] = new[] { "older / elder", "oldest / eldest", "老；年长" },
		["late"] = new[] { "later / latter", "latest / last", "晚；后" },
		["near"] = new[] { "nearer", "nearest / next", "近" }
	};

	static readonly Dictionary<string, string> ADJFORM = new() {
		["better"] = "good", ["best"] = "good", ["worse"] = "bad", ["worst"] = "bad",
		["farther"] = "far", ["further"] = "far", ["farthest"] = "far", ["furthest"] = "far",
		["less"] = "little", ["least"] = "little", ["more"] = "much", ["most"] = "much",
		["elder"] = "old", ["eldest"] = "old", ["latter"] = "late", ["last"] = "late"
	};

	static readonly HashSet<string> NONGRAD = new() {
		"perfect", "unique", "dead", "alive", "impossible", "fatal", "eternal",
		"complete", "entire", "whole", "infinite", "universal", "main", "major",
		"minor", "only", "same", "absolute", "inevitable", "total"
	};

	// 比较级/最高级直接用 -er / -est 之外的常见形容词，用 more / most。
	static readonly HashSet<string> NONINFLECT = new() { "fun", "real", "right", "wrong", "just", "like", "worth" };

	static readonly Dictionary<string, string> NMAP = new() {
		["strong"] = "strength", ["long"] = "length", ["wide"] = "width", ["deep"] = "depth", ["high"] = "height",
		["warm"] = "warmth", ["true"] = "truth", ["young"] = "youth", ["hot"] = "heat", ["broad"] = "breadth",
		["free"] = "freedom", ["wise"] = "wisdom", ["able"] = "ability", ["possible"] = "possibility",
		["responsible"] = "responsibility", ["happy"] = "happiness", ["angry"] = "anger",
		["proud"] = "pride", ["brave"] = "bravery", ["poor"] = "poverty", ["short"] = "shortness",
		["stale"] = "staleness", ["bad"] = "badness",
		["beautiful"] = "beauty", ["difficult"] = "difficulty", ["important"] = "importance",
		["different"] = "difference", ["similar"] = "similarity", ["special"] = "specialness",
		["serious"] = "seriousness", ["funny"] = "fun", ["lucky"] = "luck", ["shy"] = "shyness",
		["calm"] = "calmness", ["polite"] = "politeness", ["rude"] = "rudeness", ["honest"] = "honesty",
		["clever"] = "cleverness", ["smart"] = "smartness", ["stupid"] = "stupidity",
		["lazy"] = "laziness", ["generous"] = "generosity", ["selfish"] = "selfishness",
		["curious"] = "curiosity", ["creative"] = "creativity", ["patient"] = "patience",
		["tired"] = "tiredness", ["excited"] = "excitement", ["interesting"] = "interest",
		["boring"] = "boredom", ["necessary"] = "necessity", ["impossible"] = "impossibility",
		["dangerous"] = "danger", ["expensive"] = "expense", ["safe"] = "safety",
		["healthy"] = "health", ["hungry"] = "hunger", ["thirsty"] = "thirst", ["sleepy"] = "sleepiness",
		["afraid"] = "fear", ["ready"] = "readiness", ["false"] = "falsity", ["real"] = "reality",
		["simple"] = "simplicity", ["modern"] = "modernity", ["ancient"] = "antiquity",
		["narrow"] = "narrowness", ["shallow"] = "shallowness", ["thick"] = "thickness",
		["thin"] = "thinness", ["soft"] = "softness", ["rough"] = "roughness",
		["smooth"] = "smoothness", ["sharp"] = "sharpness", ["dull"] = "dullness",
		["wet"] = "wetness", ["dry"] = "dryness", ["fresh"] = "freshness", ["sweet"] = "sweetness",
		["sour"] = "sourness", ["bitter"] = "bitterness", ["salty"] = "saltiness",
		["spicy"] = "spiciness", ["delicious"] = "deliciousness", ["terrible"] = "terror",
		["wonderful"] = "wonder", ["perfect"] = "perfection", ["famous"] = "fame",
		["popular"] = "popularity", ["common"] = "commonness", ["rare"] = "rarity",
		["cheap"] = "cheapness", ["full"] = "fullness", ["empty"] = "emptiness",
		["busy"] = "busyness", ["dirty"] = "dirt", ["clean"] = "cleanliness", ["rich"] = "wealth",
		["weak"] = "weakness", ["heavy"] = "heaviness", ["light"] = "lightness",
		["dark"] = "darkness", ["bright"] = "brightness", ["quiet"] = "quietness",
		["loud"] = "loudness", ["ugly"] = "ugliness", ["pretty"] = "prettiness",
		["handsome"] = "handsomeness", ["nice"] = "niceness", ["kind"] = "kindness",
		["friendly"] = "friendliness", ["careful"] = "carefulness", ["useful"] = "usefulness",
		["useless"] = "uselessness"
	};

	static readonly Dictionary<string, string> ADVMAP = new() {
		["good"] = "well", ["well"] = "well", ["fast"] = "fast", ["hard"] = "hard", ["early"] = "early",
		["late"] = "late", ["high"] = "high", ["low"] = "low", ["long"] = "long", ["far"] = "far",
		["near"] = "near", ["straight"] = "straight", ["wrong"] = "wrong", ["right"] = "right",
		["friendly"] = "in a friendly way", ["lovely"] = "in a lovely way",
		["lonely"] = "in a lonely way", ["silly"] = "in a silly way", ["ugly"] = "in an ugly way",
		["likely"] = "likely", ["true"] = "truly", ["whole"] = "wholly", ["public"] = "publicly",
		["basic"] = "basically", ["automatic"] = "automatically"
	};

	static readonly string[][] NOUNIRRROWS = {
		new[] { "man", "men" }, new[] { "woman", "women" }, new[] { "child", "children" },
		new[] { "person", "people", "persons" }, new[] { "foot", "feet" }, new[] { "tooth", "teeth" },
		new[] { "goose", "geese" }, new[] { "mouse", "mice" }, new[] { "louse", "lice" },
		new[] { "ox", "oxen" }, new[] { "die", "dice" }, new[] { "penny", "pence" },
		new[] { "sheep", "sheep" }, new[] { "deer", "deer" }, new[] { "fish", "fish", "fishes" },
		new[] { "series", "series" }, new[] { "species", "species" }, new[] { "aircraft", "aircraft" },
		new[] { "offspring", "offspring" }, new[] { "means", "means" }, new[] { "analysis", "analyses" },
		new[] { "basis", "bases" }, new[] { "crisis", "crises" }, new[] { "thesis", "theses" },
		new[] { "hypothesis", "hypotheses" }, new[] { "diagnosis", "diagnoses" },
		new[] { "oasis", "oases" }, new[] { "axis", "axes" }, new[] { "index", "indices", "indexes" },
		new[] { "appendix", "appendices", "appendixes" }, new[] { "matrix", "matrices" },
		new[] { "vertex", "vertices" }, new[] { "cactus", "cacti", "cactuses" },
		new[] { "focus", "foci", "focuses" }, new[] { "fungus", "fungi", "funguses" },
		new[] { "nucleus", "nuclei" }, new[] { "radius", "radii" }, new[] { "stimulus", "stimuli" },
		new[] { "syllabus", "syllabi", "syllabuses" }, new[] { "alumnus", "alumni" },
		new[] { "datum", "data" }, new[] { "medium", "media", "mediums" }, new[] { "bacterium", "bacteria" },
		new[] { "curriculum", "curricula", "curriculums" }, new[] { "memorandum", "memoranda", "memorandums" },
		new[] { "phenomenon", "phenomena" }, new[] { "criterion", "criteria" },
		new[] { "formula", "formulae", "formulas" }, new[] { "vertebra", "vertebrae" },
		new[] { "larva", "larvae" }, new[] { "nebula", "nebulae" }, new[] { "alga", "algae" },
		new[] { "antenna", "antennae", "antennas" }, new[] { "scarf", "scarves", "scarfs" },
		new[] { "wharf", "wharves", "wharfs" }, new[] { "dwarf", "dwarfs", "dwarves" },
		new[] { "hoof", "hooves", "hoofs" }
	};

	static readonly Dictionary<string, string[]> NOUNIRR = mkrows(NOUNIRRROWS);

	static readonly HashSet<string> UNCOUNT = new() {
		"information", "advice", "news", "furniture", "luggage", "baggage",
		"equipment", "homework", "housework", "bread", "milk", "sugar", "salt",
		"rice", "water", "coffee", "tea", "air", "weather", "health",
		"happiness", "beauty", "music", "money", "research", "knowledge",
		"education", "traffic", "transport", "progress", "courage", "patience",
		"honesty", "peace", "pollution", "garbage", "rubbish", "clothing",
		"jewelry", "software", "hardware", "evidence", "feedback", "fun",
		"luck", "wealth", "poverty", "strength", "wisdom", "time", "work",
		"love", "art"
	};

	static readonly HashSet<string> VES = new() {
		"leaf", "life", "knife", "wife", "wolf", "shelf", "half", "thief",
		"loaf", "calf", "elf", "self", "sheaf"
	};

	static readonly HashSet<string> OES = new() { "potato", "tomato", "hero", "echo", "veto", "torpedo", "embargo" };

	static readonly HashSet<string> UNCHANGED = new() {
		"sheep", "deer", "fish", "series", "species", "aircraft", "offspring",
		"means", "salmon", "trout", "swine", "bison", "moose", "corps"
	};

	/// <summary>
	/// 对应 JS build(word, lang, type)：word 原形（可带小品词），type 为 auto / v / a / n，
	/// uiLang 以 zh 开头用中文标签，否则英文。obj1 / obj2 只用于动词，空串表示没有这一个宾语。
	/// </summary>
	public static ConjResult Build(string word, string type, string uiLang, string obj1 = "", string obj2 = "") {
		var zh = iszh(uiLang);
		string L(string key) => zh ? LAB[key][0] : LAB[key][1];

		var normed = norm(word);
		var parts = normed.Split(' ');
		var head = parts[0];
		var tail = parts.Length > 1 ? " " + string.Join(" ", parts, 1, parts.Length - 1) : "";
		if (head.Length == 0 || !Regex.IsMatch(head, "^[a-z][a-z-]*$") || parts.Length > 2) return fail(L("bad"));
		for (var i = 1; i < parts.Length; i++) if (!PARTS.Contains(parts[i])) return fail(L("bad"));
		if (MODALS.Contains(head)) return fail(L("modal"));
		var kind = string.IsNullOrEmpty(type) ? "auto" : type;
		if (kind == "auto") kind = detect(head, tail);
		ConjResult r;
		if (kind == "a" || kind == "n") {
			if (tail.Length > 0) return fail(L("bad"));
			r = kind == "a" ? buildadj(head, zh) : buildnoun(head, zh);
		}
		else r = buildverb(head, tail, zh, obj1, obj2);
		if (r.Ok) r.Kind = kind == "a" || kind == "n" ? kind : "v";
		return r;
	}

	static ConjResult buildverb(string head, string tail, bool zh, string obj1, string obj2) {
		string L(string key) => zh ? LAB[key][0] : LAB[key][1];

		var o1 = cleanobj(obj1);
		var o2 = cleanobj(obj2);
		if (o1.Length == 0) {
			o1 = o2;
			o2 = "";
		}
		var particle = tail.Length > 0 ? tail.Substring(1) : "";
		string vx(string stem) => attach(stem, particle, o1, o2);
		var comp = o1.Length == 0 ? "" : (o2.Length > 0 ? o1 + " " + o2 : o1);
		var sp = comp.Length > 0 ? " " + comp : "";
		var has = comp.Length > 0;
		string beof(string form) => has ? form + sp : form;

		var hit = findirr(head, out var hitkind);
		var row = hit >= 0 ? IR[hit] : null;
		var baseword = row != null ? row[0].ToLowerInvariant() : head;
		var pasts = row != null ? varlist(row[1]) : new List<string>();
		var pps = row != null ? varlist(row[2]) : new List<string>();
		var isBe = baseword == "be";
		var ingStem = isBe ? "being" : ing(baseword);
		var ppStem = isBe ? "been" : (pps.Count > 0 ? pps[0] : ed(baseword));
		var pastStem = isBe ? "was" : (pasts.Count > 0 ? pasts[0] : ed(baseword));
		var s3Stem = isBe ? "is" : s3(baseword);
		var b = vx(baseword);
		var ingF = vx(ingStem);
		var ppF = vx(ppStem);
		var pastF = vx(pastStem);
		var s3F = vx(s3Stem);
		var ppBare = particle.Length > 0 ? ppStem + " " + particle : ppStem;
		// 一般现在时按 I / he 各给一个形；be 的 am / is 和 was / were 单独取。宾语跟在动词后面。
		var p1 = isBe ? beof("am") : b;
		var p3 = isBe ? beof("is") : s3F;
		var q1 = isBe ? beof("was") : pastF;
		var q2 = isBe ? beof("were") : pastF;
		var n1 = isBe ? "am not" + sp : "do not " + b;
		var n3 = isBe ? "is not" + sp : "does not " + b;
		var rows = new List<ConjRow>();
		void R(string g, string name, string text) => rows.Add(new ConjRow(name, text, g));
		string P(string pos, string neg) {
			var c = zh ? "：" : ": ";
			return L("pos") + c + pos + "\n" + L("neg") + c + neg;
		}
		string g;
		// 基本形式
		g = L("g_form");
		R(g, L("f_base"), b);
		R(g, L("f_s3"), isBe ? beof("am") + " / " + beof("is") + " / " + beof("are") : s3F);
		R(g, L("f_ing"), ingF);
		R(g, L("f_past"), isBe ? beof("was") + " / " + beof("were") : (pasts.Count > 0 ? variants(row[1], particle, o1, o2) : vx(ed(baseword))));
		R(g, L("f_pp"), isBe ? vx("been") : (pps.Count > 0 ? variants(row[2], particle, o1, o2) : vx(ed(baseword))));
		// 现在时
		g = L("g_pres");
		R(g, L("t_pres"), "I " + p1 + " · he " + p3);
		R(g, L("t_pcont"), "I am " + ingF + " · he is " + ingF);
		R(g, L("t_ppf"), "I have " + ppF + " · he has " + ppF);
		R(g, L("t_ppc"), "I have been " + ingF + " · he has been " + ingF);
		// 过去时
		g = L("g_past");
		R(g, L("t_past"), "I " + q1 + (isBe ? " · they " + q2 : ""));
		R(g, L("t_pastc"), "I was " + ingF + " · they were " + ingF);
		R(g, L("t_pastp"), "I had " + ppF);
		R(g, L("t_pastpc"), "I had been " + ingF);
		// 将来时
		g = L("g_fut");
		R(g, L("t_fut"), "I will " + b);
		R(g, L("t_futc"), "I will be " + ingF);
		R(g, L("t_futp"), "I will have " + ppF);
		R(g, L("t_futpc"), "I will have been " + ingF);
		// 过去将来时
		g = L("g_fip");
		R(g, L("t_fips"), "I would " + b);
		R(g, L("t_fipc"), "I would be " + ingF);
		R(g, L("t_fipp"), "I would have " + ppF);
		R(g, L("t_fippc"), "I would have been " + ingF);
		// 否定
		g = L("g_neg");
		R(g, L("n_pres"), "I " + n1 + " · he " + n3);
		R(g, L("n_past"), "I " + (isBe ? "was not" + sp + " · they were not" + sp : "did not " + b));
		R(g, L("n_pcont"), "I am not " + ingF + " · he is not " + ingF);
		R(g, L("n_ppf"), "I have not " + ppF + " · he has not " + ppF);
		R(g, L("n_fut"), "I will not " + b);
		// 疑问
		g = L("g_q");
		R(g, L("q_pres"), isBe ? "Am I" + sp + "? · Is he" + sp + "?" : "Do I " + b + "? · Does he " + b + "?");
		R(g, L("q_past"), isBe ? "Was I" + sp + "? · Were they" + sp + "?" : "Did I " + b + "?");
		R(g, L("q_pcont"), "Am I " + ingF + "? · Is he " + ingF + "?");
		R(g, L("q_ppf"), "Have I " + ppF + "? · Has he " + ppF + "?");
		R(g, L("q_fut"), "Will I " + b + "?");
		// 被动语态；be 没有被动
		if (!isBe) {
			g = L("g_pas");
			if (!has) {
				R(g, L("p_pres"), "It is " + ppBare + " · They are " + ppBare);
				R(g, L("p_past"), "It was " + ppBare + " · They were " + ppBare);
				R(g, L("p_pcont"), "It is being " + ppBare);
				R(g, L("p_pastc"), "It was being " + ppBare);
				R(g, L("p_ppf"), "It has been " + ppBare);
				R(g, L("p_pastp"), "It had been " + ppBare);
				R(g, L("p_fut"), "It will be " + ppBare);
				R(g, L("p_futp"), "It will have been " + ppBare);
				R(g, L("p_fips"), "It would be " + ppBare);
			}
			else {
				objagree(o1, out var sub, out var am, out var was, out var hv);
				var rest = o2.Length > 0 ? " " + o2 : "";
				R(g, L("p_pres"), sub + " " + am + " " + ppBare + rest);
				R(g, L("p_past"), sub + " " + was + " " + ppBare + rest);
				R(g, L("p_pcont"), sub + " " + am + " being " + ppBare + rest);
				R(g, L("p_pastc"), sub + " " + was + " being " + ppBare + rest);
				R(g, L("p_ppf"), sub + " " + hv + " been " + ppBare + rest);
				R(g, L("p_pastp"), sub + " had been " + ppBare + rest);
				R(g, L("p_fut"), sub + " will be " + ppBare + rest);
				R(g, L("p_futp"), sub + " will have been " + ppBare + rest);
				R(g, L("p_fips"), sub + " would be " + ppBare + rest);
			}
		}
		// 情态动词；be 没有「be able to be」和情态被动，略过这两行
		g = L("g_mod");
		R(g, L("m_can"), "can " + b + " · can't " + b + " · could have " + ppF + " · couldn't have " + ppF);
		R(g, L("m_may"), "may " + b + " · may not " + b + " · might have " + ppF + " · might not have " + ppF);
		R(g, L("m_must"), "must " + b + " · mustn't " + b + " · must have " + ppF + " · mustn't have " + ppF);
		R(g, L("m_haveto"), "have to " + b + " · don't have to " + b + " · had to " + b + " · didn't have to " + b);
		R(g, L("m_should"), "should " + b + " · shouldn't " + b + " · should have " + ppF + " · shouldn't have " + ppF);
		R(g, L("m_will"), "will " + b + " · won't " + b + " · would have " + ppF + " · wouldn't have " + ppF);
		R(g, L("m_need"), "needn't " + b + " · need not " + b + " · needed to " + b + " · didn't need to " + b + " · daren't " + b);
		if (!isBe) {
			R(g, L("m_able"), "am / is / are able to " + b + " · am / is / are not able to " + b + " · will be able to " + b);
			if (!has) R(g, L("m_pas"), "can be " + ppBare + " · can't be " + ppBare + " · should have been " + ppBare + " · shouldn't have been " + ppBare);
			else {
				objagree(o1, out var sub, out _, out _, out _);
				var rest = o2.Length > 0 ? " " + o2 : "";
				R(g, L("m_pas"), sub + " can be " + ppBare + rest + " · " + sub + " can't be " + ppBare + rest
					+ " · " + sub + " should have been " + ppBare + rest + " · " + sub + " shouldn't have been " + ppBare + rest);
			}
		}
		// 虚拟与条件
		g = L("g_sub");
		R(g, L("s_pres"), P("I suggest that he " + b + " · it is important that he " + b,
			"I suggest that he not " + b + " · it is important that he not " + b));
		R(g, L("s_sub"), isBe ? "AmE: that he be" + sp + " · BrE: that he should be" + sp
			: "AmE: that he " + b + " · BrE: that he should " + b);
		R(g, L("s_irr"), "if I " + (isBe ? "were" + sp : pastF) + ", I would " + b);
		R(g, L("s_past"), "if I had " + ppF + ", I would have " + ppF);
		R(g, L("s_mix"), "if I had " + ppF + ", I would be " + ingF);
		R(g, L("s_wish"), "I wish I " + (isBe ? "were" + sp : pastF) + " · I wish I had " + ppF);
		R(g, L("s_time"), "I would rather he " + (isBe ? "were" + sp : pastF) + " · it is time he " + (isBe ? "were" + sp : pastF));
		R(g, L("s_opt"), "may he " + b);
		R(g, L("s_inv"), "should he " + b + " · were he to " + b);
		// 祈使、劝诱与请求
		g = L("g_imp");
		R(g, L("i_pos"), b + "! · do " + b + "! · please " + b + "!");
		R(g, L("i_neg"), "do not " + b + "! · never " + b + "!");
		R(g, L("i_hort"), "let's " + b + " · let's not " + b);
		R(g, L("i_shall"), "shall we " + b + "? · shall I " + b + "?");
		R(g, L("i_req"), "would you " + b + "? · could you " + b + "?");
		R(g, L("i_mind"), "would you mind " + ingF + "?");
		R(g, L("i_adv"), "why don't you " + b + "? · you had better " + b);
		// 附加疑问句
		g = L("g_tag");
		R(g, L("u_pres"), isBe ? "you are" + sp + ", aren't you? · he is" + sp + ", isn't he?"
			: "you " + b + ", don't you? · he " + s3F + ", doesn't he?");
		R(g, L("u_past"), isBe ? "you were" + sp + ", weren't you?" : "you " + pastF + ", didn't you?");
		R(g, L("u_cont"), "you are " + ingF + ", aren't you?");
		R(g, L("u_perf"), "you have " + ppF + ", haven't you?");
		R(g, L("u_fut"), "you will " + b + ", won't you?");
		R(g, L("u_mod"), "you can " + b + ", can't you?");
		// 使役与允许
		g = L("g_cau");
		R(g, L("c_make"), "make him " + b + " · have him " + b + " · get him to " + b);
		R(g, L("c_let"), "let him " + b + " · allow him to " + b + " · permit him to " + b);
		// 语体与语气（肯定 ／ 否定 并排）
		g = L("g_ton");
		R(g, L("t_formal"), P("I would like to " + b + " · we wish to " + b,
			"I would not like to " + b + " · we do not wish to " + b));
		R(g, L("t_neutral"), P("I want to " + b + " · I'd like to " + b,
			"I don't want to " + b + " · I wouldn't like to " + b));
		R(g, L("t_informal"), P("I wanna " + b + " · I'm gonna " + b + " · I gotta " + b,
			"I don't wanna " + b + " · I'm not gonna " + b));
		R(g, L("t_emph"), isBe
			? P("I really am" + sp + " · he really is" + sp, "I really am not" + sp + " · he really isn't" + sp)
			: P("I do " + b + " · I really do " + b + " · he does " + b, "I don't " + b + " at all · I never " + b));
		R(g, L("t_inv"), isBe
			? P("Only then was I" + (has ? sp : " there"), "Never have I been" + (has ? sp : " there") + " · not once was I" + (has ? sp : " there"))
			: P("Only then did I " + b, "Never have I " + ppF + " · not once did I " + b + " · not only did I not " + b));
		R(g, L("t_excl"), isBe
			? P("How he is" + sp + "!", "How he isn't" + sp + "!")
			: P("How he " + s3F + "!", "How he never " + s3F + "!"));
		R(g, L("t_hedge"), P("I was wondering if you could " + b + " · it might be better to " + b,
			"I was wondering if you couldn't " + b + " · it might not be better to " + b));
		R(g, L("t_insist"), P("You will " + b + " · you are to " + b + " · you shall " + b,
			"You will not " + b + " · you are not to " + b + " · you shall not " + b));
		R(g, L("t_regret"), P("You should have " + ppF, "You shouldn't have " + ppF + " · you needn't have " + ppF));
		R(g, L("t_wish"), P("If only I could " + b + " · I wish I could " + b,
			"If only I didn't have to " + b + " · I wish I didn't have to " + b));
		R(g, L("t_deduct"), P("He may have " + ppF + " · he must have " + ppF,
			"He may not have " + ppF + " · he must not have " + ppF));
		R(g, L("t_hear"), P("He is said to " + b + " · he is supposed to " + b,
			"He is said not to " + b + " · he isn't supposed to " + b));
		// 地区变体：不规则动词的 -t / -ed 写法、gotten / got、dove / dived，以及 -l 双写
		var ameP = pastF;
		var amePP = ppF;
		var breP = pastF;
		var brePP = ppF;
		var brIng = ingF;
		if (LDOUBLE.Contains(baseword)) {
			brIng = vx(baseword + "ling");
			breP = vx(baseword + "led");
			brePP = breP;
		}
		if (!isBe) {
			if (AME_BARE.Contains(baseword)) {
				if (pasts.Count > 1) {
					ameP = vx(pasts[0]);
					breP = vx(pasts[pasts.Count - 1]);
				}
				if (pps.Count > 1) {
					amePP = vx(pps[0]);
					brePP = vx(pps[pps.Count - 1]);
				}
			}
			else if (!NO_DIA.Contains(baseword)) {
				if (pasts.Count > 1) {
					ameP = vx(pickame(pasts));
					breP = vx(pickbre(pasts, pickame(pasts)));
				}
				if (pps.Count > 1) {
					amePP = vx(pickame(pps));
					brePP = vx(pickbre(pps, pickame(pps)));
				}
			}
			if (baseword == "sneak") {
				ameP = vx("snuck");
				amePP = vx("snuck");
			}
		}
		// 只列真的有地区写法差别的动词
		if ((!isBe && (breP != ameP || brePP != amePP)) || LDOUBLE.Contains(baseword)) {
			g = L("g_dia");
			R(g, L("d_bre"), breP + " / " + brePP + " · " + brIng);
			R(g, L("d_ame"), ameP + " / " + amePP + " · " + ingF);
			R(g, L("d_aus"), breP + " / " + brePP + " · " + brIng);
		}
		// 其他形式
		g = L("g_oth");
		R(g, L("o_inf"), "to " + b);
		R(g, L("o_pinf"), "to have " + ppF);
		R(g, L("o_ger"), ingF);
		R(g, L("o_pcp"), "having " + ppF);
		if (!isBe) R(g, L("o_pi"), has ? "for " + o1 + " to be " + ppBare + (o2.Length > 0 ? " " + o2 : "") : "to be " + ppBare);
		// 顶部说明
		string note;
		if (row != null) {
			note = L("irr");
			if (pasts.Count > 0) note += " · " + L("f_past") + " " + variants(row[1], particle, "", "");
			if (pps.Count > 0) note += " · " + L("f_pp") + " " + variants(row[2], particle, "", "");
			var mean = tail.Length > 0 ? "" : (zh ? row[3] : row[4]);
			if (mean.Length > 0) note += " · " + mean;
			if (isBe) note += " · " + L("be");
			if (hitkind != "base") note = fmt(L("from"), head + tail) + " · " + note;
		}
		else note = L("reg");
		if (has) note += " · " + comp;
		return ok(baseword + tail, note, rows);
	}

	static string detect(string head, string tail) {
		if (tail.Length > 0) return "v";
		if (findirr(head, out _) >= 0 || VERBSET.Contains(head)) return "v";
		if (ADJIRR.ContainsKey(head) || ADJSET.Contains(head) || ADJFORM.ContainsKey(head)) return "a";
		if (singularadj(head).Length > 0) return "a";
		if (NOUNIRR.ContainsKey(head) || NOUNSET.Contains(head)) return "n";
		var sg = singularof(head, false);
		if (sg.Length > 0 && !VERBSET.Contains(sg) && !ADJSET.Contains(sg) && !ADJIRR.ContainsKey(sg)) return "n";
		if (Regex.IsMatch(head, "(ous|ful|less|able|ible|ive|ical|ic|ish|ary|ant|ent|al|like|y)$")) return "a";
		if (Regex.IsMatch(head, "(tion|sion|ment|ness|ity|ance|ence|ship|hood|ism|ist|ee|ure|age|dom|er|or)$")) return "n";
		if (Regex.IsMatch(head, "(ize|ise|ify|fy|ate|en)$")) return "v";
		return "v";
	}

	static ConjResult buildadj(string head, bool zh) {
		string L(string key) => zh ? LAB[key][0] : LAB[key][1];

		var baseword = head;
		var from = "";
		if (!ADJIRR.ContainsKey(baseword) && !ADJSET.Contains(baseword)) {
			var sa = singularadj(baseword);
			if (sa.Length > 0) {
				from = head;
				baseword = sa;
			}
			else if (ADJFORM.ContainsKey(baseword)) {
				from = head;
				baseword = ADJFORM[head];
			}
		}
		string c, s, rule;
		ADJIRR.TryGetValue(baseword, out var irr);
		if (irr != null) {
			c = irr[0];
			s = irr[1];
			rule = L("ar_irr");
		}
		else {
			var r = cmp(baseword);
			c = r.C;
			s = r.S;
			rule = L(r.Rule);
		}
		var rows = new List<ConjRow>();
		void R(string g, string name, string text) => rows.Add(new ConjRow(name, text, g));
		string P(string pos, string neg) {
			var c2 = zh ? "：" : ": ";
			return L("pos") + c2 + pos + "\n" + L("neg") + c2 + neg;
		}
		var c1 = c.Split(new[] { " / " }, StringSplitOptions.None)[0];
		var s1 = s.Split(new[] { " / " }, StringSplitOptions.None)[0];
		var g = L("g_a_form");
		R(g, L("a_base"), baseword);
		R(g, L("a_comp"), c);
		R(g, L("a_sup"), s);
		R(g, L("a_rule"), rule);
		g = L("g_a_cmp");
		R(g, L("a_as"), "as " + baseword + " as");
		R(g, L("a_notas"), "not as / so " + baseword + " as");
		R(g, L("a_than"), c1 + " than");
		R(g, L("a_most"), "the " + s1 + " (in / of ...)");
		R(g, L("a_less"), "less " + baseword + " than · the least " + baseword);
		R(g, L("a_deg"), "much / far / even " + c1);
		R(g, L("a_and"), c1.StartsWith("more ") ? "more and more " + baseword : c1 + " and " + c1);
		g = L("g_deg");
		R(g, L("d_deg"), P("very / really / so / quite " + baseword,
			"not very / not particularly " + baseword));
		R(g, L("d_emph"), P("absolutely / utterly " + baseword,
			"not " + baseword + " at all"));
		R(g, L("d_hedge"), P("fairly / rather / somewhat " + baseword,
			"hardly / not exactly " + baseword));
		R(g, L("d_formal"), P("exceedingly / remarkably " + baseword,
			"far from " + baseword));
		R(g, L("d_informal"), P("super / pretty / totally " + baseword,
			"not so " + baseword));
		R(g, L("d_stmt"), P("It is " + baseword, "It is not " + baseword));
		R(g, L("d_deduct"), P("It may be " + baseword + " · it must be " + baseword,
			"It may not be " + baseword + " · it can't be " + baseword));
		R(g, L("d_hear"), P("It is said to be " + baseword, "It is said not to be " + baseword));
		R(g, L("d_excl"), P("How " + baseword + "! · So " + baseword + "!", "Isn't it " + baseword + "!"));
		R(g, L("d_enough"), P(baseword + " enough · too " + baseword,
			"not " + baseword + " enough · not too " + baseword));
		g = L("g_a_oth");
		R(g, L("a_adv"), advform(baseword));
		R(g, L("a_noun"), adjnoun(baseword));
		R(g, L("a_grad"), NONGRAD.Contains(baseword) ? L("grad_no") : L("grad_yes"));
		var note = irr != null ? L("irr_a") : L("reg_a");
		if (irr != null) note += " · " + c + " · " + s + (zh ? " · " + irr[2] : "");
		if (from.Length > 0) note = fmt(L("from"), from) + " · " + note;
		return ok(baseword, note, rows);
	}

	static ConjResult buildnoun(string head, bool zh) {
		string L(string key) => zh ? LAB[key][0] : LAB[key][1];

		var baseword = head;
		var from = "";
		if (!NOUNSET.Contains(baseword) && !NOUNIRR.ContainsKey(baseword)) {
			var sg = singularof(baseword, true);
			if (sg.Length > 0 && sg != baseword) {
				from = baseword;
				baseword = sg;
			}
		}
		var uncount = UNCOUNT.Contains(baseword);
		var plist = NOUNIRR.ContainsKey(baseword) ? NOUNIRR[baseword] : new[] { plural(baseword) };
		var pl = uncount ? L("uncount_dash") : string.Join(" / ", plist);
		var first = uncount ? "" : plist[0];
		var poss = baseword + "'s";
		var possP = uncount ? L("uncount_dash") : (Regex.IsMatch(first, "s$") ? first + "'" : first + "'s");
		var rows = new List<ConjRow>();
		void R(string g, string name, string text) => rows.Add(new ConjRow(name, text, g));
		string P(string pos, string neg) {
			var c = zh ? "：" : ": ";
			return L("pos") + c + pos + "\n" + L("neg") + c + neg;
		}
		var g = L("g_n_form");
		R(g, L("n_sg"), baseword);
		R(g, L("n_pl"), pl);
		R(g, L("n_poss"), poss);
		R(g, L("n_possp"), possP);
		g = L("g_n_art");
		R(g, L("n_a"), uncount ? L("uncount_art") : (article(baseword) + " " + baseword));
		R(g, L("n_the"), "the " + baseword);
		R(g, L("n_count"), uncount ? L("count_no") : L("count_yes"));
		R(g, L("n_qty"), uncount
			? "some " + baseword + " · much " + baseword + " · a lot of " + baseword
			: "one " + baseword + " · two " + first + " · some " + first);
		g = L("g_n_syn");
		R(g, L("n_agree"), uncount
			? "the " + baseword + " is"
			: "the " + baseword + " is · the " + first + " are");
		R(g, L("n_vagree"), uncount ? "it runs" : "it runs · they run");
		R(g, L("n_there"), uncount
			? "there is " + baseword
			: "there is " + article(baseword) + " " + baseword + " · there are " + first);
		R(g, L("n_this"), uncount
			? "this " + baseword + L("no_plural")
			: "this " + baseword + " · these " + first);
		R(g, L("n_pron"), uncount ? "it" : "it · they");
		R(g, L("n_rule"), uncount ? L("pl_no") : (NOUNIRR.ContainsKey(baseword) ? L("pl_irr") : (UNCHANGED.Contains(baseword) ? L("pl_same") : L(pluralrule(baseword)))));
		var artbase = uncount ? baseword : article(baseword) + " " + baseword;
		g = L("g_ntone");
		R(g, L("q_stmt"), P("It is " + artbase, "It is not " + artbase));
		R(g, L("q_formal"), P(uncount ? "a great deal of " + baseword : "a considerable number of " + first,
			uncount ? "not much " + baseword : "not a single " + baseword));
		R(g, L("q_neutral"), P("some " + (uncount ? baseword : first), "not any " + (uncount ? baseword : first)));
		R(g, L("q_informal"), P("a lot of " + (uncount ? baseword : first) + " · tons of " + (uncount ? baseword : first),
			uncount ? "not much " + baseword : "not many " + first));
		R(g, L("q_small"), P(uncount ? "a little " + baseword : "a few " + first,
			"hardly any " + (uncount ? baseword : first)));
		R(g, L("q_excl"), P(uncount ? "What " + baseword + "!" : "What a " + baseword + "!", "Isn't it " + artbase + "!"));
		R(g, L("q_deduct"), P("It may be " + artbase + " · it must be " + artbase,
			"It may not be " + artbase + " · it can't be " + artbase));
		R(g, L("q_hear"), P("It is said to be " + artbase, "It is said not to be " + artbase));
		var note = NOUNIRR.ContainsKey(baseword) ? L("irr_n") : L("reg_n");
		if (!uncount) note += " · " + string.Join(" / ", plist);
		else note += " · " + L("count_no");
		if (from.Length > 0) note = fmt(L("from"), from) + " · " + note;
		return ok(baseword, note, rows);
	}

	static (string C, string S, string Rule) cmp(string w) {
		if (NONINFLECT.Contains(w)) return ("more " + w, "most " + w, "ar_more");
		if (syll(w) == 1 || Regex.IsMatch(w, "[^aeiou]y$")) {
			if (Regex.IsMatch(w, "e$")) return (w + "r", w + "st", "ar_e");
			if (Regex.IsMatch(w, "[^aeiou]y$")) return (cut(w, 1) + "ier", cut(w, 1) + "iest", "ar_y");
			if (cvc(w)) return (w + w[w.Length - 1] + "er", w + w[w.Length - 1] + "est", "ar_dbl");
			return (w + "er", w + "est", "ar_er");
		}
		if (Regex.IsMatch(w, "le$")) return (w + "r", w + "st", "ar_e");
		if (Regex.IsMatch(w, "ow$")) return (w + "er", w + "est", "ar_er");
		if (Regex.IsMatch(w, "er$")) return (w + "er", w + "est", "ar_er");
		return ("more " + w, "most " + w, "ar_more");
	}

	static string advform(string w) {
		if (ADVMAP.TryGetValue(w, out var adv)) return adv;
		if (Regex.IsMatch(w, "[^aeiou]y$")) return cut(w, 1) + "ily";
		if (Regex.IsMatch(w, "le$")) return cut(w, 1) + "y";
		if (Regex.IsMatch(w, "ic$")) return w + "ally";
		if (Regex.IsMatch(w, "ue$")) return cut(w, 1) + "ly";
		if (Regex.IsMatch(w, "ll$")) return w + "y";
		return w + "ly";
	}

	static string adjnoun(string w) {
		if (NMAP.TryGetValue(w, out var noun)) return noun;
		if (Regex.IsMatch(w, "(shy|dry|sly|wry|spry)$")) return w + "ness";
		if (Regex.IsMatch(w, "[^aeiou]y$")) return cut(w, 1) + "iness";
		if (Regex.IsMatch(w, "able$")) return cut(w, 4) + "ability";
		if (Regex.IsMatch(w, "ible$")) return cut(w, 5) + "ibility";
		return w + "ness";
	}

	static string plural(string w) {
		if (NOUNIRR.TryGetValue(w, out var irr)) return irr[0];
		if (w.IndexOf('-') > 0) {
			var at = w.IndexOf('-');
			if (Regex.IsMatch(w, "-in-law$|-by$|-of-")) return plural(w.Substring(0, at)) + w.Substring(at);
			var last = w.LastIndexOf('-');
			return w.Substring(0, last + 1) + plural(w.Substring(last + 1));
		}
		var lower = w.ToLowerInvariant();
		if (UNCHANGED.Contains(lower)) return lower;
		if (VES.Contains(lower)) return Regex.Replace(Regex.Replace(lower, "fe$", "ves"), "f$", "ves");
		if (OES.Contains(lower)) return lower + "es";
		if (Regex.IsMatch(lower, "(s|x|z|ch|sh)$")) return lower + "es";
		if (Regex.IsMatch(lower, "[^aeiou]y$")) return cut(lower, 1) + "ies";
		return lower + "s";
	}

	static string pluralrule(string w) {
		var lower = w.ToLowerInvariant();
		if (UNCHANGED.Contains(lower)) return "pl_same";
		if (VES.Contains(lower)) return "pl_ves";
		if (OES.Contains(lower)) return "pl_oes";
		if (Regex.IsMatch(lower, "(s|x|z|ch|sh)$")) return "pl_es";
		if (Regex.IsMatch(lower, "[^aeiou]y$")) return "pl_ies";
		return "pl_s";
	}

	static string article(string w) {
		var lower = w.ToLowerInvariant();
		if (Regex.IsMatch(lower, "^(hour|honest|honor|honour|heir)")) return "an";
		if (Regex.IsMatch(lower, "^(uni|use|usu|uti|eu|ewe|one|once|ubiq|uk)")) return "a";
		if (Regex.IsMatch(lower, "^[aeiou]")) return "an";
		return "a";
	}

	static bool knownnoun(string w) => NOUNSET.Contains(w) || NOUNIRR.ContainsKey(w);

	// 由比较级/最高级认回原级：bigger → big、happier → happy、nicer → nice。
	static string singularadj(string w) {
		var cands = new List<string>();
		if (Regex.IsMatch(w, "iest$")) cands.Add(cut(w, 4) + "y");
		if (Regex.IsMatch(w, "ier$")) cands.Add(cut(w, 3) + "y");
		if (Regex.IsMatch(w, "est$")) {
			cands.Add(cut(w, 3));
			cands.Add(cut(w, 3) + "e");
			cands.Add(cut(w, 4));
		}
		if (Regex.IsMatch(w, "er$")) {
			cands.Add(cut(w, 2));
			cands.Add(cut(w, 2) + "e");
			cands.Add(cut(w, 3));
		}
		foreach (var cand in cands) {
			if ((ADJSET.Contains(cand) || ADJIRR.ContainsKey(cand)) && !VERBSET.Contains(cand)) return cand;
		}
		return "";
	}

	static string singularof(string w, bool loose) {
		if (NOUNIRR.ContainsKey(w)) return w;
		foreach (var row in NOUNIRRROWS) {
			for (var i = 1; i < row.Length; i++) if (row[i] == w) return row[0];
		}
		if (Regex.IsMatch(w, "(ss|us|is|ics)$")) return "";
		var cands = new List<string>();
		if (Regex.IsMatch(w, "ies$")) cands.Add(cut(w, 3) + "y");
		if (Regex.IsMatch(w, "ves$")) {
			cands.Add(cut(w, 3) + "f");
			cands.Add(cut(w, 3) + "fe");
		}
		if (Regex.IsMatch(w, "oes$")) cands.Add(cut(w, 2));
		if (Regex.IsMatch(w, "(ses|xes|zes|ches|shes)$")) cands.Add(cut(w, 2));
		if (Regex.IsMatch(w, "s$")) cands.Add(cut(w, 1));
		foreach (var cand in cands) {
			if (loose ? (knownnoun(cand) || VERBSET.Contains(cand) || ADJSET.Contains(cand)) : knownnoun(cand)) return cand;
		}
		return "";
	}

	static string norm(string s) {
		s = Regex.Replace(s ?? "", "^\\s+|\\s+$", "");
		s = Regex.Replace(s, "\\s+", " ");
		s = s.ToLowerInvariant();
		return Regex.Replace(s, "^to ", "");
	}

	static string fmt(string s, string v) {
		var at = s.IndexOf("%s", StringComparison.Ordinal);
		return at < 0 ? s : s.Substring(0, at) + v + s.Substring(at + 2);
	}

	// 返回表下标；找不到为 -1。kind 为 base / past / pp / s3。
	static int findirr(string word, out string kind) {
		kind = "";
		for (var i = 0; i < IR.Length; i++) {
			if (IR[i][0].ToLowerInvariant() == word) {
				kind = "base";
				return i;
			}
		}
		for (var i = 0; i < IR.Length; i++) {
			if (varhas(IR[i][1], word)) {
				kind = "past";
				return i;
			}
			if (varhas(IR[i][2], word)) {
				kind = "pp";
				return i;
			}
		}
		// 过去式和过去分词之外，再认第三人称单数（has、does、goes）和 am / is / are。
		for (var i = 0; i < IR.Length; i++) {
			var b = IR[i][0].ToLowerInvariant();
			if (s3(b) == word) {
				kind = "s3";
				return i;
			}
			if (b == "be" && (word == "am" || word == "are")) {
				kind = "s3";
				return i;
			}
		}
		return -1;
	}

	static List<string> varlist(string field) {
		var list = new List<string>();
		var parts = (field ?? "").Split('/');
		foreach (var part in parts) {
			var s = Regex.Replace(part, "^\\s+|\\s+$", "").ToLowerInvariant();
			if (s.Length > 0 && !list.Contains(s)) list.Add(s);
		}
		return list;
	}

	static bool varhas(string field, string word) => varlist(field).Contains(word);

	static string variants(string field, string particle, string o1, string o2) {
		var list = varlist(field);
		if (list.Count == 0) return "";
		for (var i = 0; i < list.Count; i++) list[i] = attach(list[i], particle, o1, o2);
		return string.Join(" / ", list);
	}

	// 宾语只收简短英语短语。可手打，字母、数字、撇号和连字符，最多六个词。
	static string cleanobj(string s) {
		s = Regex.Replace(s ?? "", "^\\s+|\\s+$", "");
		s = Regex.Replace(s, "\\s+", " ").ToLowerInvariant();
		if (s.Length == 0 || s.Length > 64) return "";
		if (!Regex.IsMatch(s, "^[a-z0-9][a-z0-9'-]*( [a-z0-9][a-z0-9'-]*){0,5}$")) return "";
		return s;
	}

	static readonly HashSet<string> OBJPRON = new() { "me", "you", "him", "her", "it", "us", "them" };

	static readonly HashSet<string> OBJPREP = new() {
		"after", "for", "with", "at", "into", "onto", "upon", "from", "of",
		"against", "about", "by", "across", "along", "through", "toward", "towards", "around"
	};

	// 主动：动词 + 宾语。短语动词里，代词宾语夹在动词和副词小品词之间（turn it on），介词则放在后面（look after him）。
	static string attach(string stem, string particle, string o1, string o2) {
		var p = particle ?? "";
		if (o1.Length == 0) return p.Length == 0 ? stem : $"{stem} {p}";
		if (p.Length == 0) return o2.Length == 0 ? $"{stem} {o1}" : $"{stem} {o1} {o2}";
		if (OBJPRON.Contains(o1) && !OBJPREP.Contains(p))
			return o2.Length == 0 ? $"{stem} {o1} {p}" : $"{stem} {o1} {p} {o2}";
		return o2.Length == 0 ? $"{stem} {p} {o1}" : $"{stem} {p} {o1} {o2}";
	}

	// 被动里宾语升成主语，并配上 be / have。
	static void objagree(string obj, out string sub, out string pres, out string past, out string have) {
		var k = obj.ToLowerInvariant();
		if (k == "i" || k == "me") { sub = "I"; pres = "am"; past = "was"; have = "have"; return; }
		if (k == "you") { sub = "you"; pres = "are"; past = "were"; have = "have"; return; }
		if (k == "we" || k == "us") { sub = "we"; pres = "are"; past = "were"; have = "have"; return; }
		if (k == "they" || k == "them") { sub = "they"; pres = "are"; past = "were"; have = "have"; return; }
		if (k == "he" || k == "him") { sub = "he"; pres = "is"; past = "was"; have = "has"; return; }
		if (k == "she" || k == "her") { sub = "she"; pres = "is"; past = "was"; have = "has"; return; }
		if (k == "it") { sub = "it"; pres = "is"; past = "was"; have = "has"; return; }
		sub = obj;
		var pl = pluralnoun(obj);
		pres = pl ? "are" : "is";
		past = pl ? "were" : "was";
		have = pl ? "have" : "has";
	}

	static bool pluralnoun(string phrase) {
		var i = phrase.LastIndexOf(' ');
		var last = i < 0 ? phrase : phrase.Substring(i + 1);
		if (last is "people" or "children" or "men" or "women" or "teeth" or "feet" or "mice" or "geese") return true;
		if (last.Length < 2 || !last.EndsWith("s", StringComparison.Ordinal)) return false;
		if (last.EndsWith("ss", StringComparison.Ordinal) || last.EndsWith("us", StringComparison.Ordinal) || last.EndsWith("is", StringComparison.Ordinal)) return false;
		if (last is "this" or "his" or "news" or "series" or "species" or "maths") return false;
		return true;
	}

	// 美式优先 gotten、dove、proven 这类写法，其次 -ed；英式取另一个写法。
	static string pickame(List<string> list) {
		foreach (var s in list) if (AME_SPECIAL.Contains(s)) return s;
		foreach (var s in list) if (Regex.IsMatch(s, "ed$")) return s;
		return list[0];
	}

	static string pickbre(List<string> list, string ame) {
		foreach (var s in list) if (s != ame) return s;
		return ame;
	}

	static bool cons(char ch) => ch >= 'a' && ch <= 'z' && VOW.IndexOf(ch) < 0 && "wxy".IndexOf(ch) < 0;

	static bool cvc(string w) {
		if (w.Length < 3) return false;
		var a = w[w.Length - 3];
		var b = w[w.Length - 2];
		var c = w[w.Length - 1];
		return cons(a) && VOW.IndexOf(b) >= 0 && cons(c);
	}

	static int syll(string w) {
		var m = Regex.Matches(w, "[aeiouy]+");
		return m.Count > 0 ? m.Count : 1;
	}

	static bool dbl(string w) => DOUBLE.Contains(w) || (syll(w) == 1 && cvc(w));

	static string s3(string w) {
		if (w == "be") return "is";
		if (w == "have") return "has";
		if (syll(w) == 1 && Regex.IsMatch(w, "[aeiou]z$")) return w + "zes";
		if (Regex.IsMatch(w, "(s|x|z|ch|sh|o)$")) return w + "es";
		if (Regex.IsMatch(w, "[^aeiou]y$")) return cut(w, 1) + "ies";
		return w + "s";
	}

	static string ing(string w) {
		if (w == "be") return "being";
		if (Regex.IsMatch(w, "ie$")) return cut(w, 2) + "ying";
		if (Regex.IsMatch(w, "(ee|oe|ye)$")) return w + "ing";
		if (Regex.IsMatch(w, "e$")) return cut(w, 1) + "ing";
		if (Regex.IsMatch(w, "c$")) return w + "king";
		if (dbl(w)) return w + w[w.Length - 1] + "ing";
		return w + "ing";
	}

	static string ed(string w) {
		if (Regex.IsMatch(w, "e$")) return w + "d";
		if (Regex.IsMatch(w, "[^aeiou]y$")) return cut(w, 1) + "ied";
		if (Regex.IsMatch(w, "c$")) return w + "ked";
		if (dbl(w)) return w + w[w.Length - 1] + "ed";
		return w + "ed";
	}

	// 对应 JS 的 s.slice(0, -n)：末尾去掉 n 个字符，长度不足时取空串。
	static string cut(string s, int n) {
		var len = s.Length - n;
		return len > 0 ? s.Substring(0, len) : "";
	}

	static bool iszh(string uiLang) => string.IsNullOrEmpty(uiLang) || uiLang.StartsWith("zh", StringComparison.Ordinal);

	static ConjResult fail(string err) => new() { Ok = false, Err = err };

	static ConjResult ok(string head, string note, List<ConjRow> rows) => new() { Ok = true, Head = head, Note = note, Rows = rows };

	static Dictionary<string, string[]> mkrows(string[][] rows) {
		var dict = new Dictionary<string, string[]>();
		foreach (var row in rows) {
			var forms = new string[row.Length - 1];
			Array.Copy(row, 1, forms, 0, forms.Length);
			dict[row[0]] = forms;
		}
		return dict;
	}
}
