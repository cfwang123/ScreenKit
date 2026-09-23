using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ScreenKit;

sealed class SfFileRow {
	public string Full { get; set; } = "";
	public string Name { get; set; } = "";
	public bool IsDir { get; set; }
	public long Size { get; set; }
	public DateTime Mtime { get; set; }
	public string SizeText { get; set; } = "";
	public string TimeText { get; set; } = "";
}

/// <summary>文件同步 Tab：接收目录平铺浏览（不进子目录）。</summary>
public partial class MainWindow {
	readonly ObservableCollection<SfFileRow> sfFiles = new();
	FileSystemWatcher sfWatch;
	System.Windows.Threading.DispatcherTimer sfWatchTick;
	bool sfMarquee;
	bool sfDragReady;
	bool sfDragging;
	SfFileRow sfClickSel;
	Point sfDown;
	Point sfMarquee0;
	HashSet<SfFileRow> sfMarqueeKeep;

	void initsfbrowse() {
		if (lvsffiles == null) return;
		lvsffiles.ItemsSource = sfFiles;
		bsfcut.Click += (_, _) => sffileclip(cut: true);
		bsfcopy.Click += (_, _) => sffileclip(cut: false);
		bsfdel.Click += (_, _) => sffiledel();
		mnsfcut.Click += (_, _) => sffileclip(cut: true);
		mnsfcopy.Click += (_, _) => sffileclip(cut: false);
		mnsfpaste.Click += (_, _) => sffilepaste();
		mnsfdel.Click += (_, _) => sffiledel();
		lvsffiles.SelectionChanged += (_, _) => sffilebtns();
		lvsffiles.MouseDoubleClick += (_, _) => sffileopen();
		lvsffiles.PreviewMouseLeftButtonDown += onsffiledown;
		lvsffiles.PreviewMouseMove += onsffilemove;
		lvsffiles.PreviewMouseLeftButtonUp += onsffileup;
		lvsffiles.LostMouseCapture += (_, _) => sffileendmarquee();
		psffbrowse.Drop += onsffinboxdrop;
		psffbrowse.DragOver += onsffinboxover;
		if (psfdropphone != null) {
			psfdropphone.Drop += onsfdrop;
			psfdropphone.DragOver += onsfdover;
		}
		sffilebtns();
		sffilerefresh();
		sffilewatch();
	}

