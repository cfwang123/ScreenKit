using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ScreenKit;

static partial class FileProbe {
	public sealed class PeKv {
		public string Key = "";
		public string Value = "";
	}

	public sealed class PeSec {
		public string Name = "";
		public string Range = "";
		public string Flags = "";
	}

	public sealed class PeImp {
		public string Dll = "";
		public string Name = "";
		public string Note = "";
	}

	public sealed class PeExp {
		public string Name = "";
		public string Ord = "";
		public string Note = "";
	}

	public sealed class PeNet {
		public string Kind = "";
		public string A = "";
		public string B = "";
	}

	public sealed class PeDump {
		public string Error = "";
		public string Sign = "";
		public string Pack = "";
		public string Runtime = "";
		public string Framework = "";
		public string Platform = "";
		public string NetFail = "";
		public string Manifest = "";
		public bool Managed;
		public bool ImportCut;
		public bool ExportCut;
		public bool NetCut;
		public readonly List<PeKv> Head = new();
		public readonly List<PeSec> Sections = new();
		public readonly List<PeImp> Imports = new();
		public readonly List<PeExp> Exports = new();
		public readonly List<PeNet> Net = new();
	}

	const int MAXLIST = 20000;

	static readonly int[] RS = { 0x00, 0x1A, 0x23, 0x01 };
	static readonly int[] TDR = { 0x02, 0x01, 0x1B };
	static readonly int[] MRP = { 0x02, 0x01, 0x1A, 0x06, 0x1B };
	static readonly int[] HCA = {
		0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x00, 0x0E, 0x17, 0x14, 0x11,
		0x1A, 0x1B, 0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2C, 0x2B
	};
	static readonly int[] CAT = { 0x06, 0x0A };
	static readonly int[] HC = { 0x04, 0x08, 0x17 };
	static readonly int[] HFM = { 0x04, 0x08 };
	static readonly int[] HDS = { 0x02, 0x06, 0x20 };
	static readonly int[] HS = { 0x14, 0x17 };
	static readonly int[] MDR = { 0x06, 0x0A };
	static readonly int[] MF = { 0x04, 0x06 };
	static readonly int[] IMPL = { 0x26, 0x23, 0x27 };
	static readonly int[] TOMD = { 0x02, 0x06 };

