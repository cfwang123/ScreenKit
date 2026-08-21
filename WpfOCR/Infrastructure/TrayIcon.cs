using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace WpfOCR;

/// <summary>
/// 通知栏图标：单击切换显示/隐藏，右键菜单。
/// </summary>
sealed class TrayIcon : IDisposable {
	readonly Forms.NotifyIcon ni;
	readonly Window win;
	Forms.ContextMenuStrip menu;
	Forms.ToolStripMenuItem miShow;
	Forms.ToolStripMenuItem miOcr;
	Forms.ToolStripMenuItem miSnap;
	Forms.ToolStripMenuItem miBoard;
	Forms.ToolStripMenuItem miClip;
	Forms.ToolStripMenuItem miClipFile;
	Forms.ToolStripMenuItem miPdf;
	Forms.ToolStripMenuItem miSnapshots;
	Forms.ToolStripMenuItem miRecord;
	Forms.ToolStripMenuItem miRecordOpt;
	Forms.ToolStripMenuItem miGifRecord;
	Forms.ToolStripMenuItem miGifRecordOpt;
	Forms.ToolStripMenuItem miSettings;
	Forms.ToolStripMenuItem miSnapCopyImg;
	Forms.ToolStripMenuItem miSnapCopyFile;
	Forms.ToolStripMenuItem miSnapCopyPath;
	Forms.ToolStripMenuItem miExit;
	bool disposed;
	bool snapCopyUi;
	/// <summary>刚点了 radio：菜单正常关掉后在原位再打开（避免 Cancel 关掉导致点外面关不上）。</summary>
	bool reopenAfterRadio;
	/// <summary>菜单屏幕坐标（Opened 时记录，用于 reopen）。</summary>
	System.Drawing.Point menuScreenLoc;

	/// <summary>返回 (呼出, 截图识别, 截图标注, 屏幕画板) 快捷键文案；空串表示无。</summary>
	public Func<(string show, string ocr, string snap, string board)> HotkeyProvider { get; set; }

