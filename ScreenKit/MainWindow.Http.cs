using System.Net.Http;
using System.Text;

namespace ScreenKit;

sealed class HttpTpl {
	public string Title { get; set; }
	public string Method { get; set; }
	public string Path { get; set; }
	public string Body { get; set; }
	public string Doc { get; set; }
}

public partial class MainWindow {
	readonly List<string> httplog = new();
	bool httpUiLoading;
	bool httpSending;

	void inithttptab() {
		fillhttptpl();
		ehttptpl.SelectionChanged += (_, _) => {
			if (httpUiLoading) return;
			if (ehttptpl.SelectedItem is HttpTpl t)
				applyhttptpl(t);
		};
		bhttpclear.Click += (_, _) => {
			httplog.Clear();
			ehttplog.Clear();
		};
		bhttpsend.Click += (_, _) => _ = httpSendAsync();
		synchttpstatus();
		if (ehttptpl.Items.Count > 0)
			ehttptpl.SelectedIndex = 0;
	}

	void fillhttptpl() {
		httpUiLoading = true;
		ehttptpl.Items.Clear();
		addhttp("GET /api/status", "GET", "/api/status", "", "http.doc.status");
		addhttp("GET /api/toast", "GET", "/api/toast?text=你好&ms=1900", "", "http.doc.toast");
		addhttp("POST /api/toast", "POST", "/api/toast", "{\n  \"text\": \"你好\",\n  \"ms\": 1900\n}", "http.doc.toast");
		addhttp("GET /api/zhconv", "GET", "/api/zhconv?text=软件&to=trad", "", "http.doc.zhconv");
		addhttp("POST /api/zhconv", "POST", "/api/zhconv", "{\n  \"text\": \"软件\",\n  \"to\": \"trad\"\n}", "http.doc.zhconv");
		addhttp("GET /api/calendar", "GET", "/api/calendar?date=2024-02-10&cal=lunar", "", "http.doc.calendar");
		addhttp("POST /api/calendar", "POST", "/api/calendar", "{\n  \"date\": \"2024-02-10\",\n  \"cal\": \"lunar\"\n}", "http.doc.calendar");
		addhttp("GET /api/jpyomi", "GET", "/api/jpyomi?text=東京は晴れです", "", "http.doc.jpyomi");
		addhttp("POST /api/jpyomi", "POST", "/api/jpyomi", "{\n  \"text\": \"東京は晴れです\",\n  \"mono\": false\n}", "http.doc.jpyomi");
		addhttp("POST /api/cast/stop", "POST", "/api/cast/stop", "", "http.doc.caststop");
		addhttp("GET /api", "GET", "/api", "", "http.doc.api");
		addhttp("GET /api/ocr/get_options", "GET", "/api/ocr/get_options", "", "http.doc.ocropt");
		addhttp("POST /api/ocr", "POST", "/api/ocr", "{\n  \"base64\": \"\",\n  \"options\": {}\n}", "http.doc.ocr");
		addhttp("POST /api/qr", "POST", "/api/qr", "{\n  \"base64\": \"\",\n  \"format\": \"dict\"\n}", "http.doc.qr");
		addhttp("GET /api/asr/models", "GET", "/api/asr/models", "", "http.doc.asrmodels");
		addhttp("POST /api/asr", "POST", "/api/asr", "{\n  \"path\": \"\",\n  \"lang\": \"auto\"\n}", "http.doc.asr");
		addhttp("GET /api/tts/models", "GET", "/api/tts/models", "", "http.doc.ttsmodels");
		addhttp("POST /api/tts", "POST", "/api/tts", "{\n  \"text\": \"你好\"\n}", "http.doc.tts");
		addhttp("POST /api/tts (SAPI)", "POST", "/api/tts", "{\n  \"text\": \"你好\",\n  \"engine\": \"sapi\"\n}", "http.doc.tts.sapi");
		addhttp("POST /api/tts (Windows)", "POST", "/api/tts", "{\n  \"text\": \"你好\",\n  \"engine\": \"winrt\"\n}", "http.doc.tts.win");
		addhttp("POST /api/tts (Edge Online)", "POST", "/api/tts", "{\n  \"text\": \"안녕하세요\",\n  \"engine\": \"edge\",\n  \"voice\": \"edge:ko-KR-SunHiNeural\"\n}", "http.doc.tts.edge");
		addhttp("POST /api/itn", "POST", "/api/itn", "{\n  \"text\": \"二零二四年一月一日\"\n}", "http.doc.itn");
		addhttp("POST /api/translate", "POST", "/api/translate", "{\n  \"items\": [\"你好\"],\n  \"src\": \"zh\",\n  \"dst\": \"en\"\n}", "http.doc.translate");
		addhttp("POST /api/chat", "POST", "/api/chat", "{\n  \"text\": \"你好\",\n  \"tts\": false,\n  \"agent\": false\n}", "http.doc.chat");
		addhttp("POST /api/chat + tts", "POST", "/api/chat", "{\n  \"text\": \"用一句话介绍你自己\",\n  \"tts\": true,\n  \"engine\": \"sapi\"\n}", "http.doc.chat.tts");
		addhttp("GET /api/face/models", "GET", "/api/face/models", "", "http.doc.facemodels");
		addhttp("POST /api/face", "POST", "/api/face", "{\n  \"base64\": \"\"\n}", "http.doc.face");
		httpUiLoading = false;
	}

