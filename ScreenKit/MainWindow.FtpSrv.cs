using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 功能页「FTP 服务」：只显示服务状态和每行一条的日志，右侧可清空。
/// 服务的启停与参数在「参数设置 → 常规」里设置，点确定后按新配置生效。
/// 状态同时反映到主窗口状态栏（FTP:21 · TFTP:69）。
/// </summary>
public partial class MainWindow {
	readonly FtpServer ftpsrv = new();
	readonly TftpServer tftpsrv = new();
	readonly List<string> ftpsrvLog = new();
	readonly object ftpsrvLogGate = new();
	DispatcherTimer ftpsrvLogTick;
	bool ftpsrvLogDirty;

	void initftpsrv() {
		if (tabftpsrv == null) return;
		ftpsrvLogTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
		ftpsrvLogTick.Tick += (_, _) => flushftpsrvlog();
		ftpsrvLogTick.Start();
		ftpsrv.Log = addftpsrvlog;
		tftpsrv.Log = addftpsrvlog;
		applyftpsrvlang();
		syncftpsrvui();
		syncftpsrvstatus();

		bftpsrvclear.Click += (_, _) => {
			lock (ftpsrvLogGate) {
				ftpsrvLog.Clear();
				ftpsrvShown = 0;
				ftpsrvLogDirty = false;
			}
			eftpsrvlog.Clear();
		};
	}

	void applyftpsrvlang() {
		tabftpsrv.Header = Loc.T("ftpsrv.tab");
		lbftpsrvlog.Text = Loc.T("ftpsrv.log");
		bftpsrvclear.Content = Loc.T("ftpsrv.clear");
	}

	void startftpsrv() {
		try {
			ftpsrv.Start(opt.FtpSrvOpts());
			addftpsrvlog(string.Format(Loc.T("ftpsrv.started"), ftpsrv.Port));
		}
		catch (Exception ex) {
			opt.FtpSrvEnabled = false;
			try { AppConfig.Save(opt); } catch { }
			addftpsrvlog(string.Format(Loc.T("ftpsrv.startfail"), ex.Message));
		}
		syncftpsrvui();
		syncftpsrvstatus();
	}

	void stopftpsrv() {
		try { ftpsrv.Stop(); } catch { }
		addftpsrvlog(Loc.T("ftpsrv.stopped"));
		syncftpsrvui();
		syncftpsrvstatus();
	}

	void starttftp() {
		try {
			tftpsrv.Start(opt.TftpOpts());
			addftpsrvlog(string.Format(Loc.T("tftpsrv.started"), tftpsrv.Port));
		}
		catch (Exception ex) {
			opt.TftpEnabled = false;
			try { AppConfig.Save(opt); } catch { }
			addftpsrvlog(string.Format(Loc.T("tftpsrv.startfail"), ex.Message));
		}
		syncftpsrvui();
		syncftpsrvstatus();
	}

	void stoptftp() {
		try { tftpsrv.Stop(); } catch { }
		addftpsrvlog(Loc.T("tftpsrv.stopped"));
		syncftpsrvui();
		syncftpsrvstatus();
	}

	/// <summary>把 FTP 与 TFTP 实际运行状态写到标题旁的状态行。</summary>
	void syncftpsrvui() {
		if (lbftpsrvstat == null) return;
		var parts = new List<string>();
		if (ftpsrv.IsRunning && ftpsrv.Port > 0)
			parts.Add(string.Format(Loc.T("ftpsrv.running"), ftpsrv.Port));
		if (tftpsrv.IsRunning && tftpsrv.Port > 0)
			parts.Add(string.Format(Loc.T("tftpsrv.running"), tftpsrv.Port));
		lbftpsrvstat.Text = parts.Count == 0
			? Loc.T("ftpsrv.off")
			: string.Join(" · ", parts);
	}

	/// <summary>启动时按配置决定要不要拉起服务。</summary>
	void bootftpsrv() {
		if (tabftpsrv != null && !opt.TabFtpSrvVisible) return;
		if (opt.TftpEnabled) starttftp();
		if (opt.FtpSrvEnabled) startftpsrv();
		syncftpsrvui();
		syncftpsrvstatus();
	}

