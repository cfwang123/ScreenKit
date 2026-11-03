using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → 网络工具 → UDP 调试。监听一个端口，把输入的报文发到指定地址。</summary>
public partial class UdpDebugWindow : Window {
	static readonly string[] Encs = ["UTF-8", "GB2312"];

	readonly List<UdpLine> log = new();
	UdpClient sock;
	CancellationTokenSource recvcts;
	DispatcherTimer timer;
	bool dead;

	sealed class UdpLine {
		public DateTime When;
		public bool Sent;
		public string Who;
		public byte[] Data;
	}

	public UdpDebugWindow() {
		InitializeComponent();
		eenc.ItemsSource = Encs;
		eenc.SelectedIndex = 0;
		applylang();
		chexshow.Checked += (_, _) => restyle();
		chexshow.Unchecked += (_, _) => restyle();
		eenc.SelectionChanged += (_, _) => restyle();
		blisten.Click += (_, _) => togglelisten();
		bsend.Click += (_, _) => send();
		bclear.Click += (_, _) => clearlog();
		ctimer.Checked += (_, _) => timeron(true);
		ctimer.Unchecked += (_, _) => timeron(false);
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
		timer.Tick += (_, _) => send();
		WindowEsc.Attach(this);
		Closed += (_, _) => shutdown();
	}

	void applylang() {
		Title = Loc.T("udpdbg.title");
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
		if (log.Count == 0) lbstat.Text = Loc.T("udpdbg.idle");
	}

	void listenlabel() =>
		ToolBtnUi.Set(blisten, sock == null ? ToolBtnUi.Play : ToolBtnUi.Cancel,
			Loc.T(sock == null ? "udpdbg.listen" : "udpdbg.stop"));

	Encoding encof() =>
		eenc.SelectedIndex == 1 ? Encoding.GetEncoding(936) : Encoding.UTF8;

	void togglelisten() {
		if (sock != null) { stoplisten(); return; }
		int port;
		try { port = UdpDebug.LocalPort(elocal.Text); }
		catch (FormatException) { say(Loc.T("udpdbg.badport")); return; }
		try {
			sock = new UdpClient(new IPEndPoint(IPAddress.Any, port));
		}
		catch (Exception ex) { say(Loc.T("udpdbg.bindfail", ex.Message)); return; }
		var bound = ((IPEndPoint)sock.Client.LocalEndPoint).Port;
		elocal.Text = bound.ToString();
		elocal.IsEnabled = false;
		recvcts = new CancellationTokenSource();
		var token = recvcts.Token;
		_ = Task.Run(() => recvloop(sock, token));
		listenlabel();
		say(Loc.T("udpdbg.listening", bound));
	}

	void stoplisten() {
		try { recvcts?.Cancel(); } catch { }
		try { sock?.Close(); } catch { }
		try { sock?.Dispose(); } catch { }
		sock = null;
		recvcts = null;
		elocal.IsEnabled = true;
		listenlabel();
		if (ctimer.IsChecked == true) { ctimer.IsChecked = false; }
		say(Loc.T("udpdbg.stopped"));
	}

	void recvloop(UdpClient client, CancellationToken token) {
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

	void send() {
		if (sock == null) { say(Loc.T("udpdbg.needlisten")); return; }
		IPEndPoint peer;
		byte[] data;
		try {
			peer = UdpDebug.Peer(epeer.Text, UdpDebug.LocalPort(epeerport.Text));
			data = UdpDebug.Parse(epayload.Text ?? "", chexsend.IsChecked == true, encof());
		}
		catch (FormatException) { say(Loc.T("udpdbg.badinput")); return; }
		if (data.Length == 0) { say(Loc.T("udpdbg.empty")); return; }
		try { sock.Send(data, data.Length, peer); }
		catch (Exception ex) { say(Loc.T("udpdbg.sendfail", ex.Message)); return; }
		append(true, data, peer.ToString());
	}

	void timeron(bool on) {
		if (!on) { timer.Stop(); return; }
		int ms;
		try { ms = UdpDebug.Interval(einterval.Text); }
		catch (FormatException) { ctimer.IsChecked = false; say(Loc.T("udpdbg.badinterval")); return; }
		if (sock == null) { ctimer.IsChecked = false; say(Loc.T("udpdbg.needlisten")); return; }
		timer.Interval = TimeSpan.FromMilliseconds(ms);
		timer.Start();
		send();
	}

	void append(bool sent, byte[] data, string who) {
		log.Add(new UdpLine { When = DateTime.Now, Sent = sent, Who = who, Data = data });
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

	void shutdown() {
		dead = true;
		try { timer?.Stop(); } catch { }
		try { recvcts?.Cancel(); } catch { }
		try { sock?.Close(); } catch { }
		try { sock?.Dispose(); } catch { }
		sock = null;
	}
}
