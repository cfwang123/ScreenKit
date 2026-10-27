(function(){
	// [原形, 过去式, 过去分词, 中文释义, 英文释义, 备注[中文, 英文]]
	// 过去式、过去分词里的「/」表示常见变体，归组和复制只按第一个。
	var ROWS = [
		// AAA 三形相同
		["beset", "beset", "beset", "困扰；包围", "trouble; surround"],
		["bet", "bet / betted", "bet / betted", "打赌", "bet"],
		["bid", "bid", "bid", "出价；投标", "offer a price",
			["出价义三种形式相同；「命令」义是 bade / bidden。", "Same in all three for a price; 'command' is bade / bidden."]],
		["broadcast", "broadcast", "broadcast", "广播；播送", "broadcast"],
		["burst", "burst", "burst", "爆裂；突然发生", "burst"],
		["cast", "cast", "cast", "投掷；铸造", "throw; cast"],
		["cost", "cost", "cost", "花费；值", "cost"],
		["cut", "cut", "cut", "切；割", "cut"],
		["fit", "fit / fitted", "fit / fitted", "适合；合身", "fit"],
		["forecast", "forecast", "forecast", "预报", "forecast"],
		["hit", "hit", "hit", "打；击中", "hit"],
		["hurt", "hurt", "hurt", "伤害；疼", "hurt"],
		["input", "input", "input", "输入", "input"],
		["inset", "inset", "inset", "嵌入", "set in"],
		["knit", "knit / knitted", "knit / knitted", "编织", "knit"],
		["let", "let", "let", "让；允许", "let; allow"],
		["misread", "misread", "misread", "读错；误读", "read wrongly",
			["拼写不变，过去式读 /ˌmɪsˈred/。", "Spelled the same; the past forms sound like 'mis-red'."]],
		["offset", "offset", "offset", "抵消", "offset"],
		["output", "output", "output", "输出", "output"],
		["preset", "preset", "preset", "预设", "set in advance"],
		["put", "put", "put", "放；置", "put; place"],
		["quit", "quit / quitted", "quit / quitted", "离开；放弃", "quit"],
		["read", "read", "read", "读；阅读", "read",
			["拼写不变，过去式和过去分词读 /red/。", "Spelled the same, but the past forms sound like 'red'."]],
		["reset", "reset", "reset", "重置", "set again"],
		["rid", "rid", "rid", "摆脱", "rid"],
		["set", "set", "set", "放置；设定", "set"],
		["shed", "shed", "shed", "流下；脱落", "shed"],
		["shred", "shred", "shred", "切碎", "shred"],
		["shut", "shut", "shut", "关闭", "shut; close"],
		["slit", "slit", "slit", "切开；裂缝", "slit"],
		["split", "split", "split", "分裂；分开", "split"],
		["spread", "spread", "spread", "传播；铺开", "spread"],
		["sweat", "sweat / sweated", "sweat / sweated", "出汗", "sweat"],
		["thrust", "thrust", "thrust", "猛推；插入", "thrust"],
		["upset", "upset", "upset", "使心烦；打翻", "upset"],
		["wed", "wed / wedded", "wed / wedded", "结婚；娶；嫁", "marry"],
		["wet", "wet / wetted", "wet / wetted", "弄湿", "wet"],

		// AAB 原形与过去式相同
		["beat", "beat", "beaten", "打；击败", "beat"],

		// ABA 原形与过去分词相同
		["become", "became", "become", "成为", "become"],
		["come", "came", "come", "来", "come"],
		["outrun", "outran", "outrun", "跑得比…快", "run faster than"],
		["overcome", "overcame", "overcome", "克服", "overcome"],
		["run", "ran", "run", "跑；经营", "run"],

		// ABB 过去式与过去分词相同
		["abide", "abode / abided", "abode / abided", "居住；忍受；遵守", "stay; endure",
			["作「居住」时过去式常用 abode；作「遵守」时多用 abided。", "abode for living; abided for obeying."]],
		["alight", "alighted / alit", "alighted / alit", "下车；飞落", "get down; land",
			["alit 偏文学用法。", "alit is literary."]],
		["behold", "beheld", "beheld", "看；注视", "look at"],
		["bend", "bent", "bent", "弯曲", "bend"],
		["bereave", "bereaved / bereft", "bereaved / bereft", "使丧失（亲人）", "deprive",
			["bereft 多用于引申的「失去」。", "bereft is common in the figurative sense."]],
		["beseech", "beseeched / besought", "beseeched / besought", "恳求", "beg"],
		["bind", "bound", "bound", "捆；绑；装订", "bind"],
		["bleed", "bled", "bled", "流血", "bleed"],
		["breed", "bred", "bred", "繁殖；饲养", "breed"],
		["bring", "brought", "brought", "带来", "bring"],
		["build", "built", "built", "建造", "build"],
		["burn", "burnt / burned", "burnt / burned", "燃烧；烧毁", "burn"],
		["buy", "bought", "bought", "买", "buy"],
		["catch", "caught", "caught", "抓住；赶上", "catch"],
		["chide", "chided / chid", "chided / chid / chidden", "责骂", "scold",
			["旧式过去式 chid，过去分词 chidden。", "chid and chidden are old forms."]],
		["cleave", "cleaved / cleft / clove", "cleaved / cleft / cloven", "劈开；黏附", "split; stick",
			["「劈开」用 cleft、clove、cloven；「黏附」用 cleaved。", "cleft, clove, and cloven for splitting; cleaved for sticking."]],
		["cling", "clung", "clung", "紧抱；粘住", "cling"],
		["clothe", "clothed / clad", "clothed / clad", "给…穿衣", "dress",
			["clad 多用于固定说法，如 clad in black。", "clad is common in fixed phrases such as clad in black."]],
		["creep", "crept", "crept", "爬行；悄悄走", "creep"],
		["dare", "dared / durst", "dared", "敢", "dare",
			["durst 是旧式；作情态动词时后接动词原形。", "durst is old; as a modal it takes the bare infinitive."]],
		["deal", "dealt", "dealt", "处理；交易", "deal"],
		["dig", "dug", "dug", "挖；掘", "dig"],
		["dive", "dived / dove", "dived", "跳水；潜水", "dive",
			["美式过去式常用 dove。", "American English often uses dove."]],
		["dream", "dreamt / dreamed", "dreamt / dreamed", "做梦", "dream"],
		["dwell", "dwelt / dwelled", "dwelt / dwelled", "居住", "dwell"],
		["feed", "fed", "fed", "喂养", "feed"],
		["feel", "felt", "felt", "感觉", "feel"],
		["fight", "fought", "fought", "打仗；打架", "fight"],
		["find", "found", "found", "找到；发现", "find"],
		["flee", "fled", "fled", "逃跑", "flee"],
		["fling", "flung", "flung", "扔；抛", "fling"],
		["foretell", "foretold", "foretold", "预言", "predict"],
		["gainsay", "gainsaid", "gainsaid", "否认；反驳", "deny"],
		["geld", "gelded / gelt", "gelded / gelt", "阉割", "castrate"],
		["get", "got", "got / gotten", "得到；变得", "get",
			["英式过去分词常用 got，美式常用 gotten。", "British often uses got; American often gotten."]],
		["gild", "gilded / gilt", "gilded / gilt", "镀金", "gild"],
		["gird", "girded / girt", "girded / girt", "束紧；围绕", "bind; surround"],
		["grind", "ground", "ground", "磨碎", "grind"],
		["hang", "hung / hanged", "hung / hanged", "悬挂；绞死", "hang",
			["「悬挂」用 hung；「绞死」用 hanged。", "hung for hanging things; hanged for execution."]],
		["have", "had", "had", "有", "have"],
		["hear", "heard", "heard", "听见", "hear"],
		["heave", "heaved / hove", "heaved / hove", "举起；起伏", "lift; heave",
			["hove 多用于航海。", "hove is mostly nautical."]],
		["hold", "held", "held", "握住；举行", "hold"],
		["keep", "kept", "kept", "保持；保存", "keep"],
		["kneel", "knelt / kneeled", "knelt / kneeled", "跪下", "kneel"],
		["lay", "laid", "laid", "放置；产卵", "lay (put down)",
			["「放置」的过去式和过去分词都是 laid。", "lay (put down) has laid for both past forms."]],
		["lead", "led", "led", "领导；带领", "lead"],
		["lean", "leant / leaned", "leant / leaned", "倾斜；倚靠", "lean"],
		["leap", "leapt / leaped", "leapt / leaped", "跳；跃", "leap"],
		["learn", "learnt / learned", "learnt / learned", "学习", "learn",
			["英式常用 learnt，美式常用 learned。", "British often uses learnt; American often learned."]],
		["leave", "left", "left", "离开；留下", "leave"],
		["lend", "lent", "lent", "借出", "lend"],
		["light", "lit / lighted", "lit / lighted", "点燃；照亮", "light"],
		["lose", "lost", "lost", "丢失；输", "lose"],
		["make", "made", "made", "做；制造", "make"],
		["mean", "meant", "meant", "意思是；打算", "mean"],
		["meet", "met", "met", "遇见；见面", "meet"],
		["mishear", "misheard", "misheard", "听错", "hear wrongly"],
		["mislead", "misled", "misled", "误导", "mislead"],
		["misspell", "misspelt / misspelled", "misspelt / misspelled", "拼错", "spell wrongly",
			["英式常用 misspelt。", "British English often uses misspelt."]],
		["misunderstand", "misunderstood", "misunderstood", "误解；误会", "misunderstand"],
		["outsell", "outsold", "outsold", "卖得比…多", "sell more than"],
		["outshine", "outshone", "outshone", "比…更出色", "shine brighter than"],
		["overhang", "overhung", "overhung", "悬于…之上", "hang over"],
		["overhear", "overheard", "overheard", "无意中听到", "hear by chance"],
		["oversleep", "overslept", "overslept", "睡过头", "sleep too long"],
		["pay", "paid", "paid", "支付", "pay"],
		["plead", "pleaded / pled", "pleaded / pled", "恳求；辩护", "beg; argue"],
		["reave", "reaved / reft", "reaved / reft", "掠夺", "plunder"],
		["rebuild", "rebuilt", "rebuilt", "重建", "build again"],
		["remake", "remade", "remade", "重制", "make again"],
		["rend", "rent", "rent", "撕开", "tear"],
		["resell", "resold", "resold", "转卖", "sell again"],
		["retell", "retold", "retold", "复述", "tell again"],
		["rethink", "rethought", "rethought", "重新考虑", "think again"],
		["say", "said", "said", "说", "say"],
		["seek", "sought", "sought", "寻找", "seek"],
		["sell", "sold", "sold", "卖", "sell"],
		["send", "sent", "sent", "寄；送", "send"],
		["shine", "shone / shined", "shone / shined", "发光；擦亮", "shine",
			["「发光」用 shone；「擦亮」用 shined。", "shone for giving light; shined for polishing."]],
		["shoe", "shod", "shod", "钉马蹄铁；穿鞋", "fit with shoes"],
		["shoot", "shot", "shot", "射击；拍摄", "shoot"],
		["sit", "sat", "sat", "坐", "sit"],
		["sleep", "slept", "slept", "睡觉", "sleep"],
		["slide", "slid", "slid", "滑动", "slide"],
		["sling", "slung", "slung", "投掷；吊", "sling"],
		["slink", "slunk", "slunk", "潜逃；溜走", "slink"],
		["smell", "smelt / smelled", "smelt / smelled", "闻；嗅", "smell"],
		["speed", "sped / speeded", "sped / speeded", "加速；超速", "speed"],
		["spell", "spelt / spelled", "spelt / spelled", "拼写", "spell"],
		["spend", "spent", "spent", "花费", "spend"],
		["spill", "spilt / spilled", "spilt / spilled", "洒出；溢出", "spill"],
		["spin", "spun", "spun", "旋转；纺", "spin"],
		["spit", "spat / spit", "spat / spit", "吐唾沫", "spit",
			["美式也常用 spit / spit / spit。", "American English often uses spit, spit, spit."]],
		["spoil", "spoilt / spoiled", "spoilt / spoiled", "破坏；宠坏", "spoil"],
		["stand", "stood", "stood", "站；忍受", "stand"],
		["stave", "staved / stove", "staved / stove", "打穿；延缓", "break in",
			["stove 多用于「打穿」义。", "stove is used for breaking in."]],
		["stick", "stuck", "stuck", "刺；粘", "stick"],
		["sting", "stung", "stung", "刺；蜇", "sting"],
		["strike", "struck", "struck / stricken", "打；罢工", "strike",
			["作「被打动」时用 stricken。", "stricken when deeply affected."]],
		["string", "strung", "strung", "串起；用线穿", "string"],
		["sweep", "swept", "swept", "扫", "sweep"],
		["swing", "swung", "swung", "摆动；挥", "swing"],
		["teach", "taught", "taught", "教", "teach"],
		["tell", "told", "told", "告诉", "tell"],
		["think", "thought", "thought", "想；认为", "think"],
		["thrive", "thrived / throve", "thrived / thriven", "兴旺；茁壮成长", "prosper",
			["throve / thriven 偏旧，现代多用 thrived。", "throve and thriven are dated; thrived is usual now."]],
		["unbend", "unbent", "unbent", "伸直；放松", "straighten; relax"],
		["unbind", "unbound", "unbound", "解开", "untie"],
		["understand", "understood", "understood", "理解", "understand"],
		["unwind", "unwound", "unwound", "解开；放松", "unwind"],
		["uphold", "upheld", "upheld", "支持；维持", "support"],
		["weep", "wept", "wept", "哭泣", "weep"],
		["win", "won", "won", "赢", "win"],
		["wind", "wound", "wound", "缠绕；上弦", "wind"],
		["withhold", "withheld", "withheld", "扣留；隐瞒", "withhold"],
		["withstand", "withstood", "withstood", "抵抗；承受", "withstand"],
		["work", "worked / wrought", "worked / wrought", "工作；使运转", "work",
			["wrought 现多用于固定说法，如 wrought iron。", "wrought survives in fixed phrases such as wrought iron."]],
		["wring", "wrung", "wrung", "拧；绞", "wring"],

		// ABC 三形都不同
		["arise", "arose", "arisen", "出现；产生", "arise"],
		["awake", "awoke / awaked", "awoken / awaked", "醒来；唤醒", "awake"],
		["be", "was / were", "been", "是；存在", "be",
			["现在式 am / is / are，过去式 was / were。", "Present am/is/are; past was/were."]],
		["bear", "bore", "borne / born", "承受；生育", "bear",
			["「出生」用 born，其余义项用 borne。", "born for birth; borne otherwise."]],
		["befall", "befell", "befallen", "（坏事）发生；降临", "happen to"],
		["beget", "begot / begat", "begotten", "生育；引起", "father; cause"],
		["begin", "began", "begun", "开始", "begin"],
		["bid", "bade", "bidden", "命令；吩咐", "command",
			["也写作 bid / bid；出价义三种形式相同。", "Also bid / bid; the auction sense is the same in all three."]],
		["betake", "betook", "betaken", "前往；投身", "go; resort"],
		["bite", "bit", "bitten", "咬", "bite"],
		["blow", "blew", "blown", "吹", "blow"],
		["break", "broke", "broken", "打破；损坏", "break"],
		["choose", "chose", "chosen", "选择", "choose"],
		["do", "did", "done", "做", "do"],
		["draw", "drew", "drawn", "画；拉", "draw"],
		["drink", "drank", "drunk", "喝", "drink"],
		["drive", "drove", "driven", "驾驶；驱动", "drive"],
		["eat", "ate", "eaten", "吃", "eat"],
		["fall", "fell", "fallen", "落下；跌倒", "fall"],
		["fly", "flew", "flown", "飞", "fly"],
		["forbear", "forbore", "forborne", "克制；忍耐", "refrain"],
		["forbid", "forbade", "forbidden", "禁止", "forbid"],
		["foresee", "foresaw", "foreseen", "预见", "foresee"],
		["forget", "forgot", "forgotten", "忘记", "forget"],
		["forgive", "forgave", "forgiven", "原谅", "forgive"],
		["forgo", "forwent", "forgone", "放弃", "forgo"],
		["forsake", "forsook", "forsaken", "抛弃", "forsake"],
		["forswear", "forswore", "forsworn", "发誓放弃；作伪证", "renounce"],
		["freeze", "froze", "frozen", "结冰；冷冻", "freeze"],
		["give", "gave", "given", "给", "give"],
		["go", "went", "gone", "去；走", "go"],
		["grow", "grew", "grown", "生长；种植", "grow"],
		["hew", "hewed", "hewn / hewed", "砍；劈", "chop"],
		["hide", "hid", "hidden", "藏；隐瞒", "hide"],
		["know", "knew", "known", "知道", "know"],
		["lade", "laded", "laden / laded", "装（货）", "load",
			["laden 现在多作形容词「装满的」。", "laden is now mostly an adjective."]],
		["lie", "lay", "lain", "躺；卧", "lie down",
			["「躺」的过去式是 lay，别和「放置」的 lay 混。", "The past of lie (recline) is lay; do not confuse it with lay (put down)."]],
		["mistake", "mistook", "mistaken", "弄错；误认", "mistake"],
		["mow", "mowed", "mown / mowed", "割草", "mow"],
		["outdo", "outdid", "outdone", "胜过", "surpass"],
		["outgrow", "outgrew", "outgrown", "长大穿不下；摆脱", "grow out of",
			["常指长大后穿不下。", "Often means to grow too big for clothes."]],
		["overdo", "overdid", "overdone", "做得过分；煮过头", "do too much"],
		["overdraw", "overdrew", "overdrawn", "透支", "draw too much"],
		["overeat", "overate", "overeaten", "吃太多", "eat too much"],
		["overtake", "overtook", "overtaken", "追上；超过", "catch up with"],
		["overthrow", "overthrew", "overthrown", "推翻", "overthrow"],
		["overwrite", "overwrote", "overwritten", "覆盖写入", "write over"],
		["partake", "partook", "partaken", "参加；分享", "take part",
			["常用 partake in / partake of。", "Usually partake in or partake of."]],
		["prove", "proved", "proven / proved", "证明", "prove",
			["过去分词也常用 proved。", "proved is also common as the participle."]],
		["redo", "redid", "redone", "重做", "do again"],
		["retake", "retook", "retaken", "重考；重拍", "take again"],
		["rewrite", "rewrote", "rewritten", "重写", "write again"],
		["ride", "rode", "ridden", "骑", "ride"],
		["ring", "rang", "rung", "按铃；响", "ring"],
		["rise", "rose", "risen", "升起；起立", "rise"],
		["rive", "rived", "riven / rived", "撕裂", "split"],
		["saw", "sawed", "sawn / sawed", "锯", "saw",
			["sawn 多作形容词，如 sawn timber。", "sawn is mostly an adjective, as in sawn timber."]],
		["see", "saw", "seen", "看见", "see"],
		["seethe", "seethed", "sodden / seethed", "沸腾；涌动", "boil",
			["sodden 现在多作形容词「浸透的」。", "sodden is now mostly an adjective."]],
		["sew", "sewed", "sewn / sewed", "缝", "sew",
			["过去分词 sewn 更常见。", "sewn is the more common participle."]],
		["shake", "shook", "shaken", "摇；抖", "shake"],
		["shave", "shaved", "shaven / shaved", "剃；刮", "shave",
			["过去分词也常用 shaved。", "shaved is also common as the participle."]],
		["shear", "sheared", "shorn / sheared", "剪（羊毛）", "shear"],
		["show", "showed", "shown / showed", "展示", "show",
			["过去分词也常用 showed。", "showed is also used as the participle."]],
		["shrink", "shrank / shrunk", "shrunk", "收缩；退缩", "shrink"],
		["shrive", "shrived / shrove", "shriven / shrived", "听忏悔；忏悔", "hear confession",
			["宗教用语，shrove / shriven 是旧式。", "Religious use; shrove and shriven are old forms."]],
		["sing", "sang", "sung", "唱", "sing"],
		["sink", "sank / sunk", "sunk", "下沉；沉没", "sink"],
		["slay", "slew", "slain", "杀死", "slay"],
		["smite", "smote", "smitten", "重击", "smite"],
		["sow", "sowed", "sown / sowed", "播种", "sow"],
		["speak", "spoke", "spoken", "说话", "speak"],
		["spring", "sprang / sprung", "sprung", "跳；弹起", "spring"],
		["steal", "stole", "stolen", "偷", "steal"],
		["stink", "stank / stunk", "stunk", "发臭", "stink"],
		["strew", "strewed", "strewn / strewed", "撒；散布", "scatter"],
		["stride", "strode", "stridden", "大步走", "stride"],
		["strive", "strove / strived", "striven / strived", "努力；奋斗", "strive"],
		["swear", "swore", "sworn", "发誓；咒骂", "swear"],
		["swell", "swelled", "swollen / swelled", "膨胀；肿胀", "swell",
			["过去分词也常用 swelled。", "swelled is also common as the participle."]],
		["swim", "swam", "swum", "游泳", "swim"],
		["take", "took", "taken", "拿；取", "take"],
		["tear", "tore", "torn", "撕；扯", "tear"],
		["throw", "threw", "thrown", "扔；抛", "throw"],
		["tread", "trod", "trodden / trod", "踩；踏", "tread"],
		["undergo", "underwent", "undergone", "经历；经受", "undergo"],
		["underlie", "underlay", "underlain", "构成…的基础", "be the basis of",
			["过去式 underlay 别和名词 underlay 混。", "The past underlay is not the noun underlay."]],
		["undertake", "undertook", "undertaken", "承担；着手", "undertake"],
		["underwrite", "underwrote", "underwritten", "承保；包销", "insure; finance"],
		["undo", "undid", "undone", "解开；撤销", "undo"],
		["wake", "woke / waked", "woken / waked", "醒来；唤醒", "wake",
			["也可规则变化 waked、waked。", "Also regular: waked, waked."]],
		["wax", "waxed", "waxen / waxed", "（月亮）渐圆；变大", "grow",
			["waxen 现在多作形容词。", "waxen is now mostly an adjective."]],
		["wear", "wore", "worn", "穿；戴；磨损", "wear"],
		["weave", "wove", "woven", "编织", "weave"],
		["withdraw", "withdrew", "withdrawn", "撤回；取款", "withdraw"],
		["write", "wrote", "written", "写", "write"],
	];
	var PATS = ["AAA", "AAB", "ABA", "ABB", "ABC"];
	var GROUP = {
		AAA: ["三形相同", "all three forms the same"],
		AAB: ["原形与过去式相同", "base and past the same"],
		ABA: ["原形与过去分词相同", "base and participle the same"],
		ABB: ["过去式与过去分词相同", "past and participle the same"],
		ABC: ["三形都不同", "all three forms different"]
	};
	var LAB = {
		base: ["原形", "Base"],
		past: ["过去式", "Past"],
		pp: ["过去分词", "Past participle"],
		mean: ["释义", "Meaning"],
		type: ["类型", "Pattern"],
		note: ["备注", "Note"],
		forms: ["三种形式", "Three forms"],
		search: ["搜索：go、went、买、ABC", "Search: go, went, buy, ABC"],
		all: ["全部", "All"],
		copy: ["点击复制 %s", "Click to copy %s"],
		none: ["没有匹配的动词", "No matching verbs"],
		count: ["共 %n 个", "%n verbs"],
		hit: ["%n 个", "%n"],
		empty: ["没有这个工具", "No such tool"]
	};
	var cur = "all";
	var picked = 0;

	// 英语词形变化用的是同一张表，在 EnConj.IR 里。改这里时把那边一起改。
	if (typeof SK !== "undefined") SK.iverbrows = ROWS;

	if (typeof module !== "undefined" && module.exports) {
		module.exports = ROWS;
		return;
	}

	SK.regutil("u-iverb", {
		title: "英文不规则动词",
		go: "查询",
		live: false,
		hideGo: true,
		hideOut: true,
		body: body,
		open: open,
		relang: function(){ SK.reopenutil(); },
		run: function(){ throw new Error(tr(LAB.empty)); }
	});

	function tx(key, fb){
		var s = SK.t ? SK.t(key) : "";
		return s || fb;
	}

	function langcode(){
		var sel = F.id("lang");
		return sel && sel.value ? sel.value : "zh";
	}

	function tr(pair){
		return langcode() === "zh" ? pair[0] : pair[1];
	}

	function body(){
		return '<div class="reftab">'
			+ '<div class="uquery"><input id="iv-q" type="text" placeholder="' + esc(tr(LAB.search)) + '">'
			+ '<span id="iv-msg" class="msg"></span></div>'
			+ '<div class="refchips" id="iv-chips"></div>'
			+ '<div class="refbody">'
			+ '<div class="refscroll"><table class="reflist iverbs" id="iv-tab"></table></div>'
			+ '<aside class="refdetail" id="iv-detail"></aside>'
			+ "</div></div>";
	}

	function open(){
		var title = F.id("box-title");
		if (title) title.textContent = tx("web.tool.iverb", "英文不规则动词");
		cur = "all";
		picked = 0;
		var box = F.id("iv-q");
		if (box) box.oninput = paint;
		var chips = F.id("iv-chips");
		if (chips) chips.onclick = onchip;
		var tab = F.id("iv-tab");
		if (tab) tab.onclick = onrow;
		paintchips();
		paint();
	}

	function prim(s){
		return String(s == null ? "" : s).split("/")[0].replace(/\s+/g, " ").trim();
	}

	function pat(row){
		var b = prim(row[0]);
		var p = prim(row[1]);
		var q = prim(row[2]);
		if (b === p && p === q) return "AAA";
		if (b === p) return "AAB";
		if (b === q) return "ABA";
		if (p === q) return "ABB";
		return "ABC";
	}

	function groupcount(id){
		var i, n = 0;
		for (i = 0; i < ROWS.length; i++) if (pat(ROWS[i]) === id) n++;
		return n;
	}

	function meaning(row){
		return langcode() === "zh" ? row[3] : row[4];
	}

	function forms(row){
		return prim(row[0]) + " " + prim(row[1]) + " " + prim(row[2]);
	}

	function query(){
		return String(SK.val("iv-q") || "").trim().toLowerCase();
	}

	function hit(row, q){
		if (!q) return true;
		var blob = (row[0] + " " + row[1] + " " + row[2] + " " + row[3] + " " + row[4] + " " + pat(row)
			+ (row[5] ? " " + row[5][0] + " " + row[5][1] : "")).toLowerCase();
		var terms = q.split(/\s+/);
		var i;
		for (i = 0; i < terms.length; i++) {
			if (!terms[i]) continue;
			// 拉丁字母按词首匹配，免得查 go 时把 forgo、undergo 也带出来；中文仍按子串。
			if (/^[a-z0-9.]+$/.test(terms[i])) {
				if (!prefix(blob, terms[i])) return false;
			}
			else if (blob.indexOf(terms[i]) < 0) return false;
		}
		return true;
	}

	function prefix(blob, term){
		var words = blob.split(/[^a-z0-9.]+/);
		var i;
		for (i = 0; i < words.length; i++) if (words[i].indexOf(term) === 0) return true;
		return false;
	}

	function paintchips(){
		var html = ['<button type="button" data-g="all"' + (cur === "all" ? ' class="on"' : "") + ">"
			+ esc(tr(LAB.all)) + "<small>" + ROWS.length + "</small></button>"];
		var i, n;
		for (i = 0; i < PATS.length; i++) {
			n = groupcount(PATS[i]);
			if (!n) continue;
			html.push('<button type="button" data-g="' + PATS[i] + '" title="'
				+ esc(PATS[i] + " · " + tr(GROUP[PATS[i]])) + '"' + (cur === PATS[i] ? ' class="on"' : "") + ">"
				+ PATS[i] + "<small>" + n + "</small></button>");
		}
		F.id("iv-chips").innerHTML = html.join("");
	}

	function paint(){
		var q = query();
		var html = ["<thead><tr><th>" + esc(tr(LAB.base)) + "</th><th>" + esc(tr(LAB.past))
			+ "</th><th>" + esc(tr(LAB.pp)) + "</th><th>" + esc(tr(LAB.mean)) + "</th></tr></thead><tbody>"];
		var total = 0;
		var i, j, rows, n;
		for (i = 0; i < PATS.length; i++) {
			if (cur !== "all" && cur !== PATS[i]) continue;
			rows = [];
			for (j = 0; j < ROWS.length; j++) {
				if (pat(ROWS[j]) === PATS[i] && hit(ROWS[j], q)) rows.push(j);
			}
			if (!rows.length) continue;
			total += rows.length;
			html.push('<tr class="refgh"><td colspan="4">' + esc(PATS[i] + " · " + tr(GROUP[PATS[i]]))
				+ '<span class="n">' + rows.length + "</span></td></tr>");
			for (n = 0; n < rows.length; n++) html.push(rowhtml(rows[n]));
		}
		html.push("</tbody>");
		if (!total) html = ['<tbody><tr><td class="refnone">' + esc(tr(LAB.none)) + "</td></tr></tbody>"];
		F.id("iv-tab").innerHTML = html.join("");
		SK.msg("iv-msg", total ? (q ? tr(LAB.hit) : tr(LAB.count)).replace("%n", total) : "");
		if (total) {
			var sel = visible(picked, q) ? picked : firstvisible(q);
			if (sel >= 0) {
				picked = sel;
				showdetail(sel);
			}
		}
		else F.id("iv-detail").innerHTML = "";
	}

	function visible(i, q){
		if (i < 0 || i >= ROWS.length) return false;
		if (cur !== "all" && cur !== pat(ROWS[i])) return false;
		return hit(ROWS[i], q);
	}

	function firstvisible(q){
		var i;
		for (i = 0; i < PATS.length; i++) {
			if (cur !== "all" && cur !== PATS[i]) continue;
			var j;
			for (j = 0; j < ROWS.length; j++) {
				if (pat(ROWS[j]) === PATS[i] && hit(ROWS[j], q)) return j;
			}
		}
		return -1;
	}

	function rowhtml(i){
		var row = ROWS[i];
		return '<tr data-iv="' + i + '" title="' + esc(tr(LAB.copy).replace("%s", forms(row))) + '"'
			+ (i === picked ? ' class="on"' : "") + ">"
			+ '<td class="ivbase">' + esc(row[0]) + "</td>"
			+ "<td>" + esc(row[1]) + "</td>"
			+ "<td>" + esc(row[2]) + "</td>"
			+ "<td>" + esc(meaning(row)) + "</td></tr>";
	}

	function showdetail(i){
		var row = ROWS[i];
		if (!row) return;
		var p = pat(row);
		var html = '<div class="refbig">' + esc(row[0]) + "</div>"
			+ '<div class="refsub">' + esc(meaning(row)) + "</div>"
			+ '<dl class="refmeta">'
			+ "<dt>" + esc(tr(LAB.past)) + "</dt><dd>" + esc(row[1]) + "</dd>"
			+ "<dt>" + esc(tr(LAB.pp)) + "</dt><dd>" + esc(row[2]) + "</dd>"
			+ "<dt>" + esc(tr(LAB.type)) + "</dt><dd>" + esc(p + " · " + tr(GROUP[p])) + "</dd>";
		if (row[5]) html += "<dt>" + esc(tr(LAB.note)) + "</dt><dd>" + esc(tr(row[5])) + "</dd>";
		html += "</dl>"
			+ '<div class="refsample-t">' + esc(tr(LAB.forms)) + "</div>"
			+ '<div class="refsample">' + esc(forms(row)) + "</div>";
		F.id("iv-detail").innerHTML = html;
	}

	function onchip(ev){
		var el = ev.target;
		while (el && el !== this) {
			if (el.getAttribute && F.attr(el, "data-g")) {
				cur = F.attr(el, "data-g");
				paintchips();
				paint();
				return;
			}
			el = el.parentNode;
		}
	}

	function onrow(ev){
		var el = ev.target;
		while (el && el.tagName !== "TR") el = el.parentNode;
		if (!el || !F.attr(el, "data-iv")) return;
		var i = parseInt(F.attr(el, "data-iv"), 10);
		if (!(i >= 0 && i < ROWS.length)) return;
		picked = i;
		paint();
		showdetail(i);
		SK.copytext(forms(ROWS[i]), "iv-msg");
	}

	function esc(s){
		return String(s == null ? "" : s)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/"/g, "&quot;");
	}
})();
