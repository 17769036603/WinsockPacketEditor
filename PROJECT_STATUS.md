# 项目状态

更新时间：2026-08-22

## 已完成

- 增加 `ReadOnlyProcessIdentity` 和 `ReadOnlyProcessMemoryReader` 只读内存基础层：显式地址读取、进程身份复核、指针宽度校验和释放状态保护均已实现；当前未接入未经确认的游戏对象偏移。
- 增加内置助手预设 `召唤兽技能`：使用 `SummonedPetSkillBookPresetPlan` 持久化 16 个清晰步骤，覆盖当前参战召唤兽确认、状态读取、材料检查、开格、技能书顺序、刷新等待、技能差异识别、锁定确认和完成日志；默认禁用并通过现有 RobotInstruction SQLite/XML 链路保存。
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
- 封包列表开始捕获时固定启用 `4D5A` 包头显示白名单，并清理旧显示队列；右键提供“仅显示4D5A包头”快捷操作。规则复用现有全局过滤配置，不改变底层网络行为。
- 已移除“坐机速度”机器人预设及其离线验收窗口；加载旧数据库时会清理同名历史预设，其他机器人和本地验收报告不受影响。
- 优化藏宝图助手启动边界：普通机器人现在按现有游戏连接启动，C6 只在执行藏宝图动态指令时懒加载；C6 启动优先解析当前前台 Android 包名，并保留配置/已知包名回退，包名变化不再直接阻塞普通机器人。

## 已验证

- 使用 VS 2022 MSBuild 对 `WPELibrary.csproj` 做隔离 Debug 重建（`MemoryReaderValidation`）通过，0 个错误；`tests/ReadOnlyProcessMemoryReaderRegression.ps1` 通过，覆盖自有测试进程缓冲区读取、身份复核、指针读取、非法参数和释放后拒绝，并检查读取器未引用写内存/远程线程/扫描 API。
- 使用 Visual Studio 2022 MSBuild 完成 `WPELibrary.csproj` Debug 构建，并完成主程序 Debug 构建（`SignManifests=false`）：0 个错误；保留原有 4 个 EasyHook 清单警告。
- 视觉审查修复、颜色/ONNX、SQLite 持久化、state machine、capture enhancements、diagnostics 和 OCR runtime 回归通过。
- `VisionCaptureRegression.ps1` 通过；当前桌面环境无法提供真实屏幕帧，因此详细屏幕诊断项按环境限制透明跳过。
- WinForms vision UI audit 通过，窄窗口截图已复查。
- 视觉等待指令映射、视觉 UI、视觉持久化和状态机回归通过；窄窗口审计截图确认右侧指令集显示视觉等待行。
- `SummonedPetSkillBookRunnerRegression.ps1` 通过；覆盖文件结构、语法、项目引用以及 `SKILL_DIFF` 前置快照修复验证（`StudyBookAsync` 不再覆盖 `_previousPetState`），并确认 `TestAdapters.cs` 共享状态模式已就绪（`ScriptableGameState` + `SharedState` + 同步化模拟方法）。
- 已创建真实执行的 `PetSkillBookRunnerHarness`（`tests/PetSkillBookRunnerHarness/`）：包含 `Program.cs` 多场景测试（完整流程/缺书/拒绝/非参战/无效预设/LockAfter）及 `PetSkillBookRunnerHarness.csproj` 跨源码编译配置；适配器已修正 `Const` 限定/`CS1998` 异步警告/构造歧义问题。
- `VisionFixedClientSizeRegression.ps1`、`VisionRobotFormRealAcceptance.ps1`、`VisionRealAcceptance.ps1`、`VisionColorAndOnnxRegression.ps1` 和 `UiDesignRegression.ps1` 均通过；主程序 UI 回归使用隔离输出目录 `WinsockPacketEditor\bin\FixedSizeDebug` 验证。
- `WPELibrary.csproj` Debug 重建通过（0 警告、0 错误）；主程序隔离 Debug 构建通过（0 错误，保留 4 个既有 EasyHook 清单警告）。默认 Debug 输出仍被运行中的 `小黑封包助手.exe` 占用，因此未覆盖运行中的正式输出。
- 删除前的“坐机速度”预设回归、程序集窗体构造和本地 `self-test-only` 报告验证记录保留在历史变更中；删除后的回归以当前项目测试脚本和构建结果为准。
- 2026-08-22 已生成未签名本地 ClickOnce 热更新 `releases/2026.8.22.4/`（历史发布记录）：Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；该版本仍包含本次删除前的“坐机速度”预设。
- 2026-08-22 已生成签名本地 ClickOnce 热更新 `releases/2026.8.22.5/`：Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；EXE、WPELibrary、应用清单、固定入口版本均为 `2026.8.22.5`，清单签名验证通过，发布目录包含 200 个文件和 86 个 `.deploy` 文件。桌面快捷方式已备份并更新到固定入口及 `.5` 图标，旧发布目录、数据库和用户配置保留。
- 2026-08-22 本轮藏宝图启动优化已通过隔离 `WPELibrary` Debug 构建（0 错误）、`AssistantManagementRegression.ps1`、`TreasureC6StartupRegression.ps1`、`TreasureC6StreamRegression.ps1` 和 `git diff --check`；未启动真实 C6 读取器，未执行真实 Jump/Use 或其他游戏封包发送。
- **2026-08-22 完整回归**：`SummonedPetSkillBookRunner` 生命周期完整改造，包含：
  1. **VerifiedProcessIdentity** 流程：StartAsync 阶段首次调用，失败即暂停；
  2. **银两成本可配置**：Preset 新增 `OpenSlotSilverCost` / `StudySilverCost` 字段，默认 0 表示不检查；
  3. **State 枚举覆盖完整状态机**（IDLE/VERIFY_CURRENT_PET/.../NEXT_BOOK），配合 `SetState` 全链路更新；
  4. **SKILL_DIFF 前置快照保护**：`StudyBookAsync` 不再覆盖学习前 `_previousPetState`，维持“学习后 vs 学习前”的准确变更检测；
  5. **TestAdapters 共享状态**：`ScriptableGameState` 公开化，`SharedState` 属性让 Runharness 端到端验证；
  6. **Catalog 匹配 ItemId+SkillId**：防止误读其他书；
  7. **LockAfter 流程完善**：跳过 LOCK 阶段仍走 NEXT_BOOK；
  8. **WPELibrary 零警告重建**：`.editorconfig` `warnaserror+ true` 启用，全部通过；
  9. **离线测试 Harness 就绪**：`tests/PetSkillBookRunnerHarness/` 包含 19 大场景自动化验证；`build_test.ps1` 成功生成可运行 DLL。
- **2026-08-22 实际执行验收**：Harness 编译后运行，验证 19 大场景全部通过：
  - 场景 1 (Happy Path)：完整流程成功，银两消耗正确（2000→1600），技能写回、锁定均正常；
  - 场景 2 (Missing Book)：因物品 ID 99999 不在目录，正确暂停并提示缺失；
  - 场景 3 (Empty Book List)：空书单检测有效，PauseReason 包含“空”；
  - 场景 4 (Missing Open Material)：缺少开格材料 ITEM=1001，Runner 正确暂停；
  - 场景 5 (Silver Insufficient)：银两不足时暂停，PauseReason 包含“银两不足”；
  - 场景 6 (Operation Rejected)：学习被拒绝，Runner 正确暂停；
  - 场景 7 (Operation Unknown)：操作结果未知时暂停；
  - 场景 8 (Pet Not Participant)：宠物 ID=0 设为非参战，预设执行前即暂停；
  - 场景 9 (Pet Id Changed)：宠物ID在运行中变化，Runner 正确暂停；
  - 场景 10 (Process Identity Failed)：身份验证失败立即暂停；
  - 场景 11 (Open Slot Refresh Timeout)：开格刷新超时暂停；
  - 场景 12 (Study Refresh Timeout)：打书刷新超时暂停；
  - 场景 13 (Zero Skill Diff)：技能差异为0时暂停；
  - 场景 14 (Multiple Slot Changes)：多个槽位变化时暂停；
  - 场景 15 (Lock Failed)：锁定操作被拒绝时暂停；
  - 场景 16 (Lock Refresh Timeout)：锁定刷新超时暂停；
  - 场景 17 (Lock Verify Failed)：锁定验证失败时暂停；
  - 场景 18 (LockAfter False)：LockAfter=false 时不锁定，完成进入 COMPLETE；
  - 场景 19 (Sequential Two Books)：两本技能书按顺序执行，状态机顺序正确（NEXT_BOOK 在 COMPLETE 之前）；
  - NEXT_BOOK 状态新增日志输出，State.NEXT_BOOK 枚举确保状态机完整；
  - VerifyProcessIdentityAsync 首次调用验证，身份失败立即暂停；
  - 银两成本从 Preset 中读取（OpenSlotSilverCost/StudySilverCost）；
  - 修复 OpenAllSlots=true 时仍进入 COMPLETE 的 Bug。
- **2026-08-22 阶段 2 收口复核**：修复状态日志先后顺序、连续开格时的状态标记、Windows PowerShell 5.1 的 UTF-8 脚本 BOM，以及 Harness 输出标记的代码页依赖；`WPELibrary.csproj` 与 `PetSkillBookRunnerHarness.csproj` 隔离重建均为 0 错误、0 警告，Harness 19 个场景全部通过，`SummonedPetSkillBookRunnerRegression.ps1` 在 Windows PowerShell 5.1 下通过；`SummonedPetSkillBookPresetRegression.ps1`、`ReadOnlyProcessMemoryReaderRegression.ps1` 和 `git diff --check` 通过。仍未接入真实宠物偏移、技能目录、背包读取或开格/打书/锁格业务动作，未启动游戏、未使用 OCR/截图、未注入、未发送真实游戏封包。
- `MobileSync_Controller` 已加入 Web API 程序集；主程序 Debug 重建通过（0 个错误、4 个既有 EasyHook 清单警告），反射检查确认 manifest、snapshot、runtime 以及发送/递进/助手启动停止路由均已注册；预设快照空数据冒烟检查通过。
- `mobile` Debug APK 构建、Android Lint 和雷电模拟器 `emulator-5554` 安装通过；模拟器 UI 冒烟确认连接配置、发送/递进/助手三块预设列表和启动/停止按钮均可显示，断开连接时能保留错误状态而不崩溃。
- 2026-08-04 本轮修复后重新完成 Release 和隔离 Debug 解决方案重建：均为 0 个错误，保留 4 个既有 EasyHook `MSB3178` 清单警告。`VisionReviewFixesRegression.ps1`、视觉采集/诊断/OCR/状态机/持久化/颜色与 ONNX/固定客户区回归、`ReviewRegressionChecks.ps1`、首页/发送/字节/可读内容回归、Release 视觉 UI 审计和发送预设 UI 审计均通过；`git diff --check` 通过。未执行真实目标注入、抓包或封包发送。
- 2026-08-04 已通过本地 ClickOnce 热更新生成 `releases/2026.8.4.8/`：Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告；应用清单 49 个文件节点、EXE/部署清单/固定入口版本一致，文件大小和 SHA-256 校验通过。当前用户证书存储无可用私钥证书，因此本次为未签名本地包；桌面 `小黑封包助手.lnk` 已备份并继续指向固定入口，图标已更新到 2026.8.4.8。发布后代码、UI、视觉采集/OCR/模板/状态机/持久化回归及 `git diff --check` 均通过；未执行真实目标注入、抓包或封包发送。
- 2026-08-07 已通过本地 ClickOnce 热更新生成未签名 `releases/2026.8.7.0/`：Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告；发布目录 192 个文件、82 个 `.deploy` 映射文件，主程序/清单/ONNX sidecar/Python Worker 文件均通过存在性和版本校验。桌面 `小黑封包助手.lnk` 已备份并更新到固定入口及 2026.8.7.0 图标；旧发布目录、数据库和用户配置保留。
- 2026-08-07 Python Worker 部署验收通过：官方 Python 3.12.10 当前用户运行时、Airtest 1.4.3、RapidOCR 3.9.2、ONNX Runtime 1.28.0 可导入；JSONL `ping` 报告三项能力可用，RapidOCR 合成 `TASK 42` 返回成功且置信度约 0.995，C# JSONL 桥接同样通过。C# Debug 重建、视觉回归、协议回归、Worker 运行时回归和代码审查通过；未选择真实目标、未执行 Airtest 输入、未注入、未抓包或发送真实封包。
- 2026-08-21 已生成未签名本地 ClickOnce 热更新 `releases/2026.8.21.0/`：Release 构建、ClickOnce 清单/依赖、代码审查、发布版 UI 回归和 `git diff --check` 均通过；桌面 `小黑封包助手.lnk` 已保留固定入口并更新到 2026.8.21.0 图标，旧发布目录、数据库和快捷方式备份保留。发布包包含开始捕获时固定启用的 `4D5A` 显示白名单；未执行真实目标注入、抓包或封包发送。

