using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenKit;

sealed class SfFileRow : INotifyPropertyChanged {
	public string Full { get; set; } = "";
	public string Name { get; set; } = "";
	public bool IsDir { get; set; }
	public long Size { get; set; }
	public DateTime Mtime { get; set; }
	public string SizeText { get; set; } = "";
	public string TimeText { get; set; } = "";
	ImageSource thumb;
	public ImageSource Thumb {
		get => thumb;
		set {
			if (ReferenceEquals(thumb, value)) return;
			thumb = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb)));
		}
	}
	public event PropertyChangedEventHandler PropertyChanged;
}

/// <summary>文件同步 Tab：接收目录平铺浏览（不进子目录）。</summary>
public partial class MainWindow {
	readonly ObservableCollection<SfFileRow> sfFiles = new();
	FileSystemWatcher sfWatch;
	System.Windows.Threading.DispatcherTimer sfWatchTick;
	bool sfMarquee;
	bool sfDragReady;
	bool sfCanDrag;
	bool sfDragging;
	bool sfRefreshing;
	bool sfSelBatch;
	SfFileRow sfClickSel;
	Point sfDown;
	Point sfMarquee0;
	Point sfMarqueePt;
	HashSet<SfFileRow> sfMarqueeKeep;
	GridView sfGridView;
	readonly HashSet<string> sfCutPaths = new(StringComparer.OrdinalIgnoreCase);
	readonly SemaphoreSlim sfThumbGate = new(4);
	int sfLastIdx = -1;
	const double sfCutOpacity = 0.42;

	void initsfbrowse() {
		if (lvsffiles == null) return;
		sfGridView = lvsffiles.View as GridView;
		lvsffiles.IsSynchronizedWithCurrentItem = false;
		lvsffiles.SelectionMode = SelectionMode.Multiple;
		lvsffiles.ItemsSource = sfFiles;
		if (bsfviewlist != null) bsfviewlist.Click += (_, _) => sfsetview(false);
		if (bsfviewthumb != null) bsfviewthumb.Click += (_, _) => sfsetview(true);
		bsfcut.Click += (_, _) => sffileclip(cut: true);
		bsfcopy.Click += (_, _) => sffileclip(cut: false);
		bsfdel.Click += (_, _) => sffiledel();
		if (bsfpush != null) bsfpush.Click += (_, _) => sffilepush();
		mnsfcut.Click += (_, _) => sffileclip(cut: true);
		mnsfcopy.Click += (_, _) => sffileclip(cut: false);
		mnsfpaste.Click += (_, _) => sffilepaste();
		mnsfdel.Click += (_, _) => sffiledel();
		lvsffiles.SelectionChanged += (_, _) => {
			if (sfSelBatch) return;
			sffileonselectionchanged();
		};
		lvsffiles.MouseDoubleClick += (_, _) => sffileopen();
		lvsffiles.PreviewMouseLeftButtonDown += onsffiledown;
		lvsffiles.MouseLeftButtonDown += (_, _) => { sfCanDrag = true; };
		lvsffiles.PreviewMouseMove += onsffilemove;
		lvsffiles.PreviewMouseLeftButtonUp += onsffileup;
		lvsffiles.LostMouseCapture += (_, _) => {
			if (sfDragging || !sfMarquee) return;
			if (Mouse.LeftButton == MouseButtonState.Pressed && lvsffiles.CaptureMouse()) return;
			sffilemarqueesel();
			sffileendmarquee();
		};
		psffbrowse.Drop += onsffinboxdrop;
		psffbrowse.DragOver += onsffinboxover;
		if (psfdropphone != null) {
			psfdropphone.Drop += onsfdrop;
			psfdropphone.DragOver += onsfdover;
		}
		sffilebtns();
		if (bsfviewlist != null) bsfviewlist.IsChecked = !opt.SfBrowseThumbView;
		if (bsfviewthumb != null) bsfviewthumb.IsChecked = opt.SfBrowseThumbView;
		sfapplyview();
		sffilerefresh();
		sffilewatch();
	}

	void sfsetview(bool thumbs) {
		if (bsfviewthumb?.IsChecked == thumbs && bsfviewlist?.IsChecked == !thumbs) return;
		if (bsfviewthumb != null) bsfviewthumb.IsChecked = thumbs;
		if (bsfviewlist != null) bsfviewlist.IsChecked = !thumbs;
		opt.SfBrowseThumbView = thumbs;
		sfapplyview();
		if (thumbs) sfloadthumbs();
	}

