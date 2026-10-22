(function(){
	var CATS = {
		syntax: "语法帮助",
		file: "文件与目录",
		text: "文本处理",
		perm: "权限与用户",
		pkg: "软件包",
		proc: "进程与系统",
		net: "网络",
		disk: "磁盘与存储",
		svc: "服务与日志",
		archive: "压缩与归档",
		dev: "开发与版本控制",
		sec: "安全与防火墙",
		other: "终端与帮助"
	};

	var COLS = [
		{ name: "命令", mono: true },
		{ name: "说明" },
		{ name: "常用示例", mono: true }
	];

	var WIDTHS = ["250px", "240px", ""];

	var GROUPS = [
		{ id: "syntax", name: "语法帮助" },
		{ id: "file", name: "文件与目录" },
		{ id: "text", name: "文本处理" },
		{ id: "perm", name: "权限与用户" },
		{ id: "pkg", name: "软件包" },
		{ id: "proc", name: "进程与系统" },
		{ id: "net", name: "网络" },
		{ id: "disk", name: "磁盘与存储" },
		{ id: "svc", name: "服务与日志" },
		{ id: "archive", name: "压缩与归档" },
		{ id: "dev", name: "开发与版本控制" },
		{ id: "sec", name: "安全与防火墙" },
		{ id: "other", name: "终端与帮助" }
	];

	// [分组, 命令, 说明, 常用示例, 备注]
	// 详情里的多行示例按「ps|命令」「cmd|命令」查表；查不到就按说明、示例和帮助拼一段。
	function R(a, tab){
		var g = a[0];
		var cmd = a[1];
		var note = a[4] || "—";
		var kind = tab === "cmd" ? "cmd" : "ps";
		if (g === "syntax" && SYNTAX_SHELL[cmd]) kind = SYNTAX_SHELL[cmd];
		var body = (SAMP[kind + "|" + cmd] || demoSample(cmd, a[2], a[3], kind)).join("\n");
		return {
			g: g,
			c: [cmd, a[2], a[3]],
			x: (CATS[g] || "") + " " + a[2] + " " + a[3] + " " + note + " " + body.replace(/\n/g, " "),
			d: [["类别", CATS[g] || ""], ["备注", note]],
			s: body
		};
	}

	// 没写专门示例时的兜底：说明、常用示例、看完整参数，保证每条都有注释和多行片段。
	function demoSample(cmd, desc, ex, kind){
		var base = cmd.split(" ")[0];
		if (kind === "cmd")
			return ["rem " + desc, ex, "", "rem 看完整参数", base + " /?"];
		if (/^[A-Za-z]+-[A-Za-z]/.test(base))
			return ["# " + desc, ex, "", "# 看完整参数和示例", "Get-Help " + base + " -Examples"];
		return ["# " + desc, ex, "", "# 看完整参数",
			base + (WINEXE[base.toLowerCase()] ? " /?" : " --help")];
	}

	// 这些是 Windows 自带 exe，帮助参数习惯用 /?。
	var WINEXE = {
		whoami: 1, icacls: 1, takeown: 1, ipconfig: 1, ping: 1, netstat: 1, "curl.exe": 1,
		mstsc: 1, sfc: 1, dism: 1, shutdown: 1, chkdsk: 1, diskpart: 1, eventvwr: 1,
		gpupdate: 1, certutil: 1, tasklist: 1, taskkill: 1, reg: 1, sc: 1, netsh: 1,
		schtasks: 1, format: 1, defrag: 1, expand: 1, cipher: 1, auditpol: 1, route: 1, arp: 1
	};

	var PS = [
		["file", "Get-ChildItem", "列出目录内容（ls / dir / gci）", "Get-ChildItem -Force"],
		["file", "Set-Location", "切换目录（cd / sl）", "Set-Location D:\\work"],
		["file", "New-Item", "新建文件或目录", "New-Item -ItemType Directory logs"],
		["file", "Remove-Item", "删除文件或目录（rm / del）", "Remove-Item build -Recurse -Force", "删除不进回收站，先确认路径。"],
		["file", "Copy-Item", "复制文件或目录", "Copy-Item src dst -Recurse"],
		["file", "Move-Item", "移动或重命名", "Move-Item old.txt new.txt"],
		["file", "Rename-Item", "重命名文件", "Rename-Item a.txt b.txt"],
		["file", "Get-Content", "读取文件（cat / type）；-Tail 看末尾", "Get-Content app.log -Tail 20 -Wait"],
		["file", "Set-Content", "写入文件，覆盖原内容", "Set-Content out.txt 'ok' -Encoding utf8"],
		["file", "Add-Content", "追加内容到文件", "Add-Content app.log 'line'"],
		["file", "Test-Path", "判断路径是否存在", "Test-Path C:\\Windows"],
		["file", "Resolve-Path", "展开成绝对路径", "Resolve-Path .\\app"],
		["file", "Get-Item", "查看文件或目录属性", "Get-Item app.exe | Select-Object Length,LastWriteTime"],
		["file", "New-Item -ItemType SymbolicLink", "创建符号链接（需管理员或开发者模式）", "New-Item -ItemType SymbolicLink -Path link -Target C:\\target"],
		["file", "Get-FileHash", "计算文件哈希", "Get-FileHash app.iso -Algorithm SHA256"],
		["file", "Compare-Object", "比较两个集合的差异", "Compare-Object (Get-Content a) (Get-Content b)"],
		["file", "Get-Acl", "查看文件权限", "Get-Acl C:\\app | Format-List"],
		["file", "Get-PSDrive", "查看驱动器和剩余空间", "Get-PSDrive -PSProvider FileSystem"],

		["text", "Select-String", "按正则搜索文本（grep）", "Select-String -Path *.log -Pattern 'error'"],
		["text", "Where-Object", "按条件过滤管道对象", "Get-Process | Where-Object CPU -gt 100"],
		["text", "ForEach-Object", "对每个对象执行（%）", "1..3 | ForEach-Object { $_ * 2 }"],
		["text", "Select-Object", "取指定列或前几行", "Get-Process | Select-Object -First 5 Name,CPU"],
		["text", "Sort-Object", "排序", "Get-Process | Sort-Object CPU -Descending"],
		["text", "Measure-Object", "统计行数、和、平均值", "Get-Content app.log | Measure-Object -Line"],
		["text", "Group-Object", "按字段分组计数", "Get-Process | Group-Object ProcessName | Sort-Object Count -Descending"],
		["text", "Out-File", "输出到文件，可指定编码", "Get-Process | Out-File procs.txt -Encoding utf8"],
		["text", "Tee-Object", "同时写屏幕和文件", "Get-ChildItem | Tee-Object -FilePath list.txt"],
		["text", "Import-Csv", "读 CSV 成对象", "Import-Csv users.csv | Select-Object Name"],
		["text", "Export-Csv", "导出 CSV", "Get-Process | Export-Csv procs.csv -NoTypeInformation -Encoding utf8"],
		["text", "ConvertTo-Json", "转成 JSON", "Get-Process | Select-Object -First 2 | ConvertTo-Json -Depth 3"],
		["text", "ConvertFrom-Json", "解析 JSON", "Get-Content app.json -Raw | ConvertFrom-Json"],
		["text", "Set-Clipboard", "把内容放进剪贴板", "Get-Content app.log -Tail 50 | Set-Clipboard"],
		["text", "Get-Clipboard", "读取剪贴板", "Get-Clipboard"],
		["text", "Out-Host -Paging", "分页显示长输出", "Get-ChildItem C:\\Windows -Recurse | Out-Host -Paging"],

		["perm", "whoami /priv", "查看当前用户的权限列表", "whoami /priv"],
		["perm", "Get-LocalUser", "列出本地用户", "Get-LocalUser", "仅 Windows PowerShell 5.1；pwsh 里改用 Get-CimInstance Win32_UserAccount。"],
		["perm", "New-LocalUser", "新建本地用户", "New-LocalUser -Name alice -Password (Read-Host -AsSecureString)", "仅 Windows PowerShell 5.1。"],
		["perm", "Add-LocalGroupMember", "把用户加入组", "Add-LocalGroupMember -Group Administrators -Member alice", "仅 Windows PowerShell 5.1。"],
		["perm", "Get-LocalGroupMember", "查看组成员", "Get-LocalGroupMember Administrators", "仅 Windows PowerShell 5.1。"],
		["perm", "icacls", "查看/修改文件权限", "icacls app /grant alice:(OI)(CI)M"],
		["perm", "takeown", "取得文件所有权", "takeown /f C:\\locked /r /d y"],
		["perm", "Start-Process -Verb RunAs", "以管理员身份启动程序", "Start-Process powershell -Verb RunAs"],
		["perm", "Get-ExecutionPolicy", "查看脚本执行策略", "Get-ExecutionPolicy -List"],
		["perm", "Set-ExecutionPolicy", "修改执行策略（当前用户）", "Set-ExecutionPolicy RemoteSigned -Scope CurrentUser"],

		["pkg", "winget install", "安装软件", "winget install Git.Git"],
		["pkg", "winget upgrade --all", "升级全部可升级软件", "winget upgrade --all --include-unknown"],
		["pkg", "winget search", "搜索软件", "winget search vscode"],
		["pkg", "winget list", "列出已安装软件", "winget list"],
		["pkg", "Get-AppxPackage", "查看已安装的商店应用", "Get-AppxPackage *store*"],
		["pkg", "Add-AppxPackage", "安装 .appx / .msix", "Add-AppxPackage .\\app.msix"],
		["pkg", "Install-Module", "安装 PowerShell 模块", "Install-Module PSReadLine -Scope CurrentUser"],
		["pkg", "Get-Module -ListAvailable", "查看已安装模块", "Get-Module -ListAvailable | Select-Object Name,Version"],
		["pkg", "choco install", "Chocolatey 安装（需先装 choco）", "choco install git -y"],
		["pkg", "scoop install", "Scoop 安装（用户级）", "scoop install ripgrep"],

		["proc", "Get-Process", "查看进程", "Get-Process chrome | Select-Object Id,CPU,WS"],
		["proc", "Stop-Process", "按 PID 结束进程", "Stop-Process -Id 1234 -Force"],
		["proc", "Start-Process", "启动程序；-Wait 等结束", "Start-Process notepad -Wait"],
		["proc", "Get-CimInstance", "查 WMI 类，获取命令行等", "Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | Select-Object ProcessId,CommandLine"],
		["proc", "Get-WmiObject", "查 WMI（5.1）；pwsh 已移除", "Get-WmiObject Win32_LogicalDisk", "已弃用，新代码用 Get-CimInstance。"],
		["proc", "Get-ComputerInfo", "查看系统和硬件概况", "Get-ComputerInfo | Select-Object OsName,OsVersion,CsTotalPhysicalMemory"],
		["proc", "Get-Counter", "读性能计数器", "Get-Counter '\\Processor(_Total)\\% Processor Time' -MaxSamples 3"],
		["proc", "$env:PATH", "查看环境变量（当前会话）", "$env:PATH"],
		["proc", "[Environment]::SetEnvironmentVariable", "永久写入用户环境变量", "[Environment]::SetEnvironmentVariable('PATH', $env:PATH, 'User')"],
		["proc", "Get-Date", "查看/格式化时间", "Get-Date -Format 'yyyy-MM-dd HH:mm:ss'"],
		["proc", "Start-Sleep", "等待若干秒", "Start-Sleep -Seconds 5"],
		["proc", "Measure-Command", "测量命令耗时", "Measure-Command { Get-ChildItem C:\\Windows -Recurse }"],
		["proc", "Get-History", "查看本次会话的命令历史", "Get-History -Count 20"],
		["proc", "Start-Transcript", "把会话输出记录到文件", "Start-Transcript .\\session.txt"],
		["proc", "Get-Random", "生成随机数", "Get-Random -Minimum 1 -Maximum 100"],

		["net", "Get-NetIPAddress", "查看 IP 地址", "Get-NetIPAddress -AddressFamily IPv4"],
		["net", "Get-NetAdapter", "查看网卡和链路速度", "Get-NetAdapter | Select-Object Name,Status,LinkSpeed"],
		["net", "Test-NetConnection", "测主机或端口连通性", "Test-NetConnection 192.168.1.10 -Port 445"],
		["net", "Resolve-DnsName", "DNS 查询", "Resolve-DnsName example.com"],
		["net", "Get-DnsClientCache", "查看 DNS 缓存", "Get-DnsClientCache | Select-Object -First 10"],
		["net", "Clear-DnsClientCache", "清空 DNS 缓存", "Clear-DnsClientCache"],
		["net", "Get-NetTCPConnection", "查看端口连接和占用进程", "Get-NetTCPConnection -LocalPort 8080 -State Listen"],
		["net", "Invoke-WebRequest", "发 HTTP 请求", "Invoke-WebRequest https://example.com -Method Head"],
		["net", "Invoke-RestMethod", "调 REST 接口，自动转对象", "Invoke-RestMethod https://api.example.com/items"],
		["net", "ipconfig /all", "查看全部网络配置", "ipconfig /all"],
		["net", "ping", "测试连通性", "ping -n 4 1.1.1.1"],
		["net", "netstat -ano", "查看端口和 PID", "netstat -ano | Select-String ':8080'"],
		["net", "Get-SmbShare", "查看共享目录", "Get-SmbShare"],
		["net", "New-SmbShare", "创建共享目录", "New-SmbShare -Name share -Path D:\\share -FullAccess alice"],
		["net", "curl.exe", "调用 curl；PowerShell 里 curl 是别名", "curl.exe -I https://example.com"],
		["net", "mstsc", "打开远程桌面", "mstsc /v:192.168.1.10"],

		["disk", "Get-Volume", "查看卷和剩余空间", "Get-Volume | Select-Object DriveLetter,FileSystemLabel,SizeRemaining"],
		["disk", "Get-Disk", "查看磁盘", "Get-Disk"],
		["disk", "Get-Partition", "查看分区", "Get-Partition"],
		["disk", "Get-PhysicalDisk", "查看物理盘和介质类型", "Get-PhysicalDisk | Select-Object FriendlyName,MediaType,Size"],
		["disk", "Optimize-Volume", "整理或优化卷", "Optimize-Volume -DriveLetter C -Defrag"],
		["disk", "Repair-Volume", "检查并修复卷", "Repair-Volume -DriveLetter D -Scan"],
		["disk", "Clear-Disk", "清空磁盘（危险操作）", "Clear-Disk -Number 2 -RemoveData", "会清除整块磁盘，先确认编号。"],
		["disk", "diskpart", "分区管理（交互）", "diskpart"],
		["disk", "chkdsk", "检查磁盘；/f 修复、/r 坏道", "chkdsk C: /f /r"],

		["svc", "Get-Service", "查看服务状态", "Get-Service | Where-Object Status -eq Running"],
		["svc", "Start-Service", "启动服务", "Start-Service Spooler"],
		["svc", "Stop-Service", "停止服务", "Stop-Service Spooler"],
		["svc", "Restart-Service", "重启服务", "Restart-Service Spooler"],
		["svc", "Set-Service", "修改服务启动类型", "Set-Service Spooler -StartupType Automatic"],
		["svc", "Get-ScheduledTask", "查看计划任务", "Get-ScheduledTask | Where-Object State -eq Ready"],
		["svc", "Register-ScheduledTask", "注册计划任务", "Register-ScheduledTask -TaskName Backup -Action $a -Trigger $t"],
		["svc", "Start-ScheduledTask", "立即运行计划任务", "Start-ScheduledTask -TaskName Backup"],
		["svc", "Get-WinEvent", "查看事件日志", "Get-WinEvent -LogName System -MaxEvents 20"],
		["svc", "eventvwr", "打开事件查看器", "eventvwr.msc"],
		["svc", "shutdown", "关机或重启；/t 延时", "shutdown /r /t 0"],
		["svc", "gpupdate /force", "强制刷新组策略", "gpupdate /force"],

		["archive", "Compress-Archive", "打包 zip", "Compress-Archive -Path .\\app -DestinationPath app.zip"],
		["archive", "Expand-Archive", "解压 zip", "Expand-Archive app.zip -DestinationPath .\\app"],
		["archive", "tar -czf", "打包 tar.gz（Win10 起自带）", "tar -czf app.tar.gz app"],
		["archive", "tar -xzf", "解压 tar.gz", "tar -xzf app.tar.gz"],
		["archive", "compact /c", "NTFS 透明压缩", "compact /c /s:D:\\data"],

		["dev", "git clone", "克隆仓库", "git clone https://github.com/user/repo.git"],
		["dev", "git status", "查看工作区状态", "git status -sb"],
		["dev", "git pull", "拉取并合并远端更新", "git pull --rebase"],
		["dev", "git push", "推送提交", "git push origin main"],
		["dev", "git log", "查看提交历史", "git log --oneline --graph -20"],
		["dev", "git add", "把改动加入暂存区", "git add ."],
		["dev", "git commit", "提交暂存区；-m 写说明", "git commit -m \"fix: update config\""],
		["dev", "python", "运行 Python 脚本", "python -X utf8 main.py"],
		["dev", "pip install", "安装 Python 依赖", "pip install -r requirements.txt"],
		["dev", "npm install", "安装 Node 依赖", "npm install"],
		["dev", "npx", "运行 npm 包里的命令", "npx serve ."],
		["dev", "dotnet build", "编译 .NET 项目", "dotnet build -c Release"],
		["dev", "dotnet run", "编译并运行 .NET 项目", "dotnet run"],
		["dev", "code .", "用 VS Code 打开当前目录", "code ."],
		["dev", "docker ps", "查看运行中的容器", "docker ps -a"],
		["dev", "docker run", "运行容器；-d 后台、-p 映射端口", "docker run -d -p 8080:80 nginx"],
		["dev", "docker exec", "在容器里执行命令", "docker exec -it web bash"],

		["sec", "Get-MpComputerStatus", "查看 Defender 防护状态", "Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled"],
		["sec", "Start-MpScan", "用 Defender 扫描", "Start-MpScan -ScanType FullScan"],
		["sec", "netsh advfirewall show allprofiles", "查看防火墙状态", "netsh advfirewall show allprofiles"],
		["sec", "netsh advfirewall firewall add rule", "放行端口", "netsh advfirewall firewall add rule name=\"web\" dir=in action=allow protocol=TCP localport=8080"],
		["sec", "netsh advfirewall firewall delete rule", "删除防火墙规则", "netsh advfirewall firewall delete rule name=\"web\""],
		["sec", "Get-AuthenticodeSignature", "查看文件数字签名", "Get-AuthenticodeSignature app.exe"],
		["sec", "Get-ItemProperty", "读注册表值", "Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion'"],
		["sec", "New-ItemProperty", "写注册表值", "New-ItemProperty -Path HKCU:\\Software\\App -Name Lang -Value zh"],
		["sec", "Remove-ItemProperty", "删除注册表值", "Remove-ItemProperty -Path HKCU:\\Software\\App -Name Lang"],
		["sec", "sfc /scannow", "扫描并修复系统文件", "sfc /scannow"],
		["sec", "DISM /Online /Cleanup-Image /RestoreHealth", "修复系统映像", "DISM /Online /Cleanup-Image /RestoreHealth"],

		["other", "Get-Help", "查看命令帮助；-Examples 看示例", "Get-Help Get-ChildItem -Examples"],
		["other", "Get-Command", "查找命令来自哪个模块", "Get-Command curl"],
		["other", "Get-Member", "查看对象的属性和方法", "Get-Process | Get-Member"],
		["other", "Get-Alias", "查看别名", "Get-Alias ls"],
		["other", "Set-Alias", "设置别名（仅当前会话）", "Set-Alias ll Get-ChildItem"],
		["other", "Clear-Host", "清屏（cls）", "Clear-Host"],
		["other", "Write-Host", "输出文本，可指定颜色", "Write-Host 'done' -ForegroundColor Green"],
		["other", "Start-Job", "后台运行脚本块", "Start-Job { Get-ChildItem C:\\ -Recurse }"],
		["other", "Receive-Job", "取后台任务结果", "Receive-Job -Id 1"],
		["other", "Get-Job", "查看后台任务", "Get-Job"],
		["other", "Update-Help", "更新 PowerShell 帮助", "Update-Help -Force"],
		["other", "exit", "退出当前会话", "exit"]
	];

	var CMD = [
		["file", "dir", "列出目录；/a 含隐藏、/s 递归", "dir /a"],
		["file", "cd /d", "切换盘符和目录", "cd /d D:\\work"],
		["file", "mkdir", "创建目录，可带多级", "mkdir logs\\2026"],
		["file", "rmdir /s /q", "递归删除目录，不提示", "rmdir /s /q build", "删除不进回收站，先确认路径。"],
		["file", "del", "删除文件；/f 强制、/s 递归", "del /f /s /q *.tmp"],
		["file", "copy", "复制文件；/y 不提示覆盖", "copy /y a.txt b.txt"],
		["file", "xcopy", "复制目录树；/e 含空目录、/i 建目标", "xcopy src dst /e /i /y"],
		["file", "robocopy", "镜像同步；/mir 镜像、/mt 多线程", "robocopy C:\\src D:\\dst /mir /mt:16"],
		["file", "move", "移动或重命名", "move old.txt new.txt"],
		["file", "ren", "重命名文件", "ren a.txt b.txt"],
		["file", "type", "显示文本文件内容", "type C:\\Windows\\win.ini"],
		["file", "attrib", "查看/修改文件属性", "attrib +h secret.txt"],
		["file", "mklink", "创建符号链接或目录联接", "mklink /d link C:\\target"],
		["file", "where", "按名称查找可执行文件", "where python"],
		["file", "tree", "树形列出目录", "tree /f /a"],
		["file", "fc", "比较两个文件的差异", "fc a.txt b.txt"],
		["file", "pushd", "切换目录并记住，popd 返回", "pushd D:\\work"],

		["text", "find", "按字面查找字符串", "find \"ERROR\" app.log"],
		["text", "findstr", "支持正则的查找；/s 递归、/i 忽略大小写", "findstr /s /i /n \"error\" *.log"],
		["text", "sort", "按行排序", "sort names.txt"],
		["text", "more", "分页显示长输出", "type big.log | more"],
		["text", "certutil -hashfile", "计算文件哈希", "certutil -hashfile app.iso SHA256"],
		["text", "certutil -encode", "Base64 编码/解码", "certutil -encode in.bin out.b64"],
		["text", "clip", "把输出送进剪贴板", "dir | clip"],
		["text", "chcp 65001", "切到 UTF-8 代码页", "chcp 65001"],
		["text", "powershell -Command", "在 cmd 里执行一条 PowerShell", "powershell -NoProfile -Command \"Get-FileHash .\\a.txt\""],

		["perm", "icacls", "查看/修改文件权限", "icacls app /grant alice:(OI)(CI)M"],
		["perm", "takeown", "取得文件所有权", "takeown /f C:\\locked /r /d y"],
		["perm", "whoami", "显示当前用户；/priv 看权限", "whoami /groups"],
		["perm", "net user", "查看/新建/删除本地用户", "net user alice P@ssw0rd /add"],
		["perm", "net localgroup", "管理本地组", "net localgroup administrators alice /add"],
		["perm", "runas", "以其他用户身份运行", "runas /user:alice cmd"],
		["perm", "query user", "查看已登录的会话", "query user"],
		["perm", "logoff", "注销指定会话", "logoff 2"],

		["pkg", "winget install", "安装软件", "winget install Microsoft.PowerShell"],
		["pkg", "winget upgrade --all", "升级全部可升级软件", "winget upgrade --all"],
		["pkg", "winget search", "搜索软件", "winget search vscode"],
		["pkg", "winget list", "列出已安装软件", "winget list"],
		["pkg", "choco install", "Chocolatey 安装（需先装 choco）", "choco install git -y"],
		["pkg", "scoop install", "Scoop 安装（用户级）", "scoop install ripgrep"],
		["pkg", "appwiz.cpl", "打开程序和功能", "appwiz.cpl"],

		["proc", "tasklist", "列出进程；/fi 过滤", "tasklist /fi \"imagename eq chrome.exe\""],
		["proc", "taskkill", "结束进程；/f 强制、/t 含子进程", "taskkill /f /pid 1234"],
		["proc", "start", "新窗口启动程序或打开文件", "start notepad"],
		["proc", "wmic process", "查进程命令行（已弃用）", "wmic process where \"name='python.exe'\" get commandline", "新系统改用 Get-CimInstance Win32_Process。"],
		["proc", "systeminfo", "查看系统信息和补丁", "systeminfo | findstr /i \"OS\""],
		["proc", "ver", "查看 Windows 版本号", "ver"],
		["proc", "set", "查看环境变量", "set PATH"],
		["proc", "setx", "永久写入环境变量", "setx PATH \"%PATH%;C:\\bin\""],
		["proc", "hostname", "查看计算机名", "hostname"],
		["proc", "timeout", "等待若干秒", "timeout /t 5"],
		["proc", "driverquery", "列出已安装驱动", "driverquery /v"],
		["proc", "powercfg /batteryreport", "生成电池使用报告", "powercfg /batteryreport"],

		["net", "ipconfig /all", "查看全部网络配置", "ipconfig /all"],
		["net", "ipconfig /flushdns", "清空 DNS 缓存", "ipconfig /flushdns"],
		["net", "ipconfig /release", "释放并重新获取 IP", "ipconfig /release && ipconfig /renew"],
		["net", "ping", "测试连通性；-t 持续", "ping -n 4 1.1.1.1"],
		["net", "tracert", "跟踪路由", "tracert example.com"],
		["net", "pathping", "路由加丢包统计", "pathping example.com"],
		["net", "nslookup", "DNS 查询", "nslookup example.com 8.8.8.8"],
		["net", "netstat -ano", "查看端口和 PID", "netstat -ano | findstr :8080"],
		["net", "arp -a", "查看 ARP 缓存", "arp -a"],
		["net", "route print", "查看路由表", "route print"],
		["net", "netsh interface show interface", "查看网卡状态", "netsh interface show interface"],
		["net", "netsh wlan show profiles", "列出保存的 Wi-Fi", "netsh wlan show profiles"],
		["net", "netsh wlan show profile", "查看某个 Wi-Fi 的密码", "netsh wlan show profile name=\"MyWiFi\" key=clear"],
		["net", "net use", "映射网络驱动器", "net use Z: \\\\server\\share /persistent:yes"],
		["net", "net share", "查看本机共享", "net share"],
		["net", "curl", "下载或调 HTTP 接口", "curl -I https://example.com"],
		["net", "ssh", "远程登录（Win10 起自带）", "ssh user@server"],
		["net", "mstsc", "打开远程桌面", "mstsc /v:192.168.1.10"],
		["net", "telnet", "测端口（需启用客户端）", "telnet 192.168.1.10 80"],

		["disk", "diskpart", "分区管理（交互）", "diskpart"],
		["disk", "chkdsk", "检查磁盘；/f 修复、/r 坏道", "chkdsk C: /f /r"],
		["disk", "format", "格式化卷", "format D: /fs:NTFS"],
		["disk", "vol", "查看卷标和序列号", "vol C:"],
		["disk", "wmic logicaldisk", "查看磁盘容量", "wmic logicaldisk get name,freespace,size"],
		["disk", "defrag", "碎片整理或优化", "defrag C: /o"],
		["disk", "cleanmgr", "磁盘清理", "cleanmgr /sagerun:1"],
		["disk", "mountvol", "挂载或卸载卷", "mountvol X: /d"],
		["disk", "subst", "用盘符映射目录", "subst X: D:\\work\\repo"],
		["disk", "cipher /w", "擦除卷上空闲空间", "cipher /w:D:"],

		["svc", "sc query", "查看服务状态", "sc query state= all | findstr SERVICE_NAME"],
		["svc", "sc start", "启动服务", "sc start Spooler"],
		["svc", "sc stop", "停止服务", "sc stop Spooler"],
		["svc", "sc config", "修改服务启动类型", "sc config Spooler start= auto", "start= 后面要留一个空格。"],
		["svc", "net start", "启动服务，或列出已启动服务", "net start"],
		["svc", "net stop", "停止服务", "net stop Spooler"],
		["svc", "schtasks /query", "查看计划任务", "schtasks /query /fo LIST /v"],
		["svc", "schtasks /create", "创建计划任务", "schtasks /create /tn Backup /tr \"D:\\backup.bat\" /sc daily /st 02:00"],
		["svc", "schtasks /run", "立即运行计划任务", "schtasks /run /tn Backup"],
		["svc", "schtasks /delete", "删除计划任务", "schtasks /delete /tn Backup /f"],
		["svc", "shutdown /r", "重启；/t 延时秒", "shutdown /r /t 0"],
		["svc", "shutdown /s", "关机", "shutdown /s /t 60"],
		["svc", "shutdown /a", "取消定时关机", "shutdown /a"],
		["svc", "gpupdate /force", "强制刷新组策略", "gpupdate /force"],
		["svc", "gpresult /r", "查看当前应用的组策略", "gpresult /r"],
		["svc", "eventvwr", "打开事件查看器", "eventvwr.msc"],

		["archive", "tar -czf", "打包 tar.gz（Win10 起自带）", "tar -czf app.tar.gz app"],
		["archive", "tar -xzf", "解压 tar.gz", "tar -xzf app.tar.gz"],
		["archive", "expand", "解出 .cab 或 .msi 里的文件", "expand app.cab -F:* C:\\out"],
		["archive", "makecab", "生成 cab 包", "makecab a.txt a.cab"],
		["archive", "compact /c", "NTFS 透明压缩", "compact /c /s:D:\\data"],
		["archive", "powershell Compress-Archive", "用 PowerShell 打 zip", "powershell -Command \"Compress-Archive -Path .\\app -DestinationPath app.zip\""],

		["dev", "git clone", "克隆仓库", "git clone https://github.com/user/repo.git"],
		["dev", "git status", "查看工作区状态", "git status -sb"],
		["dev", "git pull", "拉取并合并远端更新", "git pull --rebase"],
		["dev", "git push", "推送提交", "git push origin main"],
		["dev", "git log", "查看提交历史", "git log --oneline --graph -20"],
		["dev", "git add", "把改动加入暂存区", "git add ."],
		["dev", "git commit", "提交暂存区；-m 写说明", "git commit -m \"fix: update config\""],
		["dev", "python", "运行 Python 脚本", "python -X utf8 main.py"],
		["dev", "pip install", "安装 Python 依赖", "pip install -r requirements.txt"],
		["dev", "npm install", "安装 Node 依赖", "npm install"],
		["dev", "npx", "运行 npm 包里的命令", "npx serve ."],
		["dev", "msbuild", "编译 .sln / .csproj", "msbuild App.sln /p:Configuration=Release"],
		["dev", "dotnet build", "编译 .NET 项目", "dotnet build -c Release"],
		["dev", "dotnet run", "编译并运行 .NET 项目", "dotnet run"],
		["dev", "code .", "用 VS Code 打开当前目录", "code ."],
		["dev", "docker ps", "查看运行中的容器", "docker ps -a"],
		["dev", "docker run", "运行容器；-d 后台、-p 映射端口", "docker run -d -p 8080:80 nginx"],

		["sec", "netsh advfirewall show allprofiles", "查看防火墙状态", "netsh advfirewall show allprofiles"],
		["sec", "netsh advfirewall firewall add rule", "放行端口", "netsh advfirewall firewall add rule name=\"web\" dir=in action=allow protocol=TCP localport=8080"],
		["sec", "netsh advfirewall firewall delete rule", "删除防火墙规则", "netsh advfirewall firewall delete rule name=\"web\""],
		["sec", "netsh advfirewall set allprofiles state off", "关闭防火墙（临时排障）", "netsh advfirewall set allprofiles state off"],
		["sec", "sfc /scannow", "扫描并修复系统文件", "sfc /scannow"],
		["sec", "DISM /Online /Cleanup-Image /RestoreHealth", "修复系统映像", "DISM /Online /Cleanup-Image /RestoreHealth"],
		["sec", "cipher /e", "加密文件（EFS）", "cipher /e secret.txt"],
		["sec", "auditpol /get /category:*", "查看审核策略", "auditpol /get /category:*"],
		["sec", "reg query", "读注册表", "reg query HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion"],
		["sec", "reg add", "写注册表", "reg add HKCU\\Software\\App /v Lang /t REG_SZ /d zh /f"],
		["sec", "reg delete", "删注册表项", "reg delete HKCU\\Software\\App /f"],

		["other", "help", "列出内置命令", "help"],
		["other", "命令 /?", "查看某个命令的帮助", "xcopy /?"],
		["other", "cls", "清屏", "cls"],
		["other", "color", "设置前景和背景色", "color 0A"],
		["other", "title", "设置窗口标题", "title build"],
		["other", "doskey", "定义宏或别名", "doskey ll=dir /a"],
		["other", "cmd /c", "执行一条命令后关闭", "cmd /c \"dir > out.txt\""],
		["other", "start .", "用资源管理器打开当前目录", "start ."],
		["other", "mklink /j", "创建目录联接", "mklink /j link C:\\target"],
		["other", "exit", "退出当前命令行", "exit"]
	];

	// 三种命令行的语法要点，放在「语法帮助」组里；当前页签自己的那段排在最前。
	var SYNTAX = {
		cmd: [
			["syntax", "%VAR%", "CMD：变量用 % 取值；set 赋值时等号两边不能有空格", "set LOG=app.log && type %LOG%"],
			["syntax", "%1、%~dp0", "CMD：脚本参数；~dp0 是脚本所在目录，%* 是全部参数", "echo %~dp0"],
			["syntax", "^", "CMD：转义 & | < > ( ) 等符号", "echo a ^& b"],
			["syntax", "\"\"", "CMD：路径含空格时整体加双引号", "cd \"C:\\Program Files\""],
			["syntax", "& 与 && 与 ||", "CMD：依次执行、成功才继续、失败才执行", "copy a b && echo ok || echo fail"],
			["syntax", "> 与 >> 与 2>&1", "CMD：覆盖、追加、把错误并进标准输出", "app.exe > out.txt 2>&1"],
			["syntax", "if exist 与 for", "CMD：条件与循环；批处理里写 %%f，命令行里写 %f", "for %f in (*.log) do echo %f"],
			["syntax", "!var!", "CMD：循环里取最新值，先 setlocal enabledelayedexpansion", "setlocal enabledelayedexpansion & for /l %i in (1,1,3) do echo !i!"],
			["syntax", "rem 与 ::", "CMD：注释；行尾 ^ 可以续行", "rem 说明"],
			["syntax", "@echo off", "CMD：批处理开头关闭命令回显", "@echo off"]
		],
		powershell: [
			["syntax", "$var", "PowerShell：变量以 $ 开头，未赋值就是 $null", "$name = 'alice'"],
			["syntax", "'...' 与 \"...\"", "PowerShell：单引号不展开变量，双引号展开", "$n = 'x'; \"hi $n\""],
			["syntax", "` 反引号", "PowerShell：转义符，行尾反引号续行；`t 制表、`n 换行", "Write-Host \"a`tb\""],
			["syntax", "| Where-Object", "PowerShell：管道里传的是对象", "Get-Service | Where-Object Status -eq 'Running'"],
			["syntax", "-eq -like -match", "PowerShell：比较运算符；加 c 前缀区分大小写，取反用 -not", "'Abc' -like 'a*'"],
			["syntax", "@{} 与 @()", "PowerShell：哈希表和数组字面量", "$h = @{ name = 'a' }; @(1,2,3)"],
			["syntax", "& \"路径\"", "PowerShell：调用带空格的程序路径要用 &", "& 'C:\\Program Files\\App\\app.exe'"],
			["syntax", "try/catch/finally", "PowerShell：错误处理，配合 -ErrorAction Stop", "try { Get-Item nope -ErrorAction Stop } catch { $_.Exception.Message }"],
			["syntax", "# 与 <# #>", "PowerShell：单行注释与块注释", "# 注释"],
			["syntax", ".\\script.ps1", "PowerShell：运行当前目录的脚本要带 .\\", ".\\build.ps1"],
			["syntax", "-Name 值", "PowerShell：参数写成 -名 值，支持 Tab 补全和唯一前缀缩写", "Get-ChildItem -Path C:\\ -Recurse"]
		],
		pwsh: [
			["syntax", "? :", "pwsh：三元运算符（7.0+）", "$s = $n -gt 0 ? '正' : '负'"],
			["syntax", "?? 与 ??=", "pwsh：空合并和空赋值（7.0+）", "$v = $null ?? '默认'"],
			["syntax", "&& 与 ||", "pwsh：命令串联，语义与 cmd 相同（7.0+）", "Get-Process pwsh && 'ok'"],
			["syntax", "$using:var", "pwsh：并行块里引用外部变量（7.0+）", "1..3 | ForEach-Object -Parallel { \"$using:n\" } -ThrottleLimit 3"],
			["syntax", "-AsHashtable", "pwsh：ConvertFrom-Json 直接转哈希表（6.0+）", "Get-Content a.json -Raw | ConvertFrom-Json -AsHashtable"],
			["syntax", "--%", "pwsh：后面的参数原样交给原生程序，不再解析", "az --% group list --query \"[].name\""],
			["syntax", "$IsWindows", "pwsh：跨平台判断的自动变量", "if ($IsWindows) { 'win' } else { 'unix' }"],
			["syntax", "#requires -Version 7.0", "pwsh：脚本里声明最低版本", "#requires -Version 7.0"],
			["syntax", "Find-PSResource", "pwsh：PSResourceGet 查找模块（7.2 起自带）", "Find-PSResource PSReadLine"],
			["syntax", "$ErrorView", "pwsh：错误显示样式，7 默认 ConciseView", "$ErrorView = 'ConciseView'"],
			["syntax", "$PSNativeCommandArgumentPassing", "pwsh：原生命令参数传递方式，7.3 起默认 Standard", "$PSNativeCommandArgumentPassing = 'Standard'"]
		]
	};

	var SELF_ORDER = ["cmd", "powershell", "pwsh"];

	// 语法条目属于哪个命令行，示例要按它自己的写法取（CMD 页签里也可能列出 PS 语法）。
	var SYNTAX_SHELL = {};
	(function(){
		var t, i;
		for (t in SYNTAX) for (i = 0; i < SYNTAX[t].length; i++)
			SYNTAX_SHELL[SYNTAX[t][i][1]] = t === "cmd" ? "cmd" : "ps";
	})();

	function syntaxrows(self){
		var order = [self];
		var i;
		for (i = 0; i < SELF_ORDER.length; i++) if (SELF_ORDER[i] !== self) order.push(SELF_ORDER[i]);
		var out = [];
		for (i = 0; i < order.length; i++) out = out.concat(SYNTAX[order[i]]);
		return out;
	}

	// Windows PowerShell 5.1 里有、pwsh（PowerShell 7）里没有的命令。
	var PW_SH_SKIP = ["Get-WmiObject", "Get-LocalUser", "New-LocalUser", "Add-LocalGroupMember", "Get-LocalGroupMember"];

	// 只有 pwsh 才有的常用写法。
	var PW_SH_ONLY = [
		["proc", "$PSVersionTable", "查看 pwsh 版本和运行时", "$PSVersionTable"],
		["proc", "pwsh -NoProfile", "启动 PowerShell 7，不加载配置", "pwsh -NoProfile"],
		["proc", "Get-Uptime", "查看开机时长", "Get-Uptime"],
		["proc", "Get-Error", "查看最近一次错误的完整信息", "Get-Error"],
		["proc", "Get-Date -AsUTC", "按 UTC 显示时间", "Get-Date -AsUTC"],
		["proc", "Get-InstalledModule", "查看已安装模块和版本", "Get-InstalledModule"],
		["text", "ForEach-Object -Parallel", "并行处理管道对象", "1..10 | ForEach-Object -Parallel { $_ * 2 } -ThrottleLimit 5"],
		["text", "ConvertFrom-Json -AsHashtable", "把 JSON 转成哈希表", "Get-Content app.json -Raw | ConvertFrom-Json -AsHashtable"],
		["text", "Test-Json", "校验 JSON 或对照架构", "Test-Json -Json (Get-Content app.json -Raw) -SchemaFile schema.json"],
		["text", "ConvertFrom-Markdown", "把 Markdown 转成 HTML", "ConvertFrom-Markdown -Path README.md | Select-Object Html"],
		["text", "ConvertFrom-SecureString -AsPlainText", "把安全字符串还原成明文", "ConvertFrom-SecureString -SecureString $s -AsPlainText"],
		["perm", "Get-CimInstance Win32_UserAccount", "pwsh 里查看本地账户", "Get-CimInstance Win32_UserAccount | Select-Object Name,Disabled"],
		["net", "Test-Connection", "7 自带的连通性测试", "Test-Connection 1.1.1.1 -Count 4"],
		["other", "Set-PSReadLineOption -PredictionSource History", "开启命令历史预测", "Set-PSReadLineOption -PredictionSource History"],
		["other", "Get-PSReadLineKeyHandler", "查看可用的按键绑定", "Get-PSReadLineKeyHandler"],
		["other", "Install-PSResource", "用 PSResourceGet 安装模块", "Install-PSResource PSReadLine"],
		["other", "Get-Help -Online", "在浏览器里打开帮助", "Get-Help Get-ChildItem -Online"]
	];

	function pwshrows(){
		var out = [];
		var i;
		for (i = 0; i < PS.length; i++) if (PW_SH_SKIP.indexOf(PS[i][1]) < 0) out.push(PS[i]);
		for (i = 0; i < PW_SH_ONLY.length; i++) out.push(PW_SH_ONLY[i]);
		return out;
	}

	// 多行示例：数组每一项是一行，常用的写法配 # / rem 注释。
	// 键「ps|命令」给 Windows PowerShell 和 pwsh 共用，「cmd|命令」只给 CMD。
	var SAMP = {
		"ps|Get-ChildItem": [
			"# 列出当前目录（含隐藏项）",
			"Get-ChildItem -Force",
			"",
			"# 只看最近修改的 10 个文件",
			"Get-ChildItem -File | Sort-Object LastWriteTime -Descending |",
			"    Select-Object -First 10 Name, LastWriteTime"
		],

		"ps|Set-Location": [
			"# 路径含空格时加引号",
			"Set-Location 'D:\\work'",
			"Set-Location 'C:\\Program Files'",
			"",
			"# 看当前在哪",
			"Get-Location"
		],

		"ps|New-Item": [
			"# 建多级目录（已存在也不报错）",
			"New-Item -ItemType Directory -Path logs\\2026\\10 -Force",
			"",
			"# 建空文件",
			"New-Item -ItemType File -Path logs\\app.log -Force"
		],

		"ps|Remove-Item": [
			"# 先看要删什么，确认路径再动手",
			"Get-ChildItem .\\build -Recurse | Select-Object FullName",
			"",
			"# 递归删除，不进回收站",
			"Remove-Item .\\build -Recurse -Force"
		],

		"ps|Copy-Item": [
			"# 复制整个目录到备份位置",
			"Copy-Item .\\src .\\backup\\src -Recurse -Force",
			"",
			"# 只复制 .log 文件",
			"Copy-Item .\\logs\\*.log .\\backup\\ -Force"
		],

		"ps|Move-Item": [
			"# 重命名",
			"Move-Item .\\old.txt .\\new.txt",
			"",
			"# 移到别的目录",
			"Move-Item .\\new.txt .\\archive\\"
		],

		"ps|Get-Content": [
			"# 看末尾 20 行，并持续跟踪新内容（Ctrl+C 退出）",
			"Get-Content .\\app.log -Tail 20 -Wait",
			"",
			"# 整个文件按 UTF-8 读进来",
			"Get-Content .\\app.log -Encoding utf8 -Raw"
		],

		"ps|Set-Content": [
			"# 覆盖写入（显式指定 UTF-8）",
			"Set-Content .\\out.txt 'ok' -Encoding utf8",
			"",
			"# 一次写多行",
			"Set-Content .\\hosts.txt @('127.0.0.1 a', '127.0.0.1 b') -Encoding utf8"
		],

		"ps|Add-Content": [
			"# 追加一行，带时间戳；文件不存在会自动建",
			"$t = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'",
			"Add-Content .\\app.log \"[$t] started\" -Encoding utf8"
		],

		"ps|Test-Path": [
			"# 不存在才建，避免重复执行报错",
			"if (-not (Test-Path .\\logs)) {",
			"    New-Item -ItemType Directory .\\logs | Out-Null",
			"}"
		],

		"ps|Get-FileHash": [
			"# 算 SHA256，和官网给的值对比",
			"Get-FileHash .\\app.iso -Algorithm SHA256 | Format-List",
			"",
			"# 批量算一个目录里的压缩包",
			"Get-ChildItem .\\dist\\*.zip | Get-FileHash -Algorithm SHA256"
		],

		"ps|Compare-Object": [
			"# 只列出两个文件不一样的行",
			"Compare-Object (Get-Content .\\a.txt) (Get-Content .\\b.txt)",
			"",
			"# SideIndicator：=> 只在右边，<= 只在左边",
			"Compare-Object (Get-Content .\\a.txt) (Get-Content .\\b.txt) -IncludeEqual"
		],

		"ps|Get-Acl": [
			"# 看目录权限",
			"Get-Acl 'C:\\app' | Format-List",
			"",
			"# 只看访问规则",
			"(Get-Acl 'C:\\app').Access | Format-Table IdentityReference, FileSystemRights"
		],

		"ps|Get-PSDrive": [
			"# 各盘剩余空间（GB）",
			"Get-PSDrive -PSProvider FileSystem |",
			"    Select-Object Name, @{n='FreeGB';e={[math]::Round($_.Free/1GB,1)}}"
		],

		"ps|Select-String": [
			"# 在日志里找错误，显示文件名和行号",
			"Select-String -Path .\\logs\\*.log -Pattern 'ERROR' -SimpleMatch",
			"",
			"# 递归搜源码，忽略大小写",
			"Get-ChildItem -Recurse -Include *.cs | Select-String 'TODO' -CaseSensitive:$false"
		],

		"ps|Where-Object": [
			"# CPU 时间超过 100 秒的进程",
			"Get-Process | Where-Object CPU -gt 100",
			"",
			"# 内存大于 500MB 且不在会话 0（非系统服务）",
			"Get-Process | Where-Object { $_.WS -gt 500MB -and $_.SessionId -ne 0 }"
		],

		"ps|ForEach-Object": [
			"# 逐个文件打印大小",
			"Get-ChildItem *.log | ForEach-Object {",
			"    '{0}  {1:N0} KB' -f $_.Name, ($_.Length / 1KB)",
			"}"
		],

		"ps|Select-Object": [
			"# 取前 5 个进程，外加一列内存 MB",
			"Get-Process | Select-Object -First 5 Name, Id, @{n='MB';e={[int]($_.WS/1MB)}}",
			"",
			"# 取最后 3 个",
			"Get-Process | Select-Object -Last 3 Name"
		],

		"ps|Sort-Object": [
			"# 按 CPU 时间倒序取前 10",
			"Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 Name, CPU"
		],

		"ps|Measure-Object": [
			"# 统计日志行数和字符数",
			"Get-Content .\\app.log | Measure-Object -Line -Character",
			"",
			"# 求和、平均",
			"1..10 | Measure-Object -Sum -Average"
		],

		"ps|Group-Object": [
			"# 按进程名分组，看谁开的实例最多",
			"Get-Process | Group-Object ProcessName |",
			"    Sort-Object Count -Descending | Select-Object -First 10 Name, Count"
		],

		"ps|Import-Csv": [
			"# 读 CSV，按列取数",
			"Import-Csv .\\users.csv | Select-Object Name, Mail",
			"",
			"# 过滤后再处理",
			"Import-Csv .\\users.csv | Where-Object Enabled -eq 'true'"
		],

		"ps|Export-Csv": [
			"# -NoTypeInformation 去掉多余的 #TYPE 行",
			"Get-Process | Select-Object Name, Id, CPU |",
			"    Export-Csv .\\procs.csv -NoTypeInformation -Encoding utf8"
		],

		"ps|ConvertTo-Json": [
			"# 输出 JSON，-Depth 控制嵌套层数",
			"Get-Process | Select-Object -First 2 Name, Id | ConvertTo-Json -Depth 3",
			"",
			"# 直接写文件",
			"Get-ChildItem | ConvertTo-Json -Depth 2 | Set-Content .\\list.json -Encoding utf8"
		],

		"ps|ConvertFrom-Json": [
			"# 读 JSON 成对象，再取字段",
			"$cfg = Get-Content .\\app.json -Raw | ConvertFrom-Json",
			"$cfg.version"
		],

		"ps|Set-Clipboard": [
			"# 把日志最后 50 行放进剪贴板",
			"Get-Content .\\app.log -Tail 50 | Set-Clipboard"
		],

		"ps|Get-Clipboard": [
			"# 读剪贴板，按行去掉首尾空白",
			"Get-Clipboard | ForEach-Object { $_.Trim() }"
		],

		"ps|Out-Host -Paging": [
			"# 长输出分页：空格翻页，q 退出",
			"Get-ChildItem C:\\Windows -Recurse | Out-Host -Paging"
		],

		"ps|icacls": [
			"# 给 alice 修改权限，子目录继承",
			"icacls .\\app /grant alice:(OI)(CI)M",
			"",
			"# 查看当前权限",
			"icacls .\\app"
		],

		"ps|takeown": [
			"# 先取得所有权，再给管理员完全控制",
			"takeown /f C:\\locked /r /d y",
			"icacls C:\\locked /grant administrators:F /t"
		],

		"ps|Get-LocalUser": [
			"# 列出本地账户和状态",
			"Get-LocalUser | Format-Table Name, Enabled, LastLogon",
			"",
			"# 只看管理员组成员",
			"Get-LocalGroupMember Administrators | Select-Object Name"
		],

		"ps|Add-LocalGroupMember": [
			"# 把 alice 加进管理员组（需管理员权限）",
			"Add-LocalGroupMember -Group Administrators -Member alice",
			"",
			"# 确认结果",
			"Get-LocalGroupMember Administrators | Select-Object Name, ObjectClass"
		],

		"ps|Set-ExecutionPolicy": [
			"# 只对当前用户放开，不需要管理员",
			"Set-ExecutionPolicy RemoteSigned -Scope CurrentUser",
			"",
			"# 看各作用域的当前策略",
			"Get-ExecutionPolicy -List"
		],

		"ps|Start-Process -Verb RunAs": [
			"# 以管理员身份开一个新的 PowerShell",
			"Start-Process powershell -Verb RunAs",
			"",
			"# 以管理员身份跑脚本并等它结束",
			"Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-File','C:\\fix.ps1' -Wait"
		],

		"ps|winget install": [
			"# 先搜到准确的包 ID 再装",
			"winget search vscode",
			"winget install Microsoft.VisualStudioCode",
			"",
			"# 静默安装并自动接受协议",
			"winget install Git.Git --silent --accept-package-agreements"
		],

		"ps|winget upgrade --all": [
			"# 先看哪些能升级",
			"winget upgrade",
			"",
			"# 全部升级（包括来源未知的包）",
			"winget upgrade --all --include-unknown"
		],

		"ps|Get-AppxPackage": [
			"# 找已安装的商店应用",
			"Get-AppxPackage *store* | Select-Object Name, Version",
			"",
			"# 卸载当前用户的某个应用",
			"Get-AppxPackage *xbox* | Remove-AppxPackage"
		],

		"ps|Install-Module": [
			"# 装到当前用户，不动系统目录",
			"Install-Module PSReadLine -Scope CurrentUser -Force",
			"",
			"# 提示发布者不受信任时加这个",
			"Install-Module Pester -Scope CurrentUser -Force -SkipPublisherCheck"
		],

		"ps|Get-Process": [
			"# Chrome 各进程的内存占用",
			"Get-Process chrome | Select-Object Id, CPU, @{n='MB';e={[int]($_.WS/1MB)}}",
			"",
			"# 按内存排序取前 10",
			"Get-Process | Sort-Object WS -Descending | Select-Object -First 10 Name, Id"
		],

		"ps|Stop-Process": [
			"# 先看清 PID 是谁，再结束（未保存的数据会丢）",
			"Get-Process -Id 1234",
			"Stop-Process -Id 1234 -Force",
			"",
			"# 按名字结束所有同名进程",
			"Stop-Process -Name notepad -Force"
		],

		"ps|Start-Process": [
			"# 等程序退出再往下走",
			"Start-Process notepad -Wait",
			"",
			"# 传参数并隐藏窗口",
			"Start-Process python -ArgumentList '-X','utf8','main.py' -WindowStyle Hidden"
		],

		"ps|Get-CimInstance": [
			"# 看指定进程的完整命令行",
			"Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | Select-Object ProcessId, CommandLine",
			"",
			"# 开机时间和系统版本",
			"Get-CimInstance Win32_OperatingSystem | Select-Object LastBootUpTime, Version"
		],

		"ps|Get-ComputerInfo": [
			"# 系统和硬件概况（第一次跑比较慢）",
			"Get-ComputerInfo | Select-Object OsName, OsVersion, CsTotalPhysicalMemory"
		],

		"ps|$env:PATH": [
			"# 按分号拆开看当前 PATH",
			"$env:PATH -split ';'",
			"",
			"# 只在当前会话临时加一个目录",
			"$env:PATH = 'C:\\bin;' + $env:PATH"
		],

		"ps|[Environment]::SetEnvironmentVariable": [
			"# 永久写入用户变量（新开窗口才生效）",
			"[Environment]::SetEnvironmentVariable('APP_HOME', 'D:\\app', 'User')",
			"",
			"# 读回来确认",
			"[Environment]::GetEnvironmentVariable('APP_HOME', 'User')"
		],

		"ps|Get-Date": [
			"# 常用格式",
			"Get-Date -Format 'yyyy-MM-dd HH:mm:ss'",
			"Get-Date -Format 'yyyyMMdd_HHmmss'",
			"",
			"# 7 天前的日期",
			"(Get-Date).AddDays(-7).ToString('yyyy-MM-dd')"
		],

		"ps|Start-Sleep": [
			"# 等 5 秒",
			"Start-Sleep -Seconds 5",
			"",
			"# 带倒计时",
			"1..5 | ForEach-Object { \"$_ ...\"; Start-Sleep -Seconds 1 }"
		],

		"ps|Measure-Command": [
			"# 测一条命令耗时",
			"Measure-Command { Get-ChildItem C:\\Windows -Recurse } | Select-Object TotalSeconds"
		],

		"ps|Get-History": [
			"# 看最近的命令",
			"Get-History -Count 20 | Format-Table Id, CommandLine -AutoSize",
			"",
			"# 重新执行第 12 条",
			"Invoke-History 12"
		],

		"ps|Start-Transcript": [
			"# 把这一整段会话记录到文件",
			"Start-Transcript .\\session.txt",
			"# ... 做一些操作 ...",
			"Stop-Transcript"
		],

		"ps|Get-Random": [
			"# 随机整数、随机挑一个",
			"Get-Random -Minimum 1 -Maximum 100",
			"Get-Random -InputObject @('a','b','c')"
		],

		"ps|Get-NetIPAddress": [
			"# 只看 IPv4 地址",
			"Get-NetIPAddress -AddressFamily IPv4 |",
			"    Select-Object InterfaceAlias, IPAddress, PrefixLength"
		],

		"ps|Get-NetAdapter": [
			"# 网卡、状态、速率",
			"Get-NetAdapter | Select-Object Name, Status, LinkSpeed, MacAddress"
		],

		"ps|Test-NetConnection": [
			"# 测端口通不通",
			"Test-NetConnection 192.168.1.10 -Port 445",
			"",
			"# 只要一个是/否的结果",
			"Test-NetConnection example.com -Port 443 -InformationLevel Quiet"
		],

		"ps|Resolve-DnsName": [
			"# 查 A 记录和 CNAME",
			"Resolve-DnsName example.com -Type A",
			"Resolve-DnsName www.example.com -Type CNAME"
		],

		"ps|Clear-DnsClientCache": [
			"# 改完 hosts 或 DNS 后清缓存，再验证",
			"Clear-DnsClientCache",
			"Resolve-DnsName example.com"
		],

		"ps|Get-NetTCPConnection": [
			"# 看 8080 被谁监听，再查进程名",
			"Get-NetTCPConnection -LocalPort 8080 -State Listen |",
			"    Select-Object LocalAddress, OwningProcess",
			"Get-Process -Id (Get-NetTCPConnection -LocalPort 8080 -State Listen).OwningProcess"
		],

		"ps|Invoke-WebRequest": [
			"# 只看响应头",
			"Invoke-WebRequest https://example.com -Method Head",
			"",
			"# 下载文件",
			"Invoke-WebRequest https://example.com/a.zip -OutFile .\\a.zip"
		],

		"ps|Invoke-RestMethod": [
			"# 调接口，结果直接是对象",
			"$r = Invoke-RestMethod https://api.example.com/items",
			"$r | Select-Object -First 3",
			"",
			"# 带认证头",
			"Invoke-RestMethod https://api.example.com/me -Headers @{ Authorization = 'Bearer xxx' }"
		],

		"ps|ipconfig /all": [
			"# 看全部网络配置",
			"ipconfig /all",
			"",
			"# 只看 IPv4 地址",
			"ipconfig | Select-String 'IPv4'"
		],

		"ps|ping": [
			"# 发 4 个包",
			"ping -n 4 1.1.1.1",
			"",
			"# 持续 ping，Ctrl+C 停",
			"ping -t 192.168.1.1"
		],

		"ps|netstat -ano": [
			"# 找 8080 端口的 PID",
			"netstat -ano | Select-String ':8080'",
			"",
			"# 用 PID 查进程名",
			"Get-Process -Id 1234"
		],

		"ps|Get-SmbShare": [
			"# 看本机共享目录",
			"Get-SmbShare | Select-Object Name, Path, Description"
		],

		"ps|New-SmbShare": [
			"# 建共享，给 alice 完全控制",
			"New-SmbShare -Name share -Path D:\\share -FullAccess alice",
			"",
			"# 查看结果",
			"Get-SmbShare share"
		],

		"ps|curl.exe": [
			"# 明确调 curl.exe（PowerShell 里 curl 是别名）",
			"curl.exe -I https://example.com",
			"",
			"# 跟随跳转下载",
			"curl.exe -L -o a.zip https://example.com/a.zip"
		],

		"ps|mstsc": [
			"# 连远程桌面",
			"mstsc /v:192.168.1.10",
			"",
			"# 指定窗口大小",
			"mstsc /v:192.168.1.10 /w:1600 /h:900"
		],

		"ps|Get-Volume": [
			"# 各卷剩余空间（GB）",
			"Get-Volume | Select-Object DriveLetter, FileSystemLabel,",
			"    @{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB,1)}}"
		],

		"ps|Get-Disk": [
			"# 磁盘编号后面分区要用",
			"Get-Disk | Select-Object Number, FriendlyName, Size, PartitionStyle"
		],

		"ps|Get-Partition": [
			"# 分区和对应盘符",
			"Get-Partition | Select-Object DiskNumber, PartitionNumber, DriveLetter, Size"
		],

		"ps|Get-PhysicalDisk": [
			"# SSD 还是 HDD、健康状态",
			"Get-PhysicalDisk | Select-Object FriendlyName, MediaType, Size, HealthStatus"
		],

		"ps|Optimize-Volume": [
			"# SSD 用 Trim，机械盘用 Defrag",
			"Optimize-Volume -DriveLetter C -ReTrim",
			"Optimize-Volume -DriveLetter D -Defrag"
		],

		"ps|Repair-Volume": [
			"# 先扫描，有问题再修",
			"Repair-Volume -DriveLetter D -Scan",
			"Repair-Volume -DriveLetter D -SpotFix"
		],

		"ps|diskpart": [
			"# 交互式分区，每步都会提示",
			"diskpart",
			"list disk",
			"select disk 2"
		],

		"ps|chkdsk": [
			"# 只读检查",
			"chkdsk C:",
			"",
			"# 修复（系统盘会安排到重启后执行）",
			"chkdsk C: /f /r"
		],

		"ps|Get-Service": [
			"# 只看正在运行的服务",
			"Get-Service | Where-Object Status -eq Running",
			"",
			"# 名字里带 sql 的",
			"Get-Service *sql* | Select-Object Name, Status, StartType"
		],

		"ps|Start-Service": [
			"# 启动服务",
			"Start-Service Spooler",
			"",
			"# 起不来就看状态和启动类型",
			"Get-Service Spooler | Select-Object Status, StartType"
		],

		"ps|Stop-Service": [
			"# 有依赖服务时加 -Force",
			"Stop-Service Spooler -Force",
			"Get-Service Spooler"
		],

		"ps|Register-ScheduledTask": [
			"# 每天 2 点跑一个脚本",
			"$act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-File D:\\backup.ps1'",
			"$trg = New-ScheduledTaskTrigger -Daily -At 2am",
			"Register-ScheduledTask -TaskName Backup -Action $act -Trigger $trg -RunLevel Highest"
		],

		"ps|Get-ScheduledTask": [
			"# 只看就绪状态的任务",
			"Get-ScheduledTask | Where-Object State -eq Ready |",
			"    Select-Object TaskPath, TaskName, State"
		],

		"ps|Get-WinEvent": [
			"# 系统日志最近 20 条",
			"Get-WinEvent -LogName System -MaxEvents 20",
			"",
			"# 只看错误级别",
			"Get-WinEvent -FilterHashtable @{ LogName='System'; Level=2 } -MaxEvents 10"
		],

		"ps|shutdown": [
			"# 30 秒后重启（先提示保存）",
			"shutdown /r /t 30",
			"",
			"# 取消计划中的关机",
			"shutdown /a"
		],

		"ps|gpupdate /force": [
			"# 改完组策略立刻生效",
			"gpupdate /force",
			"",
			"# 看应用了哪些策略",
			"gpresult /r"
		],

		"ps|Compress-Archive": [
			"# 把目录打成 zip（同名会覆盖）",
			"Compress-Archive -Path .\\app -DestinationPath .\\app.zip -Force",
			"",
			"# 只打包部分文件",
			"Compress-Archive -Path .\\app\\*.exe, .\\app\\*.dll -DestinationPath .\\bin.zip"
		],

		"ps|Expand-Archive": [
			"# 解压到指定目录，目录不存在会自动建",
			"Expand-Archive .\\app.zip -DestinationPath .\\out -Force"
		],

		"ps|tar -czf": [
			"# Win10 起自带 tar",
			"tar -czf app.tar.gz app",
			"",
			"# 排除日志",
			"tar -czf app.tar.gz --exclude=*.log app"
		],

		"ps|tar -xzf": [
			"# 先看包里有什么",
			"tar -tzf app.tar.gz | Select-Object -First 20",
			"",
			"# 解压到当前目录",
			"tar -xzf app.tar.gz"
		],

		"ps|compact /c": [
			"# 对目录开启 NTFS 压缩（含子目录）",
			"compact /c /s:D:\\data",
			"",
			"# 查看压缩状态",
			"compact /s:D:\\data"
		],

		"ps|git clone": [
			"# 克隆并进入目录",
			"git clone https://github.com/user/repo.git",
			"Set-Location repo",
			"",
			"# 只要最近一次提交，省时间",
			"git clone --depth 1 https://github.com/user/repo.git"
		],

		"ps|git status": [
			"# 简短状态",
			"git status -sb",
			"",
			"# 看具体改了哪些行",
			"git diff"
		],

		"ps|git log": [
			"# 一行一条，带分支图",
			"git log --oneline --graph --decorate -20",
			"",
			"# 某个文件的修改历史",
			"git log --oneline -- app.json"
		],

		"ps|git commit": [
			"# 提交并写说明",
			"git commit -m \"fix: 修正端口解析\"",
			"",
			"# 还没推送时，追加到上一次提交",
			"git commit --amend --no-edit"
		],

		"ps|python": [
			"# 用 UTF-8 跑脚本，避免控制台乱码",
			"python -X utf8 main.py",
			"",
			"# 传参数",
			"python -X utf8 main.py --input .\\data.csv --out .\\out"
		],

		"ps|pip install": [
			"# 按 requirements 安装",
			"pip install -r requirements.txt",
			"",
			"# 装到当前用户，不动系统环境",
			"pip install --user requests"
		],

		"ps|docker ps": [
			"# 运行中的容器",
			"docker ps",
			"",
			"# 连停止的一起看",
			"docker ps -a --format \"table {{.Names}}\\t{{.Status}}\\t{{.Ports}}\""
		],

		"ps|docker run": [
			"# 后台跑 nginx，映射 8080",
			"docker run -d --name web -p 8080:80 nginx",
			"",
			"# 挂配置目录并自动重启",
			"docker run -d --name web -p 8080:80 -v ${PWD}/conf:/etc/nginx/conf.d --restart unless-stopped nginx"
		],

		"ps|sfc /scannow": [
			"# 扫描并修复系统文件（要管理员）",
			"sfc /scannow",
			"",
			"# 修复不了的会记在 CBS 日志里",
			"Select-String -Path C:\\Windows\\Logs\\CBS\\CBS.log -Pattern 'Cannot repair'"
		],

		"ps|DISM /Online /Cleanup-Image /RestoreHealth": [
			"# 先检查映像健康状态",
			"DISM /Online /Cleanup-Image /CheckHealth",
			"",
			"# 有问题再修复（要管理员，可能较久）",
			"DISM /Online /Cleanup-Image /RestoreHealth"
		],

		"ps|Get-Help": [
			"# 看示例和完整参数",
			"Get-Help Get-ChildItem -Examples",
			"Get-Help Get-ChildItem -Full",
			"",
			"# 在浏览器打开官方文档",
			"Get-Help Get-ChildItem -Online"
		],

		"ps|Get-Member": [
			"# 看对象有哪些属性可用",
			"Get-Process | Get-Member",
			"",
			"# 只看属性名",
			"Get-Process | Get-Member -MemberType Property | Select-Object Name"
		],

		"ps|Get-Alias": [
			"# ls 实际指向哪个命令",
			"Get-Alias ls",
			"",
			"# 找某个命令的所有别名",
			"Get-Alias | Where-Object Definition -like '*ChildItem*'"
		],

		"ps|Set-Alias": [
			"# 临时别名，只在当前会话有效",
			"Set-Alias ll Get-ChildItem",
			"ll -Force"
		],

		"ps|Start-Job": [
			"# 后台跑，不阻塞当前窗口",
			"$j = Start-Job { Get-ChildItem C:\\ -Recurse -ErrorAction SilentlyContinue }",
			"$j"
		],

		"ps|Get-Job": [
			"# 看后台任务状态",
			"Get-Job | Format-Table Id, State, HasMoreData, Command",
			"",
			"# 清掉已完成的",
			"Get-Job | Remove-Job"
		],

		"ps|Write-Host": [
			"# 彩色输出",
			"Write-Host '完成' -ForegroundColor Green",
			"Write-Host '警告' -ForegroundColor Yellow",
			"Write-Host '失败' -ForegroundColor Red"
		],

		"ps|Get-ItemProperty": [
			"# 读注册表值",
			"Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion' |",
			"    Select-Object ProgramFilesDir",
			"",
			"# 列出某个键下的所有值",
			"Get-ItemProperty 'HKCU:\\Software\\App'"
		],

		"ps|New-ItemProperty": [
			"# 写字符串值，不存在就创建",
			"New-ItemProperty -Path HKCU:\\Software\\App -Name Lang -Value zh -PropertyType String -Force",
			"",
			"# 读回来确认",
			"Get-ItemProperty HKCU:\\Software\\App -Name Lang"
		],

		"ps|netsh advfirewall firewall add rule": [
			"# 放行 TCP 8080 入站，只对专用网络",
			"netsh advfirewall firewall add rule name=\"web 8080\" dir=in action=allow protocol=TCP localport=8080 profile=private",
			"",
			"# 确认规则已加上",
			"netsh advfirewall firewall show rule name=\"web 8080\""
		],

		"ps|Get-MpComputerStatus": [
			"# Defender 是否开着实时防护、病毒库日期",
			"Get-MpComputerStatus | Select-Object AntivirusEnabled,",
			"    RealTimeProtectionEnabled, AntivirusSignatureLastUpdated"
		],

		"ps|Update-Help": [
			"# 更新帮助文档（要管理员）",
			"Update-Help -Force",
			"",
			"# 只更新一个模块",
			"Update-Help -Module Microsoft.PowerShell.Management -Force"
		],

		"ps|$PSVersionTable": [
			"# 看 pwsh 版本和运行时",
			"$PSVersionTable",
			"",
			"# 只要主版本号",
			"$PSVersionTable.PSVersion.Major"
		],

		"ps|pwsh -NoProfile": ["# 不加载配置启动，用来排查配置问题", "pwsh -NoProfile"],

		"ps|Get-Uptime": ["# 开机时长", "Get-Uptime"],

		"ps|Get-Error": ["# 看最近一次错误的完整信息", "Get-Error"],

		"ps|Get-Date -AsUTC": ["# 按 UTC 显示时间", "Get-Date -AsUTC"],

		"ps|Get-InstalledModule": ["# 已安装模块和版本", "Get-InstalledModule | Select-Object Name, Version"],

		"ps|ForEach-Object -Parallel": [
			"# 并行处理，-ThrottleLimit 限制并发数",
			"1..10 | ForEach-Object -Parallel { $_ * 2 } -ThrottleLimit 5"
		],

		"ps|ConvertFrom-Json -AsHashtable": [
			"# JSON 直接转哈希表",
			"$cfg = Get-Content .\\app.json -Raw | ConvertFrom-Json -AsHashtable",
			"$cfg['port']"
		],

		"ps|Test-Json": [
			"# 用 schema 校验 JSON 是否合规",
			"Test-Json -Json (Get-Content .\\app.json -Raw) -SchemaFile schema.json"
		],

		"ps|ConvertFrom-Markdown": ["# Markdown 转 HTML", "ConvertFrom-Markdown -Path README.md | Select-Object Html"],

		"ps|ConvertFrom-SecureString -AsPlainText": [
			"# 把安全字符串还原成明文（注意别写进日志）",
			"ConvertFrom-SecureString -SecureString $s -AsPlainText"
		],

		"ps|Get-CimInstance Win32_UserAccount": [
			"# pwsh 里查本地账户",
			"Get-CimInstance Win32_UserAccount | Select-Object Name, Disabled"
		],

		"ps|Test-Connection": ["# 7 自带的连通性测试", "Test-Connection 1.1.1.1 -Count 4"],

		"ps|Set-PSReadLineOption -PredictionSource History": [
			"# 开启历史命令预测",
			"Set-PSReadLineOption -PredictionSource History"
		],

		"ps|Get-PSReadLineKeyHandler": ["# 看可用的按键绑定", "Get-PSReadLineKeyHandler"],

		"ps|Install-PSResource": ["# 用 PSResourceGet 安装模块", "Install-PSResource PSReadLine"],

		"ps|Get-Help -Online": ["# 在浏览器打开帮助", "Get-Help Get-ChildItem -Online"],

		"ps|Get-Module -ListAvailable": [
			"# 列出可用模块和版本",
			"Get-Module -ListAvailable | Select-Object Name, Version, Path"
		],

		"ps|Rename-Item": [
			"# 批量把 .txt 改成 .md",
			"Get-ChildItem *.txt | Rename-Item -NewName { $_.Name -replace '\\.txt$', '.md' }"
		],

		"ps|Get-Item": [
			"# 看大小和修改时间",
			"Get-Item .\\app.exe | Select-Object Length, LastWriteTime",
			"",
			"# 看目录下的项（含隐藏）",
			"Get-Item .\\* -Force"
		],

		"ps|Out-File": [
			"# 输出 UTF-8 文本（> 在 5.1 里是 UTF-16）",
			"Get-Process | Out-File .\\procs.txt -Encoding utf8"
		],

		"ps|Tee-Object": [
			"# 同时打印到屏幕并写文件",
			"Get-ChildItem | Tee-Object -FilePath .\\list.txt"
		],

		"ps|Restart-Service": [
			"# 改完配置重启",
			"Restart-Service Spooler",
			"Get-Service Spooler | Select-Object Status"
		],

		"ps|Set-Service": [
			"# 改成自动启动",
			"Set-Service Spooler -StartupType Automatic",
			"Get-Service Spooler | Select-Object StartType"
		],

		"ps|Start-ScheduledTask": [
			"# 立即跑一次，不用等定时",
			"Start-ScheduledTask -TaskName Backup",
			"Get-ScheduledTaskInfo -TaskName Backup | Select-Object LastRunTime, LastTaskResult"
		],

		"ps|winget search": [
			"# 搜关键词",
			"winget search vscode",
			"",
			"# 只看商店源",
			"winget search --source msstore notepad"
		],

		"ps|winget list": [
			"# 列出已安装的包",
			"winget list",
			"",
			"# 按名字过滤",
			"winget list --name python"
		],

		"ps|Get-ExecutionPolicy": ["# 看各作用域的执行策略", "Get-ExecutionPolicy -List"],

		"ps|Get-Command": [
			"# 看命令来自哪个模块，是不是别名",
			"Get-Command curl",
			"Get-Command Get-ChildItem -All"
		],

		"ps|Get-DnsClientCache": [
			"# 看缓存里前 10 条",
			"Get-DnsClientCache | Select-Object -First 10 EntryName, Data"
		],

		"ps|Get-Counter": [
			"# 采样 3 次 CPU 占用",
			"Get-Counter '\\Processor(_Total)\\% Processor Time' -MaxSamples 3"
		],

		"ps|eventvwr": ["# 打开事件查看器", "eventvwr.msc"],

		"ps|whoami /priv": ["# 看当前账号的特权", "whoami /priv", "", "# 所属组", "whoami /groups"],

		"ps|Get-LocalGroupMember": [
			"# 看管理员组成员",
			"Get-LocalGroupMember -Group Administrators | Select-Object Name, ObjectClass"
		],

		"ps|New-LocalUser": [
			"# 新建本地用户，密码手动输入不回显",
			"$pw = Read-Host '密码' -AsSecureString",
			"New-LocalUser -Name alice -Password $pw"
		],

		"ps|Clear-Disk": [
			"# 危险：清空整块磁盘，先确认编号",
			"Get-Disk",
			"Clear-Disk -Number 2 -RemoveData -Confirm"
		],

		"ps|Clear-Host": ["# 清屏", "Clear-Host"],

		"ps|exit": ["# 退出当前会话（脚本里就是结束）", "exit"],

		"ps|Get-WmiObject": [
			"# 5.1 专用；pwsh 已移除，改用 Get-CimInstance",
			"Get-WmiObject Win32_LogicalDisk | Select-Object DeviceID, Size, FreeSpace"
		],

		"ps|Add-AppxPackage": [
			"# 安装本地 msix 和依赖",
			"Add-AppxPackage .\\app.msix",
			"Add-AppxPackage .\\deps\\*.appx"
		],

		"ps|choco install": ["# 安装并确认", "choco install git -y", "choco list --local-only"],

		"ps|scoop install": ["# 用户级安装，不需要管理员", "scoop install ripgrep", "scoop list"],

		"ps|git pull": ["# 拉取并变基", "git fetch origin", "git pull --rebase"],

		"ps|git push": ["# 首次推送设上游", "git push -u origin main"],

		"ps|git add": ["# 全部改动加入暂存区", "git add .", "git status -sb"],

		"ps|npm install": [
			"# 按 package.json 装依赖",
			"npm install",
			"",
			"# 装开发依赖",
			"npm install -D typescript"
		],

		"ps|npx": ["# 不全局安装，直接跑一个包", "npx serve ."],

		"ps|dotnet build": ["# Release 编译", "dotnet build -c Release"],

		"ps|dotnet run": ["# -- 后面的参数传给程序", "dotnet run -- --input a.txt"],

		"ps|code .": ["# 用 VS Code 打开当前目录", "code ."],

		"ps|docker exec": ["# 进容器开 bash", "docker exec -it web bash", "", "# 只跑一条命令", "docker exec web nginx -t"],

		"ps|Start-MpScan": [
			"# 快速扫描（比全盘快）",
			"Start-MpScan -ScanType QuickScan",
			"",
			"# 扫指定目录",
			"Start-MpScan -ScanType CustomScan -ScanPath D:\\downloads"
		],

		"ps|netsh advfirewall show allprofiles": ["# 看三个配置文件（域/专用/公用）的状态", "netsh advfirewall show allprofiles"],

		"ps|netsh advfirewall firewall delete rule": [
			"# 按名字删规则",
			"netsh advfirewall firewall delete rule name=\"web 8080\""
		],

		"ps|Get-AuthenticodeSignature": [
			"# 看签名状态和签名者",
			"Get-AuthenticodeSignature .\\app.exe | Format-List Status, SignerCertificate"
		],

		"ps|Remove-ItemProperty": [
			"# 删除某个值（不删整个键）",
			"Remove-ItemProperty -Path HKCU:\\Software\\App -Name Lang"
		],

		"ps|Receive-Job": ["# 取结果，-Keep 取完不删", "Receive-Job -Id 1"],

		"ps|Resolve-Path": ["# 展开成绝对路径；通配符也支持", "Resolve-Path .\\app", "Resolve-Path .\\logs\\*.log"],

		"ps|New-Item -ItemType SymbolicLink": [
			"# 建符号链接（管理员或开发者模式）",
			"New-Item -ItemType SymbolicLink -Path .\\link -Target 'C:\\target'",
			"",
			"# 目录联接不需要管理员",
			"New-Item -ItemType Junction -Path .\\j -Target 'D:\\data'"
		],

		"cmd|dir": [
			"rem 列出含隐藏项，按修改时间倒序",
			"dir /a /o-d",
			"",
			"rem 递归找某个文件",
			"dir /s /b *.log"
		],

		"cmd|cd /d": [
			"rem 跨盘符切换要加 /d",
			"cd /d D:\\work",
			"",
			"rem 看当前目录",
			"cd"
		],

		"cmd|mkdir": [
			"rem 一次建多级目录",
			"mkdir logs\\2026\\10",
			"",
			"rem 建多个同级目录",
			"mkdir out1 out2 out3"
		],

		"cmd|rmdir /s /q": [
			"rem 先看清目录内容再删",
			"dir build",
			"",
			"rem /s 含子目录，/q 不问确认",
			"rmdir /s /q build"
		],

		"cmd|del": [
			"rem 先列出要删的文件",
			"dir /s /b *.tmp",
			"",
			"rem /f 强制、/s 递归、/q 不问",
			"del /f /s /q *.tmp"
		],

		"cmd|xcopy": [
			"rem 复制目录树，含空目录",
			"xcopy src dst /e /i /y",
			"",
			"rem 只复制比目标新的文件",
			"xcopy src dst /d /y"
		],

		"cmd|robocopy": [
			"rem 镜像同步；退出码 0-7 都算成功",
			"robocopy C:\\src D:\\dst /mir /mt:16",
			"",
			"rem 先演练，不实际改动",
			"robocopy C:\\src D:\\dst /mir /l"
		],

		"cmd|move": ["rem 重命名", "move old.txt new.txt", "", "rem 移到别的目录", "move new.txt D:\\archive\\"],

		"cmd|ren": ["rem 只改文件名，不能带路径", "ren a.txt b.txt"],

		"cmd|type": [
			"rem 显示文件内容；大文件用 more 分页",
			"type C:\\Windows\\win.ini",
			"",
			"rem 一次看多个",
			"type a.txt b.txt"
		],

		"cmd|attrib": [
			"rem 隐藏文件",
			"attrib +h secret.txt",
			"",
			"rem 取消隐藏和只读",
			"attrib -h -r secret.txt"
		],

		"cmd|mklink": [
			"rem 目录符号链接要 /d（需管理员或开发者模式）",
			"mklink /d link C:\\target",
			"",
			"rem 目录联接用 /j，不需要管理员",
			"mklink /j j C:\\target"
		],

		"cmd|where": [
			"rem 找可执行文件在哪",
			"where python",
			"",
			"rem 在整个盘里找",
			"where /r C:\\ python.exe"
		],

		"cmd|find": [
			"rem 按字面查找（不支持正则）",
			"find \"ERROR\" app.log",
			"",
			"rem 忽略大小写并显示行号",
			"find /i /n \"error\" app.log"
		],

		"cmd|findstr": [
			"rem 递归搜 .log，忽略大小写并显示行号",
			"findstr /s /i /n \"error\" *.log",
			"",
			"rem 多个关键词任一命中",
			"findstr /i \"error fail\" app.log"
		],

		"cmd|certutil -hashfile": ["rem 算 SHA256", "certutil -hashfile app.iso SHA256"],

		"cmd|clip": [
			"rem 把输出送进剪贴板",
			"dir | clip",
			"",
			"rem 贴出来验证",
			"powershell -NoProfile -Command Get-Clipboard"
		],

		"cmd|chcp 65001": [
			"rem 切到 UTF-8，解决中文乱码",
			"chcp 65001",
			"",
			"rem 看当前代码页",
			"chcp"
		],

		"cmd|icacls": [
			"rem 给 alice 修改权限，子目录继承",
			"icacls app /grant alice:(OI)(CI)M",
			"",
			"rem 查看权限",
			"icacls app"
		],

		"cmd|takeown": [
			"rem 先取得所有权，再授权",
			"takeown /f C:\\locked /r /d y",
			"icacls C:\\locked /grant administrators:F /t"
		],

		"cmd|whoami": [
			"rem 看当前用户和所属组",
			"whoami",
			"whoami /groups",
			"",
			"rem 看特权列表",
			"whoami /priv"
		],

		"cmd|net user": [
			"rem 看账户列表",
			"net user",
			"",
			"rem 新建用户",
			"net user alice P@ssw0rd /add"
		],

		"cmd|net localgroup": [
			"rem 看本地组",
			"net localgroup",
			"",
			"rem 把 alice 加进管理员组",
			"net localgroup administrators alice /add"
		],

		"cmd|runas": ["rem 以其他用户身份开一个 cmd", "runas /user:alice cmd"],

		"cmd|query user": ["rem 看已登录会话和 ID", "query user", "", "rem 注销会话 2", "logoff 2"],

		"cmd|tasklist": [
			"rem 按映像名过滤",
			"tasklist /fi \"imagename eq chrome.exe\"",
			"",
			"rem 看 PID 和内存占用",
			"tasklist /fo table"
		],

		"cmd|taskkill": [
			"rem 先按端口找到 PID",
			"netstat -ano | findstr :8080",
			"",
			"rem 结束该进程及其子进程",
			"taskkill /f /t /pid 1234"
		],

		"cmd|start": [
			"rem 新窗口启动程序",
			"start notepad",
			"",
			"rem 用默认程序打开文件或网址",
			"start https://example.com"
		],

		"cmd|systeminfo": [
			"rem 只挑系统版本相关的行",
			"systeminfo | findstr /i \"OS Version\"",
			"",
			"rem 完整信息（比较慢）",
			"systeminfo"
		],

		"cmd|set": ["rem 看某个变量", "set PATH", "", "rem 看所有以 JAVA 开头的", "set JAVA"],

		"cmd|setx": [
			"rem 永久写用户变量（新窗口生效）",
			"setx APP_HOME D:\\app",
			"",
			"rem 追加 PATH 要小心，别把原值弄丢",
			"setx PATH \"%PATH%;C:\\bin\""
		],

		"cmd|timeout": [
			"rem 等 5 秒，按任意键可提前结束",
			"timeout /t 5",
			"",
			"rem 不允许按键跳过",
			"timeout /t 5 /nobreak"
		],

		"cmd|ipconfig /all": [
			"rem 看全部网络配置",
			"ipconfig /all",
			"",
			"rem 只看 IPv4 地址",
			"ipconfig | findstr /i \"IPv4\""
		],

		"cmd|ipconfig /flushdns": [
			"rem 清 DNS 缓存后再验证",
			"ipconfig /flushdns",
			"ping example.com"
		],

		"cmd|ping": [
			"rem 发 4 个包",
			"ping -n 4 1.1.1.1",
			"",
			"rem 持续 ping，Ctrl+C 停",
			"ping -t 192.168.1.1"
		],

		"cmd|tracert": ["rem -d 不解析域名，快一些", "tracert -d example.com"],

		"cmd|nslookup": ["rem 用指定 DNS 查", "nslookup example.com 8.8.8.8"],

		"cmd|netstat -ano": [
			"rem 找 8080 端口占用的 PID",
			"netstat -ano | findstr :8080",
			"",
			"rem 再用 PID 看是哪个程序",
			"tasklist /fi \"pid eq 1234\""
		],

		"cmd|arp -a": [
			"rem 看 ARP 缓存",
			"arp -a",
			"",
			"rem 清 ARP 缓存（要管理员）",
			"netsh interface ip delete arpcache"
		],

		"cmd|route print": [
			"rem 看路由表",
			"route print",
			"",
			"rem 加一条临时路由",
			"route add 10.0.0.0 mask 255.0.0.0 192.168.1.1"
		],

		"cmd|netsh wlan show profiles": ["rem 列出保存过的 Wi-Fi", "netsh wlan show profiles"],

		"cmd|netsh wlan show profile": [
			"rem 看某个 Wi-Fi 的密码（key=clear）",
			"netsh wlan show profile name=\"MyWiFi\" key=clear"
		],

		"cmd|net use": [
			"rem 映射网络驱动器，重启后保留",
			"net use Z: \\\\server\\share /persistent:yes",
			"",
			"rem 断开",
			"net use Z: /delete"
		],

		"cmd|net share": [
			"rem 看本机共享",
			"net share",
			"",
			"rem 建共享",
			"net share data=D:\\data /grant:alice,full"
		],

		"cmd|curl": [
			"rem 只看响应头",
			"curl -I https://example.com",
			"",
			"rem 下载文件",
			"curl -L -o a.zip https://example.com/a.zip"
		],

		"cmd|ssh": ["rem 远程登录", "ssh user@server", "", "rem 直接跑一条命令", "ssh user@server \"df -h\""],

		"cmd|mstsc": ["rem 连远程桌面", "mstsc /v:192.168.1.10"],

		"cmd|telnet": ["rem 测端口通不通（连上就是通）", "telnet 192.168.1.10 80"],

		"cmd|diskpart": [
			"rem 交互式分区，每步都有提示",
			"diskpart",
			"list disk",
			"select disk 2"
		],

		"cmd|chkdsk": [
			"rem 只读检查",
			"chkdsk C:",
			"",
			"rem 修复（系统盘会安排到重启后执行）",
			"chkdsk C: /f /r"
		],

		"cmd|format": [
			"rem 危险：格式化会清空整个分区",
			"vol D:",
			"format D: /fs:NTFS"
		],

		"cmd|defrag": ["rem 机械盘整理；SSD 用 /o 做 TRIM", "defrag C: /o"],

		"cmd|cleanmgr": ["rem 打开磁盘清理", "cleanmgr", "", "rem 直接用预设清理", "cleanmgr /sagerun:1"],

		"cmd|subst": [
			"rem 把目录映射成盘符",
			"subst X: D:\\work\\repo",
			"",
			"rem 取消映射",
			"subst X: /d"
		],

		"cmd|sc query": [
			"rem 看服务状态",
			"sc query Spooler",
			"",
			"rem 列出所有服务名",
			"sc query state= all | findstr SERVICE_NAME"
		],

		"cmd|sc config": [
			"rem start= 后面必须留一个空格",
			"sc config Spooler start= auto",
			"",
			"rem 立即生效",
			"sc start Spooler"
		],

		"cmd|schtasks /create": [
			"rem 每天 2 点跑批处理",
			"schtasks /create /tn Backup /tr \"D:\\backup.bat\" /sc daily /st 02:00",
			"",
			"rem 先手动跑一次验证",
			"schtasks /run /tn Backup"
		],

		"cmd|shutdown /r": ["rem 30 秒后重启", "shutdown /r /t 30", "", "rem 取消", "shutdown /a"],

		"cmd|shutdown /s": ["rem 60 秒后关机", "shutdown /s /t 60", "", "rem 取消", "shutdown /a"],

		"cmd|gpupdate /force": [
			"rem 强制刷新组策略",
			"gpupdate /force",
			"",
			"rem 看应用结果",
			"gpresult /r"
		],

		"cmd|gpresult /r": ["rem 看当前用户和计算机应用的策略", "gpresult /r"],

		"cmd|tar -czf": ["rem Win10 起自带 tar，打包目录", "tar -czf app.tar.gz app"],

		"cmd|tar -xzf": ["rem 先看包里有什么", "tar -tzf app.tar.gz", "", "rem 解压", "tar -xzf app.tar.gz"],

		"cmd|expand": ["rem 解出 cab 里的全部文件", "expand app.cab -F:* C:\\out"],

		"cmd|reg query": [
			"rem 读注册表值",
			"reg query HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion /v ProgramFilesDir"
		],

		"cmd|reg add": [
			"rem 写字符串值，/f 覆盖不提示",
			"reg add HKCU\\Software\\App /v Lang /t REG_SZ /d zh /f"
		],

		"cmd|sfc /scannow": ["rem 扫描并修复系统文件（要管理员）", "sfc /scannow"],

		"cmd|DISM /Online /Cleanup-Image /RestoreHealth": [
			"rem 先检查映像",
			"DISM /Online /Cleanup-Image /CheckHealth",
			"",
			"rem 再修复",
			"DISM /Online /Cleanup-Image /RestoreHealth"
		],

		"cmd|netsh advfirewall firewall add rule": [
			"rem 放行 TCP 8080 入站",
			"netsh advfirewall firewall add rule name=\"web 8080\" dir=in action=allow protocol=TCP localport=8080",
			"",
			"rem 确认规则",
			"netsh advfirewall firewall show rule name=\"web 8080\""
		],

		"cmd|doskey": [
			"rem 定义宏，相当于别名（只对当前窗口有效）",
			"doskey ll=dir /a",
			"",
			"rem 看已定义的宏",
			"doskey /macros"
		],

		"cmd|cmd /c": [
			"rem 执行一条命令后关闭窗口",
			"cmd /c \"dir > out.txt\"",
			"",
			"rem 串两条再查看结果",
			"cmd /c \"ipconfig /all > net.txt\" && type net.txt"
		],

		"cmd|start .": ["rem 用资源管理器打开当前目录", "start ."],

		"cmd|mklink /j": ["rem 目录联接：不需要管理员，跨盘也能用", "mklink /j link C:\\target"],

		"cmd|certutil -encode": [
			"rem 生成 Base64 编码文件",
			"certutil -encode in.bin out.b64",
			"",
			"rem 解码用 -decode",
			"certutil -decode out.b64 in.bin"
		],

		"cmd|powershell -Command": [
			"rem 在 cmd 里执行一条 PowerShell",
			"powershell -NoProfile -Command \"Get-FileHash .\\a.txt\"",
			"",
			"rem 执行脚本文件",
			"powershell -NoProfile -ExecutionPolicy Bypass -File .\\build.ps1"
		],

		"cmd|logoff": ["rem 先看会话 ID", "query user", "", "rem 注销该会话", "logoff 2"],

		"cmd|appwiz.cpl": ["rem 打开程序和功能", "appwiz.cpl"],

		"cmd|wmic process": [
			"rem 已弃用；新系统改用 Get-CimInstance",
			"wmic process where \"name='python.exe'\" get processid,commandline"
		],

		"cmd|wmic logicaldisk": ["rem 看磁盘容量（已弃用）", "wmic logicaldisk get name,freespace,size"],

		"cmd|eventvwr": ["rem 打开事件查看器", "eventvwr.msc"],

		"cmd|git status": ["rem 简短状态", "git status -sb"],

		"cmd|git pull": ["rem 拉取并变基", "git pull --rebase"],

		"cmd|git push": ["rem 首次推送设上游", "git push -u origin main"],

		"cmd|git log": ["rem 一行一条带分支图", "git log --oneline --graph -20"],

		"cmd|git add": ["rem 全部改动加入暂存区", "git add ."],

		"cmd|npx": ["rem 不全局安装，直接跑一个包", "npx serve ."],

		"cmd|dotnet run": ["rem 编译并运行", "dotnet run"],

		"cmd|copy": ["rem /y 不提示覆盖", "copy /y a.txt b.txt"],

		"cmd|sort": ["rem 按行排序", "sort names.txt", "", "rem 去重后输出", "sort /unique names.txt"],

		"cmd|more": ["rem 分页看长输出", "type big.log | more"],

		"cmd|tree": ["rem 树形列出目录，/f 含文件", "tree /f /a"],

		"cmd|fc": ["rem 比较两个文件的差异", "fc a.txt b.txt"],

		"cmd|pushd": ["rem 切换目录并记住", "pushd D:\\work", "popd"],

		"cmd|hostname": ["rem 看计算机名", "hostname"],

		"cmd|ver": ["rem 看 Windows 版本号", "ver"],

		"cmd|driverquery": ["rem 列出已安装的驱动", "driverquery /v"],

		"cmd|powercfg /batteryreport": ["rem 生成电池报告到当前目录", "powercfg /batteryreport"],

		"cmd|ipconfig /release": ["rem 释放并重新获取 IP", "ipconfig /release", "ipconfig /renew"],

		"cmd|pathping": ["rem 路由加丢包统计（比较慢）", "pathping example.com"],

		"cmd|netsh interface show interface": ["rem 看网卡状态", "netsh interface show interface"],

		"cmd|vol": ["rem 看盘符对应的卷标", "vol D:"],

		"cmd|mountvol": ["rem 卸载盘符（不动数据）", "mountvol X: /d"],

		"cmd|cipher /w": ["rem 擦除磁盘空闲空间（很慢）", "cipher /w:D:"],

		"cmd|sc start": ["rem 启动服务", "sc start Spooler"],

		"cmd|sc stop": ["rem 停止服务", "sc stop Spooler"],

		"cmd|net start": ["rem 列出已启动的服务", "net start", "", "rem 启动指定服务", "net start Spooler"],

		"cmd|net stop": ["rem 停止服务", "net stop Spooler"],

		"cmd|schtasks /query": ["rem 看计划任务的详细列表", "schtasks /query /fo LIST /v"],

		"cmd|schtasks /run": ["rem 立即运行计划任务", "schtasks /run /tn Backup"],

		"cmd|schtasks /delete": ["rem 删除计划任务", "schtasks /delete /tn Backup /f"],

		"cmd|shutdown /a": ["rem 取消计划中的关机", "shutdown /a"],

		"cmd|makecab": ["rem 生成 cab 包", "makecab a.txt a.cab"],

		"cmd|compact /c": ["rem 对目录开启 NTFS 压缩", "compact /c /s:D:\\data"],

		"cmd|powershell Compress-Archive": [
			"rem 在 cmd 里用 PowerShell 打 zip",
			"powershell -NoProfile -Command \"Compress-Archive -Path .\\app -DestinationPath app.zip\""
		],

		"cmd|msbuild": ["rem 编译解决方案，Release 配置", "msbuild App.sln /p:Configuration=Release"],

		"cmd|reg delete": ["rem 删除注册表项", "reg delete HKCU\\Software\\App /f"],

		"cmd|netsh advfirewall set allprofiles state off": [
			"rem 临时关闭防火墙排障，记得开回来",
			"netsh advfirewall set allprofiles state off"
		],

		"cmd|netsh advfirewall show allprofiles": ["rem 看防火墙状态", "netsh advfirewall show allprofiles"],

		"cmd|netsh advfirewall firewall delete rule": [
			"rem 按名字删规则",
			"netsh advfirewall firewall delete rule name=\"web 8080\""
		],

		"cmd|cipher /e": ["rem 用 EFS 加密文件", "cipher /e secret.txt"],

		"cmd|auditpol /get /category:*": ["rem 看审核策略", "auditpol /get /category:*"],

		"cmd|help": ["rem 列出内置命令", "help", "", "rem 看某条命令帮助", "xcopy /?"],

		"cmd|命令 /?": ["rem 查看某个命令的帮助", "xcopy /?"],

		"cmd|cls": ["rem 清屏", "cls"],

		"cmd|color": ["rem 黑底绿字", "color 0A"],

		"cmd|title": ["rem 改窗口标题", "title build"],

		"cmd|exit": ["rem 退出当前命令行", "exit"],

		"cmd|git clone": ["rem 克隆并进入目录", "git clone https://github.com/user/repo.git", "cd repo"],

		"cmd|git commit": ["rem 提交并写说明", "git commit -m \"fix: 修正端口解析\""],

		"cmd|python": ["rem 用 UTF-8 跑脚本", "python -X utf8 main.py"],

		"cmd|pip install": ["rem 按 requirements 安装", "pip install -r requirements.txt"],

		"cmd|npm install": ["rem 按 package.json 装依赖", "npm install"],

		"cmd|dotnet build": ["rem Release 编译", "dotnet build -c Release"],

		"cmd|docker run": ["rem 后台跑 nginx，映射 8080", "docker run -d --name web -p 8080:80 nginx"],

		"cmd|docker ps": ["rem 运行中的容器", "docker ps"],

		"cmd|code .": ["rem 用 VS Code 打开当前目录", "code ."],

		"cmd|winget install": ["rem 安装软件", "winget install Git.Git"],

		"cmd|winget upgrade --all": ["rem 升级全部", "winget upgrade --all --include-unknown"],

		"cmd|winget search": ["rem 搜软件", "winget search vscode"],

		"cmd|winget list": ["rem 列出已安装的软件", "winget list"],

		"cmd|choco install": ["rem 安装并自动确认", "choco install git -y"],

		"cmd|scoop install": ["rem 用户级安装", "scoop install ripgrep"],

		"cmd|%VAR%": [
			"rem 赋值时等号两边不能有空格",
			"set LOG=app.log",
			"type %LOG%",
			"",
			"rem 脚本里用 %1 取第一个参数",
			"echo 参数: %1"
		],

		"cmd|%1、%~dp0": [
			"rem %1 第一个参数，%* 全部参数",
			"echo 参数: %1",
			"echo 全部: %*",
			"",
			"rem %~dp0 是脚本所在目录（末尾带反斜杠）",
			"echo 脚本目录: %~dp0"
		],

		"cmd|^": [
			"rem 用 ^ 转义 & | > 这些有特殊含义的符号",
			"echo a ^& b",
			"echo 1 ^> 0"
		],

		"cmd|\"\"": [
			"rem 路径含空格要整体加引号",
			"cd \"C:\\Program Files\"",
			"start \"\" \"C:\\Program Files\\App\\app.exe\""
		],

		"cmd|& 与 && 与 ||": [
			"rem & 依次执行，不管前面成没成",
			"echo a & echo b",
			"",
			"rem && 前面成功才继续，|| 前面失败才执行",
			"copy a.txt b.txt && echo 成功 || echo 失败"
		],

		"cmd|> 与 >> 与 2>&1": [
			"rem > 覆盖，>> 追加",
			"app.exe > out.txt",
			"app.exe >> out.txt",
			"",
			"rem 2>&1 把错误输出并进标准输出",
			"app.exe > out.txt 2>&1"
		],

		"cmd|if exist 与 for": [
			"rem 命令行里循环变量写 %f，批处理里要写 %%f",
			"for %f in (*.log) do echo %f",
			"",
			"rem 文件存在才删除",
			"if exist out.txt del out.txt"
		],

		"cmd|!var!": [
			"rem 循环里要取最新值，先开启延迟展开",
			"setlocal enabledelayedexpansion",
			"for /l %i in (1,1,3) do echo !i!"
		],

		"cmd|rem 与 ::": [
			"rem 这是注释",
			":: 这也是注释，只建议在批处理里用",
			"",
			"rem 行尾 ^ 可以把命令接到下一行",
			"echo a ^",
			"  b"
		],

		"cmd|@echo off": [
			"rem 批处理第一行，关掉命令回显",
			"@echo off",
			"echo 只显示这一行"
		],

		"ps|$var": [
			"# 变量以 $ 开头，未赋值就是 $null",
			"$name = 'alice'",
			"$count = 3",
			"\"$name 有 $count 条\""
		],

		"ps|'...' 与 \"...\"": [
			"# 单引号不展开变量，双引号展开",
			"$n = 'world'",
			"'hi $n'    # hi $n",
			"\"hi $n\"    # hi world"
		],

		"ps|` 反引号": [
			"# 反引号是转义符；`t 制表、`n 换行",
			"Write-Host \"a`tb\"",
			"Write-Host \"第一行`n第二行\"",
			"",
			"# 行尾反引号续行",
			"Get-Process `",
			"    -Name pwsh"
		],

		"ps|| Where-Object": [
			"# 管道里传的是对象，不是文本",
			"Get-Service | Where-Object Status -eq 'Running' |",
			"    Select-Object -First 5 Name"
		],

		"ps|-eq -like -match": [
			"# 比较运算符；c 前缀区分大小写",
			"'abc' -like 'a*'",
			"'abc' -match '^a'",
			"'ABC' -ceq 'abc'    # False",
			"",
			"# 取反用 -not",
			"if (-not ($n -gt 0)) { '非正数' }"
		],

		"ps|@{} 与 @()": [
			"# 哈希表：键值对",
			"$h = @{ name = 'a'; port = 80 }",
			"$h.name",
			"",
			"# 数组",
			"$list = @(1, 2, 3)",
			"$list.Count"
		],

		"ps|& \"路径\"": [
			"# 路径带空格，必须用 & 调用",
			"& 'C:\\Program Files\\App\\app.exe' --version"
		],

		"ps|try/catch/finally": [
			"# 想让 catch 抓到，错误必须是终止错误",
			"try {",
			"    Get-Item .\\nope.txt -ErrorAction Stop",
			"}",
			"catch {",
			"    \"失败: $($_.Exception.Message)\"",
			"}",
			"finally {",
			"    '结束'",
			"}"
		],

		"ps|# 与 <# #>": [
			"# 单行注释",
			"",
			"<#",
			"  多行块注释",
			"  可以写一段说明",
			"#>"
		],

		"ps|.\\script.ps1": [
			"# 运行当前目录的脚本要带 .\\",
			".\\build.ps1",
			"",
			"# 被策略挡住时临时放开",
			"powershell -ExecutionPolicy Bypass -File .\\build.ps1"
		],

		"ps|-Name 值": [
			"# 参数写成 -名 值，支持唯一前缀缩写",
			"Get-ChildItem -Path C:\\ -Recurse -Depth 1",
			"Get-ChildItem -Pa C:\\ -Re -De 1"
		],

		"ps|? :": [
			"# 三元运算符（7.0+）",
			"$n = 5",
			"$s = $n -gt 0 ? '正数' : '非正数'",
			"$s"
		],

		"ps|?? 与 ??=": [
			"# 空合并：左边是 $null 就用右边",
			"$v = $null ?? '默认'",
			"$v",
			"",
			"# 空赋值：变量为 $null 时才赋值",
			"$cfg = $null",
			"$cfg ??= 'config.toml'"
		],

		"ps|&& 与 ||": [
			"# 命令串联，语义和 cmd 相同（7.0+）",
			"Get-Process pwsh && '有 pwsh 在跑' || '没有'"
		],

		"ps|$using:var": [
			"# 并行块里引用外部变量要加 $using:",
			"$n = 10",
			"1..3 | ForEach-Object -Parallel { \"$using:n-$_\" } -ThrottleLimit 3"
		],

		"ps|-AsHashtable": [
			"# JSON 直接转哈希表，取值更顺手",
			"$cfg = Get-Content .\\app.json -Raw | ConvertFrom-Json -AsHashtable",
			"$cfg['port']"
		],

		"ps|--%": [
			"# --% 后面的参数原样交给程序，不再被解析",
			"az --% group list --query \"[].name\""
		],

		"ps|$IsWindows": [
			"# 跨平台脚本里判断系统",
			"if ($IsWindows) { 'Windows' }",
			"elseif ($IsLinux) { 'Linux' }",
			"else { 'macOS' }"
		],

		"ps|#requires -Version 7.0": [
			"# 脚本开头声明最低版本，版本不够会直接报错",
			"#requires -Version 7.0",
			"",
			"$PSVersionTable.PSVersion"
		],

		"ps|Find-PSResource": [
			"# 查找并安装模块（7.2 起自带 PSResourceGet）",
			"Find-PSResource PSReadLine",
			"Install-PSResource PSReadLine -Scope CurrentUser"
		],

		"ps|$ErrorView": [
			"# 错误显示样式，7 默认 ConciseView",
			"$ErrorView = 'ConciseView'",
			"Get-Item .\\nope.txt"
		],

		"ps|$PSNativeCommandArgumentPassing": [
			"# 控制原生命令的参数怎么传，7.3 起默认 Standard",
			"$PSNativeCommandArgumentPassing = 'Standard'",
			"curl.exe --url https://example.com"
		]
	};

	function makeset(id, name, rows, placeholder, defaultPick){
		var all = rows.concat(syntaxrows(id));
		var out = [];
		var i;
		for (i = 0; i < all.length; i++) out.push(R(all[i], id));
		return {
			id: id,
			name: name,
			cols: COLS,
			widths: WIDTHS,
			groups: GROUPS,
			rows: out,
			placeholder: placeholder,
			big: 0,
			sub: 1,
			copy: 0,
			hl: id === "cmd" ? "cmd" : "ps",
			defaultPick: defaultPick,
			sample: function(r){ return r.s; },
			sampleTitle: "多行示例"
		};
	}

	SK.regref("u-wincmd", {
		title: "Windows 常用命令",
		setsLabel: "命令行",
		store: "sk-wincmd-shell",
		sets: [
			makeset("powershell", "Windows PowerShell", PS, "搜索：Get-Process、语法、winget、端口、注册表", "Get-ChildItem"),
			makeset("pwsh", "pwsh", pwshrows(), "搜索：-Parallel、语法、Get-Error、Test-Json、winget", "Get-ChildItem"),
			makeset("cmd", "CMD", CMD, "搜索：taskkill、语法、ipconfig、netstat、sc", "dir")
		]
	});
})();
