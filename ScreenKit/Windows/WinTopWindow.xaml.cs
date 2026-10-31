using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → 窗口管理：枚举和取窗、查看并修改窗口属性、发消息、连续鼠标。</summary>
public partial class WinTopWindow : Window {
	const int WH_MOUSE_LL = 14;
	const int WM_MOUSEMOVE = 0x0200;
	const int WM_LBUTTONDOWN = 0x0201;
	const int WM_RBUTTONDOWN = 0x0204;

	readonly List<Action> timerarr = new();
	readonly List<Action> closearr = new();
	readonly ObservableCollection<WinTopRow> view = new();
	readonly List<WinTopRow> all = new();
	readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1) };

	CheckBox[] styleboxes;
	WinTopFrame frame;
	IntPtr selfroot, curhwnd, hoverHwnd, hook;
	HookProc hookproc;
	bool picking, stylelock, mouseRun;
	int mouseX, mouseY;
	int nextframe, mouseNext, mouseLeft, mouseDone, mouseCount;
	string mouseBtn = "left", mouseMode = "click", mouseMethod = "post";
	int mouseCx, mouseCy;

	public WinTopWindow() {
		InitializeComponent();
		lstwin.ItemsSource = view;
		applylang();
		inittimer();
		initlist();
		initpick();
		initactions();
		initstyles();
		initmsg();
		initmouse();
		WindowEsc.Attach(this, onesc);
		Loaded += (_, _) => {
			selfroot = new WindowInteropHelper(this).Handle;
			reload();
		};
		Closed += (_, _) => {
			foreach (var a in closearr) {
				try { a(); } catch { }
			}
			try { timer.Stop(); } catch { }
			try { frame?.Close(); } catch { }
			frame = null;
		};
	}

	void applylang() {
		Title = Loc.T("wintop.title");
		lbhint.Text = Loc.T("wintop.hint");
		chidden.Content = Loc.T("wintop.hidden");
		lbfilter.Text = Loc.T("wintop.filter");
		efilter.ToolTip = Loc.T("wintop.filter.tip");
		ToolBtnUi.Set(bpick, ToolBtnUi.Browse, Loc.T("wintop.pick"));
		ToolBtnUi.Set(brefresh, ToolBtnUi.Swap, Loc.T("wintop.refresh"));
		ToolBtnUi.Set(bflash, ToolBtnUi.Pin, Loc.T("wintop.flash"));
		ToolBtnUi.Set(bcopyhwnd, ToolBtnUi.Copy, Loc.T("wintop.copyhwnd"));
		ToolBtnUi.Set(bcopyall, ToolBtnUi.Copy, Loc.T("wintop.copyprops"));

		tablist.Header = Loc.T("wintop.list");
		tabtree.Header = Loc.T("wintop.tree");
		ToolBtnUi.Set(bloadtree, ToolBtnUi.Folder, Loc.T("wintop.tree.load"));

		lbprops.Text = Loc.T("wintop.prop");
		lbhwnd.Text = Loc.T("wintop.prop.hwnd");
		lbdec.Text = Loc.T("wintop.prop.dec");
		lbtitle.Text = Loc.T("wintop.prop.title");
		lbclass.Text = Loc.T("wintop.prop.class");
		lbproc.Text = Loc.T("wintop.prop.proc");
		lbpath.Text = Loc.T("wintop.prop.path");
		lbrect.Text = Loc.T("wintop.prop.rect");
		lbstate.Text = Loc.T("wintop.prop.state");
		lbstyle.Text = Loc.T("wintop.prop.style");
		lbexstyle.Text = Loc.T("wintop.prop.exstyle");
		lbparent.Text = Loc.T("wintop.prop.parent");
		lbowner.Text = Loc.T("wintop.prop.owner");

		lbact.Text = Loc.T("wintop.act");
		bactivate.Content = Loc.T("wintop.act.activate");
		btopmost.Content = Loc.T("wintop.act.top");
		buntopmost.Content = Loc.T("wintop.act.untop");
		bshow.Content = Loc.T("wintop.act.show");
		bhide.Content = Loc.T("wintop.act.hide");
		benable.Content = Loc.T("wintop.act.enable");
		bdisable.Content = Loc.T("wintop.act.disable");
		bmin.Content = Loc.T("wintop.act.min");
		bmax.Content = Loc.T("wintop.act.max");
		brestore.Content = Loc.T("wintop.act.restore");
		bmovescreen.Content = Loc.T("wintop.act.movescreen");
		bapplypos.Content = Loc.T("wintop.act.applypos");
		bsettitle.Content = Loc.T("wintop.act.applytitle");
		bclose.Content = Loc.T("wintop.act.close");
		bbmclick.Content = "BM_CLICK";

		lbstylebits.Text = Loc.T("wintop.style");

		lbmsg.Text = Loc.T("wintop.msg");
		lbmsgkey.Text = "Msg";
		lbwp.Text = "wParam";
		lblp.Text = "lParam";
		esendmode.Items.Clear();
		esendmode.Items.Add("PostMessage");
		esendmode.Items.Add("SendMessage");
		esendmode.SelectedIndex = 0;
		bsendmsg.Content = Loc.T("wintop.msg.send");
		lbtip.Text = Loc.T("wintop.msg.tip");

		lbmouse.Text = Loc.T("wintop.mouse");
		lbmethod.Text = Loc.T("wintop.mouse.method");
		emethod.Items.Clear();
		emethod.Items.Add("PostMessage");
		emethod.Items.Add("SendInput");
		emethod.SelectedIndex = 0;
		lbbutton.Text = Loc.T("wintop.mouse.button");
		ebutton.Items.Clear();
		ebutton.Items.Add(Loc.T("wintop.mouse.left"));
		ebutton.Items.Add(Loc.T("wintop.mouse.right"));
		ebutton.Items.Add(Loc.T("wintop.mouse.middle"));
		ebutton.SelectedIndex = 0;
		lbaction.Text = Loc.T("wintop.mouse.mode");
		emode.Items.Clear();
		emode.Items.Add(Loc.T("wintop.mouse.click"));
		emode.Items.Add(Loc.T("wintop.mouse.down"));
		emode.Items.Add(Loc.T("wintop.mouse.up"));
		emode.Items.Add(Loc.T("wintop.mouse.dblclick"));
		emode.SelectedIndex = 0;
		lbcx.Text = Loc.T("wintop.mouse.cx");
		bcenter.Content = Loc.T("wintop.mouse.center");
		lbinterval.Text = Loc.T("wintop.mouse.interval");
		lbcount.Text = Loc.T("wintop.mouse.count");
		ecount.ToolTip = Loc.T("wintop.mouse.count.tip");
		bsendonce.Content = Loc.T("wintop.mouse.once");
		bstartmouse.Content = Loc.T("wintop.mouse.start");
		bstopmouse.Content = Loc.T("wintop.mouse.stop");
	}

	void inittimer() {
		timer.Tick += (_, _) => {
			foreach (var a in timerarr) {
				try { a(); } catch { }
			}
		};
		timer.Start();
		timerarr.Add(tickframe);
		timerarr.Add(tickmouse);
		closearr.Add(() => {
			mouseRun = false;
			endpick();
		});
	}

	void initlist() {
		brefresh.Click += (_, _) => reload();
		chidden.Checked += (_, _) => reload();
		chidden.Unchecked += (_, _) => reload();
		efilter.TextChanged += (_, _) => showlist(keep());
		lstwin.SelectionChanged += (_, _) => {
			if (lstwin.SelectedItem is WinTopRow row) select(row.Hwnd);
		};
		lstwin.MouseDoubleClick += (_, _) => {
			if (curhwnd != IntPtr.Zero) flash(curhwnd);
		};
		bloadtree.Click += (_, _) => loadtree();
		treewin.SelectedItemChanged += (_, e) => {
			if (e.NewValue is TreeViewItem { Tag: IntPtr h } && h != IntPtr.Zero) select(h);
		};
	}

	void initpick() {
		bpick.Click += (_, _) => beginpick();
		bflash.Click += (_, _) => {
			if (curhwnd != IntPtr.Zero) flash(curhwnd);
		};
		bcopyhwnd.Click += (_, _) => {
			if (curhwnd == IntPtr.Zero) return;
			var hex = hexof(curhwnd);
			try { Clipboard.SetText(hex); } catch { return; }
			lbstat.Text = Loc.T("wintop.copied", hex);
		};
		bcopyall.Click += (_, _) => {
			if (!needhwnd()) return;
			try { Clipboard.SetText(describeall(WinTop.Describe(curhwnd))); } catch { return; }
			lbstat.Text = Loc.T("wintop.copiedprops");
		};
		return;
		static string describeall(WinTopRow info) {
			var sb = new StringBuilder();
			sb.AppendLine($"HWND={info.HwndHex} ({info.HwndDec})");
			sb.AppendLine($"Title={info.Title}");
			sb.AppendLine($"Class={info.Cls}");
			sb.AppendLine($"PID={info.Pid} TID={info.Tid} Process={info.Proc}");
			sb.AppendLine($"Path={info.Path}");
			sb.AppendLine($"Rect={info.RectText} Client={info.ClientW}x{info.ClientH}");
			sb.AppendLine($"Style={info.StyleHex} ExStyle={info.ExStyleHex}");
			sb.AppendLine(info.StateText);
			sb.AppendLine($"Parent={info.ParentHex} Owner={info.OwnerHex}");
			return sb.ToString();
		}
	}

	void initactions() {
		bactivate.Click += (_, _) => doact(() => WinTop.Activate(curhwnd), "wintop.act.activate");
		btopmost.Click += (_, _) => doact(() => WinTop.SetTop(curhwnd, true, out _), "wintop.act.top");
		buntopmost.Click += (_, _) => doact(() => WinTop.SetTop(curhwnd, false, out _), "wintop.act.untop");
		bshow.Click += (_, _) => doact(() => WinTop.SetVisible(curhwnd, true), "wintop.act.show");
		bhide.Click += (_, _) => doact(() => WinTop.SetVisible(curhwnd, false), "wintop.act.hide");
		benable.Click += (_, _) => doact(() => WinTop.SetEnabled(curhwnd, true), "wintop.act.enable");
		bdisable.Click += (_, _) => doact(() => WinTop.SetEnabled(curhwnd, false), "wintop.act.disable");
		bmin.Click += (_, _) => doact(() => WinTop.Minimize(curhwnd), "wintop.act.min");
		bmax.Click += (_, _) => doact(() => WinTop.Maximize(curhwnd), "wintop.act.max");
		brestore.Click += (_, _) => doact(() => WinTop.Restore(curhwnd), "wintop.act.restore");
		bmovescreen.Click += (_, _) => doact(() => WinTop.MoveToScreen(curhwnd), "wintop.act.movescreen");
		bbmclick.Click += (_, _) => doact(() => WinTop.BmClick(curhwnd), "wintop.act.bmclick");
		bapplypos.Click += (_, _) => {
			if (!needhwnd()) return;
			if (!int.TryParse(erx.Text, out var x) || !int.TryParse(ery.Text, out var y)
			    || !int.TryParse(erw.Text, out var w) || !int.TryParse(erh.Text, out var h)) {
				lbstat.Text = Loc.T("wintop.rect.bad");
				return;
			}
			doact(() => WinTop.MoveResize(curhwnd, x, y, w, h), "wintop.act.applypos");
		};
		bsettitle.Click += (_, _) => doact(() => WinTop.SetTitle(curhwnd, ehtitle.Text), "wintop.act.applytitle");
		bclose.Click += (_, _) => {
			if (!needhwnd()) return;
			var ask = Loc.T("wintop.close.ask", hexof(curhwnd));
			if (MessageBox.Show(this, ask, Loc.T("wintop.confirm"), MessageBoxButton.YesNo,
				    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
			doact(() => WinTop.CloseWin(curhwnd), "wintop.act.close");
		};
	}

	void initstyles() {
		styleboxes = new[] {
			cws_caption, cws_border, cws_thickframe, cws_sysmenu, cws_minimizebox, cws_maximizebox,
			cws_visible, cws_disabled, cex_topmost, cex_toolwindow, cex_appwindow, cex_layered,
			cex_transparent, cex_clientedge,
		};
		foreach (var cb in styleboxes) {
			cb.Checked += onstyle;
			cb.Unchecked += onstyle;
		}
		return;
		void onstyle(object sender, RoutedEventArgs e) {
			if (stylelock || !WinTop.IsAlive(curhwnd)) return;
			if (sender is not CheckBox { Tag: string tag } cb) return;
			var (isEx, mask) = maskof(tag);
			if (mask == 0) return;
			var on = cb.IsChecked == true;
			WinTop.SetStyleBits(curhwnd, isEx, mask, on);
			// 这三个位改样式不够，要同时走系统接口
			if (tag == "WS_VISIBLE") WinTop.SetVisible(curhwnd, on);
			if (tag == "WS_DISABLED") WinTop.SetEnabled(curhwnd, !on);
			if (tag == "WS_EX_TOPMOST") WinTop.SetTop(curhwnd, on, out _);
			refreshcur();
			lbstat.Text = Loc.T("wintop.style.set", tag, on ? "ON" : "OFF");
		}
	}

	void initmsg() {
		bsendmsg.Click += (_, _) => {
			if (!needhwnd()) return;
			if (!tryparseint(emsg.Text, out var msg) || !tryparseint(ewp.Text, out var wp)
			    || !tryparseint(elp.Text, out var lp)) {
				lbstat.Text = Loc.T("wintop.msg.bad");
				return;
			}
			var post = esendmode.SelectedIndex <= 0;
			bool ok;
			if (post) ok = WinTop.Post(curhwnd, (uint)msg, (IntPtr)wp, (IntPtr)lp);
			else {
				WinTop.Send(curhwnd, (uint)msg, (IntPtr)wp, (IntPtr)lp);
				ok = true;
			}
			lbstat.Text = ok
				? Loc.T("wintop.msg.ok", post ? "PostMessage" : "SendMessage", msg.ToString("X"))
				: Loc.T("wintop.msg.fail");
			refreshcur();
		};
	}

	void initmouse() {
		bcenter.Click += (_, _) => {
			if (!needhwnd()) return;
			if (!WinTop.ClientCenter(curhwnd, out var x, out var y)) return;
			emx.Text = x.ToString();
			emy.Text = y.ToString();
		};
		bsendonce.Click += (_, _) => {
			if (!readmouseparams(out _, out _)) return;
			firemouseonce();
			lbstat.Text = Loc.T("wintop.mouse.sent1");
		};
		bstartmouse.Click += (_, _) => {
			if (!readmouseparams(out var interval, out var count)) return;
			if (interval < 10) interval = 10;
			mouseRun = true;
			mouseNext = Environment.TickCount;
			mouseLeft = count <= 0 ? -1 : count;
			mouseDone = 0;
			bstartmouse.IsEnabled = false;
			bstopmouse.IsEnabled = true;
			lbstat.Text = Loc.T("wintop.mouse.started");
			lbmouseprog.Text = Loc.T("wintop.mouse.running");
		};
		bstopmouse.Click += (_, _) => stopmouse(Loc.T("wintop.mouse.stopped"));
	}

	void tickframe() {
		if (Environment.TickCount - nextframe < 0) return;
		nextframe = Environment.TickCount + 30;
		frame?.Tick();
		var h = picking ? hover() : curhwnd;
		if (!WinTop.IsAlive(h)) {
			frame?.Place(IntPtr.Zero);
			return;
		}
		if (frame == null) frame = new WinTopFrame();
		frame.Place(h);
		if (!picking || h == hoverHwnd) return;
		hoverHwnd = h;
		try {
			var info = WinTop.Describe(h);
			lbstat.Text = Loc.T("wintop.sel", info.HwndHex, info.DisplayTitle);
		}
		catch { }
	}

	void tickmouse() {
		if (!mouseRun) return;
		if (Environment.TickCount - mouseNext < 0) return;
		mouseMethod = methodkey();
		if (mouseMethod == "post" && !WinTop.IsAlive(curhwnd)) {
			stopmouse(Loc.T("wintop.mouse.gone"));
			return;
		}
		int.TryParse(emx.Text, out mouseCx);
		int.TryParse(emy.Text, out mouseCy);
		mouseBtn = buttonkey();
		mouseMode = modekey();
		firemouseonce();
		mouseDone++;
		if (!int.TryParse(einterval.Text, out var interval) || interval < 10) interval = 10;
		mouseNext = Environment.TickCount + interval;
		if (mouseLeft > 0) {
			mouseLeft--;
			if (mouseLeft == 0) {
				stopmouse(Loc.T("wintop.mouse.done", mouseDone));
				return;
			}
		}
		lbmouseprog.Text = mouseLeft < 0
			? Loc.T("wintop.mouse.prog", mouseDone)
			: Loc.T("wintop.mouse.progleft", mouseDone, mouseLeft);
	}

	void firemouseonce() {
		if (mouseMethod == "sendinput") {
			var sx = mouseCx;
			var sy = mouseCy;
			if (!WinTop.IsAlive(curhwnd) || !WinTop.ClientToScreen(curhwnd, mouseCx, mouseCy, out sx, out sy)) {
				sx = mouseCx;
				sy = mouseCy;
			}
			WinTop.SendInputClick(sx, sy, mouseBtn, mouseMode);
			return;
		}
		if (!WinTop.IsAlive(curhwnd)) return;
		WinTop.PostClick(curhwnd, mouseCx, mouseCy, mouseBtn, mouseMode);
	}

	void stopmouse(string msg) {
		mouseRun = false;
		bstartmouse.IsEnabled = true;
		bstopmouse.IsEnabled = false;
		lbmouseprog.Text = msg;
		lbstat.Text = msg;
	}

	bool readmouseparams(out int interval, out int count) {
		interval = 100;
		count = 0;
		if (methodkey() == "post" && !needhwnd()) return false;
		if (!int.TryParse(einterval.Text, out interval) || interval < 0) {
			lbstat.Text = Loc.T("wintop.mouse.interval.bad");
			return false;
		}
		if (!int.TryParse(ecount.Text, out count) || count < 0) {
			lbstat.Text = Loc.T("wintop.mouse.count.bad");
			return false;
		}
		if (!int.TryParse(emx.Text, out mouseCx) || !int.TryParse(emy.Text, out mouseCy)) {
			lbstat.Text = Loc.T("wintop.mouse.pos.bad");
			return false;
		}
		mouseCount = count;
		mouseBtn = buttonkey();
		mouseMode = modekey();
		mouseMethod = methodkey();
		return true;
	}

	void reload() {
		var keepp = keep();
		try {
			all.Clear();
			foreach (var row in WinTop.List(chidden.IsChecked == true)) {
				if (row.Hwnd == selfroot) continue;
				if (frame != null && row.Hwnd == frame.Handle) continue;
				all.Add(row);
			}
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("wintop.fail", ex.Message);
			return;
		}
		showlist(keepp, Loc.T("wintop.loaded", all.Count));
	}

	void showlist(IntPtr keepp, string stat = null) {
		var q = (efilter.Text ?? "").Trim();
		lstwin.SelectedItem = null;
		view.Clear();
		foreach (var row in all) {
			if (q.Length == 0 || hit(row, q)) view.Add(row);
		}
		if (keepp != IntPtr.Zero) {
			foreach (var row in view) {
				if (row.Hwnd != keepp) continue;
				lstwin.SelectedItem = row;
				lstwin.ScrollIntoView(row);
				break;
			}
		}
		if (!string.IsNullOrEmpty(stat)) lbstat.Text = stat;
		else if (lstwin.SelectedItem == null) lbstat.Text = Loc.T("wintop.count", view.Count);
	}

	static bool hit(WinTopRow row, string q) {
		if (row.Title != null && row.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (row.Proc != null && row.Proc.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (row.Cls != null && row.Cls.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (row.HwndHex.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		if (row.HwndDec.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		return row.Pid.ToString() == q;
	}

	IntPtr keep() => curhwnd;

	void select(IntPtr h) {
		if (!WinTop.IsAlive(h)) {
			curhwnd = IntPtr.Zero;
			clearprops();
			frame?.Place(IntPtr.Zero);
			return;
		}
		curhwnd = h;
		var info = WinTop.Describe(h);
		fillprops(info);
		if (frame == null) frame = new WinTopFrame();
		frame.Place(h);
		if ((lstwin.SelectedItem as WinTopRow)?.Hwnd != h) {
			WinTopRow found = null;
			foreach (var row in view) {
				if (row.Hwnd != h) continue;
				found = row;
				break;
			}
			if (found != null) lstwin.SelectedItem = found;
		}
		lbstat.Text = Loc.T("wintop.sel", info.HwndHex, info.DisplayTitle);
	}

	void refreshcur() {
		if (!WinTop.IsAlive(curhwnd)) return;
		fillprops(WinTop.Describe(curhwnd));
		reload();
	}

	void fillprops(WinTopRow info) {
		ehhwnd.Text = info.HwndHex;
		ehdec.Text = info.HwndDec;
		ehtitle.Text = info.Title;
		ehclass.Text = info.Cls;
		ehproc.Text = $"{info.Proc}  PID={info.Pid}  TID={info.Tid}";
		ehpath.Text = info.Path;
		erx.Text = info.X.ToString();
		ery.Text = info.Y.ToString();
		erw.Text = info.W.ToString();
		erh.Text = info.H.ToString();
		lbclient.Text = info.ClientText;
		lbstatev.Text = info.StateText;
		ehstyle.Text = info.StyleHex;
		ehexstyle.Text = info.ExStyleHex;
		ehparent.Text = info.ParentHex;
		ehowner.Text = info.OwnerHex;
		stylelock = true;
		try {
			foreach (var cb in styleboxes) {
				var (isEx, mask) = maskof(cb.Tag as string);
				if (mask == 0) continue;
				cb.IsChecked = ((isEx ? info.ExStyle : info.Style) & mask) == mask;
			}
		}
		finally { stylelock = false; }
	}

	void clearprops() {
		ehhwnd.Text = ehdec.Text = ehtitle.Text = ehclass.Text = ehproc.Text = ehpath.Text = "";
		erx.Text = ery.Text = erw.Text = erh.Text = "";
		lbclient.Text = lbstatev.Text = "";
		ehstyle.Text = ehexstyle.Text = ehparent.Text = ehowner.Text = "";
		stylelock = true;
		try {
			foreach (var cb in styleboxes) cb.IsChecked = false;
		}
		finally { stylelock = false; }
	}

	void loadtree() {
		if (!needhwnd()) return;
		var root = WinTop.RootOf(curhwnd);
		if (root == IntPtr.Zero) root = curhwnd;
		treewin.Items.Clear();
		var item = builditem(WinTop.BuildTree(root));
		item.IsExpanded = true;
		treewin.Items.Add(item);
		selecttree(item, curhwnd);
		lbtree.Text = Loc.T("wintop.tree.root", hexof(root));
		lbstat.Text = Loc.T("wintop.tree.loaded");
	}

	TreeViewItem builditem(WinTopNode node) {
		var item = new TreeViewItem {
			Header = node.Header,
			Tag = node.Info?.Hwnd ?? IntPtr.Zero,
		};
		foreach (var c in node.Children) item.Items.Add(builditem(c));
		return item;
	}

	static bool selecttree(TreeViewItem item, IntPtr h) {
		if (item.Tag is IntPtr th && th == h) {
			item.IsSelected = true;
			item.BringIntoView();
			return true;
		}
		foreach (TreeViewItem c in item.Items) {
			if (!selecttree(c, h)) continue;
			item.IsExpanded = true;
			return true;
		}
		return false;
	}

	void flash(IntPtr h) {
		if (!WinTop.IsAlive(h)) return;
		if (frame == null) frame = new WinTopFrame();
		frame.Flash(h);
		lbstat.Text = Loc.T("wintop.flashed", hexof(h));
	}

	void beginpick() {
		if (picking) return;
		endpick();
		picking = true;
		hoverHwnd = IntPtr.Zero;
		hookproc = onhook;
		hook = SetWindowsHookEx(WH_MOUSE_LL, hookproc, GetModuleHandle(null), 0);
		if (hook == IntPtr.Zero) {
			picking = false;
			hookproc = null;
			lbstat.Text = Loc.T("wintop.pick.fail");
			return;
		}
		Cursor = Cursors.Cross;
		lbstat.Text = Loc.T("wintop.pick.hint");
		if (GetCursorPos(out var pt)) {
			mouseX = pt.X;
			mouseY = pt.Y;
		}
		nextframe = 0;
	}

	void endpick() {
		picking = false;
		Cursor = null;
		var h = hook;
		hook = IntPtr.Zero;
		hookproc = null;
		if (h == IntPtr.Zero) return;
		try { UnhookWindowsHookEx(h); } catch { }
	}

	IntPtr hover() {
		var h = WinTop.RootAt(mouseX, mouseY);
		if (h == IntPtr.Zero || h == selfroot) return IntPtr.Zero;
		if (frame != null && h == frame.Handle) return frame.Target;
		return h;
	}

	IntPtr onhook(int nCode, IntPtr wParam, IntPtr lParam) {
		if (nCode >= 0 && picking) {
			var msg = wParam.ToInt32();
			if (msg == WM_MOUSEMOVE || msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN) {
				mouseX = Marshal.ReadInt32(lParam);
				mouseY = Marshal.ReadInt32(lParam, 4);
			}
			if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN) {
				var x = mouseX;
				var y = mouseY;
				var choose = msg == WM_LBUTTONDOWN;
				Dispatcher.BeginInvoke(new Action(() => finishpick(x, y, choose)));
				return (IntPtr)1;
			}
		}
		return CallNextHookEx(hook, nCode, wParam, lParam);
	}

	void finishpick(int x, int y, bool choose) {
		endpick();
		if (!choose) {
			lbstat.Text = Loc.T("wintop.pick.cancel");
			return;
		}
		var h = WinTop.RootAt(x, y);
		if (frame != null && h == frame.Handle) h = frame.Target;
		if (!WinTop.IsAlive(h)) {
			lbstat.Text = Loc.T("wintop.pick.miss");
			return;
		}
		efilter.Text = "";
		select(h);
		try { Activate(); } catch { }
	}

	void onesc() {
		if (picking) {
			endpick();
			lbstat.Text = Loc.T("wintop.pick.cancel");
			return;
		}
		Close();
	}

	void doact(Func<bool> act, string namekey) {
		if (!needhwnd()) return;
		var name = Loc.T(namekey);
		bool ok;
		try { ok = act(); }
		catch (Exception ex) {
			lbstat.Text = Loc.T("wintop.act.err", name, ex.Message);
			return;
		}
		refreshcur();
		lbstat.Text = ok ? Loc.T("wintop.act.ok", name) : Loc.T("wintop.act.bad", name);
	}

	bool needhwnd() {
		if (WinTop.IsAlive(curhwnd)) return true;
		lbstat.Text = Loc.T("wintop.needhwnd");
		return false;
	}

	string buttonkey() => ebutton.SelectedIndex switch {
		1 => "right",
		2 => "middle",
		_ => "left",
	};

	string modekey() => emode.SelectedIndex switch {
		1 => "down",
		2 => "up",
		3 => "dblclick",
		_ => "click",
	};

	string methodkey() => emethod.SelectedIndex == 1 ? "sendinput" : "post";

	static (bool isEx, uint mask) maskof(string tag) => tag switch {
		"WS_CAPTION" => (false, WinTop.WS_CAPTION),
		"WS_BORDER" => (false, WinTop.WS_BORDER),
		"WS_THICKFRAME" => (false, WinTop.WS_THICKFRAME),
		"WS_SYSMENU" => (false, WinTop.WS_SYSMENU),
		"WS_MINIMIZEBOX" => (false, WinTop.WS_MINIMIZEBOX),
		"WS_MAXIMIZEBOX" => (false, WinTop.WS_MAXIMIZEBOX),
		"WS_VISIBLE" => (false, WinTop.WS_VISIBLE),
		"WS_DISABLED" => (false, WinTop.WS_DISABLED),
		"WS_EX_TOPMOST" => (true, WinTop.WS_EX_TOPMOST),
		"WS_EX_TOOLWINDOW" => (true, WinTop.WS_EX_TOOLWINDOW),
		"WS_EX_APPWINDOW" => (true, WinTop.WS_EX_APPWINDOW),
		"WS_EX_LAYERED" => (true, WinTop.WS_EX_LAYERED),
		"WS_EX_TRANSPARENT" => (true, WinTop.WS_EX_TRANSPARENT),
		"WS_EX_CLIENTEDGE" => (true, WinTop.WS_EX_CLIENTEDGE),
		_ => (false, 0u),
	};

	static bool tryparseint(string s, out int v) {
		v = 0;
		if (string.IsNullOrWhiteSpace(s)) return false;
		s = s.Trim();
		try {
			if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
				v = Convert.ToInt32(s, 16);
				return true;
			}
			return int.TryParse(s, out v);
		}
		catch { return false; }
	}

	static string hexof(IntPtr h) => "0x" + h.ToInt64().ToString("X8");

	delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint {
		public int X;
		public int Y;
	}

	[DllImport("user32.dll")]
	static extern bool GetCursorPos(out NativePoint pt);

	[DllImport("user32.dll", SetLastError = true)]
	static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hmod, uint tid);

	[DllImport("user32.dll")]
	static extern bool UnhookWindowsHookEx(IntPtr hook);

	[DllImport("user32.dll")]
	static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wp, IntPtr lp);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string name);
}
