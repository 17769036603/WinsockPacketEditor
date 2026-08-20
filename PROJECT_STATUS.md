# 项目状态

更新时间：2026-08-09

## 已完成

- 增加 ONNX OCR provider，支持 DBNet/CRNN 类模型文件发现、推理输出解析、CTC 贪心解码和外部模型状态提示。
- 增加 Python Worker OCR：C# 前端通过 JSONL 发送 PNG，Worker 使用 RapidOCR + ONNX Runtime，失败或低置信度时安全回退到 C# ONNX/Tesseract。
- 增加 Airtest Windows Worker 能力：支持按窗口句柄截图和受运行级授权保护的输入动作；当前状态机仍由 C# 控制，不自动执行未授权动作。
- 增加颜色出现/消失条件、RGB 容差、最小像素数和最小命中比例。
- 完成 WinForms 配置入口、SQLite/XML 持久化以及模型状态显示。
- 增加 ONNX Runtime 运行时依赖和 x64 native sidecar 复制配置。
- 补齐视觉助手的区域坐标转换、步骤编辑同步、验证区域边界检查、空白截图防误判、模板资源释放和诊断截图保留上限。
- 将视觉助手步骤接入右侧机器人指令集：新增视觉等待指令，支持与键盘、鼠标、延时和循环指令混排、保存、导入导出及标准机器人执行。
- ONNX 识别器支持从模型输入元数据读取 4D NCHW/NHWC 形状与通道数，并支持可选 `preprocess.json` / `ocr_preprocess.json` 预处理 sidecar；模型或 sidecar 变化会触发会话重载。
- 完成固定 `1280×720` 客户区助手模式：加入尺寸配置、启动/运行中/动作前保护、终止型失败、区域越界检查、固定像素坐标 UI 及 SQLite/XML 兼容持久化。
- 完成视觉稳定性审查修复：缓存改用完整位图指纹；Tesseract 重定向输入改为可取消的异步写入；模板匹配增加工作量上限和像素级取消检查；动作发送前重新验证前台窗口；机器人异常、编辑器关闭和识别资源释放流程完成收敛；Hook 过滤器延迟任务增加启动/停止生命周期与实际执行等待。
- 增加 `vision_worker/worker.py`、依赖清单和 `tools/Install-VisionWorker.ps1`；Python 运行时安装到当前用户 `%LOCALAPPDATA%\XNAS\WPE\vision-worker\python`，不覆盖仓库、数据库或 ClickOnce 历史发布目录。
- 增加移动端后端：通过认证的 `MobileSync/manifest` 和 `MobileSync/snapshot` 同步发送、递进与助手预设，并提供三类预设的启动与停止接口；移动端不提供预设写入接口。
- 增加 `mobile/` 原生 Android 客户端：显示发送、递进、助手三类同步预设；无本地缓存时首次连接自动同步，已有缓存后只在主页面或悬浮面板点击“更新预设”时检查并更新目录；运行状态独立轮询；不包含滤镜、抓包或预设编辑。
- 新增 `mobile/IMPLEMENTATION_PLAN.md` 开发实施规格，固定移动端同步不变量、状态机、协议、缓存隔离、悬浮窗边界、错误处理、测试矩阵和交付门槛，作为后续编码与联调的唯一实施依据。

## 已验证

- 使用 Visual Studio 2022 MSBuild 完成 `WPELibrary.csproj` Debug 构建，并完成主程序 Debug 构建（`SignManifests=false`）：0 个错误；保留原有 4 个 EasyHook 清单警告。
- 视觉审查修复、颜色/ONNX、SQLite 持久化、state machine、capture enhancements、diagnostics 和 OCR runtime 回归通过。
- `VisionCaptureRegression.ps1` 通过；当前桌面环境无法提供真实屏幕帧，因此详细屏幕诊断项按环境限制透明跳过。
- WinForms vision UI audit 通过，窄窗口截图已复查。
- 视觉等待指令映射、视觉 UI、视觉持久化和状态机回归通过；窄窗口审计截图确认右侧指令集显示视觉等待行。
- `VisionFixedClientSizeRegression.ps1`、`VisionRobotFormRealAcceptance.ps1`、`VisionRealAcceptance.ps1`、`VisionColorAndOnnxRegression.ps1` 和 `UiDesignRegression.ps1` 均通过；主程序 UI 回归使用隔离输出目录 `WinsockPacketEditor\bin\FixedSizeDebug` 验证。
- `WPELibrary.csproj` Debug 重建通过（0 警告、0 错误）；主程序隔离 Debug 构建通过（0 错误，保留 4 个既有 EasyHook 清单警告）。默认 Debug 输出仍被运行中的 `小黑封包助手.exe` 占用，因此未覆盖运行中的正式输出。
- `MobileSync_Controller` 已加入 Web API 程序集；主程序 Debug 重建通过（0 个错误、4 个既有 EasyHook 清单警告），反射检查确认 manifest、snapshot、runtime 以及发送/递进/助手启动停止路由均已注册；预设快照空数据冒烟检查通过。
- `mobile` Debug APK 构建、Android Lint 和雷电模拟器 `emulator-5554` 安装通过；模拟器 UI 冒烟确认连接配置、发送/递进/助手三块预设列表和启动/停止按钮均可显示，断开连接时能保留错误状态而不崩溃。
- 2026-08-04 本轮修复后重新完成 Release 和隔离 Debug 解决方案重建：均为 0 个错误，保留 4 个既有 EasyHook `MSB3178` 清单警告。`VisionReviewFixesRegression.ps1`、视觉采集/诊断/OCR/状态机/持久化/颜色与 ONNX/固定客户区回归、`ReviewRegressionChecks.ps1`、首页/发送/字节/可读内容回归、Release 视觉 UI 审计和发送预设 UI 审计均通过；`git diff --check` 通过。未执行真实目标注入、抓包或封包发送。
- 2026-08-04 已通过本地 ClickOnce 热更新生成 `releases/2026.8.4.8/`：Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告；应用清单 49 个文件节点、EXE/部署清单/固定入口版本一致，文件大小和 SHA-256 校验通过。当前用户证书存储无可用私钥证书，因此本次为未签名本地包；桌面 `小黑封包助手.lnk` 已备份并继续指向固定入口，图标已更新到 2026.8.4.8。发布后代码、UI、视觉采集/OCR/模板/状态机/持久化回归及 `git diff --check` 均通过；未执行真实目标注入、抓包或封包发送。
- 2026-08-07 已通过本地 ClickOnce 热更新生成未签名 `releases/2026.8.7.0/`：Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告；发布目录 192 个文件、82 个 `.deploy` 映射文件，主程序/清单/ONNX sidecar/Python Worker 文件均通过存在性和版本校验。桌面 `小黑封包助手.lnk` 已备份并更新到固定入口及 2026.8.7.0 图标；旧发布目录、数据库和用户配置保留。
- 2026-08-07 Python Worker 部署验收通过：官方 Python 3.12.10 当前用户运行时、Airtest 1.4.3、RapidOCR 3.9.2、ONNX Runtime 1.28.0 可导入；JSONL `ping` 报告三项能力可用，RapidOCR 合成 `TASK 42` 返回成功且置信度约 0.995，C# JSONL 桥接同样通过。C# Debug 重建、视觉回归、协议回归、Worker 运行时回归和代码审查通过；未选择真实目标、未执行 Airtest 输入、未注入、未抓包或发送真实封包。

## 尚未验收

