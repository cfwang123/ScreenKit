using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace ScreenKit.DocView;

/// <summary>共用 UI 样式（目录选中等），跟随当前主题与字号。</summary>
static class UiStyles {
	/// <summary>约 2 个英文字符宽的目录缩进（随界面字号，≈1em）。</summary>
	public static double TocIndent {
		get {
			var fs = 12.0;
			try { fs = DocViewOptions.Current.UiFontSize; } catch { /* ignore */ }
			// 1em ≈ 两个半角英文字符宽；勿再乘 1.1 以免偏大
			return Math.Max(10, Math.Round(fs));
		}
	}

	/// <summary>
	/// 目录 TreeView：选中用主题强调色；缩进约 2 英文字符；无横向滚动。
	/// </summary>
	public static void ApplyTocTree(TreeView tree) {
		if (tree == null) return;
		var t = ThemeService.Current ?? AppTheme.Get(0);
		var selBg = solid(t.Accent);
		var selBgDark = solid(t.AccentDark);
		var hoverBg = solid(t.AccentSoft);
		var textNormal = solid(t.TextPrimary);
		var selFg = islight(t.Accent) ? solid(Colors.Black) : Brushes.White;

		tree.Resources[SystemColors.HighlightBrushKey] = selBg;
		tree.Resources[SystemColors.HighlightTextBrushKey] = selFg;
		tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selBg;
		tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = selFg;
		tree.Resources[SystemColors.ControlBrushKey] = hoverBg;

		var style = toctreeitemstyle(selBg, selBgDark, hoverBg, textNormal, selFg);
		tree.Resources[typeof(TreeViewItem)] = style;
		tree.ItemContainerStyle = style;
	}

	static Style toctreeitemstyle(Brush selBg, Brush selBgDark, Brush hoverBg, Brush textNormal, Brush selFg) {
		var style = new Style(typeof(TreeViewItem));
		var fs = 12.0;
		try { fs = DocViewOptions.Current.UiFontSize; } catch { /* ignore */ }
		var indent = TocIndent;

		style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 4, 1)));
		style.Setters.Add(new Setter(Control.ForegroundProperty, textNormal));
		style.Setters.Add(new Setter(Control.FontSizeProperty, fs));
		style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
		style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
		style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
		style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));

		// 紧凑模板：每层仅缩进一次（ItemsHost 跨列 + 左边距 indent，勿再叠 Column=1）
		try {
			var tmpl = (ControlTemplate)XamlReader.Parse(
				$"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TreeViewItem'>" +
				"<Grid>" +
				"<Grid.ColumnDefinitions>" +
				$"<ColumnDefinition Width='{indent}'/>" +
				"<ColumnDefinition Width='*'/>" +
				"</Grid.ColumnDefinitions>" +
				"<Grid.RowDefinitions>" +
				"<RowDefinition Height='Auto'/>" +
				"<RowDefinition Height='Auto'/>" +
				"</Grid.RowDefinitions>" +
				"<ToggleButton x:Name='Expander' ClickMode='Press' Focusable='False'" +
				$" Width='{indent}' Height='{indent + 2}' VerticalAlignment='Center'" +
				" IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}}'>" +
				"<ToggleButton.Template>" +
				"<ControlTemplate TargetType='ToggleButton'>" +
				"<Border Background='Transparent'>" +
				"<Path x:Name='Arrow' Data='M 0 0 L 4 3.5 L 0 7 Z' Fill='#6B7280'" +
				" HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
				"</Border>" +
				"<ControlTemplate.Triggers>" +
				"<Trigger Property='IsChecked' Value='True'>" +
				"<Setter TargetName='Arrow' Property='Data' Value='M 0 0 L 7 0 L 3.5 4 Z'/>" +
				"</Trigger>" +
				"</ControlTemplate.Triggers>" +
				"</ControlTemplate>" +
				"</ToggleButton.Template>" +
				"</ToggleButton>" +
				"<Border x:Name='Bd' Grid.Column='1' Background='Transparent' Padding='2,1,4,1'>" +
				"<ContentPresenter x:Name='PART_Header' ContentSource='Header'" +
				" HorizontalAlignment='Stretch' VerticalAlignment='Center'/>" +
				"</Border>" +
				// 子项：从左边缘起只加 indent，避免「箭头列 + 再 margin」双倍缩进
				$"<ItemsPresenter x:Name='ItemsHost' Grid.Row='1' Grid.Column='0' Grid.ColumnSpan='2' Margin='{indent},0,0,0'/>" +
				"</Grid>" +
				"<ControlTemplate.Triggers>" +
				"<Trigger Property='IsExpanded' Value='False'>" +
				"<Setter TargetName='ItemsHost' Property='Visibility' Value='Collapsed'/>" +
				"</Trigger>" +
				"<Trigger Property='HasItems' Value='False'>" +
				"<Setter TargetName='Expander' Property='Visibility' Value='Hidden'/>" +
				"</Trigger>" +
				"<Trigger Property='IsMouseOver' Value='True'>" +
				"<Setter TargetName='Bd' Property='Background' Value='{DynamicResource AccentSoft}'/>" +
				"</Trigger>" +
				"<Trigger Property='IsSelected' Value='True'>" +
				"<Setter TargetName='Bd' Property='Background' Value='{DynamicResource Accent}'/>" +
				"<Setter Property='Foreground' Value='White'/>" +
				"</Trigger>" +
				"</ControlTemplate.Triggers>" +
				"</ControlTemplate>");
			style.Setters.Add(new Setter(Control.TemplateProperty, tmpl));
		} catch {
			// 模板解析失败时退回默认（缩进会大一些）
		}

		// 额外触发器（模板外选中字色等）
		var sel = new Trigger {
			Property = TreeViewItem.IsSelectedProperty,
			Value = true,
		};
		sel.Setters.Add(new Setter(Control.ForegroundProperty, selFg));
		style.Triggers.Add(sel);

		return style;
	}

	static SolidColorBrush solid(Color c) {
		var br = new SolidColorBrush(c);
		br.Freeze();
		return br;
	}

	static bool islight(Color c) {
		var y = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
		return y > 0.6;
	}
}
