using System;
using System.Windows;

namespace ScreenKit.DocView;

/// <summary>一次查找导航结果（第 n/m）。</summary>
struct FindResult {
	public bool Found;
	/// <summary>当前命中序号，1-based；未找到为 0。</summary>
	public int Current;
	/// <summary>总命中数。</summary>
	public int Total;

	public static FindResult Miss(int total = 0) => new FindResult {
		Found = false, Current = 0, Total = total,
	};

	public static FindResult Hit(int current1, int total) => new FindResult {
		Found = total > 0 && current1 > 0,
		Current = current1,
		Total = total,
	};
}

interface IDocViewer : IDisposable {
	FrameworkElement View { get; }
	string FilePath { get; }
	string Title { get; }
	DocKind Kind { get; }
	double Zoom { get; }
	string StatusText { get; }
	/// <summary>总页数；无分页则为 1。</summary>
	int PageCount { get; }
	/// <summary>当前页（1-based）；无内容为 0。</summary>
	int CurrentPage { get; }

	event Action StatusChanged;

	void Load(string path);
	void SetZoom(double zoom);
	void ZoomBy(double factor);
	void ZoomIn();
	void ZoomOut();
	void ZoomFitWidth();
	void ZoomFitPage();
	void GoPrevPage();
	void GoNextPage();
	/// <summary>跳到 1-based 页码。</summary>
	void GoToPage(int page1Based);
	/// <summary>
	/// 按 90° 步进旋转视图（Sumatra：[ 逆时针 ] 顺时针）。
	/// deltaQuarterTurns&gt;0 顺时针，&lt;0 逆时针；不支持的格式可空操作。
	/// </summary>
	void RotateBy(int deltaQuarterTurns);
	/// <summary>
	/// 全文查找并跳到上/下一个命中；同一关键词会缓存全部匹配以便显示第 n/m。
	/// </summary>
	/// <param name="ignoreCase">true=忽略大小写。</param>
	/// <param name="restart">true=强制重建命中缓存。</param>
	/// <param name="fromView">
	/// true=从当前视口起找：首击为视口内/下方第 1 个；连续则下一个；
	/// 若已滚离当前命中，则从新视口重新起算（搜索框 Enter 用）。
	/// </param>
	FindResult Find(string text, bool forward, bool ignoreCase, bool restart = false, bool fromView = false);
	/// <summary>清除查找缓存与屏幕内匹配高亮（搜索框变更/清空时调用）。</summary>
	void ClearFind();
	/// <summary>复制当前选区到剪贴板；无选区返回 false。</summary>
	bool TryCopySelection();

	/// <summary>是否有可用目录（无则侧栏默认可隐藏）。</summary>
	bool HasOutline { get; }
	/// <summary>侧栏（目录）是否可见。</summary>
	bool SidePanelVisible { get; }
	/// <summary>显示/隐藏目录侧栏。</summary>
	void SetSidePanelVisible(bool show);

	/// <summary>
	/// 采集阅读位置：水平/垂直滚动、缩放、sheet（xlsx 0-based）或页（1-based 辅助）。
	/// </summary>
	void CaptureViewState(out double h, out double v, out double zoom, out int sheetOrPage);
	/// <summary>恢复阅读位置（布局完成后再调用更可靠）。</summary>
	void RestoreViewState(double h, double v, double zoom, int sheetOrPage);
}
