namespace ScreenKit;

/// <summary>按站点协议造后端。界面和队列都只经过这里，不直接 new 具体实现。</summary>
static class FtpBackendFactory {
	/// <summary>
	/// 先把第三方库按字节载入再 new。后端类直接引用 FluentFTP / SSH.NET 的类型，
	/// 缺库时在 JIT 阶段就会抛，所以这里必须先行加载；缺文件抛 MissingFeatureException。
	/// </summary>
	public static IFtpBackend Create(string kind) {
		FtpDeps.LoadAll();
		if (FtpProto.IsSftp(kind)) return new SftpBackend();
		return new FtpBackend();
	}

	public static IFtpBackend Create(FtpSite site) => Create(site?.Kind);
}
