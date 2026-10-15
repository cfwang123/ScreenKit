namespace ScreenKit;

/// <summary>工具 → HTTP 请求调试。</summary>
public partial class HttpDebugWindow : Window {
	sealed class Row {
		public string Id = "";
		public string Text = "";
		public override string ToString() => Text ?? "";
	}

	readonly string store;
	HttpDbgBook book;
	string curid;
	string snap;
	string draftform = "";
	string draftjson = "";
	string draftxml = "";
	string draw = "";
	string bodykind = "params";
	bool loading;
	bool filling;
	bool busy;
	CancellationTokenSource cts;

	public HttpDebugWindow() {
		store = HttpDebug.StorePath();
		InitializeComponent();
		emethod.Items.Add("GET");
		emethod.Items.Add("POST");
		emethod.SelectedIndex = 0;
		applylang();
		initev();
		WindowEsc.Attach(this);
		book = HttpDebug.Load(store, Loc.T("httpdbg.defgroup"), out var err);
		fillgroups(HttpDebug.DEFAULT_GROUP);
		blank(HttpDebug.DEFAULT_GROUP);
		if (!string.IsNullOrEmpty(err)) lbstat.Text = Loc.T("httpdbg.badfile", err);
	}

	void initev() {
		egroups.SelectionChanged += (_, _) => {
			if (loading || filling) return;
			fillreqs();
		};
		ereqs.SelectionChanged += (_, _) => {
			if (loading || filling) return;
			if (ereqs.SelectedItem is not Row row) return;
			if (row.Id == curid) return;
			if (!askdirty()) {
				filling = true;
				selectreq(curid);
				filling = false;
				return;
			}
			loaditem(row.Id);
		};
		bgroupadd.Click += (_, _) => addgroup();
		bgroupren.Click += (_, _) => rengroup();
		bgroupdel.Click += (_, _) => delgroup();
		bnew.Click += (_, _) => newitem();
		bsave.Click += (_, _) => saveitem();
		bdel.Click += (_, _) => delitem();
		bsend.Click += (_, _) => { _ = sendasync(); };
		bstop.Click += (_, _) => { try { cts?.Cancel(); } catch { } };
		bcopy.Click += (_, _) => copyout();
		bclose.Click += (_, _) => Close();
		emethod.SelectionChanged += (_, _) => { if (!loading) syncmethod(); };
		rparams.Checked += (_, _) => onbody("params");
		rjson.Checked += (_, _) => onbody("json");
		rxml.Checked += (_, _) => onbody("xml");
		rmime.Checked += (_, _) => onbody("mime");
		Closing += (_, e) => {
			if (!askdirty()) e.Cancel = true;
			else { try { cts?.Cancel(); } catch { } }
		};
	}

	void applylang() {
		Title = Loc.T("httpdbg.title");
		lbgroup.Text = Loc.T("httpdbg.group");
		lbreq.Text = Loc.T("httpdbg.req");
		lbname.Text = Loc.T("httpdbg.name");
		lbitemgroup.Text = Loc.T("httpdbg.group");
		lbmethod.Text = Loc.T("httpdbg.method");
		lburl.Text = Loc.T("httpdbg.url");
		lbquery.Text = Loc.T("httpdbg.query");
		lbbody.Text = Loc.T("httpdbg.body");
		rparams.Content = Loc.T("httpdbg.params");
		rjson.Content = Loc.T("httpdbg.json");
		rxml.Content = Loc.T("httpdbg.xml");
		rmime.Content = Loc.T("httpdbg.mime");
		ToolBtnUi.Set(bgroupadd, ToolBtnUi.Add, Loc.T("httpdbg.group.new"));
		ToolBtnUi.Set(bgroupren, ToolBtnUi.Rename, Loc.T("httpdbg.group.ren"));
		ToolBtnUi.Set(bgroupdel, ToolBtnUi.Delete, Loc.T("httpdbg.group.del"));
		ToolBtnUi.Set(bnew, ToolBtnUi.Add, Loc.T("httpdbg.new"));
		ToolBtnUi.Set(bsave, ToolBtnUi.Save, Loc.T("httpdbg.save"));
		ToolBtnUi.Set(bdel, ToolBtnUi.Delete, Loc.T("httpdbg.delete"));
		ToolBtnUi.Set(bsend, ToolBtnUi.Play, Loc.T("httpdbg.send"));
		ToolBtnUi.Set(bstop, ToolBtnUi.Cancel, Loc.T("httpdbg.stop"));
		ToolBtnUi.Set(bcopy, ToolBtnUi.Copy, Loc.T("httpdbg.copy"));
		ToolBtnUi.Set(bclose, ToolBtnUi.Close, Loc.T("imgconv.close"));
	}

