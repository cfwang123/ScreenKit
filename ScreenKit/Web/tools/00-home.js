(function(){
	F.ParseEmmet = function(name){
		var tag = "div", cls = [], id = "", pos0 = 0, i, ch, seg, nowtype = 0;
		for (i = 0; i < name.length; i++) {
			ch = name.charCodeAt(i);
			if (ch !== 46 && ch !== 35) continue;
			seg = name.substring(pos0, i);
			if (seg) {
				if (nowtype === 0) tag = seg;
				else if (nowtype === 1) cls.push(seg);
				else id = seg;
			}
			nowtype = ch === 46 ? 1 : 2;
			pos0 = i + 1;
		}
		seg = name.substring(pos0);
		if (seg) {
			if (nowtype === 0) tag = seg;
			else if (nowtype === 1) cls.push(seg);
			else id = seg;
		}
		if (!tag) tag = "div";
		return { tag: tag, id: id, cls: cls.join(" ") };
	};

	F.CreateDOM = function(page, o){
		var i, arr, spec, dom, k, val, childs;
		if (o instanceof Array) {
			arr = [];
			for (i = 0; i < o.length; i++) if (o[i] != null) arr.push(F.CreateDOM(page, o[i]));
			return arr;
		}
		if (typeof Element === "function" && o instanceof Element) return o;
		spec = F.ParseEmmet(o.x || "div");
		dom = document.createElement(spec.tag);
		if (spec.id) dom.id = spec.id;
		if (spec.cls) dom.className = spec.cls;
		childs = o.c;
		for (k in o) {
			if (!Object.prototype.hasOwnProperty.call(o, k)) continue;
			if (k === "x" || k === "c" || k === "s" || k === "name") continue;
			val = o[k];
			if (typeof val === "function") dom[k] = val;
			else if (val === true) dom.setAttribute(k, "");
			else if (val !== false && val != null) dom.setAttribute(k, val);
		}
		if (o.s) {
			for (k in o.s) if (Object.prototype.hasOwnProperty.call(o.s, k)) dom.style[k] = o.s[k];
		}
		if (childs != null) addkids(page, dom, childs instanceof Array ? childs : [childs]);
		if (o.name && page && page.doms) page.doms[o.name] = dom;
		return dom;
	};

	// 工具卡片和分类按钮由 /sk/catalog.js 生成：分类、卡片、图标、搜索词都来自
	// ToolCatalog.cs，和桌面菜单、托盘是同一份定义。加工具只改那个文件。
	F.buildtools = function(page){
		var cat = window.SKCAT || {};
		var cats = cat.cats || [];
		var tools = cat.tools || [];
		var nav = page.doms.cats;
		var host = page.doms.cards;
		var i, n, o;
		// 分类按钮：「收藏」「全部」两个入口，再按目录里的顺序
		if (nav) {
			F.append(nav, catbtn("fav", "收藏", "web.cat.fav", "star", false));
			F.append(nav, catbtn("all", "全部", "web.cat.all", "border-all", true));
			for (i = 0; i < cats.length; i++)
				F.append(nav, catbtn(cats[i].id, cats[i].name, cats[i].key, cats[i].icon, false));
		}
		// 卡片：按目录顺序排，data-cat 决定它落进哪个分组
		if (host) {
			for (i = 0; i < tools.length; i++) F.append(host, toolcard(tools[i]));
		}
		return { cats: cats, tools: tools };
	}

	function catbtn(id, name, key, icon, on){
		var b = F.el("button");
		b.className = on ? "on" : "";
		b.type = "button";
		F.seta(b, "data-cat", id);
		F.seta(b, "data-title", name);
		F.seta(b, "data-kt", key);
		if (icon) {
			var i = F.el("i");
			i.className = "fa-solid fa-" + icon;
			F.append(b, i);
		}
		var s = F.el("span");
		F.seta(s, "data-k", key);
		s.textContent = name;
		F.append(b, s);
		return b;
	}

	// kind: tool 内嵌面板（data-tool）、page 独立页（data-page）、popup 弹窗页（data-popup）、
	// link 普通链接（data-link）。link 故意不用 data-page，否则托盘会多出一个和桌面重名的菜单。
	function toolcard(o){
		var node, i, s, star;
		var kind = o.kind || "tool";
		if (kind === "tool") {
			node = F.el("button");
			node.type = "button";
			F.seta(node, "data-tool", o.id);
		}
		else {
			node = F.el("a");
			F.seta(node, "href", o.path || ("/" + o.id));
			F.seta(node, kind === "popup" ? "data-popup" : kind === "page" ? "data-page" : "data-link", o.id);
		}
		F.seta(node, "data-cat", o.cat || "");
		F.seta(node, "data-name", o.search || o.name || "");
		if (o.title) node.title = o.title;
		if (o.icon) {
			i = F.el("i");
			i.className = "fa-solid fa-" + o.icon;
			F.append(node, i);
		}
		s = F.el("span");
		F.seta(s, "data-k", o.key || "");
		s.textContent = o.name || o.id;
		F.append(node, s);
		star = F.el("i");
		star.className = "star fa-solid fa-star";
		F.seta(star, "data-fav", o.id);
		star.title = "收藏";
		F.append(node, star);
		return node;
	}

	// 整页骨架在这里建一次。page.doms 收下所有带 name 的节点，后面代码直接用
	// SK.doms.boxBody 这样取，不再靠 HTML id。id 仍保留，给还在用 F.id 的工具兜底。
	SK.buildpage = function(){
		var page = { doms: {} };
		var nodes = F.CreateDOM(page, tree);
		var i, first;
		if (!(nodes instanceof Array)) nodes = [nodes];
		first = document.body.firstChild;
		for (i = 0; i < nodes.length; i++) document.body.insertBefore(nodes[i], first);
		SK.page = page;
		SK.doms = page.doms;
		F.buildtools(page);
		return page;
	};

	function addkids(page, dom, list){
		var i, v, sub;
		for (i = 0; i < list.length; i++) {
			v = list[i];
			if (v == null) continue;
			if (typeof v === "function") v = v();
			if (v instanceof Array) addkids(page, dom, v);
			else if (typeof v === "string") dom.appendChild(document.createTextNode(v));
			else if (typeof Element === "function" && v instanceof Element) dom.appendChild(v);
			else if (typeof v === "object") {
				sub = F.CreateDOM(page, v);
				if (sub instanceof Array) addkids(page, dom, sub);
				else if (sub) dom.appendChild(sub);
			}
		}
	}

	var tree = [
	{
		x:"header.topbar",
		c:[
		{
			x:"button#back.back",
			name:"back",
			"type":"button",
			"data-tool":"home",
			"hidden":true,
			c:[
			{x:"i.fa-solid.fa-arrow-left"},
			{x:"span","data-k":"web.back",c:"返回"}
		]
		},
		{
			x:"div.brand",
			c:[
			{x:"span.mark",c:"S"},
			{x:"strong",c:"ScreenKit"},
			{x:"small","data-k":"web.brand",c:"本机工具"}
		]
		},
		{
			x:"label.lang",
			c:[
			{x:"span","data-k":"web.f.lang",c:"语言"},
			{x:"select#lang",name:"lang",onchange:function(){ SK.onlang(); },}
		]
		},
		{
			x:"button#tool-fav.back",
			name:"toolFav",
			"type":"button",
			"data-ktip":"web.fav",
			"title":"收藏",
			"hidden":true,
			c:[
			{x:"i.fa-solid.fa-star"}
		]
		}
	]
	},
	{
		x:"div.shell",
		c:[
		{
			x:"main",
			c:[
			{
				x:"section#home.panel.on",
				name:"home",
				c:[
				{
					x:"div.head",
					c:[
					{
						x:"label.find",
						c:[
						{x:"input#tool-q",name:"toolQ","type":"search","data-kph":"web.find","placeholder":"搜索工具","autocomplete":"off",oninput:function(){ SK.paintcats(); },}
					]
					}
				]
				},
				{
					// 卡片和分类按钮都由 /sk/catalog.js 里的 ToolCatalog 生成，见 F.buildtools
					x:"div#cards.cards",
					name:"cards"
				},
				{x:"p#tool-none.none",name:"toolNone","hidden":true,"data-k":"web.find.none",c:"没有匹配的工具"},
				{
					// 分类按钮同样来自目录；「收藏」「全部」由脚本补在最前面
					x:"nav#cats",
					name:"cats",
					"hidden":true
				}
			]
			},
			{
				x:"section#chat.panel",
				name:"chat",
				c:[
				{x:"h1",c:"聊天室"},
				{
					x:"div#chat-gate",
					name:"chatGate",
					c:[
					{
						x:"div.row",
						c:[
						{
							x:"label",
							c:[
							{x:"span","data-k":"web.chat.nick",c:"昵称"},
							{x:"input#chat-nick",name:"chatNick","type":"text","maxlength":"16","autocomplete":"nickname"}
						]
						},
						{
							x:"label",
							c:[
							{x:"span","data-k":"web.chat.gender",c:"性别"},
							{
								x:"select#chat-gender",
								name:"chatGender",
								c:[
								{x:"option","value":"","data-k":"web.chat.secret",c:"保密"},
								{x:"option","value":"m","data-k":"web.chat.m",c:"男"},
								{x:"option","value":"f","data-k":"web.chat.f",c:"女"}
							]
							}
						]
						},
						{x:"button#chat-join.primary",name:"chatJoin","type":"button","data-k":"web.chat.join",c:"进入"},
						{x:"span#chat-msg.msg",name:"chatMsg"}
					]
					}
				]
				},
				{
					x:"div#chat-room",
					name:"chatRoom",
					"hidden":true,
					c:[
					{
						x:"div.chatmain",
						c:[
						{x:"div#chat-log",name:"chatLog"},
						{x:"aside#chat-peers",name:"chatPeers"}
					]
					},
					{x:"div#chat-offers",name:"chatOffers"},
					{
						x:"div.chatdock",
						c:[
						{
							x:"div#chat-torow.row",
							name:"chatTorow",
							c:[
							{x:"span.chatlab","data-k":"web.chat.to",c:"发给"},
							{
								x:"select#chat-to",
								name:"chatTo",
								c:[
								{x:"option","value":"","data-k":"web.chat.all",c:"所有人"}
							]
							},
							{
								x:"label.chatpriv",
								c:[
								{x:"input#chat-priv",name:"chatPriv","type":"checkbox"},
								{x:"span","data-k":"web.chat.priv",c:"私密"}
							]
							},
							{
								x:"button#chat-direct.ghost",
								name:"chatDirect",
								"type":"button",
								c:[
								{x:"i.fa-solid.fa-file-export"},
								{x:"span","data-k":"web.chat.direct",c:"直传"}
							]
							}
						]
						},
						{x:"div#chat-drafts",name:"chatDrafts", hidden:true},
						{
							x:"div#chat-sendrow.row",
							name:"chatSendrow",
							c:[
							{x:"input#chat-text",name:"chatText","type":"text","maxlength":"500","data-kph":"web.chat.ph","placeholder":"说点什么"},
							{
								x:"button#chat-send.primary",
								name:"chatSend",
								"type":"button",
								c:[
								{x:"i.fa-solid.fa-paper-plane"},
								{x:"span","data-k":"web.chat.send",c:"发送"}
							]
							},
							{
								x:"button#chat-imgbtn.ghost",
								name:"chatImgbtn",
								"type":"button",
								c:[
								{x:"i.fa-solid.fa-image"},
								{x:"span","data-k":"web.chat.image",c:"图片"}
							]
							},
							{
								x:"button#chat-filebtn.ghost",
								name:"chatFilebtn",
								"type":"button",
								c:[
								{x:"i.fa-solid.fa-paperclip"},
								{x:"span","data-k":"web.chat.file",c:"文件"}
							]
							},
							{
								x:"button#chat-leave.ghost",
								name:"chatLeave",
								"type":"button",
								c:[
								{x:"i.fa-solid.fa-right-from-bracket"},
								{x:"span","data-k":"web.chat.leave",c:"离开"}
							]
							},
							{x:"span.hint","data-k":"web.chat.paste",c:"Ctrl+V 粘贴图片或文件"},
							{x:"input#chat-img",name:"chatImg","type":"file","accept":"image/png,image/jpeg,image/gif,image/webp,image/bmp","hidden":true},
							{x:"input#chat-file",name:"chatFile","type":"file","hidden":true},
							{x:"input#chat-directfile",name:"chatDirectfile","type":"file","hidden":true}
						]
						}
					]
					}
				]
				}
			]
			},
			{
				x:"section#tr.panel",
				name:"tr",
				c:[
				{x:"h1",c:"翻译"},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.eng",c:"引擎"},
						{x:"select#tr-engine",name:"trEngine","data-ktip":"tr.engine.tip","title":"本地 ONNX、Chrome 或所选 LLM。LLM 提示词在「参数设置 → 翻译」。",onchange:function(){ if (SK.trfillmodel) SK.trfillmodel(); },}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.model",c:"模型"},
						{x:"select#tr-model",name:"trModel","data-ktip":"web.tr.model.tip","title":"只列本地 ONNX 模型；选定后源/目标语言切到该模型的方向",onchange:function(){ if (SK.trmodel) SK.trmodel(); },}
					]
					}
				]
				},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"tr.src",c:"源语言"},
						{x:"select#tr-src",name:"trSrc","data-ktip":"tr.src.tip","title":"先选源语言；「自动」在母语与学习语言之间互译（参数设置 → 翻译）",onchange:function(){ if (SK.trdst) SK.trdst(true); },}
					]
					},
					{
						x:"button#tr-swap.ghost",
						onclick:function(){ if (SK.trswap) SK.trswap(); },
						name:"trSwap",
						"type":"button",
						"data-ktip":"tr.swap.tip",
						"title":"交换源/目标语言；若有译文则与原文对调",
						c:[
						{x:"i.fa-solid.fa-right-left"},
						{x:"span","data-k":"tr.swap",c:"交换语言"}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"tr.dst",c:"目标语言"},
						{x:"select#tr-dst",name:"trDst","data-ktip":"tr.dst.tip","title":"根据已安装模型与源语言列出可选目标；自动=母语/学习语言互译",onchange:function(){ if (SK.trsync) SK.trsync(); },}
					]
					},
					{
						x:"button#tr-go.primary",
						onclick:function(){ if (SK.trgo) SK.trgo(); },
						name:"trGo",
						"type":"button",
						"data-ktip":"tr.go.tip",
						"title":"按所选方向翻译原文",
						c:[
						{x:"i.fa-solid.fa-language"},
						{x:"span","data-k":"tr.go",c:"翻译"}
					]
					},
					{x:"span#tr-msg.msg",name:"trMsg"}
				]
				},
				{
					x:"div.split.txpair",
					c:[
					{
						x:"div",
						c:[
						{
							x:"div.txhead",
							c:[
							{x:"span","data-k":"tr.src.text",c:"原文"},
							{
								x:"button#tr-speak-src.ghost",
								onclick:function(){ if (SK.trspeak) SK.trspeak("src"); },
								name:"trSpeakSrc",
								"type":"button",
								"data-ktip":"dict.speak",
								"title":"发音",
								c:[
								{x:"i.fa-solid.fa-volume-high"},
								{x:"span","data-k":"dict.speak",c:"发音"}
							]
							},
							{
								x:"button#tr-paste.ghost",
								onclick:function(){ if (SK.trpaste) SK.trpaste(); },
								name:"trPaste",
								"type":"button",
								"data-ktip":"tr.paste.tip",
								"title":"从剪贴板粘贴到原文",
								c:[
								{x:"i.fa-solid.fa-paste"},
								{x:"span","data-k":"tr.paste",c:"粘贴"}
							]
							}
						]
						},
						{x:"textarea#tr-in",name:"trIn","rows":"14","data-kph":"web.ph.src","placeholder":"要处理的文字",onkeydown:function(ev){ if (ev.key === "Enter" && (ev.ctrlKey || ev.metaKey)) { ev.preventDefault(); if (SK.trgo) SK.trgo(); } },}
					]
					},
					{
						x:"div",
						c:[
						{
							x:"div.txhead",
							c:[
							{x:"span","data-k":"tr.dst.text",c:"译文"},
							{
								x:"button#tr-speak-dst.ghost",
								onclick:function(){ if (SK.trspeak) SK.trspeak("dst"); },
								name:"trSpeakDst",
								"type":"button",
								"data-ktip":"dict.speak",
								"title":"发音",
								c:[
								{x:"i.fa-solid.fa-volume-high"},
								{x:"span","data-k":"dict.speak",c:"发音"}
							]
							},
							{
								x:"button#tr-copy.ghost",
								onclick:function(){ SK.copytext(SK.doms.trOut.value, "tr-msg"); },
								name:"trCopy",
								"type":"button",
								c:[
								{x:"i.fa-solid.fa-copy"},
								{x:"span","data-k":"tr.copy",c:"复制"}
							]
							}
						]
						},
						{x:"textarea#tr-out",name:"trOut","rows":"14","readonly":true}
					]
					}
				]
				},
				{x:"p.hint","data-k":"web.tr.hint",c:"Ctrl+Enter 翻译"}
			]
			},
			{
				x:"section#box.panel",
				name:"box",
				c:[
				{x:"h1#box-title",name:"boxTitle",c:"工具"},
				{x:"div#box-body",name:"boxBody"},
				{
					x:"div.row",
					c:[
					{x:"button#box-go.primary",name:"boxGo","type":"button","data-k":"web.go.calc",c:"计算",onclick:function(){ SK.utilrun(); },},
					{x:"button#box-copy.ghost",name:"boxCopy","type":"button","data-k":"web.copy.out",c:"复制结果",onclick:function(){ SK.copytext(SK.doms.boxOut.value, "box-msg"); },},
					{x:"span#box-msg.msg",name:"boxMsg"}
				]
				},
				{
					x:"label",
					c:[
					{x:"span","data-k":"web.result",c:"结果"},
					{x:"textarea#box-out",name:"boxOut","rows":"10","readonly":true},
					{x:"div#re-list",name:"reList","hidden":true},
					{x:"ul#rcolor-list",name:"rcolorList","hidden":true}
				]
				}
			]
			},
			{
				x:"section#cal.panel",
				name:"cal",
				c:[
				{x:"p.kicker",c:"Windows.Globalization.Calendar"},
				{x:"h1",c:"历法"},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.greg",c:"公历"},
						{x:"input#cal-date",name:"calDate","type":"date"}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.cat.time",c:"历法"},
						{
							x:"select#cal-id",
							name:"calId",
							c:[
							{x:"option","value":"lunar",c:"农历"},
							{x:"option","value":"gregorian",c:"公历"},
							{x:"option","value":"jp",c:"日本"},
							{x:"option","value":"jplunar",c:"日本旧历"},
							{x:"option","value":"tw",c:"民国"},
							{x:"option","value":"ko",c:"韩国"},
							{x:"option","value":"vilunar",c:"越南农历"},
							{x:"option","value":"he",c:"希伯来"},
							{x:"option","value":"hijri",c:"伊斯兰"},
							{x:"option","value":"umalqura",c:"乌姆库拉"},
							{x:"option","value":"fa",c:"波斯"},
							{x:"option","value":"th",c:"佛历"},
							{x:"option","value":"julian",c:"儒略历"}
						]
						}
					]
					},
					{
						x:"button#cal-go.primary",
						onclick:function(){ if (SK.cal) SK.cal(); },
						name:"calGo",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-calendar-check"},
						{x:"span","data-k":"web.go.unit",c:"换算"}
					]
					},
					{x:"span#cal-msg.msg",name:"calMsg"}
				]
				},
				{x:"dl#cal-out.facts",name:"calOut"}
			]
			},
			{
				x:"section#text.panel",
				name:"text",
				c:[
				{x:"p.kicker",c:"文本"},
				{x:"h1",c:"文本转换"},
				{
					x:"div.split.txpair",
					c:[
					{
						x:"div",
						c:[
						{
							x:"div.txhead",
							c:[
							{x:"span","data-k":"web.src",c:"原文"}
						]
						},
						{x:"textarea#tx-in",name:"txIn","rows":"14","data-kph":"web.ph.src","placeholder":"要处理的文字"}
					]
					},
					{
						x:"div",
						c:[
						{
							x:"div.txhead",
							c:[
							{x:"span","data-k":"web.result",c:"结果"},
							{
								x:"button#tx-swap.ghost",
								name:"txSwap",
								"type":"button",
								c:[
								{x:"i.fa-solid.fa-right-left"},
								{x:"span","data-k":"web.swap",c:"交换"}
							]
							},
							{
								x:"button#tx-copy.ghost",
								onclick:function(){ SK.copytext(SK.doms.txOut.value, "tx-msg"); },
								name:"txCopy",
								"type":"button",
								c:[
								{x:"i.fa-solid.fa-copy"},
								{x:"span","data-k":"web.copy",c:"复制"}
							]
							},
							{x:"span#tx-msg.msg",name:"txMsg"}
						]
						},
						{x:"textarea#tx-out",name:"txOut","rows":"14","readonly":true}
					]
					}
				]
				},
				{
					x:"div#tx-ops.ops",
					name:"txOps",
					c:[
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"Base64"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"b64enc",
								c:[
								{x:"i.fa-solid.fa-lock"},
								{x:"span","data-k":"web.op.enc",c:"编码"}
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"b64dec",
								c:[
								{x:"i.fa-solid.fa-lock-open"},
								{x:"span","data-k":"web.op.dec",c:"解码"}
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"URL"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"urlenc",
								c:[
								{x:"i.fa-solid.fa-link"},
								{x:"span","data-k":"web.op.enc",c:"编码"}
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"urldec",
								c:[
								{x:"i.fa-solid.fa-link-slash"},
								{x:"span","data-k":"web.op.dec",c:"解码"}
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"十六进制"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"utf8hex",
								c:[
								{x:"i.fa-solid.fa-hashtag"},
								"UTF-8"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"utf8unhex",
								c:[
								{x:"i.fa-solid.fa-rotate-left"},
								"还原 UTF-8"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"gbkhex",
								c:[
								{x:"i.fa-solid.fa-hashtag"},
								"GBK"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"gbkunhex",
								c:[
								{x:"i.fa-solid.fa-rotate-left"},
								"还原 GBK"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"Unicode"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"uesc",
								c:[
								{x:"i.fa-solid.fa-code"},
								"转义"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"uunesc",
								c:[
								{x:"i.fa-solid.fa-rotate-left"},
								"还原"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"大小写"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"upper",
								c:[
								{x:"i.fa-solid.fa-arrow-up-a-z"},
								{x:"span","data-k":"web.pw.up",c:"大写"}
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"lower",
								c:[
								{x:"i.fa-solid.fa-arrow-down-a-z"},
								{x:"span","data-k":"web.pw.low",c:"小写"}
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"简繁转换"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"trad",
								c:[
								{x:"i.fa-solid.fa-language"},
								"转为繁体"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"simp",
								c:[
								{x:"i.fa-solid.fa-language"},
								"转为简体"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"日文注音"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"yomi",
								c:[
								{x:"i.fa-solid.fa-comment"},
								"非逐字"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"yomimono",
								c:[
								{x:"i.fa-solid.fa-comment"},
								"逐字"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"JSON"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"json",
								c:[
								{x:"i.fa-solid.fa-code"},
								"美化"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"jsonmin",
								c:[
								{x:"i.fa-solid.fa-compress"},
								"压缩"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"jesc",
								c:[
								{x:"i.fa-solid.fa-code"},
								"转义"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"juesc",
								c:[
								{x:"i.fa-solid.fa-rotate-left"},
								"还原"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"XML"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"xml",
								c:[
								{x:"i.fa-solid.fa-code"},
								"格式化"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"xmlmin",
								c:[
								{x:"i.fa-solid.fa-compress"},
								"压缩"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"SQL"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"sql",
								c:[
								{x:"i.fa-solid.fa-database"},
								"格式化"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"sqlmin",
								c:[
								{x:"i.fa-solid.fa-compress"},
								"压缩"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"HTML"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"htmlenc",
								c:[
								{x:"i.fa-solid.fa-code"},
								{x:"span","data-k":"web.op.enc",c:"编码"}
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"htmldec",
								c:[
								{x:"i.fa-solid.fa-rotate-left"},
								{x:"span","data-k":"web.op.dec",c:"解码"}
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"hmin",
								c:[
								{x:"i.fa-solid.fa-file-code"},
								"压缩"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"hjs",
								c:[
								{x:"i.fa-solid.fa-quote-left"},
								"转 JS"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"代码"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"cmin",
								c:[
								{x:"i.fa-solid.fa-brush"},
								"CSS 压缩"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"jsmin",
								c:[
								{x:"i.fa-solid.fa-file-code"},
								"JS 压缩"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"哈希"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"md5",
								c:[
								{x:"i.fa-solid.fa-lock"},
								"MD5"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"sha",
								c:[
								{x:"i.fa-solid.fa-lock"},
								"SHA-256"
							]
							}
						]
						}
					]
					},
					{
						x:"div.opgrp",
						c:[
						{x:"span",c:"其它"},
						{
							x:"div",
							c:[
							{
								x:"button",
								"type":"button",
								"data-op":"name",
								c:[
								{x:"i.fa-solid.fa-signature"},
								"变量名转换"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"cron",
								c:[
								{x:"i.fa-solid.fa-clock"},
								"Crontab解析"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"ua",
								c:[
								{x:"i.fa-solid.fa-user"},
								"User-Agent解析"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"stats",
								c:[
								{x:"i.fa-solid.fa-chart-column"},
								"字符统计"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"tok",
								c:[
								{x:"i.fa-solid.fa-hashtag"},
								"token统计"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"collapse",
								c:[
								{x:"i.fa-solid.fa-compress"},
								"合并空白"
							]
							},
							{
								x:"button",
								"type":"button",
								"data-op":"dropempty",
								c:[
								{x:"i.fa-solid.fa-eraser"},
								"去掉空行"
							]
							}
						]
						}
					]
					}
				]
				}
			]
			},
			{
				x:"section#ocr.panel",
				name:"ocr",
				c:[
				{x:"p.kicker",c:"OCR"},
				{x:"h1",c:"文字识别"},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.eng",c:"引擎"},
						{x:"select#ocr-pack",name:"ocrPack",onchange:function(){ if (SK.fillocrmodels) SK.fillocrmodels(); },}
					]
					},
					{
						x:"label#ocr-model-lab",
						name:"ocrModelLab",
						c:[
						{x:"span","data-k":"web.f.packlang",c:"语言 / 模型"},
						{x:"select#ocr-model",name:"ocrModel"}
					]
					},
					{
						x:"label#ocr-dev-lab",
						name:"ocrDevLab",
						c:[
						{x:"span","data-k":"web.f.dev",c:"设备"},
						{
							x:"select#ocr-dev",
							name:"ocrDev",
							c:[
							{x:"option","value":"cpu",c:"CPU"},
							{x:"option","value":"gpu",c:"GPU (CUDA)"},
							{x:"option","value":"intel",c:"核显 (DirectML)"}
						]
						}
					]
					}
				]
				},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.img",c:"图片"},
						{x:"input#ocr-file",name:"ocrFile","type":"file","accept":"image/*",onchange:function(){ if (SK.onocrfile) SK.onocrfile(); },}
					]
					},
					{
						x:"button#ocr-go.primary",
						onclick:function(){ if (SK.ocrgo) SK.ocrgo(); },
						name:"ocrGo",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-eye"},
						{x:"span","data-k":"web.f.scan",c:"识别"}
					]
					},
					{
						x:"button#ocr-copy.ghost",
						onclick:function(){ SK.copytext(SK.doms.ocrOut.value, "ocr-msg"); },
						name:"ocrCopy",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-copy"},
						{x:"span","data-k":"web.copy.out",c:"复制结果"}
					]
					},
					{x:"span#ocr-msg.msg",name:"ocrMsg"}
				]
				},
				{x:"p.hint","data-k":"web.f.paste",c:"在此页按 Ctrl+V 可粘贴剪贴板里的图片。"},
				{x:"img#ocr-img",name:"ocrImg","alt":"预览","hidden":true},
				{x:"p#ocr-meta.ocr-meta",name:"ocrMeta","hidden":true},
				{
					x:"label",
					c:[
					{x:"span","data-k":"web.result",c:"结果"},
					{x:"textarea#ocr-out",name:"ocrOut","rows":"8","readonly":true}
				]
				}
			]
			},
			{
				x:"section#tts.panel",
				name:"tts",
				c:[
				{x:"p.kicker",c:"TTS"},
				{x:"h1",c:"语音合成"},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.eng",c:"引擎"},
						{x:"select#tts-eng",name:"ttsEng",onchange:function(){ if (SK.fillttsmodels) SK.fillttsmodels(); },}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.lang",c:"语言"},
						{x:"select#tts-lang",name:"ttsLang",onchange:function(){ if (SK.fillttsmodels) SK.fillttsmodels(); },}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.gender",c:"性别"},
						{
							x:"select#tts-gender",
							onchange:function(){ if (SK.fillttsmodels) SK.fillttsmodels(); },
							name:"ttsGender",
							c:[
							{x:"option","value":"",c:"全部性别"},
							{x:"option","value":"female",c:"女声"},
							{x:"option","value":"male",c:"男声"}
						]
						}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.model",c:"模型"},
						{x:"select#tts-model",name:"ttsModel",onchange:function(){ if (SK.fillttsvoices) SK.fillttsvoices(); },}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.voice",c:"发音人"},
						{x:"select#tts-voice",name:"ttsVoice"}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.speed",c:"语速"},
						{x:"input#tts-speed",name:"ttsSpeed","type":"number","min":"0.5","max":"2","step":"0.1","value":"1"}
					]
					}
				]
				},
				{
					x:"label",
					c:[
					{x:"span","data-k":"web.src",c:"原文"},
					{x:"textarea#tts-in",name:"ttsIn","rows":"6","data-kph":"web.ph.speak","placeholder":"要朗读的文字"}
				]
				},
				{
					x:"div.row",
					c:[
					{
						x:"button#tts-go.primary",
						onclick:function(){ if (SK.ttsgo) SK.ttsgo(false); },
						name:"ttsGo",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-volume-high"},
						{x:"span","data-k":"web.f.speak",c:"朗读"}
					]
					},
					{
						x:"button#tts-wav",
						onclick:function(){ if (SK.ttsgo) SK.ttsgo(true); },
						name:"ttsWav",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-file-audio"},
						{x:"span","data-k":"web.f.wav",c:"生成 WAV"}
					]
					},
					{x:"span#tts-msg.msg",name:"ttsMsg"}
				]
				},
				{x:"audio#tts-audio",name:"ttsAudio","controls":"","hidden":true}
			]
			},
			{
				x:"section#asr.panel",
				name:"asr",
				c:[
				{x:"p.kicker",c:"ASR"},
				{x:"h1",c:"语音识别"},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.eng",c:"引擎"},
						{
							x:"select#asr-eng",
							onchange:function(){ if (SK.fillasrmodels) SK.fillasrmodels(); },
							name:"asrEng",
							c:[
							{x:"option","value":"offline",c:"离线模型"},
							{x:"option","value":"windows",c:"Windows 语音识别"}
						]
						}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.model",c:"模型"},
						{x:"select#asr-model",name:"asrModel"}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.lang",c:"语言"},
						{
							x:"select#asr-lang",
							name:"asrLang",
							c:[
							{x:"option","value":"auto",c:"自动"},
							{x:"option","value":"zh",c:"中文"},
							{x:"option","value":"en",c:"英文"},
							{x:"option","value":"ja",c:"日文"},
							{x:"option","value":"ko",c:"韩文"},
							{x:"option","value":"yue",c:"粤语"}
						]
						}
					]
					}
				]
				},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.audio",c:"音频文件"},
						{x:"input#asr-file",name:"asrFile","type":"file","accept":"audio/*,video/webm"}
					]
					},
					{
						x:"button#asr-filego.primary",
						onclick:function(){ if (SK.asrfile) SK.asrfile(); },
						name:"asrFilego",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-file-audio"},
						{x:"span","data-k":"web.f.scanfile",c:"识别文件"}
					]
					},
					{
						x:"button#asr-rec",
						onclick:function(){ if (SK.asrrec) SK.asrrec(); },
						name:"asrRec",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-microphone"},
						{x:"span","data-k":"web.f.rec",c:"录音"}
					]
					},
					{
						x:"button#asr-copy.ghost",
						onclick:function(){ SK.copytext(SK.doms.asrOut.value, "asr-msg"); },
						name:"asrCopy",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-copy"},
						{x:"span","data-k":"web.copy.out",c:"复制结果"}
					]
					},
					{x:"span#asr-msg.msg",name:"asrMsg"}
				]
				},
				{
					x:"label",
					c:[
					{x:"span","data-k":"web.result",c:"结果"},
					{x:"textarea#asr-out",name:"asrOut","rows":"8","readonly":true}
				]
				}
			]
			},
			{
				x:"section#qr.panel",
				name:"qr",
				c:[
				{x:"p.kicker",c:"ZXing"},
				{x:"h1",c:"二维码生成"},
				{
					x:"div.row",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.kind",c:"类型"},
						{
							x:"select#qr-fmt",
							name:"qrFmt",
							c:[
							{x:"option","value":"qr",c:"QR Code"},
							{x:"option","value":"datamatrix",c:"Data Matrix"},
							{x:"option","value":"code128",c:"Code 128"},
							{x:"option","value":"code39",c:"Code 39"},
							{x:"option","value":"ean13",c:"EAN-13"},
							{x:"option","value":"ean8",c:"EAN-8"},
							{x:"option","value":"upca",c:"UPC-A"}
						]
						}
					]
					},
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.enc",c:"编码"},
						{x:"div.qrencs",c:[
							{x:"label.check",c:[{x:"input#qrEncUtf8",type:"radio",value:"utf8",checked:true},"UTF-8"]},
							{x:"label.check",c:[{x:"input#qrEncGbk",type:"radio",value:"gbk"},"GBK"]},
							{x:"label.check",c:[{x:"input#qrEncHex",type:"radio",value:"hex"},"Hex"]}
						]}
					]
					}
				]
				},
				{
					x:"label",
					c:[
					{x:"span","data-k":"web.f.content",c:"内容"},
					{x:"textarea#qr-in",name:"qrIn","rows":"4","data-kph":"web.ph.qr","placeholder":"要写进码里的文字"}
				]
				},
				{
					x:"div.row",
					c:[
					{
						x:"button#qr-make.primary",
						onclick:function(){ if (SK.qrmake) SK.qrmake(); },
						name:"qrMake",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-qrcode"},
						{x:"span","data-k":"web.go.gen",c:"生成"}
					]
					},
					{
						x:"label.check",
						c:[
						{x:"input#qr-capon",name:"qrCapon","type":"checkbox","checked":true},
						"带文字"
					]
					},
					{x:"span#qr-msg.msg",name:"qrMsg"}
				]
				},
				{
					x:"div.qrbox",
					c:[
					{x:"img#qr-img",name:"qrImg","alt":"","hidden":true},
					{x:"p#qr-cap.qrcap",name:"qrCap","hidden":true}
				]
				}
			]
			},
			{
				x:"section#qrs.panel",
				name:"qrs",
				c:[
				{x:"p.kicker",c:"ZXing"},
				{x:"h1",c:"二维码识别"},
				{
					x:"div.row.qrsrow",
					c:[
					{
						x:"label",
						c:[
						{x:"span","data-k":"web.f.img",c:"图片"},
						{x:"input#qrs-file",name:"qrsFile","type":"file","accept":"image/*"}
					]
					},
					{
						x:"button#qr-scan.primary",
						onclick:function(){ if (SK.qrscan) SK.qrscan(); },
						name:"qrScan",
						"type":"button",
						c:[
						{x:"i.fa-solid.fa-barcode"},
						{x:"span","data-k":"web.f.scan",c:"识别"}
					]
					},
					{x:"span#qrs-msg.msg",name:"qrsMsg"}
				]
				},
				{x:"p.hint","data-k":"web.f.paste",c:"在此页按 Ctrl+V 可粘贴剪贴板里的图片。"},
				{x:"img#qrs-img",name:"qrsImg","alt":"预览","hidden":true},
				{
					x:"label",
					c:[
					{x:"span","data-k":"web.result",c:"结果"},
					{x:"textarea#qrs-out",name:"qrsOut","rows":"6","readonly":true}
				]
				}
			]
			}
		]
		}
	]
	}
];
	SK.buildpage();
})();