	void sfapplyview() {
		if (lvsffiles == null) return;
		var thumbs = bsfviewthumb?.IsChecked == true;
		if (thumbs) {
			lvsffiles.View = null;
			lvsffiles.ItemTemplate = psffbrowse.TryFindResource("sfTplThumb") as DataTemplate;
			lvsffiles.ItemsPanel = psffbrowse.TryFindResource("sfPanelWrap") as ItemsPanelTemplate;
			lvsffiles.ItemContainerStyle = psffbrowse.TryFindResource("sfStyleThumbItem") as Style;
			ScrollViewer.SetCanContentScroll(lvsffiles, false);
		}
		else {
			lvsffiles.ItemTemplate = null;
			lvsffiles.ItemsPanel = psffbrowse.TryFindResource("sfPanelList") as ItemsPanelTemplate;
			lvsffiles.ItemContainerStyle = psffbrowse.TryFindResource("sfStyleListItem") as Style;
			lvsffiles.View = sfGridView;
			ScrollViewer.SetCanContentScroll(lvsffiles, true);
		}
		sffileapplycutui();
	}

	void sfloadthumbs() {
		if (bsfviewthumb?.IsChecked != true) return;
		foreach (var row in sfFiles) {
			if (row.IsDir || row.Thumb != null) continue;
			_ = sfloadthumb(row);
		}
	}

	async Task sfloadthumb(SfFileRow row) {
		if (row == null || row.IsDir) return;
		try {
			await sfThumbGate.WaitAsync().ConfigureAwait(true);
			BitmapSource bmp = null;
			var full = row.Full;
			try {
				bmp = await Task.Run(() => sfmkthumb(full)).ConfigureAwait(true);
			}
			finally {
				try { sfThumbGate.Release(); } catch { }
			}
			if (bmp == null || !sfFiles.Contains(row)) return;
			row.Thumb = bmp;
		}
		catch { }
	}

