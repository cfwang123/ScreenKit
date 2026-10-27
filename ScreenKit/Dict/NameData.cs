using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenKit;

/// <summary>
/// 日本人名数据（web/jname-s.txt、web/jname-g.txt）。不进安装包，第一次用到时从固定 Release
/// dict-db 下载 jname.7z 再解压到 web 目录；之后再打开工具直接用本机文件。
/// </summary>
static class NameData {
	/// <summary>固定标签 dict-db，和词典、输入法这些大文件放一起。</summary>
	const string URL = "https://github.com/cfwang123/ScreenKit/releases/download/dict-db/jname.7z";
	const string ARCHIVE = "jname.7z";
	static readonly string[] NAMES = { "jname-s.txt", "jname-g.txt" };
	static readonly object gate = new();
	static Task task;

	static string WebDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");

	public static bool Ready {
		get {
			foreach (var n in NAMES)
				if (!File.Exists(Path.Combine(WebDir, n))) return false;
			return true;
		}
	}

	/// <summary>本进程内只跑一次；失败后下次调用会重新尝试。</summary>
	public static Task EnsureAsync() {
		lock (gate) {
			if (Ready) return Task.CompletedTask;
			if (task == null || task.IsFaulted || task.IsCanceled) task = runasync();
			return task;
		}
	}

	/// <summary>阻塞等待数据就绪，给 HTTP 静态文件请求用。返回失败原因。</summary>
	public static bool Wait(int seconds, out string error) {
		error = null;
		if (Ready) return true;
		try {
			if (!EnsureAsync().Wait(TimeSpan.FromSeconds(seconds)))
				error = "下载超时";
		}
		catch (Exception ex) {
			error = (ex.InnerException ?? ex).Message;
		}
		if (Ready) return true;
		if (string.IsNullOrEmpty(error)) error = "数据未就绪";
		return false;
	}

	static async Task runasync() {
		var dir = WebDir;
		Directory.CreateDirectory(dir);
		var arc = Path.Combine(dir, ARCHIVE);
		var log = new Progress<string>(_ => { });
		var prog = new Progress<InstallProgress>(_ => { });
		try {
			try { if (File.Exists(arc)) File.Delete(arc); } catch { }
			await FeatureInstaller.DownloadUrlAsync(new[] { URL }, arc, log, prog, CancellationToken.None)
				.ConfigureAwait(false);
			await FeatureInstaller.EnsureExtractAsync(arc, log, prog, CancellationToken.None).ConfigureAwait(false);
			FeatureInstaller.ExtractArchive(arc, dir, log);
			if (!Ready) throw new InvalidOperationException("解压后缺少姓名数据");
		}
		finally {
			try { if (File.Exists(arc)) File.Delete(arc); } catch { }
		}
	}
}
