using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → 网络工具 → TCP/UDP 调试。UDP 发报文，TCP 先连接再发，两边都能监听。</summary>
public partial class UdpDebugWindow : Window {
	static readonly string[] Encs = ["UTF-8", "GB2312"];

	readonly List<SockLine> log = new();
	readonly List<TcpClient> clients = new();
	UdpClient udpsock;
	TcpListener listener;
	TcpClient tcp;
	CancellationTokenSource recvcts;
	DispatcherTimer timer;
	bool dead;

	sealed class SockLine {
		public DateTime When;
		public bool Sent;
		public string Who;
		public byte[] Data;
	}

	public UdpDebugWindow() {
		InitializeComponent();
		eenc.ItemsSource = Encs;
		eenc.SelectedIndex = 0;
		loadlast();
		applylang();
		applyproto();
		chexshow.Checked += (_, _) => restyle();
		chexshow.Unchecked += (_, _) => restyle();
		eenc.SelectionChanged += (_, _) => restyle();
		rudp.Checked += (_, _) => onproto();
		rtcp.Checked += (_, _) => onproto();
		blisten.Click += (_, _) => togglelisten();
		bsend.Click += (_, _) => send();
		bconnect.Click += (_, _) => toggleconnect();
		bclear.Click += (_, _) => clearlog();
		ctimer.Checked += (_, _) => timeron(true);
		ctimer.Unchecked += (_, _) => timeron(false);
		epeer.LostFocus += (_, _) => savelast();
		epeerport.LostFocus += (_, _) => savelast();
		elocal.LostFocus += (_, _) => savelast();
		epayload.LostFocus += (_, _) => savelast();
		einterval.LostFocus += (_, _) => savelast();
		chexshow.Checked += (_, _) => savelast();
		chexshow.Unchecked += (_, _) => savelast();
		chexsend.Checked += (_, _) => savelast();
		chexsend.Unchecked += (_, _) => savelast();
		creply.Checked += (_, _) => savelast();
		creply.Unchecked += (_, _) => savelast();
		cscroll.Checked += (_, _) => savelast();
		cscroll.Unchecked += (_, _) => savelast();
		eenc.SelectionChanged += (_, _) => savelast();
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
		timer.Tick += (_, _) => sendnow();
		WindowEsc.Attach(this);
		Closed += (_, _) => shutdown();
	}

	bool istcp() => rtcp.IsChecked == true;

	bool listening() => udpsock != null || listener != null;

	void loadlast() {
		var st = UdpDebug.LoadLast();
		epeer.Text = st.Peer ?? "";
		epeerport.Text = st.PeerPort ?? "";
		elocal.Text = st.LocalPort ?? "";
		epayload.Text = st.Payload ?? "";
		if (!string.IsNullOrWhiteSpace(st.Interval)) einterval.Text = st.Interval;
		chexshow.IsChecked = st.HexShow;
		chexsend.IsChecked = st.HexSend;
		creply.IsChecked = st.Reply;
		cscroll.IsChecked = st.Scroll;
		if (st.Enc >= 0 && st.Enc < Encs.Length) eenc.SelectedIndex = st.Enc;
		rtcp.IsChecked = st.Proto == 1;
		rudp.IsChecked = st.Proto != 1;
	}

	void savelast() {
		UdpDebug.SaveLast(new UdpDebug.Last {
			Peer = epeer.Text ?? "",
			PeerPort = epeerport.Text ?? "",
			LocalPort = elocal.Text ?? "",
			Payload = epayload.Text ?? "",
			Interval = einterval.Text ?? "",
			HexShow = chexshow.IsChecked == true,
			HexSend = chexsend.IsChecked == true,
			Reply = creply.IsChecked == true,
			Scroll = cscroll.IsChecked == true,
			Enc = eenc.SelectedIndex,
			Proto = istcp() ? 1 : 0,
		});
	}

	void applylang() {
		Title = Loc.T("udpdbg.title");
		rudp.Content = Loc.T("udpdbg.proto.udp");
		rtcp.Content = Loc.T("udpdbg.proto.tcp");
		chexshow.Content = Loc.T("udpdbg.hexshow");
		lbenc.Text = Loc.T("udpdbg.enc");
		cscroll.Content = Loc.T("udpdbg.scroll");
		lbpeer.Text = Loc.T("udpdbg.peer");
		lbpeerport.Text = Loc.T("udpdbg.port");
		lblocal.Text = Loc.T("udpdbg.local");
		chexsend.Content = Loc.T("udpdbg.hexsend");
		ctimer.Content = Loc.T("udpdbg.timer");
		lbms.Text = Loc.T("udpdbg.ms");
		creply.Content = Loc.T("udpdbg.reply");
		epayload.ToolTip = Loc.T("udpdbg.payload.tip");
		ToolBtnUi.Set(bclear, ToolBtnUi.Clear, Loc.T("udpdbg.clear"));
		ToolBtnUi.Set(bsend, ToolBtnUi.Play, Loc.T("udpdbg.send"));
		listenlabel();
		connectlabel();
		if (log.Count == 0) lbstat.Text = Loc.T("udpdbg.idle");
	}

