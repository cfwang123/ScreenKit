using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScreenKit.DocView;

/// <summary>目录侧栏共用：禁止横向滚动、标题省略、筛选高亮、展开/可见路径。</summary>
static class OutlineUi {
	/// <summary>配置 TreeView：无横向滚动、字号跟随设置。</summary>
	public static void ConfigureTree(TreeView tree) {
		if (tree == null) return;
		ScrollViewer.SetHorizontalScrollBarVisibility(tree, ScrollBarVisibility.Disabled);
		ScrollViewer.SetVerticalScrollBarVisibility(tree, ScrollBarVisibility.Auto);
		// 内容不横向撑开
		tree.HorizontalContentAlignment = HorizontalAlignment.Stretch;
		UiStyles.ApplyTocTree(tree);
	}

	/// <summary>
	/// 展开 ideal 的祖先路径，使该项可见；不展开 ideal 自身（其子节点保持折叠）。
	/// </summary>
	public static void ExpandAncestors(TreeViewItem ideal) {
		if (ideal == null) return;
		var p = LogicalTreeHelper.GetParent(ideal);
		while (p != null) {
			if (p is TreeViewItem tvi)
				tvi.IsExpanded = true;
			p = LogicalTreeHelper.GetParent(p);
		}
	}

	/// <summary>
	/// 沿 ideal 到根的路径，取当前树中已可见的最深节点：
	/// 根始终可见；子节点仅当其父 IsExpanded 时可见。
	/// 滚动同步时用：不自动展开，只选中已展开路径上的章节或根章节。
	/// </summary>
	public static TreeViewItem FindVisibleOnPath(TreeViewItem ideal) {
		if (ideal == null) return null;
		var path = new List<TreeViewItem>();
		for (var p = (DependencyObject)ideal; p != null; p = LogicalTreeHelper.GetParent(p)) {
			if (p is TreeViewItem tvi)
				path.Add(tvi);
		}
		if (path.Count == 0) return null;
		path.Reverse();
		var last = path[0];
		for (var i = 1; i < path.Count; i++) {
			if (!path[i - 1].IsExpanded) break;
			last = path[i];
		}
		return last;
	}

	/// <summary>
	/// 将目录项滚入侧栏可视区。
	/// center=true 时垂直居中（恢复位置）；false 时仅在越界时最小滚动（连续翻页不上下乱跳）。
	/// </summary>
	public static void ScrollItemIntoView(TreeViewItem item, bool center) {
		if (item == null) return;
		try {
			if (center) {
				item.BringIntoView();
			}
			var tree = FindAncestorTreeView(item);
			var sv = FindScrollViewer(tree ?? (DependencyObject)item);
			if (sv == null) {
				if (!center) item.BringIntoView();
				return;
			}
			if (!item.IsLoaded) {
				item.BringIntoView();
				return;
			}
			GeneralTransform t;
			try {
				t = item.TransformToAncestor(sv);
			} catch {
				item.BringIntoView();
				return;
			}
			var top = t.Transform(new Point(0, 0)).Y;
			var h = item.ActualHeight;
			if (h < 1) h = 22;
			if (double.IsNaN(top) || double.IsInfinity(top)) {
				item.BringIntoView();
				return;
			}
			double targetOff;
			if (center) {
				targetOff = sv.VerticalOffset + top - (sv.ViewportHeight - h) * 0.5;
			} else {
				// 已在可视区内（留边距）则不动，避免连续翻页时目录条上下跳动
				const double margin = 6;
				if (top >= margin && top + h <= sv.ViewportHeight - margin)
					return;
				if (top < margin)
					targetOff = sv.VerticalOffset + top - margin;
				else
					targetOff = sv.VerticalOffset + (top + h - sv.ViewportHeight) + margin;
			}
			if (targetOff < 0) targetOff = 0;
			var max = Math.Max(0, sv.ExtentHeight - sv.ViewportHeight);
			if (targetOff > max) targetOff = max;
			sv.ScrollToVerticalOffset(targetOff);
		} catch {
			try { item.BringIntoView(); } catch { /* ignore */ }
		}
	}

	public static TreeView FindAncestorTreeView(DependencyObject d) {
		while (d != null) {
			if (d is TreeView tv) return tv;
			d = LogicalTreeHelper.GetParent(d) ?? (d is Visual ? VisualTreeHelper.GetParent(d) : null);
		}
		return null;
	}

	public static ScrollViewer FindScrollViewer(DependencyObject root) {
		if (root == null) return null;
		if (root is ScrollViewer sv) return sv;
		var n = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < n; i++) {
			var c = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
			if (c != null) return c;
		}
		return null;
	}

	/// <summary>
	/// 生成目录项标题：超长省略；有关键字时高亮匹配段。
	/// </summary>
	/// <param name="title">章节名</param>
	/// <param name="pageSuffix">如 " · 40"，可空</param>
	/// <param name="query">筛选关键字，空则不高亮</param>
	public static FrameworkElement MakeHeader(string title, string pageSuffix, string query) {
		title = title ?? "";
		pageSuffix = pageSuffix ?? "";
		var tb = new TextBlock {
			TextTrimming = TextTrimming.CharacterEllipsis,
			TextWrapping = TextWrapping.NoWrap,
			VerticalAlignment = VerticalAlignment.Center,
			// 限制宽度由父级拉伸，避免撑出横向滚动条
		};

		var q = query?.Trim() ?? "";
		if (q.Length == 0) {
			tb.Text = title + pageSuffix;
			return tb;
		}

		// 在 title 中高亮（页码后缀不高亮）
		var idx = title.IndexOf(q, StringComparison.OrdinalIgnoreCase);
		if (idx < 0) {
			tb.Text = title + pageSuffix;
			return tb;
		}

		var accent = Application.Current?.TryFindResource("AccentSoft") as Brush
			?? new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
		var accentFg = Application.Current?.TryFindResource("TextPrimary") as Brush
			?? Brushes.Black;

		// 支持多处匹配
		var pos = 0;
		while (pos < title.Length) {
			var i = title.IndexOf(q, pos, StringComparison.OrdinalIgnoreCase);
			if (i < 0) {
				tb.Inlines.Add(new Run(title.Substring(pos)));
				break;
			}
			if (i > pos)
				tb.Inlines.Add(new Run(title.Substring(pos, i - pos)));
			var hit = new Run(title.Substring(i, q.Length)) {
				Background = accent,
				Foreground = accentFg,
				FontWeight = FontWeights.SemiBold,
			};
			tb.Inlines.Add(hit);
			pos = i + q.Length;
		}
		if (pageSuffix.Length > 0)
			tb.Inlines.Add(new Run(pageSuffix) { Foreground = Brushes.Gray });
		return tb;
	}

	public static bool Match(string title, string query) {
		if (string.IsNullOrWhiteSpace(query)) return true;
		if (string.IsNullOrEmpty(title)) return false;
		return title.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
	}

	/// <summary>筛选框样式。</summary>
	public static TextBox MakeFilterBox() {
		return new TextBox {
			Margin = new Thickness(8, 0, 8, 6),
			Padding = new Thickness(8, 4, 8, 4),
			FontSize = DocViewOptions.Current.UiFontSize,
			ToolTip = "筛选章节：只显示匹配的标题并高亮关键字",
		};
	}
}

