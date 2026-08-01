1. 总是用中文回复。
2. 乱码处理
- 用`python -X utf8`运行python脚本
- 用`chcp 65001; ...`运行powershell
- 读取文件发现乱码时，不要修改这个文件
3. 只查看、修改当前工作目录的文件。工作目录外的文件需要我的同意才能修改、查看。
4. 工作目录下`tmp/`下放临时生成的脚本等。一次性的文件用完就删除。
5. PowerShell 转义问题
	- **不要**在 PowerShell 里直接塞复杂 Python/正则/引号（如 `text=\"...\"`、嵌套引号、`r'...'` 与双引号混用），极易触发 `ParserError` / 缺终止符。
	- **做法**：把逻辑写成 `tmp/` 下的临时脚本（`.py` / `.ps1`），再用命令调用
	- 参数用命令行 `sys.argv` 传入，避免在一行里拼大段代码。
	- 用完删除 `tmp/` 里一次性脚本与中间产物。
6. 写.gitignore, README.md时，隐藏本机路径、密钥、密码、Api Key
7. 代码风格指导（按需读取，动手改代码前先对照）
	- 编写/修改 **C#**（`.cs`、csproj/sln 相关）时，先读并遵循 `D:/VS_Projects/AIPrototype/PROMPTS/csharp风格指导.md`（命名、组织、TickCount、WebSocket 服务器模式、编译方式等）。
	- 编写/修改 **JavaScript**（`.js` 等前端/模块脚本）时，先读并遵循 `D:/VS_Projects/AIPrototype/PROMPTS/JS代码风格指导.md`（IIFE 模块、私有函数、子模块、构造函数与方法语法等）。
	- 仅阅读、分析、解释已有代码而不改动时，可不强制加载；一旦要新增或改写实现，必须先加载对应风格指导再动手。
8. 改完代码后编译运行
	- C# 相关改动全部完成后，用 Release 编译并启动，不要只改不编：
	  `dotnet build WpfOCR/WpfOCR.csproj -c Release`，再运行 `WpfOCR/bin/Release/net48/WpfOCR.exe`（或精简包 `bin/Release/WpfOCR/WpfOCR.exe`）。
	- 相关改动攒齐再编一次，避免每改一行就编译；但一轮任务结束前必须 Release 编译验证。
9. 同一个问题反复修改还不行时，用命令行/adb+日志自己调试
	- **WPF**：Release 启动后用 UI Automation（`tmp/uia_probe.ps1` dump/tree/click）点控件、看控件树；可测场景加 CLI 参数（如 `--test-overlay-during-record`）+ 看 `log/`、`cli_last.log`；控件加 `AutomationId`；禁止只改代码不复现。
	- .NET Winform程序/库：增加命令行启动参数，测试关键代码路径，通过命令行跑通场景。
	- 安卓：adb + logcat，模拟运行。
	- 模拟问题场景，观察日志，定位根因后再改。
