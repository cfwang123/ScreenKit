using System.Collections.ObjectModel;

namespace ScreenKit;

/// <summary>
/// 传输队列。每个任务独立会话（FTP 和 SFTP 的连接都不是线程安全的），
/// 最多 MaxParallel 个同时跑，其余排队。进度由后台线程写字段，界面定时器来拉。
/// </summary>
sealed class FtpXfer {
	/// <summary>界面绑定的任务列表；只在 UI 线程增删。</summary>
	public ObservableCollection<FtpJob> Jobs { get; } = new();

	/// <summary>同时传输数，1～4。</summary>
	public int MaxParallel {
		get => maxParallel;
		set => maxParallel = value < 1 ? 1 : value > 4 ? 4 : value;
	}
	int maxParallel = 2;

	int running;
	readonly object gate = new();

	/// <summary>任务结束（完成/失败/取消）时通知界面刷新目录。</summary>
	public event Action<FtpJob> Finished;

	/// <summary>入队一条上传。source 是本机文件，remote 是远端目标文件完整路径。</summary>
	public FtpJob EnqueueUpload(FtpSite site, string source, string remote, string password) {
		var job = new FtpJob {
			Dir = FtpDir.Upload,
			Local = source,
			Remote = remote,
			DisplayName = Path.GetFileName(source),
			Site = site,
			Total = safeSize(source)
		};
		add(job, password);
		return job;
	}

	/// <summary>入队一条下载。</summary>
	public FtpJob EnqueueDownload(FtpSite site, string remote, string local, long remoteSize, string password) {
		var job = new FtpJob {
			Dir = FtpDir.Download,
			Local = local,
			Remote = remote,
			DisplayName = Path.GetFileName(local),
			Site = site,
			Total = remoteSize > 0 ? remoteSize : 0
		};
		add(job, password);
		return job;
	}

	static long safeSize(string path) {
		try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
		catch { return 0; }
	}

	readonly Dictionary<int, string> passwords = new();

	void add(FtpJob job, string password) {
		lock (gate) passwords[job.Id] = password ?? "";
		Jobs.Add(job);
		pump();
	}

	/// <summary>按空位启动排队中的任务。</summary>
	void pump() {
		lock (gate) {
			if (running >= maxParallel) return;
			foreach (var job in Jobs) {
				if (running >= maxParallel) break;
				if (job.State != FtpJobState.Waiting) continue;
				running++;
				job.State = FtpJobState.Running;
				var pass = passwords.TryGetValue(job.Id, out var p) ? p : "";
				Task.Run(() => run(job, pass));
			}
		}
	}

	public void Pause(FtpJob job) {
		if (job == null) return;
		job.PauseFlag = true;
		// 排队中的直接改状态，不启动
		if (job.State == FtpJobState.Waiting) job.State = FtpJobState.Paused;
	}

	/// <summary>从暂停/失败处继续。已传字节保留，从断点续传。</summary>
	public void Resume(FtpJob job) {
		if (job == null) return;
		if (job.State is not (FtpJobState.Paused or FtpJobState.Failed or FtpJobState.Canceled)) return;
		job.ResetForRetry();
		pump();
	}

	public void Cancel(FtpJob job) {
		if (job == null) return;
		job.CancelFlag = true;
		job.PauseFlag = false;
		if (job.State == FtpJobState.Waiting) job.State = FtpJobState.Canceled;
	}

	/// <summary>清掉已结束的任务，运行中的保留。</summary>
	public void ClearFinished() {
		for (int i = Jobs.Count - 1; i >= 0; i--) {
			var s = Jobs[i].State;
			if (s is FtpJobState.Done or FtpJobState.Failed or FtpJobState.Canceled) Jobs.RemoveAt(i);
		}
	}

	public int ActiveCount {
		get {
			int n = 0;
			foreach (var j in Jobs)
				if (j.State is FtpJobState.Running or FtpJobState.Waiting) n++;
			return n;
		}
	}

	// ==================== 执行 ====================
	#region
	void run(FtpJob job, string password) {
		var backend = FtpBackendFactory.Create(job.Site);
		try {
			backend.Connect(job.Site, password);
			if (job.Dir == FtpDir.Upload) doUpload(job, backend);
			else doDownload(job, backend);
			if (job.CancelFlag) job.State = FtpJobState.Canceled;
			else if (job.PauseFlag) job.State = FtpJobState.Paused;
			else job.State = FtpJobState.Done;
		}
		catch (Exception ex) {
			job.Error = ex.Message;
			job.State = job.CancelFlag ? FtpJobState.Canceled
				: job.PauseFlag ? FtpJobState.Paused
				: FtpJobState.Failed;
		}
		finally {
			try { backend.Dispose(); } catch { }
			job.Speed = 0;
			lock (gate) {
				running--;
				passwords.Remove(job.Id);
			}
			try { Finished?.Invoke(job); } catch { }
			pump();
		}
	}

