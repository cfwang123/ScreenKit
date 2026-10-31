using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace ScreenKit.Soap;

class CallResult {
	public string Url = "";
	public string RequestHeaders = "";
	public string RequestBody = "";
	public int StatusCode;
	public string StatusText = "";
	public string ResponseHeaders = "";
	public string ResponseBody = "";
	public string Error;
	public string ErrorDetail;
	public long ElapsedMs;
	public string ContentType = "";
	public string SoapAction = "";

	public bool Ok => Error == null;

	public string Dump() {
		var sb = new StringBuilder();
		sb.AppendLine($"POST {Url}");
		sb.Append(RequestHeaders);
		sb.AppendLine();
		sb.AppendLine(RequestBody);
		sb.AppendLine();
		sb.AppendLine("======== 响应 ========");
		if (StatusCode != 0) sb.AppendLine($"HTTP {StatusCode} {StatusText}  （{ElapsedMs} ms）");
		sb.Append(ResponseHeaders);
		sb.AppendLine();
		sb.AppendLine(ResponseBody);
		if (Error != null) {
			sb.AppendLine();
			sb.AppendLine("错误: " + Error);
			if (!string.IsNullOrEmpty(ErrorDetail)) sb.AppendLine(ErrorDetail);
		}
		return sb.ToString();
	}
}

/// <summary>直接基于 HttpWebRequest 的 SOAP 调用（不依赖任何 SOAP/WCF 库）。</summary>
static class SoapClient {
	public static CallResult call(Operation op, string address, string body, string soapAction, string contentType,
		int timeoutMs, string extraHeaders) {
		var result = new CallResult { Url = address, RequestBody = body };
		var sw = Stopwatch.StartNew();
		try {
			if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("服务地址为空");
			bool is12 = op?.SoapVersionValue == "1.2";
			string ct = string.IsNullOrWhiteSpace(contentType)
				? SoapEnvelope.contentType(op?.SoapVersionValue)
				: contentType.Trim();
			if (is12 && !string.IsNullOrEmpty(soapAction) && ct.IndexOf("action=", StringComparison.OrdinalIgnoreCase) < 0)
				ct = $"{ct.TrimEnd(';', ' ')}; action=\"{soapAction}\"";
			result.ContentType = ct;
			result.SoapAction = soapAction ?? "";

			var req = (HttpWebRequest)WebRequest.Create(address);
			req.Method = "POST";
			req.ContentType = ct;
			req.Timeout = timeoutMs;
			req.ReadWriteTimeout = timeoutMs;
			req.UserAgent = "ScreenKit/1.0";
			req.KeepAlive = false;
			req.AllowAutoRedirect = true;
			if (!is12 && !string.IsNullOrEmpty(soapAction)) req.Headers["SOAPAction"] = soapAction;
			req.Proxy = ScreenKit.HttpProxy.Instance;
			if (Net.TrustAllCerts) req.ServerCertificateValidationCallback = (_, _, _, _) => true;
			if (!string.IsNullOrWhiteSpace(extraHeaders)) applyHeaders(req, extraHeaders);

			byte[] payload = new UTF8Encoding(false).GetBytes(body ?? "");
			req.ContentLength = payload.Length;
			result.RequestHeaders = dumpRequestHeaders(req);

			using (var s = req.GetRequestStream()) s.Write(payload, 0, payload.Length);
			using (var resp = (HttpWebResponse)req.GetResponse()) readResponse(resp, result);
		} catch (WebException ex) {
			result.Error = ex.Message;
			if (ex.Response is HttpWebResponse hr) {
				try { readResponse(hr, result); } catch (Exception e2) { result.ErrorDetail = e2.Message; }
			} else {
				result.ErrorDetail = ex.Status.ToString();
			}
		} catch (Exception ex) {
			result.Error = ex.Message;
			result.ErrorDetail = ex.InnerException?.Message;
		} finally {
			result.ElapsedMs = sw.ElapsedMilliseconds;
		}
		return result;
	}

	static void readResponse(HttpWebResponse resp, CallResult result) {
		result.StatusCode = (int)resp.StatusCode;
		result.StatusText = resp.StatusDescription;
		result.ResponseHeaders = dumpResponseHeaders(resp);
		using var s = resp.GetResponseStream();
		using var ms = new MemoryStream();
		if (s != null) s.CopyTo(ms);
		result.ResponseBody = Net.decode(ms.ToArray());
	}

	static string dumpRequestHeaders(HttpWebRequest req) {
		var sb = new StringBuilder();
		foreach (string key in req.Headers.AllKeys) sb.AppendLine($"{key}: {req.Headers[key]}");
		sb.AppendLine($"Content-Type: {req.ContentType}");
		sb.AppendLine($"Content-Length: {req.ContentLength}");
		return sb.ToString();
	}

	static string dumpResponseHeaders(HttpWebResponse resp) {
		var sb = new StringBuilder();
		foreach (string key in resp.Headers.AllKeys) sb.AppendLine($"{key}: {resp.Headers[key]}");
		return sb.ToString();
	}

	static void applyHeaders(HttpWebRequest req, string text) {
		foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
			int p = raw.IndexOf(':');
			if (p <= 0) continue;
			string k = raw.Substring(0, p).Trim();
			string v = raw.Substring(p + 1).Trim();
			if (k.Length == 0) continue;
			switch (k.ToLowerInvariant()) {
				case "content-type": req.ContentType = v; break;
				case "soapaction": req.Headers["SOAPAction"] = v; break;
				case "host": case "content-length": case "connection": break;
				default:
					try { req.Headers[k] = v; } catch { }
					break;
			}
		}
	}
}
