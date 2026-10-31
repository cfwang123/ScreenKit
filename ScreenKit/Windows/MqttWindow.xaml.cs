using System.Text;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 工具 → 网络 → MQTT 调试。左边是客户端（连别人的 broker，发/收），右边标签页是服务端（本机起一个 broker）。
/// 报文全自己拼，不依赖 MQTT 库。
/// </summary>
public partial class MqttWindow : Window {
	readonly MqttClient cli = new();
	MqttBroker srv;
	readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
	/// <summary>日志先塞队列，定时器统一刷到界面，免得高频消息把 UI 拖死。</summary>
	readonly Queue<string> pending = new();
	readonly object pgate = new();
	int pendingDropped;
	/// <summary>状态栏插队消息（连接结果之类），下个 tick 显示。</summary>
	string flash = "";

	public MqttWindow() {
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
			try { cli.Dispose(); } catch { }
			try { srv?.Dispose(); } catch { }
		};
	}

	void applylang() {
		Title = Loc.T("mqtt.title");
		lbhint.Text = Loc.T("mqtt.hint");
		tabclient.Header = Loc.T("mqtt.tab.client");
		tabserver.Header = Loc.T("mqtt.tab.server");
		lbchost.Text = Loc.T("mqtt.host");
		lbcport.Text = Loc.T("mqtt.port");
		lbcuser.Text = Loc.T("mqtt.user");
		lbcpw.Text = Loc.T("mqtt.pw");
		lbctopic.Text = Loc.T("mqtt.subtopic");
		ectopic.ToolTip = Loc.T("mqtt.subtopic.tip");
		lbpubtopic.Text = Loc.T("mqtt.pubtopic");
		epubtopic.ToolTip = Loc.T("mqtt.pubtopic.tip");
		lbqos.Text = Loc.T("mqtt.qos");
		lbpayload.Text = Loc.T("mqtt.payload");
		cretain.Content = Loc.T("mqtt.retain");
		chexsend.Content = Loc.T("mqtt.hexsend");
		cautoscroll.Content = Loc.T("mqtt.autoscroll");
		bcsub.Content = Loc.T("mqtt.subscribe");
		bcunsub.Content = Loc.T("mqtt.unsubscribe");
		bcpub.Content = Loc.T("mqtt.publish");
		bcclear.Content = Loc.T("mqtt.clear");
		bccopy.Content = Loc.T("mqtt.copy");
		bcclose.Content = Loc.T("mqtt.disconnect");
		lbsport.Text = Loc.T("mqtt.port");
		lbclients.Text = Loc.T("mqtt.clients");
		lbslog.Text = Loc.T("mqtt.server.log");
		bsclear.Content = Loc.T("mqtt.clear");
		lbptopic.Text = Loc.T("mqtt.pubtopic");
		lbpqos.Text = Loc.T("mqtt.qos");
		lbppayload.Text = Loc.T("mqtt.payload");
		bspub.Content = Loc.T("mqtt.publish");
		updconnui();
	}

	/// <summary>按连接状态刷新按钮文字和可用性。</summary>
	void updconnui() {
		var on = cli.Connected;
		bcconn.Content = on ? Loc.T("mqtt.disconnect") : Loc.T("mqtt.connect");
		bcclose.IsEnabled = on;
		bcsub.IsEnabled = on;
		bcunsub.IsEnabled = on;
		bcpub.IsEnabled = on;
		echost.IsEnabled = ecport.IsEnabled = ecuser.IsEnabled = ecpw.IsEnabled = !on;
	}

	void initui() {
		eqos.Items.Clear();
		eqos.Items.Add("QoS 0");
		eqos.Items.Add("QoS 1");
		eqos.Items.Add("QoS 2");
		eqos.SelectedIndex = 0;

		bcconn.Click += (_, _) => {
			if (cli.Connected) { cli.Disconnect(); updconnui(); return; }
			var err = cli.Connect(echost.Text, portof(ecport, 1883), "", ecuser.Text, ecpw.Password, 60);
			if (err.Length > 0) {
				log("[连接失败] " + err);
				flash = Loc.T("mqtt.fail", err);
			}
			else {
				// 照 COMNET 习惯：连上就顺手订阅输入框里的主题
				var t = (ectopic.Text ?? "").Trim();
				if (t.Length > 0) cli.Subscribe(new List<string> { t }, qosof(eqos));
				flash = Loc.T("mqtt.connected");
			}
			updconnui();
		};

		bcsub.Click += (_, _) => {
			var t = (ectopic.Text ?? "").Trim();
			if (t.Length == 0) { flash = Loc.T("mqtt.needtopic"); return; }
			cli.Subscribe(new List<string> { t }, qosof(eqos));
			log("[订阅] " + t);
			flash = Loc.T("mqtt.subscribed", t);
		};

		bcunsub.Click += (_, _) => {
			var t = (ectopic.Text ?? "").Trim();
			if (t.Length == 0) { flash = Loc.T("mqtt.needtopic"); return; }
			cli.Unsubscribe(new List<string> { t });
			log("[取消订阅] " + t);
		};

		bcpub.Click += (_, _) => {
			var t = (epubtopic.Text ?? "").Trim();
			if (t.Length == 0) { flash = Loc.T("mqtt.needtopic"); return; }
			var body = payloadbytes(epayload.Text, chexsend.IsChecked == true);
			if (body == null) { flash = Loc.T("mqtt.badhex"); return; }
			if (!cli.Publish(t, body, qosof(eqos), cretain.IsChecked == true, out var err)) {
				log("[发送失败] " + err);
				flash = Loc.T("mqtt.fail", err);
			}
		};

		bcclear.Click += (_, _) => elog.Text = "";
		bccopy.Click += (_, _) => {
			try { Clipboard.SetText(elog.Text ?? ""); }
			catch { }
		};
		bcclose.Click += (_, _) => {
			cli.Disconnect();
			updconnui();
		};

		cli.Log += s => enqueue(s);
		cli.Lost += why => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
			updconnui();
			if (!string.IsNullOrEmpty(why)) flash = Loc.T("mqtt.lost", why);
		}));
		// 收到的消息按 COMNET 的样子标上主题
		cli.Message += (topic, body, qos, retain) => {
			var head = $"MQTT[TOPIC:{topic}]  QoS{qos}{(retain ? " 保留" : "")}  ";
			enqueue(head + MqttProto.pretty(body));
		};
	}

	void initserver() {
		epqos.Items.Clear();
		epqos.Items.Add("QoS 0");
		epqos.Items.Add("QoS 1");
		epqos.Items.Add("QoS 2");
		epqos.SelectedIndex = 0;

		bstoggle.Click += (_, _) => toggleserver();
		bspub.Click += (_, _) => {
			if (srv == null || !srv.Running) { flash = Loc.T("mqtt.server.notrunning"); return; }
			var t = (eptopic.Text ?? "").Trim();
			if (t.Length == 0) { flash = Loc.T("mqtt.needtopic"); return; }
			var body = Encoding.UTF8.GetBytes(eppayload.Text ?? "");
			var n = srv.Publish(t, body, qosof(epqos), false);
			flash = Loc.T("mqtt.server.published", n);
		};
		bsclear.Click += (_, _) => eslog.Text = "";
		updserverui();
	}

	void toggleserver() {
		if (srv != null && srv.Running) {
			try { srv.Dispose(); } catch { }
			srv = null;
			eslog.AppendText(logline("服务端已停止"));
			updserverui();
			return;
		}
		var port = portof(esport, 1883);
		var b = new MqttBroker(port);
		// 事件只挂一次：重启时会换新的 broker 对象，旧的别再往里写
		b.Log += s => {
			var line = logline(s);
			Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
				eslog.AppendText(line);
				eslog.ScrollToEnd();
			}));
		};
		try {
			b.Start();
			srv = b;
			esport.Text = srv.Port.ToString();
		}
		catch (Exception ex) {
			try { b.Dispose(); } catch { }
			eslog.AppendText(logline("启动失败：" + ex.Message));
			flash = Loc.T("mqtt.fail", ex.Message);
		}
		updserverui();
	}

	void updserverui() {
		var on = srv != null && srv.Running;
		bstoggle.Content = on ? Loc.T("mqtt.server.stop") : Loc.T("mqtt.server.start");
		esport.IsEnabled = !on;
		bspub.IsEnabled = on;
		lbsstat.Text = on ? Loc.T("mqtt.server.running", srv.Port) : Loc.T("mqtt.server.stopped");
		lclients.ItemsSource = null;
	}

	/// <summary>默认连本机：一般就是本窗口服务端页起的那个 broker，开箱能自测回环。</summary>
	void inithost() {
		echost.Text = "127.0.0.1";
		ecport.Text = "1883";
		ectopic.Text = "test/#";
		epubtopic.Text = "test/hello";
		epayload.Text = "hello";
		esport.Text = "1883";
		eptopic.Text = "test/hello";
		eppayload.Text = "hello";
	}

	/// <summary>定时器：刷日志、刷客户端列表、刷状态栏。</summary>
	void tick() {
		var lines = new StringBuilder();
		var n = 0;
		lock (pgate) {
			while (pending.Count > 0 && n < 200) {
				lines.Append(pending.Dequeue());
				n++;
			}
		}
		if (n > 0) {
			if (pendingDropped > 0) {
				lines.Append(logline($"… 丢弃 {pendingDropped} 条（来得太快）"));
				pendingDropped = 0;
			}
			elog.AppendText(lines.ToString());
			if (cautoscroll.IsChecked == true) elog.ScrollToEnd();
		}
		else if (pendingDropped > 0 && elog.Text.Length > 0) {
			// 队列空但压过东西，补一句
			lock (pgate) {
				if (pending.Count == 0) {
					elog.AppendText(logline($"… 丢弃 {pendingDropped} 条（来得太快）"));
					pendingDropped = 0;
				}
			}
		}
		if (!string.IsNullOrEmpty(flash)) {
			lbstat.Text = flash;
			flash = "";
		}
		if (srv != null && srv.Running) {
			var list = srv.Clients();
			var text = new List<string>();
			foreach (var (id, subs) in list) text.Add($"{id}    {Loc.T("mqtt.subs", subs)}");
			if (!sameas(lclients.ItemsSource as List<string>, text)) lclients.ItemsSource = text;
		}
	}

	static bool sameas(List<string> a, List<string> b) {
		if (a == null || b == null) return a == b;
		if (a.Count != b.Count) return false;
		for (var i = 0; i < a.Count; i++)
			if (a[i] != b[i]) return false;
		return true;
	}

	/// <summary>日志入队。塞太多就丢，宁可少几条也不能把界面撑爆。</summary>
	void enqueue(string s) {
		lock (pgate) {
			if (pending.Count >= 4000) { pendingDropped++; return; }
			pending.Enqueue(logline(s));
		}
	}

	static string logline(string s) => DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n";

	void log(string s) => enqueue(s);

	static int portof(TextBox box, int dflt) {
		var t = (box.Text ?? "").Trim();
		return int.TryParse(t, out var v) && v > 0 && v <= 65535 ? v : dflt;
	}

	static int qosof(ComboBox box) => box.SelectedIndex < 0 ? 0 : box.SelectedIndex;

	/// <summary>勾了 HEX 就按十六进制解，没勾直接用 UTF-8 文本。解不开返回 null。</summary>
	static byte[] payloadbytes(string text, bool hex) {
		if (!hex) return Encoding.UTF8.GetBytes(text ?? "");
		var raw = MqttProto.fromhex(text ?? "");
		return raw;
	}
}
