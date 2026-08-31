using System.Net;
using System.Text.Json.Nodes;

namespace ScreenKit;

/// <summary>HTTP 条码：POST /api/qr · /api/barcode · /api/barcodes。</summary>
sealed partial class HttpOcrServer {
	void handleqr(HttpListenerContext ctx) {
		byte[] imageBytes;
		JsonObject jo;
		try {
			(imageBytes, jo) = readqrimage(ctx.Request);
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(800, ex.Message));
			return;
		}
		jo ??= new JsonObject();
		if (imageBytes == null || imageBytes.Length == 0) {
			var p = jostr(jo, "path") ?? jostr(jo, "file");
			if (!string.IsNullOrWhiteSpace(p)) {
				writejson(ctx, 200, err(802, "图片文件不存在"));
				return;
			}
			writejson(ctx, 200, err(802, "请提供 base64 / path 或 multipart 图片"));
			return;
		}

		var format = qrformat(jo);
		var t0 = Environment.TickCount;
		QrResult codes;
		try {
			codes = QrScan.Run(imageBytes);
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(950, "条码识别失败: " + ex.Message));
			return;
		}
		var ms = Math.Max(0, Environment.TickCount - t0);
		var arr = buildbarcodearray(codes);
		var n = codes?.DecodedCount ?? 0;
		if (n == 0) {
			writejson(ctx, 200, new JsonObject {
				["code"] = 101,
				["data"] = format == "text" ? "" : "未检测到条码或二维码",
				["count"] = 0,
				["time"] = ms,
				["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
			});
			return;
		}

		JsonNode data = format == "text" ? JsonValue.Create(codes.FullText ?? "") : arr;
		writejson(ctx, 200, new JsonObject {
			["code"] = 100,
			["data"] = data,
			["count"] = n,
			["time"] = ms,
			["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
		});
	}

	(byte[] image, JsonObject jo) readqrimage(HttpListenerRequest req) {
		var ctype = req.ContentType ?? "";
		if (ctype.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)) {
			var (img, opt) = readmultipart(req);
			return (img, opt);
		}
		var jo = readjsonbody(req);
		var img2 = faceb64(jo, "base64", "image", "img");
		if (img2 == null)
			img2 = facefile(jo, "path", "file");
		return (img2, jo);
	}

	static string qrformat(JsonObject jo) {
		var f = jostr(jo, "format") ?? jostr(jo, "data.format");
		if (jo?["options"] is JsonObject opt) {
			f ??= jostr(opt, "data.format") ?? jostr(opt, "format");
		}
		if (string.Equals(f, "text", StringComparison.OrdinalIgnoreCase))
			return "text";
		return "dict";
	}
}
