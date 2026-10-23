# 自动找星直接传送基线修复与安装（2026-10-19）

## 现象与根因

扫描（SCAN）运行中点击“直接传送”时，页面提示正在停止挂机并等待清理，清理完成后服务写入一次性 `teleport` 命令并启动新 worker，但会话一直停在只读扫描，从未发送地图跳转。

根因在 Lua 启动路径：新会话的 `freshState` 固定从 `mode='scan'` 开始；`refreshCommand` 把与 `baselineSequence` 相同的首个命令视为 `unchanged`，不会调用 `acceptCommand`。因此“先停扫描、再以 `teleport` 作为基线命令启动”的会话从未进入传送模式。运行中收到新序号命令的路径本来就会调用 `acceptCommand`，不受影响。

设备现场（安装前）直接读到同一现象：`files/modules/starhunt/command` 为 `sequence=613 / mode=teleport / levels= / map=5205`，`status` 为 `status=reading`、`acceptedSequence=613`、`teleports=0`、`calls=0`，会话确实停在扫描且从未跳图。

## 修改

- 仅修改 `app/src/main/assets/starhunt.lua.in`：启动时若基线命令为 `teleport`，显式调用 `acceptCommand` 应用传送模式，随后由既有 `teleportStep` 发送并与停止/租约/身份/战斗/队伍检查共用同一路径。
- 基线为 `run` 的旧命令仍不会重放；未改 Java、Rust、命令格式、协议号或任何保护阈值。
- `boss` 模块未修改；其同类基线路径不在本次授权范围。

## 验证

- 新增 `tests/starhunt-teleport-baseline.lua`，并在 `tests/starhunt-run-state-machine.ps1` 中用独立 setup 以 `sequence=10/mode=teleport/map=1007` 作为首命令启动。
- 红测：去掉修复块后场景失败于 `fresh teleport baseline stayed in scan`，基线停在 scan、跳图发送 0 次。
- 绿测：修复后同一场景进入 `teleport`，首个调度拍发送 1 次 22568，目标坐标取地图起点；切换地图后状态为 `stopped/teleported`，序号保持 10，清理标志与移除回调检查通过。
- `starhunt-run-state-machine.ps1` 正式 12 组全部通过（含原 11 组）；`verify-starhunt-native-command.ps1` 原生命令解析自测通过。

## 构建与安装

- 构建环境：JDK 17（`WpeMobileTooling\jdk\jdk-17.0.20+8`）、Android SDK/NDK 27、cached Gradle 8.7；命令为 `gradlew.bat --offline :app:assembleDebug -PpiaomiaoLicenseBypass=true -PpiaomiaoVersionCode=20 -PpiaomiaoVersionName=2026.10.19.2-friends`，构建成功。
- 候选：`artifacts/PiaomiaoHelper-2026.10.19.2-friends-teleport-baseline.apk`，67,924,042 字节，SHA-256 `0905B90EAA73A0BDB6C6464A471350EC7B920652E3A240BFC1940C557C9D173E`。
- 签名：v1/v2 通过，Android Debug 证书 SHA-256 `9f4085bc10118b0279dd3dcecceba16858e50c2691b525701891299534245826`，与设备上一版一致。
- 包内检查：`assets/starhunt.lua.in` 与 `lib/x86_64/libstarhunt_worker.so` 均包含本次修复标记，说明 UI 资源与原生嵌入 Lua 都已更新。
- 安装前先把卡住的 613 会话按现有停止流程清理：`status=stopped`、`reason=worker_stop`、`hooksRemoved/bindingCleared=true`，`stop_read.state` 为 `phase=complete`、`actionSlotHeld=false`，root worker 进程退出。
- `adb install -r` 到 emulator-5554 成功；安装后 `versionCode=20`、`versionName=2026.10.19.2-friends`，设备 `base.apk` 拉回后 SHA-256 与候选完全一致。安装后应用进程未自动启动，崩溃缓冲区无本应用记录。

