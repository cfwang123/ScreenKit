<?php
/**
 * 编译产物的 web 脚本处理。
 *
 *   php scripts/minify-web.php --join <工具脚本目录> <输出 tools.js>  合并工具脚本（源文件较新才做）
 *   php scripts/minify-web.php <web目录>                          生成 *.min.js / *.min.css（源文件较新才做）
 *   php scripts/minify-web.php --slim <web目录>                   精简包：原地换成压缩内容，删掉 *.min.* 副本
 *
 * 压缩用 PATH 上的 minify.exe。L.js 由程序运行时按语言包生成，跳过。
 */

const SKIP_NAMES = ['l.js'];

function fail(string $msg): void {
	fwrite(STDERR, $msg . PHP_EOL);
	exit(1);
}

/** web 目录下要处理的 js/css（不含已经压缩的副本和运行时生成的文件）。 */
function sources(string $dir): array {
	clearstatcache();
	$out = [];
	foreach (scandir($dir) ?: [] as $name) {
		$p = $dir . DIRECTORY_SEPARATOR . $name;
		if (!is_file($p)) continue;
		$low = strtolower($name);
		if (str_ends_with($low, '.min.js') || str_ends_with($low, '.min.css')) continue;
		if (in_array($low, SKIP_NAMES, true)) continue;
		$ext = strtolower(pathinfo($name, PATHINFO_EXTENSION));
		if ($ext !== 'js' && $ext !== 'css') continue;
		$out[] = $p;
	}
	sort($out, SORT_STRING);
	return $out;
}

function minpath(string $p): string {
	$ext = pathinfo($p, PATHINFO_EXTENSION);
	return substr($p, 0, -strlen($ext) - 1) . '.min.' . $ext;
}

function newer(string $src, string $dst): bool {
	clearstatcache(true, $src);
	clearstatcache(true, $dst);
	if (!is_file($dst)) return true;
	return filemtime($src) > filemtime($dst);
}

function minify(string $src, string $dst): void {
	$tmp = $dst . '.tmp';
	if (is_file($tmp)) @unlink($tmp);
	$lines = [];
	exec('minify -o ' . escapeshellarg($tmp) . ' ' . escapeshellarg($src) . ' 2>&1', $lines, $rc);
	if ($rc !== 0 || !is_file($tmp) || filesize($tmp) === 0) {
		@unlink($tmp);
		fail('minify failed: ' . basename($src) . PHP_EOL . implode(PHP_EOL, $lines));
	}
	if (!@rename($tmp, $dst)) {
		@unlink($tmp);
		fail('cannot write ' . $dst);
	}
}

/** 合并工具脚本：tools 目录里没有比 tools.js 新的文件就跳过。 */
function dojoin(string $src, string $dst): void {
	$files = glob($src . DIRECTORY_SEPARATOR . '*.js') ?: [];
	if (!$files) fail('no tool scripts in ' . $src);
	usort($files, fn($a, $b) => strcasecmp(basename($a), basename($b)));
	clearstatcache();
	$newest = 0;
	$want = 0;
	foreach ($files as $f) {
		$newest = max($newest, filemtime($f));
		$sz = filesize($f);
		$want += $sz;
		if ($sz === 0) {
			$want++; // 空文件也会补一个换行
			continue;
		}
		$fh = @fopen($f, 'rb');
		if (!$fh) {
			$want = -1;
			break;
		}
		fseek($fh, -1, SEEK_END);
		if (fgetc($fh) !== "\n") $want++;
		fclose($fh);
	}
	// 时间戳和长度都对得上才跳过。文件按旧时间戳还原过时间没有更新的，靠长度和下面的内容比对兜住。
	if ($want >= 0 && is_file($dst) && filemtime($dst) >= $newest && filesize($dst) === $want) return;
	$buf = '';
	foreach ($files as $f) {
		$text = file_get_contents($f);
		if ($text === false) fail('cannot read ' . $f);
		$buf .= $text;
		if ($text === '' || substr($text, -1) !== "\n") $buf .= "\n";
	}
	if (is_file($dst) && file_get_contents($dst) === $buf) return;
	if (@file_put_contents($dst, $buf) === false) fail('cannot write ' . $dst);
}

/** 输出目录：生成压缩副本。 */
function domin(string $dir): void {
	$n = 0;
	foreach (sources($dir) as $p) {
		$dst = minpath($p);
		if (!newer($p, $dst)) continue;
		minify($p, $dst);
		$n++;
	}
	if ($n) echo 'web minify: ' . $n . ' file(s) updated' . PHP_EOL;
}

/** 精简包：压缩内容原地覆盖，只留合并、压缩后的脚本。 */
function doslim(string $dir): void {
	clearstatcache();
	$n = 0;
	foreach (sources($dir) as $p) {
		$dst = minpath($p);
		if (is_file($dst) && filesize($dst) > 0) {
			if (!@rename($dst, $p)) fail('cannot replace ' . basename($p));
		}
		else
			minify($p, $p);
		$n++;
	}
	// 删掉编译期生成的副本。viewer.min.* 本身是发布文件，没有同名源文件，保留。
	foreach (scandir($dir) ?: [] as $name) {
		$p = $dir . DIRECTORY_SEPARATOR . $name;
		if (!is_file($p)) continue;
		$low = strtolower($name);
		if (!str_ends_with($low, '.min.js') && !str_ends_with($low, '.min.css')) continue;
		$ext = strtolower(pathinfo($name, PATHINFO_EXTENSION));
		if (is_file(substr($p, 0, -strlen($ext) - 5) . '.' . $ext)) @unlink($p);
	}
	if ($n) echo 'slim web minify: ' . $n . ' file(s)' . PHP_EOL;
}

$args = array_slice($argv, 1);
$mode = 'min';
if (($args[0] ?? '') === '--join') {
	$mode = 'join';
	array_shift($args);
}
elseif (($args[0] ?? '') === '--slim') {
	$mode = 'slim';
	array_shift($args);
}

if ($mode === 'join') {
	if (count($args) < 2) fail('usage: php scripts/minify-web.php --join <toolsdir> <outfile>');
	dojoin($args[0], $args[1]);
	exit(0);
}

$dir = $args[0] ?? '';
if ($dir === '' || !is_dir($dir)) fail('web dir not found: ' . $dir);
if ($mode === 'slim')
	doslim($dir);
else
	domin($dir);
