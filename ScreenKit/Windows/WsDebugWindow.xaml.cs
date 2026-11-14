using System.Text;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → 网络工具 → WebSocket 调试。客户端连别人的服务收发，服务端页能在本机起一个。
/// 帧和握手都自己拼，不引第三方库。</summary>
public partial class WsDebugWindow : Window {
	WsConn conn;
	WsServer srv;
	readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
	/// <summary>日志先塞队列，定时器统一刷到界面，免得高频消息把 UI 拖死。</summary>
	readonly Queue<string> pending = new();
	readonly object pgate = new();
	int pendingDropped;
	readonly Queue<string> spending = new();
	int spendingDropped;
	string flash = "";
	/// <summary>服务端推送的目标：空表示广播。</summary>
	string pushto = "";
	string clientstat = "";

	public WsDebugWindow() {
		InitializeComponent();
		applylang();
		initui();
		initserver();
		inithost();
		timer.Tick += (_, _) => tick();
		timer.Start();
		WindowEsc.Attach(this);
		Closed += (_, _) => {
			timer.Stop();
			try { conn?.Dispose(); } catch { }
			try { srv?.Dispose(); } catch { }
		};
	}

	void applylang() {
		Title = Loc.T("wsdbg.title");
		lbhint.Text = Loc.T("wsdbg.hint");
		tabclient.Header = Loc.T("wsdbg.tab.client");
		tabserver.Header = Loc.T("wsdbg.tab.server");
		lbcurl.Text = Loc.T("wsdbg.url");
		lbcproto.Text = Loc.T("wsdbg.proto");
		lbcorigin.Text = Loc.T("wsdbg.origin");
		lbeheaders.Text = Loc.T("wsdbg.headers");
		lbcsend.Text = Loc.T("wsdbg.send");
		cchex.Content = Loc.T("wsdbg.hex");
		cctrust.Content = Loc.T("wsdbg.trust");
		bcclear.Content = Loc.T("wsdbg.clear");
		bccopy.Content = Loc.T("wsdbg.copy");
		ccautoscroll.Content = Loc.T("wsdbg.autoscroll");
		ToolBtnUi.Set(bcsend, ToolBtnUi.Up, Loc.T("wsdbg.sendbtn"));
		lbsport.Text = Loc.T("wsdbg.port");
		csecho.Content = Loc.T("wsdbg.echo");
		lbclients.Text = Loc.T("wsdbg.clients");
		lbslog.Text = Loc.T("wsdbg.server.log");
		bsclear.Content = Loc.T("wsdbg.clear");
		bscopy.Content = Loc.T("wsdbg.copy");
		lbppush.Text = Loc.T("wsdbg.push.target");
		cphex.Content = Loc.T("wsdbg.hex");
		bpush.Content = Loc.T("wsdbg.push");
		bpushall.Content = Loc.T("wsdbg.pushall");
		updconnui();
		updserverui();
	}

	void initui() {
		ccop.Items.Clear();
		ccop.Items.Add(Loc.T("wsdbg.op.text"));
		ccop.Items.Add(Loc.T("wsdbg.op.bin"));
		ccop.Items.Add(Loc.T("wsdbg.op.ping"));
		ccop.SelectedIndex = 0;

		bcconn.Click += (_, _) => togglconn();
		bcsend.Click += (_, _) => sendclient();

		bcclear.Click += (_, _) => eclog.Text = "";
		bccopy.Click += (_, _) => {
			try { Clipboard.SetText(eclog.Text ?? ""); }
			catch { }
		};
	}

	void initserver() {
		cpop.Items.Clear();
		cpop.Items.Add(Loc.T("wsdbg.op.text"));
		cpop.Items.Add(Loc.T("wsdbg.op.bin"));
		cpop.Items.Add(Loc.T("wsdbg.op.ping"));
		cpop.SelectedIndex = 0;

		bstoggle.Click += (_, _) => toggleserver();
		bsclear.Click += (_, _) => eslog.Text = "";
		bscopy.Click += (_, _) => {
			try { Clipboard.SetText(eslog.Text ?? ""); }
			catch { }
		};
		bpush.Click += (_, _) => push(false);
		bpushall.Click += (_, _) => push(true);
		lclients.SelectionChanged += (_, _) => {
			pushto = (lclients.SelectedItem as string) ?? "";
			if (pushto.Length == 0) pushto = "";
		};
		updserverui();
	}

	/// <summary>默认连本机：一般就是本页服务端起的端口，开箱能自测回环。</summary>
	void inithost() {
		ecurl.Text = "ws://127.0.0.1:9001/echo";
		ecproto.Text = "";
		ecorigin.Text = "";
		ecsend.Text = "hello";
		esport.Text = "9001";
		epsend.Text = "hello";
		csecho.IsChecked = true;
	}

