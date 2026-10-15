using System.Windows.Threading;

namespace ScreenKit;

public partial class RtspSrvWindow : Window {
	static readonly byte[][] glyph = {
		new byte[] { 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110 },
		new byte[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
		new byte[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111 },
		new byte[] { 0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110 },
		new byte[] { 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010 },
		new byte[] { 0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110 },
		new byte[] { 0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110 },
		new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000 },
		new byte[] { 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110 },
		new byte[] { 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100 },
	};
	const int BALL = 40;
	const int SPEED = 4;
	const int SCALE = 4;

	RtspTestSrv srv;
	RtspH264 enc;
	Thread drawth;
	WriteableBitmap bmp;
	byte[] pixels;
	int w = 640, h = 480, fps = 30;
	float bx = 80, by = 80, vx = SPEED, vy = SPEED;
	volatile bool on;
	int frames;
	int lastfps;
	int lasttick;
	readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };

	public RtspSrvWindow() {
		InitializeComponent();
		Title = Loc.T("rtspsrv.title");
		WindowEsc.Attach(this);
		applytext();
		btoggle.Click += (_, _) => toggle();
		bclear.Click += (_, _) => elog.Clear();
		Closed += (_, _) => stop();
		timer.Tick += (_, _) => paintstatus();
		timer.Start();
	}

	void applytext() {
		lbport.Text = Loc.T("rtspsrv.port");
		lbw.Text = Loc.T("rtspsrv.width");
		lbh.Text = Loc.T("rtspsrv.height");
		lbfps.Text = Loc.T("rtspsrv.fps");
		lbbr.Text = Loc.T("rtspsrv.bitrate");
		lblog.Text = Loc.T("rtspsrv.log");
		bclear.Content = Loc.T("rtspsrv.clear");
		btoggle.Content = Loc.T("rtspsrv.start");
		lbstatus.Text = Loc.T("rtspsrv.stopped");
	}

	void toggle() {
		if (on) { stop(); return; }
		if (!int.TryParse(eport.Text, out var port) || port < 1 || port > 65535) { log(Loc.T("rtspsrv.badport")); return; }
		if (!int.TryParse(ewidth.Text, out w) || w < 16) { log(Loc.T("rtspsrv.badsize")); return; }
		if (!int.TryParse(eheight.Text, out h) || h < 16) { log(Loc.T("rtspsrv.badsize")); return; }
		if (!int.TryParse(efps.Text, out fps) || fps < 1 || fps > 60) { log(Loc.T("rtspsrv.badfps")); return; }
		if (!int.TryParse(ebitrate.Text, out var kbps) || kbps < 100) { log(Loc.T("rtspsrv.badbr")); return; }
		w &= ~1;
		h &= ~1;
		try {
			enc = new RtspH264(w, h, fps, kbps * 1000);
			var warm = new byte[w * h * 4];
			for (var i = 3; i < warm.Length; i += 4) warm[i] = 0xFF;
			enc.Encode(warm);
			srv = new RtspTestSrv(port);
			srv.Log += msg => Dispatcher.BeginInvoke(new Action(() => log(msg)));
			srv.ClientPlaying += () => enc.ForceKey();
			if (enc.Sps != null) srv.SetCodec(enc.Sps, enc.Pps);
			srv.Start();
		}
		catch (Exception ex) {
			log(ex.Message);
			try { enc?.Dispose(); } catch { }
			enc = null;
			try { srv?.Dispose(); } catch { }
			srv = null;
			return;
		}
		on = true;
		bx = 80;
		by = 80;
		vx = SPEED;
		vy = SPEED;
		frames = 0;
		lastfps = 0;
		lasttick = Environment.TickCount;
		setinputs(false);
		btoggle.Content = Loc.T("rtspsrv.stop");
		lbstatus.Text = Loc.T("rtspsrv.running");
		drawth = new Thread(drawloop) { IsBackground = true, Name = "rtspsrv-draw" };
		drawth.Start();
	}

	void drawloop() {
		var delay = Math.Max(1, 1000 / fps);
		while (on) {
			var t0 = Environment.TickCount;
			step();
			var text = DateTime.Now.ToString("HH:mm:ss.fff");
			drawtext(text, 16, 16);
			try {
				var au = enc.Encode(pixels);
				if (au != null) srv?.Push(au);
			}
			catch { break; }
			frames++;
			var wait = delay - (Environment.TickCount - t0);
			if (wait > 0) Thread.Sleep(wait);
			try {
				Dispatcher.BeginInvoke(new Action(() => {
					if (bmp == null || bmp.PixelWidth != w || bmp.PixelHeight != h)
						bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
					bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
					imgprev.Source = bmp;
				}));
			}
			catch { }
		}
	}

	void step() {
		if (pixels == null || pixels.Length != w * h * 4) pixels = new byte[w * h * 4];
		for (var i = 0; i < pixels.Length; i += 4) {
			pixels[i] = 0x0D;
			pixels[i + 1] = 0x0D;
			pixels[i + 2] = 0x0D;
			pixels[i + 3] = 0xFF;
		}
		bx += vx;
		by += vy;
		if (bx < 0 || bx + BALL > w) { vx = -vx; bx = Math.Max(0, Math.Min(bx, w - BALL)); }
		if (by < 0 || by + BALL > h) { vy = -vy; by = Math.Max(0, Math.Min(by, h - BALL)); }
		var x0 = (int)bx;
		var y0 = (int)by;
		for (var y = 0; y < BALL; y++) {
			var py = y0 + y;
			if (py < 0 || py >= h) continue;
			for (var x = 0; x < BALL; x++) {
				var px = x0 + x;
				if (px < 0 || px >= w) continue;
				var o = (py * w + px) * 4;
				pixels[o] = 0x44;
				pixels[o + 1] = 0x44;
				pixels[o + 2] = 0xFF;
			}
		}
	}

	void drawtext(string text, int ox, int oy) {
		var x = ox;
		foreach (var ch in text) {
			if (ch == ':') { drawcolon(x, oy); x += 3 * SCALE; continue; }
			if (ch == '.') { drawdot(x, oy); x += 2 * SCALE; continue; }
			if (ch >= '0' && ch <= '9') drawdigit(ch - '0', x, oy);
			x += 6 * SCALE;
		}
	}

	void drawdigit(int d, int ox, int oy) {
		var g = glyph[d];
		for (var row = 0; row < 7; row++) {
			var bits = g[row];
			for (var col = 0; col < 5; col++) {
				if ((bits & (1 << (4 - col))) == 0) continue;
				fill(ox + col * SCALE, oy + row * SCALE, SCALE, SCALE, 0x44, 0xFF, 0x44);
			}
		}
	}

	void drawcolon(int ox, int oy) {
		fill(ox, oy + 2 * SCALE, SCALE, SCALE, 0x44, 0xFF, 0x44);
		fill(ox, oy + 5 * SCALE, SCALE, SCALE, 0x44, 0xFF, 0x44);
	}

	void drawdot(int ox, int oy) {
		fill(ox, oy + 6 * SCALE, SCALE, SCALE, 0x44, 0xFF, 0x44);
	}

	void fill(int x, int y, int rw, int rh, byte b, byte g, byte r) {
		for (var yy = 0; yy < rh; yy++) {
			var py = y + yy;
			if (py < 0 || py >= h) continue;
			for (var xx = 0; xx < rw; xx++) {
				var px = x + xx;
				if (px < 0 || px >= w) continue;
				var o = (py * w + px) * 4;
				pixels[o] = b;
				pixels[o + 1] = g;
				pixels[o + 2] = r;
			}
		}
	}

	void paintstatus() {
		if (!on) return;
		var now = Environment.TickCount;
		var dt = now - lasttick;
		if (dt >= 500) {
			lastfps = (int)(frames * 1000L / Math.Max(1, dt));
			frames = 0;
			lasttick = now;
		}
		var n = srv == null ? 0 : srv.ClientCount;
		var play = srv != null && srv.Playing ? Loc.T("rtspsrv.playing") : Loc.T("rtspsrv.idle");
		lbinfo.Text = "RTSP: " + n + " " + play + " | " + lastfps + " fps";
	}

	void stop() {
		on = false;
		try { drawth?.Join(800); } catch { }
		try { enc?.Dispose(); } catch { }
		enc = null;
		try { srv?.Dispose(); } catch { }
		srv = null;
		setinputs(true);
		btoggle.Content = Loc.T("rtspsrv.start");
		lbstatus.Text = Loc.T("rtspsrv.stopped");
		lbinfo.Text = "";
	}

	void setinputs(bool on) {
		eport.IsEnabled = on;
		ewidth.IsEnabled = on;
		eheight.IsEnabled = on;
		efps.IsEnabled = on;
		ebitrate.IsEnabled = on;
	}

	void log(string msg) {
		if (string.IsNullOrEmpty(msg)) return;
		elog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
		elog.ScrollToEnd();
		if (elog.LineCount > 400) {
			var cut = elog.Text.IndexOf('\n');
			if (cut > 0) elog.Text = elog.Text.Substring(cut + 1);
		}
	}
}
