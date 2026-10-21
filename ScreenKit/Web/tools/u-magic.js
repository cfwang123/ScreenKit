(function(){
	// [分组, 类型, 扩展名, 魔数(Hex), ASCII, 偏移, 说明, 备注]
	function R(g, type, ext, hex, ascii, offset, desc, note){
		return {
			g: g,
			c: [type, ext, hex, ascii],
			x: hex.replace(/\s+/g, "") + " " + ascii + " " + desc + " " + (note || ""),
			d: [["偏移", offset], ["说明", desc], ["备注", note || "—"]]
		};
	}

	var ROWS = [
		R("image", "JPEG", ".jpg .jpeg", "FF D8 FF", "...", "0", "最常见的照片格式，有损压缩。", "FF D8 FF 后面通常紧跟 E0/E1 等段标记。"),
		R("image", "PNG", ".png", "89 50 4E 47 0D 0A 1A 0A", ".PNG....", "0", "无损位图，支持透明。", "签名固定 8 字节。"),
		R("image", "GIF", ".gif", "47 49 46 38 39 61", "GIF89a", "0", "支持简单动画和 1 位透明。", "旧版文件头是 GIF87a（47 49 46 38 37 61）。"),
		R("image", "BMP", ".bmp", "42 4D", "BM", "0", "Windows 位图，通常未压缩。", "后面两个字节是文件大小。"),
		R("image", "WebP", ".webp", "52 49 46 46 ?? ?? ?? ?? 57 45 42 50", "RIFF....WEBP", "0", "RIFF 容器，支持有损、无损和透明。", "偏移 8–11 固定为 WEBP。"),
		R("image", "TIFF（小端）", ".tif .tiff", "49 49 2A 00", "II*.", "0", "TIFF 图片，印刷和扫描常用。", "II 表示小端字节序。"),
		R("image", "TIFF（大端）", ".tif .tiff", "4D 4D 00 2A", "MM.*", "0", "TIFF 图片的另一种字节序。", "MM 表示大端字节序。"),
		R("image", "ICO", ".ico", "00 00 01 00", "....", "0", "Windows 图标。", "01 表示图标，00 表示非 PNG 格式。"),
		R("image", "CUR", ".cur", "00 00 02 00", "....", "0", "Windows 鼠标光标。", "和 ICO 结构相同，类型字段是 02。"),
		R("image", "PSD", ".psd", "38 42 50 53", "8BPS", "0", "Photoshop 工程文件。", "版本字段通常是 00 01。"),
		R("image", "XCF", ".xcf", "67 69 6D 70 20 78 63 66 20", "gimp xcf ", "0", "GIMP 工程文件。", ""),
		R("image", "AVIF", ".avif", "00 00 00 ?? 66 74 79 70 61 76 69 66", "....ftypavif", "0", "基于 AV1 的图片，ISO BMFF 容器。", "偏移 4 是 ftyp，品牌字段是 avif。"),
		R("image", "HEIC", ".heic", "00 00 00 ?? 66 74 79 70 68 65 69 63", "....ftypheic", "0", "苹果设备常用的 HEIC 图片。", "品牌也可能是 heix、hevc、mif1。"),
		R("image", "JPEG XL", ".jxl", "FF 0A", "..", "0", "JPEG XL 码流。", "部分文件是 ISO BMFF 容器，头为 00 00 00 0C 4A 58 4C 20。"),
		R("image", "SVG", ".svg", "3C 73 76 67", "<svg", "0", "文本 XML 矢量图。", "不是二进制魔数；也可能以 <?xml 开头。"),

		R("doc", "PDF", ".pdf", "25 50 44 46 2D", "%PDF-", "0", "PDF 文档。", "后面紧跟版本号，如 1.7。"),
		R("doc", "RTF", ".rtf", "7B 5C 72 74 66 31", "{\\rtf1", "0", "RTF 富文本。", ""),
		R("doc", "XML", ".xml", "3C 3F 78 6D 6C", "<?xml", "0", "XML 文档。", "也有文件直接以根元素开头。"),
		R("doc", "HTML", ".html .htm", "3C 21 44 4F 43 54 59 50 45", "<!DOCTYPE", "0", "HTML 页面。", "也可能直接以 <html 开头。"),
		R("doc", "OLE 复合文档", ".doc .xls .ppt .msi .msg", "D0 CF 11 E0 A1 B1 1A E1", "........", "0", "旧版 Office、MSI、Outlook 邮件共用。", "新版 docx/xlsx/pptx 是 ZIP，开头为 PK。"),
		R("doc", "PostScript", ".ps", "25 21 50 53", "%!PS", "0", "PostScript 文档。", ""),
		R("doc", "EPS", ".eps", "25 21 50 53 2D 41 64 6F 62 65", "%!PS-Adobe", "0", "封装的 PostScript。", ""),
		R("doc", "MOBI", ".mobi .azw", "42 4F 4F 4B 4D 4F 42 49", "BOOKMOBI", "0", "Mobi / 旧 Kindle 电子书。", "偏移 60 附近通常还有 BOOKMOBI。"),
		R("doc", "DICOM", ".dcm", "44 49 43 4D", "DICM", "128", "医学影像文件。", "前 128 字节是导言，之后才是 DICM。"),
		R("doc", "CHM", ".chm", "49 54 53 46", "ITSF", "0", "Windows 帮助文档。", ""),
		R("doc", "Windows 快捷方式", ".lnk", "4C 00 00 00 01 14 02 00", "L.......", "0", "Windows .lnk 快捷方式。", ""),
		R("doc", "EVTX", ".evtx", "45 6C 66 46 69 6C 65 00", "ElfFile.", "0", "Windows 事件日志。", ""),
		R("doc", "注册表配置单元", ".reg? .hiv", "72 65 67 66", "regf", "0", "Windows 注册表 hive 文件。", "不是导出的 .reg 文本。"),
		R("doc", "Outlook 数据", ".pst", "21 42 4E 61", "!BNa", "0", "Outlook 个人文件夹。", ""),

		R("archive", "ZIP", ".zip .docx .xlsx .pptx .jar .apk .epub", "50 4B 03 04", "PK..", "0", "ZIP 压缩包。", "新版 Office、JAR、APK、EPUB 本质都是 ZIP。"),
		R("archive", "空 ZIP", ".zip", "50 4B 05 06", "PK..", "0", "没有文件的 ZIP。", ""),
		R("archive", "RAR 4", ".rar", "52 61 72 21 1A 07 00", "Rar!...", "0", "RAR 4.x 压缩包。", ""),
		R("archive", "RAR 5", ".rar", "52 61 72 21 1A 07 01 00", "Rar!....", "0", "RAR 5.x 压缩包。", ""),
		R("archive", "7z", ".7z", "37 7A BC AF 27 1C", "7z...'", "0", "7-Zip 压缩包。", ""),
		R("archive", "GZIP", ".gz .tgz", "1F 8B 08", "...", "0", "GZIP 压缩。", ".tgz 是 gzip 后的 tar。"),
		R("archive", "BZIP2", ".bz2", "42 5A 68", "BZh", "0", "BZIP2 压缩。", ""),
		R("archive", "XZ", ".xz", "FD 37 7A 58 5A 00", "ý7zXZ.", "0", "XZ 压缩。", "Linux 发行包常用。"),
		R("archive", "Zstandard", ".zst", "28 B5 2F FD", "(µ/ý", "0", "Zstandard 压缩。", "新版 tar 也支持 zstd。"),
		R("archive", "TAR", ".tar", "75 73 74 61 72", "ustar", "257", "TAR 归档。", "魔数在偏移 257，不在文件开头。"),
		R("archive", "CAB", ".cab", "4D 53 43 46", "MSCF", "0", "Windows 安装包常用压缩格式。", ""),
		R("archive", "RPM", ".rpm", "ED AB EE DB", "....", "0", "RPM 安装包。", ""),
		R("archive", "DEB", ".deb", "21 3C 61 72 63 68 3E 0A", "!<arch>.", "0", "Debian/Ubuntu 安装包。", "本质是 ar 归档。"),
		R("archive", "ISO 9660", ".iso", "43 44 30 30 31", "CD001", "32769", "光盘映像。", "也可能在 34817 等其它会话偏移出现。"),
		R("archive", "QCOW2", ".qcow2", "51 46 49 FB", "QFI.", "0", "QEMU 磁盘映像。", ""),
		R("archive", "VMDK", ".vmdk", "4B 44 4D 56", "KDMV", "0", "VMware 磁盘映像。", ""),
		R("archive", "VHDX", ".vhdx", "76 68 64 78 66 69 6C 65", "vhdxfile", "0", "Hyper-V 虚拟磁盘。", ""),

		R("media", "WAV", ".wav", "52 49 46 46 ?? ?? ?? ?? 57 41 56 45", "RIFF....WAVE", "0", "未压缩或压缩的 WAV 音频。", "偏移 8–11 固定为 WAVE。"),
		R("media", "AVI", ".avi", "52 49 46 46 ?? ?? ?? ?? 41 56 49 20", "RIFF....AVI ", "0", "AVI 视频。", "偏移 8–11 固定为 AVI 加空格。"),
		R("media", "MP3（ID3）", ".mp3", "49 44 33", "ID3", "0", "带 ID3 标签的 MP3。", "标签后面才是音频帧。"),
		R("media", "MP3（帧头）", ".mp3", "FF FB", "...", "0", "没有 ID3 标签时直接是 MPEG 音频帧。", "帧头也可能是 FF F3、FF F2。"),
		R("media", "FLAC", ".flac", "66 4C 61 43", "fLaC", "0", "无损音频。", ""),
		R("media", "Ogg", ".ogg .oga .ogv", "4F 67 67 53", "OggS", "0", "Ogg 容器，可装 Vorbis、Opus、Theora。", ""),
		R("media", "ISO BMFF", ".mp4 .mov .m4a .3gp", "66 74 79 70", "ftyp", "4", "MP4、MOV、M4A、3GP 等共用容器。", "偏移 4 是 ftyp，品牌字段区分具体格式。"),
		R("media", "Matroska / WebM", ".mkv .webm", "1A 45 DF A3", "..E.", "0", "MKV 和 WebM 共用容器。", ""),
		R("media", "FLV", ".flv", "46 4C 56 01", "FLV.", "0", "Flash 视频。", ""),
		R("media", "ASF / WMV", ".wmv .wma", "30 26 B2 75 8E 66 CF 11 A6 D9 00 AA 00 62 CE 6C", "0&µ.", "0", "Windows Media 容器。", ""),
		R("media", "MIDI", ".mid .midi", "4D 54 68 64", "MThd", "0", "MIDI 文件头。", ""),
		R("media", "AIFF", ".aif .aiff", "46 4F 52 4D ?? ?? ?? ?? 41 49 46 46", "FORM....AIFF", "0", "苹果无损音频。", ""),

		R("font", "WOFF", ".woff", "77 4F 46 46", "wOFF", "0", "网页字体，压缩的 OpenType。", ""),
		R("font", "WOFF2", ".woff2", "77 4F 46 32", "wOF2", "0", "压缩率更高的网页字体。", ""),
		R("font", "TrueType", ".ttf", "00 01 00 00 00", ".....", "0", "TrueType 字体。", "也可能以 74 72 75 65（true）开头。"),
		R("font", "OpenType", ".otf", "4F 54 54 4F", "OTTO", "0", "OpenType 字体。", ""),
		R("font", "字体集合", ".ttc", "74 74 63 66", "ttcf", "0", "一个文件包含多个字体。", ""),

		R("exec", "Windows PE", ".exe .dll .sys", "4D 5A", "MZ", "0", "Windows 可执行文件和动态库。", "MZ 是 DOS 头，真正的 PE 头在 e_lfanew 指向的位置。"),
		R("exec", "ELF", ".elf .so .o", "7F 45 4C 46", ".ELF", "0", "Linux / Unix 可执行文件和共享库。", ""),
		R("exec", "Mach-O 64 小端", ".dylib .bin", "CF FA ED FE", "....", "0", "macOS 64 位可执行文件或库。", ""),
		R("exec", "Mach-O 64 大端", ".dylib .bin", "FE ED FA CF", "....", "0", "大端 Mach-O 64 位文件。", ""),
		R("exec", "Mach-O 通用", ".dylib .bin", "CA FE BA BE", "....", "0", "包含多种架构的 Mach-O。", "和 Java class 魔数相同，需结合上下文判断。"),
		R("exec", "Java class", ".class", "CA FE BA BE", "....", "0", "Java 字节码。", "后面紧跟版本号和常量池。"),
		R("exec", "WebAssembly", ".wasm", "00 61 73 6D", ".asm", "0", "WebAssembly 模块。", ""),
		R("exec", "Dalvik DEX", ".dex", "64 65 78 0A 30 33 35 00", "dex.035.", "0", "Android DEX 字节码。", "版本号会变，如 038、039。"),
		R("exec", "SWF", ".swf", "46 57 53", "FWS", "0", "未压缩的 Flash 文件。", "压缩版以 CWS（43 57 53）开头。"),

		R("data", "SQLite", ".db .sqlite .sqlite3", "53 51 4C 69 74 65 20 66 6F 72 6D 61 74 20 33 00", "SQLite format 3.", "0", "SQLite 数据库。", ""),
		R("data", "HDF5", ".h5 .hdf5", "89 48 44 46 0D 0A 1A 0A", ".HDF....", "0", "HDF5 科学数据。", ""),
		R("data", "FITS", ".fits", "53 49 4D 50 4C 45 20 20 3D", "SIMPLE  =", "0", "天文学 FITS 数据。", ""),
		R("data", "Parquet", ".parquet", "50 41 52 31", "PAR1", "0", "列式存储文件，结尾也有 PAR1。", ""),
		R("data", "ORC", ".orc", "4F 52 43", "ORC", "0", "Hive ORC 列式文件。", ""),
		R("data", "Avro", ".avro", "4F 62 6A 01", "Obj.", "0", "Avro 数据文件。", ""),
		R("data", "Arrow", ".arrow .feather", "41 52 52 4F 57 31", "ARROW1", "0", "Apache Arrow 文件。", ""),
		R("data", "PCAP", ".pcap", "D4 C3 B2 A1", "....", "0", "经典抓包文件。", "也可能是 A1 B2 C3 D4（另一种字节序）。"),
		R("data", "PCAPNG", ".pcapng", "0A 0D 0D 0A", "....", "0", "新版抓包文件。", ""),
		R("data", "BitTorrent", ".torrent", "64 38 3A 61 6E 6E 6F 75 6E 63 65", "d8:announce", "0", "BT 种子文件。", "本质是 bencode 文本。"),

		R("other", "Blender", ".blend", "42 4C 45 4E 44 45 52", "BLENDER", "0", "Blender 工程文件。", ""),
		R("other", "glTF Binary", ".glb", "67 6C 54 46", "glTF", "0", "单文件 glTF 模型。", ""),
		R("other", "AutoCAD DWG", ".dwg", "41 43 31 30 31 35", "AC1015", "0", "AutoCAD 图形文件。", "版本不同，如 AC1018、AC1021、AC1024、AC1027、AC1032。")
	];

	SK.regref("u-magic", {
		title: "文件头魔数表",
		cols: [
			{ name: "类型" },
			{ name: "扩展名", mono: true, nowrap: true },
			{ name: "魔数（Hex）", mono: true },
			{ name: "ASCII", mono: true, nowrap: true }
		],
		widths: ["150px", "150px", "240px", ""],
		groups: [
			{ id: "image", name: "图片" },
			{ id: "doc", name: "文档" },
			{ id: "archive", name: "压缩包 / 镜像" },
			{ id: "media", name: "音视频" },
			{ id: "font", name: "字体" },
			{ id: "exec", name: "可执行 / 字节码" },
			{ id: "data", name: "数据 / 数据库" },
			{ id: "other", name: "其他" }
		],
		rows: ROWS,
		placeholder: "搜索：jpg、ffd8、PK、PDF、图片",
		big: 0,
		sub: 1,
		copy: 2,
		defaultPick: "PNG",
		sample: function(r){
			return "Hex: " + r.c[2] + "\nASCII: " + r.c[3];
		}
	});
})();
