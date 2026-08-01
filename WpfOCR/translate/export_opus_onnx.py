# -*- coding: utf-8 -*-
"""
将本地 Opus-MT（pytorch_model.bin）导出为 ONNX（encoder + decoder）。

用法:
  python -X utf8 export_opus_onnx.py
  python -X utf8 export_opus_onnx.py --root "D:\\...\\translatemodels"
  python -X utf8 export_opus_onnx.py --only zh-en

输出:
  opus-mt-zh-en-onnx/  encoder_model.onnx + decoder_model.onnx + tokenizer
  opus-mt-en-zh-onnx/
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import tempfile

# 避免 transformers 误拉 TensorFlow / 旧 httplib2+pyparsing 冲突
os.environ.setdefault("TRANSFORMERS_NO_TF", "1")
os.environ.setdefault("TRANSFORMERS_NO_FLAX", "1")
os.environ.setdefault("USE_TF", "0")


def log(msg: str) -> None:
	print(msg, flush=True)


def ensure_std_names(src: str, work: str) -> None:
	os.makedirs(work, exist_ok=True)
	aliases = {
		"config.json": ["config.json", "config.txt"],
		"generation_config.json": ["generation_config.json", "generation_config.txt"],
		"tokenizer_config.json": ["tokenizer_config.json", "tokenizer_config.txt"],
		"vocab.json": ["vocab.json", "vocab.txt"],
		"pytorch_model.bin": ["pytorch_model.bin"],
		"source.spm": ["source.spm"],
		"target.spm": ["target.spm"],
	}
	for want, cands in aliases.items():
		dst = os.path.join(work, want)
		if os.path.isfile(dst):
			continue
		for c in cands:
			p = os.path.join(src, c)
			if os.path.isfile(p):
				shutil.copy2(p, dst)
				break
		if not os.path.isfile(dst) and want in (
			"pytorch_model.bin", "config.json", "source.spm", "target.spm", "vocab.json"
		):
			raise FileNotFoundError(f"缺少 {want} @ {src}")


def export_one(src_dir: str, out_dir: str, opset: int = 14) -> None:
	import torch
	import torch.nn as nn
	from transformers import MarianMTModel, MarianTokenizer

	src_dir = os.path.abspath(src_dir)
	out_dir = os.path.abspath(out_dir)
	log(f"=== 导出 {src_dir}")
	log(f"    → {out_dir}")

	with tempfile.TemporaryDirectory(prefix="opus_export_") as td:
		ensure_std_names(src_dir, td)
		log("加载 PyTorch MarianMT…")
		tok = MarianTokenizer.from_pretrained(td)
		model = MarianMTModel.from_pretrained(td)
		model.eval()
		# 避免 SDPA + torch.export 的 SymBool 问题
		try:
			model.config._attn_implementation = "eager"
		except Exception:
			pass
		try:
			model.set_attn_implementation("eager")
		except Exception:
			pass

		sample = "你好" if "zh-en" in src_dir.replace("\\", "/").lower() else "Hello"
		batch = tok([sample], return_tensors="pt", padding=True)
		input_ids = batch["input_ids"]
		attention_mask = batch["attention_mask"]

		class Enc(nn.Module):
			def __init__(self, enc):
				super().__init__()
				self.encoder = enc

			def forward(self, input_ids, attention_mask):
				out = self.encoder(
					input_ids=input_ids,
					attention_mask=attention_mask,
					return_dict=True,
				)
				return out.last_hidden_state

		class Dec(nn.Module):
			def __init__(self, m):
				super().__init__()
				self.model = m

			def forward(self, decoder_input_ids, encoder_hidden_states, encoder_attention_mask):
				out = self.model(
					input_ids=None,
					attention_mask=encoder_attention_mask,
					decoder_input_ids=decoder_input_ids,
					encoder_outputs=(encoder_hidden_states,),
					return_dict=True,
					use_cache=False,
				)
				return out.logits

		enc_w = Enc(model.get_encoder()).eval()
		with torch.no_grad():
			enc_hidden = enc_w(input_ids, attention_mask)

		dec_start = model.config.decoder_start_token_id
		if dec_start is None:
			dec_start = model.config.pad_token_id
		# 用长度>1 导出，避免 trace 把 sequence_length!=1 冻成 False（导致因果掩码错误、重复词）
		decoder_input_ids = torch.tensor(
			[[dec_start, int(getattr(model.config, "eos_token_id", 0) or 0)]],
			dtype=torch.long,
		)

		dec_w = Dec(model).eval()
		with torch.no_grad():
			_ = dec_w(decoder_input_ids, enc_hidden, attention_mask)

		os.makedirs(out_dir, exist_ok=True)
		enc_path = os.path.join(out_dir, "encoder_model.onnx")
		dec_path = os.path.join(out_dir, "decoder_model.onnx")

		# torch 2.4+ 默认 dynamo 导出易失败；强制 legacy
		def _export(mod, args, path, in_names, out_names, dyn):
			kwargs = dict(
				input_names=in_names,
				output_names=out_names,
				dynamic_axes=dyn,
				opset_version=opset,
				do_constant_folding=True,
			)
			try:
				torch.onnx.export(mod, args, path, dynamo=False, **kwargs)
			except TypeError:
				# 旧 torch 无 dynamo 参数
				torch.onnx.export(mod, args, path, **kwargs)

		log("导出 encoder_model.onnx…")
		_export(
			enc_w,
			(input_ids, attention_mask),
			enc_path,
			["input_ids", "attention_mask"],
			["last_hidden_state"],
			{
				"input_ids": {0: "batch", 1: "src_len"},
				"attention_mask": {0: "batch", 1: "src_len"},
				"last_hidden_state": {0: "batch", 1: "src_len"},
			},
		)

		log("导出 decoder_model.onnx…")
		_export(
			dec_w,
			(decoder_input_ids, enc_hidden, attention_mask),
			dec_path,
			["decoder_input_ids", "encoder_hidden_states", "encoder_attention_mask"],
			["logits"],
			{
				"decoder_input_ids": {0: "batch", 1: "tgt_len"},
				"encoder_hidden_states": {0: "batch", 1: "src_len"},
				"encoder_attention_mask": {0: "batch", 1: "src_len"},
				"logits": {0: "batch", 1: "tgt_len"},
			},
		)

		tok.save_pretrained(out_dir)
		for f in (
			"source.spm", "target.spm", "vocab.json", "config.json",
			"generation_config.json", "tokenizer_config.json",
		):
			sp = os.path.join(td, f)
			dp = os.path.join(out_dir, f)
			if os.path.isfile(sp) and not os.path.isfile(dp):
				shutil.copy2(sp, dp)

		gen = {
			"backend": "onnx",
			"decoder_start_token_id": int(dec_start) if dec_start is not None else 65000,
			"eos_token_id": int(getattr(model.config, "eos_token_id", 0) or 0),
			"pad_token_id": int(getattr(model.config, "pad_token_id", 65000) or 65000),
			"forced_eos_token_id": int(getattr(model.config, "forced_eos_token_id", 0) or 0),
			"vocab_size": int(getattr(model.config, "vocab_size", 0) or 0),
			"max_length": int(getattr(model.config, "max_length", 512) or 512),
			"encoder": "encoder_model.onnx",
			"decoder": "decoder_model.onnx",
		}
		with open(os.path.join(out_dir, "onnx_gen_config.json"), "w", encoding="utf-8") as f:
			json.dump(gen, f, indent=2, ensure_ascii=False)
		with open(os.path.join(out_dir, "backend.txt"), "w", encoding="utf-8") as f:
			f.write("onnx\n")

	for name in sorted(os.listdir(out_dir)):
		p = os.path.join(out_dir, name)
		if os.path.isfile(p):
			log(f"  {name}  {os.path.getsize(p):,}")

	log("冒烟（onnxruntime 贪心）…")
	from onnx_infer import OnnxMarian

	inf = OnnxMarian(out_dir, providers=["CPUExecutionProvider"])
	text = inf.translate(sample, max_new_tokens=32)
	log(f"  [{sample}] → {text}")
	log("OK")


def main() -> int:
	ap = argparse.ArgumentParser()
	ap.add_argument("--root", default="")
	ap.add_argument("--only", default="")
	ap.add_argument("--opset", type=int, default=14)
	args = ap.parse_args()

	here = os.path.dirname(os.path.abspath(__file__))
	if here not in sys.path:
		sys.path.insert(0, here)

	candidates = []
	if args.root:
		candidates.append(args.root)
	candidates += [
		os.path.join(here, "..", "bin", "Release", "net48", "translatemodels"),
		os.path.join(here, "..", "bin", "Release", "net10.0-windows", "translatemodels"),
		os.environ.get("TRANSLATE_MODELS") or "",
	]
	root = ""
	for c in candidates:
		if c and os.path.isdir(c):
			root = os.path.abspath(c)
			break
	if not root:
		log("未找到 translatemodels，请 --root")
		return 1

	log(f"root = {root}")
	pairs = [
		("opus-mt-zh-en", "opus-mt-zh-en-onnx", "zh-en"),
		("opus-mt-en-zh", "opus-mt-en-zh-onnx", "en-zh"),
	]
	only = (args.only or "").strip().lower().replace("_", "-")
	for src_name, out_name, key in pairs:
		if only and only not in (key, src_name, out_name):
			continue
		src = os.path.join(root, src_name)
		out = os.path.join(root, out_name)
		if not os.path.isdir(src):
			log(f"跳过: {src}")
			continue
		try:
			export_one(src, out, opset=args.opset)
		except Exception as e:
			log(f"失败 {src_name}: {e}")
			import traceback
			traceback.print_exc()
			return 1
	log("全部完成")
	return 0


if __name__ == "__main__":
	sys.exit(main())
