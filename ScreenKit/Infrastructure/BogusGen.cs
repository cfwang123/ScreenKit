using System.Globalization;
using System.Reflection;
using System.Text;

namespace ScreenKit;

/// <summary>一种可生成的分类。</summary>
sealed class FakeKind {
	public string Id { get; set; }
	public string Key { get; set; }
}

/// <summary>一种 Bogus 语言，以及这种语言自己有数据的分类。</summary>
sealed class FakeLang {
	public string Id { get; set; }
	public string Name { get; set; }
	public string[] Kinds { get; set; }
}

/// <summary>
/// 用程序目录里的 Bogus.dll 生成测试数据。程序不引用这个库，未安装时不加载。
/// 读入字节再加载，卸载时可以删掉文件。
/// </summary>
static class BogusGen {
	const string FIRST = "name.first_name|name.male_first_name+name.female_first_name";
	const string LAST = "name.last_name|name.male_last_name+name.female_last_name";
	const string FULL =
		"name.first_name+name.last_name|name.first_name+name.male_last_name+name.female_last_name|" +
		"name.male_first_name+name.female_first_name+name.last_name|" +
		"name.male_first_name+name.female_first_name+name.male_last_name+name.female_last_name";
	const string CARD =
		"finance.credit_card.visa+finance.credit_card.mastercard+finance.credit_card.discover+" +
		"finance.credit_card.american_express+finance.credit_card.diners_club+finance.credit_card.jcb+" +
		"finance.credit_card.switch+finance.credit_card.solo+finance.credit_card.maestro+" +
		"finance.credit_card.laser+finance.credit_card.instapayment";

	static readonly object gate = new();
	static readonly Dictionary<string, object> fakers = new();
	static Assembly asm;
	static Type fakerType;
	static MethodInfo haskey;
	static string[] locales = [];
	static FakeLang[] langs;
	static string namedlang = "";

	sealed class Item {
		public string Id;
		public string Key;
		public string Need;
		public string Need2;
		public string Set;
		public string Method;
		public string Mode;
	}

