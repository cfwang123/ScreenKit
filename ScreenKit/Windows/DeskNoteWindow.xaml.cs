using System.Windows.Threading;

namespace ScreenKit;

public partial class DeskNoteWindow : Window {
	readonly DeskNote note;
	readonly string path;
	readonly DispatcherTimer timer;
	bool ready;

	public DeskNoteWindow() : this(DeskNote.DefaultPath) { }

	public DeskNoteWindow(string path) {
		this.path = path ?? DeskNote.DefaultPath;
		note = DeskNote.Load(this.path);
		InitializeComponent();
		Title = Loc.T("note.title");
		lbtitle.Text = Title;
		btodo.Content = Loc.T("note.todo");
		badd.Content = Loc.T("note.add");
		bclose.Click += (_, _) => Close();
		btodo.Click += (_, _) => toggle();
		badd.Click += (_, _) => additem(null);
		bar.MouseLeftButtonDown += (_, e) => {
			if (e.OriginalSource is Button) return;
			try { DragMove(); } catch { }
		};
		bsz.DragDelta += (_, e) => {
			Width = Math.Max(MinWidth, Width + e.HorizontalChange);
			Height = Math.Max(MinHeight, Height + e.VerticalChange);
		};
		etext.Text = note.Text ?? "";
		etext.TextChanged += (_, _) => {
			note.Text = etext.Text ?? "";
			queue();
		};
		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
		timer.Tick += (_, _) => {
			timer.Stop();
			flush();
		};
		Loaded += (_, _) => {
			place();
			showmode();
			ready = true;
		};
		Closing += (_, _) => flush();
		LocationChanged += (_, _) => queue();
		SizeChanged += (_, _) => queue();
	}

	void place() {
		if (!note.HasPos) return;
		WindowStartupLocation = WindowStartupLocation.Manual;
		if (note.Width >= MinWidth) Width = note.Width;
		if (note.Height >= MinHeight) Height = note.Height;
		Left = note.Left;
		Top = note.Top;
	}

	void toggle() {
		if (note.Todo) {
			note.Text = DeskNote.Format(note.Items);
			etext.Text = note.Text;
			note.Todo = false;
		}
		else {
			note.Items = DeskNote.Parse(etext.Text ?? "");
			note.Text = DeskNote.Format(note.Items);
			note.Todo = true;
		}
		showmode();
		queue();
	}

	void showmode() {
		btodo.Content = note.Todo ? Loc.T("note.text") : Loc.T("note.todo");
		etext.Visibility = note.Todo ? Visibility.Collapsed : Visibility.Visible;
		ptodo.Visibility = note.Todo ? Visibility.Visible : Visibility.Collapsed;
		badd.Visibility = note.Todo ? Visibility.Visible : Visibility.Collapsed;
		if (note.Todo) fillrows();
	}

	void fillrows() {
		rows.Children.Clear();
		if (note.Items.Count == 0) note.Items.Add(new DeskNoteItem());
		foreach (var item in note.Items) rows.Children.Add(rowof(item));
	}

	Grid rowof(DeskNoteItem item) {
		var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		var ck = new CheckBox {
			IsChecked = item.Done,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(Color.FromRgb(0x29, 0x25, 0x24)),
		};
		var box = new TextBox {
			Text = item.Text ?? "",
			BorderThickness = new Thickness(0),
			Background = Brushes.Transparent,
			Foreground = new SolidColorBrush(Color.FromRgb(0x29, 0x25, 0x24)),
			CaretBrush = Brushes.Black,
			FontSize = 15,
			Padding = new Thickness(2, 0, 0, 0),
			TextDecorations = item.Done ? TextDecorations.Strikethrough : null,
		};
		Grid.SetColumn(box, 1);
		ck.Checked += (_, _) => mark(item, box, true);
		ck.Unchecked += (_, _) => mark(item, box, false);
		box.TextChanged += (_, _) => {
			item.Text = box.Text ?? "";
			note.Text = DeskNote.Format(note.Items);
			queue();
		};
		box.PreviewKeyDown += (_, e) => onrowkey(item, box, e);
		grid.Children.Add(ck);
		grid.Children.Add(box);
		return grid;
	}

	void mark(DeskNoteItem item, TextBox box, bool done) {
		item.Done = done;
		box.TextDecorations = done ? TextDecorations.Strikethrough : null;
		note.Text = DeskNote.Format(note.Items);
		queue();
	}

	void onrowkey(DeskNoteItem item, TextBox box, KeyEventArgs e) {
		if (e.Key == Key.Enter) {
			e.Handled = true;
			additem(item);
			return;
		}
		if (e.Key != Key.Back || box.Text.Length > 0 || note.Items.Count < 2) return;
		e.Handled = true;
		var i = note.Items.IndexOf(item);
		note.Items.Remove(item);
		note.Text = DeskNote.Format(note.Items);
		fillrows();
		if (i >= note.Items.Count) i = note.Items.Count - 1;
		focusrow(i);
		queue();
	}

	void additem(DeskNoteItem after) {
		var item = new DeskNoteItem();
		var i = after == null ? note.Items.Count : note.Items.IndexOf(after) + 1;
		if (i < 0) i = note.Items.Count;
		note.Items.Insert(i, item);
		note.Text = DeskNote.Format(note.Items);
		fillrows();
		focusrow(i);
		queue();
	}

	void focusrow(int index) {
		if (index < 0 || index >= rows.Children.Count) return;
		if (rows.Children[index] is not Grid grid) return;
		foreach (var child in grid.Children) {
			if (child is TextBox box) {
				box.Focus();
				box.CaretIndex = box.Text.Length;
				return;
			}
		}
	}

	void queue() {
		if (!ready) return;
		timer.Stop();
		timer.Start();
	}

	void flush() {
		timer.Stop();
		if (!double.IsNaN(Left) && !double.IsNaN(Top)) {
			note.Left = Left;
			note.Top = Top;
			note.Width = Width;
			note.Height = Height;
			note.HasPos = true;
		}
		try { note.Save(path); } catch { }
	}
}
