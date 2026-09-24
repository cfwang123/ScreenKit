using System.Diagnostics;

namespace ScreenKit;

/// <summary>文件同步：安装到手机（本机 HTTP 内网下载链接 + 二维码）。</summary>
public partial class ApkInstallWindow : Window {
	readonly SendFileServer server;
	readonly OcrOptions opt;
	string url = "";
	string statusok = "";

	internal ApkInstallWindow(SendFileServer sf, OcrOptions o) {
		server = sf;
		opt = o ?? new OcrOptions();
		InitializeComponent();
		applylang();
		bclose.Click += (_, _) => Close();
		bcopy.Click += (_, _) => copyurl();
		bopen.Click += (_, _) => openurl();
		eurl.SelectionChanged += (_, _) => applyurl();
		WindowEsc.Attach(this);
		Loaded += (_, _) => { _ = load(); };
	}

	void applylang() {
		Title = Loc.T("sf.apk.title");
		lbtitle.Text = Loc.T("sf.apk.title");
		lbstatus.Text = Loc.T("sf.apk.loading");
		lbhint.Text = Loc.T("sf.apk.hint");
		lburl.Text = Loc.T("sf.apk.url");
		bcopy.Content = Loc.T("sf.apk.copy");
		bopen.Content = Loc.T("sf.apk.open");
		bclose.Content = Loc.T("sf.apk.close");
	}

	async Task load() {
		try {
			if (server == null || !server.IsRunning || !opt.SendFileEnabled) {
				fail(Loc.T("sf.apk.nosvc"));
				return;
			}
			lbstatus.Text = Loc.T("sf.apk.loading");
			string file = ApkHost.FindFile();
			if (string.IsNullOrEmpty(file)) {
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
				try {
					file = await ApkHost.EnsureAsync(cts.Token).ConfigureAwait(true);
				}
				catch (Exception ex) {
					if (!IsLoaded) return;
					fail(Loc.T("sf.apk.fail", ex is OperationCanceledException ? "timeout" : ex.Message));
					return;
				}
			}
			if (!IsLoaded) return;
			if (string.IsNullOrEmpty(file) || !File.Exists(file)) {
				fail(Loc.T("sf.apk.noapk"));
				lbhint.Text = Loc.T("sf.apk.noapk.hint");
				return;
			}
			var ips = ApkHost.LanIPv4s();
			if (ips == null || ips.Count == 0) {
				fail(Loc.T("sf.apk.noip"));
				return;
			}
			var port = server.ListenPort > 0 ? server.ListenPort : SendFileServer.FileHttpPort(opt);
			eurl.Items.Clear();
			foreach (var ip in ips)
				eurl.Items.Add(ApkHost.Url(ip, port));
			eurl.IsEnabled = eurl.Items.Count > 0;
			eurl.SelectedIndex = eurl.Items.Count > 0 ? 0 : -1;
			long size = 0;
			try { size = new FileInfo(file).Length; } catch { }
			var sizeText = size > 0 ? FeatureInstaller.FormatBytes(size) : "—";
			statusok = Loc.T("sf.apk.ver", ApkHost.VersionOf(file))
				+ "  ·  " + Path.GetFileName(file) + "  ·  " + sizeText;
			lbstatus.Text = statusok;
			lbhint.Text = Loc.T("sf.apk.hint");
			applyurl();
		}
		catch (Exception ex) {
			if (!IsLoaded) return;
			fail(Loc.T("sf.apk.fail", ex.Message));
		}
	}

	void applyurl() {
		url = (eurl.SelectedItem as string ?? "").Trim();
		bcopy.Content = Loc.T("sf.apk.copy");
		bcopy.IsEnabled = url.Length > 0;
		bopen.IsEnabled = url.Length > 0;
		if (url.Length == 0) {
			iqr.Source = null;
			return;
		}
		try {
			iqr.Source = QrMake.Encode(url, 8);
			if (!string.IsNullOrEmpty(statusok)) lbstatus.Text = statusok;
		}
		catch (Exception ex) {
			iqr.Source = null;
			lbstatus.Text = Loc.T("sf.apk.qrfail", ex.Message);
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
			bcopy.Content = Loc.T("sf.apk.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Loc.T("sf.apk.title"),
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
			MessageBox.Show(this, ex.Message, Loc.T("sf.apk.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