	public TrayIcon(Window window) {
		win = window ?? throw new ArgumentNullException(nameof(window));
		ni = new Forms.NotifyIcon {
			Visible = true,
			Text = Loc.T("tray.tip"),
			Icon = loadicon(),
		};
		ni.MouseClick += (_, e) => {
			if (e.Button == Forms.MouseButtons.Left)
				togglewindow();
		};

		menu = new Forms.ContextMenuStrip();
		// 勾选项用圆点（radio）而非对勾
		menu.Renderer = new RadioMenuRenderer();
		menu.Opening += (_, _) => {
			reopenAfterRadio = false;
			try {
				miClipFile.Enabled = ImageUtil.Hasclipboardimage();
			}
			catch {
				miClipFile.Enabled = false;
			}
			try { applyhotkeys(); } catch { }
			// 菜单关闭时常把主窗拉前台；调用方可在此记下「点菜单前」是否可见
			try { MenuOpening?.Invoke(); } catch { }
		};
		menu.Opened += (_, _) => {
			try { menuScreenLoc = menu.Bounds.Location; } catch { }
		};
		// 不 Cancel Closing：托盘上 Cancel AppFocusChange 会导致之后点外面关不上。
		// radio 点选：允许正常关闭，Closed 后再 Show 到原位置。
		menu.Closed += (_, _) => {
			if (!reopenAfterRadio) return;
			reopenAfterRadio = false;
			var loc = menuScreenLoc;
			try {
				if (menu == null || disposed) return;
				// 下一拍再开，避免同栈里关/开冲突
				menu.BeginInvoke(new Action(() => {
					try {
						if (disposed || menu == null) return;
						if (menu.Visible) return;
						if (loc.X == 0 && loc.Y == 0)
							loc = Forms.Control.MousePosition;
						menu.Show(loc);
					}
					catch { }
				}));
			}
			catch {
				try {
					if (!disposed && menu != null && !menu.Visible)
						menu.Show(loc.X == 0 && loc.Y == 0 ? Forms.Control.MousePosition : loc);
				}
				catch { }
			}
		};
		menu.ItemClicked += (_, e) => {
			// 仅 radio 需要关后重开；其它项保持关掉
			reopenAfterRadio = e.ClickedItem is Forms.ToolStripMenuItem mi
				&& Equals(mi.Tag, "radio");
		};

		miShow = item("tray.show", () => showwindow());
		miOcr = item("tray.ocr", () => OcrRequested?.Invoke());
		miSnap = item("tray.snap", () => SnapRequested?.Invoke());
		miBoard = item("tray.board", () => BoardRequested?.Invoke());
		miClip = item("tray.clip", () => {
			showwindow();
			ClipboardOcrRequested?.Invoke();
		});
		miClipFile = item("tray.clipfile", () => ClipboardAsFileRequested?.Invoke());
		miPdf = item("tray.pdf", () => {
			showwindow();
			PdfRequested?.Invoke();
		});
		// 截图历史：打开 screenshots 文件夹，不唤起主窗
		miSnapshots = item("tray.snapshots", () => SnapshotsRequested?.Invoke());
		// 录屏 / GIF 录屏 / 参数 / 系统参数：只开功能窗，不唤起主窗
		miRecord = item("tray.record", () => RecordRequested?.Invoke());
		miRecordOpt = item("tray.recordopt", () => RecordOptionsRequested?.Invoke());
		miGifRecord = item("tray.gifrecord", () => GifRecordRequested?.Invoke());
		miGifRecordOpt = item("tray.gifrecordopt", () => GifRecordOptionsRequested?.Invoke());
		miSettings = item("tray.settings", () => SettingsRequested?.Invoke());
		miSnapCopyImg = checkitem("tray.snapcopyimg", true);
		miSnapCopyFile = checkitem("tray.snapcopyfile", true);
		miSnapCopyPath = checkitem("tray.snapcopypath", true);
		miSnapCopyImg.CheckedChanged += (s, _) => onsnapcopychanged(s);
		miSnapCopyFile.CheckedChanged += (s, _) => onsnapcopychanged(s);
		miSnapCopyPath.CheckedChanged += (s, _) => onsnapcopychanged(s);
		miExit = item("tray.exit", () => {
			try { ni.Visible = false; } catch { }
			ForceExitRequested?.Invoke();
		});

		menu.Items.Add(miShow);
		menu.Items.Add(new Forms.ToolStripSeparator());
		menu.Items.Add(miOcr);
		menu.Items.Add(miSnap);
		menu.Items.Add(miBoard);
		menu.Items.Add(miClip);
		menu.Items.Add(miClipFile);
		menu.Items.Add(miPdf);
		menu.Items.Add(miSnapshots);
		menu.Items.Add(new Forms.ToolStripSeparator());
		menu.Items.Add(miSnapCopyImg);
		menu.Items.Add(miSnapCopyFile);
		menu.Items.Add(miSnapCopyPath);
		menu.Items.Add(new Forms.ToolStripSeparator());
		menu.Items.Add(miRecord);
		menu.Items.Add(miRecordOpt);
		menu.Items.Add(miGifRecord);
		menu.Items.Add(miGifRecordOpt);
		menu.Items.Add(miSettings);
		menu.Items.Add(new Forms.ToolStripSeparator());
		menu.Items.Add(miExit);
		ni.ContextMenuStrip = menu;
		applyhotkeys();
	}

	static Forms.ToolStripMenuItem item(string locKey, Action act) {
		return new Forms.ToolStripMenuItem(Loc.T(locKey), null, (_, _) => {
			try { act?.Invoke(); } catch { }
		});
	}

	Forms.ToolStripMenuItem checkitem(string locKey, bool checked0) {
		var mi = new Forms.ToolStripMenuItem(Loc.T(locKey)) {
			CheckOnClick = true,
			Checked = checked0,
			// RadioMenuRenderer 据此画圆点
			Tag = "radio",
		};
		// 再保险：Click 也标 reopen（键盘选中时 ItemClicked 仍会走）
		mi.Click += (_, _) => { reopenAfterRadio = true; };
		return mi;
	}