	// ==================== 客户端 ====================

	void togglconn() {
		if (conn != null && conn.Alive) {
			conn.Close(1000, "");
			conn = null;
			clientstat = Loc.T("wsdbg.disconnected");
			say("已断开");
			updconnui();
			return;
		}
		var url = (ecurl.Text ?? "").Trim();
		if (url.Length == 0) { flash = Loc.T("wsdbg.needurl"); return; }
		clientstat = Loc.T("wsdbg.connecting");
		updconnui();
		var e = WsClient.Connect(url, echeaders.Text, ecproto.Text, ecorigin.Text,
			cctrust.IsChecked == true, 10000, out var c, out var hs);
		if (hs.Length > 0) say("----\r\n" + hs);
		if (e.Length > 0) {
			say("[连接失败] " + e);
			flash = Loc.T("wsdbg.fail", e);
			clientstat = Loc.T("wsdbg.disconnected");
			updconnui();
			return;
		}
		conn = c;
		conn.Message += (_, op, data) => reccv(op, data);
		conn.Control += (_, op, data) => {
			if (op == WsProto.OP_PING) say($"{stamp()} ← ping  {WsProto.pretty(data, false, 0)}");
			else if (op == WsProto.OP_PONG) say($"{stamp()} ← pong  {WsProto.pretty(data, false, 0)}");
		};
		conn.Ended += (_, why) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
			conn = null;
			clientstat = Loc.T("wsdbg.disconnected");
			updconnui();
			if (!string.IsNullOrEmpty(why)) flash = Loc.T("wsdbg.lost", why);
		}));
		clientstat = Loc.T("wsdbg.connected", conn.Proto ?? "");
		flash = Loc.T("wsdbg.connected", conn.Proto ?? "");
		updconnui();
	}

	void reccv(int op, byte[] data) {
		var tag = op == WsProto.OP_BIN ? Loc.T("wsdbg.frame.bin") : Loc.T("wsdbg.frame.text");
		say($"{stamp()} ← {tag} {Loc.T("wsdbg.framelen", data.Length)}\r\n{WsProto.pretty(data, op == WsProto.OP_BIN && cchex.IsChecked != true, 4000)}");
	}

	void sendclient() {
		if (conn == null || !conn.Alive) { flash = Loc.T("wsdbg.needconn"); return; }
		var body = payload(ecsend.Text, cchex.IsChecked == true);
		if (body == null) { flash = Loc.T("wsdbg.badhex"); return; }
		var op = opof(ccop);
		if (!conn.Send(op, body)) { flash = Loc.T("wsdbg.fail", "发送失败"); return; }
		var tag = op == WsProto.OP_BIN ? Loc.T("wsdbg.frame.bin")
			: op == WsProto.OP_PING ? Loc.T("wsdbg.op.ping") : Loc.T("wsdbg.frame.text");
		say($"{stamp()} → {tag} {Loc.T("wsdbg.framelen", body.Length)}\r\n{WsProto.pretty(body, cchex.IsChecked == true, 4000)}");
	}

	void updconnui() {
		var on = conn != null && conn.Alive;
		bcconn.Content = on ? Loc.T("wsdbg.disconnect") : Loc.T("wsdbg.connect");
		bcconn.Style = (Style)FindResource(on ? "DisconnectBtn" : "ConnectBtn");
		ecurl.IsEnabled = ecproto.IsEnabled = ecorigin.IsEnabled = !on;
		bcsend.IsEnabled = on;
		dotc.Fill = (Brush)FindResource(on ? "Success" : "BorderSoft");
		lbcstat.Text = string.IsNullOrEmpty(clientstat) ? Loc.T("wsdbg.disconnected") : clientstat;
	}

	// ==================== 服务端 ====================

	void toggleserver() {
		if (srv != null && srv.Running) {
			try { srv.Dispose(); } catch { }
			srv = null;
			updserverui();
			return;
		}
		var port = portof(esport, 9001);
		var b = new WsServer(port) { Echo = csecho.IsChecked == true };
		b.Log += s => sput(s);
		b.Message += (c, op, data) => sput(Loc.T("wsdbg.server.recv", c?.Peer ?? "", WsProto.opname(op), data.Length)
			+ "\r\n" + WsProto.pretty(data, false, 800));
		b.ClientsChanged += () => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(updserverui));
		try {
			b.Start();
			srv = b;
			esport.Text = srv.Port.ToString();
		}
		catch (Exception ex) {
			try { b.Dispose(); } catch { }
			sput("启动失败：" + ex.Message);
			flash = Loc.T("wsdbg.fail", ex.Message);
		}
		updserverui();
	}

	void updserverui() {
		var on = srv != null && srv.Running;
		bstoggle.Content = on ? Loc.T("wsdbg.server.stop") : Loc.T("wsdbg.server.start");
		bstoggle.Style = (Style)FindResource(on ? "DisconnectBtn" : "ConnectBtn");
		esport.IsEnabled = !on;
		csecho.IsEnabled = on;
		bpush.IsEnabled = on;
		bpushall.IsEnabled = on;
		dotsrv.Fill = (Brush)FindResource(on ? "Success" : "BorderSoft");
		lbsstat.Text = on ? Loc.T("wsdbg.server.running", srv.Port) : Loc.T("wsdbg.server.stopped");
		if (!on) {
			lclients.ItemsSource = null;
			pushto = "";
			return;
		}
		var list = new List<string>();
		foreach (var (peer, proto) in srv.Clients())
			list.Add(proto.Length > 0 ? peer + "  [" + proto + "]" : peer);
		if (!sameas(lclients.ItemsSource as List<string>, list)) {
			var keep = lclients.SelectedItem as string;
			lclients.ItemsSource = list;
			if (keep != null) {
				var hit = list.FindIndex(x => x == keep);
				if (hit >= 0) lclients.SelectedIndex = hit;
			}
		}
	}

	/// <summary>给选中的客户端推，没选就广播。</summary>
	void push(bool all) {
		if (srv == null || !srv.Running) { flash = Loc.T("wsdbg.server.notrunning"); return; }
		var body = payload(epsend.Text, cphex.IsChecked == true);
		if (body == null) { flash = Loc.T("wsdbg.badhex"); return; }
		var op = opof(cpop);
		var only = all ? "" : pushto;
		if (!all && string.IsNullOrEmpty(only)) { flash = Loc.T("wsdbg.push.needclient"); return; }
		var n = srv.Broadcast(op, body, only);
		sput(Loc.T("wsdbg.server.pushed", n, WsProto.opname(op), body.Length));
		if (n == 0) flash = Loc.T("wsdbg.server.nobody");
	}

	static int opof(ComboBox box) => box.SelectedIndex switch {
		1 => WsProto.OP_BIN,
		2 => WsProto.OP_PING,
		_ => WsProto.OP_TEXT,
	};

	// ==================== 日志 / 定时刷新 ====================

	void tick() {
		var lines = new StringBuilder();
		var n = 0;
		lock (pgate) {
			while (pending.Count > 0 && n < 200) {
				lines.Append(pending.Dequeue());
				n++;
			}
			if (n > 0 && pendingDropped > 0) {
				lines.Append(logline(Loc.T("wsdbg.dropped", pendingDropped)));
				pendingDropped = 0;
			}
		}
		if (n > 0) {
			eclog.AppendText(lines.ToString());
			if (ccautoscroll.IsChecked == true) eclog.ScrollToEnd();
		}

		var slines = new StringBuilder();
		var m = 0;
		lock (pgate) {
			while (spending.Count > 0 && m < 200) {
				slines.Append(spending.Dequeue());
				m++;
			}
			if (m > 0 && spendingDropped > 0) {
				slines.Append(logline(Loc.T("wsdbg.dropped", spendingDropped)));
				spendingDropped = 0;
			}
		}
		if (m > 0) {
			eslog.AppendText(slines.ToString());
			eslog.ScrollToEnd();
		}

		if (!string.IsNullOrEmpty(flash)) {
			lbstat.Text = flash;
			flash = "";
		}
		if (srv != null && srv.Running) updserverui();
	}

	static bool sameas(List<string> a, List<string> b) {
		if (a == null || b == null) return a == b;
		if (a.Count != b.Count) return false;
		for (var i = 0; i < a.Count; i++)
			if (a[i] != b[i]) return false;
		return true;
	}

	void say(string s) {
		lock (pgate) {
			if (pending.Count >= 4000) { pendingDropped++; return; }
			pending.Enqueue(logline(s));
		}
	}

	void sput(string s) {
		lock (pgate) {
			if (spending.Count >= 4000) { spendingDropped++; return; }
			spending.Enqueue(logline(s));
		}
	}

	static string stamp() => DateTime.Now.ToString("HH:mm:ss.fff");

	static string logline(string s) => stamp() + "  " + (s ?? "") + "\r\n";

	static int portof(TextBox box, int dflt) {
		var t = (box.Text ?? "").Trim();
		return int.TryParse(t, out var v) && v >= 0 && v <= 65535 ? v : dflt;
	}

	/// <summary>勾了 HEX 就按十六进制解，没勾直接用 UTF-8 文本。解不开返回 null。</summary>
	static byte[] payload(string text, bool hex) {
		if (!hex) return Encoding.UTF8.GetBytes(text ?? "");
		return WsProto.fromhex(text ?? "");
	}
}
