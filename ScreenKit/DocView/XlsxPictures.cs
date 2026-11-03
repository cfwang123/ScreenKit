using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ScreenKit.DocView;

/// <summary>
/// 从 xlsx / xls 取出工作表图片：绘图锚点、WPS/Excel DISPIMG、单元格富值图。
/// 字节在后台解析时收集，位图在 UI 线程解码。
/// </summary>
static class XlsxPictures {
	const double EMU2DIP = 96.0 / 914400.0;
	const int MAX_PICS = 400;
	const int MAX_ROW = 10000;
	const int MAX_COL = 200;
	const int MAX_BYTES = 48 * 1024 * 1024;

	public static void Load(WorksheetPart part, SheetModel model) {
		if (part == null || model == null) return;
		try {
			var list = model.Pictures ?? new List<SheetPicture>();
			var cache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			var wb = workbook(part);
			var before = list.Count;
			if (wb != null) {
				loadcellimages(wb, part, model, list, cache);
				loadrichimages(wb, part, model, list, cache);
			}
			loaddrawings(part, model, list, cache);
			if (list.Count == before) return;
			model.Pictures = list;
			for (var i = before; i < list.Count; i++)
				cover(model, list[i]);
			DocViewLog.Info($"sheet pictures n={list.Count - before}");
		} catch (Exception ex) {
			DocViewLog.Warn($"xlsx pictures: {ex.Message}");
		}
	}

	public static void LoadXls(ISheet sheet, SheetModel model) {
		if (sheet == null || model == null) return;
		try {
			if (sheet is not HSSFSheet hs) return;
			HSSFPatriarch pat = null;
			try { pat = hs.DrawingPatriarch as HSSFPatriarch; } catch { /* ignore */ }
			if (pat == null) {
				try { pat = hs.CreateDrawingPatriarch() as HSSFPatriarch; } catch { return; }
			}
			if (pat == null) return;
			var list = model.Pictures ?? new List<SheetPicture>();
			var before = list.Count;
			walkhssf(pat, list, model, 0);
			if (list.Count == before) return;
			model.Pictures = list;
			for (var i = before; i < list.Count; i++)
				cover(model, list[i]);
			DocViewLog.Info($"xls pictures n={list.Count - before}");
		} catch (Exception ex) {
			DocViewLog.Warn($"xls pictures: {ex.Message}");
		}
	}

	/// <summary>UI 线程：字节 → 冻结位图。失败则丢弃字节，避免反复解码。</summary>
	public static void EnsureBitmap(SheetPicture pic) {
		if (pic == null || pic.Bitmap != null) return;
		var bytes = pic.Bytes;
		if (bytes == null || bytes.Length < 16) return;
		try {
			using (var ms = new MemoryStream(bytes, false)) {
				var bi = new BitmapImage();
				bi.BeginInit();
				bi.CacheOption = BitmapCacheOption.OnLoad;
				bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				bi.StreamSource = ms;
				bi.EndInit();
				if (bi.CanFreeze) bi.Freeze();
				pic.Bitmap = bi;
			}
			pic.Bytes = null;
		} catch (Exception ex) {
			pic.Bytes = null;
			DocViewLog.Warn($"sheet picture decode: {ex.Message}");
		}
	}

	static WorkbookPart workbook(WorksheetPart part) {
		if (part?.OpenXmlPackage is SpreadsheetDocument doc)
			return doc.WorkbookPart;
		return null;
	}

	static void loaddrawings(WorksheetPart part, SheetModel model, List<SheetPicture> list, Dictionary<string, byte[]> cache) {
		DrawingsPart dp;
		try { dp = part.DrawingsPart; } catch { return; }
		var root = dp?.WorksheetDrawing;
		if (root == null) return;
		foreach (var node in topelements(root))
			addanchor(node, dp, model, list, cache);
	}

	static IEnumerable<OpenXmlElement> topelements(OpenXmlElement root) {
		foreach (var ch in root.ChildElements) {
			if (ch is AlternateContent ac) {
				var choice = ac.GetFirstChild<AlternateContentChoice>();
				if (choice == null) continue;
				foreach (var c in choice.ChildElements)
					yield return c;
			} else yield return ch;
		}
	}

	static void addanchor(OpenXmlElement node, OpenXmlPart host, SheetModel model, List<SheetPicture> list, Dictionary<string, byte[]> cache) {
		if (list.Count >= MAX_PICS || node == null) return;
		Xdr.FromMarker from = null;
		Xdr.ToMarker to = null;
		Xdr.Extent ext = null;
		Xdr.Position pos = null;
		var kind = 2;
		var edit = 0;
		if (node is Xdr.TwoCellAnchor two) {
			kind = 0;
			from = two.FromMarker;
			to = two.ToMarker;
			edit = editas(two);
		} else if (node is Xdr.OneCellAnchor one) {
			kind = 1;
			from = one.FromMarker;
			ext = one.Extent;
		} else if (node is Xdr.AbsoluteAnchor abs) {
			kind = 2;
			pos = abs.Position;
			ext = abs.Extent;
		} else return;

		if (!anchorbox(model, kind, from, to, ext, pos, out var box)) return;
		foreach (var pic in node.Descendants<Xdr.Picture>()) {
			if (list.Count >= MAX_PICS) return;
			addpic(pic, node, host, model, list, cache, kind, edit, from, to, box);
		}
	}

