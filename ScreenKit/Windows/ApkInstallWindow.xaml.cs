using System.Diagnostics;

namespace ScreenKit;

/// <summary>文件同步：安装到手机（本机 HTTP 内网下载链接 + 二维码）。</summary>
public partial class ApkInstallWindow : Window {
	readonly SendFileServer server;
	readonly OcrOptions opt;
	string url = "";
	string statusok = "";
	ApkInfo remote;
	bool busy;
	CancellationTokenSource cts;

	internal ApkInstallWindow(SendFileServer sf, OcrOptions o) {
		server = sf;
		opt = o ?? new OcrOptions();
		InitializeComponent();
		applylang();
		bclose.Click += (_, _) => Close();
		bcopy.Click += (_, _) => copyurl();
		bopen.Click += (_, _) => openurl();
		bcheck.Click += async (_, _) => await check();
		bget.Click += async (_, _) => await download();
		eurl.SelectionChanged += (_, _) => applyurl();
		Closing += (_, _) => {
			try { cts?.Cancel(); } catch { }
		};
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
		bcheck.Content = Loc.T("sf.apk.check");
		bget.Content = Loc.T("sf.apk.download");
	}

	async Task load() {
		try {
			if (server == null || !server.IsRunning || !opt.SendFileEnabled) {
				fail(Loc.T("sf.apk.nosvc"));
				return;
			}
			var file = ApkHost.FindFile();
			if (!string.IsNullOrEmpty(file) && File.Exists(file))
				showfile(file);
			else {
				lbstatus.Text = Loc.T("sf.apk.noapk");
				lbhint.Text = Loc.T("sf.apk.noapk.hint");
				fillurls();
			}
			await check();
		}
		catch (Exception ex) {
			if (!IsLoaded) return;
			fail(Loc.T("sf.apk.fail", ex.Message));
		}
	}

	void showfile(string file) {
		if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
		ApkHost.UseFile(file);
		fillurls();
		long size = 0;
		try { size = new FileInfo(file).Length; } catch { }
		var sizeText = size > 0 ? FeatureInstaller.FormatBytes(size) : "—";
		statusok = Loc.T("sf.apk.ver", ApkHost.VersionOf(file))
			+ "  ·  " + Path.GetFileName(file) + "  ·  " + sizeText;
		lbstatus.Text = statusok;
		lbhint.Text = Loc.T("sf.apk.hint");
		pdown.Visibility = Visibility.Collapsed;
		applyurl();
	}

	void fillurls() {
		if (eurl.Items.Count > 0) return;
		var ips = ApkHost.LanIPv4s();
		if (ips == null || ips.Count == 0) {
			fail(Loc.T("sf.apk.noip"));
			return;
		}
		var port = server.ListenPort > 0 ? server.ListenPort : SendFileServer.FileHttpPort(opt);
		foreach (var ip in ips)
			eurl.Items.Add(ApkHost.Url(ip, port));
		eurl.IsEnabled = eurl.Items.Count > 0;
		if (eurl.SelectedIndex < 0 && eurl.Items.Count > 0)
			eurl.SelectedIndex = 0;
	}

	async Task check() {
		if (busy) return;
		busy = true;
		setbusy(true);
		try {
			lbstatus.Text = Loc.T("sf.apk.checking");
			using var linked = beginwork(TimeSpan.FromSeconds(25));
			remote = await AppUpdater.CheckLatestApkAsync(linked.Token).ConfigureAwait(true);
			if (!IsLoaded) return;
			if (remote == null || !remote.HasApk) {
				lbstatus.Text = Loc.T("sf.apk.norelease");
				bget.IsEnabled = false;
				return;
			}
			var local = ApkHost.FindFile();
			var lv = string.IsNullOrEmpty(local) ? "—" : ApkHost.VersionOf(local);
			if (string.IsNullOrEmpty(local) || AppUpdater.IsNewerVersion(remote.Version, lv == "—" ? "" : lv))
				lbstatus.Text = Loc.T("sf.apk.newer", remote.Version ?? "", lv);
			else {
				lbstatus.Text = Loc.T("sf.apk.uptodate", lv, remote.Version ?? "");
				statusok = lbstatus.Text;
			}
		}
		catch (Exception ex) {
			if (!IsLoaded) return;
			if (ex is OperationCanceledException) return;
			lbstatus.Text = Loc.T("sf.apk.fail", ex.Message);
		}
		finally {
			busy = false;
			if (IsLoaded) setbusy(false);
		}
	}

	async Task download() {
		if (busy) return;
		busy = true;
		setbusy(true);
		pdown.Visibility = Visibility.Visible;
		pdown.Value = 0;
		try {
			using var linked = beginwork(TimeSpan.FromMinutes(20));
			if (remote == null || !remote.HasApk) {
				lbstatus.Text = Loc.T("sf.apk.checking");
				remote = await AppUpdater.CheckLatestApkAsync(linked.Token).ConfigureAwait(true);
			}
			if (!IsLoaded) return;
			if (remote == null || !remote.HasApk) {
				lbstatus.Text = Loc.T("sf.apk.norelease");
				return;
			}
			var progress = new Progress<InstallProgress>(p => {
				if (!IsLoaded) return;
				pdown.Visibility = Visibility.Visible;
				if (p.Overall >= 0 && p.Overall <= 1) pdown.Value = p.Overall;
				lbstatus.Text = Loc.T("sf.apk.downloading", p.BytesText ?? "");
			});
			var file = await ApkHost.DownloadAsync(remote, progress, linked.Token).ConfigureAwait(true);
			if (!IsLoaded) return;
			showfile(file);
			lbstatus.Text = Loc.T("sf.apk.downloaded", remote.Version ?? ApkHost.VersionOf(file));
			statusok = lbstatus.Text;
		}
		catch (Exception ex) {
			if (!IsLoaded) return;
			if (ex is OperationCanceledException) return;
			pdown.Visibility = Visibility.Collapsed;
			lbstatus.Text = Loc.T("sf.apk.fail", ex is OperationCanceledException ? "timeout" : ex.Message);
		}
		finally {
			busy = false;
			if (IsLoaded) setbusy(false);
		}
	}

	CancellationTokenSource beginwork(TimeSpan limit) {
		try { cts?.Cancel(); } catch { }
		try { cts?.Dispose(); } catch { }
		cts = new CancellationTokenSource(limit);
		return cts;
	}

	void setbusy(bool on) {
		bcheck.IsEnabled = !on;
		bget.IsEnabled = !on && (remote == null || remote.HasApk);
		bclose.IsEnabled = true;
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
