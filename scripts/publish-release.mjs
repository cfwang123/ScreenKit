#!/usr/bin/env node
/**
 * Release 编译并打包精简发布目录为 release/screenkit_<version>.7z
 *
 * 用法（在仓库根目录）:
 *   node scripts/publish-release.mjs
 *   node scripts/publish-release.mjs --skip-build
 *   node scripts/publish-release.mjs --clean-only
 *
 * 打包前删掉精简目录里运行时留下的截图、日志、配置和接收文件。
 * 需要已安装 7-Zip（7z 在 PATH，或默认安装路径）。--clean-only 只清理、不打包。
 */
import { execSync, spawnSync } from 'child_process';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const csproj = path.join(root, 'ScreenKit', 'ScreenKit.csproj');
const slimDir = path.join(root, 'ScreenKit', 'bin', 'Release', 'ScreenKit');
const releaseDir = path.join(root, 'release');
const skipBuild = process.argv.includes('--skip-build');
const cleanOnly = process.argv.includes('--clean-only');

// 运行精简包时落在目录里、不应打进发布包的内容
const junkDirs = [
	// 使用痕迹 / 用户数据
	'log', 'screenshots', 'sendfile', 'tmp', 'apk',
	// 「安装功能」按需装进来的组件
	'ocrmodels', 'asrmodels', 'ttsmodels', 'facemodels', 'translatemodels', 'translate',
	'onnxcpu64', 'onnxgpu64', 'onnxdml64', 'ffmpeg64', 'wetext',
	// 原生库的旁路目录（安装时 copybeside 生成）
	'x64', 'dll', 'runtimes',
];
// 支持 * 通配；只匹配精简目录顶层
const junkFiles = [
	// 配置与用户数据（可能含密钥 / 本机路径）
	'config.toml', 'sql-conns.json', 'note.json', 'perf.json', 'cli_last.log',
	'dict.db', 'dict.db-wal', 'dict.db-shm',
	'dict2.db', 'dict2.db-wal', 'dict2.db-shm',
	'dict3.bin', 'ime.db', 'ime_user.db',
	// 按需组件：原生库与托管绑定
	'OpenCvSharpExtern.dll', 'opencv_videoio_ffmpeg*.dll',
	'libSkiaSharp.dll', 'SkiaSharp.dll',
	'pdfium.dll', 'PdfSharp*.dll', 'PDFtoImage.dll',
	// 注意：sherpa-onnx-c-api.dll 由 csproj 的 KeepSherpaNative 刻意保留在根目录，不可在此排除
	'ZXing.dll', 'SharpCompress.dll', 'SharpSevenZip.dll',
	'e_sqlite3.dll', 'Bogus.dll', 'NPinyin.dll',
	'MySqlConnector.dll', 'Oracle.ManagedDataAccess.dll',
	'System.Formats.Asn1.dll', 'Microsoft.Data.Sqlite.dll', 'SQLitePCLRaw.*.dll',
	// 历史构建残留（当前构建已不生成）
	'ZstdSharp.dll', 'System.Text.Encodings.Web.dll', 'jsonarr.exe',
	'WpfOCR.exe', 'WpfOCR.exe.config', 'WpfOCR.x86.exe', 'WpfOCR.x86.exe.config',
];

function readVersion() {
	const xml = fs.readFileSync(csproj, 'utf8');
	const m = xml.match(/<Version>([^<]+)<\/Version>/);
	if (!m) throw new Error('无法在 ScreenKit.csproj 中读取 <Version>');
	return m[1].trim();
}

function run(cmd, opts = {}) {
	console.log(`> ${cmd}`);
	execSync(cmd, { cwd: root, stdio: 'inherit', shell: true, ...opts });
}

function removePath(p) {
	fs.rmSync(p, { recursive: true, force: true });
	console.log(`已删除 ${path.relative(root, p)}`);
}

/** 把 junkFiles 里的名字（可含 *）展开成精简目录顶层实际存在的文件名。 */
function expandPattern(dir, pattern) {
	if (!pattern.includes('*')) return fs.existsSync(path.join(dir, pattern)) ? [pattern] : [];
	let names;
	try {
		names = fs.readdirSync(dir);
	} catch {
		return [];
	}
	const re = new RegExp('^' + pattern.split('*')
		.map(s => s.replace(/[.+^$${}()|[\]\\]/g, '\\$&'))
		.join('.*') + '$', 'i');
	return names.filter(n => re.test(n));
}

