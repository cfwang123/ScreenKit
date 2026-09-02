using System.Drawing;
using System.Drawing.Imaging;

namespace ScreenKit;

/// <summary>CLI：校验点击高亮圈能画上，并尽力叠加真实光标。</summary>
static class RecordCursorTest {
	public static int Run(string outDir, Action<string> log) {
		void L(string s) {
			try { log?.Invoke(s); } catch { }
		}
		if (string.IsNullOrWhiteSpace(outDir))
			outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "record_cursor");
		outDir = Path.GetFullPath(outDir);
		Directory.CreateDirectory(outDir);
		L("=== 录屏鼠标叠加 --test-record-cursor ===");

		var bad = 0;
		void fail(string m) {
			L("FAIL " + m);
			bad++;
		}

		var ringPath = Path.Combine(outDir, "click_ring.png");
		using (var bmp = new Bitmap(160, 160, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
			using (var g = Graphics.FromImage(bmp))
				g.Clear(System.Drawing.Color.FromArgb(255, 16, 16, 16));
			var ov = new RecordCursorOverlay(showCursor: false, highlightClicks: true);
			ov.InjectClick(80, 80, 0, ageMs: 80);
			ov.Apply(bmp, new System.Drawing.Rectangle(0, 0, 160, 160), pollInput: false);
			bmp.Save(ringPath, ImageFormat.Png);
			var yellow = countyellow(bmp);
			L($"click_ring={ringPath} yellowPx={yellow}");
			if (yellow < 40)
				fail($"点击高亮圈像素过少 yellowPx={yellow}");
		}

		var curPath = Path.Combine(outDir, "cursor_live.png");
		try {
			var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
			var ci = new RecordCursorOverlay(true, true);
			// 用屏幕中心一块，叠加当前光标（若在框内）与一发测试点击
			var w = 320;
			var h = 240;
			var region = new System.Drawing.Rectangle(
				s.Left + (s.Width - w) / 2, s.Top + (s.Height - h) / 2, w, h);
			using var live = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (var g = Graphics.FromImage(live)) {
				g.CopyFromScreen(region.Left, region.Top, 0, 0, new System.Drawing.Size(w, h),
					System.Drawing.CopyPixelOperation.SourceCopy);
			}
			ci.InjectClick(region.Left + 40, region.Top + 40, 0, ageMs: 40);
			ci.Apply(live, region, pollInput: true);
			live.Save(curPath, ImageFormat.Png);
			L($"cursor_live={curPath} size={new FileInfo(curPath).Length} region={region}");
			if (!File.Exists(curPath) || new FileInfo(curPath).Length < 64)
				fail("cursor_live.png 未写出");
		}
		catch (Exception ex) {
			L("cursor_live skip: " + ex.Message);
		}

		L(bad == 0 ? "=== OK：鼠标叠加可用 ===" : $"=== FAIL bad={bad} ===");
		return bad == 0 ? 0 : 1;
	}

	static int countyellow(Bitmap bmp) {
		var n = 0;
		var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
		var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try {
			var stride = Math.Abs(data.Stride);
			var buf = new byte[stride * bmp.Height];
			System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
			for (var y = 0; y < bmp.Height; y++) {
				var row = y * stride;
				for (var x = 0; x < bmp.Width; x++) {
					var i = row + x * 4;
					var b = buf[i];
					var g = buf[i + 1];
					var r = buf[i + 2];
					if (r > 100 && g > 70 && b < 100) n++;
				}
			}
		}
		finally {
			bmp.UnlockBits(data);
		}
		return n;
	}
}
