using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ScreenKit;

/// <summary>工具 → SQL 方言转换：拖入 .sql 认引擎和版本，选目标引擎与版本后导出新 .sql。</summary>
public partial class SqlConvWindow : Window {
	sealed class EngineItem {
		/// <summary>null 表示「自动识别」：用脚本里识别出的引擎。</summary>
		public readonly SqlEngine? Engine;

		public EngineItem(SqlEngine? e) => Engine = e;

		public override string ToString() => Engine == null ? Loc.T("sqlconv.auto") : SqlEngines.Name(Engine.Value);
	}

	string path = "";
	string sourceText = "";
	bool loading;
	SqlConvertResult last;
	readonly DispatcherTimer detectTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };

	internal string Preview => etext.Text ?? "";
	internal SqlDetect LastDetect { get; private set; }
	internal string LastStat => lbstat.Text ?? "";

	public SqlConvWindow() {
		InitializeComponent();
		initev();
		applylang();
		fillengines();
		esrceng.SelectedIndex = 0;
		edsteng.SelectedIndex = 1;
		fillversions(true);
		fillversions(false);
		enableui(false);
		WindowEsc.Attach(this);
	}

	void applylang() {
		Title = Loc.T("sqlconv.title");
		lbhint.Text = Loc.T("sqlconv.hint");
		lbsrc.Text = Loc.T("sqlconv.src");
		lbdst.Text = Loc.T("sqlconv.dst");
		lbsrcscript.Text = Loc.T("sqlconv.source");
		lbprev.Text = Loc.T("sqlconv.preview");
		bconvert.Content = Loc.T("sqlconv.convert");
		bsave.Content = Loc.T("sqlconv.save");
		ToolBtnUi.Set(bbrowse, ToolBtnUi.Browse, Loc.T("sqlconv.browse"));
		cbdrop.Content = Loc.T("sqlconv.drop");
		cbcomment.Content = Loc.T("sqlconv.comments");
		cbdrop.IsChecked = true;
		cbcomment.IsChecked = true;
		lbstat.Text = Loc.T("sqlconv.waithint");
	}

	void initev() {
		bbrowse.Click += (_, _) => browse();
		bconvert.Click += (_, _) => convert();
		bsave.Click += (_, _) => save();
		// 源脚本框可以直接输入/粘贴/删除：停下打字后再重新识别引擎和版本
		esrc.TextChanged += (_, _) => {
			if (loading) return;
			enableui(true);
			detectTimer.Stop();
			detectTimer.Start();
		};
		detectTimer.Tick += (_, _) => {
			detectTimer.Stop();
			returntoedit();
		};
		esrceng.SelectionChanged += (_, _) => {
			if (loading) return;
			fillversions(true);
			redetect();
		};
		edsteng.SelectionChanged += (_, _) => {
			if (loading) return;
			fillversions(false);
		};

		bool dropok = false;
		DragEnter += (_, e) => {
			dropok = e.Data.GetDataPresent(DataFormats.FileDrop);
			e.Effects = dropok ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		DragOver += (_, e) => {
			if (!dropok) dropok = e.Data.GetDataPresent(DataFormats.FileDrop);
			e.Effects = dropok ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		DragLeave += (_, _) => dropok = false;
		Drop += (_, e) => {
			e.Handled = true;
			e.Effects = DragDropEffects.Copy;
			dropok = false;
			if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
			Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => load(FilesOf(files))));
		};
	}

	/// <summary>拖进来的可能是文件夹：把里面的 .sql 都收下，只取第一个。</summary>
	static string FilesOf(string[] files) {
		foreach (var f in files) {
			if (File.Exists(f)) return f;
			if (!Directory.Exists(f)) continue;
			try {
				var hit = Directory.GetFiles(f, "*.sql", SearchOption.AllDirectories);
				if (hit.Length > 0) return hit[0];
			}
			catch { }
		}
		return files[0];
	}

	void fillengines() {
		loading = true;
		// 源：第一项是自动识别；其余是手动指定
		var src = new List<EngineItem> { new(null) };
		foreach (var e in new[] { SqlEngine.Sqlite, SqlEngine.Mysql, SqlEngine.Mssql, SqlEngine.Oracle, SqlEngine.Postgres })
			src.Add(new EngineItem(e));
		var dst = new List<EngineItem>();
		foreach (var e in new[] { SqlEngine.Sqlite, SqlEngine.Mysql, SqlEngine.Mssql, SqlEngine.Oracle, SqlEngine.Postgres })
			dst.Add(new EngineItem(e));
		esrceng.ItemsSource = src;
		edsteng.ItemsSource = dst;
		loading = false;
	}

	void fillversions(bool source) {
		var combo = source ? esrcver : edstver;
		var eng = source ? srce() : dste();
		loading = true;
		combo.ItemsSource = SqlEngines.Versions(eng);
		var want = source ? (LastDetect != null && LastDetect.Engine == eng ? LastDetect.Version : null) : null;
		var pick = combo.Items.Count - 1;
		if (want != null) {
			for (var i = 0; i < combo.Items.Count; i++) {
				if (combo.Items[i] is SqlTargets.Item it && it.Version == want) {
					pick = i;
					break;
				}
			}
		}
		combo.SelectedIndex = pick;
		loading = false;
	}

	/// <summary>源引擎：用户手动选了就用他选的，否则用自动识别的引擎。</summary>
	SqlEngine srce() {
		if (esrceng.SelectedItem is EngineItem it && it.Engine != null) return it.Engine.Value;
		return LastDetect != null ? LastDetect.Engine : SqlEngine.Sqlite;
	}

	bool srcmanual() => esrceng.SelectedItem is EngineItem it && it.Engine != null;

	SqlEngine dste() => edsteng.SelectedItem is EngineItem it && it.Engine != null ? it.Engine.Value : SqlEngine.Mysql;

	Version srcver() => esrcver.SelectedItem is SqlTargets.Item it ? it.Version : SqlEngines.Default(srce());

	Version dstver() => edstver.SelectedItem is SqlTargets.Item it ? it.Version : SqlEngines.Default(dste());

	// 引擎、版本和选项不受有没有脚本影响：先选好方言再输入也能正常工作
	void enableui(bool on) {
		bconvert.IsEnabled = on;
		bsave.IsEnabled = on && last != null && last.Text.Length > 0;
	}

	void browse() {
		var dlg = new Microsoft.Win32.OpenFileDialog {
			Title = Loc.T("sqlconv.browse"),
			Filter = Loc.T("sqlconv.filter"),
			Multiselect = false,
		};
		if (dlg.ShowDialog(this) != true) return;
		load(dlg.FileName);
	}

	/// <summary>读文件 → 自动识别 → 填好源引擎/版本 → 直接转一次给预览。</summary>
	void load(string file) {
		if (string.IsNullOrWhiteSpace(file)) return;
		try {
			if (!File.Exists(file)) {
				lbstat.Text = Loc.T("sqlconv.nofile", file);
				return;
			}
			sourceText = SqlScript.ReadText(file);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("sqlconv.readfail", ex.Message);
			return;
		}
		path = file;
		efile.Text = file;
		// 换文件时源引擎回到「自动识别」，让识别结果重新说了算
		loading = true;
		esrceng.SelectedIndex = 0;
		esrc.Text = sourceText;
		loading = false;
		enableui(true);
		redetect();
		convert();
	}

	/// <summary>源脚本框被编辑后：重新识别引擎与版本，再转一次刷新结果框。</summary>
	void returntoedit() {
		sourceText = esrc.Text ?? "";
		if (sourceText.Trim().Length == 0) {
			last = null;
			etext.Text = "";
			lbdeteced.Text = "";
			lbstat.Text = Loc.T("sqlconv.waithint");
			bsave.IsEnabled = false;
			return;
		}
		enableui(true);
		redetect();
		convert();
	}

	/// <summary>识别源引擎和版本并把结果填进下拉；用户手动指定了引擎就只换版本。</summary>
	void redetect() {
		// 还没有脚本时不猜方言：让下拉保持用户手选的值
		if (sourceText.Trim().Length == 0) {
			LastDetect = null;
			lbdeteced.Text = "";
			return;
		}
		var d = SqlScript.Detect(sourceText);
		LastDetect = d;
		lbdeteced.Text = d.Reasons.Count > 0
			? Loc.T("sqlconv.detected", d.Summary, string.Join("；", d.Reasons.ToArray()))
			: Loc.T("sqlconv.detected", d.Summary, "");
		var manual = srcmanual();
		loading = true;
		if (!manual) esrceng.SelectedIndex = 0;
		loading = false;
		fillversions(true);
		if (manual) lbdeteced.Text += "　" + Loc.T("sqlconv.manual", SqlEngines.Name(srce()));
		// 目标不能与源相同，否则什么都转不出来：撞上了就换一个常见的目标
		if (dste() == srce()) picktarget();
	}

	/// <summary>挑一个与源不同的目标引擎，优先 SQL Server / MySQL / PostgreSQL 这类常用库。</summary>
	void picktarget() {
		var want = srce() switch {
			SqlEngine.Mysql => SqlEngine.Postgres,
			SqlEngine.Postgres => SqlEngine.Mysql,
			SqlEngine.Mssql => SqlEngine.Mysql,
			_ => SqlEngine.Mysql,
		};
		for (var i = 0; i < edsteng.Items.Count; i++) {
			if (edsteng.Items[i] is not EngineItem ei || ei.Engine != want) continue;
			loading = true;
			edsteng.SelectedIndex = i;
			loading = false;
			fillversions(false);
			return;
		}
	}

	void convert() {
		if (sourceText.Length == 0) {
			lbstat.Text = Loc.T("sqlconv.nofile2");
			return;
		}
		var o = new SqlConvertOptions {
			Source = srce(),
			SourceVersion = srcver(),
			Target = dste(),
			TargetVersion = dstver(),
			WithDrop = cbdrop.IsChecked == true,
			KeepComments = cbcomment.IsChecked == true,
		};
		if (o.Source == o.Target) {
			lbstat.Text = Loc.T("sqlconv.same");
			last = null;
			etext.Text = "";
			enableui(true);
			return;
		}
		try {
			last = SqlScript.Convert(sourceText, o);
		}
		catch (Exception ex) {
			last = new SqlConvertResult { Error = ex.Message };
		}
		if (last.Error.Length > 0) {
			lbstat.Text = Loc.T("sqlconv.fail", last.Error);
			etext.Text = "";
			bsave.IsEnabled = false;
			return;
		}
		etext.Text = last.Text;
		etext.ScrollToHome();
		bsave.IsEnabled = true;
		var msg = Loc.T("sqlconv.done", last.Tables, last.Rows, last.Indexes);
		if (last.Kept > 0) msg += "  " + Loc.T("sqlconv.kept", last.Kept);
		if (last.Funcs > 0) msg += "  " + Loc.T("sqlconv.funcs", last.Funcs);
		if (last.Skipped.Count > 0)
			msg += "\r\n" + Loc.T("sqlconv.skipped", last.Skipped.Count, string.Join("、", last.Skipped.ToArray()));
		foreach (var w in last.Warnings) msg += "\r\n" + w;
		lbstat.Text = msg;
	}

	/// <summary>另存为：默认与源文件同目录、文件名带上目标引擎和版本。</summary>
	void save() {
		if (last == null || last.Text.Length == 0) {
			lbstat.Text = Loc.T("sqlconv.needconvert");
			return;
		}
		var target = dste();
		var ext = SqlEngines.Code(target);
		var baseName = "convert";
		var dir = "";
		try {
			if (path.Length > 0) {
				baseName = Path.GetFileNameWithoutExtension(path);
				dir = Path.GetDirectoryName(path) ?? "";
			}
		}
		catch { }
		var name = $"{baseName}.{ext}.sql";
		var dlg = new Microsoft.Win32.SaveFileDialog {
			Title = Loc.T("sqlconv.save"),
			Filter = Loc.T("sqlconv.filter.save"),
			FilterIndex = 1,
			AddExtension = true,
			DefaultExt = ".sql",
			OverwritePrompt = true,
			FileName = name,
		};
		if (dir.Length > 0) dlg.InitialDirectory = dir;
		if (dlg.ShowDialog(this) != true) return;
		try {
			SqlScript.WriteText(dlg.FileName, last.Text);
			lbstat.Text = Loc.T("sqlconv.saved", dlg.FileName);
		}
		catch (Exception ex) {
			lbstat.Text = Loc.T("sqlconv.savefail", ex.Message);
		}
	}

	/// <summary>自测用：直接载入一个脚本并转换，返回统计。</summary>
	internal string SelfTest(string file) {
		load(file);
		convert();
		return (lbstat.Text ?? "") + " | " + (lbdeteced.Text ?? "");
	}

	/// <summary>自测用：把当前预览写到指定文件。</summary>
	internal string SelfSave(string file) {
		if (last == null) return "NO_RESULT";
		SqlScript.WriteText(file, last.Text);
		return $"OK {last.Text.Length} bytes";
	}
}
