using System.Drawing;
using System.Windows.Media.Imaging;

namespace ScreenKit;

/// <summary>上次区域截图的虚拟屏像素矩形（与 <see cref="ScreenRecorder.CaptureRegion"/> 一致）。</summary>
static class SnapLastRegion {
	/// <summary>写入 config 后通知主窗同步内存中的 <see cref="OcrOptions"/>。</summary>
	public static event Action<int, int, int, int> Saved;

	public static void Remember(int left, int top, int width, int height) {
		if (width < 1 || height < 1) return;
		var o = new OcrOptions();
		try { AppConfig.LoadInto(o); } catch { }
		o.SnapLastLeft = left;
		o.SnapLastTop = top;
		o.SnapLastWidth = width;
		o.SnapLastHeight = height;
		try { AppConfig.Save(o); } catch { }
		try { Saved?.Invoke(left, top, width, height); } catch { }
	}

	public static bool TryGet(OcrOptions o, out Rectangle rect) {
		rect = default;
		int l = 0, t = 0, w = 0, h = 0;
		if (o != null && o.SnapLastWidth >= 1 && o.SnapLastHeight >= 1) {
			l = o.SnapLastLeft;
			t = o.SnapLastTop;
			w = o.SnapLastWidth;
			h = o.SnapLastHeight;
		}
		else {
			var tmp = new OcrOptions();
			try { AppConfig.LoadInto(tmp); } catch { }
			if (tmp.SnapLastWidth < 1 || tmp.SnapLastHeight < 1) return false;
			l = tmp.SnapLastLeft;
			t = tmp.SnapLastTop;
			w = tmp.SnapLastWidth;
			h = tmp.SnapLastHeight;
		}
		rect = new Rectangle(l, t, w, h);
		var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
		rect = Rectangle.Intersect(rect, vs);
		return rect.Width >= 1 && rect.Height >= 1;
	}

	public static BitmapSource Capture(OcrOptions o) {
		if (!TryGet(o, out var r)) return null;
		return ScreenRecorder.CaptureRegion(r);
	}
}