- 当前未把 APK 内的模型复制进项目，也未在没有许可证确认的情况下下载第三方模型。
- 尚未使用真实游戏截图验证 DBNet/CRNN 模型的准确率、延迟和中文字符集效果。
- 尚未进行真实目标窗口的业务结果验收；本次实现没有启动游戏、抓包、注入或发送网络数据。
- ONNX 推理的最终效果取决于模型输入预处理、模型输出布局和目标游戏字体，需用实际模型包做下一阶段标定。
- 尚未对真实目标窗口执行 Airtest 截图或输入动作；这些能力仅完成合成/协议层验收，并保留运行级授权门控。
- `mobile/IMPLEMENTATION_PLAN.md` v1.5 中的 `SyncModels`、`SnapshotStore`、`SyncStore`、`SyncCoordinator`、profile 隔离缓存、统一错误 DTO、动作幂等和命令短队列已落地；MainActivity/OverlayService 仅负责 UI 与显式命令转发。真实 HTTPS 证书、账号、雷电模拟器联调和目标业务结果仍未在本轮执行。
## 2026-08-07 全面修复与 UI 审查前置

- 已修复 Python Worker 的离线模型校验、模型/语言缓存、OCR 预处理、阈值与字符过滤、原图坐标还原。
- 已接入 Airtest 捕获源；Python Worker 动作必须经过当前运行确认、目标窗口绑定和短时授权令牌。
- Python 可执行文件、Worker 脚本和超时已加入 WinForms 高级 OCR 设置，并写入 SQLite/XML。
- ClickOnce 输出已包含 `vision_worker/models/ocr` 的三个 ONNX 文件；安装脚本增加依赖、模型和 OCR 冒烟检查。
- Python Worker 增加 warmup 健康检查、模型 SHA-256 清单校验、有限滚动日志和请求诊断；Airtest 输入授权改为短时一次性 Token，并增加输入频率、坐标和截图区域边界保护。
- 视觉设置增加 Python/Worker 浏览、测试、恢复默认和 Auto 模式说明；Auto 继续使用本地 ONNX/Tesseract 顺序，Python Worker 显式启用并支持后台预热。

## 2026-08-07 本轮交付

- Release 与本地 ClickOnce 已更新至 `2026.8.7.1`，包内清单、Worker、三个 OCR 模型和部署文件核验通过。
- 桌面 `小黑封包助手.lnk` 已保留原入口并更新图标到 `2026.8.7.1`；旧版本、数据库和快捷方式备份保留。
- 本次发布为未签名本地包，仅用于本机验证，不能作为公网分发包。

## 2026-08-07 全部优化与热更新

- 已完成 Worker 健康检查、RapidOCR warmup、模型 SHA-256 清单、缓存指纹、请求诊断和用户目录有限滚动日志；安装脚本现在执行依赖、清单、warmup 与 OCR 冒烟验收。
- 已完成 Airtest 一次性短时授权、输入频率/坐标/时长边界和截图区域边界保护；未执行真实业务目标窗口输入、注入、抓包或封包发送。
- Python 高级设置增加路径浏览、Worker 测试、恢复默认和 Auto 模式说明；现有主流程保持不变，Python Worker 继续显式选择并支持后台预热。
- 视觉助手 UI 已完成第一批简化：空指令集自动收起为紧凑入口、目标窗口提示改为可执行文案，并保留关键状态与操作；Release 与本地 ClickOnce 已更新至 `2026.8.7.5`，版本目录、固定入口和桌面快捷方式已核验，旧版本、数据库和快捷方式备份保留。
- 本次仍为 unsigned local release；对外分发前必须使用证书重新签名。

## 2026-08-07 全代码审查修复

- 修复同步/异步 Winsock Hook 的边界：重叠 I/O 和完成回调改为安全直通，多 `WSABUF` 替换会清理尾部长度并在失败时恢复原长度，TCP 发送处理部分发送。
- 修复接收拦截返回 EOF 的问题；`recv`、`recvfrom`、`WSARecv` 和 `WSARecvFrom` 现在返回 `SOCKET_ERROR/WSAEINTR`，避免误判为连接关闭。
- 修复普通过滤器空条件误匹配、有界日志队列、取消传播、配置原子替换和远程密码保护；README 已标明当前默认入口实际验收边界。
- 本轮仅完成源码、回归和 Release 构建验证，未执行真实目标注入、抓包、代理、远程管理或封包发送；未生成新的 ClickOnce 桌面版本。

## 2026-08-07 八项审查修复

- 移动端快照改为只返回发送、递进、助手的预设目录 DTO；不再下发原始封包字节、发送参数或助手指令，版本摘要由目录字段和本机完整预设序列化内容共同计算，并在 UI 线程生成一致快照。
- `/MobileSync/*` 改用单独启用的代理账号认证，管理员远程账号被明确拒绝；启动阶段加载代理账号，注入器入口新增“远程设置”和“移动端账号”入口。
- 远程服务启动/停止改为幂等、失败可清理；HTTPS 监听或证书配置失败时保持关闭并记录可定位日志。Android 客户端强制 HTTPS、禁用明文流量和自动重定向。
- 递进移动接口改为同步预检并返回 `jobId`/错误，后台任务不再通过 Web 请求线程弹窗；停止接口返回真实停止结果。Android 运行状态轮询在 Activity 可见或悬浮服务运行期间调度；预设目录不做后台自动拉取，坏快照不会覆盖上一次有效缓存。
- 数据库原子替换增加保存闸门；远程密码读取兼容 DPAPI 和历史自定义加密格式，不再把历史密文误当明文。
- 继续后端审查：revision 现在同时基于本机完整发送、递进和助手预设序列化内容计算；修改封包字节、递进参数或助手指令会触发移动端刷新，但接口仍只返回目录 DTO。发送/助手启动前后均使用 UI 线程快照，并处理启动与停止竞态；退出主窗体时会请求取消移动端任务。
- 代理账号导入/保存也纳入数据库原子替换；Basic Auth 优先兼容严格 UTF-8，并保留历史 ISO-8859-1 回退。WPELibrary Debug 重建和主程序无签名 Debug 编译在前一轮修改后重新通过；本轮 Android 同步策略已修改但未重新构建，当前环境仍缺少 Gradle/JDK/ADB。
- 已通过 WPELibrary Debug 重建（0 警告、0 错误）、主程序 Debug 重建（0 错误，4 条既有 EasyHook `MSB3178` 警告）、Android Lint/Debug APK 构建、`ReviewRegressionChecks.ps1`、`FullCodeFixRegression.ps1` 和 `CryptoRegression.ps1`。未执行真实 HTTPS 证书绑定、移动端实际连接、目标注入、抓包或封包发送验收。

## 2026-08-08 WPE 模拟器端 v1.5 实现

- 已完成 `SyncModels.java`、`SnapshotStore.java`、`SyncStore.java`、`SyncCoordinator.java` 和 HTTPS-only `WpeSyncClient.java`；完整快照进入 Android 应用私有版本文件，SharedPreferences 只保存 profile、版本指针和选择状态。
- 已完成 `MobileSync_Controller.cs` v2 manifest/snapshot/runtime、完整三模块只读参数、规范化 SHA-256/UTF-8 字节校验、持久化分组 ID 迁移、expectedRevision、requestId 去重及开始/暂停/停止路由。
- 已完成发送、助手和递进的暂停边界与同一 jobId 恢复；`OverlayService` 按 v1.5 参考图实现三大主按钮、分组树、一行摘要、四个小入口和普通/运行/暂停三态悬浮球。
- 新增 `tests/MobileSimulatorRegression.ps1`；专项回归、`git diff --check`、WPELibrary/主程序 Debug 与 Release、Android Debug/Release 和 Release lint 均已执行通过。Release 桌面构建保留 4 条既有 EasyHook `MSB3178` 警告。

## 2026-08-08 WPE 模拟器端自动连接联调

