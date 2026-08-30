using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace ScreenKit;

/// <summary>关于：版本号 + MIT 许可证全文（读 exe 旁 LICENSE）。</summary>
public partial class AboutWindow : Window {
	public AboutWindow() {
		InitializeComponent();
		applylang();
		fillversion();
		filllicense();
		bok.Click += (_, _) => Close();
		bopenlic.Click += (_, _) => openfile(licensepath());
		bthird.Click += (_, _) => openfile(thirdpath());
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("about.title");
		lbname.Text = AppNames.Current;
		lblicensehead.Text = Loc.T("about.license_head");
		bthird.Content = Loc.T("about.third");
		bopenlic.Content = Loc.T("about.open_lic");
		bok.Content = Loc.T("ok");
	}

	void fillversion() {
		var ver = appversion();
		var arch = ArchBootstrap.CurrentLabel;
		lbver.Text = Loc.T("about.version", ver) + $"  ({arch})";
		lbcopy.Text = Loc.T("about.copyright");
	}

	void filllicense() {
		var path = licensepath();
		if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
			try {
				elicense.Text = File.ReadAllText(path);
				bopenlic.IsEnabled = true;
				return;
			}
			catch { }
		}
		// 内嵌摘要（文件缺失时）
		elicense.Text = Loc.T("about.license_fallback");
		bopenlic.IsEnabled = false;
	}

	static string appversion() {
		try {
			var asm = Assembly.GetExecutingAssembly();
			var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			if (!string.IsNullOrWhiteSpace(info)) {
				// 去掉可能的 +git 后缀
				var s = info.Trim();
				var plus = s.IndexOf('+');
				if (plus > 0) s = s[..plus];
				return s;
			}
			var v = asm.GetName().Version;
			if (v != null) return $"{v.Major}.{v.Minor}.{v.Build}";
		}
		catch { }
		return "—";
	}

	static string licensepath() {
		var dir = AppDomain.CurrentDomain.BaseDirectory;
		var p = Path.Combine(dir, "LICENSE");
		if (File.Exists(p)) return p;
		p = Path.Combine(dir, "LICENCE");
		if (File.Exists(p)) return p;
		// 开发时：仓库根
		try {
			var root = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", ".."));
			p = Path.Combine(root, "LICENSE");
			if (File.Exists(p)) return p;
		}
		catch { }
		return Path.Combine(dir, "LICENSE");
	}

	static string thirdpath() {
		var dir = AppDomain.CurrentDomain.BaseDirectory;
		var p = Path.Combine(dir, "THIRD_PARTY_NOTICES.md");
		if (File.Exists(p)) return p;
		try {
			var root = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", ".."));
			p = Path.Combine(root, "THIRD_PARTY_NOTICES.md");
			if (File.Exists(p)) return p;
		}
		catch { }
		return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "THIRD_PARTY_NOTICES.md");
	}

	static void openfile(string path) {
		try {
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
				MessageBox.Show(
					Loc.T("about.file_missing"),
					Loc.T("about.title"),
					MessageBoxButton.OK,
					MessageBoxImage.Information);
				return;
			}
			Process.Start(new ProcessStartInfo {
				FileName = path,
				UseShellExecute = true,
			});
		}
		catch (Exception ex) {
			MessageBox.Show(ex.Message, Loc.T("about.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
