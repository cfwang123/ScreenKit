using System.Text;

namespace WpfOCR;

/// <summary>
/// 纯 C# SentencePiece Unigram（.spm / ModelProto），不依赖 Python。
/// 归一化近似 nmt_nfkc（FormKC + 空白折叠 + dummy prefix + ▁）。
/// </summary>
sealed class SentencePieceUnigram {
	const char SpSpace = '\u2581'; // ▁

	readonly string[] pieces;
	readonly float[] scores;
	readonly int[] types;
	readonly int unkId;
	readonly TrieNode root;
	readonly bool addDummyPrefix;
	readonly bool removeExtraWs;

	SentencePieceUnigram(
		string[] pieces, float[] scores, int[] types, int unkId,
		bool addDummyPrefix, bool removeExtraWs) {
		this.pieces = pieces;
		this.scores = scores;
		this.types = types;
		this.unkId = unkId >= 0 && unkId < pieces.Length ? unkId : 0;
		this.addDummyPrefix = addDummyPrefix;
		this.removeExtraWs = removeExtraWs;
		root = buildtrie(pieces, types);
	}

	public int VocabSize => pieces.Length;
	public string IdToPiece(int id) =>
		id >= 0 && id < pieces.Length ? pieces[id] : pieces[unkId];

	public static SentencePieceUnigram Load(string path) {
		var bytes = File.ReadAllBytes(path);
		return Parse(bytes);
	}

	public static SentencePieceUnigram Parse(byte[] data) {
		var pieceList = new List<(string piece, float score, int type)>();
		bool addDummy = true;
		bool removeExtra = true;
		int unkId = 0;

		var r = new PbReader(data);
		while (r.HasMore) {
			var (field, wt) = r.ReadTag();
			if (field == 1 && wt == 2) {
				// pieces
				var sub = r.ReadBytes();
				string piece = null;
				float score = 0;
				int type = 1;
				var sr = new PbReader(sub);
				while (sr.HasMore) {
					var (f, w) = sr.ReadTag();
					if (f == 1 && w == 2) piece = sr.ReadString();
					else if (f == 2 && w == 5) score = sr.ReadFloat();
					else if (f == 3 && w == 0) type = (int)sr.ReadVarint();
					else sr.Skip(w);
				}
				pieceList.Add((piece ?? "", score, type));
			}
			else if (field == 2 && wt == 2) {
				// trainer_spec：取 unk_id
				var sub = r.ReadBytes();
				var sr = new PbReader(sub);
				while (sr.HasMore) {
					var (f, w) = sr.ReadTag();
					if (f == 37 && w == 0) unkId = (int)sr.ReadVarint(); // unk_id = 37
					else sr.Skip(w);
				}
			}
			else if (field == 3 && wt == 2) {
				// normalizer_spec
				var sub = r.ReadBytes();
				var sr = new PbReader(sub);
				while (sr.HasMore) {
					var (f, w) = sr.ReadTag();
					if (f == 3 && w == 0) addDummy = sr.ReadVarint() != 0; // add_dummy_prefix
					else if (f == 4 && w == 0) removeExtra = sr.ReadVarint() != 0;
					else sr.Skip(w);
				}
			}
			else r.Skip(wt);
		}

		if (pieceList.Count == 0)
			throw new InvalidOperationException("SentencePiece 模型无 pieces");

		var pieces = new string[pieceList.Count];
		var scores = new float[pieceList.Count];
		var types = new int[pieceList.Count];
		for (var i = 0; i < pieceList.Count; i++) {
			pieces[i] = pieceList[i].piece;
			scores[i] = pieceList[i].score;
			types[i] = pieceList[i].type;
		}
		return new SentencePieceUnigram(pieces, scores, types, unkId, addDummy, removeExtra);
	}

	/// <summary>编码为 piece 字符串列表（不含 EOS）。</summary>
	public List<string> EncodeAsPieces(string text) {
		var norm = normalize(text ?? "");
		if (norm.Length == 0) return new List<string>();
		var ids = encodeids(norm);
		var list = new List<string>(ids.Count);
		foreach (var id in ids)
			list.Add(IdToPiece(id));
		return list;
	}

	/// <summary>piece 列表解码为文本（▁ → 空格）。</summary>
	public string DecodePieces(IEnumerable<string> pieceStrs) {
		if (pieceStrs == null) return "";
		var sb = new StringBuilder();
		foreach (var p in pieceStrs) {
			if (string.IsNullOrEmpty(p)) continue;
			if (p is "<unk>" or "<s>" or "</s>" or "<pad>") continue;
			sb.Append(p);
		}
		return sb.ToString().Replace(SpSpace, ' ').Trim();
	}

	string normalize(string text) {
		// 近似 nmt_nfkc：FormKC + 空白
		var s = text.Normalize(NormalizationForm.FormKC);
		if (removeExtraWs) {
			var sb = new StringBuilder(s.Length);
			var prevSpace = true; // 去掉首尾多余空白
			foreach (var ch in s) {
				if (char.IsWhiteSpace(ch)) {
					if (!prevSpace) {
						sb.Append(' ');
						prevSpace = true;
					}
				}
				else {
					sb.Append(ch);
					prevSpace = false;
				}
			}
			// 去尾空格
			var len = sb.Length;
			while (len > 0 && sb[len - 1] == ' ') len--;
			s = sb.ToString(0, len);
		}
		if (addDummyPrefix && s.Length > 0 && s[0] != ' ')
			s = " " + s;
		// escape whitespaces → ▁
		return s.Replace(' ', SpSpace);
	}

