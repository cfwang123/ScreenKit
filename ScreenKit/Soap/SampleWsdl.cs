using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ScreenKit.Soap;

/// <summary>内置示例 WSDL：编译进程序集，每次加载都以出厂版本为准，本地文件被改动会自动还原并置为只读。</summary>
static class SampleWsdl {
	public const string FileName = "demo.wsdl";
	const string Resource = "ScreenKit.Soap.demo.wsdl";

	static string cachedText;

	/// <summary>读取嵌入资源里的 WSDL 原文。</summary>
	public static string text() {
		if (cachedText != null) return cachedText;
		var asm = Assembly.GetExecutingAssembly();
		using (var stream = asm.GetManifestResourceStream(Resource)) {
			if (stream == null) throw new InvalidOperationException("内置示例 WSDL 缺失：" + Resource);
			using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
				cachedText = reader.ReadToEnd();
		}
		return cachedText;
	}

	/// <summary>示例 WSDL 的落盘路径（程序目录 soapmock\sample\demo.wsdl），仅计算路径。</summary>
	public static string localPath() {
		string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soapmock", "sample");
		Directory.CreateDirectory(dir);
		return Path.Combine(dir, FileName);
	}

	/// <summary>确保本地示例与出厂内容一致：被改动过就覆盖回去，并置为只读，防止外部误改。</summary>
	public static string ensureLocal() {
		string path = localPath();
		try {
			string content = text();
			bool changed = false;
			if (File.Exists(path)) {
				try { changed = File.ReadAllText(path) != content; } catch { changed = true; }
			}
			if (!File.Exists(path) || changed) {
				if (File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
				File.WriteAllText(path, content, new UTF8Encoding(false));
			}
			var attrs = File.GetAttributes(path);
			if ((attrs & FileAttributes.ReadOnly) == 0) File.SetAttributes(path, attrs | FileAttributes.ReadOnly);
		} catch { }
		return path;
	}

	public static bool isSample(string location) {
		if (string.IsNullOrEmpty(location)) return false;
		try {
			return string.Equals(Path.GetFullPath(location), Path.GetFullPath(localPath()), StringComparison.OrdinalIgnoreCase);
		} catch {
			return false;
		}
	}

	/// <summary>
	/// 示例方法对应的模拟响应（soap:Body 内层 XML）。
	/// 首次载入示例且没有历史配置时自动填入，让“启动模拟 → 调用”直接看到具体数据。
	/// </summary>
	public static Dictionary<string, string> defaultResponses() {
		const string ns = "urn:screenkit:soapdemo";
		string q = "ns0:";

		string user = string.Join("\n",
			$"<{q}GetUserResponse xmlns:{q.TrimEnd(':')}=\"{ns}\">",
			$"  <{q}user>",
			$"    <{q}id>1</{q}id>",
			$"    <{q}name>张三</{q}name>",
			$"    <{q}email>zhangsan@example.com</{q}email>",
			$"    <{q}active>true</{q}active>",
			$"    <{q}score>88.5</{q}score>",
			$"    <{q}tags>vip</{q}tags>",
			$"    <{q}tags>new</{q}tags>",
			$"    <{q}address country=\"CN\">",
			$"      <{q}street>中山路 1 号</{q}street>",
			$"      <{q}city>上海</{q}city>",
			$"      <{q}zip>200000</{q}zip>",
			$"    </{q}address>",
			$"  </{q}user>",
			$"</{q}GetUserResponse>");

		return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["Add"] = $"<{q}AddResponse xmlns:{q.TrimEnd(':')}=\"{ns}\"><{q}result>7</{q}result></{q}AddResponse>",
			["Echo"] = $"<{q}EchoResponse xmlns:{q.TrimEnd(':')}=\"{ns}\"><{q}echoed>hello from SoapTest</{q}echoed></{q}EchoResponse>",
			["GetUser"] = user,
			["SubmitOrder"] = string.Join("\n",
				$"<{q}SubmitOrderResponse xmlns:{q.TrimEnd(':')}=\"{ns}\">",
				$"  <{q}ok>true</{q}ok>",
				$"  <{q}traceId>{{{{guid}}}}</{q}traceId>",
				$"</{q}SubmitOrderResponse>"),
			["GetServerTime"] = $"<{q}GetServerTimeResponse xmlns:{q.TrimEnd(':')}=\"{ns}\"><{q}time>{{{{now}}}}</{q}time></{q}GetServerTimeResponse>",
			["Divide"] = string.Join("\n",
				$"<{q}DivideResponse xmlns:{q.TrimEnd(':')}=\"{ns}\">",
				$"  <{q}result>3.5</{q}result>",
				$"  <{q}remainder>0.5</{q}remainder>",
				$"</{q}DivideResponse>"),
		};
	}
}
