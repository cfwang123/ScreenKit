#!/usr/bin/env node
/**
 * 把 CHANGELOG.md 写成自建更新服务器的 changelog.json，并拷入当前版本的安装包。
 *
 *   node scripts/publish-update-server.mjs
 *   node scripts/publish-update-server.mjs --dest <服务器目录>
 *
 * 不写 --dest 时写到仓库里的 update-server/（已 gitignore）。
 * changelog.json 的 versions 含每个已发布版本的英文和中文，不含 unreleased。
 */
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const csproj = path.join(root, 'ScreenKit', 'ScreenKit.csproj');
const changelogPath = path.join(root, 'CHANGELOG.md');

function argsDest() {
	const dests = [];
	const argv = process.argv.slice(2);
	for (let i = 0; i < argv.length; i++) {
		if (argv[i] !== '--dest') continue;
		const next = argv[i + 1];
		if (!next) throw new Error('--dest 需要目录');
		dests.push(path.resolve(next));
		i++;
	}
	if (dests.length === 0) dests.push(path.join(root, 'update-server'));
	return dests;
}

function readVersion() {
	const xml = fs.readFileSync(csproj, 'utf8');
	const m = xml.match(/<Version>([^<]+)<\/Version>/);
	if (!m) throw new Error('无法在 ScreenKit.csproj 中读取 <Version>');
	return m[1].trim();
}

function trimBlock(lines) {
	let start = 0;
	let end = lines.length;
	while (start < end && lines[start].trim() === '') start++;
	while (end > start && lines[end - 1].trim() === '') end--;
	return lines.slice(start, end).join('\n');
}

function parseChangelog(text) {
	const lines = text.split(/\r?\n/);
	const versions = [];
	let cur = null;
	let mode = '';
	function flush() {
		if (!cur) return;
		versions.push({
			version: cur.version,
			date: cur.date,
			english: trimBlock(cur.english),
			chinese: trimBlock(cur.chinese),
		});
		cur = null;
		mode = '';
	}
	for (const line of lines) {
		const head = line.match(/^##\s+v(\d+(?:\.\d+){1,3})(?:\s*\(([^)]*)\))?\s*$/);
		if (head) {
			flush();
			cur = { version: head[1], date: (head[2] || '').trim(), english: [], chinese: [] };
			mode = '';
			continue;
		}
		if (/^##\s+/.test(line)) {
			flush();
			continue;
		}
		if (!cur) continue;
		if (/^###\s+English\s*$/.test(line)) { mode = 'en'; continue; }
		if (/^###\s+中文\s*$/.test(line)) { mode = 'zh'; continue; }
		if (mode === 'en') cur.english.push(line);
		else if (mode === 'zh') cur.chinese.push(line);
	}
	flush();
	return versions;
}

function publishOne(dest, version, versions, archive) {
	fs.mkdirSync(dest, { recursive: true });
	const jsonPath = path.join(dest, 'changelog.json');
	const body = JSON.stringify({ versions }, null, '\t') + '\n';
	fs.writeFileSync(jsonPath, body, 'utf8');
	console.log(`changelog.json  ${versions.length} 个版本  ${jsonPath}`);
	if (!fs.existsSync(archive)) {
		console.warn(`未找到安装包，只写了日志: ${archive}`);
		return;
	}
	const folder = path.join(dest, 'releases', version);
	fs.mkdirSync(folder, { recursive: true });
	const name = path.basename(archive);
	const target = path.join(folder, name);
	fs.copyFileSync(archive, target);
	const mb = (fs.statSync(target).size / (1024 * 1024)).toFixed(2);
	console.log(`安装包  ${name}  ${mb} MB  ${target}`);
}

function main() {
	const version = readVersion();
	const versions = parseChangelog(fs.readFileSync(changelogPath, 'utf8'));
	if (versions.length === 0) throw new Error('CHANGELOG.md 里没有版本');
	const hit = versions.some((v) => v.version === version);
	if (!hit) console.warn(`CHANGELOG.md 没有当前版本 ${version} 的章节`);
	const archive = path.join(root, 'release', `screenkit_${version}.7z`);
	for (const dest of argsDest()) publishOne(dest, version, versions, archive);
}

try {
	main();
} catch (e) {
	console.error(e.message || e);
	process.exit(1);
}
