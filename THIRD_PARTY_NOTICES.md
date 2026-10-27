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
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | Model archive extraction | MIT |
| [7-Zip](https://www.7-zip.org/) `7za.dll` | 7z extraction only (about 400 KB). Called in-process via `CreateObject` | LGPL-2.1-or-later; see `License.7za.txt` |
| [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) | FFmpeg P/Invoke | LGPL-3.0-or-later (bindings) |
| [Vortice.DXGI / Direct3D11](https://github.com/amerkoleci/Vortice.Windows) | DXGI capture | MIT |
| [Viewer.js](https://github.com/fengyuanchen/viewerjs) 1.11.7 | Chat image zoom (`web/viewer.min.js`, `web/viewer.min.css`) | MIT |

Always verify the license of the **exact package version** you ship.

## Optional native / data (not in source tree by default)

| Asset | Role | Notes |
|-------|------|--------|
| **OCR models** (PP-OCR / RapidOCR ONNX packs) | Text detection & recognition | Usually Apache-2.0 (PaddleOCR lineage) or terms of the pack author — **not** covered by this repo’s MIT alone |
| **FFmpeg shared libraries** (`ffmpeg64/`) | Screen-record encode / remux | GPL or LGPL depending on the build (e.g. BtbN gpl-shared). Do not redistribute without matching FFmpeg license compliance |
| **CUDA / cuDNN redistributables** | NVIDIA GPU inference | NVIDIA EULA; often not redistributable freely — prefer system install or user-provided `onnxgpu64` |
| **DirectML / ORT native DLLs** | iGPU / CUDA EP | Follow Microsoft / ORT redistributable terms |
| **7za.dll** | 7z extract | In the release archive, with `License.7za.txt` (LGPL-2.1-or-later). **Help → Install features** can download it again if the file is missing |
| **ZXing.dll**, **SharpCompress.dll**, **e_sqlite3.dll** | Barcode, `tar.bz2` extract, dictionary | Not in the release archive. **Help → Install features** downloads them |
| [Bogus](https://github.com/bchavez/Bogus) `Bogus.dll` 35.6.5 | Test data on the tools page | MIT. Not in the release archive. **Help → Install features** downloads it |
| [NPinyin](https://www.nuget.org/packages/NPinyin) `NPinyin.dll` 0.2.6321.26573 | Chinese to pinyin and initials | AWangSoft Inc. © 2011. The nupkg ships no license file. Not in the release archive. **Help → Install features** downloads it |
| **Japanese name data** (`web/jname-s.txt`, `web/jname-g.txt`, downloaded as `jname.7z` from the `dict-db` release) | Surname and given-name readings for the web name-lookup tool | Derived from [JMnedict](https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project), © EDRDG, **CC BY-SA 4.0**; the population ranking for the top 5,000 surnames comes from [siikamiika/japanese-family-names](https://github.com/siikamiika/japanese-family-names). Keep the attribution if you redistribute the data |

## Reference / research code

Ideas or reference material (e.g. screen-capture UX patterns from open-source tools) may appear in local notes under `tmp/`; they are **not** part of the MIT-licensed application source unless explicitly copied into `ScreenKit/` with attribution. Do not ship third-party source from `tmp/` without checking its license.

## Recommendation when distributing

1. Ship this `LICENSE` and `THIRD_PARTY_NOTICES.md` with binaries.
2. Include license files for any bundled ONNX models and FFmpeg/CUDA stacks.
3. Prefer linking users to download large proprietary or GPL-heavy runtimes themselves when possible.