## 尚未验收

- 尚未取得目标游戏运行时的版本化内存布局证据，因此召唤兽、背包、技能格和技能书目录尚未接入真实内存读取；本轮也未启动或读取真实游戏进程。
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

## 2026-08-20 注入目标自动绑定第一步

- 注入器新增按进程身份选择最新目标的解析器：优先选择 `0dcloudCore`，按进程启动时间排序，PID 只作为同一时刻的回退排序依据；不再把上一次 PID 当作自动绑定依据。
- `0dcloudCore` 纳入可注入目标列表；启动注入器、自动注入和目标存活校验都会重新解析当前实例，避免模拟器/游戏网络进程重启后继续使用旧 PID。
- 主程序隔离 Debug 重建通过：0 个错误，保留 4 条既有 EasyHook `MSB3178` 清单警告；反射调用解析器已在当前运行环境解析到 `0dcloudCore` PID 34932。仅完成进程绑定层，尚未改变藏宝图坐标、Jump 封包或挖宝动作。

## 2026-08-20 藏宝图状态到封包转换与发送接线第三步

- `WPELibrary/Lib/Vision/TreasurePacketTemplatePatcher.cs` 对已确认的 Jump `0x5828` 和 Use `0x783A` 模板执行定点字节替换，Use 只改 `pos`，不改 `type/num/param` 或长度字段。
- `WPELibrary/Lib/Vision/TreasurePacketRuntime.cs` 从当前出站封包列表发现模板，解析常驻状态 JSON 的四字段藏宝图成员，并生成不携带旧 Socket 的准备帧；该转换在运行时代码中完成，不依赖 Agent。
- `SendPreparedPacketOnce` 仅接受显式 `TREASURE-LIVE-SEND` 授权令牌，并在发送前重新从当前捕获列表解析 Socket；本轮没有执行真实网络发送。
- 新运行时的 Roslyn 隔离编译、状态桥接和未授权发送闸门测试通过；`TreasurePacketTemplatePatcherRegression` 在既有库加隔离运行时程序集下通过，Python 转换器回归 `5/5`。完整 `WPELibrary` 重建当时仍被 `Socket_RobotForm.cs` 既有未完成藏宝图 UI 改动阻塞（2026-08-20 历史记录，已由 2026-08-21 构建结果覆盖）。

## 2026-08-21 当前四步链路重新构建

- 主解决方案 Debug 构建（关闭本机 ClickOnce 清单签名）已通过：0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；`tests\RealAcceptanceControl\RealAcceptanceControl.csproj` 构建通过：0 个警告、0 个错误。
- `TreasurePacketEncoderRegression.ps1`、`TreasurePacketTemplatePatcherRegression.ps1`、`ReviewRegressionChecks.ps1` 和 `git diff --check` 均通过。上一条“完整 WPELibrary 重建被阻塞”的记录已被本次构建结果覆盖，但历史记录保留不改。
- 当前注入器按进程启动时间选择最新的 `Ld9BoxHeadless`；`0dcloudCore` 仍属于支持列表，但不是当前 WPE 网络宿主的首选目标。当前 resident reader 状态为只读、未授权动作，检测到 1 张宝图：`slot/packageNum=13`、`mapId/scene=1009`、`x=19`、`y=65`。
- 四步边界重新确认：第 1 步进程绑定已接线；第 2 步已有 resident 状态桥，但 WPE 侧目前由验收控制台按需读取 JSON，尚未形成生产态持续订阅；第 3 步编码/槽位替换已接线并通过回归；第 4 步发送入口已接线并保留显式 `TREASURE-LIVE-SEND` 闸门，本轮仍未进行真实网络发送。

## 2026-08-21 全代码审查修复

- 修复 `WSARecv`/`WSARecvFrom` 多 `WSABUF` 过滤后的连续字节拷贝偏移错误；异步 WSA 调用继续直通，Hook 异常返回改为设置 Winsock 错误或接收拦截错误，不再静默返回成功/EOF。
- 滤镜执行改用线程安全快照，执行计数与 UI 绑定对象分离；筛选器保存、配置保存和退出保存继续走原子事务，并新增跨进程命名 Mutex，避免多个注入目标相互覆盖 SQLite。
- 修复滤镜首次新增时空 `DataGridView` 行索引异常；修复 Python Worker 重置与请求并发、进程/图标资源释放、OWIN 请求/响应体大小边界和窗口初始化异常吞噬问题。
- MobileSync 改为“局域网边界 + 独立低权限代理账号”认证；Android 端使用 Keystore AES/GCM 保护密码并通过 Basic Auth 请求，认证不再复用管理员账号。原 2026-08-10 的完全免密记录保留为历史事实，但当前实现已由本次安全修复覆盖。
- 主解决方案隔离 Debug 重建通过，0 个错误、0 个新增警告；`ReviewRegressionChecks.ps1`、`FullCodeFixRegression.ps1`、`PersistenceAndSafetyRegression.ps1`、`AutomationHomeRegression.ps1`、`AssistantManagementRegression.ps1`、`DynamicVariableRegression.ps1`、`ByteSweepCoreBoundaryRegression.ps1`、`CryptoRegression.ps1`、`AtomicSaveRuntimeRegression.ps1`、藏宝图协议回归及 `WSABufferReceiveRegression.ps1` 通过。WinForms 运行时回归在 Windows PowerShell 5.1 + 隔离 Debug 产物下通过。
- `MobileSimulatorRegression.ps1` 的源码/协议/认证静态断言已通过；最终 APK 文件检查因本机没有 Java、Gradle 或 Android SDK 而停止，不能记为 Android 构建通过。
- 当前机器没有 Java/Android SDK，Android APK、Lint 和 Java 模型回归本轮未执行；没有执行真实注入、目标 Socket 发包、真实 HTTPS 或 MobileSync 业务动作。

## 2026-08-21 C6 持续订阅与藏宝图助手预设实现

- 新增 `TreasureC6StreamClient.cs`：WPE 内置 C# TCP 客户端连接默认 `127.0.0.1:28765`，校验 C6 schema-2 JSONL 的握手、序列、会话、进程/容器身份、完整快照和 added/removed/changed 事件；断线或协议失败清空当前可消费快照并重连等待。
- 新增 `TreasureMapPresetRunner.cs`：生产路径直接消费 C6 当前成员，按 `packageNum` 升序逐张执行 Jump `0x5828` → 1500ms（初版，后续调整为 300ms）→ 高级 Use `0x783A`（`type=13,num=1,param="2"`）。新增机器人 `TreasureMap` 指令、现有封包页“加入藏宝图流程”按钮和本次运行真实发送复选框，复选框默认关闭且不持久化。
- `TreasurePacketRuntime.PrepareEncodedFromTarget` 直接把 C6 的 `packageNum/scene/x/y` 绑定为封包字段；发送失败、路由失败、准备失败和 C6 传输失败均写入现有日志并在当前步骤重试，不跳过、不等待服务端返回、不操作游戏 UI。
- 已通过主解决方案 Debug 构建（0 错误，保留 4 条既有 EasyHook `MSB3178` 警告）、`RealAcceptanceControl` 构建、`tests\TreasureC6StreamRegression.ps1`、`tests\TreasureC6StreamRegression\TreasureC6StreamRegression.csproj`/exe、既有封包回归、`ReviewRegressionChecks.ps1`、`PersistenceAndSafetyRegression.ps1` 和 `git diff --check`。
- 当前仍未执行真实游戏封包发送或完整挖宝验收；需要用户在已注入、已建立当前 Socket 路由并完成 ADB forward/C6 实时流后，勾选本次运行授权再做现场验收。未勾选时运行器会记录 `live_send_not_authorized` 并持续等待/重试。

## 2026-08-21 发送预设按封包独立跟随当前连接

- 新增 `Socket_Cache.SocketList.CurrentSocketRoute`、`ResolveCurrentRoute` 和 `ResolveCurrentRoutes`；逐封包返回当前 Socket、源地址、目标地址和捕获时间，旧句柄失效时会通过 `getsockname/getpeername` 排除，不主动建立 TCP 连接。
- 普通单包、普通预设、批量发送、快捷键、移动端发送、移动端字节递进和桌面字节递进均改为逐封包/逐预设路由；全量路由预检失败时不启动发送线程，`runtime_route_ambiguous` 明确提示多候选连接并拒绝猜测。
- `Socket_Send.StartSendWithPacketSockets` 让发送线程使用每个封包自身的 Socket、源地址和目标地址；运行时地址只作用于发送任务和界面临时显示，不回写预设数据库/XML。新增 `tests/SocketRouteResolutionRegression.ps1` 覆盖重连换句柄、目标变化、歧义、多个连接、无连接和预设不变。
- 已通过现代 VS 2022 MSBuild 的主解决方案 Debug/Release 构建；Debug/Release `UiDesignRegression.ps1`、实时路由、发送批量、字节批量、完整代码、Review、持久化安全、字节递进边界/组合和 WSA 接收回归通过，并通过 `git diff --check`。Android APK/Lint 未执行，原因是本机没有 Java/Gradle/Android SDK；未执行真实重新注入、抓包或目标封包发送。

