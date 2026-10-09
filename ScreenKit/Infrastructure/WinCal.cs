using Windows.Globalization;

namespace ScreenKit;

/// <summary>一种 Windows.Globalization.Calendar 的换算结果。</summary>
sealed class WinCalInfo {
	public string Id { get; set; }
	public string Era { get; set; }
	public int EraNum { get; set; }
	public string Year { get; set; }
	public int YearNum { get; set; }
	public string Month { get; set; }
	public int MonthNum { get; set; }
	public int MonthCount { get; set; }
	public string Day { get; set; }
	public int DayNum { get; set; }
	public string Week { get; set; }
	public string Ganzhi { get; set; }
	public bool LeapMonth { get; set; }
	public string Gregorian { get; set; }
}

/// <summary>公历换 Windows 历法。农历干支、闰月用 zh-CN 表判断。</summary>
static class WinCal {
	const string STEMS = "甲乙丙丁戊己庚辛壬癸";
	const string BRANCHES = "子丑寅卯辰巳午未申酉戌亥";

	public static (string Id, string Key)[] Systems() => new (string, string)[] {
		(CalendarIdentifiers.ChineseLunar, "wincal.sys.lunar"),
		(CalendarIdentifiers.Gregorian, "wincal.sys.greg"),
		(CalendarIdentifiers.Japanese, "wincal.sys.jp"),
		(CalendarIdentifiers.JapaneseLunar, "wincal.sys.jplunar"),
		(CalendarIdentifiers.Taiwan, "wincal.sys.tw"),
		(CalendarIdentifiers.Korean, "wincal.sys.ko"),
		(CalendarIdentifiers.VietnameseLunar, "wincal.sys.vilunar"),
		(CalendarIdentifiers.Hebrew, "wincal.sys.he"),
		(CalendarIdentifiers.Hijri, "wincal.sys.hijri"),
		(CalendarIdentifiers.UmAlQura, "wincal.sys.umalqura"),
		(CalendarIdentifiers.Persian, "wincal.sys.fa"),
		(CalendarIdentifiers.Thai, "wincal.sys.th"),
		(CalendarIdentifiers.Julian, "wincal.sys.julian"),
	};

	public static string LangTag(string ui) {
		var code = Loc.Normalize(ui ?? "");
		if (code == "zh") return "zh-CN";
		if (code == "en") return "en-US";
		if (code == "ja") return "ja-JP";
		if (code == "ko") return "ko-KR";
		return "en-US";
	}

	public static WinCalInfo Query(DateTime date, string calId, string langTag) {
		if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
			return query(date, calId, langTag);
		return sta(() => query(date, calId, langTag));
	}

	static T sta<T>(Func<T> fn) {
		T result = default;
		Exception err = null;
		var t = new Thread(() => {
			try { result = fn(); }
			catch (Exception ex) { err = ex; }
		});
		t.IsBackground = true;
		t.SetApartmentState(ApartmentState.STA);
		t.Start();
		t.Join();
		if (err != null) throw err;
		return result;
	}

