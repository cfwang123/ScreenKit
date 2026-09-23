#!/usr/bin/env node
'use strict';

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const ROOT = __dirname;
const IS_WIN = process.platform === 'win32';
const GRADLE_WRAPPER = path.join(ROOT, IS_WIN ? 'gradlew.bat' : 'gradlew');

const APP_ID = 'com.whj.screenkit';
const MAIN_ACTIVITY = `${APP_ID}/.MainActivity`;
const RELEASE_APK_DIR = path.join(ROOT, 'release');
const APP_BUILD_GRADLE = path.join(ROOT, 'app', 'build.gradle.kts');

/** 参与增量判断的源路径（相对 ROOT） */
const WATCH_PATHS = [
  'app/src',
  'app/build.gradle.kts',
  'app/proguard-rules.pro',
  'build.gradle.kts',
  'settings.gradle.kts',
  'gradle.properties',
  'gradle/wrapper',
];

const HELP = `
PC文件传输 Android 构建脚本（com.whj.screenkit）

用法:
  node build.js <command> [options]

命令:
  build     编译 APK（默认 release）
  release   编译 release，复制到 release/ 与 ScreenKit 输出 apk/，再 slx 编译电脑端
  rebuild   清理后重新编译（clean + build）
  clean     清理构建产物
  run       安装到真机并启动（debug 包）
  install   release 编译（按源码时间增量）并安装到真机，然后启动
  apk       编译 release 并复制到 release/screenkit{version}.apk
  devices   列出已连接的 adb 设备

选项:
  --debug     build / rebuild 时编译 debug 包
  --release   build / rebuild 时编译 release 包（默认）
  --force     install 时强制重新编译（忽略时间戳）
  -s <serial> run / install 时指定设备序列号

示例:
  node build.js release     # 含拷到 ScreenKit 输出并 slx
  node build.js build --debug
  node build.js run
  node build.js install
  node build.js install --force
  node build.js install -s ABC123456789
  node build.js apk
`;

function die(message) {
  console.error(message);
  process.exit(1);
}

function ensureJavaHome() {
  if (process.env.JAVA_HOME) {
    return;
  }
  const candidates = [
    'E:\\ProgramFiles\\jdk-21.0.8',
    path.join(process.env['ProgramFiles'] || 'C:\\Program Files', 'Android', 'Android Studio', 'jbr'),
    'E:\\Program Files\\Android\\Android Studio\\jbr',
    'C:\\Program Files\\Android\\Android Studio\\jbr',
  ];
  for (const dir of candidates) {
    const java = path.join(dir, 'bin', IS_WIN ? 'java.exe' : 'java');
    if (fs.existsSync(java)) {
      process.env.JAVA_HOME = dir;
      console.log(`JAVA_HOME=${dir}`);
      return;
    }
  }
}

function ensureProxy() {
  if (process.env.HTTPS_PROXY || process.env.HTTP_PROXY || process.env.https_proxy) {
    return;
  }
  process.env.HTTP_PROXY = 'http://127.0.0.1:7897';
  process.env.HTTPS_PROXY = 'http://127.0.0.1:7897';
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: ROOT,
    stdio: 'inherit',
    shell: IS_WIN,
    env: process.env,
    ...options,
  });
  if (result.error) {
    die(`执行失败: ${command} ${args.join(' ')}\n${result.error.message}`);
  }
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

/** 不因非零退出码抛错，返回 { status, stdout, stderr } */
function runAllowFail(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: ROOT,
    encoding: 'utf8',
    shell: IS_WIN,
    env: process.env,
    ...options,
  });
  if (result.error) {
    die(`执行失败: ${command} ${args.join(' ')}\n${result.error.message}`);
  }
  return {
    status: result.status ?? 1,
    stdout: result.stdout || '',
    stderr: result.stderr || '',
  };
}

function runCapture(command, args) {
  const result = spawnSync(command, args, {
    cwd: ROOT,
    encoding: 'utf8',
    shell: IS_WIN,
    env: process.env,
  });
  if (result.error) {
    die(`执行失败: ${command} ${args.join(' ')}\n${result.error.message}`);
  }
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
  return result.stdout;
}