## 安装后实测与场景限制

- 安装后用户在一战到底（map 10000）点击直接传送，命令为 `sequence=637 / mode=teleport / map=1000`。
- 新 worker 已接受该命令并进入传送（`acceptedSequence=637`），但状态为 `waiting_role`，`reason=team_operation_restricted_or_unknown`，`teleports=0`，没有发送 22568。
- 该场景中游戏自身的 `MapMgr.jumpMap` 同样检查 `canNotOperationTeam` 并提示“请与Npc对话后离开该场景”；模块的发送前检查与游戏一致，属于场景限制，不是基线传送缺陷。
- 随后只读排查该地图：`view.m_NPC`、`manager.m_DynamicNPC`、`manager.m_CurMapFarNpc` 均为 0；`m_Teleporters`、`m_MapTx`、`m_DynamicTrans`、`m_DynamicMapTx`、`m_CurMapFarMapTx` 也均为 0。地图字段为 `ispkmap=1`、`fuben=0`、`caneasyleave=1`、`startPos=[31,22]`。即没有隐藏 NPC 或场景内传送点，离开应走活动/系统 UI（`caneasyleave`），不是场景对象。证据见 `.scratch/hidden-npc-20261019/README.md`。
- 已按停止流程结束该会话：`status=stopped`、`reason=worker_stop`、清理标志 true、`stop_read.state=complete`、worker 退出。

## 到达后队伍限制误判修复（v21）

- 现场：从帮战 3001 直接传送比武场 4154，命令 `sequence=645 / mode=teleport / map=4154`，状态 `teleports=1`、`map=4154 比武场`、`acceptedSequence=645`，但停在 `waiting_role` / `team_operation_restricted_or_unknown`；角色落在比武场起点 (20,15) 边缘。
- 根因：`teleportStep` 在确认“是否已到目标地图”之前先检查 `canNotOperationTeam`；比武场等 PK 场景该值为 true/未知，导致已到达的传送会话永远不结束。跳图本身已经成功，不是发送失败。
- 修复：把“已到目标地图 -> `finish('teleported')`”移到战斗/网络/队伍限制检查之前；这些限制只影响“再发一次跳图”，不应阻止已落地确认。
- 回归：扩展 `tests/starhunt-teleport-baseline.lua`，发送时 `canNotOperationTeam=false`，切图前改为 true；未修复时红测停在 `waiting_role/team_operation_restricted_or_unknown`，修复后绿测 `stopped/teleported`。正式 12 组 Lua 场景全部通过。
- 构建/安装：versionCode 21、2026.10.19.3-friends，v1/v2 签名，APK SHA-256 `0C0F1A90A3180C2FFD82F905D926C4393D8D3CF707E013AFF22E67C27A7F6024`，`adb install -r` 成功，设备 base.apk 哈希一致。

## 比武场中心落点（v22）

- 地图 4154 比武场：`gridNum=[121,90]`、`gridSize=[20,20]`、`width=2412`、`height=1791`，`startPos=[20,15]`；直接传送原落点 (20,15) 在地图边缘。中心网格为 (60,45)。
- 修改：仅在 `CENTER_LANDING_MAPS={[4154]=true}` 的地图使用中心落点；中心由 `m_ColCount/m_RowCount` 计算，先用 `getPosCanGo`/`getGridIsOpaque` 在中心 6 格半径内找可走格，找不到则回退到中心；其他地图仍用 `startPos`，找星挑战的 NPC 坐标不受影响。
- 测试：新增 `tests/starhunt-teleport-center.lua`，目标 4154、中心 (60,45)；断言发送坐标 (60,45) 且到达后 `stopped/teleported`；正式 13 组 Lua 全部通过。
- 构建/安装：versionCode 22、2026.10.19.4-friends，APK SHA-256 `6DE300A10F4EAFA6D97D35F26E638FD84577E273A193BCCF293A2E779A4BE6EA`，`adb install -r` 成功，设备 base.apk 哈希一致。
- 尚未从其他地图重新传送到 4154 观察服务器是否接受 (60,45) 落点；若中心被场景阻挡，需按实际可走格调整。

