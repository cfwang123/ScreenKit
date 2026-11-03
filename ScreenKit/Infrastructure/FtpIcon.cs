namespace ScreenKit;

/// <summary>
/// 列表图标：按扩展名归到几大类，每类一个 Segoe MDL2 Assets 字形。
/// 只做粗分类——分得太细反而认不出，够扫一眼知道是什么就行。
/// </summary>
static class FtpIcon {
	// 字形码位（Segoe MDL2 Assets）
	const string Folder = "\uE8B7";
	const string File = "\uE7C3";
	const string Image = "\uEB9F";
	const string Video = "\uE714";
	const string Audio = "\uE8D6";
	const string Zip = "\uF012";
	const string Doc = "\uE8A5";
	const string Pdf = "\uEA90";
	const string Code = "\uE943";
	const string Exe = "\uE7B8";

	// 颜色：文件夹和代码用主色，媒体类给点区分，其他保持中性
	const string ColFolder = "#E8C46A";
	const string ColImage = "#7BC46A";
	const string ColVideo = "#6AA8E8";
	const string ColAudio = "#C77BD6";
	const string ColZip = "#D69B6A";
	const string ColDoc = "#8FA8C8";
	const string ColPdf = "#E07A7A";
	const string ColCode = "#6AC4C4";
	const string ColExe = "#B0B0B8";
	const string ColMuted = "#8A8A92";

	static readonly HashSet<string> images = new(StringComparer.OrdinalIgnoreCase) {
		".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff",
		".svg", ".heic", ".avif", ".raw", ".psd", ".jfif"
	};

	static readonly HashSet<string> videos = new(StringComparer.OrdinalIgnoreCase) {
		".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
		".mpg", ".mpeg", ".ts", ".m2ts", ".3gp", ".rmvb", ".rm", ".vob"
	};

	static readonly HashSet<string> audios = new(StringComparer.OrdinalIgnoreCase) {
		".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus",
		".ape", ".aiff", ".mid", ".midi", ".amr"
	};

	static readonly HashSet<string> zips = new(StringComparer.OrdinalIgnoreCase) {
		".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".zst",
		".iso", ".cab", ".tgz", ".tbz", ".lz", ".lzma"
	};

	static readonly HashSet<string> docs = new(StringComparer.OrdinalIgnoreCase) {
		".txt", ".md", ".log", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
		".rtf", ".odt", ".ods", ".odp", ".csv", ".epub", ".mobi"
	};

	static readonly HashSet<string> pdfs = new(StringComparer.OrdinalIgnoreCase) {
		".pdf"
	};

	static readonly HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase) {
		".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".java", ".c", ".h", ".cpp",
		".hpp", ".cc", ".go", ".rs", ".rb", ".php", ".sql", ".html", ".htm",
		".css", ".scss", ".json", ".xml", ".yml", ".yaml", ".toml", ".ini",
		".sh", ".bat", ".cmd", ".ps1", ".lua", ".kt", ".swift", ".m", ".vue"
	};

	static readonly HashSet<string> exes = new(StringComparer.OrdinalIgnoreCase) {
		".exe", ".dll", ".msi", ".apk", ".app", ".bin", ".so", ".dylib", ".sys", ".ocx"
	};

	/// <summary>取图标字形。</summary>
	public static string Glyph(string name, bool isDir) {
		if (isDir) return Folder;
		var ext = extOf(name);
		if (ext.Length == 0) return File;
		if (images.Contains(ext)) return Image;
		if (videos.Contains(ext)) return Video;
		if (audios.Contains(ext)) return Audio;
		if (zips.Contains(ext)) return Zip;
		if (pdfs.Contains(ext)) return Pdf;
		if (exes.Contains(ext)) return Exe;
		if (codes.Contains(ext)) return Code;
		if (docs.Contains(ext)) return Doc;
		return File;
	}

	/// <summary>取图标颜色（十六进制字符串，XAML 直接当 Brush 的 Color 用）。</summary>
	public static string Color(string name, bool isDir) {
		if (isDir) return ColFolder;
		var ext = extOf(name);
		if (ext.Length == 0) return ColMuted;
		if (images.Contains(ext)) return ColImage;
		if (videos.Contains(ext)) return ColVideo;
		if (audios.Contains(ext)) return ColAudio;
		if (zips.Contains(ext)) return ColZip;
		if (pdfs.Contains(ext)) return ColPdf;
		if (exes.Contains(ext)) return ColExe;
		if (codes.Contains(ext)) return ColCode;
		if (docs.Contains(ext)) return ColDoc;
		return ColMuted;
	}

	/// <summary>取出小写扩展名（带点）；没有就返回空串。</summary>
	static string extOf(string name) {
		if (string.IsNullOrEmpty(name)) return "";
		var i = name.LastIndexOf('.');
		// 只剩「.」或以点结尾，都当没有扩展名
		if (i < 0 || i == name.Length - 1) return "";
		// 名字里只有扩展名（如 .gitignore）不算，避免把隐藏文件当类型
		if (i == 0) return "";
		return name.Substring(i).ToLowerInvariant();
	}
}
