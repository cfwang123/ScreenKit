using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>
/// Umi-OCR / RapidOCR 风格 PP-OCR 管线：det → (cls) → rec。
/// 模型文件由 ModelPack + ModelVariant 解析（兼容 Umi configs.txt 与 rapid 命名）。
/// </summary>
sealed class OcrEngine : IDisposable {
	readonly InferenceSession det;
	readonly InferenceSession cls;
	readonly InferenceSession rec;
	readonly string detIn;
	readonly string clsIn;
	readonly string recIn;
	readonly string[] charset;
	readonly OcrOptions opt;
	readonly string deviceUsed;
	readonly string modelLabel;
	bool disposed;

	public string DeviceUsed => deviceUsed;
	public string ModelLabel => modelLabel;

	public OcrEngine(OcrOptions options) {
		opt = options ?? throw new ArgumentNullException(nameof(options));

		var (packDir, variant, label) = resolvevariant(opt);
		opt.ModelsDir = packDir;
		modelLabel = label;

		try { CudaBootstrap.Init(); } catch { }

		var detPath = variant.DetPath(packDir);
		var clsPath = variant.ClsPath(packDir);
		var recPath = variant.RecPath(packDir);
		var keysPath = variant.KeysPath(packDir);
		variant.Validate(packDir);

		charset = loadkeys(keysPath);
		(det, cls, rec, deviceUsed) = createsessions(detPath, clsPath, recPath, opt.Device);
		detIn = det.InputMetadata.Keys.First();
		clsIn = cls.InputMetadata.Keys.First();
		recIn = rec.InputMetadata.Keys.First();
	}

	/// <summary>
	/// 同步推理期参数（边长/阈值/cls 等）。不重建 ONNX session。
	/// </summary>
	public void ApplyRuntime(OcrOptions o) {
		Compat.ThrowIfDisposed(disposed, this);
		if (o == null) return;
		opt.DetLimitSideLen = o.DetLimitSideLen;
		opt.DetPadding = o.DetPadding;
		opt.DetThresh = o.DetThresh;
		opt.DetBoxThresh = o.DetBoxThresh;
		opt.DetUnclipRatio = o.DetUnclipRatio;
		opt.DetUseDilation = o.DetUseDilation;
		opt.RecImgH = o.RecImgH;
		opt.RecMaxWidth = o.RecMaxWidth;
		opt.RecAbsMaxWidth = o.RecAbsMaxWidth;
		opt.RecBatchNum = o.RecBatchNum;
		opt.UseCls = o.UseCls;
	}

	public OcrResult Run(string imagePath) {
		if (!File.Exists(imagePath))
			throw new FileNotFoundException("图像不存在", imagePath);
		using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
		if (mat.Empty())
			throw new InvalidOperationException($"无法读取图像: {imagePath}");
		return Run(mat);
	}

	public OcrResult Run(Mat bgr) {
		Compat.ThrowIfDisposed(disposed, this);
		var t0 = Environment.TickCount;
		var boxes = detect(bgr);

		// 并行裁剪（纯 OpenCV）；cls/rec 的 ORT session 非线程安全，后面串行
		var n = boxes.Count;
		var crops = new Mat[n];
		Parallel.For(0, n, i => {
			var crop = getrotatecrop(bgr, boxes[i]);
			if (crop.Empty() || crop.Width < 2 || crop.Height < 2) {
				crop?.Dispose();
				crops[i] = null;
			}
			else crops[i] = crop;
		});

		// 串行 cls + 收集待识别
		var pending = new List<(int idx, Mat crop, int rw)>(n);
		for (int i = 0; i < n; i++) {
			var crop = crops[i];
			if (crop == null) continue;
			if (opt.UseCls && classify(crop) == 180) {
				var flipped = new Mat();
				Cv2.Rotate(crop, flipped, RotateFlags.Rotate180);
				crop.Dispose();
				crops[i] = flipped;
				crop = flipped;
			}
			pending.Add((i, crop, recresizedwidth(crop)));
		}
		// 仅「完全相同识别宽」的条目才组 batch（避免 pad 改变 CTC 结果）
		// 不同宽逐条跑，与优化前逐条动态宽数学等价
		pending.Sort((a, b) => {
			var c = a.rw.CompareTo(b.rw);
			return c != 0 ? c : a.idx.CompareTo(b.idx);
		});

		var lines = new List<OcrLine>(pending.Count);
		var batchN = Math.Max(1, opt.RecBatchNum);
		var i0 = 0;
		while (i0 < pending.Count) {
			var w0 = pending[i0].rw;
			var i1 = i0 + 1;
			while (i1 < pending.Count && pending[i1].rw == w0 && (i1 - i0) < batchN)
				i1++;
			recognbatch(pending, i0, i1, boxes, lines);
			i0 = i1;
		}

		for (int i = 0; i < n; i++)
			try { crops[i]?.Dispose(); } catch { }

		// 按从上到下、从左到右排序
		lines.Sort((a, b) => {
			var ay = a.Box.Average(p => p.Y);
			var by = b.Box.Average(p => p.Y);
			if (Math.Abs(ay - by) > 10) return ay.CompareTo(by);
			var ax = a.Box.Average(p => p.X);
			var bx = b.Box.Average(p => p.X);
			return ax.CompareTo(bx);
		});
		return new OcrResult {
			Lines = lines,
			DeviceUsed = deviceUsed,
			ModelLabel = modelLabel,
			InferMs = Environment.TickCount - t0,
		};
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
		det?.Dispose();
		cls?.Dispose();
		rec?.Dispose();
	}

