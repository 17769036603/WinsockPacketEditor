# 自动找星提前卸载竞态修复（2026-10-19）

## 结论与边界

已修复一条可确定性复现的找星生命周期问题：Lua 写出 `stopped` 和双清理标志时，游戏 GL 线程上的 Java Runnable 仍可能排队或尚未返回，原生 worker 却已允许卸载脚本。传送完成也会进入这条停止及重新绑定路径。

现场新增日志中，19:55:45 游戏崩溃栈顶为 `frida-agent-64.so`；20:01:57 的 signal 11 退出接近找星轮换地图与注入活动。后一次同时启动了战斗循环，不能作为仅找星的对照。离线竞态复现没有复现游戏 SIGSEGV，也不能证明所有闪退均由此问题造成。

代码修复阶段未安装或执行真实找星、传送、挑战。用户随后明确授权“覆盖安装”，已于 20:38:56 将 `emulator-5554` 从 code40 更新为本候选 code41，未卸载或清除应用数据。已打开主界面并保存截图；已安装 base.apk 的 SHA-256 与候选包一致，升级前已有的 SharedPreferences 文件均保留。code41 的游戏内稳定性和多模块共存效果仍未确认。

## 修改

- `app/src/main/assets/starhunt_agent.js`：只修改自有 agent 尾部，前面的 bundled bridge 字节保持一致。用 Java `FutureTask` 包装 GL 任务，在未取消、未异常且任务实际完成后确认回调已经返回；停止后不再投递普通 tick，重复停止只安排一次真实 Lua 清理。启动尚在 `Java.perform` 队列中时，先完成初始化，再仅执行清理，不迟到启动找星。
- `native/starhunt/worker-rust/src/main.rs`：当前会话唯一结果文件中的 Lua 停止、`hooksRemoved=true`、`bindingCleared=true`，以及精确匹配本次 `runId` 的 `drained` 回执必须同时满足，才正常卸载和分离。等待期间只从停止流程发布 `stopping`，保留返回游戏完成清理的提示；超时不代替回执。原进程结束仍单独处理，不伪造 Lua 清理成功。
- 新增两个离线测试文件，覆盖脚本调度及原生卸载门禁；未修改 Java、Lua 业务规则或其他模块。

