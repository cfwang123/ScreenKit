using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfLine = System.Windows.Shapes.Line;
using WpfPath = System.Windows.Shapes.Path;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBox = System.Windows.Controls.TextBox;
using Shape = System.Windows.Shapes.Shape;

namespace WpfOCR;

/// <summary>多屏截图会话结果。</summary>
public sealed class CaptureResult {
	public bool Confirmed;
	/// <summary>确认后是否立即 OCR（标注工具条「OCR 识图」）。</summary>
	public bool WantOcr;
	public BitmapSource Image;
	public Rect SelectedDip;
}

/// <summary>
/// 微信式截图：每块显示器一个遮罩窗，全部同时显示。
/// 支持跨屏框选：拖拽用虚拟屏坐标，松手时从各屏冻结图拼接。
/// </summary>
public partial class CaptureOverlay : Window {
	enum Phase { Select, Annotate }
	/// <summary>None = 未选绘制工具（取消工具选中时）；移动/缩放只靠边缘与手柄。</summary>
	enum Tool { None, Rect, Ellipse, Line, Arrow, Text }
	/// <summary>标注阶段调整选区：移动 / 八向缩放。</summary>
	enum AdjHit { None, Move, N, S, E, W, NE, NW, SE, SW }

	[StructLayout(LayoutKind.Sequential)]
	struct NativePoint {
		public int X, Y;
	}

	const uint SWP_SHOWWINDOW = 0x0040;
	const uint SRCCOPY = 0x00CC0020;
	const uint CAPTUREBLT = 0x40000000; // 含分层窗；副屏 CopyFromScreen 常需此标志才不黑
	static readonly IntPtr HwndTopmost = new(-1);

	[DllImport("user32.dll")]
	static extern bool GetCursorPos(out NativePoint lpPoint);

	[DllImport("user32.dll")]
	static extern uint GetDoubleClickTime();

	[DllImport("user32.dll")]
	static extern int GetSystemMetrics(int nIndex);

	const int SM_CXDOUBLECLK = 36;
	const int SM_CYDOUBLECLK = 37;

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
		int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll")]
	static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

	[DllImport("user32.dll")]
	static extern bool IsIconic(IntPtr hWnd);

	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll")]
	static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

	[DllImport("dwmapi.dll")]
	static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

	delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	const uint GW_CHILD = 5;
	const uint GW_HWNDNEXT = 2;

	[StructLayout(LayoutKind.Sequential)]
	struct NativeRect {
		public int Left, Top, Right, Bottom;
	}

	const int GWL_EXSTYLE = -20;
	const int GWL_STYLE = -16;
	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const int WS_CHILD = 0x40000000;
	const int DWMWA_CLOAKED = 14;

	/// <summary>顶层可见窗口矩形（虚拟屏坐标，与 Screen.Bounds 一致）。</summary>
	readonly struct WinHit {
		public readonly IntPtr Hwnd;
		public readonly System.Drawing.Rectangle Rect;
		public WinHit(IntPtr hwnd, System.Drawing.Rectangle rect) {
			Hwnd = hwnd;
			Rect = rect;
		}
	}

	// 放大镜：源采样边长 × 每像素放大倍数 = 视图边长（2× 于原 112）
	const int MAG_SRC = 14;
	const int MAG_SCALE = 16;
	const int MAG_VIEW = MAG_SRC * MAG_SCALE; // 224
	/// <summary>选区最小边长（底图像素）。</summary>
	const int MIN_CROP = 8;
	/// <summary>缩放手柄边长（DIP）。</summary>
	const double HANDLE_SZ = 8;
	/// <summary>选区边缘可拖动移动的热区（DIP）。</summary>
	const double EDGE_MOVE = 10;

	// 抓屏 API（对齐 ShareX Screenshot.CaptureRectangleNative）
	[DllImport("user32.dll")]
	static extern IntPtr GetDesktopWindow();

	[DllImport("user32.dll")]
	static extern IntPtr GetWindowDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	static extern IntPtr GetDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

	[DllImport("gdi32.dll")]
	static extern IntPtr CreateCompatibleDC(IntPtr hdc);

	[DllImport("gdi32.dll")]
	static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

	[DllImport("gdi32.dll")]
	static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

	[DllImport("gdi32.dll")]
	static extern bool DeleteObject(IntPtr hObject);

	[DllImport("gdi32.dll")]
	static extern bool DeleteDC(IntPtr hdc);

	[DllImport("gdi32.dll")]
	static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
		IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

