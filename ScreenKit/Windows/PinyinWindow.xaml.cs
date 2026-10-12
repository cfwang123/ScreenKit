using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

/// <summary>工具 → 中文转拼音。需要程序旁的 NPinyin.dll。</summary>
public partial class PinyinWindow : Window {
	public PinyinWindow() {
		InitializeComponent();
		applylang();
		bpy.Click += (_, _) => go(false);
		binit.Click += (_, _) => go(true);
		bcopy.Click += (_, _) => copyout();
		bclose.Click += (_, _) => Close();
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("pinyin.title");
		lbhint.Text = Loc.T("pinyin.hint");
		lbsrc.Text = Loc.T("pinyin.src");
		lbout.Text = Loc.T("pinyin.out");
		bpy.Content = Loc.T("pinyin.py");
		binit.Content = Loc.T("pinyin.init");
		bcopy.Content = Loc.T("texttool.copy");
		bclose.Content = Loc.T("imgconv.close");
	}

	void go(bool initials) {
		if (!PinyinConvert.Installed &&
			!FeaturePrompt.EnsureKinds(this, Loc.T("pinyin.title"), Loc.T("pinyin.need"), FeatureKind.NPinyin)) {
			lbstat.Text = Loc.T("pinyin.need");
			return;
		}
		try {
			var src = esrc.Text ?? "";
			var dst = PinyinConvert.Convert(src, initials);
			eout.Text = dst;
			lbstat.Text = Loc.T("pinyin.stat", src.Length, dst.Length);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("texttool.fail", ex.Message);
		}
	}

	void copyout() {
		try { Clipboard.SetText(eout.Text ?? ""); }
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
