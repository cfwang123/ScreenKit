using System;
using System.Windows;
using System.Windows.Media;

namespace ScreenKit.DocView;

/// <summary>一套 UI 主题色板。</summary>
sealed class AppTheme {
	public int Id;
	public string Name;
	public string Desc;

	// 主色
	public Color Accent;
	public Color AccentSoft;
	public Color AccentDark;
	// 背景
	public Color BgApp;
	public Color BgPanel;
	public Color BgSoft;
	public Color BgTitle;
	public Color BgToolbar;
	public Color BgStatus;
	public Color BgTab;
	public Color BgTabActive;
	// 文字 / 边框
	public Color TextPrimary;
	public Color TextMuted;
	public Color BorderSoft;
	public Color Danger;
	public Color CapBtnHover;

	/// <summary>主题色条预览用（强调色）。</summary>
	public Color Preview => Accent;

	public static int Count => All.Length;

	public static readonly AppTheme[] All = {
		// 0 默认蓝
		T(0, "默认蓝", "经典浅蓝强调，浅灰界面",
			a: 0x2563EB, asoft: 0xDBEAFE, adark: 0x1D4ED8,
			bg: 0xF3F4F6, panel: 0xFFFFFF, soft: 0xF8FAFC,
			title: 0xF0F1F3, tool: 0xF7F8FA, status: 0xF0F1F3,
			tab: 0xE5E7EB, taba: 0xFFFFFF,
			text: 0x111827, muted: 0x6B7280, border: 0xD1D5DB,
			danger: 0xDC2626, cap: 0xE5E7EB),
		// 1 清新绿
		T(1, "清新绿", "自然绿色强调",
			a: 0x059669, asoft: 0xD1FAE5, adark: 0x047857,
			bg: 0xF0FDF4, panel: 0xFFFFFF, soft: 0xECFDF5,
			title: 0xECFDF5, tool: 0xF0FDF4, status: 0xECFDF5,
			tab: 0xD1FAE5, taba: 0xFFFFFF,
			text: 0x064E3B, muted: 0x6B7280, border: 0xA7F3D0,
			danger: 0xDC2626, cap: 0xD1FAE5),
		// 2 暖橙
		T(2, "暖橙", "活力橙色强调",
			a: 0xEA580C, asoft: 0xFFEDD5, adark: 0xC2410C,
			bg: 0xFFF7ED, panel: 0xFFFFFF, soft: 0xFFFBEB,
			title: 0xFFEDD5, tool: 0xFFF7ED, status: 0xFFEDD5,
			tab: 0xFED7AA, taba: 0xFFFFFF,
			text: 0x1C1917, muted: 0x78716C, border: 0xFDBA74,
			danger: 0xDC2626, cap: 0xFED7AA),
		// 3 紫罗兰
		T(3, "紫罗兰", "柔和紫色强调",
			a: 0x7C3AED, asoft: 0xEDE9FE, adark: 0x6D28D9,
			bg: 0xF5F3FF, panel: 0xFFFFFF, soft: 0xFAF5FF,
			title: 0xEDE9FE, tool: 0xF5F3FF, status: 0xEDE9FE,
			tab: 0xDDD6FE, taba: 0xFFFFFF,
			text: 0x1E1B4B, muted: 0x6B7280, border: 0xC4B5FD,
			danger: 0xDC2626, cap: 0xDDD6FE),
		// 4 青碧
		T(4, "青碧", "青绿强调",
			a: 0x0D9488, asoft: 0xCCFBF1, adark: 0x0F766E,
			bg: 0xF0FDFA, panel: 0xFFFFFF, soft: 0xF0FDFA,
			title: 0xCCFBF1, tool: 0xF0FDFA, status: 0xCCFBF1,
			tab: 0x99F6E4, taba: 0xFFFFFF,
			text: 0x134E4A, muted: 0x6B7280, border: 0x5EEAD4,
			danger: 0xDC2626, cap: 0x99F6E4),
		// 5 玫瑰红
		T(5, "玫瑰红", "玫瑰粉强调",
			a: 0xE11D48, asoft: 0xFFE4E6, adark: 0xBE123C,
			bg: 0xFFF1F2, panel: 0xFFFFFF, soft: 0xFFF1F2,
			title: 0xFFE4E6, tool: 0xFFF1F2, status: 0xFFE4E6,
			tab: 0xFECDD3, taba: 0xFFFFFF,
			text: 0x1F2937, muted: 0x6B7280, border: 0xFDA4AF,
			danger: 0x9F1239, cap: 0xFECDD3),
		// 6 商务靛
		T(6, "商务靛", "沉稳靛蓝",
			a: 0x1E3A8A, asoft: 0xDBEAFE, adark: 0x1E40AF,
			bg: 0xEFF6FF, panel: 0xFFFFFF, soft: 0xF8FAFC,
			title: 0xDBEAFE, tool: 0xEFF6FF, status: 0xDBEAFE,
			tab: 0xBFDBFE, taba: 0xFFFFFF,
			text: 0x0F172A, muted: 0x64748B, border: 0x93C5FD,
			danger: 0xDC2626, cap: 0xBFDBFE),
		// 7 石墨灰
		T(7, "石墨灰", "中性灰，低干扰",
			a: 0x4B5563, asoft: 0xE5E7EB, adark: 0x374151,
			bg: 0xF3F4F6, panel: 0xFFFFFF, soft: 0xF9FAFB,
			title: 0xE5E7EB, tool: 0xF3F4F6, status: 0xE5E7EB,
			tab: 0xD1D5DB, taba: 0xFFFFFF,
			text: 0x111827, muted: 0x6B7280, border: 0x9CA3AF,
			danger: 0xDC2626, cap: 0xD1D5DB),
		// 8 暗夜
		T(8, "暗夜", "深色护眼",
			a: 0x60A5FA, asoft: 0x1E3A5F, adark: 0x3B82F6,
			bg: 0x111827, panel: 0x1F2937, soft: 0x1F2937,
			title: 0x0F172A, tool: 0x1F2937, status: 0x0F172A,
			tab: 0x374151, taba: 0x1F2937,
			text: 0xF3F4F6, muted: 0x9CA3AF, border: 0x4B5563,
			danger: 0xF87171, cap: 0x374151),
		// 9 高对比
		T(9, "高对比", "黑白高对比，清晰醒目",
			a: 0x000000, asoft: 0xE5E7EB, adark: 0x111827,
			bg: 0xFFFFFF, panel: 0xFFFFFF, soft: 0xF9FAFB,
			title: 0xFFFFFF, tool: 0xF3F4F6, status: 0xF3F4F6,
			tab: 0xD1D5DB, taba: 0xFFFFFF,
			text: 0x000000, muted: 0x374151, border: 0x000000,
			danger: 0xB91C1C, cap: 0xD1D5DB),
	};