	[DllImport("dbghelp.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
	static extern int UnDecorateSymbolName(string name, StringBuilder output, int maxStringLength, int flags);

	static PeDump pedump(string path) {
		var d = new PeDump();
		try {
			using var bin = new Bin(path);
			if (!bin.Open()) {
				d.Error = "bad";
				return d;
			}
			fillhead(d, bin, path);
			fillsec(d, bin);
			fillimp(d, bin);
			filldelay(d, bin);
			fillexp(d, bin);
			d.Manifest = manifest(bin);
			fillnet(d, bin);
			fillpack(d, bin);
			var managed = d.Managed ? summary(d) : "no";
			d.Head.Add(new PeKv { Key = "managed", Value = managed });
		}
		catch (Exception ex) {
			if (d.Error.Length == 0) d.Error = ex.Message;
		}
		return d;
	}

	static string summary(PeDump d) {
		var s = d.Runtime ?? "";
		if (d.Framework.Length > 0) s = s.Length == 0 ? d.Framework : $"{s}  {d.Framework}";
		if (d.Platform.Length > 0) s = s.Length == 0 ? d.Platform : $"{s}  {d.Platform}";
		return s.Length == 0 ? "yes" : s;
	}

	static void fillhead(PeDump d, Bin bin, string path) {
		if (bin.Time == 0) d.Head.Add(new PeKv { Key = "time", Value = "0" });
		else {
			string t;
			try { t = DateTimeOffset.FromUnixTimeSeconds(bin.Time).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); }
			catch { t = bin.Time.ToString(); }
			d.Head.Add(new PeKv { Key = "time", Value = t });
		}
		d.Head.Add(new PeKv { Key = "entry", Value = $"0x{bin.Entry:X8}" });
		d.Head.Add(new PeKv { Key = "base", Value = $"0x{bin.ImageBase:X}" });
		d.Head.Add(new PeKv { Key = "linker", Value = $"{bin.LinkMaj}.{bin.LinkMin}" });
		var flags = new List<string>();
		if ((bin.CoffChars & 0x2000) != 0) flags.Add("DLL");
		if ((bin.CoffChars & 0x0020) != 0) flags.Add("LargeAddress");
		if ((bin.DllChars & 0x0020) != 0) flags.Add("HighEntropyVA");
		if ((bin.DllChars & 0x0040) != 0) flags.Add("ASLR");
		if ((bin.DllChars & 0x0100) != 0) flags.Add("DEP");
		if ((bin.DllChars & 0x0400) != 0) flags.Add("NoSEH");
		if ((bin.DllChars & 0x1000) != 0) flags.Add("AppContainer");
		if ((bin.DllChars & 0x4000) != 0) flags.Add("CFG");
		if (flags.Count > 0) d.Head.Add(new PeKv { Key = "flags", Value = string.Join(", ", flags) });
		var pdb = pdbpath(bin);
		if (pdb.Length > 0) d.Head.Add(new PeKv { Key = "pdb", Value = pdb });
		d.Sign = signof(path);
		d.Head.Add(new PeKv { Key = "sign", Value = d.Sign });
	}

	static string signof(string path) {
		try {
			var c = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
			var s = (c.Subject ?? "").Trim();
			return s.Length == 0 ? "unsigned" : s;
		}
		catch { return "unsigned"; }
	}

	static string pdbpath(Bin bin) {
		if (!bin.Dir(6, out var rva, out var size)) return "";
		var n = (int)(size / 28);
		if (n < 1) return "";
		if (n > 32) n = 32;
		var ent = bin.ReadRva(rva, n * 28);
		if (ent == null) return "";
		for (var i = 0; i + 28 <= ent.Length && i / 28 < n; i += 28) {
			var type = BitConverter.ToUInt32(ent, i + 12);
			if (type != 2) continue;
			var sz = BitConverter.ToUInt32(ent, i + 16);
			var raw = BitConverter.ToUInt32(ent, i + 24);
			if (raw == 0 || sz < 16) continue;
			var take = (int)Math.Min(sz, 2048);
			var b = bin.Read(raw, take);
			if (b.Length >= 24 && b[0] == (byte)'R' && b[1] == (byte)'S' && b[2] == (byte)'D' && b[3] == (byte)'S')
				return zstr(b, 24);
			if (b.Length >= 16 && b[0] == (byte)'N' && b[1] == (byte)'B' && b[2] == (byte)'1' && b[3] == (byte)'0')
				return zstr(b, 16);
		}
		return "";
	}

	static string zstr(byte[] b, int at) {
		if (at < 0 || at >= b.Length) return "";
		var e = at;
		while (e < b.Length && b[e] != 0) e++;
		if (e == at) return "";
		return Encoding.UTF8.GetString(b, at, e - at).Trim();
	}

	static void fillsec(PeDump d, Bin bin) {
		foreach (var s in bin.Secs) {
			d.Sections.Add(new PeSec {
				Name = s.Name,
				Range = $"VA 0x{s.Va:X8}  {s.Vsz}    File 0x{s.Raw:X8}  {s.Rawsz}",
				Flags = secflags(s.Chars)
			});
		}
	}

	static string secflags(uint ch) {
		var p = new List<string>();
		if ((ch & 0x40000000) != 0) p.Add("R");
		if ((ch & 0x80000000) != 0) p.Add("W");
		if ((ch & 0x20000000) != 0) p.Add("X");
		if ((ch & 0x20) != 0) p.Add("code");
		if ((ch & 0x40) != 0) p.Add("init");
		if ((ch & 0x80) != 0) p.Add("uninit");
		return string.Join(" ", p);
	}

	static void fillimp(PeDump d, Bin bin) {
		if (!bin.Dir(1, out var rva, out _)) return;
		for (var i = 0; i < 4096; i++) {
			var desc = bin.ReadRva(rva + (uint)(i * 20), 20);
			if (desc == null || desc.Length < 20) break;
			var oft = BitConverter.ToUInt32(desc, 0);
			var nameRva = BitConverter.ToUInt32(desc, 12);
			var ft = BitConverter.ToUInt32(desc, 16);
			if (nameRva == 0 && oft == 0 && ft == 0) break;
			var dll = bin.Cstr(nameRva, 260);
			thunks(d, bin, dll, oft != 0 ? oft : ft, "");
		}
	}

	static void filldelay(PeDump d, Bin bin) {
		if (!bin.Dir(13, out var rva, out _)) return;
		for (var i = 0; i < 256; i++) {
			var desc = bin.ReadRva(rva + (uint)(i * 32), 32);
			if (desc == null || desc.Length < 32) break;
			var dllRva = BitConverter.ToUInt32(desc, 4);
			var intRva = BitConverter.ToUInt32(desc, 16);
			if (dllRva == 0) break;
			var dll = bin.Cstr(dllRva, 260);
			if (dll.Length == 0) break;
			thunks(d, bin, dll, intRva, "delay");
		}
	}

	static void thunks(PeDump d, Bin bin, string dll, uint thunkRva, string note) {
		if (thunkRva == 0 || dll.Length == 0) return;
		var step = bin.Pe64 ? 8 : 4;
		for (var slot = 0; slot < 100000; slot++) {
			var b = bin.ReadRva(thunkRva + (uint)(slot * step), step);
			if (b == null || b.Length < step) break;
			ulong v = bin.Pe64 ? BitConverter.ToUInt64(b, 0) : BitConverter.ToUInt32(b, 0);
			if (v == 0) break;
			var ord = bin.Pe64 ? (v & 0x8000000000000000UL) != 0 : (v & 0x80000000UL) != 0;
			string name;
			string mark = note;
			if (ord) {
				name = $"#{v & 0xFFFF}";
				mark = mark.Length == 0 ? "ord" : "delay,ord";
			}
			else name = bin.Cstr((uint)v + 2, 512);
			if (name.Length == 0) name = "?";
			if (d.Imports.Count >= MAXLIST) {
				d.ImportCut = true;
				return;
			}
			d.Imports.Add(new PeImp { Dll = dll, Name = name, Note = mark });
		}
	}

	static void fillexp(PeDump d, Bin bin) {
		if (!bin.Dir(0, out var rva, out var size)) return;
		var dir = bin.ReadRva(rva, 40);
		if (dir == null || dir.Length < 40) return;
		var ordBase = BitConverter.ToUInt32(dir, 16);
		var nfunc = BitConverter.ToInt32(dir, 20);
		var nname = BitConverter.ToInt32(dir, 24);
		var funcRva = BitConverter.ToUInt32(dir, 28);
		var nameRva = BitConverter.ToUInt32(dir, 32);
		var ordRva = BitConverter.ToUInt32(dir, 36);
		if (nfunc <= 0 || nfunc > 200000) return;
		if (nname < 0 || nname > 200000) nname = 0;
		var funcs = bin.ReadRva(funcRva, nfunc * 4);
		if (funcs == null || funcs.Length < nfunc * 4) return;
		var names = nname == 0 ? Array.Empty<byte>() : bin.ReadRva(nameRva, nname * 4);
		var ords = nname == 0 ? Array.Empty<byte>() : bin.ReadRva(ordRva, nname * 2);
		var by = new string[nfunc];
		if (names != null && ords != null) {
			var nn = Math.Min(nname, Math.Min(names.Length / 4, ords.Length / 2));
			for (var i = 0; i < nn; i++) {
				var ix = BitConverter.ToUInt16(ords, i * 2);
				if (ix >= nfunc) continue;
				var nr = BitConverter.ToUInt32(names, i * 4);
				by[ix] = bin.Cstr(nr, 1024);
			}
		}
		for (var i = 0; i < nfunc; i++) {
			var fr = BitConverter.ToUInt32(funcs, i * 4);
			if (fr == 0) continue;
			var name = by[i] ?? "";
			var note = "";
			if (fr >= rva && fr < rva + size) note = bin.Cstr(fr, 512);
			else if (name.Length > 0 && name[0] == '?') note = undecorate(name);
			if (d.Exports.Count >= MAXLIST) {
				d.ExportCut = true;
				return;
			}
			d.Exports.Add(new PeExp {
				Name = name,
				Ord = (ordBase + (uint)i).ToString(),
				Note = note
			});
		}
	}

	static string undecorate(string name) {
		try {
			var sb = new StringBuilder(2048);
			var n = UnDecorateSymbolName(name, sb, sb.Capacity, 0);
			if (n <= 0) return "";
			var s = sb.ToString();
			return s == name ? "" : s;
		}
		catch { return ""; }
	}

	static string manifest(Bin bin) {
		if (!bin.Dir(2, out var rva, out _)) return "";
		return manwalk(bin, rva, rva, 0);
	}

	static string manwalk(Bin bin, uint root, uint dir, int level) {
		var head = bin.ReadRva(dir, 16);
		if (head == null || head.Length < 16 || level > 4) return "";
		var named = BitConverter.ToUInt16(head, 12);
		var ids = BitConverter.ToUInt16(head, 14);
		var n = named + ids;
		if (n <= 0 || n > 4096) return "";
		var ent = bin.ReadRva(dir + 16, n * 8);
		if (ent == null) return "";
		var cnt = Math.Min(n, ent.Length / 8);
		for (var i = 0; i < cnt; i++) {
			var name = BitConverter.ToUInt32(ent, i * 8);
			var off = BitConverter.ToUInt32(ent, i * 8 + 4);
			var id = (name & 0x80000000) != 0 ? -1 : (int)name;
			if (level == 0 && id != 24) continue;
			var sub = (off & 0x80000000) != 0;
			var child = off & 0x7FFFFFFF;
			if (sub) {
				var s = manwalk(bin, root, root + child, level + 1);
				if (s.Length > 0) return s;
				continue;
			}
			var data = bin.ReadRva(root + child, 16);
			if (data == null || data.Length < 8) continue;
			var dataRva = BitConverter.ToUInt32(data, 0);
			var sz = BitConverter.ToInt32(data, 4);
			if (sz <= 0) continue;
			if (sz > 256 * 1024) sz = 256 * 1024;
			var body = bin.ReadRva(dataRva, sz);
			if (body == null || body.Length == 0) continue;
			var text = mantext(body).Trim();
			if (text.Length > 0) return text;
		}
		return "";
	}

	static string mantext(byte[] b) {
		if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
			return Encoding.Unicode.GetString(b, 2, b.Length - 2);
		if (b.Length >= 4 && b[1] == 0 && b[3] == 0)
			return Encoding.Unicode.GetString(b);
		return Encoding.UTF8.GetString(b).TrimStart('\uFEFF');
	}

	static void fillpack(PeDump d, Bin bin) {
		var upx = false;
		foreach (var s in bin.Secs) {
			var n = s.Name ?? "";
			if (n.StartsWith("UPX", StringComparison.OrdinalIgnoreCase)
				|| n.Equals(".vmp0", StringComparison.OrdinalIgnoreCase)
				|| n.Equals(".vmp1", StringComparison.OrdinalIgnoreCase)
				|| n.Equals("MPRESS1", StringComparison.OrdinalIgnoreCase))
				upx = true;
		}
		var hot = false;
		if (!d.Managed && !upx && d.Imports.Count < 8 && !d.ImportCut) {
			var ent = entropy(bin);
			if (ent >= 7.2) hot = true;
		}
		d.Pack = upx || hot ? "yes" : "no";
		d.Head.Add(new PeKv { Key = "pack", Value = d.Pack });
	}

	static double entropy(Bin bin) {
		var best = -1;
		uint bestsz = 0;
		for (var i = 0; i < bin.Secs.Length; i++) {
			if ((bin.Secs[i].Chars & 0x20000000) == 0) continue;
			if (bin.Secs[i].Rawsz <= bestsz) continue;
			bestsz = bin.Secs[i].Rawsz;
			best = i;
		}
		if (best < 0 || bestsz < 64) return 0;
		var n = (int)Math.Min(bestsz, 65536);
		var b = bin.Read(bin.Secs[best].Raw, n);
		if (b.Length < 64) return 0;
		var c = new int[256];
		for (var i = 0; i < b.Length; i++) c[b[i]]++;
		double h = 0;
		for (var i = 0; i < 256; i++) {
			if (c[i] == 0) continue;
			var p = c[i] / (double)b.Length;
			h -= p * Math.Log(p, 2);
		}
		return h;
	}

	static void fillnet(PeDump d, Bin bin) {
		if (!bin.Dir(14, out var rva, out var sz) || sz < 72) return;
		var clr = bin.ReadRva(rva, 72);
		if (clr == null || clr.Length < 72) return;
		d.Managed = true;
		var maj = BitConverter.ToUInt16(clr, 4);
		var min = BitConverter.ToUInt16(clr, 6);
		var flags = BitConverter.ToInt32(clr, 16);
		d.Runtime = clrver(maj, min);
		d.Platform = plat(flags, bin.Machine);
		var metaRva = BitConverter.ToUInt32(clr, 8);
		var metaSz = BitConverter.ToUInt32(clr, 12);
		if (metaRva == 0 || metaSz < 32) {
			d.NetFail = "meta";
			return;
		}
		if (metaSz > 48 * 1024 * 1024) {
			d.NetFail = "meta";
			return;
		}
		var meta = bin.ReadRva(metaRva, (int)metaSz);
		if (meta == null || meta.Length < 32) {
			d.NetFail = "meta";
			return;
		}
		try { readmeta(d, meta); }
		catch (Exception ex) {
			d.Net.Clear();
			d.NetFail = $"{ex.GetType().Name}: {ex.Message}";
		}
	}

	static string plat(int flags, ushort machine) {
		if ((flags & 2) != 0) return "x86";
		if (machine == 0x8664) return "x64";
		if (machine == 0xAA64) return "ARM64";
		if ((flags & 0x20000) != 0) return "AnyCPU, 32-bit preferred";
		if ((flags & 1) != 0) return "AnyCPU";
		return pemachine(machine);
	}

	static void readmeta(PeDump d, byte[] meta) {
		if (meta[0] != (byte)'B' || meta[1] != (byte)'S' || meta[2] != (byte)'J' || meta[3] != (byte)'B')
			throw new InvalidDataException("BSJB");
		if (meta.Length < 20) throw new InvalidDataException("meta");
		var verLen = BitConverter.ToInt32(meta, 12);
		if (verLen < 0 || verLen > 1024 || 16 + verLen > meta.Length) throw new InvalidDataException("ver");
		var verSkip = (verLen + 3) & ~3;
		if (16 + verSkip > meta.Length) verSkip = verLen;
		var rt = zstr(meta, 16);
		if (rt.Length > 0) d.Runtime = rt;
		var p = 16 + verSkip;
		if (p + 4 > meta.Length) throw new InvalidDataException("streams");
		p += 2;
		var nstreams = BitConverter.ToUInt16(meta, p);
		p += 2;
		if (nstreams > 64) throw new InvalidDataException("streams");
		var strOff = -1;
		var strLen = 0;
		var blobOff = -1;
		var blobLen = 0;
		var tilde = -1;
		for (var s = 0; s < nstreams; s++) {
			if (p + 8 > meta.Length) throw new InvalidDataException("stream");
			var off = BitConverter.ToInt32(meta, p);
			var sz = BitConverter.ToInt32(meta, p + 4);
			p += 8;
			var ns = p;
			while (p < meta.Length && meta[p] != 0) p++;
			var name = Encoding.ASCII.GetString(meta, ns, Math.Max(0, p - ns));
			if (p < meta.Length) p++;
			while ((p & 3) != 0 && p < meta.Length) p++;
			if (off < 0 || sz < 0 || (long)off + sz > meta.Length) continue;
			if (name == "#Strings") { strOff = off; strLen = sz; }
			else if (name == "#Blob") { blobOff = off; blobLen = sz; }
			else if (name == "#~" || name == "#-") tilde = off;
		}
		if (tilde < 0 || strOff < 0) throw new InvalidDataException("heaps");
		var img = new Meta(meta, strOff, strLen, blobOff, blobLen);
		img.Walk(d, tilde);
	}

	static string prettyfx(string s) {
		s = (s ?? "").Trim().Trim('\0');
		var a = fxone(s, ".NETFramework,Version=v", ".NET Framework ");
		if (a.Length > 0) return a;
		a = fxone(s, ".NETCoreApp,Version=v", ".NET ");
		if (a.Length > 0) return a;
		a = fxone(s, ".NETStandard,Version=v", ".NET Standard ");
		if (a.Length > 0) return a;
		return s;
	}

	static string fxone(string s, string mark, string prefix) {
		if (!s.StartsWith(mark, StringComparison.Ordinal)) return "";
		var v = s.Substring(mark.Length);
		var i = 0;
		while (i < v.Length && (v[i] == '.' || (v[i] >= '0' && v[i] <= '9'))) i++;
		if (i == 0) return "";
		return prefix + v.Substring(0, i);
	}

	static string tokenof(byte[] key) {
		if (key == null || key.Length == 0) return "";
		using var sha = SHA1.Create();
		var hash = sha.ComputeHash(key);
		var sb = new StringBuilder(16);
		for (var i = 0; i < 8; i++) sb.Append(hash[hash.Length - 1 - i].ToString("x2"));
		return sb.ToString();
	}

	static string keytext(byte[] blob, int flags) {
		if (blob == null || blob.Length == 0) return "";
		if ((flags & 1) != 0) return tokenof(blob);
		var sb = new StringBuilder(16);
		var n = Math.Min(blob.Length, 8);
		for (var i = 0; i < n; i++) sb.Append(blob[i].ToString("x2"));
		return sb.ToString();
	}

	sealed class Bin : IDisposable {
		public struct Sec {
			public string Name;
			public uint Va, Vsz, Raw, Rawsz, Chars;
		}

		readonly FileStream fs;
		byte[] cache;
		long cacheAt = -1;
		public byte[] Hdr = Array.Empty<byte>();
		public int Pe;
		public bool Pe64;
		public ushort Machine;
		public ushort SecCount;
		public int Opt;
		public ushort OptSize;
		public int Dd;
		public int DdCount;
		public uint Entry;
		public ulong ImageBase;
		public ushort DllChars;
		public ushort CoffChars;
		public uint Time;
		public byte LinkMaj, LinkMin;
		public Sec[] Secs = Array.Empty<Sec>();

		public Bin(string path) {
			fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		}

		public void Dispose() => fs.Dispose();

		public bool Open() {
			if (fs.Length < 64) return false;
			var dos = Read(0, 64);
			if (dos.Length < 64 || dos[0] != (byte)'M' || dos[1] != (byte)'Z') return false;
			Pe = BitConverter.ToInt32(dos, 0x3C);
			if (Pe < 0x40 || Pe > 4 * 1024 * 1024) return false;
			if (Pe + 24L > fs.Length) return false;
			var sig = Read(Pe, 24);
			if (sig.Length < 24 || sig[0] != (byte)'P' || sig[1] != (byte)'E' || sig[2] != 0 || sig[3] != 0) return false;
			Machine = BitConverter.ToUInt16(sig, 4);
			SecCount = BitConverter.ToUInt16(sig, 6);
			Time = BitConverter.ToUInt32(sig, 8);
			OptSize = BitConverter.ToUInt16(sig, 20);
			CoffChars = BitConverter.ToUInt16(sig, 22);
			if (SecCount > 256 || OptSize < 96 || OptSize > 1024) return false;
			var need = Pe + 24L + OptSize + SecCount * 40L;
			if (need > fs.Length || need > 8 * 1024 * 1024) return false;
			Hdr = Read(0, (int)need);
			if (Hdr.Length < need) return false;
			Opt = Pe + 24;
			var magic = BitConverter.ToUInt16(Hdr, Opt);
			Pe64 = magic == 0x20b;
			if (magic != 0x10b && magic != 0x20b) return false;
			LinkMaj = Hdr[Opt + 2];
			LinkMin = Hdr[Opt + 3];
			Entry = BitConverter.ToUInt32(Hdr, Opt + 16);
			if (Pe64) ImageBase = BitConverter.ToUInt64(Hdr, Opt + 24);
			else ImageBase = BitConverter.ToUInt32(Hdr, Opt + 28);
			DllChars = BitConverter.ToUInt16(Hdr, Opt + 70);
			var ddCountOff = Opt + (Pe64 ? 108 : 92);
			if (ddCountOff + 4 > Hdr.Length) return false;
			DdCount = (int)BitConverter.ToUInt32(Hdr, ddCountOff);
			if (DdCount < 0) DdCount = 0;
			if (DdCount > 16) DdCount = 16;
			Dd = Opt + (Pe64 ? 112 : 96);
			if (Dd + DdCount * 8 > Hdr.Length) DdCount = Math.Max(0, (Hdr.Length - Dd) / 8);
			var secOff = Opt + OptSize;
			Secs = new Sec[SecCount];
			for (var i = 0; i < SecCount; i++) {
				var at = secOff + i * 40;
				if (at + 40 > Hdr.Length) return false;
				Secs[i].Name = secname(Hdr, at);
				Secs[i].Vsz = BitConverter.ToUInt32(Hdr, at + 8);
				Secs[i].Va = BitConverter.ToUInt32(Hdr, at + 12);
				Secs[i].Rawsz = BitConverter.ToUInt32(Hdr, at + 16);
				Secs[i].Raw = BitConverter.ToUInt32(Hdr, at + 20);
				Secs[i].Chars = BitConverter.ToUInt32(Hdr, at + 36);
			}
			return true;
		}

		static string secname(byte[] b, int at) {
			var n = 0;
			while (n < 8 && b[at + n] != 0) n++;
			if (n == 0) return "";
			return Encoding.ASCII.GetString(b, at, n);
		}

		public bool Dir(int index, out uint rva, out uint size) {
			rva = 0;
			size = 0;
			if (index < 0 || index >= DdCount) return false;
			var at = Dd + index * 8;
			if (at + 8 > Hdr.Length) return false;
			rva = BitConverter.ToUInt32(Hdr, at);
			size = BitConverter.ToUInt32(Hdr, at + 4);
			return rva != 0 && size != 0;
		}

		public int Off(uint rva) {
			for (var i = 0; i < Secs.Length; i++) {
				var s = Secs[i];
				var span = Math.Max(s.Vsz, s.Rawsz);
				if (span == 0 || rva < s.Va || rva >= s.Va + span) continue;
				if (s.Raw == 0) return -1;
				var off = (long)s.Raw + (rva - s.Va);
				if (off < 0 || off > int.MaxValue) return -1;
				return (int)off;
			}
			return -1;
		}

		public byte[] ReadRva(uint rva, int len) {
			var off = Off(rva);
			if (off < 0) return null;
			return Read(off, len);
		}

		public string Cstr(uint rva, int max) {
			var off = Off(rva);
			if (off < 0 || max <= 0) return "";
			var b = Read(off, max);
			var n = 0;
			while (n < b.Length && b[n] != 0) n++;
			if (n == 0) return "";
			return Encoding.UTF8.GetString(b, 0, n);
		}

		public byte[] Read(long off, int len) {
			if (len <= 0 || off < 0 || off >= fs.Length) return Array.Empty<byte>();
			if (off + len > fs.Length) len = (int)(fs.Length - off);
			if (len > 262144) return readraw(off, len);
			const int WIN = 1024 * 1024;
			if (cache == null || off < cacheAt || off + len > cacheAt + cache.Length) {
				var n = (int)Math.Min(WIN, fs.Length - off);
				if (n < len) n = len;
				cache = readraw(off, n);
				cacheAt = off;
			}
			if (cache == null || cache.Length == 0) return Array.Empty<byte>();
			var local = (int)(off - cacheAt);
			var have = Math.Min(len, cache.Length - local);
			if (have <= 0) return Array.Empty<byte>();
			var r = new byte[have];
			Buffer.BlockCopy(cache, local, r, 0, have);
			return r;
		}

		byte[] readraw(long off, int len) {
			var buf = new byte[len];
			fs.Position = off;
			var got = 0;
			while (got < len) {
				var n = fs.Read(buf, got, len - got);
				if (n <= 0) break;
				got += n;
			}
			if (got == len) return buf;
			if (got <= 0) return Array.Empty<byte>();
			var cut = new byte[got];
			Buffer.BlockCopy(buf, 0, cut, 0, got);
			return cut;
		}
	}

	sealed class Meta {
		readonly byte[] meta;
		readonly int strOff, strLen, blobOff, blobLen;
		int p;
		int strSz = 2, guidSz = 2, blobSz = 2;
		int[] counts = Array.Empty<int>();

		struct Td { public int Flags, Name, Ns, Ext, Field0, Method0; }
		struct Md { public int Flags, Name, Sig; }
		struct Fd { public int Flags, Name, Sig; }
		struct Tr { public int Scope, Name, Ns; }
		struct Pr { public int Name, Sig; }
		struct Pm { public int Parent, List; }
		struct Sm { public int Sem, Method, Assoc; }
		struct Mr { public int Parent, Name; }
		struct Im { public int Member, Name, Scope; }
		struct Ar { public int Maj, Min, Bld, Rev, Flags, Key, Name, Cult; }
		struct Ii { public int Class, Raw; }

		Td[] tds = Array.Empty<Td>();
		Md[] mds = Array.Empty<Md>();
		Fd[] fds = Array.Empty<Fd>();
		Tr[] trs = Array.Empty<Tr>();
		Pr[] prs = Array.Empty<Pr>();
		Pm[] pms = Array.Empty<Pm>();
		Sm[] sms = Array.Empty<Sm>();
		Mr[] mrs = Array.Empty<Mr>();
		Im[] ims = Array.Empty<Im>();
		Ar[] ars = Array.Empty<Ar>();
		Ar asm;
		bool hasAsm;
		Ii[] iis = Array.Empty<Ii>();
		int[] specs = Array.Empty<int>();
		string[] mods = Array.Empty<string>();
		Dictionary<int, int> nest = new();
		int[] resName = Array.Empty<int>();

		public Meta(byte[] meta, int strOff, int strLen, int blobOff, int blobLen) {
			this.meta = meta;
			this.strOff = strOff;
			this.strLen = strLen;
			this.blobOff = blobOff;
			this.blobLen = blobLen;
		}

		public void Walk(PeDump d, int tilde) {
			p = tilde;
			if (p + 24 > meta.Length) throw new InvalidDataException("tilde");
			p += 6;
			var heap = meta[p];
			p += 2;
			var valid = BitConverter.ToUInt64(meta, p);
			p += 16;
			strSz = (heap & 1) != 0 ? 4 : 2;
			guidSz = (heap & 2) != 0 ? 4 : 2;
			blobSz = (heap & 4) != 0 ? 4 : 2;
			counts = new int[64];
			for (var t = 0; t < 64; t++) {
				if ((valid & (1UL << t)) == 0) continue;
				if (p + 4 > meta.Length) throw new InvalidDataException("counts");
				counts[t] = BitConverter.ToInt32(meta, p);
				p += 4;
				if (counts[t] < 0) throw new InvalidDataException("counts");
			}
			for (var t = 0; t <= 0x2C; t++) {
				var n = counts[t];
				if (n == 0) continue;
				var w = width(t);
				if (w <= 0) throw new InvalidDataException($"table {t:X2}");
				var bytes = (long)w * n;
				if (p < 0 || p + bytes > meta.Length) throw new InvalidDataException($"table {t:X2}");
				var end = p + (int)bytes;
				if (care(t)) readrows(t, n, w);
				p = end;
			}
			emit(d);
		}

		static bool care(int t) {
			return t == 0x01 || t == 0x02 || t == 0x04 || t == 0x06 || t == 0x09 || t == 0x0A
				|| t == 0x0C || t == 0x15 || t == 0x17 || t == 0x18 || t == 0x1A || t == 0x1B
				|| t == 0x1C || t == 0x20 || t == 0x23 || t == 0x28 || t == 0x29;
		}

		int width(int t) {
			switch (t) {
				case 0x00: return 2 + strSz + guidSz * 3;
				case 0x01: return csz(2, RS) + strSz * 2;
				case 0x02: return 4 + strSz * 2 + csz(2, TDR) + ix(0x04) + ix(0x06);
				case 0x03: return ix(0x04);
				case 0x04: return 2 + strSz + blobSz;
				case 0x05: return ix(0x06);
				case 0x06: return 4 + 2 + 2 + strSz + blobSz + ix(0x08);
				case 0x07: return ix(0x08);
				case 0x08: return 2 + 2 + strSz;
				case 0x09: return ix(0x02) + csz(2, TDR);
				case 0x0A: return csz(3, MRP) + strSz + blobSz;
				case 0x0B: return 2 + csz(2, HC) + blobSz;
				case 0x0C: return csz(5, HCA) + csz(3, CAT) + blobSz;
				case 0x0D: return csz(1, HFM) + blobSz;
				case 0x0E: return 2 + csz(2, HDS) + blobSz;
				case 0x0F: return 2 + 4 + ix(0x02);
				case 0x10: return 4 + ix(0x04);
				case 0x11: return blobSz;
				case 0x12: return ix(0x02) + ix(0x14);
				case 0x13: return ix(0x14);
				case 0x14: return 2 + strSz + csz(2, TDR);
				case 0x15: return ix(0x02) + ix(0x17);
				case 0x16: return ix(0x17);
				case 0x17: return 2 + strSz + blobSz;
				case 0x18: return 2 + ix(0x06) + csz(1, HS);
				case 0x19: return ix(0x02) + csz(1, MDR) * 2;
				case 0x1A: return strSz;
				case 0x1B: return blobSz;
				case 0x1C: return 2 + csz(1, MF) + strSz + ix(0x1A);
				case 0x1D: return 4 + ix(0x04);
				case 0x1E: return 8;
				case 0x1F: return 4;
				case 0x20: return 4 + 8 + 4 + blobSz + strSz * 2;
				case 0x21: return 4;
				case 0x22: return 12;
				case 0x23: return 8 + 4 + blobSz + strSz * 2 + blobSz;
				case 0x24: return 4 + ix(0x23);
				case 0x25: return 12 + ix(0x23);
				case 0x26: return 4 + strSz + blobSz;
				case 0x27: return 4 + 4 + strSz * 2 + csz(2, IMPL);
				case 0x28: return 4 + 4 + strSz + csz(2, IMPL);
				case 0x29: return ix(0x02) * 2;
				case 0x2A: return 2 + 2 + csz(1, TOMD) + strSz;
				case 0x2B: return csz(1, MDR) + blobSz;
				case 0x2C: return ix(0x2A) + csz(2, TDR);
				default: return -1;
			}
		}

		int ix(int table) => counts[table] < 65536 ? 2 : 4;

		int csz(int bits, int[] tables) {
			var max = 0;
			for (var i = 0; i < tables.Length; i++) {
				var t = tables[i];
				if ((uint)t < (uint)counts.Length && counts[t] > max) max = counts[t];
			}
			return max < (1 << (16 - bits)) ? 2 : 4;
		}

		void readrows(int t, int n, int w) {
			if (t == 0x01) trs = new Tr[n];
			else if (t == 0x02) tds = new Td[n];
			else if (t == 0x04) fds = new Fd[n];
			else if (t == 0x06) mds = new Md[n];
			else if (t == 0x09) iis = new Ii[n];
			else if (t == 0x0A) mrs = new Mr[n];
			else if (t == 0x15) pms = new Pm[n];
			else if (t == 0x17) prs = new Pr[n];
			else if (t == 0x18) sms = new Sm[n];
			else if (t == 0x1A) mods = new string[n + 1];
			else if (t == 0x1B) specs = new int[n];
			else if (t == 0x1C) ims = new Im[n];
			else if (t == 0x23) ars = new Ar[n];
			else if (t == 0x28) resName = new int[n];
			for (var i = 0; i < n; i++) {
				var end = p + w;
				switch (t) {
					case 0x01:
						trs[i].Scope = take(csz(2, RS));
						trs[i].Name = take(strSz);
						trs[i].Ns = take(strSz);
						break;
					case 0x02:
						tds[i].Flags = take(4);
						tds[i].Name = take(strSz);
						tds[i].Ns = take(strSz);
						tds[i].Ext = take(csz(2, TDR));
						tds[i].Field0 = take(ix(0x04));
						tds[i].Method0 = take(ix(0x06));
						break;
					case 0x04:
						fds[i].Flags = take(2);
						fds[i].Name = take(strSz);
						fds[i].Sig = take(blobSz);
						break;
					case 0x06:
						take(4);
						take(2);
						mds[i].Flags = take(2);
						mds[i].Name = take(strSz);
						mds[i].Sig = take(blobSz);
						take(ix(0x08));
						break;
					case 0x09:
						iis[i].Class = take(ix(0x02));
						iis[i].Raw = take(csz(2, TDR));
						break;
					case 0x0A:
						mrs[i].Parent = take(csz(3, MRP));
						mrs[i].Name = take(strSz);
						take(blobSz);
						break;
					case 0x0C: {
						var owner = take(csz(5, HCA));
						var ctype = take(csz(3, CAT));
						var blob = take(blobSz);
						if (((owner & 31) == 14) && (owner >> 5) == 1) tryfx(ctype, blob);
						break;
					}
					case 0x15:
						pms[i].Parent = take(ix(0x02));
						pms[i].List = take(ix(0x17));
						break;
					case 0x17:
						take(2);
						prs[i].Name = take(strSz);
						prs[i].Sig = take(blobSz);
						break;
					case 0x18:
						sms[i].Sem = take(2);
						sms[i].Method = take(ix(0x06));
						sms[i].Assoc = take(csz(1, HS));
						break;
					case 0x1A:
						mods[i + 1] = Str(take(strSz));
						break;
					case 0x1B:
						specs[i] = take(blobSz);
						break;
					case 0x1C:
						take(2);
						ims[i].Member = take(csz(1, MF));
						ims[i].Name = take(strSz);
						ims[i].Scope = take(ix(0x1A));
						break;
					case 0x20: {
						take(4);
						var row = new Ar();
						row.Maj = take(2);
						row.Min = take(2);
						row.Bld = take(2);
						row.Rev = take(2);
						row.Flags = take(4);
						row.Key = take(blobSz);
						row.Name = take(strSz);
						row.Cult = take(strSz);
						asm = row;
						hasAsm = true;
						break;
					}
					case 0x23: {
						var row = new Ar();
						row.Maj = take(2);
						row.Min = take(2);
						row.Bld = take(2);
						row.Rev = take(2);
						row.Flags = take(4);
						row.Key = take(blobSz);
						row.Name = take(strSz);
						row.Cult = take(strSz);
						take(blobSz);
						ars[i] = row;
						break;
					}
					case 0x28:
						take(4);
						take(4);
						resName[i] = take(strSz);
						take(csz(2, IMPL));
						break;
					case 0x29: {
						var child = take(ix(0x02));
						var parent = take(ix(0x02));
						if (child > 0 && !nest.ContainsKey(child)) nest[child] = parent;
						break;
					}
				}
				p = end;
			}
		}

		int take(int sz) {
			if (sz == 2) {
				if (p + 2 > meta.Length) return 0;
				var v = BitConverter.ToUInt16(meta, p);
				p += 2;
				return v;
			}
			if (sz != 4 || p + 4 > meta.Length) return 0;
			var n = BitConverter.ToInt32(meta, p);
			p += 4;
			return n;
		}

		void tryfx(int ctype, int blobIx) {
			var tag = ctype & 7;
			var row = ctype >> 3;
			if (tag != 3 || row <= 0 || row > mrs.Length) return;
			var mr = mrs[row - 1];
			if (Str(mr.Name) != ".ctor") return;
			var ptag = mr.Parent & 7;
			var prow = mr.Parent >> 3;
			var attr = ptag == 1 ? TrName(prow, 0) : ptag == 0 ? TdName(prow, 0) : "";
			if (attr != "System.Runtime.Versioning.TargetFrameworkAttribute") return;
			var blob = Blob(blobIx);
			if (blob.Length < 3 || blob[0] != 1 || blob[1] != 0) return;
			var i = 2;
			var s = ser(blob, ref i);
			if (s.Length == 0) return;
			var fx = prettyfx(s);
			if (fx.Length > 0) framework = fx;
		}

		string framework = "";

		string Str(int index) {
			if (index <= 0 || strLen <= 0 || index >= strLen) return "";
			var at = strOff + index;
			var cap = strOff + strLen;
			var e = at;
			var lim = Math.Min(cap, at + 4096);
			while (e < lim && meta[e] != 0) e++;
			if (e == at) return "";
			return Encoding.UTF8.GetString(meta, at, e - at);
		}

		byte[] Blob(int index) {
			if (blobLen <= 0 || index < 0 || index >= blobLen) return Array.Empty<byte>();
			var i = blobOff + index;
			var len = readu(meta, ref i);
			if (len < 0 || i < blobOff || i + len > blobOff + blobLen || i + len > meta.Length) return Array.Empty<byte>();
			var r = new byte[len];
			Buffer.BlockCopy(meta, i, r, 0, len);
			return r;
		}

		static int readu(byte[] b, ref int i) {
			if (i < 0 || i >= b.Length) return -1;
			var a = b[i++];
			if ((a & 0x80) == 0) return a;
			if ((a & 0xC0) == 0x80) {
				if (i >= b.Length) return -1;
				return ((a & 0x3F) << 8) | b[i++];
			}
			if ((a & 0xE0) == 0xC0) {
				if (i + 2 >= b.Length) return -1;
				var v = ((a & 0x1F) << 24) | (b[i] << 16) | (b[i + 1] << 8) | b[i + 2];
				i += 3;
				return v;
			}
			return -1;
		}

		static string ser(byte[] b, ref int i) {
			if (i < b.Length && b[i] == 0xFF) { i++; return ""; }
			var n = readu(b, ref i);
			if (n < 0 || i + n > b.Length) return "";
			var s = Encoding.UTF8.GetString(b, i, n);
			i += n;
			return s;
		}

		void emit(PeDump d) {
			if (framework.Length > 0) d.Framework = framework;
			if (hasAsm) {
				d.Net.Add(new PeNet { Kind = "asm", A = Str(asm.Name), B = reftext(asm) });
			}
			for (var i = 0; i < ars.Length; i++) {
				if (d.Net.Count >= MAXLIST) { d.NetCut = true; return; }
				var name = Str(ars[i].Name);
				if (name.Length == 0) continue;
				d.Net.Add(new PeNet { Kind = "ref", A = name, B = reftext(ars[i]) });
			}
			var owner = new int[mds.Length + 1];
			for (var i = 0; i < tds.Length; i++) {
				var s0 = tds[i].Method0;
				var e0 = i + 1 < tds.Length ? tds[i + 1].Method0 : mds.Length + 1;
				if (s0 < 1) s0 = 1;
				if (e0 < s0) e0 = s0;
				if (e0 > mds.Length + 1) e0 = mds.Length + 1;
				for (var m = s0; m < e0 && m <= mds.Length; m++) owner[m] = i + 1;
			}
			var acc = new HashSet<int>();
			var getter = new Dictionary<int, int>();
			var setter = new Dictionary<int, int>();
			for (var i = 0; i < sms.Length; i++) {
				if ((sms[i].Assoc & 1) != 1) continue;
				var prop = sms[i].Assoc >> 1;
				var method = sms[i].Method;
				if (method <= 0 || method > mds.Length) continue;
				if ((sms[i].Sem & 0x0002) != 0) { getter[prop] = mds[method - 1].Flags; acc.Add(method); }
				else if ((sms[i].Sem & 0x0001) != 0) { setter[prop] = mds[method - 1].Flags; acc.Add(method); }
			}
			for (var i = 0; i < ims.Length; i++) {
				if (d.Net.Count >= MAXLIST) { d.NetCut = true; return; }
				if ((ims[i].Member & 1) != 1) continue;
				var row = ims[i].Member >> 1;
				if (row <= 0 || row > mds.Length) continue;
				var dll = ims[i].Scope > 0 && ims[i].Scope < mods.Length ? mods[ims[i].Scope] ?? "" : "";
				var fn = Str(ims[i].Name);
				if (fn.Length == 0) fn = Str(mds[row - 1].Name);
				var who = owner[row];
				var managed = who > 0 ? $"{TdName(who, 0)}.{Str(mds[row - 1].Name)}" : Str(mds[row - 1].Name);
				d.Net.Add(new PeNet { Kind = "pinvoke", A = $"{dll}!{fn}", B = managed });
			}
			for (var i = 0; i < resName.Length; i++) {
				if (d.Net.Count >= MAXLIST) { d.NetCut = true; return; }
				var name = Str(resName[i]);
				if (name.Length == 0) continue;
				d.Net.Add(new PeNet { Kind = "res", A = name, B = "" });
			}
			for (var row = 1; row <= tds.Length; row++) {
				if (d.Net.Count >= MAXLIST) { d.NetCut = true; return; }
				var t = tds[row - 1];
				var simple = Str(t.Name);
				if (skipname(simple) || Str(t.Ns).IndexOf('<') >= 0) continue;
				if (!exported(row, 0)) continue;
				var full = TdName(row, 0);
				var word = typeword(t);
				var bases = basesof(row, t);
				d.Net.Add(new PeNet { Kind = "type", A = $"{word} {full}", B = bases });
				emitprops(d, row, full, getter, setter);
				emitmethods(d, row, full, acc);
				emitfields(d, row, full);
			}
		}

		string reftext(Ar a) {
			var s = $"{a.Maj}.{a.Min}.{a.Bld}.{a.Rev}";
			var cul = Str(a.Cult);
			if (cul.Length > 0) s = $"{s}  {cul}";
			var tok = keytext(Blob(a.Key), a.Flags);
			if (tok.Length > 0) s = $"{s}  {tok}";
			return s;
		}

		static bool skipname(string n) {
			if (n.Length == 0 || n == "<Module>") return true;
			return n.IndexOf('<') >= 0;
		}

		static bool pub(int flags) => (flags & 7) == 6;

		bool exported(int row, int depth) {
			if (row <= 0 || row > tds.Length || depth > 16) return false;
			var vis = tds[row - 1].Flags & 7;
			if (!nest.TryGetValue(row, out var parent)) return vis == 1;
			if (vis != 2) return false;
			return exported(parent, depth + 1);
		}

		string typeword(Td t) {
			if ((t.Flags & 0x20) != 0) return "interface";
			var b = basename(t.Ext);
			if (b == "System.Enum") return "enum";
			if (b == "System.ValueType") return "struct";
			if (b == "System.MulticastDelegate" || b == "System.Delegate") return "delegate";
			return "class";
		}

		string basesof(int row, Td t) {
			var list = new List<string>();
			var b = basename(t.Ext);
			if (!boring(b) && b.Length > 0) list.Add(b);
			for (var i = 0; i < iis.Length; i++) {
				if (iis[i].Class != row) continue;
				var n = basename(iis[i].Raw);
				if (n.Length == 0 || boring(n)) continue;
				if (!list.Contains(n)) list.Add(n);
			}
			return string.Join(", ", list);
		}

		static bool boring(string b) {
			return b == "System.Object" || b == "System.ValueType" || b == "System.Enum"
				|| b == "System.Delegate" || b == "System.MulticastDelegate";
		}

		string basename(int raw) {
			if (raw <= 0) return "";
			var tag = raw & 3;
			var row = raw >> 2;
			if (row <= 0) return "";
			if (tag == 0) return TdName(row, 0);
			if (tag == 1) return TrName(row, 0);
			if (tag == 2) return tspec(row, 0);
			return "";
		}

		string TdName(int row, int depth) {
			if (row <= 0 || row > tds.Length || depth > 12) return "?";
			var t = tds[row - 1];
			var name = Str(t.Name);
			if (nest.TryGetValue(row, out var parent) && parent > 0 && parent != row)
				return $"{TdName(parent, depth + 1)}.{name}";
			var ns = Str(t.Ns);
			return ns.Length == 0 ? name : $"{ns}.{name}";
		}

		string TrName(int row, int depth) {
			if (row <= 0 || row > trs.Length || depth > 12) return "?";
			var t = trs[row - 1];
			var name = Str(t.Name);
			var tag = t.Scope & 3;
			var prow = t.Scope >> 2;
			if (tag == 3 && prow > 0) return $"{TrName(prow, depth + 1)}.{name}";
			var ns = Str(t.Ns);
			return ns.Length == 0 ? name : $"{ns}.{name}";
		}

		string tspec(int row, int depth) {
			if (row <= 0 || row > specs.Length || depth > 8) return "?";
			var blob = Blob(specs[row - 1]);
			var i = 0;
			return sigtype(blob, ref i, depth + 1);
		}

		void emitprops(PeDump d, int typeRow, string full, Dictionary<int, int> getter, Dictionary<int, int> setter) {
			var found = -1;
			for (var i = 0; i < pms.Length; i++)
				if (pms[i].Parent == typeRow) { found = i; break; }
			if (found < 0) return;
			var s0 = pms[found].List;
			var e0 = found + 1 < pms.Length ? pms[found + 1].List : prs.Length + 1;
			if (s0 < 1) return;
			if (e0 > prs.Length + 1) e0 = prs.Length + 1;
			for (var i = s0; i < e0; i++) {
				if (d.Net.Count >= MAXLIST) { d.NetCut = true; return; }
				getter.TryGetValue(i, out var gf);
				setter.TryGetValue(i, out var sf);
				var hasG = getter.ContainsKey(i);
				var hasS = setter.ContainsKey(i);
				if ((!hasG || !pub(gf)) && (!hasS || !pub(sf))) continue;
				var name = Str(prs[i - 1].Name);
				if (skipname(name)) continue;
				var ty = propsig(Blob(prs[i - 1].Sig));
				var accs = "";
				if (hasG && pub(gf)) accs = "get; ";
				if (hasS && pub(sf)) accs = $"{accs}set; ";
				d.Net.Add(new PeNet { Kind = "prop", A = full, B = $"{ty} {name} {{ {accs}}}" });
			}
		}

		void emitmethods(PeDump d, int typeRow, string full, HashSet<int> acc) {
			var t = tds[typeRow - 1];
			var s0 = t.Method0;
			var e0 = typeRow < tds.Length ? tds[typeRow].Method0 : mds.Length + 1;
			if (s0 < 1) return;
			if (e0 < s0) e0 = s0;
			if (e0 > mds.Length + 1) e0 = mds.Length + 1;
			for (var i = s0; i < e0; i++) {
				if (d.Net.Count >= MAXLIST) { d.NetCut = true; return; }
				if (acc.Contains(i)) continue;
				var m = mds[i - 1];
				if (!pub(m.Flags)) continue;
				var name = Str(m.Name);
				if (name.Length == 0 || name == ".cctor" || name.IndexOf('<') >= 0) continue;
				var pre = "";
				if ((m.Flags & 0x10) != 0) pre = "static ";
				else if ((m.Flags & 0x0400) != 0) pre = "abstract ";
				var blob = Blob(m.Sig);
				var ip = 0;
				string ret, args;
				if (!methodsig(blob, ref ip, 0, out ret, out args))
					d.Net.Add(new PeNet { Kind = "method", A = full, B = $"{pre}{name}" });
				else if (name == ".ctor")
					d.Net.Add(new PeNet { Kind = "method", A = full, B = $"{pre}.ctor({args})" });
				else
					d.Net.Add(new PeNet { Kind = "method", A = full, B = $"{pre}{ret} {name}({args})" });
			}
		}

		void emitfields(PeDump d, int typeRow, string full) {
			var t = tds[typeRow - 1];
			var s0 = t.Field0;
			var e0 = typeRow < tds.Length ? tds[typeRow].Field0 : fds.Length + 1;
			if (s0 < 1) return;
			if (e0 < s0) e0 = s0;
			if (e0 > fds.Length + 1) e0 = fds.Length + 1;
			for (var i = s0; i < e0; i++) {
				if (d.Net.Count >= MAXLIST) { d.NetCut = true; return; }
				var f = fds[i - 1];
				if (!pub(f.Flags)) continue;
				var name = Str(f.Name);
				if (skipname(name)) continue;
				var ty = fieldsig(Blob(f.Sig));
				var pre = (f.Flags & 0x10) != 0 ? "static " : "";
				d.Net.Add(new PeNet { Kind = "field", A = full, B = $"{pre}{ty} {name}" });
			}
		}

		bool methodsig(byte[] s, ref int i, int depth, out string ret, out string args) {
			ret = "";
			args = "";
			if (s == null || s.Length == 0 || i >= s.Length || depth > 8) return false;
			var conv = s[i++];
			if ((conv & 0x10) != 0) readu(s, ref i);
			var n = readu(s, ref i);
			if (n < 0 || n > 64) return false;
			ret = sigtype(s, ref i, depth);
			var ps = new string[n];
			for (var k = 0; k < n; k++) {
				if (i < s.Length && s[i] == 0x41) { i++; ps[k] = "..."; continue; }
				ps[k] = sigtype(s, ref i, depth);
			}
			args = string.Join(", ", ps);
			return ret.Length > 0;
		}

		string fieldsig(byte[] s) {
			if (s == null || s.Length < 2) return "";
			var i = 1;
			return sigtype(s, ref i, 0);
		}

		string propsig(byte[] s) {
			if (s == null || s.Length < 2) return "";
			var i = 1;
			readu(s, ref i);
			return sigtype(s, ref i, 0);
		}

		string sigtype(byte[] s, ref int i, int depth) {
			if (s == null || i >= s.Length || depth > 8) return "?";
			var k = s[i++];
			switch (k) {
				case 0x01: return "void";
				case 0x02: return "bool";
				case 0x03: return "char";
				case 0x04: return "sbyte";
				case 0x05: return "byte";
				case 0x06: return "short";
				case 0x07: return "ushort";
				case 0x08: return "int";
				case 0x09: return "uint";
				case 0x0A: return "long";
				case 0x0B: return "ulong";
				case 0x0C: return "float";
				case 0x0D: return "double";
				case 0x0E: return "string";
				case 0x0F: return $"{sigtype(s, ref i, depth + 1)}*";
				case 0x10: return $"ref {sigtype(s, ref i, depth + 1)}";
				case 0x11:
				case 0x12: return shortname(tokname(readu(s, ref i)));
				case 0x13: return $"!{readu(s, ref i)}";
				case 0x1E: return $"!!{readu(s, ref i)}";
				case 0x1C: return "object";
				case 0x16: return "typedref";
				case 0x18: return "nint";
				case 0x19: return "nuint";
				case 0x1D: return $"{sigtype(s, ref i, depth + 1)}[]";
				case 0x15: return generic(s, ref i, depth);
				case 0x14: return array(s, ref i, depth);
				case 0x1F:
				case 0x20:
					readu(s, ref i);
					return sigtype(s, ref i, depth + 1);
				case 0x45: return sigtype(s, ref i, depth + 1);
				case 0x41: return "...";
				case 0x1B:
					string ret, args;
					methodsig(s, ref i, depth + 1, out ret, out args);
					return "fn";
				default: return "?";
			}
		}

		string generic(byte[] s, ref int i, int depth) {
			if (i >= s.Length) return "?";
			var kind = s[i++];
			if (kind != 0x11 && kind != 0x12) return "?";
			var name = shortname(tokname(readu(s, ref i)));
			var n = readu(s, ref i);
			if (n < 0 || n > 32) return name;
			var args = new string[n];
			for (var a = 0; a < n; a++) args[a] = sigtype(s, ref i, depth + 1);
			return $"{name}<{string.Join(", ", args)}>";
		}

		string array(byte[] s, ref int i, int depth) {
			var elem = sigtype(s, ref i, depth + 1);
			var rank = readu(s, ref i);
			if (rank < 0) return $"{elem}[]";
			var ns = readu(s, ref i);
			if (ns < 0) ns = 0;
			for (var k = 0; k < ns; k++) readu(s, ref i);
			var nl = readu(s, ref i);
			if (nl < 0) nl = 0;
			for (var k = 0; k < nl; k++) readu(s, ref i);
			if (rank <= 1) return $"{elem}[]";
			return $"{elem}[{new string(',', rank - 1)}]";
		}

		string tokname(int raw) {
			if (raw <= 0) return "?";
			var tag = raw & 3;
			var row = raw >> 2;
			if (row <= 0) return "?";
			if (tag == 0) return TdName(row, 0);
			if (tag == 1) return TrName(row, 0);
			if (tag == 2) return tspec(row, 0);
			return "?";
		}

		static string shortname(string n) {
			if (n == "System.Object") return "object";
			if (n == "System.String") return "string";
			if (n == "System.Boolean") return "bool";
			if (n == "System.Char") return "char";
			if (n == "System.SByte") return "sbyte";
			if (n == "System.Byte") return "byte";
			if (n == "System.Int16") return "short";
			if (n == "System.UInt16") return "ushort";
			if (n == "System.Int32") return "int";
			if (n == "System.UInt32") return "uint";
			if (n == "System.Int64") return "long";
			if (n == "System.UInt64") return "ulong";
			if (n == "System.Single") return "float";
			if (n == "System.Double") return "double";
			if (n == "System.Void") return "void";
			if (n == "System.IntPtr") return "nint";
			if (n == "System.UIntPtr") return "nuint";
			return n ?? "?";
		}
	}
}