/** 生成 7z 的 -x! 排除参数，与 cleanSlimDir 共用 junkDirs / junkFiles，避免两份列表漂移。 */
function buildExcludes(slimName) {
	const ex = [];
	for (const d of junkDirs) {
		ex.push(`-x!${slimName}\\D:/VS_Projects/AIPrototype/PC/OCR/ScreenKit/Infrastructure/DbConnStore.cs`);
		ex.push(`-x!${slimName}\\D:/VS_Projects/AIPrototype/PC/OCR/ScreenKit/Infrastructure/DbConnStore.cs\\*`);
	}
	for (const f of junkFiles) {
		ex.push(`-x!${slimName}\\d:/VS_Projects/AIPrototype/PC/OCR/ScreenKit/SendFile/SendFileServer.cs`);
	}
	ex.push(`-x!${slimName}\\*.log`);
	return ex;
}

/** 删掉精简发布目录里的截图、日志和其它本机运行残留。 */
function cleanSlimDir(dir) {
	for (const name of junkDirs) {
		const p = path.join(dir, name);
		if (!fs.existsSync(p)) continue;
		try {
			removePath(p);
		} catch (e) {
			console.warn(`未能删除 ${path.relative(root, p)}: ${e.message || e}`);
		}
	}
	for (const pattern of junkFiles) {
		for (const name of expandPattern(dir, pattern)) {
			const p = path.join(dir, name);
			try {
				removePath(p);
			} catch (e) {
				console.warn(`未能删除 ${path.relative(root, p)}: ${e.message || e}`);
			}
		}
	}
	let names;
	try {
		names = fs.readdirSync(dir, { withFileTypes: true });
	} catch (e) {
		console.warn(`未能列出 ${path.relative(root, dir)}: ${e.message || e}`);
		return;
	}
	for (const ent of names) {
		if (!ent.isFile() || !ent.name.toLowerCase().endsWith('.log')) continue;
		const p = path.join(dir, ent.name);
		try {
			removePath(p);
		} catch (e) {
			console.warn(`未能删除 ${path.relative(root, p)}: ${e.message || e}`);
		}
	}
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
	const archive = path.join(releaseDir, `screenkit_${version}.7z`);

	if (!skipBuild && !cleanOnly) {
		// ScreenKit 构建后会自动编并拷贝独立 x86host.exe
		run('dotnet build ScreenKit/ScreenKit.csproj -c Release');
	}

	const exe = path.join(slimDir, 'ScreenKit.exe');
	if (!fs.existsSync(exe)) {
		throw new Error(`精简发布目录不存在: ${slimDir}\n请先 Release 编译（应生成 bin/Release/ScreenKit/）。`);
	}
	const x86 = path.join(slimDir, 'x86host.exe');
	if (fs.existsSync(x86)) {
		console.log(`含 x86host: ${path.relative(root, x86)}`);
	} else {
		console.warn('未找到 x86host.exe（可选；部分 SAPI 语音需 32 位）');
	}

	cleanSlimDir(slimDir);
	if (cleanOnly) {
		console.log(`已清理: ${path.relative(root, slimDir)}`);
		return;
	}

	fs.mkdirSync(releaseDir, { recursive: true });
	if (fs.existsSync(archive)) fs.unlinkSync(archive);

	const zip7 = find7z();
	const slimParent = path.dirname(slimDir);
	const slimName = path.basename(slimDir);
	// 与 cleanSlimDir 共用同一份 junkDirs / junkFiles，避免两份排除列表漂移
	const excludes = buildExcludes(slimName);
	const cmd = `"${zip7}" a -t7z -mx=9 "${archive}" "${slimName}"` +
		excludes.map(e => ` "${e}"`).join('');
	console.log(`> ${cmd}`);
	execSync(cmd, { cwd: slimParent, stdio: 'inherit', shell: true });

	const stat = fs.statSync(archive);
	const mb = (stat.size / (1024 * 1024)).toFixed(2);
	const keep = path.basename(archive).toLowerCase();
	for (const name of fs.readdirSync(releaseDir)) {
		const low = name.toLowerCase();
		if (!low.startsWith('screenkit_') || !low.endsWith('.7z') || low === keep) continue;
		fs.unlinkSync(path.join(releaseDir, name));
		console.log(`已删除旧包 ${name}`);
	}
	console.log(`\n已发布: ${path.relative(root, archive)} (${mb} MB)`);
	console.log(`来源: ${path.relative(root, slimDir)}`);
}

try {
	main();
} catch (e) {
	console.error(e.message || e);
	process.exit(1);
}