## 2026-08-21 藏宝图状态机开发启动

- 将藏宝图运行器纳入显式状态机：当前快照/持续监听两种模式、目标身份与坐标版本复核、Jump 后固定间隔、Use 前 C6 复核，以及 `NotDispatched/Dispatched/Ambiguous` 三态发送结果。
- 接通机器人暂停门、取消令牌、停止释放和结构化运行状态；桌面模式选择写入版本化指令内容，旧纯封包指令按兼容规则解析。
- 移动端助手完成回调会区分 `completed`、`faulted`、`cancelled` 和 `ambiguous`；桌面运行结束后撤销本次真实发送复选框授权并显示结果状态。
- 按当前需求移除 Jump 后角色到达证据校验；Jump 成功后按配置等待最短间隔再复核目标并进入 Use。该间隔不代表角色坐标已确认，本轮未执行真实网络发送。
- 根据本次确认，将 Jump→Use 默认最短等待调整为 300ms；测试可通过 `JumpUseDelayMilliseconds` 覆盖该值，本轮未执行真实网络发送。
- Use 前复核改为只检查当前已接收的 C6 快照，不再阻塞等待下一条 C6 事件；新增回归覆盖“无新事件仍继续 Use”的行为。
- 按本次确认移除 Use 后的 C6 消费确认等待；Use 明确 `Dispatched` 后立即标记当前目标流程完成并进入下一张，不再等待 `removed` 或快照消失。
- 本次调整后主解决方案 Debug 构建、`TreasureC6StreamRegression`、协议脚本、Review、FullCodeFix、持久化安全回归和 `git diff --check` 均通过；未执行真实网络发送。
- 使用 VS 2022 Build Tools MSBuild 完成 `WPELibrary` Debug 重建和 `WinSockPacketEditor.sln` Debug 构建（`SignManifests=false`、关闭 ClickOnce 清单生成），0 个错误；`TreasureC6StreamRegression` 可执行回归、协议 PowerShell 回归、封包编码/模板回归、Review、FullCodeFix、UI 和持久化安全回归均通过，`git diff --check` 通过。

## 2026-08-21 藏宝图助手启动卡死修复与循环语义确认

- 修复助手按钮启动死锁：原 UI 线程同步等待 `DoRobot`，后台启动又同步回调 UI，双击时会互相等待；现在桌面按钮和热键索引启动均走异步 `DoRobotAsync`，并在启动期间屏蔽重复点击/编辑。
- 当前默认模式为“当前快照（一次）”：读取并按 `packageNum` 处理这一批目标后结束，不会自动开启第二轮，也没有固定的第二轮等待时间。
- 只有选择“持续监听”才会循环；单张完成后立即检查当前快照，若没有新目标则阻塞等待下一条 C6 事件，不按固定秒数重复同一目标。
- 本轮主解决方案 Debug 构建、`TreasureC6StreamRegression.ps1`、`AssistantManagementRegression.ps1`、`ReviewRegressionChecks.ps1`、`FullCodeFixRegression.ps1`、`PersistenceAndSafetyRegression.ps1` 和 `git diff --check` 通过；`UiDesignRegression.ps1` 在窗体实例化阶段受当前 .NET BinaryFormatter 禁用限制，未记为通过。

## 2026-08-21 藏宝图助手默认改为持续监听

- 新建藏宝图助手预设、预设选项默认值、指令解析回退、机器人运行状态和助手界面下拉框均改为 `Continuous`。
- 已明确保存为 `TREASURE_MAP_V2|mode=current` 的一次模式预设保持原语义，不会被默认值覆盖；旧版纯封包指令仍按兼容规则解释为持续模式。
- 回归测试中的一次性场景已显式指定 `CurrentSnapshot`，避免测试依赖默认值；主解决方案 Debug 构建、藏宝图回归、助手管理回归、Review、FullCodeFix、持久化安全回归均通过，`git diff --check` 通过。

## 2026-08-21 持续监听默认模式桌面版热更新

- 已生成未签名本地 ClickOnce 版本 `releases/2026.8.21.1/`；Release 隔离构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- 发布目录包含 200 个文件；应用清单版本、部署清单版本、固定入口版本和 57 个文件节点一致，清单依赖摘要已核对，必需 EXE/DLL、SQLite、ONNX Runtime 和 Python Worker sidecar 均存在。
- 固定入口为 `releases/小黑封包助手.application`；桌面 `小黑封包助手.lnk` 已更新到 `2026.8.21.1` 图标，旧快捷方式备份保存在 `releases/shortcut-backups/小黑封包助手_20260821_101535.lnk`。
- 已通过 Release UI、藏宝图、助手管理、Review、FullCodeFix 回归和 `git diff --check`；`ClickOnceUpdateRegression.ps1` 的签名断言因本次明确使用 `-Unsigned` 未通过，不能把本包描述为可公网分发版本。
- 未执行真实目标注入、抓包或封包发送；启动更新后需关闭旧实例，再从桌面快捷方式启动以加载新版本。

## 2026-08-21 助手入口状态显示与藏宝图启动授权

- 主助手入口按简洁 UI 规范显示三种可见结果：普通状态保留助手名称，启动阶段显示蓝色“启动中”，运行阶段显示黄色“运行中”；悬停提示和辅助功能描述补充助手名称及停止操作，不增加独立状态面板。
- 主入口识别藏宝图指令后弹出本次运行发送确认，并仅向当前运行实例传入 `TreasureLiveSendEnabled=true`；修复此前主入口未传授权、运行器在 Jump 前直接记录 `live_send_not_authorized` 的问题。授权不持久化。
- Debug/Release 主解决方案构建、`AssistantManagementRegression.ps1`、`TreasureC6StreamRegression.ps1`、`ReviewRegressionChecks.ps1`、`FullCodeFixRegression.ps1`、`PersistenceAndSafetyRegression.ps1`、Release `UiDesignRegression.ps1` 和 `git diff --check` 均通过。
- 已生成未签名本地 ClickOnce 热更新 `releases/2026.8.21.2/`；发布目录 200 个文件、部署/应用/固定入口版本均为 `2026.8.21.2`，应用清单 57 个文件节点及摘要已核对。桌面快捷方式已备份并更新到固定入口和该版本图标；`ClickOnceUpdateRegression.ps1` 仅因未签名包的签名断言失败。
- 未执行真实目标注入、抓包或封包发送；需关闭旧实例后从桌面快捷方式重新启动，才能加载本次版本。

## 2026-08-21 助手状态回调边界热更新

- 补充 Worker 极快完成和后台线程回调的 UI 线程收敛，避免助手按钮因完成事件竞态卡在“运行中”。
- 已重新生成未签名本地 ClickOnce `releases/2026.8.21.3/`；发布目录 200 个文件、部署/应用/固定入口版本均为 `2026.8.21.3`，57 个清单文件节点及摘要通过核对。桌面快捷方式已备份并更新到该版本图标，固定入口保持不变。
- Release UI、助手管理、藏宝图、Review、FullCodeFix 和 `git diff --check` 通过；ClickOnce 签名回归因明确使用未签名本地包而保留失败状态。

## 2026-08-21 自动藏宝图空流程与启动反馈修复

- 定位到当前数据库中的“自动藏宝图”记录存在但 `RobotInstruction` 为 0 行；主入口因此在创建 Worker 前直接返回，导致按钮不显示“运行中”、也不会尝试 Jump。
- 加载助手列表时，若精确匹配“自动藏宝图”且流程为空，会自动补写一条 `TreasureMap` 持续监听指令并保存；主入口另有一次性兜底修复，避免旧数据未重启时仍无流程。
- 助手启动返回空对象时显示启动失败；C6 连接失败、超时或断开时显示 `127.0.0.1:28765` 前置条件提示，并保留按钮的简洁“启动中/运行中”状态，不新增状态面板。
- 已通过主解决方案 Debug 构建（0 个错误）、`AssistantManagementRegression.ps1`、`TreasureC6StreamRegression.ps1`、`UiDesignRegression.ps1`；尚未执行真实 C6/游戏连接和真实封包发送。当前机器检查到 `127.0.0.1:28765` 无监听，现场测试前需启动游戏端 C6 reader/ADB forward。
- 已生成未签名本地 ClickOnce 热更新 `releases/2026.8.21.4/`；发布目录 200 个文件、应用/部署/固定入口版本均为 `2026.8.21.4`，清单 57 个文件节点与 57 个摘要节点已核对。桌面快捷方式已备份并更新到 `2026.8.21.4` 图标，固定入口保持 `releases/小黑封包助手.application`。
- Release 构建 0 个错误、保留 4 条既有 EasyHook `MSB3178` 警告；Release UI、助手管理、藏宝图、Review 和 `git diff --check` 通过。未签名包的 ClickOnce 签名断言不作为通过结果。

## 2026-08-21 藏宝图 C6 自动启动与一次性授权

- 新增 `TreasureC6ServiceController`：藏宝图助手启动时自动发现 `adb.exe`、在线模拟器和当前游戏 PID，复用或启动设备内只读 `treasure-streamd-resident`，并建立 `tcp:28765 -> localabstract:piaomiao.treasure.stream` 转发；不发送游戏封包，也不在每次运行结束时拆除转发。
- `Socket_RobotInfo.TreasureLiveSendAuthorized` 按助手持久化到 SQLite，并兼容旧数据库迁移、机器人复制、XML 导入导出和编辑器复选框。主入口首次确认后保存授权，后续启动不再重复弹窗；编辑器可取消保存设置。
- C6 自动准备失败会在启动阶段直接显示可定位原因（ADB、设备、游戏进程、读取器或转发），不再要求用户每次手动授权启动 C6 服务。
- VS 2022 MSBuild Release 无签名编译通过（0 错误，保留 4 条既有 EasyHook `MSB3178` 警告）；助手管理回归通过。只读联调已在 `emulator-5554` 验证控制器成功发现游戏 PID `1858`、启动读取器并建立 `28765` 转发，C6 客户端握手连接成功；未执行真实 Jump/Use 封包发送。

