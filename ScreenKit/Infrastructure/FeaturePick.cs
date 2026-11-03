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
	/// <summary>这一行代表的组件。整包行没有主组件，装过其中任意一个就算已装。</summary>
	public FeatureKind? Primary { get; set; }
	public string SizeLabel { get; set; }
	/// <summary>系统自带，勾选锁住，不参与安装或卸载。</summary>
	public bool Locked { get; set; }
	/// <summary>
	/// 一行代表整套组件（数据库驱动）：装过其中任意一个就算已装，勾选时装齐全部。
	/// 普通叶子只看 <see cref="Primary"/>。同行带上的依赖无论排在前还是后，都不决定这一行算不算已装。
	/// 部分安装（缺文件）不算已装：勾选时标成将安装，与底部新增计数同一套判定。
	/// </summary>
	public bool WholePack { get; set; }
	/// <summary>取消勾选已装功能时不记为卸载（首次启动向导）。</summary>
	public bool KeepInstalled { get; set; }
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
		if (Locked) value = true;
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
		// 主组件决定这一行算不算已装。同行带上的依赖（Sherpa、解压库、文本归一化）若还没装，
		// 勾着也要标绿：确认时会一起下载，不能只出现在底部计数里。
		var primary = WholePack
			? Kinds.Any(FeaturePick.Installed)
			: Primary is FeatureKind pk && FeaturePick.Installed(pk);
		var pending = check == true && Kinds.Any(k => !FeaturePick.Installed(k));
		if (check == true && (!primary || pending)) return "add";
		if (check != true && primary && !KeepInstalled) return "del";
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
	public static readonly string[] RecommendedIds = ["ocr.ch", "asr.sv", "asr.zip", "rec.ffmpeg"];

	/// <summary>装完整才算已有。测试可替换，避免探测本机磁盘。</summary>
	internal static Func<FeatureKind, bool> Installed = k => FeatureInstaller.Has(k);

	public static List<FeaturePickNode> BuildTree() {
		var roots = buildtree();
		assignsizes(roots);
		return roots;
	}

	static List<FeaturePickNode> buildtree() => [
		group("ocr", "feat.pick.ocr", "feat.pick.ocr.detail",
			withprimary(leaf("ocr.ch", "feat.pick.ocr.ch", "feat.OcrRapidCh.detail",
				FeatureKind.NativeOpenCv, FeatureKind.OrtCpu, FeatureKind.OcrRapidCh),
				FeatureKind.OcrRapidCh),
			withprimary(leaf("ocr.umi", "feat.pick.ocr.umi", "feat.OcrUmi.detail",
				FeatureKind.NativeOpenCv, FeatureKind.OrtCpu, FeatureKind.OcrUmi),
				FeatureKind.OcrUmi),
			withprimary(leaf("ocr.i18n", "feat.pick.ocr.i18n", "feat.OcrRapidI18n.detail",
				FeatureKind.NativeOpenCv, FeatureKind.OrtCpu, FeatureKind.OcrRapidI18n),
				FeatureKind.OcrRapidI18n)),
		group("asr", "feat.pick.asr", "feat.pick.asr.detail",
			withprimary(leaf("asr.sv", "feat.pick.asr.sv", "feat.AsrSenseVoice.detail",
				FeatureKind.NativeSherpa, FeatureKind.SharpCompress, FeatureKind.AsrSenseVoice,
				FeatureKind.WetextItn), FeatureKind.AsrSenseVoice),
			withprimary(leaf("asr.zip", "feat.pick.asr.zip", "feat.AsrStreamZipformer.detail",
				FeatureKind.NativeSherpa, FeatureKind.SharpCompress, FeatureKind.AsrStreamZipformer,
				FeatureKind.WetextItn), FeatureKind.AsrStreamZipformer),
			withprimary(leaf("asr.wtiny", "feat.pick.asr.wtiny", "feat.AsrWhisperTiny.detail",
				FeatureKind.NativeSherpa, FeatureKind.SharpCompress, FeatureKind.AsrWhisperTiny,
				FeatureKind.WetextItn), FeatureKind.AsrWhisperTiny),
			withprimary(leaf("asr.wbase", "feat.pick.asr.wbase", "feat.AsrWhisperBase.detail",
				FeatureKind.NativeSherpa, FeatureKind.SharpCompress, FeatureKind.AsrWhisperBase,
				FeatureKind.WetextItn), FeatureKind.AsrWhisperBase),
			withprimary(leaf("asr.vad", "feat.pick.asr.vad", "feat.AsrSileroVad.detail",
				FeatureKind.NativeSherpa, FeatureKind.SharpCompress, FeatureKind.AsrSileroVad),
				FeatureKind.AsrSileroVad)),
		withprimary(leaf("translate", "feat.pick.translate", "feat.pick.translate.detail",
			FeatureKind.OrtCpu, FeatureKind.Native7za, FeatureKind.TranslateOnnx),
			FeatureKind.TranslateOnnx),
		leaf("face", "feat.pick.face", "feat.pick.face.detail", FeatureKind.FaceInsight),
		withprimary(leaf("dict", "feat.pick.dict", "feat.pick.dict.detail",
			FeatureKind.Native7za, FeatureKind.DictDb), FeatureKind.DictDb),
		withprimary(leaf("ime", "feat.pick.ime", "feat.pick.ime.detail",
			FeatureKind.NativeSqlite, FeatureKind.Native7za, FeatureKind.ImeDb), FeatureKind.ImeDb),
		withprimary(leaf("pdf", "feat.pick.pdf", "feat.pick.pdf.detail",
			FeatureKind.NativeSkia, FeatureKind.NativePdfium, FeatureKind.PdfManaged),
			FeatureKind.PdfManaged),
		leaf("zxing", "feat.pick.zxing", "feat.pick.zxing.detail", FeatureKind.NativeZxing),
		leaf("sharpcompress", "feat.pick.sharpcompress", "feat.pick.sharpcompress.detail",
			FeatureKind.SharpCompress),
		leaf("7za", "feat.pick.7za", "feat.pick.7za.detail", FeatureKind.Native7za),
		group("rec", "feat.pick.rec", "feat.pick.rec.detail",
			builtin("rec.mf", "feat.pick.rec.mf", "feat.pick.rec.mf.detail", FeatureKind.MediaFoundation),
			withprimary(leaf("rec.ffmpeg", "feat.pick.rec.ffmpeg", "feat.pick.rec.ffmpeg.detail",
				FeatureKind.Native7za, FeatureKind.Ffmpeg), FeatureKind.Ffmpeg)),
		group("accel", "feat.pick.accel", "feat.pick.accel.detail",
			leaf("accel.cuda", "feat.pick.accel.cuda", "feat.CudaGpu.detail", FeatureKind.CudaGpu),
			leaf("accel.dml", "feat.pick.accel.dml", "feat.DirectMl.detail", FeatureKind.DirectMl),
			leaf("accel.cpu", "feat.pick.accel.cpu", "feat.OrtCpu.detail", FeatureKind.OrtCpu)),
		// 工具集是杂项，排在最后
		group("tools", "feat.pick.tools", "feat.pick.tools.detail",
			leaf("bogus", "feat.pick.bogus", "feat.pick.bogus.detail", FeatureKind.Bogus),
			leaf("pinyin", "feat.pick.pinyin", "feat.pick.pinyin.detail", FeatureKind.NPinyin),
			// 文档阅读器只有一行：勾上就把 docx/xlsx 与 doc/xls 两套解析库一起装
			wholedocview(),
			// 数据库驱动只有一行：勾上就把 SQLite / MySQL / Oracle 一起装（SQL Server 用系统自带，不用装）
			wholedb()),
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

	/// <summary>首次启动：可选功能全部不勾。系统自带项仍锁住。已装内容不记为卸载。</summary>
	public static void ApplyNone(IEnumerable<FeaturePickNode> roots) {
		foreach (var n in Leaves(roots)) {
			n.KeepInstalled = true;
			n.SetCheck(false, fromUi: true);
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
			var primary = n.Primary;
			var have = n.WholePack
				? n.Kinds.Any(Installed)
				: primary is FeatureKind pk && Installed(pk);
			var want = have || ids.Contains(n.Id) || (primary is FeatureKind pk2 && kinds.Contains(pk2));
			if (!want) {
				foreach (var k in n.Kinds) {
					if (!kinds.Contains(k)) continue;
					if (RecommendedIds.Contains(n.Id)) { want = true; break; }
				}
			}
			n.SetCheck(want, fromUi: true);
		}
		SyncShared(roots);
	}

	/// <summary>
	/// 勾了 OCR / 翻译等功能时，把树里单独列出的依赖（如 ONNX CPU）一并勾上。
	/// 否则依赖装完后该项仍未勾，会被标成红色待删除。
	/// </summary>
	public static void SyncShared(IEnumerable<FeaturePickNode> roots) {
		var leaves = Leaves(roots).ToList();
		var shared = sharedset(leaves);
		if (shared.Count == 0) return;
		var implied = new HashSet<FeatureKind>();
		foreach (var n in leaves) {
			if (n.IsChecked != true || n.Kinds == null) continue;
			foreach (var k in n.Kinds)
				implied.Add(k);
		}
		foreach (var n in leaves) {
			if (n.Locked || n.Kinds == null || n.Kinds.Length != 1) continue;
			var k = n.Kinds[0];
			if (!shared.Contains(k) || !implied.Contains(k)) continue;
			if (n.IsChecked != true)
				n.SetCheck(true, fromUi: true);
		}
	}

	public static void CollectDelta(IEnumerable<FeaturePickNode> roots, out HashSet<FeatureKind> add, out HashSet<FeatureKind> del, bool keepInstalled = false) {
		var sel = new HashSet<FeatureKind>();
		CollectKinds(roots, sel);
		add = new HashSet<FeatureKind>();
		del = new HashSet<FeatureKind>();
		foreach (FeatureKind k in Enum.GetValues(typeof(FeatureKind))) {
			var on = sel.Contains(k);
			if (builtin(k)) continue;
			var have = Installed(k);
			if (on && !have) add.Add(k);
			else if (!keepInstalled && !on && have) del.Add(k);
		}
	}

	/// <summary>相对当前磁盘：将安装 / 将卸载的行数与体积。行数与树上的绿、红一致，体积按实际组件算。</summary>
	public static void DiffSelection(IEnumerable<FeaturePickNode> roots,
		out int addN, out long addSz, out int delN, out long delSz, bool keepInstalled = false) {
		CollectDelta(roots, out var add, out var del, keepInstalled);
		addN = 0;
		delN = 0;
		foreach (var n in Leaves(roots)) {
			if (n.Diff == "add") addN++;
			else if (n.Diff == "del") delN++;
		}
		addSz = 0;
		delSz = 0;
		foreach (var k in add) addSz += FeatureInstaller.ExpectedSize(k);
		foreach (var k in del) delSz += FeatureInstaller.ExpectedSize(k);
	}

	public static void SelectMissing(IEnumerable<FeaturePickNode> roots) {
		foreach (var n in Leaves(roots)) {
			var need = n.Kinds != null && n.Kinds.Any(k => !Installed(k));
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

	static FeaturePickNode wholedocview() {
		var n = leaf("docview", "feat.pick.docview", "feat.pick.docview.detail",
			FeatureKind.DocOfficeOoxml, FeatureKind.DocOfficeLegacy);
		n.WholePack = true;
		return n;
	}

	static FeaturePickNode wholedb() {
		var n = leaf("db", "feat.pick.db", "feat.pick.db.detail",
			FeatureKind.NativeSqlite, FeatureKind.DbMysql, FeatureKind.DbOracle);
		n.WholePack = true;
		return n;
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

	static bool builtin(FeatureKind k) =>
		k is FeatureKind.MediaFoundation or FeatureKind.Mjpeg or FeatureKind.DbMssql;

	static FeaturePickNode builtin(string id, string titleKey, string detailKey, FeatureKind kind) {
		var n = leaf(id, titleKey, detailKey, kind);
		n.SizeLabel = Loc.T("feat.size.builtin");
		n.Locked = true;
		return n;
	}

	static FeaturePickNode leaf(string id, string titleKey, string detailKey, params FeatureKind[] kinds) {
		return new FeaturePickNode {
			Id = id,
			Title = Loc.T(titleKey),
			Detail = Loc.T(detailKey),
			Kinds = kinds,
			// 只有一项时它就是主组件；多项时主组件由 withprimary 指定
			Primary = kinds != null && kinds.Length == 1 ? kinds[0] : (FeatureKind?)null,
		};
	}

	/// <summary>这一行代表 <paramref name="primary"/>，其余种类是它带上的依赖。</summary>
	static FeaturePickNode withprimary(FeaturePickNode n, FeatureKind primary) {
		n.Primary = primary;
		return n;
	}

	/// <summary>单独成行、又被别的功能当依赖带上的组件。体积只记在自己那一行，避免合计看起来漏了或重复。</summary>
	static HashSet<FeatureKind> sharedset(List<FeaturePickNode> leaves) {
		var sole = new HashSet<FeatureKind>();
		var dep = new HashSet<FeatureKind>();
		foreach (var n in leaves) {
			if (n.Kinds == null || n.Kinds.Length == 0) continue;
			if (n.Kinds.Length == 1) sole.Add(n.Kinds[0]);
			else if (n.Primary is FeatureKind pk) {
				foreach (var k in n.Kinds)
					if (k != pk) dep.Add(k);
			}
		}
		sole.IntersectWith(dep);
		return sole;
	}

	static void assignsizes(IEnumerable<FeaturePickNode> roots) {
		var leaves = Leaves(roots).ToList();
		var shared = sharedset(leaves);
		foreach (var n in leaves) {
			if (n.Locked || n.Kinds == null) continue;
			long sz = 0;
			var seen = new HashSet<FeatureKind>();
			foreach (var k in n.Kinds) {
				if (!seen.Add(k)) continue;
				// 主组件的体积记在这一行；被别的行带上、自己又单独成行的依赖不重复计
				if (n.Primary is FeatureKind pk && k != pk && shared.Contains(k)) continue;
				sz += FeatureInstaller.ExpectedSize(k);
			}
			n.SizeLabel = sz > 0 ? Loc.T("feat.size.about", FeatureInstaller.FormatBytes(sz)) : "";
		}
	}
}