	void sffilewatch() {
		try { sfWatch?.Dispose(); } catch { }
		sfWatch = null;
		try { sfWatchTick?.Stop(); } catch { }
		sfWatchTick = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromMilliseconds(250),
		};
		sfWatchTick.Tick += (_, _) => {
			sfWatchTick.Stop();
			sffilerefresh();
		};
		try {
			SendFilePaths.EnsureRoot();
			var root = SendFilePaths.Root();
			sfWatch = new FileSystemWatcher(root) {
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
					| NotifyFilters.LastWrite | NotifyFilters.Size,
				IncludeSubdirectories = false,
				EnableRaisingEvents = true,
			};
			FileSystemEventHandler kick = (_, _) => sffilekick();
			RenamedEventHandler kick2 = (_, _) => sffilekick();
			sfWatch.Created += kick;
			sfWatch.Deleted += kick;
			sfWatch.Changed += kick;
			sfWatch.Renamed += kick2;
		}
		catch { }
	}

	void sffilekick() {
		try {
			Dispatcher.BeginInvoke(new Action(() => {
				if (sfWatchTick == null) return;
				sfWatchTick.Stop();
				sfWatchTick.Start();
			}));
		}
		catch { }
	}

	void sffilerefresh() {
		if (lvsffiles == null) return;
		var keep = new HashSet<string>(
			lvsffiles.SelectedItems.Cast<SfFileRow>().Select(x => x.Full),
			StringComparer.OrdinalIgnoreCase);
		sfFiles.Clear();
		try {
			SendFilePaths.EnsureRoot();
			var root = SendFilePaths.Root();
			if (!Directory.Exists(root)) return;
			var rows = new List<SfFileRow>();
			foreach (var d in Directory.GetDirectories(root)) {
				if (sfhidden(d)) continue;
				rows.Add(sffilerow(d, dir: true));
			}
			foreach (var f in Directory.GetFiles(root)) {
				if (sfhidden(f)) continue;
				rows.Add(sffilerow(f, dir: false));
			}
			rows.Sort((a, b) => {
				if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
				return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
			});
			foreach (var r in rows)
				sfFiles.Add(r);
			foreach (var r in sfFiles) {
				if (keep.Contains(r.Full))
					lvsffiles.SelectedItems.Add(r);
			}
		}
		catch (Exception ex) {
			setstatus(ex.Message);
		}
		if (lbsffempty != null)
			lbsffempty.Visibility = sfFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		sffilebtns();
	}

	static bool sfhidden(string full) {
		var name = Path.GetFileName(full);
		return !string.IsNullOrEmpty(name) && name[0] == '.';
	}

	static SfFileRow sffilerow(string full, bool dir) {
		var name = Path.GetFileName(full) ?? full;
		long size = 0;
		var mtime = DateTime.Now;
		try {
			if (dir) mtime = Directory.GetLastWriteTime(full);
			else {
				var fi = new FileInfo(full);
				size = fi.Length;
				mtime = fi.LastWriteTime;
			}
		}
		catch { }
		return new SfFileRow {
			Full = full,
			Name = name,
			IsDir = dir,
			Size = size,
			Mtime = mtime,
			SizeText = dir ? Loc.T("sf.dir") : SfJobRow.SizeText(size),
			TimeText = mtime.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
		};
	}

	void sffilebtns() {
		var n = lvsffiles?.SelectedItems.Count ?? 0;
		var on = n > 0;
		if (bsfcut != null) bsfcut.IsEnabled = on;
		if (bsfcopy != null) bsfcopy.IsEnabled = on;
		if (bsfdel != null) bsfdel.IsEnabled = on;
		if (mnsfcut != null) mnsfcut.IsEnabled = on;
		if (mnsfcopy != null) mnsfcopy.IsEnabled = on;
		if (mnsfdel != null) mnsfdel.IsEnabled = on;
	}

	List<string> sffilesel() {
		var r = new List<string>();
		if (lvsffiles == null) return r;
		foreach (SfFileRow it in lvsffiles.SelectedItems) {
			if (it != null && !string.IsNullOrWhiteSpace(it.Full))
				r.Add(it.Full);
		}
		return r;
	}

	static System.Windows.DataObject sffiledropdata(IEnumerable<string> paths) {
		var files = new StringCollection();
		foreach (var p in paths ?? Array.Empty<string>()) {
			if (!string.IsNullOrWhiteSpace(p))
				files.Add(p);
		}
		var data = new System.Windows.DataObject();
		data.SetFileDropList(files);
		return data;
	}

	void sffileclip(bool cut) {
		var paths = sffilesel();
		if (paths.Count == 0) return;
		try {
			var data = sffiledropdata(paths);
			var fx = cut ? DragDropEffects.Move : DragDropEffects.Copy;
			data.SetData("Preferred DropEffect",
				new MemoryStream(BitConverter.GetBytes((int)fx)));
			Clipboard.SetDataObject(data, true);
		}
		catch (Exception ex) {
			setstatus(ex.Message);
		}
	}

	void sffilepaste() {
		try {
			if (Clipboard.ContainsFileDropList()) {
				var list = Clipboard.GetFileDropList();
				if (list != null && list.Count > 0) {
					var paths = new string[list.Count];
					list.CopyTo(paths, 0);
					var move = sffileclipcut();
					var n = sffileimport(paths, move);
					if (move && n > 0) {
						try { Clipboard.Clear(); } catch { }
					}
					setstatus(Loc.T("sf.paste.ok", n.ToString()));
					sffilerefresh();
					return;
				}
			}
		}
		catch (Exception ex) {
			setstatus(ex.Message);
			return;
		}
		try {
			if (Clipboard.ContainsImage()) {
				var bmp = ImageUtil.Fromclipboard();
				if (bmp != null) {
					sffilesaveimg(bmp);
					return;
				}
			}
		}
		catch { }
		try {
			if (Clipboard.ContainsText()) {
				var t = Clipboard.GetText() ?? "";
				if (!string.IsNullOrWhiteSpace(t)) {
					sfpushtext(t.TrimEnd());
					return;
				}
			}
		}
		catch { }
		setstatus(Loc.T("sf.paste.empty"));
	}

	static bool sffileclipcut() {
		try {
			var data = Clipboard.GetDataObject();
			if (data == null || !data.GetDataPresent("Preferred DropEffect")) return false;
			if (data.GetData("Preferred DropEffect") is not MemoryStream ms) return false;
			var buf = new byte[4];
			if (ms.Read(buf, 0, 4) < 4) return false;
			var fx = BitConverter.ToInt32(buf, 0);
			return (fx & (int)DragDropEffects.Move) != 0;
		}
		catch {
			return false;
		}
	}

	int sffileimport(IEnumerable<string> paths, bool move) {
		SendFilePaths.EnsureRoot();
		var root = SendFilePaths.Root();
		var n = 0;
		foreach (var raw in paths ?? Array.Empty<string>()) {
			if (string.IsNullOrWhiteSpace(raw)) continue;
			string src;
			try { src = Path.GetFullPath(raw); }
			catch { continue; }
			if (!File.Exists(src) && !Directory.Exists(src)) continue;
			var name = Path.GetFileName(src);
			if (string.IsNullOrEmpty(name) || name[0] == '.') continue;
			var dest = Path.Combine(root, name);
			if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase)) {
				if (move) continue;
				try {
					SendFileOps.Import(src, "");
					n++;
				}
				catch { }
				continue;
			}
			try {
				if (move) {
					if (File.Exists(dest) || Directory.Exists(dest)) {
						var rel = SendFileOps.UniqueRel(name, Directory.Exists(src));
						if (!SendFilePaths.TryResolve(rel, out dest, out var err))
							throw new InvalidOperationException(err ?? "path");
					}
					if (Directory.Exists(src)) Directory.Move(src, dest);
					else File.Move(src, dest);
				}
				else
					SendFileOps.Import(src, "");
				n++;
			}
			catch (Exception ex) {
				setstatus(ex.Message);
			}
		}
		return n;
	}

	void sffilesaveimg(BitmapSource bmp) {
		if (bmp == null) return;
		var name = "clip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png";
		var destRel = SendFileOps.UniqueRel(name, dir: false);
		if (!SendFilePaths.TryResolve(destRel, out var full, out var err)) {
			setstatus(err ?? Loc.T("st.sendfile_fail", "path"));
			return;
		}
		try {
			var dir = Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			ImageUtil.Savefile(bmp, full);
			setstatus(Loc.T("sf.paste.img", name));
			sffilerefresh();
		}
		catch (Exception ex) {
			setstatus(ex.Message);
		}
	}

	void sffiledel() {
		var paths = sffilesel();
		if (paths.Count == 0) return;
		var ask = paths.Count == 1
			? Loc.T("sf.del.ask", Path.GetFileName(paths[0]))
			: Loc.T("sf.del.askn", paths.Count);
		var r = MessageBox.Show(this, ask, Loc.T("sf.del.title"),
			MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
		if (r != MessageBoxResult.Yes) return;
		try {
			RecycleBin.Send(paths);
			sffilerefresh();
		}
		catch (Exception ex) {
			setstatus(ex.Message);
		}
	}

	void sffileopen() {
		var it = lvsffiles?.SelectedItem as SfFileRow;
		if (it == null || string.IsNullOrWhiteSpace(it.Full)) return;
		try {
			if (it.IsDir) {
				Process.Start(new ProcessStartInfo {
					FileName = "explorer.exe", Arguments = $"\"{it.Full}\"", UseShellExecute = true,
				});
				return;
			}
			Process.Start(new ProcessStartInfo { FileName = it.Full, UseShellExecute = true });
		}
		catch (Exception ex) {
			setstatus(ex.Message);
		}
	}

	void onsffinboxover(object sender, DragEventArgs e) {
		if (sfDragging) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
		e.Effects = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
			? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	void onsffinboxdrop(object sender, DragEventArgs e) {
		e.Handled = true;
		if (sfDragging) return;
		if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (files == null || files.Length == 0) return;
		var n = sffileimport(files, move: false);
		setstatus(Loc.T("sf.paste.ok", n.ToString()));
		sffilerefresh();
	}

	void onsffiledown(object sender, MouseButtonEventArgs e) {
		sfDown = e.GetPosition(null);
		sfDragReady = false;
		sfMarquee = false;
		sfClickSel = null;
		if (e.ChangedButton != MouseButton.Left) return;
		if (sffilechrome(e.OriginalSource as DependencyObject)) return;
		var item = sffilehit(e.OriginalSource as DependencyObject);
		if (item == null) {
			if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
				lvsffiles.SelectedItems.Clear();
			sfMarquee = true;
			sfMarquee0 = e.GetPosition(csfsel);
			sfMarqueeKeep = new HashSet<SfFileRow>(
				lvsffiles.SelectedItems.Cast<SfFileRow>());
			lvsffiles.CaptureMouse();
			e.Handled = true;
			return;
		}
		sfDragReady = true;
		if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
			return;
		if (lvsffiles.SelectedItems.Contains(item)) {
			sfClickSel = item;
			e.Handled = true;
			return;
		}
		lvsffiles.SelectedItems.Clear();
		lvsffiles.SelectedItems.Add(item);
	}

	void onsffilemove(object sender, MouseEventArgs e) {
		if (e.LeftButton != MouseButtonState.Pressed) return;
		var pos = e.GetPosition(null);
		if (sfMarquee) {
			sffilemarquee(e.GetPosition(csfsel));
			return;
		}
		if (!sfDragReady || sfDragging) return;
		if (Math.Abs(pos.X - sfDown.X) < SystemParameters.MinimumHorizontalDragDistance
			&& Math.Abs(pos.Y - sfDown.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;
		var paths = sffilesel();
		if (paths.Count == 0) return;
		try {
			sfDragging = true;
			sfClickSel = null;
			var data = sffiledropdata(paths);
			DragDrop.DoDragDrop(lvsffiles, data, DragDropEffects.Copy | DragDropEffects.Move);
		}
		catch { }
		finally {
			sfDragging = false;
			sfDragReady = false;
			sffilerefresh();
		}
	}

	void onsffileup(object sender, MouseButtonEventArgs e) {
		sfDragReady = false;
		if (sfClickSel != null && !sfMarquee
			&& (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0) {
			lvsffiles.SelectedItems.Clear();
			lvsffiles.SelectedItems.Add(sfClickSel);
		}
		sfClickSel = null;
		sffileendmarquee();
	}

	void sffilemarquee(Point now) {
		if (csfsel == null || rsfsel == null) return;
		var x = Math.Min(sfMarquee0.X, now.X);
		var y = Math.Min(sfMarquee0.Y, now.Y);
		var w = Math.Abs(now.X - sfMarquee0.X);
		var h = Math.Abs(now.Y - sfMarquee0.Y);
		Canvas.SetLeft(rsfsel, x);
		Canvas.SetTop(rsfsel, y);
		rsfsel.Width = w;
		rsfsel.Height = h;
		rsfsel.Visibility = Visibility.Visible;
		var box = new Rect(x, y, Math.Max(1, w), Math.Max(1, h));
		var keepCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
		lvsffiles.SelectedItems.Clear();
		if (keepCtrl && sfMarqueeKeep != null) {
			foreach (var it in sfMarqueeKeep)
				lvsffiles.SelectedItems.Add(it);
		}
		foreach (var row in sfFiles) {
			if (lvsffiles.ItemContainerGenerator.ContainerFromItem(row) is not ListViewItem lvi)
				continue;
			var tl = lvi.TranslatePoint(new Point(0, 0), csfsel);
			var bounds = new Rect(tl, lvi.RenderSize);
			if (bounds.IntersectsWith(box) && !lvsffiles.SelectedItems.Contains(row))
				lvsffiles.SelectedItems.Add(row);
		}
	}

	void sffileendmarquee() {
		if (!sfMarquee) return;
		sfMarquee = false;
		try { lvsffiles.ReleaseMouseCapture(); } catch { }
		if (rsfsel != null) rsfsel.Visibility = Visibility.Collapsed;
		sfMarqueeKeep = null;
	}

	static SfFileRow sffilehit(DependencyObject src) {
		while (src != null) {
			if (src is ListViewItem lvi)
				return lvi.DataContext as SfFileRow;
			src = VisualTreeHelper.GetParent(src);
		}
		return null;
	}

	static bool sffilechrome(DependencyObject src) {
		while (src != null) {
			if (src is GridViewColumnHeader || src is ScrollBar)
				return true;
			src = VisualTreeHelper.GetParent(src);
		}
		return false;
	}

	void onsftabkey(KeyEventArgs e) {
		if (esfsend != null && esfsend.IsKeyboardFocusWithin) return;
		if (esfmsg != null && esfmsg.IsKeyboardFocusWithin) return;
		var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
		if (ctrl && e.Key == Key.C) { sffileclip(cut: false); e.Handled = true; return; }
		if (ctrl && e.Key == Key.X) { sffileclip(cut: true); e.Handled = true; return; }
		if (ctrl && e.Key == Key.V) { sffilepaste(); e.Handled = true; return; }
		if (ctrl && e.Key == Key.A) {
			lvsffiles?.SelectAll();
			e.Handled = true;
			return;
		}
		if (e.Key == Key.Delete) { sffiledel(); e.Handled = true; }
	}
}
