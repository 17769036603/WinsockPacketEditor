# 战斗快进 / 战斗跳过状态显示修复记录

日期：2026-10-17。

## 现场

- 战斗快进 worker 实际在运行（`libbattle_skip_worker.so`，向游戏进程写入 `running calls=N`），但快进页显示“无法确认上次任务是否退出，暂未启动。”，侧栏显示“未确认”。
- 互斥门 `.battle-conflict.guard` 归 `battleskip`，战斗跳过页却显示“状态：已停止”；点“启动”后提示只闪现约 0.25 秒，1 秒刷新又盖回“已停止”，`battleloop/status` 无变化。
- 两者都源于状态解析：页面只把 `state=...` 当作有效状态，worker 的单行 `running calls=N` 被当成“旧任务未确认”；战斗跳过页只看自己的干净停止记录，不看互斥门里另一功能的运行证据。

## 修改

- 新增 `com.xnas.wpe.suite.BattleWorkerStatus`：只识别 worker 实际写出的 `running calls=\d+`，并归一为 `state=running`；`state=...`、半截文本、`running` 无计数都返回 null（继续按未确认处理）。
- `battleskip/MainActivity`：运行状态优先于 recovery/门禁提示；`formatStatus` 与 `getModuleStatus` 补 `running` 映射（页面“运行中”，侧栏“运行中”）。
- `battleloop/MainActivity`：当互斥门被快进占用时返回持久的 `state=conflict_wait` 与原因（区分“正在运行”和“上次未确认退出”），不再被 1 秒刷新覆盖为“已停止”。
- 未修改互斥门判定、恢复/清理条件、worker 协议或原生代码。

## 验证

- `tests/BattleWorkerStatusSelfTest.java`：14 条断言通过。
- `tests/verify-battle-status-display.ps1`：抽取生产的 `battleskip.currentStatus/formatStatus` 与 `battleloop.currentStatus/display`，配合真实 `BattleConflictGate` 与临时状态目录，11 条断言通过。
- `tests/BattleConflictGateSelfTest.java`：既有场景通过。
- `gradlew.bat --offline assembleDebug`：BUILD SUCCESSFUL（21s）。
- 候选：`artifacts/PiaomiaoHelper-2026.10.17.2-status-display.apk`，67,058,908 bytes，versionCode 12 / `2026.10.17.2-friends`，x86_64，SHA-256 `8531AEFC08F3F944A8B2C4C251C1A5791258647CC1548D336651D51DD51864C1`；12 个 asset、10 个 x86_64 原生库、Debug 签名校验通过，dex 含新类与阻塞文案。
- 经用户明确授权于 2026-10-17 21:26:54 `adb install -r`；设备 base.apk SHA-256 与候选一致，授权保留。
- 设备验收：快进页“状态：运行中”、侧栏“战斗快进 ● 运行中”；战斗跳过页持续显示“战斗快进正在运行，已阻止启动战斗跳过；请先到‘战斗快进’页面停止快进，清理确认后再启动。”，侧栏“战斗跳过 ● 等待中”。截图见 `.scratch/battle-status-20261017/`。

## 边界

- CodeRabbit 审查未运行：CLI `auth status --agent` 返回 `not_authenticated`，`coderabbit auth login --agent` 的浏览器回调在超时后关闭（`automatic_login_failed`），需用户在本机终端执行 `coderabbit auth login`。未使用人工审查替代。
- 战斗循环 `state=running` 但游戏无动作的现场已确认（`calls=0`，Lua 原因 `skill_target_type_unconfirmed`）：活着的宝宝其自动技能目标类型未确认时整回合等待，属既有规则，本轮未改行为。
