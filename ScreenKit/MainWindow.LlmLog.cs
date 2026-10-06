using System.Windows.Controls;

namespace ScreenKit;

public partial class MainWindow {
	void initllmlog() {
		llmloglist.SelectionChanged += onllmlogsel;
		LlmCalls.Changed += onllmcallschanged;
		refreshllmlog();
	}

	void onllmcallschanged() {
		try { Dispatcher.BeginInvoke(new Action(refreshllmlog)); }
		catch { }
	}

	void refreshllmlog() {
		long keep = 0;
		if (llmloglist.SelectedItem is LlmLogRow cur) keep = cur.Id;
		var src = LlmCalls.Latest();
		var items = new LlmLogRow[src.Length];
		LlmLogRow sel = null;
		for (var i = 0; i < src.Length; i++) {
			items[i] = new LlmLogRow(src[i]);
			if (src[i].Id == keep) sel = items[i];
		}
		llmloglist.ItemsSource = items;
		if (sel != null) llmloglist.SelectedItem = sel;
	}

	void onllmlogsel(object sender, SelectionChangedEventArgs e) {
		if (llmloglist.SelectedItem is not LlmLogRow row) {
			lbllmlogurl.Text = "";
			ellmlogreq.Text = "";
			ellmlogresp.Text = "";
			return;
		}
		lbllmlogurl.Text = row.Url;
		ellmlogreq.Text = LlmCalls.Pretty(row.Request);
		var resp = LlmCalls.Pretty(row.Response);
		if (row.Error.Length > 0)
			resp = resp.Length > 0 ? $"{row.Error}\n\n{resp}" : row.Error;
		ellmlogresp.Text = resp;
	}

	void applyllmloglang() {
		tabllmlog.Header = Loc.T("tab.llmlog");
		lbllmlog.Text = Loc.T("tab.llmlog");
		lbllmloghint.Text = Loc.T("llmlog.hint");
		lbllmlogreq.Text = Loc.T("llmlog.req");
		lbllmlogresp.Text = Loc.T("llmlog.resp");
		collmlogtime.Header = Loc.T("llmlog.time");
		collmlogmodel.Header = Loc.T("llmlog.model");
		collmlogstatus.Header = Loc.T("llmlog.status");
		collmlogin.Header = Loc.T("llmlog.in");
		collmlogout.Header = Loc.T("llmlog.out");
		collmlogtotal.Header = Loc.T("llmlog.total");
		collmlogms.Header = Loc.T("llmlog.ms");
	}
}

sealed class LlmLogRow {
	public LlmLogRow(LlmCall c) {
		Id = c.Id;
		Time = c.Time ?? "";
		Model = c.Model ?? "";
		Url = c.Url ?? "";
		Status = c.Code == 0 && (c.Error ?? "").Length > 0 ? "ERR" : c.Code.ToString();
		In = tok(c.Prompt);
		Out = tok(c.Completion);
		Total = tok(c.Total);
		Ms = c.Ms.ToString();
		Request = c.Request ?? "";
		Response = c.Response ?? "";
		Error = c.Error ?? "";
	}

	public long Id { get; }
	public string Time { get; }
	public string Model { get; }
	public string Url { get; }
	public string Status { get; }
	public string In { get; }
	public string Out { get; }
	public string Total { get; }
	public string Ms { get; }
	public string Request { get; }
	public string Response { get; }
	public string Error { get; }

	static string tok(int n) => n < 0 ? "—" : n.ToString();
}
