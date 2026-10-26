using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
sealed class TrayIcon : IDisposable {
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
	Forms.ToolStripMenuItem miPdf;
	Forms.ToolStripMenuItem miSnapshots;
	Forms.ToolStripMenuItem miRecord;
	Forms.ToolStripMenuItem miRecordOpt;
	Forms.ToolStripMenuItem miGifRecord;
	Forms.ToolStripMenuItem miGifRecordOpt;
	Forms.ToolStripMenuItem miTools;
	Forms.ToolStripMenuItem miWebHome;
	Forms.ToolStripMenuItem miWebFiles;
	Forms.ToolStripMenuItem miWebDict;
	Forms.ToolStripMenuItem miCatText;
	Forms.ToolStripMenuItem miCatCalc;
	Forms.ToolStripMenuItem miCatSpeech;
	Forms.ToolStripMenuItem miCatTime;
	Forms.ToolStripMenuItem miCatImage;
	Forms.ToolStripMenuItem miCatTalk;
	Forms.ToolStripMenuItem miCatFile;
	Forms.ToolStripMenuItem miCatSys;
	Forms.ToolStripMenuItem miImgConv;
	Forms.ToolStripMenuItem miCharArt;
	Forms.ToolStripMenuItem miPixel;
	Forms.ToolStripMenuItem miVidConv;
	Forms.ToolStripMenuItem miVidGif;
	Forms.ToolStripMenuItem miQrMake;
	Forms.ToolStripMenuItem miRename;
	Forms.ToolStripMenuItem miHash;
	Forms.ToolStripMenuItem miTextTool;
	Forms.ToolStripMenuItem miMdRead;
	Forms.ToolStripMenuItem miPwGen;
	Forms.ToolStripMenuItem miSciCalc;
	Forms.ToolStripMenuItem miNote;
	Forms.ToolStripMenuItem miRtspSrv;
	Forms.ToolStripMenuItem miNet;
	Forms.ToolStripMenuItem miNetGroup;
	Forms.ToolStripMenuItem miInfo;
	Forms.ToolStripMenuItem miPortUse;
	Forms.ToolStripMenuItem miMidi;
	Forms.ToolStripMenuItem miVoiceRec;
	Forms.ToolStripMenuItem miCam;
	Forms.ToolStripMenuItem miSys;
	Forms.ToolStripMenuItem miSysTool;
	Forms.ToolStripMenuItem miFileProbe;
	Forms.ToolStripMenuItem miPerfMon;
	Forms.ToolStripMenuItem miHttpDbg;
	Forms.ToolStripMenuItem miZhConv;
	Forms.ToolStripMenuItem miPinyin;
	Forms.ToolStripMenuItem miHangul;

	Forms.ToolStripMenuItem miWinCal;
	Forms.ToolStripMenuItem miJpYomi;
	Forms.ToolStripMenuItem miWinTop;
	Forms.ToolStripMenuItem miAutoKey;
	Forms.ToolStripMenuItem miFastCopy;
	Forms.ToolStripMenuItem miGlyphMod;
	Forms.ToolStripMenuItem miCast;
	Forms.ToolStripMenuItem miSettings;
	Forms.ToolStripMenuItem miSnapCopyImg;
	Forms.ToolStripMenuItem miSnapCopyFile;
	Forms.ToolStripMenuItem miSnapCopyPath;
	Forms.ToolStripMenuItem miExit;
	bool disposed;
	bool snapCopyUi;
	bool dictSelUi;
	/// <summary>刚点了 radio：菜单正常关掉后在原位再打开（避免 Cancel 关掉导致点外面关不上）。</summary>
	bool reopenAfterRadio;
	/// <summary>菜单屏幕坐标（Opened 时记录，用于 reopen）。</summary>
	System.Drawing.Point menuScreenLoc;