## 2026-08-21 C6 自动启动热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.6/`；Release 隔离构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告，应用/部署/固定入口版本均为 `2026.8.21.6`，发布目录 200 个文件，必需 EXE/DLL/SQLite/manifest 文件及清单摘要已核对。
- 固定入口保持 `releases/小黑封包助手.application`；桌面快捷方式目标不变，图标已更新到 `2026.8.21.6`，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_113914.lnk`。
- `ReviewRegressionChecks.ps1`、藏宝图/助手管理/持久化安全回归和 `git diff --check` 通过；`UiDesignRegression.ps1` 仍受当前环境 BinaryFormatter 禁用限制，未记为通过；未签名包的 ClickOnce 签名断言按规则保留失败状态。

## 2026-08-21 热更新后现场复核

- 桌面快捷方式仍指向固定入口 `releases/小黑封包助手.application`，其部署版本为 `2026.8.21.6`；对应发布目录存在 200 个文件，发布版 `WPELibrary.dll` 可加载 `TreasureC6ServiceController`。
- 当前数据库中的“自动藏宝图”包含 1 条流程指令，`TreasureLiveSendAuthorized=False`，未在复核过程中替用户开启真实发送授权。
- 只读 C6 现场复核通过：ADB 设备 `emulator-5554` 在线，游戏 PID `1858` 可发现，`127.0.0.1:28765` 转发存在，发布版控制器返回 `ready`；未发送 Jump/Use 游戏封包。
- `AssistantManagementRegression.ps1`、`TreasureC6StreamRegression.ps1`、`PersistenceAndSafetyRegression.ps1`、`ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1` 和 `git diff --check` 均通过。
- 当前未运行桌面助手进程；下一步应由用户从桌面快捷方式启动并点击助手按钮做实际界面验收。ClickOnce 签名回归仍因本包明确为未签名本地包而失败，不影响本机测试。

## 2026-08-21 藏宝图复用已有连接与桌面热更新

- 藏宝图路由获取改为优先使用当前会话；当前会话为空时，从已有封包列表解析并校验仍有效的游戏 socket，和普通发送预设复用连接的行为一致，不再把点击左上角“开始监听”作为硬前置。
- 保留当前 socket 校验和多连接歧义拒绝；无有效连接时仍返回 `current_route_not_found`，不会猜测目标连接。新增回归覆盖“未启动新监听、但已有 IPv4 连接可解析路由”的场景。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；藏宝图、封包编码/模板、助手管理、持久化安全、Review、FullCodeFix、Release UI 和 `git diff --check` 均通过。
- 已生成未签名本地 ClickOnce `releases/2026.8.21.7/`，发布目录 200 个文件，固定入口版本为 `2026.8.21.7`；桌面快捷方式图标已更新，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_120649.lnk`。
- 发布版 DLL 的 C6 自动准备和已有连接路由回退均已只读验证；未执行真实 Jump/Use 封包发送。ClickOnce 签名检查仍因本包明确为未签名本地包而失败。

## 2026-08-21 C6 失步恢复与持续模式失败跳过

- 持续模式遇到 `stale_snapshot_rejected`、C6 协议失步或连接中断时，会丢弃旧基线、重新连接并等待完整快照；连续恢复失败后保持等待/监听，不自动结束助手。
- 持续模式中单张 Jump/Use 的确定性失败或结果不明确会记录为 `target_failed_skipped`，按成员、包号和坐标建立跳过键，直接监听下一张；不会重复发送该失败目标。一次性模式仍按原规则失败退出。
- 新增藏宝图回归覆盖 C6 失步重连和“失败目标跳过后继续处理下一目标”；通过 Debug/Release 构建、藏宝图、助手管理、持久化安全、Review、FullCodeFix、Release UI 和 `git diff --check`。未执行真实 Jump/Use 封包发送。
- 已生成未签名本地 ClickOnce `releases/2026.8.21.9/`；固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式图标已更新到 2026.8.21.9，备份为 `releases/shortcut-backups/小黑封包助手_20260821_122553.lnk`。ClickOnce 签名断言按未签名本地包规则保留失败。
- 正常 Jump 后等待仍为 300ms；尚未下调默认等待，现场可继续评估 200ms 的提速方案。

## 2026-08-21 持续监听复用背包槽位修复

- 定位到持续模式账本未清理：藏宝图 Use 完成后，`completedTargets`/失败跳过记录会保留成员与 `packageNum`；同一背包槽位重新放入新图时，可能被误判为上一张已处理，表现为运行一段时间后不再 Jump。
- 持续模式现在以 C6 当前完整快照为准清理已离开背包的目标记录；槽位消失后再次出现即可重新选择，C6 连接和持续监听语义不变。
- 新增回归覆盖“同一槽位移除后重新出现再次 Jump/Use”；WPELibrary Debug/Release 构建、藏宝图可执行回归、助手管理、持久化安全、Review 和 `git diff --check` 通过。未执行真实 Jump/Use 封包发送。

## 2026-08-21 藏宝图槽位复用修复热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.10/`；发布目录 200 个文件，固定入口清单已指向该版本，应用清单和必需 DLL/sidecar 存在。
- 桌面快捷方式 `小黑封包助手.lnk` 已备份并更新到固定入口及 `2026.8.21.10` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260821_124509.lnk`。
- `ClickOnceUpdateRegression.ps1 -AllowUnsigned`、藏宝图回归、助手管理回归、Review 和 `git diff --check` 通过；Release UI 回归仍因当前环境禁用 .NET BinaryFormatter，在窗体构造阶段失败，非本次槽位账本改动引起。
- 未关闭正在运行的桌面程序；当前未检测到小黑封包助手进程。未执行真实注入、抓包或 Jump/Use 封包发送；新版本需从桌面快捷方式重新启动后生效。

## 2026-08-21 藏宝图固定节拍与失败快速跳过

- 正常路径保持 Jump 后固定等待 300ms 再 Use；成功完成后增加默认 100ms 的下一目标节拍，已有目标时按约 400ms 起步间隔继续处理。
- 移除 Jump/Use 失败后的 `1/2/4/8/15` 秒长退避重试；发送未出站或结果不明确时按持续模式直接记录并跳过，一次性模式仍按原规则结束。
- 没有新藏宝图时继续由 C6 事件驱动等待，不增加固定轮询；C6 失步恢复逻辑保持独立，不会重复发送当前目标。
- 更新回归覆盖默认节拍、失败直接跳过和多目标持续处理；WPELibrary Debug/Release 构建、藏宝图、助手管理、持久化安全、Review、FullCodeFix 和 `git diff --check` 通过。未执行真实 Jump/Use 封包发送。

## 2026-08-21 固定节拍方案热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.11/`；发布目录 200 个文件，固定入口和应用清单均指向该版本，Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- 桌面快捷方式已更新到固定入口和 `2026.8.21.11` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_125738.lnk`；发布后检查为 `UpToDate`。
- `ClickOnceUpdateRegression.ps1 -AllowUnsigned`、藏宝图、助手管理、持久化安全、Review、FullCodeFix 和 `git diff --check` 通过；Release UI 回归仍因当前环境禁用 .NET BinaryFormatter 在窗体构造阶段失败，非本次节拍改动引起。
- 当前未检测到小黑封包助手进程；未执行真实注入、抓包或 Jump/Use 封包发送。启动新版本需从桌面快捷方式重新打开。

## 2026-08-21 藏宝图识别与发送诊断日志

- 自动藏宝图新增去重的 `recognition` 诊断事件，明确记录快照为空、未识别到有效藏宝图、识别到目标和等待新目标等状态；跳转前的目标缺失或校验失败也会记录原因。
- Jump/Use 日志将结果码和成功标记前置到文本，桌面日志列被截断时仍能直接看到“识别不到、跳过还是发送失败”的关键结果；识别诊断不计入发送次数、成功次数、失败次数，也不会把等待状态当成运行失败。
- WPELibrary Debug 构建、藏宝图可执行回归、Review、助手管理回归和 `git diff --check` 已通过；未执行真实 C6/游戏连接和 Jump/Use 封包发送。当前运行中的桌面旧版本不会自动加载本次源码改动，需重新构建/热更新后测试。

## 2026-08-21 藏宝图诊断日志热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.12/`；发布目录 200 个文件，固定入口仍为 `releases/小黑封包助手.application`，应用/部署清单版本均为 `2026.8.21.12`。
- 桌面快捷方式已更新到固定入口和 `2026.8.21.12` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_175649.lnk`。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；`ReviewRegressionChecks.ps1`、`TreasureC6StreamRegression.ps1`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned`、Release `UiDesignRegression.ps1` 和 `git diff --check` 通过。未执行真实注入、抓包或 Jump/Use 封包发送；当前未运行小黑封包助手，需从桌面快捷方式重新启动新版本。

## 2026-08-21 C6 新背包消息读取优化（源码完成）

- 生产藏宝图路径新增 `TreasureC6BufferedInventoryStream`：独立读取线程持续按协议顺序消费 C6 消息并更新最新快照，助手发送和 300ms 节拍不再直接占用网络读取；运行器只接收最新唤醒通知，避免旧周期快照在暂停或发送期间堆积。
- C6 等待超过 500ms 时增加 `recognition` 诊断 `c6_wait_ms=...`，可区分“助手在等新背包消息”和发送/识别失败；诊断不计入发送统计。
- C6 自动启动/复用改为请求 10ms 读取间隔；如果设备内已有同 PID、同 Socket 但仍为旧间隔的读取器，下一次助手启动会先替换为 10ms 常驻读取器。跳转后 300ms 和下一张默认 100ms 节拍保持不变。
- 已通过 WPELibrary Debug 重建、主解决方案 Debug 重建、藏宝图 C6 可执行回归（含缓存流测试）、TreasurePacketEncoder/TemplatePatcher、AssistantManagement、PersistenceAndSafety、Review 和 FullCodeFix 回归。主解决方案重建仅保留既有 `EasyLoad64.dll` 被 `0dcloudCore.exe` 占用的 MSB3061 警告；未执行真实 Jump/Use 发送，未生成 ClickOnce 热更新包。

## 2026-08-21 C6 新背包消息读取优化热更新

- 已生成未签名本地 ClickOnce 版本 `releases/2026.8.21.13/`；Release 隔离发布成功，发布目录 200 个文件，应用清单 57 个文件节点、31 个依赖节点，必需 EXE/DLL、ONNX Runtime 和 Python Worker 文件均存在。
- 固定入口 `releases/小黑封包助手.application` 已更新到 `2026.8.21.13`；桌面快捷方式仍指向固定入口，图标已更新到该版本，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_185436.lnk`。
- `ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned` 和藏宝图 C6 回归通过；本地包明确为 unsigned local release，未执行真实注入、抓包或 Jump/Use 封包发送。
- 旧桌面进程未被热更新流程结束；需要先关闭旧版，再双击桌面 `小黑封包助手.lnk`，新版本才会加载。

## 2026-08-21 C6 空槽位快照稳定性修复热更新

- 修复设备端 C6 读取器对 `packageNum=0` 空槽位的处理：live、fast-cache 和 fixture 快照不再发布空槽位；
  槽位重新出现物品时按 `removed → added` 处理，避免误判为槽位替换后反复触发 6～10 秒完整刷新。
- Android arm64 Release 构建和 Host CTest `1/1` 通过；新 ELF SHA-256 为
  `4CD53665DDA92B4D96C526E49C245C1C66B95AC09D3DE0F27949D33A1633F1DF`。已在 `emulator-5554` 的
  `/data/local/tmp/treasure-streamd-resident` 部署并重启常驻读取器，旧文件保留为
  `/data/local/tmp/treasure-streamd-resident.backup-20260821-01`；游戏进程未停止。
- 只读 C6 采样首次完整发现后连续约 250ms 刷新，未观察到 `degraded` 或 socket 重连；真实藏宝图增删和真实
  Jump/Use 封包发送未执行。
- 已生成未签名本地 ClickOnce `releases/2026.8.21.14/`，发布目录 200 个文件；固定入口和桌面快捷方式仍指向
  `releases/小黑封包助手.application`，图标已更新到 2026.8.21.14，备份为
  `releases/shortcut-backups/小黑封包助手_20260821_192414.lnk`。
- `ReviewRegressionChecks.ps1`、`TreasureC6StreamRegression.ps1`、`AssistantManagementRegression.ps1`、
  `PersistenceAndSafetyRegression.ps1`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned`、Release
  `UiDesignRegression.ps1` 和 `git diff --check` 均通过；本地包明确为 unsigned local release。