	static void addpic(Xdr.Picture pic, OpenXmlElement anchor, OpenXmlPart host, SheetModel model,
		List<SheetPicture> list, Dictionary<string, byte[]> cache, int kind, int edit,
		Xdr.FromMarker from, Xdr.ToMarker to, Box box) {
		var bytes = blipbytes(pic, host, cache);
		if (bytes == null) return;
		var sp = new SheetPicture {
			Bytes = bytes,
			Name = picname(pic),
		};
		applyxfrm(pic, sp);
		applycrop(pic, sp);
		if (ingroup(pic, anchor) && picbox(pic, anchor, box, out var mapped)) {
			sp.Kind = 2;
			sp.AbsX = mapped.X;
			sp.AbsY = mapped.Y;
			sp.AbsW = mapped.W;
			sp.AbsH = mapped.H;
			clampabs(sp);
			list.Add(sp);
			return;
		}
		if (kind == 0 && readmarker(from, out var fc, out var fr, out var fx, out var fy)
			&& readmarker(to, out var tc, out var tr, out var tx, out var ty)) {
			if (edit == 1) {
				sp.Kind = 1;
				sp.FromCol = fc;
				sp.FromRow = fr;
				sp.FromX = fx;
				sp.FromY = fy;
				sp.AbsW = Math.Abs(box.W);
				sp.AbsH = Math.Abs(box.H);
			} else if (edit == 2) {
				sp.Kind = 2;
				sp.AbsX = Math.Min(box.X, box.X + box.W);
				sp.AbsY = Math.Min(box.Y, box.Y + box.H);
				sp.AbsW = Math.Abs(box.W);
				sp.AbsH = Math.Abs(box.H);
			} else {
				sp.Kind = 0;
				sp.FromCol = fc;
				sp.FromRow = fr;
				sp.ToCol = tc;
				sp.ToRow = tr;
				sp.FromX = fx;
				sp.FromY = fy;
				sp.ToX = tx;
				sp.ToY = ty;
			}
		} else if (kind == 1 && readmarker(from, out fc, out fr, out fx, out fy)) {
			sp.Kind = 1;
			sp.FromCol = fc;
			sp.FromRow = fr;
			sp.FromX = fx;
			sp.FromY = fy;
			sp.AbsW = box.W;
			sp.AbsH = box.H;
		} else {
			sp.Kind = 2;
			sp.AbsX = box.X;
			sp.AbsY = box.Y;
			sp.AbsW = box.W;
			sp.AbsH = box.H;
		}
		clampabs(sp);
		if (sp.Kind != 0 && (sp.AbsW < 1 || sp.AbsH < 1)) return;
		list.Add(sp);
	}

	static int editas(Xdr.TwoCellAnchor two) {
		try {
			if (two?.EditAs == null || !two.EditAs.HasValue) return 0;
			var v = two.EditAs.Value;
			if (v == Xdr.EditAsValues.OneCell) return 1;
			if (v == Xdr.EditAsValues.Absolute) return 2;
		} catch { /* ignore */ }
		return 0;
	}

	static bool ingroup(Xdr.Picture pic, OpenXmlElement anchor) {
		var p = pic.Parent;
		while (p != null && !ReferenceEquals(p, anchor)) {
			if (p is Xdr.GroupShape) return true;
			p = p.Parent;
		}
		return false;
	}

	/// <summary>组合内图片：子坐标相对组，再映射到锚点矩形（DIP）。</summary>
	static bool picbox(Xdr.Picture pic, OpenXmlElement anchor, Box anchorBox, out Box box) {
		box = anchorBox;
		var xfrm = pic.ShapeProperties?.Transform2D;
		if (xfrm?.Offset == null || xfrm.Extents == null) return false;
		if (!longval(xfrm.Offset.X, out var x)) return false;
		if (!longval(xfrm.Offset.Y, out var y)) return false;
		if (!longval(xfrm.Extents.Cx, out var w)) return false;
		if (!longval(xfrm.Extents.Cy, out var h)) return false;
		foreach (var g in pic.Ancestors<Xdr.GroupShape>()) {
			var tg = g.GroupShapeProperties?.TransformGroup;
			if (tg == null) continue;
			var chx = pointx(tg.ChildOffset);
			var chy = pointy(tg.ChildOffset);
			var chw = sizecx(tg.ChildExtents);
			var chh = sizecy(tg.ChildExtents);
			if (chw < 1 || chh < 1) continue;
			var nx = (x - chx) / chw;
			var ny = (y - chy) / chh;
			var nw = w / chw;
			var nh = h / chh;
			if (groupfillsanchor(g, anchor)) {
				box = new Box {
					X = anchorBox.X + nx * anchorBox.W,
					Y = anchorBox.Y + ny * anchorBox.H,
					W = nw * anchorBox.W,
					H = nh * anchorBox.H,
				};
				return box.W > 1 && box.H > 1;
			}
			var gx = pointx(tg.Offset);
			var gy = pointy(tg.Offset);
			var gw = sizecx(tg.Extents);
			var gh = sizecy(tg.Extents);
			x = gx + nx * gw;
			y = gy + ny * gh;
			w = nw * gw;
			h = nh * gh;
		}
		return false;
	}

	static bool groupfillsanchor(Xdr.GroupShape g, OpenXmlElement anchor) {
		var p = g.Parent;
		while (p != null && !ReferenceEquals(p, anchor)) {
			if (p is Xdr.GroupShape) return false;
			p = p.Parent;
		}
		return ReferenceEquals(p, anchor);
	}