- 桌面主入口启动 HTTPS MobileSync 服务；移动端代理账号配置为独立低权限凭据，DPAPI 密文校验改为解密后比较，实测 `/MobileSync/manifest` 返回 HTTP 200。
- Android 只保存移动端连接地址、账号、profile 和快照指针；密码仅存在当前进程内存中。应用重启后保留缓存和连接字段，但必须重新输入密码，不自动连接或启动悬浮服务。
- 本机已配置 `192.168.0.100:89` 的 HTTP.SYS 证书绑定，Android Debug APK 已安装到 `emulator-5554`；强制停止后重新启动仍自动恢复 `OverlayService`。
- `MobileSimulatorRegression.ps1`、Android `lintDebug`、Android `assembleDebug` 和桌面 Release 无签名本机构建通过；桌面构建保留 4 条既有 EasyHook `MSB3178` 警告。未执行真实目标注入、抓包或封包发送。
- 已生成本地未签名 ClickOnce `releases/2026.8.8.0/`，固定入口和桌面 `小黑封包助手.lnk` 已更新，旧版本、数据库和快捷方式备份保留；发布清单校验按 unsigned local release 通过。

## 2026-08-08 WPE 模拟器端全量审查修复

- 已修复 `SyncCoordinator` 的 Activity/悬浮服务生命周期、动作短队列、缓存快照回放、离线运行状态清空和失效状态控件禁用；开始动作优先使用当前选择，运行期间才使用实际运行预设。
- 已修复 HTTPS URL/响应大小校验、结构化错误映射、幂等暂停/停止重试、快照 `revision`/`payloadSha256`/`payloadBytes` 一致性、Base64 规范性和三模块只读详情显示。
- 已修复悬浮球运行/暂停/停止过渡语义、面板详情展开、停止后普通球恢复、服务恢复策略、Activity 选择回退持久化，以及 Android Manifest 图标/备份规则/Lint 问题；补充 Gradle Wrapper 和 Debug/Release 构建入口。
- 已通过 Android `lintDebug assembleDebug lintRelease assembleRelease --offline`、`FullCodeFixRegression.ps1`、`MobileSimulatorRegression.ps1`、`ReviewRegressionChecks.ps1` 和 `git diff --check`。APK 安装/启动检查未执行真实开始、暂停、停止业务动作；当前机器没有可用的现代 C# 编译器，桌面端只能完成静态回归，不能把旧版 MSBuild 的编译失败误报为本轮代码错误。

## 2026-08-08 WPE 模拟器端真实验收记录

- 已在 `emulator-5554` 覆盖安装当前 Debug APK `0.1.0`；启动后自动恢复 `OverlayService`，启动预览不再显示旧配置页，悬浮球收起态为普通深色六点。
- 已用真实雷电引擎 `Ld9BoxHeadless` 完成一次 EasyHook 注入返回成功的验收，目标窗口出现 WPE 注入标题；桌面端远程服务当前注册 `HTTPS://192.168.0.100:89/`。重复注入被正确拒绝，日志为 `STATUS_INTERNAL_ERROR`，不能把重复注入当作新的成功证据。
- 已修复桌面端唯一模拟器自动选择、进程快照为空时的直接回退，以及单个受限进程不阻断进程列表；桌面隔离 Release 构建成功，保留 4 条既有 EasyHook `MSB3178` 警告。
- 已真实进入游戏账号登录页并点击登录；当前 `Ld9BoxHeadless` 未建立目标 `202.189.15.112:14567` Socket，因此开始→暂停→恢复→停止的真实业务链尚未通过。已观察到移动端动作请求在无目标 Socket 时被桌面端明确拒绝，不能把该拒绝算作业务成功。

## 2026-08-09 模拟器端继续审查修复

- 修复助手预设 `visionProfile=null` 被桌面端 DTO 忽略、导致 Android 拒绝整份快照的问题；补充发送、递进和助手快照必填字段校验，禁止缺字段静默回退为 0。
- 修复递进双字节组合范围未校验原始封包边界的问题；桌面端和 Android 端现在都拒绝越界组合参数。
- 修复助手指令校验失败时仍可能被移动接口当作已启动的问题；`StartRobot` 返回真实启动结果，移动端只在 Worker 成功加入后接受任务。
- snapshot 下载改为使用 manifest `payloadBytes` 加有限协议外壳做响应预检，并继续执行规范化哈希/字节数最终校验。
- 修复悬浮面板展开窗口占满 720px 高度的问题；面板高度限制为 360dp，分组列表在面板内部滚动，外部点击可正常收起。
- 已通过 Android Lint/Debug/Release 构建、桌面 Release 构建、移动端/全代码/Review 回归和 `git diff --check`；APK 已覆盖安装到 `emulator-5554`。真实验证确认前台悬浮服务、无崩溃、面板窗口由 `507x720` 收敛为 `507x540`、外部点击恢复 87x87 收起球。桌面端进程 PID 27836、启动时间保持不变。
- 真实开始/暂停/恢复/停止业务链仍受目标 Socket 未建立限制，不能以当前拒绝结果替代业务成功验收。

## 2026-08-09 模拟器端 API-01 安全修复

- 修复助手视觉配置直接反射序列化的问题：移动端快照现在使用显式 allow-list，保留区域、OCR/识别条件、采集参数、步骤、动作定义和模板 PNG 资源。
- 明确排除窗口句柄、进程标识/路径/启动时间/标题、运行级系统输入授权、桌面 OCR/Worker 路径和失败截图目录；助手动作接口对象不进入快照。
- 已通过 `MobileSimulatorRegression.ps1`、`ReviewRegressionChecks.ps1`、`FullCodeFixRegression.ps1`、`git diff --check`、桌面 Release 和 Android Lint/Debug/Release 构建。
- 本次只重新构建桌面端，没有重启或重新安装当前桌面进程；运行中的 PID 27836 仍未加载本次新桌面程序集。真实业务动作链仍待目标 Socket 建立后验收。

## 2026-08-09 模拟器端真实错误隔离复测

- 修复悬浮面板切换模块时沿用其他模块错误提示的问题；当前模块选择会立即按对应 runtime 重置错误归属，动作/同步错误会替换旧提示。
- 已重新构建并覆盖安装 Android Debug APK 到 `emulator-5554`；真实切换到递进模块后不再显示之前发送模块的 `preset_invalid` 提示。
- 真实点击递进“开始”后，模拟器正确显示电脑端返回的 `runtime_not_connected` 映射文案“电脑端当前没有可用的目标连接”；等待后台轮询后提示仍保留。
- 只读检查确认桌面 PID 27836 当前仅有主窗体，没有打开 `Socket_Form` 封包窗体；因此真实开始/暂停/恢复/停止业务链仍不能在不改变桌面状态的前提下完成，不能把该前置条件拒绝误报为业务成功。
- 本轮桌面进程未重启、未重新安装、未修改；Android Lint/Debug/Release 构建、`MobileSimulatorRegression.ps1`、`FullCodeFixRegression.ps1`、`ReviewRegressionChecks.ps1` 和 `git diff --check` 均通过。

## 2026-08-09 移动执行进程边界修复

- 定位并修复 MobileSync 服务进程边界错误：注入器主进程只能管理注入，不拥有目标进程的 `Socket_Cache`、目标套接字或 `Socket_Form`；此前主进程提前绑定 HTTPS 端口，导致移动端能同步到错误的执行上下文，递进接口永远返回 `runtime_not_connected`。
- `Injector_Form` 不再启动或关闭 MobileSync；注入目标中的 `Socket_Form_Load` 继续在加载桌面配置后启动服务，从而让 manifest、snapshot、runtime 和三类动作路由访问同一份目标执行状态。
- 已补充 `MobileSimulatorRegression.ps1` 进程边界回归断言；源码尚未加载到当前运行中的桌面/注入进程，未重启或重新安装桌面端，真实开始/暂停/恢复/停止需在下一次注入新构建后复测。

## 2026-08-09 模拟器端第二轮协议与生命周期审查