	// ───────── detection（对齐 RapidOCR DBPostProcess） ─────────

	List<Point2f[]> detect(Mat bgr) {
		var srcH = bgr.Rows;
		var srcW = bgr.Cols;
		var pad = Math.Max(0, opt.DetPadding);

		// padding：与 RapidOCR-json 一致，边缘文字不易被裁切
		Mat work = bgr;
		Mat padded = null;
		if (pad > 0) {
			padded = new Mat();
			Cv2.CopyMakeBorder(bgr, padded, pad, pad, pad, pad, BorderTypes.Constant, Scalar.All(255));
			work = padded;
		}

		try {
			var (resized, _, _) = resizelimit(work, opt.DetLimitSideLen, 32);
			try {
				var tensor = tonchw05(resized);
				using var results = det.Run(new[] { NamedOnnxValue.CreateFromTensor(detIn, tensor) });
				var outT = results.First().AsTensor<float>();
				var dims = outT.Dimensions.ToArray();
				int h, w;
				if (dims.Length == 4) { h = dims[2]; w = dims[3]; }
				else if (dims.Length == 3) { h = dims[1]; w = dims[2]; }
				else throw new InvalidOperationException($"det 输出维度异常: [{string.Join(",", dims)}]");

				var flat = outT.ToArray();
				var pred = new float[h * w];
				Array.Copy(flat, 0, pred, 0, h * w);

				// 坐标：特征图 → padding 图 → 原图
				var boxes = dbpost(pred, h, w, work.Rows, work.Cols);
				if (pad > 0) {
					foreach (var box in boxes) {
						for (int i = 0; i < box.Length; i++) {
							box[i] = new Point2f(
								Compat.Clamp(box[i].X - pad, 0, srcW - 1),
								Compat.Clamp(box[i].Y - pad, 0, srcH - 1));
						}
					}
				}
				// 合并同行近邻框（如「供」「方」被拆成两框）
				return mergeboxes(boxes);
			}
			finally {
				resized.Dispose();
			}
		}
		finally {
			padded?.Dispose();
		}
	}