	[DllImport("gdi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

	[DllImport("gdi32.dll")]
	static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

	[DllImport("user32.dll")]
	static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

	// -1 = UNAWARE, -2 = SYSTEM_AWARE, -3 = PER_MONITOR, -4 = PER_MONITOR_V2
	static readonly IntPtr DpiUnaware = new(-1);
	static readonly IntPtr DpiSystemAware = new(-2);
	static readonly IntPtr DpiPerMonitorV2 = new(-4);

	const int DESKTOPHORZRES = 118;
	const int DESKTOPVERTRES = 117;

	/// <summary>多屏会话：各屏窗口共享；跨屏框选用虚拟屏坐标。</summary>
	sealed class Session {
		public readonly bool Annotate;
		/// <summary>屏幕画板：跳过框选，全屏静止画面上直接标注。</summary>
		public readonly bool Board;
		/// <summary>画板主屏（可绘制）；其余屏仅冻结底图。</summary>
		public CaptureOverlay BoardOwner;
		public readonly List<CaptureOverlay> Windows = new();
		/// <summary>枚举得到的顶层窗口（Z 序：先 = 更靠前）。</summary>
		public List<WinHit> TopWindows = new();
		/// <summary>当前拖拽发起屏（持有鼠标捕获）；不再禁止跨屏。</summary>
		public CaptureOverlay DragOwner;
		/// <summary>框选：虚拟屏像素坐标（与 Screen.Bounds 一致）。</summary>
		public int DragVX0, DragVY0, DragVX1, DragVY1;
		public bool RegionDrag;
		public bool Finishing;
		public bool Confirmed;
		/// <summary>完成时是否请求 OCR。</summary>
		public bool WantOcr;
		public BitmapSource Image;
		public Rect SelectedDip;
		public event Action Finished;

		/// <summary>标注阶段：当前可绘制的宿主屏。</summary>
		public CaptureOverlay AnnotateHost;
		/// <summary>标注选区虚拟屏像素矩形（可跨屏移动/缩放）。</summary>
		public int AnnVL, AnnVT, AnnVW, AnnVH;
		/// <summary>已进入标注阶段（多屏时其它屏保持冻结作 guest）。</summary>
		public bool InAnnotate;

		public Session(bool annotate, bool board = false) {
			Board = board;
			// 画板也需要标注工具条 / 合成导出
			Annotate = annotate || board;
		}

		/// <summary>各屏 Bounds 并集（虚拟坐标）。</summary>
		public void VirtualBounds(out int vl, out int vt, out int vw, out int vh) {
			vl = int.MaxValue;
			vt = int.MaxValue;
			var vr = int.MinValue;
			var vb = int.MinValue;
			foreach (var w in Windows) {
				vl = Math.Min(vl, w.monL);
				vt = Math.Min(vt, w.monT);
				vr = Math.Max(vr, w.monL + w.monBoundW);
				vb = Math.Max(vb, w.monT + w.monBoundH);
			}
			if (vl == int.MaxValue) {
				vl = 0;
				vt = 0;
				vw = 1;
				vh = 1;
				return;
			}
			vw = Math.Max(1, vr - vl);
			vh = Math.Max(1, vb - vt);
		}

		/// <summary>选区中心所在屏；无交集时取面积最大的屏。</summary>
		public CaptureOverlay BestHostForAnn() {
			if (Windows.Count == 0) return null;
			var cx = AnnVL + AnnVW / 2;
			var cy = AnnVT + AnnVH / 2;
			foreach (var w in Windows) {
				if (cx >= w.monL && cx < w.monL + w.monBoundW
					&& cy >= w.monT && cy < w.monT + w.monBoundH)
					return w;
			}
			CaptureOverlay best = Windows[0];
			var bestA = 0;
			var ann = new System.Drawing.Rectangle(AnnVL, AnnVT, Math.Max(1, AnnVW), Math.Max(1, AnnVH));
			foreach (var w in Windows) {
				var mon = new System.Drawing.Rectangle(w.monL, w.monT, w.monBoundW, w.monBoundH);
				var inter = System.Drawing.Rectangle.Intersect(ann, mon);
				var a = Math.Max(0, inter.Width) * Math.Max(0, inter.Height);
				if (a > bestA) {
					bestA = a;
					best = w;
				}
			}
			return best;
		}

		/// <summary>刷新所有屏的标注选区 UI（宿主画布 + guest 遮罩）。</summary>
		public void RefreshAnnotateUi(bool clearStrokes) {
			foreach (var w in Windows)
				w.refreshannotateui(clearStrokes);
		}

		public bool BeginDrag(CaptureOverlay who, int vx, int vy) {
			if (Finishing) return false;
			if (DragOwner != null && DragOwner != who) return false;
			DragOwner = who;
			DragVX0 = DragVX1 = vx;
			DragVY0 = DragVY1 = vy;
			RegionDrag = false;
			return true;
		}

		public void UpdateDrag(int vx, int vy) {
			DragVX1 = vx;
			DragVY1 = vy;
			foreach (var w in Windows)
				w.applyvirtualsel(DragVX0, DragVY0, DragVX1, DragVY1);
		}

		/// <summary>从各屏冻结图按虚拟矩形拼接（跨屏）。</summary>
		public BitmapSource CropVirtual(int left, int top, int pw, int ph) {
			pw = Math.Max(1, pw);
			ph = Math.Max(1, ph);
			var sel = new System.Drawing.Rectangle(left, top, pw, ph);

			// 快路径：选区完全落在单屏 → CroppedBitmap，拖动时几乎零成本（避免 RTB 导致卡顿/抖）
			CaptureOverlay sole = null;
			System.Drawing.Rectangle soleInter = default;
			var hitN = 0;
			foreach (var ov in Windows) {
				if (ov.desktopBmp == null) continue;
				var mon = new System.Drawing.Rectangle(ov.monL, ov.monT, ov.monBoundW, ov.monBoundH);
				var inter = System.Drawing.Rectangle.Intersect(sel, mon);
				if (inter.Width < 1 || inter.Height < 1) continue;
				hitN++;
				if (hitN == 1) {
					sole = ov;
					soleInter = inter;
				}
				else {
					sole = null;
					break;
				}
			}
			if (sole != null
				&& soleInter.Left == left && soleInter.Top == top
				&& soleInter.Width == pw && soleInter.Height == ph) {
				var dl = (int)Math.Floor((soleInter.Left - sole.monL) * (double)sole.deskW / sole.monBoundW);
				var dt = (int)Math.Floor((soleInter.Top - sole.monT) * (double)sole.deskH / sole.monBoundH);
				var dr = (int)Math.Ceiling((soleInter.Right - sole.monL) * (double)sole.deskW / sole.monBoundW);
				var db = (int)Math.Ceiling((soleInter.Bottom - sole.monT) * (double)sole.deskH / sole.monBoundH);
				dl = Compat.Clamp(dl, 0, sole.deskW - 1);
				dt = Compat.Clamp(dt, 0, sole.deskH - 1);
				dr = Compat.Clamp(dr, dl + 1, sole.deskW);
				db = Compat.Clamp(db, dt + 1, sole.deskH);
				return sole.croplocal(dl, dt, dr - dl, db - dt);
			}

			var dv = new System.Windows.Media.DrawingVisual();
			using (var dc = dv.RenderOpen()) {
				dc.DrawRectangle(System.Windows.Media.Brushes.Black, null,
					new System.Windows.Rect(0, 0, pw, ph));
				foreach (var ov in Windows) {
					if (ov.desktopBmp == null) continue;
					var mon = new System.Drawing.Rectangle(ov.monL, ov.monT, ov.monBoundW, ov.monBoundH);
					var inter = System.Drawing.Rectangle.Intersect(sel, mon);
					if (inter.Width < 1 || inter.Height < 1) continue;
					// Bounds 相交区 → 底图像素
					var dl = (int)Math.Floor((inter.Left - ov.monL) * (double)ov.deskW / ov.monBoundW);
					var dt = (int)Math.Floor((inter.Top - ov.monT) * (double)ov.deskH / ov.monBoundH);
					var dr = (int)Math.Ceiling((inter.Right - ov.monL) * (double)ov.deskW / ov.monBoundW);
					var db = (int)Math.Ceiling((inter.Bottom - ov.monT) * (double)ov.deskH / ov.monBoundH);
					dl = Compat.Clamp(dl, 0, ov.deskW - 1);
					dt = Compat.Clamp(dt, 0, ov.deskH - 1);
					dr = Compat.Clamp(dr, dl + 1, ov.deskW);
					db = Compat.Clamp(db, dt + 1, ov.deskH);
					var dw = dr - dl;
					var dh = db - dt;
					try {
						var piece = ov.croplocal(dl, dt, dw, dh);
						var dest = new System.Windows.Rect(
							inter.Left - left, inter.Top - top, inter.Width, inter.Height);
						dc.DrawImage(piece, dest);
					}
					catch (Exception ex) {
						CaptureLog.Ex($"CropVirtual mon=({ov.monL},{ov.monT})", ex);
					}
				}
			}
			var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
			rtb.Render(dv);
			rtb.Freeze();
			// 已 Freeze 的 RTB 可直接用；避免再全图 Clone 卡 UI
			return rtb;
		}

		public void Complete(BitmapSource img, Rect selectedDip) {
			CaptureLog.Info($"Session.Complete enter Finishing={Finishing} img={CaptureLog.Bmp(img)} dip={selectedDip}");
			if (Finishing) {
				CaptureLog.Info("Session.Complete SKIP already finishing");
				return;
			}
			Finishing = true;
			Confirmed = true;
			// 已冻结位图与窗体生命周期无关，勿再全图拷贝（大图可卡数秒）
			try {
				Image = EnsureFrozen(img);
				CaptureLog.Info($"Session.Complete img={CaptureLog.Bmp(Image)}");
			}
			catch (Exception ex) {
				CaptureLog.Ex("Session.Complete EnsureFrozen", ex);
				Image = img;
			}
			SelectedDip = selectedDip;
			closeall();
			try { Finished?.Invoke(); } catch (Exception ex) { CaptureLog.Ex("Session.Complete Finished", ex); }
			CaptureLog.Info($"Session.Complete done Confirmed={Confirmed} Image={CaptureLog.Bmp(Image)}");
		}

		public void Cancel() {
			CaptureLog.Info($"Session.Cancel enter Finishing={Finishing} Confirmed={Confirmed}");
			// 已确认成功则绝不清 Image（防止 Closed 竞态把结果抹掉）
			if (Finishing) {
				CaptureLog.Info("Session.Cancel SKIP already finishing");
				return;
			}
			Finishing = true;
			Confirmed = false;
			Image = null;
			closeall();
			try { Finished?.Invoke(); } catch (Exception ex) { CaptureLog.Ex("Session.Cancel Finished", ex); }
		}

		void closeall() {
			CaptureLog.Info($"Session.closeall count={Windows.Count}");
			foreach (var w in Windows.ToArray()) {
				try {
					w.closingFromSession = true;
					CaptureLog.Info($"  close mon=({w.monL},{w.monT}) {w.deskW}x{w.deskH} visible={w.IsVisible}");
					if (w.IsVisible || w.IsLoaded)
						w.Close();
				}
				catch (Exception ex) { CaptureLog.Ex("Session.closeall", ex); }
			}
		}
	}

	/// <summary>
	/// 全屏多显示器截图：先同时冻结各屏，再显示各屏遮罩；
	/// 在某一屏按下第一角后锁定该屏。
	/// </summary>
	public static CaptureResult Run(bool annotate = false) =>
		runsession(annotate: annotate, board: false);

	/// <summary>
	/// 屏幕画板：冻结全屏静止画面，在光标所在屏上直接标注（跳过框选）。
	/// 其它屏仅显示冻结底图，Esc 取消 · Enter/完成 复制。
	/// </summary>
	public static CaptureResult RunBoard() =>
		runsession(annotate: true, board: true);

	static CaptureResult runsession(bool annotate, bool board) {
		CaptureLog.SessionStart($"Run annotate={annotate} board={board}");
		try {
			CaptureLog.Info(ScreenDpi.BuildReport().Replace("\r\n", " | ").Replace("\n", " | "));
		}
		catch (Exception ex) { CaptureLog.Ex("BuildReport", ex); }

		var session = new Session(annotate, board);
		// 画板不需要窗口枚举；框选模式仍要
		session.TopWindows = board ? new List<WinHit>() : enumtopwindows();
		CaptureLog.Info($"TopWindows count={session.TopWindows.Count}");

		// 并行抓各屏（DXGI 最慢；双屏并行可显著降进入延时）
		var screens = System.Windows.Forms.Screen.AllScreens
			.Where(s => s.Bounds.Width > 0 && s.Bounds.Height > 0)
			.ToArray();
		var freezes = new BitmapSource[screens.Length];
		// 遮罩窗矩形必须与冻结图像素对齐（1:1），否则 Stretch 会模糊/微偏移
		var overlays = new System.Drawing.Rectangle[screens.Length];
		var t0 = Environment.TickCount;
		System.Threading.Tasks.Parallel.For(0, screens.Length, i => {
			try {
				var s = screens[i];
				freezes[i] = CaptureMonitor(s, out var fw, out var fh, out var ovr);
				// 尺寸无效时回退 Bounds；原点优先用 DXGI/抓取给出的 overlay
				if (ovr.Width < 8 || ovr.Height < 8)
					ovr = s.Bounds;
				// 若帧尺寸与 overlay 不一致，强制用帧尺寸贴在原点，保证显示 1:1
				if (freezes[i] != null
					&& (Math.Abs(freezes[i].PixelWidth - ovr.Width) > 0
						|| Math.Abs(freezes[i].PixelHeight - ovr.Height) > 0)) {
					ovr = new System.Drawing.Rectangle(
						ovr.Left, ovr.Top, freezes[i].PixelWidth, freezes[i].PixelHeight);
				}
				overlays[i] = ovr;
				CaptureLog.Info($"Monitor#{i + 1} {(s.Primary ? "Primary" : "Sec")} {s.DeviceName} Bounds={s.Bounds} overlay={ovr} freeze={CaptureLog.Bmp(freezes[i])} out={fw}x{fh}");
			}
			catch (Exception ex) {
				CaptureLog.Ex($"Monitor#{i + 1} capture", ex);
			}
		});
		CaptureLog.Info($"Parallel capture screens={screens.Length} cost={Environment.TickCount - t0}ms");

		for (int i = 0; i < screens.Length; i++) {
			if (freezes[i] == null) continue;
			var ovr = overlays[i].Width > 0 ? overlays[i] : screens[i].Bounds;
			session.Windows.Add(new CaptureOverlay(session, ovr, freezes[i]));
		}
		if (session.Windows.Count == 0) {
			CaptureLog.Info("Run ABORT no windows");
			return new CaptureResult();
		}

		var frame = new System.Windows.Threading.DispatcherFrame();
		session.Finished += () => {
			CaptureLog.Info("DispatcherFrame end");
			frame.Continue = false;
		};
		foreach (var w in session.Windows)
			w.Show();
		// 焦点给光标所在屏
		CaptureOverlay focus = session.Windows[0];
		try {
			if (trycursor(out var cx, out var cy)) {
				var hit = session.Windows.FirstOrDefault(w =>
					cx >= w.monL && cx < w.monL + w.monBoundW
					&& cy >= w.monT && cy < w.monT + w.monBoundH);
				focus = hit ?? session.Windows[0];
				CaptureLog.Info($"cursor=({cx},{cy}) focusMon=({focus.monL},{focus.monT})");
			}
			focus.Activate();
		}
		catch (Exception ex) { CaptureLog.Ex("Activate", ex); }

		// 屏幕画板：等 Loaded 后再进全屏标注（否则 Actual* / 工具条布局未就绪）
		if (board) {
			try {
				session.BoardOwner = focus;
				foreach (var w in session.Windows) {
					if (w == focus) continue;
					scheduleboard(w, backdrop: true);
				}
				scheduleboard(focus, backdrop: false);
			}
			catch (Exception ex) {
				CaptureLog.Ex("schedule board", ex);
				session.Cancel();
			}
		}

		System.Windows.Threading.Dispatcher.PushFrame(frame);
		var result = new CaptureResult {
			Confirmed = session.Confirmed,
			WantOcr = session.WantOcr,
			Image = session.Image,
			SelectedDip = session.SelectedDip,
		};
		CaptureLog.Info($"Run return Confirmed={result.Confirmed} WantOcr={result.WantOcr} Image={CaptureLog.Bmp(result.Image)} SelectedDip={result.SelectedDip}");
		return result;
	}

	readonly Session session;
	readonly bool annotateMode;
	/// <summary>屏幕画板主屏（全屏标注，无框选/手柄）。</summary>
	bool boardMode;
	/// <summary>画板副屏：仅冻结底图，不可标注。</summary>
	bool boardBackdrop;
	/// <summary>截图标注副屏：冻结 + 选区遮罩，无工具条。</summary>
	bool annotateGuest;
	/// <summary>本屏冻结底图（真实像素，如 1920×1200）。</summary>
	readonly BitmapSource desktopBmp;
	/// <summary>底图像素尺寸（desk*）；用于裁切。</summary>
	readonly int deskW, deskH;
	/// <summary>Windows 虚拟坐标下本屏原点与 Bounds 尺寸（可能与 desk* 不同，如 2560×1600）。</summary>
	readonly int monL, monT, monBoundW, monBoundH;
	readonly System.Drawing.Rectangle monBounds;
	/// <summary>本屏 Effective DPI 缩放（仅诊断；WPF 尺寸用 sysScale）。</summary>
	readonly double monScale;
	/// <summary>进程系统 DPI 缩放（WPF DIP = 物理 / sysScale）。</summary>
	readonly double sysScale;

	Phase phase = Phase.Select;
	Tool tool = Tool.Rect;

	/// <summary>标注阶段用的画布起点（proot DIP）。</summary>
	Point start;
	bool dragging;
	bool drawing;
	bool closingFromSession;
	Shape draft;
	readonly List<UIElement> strokes = new();
	/// <summary>文字标注宿主 Tag。</summary>
	const string TEXT_TAG = "annotext";
	/// <summary>当前选中的文字（显示虚线框，导出前清除）。</summary>
	Border selText;
	/// <summary>正在编辑的文字宿主。</summary>
	Border editHost;
	string textEditBackup = "";
	bool textEditIsNew;
	bool textDrag;
	Point textDragMouse, textDragOrigin;

	/// <summary>框选：相对本屏左上角的底图像素。</summary>
	int locX0, locY0;
	/// <summary>是否已从单击转为拖拽框选（移动超过阈值）。</summary>
	bool regionDrag;
	/// <summary>当前悬停窗口在底图像素坐标（本屏裁剪后）。</summary>
	int hoverL, hoverT, hoverW, hoverH;
	/// <summary>悬停窗口虚拟屏矩形（完整，可跨屏）。</summary>
	int hoverVirtL, hoverVirtT, hoverVirtW, hoverVirtH;
	bool hasHoverWin;
	/// <summary>光标下像素色值（供 Ctrl+C）。</summary>
	string lastColorHex = "#000000";
	int lastCursorX, lastCursorY;

	double selX, selY, selW, selH;
	BitmapSource shot;
	int imgW, imgH;
	/// <summary>当前选区在本屏底图上的像素矩形（标注阶段可移动/缩放）。</summary>
	int cropL, cropT, cropW, cropH;
	/// <summary>拖动调整选区中。</summary>
	bool adjDrag;
	AdjHit adjHit = AdjHit.None;
	int adjStartLX, adjStartLY;
	int adj0L, adj0T, adj0W, adj0H;
	/// <summary>跨屏调整：按下时虚拟坐标与选区快照。</summary>
	int adjStartVX, adjStartVY;
	int adj0VL, adj0VT, adj0VW, adj0VH;
	/// <summary>8 点手柄 NW N NE E SE S SW W。</summary>
	WpfRectangle[] handles;
	/// <summary>本窗 HWND（排除窗口枚举命中自身）。</summary>
	IntPtr selfHwnd;
	/// <summary>悬停 UI 节流（避免每像素移动都钻 HWND / 重建放大镜导致全屏卡顿）。</summary>
	long lastHoverTick;
	int lastHoverLx = int.MinValue, lastHoverLy = int.MinValue;
	/// <summary>遮罩缓存，相同矩形不重建 Geometry。</summary>
	double lastMaskX = double.NaN, lastMaskY, lastMaskW, lastMaskH;
	/// <summary>放大镜上次采样原点（底图像素）。</summary>
	int lastMagSx = int.MinValue, lastMagSy = int.MinValue;
	/// <summary>跨屏拼接重裁节流（仅 viewPort 不可用时）。</summary>
	long lastAdjUiTick;
	/// <summary>
	/// 选区预览：整屏冻结图 + 偏移（不每帧 Crop）。false=跨屏拼接裁切图。
	/// </summary>
	bool viewPortUi;
	/// <summary>
	/// 框选确认后武装：下一次在系统双击时限/距离内的按下直接完成，
	/// 避免「单击选区 → 再双击一遍」才能结束。
	/// </summary>
	bool dblFinishArmed;
	int dblFinishArmTick;
	int dblFinishArmX, dblFinishArmY;
	const int HoverMinMs = 20;
	const int AdjUiMinMs = 40;

	/// <summary>框选结果（屏幕 DIP，兼容旧调用）。</summary>
	public Rect SelectedDip { get; private set; }
	/// <summary>是否确认（复制/保存/纯框选完成）。</summary>
	public bool Confirmed { get; private set; }
	/// <summary>最终图像（含标注）；纯 OCR 框选时为截取的位图。</summary>
	public BitmapSource ResultImage { get; private set; }

	/// <summary>设计器 / 无参（勿直接 ShowDialog，请用 <see cref="Run"/>）。</summary>
	public CaptureOverlay()
		: this(new Session(false), primarybounds(), null) { }

	static System.Drawing.Rectangle primarybounds() {
		try {
			var s = System.Windows.Forms.Screen.PrimaryScreen;
			if (s != null) return s.Bounds;
		}
		catch { }
		return new System.Drawing.Rectangle(0, 0, 800, 600);
	}

	CaptureOverlay(Session session, System.Drawing.Rectangle bounds, BitmapSource freeze) {
		this.session = session ?? new Session(false);
		annotateMode = this.session.Annotate;
		monBounds = bounds.Width > 0 ? bounds : new System.Drawing.Rectangle(0, 0, 1, 1);
		monL = monBounds.Left;
		monT = monBounds.Top;
		// 遮罩窗口按 Windows Bounds 铺满该屏（虚拟坐标 / 物理像素）
		monBoundW = Math.Max(1, monBounds.Width);
		monBoundH = Math.Max(1, monBounds.Height);
		monScale = ScreenDpi.GetMonitorScale(monL + monBoundW / 2, monT + monBoundH / 2);
		// System DPI Aware：WPF DIP 必须用系统缩放，不能用副屏 monScale（混合 DPI 会把内容缩错）
		sysScale = Math.Max(0.25, ScreenDpi.SystemScale());
		desktopBmp = freeze ?? CaptureMonitor(monBounds, out _, out _);
		// 底图用真实抓取尺寸（可为 1920×1200，与 Bounds 2560×1600 不同）
		deskW = Math.Max(1, desktopBmp?.PixelWidth ?? monBoundW);
		deskH = Math.Max(1, desktopBmp?.PixelHeight ?? monBoundH);
		CaptureLog.Info($"Overlay mon=({monL},{monT}) bounds={monBoundW}x{monBoundH} bmp={deskW}x{deskH} monScale={monScale:0.##} sysScale={sysScale:0.##}");

		InitializeComponent();

		// 物理 Bounds → WPF DIP（系统缩放）；再 SetWindowPos 钉到物理矩形，二者一致才 1:1
		var dipW = monBoundW / sysScale;
		var dipH = monBoundH / sysScale;
		Width = dipW;
		Height = dipH;
		Left = monL / sysScale;
		Top = monT / sysScale;

		imgDesktop.Source = desktopBmp;
		imgDesktop.Width = dipW;
		imgDesktop.Height = dipH;
		Canvas.SetLeft(imgDesktop, 0);
		Canvas.SetTop(imgDesktop, 0);

		SourceInitialized += (_, _) => {
			try {
				var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
				selfHwnd = hwnd;
				// 必须用物理 Bounds 盖住该屏；与 Width=px/sysScale 对齐，避免 WPF 再把窗撑大/缩小
				if (hwnd != IntPtr.Zero)
					SetWindowPos(hwnd, HwndTopmost, monL, monT, monBoundW, monBoundH, SWP_SHOWWINDOW);
			}
			catch { }
		};

		Loaded += (_, _) => {
			// 以实际客户区为准（SetWindowPos 后 Actual* 应为 bounds/sysScale）
			var aw = Math.Max(1.0, ActualWidth);
			var ah = Math.Max(1.0, ActualHeight);
			imgDesktop.Width = aw;
			imgDesktop.Height = ah;
			CaptureLog.Info($"Overlay Loaded mon=({monL},{monT}) Actual={aw:0.#}x{ah:0.#} expectDip={monBoundW / sysScale:0.#}x{monBoundH / sysScale:0.#}");
			updatemask(0, 0, 0, 0);
			try { proot.Focus(); } catch { }
		};

		Closed += (_, _) => {
			CaptureLog.Info($"Closed mon=({monL},{monT}) Finishing={session?.Finishing} fromSession={closingFromSession}");
			// 用户 AltF4 关单窗 → 取消整次会话
			if (session != null && !session.Finishing && !closingFromSession) {
				CaptureLog.Info("Closed → Cancel (user closed window)");
				session.Cancel();
			}
		};

		// 框选
		proot.MouseLeftButtonDown += onselectdown;
		proot.MouseMove += onselectmove;
		proot.MouseLeftButtonUp += onselectup;
		// 隧道阶段统一处理双击完成（不依赖点中 pdraw；衔接框选单击后的第二次按下）
		PreviewMouseLeftButtonDown += onpreviewdown;
		Loaded += (_, _) => {
			// 进入时按光标位置先躲一次
			try {
				if (trycursor(out var cx, out var cy)
					&& cx >= monL && cx < monL + monBoundW
					&& cy >= monT && cy < monT + monBoundH) {
					var (cw, ch) = clientsize();
					var px = (cx - monL) * cw / Math.Max(1, monBoundW);
					var py = (cy - monT) * ch / Math.Max(1, monBoundH);
					updatehintdodge(new Point(px, py));
				}
			}
			catch { }
		};

		// 标注
		pdraw.MouseLeftButtonDown += ondrawdown;
		pdraw.MouseMove += ondrawmove;
		pdraw.MouseLeftButtonUp += ondrawup;

		KeyDown += onkey;
		trect.Checked += (_, _) => { if (trect.IsChecked == true) settool(Tool.Rect); };
		tellipse.Checked += (_, _) => { if (tellipse.IsChecked == true) settool(Tool.Ellipse); };
		tline.Checked += (_, _) => { if (tline.IsChecked == true) settool(Tool.Line); };
		tarrow.Checked += (_, _) => { if (tarrow.IsChecked == true) settool(Tool.Arrow); };
		ttext.Checked += (_, _) => { if (ttext.IsChecked == true) settool(Tool.Text); };
		// 再次点击已选工具 → 取消绘制，回到移动选区
		trect.Unchecked += (_, _) => { if (tool == Tool.Rect) settool(Tool.None); };
		tellipse.Unchecked += (_, _) => { if (tool == Tool.Ellipse) settool(Tool.None); };
		tline.Unchecked += (_, _) => { if (tool == Tool.Line) settool(Tool.None); };
		tarrow.Unchecked += (_, _) => { if (tool == Tool.Arrow) settool(Tool.None); };
		ttext.Unchecked += (_, _) => { if (tool == Tool.Text) settool(Tool.None); };
		bundo.Click += (_, _) => undo();
		bsave.Click += (_, _) => savefile();
		bocr.Click += (_, _) => finishocr();
		bok.Click += (_, _) => finishcopy();
		bcancel.Click += (_, _) => session?.Cancel();
	}

	// ───────── 框选阶段 ─────────
	// 悬停：窗口绿框 + 放大镜；单击窗口 = 截窗进标注；双击 = 直接完成；拖拽 = 跨屏框选。

	/// <summary>框选确认进入标注后武装：连续第二次按下可直接完成。</summary>
	void armdblfinish() {
		if (boardMode || !annotateMode) return;
		dblFinishArmed = true;
		dblFinishArmTick = Environment.TickCount;
		if (!trycursor(out dblFinishArmX, out dblFinishArmY)) {
			dblFinishArmX = 0;
			dblFinishArmY = 0;
		}
	}

	/// <summary>
	/// 双击完成：标注阶段 ClickCount≥2，或框选后在系统双击时限/距离内的下一次按下。
	/// </summary>
	bool tryconsumedblfinish(MouseButtonEventArgs e) {
		if (phase != Phase.Annotate) return false;
		if (annotateGuest || boardBackdrop) return false;
		if (editHost != null) return false;
		if (e.OriginalSource is DependencyObject d && findtexthost(d) != null) return false;
		try {
			if (isoverbar(e.GetPosition(proot))) return false;
		}
		catch { }

		var finish = e.ClickCount >= 2;
		if (!finish && dblFinishArmed && !boardMode) {
			var elapsed = unchecked(Environment.TickCount - dblFinishArmTick);
			uint limit;
			try { limit = GetDoubleClickTime(); }
			catch { limit = 500; }
			if (limit < 200) limit = 200;
			if (limit > 2000) limit = 2000;
			if (elapsed >= 0 && elapsed <= (int)limit && trycursor(out var cx, out var cy)) {
				int tolX = 6, tolY = 6;
				try {
					tolX = Math.Max(4, GetSystemMetrics(SM_CXDOUBLECLK) / 2);
					tolY = Math.Max(4, GetSystemMetrics(SM_CYDOUBLECLK) / 2);
				}
				catch { }
				if (Math.Abs(cx - dblFinishArmX) <= tolX && Math.Abs(cy - dblFinishArmY) <= tolY)
					finish = true;
			}
		}

		// 任意按下后解除武装（避免误把稍后单击当完成）
		dblFinishArmed = false;
		if (!finish) return false;

		CaptureLog.Info($"dbl-finish ClickCount={e.ClickCount} board={boardMode}");
		e.Handled = true;
		finishcopy();
		return true;
	}

	void onpreviewdown(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton != MouseButton.Left) return;
		if (tryconsumedblfinish(e)) return;
	}

	void onselectdown(object sender, MouseButtonEventArgs e) {
		// 标注阶段：点在选区外不处理（手柄/画布各自接管）
		if (phase == Phase.Annotate) return;
		if (boardMode || boardBackdrop || annotateGuest) return;
		if (phase != Phase.Select) return;
		// 其它屏已在拖拽则忽略
		if (session?.DragOwner != null && session.DragOwner != this) return;
		if (isoverbar(e.GetPosition(proot))) return;
		if (!trylocal(out var lx, out var ly, e.GetPosition(proot))) return;
		if (!trycursor(out var cx, out var cy)) return;

		// 框选阶段双击悬停窗口：直接截取并完成（跳过再双击）
		if (e.ClickCount >= 2) {
			updatehoverui(cx, cy, lx, ly, e.GetPosition(proot));
			if (hasHoverWin && hoverVirtW >= 4 && hoverVirtH >= 4) {
				CaptureLog.Info($"down DBL-WINDOW virt=({hoverVirtL},{hoverVirtT},{hoverVirtW},{hoverVirtH})");
				e.Handled = true;
				commitvirtual(hoverVirtL, hoverVirtT, hoverVirtW, hoverVirtH);
				if (annotateMode && phase == Phase.Annotate)
					finishcopy();
				return;
			}
			if (hasHoverWin && hoverW >= 4 && hoverH >= 4) {
				e.Handled = true;
				commitcrop(hoverL, hoverT, hoverW, hoverH);
				if (annotateMode && phase == Phase.Annotate)
					finishcopy();
				return;
			}
		}

		if (session != null && !session.BeginDrag(this, cx, cy)) {
			CaptureLog.Info($"down BeginDrag FAIL mon=({monL},{monT})");
			return;
		}

		dragging = true;
		regionDrag = false;
		locX0 = lx;
		locY0 = ly;
		updatehoverui(cx, cy, lx, ly, e.GetPosition(proot));
		CaptureLog.Info($"down mon=({monL},{monT}) virt=({cx},{cy}) local=({lx},{ly}) hover={hasHoverWin}");
		rsel.Visibility = Visibility.Collapsed;
		proot.CaptureMouse();
		e.Handled = true;
	}

	void onselectmove(object sender, MouseEventArgs e) {
		// 标注：拖动手柄/边缘调整选区
		if (phase == Phase.Annotate) {
			if (adjDrag) {
				doadjmove(e.GetPosition(proot));
				e.Handled = true;
			}
			return;
		}
		if (boardMode || boardBackdrop) return;
		if (phase != Phase.Select) return;
		// 非拖拽发起屏：仅在未被占用时做悬停
		if (session?.DragOwner != null && session.DragOwner != this) return;

		var canvas = e.GetPosition(proot);
		updatehintdodge(canvas);
		trycursor(out var cx, out var cy);
		if (!trylocal(out var lx, out var ly, canvas)) return;

		if (!dragging) {
			// 同像素或过密事件：跳过重活，保持 UI 线程可响应
			var now = Environment.TickCount;
			if (lx == lastHoverLx && ly == lastHoverLy) return;
			if (lastHoverTick != 0 && unchecked(now - lastHoverTick) < HoverMinMs) return;
			lastHoverTick = now;
			lastHoverLx = lx;
			lastHoverLy = ly;
			updatehoverui(cx, cy, lx, ly, canvas);
			return;
		}

		// 虚拟坐标判断是否进入框选
		var dist = Math.Max(Math.Abs(cx - (session?.DragVX0 ?? cx)), Math.Abs(cy - (session?.DragVY0 ?? cy)));
		if (!regionDrag && dist >= 4) {
			regionDrag = true;
			if (session != null) session.RegionDrag = true;
			rwin.Visibility = Visibility.Collapsed;
			hasHoverWin = false;
			bmag.Visibility = Visibility.Collapsed;
		}
		if (regionDrag && session != null) {
			session.UpdateDrag(cx, cy);
			var pw = Math.Abs(session.DragVX1 - session.DragVX0);
			var ph = Math.Abs(session.DragVY1 - session.DragVY0);
			lbcap.Text = $"{pw} × {ph} · 可跨屏 · 松手确认 · Esc 取消";
			updatehintdodge(canvas, allowFlip: false);
		}
		else {
			updatehoverui(cx, cy, lx, ly, canvas);
		}
	}

	/// <summary>按虚拟屏选区更新本屏绿框/挖空（跨屏时各屏各自相交部分）。</summary>
	internal void applyvirtualsel(int vx0, int vy0, int vx1, int vy1) {
		if (phase != Phase.Select) return;
		var left = Math.Min(vx0, vx1);
		var top = Math.Min(vy0, vy1);
		var right = Math.Max(vx0, vx1);
		var bottom = Math.Max(vy0, vy1);
		// 经 desk 像素再映到画布，与最终裁切/标注层同一路径，避免框选时内容微偏
		if (!tryvirtualtodesk(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top),
				out var dl, out var dt, out var dw, out var dh)) {
			rsel.Visibility = Visibility.Collapsed;
			updatemask(0, 0, 0, 0);
			return;
		}
		var (ox, oy, ow, oh) = localtooverlay(dl, dt, dw, dh);
		Canvas.SetLeft(rsel, ox);
		Canvas.SetTop(rsel, oy);
		rsel.Width = Math.Max(0, ow);
		rsel.Height = Math.Max(0, oh);
		rsel.Visibility = Visibility.Visible;
		updatemask(ox, oy, ow, oh);
		rwin.Visibility = Visibility.Collapsed;
		bmag.Visibility = Visibility.Collapsed;
	}

