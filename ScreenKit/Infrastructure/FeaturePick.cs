using System.ComponentModel;

namespace ScreenKit;

/// <summary>安装功能「功能选择」树节点（产品功能 → 组件）。</summary>
sealed class FeaturePickNode : INotifyPropertyChanged {
	bool? check = false;
	bool silent;

	public string Id { get; set; }
	public string Title { get; set; }
	public string Detail { get; set; }
	public FeatureKind[] Kinds { get; set; }
	public string SizeLabel { get; set; }
	public List<FeaturePickNode> Children { get; } = new();
	public FeaturePickNode Parent { get; set; }
	public bool IsGroup => Children.Count > 0;

	/// <summary>相对已装：add 将安装（淡绿），del 将卸载（淡红），空为不变。</summary>
	public string Diff { get; private set; } = "";

	public override string ToString() => Title ?? Id ?? "";

	public bool? IsChecked {
		get => check;
		set => SetCheck(value, fromUi: true);
	}

	public event PropertyChangedEventHandler PropertyChanged;

	internal void SetCheck(bool? value, bool fromUi) {
		if (check == value) {
			refreshdiff();
			return;
		}
		check = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
		if (silent) {
			refreshdiff();
			return;
		}
		if (fromUi && value != null && Children.Count > 0) {
			silent = true;
			foreach (var c in Children)
				c.SetCheck(value, fromUi: true);
			silent = false;
		}
		refreshdiff();
		if (Parent != null && !Parent.silent)
			Parent.syncfromchildren();
	}

	internal void RefreshDiff() {
		refreshdiff();
		Parent?.RefreshDiff();
	}

	internal void RefreshDiffHere() => refreshdiff();

	void refreshdiff() {
		var next = diffof();
		if (Diff == next) return;
		Diff = next;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Diff)));
	}

	string diffof() {
		if (Children.Count > 0) {
			var add = false;
			var del = false;
			foreach (var c in Children) {
				if (c.Diff == "add") add = true;
				else if (c.Diff == "del") del = true;
			}
			if (add && !del) return "add";
			if (del && !add) return "del";
			return "";
		}
		if (Kinds == null || Kinds.Length == 0) return "";
		var have = FeatureInstaller.Probe(Kinds[Kinds.Length - 1]) != FeatureInstallState.Missing;
		if (check == true && !have) return "add";
		if (check != true && have) return "del";
		return "";
	}

	void syncfromchildren() {
		if (Children.Count == 0) return;
		var on = 0;
		var off = 0;
		foreach (var c in Children) {
			if (c.IsChecked == true) on++;
			else if (c.IsChecked == false) off++;
		}
		bool? v = on == Children.Count ? true : off == Children.Count ? false : (bool?)null;
		if (check != v) {
			silent = true;
			check = v;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
			silent = false;
		}
		refreshdiff();
		Parent?.syncfromchildren();
	}
}

/// <summary>功能选择树：用户勾选产品功能，确认后映射到 FeatureKind 组件。</summary>
static class FeaturePick {
	public static readonly string[] RecommendedIds = ["ocr.ch", "asr.sv", "asr.zip", "rec"];

