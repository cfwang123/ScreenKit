using System.Reflection;
using System.Text;

namespace ScreenKit;

/// <summary>用程序旁的 NPinyin.dll 把汉字转成拼音或首字母。程序集从字节加载，卸掉文件时不锁住。常见多音词按词替换。</summary>
static class PinyinConvert {
	public const int MAXLEN = 20000;

	static readonly object gate = new();
	static Assembly asm;
	static MethodInfo py;
	static MethodInfo ini;
	static readonly Dictionary<char, Hit[]> hits = buildhits();

	public static bool Installed => NativeRuntime.HasNPinyin();

	/// <summary>initials 为真时每个字一个大写首字母，否则是空格分开、不带声调的拼音。</summary>
	public static string Convert(string text, bool initials) {
		if (!Installed) throw new InvalidOperationException("NPinyin 未安装");
		text ??= "";
		if (text.Length > MAXLEN) throw new ArgumentException("文字太长");
		lock (gate) {
			ensure();
			var syl = new string[text.Length];
			for (var i = 0; i < text.Length;) {
				var n = apply(text, i, syl);
				if (n > 0) {
					i += n;
					continue;
				}
				syl[i] = onesyl(text[i]);
				i++;
			}
			return initials ? joinini(text, syl) : joinpy(text, syl);
		}
	}

	static int apply(string text, int i, string[] syl) {
		if (!hits.TryGetValue(text[i], out var list)) return 0;
		foreach (var hit in list) {
			if (i + hit.Word.Length > text.Length) continue;
			if (!same(text, i, hit.Word)) continue;
			for (var k = 0; k < hit.Word.Length; k++) syl[i + k] = hit.Syl[k];
			return hit.Word.Length;
		}
		return 0;
	}

	static bool same(string text, int i, string word) {
		for (var k = 0; k < word.Length; k++)
			if (text[i + k] != word[k]) return false;
		return true;
	}

	static string onesyl(char ch) {
		if (char.IsWhiteSpace(ch)) return "";
		return py.Invoke(null, new object[] { ch.ToString() }) as string ?? "";
	}

	/// <summary>和 NPinyin 整段结果一样：每个字后面跟一个空格，空白字再多一个空格，最后去掉两端空白。</summary>
	static string joinpy(string text, string[] syl) {
		var sb = new StringBuilder();
		for (var i = 0; i < text.Length; i++) {
			if (char.IsWhiteSpace(text[i])) sb.Append(' ');
			else sb.Append(syl[i] ?? "");
			sb.Append(' ');
		}
		return sb.ToString().Trim();
	}

	/// <summary>连续非空白算一组。组内取每个音节的首字母，组与组之间一个空格。</summary>
	static string joinini(string text, string[] syl) {
		var sb = new StringBuilder();
		var gap = false;
		var any = false;
		for (var i = 0; i < text.Length; i++) {
			if (char.IsWhiteSpace(text[i])) {
				if (any) gap = true;
				continue;
			}
			var s = syl[i];
			if (string.IsNullOrEmpty(s)) continue;
			if (gap) {
				sb.Append(' ');
				gap = false;
			}
			sb.Append(char.ToUpperInvariant(s[0]));
			any = true;
		}
		return sb.ToString();
	}

	static void ensure() {
		if (asm != null) return;
		var path = Path.Combine(NativeRuntime.BaseDir, "NPinyin.dll");
		asm = Assembly.Load(File.ReadAllBytes(path));
		var t = asm.GetType("NPinyin.Pinyin");
		if (t == null) throw new InvalidOperationException("NPinyin.dll 里没有 NPinyin.Pinyin");
		py = t.GetMethod("GetPinyin", new[] { typeof(string) });
		ini = t.GetMethod("GetInitials", new[] { typeof(string) });
		if (py == null || ini == null) throw new InvalidOperationException("NPinyin.dll 缺少 GetPinyin 或 GetInitials");
	}

	static Dictionary<char, Hit[]> buildhits() {
		var map = new Dictionary<char, List<Hit>>();
		foreach (var line in TABLE) {
			var sp = line.IndexOf(' ');
			var word = line.Substring(0, sp);
			var parts = line.Substring(sp + 1).Split(' ');
			if (parts.Length != word.Length)
				throw new InvalidOperationException("拼音条数不对 " + word);
			if (!map.TryGetValue(word[0], out var list)) {
				list = new List<Hit>();
				map[word[0]] = list;
			}
			list.Add(new Hit { Word = word, Syl = parts });
		}
		var built = new Dictionary<char, Hit[]>();
		foreach (var pair in map) {
			pair.Value.Sort((a, b) => b.Word.Length.CompareTo(a.Word.Length));
			built[pair.Key] = pair.Value.ToArray();
		}
		return built;
	}

	struct Hit {
		public string Word;
		public string[] Syl;
	}

