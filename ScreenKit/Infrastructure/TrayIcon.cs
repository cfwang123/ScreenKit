using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ScreenKit;

/// <summary>菜单右侧快捷键用灰色，与主窗 InputGestureText 一致。高亮时仍用系统高亮色。</summary>
class MenuShortcutRenderer : Forms.ToolStripProfessionalRenderer {
	protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e) {
		// Professional 会把非高亮文字改回 MenuItemText，快捷键要自己画成灰色
		if (e?.Item is Forms.ToolStripMenuItem mi && mi.Enabled && !mi.Selected
			&& !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString)
			&& string.Equals(e.Text, mi.ShortcutKeyDisplayString, StringComparison.Ordinal)) {
			Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle,
				System.Drawing.SystemColors.GrayText, e.TextFormat);
			return;
		}
		base.OnRenderItemText(e);
	}
}

/// <summary>
/// 通知栏图标：左键与主窗热键相同（前台则隐藏，否则显示并置顶），右键菜单。
/// </summary>
sealed partial class TrayIcon : IDisposable {
	readonly Forms.NotifyIcon ni;
	readonly Window win;
	Forms.ContextMenuStrip menu;
	Forms.ToolStripMenuItem miShow;
	Forms.ToolStripMenuItem miOcr;
	Forms.ToolStripMenuItem miSnap;
	Forms.ToolStripMenuItem miScreenSnap;
	Forms.ToolStripMenuItem miBoard;
	Forms.ToolStripMenuItem miVoice;
	Forms.ToolStripMenuItem miTranslate;
	Forms.ToolStripMenuItem miDictSel;
	Forms.ToolStripMenuItem miClipFile;
	Forms.ToolStripMenuItem miSnapshots;
	Forms.ToolStripMenuItem miRecord;
	Forms.ToolStripMenuItem miRecordOpt;
	Forms.ToolStripMenuItem miGifRecord;
	Forms.ToolStripMenuItem miGifRecordOpt;
	Forms.ToolStripMenuItem miTools;
	Forms.ToolStripMenuItem miFav;
	Forms.ToolStripMenuItem miSettings;
	Forms.ToolStripMenuItem miSnapCopyImg;
	Forms.ToolStripMenuItem miSnapCopyFile;
	Forms.ToolStripMenuItem miSnapCopyPath;
	Forms.ToolStripMenuItem miExit;
	/// <summary>「工具」子菜单里按目录建出来的项，id 对应 <see cref="ToolDef.Id"/>。</summary>
	readonly Dictionary<string, Forms.ToolStripMenuItem> toolitems = new();
	bool disposed;
	bool snapCopyUi;
	bool dictSelUi;
	/// <summary>刚点了 radio：菜单正常关掉后在原位再打开（避免 Cancel 关掉导致点外面关不上）。</summary>
	bool reopenAfterRadio;
	/// <summary>菜单屏幕坐标（Opened 时记录，用于 reopen）。</summary>
	System.Drawing.Point menuScreenLoc;

	/// <summary>顶栏几项的热键文案；工具项的热键由 <see cref="ToolHotkeyProvider"/> 给。</summary>
	public Func<(string show, string ocr, string snap, string screensnap, string board, string voice, string tr, string dictsel, string copy)> HotkeyProvider { get; set; }
	/// <summary>取工具目录里某一项的热键文案，参数是 <see cref="ToolDef"/>。</summary>
	public Func<ToolDef, string> ToolHotkeyProvider { get; set; }