	static readonly Item[] items = [
		new() { Id = "name", Key = "web.fake.fullname", Need = FULL, Set = "Name", Method = "FullName", Mode = "zhname" },
		new() { Id = "first", Key = "web.fake.first", Need = FIRST, Set = "Name", Method = "FirstName" },
		new() { Id = "last", Key = "web.fake.last", Need = LAST, Set = "Name", Method = "LastName" },
		new() { Id = "prefix", Key = "web.fake.prefix", Need = "name.prefix|name.male_prefix+name.female_prefix", Set = "Name", Method = "Prefix" },
		new() { Id = "suffix", Key = "web.fake.suffix", Need = "name.suffix", Set = "Name", Method = "Suffix" },
		new() { Id = "job", Key = "web.fake.job", Need = "name.title.descriptor+name.title.level+name.title.job", Set = "Name", Method = "JobTitle" },
		new() { Id = "jobdesc", Key = "web.fake.jobdesc", Need = "name.title.descriptor", Set = "Name", Method = "JobDescriptor" },
		new() { Id = "jobarea", Key = "web.fake.jobarea", Need = "name.title.level", Set = "Name", Method = "JobArea" },
		new() { Id = "jobtype", Key = "web.fake.jobtype", Need = "name.title.job", Set = "Name", Method = "JobType" },
		new() { Id = "phone", Key = "web.fake.tel", Need = "phone_number.formats", Set = "Phone", Method = "PhoneNumber" },
		new() { Id = "id", Key = "web.fake.id", Mode = "id" },
		new() { Id = "zip", Key = "web.fake.zip", Need = "address.postcode", Set = "Address", Method = "ZipCode" },
		new() { Id = "city", Key = "web.fake.city", Need = "address.city", Set = "Address", Method = "City" },
		new() { Id = "street", Key = "web.fake.street", Need = "address.street_address", Set = "Address", Method = "StreetAddress" },
		new() { Id = "citypre", Key = "web.fake.citypre", Need = "address.city_prefix", Set = "Address", Method = "CityPrefix" },
		new() { Id = "citysuf", Key = "web.fake.citysuf", Need = "address.city_suffix", Set = "Address", Method = "CitySuffix" },
		new() { Id = "streetname", Key = "web.fake.streetname", Need = "address.street_name", Set = "Address", Method = "StreetName" },
		new() { Id = "building", Key = "web.fake.building", Need = "address.building_number", Set = "Address", Method = "BuildingNumber" },
		new() { Id = "streetsuf", Key = "web.fake.streetsuf", Need = "address.street_suffix", Set = "Address", Method = "StreetSuffix" },
		new() { Id = "secondary", Key = "web.fake.secondary", Need = "address.secondary_address", Set = "Address", Method = "SecondaryAddress" },
		new() { Id = "county", Key = "web.fake.county", Need = "address.county", Set = "Address", Method = "County" },
		new() { Id = "country", Key = "web.fake.country", Need = "address.country", Set = "Address", Method = "Country" },
		new() { Id = "addr", Key = "web.fake.addr", Need = "address.street_address+address.city+address.country", Set = "Address", Method = "FullAddress" },
		new() { Id = "cc", Key = "web.fake.cc", Need = "address.country_code", Set = "Address", Method = "CountryCode" },
		new() { Id = "state", Key = "web.fake.state", Need = "address.state", Set = "Address", Method = "State" },
		new() { Id = "stateabbr", Key = "web.fake.stateabbr", Need = "address.state_abbr", Set = "Address", Method = "StateAbbr" },
		new() { Id = "direction", Key = "web.fake.direction", Need = "address.direction", Set = "Address", Method = "Direction" },
		new() { Id = "corpsuf", Key = "web.fake.corpsuf", Need = "company.suffix", Set = "Company", Method = "CompanySuffix" },
		new() { Id = "corp", Key = "web.fake.corp", Need = "company.suffix", Need2 = LAST, Set = "Company", Method = "CompanyName" },
		new() { Id = "catch", Key = "web.fake.catch", Need = "company.adjective+company.descriptor+company.noun", Set = "Company", Method = "CatchPhrase" },
		new() { Id = "bs", Key = "web.fake.bs", Need = "company.bs_adjective+company.bs_verb+company.bs_noun", Set = "Company", Method = "Bs" },
		new() { Id = "mail", Key = "web.fake.mail", Need = "internet.free_email", Need2 = FULL, Set = "Internet", Method = "Email" },
		new() { Id = "user", Key = "web.fake.user", Need = FULL, Set = "Internet", Method = "UserName", Mode = "user" },
		new() { Id = "domain", Key = "web.fake.domain", Need = "internet.domain_suffix", Need2 = FIRST, Set = "Internet", Method = "DomainName" },
		new() { Id = "domainsuf", Key = "web.fake.domainsuf", Need = "internet.domain_suffix", Set = "Internet", Method = "DomainSuffix" },
		new() { Id = "url", Key = "web.fake.url", Need = "internet.domain_suffix", Need2 = FIRST, Set = "Internet", Method = "Url" },
		new() { Id = "dept", Key = "web.fake.dept", Need = "commerce.department", Set = "Commerce", Method = "Department" },
		new() { Id = "color", Key = "web.fake.colorname", Need = "commerce.color", Set = "Commerce", Method = "Color" },
		new() { Id = "product", Key = "web.fake.product", Need = "commerce.product_name.adjective+commerce.product_name.material+commerce.product_name.product", Set = "Commerce", Method = "ProductName" },
		new() { Id = "prodadj", Key = "web.fake.prodadj", Need = "commerce.product_name.adjective", Set = "Commerce", Method = "ProductAdjective" },
		new() { Id = "prodmat", Key = "web.fake.prodmat", Need = "commerce.product_name.material", Set = "Commerce", Method = "ProductMaterial" },
		new() { Id = "proddesc", Key = "web.fake.proddesc", Need = "commerce.product_description", Set = "Commerce", Method = "ProductDescription" },
		new() { Id = "word", Key = "web.fake.word", Need = "lorem.words", Set = "Lorem", Method = "Word" },
		new() { Id = "sentence", Key = "web.fake.sentence", Need = "lorem.words", Set = "Lorem", Method = "Sentence" },
		new() { Id = "paragraph", Key = "web.fake.paragraph", Need = "lorem.words", Set = "Lorem", Method = "Paragraph" },
		new() { Id = "acctname", Key = "web.fake.acctname", Need = "finance.account_type", Set = "Finance", Method = "AccountName" },
		new() { Id = "txtype", Key = "web.fake.txtype", Need = "finance.transaction_type", Set = "Finance", Method = "TransactionType" },
		new() { Id = "currency", Key = "web.fake.currency", Need = "finance.currency", Set = "Finance", Method = "Currency", Mode = "currency" },
		new() { Id = "card", Key = "web.fake.card", Need = CARD, Set = "Finance", Method = "CreditCardNumber" },
		new() { Id = "iban", Key = "web.fake.iban", Need = "finance.iban_formats", Set = "Finance", Method = "Iban" },
		new() { Id = "hackabbr", Key = "web.fake.hackabbr", Need = "hacker.abbreviation", Set = "Hacker", Method = "Abbreviation" },
		new() { Id = "hackadj", Key = "web.fake.hackadj", Need = "hacker.adjective", Set = "Hacker", Method = "Adjective" },
		new() { Id = "hacknoun", Key = "web.fake.hacknoun", Need = "hacker.noun", Set = "Hacker", Method = "Noun" },
		new() { Id = "hackverb", Key = "web.fake.hackverb", Need = "hacker.verb", Set = "Hacker", Method = "Verb" },
		new() { Id = "hacking", Key = "web.fake.hacking", Need = "hacker.ingverb", Set = "Hacker", Method = "IngVerb" },
		new() { Id = "hackphrase", Key = "web.fake.hackphrase", Need = "hacker.phrase", Set = "Hacker", Method = "Phrase" },
		new() { Id = "maker", Key = "web.fake.maker", Need = "vehicle.manufacturer", Set = "Vehicle", Method = "Manufacturer" },
		new() { Id = "model", Key = "web.fake.model", Need = "vehicle.model", Set = "Vehicle", Method = "Model" },
		new() { Id = "vtype", Key = "web.fake.vtype", Need = "vehicle.type", Set = "Vehicle", Method = "Type" },
		new() { Id = "fuel", Key = "web.fake.fuel", Need = "vehicle.fuel", Set = "Vehicle", Method = "Fuel" },
		new() { Id = "filename", Key = "web.fake.filename", Need = "system.mimeTypes", Set = "System", Method = "FileName" },
		new() { Id = "dir", Key = "web.fake.dir", Need = "system.directoryPaths", Set = "System", Method = "DirectoryPath" },
		new() { Id = "filepath", Key = "web.fake.filepath", Need = "system.directoryPaths", Need2 = "system.mimeTypes", Set = "System", Method = "FilePath" },
		new() { Id = "mime", Key = "web.fake.mime", Need = "system.mimeTypes", Set = "System", Method = "MimeType" },
		new() { Id = "filetype", Key = "web.fake.filetype", Need = "system.mimeTypes", Set = "System", Method = "FileType" },
		new() { Id = "fileext", Key = "web.fake.fileext", Need = "system.mimeTypes", Set = "System", Method = "FileExt" },
		new() { Id = "column", Key = "web.fake.column", Need = "database.column", Set = "Database", Method = "Column" },
		new() { Id = "dbtype", Key = "web.fake.dbtype", Need = "database.type", Set = "Database", Method = "Type" },
		new() { Id = "collation", Key = "web.fake.collation", Need = "database.collation", Set = "Database", Method = "Collation" },
		new() { Id = "engine", Key = "web.fake.engine", Need = "database.engine", Set = "Database", Method = "Engine" },
		new() { Id = "month", Key = "web.fake.month", Need = "date.month.wide", Set = "Date", Method = "Month" },
		new() { Id = "weekday", Key = "web.fake.weekday", Need = "date.weekday.wide", Set = "Date", Method = "Weekday" },
		new() { Id = "timezone", Key = "web.fake.timezone", Need = "address.time_zone", Set = "Date", Method = "TimeZoneString" },
		new() { Id = "genre", Key = "web.fake.genre", Need = "music.genre", Set = "Music", Method = "Genre" },
		new() { Id = "review", Key = "web.fake.review", Need = "rant.review", Set = "Rant", Method = "Review" },
		new() { Id = "date", Key = "web.fake.date", Set = "Date", Method = "Past", Mode = "date" },
		new() { Id = "ip", Key = "web.fake.ip", Set = "Internet", Method = "Ip" },
		new() { Id = "ipv6", Key = "web.fake.ipv6", Set = "Internet", Method = "Ipv6" },
		new() { Id = "mac", Key = "web.fake.mac", Set = "Internet", Method = "Mac", Mode = "upper" },
		new() { Id = "port", Key = "web.fake.port", Set = "Internet", Method = "Port" },
		new() { Id = "password", Key = "web.fake.password", Set = "Internet", Method = "Password" },
		new() { Id = "protocol", Key = "web.fake.protocol", Set = "Internet", Method = "Protocol" },
		new() { Id = "hex", Key = "web.fake.hex", Set = "Internet", Method = "Color" },
		new() { Id = "uuid", Key = "web.fake.uuid", Mode = "uuid" },
		new() { Id = "ean8", Key = "web.fake.ean8", Set = "Commerce", Method = "Ean8" },
		new() { Id = "ean13", Key = "web.fake.ean13", Set = "Commerce", Method = "Ean13" },
		new() { Id = "price", Key = "web.fake.price", Set = "Commerce", Method = "Price" },
		new() { Id = "account", Key = "web.fake.account", Set = "Finance", Method = "Account" },
		new() { Id = "amount", Key = "web.fake.amount", Set = "Finance", Method = "Amount" },
		new() { Id = "cvv", Key = "web.fake.cvv", Set = "Finance", Method = "CreditCardCvv" },
		new() { Id = "bitcoin", Key = "web.fake.bitcoin", Set = "Finance", Method = "BitcoinAddress" },
		new() { Id = "ethereum", Key = "web.fake.ethereum", Set = "Finance", Method = "EthereumAddress" },
		new() { Id = "litecoin", Key = "web.fake.litecoin", Set = "Finance", Method = "LitecoinAddress" },
		new() { Id = "lat", Key = "web.fake.lat", Set = "Address", Method = "Latitude" },
		new() { Id = "lng", Key = "web.fake.lng", Set = "Address", Method = "Longitude" },
		new() { Id = "vin", Key = "web.fake.vin", Set = "Vehicle", Method = "Vin" },
		new() { Id = "semver", Key = "web.fake.semver", Set = "System", Method = "Semver" },
		new() { Id = "android", Key = "web.fake.android", Set = "System", Method = "AndroidId" },
		new() { Id = "apple", Key = "web.fake.apple", Set = "System", Method = "ApplePushToken" },
		new() { Id = "ua", Key = "web.fake.ua", Set = "Internet", Method = "UserAgent" },
		new() { Id = "bic", Key = "web.fake.bic", Set = "Finance", Method = "Bic" },
	];

