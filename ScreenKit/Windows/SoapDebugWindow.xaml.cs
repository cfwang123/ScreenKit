using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenKit.Soap;

namespace ScreenKit;

/// <summary>工具 → SOAP 接口调试与模拟。</summary>
public partial class SoapDebugWindow : Window {
	readonly MockServer mock = new();
	MockConfig mockcfg = new();
	readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
	readonly List<Action> timerarr = new();
	int lastlogver = -1;

	WsdlSession session;
	Operation currentOp;
	readonly ObservableCollection<PNode> currentParts = new();
	readonly ObservableCollection<MockOpEntry> mockOps = new();
	MockOpEntry currentMockOp;
	bool mockdirty;
	bool mockcustom;
	bool loading;
	int nextsync;

	sealed class MockOpEntry {
		public Operation Op { get; set; }
		public string Scope { get; set; } = "";
		public override string ToString() => $"{Op?.Name}    ({Scope})";
	}

	public SoapDebugWindow() {
		InitializeComponent();
		fitToScreen();
		Net.init();
		applylang();
		initui();
		initmock();
		initTimer();
		loadRecent();
		Closed += (_, _) => {
			applyMockResponse();
			if (session != null) saveAutoMockConfig(session.Model.Location);
			mock.stop();
			timer.Stop();
		};
	}

	/// <summary>按工作区夹取窗口尺寸，避免小屏或高缩放时按钮跑到屏幕外。</summary>
	void fitToScreen() {
		var area = SystemParameters.WorkArea;
		if (area.Width < 200 || area.Height < 200) return;
		if (Width > area.Width) Width = Math.Max(900, area.Width * 0.96);
		if (Height > area.Height) Height = Math.Max(620, area.Height * 0.96);
		Left = Math.Max(0, (area.Width - Width) / 2);
		Top = Math.Max(0, (area.Height - Height) / 2);
	}

	void applylang() {
		Title = Loc.T("soapdbg.title");
		bLoad.Content = Loc.T("soapdbg.read");
		bBrowse.Content = Loc.T("soapdbg.browse");
		bDemo.Content = Loc.T("soapdbg.demo");
		cTrust.Content = Loc.T("soapdbg.trust");
		eUrl.ToolTip = Loc.T("soapdbg.wsdl.tip");
		bLoad.ToolTip = Loc.T("soapdbg.load.tip");
		bBrowse.ToolTip = Loc.T("soapdbg.browse.tip");
		bDemo.ToolTip = Loc.T("soapdbg.demo.tip");
		cTrust.ToolTip = Loc.T("soapdbg.trust.tip");
		tabDebug.Header = Loc.T("soapdbg.tab.debug");
		tabMock.Header = Loc.T("soapdbg.tab.mock");
		tabInput.Header = Loc.T("soapdbg.tab.input");
		tabRequest.Header = Loc.T("soapdbg.tab.request");
		tabWsdl.Header = Loc.T("soapdbg.tab.wsdl");
		tabResponse.Header = Loc.T("soapdbg.tab.response");
		tabTrace.Header = Loc.T("soapdbg.tab.trace");
		lbMethod.Text = Loc.T("soapdbg.nomethod");
		lbSoapAction.Text = Loc.T("soapdbg.action");
		lbAddress.Text = Loc.T("soapdbg.address");
		lbHeaders.Text = Loc.T("soapdbg.headers");
		eHeaders.ToolTip = Loc.T("soapdbg.headers.tip");
		lbTimeout.Text = Loc.T("soapdbg.timeout");
		lbResultTitle.Text = Loc.T("soapdbg.result");
		expAdvanced.Header = Loc.T("soapdbg.advanced");
		bInvoke.Content = Loc.T("soapdbg.invoke");
		bInvoke.ToolTip = Loc.T("soapdbg.invoke.tip");
		bResetValues.Content = Loc.T("soapdbg.clearvalues");
		lbPort.Text = Loc.T("soapdbg.port");
		bStartMock.Content = Loc.T("soapdbg.start");
		bStopMock.Content = Loc.T("soapdbg.stop");
		lbMockUrl.Text = Loc.T("soapdbg.serviceurl");
		bCopyMockUrl.Content = Loc.T("soapdbg.copy");
		bUseMockUrl.Content = Loc.T("soapdbg.use");
		lbMockOps.Text = Loc.T("soapdbg.mock.ops");
		lbMockResponse.Text = Loc.T("soapdbg.mock.response");
		lbMockOp.Text = Loc.T("soapdbg.mock.none");
		bMockDefault.Content = Loc.T("soapdbg.mock.default");
		bMockDefault.ToolTip = Loc.T("soapdbg.mock.default.tip");
		bMockRevert.Content = Loc.T("soapdbg.mock.revert");
		bMockSave.Content = Loc.T("soapdbg.mock.export");
		bMockLoad.Content = Loc.T("soapdbg.mock.import");
		eMockResponse.ToolTip = Loc.T("soapdbg.mock.response.tip");
		lbMockLogTitle.Text = Loc.T("soapdbg.mock.log");
		bMockClearLog.Content = Loc.T("soapdbg.mock.clear");
		if (string.IsNullOrEmpty(lbStatus.Text) || lbStatus.Text == "就绪")
			lbStatus.Text = Loc.T("soapdbg.ready");
	}

