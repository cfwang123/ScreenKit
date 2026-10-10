using System.Globalization;
using System.Text.Json.Nodes;

namespace ScreenKit;

sealed partial class HttpOcrServer {
	void handlefx(SockCtx ctx) {
		if (!readbody(ctx, out var jo)) return;
		var amountText = field(ctx, jo, "amount") ?? field(ctx, jo, "n") ?? "";
		var from = field(ctx, jo, "from") ?? "CNY";
		var to = field(ctx, jo, "to") ?? "all";
		if (amountText.Trim().Length == 0) {
			writejson(ctx, 200, err(802, "请输入数字"));
			return;
		}
		if (!decimal.TryParse(amountText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) {
			writejson(ctx, 200, err(802, "请输入数字"));
			return;
		}
		try {
			var quote = FxRate.Quote(amount, from, to);
			writejson(ctx, 200, new JsonObject {
				["code"] = 100,
				["data"] = new JsonObject {
					["text"] = quote.Text,
					["live"] = quote.Live,
				},
			});
		}
		catch (Exception ex) {
			writejson(ctx, 200, err(802, ex.Message));
		}
	}
}