	public TrayIcon(Window window) {
		win = window ?? throw new ArgumentNullException(nameof(window));
		ni = new Forms.NotifyIcon {
			Visible = true,
			Text = Loc.T("tray.tip"),
			Icon = loadicon(),
		};
		// 按下即切换：等 MouseClick（松开）时主窗早已失活，慢点会误判成置前
		ni.MouseDown += (_, e) => {
			if (e.Button == Forms.MouseButtons.Left)
				togglewindow();
		};

		menu = new Forms.ContextMenuStrip();
		// 与主窗菜单一致：悬停显示功能说明
		menu.ShowItemToolTips = true;
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
			try { filltrayfav(); } catch { }
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
		miScreenSnap = item("tray.screensnap", () => ScreenSnapRequested?.Invoke());
		miBoard = item("tray.board", () => BoardRequested?.Invoke());
		miVoice = item("tray.voice", () => VoiceRequested?.Invoke());
		miTranslate = item("tray.translate", () => TranslateRequested?.Invoke());
		miDictSel = new Forms.ToolStripMenuItem(Loc.T("dict.selbar")) { CheckOnClick = true };
		miDictSel.CheckedChanged += (_, _) => {
			if (dictSelUi) return;
			try { DictSelBarChanged?.Invoke(miDictSel.Checked); } catch { }
		};
		miClipFile = item("tray.clipfile", () => ClipboardAsFileRequested?.Invoke());
		// 截图历史：打开截图历史窗口，不唤起主窗
		miSnapshots = item("tray.snapshots", () => SnapshotsRequested?.Invoke());
		miSnapshots.ToolTipText = Loc.T("menu.snapshots.tip");
		// 录屏 / GIF 录屏 / 参数 / 系统参数：只开功能窗，不唤起主窗
		miRecord = item("tray.record", () => RecordRequested?.Invoke());
		miRecordOpt = item("tray.recordopt", () => RecordOptionsRequested?.Invoke());
		miGifRecord = item("tray.gifrecord", () => GifRecordRequested?.Invoke());
		miGifRecordOpt = item("tray.gifrecordopt", () => GifRecordOptionsRequested?.Invoke());
		// 「工具」子菜单按 ToolCatalog 建，和主窗菜单、工具页、网页卡片同一份定义
		miTools = new Forms.ToolStripMenuItem(ToolCatalog.Plain(Loc.T("tray.tools")));
		filltools();
		miTools.DropDownOpening += (_, _) => filltools();
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
		menu.Items.Add(miScreenSnap);
		menu.Items.Add(miBoard);
		menu.Items.Add(miVoice);
		menu.Items.Add(miTranslate);
		menu.Items.Add(miDictSel);
		menu.Items.Add(miClipFile);
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
		menu.Items.Add(new Forms.ToolStripSeparator());
		miFav = new Forms.ToolStripMenuItem(Loc.T("web.cat.fav")) { Tag = "favhost", Visible = false };
		menu.Items.Add(miFav);
		menu.Items.Add(miTools);
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

	/// <summary>勾选项 Tag=radio 时画单选圆点，否则默认对勾。快捷键文字为灰色。</summary>
	sealed class RadioMenuRenderer : MenuShortcutRenderer {
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
	public void SetModules(bool ocr, bool asr, bool translate, bool dict) {
		try {
			if (miOcr != null) miOcr.Enabled = ocr;
			if (toolitems.TryGetValue("pdf", out var pdf) && pdf != null) pdf.Enabled = ocr;
			if (miVoice != null) miVoice.Enabled = asr;
			if (miTranslate != null) miTranslate.Enabled = translate;
			if (miDictSel != null) miDictSel.Enabled = dict;
			if (toolitems.TryGetValue("dict", out var dic) && dic != null) dic.Enabled = dict;
		}
		catch { }
	}

	public void ApplyHotkeys() {
		try { applyhotkeys(); } catch { }
	}

	/// <summary>托盘气泡提示（主窗隐藏时也可见）。</summary>
	public void ShowToast(string title, string text, int timeoutMs = 2500) {
		try {
			if (ni == null || disposed) return;
			if (!ni.Visible) ni.Visible = true;
			ni.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? AppNames.Current : title;
			ni.BalloonTipText = text ?? "";
			ni.BalloonTipIcon = Forms.ToolTipIcon.Info;
			var ms = timeoutMs < 500 ? 500 : (timeoutMs > 10000 ? 10000 : timeoutMs);
			ni.ShowBalloonTip(ms);
		}
		catch { }
	}

	void applyhotkeys() {
		string show = "", ocr = "", snap = "", screensnap = "", board = "", voice = "", tr = "", dictsel = "", copy = "";
		try {
			if (HotkeyProvider != null)
				(show, ocr, snap, screensnap, board, voice, tr, dictsel, copy) = HotkeyProvider();
		}
		catch { }
		setshortcut(miShow, show);
		setshortcut(miOcr, ocr);
		setshortcut(miSnap, snap);
		setshortcut(miScreenSnap, screensnap);
		setshortcut(miBoard, board);
		setshortcut(miVoice, voice);
		setshortcut(miTranslate, tr);
		setshortcut(miDictSel, dictsel);
		setshortcut(miSnapCopyImg, copy);
		setshortcut(miSnapCopyFile, copy);
		setshortcut(miSnapCopyPath, copy);
		setshortcut(miTools, null);
		setshortcut(miFav, null);
		setshortcut(miSettings, null);
		setshortcut(miExit, null);
		// 工具项的热键文案也来自目录，目录里没写 Hotkey 的一律清空
		foreach (var kv in toolitems) {
			var keys = "";
			try {
				var d = ToolCatalog.Find(kv.Key);
				if (d != null && d.Hotkey != null) keys = ToolHotkeyProvider?.Invoke(d) ?? "";
			}
			catch { }
			setshortcut(kv.Value, keys);
		}
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
			settext(miScreenSnap, "tray.screensnap");
			settext(miBoard, "tray.board");
			settext(miVoice, "tray.voice");
			settext(miTranslate, "tray.translate");
			settext(miDictSel, "dict.selbar");
			settext(miClipFile, "tray.clipfile");
			settext(miSnapshots, "tray.snapshots");
			if (miSnapshots != null) miSnapshots.ToolTipText = Loc.T("menu.snapshots.tip");
			settext(miSnapCopyImg, "tray.snapcopyimg");
			settext(miSnapCopyFile, "tray.snapcopyfile");
			settext(miSnapCopyPath, "tray.snapcopypath");
			settext(miRecord, "tray.record");
			settext(miRecordOpt, "tray.recordopt");
			settext(miGifRecord, "tray.gifrecord");
			settext(miGifRecordOpt, "tray.gifrecordopt");
			settext(miSettings, "tray.settings");
			settext(miExit, "tray.exit");
			// 「工具」整个按目录重建，名字、提示、(web) 后缀都跟着语言走
			filltools();
			applyhotkeys();
		}
		catch { }
	}

	static void settext(Forms.ToolStripMenuItem mi, string key) {
		if (mi != null) mi.Text = ToolCatalog.Plain(Loc.T(key));
	}

	/// <summary>按 ToolCatalog 重建「工具」子菜单：顶部几项，再是各分类。</summary>
	void filltools() {
		miTools.DropDownItems.Clear();
		toolitems.Clear();
		foreach (var d in ToolCatalog.Top(ToolPlace.Tray)) {
			var mi = toolitem(d);
			if (mi != null) miTools.DropDownItems.Add(mi);
		}
		miTools.DropDownItems.Add(new Forms.ToolStripSeparator());
		foreach (var cat in ToolCatalog.CatsWith(ToolPlace.Tray)) {
			var host = new Forms.ToolStripMenuItem(ToolCatalog.Plain(Loc.T(cat.NameKey)));
			var tip = ToolCatalog.CatTip(cat);
			if (tip.Length > 0) host.ToolTipText = tip;
			foreach (var d in ToolCatalog.InCat(ToolPlace.Tray, cat.Id)) {
				var mi = toolitem(d);
				if (mi != null) host.DropDownItems.Add(mi);
			}
			if (host.DropDownItems.Count == 0) {
				host.Dispose();
				continue;
			}
			miTools.DropDownItems.Add(host);
		}
	}

	Forms.ToolStripMenuItem toolitem(ToolDef d) {
		if (d == null) return null;
		var mi = new Forms.ToolStripMenuItem(ToolCatalog.TrayLabel(d)) { Checked = false };
		var tip = ToolCatalog.TipOf(d);
		if (tip.Length > 0) mi.ToolTipText = tip;
		var id = d.Id;
		mi.Click += (_, _) => {
			try { ToolRequested?.Invoke(id); } catch { }
		};
		toolitems[id] = mi;
		return mi;
	}

	/// <summary>有收藏才显示「收藏工具」，和「工具」并列、排在它前面。</summary>
	void filltrayfav() {
		var host = miFav;
		if (host == null) return;
		host.DropDownItems.Clear();
		var raw = "";
		try { raw = ToolFavProvider?.Invoke() ?? ""; } catch { }
		var ids = ToolCatalog.KeepFav((raw ?? "").Split(','));
		host.Visible = ids.Count > 0;
		if (ids.Count == 0) return;
		host.Text = ToolCatalog.Plain(Loc.T("web.cat.fav"));
		foreach (var id in ids) {
			var d = ToolCatalog.Find(id);
			if (d == null) continue;
			var name = ToolCatalog.TrayLabel(d);
			var open = d.Id;
			host.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) => {
				try { ToolRequested?.Invoke(open); } catch { }
			}));
		}
	}

	public void SetDictSelBarChecked(bool on) {
		dictSelUi = true;
		try {
			if (miDictSel != null) miDictSel.Checked = on;
		}
		catch { }
		dictSelUi = false;
	}

	public void SetHangul(bool on) {
		if (toolitems.TryGetValue("hangul", out var mi) && mi != null) mi.Checked = on;
	}

	public void SetHangulAvailable(bool ok) {
		if (!toolitems.TryGetValue("hangul", out var mi) || mi == null) return;
		mi.Enabled = ok;
		if (!ok) mi.Checked = false;
	}

	public event Action SnapshotsRequested;
	public event Action RecordRequested;
	public event Action RecordOptionsRequested;
	public event Action GifRecordRequested;
	public event Action GifRecordOptionsRequested;
	public event Action ClipboardAsFileRequested;
	public event Action OcrRequested;
	public event Action SnapRequested;
	public event Action ScreenSnapRequested;
	public event Action BoardRequested;
	public event Action VoiceRequested;
	public event Action TranslateRequested;
	public event Action<bool> DictSelBarChanged;
	/// <summary>工具目录里的某一项被点了，参数是 <see cref="ToolDef.Id"/>。</summary>
	public event Action<string> ToolRequested;
	/// <summary>收藏的卡片 id，逗号分隔。空时托盘不显示「收藏工具」。</summary>
	public Func<string> ToolFavProvider;
	public event Action SettingsRequested;
	public event Action ForceExitRequested;
	/// <summary>托盘勾选「复制为图片 / 文件 / 路径」变更（asImage, asFile, asPath）。</summary>
	public event Action<bool, bool, bool> SnapCopyOptionsChanged;
	/// <summary>右键菜单即将打开（用于记录主窗可见性，避免菜单关闭后误判）。</summary>
	public event Action MenuOpening;
	/// <summary>左键单击：由主窗按前台状态决定显示、隐藏或置顶。</summary>
	public event Action ToggleRequested;

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
		if (ToggleRequested != null) {
			try { ToggleRequested(); } catch { }
			return;
		}
		try {
			if (win.IsVisible && win.WindowState != WindowState.Minimized)
				hidewindow();
			else
				showwindow();
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
