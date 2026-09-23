using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace ScreenKit;

/// <summary>设备配对：首次弹窗确认，之后凭 token。</summary>
public sealed class SendFileAuth {
	readonly object gate = new();
	readonly Func<OcrOptions> getOpts;
	readonly Action save;
	public bool AutoAccept;
	public Func<string, string, bool> AskPair;

	public SendFileAuth(Func<OcrOptions> optionsFactory, Action saveCfg) {
		getOpts = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
		save = saveCfg;
	}

	public SendFileDevice Find(string id, string token) {
		if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(token)) return null;
		lock (gate) {
			var list = getOpts()?.SendFileDevices;
			if (list == null) return null;
			return list.FirstOrDefault(d =>
				d != null
				&& string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(d.Token, token, StringComparison.Ordinal));
		}
	}

	public SendFileDevice Pair(string id, string name, string ip) {
		id = (id ?? "").Trim();
		name = (name ?? "").Trim();
		if (id.Length == 0) return null;
		if (name.Length == 0) name = id;
		lock (gate) {
			var o = getOpts();
			if (o == null) return null;
			o.SendFileDevices ??= new List<SendFileDevice>();
		}
		var allow = AutoAccept;
		if (!allow) {
			var ask = AskPair;
			if (ask != null) {
				try { allow = ask(name, ip ?? ""); }
				catch { allow = false; }
			}
			else {
				allow = askui(name, ip);
			}
		}
		if (!allow) return null;
		var token = gentoken();
		lock (gate) {
			var o = getOpts();
			if (o == null) return null;
			o.SendFileDevices ??= new List<SendFileDevice>();
			var hit = o.SendFileDevices.FirstOrDefault(d =>
				d != null && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
			if (hit == null) {
				hit = new SendFileDevice { Id = id, Name = name, Token = token };
				o.SendFileDevices.Add(hit);
			}
			else {
				hit.Name = name;
				if (string.IsNullOrEmpty(hit.Token))
					hit.Token = token;
			}
			try { save?.Invoke(); } catch { }
			return hit;
		}
	}

	public string FirstDeviceId() {
		lock (gate) {
			var list = getOpts()?.SendFileDevices;
			return list?.FirstOrDefault(d => d != null && !string.IsNullOrEmpty(d.Id))?.Id ?? "";
		}
	}

	public void Unpair(string id) {
		lock (gate) {
			var o = getOpts();
			if (o?.SendFileDevices == null) return;
			o.SendFileDevices.RemoveAll(d =>
				d != null && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
			try { save?.Invoke(); } catch { }
		}
	}

	static bool askui(string name, string ip) {
		var app = Application.Current;
		if (app == null) return false;
		var ok = false;
		try {
			app.Dispatcher.Invoke(() => {
				ok = PairAskWindow.Ask(name, ip);
			});
		}
		catch { return false; }
		return ok;
	}

	static string gentoken() {
		var buf = new byte[24];
		using (var rng = RandomNumberGenerator.Create())
			rng.GetBytes(buf);
		var sb = new StringBuilder(buf.Length * 2);
		foreach (var b in buf)
			sb.Append(b.ToString("x2"));
		return sb.ToString();
	}
}