	static bool anchorbox(SheetModel model, int kind, Xdr.FromMarker from, Xdr.ToMarker to, Xdr.Extent ext, Xdr.Position pos, out Box box) {
		box = new Box();
		if (kind == 0) {
			if (!readmarker(from, out var fc, out var fr, out var fx, out var fy)) return false;
			if (!readmarker(to, out var tc, out var tr, out var tx, out var ty)) return false;
			var x1 = sheetx(model, fc, fx);
			var y1 = sheety(model, fr, fy);
			var x2 = sheetx(model, tc, tx);
			var y2 = sheety(model, tr, ty);
			box.X = Math.Min(x1, x2);
			box.Y = Math.Min(y1, y2);
			box.W = Math.Abs(x2 - x1);
			box.H = Math.Abs(y2 - y1);
			return box.W > 0.5 && box.H > 0.5;
		}
		if (kind == 1) {
			if (!readmarker(from, out var fc, out var fr, out var fx, out var fy)) return false;
			box.X = sheetx(model, fc, fx);
			box.Y = sheety(model, fr, fy);
			box.W = extlen(ext?.Cx);
			box.H = extlen(ext?.Cy);
			return box.W > 0.5 && box.H > 0.5;
		}
		box.X = extlen(pos?.X);
		box.Y = extlen(pos?.Y);
		box.W = extlen(ext?.Cx);
		box.H = extlen(ext?.Cy);
		return box.W > 0.5 && box.H > 0.5;
	}

	static void loadcellimages(WorkbookPart wb, WorksheetPart sheet, SheetModel model, List<SheetPicture> list, Dictionary<string, byte[]> cache) {
		var parts = allparts(wb);
		OpenXmlPart cellPart = null;
		foreach (var p in parts) {
			var uri = p.Uri?.ToString() ?? "";
			if (uri.IndexOf("cellimage", StringComparison.OrdinalIgnoreCase) >= 0) {
				cellPart = p;
				break;
			}
		}
		if (cellPart == null && anydispimg(sheet))
			cellPart = findcellimagesroot(parts);
		if (cellPart == null) return;
		Dictionary<string, byte[]> map;
		try { map = parsecellimages(cellPart, cache); }
		catch (Exception ex) {
			DocViewLog.Warn($"cellimages: {ex.Message}");
			return;
		}
		if (map == null || map.Count == 0) return;
		var ws = sheet.Worksheet;
		if (ws == null) return;
		foreach (var cell in ws.Descendants<Cell>()) {
			if (list.Count >= MAX_PICS) return;
			var id = dispimgid(cell.CellFormula?.Text);
			if (id == null) id = dispimgid(cell.CellValue?.InnerText);
			if (id == null) id = dispimgid(cell.InnerText);
			if (id == null) continue;
			if (!map.TryGetValue(id, out var bytes) || bytes == null) continue;
			if (!trya1(cell.CellReference?.Value, out var r, out var c)) continue;
			addfill(model, list, r, c, bytes, id);
		}
	}