	List<Point2f[]> dbpost(float[] pred, int h, int w, int destH, int destW) {
		var bytes = new byte[h * w];
		var th = opt.DetThresh;
		for (int i = 0; i < pred.Length; i++)
			bytes[i] = pred[i] > th ? (byte)255 : (byte)0;
		using var bin8 = Mat.FromPixelData(h, w, MatType.CV_8UC1, bytes);

		if (opt.DetUseDilation) {
			using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2));
			Cv2.Dilate(bin8, bin8, kernel);
		}

		Cv2.FindContours(bin8, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
		var boxes = new List<Point2f[]>();
		const int minSize = 3;
		var maxCand = Math.Min(contours.Length, 1000);

		for (int ci = 0; ci < maxCand; ci++) {
			var cnt = contours[ci];
			if (cnt.Length < 4) continue;

			var rect = Cv2.MinAreaRect(cnt);
			var sside = Math.Min(rect.Size.Width, rect.Size.Height);
			if (sside < minSize) continue;

			var points = rect.Points().Select(p => new Point2f(p.X, p.Y)).ToArray();
			var score = boxscorefast(pred, h, w, points);
			if (score < opt.DetBoxThresh) continue;

			// unclip：按 area*ratio/peri 放大旋转矩形（稳定；pyclipper 级多边形外扩可后续再加）
			var area = rect.Size.Width * rect.Size.Height;
			var peri = 2 * (rect.Size.Width + rect.Size.Height);
			var distance = peri < 1e-3f ? 0f : area * opt.DetUnclipRatio / peri;
			var expanded = new RotatedRect(
				rect.Center,
				new Size2f(rect.Size.Width + distance * 2, rect.Size.Height + distance * 2),
				rect.Angle);
			var expPts = expanded.Points().Select(p => new Point2f(p.X, p.Y)).ToArray();
			var shortAfter = Math.Min(expanded.Size.Width, expanded.Size.Height);
			if (shortAfter < minSize + 2) continue;

			// 特征图坐标 → 目标图尺寸（Rapid: x/w*dest_w）
			var mapped = new Point2f[4];
			for (int i = 0; i < 4; i++) {
				var x = Compat.Clamp((float)Math.Round(expPts[i].X / w * destW), 0, destW - 1);
				var y = Compat.Clamp((float)Math.Round(expPts[i].Y / h * destH), 0, destH - 1);
				mapped[i] = new Point2f(x, y);
			}
			mapped = orderpoints(mapped);
			var bw = dist(mapped[0], mapped[1]);
			var bh = dist(mapped[0], mapped[3]);
			if (bw <= 3 || bh <= 3) continue;
			boxes.Add(mapped);
		}
		return boxes;
	}

	/// <summary>
	/// 合并同一行、水平间隙小的检测框，避免单字被拆成多行（「供」「方」→「供方」）。
	/// </summary>
	static List<Point2f[]> mergeboxes(List<Point2f[]> boxes) {
		if (boxes == null || boxes.Count <= 1) return boxes ?? new List<Point2f[]>();

		// 阅读序：上→下、左→右
		var list = boxes
			.Where(b => b != null && b.Length >= 4)
			.Select(b => orderpoints(b))
			.OrderBy(b => (b[0].Y + b[1].Y + b[2].Y + b[3].Y) / 4f)
			.ThenBy(b => (b[0].X + b[1].X + b[2].X + b[3].X) / 4f)
			.ToList();

		bool changed = true;
		while (changed) {
			changed = false;
			for (int i = 0; i < list.Count; i++) {
				for (int j = i + 1; j < list.Count; j++) {
					if (!canmerge(list[i], list[j])) continue;
					list[i] = mergequad(list[i], list[j]);
					list.RemoveAt(j);
					changed = true;
					break;
				}
				if (changed) break;
			}
		}
		return list;
	}

	static bool canmerge(Point2f[] a, Point2f[] b) {
		// 轴对齐包围
		float minXa = a.Min(p => p.X), maxXa = a.Max(p => p.X);
		float minYa = a.Min(p => p.Y), maxYa = a.Max(p => p.Y);
		float minXb = b.Min(p => p.X), maxXb = b.Max(p => p.X);
		float minYb = b.Min(p => p.Y), maxYb = b.Max(p => p.Y);
		var ha = Math.Max(1f, maxYa - minYa);
		var hb = Math.Max(1f, maxYb - minYb);
		var wa = Math.Max(1f, maxXa - minXa);
		var wb = Math.Max(1f, maxXb - minXb);
		var cya = (minYa + maxYa) * 0.5f;
		var cyb = (minYb + maxYb) * 0.5f;

		// 高度接近、中心 y 对齐 → 同一行
		var hMax = Math.Max(ha, hb);
		var hMin = Math.Min(ha, hb);
		if (Math.Abs(cya - cyb) > hMax * 0.45f) return false;
		if (hMax / hMin > 1.8f) return false;

		// 只合并「短框」（单字/少数字），避免把同行长句粘成一整条
		// 「供」「方」宽约 1 个字高；长句宽远大于字高
		if (wa > hMax * 3.2f || wb > hMax * 3.2f) return false;

		// 水平间隙：重叠或很近才合
		var gap = minXb > maxXa ? minXb - maxXa : (minXa > maxXb ? minXa - maxXb : 0f);
		if (gap <= 0) return true;
		// 字间距通常 < 0.8 字高
		return gap <= hMax * 0.9f;
	}

	static Point2f[] mergequad(Point2f[] a, Point2f[] b) {
		var minX = Math.Min(a.Min(p => p.X), b.Min(p => p.X));
		var maxX = Math.Max(a.Max(p => p.X), b.Max(p => p.X));
		var minY = Math.Min(a.Min(p => p.Y), b.Min(p => p.Y));
		var maxY = Math.Max(a.Max(p => p.Y), b.Max(p => p.Y));
		return orderpoints(new[] {
			new Point2f(minX, minY),
			new Point2f(maxX, minY),
			new Point2f(maxX, maxY),
			new Point2f(minX, maxY),
		});
	}

	/// <summary>Rapid box_score_fast：minAreaRect 四点包围盒内概率均值。</summary>
	static float boxscorefast(float[] pred, int h, int w, Point2f[] box) {
		var xmin = Compat.Clamp((int)Math.Floor(box.Min(p => p.X)), 0, w - 1);
		var xmax = Compat.Clamp((int)Math.Ceiling(box.Max(p => p.X)), 0, w - 1);
		var ymin = Compat.Clamp((int)Math.Floor(box.Min(p => p.Y)), 0, h - 1);
		var ymax = Compat.Clamp((int)Math.Ceiling(box.Max(p => p.Y)), 0, h - 1);
		if (xmax <= xmin || ymax <= ymin) return 0;

		using var mask = new Mat(ymax - ymin + 1, xmax - xmin + 1, MatType.CV_8UC1, Scalar.All(0));
		var shifted = box.Select(p => new OpenCvSharp.Point(
			(int)Math.Round(p.X - xmin), (int)Math.Round(p.Y - ymin))).ToArray();
		Cv2.FillPoly(mask, new[] { shifted }, Scalar.All(255));
		var mbuf = new byte[mask.Rows * mask.Cols];
		System.Runtime.InteropServices.Marshal.Copy(mask.Data, mbuf, 0, mbuf.Length);

		double sum = 0;
		int n = 0;
		var mw = xmax - xmin + 1;
		for (int y = ymin; y <= ymax; y++) {
			for (int x = xmin; x <= xmax; x++) {
				if (mbuf[(y - ymin) * mw + (x - xmin)] == 0) continue;
				sum += pred[y * w + x];
				n++;
			}
		}
		return n == 0 ? 0 : (float)(sum / n);
	}

	/// <summary>
	/// 四点排序为 tl,tr,br,bl（imutils / Paddle 经典算法：sum 定对角，diff 定另外两角）。
	/// </summary>
	static Point2f[] orderpoints(Point2f[] pts) {
		if (pts == null || pts.Length < 4) return pts;
		// 去重取 4 点
		var p = pts.Take(4).ToArray();
		var sums = p.Select(x => x.X + x.Y).ToArray();
		var diffs = p.Select(x => x.Y - x.X).ToArray();
		int iTl = 0, iBr = 0, iTr = 0, iBl = 0;
		for (int i = 1; i < 4; i++) {
			if (sums[i] < sums[iTl]) iTl = i;
			if (sums[i] > sums[iBr]) iBr = i;
		}
		// 剩余两点用 y-x
		for (int i = 0; i < 4; i++) {
			if (i == iTl || i == iBr) continue;
			iTr = i;
			break;
		}
		for (int i = 0; i < 4; i++) {
			if (i == iTl || i == iBr || i == iTr) continue;
			iBl = i;
			break;
		}
		// tr 应有更小的 (y-x)，bl 更大
		if (diffs[iTr] > diffs[iBl]) {
			var t = iTr; iTr = iBl; iBl = t;
		}
		return new[] { p[iTl], p[iTr], p[iBr], p[iBl] };
	}

	static float dist(Point2f a, Point2f b) {
		var dx = a.X - b.X;
		var dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	static Mat getrotatecrop(Mat bgr, Point2f[] box) {
		// 与 Paddle/Rapid get_rotate_crop_image 一致：四点必须 tl,tr,br,bl
		var pts = orderpoints(box);
		var w = Math.Max(dist(pts[0], pts[1]), dist(pts[3], pts[2]));
		var h = Math.Max(dist(pts[0], pts[3]), dist(pts[1], pts[2]));
		if (w < 1 || h < 1) return new Mat();
		var srcCv = new[] {
			new OpenCvSharp.Point2f(pts[0].X, pts[0].Y),
			new OpenCvSharp.Point2f(pts[1].X, pts[1].Y),
			new OpenCvSharp.Point2f(pts[2].X, pts[2].Y),
			new OpenCvSharp.Point2f(pts[3].X, pts[3].Y),
		};
		var dstCv = new[] {
			new OpenCvSharp.Point2f(0, 0),
			new OpenCvSharp.Point2f(w, 0),
			new OpenCvSharp.Point2f(w, h),
			new OpenCvSharp.Point2f(0, h),
		};
		using var m = Cv2.GetPerspectiveTransform(srcCv, dstCv);
		var dst = new Mat();
		var dw = Math.Max(1, (int)Math.Ceiling(w));
		var dh = Math.Max(1, (int)Math.Ceiling(h));
		Cv2.WarpPerspective(bgr, dst, m, new OpenCvSharp.Size(dw, dh),
			InterpolationFlags.Cubic, BorderTypes.Replicate);
		// 竖条转横
		if (dst.Rows >= dst.Cols * 1.5) {
			var rot = new Mat();
			Cv2.Rotate(dst, rot, RotateFlags.Rotate90Counterclockwise);
			dst.Dispose();
			return rot;
		}
		return dst;
	}

	// ───────── classification ─────────

	int classify(Mat bgr) {
		// 高 48，宽按比例，限 192；仅在 180° 置信明显高于 0° 时翻转，避免误翻
		const int imgH = 48;
		const int imgW = 192;
		using var resized = resizefill(bgr, imgW, imgH);
		var tensor = tonchw05(resized);
		using var results = cls.Run(new[] { NamedOnnxValue.CreateFromTensor(clsIn, tensor) });
		var outT = results.First().AsEnumerable<float>().ToArray();
		if (outT.Length < 2) return 0;
		// 需要明显偏向 180°（阈值 0.9 与 Rapid cls_thresh 一致）
		return outT[1] > outT[0] && outT[1] >= 0.9f ? 180 : 0;
	}

	// ───────── recognition ─────────

	int recresizedwidth(Mat bgr) {
		var imgH = opt.RecImgH;
		var baseW = Math.Max(8, opt.RecMaxWidth);
		var absMax = Math.Max(baseW, opt.RecAbsMaxWidth);
		var whRatio = (float)bgr.Cols / Math.Max(bgr.Rows, 1);
		var maxWh = Math.Max((float)baseW / imgH, whRatio);
		var canvasW = Math.Min(absMax, Math.Max(8, (int)(imgH * maxWh)));
		var resizedW = (int)Math.Ceiling(imgH * whRatio);
		if (resizedW > canvasW) resizedW = canvasW;
		return Math.Max(8, resizedW);
	}

	/// <summary>
	/// 批识别：批内 pad 到最大宽（右侧 0 像素 → 归一化后 -1），语义同 Rapid resize_norm_img。
	/// 批大小=1 时与逐条动态宽完全一致。
	/// </summary>
	void recognbatch(
		List<(int idx, Mat crop, int rw)> pending, int beg, int end,
		List<Point2f[]> boxes, List<OcrLine> lines) {
		var count = end - beg;
		if (count <= 0) return;
		var imgH = opt.RecImgH;

		// 批内最大宽（与 Rapid：max_wh_ratio 取批内最大一致）
		var maxW = 8;
		for (int i = beg; i < end; i++)
			if (pending[i].rw > maxW) maxW = pending[i].rw;

		var batch = new float[count * 3 * imgH * maxW];
		var realW = new int[count];
		for (int bi = 0; bi < count; bi++) {
			var crop = pending[beg + bi].crop;
			var rw = pending[beg + bi].rw;
			realW[bi] = rw;
			using var resized = new Mat();
			Cv2.Resize(crop, resized, new OpenCvSharp.Size(rw, imgH));
			// 写入 batch[bi, c, y, x]；右侧保持 0（归一化前 pad=0 → 值 -1，同 Rapid）
			tonchw05into(resized, batch, bi, imgH, maxW);
		}

		var tensor = new DenseTensor<float>(batch, new[] { count, 3, imgH, maxW });
		using var results = rec.Run(new[] { NamedOnnxValue.CreateFromTensor(recIn, tensor) });
		var outT = results.First().AsTensor<float>();
		var dims = outT.Dimensions.ToArray();
		// 期望 [N, T, C] 或 [N, C, T]
		if (dims.Length != 3)
			throw new InvalidOperationException($"rec 输出维度异常: [{string.Join(",", dims)}]");

		var data = outT.ToArray();
		int nOut = dims[0];
		if (nOut != count)
			throw new InvalidOperationException($"rec batch 输出 N={nOut} 期望 {count}");

		int tLen, cLen;
		bool timeLast;
		if (dims[2] >= dims[1]) {
			tLen = dims[1]; cLen = dims[2]; timeLast = true;
		}
		else {
			cLen = dims[1]; tLen = dims[2]; timeLast = false;
		}
		var plane = tLen * cLen;

		for (int bi = 0; bi < count; bi++) {
			var (text, score) = ctcdecodeplane(data, bi * plane, tLen, cLen, timeLast, pending[beg + bi].crop);
			if (string.IsNullOrWhiteSpace(text)) continue;
			var boxIdx = pending[beg + bi].idx;
			lines.Add(new OcrLine {
				Text = text,
				Score = score,
				Box = boxes[boxIdx],
			});
		}
	}

	(string text, float score) ctcdecodeplane(float[] data, int offset, int t, int c, bool timeLast, Mat crop) {
		// 记录每个字的 CTC 时段，便于按空白步数 / 图像间隙补空格
		var hits = new List<(string ch, int t0, int t1, float v)>(32);
		const int blank = 0;
		var cur = -1;
		var curStart = 0;
		var curV = 0f;
		var curN = 0;
		for (int i = 0; i < t; i++) {
			int best = 0;
			float bestV = float.NegativeInfinity;
			if (timeLast) {
				var row = offset + i * c;
				for (int j = 0; j < c; j++) {
					var v = data[row + j];
					if (v > bestV) { bestV = v; best = j; }
				}
			}
			else {
				for (int j = 0; j < c; j++) {
					var v = data[offset + j * t + i];
					if (v > bestV) { bestV = v; best = j; }
				}
			}
			if (best == blank) {
				if (cur >= 0) flush(i - 1);
				continue;
			}
			if (best == cur) {
				curV += bestV;
				curN++;
				continue;
			}
			if (cur >= 0) flush(i - 1);
			cur = best;
			curStart = i;
			curV = bestV;
			curN = 1;
		}
		if (cur >= 0) flush(t - 1);

		if (hits.Count == 0) return ("", 0f);
		var scoreSum = 0f;
		foreach (var h in hits) scoreSum += h.v;
		var score = scoreSum / hits.Count;
		if (score > 1.5f || score < 0f) {
			scoreSum = 0;
			foreach (var h in hits)
				scoreSum += 1f / (1f + MathF.Exp(-h.v));
			score = scoreSum / hits.Count;
		}
		var text = insertspaces(hits, t, crop);
		return (text, Compat.Clamp(score, 0f, 1f));

		void flush(int end) {
			if (cur < 1) { cur = -1; return; }
			var idx = cur - 1;
			cur = -1;
			if ((uint)idx >= (uint)charset.Length) return;
			var s = charset[idx];
			if (s.Length == 0) s = " ";
			hits.Add((s, curStart, end, curN > 0 ? curV / curN : 0f));
		}
	}

	/// <summary>
	/// 韩/英等词间有空格的语言：PP-OCR 即使字典有空格也很少输出；中文 rec 字典没有空格。
	/// 按裁剪图列对比度谷（达到词距宽度）补空格。中日文汉字之间不插。
	/// </summary>
	static string insertspaces(List<(string ch, int t0, int t1, float v)> hits, int tLen, Mat crop) {
		if (hits == null || hits.Count == 0) return "";
		if (hits.Count == 1) return hits[0].ch;

		var durs = new int[hits.Count];
		for (int i = 0; i < hits.Count; i++)
			durs[i] = Math.Max(1, hits[i].t1 - hits[i].t0 + 1);
		Array.Sort(durs);
		var med = durs[durs.Length / 2];

		var w = 0;
		var h = 0;
		List<(int x0, int x1)> gaps = null;
		if (crop != null && !crop.Empty()) {
			w = crop.Cols;
			h = crop.Rows;
			gaps = findgaps(crop);
		}
		var charW = tLen > 0 && w > 0 ? med * (float)w / tLen : 0f;
		var minGapPx = Math.Max(6f, Math.Max(h * 0.24f, charW * 0.42f));

		var sb = new StringBuilder(hits.Count * 2);
		sb.Append(hits[0].ch);
		for (int i = 1; i < hits.Count; i++) {
			var a = hits[i - 1].ch;
			var b = hits[i].ch;
			var ca = a.Length > 0 ? a[0] : ' ';
			var cb = b.Length > 0 ? b[0] : ' ';
			if (ca == ' ' || cb == ' ') {
				sb.Append(b);
				continue;
			}
			var visGap = false;
			if (gaps != null && tLen > 0 && w > 0) {
				var xa = (hits[i - 1].t0 + hits[i - 1].t1 + 1) * 0.5f / tLen * w;
				var xb = (hits[i].t0 + hits[i].t1 + 1) * 0.5f / tLen * w;
				if (xb < xa) { var tmp = xa; xa = xb; xb = tmp; }
				var wa = Math.Max(1, hits[i - 1].t1 - hits[i - 1].t0 + 1) * (float)w / tLen;
				var wb = Math.Max(1, hits[i].t1 - hits[i].t0 + 1) * (float)w / tLen;
				var need = Math.Max(minGapPx, Math.Min(wa, wb) * 0.4f);
				if (ishangul(ca) && ishangul(cb))
					need = Math.Max(need, Math.Min(wa, wb) * 0.5f);
				visGap = hasgapbetween(gaps, xa, xb, need);
			}
			if (wantspace(ca, cb, visGap))
				sb.Append(' ');
			sb.Append(b);
		}
		return sb.ToString();
	}

	static bool wantspace(char a, char b, bool gap) {
		if (iscjk(a) && iscjk(b)) return false;
		if (nospcbefore(b) || nospcafter(a)) return false;
		if (!gap) return false;
		if (iswordch(a) && iswordch(b)) return true;
		if (iswordch(a) && !iscjk(b)) return true;
		if (iswordch(b) && !iscjk(a)) return true;
		if (iscjk(a) && iswordch(b)) return true;
		if (iscjk(b) && iswordch(a)) return true;
		return false;
	}

	static bool iscjk(char ch) =>
		(ch >= '\u4E00' && ch <= '\u9FFF') ||
		(ch >= '\u3400' && ch <= '\u4DBF') ||
		(ch >= '\u3040' && ch <= '\u30FF') ||
		(ch >= '\uFF66' && ch <= '\uFF9D') ||
		(ch >= '\uF900' && ch <= '\uFAFF');

	static bool ishangul(char ch) =>
		(ch >= '\uAC00' && ch <= '\uD7AF') ||
		(ch >= '\u1100' && ch <= '\u11FF') ||
		(ch >= '\u3130' && ch <= '\u318F');

	static bool iswordch(char ch) => !iscjk(ch) && char.IsLetterOrDigit(ch);

	static bool nospcbefore(char ch) =>
		ch is ',' or '.' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '%' or '\'' or '"';

	static bool nospcafter(char ch) =>
		ch is '(' or '[' or '{' or '"' or '\'';

	/// <summary>列对比度低的宽谷 = 词间空白（深底白字、白底黑字都适用）。</summary>
	static List<(int x0, int x1)> findgaps(Mat bgr) {
		var h = bgr.Rows;
		var w = bgr.Cols;
		if (h < 2 || w < 4) return null;
		using var gray = new Mat();
		if (bgr.Channels() == 3)
			Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
		else if (bgr.Channels() == 1)
			bgr.CopyTo(gray);
		else
			return null;

		Mat cont = gray;
		Mat owned = null;
		if (!gray.IsContinuous()) {
			owned = gray.Clone();
			cont = owned;
		}
		var contrast = new int[w];
		try {
			unsafe {
				var p = (byte*)cont.Data;
				var step = (int)cont.Step();
				for (int x = 0; x < w; x++) {
					int mn = 255, mx = 0;
					for (int y = 0; y < h; y++) {
						var v = p[y * step + x];
						if (v < mn) mn = v;
						if (v > mx) mx = v;
					}
					contrast[x] = mx - mn;
				}
			}
		}
		finally {
			owned?.Dispose();
		}

		var maxC = 0;
		for (int x = 0; x < w; x++)
			if (contrast[x] > maxC) maxC = contrast[x];
		if (maxC < 10) return null;
		var th = Math.Max(12, maxC / 4);
		var minW = Math.Max(3, (int)Math.Round(h * 0.08));
		var list = new List<(int x0, int x1)>(8);
		var run = -1;
		for (int x = 0; x <= w; x++) {
			var empty = x < w && contrast[x] < th;
			if (empty) {
				if (run < 0) run = x;
			}
			else if (run >= 0) {
				if (x - run >= minW)
					list.Add((run, x - 1));
				run = -1;
			}
		}
		return list.Count == 0 ? null : list;
	}

	static bool hasgapbetween(List<(int x0, int x1)> gaps, float xa, float xb, float minW) {
		if (gaps == null || gaps.Count == 0) return false;
		if (xb - xa < minW * 0.8f) return false;
		foreach (var g in gaps) {
			var gw = g.x1 - g.x0 + 1;
			if (gw < minW) continue;
			var gx = (g.x0 + g.x1) * 0.5f;
			if (gx >= xa && gx <= xb) return true;
		}
		return false;
	}

	// ───────── image helpers ─────────

	static (Mat resized, float ratioH, float ratioW) resizelimit(Mat src, int limitSide, int stride) {
		var h = src.Rows;
		var w = src.Cols;
		float ratio = 1f;
		var maxSide = Math.Max(h, w);
		if (maxSide > limitSide)
			ratio = (float)limitSide / maxSide;
		var rh = (int)Math.Round(h * ratio);
		var rw = (int)Math.Round(w * ratio);
		rh = Math.Max(stride, (rh + stride - 1) / stride * stride);
		rw = Math.Max(stride, (rw + stride - 1) / stride * stride);
		var dst = new Mat();
		Cv2.Resize(src, dst, new OpenCvSharp.Size(rw, rh));
		return (dst, (float)rh / h, (float)rw / w);
	}

	static Mat resizefill(Mat src, int dstW, int dstH) {
		var ratio = Math.Min((float)dstW / src.Cols, (float)dstH / src.Rows);
		var nw = Math.Max(1, (int)Math.Round(src.Cols * ratio));
		var nh = Math.Max(1, (int)Math.Round(src.Rows * ratio));
		using var tmp = new Mat();
		Cv2.Resize(src, tmp, new OpenCvSharp.Size(nw, nh));
		var dst = new Mat(dstH, dstW, src.Type(), Scalar.All(0));
		tmp.CopyTo(new Mat(dst, new OpenCvSharp.Rect(0, 0, nw, nh)));
		return dst;
	}

	/// <summary>mean/std=0.5 的 CHW 张量（单图）。(v/255-0.5)/0.5 = v*(2/255)-1</summary>
	static DenseTensor<float> tonchw05(Mat bgr) {
		var h = bgr.Rows;
		var w = bgr.Cols;
		var buf = new float[3 * h * w];
		tonchw05into(bgr, buf, 0, h, w);
		return new DenseTensor<float>(buf, new[] { 1, 3, h, w });
	}

	/// <summary>
	/// BGR → RGB CHW 归一化写入 batch 缓冲。
	/// 布局 batch[bi, c, y, x]，stride 按 plane=h*dstW；源宽 srcW≤dstW，右侧不写（保持 0）。
	/// </summary>
	static void tonchw05into(Mat bgr, float[] batch, int bi, int h, int dstW) {
		var srcW = bgr.Cols;
		if (bgr.Rows != h)
			throw new ArgumentException("高度不一致");
		// 保证连续 BGR
		Mat cont = bgr;
		Mat owned = null;
		if (!bgr.IsContinuous() || bgr.Type() != MatType.CV_8UC3) {
			owned = new Mat();
			bgr.ConvertTo(owned, MatType.CV_8UC3);
			if (!owned.IsContinuous()) {
				var tmp = owned.Clone();
				owned.Dispose();
				owned = tmp;
			}
			cont = owned;
		}
		try {
			const float k = 2f / 255f;
			var plane = h * dstW;
			var baseOff = bi * 3 * plane;
			unsafe {
				var p = (byte*)cont.Data;
				var step = (int)cont.Step();
				fixed (float* dst = batch) {
					var d0 = dst + baseOff;
					var d1 = d0 + plane;
					var d2 = d0 + plane * 2;
					for (int y = 0; y < h; y++) {
						var row = p + y * step;
						var o = y * dstW;
						for (int x = 0; x < srcW; x++) {
							var b = row[x * 3 + 0];
							var g = row[x * 3 + 1];
							var r = row[x * 3 + 2];
							// RGB 顺序写入 CHW
							d0[o + x] = r * k - 1f;
							d1[o + x] = g * k - 1f;
							d2[o + x] = b * k - 1f;
						}
					}
				}
			}
		}
		finally {
			owned?.Dispose();
		}
	}

	// ───────── session / model ─────────

	static (string packDir, ModelVariant variant, string label) resolvevariant(OcrOptions opt) {
		ModelPack pack = null;
		if (!string.IsNullOrWhiteSpace(opt.ModelsDir) && Directory.Exists(opt.ModelsDir))
			pack = ModelCatalog.TryLoad(opt.ModelsDir);
		pack ??= ModelCatalog.Find(opt.ModelPackId);
		if (pack == null) {
			var root = ModelCatalog.ModelsRoot();
			var exists = Directory.Exists(root);
			var hint = exists
				? "目录存在但无有效模型（需含 onnx + configs.txt 或可识别文件名）"
				: "目录不存在";
			throw new DirectoryNotFoundException(
				$"找不到可用模型包（{hint}）\n请把模型放到：\n{root}\\umi 或 {root}\\rapid-ch");
		}

		var variant = pack.FindVariant(opt.ModelVariant);
		if (variant == null)
			throw new InvalidOperationException($"模型包 {pack.Id} 无可用变体");

		// 回写规范化选项，便于 UI/日志
		opt.ModelPackId = pack.Id;
		opt.ModelVariant = variant.Title;
		opt.ModelsDir = pack.Dir;
		var label = $"{pack.DisplayName} · {variant.Title}";
		return (pack.Dir, variant, label);
	}

	static (InferenceSession det, InferenceSession cls, InferenceSession rec, string device) createsessions(
		string detPath, string clsPath, string recPath, OcrDevice prefer) {
		// 明确 CPU
		if (prefer == OcrDevice.Cpu)
			return makecpu(detPath, clsPath, recPath);

		// 明确核显 DirectML；未安装则 CPU
		if (prefer == OcrDevice.IntelGpu) {
			if (!CudaBootstrap.IsDmlReady)
				return makecpu(detPath, clsPath, recPath);
			return tryep(detPath, clsPath, recPath, "dml", "intel-dml",
				() => CudaBootstrap.MarkDmlFailed("建会话失败"));
		}

		// 明确 NVIDIA CUDA；未安装则 CPU
		if (prefer == OcrDevice.Gpu) {
			if (!CudaBootstrap.IsGpuReady)
				return makecpu(detPath, clsPath, recPath);
			return tryep(detPath, clsPath, recPath, "cuda", "gpu",
				() => CudaBootstrap.MarkGpuFailed("建会话失败"));
		}

		return makecpu(detPath, clsPath, recPath);
	}

	/// <param name="fallbackCpu">失败时是否回退 CPU。</param>
	/// <returns>device 为 null 表示失败且未回退。</returns>
	static (InferenceSession det, InferenceSession cls, InferenceSession rec, string device) tryep(
		string detPath, string clsPath, string recPath,
		string ep, string deviceLabel, Action onFail, bool fallbackCpu = true) {
		InferenceSession gd = null, gc = null, gr = null;
		try {
			if (ep == "cuda") {
				try { CudaBootstrap.EnsureGpuLibsLoaded(); } catch { }
				if (!CudaBootstrap.IsGpuReady)
					throw new InvalidOperationException("CUDA 不可用");
				CudaBootstrap.EnsureOrtForDevice(OcrDevice.Gpu);
			}
			else if (ep == "dml") {
				if (!CudaBootstrap.IsDmlReady)
					throw new InvalidOperationException("DirectML 不可用");
				CudaBootstrap.EnsureOrtForDevice(OcrDevice.IntelGpu);
			}

			gd = makesession(detPath, ep);
			gc = makesession(clsPath, ep);
			gr = makesession(recPath, ep);
			return (gd, gc, gr, deviceLabel);
		}
		catch (Exception ex) {
			try { gd?.Dispose(); } catch { }
			try { gc?.Dispose(); } catch { }
			try { gr?.Dispose(); } catch { }
			// 后端互斥（需重启）不要把 DML/CUDA 标成永久不可用
			var switchLocked = Compat.Contains(ex.Message ?? "", "无法切换", StringComparison.Ordinal);
			if (!switchLocked) {
				try { onFail?.Invoke(); } catch { }
			}
			if (!fallbackCpu) return (null, null, null, null);
			try {
				return makecpu(detPath, clsPath, recPath);
			}
			catch (Exception ex2) {
				throw new InvalidOperationException(
					$"{deviceLabel} 失败且 CPU 回退也失败。加速: {ex.Message}; CPU: {ex2.Message}", ex2);
			}
		}
	}

	static (InferenceSession det, InferenceSession cls, InferenceSession rec, string device) makecpu(
		string detPath, string clsPath, string recPath) {
		// 必须先用绝对路径加载真实 ORT；失败时不要吞异常去建 SessionOptions，
		// 否则 DllImport 会命中 System32 旧 stub → OrtGetApiBase EntryPointNotFound。
		try {
			CudaBootstrap.EnsureOrtForDevice(OcrDevice.Cpu);
		}
		catch (Exception ex) {
			throw new InvalidOperationException(
				"无法加载 ONNX Runtime（CPU）。请确认程序目录有 onnxcpu64（随编译附带），" +
				"或已安装 GPU/核显组件。勿使用 System32 下旧 stub。" +
				" 详情: " + ex.Message, ex);
		}
		if (!CudaBootstrap.IsOrtReady)
			throw new InvalidOperationException(
				"ONNX Runtime 未就绪：缺少 onnxcpu64/onnxgpu64/onnxdml64 中的 onnxruntime.dll。");
		return (makesession(detPath, "cpu"), makesession(clsPath, "cpu"), makesession(recPath, "cpu"), "cpu");
	}

	/// <param name="ep">cpu | cuda | dml</param>
	static InferenceSession makesession(string modelPath, string ep) {
		var so = new SessionOptions();
		// 速度优先：完整图优化 + CPU arena
		so.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
		so.EnableMemoryPattern = true;
		so.EnableCpuMemArena = true;
		// det/cls/rec 串行用 session，Intra 用满核；Inter 保持 1 避免多余并行调度
		var threads = Math.Max(1, Environment.ProcessorCount);
		so.IntraOpNumThreads = threads;
		so.InterOpNumThreads = 1;
		if (ep == "cuda") {
			try {
				so.AppendExecutionProvider_CUDA(0);
			}
			catch (Exception ex) {
				so.Dispose();
				throw new InvalidOperationException($"Append CUDA EP 失败: {ex.Message}", ex);
			}
		}
		else if (ep == "dml") {
			try {
				// Intel 核显 / AMD / 部分 NVIDIA 均可走 DirectML（需 DML 版 ORT）
				so.AppendExecutionProvider_DML(0);
			}
			catch (Exception ex) {
				so.Dispose();
				throw new InvalidOperationException($"Append DirectML EP 失败: {ex.Message}", ex);
			}
		}
		try {
			return new InferenceSession(modelPath, so);
		}
		catch {
			try { so.Dispose(); } catch { }
			throw;
		}
	}

	static string[] loadkeys(string path) {
		// 每行一字；末行空格是 PP-OCR 的 space 字符，不能 Trim
		var bytes = File.ReadAllBytes(path);
		var text = Encoding.UTF8.GetString(bytes);
		if (text.StartsWith("\uFEFF")) text = text.Substring(1);
		text = text.Replace("\r\n", "\n").Replace('\r', '\n');
		if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
		var lines = text.Split('\n');
		if (lines.Length < 10) {
			text = Encoding.GetEncoding(936).GetString(bytes);
			if (text.StartsWith("\uFEFF")) text = text.Substring(1);
			text = text.Replace("\r\n", "\n").Replace('\r', '\n');
			if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
			lines = text.Split('\n');
		}
		return lines;
	}
}
