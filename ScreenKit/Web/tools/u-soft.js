(function(){
	var CATS = {
		dev: "开发工具",
		sys: "系统工具",
		server: "网络与服务器",
		desktop: "桌面与多媒体",
		office: "办公与通讯",
		virt: "虚拟化与容器",
		sec: "安全工具"
	};

	// [分组, 软件, 包名, 安装命令, 说明, 备注, 额外搜索词]
	function R(a){
		var g = a[0];
		var note = a[5] || "—";
		return {
			g: g,
			c: [a[1], a[2], a[3], a[4]],
			x: (CATS[g] || "") + " " + a[1] + " " + a[2] + " " + a[3] + " " + a[4] + " " + note + " " + (a[6] || ""),
			d: [["类别", CATS[g] || ""], ["安装命令", a[3]], ["说明", a[4]], ["备注", note]]
		};
	}

	var ROWS = [
		["dev", "Git", "git", "sudo apt install git", "版本控制", "", "版本控制 vcs"],
		["dev", "curl", "curl", "sudo apt install curl", "命令行 HTTP 客户端"],
		["dev", "wget", "wget", "sudo apt install wget", "命令行下载"],
		["dev", "Vim", "vim", "sudo apt install vim", "终端编辑器"],
		["dev", "Neovim", "neovim", "sudo apt install neovim", "现代 Vim"],
		["dev", "Nano", "nano", "sudo apt install nano", "简单终端编辑器"],
		["dev", "tmux", "tmux", "sudo apt install tmux", "终端复用"],
		["dev", "Zsh", "zsh", "sudo apt install zsh", "Shell"],
		["dev", "编译工具", "build-essential", "sudo apt install build-essential", "gcc、make 等编译工具"],
		["dev", "CMake", "cmake", "sudo apt install cmake", "跨平台构建"],
		["dev", "GDB", "gdb", "sudo apt install gdb", "调试器"],
		["dev", "Python pip", "python3-pip", "sudo apt install python3-pip", "Python 包管理"],
		["dev", "Python venv", "python3-venv", "sudo apt install python3-venv", "Python 虚拟环境"],
		["dev", "OpenJDK 21", "openjdk-21-jdk", "sudo apt install openjdk-21-jdk", "Java 21", "22.04 可改用 openjdk-17-jdk。"],
		["dev", "Node.js", "nodejs", "sudo apt install nodejs npm", "Node.js 和 npm", "源里的版本可能偏旧，开发建议用 nvm 或 NodeSource。"],
		["dev", "Go", "golang", "sudo apt install golang", "Go 工具链"],
		["dev", "Rust", "rustc", "sudo apt install rustc cargo", "Rust 工具链"],
		["dev", "ripgrep", "ripgrep", "sudo apt install ripgrep", "快速 grep（命令 rg）"],
		["dev", "fd-find", "fd-find", "sudo apt install fd-find", "find 替代（命令 fdfind）"],
		["dev", "fzf", "fzf", "sudo apt install fzf", "模糊查找"],
		["dev", "bat", "bat", "sudo apt install bat", "cat 替代（命令 batcat）"],
		["dev", "jq", "jq", "sudo apt install jq", "JSON 处理"],
		["dev", "HTTPie", "httpie", "sudo apt install httpie", "友好 HTTP 客户端"],
		["dev", "SQLite", "sqlite3", "sudo apt install sqlite3", "SQLite 命令行"],
		["dev", "PostgreSQL 客户端", "postgresql-client", "sudo apt install postgresql-client", "psql 客户端"],
		["dev", "Redis 工具", "redis-tools", "sudo apt install redis-tools", "redis-cli"],
		["dev", "VS Code", "code", "sudo snap install code --classic", "代码编辑器", "", "编辑器 IDE"],
		["dev", "Postman", "postman", "sudo snap install postman", "API 调试"],
		["dev", "DBeaver", "dbeaver-ce", "sudo snap install dbeaver-ce", "数据库客户端"],

		["sys", "htop", "htop", "sudo apt install htop", "交互式进程查看"],
		["sys", "btop", "btop", "sudo apt install btop", "资源监视器"],
		["sys", "neofetch", "neofetch", "sudo apt install neofetch", "系统信息"],
		["sys", "tree", "tree", "sudo apt install tree", "树形目录"],
		["sys", "ncdu", "ncdu", "sudo apt install ncdu", "磁盘占用分析"],
		["sys", "duf", "duf", "sudo apt install duf", "磁盘空间查看"],
		["sys", "Timeshift", "timeshift", "sudo apt install timeshift", "系统快照"],
		["sys", "GParted", "gparted", "sudo apt install gparted", "图形分区"],
		["sys", "GNOME Tweaks", "gnome-tweaks", "sudo apt install gnome-tweaks", "GNOME 调整"],
		["sys", "dconf Editor", "dconf-editor", "sudo apt install dconf-editor", "深度设置"],
		["sys", "Baobab", "baobab", "sudo apt install baobab", "磁盘占用图"],
		["sys", "磁盘工具", "gnome-disk-utility", "sudo apt install gnome-disk-utility", "磁盘管理"],
		["sys", "Synaptic", "synaptic", "sudo apt install synaptic", "图形包管理"],
		["sys", "BleachBit", "bleachbit", "sudo apt install bleachbit", "清理工具"],
		["sys", "Ranger", "ranger", "sudo apt install ranger", "终端文件管理器"],
		["sys", "Midnight Commander", "mc", "sudo apt install mc", "双栏文件管理器"],
		["sys", "Screen", "screen", "sudo apt install screen", "终端复用"],

		["server", "Nginx", "nginx", "sudo apt install nginx", "Web 服务器"],
		["server", "Apache", "apache2", "sudo apt install apache2", "Apache Web 服务器"],
		["server", "MySQL", "mysql-server", "sudo apt install mysql-server", "MySQL 数据库"],
		["server", "PostgreSQL", "postgresql", "sudo apt install postgresql", "PostgreSQL 数据库"],
		["server", "MariaDB", "mariadb-server", "sudo apt install mariadb-server", "MariaDB 数据库"],
		["server", "Redis", "redis-server", "sudo apt install redis-server", "Redis 缓存"],
		["server", "Memcached", "memcached", "sudo apt install memcached", "内存缓存"],
		["server", "Samba", "samba", "sudo apt install samba", "SMB 文件共享"],
		["server", "NFS 服务", "nfs-kernel-server", "sudo apt install nfs-kernel-server", "NFS 文件共享"],
		["server", "vsftpd", "vsftpd", "sudo apt install vsftpd", "FTP 服务"],
		["server", "OpenSSH Server", "openssh-server", "sudo apt install openssh-server", "SSH 服务"],
		["server", "WireGuard", "wireguard", "sudo apt install wireguard", "VPN"],
		["server", "OpenVPN", "openvpn", "sudo apt install openvpn", "VPN"],
		["server", "HAProxy", "haproxy", "sudo apt install haproxy", "负载均衡"],
		["server", "Docker", "docker.io", "sudo apt install docker.io docker-compose-v2", "容器引擎", "22.04 用 docker-compose；装完把用户加入 docker 组。", "容器 compose"],
		["server", "Podman", "podman", "sudo apt install podman", "无守护进程容器", "", "容器"],
		["server", "containerd", "containerd", "sudo apt install containerd", "容器运行时", "", "容器"],

		["desktop", "VLC", "vlc", "sudo apt install vlc", "视频播放", "", "播放器 视频 电影"],
		["desktop", "mpv", "mpv", "sudo apt install mpv", "轻量播放器", "", "播放器"],
		["desktop", "GIMP", "gimp", "sudo apt install gimp", "图像编辑", "", "修图 图片"],
		["desktop", "Inkscape", "inkscape", "sudo apt install inkscape", "矢量绘图", "", "SVG 设计"],
		["desktop", "Krita", "krita", "sudo apt install krita", "数字绘画", "", "绘画"],
		["desktop", "Blender", "blender", "sudo apt install blender", "3D 建模", "", "3D 动画"],
		["desktop", "OBS Studio", "obs-studio", "sudo apt install obs-studio", "录屏和直播", "", "录屏 直播 推流"],
		["desktop", "Audacity", "audacity", "sudo apt install audacity", "音频编辑", "", "录音"],
		["desktop", "Kdenlive", "kdenlive", "sudo apt install kdenlive", "视频剪辑", "", "剪辑"],
		["desktop", "FFmpeg", "ffmpeg", "sudo apt install ffmpeg", "音视频转换", "", "视频 音频 转码"],
		["desktop", "ImageMagick", "imagemagick", "sudo apt install imagemagick", "图像批处理", "", "图片 转换"],
		["desktop", "Darktable", "darktable", "sudo apt install darktable", "照片后期", "", "摄影 RAW"],
		["desktop", "HandBrake", "handbrake", "sudo apt install handbrake", "视频转码", "", "转码"],
		["desktop", "Steam", "steam", "sudo apt install steam", "游戏平台", "需要 multiverse 源。", "游戏"],
		["desktop", "Wine", "wine", "sudo apt install wine", "运行 Windows 程序", "", "Windows exe"],
		["desktop", "Lutris", "lutris", "sudo apt install lutris", "游戏管理", "", "游戏"],

		["office", "LibreOffice", "libreoffice", "sudo apt install libreoffice", "办公套件", "", "Word Excel PPT"],
		["office", "Thunderbird", "thunderbird", "sudo apt install thunderbird", "邮件客户端", "", "邮件"],
		["office", "ONLYOFFICE", "onlyoffice-desktopeditors", "sudo snap install onlyoffice-desktopeditors", "Office 兼容套件", "", "Word Excel PPT"],
		["office", "Telegram", "telegram-desktop", "sudo apt install telegram-desktop", "Telegram", "", "聊天 通讯"],
		["office", "Discord", "discord", "sudo snap install discord", "Discord", "", "聊天 语音"],
		["office", "Slack", "slack", "sudo snap install slack", "Slack", "", "聊天 团队"],
		["office", "Obsidian", "obsidian", "sudo snap install obsidian --classic", "Markdown 笔记", "", "笔记 markdown"],
		["office", "Evince", "evince", "sudo apt install evince", "PDF 阅读", "", "PDF"],
		["office", "Okular", "okular", "sudo apt install okular", "PDF 阅读", "", "PDF"],

		["virt", "VirtualBox", "virtualbox", "sudo apt install virtualbox", "虚拟机", "需要 multiverse 源。", "虚拟机 VM"],
		["virt", "KVM / virt-manager", "qemu-kvm", "sudo apt install qemu-kvm libvirt-daemon-system virt-manager", "KVM 虚拟化", "需要 CPU 虚拟化；把用户加入 libvirt 组。", "虚拟机 VM"],
		["virt", "GNOME Boxes", "gnome-boxes", "sudo apt install gnome-boxes", "简单虚拟机", "", "虚拟机 VM"],
		["virt", "Distrobox", "distrobox", "sudo apt install distrobox", "在容器里跑发行版", "", "容器"],
		["virt", "Toolbox", "toolbox", "sudo apt install toolbox", "容器化开发环境", "", "容器"],

		["sec", "UFW", "ufw", "sudo apt install ufw", "防火墙", "", "防火墙"],
		["sec", "GUFW", "gufw", "sudo apt install gufw", "防火墙图形界面", "", "防火墙"],
		["sec", "Fail2ban", "fail2ban", "sudo apt install fail2ban", "防暴力破解", "", "安全 入侵"],
		["sec", "ClamAV", "clamav", "sudo apt install clamav", "病毒扫描", "", "杀毒"],
		["sec", "rkhunter", "rkhunter", "sudo apt install rkhunter", "Rootkit 检查", "", "安全"],
		["sec", "Nmap", "nmap", "sudo apt install nmap", "端口扫描", "", "扫描 网络"],
		["sec", "Wireshark", "wireshark", "sudo apt install wireshark", "抓包分析", "", "抓包 网络"],
		["sec", "KeePassXC", "keepassxc", "sudo apt install keepassxc", "密码管理", "", "密码"],
		["sec", "GnuPG", "gnupg", "sudo apt install gnupg", "加密与签名", "", "GPG 加密"]
	].map(R);

	SK.regref("u-soft", {
		title: "Linux 常用软件",
		cols: [
			{ name: "软件" },
			{ name: "包名", mono: true },
			{ name: "安装命令", mono: true },
			{ name: "说明" }
		],
		widths: ["150px", "180px", "300px", ""],
		groups: [
			{ id: "dev", name: "开发工具" },
			{ id: "sys", name: "系统工具" },
			{ id: "server", name: "网络与服务器" },
			{ id: "desktop", name: "桌面与多媒体" },
			{ id: "office", name: "办公与通讯" },
			{ id: "virt", name: "虚拟化与容器" },
			{ id: "sec", name: "安全工具" }
		],
		rows: ROWS,
		placeholder: "搜索：Linux 软件、编辑器、播放器、数据库",
		big: 0,
		sub: 1,
		copy: 2,
		defaultPick: "Git"
	});
})();
