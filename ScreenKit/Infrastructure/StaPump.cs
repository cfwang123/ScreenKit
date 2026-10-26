using System.Threading;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>
/// 常驻 STA 线程 + Dispatcher。WinRT 的日历换算、日文读音、系统语音合成都要求 STA，
/// 以前每个请求新建一个线程（并发时线程数随请求涨），现在统一排到这一个线程上执行。
/// </summary>
static class StaPump {
	static readonly object gate = new();
	static volatile Dispatcher pump;

	/// <summary>排到常驻 STA 线程上执行；已经在这个线程上则直接执行，避免自己等自己。</summary>
	public static T Run<T>(Func<T> fn) {
		if (fn == null) return default;
		var d = get();
		return d.CheckAccess() ? fn() : d.Invoke(fn);
	}

	/// <summary>
	/// 调用线程本身就是 STA（界面线程）时直接执行，否则排到常驻线程。
	/// 日历、读音用这个：界面线程不被后台语音合成堵住，行为和以前一致。
	/// </summary>
	public static T RunIfNotSta<T>(Func<T> fn) {
		if (fn == null) return default;
		if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return fn();
		return Run(fn);
	}

	static Dispatcher get() {
		var d = pump;
		if (d != null) return d;
		lock (gate) {
			if (pump != null) return pump;
			using var ready = new ManualResetEventSlim(false);
			var t = new Thread(() => {
				var cur = Dispatcher.CurrentDispatcher;
				pump = cur;
				ready.Set();
				Dispatcher.Run();
			});
			t.IsBackground = true;
			t.SetApartmentState(ApartmentState.STA);
			t.Name = "sta-pump";
			t.Start();
			ready.Wait(5000);
			return pump ?? throw new InvalidOperationException("STA 线程启动失败");
		}
	}
}
