namespace ScreenKit;

/// <summary>录屏视频写入：FFmpeg MP4、Media Foundation H.264 或 MJPEG AVI。</summary>
interface IRecordVideoSink : IDisposable {
	int OutWidth { get; }
	int OutHeight { get; }
	string CodecName { get; }
	string OpenedEncoder { get; }
	void WriteBgra(byte[] bgra, int stride, long pts);
	void Finish();
}
