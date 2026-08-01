# -*- coding: utf-8 -*-
"""
Opus-MT 翻译管道：stdin 一行 JSON → stdout 一行 JSON。
协议:
  {"cmd":"load","dir":"zh-en","path":"...","device":"auto|cuda|cpu|dml"}
  {"cmd":"tr","dir":"zh-en","text":"..."}
优先加载目录内 ONNX（encoder_model.onnx）；否则 PyTorch。

环境:
  ONNX: onnxruntime (+ 可选 onnxruntime-gpu)
  PT:   torch + transformers + sentencepiece
"""
from __future__ import annotations

import json
import os
import sys
import traceback

os.environ.setdefault("TRANSFORMERS_NO_TF", "1")
os.environ.setdefault("TRANSFORMERS_NO_FLAX", "1")
os.environ.setdefault("USE_TF", "0")

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
	sys.path.insert(0, _HERE)


def log(msg: str) -> None:
	try:
		sys.stderr.write(msg + "\n")
		sys.stderr.flush()
	except Exception:
		pass


def out(obj: dict) -> None:
	sys.stdout.write(json.dumps(obj, ensure_ascii=False) + "\n")
	sys.stdout.flush()


def is_onnx_dir(path: str) -> bool:
	if not path or not os.path.isdir(path):
		return False
	if os.path.isfile(os.path.join(path, "encoder_model.onnx")) and os.path.isfile(
		os.path.join(path, "decoder_model.onnx")
	):
		return True
	if os.path.isfile(os.path.join(path, "backend.txt")):
		try:
			with open(os.path.join(path, "backend.txt"), encoding="utf-8") as f:
				return "onnx" in f.read().lower()
		except Exception:
			pass
	return False


def resolve_ort_providers(prefer: str):
	"""返回 (label, providers列表)。"""
	import onnxruntime as ort

	avail = ort.get_available_providers()
	p = (prefer or "auto").strip().lower()

	def has(name: str) -> bool:
		return name in avail

	if p in ("cpu",):
		return "cpu", ["CPUExecutionProvider"]
	if p in ("gpu", "cuda"):
		if has("CUDAExecutionProvider"):
			return "cuda", ["CUDAExecutionProvider", "CPUExecutionProvider"]
		raise RuntimeError(
			"ONNX CUDA 不可用。请 pip install onnxruntime-gpu（与 CUDA 匹配）；"
			"onnxgpu64 是 C# ORT 用的，Python 管道需独立 GPU 包。"
		)
	if p in ("igpu", "dml", "directml"):
		if has("DmlExecutionProvider"):
			return "dml", ["DmlExecutionProvider", "CPUExecutionProvider"]
		raise RuntimeError("ONNX DirectML 不可用（需支持 DML 的 onnxruntime 构建）")

	# auto
	if has("CUDAExecutionProvider"):
		return "cuda", ["CUDAExecutionProvider", "CPUExecutionProvider"]
	if has("DmlExecutionProvider"):
		return "dml", ["DmlExecutionProvider", "CPUExecutionProvider"]
	return "cpu", ["CPUExecutionProvider"]


def resolve_torch_device(prefer: str):
	import torch

	p = (prefer or "auto").strip().lower()
	if p in ("cpu",):
		return "cpu", torch.device("cpu")
	if p in ("gpu", "cuda"):
		if not torch.cuda.is_available():
			raise RuntimeError("CUDA 不可用（当前 torch 多为 +cpu）。ONNX 模型请用 device=cuda + onnxruntime-gpu")
		return "cuda", torch.device("cuda")
	if p in ("igpu", "dml", "directml"):
		try:
			import torch_directml  # type: ignore
			return "dml", torch_directml.device()
		except Exception as e:
			raise RuntimeError(f"torch-directml 不可用: {e}") from e
	if torch.cuda.is_available():
		return "cuda", torch.device("cuda")
	try:
		import torch_directml  # type: ignore
		return "dml", torch_directml.device()
	except Exception:
		pass
	return "cpu", torch.device("cpu")


def ensure_pt_names(path: str) -> str:
	"""保证 transformers 能读到标准文件名；原地复制 .txt → .json。"""
	aliases = {
		"config.json": ["config.txt"],
		"generation_config.json": ["generation_config.txt"],
		"tokenizer_config.json": ["tokenizer_config.txt"],
		"vocab.json": ["vocab.txt"],
	}
	import shutil
	for want, alts in aliases.items():
		target = os.path.join(path, want)
		if os.path.isfile(target):
			continue
		for a in alts:
			src = os.path.join(path, a)
			if os.path.isfile(src):
				shutil.copy2(src, target)
				log(f"normalize {a} -> {want}")
				break
	return path


def load_onnx(path: str, prefer: str):
	from onnx_infer import OnnxMarian

	label, providers = resolve_ort_providers(prefer)
	inf = OnnxMarian(path, providers=providers)
	return {"backend": "onnx", "infer": inf, "label": inf.device_label or label}