function gradle(tasks) {
  ensureJavaHome();
  ensureProxy();
  if (!fs.existsSync(GRADLE_WRAPPER)) {
    die(`未找到 Gradle Wrapper: ${GRADLE_WRAPPER}`);
  }
  const taskList = Array.isArray(tasks) ? tasks : [tasks];
  run(GRADLE_WRAPPER, taskList);
}

function adb(args, deviceSerial) {
  const adbArgs = deviceSerial ? ['-s', deviceSerial, ...args] : args;
  run('adb', adbArgs);
}

function adbAllowFail(args, deviceSerial) {
  const adbArgs = deviceSerial ? ['-s', deviceSerial, ...args] : args;
  return runAllowFail('adb', adbArgs);
}

/**
 * 安装 APK。若签名与已装包不一致（debug ↔ release），先卸载再装。
 */
function adbInstallApk(apkPath, deviceSerial) {
  console.log('[install] 安装中…');
  let r = adbAllowFail(['install', '-r', '-d', apkPath], deviceSerial);
  const out = `${r.stdout}\n${r.stderr}`;
  if (r.status === 0 && !/Failure|failed/i.test(out)) {
    return;
  }

  const restricted = /INSTALL_FAILED_USER_RESTRICTED/i.test(out);
  if (restricted) {
    process.stdout.write(r.stdout);
    process.stderr.write(r.stderr);
    die(
      '[install] 安装被系统拒绝（INSTALL_FAILED_USER_RESTRICTED）。\n' +
        '请在手机开发者选项中开启「USB 安装 / USB 调试（安全设置）」，点允许后重试：\n' +
        '  node build.js run',
    );
  }

  const incompatible =
    /INSTALL_FAILED_UPDATE_INCOMPATIBLE|signatures do not match/i.test(out);
  if (!incompatible) {
    process.stdout.write(r.stdout);
    process.stderr.write(r.stderr);
    die(`[install] 安装失败 (exit ${r.status})`);
  }

  console.log(
    `[install] 签名与已装包不一致（旧包为其它密钥所签），将卸载 ${APP_ID} 后重装…`,
  );
  const u = adbAllowFail(['uninstall', APP_ID], deviceSerial);
  if (u.status !== 0) {
    console.log(`[install] uninstall 输出: ${(u.stdout + u.stderr).trim() || '(无)'}`);
  } else {
    console.log('[install] 旧包已卸载');
  }

  console.log('[install] 重新安装…');
  r = adbAllowFail(['install', '-r', '-d', apkPath], deviceSerial);
  process.stdout.write(r.stdout);
  process.stderr.write(r.stderr);
  if (r.status !== 0 || /Failure|failed/i.test(`${r.stdout}\n${r.stderr}`)) {
    die('[install] 卸载后安装仍失败');
  }
}

function capitalize(value) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function parseArgs(argv) {
  const positional = [];
  let variant = 'release';
  let deviceSerial = null;
  let force = false;

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--debug') {
      variant = 'debug';
    } else if (arg === '--release') {
      variant = 'release';
    } else if (arg === '--force') {
      force = true;
    } else if (arg === '-s') {
      deviceSerial = argv[i + 1];
      if (!deviceSerial) {
        die('缺少 -s 参数的设备序列号');
      }
      i += 1;
    } else if (arg === '--help' || arg === '-h') {
      console.log(HELP.trim());
      process.exit(0);
    } else {
      positional.push(arg);
    }
  }

  return { command: positional[0], variant, deviceSerial, force };
}

function getRealDevices() {
  const output = runCapture('adb', ['devices']);
  const devices = [];

  for (const line of output.split('\n')) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('List of devices')) {
      continue;
    }
    const [serial, state] = trimmed.split(/\s+/);
    if (!serial || state !== 'device') {
      continue;
    }
    if (serial.startsWith('emulator-')) {
      continue;
    }
    devices.push(serial);
  }

  return devices;
}