	void fillgroups(string select) {
		loading = true;
		egroups.Items.Clear();
		Row pick = null;
		foreach (var g in book.Groups) {
			var row = new Row { Id = g.Id, Text = g.Name ?? "" };
			egroups.Items.Add(row);
			if (g.Id == select) pick = row;
		}
		if (pick == null && egroups.Items.Count > 0) pick = (Row)egroups.Items[0];
		egroups.SelectedItem = pick;
		fillitemgroups(itemgroupid());
		loading = false;
		fillreqs();
	}

	void fillitemgroups(string select) {
		var keep = loading;
		loading = true;
		eitemgroup.Items.Clear();
		ComboBoxItem pick = null;
		foreach (var g in book.Groups) {
			var it = new ComboBoxItem { Content = g.Name ?? "", Tag = g.Id };
			eitemgroup.Items.Add(it);
			if (g.Id == select) pick = it;
		}
		if (pick == null && eitemgroup.Items.Count > 0) pick = (ComboBoxItem)eitemgroup.Items[0];
		eitemgroup.SelectedItem = pick;
		if (!keep) loading = false;
	}

	void fillreqs() {
		filling = true;
		ereqs.Items.Clear();
		var gid = groupid();
		Row pick = null;
		foreach (var it in book.Items) {
			if (it.Group != gid) continue;
			var text = string.IsNullOrWhiteSpace(it.Name) ? (it.Url ?? "") : it.Name;
			var row = new Row { Id = it.Id, Text = text };
			ereqs.Items.Add(row);
			if (it.Id == curid) pick = row;
		}
		ereqs.SelectedItem = pick;
		filling = false;
	}

	void selectreq(string id) {
		foreach (var obj in ereqs.Items) {
			if (obj is Row row && row.Id == id) {
				ereqs.SelectedItem = row;
				return;
			}
		}
		ereqs.SelectedItem = null;
	}

	void loaditem(string id) {
		var item = book.Items.FirstOrDefault(x => x.Id == id);
		if (item == null) return;
		curid = id;
		loading = true;
		ename.Text = item.Name ?? "";
		eurl.Text = item.Url ?? "";
		equery.Text = item.Query ?? "";
		emethod.SelectedItem = item.Method == "POST" ? "POST" : "GET";
		draftform = item.Form ?? "";
		draftjson = item.Json ?? "";
		draftxml = item.Xml ?? "";
		draw = item.Raw ?? "";
		emime.Text = string.IsNullOrWhiteSpace(item.Mime) ? "text/plain" : item.Mime;
		setbody(item.Body);
		fillitemgroups(item.Group);
		loading = false;
		syncmethod();
		snap = takesnap();
	}

	void newitem() {
		if (!askdirty()) return;
		curid = null;
		blank(groupid());
		fillreqs();
		ename.Focus();
	}

	void blank(string group) {
		loading = true;
		ename.Text = "";
		eurl.Text = "";
		equery.Text = "";
		emethod.SelectedItem = "GET";
		draftform = "";
		draftjson = "";
		draftxml = "";
		draw = "";
		emime.Text = "text/plain";
		setbody("params");
		fillitemgroups(group);
		loading = false;
		syncmethod();
		snap = takesnap();
	}

	bool saveitem() {
		var item = findoradd();
		apply(item);
		if (string.IsNullOrWhiteSpace(item.Name)) item.Name = fallbackname();
		ename.Text = item.Name;
		if (!write()) return false;
		snap = takesnap();
		fillreqs();
		lbstat.Text = Loc.T("httpdbg.saved");
		return true;
	}

