using System.Diagnostics;

namespace ScreenKit;

/// <summary>文件同步：网页管理地址 + 二维码 + 登录密码。</summary>
public partial class WebFileWindow : Window {
	readonly SendFileServer server;
	readonly OcrOptions opt;
	string url = "";

	internal WebFileWindow(SendFileServer sf, OcrOptions o) {
		server = sf;
		opt = o ?? new OcrOptions();
		InitializeComponent();
		applylang();
		bclose.Click += (_, _) => Close();
		bcopy.Click += (_, _) => copyurl();
		bcopypass.Click += (_, _) => copypass();
		bopen.Click += (_, _) => openurl();
		eurl.SelectionChanged += (_, _) => applyurl();
		WindowEsc.Attach(this);
		Loaded += (_, _) => load();
	}

	void applylang() {
		Title = Loc.T("sf.web.title");
		lbtitle.Text = Loc.T("sf.web.title");
		lbstatus.Text = Loc.T("sf.web.loading");
		lbhint.Text = Loc.T("sf.web.hint");
		lburl.Text = Loc.T("sf.web.url");
		lbpass.Text = Loc.T("sf.web.pass");
		bcopy.Content = Loc.T("sf.web.copy");
		bcopypass.Content = Loc.T("sf.web.copypass");
		bopen.Content = Loc.T("sf.web.open");
		bclose.Content = Loc.T("sf.web.close");
	}

	void load() {
		if (!opt.SendFileEnabled) {
			fail(Loc.T("sf.web.nosvc"));
			return;
		}
		if (server == null || !server.IsRunning) {
			var err = server?.LastError;
			fail(string.IsNullOrWhiteSpace(err) ? Loc.T("sf.web.down") : err);
			return;
		}
		var pass = "";
		try { pass = server.Web?.EnsurePass() ?? opt.SendFileWebPass ?? ""; }
		catch { pass = opt.SendFileWebPass ?? ""; }
		epass.Text = pass ?? "";
		bcopypass.IsEnabled = !string.IsNullOrEmpty(pass);
		var ips = ApkHost.LanIPv4s();
		if (ips == null || ips.Count == 0) {
			fail(Loc.T("sf.web.noip"));
			return;
		}
		var port = server.ListenPort > 0 ? server.ListenPort : SendFileServer.FileHttpPort(opt);
		eurl.Items.Clear();
		foreach (var ip in ips) {
			eurl.Items.Add($"http://{ip}:{port}/");
			eurl.Items.Add($"http://{ip}:{port}/m");
		}
		eurl.IsEnabled = eurl.Items.Count > 0;
		eurl.SelectedIndex = eurl.Items.Count > 0 ? 0 : -1;
		lbstatus.Text = Loc.T("sf.web.status", port.ToString());
		applyurl();
	}

	void applyurl() {
		url = (eurl.SelectedItem as string ?? "").Trim();
		bcopy.Content = Loc.T("sf.web.copy");
		bcopy.IsEnabled = url.Length > 0;
		bopen.IsEnabled = url.Length > 0;
		if (url.Length == 0) {
			iqr.Source = null;
			return;
		}
		try {
			iqr.Source = QrMake.Encode(url, 8);
		}
		catch (Exception ex) {
			iqr.Source = null;
			lbstatus.Text = Loc.T("sf.web.qrfail", ex.Message);
		}
	}

	void fail(string msg) {
		lbstatus.Text = msg ?? "";
		bcopy.IsEnabled = false;
		bopen.IsEnabled = false;
		eurl.IsEnabled = false;
		iqr.Source = null;
	}

	void copyurl() {
		if (string.IsNullOrEmpty(url)) return;
		try {
			Clipboard.SetText(url);
			bcopy.Content = Loc.T("sf.web.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Loc.T("sf.web.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void copypass() {
		var p = epass.Text ?? "";
		if (string.IsNullOrEmpty(p)) return;
		try {
			Clipboard.SetText(p);
			bcopypass.Content = Loc.T("sf.web.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Loc.T("sf.web.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void openurl() {
		if (string.IsNullOrEmpty(url)) return;
		try {
			Process.Start(new ProcessStartInfo {
				FileName = url,
				UseShellExecute = true,
			});
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Loc.T("sf.web.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
