using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace ScreenKit.DocView;

/// <summary>
/// 移植层自测：用临时文件跑一遍各阅读器的解析与排版，不建窗口。
/// 由 --test-mdread 调用，输出每项结果。
/// </summary>
static class DocViewSelfTest {
	public static int Run(Action<string> log, string pdfSample = null) {
		var say = log ?? (_ => { });
		var dir = Path.Combine(Path.GetTempPath(), "ScreenKit_docview_test");
		try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
		Directory.CreateDirectory(dir);
		try {
			if (!testmd(say, dir)) return 1;
			if (!testtxt(say, dir)) return 1;
			if (!testcsv(say, dir)) return 1;
			if (!testimage(say, dir)) return 1;
			if (!testdocx(say, dir)) return 1;
			if (!testxlsx(say, dir)) return 1;
			if (!string.IsNullOrEmpty(pdfSample) && !testpdf(say, pdfSample)) return 1;
			say("OK");
			return 0;
		}
		catch (Exception ex) {
			say("FAIL: " + ex);
			return 1;
		}
		finally {
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	static bool testmd(Action<string> say, string dir) {
		var path = Path.Combine(dir, "t.md");
		File.WriteAllText(path, """
			# 标题一

			正文**粗体**和 `code`。

			- 甲
			- 乙

			| A | B |
			|---|---|
			| 1 | 2 |

			```csharp
			var x = 1;
			```
			""", new System.Text.UTF8Encoding(false));
		var doc = MdParser.Parse(File.ReadAllText(path));
		if (doc.Blocks.Count == 0) return bad(say, "md 解析为空");
		var heads = 0;
		var tables = 0;
		var codes = 0;
		foreach (var b in doc.Blocks) {
			if (b.Kind == MdBlockKind.Heading) heads++;
			else if (b.Kind == MdBlockKind.Table) tables++;
			else if (b.Kind == MdBlockKind.Code) codes++;
		}
		if (heads != 1) return bad(say, $"md 标题数 {heads}");
		if (tables != 1) return bad(say, $"md 表格数 {tables}");
		if (codes != 1) return bad(say, $"md 代码块数 {codes}");
		var fd = MdFlowBuilder.Build(doc, 780, null, path, true);
		if (fd == null || fd.Blocks.Count == 0) return bad(say, "md FlowDocument 为空");
		say($"md: {doc.Blocks.Count} 块，{fd.Blocks.Count} 段");
		return true;
	}

	static bool testtxt(Action<string> say, string dir) {
		var path = Path.Combine(dir, "t.txt");
		File.WriteAllText(path, "line1\nline2\nline3\n", new System.Text.UTF8Encoding(false));
		var got = TextFileIo.Load(path);
		if (got == null || string.IsNullOrEmpty(got.Text)) return bad(say, "txt 读不出内容");
		if ((got.Text ?? "").IndexOf("line2", StringComparison.Ordinal) < 0)
			return bad(say, "txt 内容不完整");
		say($"txt: {got.Encoding?.WebName ?? "?"} {got.Text.Length} 字");
		return true;
	}

	static bool testcsv(Action<string> say, string dir) {
		var path = Path.Combine(dir, "t.csv");
		File.WriteAllText(path, "a,b,c\n1,2,3\n", new System.Text.UTF8Encoding(false));
		var v = new CsvViewer();
		try {
			v.Load(path);
			if (v.PageCount < 1) return bad(say, "csv 页数为 0");
			say("csv: " + v.StatusText);
			return true;
		}
		finally { v.Dispose(); }
	}

	static bool testimage(Action<string> say, string dir) {
		var path = Path.Combine(dir, "t.png");
		makepng(path, 32, 24);
		var v = new ImageViewer();
		try {
			v.Load(path);
			if (v.PageCount < 1) return bad(say, "image 页数为 0");
			say("image: " + v.StatusText);
			return true;
		}
		finally { v.Dispose(); }
	}

	static bool testdocx(Action<string> say, string dir) {
		var path = Path.Combine(dir, "t.docx");
		makedocx(path);
		var v = new DocxViewer();
		try {
			v.Load(path);
			if (v.PageCount < 1) return bad(say, "docx 页数为 0");
			say("docx: " + v.StatusText);
			return true;
		}
		finally { v.Dispose(); }
	}

	static bool testxlsx(Action<string> say, string dir) {
		var path = Path.Combine(dir, "t.xlsx");
		makexlsx(path);
		var v = new XlsxViewer();
		try {
			v.Load(path);
			say("xlsx: " + v.StatusText);
			return true;
		}
		finally { v.Dispose(); }
	}

	static bool testpdf(Action<string> say, string path) {
		if (!File.Exists(path)) return bad(say, "pdf 样例不存在: " + path);
		if (!NativeRuntime.HasPdfium()) {
			say("pdf: 跳过（未安装 PDFium）");
			return true;
		}
		var v = new PdfViewer();
		try {
			v.Load(path);
			if (v.PageCount < 1) return bad(say, "pdf 页数为 0");
			say("pdf: " + v.StatusText);
			return true;
		}
		finally { v.Dispose(); }
	}

	static bool bad(Action<string> say, string msg) {
		say("FAIL: " + msg);
		return false;
	}

	// ==================== 现场造样例 ====================
	#region
	static void makepng(string path, int w, int h) {
		var bmp = new System.Windows.Media.Imaging.WriteableBitmap(w, h, 96, 96,
			System.Windows.Media.PixelFormats.Bgra32, null);
		var buf = new byte[w * h * 4];
		for (var i = 0; i < buf.Length; i += 4) {
			buf[i] = 0xDC;
			buf[i + 1] = 0x8C;
			buf[i + 2] = 0x50;
			buf[i + 3] = 0xFF;
		}
		bmp.WritePixels(new Int32Rect(0, 0, w, h), buf, w * 4, 0);
		var enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(bmp));
		using var fs = File.Create(path);
		enc.Save(fs);
	}

	static void makedocx(string path) {
		var doc = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
			  <w:body>
			    <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>自测标题</w:t></w:r></w:p>
			    <w:p><w:r><w:t>正文一段。</w:t></w:r></w:p>
			    <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr>
			  </w:body>
			</w:document>
			""";
		var styles = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
			  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/>
			    <w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style>
			  <w:style w:type="paragraph" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
			</w:styles>
			""";
		var ct = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
			  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
			  <Default Extension="xml" ContentType="application/xml"/>
			  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
			  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
			</Types>
			""";
		var rels = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
			  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
			</Relationships>
			""";
		var drels = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
			  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
			</Relationships>
			""";
		using var z = new System.IO.Compression.ZipArchive(
			File.Create(path), System.IO.Compression.ZipArchiveMode.Create);
		add(z, "[Content_Types].xml", ct);
		add(z, "_rels/.rels", rels);
		add(z, "word/document.xml", doc);
		add(z, "word/styles.xml", styles);
		add(z, "word/_rels/document.xml.rels", drels);
	}

	static void makexlsx(string path) {
		var ct = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
			  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
			  <Default Extension="xml" ContentType="application/xml"/>
			  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
			  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
			  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
			</Types>
			""";
		var rels = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
			  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
			</Relationships>
			""";
		var wb = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
			          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
			  <sheets><sheet name="表一" sheetId="1" r:id="rId1"/></sheets>
			</workbook>
			""";
		var wbrels = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
			  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
			  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
			</Relationships>
			""";
		var sst = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2" uniqueCount="2">
			  <si><t>名称</t></si><si><t>数量</t></si>
			</sst>
			""";
		var sheet = """
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
			  <sheetData>
			    <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
			    <row r="2"><c r="A2" t="inlineStr"><is><t>苹果</t></is></c><c r="B2"><v>12</v></c></row>
			  </sheetData>
			</worksheet>
			""";
		using var z = new System.IO.Compression.ZipArchive(
			File.Create(path), System.IO.Compression.ZipArchiveMode.Create);
		add(z, "[Content_Types].xml", ct);
		add(z, "_rels/.rels", rels);
		add(z, "xl/workbook.xml", wb);
		add(z, "xl/_rels/workbook.xml.rels", wbrels);
		add(z, "xl/sharedStrings.xml", sst);
		add(z, "xl/worksheets/sheet1.xml", sheet);
	}

	static void add(System.IO.Compression.ZipArchive z, string name, string text) {
		var e = z.CreateEntry(name);
		using var s = e.Open();
		var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
		s.Write(bytes, 0, bytes.Length);
	}
	#endregion
}
