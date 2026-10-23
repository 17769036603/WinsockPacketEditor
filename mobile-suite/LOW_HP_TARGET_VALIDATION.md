# 2026-10-20 自动战斗低血量选敌

## 后续选敌日志版本 code43

为核对实机选择过程，新增 `decision=target_trace` 日志。`phase=selected` 表示本机选中，`retargeted` 表示首次发送前目标血量非正而重新选敌，`send_returned` 表示客户端发送调用成功返回，不能据此推断服务器接受。每条记录包含战斗号、回合、行动者、技能、目标 ID/站位/当前 HP、尝试次数及候选快照 `pos:roleId:hp`。候选快照列出符合原身份和存活检查的敌人，非正 HP 可出现在快照中但不参与选中，未知或非有限 HP 记录为 `unknown`。

日志读取与写入包装在 `pcall` 内，不参与选敌判定；发送重试沿用已有次数上限。8 项相关场景通过，额外沙盒核对人物与宝宝的两条选择、两条发送日志及 HP 内容通过，CodeRabbit 完成审查并提出 0 issues。证据在 `.scratch/low-hp-log-20261020-01/`。

code43 / `2026.10.20.2-friends-open-v1` 已于 12:57:45 保留数据覆盖安装；升级前旧模块正常返回 `stopped`，并确认 `hooksRemoved`、`bindingCleared`、`restoreConfirmed` 均为 true。安装包哈希与制品一致：`FFA9ACBFD7A439FED4D4E8E216BDE399CF4B469E7752214EBDAD2E0D4CEFFFDB`。候选在 `artifacts/low-hp-log-2026.10.20.2-open-v1/`。已恢复用户原先开启的战斗跳过，真实目标仍须根据新会话日志核对。

升级后已取得真实会话日志 `runtime-rounds.log`：16 条具有有效正 HP 的选择快照逐项与候选最低 HP 比较，0 条不匹配。第 `138988:5` 回合，候选 HP 为 25,760、53,204、8,355，人物与宝宝均选择 ID `567898749`、HP 8,355，`send_returned` 记录的目标一致。确认本机选择和客户端发送参数按规则执行；未核对服务端最终目标或伤害对象。

第六回合曾记录 `stale_finish_flag` 等待，之后日志进入 `not_in_war`，截图确认已经回到地图。没有将本次选敌取证扩展为整个跳过流程的稳定性结论。

## 实现范围

统一助手的 `app/src/main/assets/battle_loop.lua.in` 在原有敌方五个候选站位中，优先选择当前剩余血量最少的合法存活目标。人物与宝宝使用同一个选择函数。独立战斗循环模块未同步修改。

- 读取现有 `m_Hp`，按绝对剩余血量比较；数值相同时保留站位顺序。
- 排除已知为零或负数的血量；非数字、NaN、正负无穷按未知处理。
- 存在已知正血量目标时优先比较这些目标；全部未知时沿用首个合法目标。
- 首次发送前再次检查目标身份、战场绑定与存活状态；仅血量变成非正数时，重新选择合法目标。没有替代目标时恢复自动并等待，不停止循环；后续 tick 出现目标后可以继续。
- 已提交动作的重试保留原目标 ID 和原绑定检查，避免重试时改成另一条指令。
- 技能目标限制、防御、自有回调清理、行动确认与动画完成通知保持原规则。

## 离线检查

六个新增 Lua 场景均通过，覆盖最低血量、同值顺序、未知值回退、无效血量、双方站位、发送前目标失效、无替代目标后恢复和重试目标固定。

完整入口遇到 `unknown-target.lua` 时退出码为 1，断言为 `unconfirmed skill target caused a send`。相同场景搭配修改前资产仍失败，未修改该旧期望。逐项运行其余场景与状态契约检查，共 27 项通过、1 项基线失败。没有将完整入口报告为通过。

日志位于项目根目录 `.scratch/low-hp-target-20261020-01/` 下的 `verification-current.log`、`existing-scenarios.log` 与 `full-entry.log`。

CodeRabbit 首轮提出 1 个 major：发送前目标血量变零会触发模块停止。修复后独立复查六个定向用例及七个相关既有场景，13/13 通过，日志为 `verification-final.log`。最终 Lua SHA-256：`1C40D2A1794371EBDB31AEBF78AC413F7B22908A81C560BBDE7D7D7EF4A514C9`。

最终 CodeRabbit 复审完成，0 issues；覆盖 Lua 模板、沙盒入口与六个新增用例。首轮及最终输出分别为 `coderabbit-retry.ndjson`、`coderabbit-final.ndjson`。

计划与结果两个阶段的判断代理均实际调用 API，但均返回 `LOW_CONFIDENCE` / `not_run`。没有将其记为通过；主代理根据独立沙盒结果、CodeRabbit 完成回执、源码与制品哈希及构建日志核对交付。

## 验收边界

沙盒检查证明模拟战场内的选择行为。用户授权覆盖安装，并在旧进程清理停滞后明确允许重启游戏。旧游戏 PID 1938 退出后，模块回执为 `state=stopped`、`processEnded=true`、`recoveryVerified=true`，原生 worker 已退出。

2026-10-20 12:52:03 已通过 `adb install -r` 保留数据覆盖安装到 `emulator-5554`。安装后版本 code42、安装 APK 哈希与候选一致，原 SharedPreferences 文件仍在，助手主界面截图确认正常打开，游戏也已重新启动。未开启新版本战斗功能，尚未在真实游戏中确认服务器接收的目标以及最终攻击对象。证据为任务目录的 `installed-metadata.json`、`install-before.png` 与 `install-after.png`。

## 构建制品

离线重建成功，版本 code42 / `2026.10.20.1-friends-open-v1`，x86_64、Android Debug 签名，免激活并锁定 `com.gdoo.dhxy`，授权服务 URL 为空。APK 位于 `artifacts/low-hp-target-2026.10.20.1-open-v1/`，同目录含安装说明、`SHA256SUMS.txt` 与 `build-info.json`。

APK SHA-256：`D82667A4A1E103154992163631D1D443C9D16AB6121CB27CD835EB19F9B3A777`，大小 68,699,527 字节。包内 Lua 与冻结生产模板哈希一致；构建日志确认 `battle-loop-worker` 重新编译，包内 worker SHA-256 为 `86479BC58321C2B10052C3EC408B72EB4400D5E438381A2032C0B643304595DA`。上个版本找星修复对应两份源码哈希保持原值。