	public static bool Installed => NativeRuntime.HasBogus();

	public static FakeKind[] KindMeta() {
		var list = new FakeKind[items.Length];
		for (var i = 0; i < items.Length; i++)
			list[i] = new FakeKind { Id = items[i].Id, Key = items[i].Key };
		return list;
	}

	public static FakeLang[] Languages() {
		if (!Installed) return [];
		lock (gate) {
			ensure();
			return langs;
		}
	}

	public static string Lines(string kind, int n) => Lines("", kind, n);

	public static string Lines(string locale, string kind, int n) {
		if (n < 1 || n > 50) throw new ArgumentException("行数要在 1 到 50");
		if (!Installed) throw new InvalidOperationException("Bogus 未安装");
		lock (gate) {
			ensure();
			var raw = kind ?? "";
			kind = mapkind(raw);
			locale = maplocale(locale, raw);
			var item = find(kind);
			if (item == null || !show(locale, item)) throw new ArgumentException("请选择分类");
			var sb = new StringBuilder();
			for (var i = 0; i < n; i++) {
				if (i > 0) sb.Append('\n');
				sb.Append(one(locale, item));
			}
			return sb.ToString();
		}
	}

	static void ensure() {
		if (asm == null) {
			var path = Path.Combine(NativeRuntime.BaseDir, "Bogus.dll");
			asm = Assembly.Load(File.ReadAllBytes(path));
			var db = asm.GetType("Bogus.Database");
			if (db == null) throw new InvalidOperationException("Bogus.dll 里没有 Bogus.Database");
			fakerType = asm.GetType("Bogus.Faker");
			if (fakerType == null) throw new InvalidOperationException("Bogus.dll 里没有 Bogus.Faker");
			haskey = db.GetMethod("HasKey");
			var getall = db.GetMethod("GetAllLocales");
			locales = (string[])getall.Invoke(null, null) ?? [];
			Array.Sort(locales, StringComparer.Ordinal);
		}
		var ui = Loc.Lang ?? "";
		if (langs != null && namedlang == ui) return;
		var order = locales.OrderBy(langrank).ThenBy(localename, StringComparer.CurrentCulture).ToArray();
		var built = new List<FakeLang>();
		foreach (var id in order) {
			var ks = new List<string>();
			foreach (var it in items)
				if (show(id, it)) ks.Add(it.Id);
			if (ks.Count == 0) continue;
			built.Add(new FakeLang { Id = id, Name = localename(id), Kinds = ks.ToArray() });
		}
		langs = built.ToArray();
		namedlang = ui;
	}