	/// <summary>返回 (呼出, 截图识别, 截图标注, 重复上次截图, 屏幕画板, 语音输入, 翻译小窗, 划词浮窗, 切换复制方式, 输入法, web工具主页) 快捷键文案；空串表示无。</summary>
	public Func<(string show, string ocr, string snap, string screensnap, string board, string voice, string tr, string dictsel, string copy, string hangul, string web)> HotkeyProvider { get; set; }

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
		miPdf = item("menu.pdf", () => {
			showwindow();
			PdfRequested?.Invoke();
		});
		miPdf.ToolTipText = Loc.T("menu.pdf.tip");
		// 截图历史：打开 screenshots 文件夹，不唤起主窗
		miSnapshots = item("tray.snapshots", () => SnapshotsRequested?.Invoke());
		// 录屏 / GIF 录屏 / 参数 / 系统参数：只开功能窗，不唤起主窗
		miRecord = item("tray.record", () => RecordRequested?.Invoke());
		miRecordOpt = item("tray.recordopt", () => RecordOptionsRequested?.Invoke());
		miGifRecord = item("tray.gifrecord", () => GifRecordRequested?.Invoke());
		miGifRecordOpt = item("tray.gifrecordopt", () => GifRecordOptionsRequested?.Invoke());
		miImgConv = item("tray.imgconv", () => ImgConvRequested?.Invoke());
		miCharArt = item("tray.charart", () => CharArtRequested?.Invoke());
		miPixel = item("tray.pixel", () => PixelArtRequested?.Invoke());
		miVidConv = item("tray.vidconv", () => VidConvRequested?.Invoke());
		miVidGif = item("tray.vidgif", () => VidGifRequested?.Invoke());
		miQrMake = item("tray.qrmake", () => QrMakeRequested?.Invoke());
		miRename = item("tray.rename", () => RenameRequested?.Invoke());
		miHash = item("tray.hash", () => HashRequested?.Invoke());
		miTextTool = item("tray.texttool", () => TextToolRequested?.Invoke());
		miMdRead = item("tray.mdread", () => MdReadRequested?.Invoke());
		miPwGen = item("tray.pwgen", () => PwGenRequested?.Invoke());
		miSciCalc = item("tray.scicalc", () => SciCalcRequested?.Invoke());
		miNote = item("tray.note", () => NoteRequested?.Invoke());
		miRtspSrv = item("tray.rtspsrv", () => RtspSrvRequested?.Invoke());
		miNet = item("tray.net", () => NetToolRequested?.Invoke());
		miPortUse = item("tray.portuse", () => PortUseRequested?.Invoke());
		miMidi = item("tray.midisynth", () => MidiSynthRequested?.Invoke());
		miVoiceRec = item("tray.voicerec", () => VoiceRecRequested?.Invoke());
		miCam = item("tray.cam", () => CamRequested?.Invoke());
		miSys = item("tray.sysinfo", () => SysRequested?.Invoke());
		miSysTool = item("tray.systool", () => SysToolRequested?.Invoke());
		miFileProbe = item("tray.fileprobe", () => FileProbeRequested?.Invoke());
		miPerfMon = item("tray.perfmon", () => PerfMonRequested?.Invoke());
		miHttpDbg = item("tray.httpdbg", () => HttpDbgRequested?.Invoke());
		miZhConv = item("tray.zhconv", () => ZhConvRequested?.Invoke());
		miPinyin = item("tray.pinyin", () => PinyinRequested?.Invoke());
		miHangul = item("tray.hangul", () => HangulRequested?.Invoke());
		miWinCal = item("tray.wincal", () => WinCalRequested?.Invoke());
		miJpYomi = item("tray.jpyomi", () => JpYomiRequested?.Invoke());
		miWinTop = item("tray.wintop", () => WinTopRequested?.Invoke());
		miAutoKey = item("tray.autokey", () => AutoKeyRequested?.Invoke());
		miFastCopy = item("tray.fastcopy", () => FastCopyRequested?.Invoke());
		miGlyphMod = item("tray.glyphmod", () => GlyphModRequested?.Invoke());
		miCast = item("tray.cast", () => CastRequested?.Invoke());
		miWebHome = item("tray.webhome", () => WebHomeRequested?.Invoke());
		miWebFiles = item("tray.webfiles", () => WebFilesRequested?.Invoke());
		miWebDict = item("web.dict", () => WebDictRequested?.Invoke());
		miTools = new Forms.ToolStripMenuItem(Loc.T("tray.tools"));
		miTools.DropDownItems.Add(miWebHome);
		miTools.DropDownItems.Add(miWebFiles);
		miTools.DropDownItems.Add(miWebDict);
		miTools.DropDownItems.Add(miHangul);
		miTools.DropDownItems.Add(new Forms.ToolStripSeparator());
		miCatText = new Forms.ToolStripMenuItem(Loc.T("web.cat.text"));
		miCatText.DropDownItems.Add(miTextTool);
		miCatText.DropDownItems.Add(miMdRead);
		miCatText.DropDownItems.Add(miPwGen);
		miCatText.DropDownItems.Add(miNote);
		miCatText.DropDownItems.Add(miZhConv);
		miCatText.DropDownItems.Add(miPinyin);
		miCatText.DropDownItems.Add(miJpYomi);
		miCatText.DropDownItems.Add(miHash);
		miCatCalc = new Forms.ToolStripMenuItem(Loc.T("web.cat.calc"));
		miCatCalc.DropDownItems.Add(miSciCalc);
		miCatSpeech = new Forms.ToolStripMenuItem(Loc.T("web.cat.speech"));
		miCatSpeech.DropDownItems.Add(miVoiceRec);
		miCatSpeech.DropDownItems.Add(miMidi);
		miCatTime = new Forms.ToolStripMenuItem(Loc.T("web.cat.time"));
		miCatTime.DropDownItems.Add(miWinCal);
		miCatImage = new Forms.ToolStripMenuItem(Loc.T("web.cat.image"));
		miCatImage.DropDownItems.Add(miPdf);
		miCatImage.DropDownItems.Add(miImgConv);
		miCatImage.DropDownItems.Add(miCharArt);
		miCatImage.DropDownItems.Add(miPixel);
		miCatImage.DropDownItems.Add(miVidConv);
		miCatImage.DropDownItems.Add(miVidGif);
		miCatImage.DropDownItems.Add(miQrMake);
		miCatImage.DropDownItems.Add(miCam);
		miCatImage.DropDownItems.Add(miRtspSrv);
		miCatImage.DropDownItems.Add(miGlyphMod);
		miCatImage.DropDownItems.Add(miCast);
		miInfo = new Forms.ToolStripMenuItem(Loc.T("tray.info"));
		miInfo.DropDownItems.Add(miSys);
		miCatTalk = new Forms.ToolStripMenuItem(Loc.T("web.cat.talk"));
		miNetGroup = new Forms.ToolStripMenuItem(Loc.T("tray.netgroup"));
		miNetGroup.DropDownItems.Add(miNet);
		miNetGroup.DropDownItems.Add(miHttpDbg);
		miNetGroup.DropDownItems.Add(miPortUse);
		miCatFile = new Forms.ToolStripMenuItem(Loc.T("menu.cat.file"));
		miCatFile.DropDownItems.Add(miRename);
		miCatFile.DropDownItems.Add(miFastCopy);
		miCatFile.DropDownItems.Add(miFileProbe);
		miCatSys = new Forms.ToolStripMenuItem(Loc.T("menu.cat.sys"));
		miCatSys.DropDownItems.Add(miSysTool);
		miCatSys.DropDownItems.Add(miPerfMon);
		miCatSys.DropDownItems.Add(miWinTop);
		miCatSys.DropDownItems.Add(miAutoKey);
		miTools.DropDownItems.Add(miCatText);
		miTools.DropDownItems.Add(miCatCalc);
		miTools.DropDownItems.Add(miCatSpeech);
		miTools.DropDownItems.Add(miCatTime);
		miTools.DropDownItems.Add(miCatImage);
		miTools.DropDownItems.Add(miInfo);
		miTools.DropDownItems.Add(miCatTalk);
		miTools.DropDownItems.Add(miNetGroup);
		miTools.DropDownItems.Add(miCatFile);
		miTools.DropDownItems.Add(miCatSys);
		miTools.DropDownOpening += (_, _) => fillwebtools();
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
			if (miPdf != null) miPdf.Enabled = ocr;
			if (miVoice != null) miVoice.Enabled = asr;
			if (miTranslate != null) miTranslate.Enabled = translate;
			if (miDictSel != null) miDictSel.Enabled = dict;
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
		string show = "", ocr = "", snap = "", screensnap = "", board = "", voice = "", tr = "", dictsel = "", copy = "", hangul = "", web = "";
		try {
			if (HotkeyProvider != null)
				(show, ocr, snap, screensnap, board, voice, tr, dictsel, copy, hangul, web) = HotkeyProvider();
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
		// 其余无全局热键，清空以免残留
		setshortcut(miClipFile, null);
		setshortcut(miPdf, null);
		setshortcut(miSnapshots, null);
		setshortcut(miSnapCopyImg, copy);
		setshortcut(miSnapCopyFile, copy);
		setshortcut(miSnapCopyPath, copy);
		setshortcut(miRecord, null);
		setshortcut(miRecordOpt, null);
		setshortcut(miGifRecord, null);
		setshortcut(miGifRecordOpt, null);
		setshortcut(miTools, null);
		setshortcut(miWebHome, web);
		setshortcut(miImgConv, null);
		setshortcut(miCharArt, null);
		setshortcut(miPixel, null);
		setshortcut(miVidConv, null);
		setshortcut(miVidGif, null);
		setshortcut(miQrMake, null);
		setshortcut(miRename, null);
		setshortcut(miHash, null);
		setshortcut(miTextTool, null);
		setshortcut(miMdRead, null);
		setshortcut(miPwGen, null);
		setshortcut(miSciCalc, null);
		setshortcut(miNote, null);
		setshortcut(miRtspSrv, null);
		setshortcut(miNet, null);
		setshortcut(miNetGroup, null);
		setshortcut(miPortUse, null);
		setshortcut(miMidi, null);
		setshortcut(miVoiceRec, null);
		setshortcut(miCam, null);
		setshortcut(miSys, null);
		setshortcut(miHttpDbg, null);
		setshortcut(miZhConv, null);
		setshortcut(miPinyin, null);
		setshortcut(miHangul, hangul);
		setshortcut(miWinCal, null);
		setshortcut(miJpYomi, null);
		setshortcut(miWinTop, null);
		setshortcut(miAutoKey, null);
		setshortcut(miFastCopy, null);
		setshortcut(miFileProbe, null);
		setshortcut(miGlyphMod, null);
		setshortcut(miCast, null);
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
			settext(miScreenSnap, "tray.screensnap");
			settext(miBoard, "tray.board");
			settext(miVoice, "tray.voice");
			settext(miTranslate, "tray.translate");
			settext(miDictSel, "dict.selbar");
			settext(miClipFile, "tray.clipfile");
			settext(miPdf, "menu.pdf");
			if (miPdf != null) miPdf.ToolTipText = Loc.T("menu.pdf.tip");
			settext(miSnapshots, "tray.snapshots");
			settext(miSnapCopyImg, "tray.snapcopyimg");
			settext(miSnapCopyFile, "tray.snapcopyfile");
			settext(miSnapCopyPath, "tray.snapcopypath");
			settext(miRecord, "tray.record");
			settext(miRecordOpt, "tray.recordopt");
			settext(miGifRecord, "tray.gifrecord");
			settext(miGifRecordOpt, "tray.gifrecordopt");
			settext(miTools, "tray.tools");
			settext(miWebHome, "tray.webhome");
			settext(miWebFiles, "tray.webfiles");
			settext(miWebDict, "web.dict");
			settext(miCatText, "web.cat.text");
			settext(miCatCalc, "web.cat.calc");
			settext(miCatSpeech, "web.cat.speech");
			settext(miCatTime, "web.cat.time");
			settext(miCatImage, "web.cat.image");
			settext(miCatTalk, "web.cat.talk");
			settext(miCatFile, "menu.cat.file");
			settext(miCatSys, "menu.cat.sys");
			settext(miImgConv, "tray.imgconv");
			settext(miCharArt, "tray.charart");
			settext(miPixel, "tray.pixel");
			settext(miVidConv, "tray.vidconv");
			settext(miVidGif, "tray.vidgif");
			settext(miQrMake, "tray.qrmake");
			settext(miRename, "tray.rename");
			settext(miHash, "tray.hash");
			settext(miTextTool, "tray.texttool");
			settext(miMdRead, "tray.mdread");
			settext(miPwGen, "tray.pwgen");
			settext(miSciCalc, "tray.scicalc");
			settext(miNote, "tray.note");
			settext(miRtspSrv, "tray.rtspsrv");
			settext(miNet, "tray.net");
			settext(miNetGroup, "tray.netgroup");
			settext(miInfo, "tray.info");
			settext(miPortUse, "tray.portuse");
			settext(miMidi, "tray.midisynth");
			settext(miVoiceRec, "tray.voicerec");
			settext(miCam, "tray.cam");
			settext(miSys, "tray.sysinfo");
			settext(miSysTool, "tray.systool");
			settext(miPerfMon, "tray.perfmon");
			settext(miHttpDbg, "tray.httpdbg");
			settext(miZhConv, "tray.zhconv");
			settext(miPinyin, "tray.pinyin");
			settext(miHangul, "tray.hangul");
			settext(miWinCal, "tray.wincal");
			settext(miJpYomi, "tray.jpyomi");
			settext(miWinTop, "tray.wintop");
			settext(miAutoKey, "tray.autokey");
			settext(miFastCopy, "tray.fastcopy");
			settext(miFileProbe, "tray.fileprobe");
			settext(miGlyphMod, "tray.glyphmod");
			settext(miCast, "tray.cast");
			settext(miSettings, "tray.settings");
			settext(miExit, "tray.exit");
			applyhotkeys();
			fillwebtools();
		}
		catch { }
	}

	static void settext(Forms.ToolStripMenuItem mi, string key) {
		if (mi != null) mi.Text = Loc.T(key);
	}

	public event Action ClipboardAsFileRequested;
	public event Action OcrRequested;
	public event Action SnapRequested;
	public event Action ScreenSnapRequested;
	public event Action BoardRequested;
	public event Action VoiceRequested;
	public event Action TranslateRequested;
	public event Action<bool> DictSelBarChanged;
	public event Action PdfRequested;
	public event Action SnapshotsRequested;
	public event Action RecordRequested;
	public event Action RecordOptionsRequested;
	public event Action GifRecordRequested;
	public event Action GifRecordOptionsRequested;
	public event Action WebHomeRequested;
	public event Action WebFilesRequested;
	public event Action WebDictRequested;
	public event Action<string> WebToolRequested;

	void fillwebtools() {
		stripweb(miCatText);
		stripweb(miCatCalc);
		stripweb(miCatSpeech);
		stripweb(miCatTime);
		stripweb(miCatImage);
		stripweb(miInfo);
		stripweb(miCatTalk);
		for (int i = miTools.DropDownItems.Count - 1; i >= 0; i--) {
			var it = miTools.DropDownItems[i];
			if (it.Tag is string tag && tag.StartsWith("webcat:")) {
				miTools.DropDownItems.RemoveAt(i);
				it.Dispose();
			}
		}
		foreach (var cat in WebCatalog()) {
			var host = trayhost(cat.Id);
			if (host == null) {
				if (cat.Tools.Count == 0) continue;
				host = new Forms.ToolStripMenuItem(cat.Title) { Tag = "webcat:" + cat.Id };
				miTools.DropDownItems.Add(host);
			}
			foreach (var tool in cat.Tools) {
				var id = tool.Id;
				host.DropDownItems.Add(new Forms.ToolStripMenuItem(tool.Name + " (web)", null, (_, _) => {
					try { WebToolRequested?.Invoke(id); } catch { }
				}) { Tag = "web" });
			}
		}
	}

	static void stripweb(Forms.ToolStripMenuItem host) {
		if (host == null) return;
		for (int i = host.DropDownItems.Count - 1; i >= 0; i--) {
			var it = host.DropDownItems[i];
			if (Equals(it.Tag, "web")) {
				host.DropDownItems.RemoveAt(i);
				it.Dispose();
			}
		}
	}

	Forms.ToolStripMenuItem trayhost(string id) {
		if (id == "text") return miCatText;
		if (id == "calc") return miCatCalc;
		if (id == "speech") return miCatSpeech;
		if (id == "time") return miCatTime;
		if (id == "image") return miCatImage;
		if (id == "info") return miInfo;
		if (id == "talk") return miCatTalk;
		return null;
	}

	static string catalogjs() {
		try {
			var home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "tools", "00-home.js");
			if (File.Exists(home)) return File.ReadAllText(home);
		}
		catch { }
		if (SendFileWebPages.TryLoad("tools.js", out var bytes) && bytes != null && bytes.Length > 0)
			return Encoding.UTF8.GetString(bytes);
		return "";
	}

	static string webname(string key, string fallback) {
		var name = (fallback ?? "").Trim();
		if (string.IsNullOrEmpty(key) || !key.StartsWith("web.", StringComparison.Ordinal)) return name;
		var tr = Loc.T(key);
		return string.IsNullOrEmpty(tr) || tr == key ? name : tr;
	}

	public static List<(string Id, string Title, List<(string Id, string Name)> Tools)> WebCatalog() {
		var cats = new List<(string Id, string Title)>();
		var tools = new List<(string Id, string Cat, string Name)>();
		var list = new List<(string Id, string Title, List<(string Id, string Name)> Tools)>();
		var js = catalogjs();
		if (js.Length == 0) return list;
		foreach (Match m in Regex.Matches(js, "\"data-cat\":\"([^\"]+)\"\\s*,\\s*\"data-title\":\"([^\"]+)\"\\s*,\\s*\"data-kt\":\"([^\"]+)\"")) {
			if (m.Groups[1].Value == "fav" || m.Groups[1].Value == "all") continue;
			cats.Add((m.Groups[1].Value, webname(m.Groups[3].Value, m.Groups[2].Value)));
		}
		foreach (Match m in Regex.Matches(js, "\"data-(?:tool|page)\":\"([^\"]+)\"\\s*,\\s*\"data-cat\":\"([^\"]+)\"[\\s\\S]*?\"data-k\":\"([^\"]+)\"\\s*,\\s*c:\"([^\"]*)\""))
			tools.Add((m.Groups[1].Value, m.Groups[2].Value, webname(m.Groups[3].Value, m.Groups[4].Value)));
		foreach (var cat in cats) {
			var rows = new List<(string Id, string Name)>();
			foreach (var tool in tools) {
				if (tool.Cat == cat.Id && tool.Name.Length > 0)
					rows.Add((tool.Id, tool.Name));
			}
			if (rows.Count > 0) list.Add((cat.Id, cat.Title, rows));
		}
		return list;
	}
	public event Action ImgConvRequested;
	public event Action CharArtRequested;
	public event Action PixelArtRequested;
	public event Action VidConvRequested;
	public event Action VidGifRequested;
	public event Action QrMakeRequested;
	public event Action RenameRequested;
	public event Action HashRequested;
	public event Action TextToolRequested;
	public event Action MdReadRequested;
	public event Action PwGenRequested;
	public event Action SciCalcRequested;
	public event Action NoteRequested;
	public event Action RtspSrvRequested;
	public event Action NetToolRequested;
	public event Action PortUseRequested;
	public event Action MidiSynthRequested;
	public event Action VoiceRecRequested;
	public event Action CamRequested;
	public event Action SysRequested;
	public event Action SysToolRequested;
	public event Action FileProbeRequested;
	public event Action PerfMonRequested;
	public event Action HttpDbgRequested;
	public event Action ZhConvRequested;
	public event Action PinyinRequested;
	public event Action HangulRequested;

	public void SetDictSelBarChecked(bool on) {
		dictSelUi = true;
		try {
			if (miDictSel != null) miDictSel.Checked = on;
		}
		catch { }
		dictSelUi = false;
	}

	public void SetHangul(bool on) {
		if (miHangul == null) return;
		miHangul.Checked = on;
	}

	public void SetHangulAvailable(bool ok) {
		if (miHangul == null) return;
		miHangul.Enabled = ok;
		if (!ok) miHangul.Checked = false;
	}

	public event Action WinCalRequested;
	public event Action JpYomiRequested;
	public event Action WinTopRequested;
	public event Action AutoKeyRequested;
	public event Action FastCopyRequested;
	public event Action GlyphModRequested;
	public event Action CastRequested;
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
