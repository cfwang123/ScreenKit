namespace ScreenKit;

/// <summary>按站点协议造后端。界面和队列都只经过这里，不直接 new 具体实现。</summary>
static class FtpBackendFactory {
	public static IFtpBackend Create(string kind) {
		if (FtpProto.IsSftp(kind)) return new SftpBackend();
		return new FtpBackend();
	}

	public static IFtpBackend Create(FtpSite site) => Create(site?.Kind);
}
