using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>工具按钮：Segoe MDL2 图标 + 文字。</summary>
static class ToolBtnUi {
	public static readonly FontFamily Mdl2 = new("Segoe MDL2 Assets");
	public const string Add = "\uE710";
	public const string Folder = "\uE8B7";
	public const string Delete = "\uE74D";
	public const string Clear = "\uE894";
	public const string Rename = "\uE8AC";
	public const string Close = "\uE711";
	public const string Copy = "\uE8C8";
	public const string Save = "\uE74E";
	public const string Browse = "\uE8A7";
	public const string Play = "\uE768";
	public const string Cancel = "\uE711";
	public const string Encode = "\uE8AB";
	public const string Decode = "\uE72B";
	public const string Font = "\uE8D2";
	public const string Swap = "\uE8AB";

	public static void Set(Button b, string glyph, string text) {
		if (b == null) return;
		var sp = new StackPanel { Orientation = Orientation.Horizontal };
		if (!string.IsNullOrEmpty(glyph)) {
			sp.Children.Add(new TextBlock {
				Text = glyph,
				FontFamily = Mdl2,
				FontSize = 14,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 1, 6, 0),
			});
		}
		sp.Children.Add(new TextBlock {
			Text = text ?? "",
			VerticalAlignment = VerticalAlignment.Center,
		});
		b.Content = sp;
	}
}
