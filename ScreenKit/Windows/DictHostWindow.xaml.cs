using System;
using System.Windows;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>单独的词典窗口。内容与主窗口词典页是同一个 <see cref="DictWindow"/>。</summary>
public partial class DictHostWindow : Window {
	static int slot;

	public DictHostWindow(string query, Func<OcrOptions> options, Action<string> onTranslate, long id = 0) {
		InitializeComponent();
		var n = slot++;
		host.Options = options;
		host.OnTranslate = onTranslate;
		host.ApplyLang();
		Title = Loc.T("dict.win", query ?? "");
		WindowEsc.Attach(this, () => {
			if (!host.CloseSel()) Close();
		});
		ContentRendered += (_, _) => {
			Left += 28 * (n % 8);
			Top += 28 * (n % 8);
		};
		Loaded += (_, _) => host.SearchText(query, id);
		Closed += (_, _) => host.Shutdown();
	}

	internal int HitCount => host.HitCount;
}