	static WinCalInfo query(DateTime date, string calId, string langTag) {
		if (string.IsNullOrEmpty(calId)) calId = CalendarIdentifiers.ChineseLunar;
		if (string.IsNullOrEmpty(langTag)) langTag = "zh-CN";
		var noon = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Local);
		var when = new DateTimeOffset(noon);
		var info = read(when, calId, langTag);
		var zh = read(when, calId, "zh-CN");
		info.Ganzhi = pickganzhi(zh.Era, zh.Year);
		info.LeapMonth = isleapy(zh.Month);
		if (calId == CalendarIdentifiers.ChineseLunar)
			applylunar(info, date);
		info.Gregorian = date.ToString("yyyy-MM-dd");
		return info;
	}

	/// <summary>
	/// WinRT 农历的 Year 是 1–60 干支序，月份字符串只有数字。
	/// 闰月槽位与 ChineseLunisolarCalendar 相同（同为 Windows 历表）。
	/// </summary>
	static void applylunar(WinCalInfo info, DateTime date) {
		if (info.Ganzhi.Length == 0)
			info.Ganzhi = sexagenary(info.YearNum);
		var slot = 0;
		try {
			var bcl = new System.Globalization.ChineseLunisolarCalendar();
			var noon = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Unspecified);
			slot = bcl.GetLeapMonth(bcl.GetYear(noon), bcl.GetEra(noon));
		}
		catch { slot = 0; }
		if (slot != 0 && info.MonthNum == slot) info.LeapMonth = true;
		var named = lunarmonth(info.MonthNum, slot);
		if (named.Length > 0) info.Month = named;
		var day = lunarday(info.DayNum);
		if (day.Length > 0) info.Day = day;
	}

	static string sexagenary(int year) {
		if (year < 1 || year > 60) return "";
		var i = year - 1;
		return STEMS[i % 10].ToString() + BRANCHES[i % 12];
	}

	static readonly string[] LUNAR_MONTH = { "", "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊" };
	static readonly string[] DIG = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

	static string lunarmonth(int month, int leapSlot) {
		var leap = leapSlot != 0 && month == leapSlot;
		var nominal = month;
		if (leapSlot != 0 && month >= leapSlot)
			nominal = month == leapSlot ? leapSlot - 1 : month - 1;
		if (nominal < 1 || nominal > 12) return "";
		var name = LUNAR_MONTH[nominal] + "月";
		if (leap) name = "闰" + name;
		return name;
	}

	static string lunarday(int day) {
		if (day == 10) return "初十";
		if (day > 0 && day < 10) return "初" + DIG[day];
		if (day > 10 && day < 20) return "十" + DIG[day - 10];
		if (day == 20) return "二十";
		if (day > 20 && day < 30) return "廿" + DIG[day - 20];
		if (day == 30) return "三十";
		return "";
	}

	static WinCalInfo read(DateTimeOffset when, string calId, string langTag) {
		var cal = new Windows.Globalization.Calendar(new[] { langTag }, calId, ClockIdentifiers.TwentyFourHour);
		cal.SetDateTime(when);
		return new WinCalInfo {
			Id = calId,
			Era = text(() => cal.EraAsString()),
			EraNum = cal.Era,
			Year = text(() => cal.YearAsString()),
			YearNum = cal.Year,
			Month = text(() => cal.MonthAsString()),
			MonthNum = cal.Month,
			MonthCount = cal.NumberOfMonthsInThisYear,
			Day = text(() => cal.DayAsString()),
			DayNum = cal.Day,
			Week = weekday(cal, when),
		};
	}

	static string weekday(Windows.Globalization.Calendar cal, DateTimeOffset when) {
		var name = text(() => cal.DayOfWeekAsString());
		if (name.Length > 0) return name;
		try { return when.ToString("dddd"); }
		catch { return ""; }
	}

	static string text(Func<string> read) {
		try { return read() ?? ""; }
		catch { return ""; }
	}

	static string pickganzhi(string era, string year) {
		if (isganzhi(era)) return era;
		if (isganzhi(year)) return year;
		return "";
	}

	static bool isganzhi(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		var stem = false;
		var branch = false;
		foreach (var c in s) {
			if (STEMS.IndexOf(c) >= 0) stem = true;
			if (BRANCHES.IndexOf(c) >= 0) branch = true;
		}
		return stem && branch;
	}

	static bool isleapy(string month) {
		if (string.IsNullOrEmpty(month)) return false;
		return month.IndexOf('闰') >= 0 || month.IndexOf('閏') >= 0;
	}

	public static bool TryDate(string text, out DateTime date) {
		date = DateTime.Today;
		if (string.IsNullOrWhiteSpace(text)) return true;
		return DateTime.TryParseExact(text.Trim(),
			new[] { "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd" },
			System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.None, out date);
	}

	public static string FindId(string name) {
		var s = (name ?? "").Trim();
		if (s.Length == 0) return CalendarIdentifiers.ChineseLunar;
		foreach (var one in Systems()) {
			if (string.Equals(one.Id, s, StringComparison.OrdinalIgnoreCase)) return one.Id;
		}
		switch (s.ToLowerInvariant()) {
			case "lunar": case "chinese": case "zh": return CalendarIdentifiers.ChineseLunar;
			case "greg": case "gregorian": case "ad": return CalendarIdentifiers.Gregorian;
			case "jp": case "japanese": return CalendarIdentifiers.Japanese;
			case "jplunar": case "japanese-lunar": return CalendarIdentifiers.JapaneseLunar;
			case "tw": case "taiwan": case "minguo": return CalendarIdentifiers.Taiwan;
			case "ko": case "korean": return CalendarIdentifiers.Korean;
			case "vilunar": case "vietnamese": return CalendarIdentifiers.VietnameseLunar;
			case "he": case "hebrew": return CalendarIdentifiers.Hebrew;
			case "hijri": case "islamic": return CalendarIdentifiers.Hijri;
			case "umalqura": return CalendarIdentifiers.UmAlQura;
			case "fa": case "persian": return CalendarIdentifiers.Persian;
			case "th": case "thai": return CalendarIdentifiers.Thai;
			case "julian": return CalendarIdentifiers.Julian;
			default: return null;
		}
	}

	public static string Alias(string id) {
		if (id == CalendarIdentifiers.ChineseLunar) return "lunar";
		if (id == CalendarIdentifiers.Gregorian) return "gregorian";
		if (id == CalendarIdentifiers.Japanese) return "jp";
		if (id == CalendarIdentifiers.JapaneseLunar) return "jplunar";
		if (id == CalendarIdentifiers.Taiwan) return "tw";
		if (id == CalendarIdentifiers.Korean) return "ko";
		if (id == CalendarIdentifiers.VietnameseLunar) return "vilunar";
		if (id == CalendarIdentifiers.Hebrew) return "he";
		if (id == CalendarIdentifiers.Hijri) return "hijri";
		if (id == CalendarIdentifiers.UmAlQura) return "umalqura";
		if (id == CalendarIdentifiers.Persian) return "fa";
		if (id == CalendarIdentifiers.Thai) return "th";
		if (id == CalendarIdentifiers.Julian) return "julian";
		return id ?? "";
	}
}
