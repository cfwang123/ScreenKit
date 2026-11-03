using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 工具 → 网络工具 → FTP 文件管理。
/// 左本机右远程，拖放即传，底部一条传输队列。浏览和传输各用独立会话。
/// </summary>
public partial class FtpWindow : Window {
	readonly ObservableCollection<FtpEntry> localRows = new();
	readonly ObservableCollection<FtpEntry> remoteRows = new();
	readonly FtpXfer xfer = new();
	readonly List<FtpSite> sites = new();

	IFtpBackend browse;
	FtpSite curSite;
	string localDir = "";
	string remoteDir = "/";

	readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(50) };
	bool dead;
	bool localDragBusy;
	bool remoteDragBusy;

	public FtpWindow() {
		InitializeComponent();
		lvlocal.ItemsSource = localRows;
		lvremote.ItemsSource = remoteRows;
		lvxfer.ItemsSource = xfer.Jobs;
		applylang();
		initconn();
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
			connect();
		};
		epass.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			connect();
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
		blocalup.Click += (_, _) => localup();
		blocalrefresh.Click += (_, _) => loadlocal(localDir);
		blocalmkdir.Click += (_, _) => localmkdir();
		blocaldel.Click += (_, _) => localdel();
		bremoteup.Click += (_, _) => remoteup();
		bremoterefresh.Click += (_, _) => loadremote(remoteDir);
		bremotemkdir.Click += (_, _) => remotemkdir();
		bremotedel.Click += (_, _) => remotedel();

		// 地址栏回车跳转
		elocalpath.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			loadlocal((elocalpath.Text ?? "").Trim());
		};
		eremotepath.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			loadremote((eremotepath.Text ?? "").Trim());
		};

		// 双击进目录
		lvlocal.MouseDoubleClick += (_, _) => {
			if (lvlocal.SelectedItem is not FtpEntry e || !e.IsDir) return;
			loadlocal(e.Full);
		};
		lvremote.MouseDoubleClick += (_, _) => {
			if (lvremote.SelectedItem is not FtpEntry e) return;
			if (!e.IsDir) return;
			loadremote(e.Full);
		};

		initlocaldrag();
		initremotedrag();
		initremotedrop();
	}

	/// <summary>把本机选中项拖出去（给资源管理器和远程栏）。</summary>
	void initlocaldrag() {
		Point down = default;
		lvlocal.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(lvlocal);
		lvlocal.PreviewMouseMove += (_, e) => {
			if (e.LeftButton != MouseButtonState.Pressed || localDragBusy) return;
			var now = e.GetPosition(lvlocal);
			if (Math.Abs(now.X - down.X) < 6 && Math.Abs(now.Y - down.Y) < 6) return;
			var files = new List<string>();
			foreach (var one in lvlocal.SelectedItems)
				if (one is FtpEntry it) files.Add(it.Full);
			if (files.Count == 0) return;
			localDragBusy = true;
			try {
				// 拖到远程栏由下面的 Drop 接；拖到资源管理器按 Move 不行，用 Copy
				var data = new DataObject(DataFormats.FileDrop, files.ToArray());
				DragDrop.DoDragDrop(lvlocal, data, DragDropEffects.Copy);
			}
			catch { }
			finally { localDragBusy = false; }
		};
	}

	/// <summary>远程栏选中项拖到本机栏 → 下载。用私有格式传对象，避免和文件拖放混淆。</summary>
	void initremotedrag() {
		Point down = default;
		lvremote.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(lvremote);
		lvremote.PreviewMouseMove += (_, e) => {
			if (e.LeftButton != MouseButtonState.Pressed || remoteDragBusy) return;
			var now = e.GetPosition(lvremote);
			if (Math.Abs(now.X - down.X) < 6 && Math.Abs(now.Y - down.Y) < 6) return;
			var items = lvremote.SelectedItems.Cast<FtpEntry>().ToList();
			if (items.Count == 0) return;
			remoteDragBusy = true;
			try {
				var data = new DataObject();
				data.SetData("ScreenKitFtpRemote", items);
				DragDrop.DoDragDrop(lvremote, data, DragDropEffects.Copy);
			}
			catch { }
			finally { remoteDragBusy = false; }
		};
	}

	/// <summary>远程栏接收：本机拖来的文件 → 上传；资源管理器拖来的 → 上传。</summary>
	void initremotedrop() {
		lvremote.AllowDrop = true;
		lvremote.DragOver += (_, e) => {
			if (!canaccept(e.Data)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
			e.Effects = DragDropEffects.Copy;
			e.Handled = true;
		};
		lvremote.Drop += (_, e) => {
			if (!canaccept(e.Data)) return;
			e.Handled = true;
			if (browse == null) {
				setstat(Loc.T("ftp.err.noconn"));
				return;
			}
			var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (paths == null) return;
			uploadpaths(paths);
		};

		// 本机栏接收远程拖来的文件 → 下载
		lvlocal.DragOver += (_, e) => {
			if (!e.Data.GetDataPresent("ScreenKitFtpRemote")) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
			e.Effects = DragDropEffects.Copy;
			e.Handled = true;
		};
		lvlocal.Drop += (_, e) => {
			if (!e.Data.GetDataPresent("ScreenKitFtpRemote")) return;
			e.Handled = true;
			if (e.Data.GetData("ScreenKitFtpRemote") is not List<FtpEntry> items) return;
			downloaditems(items);
		};
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
		var ld = (last.LocalDir ?? "").Trim();
		if (ld.Length == 0 || !Directory.Exists(ld)) ld = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		loadlocal(ld);
		var rd = (last.RemoteDir ?? "").Trim();
		remoteDir = rd.Length > 0 ? rd : "/";
		eremotepath.Text = remoteDir;
	}
	#endregion

	// ==================== 连接 ====================
	#region
	void connect() {
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
					bconn.IsEnabled = true;
					setstat(string.Format(Loc.T("ftp.err.connect"), ex.Message));
				});
			}
		});
	}

	void onconnected(FtpSite site, IFtpBackend backend) {
		if (dead) {
			try { backend.Dispose(); } catch { }
			return;
		}
		browse = backend;
		curSite = site;
		bconn.IsEnabled = true;
		var wd = "/";
		try { wd = backend.WorkDir(); }
		catch { }
		remoteDir = wd;
		setstat(string.Format(Loc.T("ftp.connected"), site.Kind, site.Host, site.PortOr(FtpProto.DefPort(site.Kind))));
		loadremote(remoteDir);
	}
	#endregion

	// ==================== 本机浏览 ====================
	#region
	void loadlocal(string dir) {
		var d = (dir ?? "").Trim();
		if (d.Length == 0) return;
		// 空路径或相对路径按当前目录解析
		try { d = Path.GetFullPath(d); }
		catch {
			setstat(Loc.T("ftp.err.badpath"));
			return;
		}
		if (!Directory.Exists(d)) {
			setstat(string.Format(Loc.T("ftp.err.nodir"), d));
			return;
		}
		localRows.Clear();
		try {
			var di = new DirectoryInfo(d);
			foreach (var sub in di.GetDirectories()) {
				localRows.Add(new FtpEntry {
					Name = sub.Name,
					Full = sub.FullName,
					IsDir = true,
					Modified = sub.LastWriteTime
				});
			}
			foreach (var f in di.GetFiles()) {
				localRows.Add(new FtpEntry {
					Name = f.Name,
					Full = f.FullName,
					IsDir = false,
					Size = f.Length,
					Modified = f.LastWriteTime
				});
			}
		}
		catch (Exception ex) {
			setstat(string.Format(Loc.T("ftp.err.list"), ex.Message));
			return;
		}
		localDir = d;
		elocalpath.Text = d;
		updateupbtn();
	}

	void localup() {
		var parent = Path.GetDirectoryName(localDir.TrimEnd('\\', '/'));
		if (string.IsNullOrEmpty(parent)) return;
		loadlocal(parent);
	}

	void localmkdir() {
		var name = askname(Loc.T("ftp.prompt.newdir"));
		if (string.IsNullOrWhiteSpace(name)) return;
		try {
			Directory.CreateDirectory(Path.Combine(localDir, name.Trim()));
			loadlocal(localDir);
		}
		catch (Exception ex) { setstat(ex.Message); }
	}

	void localdel() {
		var items = lvlocal.SelectedItems.Cast<FtpEntry>().ToList();
		if (items.Count == 0) return;
		if (MessageBox.Show(string.Format(Loc.T("ftp.confirm.del"), items.Count),
			Loc.T("ftp.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
		foreach (var it in items) {
			try {
				if (it.IsDir) Directory.Delete(it.Full, true);
				else File.Delete(it.Full);
			}
			catch (Exception ex) { setstat(ex.Message); }
		}
		loadlocal(localDir);
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
				if (dead) return;
				if (err != null) {
					setstat(string.Format(Loc.T("ftp.err.list"), err));
					return;
				}
				remoteRows.Clear();
				foreach (var one in got) remoteRows.Add(one);
				remoteDir = d;
				eremotepath.Text = d;
				updateupbtn();
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
		// 本机已是盘根时禁掉上级
		var p = Path.GetDirectoryName(localDir.TrimEnd('\\', '/'));
		blocalup.IsEnabled = !string.IsNullOrEmpty(p);
		bremoteup.IsEnabled = browse != null && remoteDir.TrimEnd('/').Length > 0;
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

	void downloaditems(List<FtpEntry> items) {
		if (curSite == null || browse == null) {
			setstat(Loc.T("ftp.err.noconn"));
			return;
		}
		var pw = epass.Password ?? "";
		int n = 0;
		foreach (var it in items) {
			if (it.IsDir) {
				// 目录下载：展开远端目录树，逐文件入队
				var files = expandRemoteDir(it.Full);
				foreach (var f in files) {
					var rel = f.Full.Substring(it.Full.Length).TrimStart('/').Replace('/', '\\');
					var local = Path.Combine(localDir, it.Name, rel);
					xfer.EnqueueDownload(curSite, f.Full, local, f.Size, pw);
					n++;
				}
			}
			else {
				var local = Path.Combine(localDir, it.Name);
				xfer.EnqueueDownload(curSite, it.Full, local, it.Size, pw);
				n++;
			}
		}
		if (n > 0) setstat(string.Format(Loc.T("ftp.queued.down"), n));
	}

	/// <summary>递归列远端目录。浏览会话在 UI 线程外用时注意串行，这里直接跑在调用方的后台任务里。</summary>
	List<FtpEntry> expandRemoteDir(string dir) {
		var list = new List<FtpEntry>();
		try {
			var items = browse.List(dir);
			foreach (var it in items) {
				if (it.IsDir) list.AddRange(expandRemoteDir(it.Full));
				else list.Add(it);
			}
		}
		catch { }
		return list;
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
		bconn.Content = Loc.T("ftp.connect");
		bsave.Content = Loc.T("ftp.save");
		lblocal.Text = Loc.T("ftp.local");
		lbremote.Text = Loc.T("ftp.remote");
		blocalup.Content = Loc.T("ftp.up");
		blocalrefresh.Content = Loc.T("ftp.refresh");
		blocalmkdir.Content = Loc.T("ftp.mkdir");
		blocaldel.Content = Loc.T("ftp.del");
		bremoteup.Content = Loc.T("ftp.up");
		bremoterefresh.Content = Loc.T("ftp.refresh");
		bremotemkdir.Content = Loc.T("ftp.mkdir");
		bremotedel.Content = Loc.T("ftp.del");
		lbxfer.Text = Loc.T("ftp.xfer");
		lbparal.Text = Loc.T("ftp.parallel");
		bclearfinished.Content = Loc.T("ftp.clear");
		colllname.Header = Loc.T("ftp.colname");
		colllsize.Header = Loc.T("ftp.colsize");
		collltime.Header = Loc.T("ftp.coltime");
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