	void delitem() {
		if (string.IsNullOrEmpty(curid)) return;
		var item = book.Items.FirstOrDefault(x => x.Id == curid);
		var name = string.IsNullOrWhiteSpace(item?.Name) ? Loc.T("httpdbg.noname") : item.Name;
		if (MessageBox.Show(this, Loc.T("httpdbg.delask", name), Title,
			MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
		book.Items.RemoveAll(x => x.Id == curid);
		if (!write()) return;
		curid = null;
		blank(groupid());
		fillreqs();
		lbstat.Text = Loc.T("httpdbg.saved");
	}

	void addgroup() {
		var name = asktext(Loc.T("httpdbg.group.new"), Loc.T("httpdbg.group.name"), "");
		if (string.IsNullOrEmpty(name)) return;
		var id = "g" + Guid.NewGuid().ToString("N").Substring(0, 12);
		book.Groups.Add(new HttpDbgGroup { Id = id, Name = name });
		if (!write()) return;
		fillgroups(id);
		lbstat.Text = Loc.T("httpdbg.saved");
	}

	void rengroup() {
		var id = groupid();
		var g = book.Groups.FirstOrDefault(x => x.Id == id);
		if (g == null) return;
		var name = asktext(Loc.T("httpdbg.group.ren"), Loc.T("httpdbg.group.name"), g.Name);
		if (string.IsNullOrEmpty(name)) return;
		var was = isdirty();
		g.Name = name;
		if (!write()) return;
		fillgroups(id);
		if (!was) snap = takesnap();
		lbstat.Text = Loc.T("httpdbg.saved");
	}

	void delgroup() {
		var id = groupid();
		if (id == HttpDebug.DEFAULT_GROUP) {
			lbstat.Text = Loc.T("httpdbg.group.keep");
			return;
		}
		var g = book.Groups.FirstOrDefault(x => x.Id == id);
		if (g == null) return;
		if (MessageBox.Show(this, Loc.T("httpdbg.group.delask", g.Name), Title,
			MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
		var was = isdirty();
		book.Groups.RemoveAll(x => x.Id == id);
		foreach (var it in book.Items)
			if (it.Group == id) it.Group = HttpDebug.DEFAULT_GROUP;
		if (!write()) return;
		fillgroups(HttpDebug.DEFAULT_GROUP);
		if (!was) snap = takesnap();
		lbstat.Text = Loc.T("httpdbg.saved");
	}

	HttpDbgItem findoradd() {
		HttpDbgItem item = null;
		if (!string.IsNullOrEmpty(curid))
			item = book.Items.FirstOrDefault(x => x.Id == curid);
		if (item != null) return item;
		item = new HttpDbgItem { Id = "r" + Guid.NewGuid().ToString("N").Substring(0, 12) };
		book.Items.Add(item);
		curid = item.Id;
		return item;
	}

	void apply(HttpDbgItem item) {
		stash();
		item.Name = (ename.Text ?? "").Trim();
		item.Group = itemgroupid();
		item.Method = methodof();
		item.Url = eurl.Text ?? "";
		item.Query = equery.Text ?? "";
		item.Body = bodykind;
		item.Form = draftform;
		item.Json = draftjson;
		item.Xml = draftxml;
		item.Raw = draw;
		item.Mime = (emime.Text ?? "").Trim();
	}

	HttpDbgItem draft() {
		var item = new HttpDbgItem();
		apply(item);
		return item;
	}

	bool write() {
		try {
			HttpDebug.Save(store, book);
			return true;
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("httpdbg.fail", ex.Message);
			return false;
		}
	}

	bool askdirty() {
		if (!isdirty()) return true;
		var ans = MessageBox.Show(this, Loc.T("httpdbg.dirty"), Title,
			MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
		if (ans == MessageBoxResult.Cancel) return false;
		if (ans == MessageBoxResult.No) return true;
		return saveitem();
	}

	bool isdirty() => snap != null && takesnap() != snap;

	string takesnap() {
		var form = bodykind == "params" ? (ebody.Text ?? "") : draftform;
		var js = bodykind == "json" ? (ebody.Text ?? "") : draftjson;
		var xml = bodykind == "xml" ? (ebody.Text ?? "") : draftxml;
		var raw = bodykind == "mime" ? (ebody.Text ?? "") : draw;
		return string.Join("\u0001", itemgroupid(), ename.Text ?? "", methodof(), eurl.Text ?? "",
			equery.Text ?? "", bodykind, form, js, xml, raw, emime.Text ?? "");
	}

	string groupid() {
		if (egroups.SelectedItem is Row row && !string.IsNullOrEmpty(row.Id)) return row.Id;
		return HttpDebug.DEFAULT_GROUP;
	}

	string itemgroupid() {
		if (eitemgroup.SelectedItem is ComboBoxItem it && it.Tag is string id && id.Length > 0) return id;
		return HttpDebug.DEFAULT_GROUP;
	}

	string methodof() => emethod.SelectedItem as string == "POST" ? "POST" : "GET";

	string fallbackname() {
		var url = (eurl.Text ?? "").Trim();
		if (url.Length == 0) return Loc.T("httpdbg.noname");
		if (url.Length > 48) url = url.Substring(0, 48);
		return url;
	}

	void onbody(string kind) {
		kind = normkind(kind);
		if (loading || kind == bodykind) return;
		stash();
		bodykind = kind;
		loading = true;
		ebody.Text = textforkind(kind);
		showmime();
		loading = false;
	}

	void setbody(string kind) {
		bodykind = normkind(kind);
		rparams.IsChecked = bodykind == "params";
		rjson.IsChecked = bodykind == "json";
		rxml.IsChecked = bodykind == "xml";
		rmime.IsChecked = bodykind == "mime";
		ebody.Text = textforkind(bodykind);
		showmime();
	}

	void stash() {
		var text = ebody.Text ?? "";
		if (bodykind == "json") draftjson = text;
		else if (bodykind == "xml") draftxml = text;
		else if (bodykind == "mime") draw = text;
		else draftform = text;
	}

	string textforkind(string kind) {
		if (kind == "json") return draftjson;
		if (kind == "xml") return draftxml;
		if (kind == "mime") return draw;
		return draftform;
	}

	static string normkind(string kind) {
		if (kind == "json" || kind == "xml" || kind == "mime") return kind;
		return "params";
	}

	void showmime() {
		emime.Visibility = bodykind == "mime" ? Visibility.Visible : Visibility.Collapsed;
	}

	void syncmethod() {
		var post = methodof() == "POST";
		rparams.IsEnabled = post;
		rjson.IsEnabled = post;
		rxml.IsEnabled = post;
		rmime.IsEnabled = post;
		emime.IsEnabled = post;
		ebody.IsEnabled = post;
	}

	async Task sendasync() {
		if (busy) return;
		busy = true;
		bsend.IsEnabled = false;
		bstop.IsEnabled = true;
		lbstat.Text = Loc.T("httpdbg.busy");
		eout.Text = "";
		cts?.Dispose();
		cts = new CancellationTokenSource();
		var token = cts.Token;
		HttpDbgResult r;
		try { r = await HttpDebug.SendAsync(draft(), token); }
		catch (Exception ex) { r = new HttpDbgResult { Error = ex.Message }; }
		busy = false;
		if (!IsLoaded) return;
		bsend.IsEnabled = true;
		bstop.IsEnabled = false;
		show(r);
	}

	void show(HttpDbgResult r) {
		if (r == null) return;
		if (!r.Ok) {
			eout.Text = "";
			lbstat.Text = r.Error switch {
				"empty" => Loc.T("httpdbg.needurl"),
				"bad" => Loc.T("httpdbg.badurl"),
				"emptyjson" => Loc.T("httpdbg.emptyjson"),
				"badmime" => Loc.T("httpdbg.badmime"),
				"cancel" => Loc.T("httpdbg.cancelled"),
				"timeout" => Loc.T("httpdbg.timeout"),
				_ => Loc.T("httpdbg.fail", r.Error ?? ""),
			};
			return;
		}
		lbstat.Text = Loc.T("httpdbg.done", r.Status, r.Ms);
		var body = r.Body ?? "";
		eout.Text = string.IsNullOrEmpty(r.Head) ? body : r.Head + "\r\n\r\n" + body;
	}

	void copyout() {
		try {
			Clipboard.SetText(eout.Text ?? "");
			lbstat.Text = Loc.T("nettool.copied");
		}
		catch (Exception ex) {
			MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	string asktext(string title, string label, string value) {
		var w = new Window {
			Title = title,
			Width = 380,
			Height = 168,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Owner = this,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false,
			Background = Background,
			FontFamily = FontFamily,
		};
		var grid = new Grid { Margin = new Thickness(16) };
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		var lb = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) };
		var box = new TextBox {
			Text = value ?? "",
			Height = 32,
			Padding = new Thickness(8, 4, 8, 4),
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		var bar = new StackPanel {
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0, 12, 0, 0),
		};
		var ok = new Button {
			Content = Loc.T("ok"),
			IsDefault = true,
			MinWidth = 72,
			Margin = new Thickness(0, 0, 8, 0),
			Style = (Style)FindResource("ToolBtn"),
		};
		var cancel = new Button {
			Content = Loc.T("cancel"),
			IsCancel = true,
			MinWidth = 72,
			Style = (Style)FindResource("ToolBtn"),
		};
		ok.Click += (_, _) => { w.DialogResult = true; };
		bar.Children.Add(ok);
		bar.Children.Add(cancel);
		Grid.SetRow(box, 1);
		Grid.SetRow(bar, 2);
		grid.Children.Add(lb);
		grid.Children.Add(box);
		grid.Children.Add(bar);
		w.Content = grid;
		WindowEsc.Attach(w);
		w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
		if (w.ShowDialog() != true) return null;
		return (box.Text ?? "").Trim();
	}
}
