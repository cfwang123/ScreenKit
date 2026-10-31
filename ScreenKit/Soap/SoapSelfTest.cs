using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;

namespace ScreenKit.Soap;

/// <summary>SOAP 调试核心自测：解析 WSDL、构造报文、调用模拟服务并回读 WSDL。</summary>
static class SoapSelfTest {
	public static int Run(Action<string> log, Action<string> err) {
		int pass = 0, fail = 0;
		void ok(string name) {
			pass++;
			log("PASS  " + name);
		}
		void bad(string name, string detail) {
			fail++;
			err("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : "：" + detail));
		}
		void check(string name, bool cond, string detail = "") {
			if (cond) ok(name);
			else bad(name, detail);
		}
		Net.init();
		log("=== SOAP 接口调试 --test-soap ===");
		try {
			var s = WsdlSession.openText(SampleWsdl.text(), "demo.wsdl");
			check("示例 WSDL 解析", s.Error == null && s.Model != null, s.Error ?? "");
			if (s.Error != null || s.Model == null) return 1;
			check("示例端点", s.Model.Endpoints.Count > 0, $"endpoints={s.Model.Endpoints.Count}");
			check("示例方法数", s.ops().Count() == 6, $"ops={s.ops().Count()}");

			var add = s.findOp("Add");
			check("找到 Add", add != null);
			if (add == null) return 1;
			var parts = s.buildInput(add);
			check("Add 参数", parts.Count == 1 && parts[0].Name == "Add", $"parts={parts.Count}");
			if (parts.Count == 0) return 1;
			var a = parts[0].Children.FirstOrDefault(x => x.Name == "a");
			var b = parts[0].Children.FirstOrDefault(x => x.Name == "b");
			check("Add 子参数", a != null && b != null);
			if (a == null || b == null) return 1;
			a.Value = "2";
			b.Value = "3";
			string request = SoapEnvelope.buildRequest(add, parts);
			var xreq = XDocument.Parse(request);
			check("请求含 Add", xreq.Descendants().Any(e => e.Name.LocalName == "Add"));
			check("请求参数 a=2", xreq.Descendants().Any(e => e.Name.LocalName == "a" && e.Value == "2"));
			check("请求命名空间", request.Contains("urn:screenkit:soapdemo"));

			add.SoapVersionOverride = "1.2";
			string req12 = SoapEnvelope.buildRequest(add, parts);
			check("SOAP 1.2 命名空间", req12.Contains(SoapEnvelope.NS_SOAP12));
			check("SOAP 1.2 Content-Type", SoapEnvelope.contentType("1.2").Contains("application/soap+xml"));
			add.SoapVersionOverride = null;

			check("模板占位符", Template.expand("{{guid}}").Length == 36 && Template.expand("{{now}}").Length >= 19);
			string fault = "<soap:Envelope xmlns:soap=\"" + SoapEnvelope.NS_SOAP11 + "\"><soap:Body><soap:Fault><faultcode>soap:Server</faultcode><faultstring>boom</faultstring></soap:Fault></soap:Body></soap:Envelope>";
			check("Fault 识别", SoapEnvelope.tryGetFault(fault, out string fcode, out string fmsg) && fcode == "soap:Server" && fmsg == "boom");

			int port = freeport();
			var mock = new MockServer();
			if (!mock.start(port, false, out string error)) {
				bad("模拟服务启动", error);
				return 1;
			}
			try {
				mock.Builder = s.Builder;
				mock.publish(s.Model, port);
				string url = mock.BaseUrl + "/demo/calc";
				mock.Resolver = name => string.Equals(name, "Add", StringComparison.OrdinalIgnoreCase)
					? SampleWsdl.defaultResponses()["Add"]
					: null;
				var r = SoapClient.call(add, url, request, add.SoapAction, null, 5000, "");
				check("模拟调用 HTTP 200", r.StatusCode == 200, r.Error ?? "");
				check("模拟调用返回 7", r.ResponseBody.Contains(">7<"), r.ResponseBody);

				mock.Resolver = null;
				var r2 = SoapClient.call(add, url, request, add.SoapAction, null, 5000, "");
				check("默认响应可用", r2.StatusCode == 200 && r2.ResponseBody.Contains("AddResponse"), r2.ResponseBody);

				string wsdl = Net.readText(mock.BaseUrl + "/demo.wsdl");
				check("模拟服务发布 WSDL", wsdl.Contains("127.0.0.1:" + port), wsdl.Length.ToString());
				check("模拟 WSDL 改地址", wsdl.Contains("127.0.0.1:" + port + "/demo/calc"));
				var s2 = WsdlSession.openText(wsdl, mock.BaseUrl + "/demo.wsdl");
				check("回读模拟 WSDL", s2.Error == null && s2.ops().Count() == 6, s2.Error ?? "");
				if (s2.Error == null) {
					var add2 = s2.findOp("Add");
					check("回读端点地址", add2 != null && add2.Endpoint.Address.Contains("127.0.0.1:" + port));
				}
				check("模拟日志", mock.logsnapshot().Count >= 2);
			}
			finally {
				mock.stop();
			}
		}
		catch (Exception ex) {
			bad("异常", ex.ToString());
		}
		log($"SOAP 自测：{pass} 通过，{fail} 失败");
		return fail == 0 ? 0 : 1;
	}

	static int freeport() {
		var l = new TcpListener(IPAddress.Loopback, 0);
		l.Start();
		try { return ((IPEndPoint)l.LocalEndpoint).Port; }
		finally { l.Stop(); }
	}
}
