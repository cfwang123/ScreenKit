using System.Net.Http;
using System.Text;

namespace ScreenKit;

sealed class HttpTpl {
	public string Title { get; set; }
	public string Method { get; set; }
	public string Path { get; set; }
	public string Body { get; set; }
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
		ehttptpl.Items.Add(new HttpTpl { Title = "GET /api/status", Method = "GET", Path = "/api/status", Body = "" });
		ehttptpl.Items.Add(new HttpTpl { Title = "GET /api", Method = "GET", Path = "/api", Body = "" });
		ehttptpl.Items.Add(new HttpTpl { Title = "GET /api/ocr/get_options", Method = "GET", Path = "/api/ocr/get_options", Body = "" });
		ehttptpl.Items.Add(new HttpTpl {
			Title = "POST /api/ocr", Method = "POST", Path = "/api/ocr",
			Body = "{\n  \"base64\": \"\",\n  \"options\": {}\n}",
		});
		ehttptpl.Items.Add(new HttpTpl { Title = "GET /api/asr/models", Method = "GET", Path = "/api/asr/models", Body = "" });
		ehttptpl.Items.Add(new HttpTpl {
			Title = "POST /api/asr", Method = "POST", Path = "/api/asr",
			Body = "{\n  \"path\": \"\",\n  \"lang\": \"auto\"\n}",
		});
		ehttptpl.Items.Add(new HttpTpl { Title = "GET /api/tts/models", Method = "GET", Path = "/api/tts/models", Body = "" });
		ehttptpl.Items.Add(new HttpTpl {
			Title = "POST /api/tts", Method = "POST", Path = "/api/tts",
			Body = "{\n  \"text\": \"你好\"\n}",
		});
		ehttptpl.Items.Add(new HttpTpl {
			Title = "POST /api/itn", Method = "POST", Path = "/api/itn",
			Body = "{\n  \"text\": \"二零二四年一月一日\"\n}",
		});
		ehttptpl.Items.Add(new HttpTpl {
			Title = "POST /api/translate", Method = "POST", Path = "/api/translate",
			Body = "{\n  \"items\": [\"你好\"],\n  \"src\": \"zh\",\n  \"dst\": \"en\"\n}",
		});
		ehttptpl.Items.Add(new HttpTpl { Title = "GET /api/face/models", Method = "GET", Path = "/api/face/models", Body = "" });
		ehttptpl.Items.Add(new HttpTpl {
			Title = "POST /api/face", Method = "POST", Path = "/api/face",
			Body = "{\n  \"base64\": \"\"\n}",
		});
		httpUiLoading = false;
	}

	void applyhttptpl(HttpTpl t) {
		if (t == null) return;
		httpUiLoading = true;
		pickhttpmethod(t.Method);
		ehttppath.Text = t.Path ?? "/api/status";
		ehttpbody.Text = t.Body ?? "";
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
		if (opt.HttpEnabled && httpServer != null && httpServer.IsRunning)
			lbhttpstatus.Text = $"http://{opt.HttpHost}:{opt.HttpPort}";
		else
			lbhttpstatus.Text = Loc.T("http.tab.off");
	}

	void applyhttplang() {
		try {
			tabhttp.Header = Loc.T("tab.http");
			lbhttpbrand.Text = Loc.T("http.tab.brand");
			lbhttplog.Text = Loc.T("http.tab.log");
			bhttpclear.Content = Loc.T("http.tab.clear");
			lbhttpreq.Text = Loc.T("http.tab.req");
			lbhttptpl.Text = Loc.T("http.tab.tpl");
			lbhttpbody.Text = Loc.T("http.tab.body");
			lbhttpresp.Text = Loc.T("http.tab.resp");
			bhttpsend.Content = Loc.T("http.tab.send");
			synchttpstatus();
		}
		catch { }
	}

	string httpbaseurl() {
		var host = (opt.HttpHost ?? "").Trim();
		if (string.IsNullOrEmpty(host) || host == "0.0.0.0" || host == "*" || host == "+")
			host = "127.0.0.1";
		return $"http://{host}:{opt.HttpPort}";
	}

	async Task httpSendAsync() {
		if (httpSending) return;
		if (!opt.HttpEnabled || httpServer == null || !httpServer.IsRunning) {
			ehttpresp.Text = Loc.T("http.tab.off");
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