	void initui() {
		eUrl.Text = "http://";
		tvMethods.SelectedItemChanged += (_, e) => onSelectMethod(e.NewValue as TreeViewItem);
		bLoad.Click += (_, _) => loadWsdl();
		bBrowse.Click += (_, _) => browseWsdl();
		bDemo.Click += (_, _) => loadDemo();
		eUrl.KeyDown += (_, e) => {
			if (e.Key != Key.Enter) return;
			e.Handled = true;
			loadWsdl();
		};
		eFilter.TextChanged += (_, _) => rebuildMethodTree();
		bInvoke.Click += (_, _) => invoke();
		bResetValues.Click += (_, _) => {
			foreach (var p in currentParts) p.resetValues();
			refreshPreview();
		};
		eSoapAction.TextChanged += (_, _) => {
			if (currentOp != null) currentOp.SoapAction = eSoapAction.Text ?? "";
			refreshPreview();
		};
		eSoapVersion.SelectionChanged += (_, _) => refreshPreview();
		cTrust.Checked += (_, _) => Net.TrustAllCerts = true;
		cTrust.Unchecked += (_, _) => Net.TrustAllCerts = false;
		InputBindings.Add(new KeyBinding(new SoapRelayCommand(invoke), Key.F5, ModifierKeys.None));
	}

	void initTimer() {
		timer.Tick += (_, _) => {
			foreach (var a in timerarr.ToArray()) a();
		};
		timer.Start();
	}

	void every(int ms, Action action) {
		int next = 0;
		timerarr.Add(() => {
			if (Environment.TickCount - next < ms) return;
			next = Environment.TickCount;
			action();
		});
	}

	// ==================== WSDL 读取 ====================
	#region
	void browseWsdl() {
		var dlg = new OpenFileDialog {
			Filter = Loc.T("soapdbg.dlg.openwsdl"),
			Title = Loc.T("soapdbg.browse"),
		};
		if (dlg.ShowDialog() != true) return;
		eUrl.Text = dlg.FileName;
		loadWsdl();
	}

	void loadDemo() {
		try {
			string path = SampleWsdl.ensureLocal();
			eUrl.Text = path;
			loadWsdl();
		}
		catch (Exception ex) {
			setstatus(Loc.T("soapdbg.demo.fail", ex.Message));
		}
	}

