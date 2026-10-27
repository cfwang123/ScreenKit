namespace ScreenKit;

/// <summary>GridViewColumn 的附加属性：点列头排序时用哪个属性排名（显示文本和排序键不一致时用）。</summary>
static class GridViewSort {
	public static readonly DependencyProperty SortPathProperty =
		DependencyProperty.RegisterAttached("SortPath", typeof(string), typeof(GridViewSort),
			new PropertyMetadata(""));

	public static string GetSortPath(DependencyObject o) => (string)o.GetValue(SortPathProperty);

	public static void SetSortPath(DependencyObject o, string value) => o.SetValue(SortPathProperty, value);
}
