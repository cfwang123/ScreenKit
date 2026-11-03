using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 功能页「FTP 服务」：设置根目录、端口和单个用户（留空匿名），就地启停，
/// 右侧只显示每行一条的简单日志。状态同时反映到主窗口状态栏（FTP:21）。
/// </summary>
public partial class MainWindow {
	readonly FtpServer ftpsrv = new();
	readonly TftpServer tftpsrv = new();
	readonly List<string> ftpsrvLog = new();
	readonly object ftpsrvLogGate = new();
	DispatcherTimer ftpsrvLogTick;
	bool ftpsrvLogDirty;
	/// <summary>正在用配置回填控件，别触发保存。</summary>
	bool ftpsrvLoading;

	void initftpsrv() {
		if (tabftpsrv == null) return;
		ftpsrvLogTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
		ftpsrvLogTick.Tick += (_, _) => flushftpsrvlog();
		ftpsrvLogTick.Start();
		ftpsrv.Log = addftpsrvlog;
		tftpsrv.Log = addftpsrvlog;
		ftpsrvLoading = true;
		eftpsrvdir.Text = opt.FtpSrvRoot ?? "";
		eftpsrvport.Text = opt.FtpSrvPort <= 0 ? "21" : opt.FtpSrvPort.ToString();
		eftpsrvuser.Text = opt.FtpSrvUser ?? "";
		eftpsrvpass.Password = opt.FtpSrvPass ?? "";
		etftpport.Text = opt.TftpPort <= 0 ? "69" : opt.TftpPort.ToString();
		ftpsrvLoading = false;
		applyftpsrvlang();
		updateftpsrvhint();
		syncftpsrvui();

		eftpsrvdir.LostFocus += (_, _) => saveftpsrvopts();
		eftpsrvport.LostFocus += (_, _) => saveftpsrvopts();
		eftpsrvuser.LostFocus += (_, _) => {
			saveftpsrvopts();
			updateftpsrvhint();
		};
		eftpsrvpass.LostFocus += (_, _) => saveftpsrvopts();
		etftpport.LostFocus += (_, _) => savetftpopts();
		etftpport.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			savetftpopts();
		};
		eftpsrvdir.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			saveftpsrvopts();
		};
		eftpsrvport.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			saveftpsrvopts();
		};
		eftpsrvuser.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			saveftpsrvopts();
			updateftpsrvhint();
		};
		eftpsrvpass.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			saveftpsrvopts();
		};
		bftpsrvtoggle.Click += (_, _) => toggleftpsrv();
		btftptoggle.Click += (_, _) => toggletftp();
		bftpsrvbrowse.Click += (_, _) => browseftpsrvdir();
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
		lbftpsrvbrand.Text = Loc.T("ftpsrv.title");
		lbftpsrvdir.Text = Loc.T("ftpsrv.dir");
		lbftpsrvport.Text = Loc.T("ftpsrv.port");
		lbftpsrvuser.Text = Loc.T("ftpsrv.user");
		lbftpsrvpass.Text = Loc.T("ftpsrv.pass");
		lbftpsrvlog.Text = Loc.T("ftpsrv.log");
		bftpsrvbrowse.Content = Loc.T("ftpsrv.browse");
		bftpsrvclear.Content = Loc.T("ftpsrv.clear");
		lbftpsrvhint.Text = Loc.T("ftpsrv.hint");
		lbftpsrvtftp.Text = Loc.T("ftpsrv.tftp");
		lbftpsrvtftphint.Text = Loc.T("ftpsrv.tftp.hint");
		lbftpsrvtftpport.Text = Loc.T("ftpsrv.tftp.port");
	}

	/// <summary>用户名留空就是匿名，提示里说明当前是哪种。</summary>
	void updateftpsrvhint() {
		var anon = (eftpsrvuser.Text ?? "").Trim().Length == 0;
		lbftpsrvhint.Text = anon ? Loc.T("ftpsrv.anon") : Loc.T("ftpsrv.needuser");
		eftpsrvpass.IsEnabled = !anon;
	}

	void saveftpsrvopts() {
		if (ftpsrvLoading) return;
		opt.FtpSrvRoot = (eftpsrvdir.Text ?? "").Trim();
		opt.FtpSrvPort = int.TryParse((eftpsrvport.Text ?? "").Trim(), out var p) && p >= 1 && p <= 65535
			? p : 21;
		opt.FtpSrvUser = (eftpsrvuser.Text ?? "").Trim();
		opt.FtpSrvPass = eftpsrvpass.Password ?? "";
		try { AppConfig.Save(opt); } catch { }
	}

	void browseftpsrvdir() {
		var dlg = new System.Windows.Forms.FolderBrowserDialog {
			Description = Loc.T("ftpsrv.dir"),
			ShowNewFolderButton = true,
		};
		var cur = (eftpsrvdir.Text ?? "").Trim();
		if (cur.Length == 0) cur = opt.FtpSrvRoot;
		if (string.IsNullOrWhiteSpace(cur)) cur = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ftproot");
		try {
			if (Directory.Exists(cur)) dlg.SelectedPath = cur;
		}
		catch { }
		try {
			if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
				eftpsrvdir.Text = dlg.SelectedPath;
				saveftpsrvopts();
			}
		}
		catch { }
		finally {
			try { dlg.Dispose(); } catch { }
		}
	}

	void toggleftpsrv() {
		if (ftpsrv.IsRunning) stopftpsrv();
		else startftpsrv();
	}

	void startftpsrv() {
		saveftpsrvopts();
		opt.FtpSrvEnabled = true;
		try { AppConfig.Save(opt); } catch { }
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
		synctftpui();
		syncftpsrvstatus();
	}

	void stopftpsrv() {
		try { ftpsrv.Stop(); } catch { }
		addftpsrvlog(Loc.T("ftpsrv.stopped"));
		if (!ftpsrvLoading) {
			opt.FtpSrvEnabled = false;
			try { AppConfig.Save(opt); } catch { }
		}
		syncftpsrvui();
		synctftpui();
		syncftpsrvstatus();
	}

	/// <summary>按服务实际状态刷新按钮文字和状态行。</summary>
	void syncftpsrvui() {
		if (bftpsrvtoggle == null) return;
		var on = ftpsrv.IsRunning;
		bftpsrvtoggle.Content = on ? Loc.T("ftpsrv.stop") : Loc.T("ftpsrv.start");
		lbftpsrvstat.Text = on
			? string.Format(Loc.T("ftpsrv.running"), ftpsrv.Port)
			: Loc.T("ftpsrv.off");
		eftpsrvdir.IsEnabled = !on;
		eftpsrvport.IsEnabled = !on;
		eftpsrvuser.IsEnabled = !on;
		eftpsrvpass.IsEnabled = !on;
		bftpsrvbrowse.IsEnabled = !on;
		// TFTP 与 FTP 共用目录，目录被锁时 TFTP 也一起锁
		etftpport.IsEnabled = !on && !tftpsrv.IsRunning;
	}

	void savetftpopts() {
		if (ftpsrvLoading) return;
		opt.TftpPort = int.TryParse((etftpport.Text ?? "").Trim(), out var p) && p >= 1 && p <= 65535
			? p : 69;
		// 根目录与 FTP 共用，这里不动 FtpSrvRoot
		try { AppConfig.Save(opt); } catch { }
	}

	void toggletftp() {
		if (tftpsrv.IsRunning) stoptftp();
		else starttftp();
	}

	void starttftp() {
		savetftpopts();
		opt.TftpEnabled = true;
		try { AppConfig.Save(opt); } catch { }
		try {
			tftpsrv.Start(opt.TftpOpts());
			addftpsrvlog(string.Format(Loc.T("tftpsrv.started"), tftpsrv.Port));
		}
		catch (Exception ex) {
			opt.TftpEnabled = false;
			try { AppConfig.Save(opt); } catch { }
			addftpsrvlog(string.Format(Loc.T("tftpsrv.startfail"), ex.Message));
		}
		synctftpui();
		syncftpsrvstatus();
	}

	void stoptftp() {
		try { tftpsrv.Stop(); } catch { }
		addftpsrvlog(Loc.T("tftpsrv.stopped"));
		if (!ftpsrvLoading) {
			opt.TftpEnabled = false;
			try { AppConfig.Save(opt); } catch { }
		}
		synctftpui();
		syncftpsrvstatus();
	}

	/// <summary>按 TFTP 服务实际状态刷新开关文字、端口可编辑性和状态行。</summary>
	void synctftpui() {
		if (btftptoggle == null) return;
		var on = tftpsrv.IsRunning;
		btftptoggle.Content = on ? Loc.T("tftpsrv.stop") : Loc.T("tftpsrv.start");
		lbftpsrvtftpstat.Text = on
			? string.Format(Loc.T("tftpsrv.running"), tftpsrv.Port)
			: Loc.T("tftpsrv.off");
		etftpport.IsEnabled = !on;
	}

	/// <summary>启动时按配置决定要不要拉起服务。</summary>
	void bootftpsrv() {
		if (tabftpsrv != null && !opt.TabFtpSrvVisible) return;
		boottftp();
		if (!opt.FtpSrvEnabled) {
			syncftpsrvstatus();
			return;
		}
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
		synctftpui();
		syncftpsrvstatus();
	}

	/// <summary>启动时按配置决定要不要拉起 TFTP。</summary>
	void boottftp() {
		synctftpui();
		if (!opt.TftpEnabled) return;
		try {
			tftpsrv.Start(opt.TftpOpts());
			addftpsrvlog(string.Format(Loc.T("tftpsrv.started"), tftpsrv.Port));
		}
		catch (Exception ex) {
			opt.TftpEnabled = false;
			try { AppConfig.Save(opt); } catch { }
			addftpsrvlog(string.Format(Loc.T("tftpsrv.startfail"), ex.Message));
		}
		synctftpui();
	}

	/// <summary>功能开关切换时调用：隐藏页面就停服务。</summary>
	void applyftpsrvvisible() {
		if (tabftpsrv != null) tabftpsrv.Visibility = opt.TabFtpSrvVisible
			? Visibility.Visible : Visibility.Collapsed;
		if (!opt.TabFtpSrvVisible) {
			if (ftpsrv.IsRunning) stopftpsrv();
			if (tftpsrv.IsRunning) stoptftp();
		}
		else syncftpsrvstatus();
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
