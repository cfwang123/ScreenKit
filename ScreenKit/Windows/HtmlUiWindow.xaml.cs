using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → HTML 界面设计。左边写 HTML，右边用系统浏览器控件即时预览。</summary>
public partial class HtmlUiWindow : Window {
	readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };

	public HtmlUiWindow() {
		InitializeComponent();
		applylang();
		buildtpl();
		timer.Tick += (_, _) => {
			timer.Stop();
			preview();
		};
		esrc.TextChanged += (_, _) => schedule();
		bcopy.Click += (_, _) => copyhtml();
		bclose.Click += (_, _) => Close();
		var first = HtmlUiSamples.Find("buttons");
		esrc.Text = first == null ? "" : first.Html;
		Loaded += (_, _) => preview();
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("htmlui.title");
		lbhint.Text = Loc.T("htmlui.hint");
		lbsrc.Text = Loc.T("htmlui.src");
		lbview.Text = Loc.T("htmlui.preview");
		bcopy.Content = Loc.T("copy");
		bclose.Content = Loc.T("imgconv.close");
	}

	void buildtpl() {
		foreach (var one in HtmlUiSamples.All) {
			var btn = new Button {
				Content = Loc.T(one.Key),
				Style = (Style)FindResource("ToolBtn"),
				Margin = new Thickness(0, 0, 6, 6),
				Tag = one.Id,
			};
			btn.SetValue(AutomationProperties.AutomationIdProperty, "htmlui.tpl." + one.Id);
			btn.Click += (_, _) => {
				var sample = HtmlUiSamples.Find((string)btn.Tag);
				if (sample == null) return;
				esrc.Text = sample.Html;
				esrc.CaretIndex = esrc.Text.Length;
			};
			ptpl.Children.Add(btn);
		}
	}

	void schedule() {
		timer.Stop();
		timer.Start();
	}

	void preview() {
		if (!IsLoaded) return;
		try {
			view.NavigateToString(HtmlUiSamples.Wrap(esrc.Text));
			lbstat.Text = "";
		}
		catch (Exception ex) {
			lbstat.Text = ex.Message;
		}
	}

	void copyhtml() {
		try { Clipboard.SetText(esrc.Text ?? ""); }
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
