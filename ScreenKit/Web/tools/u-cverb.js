(function(){
	// 中文词形/语法变化：动词（体貌、否定、疑问、补语、把被、离合词、语气）、
	// 形容词（重叠、程度、比较、否定、语体）、名词（复数、量词、指示、语气）。
	var LAB = {
		title: ["中文词形变化", "Chinese word forms"],
		hint: ["输入中文动词、形容词或名词，类型默认自动：先查常用词表，再按词尾判断，认不出时按动词处理，也可以改类型。动词列出重叠、了/着/过、否定、疑问、补语、把字句和被字句，以及正式、口语、请求、建议、命令、感叹、推测、传闻等语气；离合词另列中间插入「了、个、着、过」和宾语前置等拆分形式。形容词列出重叠、很/非常/太、比较句、否定和语气。名词列出复数「们」、量词、指示、数量和语气。每种语气把肯定和否定放在同一格，每句一行，可以单独发音。",
			"Enter a Chinese verb, adjective or noun. Auto checks the common word lists, then the ending; if unsure it treats the word as a verb, and you can change the type. Verbs list reduplication, 了/着/过, negation, questions, complements, 把 and 被 sentences, and tones such as formal, colloquial, request, advice, command, exclamation, deduction and hearsay; separable verbs also show inserted 了/个/着/过 and fronted objects. Adjectives list reduplication, degree, comparison, negation and tone. Nouns list plural 们, classifiers, demonstratives, quantity and tone. Each tone keeps the affirmative and negative in one cell, one sentence per line, each with its own speaker."],
		word: ["单词", "Word"],
		ph: ["动词、形容词或名词，如 吃、洗澡、漂亮、学生", "A verb, adjective or noun, e.g. 吃, 洗澡, 漂亮, 学生"],
		bad: ["只支持中文词，如 吃、洗澡、漂亮、学生", "Chinese words only, e.g. 吃, 洗澡, 漂亮, 学生"],
		type: ["类型", "Type"],
		t_auto: ["自动", "Auto"],
		t_v: ["动词", "Verb"],
		t_a: ["形容词", "Adjective"],
		t_n: ["名词", "Noun"],
		pos: ["肯定", "Positive"],
		neg: ["否定", "Negative"],
		g_form: ["基本形式", "Basic forms"],
		f_base: ["原形", "Base"],
		f_red: ["重叠", "Reduplication"],
		g_asp: ["体貌", "Aspect"],
		a_le: ["了（完成）", "了 (perfective)"],
		a_zhe: ["着（持续）", "着 (continuous)"],
		a_guo: ["过（经历）", "过 (experiential)"],
		a_zai: ["正在（进行）", "正在 (progressive)"],
		a_qilai: ["起来（起始）", "起来 (inchoative)"],
		a_xiaqu: ["下去（继续）", "下去 (continuation)"],
		g_neg: ["否定", "Negation"],
		n_bu: ["不", "不"],
		n_mei: ["没（有）", "没(有)"],
		n_bie: ["别（劝阻）", "别 (prohibitive)"],
		n_buyong: ["不用", "不用"],
		g_q: ["疑问", "Questions"],
		q_ma: ["……吗？", "...吗?"],
		q_bu: ["V 不 V？", "V-not-V"],
		q_le: ["……了没有？", "...了没有?"],
		g_comp: ["补语", "Complements"],
		c_res: ["结果补语（完 / 好）", "Resultative (完 / 好)"],
		c_dir: ["趋向补语（起来 / 出来 / 下去）", "Directional"],
		c_pot: ["可能补语（得 / 不）", "Potential"],
		g_ba: ["把 / 被", "把 / 被"],
		ba: ["把字句", "把 construction"],
		bei: ["被字句", "被 construction"],
		g_tone: ["语体与语气", "Register & tone"],
		t_formal: ["正式、客气", "Formal"],
		t_coll: ["口语", "Colloquial"],
		t_req: ["请求", "Request"],
		t_adv: ["建议", "Advice"],
		t_cmd: ["命令", "Command"],
		t_excl: ["感叹", "Exclamation"],
		t_emph: ["强调", "Emphasis"],
		t_ded: ["推测", "Deduction"],
		t_hear: ["听闻", "Hearsay"],
		t_hedge: ["委婉", "Hedged"],
		t_cmp: ["比较", "Comparison"],
		n_stmt: ["陈述", "Statement"],
		note_v: ["动词", "Verb"],
		note_sep: ["离合词", "Separable verb"],
		note_a: ["形容词", "Adjective"],
		note_n: ["名词", "Noun"],
		note_np: ["名词（指人）", "Noun (person)"],
		from: ["由 %s 识别", "from %s"],
		g_sep: ["离合词拆分", "Separable verb"],
		s_le: ["插入「了」", "Inserted 了"],
		s_ge: ["插入「个」", "Inserted 个"],
		s_zhe: ["插入「着」", "Inserted 着"],
		s_guo: ["插入「过」", "Inserted 过"],
		s_red: ["重叠", "Reduplication"],
		s_q: ["正反问", "A-not-A"],
		s_front: ["宾语前置", "Fronted object"],
		s_res: ["拆分的结果补语", "Split resultative"],
		s_note: ["说明", "Note"],
		s_note_v: ["离合词中间可以插入「了、个、着、过」等，宾语也可以提前。", "The two parts can be split by 了, 个, 着 or 过, and the object can move forward."],
		g_oth: ["其他形式", "Other forms"],
		o_de: ["……的（的字结构）", "...的 (nominalizer)"],
		o_time: ["……的时候", "...的时候"],
		o_after: ["……以后 / 以前", "...以后 / ...以前"],
		o_hua: ["……的话（条件）", "...的话 (conditional)"],
		g_a_form: ["基本形式", "Basic forms"],
		a_base: ["原级", "Positive"],
		a_red: ["重叠", "Reduplication"],
		a_hen: ["很 / 非常 / 特别", "很 / 非常 / 特别"],
		a_tai: ["太……了", "太...了"],
		g_a_cmp: ["比较与程度", "Comparison & degree"],
		a_bi: ["比 + 他 + A", "比 + him + A"],
		a_meiyou: ["没有 + 他 + A", "没有 + him + A"],
		a_yiyang: ["跟 + 他 + 一样 + A", "跟 + him + same"],
		a_buru: ["不如 + 他 + A", "不如 + him + A"],
		a_yuelai: ["越来越 A", "越来越 A"],
		a_yue: ["越 A 越……", "the more A, the more..."],
		a_geng: ["更 A / 最 A", "更 A / 最 A"],
		a_yidian: ["A 一点儿", "A 一点儿"],
		g_a_neg: ["否定", "Negation"],
		a_bu: ["不 A", "不 A"],
		a_butai: ["不太 A", "不太 A"],
		a_yidianbu: ["一点儿也不 A", "一点儿也不 A"],
		a_buma: ["不 A 吗？", "不 A 吗?"],
		g_a_syn: ["的 / 地 / 得", "的 / 地 / 得"],
		a_de: ["A 的（定语）", "A 的 (attributive)"],
		a_di: ["A 地（状语）", "A 地 (adverbial)"],
		a_de2: ["A 得（补语）", "A 得 (complement)"],
		g_a_tone: ["语体与语气", "Register & tone"],
		g_n_form: ["基本形式", "Basic forms"],
		n_sg: ["单数", "Singular"],
		n_pl: ["复数（们）", "Plural (们)"],
		n_poss: ["所有格（的）", "Possessive (的)"],
		g_n_q: ["指示与数量", "Demonstratives & quantity"],
		n_this: ["这个 / 那个", "这个 / 那个"],
		n_these: ["这些 / 那些", "这些 / 那些"],
		n_cls: ["量词搭配", "With classifier"],
		n_some: ["一些 / 很多", "一些 / 很多"],
		g_n_syn: ["句法", "Syntax"],
		n_is: ["是 + N", "是 + N"],
		n_not: ["不是 + N", "不是 + N"],
		n_have: ["有 + N 吗？", "有 + N 吗?"],
		g_n_cls: ["量词", "Classifier"],
		n_cls2: ["常用量词", "Common classifiers"],
		n_cls_note: ["说明", "Note"],
		n_cls_note_v: ["人用「个 / 位」，动物用「只 / 头 / 条」，书用「本」，纸和桌子用「张」，衣服用「件」，鞋用「双」。", "People use 个/位, animals 只/头/条, books 本, paper and tables 张, clothes 件, shoes 双."],
		g_n_tone: ["语体与语气", "Register & tone"]
	};
	var VERBS = ("吃 喝 看 听 说 读 写 学 买 卖 去 来 走 跑 坐 站 睡 醒 起 回 进 出 开 关 做 干 用 想 爱 恨 " +
		"喜欢 讨厌 知道 认识 觉得 希望 需要 开始 结束 继续 帮助 谢谢 等 找 放 拿 给 送 借 还 换 修 打扫 整理 " +
		"准备 决定 参加 介绍 回答 问 告诉 认为 同意 反对 支持 解决 处理 发现 发生 出现 变成 成为 工作 学习 " +
		"休息 旅游 运动 研究 讨论 商量 欢迎 使用 检查 比较 选择 相信 担心 着急 难过 开心 笑 哭 唱 跳 玩 教").split(" ");
	var SEP = {
		"洗澡": ["洗", "澡"], "睡觉": ["睡", "觉"], "吃饭": ["吃", "饭"],
		"见面": ["见", "面"], "结婚": ["结", "婚"], "帮忙": ["帮", "忙"],
		"生气": ["生", "气"], "生病": ["生", "病"], "请假": ["请", "假"],
		"上课": ["上", "课"], "下课": ["下", "课"], "上班": ["上", "班"],
		"下班": ["下", "班"], "开会": ["开", "会"], "说话": ["说", "话"],
		"聊天": ["聊", "天"], "游泳": ["游", "泳"], "跑步": ["跑", "步"],
		"跳舞": ["跳", "舞"], "唱歌": ["唱", "歌"], "读书": ["读", "书"],
		"写字": ["写", "字"], "看病": ["看", "病"], "发烧": ["发", "烧"],
		"打电话": ["打", "电话"], "打篮球": ["打", "篮球"], "洗手": ["洗", "手"],
		"散步": ["散", "步"], "鼓掌": ["鼓", "掌"], "点头": ["点", "头"],
		"握手": ["握", "手"], "道歉": ["道", "歉"], "离婚": ["离", "婚"],
		"出名": ["出", "名"], "上当": ["上", "当"], "吃惊": ["吃", "惊"],
		"丢人": ["丢", "人"]
	};
	var ADJS = ("大 小 高 矮 长 短 快 慢 好 坏 美 丑 胖 瘦 冷 热 早 晚 新 旧 贵 便宜 多 少 对 错 " +
		"漂亮 干净 安静 高兴 快乐 忙 累 难 容易 聪明 努力 认真 可爱 有趣 无聊 舒服 健康 有名 新鲜 甜 酸 苦 辣 咸 " +
		"红 蓝 绿 黑 白 宽 窄 厚 薄 深 浅 简单 复杂 重要 特别 普通 现代 古代 年轻 老 真 假 香 臭 亮 暗 圆 方").split(" ");
	var NOUNS = ("人 学生 老师 朋友 孩子 妈妈 爸爸 哥哥 姐姐 弟弟 妹妹 同学 医生 护士 工人 农民 警察 司机 客人 邻居 同事 " +
		"经理 老板 作家 演员 歌手 运动员 记者 律师 工程师 科学家 猫 狗 鸟 鱼 牛 马 猪 鸡 书 词典 杂志 纸 桌子 椅子 床 票 " +
		"照片 衣服 鞋 帽子 眼镜 裤子 裙子 电脑 电视 手机 相机 车 自行车 房子 楼 学校 医院 公司 银行 商店 饭馆 公园 " +
		"城市 国家 山 河 路 街 苹果 香蕉 蛋糕 面包 饭 水 茶 咖啡 钱 时间 电影 故事 问题 办法 事情 工作 音乐 歌 花 树 " +
		"草 空气 天气 心情 中文 英语 日语 汉语 语言 名字 家 门 窗 灯 包 笔 本子").split(" ");
	var PERSON = {};
	("人 学生 老师 朋友 孩子 妈妈 爸爸 哥哥 姐姐 弟弟 妹妹 同学 医生 护士 工人 农民 警察 司机 客人 邻居 同事 " +
		"经理 老板 作家 演员 歌手 运动员 记者 律师 工程师 科学家").split(" ").forEach(function(w){ PERSON[w] = 1; });
	var CLS = {
		"人": "个/位", "学生": "个/名/位", "老师": "个/位", "朋友": "个/位", "孩子": "个",
		"妈妈": "个", "爸爸": "个", "哥哥": "个", "姐姐": "个", "弟弟": "个", "妹妹": "个",
		"同学": "个/位", "医生": "个/位", "护士": "个/位", "工人": "个/名", "农民": "个/名",
		"警察": "个/名", "司机": "个/位", "客人": "个/位", "邻居": "个/位", "同事": "个/位",
		"经理": "个/位", "老板": "个/位", "作家": "个/位", "演员": "个/位", "歌手": "个/位",
		"运动员": "个/名", "记者": "个/名", "律师": "个/位", "工程师": "个/位", "科学家": "个/位",
		"猫": "只", "狗": "只", "鸟": "只", "鱼": "条", "牛": "头", "马": "匹", "猪": "头", "鸡": "只",
		"书": "本", "词典": "本", "杂志": "本", "本子": "本", "笔": "支", "纸": "张",
		"桌子": "张", "椅子": "把", "床": "张", "票": "张", "照片": "张",
		"衣服": "件", "鞋": "双", "帽子": "顶", "眼镜": "副", "裤子": "条", "裙子": "条", "包": "个",
		"电脑": "台", "电视": "台", "手机": "部", "相机": "台", "车": "辆", "自行车": "辆",
		"房子": "座/栋", "楼": "座/栋", "学校": "所", "医院": "家/所", "公司": "家",
		"银行": "家", "商店": "家", "饭馆": "家", "公园": "个/座", "城市": "座", "国家": "个",
		"山": "座", "河": "条", "路": "条", "街": "条", "花": "朵", "树": "棵", "草": "根",
		"苹果": "个", "香蕉": "根", "蛋糕": "块", "面包": "个/片", "饭": "碗",
		"水": "杯/瓶", "茶": "杯", "咖啡": "杯", "钱": "块", "时间": "段",
		"电影": "部/场", "故事": "个", "问题": "个", "办法": "个", "事情": "件",
		"工作": "份", "音乐": "首/段", "歌": "首", "空气": "—", "天气": "—",
		"心情": "—", "中文": "—", "英语": "—", "日语": "—", "汉语": "—",
		"语言": "门", "名字": "个", "家": "个", "门": "扇", "窗": "扇", "灯": "盏"
	};
	var REDUP = {};
	("大 小 高 矮 长 短 快 慢 好 坏 美 冷 热 早 晚 新 旧 胖 瘦 甜 酸 苦 辣 咸 红 蓝 绿 黑 白 " +
		"漂亮 干净 安静 高兴 快乐 认真 简单 清楚 整齐 舒服 热闹 明白 老实 大方 客气").split(" ").forEach(function(w){ REDUP[w] = 1; });
	var DI = {};
	("高兴 认真 努力 安静 简单 清楚 漂亮 客气 大方 热情 仔细 小心 顺利 成功 积极 主动 耐心 痛快 轻松 自然 流利 准确 正确").split(" ").forEach(function(w){ DI[w] = 1; });
	var VERBSET = {}, ADJSET = {}, NOUNSET = {};
	var i;
	for (i = 0; i < VERBS.length; i++) VERBSET[VERBS[i]] = 1;
	for (i = 0; i < ADJS.length; i++) ADJSET[ADJS[i]] = 1;
	for (i = 0; i < NOUNS.length; i++) NOUNSET[NOUNS[i]] = 1;
	for (i in SEP) if (Object.prototype.hasOwnProperty.call(SEP, i)) VERBSET[i] = 1;
	var timer = 0;
	var clip = null;

	function build(word, lang, type){
		lang = lang || "zh";
		function L(pair){ return lang === "zh" ? pair[0] : pair[1]; }
		var w = norm(word);
		if (!w || !/^[\u4e00-\u9fa5]{1,6}$/.test(w)) return { ok: false, err: L(LAB.bad) };
		type = type || "auto";
		var kind = type === "auto" ? detect(w) : type;
		var from = "";
		if (kind === "v" && !VERBSET[w]) {
			var bse = stripverb(w);
			if (bse) {
				from = w;
				w = bse;
			}
		}
		if (kind === "a") return buildadj(w, lang);
		if (kind === "n") return buildnoun(w, lang);
		return buildverb(w, lang, from);
	}

	function detect(w){
		if (VERBSET[w]) return "v";
		if (ADJSET[w]) return "a";
		if (NOUNSET[w]) return "n";
		var base = stripverb(w);
		if (base) return "v";
		if (/[子儿头们者员家店馆院校长机票书词语]$/.test(w)) return "n";
		return "v";
	}

	function stripverb(w){
		if (w.length < 2) return "";
		var last = w.charAt(w.length - 1);
		if ("了着过".indexOf(last) >= 0 && VERBSET[w.slice(0, -1)]) return w.slice(0, -1);
		if ((w.charAt(0) === "不" || w.charAt(0) === "没" || w.charAt(0) === "别") && VERBSET[w.slice(1)]) return w.slice(1);
		var half = w.length / 2;
		if (w.length % 2 === 0 && w.slice(0, half) === w.slice(half)) return w.slice(0, half);
		if (w.length === 3 && w.charAt(0) === w.charAt(2) && VERBSET[w.slice(0, 2)]) return w.slice(0, 2);
		return "";
	}

	function P(lang, a, ngt){
		var c = lang === "zh" ? "：" : ": ";
		return lab(LAB.pos, lang) + c + a + "\n" + lab(LAB.neg, lang) + c + ngt;
	}

	function lab(pair, lang){ return lang === "zh" ? pair[0] : pair[1]; }

	function fmt(s, v){
		return String(s).replace("%s", v);
	}

	function buildverb(w, lang, from){
		var L = function(pair){ return lab(pair, lang); };
		var sep = SEP[w] || null;
		var vp = sep ? sep[0] : w;
		var op = sep ? sep[1] : "";
		var red = sep ? vp + vp + op : (w.length === 1 ? w + w : w + w);
		var rows = [];
		function R(g, name, text){ rows.push({ g: g, name: name, text: text }); }
		var g = L(LAB.g_form);
		R(g, L(LAB.f_base), w);
		R(g, L(LAB.f_red), red);
		g = L(LAB.g_asp);
		R(g, L(LAB.a_le), sep ? vp + "了" + op : w + "了");
		R(g, L(LAB.a_zhe), sep ? vp + "着" + op : w + "着");
		R(g, L(LAB.a_guo), sep ? vp + "过" + op : w + "过");
		R(g, L(LAB.a_zai), "正在" + w);
		R(g, L(LAB.a_qilai), sep ? vp + "起" + op + "来" : w + "起来");
		R(g, L(LAB.a_xiaqu), w + "下去");
		g = L(LAB.g_neg);
		R(g, L(LAB.n_bu), w === "有" ? "没有" : "不" + w);
		R(g, L(LAB.n_mei), "没" + w + "（也作「没有" + w + "」）");
		R(g, L(LAB.n_bie), "别" + w);
		R(g, L(LAB.n_buyong), "不用" + w);
		g = L(LAB.g_q);
		R(g, L(LAB.q_ma), w + "吗？");
		R(g, L(LAB.q_bu), sep ? vp + "不" + vp + op + "？" : w + "不" + w + "？");
		R(g, L(LAB.q_le), w + "了没有？");
		g = L(LAB.g_comp);
		R(g, L(LAB.c_res), sep ? vp + "完" + op + " · " + vp + "好" + op : w + "完 · " + w + "好");
		R(g, L(LAB.c_dir), sep ? vp + "起" + op + "来 · " + w + "下去" : w + "起来 · " + w + "下去");		R(g, L(LAB.c_pot), sep ? "—（离合词一般说「" + vp + "得上" + op + " / " + vp + "不上" + op + "」）" : w + "得完 · " + w + "不完");
		g = L(LAB.g_ba);
		R(g, L(LAB.ba), "把 + 宾语 + " + w + "了");
		R(g, L(LAB.bei), "被 + 施事 + " + w + "了");
		g = L(LAB.g_tone);
		R(g, L(LAB.t_formal), P(lang, "请您" + w, "请您不要" + w));
		R(g, L(LAB.t_coll), P(lang, w + "一下", "别" + w + "了"));
		R(g, L(LAB.t_req), P(lang, "能不能" + w + "？", "能不能不" + w + "？"));
		R(g, L(LAB.t_adv), P(lang, "还是" + w + "吧", "还是别" + w + "吧"));
		R(g, L(LAB.t_cmd), P(lang, "快" + w + "！", "别" + w + "！"));
		R(g, L(LAB.t_excl), P(lang, "真" + w + "了！", "真没" + w + "！"));
		R(g, L(LAB.t_emph), P(lang, w + "是" + w + "了", "不" + w + "是不" + w));
		R(g, L(LAB.t_ded), P(lang, "可能" + w, "可能没" + w));
		R(g, L(LAB.t_hear), P(lang, "听说" + w, "听说没" + w));
		if (sep) {
			g = L(LAB.g_sep);
			R(g, L(LAB.s_le), vp + "了" + op);
			R(g, L(LAB.s_ge), vp + "个" + op);
			R(g, L(LAB.s_zhe), vp + "着" + op);
			R(g, L(LAB.s_guo), vp + "过" + op);
			R(g, L(LAB.s_red), vp + vp + op);
			R(g, L(LAB.s_q), vp + "不" + vp + op + "？");
			R(g, L(LAB.s_front), op + vp + "了");
			R(g, L(LAB.s_res), vp + "完" + op);
			R(g, L(LAB.s_note), L(LAB.s_note_v));
		}
		g = L(LAB.g_oth);
		R(g, L(LAB.o_de), w + "的");
		R(g, L(LAB.o_time), w + "的时候");
		R(g, L(LAB.o_after), w + "以后 · " + w + "以前");
		R(g, L(LAB.o_hua), w + "的话");
		var note = sep ? L(LAB.note_sep) : L(LAB.note_v);
		if (from) note = fmt(L(LAB.from), from) + " · " + note;
		return { ok: true, head: w, note: note, rows: rows };
	}

	function buildadj(w, lang){
		var L = function(pair){ return lab(pair, lang); };
		var rows = [];
		function R(g, name, text){ rows.push({ g: g, name: name, text: text }); }
		var g = L(LAB.g_a_form);
		R(g, L(LAB.a_base), w);
		R(g, L(LAB.a_red), REDUP[w] ? (w.length === 1 ? w + w : w.charAt(0) + w.charAt(0) + w.charAt(1) + w.charAt(1)) : "—（一般不重叠）");
		R(g, L(LAB.a_hen), "很" + w + " · 非常" + w + " · 特别" + w);
		R(g, L(LAB.a_tai), "太" + w + "了");
		g = L(LAB.g_a_cmp);
		R(g, L(LAB.a_bi), "比他" + w);
		R(g, L(LAB.a_meiyou), "没有他" + w);
		R(g, L(LAB.a_yiyang), "跟他一样" + w);
		R(g, L(LAB.a_buru), "不如他" + w);
		R(g, L(LAB.a_yuelai), "越来越" + w);
		R(g, L(LAB.a_yue), "越" + w + "越……");
		R(g, L(LAB.a_geng), "更" + w + " · 最" + w);
		R(g, L(LAB.a_yidian), w + "一点儿");
		g = L(LAB.g_a_neg);
		R(g, L(LAB.a_bu), "不" + w);
		R(g, L(LAB.a_butai), "不太" + w);
		R(g, L(LAB.a_yidianbu), "一点儿也不" + w);
		R(g, L(LAB.a_buma), "不" + w + "吗？");
		g = L(LAB.g_a_syn);
		R(g, L(LAB.a_de), w + "的（人 / 东西）");
		R(g, L(LAB.a_di), DI[w] ? w + "地（做）" : "—（一般不直接作状语）");
		R(g, L(LAB.a_de2), "做得很" + w);
		g = L(LAB.g_a_tone);
		R(g, L(LAB.t_excl), P(lang, "多" + w + "啊！", "一点儿也不" + w + "！"));
		R(g, L(LAB.t_emph), P(lang, w + "极了", "一点儿也不" + w));
		R(g, L(LAB.t_hedge), P(lang, "有点儿" + w, "不太" + w));
		R(g, L(LAB.t_ded), P(lang, "可能" + w, "可能不" + w));
		R(g, L(LAB.t_hear), P(lang, "听说" + w, "听说不" + w));
		R(g, L(LAB.t_cmp), P(lang, "更" + w, "更不" + w));
		return { ok: true, head: w, note: L(LAB.note_a), rows: rows };
	}

	function buildnoun(w, lang){
		var L = function(pair){ return lab(pair, lang); };
		var person = !!PERSON[w];
		var cls = CLS[w] || "个";
		var cls1 = cls.split("/")[0];
		var rows = [];
		function R(g, name, text){ rows.push({ g: g, name: name, text: text }); }
		var g = L(LAB.g_n_form);
		R(g, L(LAB.n_sg), w);
		R(g, L(LAB.n_pl), person ? w + "们" : "—（一般不加「们」，用「一些" + w + "」）");
		R(g, L(LAB.n_poss), w + "的");
		g = L(LAB.g_n_q);
		R(g, L(LAB.n_this), "这个" + w + " · 那个" + w);
		R(g, L(LAB.n_these), "这些" + w + " · 那些" + w);
		R(g, L(LAB.n_cls), cls === "—" ? "—（不加量词）" : "一" + cls1 + w + " · 两" + cls1 + w);
		R(g, L(LAB.n_some), "一些" + w + " · 很多" + w);
		g = L(LAB.g_n_syn);
		R(g, L(LAB.n_is), "这是" + w);
		R(g, L(LAB.n_not), "这不是" + w);
		R(g, L(LAB.n_have), "有" + w + "吗？");
		g = L(LAB.g_n_cls);
		R(g, L(LAB.n_cls2), cls);
		R(g, L(LAB.n_cls_note), L(LAB.n_cls_note_v));
		g = L(LAB.g_n_tone);
		R(g, L(LAB.n_stmt), P(lang, "这是" + w, "这不是" + w));
		R(g, L(LAB.t_excl), P(lang, "多好的" + w + "啊！", "这算什么" + w + "！"));
		R(g, L(LAB.t_ded), P(lang, "可能是" + w, "可能不是" + w));
		R(g, L(LAB.t_hear), P(lang, "听说是" + w, "听说不是" + w));
		R(g, L(LAB.t_emph), P(lang, "就是" + w, "根本不是" + w));
		return { ok: true, head: w, note: person ? L(LAB.note_np) : L(LAB.note_n), rows: rows };
	}

	function norm(s){
		return String(s == null ? "" : s).replace(/\s+/g, "").replace(/[，。？！、；：]/g, "");
	}

	function speech(s){
		return String(s == null ? "" : s).replace(/^(肯定|否定|Positive|Negative)[:：]\s*/i, "");
	}

	function saytext(s){
		return String(s == null ? "" : s).replace(/·/g, "，").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
	}

	if (typeof module !== "undefined" && module.exports) {
		module.exports = { build: build, detect: detect };
		return;
	}
	if (typeof SK === "undefined" || !SK.regutil) return;

	SK.regutil("u-cverb", {
		title: "中文词形变化",
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
		return typeof SK !== "undefined" && SK.t ? SK.t(key, fb) : fb;
	}

	function langcode(){
		return (typeof LANG !== "undefined" && LANG) || "zh";
	}

	function tr(pair){ return lab(pair, langcode()); }

	function esc(s){
		return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}

	function topick(){
		return [
			["v", "吃", "吃"], ["v", "学习", "学习"], ["v", "喜欢", "喜欢"], ["v", "看", "看"],
			["sep", "洗澡", "洗澡"], ["sep", "见面", "见面"], ["sep", "打电话", "打电话"],
			["a", "漂亮", "漂亮"], ["a", "高兴", "高兴"], ["a", "干净", "干净"], ["a", "大", "大"],
			["n", "学生", "学生"], ["n", "书", "书"], ["n", "苹果", "苹果"], ["n", "猫", "猫"]
		];
	}

	function body(){
		return '<p class="hint" id="cv-hint" data-k="web.cverb.hint">' + esc(tr(LAB.hint)) + "</p>"
			+ '<div class="row" id="cv-row">'
			+ '<label id="cv-word"><span data-k="web.everb.word">' + esc(tr(LAB.word)) + "</span>"
			+ SK.combo("cv-in", "吃", LAB.ph[0]) + "</label>"
			+ '<label id="cv-type"><span data-k="web.everb.type">' + esc(tr(LAB.type)) + "</span>"
			+ '<select id="cv-sel">'
			+ '<option value="auto"></option>'
			+ '<option value="v"></option>'
			+ '<option value="a"></option>'
			+ '<option value="n"></option>'
			+ "</select></label>"
			+ "</div>"
			+ '<div id="cv-out"></div>';
	}

	function setopt(value, key, fb){
		var sel = F.id("cv-sel");
		if (!sel) return;
		for (var i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === value) sel.options[i].textContent = tx(key, tr(fb));
		}
	}

	function open(){
		var title = F.id("box-title");
		if (title) title.textContent = tx("web.tool.cverb", LAB.title[0]);
		var zh = langcode() === "zh";
		var groups = [];
		var cur = null;
		var names = { v: ["常见动词", "Common verbs"], sep: ["离合词", "Separable"], a: ["常见形容词", "Common adjectives"], n: ["常见名词", "Common nouns"] };
		var items = topick();
		var i, it, name;
		for (i = 0; i < items.length; i++) {
			it = items[i];
			name = zh ? names[it[0]][0] : names[it[0]][1];
			if (!cur || cur.g !== name) {
				cur = { g: name, items: [] };
				groups.push(cur);
			}
			cur.items.push({ v: it[1], t: zh ? it[1] : it[1] + "  " + it[2] });
		}
		SK.combofill("cv-in", groups);
		setopt("auto", "web.everb.auto", LAB.t_auto);
		setopt("v", "web.everb.verb", LAB.t_v);
		setopt("a", "web.everb.adj", LAB.t_a);
		setopt("n", "web.everb.noun", LAB.t_n);
		var out = F.id("cv-out");
		if (out && !F.attr(out, "data-bind")) {
			F.seta(out, "data-bind", "1");
			out.onclick = onout;
		}
		run();
	}

	function onout(ev){
		var btn = ev.target;
		var out = F.id("cv-out");
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
		var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=zh");
		clip = audio;
		audio.onerror = function(){
			if (clip === audio) SK.msg("box-msg", tx("web.cverb.nospeak", "无法发音"), true);
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (clip === audio) SK.msg("box-msg", tx("web.cverb.nospeak", "无法发音"), true);
		});
	}

	function run(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(go, 150);
	}

	function go(){
		timer = 0;
		var box = F.id("cv-in");
		var sel = F.id("cv-sel");
		var w = norm(box ? box.value : "");
		if (!w) {
			paint("", "", []);
			SK.msg("box-msg", "");
			return;
		}
		var built = build(w, langcode(), sel ? sel.value : "auto");
		if (!built.ok) {
			paint("", "", []);
			SK.msg("box-msg", built.err, true);
			return;
		}
		paint(built.head, built.note, built.rows);
	}

	function paint(head, note, rows){
		var out = F.id("cv-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; return; }
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(head) + "</b><span>" + esc(note) + "</span></div>"
			+ '<table class="jv-tab cv-tab">';
		var g = null;
		var i, j;
		for (i = 0; i < rows.length; i++) {
			if (rows[i].g !== g) {
				g = rows[i].g;
				html += '<tr><td class="kv-g" colspan="2">' + esc(g) + "</td></tr>";
			}
			html += '<tr><td class="jv-k">' + esc(rows[i].name) + '</td><td class="jv-v">';
			var lines = String(rows[i].text).split(/\s*(?:\n|·)\s*/);
			for (j = 0; j < lines.length; j++) {
				if (!lines[j]) continue;
				html += '<div class="cv-line"><span>' + esc(lines[j]) + "</span>"
					+ '<button type="button" class="jv-spk" data-v="' + esc(saytext(speech(lines[j]))) + '" title="' + tip + '">'
					+ '<i class="fa-solid fa-volume-high"></i></button></div>';
			}
			html += "</td></tr>";
		}
		html += "</table>";
		out.innerHTML = html;
	}
})();
