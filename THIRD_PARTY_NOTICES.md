# Third-party notices

ScreenKit application source code is licensed under the **MIT License** (see [LICENSE](LICENSE)).

This file summarizes **third-party components** that may be used at build time or runtime. Their licenses are **independent** of the MIT license of this repository. You must comply with each component’s license when redistributing binaries or models.

## NuGet / runtime libraries (typical)

| Component | Role | Common license (check package) |
|-----------|------|--------------------------------|
| [Microsoft.ML.OnnxRuntime](https://github.com/microsoft/onnxruntime) (+ GPU / DirectML packages) | OCR inference | MIT |
| [Microsoft.AI.DirectML](https://www.nuget.org/packages/Microsoft.AI.DirectML) | DirectML runtime | Microsoft software license |
| [OpenCvSharp4](https://github.com/shimat/opencvsharp) | Image / video helpers | Apache-2.0 |
| [ZXingCpp](https://github.com/zxing-cpp/zxing-cpp) (zxing-cpp native) | Multi-format barcode / QR decode | Apache-2.0 |
| [PDFsharp](https://github.com/empira/PDFsharp) | PDF write | MIT |
| [PDFtoImage](https://github.com/sungaila/PDFtoImage) | PDF rasterize | MIT (plus Pdfium/Skia natives) |
| [NAudio](https://github.com/naudio/NAudio) | WASAPI capture | MIT |
| [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) | FFmpeg P/Invoke | LGPL-3.0-or-later (bindings) |
| [Vortice.DXGI / Direct3D11](https://github.com/amerkoleci/Vortice.Windows) | DXGI capture | MIT |

Always verify the license of the **exact package version** you ship.

## Optional native / data (not in source tree by default)

| Asset | Role | Notes |
|-------|------|--------|
| **OCR models** (PP-OCR / RapidOCR ONNX packs) | Text detection & recognition | Usually Apache-2.0 (PaddleOCR lineage) or terms of the pack author — **not** covered by this repo’s MIT alone |
| **FFmpeg shared libraries** (`ffmpeg64/`) | Screen-record encode / remux | GPL or LGPL depending on the build (e.g. BtbN gpl-shared). Do not redistribute without matching FFmpeg license compliance |
| **CUDA / cuDNN redistributables** | NVIDIA GPU inference | NVIDIA EULA; often not redistributable freely — prefer system install or user-provided `onnxgpu64` |
| **DirectML / ORT native DLLs** | iGPU / CUDA EP | Follow Microsoft / ORT redistributable terms |

## Reference / research code

Ideas or reference material (e.g. screen-capture UX patterns from open-source tools) may appear in local notes under `tmp/`; they are **not** part of the MIT-licensed application source unless explicitly copied into `ScreenKit/` with attribution. Do not ship third-party source from `tmp/` without checking its license.

## Recommendation when distributing

1. Ship this `LICENSE` and `THIRD_PARTY_NOTICES.md` with binaries.
2. Include license files for any bundled ONNX models and FFmpeg/CUDA stacks.
3. Prefer linking users to download large proprietary or GPL-heavy runtimes themselves when possible.
