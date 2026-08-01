using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfOCR;

/// <summary>
/// 录屏 HUD：红色外框 + 底部浮动条（区域尺寸、参数摘要、开始/暂停/停止）。
/// 声音在「录屏选项」中设置。窗口 WDA_EXCLUDEFROMCAPTURE 避免录进自身。
/// 截图识别/标注进行时通过 <see cref="SuspendForCapture"/> 隐藏，避免挡遮罩与叠进画面。
/// </summary>
public partial class RecordHud : Window {
	const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HwndTopmost = new(-1);

	[DllImport("user32.dll")]
	static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	readonly System.Drawing.Rectangle region;
	readonly RecordOptions recOpt;
	ScreenRecorder rec;
	DispatcherTimer timer;
	bool started;
	bool stopping;
	bool suspendedForCapture;
	bool pausedBeforeSuspend;
	string tmpPath;

	public bool Completed { get; private set; }
	public bool Saved { get; private set; }
	public string SavedPath { get; private set; }
	/// <summary>是否已点开始并在录制（含暂停）。</summary>
	public bool IsRecording => started && rec != null && !stopping;

	public event Action Finished;

	public RecordHud(System.Drawing.Rectangle region, RecordOptions options = null) {
		this.region = region;
		recOpt = (options ?? new RecordOptions()).Clone();
		recOpt.Clamp();
		InitializeComponent();

		// 必须先设 DIP 尺寸，再 SetWindowPos 物理像素；否则分层窗命中测试与绘制错位
		var (vlDip, vtDip, vwDip, vhDip) = ScreenDpi.VirtualScreenDip();
		Left = vlDip;
		Top = vtDip;
		Width = vwDip;
		Height = vhDip;

		var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
		SourceInitialized += (_, _) => {
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd != IntPtr.Zero) {
				SetWindowPos(hwnd, HwndTopmost, vl, vt, vw, vh, SWP_SHOWWINDOW);
				try { SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); } catch { }
			}
		};

		Loaded += (_, _) => {
			proot.Width = Math.Max(1, ActualWidth);
			proot.Height = Math.Max(1, ActualHeight);
			fillsummary();
			layoutchrome();
			Dispatcher.BeginInvoke(new Action(() => {
				proot.Width = Math.Max(1, ActualWidth);
				proot.Height = Math.Max(1, ActualHeight);
				layoutchrome();
			}), DispatcherPriority.Loaded);
			timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
			timer.Tick += (_, _) => tickui();
			timer.Start();
		};

		bstart.Click += (_, _) => onstart();
		bpause.Click += (_, _) => onpause();
		bstop.Click += (_, _) => onstop();
		// 未开录前 Esc 取消；录制中不关（需点停止）
		WindowEsc.Attach(this, () => {
			if (!started && !stopping) closeout(false);
		});
	}

	/// <summary>
	/// 截图识别/标注前：隐藏 HUD，并暂停录制（避免遮罩进录像、挡操作）。
	/// </summary>
	public void SuspendForCapture() {
		if (suspendedForCapture || stopping) return;
		suspendedForCapture = true;
		pausedBeforeSuspend = rec != null && started && rec.IsPaused;
		RecordLog.Step("hud_suspend_capture",
			$"started={started} wasPaused={pausedBeforeSuspend}");
		try {
			if (rec != null && started && !rec.IsPaused)
				rec.Pause();
		}
		catch (Exception ex) { RecordLog.Ex("hud_suspend.Pause", ex); }
		try { Hide(); } catch { }
	}

	/// <summary>截图结束后恢复 HUD 与暂停状态。</summary>
	public void ResumeAfterCapture() {
		if (!suspendedForCapture) return;
		suspendedForCapture = false;
		RecordLog.Step("hud_resume_capture", $"restorePause={pausedBeforeSuspend}");
		try {
			Show();
			retopmost();
		}
		catch (Exception ex) { RecordLog.Ex("hud_resume.Show", ex); }
		try {
			// 仅当挂起前未暂停时才自动继续
			if (rec != null && started && !pausedBeforeSuspend && rec.IsPaused) {
				rec.Resume();
				bpause.Content = "暂停";
				lbstate.Text = "录制中";
				lbstate.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
				edot.Fill = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
			}
			else if (rec != null && started && rec.IsPaused) {
				bpause.Content = "继续";
				lbstate.Text = "已暂停";
				lbstate.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
				edot.Fill = new SolidColorBrush(Color.FromRgb(0x5D, 0x40, 0x37));
			}
		}
		catch (Exception ex) { RecordLog.Ex("hud_resume.Resume", ex); }
	}

	void retopmost() {
		try {
			var (vl, vt, vw, vh) = ScreenDpi.VirtualScreenPixels();
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd != IntPtr.Zero) {
				SetWindowPos(hwnd, HwndTopmost, vl, vt, vw, vh, SWP_SHOWWINDOW);
				try { SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); } catch { }
			}
		}
		catch { }
	}

	void fillsummary() {
		var rw = region.Width % 2 == 0 ? region.Width : region.Width - 1;
		var rh = region.Height % 2 == 0 ? region.Height : region.Height - 1;
		lbregion.Text = $"{rw}×{rh}";
		recOpt.FitSize(rw, rh, out var ow, out var oh);
		var sum = recOpt.SummaryText(rw, rh);
		if (recOpt.MaxSizeEnabled && (ow != rw || oh != rh))
			lbsummary.Text = $"{sum} · out {ow}×{oh}";
		else
			lbsummary.Text = sum;
		var tip = $"选区 {rw}×{rh}\n{sum}";
		ToolTip = tip;
		bbar.ToolTip = tip;
	}

	void layoutchrome() {
		var (vl, vt, _, _) = ScreenDpi.VirtualScreenPixels();
		ScreenDpi.VirtualScreenScale(out var sx, out var sy);
		if (sx < 0.25) sx = 1;
		if (sy < 0.25) sy = 1;
		var bx = (region.Left - vl) / sx;
		var by = (region.Top - vt) / sy;
		var bw = region.Width / sx;
		var bh = region.Height / sy;

		var stroke = rborder.StrokeThickness > 0 ? rborder.StrokeThickness : 3;
		var gap = 2.0;
		var outM = stroke + gap;
		Canvas.SetLeft(rborder, bx - outM);
		Canvas.SetTop(rborder, by - outM);
		rborder.Width = bw + outM * 2;
		rborder.Height = bh + outM * 2;
		Canvas.SetLeft(rdot, bx - outM - 4);
		Canvas.SetTop(rdot, by - outM - 4);

		bbar.UpdateLayout();
		var barW = bbar.ActualWidth > 1 ? bbar.ActualWidth : 420;
		var barH = bbar.ActualHeight > 1 ? bbar.ActualHeight : 28;
		var screenW = Math.Max(1.0, ActualWidth > 1 ? ActualWidth : Width);
		var screenH = Math.Max(1.0, ActualHeight > 1 ? ActualHeight : Height);
		var barX = bx + (bw - barW) / 2;
		var barY = by + bh + outM + 6;
		if (barY + barH > screenH - 4)
			barY = by - barH - outM - 6;
		if (barY < 4) barY = screenH - barH - 8;
		if (barX < 4) barX = 4;
		if (barX + barW > screenW - 4) barX = Math.Max(4, screenW - barW - 4);
		Canvas.SetLeft(bbar, barX);
		Canvas.SetTop(bbar, barY);
		bbar.IsHitTestVisible = true;

		try {
			var hwnd = new WindowInteropHelper(this).Handle;
			if (hwnd != IntPtr.Zero)
				SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
		}
		catch { }
	}

	void onstart() {
		if (started || stopping) return;
		try {
			rec = new ScreenRecorder(region, recOpt.AudioMode, recOpt);
			rec.Progress = msg => {
				try {
					Dispatcher.BeginInvoke(new Action(() => {
						if (!stopping) return;
						lbstate.Text = string.IsNullOrEmpty(msg) ? "导出中" : msg;
					}));
				}
				catch { }
			};
			rec.Start();
			tmpPath = rec.TempPath;
			started = true;
			bstart.IsEnabled = false;
			bpause.IsEnabled = true;
			lbstate.Text = "录制中";
			lbstate.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
			if (!string.IsNullOrEmpty(rec.Backend)) {
				lbsummary.Text = rec.Backend;
				bbar.ToolTip = rec.Backend;
			}
			RecordLog.Step("hud_start", "backend=" + (rec.Backend ?? "") + " log=" + (RecordLog.LogPath ?? ""));
		}
		catch (Exception ex) {
			RecordLog.Ex("hud_start", ex);
			MessageBox.Show(this, ex.Message, "录屏", MessageBoxButton.OK, MessageBoxImage.Warning);
			try { rec?.Dispose(); } catch { }
			rec = null;
		}
	}

	void onpause() {
		if (rec == null || !started || stopping || suspendedForCapture) return;
		if (rec.IsPaused) {
			rec.Resume();
			bpause.Content = "暂停";
			lbstate.Text = "录制中";
			lbstate.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
			edot.Fill = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
		}
		else {
			rec.Pause();
			bpause.Content = "继续";
			lbstate.Text = "已暂停";
			lbstate.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
			edot.Fill = new SolidColorBrush(Color.FromRgb(0x5D, 0x40, 0x37));
		}
	}

	void onstop() {
		if (stopping) return;
		if (!started) {
			closeout(false);
			return;
		}
		stopping = true;
		// 若正在为截图挂起，先恢复再停
		if (suspendedForCapture) {
			suspendedForCapture = false;
			try { Show(); } catch { }
		}
		bstart.IsEnabled = false;
		bpause.IsEnabled = false;
		bstop.IsEnabled = false;
		lbstate.Text = "正在停止…";
		lbstate.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
		edot.Fill = new SolidColorBrush(Color.FromRgb(0x5D, 0x40, 0x37));
		edot.Opacity = 1;
		var recorder = rec;
		// 停采集 + 写视频索引后立刻弹保存；合成在后台并行
		Task.Run(() => {
			try { recorder?.Stop(); }
			catch (Exception ex) { RecordLog.Ex("hud_stop", ex); }
			Dispatcher.BeginInvoke(new Action(() => afterstop()));
		});
	}

	void afterstop() {
		try {
			timer?.Stop();
			tmpPath = rec?.TempPath ?? tmpPath;
			tickui();
			var size = rec?.FileBytes ?? 0;
			var elapsed = rec?.Elapsed ?? TimeSpan.Zero;
			RecordLog.Step("hud_ask_save",
				$"finalizeDone={rec?.IsFinalizeDone} size={size} path={tmpPath} " +
				$"elapsed={elapsed}");
			// 不等合成：立刻选输出路径
			var sfd = new Microsoft.Win32.SaveFileDialog {
				Title = "保存录屏",
				Filter = "MP4 视频|*.mp4",
				FileName = $"rec_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
				DefaultExt = ".mp4",
				AddExtension = true,
				OverwritePrompt = true,
			};
			if (sfd.ShowDialog(this) != true) {
				RecordLog.Step("hud_save_cancel", "user cancelled save dialog");
				lbstate.Text = "已取消，清理中…";
				var drop = rec;
				Task.Run(() => {
					try { drop?.DiscardTemps(); } catch (Exception ex) { RecordLog.Ex("hud_discard", ex); }
					Dispatcher.BeginInvoke(new Action(() => closeout(true)));
				});
				return;
			}
			var dest = sfd.FileName;
			lbstate.Text = recOpt.AudioEnabled && rec != null && !rec.IsFinalizeDone
				? "正在合成音轨…"
				: "正在保存…";
			var recorder = rec;
			Task.Run(() => {
				string err = null;
				string finalSrc = null;
				try {
					// 选完路径后再等合成完成并复制
					if (recorder != null) {
						if (!recorder.IsFinalizeDone) {
							try { recorder.Progress?.Invoke("正在合成音轨…"); } catch { }
						}
						recorder.WaitFinalize();
						finalSrc = recorder.TempPath;
					}
					else finalSrc = tmpPath;

					if (string.IsNullOrEmpty(finalSrc) || !File.Exists(finalSrc)) {
						err = "临时文件不存在。";
						return;
					}
					try { recorder?.Progress?.Invoke("正在保存…"); } catch { }
					RecordLog.Step("hud_copy",
						$"src={RecordLog.FileInfo(finalSrc)} dest={dest} " +
						$"HasAudio={recorder?.HasAudio} err={recorder?.AudioError ?? "-"}");
					if (string.Equals(Path.GetFullPath(finalSrc), Path.GetFullPath(dest),
						StringComparison.OrdinalIgnoreCase)) {
						// 目标就是临时路径（极少见）
					}
					else {
						var dir = Path.GetDirectoryName(dest);
						if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
						File.Copy(finalSrc, dest, overwrite: true);
						try { File.Delete(finalSrc); } catch { }
						// 清理可能残留的纯视频/ wav
						try { recorder?.DiscardTemps(); } catch { }
					}
					Saved = true;
					SavedPath = dest;
				}
				catch (Exception ex) {
					err = ex.Message;
					RecordLog.Ex("hud_copy", ex);
				}
				Dispatcher.BeginInvoke(new Action(() => {
					try {
						if (!string.IsNullOrEmpty(err)) {
							MessageBox.Show(this, err, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
						}
						else if (Saved) {
							var audioNote = "";
							if (recOpt.AudioEnabled) {
								if (!string.IsNullOrEmpty(recorder?.AudioError))
									audioNote = "\n⚠ 声音: " + recorder.AudioError;
								else if (recorder != null && !recorder.HasAudio)
									audioNote = "\n⚠ 声音可能未写入（请确认有系统声/麦克风权限）";
							}
							if (!string.IsNullOrEmpty(audioNote))
								MessageBox.Show(this,
									$"已保存：\n{dest}{audioNote}",
									"录屏", MessageBoxButton.OK, MessageBoxImage.Warning);
							revealinfile(dest);
						}
					}
					catch (Exception ex) {
						RecordLog.Ex("hud_aftersave_ui", ex);
					}
					finally {
						closeout(true);
					}
				}));
			});
		}
		catch (Exception ex) {
			RecordLog.Ex("hud_afterstop", ex);
			MessageBox.Show(this, ex.Message, "录屏", MessageBoxButton.OK, MessageBoxImage.Warning);
			closeout(true);
		}
	}

	static void revealinfile(string filePath) {
		try {
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
			Process.Start(new ProcessStartInfo {
				FileName = "explorer.exe",
				Arguments = $"/select,\"{filePath}\"",
				UseShellExecute = true,
			});
		}
		catch { }
	}

	void tickui() {
		if (rec == null || suspendedForCapture) return;
		lbtime.Text = fmt(rec.Elapsed);
		lbsize.Text = fmtbytes(rec.FileBytes);
		if (started && !stopping && !rec.IsPaused) {
			var on = (Environment.TickCount / 500) % 2 == 0;
			edot.Opacity = on ? 1 : 0.35;
		}
		else edot.Opacity = 1;
	}

	void closeout(bool completed) {
		Completed = completed;
		stopping = false;
		suspendedForCapture = false;
		try { timer?.Stop(); } catch { }
		try { rec?.Dispose(); } catch { }
		rec = null;
		try { Close(); } catch { }
		try { Finished?.Invoke(); } catch { }
	}

	protected override void OnClosed(EventArgs e) {
		try { timer?.Stop(); } catch { }
		base.OnClosed(e);
	}

	static string fmt(TimeSpan t) {
		var h = (int)t.TotalHours;
		if (h > 0)
			return $"{h:00}:{t.Minutes:00}:{t.Seconds:00}";
		return $"00:{t.Minutes:00}:{t.Seconds:00}";
	}

	static string fmtbytes(long n) {
		if (n < 1024) return $"{n} B";
		if (n < 1024 * 1024) return $"{n / 1024.0:0.#} KB";
		return $"{n / (1024.0 * 1024):0.##} MB";
	}
}