	/// <summary>一词一条。只收 NPinyin 读错、日常又常见的词。音节数必须和字数一样。ü 仍写成 u，与库一致。</summary>
	static readonly string[] TABLE = {
		"重庆 chong qing",
		"重复 chong fu",
		"重新 chong xin",
		"重阳 chong yang",
		"重申 chong shen",
		"重逢 chong feng",
		"重返 chong fan",
		"重来 chong lai",
		"重现 chong xian",
		"重演 chong yan",
		"重写 chong xie",
		"重做 chong zuo",
		"重试 chong shi",
		"重叠 chong die",
		"重合 chong he",
		"重生 chong sheng",
		"重播 chong bo",
		"重建 chong jian",
		"重修 chong xiu",
		"重温 chong wen",
		"重提 chong ti",
		"双重 shuang chong",
		"着重 zhuo zhong",
		"银行 yin hang",
		"行业 hang ye",
		"分行 fen hang",
		"央行 yang hang",
		"行长 hang zhang",
		"行列 hang lie",
		"行情 hang qing",
		"行家 hang jia",
		"洋行 yang hang",
		"内行 nei hang",
		"外行 wai hang",
		"商行 shang hang",
		"总行 zong hang",
		"支行 zhi hang",
		"本行 ben hang",
		"行当 hang dang",
		"行话 hang hua",
		"在行 zai hang",
		"懂行 dong hang",
		"改行 gai hang",
		"跨行 kua hang",
		"行规 hang gui",
		"行市 hang shi",
		"行距 hang ju",
		"排行 pai hang",
		"字行 zi hang",
		"两行 liang hang",
		"几行 ji hang",
		"一行字 yi hang zi",
		"一行白鹭 yi hang bai lu",
		"长大 zhang da",
		"校长 xiao zhang",
		"市长 shi zhang",
		"部长 bu zhang",
		"家长 jia zhang",
		"会长 hui zhang",
		"增长 zeng zhang",
		"长子 zhang zi",
		"长高 zhang gao",
		"成长 cheng zhang",
		"区长 qu zhang",
		"班长 ban zhang",
		"县长 xian zhang",
		"局长 ju zhang",
		"院长 yuan zhang",
		"船长 chuan zhang",
		"站长 zhan zhang",
		"厂长 chang zhang",
		"店长 dian zhang",
		"组长 zu zhang",
		"科长 ke zhang",
		"处长 chu zhang",
		"厅长 ting zhang",
		"省长 sheng zhang",
		"乡长 xiang zhang",
		"村长 cun zhang",
		"军长 jun zhang",
		"机长 ji zhang",
		"连长 lian zhang",
		"营长 ying zhang",
		"团长 tuan zhang",
		"排长 pai zhang",
		"师长 shi zhang",
		"首长 shou zhang",
		"学长 xue zhang",
		"董事长 dong shi zhang",
		"队长 dui zhang",
		"族长 zu zhang",
		"酋长 qiu zhang",
		"长官 zhang guan",
		"长相 zhang xiang",
		"长女 zhang nu",
		"长孙 zhang sun",
		"长辈 zhang bei",
		"长老 zhang lao",
		"长进 zhang jin",
		"音乐 yin yue",
		"乐器 yue qi",
		"乐队 yue dui",
		"乐曲 yue qu",
		"声乐 sheng yue",
		"民乐 min yue",
		"配乐 pei yue",
		"乐谱 yue pu",
		"乐清 yue qing",
		"乐团 yue tuan",
		"乐章 yue zhang",
		"乐理 yue li",
		"乐手 yue shou",
		"乐师 yue shi",
		"哀乐 ai yue",
		"军乐 jun yue",
		"器乐 qi yue",
		"乐府 yue fu",
		"乐坛 yue tan",
		"奏乐 zou yue",
		"归还 gui huan",
		"还钱 huan qian",
		"还给 huan gei",
		"还债 huan zhai",
		"还书 huan shu",
		"还手 huan shou",
		"还价 huan jia",
		"还击 huan ji",
		"还礼 huan li",
		"还愿 huan yuan",
		"偿还 chang huan",
		"退还 tui huan",
		"交还 jiao huan",
		"返还 fan huan",
		"送还 song huan",
		"送给 song gei",
		"交给 jiao gei",
		"给你 gei ni",
		"给他 gei ta",
		"给我 gei wo",
		"给她 gei ta",
		"给人 gei ren",
		"便宜 pian yi",
		"投降 tou xiang",
		"调整 tiao zheng",
		"空调 kong tiao",
		"协调 xie tiao",
		"调皮 tiao pi",
		"调解 tiao jie",
		"调节 tiao jie",
		"调味 tiao wei",
		"调料 tiao liao",
		"调色 tiao se",
		"调音 tiao yin",
		"了解 liao jie",
		"角色 jue se",
		"主角 zhu jue",
		"配角 pei jue",
		"口角 kou jue",
		"睡觉 shui jiao",
		"目的 mu di",
		"似的 shi de",
		"什么 shen me",
		"曾经 ceng jing",
		"不曾 bu ceng",
		"未曾 wei ceng",
		"会计 kuai ji",
		"会稽 kuai ji",
		"阿胶 e jiao",
		"东阿 dong e",
		"阿弥陀佛 e mi tuo fo",
		"星宿 xing xiu",
		"二十八宿 er shi ba xiu",
		"呕吐 ou tu",
		"吐蕃 tu bo",
		"都是 dou shi",
		"全都 quan dou",
		"朝气 zhao qi",
		"今朝 jin zhao",
		"西藏 xi zang",
		"藏族 zang zu",
		"宝藏 bao zang",
		"大厦 da sha",
		"六安 lu an",
		"六合 lu he",
		"黄陂 huang pi",
		"番禺 pan yu",
		"单县 shan xian",
		"铅山 yan shan",
		"洪洞 hong tong",
		"乐亭 lao ting",
		"大埔 da bu",
		"十里堡 shi li pu",
		"吴堡 wu bu",
		"蔚县 yu xian",
		"尉犁 yu li",
		"牟平 mu ping",
		"秘鲁 bi lu",
		"单于 chan yu",
		"龟兹 qiu ci",
		"万俟 mo qi",
		"犍为 qian wei",
		"南 nan",
		"二 er",
		"儿 er",
		"顿 dun",
		"呕 ou",
	};
}