## 未验证

- 尚未从其他地图重新传送到 4154 验证 v21 落地确认和 v22 中心落点；真实服务器接受/落点可走性仍未确认。
- 本机现在有 JDK 17，但依赖 Java 的旧契约脚本未在本轮重跑；本次未改 Java 源码。
- 未修改 `boss` 模块，也未验证其同类“直接传送”路径。

## v23 中心落点与 3003 实测

- v23 将 3003、4154 都加入 `CENTER_LANDING_MAPS`，并支持“同图但不在中心时再发一次中心跳转”；正式 14 组 Lua 通过。
- 安装：versionCode 23、2026.10.19.5-friends，APK SHA-256 `0D2E8127074AE950841FD3ADD6AD4809E063DFC0AA84D5629FBDB69E412B8FD4`，设备 base.apk 哈希一致。
- 从 3003 内实测同图中心跳转（3003→3003）和跨图中心跳转（3003→4154）：均为 `teleports=1`，但地图仍为 3003，状态停在 `teleport_wait`。这说明 **3003 作为源地图拒绝向外跳图**（与帮战类似），不能据此判定中心坐标无效。
- 尚未从外部地图传入 3003/4154 验证中心落点；需先通过水陆督军/活动/离开入口离开 3003，再从外部传送回来。
- 两次卡住的会话均已停止清理（`stopped/worker_stop`、清理 true、worker 退出）。

## 直接传送同名地图显示 ID（v24）

- 地图目录 83 条中有 12 组同名：玩家之家 x6（4001、5201-5205）、帮派 x3（3002、3021、3022）、比武场 x2（3003、4154）、大雁塔各层 x2（1101/1111…1107/1117）、一战到底 x2（4421、10000）等。
- 原“直接传送”选择框只显示 `map.name`，同名项无法区分；v24 对同名项追加 ID，显示“比武场 (3003)”“比武场 (4154)”等；唯一名称保持原样，传送目标仍是地图 ID。
- 仅改 `starhunt/MainActivity.showTeleportPicker`；未改地图目录、未按名字去重，巡回地图选择器未改。
- 安装：versionCode 24、2026.10.19.6-friends，APK SHA-256 `D665501ACC7A7DAA4A5F181C5B9C2358075F9B6B7B4739FA48FF19F0D2B4DE14`，设备 base.apk 哈希一致；DEX 已确认包含新逻辑。未实机打开弹窗截图验收。

## 巡回地图选择器同名显示 ID（v25）

- 与 v24 直接传送一致，巡回地图多选弹窗对同名项追加 ID，例如“比武场 (3003)”“比武场 (4154)”；唯一名称保持原样，勾选保存的仍是地图 ID。
- 仅改 `starhunt/MainActivity.showCycleMapPicker`；未改地图目录、去重、巡回顺序或发送逻辑。
- 安装：versionCode 25、2026.10.19.7-friends，APK SHA-256 `B818DFABA4E28E7AAE1569D25BB835CCDBE76A7BBB46FC2DF89CA081E377848B`，设备 base.apk 哈希一致。未实机打开弹窗截图验收。

## 停止记录自动恢复（v26）

