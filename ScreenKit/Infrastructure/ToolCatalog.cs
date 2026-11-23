using System.Text;

namespace ScreenKit;

/// <summary>工具出现在哪些桌面界面。位标记，可同时出现在多处。</summary>
[Flags]
enum ToolPlace {
	None = 0,
	/// <summary>主窗口「工具」菜单。</summary>
	Menu = 1,
	/// <summary>托盘菜单。</summary>
	Tray = 2,
	/// <summary>主窗口的「工具」页。</summary>
	Tab = 4,
}

/// <summary>桌面上怎么打开这个工具。</summary>
enum ToolKind {
	/// <summary>只在网页里有，桌面上不出现。</summary>
	None,
	/// <summary>调主窗口里的打开方法。</summary>
	Act,
	/// <summary>切到主窗口已有的页。</summary>
	Tab,
}

/// <summary>网页工具的打开方式。</summary>
enum WebKind {
	/// <summary>没有网页卡片。</summary>
	None,
	/// <summary>本页里的面板，地址是 /#id。</summary>
	Tool,
	/// <summary>独立页面。</summary>
	Page,
	/// <summary>独立窗口，只有标题栏。</summary>
	Popup,
	/// <summary>另开一个站内地址。</summary>
	Link,
}

/// <summary>
/// 一个工具。主窗口「工具」菜单、托盘菜单、「工具」页、网页卡片都从这里取，
/// 各自按 <see cref="Place"/>、<see cref="TopPlace"/>、<see cref="Web"/> 决定显不显示、怎么打开。
/// </summary>
sealed class ToolDef {
	/// <summary>唯一标识。菜单项后缀、网页卡片 id、收藏里存的就是它。</summary>
	public string Id;
	/// <summary>所属分类，见 <see cref="ToolCatalog.Cats"/>。空表示不归分类。</summary>
	public string Cat;
	/// <summary>出现在哪些桌面界面。</summary>
	public ToolPlace Place;
	/// <summary>这些界面里放到菜单顶部，不进分类子菜单。</summary>
	public ToolPlace TopPlace;
	public ToolKind Kind;
	/// <summary>网页卡片怎么打开，None 表示没有网页卡片。</summary>
	public WebKind Web;
	/// <summary>网页卡片的站内地址，空表示 /#id。</summary>
	public string Path;
	/// <summary>Act / Tab 的动作名，传给主窗口的打开方法。</summary>
	public string Open;
	public string NameKey;
	/// <summary>托盘文案键。菜单和托盘叫法不同时填，空表示用 <see cref="NameKey"/>。</summary>
	public string TrayKey;
	/// <summary>网页卡片文案键。菜单和卡片叫法不同时填，空表示用 <see cref="NameKey"/>。</summary>
	public string WebKey;
	/// <summary>网页卡片 id。和 <see cref="Id"/> 不同时填（文档阅读器的卡片叫 doc）。</summary>
	public string CardId;
	/// <summary>提示文案键。没有时用 <see cref="Title"/> 兜底。</summary>
	public string TipKey;
	/// <summary>Segoe MDL2 图标，用于菜单和「工具」页。</summary>
	public string Glyph;
	/// <summary>FontAwesome 图标名，用于网页卡片。</summary>
	public string Fa;
	/// <summary>网页卡片的提示原文。没有文案键的旧卡片靠它自动查键翻译。</summary>
	public string Title;
	/// <summary>网页卡片的搜索关键词。</summary>
	public string Search;
	/// <summary>要开的模块，空表示不受模块开关影响。取值 Ocr / Translate / Dict。</summary>
	public string Module;
	/// <summary>取这个工具的热键文案，空表示没有全局热键。</summary>
	public Func<OcrOptions, string> Hotkey;
	/// <summary>菜单项可勾选（如输入法开关）。</summary>
	public bool Check;
}

/// <summary>工具分类。顺序就是菜单、托盘和「工具」页里的顺序。</summary>
sealed class ToolCat {
	public string Id;
	public string NameKey;
	/// <summary>网页分类按钮的文案键。空表示用 <see cref="NameKey"/>。</summary>
	public string WebKey;
	/// <summary>分类项的提示文案键。空表示没有提示。</summary>
	public string TipKey;
	/// <summary>Segoe MDL2 图标，用于菜单和「工具」页。</summary>
	public string Glyph;
	/// <summary>FontAwesome 图标名，用于网页分类按钮。</summary>
	public string Fa;
}

/// <summary>
/// 全部工具的唯一定义。菜单、托盘、「工具」页、网页卡片都读这一份，加工具只改这里。
/// </summary>
static class ToolCatalog {
	/// <summary>「收藏」分类，不是真分类。</summary>
	public const string Fav = "fav";
	/// <summary>「全部」分类，不是真分类。</summary>
	public const string All = "all";

	public static readonly ToolCat[] Cats = {
		new() { Id = "text", NameKey = "web.cat.text", Glyph = "\uE8D2", Fa = "font" },
		new() { Id = "calc", NameKey = "web.cat.calc", Glyph = "\uE8EF", Fa = "calculator" },
		new() { Id = "speech", NameKey = "web.cat.speech", Glyph = "\uE767", Fa = "volume-high" },
		new() { Id = "time", NameKey = "web.cat.time", Glyph = "\uE787", Fa = "calendar-days" },
		new() { Id = "image", NameKey = "web.cat.image", Glyph = "\uEB9F", Fa = "image" },
		new() { Id = "info", NameKey = "web.cat.info", TipKey = "menu.info.tip", Glyph = "\uE946", Fa = "circle-info" },
		new() { Id = "talk", NameKey = "web.cat.talk", Glyph = "\uE8F2", Fa = "comments" },
		new() { Id = "net", NameKey = "tray.netgroup", TipKey = "menu.netgroup.tip", Glyph = "\uE839", Fa = "network-wired" },
		new() { Id = "files", NameKey = "menu.cat.file", WebKey = "web.cat.files", Glyph = "\uE8B7", Fa = "folder" },
		new() { Id = "sys", NameKey = "menu.cat.sys", Glyph = "\uE770", Fa = "microchip" },
	};