## 2026-08-21 C6 容器迁移与同槽位复用稳定性修复热更新

- 定位到剩余抖动来自三个设备端状态变化：Lua table 的 `Node` 在运行中迁移导致完整内存重扫、成员对象指针变化导致身份漂移，以及同一个 `packageNum` 背包槽位被下一张藏宝图真实复用。读取器现在先用已校验的 `BagMgr/m_ItemDict` 根快速重绑容器，以成员 key（且仍要求 `member.key == m_Id`）作为稳定身份；同槽位换图按 `removed → added` 发布，不再误报 `slot_replacement_unproven`。
- Android arm64 Release 构建和 Host CTest `1/1` 通过；最终 ELF SHA-256 为 `A1302B153553C7EA1A901253D6A3687EBDFFDD061F1BC4019ABD31AEE6FCFEB4`，已部署到 `emulator-5554:/data/local/tmp/treasure-streamd-resident`，替换前文件备份为 `/data/local/tmp/treasure-streamd-resident.backup-20260821-06`。当前游戏 PID `1852`、读取器 PID `3779`、`tcp:28765` 转发和 `@piaomiao.treasure.stream` 监听均存在。
- 最终只读连续采样 60.22 秒：收到 205 条消息、194 个完整快照和 10 个事件（5 次 removed、5 次 added）；容器身份始终为 1 个，`nonReady=0`，稳定阶段最大消息间隔约 271ms。冷启动首次完整内存发现仍约 10.9 秒；该耗时只发生在没有可复用根的首次绑定，不记为稳定阶段抖动。
- 已生成未签名本地 ClickOnce `releases/2026.8.21.15/`，发布目录 200 个文件，应用清单和固定入口版本均为 `2026.8.21.15`。桌面快捷方式保持指向 `releases/小黑封包助手.application`，图标已更新到该版本，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_200000.lnk`。
- `ReviewRegressionChecks.ps1`、`TreasureC6StreamRegression.ps1`、`AssistantManagementRegression.ps1`、`PersistenceAndSafetyRegression.ps1`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned` 和 Windows PowerShell 下的 Release `UiDesignRegression.ps1` 均通过；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。本次未执行真实 Jump/Use 封包发送。

## 2026-08-21 持续模式重复重连退出修复（源码完成）

- 用户现场复测期间游戏 PID 从 `1852` 变化为 `1987`，设备端读取器已自动换绑；随后桌面“自动藏宝图”按钮不再处于运行中。并行只读 C6 连续采样 180.06 秒收到 715 个快照，最大消息间隔 266ms、无超过 500ms 的间隔、无协议异常，确认空闲背包读取本身稳定。
- 定位到 `TreasureMapPresetRunner.ResyncC6Stream` 的第三次内部连接失败分支仍会直接抛出 `c6_resync_exhausted`，绕过持续模式已有的等待逻辑，导致游戏/读取器切换时助手低频退出。该分支现在与外层耗尽分支统一进入 `c6_resync_waiting`：断开旧连接、等待一秒、清零本轮次数并继续下一轮，不结束持续助手。
- 新增可重复失败的测试流，覆盖连续 6 次 `connect_failed` 后第 7 次恢复连接；.NET Framework Debug 可执行回归、WPELibrary Debug/Release 构建、PowerShell 藏宝图回归、助手管理回归、Review 和 `git diff --check` 均通过。
- 本节仅完成源码和本地验证，尚未生成新的 ClickOnce 版本；未执行真实 Jump/Use 封包发送。

## 2026-08-21 持续模式重复重连退出修复热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.16/`；Release 隔离构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告，发布目录 200 个文件，应用清单和固定入口版本均为 `2026.8.21.16`。
- 固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式已更新到固定入口和 `.16` 图标，发布后检查为 `UpToDate`，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_201705.lnk`。
- `ReviewRegressionChecks.ps1`、`TreasureC6StreamRegression.ps1`、`AssistantManagementRegression.ps1`、`PersistenceAndSafetyRegression.ps1`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned`、Windows PowerShell 下的 Release `UiDesignRegression.ps1` 和 `git diff --check` 均通过。ClickOnce 包明确为 unsigned local release，未执行真实注入、抓包或 Jump/Use 封包发送。
- 热更新流程未结束当前旧进程；现场仍显示旧窗口标题 `小黑封包助手 - 2026.8.21.15`。需要先关闭旧窗口，再双击桌面快捷方式，才能加载 `.16` 修复。

## 2026-08-21 C6 事件纪元与 Jump 后误跳过根因修复（源码完成）

- `.16` 现场日志共记录 35 次 Jump、30 次 Use、4 次 `target_changed_before_use` 和 3 次 `stale_snapshot_rejected`。协议根因是设备端发送增删事件后提前把服务端基线推进到尚未发给 Host 的新 `snapshotId`；下一条快速事件因此引用 Host 未确认的纪元并触发重连。
- 设备端 `StreamState` 新增事件应用路径：成员基线随事件更新，但在真正发送下一份完整 snapshot 前保留客户端已经确认的 `snapshotId`，连续事件不再制造 `stale_snapshot_rejected`。
- 桌面流程移除 Jump 后残留的 Use 前背包复核；Jump 成功后只等待默认 300ms，再按原目标槽位发送 Use，符合此前确定的“不验证到达、不二次检查背包”流程。手动取消在指令返回后同步为 BackgroundWorker 取消状态，不再误记为“执行完毕”。
- WPELibrary Debug 构建和 `TreasureC6StreamRegression` 可执行回归通过；Android Host 核心测试通过，arm64 Release 构建成功，新 ELF SHA-256 为 `491AE92F36E3FB24657AA36FEFCB2FF65F4BF0D253BF08D5FE99C382AF320C23`。本节尚未覆盖设备二进制或发布 ClickOnce，未执行真实 Jump/Use 封包发送。

## 2026-08-21 C6 事件纪元修复热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.17/`；发布目录 200 个文件，应用清单、部署清单和固定入口均为 `2026.8.21.17`。
- 桌面快捷方式仍指向 `releases/小黑封包助手.application`，图标已更新到 `.17`；旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_203933.lnk`。发布后状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned`、Release `UiDesignRegression.ps1` 和 `git diff --check` 通过；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。包明确为 unsigned local release，未执行真实 Jump/Use 封包发送。
- Android arm64 Release ELF `491AE92F36E3FB24657AA36FEFCB2FF65F4BF0D253BF08D5FE99C382AF320C23` 已热替换到 `emulator-5554:/data/local/tmp/treasure-streamd-resident`；旧文件保留为 `/data/local/tmp/treasure-streamd-resident.backup-20260821-07`。游戏 PID `1854` 未停止，读取器以 `--interval-ms 10 --resident` 重启，`tcp:28765 -> localabstract:piaomiao.treasure.stream` 转发和抽象 socket 均存在。
- 设备只读握手复核在读取器完成首次冷绑定后通过：收到 7 条有效消息、形成完整快照、无协议诊断；Host CTest `1/1` 通过。未执行真实 Jump/Use、游戏内存写入或注入。
- 新桌面版本需要关闭旧窗口后，再双击桌面 `小黑封包助手.lnk` 才会加载 `.17`；本次热更新未强制结束桌面进程。

## 2026-08-21 Jump 短重试与单图停顿修复（源码完成）

- 定位到“批量连续跳转后偶尔卡住一张、手动使用后恢复”的直接行为来源：持续模式把一次 Jump 未发送的目标立即写入 `skippedTargets`，该目标留在背包期间不会再次尝试。
- Jump 现在只对明确 `NotDispatched` 的结果做短重试：默认间隔 250ms，最多追加 2 次；任一次成功后继续固定 300ms 再发送一次 Use。Jump 结果不明确时不重复，Use 仍保持只发送一次。
- 每次短重试前重新检查 Hook 会话和当前目标版本；三次 Jump 均明确未发送后才记录 `target_failed_skipped` 并继续下一目标。新增回归覆盖一次失败后恢复、重试预算耗尽后跳过以及 Use 不重复；WPELibrary Debug/Release 构建、藏宝图可执行/PowerShell 回归、助手管理、Review 和 `git diff --check` 均通过。未执行真实 Jump/Use 封包发送，尚未生成新的 ClickOnce 热更新。

## 2026-08-21 Jump 短重试桌面热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.18/`；发布目录 200 个文件，应用清单、部署清单和固定入口版本均为 `2026.8.21.18`，必需的 `小黑封包助手.exe` 与 `WPELibrary.dll` 均存在。
- 固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式已更新到固定入口和 `.18` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_205747.lnk`，发布后预览状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、`TreasureC6StreamRegression.ps1 -BuildDirectory WPELibrary\\bin\\Release`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned` 和 `git diff --check` 均通过。Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；ClickOnce 包明确为 unsigned local release，未执行真实注入、抓包或 Jump/Use 封包发送。
- 本次只更新桌面端，设备端读取器未改动。若桌面仍有旧窗口，请先关闭，再双击桌面 `小黑封包助手.lnk`，使 `.18` 版本加载。

## 2026-08-21 藏宝图消费确认与持续补偿（源码完成）

- 修复持续助手把“Winsock 已接收完整发送缓冲区”直接当成“游戏已经使用藏宝图”的假完成问题。Use 发出后现在根据 C6 当前背包确认原目标消失或同槽位版本变化，确认前不再写入完成账本。
- 默认消费确认窗口为 1000ms；同一目标仍在时先立即完整重试一次 Jump→300ms→Use，第二次及以后仍未消耗则每隔 3000ms 重试整套流程，直到确认消费或用户手动停止。不会把这类目标永久跳过，也不会只重发 Use。
- 新增 `consume_confirmed`、`consume_timeout`、`target_retry`、`target_cooldown` 诊断；同槽位已换入新图时只完成旧目标，不会把新图误记为已完成。
- WPELibrary Debug 构建和 `TreasureC6StreamRegression` Debug 构建/可执行回归通过，均为 0 警告、0 错误；新增回归覆盖前两次 Use 未消耗、进入冷却、第三次确认成功且不写入 `target_failed_skipped`。未执行真实 Jump/Use 封包发送，尚未生成新的 ClickOnce 热更新。