	/// <summary>TCP 显示连接/断开和监听，UDP 显示发送和监听。</summary>
	void applyproto() {
		var tcp = istcp();
		bsend.Visibility = tcp ? Visibility.Collapsed : Visibility.Visible;
		bconnect.Visibility = tcp ? Visibility.Visible : Visibility.Collapsed;
		rudp.IsEnabled = !busy();
		rtcp.IsEnabled = !busy();
	}

	bool busy() => listening() || tcp != null;

	void onproto() {
		if (rudp.IsChecked != true && rtcp.IsChecked != true) return;
		applyproto();
		savelast();
	}

	void listenlabel() =>
		ToolBtnUi.Set(blisten, listening() ? ToolBtnUi.Cancel : ToolBtnUi.Play,
			Loc.T(listening() ? "udpdbg.stop" : "udpdbg.listen"));

	void connectlabel() =>
		ToolBtnUi.Set(bconnect, tcp == null ? ToolBtnUi.Play : ToolBtnUi.Cancel,
			Loc.T(tcp == null ? "udpdbg.connect" : "udpdbg.disconnect"));

	Encoding encof() =>
		eenc.SelectedIndex == 1 ? Encoding.GetEncoding(936) : Encoding.UTF8;

	void togglelisten() {
		if (listening()) { stoplisten(); return; }
		int port;
		try { port = UdpDebug.LocalPort(elocal.Text); }
		catch (FormatException) { say(Loc.T("udpdbg.badport")); return; }
		if (istcp()) listentcp(port);
		else listenudp(port);
	}

	void listenudp(int port) {
		try { udpsock = new UdpClient(new IPEndPoint(IPAddress.Any, port)); }
		catch (Exception ex) { say(Loc.T("udpdbg.bindfail", ex.Message)); return; }
		var bound = ((IPEndPoint)udpsock.Client.LocalEndPoint).Port;
		boundlisten(bound);
		_ = Task.Run(() => udploop(udpsock, recvcts.Token));
	}

	void listentcp(int port) {
		TcpListener srv = null;
		try {
			srv = new TcpListener(IPAddress.Any, port);
			srv.Start();
			listener = srv;
		}
		catch (Exception ex) {
			try { srv?.Stop(); } catch { }
			listener = null;
			say(Loc.T("udpdbg.bindfail", ex.Message));
			return;
		}
		var bound = ((IPEndPoint)listener.LocalEndpoint).Port;
		boundlisten(bound);
		_ = Task.Run(() => tcploop(listener, recvcts.Token));
	}

	void boundlisten(int bound) {
		elocal.Text = bound.ToString();
		elocal.IsEnabled = false;
		recvcts = new CancellationTokenSource();
		listenlabel();
		applyproto();
		say(Loc.T("udpdbg.listening", bound));
	}

	void stoplisten() {
		try { recvcts?.Cancel(); } catch { }
		closeudp();
		closetcp(false);
		recvcts = null;
		elocal.IsEnabled = true;
		listenlabel();
		applyproto();
		if (ctimer.IsChecked == true) ctimer.IsChecked = false;
		say(Loc.T("udpdbg.stopped"));
	}

	void udploop(UdpClient client, CancellationToken token) {
		while (!token.IsCancellationRequested) {
			IPEndPoint from;
			byte[] data;
			try {
				from = new IPEndPoint(IPAddress.Any, 0);
				data = client.Receive(ref from);
			}
			catch { break; }
			if (token.IsCancellationRequested) break;
			var who = from.ToString();
			byte[] reply = null;
			try {
				Dispatcher.Invoke(() => {
					append(false, data, who);
					if (dead || creply.IsChecked != true) return;
					reply = UdpDebug.Parse(epayload.Text ?? "", chexsend.IsChecked == true, encof());
				});
			}
			catch { reply = null; }
			if (reply == null || reply.Length == 0) continue;
			try { client.Send(reply, reply.Length, from); }
			catch { continue; }
			try { Dispatcher.Invoke(() => { if (!dead) append(true, reply, who); }); }
			catch { }
		}
	}