	void loadWsdl() {
		string location = (eUrl.Text ?? "").Trim();
		if (string.IsNullOrEmpty(location) || location == "http://" || location == "https://") {
			setstatus(Loc.T("soapdbg.needwsdl"));
			return;
		}
		bool sample = SampleWsdl.isSample(location);
		if (sample) SampleWsdl.ensureLocal();
		bLoad.IsEnabled = false;
		setstatus(Loc.T("soapdbg.loading"));
		Mouse.OverrideCursor = Cursors.Wait;
		Task.Factory.StartNew(() => sample
				? WsdlSession.openText(SampleWsdl.text(), location)
				: WsdlSession.open(location))
			.ContinueWith(t => onWsdlLoaded(t, location), TaskScheduler.FromCurrentSynchronizationContext());
	}

	void onWsdlLoaded(Task<WsdlSession> task, string location) {
		bLoad.IsEnabled = true;
		Mouse.OverrideCursor = null;
		if (task.IsFaulted) {
			setstatus(Loc.T("soapdbg.loadfail", task.Exception?.GetBaseException().Message ?? "?"));
			return;
		}
		var s = task.Result;
		if (s == null || s.Error != null) {
			setstatus(Loc.T("soapdbg.loadfail", s?.Error ?? "?"));
			showWarnings(s);
			return;
		}
		applySession(s, location);
	}

	void applySession(WsdlSession s, string location) {
		currentOp = null;
		currentMockOp = null;
		mockcustom = false;
		currentParts.Clear();
		session = s;
		addRecent(location);
		mockcfg = new MockConfig();
		bool cfgLoaded = loadAutoMockConfig(location);
		if (!cfgLoaded && SampleWsdl.isSample(location)) seedDemoResponses();
		rebuildMethodTree();
		rebuildMockOps();
		lbMethod.Text = Loc.T("soapdbg.nomethod");
		eSoapAction.Text = "";
		eAddress.Text = "";
		eRequestXml.Text = "";
		eResponse.Text = "";
		eTrace.Text = "";
		lbTiming.Text = "";
		lbResponseInfo.Text = "";

		var first = tvMethods.Items.OfType<TreeViewItem>().SelectMany(i => i.Items.OfType<TreeViewItem>()).FirstOrDefault();
		if (first != null) first.IsSelected = true;

		setstatus(Loc.T("soapdbg.loaded", s.ops().Count(), s.Model.Endpoints.Count));
		showWarnings(s);
		eWsdlText.Text = buildWsdlText(s);
		if (SampleWsdl.isSample(location)) setstatus(Loc.T("soapdbg.samplehint"));
	}

	void seedDemoResponses() {
		foreach (var kv in SampleWsdl.defaultResponses()) mockcfg.set(kv.Key, kv.Value);
		mock.Resolver = name => mockcfg.get(name);
	}

	/// <summary>读取 WSDL 时自动带出同名配置（程序目录 soapmock 下）。</summary>
	bool loadAutoMockConfig(string location) {
		try {
			string cfgPath = AutoMockPath(location);
			if (cfgPath == null || !File.Exists(cfgPath)) return false;
			var cfg = MockConfig.load(cfgPath, out string error);
			if (error != null) return false;
			mockcfg = cfg;
			ePort.Text = cfg.Port.ToString();
			mock.Resolver = name => mockcfg.get(name);
			lbStatus.Text = Loc.T("soapdbg.mockloaded", cfg.Count, cfgPath);
			return true;
		}
		catch {
			return false;
		}
	}

	static string MockDir() {
		string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soapmock");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static string AutoMockPath(string location) {
		try {
			var uri = Net.toUri(location);
			if (uri == null) return null;
			string name = Path.GetFileName(uri.IsFile ? uri.LocalPath : uri.AbsolutePath);
			if (string.IsNullOrEmpty(name)) return null;
			return Path.Combine(MockDir(), SafeName(name) + ".mockcfg");
		}
		catch { return null; }
	}

	static string SafeName(string name) {
		foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
		return name;
	}

