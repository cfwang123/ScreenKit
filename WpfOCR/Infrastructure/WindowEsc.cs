using System.Windows;
using System.Windows.Input;

namespace WpfOCR;

/// <summary>弹窗 Esc 关闭/取消统一挂接（PreviewKeyDown，覆盖焦点在 TextBox 等情况）。</summary>
static class WindowEsc {
	/// <param name="onEsc">自定义处理；null 则 <see cref="Window.Close"/>。</param>
	public static void Attach(Window w, Action onEsc = null) {
		if (w == null) return;
		w.PreviewKeyDown += (_, e) => {
			if (e.Key != Key.Escape) return;
			e.Handled = true;
			try {
				if (onEsc != null) onEsc();
				else w.Close();
			}
			catch { }
		};
	}
}
