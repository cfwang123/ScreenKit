(function(){
	// 俄语动词变形：输入不定式（可带 -ся），列出体、变位、现在/将来时、过去时、命令式、
	// 假定式、分词和副动词。规则按词尾推导，常见不规则动词和重音引起的特殊形单独列表。
	var CONF = {
		"быть": { a: "impf", tr: 0, pres: ["—", "—", "есть", "—", "—", "—"], fut: ["буду", "будешь", "будет", "будем", "будете", "будут"], past: ["был", "была", "было", "были"], imp: ["будь", "будьте"], ppa: "будущий", pap: "бывший", gr: "будучи", pg: "быв" },
		"дать": { a: "pf", tr: 1, pres: ["дам", "дашь", "даст", "дадим", "дадите", "дадут"], past: ["дал", "дала", "дало", "дали"], imp: ["дай", "дайте"], ppa: "дающий", pap: "давший", ppn: "даваемый", ppp: "данный", gr: "давая", pg: "дав" },
		"есть": { a: "impf", tr: 1, pres: ["ем", "ешь", "ест", "едим", "едите", "едят"], past: ["ел", "ела", "ело", "ели"], imp: ["ешь", "ешьте"], ppa: "едящий", pap: "евший", ppn: "едомый", ppp: "еденный", gr: "едя", pg: "ев" },
		"хотеть": { a: "impf", tr: 0, pres: ["хочу", "хочешь", "хочет", "хотим", "хотите", "хотят"], past: ["хотел", "хотела", "хотело", "хотели"], imp: null, ppa: "хотящий", pap: "хотевший", gr: "хотя", pg: "хотев" },
		"бежать": { a: "impf", tr: 0, pres: ["бегу", "бежишь", "бежит", "бежим", "бежите", "бегут"], past: ["бежал", "бежала", "бежало", "бежали"], imp: ["беги", "бегите"], ppa: "бегущий", pap: "бежавший", gr: "бежа", pg: "бежав" },
		"ехать": { a: "impf", tr: 0, pres: ["еду", "едешь", "едет", "едем", "едете", "едут"], past: ["ехал", "ехала", "ехало", "ехали"], imp: ["поезжай", "поезжайте"], imp0: ["езжай", "езжайте"], ppa: "едущий", pap: "ехавший", gr: "", pg: "ехав" },
		"идти": { a: "impf", tr: 0, pres: ["иду", "идёшь", "идёт", "идём", "идёте", "идут"], past: ["шёл", "шла", "шло", "шли"], imp: ["иди", "идите"], ppa: "идущий", pap: "шедший", gr: "идя", pg: "шедши" },
		"писать": { a: "impf", tr: 1, ps: "пиш", t: 1, ppn: "писаемый", ppp: "писанный", imp: ["пиши", "пишите"] },
		"читать": { a: "impf", tr: 1 },
		"делать": { a: "impf", tr: 1 },
		"работать": { a: "impf", tr: 0 },
		"играть": { a: "impf", tr: 0 },
		"думать": { a: "impf", tr: 0 },
		"знать": { a: "impf", tr: 1 },
		"слушать": { a: "impf", tr: 1 },
		"гулять": { a: "impf", tr: 0 },
		"говорить": { a: "impf", tr: 1, ps: "говор", t: 2, ppn: "говоримый", ppp: "говорённый" },
		"смотреть": { a: "impf", tr: 1, ps: "смотр", t: 2, ppn: "смотримый", ppp: "смотренный" },
		"видеть": { a: "impf", tr: 1, ps: "вид", t: 2, imp: null, ppp: "виденный" },
		"слышать": { a: "impf", tr: 1, ps: "слыш", t: 2, imp: null, ppn: "слышимый", ppp: "слышанный" },
		"держать": { a: "impf", tr: 1, ps: "держ", t: 2, ppn: "держимый", ppp: "держанный" },
		"дышать": { a: "impf", tr: 0, ps: "дыш", t: 2, ppp: "" },
		"гнать": { a: "impf", tr: 1, ps: "гон", t: 2, ppn: "гонимый", ppp: "гнанный" },
		"любить": { a: "impf", tr: 1, ps: "люб", p1: "любл", t: 2, ppn: "любимый", ppp: "любленный" },
		"учить": { a: "impf", tr: 1, ps: "уч", t: 2, ppp: "ученный" },
		"звонить": { a: "impf", tr: 0, ps: "звон", t: 2 },
		"просить": { a: "impf", tr: 1, ps: "прос", t: 2, ppp: "прошенный" },
		"дарить": { a: "impf", tr: 1, ps: "дар", t: 2 },
		"помнить": { a: "impf", tr: 1, ps: "помн", t: 2 },
		"готовить": { a: "impf", tr: 1, ps: "готов", p1: "готовл", t: 2, imp: ["готовь", "готовьте"], ppp: "готовленный" },
		"ставить": { a: "impf", tr: 1, ps: "став", p1: "ставл", t: 2, imp: ["ставь", "ставьте"], ppp: "ставленный" },
		"кормить": { a: "impf", tr: 1, ps: "корм", p1: "кормл", t: 2, ppp: "кормленный" },
		"боять": { a: "impf", tr: 0, ps: "бо", t: 2 },
		"смеять": { a: "impf", tr: 0, ps: "сме", t: 1, yo: 1 },
		"надеять": { a: "impf", tr: 0, ps: "наде", t: 1 },
		"нравить": { a: "impf", tr: 0, ps: "нрав", t: 2 },
		"спать": { a: "impf", tr: 0, ps: "сп", p1: "спл", t: 2, imp: ["спи", "спите"] },
		"стоять": { a: "impf", tr: 0, ps: "сто", t: 2, imp: ["стой", "стойте"] },
		"лежать": { a: "impf", tr: 0, ps: "леж", t: 2 },
		"сидеть": { a: "impf", tr: 0, ps: "сид", t: 2 },
		"лететь": { a: "impf", tr: 0, ps: "лет", t: 2, imp: ["лети", "летите"] },
		"гореть": { a: "impf", tr: 0, ps: "гор", t: 2, imp: null },
		"блестеть": { a: "impf", tr: 0, ps: "блест", t: 2, imp: null },
		"свистеть": { a: "impf", tr: 0, ps: "свист", t: 2, imp: ["свисти", "свистите"] },
		"звенеть": { a: "impf", tr: 0, ps: "звен", t: 2, imp: null },
		"кипеть": { a: "impf", tr: 0, ps: "кип", t: 2, imp: ["кипи", "кипите"] },
		"шуметь": { a: "impf", tr: 0, ps: "шум", t: 2, imp: ["шуми", "шумите"] },
		"храпеть": { a: "impf", tr: 0, ps: "храп", t: 2, imp: ["храпи", "храпите"] },
		"висеть": { a: "impf", tr: 0, ps: "вис", t: 2, imp: null },
		"глядеть": { a: "impf", tr: 0, ps: "гляд", t: 2, imp: ["гляди", "глядите"] },
		"шипеть": { a: "impf", tr: 0, ps: "шип", t: 2, imp: ["шипи", "шипите"] },
		"скрипеть": { a: "impf", tr: 0, ps: "скрип", t: 2, imp: ["скрипи", "скрипите"] },
		"греметь": { a: "impf", tr: 0, ps: "грем", t: 2, imp: ["греми", "гремите"] },
		"сопеть": { a: "impf", tr: 0, ps: "соп", t: 2, imp: ["сопи", "сопите"] },
		"пыхтеть": { a: "impf", tr: 0, ps: "пыхт", t: 2, imp: ["пыхти", "пыхтите"] },
		"терпеть": { a: "impf", tr: 1, ps: "терп", t: 2, imp: ["терпи", "терпите"] },
		"вертеть": { a: "impf", tr: 1, ps: "верт", t: 2, imp: ["верти", "вертите"] },
		"зависеть": { a: "impf", tr: 0, ps: "завис", t: 2, imp: null },
		"ненавидеть": { a: "impf", tr: 1, ps: "ненавид", t: 2, imp: null },
		"обидеть": { a: "pf", tr: 1, ps: "обид", t: 2, imp: null, ppp: "обиженный" },
		"кричать": { a: "impf", tr: 0, ps: "крич", t: 2 },
		"молчать": { a: "impf", tr: 0, ps: "молч", t: 2 },
		"стучать": { a: "impf", tr: 0, ps: "стуч", t: 2 },
		"звучать": { a: "impf", tr: 0, ps: "звуч", t: 2 },
		"ворчать": { a: "impf", tr: 0, ps: "ворч", t: 2 },
		"мычать": { a: "impf", tr: 0, ps: "мыч", t: 2 },
		"рычать": { a: "impf", tr: 0, ps: "рыч", t: 2 },
		"пищать": { a: "impf", tr: 0, ps: "пищ", t: 2 },
		"трещать": { a: "impf", tr: 0, ps: "трещ", t: 2 },
		"сосать": { a: "impf", tr: 1, ps: "сос", t: 1, yo: 1 },
		"топтать": { a: "impf", tr: 1, ps: "топч", t: 1 },
		"клеветать": { a: "impf", tr: 1, ps: "клевещ", t: 1 },
		"роптать": { a: "impf", tr: 0, ps: "ропщ", t: 1 },
		"трепетать": { a: "impf", tr: 0, ps: "трепещ", t: 1 },
		"щебетать": { a: "impf", tr: 0, ps: "щебеч", t: 1 },
		"прятать": { a: "impf", tr: 1, ps: "пряч", t: 1 },
		"хлестать": { a: "impf", tr: 1, ps: "хлещ", t: 1 },
		"полоскать": { a: "impf", tr: 1, ps: "полощ", t: 1 },
		"искать": { a: "impf", tr: 1, ps: "ищ", t: 1, ppp: "исканный" },
		"плакать": { a: "impf", tr: 0, ps: "плач", t: 1, imp: ["плачь", "плачьте"] },
		"махать": { a: "impf", tr: 0, ps: "маш", t: 1, imp: ["маши", "машите"], gr: "махая" },
		"скакать": { a: "impf", tr: 0, ps: "скач", t: 1, gr: "скача" },
		"лизать": { a: "impf", tr: 1, ps: "лиж", t: 1 },
		"вязать": { a: "impf", tr: 1, ps: "вяж", t: 1 },
		"резать": { a: "impf", tr: 1, ps: "реж", t: 1, imp: ["режь", "режьте"] },
		"сыпать": { a: "impf", tr: 1, ps: "сыпл", t: 1, imp: ["сыпь", "сыпьте"] },
		"трепать": { a: "impf", tr: 1, ps: "трепл", t: 1, imp: ["трепи", "трепите"] },
		"колебать": { a: "impf", tr: 1, ps: "колебл", t: 1 },
		"дремать": { a: "impf", tr: 0, ps: "дремл", t: 1 },
		"щипать": { a: "impf", tr: 1, ps: "щипл", t: 1 },
		"казать": { a: "impf", tr: 1, ps: "каж", t: 1, ppp: "казанный" },
		"крыть": { a: "impf", tr: 1, ps: "кро", t: 1, ppp: "крытый" },
		"мыть": { a: "impf", tr: 1, ps: "мо", t: 1, ppp: "мытый" },
		"рыть": { a: "impf", tr: 1, ps: "ро", t: 1, ppp: "рытый" },
		"петь": { a: "impf", tr: 1, ps: "по", t: 1, yo: 1, ppp: "петый" },
		"пить": { a: "impf", tr: 1, ps: "пь", t: 1, ju: 1, imp: ["пей", "пейте"], ppp: "питый", gr: "" },
		"бить": { a: "impf", tr: 1, ps: "бь", t: 1, ju: 1, imp: ["бей", "бейте"], ppp: "битый", gr: "" },
		"лить": { a: "impf", tr: 1, ps: "ль", t: 1, ju: 1, imp: ["лей", "лейте"], ppp: "литый", gr: "" },
		"вить": { a: "impf", tr: 1, ps: "вь", t: 1, ju: 1, imp: ["вей", "вейте"], ppp: "витый", gr: "" },
		"шить": { a: "impf", tr: 1, ps: "шь", t: 1, ju: 1, imp: ["шей", "шейте"], ppp: "шитый", gr: "" },
		"жить": { a: "impf", tr: 0, ps: "жив", t: 1, yo: 1 },
		"плыть": { a: "impf", tr: 0, ps: "плыв", t: 1, yo: 1 },
		"слыть": { a: "impf", tr: 0, ps: "слыв", t: 1, yo: 1 },
		"гнить": { a: "impf", tr: 0, ps: "гни", t: 1, yo: 1 },
		"брить": { a: "impf", tr: 1, ps: "бре", t: 1, ppp: "бритый" },
		"стелить": { a: "impf", tr: 1, ps: "стел", t: 1 },
		"молоть": { a: "impf", tr: 1, ps: "мел", t: 1, ppp: "молотый" },
		"колоть": { a: "impf", tr: 1, ppp: "колотый" },
		"бороть": { a: "impf", tr: 1, ppp: "боротый" },
		"полоть": { a: "impf", tr: 1, ppp: "полотый" },
		"пороть": { a: "impf", tr: 1, ppp: "поротый" },
		"класть": { a: "impf", tr: 1, ps: "клад", t: 1, yo: 1, pst: "кла", ppn: "", ppp: "" },
		"красть": { a: "impf", tr: 1, ps: "крад", t: 1, yo: 1, pst: "кра", ppn: "", ppp: "краденный" },
		"пасть": { a: "pf", tr: 0, ps: "пад", t: 1, yo: 1, pst: "па" },
		"сесть": { a: "pf", tr: 0, ps: "сяд", t: 1, past: ["сел", "села", "село", "сели"], imp: ["сядь", "сядьте"] },
		"лечь": { a: "pf", tr: 0, ps: "ляг", p2: "ляж", t: 1, past: ["лёг", "легла", "легло", "легли"], imp: ["ляг", "лягте"], pap: "лёгший" },
		"стать": { a: "pf", tr: 0, ps: "стан", t: 1, imp: ["стань", "станьте"] },
		"брать": { a: "impf", tr: 1, ps: "бер", t: 1, yo: 1 },
		"звать": { a: "impf", tr: 1, ps: "зов", t: 1, yo: 1 },
		"рвать": { a: "impf", tr: 1, ps: "рв", t: 1, yo: 1 },
		"ждать": { a: "impf", tr: 1, ps: "жд", t: 1, yo: 1 },
		"слать": { a: "impf", tr: 1, ps: "шл", t: 1, yo: 1, ju: 1, ppn: "" },
		"мять": { a: "impf", tr: 1, ps: "мн", t: 1, yo: 1, ppn: "", ppp: "мятый" },
		"жать": { a: "impf", tr: 1, ps: "жм", t: 1, yo: 1, ppn: "", ppp: "жатый" },
		"начать": { a: "pf", tr: 1, ps: "начн", t: 1, yo: 1, ppp: "начатый" },
		"понять": { a: "pf", tr: 1, ps: "пойм", t: 1, yo: 1, ppp: "понятый" },
		"принять": { a: "pf", tr: 1, ps: "прим", t: 1, ppp: "принятый" },
		"снять": { a: "pf", tr: 1, ps: "сним", t: 1, ppp: "снятый" },
		"занять": { a: "pf", tr: 1, ps: "займ", t: 1, yo: 1, ppp: "занятый" },
		"обнять": { a: "pf", tr: 1, ps: "обним", t: 1, ppp: "обнятый" },
		"поднять": { a: "pf", tr: 1, ps: "подним", t: 1, ppp: "поднятый" },
		"взять": { a: "pf", tr: 1, ps: "возьм", t: 1, yo: 1, ppp: "взятый" },
		"надеть": { a: "pf", tr: 1, ps: "наден", t: 1, imp: ["надень", "наденьте"], ppp: "надетый" },
		"одеть": { a: "pf", tr: 1, ps: "оден", t: 1, imp: ["одень", "оденьте"], ppp: "одетый" },
		"умереть": { a: "pf", tr: 0, ps: "умр", t: 1, yo: 1, past: ["умер", "умерла", "умерло", "умерли"], imp: ["умри", "умрите"], pap: "умерший", gst: "умере" },
		"тереть": { a: "impf", tr: 1, ps: "тр", t: 1, yo: 1, past: ["тёр", "тёрла", "тёрло", "тёрли"], imp: ["три", "трите"], ppn: "", pap: "тёрший", gst: "тере", ppp: "тёртый" },
		"запереть": { a: "pf", tr: 1, ps: "запр", t: 1, yo: 1, past: ["запер", "заперла", "заперло", "заперли"], imp: ["запри", "заприте"], ppn: "", pap: "заперший", gst: "запере", ppp: "запертый" },
		"стереть": { a: "pf", tr: 1, ps: "сотр", t: 1, yo: 1, past: ["стёр", "стёрла", "стёрло", "стёрли"], imp: ["сотри", "сотрите"], ppn: "", pap: "стёрший", gst: "стере", ppp: "стёртый" },
		"мочь": { a: "impf", tr: 0, ps: "мог", p2: "мож", t: 1, past: ["мог", "могла", "могло", "могли"], imp: null, ppa: "могущий", pap: "могший", gr: "", pg: "могши" },
		"печь": { a: "impf", tr: 1, ps: "пек", p2: "печ", t: 1, yo: 1, past: ["пёк", "пекла", "пекло", "пекли"], imp: ["пеки", "пеките"], ppn: "", ppa: "пекущий", pap: "пёкший", ppp: "печённый", gr: "", pg: "пёкши" },
		"течь": { a: "impf", tr: 0, ps: "тек", p2: "теч", t: 1, yo: 1, past: ["тёк", "текла", "текло", "текли"], imp: ["теки", "теките"], ppa: "текущий", pap: "тёкший", gr: "", pg: "тёкши" },
		"жечь": { a: "impf", tr: 1, ps: "жг", p2: "жж", t: 1, yo: 1, past: ["жёг", "жгла", "жгло", "жгли"], imp: ["жги", "жгите"], ppn: "", ppa: "жгущий", pap: "жёгший", ppp: "жжённый", gr: "", pg: "жёгши" },
		"стричь": { a: "impf", tr: 1, ps: "стриг", p2: "стриж", t: 1, yo: 1, past: ["стриг", "стригла", "стригло", "стригли"], imp: ["стриги", "стригите"], ppn: "", ppa: "стригущий", pap: "стригший", ppp: "стриженный", gr: "", pg: "стригши" },
		"беречь": { a: "impf", tr: 1, ps: "берег", p2: "береж", t: 1, yo: 1, past: ["берёг", "берегла", "берегло", "берегли"], imp: ["береги", "берегите"], ppn: "", ppa: "берегущий", pap: "берёгший", ppp: "бережённый", gr: "", pg: "берёгши" },
		"нести": { a: "impf", tr: 1, ps: "нес", t: 1, yo: 1, past: ["нёс", "несла", "несло", "несли"], imp: ["неси", "несите"], ppn: "несомый", pap: "нёсший", ppp: "несённый", pg: "нёсши" },
		"везти": { a: "impf", tr: 1, ps: "вез", t: 1, yo: 1, past: ["вёз", "везла", "везло", "везли"], imp: ["вези", "везите"], ppn: "", pap: "вёзший", ppp: "везённый", pg: "вёзши" },
		"вести": { a: "impf", tr: 1, ps: "вед", t: 1, yo: 1, past: ["вёл", "вела", "вело", "вели"], imp: ["веди", "ведите"], ppn: "ведомый", pap: "ведший", ppp: "ведённый", pg: "ведши" },
		"мести": { a: "impf", tr: 1, ps: "мет", t: 1, yo: 1, past: ["мёл", "мела", "мело", "мели"], imp: ["мети", "метите"], ppn: "", pap: "мётший", ppp: "метённый", pg: "мётши" },
		"расти": { a: "impf", tr: 0, ps: "раст", t: 1, yo: 1, past: ["рос", "росла", "росло", "росли"], imp: ["расти", "растите"], pap: "росший", pg: "росши" },
		"грести": { a: "impf", tr: 1, ps: "греб", t: 1, yo: 1, past: ["грёб", "гребла", "гребло", "гребли"], imp: ["греби", "гребите"], ppn: "", pap: "грёбший", ppp: "гребённый", pg: "грёбши" },
		"трясти": { a: "impf", tr: 1, ps: "тряс", t: 1, yo: 1, past: ["тряс", "трясла", "трясло", "трясли"], imp: ["тряси", "трясите"], ppn: "", pap: "трясший", ppp: "трясённый", pg: "трясши" },
		"цвести": { a: "impf", tr: 0, ps: "цвет", t: 1, yo: 1, past: ["цвёл", "цвела", "цвело", "цвели"], imp: ["цвети", "цветите"], pap: "цветший", pg: "цвётши" },
		"спасти": { a: "pf", tr: 1, ps: "спас", t: 1, yo: 1, past: ["спас", "спасла", "спасло", "спасли"], imp: ["спаси", "спасите"], ppn: "", pap: "спасший", ppp: "спасённый", pg: "спасши" },
		"лезть": { a: "impf", tr: 0, ps: "лез", t: 1, yo: 1, past: ["лез", "лезла", "лезло", "лезли"], imp: ["лезь", "лезьте"], pap: "лезший", pg: "лезши" },
		"грызть": { a: "impf", tr: 1, ps: "грыз", t: 1, yo: 1, past: ["грыз", "грызла", "грызло", "грызли"], imp: ["грызи", "грызите"], ppn: "", pap: "грызший", ppp: "" },
		"мокнуть": { a: "impf", tr: 0, ps: "мокн", t: 1, past: ["мок", "мокла", "мокло", "мокли"], pap: "мокший", gst: "мокну" },
		"сохнуть": { a: "impf", tr: 0, ps: "сохн", t: 1, past: ["сох", "сохла", "сохло", "сохли"], pap: "сохший", gst: "сохну" },
		"мерзнуть": { a: "impf", tr: 0, ps: "мерзн", t: 1, past: ["мёрз", "мёрзла", "мёрзло", "мёрзли"], pap: "мёрзший", gst: "мерзну" },
		"исчезнуть": { a: "pf", tr: 0, ps: "исчезн", t: 1, past: ["исчез", "исчезла", "исчезло", "исчезли"], imp: ["исчезни", "исчезните"], pap: "исчезший", gst: "исчезну" },
		"погибнуть": { a: "pf", tr: 0, ps: "погибн", t: 1, past: ["погиб", "погибла", "погибло", "погибли"], imp: ["погибни", "погибните"], pap: "погибший", gst: "погибну" },
		"привыкнуть": { a: "pf", tr: 0, ps: "привыкн", t: 1, past: ["привык", "привыкла", "привыкло", "привыкли"], imp: ["привыкни", "привыкните"], pap: "привыкший", gst: "привыкну" },
		"достигнуть": { a: "pf", tr: 1, ps: "достигн", t: 1, past: ["достиг", "достигла", "достигло", "достигли"], imp: ["достигни", "достигните"], pap: "достигший", gst: "достигну", ppp: "достигнутый" },
		"проснуть": { a: "pf", tr: 0, ps: "просн", t: 1, yo: 1 },
		"вернуть": { a: "pf", tr: 1, ps: "верн", t: 1, yo: 1 },
		"улыбнуть": { a: "pf", tr: 0, ps: "улыбн", t: 1, yo: 1 },
		"отдохнуть": { a: "pf", tr: 0, ps: "отдохн", t: 1, yo: 1 },
		"прыгнуть": { a: "pf", tr: 0, ps: "прыгн", t: 1 },
		"уснуть": { a: "pf", tr: 0, ps: "усн", t: 1, yo: 1 },
		"вздохнуть": { a: "pf", tr: 0, ps: "вздохн", t: 1, yo: 1 },
		"крикнуть": { a: "pf", tr: 0, ps: "крикн", t: 1 },
		"толкнуть": { a: "pf", tr: 1, ps: "толкн", t: 1 },
		"махнуть": { a: "pf", tr: 0, ps: "махн", t: 1 },
		"купить": { a: "pf", tr: 1, ps: "куп", p1: "купл", t: 2, ppp: "купленный" },
		"решить": { a: "pf", tr: 1, ps: "реш", t: 2, ppp: "решённый" },
		"получить": { a: "pf", tr: 1, ps: "получ", t: 2, ppp: "полученный" },
		"простить": { a: "pf", tr: 1, ps: "прост", p1: "прощ", t: 2, ppp: "прощённый" },
		"пустить": { a: "pf", tr: 1, ps: "пуст", p1: "пущ", t: 2, ppp: "пущенный" },
		"встретить": { a: "pf", tr: 1, ps: "встрет", p1: "встреч", t: 2, imp: null, ppp: "встреченный" },
		"ответить": { a: "pf", tr: 0, ps: "ответ", p1: "отвеч", t: 2, imp: ["ответь", "ответьте"] },
		"заметить": { a: "pf", tr: 1, ps: "замет", p1: "замеч", t: 2, imp: ["заметь", "заметьте"], ppp: "замеченный" },
		"объяснить": { a: "pf", tr: 1, ps: "объясн", t: 2, ppp: "объяснённый" },
		"продолжить": { a: "pf", tr: 1, ps: "продолж", t: 2, ppp: "продолженный" },
		"бросить": { a: "pf", tr: 1, ps: "брос", p1: "брош", t: 2, imp: ["брось", "бросьте"], ppp: "брошенный" },
		"кончить": { a: "pf", tr: 1, ps: "конч", t: 2, ppp: "конченный" },
		"включить": { a: "pf", tr: 1, ps: "включ", t: 2, ppp: "включённый" },
		"заключить": { a: "pf", tr: 1, ps: "заключ", t: 2, ppp: "заключённый" },
		"строить": { a: "impf", tr: 1, ps: "стро", t: 2, ppp: "строенный" },
		"продолжать": { a: "impf", tr: 1, ppp: "" },
		"решать": { a: "impf", tr: 1, ppp: "" },
		"считать": { a: "impf", tr: 1 },
		"успеть": { a: "pf", tr: 0, ps: "успе", t: 1 },
		"создать": { a: "pf", tr: 1, pres: ["создам", "создашь", "создаст", "создадим", "создадите", "создадут"], past: ["создал", "создала", "создало", "создали"], imp: ["создай", "создайте"], pap: "создавший", ppp: "созданный", pg: "создав" },
		"собрать": { a: "pf", tr: 1, ps: "собер", t: 1, yo: 1, ppp: "собранный" },
		"сорвать": { a: "pf", tr: 1, ps: "сорв", t: 1, yo: 1, ppp: "сорванный" },
		"созвать": { a: "pf", tr: 1, ps: "созов", t: 1, yo: 1, ppp: "созванный" },
		"сжать": { a: "pf", tr: 1, ps: "сожм", t: 1, yo: 1, ppp: "сжатый" },
		"сбить": { a: "pf", tr: 1, ps: "собь", t: 1, ju: 1, imp: ["сбей", "сбейте"], ppp: "сбитый" },
		"сшить": { a: "pf", tr: 1, ps: "сошь", t: 1, ju: 1, imp: ["сшей", "сшейте"], ppp: "сшитый" },
		"согнать": { a: "pf", tr: 1, ps: "сгон", t: 2, ppp: "согнанный" },
		"вбить": { a: "pf", tr: 1, ps: "вобь", t: 1, ju: 1, imp: ["вбей", "вбейте"], ppp: "вбитый" },
		"вшить": { a: "pf", tr: 1, ps: "вошь", t: 1, ju: 1, imp: ["вшей", "вшейте"], ppp: "вшитый" },
		"влить": { a: "pf", tr: 1, ps: "воль", t: 1, ju: 1, imp: ["влей", "влейте"], ppp: "влитый" },
		"слить": { a: "pf", tr: 1, ps: "соль", t: 1, ju: 1, imp: ["слей", "слейте"], ppp: "слитый" },
		"отлить": { a: "pf", tr: 1, ps: "отоль", t: 1, ju: 1, imp: ["отлей", "отлейте"], ppp: "отлитый" },
		"подлить": { a: "pf", tr: 1, ps: "подоль", t: 1, ju: 1, imp: ["подлей", "подлейте"], ppp: "подлитый" },
		"разлить": { a: "pf", tr: 1, ps: "разоль", t: 1, ju: 1, imp: ["разлей", "разлейте"], ppp: "разлитый" },
		"подшить": { a: "pf", tr: 1, ps: "подошь", t: 1, ju: 1, imp: ["подшей", "подшейте"], ppp: "подшитый" },
		"отшить": { a: "pf", tr: 1, ps: "отошь", t: 1, ju: 1, imp: ["отшей", "отшейте"], ppp: "отшитый" },
		"вжать": { a: "pf", tr: 1, ps: "вожм", t: 1, yo: 1, imp: ["вожми", "вожмите"], ppp: "вжатый" },
		"свить": { a: "pf", tr: 1, ps: "совь", t: 1, ju: 1, imp: ["свей", "свейте"], ppp: "свитый" },
		"развить": { a: "pf", tr: 1, ps: "разовь", t: 1, ju: 1, imp: ["развей", "развейте"], ppp: "развитый" },
		"разбить": { a: "pf", tr: 1, ps: "разобь", t: 1, ju: 1, imp: ["разбей", "разбейте"], ppp: "разбитый" },
		"отбить": { a: "pf", tr: 1, ps: "отобь", t: 1, ju: 1, imp: ["отбей", "отбейте"], ppp: "отбитый" },
		"подбить": { a: "pf", tr: 1, ps: "подобь", t: 1, ju: 1, imp: ["подбей", "подбейте"], ppp: "подбитый" },
		"взбить": { a: "pf", tr: 1, ps: "взобь", t: 1, ju: 1, imp: ["взбей", "взбейте"], ppp: "взбитый" },
		"избить": { a: "pf", tr: 1, ps: "изобь", t: 1, ju: 1, imp: ["избей", "избейте"], ppp: "избитый" },
		"разобрать": { a: "pf", tr: 1, ps: "разбер", t: 1, yo: 1, imp: ["разбери", "разберите"], ppp: "разобранный" },
		"отобрать": { a: "pf", tr: 1, ps: "отбер", t: 1, yo: 1, imp: ["отбери", "отберите"], ppp: "отобранный" },
		"подобрать": { a: "pf", tr: 1, ps: "подбер", t: 1, yo: 1, imp: ["подбери", "подберите"], ppp: "подобранный" },
		"вобрать": { a: "pf", tr: 1, ps: "вбер", t: 1, yo: 1, imp: ["вбери", "вберите"], ppp: "вобранный" },
		"разогнать": { a: "pf", tr: 1, ps: "разгон", t: 2, imp: ["разгони", "разгоните"], ppp: "разогнанный" },
		"забыть": { a: "pf", tr: 1, pres: ["забуду", "забудешь", "забудет", "забудем", "забудете", "забудут"], past: ["забыл", "забыла", "забыло", "забыли"], imp: ["забудь", "забудьте"], pap: "забывший", ppp: "забытый", pg: "забыв" }
	};
	var PICKS = [
		["cm", "читать", "读", "read"],
		["cm", "писать", "写", "write"],
		["cm", "говорить", "说", "speak"],
		["cm", "делать", "做", "do"],
		["cm", "работать", "工作", "work"],
		["cm", "любить", "爱", "love"],
		["cm", "видеть", "看见", "see"],
		["cm", "смотреть", "看", "watch"],
		["cm", "слышать", "听见", "hear"],
		["cm", "знать", "知道", "know"],
		["cm", "думать", "想", "think"],
		["cm", "жить", "生活", "live"],
		["cm", "идти", "走", "go on foot"],
		["cm", "ехать", "乘车去", "go by vehicle"],
		["cm", "петь", "唱", "sing"],
		["cm", "пить", "喝", "drink"],
		["cm", "есть", "吃", "eat"],
		["cm", "спать", "睡觉", "sleep"],
		["cm", "сидеть", "坐", "sit"],
		["cm", "стоять", "站", "stand"],
		["cm", "лежать", "躺", "lie"],
		["cm", "бежать", "跑", "run"],
		["cm", "хотеть", "想要", "want"],
		["cm", "мочь", "能够", "can"],
		["cm", "давать", "给", "give"],
		["cm", "брать", "拿", "take"],
		["cm", "учить", "教；学", "teach; learn"],
		["cm", "учиться", "学习", "study"],
		["cm", "играть", "玩", "play"],
		["cm", "гулять", "散步", "walk"],
		["cm", "покупать", "买", "buy"],
		["cm", "купить", "买到", "buy (pf)"],
		["cm", "строить", "建造", "build"],
		["cm", "готовить", "准备；做饭", "cook"],
		["cm", "открывать", "打开", "open"],
		["cm", "открыть", "打开", "open (pf)"],
		["cm", "закрывать", "关上", "close"],
		["cm", "закрыть", "关上", "close (pf)"],
		["cm", "находить", "找到", "find"],
		["cm", "найти", "找到", "find (pf)"],
		["cm", "ждать", "等待", "wait"],
		["cm", "слушать", "听", "listen"],
		["cm", "помогать", "帮助", "help"],
		["cm", "нравиться", "喜欢", "be pleasing"],
		["cm", "бояться", "害怕", "fear"],
		["cm", "смеяться", "笑", "laugh"],
		["cm", "улыбаться", "微笑", "smile"],
		["cm", "вставать", "起床", "get up"],
		["cm", "ложиться", "躺下", "lie down"],
		["cm", "садиться", "坐下", "sit down"],
		["cm", "возвращаться", "返回", "return"],
		["cm", "начинать", "开始", "begin"],
		["cm", "продолжать", "继续", "continue"],
		["cm", "решать", "决定", "decide"],
		["cm", "решить", "决定", "decide (pf)"],
		["cm", "использовать", "使用", "use"],
		["cm", "танцевать", "跳舞", "dance"],
		["cm", "рисовать", "画画", "draw"],
		["cm", "отдыхать", "休息", "rest"],
		["irr", "быть", "是", "be"],
		["irr", "дать", "给", "give"],
		["irr", "мочь", "能够", "can"],
		["irr", "печь", "烤", "bake"],
		["irr", "нести", "携带", "carry"],
		["irr", "вести", "带领", "lead"],
		["irr", "везти", "运送", "transport"],
		["irr", "расти", "生长", "grow"],
		["irr", "лечь", "躺下", "lie down"],
		["irr", "сесть", "坐下", "sit down"],
		["irr", "стать", "成为", "become"],
		["irr", "брать", "拿", "take"],
		["irr", "звать", "叫", "call"],
		["irr", "ждать", "等待", "wait"],
		["irr", "начать", "开始", "begin"],
		["irr", "понять", "明白", "understand"],
		["irr", "принять", "接受", "accept"],
		["irr", "взять", "拿", "take"],
		["irr", "надеть", "穿上", "put on"],
		["irr", "умереть", "死", "die"],
		["irr", "тереть", "擦", "rub"],
		["irr", "исчезнуть", "消失", "disappear"],
		["irr", "погибнуть", "死亡", "perish"],
		["irr", "прыгнуть", "跳", "jump"],
		["irr", "проснуться", "醒来", "wake up"],
		["irr", "вернуться", "返回", "return"],
		["irr", "улыбнуться", "微笑", "smile"],
		["irr", "забыть", "忘记", "forget"]
	];
	var PICKG = {
		cm: ["常见动词", "Common"],
		irr: ["不规则 / 特殊变化", "Irregular"]
	};
	var PREFIXES = ["противо", "пере", "пред", "подо", "разо", "обо", "ото", "изо", "произо", "во", "со", "взо", "недо", "полу", "сверх", "около", "про", "при", "за", "на", "вы", "от", "до", "под", "над", "об", "раз", "рас", "из", "ис", "воз", "вос", "вз", "вс", "без", "бес", "пре", "съ", "въ", "объ", "отъ", "подъ", "надъ", "разъ", "в", "с", "по", "о", "у", "не"];
	var MUT_A = {
		"писа": "пиш", "иска": "ищ", "плака": "плач", "маха": "маш", "скака": "скач",
		"лиза": "лиж", "вяза": "вяж", "реза": "реж", "прята": "пряч", "хлеста": "хлещ",
		"полоска": "полощ", "сыпа": "сыпл", "трепа": "трепл", "колеба": "колебл",
		"дрема": "дремл", "щипа": "щипл", "топта": "топч", "клевета": "клевещ",
		"ропта": "ропщ", "трепета": "трепещ", "щебета": "щебеч", "каза": "каж"
	};
	var PERSON = [
		["я（我）", "ты（你）", "он / она（他/她）", "мы（我们）", "вы（你们/您）", "они（他们）"],
		["я (I)", "ты (you sg.)", "он / она (he/she)", "мы (we)", "вы (you pl.)", "они (they)"]
	];
	var PAST = [
		["м. р.（он）", "ж. р.（она）", "ср. р.（оно）", "мн. ч.（они）"],
		["masc. (he)", "fem. (she)", "neut. (it)", "plural (they)"]
	];
	var PERSON_FUT = ["буду", "будешь", "будет", "будем", "будете", "будут"];
	var MOTION = { "ходить": 1, "ездить": 1, "носить": 1, "водить": 1, "летать": 1, "бегать": 1, "плавать": 1, "ползать": 1, "лазить": 1, "езжать": 1 };
	var IMPF_NUT = { "тянуть": 1, "тонуть": 1, "вянуть": 1, "гнуть": 1, "льнуть": 1, "стынуть": 1, "мерзнуть": 1, "мокнуть": 1, "сохнуть": 1, "киснуть": 1, "слепнуть": 1, "глохнуть": 1, "крепнуть": 1, "пухнуть": 1, "виснуть": 1, "липнуть": 1, "зябнуть": 1, "гаснуть": 1 };
	var VOW = "аеёиоуыэюя";
	var LAB = {
		title: ["俄语动词变形", "Russian verb conjugation"],
		hint: ["输入俄语动词不定式（-ть / -ти / -чь，可带 -ся），按体列出现在时或将来时、过去时、命令式、假定式、分词和副动词。完成体没有现在时，第一组是将来时。重音不标；分词只给阳性单数完整形式，另有短尾。体默认按词尾和前缀推断，可以手动改。",
			"Enter a Russian infinitive (-ть / -ти / -чь, optionally with -ся). The table lists present or future, past, imperative, subjunctive, participles and gerunds. Perfective verbs have no present, so their finite forms are future. Stress is not marked. Participles are given in the masculine nominative, with short forms where available. Aspect is inferred from the ending and prefix and can be overridden."],
		word: ["动词", "Verb"],
		aspect: ["体", "Aspect"],
		ph: ["不定式，如 читать、говорить、идти", "Infinitive, e.g. читать, говорить, идти"],
		auto: ["自动", "Auto"],
		impf: ["未完成体", "Imperfective"],
		pf: ["完成体", "Perfective"],
		bad: ["请输入俄语动词不定式，如 читать、говорить、идти", "Enter a Russian infinitive, e.g. читать, говорить, идти"],
		unsup: ["暂不支持这个词尾，试试 -ать / -ить / -еть / -нуть / -овать 或下拉里的动词", "This ending is not supported yet; try -ать / -ить / -еть / -нуть / -овать or a verb from the list"],
		nospeak: ["无法发音", "Can't play audio"],
		g_basic: ["基本形式", "Basic forms"],
		g_pres: ["现在时", "Present"],
		g_fut: ["将来时", "Future"],
		g_pf: ["将来时（完成体）", "Future (perfective)"],
		g_past: ["过去时", "Past"],
		g_imp: ["命令式", "Imperative"],
		g_sub: ["假定式", "Subjunctive"],
		g_ppa: ["主动分词 · 现在时", "Active participle · present"],
		g_pap: ["主动分词 · 过去时", "Active participle · past"],
		g_ppn: ["被动分词 · 现在时", "Passive participle · present"],
		g_ppp: ["被动分词 · 过去时", "Passive participle · past"],
		g_spp: ["被动分词 · 短尾", "Short passive participle"],
		g_ger: ["副动词", "Gerund"],
		f_inf: ["不定式", "Infinitive"],
		f_aspect: ["体", "Aspect"],
		f_conj: ["变位", "Conjugation"],
		f_tr: ["及物性", "Transitivity"],
		t1: ["第一变位", "First conjugation"],
		t2: ["第二变位", "Second conjugation"],
		tr_yes: ["及物", "Transitive"],
		tr_no: ["不及物", "Intransitive"],
		imp_t: ["ты", "ты (you sg.)"],
		imp_v: ["вы", "вы (you pl.)"],
		imp_1: ["давай(те) + 不定式", "давай(те) + infinitive"],
		imp_3: ["пусть + 3-е л.", "пусть + 3rd person"],
		noimp: ["没有命令式", "No imperative"],
		gr_pres: ["现在时", "Present"],
		gr_past: ["过去时", "Past"],
		from: ["由 %s 加前缀", "from %s + prefix"],
		refl: ["带 -ся", "Reflexive -ся"],
		be_pres: ["есть（3-е л. ед. ч.）", "есть (3rd person singular)"],
		speak: ["发音", "Speak"],
		stress: ["重音未标", "Stress not marked"],
		note_pf: ["完成体没有现在时；上面列的是将来时。", "Perfective verbs have no present; the finite forms above are future."]
	};
	var timer = 0;
	var clip = null;

	function build(word, lang, opt){
		lang = lang || "zh";
		opt = opt || {};
		var w = norm(word);
		var refl = false;
		if (/ся$/.test(w)) {
			refl = true;
			w = w.slice(0, -2);
		}
		if (!w || !/^[а-яё-]+$/.test(w)) return { ok: false, err: L(lang, LAB.bad) };
		if (!/(ть|ти|чь)$/.test(w)) return { ok: false, err: L(lang, LAB.bad) };
		var hit = resolve(w);
		var d = hit ? hit.d : null;
		var pref = hit ? hit.pref : "";
		var base = hit ? hit.base : w;
		var info = conj(base, d, pref, refl, opt, lang);
		if (!info) return { ok: false, err: L(lang, LAB.unsup) };
		var full = w + (refl ? "ся" : "");
		var rows = [];
		function R(g, name, text){ rows.push({ g: g, name: name, text: text }); }
		var g = L(lang, LAB.g_basic);
		R(g, L(lang, LAB.f_inf), full);
		R(g, L(lang, LAB.f_aspect), info.a === "pf" ? L(lang, LAB.pf) : L(lang, LAB.impf));
		R(g, L(lang, LAB.f_conj), info.t === 2 ? L(lang, LAB.t2) : L(lang, LAB.t1));
		if (info.tr === 1) R(g, L(lang, LAB.f_tr), L(lang, LAB.tr_yes));
		else if (info.tr === 0 && d && d.tr !== undefined) R(g, L(lang, LAB.f_tr), L(lang, LAB.tr_no));
		var isBe = base === "быть" && !pref;
		if (isBe) {
			g = L(lang, LAB.g_pres);
			R(g, L(lang, LAB.be_pres), "есть");
			g = L(lang, LAB.g_fut);
			for (var j = 0; j < 6; j++) R(g, PERSON[lang === "zh" ? 0 : 1][j], info.fut[j]);
		}
		else {
			g = L(lang, info.a === "pf" ? LAB.g_pf : LAB.g_pres);
			for (var j2 = 0; j2 < 6; j2++) R(g, PERSON[lang === "zh" ? 0 : 1][j2], info.pres[j2]);
			if (info.a !== "pf") {
				g = L(lang, LAB.g_fut);
				for (var j3 = 0; j3 < 6; j3++) R(g, PERSON[lang === "zh" ? 0 : 1][j3], info.comp[j3]);
			}
		}
		g = L(lang, LAB.g_past);
		for (var j4 = 0; j4 < 4; j4++) R(g, PAST[lang === "zh" ? 0 : 1][j4], info.past[j4]);
		g = L(lang, LAB.g_imp);
		if (info.imp) {
			R(g, L(lang, LAB.imp_t), info.imp[0]);
			R(g, L(lang, LAB.imp_v), info.imp[1]);
			R(g, L(lang, LAB.imp_1), "давай(те) " + full);
			R(g, L(lang, LAB.imp_3), isBe ? "пусть будет / пусть будут" : "пусть " + info.pres[2] + " / пусть " + info.pres[5]);
		}
		else R(g, L(lang, LAB.noimp), "—");
		g = L(lang, LAB.g_sub);
		for (var j5 = 0; j5 < 4; j5++) R(g, PAST[lang === "zh" ? 0 : 1][j5], info.past[j5] + " бы");
		if (info.a !== "pf" && info.ppa) {
			g = L(lang, LAB.g_ppa);
			for (var j6 = 0; j6 < 4; j6++) R(g, PAST[lang === "zh" ? 0 : 1][j6], info.ppa[j6]);
		}
		if (info.pap) {
			g = L(lang, LAB.g_pap);
			for (var j7 = 0; j7 < 4; j7++) R(g, PAST[lang === "zh" ? 0 : 1][j7], info.pap[j7]);
		}
		if (info.a !== "pf" && info.ppn) {
			g = L(lang, LAB.g_ppn);
			for (var j8 = 0; j8 < 4; j8++) R(g, PAST[lang === "zh" ? 0 : 1][j8], info.ppn[j8]);
		}
		if (info.ppp) {
			g = L(lang, LAB.g_ppp);
			for (var j9 = 0; j9 < 4; j9++) R(g, PAST[lang === "zh" ? 0 : 1][j9], info.ppp[j9]);
		}
		if (info.spp) {
			g = L(lang, LAB.g_spp);
			for (var j10 = 0; j10 < 4; j10++) R(g, PAST[lang === "zh" ? 0 : 1][j10], info.spp[j10]);
		}
		if (info.a !== "pf" && info.gr) {
			g = L(lang, LAB.g_ger);
			R(g, L(lang, LAB.gr_pres), info.gr);
		}
		if (info.a === "pf" && info.pg) {
			g = L(lang, LAB.g_ger);
			R(g, L(lang, LAB.gr_past), info.pg);
		}
		var note = [];
		note.push(info.a === "pf" ? L(lang, LAB.pf) : L(lang, LAB.impf));
		note.push(info.t === 2 ? L(lang, LAB.t2) : L(lang, LAB.t1));
		if (info.tr === 1) note.push(L(lang, LAB.tr_yes));
		if (pref) note.push(fmt(L(lang, LAB.from), base) + " " + pref + "-");
		if (refl) note.push(L(lang, LAB.refl));
		note.push(L(lang, LAB.stress));
		return { ok: true, head: full, note: note.join(" · "), rows: rows, a: info.a };
	}

	function conj(base, d, pref, refl, opt, lang){
		var i;
		var t = 1;
		var ps = "";
		var p1 = null;
		var p2 = null;
		var yo = 0;
		var ju = 0;
		var pres = null;
		var fut = null;
		var past = null;
		var pst = null;
		var nol = 0;
		var gst = null;
		var imp = null;
		var imp0 = null;
		var ppa = null;
		var pap = null;
		var ppn = null;
		var ppp = null;
		var gr = null;
		var pg = null;
		var a = "impf";
		var tr = 0;
		var knownTr = false;
		var jt = 0;
		var done = false;
		if (d) {
			if (d.a) a = d.a;
			if (d.tr !== undefined) { tr = d.tr; knownTr = true; }
			t = d.t || 1;
			ps = d.ps || "";
			p1 = d.p1 !== undefined ? d.p1 : null;
			p2 = d.p2 || null;
			yo = d.yo || 0;
			ju = d.ju || 0;
			pres = d.pres ? d.pres.slice() : null;
			fut = d.fut ? d.fut.slice() : null;
			past = d.past ? d.past.slice() : null;
			pst = d.pst !== undefined ? d.pst : null;
			nol = d.nol || 0;
			gst = d.gst !== undefined ? d.gst : null;
			imp = d.imp ? d.imp.slice() : (d.imp === null ? null : null);
			imp0 = d.imp0 ? d.imp0.slice() : null;
			ppa = d.ppa !== undefined ? d.ppa : null;
			pap = d.pap !== undefined ? d.pap : null;
			ppn = d.ppn !== undefined ? d.ppn : null;
			ppp = d.ppp !== undefined ? d.ppp : null;
			gr = d.gr !== undefined ? d.gr : null;
			pg = d.pg !== undefined ? d.pg : null;
		}
		a = calcAspect(base, d, pref, opt.aspect);
		if (refl) { tr = 0; knownTr = true; }
		if (t === 2 && !pres && !p1 && ps) p1 = mutIt(ps);
		if (base === "идти" && pref) {
			jt = 1;
			var roots = ["йду", "йдёшь", "йдёт", "йдём", "йдёте", "йдут"];
			var dropY = /и$/.test(pref);
			pres = [];
			for (i = 0; i < 6; i++) pres.push(pref + (dropY ? roots[i].slice(1) : roots[i]));
			past = [];
			var pbase = ["шёл", "шла", "шло", "шли"];
			if (pref === "вы") pbase = ["шел", "шла", "шло", "шли"];
			for (i = 0; i < 4; i++) past.push(pref + pbase[i]);
			imp = [pref + (dropY ? "ди" : "йди"), pref + (dropY ? "дите" : "йдите")];
			ppa = [pref + "идущий", pref + "идущая", pref + "идущее", pref + "идущие"];
			pap = [pref + "шедший", pref + "шедшая", pref + "шедшее", pref + "шедшие"];
			gr = pref + "идя";
			pg = pref + "шедши";
			t = 1;
			a = "pf";
			tr = 0;
			knownTr = true;
			done = true;
		}
		else if (base === "быть" && pref) {
			pres = [];
			for (i = 0; i < 6; i++) pres.push(pref + fut[i]);
			past = [];
			for (i = 0; i < 4; i++) past.push(pref + d.past[i]);
			imp = [pref + d.imp[0], pref + d.imp[1]];
			ppa = null;
			ppn = null;
			pap = [pref + "бывший", pref + "бывшая", pref + "бывшее", pref + "бывшие"];
			ppp = null;
			gr = null;
			pg = pref + "быв";
			t = 1;
			a = "pf";
			tr = d.tr;
			knownTr = true;
			done = true;
		}
		if (!pres) {
			if (!ps && !d) {
				var reg = derive(base);
				if (!reg) return null;
				t = reg.t;
				ps = reg.ps;
				p1 = reg.p1 || null;
				yo = reg.yo || 0;
				ju = reg.ju || 0;
			}
			else if (!ps) {
				var reg2 = derive(base);
				if (!reg2) return null;
				t = reg2.t;
				ps = reg2.ps;
				p1 = p1 || reg2.p1 || null;
				yo = yo || reg2.yo || 0;
				ju = ju || reg2.ju || 0;
			}
			pres = present(ps, t, yo, ju, p1, p2);
		}
		if (!past) {
			if (pst === null) pst = base.replace(/ть$/, "");
			if (gst === null) gst = pst;
			if (nol) {
				past = [pst, pst + "ла", pst + "ло", pst + "ли"];
			}
			else {
				past = [pst + "л", pst + "ла", pst + "ло", pst + "ли"];
			}
		}
		if (pst === null) {
			if (d && d.past && d.past.length) pst = String(d.past[0]).replace(/л$/, "");
			else pst = base.replace(/ть$/, "");
		}
		if (gst === null) gst = pst;
		if (base === "ехать" && pref && imp0) imp = imp0.slice();
		if (imp === null && d && d.imp === null) imp = null;
		else if (imp === null) {
			if (base === "ехать" && !pref && d && d.imp) imp = d.imp.slice();
			else if (/авать$/.test(base)) imp = [base.slice(0, -2) + "й", base.slice(0, -2) + "йте"];
			else imp = imperative(ps, t);
		}
		if (!ppa && ppa !== "" && a !== "pf") ppa = presAct(ps, t, refl, ju);
		if (!pap && pap !== "" ) pap = pastAct(pst, t, refl);
		if (ppn === null && a !== "pf" && tr && !refl) ppn = presPass(ps, t, base);
		if (ppp === null && tr && !refl) ppp = pastPass(base, t);
		if (!gr && gr !== "" && a !== "pf") {
			if (/авать$/.test(base)) gr = base.slice(0, -2) + "я";
			else gr = gerPres(ps, t);
		}
		if (!pg && pg !== "" && a === "pf") pg = gerPast(gst);
		if (d && d.note) { /* 预留 */ }
		var pfx = done ? "" : pref;
		var finPres = [];
		for (i = 0; i < 6; i++) finPres.push(fixRefl(prefForm(pres[i], pfx, base), refl));
		var comp = [];
		for (i = 0; i < 6; i++) comp.push(PERSON_FUT[i] + " " + (done ? "" : pref) + base + (refl ? "ся" : ""));
		var finPast = [];
		for (i = 0; i < 4; i++) finPast.push(fixRefl(prefForm(past[i], pfx, base), refl));
		var finImp = null;
		if (imp) finImp = [fixRefl(prefForm(imp[0], pfx, base), refl), fixRefl(prefForm(imp[1], pfx, base), refl)];
		var out = {
			a: a, t: t, tr: knownTr ? tr : 0, refl: refl, pref: pref, base: base,
			pres: finPres, comp: comp, past: finPast, imp: finImp, fut: fut,
			ppa: partOut(ppa, pfx, base, refl, false),
			pap: partOut(pap, pfx, base, refl, false),
			ppn: partOut(ppn, pfx, base, refl, false),
			ppp: partOut(ppp, pfx, base, refl, false),
			gr: gr ? fixRefl(prefForm(gr, pfx, base), false) + (refl ? "сь" : "") : "",
			pg: pg ? prefForm(pg, pfx, base) + (refl ? "шись" : "") : ""
		};
		if (out.ppp) {
			var short = shortPart(out.ppp[0]);
			if (short) out.spp = [short[0], short[1], short[2], short[3]];
		}
		if (refl) {
			out.ppn = null;
			out.ppp = null;
			out.spp = null;
		}
		if (pref === "вы") {
			out.pres = out.pres.map(deyo);
			out.comp = out.comp;
			out.past = out.past.map(deyo);
			if (out.imp) out.imp = out.imp.map(deyo);
			out.ppa = out.ppa ? out.ppa.map(deyo) : null;
			out.pap = out.pap ? out.pap.map(deyo) : null;
			out.ppn = out.ppn ? out.ppn.map(deyo) : null;
			out.ppp = out.ppp ? out.ppp.map(deyo) : null;
			out.spp = out.spp ? out.spp.map(deyo) : null;
			out.gr = out.gr ? deyo(out.gr) : out.gr;
			out.pg = out.pg ? deyo(out.pg) : out.pg;
			out.pres = out.pres;
		}
		return out;
	}

	function derive(base){
		var stem;
		if (/овать$/.test(base)) {
			stem = base.slice(0, -5);
			return { t: 1, ps: stem + "у" };
		}
		if (/евать$/.test(base)) {
			stem = base.slice(0, -5);
			return { t: 1, ps: stem + (/[жшчщц]$/.test(stem) ? "у" : "ю") };
		}
		if (/ывать$|ивать$/.test(base)) {
			return { t: 1, ps: base.slice(0, -2) };
		}
		if (/авать$/.test(base)) {
			return { t: 1, ps: base.slice(0, -5) + "а", yo: 1 };
		}
		if (/нуть$/.test(base)) {
			return { t: 1, ps: base.slice(0, -3) };
		}
		if (/ить$/.test(base)) {
			stem = base.slice(0, -3);
			return { t: 2, ps: stem, p1: mutIt(stem) };
		}
		if (/еть$/.test(base)) {
			return { t: 1, ps: base.slice(0, -2) };
		}
		if (/ать$|ять$/.test(base)) {
			stem = base.slice(0, -2);
			if (/ять$/.test(base) && stem.length > 1 && vowel(stem.charAt(stem.length - 2))) stem = stem.slice(0, -1);
			return { t: 1, ps: MUT_A[stem] || stem };
		}
		if (/оть$/.test(base)) {
			return { t: 1, ps: base.slice(0, -3) };
		}
		if (/ыть$/.test(base)) {
			return { t: 1, ps: base.slice(0, -3) + "о" };
		}
		if (/уть$/.test(base)) {
			return { t: 1, ps: base.slice(0, -2) };
		}
		return null;
	}

	function present(stem, t, yo, ju, p1, p2){
		var i, s, out = [];
		if (t === 1) {
			var ends;
			if (ju) ends = ["ю", "ёшь", "ёт", "ём", "ёте", "ют"];
			else if (yo && vowel(last(stem))) ends = ["ю", "ёшь", "ёт", "ём", "ёте", "ют"];
			else if (yo) ends = ["у", "ёшь", "ёт", "ём", "ёте", "ут"];
			else if (vowel(last(stem))) ends = ["ю", "ешь", "ет", "ем", "ете", "ют"];
			else ends = ["у", "ешь", "ет", "ем", "ете", "ут"];
			for (i = 0; i < 6; i++) {
				s = (i >= 1 && i <= 4 && p2) ? p2 : stem;
				out.push(s + ends[i]);
			}
			return out;
		}
		for (i = 0; i < 6; i++) {
			if (i === 0) {
				s = p1 || stem;
				out.push(s + (sibilant(last(s)) ? "у" : "ю"));
			}
			else if (i === 5) out.push(stem + (sibilant(last(stem)) ? "ат" : "ят"));
			else out.push(stem + ["", "ишь", "ит", "им", "ите"][i]);
		}
		return out;
	}

	function imperative(stem, t){
		if (!stem) return null;
		if (vowel(last(stem))) return [stem + "й", stem + "йте"];
		return [stem + "и", stem + "ите"];
	}

	function presAct(stem, t, refl, ju){
		var base;
		if (t === 1) base = stem + (ju || vowel(last(stem)) ? "ющий" : "ущий");
		else base = stem + (sibilant(last(stem)) ? "ащий" : "ящий");
		return adjI(base, refl);
	}

	function presPass(stem, t, base){
		var s;
		if (t === 2) s = stem + "имый";
		else if (/овать$|-евать$/.test(base)) s = stem + "емый";
		else if (/ыть$|-оть$|-еть$/.test(base)) s = stem + "емый";
		else s = base.replace(/ть$/, "") + "емый";
		return adjY(s, false);
	}

	function pastAct(pst, t, refl){
		var s = pst + (vowel(last(pst)) ? "вший" : "ший");
		return adjI(s, refl);
	}

	function pastPass(base, t){
		var s;
		if (/ать$|-ять$/.test(base)) s = base.slice(0, -2) + "нный";
		else if (/еть$/.test(base)) s = base.slice(0, -3) + "енный";
		else if (/ить$/.test(base)) {
			var st = base.slice(0, -3);
			var st2 = mutIt(st);
			s = st2 + "енный";
		}
		else if (/нуть$/.test(base)) s = base.slice(0, -2) + "тый";
		else if (/оть$|-ыть$|-уть$/.test(base)) s = base.slice(0, -2) + "тый";
		else if (/овать$|-евать$/.test(base)) s = base.slice(0, -2) + "нный";
		else s = base.slice(0, -2) + "нный";
		return adjY(s, false);
	}

	function gerPres(stem, t, refl){
		var s = sibilant(last(stem)) ? stem + "а" : stem + "я";
		return refl ? s + "сь" : s;
	}

	function gerPast(gst, refl){
		var s = vowel(last(gst)) ? gst + "в" : gst + "ши";
		return refl ? s + "шись" : s;
	}

	function adjI(base, refl){
		var root = base.slice(0, -2);
		var arr = [base, root + "ая", root + "ее", root + "ие"];
		return partRefl(arr, refl);
	}

	function adjY(base, refl){
		var root = base.slice(0, -2);
		var arr = [base, root + "ая", root + "ое", root + "ые"];
		return partRefl(arr, refl);
	}

	function partRefl(arr, refl){
		if (!refl) return arr;
		var out = [];
		for (var i = 0; i < arr.length; i++) out.push(arr[i] + "ся");
		return out;
	}

	function partOut(arr, pref, base, refl, deyo){
		if (arr === null || arr === undefined) return null;
		if (typeof arr === "string") {
			if (!arr) return null;
			arr = /ий$/.test(arr) ? adjI(arr, false) : (/ый$/.test(arr) ? adjY(arr, false) : [arr]);
		}
		var out = [];
		for (var i = 0; i < arr.length; i++) {
			var s = prefForm(arr[i], pref, base);
			if (refl && !/ся$/.test(s)) s += "ся";
			out.push(s);
		}
		return out;
	}

	function shortPart(m){
		if (!m || !/ый$/.test(m)) return null;
		var s = m.slice(0, -2);
		if (/нн$/.test(s)) s = s.slice(0, -1);
		return [s, s + "а", s + "о", s + "ы"];
	}

	function prefForm(s, pref, base){
		if (!pref) return s;
		return pref + s;
	}

	function fixRefl(s, refl){
		if (!refl) return s;
		return s + (vowel(last(s)) ? "сь" : "ся");
	}

	function deyo(s){
		return String(s).replace(/ё/g, "е");
	}

	function calcAspect(base, d, pref, manual){
		if (manual === "impf" || manual === "pf") return manual;
		if (d && d.a) {
			if (!pref) return d.a;
			if (d.a === "impf" && MOTION[base]) return "impf";
			return "pf";
		}
		if (!pref) {
			if (IMPF_NUT[base]) return "impf";
			if (/нуть$/.test(base)) return "pf";
			return "impf";
		}
		return "pf";
	}

	function resolve(base){
		if (CONF[base]) return { d: CONF[base], pref: "", base: base };
		var i, p, rest;
		for (i = 0; i < PREFIXES.length; i++) {
			p = PREFIXES[i];
			if (base.indexOf(p) !== 0) continue;
			rest = base.slice(p.length);
			if (rest.length < 3) continue;
			if (CONF[rest]) return { d: CONF[rest], pref: p, base: rest };
		}
		if (/йти$/.test(base) && base !== "идти") {
			p = base.slice(0, -3);
			if (p) return { d: CONF["идти"], pref: p, base: "идти", jt: 1 };
		}
		return null;
	}

	function norm(s){
		s = String(s == null ? "" : s).toLowerCase().replace(/\s+/g, "");
		s = s.replace(/ё/g, "ё");
		return s.replace(/^-+|-+$/g, "");
	}

	function last(s){
		return s ? s.charAt(s.length - 1) : "";
	}

	function vowel(ch){
		return VOW.indexOf(ch) >= 0;
	}

	function sibilant(ch){
		return "жшчщ".indexOf(ch) >= 0;
	}

	function mutIt(stem){
		if (/ст$/.test(stem)) return stem.slice(0, -2) + "щ";
		if (/ск$/.test(stem)) return stem.slice(0, -2) + "щ";
		if (/т$/.test(stem)) return stem.slice(0, -1) + "ч";
		if (/д$/.test(stem)) return stem.slice(0, -1) + "ж";
		if (/с$/.test(stem)) return stem.slice(0, -1) + "ш";
		if (/з$/.test(stem)) return stem.slice(0, -1) + "ж";
		if (/[бпвмф]$/.test(stem)) return stem + "л";
		return stem;
	}

	function L(lang, pair){
		return lang === "zh" ? pair[0] : pair[1];
	}

	function fmt(s, v){
		return String(s).replace("%s", v);
	}

	function pickgroups(lang){
		var groups = [];
		var cur = null;
		var i, row, gloss, name;
		for (i = 0; i < PICKS.length; i++) {
			row = PICKS[i];
			name = lang === "zh" ? PICKG[row[0]][0] : PICKG[row[0]][1];
			if (!cur || cur.g !== name) {
				cur = { g: name, items: [] };
				groups.push(cur);
			}
			gloss = lang === "zh" ? row[2] : row[3];
			cur.items.push({ v: row[1], t: row[1] + "  " + gloss });
		}
		return groups;
	}

	if (typeof module !== "undefined" && module.exports) {
		module.exports = { build: build, conf: CONF, picks: PICKS, derive: derive };
		return;
	}
	if (typeof SK === "undefined" || !SK.regutil) return;

	SK.regutil("u-rverb", {
		title: "俄语动词变化",
		go: "变形",
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

	function langcode(){
		var sel = F.id("lang");
		return sel && sel.value ? sel.value : "zh";
	}

	function body(){
		return '<p class="hint" id="rv-hint"></p>'
			+ '<div class="row" id="rv-row">'
			+ '<label id="rv-word">' + SK.combo("rv-in", "читать", "不定式，如 читать、говорить、идти") + "</label>"
			+ '<label id="rv-aspect"><select id="rv-sel">'
			+ '<option value="auto"></option>'
			+ '<option value="impf"></option>'
			+ '<option value="pf"></option>'
			+ "</select></label>"
			+ "</div>"
			+ '<div id="rv-out"></div>';
	}

	function open(){
		var lang = langcode();
		var hint = F.id("rv-hint");
		if (hint) {
			F.seta(hint, "data-k", "web.rverb.hint");
			hint.textContent = tx("web.rverb.hint", L(lang, LAB.hint));
		}
		var title = F.id("box-title");
		if (title) title.textContent = tx("web.tool.rverb", L(lang, LAB.title));
		setlab("rv-word", "web.rverb.word", L(lang, LAB.word));
		setlab("rv-aspect", "web.rverb.aspect", L(lang, LAB.aspect));
		SK.combofill("rv-in", pickgroups(lang));
		setopt("auto", "web.rverb.auto", L(lang, LAB.auto));
		setopt("impf", "web.rverb.impf", L(lang, LAB.impf));
		setopt("pf", "web.rverb.pf", L(lang, LAB.pf));
		var box = F.id("rv-in");
		if (box) {
			F.seta(box, "data-kph", "web.rverb.ph");
			box.placeholder = tx("web.rverb.ph", L(lang, LAB.ph));
		}
		var out = F.id("rv-out");
		if (out && !F.attr(out, "data-bind")) {
			F.seta(out, "data-bind", "1");
			out.onclick = onout;
		}
		run();
	}

	function setlab(id, key, fb){
		var el = F.id(id);
		if (!el) return;
		var text = tx(key, fb);
		if (el.childNodes.length && el.childNodes[0].nodeType === 3) el.childNodes[0].nodeValue = text;
		else el.insertBefore(document.createTextNode(text), el.firstChild);
	}

	function setopt(value, key, fb){
		var sel = F.id("rv-sel");
		if (!sel) return;
		for (var i = 0; i < sel.options.length; i++) {
			if (sel.options[i].value === value) sel.options[i].textContent = tx(key, fb);
		}
	}

	function onout(ev){
		var btn = ev.target;
		var out = F.id("rv-out");
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
		var audio = new Audio("/api/dict/speak?text=" + encodeURIComponent(text) + "&lang=ru");
		clip = audio;
		audio.onerror = function(){
			if (clip === audio) SK.msg("box-msg", tx("web.rverb.nospeak", "无法发音"), true);
		};
		var played = audio.play();
		if (played && played.then) played.then(function(){}, function(){
			if (clip === audio) SK.msg("box-msg", tx("web.rverb.nospeak", "无法发音"), true);
		});
	}

	function run(){
		if (timer) clearTimeout(timer);
		timer = setTimeout(go, 150);
	}

	function go(){
		timer = 0;
		var box = F.id("rv-in");
		var word = box ? box.value : "";
		var sel = F.id("rv-sel");
		var manual = sel ? sel.value : "auto";
		if (!norm(word)) {
			paint("", "", []);
			SK.msg("box-msg", "");
			return;
		}
		var built = build(word, langcode(), { aspect: manual });
		if (!built.ok) {
			paint("", "", []);
			SK.msg("box-msg", built.err, true);
			return;
		}
		paint(built.head, built.note, built.rows);
		SK.msg("box-msg", "");
	}

	function paint(head, note, rows){
		var out = F.id("rv-out");
		if (!out) return;
		if (!rows || !rows.length) { out.innerHTML = ""; return; }
		var tip = esc(tx("dict.speak", "发音"));
		var html = '<div class="jv-head"><b>' + esc(head) + "</b><span>" + esc(note) + "</span></div>"
			+ '<table class="jv-tab rv-tab">';
		var g = null;
		for (var i = 0; i < rows.length; i++) {
			if (rows[i].g !== g) {
				g = rows[i].g;
				html += '<tr><td class="kv-g" colspan="2">' + esc(g) + "</td></tr>";
			}
			html += '<tr><td class="jv-k">' + esc(rows[i].name) + '</td><td class="jv-v"><span>'
				+ esc(rows[i].text) + "</span>";
			if (rows[i].text && rows[i].text !== "—") {
				html += '<button type="button" class="jv-spk" data-v="' + esc(saytext(rows[i].text)) + '" title="' + tip + '">'
					+ '<i class="fa-solid fa-volume-high"></i></button>';
			}
			html += "</td></tr>";
		}
		html += "</table>";
		out.innerHTML = html;
	}

	function saytext(s){
		return String(s == null ? "" : s).replace(/·/g, ",").replace(/\s*\/\s*/g, ", ").replace(/\(те\)/g, "те").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
	}

	function esc(s){
		return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
	}
})();