	/// <summary>
	/// 提示文字固定在顶边；光标靠近当前侧时切换到另一侧（左上 ↔ 右上），避免挡操作。
	/// </summary>
	/// <param name="allowFlip">false 时只按当前侧贴边（文案变长时用）。</param>
	void updatehintdodge(Point canvasPos, bool allowFlip = true) {
		if (phase != Phase.Select) return;
		if (bhint.Visibility != Visibility.Visible) return;
		const double pad = 24;
		const double margin = 12; // 光标进入提示区外扩边距才触发
		var aw = ActualWidth > 1 ? ActualWidth : Width;
		if (aw < 80) return;

		if (bhint.ActualWidth < 1 || bhint.ActualHeight < 1)
			bhint.UpdateLayout();
		var hw = bhint.ActualWidth > 1 ? bhint.ActualWidth : 280;
		var hh = bhint.ActualHeight > 1 ? bhint.ActualHeight : 40;

		var curLeft = Canvas.GetLeft(bhint);
		if (double.IsNaN(curLeft)) curLeft = pad;
		var onLeft = curLeft < aw * 0.5;

		var left = onLeft ? pad : Math.Max(pad, aw - hw - pad);
		var top = pad;
		if (allowFlip) {
			var hit = canvasPos.X >= left - margin
				&& canvasPos.X <= left + hw + margin
				&& canvasPos.Y >= top - margin
				&& canvasPos.Y <= top + hh + margin;
			if (hit) {
				onLeft = !onLeft;
				left = onLeft ? pad : Math.Max(pad, aw - hw - pad);
			}
		}
		Canvas.SetLeft(bhint, left);
		Canvas.SetTop(bhint, top);
	}

	void onselectup(object sender, MouseButtonEventArgs e) {
		if (phase == Phase.Annotate && adjDrag) {
			endadj();
			e.Handled = true;
			return;
		}
		if (boardMode || boardBackdrop) return;
		if (phase != Phase.Select || !dragging) return;
		dragging = false;
		try { proot.ReleaseMouseCapture(); } catch { }
		trycursor(out var cx, out var cy);
		if (!trylocal(out var lx, out var ly, e.GetPosition(proot))) {
			if (session != null) session.DragOwner = null;
			session?.Cancel();
			return;
		}

		// 单击：截取悬停窗口（可跨屏窗 → 虚拟矩形拼接）
		if (!regionDrag && session?.RegionDrag != true) {
			updatehoverui(cx, cy, lx, ly, e.GetPosition(proot));
			if (session != null) session.DragOwner = null;
			if (hasHoverWin && hoverVirtW >= 4 && hoverVirtH >= 4) {
				CaptureLog.Info($"up CLICK-WINDOW virt=({hoverVirtL},{hoverVirtT},{hoverVirtW},{hoverVirtH})");
				commitvirtual(hoverVirtL, hoverVirtT, hoverVirtW, hoverVirtH);
				// 武装：同位置的第二次按下（双击）直接完成，不必再双击一遍
				if (annotateMode && phase == Phase.Annotate)
					armdblfinish();
				return;
			}
			// 回退本屏局部（兼容）
			if (hasHoverWin && hoverW >= 4 && hoverH >= 4) {
				commitcrop(hoverL, hoverT, hoverW, hoverH);
				if (annotateMode && phase == Phase.Annotate)
					armdblfinish();
				return;
			}
			session?.Cancel();
			return;
		}

		// 拖拽框选：虚拟坐标
		var vx0 = session?.DragVX0 ?? cx;
		var vy0 = session?.DragVY0 ?? cy;
		var left = Math.Min(vx0, cx);
		var top = Math.Min(vy0, cy);
		var pw = Math.Abs(cx - vx0);
		var ph = Math.Abs(cy - vy0);
		if (session != null) {
			session.DragOwner = null;
			session.RegionDrag = false;
		}
		CaptureLog.Info($"up DRAG virt=({left},{top},{pw},{ph}) from mon=({monL},{monT})");
		if (pw < 4 || ph < 4) {
			session?.Cancel();
			return;
		}
		commitvirtual(left, top, pw, ph);
	}

	/// <summary>本屏 desk 像素裁切（单屏）。</summary>
	void commitcrop(int left, int top, int pw, int ph) {
		// 转虚拟坐标再统一走拼接（单屏也正确）
		var vl = monL + (int)Math.Round(left * (double)monBoundW / deskW);
		var vt = monT + (int)Math.Round(top * (double)monBoundH / deskH);
		var vr = monL + (int)Math.Round((left + pw) * (double)monBoundW / deskW);
		var vb = monT + (int)Math.Round((top + ph) * (double)monBoundH / deskH);
		commitvirtual(vl, vt, Math.Max(1, vr - vl), Math.Max(1, vb - vt));
	}

	/// <summary>虚拟屏矩形裁切确认（支持跨屏拼接）。</summary>
	void commitvirtual(int left, int top, int pw, int ph) {
		rwin.Visibility = Visibility.Collapsed;
		bmag.Visibility = Visibility.Collapsed;
		bhint.Visibility = Visibility.Collapsed;
		rsel.Visibility = Visibility.Collapsed;
		hasHoverWin = false;

		CaptureLog.Info($"commitvirtual virt=({left},{top},{pw},{ph}) screens={session?.Windows.Count}");
		// 本屏 desk 像素矩形：裁切与 UI 必须用同一组坐标，否则标注层相对底图会偏移
		if (!tryvirtualtodesk(left, top, pw, ph, out var dl, out var dt, out var dw, out var dh)) {
			CaptureLog.Info("commitvirtual no intersect with this monitor");
			session?.Cancel();
			return;
		}
		// 可调选区始终用本屏底图像素矩形（移动/缩放时重裁）
		cropL = dl;
		cropT = dt;
		cropW = dw;
		cropH = dh;

		try {
			BitmapSource crop;
			if (annotateMode && session != null && session.Windows.Count > 1) {
				// 多屏标注：整段虚拟矩形拼接（选区可后续跨屏拖动）
				crop = session.CropVirtual(left, top, pw, ph);
			}
			else if (annotateMode) {
				// 单屏标注：与选区手柄同源
				crop = croplocal(dl, dt, dw, dh);
			}
			else if (session != null && session.Windows.Count > 1)
				crop = session.CropVirtual(left, top, pw, ph);
			else
				// 单屏：按 desk 像素裁切（与下方 localtooverlay 同源）
				crop = croplocal(dl, dt, dw, dh);
			// croplocal/CropVirtual 已返回冻结图，勿再 CloneFrozen 全图拷贝
			shot = EnsureFrozen(crop);
			CaptureLog.Info($"commitvirtual shot={CaptureLog.Bmp(shot)} desk=({dl},{dt},{dw},{dh}) annotate={annotateMode}");
		}
		catch (Exception ex) {
			CaptureLog.Ex("commitvirtual", ex);
			session?.Cancel();
			return;
		}

		// DIP 选区：严格由 desk 像素矩形映射，保证与底图/裁切 1:1
		var (ox, oy, ow, oh) = localtooverlay(dl, dt, dw, dh);
		selX = ox; selY = oy;
		selW = Math.Max(1, ow); selH = Math.Max(1, oh);
		SelectedDip = new Rect(selX, selY, selW, selH);

		ResultImage = shot;
		if (!annotateMode) {
			Confirmed = true;
			session?.Complete(shot, SelectedDip);
			return;
		}
		// 标注：记录虚拟选区；多屏时其它屏保持冻结，选区可跨屏移动
		if (session != null) {
			session.InAnnotate = true;
			session.AnnotateHost = this;
			session.AnnVL = left;
			session.AnnVT = top;
			session.AnnVW = Math.Max(MIN_CROP, pw);
			session.AnnVH = Math.Max(MIN_CROP, ph);
			foreach (var w in session.Windows) {
				if (w == this) continue;
				try { w.enterannotateguest(); } catch (Exception ex) { CaptureLog.Ex("enterannotateguest", ex); }
			}
		}
		enterannotate();
	}

	/// <summary>虚拟矩形 → 本屏 desk 像素矩形（Floor/Ceiling，与 CropVirtual 一致）。</summary>
	bool tryvirtualtodesk(int left, int top, int pw, int ph,
		out int dl, out int dt, out int dw, out int dh) {
		dl = dt = dw = dh = 0;
		var mon = new System.Drawing.Rectangle(monL, monT, monBoundW, monBoundH);
		var inter = System.Drawing.Rectangle.Intersect(mon, new System.Drawing.Rectangle(left, top, pw, ph));
		if (inter.Width < 1 || inter.Height < 1) return false;
		dl = (int)Math.Floor((inter.Left - monL) * (double)deskW / monBoundW);
		dt = (int)Math.Floor((inter.Top - monT) * (double)deskH / monBoundH);
		var dr = (int)Math.Ceiling((inter.Right - monL) * (double)deskW / monBoundW);
		var db = (int)Math.Ceiling((inter.Bottom - monT) * (double)deskH / monBoundH);
		dl = Compat.Clamp(dl, 0, deskW - 1);
		dt = Compat.Clamp(dt, 0, deskH - 1);
		dr = Compat.Clamp(dr, dl + 1, deskW);
		db = Compat.Clamp(db, dt + 1, deskH);
		dw = Math.Max(1, dr - dl);
		dh = Math.Max(1, db - dt);
		return true;
	}

	/// <summary>单屏：虚拟矩形 → 本屏 desk 裁切。</summary>
	BitmapSource croplocalfromvirtual(int left, int top, int pw, int ph) {
		if (!tryvirtualtodesk(left, top, pw, ph, out var dl, out var dt, out var dw, out var dh))
			throw new InvalidOperationException("选区与本屏无交集");
		return croplocal(dl, dt, dw, dh);
	}

	// ───────── 窗口识别 / 放大镜 / 取色 ─────────

