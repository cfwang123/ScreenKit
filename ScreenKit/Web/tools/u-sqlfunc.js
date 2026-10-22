(function(){
	var CATS = {
		str: "字符串",
		num: "数值与数学",
		date: "日期与时间",
		conv: "类型转换",
		aggr: "聚合与分组",
		win: "窗口函数",
		ctrl: "条件与流程",
		json: "JSON",
		meta: "元数据与系统",
		misc: "其他"
	};

	var COLS = [
		{ name: "函数 / 写法", mono: true },
		{ name: "说明" },
		{ name: "示例", mono: true }
	];

	var WIDTHS = ["260px", "250px", ""];

	var GROUPS = [
		{ id: "str", name: "字符串" },
		{ id: "num", name: "数值与数学" },
		{ id: "date", name: "日期与时间" },
		{ id: "conv", name: "类型转换" },
		{ id: "aggr", name: "聚合与分组" },
		{ id: "win", name: "窗口函数" },
		{ id: "ctrl", name: "条件与流程" },
		{ id: "json", name: "JSON" },
		{ id: "meta", name: "元数据与系统" },
		{ id: "misc", name: "其他" }
	];

	// [分组, 函数/写法, 说明, 示例, 备注]
	function R(a){
		var g = a[0];
		var note = a[4] || "—";
		return {
			g: g,
			c: [a[1], a[2], a[3]],
			x: (CATS[g] || "") + " " + a[2] + " " + a[3] + " " + note,
			d: [["类别", CATS[g] || ""], ["示例", a[3]], ["备注", note]]
		};
	}

	// 各库写法一致的部分，避免重复抄。
	var AGG = [
		["aggr", "COUNT(*)", "统计行数；COUNT(col) 不计 NULL", "SELECT COUNT(*) FROM orders"],
		["aggr", "COUNT(DISTINCT col)", "去重计数", "SELECT COUNT(DISTINCT user_id) FROM orders"],
		["aggr", "SUM(col)", "求和，NULL 不参与", "SELECT SUM(amount) FROM orders"],
		["aggr", "AVG(col)", "平均值", "SELECT AVG(amount) FROM orders"],
		["aggr", "MIN(col) / MAX(col)", "最小值和最大值", "SELECT MIN(created_at), MAX(created_at) FROM orders"],
		["aggr", "GROUP BY", "按列分组后聚合", "SELECT status, COUNT(*) FROM orders GROUP BY status"],
		["aggr", "HAVING", "对分组结果再过滤", "SELECT status, COUNT(*) c FROM orders GROUP BY status HAVING c > 10"]
	];

	var WIN = [
		["win", "ROW_NUMBER() OVER", "按顺序编号，不并列", "ROW_NUMBER() OVER (PARTITION BY user_id ORDER BY created_at)"],
		["win", "RANK() / DENSE_RANK()", "并列名次；DENSE_RANK 不跳号", "RANK() OVER (ORDER BY score DESC)"],
		["win", "NTILE(n)", "把结果分成 n 个桶", "NTILE(4) OVER (ORDER BY score DESC)"],
		["win", "LAG() / LEAD()", "取前一行 / 后一行的值", "LAG(amount) OVER (ORDER BY created_at)"],
		["win", "SUM() OVER", "累计和或分组小计", "SUM(amount) OVER (PARTITION BY user_id ORDER BY created_at)"],
		["win", "FIRST_VALUE() / LAST_VALUE()", "取分组内首尾值", "FIRST_VALUE(amount) OVER (PARTITION BY user_id ORDER BY created_at)"]
	];

	var CASE = [
		["ctrl", "CASE WHEN", "标准条件表达式，各库通用", "CASE WHEN amount > 100 THEN '大' ELSE '小' END"]
	];

	var STR_MY = [
		["str", "CONCAT(a, b)", "拼接字符串，任一参数为 NULL 结果为 NULL", "CONCAT(first_name, ' ', last_name)"],
		["str", "CONCAT_WS(sep, …)", "带分隔符拼接，自动跳过 NULL", "CONCAT_WS('-', city, district)"],
		["str", "LENGTH(s) / CHAR_LENGTH(s)", "字节数和字符数", "CHAR_LENGTH(name)"],
		["str", "SUBSTRING(s, pos, len)", "截取；pos 从 1 开始", "SUBSTRING(code, 1, 4)"],
		["str", "SUBSTRING_INDEX(s, sep, n)", "按分隔符取第 n 段", "SUBSTRING_INDEX(email, '@', -1)"],
		["str", "REPLACE(s, from, to)", "替换全部匹配", "REPLACE(phone, '-', '')"],
		["str", "UPPER(s) / LOWER(s)", "转大小写", "LOWER(email)"],
		["str", "TRIM(s) / LTRIM / RTRIM", "去两端或单侧空格", "TRIM(name)"],
		["str", "LOCATE(sub, s) / INSTR(s, sub)", "子串位置；找不到为 0", "LOCATE('@', email)"],
		["str", "LEFT(s, n) / RIGHT(s, n)", "取左/右 n 个字符", "LEFT(code, 2)"],
		["str", "LPAD(s, n, pad) / RPAD", "左/右补齐到 n 位", "LPAD(id, 8, '0')"],
		["str", "REGEXP_REPLACE(s, pat, rep)", "正则替换（8.0+）", "REGEXP_REPLACE(phone, '[^0-9]', '')"],
		["str", "REGEXP_SUBSTR(s, pat)", "正则提取（8.0+）", "REGEXP_SUBSTR(text, '[0-9]+')"]
	];

	var NUM_MY = [
		["num", "ROUND(x, d)", "四舍五入到 d 位小数", "ROUND(amount, 2)"],
		["num", "CEIL(x) / FLOOR(x)", "向上 / 向下取整", "CEIL(price)"],
		["num", "TRUNCATE(x, d)", "截断小数，不四舍五入", "TRUNCATE(amount, 2)"],
		["num", "ABS(x)", "绝对值", "ABS(diff)"],
		["num", "MOD(a, b) 或 a % b", "取余", "MOD(id, 2)"],
		["num", "POW(a, b) / POWER", "乘方", "POW(2, 10)"],
		["num", "SQRT(x)", "平方根", "SQRT(area)"],
		["num", "RAND()", "0~1 随机数", "ORDER BY RAND() LIMIT 1"],
		["num", "SIGN(x)", "符号：-1、0、1", "SIGN(diff)"],
		["num", "GREATEST(…) / LEAST(…)", "多个值里的最大 / 最小", "GREATEST(a, b, 0)"],
		["num", "FORMAT(x, d)", "千分位加 d 位小数，返回字符串", "FORMAT(amount, 2)"]
	];

	var DATE_MY = [
		["date", "NOW()", "当前日期时间", "SELECT NOW()"],
		["date", "CURDATE() / CURTIME()", "当前日期 / 时间", "CURDATE()"],
		["date", "DATE_FORMAT(d, fmt)", "格式化日期，%Y-%m-%d %H:%i:%s", "DATE_FORMAT(NOW(), '%Y-%m-%d')"],
		["date", "STR_TO_DATE(s, fmt)", "字符串转日期", "STR_TO_DATE('2026-01-02', '%Y-%m-%d')"],
		["date", "DATE_ADD(d, INTERVAL n unit)", "日期加；DATE_SUB 减", "DATE_ADD(NOW(), INTERVAL 7 DAY)"],
		["date", "DATEDIFF(a, b)", "相差天数", "DATEDIFF(NOW(), created_at)"],
		["date", "TIMESTAMPDIFF(unit, a, b)", "按单位求差，如 MINUTE、HOUR", "TIMESTAMPDIFF(HOUR, start_at, end_at)"],
		["date", "YEAR(d) / MONTH(d) / DAY(d)", "取年、月、日", "YEAR(created_at)"],
		["date", "LAST_DAY(d)", "当月最后一天", "LAST_DAY(NOW())"],
		["date", "UNIX_TIMESTAMP(d)", "转 Unix 时间戳", "UNIX_TIMESTAMP(created_at)"],
		["date", "FROM_UNIXTIME(ts)", "时间戳转日期时间", "FROM_UNIXTIME(1700000000)"],
		["date", "EXTRACT(unit FROM d)", "取日期的一部分", "EXTRACT(YEAR_MONTH FROM created_at)"],
		["date", "DAYOFWEEK(d) / WEEK(d)", "星期几（1=周日）、第几周", "DAYOFWEEK(created_at)"]
	];

	var CONV_MY = [
		["conv", "CAST(x AS type)", "转换类型：CHAR、SIGNED、DECIMAL(10,2)、DATETIME", "CAST(price AS DECIMAL(10,2))"],
		["conv", "CONVERT(x, CHAR)", "类型转换的另一种写法", "CONVERT(created_at, CHAR)"],
		["conv", "BINARY s", "按字节比较，区分大小写", "WHERE BINARY name = 'Alice'"],
		["conv", "JSON_UNQUOTE(j)", "去掉 JSON 字符串的引号", "JSON_UNQUOTE(JSON_EXTRACT(doc, '$.name'))"],
		["conv", "CONVERT_TZ(d, from, to)", "转换时区", "CONVERT_TZ(NOW(), '+00:00', '+08:00')"]
	];

	var AGGR_MY = [
		["aggr", "GROUP_CONCAT(col SEPARATOR ',')", "把分组内的值拼成一列", "SELECT GROUP_CONCAT(name) FROM users GROUP BY dept"],
		["aggr", "STDDEV(col) / VARIANCE(col)", "标准差 / 方差", "STDDEV(amount)"],
		["aggr", "GROUP BY … WITH ROLLUP", "加合计行", "SELECT dept, SUM(amount) FROM t GROUP BY dept WITH ROLLUP"]
	];

	var CTRL_MY = [
		["ctrl", "IF(cond, a, b)", "三元判断", "IF(amount > 0, '入', '出')"],
		["ctrl", "IFNULL(x, fallback)", "x 为 NULL 时取备用值", "IFNULL(nickname, name)"],
		["ctrl", "NULLIF(a, b)", "a = b 时返回 NULL", "NULLIF(count, 0)"],
		["ctrl", "COALESCE(a, b, …)", "取第一个非 NULL", "COALESCE(phone, mobile, email)"]
	];

	var JSON_MY = [
		["json", "JSON_EXTRACT(doc, '$.a')", "取 JSON 字段", "JSON_EXTRACT(doc, '$.user.name')"],
		["json", "doc -> '$.a'", "取 JSON 值（保留引号）", "doc->'$.user.name'"],
		["json", "doc ->> '$.a'", "取 JSON 值并去掉引号", "doc->>'$.user.name'"],
		["json", "JSON_OBJECT(k, v, …)", "构造 JSON 对象", "JSON_OBJECT('id', id, 'name', name)"],
		["json", "JSON_ARRAY(v, …)", "构造 JSON 数组", "JSON_ARRAY(1, 2, 3)"],
		["json", "JSON_ARRAYAGG(col)", "把行聚合成 JSON 数组", "SELECT JSON_ARRAYAGG(name) FROM users"],
		["json", "JSON_OBJECTAGG(k, v)", "把行聚合成 JSON 对象", "SELECT JSON_OBJECTAGG(id, name) FROM users"],
		["json", "JSON_CONTAINS(doc, val, path)", "判断是否包含某个值", "JSON_CONTAINS(doc, '\"admin\"', '$.roles')"],
		["json", "JSON_SET(doc, path, val)", "修改 JSON 字段", "JSON_SET(doc, '$.vip', true)"],
		["json", "JSON_VALID(s)", "判断是否为合法 JSON", "JSON_VALID(text)"],
		["json", "JSON_TABLE(doc, '$[*]' …)", "把 JSON 展开成行（8.0+）", "SELECT * FROM JSON_TABLE(doc, '$[*]' COLUMNS (id INT PATH '$.id')) jt"]
	];

	var META_MY = [
		["meta", "SELECT VERSION()", "查看 MySQL 版本", "SELECT VERSION()"],
		["meta", "SHOW DATABASES", "列出数据库", "SHOW DATABASES"],
		["meta", "SHOW TABLES", "列出当前库的表", "SHOW TABLES"],
		["meta", "DESCRIBE t / SHOW COLUMNS FROM t", "查看表结构", "DESCRIBE orders"],
		["meta", "SHOW CREATE TABLE t", "查看建表语句", "SHOW CREATE TABLE orders"],
		["meta", "SHOW INDEX FROM t", "查看索引", "SHOW INDEX FROM orders"],
		["meta", "SHOW STATUS / SHOW VARIABLES", "查看状态和配置", "SHOW VARIABLES LIKE 'innodb%'"],
		["meta", "information_schema.TABLES", "查所有库表的大小和行数", "SELECT table_schema, table_name, table_rows FROM information_schema.TABLES"],
		["meta", "SHOW PROCESSLIST", "查看正在执行的连接", "SHOW FULL PROCESSLIST"],
		["meta", "EXPLAIN", "查看执行计划", "EXPLAIN SELECT * FROM orders WHERE user_id = 1"]
	];

	var MISC_MY = [
		["misc", "LIMIT n", "只取前 n 行", "SELECT * FROM orders ORDER BY id DESC LIMIT 10"],
		["misc", "LIMIT offset, n", "分页", "SELECT * FROM orders LIMIT 20, 10"],
		["misc", "ON DUPLICATE KEY UPDATE", "主键冲突时改为更新", "INSERT INTO t (id, n) VALUES (1, 2) ON DUPLICATE KEY UPDATE n = 2"],
		["misc", "REPLACE INTO", "存在则删后插入", "REPLACE INTO t (id, n) VALUES (1, 2)"],
		["misc", "INSERT IGNORE", "冲突时跳过，不报错", "INSERT IGNORE INTO t (id) VALUES (1)"],
		["misc", "LAST_INSERT_ID()", "上一条 INSERT 生成的自增值", "SELECT LAST_INSERT_ID()"],
		["misc", "ROW_COUNT()", "上一条语句影响的行数", "SELECT ROW_COUNT()"],
		["misc", "USE db", "切换数据库", "USE shop"],
		["misc", "`反引号`", "标识符含关键字或特殊字符时加反引号", "SELECT `order`, `group` FROM t"],
		["misc", "@@variable", "查看系统变量", "SELECT @@sql_mode"]
	];

	var STR_MS = [
		["str", "CONCAT(a, b)", "拼接；任一为 NULL 结果就是 NULL", "CONCAT(first_name, ' ', last_name)"],
		["str", "CONCAT_WS(sep, …)", "带分隔符拼接（2017+）", "CONCAT_WS('-', city, district)"],
		["str", "LEN(s) / DATALENGTH(s)", "字符数 / 字节数", "LEN(name)"],
		["str", "SUBSTRING(s, start, len)", "截取，start 从 1 开始", "SUBSTRING(code, 1, 4)"],
		["str", "REPLACE(s, from, to)", "替换全部匹配", "REPLACE(phone, '-', '')"],
		["str", "UPPER(s) / LOWER(s)", "转大小写", "LOWER(email)"],
		["str", "TRIM(s)", "去两端空格（2017+）；LTRIM/RTRIM 去单侧", "TRIM(name)"],
		["str", "CHARINDEX(sub, s)", "子串位置；找不到为 0", "CHARINDEX('@', email)"],
		["str", "LEFT(s, n) / RIGHT(s, n)", "取左/右 n 个字符", "LEFT(code, 2)"],
		["str", "REPLICATE(s, n)", "重复 n 次", "REPLICATE('0', 4)"],
		["str", "STUFF(s, start, len, new)", "从 start 起删 len 个字符再插入", "STUFF(code, 3, 2, '**')"],
		["str", "STRING_SPLIT(s, sep)", "按分隔符拆成行（2016+，2022 起可带序号）", "SELECT value FROM STRING_SPLIT('a,b,c', ',')"],
		["str", "FORMAT(x, fmt)", "格式化数字或日期（.NET 格式串）", "FORMAT(amount, 'N2')"],
		["str", "PATINDEX(pat, s)", "支持通配符的位置查找", "PATINDEX('%[0-9]%', code)"]
	];

	var NUM_MS = [
		["num", "ROUND(x, d)", "四舍五入到 d 位", "ROUND(amount, 2)"],
		["num", "CEILING(x) / FLOOR(x)", "向上 / 向下取整", "CEILING(price)"],
		["num", "ABS(x)", "绝对值", "ABS(diff)"],
		["num", "x % y", "取余", "id % 2"],
		["num", "POWER(a, b)", "乘方；没有 POW 别名", "POWER(2, 10)"],
		["num", "SQRT(x)", "平方根", "SQRT(area)"],
		["num", "RAND()", "0~1 随机数；RAND(seed) 固定序列", "ORDER BY NEWID()"],
		["num", "SIGN(x)", "符号：-1、0、1", "SIGN(diff)"],
		["num", "ROUND(x, 0, 1)", "第三参数非 0 时截断而非四舍五入", "ROUND(amount, 2, 1)"]
	];

	var DATE_MS = [
		["date", "GETDATE()", "当前日期时间", "SELECT GETDATE()"],
		["date", "SYSDATETIME() / GETUTCDATE()", "更高精度 / UTC 时间", "SYSDATETIME()"],
		["date", "DATEADD(unit, n, d)", "日期加减，unit 如 day、hour", "DATEADD(day, 7, GETDATE())"],
		["date", "DATEDIFF(unit, a, b)", "按单位求差", "DATEDIFF(day, created_at, GETDATE())"],
		["date", "DATEPART(unit, d)", "取日期的一部分", "DATEPART(month, created_at)"],
		["date", "DATENAME(unit, d)", "取名称，如月份英文名、星期", "DATENAME(weekday, GETDATE())"],
		["date", "EOMONTH(d)", "当月最后一天（2012+）", "EOMONTH(GETDATE())"],
		["date", "DATEFROMPARTS(y, m, d)", "由年月日构造日期", "DATEFROMPARTS(2026, 1, 2)"],
		["date", "CONVERT(varchar, d, style)", "按样式号格式化，120 是 yyyy-mm-dd hh:mi:ss", "CONVERT(varchar(19), GETDATE(), 120)"],
		["date", "YEAR(d) / MONTH(d) / DAY(d)", "取年、月、日", "YEAR(created_at)"],
		["date", "ISDATE(s)", "字符串是否为合法日期", "ISDATE('2026-01-02')"]
	];

	var CONV_MS = [
		["conv", "CAST(x AS type)", "转换类型，如 int、decimal(10,2)、date", "CAST(price AS decimal(10,2))"],
		["conv", "CONVERT(type, x, style)", "带样式号的转换，日期常用", "CONVERT(varchar(10), GETDATE(), 120)"],
		["conv", "TRY_CAST(x AS type)", "失败返回 NULL（2012+）", "TRY_CAST(code AS int)"],
		["conv", "TRY_CONVERT(type, x)", "同上，CONVERT 版本", "TRY_CONVERT(int, code)"],
		["conv", "PARSE(s AS type)", "按区域设置解析，较慢", "PARSE('2026-01-02' AS date)"],
		["conv", "STR(x, len, d)", "数字转字符串，可指定长度和小数位", "STR(amount, 10, 2)"]
	];

	var AGGR_MS = [
		["aggr", "STRING_AGG(col, sep)", "分组内拼接字符串（2017+）", "SELECT STRING_AGG(name, ',') FROM users GROUP BY dept"],
		["aggr", "COUNT_BIG(*)", "返回 bigint 的计数", "SELECT COUNT_BIG(*) FROM orders"],
		["aggr", "STDEV(col) / VAR(col)", "标准差 / 方差", "STDEV(amount)"]
	];

	var CTRL_MS = [
		["ctrl", "IIF(cond, a, b)", "三元判断（2012+）", "IIF(amount > 0, '入', '出')"],
		["ctrl", "ISNULL(x, fallback)", "x 为 NULL 时取备用值（两个参数）", "ISNULL(nickname, name)"],
		["ctrl", "NULLIF(a, b)", "a = b 时返回 NULL", "NULLIF(count, 0)"],
		["ctrl", "COALESCE(a, b, …)", "取第一个非 NULL", "COALESCE(phone, mobile, email)"],
		["ctrl", "CHOOSE(i, v1, v2, …)", "按下标取值", "CHOOSE(2, 'a', 'b', 'c')"]
	];

	var JSON_MS = [
		["json", "JSON_VALUE(doc, '$.a')", "取标量值", "JSON_VALUE(doc, '$.user.name')"],
		["json", "JSON_QUERY(doc, '$.a')", "取对象或数组", "JSON_QUERY(doc, '$.roles')"],
		["json", "OPENJSON(doc)", "把 JSON 展开成行", "SELECT * FROM OPENJSON(doc) WITH (id int '$.id')"],
		["json", "FOR JSON PATH", "查询结果转 JSON", "SELECT id, name FROM users FOR JSON PATH"],
		["json", "JSON_MODIFY(doc, path, val)", "修改 JSON 字段", "JSON_MODIFY(doc, '$.vip', 'true')"],
		["json", "ISJSON(s)", "判断是否为合法 JSON", "ISJSON(text)"]
	];

	var META_MS = [
		["meta", "SELECT @@VERSION", "查看 SQL Server 版本", "SELECT @@VERSION"],
		["meta", "SELECT DB_NAME()", "当前数据库名", "SELECT DB_NAME()"],
		["meta", "SUSER_SNAME() / SUSER_NAME()", "当前登录名", "SELECT SUSER_SNAME()"],
		["meta", "OBJECT_ID('dbo.t')", "对象是否存在，不存在为 NULL", "IF OBJECT_ID('dbo.t') IS NOT NULL PRINT 'exists'"],
		["meta", "sys.tables / sys.columns", "查表和列的系统视图", "SELECT name FROM sys.tables"],
		["meta", "INFORMATION_SCHEMA.TABLES", "标准的元数据视图", "SELECT table_schema, table_name FROM INFORMATION_SCHEMA.TABLES"],
		["meta", "sp_help / sp_columns", "查看表结构", "EXEC sp_help 'orders'"],
		["meta", "OBJECT_NAME(id) / COL_LENGTH", "按 id 取对象名、按列取长度", "OBJECT_NAME(OBJECT_ID('dbo.t'))"]
	];

	var MISC_MS = [
		["misc", "SELECT TOP n", "只取前 n 行", "SELECT TOP 10 * FROM orders ORDER BY id DESC"],
		["misc", "OFFSET … FETCH NEXT", "分页（2012+）", "ORDER BY id OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY"],
		["misc", "SCOPE_IDENTITY()", "当前作用域最后插入的标识值", "SELECT SCOPE_IDENTITY()"],
		["misc", "@@IDENTITY / IDENT_CURRENT", "最后标识值 / 指定表的当前标识值", "SELECT IDENT_CURRENT('orders')"],
		["misc", "OUTPUT INSERTED.*", "INSERT/UPDATE 返回受影响行", "INSERT INTO t (n) OUTPUT INSERTED.id VALUES (1)"],
		["misc", "N'文本'", "Unicode 字符串字面量", "WHERE name = N'张三'"],
		["misc", "[方括号]", "标识符含关键字或空格时加方括号", "SELECT [order], [group] FROM t"],
		["misc", "GO", "批处理分隔符，SSMS 和 sqlcmd 识别", "GO"],
		["misc", "SET NOCOUNT ON", "脚本里关闭“n 行受影响”提示", "SET NOCOUNT ON"],
		["misc", "WITH (NOLOCK)", "读未提交，减少阻塞，可能脏读", "SELECT * FROM orders WITH (NOLOCK)"]
	];

	var STR_OR = [
		["str", "a || b", "拼接，任一为 NULL 结果就是 NULL", "first_name || ' ' || last_name"],
		["str", "CONCAT(a, b)", "拼接，NULL 当空串处理（只有两个参数）", "CONCAT(city, district)"],
		["str", "LENGTH(s)", "字符数", "LENGTH(name)"],
		["str", "SUBSTR(s, pos, len)", "截取，pos 从 1 开始；0 当 1", "SUBSTR(code, 1, 4)"],
		["str", "REPLACE(s, from, to)", "替换全部匹配", "REPLACE(phone, '-', '')"],
		["str", "UPPER(s) / LOWER(s) / INITCAP(s)", "大小写；INITCAP 首字母大写", "INITCAP(name)"],
		["str", "TRIM(s) / LTRIM / RTRIM", "去两端或单侧空格", "TRIM(name)"],
		["str", "INSTR(s, sub, pos, n)", "子串位置；找不到为 0", "INSTR(email, '@')"],
		["str", "LPAD(s, n, pad) / RPAD", "左/右补齐到 n 位", "LPAD(id, 8, '0')"],
		["str", "REGEXP_REPLACE(s, pat, rep)", "正则替换（10g+）", "REGEXP_REPLACE(phone, '[^0-9]', '')"],
		["str", "REGEXP_SUBSTR(s, pat)", "正则提取", "REGEXP_SUBSTR(text, '[0-9]+')"],
		["str", "REGEXP_LIKE(s, pat)", "正则匹配，可当条件用", "WHERE REGEXP_LIKE(code, '^A[0-9]+$')"]
	];

	var NUM_OR = [
		["num", "ROUND(x, d)", "四舍五入到 d 位", "ROUND(amount, 2)"],
		["num", "CEIL(x) / FLOOR(x)", "向上 / 向下取整", "CEIL(price)"],
		["num", "TRUNC(x, d)", "截断，不四舍五入", "TRUNC(amount, 2)"],
		["num", "ABS(x)", "绝对值", "ABS(diff)"],
		["num", "MOD(a, b)", "取余", "MOD(id, 2)"],
		["num", "POWER(a, b)", "乘方；没有 POW 别名", "POWER(2, 10)"],
		["num", "SQRT(x)", "平方根", "SQRT(area)"],
		["num", "REMAINDER(a, b)", "取余，符号跟随被除数", "REMAINDER(7, 3)"],
		["num", "SIGN(x)", "符号：-1、0、1", "SIGN(diff)"],
		["num", "GREATEST(…) / LEAST(…)", "多个值里的最大 / 最小", "GREATEST(a, b, 0)"]
	];

	var DATE_OR = [
		["date", "SYSDATE", "当前日期时间，来自数据库服务器", "SELECT SYSDATE FROM dual"],
		["date", "SYSTIMESTAMP / CURRENT_DATE", "带时区的时间戳 / 会话时区的日期", "SYSTIMESTAMP"],
		["date", "ADD_MONTHS(d, n)", "按月加减", "ADD_MONTHS(SYSDATE, 3)"],
		["date", "MONTHS_BETWEEN(a, b)", "相差月数，可能是小数", "MONTHS_BETWEEN(SYSDATE, created_at)"],
		["date", "LAST_DAY(d)", "当月最后一天", "LAST_DAY(SYSDATE)"],
		["date", "NEXT_DAY(d, 'MONDAY')", "下一个指定星期几", "NEXT_DAY(SYSDATE, 'MONDAY')"],
		["date", "TRUNC(d, 'MM')", "截断到月、日、年等", "TRUNC(SYSDATE, 'MM')"],
		["date", "TO_CHAR(d, fmt)", "日期转字符串", "TO_CHAR(SYSDATE, 'YYYY-MM-DD HH24:MI:SS')"],
		["date", "TO_DATE(s, fmt)", "字符串转日期", "TO_DATE('2026-01-02', 'YYYY-MM-DD')"],
		["date", "EXTRACT(unit FROM d)", "取年、月、日、时等", "EXTRACT(YEAR FROM created_at)"],
		["date", "d + INTERVAL '7' DAY", "日期加减", "SYSDATE + INTERVAL '7' DAY"],
		["date", "ROUND(d, 'HH24')", "对日期做四舍五入", "ROUND(SYSDATE, 'HH24')"]
	];

	var CONV_OR = [
		["conv", "TO_CHAR(x, fmt)", "数字或日期转字符串", "TO_CHAR(amount, 'FM999999990.00')"],
		["conv", "TO_NUMBER(s)", "字符串转数字", "TO_NUMBER('123.45')"],
		["conv", "CAST(x AS type)", "标准转换，如 VARCHAR2(20)、NUMBER", "CAST(amount AS NUMBER(10,2))"]
	];

	var AGGR_OR = [
		["aggr", "LISTAGG(col, sep) WITHIN GROUP", "分组内拼接字符串（11g R2+）", "SELECT LISTAGG(name, ',') WITHIN GROUP (ORDER BY name) FROM users GROUP BY dept"],
		["aggr", "STDDEV(col) / VARIANCE(col)", "标准差 / 方差", "STDDEV(amount)"],
		["aggr", "MAX(col) KEEP (DENSE_RANK LAST …)", "取分组内最后一行的某个字段", "MAX(amount) KEEP (DENSE_RANK LAST ORDER BY created_at)"]
	];

	var CTRL_OR = [
		["ctrl", "DECODE(x, a, r1, b, r2, default)", "等值判断，可替代简单 CASE", "DECODE(status, 1, '启用', 0, '停用', '其他')"],
		["ctrl", "NVL(x, fallback)", "x 为 NULL 时取备用值", "NVL(nickname, name)"],
		["ctrl", "NVL2(x, a, b)", "x 非 NULL 取 a，否则取 b", "NVL2(phone, '有', '无')"],
		["ctrl", "NULLIF(a, b)", "a = b 时返回 NULL", "NULLIF(count, 0)"],
		["ctrl", "COALESCE(a, b, …)", "取第一个非 NULL", "COALESCE(phone, mobile, email)"]
	];

	var JSON_OR = [
		["json", "JSON_VALUE(doc, '$.a')", "取标量值（12c+）", "JSON_VALUE(doc, '$.user.name')"],
		["json", "JSON_QUERY(doc, '$.a')", "取对象或数组", "JSON_QUERY(doc, '$.roles')"],
		["json", "JSON_TABLE(doc, '$[*]' COLUMNS …)", "把 JSON 展开成行（12c+）", "SELECT * FROM JSON_TABLE(doc, '$[*]' COLUMNS (id NUMBER PATH '$.id'))"],
		["json", "JSON_OBJECT(k, v, …)", "构造 JSON 对象", "JSON_OBJECT('id', id, 'name', name)"],
		["json", "JSON_ARRAY(v, …)", "构造 JSON 数组", "JSON_ARRAY(1, 2, 3)"],
		["json", "JSON_EXISTS(doc, '$.a')", "判断路径是否存在", "JSON_EXISTS(doc, '$.vip')"],
		["json", "IS JSON", "判断列值是否为合法 JSON", "WHERE doc IS JSON"]
	];

	var META_OR = [
		["meta", "SELECT * FROM v$version", "查看 Oracle 版本", "SELECT * FROM v$version"],
		["meta", "SELECT * FROM dual", "单行伪表，取常量用", "SELECT SYSDATE FROM dual"],
		["meta", "USER_TABLES / ALL_TABLES", "当前用户 / 可访问的表", "SELECT table_name FROM user_tables"],
		["meta", "USER_TAB_COLUMNS", "表的列信息", "SELECT column_name, data_type FROM user_tab_columns WHERE table_name = 'ORDERS'"],
		["meta", "USER_INDEXES", "索引信息", "SELECT index_name FROM user_indexes"],
		["meta", "USER / SYS_CONTEXT", "当前用户 / 环境信息", "SYS_CONTEXT('USERENV', 'DB_NAME')"],
		["meta", "DESC t", "SQL*Plus 查看表结构", "DESC orders"],
		["meta", "EXPLAIN PLAN FOR", "查看执行计划", "EXPLAIN PLAN FOR SELECT * FROM orders; SELECT * FROM TABLE(DBMS_XPLAN.DISPLAY)"]
	];

	var MISC_OR = [
		["misc", "ROWNUM", "取前 n 行，先编号后排序", "SELECT * FROM orders WHERE ROWNUM <= 10"],
		["misc", "FETCH FIRST n ROWS ONLY", "标准分页写法（12c+）", "ORDER BY id OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY"],
		["misc", "CONNECT BY PRIOR", "层级查询，树形结构", "SELECT LEVEL, name FROM t START WITH parent_id IS NULL CONNECT BY PRIOR id = parent_id"],
		["misc", "seq.NEXTVAL", "取序列下一个值", "SELECT seq_order.NEXTVAL FROM dual"],
		["misc", "MERGE INTO", "按条件插入或更新", "MERGE INTO t USING src ON (t.id = src.id) WHEN MATCHED THEN UPDATE SET n = src.n"],
		["misc", "DBMS_OUTPUT.PUT_LINE", "PL/SQL 里打印，先 SET SERVEROUTPUT ON", "DBMS_OUTPUT.PUT_LINE('ok')"],
		["misc", "SET LINESIZE / PAGESIZE", "SQL*Plus 调整显示宽度", "SET LINESIZE 200"],
		["misc", "SPOOL out.txt", "SQL*Plus 把输出写入文件", "SPOOL out.txt"],
		["misc", "/", "SQL*Plus 重新执行上一条语句", "/"]
	];

	var STR_LT = [
		["str", "a || b", "拼接，NULL 当空串处理", "first_name || ' ' || last_name"],
		["str", "concat(a, b)", "拼接（3.44+，两参数）", "concat(city, district)"],
		["str", "length(s)", "字符数", "length(name)"],
		["str", "substr(s, pos, len)", "截取，pos 从 1 开始", "substr(code, 1, 4)"],
		["str", "replace(s, from, to)", "替换全部匹配", "replace(phone, '-', '')"],
		["str", "upper(s) / lower(s)", "转大小写", "lower(email)"],
		["str", "trim(s) / ltrim / rtrim", "去两端或单侧空格", "trim(name)"],
		["str", "instr(s, sub)", "子串位置；找不到为 0", "instr(email, '@')"],
		["str", "printf(fmt, …) / format", "格式化输出", "printf('%05d', id)"],
		["str", "hex(x) / quote(x)", "转十六进制 / 加引号转义", "quote(name)"],
		["str", "unicode(c) / char(n)", "字符与码点互转", "char(65)"]
	];

	var NUM_LT = [
		["num", "round(x, d)", "四舍五入到 d 位", "round(amount, 2)"],
		["num", "abs(x)", "绝对值", "abs(diff)"],
		["num", "max(a, b, …) / min(a, b, …)", "多参数时是标量函数取最大 / 最小", "max(a, b)"],
		["num", "random()", "64 位随机整数", "abs(random()) % 100"],
		["num", "ceil(x) / floor(x)", "向上 / 向下取整（3.35+，需启用数学函数）", "ceil(price)"],
		["num", "pow(a, b) / sqrt(x)", "乘方 / 平方根（3.35+）", "pow(2, 10)"],
		["num", "x % y", "取余", "id % 2"]
	];

	var DATE_LT = [
		["date", "date('now')", "当前日期，UTC", "SELECT date('now')"],
		["date", "datetime('now', 'localtime')", "当前日期时间，本地时区", "datetime('now', 'localtime')"],
		["date", "date('now', '+7 day')", "日期加减：day、month、year、hour", "date('now', '+1 month')"],
		["date", "time('now')", "当前时间", "time('now')"],
		["date", "julianday(d)", "儒略日，可用来求天数差", "julianday(end) - julianday(start)"],
		["date", "unixepoch(d)", "转 Unix 时间戳（3.38+）", "unixepoch('2026-01-02')"],
		["date", "strftime('%Y-%m', d)", "按格式输出，支持 %Y %m %d %H %M %S %w", "strftime('%Y-%m', created_at)"],
		["date", "CURRENT_TIMESTAMP", "当前 UTC 时间戳", "SELECT CURRENT_TIMESTAMP"],
		["date", "date(d, 'start of month')", "取月初、月末等修饰符", "date('now', 'start of month', '+1 month', '-1 day')"]
	];

	var CONV_LT = [
		["conv", "CAST(x AS type)", "类型：INTEGER、REAL、TEXT、BLOB、NUMERIC", "CAST(price AS REAL)"],
		["conv", "typeof(x)", "查看值的存储类型", "typeof(created_at)"],
		["conv", "quote(x)", "输出带引号的 SQL 字面量", "quote(name)"],
		["conv", "hex(x)", "转十六进制文本", "hex(randomblob(4))"]
	];

	var AGGR_LT = [
		["aggr", "total(col)", "求和，全是 NULL 时返回 0.0", "SELECT total(amount) FROM orders"],
		["aggr", "group_concat(col, sep)", "分组内拼接字符串", "SELECT group_concat(name, ',') FROM users GROUP BY dept"],
		["aggr", "COUNT(*) FILTER (WHERE …)", "带条件的聚合（3.30+）", "SELECT COUNT(*) FILTER (WHERE amount > 100) FROM orders"]
	];

	var CTRL_LT = [
		["ctrl", "iif(cond, a, b)", "三元判断（3.32+）", "iif(amount > 0, '入', '出')"],
		["ctrl", "ifnull(x, fallback)", "x 为 NULL 时取备用值", "ifnull(nickname, name)"],
		["ctrl", "nullif(a, b)", "a = b 时返回 NULL", "nullif(count, 0)"],
		["ctrl", "coalesce(a, b, …)", "取第一个非 NULL", "coalesce(phone, mobile, email)"]
	];

	var JSON_LT = [
		["json", "json_extract(doc, '$.a')", "取 JSON 字段（JSON1 内置）", "json_extract(doc, '$.user.name')"],
		["json", "doc -> '$.a'", "取 JSON 值（保留引号）", "doc->'$.user.name'"],
		["json", "doc ->> '$.a'", "取 JSON 值并去掉引号", "doc->>'$.user.name'"],
		["json", "json_each(doc)", "把数组或对象展开成行", "SELECT value FROM json_each(doc, '$.tags')"],
		["json", "json_tree(doc)", "递归展开所有层", "SELECT * FROM json_tree(doc)"],
		["json", "json_object(k, v, …)", "构造 JSON 对象", "json_object('id', id, 'name', name)"],
		["json", "json_array(v, …)", "构造 JSON 数组", "json_array(1, 2, 3)"],
		["json", "json_group_array(col)", "把行聚合成 JSON 数组", "SELECT json_group_array(name) FROM users"],
		["json", "json_group_object(k, v)", "把行聚合成 JSON 对象", "SELECT json_group_object(id, name) FROM users"],
		["json", "json_set(doc, path, val)", "修改 JSON 字段；json_patch 用于合并", "json_set(doc, '$.vip', json('true'))"],
		["json", "json_valid(s)", "判断是否为合法 JSON", "json_valid(text)"]
	];

	var META_LT = [
		["meta", "sqlite_version()", "查看 SQLite 版本", "SELECT sqlite_version()"],
		["meta", "sqlite_master", "库里的表、索引、视图定义", "SELECT name, type FROM sqlite_master WHERE type = 'table'"],
		["meta", "PRAGMA table_info(t)", "查看表的列", "PRAGMA table_info(orders)"],
		["meta", "PRAGMA table_list", "列出所有表（3.37+）", "PRAGMA table_list"],
		["meta", "PRAGMA index_list(t)", "查看索引", "PRAGMA index_list(orders)"],
		["meta", "PRAGMA foreign_key_list(t)", "查看外键", "PRAGMA foreign_key_list(orders)"],
		["meta", "last_insert_rowid()", "最后插入的 rowid", "SELECT last_insert_rowid()"],
		["meta", "changes()", "上一条语句影响的行数", "SELECT changes()"],
		["meta", "EXPLAIN QUERY PLAN", "查看执行计划", "EXPLAIN QUERY PLAN SELECT * FROM orders WHERE user_id = 1"]
	];

	var MISC_LT = [
		["misc", "LIMIT n OFFSET m", "分页", "SELECT * FROM orders ORDER BY id DESC LIMIT 10 OFFSET 20"],
		["misc", "INSERT OR REPLACE", "主键冲突时替换", "INSERT OR REPLACE INTO t (id, n) VALUES (1, 2)"],
		["misc", "ON CONFLICT DO UPDATE", "冲突时更新（3.24+）", "INSERT INTO t (id, n) VALUES (1, 2) ON CONFLICT(id) DO UPDATE SET n = excluded.n"],
		["misc", "rowid", "隐式主键，整数且自增", "SELECT rowid, * FROM t"],
		["misc", "WITHOUT ROWID", "省掉 rowid 的表，适合窄表", "CREATE TABLE kv (k TEXT PRIMARY KEY, v TEXT) WITHOUT ROWID"],
		["misc", "STRICT", "严格类型表（3.37+）", "CREATE TABLE t (id INTEGER PRIMARY KEY, n TEXT) STRICT"],
		["misc", "VACUUM", "整理数据库文件、回收空间", "VACUUM"],
		["misc", "ATTACH DATABASE", "挂载另一个库文件", "ATTACH DATABASE 'other.db' AS other"],
		["misc", ".mode / .headers", "命令行工具的输出格式设置", ".mode column"],
		["misc", "生成列", "虚拟列由表达式算出（3.31+）", "ALTER TABLE t ADD COLUMN total INT GENERATED ALWAYS AS (a + b) VIRTUAL"]
	];

	var STR_PG = [
		["str", "a || b", "拼接，NULL 会污染结果", "first_name || ' ' || last_name"],
		["str", "concat(a, b, …)", "拼接，NULL 当空串", "concat(city, district)"],
		["str", "concat_ws(sep, …)", "带分隔符拼接，跳过 NULL", "concat_ws('-', city, district)"],
		["str", "length(s)", "字符数；octet_length 是字节数", "length(name)"],
		["str", "substring(s, start, len)", "截取，start 从 1 开始", "substring(code, 1, 4)"],
		["str", "replace(s, from, to)", "替换全部匹配", "replace(phone, '-', '')"],
		["str", "upper(s) / lower(s) / initcap(s)", "大小写；initcap 首字母大写", "initcap(name)"],
		["str", "trim(s) / ltrim / rtrim", "去两端或单侧字符", "trim(both ' ' from name)"],
		["str", "position(sub in s)", "子串位置；找不到为 0", "position('@' in email)"],
		["str", "left(s, n) / right(s, n)", "取左/右 n 个字符", "left(code, 2)"],
		["str", "lpad(s, n, pad) / rpad", "左/右补齐到 n 位", "lpad(id::text, 8, '0')"],
		["str", "regexp_replace(s, pat, rep, 'g')", "正则替换，g 表示全部", "regexp_replace(phone, '[^0-9]', '', 'g')"],
		["str", "regexp_matches(s, pat)", "正则提取，返回数组", "regexp_matches(text, '[0-9]+', 'g')"],
		["str", "split_part(s, sep, n)", "按分隔符取第 n 段，从 1 开始", "split_part(email, '@', 1)"],
		["str", "format('%s-%s', a, b)", "printf 风格格式化", "format('%s(%s)', name, code)"]
	];

	var NUM_PG = [
		["num", "round(x, d)", "四舍五入；numeric 才支持指定位数", "round(amount::numeric, 2)"],
		["num", "ceil(x) / floor(x)", "向上 / 向下取整", "ceil(price)"],
		["num", "trunc(x, d)", "截断，不四舍五入", "trunc(amount, 2)"],
		["num", "abs(x)", "绝对值", "abs(diff)"],
		["num", "mod(a, b) 或 a % b", "取余", "mod(id, 2)"],
		["num", "power(a, b)", "乘方", "power(2, 10)"],
		["num", "sqrt(x)", "平方根", "sqrt(area)"],
		["num", "random()", "0~1 随机浮点", "ORDER BY random() LIMIT 1"],
		["num", "sign(x)", "符号：-1、0、1", "sign(diff)"],
		["num", "greatest(…) / least(…)", "多个值里的最大 / 最小", "greatest(a, b, 0)"]
	];

	var DATE_PG = [
		["date", "now()", "当前事务时间戳", "SELECT now()"],
		["date", "current_date / current_timestamp", "当前日期 / 时间戳", "current_date"],
		["date", "date_trunc('day', ts)", "截断到 day、month、hour 等", "date_trunc('month', created_at)"],
		["date", "extract(field from ts) / date_part", "取年、月、日、时等", "extract(year from created_at)"],
		["date", "age(a, b)", "相差多久，返回 interval", "age(now(), created_at)"],
		["date", "ts + interval '7 day'", "日期加减", "now() + interval '7 day'"],
		["date", "to_char(ts, 'YYYY-MM-DD')", "按格式输出", "to_char(created_at, 'YYYY-MM-DD')"],
		["date", "to_date('2026-01-02', 'YYYY-MM-DD')", "字符串转日期", "to_date('2026-01-02', 'YYYY-MM-DD')"],
		["date", "to_timestamp(ts)", "数字或字符串转时间戳", "to_timestamp(1700000000)"],
		["date", "make_date(y, m, d)", "由年月日构造日期", "make_date(2026, 1, 2)"],
		["date", "ts AT TIME ZONE 'UTC'", "按指定时区显示", "created_at AT TIME ZONE 'Asia/Shanghai'"]
	];

	var CONV_PG = [
		["conv", "x::type", "类型转换简写，如 ::int、::numeric(10,2)", "amount::numeric(10,2)"],
		["conv", "CAST(x AS type)", "标准写法", "CAST(price AS numeric(10,2))"],
		["conv", "to_char(x, fmt)", "数字或日期转字符串", "to_char(amount, 'FM999990.00')"],
		["conv", "to_number(s, fmt)", "字符串转数字", "to_number('1,234.5', '9,999.9')"]
	];

	var AGGR_PG = [
		["aggr", "string_agg(col, sep)", "分组内拼接字符串", "SELECT string_agg(name, ',') FROM users GROUP BY dept"],
		["aggr", "array_agg(col)", "聚合成数组", "SELECT array_agg(name) FROM users"],
		["aggr", "COUNT(*) FILTER (WHERE …)", "带条件的聚合", "SELECT count(*) FILTER (WHERE amount > 100) FROM orders"],
		["aggr", "percentile_cont(0.5) WITHIN GROUP", "中位数等分位数", "percentile_cont(0.5) WITHIN GROUP (ORDER BY amount)"],
		["aggr", "bool_and(col) / bool_or(col)", "全真 / 任一为真", "bool_and(paid)"]
	];

	var CTRL_PG = [
		["ctrl", "coalesce(a, b, …)", "取第一个非 NULL", "coalesce(phone, mobile, email)"],
		["ctrl", "nullif(a, b)", "a = b 时返回 NULL", "nullif(count, 0)"],
		["ctrl", "IS DISTINCT FROM", "NULL 安全的比较", "WHERE a IS DISTINCT FROM b"]
	];

	var JSON_PG = [
		["json", "doc -> 'a'", "取 JSON 值，保留 JSON 类型", "doc->'user'->'name'"],
		["json", "doc ->> 'a'", "取 JSON 值并转成文本", "doc->>'name'"],
		["json", "doc #> '{a,b}' / #>>", "按路径取嵌套值", "doc#>>'{user,name}'"],
		["json", "jsonb_build_object(k, v, …)", "构造 JSON 对象", "jsonb_build_object('id', id, 'name', name)"],
		["json", "jsonb_agg(col)", "把行聚合成 JSON 数组", "SELECT jsonb_agg(name) FROM users"],
		["json", "jsonb_each(doc)", "把对象展开成键值行", "SELECT * FROM jsonb_each(doc)"],
		["json", "jsonb_set(doc, '{a}', '\"v\"')", "修改或新增字段", "jsonb_set(doc, '{vip}', 'true')"],
		["json", "jsonb_path_query(doc, '$.a[*]')", "JSONPath 查询", "jsonb_path_query(doc, '$.tags[*]')"],
		["json", "to_jsonb(row)", "整行转 JSON", "SELECT to_jsonb(orders) FROM orders"]
	];

	var META_PG = [
		["meta", "SELECT version()", "查看 PostgreSQL 版本", "SELECT version()"],
		["meta", "current_database() / current_user", "当前库和用户", "current_database()"],
		["meta", "\\d t / \\dt", "psql 里查看表结构和表列表", "\\dt"],
		["meta", "pg_tables", "用 SQL 查表列表", "SELECT tablename FROM pg_tables WHERE schemaname = 'public'"],
		["meta", "information_schema.tables", "标准元数据视图", "SELECT table_schema, table_name FROM information_schema.tables"],
		["meta", "pg_indexes", "查看索引", "SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'orders'"],
		["meta", "pg_stat_activity", "查看正在执行的查询", "SELECT pid, state, query FROM pg_stat_activity"],
		["meta", "pg_size_pretty(pg_database_size(…))", "查看库大小", "pg_size_pretty(pg_database_size(current_database()))"]
	];

	var MISC_PG = [
		["misc", "LIMIT n OFFSET m", "分页", "SELECT * FROM orders ORDER BY id DESC LIMIT 10 OFFSET 20"],
		["misc", "RETURNING *", "INSERT/UPDATE/DELETE 返回受影响行", "INSERT INTO t (n) VALUES (2) RETURNING id"],
		["misc", "ON CONFLICT DO UPDATE", "Upsert", "INSERT INTO t (id, n) VALUES (1, 2) ON CONFLICT (id) DO UPDATE SET n = excluded.n"],
		["misc", "serial / GENERATED ALWAYS AS IDENTITY", "自增列；新代码推荐 IDENTITY", "id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY"],
		["misc", "WITH 查询", "CTE，可递归", "WITH t AS (SELECT 1 AS n) SELECT * FROM t"],
		["misc", "EXPLAIN ANALYZE", "看执行计划和实际耗时", "EXPLAIN ANALYZE SELECT * FROM orders WHERE user_id = 1"],
		["misc", "VACUUM / ANALYZE", "回收空间、更新统计信息", "VACUUM ANALYZE orders"],
		["misc", "nextval('seq')", "取序列下一个值", "SELECT nextval('orders_id_seq')"],
		["misc", "ILIKE", "不区分大小写的 LIKE", "WHERE name ILIKE 'a%'"],
		["misc", "DISTINCT ON (col)", "每个分组只取第一行", "SELECT DISTINCT ON (user_id) * FROM orders ORDER BY user_id, created_at DESC"],
		["misc", "LATERAL", "子查询里引用左边的列", "SELECT * FROM users u, LATERAL (SELECT * FROM orders o WHERE o.user_id = u.id LIMIT 1) x"]
	];

	var SETS = [
		{
			id: "mysql",
			name: "MySQL",
			rows: [].concat(STR_MY, NUM_MY, DATE_MY, CONV_MY, AGG, AGGR_MY, WIN, CASE, CTRL_MY, JSON_MY, META_MY, MISC_MY),
			placeholder: "搜索：CONCAT、DATE_FORMAT、JSON、GROUP_CONCAT、SHOW"
		},
		{
			id: "sqlserver",
			name: "SQL Server",
			rows: [].concat(STR_MS, NUM_MS, DATE_MS, CONV_MS, AGG, AGGR_MS, WIN, CASE, CTRL_MS, JSON_MS, META_MS, MISC_MS),
			placeholder: "搜索：CAST、DATEADD、STRING_AGG、OPENJSON、TOP"
		},
		{
			id: "oracle",
			name: "Oracle",
			rows: [].concat(STR_OR, NUM_OR, DATE_OR, CONV_OR, AGG, AGGR_OR, WIN, CASE, CTRL_OR, JSON_OR, META_OR, MISC_OR),
			placeholder: "搜索：TO_CHAR、NVL、LISTAGG、ROWNUM、DUAL"
		},
		{
			id: "sqlite",
			name: "SQLite",
			rows: [].concat(STR_LT, NUM_LT, DATE_LT, CONV_LT, AGG, AGGR_LT, WIN, CASE, CTRL_LT, JSON_LT, META_LT, MISC_LT),
			placeholder: "搜索：printf、strftime、json_extract、PRAGMA、rowid"
		},
		{
			id: "postgres",
			name: "PostgreSQL",
			rows: [].concat(STR_PG, NUM_PG, DATE_PG, CONV_PG, AGG, AGGR_PG, WIN, CASE, CTRL_PG, JSON_PG, META_PG, MISC_PG),
			placeholder: "搜索：::、date_trunc、string_agg、jsonb、RETURNING"
		}
	];

	function makeset(d){
		var out = [];
		var i;
		for (i = 0; i < d.rows.length; i++) out.push(R(d.rows[i]));
		return {
			id: d.id,
			name: d.name,
			cols: COLS,
			widths: WIDTHS,
			groups: GROUPS,
			rows: out,
			placeholder: d.placeholder,
			big: 0,
			sub: 1,
			copy: 0,
			defaultPick: "COUNT(*)"
		};
	}

	SK.regref("u-sqlfunc", {
		title: "SQL 函数查询",
		setsLabel: "数据库",
		store: "sk-sql-db",
		sets: SETS.map(makeset)
	});
})();
