using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace WpfOCR;

/// <summary>批量字幕队列项。</summary>
sealed class AsrSrtQueueItem : INotifyPropertyChanged {
	string status = "等待";
	string detail = "";
	double filePct;

	public string Path { get; }
	public string FileName => System.IO.Path.GetFileName(Path) ?? Path;

	public string Status {
		get => status;
		set {
			if (status == value) return;
			status = value ?? "";
			OnPropertyChanged();
			OnPropertyChanged(nameof(StatusLine));
		}
	}

	public string Detail {
		get => detail;
		set {
			if (detail == value) return;
			detail = value ?? "";
			OnPropertyChanged();
			OnPropertyChanged(nameof(StatusLine));
		}
	}

	/// <summary>当前文件内进度 0–100。</summary>
	public double FilePct {
		get => filePct;
		set {
			if (Math.Abs(filePct - value) < 0.01) return;
			filePct = value;
			OnPropertyChanged();
		}
	}

	public string StatusLine {
		get {
			if (string.IsNullOrEmpty(detail)) return status;
			return status + " · " + detail;
		}
	}

	public event PropertyChangedEventHandler PropertyChanged;

	public AsrSrtQueueItem(string path) {
		Path = path ?? throw new ArgumentNullException(nameof(path));
	}

	void OnPropertyChanged([CallerMemberName] string name = null) {
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