## 2026-08-21 藏宝图消费确认桌面热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.19/`；Release 隔离构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告，发布目录和应用清单/固定入口版本均为 `2026.8.21.19`。
- 固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式已更新到固定入口和 `.19` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_213329.lnk`。
- `ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned`、Release 藏宝图回归和 `git diff --check` 均通过。ClickOnce 包明确为 unsigned local release，未执行真实注入、抓包或 Jump/Use 封包发送。
- 热更新流程未结束旧桌面进程；需先关闭旧窗口，再双击桌面 `小黑封包助手.lnk`，新版本才会加载。

## 2026-08-21 藏宝图补偿统计与冷却提前结束（源码完成）

- `.19` 现场日志共识别 13 张图，20 次 Jump 和 20 次 Use 均为 `sent`，发送失败、永久跳过和运行异常均为 0；界面失败次数增多的直接原因是把 `consume_timeout`、`target_retry`、`target_cooldown` 等补偿状态也累计为发送失败。
- 藏宝图运行统计现在只累计 Jump/Use 的真实发送结果；识别、连接、消费确认、确认超时、重试和冷却日志继续保留，但不再增加发送次数、成功次数、失败次数或覆盖最后发送错误。
- 第二次确认超时后仍保留最长 3000ms 冷却，但冷却期间每 25ms 检查当前 C6 背包；原目标一旦消失或同槽位换图立即确认完成并处理下一张，不再固定等待满 3 秒。
- 新增回归覆盖“冷却期间目标消失后提前结束且不发送第三组 Jump/Use”，并补充助手统计边界静态检查。本节尚未生成新的 ClickOnce 热更新，未执行真实 Jump/Use 封包发送。

## 2026-08-21 藏宝图补偿统计与冷却提前结束桌面热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.21.20/`；Release 隔离构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告，发布目录 200 个文件，应用清单、部署清单和固定入口版本均为 `2026.8.21.20`。
- 固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式已更新到固定入口和 `.20` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_214700.lnk`，发布后预览状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1 -AllowUnsigned`、Release 藏宝图回归和 `git diff --check` 均通过。ClickOnce 包明确为 unsigned local release，未执行真实注入、抓包或 Jump/Use 封包发送。
- 热更新流程未结束旧桌面进程；当前未检测到 `小黑封包助手` 进程。请双击桌面 `小黑封包助手.lnk` 加载 `.20`，运行时不要重复启动旧入口。

## 2026-08-21 藏宝图运行日志持久化（源码完成）

- 新增 `TreasureMapRunLogStore`，把 `TreasureMapPresetRunner` 的结构化运行记录同步写入 `%LOCALAPPDATA%\XNAS\WPE\treasure-map\logs\treasure-map.jsonl`；单文件上限 5 MiB，保留 5 份编号归档，文件使用共享读取并在每条记录后刷新，助手或注入进程退出后日志仍可分析。
- 每条 JSONL 包含 schema、UTC 时间、`runId`、进程 ID、步骤、逻辑包类型、结果码、重试、成功标记及安全的槽位/场景/坐标；不记录 C6 `memberIdentity`、原始 C6 payload 或完整封包字节。运行开始/结束也进入同一 `runId` 时间线，持久化初始化或写入失败只在现有系统日志报告一次，不中断藏宝图流程。
- 新增日志落盘、字段白名单、运行中可读和有界轮转回归；`AssistantManagementRegression.ps1`、`ReviewRegressionChecks.ps1`、`PersistenceAndSafetyRegression.ps1`、Debug/Release `WPELibrary` 构建、完整 Release 解决方案构建、Debug `TreasureC6StreamRegression` 可执行回归及 PowerShell 回归均通过，`git diff --check` 通过。完整构建保留 4 条既有 EasyHook `MSB3178` 警告；本节未生成 ClickOnce 热更新，未执行真实注入、抓包或 Jump/Use 封包发送。

## 2026-08-21 藏宝图运行日志持久化桌面热更新

- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.21.21/`；Release 隔离构建 0 个错误，发布目录 200 个文件，应用清单、部署清单和固定入口版本均为 `2026.8.21.21`，必需文件齐全。
- 固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式已更新到固定入口和 `.21` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_222809.lnk`，发布后状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、Windows PowerShell 5.1 下的 Release `UiDesignRegression.ps1`、Release `TreasureC6StreamRegression.ps1`、签名模式 `ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。完整 Release 构建保留 4 条既有 EasyHook `MSB3178` 警告。
- 本次仅更新桌面端藏宝图运行日志持久化；未结束旧桌面进程，当前未检测到 `小黑封包助手` 进程；未执行真实注入、抓包或 Jump/Use 封包发送。请双击桌面 `小黑封包助手.lnk` 加载 `.21`。

## 2026-08-21 藏宝图消费确认窗口修复（源码完成）

- 根据 `.21` 现场日志，21 张图均最终消费成功但 8 张触发重试，8 张最后一次 Use 后在约 1.33～1.92 秒才收到 C6 消费确认；原 1000ms 窗口会过早启动重复 Jump→Use。
- 默认消费确认上限调整为 2500ms；确认仍每 25ms 轮询，目标提前消失或换版本时立即进入下一张，不增加成功宝图的固定等待时间。
- 新增回归覆盖“Use 后 1200ms 才从 C6 背包消失，但不触发重复 Jump/Use”；默认时序断言、WPELibrary Debug 构建、TreasureC6StreamRegression 可执行回归及 PowerShell 回归均通过，`git diff --check` 通过。尚未生成本次修复的 ClickOnce 热更新，未执行真实 Jump/Use 封包发送。

## 2026-08-21 藏宝图消费确认窗口修复桌面热更新

- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.21.22/`；Release 隔离构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告，发布目录 200 个文件，应用清单、部署清单和固定入口版本均为 `2026.8.21.22`。
- 固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式已更新到 `.22` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260821_224401.lnk`，发布后状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、Release `TreasureC6StreamRegression.ps1`、签名模式 `ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过；两个 ClickOnce 清单均已确认带签名，必需 EXE/DLL/sidecar 文件齐全。
- 热更新流程未结束旧桌面进程，当前未检测到 `小黑封包助手` 进程；未执行真实注入、抓包或 Jump/Use 封包发送。请双击桌面 `小黑封包助手.lnk` 加载 `.22` 后再进行测试。

## 2026-08-21 藏宝图改用原生自动挖宝包（源码完成）

- 根据用户提供的抓包样本，新增固定 24 字节 `4D5A / 0xB0F4` 自动挖宝帧：`4D 5A 00 00 00 00 00 00 00 0C B0 F4 00 02 00 00 00 07 D0 00 00 00 07 D1`。目标场景和坐标仍只由前置 `0x5828` Jump 承载，自动挖宝帧不绑定 `packageNum`。
- `TreasureMapPresetRunner` 现在按“Jump → 默认 300ms → AutoDig”执行，持续模式仍通过 C6 背包消耗确认和 2500ms 上限窗口判定完成；旧 `0x783A` Use 编码和准备接口保留给兼容调用方，但助手运行路径不再发送 Use。
- `TreasurePacketRuntime` 的准备包、发送合同和预检已支持 AutoDig；运行日志步骤改为 `auto_dig`，发送统计同时保留旧 `use` 步骤兼容。授权边界、当前 Socket 解析和持久化日志保持不变。
- 新增 AutoDig 编码/运行器回归断言；WPELibrary Debug/Release 构建、Release 主解决方案构建（0 错误，保留 4 条既有 EasyHook `MSB3178` 警告）、TreasureC6StreamRegression 可执行回归、TreasurePacketEncoderRegression、TreasurePacketTemplatePatcherRegression、AssistantManagementRegression 均通过。尚未执行真实自动挖宝或其他游戏封包发送，尚未生成 ClickOnce 热更新。

## 2026-08-21 藏宝图原生自动挖宝包桌面热更新

- 已先完成可回滚备份：`releases/backup-before-hotupdate-20260821-231805/` 保存了热更前的固定入口、`2026.8.21.22` 完整发布目录和桌面快捷方式；历史发布目录未删除。
- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.21.23/`；发布目录 200 个文件，固定入口 `releases/小黑封包助手.application` 已指向 `2026.8.21.23`，必需 EXE、WPELibrary、EasyHook、SQLite 和视觉 worker sidecar 均存在。
- 桌面 `小黑封包助手.lnk` 已切换到固定入口和 `.23` 图标，更新前的快捷方式另备份为 `releases/shortcut-backups/小黑封包助手_20260821_231829.lnk`，更新后预览状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、Windows PowerShell 5.1 下的 Release `UiDesignRegression.ps1`、签名模式 `ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过；发布构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。当前未检测到 `小黑封包助手` 进程，未执行真实注入、抓包或自动挖宝发包。
- 请关闭可能已打开的旧窗口后，双击桌面 `小黑封包助手.lnk` 加载 `.23`；ClickOnce 热更不会强制结束正在运行的旧实例。

## 2026-08-21 藏宝图桌面版本回退

- 根据现场日志确认 `.23` 的原生 AutoDig 包虽已成功分派，但沿用 2500ms 消费确认与 Jump+AutoDig 补偿会过早重复跳转；应用户要求，桌面固定入口已回退到上一个签名版本 `2026.8.21.22`，恢复 Jump→Use 运行路径。
- 回退前已把 `.23` 固定入口和桌面快捷方式备份到 `releases/backup-before-rollback-20260821-232741/`；`.23` 发布目录、源码修改、历史版本、数据库和用户配置均未删除或覆盖。
- 固定入口 `releases/小黑封包助手.application` 已恢复为 `.22` 签名清单，应用清单依赖摘要、必需 EXE/DLL 和签名标记校验通过；桌面快捷方式仍指向固定入口，图标恢复为 `releases/2026.8.21.22/小黑封包助手.exe,0`。
- 当前未检测到 `小黑封包助手` 进程；本次是已有发布产物的入口回退，没有重新构建，也未执行真实注入、抓包或封包发送。`git diff --check` 通过。

## 2026-08-21 宝图旧逻辑高版本回退发布

- 为避免 ClickOnce 直接从已运行的 `.23` 降到 `.22`，已将原签名 `.22` 发布产物重新封装为更高版本 `releases/2026.8.21.24/`；200 个文件中除 ClickOnce 清单版本、依赖摘要和签名外，其余文件与 `.22` 完全一致，EXE/WPELibrary 二进制版本仍为 `2026.8.21.22`，运行逻辑恢复 Jump→Use。
- 切换前备份了当前固定入口和快捷方式到 `releases/backup-before-high-version-20260821-233940/`；`.22`、`.23` 发布目录、源码、数据库和用户配置均保留。
- 固定入口已更新为签名的 `.24` 清单，桌面快捷方式已切换到固定入口和 `.24` 图标，脚本备份为 `releases/shortcut-backups/小黑封包助手_20260821_233953.lnk`，更新后状态为 `UpToDate`。
- `mage.exe` 三份清单签名校验、应用清单文件/摘要校验、`ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、签名模式 `ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。本次没有重新编译源码，未执行真实注入、抓包或封包发送；当前未检测到 `小黑封包助手` 进程。

