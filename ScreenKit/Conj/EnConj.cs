using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ScreenKit;

/// <summary>
/// 英语词形变化：动词的十六种时态、否定、疑问、被动、情态、虚拟、祈使、附加疑问、
/// 使役、地区变体和 ain't，形容词的比较级和名词的复数。对照 Web/tools/u-everb.js 的 build()。
/// 纯函数：不读文件、不联网、无可变静态状态。
/// </summary>
static class EnConj {
	// 英语不规则动词，只保留这一份。页面从 /api/conj/irr 读取。
	// [原形, 过去式, 过去分词, 中文释义, 英文释义]，有备注时再接中文备注、英文备注。
	static readonly string[][] IR = {
		new[] { "beset", "beset", "beset", "困扰；包围", "trouble; surround" },
		new[] { "bet", "bet / betted", "bet / betted", "打赌", "bet" },
		new[] { "bid", "bid", "bid", "出价；投标", "offer a price", "出价义三种形式相同；「命令」义是 bade / bidden。", "Same in all three for a price; 'command' is bade / bidden." },
		new[] { "broadcast", "broadcast", "broadcast", "广播；播送", "broadcast" },
		new[] { "burst", "burst", "burst", "爆裂；突然发生", "burst" },
		new[] { "cast", "cast", "cast", "投掷；铸造", "throw; cast" },
		new[] { "cost", "cost", "cost", "花费；值", "cost" },
		new[] { "cut", "cut", "cut", "切；割", "cut" },
		new[] { "fit", "fit / fitted", "fit / fitted", "适合；合身", "fit" },
		new[] { "forecast", "forecast", "forecast", "预报", "forecast" },
		new[] { "hit", "hit", "hit", "打；击中", "hit" },
		new[] { "hurt", "hurt", "hurt", "伤害；疼", "hurt" },
		new[] { "input", "input", "input", "输入", "input" },
		new[] { "inset", "inset", "inset", "嵌入", "set in" },
		new[] { "knit", "knit / knitted", "knit / knitted", "编织", "knit" },
		new[] { "let", "let", "let", "让；允许", "let; allow" },
		new[] { "misread", "misread", "misread", "读错；误读", "read wrongly", "拼写不变，过去式读 /ˌmɪsˈred/。", "Spelled the same; the past forms sound like 'mis-red'." },
		new[] { "offset", "offset", "offset", "抵消", "offset" },
		new[] { "output", "output", "output", "输出", "output" },
		new[] { "preset", "preset", "preset", "预设", "set in advance" },
		new[] { "put", "put", "put", "放；置", "put; place" },
		new[] { "quit", "quit / quitted", "quit / quitted", "离开；放弃", "quit" },
		new[] { "read", "read", "read", "读；阅读", "read", "拼写不变，过去式和过去分词读 /red/。", "Spelled the same, but the past forms sound like 'red'." },
		new[] { "reset", "reset", "reset", "重置", "set again" },
		new[] { "rid", "rid", "rid", "摆脱", "rid" },
		new[] { "set", "set", "set", "放置；设定", "set" },
		new[] { "shed", "shed", "shed", "流下；脱落", "shed" },
		new[] { "shred", "shred", "shred", "切碎", "shred" },
		new[] { "shut", "shut", "shut", "关闭", "shut; close" },
		new[] { "slit", "slit", "slit", "切开；裂缝", "slit" },
		new[] { "split", "split", "split", "分裂；分开", "split" },
		new[] { "spread", "spread", "spread", "传播；铺开", "spread" },
		new[] { "sweat", "sweat / sweated", "sweat / sweated", "出汗", "sweat" },
		new[] { "thrust", "thrust", "thrust", "猛推；插入", "thrust" },
		new[] { "upset", "upset", "upset", "使心烦；打翻", "upset" },
		new[] { "wed", "wed / wedded", "wed / wedded", "结婚；娶；嫁", "marry" },
		new[] { "wet", "wet / wetted", "wet / wetted", "弄湿", "wet" },
		new[] { "beat", "beat", "beaten", "打；击败", "beat" },
		new[] { "become", "became", "become", "成为", "become" },
		new[] { "come", "came", "come", "来", "come" },
		new[] { "outrun", "outran", "outrun", "跑得比…快", "run faster than" },
		new[] { "overcome", "overcame", "overcome", "克服", "overcome" },
		new[] { "run", "ran", "run", "跑；经营", "run" },
		new[] { "abide", "abode / abided", "abode / abided", "居住；忍受；遵守", "stay; endure", "作「居住」时过去式常用 abode；作「遵守」时多用 abided。", "abode for living; abided for obeying." },
		new[] { "alight", "alighted / alit", "alighted / alit", "下车；飞落", "get down; land", "alit 偏文学用法。", "alit is literary." },
		new[] { "behold", "beheld", "beheld", "看；注视", "look at" },
		new[] { "bend", "bent", "bent", "弯曲", "bend" },
		new[] { "bereave", "bereaved / bereft", "bereaved / bereft", "使丧失（亲人）", "deprive", "bereft 多用于引申的「失去」。", "bereft is common in the figurative sense." },
		new[] { "beseech", "beseeched / besought", "beseeched / besought", "恳求", "beg" },
		new[] { "bind", "bound", "bound", "捆；绑；装订", "bind" },
		new[] { "bleed", "bled", "bled", "流血", "bleed" },
		new[] { "breed", "bred", "bred", "繁殖；饲养", "breed" },
		new[] { "bring", "brought", "brought", "带来", "bring" },
		new[] { "build", "built", "built", "建造", "build" },
		new[] { "burn", "burnt / burned", "burnt / burned", "燃烧；烧毁", "burn" },
		new[] { "buy", "bought", "bought", "买", "buy" },
		new[] { "catch", "caught", "caught", "抓住；赶上", "catch" },
		new[] { "chide", "chided / chid", "chided / chid / chidden", "责骂", "scold", "旧式过去式 chid，过去分词 chidden。", "chid and chidden are old forms." },
		new[] { "cleave", "cleaved / cleft / clove", "cleaved / cleft / cloven", "劈开；黏附", "split; stick", "「劈开」用 cleft、clove、cloven；「黏附」用 cleaved。", "cleft, clove, and cloven for splitting; cleaved for sticking." },
		new[] { "cling", "clung", "clung", "紧抱；粘住", "cling" },
		new[] { "clothe", "clothed / clad", "clothed / clad", "给…穿衣", "dress", "clad 多用于固定说法，如 clad in black。", "clad is common in fixed phrases such as clad in black." },
		new[] { "creep", "crept", "crept", "爬行；悄悄走", "creep" },
		new[] { "dare", "dared / durst", "dared", "敢", "dare", "durst 是旧式；作情态动词时后接动词原形。", "durst is old; as a modal it takes the bare infinitive." },
		new[] { "deal", "dealt", "dealt", "处理；交易", "deal" },
		new[] { "dig", "dug", "dug", "挖；掘", "dig" },
		new[] { "dive", "dived / dove", "dived", "跳水；潜水", "dive", "美式过去式常用 dove。", "American English often uses dove." },
		new[] { "dream", "dreamt / dreamed", "dreamt / dreamed", "做梦", "dream" },
		new[] { "dwell", "dwelt / dwelled", "dwelt / dwelled", "居住", "dwell" },
		new[] { "feed", "fed", "fed", "喂养", "feed" },
		new[] { "feel", "felt", "felt", "感觉", "feel" },
		new[] { "fight", "fought", "fought", "打仗；打架", "fight" },
		new[] { "find", "found", "found", "找到；发现", "find" },
		new[] { "flee", "fled", "fled", "逃跑", "flee" },
		new[] { "fling", "flung", "flung", "扔；抛", "fling" },
		new[] { "foretell", "foretold", "foretold", "预言", "predict" },
		new[] { "gainsay", "gainsaid", "gainsaid", "否认；反驳", "deny" },
		new[] { "geld", "gelded / gelt", "gelded / gelt", "阉割", "castrate" },
		new[] { "get", "got", "got / gotten", "得到；变得", "get", "英式过去分词常用 got，美式常用 gotten。", "British often uses got; American often gotten." },
		new[] { "gild", "gilded / gilt", "gilded / gilt", "镀金", "gild" },
		new[] { "gird", "girded / girt", "girded / girt", "束紧；围绕", "bind; surround" },
		new[] { "grind", "ground", "ground", "磨碎", "grind" },
		new[] { "hang", "hung / hanged", "hung / hanged", "悬挂；绞死", "hang", "「悬挂」用 hung；「绞死」用 hanged。", "hung for hanging things; hanged for execution." },
		new[] { "have", "had", "had", "有", "have" },
		new[] { "hear", "heard", "heard", "听见", "hear" },
		new[] { "heave", "heaved / hove", "heaved / hove", "举起；起伏", "lift; heave", "hove 多用于航海。", "hove is mostly nautical." },
		new[] { "hold", "held", "held", "握住；举行", "hold" },
		new[] { "keep", "kept", "kept", "保持；保存", "keep" },
		new[] { "kneel", "knelt / kneeled", "knelt / kneeled", "跪下", "kneel" },
		new[] { "lay", "laid", "laid", "放置；产卵", "lay (put down)", "「放置」的过去式和过去分词都是 laid。", "lay (put down) has laid for both past forms." },
		new[] { "lead", "led", "led", "领导；带领", "lead" },
		new[] { "lean", "leant / leaned", "leant / leaned", "倾斜；倚靠", "lean" },
		new[] { "leap", "leapt / leaped", "leapt / leaped", "跳；跃", "leap" },
		new[] { "learn", "learnt / learned", "learnt / learned", "学习", "learn", "英式常用 learnt，美式常用 learned。", "British often uses learnt; American often learned." },
		new[] { "leave", "left", "left", "离开；留下", "leave" },
		new[] { "lend", "lent", "lent", "借出", "lend" },
		new[] { "light", "lit / lighted", "lit / lighted", "点燃；照亮", "light" },
		new[] { "lose", "lost", "lost", "丢失；输", "lose" },
		new[] { "make", "made", "made", "做；制造", "make" },
		new[] { "mean", "meant", "meant", "意思是；打算", "mean" },
		new[] { "meet", "met", "met", "遇见；见面", "meet" },
		new[] { "mishear", "misheard", "misheard", "听错", "hear wrongly" },
		new[] { "mislead", "misled", "misled", "误导", "mislead" },
		new[] { "misspell", "misspelt / misspelled", "misspelt / misspelled", "拼错", "spell wrongly", "英式常用 misspelt。", "British English often uses misspelt." },
		new[] { "misunderstand", "misunderstood", "misunderstood", "误解；误会", "misunderstand" },
		new[] { "outsell", "outsold", "outsold", "卖得比…多", "sell more than" },
		new[] { "outshine", "outshone", "outshone", "比…更出色", "shine brighter than" },
		new[] { "overhang", "overhung", "overhung", "悬于…之上", "hang over" },
		new[] { "overhear", "overheard", "overheard", "无意中听到", "hear by chance" },
		new[] { "oversleep", "overslept", "overslept", "睡过头", "sleep too long" },
		new[] { "pay", "paid", "paid", "支付", "pay" },
		new[] { "plead", "pleaded / pled", "pleaded / pled", "恳求；辩护", "beg; argue" },
		new[] { "reave", "reaved / reft", "reaved / reft", "掠夺", "plunder" },
		new[] { "rebuild", "rebuilt", "rebuilt", "重建", "build again" },
		new[] { "remake", "remade", "remade", "重制", "make again" },
		new[] { "rend", "rent", "rent", "撕开", "tear" },
		new[] { "resell", "resold", "resold", "转卖", "sell again" },
		new[] { "retell", "retold", "retold", "复述", "tell again" },
		new[] { "rethink", "rethought", "rethought", "重新考虑", "think again" },
		new[] { "say", "said", "said", "说", "say" },
		new[] { "seek", "sought", "sought", "寻找", "seek" },
		new[] { "sell", "sold", "sold", "卖", "sell" },
		new[] { "send", "sent", "sent", "寄；送", "send" },
		new[] { "shine", "shone / shined", "shone / shined", "发光；擦亮", "shine", "「发光」用 shone；「擦亮」用 shined。", "shone for giving light; shined for polishing." },
		new[] { "shoe", "shod", "shod", "钉马蹄铁；穿鞋", "fit with shoes" },
		new[] { "shoot", "shot", "shot", "射击；拍摄", "shoot" },
		new[] { "sit", "sat", "sat", "坐", "sit" },
		new[] { "sleep", "slept", "slept", "睡觉", "sleep" },
		new[] { "slide", "slid", "slid", "滑动", "slide" },
		new[] { "sling", "slung", "slung", "投掷；吊", "sling" },
		new[] { "slink", "slunk", "slunk", "潜逃；溜走", "slink" },
		new[] { "smell", "smelt / smelled", "smelt / smelled", "闻；嗅", "smell" },
		new[] { "speed", "sped / speeded", "sped / speeded", "加速；超速", "speed" },
		new[] { "spell", "spelt / spelled", "spelt / spelled", "拼写", "spell" },
		new[] { "spend", "spent", "spent", "花费", "spend" },
		new[] { "spill", "spilt / spilled", "spilt / spilled", "洒出；溢出", "spill" },
		new[] { "spin", "spun", "spun", "旋转；纺", "spin" },
		new[] { "spit", "spat / spit", "spat / spit", "吐唾沫", "spit", "美式也常用 spit / spit / spit。", "American English often uses spit, spit, spit." },
		new[] { "spoil", "spoilt / spoiled", "spoilt / spoiled", "破坏；宠坏", "spoil" },
		new[] { "stand", "stood", "stood", "站；忍受", "stand" },
		new[] { "stave", "staved / stove", "staved / stove", "打穿；延缓", "break in", "stove 多用于「打穿」义。", "stove is used for breaking in." },
		new[] { "stick", "stuck", "stuck", "刺；粘", "stick" },
		new[] { "sting", "stung", "stung", "刺；蜇", "sting" },
		new[] { "strike", "struck", "struck / stricken", "打；罢工", "strike", "作「被打动」时用 stricken。", "stricken when deeply affected." },
		new[] { "string", "strung", "strung", "串起；用线穿", "string" },
		new[] { "sweep", "swept", "swept", "扫", "sweep" },
		new[] { "swing", "swung", "swung", "摆动；挥", "swing" },
		new[] { "teach", "taught", "taught", "教", "teach" },
		new[] { "tell", "told", "told", "告诉", "tell" },
		new[] { "think", "thought", "thought", "想；认为", "think" },
		new[] { "thrive", "thrived / throve", "thrived / thriven", "兴旺；茁壮成长", "prosper", "throve / thriven 偏旧，现代多用 thrived。", "throve and thriven are dated; thrived is usual now." },
		new[] { "unbend", "unbent", "unbent", "伸直；放松", "straighten; relax" },
		new[] { "unbind", "unbound", "unbound", "解开", "untie" },
		new[] { "understand", "understood", "understood", "理解", "understand" },
		new[] { "unwind", "unwound", "unwound", "解开；放松", "unwind" },
		new[] { "uphold", "upheld", "upheld", "支持；维持", "support" },
		new[] { "weep", "wept", "wept", "哭泣", "weep" },
		new[] { "win", "won", "won", "赢", "win" },
		new[] { "wind", "wound", "wound", "缠绕；上弦", "wind" },
		new[] { "withhold", "withheld", "withheld", "扣留；隐瞒", "withhold" },
		new[] { "withstand", "withstood", "withstood", "抵抗；承受", "withstand" },
		new[] { "work", "worked / wrought", "worked / wrought", "工作；使运转", "work", "wrought 现多用于固定说法，如 wrought iron。", "wrought survives in fixed phrases such as wrought iron." },
		new[] { "wring", "wrung", "wrung", "拧；绞", "wring" },
		new[] { "arise", "arose", "arisen", "出现；产生", "arise" },
		new[] { "awake", "awoke / awaked", "awoken / awaked", "醒来；唤醒", "awake" },
		new[] { "be", "was / were", "been", "是；存在", "be", "现在式 am / is / are，过去式 was / were。", "Present am/is/are; past was/were." },
		new[] { "bear", "bore", "borne / born", "承受；生育", "bear", "「出生」用 born，其余义项用 borne。", "born for birth; borne otherwise." },
		new[] { "befall", "befell", "befallen", "（坏事）发生；降临", "happen to" },
		new[] { "beget", "begot / begat", "begotten", "生育；引起", "father; cause" },
		new[] { "begin", "began", "begun", "开始", "begin" },
		new[] { "bid", "bade", "bidden", "命令；吩咐", "command", "也写作 bid / bid；出价义三种形式相同。", "Also bid / bid; the auction sense is the same in all three." },
		new[] { "betake", "betook", "betaken", "前往；投身", "go; resort" },
		new[] { "bite", "bit", "bitten", "咬", "bite" },
		new[] { "blow", "blew", "blown", "吹", "blow" },
		new[] { "break", "broke", "broken", "打破；损坏", "break" },
		new[] { "choose", "chose", "chosen", "选择", "choose" },
		new[] { "do", "did", "done", "做", "do" },
		new[] { "draw", "drew", "drawn", "画；拉", "draw" },
		new[] { "drink", "drank", "drunk", "喝", "drink" },
		new[] { "drive", "drove", "driven", "驾驶；驱动", "drive" },
		new[] { "eat", "ate", "eaten", "吃", "eat" },
		new[] { "fall", "fell", "fallen", "落下；跌倒", "fall" },
		new[] { "fly", "flew", "flown", "飞", "fly" },
		new[] { "forbear", "forbore", "forborne", "克制；忍耐", "refrain" },
		new[] { "forbid", "forbade", "forbidden", "禁止", "forbid" },
		new[] { "foresee", "foresaw", "foreseen", "预见", "foresee" },
		new[] { "forget", "forgot", "forgotten", "忘记", "forget" },
		new[] { "forgive", "forgave", "forgiven", "原谅", "forgive" },
		new[] { "forgo", "forwent", "forgone", "放弃", "forgo" },
		new[] { "forsake", "forsook", "forsaken", "抛弃", "forsake" },
		new[] { "forswear", "forswore", "forsworn", "发誓放弃；作伪证", "renounce" },
		new[] { "freeze", "froze", "frozen", "结冰；冷冻", "freeze" },
		new[] { "give", "gave", "given", "给", "give" },
		new[] { "go", "went", "gone", "去；走", "go" },
		new[] { "grow", "grew", "grown", "生长；种植", "grow" },
		new[] { "hew", "hewed", "hewn / hewed", "砍；劈", "chop" },
		new[] { "hide", "hid", "hidden", "藏；隐瞒", "hide" },
		new[] { "know", "knew", "known", "知道", "know" },
		new[] { "lade", "laded", "laden / laded", "装（货）", "load", "laden 现在多作形容词「装满的」。", "laden is now mostly an adjective." },
		new[] { "lie", "lay", "lain", "躺；卧", "lie down", "「躺」的过去式是 lay，别和「放置」的 lay 混。", "The past of lie (recline) is lay; do not confuse it with lay (put down)." },
		new[] { "mistake", "mistook", "mistaken", "弄错；误认", "mistake" },
		new[] { "mow", "mowed", "mown / mowed", "割草", "mow" },
		new[] { "outdo", "outdid", "outdone", "胜过", "surpass" },
		new[] { "outgrow", "outgrew", "outgrown", "长大穿不下；摆脱", "grow out of", "常指长大后穿不下。", "Often means to grow too big for clothes." },
		new[] { "overdo", "overdid", "overdone", "做得过分；煮过头", "do too much" },
		new[] { "overdraw", "overdrew", "overdrawn", "透支", "draw too much" },
		new[] { "overeat", "overate", "overeaten", "吃太多", "eat too much" },
		new[] { "overtake", "overtook", "overtaken", "追上；超过", "catch up with" },
		new[] { "overthrow", "overthrew", "overthrown", "推翻", "overthrow" },
		new[] { "overwrite", "overwrote", "overwritten", "覆盖写入", "write over" },
		new[] { "partake", "partook", "partaken", "参加；分享", "take part", "常用 partake in / partake of。", "Usually partake in or partake of." },
		new[] { "prove", "proved", "proven / proved", "证明", "prove", "过去分词也常用 proved。", "proved is also common as the participle." },
		new[] { "redo", "redid", "redone", "重做", "do again" },
		new[] { "retake", "retook", "retaken", "重考；重拍", "take again" },
		new[] { "rewrite", "rewrote", "rewritten", "重写", "write again" },
		new[] { "ride", "rode", "ridden", "骑", "ride" },
		new[] { "ring", "rang", "rung", "按铃；响", "ring" },
		new[] { "rise", "rose", "risen", "升起；起立", "rise" },
		new[] { "rive", "rived", "riven / rived", "撕裂", "split" },
		new[] { "saw", "sawed", "sawn / sawed", "锯", "saw", "sawn 多作形容词，如 sawn timber。", "sawn is mostly an adjective, as in sawn timber." },
		new[] { "see", "saw", "seen", "看见", "see" },
		new[] { "seethe", "seethed", "sodden / seethed", "沸腾；涌动", "boil", "sodden 现在多作形容词「浸透的」。", "sodden is now mostly an adjective." },
		new[] { "sew", "sewed", "sewn / sewed", "缝", "sew", "过去分词 sewn 更常见。", "sewn is the more common participle." },
		new[] { "shake", "shook", "shaken", "摇；抖", "shake" },
		new[] { "shave", "shaved", "shaven / shaved", "剃；刮", "shave", "过去分词也常用 shaved。", "shaved is also common as the participle." },
		new[] { "shear", "sheared", "shorn / sheared", "剪（羊毛）", "shear" },
		new[] { "show", "showed", "shown / showed", "展示", "show", "过去分词也常用 showed。", "showed is also used as the participle." },
		new[] { "shrink", "shrank / shrunk", "shrunk", "收缩；退缩", "shrink" },
		new[] { "shrive", "shrived / shrove", "shriven / shrived", "听忏悔；忏悔", "hear confession", "宗教用语，shrove / shriven 是旧式。", "Religious use; shrove and shriven are old forms." },
		new[] { "sing", "sang", "sung", "唱", "sing" },
		new[] { "sink", "sank / sunk", "sunk", "下沉；沉没", "sink" },
		new[] { "slay", "slew", "slain", "杀死", "slay" },
		new[] { "smite", "smote", "smitten", "重击", "smite" },
		new[] { "sow", "sowed", "sown / sowed", "播种", "sow" },
		new[] { "speak", "spoke", "spoken", "说话", "speak" },
		new[] { "spring", "sprang / sprung", "sprung", "跳；弹起", "spring" },
		new[] { "steal", "stole", "stolen", "偷", "steal" },
		new[] { "stink", "stank / stunk", "stunk", "发臭", "stink" },
		new[] { "strew", "strewed", "strewn / strewed", "撒；散布", "scatter" },
		new[] { "stride", "strode", "stridden", "大步走", "stride" },
		new[] { "strive", "strove / strived", "striven / strived", "努力；奋斗", "strive" },
		new[] { "swear", "swore", "sworn", "发誓；咒骂", "swear" },
		new[] { "swell", "swelled", "swollen / swelled", "膨胀；肿胀", "swell", "过去分词也常用 swelled。", "swelled is also common as the participle." },
		new[] { "swim", "swam", "swum", "游泳", "swim" },
		new[] { "take", "took", "taken", "拿；取", "take" },
		new[] { "tear", "tore", "torn", "撕；扯", "tear" },
		new[] { "throw", "threw", "thrown", "扔；抛", "throw" },
		new[] { "tread", "trod", "trodden / trod", "踩；踏", "tread" },
		new[] { "undergo", "underwent", "undergone", "经历；经受", "undergo" },
		new[] { "underlie", "underlay", "underlain", "构成…的基础", "be the basis of", "过去式 underlay 别和名词 underlay 混。", "The past underlay is not the noun underlay." },
		new[] { "undertake", "undertook", "undertaken", "承担；着手", "undertake" },
		new[] { "underwrite", "underwrote", "underwritten", "承保；包销", "insure; finance" },
		new[] { "undo", "undid", "undone", "解开；撤销", "undo" },
		new[] { "wake", "woke / waked", "woken / waked", "醒来；唤醒", "wake", "也可规则变化 waked、waked。", "Also regular: waked, waked." },
		new[] { "wax", "waxed", "waxen / waxed", "（月亮）渐圆；变大", "grow", "waxen 现在多作形容词。", "waxen is now mostly an adjective." },
		new[] { "wear", "wore", "worn", "穿；戴；磨损", "wear" },
		new[] { "weave", "wove", "woven", "编织", "weave" },
		new[] { "withdraw", "withdrew", "withdrawn", "撤回；取款", "withdraw" },
		new[] { "write", "wrote", "written", "写", "write" },
	};

