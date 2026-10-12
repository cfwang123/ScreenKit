(function(){
	var wait = 0;
	var samples = [
		{ id: "buttons", key: "htmlui.tpl.buttons", html: buttons() },
		{ id: "card", key: "htmlui.tpl.card", html: card() },
		{ id: "media", key: "htmlui.tpl.media", html: media() },
		{ id: "inputs", key: "htmlui.tpl.inputs", html: inputs() },
		{ id: "choice", key: "htmlui.tpl.choice", html: choice() },
		{ id: "form", key: "htmlui.tpl.form", html: form() }
	];
	SK.regutil("u-html", {
		title: "HTML 界面设计",
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
})();