	/// <summary>
	/// 设置窗口点确定后调用：按新旧配置的差异启停服务。
	/// 端口、目录、账号任一变化都要重建监听，已在运行的服务先停再按新值启动。
	/// </summary>
	void applyftpsrvopts(OcrOptions old) {
		var rootChanged = !string.Equals(old.FtpSrvRoot, opt.FtpSrvRoot, StringComparison.OrdinalIgnoreCase);
		var ftpChanged = rootChanged
			|| old.FtpSrvEnabled != opt.FtpSrvEnabled
			|| old.FtpSrvPort != opt.FtpSrvPort
			|| !string.Equals(old.FtpSrvUser, opt.FtpSrvUser, StringComparison.Ordinal)
			|| !string.Equals(old.FtpSrvPass, opt.FtpSrvPass, StringComparison.Ordinal);
		var tftpChanged = rootChanged
			|| old.TftpEnabled != opt.TftpEnabled
			|| old.TftpPort != opt.TftpPort;

		if (!ftpChanged && !tftpChanged) return;
		if (ftpChanged) {
			if (ftpsrv.IsRunning) stopftpsrv();
			if (opt.FtpSrvEnabled) startftpsrv();
		}
		if (tftpChanged) {
			if (tftpsrv.IsRunning) stoptftp();
			if (opt.TftpEnabled) starttftp();
		}
		syncftpsrvui();
		syncftpsrvstatus();
	}

	/// <summary>功能开关切换时调用：隐藏页面就停服务。</summary>
	void applyftpsrvvisible() {
		if (tabftpsrv != null) tabftpsrv.Visibility = opt.TabFtpSrvVisible
			? Visibility.Visible : Visibility.Collapsed;
		if (!opt.TabFtpSrvVisible) {
			if (ftpsrv.IsRunning) stopftpsrv();
			if (tftpsrv.IsRunning) stoptftp();
		}
		syncftpsrvui();
		syncftpsrvstatus();
	}

	/// <summary>状态栏点「FTP:21」时把主窗口带到前台并切到 FTP 服务页。</summary>
	void opentabftpsrv() {
		try {
			if (!opt.TabFtpSrvVisible) {
				opt.TabFtpSrvVisible = true;
				AppConfig.Save(opt);
				applymaintabvisibility();
			}
			if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
			Show();
			Activate();
			if (tabftpsrv != null) {
				tabftpsrv.Visibility = Visibility.Visible;
				maintabs.SelectedItem = tabftpsrv;
			}
		}
		catch { }
	}

	/// <summary>把服务状态写到状态栏：FTP:21、TFTP:69，各自独立显示。</summary>
	void syncftpsrvstatus() {
		if (bstftpsrv == null) return;
		var ftpOn = ftpsrv.IsRunning && ftpsrv.Port > 0;
		var tftpOn = tftpsrv.IsRunning && tftpsrv.Port > 0;
		var parts = new List<string>();
		if (ftpOn) parts.Add(string.Format(Loc.T("stbar.ftpsrv"), ftpsrv.Port));
		if (tftpOn) parts.Add(string.Format(Loc.T("stbar.tftpsrv"), tftpsrv.Port));
		bstftpsrv.Text = string.Join(" · ", parts);
		var on = parts.Count > 0;
		bstftpsrv.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
		if (lstftpsrvsep != null)
			lstftpsrvsep.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
	}

	// ==================== 日志 ====================
	#region
	/// <summary>后台线程调用，先塞进列表，定时器统一刷到界面。</summary>
	void addftpsrvlog(string line) {
		if (string.IsNullOrEmpty(line)) return;
		lock (ftpsrvLogGate) {
			ftpsrvLog.Add(line);
			while (ftpsrvLog.Count > 500) ftpsrvLog.RemoveAt(0);
			ftpsrvLogDirty = true;
		}
	}

	void flushftpsrvlog() {
		if (eftpsrvlog == null) return;
		string text;
		lock (ftpsrvLogGate) {
			if (!ftpsrvLogDirty) return;
			ftpsrvLogDirty = false;
			// 列表超过上限会从头裁掉，计数跟着往回缩
			if (ftpsrvShown > ftpsrvLog.Count) ftpsrvShown = 0;
			var from = ftpsrvShown;
			var add = ftpsrvLog.Skip(from).ToList();
			if (add.Count == 0) return;
			text = string.Join("\n", add);
			ftpsrvShown = ftpsrvLog.Count;
		}
		if (text.Length == 0) return;
		try {
			if (eftpsrvlog.Text.Length == 0) eftpsrvlog.Text = text;
			else eftpsrvlog.AppendText("\n" + text);
			eftpsrvlog.CaretIndex = eftpsrvlog.Text.Length;
			eftpsrvlog.ScrollToEnd();
		}
		catch { }
	}

	/// <summary>已经贴到界面的条数；清空时归零。</summary>
	int ftpsrvShown;
	#endregion
}