	public static string[][] Irregulars => IR;

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
		["g_aint"] = new[] { "ain't（口语、方言）", "ain't (informal & dialect)" },
		["a_cop"] = new[] { "现在（am / is / are not）", "Present (am / is / are not)" },
		["a_do"] = new[] { "现在（方言 don't / doesn't，后接原形）", "Present (dialect don't / doesn't + base)" },
		["a_did"] = new[] { "过去（方言 didn't，后接原形）", "Past (dialect didn't + base)" },
		["a_ing"] = new[] { "进行（am / is / are not + -ing）", "Continuous (am / is / are not + -ing)" },
		["a_pp"] = new[] { "完成（have / has not；有的方言也当过去）", "Perfect (haven't / hasn't; some dialects use it as the past)" },
		["a_ppc"] = new[] { "完成进行（have / has not been + -ing）", "Perfect continuous (haven't / hasn't been + -ing)" },
		["a_qcop"] = new[] { "疑问 · 现在", "Question · present" },
		["a_qdo"] = new[] { "疑问 · 原形（方言）", "Question · base (dialect)" },
		["a_qing"] = new[] { "疑问 · 进行", "Question · continuous" },
		["a_qpp"] = new[] { "疑问 · 完成", "Question · perfect" },
		["a_wh"] = new[] { "特殊疑问", "Wh-question" },
		["a_tag"] = new[] { "附加疑问 · 现在", "Tag · present" },
		["a_tagc"] = new[] { "附加疑问 · 进行", "Tag · continuous" },
		["a_tagf"] = new[] { "附加疑问 · 完成", "Tag · perfect" },
		["a_pas"] = new[] { "被动（isn't / aren't，或 hasn't / haven't been）", "Passive (isn't / aren't, or hasn't / haven't been)" },
		["a_gonna"] = new[] { "将要（ain't gonna）", "ain't gonna" },
		["a_about"] = new[] { "拒绝（ain't about to）", "ain't about to" },
		["a_gotta"] = new[] { "不必（ain't gotta / ain't got to）", "ain't gotta / ain't got to" },
		["a_spose"] = new[] { "应当（ain't supposed to）", "ain't supposed to" },
		["a_got"] = new[] { "没有（ain't got）", "ain't got" },
		["a_never"] = new[] { "否定一致（ain't never）", "ain't never" },
		["a_half"] = new[] { "英式强调（ain't half）", "ain't half (British)" },
		["a_stmt"] = new[] { "陈述（isn't / aren't）", "Statement (isn't / aren't)" },
		["a_there"] = new[] { "there ain't", "there ain't" },
		["a_excl"] = new[] { "感叹", "Exclamation" },
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
		["q_hear"] = new[] { "据说", "Hearsay" },
		["g_aux"] = new[] { "助动词与搭配", "Auxiliaries & patterns" },
		["g_sent"] = new[] { "句型", "Sentence patterns" },
		["g_pron"] = new[] { "代词", "Pronoun" },
		["g_a_extra"] = new[] { "用法补充", "Usage notes" },
		["g_n_extra"] = new[] { "所有格与量词", "Possession & partitives" },
		["x_have"] = new[] { "have / get + 宾语 + 过去分词", "have / get + object + past participle" },
		["x_sense"] = new[] { "感官动词 + 原形 / -ing", "sense verb + base / -ing" },
		["x_used"] = new[] { "used to / be used to", "used to / be used to" },
		["x_going"] = new[] { "be going to / about to / be to", "be going to / about to / be to" },
		["x_ought"] = new[] { "ought to / had better / would rather", "ought to / had better / would rather" },
		["x_modpc"] = new[] { "情态 + 完成进行", "modal + perfect continuous" },
		["x_vpat"] = new[] { "接 -ing / to do 的动词", "verb + -ing / to do" },
		["x_tagx"] = new[] { "祈使与 Let's 的附加疑问", "Tags after imperatives and let's" },
		["x_contract"] = new[] { "缩写形式", "Contractions" },
		["x_phrasal"] = new[] { "短语动词分离", "Phrasal verb separation" },
		["x_rep"] = new[] { "间接引语", "Reported speech" },
		["x_rel"] = new[] { "关系从句", "Relative clause" },
		["x_inv"] = new[] { "倒装", "Inversion" },
		["x_cleft"] = new[] { "强调句", "Cleft / emphasis" },
		["x_there"] = new[] { "there be", "there be" },
		["x_embed"] = new[] { "嵌入疑问", "Embedded question" },
		["x_cond"] = new[] { "条件与让步", "Condition & concession" },
		["x_adv"] = new[] { "时间 / 让步状语从句", "Time / concession clauses" },
		["a_order"] = new[] { "形容词顺序", "Adjective order" },
		["a_order_t"] = new[] { "观点 → 大小 → 新旧 → 颜色 → 产地 → 材料（a nice big new red French wooden table）", "opinion → size → age → color → origin → material (a nice big new red French wooden table)" },
		["a_eding"] = new[] { "-ed / -ing 形容词", "-ed / -ing adjectives" },
		["a_eding_t"] = new[] { "-ed 多指人，-ing 多指物：interested / interesting", "-ed for people, -ing for things: interested / interesting" },
		["a_the"] = new[] { "the + 形容词", "the + adjective" },
		["a_mult"] = new[] { "倍数与比较句", "Multiples & comparison" },
		["n_dposs"] = new[] { "双重所有格", "Double possessive" },
		["n_part"] = new[] { "量词", "Partitives" },
		["n_compound"] = new[] { "复合名词复数", "Compound plurals" },
		["n_compound_t"] = new[] { "主体名词变复数：mothers-in-law · passers-by", "plural on the main noun: mothers-in-law · passers-by" },
		["n_coll"] = new[] { "集体名词一致", "Collective agreement" },
		["n_coll_t"] = new[] { "英式可作复数：the team is / are · the family is / are", "British can take a plural verb: the team is / are · the family is / are" },
		["p_subj"] = new[] { "主格", "Subject" },
		["p_obj"] = new[] { "宾格", "Object" },
		["p_det"] = new[] { "形容词性物主", "Possessive determiner" },
		["p_poss"] = new[] { "名词性物主", "Possessive pronoun" },
		["p_refl"] = new[] { "反身", "Reflexive" },
		["p_use"] = new[] { "用法", "Usage" }
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
		if (kind == "pron") {
			if (tail.Length > 0) return fail(L("bad"));
			r = buildpron(head, zh);
		}
		else if (kind == "a" || kind == "n") {
			if (tail.Length > 0) return fail(L("bad"));
			r = kind == "a" ? buildadj(head, zh) : buildnoun(head, zh);
		}
		else r = buildverb(head, tail, zh, obj1, obj2);
		if (r.Ok) r.Kind = kind == "a" || kind == "n" || kind == "pron" ? kind : "v";
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
		// ain't：各人称同形。代替 am/is/are not、have/has not；方言里后接原形，代替 don't/doesn't/didn't。
		g = L("g_aint");
		if (isBe) {
			R(g, L("a_cop"), "I ain't" + sp + " · you ain't" + sp + " · he ain't" + sp + " · she ain't" + sp
				+ " · it ain't" + sp + " · we ain't" + sp + " · they ain't" + sp + " · there ain't" + sp);
			R(g, L("a_qcop"), "Ain't I" + sp + "? · Ain't you" + sp + "? · Ain't he" + sp + "? · Ain't she" + sp
				+ "? · Ain't it" + sp + "? · Ain't we" + sp + "? · Ain't they" + sp + "? · Ain't there" + sp + "?");
			R(g, L("a_tag"), "I am" + sp + ", ain't I? · you are" + sp + ", ain't you? · he is" + sp + ", ain't he?"
				+ " · we are" + sp + ", ain't we? · they are" + sp + ", ain't they?");
			R(g, L("a_wh"), "Why ain't I" + sp + "? · Why ain't he" + sp + "? · Where ain't they" + sp + "?");
		}
		else {
			R(g, L("a_do"), "I ain't " + b + " · he ain't " + b + " · they ain't " + b);
			R(g, L("a_did"), "I ain't " + b + " · he ain't " + b + " · they ain't " + b);
			R(g, L("a_qdo"), "Ain't I " + b + "? · Ain't he " + b + "? · Ain't they " + b + "?");
			R(g, L("a_tag"), "you " + b + ", ain't you? · he " + s3F + ", ain't he?");
		}
		R(g, L("a_ing"), "I ain't " + ingF + " · he ain't " + ingF + " · they ain't " + ingF);
		R(g, L("a_pp"), "I ain't " + ppF + " · he ain't " + ppF + " · they ain't " + ppF);
		R(g, L("a_ppc"), "I ain't been " + ingF + " · he ain't been " + ingF);
		R(g, L("a_qing"), "Ain't I " + ingF + "? · Ain't he " + ingF + "? · Ain't they " + ingF + "?");
		R(g, L("a_qpp"), "Ain't I " + ppF + "? · Ain't he " + ppF + "? · Ain't they " + ppF + "?");
		R(g, L("a_tagc"), "you're " + ingF + ", ain't you? · he's " + ingF + ", ain't he?");
		R(g, L("a_tagf"), "you've " + ppF + ", ain't you? · he's " + ppF + ", ain't he?");
		if (!isBe) R(g, L("a_wh"), "Why ain't you " + ingF + "? · Why ain't he " + ppF + "? · Why ain't they " + b + "?");
		if (!isBe) {
			if (!has) R(g, L("a_pas"), "it ain't " + ppBare + " · they ain't " + ppBare
				+ " · it ain't been " + ppBare + " · they ain't been " + ppBare);
			else {
				objagree(o1, out var asub, out _, out _, out _);
				var arest = o2.Length > 0 ? " " + o2 : "";
				R(g, L("a_pas"), asub + " ain't " + ppBare + arest + " · " + asub + " ain't been " + ppBare + arest);
			}
		}
		R(g, L("a_gonna"), "I ain't gonna " + b + " · he ain't gonna " + b);
		R(g, L("a_about"), "I ain't about to " + b + " · he ain't about to " + b);
		R(g, L("a_gotta"), "I ain't gotta " + b + " · he ain't gotta " + b + " · I ain't got to " + b);
		R(g, L("a_spose"), "he ain't supposed to " + b + " · they ain't supposed to " + b);
		if (baseword == "have")
			R(g, L("a_got"), "I ain't got" + sp + " · he ain't got" + sp + " · they ain't got" + sp);
		R(g, L("a_never"), isBe
			? "I ain't never" + sp + " · he ain't never been" + sp
			: "I ain't never " + b + " · he ain't never " + ppF + " · he ain't never " + ingF);
		if (!isBe || has) R(g, L("a_half"), isBe ? "he ain't half" + sp : "he ain't half " + ingF);
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
		// 助动词与常见搭配
		g = L("g_aux");
		if (!isBe) {
			R(g, L("x_have"), "I had it " + ppBare + " · I got it " + ppBare);
			R(g, L("x_sense"), "I saw him " + b + " · I saw him " + ingF);
			R(g, L("x_modpc"), "must have been " + ingF + " · should have been " + ingF + " · might have been " + ingF);
			R(g, L("x_vpat"), "I enjoy " + ingF + " · I want to " + b + " · I stopped " + ingF + " / to " + b);
			if (particle.Length > 0) R(g, L("x_phrasal"), baseword + " it " + particle + " · " + baseword + " " + particle + " it ✗");
		}
		R(g, L("x_used"), "I used to " + b + " · I am used to " + ingF + " · I would " + b);
		R(g, L("x_going"), "I am going to " + b + " · I am about to " + b + " · I am to " + b + " · I am supposed to " + b);
		R(g, L("x_ought"), "I ought to " + b + " · I had better " + b + " · I would rather " + b);
		R(g, L("x_tagx"), "Let's " + b + ", shall we? · " + b + ", will you? · " + (isBe ? "I am ..., aren't I?" : "I do ..., don't I?"));
		R(g, L("x_contract"), "I'm " + ingF + " · he's " + ppF + " · I won't " + b + " · I hadn't " + ppF);
		// 句型
		g = L("g_sent");
		R(g, L("x_rep"), "He said (that) he " + pastF + " · He told me (that) he had " + ppF);
		R(g, L("x_rel"), "the man who " + s3F + " · the thing that he " + pastF);
		R(g, L("x_embed"), "I don't know whether he " + s3F + " · Do you know if he " + s3F + "?");
		R(g, L("x_cond"), "Unless he " + s3F + ", ... · Provided he " + s3F + ", ... · Should he " + b + ", ...");
		R(g, L("x_adv"), "When he " + s3F + ", ... · While " + ingF + ", ... · Although he " + s3F + ", ...");
		if (!isBe) {
			R(g, L("x_inv"), "Never did I " + b + " · Only then did I " + b + " · So did I · Neither did I");
			R(g, L("x_there"), "There is nothing to " + b + " · There was nothing to " + b);
			R(g, L("x_cleft"), "I do " + b + " · It was he who " + pastF);
		}
		else R(g, L("x_cleft"), "It is he who " + s3F + " · It was he who " + pastF);
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