	static BitmapSource sfmkthumb(string full) {
		if (string.IsNullOrWhiteSpace(full)) return null;
		if (Directory.Exists(full)) return null;
		if (!File.Exists(full)) return null;
		var ext = Path.GetExtension(full)?.ToLowerInvariant() ?? "";
		if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".jfif") {
			try {
				using var fs = File.OpenRead(full);
				var bi = new BitmapImage();
				bi.BeginInit();
				bi.CacheOption = BitmapCacheOption.OnLoad;
				bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				bi.DecodePixelWidth = 72;
				bi.StreamSource = fs;
				bi.EndInit();
				bi.Freeze();
				return bi;
			}
			catch {
				return sfmkicon(full);
			}
		}
		return sfmkicon(full);
	}

	static BitmapSource sfmkicon(string full) {
		try {
			using var icon = System.Drawing.Icon.ExtractAssociatedIcon(full);
			if (icon == null) return null;
			var src = Imaging.CreateBitmapSourceFromHIcon(
				icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			src.Freeze();
			return src;
		}
		catch {
			return null;
		}
	}

	void sffileonselectionchanged() {
		sffilebtns();
		if (lvsffiles != null && lvsffiles.SelectedItems.Count == 1
			&& lvsffiles.SelectedItems[0] is SfFileRow one) {
			var ix = sfFiles.IndexOf(one);
			if (ix >= 0) sfLastIdx = ix;
		}
		sffileapplycutui();
	}

	void sffileapplycutui() {
		if (lvsffiles == null || sfMarquee) return;
		foreach (var row in sfFiles) {
			if (lvsffiles.ItemContainerGenerator.ContainerFromItem(row) is not ListViewItem lvi) continue;
			lvi.Opacity = sfCutPaths.Contains(row.Full) ? sfCutOpacity : 1.0;
		}
	}

	static bool sfsameSel(IList cur, HashSet<SfFileRow> want) {
		if (cur.Count != want.Count) return false;
		foreach (SfFileRow it in cur) {
			if (!want.Contains(it)) return false;
		}
		return true;
	}

	void sfapplysel(HashSet<SfFileRow> want) {
		if (lvsffiles == null || want == null) return;
		if (sfsameSel(lvsffiles.SelectedItems, want)) return;
		sfSelBatch = true;
		try {
			lvsffiles.SelectedItems.Clear();
			foreach (var row in want) {
				var live = sflive(row);
				if (live == null || lvsffiles.SelectedItems.Contains(live)) continue;
				lvsffiles.SelectedItems.Add(live);
			}
		}
		finally {
			sfSelBatch = false;
		}
		sffileonselectionchanged();
	}

	void sfclearcut() {
		if (sfCutPaths.Count == 0) return;
		sfCutPaths.Clear();
		sffileapplycutui();
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
		if (lvsffiles == null || sfRefreshing || sfDragging) return;
		sfRefreshing = true;
		try {
			var keep = new HashSet<string>(
				lvsffiles.SelectedItems.OfType<SfFileRow>().Select(x => x.Full),
				StringComparer.OrdinalIgnoreCase);
			sfFiles.Clear();
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
			sfCutPaths.RemoveWhere(p => !File.Exists(p) && !Directory.Exists(p));
			foreach (var r in sfFiles) {
				if (keep.Contains(r.Full))
					lvsffiles.SelectedItems.Add(r);
			}
			if (bsfviewthumb?.IsChecked == true) sfloadthumbs();
		}
		catch (Exception ex) {
			setstatus(ex.Message);
		}
		finally {
			sfRefreshing = false;
			if (lbsffempty != null)
				lbsffempty.Visibility = sfFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			sffilebtns();
		}
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
		var phone = sendFile != null && sendFile.PhoneOnline;
		if (bsfcut != null) bsfcut.IsEnabled = on;
		if (bsfcopy != null) bsfcopy.IsEnabled = on;
		if (bsfdel != null) bsfdel.IsEnabled = on;
		if (bsfpush != null) bsfpush.IsEnabled = on && phone;
		if (mnsfcut != null) mnsfcut.IsEnabled = on;
		if (mnsfcopy != null) mnsfcopy.IsEnabled = on;
		if (mnsfdel != null) mnsfdel.IsEnabled = on;
	}

	void sffilepush() {
		var id = sendFile?.OnlineDeviceId;
		if (string.IsNullOrEmpty(id)) {
			setstatus(Loc.T("sf.nophone"));
			return;
		}
		var paths = sffilesel();
		if (paths.Count == 0) return;
		setstatus(Loc.T("sf.sending"));
		var svc = sendFile;
		Task.Run(() => {
			var n = 0;
			string err = null;
			try {
				foreach (var full in paths) {
					var rel = SendFilePaths.RelFrom(full);
					if (string.IsNullOrWhiteSpace(rel)) continue;
					n += svc.PushStoreToPhone(id, rel);
				}
			}
			catch (Exception ex) { err = ex.Message; }
			try {
				Dispatcher.BeginInvoke(new Action(() => {
					if (n > 0) setstatus(Loc.T("sf.to_phone", n.ToString()));
					else setstatus(err ?? Loc.T("sf.nophone"));
					syncsfstatus();
					syncsfjobs();
				}));
			}
			catch { }
		});
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
			if (cut) {
				sfCutPaths.Clear();
				foreach (var p in paths) sfCutPaths.Add(p);
			}
			else sfclearcut();
			sffileapplycutui();
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
						sfclearcut();
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
			foreach (var p in paths) sfCutPaths.Remove(p);
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
		sfDown = e.GetPosition(lvsffiles);
		sfDragReady = false;
		sfCanDrag = false;
		sfMarquee = false;
		sfClickSel = null;
		if (e.ChangedButton != MouseButton.Left) return;
		if (sffilechrome(e.OriginalSource as DependencyObject)) return;
		var item = sffilehit(e.OriginalSource as DependencyObject);
		if (item != null && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
			var idx = sfFiles.IndexOf(item);
			if (idx >= 0) {
				var anchor = sfLastIdx >= 0 ? sfLastIdx : idx;
				var lo = Math.Min(anchor, idx);
				var hi = Math.Max(anchor, idx);
				if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
					lvsffiles.SelectedItems.Clear();
				for (var i = lo; i <= hi; i++) {
					var row = sfFiles[i];
					if (!lvsffiles.SelectedItems.Contains(row))
						lvsffiles.SelectedItems.Add(row);
				}
				e.Handled = true;
				return;
			}
		}
		if (item == null) {
			sfMarquee = true;
			sfMarquee0 = e.GetPosition(csfsel);
			sfMarqueePt = sfMarquee0;
			sfMarqueeKeep = new HashSet<SfFileRow>(
				lvsffiles.SelectedItems.OfType<SfFileRow>());
			lvsffiles.CaptureMouse();
			e.Handled = true;
			return;
		}
		sfDragReady = true;
		try { lvsffiles.Focus(); } catch { }
		if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
			return;
		if (!lvsffiles.SelectedItems.Contains(item)) {
			sfapplysel(new HashSet<SfFileRow> { item });
			sfCanDrag = true;
			e.Handled = true;
			return;
		}
		sfClickSel = item;
		sfCanDrag = true;
		e.Handled = true;
	}

	void onsffilemove(object sender, MouseEventArgs e) {
		if (e.LeftButton != MouseButtonState.Pressed) return;
		if (sfMarquee) {
			sffilemarquee(e.GetPosition(csfsel));
			e.Handled = true;
			return;
		}
		if (!sfCanDrag || !sfDragReady || sfDragging) return;
		var pos = e.GetPosition(lvsffiles);
		if (Math.Abs(pos.X - sfDown.X) < SystemParameters.MinimumHorizontalDragDistance
			&& Math.Abs(pos.Y - sfDown.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;
		var paths = sffilesel();
		if (paths.Count == 0) return;
		sfDragReady = false;
		sfClickSel = null;
		sffiledodrag(paths);
	}

	void sffiledodrag(List<string> paths) {
		if (paths == null || paths.Count == 0 || sfDragging) return;
		var zones = new FrameworkElement[] { this, psftab, psffiles, psffbrowse, psfdropphone };
		var allow = new bool[zones.Length];
		for (var i = 0; i < zones.Length; i++)
			allow[i] = zones[i] != null && zones[i].AllowDrop;
		sfDragging = true;
		try {
			for (var i = 0; i < zones.Length; i++) {
				if (zones[i] != null) zones[i].AllowDrop = false;
			}
			DragDrop.DoDragDrop(lvsffiles, sffiledropdata(paths),
				DragDropEffects.Copy | DragDropEffects.Move);
		}
		catch { }
		finally {
			sfDragging = false;
			sfDragReady = false;
			sfCanDrag = false;
			for (var i = 0; i < zones.Length; i++) {
				if (zones[i] != null) zones[i].AllowDrop = allow[i];
			}
		}
	}

	void onsffileup(object sender, MouseButtonEventArgs e) {
		sfDragReady = false;
		sfCanDrag = false;
		if (sfClickSel != null && !sfMarquee
			&& (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0) {
			var one = new HashSet<SfFileRow> { sfClickSel };
			sfapplysel(one);
		}
		sfClickSel = null;
		if (sfMarquee) {
			var w = Math.Abs(sfMarqueePt.X - sfMarquee0.X);
			var h = Math.Abs(sfMarqueePt.Y - sfMarquee0.Y);
			var tiny = w < 3 && h < 3;
			if (!tiny) sffilemarqueesel();
			else if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
				sfapplysel(new HashSet<SfFileRow>());
			sffileendmarquee();
			e.Handled = true;
			return;
		}
	}

	void sffilemarqueesel() {
		if (lvsffiles == null || csfsel == null) return;
		var host = (UIElement)psffbrowse ?? lvsffiles;
		var x = Math.Min(sfMarquee0.X, sfMarqueePt.X);
		var y = Math.Min(sfMarquee0.Y, sfMarqueePt.Y);
		var w = Math.Abs(sfMarqueePt.X - sfMarquee0.X);
		var h = Math.Abs(sfMarqueePt.Y - sfMarquee0.Y);
		var origin = csfsel.TranslatePoint(new Point(x, y), host);
		var box = new Rect(origin.X, origin.Y, Math.Max(1, w), Math.Max(1, h));
		var keepCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
		var want = new HashSet<SfFileRow>();
		if (keepCtrl && sfMarqueeKeep != null) {
			foreach (var it in sfMarqueeKeep)
				want.Add(it);
		}
		sfwalkrows((lvi, row) => {
			if (lvi.ActualWidth <= 0 || lvi.ActualHeight <= 0) return;
			var live = sflive(row);
			if (live == null) return;
			var p = lvi.TranslatePoint(new Point(0, 0), host);
			var bounds = new Rect(p.X, p.Y, lvi.ActualWidth, lvi.ActualHeight);
			if (!box.IntersectsWith(bounds)) return;
			want.Add(live);
		});
		sfapplysel(want);
	}

	SfFileRow sflive(SfFileRow row) {
		if (row == null) return null;
		if (sfFiles.Contains(row)) return row;
		foreach (var it in sfFiles) {
			if (string.Equals(it.Full, row.Full, StringComparison.OrdinalIgnoreCase))
				return it;
		}
		return null;
	}

	void sfwalkrows(Action<ListViewItem, SfFileRow> fn) {
		if (lvsffiles == null || fn == null) return;
		sfwalkrows(lvsffiles, fn);
	}

	static void sfwalkrows(DependencyObject node, Action<ListViewItem, SfFileRow> fn) {
		var n = VisualTreeHelper.GetChildrenCount(node);
		for (var i = 0; i < n; i++) {
			var child = VisualTreeHelper.GetChild(node, i);
			if (child is ListViewItem lvi) {
				if (lvi.DataContext is SfFileRow row) fn(lvi, row);
				continue;
			}
			sfwalkrows(child, fn);
		}
	}

	void sffilemarquee(Point now) {
		if (csfsel == null || rsfsel == null) return;
		sfMarqueePt = now;
		var x = Math.Min(sfMarquee0.X, now.X);
		var y = Math.Min(sfMarquee0.Y, now.Y);
		var w = Math.Abs(now.X - sfMarquee0.X);
		var h = Math.Abs(now.Y - sfMarquee0.Y);
		Canvas.SetLeft(rsfsel, x);
		Canvas.SetTop(rsfsel, y);
		rsfsel.Width = w;
		rsfsel.Height = h;
		rsfsel.Visibility = Visibility.Visible;
		if (w >= 3 || h >= 3) sffilemarqueesel();
	}

	internal void sfmarqueeuitest(string logPath) {
		var lines = new List<string>();
		void log(string s) => lines.Add(s);
		try {
			if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
			Activate();
			maintabs.SelectedItem = tabsf;
			Width = Math.Max(Width, 1100);
			Height = Math.Max(Height, 760);
			var root = SendFilePaths.Root();
			Directory.CreateDirectory(root);
			for (var i = 0; i < 6; i++) {
				var p = Path.Combine(root, $"marquee-probe-{i}.txt");
				if (!File.Exists(p)) File.WriteAllText(p, "x");
			}
			sffilerefresh();
			UpdateLayout();
			lvsffiles?.UpdateLayout();
			log($"files={sfFiles.Count} thumb={bsfviewthumb?.IsChecked == true}");
			var shown = 0;
			sfwalkrows((lvi, row) => {
				var pc = lvi.TranslatePoint(new Point(0, 0), csfsel);
				var pb = lvi.TranslatePoint(new Point(0, 0), psffbrowse);
				log($"row {row.Name} csf=({pc.X:0.#},{pc.Y:0.#}) browse=({pb.X:0.#},{pb.Y:0.#}) {lvi.ActualWidth:0.#}x{lvi.ActualHeight:0.#} tpl={(lvi.Template == null ? "null" : "ok")} sel={lvi.IsSelected}");
				shown++;
			});
			log($"shown={shown}");
			ListViewItem a = null, b = null;
			var idx = 0;
			sfwalkrows((lvi, _) => {
				if (idx == 0) a = lvi;
				if (idx == 2) b = lvi;
				idx++;
			});
			if (a == null || b == null) {
				log("FAIL no rows");
				return;
			}
			log($"mode={lvsffiles.SelectionMode}");
			sfMarquee = true;
			sfMarquee0 = a.TranslatePoint(new Point(8, 2), csfsel);
			sfMarqueePt = b.TranslatePoint(new Point(80, Math.Max(1, b.ActualHeight - 2)), csfsel);
			sfMarqueeKeep = new HashSet<SfFileRow>();
			sffilemarqueesel();
			var thumbN = lvsffiles.SelectedItems.Count;
			log($"probe-sel={thumbN}");
			foreach (SfFileRow it in lvsffiles.SelectedItems) log($"probe-item {it.Name}");
			sfwalkrows((lvi, row) => { if (lvi.IsSelected) log($"probe-hit {row.Name}"); });
			sfMarquee = false;
			lvsffiles.SelectedItems.Clear();
			sfsetview(false);
			UpdateLayout();
			lvsffiles.UpdateLayout();
			a = null; b = null; idx = 0;
			sfwalkrows((lvi, _) => {
				if (idx == 0) a = lvi;
				if (idx == 2) b = lvi;
				idx++;
			});
			sfMarquee = true;
			sfMarquee0 = a.TranslatePoint(new Point(8, 2), csfsel);
			sfMarqueePt = b.TranslatePoint(new Point(120, Math.Max(1, b.ActualHeight - 2)), csfsel);
			sfMarqueeKeep = new HashSet<SfFileRow>();
			sffilemarqueesel();
			var listN = lvsffiles.SelectedItems.Count;
			log($"list-sel={listN}");
			foreach (SfFileRow it in lvsffiles.SelectedItems) log($"list-item {it.Name}");
			sfwalkrows((lvi, row) => { if (lvi.IsSelected) log($"list-hit {row.Name}"); });
			sfMarquee = false;
			lvsffiles.SelectedItems.Clear();
			sfsetview(true);
			UpdateLayout();
			lvsffiles.UpdateLayout();
			new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
			Activate();
			a = null;
			sfwalkrows((lvi, _) => { if (a == null) a = lvi; });
			var downLocal = new Point(24, Math.Max(8, lvsffiles.ActualHeight - 12));
			log($"bottomItem={sffilehit(lvsffiles.InputHitTest(downLocal) as DependencyObject) != null} lv={lvsffiles.ActualWidth:0}x{lvsffiles.ActualHeight:0}");
			var down = sfclientpx(lvsffiles.TranslatePoint(downLocal, this));
			var up = sfclientpx(a.TranslatePoint(new Point(40, 4), this));
			log($"drag ({down.X:0},{down.Y:0}) -> ({up.X:0},{up.Y:0})");
			var step = 0;
			var timer = new System.Windows.Threading.DispatcherTimer {
				Interval = TimeSpan.FromMilliseconds(40),
			};
			timer.Tick += (_, _) => {
				if (step == 0) {
					sfsetcursor((int)down.X, (int)down.Y);
					sfmousebtn(true);
				}
				else if (step < 12) {
					var t = step / 11.0;
					var x = down.X + (up.X - down.X) * t;
					var y = down.Y + (up.Y - down.Y) * t;
					sfsetcursor((int)x, (int)y);
				}
				else if (step == 12) {
					sfsetcursor((int)up.X, (int)up.Y);
					sfmousebtn(false);
				}
				else {
					timer.Stop();
					var mouseN = lvsffiles.SelectedItems.Count;
					log($"mouse-sel={mouseN} marquee={sfMarquee}");
					sfwalkrows((lvi, row) => { if (lvi.IsSelected) log($"mouse-hit {row.Name}"); });
					for (var i = 0; i < 6; i++) {
						try { File.Delete(Path.Combine(root, $"marquee-probe-{i}.txt")); } catch { }
					}
					try { File.WriteAllLines(logPath, lines); } catch { }
					var ok = thumbN >= 3 && listN >= 3 && mouseN >= 2;
					System.Windows.Application.Current.Shutdown(ok ? 0 : 2);
				}
				step++;
			};
			timer.Start();
			return;
		}
		catch (Exception ex) {
			log(ex.ToString());
		}
		try { File.WriteAllLines(logPath, lines); } catch { }
		System.Windows.Application.Current.Shutdown(3);
	}

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	struct SfWinPoint { public int X; public int Y; }

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	static extern bool ClientToScreen(IntPtr hwnd, ref SfWinPoint pt);

	Point sfclientpx(Point inWindow) {
		var src = PresentationSource.FromVisual(this);
		var d = src.CompositionTarget.TransformToDevice.Transform(inWindow);
		var corner = new SfWinPoint();
		ClientToScreen(new System.Windows.Interop.WindowInteropHelper(this).Handle, ref corner);
		return new Point(corner.X + d.X, corner.Y + d.Y);
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	static extern bool SetCursorPos(int x, int y);

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

	static void sfsetcursor(int x, int y) => SetCursorPos(x, y);

	static void sfmousebtn(bool down) => mouse_event(down ? 0x0002u : 0x0004u, 0, 0, 0, UIntPtr.Zero);

	void sffileendmarquee() {
		if (!sfMarquee) return;
		sfMarquee = false;
		try { lvsffiles.ReleaseMouseCapture(); } catch { }
		if (rsfsel != null) rsfsel.Visibility = Visibility.Collapsed;
		sfMarqueeKeep = null;
		sffileapplycutui();
	}

	SfFileRow sffilehit(DependencyObject src) {
		if (src == null || lvsffiles == null) return null;
		if (ItemsControl.ContainerFromElement(lvsffiles, src) is ListViewItem lvi)
			return lvi.DataContext as SfFileRow;
		while (src != null) {
			if (src is ListViewItem row)
				return row.DataContext as SfFileRow;
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