	public static readonly ToolDef[] Items = {
		new() { Id = "webhome", Place = ToolPlace.Menu | ToolPlace.Tray, TopPlace = ToolPlace.Menu | ToolPlace.Tray, Kind = ToolKind.Act, Open = "webhome", NameKey = "tray.webhome", TipKey = "menu.webhome.tip", Glyph = "\uE774", Hotkey = o => o.HotkeyWebHome },  // 顶部：web 工具主页
		new() { Id = "files", Cat = "files", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, TopPlace = ToolPlace.Menu | ToolPlace.Tray, Kind = ToolKind.Act, Web = WebKind.Link, Path = "/files", Open = "files", NameKey = "tray.webfiles", WebKey = "web.files", TipKey = "web.files", Glyph = "\uE8B7", Fa = "folder", Title = "手机与电脑互传文件，点击打开，Ctrl+点击新窗口", Search = "文件传输 手机 电脑 上传 下载 互传 传文件 transfer files" },  // 顶部 + 「文件」分类：手机电脑互传
		new() { Id = "dictweb", Place = ToolPlace.Menu | ToolPlace.Tray, TopPlace = ToolPlace.Menu | ToolPlace.Tray, Kind = ToolKind.Act, Open = "dictweb", NameKey = "web.dict", TipKey = "web.dict.tip", Glyph = "\uE82D" },  // 顶部：在浏览器里开网页词典
		new() { Id = "hangul", Place = ToolPlace.Menu | ToolPlace.Tray, TopPlace = ToolPlace.Menu | ToolPlace.Tray, Kind = ToolKind.Act, Open = "hangul", NameKey = "tray.hangul", TipKey = "menu.hangul.tip", Glyph = "\uE765", Hotkey = o => o.HotkeyImeCycle, Check = true },  // 顶部：五笔/日语/韩语输入，可勾选
		// ---------- text ----------
		new() { Id = "trpopup", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tab, Kind = ToolKind.Act, Open = "trpopup", NameKey = "menu.translate.popup", TipKey = "menu.translate.popup.tip", Glyph = "\uE8C1", Module = "Translate", Hotkey = o => o.HotkeyTranslate },  // 托盘顶部已有「翻译」，不重复
		new() { Id = "texttool", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "texttool", NameKey = "menu.texttool", TrayKey = "tray.texttool", TipKey = "menu.texttool.tip", Glyph = "\uE8C1" },
		new() { Id = "mdread", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Web = WebKind.Popup, Path = "/doc", Open = "mdread", NameKey = "menu.mdread", WebKey = "web.tool.mdread", CardId = "doc", TipKey = "menu.mdread.tip", Glyph = "\uE736", Fa = "book-open", Title = "弹一个只有标题栏的窗口，拖入 Markdown、Word 或 PDF，多文件用标签切换", Search = "md 阅读器 markdown 预览 拖入 docx word pdf 文档 标签 文档阅读器", Hotkey = o => o.HotkeyMdRead },  // 桌面文档阅读器，网页是弹窗页
		new() { Id = "doc2md", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "doc2md", NameKey = "menu.doc2md", TrayKey = "tray.doc2md", TipKey = "menu.doc2md.tip", Glyph = "\uE8A5" },
		new() { Id = "pwgen", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "pwgen", NameKey = "menu.pwgen", TrayKey = "tray.pwgen", TipKey = "menu.pwgen.tip", Glyph = "\uE8D7" },
		new() { Id = "note", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "note", NameKey = "menu.note", TrayKey = "tray.note", TipKey = "menu.note.tip", Glyph = "\uE70B" },
		new() { Id = "zhconv", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "zhconv", NameKey = "menu.zhconv", TrayKey = "tray.zhconv", TipKey = "menu.zhconv.tip", Glyph = "\uE8C1" },
		new() { Id = "pinyin", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "pinyin", NameKey = "menu.pinyin", TrayKey = "tray.pinyin", TipKey = "menu.pinyin.tip", Glyph = "\uE8C1" },
		new() { Id = "jpyomi", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "jpyomi", NameKey = "menu.jpyomi", TrayKey = "tray.jpyomi", TipKey = "menu.jpyomi.tip", Glyph = "\uE8C1" },
		new() { Id = "hash", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "hash", NameKey = "menu.hash", TrayKey = "tray.hash", TipKey = "menu.hash.tip", Glyph = "\uE73E" },
		new() { Id = "reader", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Page, Path = "/reader", NameKey = "web.tool.reader", Glyph = "\uE8D2", Fa = "book-open-reader", Title = "拖入 txt、md，存在这个浏览器里阅读，点击打开，Ctrl+点击新窗口", Search = "小说阅读器 阅读 小说 朗读 听书 txt md reader" },
		new() { Id = "text", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.text", Glyph = "\uE8D2", Fa = "file-lines", Title = "Base64、URL、十六进制、JSON、XML、SQL、哈希、HTML、CSS、JS、简繁、日文注音、变量名、Crontab、Token、User-Agent", Search = "文本转换 base64 url json xml sql 十六进制 md5 sha256 html css js 转义 简繁转换 繁体 简体 日文注音 读音 假名 变量名 camel snake crontab cron token user-agent ua 解析" },
		new() { Id = "u-json", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.json", Glyph = "\uE8D2", Fa = "file-code", Title = "把 JSON、XML 或 YAML 展开成可折叠的树，和浏览器控制台里看到的一样", Search = "json xml yaml 查看器 格式化 折叠 树形 结构 解析 预览 缩进" },
		new() { Id = "u-py", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.pinyin", Glyph = "\uE8D2", Fa = "language", Title = "把汉字转成拼音或拼音首字母", Search = "中文转拼音 拼音 首字母 npinyin" },
		new() { Id = "u-seg", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.seg", Glyph = "\uE8D2", Fa = "scissors", Title = "用浏览器自带的分词，按词、句或字素切开", Search = "中文分词 分词 断句 字素 segmenter" },
		new() { Id = "u-aim", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.aim", Glyph = "\uE8D2", Fa = "robot", Title = "用 Chrome 自带的本地小模型做检测、翻译、摘要、改写、校对或提问，也可以朗读和语音识别", Search = "浏览器小模型 本地 翻译 摘要 改写 校对 提问 朗读 语音识别 gemini" },
		new() { Id = "tr", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.tr", Glyph = "\uE8D2", Fa = "right-left", Title = "输入原文，译到所选语言。两边都可以发音，也可以交换", Search = "翻译 中英 交换 发音 朗读" },
		new() { Id = "u-pw", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.pw", Glyph = "\uE8D2", Fa = "key", Title = "本地生成，一次可出多条", Search = "随机密码 批量" },
		new() { Id = "u-re", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.re", Glyph = "\uE8D2", Fa = "asterisk", Title = "在文字里查找匹配", Search = "正则 表达式" },
		new() { Id = "u-numread", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.numread", Glyph = "\uE8D2", Fa = "font", Title = "输入数字，列出中文、韩文、日语、德法西葡俄读法、英语多边形名、人民币大写，以及小数中数大数和常用表记。能转成浮点数时给出 FP8/16/32/64 的十六进制和 a*2^x", Search = "数字表记 转换 中文读法 英文读法 韩语 韩文 固有 汉字 日语 德语 法语 西班牙语 葡萄牙语 俄语 小数 中数 大数 分厘毫 SI kilo mega 大写 罗马 英语多边形 pentagon chiliagon myriagon 千分位 科学计数 人民币 元 角 分 fp8 fp16 fp32 fp64 bf16 bfloat E4M3 E5M2 浮点 hex" },
		new() { Id = "u-fake", Cat = "text", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.fake", Glyph = "\uE8D2", Fa = "user-group", Title = "按类型生成，一行一条。已安装 Bogus 时先选语言，再选该语言有的分类", Search = "生成测试数据 测试数据 假数据 中文姓名 中文网名 英文网名 邮箱 手机 地址 公司 用户名 身份证 mac 地址 uuid bogus" },
		// ---------- calc ----------
		new() { Id = "scicalc", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "scicalc", NameKey = "menu.scicalc", TrayKey = "tray.scicalc", TipKey = "menu.scicalc.tip", Glyph = "\uE8EF" },
		new() { Id = "u-scicalc", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.scicalc", Glyph = "\uE8EF", Fa = "calculator", Title = "紧凑科学计算器：三角函数、对数、乘方、阶乘、内存", Search = "科学计算器 三角函数 对数" },
		new() { Id = "u-radix", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.radix", Glyph = "\uE8EF", Fa = "hashtag", Title = "转换到默认全部，一次列出 2、8、10、16 进制", Search = "进制 二进制 十六进制" },
		new() { Id = "u-bytes", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.bytes", Glyph = "\uE8EF", Fa = "microchip", Title = "把连续字节按各种整数、浮点和文本格式解读，大小端可选", Search = "字节 十六进制 hex 二进制 解读 int8 uint8 int16 uint16 int32 uint32 int64 uint64 浮点 fp8 fp16 fp32 fp64 bf16 e4m3 e5m2 float8 ascii unicode utf8 端序 小端 大端 大小端 内存" },
		new() { Id = "u-unit", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.unit", Glyph = "\uE8EF", Fa = "ruler", Title = "数值可直接写 1米；新单位默认「全部」，一次列出该类型的全部单位。外币查中国银行中间价，失败时用 2026-10-10 的牌价并缓存 10 分钟", Search = "单位换算 长度 重量 货币 温度换算 面积 体积 存储 时间 压力 功率 px rem 米 英尺 摄氏 全部" },
		new() { Id = "u-rand", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.rand", Glyph = "\uE8EF", Fa = "dice", Title = "指定范围的随机数", Search = "随机数" },
		new() { Id = "u-plot", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.plot", Glyph = "\uE8EF", Fa = "chart-line", Title = "输入公式或等式画出函数图像，可平移和缩放", Search = "函数图像 函数 曲线 曲面 方程 示例 y=f(x) z=f(x,y) x^2+y^2=1 plot" },
		new() { Id = "u-bmi", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.bmi", Glyph = "\uE8EF", Fa = "weight-scale", Title = "身高体重算 BMI", Search = "bmi 体质" },
		new() { Id = "u-loan", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.loan", Glyph = "\uE8EF", Fa = "house", Title = "等额本息或等额本金", Search = "房贷 等额" },
		new() { Id = "u-comp", Cat = "calc", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.comp", Glyph = "\uE8EF", Fa = "percent", Title = "按年利率复利", Search = "复利" },
		// ---------- speech ----------
		new() { Id = "voicerec", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "voicerec", NameKey = "menu.voicerec", TrayKey = "tray.voicerec", TipKey = "menu.voicerec.tip", Glyph = "\uE720" },
		new() { Id = "midi", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "midi", NameKey = "menu.midisynth", TrayKey = "tray.midisynth", TipKey = "menu.midisynth.tip", Glyph = "\uE189" },
		new() { Id = "player", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "player", NameKey = "menu.player", TipKey = "menu.player.tip", Glyph = "\uE768" },
		new() { Id = "vidconv", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "vidconv", NameKey = "menu.vidconv", TrayKey = "tray.vidconv", TipKey = "menu.vidconv.tip", Glyph = "\uE714" },
		new() { Id = "vidgif", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "vidgif", NameKey = "menu.vidgif", TrayKey = "tray.vidgif", TipKey = "menu.vidgif.tip", Glyph = "\uE714" },
		new() { Id = "cam", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "cam", NameKey = "menu.cam", TrayKey = "tray.cam", TipKey = "menu.cam.tip", Glyph = "\uE722" },
		new() { Id = "rtspsrv", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "rtspsrv", NameKey = "menu.rtspsrv", TrayKey = "tray.rtspsrv", TipKey = "menu.rtspsrv.tip", Glyph = "\uE7F4" },
		new() { Id = "cast", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Tab, Open = "cast", NameKey = "menu.cast", TrayKey = "tray.cast", TipKey = "menu.cast.tip", Glyph = "\uE7F4" },
		new() { Id = "tts", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.tts", Glyph = "\uE767", Fa = "volume-high", Title = "Windows、SAPI、Edge 或离线模型", Search = "语音合成 朗读" },
		new() { Id = "asr", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.asr", Glyph = "\uE767", Fa = "microphone", Title = "系统识别器或离线模型，可录音或选文件", Search = "语音识别 录音" },
		new() { Id = "u-bit", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.bit", Glyph = "\uE767", Fa = "music", Title = "方波等乐器、四通道编排，以及爆炸、警报等音效", Search = "8bit 合成器 方波 脉冲 三角波 锯齿 风琴 铃 贝斯 笛子 鼓 爆炸 警报 音效 乐器 编排 杂音" },
		new() { Id = "u-cam", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.cam", Glyph = "\uE767", Fa = "camera", Title = "用摄像头拍照或扫码", Search = "相机 拍照 扫码 摄像头" },
		new() { Id = "u-rtsp", Cat = "speech", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.rtsp", Glyph = "\uE767", Fa = "video", Title = "填写 RTSP 地址，在网页里播放 H.264", Search = "rtsp 视频 监控 直播 播放" },
		// ---------- time ----------
		new() { Id = "wincal", Cat = "time", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "wincal", NameKey = "menu.wincal", TrayKey = "tray.wincal", TipKey = "menu.wincal.tip", Glyph = "\uE787" },
		new() { Id = "cal", Cat = "time", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.cal", Glyph = "\uE787", Fa = "calendar-days", Title = "公历换农历、干支、日本、希伯来等", Search = "历法 农历 干支" },
		new() { Id = "u-ts", Cat = "time", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.ts", Glyph = "\uE787", Fa = "clock", Title = "秒或毫秒与本地时间互转", Search = "时间戳 unix" },
		new() { Id = "u-datecalc", Cat = "time", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.date", Glyph = "\uE787", Fa = "calendar-days", Title = "两个日期时间相差多少（单位可选，默认「全部」），或某个日期时间加减秒、分、小时、天、月、年（数量可为负，单位在数量后面）", Search = "日期计算 日期差 日期加减 月份加减 日期时间 天数 秒 分 小时 月 年" },
		new() { Id = "u-watch", Cat = "time", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.watch", Glyph = "\uE787", Fa = "stopwatch", Title = "秒表、按秒倒计时，或到某个时间", Search = "计时 秒表 倒计时 截止" },
		new() { Id = "u-wclock", Cat = "time", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.wclock", Glyph = "\uE787", Fa = "globe", Title = "常用地区的本地时间，也可再选一个地区", Search = "世界时钟 时区 北京 东京 伦敦 纽约 world clock timezone" },
		new() { Id = "u-work", Cat = "time", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.work", Glyph = "\uE787", Fa = "briefcase", Title = "跳过周六和周日", Search = "工作日" },
		// ---------- image ----------
		new() { Id = "pdf", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "pdf", NameKey = "menu.pdf", TrayKey = "tray.pdf", TipKey = "menu.pdf.tip", Glyph = "\uEA90", Module = "Ocr" },
		new() { Id = "imgconv", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "imgconv", NameKey = "menu.imgconv", TrayKey = "tray.imgconv", TipKey = "menu.imgconv.tip", Glyph = "\uEB9F" },
		new() { Id = "charart", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "charart", NameKey = "menu.charart", TrayKey = "tray.charart", TipKey = "menu.charart.tip", Glyph = "\uE8D2" },
		new() { Id = "pixel", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "pixel", NameKey = "menu.pixel", TrayKey = "tray.pixel", TipKey = "menu.pixel.tip", Glyph = "\uE91B" },
		new() { Id = "qrmake", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "qrmake", NameKey = "menu.qrmake", TrayKey = "tray.qrmake", TipKey = "menu.qrmake.tip", Glyph = "\uED14" },
		new() { Id = "glyphmod", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "glyphmod", NameKey = "menu.glyphmod", TrayKey = "tray.glyphmod", TipKey = "menu.glyphmod.tip", Glyph = "\uE8D2" },
		new() { Id = "ocr", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.ocr", Glyph = "\uEB9F", Fa = "eye", Title = "Windows 系统 OCR 或 ONNX 模型包", Search = "文字识别 ocr" },
		new() { Id = "qr", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.qr", Glyph = "\uEB9F", Fa = "qrcode", Title = "生成 PNG，图下显示原文", Search = "二维码 条码 生成" },
		new() { Id = "qrs", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.qrs", Glyph = "\uEB9F", Fa = "barcode", Title = "从图片读出内容，可粘贴", Search = "二维码 条码 识别" },
		new() { Id = "u-b64img", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.b64", Glyph = "\uEB9F", Fa = "file-image", Title = "图片转成 Data URL", Search = "图片 base64" },
		new() { Id = "u-ico", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.ico", Glyph = "\uEB9F", Fa = "icons", Title = "把图片做成 ICO，默认 32", Search = "图片 ico 图标" },
		new() { Id = "u-pick", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.pick", Glyph = "\uEB9F", Fa = "eye-dropper", Title = "选一个颜色，或从屏幕上吸一个像素，给出各种写法", Search = "取色 颜色选择 屏幕 吸管" },
		new() { Id = "u-grad", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.grad", Glyph = "\uEB9F", Fa = "fill", Title = "线性、径向、锥形。可加多种颜色，用浏览器选色器，改完即预览", Search = "css 渐变 线性 径向 锥形" },
		new() { Id = "u-board", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.board", Glyph = "\uEB9F", Fa = "pen-ruler", Title = "画形状、文字，圈选笔迹后可移动，粘贴图片后可改大小", Search = "画板 标注 矩形 椭圆 箭头 画笔 圈选 文字 粘贴 图片" },
		new() { Id = "u-html", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.htmlui", Glyph = "\uEB9F", Fa = "code", Title = "左边写 HTML，右边即时预览，可套用界面模板", Search = "html 界面设计 工具 界面 按钮 卡片 表格 表单 预览" },
		new() { Id = "u-ph", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.ph", Glyph = "\uEB9F", Fa = "image", Title = "改宽高、颜色或文字后马上预览，Ctrl+C 复制图片", Search = "占位图" },
		new() { Id = "u-rcolor", Cat = "image", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.rcolor", Glyph = "\uEB9F", Fa = "fill-drip", Title = "随机颜色列表，HEX、RGB、HSL、HSV、CMYK、YUV，点一个色值复制", Search = "随机颜色" },
		// ---------- info ----------
		new() { Id = "dict", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Tab, Web = WebKind.Page, Path = "/dict", Open = "dict", NameKey = "menu.dict", TrayKey = "tray.dict", WebKey = "web.dict", TipKey = "menu.dict.tip", Glyph = "\uE82D", Fa = "book", Title = "查词、看例句和发音，点击打开，Ctrl+点击新窗口", Search = "词典 查词 翻译 例句 发音 汉语 英语 日语 韩语 dictionary", Module = "Dict", Hotkey = o => o.HotkeyDict },  // 桌面切词典页，网页是独立页
		new() { Id = "u-jverb", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.jverb", Glyph = "\uE946", Fa = "language", Title = "列出动词、い形容词、な形容词、名词的变化，含补助动词、敬语和方言。输入框可下拉常见词和特殊变化，每项可以发音", Search = "日语词形变化 日语动词变形 活用 五段 一段 する くる いく い形容词 な形容词 名词 敬语 尊敬 谦让 方言 verb adjective noun" },
		new() { Id = "u-jname", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.s.98f196ba27", Glyph = "\uE946", Fa = "id-card", Title = "查日本姓氏和名字的读法。输入完整姓名会拆成一姓一名，也可以随机生成一个姓名，每条可以听", Search = "日本姓名读法 日本姓名查询 日本人名 姓名读音 姓氏 名字 苗字 読み方 随机 name surname reading" },
		new() { Id = "u-kverb", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.kverb", Glyph = "\uE946", Fa = "language", Title = "输入韩语词典形，列出动词或形容词的变化，每条可以听发音", Search = "韩语词形变化 韩语动词变化 韩语 韩文 动词 形容词 名词 变化 活用 하다 먹다 살다 发音 conjugation korean verb" },
		new() { Id = "u-rverb", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.rverb", Glyph = "\uE946", Fa = "language", Title = "输入俄语动词、形容词或名词，列出体、时态、命令式、假定式、语气、分词、副动词，或形容词、名词的变格和特殊形式", Search = "俄语 俄文 动词 形容词 名词 变化 变位 变格 活用 читать красивый книга 完成体 未完成体 分词 副动词 格 动物 方言 conjugation declension russian" },
		new() { Id = "u-everb", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.everb", Glyph = "\uE946", Fa = "clock", Title = "输入英语动词、形容词或名词，列出时态变化、比较级和最高级、复数和所有格，每项可以发音", Search = "英语 英文 动词 时态 一般现在时 一般过去时 一般将来时 现在进行时 现在完成时 过去完成时 将来完成时 过去将来 被动语态 否定 疑问 变形 conjugation tense verb 形容词 比较级 最高级 名词 复数 所有格 冠词 adjective noun plural" },
		new() { Id = "u-ctab", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.ctab", Glyph = "\uE946", Fa = "table-cells", Title = "树形列出 web 命名色与 C# (.NET) 颜色，点一下复制十六进制", Search = "颜色表 色表 web 颜色 C# 颜色 .NET 系统色 常用颜色 红 橙 黄 绿 青 蓝 紫" },
		new() { Id = "u-httptab", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.httptab", Glyph = "\uE946", Fa = "server", Title = "按 1xx–5xx 分组列出 HTTP 状态码，点一下复制，含非标准扩展码", Search = "http 状态码 状态代码 响应码 返回码 status code 200 301 302 400 404 500 502 503 504 重定向 网关 错误" },
		new() { Id = "u-mimetab", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.mimetab", Glyph = "\uE946", Fa = "file-code", Title = "按类别列出常见 MIME 类型和扩展名，点一下复制 Content-Type", Search = "mime 类型 媒体类型 content-type 扩展名 文件类型 后缀 jpg png json pdf zip mp4 mp3 woff2" },
		new() { Id = "u-ports", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.ports", Glyph = "\uE946", Fa = "network-wired", Title = "常见 TCP/UDP 端口、服务和用途，点一下复制端口号", Search = "常见端口 端口号 port 3306 80 443 22 3389 8080 数据库 代理 服务" },
		new() { Id = "u-magic", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.magic", Glyph = "\uE946", Fa = "fingerprint", Title = "按十六进制文件头识别真实格式，含 ASCII 和偏移", Search = "文件头 魔数 magic 文件签名 signature hex ascii jpg png pdf zip exe" },
		new() { Id = "u-tz", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.tz", Glyph = "\uE946", Fa = "earth-asia", Title = "常见 IANA 时区、缩写、标准/夏令时偏移和主要城市", Search = "时区 时间 zone timezone UTC GMT CST 北京 纽约 伦敦 夏令时" },
		new() { Id = "u-codes", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.codes", Glyph = "\uE946", Fa = "globe", Title = "查 ISO 国家/地区、语言和货币代码，点一下复制主代码", Search = "国家 地区 语言 货币 代码 ISO 3166 639 4217 中国 CNY USD 人民币" },
		new() { Id = "u-iverb", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.iverb", Glyph = "\uE946", Fa = "spell-check", Title = "查英文不规则动词的原形、过去式和过去分词，输入任一形式或中文释义都能找到", Search = "英文 英语 不规则动词 过去式 过去分词 动词表 irregular verb past participle 三态 变化" },
		new() { Id = "u-keycode", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.keycode", Glyph = "\uE946", Fa = "keyboard", Title = "按键对应的 event.code、event.key、keyCode 和 Windows VK", Search = "键盘 keycode 键码 event.code event.key VK 快捷键 虚拟键" },
		new() { Id = "u-linuxcmd", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.linuxcmd", Glyph = "\uE946", Fa = "terminal", Title = "Linux 常用命令、示例和分类，可选发行版，点一下复制命令", Search = "linux 命令 shell 终端 发行版 ubuntu debian rhel centos fedora openeuler arch opensuse alpine apt dnf pacman zypper apk systemctl 常用命令" },
		new() { Id = "u-wincmd", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.wincmd", Glyph = "\uE946", Fa = "window-maximize", Title = "Windows 常用命令、示例和分类，可选 Windows PowerShell、pwsh 或 CMD，点一下复制命令", Search = "windows 命令 语法 powershell pwsh cmd 终端 bat taskkill ipconfig netstat sc schtasks winget 注册表 防火墙 常用命令" },
		new() { Id = "u-sqlfunc", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.sqlfunc", Glyph = "\uE946", Fa = "database", Title = "SQL 函数查询，可选 MySQL、SQL Server、Oracle、SQLite、PostgreSQL，点一下复制", Search = "sql 数据库 函数 mysql sqlserver oracle sqlite postgresql 字符串 日期 聚合 窗口 json 查询" },
		new() { Id = "u-soft", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.soft", Glyph = "\uE946", Fa = "box-open", Title = "Linux 开源常用软件的包名和安装命令，点一下复制安装命令", Search = "linux 开源 常用软件 软件 安装 apt docker 播放器 数据库 编辑器" },
		new() { Id = "u-binfo", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.binfo", Glyph = "\uE946", Fa = "desktop", Title = "这个浏览器能读到的屏幕、内存、键盘和处理器压力", Search = "本机信息 浏览器 屏幕 内存 键盘 压力" },
		new() { Id = "u-usbid", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.usbid", Glyph = "\uE946", Fa = "plug", Title = "列出常见 USB 设备的厂家、产品和型号，点一行可再查接口", Search = "usb id 厂家 产品 型号 vid pid 罗技 苹果 微软" },
		new() { Id = "u-ascii", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.ascii", Glyph = "\uE946", Fa = "table", Title = "按分组查看字符", Search = "unicode 码表 符号 ascii" },
		new() { Id = "u-emoji", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.emoji", Glyph = "\uE946", Fa = "icons", Title = "按分类列出 emoji，支持中英文搜索，点一下复制", Search = "emoji 表情 表情符号 笑脸 符号 smile 笑 开心 emoji查询 码表" },
		new() { Id = "u-asc", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.asc", Glyph = "\uE946", Fa = "table-list", Title = "按类别查看 0x00-0x7F，点一下选中并复制", Search = "ascii 码表 字符 控制字符 转义 0x41" },
		new() { Id = "u-gbk", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.gbk", Glyph = "\uE946", Fa = "font", Title = "按首字节查看 GBK", Search = "gbk 码表 国标" },
		new() { Id = "u-morse", Cat = "info", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.morse", Glyph = "\uE946", Fa = "ellipsis", Title = "字母、数字、标点和信号的国际摩斯码，点一下复制试听，也可输入一段文字整段播放", Search = "摩斯 摩尔斯 码表 morse 点划 电报 试听 播放 声音 文本" },
		// ---------- talk ----------
		new() { Id = "chat", Cat = "talk", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.None, Web = WebKind.Tool, NameKey = "web.tool.chat", Glyph = "\uE8F2", Fa = "comments", Title = "进入前填写昵称和性别，可以发文字、图片和文件，Ctrl+V 可粘贴图片或文件", Search = "聊天室 昵称 性别 图片 文件 粘贴" },
		// ---------- net ----------
		new() { Id = "net", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "net", NameKey = "menu.net", TrayKey = "tray.net", TipKey = "menu.net.tip", Glyph = "\uE839" },
		new() { Id = "httpdbg", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "httpdbg", NameKey = "menu.httpdbg", TrayKey = "tray.httpdbg", TipKey = "menu.httpdbg.tip", Glyph = "\uE774" },
		new() { Id = "mqtt", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "mqtt", NameKey = "menu.mqtt", TrayKey = "tray.mqtt", TipKey = "menu.mqtt.tip", Glyph = "\uEC05" },
		new() { Id = "wsdbg", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "wsdbg", NameKey = "menu.wsdbg", TrayKey = "tray.wsdbg", TipKey = "menu.wsdbg.tip", Glyph = "\uE968" },
		new() { Id = "udpdbg", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "udpdbg", NameKey = "menu.udpdbg", TrayKey = "tray.udpdbg", TipKey = "menu.udpdbg.tip", Glyph = "\uE839" },
		new() { Id = "soapdbg", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "soapdbg", NameKey = "menu.soapdbg", TrayKey = "tray.soapdbg", TipKey = "menu.soapdbg.tip", Glyph = "\uE774" },
		new() { Id = "portuse", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "portuse", NameKey = "menu.portuse", TrayKey = "tray.portuse", TipKey = "menu.portuse.tip", Glyph = "\uE839" },
		new() { Id = "netmon", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "netmon", NameKey = "menu.netmon", TrayKey = "tray.netmon", TipKey = "menu.netmon.tip", Glyph = "\uE9D9" },
		new() { Id = "usbcap", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "usbcap", NameKey = "menu.usbcap", TrayKey = "tray.usbcap", TipKey = "menu.usbcap.tip", Glyph = "\uE88E" },
		new() { Id = "tls", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "tls", NameKey = "menu.tls", TrayKey = "tray.tls", TipKey = "menu.tls.tip", Glyph = "\uE72E" },
		new() { Id = "ftp", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "ftp", NameKey = "menu.ftp", TrayKey = "tray.ftp", TipKey = "menu.ftp.tip", Glyph = "\uE8B7" },
		new() { Id = "sql", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "sql", NameKey = "menu.sql", TrayKey = "tray.sql", TipKey = "menu.sql.tip", Glyph = "\uE8F1" },
		new() { Id = "sqlconv", Cat = "net", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "sqlconv", NameKey = "menu.sqlconv", TipKey = "menu.sqlconv.tip", Glyph = "\uE8AB" },
		// ---------- files ----------
		new() { Id = "rename", Cat = "files", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "rename", NameKey = "menu.rename", TrayKey = "tray.rename", TipKey = "menu.rename.tip", Glyph = "\uE8AC" },
		new() { Id = "fastcopy", Cat = "files", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "fastcopy", NameKey = "menu.fastcopy", TrayKey = "tray.fastcopy", TipKey = "menu.fastcopy.tip", Glyph = "\uE8C8" },
		new() { Id = "fileprobe", Cat = "files", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "fileprobe", NameKey = "menu.fileprobe", TipKey = "menu.fileprobe.tip", Glyph = "\uE7C3" },
		new() { Id = "codestat", Cat = "files", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "codestat", NameKey = "menu.codestat", TrayKey = "tray.codestat", TipKey = "menu.codestat.tip", Glyph = "\uE943" },
		new() { Id = "sendfile", Cat = "files", Place = ToolPlace.Tab, Kind = ToolKind.Tab, Open = "sendfile", NameKey = "tab.sendfile", TipKey = "tab.sendfile", Glyph = "\uE898" },
		// ---------- sys ----------
		new() { Id = "sys", Cat = "sys", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "sys", NameKey = "menu.sysinfo", TrayKey = "tray.sysinfo", TipKey = "menu.sysinfo.tip", Glyph = "\uE770" },
		new() { Id = "systool", Cat = "sys", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "systool", NameKey = "menu.systool", TipKey = "menu.systool.tip", Glyph = "\uE90F" },
		new() { Id = "perfmon", Cat = "sys", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "perfmon", NameKey = "menu.perfmon", TipKey = "menu.perfmon.tip", Glyph = "\uE9D9" },
		new() { Id = "wintop", Cat = "sys", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "wintop", NameKey = "menu.wintop", TrayKey = "tray.wintop", TipKey = "menu.wintop.tip", Glyph = "\uE737" },
		new() { Id = "autokey", Cat = "sys", Place = ToolPlace.Menu | ToolPlace.Tray | ToolPlace.Tab, Kind = ToolKind.Act, Open = "autokey", NameKey = "menu.autokey", TrayKey = "tray.autokey", TipKey = "menu.autokey.tip", Glyph = "\uE765" },
		new() { Id = "diag", Cat = "sys", Place = ToolPlace.Tray, Kind = ToolKind.Act, Open = "diag", NameKey = "menu.diag", TrayKey = "tray.diag", TipKey = "menu.diag.tip", Glyph = "\uE9D9" },  // 只在托盘「系统」里，菜单在「帮助」下
		new() { Id = "mem", Cat = "sys", Place = ToolPlace.Tray, Kind = ToolKind.Act, Open = "mem", NameKey = "menu.mem", TrayKey = "tray.mem", TipKey = "menu.mem.tip", Glyph = "\uE950" },  // 只在托盘「系统」里，菜单在「帮助」下
	};

	/// <summary>某个界面上按定义顺序列出工具，不含放到顶部的那些。</summary>
	public static List<ToolDef> In(ToolPlace place) {
		var list = new List<ToolDef>();
		foreach (var d in Items)
			if ((d.Place & place) != 0 && (d.TopPlace & place) == 0)
				list.Add(d);
		return list;
	}

	/// <summary>某个界面上放到菜单顶部的工具。</summary>
	public static List<ToolDef> Top(ToolPlace place) {
		var list = new List<ToolDef>();
		foreach (var d in Items)
			if ((d.Place & place) != 0 && (d.TopPlace & place) != 0)
				list.Add(d);
		return list;
	}

	/// <summary>某个界面在这个分类里的工具。</summary>
	public static List<ToolDef> InCat(ToolPlace place, string cat) {
		var list = new List<ToolDef>();
		foreach (var d in Items)
			if (d.Cat == cat && (d.Place & place) != 0 && (d.TopPlace & place) == 0)
				list.Add(d);
		return list;
	}

	/// <summary>有内容的分类，菜单和托盘只挂这些。</summary>
	public static List<ToolCat> CatsWith(ToolPlace place) {
		var list = new List<ToolCat>();
		foreach (var cat in Cats)
			if (InCat(place, cat.Id).Count > 0)
				list.Add(cat);
		return list;
	}

	public static ToolDef Find(string id) {
		if (string.IsNullOrEmpty(id)) return null;
		foreach (var d in Items)
			if (d.Id == id) return d;
		return null;
	}

	public static ToolCat Cat(string id) {
		if (string.IsNullOrEmpty(id)) return null;
		foreach (var c in Cats)
			if (c.Id == id) return c;
		return null;
	}

	public static string CatName(string id) {
		var cat = Cat(id);
		return cat == null ? (id ?? "") : Plain(Loc.T(cat.NameKey));
	}

	/// <summary>网页分类按钮上的叫法。</summary>
	public static string CatWebName(ToolCat cat) {
		if (cat == null) return "";
		var key = string.IsNullOrEmpty(cat.WebKey) ? cat.NameKey : cat.WebKey;
		var name = Loc.T(key);
		if (string.IsNullOrEmpty(name) || name == key) name = Loc.T(cat.NameKey);
		return Plain(name);
	}

	public static string CatGlyph(string id) {
		var cat = Cat(id);
		return cat == null ? "\uE774" : cat.Glyph;
	}

	/// <summary>主窗菜单里的分类标题。</summary>
	public static string CatMenuName(ToolCat cat) {
		if (cat == null) return "";
		return Plain(Loc.T(cat.NameKey));
	}

	/// <summary>分类项的提示，没有就返回空串。</summary>
	public static string CatTip(ToolCat cat) {
		if (cat == null || string.IsNullOrEmpty(cat.TipKey)) return "";
		var tip = Loc.T(cat.TipKey);
		return string.IsNullOrEmpty(tip) || tip == cat.TipKey ? "" : Plain(tip);
	}

	/// <summary>工具名。文案键没配就退回网页卡片上的原文。</summary>
	public static string NameOf(ToolDef d) => NameBy(d, d == null ? null : d.NameKey);

	/// <summary>托盘里的叫法。</summary>
	public static string TrayName(ToolDef d) =>
		NameBy(d, d != null && !string.IsNullOrEmpty(d.TrayKey) ? d.TrayKey : d == null ? null : d.NameKey);

	/// <summary>网页卡片上的叫法。</summary>
	public static string WebName(ToolDef d) =>
		NameBy(d, d != null && !string.IsNullOrEmpty(d.WebKey) ? d.WebKey : d == null ? null : d.NameKey);

	/// <summary>网页卡片 id，和收藏里存的一致。</summary>
	public static string WebId(ToolDef d) => d == null ? "" : (string.IsNullOrEmpty(d.CardId) ? d.Id : d.CardId);

	static string NameBy(ToolDef d, string key) {
		if (d == null) return "";
		var name = string.IsNullOrEmpty(key) ? "" : Loc.T(key);
		if (string.IsNullOrEmpty(name) || name == key) name = d.Title;
		if (string.IsNullOrEmpty(name)) name = d.Id;
		return Plain(name);
	}

	/// <summary>菜单标题，网页工具再标 (web)。</summary>
	public static string MenuName(ToolDef d) {
		if (d == null) return "";
		var name = NameOf(d);
		if (d.Kind == ToolKind.None) name += " (web)";
		return name;
	}

	/// <summary>托盘标题，网页工具再标 (web)。</summary>
	public static string TrayLabel(ToolDef d) {
		if (d == null) return "";
		var name = TrayName(d);
		if (d.Kind == ToolKind.None) name += " (web)";
		return name;
	}

	/// <summary>这个工具要的模块开着没有。没有 <see cref="ToolDef.Module"/> 的一律算开。</summary>
	public static bool ModuleOn(ToolDef d, bool ocr, bool translate, bool dict) {
		if (d == null || string.IsNullOrEmpty(d.Module)) return true;
		if (d.Module == "Ocr") return ocr;
		if (d.Module == "Translate") return translate;
		if (d.Module == "Dict") return dict;
		return true;
	}

	public static string TipOf(ToolDef d) {
		if (d == null) return "";
		if (!string.IsNullOrEmpty(d.TipKey)) {
			var tip = Loc.T(d.TipKey);
			if (!string.IsNullOrEmpty(tip) && tip != d.TipKey) return Plain(tip);
		}
		return Plain(d.Title);
	}

	/// <summary>收藏只留当前还在的工具，顺序不变。没有的 id 直接丢掉。</summary>
	public static List<string> KeepFav(IEnumerable<string> ids) {
		var keep = new List<string>();
		if (ids == null) return keep;
		var seen = new HashSet<string>();
		foreach (var raw in ids) {
			var id = (raw ?? "").Trim();
			if (id.Length == 0 || !seen.Add(id)) continue;
			if (Find(id) == null) continue;
			keep.Add(id);
		}
		return keep;
	}

	/// <summary>去掉名称末尾的菜单快捷键提示，如「简繁转换(_S)…」。</summary>
	public static string Plain(string text) {
		var s = text ?? "";
		var i = s.IndexOf("(_", StringComparison.Ordinal);
		if (i >= 0 && i + 3 < s.Length && s[i + 3] == ')')
			s = s.Substring(0, i) + s.Substring(i + 4);
		if (s.EndsWith("…")) s = s.Substring(0, s.Length - 1);
		return s.Trim();
	}

	/// <summary>网页要的分类和卡片，页面拿它建 DOM。界面语言变了要重新取。</summary>
	public static string WebJs() {
		var sb = new StringBuilder();
		sb.Append("window.SKCAT={");
		sb.Append("\"cats\":[");
		var first = true;
		foreach (var cat in Cats) {
			var has = false;
			foreach (var d in Items)
				if (d.Cat == cat.Id && d.Web != WebKind.None) { has = true; break; }
			if (!has) continue;
			if (!first) sb.Append(',');
			first = false;
			sb.Append("{\"id\":");
			jsstr(sb, cat.Id);
			sb.Append(",\"key\":");
			jsstr(sb, string.IsNullOrEmpty(cat.WebKey) ? cat.NameKey : cat.WebKey);
			sb.Append(",\"name\":");
			jsstr(sb, CatWebName(cat));
			sb.Append(",\"icon\":");
			jsstr(sb, cat.Fa ?? "");
			sb.Append('}');
		}
		sb.Append("],\"tools\":[");
		first = true;
		foreach (var d in Items) {
			if (d.Web == WebKind.None) continue;
			if (!first) sb.Append(',');
			first = false;
			sb.Append("{\"id\":");
			jsstr(sb, WebId(d));
			sb.Append(",\"cat\":");
			jsstr(sb, d.Cat);
			sb.Append(",\"kind\":");
			jsstr(sb, d.Web.ToString().ToLowerInvariant());
			sb.Append(",\"path\":");
			jsstr(sb, d.Path ?? "");
			sb.Append(",\"key\":");
			jsstr(sb, string.IsNullOrEmpty(d.WebKey) ? d.NameKey : d.WebKey);
			sb.Append(",\"name\":");
			jsstr(sb, WebName(d));
			sb.Append(",\"title\":");
			jsstr(sb, d.Title ?? "");
			sb.Append(",\"search\":");
			jsstr(sb, d.Search ?? "");
			sb.Append(",\"icon\":");
			jsstr(sb, d.Fa ?? "");
			sb.Append('}');
		}
		sb.Append("]};");
		return sb.ToString();
	}

	static void jsstr(StringBuilder sb, string s) {
		sb.Append('"');
		foreach (var c in s ?? "") {
			if (c == '"' || c == '\\') sb.Append('\\').Append(c);
			else if (c == '\n') sb.Append("\\n");
			else if (c == '\r') sb.Append("\\r");
			else if (c == '\t') sb.Append("\\t");
			else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
			else sb.Append(c);
		}
		sb.Append('"');
	}
}
