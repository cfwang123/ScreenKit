using System.Text.Json.Nodes;

namespace ScreenKit;

sealed partial class HttpOcrServer {
	void handleusb(SockCtx ctx) {
		if (!readbody(ctx, out var jo)) return;
		var id = field(ctx, jo, "id") ?? field(ctx, jo, "usb") ?? "";
		var vid = field(ctx, jo, "vid") ?? "";
		var pid = field(ctx, jo, "pid") ?? "";
		if (id.Trim().Length == 0) {
			if (vid.Trim().Length == 0)
				id = field(ctx, jo, "host") ?? field(ctx, jo, "query") ?? "";
			else
				id = pid.Trim().Length == 0 ? vid : vid.Trim() + ":" + pid.Trim();
		}
		if (id.Trim().Length == 0) {
			writejson(ctx, 200, err(802, "请输入 USB ID"));
			return;
		}
		try {
			var info = NetTools.UsbVendor(id, null, CancellationToken.None).GetAwaiter().GetResult();
			if (!info.Ok) {
				var msg = info.Error.Length > 0 ? info.Error : "未登记厂家";
				writejson(ctx, 200, err(802, msg));
				return;
			}
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["vid"] = info.Vid,
					["pid"] = info.Pid,
					["vendor"] = info.Vendor,
					["product"] = info.Product,
					["text"] = info.Text,
				},
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(802, ex.InnerException?.Message ?? ex.Message));
		}
	}
}
