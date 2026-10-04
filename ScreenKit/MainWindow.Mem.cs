namespace ScreenKit;

public partial class MainWindow {
	System.Windows.Threading.DispatcherTimer stbarTimer;
	static string ortFlavorCached = "";
	static long ortBytesCached;
	static int ortBytesAt;
	static bool ortBytesOk;

	void initstbar() {
		// 系统 ToolTip 会吃掉第一次点击。按下即打开内存窗口。
		bstbar.ToolTip = null;
		ToolTipService.SetIsEnabled(bstbar, false);
		bstbar.Click += (_, _) => openmem();
		bstbar.PreviewMouseLeftButtonDown += (_, e) => {
			openmem();
			e.Handled = true;
		};
		refreshstbar();
		stbarTimer = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromSeconds(2),
		};
		stbarTimer.Tick += (_, _) => refreshstbar();
		stbarTimer.Start();
	}

	void refreshstbar() {
		if (bstbar == null) return;
		try { bstbar.Content = statusbartext(); }
		catch { }
	}

	string statusbartext() {
		var snap = MemSnapNow();
		var flavor = flavorname(CudaBootstrap.LoadedOrtFlavor());
		var models = 0;
		long sum = 0;
		foreach (var it in snap.Items) {
			sum += it.Bytes;
			if (it.Id != "ort") models++;
		}
		var size = stbarsiz(sum);
		if (flavor.Length == 0 && models == 0) return Loc.T("stbar.none");
		if (flavor.Length == 0) return Loc.T("stbar.models", models, size);
		if (models == 0) return Loc.T("stbar.ortonly", flavor, size);
		return Loc.T("stbar.sum", flavor, models, size);
	}

	static string flavorname(string flavor) => flavor switch {
		"cuda" => "GPU",
		"dml" => "DML",
		"cpu" => "CPU",
		_ => "",
	};

	static string stbarsiz(long bytes) {
		if (bytes < 0) bytes = 0;
		const long KB = 1024;
		const long MB = 1024 * 1024;
		const long GB = 1024L * 1024 * 1024;
		if (bytes >= GB) {
			var hundredths = (bytes * 100 + GB / 2) / GB;
			return (hundredths / 100) + "." + (hundredths % 100).ToString("00") + "GB";
		}
		if (bytes >= MB) {
			var tenths = (bytes * 10 + MB / 2) / MB;
			var whole = tenths / 10;
			var frac = tenths % 10;
			if (whole >= 100 || frac == 0) return whole + "MB";
			return whole + "." + frac + "MB";
		}
		if (bytes >= KB) return ((bytes + KB / 2) / KB) + "KB";
		return bytes + "B";
	}

	static string flavortag(string flavor) => flavor switch {
		"cuda" => "gpu",
		"dml" => "dml",
		"cpu" => "cpu",
		_ => "",
	};

	internal MemSnap MemSnapNow() {
		var snap = MemUsage.Read();
		var items = snap.Items;
		try {
			if (runner.TryMem(out var name, out var dev, out var bytes))
				items.Add(hold("ocr", "ocr", name, dev, bytes));
		}
		catch { }
		try { trEngine?.CopyMem(items); } catch { }
		try {
			if (asrEngine != null && asrEngine.TryMem(out var name, out var dev, out var bytes))
				items.Add(hold("asr", "asr", name, dev, bytes));
		}
		catch { }
		try {
			if (asrStreamEngine != null && asrStreamEngine.TryMem(out var name, out var dev, out var bytes))
				items.Add(hold("asrstream", "asrstream", name, dev, bytes));
		}
		catch { }
		try {
			if (sherpaTts != null && sherpaTts.TryMem(out var name, out var dev, out var bytes))
				items.Add(hold("tts", "tts", name, dev, bytes));
		}
		catch { }
		try {
			if (DictTts.TryOnnxMem(out var name, out var dev, out var bytes))
				items.Add(hold("dicttts", "dict", name, dev, bytes));
		}
		catch { }
		try {
			if (facePipe != null && facePipe.IsLive)
				items.Add(hold("face", "face", facePipe.MemName, facePipe.EpLabel, facePipe.WeightBytes));
		}
		catch { }
		try {
			if (faceLmk != null && faceLmk.IsLive)
				items.Add(hold("lmk", "lmk", faceLmk.MemName, faceLmk.EpLabel, faceLmk.WeightBytes));
		}
		catch { }
		try {
			if (faceAttr != null && faceAttr.IsLive)
				items.Add(hold("attr", "attr", faceAttr.MemName, faceAttr.EpLabel, faceAttr.WeightBytes));
		}
		catch { }
		try { httpServer?.CopyFaceMem(items); } catch { }
		addortruntime(items);
		return snap;
	}

	static void addortruntime(List<MemHold> items) {
		var flavor = CudaBootstrap.LoadedOrtFlavor();
		if (string.IsNullOrEmpty(flavor)) return;
		var name = flavor == "dml" ? "DirectML" : flavor == "cpu" ? "CPU" : "CUDA";
		items.Insert(0, hold("ort", "ort", name, flavortag(flavor), ortpackagesize(flavor)));
	}

	// 状态栏每 2 秒读一次。CUDA 目录约 1.5 GB，量体积做 30 秒缓存。
	static long ortpackagesize(string flavor) {
		var now = Environment.TickCount;
		if (ortBytesOk && flavor == ortFlavorCached && now - ortBytesAt < 30000)
			return ortBytesCached;
		var kind = flavor == "dml" ? FeatureKind.DirectMl
			: flavor == "cpu" ? FeatureKind.OrtCpu
			: FeatureKind.CudaGpu;
		var info = new FeatureItem { Kind = kind };
		FeatureInstaller.RefreshState(info);
		ortFlavorCached = flavor;
		ortBytesCached = info.SizeBytes;
		ortBytesAt = now;
		ortBytesOk = true;
		return ortBytesCached;
	}

	static MemHold hold(string id, string engine, string name, string device, long bytes) => new() {
		Id = id,
		Engine = engine,
		Name = name ?? "",
		Device = device ?? "",
		Bytes = bytes,
	};

	/// <summary>空字符串表示已卸。返回 busy 表示这项正在使用。</summary>
	internal string UnloadMem(string id) {
		CudaBootstrap.HoldNative(8000);
		string r;
		if (id == "ort")
			r = unloadort();
		else
			r = unloadmem(id, trim: true);
		kickstbar();
		return r;
	}

	string unloadort() {
		var snap = MemSnapNow();
		var busy = false;
		foreach (var it in snap.Items) {
			if (it.Id == "ort") continue;
			if (unloadmem(it.Id, trim: false) == "busy") busy = true;
		}
		MemUsage.Trim();
		if (busy) return "busy";
		if (!string.IsNullOrEmpty(CudaBootstrap.LoadedOrtFlavor())) return "resident";
		return "";
	}

	string unloadmem(string id, bool trim) {
		id ??= "";
		if ((id == "asr" || id == "asrstream") && asrLiveOn) return "busy";
		if (id == "ocr")
			runner.UnloadNow();
		else if (id == "asr")
			try { asrEngine?.UnloadSafe(); } catch { }
		else if (id == "asrstream")
			try { asrStreamEngine?.UnloadSafe(); } catch { }
		else if (id == "tts")
			try { sherpaTts?.UnloadSafe(); } catch { }
		else if (id == "dicttts")
			DictTts.UnloadOnnx();
		else if (id == "face") {
			if (facePipe != null && !facePipe.TryRelease()) return "busy";
			facePipe = null;
		}
		else if (id == "lmk") {
			if (faceLmk != null && !faceLmk.TryRelease()) return "busy";
			faceLmk = null;
			faceLmkName = "";
		}
		else if (id == "attr") {
			if (faceAttr != null && !faceAttr.TryRelease()) return "busy";
			faceAttr = null;
			faceAttrName = "";
		}
		else if (id.StartsWith("tr:"))
			try { trEngine?.UnloadOne(id.Substring(3)); } catch { }
		else if (id == "httpface" || id == "httpattr") {
			if (httpServer != null && !httpServer.UnloadFaceMem(id)) return "busy";
		}
		if (trim) MemUsage.Trim();
		return "";
	}

	void kickstbar() {
		try { Dispatcher.BeginInvoke(new Action(refreshstbar)); }
		catch { }
	}
}
