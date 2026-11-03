namespace ScreenKit;

/// <summary>
/// 远端文件系统的一层抽象。FTP / FTPS / SFTP 各实现一份，界面只认这个接口。
/// 实现都不保证线程安全：浏览和传输各自持有独立实例。
/// </summary>
interface IFtpBackend : IDisposable {
	bool Connected { get; }

	/// <summary>建立连接。失败抛异常，消息可直接展示给用户。</summary>
	void Connect(FtpSite site, string password);

	void Disconnect();

	/// <summary>列目录，不含 . 和 ..。返回项已填 Name / Full / IsDir / Size / Modified / Perms。</summary>
	List<FtpEntry> List(string path);

	/// <summary>当前工作目录绝对路径。</summary>
	string WorkDir();

	/// <summary>把 path 规整成绝对路径（远程路径用 / 分隔）。</summary>
	string Full(string path);

	void MkDir(string path);

	/// <summary>递归删目录。</summary>
	void RmDir(string path);

	void RmFile(string path);

	void Rename(string src, string dst);

	/// <summary>改权限，mode 是八进制数值（如 755）。不支持时抛 NotSupportedException。</summary>
	void Chmod(string path, int mode);

	/// <summary>文件大小；不存在返回 -1。</summary>
	long Size(string path);

	/// <summary>远程路径是否是目录。</summary>
	bool IsDir(string path);

	/// <summary>读远端文件，offset 为续传起点。</summary>
	Stream OpenRead(string path, long offset, long length);

	/// <summary>
	/// 写远端文件。offset == 0 时新建覆盖；offset &gt; 0 时追加（远端已有内容长度必须等于 offset，
	/// 由 <see cref="AlignResume"/> 先行确认）。
	/// </summary>
	Stream OpenWrite(string path, long offset);

	/// <summary>
	/// 续传起点对齐：远端当前长度正好等于 want 才能从 want 接着传，否则只能从 0 重来。
	/// 返回实际可用的起点。
	/// </summary>
	long AlignResume(string path, long want);
}
