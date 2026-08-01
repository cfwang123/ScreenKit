using System.Text.Json;

namespace WpfOCR;

/// <summary>
/// Opus-MT / Marian 分词：source.spm 编码 + vocab.json id 映射 + target.spm 解码。
/// </summary>
sealed class MarianTokenizer {
	readonly SentencePieceUnigram sourceSpm;
	readonly SentencePieceUnigram targetSpm;
	readonly Dictionary<string, int> pieceToId;
	readonly string[] idToPiece;
	readonly int unkId;
	readonly int eosId;
	readonly int padId;
	readonly int bosId;

	public int UnkId => unkId;
	public int EosId => eosId;
	public int PadId => padId;
	public int BosId => bosId;
	public int VocabSize => idToPiece.Length;

	MarianTokenizer(
		SentencePieceUnigram sourceSpm,
		SentencePieceUnigram targetSpm,
		Dictionary<string, int> pieceToId,
		string[] idToPiece,
		int unkId, int eosId, int padId, int bosId) {
		this.sourceSpm = sourceSpm;
		this.targetSpm = targetSpm;
		this.pieceToId = pieceToId;
		this.idToPiece = idToPiece;
		this.unkId = unkId;
		this.eosId = eosId;
		this.padId = padId;
		this.bosId = bosId;
	}

	public static MarianTokenizer Load(string modelDir) {
		var srcSpm = Path.Combine(modelDir, "source.spm");
		var tgtSpm = Path.Combine(modelDir, "target.spm");
		var vocabPath = Path.Combine(modelDir, "vocab.json");
		if (!File.Exists(vocabPath))
			vocabPath = Path.Combine(modelDir, "vocab.txt");
		if (!File.Exists(srcSpm) || !File.Exists(tgtSpm) || !File.Exists(vocabPath))
			throw new FileNotFoundException($"缺少 source.spm / target.spm / vocab.json @ {modelDir}");

		var source = SentencePieceUnigram.Load(srcSpm);
		var target = SentencePieceUnigram.Load(tgtSpm);

		Dictionary<string, int> map;
		using (var fs = File.OpenRead(vocabPath))
		using (var doc = JsonDocument.Parse(fs)) {
			map = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var p in doc.RootElement.EnumerateObject())
				map[p.Name] = p.Value.GetInt32();
		}

		var maxId = 0;
		foreach (var kv in map)
			if (kv.Value > maxId) maxId = kv.Value;
		var idToPiece = new string[maxId + 1];
		foreach (var kv in map)
			idToPiece[kv.Value] = kv.Key;

		int get(string name, int def) => map.TryGetValue(name, out var id) ? id : def;
		var unk = get("<unk>", 1);
		var eos = get("</s>", 0);
		var pad = get("<pad>", maxId);
		var bos = get("<s>", eos);

		return new MarianTokenizer(source, target, map, idToPiece, unk, eos, pad, bos);
	}

	/// <summary>编码为 input_ids（含末尾 eos），截断 maxLen。</summary>
	public long[] Encode(string text, int maxLen = 512) {
		var pieces = sourceSpm.EncodeAsPieces(text ?? "");
		var ids = new List<long>(pieces.Count + 1);
		foreach (var p in pieces) {
			if (!pieceToId.TryGetValue(p, out var id))
				id = unkId;
			ids.Add(id);
			if (ids.Count >= maxLen - 1) break;
		}
		ids.Add(eosId);
		return ids.ToArray();
	}

	/// <summary>ids → 文本（跳过 special）。</summary>
	public string Decode(IList<int> ids, bool skipSpecial = true) {
		if (ids == null || ids.Count == 0) return "";
		var pieces = new List<string>(ids.Count);
		foreach (var id in ids) {
			if (id < 0 || id >= idToPiece.Length) continue;
			var p = idToPiece[id];
			if (p == null) continue;
			if (skipSpecial && (id == eosId || id == padId || id == bosId || p is "<unk>" or "<s>" or "</s>" or "<pad>"))
				continue;
			pieces.Add(p);
		}
		return targetSpm.DecodePieces(pieces);
	}

	public string Decode(IList<long> ids, bool skipSpecial = true) {
		if (ids == null || ids.Count == 0) return "";
		var list = new List<int>(ids.Count);
		foreach (var x in ids) list.Add((int)x);
		return Decode(list, skipSpecial);
	}
}
