using System;
using System.Runtime.InteropServices;

namespace ScreenKit.DocView;

/// <summary>pdfium C API（与 PDFtoImage 共用 pdfium.dll，含完整编辑导出）。</summary>
static class PdfiumNative {
	public const int FPDFBitmap_BGRA = 4;
	public const int FPDF_ANNOT = 0x01;
	public const int FPDF_LCD_TEXT = 0x02;
	public const int FPDF_NO_NATIVETEXT = 0x04;
	public const int FPDF_GRAYSCALE = 0x08;
	public const int FPDF_RENDER_LIMITEDIMAGECACHE = 0x200;
	public const int FPDF_RENDER_FORCEHALFTONE = 0x400;
	public const int FPDF_PRINTING = 0x800;
	public const int FPDF_RENDER_NO_SMOOTHTEXT = 0x1000;
	public const int FPDF_RENDER_NO_SMOOTHIMAGE = 0x2000;
	public const int FPDF_RENDER_NO_SMOOTHPATH = 0x4000;
	public const int FPDF_REVERSE_BYTE_ORDER = 0x10;

	const CallingConvention CC = CallingConvention.Cdecl;

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDF_InitLibrary();

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDF_DestroyLibrary();

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDF_LoadMemDocument(byte[] data_buf, int size, [MarshalAs(UnmanagedType.LPStr)] string password);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDF_CloseDocument(IntPtr document);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDF_GetPageCount(IntPtr document);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDF_GetPageSizeByIndex(IntPtr document, int page_index, out double width, out double height);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDF_LoadPage(IntPtr document, int page_index);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDF_ClosePage(IntPtr page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern double FPDF_GetPageWidth(IntPtr page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern double FPDF_GetPageHeight(IntPtr page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFBitmap_GetStride(IntPtr bitmap);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page,
		int start_x, int start_y, int size_x, int size_y, int rotate, int flags);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFText_LoadPage(IntPtr page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFText_ClosePage(IntPtr text_page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFText_CountChars(IntPtr text_page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern uint FPDFText_GetUnicode(IntPtr text_page, int index);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFText_GetCharBox(IntPtr text_page, int index,
		out double left, out double right, out double bottom, out double top);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr document, IntPtr bookmark);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern uint FPDFBookmark_GetTitle(IntPtr bookmark, byte[] buffer, uint buflen);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFBookmark_GetDest(IntPtr document, IntPtr bookmark);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFBookmark_GetAction(IntPtr bookmark);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFAction_GetDest(IntPtr document, IntPtr action);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFDest_GetDestPageIndex(IntPtr document, IntPtr dest);

	/// <summary>读取 /XYZ 等目标的 (x,y,zoom)。坐标为 PDF 用户空间（原点左下，Y 向上）。</summary>
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFDest_GetLocationInPage(IntPtr dest,
		out int hasXVal, out int hasYVal, out int hasZoomVal,
		out float x, out float y, out float zoom);

	// ---------- 页内链接（书内跳转 / URI）----------
	public const uint PDFACTION_UNSUPPORTED = 0;
	public const uint PDFACTION_GOTO = 1;
	public const uint PDFACTION_REMOTEGOTO = 2;
	public const uint PDFACTION_URI = 3;
	public const uint PDFACTION_LAUNCH = 4;

	/// <summary>点命中页内链接。x/y 为 PDF 用户空间（原点左下，Y 向上，单位 pt）。</summary>
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFLink_GetLinkAtPoint(IntPtr page, double x, double y);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFLink_GetDest(IntPtr document, IntPtr link);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFLink_GetAction(IntPtr link);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern uint FPDFAction_GetType(IntPtr action);

	/// <summary>URI 动作目标路径（UTF-8）。buffer=null 时返回所需字节数（含 \0）。</summary>
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern uint FPDFAction_GetURIPath(IntPtr document, IntPtr action, byte[] buffer, uint buflen);

	// ---------- 页对象类型 ----------
	public const int FPDF_PAGEOBJ_TEXT = 1;
	public const int FPDF_PAGEOBJ_PATH = 2;
	public const int FPDF_PAGEOBJ_IMAGE = 3;
	public const int FPDF_PAGEOBJ_SHADING = 4;
	public const int FPDF_PAGEOBJ_FORM = 5;

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPage_CountObjects(IntPtr page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_GetType(IntPtr page_object);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_GetBounds(IntPtr page_object,
		out float left, out float bottom, out float right, out float top);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFImageObj_GetBitmap(IntPtr image_object);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFImageObj_GetRenderedBitmap(IntPtr document, IntPtr page, IntPtr image_object);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFBitmap_GetWidth(IntPtr bitmap);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFBitmap_GetHeight(IntPtr bitmap);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFBitmap_GetFormat(IntPtr bitmap);

	// ---------- 文档 / 页级编辑 ----------
	public const int FPDF_NO_INCREMENTAL = 1;
	public const int FPDF_FONT_TYPE1 = 1;
	public const int FPDF_FONT_TRUETYPE = 2;

	[StructLayout(LayoutKind.Sequential)]
	public struct FS_MATRIX {
		public float a, b, c, d, e, f;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct FPDF_FILEWRITE {
		public int version;
		public IntPtr WriteBlock;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate int FPDF_WriteBlock(IntPtr pThis, IntPtr pData, uint size);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDF_SaveAsCopy(IntPtr document, ref FPDF_FILEWRITE pFileWrite, int flags);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDF_SaveWithVersion(IntPtr document, ref FPDF_FILEWRITE pFileWrite, int flags, int fileVersion);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDF_CreateNewDocument();

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDF_ImportPages(IntPtr dest_doc, IntPtr src_doc,
		[MarshalAs(UnmanagedType.LPStr)] string pagerange, int index);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFPage_New(IntPtr document, int page_index, double width, double height);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFPage_Delete(IntPtr document, int page_index);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPage_GetRotation(IntPtr page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFPage_SetRotation(IntPtr page, int rotate);

	// ---------- 页对象增删改 ----------
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFPage_InsertObject(IntPtr page, IntPtr page_obj);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPage_RemoveObject(IntPtr page, IntPtr page_obj);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPage_GenerateContent(IntPtr page);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFPageObj_Destroy(IntPtr page_obj);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFPageObj_Transform(IntPtr page_object,
		double a, double b, double c, double d, double e, double f);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_GetMatrix(IntPtr page_object, out FS_MATRIX matrix);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_SetMatrix(IntPtr page_object, ref FS_MATRIX matrix);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_SetFillColor(IntPtr page_object,
		uint r, uint g, uint b, uint a);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_GetFillColor(IntPtr page_object,
		out uint r, out uint g, out uint b, out uint a);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_SetStrokeColor(IntPtr page_object,
		uint r, uint g, uint b, uint a);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_GetStrokeColor(IntPtr page_object,
		out uint r, out uint g, out uint b, out uint a);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPageObj_SetStrokeWidth(IntPtr page_object, float width);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern float FPDFPageObj_GetStrokeWidth(IntPtr page_object);

	// ---------- 文字 / 字体 ----------
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFPageObj_NewTextObj(IntPtr document,
		[MarshalAs(UnmanagedType.LPStr)] string font, float font_size);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFPageObj_CreateTextObj(IntPtr document, IntPtr font, float font_size);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFText_SetText(IntPtr text_object,
		[MarshalAs(UnmanagedType.LPWStr)] string text);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern float FPDFTextObj_GetFontSize(IntPtr text);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern uint FPDFTextObj_GetText(IntPtr text_object, IntPtr text_page,
		[Out] char[] buffer, uint length);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFTextObj_GetFont(IntPtr text);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFTextObj_GetTextRenderMode(IntPtr text);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFTextObj_SetTextRenderMode(IntPtr text, int render_mode);

	/// <summary>加载嵌入字体。font_type: 1=Type1 2=TrueType；cid=1 支持中日韩。</summary>
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFText_LoadFont(IntPtr document, byte[] data, uint size, int font_type, int cid);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFText_LoadStandardFont(IntPtr document,
		[MarshalAs(UnmanagedType.LPStr)] string font);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern void FPDFFont_Close(IntPtr font);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern uint FPDFFont_GetFamilyName(IntPtr font, byte[] buffer, uint length);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern uint FPDFFont_GetBaseFontName(IntPtr font, byte[] buffer, uint length);

	/// <summary>读取嵌入字体原始数据。buffer=null 时返回所需字节数。</summary>
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern uint FPDFFont_GetFontData(IntPtr font, byte[] buffer, uint buflen);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFFont_GetIsEmbedded(IntPtr font);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFFont_GetFlags(IntPtr font);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFFont_GetWeight(IntPtr font);

	// ---------- 图片 ----------
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFPageObj_NewImageObj(IntPtr document);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFImageObj_SetBitmap(IntPtr[] pages, int count,
		IntPtr image_object, IntPtr bitmap);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFImageObj_SetMatrix(IntPtr image_object,
		double a, double b, double c, double d, double e, double f);

	// ---------- 路径 / 矩形 ----------
	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFPageObj_CreateNewRect(float x, float y, float w, float h);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFPageObj_CreateNewPath(float x, float y);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPath_SetDrawMode(IntPtr path, int fillmode, int stroke);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPath_MoveTo(IntPtr path, float x, float y);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPath_LineTo(IntPtr path, float x, float y);

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern int FPDFPath_Close(IntPtr path);

	// fillmode: 0=None 1=Alternate 2=Winding
	public const int FPDF_FILLMODE_NONE = 0;
	public const int FPDF_FILLMODE_ALTERNATE = 1;
	public const int FPDF_FILLMODE_WINDING = 2;

	[DllImport("pdfium", CallingConvention = CC)]
	public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format,
		IntPtr first_scan, int stride);

	public const int FPDFBitmap_BGR = 2;
	public const int FPDFBitmap_BGRx = 3;
}
