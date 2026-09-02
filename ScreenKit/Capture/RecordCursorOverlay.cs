using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ScreenKit;

/// <summary>
/// 录屏帧叠加：系统光标 + 鼠标点击高亮圈。
/// GDI CopyFromScreen 不含指针，需每帧 GetCursorInfo / DrawIconEx。
/// </summary>
sealed class RecordCursorOverlay {
	readonly bool showCursor;
	readonly bool highlightClicks;
	readonly bool[] prevDown = new bool[3];
	readonly bool[] held = new bool[3];
	readonly List<Ripple> ripples = new();

	const int CURSOR_SHOWING = 0x00000001;
	const int DI_NORMAL = 0x0003;
	const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;
	const int RIPPLE_MS = 450;
	static readonly int[] Vks = { VK_LBUTTON, VK_RBUTTON, VK_MBUTTON };

	struct NativePoint {
		public int X, Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct CURSORINFO {
		public int cbSize;
		public int flags;
		public IntPtr hCursor;
		public NativePoint ptScreenPos;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct ICONINFO {
		public bool fIcon;
		public int xHotspot;
		public int yHotspot;
		public IntPtr hbmMask;
		public IntPtr hbmColor;
	}

	struct Ripple {
		public int X, Y;
		public int Kind;
		public int StartTick;
	}

	[DllImport("user32.dll")]
	static extern bool GetCursorInfo(ref CURSORINFO pci);

	[DllImport("user32.dll")]
	static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

	[DllImport("user32.dll")]
	static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon,
		int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

	[DllImport("user32.dll")]
	static extern short GetAsyncKeyState(int vKey);

	[DllImport("gdi32.dll")]
	static extern bool DeleteObject(IntPtr ho);

	public RecordCursorOverlay(bool showCursor, bool highlightClicks) {
		this.showCursor = showCursor;
		this.highlightClicks = highlightClicks;
	}

	public bool Enabled => showCursor || highlightClicks;

	/// <summary>在抓屏位图上叠加光标与点击圈。region 为桌面物理像素。</summary>
	public void Apply(Bitmap bmp, System.Drawing.Rectangle region, bool pollInput = true) {
		if (bmp == null || !Enabled) return;
		var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
		var have = GetCursorInfo(ref ci);
		if (pollInput && highlightClicks && have)
			pollclicks(ci.ptScreenPos.X, ci.ptScreenPos.Y);
		draw(bmp, region, have ? ci : default, have);
	}

	/// <summary>测试用：在屏幕坐标注入一次点击（左=0 右=1 中=2）。</summary>
	public void InjectClick(int screenX, int screenY, int kind, int ageMs = 80) {
		kind = Compat.Clamp(kind, 0, 2);
		ripples.Add(new Ripple {
			X = screenX,
			Y = screenY,
			Kind = kind,
			StartTick = Environment.TickCount - Math.Max(0, ageMs),
		});
	}

	void pollclicks(int sx, int sy) {
		var now = Environment.TickCount;
		for (var i = 0; i < 3; i++) {
			var down = (GetAsyncKeyState(Vks[i]) & 0x8000) != 0;
			if (down && !prevDown[i])
				ripples.Add(new Ripple { X = sx, Y = sy, Kind = i, StartTick = now });
			held[i] = down;
			prevDown[i] = down;
		}
		prune(now);
	}

	void prune(int now) {
		for (var i = ripples.Count - 1; i >= 0; i--) {
			if (now - ripples[i].StartTick >= RIPPLE_MS)
				ripples.RemoveAt(i);
		}
	}

	void draw(Bitmap bmp, System.Drawing.Rectangle region, CURSORINFO ci, bool haveCur) {
		var now = Environment.TickCount;
		if (highlightClicks)
			prune(now);
		using var g = Graphics.FromImage(bmp);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		if (showCursor && haveCur && (ci.flags & CURSOR_SHOWING) != 0)
			drawcursor(g, region, ci);
		if (!highlightClicks) return;
		foreach (var rp in ripples)
			drawripple(g, region, rp.X, rp.Y, rp.Kind, now - rp.StartTick);
		if (haveCur) {
			for (var i = 0; i < 3; i++) {
				if (held[i])
					drawheld(g, region, ci.ptScreenPos.X, ci.ptScreenPos.Y, i);
			}
		}
	}

	static void drawcursor(Graphics g, System.Drawing.Rectangle region, CURSORINFO ci) {
		if (ci.hCursor == IntPtr.Zero) return;
		var hx = 0;
		var hy = 0;
		if (GetIconInfo(ci.hCursor, out var ii)) {
			hx = ii.xHotspot;
			hy = ii.yHotspot;
			if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
			if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
		}
		var x = ci.ptScreenPos.X - region.Left - hx;
		var y = ci.ptScreenPos.Y - region.Top - hy;
		var hdc = g.GetHdc();
		try {
			DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
		}
		finally {
			g.ReleaseHdc(hdc);
		}
	}

	static void drawripple(Graphics g, System.Drawing.Rectangle region, int sx, int sy, int kind, int ageMs) {
		if (ageMs < 0) ageMs = 0;
		if (ageMs >= RIPPLE_MS) return;
		var t = ageMs / (float)RIPPLE_MS;
		var lx = sx - region.Left;
		var ly = sy - region.Top;
		var radius = 14f + t * 28f;
		var alpha = (int)(200 * (1f - t));
		if (alpha < 8) return;
		var c = tint(kind, alpha);
		var penW = Math.Max(2f, 4.5f * (1f - t * 0.4f));
		using var pen = new System.Drawing.Pen(c, penW);
		g.DrawEllipse(pen, lx - radius, ly - radius, radius * 2, radius * 2);
		var innerA = (int)(90 * (1f - t));
		if (innerA >= 12) {
			var ir = Math.Max(4f, radius * 0.35f);
			using var fill = new SolidBrush(tint(kind, innerA));
			g.FillEllipse(fill, lx - ir, ly - ir, ir * 2, ir * 2);
		}
	}

	static void drawheld(Graphics g, System.Drawing.Rectangle region, int sx, int sy, int kind) {
		var lx = sx - region.Left;
		var ly = sy - region.Top;
		const float r = 16f;
		using var fill = new SolidBrush(tint(kind, 70));
		using var pen = new System.Drawing.Pen(tint(kind, 180), 2f);
		g.FillEllipse(fill, lx - r, ly - r, r * 2, r * 2);
		g.DrawEllipse(pen, lx - r, ly - r, r * 2, r * 2);
	}

	static System.Drawing.Color tint(int kind, int alpha) {
		alpha = Compat.Clamp(alpha, 0, 255);
		return kind switch {
			1 => System.Drawing.Color.FromArgb(alpha, 70, 160, 255),
			2 => System.Drawing.Color.FromArgb(alpha, 80, 220, 100),
			_ => System.Drawing.Color.FromArgb(alpha, 255, 210, 0),
		};
	}
}
