using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScreenKit;

/// <summary>
/// 在文本中高亮与搜索关键词匹配的子串（非整行背景）。
/// Query 来自 <see cref="SearchHighlight.Query"/> 或自身 Query 属性。
/// </summary>
public class HighlightTextBlock : TextBlock {
	public new static readonly DependencyProperty TextProperty =
		DependencyProperty.Register(nameof(Text), typeof(string), typeof(HighlightTextBlock),
			new PropertyMetadata("", onChanged));

	public static readonly DependencyProperty QueryProperty =
		DependencyProperty.Register(nameof(Query), typeof(string), typeof(HighlightTextBlock),
			new PropertyMetadata("", onChanged));

	static readonly Brush HitBg = freeze(new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)));
	static readonly Brush HitFg = freeze(new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)));

	public new string Text {
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public string Query {
		get => (string)GetValue(QueryProperty);
		set => SetValue(QueryProperty, value);
	}

	static void onChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is HighlightTextBlock h) h.rebuild();
	}

	public HighlightTextBlock() {
		VerticalAlignment = VerticalAlignment.Center;
		SearchHighlight.QueryChanged += onGlobalQuery;
		Unloaded += (_, _) => SearchHighlight.QueryChanged -= onGlobalQuery;
	}

	void onGlobalQuery() => rebuild();

	void rebuild() {
		Inlines.Clear();
		var text = Text ?? "";
		var q = Query;
		if (string.IsNullOrEmpty(q))
			q = SearchHighlight.Query;
		if (string.IsNullOrEmpty(text)) return;
		if (string.IsNullOrEmpty(q)) {
			Inlines.Add(new Run(text));
			return;
		}

		var idx = 0;
		while (idx < text.Length) {
			var found = text.IndexOf(q, idx, StringComparison.OrdinalIgnoreCase);
			if (found < 0) {
				Inlines.Add(new Run(text[idx..]));
				break;
			}
			if (found > idx)
				Inlines.Add(new Run(text[idx..found]));
			var hit = text.Substring(found, q.Length);
			Inlines.Add(new Run(hit) {
				Background = HitBg,
				Foreground = HitFg,
				FontWeight = FontWeights.SemiBold
			});
			idx = found + q.Length;
		}
	}

	static SolidColorBrush freeze(SolidColorBrush b) {
		if (b.CanFreeze) b.Freeze();
		return b;
	}
}

/// <summary>全局搜索关键词，供表格单元格文字高亮。</summary>
public static class SearchHighlight {
	static string query = "";
	public static event Action QueryChanged;

	public static string Query {
		get => query;
		set {
			var v = value ?? "";
			if (query == v) return;
			query = v;
			QueryChanged?.Invoke();
		}
	}
}
