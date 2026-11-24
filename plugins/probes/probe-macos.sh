#!/bin/sh
# DeskPal 探测脚本（macOS）：查看 QQ、微信等程序向辅助功能接口公开了什么界面结构。
#
# 只输出元素的角色、标识、类名、位置；名称和文字一律只输出首字和长度（例如「张…(2)」），
# 不输出聊天内容、联系人或群名。不联网。对 QQ 这类 Electron 程序，会把它的 AXManualAccessibility
# 设为开（让它公开界面结构，和打开“旁白”时一样），程序退出后自动恢复。
#
# 用法：
#   sh probe-macos.sh                         # 探测 QQ 和微信
#   sh probe-macos.sh com.example.app         # 探测别的程序（写 Bundle ID）
#   sh probe-macos.sh > probe.txt             # 保存结果，把 probe.txt 发给开发者
#
# 运行前：先打开要探测的程序并登录，切到一个聊天界面。
# 第一次运行时 macOS 会要求权限：
#   · 「系统设置 → 隐私与安全性 → 辅助功能」里允许你用的终端程序（终端、iTerm 等）；
#   · 弹窗询问是否允许终端控制“System Events”，选允许。
# 元素多时要一两分钟，进度显示在屏幕上，结果写到标准输出。

exec osascript -l JavaScript - "$@" <<'JXA'
ObjC.import('Foundation');

const MAX_NODES = 1200;
const MAX_DEPTH = 40;
const MAX_CHILDREN = 120;

function out(text) {
  const data = $(String(text) + '\n').dataUsingEncoding($.NSUTF8StringEncoding);
  $.NSFileHandle.fileHandleWithStandardOutput.writeData(data);
}

function mask(value) {
  if (value === null || value === undefined) return '';
  const text = String(value).trim();
  if (!text) return '';
  const chars = Array.from(text);
  return chars[0] + '…(' + chars.length + ')';
}

function safe(read) {
  try { return read(); } catch (e) { return undefined; }
}

function attr(element, name) {
  return safe(() => element.attributes.byName(name).value());
}

function describe(element, depth) {
  const parts = [];
  parts.push(safe(() => element.role()) || '?');
  const subrole = attr(element, 'AXSubrole');
  if (subrole) parts.push('sub=' + subrole);
  const identifier = attr(element, 'AXIdentifier');
  if (identifier) parts.push('id=' + identifier);
  const domId = attr(element, 'AXDOMIdentifier');
  if (domId) parts.push('domid=' + domId);
  const domClass = attr(element, 'AXDOMClassList');
  if (domClass && domClass.length) parts.push('class=' + [].concat(domClass).join(' '));
  const name = mask(safe(() => element.name()));
  if (name) parts.push('name=' + name);
  const description = mask(attr(element, 'AXDescription'));
  if (description) parts.push('desc=' + description);
  const value = attr(element, 'AXValue');
  if (typeof value === 'string' && value.trim()) parts.push('value=' + mask(value));
  if (attr(element, 'AXFocused') === true) parts.push('focused');
  const position = safe(() => element.position());
  const size = safe(() => element.size());
  if (position && size && size[0] > 0 && size[1] > 0) {
    parts.push('rect=' + Math.round(position[0]) + ',' + Math.round(position[1]) + ',' + Math.round(size[0]) + ',' + Math.round(size[1]));
  }
  return '  '.repeat(Math.min(depth, 30)) + parts.join(' ');
}

function walk(root) {
  let count = 0;
  const stack = [[root, 0]];
  while (stack.length && count < MAX_NODES) {
    const [element, depth] = stack.pop();
    out(describe(element, depth));
    count++;
    if (count % 100 === 0) console.log('已读 ' + count + ' 个元素…');
    if (depth >= MAX_DEPTH) continue;
    const children = safe(() => element.uiElements()) || [];
    if (children.length > MAX_CHILDREN) {
      out('  '.repeat(Math.min(depth + 1, 30)) + '…还有 ' + (children.length - MAX_CHILDREN) + ' 个子元素没有列出');
    }
    const kept = children.slice(0, MAX_CHILDREN);
    for (let i = kept.length - 1; i >= 0; i--) stack.push([kept[i], depth + 1]);
  }
  if (stack.length) out('…达到 ' + MAX_NODES + ' 个元素的上限，后面没有列出');
  return count;
}

function run(argv) {
  const targets = argv.length ? argv : ['com.tencent.qq', 'com.tencent.xinWeChat'];
  const self = Application.currentApplication();
  self.includeStandardAdditions = true;

  out('DeskPal 探测（macOS）。名称只显示首字和长度。');
  out('');
  out('== 环境');
  out('系统=macOS ' + safe(() => self.doShellScript('sw_vers -productVersion')) + ' ' + safe(() => self.doShellScript('uname -m')));

  const events = Application('System Events');
  const allowed = safe(() => events.uiElementsEnabled());
  out('运行这个脚本的终端有辅助功能权限=' + allowed);
  if (!allowed) {
    out('请在「系统设置 → 隐私与安全性 → 辅助功能」里允许你用的终端程序，然后重新运行。');
    return;
  }

  let readSomething = false;
  for (const id of targets) {
    out('');
    out('== ' + id);
    const found = safe(() => events.processes.whose({ bundleIdentifier: id })()) || [];
    if (!found.length) {
      out('没有在运行。请先打开并登录，切到一个聊天。');
      continue;
    }

    const proc = found[0];
    out('进程名=' + safe(() => proc.name()) + ' pid=' + safe(() => proc.unixId()) + ' 在前台=' + safe(() => proc.frontmost())
      + '（DeskPal 在 macOS 上把它记作 ' + id + '）');
    const manual = safe(() => { proc.attributes.byName('AXManualAccessibility').value = true; return true; });
    out('AXManualAccessibility：' + (manual ? '已打开' : '没有这个属性或打不开（不是 Electron 程序时正常）'));
    delay(2);

    const windows = safe(() => proc.windows()) || [];
    out('窗口数=' + windows.length);
    windows.forEach((win, index) => {
      out('');
      out('-- 窗口 ' + index + ' 标题=' + mask(safe(() => win.name())) + ' 主窗口=' + attr(win, 'AXMain') + ' 有焦点=' + attr(win, 'AXFocused'));
      const count = walk(win);
      out('共 ' + count + ' 个元素');
      if (count > 20) readSomething = true;
    });
  }

  out('');
  out('== 下一步');
  if (readSomething) {
    out('已经读到界面结构。把这份输出发给开发者，他会据此找到会话名称所在的元素。');
  } else {
    out('没有读到足够的界面结构。可以打开“旁白”（Command-F5）后切到聊天界面，再运行一次；运行完用 Command-F5 关掉。');
  }
}
JXA
