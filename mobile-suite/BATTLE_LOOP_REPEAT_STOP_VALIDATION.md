# 战斗跳过重复停止修复记录

日期：2026-10-17。

## 现场证据

当前模拟器的模块状态为 `state=stopping`、`reason=app_lease_expired`，原生 worker 仍在运行。对应同一次运行的游戏结果已是 `state/status=stopped`、`hooksRemoved=true`、`bindingCleared=true`、`reason=nothing_owned`，但没有 `restoreConfirmed`。这些读取只用于诊断，没有发送装备或战斗请求。

Lua 停止入口在没有自身状态时会重写 `nothing_owned` 结果，遗漏恢复确认字段。离线回归先执行真实模板清理并断言完整终态，再执行第二次停止；旧代码以 `repeated stop overwrote the complete cleanup receipt` 失败，证明重复停止会破坏已经存在的确认结果。现场已经丢失的字段无法据此追溯补出。

## 修复边界

无自身状态的重复停止只允许保留同一 `runId`、精确匹配 `state/status=stopped` 且 `hooksRemoved/bindingCleared/restoreConfirmed=true` 的完整结果。关键字段重复、跨运行、不完整或读取失败均不能充当清理完成证明。不放宽原生 worker、冲突保护或服务的终态条件。

本次不改变恢复请求次数、战斗操作、GL 排队或租约时长。当前旧运行的恢复，以及覆盖安装后的页面验收仍需单独完成；不得把离线回归或 APK 构建视为设备验收。

## 验证

- 红测：`powershell -NoProfile -ExecutionPolicy Bypass -File tests/battle-loop-sandbox/repeat-stop.ps1 -Scenario completed-run`，已复现第二次停止覆盖完整清理结果。
- 修复后六个离线场景通过：真实清理后重复停止、同运行完整收据、跨运行收据、不完整收据、无收据、重复关键字段。原有 20 个 Lua 场景及状态契约通过；金蝉页面生产方法提取测试 34 项通过。
- 离线 Gradle 构建成功，构建前后两项生产输入哈希稳定；签名 v1/v2 验证通过。APK 中 Lua asset 和原生 worker 内嵌模板都与源码逐字节一致；授权服务地址存在，10 个 x86_64 原生库存在。
- 候选：`WinsockPacketEditor/artifacts/PiaomiaoHelper-2026.10.17-binding-cleanup-r8.apk`，SHA256：`2057D7DBF638E6923F4E8072DCDC8375BDB585618DC159F29622666DF94BD94B`。
- 计划和结果判断 API 均返回 LOW_CONFIDENCE/not_run，主代理依据现场证据和红绿回归接手，未作为判断通过。CodeRabbit 最终审查完成，覆盖两项生产修改及相关测试共 11 个文件，0 issues；前一轮金蝉审查的两项 minor 已修复并包含在本轮复审。
- 用户明确授权“允许安装，游戏我自己重启”后，于设备时间 2026-10-17 20:37:48 完成 `adb install -r`；设备 base.apk SHA256 与上述候选一致。安装前已对角色速度、金蝉连接和战斗跳过执行各自停止入口，角色速度及金蝉原生 worker 已退出，旧战斗跳过 worker 仍等待旧清理证明，未强行清除保护。
- 安装后页面显示“已授权”，角色速度运行标记已消失；旧战斗跳过仍显示“清理中”，符合尚未重启的旧游戏进程状态。截图：根目录 `.scratch/helper-r8-installed.png`。未启动新战斗任务。
- 游戏由用户自行重启，尚未完成；旧现场恢复与下一次真实清理闭环尚未验收。