- 悬浮服务现在独立复用 `SyncCoordinator` 轮询 runtime；Activity 被退后台或销毁后，面板仍能获得电脑端状态，服务销毁时会取消轮询。
- Manifest/snapshot schema、runtime 计数和 action 布尔值改为严格类型解析；畸形响应不会再被宽松转换成“已接受”或伪造进度，缺失的可选 runtime 字段仍保持未知/空值兼容。
- 修复 profile 切换时清空新 profile 已保存预设选择的问题；现在按目标 profile 的 GUID 选择恢复，删除或不存在时才按规范回退第一项。
- 修复动作失败提示被 runtime 后台轮询清掉的问题；“电脑端当前没有可用的目标连接”等动作错误会持续显示，直到用户手动更新、切换模块或发起下一次动作。
- Android Debug APK 已重新构建并覆盖安装到 `emulator-5554`；前台悬浮服务 `startForegroundCount=1`、`isForeground=true`，未发现 `FATAL EXCEPTION`。
- 已通过 Android Lint/Debug/Release 构建、全部八项回归脚本和 `git diff --check`；错误提示立即截图及等待约 5 秒后的截图均保持可见。

## 2026-08-09 模拟器端全量审查收尾

- 修复发送和助手启动受理期间的停止竞态：移动端停止请求现在可以先登记到当前 `jobId`，桌面端拿到 Worker 句柄后立即执行停止，不再把启动窗口误报为 `stop_not_found`；桌面关闭使用的无 `jobId` 兼容停止入口也同步覆盖该阶段，启动失败时会正确收敛为取消或失败状态。
- 修复新增移动端回归脚本在 Windows PowerShell 无 BOM UTF-8 下的解析兼容性；中文断言改为字符码构造，不改变被测源码内容。
- 已通过 Visual Studio 2022 MSBuild Release 无签名编译（0 错误，保留 4 条既有 EasyHook `MSB3178` 警告）；直接签名构建仍受本机证书存储缺少 ClickOnce 私钥证书限制，未改动签名配置。
- 已通过 Android `lintDebug assembleDebug lintRelease assembleRelease --offline`、桌面全量构建后的全部八项回归脚本和 `git diff --check`。
- 已覆盖安装 Debug APK 到 `emulator-5554`；强制停止后 150ms 首帧直接显示目标应用和普通深色六点悬浮球，没有闪现旧配置页。真实点击展开、分组展开、递进模块切换均可用；预设摘要已压缩为一行，分组三角位于最右侧。
- 当前桌面 PID `27836` 未重启、未重新安装，尚未加载本轮停止竞态修复；目标 Socket 仍未建立，因此真实开始/暂停/恢复/停止业务链继续保留为外部验收项，不能用电脑端拒绝结果冒充成功。

## 2026-08-09 模拟器端第二轮修复

- 修复 `SyncCoordinator` 切换 profile 时只清空后台动作标志、未通知当前 Activity 的问题；现在会同步释放主页面和悬浮面板的动作锁。
- 修复发送入口把 `Socket_Send.StartSend` 的启动异常吞掉后仍返回成功句柄的问题；启动方法现在返回真实 admission 结果，移动端收到失败时保留 `send_start_failed`。
- 修复递进任务在 `Starting` 阶段并发暂停/停止时被 `MarkRunning` 覆盖为 `Running` 的竞态；已暂停或停止状态不会被启动回调改写。
- 修复移动端凭据按单一全局键覆盖不同连接 profile 的问题；凭据现在按 profile key 隔离，并只对旧版单 profile 凭据执行一次性迁移。
- 修复移动端开始/恢复响应受旧 runtime 快照影响的问题；共享启动路由现在同时接受本次请求合法的 `start` 或 `resume` 结果。
- 本轮通过 `MobileSimulatorRegression.ps1`、`FullCodeFixRegression.ps1`、`ReviewRegressionChecks.ps1`、递进核心/暂停/组合/批量、发送批量和助手选择器共 9 项回归，`git diff --check` 通过；桌面 Release 编译 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- 本轮已使用临时官方 Temurin 17 与 Android SDK 完成 `lintDebug assembleDebug lintRelease assembleRelease`，Lint 报告 `No issues found`；`testDebugUnitTest` 为 `NO-SOURCE`，`connectedDebugAndroidTest` 成功。
- 已将 Debug APK 安装到 LDPlayer `127.0.0.1:5555`；`am start -W` 返回 `Status: ok`，无 `FATAL EXCEPTION`，悬浮服务进入 `isForeground=true` 且悬浮窗窗口成功创建。临时拒绝 `SYSTEM_ALERT_WINDOW` 时配置引导可见且服务不启动，恢复授权后服务可重新启动。
- 真实注入、目标 Socket 发包和开始/暂停/恢复/停止业务链仍未执行；桌面 PID 未重启，不能用静态、构建或启动证据冒充业务成功。

## 2026-08-09 模拟器端 UI 与 Hook 边界收尾

- 修复 `Socket_Form_Load` 打开封包窗体时自动启动真实 Hook 的问题；现在仅启动目标进程内的 MobileSync，桌面 Hook 仍由用户控件或移动端动作显式触发，UI/模拟器审计不会隐式改变真实网络状态。
- 修复递进实时预览恢复临时字节时污染编辑器脏状态的问题；恢复预览值会保留恢复前的脏状态，关闭预览窗体不再无故弹出未保存对话框。
- 修复递进移动启动错误码被统一压成 `preset_invalid` 的问题；现在会透传 `runtime_busy`、`runtime_not_connected` 等预检结果，模拟器不会把目标连接缺失误报成预设损坏。
- 修正 `CaptureUiAudit.ps1` 对 `DataBase.conStr` 私有属性的反射读取，完整 UI 审计已生成 10 张截图并通过：主工作区、普通发送、运行态、递进实时值和发送预设窗体均已复查；`RobotSendPresetPickerUiAudit.ps1` 也通过。
- 使用隔离 Release 输出 `WPELibrary\work\DesktopAuditBuildAfterErrorCodeFix` 构建解决方案，0 错误，保留 4 条既有 EasyHook `MSB3178` 警告；32 个非真实业务回归脚本、核心递进/发送回归、两项 UI 审计和 `git diff --check` 均通过。
- 真实目标注入、目标 Socket 发包及开始/暂停/恢复/停止业务链仍未执行；当前结果不把静态、UI、构建或模拟器启动证据冒充为真实业务成功。

## 2026-08-09 模拟器端真实验收工具与恢复一致性修复

- 修复 `RealAcceptanceUiDriver` 的误报路径：目标进程无响应、模块枚举失败或未加载 `WPELibrary.dll` 时现在返回非零并写入 `target-injection-not-proven=true`，只有实际发现目标模块才写入真实验收通过标记。
- 修复发送、助手和递进任务的暂停恢复绑定：暂停任务保存的 `jobId` 现在必须同时匹配当前快照 `revision`，预设内容变化时返回 `runtime_revision_mismatch`，不会用旧任务恢复新预设。
- 已重新编译真实验收辅助程序；桌面隔离 Release 构建仍为 0 错误，相关模拟器/生命周期/递进/发送回归和 `git diff --check` 已在本轮最终复核通过。
- 真实目标注入、目标 Socket 发包及业务开始/暂停/恢复/停止链仍未执行；真实验收辅助程序在缺少这些前置条件时会明确失败，不将静态或模拟器证据冒充成功。

## 2026-08-09 模拟器端最终并发与运行态收口