	// 人称代词：主格、宾格、形容词性物主、名词性物主、反身、中文说明、英文说明。
	static readonly string[][] PRON = {
		new[] { "I", "me", "my", "mine", "myself", "第一人称单数", "first person singular" },
		new[] { "you", "you", "your", "yours", "yourself / yourselves", "第二人称", "second person" },
		new[] { "he", "him", "his", "his", "himself", "第三人称阳性单数", "third person masculine singular" },
		new[] { "she", "her", "her", "hers", "herself", "第三人称阴性单数", "third person feminine singular" },
		new[] { "it", "it", "its", "its", "itself", "第三人称中性单数", "third person neuter singular" },
		new[] { "we", "us", "our", "ours", "ourselves", "第一人称复数", "first person plural" },
		new[] { "they", "them", "their", "theirs", "themselves", "第三人称复数", "third person plural" },
		new[] { "one", "one", "one's", "—", "oneself", "泛指人", "impersonal one" },
	};

	static readonly Dictionary<string, int> PRONIDX = mkpron();

	static Dictionary<string, int> mkpron() {
		var d = new Dictionary<string, int>();
		for (var i = 0; i < PRON.Length; i++)
			for (var k = 0; k < 5; k++)
				foreach (var f in PRON[i][k].Split(new[] { " / " }, StringSplitOptions.None))
					if (f.Length > 0 && !d.ContainsKey(f)) d[f] = i;
		return d;
	}