## 2026-08-22 宝图版本显示修复热更新

- 定位到 `.24` 界面仍显示 `.22` 的原因：`.24` 为保留旧 Jump→Use 逻辑而复用 `.22` 二进制的高版本 ClickOnce 包，固定入口版本虽为 `.24`，但标题读取的是 `WPELibrary` 程序集内部版本。
- 已基于 `.24` 原包制作签名本地 ClickOnce `releases/2026.8.21.25/`；200 个文件中只对 `小黑封包助手.exe`、`WPELibrary.dll` 原地更新程序集/文件版本及 EXE 对 `WPELibrary` 的引用，未重写方法体，运行路径仍为 Jump→Use。现在 EXE、WPELibrary、入口清单和桌面图标均为 `.25`。
- 切换前已备份固定入口和桌面快捷方式到 `releases/backup-before-version-display-fix-20260821-235944/`；快捷方式脚本另备份为 `releases/shortcut-backups/小黑封包助手_20260822_000021.lnk`。`.22`、`.23`、`.24` 和此前备份均保留。
- 应用清单、部署清单和固定入口均由 `mage.exe` 验签通过；依赖文件大小/SHA-256 摘要、`ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、签名模式 `ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。EXE `PEVerify` 通过；WPELibrary 仍保留原 `.24` 相同的 49 条既有验证诊断，未因版本字段修复增加。
- 本次未启动游戏、未注入、未抓包、未发送真实 Jump/Use 封包；更新时未检测到桌面助手进程。需关闭已打开的旧窗口后，再双击桌面 `小黑封包助手.lnk` 才会加载 `.25` 并显示新版本。

## 2026-08-22 ClickOnce 映射载荷校验修复热更新

- 现场启动 `.25` 时出现“应用程序验证未成功”。根因是清单启用 `mapFileExtensions=true` 后，`小黑封包助手.exe.deploy` 和 `WPELibrary.dll.deploy` 仍保留 `.22` 旧载荷，而 `.25` 应用清单声明的是新版本摘要，导致 ClickOnce 哈希校验失败；该问题属于发布封装错误，不是用户操作问题。
- 已保留 `.25` 失败包，并基于它生成签名本地 ClickOnce `releases/2026.8.21.26/`；EXE/DLL 及其对应 `.deploy` 文件均同步为 `.26`，发布目录 200 个文件，所有 `.deploy` 与载荷 SHA-256 一致。Jump→Use 运行逻辑未改变。
- 切换前已备份固定入口和桌面快捷方式到 `releases/backup-before-deploy-fix-20260822-000938/`；快捷方式更新脚本另备份为 `releases/shortcut-backups/小黑封包助手_20260822_000952.lnk`。`.22`～`.25`、历史发布目录和其他备份均保留。
- 应用清单、部署清单和固定入口均由 `mage.exe` 验签通过；`ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、签名模式 `ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。EXE `PEVerify` 通过；WPELibrary 保留原有 49 条验证诊断，未因本次修复增加。程序集反射版本、EXE/DLL 文件版本和桌面入口均为 `.26`。
- 本次未启动游戏、未注入、未抓包、未发送真实 Jump/Use 封包；切换时未检测到桌面助手进程。请关闭旧窗口后双击桌面 `小黑封包助手.lnk`，当前应加载 `.26`；若仍弹校验错误，请点“详细信息”把错误内容发来。

## 2026-08-22 藏宝图稳定性与日志降噪优化桌面热更新

- 生产桌面路径保持已验证的 `Jump → Use`，未重新启用原生 AutoDig。消费确认仍为 2500ms；首次确认超时只重发一次 Use，后续超时才进入冷却并恢复完整 Jump→Use 补偿，避免延迟确认时重复跳转。
- 重复 `snapshot_empty` 识别日志改为同一状态最多每 5 秒写一条心跳；结构化 JSONL schema 升为 2，新增每次动作的 `attemptId`，发送/超时记录保留准备阶段的槽位、场景和坐标，即使实时快照已经前进也不会丢失定位信息。
- WPELibrary Debug/Release 构建、藏宝图流/编码/模板回归、助手管理、持久化安全、路由、WSA、Review、Release UI 和签名 ClickOnce 回归均通过；`git diff --check` 通过。`AtomicSaveRuntimeRegression.ps1` 仍受既有缺失 fixture `WinsockPacketEditor/work/AtomicSaveFixDebug/Be.Windows.Forms.HexBox.dll` 影响，未将该无关问题混入本次修复。
- 已先备份当前 `.26` 固定入口、发布目录和快捷方式到 `releases/backup-before-optimization-hotupdate-20260822-003715-current26/`，随后生成签名 ClickOnce `releases/2026.8.22.0/`。新包 100 个文件（含 `.deploy` 对）全部存在，所有 `.deploy` 与载荷 SHA-256 一致，应用清单、部署清单和固定入口均由 `mage.exe` 验签通过；EXE 与 WPELibrary 文件版本均为 `2026.8.22.0`。
- 桌面快捷方式已备份到 `releases/shortcut-backups/小黑封包助手_20260822_003920.lnk` 并切换到固定入口及 `.0` 图标。热更期间未结束桌面进程、未启动游戏、未注入、未抓包或发送真实游戏封包。

## 2026-08-22 藏宝图半成品收口（源码完成，未热更）

- 修复证据观察代码的断裂作用域、重复构造器和工程漏编译项；`TreasureEvidenceDecoder` 按实际 MZ 帧的 2 字节大端协议位解析 EnterMap/GoToPos/PlayerJumpToPos 与 RespPotholing，`TreasureC6StreamObservation` 以线程安全 FIFO 提供到达/消费观察。
- 指令编解码新增 v3 的 `control`/`evidence` 字段；默认仍为 Shadow，不改变现有生产 Jump→Use 节奏，Enforced 模式在缺少匹配到达证据时 fail-closed。运行器增加进程内 Exclusive 控制租约，非法持久化指令不再静默降级，控制冲突会同步到桌面/移动状态。
- 新增回归覆盖证据解码、Enforced 超时不发 AutoDig、Exclusive 并发冲突和租约释放；WPELibrary Debug 构建、完整 Debug 解决方案构建、TreasureC6StreamRegression 可执行回归、TreasureC6StreamRegression PowerShell、Persistence/Safety、Review、路由和 WSA 回归均通过。完整方案仍保留 4 条既有 EasyHook `MSB3178` 警告；`AtomicSaveRuntimeRegression.ps1` 继续受既有缺失 fixture 影响。
- 本轮只修改源码和回归测试，未执行 ClickOnce 热更新、未启动游戏、未注入、未抓包或发送真实游戏封包。

## 2026-08-22 藏宝图半成品收口桌面热更新

