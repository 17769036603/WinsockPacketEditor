# 战斗门禁“锁空闲即启动”验证（2026-10-17）

## 问题

1. 点「启动快进」时若旧状态没有确认干净，页面只跑一次只读核查，然后提示“完成后请再次点击启动”，需要点两次。
2. 核查要求先证明“上一轮那个 DHXY 进程已结束”。身份记录缺失、损坏、同 PID 仍在运行都只给 unknown，只能重启游戏。
3. 核查结论写在 `recovery-status`，要等下一次核查才被覆盖，于是“工作进程仍持有运行锁”会长期挂在页面上。

## 判定依据

`app/src/main/assets/battle_skip.lua.in` 遇到 `_G` 里 runId 不同的旧状态时会先 `restore(s)`（摘 hook、恢复自动战斗状态）再装新 hook。因此“同进程内上一轮没清理”会被新一轮自己收尾，真正需要硬拒绝的只有“还有 worker 活着”。

## 改动

- `native/battleskip/worker-rust/src/recovery.rs`：`Decision` 用 `Startable(Option<RecoveryIdentity>, reason)` 取代 `Observe`/`Waiting`。锁空闲（`LockObservation::Acquired`）时，身份记录缺失/损坏/读不到、上一轮进程读不到或身份已变、上一轮同一进程仍存活、启动在派发前中断 → 全部判为 `Startable`；`LockObservation::Busy` 仍然是 `Unknown("worker_running")`。
- `native/battleskip/worker-rust/src/main.rs`：`Startable` 写 `state=startable`、`lockProbe=free`、`checkedAt`、当前 `package/pid/startTicks`，并尽力保存当前身份作为下次基线；不再写“请重启游戏”。`Unknown` 也带 `checkedAt`。没有可用身份记录时扫描当前进程（原来只在 `Missing` 时扫描）。
- `app/.../suite/BattleConflictGate.java`：新增 `isStartable`（要求 `state=startable` + `lockProbe=free`，且模块状态不是 running/starting/stopping、不是 `running calls=N`）与 `settled = isClean || isStartable`；`reserve`/`release`/`hasUnconfirmed*` 改用 `settled`。新增 `blockerMessage`：对方真的在运行或清理时直接说“「战斗跳过」正在运行或清理，请先停止它”，不再让用户去点“核查”。
- `app/.../battleskip/MainActivity.java`：点一次启动即完成“核对 + 启动”，核对结束由 1 秒刷新循环自动续跑（`autoStartPending`）；自动续跑只做一次，仍无法确认就停下说明，不会反复起 `su`。状态文本带“（核对于 HH:mm）”，并新增 `startable` → “状态：可以启动”。

## 验证

- `recovery.rs` 单元测试（host 编译运行）：7 项通过，覆盖锁忙仍拒绝、`Startable` 各分支、多目标进程仍拒绝、启动中断可启动。
- 新增 `tests/verify-battle-gate-startable.ps1`（把 `BattleConflictGate`/`BattleWorkerStatus` 编成纯 Java 在临时目录上跑）：17 项断言通过，包括 startable 放行、缺 `lockProbe=free` 拒绝、`running calls=N` 覆盖旧结论、`state=starting/stopping` 拒绝、unconfirmed 仍挡对方、startable 不再挡对方、活着的 loop 仍挡快进、阻碍文案点名正在运行的模块。
- 设备（emulator-5554）：新包 `artifacts/PiaomiaoHelper-2026.10.17.2-battle-gate-startable-20261017.apk`（67,793,474 bytes，SHA-256 `6E8E0A6D…7FDD0B`）安装后设备 `base.apk` 的 MD5 与本地产物一致（`3a8c6c18…b2ba`）。
- 设备实跑只读核查：把 `recovery-identity` 改名后执行 `libbattle_skip_worker.so --reconcile`，得到
  `state=startable / reason=no_prior_record / lockProbe=free / checkedAt=1792248584 / package=com.gdoo.dhxy / pid=3033 / startTicks=344939`，并写回当前身份记录；页面显示“状态：已停止”，侧栏不再出现“未确认”。
- 设备实点一次「启动快进」：被门禁拒绝并显示“「战斗跳过」正在运行或清理，请先停止它再启动「战斗快进」；两项功能仍保持互斥。”，`gate-status` 同步写入同一句；没有派发 worker、没有附加游戏。

## 未验证

- 一键启动到“运行中”的完整链路：当时「战斗跳过」的 worker（`libbattle_loop_worker.so` pid 2974）仍在运行且状态写着 `stopping`，互斥门正确地拦住快进。要跑通必须先在「战斗跳过」页停止并完成清理，本轮未替用户停止。

