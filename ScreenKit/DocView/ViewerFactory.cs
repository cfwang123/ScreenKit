using System;

namespace ScreenKit.DocView;

/// <summary>按文件类型建阅读器。只保留 ScreenKit 文档阅读器用得到的几种。</summary>
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
			default: throw new NotSupportedException("不支持的文件类型");
		}
	}
}