	void addhttp(string title, string method, string path, string body, string doc) {
		ehttptpl.Items.Add(new HttpTpl {
			Title = title, Method = method, Path = path, Body = body, Doc = doc,
		});
	}

	void applyhttptpl(HttpTpl t) {
		if (t == null) return;
		httpUiLoading = true;
		pickhttpmethod(t.Method);
		ehttppath.Text = t.Path ?? "/api/status";
		ehttpbody.Text = t.Body ?? "";
		ehttphelp.Text = string.IsNullOrEmpty(t.Doc) ? "" : Loc.T(t.Doc);
		httpUiLoading = false;
	}

	void pickhttpmethod(string method) {
		var want = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ? "POST" : "GET";
		foreach (ComboBoxItem it in ehttpmethod.Items) {
			if (string.Equals(it.Content as string, want, StringComparison.OrdinalIgnoreCase)) {
				ehttpmethod.SelectedItem = it;
				return;
			}
		}
		ehttpmethod.SelectedIndex = 0;
	}

	void onhttplog(string line) {
		if (string.IsNullOrEmpty(line)) return;
		try {
			Dispatcher.BeginInvoke(new Action(() => {
				httplog.Add(line);
				while (httplog.Count > 200)
					httplog.RemoveAt(0);
				ehttplog.Text = string.Join("\n", httplog);
				ehttplog.CaretIndex = ehttplog.Text.Length;
				ehttplog.ScrollToEnd();
			}));
		}
		catch { }
	}

	void synchttpstatus() {
		if (lbhttpstatus == null) return;
		lbhttpstatus.Text = httpstatustext();
	}

	string httpstatustext() {
		if (opt.HttpEnabled && httpServer != null && httpServer.IsRunning) {
			var host = httpServer.LanAll ? "0.0.0.0" : "127.0.0.1";
			var text = $"http://{host}:{SendFileServer.FileHttpPort(opt)}";
			if (opt.HttpLan && !httpServer.LanAll)
				text += Loc.T("http.tab.localonly");
			return text;
		}
		if (!opt.HttpEnabled) return Loc.T("http.tab.off");
		var err = (httpListenErr ?? "").Replace("\r", " ").Replace('\n', ' ').Trim();
		if (err.Length > 80) err = err.Substring(0, 80) + "…";
		if (err.Length > 0) return Loc.T("http.tab.down", err);
		return Loc.T("http.tab.downplain");
	}

	void applyhttplang() {
		try {
			tabhttp.Header = Loc.T("tab.http");
			lbhttpbrand.Text = Loc.T("http.tab.brand");
			lbhttplog.Text = Loc.T("http.tab.log");
			bhttpclear.Content = Loc.T("http.tab.clear");
			lbhttpreq.Text = Loc.T("http.tab.req");
			lbhttptpl.Text = Loc.T("http.tab.tpl");
			lbhttphelp.Text = Loc.T("http.tab.help");
			if (ehttptpl.SelectedItem is HttpTpl t && !string.IsNullOrEmpty(t.Doc))
				ehttphelp.Text = Loc.T(t.Doc);
			lbhttpbody.Text = Loc.T("http.tab.body");
			lbhttpresp.Text = Loc.T("http.tab.resp");
			bhttpsend.Content = Loc.T("http.tab.send");
			synchttpstatus();
		}
		catch { }
	}

	string httpbaseurl() {
		return $"http://127.0.0.1:{opt.HttpPort}";
	}

	async Task httpSendAsync() {
		if (httpSending) return;
		if (!opt.HttpEnabled || httpServer == null || !httpServer.IsRunning) {
			ehttpresp.Text = httpstatustext();
			return;
		}
		var method = (ehttpmethod.SelectedItem as ComboBoxItem)?.Content as string ?? "GET";
		var path = (ehttppath.Text ?? "").Trim();
		if (path.Length == 0) path = "/";
		if (!path.StartsWith("/")) path = "/" + path;
		var body = ehttpbody.Text ?? "";
		httpSending = true;
		bhttpsend.IsEnabled = false;
		ehttpresp.Text = Loc.T("http.tab.busy");
		try {
			using var http = new HttpClient(HttpProxy.CreateHandler()) {
				Timeout = TimeSpan.FromSeconds(60),
			};
			var url = httpbaseurl() + path;
			HttpResponseMessage resp;
			if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)) {
				var content = new StringContent(body, Encoding.UTF8, "application/json");
				resp = await http.PostAsync(url, content).ConfigureAwait(true);
			}
			else {
				resp = await http.GetAsync(url).ConfigureAwait(true);
			}
			var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(true);
			ehttpresp.Text = $"{(int)resp.StatusCode} {resp.ReasonPhrase}\n\n{text}";
		}
		catch (Exception ex) {
			ehttpresp.Text = Loc.T("http.tab.fail", ex.Message);
		}
		finally {
			httpSending = false;
			bhttpsend.IsEnabled = true;
		}
	}
}
