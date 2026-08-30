# -*- coding: utf-8 -*-
"""
Opus-MT ONNX 推理：encoder + decoder。

解码：
  - num_beams=1 → 贪心
  - num_beams>1 → 经典 beam search（整段 decoder 输入，无 past_key_values）

原理简述：
  1) encoder 只跑一次，得到 encoder_hidden_states
  2) 维护 B 条「部分译文」假设，每条有累计 log 概率
  3) 每一步对未结束的假设调 decoder，取最后位置 logits → log_softmax
  4) 每条扩展 top-k 个下一 token，全体候选再截断回 B 条
  5) 碰到 eos 的假设放入完成列表；最后按 length_penalty 选最优
"""
from __future__ import annotations

import json
import math
import os
from typing import List, Optional, Tuple

import numpy as np

os.environ.setdefault("TRANSFORMERS_NO_TF", "1")
os.environ.setdefault("TRANSFORMERS_NO_FLAX", "1")
os.environ.setdefault("USE_TF", "0")


def _log_softmax(x: np.ndarray) -> np.ndarray:
	x = x.astype(np.float64)
	x = x - np.max(x)
	e = np.exp(x)
	return np.log(e / np.sum(e) + 1e-12)


class OnnxMarian:
	def __init__(self, model_dir: str, providers: Optional[List[str]] = None):
		import onnxruntime as ort
		from transformers import MarianTokenizer

		self.dir = os.path.abspath(model_dir)
		cfg_path = os.path.join(self.dir, "onnx_gen_config.json")
		if os.path.isfile(cfg_path):
			with open(cfg_path, encoding="utf-8") as f:
				self.gen = json.load(f)
		else:
			self.gen = {
				"decoder_start_token_id": 65000,
				"eos_token_id": 0,
				"pad_token_id": 65000,
				"forced_eos_token_id": 0,
				"max_length": 512,
				"encoder": "encoder_model.onnx",
				"decoder": "decoder_model.onnx",
			}

		enc = os.path.join(self.dir, self.gen.get("encoder") or "encoder_model.onnx")
		dec = os.path.join(self.dir, self.gen.get("decoder") or "decoder_model.onnx")
		if not os.path.isfile(enc) or not os.path.isfile(dec):
			raise FileNotFoundError(f"缺少 encoder/decoder onnx @ {self.dir}")

		if providers is None:
			providers = ["CPUExecutionProvider"]
		so = ort.SessionOptions()
		so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
		self.enc = ort.InferenceSession(enc, so, providers=providers)
		self.dec = ort.InferenceSession(dec, so, providers=providers)
		self.tok = MarianTokenizer.from_pretrained(self.dir)
		self.providers = list(self.enc.get_providers())
		self.device_label = "cuda" if any("CUDA" in p for p in self.providers) else (
			"dml" if any("Dml" in p or "DML" in p for p in self.providers) else "cpu"
		)

		self.start = int(self.gen.get("decoder_start_token_id", 65000))
		self.eos = int(self.gen.get("eos_token_id", 0))
		self.forced_eos = int(self.gen.get("forced_eos_token_id", self.eos))
		self.pad = int(self.gen.get("pad_token_id", 65000))
		self.cfg_max = int(self.gen.get("max_length", 512) or 512)

	def translate(
		self,
		text: str,
		max_new_tokens: int = 128,
		num_beams: int = 4,
		length_penalty: float = 1.0,
	) -> str:
		text = (text or "").strip()
		if not text:
			return ""
		lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
		outs = []
		for line in lines:
			if not line.strip():
				outs.append("")
				continue
			outs.append(
				self._one(
					line,
					max_new_tokens=max_new_tokens,
					num_beams=num_beams,
					length_penalty=length_penalty,
				)
			)
		return "\n".join(outs)

	def _one(
		self,
		text: str,
		max_new_tokens: int = 128,
		num_beams: int = 4,
		length_penalty: float = 1.0,
	) -> str:
		enc_in = self.tok([text], return_tensors="np", padding=True, truncation=True, max_length=512)
		input_ids = enc_in["input_ids"].astype(np.int64)
		attention_mask = enc_in["attention_mask"].astype(np.int64)

		hidden = self.enc.run(
			None,
			{"input_ids": input_ids, "attention_mask": attention_mask},
		)[0]

		max_len = min(self.cfg_max, max(1, max_new_tokens) + 1)
		num_beams = max(1, int(num_beams))

		if num_beams == 1:
			ids = self._greedy(hidden, attention_mask, max_len)
		else:
			ids = self._beam_search(
				hidden,
				attention_mask,
				max_len=max_len,
				num_beams=num_beams,
				length_penalty=length_penalty,
			)
		return self._decode_ids(ids)

	def _decode_ids(self, ids: List[int]) -> str:
		eos, forced_eos, pad, start = self.eos, self.forced_eos, self.pad, self.start
		if ids and ids[0] == start:
			ids = ids[1:]
		while ids and ids[-1] in (eos, forced_eos, pad, start):
			ids.pop()
		return self.tok.decode(ids, skip_special_tokens=True).strip()

	def _decoder_step(self, dec_ids: np.ndarray, hidden: np.ndarray, attention_mask: np.ndarray) -> np.ndarray:
		"""返回最后时间步 logits [vocab]。"""
		logits = self.dec.run(
			None,
			{
				"decoder_input_ids": dec_ids.astype(np.int64),
				"encoder_hidden_states": hidden,
				"encoder_attention_mask": attention_mask,
			},
		)[0]
		return logits[0, -1, :].astype(np.float64)

	def _greedy(self, hidden: np.ndarray, attention_mask: np.ndarray, max_len: int) -> List[int]:
		dec_ids = np.array([[self.start]], dtype=np.int64)
		for step in range(max_len - 1):
			row = self._decoder_step(dec_ids, hidden, attention_mask)
			if self.pad >= 0 and self.pad < row.shape[0] and step > 0:
				row[self.pad] = -1e9
			next_id = int(np.argmax(row))
			dec_ids = np.concatenate([dec_ids, np.array([[next_id]], dtype=np.int64)], axis=1)
			if next_id in (self.eos, self.forced_eos, self.pad):
				break
			if step >= 3:
				tail = dec_ids[0, -4:].tolist()
				if len(set(tail)) == 1:
					break
		return dec_ids[0].tolist()

	def _beam_search(
		self,
		hidden: np.ndarray,
		attention_mask: np.ndarray,
		max_len: int,
		num_beams: int,
		length_penalty: float,
	) -> List[int]:
		"""
		标准 beam search（无 KV cache）。
		每条假设: (cum_log_prob, token_ids, finished)
		score 排序用: cum_log_prob / (len ** length_penalty)
		"""
		# 复制 encoder 输出给 batch 时按需 tile；此处逐步单条/小批
		# beams: open list
		open_beams: List[Tuple[float, List[int]]] = [(0.0, [self.start])]
		finished: List[Tuple[float, List[int]]] = []

		for step in range(max_len - 1):
			if not open_beams:
				break

			# 同长度假设可拼 batch，加速 ORT
			by_len: dict = {}
			for score, toks in open_beams:
				by_len.setdefault(len(toks), []).append((score, toks))

			all_cand: List[Tuple[float, List[int], bool]] = []  # score, toks, is_eos

			for tlen, group in by_len.items():
				# batch decoder
				batch = np.stack([np.array(t, dtype=np.int64) for _, t in group], axis=0)
				b = batch.shape[0]
				# encoder 侧按 batch 复制
				h = np.repeat(hidden, b, axis=0)
				am = np.repeat(attention_mask, b, axis=0)
				logits = self.dec.run(
					None,
					{
						"decoder_input_ids": batch,
						"encoder_hidden_states": h,
						"encoder_attention_mask": am,
					},
				)[0]  # [B, T, V]
				last = logits[:, -1, :].astype(np.float64)  # [B, V]

				for i, (score, toks) in enumerate(group):
					row = last[i].copy()
					# 屏蔽 pad
					if 0 <= self.pad < row.shape[0] and step > 0:
						row[self.pad] = -1e30
					logp = _log_softmax(row)
					# 取 top num_beams
					if num_beams < logp.shape[0]:
						# 部分排序
						idx = np.argpartition(-logp, num_beams - 1)[:num_beams]
						idx = idx[np.argsort(-logp[idx])]
					else:
						idx = np.argsort(-logp)[:num_beams]

					for tid in idx:
						tid = int(tid)
						ns = score + float(logp[tid])
						ntoks = toks + [tid]
						ended = tid in (self.eos, self.forced_eos) or tid == self.pad
						all_cand.append((ns, ntoks, ended))

			# 分成完成 / 未完成，未完成再截断到 num_beams
			new_open: List[Tuple[float, List[int]]] = []
			for ns, ntoks, ended in all_cand:
				if ended:
					# 长度惩罚：HF 风格 len_penalty
					lp = self._length_score(ns, len(ntoks), length_penalty)
					finished.append((lp, ntoks))
				else:
					new_open.append((ns, ntoks))

			# 未完成按「当前惩罚分」排序截断
			def open_key(item: Tuple[float, List[int]]) -> float:
				return self._length_score(item[0], len(item[1]), length_penalty)

			new_open.sort(key=open_key, reverse=True)
			open_beams = new_open[:num_beams]

			# 早停：已完成条数足够，且最差完成分已压过最好未完成
			if len(finished) >= num_beams and open_beams:
				finished.sort(key=lambda x: x[0], reverse=True)
				best_open = open_key(open_beams[0])
				if finished[num_beams - 1][0] >= best_open:
					break

		# 未结束的也当作候选
		for score, toks in open_beams:
			finished.append((self._length_score(score, len(toks), length_penalty), toks))

		if not finished:
			return [self.start]
		finished.sort(key=lambda x: x[0], reverse=True)
		return finished[0][1]

	@staticmethod
	def _length_score(cum_log_prob: float, length: int, length_penalty: float) -> float:
		# 与 HF 类似：score / ((5+len)/6)**lp ，length 含 start
		length = max(1, length)
		if length_penalty == 0:
			return cum_log_prob
		penalty = ((5.0 + length) / 6.0) ** length_penalty
		return cum_log_prob / penalty
