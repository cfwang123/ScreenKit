namespace System.Text.Encodings.Web;

/// <summary>只为保留原来的 JsonSerializerOptions.Encoder 调用形状，实际转义由 JSONF 完成。</summary>
public class JavaScriptEncoder {
	public static JavaScriptEncoder UnsafeRelaxedJsonEscaping { get; } = new();
	public static JavaScriptEncoder Default { get; } = new();
	public static JavaScriptEncoder Create() => new();
}