- 新增 `StarHuntStopRecovery.shouldRecover`：仅当 journal 非 complete、**无 worker**、status 明确 `gameProcessGone`（`status=stopped` + `sessionDetached/original_process_ended_cleanup_unconfirmed`），且 `status.acceptedSequence >= journal.sequence` 时恢复；活着的游戏/worker 仍保留手动重试。
- `StarHuntService.recoverStopJournalIfProcessGone` 在 `restoreStopJournal`/`startReading` 调用：删除 `stop_read.state` 和残留 `stop`、清 pending/retryable/active、释放动作位；`hasRetryableStop` 对可恢复记录返回 false，页面不再显示“重试停止”。
- 新增 `StarHuntStopRecoverySelfTest`（10 断言），starhunt Java 模型自测全部通过；模拟旧 journal+status 后页面显示“开始挂机”，启动读取会删除 journal 并启动 worker。
- 安装：versionCode 26、2026.10.19.8-friends，APK SHA-256 `0A6C4366A93DEB5BEEFD1933C428FE0D132E08D5E82B34F79037BD8EB93638F1`，设备 base.apk 哈希一致。
- 测试读取时 App 在前台导致 Frida attach 超时（status=fatal、cleanup=true）；worker 已退出、journal 为 complete，这是测试环境现象，不影响自动恢复结论。

## BossHunt 停止记录自动恢复（v27）

- 现象：点“自动BOSS”侧边栏、未点开始，页面自动发 `BossHunt STOP_READ` 并跳到游戏。原因：boss 模块残留 `stop_read.state phase=pending sequence=34 actionSlotHeld=true`，`status=stopped reason=original_process_ended_cleanup_unconfirmed acceptedSequence=33 sessionDetached=true hooksRemoved/bindingCleared=false`；`BossHuntMainActivity.refreshPage` 对 `hasRetryableStop` 自动 dispatch STOP_READ，而 `BossHuntService` 的 STOP_READ 会 `GameForeground.bringToFront`。
- 修复：新增 `BossHuntStopRecovery`（journal 非 complete、无 worker、status 明确 `gameProcessGone` 即可恢复；停止序号可比最后 status 大 1）；`BossHuntService.hasRetryableStop` 对可恢复记录返回 false（页面不再自动 STOP_READ），`restoreStopJournal`/`startReading` 删除 journal、残留 stop 并释放动作位；`StarHuntStopRecovery` 同步去掉“序号必须已被 status 接受”的限制。
- 测试：`BossHuntStopRecoverySelfTest` 9 断言、`StarHuntStopRecoverySelfTest` 10 断言，两套 Java 模型通过。真机 emulator-5554：点“自动BOSS”后 top activity 仍是 `com.xnas.wpe.suite`，无 STOP_READ/游戏启动日志，页面显示“未开始/开始巡回”。
- 安装：versionCode 27、2026.10.19.9-friends，APK SHA-256 `22DD235252E1B5E93465F6092AE4E389DE487B0374795F52E17E3A4013FED263`，设备 base.apk 哈希一致。
- 残留 boss journal 仍在；开始巡回时由恢复逻辑清除；页面打开不再自动跳游戏。

## 定时任务显示模块预设（v29）

- 任务列表的“启动”卡片增加预设摘要；编辑弹窗目标列表下增加实时摘要和提示“预设以各模块页面保存的选择为准；启动时按这里显示的星级/地图执行”。
- 自动找星：读取 `selected_levels_v1`/`selected_level` 和 `cycle_maps_v1`；本机显示“自动找星：8星、9星 · 巡回 东海渔村、长安”。
- 自动BOSS：读取 `selected_maps_v1`；本机显示“自动BOSS：地图 地府”。
- 多目标用“；”分隔，模块内星级/地图用“、”；只读展示，任务本身不保存星级/地图，实际启动仍按模块页面当前保存值。
- 验证：模拟器 emulator-5554 实际打开任务列表和编辑弹窗；列表显示上述摘要；编辑弹窗勾选自动BOSS后实时变为“自动找星：8星、9星 · 巡回 东海渔村、长安；自动BOSS：地图 地府”，取消未保存。
- 安装：versionCode 29、2026.10.19.11-friends，APK SHA-256 `D97F236BEAD91213DFB93CFF15AA2AD8815D7301D8B0E9A6FF48183A6428B813`，设备 base.apk 哈希一致。