	void doUpload(FtpJob job, IFtpBackend backend) {
		using var src = new FileStream(job.Local, FileMode.Open, FileAccess.Read, FileShare.Read, BUFSIZE);
		if (job.Total <= 0) job.Total = src.Length;
		// 断点续传：只有远端长度正好等于已传字节数才能接着传，否则从头来
		long offset = backend.AlignResume(job.Remote, job.Done);
		if (offset <= 0) {
			offset = 0;
			job.Done = 0;
		}
		if (offset > src.Length) {
			offset = 0;
			job.Done = 0;
		}
		src.Seek(offset, SeekOrigin.Begin);
		long remain = src.Length - offset;
		using var dst = backend.OpenWrite(job.Remote, offset);
		copy(src, dst, job, remain, (n) => job.Done += n);
	}

	void doDownload(FtpJob job, IFtpBackend backend) {
		// 先探测远端大小，用来算进度
		long remoteSize = backend.Size(job.Remote);
		if (remoteSize > 0) job.Total = remoteSize;
		long offset = job.Done;
		if (remoteSize > 0 && offset > remoteSize) offset = 0;
		var dir = Path.GetDirectoryName(job.Local);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		using var dst = new FileStream(job.Local, offset > 0 ? FileMode.OpenOrCreate : FileMode.Create,
			FileAccess.Write, FileShare.Read, BUFSIZE);
		if (offset > 0) {
			dst.Seek(offset, SeekOrigin.Begin);
			// 续传前把多余部分截掉，避免上次多写的尾巴
			if (dst.Length > offset) dst.SetLength(offset);
		}
		long remain = remoteSize > 0 ? remoteSize - offset : 0;
		using var src = backend.OpenRead(job.Remote, offset, remoteSize);
		copy(src, dst, job, remain, (n) => job.Done += n);
	}

	const int BUFSIZE = 64 * 1024;

	/// <summary>
	/// 一读一写，中间按 TickCount 算速度。暂停/取消都在这里检查，所以粒度是 64KB。
	/// remain ≤ 0 表示长度未知，一直读到流结束。
	/// </summary>
	static void copy(Stream src, Stream dst, FtpJob job, long remain, Action<long> advance) {
		var buf = new byte[BUFSIZE];
		int lastTick = Environment.TickCount;
		long lastBytes = job.Done;
		long left = remain;
		while (true) {
			if (job.CancelFlag) throw new OperationCanceledException();
			if (job.PauseFlag) return;
			int want = buf.Length;
			if (left > 0 && left < want) want = (int)left;
			if (want <= 0) break;
			int n = src.Read(buf, 0, want);
			if (n <= 0) break;
			dst.Write(buf, 0, n);
			advance(n);
			if (left > 0) left -= n;

			int now = Environment.TickCount;
			int dt = now - lastTick;
			if (dt >= 300) {
				long done = job.Done;
				job.Speed = (long)((done - lastBytes) * 1000.0 / dt);
				lastBytes = done;
				lastTick = now;
			}
		}
		dst.Flush();
	}
	#endregion

	// ==================== 目录展开 ====================
	#region
	/// <summary>
	/// 把一个本机路径展开成「文件 → 远端目标」列表。目录递归展开，remote 下同名建结构。
	/// 调用方负责按顺序入队。
	/// </summary>
	public static List<(string local, string remote)> ExpandUpload(string localPath, string remoteRoot) {
		var list = new List<(string, string)>();
		if (File.Exists(localPath)) {
			list.Add((localPath, joinRemote(remoteRoot, Path.GetFileName(localPath))));
			return list;
		}
		if (!Directory.Exists(localPath)) return list;
		var baseName = Path.GetFileName(localPath.TrimEnd('\\', '/'));
		var rootRemote = joinRemote(remoteRoot, baseName);
		walk(localPath, rootRemote, list);
		return list;
	}

	static void walk(string dir, string remote, List<(string, string)> list) {
		foreach (var sub in Directory.GetDirectories(dir)) {
			var name = Path.GetFileName(sub);
			walk(sub, joinRemote(remote, name), list);
		}
		foreach (var f in Directory.GetFiles(dir)) {
			list.Add((f, joinRemote(remote, Path.GetFileName(f))));
		}
	}

	public static string joinRemote(string root, string name) {
		var r = (root ?? "").TrimEnd('/');
		if (r.Length == 0) return "/" + name;
		return r + "/" + name;
	}
	#endregion
}
