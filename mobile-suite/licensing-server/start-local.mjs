import { randomBytes } from 'node:crypto';
import { spawn } from 'node:child_process';
import { createLicensingServer } from './server.mjs';

if (process.versions.node.split('.')[0] < 24) {
  throw new Error(`Node.js 24 or newer is required. Found ${process.versions.node}.`);
}
if (process.platform !== 'win32') {
  throw new Error('This launcher is for Windows. Start the service with `pnpm start` on other platforms.');
}

let generatedToken = false;
if (!process.env.PIAOMIAO_ADMIN_TOKEN?.trim()) {
  process.env.PIAOMIAO_ADMIN_TOKEN = randomBytes(32).toString('hex');
  generatedToken = true;
}

const port = process.env.PORT ? Number(process.env.PORT) : 8787;
if (!Number.isInteger(port) || port < 1 || port > 65_535) throw new Error('PORT must be between 1 and 65535.');
const url = `http://127.0.0.1:${port}/`;
const app = createLicensingServer();

app.server.once('close', app.close);
await new Promise((resolve, reject) => {
  app.server.once('error', reject);
  app.server.listen(port, '127.0.0.1', resolve);
});

if (generatedToken) {
  console.log('临时管理令牌（仅显示在此窗口，请复制并粘贴到管理页面）：');
  console.log(process.env.PIAOMIAO_ADMIN_TOKEN);
} else {
  console.log('正在使用启动前已设置的 PIAOMIAO_ADMIN_TOKEN。');
}
console.log(`服务已就绪，管理页面：${url}`);
console.log('请将令牌粘贴到页面。按 Ctrl+C 停止服务；如果浏览器没有自动打开，请复制上方地址手动打开。');

const browser = spawn('explorer.exe', [url], { detached: true, stdio: 'ignore', windowsHide: true, shell: false });
browser.once('error', error => console.error(`浏览器未能自动打开（${error.message}），请复制上方地址手动打开：${url}`));
browser.unref();

let stopping = false;
const stop = () => {
  if (stopping) return;
  stopping = true;
  app.server.close();
};
process.once('SIGINT', stop);
process.once('SIGTERM', stop);
