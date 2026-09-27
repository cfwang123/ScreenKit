using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenKit;

sealed class CastScreenGrab : IDisposable {
	readonly object gate = new();
	Bitmap bmp;
	Graphics g;
	int ox, oy, w, h;

	public int Width => w;
	public int Height => h;

	[DllImport("gdi32.dll")]
	static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);
	[DllImport("user32.dll")]
	static extern IntPtr GetDC(IntPtr hwnd);
	[DllImport("user32.dll")]
	static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

	const int SRCCOPY = 0x00CC0020;
	const int CAPTUREBLT = 0x40000000;

	public CastScreenGrab() {
		var b = Screen.PrimaryScreen.Bounds;
		init(0, 0, b.Width, b.Height);
	}

	public CastScreenGrab(System.Drawing.Rectangle region) {
		init(region.X, region.Y, region.Width, region.Height);
	}

	void init(int x, int y, int rw, int rh) {
		ox = x;
		oy = y;
		w = rw / 2 * 2;
		h = rh / 2 * 2;
		if (w < 16) w = 16;
		if (h < 16) h = 16;
		bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		g = Graphics.FromImage(bmp);
	}

	public void Move(int x, int y) {
		lock (gate) { ox = x; oy = y; }
	}

	public bool Grab(byte[] dst, int stride) {
		lock (gate) {
			if (g == null || bmp == null) return false;
			return grabcore(dst, stride);
		}
	}

	bool grabcore(byte[] dst, int stride) {
		var ok = grabblt();
		if (!ok) {
			try {
				g.CopyFromScreen(ox, oy, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
				ok = true;
			}
			catch { return false; }
		}
		var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			var srcSt = data.Stride;
			var n = Math.Min(stride, srcSt);
			for (int y = 0; y < h; y++)
				Marshal.Copy(data.Scan0 + y * srcSt, dst, y * stride, n);
		}
		finally { bmp.UnlockBits(data); }
		return true;
	}

	bool grabblt() {
		var hdcSrc = GetDC(IntPtr.Zero);
		if (hdcSrc == IntPtr.Zero) return false;
		var hdcDst = g.GetHdc();
		try {
			return BitBlt(hdcDst, 0, 0, w, h, hdcSrc, ox, oy, SRCCOPY | CAPTUREBLT);
		}
		finally {
			g.ReleaseHdc(hdcDst);
			ReleaseDC(IntPtr.Zero, hdcSrc);
		}
	}

	public void Dispose() {
		lock (gate) {
			g?.Dispose();
			bmp?.Dispose();
			g = null;
			bmp = null;
		}
	}
}