- 修复 Android 运行态响应宽松解析问题：`MobileSync/runtime` 缺失模块、未知 `state` 或布尔字段类型错误时 fail-closed，运行态显示未知，不会伪装成已停止或已同步。
- 修复已有缓存连接时控件提前可用的问题；只有快照存在、同步状态可控且发送/递进/助手三个运行态均已确认后，开始/暂停/停止控件才会启用。
- 修复 profile 切换后的异步旧会话回写：快照、runtime、状态发布均绑定 generation/client/profile，旧会话不能再覆盖新会话的主页面或悬浮面板；配置重置与会话锁保持一致。
- 修复同一 `requestId` 并发动作可能重复进入业务层的问题；现在按 requestId 原子去重、不同 requestId 独立排队，缓存的拒绝结果仍返回结构化 HTTP 409。
- 修复递进 runtime 首次轮询可能先看到空 revision 的窗口；任务创建后先发布 revision，再标记 Running/启动 worker。
- 已通过 Android `clean lintDebug assembleDebug lintRelease assembleRelease --offline`、桌面隔离 Release 重建（0 错误，保留 4 条既有 EasyHook `MSB3178` 警告）、29 项全量非真实回归、三个核心模拟器/代码/Review 回归和 `git diff --check`。
- Debug APK 已重新安装到 LDPlayer `127.0.0.1:5555`；`MainActivity` 进程保持存活，目标 Activity 可见，`AndroidRuntime` 无崩溃记录。ClickOnce 检查按本机无可用私钥证书的 unsigned local release 边界通过，未修改签名配置。
- 真实目标注入、目标 Socket 发包以及开始/暂停/恢复/停止业务链仍未执行；当前桌面目标连接前置条件不足，不能以静态、构建、回归或 APK 启动证据宣称“0 bug”或真实业务成功。

## 2026-08-09 模拟器端安全与交互复核

- 修复移动端密码持久化越界：移除旧版 `CredentialStore`，连接 profile 只保存地址、账号和快照指针；密码仅在当前进程内存中使用，API 24+ 启动迁移时删除旧凭据偏好文件，API 23 使用清空回退。
- 新增 C#/Java 规范化快照固定向量回归，已验证规范化 UTF-8 正文 156 字节及 SHA-256 `84a366e6cb1ae692c94639ca25726920d622983f59b781b2cf067c77aecffbe2` 两端一致。
- 模拟器实测覆盖安装、重启、缓存回放、重新输入密码提示、预设选择、悬浮服务前台启动、悬浮球展开/收起、外部点击收起和停止；修复停止悬浮服务后主页面残留“正在启动”状态的问题。
- 当前验证：Android `clean lintDebug assembleDebug lintRelease assembleRelease --offline`、最新 Debug 包安装与重启、30 项非真实回归及 `git diff --check`；未执行真实目标注入、目标 Socket 发包或开始/暂停/恢复/停止业务链。

## 2026-08-09 模拟器端全面审查第二轮收口

- 修复运行态语义误判：桌面端 `isBusy` 不再被移动端当作 `running`；忙碌状态必须带合法的 `presetId`、`jobId` 和 `revision`，快照清单版本比较改为大小写不敏感并校验清单计数。
- 修复服务与会话边界：Activity 只有在当前会话已连接且三类 runtime 均可控时才自动启动悬浮服务；显式关闭会禁用本次 Activity 自动启动；服务单独重建时只回放私有缓存，不恢复密码或网络连接。
- 修复动作安全性：暂停必须匹配当前任务 preset 且只允许在 `STARTING/RUNNING` 阶段执行；无效动作请求在进入业务层前拒绝；桌面端未知动作异常缓存为非受理结果，重复 requestId 不会再次执行未知操作。
- 修复本地缓存路径边界：profile 文件名过滤点号及空值，避免被篡改的 profile key 越出应用私有快照目录；移动端错误映射补充 `runtime_faulted`。
- 已通过 Android `lintDebug assembleDebug lintRelease assembleRelease --offline`、桌面隔离 Release 重建（0 错误，保留 4 条既有 EasyHook `MSB3178` 警告）、C#/Java 规范化快照固定向量回归，以及继承 Java/SDK 环境执行的 30 项非真实回归。
- Debug APK 已覆盖安装到 LDPlayer `127.0.0.1:5555`；已实测缓存页面、重新输入密码提示、悬浮服务前台启动、显式停止和再次启动边界，未发现 `FATAL EXCEPTION` 或 `AndroidRuntime` 崩溃堆栈。
- 真实 HTTPS/账号联调、目标注入、目标 Socket 发包及开始/暂停/恢复/停止业务链仍未执行；当前不能把静态、构建、回归或模拟器生命周期证据宣称为真实业务“0 bug”。

## 2026-08-09 模拟器端运行时合同与生命周期最终复核

- 修复 Android 运行控制请求的超时边界：开始、暂停、停止均使用同一 `requestId` 和请求体进行有限重试，服务端原子请求缓存保证重试不会创建第二个任务；相关计划文档已同步为实际行为。
- 补充移动端恢复前的任务身份校验：暂停态点击开始时必须继续绑定当前运行 `presetId`，不会把另一个选中预设误发成恢复请求。
- 修复桌面运行时版本绑定：发送、助手和递进的暂停/恢复在业务层再次校验活动任务 `revision`，版本变化时返回结构化 `runtime_revision_mismatch`；停止保持纯 `jobId` 清理语义，允许终止版本变化后的旧任务。
- 修复版本切换后的移动端死锁：忙碌 runtime 对应旧 revision 时保留可识别运行态，禁用开始/暂停/恢复但保留停止入口；桌面端停止路由接受 job-scoped 的旧 revision 请求，重复停止仍然幂等。
- 修复视觉诊断回归的前景窗口竞态：合成目标窗体采样前显式激活并让出消息循环，避免首次模板从其他前景窗口取样；最终 30 项非真实回归全部通过。
- 修复递进重复停止的幂等边界：同一非空 `jobId` 的重复停止即使当前快照已经清除，也返回原任务的已受理结果，不会误停后续任务。
- 已通过 `MobileSimulatorRegression.ps1`、`FullCodeFixRegression.ps1`、`ReviewRegressionChecks.ps1`、`ByteSweepPauseRegression.ps1`，桌面 WPELibrary 隔离 Release 重编译 0 错误，30 项非真实回归全部通过，`git diff --check` 通过。
- Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon` 通过；LDPlayer 已验证前台服务进程被杀后按 `START_STICKY` 重建、Activity 内容根隐藏/由显式打开命令恢复、停止后服务记录清除，当前服务已停止。
- 真实 HTTPS/账号联调、目标注入、目标 Socket 发包及开始/暂停/恢复/停止业务链仍未执行；这些证据仍不能替代真实业务验收，也不能据此宣称绝对“0 bug”。

## 2026-08-09 模拟器端运行态合同最终回归

- 修复桌面 runtime DTO 的 `pausing` 字段缺失：现在显式返回 `pausing`，`isBusy` 覆盖启动、运行、暂停过渡、已暂停和停止中过渡。
- 修复移动端 runtime fail-closed 边界：`state` 与 `starting/running/pausing/paused/stopping/isBusy` 的显式布尔值冲突时拒绝整份 runtime，不再把矛盾状态渲染成可操作状态。
- 已通过 Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon`、桌面 WPELibrary 隔离 Release 重编译、`MobileSimulatorRegression.ps1`、`FullCodeFixRegression.ps1`、`ReviewRegressionChecks.ps1`、`MobileCanonicalVectorRegression.ps1`、`ByteSweepPauseRegression.ps1`、Vision diagnostics/persistence 回归和 `git diff --check`。
- Debug APK 已覆盖安装到 LDPlayer `127.0.0.1:5555`；`MainActivity` 冷启动返回 `Status: ok`，前台 Activity 可见，未发现 `FATAL EXCEPTION`/`AndroidRuntime`，强制停止后移动端进程已清理。
- 本轮对当前 LDPlayer/WPE 外部状态仅做进程、模块和 TCP 只读检查；未执行 EasyHook 注入、真实开始/暂停/恢复/停止或目标 Socket 发包，因此真实 HTTPS/账号联调和业务链仍不能宣称通过或“0 bug”。

