# 自动找星读取恢复验证（2026-10-16 20:40）

当前交付为 `../artifacts/PiaomiaoHelper-2026.10.16.4-ui-read-rebind.apk`，包名和版本仍为 `com.xnas.wpe.suite` / code10 / `2026.10.16.4-starhunt-candidate`，签名 v1/v2 验证通过。SHA-256：`3EFEE33812C3585859C16E864A0ECBEA64050BCB02497FDE000766EB6D799E27`。已安装 emulator-5554，未发布。

本轮保留用户当前 UI。生产源码仅修改 `StarHuntService.java`、`StarHuntStatus.java`，并增加既有 `StarHuntStatusSelfTest.java` 的回归覆盖；没有修改 UI、Lua 发送流程或原生源码。此前 r3 的审查结论不扩大为用户后续全部 UI/原生改动的审查。

## 修复与验证

- 后续 UI 安装包缺失既有公开授权服务地址，开启读取被授权门禁拒绝。使用正确的 `piaomiaoLicenseUrl` 参数重新构建并更新安装后，页面恢复显示授权有效。更新前安装包保留在任务证据目录，未读取或导出授权凭证。
- 旧状态明确报告 `stopped`、`original_process_ended_cleanup_unconfirmed`、`sessionDetached=true`，但 Java 仅接受双清理标志，导致新游戏进程无法扫描。设备点击已复现拒绝，回归先失败、修复后通过。新的读取准入接受明确清理，或明确旧进程结束且会话已分离的终态；没有把后者改写成 Lua 清理成功。新读取写入新的 scan，不重放旧 run。停止待确认与持久化重试的前置门禁保持不变。
- 状态模型40项断言、Java集成34项检查、完整离线 APK 构建通过。CodeRabbit 实际完成本轮3文件审查，`CodeRabbit raised 0 issues.`；交付前源码与审查快照校验一致。

## 设备结果

- 游戏身份 PID26874 / startTicks4073095，读取和停止期间未改变。旧命令为 sequence4/run，新读取为 sequence5/scan/level0/map1007，并确认序号5。
- 20:40“大唐边境”1007，`levels=1:8`、`levelNames=1:地狗星`；用户 UI 显示地狗星（星级1）8个。这是当时客户端地图缓存快照，不证明所有目标可被服务器接受挑战。
- 新会话 calls/teleports/challenges 始终均0；没有点击开始找星，没有真实传送或挑战。
- 停止命令 sequence6/scan，末态 `stopped/nothing_owned`、acceptedSequence6、hooksRemoved/bindingCleared均true，进程列表无找星 worker。最后留在助手自动找星页面，读取与动作均停止。游戏后台暂停时保留旧快照，返回游戏后才更新，连续后台刷新未确认。

## 旧手动测试与未确认边界

用户确认旧 sequence3/scan、sequence4/run 为手动点击，开始次数记不清。旧记录为6次传送调用、0次挑战，不能据此判定自动启动、实际服务器接受次数或重复发送根因。旧游戏进程随后结束，Lua 清理未确认与会话分离分开记录；旧计数不属于本轮只读会话。

真实 Lua 离线复现使用 baseline3/run4/level1/map1007，无65505和本地位置更新，模拟60秒，只传送一次，8秒后保持 unknown/teleport_confirmation_timeout；要求重复发送的断言按预期失败。未复现6次，也不据此声称现场没有问题，需要未来同一会话的命令、目标变更和位置确认时间线。连续传送挑战、忙碌轮换及与战投/面板共存仍未验收，本轮用户授权为安装、页面和只读验证。

证据目录：`F:\项目目录\封包监听工具\.scratch\starhunt-feature-20261016`。关键文件为 `read-rebind-red.png`、`rebind-reading.png`、`rebind-stopped.png`、`rebind-reading-command.txt`、`rebind-reading-status.txt`、`rebind-stopped-status.txt`、`rebind-build.log`、`coderabbit-rebind-review.ndjson`、`offline-repro/`。