function resolveDevice(serial) {
  const devices = getRealDevices();

  if (devices.length === 0) {
    die('未检测到已连接的真机。请开启 USB 调试并确认 adb devices 可见。');
  }

  if (serial) {
    if (!devices.includes(serial)) {
      die(`设备 ${serial} 未连接或不是可用真机。\n当前真机: ${devices.join(', ')}`);
    }
    return serial;
  }

  if (devices.length > 1) {
    die(`检测到多台真机，请用 -s 指定序列号:\n${devices.join('\n')}`);
  }

  return devices[0];
}

/** 从 app/build.gradle.kts 读取 versionName */
function getVersionName() {
  if (!fs.existsSync(APP_BUILD_GRADLE)) {
    return '1.0.0';
  }
  const text = fs.readFileSync(APP_BUILD_GRADLE, 'utf8');
  const m = text.match(/versionName\s*=\s*["']([^"']+)["']/);
  return m ? m[1] : '1.0.0';
}

function getApkPath(variant) {
  return path.join(
    ROOT,
    'app',
    'build',
    'outputs',
    'apk',
    variant,
    `app-${variant}.apk`,
  );
}

function releaseDestName() {
  return `screenkit${getVersionName()}.apk`;
}

/** 递归取路径下最新 mtime（毫秒） */
function maxMtime(target, state) {
  if (!fs.existsSync(target)) {
    return;
  }
  let st;
  try {
    st = fs.statSync(target);
  } catch {
    return;
  }
  if (st.isFile()) {
    if (st.mtimeMs > state.max) {
      state.max = st.mtimeMs;
      state.file = target;
    }
    return;
  }
  if (!st.isDirectory()) {
    return;
  }
  let names;
  try {
    names = fs.readdirSync(target);
  } catch {
    return;
  }
  for (const name of names) {
    if (
      name === 'build' ||
      name === '.gradle' ||
      name === 'node_modules' ||
      name === '.git' ||
      name === 'tmp' ||
      name === 'release'
    ) {
      continue;
    }
    maxMtime(path.join(target, name), state);
  }
}

function getSourceLatestMtime() {
  const state = { max: 0, file: null };
  for (const rel of WATCH_PATHS) {
    maxMtime(path.join(ROOT, rel), state);
  }
  return state;
}

/**
 * 是否需要重新编译 release：
 * - APK 不存在
 * - 任一监视源文件 mtime 新于 APK
 */
function needsReleaseRebuild(apkPath) {
  if (!fs.existsSync(apkPath)) {
    return { need: true, reason: 'release APK 不存在' };
  }
  const apkMtime = fs.statSync(apkPath).mtimeMs;
  const src = getSourceLatestMtime();
  if (src.max <= 0) {
    return { need: true, reason: '未找到可监视源文件' };
  }
  if (src.max > apkMtime) {
    const rel = path.relative(ROOT, src.file || '');
    return {
      need: true,
      reason: `源码较新（${rel || 'source'}）`,
    };
  }
  return { need: false, reason: '源码未变更' };
}

function ensureReleaseApk(force) {
  const apkPath = getApkPath('release');
  const check = force
    ? { need: true, reason: '--force 强制编译' }
    : needsReleaseRebuild(apkPath);

  if (check.need) {
    console.log(`[install] ${check.reason}，开始 assembleRelease…`);
    gradle('assembleRelease');
  } else {
    console.log(`[install] ${check.reason}，跳过编译`);
  }

  if (!fs.existsSync(apkPath)) {
    die(`未找到 release APK: ${apkPath}`);
  }
  console.log(`[install] APK: ${apkPath}`);
  return apkPath;
}

function build(variant) {
  gradle(`assemble${capitalize(variant)}`);
  const apkPath = getApkPath(variant);
  if (fs.existsSync(apkPath)) {
    console.log(`\nAPK: ${apkPath}`);
  } else if (variant === 'release') {
    const dir = path.join(ROOT, 'app', 'build', 'outputs', 'apk', 'release');
    if (fs.existsSync(dir)) {
      console.log(`\nRelease 输出目录: ${dir}`);
      for (const f of fs.readdirSync(dir)) {
        if (f.endsWith('.apk')) console.log(`  ${path.join(dir, f)}`);
      }
    }
  }
}

