(function(){
	// 英语动词时态：输入原形，列出基本形式、十六种时态，以及否定、疑问、被动和其他形式。
	// [原形, 过去式, 过去分词, 中文释义, 英文释义]；没有英文词典时用这一小份兜底。
	var IR = [
		["be", "was / were", "been", "是；存在", "be"],
		["have", "had", "had", "有", "have"],
		["do", "did", "done", "做", "do"],
		["go", "went", "gone", "去；走", "go"],
		["come", "came", "come", "来", "come"],
		["get", "got", "got / gotten", "得到；变得", "get"],
		["make", "made", "made", "做；制造", "make"],
		["take", "took", "taken", "拿；取", "take"],
		["see", "saw", "seen", "看见", "see"],
		["give", "gave", "given", "给", "give"],
		["write", "wrote", "written", "写", "write"],
		["speak", "spoke", "spoken", "说话", "speak"],
		["begin", "began", "begun", "开始", "begin"],
		["run", "ran", "run", "跑；经营", "run"],
		["swim", "swam", "swum", "游泳", "swim"],
		["teach", "taught", "taught", "教", "teach"],
		["buy", "bought", "bought", "买", "buy"],
		["think", "thought", "thought", "想；认为", "think"],
		["lie", "lay", "lain", "躺；卧", "lie down"]
	];
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
	var MODALS = {
		can: 1, could: 1, may: 1, might: 1, must: 1, shall: 1, should: 1,
		will: 1, would: 1, ought: 1
	};
	// 短语动词只接这类小品词或介词，避免把两个单词的短语当成动词。
	var PARTS = {
		up: 1, down: 1, off: 1, on: 1, in: 1, out: 1, away: 1, back: 1,
		over: 1, around: 1, about: 1, through: 1, along: 1, across: 1,
		apart: 1, aside: 1, by: 1, for: 1, with: 1, after: 1, at: 1,
		into: 1, onto: 1, upon: 1, from: 1, of: 1, against: 1, together: 1,
		forward: 1, toward: 1, towards: 1
	};
	// 多音节但重音在末音节，-ing / -ed 要双写末尾辅音。
	var DOUBLE = {
		begin: 1, prefer: 1, refer: 1, occur: 1, admit: 1, permit: 1, commit: 1,
		submit: 1, omit: 1, transmit: 1, equip: 1, infer: 1, confer: 1, defer: 1,
		deter: 1, abhor: 1, allot: 1, regret: 1, forget: 1, upset: 1, reset: 1,
		preset: 1, inset: 1, input: 1, output: 1, offset: 1, beset: 1, control: 1,
		patrol: 1, propel: 1, expel: 1, compel: 1, rebel: 1, excel: 1, acquit: 1,
		quiz: 1, quit: 1, format: 1, kidnap: 1, embed: 1, transfer: 1, enrol: 1,
		fulfil: 1, distil: 1, instil: 1
	};
	var VOW = "aeiou";
	// 英式会双写词尾 l、美式不双写的常用动词（travel / cancel / model 等）。
	var LDOUBLE = {
		travel: 1, cancel: 1, model: 1, label: 1, signal: 1, total: 1, level: 1,
		tunnel: 1, channel: 1, quarrel: 1, dial: 1, spiral: 1, jewel: 1, fuel: 1,
		marvel: 1, shovel: 1, pedal: 1, rival: 1, panel: 1, funnel: 1
	};
	// 美式保留原形、英式多用 -ted 的几个动词。
	var AME_BARE = { fit: 1, quit: 1, wet: 1, wed: 1, bet: 1, knit: 1, sweat: 1 };
	// 这些词的多种写法不是英式/美式差别（含义或旧体不同），不放进地区变体。
	var NO_DIA = {
		light: 1, shine: 1, hang: 1, wake: 1, swell: 1, sew: 1, sow: 1, strew: 1,
		shear: 1, show: 1, tread: 1, wax: 1, gild: 1, gird: 1, geld: 1, clothe: 1,
		cleave: 1, chide: 1, abide: 1, beseech: 1, bereave: 1, heave: 1, dare: 1,
		alight: 1, strive: 1, speed: 1
	};
	var AME_SPECIAL = { gotten: 1, dove: 1, proven: 1, snuck: 1 };
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
		irr_n: ["不规则名词", "Irregular noun"]
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
	var ADJIRR = {
		good: ["better", "best", "好"],
		well: ["better", "best", "健康；好"],
		bad: ["worse", "worst", "坏"],
		ill: ["worse", "worst", "生病"],
		far: ["farther / further", "farthest / furthest", "远"],
		little: ["less", "least", "少；小"],
		much: ["more", "most", "多"],
		many: ["more", "most", "许多"],
		old: ["older / elder", "oldest / eldest", "老；年长"],
		late: ["later / latter", "latest / last", "晚；后"],
		near: ["nearer", "nearest / next", "近"]
	};
	var ADJFORM = {
		better: "good", best: "good", worse: "bad", worst: "bad",
		farther: "far", further: "far", farthest: "far", furthest: "far",
		less: "little", least: "little", more: "much", most: "much",
		elder: "old", eldest: "old", latter: "late", last: "late"
	};
	var NONGRAD = {
		perfect: 1, unique: 1, dead: 1, alive: 1, impossible: 1, fatal: 1, eternal: 1,
		complete: 1, entire: 1, whole: 1, infinite: 1, universal: 1, main: 1, major: 1,
		minor: 1, only: 1, same: 1, absolute: 1, inevitable: 1, total: 1
	};
	// 比较级/最高级直接用 -er / -est 之外的常见形容词，用 more / most。
	var NONINFLECT = { fun: 1, real: 1, right: 1, wrong: 1, just: 1, like: 1, worth: 1 };
	var NMAP = {
		strong: "strength", long: "length", wide: "width", deep: "depth", high: "height",
		warm: "warmth", true: "truth", young: "youth", hot: "heat", broad: "breadth",
		free: "freedom", wise: "wisdom", able: "ability", possible: "possibility",
		responsible: "responsibility", happy: "happiness", angry: "anger",
		proud: "pride", brave: "bravery", poor: "poverty", short: "shortness",
		stale: "staleness", bad: "badness",
		beautiful: "beauty", difficult: "difficulty", important: "importance",
		different: "difference", similar: "similarity", special: "specialness",
		serious: "seriousness", funny: "fun", lucky: "luck", shy: "shyness",
		calm: "calmness", polite: "politeness", rude: "rudeness", honest: "honesty",
		clever: "cleverness", smart: "smartness", stupid: "stupidity",
		lazy: "laziness", generous: "generosity", selfish: "selfishness",
		curious: "curiosity", creative: "creativity", patient: "patience",
		tired: "tiredness", excited: "excitement", interesting: "interest",
		boring: "boredom", necessary: "necessity", impossible: "impossibility",
		dangerous: "danger", expensive: "expense", safe: "safety",
		healthy: "health", hungry: "hunger", thirsty: "thirst", sleepy: "sleepiness",
		afraid: "fear", ready: "readiness", false: "falsity", real: "reality",
		simple: "simplicity", modern: "modernity", ancient: "antiquity",
		narrow: "narrowness", shallow: "shallowness", thick: "thickness",
		thin: "thinness", soft: "softness", rough: "roughness",
		smooth: "smoothness", sharp: "sharpness", dull: "dullness",
		wet: "wetness", dry: "dryness", fresh: "freshness", sweet: "sweetness",
		sour: "sourness", bitter: "bitterness", salty: "saltiness",
		spicy: "spiciness", delicious: "deliciousness", terrible: "terror",
		wonderful: "wonder", perfect: "perfection", famous: "fame",
		popular: "popularity", common: "commonness", rare: "rarity",
		cheap: "cheapness", full: "fullness", empty: "emptiness",
		busy: "busyness", dirty: "dirt", clean: "cleanliness", rich: "wealth",
		weak: "weakness", heavy: "heaviness", light: "lightness",
		dark: "darkness", bright: "brightness", quiet: "quietness",
		loud: "loudness", ugly: "ugliness", pretty: "prettiness",
		handsome: "handsomeness", nice: "niceness", kind: "kindness",
		friendly: "friendliness", careful: "carefulness", useful: "usefulness",
		useless: "uselessness"
	};
	var ADVMAP = {
		good: "well", well: "well", fast: "fast", hard: "hard", early: "early",
		late: "late", high: "high", low: "low", long: "long", far: "far",
		near: "near", straight: "straight", wrong: "wrong", right: "right",
		friendly: "in a friendly way", lovely: "in a lovely way",
		lonely: "in a lonely way", silly: "in a silly way", ugly: "in an ugly way",
		likely: "likely", true: "truly", whole: "wholly", public: "publicly",
		basic: "basically", automatic: "automatically", hard: "hard"
	};
	var NOUNIRR = {
		man: ["men"], woman: ["women"], child: ["children"], person: ["people", "persons"],
		foot: ["feet"], tooth: ["teeth"], goose: ["geese"], mouse: ["mice"],
		louse: ["lice"], ox: ["oxen"], die: ["dice"], penny: ["pence"],
		sheep: ["sheep"], deer: ["deer"], fish: ["fish", "fishes"], series: ["series"],
		species: ["species"], aircraft: ["aircraft"], offspring: ["offspring"],
		means: ["means"], analysis: ["analyses"], basis: ["bases"], crisis: ["crises"],
		thesis: ["theses"], hypothesis: ["hypotheses"], diagnosis: ["diagnoses"],
		oasis: ["oases"], axis: ["axes"], index: ["indices", "indexes"],
		appendix: ["appendices", "appendixes"], matrix: ["matrices"],
		vertex: ["vertices"], cactus: ["cacti", "cactuses"], focus: ["foci", "focuses"],
		fungus: ["fungi", "funguses"], nucleus: ["nuclei"], radius: ["radii"],
		stimulus: ["stimuli"], syllabus: ["syllabi", "syllabuses"],
		alumnus: ["alumni"], datum: ["data"], medium: ["media", "mediums"],
		bacterium: ["bacteria"], curriculum: ["curricula", "curriculums"],
		memorandum: ["memoranda", "memorandums"], phenomenon: ["phenomena"],
		criterion: ["criteria"], formula: ["formulae", "formulas"],
		vertebra: ["vertebrae"], larva: ["larvae"], nebula: ["nebulae"], alga: ["algae"],
		antenna: ["antennae", "antennas"], scarf: ["scarves", "scarfs"],
		wharf: ["wharves", "wharfs"], dwarf: ["dwarfs", "dwarves"],
		hoof: ["hooves", "hoofs"]
	};
	var UNCOUNT = {
		information: 1, advice: 1, news: 1, furniture: 1, luggage: 1, baggage: 1,
		equipment: 1, homework: 1, housework: 1, bread: 1, milk: 1, sugar: 1, salt: 1,
		rice: 1, water: 1, coffee: 1, tea: 1, air: 1, weather: 1, health: 1,
		happiness: 1, beauty: 1, music: 1, money: 1, research: 1, knowledge: 1,
		education: 1, traffic: 1, transport: 1, progress: 1, courage: 1, patience: 1,
		honesty: 1, peace: 1, pollution: 1, garbage: 1, rubbish: 1, clothing: 1,
		jewelry: 1, software: 1, hardware: 1, evidence: 1, feedback: 1, fun: 1,
		luck: 1, wealth: 1, poverty: 1, strength: 1, wisdom: 1, time: 1, work: 1,
		love: 1, art: 1
	};
	var VES = {
		leaf: 1, life: 1, knife: 1, wife: 1, wolf: 1, shelf: 1, half: 1, thief: 1,
		loaf: 1, calf: 1, elf: 1, self: 1, sheaf: 1, thiefs: 0
	};
	var OES = { potato: 1, tomato: 1, hero: 1, echo: 1, veto: 1, torpedo: 1, embargo: 1 };
	var UNCHANGED = {
		sheep: 1, deer: 1, fish: 1, series: 1, species: 1, aircraft: 1, offspring: 1,
		means: 1, salmon: 1, trout: 1, swine: 1, bison: 1, moose: 1, corps: 1
	};
	var ADJSET = {};
	var NOUNSET = {};
	var VERBSET = {};
	var i;
	for (i = 0; i < ADJS.length; i++) ADJSET[ADJS[i][0]] = 1;
	for (i = 0; i < NOUNS.length; i++) NOUNSET[NOUNS[i][0]] = 1;
	for (i = 0; i < VERBS.length; i++) VERBSET[VERBS[i]] = 1;
	var timer = 0;
	var clip = null;

	function build(word, lang, type){
		lang = lang || "zh";
		type = type || "auto";
		function L(pair){ return lang === "zh" ? pair[0] : pair[1]; }
		var parts = norm(word).split(" ");
		var head = parts[0] || "";
		var tail = parts.length > 1 ? " " + parts.slice(1).join(" ") : "";
		var i;
		if (!head || !/^[a-z][a-z-]*$/.test(head) || parts.length > 2) return { ok: false, err: L(LAB.bad) };
		for (i = 1; i < parts.length; i++) if (!PARTS[parts[i]]) return { ok: false, err: L(LAB.bad) };
		if (MODALS[head]) return { ok: false, err: L(LAB.modal) };
		var kind = type === "auto" ? detect(head, tail) : type;
		if (kind === "a" || kind === "n") {
			if (tail) return { ok: false, err: L(LAB.bad) };
			return kind === "a" ? buildadj(head, lang) : buildnoun(head, lang);
		}
		return buildverb(head, tail, lang);
	}

	function buildverb(head, tail, lang){
		lang = lang || "zh";
		function L(pair){ return lang === "zh" ? pair[0] : pair[1]; }
		var hit = findirr(head);
		var row = hit ? hit.row : null;
		var base = row ? String(row[0]).toLowerCase() : head;
		var pasts = row ? varlist(row[1]) : [];
		var pps = row ? varlist(row[2]) : [];
		var isBe = base === "be";
		var b = base + tail;
		var ingF = ing(base) + tail;
		var ppF = (pps.length ? pps[0] : ed(base)) + tail;
		var pastF = (pasts.length ? pasts[0] : ed(base)) + tail;
		var s3F = s3(base) + tail;
		if (isBe) {
			ingF = "being" + tail;
			ppF = "been" + tail;
			pastF = "was" + tail;
			s3F = "is" + tail;
		}
		// 一般现在时按 I / he 各给一个形；be 的 am / is 和 was / were 单独取。
		var p1 = isBe ? "am" : b;
		var p3 = isBe ? "is" : s3F;
		var q1 = isBe ? "was" : pastF;
		var q2 = isBe ? "were" : pastF;
		var n1 = isBe ? "am not" : "do not " + b;
		var n3 = isBe ? "is not" : "does not " + b;
		var rows = [];
		function R(g, name, text){ rows.push({ g: g, name: name, text: text }); }
		var g;
		// 基本形式
		g = L(LAB.g_form);
		R(g, L(LAB.f_base), b);
		R(g, L(LAB.f_s3), isBe ? "am / is / are" : s3F);
		R(g, L(LAB.f_ing), ingF);
		R(g, L(LAB.f_past), isBe ? "was / were" : (pasts.length ? variants(row[1], tail) : ed(base) + tail));
		R(g, L(LAB.f_pp), isBe ? "been" : (pps.length ? variants(row[2], tail) : ed(base) + tail));
		// 现在时
		g = L(LAB.g_pres);
		R(g, L(LAB.t_pres), "I " + p1 + " · he " + p3);
		R(g, L(LAB.t_pcont), "I am " + ingF + " · he is " + ingF);
		R(g, L(LAB.t_ppf), "I have " + ppF + " · he has " + ppF);
		R(g, L(LAB.t_ppc), "I have been " + ingF + " · he has been " + ingF);
		// 过去时
		g = L(LAB.g_past);
		R(g, L(LAB.t_past), "I " + q1 + (isBe ? " · they " + q2 : ""));
		R(g, L(LAB.t_pastc), "I was " + ingF + " · they were " + ingF);
		R(g, L(LAB.t_pastp), "I had " + ppF);
		R(g, L(LAB.t_pastpc), "I had been " + ingF);
		// 将来时
		g = L(LAB.g_fut);
		R(g, L(LAB.t_fut), "I will " + b);
		R(g, L(LAB.t_futc), "I will be " + ingF);
		R(g, L(LAB.t_futp), "I will have " + ppF);
		R(g, L(LAB.t_futpc), "I will have been " + ingF);
		// 过去将来时
		g = L(LAB.g_fip);
		R(g, L(LAB.t_fips), "I would " + b);
		R(g, L(LAB.t_fipc), "I would be " + ingF);
		R(g, L(LAB.t_fipp), "I would have " + ppF);
		R(g, L(LAB.t_fippc), "I would have been " + ingF);
		// 否定
		g = L(LAB.g_neg);
		R(g, L(LAB.n_pres), "I " + n1 + " · he " + n3);
		R(g, L(LAB.n_past), "I " + (isBe ? "was not · they were not" : "did not " + b));
		R(g, L(LAB.n_pcont), "I am not " + ingF + " · he is not " + ingF);
		R(g, L(LAB.n_ppf), "I have not " + ppF + " · he has not " + ppF);
		R(g, L(LAB.n_fut), "I will not " + b);
		// 疑问
		g = L(LAB.g_q);
		R(g, L(LAB.q_pres), isBe ? "Am I? · Is he?" : "Do I " + b + "? · Does he " + b + "?");
		R(g, L(LAB.q_past), isBe ? "Was I? · Were they?" : "Did I " + b + "?");
		R(g, L(LAB.q_pcont), "Am I " + ingF + "? · Is he " + ingF + "?");
		R(g, L(LAB.q_ppf), "Have I " + ppF + "? · Has he " + ppF + "?");
		R(g, L(LAB.q_fut), "Will I " + b + "?");
		// 被动语态；be 没有被动
		if (!isBe) {
			g = L(LAB.g_pas);
			R(g, L(LAB.p_pres), "It is " + ppF + " · They are " + ppF);
			R(g, L(LAB.p_past), "It was " + ppF + " · They were " + ppF);
			R(g, L(LAB.p_pcont), "It is being " + ppF);
			R(g, L(LAB.p_pastc), "It was being " + ppF);
			R(g, L(LAB.p_ppf), "It has been " + ppF);
			R(g, L(LAB.p_pastp), "It had been " + ppF);
			R(g, L(LAB.p_fut), "It will be " + ppF);
			R(g, L(LAB.p_futp), "It will have been " + ppF);
			R(g, L(LAB.p_fips), "It would be " + ppF);
		}
		// 情态动词；be 没有「be able to be」和情态被动，略过这两行
		g = L(LAB.g_mod);
		R(g, L(LAB.m_can), "can " + b + " · could " + b + " · could have " + ppF);
		R(g, L(LAB.m_may), "may " + b + " · might " + b + " · might have " + ppF);
		R(g, L(LAB.m_must), "must " + b + " · must have " + ppF);
		R(g, L(LAB.m_haveto), "have to " + b + " · had to " + b + " · will have to " + b);
		R(g, L(LAB.m_should), "should " + b + " · ought to " + b + " · should have " + ppF);
		R(g, L(LAB.m_will), "will " + b + " · would " + b + " · would have " + ppF);
		R(g, L(LAB.m_need), "need not " + b + " · dare not " + b);
		if (!isBe) {
			R(g, L(LAB.m_able), "am / is / are able to " + b + " · was / were able to " + b + " · will be able to " + b);
			R(g, L(LAB.m_pas), "can be " + ppF + " · must be " + ppF + " · should have been " + ppF);
		}
		// 虚拟与条件
		g = L(LAB.g_sub);
		R(g, L(LAB.s_pres), "I suggest that he " + b + " · it is important that he " + b);
		R(g, L(LAB.s_sub), isBe ? "AmE: that he be · BrE: that he should be"
			: "AmE: that he " + b + " · BrE: that he should " + b);
		R(g, L(LAB.s_irr), "if I " + (isBe ? "were" : pastF) + ", I would " + b);
		R(g, L(LAB.s_past), "if I had " + ppF + ", I would have " + ppF);
		R(g, L(LAB.s_mix), "if I had " + ppF + ", I would be " + ingF);
		R(g, L(LAB.s_wish), "I wish I " + (isBe ? "were" : pastF) + " · I wish I had " + ppF);
		R(g, L(LAB.s_time), "I would rather he " + (isBe ? "were" : pastF) + " · it is time he " + (isBe ? "were" : pastF));
		R(g, L(LAB.s_opt), "may he " + b);
		R(g, L(LAB.s_inv), "should he " + b + " · were he to " + b);
		// 祈使、劝诱与请求
		g = L(LAB.g_imp);
		R(g, L(LAB.i_pos), b + "! · do " + b + "! · please " + b + "!");
		R(g, L(LAB.i_neg), "do not " + b + "! · never " + b + "!");
		R(g, L(LAB.i_hort), "let's " + b + " · let's not " + b);
		R(g, L(LAB.i_shall), "shall we " + b + "? · shall I " + b + "?");
		R(g, L(LAB.i_req), "would you " + b + "? · could you " + b + "?");
		R(g, L(LAB.i_mind), "would you mind " + ingF + "?");
		R(g, L(LAB.i_adv), "why don't you " + b + "? · you had better " + b);
		// 附加疑问句
		g = L(LAB.g_tag);
		R(g, L(LAB.u_pres), isBe ? "you are, aren't you? · he is, isn't he?"
			: "you " + b + ", don't you? · he " + s3F + ", doesn't he?");
		R(g, L(LAB.u_past), isBe ? "you were, weren't you?" : "you " + pastF + ", didn't you?");
		R(g, L(LAB.u_cont), "you are " + ingF + ", aren't you?");
		R(g, L(LAB.u_perf), "you have " + ppF + ", haven't you?");
		R(g, L(LAB.u_fut), "you will " + b + ", won't you?");
		R(g, L(LAB.u_mod), "you can " + b + ", can't you?");
		// 使役与允许
		g = L(LAB.g_cau);
		R(g, L(LAB.c_make), "make him " + b + " · have him " + b + " · get him to " + b);
		R(g, L(LAB.c_let), "let him " + b + " · allow him to " + b + " · permit him to " + b);
		// 地区变体：不规则动词的 -t / -ed 写法、gotten / got、dove / dived，以及 -l 双写
		var ameP = pastF;
		var amePP = ppF;
		var breP = pastF;
		var brePP = ppF;
		var brIng = ingF;
		if (LDOUBLE[base]) {
			brIng = base + "l" + "ing" + tail;
			breP = base + "l" + "ed" + tail;
			brePP = breP;
		}
		if (!isBe) {
			if (AME_BARE[base]) {
				if (pasts.length > 1) { ameP = pasts[0] + tail; breP = pasts[pasts.length - 1] + tail; }
				if (pps.length > 1) { amePP = pps[0] + tail; brePP = pps[pps.length - 1] + tail; }
			}
			else if (!NO_DIA[base]) {
				if (pasts.length > 1) { ameP = pickame(pasts) + tail; breP = pickbre(pasts, pickame(pasts)) + tail; }
				if (pps.length > 1) { amePP = pickame(pps) + tail; brePP = pickbre(pps, pickame(pps)) + tail; }
			}
			if (base === "sneak") { ameP = "snuck" + tail; amePP = "snuck" + tail; }
		}
		// 只列真的有地区写法差别的动词
		if ((!isBe && (breP !== ameP || brePP !== amePP)) || LDOUBLE[base]) {
			g = L(LAB.g_dia);
			R(g, L(LAB.d_bre), breP + " / " + brePP + " · " + brIng);
			R(g, L(LAB.d_ame), ameP + " / " + amePP + " · " + ingF);
			R(g, L(LAB.d_aus), breP + " / " + brePP + " · " + brIng);
		}
		// 其他形式
		g = L(LAB.g_oth);
		R(g, L(LAB.o_inf), "to " + b);
		R(g, L(LAB.o_pinf), "to have " + ppF);
		R(g, L(LAB.o_ger), ingF);
		R(g, L(LAB.o_pcp), "having " + ppF);
		if (!isBe) R(g, L(LAB.o_pi), "to be " + ppF);
		// 顶部说明
		var note;
		if (row) {
			note = L(LAB.irr);
			if (pasts.length) note += " · " + L(LAB.f_past) + " " + variants(row[1], tail);
			if (pps.length) note += " · " + L(LAB.f_pp) + " " + variants(row[2], tail);
			var mean = tail ? "" : (lang === "zh" ? row[3] : row[4]);
			if (mean) note += " · " + mean;
			if (isBe) note += " · " + L(LAB.be);
			if (hit.kind !== "base") note = fmt(L(LAB.from), head + tail) + " · " + note;
		}
		else note = L(LAB.reg);
		return { ok: true, head: base + tail, note: note, rows: rows };
	}

	function detect(head, tail){
		if (tail) return "v";
		if (findirr(head) || VERBSET[head]) return "v";
		if (ADJIRR[head] || ADJSET[head] || ADJFORM[head]) return "a";
		if (singularadj(head)) return "a";
		if (NOUNIRR[head] || NOUNSET[head]) return "n";
		var sg = singularof(head);
		if (sg && !VERBSET[sg] && !ADJSET[sg] && !ADJIRR[sg]) return "n";
		if (/(ous|ful|less|able|ible|ive|ical|ic|ish|ary|ant|ent|al|like|y)$/.test(head)) return "a";
		if (/(tion|sion|ment|ness|ity|ance|ence|ship|hood|ism|ist|ee|ure|age|dom|er|or)$/.test(head)) return "n";
		if (/(ize|ise|ify|fy|ate|en)$/.test(head)) return "v";
		return "v";
	}

	function buildadj(head, lang){
		lang = lang || "zh";
		function L(pair){ return lang === "zh" ? pair[0] : pair[1]; }
		var base = head;
		var from = "";
		if (!ADJIRR[base] && !ADJSET[base]) {
			var sa = singularadj(base);
			if (sa) {
				from = head;
				base = sa;
			}
			else if (ADJFORM[base]) {
				from = head;
				base = ADJFORM[head];
			}
		}
		var irr = ADJIRR[base];
		var c, s, rule;
		if (irr) {
			c = irr[0];
			s = irr[1];
			rule = L(LAB.ar_irr);
		}
		else {
			var r = cmp(base);
			c = r.c;
			s = r.s;
			rule = L(LAB[r.rule]);
		}
		var rows = [];
		function R(g, name, text){ rows.push({ g: g, name: name, text: text }); }
		var c1 = String(c).split(" / ")[0];
		var s1 = String(s).split(" / ")[0];
		var g = L(LAB.g_a_form);
		R(g, L(LAB.a_base), base);
		R(g, L(LAB.a_comp), c);
		R(g, L(LAB.a_sup), s);
		R(g, L(LAB.a_rule), rule);
		g = L(LAB.g_a_cmp);
		R(g, L(LAB.a_as), "as " + base + " as");
		R(g, L(LAB.a_notas), "not as / so " + base + " as");
		R(g, L(LAB.a_than), c1 + " than");
		R(g, L(LAB.a_most), "the " + s1 + " (in / of ...)");
		R(g, L(LAB.a_less), "less " + base + " than · the least " + base);
		R(g, L(LAB.a_deg), "much / far / even " + c1);
		R(g, L(LAB.a_and), /^more /.test(c1) ? "more and more " + base : c1 + " and " + c1);
		g = L(LAB.g_a_oth);
		R(g, L(LAB.a_adv), advform(base));
		R(g, L(LAB.a_noun), adjnoun(base));
		R(g, L(LAB.a_grad), NONGRAD[base] ? L(LAB.grad_no) : L(LAB.grad_yes));
		var note = irr ? L(LAB.irr_a) : L(LAB.reg_a);
		if (irr) note += " · " + c + " · " + s + (lang === "zh" ? " · " + irr[2] : "");
		if (from) note = fmt(L(LAB.from), from) + " · " + note;
		return { ok: true, head: base, note: note, rows: rows };
	}

	function buildnoun(head, lang){
		lang = lang || "zh";
		function L(pair){ return lang === "zh" ? pair[0] : pair[1]; }
		var base = head;
		var from = "";
		if (!NOUNSET[base] && !NOUNIRR[base]) {
			var sg = singularof(base, true);
			if (sg && sg !== base) {
				from = base;
				base = sg;
			}
		}
		var uncount = !!UNCOUNT[base];
		var plist = NOUNIRR[base] || [plural(base)];
		var pl = uncount ? L(LAB.uncount_dash) : plist.join(" / ");
		var first = uncount ? "" : plist[0];
		var poss = base + "'s";
		var possP = uncount ? L(LAB.uncount_dash) : (/s$/.test(first) ? first + "'" : first + "'s");
		var rows = [];
		function R(g, name, text){ rows.push({ g: g, name: name, text: text }); }
		var g = L(LAB.g_n_form);
		R(g, L(LAB.n_sg), base);
		R(g, L(LAB.n_pl), pl);
		R(g, L(LAB.n_poss), poss);
		R(g, L(LAB.n_possp), possP);
		g = L(LAB.g_n_art);
		R(g, L(LAB.n_a), uncount ? L(LAB.uncount_art) : (article(base) + " " + base));
		R(g, L(LAB.n_the), "the " + base);
		R(g, L(LAB.n_count), uncount ? L(LAB.count_no) : L(LAB.count_yes));
		R(g, L(LAB.n_qty), uncount
			? "some " + base + " · much " + base + " · a lot of " + base
			: "one " + base + " · two " + first + " · some " + first);
		g = L(LAB.g_n_syn);
		R(g, L(LAB.n_agree), uncount
			? "the " + base + " is"
			: "the " + base + " is · the " + first + " are");
		R(g, L(LAB.n_vagree), uncount ? "it runs" : "it runs · they run");
		R(g, L(LAB.n_there), uncount
			? "there is " + base
			: "there is " + article(base) + " " + base + " · there are " + first);
		R(g, L(LAB.n_this), uncount
			? "this " + base + L(LAB.no_plural)
			: "this " + base + " · these " + first);
		R(g, L(LAB.n_pron), uncount ? "it" : "it · they");
		R(g, L(LAB.n_rule), uncount ? L(LAB.pl_no) : (NOUNIRR[base] ? L(LAB.pl_irr) : (UNCHANGED[base] ? L(LAB.pl_same) : L(pluralrule(base)))));
		var note = NOUNIRR[base] ? L(LAB.irr_n) : L(LAB.reg_n);
		if (!uncount) note += " · " + plist.join(" / ");
		else note += " · " + L(LAB.count_no);
		if (from) note = fmt(L(LAB.from), from) + " · " + note;
		return { ok: true, head: base, note: note, rows: rows };
	}

	function cmp(w){
		if (NONINFLECT[w]) return { c: "more " + w, s: "most " + w, rule: "ar_more" };
		if (syll(w) === 1 || /[^aeiou]y$/.test(w)) {
			if (/e$/.test(w)) return { c: w + "r", s: w + "st", rule: "ar_e" };
			if (/[^aeiou]y$/.test(w)) return { c: w.slice(0, -1) + "ier", s: w.slice(0, -1) + "iest", rule: "ar_y" };
			if (cvc(w)) return { c: w + w.slice(-1) + "er", s: w + w.slice(-1) + "est", rule: "ar_dbl" };
			return { c: w + "er", s: w + "est", rule: "ar_er" };
		}
		if (/le$/.test(w)) return { c: w + "r", s: w + "st", rule: "ar_e" };
		if (/ow$/.test(w)) return { c: w + "er", s: w + "est", rule: "ar_er" };
		if (/er$/.test(w)) return { c: w + "er", s: w + "est", rule: "ar_er" };
		return { c: "more " + w, s: "most " + w, rule: "ar_more" };
	}

	function advform(w){
		if (ADVMAP[w]) return ADVMAP[w];
		if (/[^aeiou]y$/.test(w)) return w.slice(0, -1) + "ily";
		if (/le$/.test(w)) return w.slice(0, -1) + "y";
		if (/ic$/.test(w)) return w + "ally";
		if (/ue$/.test(w)) return w.slice(0, -1) + "ly";
		if (/ll$/.test(w)) return w + "y";
		return w + "ly";
	}

	function adjnoun(w){
		if (NMAP[w]) return NMAP[w];
		if (/(shy|dry|sly|wry|spry)$/.test(w)) return w + "ness";
		if (/[^aeiou]y$/.test(w)) return w.slice(0, -1) + "iness";
		if (/able$/.test(w)) return w.slice(0, -4) + "ability";
		if (/ible$/.test(w)) return w.slice(0, -5) + "ibility";
		if (/ic$/.test(w)) return w + "ness";
		return w + "ness";
	}

	function plural(w){
		if (NOUNIRR[w]) return NOUNIRR[w][0];
		if (w.indexOf("-") > 0) {
			var at = w.indexOf("-");
			if (/-in-law$|-by$|-of-/.test(w)) return plural(w.slice(0, at)) + w.slice(at);
			var last = w.lastIndexOf("-");
			return w.slice(0, last + 1) + plural(w.slice(last + 1));
		}
		var lower = w.toLowerCase();
		if (UNCHANGED[lower]) return lower;
		if (VES[lower]) return lower.replace(/fe$/, "ves").replace(/f$/, "ves");
		if (OES[lower]) return lower + "es";
		if (/(s|x|z|ch|sh)$/.test(lower)) return lower + "es";
		if (/[^aeiou]y$/.test(lower)) return lower.slice(0, -1) + "ies";
		return lower + "s";
	}

	function pluralrule(w){
		var lower = w.toLowerCase();
		if (UNCHANGED[lower]) return LAB.pl_same;
		if (VES[lower]) return LAB.pl_ves;
		if (OES[lower]) return LAB.pl_oes;
		if (/(s|x|z|ch|sh)$/.test(lower)) return LAB.pl_es;
		if (/[^aeiou]y$/.test(lower)) return LAB.pl_ies;
		return LAB.pl_s;
	}

	function article(w){
		var lower = w.toLowerCase();
		if (/^(hour|honest|honor|honour|heir)/.test(lower)) return "an";
		if (/^(uni|use|usu|uti|eu|ewe|one|once|ubiq|uk)/.test(lower)) return "a";
		if (/^[aeiou]/.test(lower)) return "an";
		return "a";
	}

	function knownnoun(w){
		return !!(NOUNSET[w] || NOUNIRR[w]);
	}

	// 由比较级/最高级认回原级：bigger → big、happier → happy、nicer → nice。
	function singularadj(w){
		var cands = [];
		var i;
		if (/iest$/.test(w)) cands.push(w.slice(0, -4) + "y");
		if (/ier$/.test(w)) cands.push(w.slice(0, -3) + "y");
		if (/est$/.test(w)) {
			cands.push(w.slice(0, -3));
			cands.push(w.slice(0, -3) + "e");
			cands.push(w.slice(0, -4));
		}
		if (/er$/.test(w)) {
			cands.push(w.slice(0, -2));
			cands.push(w.slice(0, -2) + "e");
			cands.push(w.slice(0, -3));
		}
		for (i = 0; i < cands.length; i++) {
			if ((ADJSET[cands[i]] || ADJIRR[cands[i]]) && !VERBSET[cands[i]]) return cands[i];
		}
		return "";
	}

	function singularof(w, loose){
		if (NOUNIRR[w]) return w;
		var k, list, i;
		for (k in NOUNIRR) {
			if (!Object.prototype.hasOwnProperty.call(NOUNIRR, k)) continue;
			list = NOUNIRR[k];
			for (i = 0; i < list.length; i++) if (list[i] === w) return k;
		}
		if (/(ss|us|is|ics)$/.test(w)) return "";
		var cands = [];
		if (/ies$/.test(w)) cands.push(w.slice(0, -3) + "y");
		if (/ves$/.test(w)) {
			cands.push(w.slice(0, -3) + "f");
			cands.push(w.slice(0, -3) + "fe");
		}
		if (/oes$/.test(w)) cands.push(w.slice(0, -2));
		if (/(ses|xes|zes|ches|shes)$/.test(w)) cands.push(w.slice(0, -2));
		if (/s$/.test(w)) cands.push(w.slice(0, -1));
		for (i = 0; i < cands.length; i++) {
			if (loose ? (knownnoun(cands[i]) || VERBSET[cands[i]] || ADJSET[cands[i]]) : knownnoun(cands[i])) return cands[i];
		}
		return "";
	}

	function norm(s){
		s = String(s == null ? "" : s).toLowerCase().replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
		return s.replace(/^to /, "");
	}

	function fmt(s, v){
		return String(s).replace("%s", v);
	}

	function irs(){
		return (typeof SK !== "undefined" && SK.iverbrows) || IR;
	}

	function findirr(word){
		var rows = irs();
		var i, r;
		for (i = 0; i < rows.length; i++) {
			if (String(rows[i][0]).toLowerCase() === word) return { kind: "base", row: rows[i] };
		}
		for (i = 0; i < rows.length; i++) {
			r = rows[i];
			if (varhas(r[1], word)) return { kind: "past", row: r };
			if (varhas(r[2], word)) return { kind: "pp", row: r };
		}
		// 过去式和过去分词之外，再认第三人称单数（has、does、goes）和 am / is / are。
		for (i = 0; i < rows.length; i++) {
			r = rows[i];
			if (s3(String(r[0]).toLowerCase()) === word) return { kind: "s3", row: r };
			if (String(r[0]).toLowerCase() === "be" && (word === "am" || word === "are")) return { kind: "s3", row: r };
		}
		return null;
	}

	function varlist(field){
		var out = [];
		var parts = String(field == null ? "" : field).split("/");
		var i, s;
		for (i = 0; i < parts.length; i++) {
			s = parts[i].replace(/^\s+|\s+$/g, "").toLowerCase();
			if (s && out.indexOf(s) < 0) out.push(s);
		}
		return out;
	}

	function varhas(field, word){
		return varlist(field).indexOf(word) >= 0;
	}

	function variants(field, tail){
		var list = varlist(field);
		var i;
		if (!list.length) return "";
		for (i = 0; i < list.length; i++) list[i] += tail;
		return list.join(" / ");
	}

	// 美式优先 gotten、dove、proven 这类写法，其次 -ed；英式取另一个写法。
	function pickame(list){
		var i;
		for (i = 0; i < list.length; i++) if (AME_SPECIAL[list[i]]) return list[i];
		for (i = 0; i < list.length; i++) if (/ed$/.test(list[i])) return list[i];
		return list[0];
	}

	function pickbre(list, ame){
		var i;
		for (i = 0; i < list.length; i++) if (list[i] !== ame) return list[i];
		return ame;
	}

	function cons(ch){
		return ch >= "a" && ch <= "z" && VOW.indexOf(ch) < 0 && "wxy".indexOf(ch) < 0;
	}

	function cvc(w){
		if (w.length < 3) return false;
		var a = w.charAt(w.length - 3);
		var b = w.charAt(w.length - 2);
		var c = w.charAt(w.length - 1);
		return cons(a) && VOW.indexOf(b) >= 0 && cons(c);
	}

	function syll(w){
		var m = w.match(/[aeiouy]+/g);
		return m ? m.length : 1;
	}

	function dbl(w){
		if (DOUBLE[w]) return true;
		return syll(w) === 1 && cvc(w);
	}

	function s3(w){
		if (w === "be") return "is";
		if (w === "have") return "has";
		if (syll(w) === 1 && /[aeiou]z$/.test(w)) return w + "zes";
		if (/(s|x|z|ch|sh|o)$/.test(w)) return w + "es";
		if (/[^aeiou]y$/.test(w)) return w.slice(0, -1) + "ies";
		return w + "s";
	}

	function ing(w){
		if (w === "be") return "being";
		if (/ie$/.test(w)) return w.slice(0, -2) + "ying";
		if (/(ee|oe|ye)$/.test(w)) return w + "ing";
		if (/e$/.test(w)) return w.slice(0, -1) + "ing";
		if (/c$/.test(w)) return w + "king";
		if (dbl(w)) return w + w.slice(-1) + "ing";
		return w + "ing";
	}

	function ed(w){
		if (/e$/.test(w)) return w + "d";
		if (/[^aeiou]y$/.test(w)) return w.slice(0, -1) + "ied";
		if (/c$/.test(w)) return w + "ked";
		if (dbl(w)) return w + w.slice(-1) + "ed";
		return w + "ed";
	}

	function saytext(s){
		return String(s == null ? "" : s).replace(/·/g, ",").replace(/\s*\/\s*/g, " or ").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
	}

	if (typeof module !== "undefined" && module.exports) {
		module.exports = {
			build: build,
			findirr: findirr,
			s3: s3,
			ing: ing,
			ed: ed
		};
		return;
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
			+ '<label id="ev-word"><span data-k="web.everb.word">' + esc(tr(LAB.word)) + "</span>"
			+ SK.combo("ev-in", "walk", LAB.ph[0]) + "</label>"
			+ '<label id="ev-type"><span data-k="web.everb.type">' + esc(tr(LAB.type)) + "</span>"
			+ '<select id="ev-sel">'
			+ '<option value="auto"></option>'
			+ '<option value="v"></option>'
			+ '<option value="a"></option>'
			+ '<option value="n"></option>'
			+ "</select></label>"
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
		var out = F.id("ev-out");
		if (out && !F.attr(out, "data-bind")) {
			F.seta(out, "data-bind", "1");
			out.onclick = onout;
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

	function pickgroups(){
		var zh = langcode() === "zh";
		var groups = [];
		var cur = null;
		var i, p, name;
		for (i = 0; i < PICKS.length; i++) {
			p = PICKS[i];
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
		var box = F.id("ev-in");
		var sel = F.id("ev-sel");
		var word = norm(box ? box.value : "");
		if (!word) {
			paint("", "", []);
			SK.msg("box-msg", "");
			return;
		}
		var built = build(word, langcode(), sel ? sel.value : "auto");
		if (!built.ok) {
			paint("", "", []);
			SK.msg("box-msg", built.err, true);
			return;
		}
		paint(built.head, built.note, built.rows);
		SK.msg("box-msg", "");
	}

	function paint(head, note, rows){
		var out = F.id("ev-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; return; }
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(head) + "</b><span>" + esc(note) + "</span></div>"
			+ '<table class="jv-tab ev-tab">';
		var g = null;
		var i;
		for (i = 0; i < rows.length; i++) {
			if (rows[i].g !== g) {
				g = rows[i].g;
				html += '<tr><td class="kv-g" colspan="2">' + esc(g) + "</td></tr>";
			}
			html += '<tr><td class="jv-k">' + esc(rows[i].name) + '</td><td class="jv-v"><span>'
				+ esc(rows[i].text) + "</span>"
				+ '<button type="button" class="jv-spk" data-v="' + esc(saytext(rows[i].text)) + '" title="' + tip + '">'
				+ '<i class="fa-solid fa-volume-high"></i></button></td></tr>';
		}
		html += "</table>";
		out.innerHTML = html;
	}

	function esc(s){
		return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}
})();
