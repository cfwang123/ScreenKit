using System;

namespace ScreenKit.DocView;

static class ViewerFactory {
	public static IDocViewer Create(DocKind kind) {
		switch (kind) {
			case DocKind.Pdf: return new PdfViewer();
			case DocKind.Docx: return new DocxViewer();
			case DocKind.Xlsx: return new XlsxViewer();
			case DocKind.Txt: return new TextViewer();
			case DocKind.Md: return new MdViewer();
			case DocKind.Image: return new ImageViewer();
			case DocKind.Csv: return new CsvViewer();
			case DocKind.Browser: return new BrowserViewer();
			case DocKind.Console: return new ConsoleViewer();
			default: throw new NotSupportedException("不支持的文件类型");
		}
	}
}