	/// <summary>勾选项 Tag=radio 时画单选圆点，否则默认对勾。</summary>
	sealed class RadioMenuRenderer : Forms.ToolStripProfessionalRenderer {
		protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e) {
			if (e?.Item is Forms.ToolStripMenuItem mi
				&& mi.Checked
				&& Equals(mi.Tag, "radio")) {
				var g = e.Graphics;
				var old = g.SmoothingMode;
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				var r = e.ImageRectangle;
				int s = Math.Min(r.Width, r.Height) - 2;
				if (s < 8) s = 8;
				int cx = r.X + r.Width / 2;
				int cy = r.Y + r.Height / 2;
				var outer = new Rectangle(cx - s / 2, cy - s / 2, s, s);
				using (var pen = new System.Drawing.Pen(System.Drawing.SystemColors.MenuText, 1.1f))
					g.DrawEllipse(pen, outer);
				int inn = Math.Max(3, s / 2 - 1);
				var inner = new Rectangle(cx - inn / 2, cy - inn / 2, inn, inn);
				using (var br = new SolidBrush(System.Drawing.SystemColors.MenuText))
					g.FillEllipse(br, inner);
				g.SmoothingMode = old;
				return;
			}
			base.OnRenderItemCheck(e);
		}
	}

	void onsnapcopychanged(object sender) {
		if (snapCopyUi) return;
		// radio：点哪项就选哪项，其余关掉
		var pathMode = sender == miSnapCopyPath;
		var fileMode = sender == miSnapCopyFile;
		var imgMode = !pathMode && !fileMode;
		snapCopyUi = true;
		try {
			if (miSnapCopyImg != null) miSnapCopyImg.Checked = imgMode;
			if (miSnapCopyFile != null) miSnapCopyFile.Checked = fileMode;
			if (miSnapCopyPath != null) miSnapCopyPath.Checked = pathMode;
		}
		finally { snapCopyUi = false; }
		// 延迟通知主窗，避免与菜单关/重开抢同一拍
		var asImg = imgMode;
		var asFile = fileMode;
		var asPath = pathMode;
		try {
			if (menu != null && menu.IsHandleCreated) {
				menu.BeginInvoke(new Action(() => {
					try { SnapCopyOptionsChanged?.Invoke(asImg, asFile, asPath); } catch { }
				}));
				return;
			}
		}
		catch { }
		try { SnapCopyOptionsChanged?.Invoke(asImg, asFile, asPath); } catch { }
	}

	/// <summary>同步「复制为图片 / 文件 / 路径」单选状态（不触发变更事件）。</summary>
	public void SetSnapCopyOptions(bool asImage, bool asFile, bool asPath = false) {
		var pathMode = asPath && !asImage && !asFile;
		var fileMode = !pathMode && asFile && !asImage;
		var imgMode = !pathMode && !fileMode;
		snapCopyUi = true;
		try {
			if (miSnapCopyImg != null) miSnapCopyImg.Checked = imgMode;
			if (miSnapCopyFile != null) miSnapCopyFile.Checked = fileMode;
			if (miSnapCopyPath != null) miSnapCopyPath.Checked = pathMode;
		}
		finally { snapCopyUi = false; }
	}

	/// <summary>刷新右侧快捷键显示（不注册 WinForms 快捷键，仅展示）。</summary>
	public void ApplyHotkeys() {
		try { applyhotkeys(); } catch { }
	}

	/// <summary>托盘气泡提示（主窗隐藏时也可见）。</summary>
	public void ShowToast(string title, string text, int timeoutMs = 2500) {
		try {
			if (ni == null || disposed) return;
			if (!ni.Visible) ni.Visible = true;
			ni.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "WpfOCR" : title;
			ni.BalloonTipText = text ?? "";
			ni.BalloonTipIcon = Forms.ToolTipIcon.Info;
			var ms = timeoutMs < 500 ? 500 : (timeoutMs > 10000 ? 10000 : timeoutMs);
			ni.ShowBalloonTip(ms);
		}
		catch { }
	}

	void applyhotkeys() {
		string show = "", ocr = "", snap = "", board = "";
		try {
			if (HotkeyProvider != null)
				(show, ocr, snap, board) = HotkeyProvider();
		}
		catch { }
		setshortcut(miShow, show);
		setshortcut(miOcr, ocr);
		setshortcut(miSnap, snap);
		setshortcut(miBoard, board);
		// 其余无全局热键，清空以免残留
		setshortcut(miClip, null);
		setshortcut(miClipFile, null);
		setshortcut(miPdf, null);
		setshortcut(miSnapshots, null);
		setshortcut(miSnapCopyImg, null);
		setshortcut(miSnapCopyFile, null);
		setshortcut(miSnapCopyPath, null);
		setshortcut(miRecord, null);
		setshortcut(miRecordOpt, null);
		setshortcut(miGifRecord, null);
		setshortcut(miGifRecordOpt, null);
		setshortcut(miSettings, null);
		setshortcut(miExit, null);
	}

	static void setshortcut(Forms.ToolStripMenuItem mi, string keys) {
		if (mi == null) return;
		// 仅显示，不绑定 Forms 快捷键（全局热键由 GlobalHotkey 处理）
		mi.ShowShortcutKeys = true;
		mi.ShortcutKeyDisplayString = string.IsNullOrWhiteSpace(keys) ? "" : keys.Trim();
	}

	public void ApplyLang() {
		try {
			ni.Text = Loc.T("tray.tip");
			settext(miShow, "tray.show");
			settext(miOcr, "tray.ocr");
			settext(miSnap, "tray.snap");
			settext(miBoard, "tray.board");
			settext(miClip, "tray.clip");
			settext(miClipFile, "tray.clipfile");
			settext(miPdf, "tray.pdf");
			settext(miSnapshots, "tray.snapshots");
			settext(miSnapCopyImg, "tray.snapcopyimg");
			settext(miSnapCopyFile, "tray.snapcopyfile");
			settext(miSnapCopyPath, "tray.snapcopypath");
			settext(miRecord, "tray.record");
			settext(miRecordOpt, "tray.recordopt");
			settext(miGifRecord, "tray.gifrecord");
			settext(miGifRecordOpt, "tray.gifrecordopt");
			settext(miSettings, "tray.settings");
			settext(miExit, "tray.exit");
			applyhotkeys();
		}
		catch { }
	}

	static void settext(Forms.ToolStripMenuItem mi, string key) {
		if (mi != null) mi.Text = Loc.T(key);
	}

	public event Action ClipboardOcrRequested;
	public event Action ClipboardAsFileRequested;
	public event Action OcrRequested;
	public event Action SnapRequested;
	public event Action BoardRequested;
	public event Action PdfRequested;
	public event Action SnapshotsRequested;
	public event Action RecordRequested;
	public event Action RecordOptionsRequested;
	public event Action GifRecordRequested;
	public event Action GifRecordOptionsRequested;
	public event Action SettingsRequested;
	public event Action ForceExitRequested;
	/// <summary>托盘勾选「复制为图片 / 文件 / 路径」变更（asImage, asFile, asPath）。</summary>
	public event Action<bool, bool, bool> SnapCopyOptionsChanged;
	/// <summary>右键菜单即将打开（用于记录主窗可见性，避免菜单关闭后误判）。</summary>
	public event Action MenuOpening;

	public void showwindow() {
		try {
			if (!win.IsVisible) win.Show();
			if (win.WindowState == WindowState.Minimized)
				win.WindowState = WindowState.Normal;
			win.Activate();
			win.Topmost = true;
			win.Topmost = false;
			win.Focus();
		}
		catch { }
	}

	public void hidewindow() {
		try { win.Hide(); } catch { }
	}

	public void togglewindow() {
		try {
			if (win.IsVisible && win.WindowState != WindowState.Minimized)
				hidewindow();
			else
				showwindow();
		}
		catch { }
	}

	public void balloon(string title, string text, int ms = 2000) {
		try {
			ni.BalloonTipTitle = title ?? "WpfOCR";
			ni.BalloonTipText = text ?? "";
			ni.ShowBalloonTip(ms);
		}
		catch { }
	}

	static Icon loadicon() {
		foreach (var path in iconpaths()) {
			try {
				if (!File.Exists(path)) continue;
				var bytes = File.ReadAllBytes(path);
				using var ms = new MemoryStream(bytes);
				using var ico = new Icon(ms);
				return (Icon)ico.Clone();
			}
			catch { }
		}
		try {
			var path = Compat.ProcessPath
				?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrEmpty(path)) {
				var ico = Icon.ExtractAssociatedIcon(path);
				if (ico != null) return ico;
			}
		}
		catch { }
		return SystemIcons.Application;
	}

	static IEnumerable<string> iconpaths() {
		var baseDir = AppDomain.CurrentDomain.BaseDirectory;
		yield return Path.Combine(baseDir, "Assets", "app.ico");
		yield return Path.Combine(baseDir, "app.ico");
		yield return Path.Combine(baseDir, "..", "..", "..", "Assets", "app.ico");
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		try { ni.Visible = false; } catch { }
		try { ni.Dispose(); } catch { }
	}
}
