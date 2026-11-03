using System;
using System.Windows;
using System.Windows.Threading;

namespace ScreenKit.DocView;

/// <summary>
/// 在 UI 线程长任务中穿插处理输入/绘制消息，避免窗口无法拖动、界面假死。
/// 泵太密会显著拖慢加载（曾把 2s 拖到 4s），故用时间节流。
/// </summary>
static class UiPump {
	/// <summary>最短泵间隔（ms）。</summary>
	const int MIN_INTERVAL_MS = 48;

	static int nest;
	static int lastTick;
	static readonly DispatcherOperationCallback ExitFrame = state => {
		((DispatcherFrame)state).Continue = false;
		return null;
	};

	/// <summary>处理已排队的输入与绘制消息（可重入安全）。</summary>
	public static void Once() {
		var app = Application.Current;
		if (app == null) return;
		var d = app.Dispatcher;
		if (!d.CheckAccess()) return;
		var now = Environment.TickCount;
		if (nest > 0) return;
		if (lastTick != 0 && now - lastTick < MIN_INTERVAL_MS) return;
		lastTick = now;
		try {
			nest++;
			var frame = new DispatcherFrame();
			d.BeginInvoke(DispatcherPriority.ApplicationIdle, ExitFrame, frame);
			Dispatcher.PushFrame(frame);
		} catch {
			// ignore
		} finally {
			nest--;
		}
	}

	/// <summary>每处理 count 次尝试泵一次（仍受 MIN_INTERVAL_MS 限制）。</summary>
	public static void Every(ref int counter, int every) {
		if (every < 1) every = 1;
		counter++;
		if (counter % every == 0)
			Once();
	}
}
