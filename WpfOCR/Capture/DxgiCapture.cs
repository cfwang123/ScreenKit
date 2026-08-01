using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace WpfOCR;

/// <summary>DXGI 抓取结果：帧像素 + 输出在桌面上的矩形。</summary>
sealed class DxgiFrame {
	public BitmapSource Image;
	/// <summary>DXGI DesktopCoordinates（与 Screen.Bounds 同坐标系）。</summary>
	public System.Drawing.Rectangle DesktopRect;
	public string DeviceName;
}

/// <summary>
/// DXGI Desktop Duplication 抓屏。
/// 副屏 GDI 常只得到左侧一条；DXGI 输出纹理尺寸才是真实分辨率（如 1920×1200）。
/// </summary>
static class DxgiCapture {
	/// <summary>抓取与 screen 匹配的输出全帧。失败返回 null。</summary>
	public static BitmapSource CaptureScreen(System.Windows.Forms.Screen screen) =>
		CaptureScreenEx(screen)?.Image;

	/// <summary>抓取并返回桌面矩形（用于遮罩与 Bounds 对齐）。</summary>
	public static DxgiFrame CaptureScreenEx(System.Windows.Forms.Screen screen) {
		if (screen == null) return null;
		var target = screen.Bounds;
		try {
			using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
			for (int ai = 0; factory.EnumAdapters1(ai, out var adapter).Success; ai++) {
				using (adapter) {
					for (int oi = 0; adapter.EnumOutputs(oi, out var output).Success; oi++) {
						using (output) {
							var desc = output.Description;
							var desk = desc.DesktopCoordinates;
							var outRect = new System.Drawing.Rectangle(
								desk.Left, desk.Top,
								Math.Max(1, desk.Right - desk.Left),
								Math.Max(1, desk.Bottom - desk.Top));

							// 设备名或矩形重叠匹配
							var nameMatch = !string.IsNullOrEmpty(desc.DeviceName)
								&& (desc.DeviceName.IndexOf(screen.DeviceName.TrimStart('\\').Replace(".\\", ""),
									StringComparison.OrdinalIgnoreCase) >= 0
									|| string.Equals(desc.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase));
							var inter = System.Drawing.Rectangle.Intersect(outRect, target);
							if (!nameMatch && (inter.Width < 8 || inter.Height < 8))
								continue;
							// 重叠面积够大
							if (inter.Width * inter.Height < target.Width * target.Height * 0.3
								&& !nameMatch)
								continue;

							CaptureLog.Info($"DXGI output#{ai}.{oi} '{desc.DeviceName}' desk={outRect} target={target} nameMatch={nameMatch}");

							var img = captureOutput(adapter, output);
							if (img == null) continue;

							CaptureLog.Info($"DXGI frame {CaptureLog.Bmp(img)} desk={outRect} (texture may differ from desk size)");
							// 桌面矩形：优先 DXGI DesktopCoordinates；若与纹理差很多，用纹理尺寸+desk 原点
							var deskForUi = outRect;
							if (img.PixelWidth > 0 && img.PixelHeight > 0
								&& (Math.Abs(img.PixelWidth - outRect.Width) > 8
									|| Math.Abs(img.PixelHeight - outRect.Height) > 8)) {
								// 纹理=真实像素；遮罩窗口用纹理尺寸贴在 desk 原点，避免 2560 窗 + 1920 图映射错位
								deskForUi = new System.Drawing.Rectangle(
									outRect.Left, outRect.Top, img.PixelWidth, img.PixelHeight);
								CaptureLog.Info($"DXGI adjust deskForUi={deskForUi} (match texture)");
							}

							return new DxgiFrame {
								Image = img,
								DesktopRect = deskForUi,
								DeviceName = desc.DeviceName ?? screen.DeviceName,
							};
						}
					}
				}
			}
			CaptureLog.Info("DXGI: no matching output");
			return null;
		}
		catch (Exception ex) {
			CaptureLog.Ex("DXGI CaptureScreen", ex);
			return null;
		}
	}

	static BitmapSource captureOutput(IDXGIAdapter1 adapter, IDXGIOutput output) {
		var deviceCreationFlags = DeviceCreationFlags.BgraSupport;
		D3D11.D3D11CreateDevice(
			adapter,
			DriverType.Unknown,
			deviceCreationFlags,
			new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_0 },
			out ID3D11Device device,
			out _,
			out ID3D11DeviceContext context).CheckError();

		using (device)
		using (context) {
			using var output1 = output.QueryInterface<IDXGIOutput1>();
			using var duplication = output1.DuplicateOutput(device);

			// 尽快出图：短超时、最多 3 帧，首帧可用即返回（目标进入截图 <0.5s）
			ID3D11Texture2D staging = null;
			try {
				BitmapSource best = null;
				var bestNb = -1.0;
				for (int attempt = 0; attempt < 3; attempt++) {
					var hr = duplication.AcquireNextFrame(60, out _, out var resource);
					if (hr.Failure) {
						if (hr == Vortice.DXGI.ResultCode.WaitTimeout)
							continue;
						CaptureLog.Info($"DXGI AcquireNextFrame hr={hr}");
						break;
					}

					try {
						using var tex = resource.QueryInterface<ID3D11Texture2D>();
						var td = tex.Description;
						staging ??= device.CreateTexture2D(new Texture2DDescription {
							Width = td.Width,
							Height = td.Height,
							MipLevels = 1,
							ArraySize = 1,
							Format = td.Format,
							SampleDescription = new SampleDescription(1, 0),
							Usage = ResourceUsage.Staging,
							BindFlags = BindFlags.None,
							CPUAccessFlags = CpuAccessFlags.Read,
							MiscFlags = ResourceOptionFlags.None,
						});

						context.CopyResource(staging, tex);

						var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
						try {
							var width = (int)td.Width;
							var height = (int)td.Height;
							var srcStride = (int)mapped.RowPitch;
							var dstStride = width * 4;
							var pixels = new byte[dstStride * height];
							// 按行拷（RowPitch 可能 > width*4）
							unsafe {
								var srcPtr = (byte*)mapped.DataPointer;
								for (int y = 0; y < height; y++)
									Marshal.Copy((IntPtr)(srcPtr + y * srcStride), pixels, y * dstStride, dstStride);
							}
							for (int i = 3; i < pixels.Length; i += 4)
								pixels[i] = 255;

							// 稀疏采样 nonBlack
							long nb = 0, n = 0;
							var step = Math.Max(16, (dstStride * height) / 2000 / 4 * 4);
							if (step < 4) step = 4;
							for (int i = 0; i + 3 < pixels.Length; i += step) {
								n++;
								if (pixels[i] > 12 || pixels[i + 1] > 12 || pixels[i + 2] > 12) nb++;
							}
							var ratio = n > 0 ? nb / (double)n : 0;

							var bmp = BitmapSource.Create(width, height, 96, 96,
								PixelFormats.Bgra32, null, pixels, dstStride);
							bmp.Freeze();
							if (ratio > bestNb) {
								bestNb = ratio;
								best = bmp;
							}
							// 首帧可用即走（快路径）
							if (ratio > 0.08)
								return bmp;
						}
						finally {
							context.Unmap(staging, 0);
						}
					}
					finally {
						try { resource?.Dispose(); } catch { }
						try { duplication.ReleaseFrame(); } catch { }
					}
				}
				if (best != null)
					CaptureLog.Info($"DXGI best frame nonBlack~{bestNb:P0} {CaptureLog.Bmp(best)}");
				return best;
			}
			finally {
				staging?.Dispose();
			}
		}
	}
}