	List<int> encodeids(string s) {
		var n = s.Length;
		// best score ending at i, and backpointer (prev pos, piece id)
		var best = new float[n + 1];
		var prev = new int[n + 1];
		var pieceAt = new int[n + 1];
		for (var i = 0; i <= n; i++) {
			best[i] = float.NegativeInfinity;
			prev[i] = -1;
			pieceAt[i] = unkId;
		}
		best[0] = 0;

		for (var i = 0; i < n; i++) {
			if (float.IsNegativeInfinity(best[i])) continue;
			// 从 trie 匹配 s[i..]
			var node = root;
			for (var j = i; j < n; j++) {
				if (!node.Next.TryGetValue(s[j], out node)) break;
				if (node.PieceId >= 0) {
					var score = best[i] + scores[node.PieceId];
					var end = j + 1;
					if (score > best[end]) {
						best[end] = score;
						prev[end] = i;
						pieceAt[end] = node.PieceId;
					}
				}
			}
			// 单字 unk 回退（保证可达）
			if (i < n) {
				var end = i + 1;
				// 若没有任何正常边覆盖，至少前进 1 字符
				var hasChild = false;
				// 简化：若 best[end] 仍 -inf，用 unk
				// 下面在循环后处理
				_ = hasChild;
			}
		}

		// 填补不可达位置：逐字符 unk
		for (var i = 0; i < n; i++) {
			if (float.IsNegativeInfinity(best[i])) continue;
			var end = i + 1;
			if (float.IsNegativeInfinity(best[end])) {
				best[end] = best[i] + (unkId < scores.Length ? scores[unkId] : -100f);
				prev[end] = i;
				pieceAt[end] = unkId;
			}
		}
		// 若终点仍不可达，强制填满
		if (float.IsNegativeInfinity(best[n])) {
			for (var i = 0; i < n; i++) {
				if (float.IsNegativeInfinity(best[i + 1])) {
					best[i + 1] = (float.IsNegativeInfinity(best[i]) ? 0 : best[i]) - 100f;
					prev[i + 1] = i;
					pieceAt[i + 1] = unkId;
				}
			}
		}

		var rev = new List<int>();
		for (var pos = n; pos > 0; ) {
			var id = pieceAt[pos];
			var p = prev[pos];
			if (p < 0) break;
			rev.Add(id);
			pos = p;
		}
		rev.Reverse();
		return rev;
	}

	static TrieNode buildtrie(string[] pieces, int[] types) {
		var root = new TrieNode();
		for (var id = 0; id < pieces.Length; id++) {
			// UNKNOWN / CONTROL / UNUSED 不参与分割
			var t = types[id];
			if (t == 2 || t == 3 || t == 6) continue; // UNK / CONTROL / UNUSED
			var p = pieces[id];
			if (string.IsNullOrEmpty(p)) continue;
			var node = root;
			foreach (var ch in p) {
				if (!node.Next.TryGetValue(ch, out var nx)) {
					nx = new TrieNode();
					node.Next[ch] = nx;
				}
				node = nx;
			}
			// 更长或同分保留首次；score 在 DP 里比
			if (node.PieceId < 0)
				node.PieceId = id;
			else {
				// 同字符串多 id 时保留分更高者
				// 这里 pieces 通常唯一
				node.PieceId = id;
			}
		}
		return root;
	}

	sealed class TrieNode {
		public readonly Dictionary<char, TrieNode> Next = new();
		public int PieceId = -1;
	}

	/// <summary>极简 protobuf 读取（仅 wire 解析）。</summary>
	struct PbReader {
		readonly byte[] data;
		int pos;

		public PbReader(byte[] data) {
			this.data = data ?? Array.Empty<byte>();
			pos = 0;
		}

		public bool HasMore => pos < data.Length;

		public (int field, int wt) ReadTag() {
			var t = ReadVarint();
			return ((int)(t >> 3), (int)(t & 7));
		}

		public ulong ReadVarint() {
			ulong r = 0;
			var s = 0;
			while (pos < data.Length) {
				var b = data[pos++];
				r |= (ulong)(b & 0x7F) << s;
				if ((b & 0x80) == 0) break;
				s += 7;
			}
			return r;
		}

		public byte[] ReadBytes() {
			var len = (int)ReadVarint();
			if (len < 0 || pos + len > data.Length) throw new InvalidDataException("pb bytes");
			var slice = new byte[len];
			Buffer.BlockCopy(data, pos, slice, 0, len);
			pos += len;
			return slice;
		}

		public string ReadString() {
			var b = ReadBytes();
			return Encoding.UTF8.GetString(b);
		}

		public float ReadFloat() {
			if (pos + 4 > data.Length) throw new InvalidDataException("pb float");
			var v = BitConverter.ToSingle(data, pos);
			pos += 4;
			return v;
		}

		public void Skip(int wt) {
			switch (wt) {
			case 0: ReadVarint(); break;
			case 1: pos += 8; break;
			case 2:
				var len = (int)ReadVarint();
				pos += len;
				break;
			case 5: pos += 4; break;
			default: throw new InvalidDataException("pb wire " + wt);
			}
		}
	}
}
