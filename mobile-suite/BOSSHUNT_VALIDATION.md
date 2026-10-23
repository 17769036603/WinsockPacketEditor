# 自动 BOSS 候选验收记录

日期：2026-10-18。功能说明见 [BOSSHUNT.md](BOSSHUNT.md)。

## 候选包

- 路径：`artifacts/PiaomiaoHelper-2026.10.18-bosshunt-r1.apk`
- 大小：67,860,558 bytes。
- SHA-256：`CC7C9376D9FB0067B5E61B03B63C2BA79D0D8C2BB9D3540EA7DF48BABE77FD3B`。
- 版本：code14，`2026.10.18.1-friends-open-v1`，沿用已存在的朋友免激活构建参数。
- ABI：x86_64；minSdk23；11个原生库，包含独立 `libbosshunt_worker.so`。
- Android Debug证书SHA-256：`9f4085bc10118b0279dd3dcecceba16858e50c2691b525701891299534245826`；v1/v2验证通过。

## 已完成检查

1. Java编译与完整离线 `assembleDebug` 成功。
2. Android Rust release离线编译成功；共享授权模块保留未使用函数与clang `-pthread` 警告。
3. 使用已有lua54.dll只编译Lua语法，没有执行游戏脚本。
4. 包内Lua asset、原生worker中嵌入的Lua与最终修复源码一致。
5. 构建采用独立源码快照，79个app输入文件；源码及输入清单保存在项目根 `.scratch/bosshunt-20261018/`。当前仓库存在其他会话并行改动，候选包含快照时的共享源码，不把那些页面变化归为BOSS开发。

离线部分只做编译与打包检查；编译检查不替代运行行为验证。

## 已授权的设备界面与保存验收

用户明确回复“覆盖安装，只检查界面和保存”后，已覆盖安装至 `emulator-5554`，安装后的base.apk哈希与上述候选一致。逐个点击五个地图按钮，确认名称与逻辑ID对应；五图全选、三图多选、全部取消分别跨强制关闭与重开保留。截图已目视检查点亮和未选外观，空选择时启动按钮不可用。最后恢复首次空选择，找星设置内容保持不变。

没有点击开始巡回，BOSS runtime目录没有产生command/status文件，也没有启动BOSS服务。崩溃缓冲无记录。自动化脚本与结果位于项目根 `.scratch/bosshunt-20261018/verify-device-ui.ps1`、`device-ui-validation.json`；截图包括 `device-boss-all-reopened.png`、`device-boss-multi-reopened.png`、`device-boss-empty-reopened.png`。

## 代码审查

CodeRabbit v0.9.0首轮 `review_completed`，共6 issues。BOSS模块1项major：未知或歧义NPC按钮导致暂停时，移除观察器会清空 `s.useOriginal`，随后回调尾部再次调用它可能出错。已改为创建回调时捕获局部 `original` 并始终转发；修复后重新完成Lua语法、原生编译与打包检查。

其余5项minor涉及并行旧页面移除状态提示区域：暗雷、战斗跳过、战斗面板、战斗快进、角色速度。这些不属于本次BOSS实现，未修改，不能将全仓库审查记为零问题。

修复复审返回 `rate_limit`：免费CLI额度已耗尽，提示等待17minutes。没有自动购买额度，也没有替代审查；最终修复尚未通过CodeRabbit复审。原始记录：项目根 `.scratch/bosshunt-20261018/review.ndjson`、`review-fix.ndjson`。

计划和结果判断均实际调用Decisions API，HTTP200返回LOW_CONFIDENCE、`status=not_run`，不计为判断通过。

## 尚未完成

没有启动BOSS服务、连接游戏、发送传送或NPC请求。服务器接受跨地图传送、实际NPC按钮、完整战斗循环和停止时延均待实机验收。此前服务器无法进入期间，前一个任务已授权的只读扫描计时也没有执行。

## 临时文件清理

工具策略拒绝删除已经核对为空的 `app/src/main/java/com/xnas/wpe/bosshunt/bosshunt-hold/` 目录，返回 `blocked by policy`。已保留该空目录，没有改用其他方式绕过；不参与APK内容。