`FutureTask` 的完成、取消和异常语义参考 [Java 官方 API](https://docs.oracle.com/javase/8/docs/api/java/util/concurrent/FutureTask.html)。本实现不把取消当作回调已安全返回的证据。

## 验证

| 项目 | 结果 |
| --- | --- |
| 旧生产 agent 的同组调度测试 | 6 项中 5 失败、1 通过；证明旧版缺少排空协议及幂等停止约束 |
| 旧生产 Rust 停止分支抽取运行 | 编译成功，断言以退出码 101 失败：GL callback pending 时仍允许 detached |
| 新生产 agent 调度测试 | 6 项通过，包括排队 tick、清理回调尚未返回、重复 stop、队列超时、待启动回调和清理失败 |
| 新生产 Rust 门禁抽取测试 | 7 项通过，包括提前/过期/畸形回执、runId、进程结束边界和停止提示保留 |
| 既有 Lua 状态机 | 14 个场景通过 |
| 既有 Java 模型 | 172 条断言通过 |
| 既有原生命令解析 | 3 项通过 |
| 完整 agent 语法、Rust 格式 | 通过 |
| 最终 Android 离线构建 | `BUILD SUCCESSFUL`，code41 APK 已生成 |
| APK 检查 | v1/v2 签名通过；仅 x86_64；包内 agent 和 worker 与构建输入哈希一致 |

新 agent 测试执行真实生产尾部，Java、FutureTask 和 GL 队列由确定性夹具模拟；Rust 测试运行抽取出的生产门禁模块。它们不是完整 Frida daemon 或真实 Android 游戏测试。旧版没有新门禁模块，不能把“缺模块”测试装载失败算作原生行为红测；表中的旧分支断言运行是另行补充的证据。

主代理和测试代理独立核对过回归结果；最后状态发布小修之后，主代理再次运行两组新增测试、格式检查和完整构建。修复过程中出现的 Rust 返回值生命周期及漏导入编译错误均已修正，早期失败日志保留，未将中间包作为交付。

## 独立审查范围

CodeRabbit CLI 已认证并实际完成两轮审查，范围均为两个生产文件、两个新增测试和 `ARCHITECTURE.md`。两轮各报告 1 个 minor issue，分别是停止提示被状态刷新覆盖、停止期间重复发布快照；均已修复。

按既定的一次复审上限未启动第三轮。复审快照之后补充的函数导入与最终发布条件收窄只经过回归、格式和完整构建验证，不能声称最终每一个字节都经过独立审查；没有用人工审查替代 CodeRabbit，也没有声称最终为 0 issues。

判断代理计划阶段实际 API 返回低置信度 `not_run`。结果阶段首次输入格式错误，修正为文件输入后实际 API 仍返回低置信度 `not_run`。两者均未视为判断通过，由主代理按本地证据核对交付边界。

## 候选包

- 文件：`artifacts/starhunt-crash-fix-2026.10.19.23-open-v1/飘渺助手-找星修复候选-2026.10.19.23.apk`
- 包名：`com.xnas.wpe.suite`；版本码 41；版本名 `2026.10.19.23-friends-open-v1`。
- 免激活、锁定 `com.gdoo.dhxy`；生成 `BuildConfig` 的授权服务地址为空；仅 x86_64，Android Debug 签名。
- 大小：68,708,065 bytes。
- APK SHA-256：`2BA8E96F44A0A5944A7A68BA7F7852A4AF284D5277FA04EDE9B76A1994F053E3`。
- 证书 SHA-256：`9f4085bc10118b0279dd3dcecceba16858e50c2691b525701891299534245826`。
- 最终 agent SHA-256：`F8FA4CA3D18EAE3DB51EB46414CC43B0513BE50F9005F2A0DA4D71AC47FD54CF`。
- 最终 Rust 源 SHA-256：`D67BC8C84BAA094FA595BF6E640F071627E84E8092011E7FE66EE0BA5BABEB39`。

## 证据与后续验收

任务证据目录为根目录 `.scratch/starhunt-crash-fix-20261019/`，包含逐文件基线、隔离构建快照、红绿测试日志、两轮 CodeRabbit 输出、最终构建日志、签名及 APK 内容核对结果。原有源码除本任务明确列出的文件外未发生漂移；未修改原有未提交的 WPE 代码。

设备验收需另行明确授权覆盖安装，并分别验证仅自动找星、以及找星与战斗循环共存时的多次切图和停止；测试会产生真实游戏操作。应使用新会话日志确认排空与卸载顺序，并核对游戏进程及崩溃记录，不能用构建通过代替稳定性验收。

## 授权覆盖安装验证（20:38—20:39）

- `adb install -r` 返回 `Success`；包管理器确认 code41 / `2026.10.19.23-friends-open-v1`，原 `firstInstallTime=2026-10-13 14:24:09` 保留。
- 已安装 APK 的哈希与上述交付哈希相同，安装前的 SharedPreferences 文件没有缺失；未导出授权凭证或设置内容。
- `am start -W` 返回 `Status: ok`，主 Activity 进入前台。`installed-home.png` 证明页面正常显示；启动后助手新进程的 crash buffer 未见记录。这只是启动检查，不等于长时间稳定性验收。
- 本次仅覆盖安装和打开助手，没有点击启动读取、自动找星或其它功能，没有主动执行游戏操作。
- 证据位于任务目录 `install.log`、`installed-apk-sha256.txt`、`prefs-before-install.txt`、`prefs-after-install.txt`、`installed-home.png`。

### 覆盖后战斗跳过启动受阻（20:41—20:43）

用户报告战斗跳过显示未确认。现场确认旧安装目录启动的 `libbattle_loop_worker.so`（PID17930）仍存活，绑定当前游戏 PID17798，状态为 `stopping / app_lease_expired`；新程序的只读核对返回 `unconfirmed / worker_running`，运行锁尚未释放。该任务的服务心跳停在覆盖安装前，支持升级中断服务后旧任务等待清理的判断。安装前只核对了找星状态，漏查了战斗跳过后台任务。

目前未强杀 worker、删除状态或放宽启动门禁。已告知用户返回游戏，让旧任务在游戏线程上完成收尾。20:43 再次只读核对仍为上述状态，恢复启动尚未确认。该事件不能算作 code41 的战斗跳过功能验收通过。

本次早期测试留下的 5 个系统 TEMP `.pdb` 文件清理被自动审批拒绝（`blocked by policy`），已保留并告知用户；未绕过拒绝。后续测试仅清理自身当次生成的 `.rs/.exe/.pdb`。