	void tcploop(TcpListener srv, CancellationToken token) {
		while (!token.IsCancellationRequested) {
			TcpClient got;
			try { got = srv.AcceptTcpClient(); }
			catch { break; }
			lock (clients) clients.Add(got);
			var who = "";
			try { who = got.Client.RemoteEndPoint?.ToString() ?? ""; } catch { }
			try { Dispatcher.Invoke(() => { if (!dead) say(Loc.T("udpdbg.accepted", who)); }); }
			catch { }
			var held = got;
			_ = Task.Run(() => readclient(held, who, token));
		}
	}

	void readclient(TcpClient client, string who, CancellationToken token) {
		var buf = new byte[65536];
		try {
			var stream = client.GetStream();
			while (!token.IsCancellationRequested) {
				var n = stream.Read(buf, 0, buf.Length);
				if (n <= 0) break;
				var data = new byte[n];
				Buffer.BlockCopy(buf, 0, data, 0, n);
				byte[] reply = null;
				try {
					Dispatcher.Invoke(() => {
						if (dead) return;
						append(false, data, who);
						if (creply.IsChecked != true) return;
						reply = UdpDebug.Parse(epayload.Text ?? "", chexsend.IsChecked == true, encof());
					});
				}
				catch { break; }
				if (reply == null || reply.Length == 0) continue;
				try { stream.Write(reply, 0, reply.Length); }
				catch { break; }
				try { Dispatcher.Invoke(() => { if (!dead) append(true, reply, who); }); }
				catch { break; }
			}
		}
		catch { }
		lock (clients) clients.Remove(client);
		try { client.Close(); } catch { }
		try { Dispatcher.Invoke(() => { if (!dead) say(Loc.T("udpdbg.closed", who)); }); }
		catch { }
	}

	void toggleconnect() {
		if (tcp != null) { dropconnect(); return; }
		IPEndPoint peer;
		try { peer = UdpDebug.Peer(epeer.Text, UdpDebug.LocalPort(epeerport.Text)); }
		catch (FormatException) { say(Loc.T("udpdbg.badinput")); return; }
		bconnect.IsEnabled = false;
		say(Loc.T("udpdbg.connecting", peer));
		_ = Task.Run(() => {
			TcpClient got = null;
			string err = null;
			try {
				got = new TcpClient();
				got.Connect(peer.Address, peer.Port);
			}
			catch (Exception ex) { err = ex.Message; try { got?.Close(); } catch { } got = null; }
			try {
				Dispatcher.Invoke(() => {
					bconnect.IsEnabled = true;
					if (dead) { try { got?.Close(); } catch { } return; }
					if (got == null) { say(Loc.T("udpdbg.connectfail", err)); return; }
					tcp = got;
					epeer.IsEnabled = false;
					epeerport.IsEnabled = false;
					connectlabel();
					applyproto();
					say(Loc.T("udpdbg.connected", peer));
					_ = Task.Run(() => readtcp(tcp));
				});
			}
			catch { try { got?.Close(); } catch { } }
		});
	}

	void readtcp(TcpClient client) {
		var who = "";
		try { who = client.Client.RemoteEndPoint?.ToString() ?? ""; } catch { }
		var buf = new byte[65536];
		try {
			var stream = client.GetStream();
			while (!dead) {
				var n = stream.Read(buf, 0, buf.Length);
				if (n <= 0) break;
				var data = new byte[n];
				Buffer.BlockCopy(buf, 0, data, 0, n);
				byte[] reply = null;
				try {
					Dispatcher.Invoke(() => {
						if (dead || tcp != client) return;
						append(false, data, who);
						if (creply.IsChecked != true) return;
						reply = UdpDebug.Parse(epayload.Text ?? "", chexsend.IsChecked == true, encof());
					});
				}
				catch { break; }
				if (reply == null || reply.Length == 0) continue;
				try { stream.Write(reply, 0, reply.Length); }
				catch { break; }
				try { Dispatcher.Invoke(() => { if (!dead && tcp == client) append(true, reply, who); }); }
				catch { break; }
			}
		}
		catch { }
		try {
			Dispatcher.Invoke(() => {
				if (dead || tcp != client) return;
				dropconnect();
			});
		}
		catch { }
	}

	void dropconnect() {
		closetcp(true);
		epeer.IsEnabled = true;
		epeerport.IsEnabled = true;
		connectlabel();
		applyproto();
		if (ctimer.IsChecked == true) ctimer.IsChecked = false;
		say(Loc.T("udpdbg.disconnected"));
	}

