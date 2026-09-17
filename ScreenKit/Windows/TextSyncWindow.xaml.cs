using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace ScreenKit;

sealed class TextSyncRow {
	public string Line { get; set; } = "";
	public string Full { get; set; } = "";
}

public partial class TextSyncWindow : Window {
	readonly SendFileServer server;
	readonly ObservableCollection<TextSyncRow> rows = new();

	public TextSyncWindow(SendFileServer srv) {
		server = srv ?? throw new ArgumentNullException(nameof(srv));
		InitializeComponent();
		lstmsgs.ItemsSource = rows;
		Title = Loc.T("sendfile.text.title");
		lbhint.Text = Loc.T("sendfile.text.hint");
		bsend.Content = Loc.T("sendfile.text.send");
		bsend.Click += (_, _) => onsend();
		Closed += (_, _) => {
			try { server.Text.InboxArrived -= oninbox; } catch { }
		};
		server.Text.InboxArrived += oninbox;
		esend.Focus();
	}

	void oninbox(SendFileMsg msg) {
		if (msg == null) return;
		try {
			Dispatcher.BeginInvoke(new Action(() => addrow(msg.Text)));
		}
		catch { }
	}

	void onsend() {
		var text = (esend.Text ?? "").Trim();
		if (text.Length == 0) return;
		var id = server.LastDeviceId;
		if (string.IsNullOrEmpty(id))
			id = server.Auth.FirstDeviceId();
		if (string.IsNullOrEmpty(id)) {
			MessageBox.Show(this, Loc.T("sendfile.text.nophone"), Loc.T("sendfile.text.title"),
				MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		server.Text.PushOut(id, text);
		esend.Clear();
	}

	void addrow(string full) {
		full = full ?? "";
		var line = oneline(full);
		rows.Insert(0, new TextSyncRow { Line = line, Full = full });
		while (rows.Count > 200)
			rows.RemoveAt(rows.Count - 1);
	}

	static string oneline(string s) {
		s = (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
		var i = s.IndexOf('\n');
		if (i >= 0) s = s.Substring(0, i) + "…";
		return s;
	}

	void oncopy(object sender, RoutedEventArgs e) {
		var row = (sender as FrameworkElement)?.Tag as TextSyncRow
			?? (sender as Button)?.DataContext as TextSyncRow;
		if (row == null) return;
		try {
			Clipboard.SetText(row.Full ?? "");
		}
		catch (Exception ex) {
			MessageBox.Show(this, Loc.T("st.copy_fail", ex.Message), Loc.T("sendfile.text.title"),
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
}
