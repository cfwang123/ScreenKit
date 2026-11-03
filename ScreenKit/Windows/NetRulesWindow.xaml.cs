namespace ScreenKit;

/// <summary>进程/IP 黑白名单着色与 LISTEN 过滤设置。</summary>
public partial class NetRulesWindow : Window {
	public NetRulesWindow() {
		InitializeComponent();
		var r = RuleStore.Instance;
		r.Load();
		cklisten.IsChecked = r.IgnoreListen;
		ckcolor.IsChecked = r.EnableColorRules;
		eprocblack.Text = r.GetProcessBlackText();
		eprocwhite.Text = r.GetProcessWhiteText();
		eipblack.Text = r.GetIpBlackText();
		eipwhite.Text = r.GetIpWhiteText();
		lbpath.Text = "配置文件: " + r.ConfigPath;

		bsave.Click += (_, _) => {
			RuleStore.Instance.SetLists(
				eprocblack.Text,
				eprocwhite.Text,
				eipblack.Text,
				eipwhite.Text,
				cklisten.IsChecked == true,
				ckcolor.IsChecked == true);
			DialogResult = true;
			Close();
		};
		bcancel.Click += (_, _) => {
			DialogResult = false;
			Close();
		};
		WindowEsc.Attach(this, () => { DialogResult = false; Close(); });
	}
}
