- 总是用中文回复。
- 改完可运行的 Android 代码后执行 `node build.js run`（debug 编译+安装+启动）。
- 用户说 `install` 时用 `node build.js install`（release 增量编译+安装+启动）。
- 仅改文档 / 纯分析 / 用户明确说不要安装时，可跳过。
- 构建脚本对齐 reader：`build/release/rebuild/clean/run/install/apk/devices`，`--debug/--release/--force/-s`。
- `node build.js release` 只把 APK 写到 `android/release/`。不要打进电脑端发布包。

- PowerShell 命令与脚本参考（动手写 pwsh 命令或脚本前先对照）
	- 内联代码别套双引号：`python -c "…"`、`rg -o ".{0,90}…"` 里的引号会被 pwsh 吃掉，报 `SyntaxError: '(' was never closed` 或 `missing the terminator`。复杂命令一律写 `tmp/x.ps1` 再 `-File`；非要内联，外层用单引号、内部只用双引号。
	- 原生命令别用 `2>&1` 合并输出：pwsh 会把 stderr 包成 `ErrorRecord`（`+ CategoryInfo … RemoteException`），真错误被淹没。要合并用 `cmd /c "prog args 2>&1"`（实测得到纯字符串），或 `2>&1 | ForEach-Object { "$_" }`。
	- 读来源不明的文本：有 BOM 按 BOM；无 BOM 用严格 UTF-8 试解（`[Text.UTF8Encoding]::new($false,$true)`，非法字节会抛异常），抛了退回 `[Text.Encoding]::GetEncoding(936)`。别用 `-Encoding Default`（7 里是 UTF-8、5.1 里是 GBK）。写文件统一 `-Encoding utf8NoBOM`。
	- 跨目录一律用绝对路径：每次调用都是新进程，上一条的 `cd` 不保留；`Set-Location` 失败不报错且退出码仍为 0；它只改 PowerShell 的 cwd，不改 .NET 的 `[Environment]::CurrentDirectory`。要换目录用工具的 `workdir` 参数。
	- `chcp 65001` 不用写：pwsh 7 默认 UTF-8，那是 5.1 的历史遗留。

- **只要踩了坑、在某个问题上浪费了时间，就写 `mydocs/调试记录.md`**，避免下次再遇到、再浪费时间。
	- 触发条件：任何踩坑都记，不要求「难题」；同一个错试了两三次、走错方向又回头、被工具/环境坑住，都算。
	- **尽量简短，一条只写「问题」+「避免方法」**，不写排查流水账，不重复别处已有的内容。
	- 一次踩坑写一条，追加到文首，带日期。
	- **过时、错误的记录直接删掉**，不要留着「已修复」的历史。记录只保留现在仍然成立的坑。

- **默认不做实际测试：依据可靠文档/源码改完直接交差，不要每轮任务都花时间跑验证。**
	- 改配置、改文档、改注释、改样式这类低风险改动，读完相关文件直接改，改完即回复，不跑进程、不开浏览器、不反复确认。
	- 结论优先来自「读源码 + 查官方文档/README/CHANGELOG」，而不是「跑一遍看结果」。文档能确定的就别实测。
	- 只在下列情况才实测：用户明确要求验证；改动涉及数据写入/删除、编译构建、对外接口等不可逆或高风险逻辑；读文档仍无法确定行为且猜错代价高。
	- 确实要测时，一次跑通就把该轮所有断言打包进同一个脚本，不要一个点一个脚本轮着试；已经跑起来的实例直接复用，别反复 kill 再启动。
	- 连续两次失败先回头读代码/查文档找根因，不要接着调探测脚本。
	- 测完不要写「已验证」式的长篇流水账，直接说改了什么、依据是什么。