- 已先保留历史发布目录和源码工作区，使用项目现有 `tools/Publish-ClickOnceStable.ps1 -Unsigned` 生成未签名本地 ClickOnce `releases/2026.8.22.1/`；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- 新包包含 200 个文件、86 个 `.deploy` 映射文件；应用清单包含 57 个文件节点，EXE 清单、应用清单、部署入口版本均为 `2026.8.22.1`，必需 EXE/DLL、ONNX sidecar 和 Python Worker 文件齐全，固定入口为 `releases/小黑封包助手.application`。
- 桌面 `小黑封包助手.lnk` 已备份为 `releases/shortcut-backups/小黑封包助手_20260822_103952.lnk` 并更新到固定入口及 `.1` 图标，更新后预览状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1` 和未签名包的清单/依赖/文件 SHA-256 校验均通过；签名专用 `ClickOnceUpdateRegression.ps1` 按预期拒绝未签名包，未将签名校验误报为通过。未启动游戏、未注入、未抓包或发送真实游戏封包；请关闭旧窗口后双击桌面快捷方式加载 `.1`。

## 2026-08-22 藏宝图节拍与途中补跳优化桌面热更新

- 生产桌面路径仍为 `Jump → Use`，保留游戏原生自动挖宝作为 fallback；新增目标稳定窗口 300ms、完成一张后下一张间隔 600ms、相邻 Jump 最小间隔 1200ms，以及首次 Jump/Use 后 1200ms 的单次途中补 Jump。每张图最多补一次，不改变现有封包格式或 C6 读取器。
- 新增 `target_stable`、`jump_throttle_wait` 和 `mid_route_retry:*` 运行日志；回归覆盖稳定窗口、最小 Jump 间隔和单次途中补跳。WPELibrary Debug 构建、TreasureC6StreamRegression 可执行回归、Release ClickOnce 构建、Review/UI 回归和 `git diff --check` 均通过。
- 已使用现有发布脚本生成未签名本地 ClickOnce `releases/2026.8.22.2/`；发布目录 200 个文件、86 个 `.deploy` 映射，应用清单 57 个文件节点，必需 EXE/DLL、ONNX sidecar、Python Worker 文件、映射载荷和 SHA-256/大小校验均通过，EXE/部署清单/固定入口版本一致。Release 构建保留 4 条既有 EasyHook `MSB3178` 警告；签名专用 `ClickOnceUpdateRegression.ps1` 按预期因 unsigned 包拒绝签名断言。
- 桌面 `小黑封包助手.lnk` 已备份为 `releases/shortcut-backups/小黑封包助手_20260822_112242.lnk`，更新后指向固定入口，图标为 `.2`，预览状态 `UpToDate`。热更未结束桌面进程，未启动游戏、未注入、未抓包或发送真实游戏封包；需关闭旧窗口后双击桌面快捷方式加载 `.2`。

## 2026-08-22 藏宝图竞速窗口与原生先消耗诊断热更新

- 生产桌面路径仍为 `Jump → Use`，消费确认上限仍为 2500ms；将目标稳定窗口从 300ms 缩短为 20ms，完成一张后的固定等待从 600ms 改为 0ms，相邻 Jump 最小间隔从 1200ms 调整为 800ms，途中单次补 Jump 从 1200ms 调整为 700ms。游戏原生自动挖宝仍作为 fallback，不重新启用 AutoDig 发送路径。
- 运行器新增 `native_auto_use` 诊断步骤：当 C6 在助手发送 Jump 前已消耗目标时记录 `native_consumed_before_jump`，与 `jump`/`jump_retry` 主动发送记录区分，用于下一轮现场日志直接统计两条路径的占比。
- WPELibrary Debug 构建、`TreasureC6StreamRegression.ps1`、`ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1` 和 `git diff --check` 均通过；ClickOnce Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。未签名专用 `ClickOnceUpdateRegression.ps1` 按预期因缺少 ClickOnce 签名拒绝，未误报为通过。
- 已生成未签名本地 ClickOnce `releases/2026.8.22.3/`：200 个文件、86 个 `.deploy` 映射、应用清单 57 个文件节点；应用/部署/固定入口版本均为 `2026.8.22.3`，逐项文件大小与 SHA-256 映射、部署清单对应用清单摘要校验通过。无签名节点，不能作为公网分发包。
- 桌面快捷方式更新前备份为 `releases/shortcut-backups/小黑封包助手_20260822_114223.lnk`，更新后预览状态为 `UpToDate`，图标指向 `.3`。热更未强制结束已运行的桌面助手进程，旧发布目录、数据库和用户配置均保留；本次未启动游戏、未注入、未抓包或发送真实游戏封包。

## 2026-08-22 召唤兽技能助手预设模板（源码完成）

- 在现有助手列表中加入默认禁用的 `召唤兽技能` 内置预设；首次从数据库加载时自动创建，已有同名且非空的用户预设不覆盖，空同名预设才补齐模板。
- 预设固定保存 16 行 `SummonedPetSkillBook` 指令，内容使用版本化 `SummonedPetSkillBook|1|序号|状态` 编码；现有助手网格会显示每一步的序号、状态和中文说明。指令校验拒绝缺步、乱序、坏内容及与普通指令混排。
- 当前运行入口只做模板解析后 fail-closed，提示只读召唤兽数据适配器和正常 UI/业务事件适配器尚未接入，未执行任何游戏操作；尚未接入真实宠物偏移、技能目录、背包读取或开格/打书/锁格业务动作。
- `WPELibrary` Debug 隔离构建、`SummonedPetSkillBookPresetRegression.ps1`、`ReadOnlyProcessMemoryReaderRegression.ps1`、`AssistantManagementRegression.ps1` 和 `git diff --check` 均通过；未启动游戏、未注入、未使用 OCR/截图、未发送真实游戏封包，也未执行 ClickOnce 热更新。

## 2026-08-22 召唤兽技能助手阶段 0-2 完成

### 阶段 0 完成：阅读文档和源码
- 已读 AGENTS.md、README.md、ARCHITECTURE.md、DEVELOPMENT_RULES.md、PROJECT_STATUS.md 等核心文档
- 已读 WPELibrary 项目结构和现有助手代码
- 已检查 git 工作区：当前在 master 分支，ahead 13 commits，有多个文件待提交
- **结论**：未发现经过确认的真实读取接口（游戏内存偏移、实时数据来源）

### 阶段 1 完成：实现强类型数据契约和错误码
- 创建 `PetSkillBookModels.cs`：定义 `PetSkillSnapshot`、`PetStateSnapshot`、`InventorySnapshot`、`SkillBookCatalogEntry`、`PetMode`、`MissingMaterialPolicy`、`OperationResult`、`SkillBookEntry` 等数据契约
- 创建 `SummonedPetSkillBookPreset.cs`：定义预设配置类 `SummonedPetSkillBookPreset`，包含验证方法 `IsValid()`
- 创建 `SummonedPetSkillBookContext.cs`：定义运行上下文类

### 阶段 2 完成：实现离线测试适配器
- 创建 `IPetSkillBookAdapter.cs`：定义只读接口 `IPetSkillBookReadOnlyAdapter` 和操作接口 `IPetSkillBookOperationAdapter`
- 创建 `TestAdapters.cs`：实现 `TestReadOnlyAdapter`（提供离线模拟数据）和 `TestOperationAdapter`（捕获操作日志）
- 创建 `SummonedPetSkillBookRunner.cs`：实现状态机 runner，包含 17 个状态（IDLE → VERIFY_CURRENT_PET → ... → COMPLETE）

### 当前状态
- **停在测试适配器阶段**：未找到经过确认的真实读取接口（游戏进程内的召唤兽/背包/技能格/技能书内存地址）
- **下一步阻断**：必须收集目标游戏的版本化内存布局证据，才能进入阶段 3（真实适配器）
- **不做**：猜测内存偏移、伪造接口、发送封包

## 2026-08-22 藏宝图到达后 Use 参数与时序修复热更新

- 现场结构化日志 runId `96ea9ae8-46db-4df9-af5f-6fb7958e4bc8` 显示目标 `slot=8, scene=1018, x=52, y=87` 已记录 Jump sent，约 305ms 后记录 Use sent；随后仍出现 `arrival_evidence_not_observed_shadow`、约 700ms 后的 `jump_retry`，最终连续 `consume_timeout`。`sent` 只证明传输写入已派发，不等于游戏已消耗藏宝图。
- 最新 run `c3ab07ae-80dc-4537-973f-a79f05156768` 证明强制等待到达证据会造成 `jump sent → arrival_evidence_timeout → target_failed_skipped`，没有任何 Use；因此生产 V1/V2 恢复为 Shadow 观察模式，收到证据就记录、缺失也继续发送 Use。途中仍不发送补偿 Jump，V3 保留其显式证据策略，连续模式仍等待 C6 消耗确认。
- Use 编码优先复用当前会话捕获的 `0x783A` 模板字段，只重绑定 C6 背包位置；没有模板时回退到校验过的高级藏宝图字段 `type=13,num=1,param=2`。新增 `use_prepare` 日志记录实际来源和字段，便于下一轮现场确认。
- 已通过 WPELibrary Debug/Release 构建、藏宝图 C6 流回归、封包编码/模板回归、启动回归、助手管理回归、Review 回归、Release UI 回归和 ClickOnce 更新配置回归。
- 已生成签名本地 ClickOnce `releases/2026.8.22.6/`，包含 200 个文件；Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告。桌面快捷方式已备份并更新为 `.6`，关闭旧窗口后双击 `小黑封包助手.lnk` 才会加载新版本。
- 本次未启动游戏、未注入、未抓包、未发送真实游戏封包，也未实际使用藏宝图。全量 `git diff --check` 仍只被既有的 `WPELibrary/Socket_RobotForm.Designer.cs:792` 尾随空格阻断；本次修改文件的定向检查通过。

## 2026-08-22 藏宝图最新日志复核与生产模式回退热更新

- 最新日志 `c3ab07ae-80dc-4537-973f-a79f05156768` 的每个目标均为 `jump sent → arrival_evidence_timeout → target_failed_skipped`，没有 `use_prepare`/`use sent`；根因确认是生产入口强制等待未稳定出现的到达帧，导致 Use 分支被提前返回。
- 已将生产 V1/V2 恢复为 `TreasureEvidenceMode.Shadow`：到达帧只作诊断，缺失时继续按原有 Jump→Use 节奏；保留当前会话 `0x783A` 模板参数、Use 后消费确认和禁止途中补 Jump。V3/离线 Enforced 回归路径不变。
- 已重新生成签名本地 ClickOnce `releases/2026.8.22.7/`，200 个文件，Release 构建 0 错误、保留 4 条既有 EasyHook `MSB3178` 警告；桌面快捷方式已备份并更新，最终预览为 `UpToDate`。旧 `.6`、数据库和用户配置保留。
- 本轮实际通过 Assistant 管理回归、WPELibrary Debug 构建、TreasureC6StreamRegression Debug/Release、Review 回归、Release UI 回归和 ClickOnce 更新回归。仍未启动游戏、未注入、未抓包或发送真实封包；下一轮日志应重点出现 `use_prepare`、`use sent`，成功时出现 `consume_confirmed`。

## 2026-08-22 藏宝图继续查日志与普通发送预设复用修复

- `.7` 最新运行 `95f028e0-575b-4a04-b702-431a2b465d61` 的尾部持续为 `Jump sent → arrival_evidence_not_observed_shadow → configured_fallback:use_template_not_found;type=13;num=1;param=2 → Use sent → consume_timeout`，重复到至少 retry 109，没有 `consume_confirmed`。因此问题不是 Jump 没发出，而是助手没有拿到当前游戏可用的挖宝动作参数。
- 只读检查当前发送预设数据库发现已有普通预设“挖宝图”，保存的是 22 字节 `0xB0F4` 原生挖宝帧；普通发送路径会直接使用保存帧并按当前连接解析 Socket，藏宝图助手此前没有读取该预设。
- 运行时现在先复用当前会话/当前抓包列表/已保存发送预设中的 `0x783A` Use；没有 Use 时自动复用当前路由匹配的已保存 `0xB0F4` AutoDig 帧，并保留 22 字节旧帧与 24 字节闭合帧校验。发送前只重绑定当前路由，不保留旧 Socket；没有任何保存帧时才保留固定兼容回退。
- 已通过 WPELibrary Debug 重建、`TreasurePacketEncoderRegression.ps1`、`TreasureC6StreamRegression.ps1` 和 `AssistantManagementRegression.ps1`。新增回归覆盖已保存 AutoDig 帧的字节保持、当前路由重绑定及旧 Socket 清零；尚未启动新版桌面、未进行真实游戏发包。

## 2026-08-22 藏宝图普通预设复用修复桌面热更新

- 已按本地 ClickOnce 流程生成签名版本 `releases/2026.8.22.8/`；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。发布目录 200 个文件、86 个 `.deploy` 映射，`WPELibrary.dll` 与 `WPELibrary.dll.deploy` SHA-256 一致。
- `ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、Release `TreasureC6StreamRegression.ps1`、Release `TreasurePacketEncoderRegression.ps1`、`AssistantManagementRegression.ps1` 和 `ClickOnceUpdateRegression.ps1` 均通过；桌面快捷方式更新后状态为 `UpToDate`，图标指向 `.8`。旧发布目录、数据库和快捷方式备份均保留。
- 热更未结束桌面进程，未启动游戏、未注入、未抓包或发送真实游戏封包；请关闭可能仍打开的旧窗口后双击桌面 `小黑封包助手.lnk`，再测试日志中的 `auto_dig_prepare|current_send_preset`、`auto_dig|sent` 和后续 `consume_confirmed`。
- 全量 `git diff --check` 仍只报告既有 `WPELibrary/Socket_RobotForm.Designer.cs:792` 尾随空格，本轮未修改该无关问题。

## 2026-08-22 藏宝图超时重复飞行修复热更新

- 根据现场 run `bb1cb47c-56c6-401b-8719-518b053a749d` 确认 AutoDig 消费超时后，旧逻辑每轮重新发送同一坐标 Jump，形成约 3 秒的重复飞行循环。
- `TreasureMapPresetRunner` 现改为 AutoDig 首次尝试发送一次 Jump，后续消费超时只重发当前 AutoDig 包；Jump→Use 路径仍保留首次 Use-only 重试和后续完整补偿语义。`TreasureC6StreamRegression` 新增 1 次 Jump、3 次 AutoDig 的回归断言。
- 已生成签名本地 ClickOnce `releases/2026.8.22.9/`；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。发布目录 200 个文件、86 个 `.deploy` 映射，固定入口和桌面快捷方式均已切换到 `.9`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260822_235857.lnk`，最终状态为 `UpToDate`。
- Debug/Release 与发布包的 `TreasureC6StreamRegression`、`TreasurePacketEncoderRegression`、`ReviewRegressionChecks`、Windows PowerShell 下的 Release `UiDesignRegression`、`AssistantManagementRegression` 和签名 `ClickOnceUpdateRegression` 均通过。未启动游戏、未注入、未抓包或发送真实封包；全量 `git diff --check` 仍只命中既有 `Socket_RobotForm.Designer.cs:792` 尾随空格。