	static int langrank(string id) {
		if (id == "zh_CN") return 0;
		if (id == "zh_TW") return 1;
		if (id == "en") return 2;
		if (id == "en_US") return 3;
		if (id == "ja") return 4;
		if (id == "ko") return 5;
		if (id == "vi") return 6;
		if (id == "fr") return 7;
		if (id == "de") return 8;
		if (id == "es") return 9;
		var primary = id.Split('_')[0];
		string[] prefer = ["zh", "en", "ja", "ko", "vi", "fr", "de", "es"];
		var p = Array.IndexOf(prefer, primary);
		if (p >= 0) return 20 + p;
		return 100;
	}

	static string localename(string id) {
		string name;
		if (id == "en_BORK") name = Loc.IsZh ? "英语（Bork）" : "English (Bork)";
		else if (id == "en_AU_ocker") name = Loc.IsZh ? "澳大利亚英语（口语）" : "Australian Ocker";
		else if (id == "en_IND") name = Loc.IsZh ? "印度英语" : "Indian English";
		else {
			var code = id == "cz" ? "cs" : id.Replace('_', '-');
			name = culturename(code);
			if (name.Length == 0 && code.Contains("-")) name = culturename(code.Split('-')[0]);
		}
		if (name.Length == 0) return id;
		return name + " (" + id + ")";
	}

