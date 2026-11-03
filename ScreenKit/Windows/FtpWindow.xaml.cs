using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 工具 → 网络工具 → FTP 文件管理。
/// 只显示服务器一侧：从资源管理器拖文件或文件夹进来即上传，把选中文件拖出去即交给资源管理器下载。
/// 浏览和传输各用独立会话。
/// </summary>
public partial class FtpWindow : Window {
	readonly ObservableCollection<FtpEntry> remoteRows = new();
	readonly FtpXfer xfer = new();
	readonly List<FtpSite> sites = new();

	IFtpBackend browse;
	FtpSite curSite;
	string remoteDir = "/";

	readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(50) };
	bool dead;
	bool connecting;
	bool remoteDragBusy;

	public FtpWindow() {
		InitializeComponent();
		lvremote.ItemsSource = remoteRows;
		lvxfer.ItemsSource = xfer.Jobs;
		applylang();
		initconn();
		setconnstate(false);
		initsites();
		initbrowse();
		initxfer();
		inittimer();
		WindowEsc.Attach(this);
		Closed += (_, _) => {
			dead = true;
			try { tick.Stop(); } catch { }
			try { browse?.Dispose(); } catch { }
			browse = null;
		};
		Loaded += (_, _) => {
			restoresession();
			checkdeps();
		};
	}

	/// <summary>开窗先看库在不在；缺了问一句，同意就去装，拒绝就只留本机浏览。</summary>
	void checkdeps() {
		if (FtpDeps.Installed) return;
		var ok = FtpDeps.Ensure(this);
		if (ok) return;
		setstat(Loc.T("ftp.need.hint"));
		bconn.IsEnabled = false;
		bsave.IsEnabled = false;
		bremotedel.IsEnabled = false;
		bremotemkdir.IsEnabled = false;
		bremoterefresh.IsEnabled = false;
		bdownloadto.IsEnabled = false;
	}

	// ==================== 初始化 ====================
	#region
	void initconn() {
		foreach (var k in FtpProto.All) cbKind.Items.Add(kindLabel(k));
		cbKind.SelectedIndex = 3;
		cbKind.SelectionChanged += (_, _) => {
			// 端口没被改过就跟着协议走
			var kind = selKind();
			if (string.IsNullOrWhiteSpace(eport.Text) || isDefaultPort(eport.Text.Trim()))
				eport.Text = FtpProto.DefPort(kind).ToString();
		};
		eport.Text = FtpProto.DefPort(FtpProto.Sftp).ToString();
		bconn.Click += (_, _) => connect();
		bsave.Click += (_, _) => savesite();
		ehost.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			doconnect();
		};
		epass.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			doconnect();
		};
	}

	static bool isDefaultPort(string s) {
		foreach (var k in FtpProto.All)
			if (s == FtpProto.DefPort(k).ToString()) return true;
		return false;
	}

	string kindLabel(string kind) => kind switch {
		FtpProto.Ftps => Loc.T("ftp.kind.ftps"),
		FtpProto.FtpsImplicit => Loc.T("ftp.kind.ftpsi"),
		FtpProto.Sftp => Loc.T("ftp.kind.sftp"),
		_ => Loc.T("ftp.kind.ftp")
	};

	string selKind() {
		var i = cbKind.SelectedIndex;
		if (i < 0 || i >= FtpProto.All.Length) return FtpProto.Ftp;
		return FtpProto.All[i];
	}

	void initsites() {
		reloadsites();
		cbSite.SelectionChanged += (_, _) => {
			if (cbSite.SelectedItem is not FtpSite s) return;
			fillsite(s);
		};
	}

	void reloadsites() {
		sites.Clear();
		sites.AddRange(FtpSiteStore.Load());
		cbSite.ItemsSource = null;
		cbSite.ItemsSource = sites;
	}

	void fillsite(FtpSite s) {
		ehost.Text = s.Host ?? "";
		eport.Text = s.PortOr(FtpProto.DefPort(FtpProto.Norm(s.Kind))).ToString();
		euser.Text = s.User ?? "";
		var i = Array.IndexOf(FtpProto.All, FtpProto.Norm(s.Kind));
		if (i >= 0) cbKind.SelectedIndex = i;
		epass.Password = FtpSiteStore.Unprotect(s.Secret);
	}

	void savesite() {
		var host = (ehost.Text ?? "").Trim();
		if (host.Length == 0) {
			setstat(Loc.T("ftp.err.nohost"));
			return;
		}
		var name = (curSite?.Name ?? "").Trim();
		if (name.Length == 0) name = host;
		var site = curSite ?? new FtpSite();
		var exist = sites.FirstOrDefault(x => string.Equals(x.Name, site.Name, StringComparison.Ordinal));
		if (exist != null) site = exist;
		site.Name = name;
		site.Kind = selKind();
		site.Host = host;
		site.Port = parseInt(eport.Text, 0);
		site.User = (euser.Text ?? "").Trim();
		var pw = epass.Password ?? "";
		site.Secret = pw.Length > 0 ? FtpSiteStore.Protect(pw) : site.Secret;
		if (exist == null) sites.Add(site);
		FtpSiteStore.Save(sites);
		cbSite.ItemsSource = null;
		cbSite.ItemsSource = sites;
		cbSite.SelectedItem = site;
		setstat(string.Format(Loc.T("ftp.site.saved"), site.Name));
	}

	static int parseInt(string s, int def) =>
		int.TryParse((s ?? "").Trim(), out var v) && v > 0 && v <= 65535 ? v : def;

	void initbrowse() {
		bremoteup.Click += (_, _) => remoteup();
		bremoterefresh.Click += (_, _) => loadremote(remoteDir);
		bremotemkdir.Click += (_, _) => remotemkdir();
		bremotedel.Click += (_, _) => remotedel();
		bdownloadto.Click += (_, _) => downloadto();

		eremotepath.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			loadremote((eremotepath.Text ?? "").Trim());
		};

		lvremote.MouseDoubleClick += (_, _) => {
			if (lvremote.SelectedItem is not FtpEntry e) return;
			if (!e.IsDir) return;
			loadremote(e.Full);
		};

		initremotedrag();
		initremotedrop();
	}

	/// <summary>把选中文件拖到资源管理器：给的是虚拟文件，资源管理器边读边落盘。</summary>
	void initremotedrag() {
		Point down = default;
		lvremote.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(lvremote);
		lvremote.PreviewMouseMove += (_, e) => {
			if (e.LeftButton != MouseButtonState.Pressed || remoteDragBusy) return;
			var now = e.GetPosition(lvremote);
			if (Math.Abs(now.X - down.X) < 6 && Math.Abs(now.Y - down.Y) < 6) return;
			if (browse == null) {
				setstat(Loc.T("ftp.err.noconn"));
				return;
			}
			var files = new List<FtpDragOut.Item>();
			int dirs = 0;
			foreach (var one in lvremote.SelectedItems) {
				if (one is not FtpEntry it) continue;
				if (it.IsDir) { dirs++; continue; }
				files.Add(new FtpDragOut.Item {
					Remote = it.Full,
					Name = it.Name,
					Size = it.Size,
					Modified = it.Modified
				});
			}
			if (dirs > 0) setstat(Loc.T("ftp.drag.dirs"));
			if (files.Count == 0) return;
			remoteDragBusy = true;
			try {
				Mouse.Capture(null);
				setstat(string.Format(Loc.T("ftp.drag.start"), files.Count));
				var site = curSite;
				var pw = epass.Password ?? "";
				var ok = FtpDragOut.Drag(site, pw, files);
				setstat(ok ? Loc.T("ftp.drag.done") : Loc.T("ftp.drag.cancel"));
			}
			catch (Exception ex) { setstat(ex.Message); }
			finally { remoteDragBusy = false; }
		};
	}

	/// <summary>资源管理器（本程序选中项也行）拖进列表 → 上传到当前目录。</summary>
	void initremotedrop() {
		DragEventHandler over = (_, e) => {
			if (browse == null || !canaccept(e.Data)) {
				e.Effects = DragDropEffects.None;
				e.Handled = true;
				return;
			}
			e.Effects = DragDropEffects.Copy;
			e.Handled = true;
		};
		DragEventHandler drop = (_, e) => {
			if (browse == null) {
				setstat(Loc.T("ftp.err.noconn"));
				return;
			}
			if (!canaccept(e.Data)) return;
			e.Handled = true;
			var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (paths == null || paths.Length == 0) return;
			uploadpaths(paths);
		};
		lvremote.DragOver += over;
		lvremote.Drop += drop;
		dropzone.DragOver += over;
		dropzone.Drop += drop;
	}

	static bool canaccept(IDataObject data) =>
		data != null && data.GetDataPresent(DataFormats.FileDrop);

	void initxfer() {
		foreach (var n in new[] { 1, 2, 3, 4 }) cbParallel.Items.Add(n.ToString());
		cbParallel.SelectedIndex = 1;
		cbParallel.SelectionChanged += (_, _) => {
			if (int.TryParse(cbParallel.SelectedItem as string, out var n)) xfer.MaxParallel = n;
		};
		bclearfinished.Click += (_, _) => xfer.ClearFinished();
		xfer.Finished += _ => dispatcher(() => {
			// 传完刷新远程目录，不然新文件看不到
			if (browse != null) refreshremotequiet();
		});
	}

	/// <summary>50ms 拉一次进度快照。任务自己会通知绑定，这里只补状态栏计数。</summary>
	void inittimer() {
		tick.Tick += (_, _) => {
			if (dead) return;
			var n = xfer.ActiveCount;
			lbxfercount.Text = n > 0
				? string.Format(Loc.T("ftp.xfer.active"), n)
				: "";
		};
		tick.Start();
	}

	void restoresession() {
		var last = FtpSiteStore.LoadLast();
		if (sites.Count > 0) {
			var pick = sites.FirstOrDefault(s => s.Name == last.Site) ?? sites[0];
			cbSite.SelectedItem = pick;
		}
		var rd = (last.RemoteDir ?? "").Trim();
		remoteDir = rd.Length > 0 ? rd : "/";
		eremotepath.Text = remoteDir;
	}

	void savelast() {
		try {
			FtpSiteStore.SaveLast(new FtpSiteStore.LastState {
				Site = curSite?.Name ?? "",
				RemoteDir = remoteDir
			});
		}
		catch { }
	}
	#endregion

	// ==================== 连接 ====================
	#region
	/// <summary>连接按钮是切换键：没连就连接，已连就断开。</summary>
	void connect() {
		if (connecting) return;
		if (browse != null) {
			disconnect();
			return;
		}
		doconnect();
	}

	void disconnect() {
		connecting = false;
		try { browse?.Dispose(); } catch { }
		browse = null;
		remoteRows.Clear();
		setconnstate(false);
		setstat(Loc.T("ftp.disconnected"));
	}

	/// <summary>按当前是否连着，换按钮文字与配色（绿=连接，红=断开）。</summary>
	void setconnstate(bool on) {
		bconn.Content = Loc.T(on ? "ftp.disconnect" : "ftp.connect");
		bconn.Style = (Style)FindResource(on ? "DisconnectBtn" : "ConnectBtn");
		bconn.IsEnabled = true;
		bremotemkdir.IsEnabled = on;
		bremotedel.IsEnabled = on;
		bremoterefresh.IsEnabled = on;
		bdownloadto.IsEnabled = on;
		updateupbtn();
	}

	void doconnect() {
		if (connecting) return;
		if (!FtpDeps.Ensure(this)) {
			setstat(Loc.T("ftp.need.hint"));
			return;
		}
		var host = (ehost.Text ?? "").Trim();
		if (host.Length == 0) {
			setstat(Loc.T("ftp.err.nohost"));
			return;
		}
		var site = new FtpSite {
			Name = (curSite?.Name ?? host).Trim(),
			Kind = selKind(),
			Host = host,
			Port = parseInt(eport.Text, 0),
			User = (euser.Text ?? "").Trim(),
			KeyFile = curSite?.KeyFile ?? ""
		};
		var pw = epass.Password ?? "";
		if (browse != null) {
			try { browse.Dispose(); } catch { }
			browse = null;
		}
		setstat(string.Format(Loc.T("ftp.connecting"), host));
		connecting = true;
		bconn.IsEnabled = false;
		var backend = FtpBackendFactory.Create(site.Kind);
		Task.Run(() => {
			try {
				backend.Connect(site, pw);
				dispatcher(() => onconnected(site, backend));
			}
			catch (Exception ex) {
				try { backend.Dispose(); } catch { }
				dispatcher(() => {
					connecting = false;
					setconnstate(false);
					setstat(string.Format(Loc.T("ftp.err.connect"), ex.Message));
				});
			}
		});
	}

	void onconnected(FtpSite site, IFtpBackend backend) {
		connecting = false;
		if (dead) {
			try { backend.Dispose(); } catch { }
			return;
		}
		browse = backend;
		curSite = site;
		setconnstate(true);
		var wd = "/";
		try { wd = backend.WorkDir(); }
		catch { }
		remoteDir = wd;
		setstat(string.Format(Loc.T("ftp.connected"), site.Kind, site.Host, site.PortOr(FtpProto.DefPort(site.Kind))));
		loadremote(remoteDir);
	}
	#endregion

	// ==================== 远程浏览 ====================
	#region
	void loadremote(string dir) {
		if (browse == null) {
			setstat(Loc.T("ftp.err.noconn"));
			return;
		}
		var d = (dir ?? "").Trim();
		if (d.Length == 0) d = "/";
		var backend = browse;
		setstat(Loc.T("ftp.loading"));
		Task.Run(() => {
			List<FtpEntry> got = null;
			string err = null;
			try { got = backend.List(d); }
			catch (Exception ex) { err = ex.Message; }
			dispatcher(() => {
				if (dead || browse != backend) return;
				if (err != null) {
					setstat(string.Format(Loc.T("ftp.err.list"), err));
					return;
				}
				remoteRows.Clear();
				foreach (var one in got) remoteRows.Add(one);
				remoteDir = d;
				eremotepath.Text = d;
				updateupbtn();
				savelast();
				setstat(string.Format(Loc.T("ftp.listed"), got.Count));
			});
		});
	}

	/// <summary>传完后静默刷新：失败不弹提示，别打断用户。</summary>
	void refreshremotequiet() {
		if (browse == null) return;
		var backend = browse;
		var d = remoteDir;
		Task.Run(() => {
			List<FtpEntry> got = null;
			try { got = backend.List(d); }
			catch { return; }
			dispatcher(() => {
				if (dead || browse != backend || remoteDir != d) return;
				remoteRows.Clear();
				foreach (var one in got) remoteRows.Add(one);
			});
		});
	}

	void remoteup() {
		var r = remoteDir.TrimEnd('/');
		var i = r.LastIndexOf('/');
		if (i <= 0) {
			loadremote("/");
			return;
		}
		loadremote(r.Substring(0, i));
	}

	void remotemkdir() {
		if (browse == null) { setstat(Loc.T("ftp.err.noconn")); return; }
		var name = askname(Loc.T("ftp.prompt.newdir"));
		if (string.IsNullOrWhiteSpace(name)) return;
		var target = FtpXfer.joinRemote(remoteDir, name.Trim());
		var backend = browse;
		Task.Run(() => {
			try {
				backend.MkDir(target);
				dispatcher(() => loadremote(remoteDir));
			}
			catch (Exception ex) { dispatcher(() => setstat(ex.Message)); }
		});
	}

	void remotedel() {
		if (browse == null) { setstat(Loc.T("ftp.err.noconn")); return; }
		var items = lvremote.SelectedItems.Cast<FtpEntry>().ToList();
		if (items.Count == 0) return;
		if (MessageBox.Show(string.Format(Loc.T("ftp.confirm.del"), items.Count),
			Loc.T("ftp.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
		var backend = browse;
		Task.Run(() => {
			var errs = new List<string>();
			foreach (var it in items) {
				try {
					if (it.IsDir) backend.RmDir(it.Full);
					else backend.RmFile(it.Full);
				}
				catch (Exception ex) { errs.Add($"{it.Name}: {ex.Message}"); }
			}
			dispatcher(() => {
				loadremote(remoteDir);
				if (errs.Count > 0) setstat(string.Join("; ", errs));
			});
		});
	}

	void updateupbtn() {
		bremoteup.IsEnabled = browse != null && remoteDir.TrimEnd('/').Length > 0;
	}

	/// <summary>选中项（文件或整个目录）下载到指定本机目录。</summary>
	void downloadto() {
		if (browse == null) { setstat(Loc.T("ftp.err.noconn")); return; }
		var items = lvremote.SelectedItems.Cast<FtpEntry>().ToList();
		if (items.Count == 0) { setstat(Loc.T("ftp.err.nosel")); return; }
		string root;
		using (var dlg = new System.Windows.Forms.FolderBrowserDialog {
			Description = Loc.T("ftp.prompt.downloaddir"),
			ShowNewFolderButton = true
		}) {
			if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
			root = dlg.SelectedPath;
		}
		if (string.IsNullOrWhiteSpace(root)) return;
		var site = curSite;
		var pw = epass.Password ?? "";
		setstat(Loc.T("ftp.loading"));
		Task.Run(() => {
			var list = new List<(string remote, string local, long size)>();
			string err = null;
			try {
				var be = FtpBackendFactory.Create(site);
				try {
					be.Connect(site, pw);
					foreach (var it in items) collect(be, it, root, list);
				}
				finally { be.Dispose(); }
			}
			catch (Exception ex) { err = ex.Message; }
			dispatcher(() => {
				if (dead) return;
				if (err != null) { setstat(err); return; }
				foreach (var one in list)
					xfer.EnqueueDownload(site, one.remote, one.local, one.size, pw);
				setstat(string.Format(Loc.T("ftp.queued.down"), list.Count));
			});
		});
	}

	static void collect(IFtpBackend be, FtpEntry it, string localDir, List<(string remote, string local, long size)> list) {
		if (it.IsDir) {
			var sub = Path.Combine(localDir, it.Name);
			Directory.CreateDirectory(sub);
			foreach (var one in be.List(it.Full)) collect(be, one, sub, list);
			return;
		}
		list.Add((it.Full, Path.Combine(localDir, it.Name), it.Size));
	}
	#endregion

	// ==================== 传输触发 ====================
	#region
	void uploadpaths(string[] paths) {
		if (curSite == null || browse == null) {
			setstat(Loc.T("ftp.err.noconn"));
			return;
		}
		var pw = epass.Password ?? "";
		int n = 0;
		foreach (var p in paths) {
			foreach (var (local, remote) in FtpXfer.ExpandUpload(p, remoteDir)) {
				xfer.EnqueueUpload(curSite, local, remote, pw);
				n++;
			}
		}
		if (n > 0) setstat(string.Format(Loc.T("ftp.queued.up"), n));
	}

	void OnJobResume(object sender, RoutedEventArgs e) {
		if ((sender as Button)?.Tag is FtpJob job) xfer.Resume(job);
	}

	void OnJobPause(object sender, RoutedEventArgs e) {
		if ((sender as Button)?.Tag is FtpJob job) xfer.Pause(job);
	}

	void OnJobCancel(object sender, RoutedEventArgs e) {
		if ((sender as Button)?.Tag is FtpJob job) xfer.Cancel(job);
	}
	#endregion

	// ==================== 杂项 ====================
	#region
	/// <summary>要一个名字（新建文件夹）。取消返回 null。</summary>
	string askname(string label) {
		var w = new Window {
			Title = label,
			Owner = this,
			Width = 380,
			SizeToContent = SizeToContent.Height,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ResizeMode = ResizeMode.NoResize,
			Background = (Brush)FindResource("BgApp"),
			FontFamily = FontFamily
		};
		var grid = new Grid { Margin = new Thickness(16) };
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		var lb = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
		var box = new TextBox {
			Height = 32,
			Padding = new Thickness(8, 4, 8, 4),
			VerticalContentAlignment = VerticalAlignment.Center
		};
		var bar = new StackPanel {
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0, 12, 0, 0)
		};
		var ok = new Button {
			Content = Loc.T("ok"),
			IsDefault = true,
			MinWidth = 72,
			Margin = new Thickness(0, 0, 8, 0),
			Style = (Style)FindResource("ToolBtn")
		};
		var cancel = new Button {
			Content = Loc.T("cancel"),
			IsCancel = true,
			MinWidth = 72,
			Style = (Style)FindResource("ToolBtn")
		};
		ok.Click += (_, _) => { w.DialogResult = true; };
		bar.Children.Add(ok);
		bar.Children.Add(cancel);
		Grid.SetRow(box, 1);
		Grid.SetRow(bar, 2);
		grid.Children.Add(lb);
		grid.Children.Add(box);
		grid.Children.Add(bar);
		w.Content = grid;
		WindowEsc.Attach(w);
		w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
		if (w.ShowDialog() != true) return null;
		return (box.Text ?? "").Trim();
	}

	void dispatcher(Action act) {
		if (dead) return;
		if (!Dispatcher.CheckAccess()) {
			Dispatcher.BeginInvoke(act);
			return;
		}
		act();
	}

	void setstat(string text) {
		if (dead || lbstat == null) return;
		lbstat.Text = text ?? "";
	}

	void applylang() {
		Title = Loc.T("ftp.title");
		lbsite.Text = Loc.T("ftp.site");
		lbhost.Text = Loc.T("ftp.host");
		lbport.Text = Loc.T("ftp.port");
		lbuser.Text = Loc.T("ftp.user");
		lbpass.Text = Loc.T("ftp.pass");
		lbkind.Text = Loc.T("ftp.kind");
		bconn.Content = Loc.T(browse != null ? "ftp.disconnect" : "ftp.connect");
		bsave.Content = Loc.T("ftp.save");
		lbremote.Text = Loc.T("ftp.remote");
		bremoteup.Content = Loc.T("ftp.up");
		bremoterefresh.Content = Loc.T("ftp.refresh");
		bremotemkdir.Content = Loc.T("ftp.mkdir");
		bremotedel.Content = Loc.T("ftp.del");
		bdownloadto.Content = Loc.T("ftp.downloadto");
		lbxfer.Text = Loc.T("ftp.xfer");
		lbparal.Text = Loc.T("ftp.parallel");
		bclearfinished.Content = Loc.T("ftp.clear");
		colrname.Header = Loc.T("ftp.colname");
		colrsize.Header = Loc.T("ftp.colsize");
		colrperm.Header = Loc.T("ftp.colperm");
		colrtime.Header = Loc.T("ftp.coltime");
		colxdir.Header = Loc.T("ftp.coldir");
		colxname.Header = Loc.T("ftp.colname");
		colxprog.Header = Loc.T("ftp.colprog");
		colxpct.Header = Loc.T("ftp.colpct");
		colxsize.Header = Loc.T("ftp.colxsize");
		colxspeed.Header = Loc.T("ftp.colspeed");
		colxstate.Header = Loc.T("ftp.colstate");
		colxops.Header = Loc.T("ftp.colops");
		lbhint.Text = Loc.T("ftp.hint");
	}

	#endregion
}
