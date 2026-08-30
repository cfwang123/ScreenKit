using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCvSharp;

namespace ScreenKit;

/// <summary>HTTP 人脸：GET /api/face/models · POST /api/face。</summary>
sealed partial class HttpOcrServer {
	readonly object httpFaceLock = new();
	FacePipeline httpFace;
	GenderAgeDetector httpAttr;
	string httpFaceKey = "";
	string httpAttrKey = "";

	void handlefacemodels(HttpListenerContext ctx) {
		List<string> onnx;
		try { onnx = FaceModels.ListOnnx(); }
		catch (Exception ex) {
			writejson(ctx, 200, err(930, "扫描 facemodels 失败: " + ex.Message));
			return;
		}
		onnx ??= new List<string>();
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = new JsonObject {
				["root"] = FaceModels.ModelsRoot(),
				["ready"] = FaceModels.IsReady(),
				["det"] = strarr(onnx.Where(FaceModels.IsDetFile)),
				["rec"] = strarr(onnx.Where(FaceModels.IsRegFile)),
				["landmark"] = strarr(FaceModels.LmkModels(onnx)),
				["attr"] = strarr(FaceModels.AttrModels(onnx)),
			},
			["count"] = onnx.Count,
		});
	}

	void handleface(HttpListenerContext ctx) {
		byte[] imgA;
		byte[] imgB;
		JsonObject jo;
		try {
			(imgA, imgB, jo) = readfaceimages(ctx.Request);
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}
		jo ??= new JsonObject();
		if (imgA == null || imgA.Length == 0) {
			writejson(ctx, 200, err(802, "请提供 base64 / path 或 multipart 图片（比对再加 base64_b / path_b）"));
			return;
		}

		if (!FaceModels.IsReady()) {
			writejson(ctx, 200, err(930, "未找到人脸模型，请在「安装功能」下载 InsightFace buffalo_l"));
			return;
		}

		var onnx = FaceModels.ListOnnx();
		var detList = onnx.Where(FaceModels.IsDetFile).ToList();
		var regList = onnx.Where(FaceModels.IsRegFile).ToList();
		var opt = getOpts?.Invoke() ?? new OcrOptions();
		var detName = pickface(detList,
			jostr(jo, "det") ?? jostr(jo, "det_model"),
			opt.FaceDetModel);
		var regName = pickface(regList,
			jostr(jo, "reg") ?? jostr(jo, "rec") ?? jostr(jo, "reg_model"),
			opt.FaceRegModel);
		if (string.IsNullOrEmpty(detName) || string.IsNullOrEmpty(regName)) {
			writejson(ctx, 200, err(932, "facemodels 中无可用检测/识别 ONNX"));
			return;
		}

		var thresh = Compat.Clamp(asfloat(jo["threshold"], (float)opt.FaceThreshold), 0.1f, 0.95f);
		var compute = parsecompute(jostr(jo, "device") ?? jostr(jo, "compute") ?? opt.FaceCompute ?? "auto");
		var wantFeat = asbool(jo["include_feature"], false) || asbool(jo["feature"], false);
		var wantAttr = jo["attr"] == null && jo["genderage"] == null
			? FaceModels.AttrModels(onnx).Count > 0
			: asbool(jo["attr"], true) || asbool(jo["genderage"], false);
		var attrName = pickface(FaceModels.AttrModels(onnx),
			jostr(jo, "attr_model"),
			opt.FaceAttrModel);

		var t0 = Environment.TickCount;
		try {
			using var matA = decodefacemat(imgA);
			using var matB = imgB != null && imgB.Length > 0 ? decodefacemat(imgB) : null;

			FaceExtractResult ra;
			FaceExtractResult rb = null;
			GenderAgeResult? gaA = null, gaB = null;
			string ep;
			lock (httpFaceLock) {
				ensurehttpface(FaceModels.PathOf(detName), FaceModels.PathOf(regName), compute);
				ep = httpFace.EpLabel;
				ra = httpFace.ExtractTimed(matA);
				if (matB != null)
					rb = httpFace.ExtractTimed(matB);
				if (wantAttr && !string.IsNullOrEmpty(attrName) && attrName != "(无)") {
					ensurehttpattr(FaceModels.PathOf(attrName), compute);
					if (httpAttr != null) {
						if (ra?.Face != null) {
							try { gaA = httpAttr.Predict(matA, ra.Face); }
							catch { }
						}
						if (rb?.Face != null && matB != null) {
							try { gaB = httpAttr.Predict(matB, rb.Face); }
							catch { }
						}
					}
				}
			}

			var ms = Math.Max(0, Environment.TickCount - t0);
			if (matB == null) {
				if (ra?.Face == null || ra.Feature == null) {
					writejson(ctx, 200, new JsonObject {
						["code"] = 101,
						["data"] = "未检测到人脸",
						["time"] = ms,
						["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
					});
					return;
				}
				var data = new JsonObject {
					["faces"] = ra.FaceCount,
					["det"] = detName,
					["reg"] = regName,
					["provider"] = ep,
					["face"] = facejson(ra, gaA, wantFeat),
				};
				writejson(ctx, 200, new JsonObject {
					["code"] = 100,
					["data"] = data,
					["time"] = ms,
					["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
				});
				return;
			}

			if (ra?.Face == null || ra.Feature == null || rb?.Face == null || rb.Feature == null) {
				var miss = ra?.Face == null ? "左侧" : "右侧";
				writejson(ctx, 200, new JsonObject {
					["code"] = 101,
					["data"] = miss + "未检测到人脸",
					["time"] = ms,
					["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
				});
				return;
			}

			var sim = FaceSimilarity.Cosine(ra.Feature, rb.Feature);
			bool match = sim >= thresh;
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["similarity"] = Math.Round(sim, 6),
					["match"] = match,
					["threshold"] = Math.Round(thresh, 4),
					["det"] = detName,
					["reg"] = regName,
					["provider"] = ep,
					["left"] = facejson(ra, gaA, wantFeat),
					["right"] = facejson(rb, gaB, wantFeat),
				},
				["time"] = ms,
				["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(931, "人脸识别失败: " + ex.Message));
		}
	}

	void ensurehttpface(string detPath, string regPath, TtsComputeMode mode) {
		var key = detPath + "|" + regPath + "|" + (int)mode;
		if (httpFace != null && httpFaceKey == key) return;
		try { httpFace?.Dispose(); } catch { }
		httpFace = null;
		httpFace = new FacePipeline(detPath, regPath, 0.5f, mode);
		httpFaceKey = key;
	}

	void ensurehttpattr(string attrPath, TtsComputeMode mode) {
		if (string.IsNullOrEmpty(attrPath) || !File.Exists(attrPath)) return;
		var key = attrPath + "|" + (int)mode;
		if (httpAttr != null && httpAttrKey == key) return;
		try { httpAttr?.Dispose(); } catch { }
		httpAttr = null;
		httpAttr = new GenderAgeDetector(attrPath, mode);
		httpAttrKey = key;
	}

	void disposeface() {
		lock (httpFaceLock) {
			try { httpFace?.Dispose(); } catch { }
			try { httpAttr?.Dispose(); } catch { }
			httpFace = null;
			httpAttr = null;
			httpFaceKey = "";
			httpAttrKey = "";
		}
	}

	static Mat decodefacemat(byte[] bytes) {
		var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
		if (mat == null || mat.Empty()) {
			mat?.Dispose();
			throw new InvalidOperationException("无法解码图片（支持 png/jpg/bmp/webp 等）");
		}
		return mat;
	}

	static JsonObject facejson(FaceExtractResult r, GenderAgeResult? ga, bool includeFeat) {
		var face = r.Face;
		var box = new JsonArray {
			Math.Round(face.X1, 1), Math.Round(face.Y1, 1),
			Math.Round(face.X2, 1), Math.Round(face.Y2, 1),
		};
		JsonArray lmk = null;
		if (face.Landmarks != null && face.Landmarks.Length >= 10) {
			lmk = new JsonArray();
			for (int i = 0; i < 5; i++)
				lmk.Add(new JsonArray {
					Math.Round(face.Landmarks[i * 2], 1),
					Math.Round(face.Landmarks[i * 2 + 1], 1),
				});
		}
		var o = new JsonObject {
			["faces"] = r.FaceCount,
			["score"] = Math.Round(face.Score, 4),
			["box"] = box,
			["detect_ms"] = Math.Round(r.DetectMs, 1),
			["extract_ms"] = Math.Round(r.ExtractMs, 1),
		};
		if (lmk != null) o["landmarks5"] = lmk;
		if (ga.HasValue) {
			o["gender"] = ga.Value.GenderText;
			o["age"] = ga.Value.Age;
		}
		if (includeFeat && r.Feature != null) {
			var arr = new JsonArray();
			foreach (var v in r.Feature)
				arr.Add(Math.Round(v, 6));
			o["feature"] = arr;
			o["dim"] = r.Feature.Length;
		}
		return o;
	}

	(byte[] a, byte[] b, JsonObject jo) readfaceimages(HttpListenerRequest req) {
		var ctype = req.ContentType ?? "";
		if (ctype.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
			return readfaceparts(req);

		var jo = readjsonbody(req);
		var a = faceb64(jo, "base64", "image", "base64_a", "image_a");
		var b = faceb64(jo, "base64_b", "image_b", "base64_right");
		if (a == null) a = facefile(jo, "path", "path_a");
		if (b == null) b = facefile(jo, "path_b", "path_right");
		return (a, b, jo);
	}

	(byte[] a, byte[] b, JsonObject jo) readfaceparts(HttpListenerRequest req) {
		var boundary = extractboundary(req.ContentType ?? "");
		if (string.IsNullOrEmpty(boundary))
			throw new InvalidOperationException("multipart 缺少 boundary");
		using var ms = new MemoryStream();
		req.InputStream.CopyTo(ms);
		var parts = parseparts(ms.ToArray(), boundary);
		byte[] a = null, b = null;
		JsonObject jo = null;
		var extras = new List<byte[]>();
		foreach (var p in parts) {
			var name = (p.Name ?? "").ToLowerInvariant();
			var isFile = !string.IsNullOrEmpty(p.FileName)
				|| name is "file" or "image" or "img" or "upload" or "pic"
					or "file2" or "image_b" or "img_b" or "b" or "right"
					or "a" or "left" or "image_a";
			if (!isFile && p.ContentType != null
				&& p.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
				isFile = true;
			if (isFile && p.Data != null && p.Data.Length > 0) {
				if (name is "file2" or "image_b" or "img_b" or "b" or "right" or "pic_b")
					b ??= p.Data;
				else if (name is "file" or "image" or "img" or "upload" or "pic"
					or "a" or "left" or "image_a")
					a ??= p.Data;
				else
					extras.Add(p.Data);
				continue;
			}
			var text = p.Text ?? "";
			if (name is "base64" or "image_base64") {
				if (a == null && !string.IsNullOrWhiteSpace(text))
					a = decodebase64(text.Trim());
				continue;
			}
			if (name is "base64_b" or "image_b_base64") {
				if (b == null && !string.IsNullOrWhiteSpace(text))
					b = decodebase64(text.Trim());
				continue;
			}
			if (name is "options" or "option") {
				if (!string.IsNullOrWhiteSpace(text))
					jo = parseoptionsfield(text);
				continue;
			}
			if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrEmpty(p.Name)) {
				jo ??= new JsonObject();
				jo[p.Name] = tryparsescalar(text);
			}
		}
		foreach (var e in extras) {
			if (a == null) a = e;
			else if (b == null) { b = e; break; }
		}
		return (a, b, jo);
	}

	static byte[] faceb64(JsonObject jo, params string[] keys) {
		foreach (var k in keys) {
			if (jo[k] == null || jo[k].GetValueKind() != JsonValueKind.String) continue;
			var s = jo[k].GetValue<string>();
			if (string.IsNullOrWhiteSpace(s)) continue;
			return decodebase64(s);
		}
		return null;
	}

	static byte[] facefile(JsonObject jo, params string[] keys) {
		foreach (var k in keys) {
			if (jo[k] == null || jo[k].GetValueKind() != JsonValueKind.String) continue;
			var p = jo[k].GetValue<string>();
			if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) continue;
			return File.ReadAllBytes(p);
		}
		return null;
	}

	static string jostr(JsonObject jo, string key) {
		if (jo == null || jo[key] == null || jo[key].GetValueKind() == JsonValueKind.Null)
			return null;
		if (jo[key].GetValueKind() == JsonValueKind.String)
			return jo[key].GetValue<string>();
		return jo[key].ToString();
	}

	static string pickface(List<string> list, string want, string cfg) {
		if (list == null || list.Count == 0) return null;
		if (!string.IsNullOrWhiteSpace(want)) {
			var hit = list.FirstOrDefault(n =>
				string.Equals(n, want, StringComparison.OrdinalIgnoreCase))
				?? list.FirstOrDefault(n =>
					n.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
			if (hit != null) return hit;
		}
		if (!string.IsNullOrWhiteSpace(cfg)) {
			var hit = list.FirstOrDefault(n =>
				string.Equals(n, cfg, StringComparison.OrdinalIgnoreCase));
			if (hit != null) return hit;
		}
		return list[0];
	}

	static JsonArray strarr(IEnumerable<string> items) {
		var arr = new JsonArray();
		if (items == null) return arr;
		foreach (var s in items) arr.Add(s ?? "");
		return arr;
	}
}