	static List<WinHit> enumtopwindows() {
		var list = new List<WinHit>();
		try {
			EnumWindows((h, _) => {
				try {
					if (h == IntPtr.Zero || !IsWindowVisible(h) || IsIconic(h))
						return true;
					var style = GetWindowLong(h, GWL_STYLE);
					if ((style & WS_CHILD) != 0) return true;
					var ex = GetWindowLong(h, GWL_EXSTYLE);
					if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & 0x00040000) == 0) // 无 APPWINDOW 的 tool
						return true;
					// 被 DWM 遮罩（UWP 等）
					try {
						if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
							return true;
					}
					catch { }
					var cls = new System.Text.StringBuilder(64);
					GetClassName(h, cls, cls.Capacity);
					var cn = cls.ToString();
					// 任务栏 / 桌面壳
					if (cn is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW")
						return true;
					if (!GetWindowRect(h, out var r)) return true;
					var w = r.Right - r.Left;
					var hgt = r.Bottom - r.Top;
					if (w < 8 || hgt < 8) return true;
					// 超大桌面级窗口（几乎等于虚拟屏）在列表末尾也允许，但 Z 序靠后
					list.Add(new WinHit(h, new System.Drawing.Rectangle(r.Left, r.Top, w, hgt)));
				}
				catch { }
				return true;
			}, IntPtr.Zero);
		}
		catch (Exception ex) { CaptureLog.Ex("enumtopwindows", ex); }
		return list;
	}

	/// <summary>
	/// 虚拟屏坐标下命中窗口：先顶层 Z 序，再钻到最深可见子 HWND
	///（Chrome 内容区、记事本编辑区等）。
	/// </summary>
	bool tryfindwindowat(int cx, int cy, out System.Drawing.Rectangle winRect) {
		winRect = default;
		var wins = session?.TopWindows;
		if (wins == null || wins.Count == 0) return false;
		var skip = new HashSet<IntPtr>();
		if (selfHwnd != IntPtr.Zero) skip.Add(selfHwnd);
		if (session != null) {
			foreach (var w in session.Windows) {
				if (w.selfHwnd != IntPtr.Zero) skip.Add(w.selfHwnd);
			}
		}
		foreach (var hit in wins) {
			if (skip.Contains(hit.Hwnd)) continue;
			var r = hit.Rect;
			if (cx < r.Left || cx >= r.Right || cy < r.Top || cy >= r.Bottom)
				continue;
			// 在该顶层窗内钻到最深子控件
			var deep = DeepestChildAt(hit.Hwnd, cx, cy, skip);
			if (deep != IntPtr.Zero && GetWindowRect(deep, out var cr)) {
				var cw = cr.Right - cr.Left;
				var ch = cr.Bottom - cr.Top;
				if (cw >= 8 && ch >= 8) {
					winRect = new System.Drawing.Rectangle(cr.Left, cr.Top, cw, ch);
					return true;
				}
			}
			winRect = r;
			return true;
		}
		return false;
	}

	/// <summary>
	/// 从 root 起，在含 (x,y) 的可见子窗中沿 Z 序钻取最深一层。
	/// GW_CHILD → 最前子窗；GW_HWNDNEXT → 下一兄弟。
	/// </summary>
	static IntPtr DeepestChildAt(IntPtr root, int x, int y, HashSet<IntPtr> skip) {
		if (root == IntPtr.Zero) return IntPtr.Zero;
		var cur = root;
		// 深度/同层兄弟数上限：Chrome/Electron 子窗极多时全量遍历会卡死 UI 数秒
		const int maxDepth = 12;
		const int maxSiblings = 48;
		for (int depth = 0; depth < maxDepth; depth++) {
			IntPtr child = GetWindow(cur, GW_CHILD);
			IntPtr hit = IntPtr.Zero;
			var n = 0;
			while (child != IntPtr.Zero && n < maxSiblings) {
				n++;
				try {
					if (skip == null || !skip.Contains(child)) {
						if (IsWindowVisible(child) && !IsIconic(child)
							&& GetWindowRect(child, out var r)) {
							var w = r.Right - r.Left;
							var h = r.Bottom - r.Top;
							if (w >= 4 && h >= 4
								&& x >= r.Left && x < r.Right
								&& y >= r.Top && y < r.Bottom) {
								// 同层 Z 序：第一个命中即为更靠前
								hit = child;
								break;
							}
						}
					}
				}
				catch { }
				child = GetWindow(child, GW_HWNDNEXT);
			}
			if (hit == IntPtr.Zero)
				return cur;
			cur = hit;
		}
		return cur;
	}

	/// <summary>虚拟窗口矩形 → 本屏底图像素矩形（与 mon 相交后映射）。</summary>
	bool mapwintodesk(System.Drawing.Rectangle win, out int left, out int top, out int pw, out int ph) {
		left = top = pw = ph = 0;
		var mon = new System.Drawing.Rectangle(monL, monT, monBoundW, monBoundH);
		var inter = System.Drawing.Rectangle.Intersect(win, mon);
		if (inter.Width < 4 || inter.Height < 4) return false;
		// Bounds 像素 → desk 像素
		left = (int)Math.Floor((inter.Left - monL) * (double)deskW / monBoundW);
		top = (int)Math.Floor((inter.Top - monT) * (double)deskH / monBoundH);
		var right = (int)Math.Ceiling((inter.Right - monL) * (double)deskW / monBoundW);
		var bottom = (int)Math.Ceiling((inter.Bottom - monT) * (double)deskH / monBoundH);
		left = Compat.Clamp(left, 0, deskW - 1);
		top = Compat.Clamp(top, 0, deskH - 1);
		right = Compat.Clamp(right, left + 1, deskW);
		bottom = Compat.Clamp(bottom, top + 1, deskH);
		pw = right - left;
		ph = bottom - top;
		return pw >= 4 && ph >= 4;
	}

	void updatehoverui(int cx, int cy, int lx, int ly, Point canvas) {
		lastCursorX = cx;
		lastCursorY = cy;
		// 窗口高亮：绿框 + 遮罩挖空（目标保持原色，周围变暗）
		hasHoverWin = false;
		if (tryfindwindowat(cx, cy, out var wr) && mapwintodesk(wr, out var wl, out var wt, out var ww, out var wh)) {
			hasHoverWin = true;
			hoverL = wl; hoverT = wt; hoverW = ww; hoverH = wh;
			hoverVirtL = wr.Left; hoverVirtT = wr.Top; hoverVirtW = wr.Width; hoverVirtH = wr.Height;
			var (ox, oy, ow, oh) = localtooverlay(wl, wt, ww, wh);
			Canvas.SetLeft(rwin, ox);
			Canvas.SetTop(rwin, oy);
			rwin.Width = ow;
			rwin.Height = oh;
			rwin.Visibility = Visibility.Visible;
			// EvenOdd 挖空：目标窗口不暗
			if (!regionDrag)
				updatemask(ox, oy, ow, oh);
			lbcap.Text = $"单击标注 / 双击完成 {ww}×{wh} · 拖拽框选 · Esc 取消";
		}
		else {
			rwin.Visibility = Visibility.Collapsed;
			if (!regionDrag)
				updatemask(0, 0, 0, 0);
			lbcap.Text = "单击标注 · 双击完成 · 拖拽框选 · Ctrl+C 复制色值 · Esc 取消";
		}
		// 放大镜 + 色值
		updatemagnifier(lx, ly, canvas);
	}

	void updatemagnifier(int lx, int ly, Point canvas) {
		if (desktopBmp == null || phase != Phase.Select) {
			bmag.Visibility = Visibility.Collapsed;
			return;
		}
		try {
			// 采样色
			var c = samplepixel(lx, ly);
			lastColorHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
			lbmagxy.Text = $"{lastCursorX}, {lastCursorY}";
			lbmagcolor.Text = lastColorHex;
			bmagswatch.Background = new SolidColorBrush(c);

			// 源矩形（以光标为中心）
			var half = MAG_SRC / 2;
			var sx = Compat.Clamp(lx - half, 0, Math.Max(0, deskW - MAG_SRC));
			var sy = Compat.Clamp(ly - half, 0, Math.Max(0, deskH - MAG_SRC));
			var sw = Math.Min(MAG_SRC, deskW - sx);
			var sh = Math.Min(MAG_SRC, deskH - sy);
			if (sw < 1 || sh < 1) {
				bmag.Visibility = Visibility.Collapsed;
				return;
			}
			// 原点未变则复用 Source，只挪位置（避免每帧 CPU 像素放大）
			if (sx != lastMagSx || sy != lastMagSy || imgmag.Source == null) {
				lastMagSx = sx;
				lastMagSy = sy;
				// CroppedBitmap + 控件最近邻拉伸，比逐像素 buildpixelmag 轻一个数量级
				var crop = new CroppedBitmap(desktopBmp, new Int32Rect(sx, sy, sw, sh));
				if (crop.CanFreeze) crop.Freeze();
				imgmag.Source = crop;
				imgmag.Width = sw * MAG_SCALE;
				imgmag.Height = sh * MAG_SCALE;
				imgmag.Stretch = Stretch.Fill;
			}

			bmag.Visibility = Visibility.Visible;
			// 勿每帧 UpdateLayout：首次用估算尺寸
			var mw = bmag.ActualWidth > 1 ? bmag.ActualWidth : 240;
			var mh = bmag.ActualHeight > 1 ? bmag.ActualHeight : 300;
			var (cw, ch) = clientsize();
			var mx = canvas.X + 28;
			var my = canvas.Y + 28;
			if (mx + mw > cw - 8) mx = canvas.X - mw - 28;
			if (my + mh > ch - 8) my = canvas.Y - mh - 28;
			if (mx < 4) mx = 4;
			if (my < 4) my = 4;
			Canvas.SetLeft(bmag, mx);
			Canvas.SetTop(bmag, my);
		}
		catch {
			bmag.Visibility = Visibility.Collapsed;
		}
	}

	Color samplepixel(int lx, int ly) {
		try {
			lx = Compat.Clamp(lx, 0, deskW - 1);
			ly = Compat.Clamp(ly, 0, deskH - 1);
			BitmapSource src = desktopBmp;
			if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Bgr32
				&& src.Format != PixelFormats.Pbgra32)
				src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
			var buf = new byte[4];
			src.CopyPixels(new Int32Rect(lx, ly, 1, 1), buf, 4, 0);
			// Bgra
			return Color.FromRgb(buf[2], buf[1], buf[0]);
		}
		catch {
			return Colors.Black;
		}
	}

	void copycolor() {
		try {
			System.Windows.Clipboard.SetText(lastColorHex ?? "#000000");
			lbcap.Text = $"已复制 {lastColorHex}";
		}
		catch (Exception ex) {
			CaptureLog.Ex("copycolor", ex);
		}
	}

	/// <summary>
	/// 光标 → 底图像素坐标（0..deskW/H）。
	/// 虚拟 Bounds（monBound*）与底图（desk*）可能不同：按比例映射，禁止强行对齐导致裁切错位。
	/// </summary>
	bool trylocal(out int lx, out int ly, Point canvasFallback) {
		if (trycursor(out var cx, out var cy)) {
			// 离开本屏 Bounds 时用画布
			if (cx < monL || cx >= monL + monBoundW || cy < monT || cy >= monT + monBoundH) {
				canvaslocal(canvasFallback, out lx, out ly);
				return true;
			}
			// Bounds 相对 → 底图像素
			lx = Compat.Clamp((int)Math.Floor((cx - monL) * (double)deskW / monBoundW), 0, deskW - 1);
			ly = Compat.Clamp((int)Math.Floor((cy - monT) * (double)deskH / monBoundH), 0, deskH - 1);
			return true;
		}
		canvaslocal(canvasFallback, out lx, out ly);
		return true;
	}

	void canvaslocal(Point p, out int lx, out int ly) {
		var (cw, ch) = clientsize();
		lx = Compat.Clamp((int)Math.Floor(p.X / cw * deskW), 0, deskW - 1);
		ly = Compat.Clamp((int)Math.Floor(p.Y / ch * deskH), 0, deskH - 1);
	}

	static bool trycursor(out int x, out int y) {
		if (GetCursorPos(out var p)) {
			x = p.X;
			y = p.Y;
			return true;
		}
		x = y = 0;
		return false;
	}

	(double w, double h) clientsize() {
		var w = ActualWidth > 1 ? ActualWidth : Width;
		var h = ActualHeight > 1 ? ActualHeight : Height;
		// proot 铺满窗口时优先用其实际尺寸
		if (proot != null && proot.ActualWidth > 1) w = proot.ActualWidth;
		if (proot != null && proot.ActualHeight > 1) h = proot.ActualHeight;
		return (Math.Max(1.0, w), Math.Max(1.0, h));
	}

	void applyselui(int x1, int y1, int x2, int y2) {
		var left = Math.Min(x1, x2);
		var top = Math.Min(y1, y2);
		var pw = Math.Max(0, Math.Abs(x2 - x1));
		var ph = Math.Max(0, Math.Abs(y2 - y1));
		var (ox, oy, ow, oh) = localtooverlay(left, top, pw, ph);
		Canvas.SetLeft(rsel, ox);
		Canvas.SetTop(rsel, oy);
		rsel.Width = ow;
		rsel.Height = oh;
		updatemask(ox, oy, ow, oh);
	}

	/// <summary>虚拟屏像素矩形 → 本屏画布 DIP（可部分在屏外）。</summary>
	(double x, double y, double w, double h) virttooverlay(int vl, int vt, int vw, int vh) {
		var (cw, ch) = clientsize();
		var x = (vl - monL) / (double)monBoundW * cw;
		var y = (vt - monT) / (double)monBoundH * ch;
		var w = Math.Max(1, vw / (double)monBoundW * cw);
		var h = Math.Max(1, vh / (double)monBoundH * ch);
		return (x, y, w, h);
	}

	/// <summary>本屏局部物理像素 → 画布 DIP（底图与窗口 1:1 对应）。</summary>
	(double x, double y, double w, double h) localtooverlay(int left, int top, int pw, int ph) {
		var (cw, ch) = clientsize();
		var x = left / (double)deskW * cw;
		var y = top / (double)deskH * ch;
		var w = pw / (double)deskW * cw;
		var h = ph / (double)deskH * ch;
		return (x, y, Math.Max(0, w), Math.Max(0, h));
	}

	/// <summary>
	/// 按底图像素裁切。返回冻结 CroppedBitmap（几乎零拷贝）；
	/// 拖选区会频繁调用，禁止 WriteableBitmap 全图拷贝。
	/// </summary>
	BitmapSource croplocal(int left, int top, int pw, int ph) {
		if (desktopBmp == null || deskW < 1 || deskH < 1)
			throw new InvalidOperationException("desktopBmp missing");
		var rect = ScreenDpi.ClampToDesk(left, top, Math.Max(1, pw), Math.Max(1, ph), deskW, deskH);
		var crop = new CroppedBitmap(desktopBmp, rect);
		if (crop.CanFreeze) crop.Freeze();
		return crop;
	}

	static BitmapSource capturephys(int left, int top, int pw, int ph) =>
		captureRectDesktop(left, top, pw, ph, "capturephys");

	/// <summary>截取指定显示器。out overlayRect：遮罩窗口应用的桌面矩形（与帧对齐）。</summary>
	public static BitmapSource CaptureMonitor(System.Windows.Forms.Screen screen, out int pixelW, out int pixelH) {
		return CaptureMonitor(screen, out pixelW, out pixelH, out _);
	}

	/// <summary>
	/// 截取指定显示器。多策略抓取并打分选优。
	/// DXGI 成功时 overlayRect 用帧像素尺寸贴在输出原点，避免 Bounds(2560)≠纹理(1920) 导致「截不全」。
	/// </summary>
	public static BitmapSource CaptureMonitor(System.Windows.Forms.Screen screen, out int pixelW, out int pixelH,
		out System.Drawing.Rectangle overlayRect) {
		if (screen == null) throw new ArgumentNullException(nameof(screen));
		var b = screen.Bounds;
		overlayRect = b;
		var tag = $"CaptureMonitor {screen.DeviceName}";
		int capsW = 0, capsH = 0;
		try {
			var hdc = CreateDC(screen.DeviceName, null, null, IntPtr.Zero);
			if (hdc == IntPtr.Zero) hdc = CreateDC("DISPLAY", screen.DeviceName, null, IntPtr.Zero);
			if (hdc != IntPtr.Zero) {
				capsW = GetDeviceCaps(hdc, DESKTOPHORZRES);
				capsH = GetDeviceCaps(hdc, DESKTOPVERTRES);
				DeleteDC(hdc);
			}
		}
		catch { }
		CaptureLog.Info($"{tag} Bounds={b} desktopCaps={capsW}x{capsH}");

		// 0) DXGI 优先：成功即用（不再用 score 阈值刷掉暗色桌面，否则会掉进 GDI 瀑布 ~1s）
		try {
			var dx = DxgiCapture.CaptureScreenEx(screen);
			if (dx?.Image != null && dx.Image.PixelWidth >= 8 && dx.Image.PixelHeight >= 8) {
				// 轻量采样仅写日志，不拦截（DXGI 全帧比 GDI 副屏残缺更可信）
				var sc = scoreCaptureFast(dx.Image, out var det);
				CaptureLog.Info($"{tag} dxgi score={sc:0.###} {det} desk={dx.DesktopRect} {CaptureLog.Bmp(dx.Image)}");
				var ox = dx.DesktopRect.Left;
				var oy = dx.DesktopRect.Top;
				if (Math.Abs(ox - b.Left) > b.Width / 2) ox = b.Left;
				if (Math.Abs(oy - b.Top) > b.Height / 2) oy = b.Top;
				overlayRect = new System.Drawing.Rectangle(ox, oy, dx.Image.PixelWidth, dx.Image.PixelHeight);
				pixelW = dx.Image.PixelWidth;
				pixelH = dx.Image.PixelHeight;
				CaptureLog.Info($"{tag} PICK dxgi overlay={overlayRect}");
				return dx.Image;
			}
		}
		catch (Exception ex) { CaptureLog.Ex(tag + " dxgi", ex); }

		// 候选 GDI
		var cands = new List<(string name, BitmapSource bmp)>();
		void tryadd(string name, Func<BitmapSource> f) {
			try {
				var img = f();
				if (img != null && img.PixelWidth > 1)
					cands.Add((name, img));
			}
			catch (Exception ex) { CaptureLog.Ex(tag + " " + name, ex); }
		}

		if (capsW > 0 && capsH > 0)
			tryadd("deviceFull-caps", () => captureDeviceSize(screen.DeviceName, capsW, capsH, tag + " A-caps"));
		tryadd("deviceFull-bounds", () => captureDeviceSize(screen.DeviceName, b.Width, b.Height, tag + " B-bounds"));
		tryadd("desktop-bounds", () => captureRectDesktop(b.Left, b.Top, b.Width, b.Height, tag + " C-deskBounds"));
		if (capsW > 0 && capsH > 0)
			tryadd("desktop-caps", () => captureRectDesktop(b.Left, b.Top, capsW, capsH, tag + " D-deskCaps"));
		tryadd("gdi-bounds", () => grabscreenVirtual(b.Left, b.Top, b.Width, b.Height, tag + " E-gdiBounds"));
		if (capsW > 0 && capsH > 0)
			tryadd("gdi-caps", () => grabscreenVirtual(b.Left, b.Top, capsW, capsH, tag + " F-gdiCaps"));
		tryadd("dpi-unaware", () => captureScreenDpiUnaware(screen.DeviceName, tag + " G-unaware"));

		if (cands.Count == 0) {
			pixelW = Math.Max(1, b.Width);
			pixelH = Math.Max(1, b.Height);
			overlayRect = b;
			return captureRectDesktop(b.Left, b.Top, pixelW, pixelH, tag + "-last");
		}

		BitmapSource best = null;
		var bestScore = -1.0;
		string bestName = "";
		foreach (var (name, bmp) in cands) {
			var sc = scoreCapture(bmp, out var detail);
			CaptureLog.Info($"{tag} candidate {name} score={sc:0.###} {detail} {CaptureLog.Bmp(bmp)}");
			if (sc > bestScore) {
				bestScore = sc;
				best = bmp;
				bestName = name;
			}
		}

		// 仅当面积仍接近原图时才 crop（禁止 640 宽条「满分」冒充全屏）
		if (best != null && bestScore < 0.5) {
			var cropped = croptocontentbbox(best);
			if (cropped != null
				&& cropped.PixelWidth >= best.PixelWidth * 0.85
				&& cropped.PixelHeight >= best.PixelHeight * 0.85) {
				var sc2 = scoreCapture(cropped, out var d2);
				CaptureLog.Info($"{tag} croptocontent {CaptureLog.Bmp(cropped)} score={sc2:0.###} {d2}");
				if (sc2 > bestScore) {
					best = cropped;
					bestName += "+crop";
					bestScore = sc2;
				}
			}
		}

		CaptureLog.Info($"{tag} PICK {bestName} score={bestScore:0.###}");
		pixelW = best.PixelWidth;
		pixelH = best.PixelHeight;
		// GDI 帧尺寸作遮罩（1:1）
		overlayRect = new System.Drawing.Rectangle(b.Left, b.Top, pixelW, pixelH);
		return best;
	}

	/// <summary>在指定 DPI 感知上下文中按设备名匹配显示器并抓取。</summary>
	static BitmapSource captureScreenDpiUnaware(string deviceName, string tag) =>
		captureScreenDpiContext(deviceName, DpiUnaware, tag);

	static BitmapSource captureScreenDpiContext(string deviceName, IntPtr ctx, string tag) {
		BitmapSource result = null;
		Exception error = null;
		// 必须在 STA 且改线程 DPI 上下文
		var th = new Thread(() => {
			var prev = SetThreadDpiAwarenessContext(ctx);
			try {
				System.Windows.Forms.Screen hit = null;
				foreach (var s in System.Windows.Forms.Screen.AllScreens) {
					if (string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) {
						hit = s;
						break;
					}
				}
				hit ??= System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary)
					?? System.Windows.Forms.Screen.PrimaryScreen;
				if (hit == null) throw new InvalidOperationException("no screen");
				var b = hit.Bounds;
				CaptureLog.Info($"{tag} dpiCtx Bounds={b} Primary={hit.Primary}");
				// 优先 CopyFromScreen / Desktop BitBlt（该 DPI 上下文下坐标一致）
				result = captureRectDesktop(b.Left, b.Top, b.Width, b.Height, tag + "-desk");
				var sc = scoreCapture(result, out var det);
				CaptureLog.Info($"{tag} desk score={sc:0.###} {det}");
				if (sc < 0.4) {
					var gdi = grabscreenVirtual(b.Left, b.Top, b.Width, b.Height, tag + "-gdi");
					var sc2 = scoreCapture(gdi, out var det2);
					CaptureLog.Info($"{tag} gdi score={sc2:0.###} {det2}");
					if (sc2 > sc) result = gdi;
				}
			}
			catch (Exception ex) { error = ex; }
			finally {
				SetThreadDpiAwarenessContext(prev);
			}
		});
		th.SetApartmentState(ApartmentState.STA);
		th.IsBackground = true;
		th.Start();
		if (!th.Join(15000))
			throw new TimeoutException(tag + " timeout");
		if (error != null) throw error;
		return result;
	}

	/// <summary>裁掉纯黑边，得到有内容的包围盒（不拉伸）。</summary>
	static BitmapSource croptocontentbbox(BitmapSource src) {
		if (src == null) return null;
		try {
			var w = src.PixelWidth;
			var h = src.PixelHeight;
			var stride = w * 4;
			var px = new byte[stride * h];
			BitmapSource bgra = src;
			if (src.Format != PixelFormats.Bgra32)
				bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
			bgra.CopyPixels(px, stride, 0);
			int minX = w, minY = h, maxX = -1, maxY = -1;
			var step = Math.Max(1, Math.Min(w, h) / 250);
			for (int y = 0; y < h; y += step) {
				for (int x = 0; x < w; x += step) {
					var i = y * stride + x * 4;
					if (px[i] > 14 || px[i + 1] > 14 || px[i + 2] > 14) {
						if (x < minX) minX = x;
						if (y < minY) minY = y;
						if (x > maxX) maxX = x;
						if (y > maxY) maxY = y;
					}
				}
			}
			if (maxX < minX || maxY < minY) return null;
			minX = Math.Max(0, minX - 1);
			minY = Math.Max(0, minY - 1);
			maxX = Math.Min(w - 1, maxX + 1);
			maxY = Math.Min(h - 1, maxY + 1);
			var cw = maxX - minX + 1;
			var ch = maxY - minY + 1;
			if (cw >= w * 0.92 && ch >= h * 0.92) return src; // 几乎全图
			if (cw < 32 || ch < 32) return null;
			var crop = new CroppedBitmap(src, new Int32Rect(minX, minY, cw, ch));
			var wb = new WriteableBitmap(crop);
			wb.Freeze();
			return wb;
		}
		catch { return null; }
	}

	/// <summary>抓屏质量分：高非黑 + 四象限均衡；右/下全黑重罚。</summary>
	static double scoreCapture(BitmapSource src, out string detail) =>
		scoreCaptureCore(src, out detail, fast: false);

	/// <summary>稀疏采样打分（DXGI 日志用，避免整图 CopyPixels）。</summary>
	static double scoreCaptureFast(BitmapSource src, out string detail) =>
		scoreCaptureCore(src, out detail, fast: true);

	static double scoreCaptureCore(BitmapSource src, out string detail, bool fast) {
		detail = "";
		if (src == null) return -1;
		var q = sampleQuadsInternal(src, fast);
		var avg = (q[0] + q[1] + q[2] + q[3]) / 4.0;
		var minQ = Math.Min(Math.Min(q[0], q[1]), Math.Min(q[2], q[3]));
		var maxQ = Math.Max(Math.Max(q[0], q[1]), Math.Max(q[2], q[3]));
		var balance = minQ / (maxQ + 0.01);
		var score = avg * (0.35 + 0.65 * balance);
		if (src.PixelWidth < 200 || src.PixelHeight < 200) score *= 0.2;
		detail = $"avg={avg:P0} bal={balance:0.00} quads=[{q[0]:P0},{q[1]:P0},{q[2]:P0},{q[3]:P0}]";
		return score;
	}

	/// <summary>
	/// 四象限非黑比例。fast=true 时只采每象限约 8×8 点（不整图拷贝），否则步长采样整图。
	/// </summary>
	static double[] sampleQuadsInternal(BitmapSource src, bool fast = false) {
		var r = new double[4];
		var w = src.PixelWidth;
		var h = src.PixelHeight;
		if (w < 2 || h < 2) return r;
		BitmapSource bgra = src;
		if (src.Format != PixelFormats.Bgra32)
			bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

		if (fast) {
			// 每象限 8×8 单像素 CopyPixels，4K 图约 256 次 vs 整图 33MB
			var midX = w / 2;
			var midY = h / 2;
			long[] nb = new long[4], n = new long[4];
			var buf = new byte[4];
			for (int qi = 0; qi < 4; qi++) {
				var x0 = (qi & 1) == 0 ? 0 : midX;
				var y0 = (qi & 2) == 0 ? 0 : midY;
				var qw = (qi & 1) == 0 ? midX : w - midX;
				var qh = (qi & 2) == 0 ? midY : h - midY;
				if (qw < 1 || qh < 1) continue;
				const int grid = 8;
				for (int gy = 0; gy < grid; gy++) {
					for (int gx = 0; gx < grid; gx++) {
						var x = x0 + (gx * 2 + 1) * qw / (grid * 2);
						var y = y0 + (gy * 2 + 1) * qh / (grid * 2);
						if (x < 0 || x >= w || y < 0 || y >= h) continue;
						try {
							bgra.CopyPixels(new Int32Rect(x, y, 1, 1), buf, 4, 0);
						}
						catch { continue; }
						n[qi]++;
						if (buf[0] > 12 || buf[1] > 12 || buf[2] > 12) nb[qi]++;
					}
				}
			}
			for (int i = 0; i < 4; i++)
				r[i] = n[i] > 0 ? nb[i] / (double)n[i] : 0;
			return r;
		}

		var stride = w * 4;
		var px = new byte[stride * h];
		bgra.CopyPixels(px, stride, 0);
		var midXf = w / 2;
		var midYf = h / 2;
		long[] nbf = new long[4], nf = new long[4];
		var step = Math.Max(1, Math.Min(w, h) / 60);
		for (int y = 0; y < h; y += step) {
			for (int x = 0; x < w; x += step) {
				var i = y * stride + x * 4;
				var lit = px[i] > 12 || px[i + 1] > 12 || px[i + 2] > 12;
				var qi = (y < midYf ? 0 : 2) + (x < midXf ? 0 : 1);
				nf[qi]++;
				if (lit) nbf[qi]++;
			}
		}
		for (int i = 0; i < 4; i++)
			r[i] = nf[i] > 0 ? nbf[i] / (double)nf[i] : 0;
		return r;
	}

	/// <summary>兼容旧调用：按 Bounds 匹配 Screen。</summary>
	public static BitmapSource CaptureMonitor(System.Drawing.Rectangle bounds, out int pixelW, out int pixelH) {
		foreach (var s in System.Windows.Forms.Screen.AllScreens) {
			if (s.Bounds == bounds)
				return CaptureMonitor(s, out pixelW, out pixelH, out _);
		}
		System.Windows.Forms.Screen best = null;
		var bestArea = 0;
		foreach (var s in System.Windows.Forms.Screen.AllScreens) {
			var i = System.Drawing.Rectangle.Intersect(s.Bounds, bounds);
			var a = Math.Max(0, i.Width) * Math.Max(0, i.Height);
			if (a > bestArea) { bestArea = a; best = s; }
		}
		if (best != null)
			return CaptureMonitor(best, out pixelW, out pixelH, out _);
		pixelW = Math.Max(1, bounds.Width);
		pixelH = Math.Max(1, bounds.Height);
		return captureRectDesktop(bounds.Left, bounds.Top, pixelW, pixelH, "CaptureMonitor-bounds");
	}

	/// <summary>截取虚拟屏全图：各屏真实像素拼接（不拉伸）。</summary>
	public static BitmapSource CaptureVirtualScreen(out int pixelW, out int pixelH) {
		var (l, t, w, h) = ScreenDpi.VirtualScreenPixels();
		pixelW = w;
		pixelH = h;
		return captureRectDesktop(l, t, w, h, "CaptureVirtualScreen");
	}

	/// <summary>CreateDC(设备) + 指定宽高从 (0,0) BitBlt。</summary>
	static BitmapSource captureDeviceSize(string deviceName, int grabW, int grabH, string tag) {
		grabW = Math.Max(1, grabW);
		grabH = Math.Max(1, grabH);
		IntPtr hdcSrc = IntPtr.Zero;
		IntPtr hdcDest = IntPtr.Zero;
		IntPtr hBitmap = IntPtr.Zero;
		IntPtr hOld = IntPtr.Zero;
		try {
			hdcSrc = CreateDC(deviceName, null, null, IntPtr.Zero);
			if (hdcSrc == IntPtr.Zero)
				hdcSrc = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
			if (hdcSrc == IntPtr.Zero)
				throw new InvalidOperationException("CreateDC failed");

			CaptureLog.Info($"{tag} CreateDC size={grabW}x{grabH}");
			hdcDest = CreateCompatibleDC(hdcSrc);
			hBitmap = CreateCompatibleBitmap(hdcSrc, grabW, grabH);
			if (hdcDest == IntPtr.Zero || hBitmap == IntPtr.Zero)
				throw new InvalidOperationException("CreateCompatible* failed");

			hOld = SelectObject(hdcDest, hBitmap);
			if (!BitBlt(hdcDest, 0, 0, grabW, grabH, hdcSrc, 0, 0, SRCCOPY | CAPTUREBLT)) {
				if (!BitBlt(hdcDest, 0, 0, grabW, grabH, hdcSrc, 0, 0, SRCCOPY))
					throw new InvalidOperationException("BitBlt failed");
			}
			SelectObject(hdcDest, hOld);
			hOld = IntPtr.Zero;

			System.Drawing.Bitmap argb;
			using (var gdi = System.Drawing.Image.FromHbitmap(hBitmap)) {
				argb = new System.Drawing.Bitmap(grabW, grabH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
				using (var g = System.Drawing.Graphics.FromImage(argb))
					g.DrawImageUnscaled(gdi, 0, 0);
			}
			var src = tobmp(argb, out var nb);
			CaptureLog.Info($"{tag} ok nonBlack~{nb:P0} {CaptureLog.Bmp(src)}");
			return src;
		}
		finally {
			if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero)
				SelectObject(hdcDest, hOld);
			if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
			if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
			if (hdcSrc != IntPtr.Zero) DeleteDC(hdcSrc);
		}
	}

	/// <summary>
	/// ShareX 风格：GetWindowDC(Desktop) + 虚拟屏绝对坐标 BitBlt。
	/// 参考 tmp/ref-sharex/.../Screenshot.cs（主屏可靠；副屏 Bounds≠真实像素时可能有黑边）。
	/// </summary>
	static BitmapSource captureRectDesktop(int x, int y, int w, int h, string tag) {
		w = Math.Max(1, w);
		h = Math.Max(1, h);
		IntPtr hdcSrc = IntPtr.Zero;
		IntPtr hdcDest = IntPtr.Zero;
		IntPtr hBitmap = IntPtr.Zero;
		IntPtr hOld = IntPtr.Zero;
		try {
			var desktop = GetDesktopWindow();
			hdcSrc = GetWindowDC(desktop);
			if (hdcSrc == IntPtr.Zero)
				throw new InvalidOperationException("GetWindowDC failed");

			hdcDest = CreateCompatibleDC(hdcSrc);
			hBitmap = CreateCompatibleBitmap(hdcSrc, w, h);
			if (hdcDest == IntPtr.Zero || hBitmap == IntPtr.Zero)
				throw new InvalidOperationException("CreateCompatible* failed");

			hOld = SelectObject(hdcDest, hBitmap);
			if (!BitBlt(hdcDest, 0, 0, w, h, hdcSrc, x, y, SRCCOPY | CAPTUREBLT)) {
				if (!BitBlt(hdcDest, 0, 0, w, h, hdcSrc, x, y, SRCCOPY))
					throw new InvalidOperationException("BitBlt failed");
			}
			SelectObject(hdcDest, hOld);
			hOld = IntPtr.Zero;

			System.Drawing.Bitmap argb;
			using (var gdi = System.Drawing.Image.FromHbitmap(hBitmap)) {
				argb = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
				using (var g = System.Drawing.Graphics.FromImage(argb))
					g.DrawImageUnscaled(gdi, 0, 0);
			}
			var src = tobmp(argb, out var nb);
			CaptureLog.Info($"{tag} desktop-DC rect=({x},{y},{w},{h}) nonBlack~{nb:P0} {CaptureLog.Bmp(src)}");
			return src;
		}
		catch (Exception ex) {
			CaptureLog.Ex(tag, ex);
			return grabscreenVirtual(x, y, w, h, tag + "-gdi+");
		}
		finally {
			if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero)
				SelectObject(hdcDest, hOld);
			if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
			if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
			if (hdcSrc != IntPtr.Zero) ReleaseDC(GetDesktopWindow(), hdcSrc);
		}
	}

	/// <summary>回退：CopyFromScreen。</summary>
	static BitmapSource grabscreenVirtual(int left, int top, int pw, int ph, string tag) {
		pw = Math.Max(1, pw);
		ph = Math.Max(1, ph);
		try {
			var bmp = new System.Drawing.Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (var g = System.Drawing.Graphics.FromImage(bmp)) {
				g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(pw, ph),
					System.Drawing.CopyPixelOperation.SourceCopy
					| System.Drawing.CopyPixelOperation.CaptureBlt);
			}
			var src = tobmp(bmp, out var nonBlack);
			CaptureLog.Info($"{tag} CopyFromScreen nonBlack~{nonBlack:P0} {CaptureLog.Bmp(src)}");
			return src;
		}
		catch (Exception ex) {
			CaptureLog.Ex(tag, ex);
			var last = new System.Drawing.Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (var g = System.Drawing.Graphics.FromImage(last)) {
				g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(pw, ph),
					System.Drawing.CopyPixelOperation.SourceCopy);
			}
			return tobmp(last, out _);
		}
	}

	/// <summary>在窗口布局就绪后进入画板主屏 / 副屏。</summary>
	static void scheduleboard(CaptureOverlay w, bool backdrop) {
		if (w == null) return;
		void go() {
			try {
				if (backdrop) w.enterboardbackdrop();
				else w.enterboard();
			}
			catch (Exception ex) {
				CaptureLog.Ex(backdrop ? "enterboardbackdrop" : "enterboard", ex);
				try { w.session?.Cancel(); } catch { }
			}
		}
		if (w.IsLoaded)
			w.Dispatcher.BeginInvoke((Action)go,
				System.Windows.Threading.DispatcherPriority.Loaded);
		else {
			RoutedEventHandler h = null;
			h = (_, __) => {
				w.Loaded -= h;
				w.Dispatcher.BeginInvoke((Action)go,
					System.Windows.Threading.DispatcherPriority.Loaded);
			};
			w.Loaded += h;
		}
	}

	/// <summary>屏幕画板：本屏全幅进入标注（无框选、无缩放手柄）。</summary>
	void enterboard() {
		if (session != null && session.Finishing) return;
		boardMode = true;
		boardBackdrop = false;
		cropL = 0;
		cropT = 0;
		cropW = deskW;
		cropH = deskH;
		CaptureLog.Info($"enterboard mon=({monL},{monT}) desk={deskW}x{deskH}");
		enterannotate();
		CaptureLog.Info($"enterboard done phase={phase} board={boardMode} barVis={bbar.Visibility} pane={bpane.Visibility}");
		try { Activate(); } catch { }
		try { Focus(); } catch { }
		try { proot.Focus(); } catch { }
		try { Keyboard.Focus(proot); } catch { }
	}

	/// <summary>画板副屏：只显示冻结底图，Esc 可取消整次会话。</summary>
	void enterboardbackdrop() {
		if (session != null && session.Finishing) return;
		boardBackdrop = true;
		boardMode = false;
		phase = Phase.Select;
		Cursor = Cursors.Arrow;
		rsel.Visibility = Visibility.Collapsed;
		rwin.Visibility = Visibility.Collapsed;
		bmag.Visibility = Visibility.Collapsed;
		bhint.Visibility = Visibility.Collapsed;
		bbar.Visibility = Visibility.Collapsed;
		bpane.Visibility = Visibility.Collapsed;
		phandles.Visibility = Visibility.Collapsed;
		hasHoverWin = false;
		// 去掉遮罩，完整显示冻结画面
		try {
			var (W, H) = clientsize();
			updatemask(0, 0, W, H);
		}
		catch {
			updatemask(0, 0, Width, Height);
		}
		CaptureLog.Info($"enterboardbackdrop mon=({monL},{monT})");
	}

	/// <summary>标注副屏：只显示冻结底图 + 选区挖空，无工具。</summary>
	void enterannotateguest() {
		if (boardMode) return;
		annotateGuest = true;
		boardBackdrop = false;
		phase = Phase.Annotate;
		Cursor = Cursors.Arrow;
		rsel.Visibility = Visibility.Collapsed;
		rwin.Visibility = Visibility.Collapsed;
		bmag.Visibility = Visibility.Collapsed;
		bhint.Visibility = Visibility.Collapsed;
		bbar.Visibility = Visibility.Collapsed;
		bpane.Visibility = Visibility.Collapsed;
		phandles.Visibility = Visibility.Collapsed;
		hasHoverWin = false;
		adjDrag = false;
		applyguestmask();
		CaptureLog.Info($"enterannotateguest mon=({monL},{monT})");
	}

	/// <summary>副屏：按会话虚拟选区更新遮罩挖空。</summary>
	void applyguestmask() {
		if (session == null || !session.InAnnotate) return;
		var mon = new System.Drawing.Rectangle(monL, monT, monBoundW, monBoundH);
		var ann = new System.Drawing.Rectangle(
			session.AnnVL, session.AnnVT, Math.Max(1, session.AnnVW), Math.Max(1, session.AnnVH));
		var inter = System.Drawing.Rectangle.Intersect(mon, ann);
		if (inter.Width < 1 || inter.Height < 1) {
			// 选区不在本屏：整屏遮罩
			updatemask(0, 0, 0, 0);
			rsel.Visibility = Visibility.Collapsed;
			return;
		}
		// Bounds 相交 → 本屏 DIP（与底图 1:1 用 bounds 映射，与 applyvirtualsel 一致）
		if (!tryvirtualtodesk(inter.Left, inter.Top, inter.Width, inter.Height,
				out var dl, out var dt, out var dw, out var dh)) {
			updatemask(0, 0, 0, 0);
			return;
		}
		var (ox, oy, ow, oh) = localtooverlay(dl, dt, dw, dh);
		Canvas.SetLeft(rsel, ox);
		Canvas.SetTop(rsel, oy);
		rsel.Width = Math.Max(0, ow);
		rsel.Height = Math.Max(0, oh);
		rsel.Visibility = Visibility.Visible;
		rsel.Stroke = new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60));
		updatemask(ox, oy, ow, oh);
	}

	/// <summary>多屏标注：每屏刷新（宿主画布 / guest 遮罩）。</summary>
	internal void refreshannotateui(bool clearStrokes) {
		if (boardMode) return;
		if (annotateGuest || (session != null && session.AnnotateHost != this && session.InAnnotate)) {
			annotateGuest = true;
			bpane.Visibility = Visibility.Collapsed;
			bbar.Visibility = Visibility.Collapsed;
			phandles.Visibility = Visibility.Collapsed;
			applyguestmask();
			return;
		}
		// 宿主
		annotateGuest = false;
		applyregionui(clearStrokes);
	}

	void enterannotate() {
		phase = Phase.Annotate;
		annotateGuest = false;
		Cursor = Cursors.Arrow;
		rsel.Visibility = Visibility.Collapsed;
		rwin.Visibility = Visibility.Collapsed;
		bmag.Visibility = Visibility.Collapsed;
		bhint.Visibility = Visibility.Collapsed;
		hasHoverWin = false;
		adjDrag = false;
		adjHit = AdjHit.None;
		// 选区内放截图 + 绿框 + 手柄
		applyregionui(clearStrokes: false);
		bpane.Visibility = Visibility.Visible;
		bbar.Visibility = Visibility.Visible;
		if (boardMode) {
			// 全屏画板：无绿框、无缩放手柄、无暗角遮罩
			bpane.BorderThickness = new Thickness(0);
			phandles.Visibility = Visibility.Collapsed;
			try {
				var (W, H) = clientsize();
				updatemask(0, 0, W, H);
			}
			catch {
				updatemask(0, 0, Width, Height);
			}
			// 默认画笔；工具条贴底居中
			settool(Tool.Line);
			lbcap.Text = "屏幕画板 · 画笔/框/箭头/文字 · Ctrl+Z 撤销 · Enter 完成 · Esc 取消";
			placebarboard();
		}
		else {
			bpane.BorderThickness = new Thickness(2);
			ensurehandles();
			placehandles();
			phandles.Visibility = Visibility.Visible;
			// 默认画矩形；移动/缩放只在边缘热区与 8 点手柄
			settool(Tool.Rect);
			lbcap.Text = session != null && session.Windows.Count > 1
				? "可跨屏拖动选区 · 角/边缩放 · 双击/回车完成"
				: "默认矩形 · 边缘拖动移动 · 角/边缩放 · 双击/回车完成";
		}
		// 刷新副屏遮罩
		if (session != null && session.InAnnotate && !boardMode) {
			foreach (var w in session.Windows) {
				if (w != this && w.annotateGuest)
					w.applyguestmask();
			}
		}
	}

	/// <summary>画板工具条：底部居中（略上移避任务栏）。</summary>
	void placebarboard() {
		bbar.UpdateLayout();
		var barW = bbar.ActualWidth > 1 ? bbar.ActualWidth : 520;
		var barH = bbar.ActualHeight > 1 ? bbar.ActualHeight : 48;
		var x = (Width - barW) / 2;
		var y = Height - barH - 28;
		if (y < 8) y = 8;
		if (x < 8) x = 8;
		if (x + barW > Width - 8)
			x = Math.Max(8, Width - barW - 8);
		Canvas.SetLeft(bbar, x);
		Canvas.SetTop(bbar, y);
	}

	/// <summary>是否多屏标注选区（虚拟坐标）。</summary>
	bool usemultimonann() =>
		!boardMode && session != null && session.InAnnotate && session.Windows.Count > 1;

	/// <summary>按 crop*/虚拟选区刷新选区 UI。优先视口偏移整屏图，仅跨屏拼接才裁切。</summary>
	void applyregionui(bool clearStrokes) {
		if (annotateGuest) {
			applyguestmask();
			return;
		}
		if (!syncregiongeom()) return;
		if (!applyregioncontent()) return;
		layoutregionchrome(clearStrokes);
	}

	/// <summary>只同步 crop/sel 几何（不改图源）。</summary>
	bool syncregiongeom() {
		if (usemultimonann()) {
			session.AnnVW = Math.Max(MIN_CROP, session.AnnVW);
			session.AnnVH = Math.Max(MIN_CROP, session.AnnVH);
			session.VirtualBounds(out var vL, out var vT, out var vW, out var vH);
			session.AnnVL = Compat.Clamp(session.AnnVL, vL, vL + Math.Max(0, vW - session.AnnVW));
			session.AnnVT = Compat.Clamp(session.AnnVT, vT, vT + Math.Max(0, vH - session.AnnVH));
			if (session.AnnVL + session.AnnVW > vL + vW)
				session.AnnVW = Math.Max(MIN_CROP, vL + vW - session.AnnVL);
			if (session.AnnVT + session.AnnVH > vT + vH)
				session.AnnVH = Math.Max(MIN_CROP, vT + vH - session.AnnVT);
			if (tryvirtualtodesk(session.AnnVL, session.AnnVT, session.AnnVW, session.AnnVH,
					out var dl, out var dt, out var dw, out var dh)) {
				cropL = dl;
				cropT = dt;
				cropW = dw;
				cropH = dh;
			}
			// 虚拟像素整数 → 设备像素对齐的 DIP，遮罩与绿框同源无抖
			selsnapfromdeskvirtual(session.AnnVL, session.AnnVT, session.AnnVW, session.AnnVH);
			SelectedDip = new Rect(selX, selY, selW, selH);
			return true;
		}
		cropW = Math.Max(MIN_CROP, cropW);
		cropH = Math.Max(MIN_CROP, cropH);
		cropL = Compat.Clamp(cropL, 0, Math.Max(0, deskW - cropW));
		cropT = Compat.Clamp(cropT, 0, Math.Max(0, deskH - cropH));
		if (cropL + cropW > deskW) cropW = deskW - cropL;
		if (cropT + cropH > deskH) cropH = deskH - cropT;
		cropW = Math.Max(MIN_CROP, cropW);
		cropH = Math.Max(MIN_CROP, cropH);
		// 底图像素整数 → 设备像素对齐 DIP
		selsnapfromdesk(cropL, cropT, cropW, cropH);
		SelectedDip = new Rect(selX, selY, selW, selH);
		return true;
	}

	/// <summary>本屏 desk 像素矩形 → 与 imgDesktop 1:1 的 DIP，并吸附到设备像素。</summary>
	void selsnapfromdesk(int left, int top, int pw, int ph) {
		var (cw, ch) = clientsize();
		// 与 imgDesktop 铺满方式一致：按 client 比例，先算设备像素再回 DIP
		var px = devpixeldip();
		var x0 = (int)Math.Round(left * (double)cw / deskW * px);
		var y0 = (int)Math.Round(top * (double)ch / deskH * px);
		var x1 = (int)Math.Round((left + pw) * (double)cw / deskW * px);
		var y1 = (int)Math.Round((top + ph) * (double)ch / deskH * px);
		if (x1 <= x0) x1 = x0 + 1;
		if (y1 <= y0) y1 = y0 + 1;
		selX = x0 / px;
		selY = y0 / px;
		selW = (x1 - x0) / px;
		selH = (y1 - y0) / px;
	}

	/// <summary>虚拟像素矩形 → 本屏 DIP（设备像素对齐）。</summary>
	void selsnapfromdeskvirtual(int vl, int vt, int vw, int vh) {
		var (cw, ch) = clientsize();
		var px = devpixeldip();
		// 虚拟 → 本屏 Bounds 相对 → client DIP，与 virttooverlay 同源但取整到设备像素
		var x0 = (int)Math.Round((vl - monL) * (double)cw / monBoundW * px);
		var y0 = (int)Math.Round((vt - monT) * (double)ch / monBoundH * px);
		var x1 = (int)Math.Round((vl + vw - monL) * (double)cw / monBoundW * px);
		var y1 = (int)Math.Round((vt + vh - monT) * (double)ch / monBoundH * px);
		if (x1 <= x0) x1 = x0 + 1;
		if (y1 <= y0) y1 = y0 + 1;
		selX = x0 / px;
		selY = y0 / px;
		selW = (x1 - x0) / px;
		selH = (y1 - y0) / px;
	}

	double devpixeldip() {
		try {
			var d = VisualTreeHelper.GetDpi(this).PixelsPerDip;
			if (d > 0.1) return d;
		}
		catch { }
		return Math.Max(0.25, sysScale);
	}

	/// <summary>选区完全落在单屏时可走视口（整屏图偏移）；跨屏需拼接裁切。</summary>
	bool canviewport() {
		if (boardMode) return false;
		if (!usemultimonann()) return true;
		return !cropvirtualisslow(session.AnnVL, session.AnnVT, session.AnnVW, session.AnnVH);
	}

	/// <summary>设置框内底图：视口偏移或跨屏裁切。</summary>
	bool applyregioncontent() {
		if (boardMode) {
			// 画板：整屏即选区，直接贴冻结图
			viewPortUi = false;
			try {
				shot = EnsureFrozen(desktopBmp);
			}
			catch (Exception ex) {
				CaptureLog.Ex("applyregioncontent board", ex);
				return false;
			}
			imgW = shot.PixelWidth;
			imgH = shot.PixelHeight;
			imgshot.Source = shot;
			imgshot.Margin = new Thickness(0);
			imgshot.Width = selW;
			imgshot.Height = selH;
			return true;
		}
		if (canviewport()) {
			// 视口：不叠第二层图，遮罩挖空直接露 imgDesktop（与底图同一层，无亚像素错位）
			viewPortUi = true;
			shot = null;
			imgW = Math.Max(1, cropW);
			imgH = Math.Max(1, cropH);
			imgshot.Source = null;
			imgshot.Visibility = Visibility.Collapsed;
			return true;
		}
		// 跨屏拼接：裁切图填入
		viewPortUi = false;
		try {
			shot = EnsureFrozen(session.CropVirtual(
				session.AnnVL, session.AnnVT, session.AnnVW, session.AnnVH));
		}
		catch (Exception ex) {
			CaptureLog.Ex("applyregioncontent CropVirtual", ex);
			return false;
		}
		imgW = shot.PixelWidth;
		imgH = shot.PixelHeight;
		imgshot.Visibility = Visibility.Visible;
		imgshot.Source = shot;
		imgshot.Margin = new Thickness(0);
		imgshot.Width = selW;
		imgshot.Height = selH;
		return true;
	}

	/// <summary>绿框 / 遮罩 / 手柄 / 工具条布局。</summary>
	void layoutregionchrome(bool clearStrokes) {
		var bd = boardMode ? 0.0 : 2.0;
		Canvas.SetLeft(bpane, selX - bd);
		Canvas.SetTop(bpane, selY - bd);
		bpane.Width = selW + bd * 2;
		bpane.Height = selH + bd * 2;
		bpane.Visibility = Visibility.Visible;
		pstage.Width = selW;
		pstage.Height = selH;
		pdraw.Width = selW;
		pdraw.Height = selH;
		if (boardMode) {
			try {
				var (cw, ch) = clientsize();
				updatemask(0, 0, cw, ch);
			}
			catch { updatemask(0, 0, Width, Height); }
		}
		else
			updatemask(selX, selY, selW, selH);

		if (clearStrokes) {
			pdraw.Children.Clear();
			strokes.Clear();
			draft = null;
			drawing = false;
			editHost = null;
			selText = null;
		}
		// 拖动中不 UpdateLayout 工具条，避免布局取整造成框/遮罩微抖
		if (!adjDrag) {
			if (boardMode)
				placebarboard();
			else {
				bbar.Visibility = Visibility.Visible;
				placebar();
			}
		}
		else if (!boardMode)
			bbar.Visibility = Visibility.Visible;
		if (phandles.Visibility == Visibility.Visible)
			placehandles();
		// 导出前再 materialize；预览阶段 ResultImage 可空
		if (shot != null)
			ResultImage = shot;

		if (usemultimonann()) {
			foreach (var w in session.Windows) {
				if (w != this)
					w.applyguestmask();
			}
		}
	}

	/// <summary>导出时再裁切选区像素（视口模式预览不持有 shot）。</summary>
	BitmapSource materializeshot() {
		if (shot != null) return shot;
		if (usemultimonann()) {
			return EnsureFrozen(session.CropVirtual(
				session.AnnVL, session.AnnVT, session.AnnVW, session.AnnVH));
		}
		return EnsureFrozen(croplocal(cropL, cropT, cropW, cropH));
	}

	void placebar() {
		if (boardMode) {
			placebarboard();
			return;
		}
		bbar.UpdateLayout();
		var barW = bbar.ActualWidth > 1 ? bbar.ActualWidth : 520;
		var barH = bbar.ActualHeight > 1 ? bbar.ActualHeight : 48;
		// 优先选区下方居中（微信式）；手柄占一点空间
		var x = selX + (selW - barW) / 2;
		var y = selY + selH + 14;
		if (y + barH > Height - 8)
			y = selY - barH - 14;
		if (y < 4) y = 4;
		if (x + barW > Width - 8)
			x = Math.Max(4, Width - barW - 8);
		if (x < 4) x = 4;
		Canvas.SetLeft(bbar, x);
		Canvas.SetTop(bbar, y);
	}

	bool isoverbar(Point p) {
		if (bbar.Visibility != Visibility.Visible) return false;
		var bx = Canvas.GetLeft(bbar);
		var by = Canvas.GetTop(bbar);
		var bw = bbar.ActualWidth > 1 ? bbar.ActualWidth : 520;
		var bh = bbar.ActualHeight > 1 ? bbar.ActualHeight : 48;
		return p.X >= bx && p.X <= bx + bw
			&& p.Y >= by && p.Y <= by + bh;
	}

	// ───────── 选区移动 / 缩放（微信式） ─────────

	void ensurehandles() {
		if (handles != null) return;
		handles = new WpfRectangle[8];
		var stroke = new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60));
		stroke.Freeze();
		for (var i = 0; i < 8; i++) {
			var hit = handlehit(i);
			var r = new WpfRectangle {
				Width = HANDLE_SZ,
				Height = HANDLE_SZ,
				Fill = Brushes.White,
				Stroke = stroke,
				StrokeThickness = 1.5,
				Cursor = cursorfor(hit),
				Tag = hit,
			};
			r.MouseLeftButtonDown += onhandledown;
			phandles.Children.Add(r);
			handles[i] = r;
		}
	}

	static AdjHit handlehit(int i) => i switch {
		0 => AdjHit.NW,
		1 => AdjHit.N,
		2 => AdjHit.NE,
		3 => AdjHit.E,
		4 => AdjHit.SE,
		5 => AdjHit.S,
		6 => AdjHit.SW,
		7 => AdjHit.W,
		_ => AdjHit.None,
	};

	static Cursor cursorfor(AdjHit hit) => hit switch {
		AdjHit.N or AdjHit.S => Cursors.SizeNS,
		AdjHit.E or AdjHit.W => Cursors.SizeWE,
		AdjHit.NE or AdjHit.SW => Cursors.SizeNESW,
		AdjHit.NW or AdjHit.SE => Cursors.SizeNWSE,
		AdjHit.Move => Cursors.SizeAll,
		_ => Cursors.Arrow,
	};

	void placehandles() {
		if (handles == null) return;
		var half = HANDLE_SZ / 2;
		var l = selX;
		var t = selY;
		var r = selX + selW;
		var b = selY + selH;
		var cx = selX + selW / 2;
		var cy = selY + selH / 2;
		// NW N NE E SE S SW W
		void put(int i, double x, double y) {
			Canvas.SetLeft(handles[i], x - half);
			Canvas.SetTop(handles[i], y - half);
		}
		put(0, l, t);
		put(1, cx, t);
		put(2, r, t);
		put(3, r, cy);
		put(4, r, b);
		put(5, cx, b);
		put(6, l, b);
		put(7, l, cy);
	}

	void onhandledown(object sender, MouseButtonEventArgs e) {
		if (phase != Phase.Annotate) return;
		if (sender is not FrameworkElement fe || fe.Tag is not AdjHit hit) return;
		startadj(hit, e.GetPosition(proot));
		e.Handled = true;
	}

	void startadj(AdjHit hit, Point prootPos) {
		if (phase != Phase.Annotate || hit == AdjHit.None) return;
		if (boardMode || annotateGuest) return; // 画板/副屏不可缩放/移动选区
		// 正在画图形时不抢
		if (drawing) return;
		// 仅宿主可调选区
		if (session != null && session.InAnnotate && session.AnnotateHost != null && session.AnnotateHost != this)
			return;
		adjHit = hit;
		adjDrag = true;
		canvaslocal(prootPos, out adjStartLX, out adjStartLY);
		adj0L = cropL;
		adj0T = cropT;
		adj0W = cropW;
		adj0H = cropH;
		// 跨屏：记录虚拟选区与光标
		if (usemultimonann()) {
			adj0VL = session.AnnVL;
			adj0VT = session.AnnVT;
			adj0VW = session.AnnVW;
			adj0VH = session.AnnVH;
			if (!trycursor(out adjStartVX, out adjStartVY)) {
				// 回退：本屏 desk → 虚拟
				adjStartVX = monL + (int)Math.Round(adjStartLX * (double)monBoundW / deskW);
				adjStartVY = monT + (int)Math.Round(adjStartLY * (double)monBoundH / deskH);
			}
		}
		try { proot.CaptureMouse(); } catch { }
		Cursor = cursorfor(hit);
	}

	void doadjmove(Point prootPos) {
		if (!adjDrag) return;

		// ── 多屏：虚拟坐标移动/缩放，可选跨屏 ──
		if (usemultimonann()) {
			if (!trycursor(out var vx, out var vy)) return;
			var dx = vx - adjStartVX;
			var dy = vy - adjStartVY;
			var l = adj0VL;
			var t = adj0VT;
			var w = adj0VW;
			var h = adj0VH;
			switch (adjHit) {
				case AdjHit.Move:
					l = adj0VL + dx;
					t = adj0VT + dy;
					break;
				case AdjHit.N:
					t = adj0VT + dy;
					h = adj0VH - dy;
					break;
				case AdjHit.S:
					h = adj0VH + dy;
					break;
				case AdjHit.W:
					l = adj0VL + dx;
					w = adj0VW - dx;
					break;
				case AdjHit.E:
					w = adj0VW + dx;
					break;
				case AdjHit.NW:
					l = adj0VL + dx;
					t = adj0VT + dy;
					w = adj0VW - dx;
					h = adj0VH - dy;
					break;
				case AdjHit.NE:
					t = adj0VT + dy;
					w = adj0VW + dx;
					h = adj0VH - dy;
					break;
				case AdjHit.SW:
					l = adj0VL + dx;
					w = adj0VW - dx;
					h = adj0VH + dy;
					break;
				case AdjHit.SE:
					w = adj0VW + dx;
					h = adj0VH + dy;
					break;
				default:
					return;
			}
			if (w < 0) { l += w; w = -w; }
			if (h < 0) { t += h; h = -h; }
			if (w < MIN_CROP) w = MIN_CROP;
			if (h < MIN_CROP) h = MIN_CROP;

			session.VirtualBounds(out var vL, out var vT, out var vW, out var vH);
			var vR = vL + vW;
			var vB = vT + vH;
			if (adjHit == AdjHit.Move) {
				l = Compat.Clamp(l, vL, Math.Max(vL, vR - w));
				t = Compat.Clamp(t, vT, Math.Max(vT, vB - h));
			}
			else {
				if (l < vL) { w -= (vL - l); l = vL; }
				if (t < vT) { h -= (vT - t); t = vT; }
				if (l + w > vR) w = vR - l;
				if (t + h > vB) h = vB - t;
				if (w < MIN_CROP) w = MIN_CROP;
				if (h < MIN_CROP) h = MIN_CROP;
				if (l + w > vR) l = Math.Max(vL, vR - w);
				if (t + h > vB) t = Math.Max(vT, vB - h);
			}
			if (l == session.AnnVL && t == session.AnnVT && w == session.AnnVW && h == session.AnnVH)
				return;
			session.AnnVL = l;
			session.AnnVT = t;
			session.AnnVW = w;
			session.AnnVH = h;
			// 视口：只改框/偏移；跨屏拼接：节流重裁
			if (canviewport()) {
				if (tryvirtualtodesk(l, t, w, h, out var dl, out var dt, out var dw, out var dh)) {
					cropL = dl; cropT = dt; cropW = dw; cropH = dh;
				}
				applyregionframeonly(clearStrokes: true);
			}
			else {
				var now = Environment.TickCount;
				if (lastAdjUiTick == 0 || unchecked(now - lastAdjUiTick) >= AdjUiMinMs) {
					lastAdjUiTick = now;
					applyregionui(clearStrokes: true);
				}
				else
					applyregionframeonly(clearStrokes: true);
			}
			return;
		}

		// ── 单屏：本屏 desk 像素 ──
		canvaslocal(prootPos, out var lx, out var ly);
		var dx2 = lx - adjStartLX;
		var dy2 = ly - adjStartLY;
		var l2 = adj0L;
		var t2 = adj0T;
		var w2 = adj0W;
		var h2 = adj0H;
		switch (adjHit) {
			case AdjHit.Move:
				l2 = adj0L + dx2;
				t2 = adj0T + dy2;
				break;
			case AdjHit.N:
				t2 = adj0T + dy2;
				h2 = adj0H - dy2;
				break;
			case AdjHit.S:
				h2 = adj0H + dy2;
				break;
			case AdjHit.W:
				l2 = adj0L + dx2;
				w2 = adj0W - dx2;
				break;
			case AdjHit.E:
				w2 = adj0W + dx2;
				break;
			case AdjHit.NW:
				l2 = adj0L + dx2;
				t2 = adj0T + dy2;
				w2 = adj0W - dx2;
				h2 = adj0H - dy2;
				break;
			case AdjHit.NE:
				t2 = adj0T + dy2;
				w2 = adj0W + dx2;
				h2 = adj0H - dy2;
				break;
			case AdjHit.SW:
				l2 = adj0L + dx2;
				w2 = adj0W - dx2;
				h2 = adj0H + dy2;
				break;
			case AdjHit.SE:
				w2 = adj0W + dx2;
				h2 = adj0H + dy2;
				break;
			default:
				return;
		}
		// 拖过对边时翻转
		if (w2 < 0) { l2 += w2; w2 = -w2; }
		if (h2 < 0) { t2 += h2; h2 = -h2; }
		if (w2 < MIN_CROP) w2 = MIN_CROP;
		if (h2 < MIN_CROP) h2 = MIN_CROP;

		if (adjHit == AdjHit.Move) {
			l2 = Compat.Clamp(l2, 0, Math.Max(0, deskW - w2));
			t2 = Compat.Clamp(t2, 0, Math.Max(0, deskH - h2));
		}
		else {
			if (l2 < 0) { w2 += l2; l2 = 0; }
			if (t2 < 0) { h2 += t2; t2 = 0; }
			if (l2 + w2 > deskW) w2 = deskW - l2;
			if (t2 + h2 > deskH) h2 = deskH - t2;
			if (w2 < MIN_CROP) w2 = MIN_CROP;
			if (h2 < MIN_CROP) h2 = MIN_CROP;
			if (l2 + w2 > deskW) l2 = Math.Max(0, deskW - w2);
			if (t2 + h2 > deskH) t2 = Math.Max(0, deskH - h2);
			l2 = Compat.Clamp(l2, 0, Math.Max(0, deskW - MIN_CROP));
			t2 = Compat.Clamp(t2, 0, Math.Max(0, deskH - MIN_CROP));
		}

		if (l2 == cropL && t2 == cropT && w2 == cropW && h2 == cropH) return;
		cropL = l2;
		cropT = t2;
		cropW = w2;
		cropH = h2;
		// 单屏视口：只重绘框与偏移，不重裁
		applyregionframeonly(clearStrokes: true);
	}

	/// <summary>跨屏虚拟选区是否需 RTB 拼接（重）；单屏落点则为 false。</summary>
	bool cropvirtualisslow(int vl, int vt, int vw, int vh) {
		if (session == null || session.Windows.Count <= 1) return false;
		var sel = new System.Drawing.Rectangle(vl, vt, Math.Max(1, vw), Math.Max(1, vh));
		var hits = 0;
		foreach (var ov in session.Windows) {
			var mon = new System.Drawing.Rectangle(ov.monL, ov.monT, ov.monBoundW, ov.monBoundH);
			var inter = System.Drawing.Rectangle.Intersect(sel, mon);
			if (inter.Width < 1 || inter.Height < 1) continue;
			hits++;
			if (hits > 1) return true;
			// 未完全覆盖该选区（贴边半屏等仍可能只需一屏裁切）
			if (inter.Left != sel.Left || inter.Top != sel.Top
				|| inter.Width != sel.Width || inter.Height != sel.Height)
				return true;
		}
		return false;
	}

	void endadj() {
		if (!adjDrag) return;
		adjDrag = false;
		adjHit = AdjHit.None;
		lastAdjUiTick = 0;
		try { proot.ReleaseMouseCapture(); } catch { }
		Cursor = Cursors.Arrow;
		// 跨屏：选区中心落到另一屏时，把宿主切过去
		if (usemultimonann()) {
			trytransferannotatehost();
			// 可能从视口切到拼接（或反过来），全量刷新内容模式
			applyregionui(clearStrokes: false);
			return;
		}
		// 视口模式松手只需对齐框；否则补一帧
		if (viewPortUi || canviewport())
			applyregionframeonly(clearStrokes: false);
		else
			applyregionui(clearStrokes: false);
	}

	/// <summary>
	/// 拖动选区轻量刷新：只改挖空/绿框/手柄。视口不叠第二层图，露底层冻结图。
	/// </summary>
	void applyregionframeonly(bool clearStrokes = false) {
		if (annotateGuest) {
			applyguestmask();
			return;
		}
		try {
			if (!syncregiongeom()) return;
			if (viewPortUi || canviewport()) {
				viewPortUi = true;
				shot = null;
				imgW = Math.Max(1, cropW);
				imgH = Math.Max(1, cropH);
				// 隐藏框内图，避免与 imgDesktop 双层亚像素错位
				if (imgshot.Visibility != Visibility.Collapsed) {
					imgshot.Source = null;
					imgshot.Visibility = Visibility.Collapsed;
				}
			}
			layoutregionchrome(clearStrokes);
		}
		catch (Exception ex) { CaptureLog.Ex("applyregionframeonly", ex); }
	}

	/// <summary>选区中心换屏时，切换标注宿主（工具条/画布跟到新屏）。</summary>
	void trytransferannotatehost() {
		if (session == null || !session.InAnnotate || boardMode) return;
		var best = session.BestHostForAnn();
		if (best == null || best == this) return;
		if (session.AnnotateHost != this && session.AnnotateHost != null) return;
		// 本屏降为 guest
		annotateGuest = true;
		bpane.Visibility = Visibility.Collapsed;
		bbar.Visibility = Visibility.Collapsed;
		phandles.Visibility = Visibility.Collapsed;
		// 清理本屏画布（调选区时本就会清空笔画；此处再保险）
		pdraw.Children.Clear();
		strokes.Clear();
		// 新宿主
		session.AnnotateHost = best;
		best.annotateGuest = false;
		best.phase = Phase.Annotate;
		best.boardMode = false;
		// 同步本屏局部 crop（用于单屏路径回退）
		if (best.tryvirtualtodesk(session.AnnVL, session.AnnVT, session.AnnVW, session.AnnVH,
				out var dl, out var dt, out var dw, out var dh)) {
			best.cropL = dl;
			best.cropT = dt;
			best.cropW = dw;
			best.cropH = dh;
		}
		best.enterannotate();
		// 其它屏保持 guest 遮罩
		foreach (var w in session.Windows) {
			if (w != best && !w.boardMode) {
				w.annotateGuest = true;
				w.bpane.Visibility = Visibility.Collapsed;
				w.bbar.Visibility = Visibility.Collapsed;
				w.phandles.Visibility = Visibility.Collapsed;
				w.applyguestmask();
			}
		}
		CaptureLog.Info($"transfer annotate host -> mon=({best.monL},{best.monT})");
	}

	/// <summary>画布坐标是否落在选区边缘热区（用于移动）。</summary>
	bool isedgehit(Point pInDraw) {
		var ex = Math.Min(EDGE_MOVE, Math.Max(4, selW / 4));
		var ey = Math.Min(EDGE_MOVE, Math.Max(4, selH / 4));
		if (pInDraw.X < 0 || pInDraw.Y < 0 || pInDraw.X > selW || pInDraw.Y > selH)
			return false;
		return pInDraw.X <= ex || pInDraw.Y <= ey
			|| pInDraw.X >= selW - ex || pInDraw.Y >= selH - ey;
	}

	// ───────── 标注阶段 ─────────

	void settool(Tool t) {
		tool = t;
		trect.IsChecked = t == Tool.Rect;
		tellipse.IsChecked = t == Tool.Ellipse;
		tline.IsChecked = t == Tool.Line;
		tarrow.IsChecked = t == Tool.Arrow;
		ttext.IsChecked = t == Tool.Text;
		pdraw.Cursor = Cursors.Cross;
	}

	Color curcolor() {
		// 优先微信式颜色圆点
		foreach (var rb in new[] { crred, cryellow, crgreen, crblue, crwhite }) {
			if (rb?.IsChecked == true) {
				var tag = rb.Tag as string;
				if (!string.IsNullOrEmpty(tag)) {
					try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(tag); }
					catch { }
				}
			}
		}
		var legacy = (ecolor.SelectedItem as ComboBoxItem)?.Tag as string ?? "#FA5151";
		try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(legacy); }
		catch { return Color.FromRgb(0xFA, 0x51, 0x51); }
	}

	double curfontsize() {
		var tag = (efont.SelectedItem as ComboBoxItem)?.Tag as string;
		return double.TryParse(tag, out var v) ? v : 18;
	}

	Brush strokebrush() => new SolidColorBrush(curcolor());

	double curthick() {
		var tag = (ethick.SelectedItem as ComboBoxItem)?.Tag as string;
		return double.TryParse(tag, out var v) && v > 0 ? v : 2;
	}

	void ondrawdown(object sender, MouseButtonEventArgs e) {
		if (phase != Phase.Annotate) return;
		if (e.ClickCount >= 2) return; // 双击由 Preview 完成
		if (e.Handled) return;
		if (adjDrag) return;
		// 点在文字上由文字宿主接管
		if (e.OriginalSource is DependencyObject od && findtexthost(od) != null)
			return;
		start = e.GetPosition(pdraw);
		// 画板全屏：不移动选区；截图标注：仅边缘热区移动
		if (!boardMode && isedgehit(start)) {
			committextedit();
			cleartextsel();
			startadj(AdjHit.Move, e.GetPosition(proot));
			e.Handled = true;
			return;
		}
		if (tool == Tool.Text) {
			// 空白处新建文字
			addtext(start);
			e.Handled = true;
			return;
		}
		// 其它工具：先结束文字编辑/选中
		committextedit();
		cleartextsel();
		if (tool == Tool.None) {
			// 无工具时框内不绘制（取消选中全部工具的情况）
			e.Handled = true;
			return;
		}
		drawing = true;
		var br = strokebrush();
		var thick = curthick();
		// Line = 自由画笔（Polyline）；Arrow = 直线箭头
		if (tool == Tool.Line) {
			var pl = new System.Windows.Shapes.Polyline {
				Stroke = br,
				StrokeThickness = thick,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeLineJoin = PenLineJoin.Round,
				Points = new PointCollection { start },
			};
			draft = pl;
			pdraw.Children.Add(pl);
			pdraw.CaptureMouse();
			e.Handled = true;
			return;
		}
		draft = tool switch {
			Tool.Rect => new WpfRectangle {
				Stroke = br, StrokeThickness = thick, Fill = Brushes.Transparent,
			},
			Tool.Ellipse => new System.Windows.Shapes.Ellipse {
				Stroke = br, StrokeThickness = thick, Fill = Brushes.Transparent,
			},
			Tool.Arrow => new WpfLine {
				Stroke = br, StrokeThickness = thick,
				StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
				X1 = start.X, Y1 = start.Y, X2 = start.X, Y2 = start.Y,
			},
			_ => null,
		};
		if (draft is WpfRectangle rc) {
			Canvas.SetLeft(rc, start.X);
			Canvas.SetTop(rc, start.Y);
			rc.Width = 0;
			rc.Height = 0;
		}
		else if (draft is System.Windows.Shapes.Ellipse el) {
			Canvas.SetLeft(el, start.X);
			Canvas.SetTop(el, start.Y);
			el.Width = 0;
			el.Height = 0;
		}
		if (draft != null) {
			pdraw.Children.Add(draft);
			pdraw.CaptureMouse();
		}
		e.Handled = true;
	}

	void ondrawmove(object sender, MouseEventArgs e) {
		if (phase != Phase.Annotate) return;
		if (adjDrag) {
			doadjmove(e.GetPosition(proot));
			return;
		}
		var p = e.GetPosition(pdraw);
		if (!drawing || draft == null) {
			// 悬停：画板始终十字；截图标注仅边缘显示移动光标
			pdraw.Cursor = (!boardMode && isedgehit(p)) ? Cursors.SizeAll : Cursors.Cross;
			return;
		}
		if (draft is System.Windows.Shapes.Polyline pl) {
			// 自由画笔：点距过近则跳过，减轻点数
			if (pl.Points.Count > 0) {
				var last = pl.Points[pl.Points.Count - 1];
				var dx = p.X - last.X;
				var dy = p.Y - last.Y;
				if (dx * dx + dy * dy < 1.5) return;
			}
			pl.Points.Add(p);
			return;
		}
		if (draft is WpfRectangle rc) {
			var x = Math.Min(p.X, start.X);
			var y = Math.Min(p.Y, start.Y);
			Canvas.SetLeft(rc, x);
			Canvas.SetTop(rc, y);
			rc.Width = Math.Abs(p.X - start.X);
			rc.Height = Math.Abs(p.Y - start.Y);
		}
		else if (draft is System.Windows.Shapes.Ellipse el) {
			var x = Math.Min(p.X, start.X);
			var y = Math.Min(p.Y, start.Y);
			Canvas.SetLeft(el, x);
			Canvas.SetTop(el, y);
			el.Width = Math.Abs(p.X - start.X);
			el.Height = Math.Abs(p.Y - start.Y);
		}
		else if (draft is WpfLine ln) {
			ln.X2 = p.X;
			ln.Y2 = p.Y;
		}
	}

	void ondrawup(object sender, MouseButtonEventArgs e) {
		if (phase == Phase.Annotate && adjDrag) {
			endadj();
			e.Handled = true;
			return;
		}
		if (!drawing) return;
		drawing = false;
		try { pdraw.ReleaseMouseCapture(); } catch { }
		var p = e.GetPosition(pdraw);
		if (draft is System.Windows.Shapes.Polyline pl) {
			if (pl.Points.Count < 2) {
				pdraw.Children.Remove(pl);
				draft = null;
				return;
			}
			// 单点点击：补一个极近点避免零长度
			if (pl.Points.Count == 1)
				pl.Points.Add(p);
			// 路径过短丢弃
			double len = 0;
			for (var i = 1; i < pl.Points.Count; i++) {
				var a = pl.Points[i - 1];
				var b = pl.Points[i];
				var dx = b.X - a.X;
				var dy = b.Y - a.Y;
				len += Math.Sqrt(dx * dx + dy * dy);
			}
			if (len < 2) {
				pdraw.Children.Remove(pl);
				draft = null;
				return;
			}
			strokes.Add(pl);
			draft = null;
			return;
		}
		if (draft is WpfLine ln && tool == Tool.Arrow) {
			pdraw.Children.Remove(ln);
			var arrow = makearrow(start, p, strokebrush(), curthick());
			if (arrow != null) {
				pdraw.Children.Add(arrow);
				strokes.Add(arrow);
			}
			draft = null;
			return;
		}
		if (draft is WpfRectangle rc && (rc.Width < 2 || rc.Height < 2)) {
			pdraw.Children.Remove(rc);
			draft = null;
			return;
		}
		if (draft is System.Windows.Shapes.Ellipse el && (el.Width < 2 || el.Height < 2)) {
			pdraw.Children.Remove(el);
			draft = null;
			return;
		}
		if (draft is WpfLine line) {
			var dx = line.X2 - line.X1;
			var dy = line.Y2 - line.Y1;
			if (Math.Sqrt(dx * dx + dy * dy) < 2) {
				pdraw.Children.Remove(line);
				draft = null;
				return;
			}
		}
		if (draft != null) {
			strokes.Add(draft);
			draft = null;
		}
	}

	// ───────── 文字标注（无背景；可拖动 / 双击再编辑，仿 PS） ─────────

	static Border findtexthost(DependencyObject d) {
		while (d != null) {
			if (d is Border b && b.Tag as string == TEXT_TAG)
				return b;
			d = VisualTreeHelper.GetParent(d);
		}
		return null;
	}

	void addtext(Point pos) {
		committextedit();
		cleartextsel();
		var host = new Border {
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			BorderBrush = Brushes.Transparent,
			Padding = new Thickness(0),
			Tag = TEXT_TAG,
			Cursor = Cursors.SizeAll,
			SnapsToDevicePixels = true,
		};
		Canvas.SetLeft(host, pos.X);
		Canvas.SetTop(host, pos.Y);
		pdraw.Children.Add(host);
		strokes.Add(host);
		wiretext(host);
		entertextedit(host, isNew: true);
	}

	void wiretext(Border host) {
		if (host == null) return;
		host.MouseLeftButtonDown += (s, e) => {
			if (phase != Phase.Annotate) return;
			// 编辑中：交给 TextBox
			if (editHost == host) return;
			if (e.ClickCount >= 2) {
				entertextedit(host, isNew: false);
				e.Handled = true;
				return;
			}
			committextedit();
			selecttext(host);
			textDrag = true;
			textDragMouse = e.GetPosition(pdraw);
			var lx = Canvas.GetLeft(host);
			var ty = Canvas.GetTop(host);
			if (double.IsNaN(lx)) lx = 0;
			if (double.IsNaN(ty)) ty = 0;
			textDragOrigin = new Point(lx, ty);
			try { host.CaptureMouse(); } catch { }
			e.Handled = true;
		};
		host.MouseMove += (s, e) => {
			if (!textDrag || editHost == host) return;
			if (e.LeftButton != MouseButtonState.Pressed) return;
			var p = e.GetPosition(pdraw);
			Canvas.SetLeft(host, textDragOrigin.X + (p.X - textDragMouse.X));
			Canvas.SetTop(host, textDragOrigin.Y + (p.Y - textDragMouse.Y));
			e.Handled = true;
		};
		host.MouseLeftButtonUp += (s, e) => {
			if (!textDrag) return;
			textDrag = false;
			try { host.ReleaseMouseCapture(); } catch { }
			e.Handled = true;
		};
	}

	void selecttext(Border host) {
		if (host == null) return;
		if (selText != null && selText != host)
			settextselvisual(selText, false);
		selText = host;
		if (editHost != host)
			settextselvisual(host, true);
	}

	void cleartextsel() {
		if (selText == null) return;
		settextselvisual(selText, false);
		selText = null;
	}

	void settextselvisual(Border host, bool on) {
		if (host == null || editHost == host) return;
		if (on) {
			host.BorderBrush = new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60));
			host.BorderThickness = new Thickness(1);
			host.Padding = new Thickness(2, 0, 2, 0);
		}
		else {
			host.BorderBrush = Brushes.Transparent;
			host.BorderThickness = new Thickness(0);
			host.Padding = new Thickness(0);
		}
	}

	void entertextedit(Border host, bool isNew) {
		if (host == null || phase != Phase.Annotate) return;
		if (editHost == host) return;
		committextedit();
		selecttext(host);
		settextselvisual(host, false);

		var fg = strokebrush();
		var fs = curfontsize();
		var old = "";
		if (host.Child is TextBlock blk) {
			old = blk.Text ?? "";
			if (blk.Foreground is SolidColorBrush scb) fg = scb;
			fs = blk.FontSize > 0 ? blk.FontSize : fs;
		}
		textEditBackup = old;
		textEditIsNew = isNew;
		editHost = host;

		var box = new WpfTextBox {
			Text = old,
			FontSize = fs,
			Foreground = fg,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			CaretBrush = fg is SolidColorBrush cb ? cb : Brushes.White,
			Padding = new Thickness(0),
			FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
			FontWeight = FontWeights.SemiBold,
			MinWidth = 48,
			AcceptsReturn = false,
		};
		// 编辑态：仅细描边，无底色
		host.Background = Brushes.Transparent;
		host.BorderBrush = fg;
		host.BorderThickness = new Thickness(1);
		host.Padding = new Thickness(2, 1, 2, 1);
		host.Cursor = Cursors.IBeam;
		host.Child = box;
		Dispatcher.BeginInvoke(new Action(() => {
			try {
				box.Focus();
				box.SelectAll();
			}
			catch { }
		}), System.Windows.Threading.DispatcherPriority.Input);

		box.KeyDown += (_, ke) => {
			if (ke.Key == Key.Enter) {
				committextedit();
				ke.Handled = true;
			}
			else if (ke.Key == Key.Escape) {
				if (textEditIsNew && string.IsNullOrWhiteSpace(textEditBackup))
					discardtext(host);
				else {
					box.Text = textEditBackup ?? "";
					committextedit();
				}
				ke.Handled = true;
			}
		};
		box.LostKeyboardFocus += (_, _) => {
			if (editHost == host)
				committextedit();
		};
	}

	void discardtext(Border host) {
		if (host == null) return;
		if (editHost == host) editHost = null;
		if (selText == host) selText = null;
		try { pdraw.Children.Remove(host); } catch { }
		strokes.Remove(host);
	}

	void committextedit() {
		if (editHost == null) return;
		var host = editHost;
		editHost = null;
		var text = "";
		Brush fg = strokebrush();
		var fs = curfontsize();
		if (host.Child is WpfTextBox box) {
			text = (box.Text ?? "").Trim();
			if (box.Foreground != null) fg = box.Foreground;
			if (box.FontSize > 0) fs = box.FontSize;
		}
		if (string.IsNullOrEmpty(text)) {
			discardtext(host);
			return;
		}
		host.Child = new TextBlock {
			Text = text,
			Foreground = fg,
			FontSize = fs,
			FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
			FontWeight = FontWeights.SemiBold,
			Background = Brushes.Transparent,
		};
		host.Background = Brushes.Transparent;
		host.Cursor = Cursors.SizeAll;
		if (selText == host)
			settextselvisual(host, true);
		else {
			host.BorderBrush = Brushes.Transparent;
			host.BorderThickness = new Thickness(0);
			host.Padding = new Thickness(0);
		}
	}

	static UIElement makearrow(Point from, Point to, Brush brush, double thick) {
		var dx = to.X - from.X;
		var dy = to.Y - from.Y;
		var len = Math.Sqrt(dx * dx + dy * dy);
		if (len < 2) return null;
		var ux = dx / len;
		var uy = dy / len;
		var head = Math.Max(10, thick * 4);
		var bx = to.X - ux * head;
		var by = to.Y - uy * head;
		var px = -uy;
		var py = ux;
		var hw = head * 0.45;
		var geo = new PathGeometry();
		var fig = new PathFigure { StartPoint = from, IsClosed = false };
		fig.Segments.Add(new LineSegment(to, true));
		geo.Figures.Add(fig);
		var fig2 = new PathFigure { StartPoint = to, IsClosed = true };
		fig2.Segments.Add(new LineSegment(new Point(bx + px * hw, by + py * hw), true));
		fig2.Segments.Add(new LineSegment(new Point(bx - px * hw, by - py * hw), true));
		geo.Figures.Add(fig2);
		return new WpfPath {
			Data = geo,
			Stroke = brush,
			StrokeThickness = thick,
			Fill = brush,
			StrokeLineJoin = PenLineJoin.Round,
			StrokeStartLineCap = PenLineCap.Round,
		};
	}

	void undo() {
		if (phase != Phase.Annotate) return;
		committextedit();
		if (strokes.Count == 0) return;
		var last = strokes[strokes.Count - 1];
		strokes.RemoveAt(strokes.Count - 1);
		pdraw.Children.Remove(last);
		if (selText == last) selText = null;
		if (editHost == last) editHost = null;
	}

	void onkey(object sender, KeyEventArgs e) {
		// 文字编辑中：Esc 交给 TextBox；不关闭遮罩
		if (editHost != null && e.Key == Key.Escape) return;
		if (e.Key == Key.Escape) {
			// 有选中文字时先取消选中
			if (selText != null) {
				cleartextsel();
				e.Handled = true;
				return;
			}
			session?.Cancel();
			e.Handled = true;
			return;
		}
		// 框选阶段：Ctrl+C 复制光标下色值
		if (phase == Phase.Select
			&& e.Key == Key.C
			&& Keyboard.Modifiers == ModifierKeys.Control) {
			copycolor();
			e.Handled = true;
			return;
		}
		if (phase == Phase.Annotate) {
			if (e.Key == Key.Enter) {
				finishcopy();
				e.Handled = true;
			}
			else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) {
				undo();
				e.Handled = true;
			}
		}
	}

	// ───────── 完成 ─────────

	/// <summary>完成并按配置写入剪贴板（screenshots/ 历史）。</summary>
	void finishcopy() => finishconfirm(wantOcr: false);

	/// <summary>完成标注 → 保存/复制 → 主窗 OCR 识别。</summary>
	void finishocr() => finishconfirm(wantOcr: true);

	void finishconfirm(bool wantOcr) {
		try {
			committextedit();
			cleartextsel();
			var bmp = renderresult();
			ResultImage = bmp;
			Confirmed = true;
			if (session != null) session.WantOcr = wantOcr;
			// 先关全屏遮罩再落盘/剪贴板，避免 PNG 编码 + 剪贴板重试时界面假死数秒
			session?.Complete(bmp, SelectedDip);
			try { ImageUtil.SaveScreenshotAndCopy(bmp, wantOcr ? "ocr" : "shot"); }
			catch (Exception ex) { CaptureLog.Ex("SaveScreenshotAndCopy", ex); }
		}
		catch (Exception ex) {
			MessageBox.Show(ex.Message, wantOcr ? "OCR 失败" : "复制失败",
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	void savefile() {
		try {
			committextedit();
			cleartextsel();
			var bmp = renderresult();
			var sfd = new Microsoft.Win32.SaveFileDialog {
				Title = "保存截图",
				Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
				FileName = $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
				DefaultExt = ".png",
				AddExtension = true,
			};
			Topmost = false;
			var ok = sfd.ShowDialog(this) == true;
			Topmost = true;
			if (!ok) return;
			ImageUtil.Savefile(bmp, sfd.FileName);
			// 同时写入历史目录并按配置复制到剪贴板
			try { ImageUtil.SaveScreenshotAndCopy(bmp, "shot"); }
			catch { }
			ResultImage = bmp;
			Confirmed = true;
			if (session != null) session.WantOcr = false;
			session?.Complete(bmp, SelectedDip);
		}
		catch (Exception ex) {
			Topmost = true;
			MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	BitmapSource renderresult() {
		committextedit();
		cleartextsel();
		if (phase != Phase.Annotate)
			return shot ?? materializeshot();
		// 视口模式预览不裁切；导出时再 materialize 选区像素
		var baseImg = materializeshot();
		if (baseImg == null) return null;
		imgW = baseImg.PixelWidth;
		imgH = baseImg.PixelHeight;
		if (pdraw.Children.Count == 0 && strokes.Count == 0)
			return baseImg;
		// 按像素尺寸渲染：标注从 DIP 缩放到像素
		var scaleX = imgW / Math.Max(1.0, selW);
		var scaleY = imgH / Math.Max(1.0, selH);
		var dv = new DrawingVisual();
		using (var dc = dv.RenderOpen()) {
			dc.DrawImage(baseImg, new Rect(0, 0, imgW, imgH));
			dc.PushTransform(new ScaleTransform(scaleX, scaleY));
			var vb = new VisualBrush(pdraw) {
				Stretch = Stretch.None,
				AlignmentX = AlignmentX.Left,
				AlignmentY = AlignmentY.Top,
			};
			dc.DrawRectangle(vb, null, new Rect(0, 0, selW, selH));
			dc.Pop();
		}
		var rtb = new RenderTargetBitmap(imgW, imgH, 96, 96, PixelFormats.Pbgra32);
		rtb.Render(dv);
		rtb.Freeze();
		return ImageUtil.Withdpi(rtb, 96, 96);
	}

	void updatemask(double x, double y, double w, double h) {
		// 相同挖空区不重建 Geometry（悬停高频路径）
		if (!double.IsNaN(lastMaskX)
			&& Math.Abs(lastMaskX - x) < 0.25 && Math.Abs(lastMaskY - y) < 0.25
			&& Math.Abs(lastMaskW - w) < 0.25 && Math.Abs(lastMaskH - h) < 0.25)
			return;
		lastMaskX = x; lastMaskY = y; lastMaskW = w; lastMaskH = h;
		var (W, H) = clientsize();
		var g = new PathGeometry { FillRule = FillRule.EvenOdd };
		g.AddGeometry(new RectangleGeometry(new Rect(0, 0, W, H)));
		if (w > 0.5 && h > 0.5)
			g.AddGeometry(new RectangleGeometry(new Rect(x, y, w, h)));
		mask.Data = g;
	}

	/// <summary>按屏幕 DIP 矩形截取（回退路径）。相对虚拟屏原点均匀映射到物理像素。</summary>
	public static BitmapSource Capturerect(Rect dipRect) {
		var (vsL, vsT, vsW, vsH) = ScreenDpi.VirtualScreenPixels();
		var (vLeft, vTop, vW, vH) = ScreenDpi.VirtualScreenDip();
		var relX = dipRect.X - vLeft;
		var relY = dipRect.Y - vTop;
		var left = vsL + (int)Math.Floor(relX / vW * vsW);
		var top = vsT + (int)Math.Floor(relY / vH * vsH);
		var right = vsL + (int)Math.Ceiling((relX + dipRect.Width) / vW * vsW);
		var bottom = vsT + (int)Math.Ceiling((relY + dipRect.Height) / vH * vsH);
		var pw = Math.Max(1, right - left);
		var ph = Math.Max(1, bottom - top);
		return grabscreenVirtual(left, top, pw, ph, "Capturerect");
	}

	static BitmapSource tobmp(System.Drawing.Bitmap bmp) => tobmp(bmp, out _);

	/// <summary>
	/// GDI → WPF：拷托管缓冲、强制不透明。
	/// GDI 抓屏 Alpha 常为 0，WPF 会画成全透明（深色底上看像「没图」）。
	/// </summary>
	static BitmapSource tobmp(System.Drawing.Bitmap bmp, out double nonBlackRatio) {
		nonBlackRatio = 0;
		var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
		var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			var stride = data.Stride;
			var nbytes = stride * bmp.Height;
			var pixels = new byte[nbytes];
			Marshal.Copy(data.Scan0, pixels, 0, nbytes);

			// 采样非黑像素 + 强制 Alpha=255
			long nonBlack = 0, samples = 0;
			var step = Math.Max(4, (nbytes / 4 / 2000) * 4); // 约采样 2k 点
			if (step % 4 != 0) step = (step / 4) * 4;
			if (step < 4) step = 4;
			for (int i = 0; i + 3 < nbytes; i += 4) {
				pixels[i + 3] = 255; // A
				if (i % step == 0) {
					samples++;
					if (pixels[i] > 8 || pixels[i + 1] > 8 || pixels[i + 2] > 8)
						nonBlack++;
				}
			}
			if (samples > 0) nonBlackRatio = nonBlack / (double)samples;

			var src = BitmapSource.Create(
				bmp.Width, bmp.Height, 96, 96,
				PixelFormats.Bgra32, null,
				pixels, stride);
			src.Freeze();
			return src;
		}
		finally {
			bmp.UnlockBits(data);
			bmp.Dispose();
		}
	}

	/// <summary>
	/// 保证冻结可跨线程/跨窗使用。已 Freeze 则直接返回，避免大图二次全像素拷贝卡住 UI。
	/// CroppedBitmap 持有源图引用，生命周期随结果图走，与遮罩窗关闭无关。
	/// </summary>
	static BitmapSource EnsureFrozen(BitmapSource src) {
		if (src == null) return null;
		try {
			if (src.IsFrozen) return src;
			if (src.CanFreeze) {
				src.Freeze();
				return src;
			}
		}
		catch { }
		return CloneFrozen(src);
	}

	/// <summary>深拷贝为冻结 Bgra32@96，强制不透明。仅在必须脱离源图时使用。</summary>
	static BitmapSource CloneFrozen(BitmapSource src) {
		if (src == null) return null;
		try {
			if (src.IsFrozen
				&& (src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32
					|| src.Format == PixelFormats.Bgr32))
				return src;
			BitmapSource bgra = src;
			if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Pbgra32
				&& src.Format != PixelFormats.Bgr32)
				bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
			var w = bgra.PixelWidth;
			var h = bgra.PixelHeight;
			if (w < 1 || h < 1) return src;
			var stride = w * 4;
			var pixels = new byte[stride * h];
			bgra.CopyPixels(pixels, stride, 0);
			for (int i = 3; i < pixels.Length; i += 4)
				pixels[i] = 255;
			var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
			bmp.Freeze();
			return bmp;
		}
		catch {
			try {
				var wb = new WriteableBitmap(src);
				wb.Freeze();
				return wb;
			}
			catch { return src; }
		}
	}
}
