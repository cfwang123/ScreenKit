using System.Text.Json.Nodes;

namespace ScreenKit;

sealed partial class HttpOcrServer {
	void handlemac(SockCtx ctx) {
		if (!readbody(ctx, out var jo)) return;
		var mac = field(ctx, jo, "mac") ?? field(ctx, jo, "oui") ?? field(ctx, jo, "host") ?? field(ctx, jo, "query") ?? "";
		if (mac.Trim().Length == 0) {
			writejson(ctx, 200, err(802, "请输入 MAC 地址"));
			return;
		}
		try {
			var info = NetTools.MacVendor(mac, null, CancellationToken.None).GetAwaiter().GetResult();
			if (!info.Ok) {
				var msg = info.Error.Length > 0 ? info.Error : "未登记厂家";
				writejson(ctx, 200, err(802, msg));
				return;
			}
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["mac"] = info.Mac,
					["company"] = info.Company,
					["country"] = info.Country,
					["address"] = info.Address,
					["block"] = info.Block,
					["local"] = info.Local,
					["multicast"] = info.Multicast,
					["text"] = info.Text,
				},
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(802, ex.InnerException?.Message ?? ex.Message));
		}
	}
}
