(function(){
	// 英语动词时态：输入原形，列出基本形式、十六种时态，以及否定、疑问、被动和其他形式。
	// [原形, 过去式, 过去分词, 中文释义, 英文释义]；没有英文词典时用这一小份兜底。
;
	var PICKS = [
		["cm", "walk", "走"],
		["cm", "study", "学习"],
		["cm", "play", "玩"],
		["cm", "stop", "停止"],
		["cm", "work", "工作"],
		["cm", "watch", "看"],
		["cm", "live", "住"],
		["cm", "like", "喜欢"],
		["cm", "open", "打开"],
		["cm", "carry", "搬；携带"],
		["cm", "panic", "惊慌"],
		["irr", "be", "是"],
		["irr", "have", "有"],
		["irr", "do", "做"],
		["irr", "go", "去"],
		["irr", "come", "来"],
		["irr", "get", "得到"],
		["irr", "make", "做；制造"],
		["irr", "take", "拿；取"],
		["irr", "see", "看见"],
		["irr", "give", "给"],
		["irr", "write", "写"],
		["irr", "speak", "说"],
		["irr", "begin", "开始"],
		["irr", "run", "跑"],
		["irr", "swim", "游泳"],
		["irr", "teach", "教"],
		["irr", "buy", "买"],
		["irr", "think", "想"],
		["irr", "lie", "躺"],
		["phr", "give up", "放弃"],
		["phr", "look after", "照顾"],
		["phr", "turn on", "打开（电器）"],
		["adj", "beautiful", "美丽"],
		["adj", "happy", "快乐"],
		["adj", "big", "大"],
		["adj", "good", "好"],
		["adj", "easy", "容易"],
		["adj", "difficult", "困难"],
		["noun", "child", "孩子"],
		["noun", "box", "盒子"],
		["noun", "city", "城市"],
		["noun", "information", "信息"],
		["noun", "knife", "刀"],
		["noun", "man", "男人"]
	];
	var PICKG = {
		cm: ["常见规则动词", "Common regular"],
		irr: ["常见不规则动词", "Common irregular"],
		phr: ["短语动词", "Phrasal"],
		adj: ["常见形容词", "Common adjectives"],
		noun: ["常见名词", "Common nouns"]
	};
;
	// 短语动词只接这类小品词或介词，避免把两个单词的短语当成动词。
;
	// 多音节但重音在末音节，-ing / -ed 要双写末尾辅音。
;
	var VOW = "aeiou";
	// 英式会双写词尾 l、美式不双写的常用动词（travel / cancel / model 等）。
;
	// 美式保留原形、英式多用 -ted 的几个动词。
;
	// 这些词的多种写法不是英式/美式差别（含义或旧体不同），不放进地区变体。
;
;
	var LAB = {
		title: ["英语词形变化", "English word forms"],
		hint: ["输入英语动词原形、形容词或名词，类型默认自动：先查不规则的动词、形容词、名词表，再按词尾判断，认不出时按动词处理，也可以改类型。动词列基本形式、十六种时态、否定、疑问、被动、情态、虚拟、祈使、附加疑问、使役和地区变体；形容词列比较级、最高级、比较句型、副词和名词形式；名词列复数、冠词、所有格和主谓一致。斜杠两边是不同写法，表里用第一个。",
			"Enter a verb, adjective or noun. Auto checks the irregular verb, adjective and noun tables, then the ending; if unsure it treats the word as a verb, and you can change the type. Verbs list the basic forms, sixteen tenses, negatives, questions, passive, modals, subjunctive, imperative, tag questions, causative and regional variants. Adjectives list comparative and superlative forms, comparison patterns, the adverb and the noun. Nouns list plural, articles, possessives and subject-verb agreement. Slashes separate variants; the tables use the first."],
		word: ["单词", "Word"],
		ph: ["动词原形、形容词或名词，如 walk、beautiful、child", "A verb, adjective or noun, e.g. walk, beautiful, child"],
		reg: ["规则动词", "Regular verb"],
		irr: ["不规则动词", "Irregular verb"],
		bad: ["只支持一个英语单词，可带小品词，如 walk、beautiful、child", "One English word, with an optional particle, e.g. walk, beautiful or child"],
		modal: ["情态动词没有这些时态变化", "Modal verbs have no such tense forms"],
		from: ["由 %s 识别", "from %s"],
		be: ["be 的进行时、完成进行时和被动语态极少用", "be rarely uses continuous, perfect continuous or passive forms"],
		g_form: ["基本形式", "Basic forms"],
		g_pres: ["现在时", "Present"],
		g_past: ["过去时", "Past"],
		g_fut: ["将来时", "Future"],
		g_fip: ["过去将来时", "Future in the past"],
		g_neg: ["否定式", "Negative"],
		g_q: ["疑问式", "Questions"],
		g_pas: ["被动语态（及物动词）", "Passive (transitive)"],
		g_oth: ["其他形式", "Other forms"],
		f_base: ["原形", "Base form"],
		f_s3: ["第三人称单数", "3rd person singular"],
		f_ing: ["现在分词", "Present participle"],
		f_past: ["过去式", "Past"],
		f_pp: ["过去分词", "Past participle"],
		t_pres: ["一般现在时", "Simple present"],
		t_pcont: ["现在进行时", "Present continuous"],
		t_ppf: ["现在完成时", "Present perfect"],
		t_ppc: ["现在完成进行时", "Present perfect continuous"],
		t_past: ["一般过去时", "Simple past"],
		t_pastc: ["过去进行时", "Past continuous"],
		t_pastp: ["过去完成时", "Past perfect"],
		t_pastpc: ["过去完成进行时", "Past perfect continuous"],
		t_fut: ["一般将来时", "Simple future"],
		t_futc: ["将来进行时", "Future continuous"],
		t_futp: ["将来完成时", "Future perfect"],
		t_futpc: ["将来完成进行时", "Future perfect continuous"],
		t_fips: ["一般过去将来时", "Simple future in the past"],
		t_fipc: ["过去将来进行时", "Future in the past continuous"],
		t_fipp: ["过去将来完成时", "Future in the past perfect"],
		t_fippc: ["过去将来完成进行时", "Future in the past perfect continuous"],
		n_pres: ["一般现在时否定", "Simple present negative"],
		n_past: ["一般过去时否定", "Simple past negative"],
		n_pcont: ["现在进行时否定", "Present continuous negative"],
		n_ppf: ["现在完成时否定", "Present perfect negative"],
		n_fut: ["一般将来时否定", "Simple future negative"],
		q_pres: ["一般现在时疑问", "Simple present question"],
		q_past: ["一般过去时疑问", "Simple past question"],
		q_pcont: ["现在进行时疑问", "Present continuous question"],
		q_ppf: ["现在完成时疑问", "Present perfect question"],
		q_fut: ["一般将来时疑问", "Simple future question"],
		p_pres: ["被动 · 一般现在时", "Passive · simple present"],
		p_past: ["被动 · 一般过去时", "Passive · simple past"],
		p_pcont: ["被动 · 现在进行时", "Passive · present continuous"],
		p_pastc: ["被动 · 过去进行时", "Passive · past continuous"],
		p_ppf: ["被动 · 现在完成时", "Passive · present perfect"],
		p_pastp: ["被动 · 过去完成时", "Passive · past perfect"],
		p_fut: ["被动 · 一般将来时", "Passive · simple future"],
		p_futp: ["被动 · 将来完成时", "Passive · future perfect"],
		p_fips: ["被动 · 过去将来时", "Passive · future in the past"],
		g_mod: ["情态动词", "Modal verbs"],
		g_sub: ["虚拟与条件", "Subjunctive & conditionals"],
		g_imp: ["祈使、劝诱与请求", "Imperative & requests"],
		g_tag: ["附加疑问句", "Tag questions"],
		g_cau: ["使役与允许", "Causative & permission"],
		g_dia: ["地区变体", "Regional variants"],
		m_can: ["能力·可能（can / could）", "Ability & possibility (can / could)"],
		m_may: ["许可·推测（may / might）", "Permission & speculation (may / might)"],
		m_must: ["必须（must）", "Obligation (must)"],
		m_haveto: ["不得不（have to）", "Necessity (have to)"],
		m_should: ["建议（should / ought to）", "Advice (should / ought to)"],
		m_will: ["意愿·将来（will / would）", "Will & future (will / would)"],
		m_need: ["需要·敢（need / dare）", "Need & dare"],
		m_able: ["能力短语（be able to）", "Ability phrase (be able to)"],
		m_pas: ["情态被动（及物动词）", "Modal passive (transitive)"],
		s_pres: ["现在虚拟（建议、要求）", "Present subjunctive (mandative)"],
		s_irr: ["与现在事实相反", "Unreal present"],
		s_past: ["与过去事实相反", "Unreal past"],
		s_mix: ["混合虚拟", "Mixed conditional"],
		s_wish: ["wish 愿望", "wish"],
		s_time: ["would rather · it is time", "would rather · it is time"],
		s_opt: ["祈愿（may）", "Optative (may)"],
		s_inv: ["倒装条件", "Inverted condition"],
		s_sub: ["虚拟式（英式多用 should）", "Mandative subjunctive"],
		i_pos: ["肯定祈使", "Affirmative imperative"],
		i_neg: ["否定祈使", "Negative imperative"],
		i_hort: ["劝诱（let's）", "Hortative (let's)"],
		i_shall: ["提议（shall）", "Suggestions with shall"],
		i_req: ["请求（would / could you）", "Requests (would / could you)"],
		i_mind: ["委婉请求（would you mind）", "Polite request (would you mind)"],
		i_adv: ["建议（why don't you / had better）", "Suggestion (why don't you / had better)"],
		u_pres: ["一般现在时", "Simple present"],
		u_past: ["一般过去时", "Simple past"],
		u_cont: ["现在进行时", "Present continuous"],
		u_perf: ["现在完成时", "Present perfect"],
		u_fut: ["一般将来时", "Simple future"],
		u_mod: ["情态动词", "With a modal"],
		c_make: ["使役（make / have / get）", "Causative (make / have / get)"],
		c_let: ["允许（let / allow / permit）", "Permission (let / allow / permit)"],
		d_bre: ["英式", "British"],
		d_ame: ["美式", "American"],
		d_aus: ["澳新（多同英式）", "Australian & NZ (mostly British)"],
		o_inf: ["不定式", "Infinitive"],
		o_pinf: ["完成式不定式", "Perfect infinitive"],
		o_ger: ["动名词", "Gerund"],
		o_pcp: ["完成式分词", "Perfect participle"],
		o_pi: ["被动式不定式", "Passive infinitive"],
		t_auto: ["自动", "Auto"],
		t_v: ["动词", "Verb"],
		t_a: ["形容词", "Adjective"],
		t_n: ["名词", "Noun"],
		type: ["类型", "Type"],
		g_a_form: ["基本形式", "Basic forms"],
		a_base: ["原级", "Positive"],
		a_comp: ["比较级", "Comparative"],
		a_sup: ["最高级", "Superlative"],
		a_rule: ["变化规则", "Pattern"],
		ar_er: ["加 -er / -est", "add -er / -est"],
		ar_e: ["加 -r / -st（e 结尾）", "add -r / -st (ends in e)"],
		ar_y: ["变 y 为 i 再加 -er / -est", "y → ier / iest"],
		ar_dbl: ["双写末尾辅音再加 -er / -est", "double the final consonant"],
		ar_more: ["前面加 more / most", "more / most"],
		ar_irr: ["不规则比较级", "irregular"],
		g_a_cmp: ["比较句型", "Comparison"],
		a_as: ["原级比较（as ... as）", "as ... as"],
		a_notas: ["否定比较（not as / so ... as）", "not as / so ... as"],
		a_than: ["比较级 + than", "comparative + than"],
		a_most: ["最高级（the ... in / of）", "the ... in / of"],
		a_less: ["降级比较（less / least）", "less / least"],
		a_deg: ["程度修饰（much / far / even）", "much / far / even"],
		a_and: ["越来越（比较级 + and + 比较级）", "comparative and comparative"],
		g_a_oth: ["其他形式", "Other forms"],
		a_adv: ["副词形式", "Adverb"],
		a_noun: ["名词形式", "Noun"],
		a_grad: ["可分级性", "Gradability"],
		grad_yes: ["可以分级，能比较", "Gradable, can be compared"],
		grad_no: ["一般不用比较级、最高级", "Usually not compared"],
		g_n_form: ["基本形式", "Basic forms"],
		n_sg: ["单数", "Singular"],
		n_pl: ["复数", "Plural"],
		n_poss: ["所有格（单数）", "Possessive (singular)"],
		n_possp: ["所有格（复数）", "Possessive (plural)"],
		g_n_art: ["冠词与数量", "Articles & quantity"],
		n_a: ["不定冠词", "Indefinite article"],
		n_the: ["定冠词", "Definite article"],
		n_count: ["可数性", "Countability"],
		n_qty: ["数量", "Quantity"],
		g_n_syn: ["句法", "Syntax"],
		n_agree: ["主谓一致（be）", "Subject-verb agreement (be)"],
		n_vagree: ["主谓一致（实义动词）", "Agreement (lexical verb)"],
		n_there: ["there be", "there be"],
		n_this: ["this / these", "this / these"],
		n_pron: ["代词", "Pronoun"],
		n_rule: ["复数规则", "Plural pattern"],
		count_yes: ["可数", "Countable"],
		count_no: ["不可数", "Uncountable"],
		uncount_dash: ["—（不可数，没有复数）", "— (uncountable, no plural)"],
		uncount_art: ["—（不加 a / an）", "— (no a / an)"],
		pl_s: ["加 -s", "add -s"],
		pl_es: ["加 -es（s、x、z、ch、sh 结尾）", "add -es (after s, x, z, ch, sh)"],
		pl_ies: ["变 y 为 i 再加 -es（辅音 + y）", "y → ies (consonant + y)"],
		pl_ves: ["变 f / fe 为 v 再加 -es", "f / fe → ves"],
		pl_oes: ["加 -es（部分 o 结尾）", "add -es (some words ending in o)"],
		pl_irr: ["不规则复数", "Irregular plural"],
		pl_same: ["单复数同形", "Same singular and plural"],
		pl_no: ["不可数，没有复数", "Uncountable, no plural"],
		no_plural: ["（没有复数）", " (no plural)"],
		n_sing_verb: ["—（按单数）", "— (takes a singular verb)"],
		reg_a: ["规则形容词", "Regular adjective"],
		irr_a: ["不规则形容词", "Irregular adjective"],
		reg_n: ["规则名词", "Regular noun"],
		irr_n: ["不规则名词", "Irregular noun"],
		g_ton: ["语体与语气", "Register & tone"],
		t_formal: ["正式书面", "Formal"],
		t_neutral: ["中性", "Neutral"],
		t_informal: ["口语", "Informal"],
		t_emph: ["强调（do / does）", "Emphatic do"],
		t_inv: ["强调倒装", "Emphatic inversion"],
		t_excl: ["感叹", "Exclamation"],
		t_hedge: ["委婉、试探", "Hedged"],
		t_insist: ["坚决、命令", "Insistent"],
		t_regret: ["后悔、责备", "Regret"],
		t_wish: ["愿望", "Wish"],
		t_deduct: ["推测", "Deduction"],
		t_hear: ["据说、应当", "Hearsay & supposed"],
		g_deg: ["程度与语气", "Degree & tone"],
		d_deg: ["程度", "Degree"],
		d_formal: ["正式", "Formal"],
		d_informal: ["口语", "Informal"],
		d_emph: ["强调", "Emphasis"],
		d_hedge: ["弱化、委婉", "Hedged"],
		d_excl: ["感叹", "Exclamation"],
		d_enough: ["足够、过度", "Enough / too"],
		g_ntone: ["语体与数量", "Register & quantity"],
		q_formal: ["正式", "Formal"],
		q_neutral: ["中性", "Neutral"],
		q_informal: ["口语", "Informal"],
		q_small: ["少量", "A few"],
		q_excl: ["感叹", "Exclamation"],
		pos: ["肯定", "Positive"],
		neg: ["否定", "Negative"],
		d_stmt: ["陈述", "Statement"],
		d_deduct: ["推测", "Deduction"],
		d_hear: ["据说", "Hearsay"],
		q_stmt: ["陈述", "Statement"],
		q_deduct: ["推测", "Deduction"],
		q_hear: ["据说", "Hearsay"]
	};
	// 常见形容词和名词，用来自动判断词性，也给输入框下拉用。
	var ADJS = [
		["good", "好"], ["bad", "坏"], ["big", "大"], ["small", "小"], ["long", "长"],
		["short", "短"], ["tall", "高"], ["high", "高"], ["low", "低"], ["old", "老"],
		["new", "新"], ["young", "年轻"], ["hot", "热"], ["cold", "冷"], ["warm", "暖"],
		["cool", "凉"], ["fast", "快"], ["slow", "慢"], ["easy", "容易"], ["hard", "难"],
		["difficult", "困难"], ["happy", "快乐"], ["sad", "悲伤"], ["angry", "生气"],
		["busy", "忙"], ["free", "自由"], ["full", "满"], ["empty", "空"], ["clean", "干净"],
		["dirty", "脏"], ["rich", "富"], ["poor", "穷"], ["strong", "强"], ["weak", "弱"],
		["heavy", "重"], ["light", "轻"], ["dark", "暗"], ["bright", "亮"], ["quiet", "安静"],
		["loud", "响"], ["beautiful", "美丽"], ["ugly", "丑"], ["pretty", "漂亮"],
		["handsome", "英俊"], ["nice", "好"], ["kind", "善良"], ["friendly", "友好"],
		["careful", "小心"], ["useful", "有用"], ["useless", "无用"], ["important", "重要"],
		["interesting", "有趣"], ["boring", "无聊"], ["tired", "累"], ["excited", "兴奋"],
		["famous", "有名"], ["popular", "流行"], ["common", "常见"], ["rare", "稀有"],
		["cheap", "便宜"], ["expensive", "贵"], ["safe", "安全"], ["dangerous", "危险"],
		["healthy", "健康"], ["sick", "生病"], ["hungry", "饿"], ["thirsty", "渴"],
		["sleepy", "困"], ["afraid", "害怕"], ["ready", "准备好"], ["sure", "确定"],
		["true", "真"], ["false", "假"], ["real", "真"], ["right", "对"], ["wrong", "错"],
		["simple", "简单"], ["modern", "现代"], ["ancient", "古代"], ["wide", "宽"],
		["narrow", "窄"], ["deep", "深"], ["shallow", "浅"], ["thick", "厚"], ["thin", "薄"],
		["soft", "软"], ["rough", "粗糙"], ["smooth", "光滑"], ["sharp", "锋利"],
		["dull", "钝"], ["wet", "湿"], ["dry", "干"], ["fresh", "新鲜"], ["sweet", "甜"],
		["sour", "酸"], ["bitter", "苦"], ["salty", "咸"], ["spicy", "辣"],
		["delicious", "好吃"], ["terrible", "可怕"], ["wonderful", "奇妙"],
		["perfect", "完美"], ["possible", "可能"], ["impossible", "不可能"],
		["necessary", "必要"], ["different", "不同"], ["similar", "相似"],
		["special", "特别"], ["usual", "平常"], ["unusual", "不寻常"], ["serious", "严肃"],
		["funny", "好笑"], ["lucky", "幸运"], ["proud", "自豪"], ["shy", "害羞"],
		["brave", "勇敢"], ["calm", "冷静"], ["polite", "礼貌"], ["rude", "粗鲁"],
		["honest", "诚实"], ["clever", "聪明"], ["smart", "聪明"], ["stupid", "笨"],
		["lazy", "懒"], ["generous", "大方"], ["selfish", "自私"], ["curious", "好奇"],
		["creative", "有创意"], ["patient", "耐心"], ["pretty", "漂亮"], ["nasty", "讨厌"],
		["guilty", "有罪"], ["wise", "明智"], ["still", "静止"], ["even", "平"]
	];
	var NOUNS = [
		["dog", "狗"], ["cat", "猫"], ["book", "书"], ["car", "车"], ["house", "房子"],
		["city", "城市"], ["country", "国家"], ["school", "学校"], ["teacher", "老师"],
		["student", "学生"], ["family", "家庭"], ["friend", "朋友"], ["money", "钱"],
		["time", "时间"], ["day", "天"], ["year", "年"], ["month", "月"], ["week", "周"],
		["hour", "小时"], ["minute", "分钟"], ["hand", "手"], ["eye", "眼睛"],
		["head", "头"], ["food", "食物"], ["air", "空气"], ["music", "音乐"], ["art", "艺术"],
		["science", "科学"], ["math", "数学"], ["history", "历史"], ["information", "信息"],
		["advice", "建议"], ["news", "新闻"], ["furniture", "家具"], ["luggage", "行李"],
		["equipment", "设备"], ["homework", "作业"], ["bread", "面包"], ["milk", "牛奶"],
		["sugar", "糖"], ["salt", "盐"], ["rice", "米饭"], ["coffee", "咖啡"], ["tea", "茶"],
		["weather", "天气"], ["health", "健康"], ["happiness", "幸福"], ["beauty", "美"],
		["nature", "自然"], ["world", "世界"], ["earth", "地球"], ["sun", "太阳"],
		["moon", "月亮"], ["star", "星星"], ["sky", "天空"], ["sea", "海"], ["river", "河"],
		["mountain", "山"], ["forest", "森林"], ["animal", "动物"], ["bird", "鸟"],
		["horse", "马"], ["cow", "牛"], ["pig", "猪"], ["chicken", "鸡"], ["apple", "苹果"],
		["orange", "橙子"], ["banana", "香蕉"], ["egg", "蛋"], ["cake", "蛋糕"],
		["table", "桌子"], ["chair", "椅子"], ["door", "门"], ["window", "窗"],
		["room", "房间"], ["street", "街"], ["road", "路"], ["park", "公园"],
		["hospital", "医院"], ["bank", "银行"], ["station", "车站"], ["airport", "机场"],
		["computer", "电脑"], ["phone", "电话"], ["internet", "互联网"], ["email", "邮件"],
		["photo", "照片"], ["video", "视频"], ["game", "游戏"], ["sport", "运动"],
		["team", "队"], ["language", "语言"], ["question", "问题"], ["problem", "问题"],
		["idea", "主意"], ["word", "单词"], ["story", "故事"], ["baby", "婴儿"],
		["lady", "女士"], ["party", "聚会"], ["company", "公司"], ["key", "钥匙"],
		["toy", "玩具"], ["boy", "男孩"], ["way", "方式"], ["box", "盒子"],
		["bus", "公交车"], ["glass", "玻璃杯"], ["knife", "刀"], ["leaf", "叶子"],
		["wife", "妻子"], ["life", "生活"], ["half", "一半"], ["shelf", "架子"],
		["wolf", "狼"], ["potato", "土豆"], ["tomato", "西红柿"], ["hero", "英雄"],
		["piano", "钢琴"], ["radio", "收音机"], ["zoo", "动物园"], ["city", "城市"],
		["capital", "首都"], ["hospital", "医院"], ["material", "材料"], ["animal", "动物"],
		["holiday", "假日"], ["memory", "记忆"], ["history", "历史"], ["industry", "工业"],
		["society", "社会"], ["theory", "理论"], ["victory", "胜利"], ["policy", "政策"],
		["quality", "质量"], ["safety", "安全"], ["difficulty", "困难"],
		["opportunity", "机会"], ["activity", "活动"], ["reality", "现实"],
		["ability", "能力"], ["university", "大学"], ["community", "社区"],
		["property", "财产"], ["security", "安全"], ["authority", "权威"],
		["celebrity", "名人"], ["energy", "能量"], ["salary", "工资"], ["plate", "盘子"],
		["village", "村庄"], ["message", "消息"], ["picture", "图片"], ["tree", "树"]
	];
	var VERBS = ("walk work study play live like love open close watch help want need call ask " +
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
		"prevent protect repair repeat report require suggest support provide").split(" ");
;
;
;
	// 比较级/最高级直接用 -er / -est 之外的常见形容词，用 more / most。
;
;
;
;
;
;
;
;
	var ADJSET = {};
	var NOUNSET = {};
	var VERBSET = {};
	var i;
	for (i = 0; i < ADJS.length; i++) ADJSET[ADJS[i][0]] = 1;
	for (i = 0; i < NOUNS.length; i++) NOUNSET[NOUNS[i][0]] = 1;
	for (i = 0; i < VERBS.length; i++) VERBSET[VERBS[i]] = 1;
	var timer = 0;
	var gen = 0;
	var clip = null;

		// 旧实现：计算已移到 C# 端（/api/conj），保留仅供对照，勿再调用。

	// 由比较级/最高级认回原级：bigger → big、happier → happy、nicer → nice。

	function norm(s){
		s = String(s == null ? "" : s).toLowerCase().replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
		return s.replace(/^to /, "");
	}

	// 美式优先 gotten、dove、proven 这类写法，其次 -ed；英式取另一个写法。

	function speech(s){
		return String(s == null ? "" : s).replace(/^(肯定|否定|Positive|Negative)[:：]\s*/i, "");
	}

	function saytext(s){
		return String(s == null ? "" : s).replace(/·/g, ",").replace(/\s*\/\s*/g, " or ").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
	}

	if (typeof SK === "undefined" || !SK.regutil) return;

	SK.regutil("u-everb", {
		title: "英语词形变化",
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

	function tr(pair){
		return langcode() === "zh" ? pair[0] : pair[1];
	}

	function langcode(){
		var sel = F.id("lang");
		return sel && sel.value ? sel.value : "zh";
	}

	function body(){
		return '<p class="hint" id="ev-hint" data-k="web.everb.hint">' + esc(tr(LAB.hint)) + "</p>"
			+ '<div class="row" id="ev-row">'
			+ '<label id="ev-type"><span data-k="web.everb.type">' + esc(tr(LAB.type)) + "</span>"
			+ '<select id="ev-sel">'
			+ '<option value="auto"></option>'
			+ '<option value="v"></option>'
			+ '<option value="a"></option>'
			+ '<option value="n"></option>'
			+ "</select></label>"
			+ '<label id="ev-word"><span data-k="web.everb.word">' + esc(tr(LAB.word)) + "</span>"
			+ SK.combo("ev-in", "walk", LAB.ph[0]) + "</label>"
			+ "</div>"
			+ '<div id="ev-out"></div>';
	}

	function open(){
		var title = F.id("box-title");
		if (title) title.textContent = tx("web.tool.everb", LAB.title[0]);
		SK.combofill("ev-in", pickgroups());
		setopt("auto", "web.everb.auto", LAB.t_auto[0]);
		setopt("v", "web.everb.verb", LAB.t_v[0]);
		setopt("a", "web.everb.adj", LAB.t_a[0]);
		setopt("n", "web.everb.noun", LAB.t_n[0]);
		var sel = F.id("ev-sel");
		if (sel && !F.attr(sel, "data-bind")) {
			F.seta(sel, "data-bind", "1");
			sel.onchange = ontype;
		}
		var out = F.id("ev-out");
		if (out && !F.attr(out, "data-bind")) {
			F.seta(out, "data-bind", "1");
			out.onclick = onout;
		}
		if (!bound) {
			bound = true;
			window.addEventListener("resize", onresize);
		}
		run();
	}

	function setopt(value, key, fb){
		var sel = F.id("ev-sel");
		if (!sel) return;
		for (var i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === value) sel.options[i].textContent = tx(key, tr(fb));
		}
	}

	// 下拉只列当前类型的例词：自动列全部，选了动词/形容词/名词就只列这一类。
	function ontype(){
		SK.combofill("ev-in", pickgroups());
		run();
	}

	function pickok(group, pick){
		if (pick === "auto") return true;
		if (pick === "a") return group === "adj";
		if (pick === "n") return group === "noun";
		return group !== "adj" && group !== "noun";
	}

	function pickkind(){
		var sel = F.id("ev-sel");
		return sel && sel.value ? sel.value : "auto";
	}

	function pickgroups(){
		var zh = langcode() === "zh";
		var pick = pickkind();
		var groups = [];
		var cur = null;
		var i, p, name;
		for (i = 0; i < PICKS.length; i++) {
			p = PICKS[i];
			if (!pickok(p[0], pick)) continue;
			name = zh ? PICKG[p[0]][0] : PICKG[p[0]][1];
			if (!cur || cur.g !== name) {
				cur = { g: name, items: [] };
				groups.push(cur);
			}
			cur.items.push({ v: p[1], t: zh ? p[1] + "  " + p[2] : p[1] });
		}
		return groups;
	}

	function onout(ev){
		var btn = ev.target;
		var out = F.id("ev-out");
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
		var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=en");
		clip = audio;
		audio.onerror = function(){
			if (clip === audio) SK.msg("box-msg", tx("web.everb.nospeak", "无法发音"), true);
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (clip === audio) SK.msg("box-msg", tx("web.everb.nospeak", "无法发音"), true);
		});
	}

	function run(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(go, 150);
	}

	function go(){
		timer = 0;
		var n = ++gen;
		var box = F.id("ev-in");
		var sel = F.id("ev-sel");
		var word = norm(box ? box.value : "");
		if (!word) {
			paint("", "", []);
			SK.msg("box-msg", "");
			return;
		}
		var t0 = now();
		lookup(word, function(hits){
			if (n !== gen) return;
			var tdict = hits ? now() - t0 : -1;
			var miss = hits && !hits.length ? tx("web.everb.miss", "词典里没有这个词，已按词形判断。") : "";
			var t1 = now();
			conjreq({ lang: "en", q: word, type: sel ? sel.value : "auto", ui: langcode() }, function(d){
				if (n !== gen) return;
				var tcalc = now() - t1;
				if (!d) {
					paint("", "", []);
					SK.msg("box-msg", tx("web.everb.down", "变形接口不可用"), true);
					return;
				}
				if (!d.ok) {
					paint("", "", []);
					SK.msg("box-msg", d.err, true);
					return;
				}
				paint(d.head, d.note, objrows(d.rows), timestr(tdict, tcalc, miss));
				SK.msg("box-msg", "");
			});
		});
	}

	// 查词只用来算耗时、提示词条在不在；类型仍按词形判断。
	function lookup(word, done){
		fetch("/api/dict?dict=en&limit=8&q=" + encodeURIComponent(word)).then(function(r){ return r.json(); }).then(function(jo){
			done(jo && jo.code === 100 && jo.data ? (jo.data.hits || []) : null);
		}, function(){ done(null); });
	}

	// 变形算在 C# 端（/api/conj），这里只取结果和渲染。
	function conjreq(params, done){
		var qs = [], k;
		for (k in params) if (params[k] !== undefined && params[k] !== null && params[k] !== "")
			qs.push(k + "=" + encodeURIComponent(params[k]));
		fetch("/api/conj?" + qs.join("&")).then(function(r){ return r.json(); }).then(function(jo){
			done(jo && jo.code === 100 && jo.data ? jo.data : null);
		}, function(){ done(null); });
	}

	function objrows(rows){
		var out = [], i;
		if (!rows) return out;
		for (i = 0; i < rows.length; i++) out.push({ name: rows[i].l, text: rows[i].v, g: rows[i].g || "" });
		return out;
	}

	var perline = 2;
	var maxcols = 4;
	var tries = 0;
	var lastpaint = null;
	var rtimer = 0;
	var bound = false;

	function paint(head, note, rows, tm){
		var out = F.id("ev-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; lastpaint = null; return; }
		lastpaint = { head: head, note: note, rows: rows, tm: tm };
		render(out);
	}

	// 页面越宽排得越多：先按一栏量出最宽的一行，够放几栏就排几栏，最多四栏；
	// 排完仍然超宽再逐栏退回，尽量不出现横向滚动条。
	function render(out){
		if (!out || !lastpaint) return;
		perline = 1;
		out.innerHTML = tablehtml(lastpaint.head, lastpaint.note, lastpaint.rows, lastpaint.tm);
		var tab = out.getElementsByTagName("table")[0];
		var w = tab ? tab.offsetWidth : 0;
		var cw = out.clientWidth;
		if (cw <= 0) {
			// 刚切到工具页时容器可能还没显示，等一帧再量。
			if (tries < 5) { tries++; setTimeout(refit, 40); }
			return;
		}
		tries = 0;
		var n = 1;
		if (w > 0) {
			n = Math.floor((cw + 10) / (w + 10));
			if (n > maxcols) n = maxcols;
			if (n < 1) n = 1;
		}
		if (n > 1) {
			perline = n;
			out.innerHTML = tablehtml(lastpaint.head, lastpaint.note, lastpaint.rows, lastpaint.tm);
		}
		while (perline > 1 && out.scrollWidth > out.clientWidth + 2) {
			perline--;
			out.innerHTML = tablehtml(lastpaint.head, lastpaint.note, lastpaint.rows, lastpaint.tm);
		}
	}

	function onresize(){
		if (rtimer) clearTimeout(rtimer);
		rtimer = setTimeout(refit, 150);
	}

	function refit(){
		var out = F.id("ev-out");
		if (out && lastpaint) render(out);
	}

	// 一行两组「名称 + 词形」。
	function tablehtml(head, note, rows, tm){
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(head) + "</b><span>" + esc(note) + "</span>"
			+ (tm ? '<span class="jv-time">' + esc(tm) + "</span>" : "") + "</div>"
			+ '<table class="jv-tab ev-tab">';
		var g = null;
		var buf = [];
		var i;
		for (i = 0; i < rows.length; i++) {
			if (rows[i].g !== g) {
				html += flushrows(buf, tip);
				buf = [];
				g = rows[i].g;
				if (g) html += '<tr><td class="kv-g" colspan="' + perline * 2 + '">' + esc(g) + "</td></tr>";
			}
			buf.push(rows[i]);
		}
		return html + flushrows(buf, tip) + "</table>";
	}

	function flushrows(buf, tip){
		var html = "";
		var j, k;
		for (j = 0; j < buf.length; j += perline) {
			html += "<tr>";
			for (k = 0; k < perline; k++) {
				html += j + k < buf.length ? cellhtml(buf[j + k], tip) : '<td class="jv-k"></td><td class="jv-v"></td>';
			}
			html += "</tr>";
		}
		return html;
	}

	function cellhtml(item, tip){
		var html = '<td class="jv-k">' + esc(item.name) + '</td><td class="jv-v">';
		var lines = String(item.text).split(/\s*(?:\n|·)\s*/);
		var j;
		for (j = 0; j < lines.length; j++) {
			if (!lines[j]) continue;
			html += '<div class="ev-line"><span>' + esc(lines[j]) + "</span>"
				+ '<button type="button" class="jv-spk" data-v="' + esc(saytext(speech(lines[j]))) + '" title="' + tip + '">'
				+ '<i class="fa-solid fa-volume-high"></i></button></div>';
		}
		return html + "</td>";
	}

	// 查词、计算各计一段；不足 1 毫秒显示 <1 ms。
	function timestr(dictms, calcms, extra){
		var a = "";
		if (dictms >= 0) a = tx("web.verb.tdict", "查词") + " " + fmtms(dictms);
		if (calcms >= 0) a += (a ? " · " : "") + tx("web.verb.tcalc", "计算") + " " + fmtms(calcms);
		if (extra) a += (a ? " · " : "") + extra;
		return a;
	}

	function fmtms(v){
		v = Math.round(v * 10) / 10;
		return v < 1 ? "<1 ms" : Math.round(v) + " ms";
	}

	function now(){
		return window.performance && window.performance.now ? window.performance.now() : Date.now();
	}

	function esc(s){
		return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}
})();
