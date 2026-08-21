#!/usr/bin/env node
/**
 * Release 编译并打包精简发布目录为 release/wpfocr_<version>.7z
 *
 * 用法（在仓库根目录）:
 *   node scripts/publish-release.mjs
 *   node scripts/publish-release.mjs --skip-build
 *
 * 需要已安装 7-Zip（7z 在 PATH，或默认安装路径）。
 */
import { execSync, spawnSync } from 'child_process';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const csproj = path.join(root, 'WpfOCR', 'WpfOCR.csproj');
const slimDir = path.join(root, 'WpfOCR', 'bin', 'Release', 'WpfOCR');
const releaseDir = path.join(root, 'release');
const skipBuild = process.argv.includes('--skip-build');

function readVersion() {
	const xml = fs.readFileSync(csproj, 'utf8');
	const m = xml.match(/<Version>([^<]+)<\/Version>/);
	if (!m) throw new Error('无法在 WpfOCR.csproj 中读取 <Version>');
	return m[1].trim();
}

function run(cmd, opts = {}) {
	console.log(`> ${cmd}`);
	execSync(cmd, { cwd: root, stdio: 'inherit', shell: true, ...opts });
}

function find7z() {
	const candidates = [
		process.env.SEVEN_ZIP,
		'7z',
		'C:\\Program Files\\7-Zip\\7z.exe',
		'C:\\Program Files (x86)\\7-Zip\\7z.exe',
	].filter(Boolean);
	for (const c of candidates) {
		const r = spawnSync(c, ['-h'], { stdio: 'ignore', shell: true });
		if (r.status === 0) return c;
	}
	throw new Error('未找到 7-Zip。请安装 7-Zip 或将 7z.exe 加入 PATH（可设置 SEVEN_ZIP 环境变量）。');
}

function main() {
	const version = readVersion();
	const archive = path.join(releaseDir, `wpfocr_${version}.7z`);

	if (!skipBuild) {
		// WpfOCR 构建后会自动编并拷贝独立 x86host.exe
		run('dotnet build WpfOCR/WpfOCR.csproj -c Release');
	}

	const exe = path.join(slimDir, 'WpfOCR.exe');
	if (!fs.existsSync(exe)) {
		throw new Error(`精简发布目录不存在: ${slimDir}\n请先 Release 编译（应生成 bin/Release/WpfOCR/）。`);
	}
	const x86 = path.join(slimDir, 'x86host.exe');
	if (fs.existsSync(x86)) {
		console.log(`含 x86host: ${path.relative(root, x86)}`);
	} else {
		console.warn('未找到 x86host.exe（可选；部分 SAPI 语音需 32 位）');
	}

	fs.mkdirSync(releaseDir, { recursive: true });
	if (fs.existsSync(archive)) fs.unlinkSync(archive);

	const zip7 = find7z();
	const cmd = `"${zip7}" a -t7z -mx=9 "${archive}" "${slimDir}${path.sep}*"`;
	console.log(`> ${cmd}`);
	execSync(cmd, { cwd: root, stdio: 'inherit', shell: true });

	const stat = fs.statSync(archive);
	const mb = (stat.size / (1024 * 1024)).toFixed(2);
	console.log(`\n已发布: ${path.relative(root, archive)} (${mb} MB)`);
	console.log(`来源: ${path.relative(root, slimDir)}`);
}

try {
	main();
} catch (e) {
	console.error(e.message || e);
	process.exit(1);
}
