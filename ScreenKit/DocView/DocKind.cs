namespace ScreenKit.DocView;

enum DocKind {
	Unknown = 0,
	Pdf = 1,
	Docx = 2,
	Xlsx = 3,
	/// <summary>纯文本 / 代码（.txt .py .cs .js …），共用 TextViewer。</summary>
	Txt = 4,
	Md = 5,
	/// <summary>图片预览（.png .jpg …）。</summary>
	Image = 6,
	/// <summary>CSV / TSV 表格预览。</summary>
	Csv = 7,
	/// <summary>浏览器标签（Edge WebView2）。</summary>
	Browser = 8,
	/// <summary>命令行标签（WPF 模拟终端，托管 cmd/PowerShell）。</summary>
	Console = 9,
}

static class DocKindUtil {
	public static DocKind FromPath(string path) {
		if (string.IsNullOrWhiteSpace(path)) return DocKind.Unknown;
		var ext = System.IO.Path.GetExtension(path);
		if (string.IsNullOrEmpty(ext)) return DocKind.Unknown;
		switch (ext.ToLowerInvariant()) {
			case ".pdf": return DocKind.Pdf;
			case ".doc":
			case ".docx": return DocKind.Docx;
			case ".xls":
			case ".xlsx":
			case ".xlsm": return DocKind.Xlsx;
			case ".md":
			case ".markdown":
			case ".mdown": return DocKind.Md;
			// 图片
			case ".png":
			case ".jpg":
			case ".jpeg":
			case ".gif":
			case ".bmp":
			case ".ico":
			case ".tif":
			case ".tiff":
			case ".webp": return DocKind.Image;
			// 纯文本 + 常见代码
			case ".txt":
			case ".log":
			case ".text":
			case ".py":
			case ".pyw":
			case ".php":
			case ".lua":
			case ".cs":
			case ".js":
			case ".mjs":
			case ".cjs":
			case ".ts":
			case ".jsx":
			case ".tsx":
			case ".html":
			case ".htm":
			case ".css":
			case ".scss":
			case ".less":
			case ".json":
			case ".xml":
			case ".yaml":
			case ".yml":
			case ".toml":
			case ".ini":
			case ".cfg":
			case ".conf":
			case ".sql":
			case ".sh":
			case ".bash":
			case ".bat":
			case ".cmd":
			case ".ps1":
			case ".go":
			case ".rs":
			case ".java":
			case ".kt":
			case ".c":
			case ".h":
			case ".cpp":
			case ".cc":
			case ".cxx":
			case ".hpp":
			case ".hxx":
			case ".rb":
			case ".pl":
			case ".r":
			case ".swift":
			case ".m":
			case ".mm":
			case ".vue":
			case ".svelte":
			case ".dart":
			case ".gradle":
			case ".cmake":
			case ".makefile":
			case ".mk":
			case ".diff":
			case ".patch":
			case ".gitignore":
			case ".dockerfile":
			case ".env":
			case ".editorconfig": return DocKind.Txt;
			case ".csv":
			case ".tsv": return DocKind.Csv;
			default: return DocKind.Unknown;
		}
	}

	/// <summary>打开对话框筛选：文档 / 代码 / 图片 / 全部分组。</summary>
	public static string Filter =>
		"支持的文件|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.xlsm;*.csv;*.tsv;*.txt;*.md;*.markdown"
		+ ";*.py;*.php;*.lua;*.cs;*.js;*.ts;*.html;*.htm;*.css;*.json;*.xml;*.sql;*.java;*.go;*.rs;*.c;*.cpp;*.h"
		+ ";*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico;*.tif;*.tiff;*.webp"
		+ "|PDF|*.pdf|Word|*.doc;*.docx|Excel|*.xls;*.xlsx;*.xlsm|CSV|*.csv;*.tsv|Markdown|*.md;*.markdown"
		+ "|文本与代码|*.txt;*.log;*.py;*.php;*.lua;*.cs;*.js;*.ts;*.jsx;*.tsx;*.html;*.htm;*.css;*.scss;*.json;*.xml;*.yaml;*.yml;*.sql;*.sh;*.bat;*.ps1;*.java;*.go;*.rs;*.c;*.cpp;*.h;*.hpp"
		+ "|图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico;*.tif;*.tiff;*.webp"
		+ "|所有文件|*.*";
}
