using System.Windows.Automation;

namespace ScreenKit;

public partial class SciCalcWindow : Window {
	readonly SciCalc calc = new SciCalc();
	readonly List<KeyBtn> pads = new List<KeyBtn>();

	public SciCalcWindow() {
		InitializeComponent();
		Title = Loc.T("scicalc.title");
		WindowEsc.Attach(this);
		bclose.Click += (_, _) => Close();
		bdeg.Click += (_, _) => { calc.Press("deg"); paint(); };
		chrome.MouseLeftButtonDown += (_, e) => {
			if (e.OriginalSource is Button || e.OriginalSource == lbval) return;
			try { DragMove(); } catch { }
		};
		lbval.MouseLeftButtonUp += (_, e) => {
			try { Clipboard.SetText(lbval.Text ?? ""); } catch { }
			e.Handled = true;
		};
		PreviewKeyDown += onkey;
		buildkeys();
		paint();
	}

	void buildkeys() {
		add("second", "2nd", "2nd", "second", "", op: true);
		add("lp", "(", "(", "(", "");
		add("rp", ")", ")", ")", "");
		add("bs", "⌫", "⌫", "bs", "", op: true);
		add("ce", "CE", "CE", "ce", "", danger: true);
		add("ac", "AC", "AC", "ac", "", danger: true);
		add("sin", "sin", "sin⁻¹", "sin", "asin");
		add("cos", "cos", "cos⁻¹", "cos", "acos");
		add("tan", "tan", "tan⁻¹", "tan", "atan");
		add("ln", "ln", "eˣ", "ln", "exp");
		add("log", "log", "10ˣ", "log", "tenx");
		add("sqrt", "√", "³√", "sqrt", "cbrt");
		add("sq", "x²", "x⁻¹", "sq", "inv");
		add("pow", "xʸ", "ʸ√x", "pow", "nroot");
		add("fact", "n!", "|x|", "fact", "abs");
		add("inv", "1/x", "1/x", "inv", "");
		add("pi", "π", "π", "pi", "");
		add("e", "e", "e", "e", "");
		add("7", "7", "7", "7", "", digit: true);
		add("8", "8", "8", "8", "", digit: true);
		add("9", "9", "9", "9", "", digit: true);
		add("div", "/", "/", "/", "", op: true);
		add("pct", "%", "mod", "pct", "mod");
		add("ans", "Ans", "Ans", "ans", "");
		add("4", "4", "4", "4", "", digit: true);
		add("5", "5", "5", "5", "", digit: true);
		add("6", "6", "6", "6", "", digit: true);
		add("mul", "*", "*", "*", "", op: true);
		add("sign", "±", "±", "sign", "");
		add("expn", "EXP", "EXP", "expn", "");
		add("1", "1", "1", "1", "", digit: true);
		add("2", "2", "2", "2", "", digit: true);
		add("3", "3", "3", "3", "", digit: true);
		add("sub", "-", "-", "-", "", op: true);
		add("mc", "MC", "MC", "mc", "");
		add("mr", "MR", "MR", "mr", "");
		add("0", "0", "0", "0", "", digit: true);
		add("dot", ".", ".", ".", "");
		add("eq", "=", "=", "eq", "", eq: true);
		add("add", "+", "+", "+", "", op: true);
		add("mp", "M+", "M+", "mp", "");
		add("ms", "MS", "MS", "ms", "");
	}

	void add(string id, string lab, string lab2, string key, string key2,
		bool digit = false, bool op = false, bool danger = false, bool eq = false) {
		var b = new Button {
			Content = lab,
			Width = 46,
			Height = 30,
			Margin = new Thickness(2),
			Foreground = Brushes.White,
			BorderThickness = new Thickness(0),
			FontSize = digit || op || eq ? 14 : 11,
			Padding = new Thickness(0),
			Cursor = Cursors.Hand,
			Background = eq ? brush("#C2410C") : danger ? brush("#7F1D1D") : op ? brush("#3F3F46") : brush("#262626"),
		};
		AutomationProperties.SetAutomationId(b, "scicalc." + id);
		var row = new KeyBtn { btn = b, lab = lab, lab2 = lab2, key = key, key2 = key2, id = id };
		b.Click += (_, _) => tap(row);
		pads.Add(row);
		keys.Children.Add(b);
	}

	void tap(KeyBtn row) {
		var use2 = calc.Second && row.key2.Length > 0;
		var key = use2 ? row.key2 : row.key;
		calc.Press(key, use2);
		paint();
	}

	void paint() {
		lbexpr.Text = calc.Shown;
		lbval.Text = calc.Ok ? calc.Value : Loc.T("scicalc.err");
		lbmem.Text = calc.Memory == 0 ? "" : "M";
		bdeg.Content = calc.Deg ? "DEG" : "RAD";
		foreach (var row in pads) {
			if (row.id == "second") {
				row.btn.Content = "2nd";
				row.btn.Background = calc.Second ? brush("#C2410C") : brush("#3F3F46");
			}
			else if (row.lab2.Length > 0 && row.lab2 != row.lab)
				row.btn.Content = calc.Second ? row.lab2 : row.lab;
		}
	}

	void onkey(object sender, KeyEventArgs e) {
		var k = e.Key == Key.System ? e.SystemKey : e.Key;
		var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		string key = null;
		if (shift && k == Key.D8) key = "*";
		else if (shift && k == Key.D9) key = "(";
		else if (shift && k == Key.D0) key = ")";
		else if (!shift && k >= Key.D0 && k <= Key.D9) key = ((int)(k - Key.D0)).ToString();
		else if (k >= Key.NumPad0 && k <= Key.NumPad9) key = ((int)(k - Key.NumPad0)).ToString();
		else if (k == Key.Decimal || k == Key.OemPeriod) key = ".";
		else if (k == Key.Add || (shift && k == Key.OemPlus)) key = "+";
		else if (k == Key.OemPlus) key = "eq";
		else if (k == Key.Subtract || k == Key.OemMinus) key = "-";
		else if (k == Key.Multiply) key = "*";
		else if (k == Key.Divide || (!shift && k == Key.Oem2)) key = "/";
		else if (k == Key.Enter) key = "eq";
		else if (k == Key.Back) key = "bs";
		else if (k == Key.Delete) key = "ac";
		if (key == null) return;
		calc.Press(key);
		paint();
		e.Handled = true;
	}

	static SolidColorBrush brush(string hex) {
		var c = (Color)ColorConverter.ConvertFromString(hex);
		var b = new SolidColorBrush(c);
		b.Freeze();
		return b;
	}

	sealed class KeyBtn {
		public Button btn;
		public string id;
		public string lab;
		public string lab2;
		public string key;
		public string key2;
	}
}
