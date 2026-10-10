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
		bhttptools.Click += (_, _) => openhttptools();
		bhttpsend.Click += (_, _) => _ = httpSendAsync();
		synchttpstatus();
		if (ehttptpl.Items.Count > 0)
			ehttptpl.SelectedIndex = 0;
	}

	void fillhttptpl() {
		httpUiLoading = true;
		ehttptpl.Items.Clear();
		foreach (var it in HttpApiCatalog.Items) {
			if (!it.Tab) continue;
			addhttp(it.Title, it.Method, it.Path, it.Body, it.Doc);
		}
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
			bhttptools.Content = Loc.T("http.tab.tools");
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

	void openhttptools(bool fromTray = false) {
		if (!opt.HttpEnabled || httpServer == null || !httpServer.IsRunning) {
			httptoolfail(httpstatustext(), fromTray);
			return;
		}
		try {
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
				FileName = httpbaseurl() + "/",
				UseShellExecute = true,
			});
		}
		catch (Exception ex) {
			httptoolfail(ex.Message, fromTray);
		}
	}

	void httptoolfail(string msg, bool fromTray) {
		try { ehttpresp.Text = msg ?? ""; } catch { }
		if (fromTray) UiToast.Show(null, msg ?? "");
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