	static ConjResult buildpron(string head, bool zh) {
		string L(string key) => zh ? LAB[key][0] : LAB[key][1];

		var p = PRON[PRONIDX.TryGetValue(head.ToLowerInvariant(), out var i) ? i : 0];
		var rows = new List<ConjRow>();
		void R(string g, string name, string text) => rows.Add(new ConjRow(name, text, g));
		var g = L("g_pron");
		R(g, L("p_subj"), p[0]);
		R(g, L("p_obj"), p[1]);
		R(g, L("p_det"), p[2] + " book");
		R(g, L("p_poss"), p[3]);
		R(g, L("p_refl"), p[4]);
		g = L("g_sent");
		R(g, L("p_use"), "between you and me · It's me. · by " + p[4]);
		return ok(p[0], zh ? p[5] : p[6], rows);
	}

	static string detect(string head, string tail) {
		if (tail.Length > 0) return "v";
		if (PRONIDX.ContainsKey(head)) return "pron";
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
		g = L("g_aint");
		R(g, L("a_stmt"), "it ain't " + baseword + " · they ain't " + baseword);
		R(g, L("a_qcop"), "Ain't it " + baseword + "? · Ain't they " + baseword + "?");
		R(g, L("a_tag"), "it's " + baseword + ", ain't it? · they're " + baseword + ", ain't they?");
		R(g, L("a_excl"), "Ain't it " + baseword + "!");
		R(g, L("a_wh"), "Why ain't it " + baseword + "? · Why ain't they " + baseword + "?");
		R(g, L("a_never"), "it ain't never " + baseword + " · they ain't never " + baseword);
		R(g, L("a_half"), "it ain't half " + baseword);
		g = L("g_a_oth");
		R(g, L("a_adv"), advform(baseword));
		R(g, L("a_noun"), adjnoun(baseword));
		R(g, L("a_grad"), NONGRAD.Contains(baseword) ? L("grad_no") : L("grad_yes"));
		g = L("g_a_extra");
		R(g, L("a_order"), L("a_order_t"));
		R(g, L("a_eding"), L("a_eding_t"));
		R(g, L("a_the"), zh ? "the rich / the poor / the young（一类人）" : "the rich / the poor / the young (a group of people)");
		R(g, L("a_mult"), "three times as " + baseword + " as · the more ..., the " + c1);
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
		g = L("g_aint");
		if (uncount) {
			R(g, L("a_stmt"), "it ain't " + baseword);
			R(g, L("a_there"), "there ain't any " + baseword + " · there ain't no " + baseword);
			R(g, L("a_qcop"), "Ain't it " + baseword + "?");
			R(g, L("a_tag"), "it's " + baseword + ", ain't it?");
			R(g, L("a_wh"), "Why ain't it " + baseword + "?");
		}
		else {
			R(g, L("a_stmt"), "it ain't " + artbase + " · they ain't " + first);
			R(g, L("a_there"), "there ain't " + article(baseword) + " " + baseword + " · there ain't no " + first);
			R(g, L("a_qcop"), "Ain't it " + artbase + "? · Ain't they " + first + "?");
			R(g, L("a_tag"), "it's " + artbase + ", ain't it? · they're " + first + ", ain't they?");
			R(g, L("a_wh"), "Why ain't it " + artbase + "? · Why ain't there any " + first + "?");
		}
		g = L("g_n_extra");
		R(g, L("n_dposs"), uncount ? ("some " + baseword + " of mine · a friend of mine") : ("a " + baseword + " of mine · a friend of mine"));
		R(g, L("n_part"), uncount ? ("a piece of " + baseword + " · a bit of " + baseword) : ("a " + baseword + " · two " + first + " · a couple of " + first));
		R(g, L("n_compound"), L("n_compound_t"));
		R(g, L("n_coll"), L("n_coll_t"));
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
				// 「锯」的原形也是 saw，和 see 的过去式相同。输入 saw 时认 see。
				if (word == "saw") continue;
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
