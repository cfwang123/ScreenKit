(function(){
	var CATS = {
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
				["proc", "lsb_release -a", "查看发行版描述", "lsb_release -a"],
				["net", "nmcli device status", "查看网络设备状态", "nmcli device status"]
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

	// [分组, 命令, 说明, 常用示例, 备注]
	function R(a){
		var g = a[0];
		var note = a[4] || "—";
		return {
			g: g,
			c: [a[1], a[2], a[3]],
			x: (CATS[g] || "") + " " + a[2] + " " + a[3] + " " + note,
			d: [["类别", CATS[g] || ""], ["常用示例", a[3]], ["备注", note]]
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
			defaultPick: "ls"
		};
	}

	SK.regref("u-linuxcmd", {
		title: "Linux 常用命令",
		setsLabel: "发行版",
		store: "sk-linuxcmd-dist",
		sets: DISTS.map(makeset)
	});
})();
