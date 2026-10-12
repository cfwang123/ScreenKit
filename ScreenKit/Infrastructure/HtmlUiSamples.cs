namespace ScreenKit;

/// <summary>HTML 界面设计的常用模板。片段不含整页时，预览会补上文档头。</summary>
static class HtmlUiSamples {
	public sealed class Sample {
		public readonly string Id;
		public readonly string Key;
		public readonly string Html;

		public Sample(string id, string key, string html) {
			Id = id;
			Key = key;
			Html = html;
		}
	}

	public static readonly Sample[] All = {
		new("buttons", "htmlui.tpl.buttons", Buttons),
		new("card", "htmlui.tpl.card", Card),
		new("media", "htmlui.tpl.media", Media),
		new("inputs", "htmlui.tpl.inputs", Inputs),
		new("choice", "htmlui.tpl.choice", Choice),
		new("form", "htmlui.tpl.form", Form),
	};

	public static Sample Find(string id) {
		foreach (var one in All)
			if (one.Id == id) return one;
		return null;
	}

	public static string Wrap(string html) {
		var raw = html ?? "";
		var head = raw.TrimStart();
		if (head.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
			|| head.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
			return raw;
		return "<!DOCTYPE html><html><head>"
			+ "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">"
			+ "<meta charset=\"utf-8\">"
			+ "<style>body{margin:16px;font-family:Segoe UI,Microsoft YaHei UI,sans-serif;color:#1f2937;background:#f8fafc;}</style>"
			+ "</head><body>" + raw + "</body></html>";
	}

	const string Buttons = """
		<style>
		.btn { display:inline-block; margin:0 8px 8px 0; border-radius:8px; padding:8px 16px; font-size:14px; line-height:1.2; cursor:pointer; }
		.btn-primary { background:#2563eb; color:#fff; border:1px solid #2563eb; }
		.btn-line { background:#fff; color:#2563eb; border:1px solid #2563eb; }
		.btn-danger { background:#dc2626; color:#fff; border:1px solid #dc2626; }
		.btn-text { background:transparent; color:#334155; border:1px solid transparent; }
		.btn:disabled { background:#e5e7eb; color:#9ca3af; border-color:#e5e7eb; cursor:default; }
		</style>
		<!-- htmlui-buttons -->
		<button class="btn btn-primary" type="button">主要按钮</button>
		<button class="btn btn-line" type="button">线框按钮</button>
		<button class="btn btn-danger" type="button">危险按钮</button>
		<button class="btn btn-text" type="button">文字按钮</button>
		<button class="btn btn-primary" type="button" disabled>不可用</button>
		""";

	const string Card = """
		<style>
		.card { max-width:360px; background:#fff; border:1px solid #e5e7eb; border-radius:12px; padding:16px; }
		.card h3 { margin:0 0 8px; font-size:18px; }
		.card p { margin:0 0 14px; color:#64748b; font-size:14px; line-height:1.5; }
		.btn { display:inline-block; margin:0 8px 0 0; border-radius:8px; padding:8px 16px; font-size:14px; cursor:pointer; }
		.btn-primary { background:#2563eb; color:#fff; border:1px solid #2563eb; }
		.btn-line { background:#fff; color:#334155; border:1px solid #cbd5e1; }
		</style>
		<!-- htmlui-card -->
		<div class="card">
		<h3>卡片标题</h3>
		<p>一段说明。标题、正文和按钮放在同一张卡片里。</p>
		<button class="btn btn-primary" type="button">确定</button>
		<button class="btn btn-line" type="button">取消</button>
		</div>
		""";

	const string Media = """
		<style>
		.card { max-width:320px; background:#fff; border:1px solid #e5e7eb; border-radius:12px; overflow:hidden; }
		.thumb { height:120px; background:#dbeafe; }
		.body { padding:14px 16px 16px; }
		.body h3 { margin:0 0 6px; font-size:16px; }
		.body p { margin:0 0 12px; color:#64748b; font-size:13px; line-height:1.5; }
		.btn { border:0; border-radius:8px; padding:8px 14px; background:#2563eb; color:#fff; font-size:14px; cursor:pointer; }
		</style>
		<!-- htmlui-media -->
		<div class="card">
		<div class="thumb"></div>
		<div class="body">
		<h3>图文卡片</h3>
		<p>上面是色块，下面是标题、说明和按钮。</p>
		<button class="btn" type="button">查看</button>
		</div>
		</div>
		""";

	const string Inputs = """
		<style>
		label { display:block; margin:0 0 12px; font-size:13px; color:#334155; }
		input, select, textarea { display:block; width:100%; max-width:320px; box-sizing:border-box; margin-top:4px; padding:8px 10px; border:1px solid #cbd5e1; border-radius:8px; font-size:14px; }
		</style>
		<!-- htmlui-inputs -->
		<label>姓名<input type="text" value="张三"></label>
		<label>密码<input type="password" value="secret"></label>
		<label>城市<select><option>北京</option><option>上海</option><option>广州</option></select></label>
		<label>备注<textarea rows="3">写一点说明</textarea></label>
		""";

	const string Choice = """
		<style>
		fieldset { border:1px solid #e5e7eb; border-radius:12px; max-width:320px; margin:0 0 12px; padding:12px 14px; }
		legend { padding:0 6px; font-size:13px; color:#334155; }
		label { display:block; margin:6px 0; font-size:14px; }
		</style>
		<!-- htmlui-choice -->
		<fieldset>
		<legend>多选</legend>
		<label><input type="checkbox" checked> 邮件通知</label>
		<label><input type="checkbox"> 短信通知</label>
		</fieldset>
		<fieldset>
		<legend>单选</legend>
		<label><input type="radio" name="plan" checked> 免费</label>
		<label><input type="radio" name="plan"> 专业</label>
		<label><input type="radio" name="plan"> 团队</label>
		</fieldset>
		""";

	const string Form = """
		<style>
		.card { max-width:360px; background:#fff; border:1px solid #e5e7eb; border-radius:12px; padding:16px; }
		.card h3 { margin:0 0 12px; font-size:18px; }
		label { display:block; margin:0 0 12px; font-size:13px; color:#334155; }
		input, textarea { display:block; width:100%; box-sizing:border-box; margin-top:4px; padding:8px 10px; border:1px solid #cbd5e1; border-radius:8px; font-size:14px; }
		.btn { border:0; border-radius:8px; padding:8px 16px; background:#2563eb; color:#fff; font-size:14px; cursor:pointer; }
		</style>
		<!-- htmlui-form -->
		<form class="card">
		<h3>联系我们</h3>
		<label>姓名<input type="text" value="张三"></label>
		<label>邮箱<input type="email" value="a@example.com"></label>
		<label>内容<textarea rows="3">想了解一下</textarea></label>
		<button class="btn" type="button">提交</button>
		</form>
		""";
}