	public static List<FeaturePickNode> BuildTree() => [
		group("ocr", "feat.pick.ocr", "feat.pick.ocr.detail",
			leaf("ocr.ch", "feat.pick.ocr.ch", "feat.OcrRapidCh.detail",
				FeatureKind.NativeOpenCv, FeatureKind.OrtCpu, FeatureKind.OcrRapidCh),
			leaf("ocr.umi", "feat.pick.ocr.umi", "feat.OcrUmi.detail",
				FeatureKind.NativeOpenCv, FeatureKind.OrtCpu, FeatureKind.OcrUmi),
			leaf("ocr.i18n", "feat.pick.ocr.i18n", "feat.OcrRapidI18n.detail",
				FeatureKind.NativeOpenCv, FeatureKind.OrtCpu, FeatureKind.OcrRapidI18n)),
		group("asr", "feat.pick.asr", "feat.pick.asr.detail",
			leaf("asr.sv", "feat.pick.asr.sv", "feat.AsrSenseVoice.detail",
				FeatureKind.NativeSherpa, FeatureKind.AsrSenseVoice),
			leaf("asr.zip", "feat.pick.asr.zip", "feat.AsrStreamZipformer.detail",
				FeatureKind.NativeSherpa, FeatureKind.AsrStreamZipformer),
			leaf("asr.wtiny", "feat.pick.asr.wtiny", "feat.AsrWhisperTiny.detail",
				FeatureKind.NativeSherpa, FeatureKind.AsrWhisperTiny),
			leaf("asr.wbase", "feat.pick.asr.wbase", "feat.AsrWhisperBase.detail",
				FeatureKind.NativeSherpa, FeatureKind.AsrWhisperBase),
			leaf("asr.vad", "feat.pick.asr.vad", "feat.AsrSileroVad.detail",
				FeatureKind.NativeSherpa, FeatureKind.AsrSileroVad)),
		leaf("face", "feat.pick.face", "feat.pick.face.detail", FeatureKind.FaceInsight),
		leaf("pdf", "feat.pick.pdf", "feat.pick.pdf.detail",
			FeatureKind.NativeSkia, FeatureKind.NativePdfium),
		leaf("rec", "feat.pick.rec", "feat.pick.rec.detail", FeatureKind.Ffmpeg),
		group("accel", "feat.pick.accel", "feat.pick.accel.detail",
			leaf("accel.cuda", "feat.pick.accel.cuda", "feat.CudaGpu.detail", FeatureKind.CudaGpu),
			leaf("accel.dml", "feat.pick.accel.dml", "feat.DirectMl.detail", FeatureKind.DirectMl),
			leaf("accel.cpu", "feat.pick.accel.cpu", "feat.OrtCpu.detail", FeatureKind.OrtCpu)),
	];

	public static IEnumerable<FeaturePickNode> Leaves(IEnumerable<FeaturePickNode> roots) {
		foreach (var n in roots ?? []) {
			if (n.Children.Count == 0) yield return n;
			else {
				foreach (var x in Leaves(n.Children))
					yield return x;
			}
		}
	}

	public static void ApplyIds(IEnumerable<FeaturePickNode> roots, IEnumerable<string> ids) {
		var set = new HashSet<string>(ids ?? [], StringComparer.Ordinal);
		foreach (var n in Leaves(roots))
			n.SetCheck(set.Contains(n.Id), fromUi: true);
	}

	public static void ApplyKinds(IEnumerable<FeaturePickNode> roots, FeatureKind[] kinds) {
		var set = kinds == null || kinds.Length == 0
			? new HashSet<FeatureKind>()
			: new HashSet<FeatureKind>(kinds);
		foreach (var n in Leaves(roots)) {
			if (n.Kinds == null || n.Kinds.Length == 0) {
				n.SetCheck(false, fromUi: true);
				continue;
			}
			n.SetCheck(set.Contains(n.Kinds[n.Kinds.Length - 1]), fromUi: true);
		}
	}

	/// <summary>勾选主组件已装/部分安装的功能；可选再并上推荐 id 或指定组件。</summary>
	public static void ApplyInstalled(IEnumerable<FeaturePickNode> roots,
		IEnumerable<string> extraIds = null, FeatureKind[] extraKinds = null) {
		var ids = extraIds == null
			? new HashSet<string>(StringComparer.Ordinal)
			: new HashSet<string>(extraIds, StringComparer.Ordinal);
		var kinds = extraKinds == null || extraKinds.Length == 0
			? new HashSet<FeatureKind>()
			: new HashSet<FeatureKind>(extraKinds);
		foreach (var n in Leaves(roots)) {
			if (n.Kinds == null || n.Kinds.Length == 0) {
				n.SetCheck(ids.Contains(n.Id), fromUi: true);
				continue;
			}
			var primary = n.Kinds[n.Kinds.Length - 1];
			var have = FeatureInstaller.Probe(primary) != FeatureInstallState.Missing;
			var want = have || ids.Contains(n.Id) || kinds.Contains(primary);
			if (!want) {
				foreach (var k in n.Kinds) {
					if (!kinds.Contains(k)) continue;
					if (RecommendedIds.Contains(n.Id)) { want = true; break; }
				}
			}
			n.SetCheck(want, fromUi: true);
		}
	}

	public static void CollectDelta(IEnumerable<FeaturePickNode> roots, out HashSet<FeatureKind> add, out HashSet<FeatureKind> del) {
		var sel = new HashSet<FeatureKind>();
		CollectKinds(roots, sel);
		add = new HashSet<FeatureKind>();
		del = new HashSet<FeatureKind>();
		foreach (FeatureKind k in Enum.GetValues(typeof(FeatureKind))) {
			var st = FeatureInstaller.Probe(k);
			var on = sel.Contains(k);
			if (on && st != FeatureInstallState.Installed) add.Add(k);
			else if (!on && st != FeatureInstallState.Missing) del.Add(k);
		}
	}

