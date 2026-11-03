using System.ComponentModel;

namespace ScreenKit;

/// <summary>
/// 快速复制列表里的一行：本机路径 + 派生出来的显示名和图标。
/// 原来列表直接绑字符串路径，加图标需要多两个属性，所以包一层。
/// 图标复用 <see cref="FtpIcon"/>，按扩展名和是否目录分，跟 FTP 文件管理同一套。
/// </summary>
sealed class FastCopyRow : INotifyPropertyChanged {
	string path = "";
	bool isDir;
	string label = "";
	string leaf = "";

	public string Path {
		get => path;
		set {
			if (path == value) return;
			path = value ?? "";
			refresh();
		}
	}

	/// <summary>列表里显示的文字：带路径的照样显示完整路径，只有一个名字时就是名字。</summary>
	public string Label => label;

	/// <summary>去掉目录部分的名字，判断类型用。</summary>
	public string Leaf => leaf;

	public bool IsDir => isDir;

	public string Icon => FtpIcon.Glyph(leaf, isDir);

	public string IconBrush => FtpIcon.Color(leaf, isDir);

	/// <summary>文件名后面的补充说明，只有目录才写「文件夹」，文件留空免得啰嗦。</summary>
	public string KindText => isDir ? Loc.T("fc.iskind.dir") : "";

	public event PropertyChangedEventHandler PropertyChanged;

	void refresh() {
		leaf = leafof(path);
		try { isDir = Directory.Exists(path); }
		catch { isDir = false; }
		label = path;
		notify(nameof(Leaf));
		notify(nameof(IsDir));
		notify(nameof(Icon));
		notify(nameof(IconBrush));
		notify(nameof(Label));
		notify(nameof(KindText));
	}

	static string leafof(string p) {
		if (string.IsNullOrEmpty(p)) return "";
		try {
			var t = p.TrimEnd('\\', '/');
			var i = t.LastIndexOfAny(new[] { '\\', '/' });
			if (i >= 0 && i < t.Length - 1) return t.Substring(i + 1);
			// 根目录（C:\）取不到名字，用整串
			return t;
		}
		catch { return p; }
	}

	void notify(string prop) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

	/// <summary>从路径造一行。</summary>
	public static FastCopyRow From(string path) => new() { Path = path };
}