	void send() {
		byte[] data;
		try { data = UdpDebug.Parse(epayload.Text ?? "", chexsend.IsChecked == true, encof()); }
		catch (FormatException) { say(Loc.T("udpdbg.badinput")); return; }
		if (data.Length == 0) { say(Loc.T("udpdbg.empty")); return; }
		if (istcp()) sendtcp(data);
		else sendudp(data);
	}

	/// <summary>TCP 连上或有人接入后，把当前报文发出去。连接按钮不发报文。</summary>
	void sendnow() {
		if (!istcp()) { send(); return; }
		if (tcp == null && clients.Count == 0) { say(Loc.T("udpdbg.needconnect")); return; }
		send();
	}

	void sendudp(byte[] data) {
		if (udpsock == null) { say(Loc.T("udpdbg.needlisten")); return; }
		IPEndPoint peer;
		try { peer = UdpDebug.Peer(epeer.Text, UdpDebug.LocalPort(epeerport.Text)); }
		catch (FormatException) { say(Loc.T("udpdbg.badinput")); return; }
		try { udpsock.Send(data, data.Length, peer); }
		catch (Exception ex) { say(Loc.T("udpdbg.sendfail", ex.Message)); return; }
		append(true, data, peer.ToString());
	}

	void sendtcp(byte[] data) {
		var targets = new List<(TcpClient cli, string who)>();
		if (tcp != null) targets.Add((tcp, remoteof(tcp)));
		lock (clients) {
			foreach (var cli in clients) targets.Add((cli, remoteof(cli)));
		}
		if (targets.Count == 0) { say(Loc.T("udpdbg.needconnect")); return; }
		var sent = 0;
		foreach (var it in targets) {
			try { it.cli.GetStream().Write(data, 0, data.Length); }
			catch { continue; }
			append(true, data, it.who);
			sent++;
		}
		if (sent == 0) say(Loc.T("udpdbg.sendfail", ""));
	}

	static string remoteof(TcpClient cli) {
		try { return cli.Client.RemoteEndPoint?.ToString() ?? ""; }
		catch { return ""; }
	}

	void timeron(bool on) {
		if (!on) { timer.Stop(); return; }
		int ms;
		try { ms = UdpDebug.Interval(einterval.Text); }
		catch (FormatException) { ctimer.IsChecked = false; say(Loc.T("udpdbg.badinterval")); return; }
		if (!istcp() && udpsock == null) { ctimer.IsChecked = false; say(Loc.T("udpdbg.needlisten")); return; }
		if (istcp() && tcp == null && clients.Count == 0) {
			ctimer.IsChecked = false;
			say(Loc.T("udpdbg.needconnect"));
			return;
		}
		timer.Interval = TimeSpan.FromMilliseconds(ms);
		timer.Start();
		sendnow();
	}

	void append(bool sent, byte[] data, string who) {
		log.Add(new SockLine { When = DateTime.Now, Sent = sent, Who = who, Data = data });
		var head = UdpDebug.Head(log[log.Count - 1].When, sent, data.Length, who);
		paint();
		say(head);
	}

	/// <summary>显示方式变了就把已有报文按新方式重画。</summary>
	void restyle() => paint();

	void paint() {
		var enc = encof();
		var hex = chexshow.IsChecked == true;
		var sb = new StringBuilder();
		foreach (var line in log) {
			if (sb.Length > 0) sb.Append('\n');
			sb.Append(UdpDebug.Head(line.When, line.Sent, line.Data.Length, line.Who));
			sb.Append(' ');
			sb.Append(UdpDebug.Show(line.Data, hex, enc));
		}
		elog.Text = sb.ToString();
		if (cscroll.IsChecked == true) elog.ScrollToEnd();
	}

	void clearlog() {
		log.Clear();
		elog.Clear();
		say(Loc.T("udpdbg.cleared"));
	}

	void say(string text) => lbstat.Text = text ?? "";

	void closeudp() {
		try { udpsock?.Close(); } catch { }
		try { udpsock?.Dispose(); } catch { }
		udpsock = null;
	}

	void closetcp(bool client) {
		if (!client) {
			try { listener?.Stop(); } catch { }
			listener = null;
			List<TcpClient> held;
			lock (clients) { held = clients.ToList(); clients.Clear(); }
			foreach (var cli in held) try { cli.Close(); } catch { }
		}
		if (client || tcp != null) {
			try { tcp?.Close(); } catch { }
			try { tcp?.Dispose(); } catch { }
			tcp = null;
		}
	}

	void shutdown() {
		dead = true;
		savelast();
		try { timer?.Stop(); } catch { }
		try { recvcts?.Cancel(); } catch { }
		closeudp();
		closetcp(false);
		closetcp(true);
	}
}