def load_pt(path: str, prefer: str):
	from transformers import MarianMTModel, MarianTokenizer
	import torch

	path = ensure_pt_names(path)
	need = ["pytorch_model.bin", "source.spm", "target.spm", "config.json", "vocab.json"]
	miss = [n for n in need if not os.path.isfile(os.path.join(path, n))]
	if miss:
		raise FileNotFoundError(f"PyTorch 模型缺文件 {miss} @ {path}")

	label, torch_dev = resolve_torch_device(prefer)
	tok = MarianTokenizer.from_pretrained(path)
	model = MarianMTModel.from_pretrained(path)
	model.eval()
	try:
		model = model.to(torch_dev)
	except Exception as e:
		if label != "cpu":
			log(f"移到 {label} 失败，回落 CPU: {e}")
			label, torch_dev = "cpu", torch.device("cpu")
			model = model.to(torch_dev)
		else:
			raise
	return {"backend": "pt", "tok": tok, "model": model, "dev": torch_dev, "label": label}


def load_pair(path: str, prefer: str = "auto"):
	path = os.path.abspath(path)
	if not os.path.isdir(path):
		raise FileNotFoundError(f"模型目录不存在: {path}")

	# 若 path 是 pytorch 目录，旁路查找 -onnx 目录优先
	onnx_sibling = path
	if path.rstrip("\\/").endswith("-onnx") or is_onnx_dir(path):
		return load_onnx(path, prefer)

	sibling = path.rstrip("\\/") + "-onnx"
	if os.path.isdir(sibling) and is_onnx_dir(sibling):
		log(f"使用 ONNX 目录: {sibling}")
		return load_onnx(sibling, prefer)

	if is_onnx_dir(path):
		return load_onnx(path, prefer)

	return load_pt(path, prefer)


def translate_entry(entry: dict, text: str, max_new_tokens: int = 256, num_beams: int = 4) -> str:
	if entry["backend"] == "onnx":
		return entry["infer"].translate(
			text,
			max_new_tokens=max_new_tokens,
			num_beams=num_beams,
			length_penalty=1.0,
		)

	import torch
	tok, model, torch_dev = entry["tok"], entry["model"], entry["dev"]
	text = (text or "").strip()
	if not text:
		return ""
	lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
	outs = []
	batch_buf, batch_idx = [], []
	nb = max(1, int(num_beams))

	def flush():
		nonlocal batch_buf, batch_idx
		if not batch_buf:
			return
		enc = tok(batch_buf, return_tensors="pt", padding=True, truncation=True, max_length=512)
		enc = {k: v.to(torch_dev) for k, v in enc.items()}
		with torch.no_grad():
			gen = model.generate(
				**enc,
				max_new_tokens=max_new_tokens,
				num_beams=nb,
				early_stopping=True,
			)
		dec = tok.batch_decode(gen.cpu() if hasattr(gen, "cpu") else gen, skip_special_tokens=True)
		for i, t in zip(batch_idx, dec):
			outs.append((i, t))
		batch_buf, batch_idx = [], []

	for i, line in enumerate(lines):
		if not line.strip():
			outs.append((i, ""))
			continue
		batch_buf.append(line)
		batch_idx.append(i)
		if len(batch_buf) >= 8:
			flush()
	flush()
	outs.sort(key=lambda x: x[0])
	return "\n".join(t for _, t in outs)


def main() -> int:
	try:
		sys.stdout.reconfigure(encoding="utf-8")
		sys.stderr.reconfigure(encoding="utf-8")
		sys.stdin.reconfigure(encoding="utf-8")
	except Exception:
		pass

	# 就绪探测
	try:
		import onnxruntime  # noqa: F401
	except Exception as e:
		out({"ok": False, "err": f"onnxruntime 不可用: {e}"})
		return 1

	out({"ok": True, "cmd": "ready", "msg": "translate_pipe ready (onnx|pt)"})

	models = {}  # dir -> entry

	for line in sys.stdin:
		line = line.lstrip("\ufeff").strip()
		if not line:
			continue
		try:
			req = json.loads(line)
		except Exception as e:
			out({"ok": False, "err": f"JSON 解析失败: {e}"})
			continue

		cmd = (req.get("cmd") or "").lower()
		try:
			if cmd == "ping":
				out({"ok": True, "cmd": "ping"})
				continue
			if cmd == "quit":
				out({"ok": True, "cmd": "quit"})
				return 0
			if cmd == "load":
				d = (req.get("dir") or "").lower()
				path = req.get("path") or ""
				prefer = (req.get("device") or "auto").strip().lower()
				if d not in ("zh-en", "en-zh") and "-" not in d:
					# 允许任意 xx-yy
					pass
				entry = load_pair(path, prefer)
				models[d] = entry
				out({
					"ok": True,
					"cmd": "load",
					"dir": d,
					"device": entry.get("label", "cpu"),
					"backend": entry.get("backend", "?"),
					"prefer": prefer,
				})
				continue
			if cmd == "tr":
				d = (req.get("dir") or "").lower()
				text = req.get("text") or ""
				if d not in models:
					raise RuntimeError(f"未加载模型 {d}，请先 load")
				entry = models[d]
				max_tok = int(req.get("max_new_tokens") or 256)
				num_beams = int(req.get("num_beams") or 4)
				result = translate_entry(entry, text, max_new_tokens=max_tok, num_beams=num_beams)
				out({
					"ok": True,
					"cmd": "tr",
					"dir": d,
					"text": result,
					"device": entry.get("label", "cpu"),
					"backend": entry.get("backend", "?"),
					"num_beams": num_beams,
				})
				continue
			out({"ok": False, "err": f"未知 cmd: {cmd}"})
		except Exception as e:
			log(traceback.format_exc())
			out({"ok": False, "err": str(e)})

	return 0


if __name__ == "__main__":
	sys.exit(main())
