- 总是用中文回复。
- 改完可运行的 Android 代码后执行 `node build.js run`（debug 编译+安装+启动）。
- 用户说 `install` 时用 `node build.js install`（release 增量编译+安装+启动）。
- 仅改文档 / 纯分析 / 用户明确说不要安装时，可跳过。
- 构建脚本对齐 reader：`build/release/rebuild/clean/run/install/apk/devices`，`--debug/--release/--force/-s`。
- `node build.js release` 只把 APK 写到 `android/release/`。不要打进电脑端发布包。

- PowerShell 命令与脚本参考（动手写 pwsh 命令或脚本前先对照）
	- 写较复杂的命令、拼接引号、处理中文编码、并行、或判断该用 pwsh 还是 powershell 时，先读 `D:/VS_Projects/AIPrototype/PROMPTS/pwsh-agent使用参考.md`（7 与 5.1 的差异、`ForEach-Object -Parallel`、失败即停、编码坑、性能口径）。