	static bool anydispimg(WorksheetPart sheet) {
		var ws = sheet?.Worksheet;
		if (ws == null) return false;
		foreach (var f in ws.Descendants<CellFormula>()) {
			var t = f?.Text;
			if (t != null && t.IndexOf("DISPIMG", StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
		}
		return false;
	}

	static OpenXmlPart findcellimagesroot(List<OpenXmlPart> parts) {
		foreach (var p in parts) {
			var uri = p.Uri?.ToString() ?? "";
			var ct = p.ContentType ?? "";
			if (ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
			if (uri.IndexOf("sharedStrings", StringComparison.OrdinalIgnoreCase) >= 0) continue;
			if (uri.IndexOf("theme", StringComparison.OrdinalIgnoreCase) >= 0) continue;
			if (uri.IndexOf("/worksheets/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
			try {
				using var s = p.GetStream();
				if (s.CanSeek && s.Length > 1000000) continue;
				var doc = XDocument.Load(s);
				if (doc.Root != null && string.Equals(doc.Root.Name.LocalName, "cellImages", StringComparison.OrdinalIgnoreCase))
					return p;
			} catch { /* 非 xml */ }
		}
		return null;
	}

	static Dictionary<string, byte[]> parsecellimages(OpenXmlPart part, Dictionary<string, byte[]> cache) {
		var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		var doc = xdoc(part);
		if (doc?.Root == null) return map;
		foreach (var pic in doc.Descendants()) {
			if (!string.Equals(pic.Name.LocalName, "pic", StringComparison.Ordinal)) continue;
			string name = null;
			string embed = null;
			foreach (var el in pic.Descendants()) {
				if (name == null && string.Equals(el.Name.LocalName, "cNvPr", StringComparison.Ordinal))
					name = el.Attribute("name")?.Value;
				if (embed == null && string.Equals(el.Name.LocalName, "blip", StringComparison.Ordinal)) {
					foreach (var at in el.Attributes()) {
						if (at.Name.LocalName == "embed" || at.Name.LocalName == "link") {
							embed = at.Value;
							break;
						}
					}
				}
			}
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(embed)) continue;
			var bytes = readrel(part, embed, cache);
			if (bytes != null) map[name] = bytes;
		}
		return map;
	}

	static void loadrichimages(WorkbookPart wb, WorksheetPart sheet, SheetModel model, List<SheetPicture> list, Dictionary<string, byte[]> cache) {
		var parts = allparts(wb);
		OpenXmlPart meta = null, rels = null, values = null, structure = null;
		foreach (var p in parts) {
			var uri = p.Uri?.ToString() ?? "";
			var name = uri.Replace('\\', '/');
			if (name.EndsWith("/metadata.xml", StringComparison.OrdinalIgnoreCase)) meta = p;
			else if (name.IndexOf("richValueRel", StringComparison.OrdinalIgnoreCase) >= 0) rels = p;
			else if (name.IndexOf("rdrichvaluestructure", StringComparison.OrdinalIgnoreCase) >= 0) structure = p;
			else if (name.IndexOf("rdrichvalue.xml", StringComparison.OrdinalIgnoreCase) >= 0) values = p;
		}
		if (meta == null || rels == null || values == null) return;
		List<byte[]> byVm;
		try { byVm = parserich(meta, rels, values, structure, cache); }
		catch (Exception ex) {
			DocViewLog.Warn($"rich images: {ex.Message}");
			return;
		}
		if (byVm == null || byVm.Count == 0) return;
		var ws = sheet.Worksheet;
		if (ws == null) return;
		foreach (var cell in ws.Descendants<Cell>()) {
			if (list.Count >= MAX_PICS) return;
			if (cell.ValueMetaIndex == null || !cell.ValueMetaIndex.HasValue) continue;
			var vm = (int)cell.ValueMetaIndex.Value;
			if (vm < 1 || vm > byVm.Count) continue;
			var bytes = byVm[vm - 1];
			if (bytes == null) continue;
			if (!trya1(cell.CellReference?.Value, out var r, out var c)) continue;
			addfill(model, list, r, c, bytes, "cell");
		}
	}

	static List<byte[]> parserich(OpenXmlPart meta, OpenXmlPart rels, OpenXmlPart values, OpenXmlPart structure, Dictionary<string, byte[]> cache) {
		var relIds = new List<string>();
		var relDoc = xdoc(rels);
		if (relDoc?.Root != null) {
			foreach (var el in relDoc.Descendants()) {
				if (!string.Equals(el.Name.LocalName, "rel", StringComparison.Ordinal)) continue;
				string id = null;
				foreach (var at in el.Attributes())
					if (at.Name.LocalName == "id") { id = at.Value; break; }
				relIds.Add(id);
			}
		}
		var structs = new List<string[]>();
		if (structure != null) {
			var sd = xdoc(structure);
			var root = sd?.Root;
			if (root != null) {
				foreach (var s in root.Elements()) {
					if (!string.Equals(s.Name.LocalName, "s", StringComparison.Ordinal)) continue;
					var keys = new List<string>();
					foreach (var k in s.Elements()) {
						if (!string.Equals(k.Name.LocalName, "k", StringComparison.Ordinal)) continue;
						keys.Add(k.Attribute("n")?.Value ?? "");
					}
					structs.Add(keys.ToArray());
				}
			}
		}
		var relIndex = new List<int>();
		var vd = xdoc(values);
		if (vd?.Root != null) {
			foreach (var rv in vd.Root.Elements()) {
				if (!string.Equals(rv.Name.LocalName, "rv", StringComparison.Ordinal)) continue;
				var sidx = 0;
				var sat = rv.Attribute("s")?.Value;
				if (sat != null) int.TryParse(sat, NumberStyles.Integer, CultureInfo.InvariantCulture, out sidx);
				var nums = new List<int>();
				foreach (var v in rv.Elements()) {
					if (!string.Equals(v.Name.LocalName, "v", StringComparison.Ordinal)) continue;
					if (int.TryParse(v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
						nums.Add(n);
					else nums.Add(-1);
				}
				var field = -1;
				if (sidx >= 0 && sidx < structs.Count) {
					var keys = structs[sidx];
					for (var i = 0; i < keys.Length; i++) {
						var kn = keys[i] ?? "";
						if (kn.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0
							|| kn.IndexOf("LocalImage", StringComparison.OrdinalIgnoreCase) >= 0) {
							field = i;
							break;
						}
					}
				}
				if (field < 0) {
					for (var i = 0; i < nums.Count; i++) {
						if (nums[i] >= 0 && nums[i] < relIds.Count) { field = i; break; }
					}
				}
				relIndex.Add(field >= 0 && field < nums.Count ? nums[field] : -1);
			}
		}
		var vmToRv = new List<int>();
		var md = xdoc(meta);
		XElement vmRoot = null;
		if (md?.Root != null) {
			foreach (var el in md.Descendants()) {
				if (string.Equals(el.Name.LocalName, "valueMetadata", StringComparison.Ordinal)) {
					vmRoot = el;
					break;
				}
			}
		}
		if (vmRoot != null) {
			foreach (var bk in vmRoot.Elements()) {
				if (!string.Equals(bk.Name.LocalName, "bk", StringComparison.Ordinal)) continue;
				var idx = -1;
				foreach (var rc in bk.Descendants()) {
					if (!string.Equals(rc.Name.LocalName, "rc", StringComparison.Ordinal)) continue;
					var vs = rc.Attribute("v")?.Value;
					if (vs != null && int.TryParse(vs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
						idx = n;
					break;
				}
				vmToRv.Add(idx);
			}
		}
		var result = new List<byte[]>(vmToRv.Count);
		for (var i = 0; i < vmToRv.Count; i++) {
			byte[] bytes = null;
			var rv = vmToRv[i];
			if (rv >= 0 && rv < relIndex.Count) {
				var ri = relIndex[rv];
				if (ri >= 0 && ri < relIds.Count && !string.IsNullOrEmpty(relIds[ri]))
					bytes = readrel(rels, relIds[ri], cache);
			}
			result.Add(bytes);
		}
		return result;
	}

	static void addfill(SheetModel model, List<SheetPicture> list, int row, int col, byte[] bytes, string name) {
		if (bytes == null || row < 0 || col < 0 || row >= MAX_ROW || col >= MAX_COL) return;
		for (var i = 0; i < list.Count; i++) {
			var old = list[i];
			if (old != null && old.Kind == 3 && old.FromRow == row && old.FromCol == col)
				return;
		}
		list.Add(new SheetPicture {
			Kind = 3,
			FromCol = col,
			FromRow = row,
			Bytes = bytes,
			Name = name,
		});
		clearformula(model, row, col);
	}

	static void clearformula(SheetModel model, int row, int col) {
		var sc = model.CellAt(row, col);
		if (sc == null || ReferenceEquals(sc, SheetCell.SharedEmpty)) return;
		var t = sc.Text ?? "";
		if (t.Length == 0) return;
		if (t.IndexOf("DISPIMG", StringComparison.OrdinalIgnoreCase) < 0 && t != "#VALUE!")
			return;
		var w = model.EnsureCell(row, col);
		if (w == null || ReferenceEquals(w, SheetCell.SharedEmpty)) return;
		var wt = w.Text ?? "";
		if (wt.IndexOf("DISPIMG", StringComparison.OrdinalIgnoreCase) >= 0 || wt == "#VALUE!")
			w.Text = "";
	}

	static string dispimgid(string text) {
		if (string.IsNullOrEmpty(text)) return null;
		var i = text.IndexOf("DISPIMG", StringComparison.OrdinalIgnoreCase);
		if (i < 0) return null;
		var q1 = text.IndexOf('"', i);
		if (q1 < 0) return null;
		var q2 = text.IndexOf('"', q1 + 1);
		if (q2 <= q1 + 1) return null;
		return text.Substring(q1 + 1, q2 - q1 - 1);
	}

	static bool trya1(string a1, out int row, out int col) {
		row = col = 0;
		if (string.IsNullOrWhiteSpace(a1)) return false;
		a1 = a1.Trim().Trim('$');
		var i = 0;
		while (i < a1.Length && char.IsLetter(a1[i])) i++;
		if (i == 0 || i >= a1.Length) return false;
		var c = 0;
		for (var k = 0; k < i; k++)
			c = c * 26 + (char.ToUpperInvariant(a1[k]) - 'A' + 1);
		c--;
		if (!int.TryParse(a1.Substring(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) || r < 1)
			return false;
		row = r - 1;
		col = c;
		return row >= 0 && col >= 0;
	}

	static void walkhssf(HSSFShapeContainer box, List<SheetPicture> list, SheetModel model, int depth) {
		if (box == null || depth > 8 || list.Count >= MAX_PICS) return;
		var children = box.Children;
		if (children == null) return;
		foreach (var obj in children) {
			if (list.Count >= MAX_PICS) return;
			if (obj is HSSFPicture pic) addhssf(pic, list, model);
			else if (obj is HSSFShapeContainer nest && !ReferenceEquals(nest, box))
				walkhssf(nest, list, model, depth + 1);
		}
	}

	static void addhssf(HSSFPicture pic, List<SheetPicture> list, SheetModel model) {
		var data = pic?.PictureData?.Data;
		if (data == null || data.Length < 16 || data.Length > MAX_BYTES) return;
		var anc = pic.ClientAnchor;
		if (anc == null) return;
		var c1 = anc.Col1;
		var r1 = anc.Row1;
		var c2 = anc.Col2;
		var r2 = anc.Row2;
		if (c1 < 0) c1 = 0;
		if (r1 < 0) r1 = 0;
		if (c2 < 0) c2 = 0;
		if (r2 < 0) r2 = 0;
		list.Add(new SheetPicture {
			Kind = 0,
			Bytes = data,
			Name = pic.FileName,
			FromCol = c1,
			FromRow = r1,
			ToCol = c2,
			ToRow = r2,
			FromX = colw(model, c1) * frac1024(anc.Dx1),
			FromY = rowh(model, r1) * frac256(anc.Dy1),
			ToX = colw(model, c2) * frac1024(anc.Dx2),
			ToY = rowh(model, r2) * frac256(anc.Dy2),
		});
	}

	static double frac1024(int dx) {
		if (dx < 0) dx = 0;
		if (dx > 1023) dx = 1023;
		return dx / 1024.0;
	}

	static double frac256(int dy) {
		if (dy < 0) dy = 0;
		if (dy > 255) dy = 255;
		return dy / 256.0;
	}

	static byte[] blipbytes(Xdr.Picture pic, OpenXmlPart host, Dictionary<string, byte[]> cache) {
		var blip = pic?.BlipFill?.Blip ?? pic?.Descendants<A.Blip>().FirstOrDefault();
		if (blip == null) return null;
		var id = blip.Embed?.Value;
		if (string.IsNullOrEmpty(id)) id = blip.Link?.Value;
		return readrel(host, id, cache);
	}

	static byte[] readrel(OpenXmlPart host, string id, Dictionary<string, byte[]> cache) {
		if (host == null || string.IsNullOrEmpty(id)) return null;
		var key = (host.Uri?.ToString() ?? "") + "|" + id;
		if (cache.TryGetValue(key, out var hit)) return hit;
		byte[] bytes = null;
		try {
			var p = host.GetPartById(id);
			if (p != null) {
				using var s = p.GetStream();
				using var ms = new MemoryStream();
				s.CopyTo(ms);
				if (ms.Length > 0 && ms.Length <= MAX_BYTES)
					bytes = ms.ToArray();
			}
		} catch (Exception ex) {
			DocViewLog.Warn($"sheet picture rel: {ex.Message}");
		}
		cache[key] = bytes;
		return bytes;
	}

	static void applyxfrm(Xdr.Picture pic, SheetPicture sp) {
		var xfrm = pic?.ShapeProperties?.Transform2D;
		if (xfrm == null) return;
		try {
			if (xfrm.Rotation != null && xfrm.Rotation.HasValue)
				sp.Rot = xfrm.Rotation.Value / 60000.0;
		} catch { /* ignore */ }
		try {
			sp.FlipH = xfrm.HorizontalFlip != null && xfrm.HorizontalFlip.HasValue && xfrm.HorizontalFlip.Value;
			sp.FlipV = xfrm.VerticalFlip != null && xfrm.VerticalFlip.HasValue && xfrm.VerticalFlip.Value;
		} catch { /* ignore */ }
	}

	static void applycrop(Xdr.Picture pic, SheetPicture sp) {
		A.SourceRectangle src = null;
		try { src = pic?.Descendants<A.SourceRectangle>().FirstOrDefault(); } catch { return; }
		if (src == null) return;
		sp.CropL = cropv(src.Left);
		sp.CropT = cropv(src.Top);
		sp.CropR = cropv(src.Right);
		sp.CropB = cropv(src.Bottom);
		if (sp.CropL + sp.CropR >= 0.95) { sp.CropL = 0; sp.CropR = 0; }
		if (sp.CropT + sp.CropB >= 0.95) { sp.CropT = 0; sp.CropB = 0; }
	}

	static double cropv(Int32Value v) {
		if (v == null || !v.HasValue) return 0;
		var d = v.Value / 100000.0;
		if (d < 0) d = 0;
		if (d > 0.9) d = 0.9;
		return d;
	}

	static string picname(OpenXmlElement pic) {
		if (pic == null) return null;
		foreach (var el in pic.Descendants()) {
			if (!string.Equals(el.LocalName, "cNvPr", StringComparison.Ordinal)) continue;
			foreach (var at in el.GetAttributes())
				if (at.LocalName == "name" && !string.IsNullOrEmpty(at.Value))
					return at.Value;
		}
		return null;
	}

	static bool readmarker(OpenXmlElement m, out int col, out int row, out double ox, out double oy) {
		col = row = 0;
		ox = oy = 0;
		if (m == null) return false;
		if (!int.TryParse(childtext(m, "col"), NumberStyles.Integer, CultureInfo.InvariantCulture, out col))
			return false;
		if (!int.TryParse(childtext(m, "row"), NumberStyles.Integer, CultureInfo.InvariantCulture, out row))
			return false;
		long.TryParse(childtext(m, "colOff"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cx);
		long.TryParse(childtext(m, "rowOff"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cy);
		ox = cx * EMU2DIP;
		oy = cy * EMU2DIP;
		if (col < 0) col = 0;
		if (row < 0) row = 0;
		return true;
	}

	static string childtext(OpenXmlElement parent, string local) {
		foreach (var c in parent.ChildElements)
			if (string.Equals(c.LocalName, local, StringComparison.Ordinal))
				return c.InnerText;
		return null;
	}

	static double extlen(Int64Value v) {
		if (v == null || !v.HasValue) return 0;
		var d = v.Value * EMU2DIP;
		if (d < 0) d = 0;
		return d;
	}

	static bool longval(Int64Value v, out double n) {
		n = 0;
		if (v == null || !v.HasValue) return false;
		n = v.Value;
		return true;
	}

	static double pointx(A.Offset o) => o?.X != null && o.X.HasValue ? o.X.Value : 0;
	static double pointy(A.Offset o) => o?.Y != null && o.Y.HasValue ? o.Y.Value : 0;
	static double pointx(A.ChildOffset o) => o?.X != null && o.X.HasValue ? o.X.Value : 0;
	static double pointy(A.ChildOffset o) => o?.Y != null && o.Y.HasValue ? o.Y.Value : 0;
	static double sizecx(A.Extents e) => e?.Cx != null && e.Cx.HasValue ? e.Cx.Value : 0;
	static double sizecy(A.Extents e) => e?.Cy != null && e.Cy.HasValue ? e.Cy.Value : 0;
	static double sizecx(A.ChildExtents e) => e?.Cx != null && e.Cx.HasValue ? e.Cx.Value : 0;
	static double sizecy(A.ChildExtents e) => e?.Cy != null && e.Cy.HasValue ? e.Cy.Value : 0;

	static void clampabs(SheetPicture sp) {
		if (sp.AbsW > 12000) sp.AbsW = 12000;
		if (sp.AbsH > 12000) sp.AbsH = 12000;
		if (sp.AbsW < 0) sp.AbsW = 0;
		if (sp.AbsH < 0) sp.AbsH = 0;
	}

	static void cover(SheetModel model, SheetPicture p) {
		if (p == null) return;
		if (p.Kind == 3 || p.Kind == 0) {
			var r = Math.Max(p.FromRow, p.ToRow);
			var c = Math.Max(p.FromCol, p.ToCol);
			if (p.Kind == 3) { r = p.FromRow; c = p.FromCol; }
			if (r >= MAX_ROW) r = MAX_ROW - 1;
			if (c >= MAX_COL) c = MAX_COL - 1;
			if (r >= 0 && c >= 0) model.EnsureCell(r, c);
			if (p.Kind == 0) return;
		}
		double x, y, w, h;
		if (p.Kind == 1) {
			x = sheetx(model, p.FromCol, p.FromX);
			y = sheety(model, p.FromRow, p.FromY);
			w = p.AbsW;
			h = p.AbsH;
		} else if (p.Kind == 2) {
			x = p.AbsX;
			y = p.AbsY;
			w = p.AbsW;
			h = p.AbsH;
		} else return;
		coverpoint(model, x + Math.Max(0, w), y + Math.Max(0, h));
	}

	static void coverpoint(SheetModel model, double right, double bottom) {
		if (right < 1 && bottom < 1) return;
		var y = 0.0;
		var r = 0;
		var rh = model.RowHeights;
		var nr = rh?.Length ?? 0;
		while (r < nr && y + 1 < bottom && r < MAX_ROW) {
			var h = rh[r];
			if (h > 0) y += h;
			r++;
		}
		var guard = 0;
		while (y + 1 < bottom && r < MAX_ROW && guard++ < MAX_ROW) {
			model.EnsureCell(r, 0);
			rh = model.RowHeights;
			var h = (rh != null && r < rh.Length && rh[r] > 0) ? rh[r] : 20;
			y += h;
			r++;
		}
		var x = 0.0;
		var c = 0;
		var cw = model.ColWidths;
		var nc = cw?.Length ?? 0;
		while (c < nc && x + 1 < right && c < MAX_COL) {
			var w = cw[c];
			if (w > 0) x += w;
			c++;
		}
		guard = 0;
		while (x + 1 < right && c < MAX_COL && guard++ < MAX_COL) {
			model.EnsureCell(0, c);
			cw = model.ColWidths;
			var w = (cw != null && c < cw.Length && cw[c] > 0) ? cw[c] : 64;
			x += w;
			c++;
		}
	}

	static double sheetx(SheetModel m, int col, double off) {
		if (col < 0) col = 0;
		var widths = m?.ColWidths;
		var n = widths?.Length ?? 0;
		var x = 0.0;
		var lim = Math.Min(col, n);
		for (var i = 0; i < lim; i++) {
			var w = widths[i];
			if (w > 0) x += w;
		}
		if (col > n) x += (col - n) * 64.0;
		return x + off;
	}

	static double sheety(SheetModel m, int row, double off) {
		if (row < 0) row = 0;
		var heights = m?.RowHeights;
		var n = heights?.Length ?? 0;
		var y = 0.0;
		var lim = Math.Min(row, n);
		for (var i = 0; i < lim; i++) {
			var h = heights[i];
			if (h > 0) y += h;
		}
		if (row > n) y += (row - n) * 20.0;
		return y + off;
	}

	static double colw(SheetModel m, int c) {
		if (m?.ColWidths != null && c >= 0 && c < m.ColWidths.Length && m.ColWidths[c] > 0)
			return m.ColWidths[c];
		return 64;
	}

	static double rowh(SheetModel m, int r) {
		if (m?.RowHeights != null && r >= 0 && r < m.RowHeights.Length && m.RowHeights[r] > 0)
			return m.RowHeights[r];
		return 20;
	}

	static XDocument xdoc(OpenXmlPart part) {
		using var s = part.GetStream();
		return XDocument.Load(s);
	}

	static List<OpenXmlPart> allparts(OpenXmlPartContainer root) {
		var list = new List<OpenXmlPart>();
		var seen = new HashSet<OpenXmlPart>();
		walkparts(root, list, seen);
		return list;
	}

	static void walkparts(OpenXmlPartContainer container, List<OpenXmlPart> list, HashSet<OpenXmlPart> seen) {
		if (container == null) return;
		foreach (var id in container.Parts) {
			var p = id.OpenXmlPart;
			if (p == null || !seen.Add(p)) continue;
			list.Add(p);
			walkparts(p, list, seen);
		}
	}

	/// <summary>命令行自检：构造带图的 xlsx/xls，走 Prepare 与位图解码。</summary>
	public static int SelfCheck(TextWriter log) {
		if (log == null) log = Console.Out;
		var dir = Path.Combine(Path.GetTempPath(), "ScreenKit_xlsxpic_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var fail = 0;
		try {
			var xlsx = Path.Combine(dir, "pic.xlsx");
			var xls = Path.Combine(dir, "pic.xls");
			writetestxlsx(xlsx);
			writetestxls(xls);
			fail += checkxlsx(xlsx, log);
			fail += checkxls(xls, log);
		} catch (Exception ex) {
			log.WriteLine("FAIL exception " + ex.Message);
			fail++;
		} finally {
			try { Directory.Delete(dir, true); } catch { /* ignore */ }
		}
		log.WriteLine(fail == 0 ? "xlsx-pic PASS" : "xlsx-pic FAIL " + fail);
		return fail == 0 ? 0 : 1;
	}

	static int checkxlsx(string path, TextWriter log) {
		var data = XlsxViewer.Prepare(path);
		var pics = data?.Sheets != null && data.Sheets.Count > 0 ? data.Sheets[0].Model?.Pictures : null;
		if (pics == null || pics.Count < 2) {
			log.WriteLine("FAIL xlsx count " + (pics?.Count ?? 0));
			return 1;
		}
		SheetPicture anchor = null;
		SheetPicture fill = null;
		foreach (var p in pics) {
			if (p.Kind == 0) anchor = p;
			if (p.Kind == 3) fill = p;
		}
		if (anchor == null || anchor.FromCol != 1 || anchor.FromRow != 2 || anchor.ToCol != 3 || anchor.ToRow != 6) {
			log.WriteLine("FAIL xlsx anchor");
			return 1;
		}
		if (fill == null || fill.FromRow != 0 || fill.FromCol != 1) {
			log.WriteLine("FAIL xlsx dispimg");
			return 1;
		}
		EnsureBitmap(anchor);
		EnsureBitmap(fill);
		if (anchor.Bitmap == null || anchor.Bitmap.PixelWidth < 1 || fill.Bitmap == null) {
			log.WriteLine("FAIL xlsx decode");
			return 1;
		}
		log.WriteLine("ok xlsx pics=" + pics.Count);
		return 0;
	}

	static int checkxls(string path, TextWriter log) {
		var data = XlsxViewer.Prepare(path);
		var pics = data?.Sheets != null && data.Sheets.Count > 0 ? data.Sheets[0].Model?.Pictures : null;
		if (pics == null || pics.Count < 1) {
			log.WriteLine("FAIL xls count");
			return 1;
		}
		var p = pics[0];
		if (p.Kind != 0 || p.FromCol != 1 || p.FromRow != 1 || p.ToCol != 3 || p.ToRow != 5) {
			log.WriteLine($"FAIL xls anchor kind={p.Kind} {p.FromCol},{p.FromRow}-{p.ToCol},{p.ToRow}");
			return 1;
		}
		EnsureBitmap(p);
		if (p.Bitmap == null || p.Bitmap.PixelWidth < 1) {
			log.WriteLine("FAIL xls decode");
			return 1;
		}
		log.WriteLine("ok xls pics=" + pics.Count);
		return 0;
	}

	static void writetestxlsx(string path) {
		var png = png1x1();
		using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
		var wb = doc.AddWorkbookPart();
		wb.Workbook = new Workbook();
		var wsp = wb.AddNewPart<WorksheetPart>();
		var dp = wsp.AddNewPart<DrawingsPart>();
		var img = dp.AddImagePart(ImagePartType.Png);
		using (var s = img.GetStream(FileMode.Create, FileAccess.Write))
			s.Write(png, 0, png.Length);
		var rel = dp.GetIdOfPart(img);
		var from = new Xdr.FromMarker();
		from.Append(new Xdr.ColumnId("1"));
		from.Append(new Xdr.ColumnOffset("0"));
		from.Append(new Xdr.RowId("2"));
		from.Append(new Xdr.RowOffset("0"));
		var to = new Xdr.ToMarker();
		to.Append(new Xdr.ColumnId("3"));
		to.Append(new Xdr.ColumnOffset("0"));
		to.Append(new Xdr.RowId("6"));
		to.Append(new Xdr.RowOffset("0"));
		var blipFill = new Xdr.BlipFill();
		blipFill.Append(new A.Blip { Embed = rel });
		blipFill.Append(new A.Stretch(new A.FillRectangle()));
		var pic = new Xdr.Picture();
		pic.Append(blipFill);
		pic.Append(new Xdr.ShapeProperties());
		var anchor = new Xdr.TwoCellAnchor();
		anchor.Append(from);
		anchor.Append(to);
		anchor.Append(pic);
		anchor.Append(new Xdr.ClientData());
		dp.WorksheetDrawing = new Xdr.WorksheetDrawing(anchor);
		var cell = new Cell {
			CellReference = "B1",
			DataType = CellValues.String,
			CellFormula = new CellFormula("DISPIMG(\"ID_TESTPIC\",1)"),
		};
		var row = new Row { RowIndex = 1U };
		row.Append(cell);
		var ws = new Worksheet();
		ws.Append(new SheetData(row));
		ws.Append(new Drawing { Id = wsp.GetIdOfPart(dp) });
		wsp.Worksheet = ws;
		var ext = wb.AddExtendedPart(
			"http://www.wps.cn/officeDocument/2017/etCustomData",
			"application/xml",
			".xml");
		var id2 = ext.CreateRelationshipToPart(img);
		var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
			+ "<etc:cellImages xmlns:etc=\"http://www.wps.cn/officeDocument/2017/etCustomData\" "
			+ "xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" "
			+ "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
			+ "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">"
			+ "<etc:cellImage><xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"2\" name=\"ID_TESTPIC\"/></xdr:nvPicPr>"
			+ "<xdr:blipFill><a:blip r:embed=\"" + id2 + "\"/></xdr:blipFill></xdr:pic></etc:cellImage></etc:cellImages>";
		using (var xs = ext.GetStream(FileMode.Create, FileAccess.Write)) {
			var buf = System.Text.Encoding.UTF8.GetBytes(xml);
			xs.Write(buf, 0, buf.Length);
		}
		var sheets = wb.Workbook.AppendChild(new Sheets());
		sheets.Append(new Sheet { Id = wb.GetIdOfPart(wsp), SheetId = 1U, Name = "S" });
		wb.Workbook.Save();
	}

	static void writetestxls(string path) {
		var wb = new HSSFWorkbook();
		var sheet = wb.CreateSheet("S");
		sheet.CreateRow(0).CreateCell(0).SetCellValue("a");
		var idx = wb.AddPicture(png1x1(), PictureType.PNG);
		var pat = (HSSFPatriarch)sheet.CreateDrawingPatriarch();
		var anc = new HSSFClientAnchor(0, 0, 0, 0, 1, 1, 3, 5);
		pat.CreatePicture(anc, idx);
		using var fs = File.Create(path);
		wb.Write(fs);
	}

	static byte[] png1x1() => Convert.FromBase64String(
		"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

	struct Box {
		public double X, Y, W, H;
	}
}
