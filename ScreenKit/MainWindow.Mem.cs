namespace ScreenKit;

public partial class MainWindow {
	System.Windows.Threading.DispatcherTimer stbarTimer;

	void initstbar() {
		bstbar.Click += (_, _) => openmem();
		refreshstbar();
		stbarTimer = new System.Windows.Threading.DispatcherTimer {
			Interval = TimeSpan.FromSeconds(2),
		};
		stbarTimer.Tick += (_, _) => refreshstbar();
		stbarTimer.Start();
	}

	void refreshstbar() {
		if (bstbar == null) return;
		try {
			var text = statusbartext();
			bstbar.Content = text;
			bstbar.ToolTip = text + "\n" + Loc.T("stbar.tip");
		}
		catch { }
	}

	string statusbartext() {
		var snap = MemSnapNow();
		var parts = new List<string>();
		var flavor = flavortag(CudaBootstrap.LoadedOrtFlavor());
		parts.Add(flavor.Length == 0 ? Loc.T("stbar.ort.none") : Loc.T("stbar.ort", flavor));
		foreach (var it in snap.Items) {
			var eng = Loc.T("mem.eng." + (it.Engine ?? ""));
			parts.Add(Loc.T("stbar.item", eng, devtag(it.Device), FeatureInstaller.FormatBytes(it.Bytes)));
		}
		parts.Add(Loc.T("stbar.ws", FeatureInstaller.FormatBytes(snap.WorkingSet)));
		return string.Join("    ·    ", parts);
	}

	static string flavortag(string flavor) => flavor switch {
		"cuda" => "gpu",
		"dml" => "dml",
		"cpu" => "cpu",
		_ => "",
	};

	static string devtag(string device) {
		var d = (device ?? "").Trim();
		var l = d.ToLowerInvariant();
		if (l.Contains("dml") || l.Contains("directml") || d.Contains("核显")) return "dml";
		if (l.Contains("cuda") || l.Contains("gpu") || l.Contains("nvidia")) return "gpu";
		if (d.Length == 0 || l.Contains("cpu")) return "cpu";
		return d;
	}

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
		return snap;
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
		var r = unloadmem(id, trim: true);
		kickstbar();
		return r;
	}

	/// <summary>卸掉当前能卸的模型并强制回收。返回 busy 表示有一项正在使用、没有全卸。</summary>
	internal string UnloadAllMem() {
		var snap = MemSnapNow();
		var busy = false;
		foreach (var it in snap.Items) {
			if (unloadmem(it.Id, trim: false) == "busy") busy = true;
		}
		MemUsage.Trim();
		kickstbar();
		return busy ? "busy" : "";
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