## 2026-08-09 模拟器端状态显示与注入证据复核

- 补齐 Android `RuntimeStatus` 对 `pausing` 的显式解析、类型校验和状态一致性校验；桌面 DTO 与移动端现在对暂停过渡状态使用同一字段合同。
- 修复 Activity 在悬浮服务被系统回收后仍可能保留隐藏配置页的问题；Activity 恢复/轮询时会重新显示配置页并刷新悬浮按钮状态。
- `RealAcceptanceUiDriver verify` 只读复核当前 PID `7080`：目标进程可响应，但模块检查未发现 `WPELibrary.dll`，程序返回 `20` 并写入 `real-acceptance=false;target-injection-not-proven=true`；该证据不执行注入或业务动作。
- 已重新通过 Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon`、`MobileSimulatorRegression.ps1`、桌面 WPELibrary 隔离 Release 编译、LDPlayer Debug APK 安装/冷启动/强制停止和 `git diff --check`。
- 真实 HTTPS/账号联调、目标注入、目标 Socket 发包以及开始/暂停/恢复/停止仍未执行；当前仍不能宣称真实业务“0 bug”。

## 2026-08-09 模拟器端全量非真实回归与预览生命周期修复

- 修复 `Socket_SendForm` 字节扫掠实时预览恢复时的脏状态竞态：临时值恢复触发的字节提供器事件不再把干净编辑器标记为未保存，隐藏 UI 审计窗口可以正常关闭，不会等待不可见的保存对话框。
- 已通过真实覆盖该路径的 `CaptureUiAudit.ps1`；预览恢复后 `byteSweepEditorDirty=False` 且窗口正常关闭，并生成 10 张审计截图。
- 35 项非真实回归全部通过，覆盖桌面核心/批量/视觉/OCR/Python Worker/UI 审计、移动端规范化向量和模拟器回归；方案 Release 编译在 `SignManifests=false` 下通过，保留既有 EasyHook 清单警告。默认签名 Release 仍因本机证书存储缺少 ClickOnce 签名证书而无法通过，未修改签名配置。
- 真实 HTTPS/账号联调、目标注入、目标 Socket 发包以及开始/暂停/恢复/停止业务链仍未执行；当前仍不能宣称真实业务“0 bug”。

## 2026-08-09 模拟器端缓存恢复与生命周期收尾

- 修复 `SyncStore.activateProfile` 将连接地址、账号和活动 profile 分成两次异步写入的问题；现在一次 `SharedPreferences.Editor` 同时提交，避免进程中断后出现活动 profile 与连接元数据不一致。
- 修复悬浮服务重建后只恢复 `SyncCoordinator`、却没有把缓存快照/选中预设/runtime 重新灌入面板的问题；服务启动时会从当前 Coordinator 恢复面板状态，服务或 Coordinator 关闭时清理旧面板状态。
- 修复成功状态更新后旧错误提示仍遮挡悬浮面板的问题；非错误同步状态现在会清除旧错误文本。
- 修复后已通过 `MobileSimulatorRegression.ps1`、`MobileCanonicalVectorRegression.ps1`、`FullCodeFixRegression.ps1`、`ReviewRegressionChecks.ps1`、Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon`、LDPlayer Debug APK 覆盖安装/冷启动和 `git diff --check`。
- 真实 HTTPS/账号联调、目标注入、目标 Socket 发包以及开始/暂停/恢复/停止业务链仍未执行；当前仍不能宣称真实业务“0 bug”。

## 2026-08-09 模拟器端最终审查收口

- 修复 Activity 重建期间的悬浮命令竞态：状态回放完成前的开始/暂停/停止、模块和预设切换命令进入有上限的短期队列，回放完成后再按命令 ID 去重处理，不再因选中项尚未恢复而静默丢失。
- 收紧移动端协议边界：助手 `instructions` 的每项必须是 JSON 对象，`visionProfile` 必须为对象或 `null`；runtime 的 `completedCount`/`totalCount` 存在时必须是非负整数，非法类型不再静默变成空进度。
- 加固 Coordinator 生命周期：同步、runtime 和动作提交遇到执行器关闭/拒绝时会释放对应 in-flight/action 锁并安全提示，不再把 Activity/Service 关闭竞态升级为未捕获异常。
- 加固旧 Android 版本的动态命令接收：应用内广播使用签名级权限，Android 13+ 继续使用 `RECEIVER_NOT_EXPORTED`，并保留服务 `exported=false`；外部 UID 无法伪造开始/暂停/停止命令。
- 已通过 Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon`、C#/Java 规范化向量、移动模拟器回归、桌面 Review/FullCodeFix 及总计 34 项非真实回归；`git diff --check` 通过。Debug APK 已覆盖安装到 LDPlayer `127.0.0.1:5555`，冷启动 `Status: ok`，UI dump 可见配置/连接/更新/密码/预设控件，应用内悬浮服务启停均通过，未发现应用崩溃。
- `ClickOnceUpdateRegression.ps1` 仍受本机缺少 ClickOnce 私钥证书影响；该签名门槛与本轮模拟器代码无关，未修改签名配置。真实 HTTPS/账号联调、目标注入、目标 Socket 发包及开始/暂停/恢复/停止业务链仍未执行，因此不能把本轮静态、构建、生命周期或协议证据宣称为真实业务“0 bug”。

## 2026-08-09 模拟器端选择与会话并发最终扫尾

- 修复选择预设只落盘、不更新当前 `SyncCoordinator.ProfileState` 的状态分叉；现在 `saveSelection` 在 profile 锁内持久化并立即回读当前 profile，悬浮服务重建或切模块不会回放旧选择。`snapshot=null` 的瞬态也不再清空已保存选择；只有有效空类别才会清理选择。
- 收紧暂停任务恢复：主页面与悬浮面板只有在当前选中 ID 等于 runtime 的 `presetId` 时才启用“开始/恢复”；排队动作在真正 POST 前再次校验 session，切换连接后不会发送尚未执行的旧动作。
- 最新 Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon` 通过，lint 报告为 `No issues found.`；`MobileSimulatorRegression.ps1`、`MobileCanonicalVectorRegression.ps1`、`FullCodeFixRegression.ps1`、`ReviewRegressionChecks.ps1` 和 `git diff --check` 均通过。
- 按脚本契约完成 34 项非真实回归；`CaptureUiAudit.ps1` 生成 11 张截图，`RobotSendPresetPickerUiAudit.ps1` 生成 2 张截图。最新 APK 已覆盖安装到 LDPlayer `127.0.0.1:5555`，冷启动 `Status: ok`；UI dump 检查配置、连接、密码和预设控件，悬浮前台服务启停通过，应用日志无 fatal。
- ClickOnce 本机证书签名门槛仍未解决；真实 HTTPS/账号联调、目标注入、目标 Socket 发包及开始/暂停/恢复/停止业务链仍未执行，因此静态、构建、UI 和生命周期证据不能宣称真实业务“0 bug”。

## 2026-08-09 模拟器端并发与快照身份最终补强