## 第二轮：同一规则搬到「战斗跳过」（2026-10-17 22:53）

- `native/battleloop/worker-rust/src/recovery.rs`：同样是 `Startable(Option<RecoveryIdentity>, reason)`，判定矩阵与快进一致（锁忙仍 `Unknown("worker_running")`）。
- `native/battleloop/worker-rust/src/main.rs`：`Startable` 写 `state=startable / lockProbe=free / checkedAt / package / pid / startTicks` 并保存当前身份；`Unknown` 带 `checkedAt`；没有可用记录时也扫描当前进程。
- `app/.../battleloop/MainActivity.java`：`isClean` 改为 `settled`（4 处），新增 `startable` 的显示与侧栏标签“可以启动”，并加 `autoStartPending`，点一次「启动」后核对结束自动续跑（只续一次）。
- `app/.../battleloop/BattleLoopService.java` 停止收尾仍要求 `isClean`（真实清理证据），没有放宽：停止流程不能用“锁空闲”当清理证明。
- 回归：`recovery.rs` 单元测试 14 项通过；`tests/verify-battle-gate-startable.ps1` 17 项通过；`assembleDebug` 通过；新包 68,534,197 bytes、SHA-256 `3A1212F6…2621CE6` 安装后设备 `base.apk` 的 MD5 与本地产物一致（`4dc38af1…2cc1`）。
- 设备实跑循环只读核查：`libbattle_loop_worker.so --reconcile` 返回 `state=unconfirmed / reason=worker_running / checkedAt=1792248825`，与僵尸 worker 持有 `battleloop/worker.lock` 的事实一致。

## 仍未解决：遗留的循环 worker

`emulator-5554` 上 `libbattle_loop_worker.so`（pid 2974）从 22:38 起一直活着，`battleloop/status` 写着 `state=stopping / reason=app_lease_expired`，`lease` 心跳停在 22:38（应用服务在多次覆盖安装后没有再写）。它占着 `battleloop/worker.lock`，所以循环与快进都会被正确判定为“有 worker 在跑”。这不是门禁判定问题。

## 第三轮：时效规则与设备实测（2026-10-17 22:56）

发现一个真问题：循环 reconcile 已经写出 `startable`，但 `battleloop/status` 还留着 `state=stopping`，Java 侧原来把 `stopping/starting` 一律当“进行中”拒绝，于是新判定被旧状态卡住。

- `BattleConflictGate.isStartable` 改为**时效判定**：结论必须带 `checkedAt`，且状态文件 `status` 的修改时间不得晚于该时刻（晚于说明核对后又有 worker 动过，结论作废）；仍然拒绝 `running calls=N` 与 `state=running`。`starting`/`stopping` 不再一律拒绝——锁已被核对为空闲时它们只是旧状态。
- `tests/verify-battle-gate-startable.ps1` 扩到 19 项断言，覆盖“结论比状态新”“状态比结论新”“运行中的 worker 压过新结论”“打断的启动在锁空闲核对后放行”等。
- 经用户授权结束遗留 worker（pid 2974）。结束前确认 `/proc/2974` 已随先前 TERM 退出，未影响游戏进程（`com.gdoo.dhxy` pid 3033 不变）。
- 设备实跑循环 `--reconcile`：`state=startable / reason=prior_process_alive / lockProbe=free / checkedAt=1792248886 / pid=3033 / startTicks=344939`。
- 设备**一键启动实测**：打开「战斗快进」页只点了一次「启动快进」，未再点第二次；`battleskip/status` 变为 `waiting_manager_or_local_role`，`libbattle_skip_worker.so`（pid 6155）以 `--run` 在跑，互斥位 `.battle-conflict.guard` 由 `battleloop` 变成 `battleskip`。这证明“一次点击 → 门禁放行 → 派发 → worker 启动”打通。
- 未到“运行中”的原因：当时游戏停在一个弹窗界面（命盘），游戏侧没有提供角色/管理器对象，worker 停在等待绑定，属预期状态，不是门禁问题。
- 随后点一次「停止快进」，约 10 秒后 `battleskip/status` 为 `state=stopped / hooksRemoved=true / bindingCleared=true`，`ps` 中已无 `libbattle_skip_worker`，页面显示“状态：已停止”，设备回到空闲。
- 最终产物 68,534,197 bytes、SHA-256 `FA08AB7E…D2433C`，设备 `base.apk` MD5 与本地产物一致。