/** 将 build 输出目录中的 release APK 复制到项目 release/ 目录 */
function copyReleaseApk(apkPath) {
  if (!fs.existsSync(apkPath)) {
    die(`未找到 release APK: ${apkPath}`);
  }
  fs.mkdirSync(RELEASE_APK_DIR, { recursive: true });
  const dest = path.join(RELEASE_APK_DIR, releaseDestName());
  fs.copyFileSync(apkPath, dest);
  console.log(`\nRelease APK: ${dest}`);
  return dest;
}

function pcRoot() {
  return path.join(ROOT, '..');
}

/** 把 android/release 最新 APK 拷到 ScreenKit net48 与精简包 apk/ */
function copyApkToPcExe() {
  const root = pcRoot();
  const py = path.join(root, 'scripts', 'copy-latest-apk.py');
  const net48 = path.join(root, 'ScreenKit', 'bin', 'Release', 'net48');
  const slim = path.join(root, 'ScreenKit', 'bin', 'Release', 'ScreenKit');
  if (!fs.existsSync(py)) {
    console.warn(`[release] 未找到 ${py}，跳过复制到 exe 目录`);
    return;
  }
  console.log('[release] 复制 APK 到 ScreenKit 输出目录…');
  run('python', ['-X', 'utf8', py, net48, slim], { cwd: root });
}

function compileScreenKit() {
  const root = pcRoot();
  console.log('[release] 编译 ScreenKit…');
  run('slx', ['ScreenKit'], { cwd: root });
}

function releaseApk() {
  build('release');
  copyReleaseApk(getApkPath('release'));
  copyApkToPcExe();
  compileScreenKit();
}

function apk() {
  const apkPath = ensureReleaseApk(true);
  return copyReleaseApk(apkPath);
}

function clean() {
  gradle('clean');
}

function rebuild(variant) {
  clean();
  build(variant);
}

function runOnDevice(deviceSerial) {
  const serial = resolveDevice(deviceSerial);
  console.log(`目标设备: ${serial}`);
  gradle('assembleDebug');
  const apkPath = getApkPath('debug');
  if (!fs.existsSync(apkPath)) {
    die(`未找到 debug APK: ${apkPath}`);
  }
  adbInstallApk(apkPath, serial);
  adb(['shell', 'am', 'start', '-n', MAIN_ACTIVITY], serial);
  console.log(`\n已安装并启动 debug 版 ${APP_ID}。`);
}

/**
 * release：按源码时间决定是否编译，再 adb install -r 并启动
 */
function installRelease(deviceSerial, force) {
  const apkPath = ensureReleaseApk(force);
  const serial = resolveDevice(deviceSerial);
  console.log(`[install] 目标设备: ${serial}`);
  adbInstallApk(apkPath, serial);
  console.log(`[install] 启动 ${MAIN_ACTIVITY}`);
  adb(['shell', 'am', 'start', '-n', MAIN_ACTIVITY], serial);
  console.log('[install] 完成');
}

function listDevices() {
  const output = runCapture('adb', ['devices', '-l']);
  console.log(output.trim() || '无设备');
}

const { command, variant, deviceSerial, force } = parseArgs(process.argv.slice(2));

if (!command) {
  console.log(HELP.trim());
  process.exit(0);
}

switch (command) {
  case 'build':
    build(variant);
    break;
  case 'release':
    releaseApk();
    break;
  case 'rebuild':
    rebuild(variant);
    break;
  case 'clean':
    clean();
    break;
  case 'run':
    runOnDevice(deviceSerial);
    break;
  case 'install':
    installRelease(deviceSerial, force);
    break;
  case 'apk':
    apk();
    break;
  case 'devices':
    listDevices();
    break;
  default:
    die(`未知命令: ${command}\n${HELP.trim()}`);
}