	void saveAutoMockConfig(string location) {
		try {
			string path = AutoMockPath(location);
			if (path == null) return;
			mockcfg.WsdlLocation = location;
			mockcfg.save(path);
		}
		catch { }
	}

	/// <summary>把已下载的 WSDL / XSD 文档拼成一份可审阅的文本。</summary>
	static string buildWsdlText(WsdlSession s) {
		var sb = new StringBuilder();
		foreach (var doc in s.Model.Docs) {
			sb.AppendLine($"<!-- ===== 文档：{doc.Url} =====" + (doc.IsWsdl ? "" : "（XSD）") + " -->");
			var parsed = SoapEnvelope.parse(doc.Text, out _);
			sb.AppendLine(parsed != null ? parsed.ToString() : doc.Text);
			sb.AppendLine();
		}
		return sb.ToString();
	}

	void showWarnings(WsdlSession s) {
		if (s?.Messages == null || s.Messages.Count == 0) return;
		lbStatus.Text = $"{lbStatus.Text}   |   {Loc.T("soapdbg.warn", s.Messages.Count, s.Messages[0])}";
	}

	void rebuildMethodTree() {
		tvMethods.Items.Clear();
		if (session == null) return;
		string filter = (eFilter.Text ?? "").Trim();
		foreach (var ep in session.Model.Endpoints) {
			var ops = ep.Ops.Where(o => filter.Length == 0 || o.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			if (ops.Count == 0) continue;
			var node = new TreeViewItem { Header = ep.ToString(), IsExpanded = true };
			foreach (var op in ops) {
				string tip = string.IsNullOrEmpty(op.SoapAction) ? ep.Address : $"{op.SoapAction}\n{ep.Address}";
				node.Items.Add(new TreeViewItem { Header = op.Name, Tag = op, ToolTip = tip });
			}
			tvMethods.Items.Add(node);
		}
	}

	void onSelectMethod(TreeViewItem item) {
		if (item?.Tag is not Operation op) return;
		currentOp = op;
		lbMethod.Text = $"{op.Endpoint.ServiceName} / {op.Endpoint.PortName} / {op.Name}";
		eSoapAction.Text = op.SoapAction ?? "";
		eSoapVersion.SelectedIndex = op.SoapVersionValue == "1.2" ? 1 : 0;
		if (!string.IsNullOrEmpty(op.Endpoint.Address)) eAddress.Text = op.Endpoint.Address;

		currentParts.Clear();
		foreach (var p in session.buildInput(op)) {
			watch(p);
			currentParts.Add(p);
		}
		tvInput.ItemsSource = currentParts;
		refreshPreview();
	}

	void watch(PNode node) {
		node.PropertyChanged += (_, e) => {
			if (e.PropertyName == nameof(PNode.Value) || e.PropertyName == nameof(PNode.Occurs)) refreshPreview();
		};
		foreach (var c in node.Children) watch(c);
	}
	#endregion

	// ==================== 报文预览与调用 ====================
	#region
	string effectiveVersion() => eSoapVersion.SelectedIndex == 1 ? "1.2" : "1.1";

	void refreshPreview() {
		if (currentOp == null) { eRequestXml.Text = ""; return; }
		currentOp.SoapVersionOverride = effectiveVersion();
		try {
			eRequestXml.Text = SoapEnvelope.buildRequest(currentOp, currentParts);
		}
		catch (Exception ex) {
			eRequestXml.Text = Loc.T("soapdbg.requestfail", ex.Message);
		}
	}

	void invoke() {
		if (currentOp == null) { setstatus(Loc.T("soapdbg.nomethodsel")); return; }
		string address = (eAddress.Text ?? "").Trim();
		if (string.IsNullOrEmpty(address)) { setstatus(Loc.T("soapdbg.noaddress")); return; }
		refreshPreview();
		var op = currentOp;
		string body = eRequestXml.Text;
		string action = (eSoapAction.Text ?? "").Trim();
		string headers = eHeaders.Text ?? "";
		int timeout = int.TryParse((eTimeout.Text ?? "").Trim(), out int t) && t > 0 ? t : 60000;
		Net.TrustAllCerts = cTrust.IsChecked == true;
		bInvoke.IsEnabled = false;
		setstatus(Loc.T("soapdbg.calling"));
		Task.Factory.StartNew(() => SoapClient.call(op, address, body, action, null, timeout, headers))
			.ContinueWith(t => onCalled(t), TaskScheduler.FromCurrentSynchronizationContext());
	}

	void onCalled(Task<CallResult> task) {
		bInvoke.IsEnabled = true;
		if (task.IsFaulted) {
			setstatus(Loc.T("soapdbg.callfail", task.Exception?.GetBaseException().Message ?? "?"));
			return;
		}
		showResult(task.Result);
	}

	void showResult(CallResult r) {
		if (r == null) { setstatus(Loc.T("soapdbg.callnoresult")); return; }
		lbTiming.Text = r.StatusCode != 0
			? Loc.T("soapdbg.httptiming", r.StatusCode, r.StatusText, r.ElapsedMs)
			: Loc.T("soapdbg.timing", r.ElapsedMs);
		eTrace.Text = r.Dump();
		if (!string.IsNullOrWhiteSpace(r.ResponseBody)) {
			eResponse.Text = pretty(r.ResponseBody);
			string info;
			if (SoapEnvelope.tryGetFault(r.ResponseBody, out string code, out string msg))
				info = Loc.T("soapdbg.fault", code ?? "", msg ?? "");
			else {
				var body = SoapEnvelope.firstBodyElement(r.ResponseBody);
				info = body == null ? "" : Loc.T("soapdbg.return", snippet(body.Value));
			}
			lbResponseInfo.Text = info;
		}
		else {
			eResponse.Text = r.Error ?? "";
			lbResponseInfo.Text = r.Error != null ? Loc.T("soapdbg.requestfail", r.Error) : "";
		}
		setstatus(r.Ok ? Loc.T("soapdbg.called", r.ElapsedMs) : Loc.T("soapdbg.callfail", r.Error ?? ""));
	}

	static string snippet(string s) {
		if (string.IsNullOrEmpty(s)) return Loc.T("soapdbg.emptyvalue");
		s = s.Trim().Replace("\r", " ").Replace("\n", " ");
		return s.Length > 300 ? s.Substring(0, 300) + " ..." : s;
	}

	static string pretty(string xml) {
		var doc = SoapEnvelope.parse(xml, out _);
		return doc == null ? xml : doc.ToString();
	}
	#endregion

	// ==================== 接口模拟 ====================
	#region
	void initmock() {
		lbMockOpsList.ItemsSource = mockOps;
		lbMockOpsList.SelectionChanged += (_, _) => onSelectMockOp();
		lbMockLog.SelectionChanged += (_, _) => showMockLogDetail();

		bStartMock.Click += (_, _) => startMock();
		bStopMock.Click += (_, _) => stopMock();
		bCopyMockUrl.Click += (_, _) => {
			if (string.IsNullOrEmpty(eMockUrl.Text)) return;
			try { Clipboard.SetText(eMockUrl.Text); } catch { }
			setstatus(Loc.T("soapdbg.copied"));
		};
		bUseMockUrl.Click += (_, _) => {
			if (string.IsNullOrEmpty(eMockUrl.Text)) return;
			eUrl.Text = $"{eMockUrl.Text}/{mockWsdlName()}";
			tabs.SelectedIndex = 0;
			setstatus(Loc.T("soapdbg.usemock"));
		};
		bMockDefault.Click += (_, _) => {
			if (currentMockOp == null || session == null) return;
			setmocktext(session.defaultResponseInner(currentMockOp.Op));
			mockcustom = true;
			mockdirty = true;
			nextsync = Environment.TickCount + 400;
		};
		bMockRevert.Click += (_, _) => {
			if (currentMockOp == null) return;
			mockcfg.set(currentMockOp.Op.Name, null);
			setmocktext(session?.defaultResponseInner(currentMockOp.Op) ?? "");
			mockcustom = false;
			mockdirty = false;
			mock.Resolver = name => mockcfg.get(name);
			setstatus(Loc.T("soapdbg.reverted"));
		};
		bMockSave.Click += (_, _) => saveMockConfig();
		bMockLoad.Click += (_, _) => loadMockConfig();
		bMockClearLog.Click += (_, _) => {
			mock.clearLogs();
			lbMockLog.ItemsSource = null;
			eMockLogDetail.Text = "";
			lbMockCount.Text = "";
		};
		eMockResponse.TextChanged += (_, _) => {
			if (loading) return;
			mockcustom = true;
			mockdirty = true;
			nextsync = Environment.TickCount + 400;
		};

		every(100, () => {
			if (!mockdirty || currentMockOp == null) return;
			if (Environment.TickCount - nextsync < 0) return;
			mockdirty = false;
			applyMockResponse();
		});

		every(300, () => {
			if (mock.LogVersion == lastlogver) return;
			lastlogver = mock.LogVersion;
			var items = mock.logsnapshot();
			lbMockLog.ItemsSource = null;
			lbMockLog.ItemsSource = items;
			lbMockCount.Text = Loc.T("soapdbg.mockcount", items.Count);
			if (items.Count > 0 && lbMockLog.SelectedItem == null) lbMockLog.SelectedIndex = 0;
		});
	}

	void setmocktext(string text) {
		loading = true;
		eMockResponse.Text = text ?? "";
		loading = false;
	}

	void applyMockResponse() {
		if (currentMockOp == null) return;
		string xml = eMockResponse.Text;
		mockcfg.set(currentMockOp.Op.Name, mockcustom && !string.IsNullOrWhiteSpace(xml) ? xml : null);
		mock.Resolver = name => mockcfg.get(name);
	}

	void rebuildMockOps() {
		mockOps.Clear();
		if (session == null) return;
		foreach (var op in session.ops()) mockOps.Add(new MockOpEntry { Op = op, Scope = op.Endpoint.Scope });
		if (mockOps.Count > 0) lbMockOpsList.SelectedIndex = 0;
	}

	void onSelectMockOp() {
		if (lbMockOpsList.SelectedItem is not MockOpEntry entry) return;
		applyMockResponse();
		currentMockOp = entry;
		lbMockOp.Text = entry.Op.Name;
		string custom = mockcfg.get(entry.Op.Name);
		mockcustom = custom != null;
		setmocktext(custom ?? session?.defaultResponseInner(entry.Op) ?? "");
		mockdirty = false;
	}

	string mockWsdlName() {
		var doc = session?.Model?.Docs.FirstOrDefault(d => d.IsWsdl) ?? session?.Model?.Docs.FirstOrDefault();
		string name = doc == null ? "" : Path.GetFileName(doc.Path);
		return string.IsNullOrEmpty(name) ? "service?wsdl" : name;
	}

	void startMock() {
		if (session == null) { setstatus(Loc.T("soapdbg.needread")); return; }
		if (!int.TryParse((ePort.Text ?? "").Trim(), out int port)) { setstatus(Loc.T("soapdbg.badport")); return; }
		applyMockResponse();
		mockcfg.Port = port;
		mockcfg.WsdlLocation = session.Model.Location;
		mock.Resolver = name => mockcfg.get(name);
		if (!mock.start(port, false, out string error)) {
			setstatus(Loc.T("soapdbg.startfail", error));
			return;
		}
		mock.Builder = session.Builder;
		mock.publish(session.Model, port);
		eMockUrl.Text = mock.BaseUrl;
		setstatus(Loc.T("soapdbg.started", mock.BaseUrl, string.Join(", ", mock.paths())));
	}

	void stopMock() {
		mock.stop();
		setstatus(Loc.T("soapdbg.stopped"));
	}

	void showMockLogDetail() {
		if (lbMockLog.SelectedItem is not MockServer.MockLog log) { eMockLogDetail.Text = ""; return; }
		var sb = new StringBuilder();
		sb.AppendLine($"{log.TimeText}  {log.Method} {log.Url}");
		sb.AppendLine(Loc.T("soapdbg.log.status", log.Status, log.ElapsedMs, log.Note));
		sb.AppendLine();
		sb.AppendLine(Loc.T("soapdbg.log.request"));
		sb.AppendLine(log.Body);
		sb.AppendLine();
		sb.AppendLine(Loc.T("soapdbg.log.response"));
		sb.AppendLine(log.ResponseBody);
		eMockLogDetail.Text = sb.ToString();
	}

	void saveMockConfig() {
		applyMockResponse();
		var dlg = new SaveFileDialog {
			Filter = Loc.T("soapdbg.dlg.savecfg"),
			FileName = "mock.mockcfg",
		};
		if (dlg.ShowDialog() != true) return;
		mockcfg.WsdlLocation = session?.Model?.Location ?? "";
		mockcfg.Port = int.TryParse(ePort.Text, out int p) ? p : 8080;
		mockcfg.save(dlg.FileName);
		setstatus(Loc.T("soapdbg.exported", dlg.FileName));
	}

	void loadMockConfig() {
		var dlg = new OpenFileDialog {
			Filter = Loc.T("soapdbg.dlg.loadcfg"),
		};
		if (dlg.ShowDialog() != true) return;
		var cfg = MockConfig.load(dlg.FileName, out string error);
		if (error != null) { setstatus(Loc.T("soapdbg.importfail", error)); return; }
		mockcfg = cfg;
		ePort.Text = cfg.Port.ToString();
		mock.Resolver = name => mockcfg.get(name);
		if (currentMockOp != null) {
			string custom = mockcfg.get(currentMockOp.Op.Name);
			mockcustom = custom != null;
			setmocktext(custom ?? session?.defaultResponseInner(currentMockOp.Op) ?? "");
		}
		setstatus(Loc.T("soapdbg.imported", cfg.Count, dlg.FileName));
		if (!string.IsNullOrEmpty(cfg.WsdlLocation) && session == null) {
			eUrl.Text = cfg.WsdlLocation;
			setstatus(Loc.T("soapdbg.cfgloaded", cfg.Count, cfg.WsdlLocation));
		}
	}
	#endregion

	// ==================== 其他 ====================
	#region
	void setstatus(string text) => lbStatus.Text = text;

	string recentPath() => Path.Combine(MockDir(), "recent.txt");

	void loadRecent() {
		try {
			string path = recentPath();
			if (!File.Exists(path)) return;
			foreach (var line in File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Take(20))
				eUrl.Items.Add(line);
		}
		catch { }
	}

	void addRecent(string location) {
		try {
			string keep = eUrl.Text;
			var list = eUrl.Items.Cast<string>()
				.Where(x => !string.Equals(x, location, StringComparison.OrdinalIgnoreCase)).ToList();
			list.Insert(0, location);
			if (list.Count > 20) list = list.Take(20).ToList();
			eUrl.Items.Clear();
			foreach (var l in list) eUrl.Items.Add(l);
			// 清空 Items 会把可编辑 ComboBox 的 Text 一并清掉，需要还原
			eUrl.Text = string.IsNullOrEmpty(keep) ? location : keep;
			File.WriteAllLines(recentPath(), list);
		}
		catch { }
	}
	#endregion
}

/// <summary>把委托包成 ICommand，供 InputBinding 使用。</summary>
public sealed class SoapRelayCommand : ICommand {
	readonly Action action;
	public SoapRelayCommand(Action action) { this.action = action; }
	public event EventHandler CanExecuteChanged { add { } remove { } }
	public bool CanExecute(object parameter) => true;
	public void Execute(object parameter) => action();
}
