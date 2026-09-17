#!/usr/bin/env node
'use strict';

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = __dirname;
const isWindows = process.platform === 'win32';
const gradleWrapper = path.join(root, isWindows ? 'gradlew.bat' : 'gradlew');
const appId = 'com.whj.screenkit';
const mainActivity = `${appId}/.MainActivity`;

function run(command, args, capture = false) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: capture ? 'utf8' : undefined,
    stdio: capture ? 'pipe' : 'inherit',
    shell: isWindows,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    if (capture) {
      process.stderr.write(result.stderr || '');
    }
    process.exit(result.status ?? 1);
  }
  return result.stdout || '';
}

function gradle(task) {
  if (!fs.existsSync(gradleWrapper)) {
    throw new Error(`未找到 Gradle Wrapper: ${gradleWrapper}`);
  }
  run(gradleWrapper, [task]);
}

function getDevice(serial) {
  const output = run('adb', ['devices'], true);
  const devices = output
    .split(/\r?\n/)
    .map((line) => line.trim().split(/\s+/))
    .filter(([id, state]) => id && state === 'device')
    .map(([id]) => id);
  if (serial) {
    if (!devices.includes(serial)) {
      throw new Error(`设备 ${serial} 未连接`);
    }
    return serial;
  }
  if (devices.length !== 1) {
    throw new Error(`需要连接一台设备，当前可用设备数：${devices.length}`);
  }
  return devices[0];
}

function adb(args, serial) {
  run('adb', ['-s', serial, ...args]);
}

function installOnDevice(variant, serial) {
  const device = getDevice(serial);
  const task = variant === 'release' ? 'assembleRelease' : 'assembleDebug';
  gradle(task);
  const apk = path.join(
    root,
    'app',
    'build',
    'outputs',
    'apk',
    variant,
    `app-${variant}.apk`,
  );
  adb(['install', '-r', apk], device);
  adb(['shell', 'am', 'start', '-n', mainActivity], device);
}

function parseSerial(args) {
  const index = args.indexOf('-s');
  if (index < 0) {
    return undefined;
  }
  if (!args[index + 1]) {
    throw new Error('缺少 -s 参数的设备序列号');
  }
  return args[index + 1];
}

function main() {
  const args = process.argv.slice(2);
  const command = args[0];
  switch (command) {
    case 'build':
      gradle(args.includes('--debug') ? 'assembleDebug' : 'assembleRelease');
      break;
    case 'clean':
      gradle('clean');
      break;
    case 'run':
      installOnDevice('debug', parseSerial(args));
      break;
    case 'install':
      installOnDevice('release', parseSerial(args));
      break;
    case 'devices':
      run('adb', ['devices', '-l']);
      break;
    default:
      console.log('用法: node build.js <build|clean|run|install|devices> [--debug] [-s serial]');
  }
}

try {
  main();
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
