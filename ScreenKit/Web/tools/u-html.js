(function(){
	var wait = 0;
	var samples = [
		{ id: "buttons", key: "htmlui.tpl.buttons", html: buttons() },
		{ id: "card", key: "htmlui.tpl.card", html: card() },
		{ id: "media", key: "htmlui.tpl.media", html: media() },
		{ id: "inputs", key: "htmlui.tpl.inputs", html: inputs() },
		{ id: "choice", key: "htmlui.tpl.choice", html: choice() },
		{ id: "form", key: "htmlui.tpl.form", html: form() },
		{ id: "table", key: "htmlui.tpl.table", html: table() },
		{ id: "nav", key: "htmlui.tpl.nav", html: nav() },
		{ id: "alert", key: "htmlui.tpl.alert", html: alert() },
		{ id: "tags", key: "htmlui.tpl.tags", html: tags() },
		{ id: "list", key: "htmlui.tpl.list", html: list() },
		{ id: "progress", key: "htmlui.tpl.progress", html: progress() },
		{ id: "empty", key: "htmlui.tpl.empty", html: empty() },
		{ id: "dialog", key: "htmlui.tpl.dialog", html: dialog() },
		{ id: "pager", key: "htmlui.tpl.pager", html: pager() },
		{ id: "steps", key: "htmlui.tpl.steps", html: steps() }
	];
	SK.regutil("u-html", {
		title: "HTML界面设计",
		go: "显示",
		own: true,
		hideGo: true,
		hideCopy: true,
		hideOut: true,
		body: body,
		open: open
	});

	function body(){
		var hint = SK.t("web.htmlui.hint") || "";
		var html = "";
		for (var i = 0; i < samples.length; i++) {
			var one = samples[i];
			html += '<button type="button" data-htmlui="' + one.id + '">' + SK.esc(SK.t(one.key) || one.id) + "</button>";
		}
		return '<div class="htmlui">'
			+ '<p class="hint" data-k="web.htmlui.hint">' + SK.esc(hint) + "</p>"
			+ '<div class="htmlui-tpl">' + html + "</div>"
			+ '<div class="htmlui-split">'
			+ '<textarea id="htmlui-src" spellcheck="false"></textarea>'
			+ '<iframe id="htmlui-view" sandbox="allow-forms allow-scripts" title="preview"></iframe>'
			+ "</div></div>";
	}

	function open(){
		var src = SK.$("htmlui-src");
		var bar = document.querySelector(".htmlui-tpl");
		if (!src || !bar) return;
		src.oninput = schedule;
		bar.onclick = function(ev){
			var el = ev.target;
			if (!el || !el.getAttribute) return;
			var id = el.getAttribute("data-htmlui");
			if (!id) return;
			var one = find(id);
			if (!one) return;
			src.value = one.html;
			paint();
		};
		if (!src.value) {
			src.value = samples[0].html;
			paint();
		}
	}

	function find(id){
		for (var i = 0; i < samples.length; i++)
			if (samples[i].id === id) return samples[i];
		return null;
	}

	function schedule(){
		if (wait) clearTimeout(wait);
		wait = setTimeout(function(){
			wait = 0;
			paint();
		}, 200);
	}

	function paint(){
		var src = SK.$("htmlui-src");
		var view = SK.$("htmlui-view");
		if (!src || !view) return;
		view.srcdoc = wrap(src.value);
	}

	function wrap(html){
		var raw = html == null ? "" : String(html);
		var head = raw.replace(/^\s+/, "");
		if (/^<!doctype/i.test(head) || /^<html/i.test(head)) return raw;
		return "<!DOCTYPE html><html><head>"
			+ '<meta http-equiv="X-UA-Compatible" content="IE=edge">'
			+ '<meta charset="utf-8">'
			+ "<style>body{margin:16px;font-family:Segoe UI,Microsoft YaHei UI,sans-serif;color:#1f2937;background:#f8fafc;}</style>"
			+ "</head><body>" + raw + "</body></html>";
	}

	function buttons(){
		return "<style>\n"
			+ ".btn { display:inline-block; margin:0 8px 8px 0; border-radius:8px; padding:8px 16px; font-size:14px; line-height:1.2; cursor:pointer; }\n"
			+ ".btn-primary { background:#2563eb; color:#fff; border:1px solid #2563eb; }\n"
			+ ".btn-line { background:#fff; color:#2563eb; border:1px solid #2563eb; }\n"
			+ ".btn-danger { background:#dc2626; color:#fff; border:1px solid #dc2626; }\n"
			+ ".btn-text { background:transparent; color:#334155; border:1px solid transparent; }\n"
			+ ".btn:disabled { background:#e5e7eb; color:#9ca3af; border-color:#e5e7eb; cursor:default; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-buttons -->\n"
			+ '<button class="btn btn-primary" type="button">主要按钮</button>\n'
			+ '<button class="btn btn-line" type="button">线框按钮</button>\n'
			+ '<button class="btn btn-danger" type="button">危险按钮</button>\n'
			+ '<button class="btn btn-text" type="button">文字按钮</button>\n'
			+ '<button class="btn btn-primary" type="button" disabled>不可用</button>\n';
	}

	function card(){
		return "<style>\n"
			+ ".card { max-width:360px; background:#fff; border:1px solid #e5e7eb; border-radius:12px; padding:16px; }\n"
			+ ".card h3 { margin:0 0 8px; font-size:18px; }\n"
			+ ".card p { margin:0 0 14px; color:#64748b; font-size:14px; line-height:1.5; }\n"
			+ ".btn { display:inline-block; margin:0 8px 0 0; border-radius:8px; padding:8px 16px; font-size:14px; cursor:pointer; }\n"
			+ ".btn-primary { background:#2563eb; color:#fff; border:1px solid #2563eb; }\n"
			+ ".btn-line { background:#fff; color:#334155; border:1px solid #cbd5e1; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-card -->\n"
			+ '<div class="card">\n<h3>卡片标题</h3>\n'
			+ "<p>一段说明。标题、正文和按钮放在同一张卡片里。</p>\n"
			+ '<button class="btn btn-primary" type="button">确定</button>\n'
			+ '<button class="btn btn-line" type="button">取消</button>\n</div>\n';
	}

	function media(){
		return "<style>\n"
			+ ".card { max-width:320px; background:#fff; border:1px solid #e5e7eb; border-radius:12px; overflow:hidden; }\n"
			+ ".thumb { height:120px; background:#dbeafe; }\n"
			+ ".body { padding:14px 16px 16px; }\n"
			+ ".body h3 { margin:0 0 6px; font-size:16px; }\n"
			+ ".body p { margin:0 0 12px; color:#64748b; font-size:13px; line-height:1.5; }\n"
			+ ".btn { border:0; border-radius:8px; padding:8px 14px; background:#2563eb; color:#fff; font-size:14px; cursor:pointer; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-media -->\n"
			+ '<div class="card">\n<div class="thumb"></div>\n<div class="body">\n'
			+ "<h3>图文卡片</h3>\n<p>上面是色块，下面是标题、说明和按钮。</p>\n"
			+ '<button class="btn" type="button">查看</button>\n</div>\n</div>\n';
	}

	function inputs(){
		return "<style>\n"
			+ "label { display:block; margin:0 0 12px; font-size:13px; color:#334155; }\n"
			+ "input, select, textarea { display:block; width:100%; max-width:320px; box-sizing:border-box; margin-top:4px; padding:8px 10px; border:1px solid #cbd5e1; border-radius:8px; font-size:14px; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-inputs -->\n"
			+ '<label>姓名<input type="text" value="张三"></label>\n'
			+ '<label>密码<input type="password" value="secret"></label>\n'
			+ '<label>城市<select><option>北京</option><option>上海</option><option>广州</option></select></label>\n'
			+ '<label>备注<textarea rows="3">写一点说明</textarea></label>\n';
	}

	function choice(){
		return "<style>\n"
			+ "fieldset { border:1px solid #e5e7eb; border-radius:12px; max-width:320px; margin:0 0 12px; padding:12px 14px; }\n"
			+ "legend { padding:0 6px; font-size:13px; color:#334155; }\n"
			+ "label { display:block; margin:6px 0; font-size:14px; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-choice -->\n"
			+ "<fieldset>\n<legend>多选</legend>\n"
			+ '<label><input type="checkbox" checked> 邮件通知</label>\n'
			+ '<label><input type="checkbox"> 短信通知</label>\n</fieldset>\n'
			+ "<fieldset>\n<legend>单选</legend>\n"
			+ '<label><input type="radio" name="plan" checked> 免费</label>\n'
			+ '<label><input type="radio" name="plan"> 专业</label>\n'
			+ '<label><input type="radio" name="plan"> 团队</label>\n</fieldset>\n';
	}

	function form(){
		return "<style>\n"
			+ ".card { max-width:360px; background:#fff; border:1px solid #e5e7eb; border-radius:12px; padding:16px; }\n"
			+ ".card h3 { margin:0 0 12px; font-size:18px; }\n"
			+ "label { display:block; margin:0 0 12px; font-size:13px; color:#334155; }\n"
			+ "input, textarea { display:block; width:100%; box-sizing:border-box; margin-top:4px; padding:8px 10px; border:1px solid #cbd5e1; border-radius:8px; font-size:14px; }\n"
			+ ".btn { border:0; border-radius:8px; padding:8px 16px; background:#2563eb; color:#fff; font-size:14px; cursor:pointer; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-form -->\n"
			+ '<form class="card">\n<h3>联系我们</h3>\n'
			+ '<label>姓名<input type="text" value="张三"></label>\n'
			+ '<label>邮箱<input type="email" value="a@example.com"></label>\n'
			+ '<label>内容<textarea rows="3">想了解一下</textarea></label>\n'
			+ '<button class="btn" type="button">提交</button>\n</form>\n';
	}

	function table(){
		return "<style>\n"
			+ "table { border-collapse:collapse; width:100%; max-width:480px; background:#fff; font-size:14px; }\n"
			+ "th, td { border:1px solid #e5e7eb; padding:8px 10px; text-align:left; }\n"
			+ "th { background:#f1f5f9; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-table -->\n"
			+ "<table>\n<tr><th>名称</th><th>数量</th><th>状态</th></tr>\n"
			+ "<tr><td>苹果</td><td>12</td><td>在库</td></tr>\n"
			+ "<tr><td>香蕉</td><td>8</td><td>在库</td></tr>\n"
			+ "<tr><td>橙子</td><td>0</td><td>缺货</td></tr>\n</table>\n";
	}

	function nav(){
		return "<style>\n"
			+ ".bar { display:flex; align-items:center; gap:16px; max-width:520px; background:#0f172a; color:#fff; border-radius:12px; padding:12px 16px; }\n"
			+ ".bar b { margin-right:auto; }\n"
			+ ".bar a { color:#e2e8f0; text-decoration:none; font-size:14px; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-nav -->\n"
			+ '<div class="bar">\n<b>站点</b>\n'
			+ '<a href="#">首页</a>\n<a href="#">文档</a>\n<a href="#">价格</a>\n</div>\n';
	}

	function alert(){
		return "<style>\n"
			+ ".note { max-width:420px; margin:0 0 8px; border-radius:8px; padding:10px 12px; font-size:14px; }\n"
			+ ".info { background:#dbeafe; color:#1e3a8a; }\n"
			+ ".ok { background:#dcfce7; color:#14532d; }\n"
			+ ".warn { background:#fef3c7; color:#78350f; }\n"
			+ ".bad { background:#fee2e2; color:#7f1d1d; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-alert -->\n"
			+ '<div class="note info">这是一条说明。</div>\n'
			+ '<div class="note ok">已保存。</div>\n'
			+ '<div class="note warn">请先检查输入。</div>\n'
			+ '<div class="note bad">提交失败。</div>\n';
	}

	function tags(){
		return "<style>\n"
			+ ".tag { display:inline-block; margin:0 6px 6px 0; border-radius:999px; padding:4px 10px; font-size:12px; background:#e2e8f0; color:#334155; }\n"
			+ ".tag.on { background:#dbeafe; color:#1d4ed8; }\n"
			+ ".tag.hot { background:#fee2e2; color:#b91c1c; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-tags -->\n"
			+ '<span class="tag on">设计</span>\n'
			+ '<span class="tag">前端</span>\n'
			+ '<span class="tag hot">紧急</span>\n'
			+ '<span class="tag">草稿</span>\n';
	}

	function list(){
		return "<style>\n"
			+ "ul { max-width:360px; margin:0; padding:0; list-style:none; background:#fff; border:1px solid #e5e7eb; border-radius:12px; overflow:hidden; }\n"
			+ "li { padding:10px 14px; border-bottom:1px solid #e5e7eb; font-size:14px; }\n"
			+ "li:last-child { border-bottom:0; }\n"
			+ "small { display:block; color:#64748b; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-list -->\n"
			+ "<ul>\n<li>收件箱<small>3 封未读</small></li>\n"
			+ "<li>已发送<small>昨天</small></li>\n"
			+ "<li>草稿<small>1 封</small></li>\n</ul>\n";
	}

	function progress(){
		return "<style>\n"
			+ ".row { max-width:360px; margin:0 0 12px; font-size:13px; color:#334155; }\n"
			+ ".track { height:8px; background:#e5e7eb; border-radius:99px; overflow:hidden; margin-top:4px; }\n"
			+ ".bar { height:100%; background:#2563eb; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-progress -->\n"
			+ '<div class="row">上传 40%<div class="track"><div class="bar" style="width:40%"></div></div></div>\n'
			+ '<div class="row">同步 75%<div class="track"><div class="bar" style="width:75%"></div></div></div>\n';
	}

	function empty(){
		return "<style>\n"
			+ ".box { max-width:320px; background:#fff; border:1px dashed #cbd5e1; border-radius:12px; padding:28px 16px; text-align:center; }\n"
			+ ".box h3 { margin:0 0 6px; font-size:16px; }\n"
			+ ".box p { margin:0 0 12px; color:#64748b; font-size:13px; }\n"
			+ ".btn { border:0; border-radius:8px; padding:8px 14px; background:#2563eb; color:#fff; cursor:pointer; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-empty -->\n"
			+ '<div class="box">\n<h3>还没有内容</h3>\n'
			+ "<p>添加第一条记录后，这里会列出来。</p>\n"
			+ '<button class="btn" type="button">新建</button>\n</div>\n';
	}

	function dialog(){
		return "<style>\n"
			+ ".mask { max-width:420px; background:rgba(15,23,42,.45); border-radius:12px; padding:28px 16px; }\n"
			+ ".dlg { max-width:300px; margin:0 auto; background:#fff; border-radius:12px; padding:16px; }\n"
			+ ".dlg h3 { margin:0 0 8px; font-size:16px; }\n"
			+ ".dlg p { margin:0 0 14px; color:#64748b; font-size:14px; }\n"
			+ ".btn { border-radius:8px; padding:8px 14px; font-size:14px; cursor:pointer; }\n"
			+ ".ok { background:#2563eb; color:#fff; border:1px solid #2563eb; }\n"
			+ ".no { background:#fff; color:#334155; border:1px solid #cbd5e1; margin-left:8px; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-dialog -->\n"
			+ '<div class="mask">\n<div class="dlg">\n<h3>删除这条记录？</h3>\n'
			+ "<p>删除后不能恢复。</p>\n"
			+ '<button class="btn ok" type="button">删除</button>\n'
			+ '<button class="btn no" type="button">取消</button>\n</div>\n</div>\n';
	}

	function pager(){
		return "<style>\n"
			+ ".pg { display:flex; gap:6px; }\n"
			+ ".pg a, .pg span { min-width:32px; text-align:center; padding:6px 8px; border:1px solid #e5e7eb; border-radius:8px; background:#fff; color:#334155; text-decoration:none; font-size:14px; }\n"
			+ ".pg .on { background:#2563eb; color:#fff; border-color:#2563eb; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-pager -->\n"
			+ '<div class="pg">\n<a href="#">上一页</a>\n'
			+ '<span class="on">1</span>\n<a href="#">2</a>\n<a href="#">3</a>\n'
			+ '<a href="#">下一页</a>\n</div>\n';
	}

	function steps(){
		return "<style>\n"
			+ ".steps { display:flex; gap:8px; max-width:420px; font-size:13px; }\n"
			+ ".steps div { flex:1; text-align:center; color:#64748b; }\n"
			+ ".n { display:block; width:28px; height:28px; line-height:28px; margin:0 auto 6px; border-radius:50%; background:#e5e7eb; }\n"
			+ ".on .n { background:#2563eb; color:#fff; }\n"
			+ ".on { color:#1e293b; }\n"
			+ "</style>\n"
			+ "<!-- htmlui-steps -->\n"
			+ '<div class="steps">\n'
			+ '<div class="on"><span class="n">1</span>填写</div>\n'
			+ '<div class="on"><span class="n">2</span>确认</div>\n'
			+ '<div><span class="n">3</span>完成</div>\n</div>\n';
	}
})();
