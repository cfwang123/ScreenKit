(function(){
	var CATS = {
		syntax: "Bash 语法",
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

	var WIDTHS = ["230px", "240px", ""];

	var GROUPS = [
		{ id: "syntax", name: "Bash 语法" },
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

	// bash 的写法要点：变量、引号、替换、管道、条件、循环、函数、数组。
	var SYNTAX = [
		["syntax", "$VAR", "变量赋值等号两边不能有空格；取值用 $VAR 或 ${VAR}", "name=alice && echo \"$name\""],
		["syntax", "'...' 与 \"...\"", "单引号不展开变量，双引号展开", "echo '$HOME' 与 \"$HOME\""],
		["syntax", "$(命令)", "命令替换，把输出当成值", "now=$(date +%F) && echo $now"],
		["syntax", "| 管道", "把上一条命令的输出交给下一条", "ps aux | grep nginx"],
		["syntax", "> 与 >> 与 2>&1", "覆盖、追加、把错误并进标准输出", "app > out.log 2>&1"],
		["syntax", "&& 与 || 与 ;", "成功才继续、失败才执行、依次执行", "make && ./app || echo fail"],
		["syntax", "\\ 续行", "行尾反斜杠把一条命令接到下一行", "tar -czf app.tgz \\"],
		["syntax", "* ? [] 通配符", "按模式匹配文件名，由 shell 展开", "ls *.log ?.txt [abc]*"],
		["syntax", "<<EOF", "here-doc：把多行文本喂给命令", "cat > app.conf <<EOF"],
		["syntax", "[ ] 与 [[ ]]", "条件判断；[[ ]] 支持正则和 && ||", "if [ -f app.log ]; then echo ok; fi"],
		["syntax", "for 与 while", "循环", "for f in *.log; do echo \"$f\"; done"],
		["syntax", "case ... esac", "多分支匹配", "case $1 in start) echo start ;; *) echo other ;; esac"],
		["syntax", "函数", "定义和调用；$1 是第一个参数", "greet(){ echo \"hi $1\"; } && greet alice"],
		["syntax", "数组", "Bash 数组，下标从 0 开始", "arr=(a b c) && echo ${arr[1]} ${#arr[@]}"],
		["syntax", "${var:-默认}", "变量为空时用默认值", "echo ${PORT:-8080}"],
		["syntax", "set -euo pipefail", "脚本开头：出错即停、变量未定义报错、管道出错也停", "set -euo pipefail"],
		["syntax", "后台运行 & 与 jobs", "命令放后台，jobs 查看", "./app > app.log 2>&1 &"]
	];

	// [分组, 命令, 说明, 常用示例, 备注, 适用标签]
	// 标签留空表示各发行版通用；否则取发行版 tags 的交集。
	var COMMON = [
		["file", "pwd", "显示当前工作目录", "pwd"],
		["file", "ls", "列出目录内容；-l 长格式、-a 含隐藏文件", "ls -lah"],
		["file", "cd", "切换目录；cd - 返回上一个目录", "cd /var/log"],
		["file", "mkdir", "创建目录；-p 自动创建父目录", "mkdir -p ~/app/logs"],
		["file", "rm", "删除文件或目录；-r 递归、-f 强制", "rm -rf build", "rm -rf 不会进回收站，先确认路径。"],
		["file", "cp", "复制文件或目录；-r 递归复制", "cp -r src/ dst/"],
		["file", "mv", "移动或重命名", "mv old.txt new.txt"],
		["file", "touch", "创建空文件或更新时间戳", "touch app.log"],
		["file", "ln", "创建链接；-s 创建符号链接", "ln -s /opt/app/current app"],
		["file", "find", "按名称、时间、大小等条件查找", "find . -name \"*.log\" -mtime +7"],
		["file", "locate", "从数据库快速查路径，先更新数据库", "sudo updatedb && locate nginx.conf"],
		["file", "tree", "树形列出目录；-L 限制深度", "tree -L 2", "多数发行版要额外安装。"],
		["file", "stat", "查看文件大小、权限和时间", "stat app.log"],
		["file", "file", "根据内容判断文件类型", "file unknown.bin"],
		["file", "rmdir", "删除空目录", "rmdir olddir"],
		["file", "realpath", "输出绝对路径，解析符号链接", "realpath ./app/bin"],

		["text", "cat", "查看文件内容", "cat /etc/os-release"],
		["text", "less", "分页查看大文件，/ 搜索、q 退出", "less /var/log/syslog"],
		["text", "head", "查看文件开头，默认 10 行", "head -n 20 app.log"],
		["text", "tail", "查看文件结尾；-f 持续跟踪", "tail -f app.log"],
		["text", "grep", "按模式搜索文本；-r 递归、-n 行号", "grep -rn \"ERROR\" ."],
		["text", "sed", "流式编辑；-i 直接修改文件", "sed -i \"s/old/new/g\" file.txt"],
		["text", "awk", "按列处理文本", "awk '{print $1}' access.log"],
		["text", "cut", "按分隔符截取列", "cut -d: -f1 /etc/passwd"],
		["text", "sort", "排序；-u 去重、-n 按数字", "sort -u names.txt"],
		["text", "uniq", "相邻行去重；-c 计数", "sort names.txt | uniq -c"],
		["text", "wc", "统计行数、单词数、字节数", "wc -l app.log"],
		["text", "tee", "同时写到屏幕和文件", "echo ok | tee result.txt"],
		["text", "xargs", "把标准输入变成命令参数", "find . -name \"*.tmp\" | xargs rm -f"],
		["text", "diff", "比较两个文件的差异", "diff -u old.conf new.conf"],
		["text", "tr", "替换或删除字符", "tr 'a-z' 'A-Z' < names.txt"],
		["text", "paste", "按列合并多个文件", "paste -d, a.txt b.txt"],
		["text", "column", "把文本对齐成列", "mount | column -t"],
		["text", "base64", "Base64 编码/解码；-w0 不换行", "base64 -w0 file.bin > file.b64"],
		["text", "sha256sum", "计算或校验 SHA-256", "sha256sum app.iso"],
		["text", "md5sum", "计算或校验 MD5（仅用于完整性）", "md5sum app.iso"],
		["text", "xxd", "以十六进制查看文件", "xxd -l 64 file.bin"],

		["perm", "chmod", "修改权限；755 可执行、644 普通文件", "chmod 755 run.sh"],
		["perm", "chown", "修改所有者和组；-R 递归", "sudo chown -R www-data:www-data /var/www"],
		["perm", "chgrp", "修改所属组", "sudo chgrp developers app/"],
		["perm", "umask", "查看或设置默认权限掩码", "umask 022"],
		["perm", "getfacl", "查看文件 ACL", "getfacl app/"],
		["perm", "setfacl", "设置文件 ACL", "setfacl -m u:alice:rw app.txt"],
		["perm", "sudo", "以管理员权限执行命令", "sudo apt update"],
		["perm", "sudo -i", "切到 root 的登录 shell", "sudo -i"],
		["perm", "su", "切换用户；- 同时加载环境", "su - root"],
		["perm", "passwd", "修改当前用户密码", "passwd"],
		["perm", "useradd", "新建用户；-m 建家目录、-s 指定 shell", "sudo useradd -m -s /bin/bash alice"],
		["perm", "usermod", "修改用户；-aG 追加组", "sudo usermod -aG wheel alice"],
		["perm", "userdel", "删除用户；-r 同时删家目录", "sudo userdel -r alice"],
		["perm", "groupadd", "新建用户组", "sudo groupadd developers"],
		["perm", "id", "查看用户的 UID、GID 和组", "id alice"],
		["perm", "whoami", "显示当前用户名", "whoami"],
		["perm", "chage -l", "查看密码和账号有效期", "sudo chage -l alice"],
		["perm", "visudo", "安全地编辑 sudoers", "sudo visudo"],

		["proc", "ps aux", "查看所有进程", "ps aux | grep nginx"],
		["proc", "top", "实时查看进程和资源占用", "top"],
		["proc", "htop", "交互式进程查看，需安装 htop", "htop"],
		["proc", "kill", "按 PID 结束进程；-9 强制", "kill -9 12345"],
		["proc", "killall", "按进程名结束", "killall firefox"],
		["proc", "pkill", "按命令行模式结束", "pkill -f \"python app.py\""],
		["proc", "pidof", "按进程名查 PID", "pidof nginx"],
		["proc", "nice", "以指定优先级启动", "nice -n 10 make"],
		["proc", "renice", "调整运行中进程的优先级", "sudo renice -n 5 -p 12345"],
		["proc", "nohup", "退出终端后继续运行", "nohup ./app > app.log 2>&1 &"],
		["proc", "jobs", "查看当前 shell 的后台任务", "jobs -l"],
		["proc", "free -h", "查看内存和交换分区", "free -h"],
		["proc", "uname -a", "查看内核和架构", "uname -a"],
		["proc", "lscpu", "查看 CPU 型号和核心数", "lscpu"],
		["proc", "cat /proc/cpuinfo", "查看 CPU 详细信息", "grep -m1 \"model name\" /proc/cpuinfo"],
		["proc", "uptime", "查看运行时长和平均负载", "uptime"],
		["proc", "vmstat", "查看 CPU、内存、IO 概况", "vmstat 1 5"],
		["proc", "dmesg", "查看内核日志；-T 显示时间", "sudo dmesg -T | tail"],
		["proc", "lsusb", "列出 USB 设备", "lsusb"],
		["proc", "lspci", "列出 PCI 设备", "lspci"],
		["proc", "cat /etc/os-release", "查看发行版名称和版本", "cat /etc/os-release"],

		["net", "ip a", "查看网卡和 IP 地址", "ip a"],
		["net", "ip r", "查看路由表", "ip r"],
		["net", "ip link", "查看网卡状态，up/down 开关", "sudo ip link set eth0 up"],
		["net", "ping", "测试连通性；-c 指定次数", "ping -c 4 1.1.1.1"],
		["net", "ss -tulnp", "查看监听端口和进程", "sudo ss -tulnp"],
		["net", "netstat -tulnp", "旧式端口查看，需 net-tools", "sudo netstat -tulnp"],
		["net", "traceroute", "跟踪到目标的路由", "traceroute example.com"],
		["net", "dig", "DNS 查询；+short 只显示结果", "dig example.com +short"],
		["net", "nslookup", "交互式 DNS 查询", "nslookup example.com"],
		["net", "hostname -I", "查看本机所有 IPv4 地址", "hostname -I"],
		["net", "ssh", "远程登录", "ssh user@server"],
		["net", "ssh-keygen", "生成 SSH 密钥", "ssh-keygen -t ed25519 -C \"me@example.com\""],
		["net", "ssh-copy-id", "把公钥装到远程主机", "ssh-copy-id user@server"],
		["net", "scp", "通过 SSH 复制文件", "scp app.tar.gz user@server:/tmp/"],
		["net", "rsync", "增量同步文件；-avz 常用组合", "rsync -avz src/ user@server:/data/"],
		["net", "wget", "下载文件", "wget https://example.com/file.zip"],
		["net", "curl", "发 HTTP 请求；-I 只看响应头", "curl -I https://example.com"],
		["net", "nmcli device status", "查看 NetworkManager 设备状态", "nmcli device status", "需要 NetworkManager。"],
		["net", "nmtui", "文本界面的网络配置", "nmtui", "需要 NetworkManager。"],
		["net", "sftp", "交互式 SFTP 传输", "sftp user@server"],

		["disk", "df -h", "查看磁盘空间", "df -h"],
		["disk", "du -sh", "查看目录总大小", "du -sh /var/log"],
		["disk", "mount", "挂载设备或镜像", "sudo mount /dev/sdb1 /mnt"],
		["disk", "umount", "卸载挂载点", "sudo umount /mnt"],
		["disk", "findmnt", "查看挂载点和文件系统", "findmnt /"],
		["disk", "lsblk", "树形查看块设备；-f 显示文件系统", "lsblk -f"],
		["disk", "fdisk -l", "查看分区表", "sudo fdisk -l"],
		["disk", "parted -l", "查看分区表（含 GPT）", "sudo parted -l"],
		["disk", "dd", "复制或写镜像；bs 块大小", "sudo dd if=app.iso of=/dev/sdb bs=4M status=progress", "写错 of= 会覆盖整块磁盘，务必确认设备。"],
		["disk", "sync", "把缓存刷到磁盘", "sync"],
		["disk", "fsck", "检查并修复文件系统", "sudo fsck /dev/sdb1"],
		["disk", "blkid", "查看设备 UUID 和类型", "sudo blkid"],
		["disk", "swapon --show", "查看交换分区", "sudo swapon --show"],
		["disk", "smartctl", "查看硬盘 SMART 健康信息", "sudo smartctl -a /dev/sda"],

		["svc", "systemctl status", "查看服务状态", "systemctl status nginx", "", "sysd"],
		["svc", "systemctl start", "启动服务", "sudo systemctl start nginx", "", "sysd"],
		["svc", "systemctl stop", "停止服务", "sudo systemctl stop nginx", "", "sysd"],
		["svc", "systemctl restart", "重启服务", "sudo systemctl restart nginx", "", "sysd"],
		["svc", "systemctl enable", "设置开机自启", "sudo systemctl enable nginx", "", "sysd"],
		["svc", "systemctl disable", "取消开机自启", "sudo systemctl disable nginx", "", "sysd"],
		["svc", "systemctl daemon-reload", "改动 unit 文件后重载", "sudo systemctl daemon-reload", "", "sysd"],
		["svc", "systemctl list-units --failed", "查看启动失败的服务", "systemctl --failed", "", "sysd"],
		["svc", "systemctl list-timers", "查看 systemd 定时器", "systemctl list-timers --all", "", "sysd"],
		["svc", "journalctl -u", "查看某个服务的日志", "sudo journalctl -u nginx -f", "", "sysd"],
		["svc", "journalctl -f", "持续跟踪系统日志", "sudo journalctl -f", "", "sysd"],
		["svc", "journalctl --since", "按时间查日志", "sudo journalctl --since \"1 hour ago\"", "", "sysd"],
		["svc", "hostnamectl", "查看或修改主机名", "sudo hostnamectl set-hostname web01", "", "sysd"],
		["svc", "timedatectl", "查看和设置时间、时区、NTP", "timedatectl", "", "sysd"],
		["svc", "crontab -e", "编辑当前用户的定时任务", "crontab -e"],
		["svc", "crontab -l", "查看当前用户的定时任务", "crontab -l"],
		["svc", "reboot", "重启系统", "sudo reboot"],
		["svc", "shutdown", "定时关机；-h 关机、-r 重启", "sudo shutdown -h +10"],
		["svc", "poweroff", "立即关机", "sudo poweroff"],

		["archive", "tar -czf", "创建 tar.gz 归档", "tar -czf app.tar.gz app/"],
		["archive", "tar -xzf", "解压 tar.gz", "tar -xzf app.tar.gz"],
		["archive", "tar -tf", "查看归档内容", "tar -tf app.tar.gz"],
		["archive", "zip -r", "创建 zip 压缩包", "zip -r app.zip app/"],
		["archive", "unzip", "解压 zip；-d 指定目录", "unzip app.zip -d app/"],
		["archive", "gzip", "压缩文件；-k 保留原文件", "gzip -k app.log"],
		["archive", "gunzip", "解压 .gz", "gunzip app.log.gz"],
		["archive", "xz -d", "解压 .xz", "xz -d app.tar.xz"],
		["archive", "zstd -d", "解压 .zst", "zstd -d app.tar.zst"],
		["archive", "7z a", "创建 7z 压缩包，需 p7zip", "7z a app.7z app/"],
		["archive", "7z x", "解压 7z", "7z x app.7z"],

		["dev", "git clone", "克隆仓库", "git clone https://github.com/user/repo.git"],
		["dev", "git status", "查看工作区状态", "git status -sb"],
		["dev", "git pull", "拉取并合并远端更新", "git pull --rebase"],
		["dev", "git push", "推送提交", "git push origin main"],
		["dev", "git log", "查看提交历史", "git log --oneline --graph -20"],
		["dev", "git diff", "查看未提交的差异", "git diff HEAD"],
		["dev", "git add", "把改动加入暂存区", "git add ."],
		["dev", "git commit", "提交暂存区；-m 写说明", "git commit -m \"fix: update config\""],
		["dev", "git checkout", "切换分支或恢复文件", "git checkout -b feature"],
		["dev", "make", "按 Makefile 构建；-j 并行", "make -j$(nproc)"],
		["dev", "cmake", "生成构建目录", "cmake -S . -B build"],
		["dev", "gcc", "编译 C 程序", "gcc -O2 -o app app.c"],
		["dev", "gdb", "调试程序", "gdb ./app"],
		["dev", "python3 -m venv", "创建 Python 虚拟环境", "python3 -m venv .venv"],
		["dev", "pip install", "安装 Python 依赖", "pip install -r requirements.txt"],
		["dev", "npm install", "安装 Node 依赖", "npm install"],
		["dev", "docker ps", "查看运行中的容器", "docker ps -a"],
		["dev", "docker run", "运行容器；-d 后台、-p 映射端口", "docker run -d -p 8080:80 nginx"],
		["dev", "docker exec", "在容器里执行命令", "docker exec -it web bash"],
		["dev", "code .", "用 VS Code 打开当前目录", "code ."],

		["sec", "ufw status", "查看 ufw 状态和规则", "sudo ufw status verbose", "", "deb"],
		["sec", "ufw allow", "放行端口或服务", "sudo ufw allow 22/tcp", "", "deb"],
		["sec", "ufw deny", "拒绝端口或服务", "sudo ufw deny 3306", "", "deb"],
		["sec", "ufw enable", "启用防火墙", "sudo ufw enable", "", "deb"],
		["sec", "firewall-cmd --state", "查看 firewalld 状态", "sudo firewall-cmd --state", "", "rh suse"],
		["sec", "firewall-cmd --list-all", "查看区域和已放行端口", "sudo firewall-cmd --list-all", "", "rh suse"],
		["sec", "firewall-cmd --add-port", "永久放行端口，需 reload", "sudo firewall-cmd --add-port=8080/tcp --permanent", "", "rh suse"],
		["sec", "firewall-cmd --reload", "重载 firewalld 规则", "sudo firewall-cmd --reload", "", "rh suse"],
		["sec", "getenforce", "查看 SELinux 模式", "getenforce", "", "rh"],
		["sec", "setenforce 0", "临时切到宽容模式", "sudo setenforce 0", "临时生效，重启后恢复 enforcing。", "rh"],
		["sec", "nft list ruleset", "查看 nftables 规则", "sudo nft list ruleset"],
		["sec", "iptables -L", "查看 iptables 规则", "sudo iptables -L -n -v"],
		["sec", "nmap", "扫描端口和服务", "nmap -sV 192.168.1.10"],
		["sec", "tcpdump", "抓包；-i 指定网卡", "sudo tcpdump -i any port 80"],
		["sec", "lsof -i", "查看端口被哪个进程占用", "sudo lsof -i :8080"],
		["sec", "fail2ban-client status", "查看 fail2ban 封禁情况", "sudo fail2ban-client status sshd", "需要安装 fail2ban。", ""],
		["sec", "sudo -l", "查看当前用户可执行的 sudo 命令", "sudo -l"],

		["other", "man", "查看命令手册", "man tar"],
		["other", "man -k", "按关键字搜索手册", "man -k network"],
		["other", "apropos", "按描述搜索命令", "apropos \"copy files\""],
		["other", "which", "查找可执行文件路径", "which python3"],
		["other", "whereis", "查找程序、源码和手册", "whereis nginx", "Alpine 等精简系统需装 man 相关包。", ""],
		["other", "type", "判断命令是别名、内建还是文件", "type ll"],
		["other", "history", "查看命令历史", "history | tail -20"],
		["other", "alias", "设置命令别名", "alias ll='ls -lah'"],
		["other", "env", "查看环境变量", "env | sort"],
		["other", "export", "设置当前 shell 的环境变量", "export PATH=\"$HOME/bin:$PATH\""],
		["other", "watch", "定时重复执行命令", "watch -n 2 \"df -h\""],
		["other", "tmux", "终端复用；new -s 新建会话", "tmux new -s work"],
		["other", "nano", "简单终端编辑器", "nano app.conf"],
		["other", "vim", "常用终端编辑器", "vim app.conf"]
	];

	// 各发行版的差异部分：软件包、防火墙、服务、网络配置、版本信息。
	var DISTS = [
		{
			id: "ubuntu",
			name: "Ubuntu",
			tags: ["deb", "", "sysd"],
			placeholder: "搜索：apt、snap、chmod、端口、日志、压缩",
			rows: [
				["pkg", "sudo apt update", "更新软件包索引", "sudo apt update"],
				["pkg", "sudo apt upgrade", "升级已安装的软件包", "sudo apt upgrade"],
				["pkg", "sudo apt full-upgrade", "升级并处理依赖变化", "sudo apt full-upgrade"],
				["pkg", "sudo apt install", "安装软件包", "sudo apt install nginx"],
				["pkg", "sudo apt remove", "卸载软件包，保留配置", "sudo apt remove nginx"],
				["pkg", "sudo apt purge", "卸载并删除配置", "sudo apt purge nginx"],
				["pkg", "apt search", "搜索软件包", "apt search \"text editor\""],
				["pkg", "apt show", "查看包版本、依赖和说明", "apt show nginx"],
				["pkg", "sudo apt autoremove", "清理不再需要的依赖", "sudo apt autoremove"],
				["pkg", "apt list --upgradable", "列出可升级的包", "apt list --upgradable"],
				["pkg", "sudo dpkg -i", "安装本地 .deb 包", "sudo dpkg -i app.deb"],
				["pkg", "dpkg -l", "列出已安装的 deb 包", "dpkg -l | grep nginx"],
				["pkg", "sudo snap install", "安装 snap 包；--classic 用经典权限", "sudo snap install code --classic"],
				["pkg", "snap list", "列出已安装的 snap", "snap list"],
				["pkg", "sudo add-apt-repository", "添加 PPA 或第三方源", "sudo add-apt-repository ppa:user/ppa"],
				["pkg", "apt-cache policy", "查看包的候选版本和来源", "apt-cache policy nginx"],
				["pkg", "cat /etc/apt/sources.list", "查看软件源（新版本在 sources.list.d）", "cat /etc/apt/sources.list"],
				["pkg", "sudo do-release-upgrade", "升级到下一个发行版", "sudo do-release-upgrade"],
				["pkg", "sudo ubuntu-drivers autoinstall", "自动安装推荐的显卡驱动", "sudo ubuntu-drivers autoinstall"],
				["proc", "lsb_release -a", "查看 Ubuntu 版本代号", "lsb_release -a"],
				["net", "sudo netplan apply", "应用 /etc/netplan 的网络配置", "sudo netplan apply"],
				["net", "cat /etc/netplan/*.yaml", "查看 netplan 配置", "cat /etc/netplan/*.yaml"]
			]
		},
		{
			id: "debian",
			name: "Debian",
			tags: ["deb", "", "sysd"],
			placeholder: "搜索：apt、dpkg、systemctl、端口、日志",
			rows: [
				["pkg", "sudo apt update", "更新软件包索引", "sudo apt update"],
				["pkg", "sudo apt upgrade", "升级已安装的软件包", "sudo apt upgrade"],
				["pkg", "sudo apt full-upgrade", "升级并处理依赖变化", "sudo apt full-upgrade"],
				["pkg", "sudo apt install", "安装软件包", "sudo apt install nginx"],
				["pkg", "sudo apt install ./app.deb", "安装本地 deb 并处理依赖", "sudo apt install ./app.deb"],
				["pkg", "sudo apt remove", "卸载软件包，保留配置", "sudo apt remove nginx"],
				["pkg", "sudo apt purge", "卸载并删除配置", "sudo apt purge nginx"],
				["pkg", "apt search", "搜索软件包", "apt search \"text editor\""],
				["pkg", "apt show", "查看包版本、依赖和说明", "apt show nginx"],
				["pkg", "sudo apt autoremove", "清理不再需要的依赖", "sudo apt autoremove"],
				["pkg", "apt list --upgradable", "列出可升级的包", "apt list --upgradable"],
				["pkg", "sudo apt-mark hold", "锁定包版本，不随升级变化", "sudo apt-mark hold nginx"],
				["pkg", "sudo dpkg -i", "安装本地 .deb 包", "sudo dpkg -i app.deb"],
				["pkg", "dpkg -l", "列出已安装的 deb 包", "dpkg -l | grep nginx"],
				["pkg", "cat /etc/apt/sources.list", "查看软件源", "cat /etc/apt/sources.list"],
				["proc", "cat /etc/debian_version", "查看 Debian 版本号", "cat /etc/debian_version"],
				["net", "cat /etc/network/interfaces", "查看 ifupdown 网络配置", "cat /etc/network/interfaces"],
				["net", "systemctl restart networking", "重载 ifupdown 配置", "sudo systemctl restart networking"]
			]
		},
		{
			id: "deepin",
			name: "Deepin / UOS",
			tags: ["deb", "", "sysd"],
			placeholder: "搜索：apt、dpkg、文件、网络、日志",
			rows: [
				["pkg", "sudo apt update", "更新软件包索引", "sudo apt update"],
				["pkg", "sudo apt upgrade", "升级已安装的软件包", "sudo apt upgrade"],
				["pkg", "sudo apt install", "安装软件包", "sudo apt install nginx"],
				["pkg", "sudo apt install ./app.deb", "安装本地 deb 并处理依赖", "sudo apt install ./app.deb"],
				["pkg", "sudo apt remove", "卸载软件包，保留配置", "sudo apt remove nginx"],
				["pkg", "sudo apt purge", "卸载并删除配置", "sudo apt purge nginx"],
				["pkg", "apt search", "搜索软件包", "apt search nginx"],
				["pkg", "apt show", "查看包版本、依赖和说明", "apt show nginx"],
				["pkg", "sudo apt autoremove", "清理不再需要的依赖", "sudo apt autoremove"],
				["pkg", "sudo dpkg -i", "安装本地 .deb 包", "sudo dpkg -i app.deb"],
				["pkg", "dpkg -l", "列出已安装的 deb 包", "dpkg -l | grep nginx"],
				["proc", "cat /etc/deepin-version", "查看深度系统版本", "cat /etc/deepin-version", "UOS 上是 /etc/uos-version。"],
				["proc", "lsb_release -a", "查看发行版描述", "lsb_release -a"]
			]
		},
		{
			id: "rhel",
			name: "RHEL 系",
			tags: ["rh", "dnf", "rpm", "", "sysd"],
			placeholder: "搜索：dnf、rpm、firewall-cmd、SELinux、日志",
			rows: [
				["pkg", "sudo dnf install", "安装软件包", "sudo dnf install nginx"],
				["pkg", "sudo dnf remove", "卸载软件包", "sudo dnf remove nginx"],
				["pkg", "sudo dnf update", "升级已安装的软件包", "sudo dnf update"],
				["pkg", "sudo dnf search", "搜索软件包", "sudo dnf search nginx"],
				["pkg", "dnf info", "查看包信息", "dnf info nginx"],
				["pkg", "dnf list installed", "列出已安装的包", "dnf list installed | grep nginx"],
				["pkg", "sudo dnf provides", "查文件由哪个包提供", "sudo dnf provides /usr/bin/nginx"],
				["pkg", "sudo dnf history", "查看安装历史，可回滚", "sudo dnf history undo last"],
				["pkg", "sudo dnf makecache", "刷新元数据缓存", "sudo dnf makecache"],
				["pkg", "sudo dnf config-manager --add-repo", "添加软件源", "sudo dnf config-manager --add-repo https://example.com/repo.repo"],
				["pkg", "sudo rpm -ivh", "安装本地 rpm 包", "sudo rpm -ivh app.rpm"],
				["pkg", "rpm -qa", "列出已安装的 rpm 包", "rpm -qa | grep nginx"],
				["pkg", "rpm -qf", "查文件属于哪个包", "rpm -qf /usr/bin/nginx"],
				["pkg", "sudo yum", "老系统上的等价命令", "sudo yum install nginx", "CentOS 7 及更早版本用 yum。"],
				["pkg", "sudo subscription-manager repos", "查看已启用的 RHEL 订阅源", "sudo subscription-manager repos --list-enabled", "仅 RHEL 需要订阅。"],
				["proc", "cat /etc/redhat-release", "查看 RHEL 系版本", "cat /etc/redhat-release"],
				["proc", "rpm -q kernel", "查看已安装的内核包", "rpm -q kernel"],
				["net", "nmcli connection show", "列出网络连接", "nmcli connection show"],
				["net", "nmcli con up", "启用某个连接", "sudo nmcli con up \"Wired connection 1\""],
				["svc", "sudo systemctl status firewalld", "查看防火墙服务状态", "sudo systemctl status firewalld"],
				["svc", "sudo systemctl status NetworkManager", "查看网络服务状态", "sudo systemctl status NetworkManager"]
			]
		},
		{
			id: "fedora",
			name: "Fedora",
			tags: ["rh", "dnf", "rpm", "", "sysd"],
			placeholder: "搜索：dnf、copr、firewall-cmd、SELinux、日志",
			rows: [
				["pkg", "sudo dnf install", "安装软件包", "sudo dnf install nginx"],
				["pkg", "sudo dnf remove", "卸载软件包", "sudo dnf remove nginx"],
				["pkg", "sudo dnf upgrade --refresh", "刷新元数据并升级", "sudo dnf upgrade --refresh"],
				["pkg", "sudo dnf search", "搜索软件包", "sudo dnf search nginx"],
				["pkg", "dnf info", "查看包信息", "dnf info nginx"],
				["pkg", "dnf list installed", "列出已安装的包", "dnf list installed | grep nginx"],
				["pkg", "sudo dnf provides", "查文件由哪个包提供", "sudo dnf provides /usr/bin/nginx"],
				["pkg", "sudo dnf group install", "安装软件组", "sudo dnf group install \"Development Tools\""],
				["pkg", "sudo dnf copr enable", "启用 Copr 第三方源", "sudo dnf copr enable user/project"],
				["pkg", "sudo dnf history", "查看安装历史，可回滚", "sudo dnf history undo last"],
				["pkg", "sudo rpm -ivh", "安装本地 rpm 包", "sudo rpm -ivh app.rpm"],
				["pkg", "rpm -qa", "列出已安装的 rpm 包", "rpm -qa | grep nginx"],
				["pkg", "rpm -qf", "查文件属于哪个包", "rpm -qf /usr/bin/nginx"],
				["proc", "cat /etc/fedora-release", "查看 Fedora 版本", "cat /etc/fedora-release"],
				["proc", "rpm -q kernel", "查看已安装的内核包", "rpm -q kernel"],
				["net", "nmcli connection show", "列出网络连接", "nmcli connection show"],
				["svc", "sudo systemctl status firewalld", "查看防火墙服务状态", "sudo systemctl status firewalld"]
			]
		},
		{
			id: "openeuler",
			name: "openEuler",
			tags: ["rh", "dnf", "rpm", "", "sysd"],
			placeholder: "搜索：dnf、rpm、firewall、日志、压缩",
			rows: [
				["pkg", "sudo dnf install", "安装软件包", "sudo dnf install nginx"],
				["pkg", "sudo dnf remove", "卸载软件包", "sudo dnf remove nginx"],
				["pkg", "sudo dnf update", "升级已安装的软件包", "sudo dnf update"],
				["pkg", "sudo dnf search", "搜索软件包", "sudo dnf search nginx"],
				["pkg", "dnf info", "查看包信息", "dnf info nginx"],
				["pkg", "dnf list installed", "列出已安装的包", "dnf list installed | grep nginx"],
				["pkg", "sudo dnf makecache", "刷新元数据缓存", "sudo dnf makecache"],
				["pkg", "sudo dnf repolist", "查看已配置的软件源", "sudo dnf repolist"],
				["pkg", "sudo rpm -ivh", "安装本地 rpm 包", "sudo rpm -ivh app.rpm"],
				["pkg", "rpm -qa", "列出已安装的 rpm 包", "rpm -qa | grep nginx"],
				["pkg", "rpm -qf", "查文件属于哪个包", "rpm -qf /usr/bin/nginx"],
				["pkg", "sudo yum", "dnf 的兼容命令", "sudo yum install nginx"],
				["proc", "cat /etc/openEuler-release", "查看 openEuler 版本", "cat /etc/openEuler-release"],
				["proc", "rpm -q kernel", "查看已安装的内核包", "rpm -q kernel"],
				["net", "nmcli connection show", "列出网络连接", "nmcli connection show"],
				["svc", "sudo systemctl status firewalld", "查看防火墙服务状态", "sudo systemctl status firewalld"]
			]
		},
		{
			id: "arch",
			name: "Arch",
			tags: ["arch", "pacman", "", "sysd"],
			placeholder: "搜索：pacman、yay、AUR、iwctl、日志",
			rows: [
				["pkg", "sudo pacman -Syu", "同步源并升级全系统", "sudo pacman -Syu"],
				["pkg", "sudo pacman -S", "安装软件包", "sudo pacman -S nginx"],
				["pkg", "sudo pacman -Rns", "卸载包及无用依赖和配置", "sudo pacman -Rns nginx"],
				["pkg", "pacman -Ss", "搜索软件包", "pacman -Ss editor"],
				["pkg", "pacman -Qi", "查看已安装包的信息", "pacman -Qi nginx"],
				["pkg", "pacman -Ql", "列出包安装的文件", "pacman -Ql nginx"],
				["pkg", "pacman -Qo", "查文件属于哪个包", "pacman -Qo /usr/bin/nginx"],
				["pkg", "pacman -Qdt", "列出孤立依赖，可清理", "pacman -Qdt"],
				["pkg", "sudo pacman -Sc", "清理包缓存", "sudo pacman -Sc"],
				["pkg", "sudo pacman -Fy", "更新文件数据库", "sudo pacman -Fy"],
				["pkg", "yay -S", "从 AUR 安装（需 yay）", "yay -S visual-studio-code-bin"],
				["pkg", "paru -S", "AUR 助手 paru 的等价命令", "paru -S google-chrome"],
				["pkg", "cat /etc/pacman.d/mirrorlist", "查看镜像源", "cat /etc/pacman.d/mirrorlist"],
				["proc", "pacman -Q linux", "查看内核包版本", "pacman -Q linux"],
				["net", "nmcli device wifi connect", "连接 Wi-Fi", "nmcli device wifi connect MyWiFi password <密码>"],
				["net", "iwctl station wlan0 connect", "用 iwd 连接 Wi-Fi", "iwctl station wlan0 connect MyWiFi"],
				["net", "systemctl status systemd-networkd", "查看 systemd-networkd 状态", "systemctl status systemd-networkd"]
			]
		},
		{
			id: "opensuse",
			name: "openSUSE",
			tags: ["suse", "zypper", "rpm", "", "sysd"],
			placeholder: "搜索：zypper、YaST、firewall-cmd、日志",
			rows: [
				["pkg", "sudo zypper refresh", "刷新软件源", "sudo zypper refresh"],
				["pkg", "sudo zypper install", "安装软件包", "sudo zypper install nginx"],
				["pkg", "sudo zypper remove", "卸载软件包", "sudo zypper remove nginx"],
				["pkg", "sudo zypper update", "升级已安装的包", "sudo zypper update"],
				["pkg", "sudo zypper dup", "发行版升级或滚动更新", "sudo zypper dup"],
				["pkg", "zypper search", "搜索软件包", "zypper search nginx"],
				["pkg", "zypper info", "查看包信息", "zypper info nginx"],
				["pkg", "zypper repos", "列出软件源", "zypper repos"],
				["pkg", "sudo zypper addrepo", "添加软件源", "sudo zypper addrepo https://example.com/repo repo"],
				["pkg", "rpm -qa", "列出已安装的 rpm 包", "rpm -qa | grep nginx"],
				["pkg", "rpm -qf", "查文件属于哪个包", "rpm -qf /usr/bin/nginx"],
				["pkg", "sudo yast2", "打开 YaST 管理界面", "sudo yast2"],
				["net", "sudo wicked show all", "查看 wicked 网络配置", "sudo wicked show all"],
				["net", "sudo yast2 lan", "YaST 里配置网络", "sudo yast2 lan"],
				["svc", "sudo systemctl status firewalld", "查看防火墙服务状态", "sudo systemctl status firewalld"]
			]
		},
		{
			id: "alpine",
			name: "Alpine",
			tags: ["alpine", "apk", "openrc"],
			placeholder: "搜索：apk、rc-service、busybox、doas、日志",
			rows: [
				["pkg", "sudo apk update", "更新软件包索引", "sudo apk update"],
				["pkg", "sudo apk add", "安装软件包", "sudo apk add nginx"],
				["pkg", "sudo apk add --no-cache", "构建镜像时常用，不留缓存", "sudo apk add --no-cache curl"],
				["pkg", "sudo apk del", "卸载软件包", "sudo apk del nginx"],
				["pkg", "sudo apk upgrade", "升级已安装的包", "sudo apk upgrade"],
				["pkg", "apk search", "搜索软件包", "apk search nginx"],
				["pkg", "apk info", "查看包信息", "apk info nginx"],
				["pkg", "apk info -L", "列出包安装的文件", "apk info -L nginx"],
				["pkg", "apk list --installed", "列出已安装的包", "apk list --installed | grep nginx"],
				["pkg", "sudo apk fix", "修复依赖和损坏的包", "sudo apk fix"],
				["pkg", "cat /etc/apk/repositories", "查看软件源", "cat /etc/apk/repositories"],
				["perm", "doas", "以管理员权限执行（替代 sudo）", "doas apk add curl", "需先配置 /etc/doas.conf。"],
				["perm", "adduser", "交互式新建用户（busybox）", "adduser alice"],
				["perm", "deluser", "删除用户", "deluser alice"],
				["svc", "rc-service", "启停服务", "sudo rc-service nginx restart"],
				["svc", "rc-update add", "设置开机自启", "sudo rc-update add nginx default"],
				["svc", "rc-status", "查看各运行级别的服务状态", "rc-status"],
				["svc", "logread -f", "跟踪系统日志", "logread -f"],
				["proc", "cat /etc/alpine-release", "查看 Alpine 版本", "cat /etc/alpine-release"],
				["net", "setup-interfaces", "交互式配置网卡", "sudo setup-interfaces"],
				["net", "rc-service networking restart", "重载网络配置", "sudo rc-service networking restart"],
				["other", "busybox", "查看 busybox 提供的内建命令", "busybox --list", "Alpine 的基础命令多为 busybox 实现，参数比 GNU 版少。"]
			]
		}
	];

	// 多行示例：数组每一项是一行，常用的写法配 # 注释。
	var SAMP = {
		"sh|$VAR": [
			"# 赋值等号两边不能有空格",
			"name=alice",
			"echo \"$name\"",
			"",
			"# 变量和后面的字连在一起时加花括号",
			"echo \"${name}_log\""
		],

		"sh|'...' 与 \"...\"": [
			"# 单引号原样输出，双引号展开变量",
			"echo '$HOME'",
			"echo \"$HOME\"",
			"",
			"# 想输出双引号本身，用单引号包起来",
			"echo 'say \"hi\"'"
		],

		"sh|$(命令)": [
			"# 命令替换：取命令输出当值",
			"now=$(date +%F)",
			"echo \"$now\"",
			"",
			"# 嵌套也行，注意引号",
			"echo \"剩余 $(df -h / | tail -n 1 | awk '{print $4}')\""
		],

		"sh|| 管道": [
			"# 前一条的输出交给后一条",
			"ps aux | grep '[n]ginx'",
			"",
			"# 多级管道：排序取前 10",
			"ls -S | head -n 10"
		],

		"sh|> 与 >> 与 2>&1": [
			"# > 覆盖，>> 追加",
			"echo hello > out.txt",
			"echo world >> out.txt",
			"",
			"# 标准和错误一起写进日志",
			"./app > app.log 2>&1"
		],

		"sh|&& 与 || 与 ;": [
			"# &&：前面成功才继续",
			"make && ./app",
			"",
			"# ||：前面失败才执行",
			"./app || echo \"启动失败\"",
			"",
			"# ;：不管成败依次执行",
			"date ; whoami"
		],

		"sh|\\ 续行": [
			"# 行尾反斜杠把长命令接到下一行",
			"tar -czf app.tar.gz \\",
			"    --exclude='*.log' \\",
			"    app/",
			"",
			"# 注意反斜杠后面不能再有空格"
		],

		"sh|* ? [] 通配符": [
			"# * 任意多个字符，? 一个字符，[] 任一",
			"ls *.log",
			"ls ?.txt",
			"ls [abc]*",
			"",
			"# 大括号展开（不是通配符）",
			"ls *.{log,txt}"
		],

		"sh|<<EOF": [
			"# here-doc 写多行配置",
			"cat > app.conf <<EOF",
			"port=8080",
			"host=0.0.0.0",
			"EOF",
			"",
			"# 追加并去掉行首缩进",
			"cat >> app.conf <<-'EOF'",
			"    debug=true",
			"EOF"
		],

		"sh|[ ] 与 [[ ]]": [
			"# 文件存在才执行",
			"if [ -f app.log ]; then echo ok; fi",
			"",
			"# [[ ]] 支持正则和 && ||",
			"if [[ \"$name\" =~ ^a ]]; then echo \"a 开头\"; fi"
		],

		"sh|for 与 while": [
			"# 遍历当前目录的 .log",
			"for f in *.log; do",
			"    echo \"$f\"",
			"done",
			"",
			"# while 读文件，逐行处理",
			"while read -r line; do echo \"$line\"; done < list.txt"
		],

		"sh|case ... esac": [
			"# 按第一个参数分支",
			"case \"$1\" in",
			"    start) echo start ;;",
			"    stop)  echo stop ;;",
			"    *)     echo \"用法: $0 start|stop\" ;;",
			"esac"
		],

		"sh|函数": [
			"# 定义函数，$1 是第一个参数",
			"greet(){",
			"    echo \"hi $1\"",
			"}",
			"greet alice"
		],

		"sh|数组": [
			"# 下标从 0 开始，${#arr[@]} 是长度",
			"arr=(a b c)",
			"echo ${arr[1]}",
			"echo ${#arr[@]}",
			"",
			"# 遍历所有元素",
			"for x in \"${arr[@]}\"; do echo \"$x\"; done"
		],

		"sh|${var:-默认}": [
			"# 变量为空时用默认值，原变量不变",
			"echo ${PORT:-8080}",
			"",
			"# := 顺带给变量赋值",
			": ${PORT:=8080}",
			"echo \"$PORT\""
		],

		"sh|set -euo pipefail": [
			"# 脚本开头加上：出错即停、变量未定义报错、管道出错也停",
			"set -euo pipefail",
			"",
			"# 想允许某条命令失败，用 || true",
			"rm -f app.log || true"
		],

		"sh|后台运行 & 与 jobs": [
			"# 放后台，日志自己收好",
			"./app > app.log 2>&1 &",
			"",
			"# 看后台任务和 PID",
			"jobs -l"
		],

		"sh|pwd": ["# 看当前目录的绝对路径", "pwd"],

		"sh|ls": [
			"# 长格式、含隐藏文件、人类可读大小",
			"ls -lah",
			"",
			"# 按修改时间倒序，只看前 10 个",
			"ls -lt | head -n 10"
		],

		"sh|cd": [
			"# cd - 回到上一个目录",
			"cd /var/log",
			"cd -",
			"",
			"# 回到家目录",
			"cd ~"
		],

		"sh|mkdir": ["# 一次建多级目录", "mkdir -p ~/app/logs", "", "# 建完看权限", "ls -ld ~/app"],

		"sh|rm": [
			"# 先列出要删的文件",
			"find . -name '*.tmp' -print",
			"",
			"# 确认后再删；rm -rf 不进回收站",
			"rm -rf ./build"
		],

		"sh|cp": [
			"# 递归复制目录",
			"cp -r src/ dst/",
			"",
			"# -a 保留权限和时间戳",
			"cp -a src/ dst/"
		],

		"sh|mv": ["# 重命名", "mv old.txt new.txt", "", "# 移到别的目录", "mv new.txt /tmp/"],

		"sh|ln": ["# 建软链接（目标用绝对路径更稳）", "ln -s /opt/app/current app", "", "# 看链接指向", "ls -l app"],

		"sh|find": [
			"# 找 7 天前的日志，先只打印",
			"find /var/log -type f -name '*.log' -mtime +7 -print",
			"",
			"# 确认后加 -delete 真正删除",
			"find /var/log -type f -name '*.log' -mtime +7 -delete"
		],

		"sh|stat": [
			"# 看大小、权限和时间",
			"stat app.log",
			"",
			"# 只要权限数字和文件名",
			"stat -c '%a %n' app.log"
		],

		"sh|file": ["# 按内容判断文件类型", "file unknown.bin"],

		"sh|realpath": ["# 输出绝对路径，顺带解析软链接", "realpath ./app/bin"],

		"sh|cat": ["# 看系统版本", "cat /etc/os-release", "", "# 带行号看配置前 20 行", "cat -n app.conf | head -n 20"],

		"sh|less": [
			"# 分页看日志：/ 搜索，q 退出，G 到末尾",
			"less /var/log/syslog",
			"",
			"# 打开就跟踪新内容（等同 tail -f）",
			"less +F /var/log/syslog"
		],

		"sh|head": ["# 看前 20 行", "head -n 20 app.log"],

		"sh|tail": [
			"# 持续跟踪新日志（Ctrl+C 退出）",
			"tail -f app.log",
			"",
			"# 看最后 50 行并继续跟踪",
			"tail -n 50 -f app.log"
		],

		"sh|grep": [
			"# 递归搜源码，显示行号",
			"grep -rn --include='*.cs' 'TODO' .",
			"",
			"# 忽略大小写并排除目录",
			"grep -rni --exclude-dir=bin 'error' ."
		],

		"sh|sed": [
			"# 先预览替换结果（不改文件）",
			"sed 's/old/new/g' file.txt",
			"",
			"# 确认后原地改，-i.bak 留个备份",
			"sed -i.bak 's/old/new/g' file.txt"
		],

		"sh|awk": [
			"# 取第一列",
			"awk '{print $1}' access.log",
			"",
			"# 按逗号分隔并对第二列求和",
			"awk -F, '{s+=$2} END {print s}' data.csv"
		],

		"sh|cut": ["# 按冒号取用户名", "cut -d: -f1 /etc/passwd", "", "# 取第 1 到 3 个字段", "cut -d: -f1-3 /etc/passwd"],

		"sh|sort": ["# 去重并排序", "sort -u names.txt", "", "# 按第 2 列数字倒序", "sort -k2 -nr data.txt"],

		"sh|uniq": ["# 相邻行去重并计数（先 sort）", "sort names.txt | uniq -c | sort -nr | head"],

		"sh|wc": ["# 统计行数", "wc -l app.log", "", "# 统计文件数", "find . -name '*.log' | wc -l"],

		"sh|tee": ["# 同时看结果并写文件", "echo ok | tee result.txt", "", "# 追加写", "echo more | tee -a result.txt"],

		"sh|xargs": [
			"# 文件名带空格时用 -print0 配 -0",
			"find . -name '*.tmp' -print0 | xargs -0 rm -f",
			"",
			"# 每个文件跑一次命令",
			"ls *.log | xargs -n1 gzip"
		],

		"sh|diff": ["# 统一格式比较两个配置", "diff -u old.conf new.conf", "", "# 脚本里只关心有没有差异", "diff -q a b && echo same"],

		"sh|tr": ["# 转大写", "tr 'a-z' 'A-Z' < names.txt", "", "# 去掉 Windows 换行里的回车", "tr -d '\\r' < win.txt > unix.txt"],

		"sh|column": ["# 把输出对齐成列", "mount | column -t"],

		"sh|base64": ["# 编码成单行", "base64 -w0 file.bin > file.b64", "", "# 解码", "base64 -d file.b64 > file.bin"],

		"sh|sha256sum": ["# 算哈希并写进校验文件", "sha256sum app.iso > app.iso.sha256", "", "# 校验", "sha256sum -c app.iso.sha256"],

		"sh|xxd": ["# 看文件头 64 字节", "xxd -l 64 file.bin"],

		"sh|chmod": [
			"# 脚本加可执行",
			"chmod 755 run.sh",
			"",
			"# 目录 755、文件 644",
			"find . -type d -exec chmod 755 {} +",
			"find . -type f -exec chmod 644 {} +"
		],

		"sh|chown": ["# 递归改所有者和组", "sudo chown -R www-data:www-data /var/www"],

		"sh|umask": ["# 看当前掩码", "umask", "", "# 设成 022：新文件 644、目录 755", "umask 022"],

		"sh|setfacl": ["# 给 alice 读写权限", "setfacl -m u:alice:rw app.txt", "", "# 看 ACL", "getfacl app.txt"],

		"sh|sudo": ["# 以 root 执行一条命令", "sudo apt update", "", "# 切到 root 的登录 shell", "sudo -i"],

		"sh|useradd": [
			"# 新建用户并建家目录",
			"sudo useradd -m -s /bin/bash alice",
			"",
			"# 设密码",
			"sudo passwd alice"
		],

		"sh|usermod": ["# 追加到 wheel 组（RHEL 系）", "sudo usermod -aG wheel alice", "", "# 看结果", "id alice"],

		"sh|id": ["# 看用户的 UID、GID 和组", "id alice"],

		"sh|ps aux": [
			"# 找 nginx 进程（中括号避免匹配到自己）",
			"ps aux | grep '[n]ginx'",
			"",
			"# 按内存排序取前 10",
			"ps aux --sort=-%mem | head"
		],

		"sh|kill": ["# 先看这个 PID 是什么", "ps -p 12345 -o pid,cmd", "", "# 优雅结束，不行再 -9", "kill 12345", "kill -9 12345"],

		"sh|pkill": ["# 先看会匹配到谁", "pgrep -af 'python app.py'", "pkill -f 'python app.py'"],

		"sh|nohup": [
			"# 退出终端后继续跑，日志写文件",
			"nohup ./app > app.log 2>&1 &",
			"",
			"# 看 PID 和日志",
			"pgrep -af app",
			"tail -f app.log"
		],

		"sh|free -h": ["# 看内存和交换分区", "free -h"],

		"sh|uname -a": ["# 内核、架构和主机名", "uname -a", "", "# 只看内核版本", "uname -r"],

		"sh|lscpu": ["# CPU 型号、核心数", "lscpu"],

		"sh|uptime": ["# 运行时长和 1/5/15 分钟负载", "uptime"],

		"sh|vmstat": ["# 每秒采样一次，共 5 次", "vmstat 1 5"],

		"sh|dmesg": ["# 看内核日志末尾，带可读时间", "sudo dmesg -T | tail -n 30"],

		"sh|lsblk": ["# 树形看块设备和文件系统", "lsblk -f"],

		"sh|df -h": ["# 磁盘空间，去掉临时文件系统", "df -h -x tmpfs -x devtmpfs"],

		"sh|du -sh": [
			"# 看目录总大小",
			"du -sh /var/log",
			"",
			"# 子目录按大小排序",
			"du -sh /var/log/* | sort -h | tail"
		],

		"sh|mount": ["# 挂载设备到目录", "sudo mount /dev/sdb1 /mnt", "", "# 看挂载情况", "findmnt /mnt"],

		"sh|fdisk -l": ["# 看分区表（要 root）", "sudo fdisk -l"],

		"sh|dd": [
			"# 写镜像到 U 盘：先用 lsblk 确认 of= 是哪个设备！",
			"lsblk",
			"sudo dd if=app.iso of=/dev/sdb bs=4M status=progress",
			"sync"
		],

		"sh|fsck": ["# 检查前先卸载分区", "sudo umount /dev/sdb1", "sudo fsck /dev/sdb1"],

		"sh|blkid": ["# 看设备 UUID 和文件系统类型", "sudo blkid"],

		"sh|smartctl": ["# 看硬盘健康状态", "sudo smartctl -a /dev/sda | grep -i -E 'health|error'"],

		"sh|systemctl status": ["# 服务状态和最近日志", "systemctl status nginx"],

		"sh|systemctl start": ["# 启动并设为开机自启", "sudo systemctl start nginx", "sudo systemctl enable nginx"],

		"sh|systemctl restart": ["# 改完配置重启", "sudo systemctl restart nginx", "systemctl status nginx --no-pager"],

		"sh|systemctl enable": ["# 开机自启", "sudo systemctl enable nginx", "", "# 确认", "systemctl is-enabled nginx"],

		"sh|systemctl daemon-reload": ["# 改过 unit 文件后必须重载", "sudo systemctl daemon-reload", "sudo systemctl restart myapp"],

		"sh|journalctl -u": ["# 持续跟踪某个服务的日志", "sudo journalctl -u nginx -f", "", "# 只看今天的", "sudo journalctl -u nginx --since today"],

		"sh|journalctl -f": ["# 持续跟踪系统日志", "sudo journalctl -f"],

		"sh|crontab -e": [
			"# 每天 2 点备份；cron 里 PATH 很精简，命令写绝对路径",
			"crontab -e",
			"# 0 2 * * * /usr/bin/tar -czf /backup/app.tgz /srv/app"
		],

		"sh|crontab -l": ["# 看当前用户的定时任务", "crontab -l"],

		"sh|shutdown": ["# 10 分钟后关机", "sudo shutdown -h +10", "", "# 取消", "sudo shutdown -c"],

		"sh|tar -czf": [
			"# 打包目录",
			"tar -czf app.tar.gz app/",
			"",
			"# 排除日志和依赖目录",
			"tar -czf app.tar.gz --exclude='*.log' --exclude='node_modules' app/"
		],

		"sh|tar -xzf": [
			"# 先看包里有什么",
			"tar -tzf app.tar.gz | head -n 20",
			"",
			"# 解压到指定目录",
			"mkdir -p app && tar -xzf app.tar.gz -C app"
		],

		"sh|tar -tf": ["# 列出归档内容", "tar -tf app.tar.gz | head -n 20"],

		"sh|zip -r": ["# 打包目录", "zip -r app.zip app/"],

		"sh|unzip": ["# 解压到指定目录", "unzip app.zip -d app/"],

		"sh|gzip": ["# 压缩并保留原文件", "gzip -k app.log"],

		"sh|7z a": ["# 打包成 7z（需 p7zip）", "7z a app.7z app/"],

		"sh|git clone": ["# 克隆并进入目录", "git clone https://github.com/user/repo.git", "cd repo"],

		"sh|git status": ["# 简短状态", "git status -sb"],

		"sh|git pull": ["# 拉取并变基", "git fetch origin", "git pull --rebase"],

		"sh|git push": ["# 首次推送设上游", "git push -u origin main"],

		"sh|git log": ["# 一行一条带分支图", "git log --oneline --graph --decorate -20"],

		"sh|git diff": ["# 看未提交的改动", "git diff HEAD", "", "# 只看改了哪些文件", "git diff --name-only"],

		"sh|git commit": [
			"# 提交并写说明",
			"git commit -m \"fix: 修正端口解析\"",
			"",
			"# 还没推送时，追加到上一次提交",
			"git commit --amend --no-edit"
		],

		"sh|git checkout": ["# 新建并切到分支", "git checkout -b feature", "", "# 恢复某个文件到 HEAD", "git checkout -- app.conf"],

		"sh|make": ["# 按 CPU 核数并行编译", "make -j$(nproc)"],

		"sh|cmake": ["# 生成构建目录并编译", "cmake -S . -B build -DCMAKE_BUILD_TYPE=Release", "cmake --build build -j$(nproc)"],

		"sh|gcc": ["# 开 O2 优化并打开警告", "gcc -O2 -Wall -o app app.c"],

		"sh|gdb": ["# 带调试信息编译后再调试", "gcc -g -o app app.c", "gdb ./app"],

		"sh|python3 -m venv": [
			"# 创建并激活虚拟环境",
			"python3 -m venv .venv",
			"source .venv/bin/activate",
			"",
			"# 退出",
			"deactivate"
		],

		"sh|pip install": ["# 按 requirements 安装", "pip install -r requirements.txt", "", "# 装到当前用户", "pip install --user requests"],

		"sh|docker ps": [
			"# 运行中的容器",
			"docker ps",
			"",
			"# 连停止的一起看",
			"docker ps -a --format 'table {{.Names}}\\t{{.Status}}\\t{{.Ports}}'"
		],

		"sh|docker run": [
			"# 后台跑 nginx，映射 8080",
			"docker run -d --name web -p 8080:80 nginx",
			"",
			"# 挂配置目录并自动重启",
			"docker run -d --name web -p 8080:80 -v \"$PWD/conf:/etc/nginx/conf.d\" --restart unless-stopped nginx"
		],

		"sh|ufw status": ["# 看状态和规则", "sudo ufw status verbose"],

		"sh|ufw allow": ["# 放行 SSH 和 8080", "sudo ufw allow 22/tcp", "sudo ufw allow 8080/tcp", "", "# 看规则编号", "sudo ufw status numbered"],

		"sh|ufw enable": ["# 启用前先放行 SSH，避免把自己关在外面", "sudo ufw allow 22/tcp", "sudo ufw enable"],

		"sh|firewall-cmd --add-port": ["# 永久放行 8080，然后重载", "sudo firewall-cmd --add-port=8080/tcp --permanent", "sudo firewall-cmd --reload"],

		"sh|firewall-cmd --list-all": ["# 看当前区域的规则", "sudo firewall-cmd --list-all"],

		"sh|getenforce": ["# 看 SELinux 模式", "getenforce", "", "# 看最近被拒绝的记录", "sudo ausearch -m avc -ts recent"],

		"sh|setenforce 0": ["# 临时切宽容模式，重启恢复", "sudo setenforce 0", "getenforce"],

		"sh|iptables -L": ["# 看规则，带行号和包计数", "sudo iptables -L -n -v --line-numbers"],

		"sh|nmap": ["# 扫端口和服务版本", "nmap -sV 192.168.1.10", "", "# 只看开放端口", "nmap -p- --open 192.168.1.10"],

		"sh|tcpdump": ["# 抓 80 端口的包，先看 20 个", "sudo tcpdump -i any -nn port 80 -c 20"],

		"sh|lsof -i": ["# 看 8080 被哪个进程占用", "sudo lsof -i :8080"],

		"sh|sudo -l": ["# 看自己能 sudo 哪些命令", "sudo -l"],

		"sh|man": ["# 看手册，q 退出", "man tar", "", "# 直接定位到示例段", "man tar | grep -A 20 EXAMPLES"],

		"sh|which": ["# 查可执行文件路径", "which python3"],

		"sh|type": ["# 判断是别名、内建还是文件", "type ll"],

		"sh|history": ["# 看最近 20 条命令", "history | tail -n 20"],

		"sh|alias": ["# 设别名，写进 ~/.bashrc 才能长期生效", "alias ll='ls -lah'", "ll"],

		"sh|env": ["# 看所有环境变量", "env | sort"],

		"sh|export": [
			"# 只在当前 shell 生效",
			"export PATH=\"$HOME/bin:$PATH\"",
			"",
			"# 长期生效写进配置文件",
			"echo 'export PATH=\"$HOME/bin:$PATH\"' >> ~/.bashrc"
		],

		"sh|watch": ["# 每 2 秒刷新一次磁盘空间", "watch -n 2 \"df -h\""],

		"sh|tmux": ["# 新建会话，断线后还能接回来", "tmux new -s work", "", "# 下次接回", "tmux attach -t work"],

		"sh|vim": ["# 打开配置：i 编辑，Esc 后 :wq 保存退出", "vim app.conf"],

		"sh|nano": ["# 简单编辑器：Ctrl+O 保存，Ctrl+X 退出", "nano app.conf"],

		"sh|ping": ["# 发 4 个包", "ping -c 4 1.1.1.1"],

		"sh|ip a": ["# 看网卡和 IP", "ip a", "", "# 只看某个网卡", "ip a show eth0"],

		"sh|ip r": ["# 看路由表", "ip r"],

		"sh|ss -tulnp": ["# 看监听端口和进程", "sudo ss -tulnp", "", "# 只看 8080", "sudo ss -tulnp | grep :8080"],

		"sh|ssh": ["# 远程登录", "ssh user@server", "", "# 直接跑一条命令", "ssh user@server 'df -h'"],

		"sh|ssh-keygen": ["# 生成 ed25519 密钥（一路回车）", "ssh-keygen -t ed25519 -C \"me@example.com\""],

		"sh|scp": ["# 上传文件", "scp app.tar.gz user@server:/tmp/", "", "# 下载目录", "scp -r user@server:/data/logs ./"],

		"sh|rsync": ["# 增量同步目录，末尾斜杠别丢", "rsync -avz src/ user@server:/data/", "", "# 先演练，不实际改动", "rsync -avzn src/ user@server:/data/"],

		"sh|wget": ["# 下载文件", "wget https://example.com/file.zip"],

		"sh|curl": ["# 只看响应头", "curl -I https://example.com", "", "# 下载并跟随跳转", "curl -L -o a.zip https://example.com/a.zip"],

		"sh|dig": ["# 只显示结果", "dig example.com +short", "", "# 指定 DNS 查", "dig @8.8.8.8 example.com +short"],

		"sh|nmcli device status": ["# 看网络设备状态", "nmcli device status"],

		"sh|sudo -i": ["# 切到 root 的登录 shell", "sudo -i"],

		"sh|ufw deny": ["# 拒绝 3306 入站", "sudo ufw deny 3306"],

		"sh|nft list ruleset": ["# 看 nftables 规则", "sudo nft list ruleset"],

		"sh|fail2ban-client status": ["# 看 sshd 的封禁情况", "sudo fail2ban-client status sshd"],

		"sh|sudo apt full-upgrade": ["# 升级并处理依赖变化", "sudo apt full-upgrade -y"],

		"sh|snap list": ["# 已安装的 snap", "snap list"],

		"sh|apt-cache policy": ["# 看候选版本和来源", "apt-cache policy nginx"],

		"sh|sudo ubuntu-drivers autoinstall": ["# 自动装推荐的显卡驱动，装完重启", "sudo ubuntu-drivers autoinstall"],

		"sh|cat /etc/netplan/*.yaml": ["# 看 netplan 配置", "cat /etc/netplan/*.yaml"],

		"sh|sudo apt-mark hold": ["# 锁定版本，不跟着升级", "sudo apt-mark hold nginx", "", "# 解锁", "sudo apt-mark unhold nginx"],

		"sh|cat /etc/debian_version": ["# 看 Debian 版本号", "cat /etc/debian_version"],

		"sh|cat /etc/network/interfaces": ["# 看 ifupdown 网络配置", "cat /etc/network/interfaces"],

		"sh|systemctl restart networking": ["# 重载 ifupdown 配置", "sudo systemctl restart networking"],

		"sh|cat /etc/deepin-version": ["# 看深度系统版本（UOS 是 /etc/uos-version）", "cat /etc/deepin-version"],

		"sh|firewall-cmd --state": ["# 看 firewalld 是否在跑", "sudo firewall-cmd --state"],

		"sh|firewall-cmd --reload": ["# 改完规则重载", "sudo firewall-cmd --reload"],

		"sh|sudo dnf search": ["# 搜索软件包", "sudo dnf search nginx"],

		"sh|dnf info": ["# 看包信息", "dnf info nginx"],

		"sh|dnf list installed": ["# 看包是否已安装", "dnf list installed | grep nginx"],

		"sh|sudo dnf provides": ["# 查文件由哪个包提供", "sudo dnf provides /usr/bin/nginx"],

		"sh|sudo dnf makecache": ["# 刷新元数据缓存", "sudo dnf makecache"],

		"sh|sudo dnf repolist": ["# 看已配置的软件源", "sudo dnf repolist"],

		"sh|sudo dnf group install": ["# 安装开发工具组", "sudo dnf group install \"Development Tools\""],

		"sh|sudo dnf copr enable": ["# 启用 Copr 源再安装", "sudo dnf copr enable user/project", "sudo dnf install -y app"],

		"sh|sudo rpm -ivh": ["# 安装本地 rpm（缺依赖会报错）", "sudo rpm -ivh app.rpm"],

		"sh|sudo subscription-manager repos": ["# RHEL 看已启用的订阅源", "sudo subscription-manager repos --list-enabled"],

		"sh|rpm -q kernel": ["# 看已安装的内核包", "rpm -q kernel"],

		"sh|nmcli connection show": ["# 列出网络连接", "nmcli connection show"],

		"sh|nmcli con up": ["# 启用某个连接", "sudo nmcli con up \"Wired connection 1\""],

		"sh|sudo systemctl status firewalld": ["# 看防火墙服务状态", "sudo systemctl status firewalld"],

		"sh|sudo systemctl status NetworkManager": ["# 看网络服务状态", "sudo systemctl status NetworkManager"],

		"sh|cat /etc/fedora-release": ["# 看 Fedora 版本", "cat /etc/fedora-release"],

		"sh|cat /etc/openEuler-release": ["# 看 openEuler 版本", "cat /etc/openEuler-release"],

		"sh|pacman -Qi": ["# 看已安装包的信息", "pacman -Qi nginx"],

		"sh|pacman -Ql": ["# 列出包安装的文件", "pacman -Ql nginx"],

		"sh|pacman -Qdt": ["# 列出孤立依赖，确认后可清理", "pacman -Qdt"],

		"sh|sudo pacman -Sc": ["# 清理包缓存", "sudo pacman -Sc"],

		"sh|sudo pacman -Fy": ["# 更新文件数据库", "sudo pacman -Fy"],

		"sh|paru -S": ["# 用 paru 从 AUR 安装", "paru -S google-chrome"],

		"sh|cat /etc/pacman.d/mirrorlist": ["# 看镜像源", "cat /etc/pacman.d/mirrorlist"],

		"sh|pacman -Q linux": ["# 看内核包版本", "pacman -Q linux"],

		"sh|iwctl station wlan0 connect": ["# 用 iwd 连 Wi-Fi", "iwctl station wlan0 connect MyWiFi"],

		"sh|systemctl status systemd-networkd": ["# 看 systemd-networkd 状态", "systemctl status systemd-networkd"],

		"sh|sudo zypper remove": ["# 卸载软件包", "sudo zypper remove nginx"],

		"sh|sudo zypper update": ["# 升级已安装的包", "sudo zypper update"],

		"sh|sudo zypper dup": ["# 滚动更新 / 发行版升级", "sudo zypper dup"],

		"sh|zypper search": ["# 搜索软件包", "zypper search nginx"],

		"sh|zypper info": ["# 看包信息", "zypper info nginx"],

		"sh|zypper repos": ["# 列出软件源", "zypper repos"],

		"sh|sudo zypper addrepo": ["# 添加软件源并刷新", "sudo zypper addrepo https://example.com/repo repo", "sudo zypper refresh"],

		"sh|sudo yast2": ["# 打开 YaST 管理界面", "sudo yast2"],

		"sh|sudo wicked show all": ["# 看 wicked 网络配置", "sudo wicked show all"],

		"sh|sudo yast2 lan": ["# YaST 里配网络", "sudo yast2 lan"],

		"sh|sudo apk update": ["# 更新软件包索引", "sudo apk update"],

		"sh|sudo apk add --no-cache": ["# 构建镜像时常用，不留缓存", "sudo apk add --no-cache curl"],

		"sh|sudo apk del": ["# 卸载软件包", "sudo apk del nginx"],

		"sh|sudo apk upgrade": ["# 升级已安装的包", "sudo apk upgrade"],

		"sh|apk search": ["# 搜索软件包", "apk search nginx"],

		"sh|apk info": ["# 看包信息", "apk info nginx"],

		"sh|apk info -L": ["# 列出包安装的文件", "apk info -L nginx"],

		"sh|apk list --installed": ["# 列出已安装的包", "apk list --installed | grep nginx"],

		"sh|sudo apk fix": ["# 修复依赖和损坏的包", "sudo apk fix"],

		"sh|cat /etc/apk/repositories": ["# 看软件源", "cat /etc/apk/repositories"],

		"sh|doas": ["# 以管理员权限执行（需先配 /etc/doas.conf）", "doas apk add curl"],

		"sh|adduser": ["# 交互式新建用户（busybox）", "adduser alice"],

		"sh|deluser": ["# 删除用户", "deluser alice"],

		"sh|rc-status": ["# 看各运行级别的服务状态", "rc-status"],

		"sh|logread -f": ["# 跟踪系统日志", "logread -f"],

		"sh|cat /etc/alpine-release": ["# 看 Alpine 版本", "cat /etc/alpine-release"],

		"sh|setup-interfaces": ["# 交互式配置网卡", "sudo setup-interfaces"],

		"sh|rc-service networking restart": ["# 重载网络配置", "sudo rc-service networking restart"],

		"sh|busybox": ["# 看 busybox 的内建命令；参数比 GNU 版少", "busybox --list"],

		"sh|touch": ["# 建空文件；已存在就更新时间戳", "touch app.log", "", "# 一次建多个", "touch a.txt b.txt"],

		"sh|top": ["# 实时看 CPU 和内存占用，q 退出", "top"],

		"sh|htop": ["# 交互式进程查看（需安装），F6 切换排序", "htop"],

		"sh|killall": ["# 按名字结束所有实例", "killall firefox"],

		"sh|pidof": ["# 按名字查 PID", "pidof nginx"],

		"sh|nice": ["# 以较低优先级启动编译", "nice -n 10 make -j$(nproc)"],

		"sh|renice": ["# 调整运行中进程的优先级", "sudo renice -n 5 -p 12345"],

		"sh|jobs": ["# 看当前 shell 的后台任务", "jobs -l"],

		"sh|su": ["# 切到 root 并加载环境", "su -"],

		"sh|passwd": ["# 改自己的密码", "passwd", "", "# 管理员改别人的", "sudo passwd alice"],

		"sh|userdel": ["# 删用户并删除家目录", "sudo userdel -r alice"],

		"sh|groupadd": ["# 建组并查看", "sudo groupadd developers", "getent group developers"],

		"sh|whoami": ["# 当前用户", "whoami", "", "# 当前用户的组", "id -nG"],

		"sh|getfacl": ["# 看文件的 ACL", "getfacl app.txt"],

		"sh|chgrp": ["# 递归改组", "sudo chgrp -R developers app/"],

		"sh|ip link": ["# 看网卡状态", "ip link", "", "# 启停网卡", "sudo ip link set eth0 up"],

		"sh|netstat -tulnp": ["# 旧式端口查看（需 net-tools）", "sudo netstat -tulnp | grep :8080"],

		"sh|traceroute": ["# 跟踪到目标的路由，-n 不解析域名", "traceroute -n example.com"],

		"sh|nslookup": ["# 交互式 DNS 查询", "nslookup example.com 8.8.8.8"],

		"sh|hostname -I": ["# 看本机所有 IPv4 地址", "hostname -I"],

		"sh|ssh-copy-id": ["# 把公钥装到远程主机，之后免密登录", "ssh-copy-id user@server"],

		"sh|umount": ["# 卸载挂载点", "sudo umount /mnt", "", "# 占用时先看是谁", "sudo lsof +D /mnt"],

		"sh|findmnt": ["# 看某个目录挂的是哪个设备", "findmnt /", "", "# 只列真实挂载点", "findmnt --real"],

		"sh|sync": ["# 拔盘、关机前把缓存刷到磁盘", "sync"],

		"sh|lsusb": ["# 列出 USB 设备", "lsusb"],

		"sh|lspci": ["# 列出 PCI 设备", "lspci", "", "# 只看显卡", "lspci | grep -i vga"],

		"sh|cat /etc/os-release": ["# 看发行版名称和版本", "cat /etc/os-release"],

		"sh|systemctl stop": ["# 停止服务", "sudo systemctl stop nginx", "systemctl status nginx --no-pager"],

		"sh|systemctl disable": ["# 取消开机自启", "sudo systemctl disable nginx"],

		"sh|systemctl list-units --failed": ["# 看启动失败的服务", "systemctl --failed"],

		"sh|systemctl list-timers": ["# 看 systemd 定时器", "systemctl list-timers --all"],

		"sh|journalctl --since": [
			"# 查最近一小时的日志",
			"sudo journalctl --since '1 hour ago'",
			"",
			"# 查某个时间段",
			"sudo journalctl --since '2026-10-01' --until '2026-10-02'"
		],

		"sh|hostnamectl": ["# 看主机名和系统信息", "hostnamectl", "", "# 改主机名", "sudo hostnamectl set-hostname web01"],

		"sh|timedatectl": [
			"# 看时区和 NTP 状态",
			"timedatectl",
			"",
			"# 设时区",
			"sudo timedatectl set-timezone Asia/Shanghai"
		],

		"sh|reboot": ["# 重启（先确认没有人在用）", "sudo reboot"],

		"sh|poweroff": ["# 关机", "sudo poweroff"],

		"sh|gunzip": ["# 解压 .gz", "gunzip app.log.gz"],

		"sh|xz -d": ["# 解压 .xz", "xz -d app.tar.xz"],

		"sh|zstd -d": ["# 解压 .zst", "zstd -d app.tar.zst"],

		"sh|7z x": ["# 解压 7z", "7z x app.7z"],

		"sh|git add": ["# 全部改动加入暂存区", "git add .", "git status -sb"],

		"sh|npm install": ["# 按 package.json 装依赖", "npm install", "", "# 装开发依赖", "npm install -D typescript"],

		"sh|docker exec": ["# 进容器开 bash", "docker exec -it web bash", "", "# 只跑一条命令", "docker exec web nginx -t"],

		"sh|code .": ["# 用 VS Code 打开当前目录", "code ."],

		"sh|man -k": ["# 按关键字搜手册", "man -k network"],

		"sh|apropos": ["# 按描述搜命令", "apropos 'copy files'"],

		"sh|whereis": ["# 查程序、源码和手册位置", "whereis nginx"],

		"sh|locate": ["# 先更新数据库再查路径", "sudo updatedb", "locate nginx.conf"],

		"sh|tree": ["# 树形列出两层目录", "tree -L 2"],

		"sh|rmdir": ["# 删除空目录", "rmdir olddir"],

		"sh|paste": ["# 按列合并两个文件", "paste -d, a.txt b.txt"],

		"sh|md5sum": ["# 算 MD5（只用于完整性校验）", "md5sum app.iso"],

		"sh|visudo": ["# 安全地编辑 sudoers，保存前会检查语法", "sudo visudo"],

		"sh|chage -l": ["# 看密码和账号有效期", "sudo chage -l alice"],

		"sh|nmtui": ["# 文本界面的网络配置", "sudo nmtui"],

		"sh|sftp": ["# 交互式传输：put 上传，get 下载", "sftp user@server"],

		"sh|parted -l": ["# 看分区表（含 GPT）", "sudo parted -l"],

		"sh|swapon --show": ["# 看交换分区", "sudo swapon --show"],

		"sh|cat /proc/cpuinfo": ["# 看 CPU 型号", "grep -m1 'model name' /proc/cpuinfo"],

		"sh|sudo apt update": ["# 更新索引后再升级", "sudo apt update", "sudo apt upgrade -y"],

		"sh|sudo apt upgrade": ["# 升级已安装的包", "sudo apt upgrade -y"],

		"sh|sudo apt install": [
			"# 安装并自动确认",
			"sudo apt install -y nginx",
			"",
			"# 装本地 deb 并自动处理依赖",
			"sudo apt install ./app.deb"
		],

		"sh|sudo apt install ./app.deb": ["# 装本地 deb 并处理依赖", "sudo apt install ./app.deb"],

		"sh|sudo apt remove": ["# 卸载但保留配置", "sudo apt remove nginx", "", "# 连配置一起删", "sudo apt purge nginx"],

		"sh|sudo apt purge": ["# 卸载并删除配置", "sudo apt purge nginx"],

		"sh|sudo apt autoremove": ["# 清掉不再需要的依赖", "sudo apt autoremove -y", "sudo apt clean"],

		"sh|apt list --upgradable": ["# 看哪些包能升级", "apt list --upgradable"],

		"sh|apt search": ["# 搜索软件包", "apt search \"text editor\" | head -n 20"],

		"sh|apt show": ["# 看版本、依赖和说明", "apt show nginx"],

		"sh|dpkg -l": ["# 看某个包是否安装", "dpkg -l | grep nginx"],

		"sh|sudo dpkg -i": ["# 装本地 deb，缺依赖时用 apt -f 补", "sudo dpkg -i app.deb", "sudo apt -f install"],

		"sh|sudo snap install": ["# 装 VS Code（经典权限）", "sudo snap install code --classic"],

		"sh|sudo add-apt-repository": ["# 添加 PPA 并安装", "sudo add-apt-repository ppa:user/ppa", "sudo apt update"],

		"sh|sudo do-release-upgrade": ["# 升级到下一个发行版（先备份）", "sudo do-release-upgrade"],

		"sh|cat /etc/apt/sources.list": ["# 看软件源", "cat /etc/apt/sources.list", "", "# 新版本源在 sources.list.d 里", "ls /etc/apt/sources.list.d/"],

		"sh|lsb_release -a": ["# 看发行版版本代号", "lsb_release -a"],

		"sh|sudo netplan apply": ["# 改完 /etc/netplan 先试，确认不断网再应用", "sudo netplan try", "sudo netplan apply"],

		"sh|sudo dnf install": ["# 安装软件包", "sudo dnf install -y nginx"],

		"sh|sudo dnf remove": ["# 卸载软件包", "sudo dnf remove nginx"],

		"sh|sudo dnf update": ["# 升级已安装的包", "sudo dnf update -y"],

		"sh|sudo dnf upgrade --refresh": ["# 刷新元数据并升级", "sudo dnf upgrade --refresh -y"],

		"sh|sudo dnf history": ["# 看安装历史", "sudo dnf history", "", "# 回滚上一次操作", "sudo dnf history undo last"],

		"sh|sudo dnf config-manager --add-repo": [
			"# 添加第三方源并安装",
			"sudo dnf config-manager --add-repo https://example.com/repo.repo",
			"sudo dnf install -y app"
		],

		"sh|rpm -qa": ["# 看包是否安装", "rpm -qa | grep nginx"],

		"sh|rpm -qf": ["# 查文件属于哪个包", "rpm -qf /usr/bin/nginx"],

		"sh|sudo yum": ["# 老系统（CentOS 7）上的等价写法", "sudo yum install -y nginx"],

		"sh|cat /etc/redhat-release": ["# 看 RHEL 系版本", "cat /etc/redhat-release"],

		"sh|sudo pacman -Syu": ["# 同步源并升级全系统（Arch 要常做）", "sudo pacman -Syu"],

		"sh|sudo pacman -S": ["# 安装软件包", "sudo pacman -S nginx"],

		"sh|sudo pacman -Rns": ["# 卸载包、无用依赖和配置", "sudo pacman -Rns nginx"],

		"sh|pacman -Ss": ["# 搜索软件包", "pacman -Ss editor"],

		"sh|pacman -Qo": ["# 查文件属于哪个包", "pacman -Qo /usr/bin/nginx"],

		"sh|yay -S": ["# 从 AUR 安装（需先装 yay）", "yay -S visual-studio-code-bin"],

		"sh|sudo zypper install": ["# openSUSE 安装", "sudo zypper install nginx"],

		"sh|sudo zypper refresh": ["# 刷新软件源", "sudo zypper refresh"],

		"sh|sudo apk add": ["# Alpine 安装；构建镜像常加 --no-cache", "sudo apk add --no-cache curl"],

		"sh|rc-service": ["# Alpine 启停服务", "sudo rc-service nginx restart"],

		"sh|rc-update add": ["# 设置开机自启", "sudo rc-update add nginx default"],

		"sh|nmcli device wifi connect": ["# 连接 Wi-Fi", "nmcli device wifi connect MyWiFi password '密码'"]
	};

	function R(a){
		var g = a[0];
		var cmd = a[1];
		var note = a[4] || "—";
		var base = cmd.split(" ")[0];
		if (base === "sudo" || base === "doas") base = cmd.split(" ")[1] || base;
		var body = (SAMP["sh|" + cmd] || (g === "syntax"
			? ["# " + a[2], a[3]]
			: ["# " + a[2], a[3], "", "# 看完整参数", "man " + base])).join("\n");
		return {
			g: g,
			c: [cmd, a[2], a[3]],
			x: (CATS[g] || "") + " " + a[2] + " " + a[3] + " " + note + " " + body.replace(/\n/g, " "),
			d: [["类别", CATS[g] || ""], ["备注", note]],
			s: body
		};
	}

	function has(d, tag){
		if (!tag) return true;
		var want = tag.split(" ");
		var i;
		for (i = 0; i < want.length; i++) if (d.tags.indexOf(want[i]) >= 0) return true;
		return false;
	}

	function build(d){
		var out = [];
		var i;
		for (i = 0; i < SYNTAX.length; i++) out.push(R(SYNTAX[i]));
		for (i = 0; i < COMMON.length; i++) if (has(d, COMMON[i][5] || "")) out.push(R(COMMON[i]));
		for (i = 0; i < d.rows.length; i++) out.push(R(d.rows[i]));
		return out;
	}

	function makeset(d){
		return {
			id: d.id,
			name: d.name,
			cols: COLS,
			widths: WIDTHS,
			groups: GROUPS,
			rows: build(d),
			placeholder: d.placeholder,
			big: 0,
			sub: 1,
			copy: 0,
			hl: "sh",
			defaultPick: "ls",
			sample: function(r){ return r.s; },
			sampleTitle: "多行示例"
		};
	}

	SK.regref("u-linuxcmd", {
		title: "Linux 常用命令",
		setsLabel: "发行版",
		store: "sk-linuxcmd-dist",
		sets: DISTS.map(makeset)
	});
})();
