using System.Windows.Threading;

namespace ScreenKit;

public partial class CastUsbWaitWindow : Window {
	static CastUsbWaitWindow cur;
	readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
	bool closing;

	public static void ShowWait(Window owner) {
		CastHost.BeginUsbAccessoryWait();
		if (cur != null) {
			try { cur.Activate(); } catch { }
			return;
		}
		var w = new CastUsbWaitWindow();
		if (owner != null && owner.IsVisible) {
			w.Owner = owner;
			w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		}
		cur = w;
		w.Closed += (_, _) => { if (cur == w) cur = null; };
		w.ShowDialog();
	}

	public CastUsbWaitWindow() {
		InitializeComponent();
		if (CastHost.WinIcon != null) Icon = CastHost.WinIcon;
		Title = Loc.T("tray.usbacc");
		WindowEsc.Attach(this, () => Close());
		Closing += (_, _) => onclose();
		timer.Tick += (_, _) => tick();
		timer.Start();
		tick();
	}

	void tick() {
		var linked = CastHost.UsbLinked();
		lbst.Text = Loc.T("usbacc.line", CastHost.UsbAccStatus());
		if (linked) {
			lb.Text = Loc.T("usbacc.modal.on");
			Close();
			return;
		}
		if (!CastHost.UsbAccessoryOn) {
			lb.Text = Loc.T("usbacc.off");
			Close();
			return;
		}
		lb.Text = Loc.T("usbacc.modal");
		bclose.Content = Loc.T("usbacc.cancel");
	}

	void bclose_Click(object sender, RoutedEventArgs e) => Close();

	void onclose() {
		if (closing) return;
		closing = true;
		timer.Stop();
		CastHost.UsbNotifyPhone = false;
		if (!CastHost.UsbLinked())
			CastHost.SetUsbAccessory(false);
	}
}