	/// <summary>相对当前磁盘：将安装的组件 / 将卸载的组件。</summary>
	public static void DiffSelection(IEnumerable<FeaturePickNode> roots,
		out int addN, out long addSz, out int delN, out long delSz) {
		var sel = new HashSet<FeatureKind>();
		CollectKinds(roots, sel);
		addN = 0;
		addSz = 0;
		delN = 0;
		delSz = 0;
		foreach (FeatureKind k in Enum.GetValues(typeof(FeatureKind))) {
			var st = FeatureInstaller.Probe(k);
			var on = sel.Contains(k);
			if (on && st != FeatureInstallState.Installed) {
				addN++;
				addSz += FeatureInstaller.ExpectedSize(k);
			}
			else if (!on && st != FeatureInstallState.Missing) {
				delN++;
				delSz += FeatureInstaller.ExpectedSize(k);
			}
		}
	}

	public static void SelectMissing(IEnumerable<FeaturePickNode> roots) {
		foreach (var n in Leaves(roots)) {
			var need = n.Kinds != null && n.Kinds.Any(k =>
				FeatureInstaller.Probe(k) != FeatureInstallState.Installed);
			n.SetCheck(need, fromUi: true);
		}
	}

	public static void SelectAll(IEnumerable<FeaturePickNode> roots, bool on) {
		foreach (var n in Leaves(roots))
			n.SetCheck(on, fromUi: true);
	}

	public static void RefreshDiff(IEnumerable<FeaturePickNode> roots) {
		foreach (var n in roots ?? []) {
			RefreshDiff(n.Children);
			n.RefreshDiffHere();
		}
	}

	public static void CollectKinds(IEnumerable<FeaturePickNode> roots, HashSet<FeatureKind> set) {
		if (set == null) return;
		foreach (var n in Leaves(roots)) {
			if (n.IsChecked != true || n.Kinds == null) continue;
			foreach (var k in n.Kinds)
				set.Add(k);
		}
	}

	public static void MeasureSelection(IEnumerable<FeaturePickNode> roots, out long total, out long need) {
		var set = new HashSet<FeatureKind>();
		CollectKinds(roots, set);
		MeasureKinds(set, out total, out need);
	}

	public static void MeasureItems(IEnumerable<FeatureItem> items, out long total, out long need) {
		total = 0;
		need = 0;
		if (items == null) return;
		foreach (var it in items) {
			if (it == null || !it.Selected) continue;
			var sz = FeatureInstaller.ExpectedSize(it.Kind);
			total += sz;
			if (it.State != FeatureInstallState.Installed)
				need += sz;
		}
	}

	public static void MeasureKinds(IEnumerable<FeatureKind> kinds, out long total, out long need) {
		total = 0;
		need = 0;
		if (kinds == null) return;
		foreach (var k in kinds) {
			var sz = FeatureInstaller.ExpectedSize(k);
			total += sz;
			if (FeatureInstaller.Probe(k) != FeatureInstallState.Installed)
				need += sz;
		}
	}

	static FeaturePickNode group(string id, string titleKey, string detailKey, params FeaturePickNode[] kids) {
		var n = new FeaturePickNode {
			Id = id,
			Title = Loc.T(titleKey),
			Detail = Loc.T(detailKey),
			SizeLabel = "",
		};
		foreach (var c in kids) {
			c.Parent = n;
			n.Children.Add(c);
		}
		return n;
	}

	static FeaturePickNode leaf(string id, string titleKey, string detailKey, params FeatureKind[] kinds) {
		long sz = 0;
		var seen = new HashSet<FeatureKind>();
		foreach (var k in kinds) {
			if (!seen.Add(k)) continue;
			sz += FeatureInstaller.ExpectedSize(k);
		}
		return new FeaturePickNode {
			Id = id,
			Title = Loc.T(titleKey),
			Detail = Loc.T(detailKey),
			Kinds = kinds,
			SizeLabel = sz > 0 ? Loc.T("feat.size.about", FeatureInstaller.FormatBytes(sz)) : "",
		};
	}
}