- 修复 Activity 尚未完成 UI 初始化时提前 claim 悬浮命令的竞态；命令现在在真正执行前去重，Activity 重建期间保留服务侧未认领副本，避免开始/暂停/停止被静默丢失。
- 修复悬浮按钮延迟执行时跟随错误模块的问题；动作携带按下时的模块，旧服务实例的回调只会重新排队，不会在新服务实例接管后执行旧动作；旧实例也不会清空新实例的面板状态。
- 收紧 runtime 身份和进度别名校验：任务 ID 必须是合法 GUID、revision 必须是 64 位十六进制摘要、双命名字段必须一致，计数必须为非负整数且别名值一致。
- 修复桌面移动同步快照的分组 ID 当前快照内冲突：历史名称映射与重命名/同名复用冲突时生成新的持久唯一 ID，避免完整快照因重复分组 ID 被移动端拒绝。
- 使用 VS 2022 Build Tools 完成隔离 Release 解决方案重建，主程序与 `WPELibrary` 均 0 错误，仅保留既有 4 条 EasyHook `MSB3178` 警告；Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon`、C#/Java 规范化向量、移动模拟器、FullCodeFix、Review 和 `git diff --check` 均通过。
- 本轮累计 34 项非真实回归通过；`CaptureUiAudit.ps1` 生成 11 张截图，`RobotSendPresetPickerUiAudit.ps1` 生成 2 张截图。最新 Debug APK 已覆盖安装到 LDPlayer `127.0.0.1:5555`，冷启动正常、配置/预设控件可见、悬浮前台服务最终停止后的 `dumpsys` 为 `(nothing)`，无 fatal 日志。
- ClickOnce 签名仍受本机缺少私钥证书限制；真实 HTTPS/账号联调、EasyHook 注入、目标 Socket 发包及开始/暂停/恢复/停止业务链仍未执行，因此本轮只能确认安全范围内的模拟器与编译回归，不能宣称真实业务绝对“0 bug”。

## 2026-08-09 模拟器端协议模型与生命周期最终回归

- 新增 `tests/MobileModelContractRegression.ps1`，在不联网、不注入、不执行目标动作的前提下，直接编译并运行 Android Java 模型契约，覆盖 21 项断言：runtime 身份/进度、整数别名、分组与预设 GUID 别名、规范 GUID、快照完整性、Base64 长度、路由和 HTTPS URL 边界均通过。
- 修复 `SyncModels.java` 的严格字段校验缺口：整数别名必须一致；分组/预设 GUID 的双命名字段必须是非零规范 UUID 且值一致；补齐共享的空 GUID 判定辅助函数，C#/Java 规范化向量回归恢复通过。
- 修复 `OverlayService.updatePanelData` 成功回放时残留旧错误文本的问题；成功的 runtime/快照状态会清除旧错误，错误状态仍会保留并显示。
- 最新 Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon` 通过，Lint 报告为 `No issues found.`；`MobileModelContractRegression.ps1`、`MobileSimulatorRegression.ps1`、`MobileCanonicalVectorRegression.ps1`、`FullCodeFixRegression.ps1`、`ReviewRegressionChecks.ps1` 和 `git diff --check` 均通过。
- 最新 Debug APK 已覆盖安装到 LDPlayer `127.0.0.1:5555`；冷启动返回 `Status: ok`，UI dump 可见配置/连接/密码/预设控件，悬浮前台服务开启后可见、关闭后 `dumpsys` 不再存在，未发现 `FATAL EXCEPTION`。
- 真实 HTTPS/账号联调、EasyHook 注入、目标 Socket 发包及开始/暂停/恢复/停止业务链仍未执行；因此本轮确认的是安全范围内的协议、模型、构建和模拟器生命周期，不宣称真实业务绝对“0 bug”。

## 2026-08-09 模拟器端 URL ACL 兼容修复与真实注入

- 定位到电脑端与模拟器端状态分叉的直接原因：MobileSync 监听迁入普通权限的注入目标进程后，当前 `https://192.168.0.100:89/` 没有 HTTP.sys URL ACL，导致 OWIN HttpListener 无法启动；证书与 SSL 绑定本身有效。
- 新增 `Socket_TcpOwinHost` 作为 HTTPS 后备主机：原 HTTP.sys 主机启动失败时，复用同一证书、同一 `Socket_Web` OWIN 路由和 Basic 账号校验，通过普通 TLS/TCP 监听避开 URL ACL；未新增依赖、未降低为明文 HTTP、未改变 Android 协议。
- 已发布并签名本地 ClickOnce `2026.8.9.1`，固定入口和桌面快捷方式继续指向 `releases\小黑封包助手.application`；Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- 真实重启 LDPlayer `index 0` 后，已将 `2026.8.9.1` 注入 `Ld9BoxHeadless` PID `7908`；窗口标题显示新版本，TCP `192.168.0.100:89` 的监听 PID 同为 `7908`。电脑端无凭据请求返回预期 HTTP 401 与 Basic challenge，Android 端到该端口的 TCP 实测可达。
- 移动伴侣已启动并恢复缓存预设，当前明确显示 `CACHED_OFFLINE / 请输入密码后连接电脑端`；移动端密码按既定安全边界仅保存在进程内存，本轮未读取、解密或代填密码，因此账号认证和开始/暂停/停止业务动作仍未执行。

## 2026-08-10 MobileSync 完全免密与配置页永久隐藏

- 按用户明确确认的局域网风险边界，将 `/MobileSync` 与 `/MobileSync/*` 改为匿名 HTTPS 路由；其他 Web API 仍由远程管理管理员 Basic Auth 保护，匿名范围没有扩大到桌面管理接口。
- Android 客户端移除 Basic Authorization、账号/密码输入和密码重连分支；使用保存地址或默认 `https://192.168.0.100:89/` 自动连接。旧用户名只作为升级期间的本地 profile/cache 命名空间，不会发往电脑端。
- 修复配置页被 Activity 生命周期重新显示的问题：根内容始终为 `GONE`，透明 Activity 启动后立即退到后台；首次只展示 Android 悬浮窗系统授权页。粘性服务和 `BootReceiver` 在进程或模拟器重建后自动恢复连接与悬浮控制。
- 已通过 Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon`、21 项 Java 模型契约、`MobileSimulatorRegression.ps1`、`ReviewRegressionChecks.ps1`、`ClickOnceUpdateRegression.ps1` 和 `git diff --check`。真实 APK 覆盖安装、ClickOnce 新版本和注入后局域网路由验收在本节后续交付中补充。
- 已签名发布本地 ClickOnce `2026.8.10.0`；固定入口和桌面 `小黑封包助手.lnk` 都指向 `releases\小黑封包助手.application`。Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- 最新 Debug APK 已覆盖安装。重启 LDPlayer 后将 `2026.8.10.0` 真实注入 `Ld9BoxHeadless` PID `11856`，窗口标题与端口 89 监听进程一致；无凭据 `manifest/runtime` 均返回 200，`SystemInfo` 和 `/MobileSyncExtra` 均返回 401，`/MobileSync` 根路由返回 404，证明匿名前缀没有外溢。
- 模拟器实测启动移动伴侣后，前台保持游戏 Activity，配置标题/账号/密码不在 UI 树中；悬浮服务为 foreground，悬浮面板显示 57 条发送、1 条递进、17 条助手预设且可用。未点击开始、暂停、停止或发送。
- `UiDesignRegression.ps1` 的 Release UI 进程在本机 60 秒内未返回，按超时终止且没有失败断言。Android shell 不允许伪造受保护的 `BOOT_COMPLETED`，因此改用完整 LDPlayer 重启验收：在桌面端尚未重新注入时，`BootReceiver` 已自动恢复前台悬浮服务，前台仍是 Launcher 而非配置 Activity；重新注入后服务自动恢复同步，游戏内悬浮面板预设和控制状态正常。强杀前台服务进程仍受 shell 权限限制，未单独冒充系统回收验收。其余 FullCodeFix、Review、MobileSimulator v1.6、C#/Java 规范化向量、21 项模型契约、ClickOnce 回归和 `git diff --check` 均通过。

## 2026-08-10 模拟器端开始按钮预设身份修复

- 定位到悬浮层“开始”错误使用回家预设的根因：电脑端 runtime 在任务完成后仍保留上一次 `presetId`，而悬浮服务处理开始命令时无条件用该 runtime ID 覆盖当前选择；实测当前选中 `风水混元丹`（`fe28d931-e26d-4ac8-a889-3e725ad5f75a`），runtime 旧值为 `回家`（`b8a07b5e-5cfd-4d91-9eeb-004057f23f26`）。
- 修复 `OverlayService`：主按钮在按下瞬间携带当前模块和预设 ID；`START` 优先使用命令携带的预设，只在 `PAUSE/STOP` 使用实际忙碌任务的 runtime ID，不再用已完成任务覆盖新选择。隐藏 Activity 的广播回退路径同步采用同一规则。
- 已通过 Android `lintDebug assembleDebug lintRelease assembleRelease --offline --no-daemon`、21 项 Java 模型契约、移动模拟器、C#/Java 规范化向量、FullCodeFix、Review 和 `git diff --check`；新 Debug APK SHA-256 为 `94BC3A41FFC03FA3A0847817C7C9AF900D82AAF0CAD020F11D410E390287902D`，已覆盖安装到 LDPlayer `emulator-5554`。
- 安装后 `MainActivity` 冷启动返回 `Status: ok`，游戏 Activity 仍在前台，`OverlayService` 为 `isForeground=true`，未发现 `FATAL EXCEPTION`；本轮未点击真实开始/暂停/停止，避免改变游戏任务状态。

## 2026-08-10 移动端无响应根因与真实连接修复

- 定位到选中预设“完成但无效果”的根因：预设保存的 `PacketSocket` 可能在模拟器重连后失效（本次旧值为 `816`），发送路径原先仍使用旧 socket，且运行时只看工作线程结束而没有检查实际成功数。
- `Socket_Cache.DoSendWithResultAsync` 现在每次按当前目标地址/端口解析实时 socket；`MobileSendRuntime` 在实际成功数为 `0` 时返回 `send_failed`，不再伪报完成。
- 修复无人值守启动链：`--auto-inject` 会跨越 UAC 提权边界传递；ClickOnce `2026.8.10.3` 已签名发布，固定入口和桌面快捷方式已更新，旧版本与数据库保留。
- 现场已通过 LDPlayer 冷启动清除旧注入模块并重新注入：目标进程 PID `19972` 显示 `小黑封包助手 - 2026.8.10.3`，`https://192.168.0.100:89/MobileSync/manifest` 与 `/runtime` 均返回 HTTP 200；移动端 Debug APK 已覆盖安装，SHA-256 为 `641A0C593D809EB36235F44A93FE17BE9FF9A2AE57F7A22CC46F6DE1B3E8CA68`，悬浮服务为 foreground。
- 本轮只验证连接、注入、预设目录和运行状态，未点击真实发送，故不宣称真实游戏业务结果已验收。