	static string culturename(string code) {
		CultureInfo culture;
		try { culture = CultureInfo.GetCultureInfo(code); }
		catch (CultureNotFoundException) { return ""; }
		var name = Loc.IsZh ? culture.DisplayName : culture.EnglishName;
		if (string.IsNullOrWhiteSpace(name)) return "";
		name = name.Trim();
		if (name.StartsWith("未知", StringComparison.Ordinal)) return "";
		if (name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)) return "";
		if (string.Equals(name, code, StringComparison.OrdinalIgnoreCase)) return "";
		return name;
	}

	static string mapkind(string kind) => kind switch {
		"enname" => "name",
		"cnnick" => "first",
		"ennick" => "user",
		_ => kind ?? "",
	};

	static string maplocale(string locale, string raw) {
		if (!string.IsNullOrEmpty(locale)) {
			if (Array.BinarySearch(locales, locale, StringComparer.Ordinal) < 0)
				throw new ArgumentException("请选择语言");
			return locale;
		}
		if (raw is "enname" or "ennick") {
			if (haslocale("en")) return "en";
		}
		if (haslocale("zh_CN")) return "zh_CN";
		if (haslocale("en")) return "en";
		if (locales.Length == 0) throw new InvalidOperationException("Bogus 没有语言数据");
		return locales[0];
	}

	static bool haslocale(string id) => Array.BinarySearch(locales, id, StringComparer.Ordinal) >= 0;

	static Item find(string id) {
		foreach (var it in items)
			if (it.Id == id) return it;
		return null;
	}

	static bool show(string locale, Item it) {
		if (it.Mode == "id") return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
		if (!has(locale, it.Need)) return false;
		if (!has(locale, it.Need2)) return false;
		return true;
	}

	static bool has(string locale, string spec) {
		if (string.IsNullOrEmpty(spec)) return true;
		foreach (var or in spec.Split('|')) {
			var ok = true;
			foreach (var atom in or.Split('+')) {
				var dot = atom.IndexOf('.');
				if (dot <= 0) { ok = false; break; }
				var cat = atom.Substring(0, dot);
				var path = atom.Substring(dot + 1);
				var value = haskey.Invoke(null, [cat, path, locale, null]);
				if (value is not bool yes || !yes) { ok = false; break; }
			}
			if (ok) return true;
		}
		return false;
	}

	static object fakerof(string locale) {
		if (fakers.TryGetValue(locale, out var got)) return got;
		var faker = Activator.CreateInstance(fakerType, locale);
		// 这四个数据集的构造函数不接收 locale，默认是 en。
		foreach (var name in new[] { "Database", "Rant", "Vehicle", "Music" }) {
			var set = prop(faker, name);
			set.GetType().GetProperty("Locale")?.SetValue(set, locale);
		}
		fakers[locale] = faker;
		return faker;
	}

	static string one(string locale, Item it) {
		var faker = fakerof(locale);
		switch (it.Mode) {
		case "zhname": return zhname(faker, locale);
		case "user": return username(faker, locale);
		case "date": return date(faker);
		case "uuid": return uuid(faker);
		case "id": return idno(faker);
		case "upper": return callstr(prop(faker, it.Set), it.Method).ToUpperInvariant();
		case "currency": return currency(faker);
		default: return text(call(prop(faker, it.Set), it.Method));
		}
	}

	/// <summary>中文、日语、韩语姓名是姓在前，中间不空格。其它语言用 Bogus 的 FullName。</summary>
	static string zhname(object faker, string locale) {
		var name = prop(faker, "Name");
		if (familyfirst(locale))
			return $"{callstr(name, "LastName")}{callstr(name, "FirstName")}";
		return callstr(name, "FullName");
	}

	static bool familyfirst(string locale) {
		if (string.IsNullOrEmpty(locale)) return false;
		return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
			|| locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
			|| locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Bogus 的 UserName 会把非拉丁字母抹掉，中文姓名只剩分隔符和数字。
	/// 这种结果改成姓+名，有时再加数字。
	/// </summary>
	static string username(object faker, string locale) {
		var raw = callstr(prop(faker, "Internet"), "UserName");
		if (hasletter(raw)) return raw;
		var name = prop(faker, "Name");
		var last = callstr(name, "LastName");
		var first = callstr(name, "FirstName");
		var full = (familyfirst(locale) ? last + first : first + last).Replace(" ", "");
		if (!hasletter(full)) return raw.Length > 0 ? raw : full;
		var pick = num(faker, 0, 2);
		if (pick == 0) return full + num(faker, 0, 99).ToString(CultureInfo.InvariantCulture);
		if (pick == 1) return full;
		return full + "_" + num(faker, 0, 99).ToString(CultureInfo.InvariantCulture);
	}

	static bool hasletter(string s) {
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s)
			if (char.IsLetter(c)) return true;
		return false;
	}

	static string date(object faker) {
		var value = call(prop(faker, "Date"), "Past");
		if (value is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		return text(value);
	}

	static string uuid(object faker) {
		var random = prop(faker, "Random");
		foreach (var name in new[] { "Uuid", "Guid" }) {
			var method = random.GetType().GetMethod(name, Type.EmptyTypes);
			if (method == null) continue;
			return text(method.Invoke(random, null));
		}
		return Guid.NewGuid().ToString();
	}

	static string currency(object faker) {
		var value = call(prop(faker, "Finance"), "Currency");
		var code = text(prop(value, "Code"));
		var desc = text(prop(value, "Description"));
		if (desc.Length == 0) return code;
		return code + " " + desc;
	}

	static string idno(object faker) {
		var year = num(faker, 1975, 2004);
		var month = num(faker, 1, 12);
		var day = num(faker, 1, 28);
		var seq = num(faker, 0, 999);
		var body = "110101"
			+ year.ToString(CultureInfo.InvariantCulture)
			+ month.ToString("00", CultureInfo.InvariantCulture)
			+ day.ToString("00", CultureInfo.InvariantCulture)
			+ seq.ToString("000", CultureInfo.InvariantCulture);
		int[] w = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
		const string map = "10X98765432";
		var sum = 0;
		for (var i = 0; i < 17; i++) sum += (body[i] - '0') * w[i];
		return body + map[sum % 11];
	}

	static int num(object faker, int min, int max) {
		var value = call(prop(faker, "Random"), "Number", min, max);
		return Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	static object prop(object target, string name) {
		var info = target.GetType().GetProperty(name);
		if (info == null) throw new MissingMemberException(target.GetType().FullName, name);
		return info.GetValue(target);
	}

	static string callstr(object target, string name, params object[] args) => text(call(target, name, args));

	static string text(object value) {
		if (value == null) return "";
		if (value is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
	}

	static object call(object target, string name, params object[] args) {
		args ??= [];
		MethodInfo best = null;
		var bestExtra = int.MaxValue;
		foreach (var method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)) {
			if (method.Name != name) continue;
			var ps = method.GetParameters();
			if (ps.Length < args.Length) continue;
			var ok = true;
			for (var i = 0; i < args.Length; i++) {
				if (args[i] == null) {
					if (ps[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(ps[i].ParameterType) == null) {
						ok = false;
						break;
					}
					continue;
				}
				if (!ps[i].ParameterType.IsInstanceOfType(args[i])) {
					ok = false;
					break;
				}
			}
			if (!ok) continue;
			for (var i = args.Length; i < ps.Length; i++) {
				if (!ps[i].IsOptional && !ps[i].HasDefaultValue) {
					ok = false;
					break;
				}
			}
			if (!ok) continue;
			var extra = ps.Length - args.Length;
			if (extra < bestExtra) {
				best = method;
				bestExtra = extra;
			}
		}
		if (best == null) throw new MissingMethodException(target.GetType().FullName, name);
		var parms = best.GetParameters();
		var callArgs = new object[parms.Length];
		for (var i = 0; i < parms.Length; i++) {
			if (i < args.Length) {
				callArgs[i] = args[i];
				continue;
			}
			var def = parms[i].HasDefaultValue ? parms[i].DefaultValue : null;
			if (def == DBNull.Value) def = null;
			callArgs[i] = def;
		}
		try { return best.Invoke(target, callArgs); }
		catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
	}
}
