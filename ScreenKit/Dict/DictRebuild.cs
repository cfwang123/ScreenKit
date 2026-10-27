using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace ScreenKit;

/// <summary>从核心包（词条原文 + 一份检索键）重建 RDS1 的 dict3.bin。给安装和计时用。</summary>
static class DictRebuild {
	const int Head = 64;
	const int GramChunk = 48 * 1024;

	struct KeyRec {
		public byte[] Key;
		public int At;
		public int N;
	}

	public static void Run(string corePath, string destPath, Action<string> log) {
		var sw = Stopwatch.StartNew();
		var phase = Stopwatch.StartNew();
		using var src = new FileStream(corePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		var meta = readhead(src);
		log?.Invoke($"核心 词条 {meta.NEntries} 键 {meta.NKeys} 原文 {meta.EntryBytes}");
		var keys = readkeys(src, meta);
		var ids = keys.ids;
		var metas = keys.metas;
		log?.Invoke($"读键 {phase.ElapsedMilliseconds} ms");
		phase.Restart();
		var gmap = buildgrams(keys.items, ids, metas, meta.GramChars, log);
		log?.Invoke($"生成 gram {gmap.Count}  {phase.ElapsedMilliseconds} ms");
		phase.Restart();
		var rev = new byte[keys.items.Length][];
		for (var i = 0; i < rev.Length; i++) rev[i] = revutf8(keys.items[i].Key);
		var order = new int[rev.Length];
		for (var i = 0; i < order.Length; i++) order[i] = i;
		Array.Sort(order, (a, b) => cmp(rev[a], rev[b]));
		log?.Invoke($"后缀排序 {phase.ElapsedMilliseconds} ms");
		phase.Restart();
		var tmp = destPath + ".tmp";
		using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.Read)) {
			dst.SetLength(0);
			dst.Write(new byte[256], 0, 256);
			var h = new HeadW {
				NEntries = meta.NEntries,
				NKeys = keys.items.Length,
				KeysPer = meta.KeysPer,
				DirGroup = meta.DirGroup,
				EntriesPer = meta.EntriesPer,
				GramChars = meta.GramChars,
			};
			var pre = writekeys(dst, keys.items, ids, metas, null, null, meta.KeysPer, meta.DirGroup);
			h.OffPrefixData = pre.Data;
			h.PrefixDataEnd = pre.DataEnd;
			h.OffPrefixL1 = pre.L1;
			h.NPrefixBlocks = pre.Blocks;
			h.NPrefixL1 = pre.L1n;
			log?.Invoke($"前缀 {phase.ElapsedMilliseconds} ms");
			phase.Restart();
			var suf = writekeys(dst, keys.items, ids, metas, order, rev, meta.KeysPer, meta.DirGroup);
			h.OffSuffixData = suf.Data;
			h.SuffixDataEnd = suf.DataEnd;
			h.OffSuffixL1 = suf.L1;
			h.NSuffixBlocks = suf.Blocks;
			h.NSuffixL1 = suf.L1n;
			rev = null;
			log?.Invoke($"后缀 {phase.ElapsedMilliseconds} ms");
			phase.Restart();
			var gram = writegrams(dst, gmap, keys.items, ids, metas, meta.DirGroup);
			h.NGrams = gram.N;
			h.NGramBlocks = gram.Blocks;
			h.OffGramIds = gram.Ids;
			h.GramIdsEnd = gram.IdsEnd;
			h.OffGramRecs = gram.Rec;
			h.GramRecsEnd = gram.RecEnd;
			h.OffGramL1 = gram.L1;
			h.NGramL1 = gram.L1n;
			gmap = null;
			log?.Invoke($"gram 写入 {phase.ElapsedMilliseconds} ms");
			phase.Restart();
			var ent = writeentries(dst, src, meta);
			h.OffEntryData = ent.Data;
			h.EntryDataEnd = ent.DataEnd;
			h.OffEntryIndex = ent.Index;
			h.NEntryBlocks = ent.Blocks;
			h.EntryRaw = ent.Raw;
			h.NEntries = ent.N;
			h.FileSize = dst.Length;
			writeheader(dst, h);
		}
		if (File.Exists(destPath)) File.Delete(destPath);
		File.Move(tmp, destPath);
		log?.Invoke($"词条压缩 {phase.ElapsedMilliseconds} ms");
		var len = new FileInfo(destPath).Length;
		var peak = Process.GetCurrentProcess().PeakWorkingSet64;
		log?.Invoke($"重建 {sw.ElapsedMilliseconds} ms  文件 {len}  峰值工作集 {peak}");
		DictDb.Close();
		if (!DictDb.Init(destPath))
			throw new InvalidOperationException("重建文件打不开: " + DictDb.Error);
		DictDb.Close();
		log?.Invoke("打开校验通过");
	}

	struct CoreHead {
		public int GramChars;
		public int KeysPer;
		public int DirGroup;
		public int EntriesPer;
		public int NEntries;
		public int NKeys;
		public long EntryBytes;
		public long KeyBytes;
	}

	static CoreHead readhead(FileStream src) {
		var b = new byte[Head];
		if (src.Read(b, 0, Head) != Head) throw new InvalidOperationException("核心包太短");
		if (b[0] != (byte)'D' || b[1] != (byte)'3' || b[2] != (byte)'C' || b[3] != (byte)'1')
			throw new InvalidOperationException("不是核心包");
		if (u16(b, 4) != 1) throw new InvalidOperationException("核心包版本不对");
		return new CoreHead {
			GramChars = u16(b, 6),
			KeysPer = u16(b, 8),
			DirGroup = u16(b, 10),
			EntriesPer = u16(b, 12),
			NEntries = (int)u32(b, 14),
			NKeys = (int)u32(b, 18),
			EntryBytes = (long)u64(b, 22),
			KeyBytes = (long)u64(b, 30),
		};
	}

	static (KeyRec[] items, int[] ids, byte[] metas) readkeys(FileStream src, CoreHead meta) {
		src.Seek(Head + meta.EntryBytes, SeekOrigin.Begin);
		var buf = new byte[meta.KeyBytes];
		readfull(src, buf);
		var items = new KeyRec[meta.NKeys];
		var idList = new List<int>(meta.NKeys * 2);
		var metaList = new List<byte>(meta.NKeys * 2);
		var p = 0;
		for (var i = 0; i < meta.NKeys; i++) {
			var klen = u16(buf, p);
			p += 2;
			var key = new byte[klen];
			Buffer.BlockCopy(buf, p, key, 0, klen);
			p += klen;
			var np = u16(buf, p);
			p += 2;
			var at = idList.Count;
			for (var j = 0; j < np; j++) {
				idList.Add((int)u32(buf, p));
				metaList.Add(buf[p + 4]);
				p += 5;
			}
			items[i] = new KeyRec { Key = key, At = at, N = np };
		}
		if (p != buf.Length) throw new InvalidOperationException("检索键尾部多余");
		return (items, idList.ToArray(), metaList.ToArray());
	}

	static Dictionary<string, List<int>> buildgrams(KeyRec[] items, int[] ids, byte[] metas, int gramChars, Action<string> log) {
		var map = new Dictionary<string, List<int>>(800000);
		for (var i = 0; i < items.Length; i++) {
			var rec = items[i];
			if (!qualify(metas, rec.At, rec.N, rec.Key, gramChars, out var text)) continue;
			var n = text.Length - gramChars + 1;
			for (var s = 0; s < n; s++) {
				var g = text.Substring(s, gramChars);
				if (!map.TryGetValue(g, out var list)) {
					list = new List<int>(2);
					map[g] = list;
				}
				list.Add(i);
			}
			if (i > 0 && (i % 500000) == 0) log?.Invoke($"  gram 键 {i}");
		}
		return map;
	}

	static bool qualify(byte[] metas, int at, int n, byte[] key, int gramChars, out string text) {
		text = null;
		if (gramChars < 1 || key == null || key.Length == 0) return false;
		var head = false;
		var ja = false;
		for (var i = 0; i < n; i++) {
			var m = metas[at + i];
			if ((m & 4) != 0) head = true;
			if ((m & 3) == 1) ja = true;
		}
		if (!head && !ja) return false;
		text = Encoding.UTF8.GetString(key);
		if (text.Length < 3 || text.Length < gramChars) return false;
		if (head) return true;
		return ja && hanonly(text);
	}

	static bool hanonly(string s) {
		for (var i = 0; i < s.Length; i++) {
			var o = (int)s[i];
			var ok = (o >= 0x3400 && o <= 0x4DBF) || (o >= 0x4E00 && o <= 0x9FFF) || (o >= 0xF900 && o <= 0xFAFF) || o == 0x3007;
			if (!ok) return false;
		}
		return s.Length > 0;
	}

	struct SecW {
		public long Data;
		public long DataEnd;
		public long L1;
		public int Blocks;
		public int L1n;
	}

	static byte[] sortkey(KeyRec[] items, int[] order, byte[][] rev, int slot) {
		var lex = order == null ? slot : order[slot];
		return rev == null ? items[lex].Key : rev[lex];
	}

	static SecW writekeys(FileStream dst, KeyRec[] items, int[] ids, byte[] metas, int[] order, byte[][] rev, int per, int dirGroup) {
		var data = dst.Position;
		var recs = new List<(byte[] key, long off, int len, int lex)>();
		var buf = new List<int>();
		void flush() {
			if (buf.Count == 0) return;
			var off = dst.Position;
			var first = order == null ? buf[0] : order[buf[0]];
			packkeyblock(dst, buf, items, ids, metas, order, rev);
			var len = (int)(dst.Position - off);
			recs.Add((sortkey(items, order, rev, buf[0]), off, len, first));
			buf.Clear();
		}
		var n = order == null ? items.Length : order.Length;
		for (var i = 0; i < n; i++) {
			buf.Add(i);
			if (buf.Count >= per) flush();
		}
		flush();
		var dataEnd = dst.Position;
		var dirs = writedirs(dst, recs, dirGroup);
		return new SecW { Data = data, DataEnd = dataEnd, L1 = dirs.off, Blocks = recs.Count, L1n = dirs.n };
	}

	static void packkeyblock(FileStream dst, List<int> idx, KeyRec[] items, int[] ids, byte[] metas, int[] order, byte[][] rev) {
		w16(dst, idx.Count);
		byte[] prev = null;
		foreach (var slot in idx) {
			var lex = order == null ? slot : order[slot];
			var key = rev == null ? items[lex].Key : rev[lex];
			var shared = lcp(prev, key);
			var rest = key.Length - shared;
			w16(dst, shared);
			w16(dst, rest);
			dst.Write(key, shared, rest);
			var rec = items[lex];
			w32(dst, lex);
			w16(dst, rec.N);
			for (var j = 0; j < rec.N; j++) {
				w32(dst, ids[rec.At + j]);
				dst.WriteByte(metas[rec.At + j]);
			}
			prev = key;
		}
	}

	struct GramW {
		public int N;
		public int Blocks;
		public long Ids;
		public long IdsEnd;
		public long Rec;
		public long RecEnd;
		public long L1;
		public int L1n;
	}

	static GramW writegrams(FileStream dst, Dictionary<string, List<int>> map, KeyRec[] items, int[] ids, byte[] metas, int dirGroup) {
		var names = new string[map.Count];
		map.Keys.CopyTo(names, 0);
		Array.Sort(names, StringComparer.Ordinal);
		var idsBegin = dst.Position;
		var rows = new List<(byte[] g, long idAt, int idLen, int slot, int inner, int blen)>();
		var blocks = new List<(long off, int len)>();
		var chunk = new MemoryStream();
		var chunkRows = new List<int>();
		void flush() {
			if (chunkRows.Count == 0) return;
			var raw = chunk.ToArray();
			var comp = zlib(raw);
			var off = dst.Position;
			dst.Write(comp, 0, comp.Length);
			blocks.Add((off, comp.Length));
			var slot = blocks.Count - 1;
			foreach (var ri in chunkRows) {
				var row = rows[ri];
				row.slot = slot;
				rows[ri] = row;
			}
			chunkRows.Clear();
			chunk.SetLength(0);
		}
		foreach (var name in names) {
			var list = map[name];
			list.Sort();
			var uniq = new List<int>(list.Count);
			var last = -1;
			foreach (var x in list) {
				if (x == last) continue;
				uniq.Add(x);
				last = x;
			}
			var g = Encoding.UTF8.GetBytes(name);
			var idAt = dst.Position;
			foreach (var lex in uniq) w32(dst, lex);
			var body = grambody(uniq, items, ids, metas);
			if (chunk.Length > 0 && chunk.Length + body.Length > GramChunk) flush();
			var inner = (int)chunk.Length;
			chunk.Write(body, 0, body.Length);
			rows.Add((g, idAt, uniq.Count * 4, -1, inner, body.Length));
			chunkRows.Add(rows.Count - 1);
			if (body.Length >= GramChunk) flush();
		}
		flush();
		var idsEnd = dst.Position;
		long recOff, recEnd;
		if (blocks.Count > 0) {
			recOff = blocks[0].off;
			recEnd = blocks[blocks.Count - 1].off + blocks[blocks.Count - 1].len;
		}
		else {
			recOff = idsEnd;
			recEnd = idsEnd;
		}
		var gramRecs = new List<(byte[] key, uint a, uint b, uint c, uint d, uint e, uint f)>();
		foreach (var row in rows) {
			var blk = blocks[row.slot];
			gramRecs.Add((row.g, (uint)row.idAt, (uint)row.idLen, (uint)blk.off, (uint)blk.len, (uint)row.inner, (uint)row.blen));
		}
		var l1 = writegramdirs(dst, gramRecs, dirGroup);
		return new GramW {
			N = gramRecs.Count,
			Blocks = blocks.Count,
			Ids = idsBegin,
			IdsEnd = idsEnd,
			Rec = recOff,
			RecEnd = recEnd,
			L1 = l1.off,
			L1n = l1.n,
		};
	}

	static byte[] grambody(List<int> lexes, KeyRec[] items, int[] ids, byte[] metas) {
		var ms = new MemoryStream();
		w32(ms, lexes.Count);
		var offs = new int[lexes.Count];
		var rec = new MemoryStream();
		for (var i = 0; i < lexes.Count; i++) {
			offs[i] = (int)rec.Length;
			var lex = lexes[i];
			var key = items[lex].Key;
			var n = items[lex].N;
			w16(rec, key.Length);
			rec.Write(key, 0, key.Length);
			w16(rec, n);
			for (var j = 0; j < n; j++) {
				w32(rec, ids[items[lex].At + j]);
				rec.WriteByte(metas[items[lex].At + j]);
			}
		}
		for (var i = 0; i < offs.Length; i++) w32(ms, offs[i]);
		var body = rec.ToArray();
		ms.Write(body, 0, body.Length);
		return ms.ToArray();
	}

	struct EntW {
		public long Data;
		public long DataEnd;
		public long Index;
		public int Blocks;
		public long Raw;
		public int N;
	}

	static EntW writeentries(FileStream dst, FileStream src, CoreHead meta) {
		src.Seek(Head, SeekOrigin.Begin);
		var raw = new byte[meta.EntryBytes];
		readfull(src, raw);
		var data = dst.Position;
		var blocks = new List<(long off, int len)>();
		var p = 0;
		var n = 0;
		var nIn = 0;
		var start = 0;
		void flush() {
			if (nIn == 0) return;
			var slice = new byte[p - start];
			Buffer.BlockCopy(raw, start, slice, 0, slice.Length);
			var comp = zlib(slice);
			var off = dst.Position;
			dst.Write(comp, 0, comp.Length);
			blocks.Add((off, comp.Length));
			start = p;
			nIn = 0;
		}
		while (p < raw.Length) {
			if (p + 10 > raw.Length) throw new InvalidOperationException("词条截断");
			p += 10;
			for (var k = 0; k < 4; k++) {
				var ln = (int)u32(raw, p);
				p += 4 + ln;
				if (p > raw.Length) throw new InvalidOperationException("词条字段截断");
			}
			n++;
			nIn++;
			if (nIn >= meta.EntriesPer) flush();
		}
		flush();
		if (n != meta.NEntries) throw new InvalidOperationException("词条数对不上");
		var dataEnd = dst.Position;
		var index = dst.Position;
		w32(dst, blocks.Count);
		foreach (var b in blocks) {
			w64(dst, b.off);
			w32(dst, b.len);
		}
		return new EntW { Data = data, DataEnd = dataEnd, Index = index, Blocks = blocks.Count, Raw = raw.Length, N = n };
	}

	struct HeadW {
		public int NEntries;
		public int NKeys;
		public int NGrams;
		public int KeysPer;
		public int DirGroup;
		public int EntriesPer;
		public int GramChars;
		public int NPrefixBlocks;
		public int NSuffixBlocks;
		public int NGramBlocks;
		public int NEntryBlocks;
		public int NPrefixL1;
		public int NSuffixL1;
		public int NGramL1;
		public long EntryRaw;
		public long OffPrefixL1;
		public long OffSuffixL1;
		public long OffGramL1;
		public long OffEntryIndex;
		public long OffPrefixData;
		public long OffSuffixData;
		public long OffGramIds;
		public long OffGramRecs;
		public long OffEntryData;
		public long PrefixDataEnd;
		public long SuffixDataEnd;
		public long GramIdsEnd;
		public long GramRecsEnd;
		public long EntryDataEnd;
		public long FileSize;
	}

	static void writeheader(FileStream dst, HeadW h) {
		var b = new byte[256];
		b[0] = (byte)'R';
		b[1] = (byte)'D';
		b[2] = (byte)'S';
		b[3] = (byte)'1';
		p16(b, 4, 1);
		p32(b, 8, h.NEntries);
		p32(b, 12, h.NKeys);
		p32(b, 16, h.NGrams);
		p16(b, 20, h.KeysPer);
		p16(b, 22, h.DirGroup);
		p16(b, 24, h.EntriesPer);
		p16(b, 26, h.GramChars);
		p32(b, 28, h.NPrefixBlocks);
		p32(b, 32, h.NSuffixBlocks);
		p32(b, 36, h.NGramBlocks);
		p32(b, 40, h.NEntryBlocks);
		p32(b, 44, h.NPrefixL1);
		p32(b, 48, h.NSuffixL1);
		p32(b, 52, h.NGramL1);
		p16(b, 56, 1);
		p64(b, 60, (ulong)h.EntryRaw);
		var offs = new long[] {
			h.OffPrefixL1, h.OffSuffixL1, h.OffGramL1, h.OffEntryIndex,
			h.OffPrefixData, h.OffSuffixData, h.OffGramIds, h.OffGramRecs, h.OffEntryData,
			h.PrefixDataEnd, h.SuffixDataEnd, h.GramIdsEnd, h.GramRecsEnd, h.EntryDataEnd,
			h.FileSize,
		};
		var p = 68;
		foreach (var off in offs) {
			p64(b, p, (ulong)off);
			p += 8;
		}
		dst.Seek(0, SeekOrigin.Begin);
		dst.Write(b, 0, b.Length);
	}

	static (long off, int n) writedirs(FileStream dst, List<(byte[] key, long off, int len, int lex)> recs, int dirGroup) {
		var l1 = new List<(byte[] key, long off, int len)>();
		for (var i = 0; i < recs.Count; i += dirGroup) {
			var n = Math.Min(dirGroup, recs.Count - i);
			var off = dst.Position;
			w16(dst, n);
			for (var j = 0; j < n; j++) {
				var r = recs[i + j];
				w16(dst, r.key.Length);
				dst.Write(r.key, 0, r.key.Length);
				w32(dst, (int)r.off);
				w32(dst, r.len);
				w32(dst, r.lex);
				w32(dst, 0);
				w32(dst, 0);
				w32(dst, 0);
			}
			l1.Add((recs[i].key, off, (int)(dst.Position - off)));
		}
		var l1off = dst.Position;
		w32(dst, l1.Count);
		foreach (var it in l1) {
			w16(dst, it.key.Length);
			dst.Write(it.key, 0, it.key.Length);
			w64(dst, it.off);
			w32(dst, it.len);
		}
		return (l1off, l1.Count);
	}

	static (long off, int n) writegramdirs(FileStream dst, List<(byte[] key, uint a, uint b, uint c, uint d, uint e, uint f)> recs, int dirGroup) {
		var l1 = new List<(byte[] key, long off, int len)>();
		for (var i = 0; i < recs.Count; i += dirGroup) {
			var n = Math.Min(dirGroup, recs.Count - i);
			var off = dst.Position;
			w16(dst, n);
			for (var j = 0; j < n; j++) {
				var r = recs[i + j];
				w16(dst, r.key.Length);
				dst.Write(r.key, 0, r.key.Length);
				w32(dst, (int)r.a);
				w32(dst, (int)r.b);
				w32(dst, (int)r.c);
				w32(dst, (int)r.d);
				w32(dst, (int)r.e);
				w32(dst, (int)r.f);
			}
			l1.Add((recs[i].key, off, (int)(dst.Position - off)));
		}
		var l1off = dst.Position;
		w32(dst, l1.Count);
		foreach (var it in l1) {
			w16(dst, it.key.Length);
			dst.Write(it.key, 0, it.key.Length);
			w64(dst, it.off);
			w32(dst, it.len);
		}
		return (l1off, l1.Count);
	}

	static byte[] zlib(byte[] raw) {
		var ms = new MemoryStream(raw.Length / 2 + 64);
		ms.WriteByte(0x78);
		ms.WriteByte(0x9C);
		using (var def = new DeflateStream(ms, CompressionLevel.Optimal, true))
			def.Write(raw, 0, raw.Length);
		var sum = adler(raw);
		ms.WriteByte((byte)(sum >> 24));
		ms.WriteByte((byte)(sum >> 16));
		ms.WriteByte((byte)(sum >> 8));
		ms.WriteByte((byte)sum);
		return ms.ToArray();
	}

	static uint adler(byte[] data) {
		uint a = 1, b = 0;
		var i = 0;
		while (i < data.Length) {
			var end = Math.Min(data.Length, i + 5552);
			for (; i < end; i++) {
				a += data[i];
				b += a;
			}
			a %= 65521;
			b %= 65521;
		}
		return (b << 16) | a;
	}

	static byte[] revutf8(byte[] b) {
		var o = new byte[b.Length];
		var i = 0;
		var w = b.Length;
		while (i < b.Length) {
			var c = b[i];
			var len = 1;
			if ((c & 0x80) == 0) len = 1;
			else if ((c & 0xE0) == 0xC0) len = 2;
			else if ((c & 0xF0) == 0xE0) len = 3;
			else if ((c & 0xF8) == 0xF0) len = 4;
			if (i + len > b.Length) len = 1;
			w -= len;
			Buffer.BlockCopy(b, i, o, w, len);
			i += len;
		}
		return o;
	}

	static int cmp(byte[] a, byte[] b) {
		var n = a.Length < b.Length ? a.Length : b.Length;
		for (var i = 0; i < n; i++) {
			if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
		}
		return a.Length.CompareTo(b.Length);
	}

	static int lcp(byte[] a, byte[] b) {
		if (a == null || b == null) return 0;
		var n = a.Length < b.Length ? a.Length : b.Length;
		var i = 0;
		while (i < n && a[i] == b[i]) i++;
		return i;
	}

	static void readfull(FileStream s, byte[] buf) {
		var got = 0;
		while (got < buf.Length) {
			var n = s.Read(buf, got, buf.Length - got);
			if (n <= 0) throw new EndOfStreamException();
			got += n;
		}
	}

	static int u16(byte[] b, int i) => b[i] | (b[i + 1] << 8);
	static uint u32(byte[] b, int i) => (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));
	static ulong u64(byte[] b, int i) => u32(b, i) | ((ulong)u32(b, i + 4) << 32);

	static void w16(Stream s, int v) {
		s.WriteByte((byte)v);
		s.WriteByte((byte)(v >> 8));
	}

	static void w32(Stream s, int v) {
		s.WriteByte((byte)v);
		s.WriteByte((byte)(v >> 8));
		s.WriteByte((byte)(v >> 16));
		s.WriteByte((byte)(v >> 24));
	}

	static void w64(Stream s, long v) {
		w32(s, (int)v);
		w32(s, (int)(v >> 32));
	}

	static void p16(byte[] b, int i, int v) {
		b[i] = (byte)v;
		b[i + 1] = (byte)(v >> 8);
	}

	static void p32(byte[] b, int i, int v) {
		b[i] = (byte)v;
		b[i + 1] = (byte)(v >> 8);
		b[i + 2] = (byte)(v >> 16);
		b[i + 3] = (byte)(v >> 24);
	}

	static void p64(byte[] b, int i, ulong v) {
		p32(b, i, (int)v);
		p32(b, i + 4, (int)(v >> 32));
	}
}
