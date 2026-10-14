using System.Globalization;
using System.Text.Json.Nodes;

namespace ScreenKit;

sealed partial class HttpOcrServer {
	void handleipcity(SockCtx ctx) {
		if (!readbody(ctx, out var jo)) return;
		var ip = field(ctx, jo, "ip") ?? field(ctx, jo, "host") ?? field(ctx, jo, "query") ?? "";
		try {
			var info = NetTools.IpCity(ip, null, CancellationToken.None).GetAwaiter().GetResult();
			if (!info.Ok) {
				var msg = info.Error.Length > 0 ? info.Error : "查询失败";
				writejson(ctx, 200, err(802, msg));
				return;
			}
			var data = new JsonObject {
				["ip"] = info.Ip,
				["country"] = info.Country,
				["region"] = info.Region,
				["city"] = info.City,
				["isp"] = info.Isp,
				["text"] = info.Text,
			};
			if (info.HasPos) {
				var lat = info.Lat.ToString("0.######", CultureInfo.InvariantCulture);
				var lon = info.Lon.ToString("0.######", CultureInfo.InvariantCulture);
				data["lat"] = decimal.Parse(lat, CultureInfo.InvariantCulture);
				data["lon"] = decimal.Parse(lon, CultureInfo.InvariantCulture);
			}
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = data,
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(802, ex.InnerException?.Message ?? ex.Message));
		}
	}
}