	public static AppTheme Get(int id) {
		if (id < 0 || id >= All.Length) return All[0];
		return All[id];
	}

	static AppTheme T(int id, string name, string desc,
		uint a, uint asoft, uint adark,
		uint bg, uint panel, uint soft,
		uint title, uint tool, uint status,
		uint tab, uint taba,
		uint text, uint muted, uint border,
		uint danger, uint cap) {
		return new AppTheme {
			Id = id,
			Name = name,
			Desc = desc,
			Accent = rgb(a),
			AccentSoft = rgb(asoft),
			AccentDark = rgb(adark),
			BgApp = rgb(bg),
			BgPanel = rgb(panel),
			BgSoft = rgb(soft),
			BgTitle = rgb(title),
			BgToolbar = rgb(tool),
			BgStatus = rgb(status),
			BgTab = rgb(tab),
			BgTabActive = rgb(taba),
			TextPrimary = rgb(text),
			TextMuted = rgb(muted),
			BorderSoft = rgb(border),
			Danger = rgb(danger),
			CapBtnHover = rgb(cap),
		};
	}

	static Color rgb(uint v) =>
		Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
}

/// <summary>
/// 目录侧栏等控件取强调色用。ScreenKit 已有自己的全局配色（App.xaml 的 Accent 等），
/// 这里只保留当前色板供移植层取色，不再改写 Application.Resources。
/// </summary>
static class ThemeService {
	public static AppTheme Current { get; private set; } = AppTheme.All[0];
	public static event Action Changed;

	public static void Apply(int themeId) {
		var t = AppTheme.Get(themeId);
		Current = t;
		try { Changed?.Invoke(); } catch { /* ignore */ }
	}

	static SolidColorBrush brush(Color c) {
		var br = new SolidColorBrush(c);
		// 不 Freeze，便于下次就地改 Color
		return br;
	}

	static bool islight(Color c) {
		// 相对亮度
		var y = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
		return y > 0.6;
	}
}
