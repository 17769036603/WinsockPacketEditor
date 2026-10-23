# 近处场景NPC漏检修复（2026-10-16）

## 实战结果与根因

用户授权当前地图1星最多30秒实战。绑定 PID26874 / startTicks4073095，读取序号7后唯一开始序号8/run。时间线在20:47记录14次传送调用、0次挑战，反复出现位置确认后 `target_instance_missing`，继而再次传送。在30秒上限内停止，末态 `worker_stop`、hooksRemoved/bindingCleared均true，worker退出。调用次数不等于服务器接受次数，不能把本次结果报告为实战通过。

后续只读检查业务发送0，临时部署文件已清理，确认动态条目为table，而近处 `MapView.m_NPC` 条目为userdata，其m_NpcInfo仍是table，包含有效的同ID/称号/地图/坐标。旧扫描只接受table对象，因而漏掉已转入近处集合的目标。现场快照有7个远处地狗星和1个近处地狗星，旧统计也会漏掉近处对象。

真实Lua离线红测同时覆盖了两条相关路径：匹配位置后目标短暂消失可在同序号下复现14次传送0挑战；目标从动态table转入真实userdata场景对象时，原代码错误进入target_instance_missing，挑战为0。此前“完全没有位置确认时只发一次”的测试不覆盖这些路径。

## 修复范围

仅改 `assets/starhunt.lua.in`、新增正式测试 `starhunt-scene-userdata.lua` / `starhunt-challenge-rebind.lua` 和既有runner。保留用户当前UI，不改Java、native或发送schema。扫描安全访问table/userdata的m_NpcInfo/getIsFight，按ID去重并保留场景对象。传送确认后挑战前目标暂缺时，保留原目标及位置，最多等待3秒；同ID同坐标且玩家仍在确认位置才挑战。持续缺失、位置变化、玩家离位或无效数据进入unknown，不再次传送。停止、新命令、租约、角色地图保护仍适用。

## 交付与验证

- 已安装 `../artifacts/PiaomiaoHelper-2026.10.16.4-scene-npc-fix.apk`，code10 / `2026.10.16.4-starhunt-candidate`，x86_64；未发布。SHA-256：`C93826AF68141BC71990AF79405DD34058DBBBE9AEE0DA9456CD26E432B1EC30`。
- 精确userdata红测修复后通过：1次传送、1次挑战，参数保持原样；正式六组Lua场景全通过，含userdata、去重、busy、暂缺恢复、超时、坐标变化、玩家离位与无效目标。
- 完整离线shared APK构建、v1/v2签名、版本ABI资源核验通过；打包Lua SHA-256 `2483241701a6e4dedfad92d7da39ac9d1278d40002bb3f5076016467f47c855a` 与测试源码一致，授权地址已配置，所有app源码输入在构建期间稳定。
- CodeRabbit实际完成本轮4文件审查，0 issues。计划判断API HTTP200/REFUSAL、结果API HTTP200/LOW_CONFIDENCE，均为not_run，不视为判断通过；主代理根据复现、测试和审查证据确认候选边界。
- 修复包只读验证：序号9/scan，21:05大唐边境1007，地狗星1星7个，calls/teleports/challenges均0。此数量与较早快照处于不同时间，不能用8到7的变化证明修复效果。停止序号10、双清理标志true、worker退出；最后留在助手自动找星页面，读取和动作均停止。
- 修复后真实传送挑战尚未执行，不声称已开战或连续循环验收通过。操作时需返回游戏前台，后台暂停时界面保留最后快照。

证据目录：`F:\项目目录\封包监听工具\.scratch\starhunt-feature-20261016`，含live-timeline.txt、live-session-result.json、npc-shapes-result.txt、npc-shapes-cleanup.json、offline-repro/、scene-fix-build.log、coderabbit-scene-fix-review.ndjson、scene-fixed-reading.png、scene-fixed-reading-status.txt、scene-fixed-stopped-status.txt。

## 21:14 修复后单场实战复测

- 用户授权保留21:11新版UI、补齐授权地址并覆盖安装后继续最多30秒复测。安装包 `PiaomiaoHelper-2026.10.16.4-ui-retest-configured.apk`，SHA-256 `17EDE22B948296EB6CAA42B0CBDBDA7D3370EE51317C1ADFD6A16233F5AA4254`；找星Lua与已审查修复一致。
- sequence11/scan读取大唐边境1007，1星地狗星5个，业务调用0；sequence12/run唯一开始，目标1463430、坐标(106,10)，1次传送、1次挑战，约2.75秒获得own_start_battle_confirmed。设备截图确认进入第1回合。
- 首场战斗确认即写停止标志，末态worker_stop、hooksRemoved/bindingCleared均true、worker退出，未强制退出战斗。战后继续、忙碌换目标、长期循环及其他模块共存尚未实战验收，未发布。证据：retest-timeline.txt、retest-final-status.txt、retest-final.png。