## 2026-08-10 动态字段/动态变量 V1

- 在不新增依赖、不改变现有滤镜语法、预设循环语义和原生发送函数的前提下，完成动态变量模型、规则匹配/提取、当前值与历史值、SQLite 增量表、XML 备份及旧数据库/旧预设兼容迁移。
- 抓包处理现在按“最终显示缓冲区匹配 → 同包变量原子更新 → 原顺序投递滤镜发送/机器人动作”的边界运行；发送预设、机器人和高级滤镜触发统一解析启动时不可变变量快照。
- 完成抓包详情 HexBox 动态字段菜单、普通标注组合样式、发送编辑器变量绑定与模板预览，以及设置中的独立“变量中心”页面；支持规则测试、编辑、删除、会话暂停、清空当前值和历史值标签。
- `tests\DynamicVariableRegression.ps1` 专项回归通过，覆盖选择范围、规则拒绝条件、同包原子更新、快照发送、历史去重/标签、备份、旧格式迁移、绑定冲突和有界历史队列；`ByteAnnotationRegression.ps1`、`UiDesignRegression.ps1`、`ReviewRegressionChecks.ps1`、`FullCodeFixRegression.ps1` 也均通过。
- Debug 解决方案构建和隔离 Release 构建均为 0 个错误；隔离输出为 `WPELibrary\work\DynamicVariableV1Release`，保留 4 条既有 EasyHook `MSB3178` 清单警告。上述专项及既有回归均使用隔离 Release 产物复跑通过，`git diff --check` 通过。
- 本轮未执行真实目标抓包、注入或网络发送；构建、静态回归和合成封包测试不等同于真实业务验收。未创建 ClickOnce 版本、Git Tag、Release、提交或推送。

## 2026-08-10 动态字段/动态变量 V1 审查修复

- 审查并修复动态变量加载/导入边界：非法或重复变量定义、无效封包方向、重复规则/字段 ID、重复提取来源和非法历史值不会替换当前运行态；拒绝的备份不会覆盖已有规则。
- 修复发送失败提示使用封包第一个变量名的问题；解析器现在返回实际失败变量，手动发送显示具体符号，滤镜/机器人触发发送记录同一错误。
- 修复动态字段/绑定 UI 的越界、空字段和重复来源路径；保存弱固定特征规则前增加确认，变量定义或规则写入失败前不再提前产生无主定义。
- 修复数据库加载未完成时导出/退出保存可能覆盖已有动态变量数据的问题；数据库加载失败时跳过动态变量持久化。
- 修复后 Debug 与隔离 Release 重建均为 0 个错误，保留 4 条既有 EasyHook `MSB3178` 清单警告；动态变量专项、ByteAnnotation、UI、Review、FullCodeFix 回归均在两种配置下通过，`git diff --check` 通过。

## 2026-08-10 全面审查：持久化、服务边界与执行链

- 预设、滤镜、递进、机器人、代理账号和代理映射的新增、编辑、删除、排序、启用、导入及跨分组操作统一经过数据库原子保存；保存失败时恢复内存列表和顺序，并向界面报告失败。
- 启动加载现在区分“有效空表”和“数据库读取失败/结构异常”；加载未完成或失败时跳过退出保存，避免空内存覆盖已有数据库。系统备份导入成功后保留当前内存状态，不再因后续保存把列表误清空。
- 系统、代理和注入配置导入增加布尔值、整数、端口及密码解密前置校验；动态变量保存返回明确成功/失败结果，失败时不再向界面伪报成功。
- HTTPS 后备主机增加 TLS 1.2、请求头 64 KiB、请求体 4 MiB、响应体 16 MiB、单机并发 64 和握手/请求 15 秒上限；停止服务时主动关闭活动连接并等待排空。`MobileSync` 仅允许本机回环、私有网、链路本地和 ULA 地址，快照正文上限为 8 MiB，超限返回 413。
- 发送执行前拒绝负循环次数/间隔，并对无效 socket、空封包和实际发送失败计数；移动端运行结果不再仅依据工作线程结束判断成功。
- 本轮完成源码、Debug/Release 构建、静态回归、UI 审计和发布前包级检查；未执行真实目标抓包、注入、封包发送或开始/暂停/恢复/停止业务验收。Android 规范化向量测试因当前机器缺少 `JAVA_HOME` 与 Android SDK 环境而未执行。

## 2026-08-10 全面审查后本地热更新

- 已生成 ClickOnce `2026.8.10.6`，Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；应用清单、固定入口和部署版本一致，发布目录 200 个文件（含 86 个 `.deploy` 文件），SQLite、HexBox、WPELibrary 和视觉 Worker 依赖存在。
- ClickOnce 应用清单、部署清单和固定入口均包含签名节点，`ClickOnceUpdateRegression.ps1` 通过；Windows `Get-AuthenticodeSignature` 对 XML 清单返回 `UnknownError` 是当前信任提供程序兼容性提示，不影响本地清单回归结果。桌面 `小黑封包助手.lnk` 已更新到固定入口和 `2026.8.10.6` 图标，旧快捷方式备份保留。
- 旧版本目录、数据库和用户配置未清理；当前未启动桌面程序，使用时请先关闭旧实例，再从桌面快捷方式启动新版本。仍未执行真实目标注入、抓包、封包发送或移动端开始/暂停/恢复/停止业务验收。
