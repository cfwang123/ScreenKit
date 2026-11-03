namespace ScreenKit;

/// <summary>按站点协议造后端。界面和队列都只经过这里，不直接 new 具体实现。</summary>
static class FtpBackendFactory {
	/// <summary>
	/// 先把第三方库按字节载入再 new。后端类直接引用 FluentFTP / SSH.NET 的类型，
	/// 缺库时在 JIT 阶段就会抛，所以这里必须先行加载；缺文件抛 MissingFeatureException。
	/// </summary>
	public static IFtpBackend Create(string kind) {
		// TFTP 自己实现，不依赖第三方库，不需要先装依赖
		if (FtpProto.IsTftp(kind)) return new TftpBackend();
		FtpDeps.LoadAll();
		if (FtpProto.IsSftp(kind)) return new SftpBackend();
		return new FtpBackend();
	}

	/// <summary>这个协议需不需要 FluentFTP / SSH.NET 那两个库。TFTP 不需要。</summary>
	public static bool NeedsDeps(string kind) => !FtpProto.IsTftp(kind);

	public static IFtpBackend Create(FtpSite site) => Create(site?.Kind);
}
