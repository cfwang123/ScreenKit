using System.ComponentModel;

namespace ScreenKit;

/// <summary>传输任务的方向。</summary>
enum FtpDir { Upload, Download }

/// <summary>传输任务状态。界面按它决定显示什么按钮。</summary>
enum FtpJobState { Waiting, Running, Paused, Done, Failed, Canceled }

/// <summary>
/// 一条传输任务。进度字段由后台线程写、UI 线程读，用 Interlocked 保证可见性。
/// 属性加上通知，只为了让 ListView 绑定能刷新文字。
/// </summary>
sealed class FtpJob : INotifyPropertyChanged {
	static int seq;

	public int Id { get; } = Interlocked.Increment(ref seq);
	public FtpDir Dir { get; set; }
	/// <summary>本机完整路径。</summary>
	public string Local { get; set; } = "";
	/// <summary>远端完整路径。</summary>
	public string Remote { get; set; } = "";
	public string DisplayName { get; set; } = "";
	/// <summary>出错时的原始信息，重试时清空。</summary>
	public string Error { get; set; } = "";
	public FtpSite Site { get; set; }

	long total;
	long done;
	FtpJobState state = FtpJobState.Waiting;
	long speed;
	int startTick;
	int endTick;

	public long Total {
		get => Interlocked.Read(ref total);
		set { Interlocked.Exchange(ref total, value); notify(nameof(Total)); notify(nameof(Percent)); notify(nameof(SizeText)); }
	}

	public long Done {
		get => Interlocked.Read(ref done);
		set { Interlocked.Exchange(ref done, value); notify(nameof(Done)); notify(nameof(Percent)); notify(nameof(SizeText)); }
	}

	public FtpJobState State {
		get => state;
		set {
			if (state == value) return;
			state = value;
			if (value == FtpJobState.Running && startTick == 0) startTick = Environment.TickCount;
			if (value is FtpJobState.Done or FtpJobState.Failed or FtpJobState.Canceled)
				endTick = Environment.TickCount;
			notify(nameof(State));
			notify(nameof(StateText));
			notify(nameof(Percent));
			notify(nameof(CanPause));
			notify(nameof(CanResume));
			notify(nameof(CanCancel));
			notify(nameof(SpeedText));
		}
	}

	public long Speed {
		get => Interlocked.Read(ref speed);
		set {
			if (Interlocked.Read(ref speed) == value) return;
			Interlocked.Exchange(ref speed, value);
			notify(nameof(Speed));
			notify(nameof(SpeedText));
		}
	}

	public int StartTick => startTick;
	public int EndTick => endTick;

	public double Percent {
		get {
			var t = Total;
			if (t <= 0) return State == FtpJobState.Done ? 100 : 0;
			var d = Done;
			if (d > t) d = t;
			return t == 0 ? 0 : d * 100.0 / t;
		}
	}

	public string StateText => State switch {
		FtpJobState.Waiting => Loc.T("ftp.state.wait"),
		FtpJobState.Running => Loc.T("ftp.state.run"),
		FtpJobState.Paused => Loc.T("ftp.state.pause"),
		FtpJobState.Done => Loc.T("ftp.state.done"),
		FtpJobState.Failed => Loc.T("ftp.state.fail"),
		FtpJobState.Canceled => Loc.T("ftp.state.cancel"),
		_ => ""
	};

	public string SizeText => Total > 0 ? $"{FtpFmt.Size(Done)} / {FtpFmt.Size(Total)}" : FtpFmt.Size(Done);

	public string SpeedText => State != FtpJobState.Running ? "" : $"{FtpFmt.Size(Speed)}/s";

	public string DirText => Dir == FtpDir.Upload ? Loc.T("ftp.dir.up") : Loc.T("ftp.dir.down");

	public bool CanPause => State is FtpJobState.Running or FtpJobState.Waiting;
	public bool CanResume => State is FtpJobState.Paused or FtpJobState.Failed or FtpJobState.Canceled;
	public bool CanCancel => State is FtpJobState.Running or FtpJobState.Waiting or FtpJobState.Paused;

	public event PropertyChangedEventHandler PropertyChanged;

	/// <summary>暂停/取消标志。后台循环每次读缓冲都检查一次。</summary>
	internal volatile bool PauseFlag;
	internal volatile bool CancelFlag;

	public void ResetForRetry() {
		PauseFlag = false;
		CancelFlag = false;
		Error = "";
		startTick = 0;
		endTick = 0;
		State = FtpJobState.Waiting;
	}

	internal void notify(string prop) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
