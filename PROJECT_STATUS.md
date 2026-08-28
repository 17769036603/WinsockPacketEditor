# 项目状态

更新时间：2026-08-28

## 2026-08-28 坐骑炼化 sequence 未推进过早停止修复（源码已验证，未热更新）

- 新版本实机日志显示，发送后读取仍为完整 21 张卡且进程、会话和坐骑上下文未变，但 sequence 暂未推进；旧状态机把该合法等待态包装为 `mount_refine_snapshot_sequence_not_advanced` 读取失败，导致在第 9、19、2 次发送后过早停止。
- `MountRefineStateMachine` 现在固定发送前快照为本轮基线。候选必须同时满足同 PID/startTicks/session/mount/activeRide、快照有效且完整 21 张卡、sequence 严格高于发送前基线，并且卡片内容相对发送前基线确实变化，才允许进入 verify、目标判断和下一轮。sequence 未推进，或 sequence 已推进但卡片内容仍未变化，分别写入有界 `snapshot_sequence_wait` / `snapshot_content_wait` 日志并继续可取消等待，不做目标匹配、不重发；同一推进序号下稍后出现真实卡片变化仍可恢复。真实读取失败、上下文变化、不完整快照和取消仍分别保持 fail-closed 或 `UserStopped`。
- `MountSpeedRunnerHarness` 新增 point、fast、fallback 三路径的连续旧序号等待、sequence 已推进但卡片未变、随后同序号卡片变化恢复、等待中间态保持单次发送、等待取消、真实读取失败、换坐骑和不完整卡片回归；Python 探针路径回归、`MountRefineCompleteFixRegression`、`ReviewRegressionChecks`、隔离 WPELibrary Release、harness Release 和整套解决方案 Release 构建均通过。`git diff --check` 仍只报告本轮之前 `WPELibrary/Lib/Socket_Cache.cs:11206` 的尾随空格。
- 本轮未发布 ClickOnce，未启动或停止助手，未操作游戏，未注入、未抓包、未发送真实封包；需关闭旧助手并用源码对应版本进行实机复测，确认真实 sequence 延迟后的恢复行为。

## 2026-08-28 坐骑炼化提速及既有工作区改动本地 ClickOnce 热更新（2026.8.28.2，重新执行）

- 已重新使用项目现有发布脚本生成签名本地 ClickOnce `releases\2026.8.28.2`，固定入口 `releases\小黑封包助手.application` 已更新；发布包包含当前工作区既有改动、坐骑 point/warm/cold 提速、21 张卡严格校验和性能日志字段。
- Release 构建 0 个错误，保留既有 4 条 EasyHook `MSB3178` 警告；`ReviewRegressionChecks`、Release `UiDesignRegression`、签名 `ClickOnceUpdateRegression` 和清单依赖摘要校验通过。
- 桌面快捷方式预览为不存在，因此未创建、未更新、未备份快捷方式；本轮未启动助手、未结束进程、未选择进程、未注入、未抓包、未发送真实封包。旧运行实例需关闭后再从固定入口启动，更新才会生效。

## 2026-08-28 停止抓包后普通/受保护预设继续运行修复（源码已验证，待热更新）

- 现场日志确认普通发送路由曾依赖可见抓包列表，受保护预设在高速模式下又可能未建立当前会话序号；停止抓包还会清空 `TreasurePacketRuntime`，因此会出现 `runtime_not_connected` 或 `session_sequence_unavailable`。
- 停止抓包现在只停止采集，保留当前注入会话的实时 Socket 路由、受保护预设协议序号和坐骑 A050 证据；下一次开始抓包或程序退出时统一重建/清理，状态不会跨注入会话复用。
- 高速模式在绕过界面队列前观察所有出站路由和协议帧，并用最多 256 条、仅含 Socket/方向/地址/时间的有界路由证据支持停止抓包后的普通预设；入站包仍在高速路径提前返回，避免额外端点查询。
- Debug/Release 停止抓包本地回环回归、实时路由、受保护预设、盘古协议/发送、藏宝图编码/模板/C6、坐骑完整修复和项目总回归均通过；主解决方案 Release 重建 0 个错误，保留既有 4 条 EasyHook `MSB3178` 警告。本轮未注入、未抓包、未发送真实游戏封包，源码尚未发布到桌面 ClickOnce 版本。

## 2026-08-28 坐骑炼化 point/warm/cold 提速源码实现（未热更新）

- 同一长驻 LuaJIT 探针在 PID、startTicks、可执行文件、当前映射、会话和坐骑实例仍一致且上一轮已验证完整 21 张卡时，进程内临时复用 `m_RideIns`、`m_ResetData` 外层索引和卡片容器位置做定点只读；地址、映射、绑定、21 张卡或动态字段任一不满足就丢弃定点计划并回退安全路径，绝不复用旧卡发包。
- 新进程不跨进程复用绝对地址：现有相对 warm hint 先在当前映射中重新物化并完整校验；没有 warm hint 时增加有界小映射 cold 优先阶段（最多 16 个、最多占当前映射 25%），未命中继续完整发现。Root broker 长连接和批量读取保持不变。
- 探针快照新增 `reader.readPath`（`point/fast/warm/cold/discover/fallback`）及 Root/ADB、探针、丰富字段/卡片解析耗时；桌面结构化日志新增读取总耗时和 `send_result` → `verify_new_snapshot` 分段耗时。成功刷新后的默认间隔由 500ms 调整为 250ms；3 秒确认窗口、2.8 秒读取上限、超时继续等待和 fail-closed 语义未改。
- 已通过 Python 探针回归、坐骑完整修复静态回归、Android Root 会话静态回归、Review 回归、MountSpeedRunnerHarness（全部测试）和隔离 Release 重建。未发布 ClickOnce，未启动助手、未操作游戏、未注入、未抓包、未发送真实封包；point/cold 的真实首轮收益和服务端业务接受仍需后续实机验证。

## 2026-08-27 坐骑炼化路由保留与刷新持续等待修复桌面热更新（2026.8.27.17）

- 已将“停止抓包后继续使用当前注入会话的坐骑炼化路由”和“刷新确认超时继续等待、不自动重发或停止”修复发布到签名本地 ClickOnce `releases\2026.8.27.17`；旧发布、SQLite 数据库和用户配置保留。
- Release 发布构建 0 个错误，保留既有 4 条 EasyHook `MSB3178` 警告；`ReviewRegressionChecks`、Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression` 均通过，发布包必需文件和清单依赖摘要核对通过。
- 固定入口 `releases\小黑封包助手.application` 已更新并签名。检查发现 `C:\Users\Administrator\Desktop\小黑封包助手.lnk` 当前不存在，因此未擅自创建或覆盖快捷方式；本轮未启动助手、未注入、未抓包、未发送真实封包。

## 2026-08-27 坐骑首次扫描根因修复与桌面热更新（2026.8.27.16）

- 现场日志确认 `.15` 在 Android PID 从 `1860` 换到 `1897` 后仍会卡满两轮各 5 分钟：旧跨进程提示把 Lua 临时访问页和表节点相对偏移当作可迁移布局，实际进程重启后分配段拆分、大小及 Lua 表哈希顺序都会变化；提示失效后，Root broker 又把大段 Base64 文本通过无缓冲命名管道逐字节读取，最终超时并重复一次同类扫描。
- 跨进程提示现在只用于生成有界的扫描优先级：按旧基础字段所在映射序号附近范围与不超过 4 MiB 的小分配段先扫描，仍重新发现字符串键、唯一当前角色、坐骑绑定和完整 21 张卡；任一条件不满足自动回退全量发现。跨进程不再把旧临时页作为必需条件，也不把旧相对地址当作有效快照。
- Root broker 改为持久命名管道连接和 1 MiB 缓冲读取，连接建立阶段容忍服务端重建管道的短暂空窗；内存块按最多 32 MiB 原始数据分批，避免原 64 MiB 单批膨胀，同时显著减少往返。长驻探针若已经耗尽响应超时，不再启动一次性探针重复同一轮扫描。
- 真实只读验收使用旧 PID `1860` 缓存和当前 PID `1897`：普通独立 ADB 路径 `36.88s`，桌面实际 C# 共享 Root-broker 路径 `53.61s`，均返回 `mountId=9111`、`rideBindingStatus=bound`、当前实例 `2092100442933833732` 和完整 21 张炼化卡；同进程后续共享读取 `0.67s`。同一 32 MiB broker 响应从无缓冲的 `31.83s` 降至 `1.88s`。全部验证均为只读，`readOnly=true`、`actionAuthorized=false`，未注入、未抓包、未发送封包。
- `MountStatusLuaJitProbeRegression`、`MountRefineCompleteFixRegression`、`AndroidRootShellSessionRegression`、`ReviewRegressionChecks`、Windows PowerShell 5.1 Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression` 均通过。已生成签名本地 ClickOnce `releases\2026.8.27.16`，主解决方案 Release 构建 0 错误并保留既有 4 条 EasyHook `MSB3178` 警告；固定入口和桌面快捷方式已更新，旧快捷方式备份为 `releases\shortcut-backups\小黑封包助手_20260827_210242.lnk`。

## 2026-08-27 Root-broker 批量读取修复与桌面热更新（2026.8.27.15，后续确认未彻底解决）

- `.15` 把 Root-broker 请求改为每批最多 32 个命令，解决了“所有块一次提交”的一个问题；离线回归覆盖当时的 `32/32/1` 命令分批行为，但没有限制单批总字节量，也没有解决命名管道无缓冲大响应。
- 当时直接调用 `.15` 发布包读取 PID `1860`，返回 `mountId=9111`、`rideBindingStatus=bound` 和完整 21 张炼化卡；后续复核确认该次为同 PID 缓存命中，不能证明换进程后的首次扫描。PID 切换到 `1897` 后日志记录约 10 分钟才以命令超时失败，因此 `.15` 的首次扫描结论由 `.16` 更正。
- 已生成签名本地 ClickOnce `releases\2026.8.27.15`，固定入口和桌面快捷方式均已更新；旧快捷方式备份为 `releases\shortcut-backups\小黑封包助手_20260827_195208.lnk`，最终入口检查为 `UpToDate`。Release 构建 0 错误，保留既有 4 条 EasyHook `MSB3178` 警告。

## 2026-08-27 坐骑探针缓存提速与批量读取修复、桌面热更新（2026.8.27.14）

- 同进程缓存读取继续保留 2 次、每次 250ms 的短重试；Android 进程更换时只使用相对偏移候选并重新校验，不跨进程复用旧绝对地址。共享 Root 通道的 32 块批量读取超时已提高到至少 30 秒，并在探针诊断中记录具体缺失字段，避免把批量通道超时误报为 `field_string_missing`。
- `MountStatusLuaJitProbeRegression`、`MountRefineCompleteFixRegression`、`ReviewRegressionChecks`、Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression` 均通过；主解决方案 Release 发布构建 0 错误，保留既有 4 条 EasyHook `MSB3178` 警告。
- 已生成签名本地 ClickOnce `releases\2026.8.27.14`，固定入口 `releases\小黑封包助手.application` 和桌面快捷方式均已更新；旧快捷方式备份为 `releases\shortcut-backups\小黑封包助手_20260827_193531.lnk`。最终入口检查为 `UpToDate`，本轮未启动助手、未重启模拟器、未注入、未抓包、未发送真实封包。

## 2026-08-27 全局 Android Root 会话统一与桌面热更新（2026.8.27.13）

- 新增 `WPELibrary.Lib.Android.AndroidRootShellSessionManager`：按 ADB 路径和设备序列号，在当前桌面进程内缓存一个持久 `adb shell -t su -c sh`，C# 调用通过串行命令闸门复用；lease 释放不关闭全局会话，进程退出或会话失效时统一清理/重建。
- 坐骑 LuaJIT 探针、装备属性 reader、宠物属性权限回退和藏宝图 C6 已统一接入该会话；Python reader 使用本地 `--root-broker-pipe`，桌面生产路径不再为每次读取重新启动 `adb shell su`。独立 CLI 仍保留无 broker 的兼容分支，但新增 Android 预设必须使用共享会话入口。
- 新增 `tests\AndroidRootShellSessionRegression.ps1`，并通过该回归、`MountStatusLuaJitProbeRegression`、`EquipmentRefineRuntimeWiringRegression`、`MountRefineCompleteFixRegression`、`TreasureC6StreamRegression`、`ReviewRegressionChecks`、Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression`。WPELibrary 隔离 Release 重建 0 错误；主解决方案发布构建保留既有 4 条 EasyHook `MSB3178` 警告、0 错误。
- 已生成签名本地 ClickOnce `releases\2026.8.27.13`，固定入口 `releases\小黑封包助手.application` 已更新；桌面快捷方式已切换到 `.13` 图标，旧快捷方式备份为 `releases\shortcut-backups\小黑封包助手_20260827_191018.lnk`。旧发布、数据库和用户配置未删除或覆盖。
- 发布期间不强制关闭旧助手进程；最终检查未发现仍在运行的该进程。若重新出现旧实例，需先关闭它，再从桌面快捷方式启动 `.13` 才会加载新代码。尚未进行真实 Android Root Toast、模拟器重启、注入、抓包或封包发送验收；程序侧已将重复授权申请收敛为同一桌面进程/设备的一次 Root 会话，首次 Toast 是否显示仍由 Android Root 管理器通知设置决定。另发现 `AssistantManagementRegression` 的既有预设选择断言失败，以及 `PetSkillBookReadOnlyStateRegression` 测试脚本历史乱码导致无法解析，均未涉及本次 Root 接线。

## 2026-08-27 坐骑探针缓存提速与批量读取修复（源码变更已随桌面热更新发布）

- 同进程缓存读取遇到暂时不完整的炼化卡片时，先做 2 次、每次 250ms 的短重试；只有确认引用方案失效或重试后仍不完整，才进入原有全量发现，严格 21 张卡片校验不变。
- 新增独立的相对布局暖提示 sidecar：Android 进程更换后只把上次映射的相对偏移作为候选，重新映射并读取六个基础字段、坐骑绑定和完整卡片，验证失败自动回退全量扫描；不跨进程直接复用旧绝对地址。
- 针对 `19:10` 的 `field_string_missing` 日志，确认共享 Root 通道一次批量读取 32 个内存块时沿用了 8 秒的单次读取超时，批量通道超时后被误报为字段缺失；已改为批量读取至少 30 秒，并在探针诊断中记录具体缺失字段。
- `MountStatusLuaJitProbeRegression`、`MountRefineCompleteFixRegression`、`ReviewRegressionChecks` 和隔离 Release 编译已通过；主解决方案 Release 编译 0 错误、保留既有 4 条 EasyHook `MSB3178` 警告。上述源码修复已随签名本地 ClickOnce `2026.8.27.14` 发布；本轮未重启模拟器、未注入、未抓包、未发送真实封包。

## 2026-08-27 坐骑手动刷新复用 Root 会话与桌面热更新（2026.8.27.11）

- 手动“刷新只读快照”现在复用当前窗体的 `MountStatusAndroidSnapshotReader`；窗体关闭时统一释放。结合机器人运行级长驻 JSONL 探针，同一轮连续刷新不会反复启动 `adb shell su -c`，游戏 PID 变化时仍会重建会话。
- 已生成签名本地 ClickOnce `2026.8.27.11`，发布目录为 `releases/2026.8.27.11`，固定入口 `releases/小黑封包助手.application` 已更新；桌面快捷方式仍指向固定入口并切换到 `.11` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260827_182944.lnk`。旧发布、数据库和用户配置未删除或覆盖。
- 主解决方案 Release 发布构建 0 错误、保留既有 4 条 EasyHook `MSB3178` 警告；`MountStatusLuaJitProbeRegression`、`MountRefineCompleteFixRegression`、`ReviewRegressionChecks`、Release `UiDesignRegression`、签名 `ClickOnceUpdateRegression` 和 `MountSpeedRunnerHarness`（10 项）均通过。本轮未启动助手、未注入、未抓包、未发送真实封包；是否完全隐藏首次授权 Toast 仍由 Android Root 管理器设置决定。

## 2026-08-27 坐骑探针复用 Root 会话与授权提示收敛（2026.8.27.10）

- `MountStatusAndroidSnapshotReader` 现在为一次助手运行长驻一个 JSONL Python 探针进程，并由探针复用同一个只读 Root ADB shell；进程身份、映射、完整发现、缓存引用和 21 张炼化卡读取不再为每次刷新重复启动 `adb shell su -c`。游戏进程 PID 变化时会重建会话，不能复用旧进程上下文。
- 保留旧的一次性探针作为启动失败或通道损坏时的兼容回退；超时、取消、缓存失效、卡片不完整和结构不一致仍按原有 fail-closed 语义处理。机器人结束、启动失败、只读早退和桌面手动刷新结束时释放长驻进程，不改权限级别、不写内存、不注入、不发送封包。
- 本地回归与 WPELibrary/`MountSpeedRunnerHarness` Release 构建通过；尚未在真实游戏流程中重复授权或验证 Root 管理器 Toast 设置。程序侧只能把重复授权申请收敛为一次会话，首次授权提示是否可见仍由 Android Root 管理器的 Shell 授权通知开关决定。

## 2026-08-27 一坐骑缓存读取方案本地热更新（2026.8.27.9）

- 已将缓存读取与 2800ms 刷新超时方案生成签名本地 ClickOnce 版本 `2026.8.27.9`；发布目录为 `releases/2026.8.27.9`，固定入口 `releases/小黑封包助手.application` 已更新。
- 桌面快捷方式已继续指向固定入口，并切换到 `.9` 版本图标；旧快捷方式已备份为 `releases/shortcut-backups/小黑封包助手_20260827_175513.lnk`。旧发布、SQLite 数据库和用户配置未删除或覆盖。
- 发布构建 0 错误、保留既有 4 条 EasyHook `MSB3178` 警告；`ReviewRegressionChecks`、Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression` 均通过。本次只完成本地发布与入口更新，未启动旧助手、未注入、未抓包、未发送真实封包。

## 2026-08-27 一坐骑炼化缓存读取与刷新超时收敛

- Android LuaJIT 坐骑只读探针现在会把经过进程身份校验的引用方案和本次实际访问过的内存页缓存到 `%LOCALAPPDATA%\XNAS\WPE\mount-refine\probe-cache`；同一 PID、启动 tick、可执行文件和缓存版本有效时，后续读取走缓存并继续保留当前坐骑绑定与 21 张炼化卡。普通读取遇到缓存失效会回到首次发现路径；发送后严格缓存读取遇到失效、结构变化或卡片不完整时保持 fail-closed。
- `MountStatusAndroidSnapshotReader` 为同一读取器串行化探针调用；`MountRefineStateMachine` 为发送后刷新增加 2800ms 单次读取上限，仍位于 3000ms 结果确认窗口内。探针使用只读 ADB 长连接合并页读取，并保留原有安全回退；没有改变 A050 39 字节模板、Socket 路由或发送闸门。
- 只读实测：首次完整发现约 110–114 秒；完整缓存首次自适应读取约 2.7 秒，随后稳定重复读取约 1.7 秒，均返回 `rideBindingStatus=bound` 与 21 张卡。Python 回归、`MountRefineCompleteFixRegression`、`ReviewRegressionChecks`、`MountSpeedRunnerHarness`（10 项）、WPELibrary Release（0 警告、0 错误）和主解决方案 Release（0 错误、保留既有 4 条 `MSB3178` 警告）通过。本轮未执行真实发包、注入、模拟器重启或桌面热更新；上述读取成功不等同于服务端炼化业务已接受。

## 2026-08-27 一坐骑固定 A050 包与原始 Socket 修复（2026.8.27.8）

- 根据用户确认，一坐骑炼化请求是固定的 39 字节 `4D5A ... A050` 帧；`MountRefineA050PacketTemplate.TryBuild` 现在只复制当前注入捕获的完整样本，不再把只读快照中的坐骑实例 ID 写入包尾，发送副本与手动发送选中封包保持字节一致。
- `MountRefineA050RouteTemplate` 现在保留当前注入捕获的原始 `PacketSocket`、封包类型和方向；`MountRefineA050SocketPacketSender` 沿手动发送路径直接使用该 Socket，不在发送时按目标地址重新切换到另一条连接。Socket 仍不跨注入会话持久化。
- Android 只读桥接的子进程等待改为 100ms 可取消轮询，取消或超时会终止子进程并返回原有取消/失败分类；不写内存、不控制页面、不注入、不抓包、不发送真实封包。
- `MountSpeedRunnerHarness`（9 项）、`MountRefineCaptureEvidenceHarness`、`MountRefineCompleteFixRegression`、`MountStatusLuaJitProbeRegression`、`ReviewRegressionChecks`、Windows PowerShell 5.1 下的 Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression` 均通过。Release ClickOnce 构建 0 个错误，保留既有 4 条 EasyHook `MSB3178` 警告。
- 已生成签名本地 ClickOnce `releases/2026.8.27.8`；固定入口 `releases/小黑封包助手.application` 和桌面快捷方式均已更新，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260827_155222.lnk`，发布包与版本清单核对通过。
- 尚未执行真实游戏注入、抓包或封包发送；需从桌面快捷方式重新启动 `.8`，再实测“一坐骑洗炼”。`git diff --check` 仍只报告 `WPELibrary/Lib/Socket_Cache.cs` 两处本轮之前已有的尾随空格。

## 2026-08-27 洛神赋藏宝图定向修复与桌面热更新（2026.8.27.7）

- 根据运行日志确认 C6 连接和目标识别正常；首个修复绕过了 `0x5828 Jump` 模板缺失，但仍在发送前被 `session_sequence_unavailable` 拦截，导致实际零发送。
- 仅对精确名称“洛神赋”关闭当前 Jump/Use 模板硬门槛，并允许其兼容编码包在当前会话没有序号时继续进入当前 Socket；有序号时仍正常递增绑定。其他藏宝图预设继续使用当前会话模板和序号严格校验，未修改其逻辑或数据库内容。
- WPELibrary Debug/Release 构建、`TreasureC6StreamRegression`、封包编码/模板回归、`ReviewRegressionChecks`、Windows PowerShell 5.1 下的 Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression` 均通过；Release 构建保留既有 4 条 EasyHook `MSB3178` 警告。
- 已生成签名本地 ClickOnce `releases/2026.8.27.7`，固定入口和桌面快捷方式已更新，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260827_154418.lnk`；应用与 WPELibrary 版本均为 `.7`，旧发布、数据库和用户配置保留。
- 本次修复没有启动目标游戏、注入、抓包或发送真实游戏封包；需关闭仍在使用的旧助手窗口后，从桌面快捷方式重新启动 `.7`，再只验收“洛神赋”。
- `git diff --check` 仍只报告 `WPELibrary/Lib/Socket_Cache.cs` 两处既有尾随空格，本次未修改无关脏工作区。

## 2026-08-27 一坐骑取消误报修复与桌面热更新（2026.8.27.5）

- 修复 Android 坐骑只读读取被取消时的错误归类：读取结果现在显式保留 `WasCancelled`，`MountRefineStateMachine` 将首读或发送后刷新阶段的取消统一结束为 `UserStopped / mount_refine_user_stopped`，不再把“用户或宿主停止请求”显示成“Android 只读读取失败”。该修复不会忽略或吞掉真正的取消。
- `Socket_Robot.StopRobot()` 在取消活动坐骑炼化前写入 `stop_requested` 结构化日志，记录当时状态、运行 ID、协议验收和授权状态，便于区分用户/宿主停止与探针自身故障；新增离线回归覆盖“首读成功、发送一次、刷新读取被取消”的路径。
- 本次验证通过：`MountStatusLuaJitProbeRegression`、`MountRefineCompleteFixRegression`、`MountSpeedRunnerHarness`（9 项）、`ReviewRegressionChecks`、Windows PowerShell 5.1 下的 Release `UiDesignRegression` 和 `ClickOnceUpdateRegression`；Native/.NET Framework 4.8 harness Release 重建 0 警告、0 错误，主程序 Release 构建 0 错误，保留既有 4 条 EasyHook `MSB3178` 警告。PowerShell 7 运行 UI 回归时受其宿主禁用 BinaryFormatter 影响，改用 Windows PowerShell 5.1 后通过。
- 已生成签名本地 ClickOnce `releases/2026.8.27.5`；固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式已切换到 `.5` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260827_131407.lnk`。发布包必需文件、应用程序集版本和清单版本均为 `.5`，旧发布、数据库和用户配置保留。
- `.4` 实机记录中，前 3 次发送后的刷新已通过，第 4 次发送也返回 `Accepted`；截图对应的停止原因是 Android 只读读取被共享取消令牌取消，而不是 A050 写入失败。本轮没有停止当前模拟器、没有重新注入、没有新增真实抓包或发送；当前运行中的任务保持原状，需重启助手后才会加载 `.5`。
- Android 只读探针每次完整扫描仍可能耗时约 2–3 分钟；本轮只修正取消分类和诊断，不擅自改变扫描范围或超时语义。`git diff --check` 仍只报告 `WPELibrary/Lib/Socket_Cache.cs` 两处既有尾随空格。

## 2026-08-27 一坐骑刷新上下文误判修复与桌面热更新（2026.8.27.4）

- 修复 Android LuaJIT 探针每次读取都生成随机 `sessionId` 的问题；现在由当前 Android 进程 PID、启动 tick 和可执行文件稳定推导，同一进程连续快照不会再被状态机误判为“坐骑已变化”。进程身份、坐骑 ID 和当前坐骑实例 ID 校验保持不变。
- 本次离线验证通过：`MountStatusLuaJitProbeRegression`、`MountRefineCompleteFixRegression`、`MountSpeedRunnerHarness`（9 项）、`ReviewRegressionChecks`、Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression`；Release 构建 0 错误，保留既有 4 条 EasyHook `MSB3178` 警告。
- 已生成签名本地 ClickOnce `releases/2026.8.27.4`；固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式已切换到 `.4` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260827_122716.lnk`。旧发布、数据库和用户配置保留。
- 之前 `.3` 实机记录已确认 A050 绑定和 39 字节 Socket 写入成功，但因随机会话 ID 在发送后刷新阶段停止；本次未重新执行真实注入或游戏操作。当前模拟器宿主仍在运行旧注入代码，需关闭并重新启动目标模拟器后，从桌面快捷方式重新加载 `.4` 才能进行实机复测。
- `git diff --check` 仍只报告 `WPELibrary/Lib/Socket_Cache.cs` 两处既有尾随空格，本次未改动无关脏工作区。

## 2026-08-27 一坐骑 A050 绑定拒绝诊断日志热更新（2026.8.27.3）

- 在保留原有严格绑定和零发送闸门的前提下，A050 捕获扫描现在会记录安全的拒绝分类与扫描汇总，例如协议不匹配、方向不匹配、路由缺失和终态原因；不记录原始封包字节，也不放宽发送条件。
- `MountRefineCaptureEvidenceHarness`、`MountRefineCompleteFixRegression`、`ReviewRegressionChecks` 和 Release `UiDesignRegression` 均通过；Release 构建 0 错误，保留既有 4 条 EasyHook `MSB3178` 警告。`ClickOnceUpdateRegression` 已通过签名、依赖摘要、`.deploy` 映射和固定入口校验。
- 已生成签名本地 ClickOnce `releases/2026.8.27.3`；固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式已更新到 `.3` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260827_103908.lnk`。旧 `.2`、数据库和用户配置保留。
- 发布后快捷方式预览状态为 `UpToDate`；当前未检测到小黑封包助手进程。关闭旧窗口（如仍有）后，从桌面快捷方式重新启动，才会实际加载 `.3` 并产生新的 A050 拒绝分类日志。
- `git diff --check` 仍报告 `WPELibrary/Lib/Socket_Cache.cs` 两处本轮开始前已存在的尾随空格，本次未改动无关脏工作区；本轮未启动游戏、未注入、未抓包、未发送真实游戏封包。

## 2026-08-27 一坐骑极速抓包 A050 修复

- `2026.8.27.1` 实机日志再次成功读取坐骑 `9111`、唯一实例和 21 张卡，但仍返回 `mount_refine_a050_outbound_capture_missing`。源码复核确认 `SocketPacket_ToQueue` 在 `SpeedMode=true` 时只累计统计量、不把封包放入 UI 队列；`.1` 的 A050 会话观察点位于 UI 队列消费端，因此极速模式下从未执行。
- A050 观察点已前移到 `SocketPacket_ToQueue` 的极速模式分支之前：它先解析当前连接并严格校验出站 A050，再决定是否写入页面队列。新增绑定诊断会记录 `visible/evidence/queued/speedMode/totalPackets/sendBytes`，实机日志可直接区分未抓到、极速绕过、队列积压和证据保留。
- `MountRefineCaptureEvidenceHarness` 现在建立本机环回 TCP 连接并开启 `SpeedMode`，确认 UI 队列为 0 时仍保留 A050、页面清空后可绑定、新会话清零；专用源码回归同时锁定观察调用必须位于极速分支之前。
- 应用和库版本已统一提升为 `2026.8.27.2`。Release 编译 0 错误，保留既有 4 条 EasyHook `MSB3178` 警告；`MountRefineCaptureEvidenceHarness`、`MountRefineCompleteFixRegression`、`MountSpeedRunnerHarness`（9 项）、`MountStatusLuaJitProbeRegression`、`ReviewRegressionChecks`、Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression` 均通过。
- 已生成签名本地 ClickOnce `releases/2026.8.27.2`，版本目录部署清单、应用清单和固定入口三个清单均带签名；固定入口仍为 `releases/小黑封包助手.application`。桌面快捷方式继续指向固定入口，图标已切换到 `.2`，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260827_013500.lnk`。旧发布和数据库未删除。
- 固定入口已实启并由启动日志确认 `assemblyVersion/fileVersion=2026.8.27.2`。关闭 `.1` 助手窗口后直接向仍运行的 `Ld9BoxHeadless` 重注入会返回 EasyHook `STATUS_INTERNAL_ERROR (Code 15)`；旧 `EasyHook64/EasyLoad64` 模块仍驻留，必须先重启该模拟器进程才能装入 `.2`，不能把新启动的注入器版本误当成宿主内已加载版本。
- 重启模拟器并从 `.2` 重新注入后仍需进行实机复测，以结构化日志确认极速模式下 A050 证据计数、当前 Socket 绑定、真实发送和发送后卡片刷新；未取得这些日志前不把离线通过写成实机流程已跑通。

## 2026-08-27 一坐骑 A050 捕获生命周期修复

- 实机结构化日志已确认：一坐骑预检成功读取当前坐骑 `9111`、唯一实例和完整 21 张炼化卡，但只在约 90 秒只读快照结束后才从页面捕获表查找 A050；页面默认超过 5000 条会整表自动清空，因此已经显示过的 A050 在绑定时变成 `mount_refine_a050_outbound_capture_missing`，发送次数保持 0。
- `Socket_Cache.SocketList` 现在为当前注入会话独立保留最多 64 条结构有效的 A050 出站证据，不受页面筛选或自动清空影响；开始新注入会话时立即清空，绝不跨会话沿用。A050 启动绑定和发送前当前路由解析都读取该会话证据，仍要求方向唯一、当前 Socket 可解析、协议校验和本次授权全部成立。
- 新增 `MountRefineCaptureEvidenceHarness`，实际覆盖“页面列表清空后仍可绑定”和“新注入会话旧证据归零并 fail-closed”；`MountRefineCompleteFixRegression` 同步检查捕获、重置、绑定和发送路由接线。
- 应用和库版本统一提升为 `2026.8.27.1`。Release 无签名编译 0 错误，保留既有 4 条 EasyHook `MSB3178` 警告；`MountSpeedRunnerHarness`、`MountRefineCaptureEvidenceHarness`、`MountRefineCompleteFixRegression`、`MountStatusLuaJitProbeRegression`、`ReviewRegressionChecks`、Release `UiDesignRegression` 和签名 `ClickOnceUpdateRegression` 均通过。
- 已生成签名本地 ClickOnce `releases/2026.8.27.1`，应用、库、版本清单和固定入口版本一致，三个清单均带签名；固定入口仍为 `releases/小黑封包助手.application`。桌面快捷方式继续指向固定入口，图标切换到 `.1`，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260827_012028.lnk`。旧发布和数据库未删除。
- `git diff --check` 仍报告 `Socket_Cache.cs` 两处本轮开始前已存在的尾随空格（旧助手管理代码），本次未为通过检查改动无关脏工作区。实机流程还需从 `.1` 重启后再次运行一坐骑，并以结构化日志确认 A050 绑定、发送和卡片刷新。

## 2026-08-26 只读背包 reader 版本同步验收

- 弹窗 `resident_reader_exited_before_connect` 的直接证据是远端 `/data/local/tmp/equipment-streamd` 与本地已验证构建不一致；未修改游戏进程或发送链。已将本地 `F:\项目目录\飘渺游戏助手\tools\equipment-reader\android\build\equipment-streamd`（SHA-256 `5B018F165BF4DFCFEC13C9865653E50F916F210670C3DD21A9682ED266728D30`）部署到远端同一路径，旧文件保留为 `/data/local/tmp/equipment-streamd.before-wpe-20260826-175839`。
- 随后只执行一次 `inventory-only` 读取：游戏绑定 `PID=1850/startTicks=1461`，状态文件为 `F:\项目目录\飘渺游戏助手\tools\equipment-reader\work\wpe-live-inventory-20260826-175909.state.json`，JSONL 为 `F:\项目目录\飘渺游戏助手\tools\equipment-reader\work\wpe-live-inventory-20260826-175909.jsonl`；返回 `available=true`、`itemCount=4`、`streamSessionId=service-413312711465`、`snapshotId=snapshot-425655777393`、`sequence=3`、`diagnosticCode=normal_eof`，`actionAuthorized=false`。4 条装备均带 slot/memberIdentity/rawFields，名称和阶数状态为已解码；没有读取炼化卡、发送炼化包或替换属性。
- reader 诊断显示 C6 hello/hello_ack、首轮扫描和 ready 均成功，扫描约 12.3 秒；正常 EOF 保留 ready 快照，端口转发清理成功。该次只证明实时背包枚举链恢复，不证明真实炼化请求、20 卡二进制解码或发送器已接通。

## 2026-08-26 坐骑速度流程迁移到一坐骑洗炼

- `Socket_Cache.Robot.EnsureBuiltInFirstRideRefinePreset` 已将旧“坐骑速度”的完整 11 步 `MountSpeed` 流程迁移到“一坐骑洗炼”；旧记录保留作回退，不覆盖一坐骑已有的非空自定义步骤。
- 一坐骑启动时若数据库步骤为空或目标未保存，会自动补齐坐骑流程，并固定带入高级秋水流弦（61108）、高级百步穿杨（61118）、高级追魂夺命（61117）和成长率 `1.175`；编辑页也会自动补齐空步骤。
- 当前本机数据库已确认一坐骑原来为 0 条指令、0 条坐骑目标记录；迁移演练已确认生成 11 条步骤、目标四项正确且旧记录仍保留。迁移只在新程序启动加载时落盘，不直接改写当前数据库。
- 本轮 WPELibrary Debug、主程序 Debug 隔离重建均通过；保留既有 4 条 EasyHook `MSB3178` 清单警告。未启动游戏、未注入、未读取真实内存、未发送封包。
- 已按本地 ClickOnce 流程生成最终未签名版本 `releases/2026.8.26.12`；固定入口 `releases/小黑封包助手.application` 已指向 `.12`，桌面快捷方式已切换到 `.12` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260826_175622.lnk`。旧发布、数据库和两个正在运行的旧助手进程均保留。
- `.12` 发布包必需文件齐全，部署清单的 31 个依赖全部存在；发布版程序集已确认包含一坐骑迁移和 11 步流程生成方法。`ReviewRegressionChecks`、Release `UiDesignRegression`、`AssistantManagementRegression`、`MountSpeedRunnerHarness`（9 项）和 `git diff --check` 均通过。`ClickOnceUpdateRegression` 仅因本次明确使用 unsigned 本地包而在签名断言处失败，不能视为签名发布。

## 2026-08-26 坐骑炼化 A050 自动验收接线

- 修复“一坐骑洗炼”启动后已捕获有效 A050 出站包，却仍在发送前提示“协议尚未完成验收”的问题。当前运行若由一坐骑预设明确启动，并从本次注入捕获中找到结构有效且方向唯一的 A050 出站包，会自动完成本次会话的协议验收和临时发送授权，不要求用户再填写协议开关或确认文本。
- 自动接线仍只在当前会话生效，不把授权写入预设；每次启动重新绑定当前 A050 出站方向、重新解析当前 Socket，并用只读快照的当前坐骑实例号生成发送副本。没有有效当前出站包、方向不唯一、连接解析失败或卡片刷新失败时继续零发送并停止。
- 新增管理回归断言覆盖自动验收/授权接线；WPELibrary 隔离 Debug Rebuild、主程序 `SignManifests=false` 隔离 Debug Rebuild、`MountSpeedRunnerHarness`（9 项）、`AssistantManagementRegression`、`ReviewRegressionChecks`、Release `UiDesignRegression` 和发布清单依赖核对均通过。主程序正常 ClickOnce 签名构建仍受本机证书存储缺少清单证书影响；`ClickOnceUpdateRegression` 仅因本次使用 unsigned 本地包在签名断言处失败。
- 已生成未签名本地 ClickOnce `releases/2026.8.26.13`；固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式已切换到 `.13` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260826_190338.lnk`。旧版本、数据库和两个正在运行的旧助手进程均保留。
- 本轮未启动游戏、未执行真实发送；下一次实机测试只针对一坐骑，需从最新入口启动并观察发送后 21 张卡片是否刷新。

## 2026-08-26 坐骑 Android 角色发现临时失败重试修复

- 现场日志曾返回 `status=needs_discover`、`diagnosticCode=local_player_missing`、`candidateCount=0`；同一 Android PID 1894 稍后只读复读成功，确认是角色对象加载窗口而不是包名或 PID 错误。
- `MountStatusAndroidSnapshotReader` 现在仅对 `local_player_missing` 和 `field_string_missing` 做 1 次、可取消的 1 秒重试；不会放宽唯一角色、字段值、进程启动 tick 或快照协议校验，其他错误仍立即失败。
- 探针回归、`AssistantManagementRegression.ps1`、WPELibrary Debug 隔离构建和 `MountSpeedRunnerHarness` 均通过；本次修复仍未写内存、未注入、未点击页面、未发送封包。
- 已生成未签名本地 ClickOnce `releases/2026.8.26.14`；固定入口和桌面快捷方式已核对为最新版本，备份为 `releases/shortcut-backups/小黑封包助手_20260826_193030.lnk`。旧版本、数据库和正在运行的旧助手进程均保留。

## 2026-08-26 背包读取来源接线修复

- `Socket_RobotForm` 打开装备炼化编辑器时，背包读取来源按“宿主注入 provider → 预设中的只读 state 文件 → `WPE_EQUIPMENT_STATE_FILE` → 单次 `EquipmentInventoryAndroidSnapshotReader`”顺序解析；最后一项只调用现有 `equipment_inventory_resident.py` 的 `--inventory-only --max-polls 1`，不扫描目录、不读取旧快照、不导航页面、不发包。
- 本机当前没有运行中的 equipment reader，`WPE_EQUIPMENT_STATE_FILE` 未配置；旧 equipment-reader state 文件最后更新时间不是当前会话，因此本次没有把它自动当作当前背包。点击“读取背包”或启动背包预设时，若本地 ADB/Python/reader 依赖均可定位，才会执行一次有界只读读取；依赖缺失、reader 失败或 state 不可用均保持“背包读取不可用”并 fail-closed。
- 本轮离线验证：`EquipmentRefineRunnerHarness: PASS (49 tests)`、WPELibrary Debug Rebuild、主程序 Debug Rebuild（`SignManifests=false`）、`EquipmentRefineRuntimeWiringRegression` 和 `git diff --check` 均通过；未启动 reader、未操作游戏、未发包。
- 已生成未签名本地 ClickOnce `releases/2026.8.26.11`，固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式仍指向固定入口，图标已切换到 `.11`，备份为 `releases/shortcut-backups/小黑封包助手_20260826_175002.lnk`。旧版本、数据库和两个正在运行的旧助手进程均保留，需关闭旧进程后从桌面快捷方式重新启动才会加载本版本。
- 发布后 `ReviewRegressionChecks` 通过；`UiDesignRegression.ps1 -Configuration Release` 仍受既有 BinaryFormatter 禁用阻塞，`ClickOnceUpdateRegression.ps1` 按预期拒绝未签名本地包（签名断言失败），没有把这两项写成通过。

## 2026-08-26 装备炼化背包装备选择热更新

- 已生成未签名本地 ClickOnce `releases/2026.8.26.9`；固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式图标已切换到 `.9`，备份为 `releases/shortcut-backups/小黑封包助手_20260826_171326.lnk`。旧发布、数据库、配置和运行中的旧助手进程均保留。
- 本版本把炼化目标下拉改为只读背包装备：显示已确认的名称和阶数，隐藏 slot/memberIdentity/item ID；“读取背包”只调用宿主注入的只读 provider，未配置时保持空列表并提示，不导航、不发包。
- 发布构建、Review、Release UI 设计检查和 `git diff --check` 通过；`ClickOnceUpdateRegression` 的签名断言因本次明确使用 unsigned local release 保持失败，不能视为签名发布。未启动游戏、未炼化、未发包。

## 2026-08-26 一坐骑启动自动带入固定目标

- 修复直接从助手列表/快捷方式启动时没有传递坐骑目标的缺口：当指令属于名称为 `一坐骑洗炼` 的助手且保存目标为空或不完整时，主启动入口 `Socket_Cache.DoRobotAsync` 自动使用固定的三个技能和成长率 `1.175`；编辑窗口启动入口也保持同样兜底。
- 不需要每次重复填写；已保存的完整目标保持不变，二/三/四坐骑和普通“坐骑速度”预设不受影响。
- 本次只修复目标参数进入运行链的方式；启动后仍会重新读取当前坐骑和 21 张炼化卡，A050 协议、当前连接和显式真实发送授权未通过时继续零发送并给出停止原因。
- 已生成 unsigned 本地 ClickOnce `releases/2026.8.26.10`；固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式已更新到 `.10`，备份为 `releases/shortcut-backups/小黑封包助手_20260826_171906.lnk`。旧版本、数据库和运行中的旧助手进程均保留。
- 本轮验证：WPELibrary Debug 隔离重建 0 错误、主程序 Debug 隔离重建 0 错误（保留原有 4 条 EasyHook `MSB3178` 警告）、Release ClickOnce 构建 0 错误、`MountSpeedRunnerHarness` 9 项通过、`MountStatusLuaJitProbeRegression` 通过、`ReviewRegressionChecks` 通过、发布目录依赖核对通过、快捷方式状态 `UpToDate`、`git diff --check` 通过。`UiDesignRegression` 仍受本机既有 BinaryFormatter 禁用影响，`ClickOnceUpdateRegression` 仅因本次明确为 unsigned 本地包未通过签名断言。

## 2026-08-26 一坐骑固定目标桌面热更新

- 已生成签名本地 ClickOnce `releases/2026.8.26.8`，包含一坐骑固定目标默认值、浮点坐标兼容后的坐骑读取器和技能名称映射文件；固定入口仍为 `releases/小黑封包助手.application`。
- 桌面快捷方式 `C:\Users\Administrator\Desktop\小黑封包助手.lnk` 已更新到最新版本图标，并保留旧快捷方式备份 `releases/shortcut-backups/小黑封包助手_20260826_165437.lnk`。
- `ReviewRegressionChecks`、`UiDesignRegression -Configuration Release`、`ClickOnceUpdateRegression` 和 `git diff --check` 均通过。
- 当前运行中的旧助手进程未关闭；需关闭旧窗口后从桌面快捷方式重新启动，热更新版本才会实际运行。本次仍未注入、未写内存、未点击炼化、未发送真实封包。

## 2026-08-26 坐骑本地角色浮点坐标兼容修复

- 修复 Android LuaJIT 坐骑探针只接受 `m_Gx/m_Gy` 为 `i32` 的问题；当前客户端会把这两个坐标以有限 `f64` 保存，探针现在在保留本地角色、玩家 ID、坐骑 ID 和速度校验的前提下兼容并归一化为 Int32。
- 新增 `tests/MountStatusLuaJitProbeRegression.py`，覆盖整数坐标、浮点坐标和非有限坐标拒绝三种情况。
- 修复后使用 rooted ADB 对当前进程 `PID 1866` 完成只读复测：唯一候选、本地角色 `playerId=66`、坐骑 `9111`、当前实例 `2092100442933833732`、成长率 `1.175`，技能为“高级秋水流弦 / 高级百步穿杨 / 高级追魂夺命”；当前未打开炼化页，因此炼化卡片为空，不代表读取失败。
- 本次只读复测未写内存、未注入、未发包、未执行坐骑操作；坐骑炼化真实发送链路仍保持原有 fail-closed 闸门。

## 2026-08-26 一坐骑固定目标预设默认值

- 新增 `MountSpeedPreset.CreateDefaultFirstRideRefinePreset`，为新建或尚未配置坐骑目标的编辑入口默认填入三个固定技能：高级秋水流弦（61108）、高级百步穿杨（61118）、高级追魂夺命（61117），以及固定成长率 `1.175`。
- `Socket_RobotForm` 使用该默认工厂加载一坐骑目标；已有保存的普通坐骑速度预设不自动覆盖，“清空目标”仍可恢复为空目标。点击“应用到助手”或助手主保存后才写入对应预设。
- 坐骑运行器回归新增固定目标检查；坐骑运行器 9 项、`WPELibrary` Debug 隔离构建和主程序 Debug 隔离构建均通过。当前仍未启用真实炼化发包。

## 当前装备炼化目标模式状态（本轮）

- 新增协议中立的手动出包绑定链：`EquipmentRefineManualCaptureSource` 只观察用户手动炼化后、武器/装备请求方向的新记录；`EquipmentRefineManualCaptureBinding` 对离线已观察的单帧边界（MZ、总长 44、体长 `0x22`、raw code `0x8008`）做严格校验，并只提取 19 位 ASCII ID，其他字段保留 raw，不解析接收包。
- WPE 现有捕获列表通过 `EquipmentRefineSocketPacketCaptureSource` 注入该契约；`EquipmentRefineHost.CaptureAndBindManualRefineAsync` 只完成捕获和目标 ID 绑定，不自动启动 Runner、不导航页面、不发包、不替换属性。绑定后仍必须由宿主显式调用 `StartAsync`，且原有真实 sender 授权/模板/状态源闸门不变。
- 目标不一致、入包、空帧、分帧/长度/头部不符、无 socket/目的地址和超时均 fail-closed；不会使用旧捕获记录（只接受 armedAtUtc 之后的记录）。
- `EquipmentRefineStateMachine` 已修正：`ResponseCardMode`/`MemoryResultMode` 不再使用发送前旧属性快照提前判定成功，只由本轮发送后的响应卡/内存快照决定。
- `EquipmentRefineRunnerHarness` 当前实际调用 48 项，最新 Rebuild 与运行均通过；WPELibrary Debug Rebuild 通过。真实 20 卡内存实时 source、真实请求模板/发送、真实资源错误码和实机炼化仍未验证，默认未配置依赖继续发送前零发送。

## 2026-08-26 装备炼化预设改为背包装备选择

- `EquipmentRefinePresetEditor` 现在只显示背包装备下拉框、属性下拉框、目标数值和启用/添加/删除规则按钮；身份、resident/source 路径、模板路径、K 和速度高级项均不再显示。
- 子页面已缩小为 560×400，并在底部保留“保存”和“确定”按钮；新建默认预设保存 5 行规则，仅第一行默认启用，其余 4 行可直接选择/填写后启用。
- 背包装备下拉只接受同一份已验证只读 Bag 快照中的唯一 `slot + memberIdentity`；显示名称和阶数只使用 reader 明确标记为 `decoded` 的 `displayName`/`xianqiTierLabel`，未解码时明确显示“未解码装备”，不按数组顺序或名称猜测目标。主窗体提供 `SetEquipmentRefineBagOptionsFromInventory` 和只读 `SetEquipmentRefineBagInventoryProvider` 注入入口；没有注入快照/reader 时列表为空，点击“读取背包”会提示未配置，不会发送或操作游戏。
- 比较方式固定保存为 `>=`，目标值仍由用户手动填写；保存时 `RequiredMatches=1`，最大尝试默认 20 次、间隔默认 1500ms。旧模型/序列化字段继续保留，运行时仍由状态机执行严格校验。
- 背包模式保存下拉选中的 `slot/memberIdentity`，并同步保留已读 `m_ItemId/m_ItemTypeId/name/rawFields` 与可选阶数；旧穿戴模式模型仍保留，但本编辑器不再把“穿戴部位”作为目标入口。
- 本轮验证：`EquipmentRefineRunnerHarness: PASS (48 tests)`、WPELibrary Debug Rebuild、主程序 Debug Rebuild（`SignManifests=false`）均通过；Review 与炼化 runtime wiring 回归通过。UI 设计脚本在本机因 PowerShell 应用域禁用 BinaryFormatter 失败，未改其环境配置；未启动游戏、未发包、未替换属性。
- 已生成未签名本地 ClickOnce `releases/2026.8.26.7`，固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式目标保持固定入口、图标已切换到 `.7`，本次备份为 `releases/shortcut-backups/小黑封包助手_20260826_164453.lnk`；旧版本和用户数据未清理。

## 2026-08-26 坐骑炼化循环状态机运行时接线

- 新增 `MountRefineStateMachine`：每轮先读取当前坐骑的 21 张炼化卡并匹配“三技能 + 成长率”；启动时已命中则直接停止且发送次数为 0，未命中时才进入发送接口，发送被接受后等待卡片字段变化，再重新读取和匹配，命中后停止。
- 状态机要求进程身份、会话、当前坐骑实例和坐骑 ID在刷新前后保持一致；读取失败、卡片不完整、换坐骑、刷新超时、次数上限、协议未验证或授权缺失都会停止，不沿用旧快照。
- 发送接口目前只接收已读取的炼化上下文，不包含原始 Socket；默认 `FailClosedMountRefinePacketSender` 永不发送，`RecordingMountRefinePacketSender` 仅用于离线记录。`Socket_Robot` 已在完整四项目标的坐骑 `IDLE` 步骤接入该状态机：启动预检重新读取 21 张卡，之后每轮按“目标检查 → 发送闸门 → 等待卡片变化 → 重读匹配”执行，命中即停止；用户停止会同时中止状态机和机器人。
- 已只读对照装备炼化发送链：现有 `EquipmentRefineSocketPacketSender` 可以复用“每次发送前解析当前连接”的路由原则，但它的模板字段是装备槽位/装备 ID/炼化类型，不能直接当作坐骑炼化封包；当前源码和本地资料中没有已验收的坐骑炼化请求字段，不能据此猜包。
- 已对比用户提供的四只坐骑 `A050` 样本：均为 39 字节、body 长度 `0x1D`，修正版样本的操作前缀完全一致，仅末尾 19 字节 ASCII 数字字段发生变化。新增 `MountRefineA050PacketTemplate`，严格校验 `4D5A`、`0xA050`、操作前缀和 19 字节数字字段，并只在离线发送副本中替换读取器提供的 `ActiveRideInstanceId`。
- `MountSpeedRunnerHarness` 已增加 A050 模板回归，验证四只坐骑样本均可由同一模板生成、模板原始实例 ID不被修改、非法长度 ID 被拒绝；当前共 9 项通过。该样本仍未证明业务字段语义、出站方向、当前连接或服务端接受条件。
- 发送方向不要求用户手填：新增 `MountRefineA050CaptureBinding`，从注入捕获列表自动忽略入站包、筛选 A050 出站包，保存 `PacketType/PacketFrom/PacketTo`；没有候选或存在多个不同方向时拒绝绑定。`Socket_Robot` 每次启动坐骑炼化目标时从当前捕获列表重建绑定，不沿用上次运行的自动模板；`MountRefineA050SocketPacketSender` 每次发送前重新解析当前 Socket，并额外要求显式协议确认和 `MOUNT-REFINE-LIVE-SEND` 授权。真实发送仍未执行，A050 业务字段、服务端接受条件和授权入口仍未验收。
- 本轮验证：WPELibrary Debug 隔离重建 0 错误、主程序 Debug 隔离重建 0 错误（保留原有 4 条 EasyHook `MSB3178` 警告）；`MountSpeedRunnerHarness` 9 项通过；另以已构建程序集验证“入站忽略、唯一出站绑定、多个出站方向拒绝”。
- `MountSpeedRunnerHarness` 已增加状态机离线回归：目标命中后一次刷新停止、启动即命中零发送、协议未验证零发送，8 项全部通过。

## 2026-08-26 装备炼化三项收尾接线

- 助手页的装备炼化总设置现在可保存只读结果 JSON/JSONL 路径、已验收炼化模板路径、背包协议位置（可选）以及原有目标/属性规则；预设计划、JSON 克隆和编辑模型均保留这些字段。背包 reader 使用非数字稳定 `slot` 时，不按列表顺序猜协议位置；只有模板明确需要 `SlotIndex` 且用户填入已验收位置才允许发送，另支持已验收的 `ItemIdAscii` 字段。
- `Socket_Robot` 已将预设路径接入 `ResidentJsonlRefineMemoryResultSource` 和模板加载链；助手页在当前 Socket 路由与模板同时有效时，每次启动弹出一次性真实发送确认，确认文本不会写入预设。缺少任一条件仍在发送前停止并保持零发送。
- 运行完成后，`Socket_Robot` 将状态机结果回传助手页；命中结果提示包含第几张卡片、命中的中文属性/值和发送次数，未命中、未授权、目标变化、结果超时和协议未验收也显示明确停止原因。
- 用 `飘渺游戏助手\tools\equipment-reader\work\equipment-inventory-rerefine-live-state.json` 做离线复核：只读背包快照为 60 个条目，指定条目唯一匹配，resident source 从对应路径解码出完整 20 张卡（sequence 4）；缺少 `propertyKey` 的评分字段仍保留 rawId/rawValue/rawOrder 并按未知映射处理。该复核没有启动 reader、注入进程或发送封包。
- `EquipmentRefineRunnerHarness` 已扩展为 45 项通过；WPELibrary Debug 和主程序 Debug 重建通过。真实炼化请求模板、实机发送后结果 source、资源错误码和真实发送验收仍未完成，当前不伪造模板、不启动目标游戏、不注入、不抓包、不发送真实封包。

## 2026-08-26 坐骑炼化四项目标与无序技能命中预演

- 坐骑目标配置现在固定为 4 项：3 个技能和 1 个成长率；界面限制技能最多 3 项，保存炼化目标时要求三项技能与成长率全部有效。
- 新增 `MountRideRefineTargetMatcher`：对只读快照中的 21 张卡片逐张判断，成长率按 `0.0005` 容差匹配，三个技能采用一对一无序匹配；技能行顺序打乱仍可命中，任一张卡完整满足四项即报告命中卡片索引。
- 当前已接入配置、只读预演、命中展示和助手主运行入口；完整四项目标会在 `MountSpeed` 计划的 `IDLE` 步骤进入坐骑炼化循环，未完整填写时仍保持旧的坐骑只读速度流程。仍没有发送炼化封包、没有点击炼化、没有写内存；真实发包前还需单独验收模板字段、当前连接和显式授权。

## 2026-08-26 装备炼化安全发送适配器接线

- 新增 `EquipmentRefineSocketPacketSender`：发送前重新解析当前连接，只接受发送方向和非空目标地址；连接缺失、多个候选、方向错误、发送授权缺失或 Socket 写入失败时保持零发送。
- 新增 `EquipmentRefineLiveSendAuthorization` 显式授权令牌；助手启动只传递当前封包的方向/地址解析模板，不隐式获得真实发送授权。`Socket_Robot` 已支持外部显式注入适配器、解析模板和授权令牌。
- 当前仍未接入真实炼化请求模板、实机 resident 属性/结果 source 和真实授权入口；本轮仅做编译、离线状态机和无当前连接的 fail-closed 验证，未启动目标游戏、未注入、未抓包、未发送真实封包。

## 2026-08-26 装备炼化运行时结果源与背包目标接线

- `Socket_Robot` 现在把预设的 `BagTargetMode`/`BagTarget` 和 `MemoryResultMode` 传入状态机；启用 typed resident 结果模式时，可显式注入 `EquipmentRefineMemoryResultSource`，或通过 `EquipmentRefineMemoryResultPath`、`EquipmentRefineStateFilePath`、`WPE_EQUIPMENT_STATE_FILE` 读取只读 JSON/JSONL 适配器。
- 该路径只消费外部已经生成的 schema 3 只读 resident 文档，仍严格要求同一目标、刷新 sequence/snapshot 和完整 20 张卡；不会启动 reader、附加进程、猜地址/偏移或自动取得真实发送授权。
- 已增加 `WPE_EQUIPMENT_REFINE_TEMPLATE_FILE` 作为可选的已验收模板 JSON 路径；模板仍必须通过 `ProtocolVerified/EvidenceId/字段范围` 校验。请求字段语义、实机属性/20 卡二进制解码、资源错误码和真实授权仍未验收；没有这些证据时状态机继续在发送前停止。

## 2026-08-26 坐骑炼化页面 21 张技能卡只读读取

- Python LuaJIT 探针已从选中 `LocalRide.m_RideIns` 复核到 `m_ResetData[4]` 的 33 槽临时卡片表：非空候选卡为页面索引 2–21，共 20 张；当前/自带卡复用 `m_Rideskills` 作为索引 1，快照合计 21 张。
- 候选卡已输出成长率（`growty/1000`）、速度、评分和三项技能；技能 ID 从 `skill[].name` 数字字符串读取，并通过现有 `ride_skill_name_map.json` 映射中文名称。技能子记录的 `val` 只保留原始字符串，不解释为经验。
- C# 只读快照协议、坐骑页面文本显示和 `MountSpeedRunnerHarness` 离线 fixture 已接入 21 卡结构；现场 rooted-ADB 复读返回 21 张，当前卡为 `高级秋水流弦 / 高级泣血枕戈 / 高级百步穿杨`，候选卡名称已映射。
- 坐骑只读页面已保留当前窗体内上一次成功快照，按卡片索引比较成长率、速度、评分和技能；换坐骑或游戏进程重启时自动放弃旧基线并重新建立，不沿用旧卡片数据。
- 本轮只读内存和解析，没有写内存、注入、点击炼化、发包或停止炼化；目标卡命中后自动停止的状态机仍未启用。

## 2026-08-26 装备炼化属性命中即停

- `EquipmentRefineStateMachine` 的发送前 `target_reached_before_send` 仅保留给旧正式属性快照模式；响应卡模式和内存结果模式必须先发送，再使用本轮完整结果判定，不能用发送前旧快照提前成功。
- 响应卡/内存模式仍在发送前做 source/adapter、身份和 baseline 闸门；发送后只接受本轮稳定刷新结果，命中即停，未命中才继续，缺少依赖时保持 `ProtocolUnverified` 和零发送。
- `EquipmentRefineRunnerHarness` 43 项通过；本轮只做离线状态机与回归验证，未启动目标游戏、未注入、未抓包、未发送真实封包。

## 2026-08-26 装备炼化预设完整中文属性目录

- `EquipmentRefineAttributeCatalog` 已按只读 reader 的已解码字段加入 55 项炼化属性，顺序与确认清单一致：包含根骨、抗性、忽视抗性、系别狂暴、加强、强力克，以及抗感山/抗啸月；预设页面只展示中文属性名称。
- `EquipmentRefinePresetEditor` 的属性下拉现在默认显示这 55 项；新规则默认从“根骨”开始。旧 JSON 中仍使用旧占位枚举的规则会按需追加对应中文项，原有枚举数值不重排，避免旧预设失效。
- 百分比规则继续使用整数协议单位，界面输入 `2` 仍表示 `2.0%`；比较符仍只允许 `>=` 和 `=`。目录提供显式 reader 字段映射辅助，但没有自动填充或放宽现有 fail-closed 映射边界。
- `EquipmentRefineRunnerHarness` 43 项通过；WPELibrary Debug 与主程序 Debug 构建通过。主程序构建仍保留 4 条既有 EasyHook `MSB3178` 警告；未启动目标游戏、未注入、未抓包、未发送真实封包。

## 2026-08-26 坐骑 Python 运行时路径修复与本地 ClickOnce 热更新（2026.8.26.0）

- 修复 `MountStatusAndroidSnapshotReader.ResolvePythonPath` 只返回裸 `python.exe`、导致桌面端启动坐骑 LuaJIT 探针时报“系统找不到指定的文件”的问题；现在会优先查找现有的 `%LOCALAPPDATA%\XNAS\WPE\vision-worker` Python 运行时，并解析 PATH 中的 `python.exe`/`py.exe`。
- 已生成签名本地 ClickOnce `releases/2026.8.26.0/`，发布目录包含 204 个文件和 88 个 `.deploy` 文件；坐骑探针、`ride_skill_name_map.json` 和 213 条技能映射均已随包交付。
- 固定入口和桌面快捷方式已切换到 `.0`；快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260826_004758.lnk`。旧发布目录、数据库和用户配置未清理。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；坐骑运行器 7 项、宠物只读状态/库存/UI 回归、Review、发布版 UI、ClickOnce 清单和依赖审计、`git diff --check` 均通过。助手管理回归保留一条与本次坐骑无关的既有预设选择器断言失败，未修改该业务逻辑。
- 未启动目标游戏，未执行真实注入、抓包、封包发送或坐骑操作。

## 2026-08-26 坐骑成长率只读读取

- 已确认 `RideData[Shape].GrowthRate=940` 是坐骑模板字段，不是每只坐骑当前显示值，探针不再将它作为成长率输出。
- 当前 `emulator-5554` 的真实 rooted-ADB 只读复测已从四个 `LocalRide.PropertyValueDict.GROWUP` 读取当前值：Shape `9111=1.175`、`9131=0.975`、`9121=1.175`、`9141=0.935`；当前上马实例为 Shape `9111`。
- C# 快照协议、坐骑状态对象和坐骑只读界面已改为透传并显示实例当前成长率，来源标记为 `LocalRide.PropertyValueDict.GROWUP`；未增加写内存、注入、封包发送或坐骑操作能力。
- Python 探针编译、`MountSpeedRunnerHarness` 7 项回归、WPELibrary Release 隔离构建和 `git diff --check` 均通过。

## 2026-08-25 测试（一）新增超级宝图复用受保护发送逻辑本地热更（2026.8.25.26）

- CLI 只读确认“超级宝图”位于“测试（一）”分组，当前保存值为 `LoopCNT=1`、`LoopINT=1000ms`；19 字节 `0xF908` 的 `BufferHex` `4D5A0000000000000009F9080000002B013400` 保持不变。
- “超级宝图”已加入与“抗性”等受保护预设相同的当前 Socket、当前会话序号、1800ms 最小间隔和失败即停止逻辑；只复制发送缓冲并更新发送副本的第 4–7 字节，不改写数据库或原始封包。
- 本轮未向数据库写入；旧发布、数据库和用户配置未清理。已生成签名本地 ClickOnce `releases/2026.8.25.26/`，发布目录 204 个文件、88 个 `.deploy` 文件，EXE 版本为 `2026.8.25.26`。
- 固定入口和桌面快捷方式已切换到 `.26`，当前预览状态 `UpToDate`；快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_222231.lnk`。
- 使用 `.26` 发布目录的受保护预设回环、出售盘古精铁会话/发送回归、发送保护静态回归、Review、ClickOnce 和 `git diff --check` 均通过；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- `UiDesignRegression.ps1 -Configuration Release` 仍因当前环境禁用 .NET `BinaryFormatter` 在窗体构造处失败，非本轮发送逻辑改动引起；未启动目标游戏、未注入、未抓包、未发送真实游戏封包。

## 2026-08-25 抗性发送次数和间隔更新本地热更（2026.8.25.25）

- CLI 只读确认“抗性”仍位于“测试（一）”分组，当前保存值为 `LoopCNT=1000`、`LoopINT=2000ms`；24 字节 `0x70AB` 的 `BufferHex` `4D5A0000000A2623000E70AB0B7B273230323037273A307D` 保持不变。
- 本轮未向数据库写入；旧发布、数据库和用户配置未清理。工作期间数据库文件整体最后写入时间仍出现无法由本次 CLI 归因的变化，但“抗性”目标行只读复核为上述值。
- 已生成签名本地 ClickOnce `releases/2026.8.25.25/`，发布目录 204 个文件、88 个 `.deploy` 文件，EXE 版本为 `2026.8.25.25`；固定入口和桌面快捷方式已切换到 `.25`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_215325.lnk`，当前预览状态 `UpToDate`。
- 使用 `.25` 发布目录的受保护预设回环、出售盘古精铁会话/发送回归、发送保护静态回归、Review、ClickOnce 和 `git diff --check` 均通过；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- `UiDesignRegression.ps1 -Configuration Release` 仍因当前环境禁用 .NET `BinaryFormatter` 在窗体构造处失败，非本轮发送逻辑改动引起；未启动目标游戏、未注入、未抓包、未发送真实游戏封包。

## 2026-08-25 测试（一）新增抗性预设复用受保护发送逻辑本地热更（2026.8.25.24）

- “抗性”已确认位于“测试（一）”分组，保存的是 24 字节 `0x70AB` 帧；已加入与出售盘古精铁、郭氏积分和测试（一）其他预设相同的当前 Socket、当前会话序号、1800ms 最小间隔和失败即停止保护。
- “抗性”的原始 `BufferHex` `4D5A0000000A2623000E70AB0B7B273230323037273A307D` 未被数据库或发送集合改写；回环验证确认发送副本只更新第 4–7 字节会话序号，正文保持不变。
- 受保护预设回环、出售盘古精铁会话/发送回归、发送保护静态回归、Review、ClickOnce 和 `git diff --check` 均通过，发布目录 DLL 回归也通过。
- 已生成签名本地 ClickOnce `releases/2026.8.25.24/`，发布目录 204 个文件、88 个 `.deploy` 文件；固定入口和桌面快捷方式已切换到 `.24`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_214305.lnk`，当前预览状态 `UpToDate`。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；`UiDesignRegression.ps1 -Configuration Release` 仍因当前环境禁用 .NET `BinaryFormatter` 在窗体构造处失败，非本次发送逻辑改动引起。数据库文件整体哈希/最后写入时间在工作期间出现无法由本次 CLI 归因的变化，但“抗性”目标行只读复核未变；未启动目标游戏、未注入、未抓包、未发送真实游戏封包。

## 2026-08-25 测试（一）分组全部预设复用受保护发送逻辑本地热更（2026.8.25.23）

- “测试（一）”分组共 13 个预设：百亿玉、百亿银子、百亿师贡献、百亿帮贡、百亿成就、百亿积分、炼星石、积分、嘉嘉的嫁妆、扭转乾坤、子虚乌有、化无、成仁取义；均按名称加入与“出售盘古精铁”相同的当前 Socket、当前会话序号、1800ms 最小间隔和失败即停止保护。
- 这 13 个预设保存的是 35–46 字节的 `0x4062` 帧，数据库正文未被代码改写；回环验证确认每个预设除发送副本的第 4–7 字节会话序号外，其余字节保持不变，原始 `Socket_PacketInfo.PacketBuffer` 不被修改。
- 7 个郭氏积分预设和原出售预设回归仍通过；`ProtectedPresetSendWorkerRegression.ps1`、会话/发送回归、发送保护静态回归、Review、ClickOnce 和 `git diff --check` 均通过，发布目录 DLL 回归也通过。
- 已生成签名本地 ClickOnce `releases/2026.8.25.23/`，发布目录 204 个文件、88 个 `.deploy` 文件；固定入口和桌面快捷方式已切换到 `.23`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_213446.lnk`，当前预览状态 `UpToDate`。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；`UiDesignRegression.ps1 -Configuration Release` 仍因当前环境禁用 .NET `BinaryFormatter` 在窗体构造处失败，非本次发送逻辑改动引起。工作期间数据库文件整体 SHA-256 发生变化，但目标 13 个预设的长度和正文只读复核未变；未启动目标游戏、未注入、未抓包、未发送真实游戏封包。

## 2026-08-25 郭氏积分一至七复用出售盘古精铁发送保护本地热更（2026.8.25.22）

- “郭氏积分”分组中的“积分一”到“积分七”已加入与“出售盘古精铁”相同的当前 Socket、当前会话序号、1800ms 最小间隔和失败即停止保护；发送入口按预设名称识别，数据库分组和预设正文未改动。
- 7 个预设的原始字节已只读复核：积分一为 45 字节，其余为 26 字节，均为 `0x3044` 帧；回环回归确认每个预设除发送副本的当前会话序号外，其余字节保持不变，原始 `Socket_PacketInfo.PacketBuffer` 也不被修改。
- `ProtectedPresetSendWorkerRegression.ps1`、出售盘古精铁会话/发送回归、发送保护静态回归、Review、ClickOnce 和 `git diff --check` 均通过；实际发布目录 DLL 回归也通过。
- 已生成签名本地 ClickOnce `releases/2026.8.25.22/`，发布目录 204 个文件、88 个 `.deploy` 文件；固定入口和桌面快捷方式已切换到 `.22`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_212542.lnk`，当前预览状态 `UpToDate`。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；`UiDesignRegression.ps1 -Configuration Release` 仍因当前环境禁用 .NET `BinaryFormatter` 在窗体构造处失败，非本次发送逻辑改动引起。未启动目标游戏、未注入、未抓包、未发送真实游戏封包。

## 2026-08-25 出售盘古精铁首包掉线修复当前发布包补充（2026.8.25.21）

- `.19` 的两次实机诊断均为 26/26 字节、`wsaError=0`、`send_result success=true`；因此首包掉线发生在服务端收到封包之后，不是本地 Socket 部分写入。
- 当前发布包对精确 26 字节 `0x3044` 的“出售盘古精铁”封包复制发送缓冲，并绑定当前会话已观测到的下一序号；没有可用非零序号时在 native send 前 fail-closed，不再发送全零序号首包。
- 使用实际 `releases/2026.8.25.21/` DLL 验证 `PanguIronSaleSessionSequenceRegression.ps1`、`PanguIronSaleSendWorkerRegression.ps1`、发送保护静态回归和 ClickOnce 更新回归均通过；发布目录为 204 个文件、88 个 `.deploy` 文件。
- 固定入口和桌面 `小黑封包助手.lnk` 均指向 `.21`，快捷方式预览状态为 `UpToDate`。`UiDesignRegression.ps1` 仍受当前环境禁用 .NET `BinaryFormatter` 阻塞，未将该环境问题误判为本次发送修复失败。
- 本轮未启动目标游戏、未注入、未抓包、未发送真实游戏封包；需关闭旧窗口后双击桌面快捷方式，先只发送一次“出售盘古精铁”进行实机验收。

## 2026-08-25 相聚长安藏宝图缺 Use 时恢复跳转本地热更（2026.8.25.21）

- 根据当前运行日志 `f105c6c7...`、`2315cec9...` 与历史正常运行 `cf5f8ffa-e036-46bd-b859-837be7234129` 对比，确认本轮回归点是“当前 Jump 存在但 Use 缺失时，完整动作门槛提前拦截，导致地图完全不跳转”；不是把“一战倾城”预设改掉。
- 仅在“相聚长安”启用的动作门槛中，允许已验证的当前会话 Jump 先执行；Use 仍在发送前重新校验，缺失时记录 `use_template_not_found` 并零发送，不伪造或重发 Use。`一战倾城`未启用该门槛，逻辑保持不变；数据库预设正文未修改。
- 新增 C6 回归：本机回环当前连接下验证“有 Jump、无 Use”会发送 1 次目标 Jump，随后 Use 安全停止；`WPELibrary` Debug、C6、助手管理、编码/模板、Review、ClickOnce 和 `git diff --check` 均通过。
- 已生成签名本地 ClickOnce `releases/2026.8.25.21/`，固定入口和桌面快捷方式已切换到 `.21`；发布目录 204 个文件、88 个 `.deploy` 文件，旧发布、数据库和用户配置未清理。快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_205531.lnk`。
- `UiDesignRegression.ps1` 仍在窗体构造处受当前环境禁用 .NET `BinaryFormatter` 阻塞，非本轮改动引起。未启动目标游戏、未注入、未抓包、未发送真实游戏封包；需关闭旧窗口后双击桌面 `小黑封包助手.lnk`，只运行“相聚长安藏宝图”验收。

## 2026-08-25 出售盘古精铁首包掉线诊断与会话序号保护本地热更（2026.8.25.20）

- 从 `.19` 实机诊断 JSONL 确认两次首包均为 26/26 字节写入、`wsaError=0`、`send_result success=true`；底层 Socket 写入成功，掉线发生在服务端收到包之后，不是旧 Socket 或部分写入。
- 对比确认“出售盘古精铁”数据库正文为 26 字节 `0x3044`，包头偏移 4–7 的会话字段保存为全零；普通发送入口现在只对精确该预设校验 26 字节 `0x3044` 契约，复制发送缓冲并绑定当前会话已观测到的下一序号，不把挖宝正文套用到出售包。
- 当前会话没有观测到非零序号时，出售包在 native send 前记录 `prepare_failed:session_sequence_unavailable` 并停止，避免再次发送全零序号包；成功回环日志会标记 `reason=session_sequence_patched`，不记录包体。
- `PanguIronSaleSessionSequenceRegression.ps1`、`PanguIronSaleSendWorkerRegression.ps1`、`SendPresetTransportProtectionRegression.ps1`、编码/模板/路由/Review 回归和 `git diff --check` 通过；发布目录内 DLL 回归也通过。
- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.25.20/`；桌面快捷方式已切到 `.20` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260825_205438.lnk`，旧发布、数据库和用户配置未清理。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；`UiDesignRegression.ps1 -Configuration Release` 仍因当前环境禁用 .NET `BinaryFormatter` 在窗体构造处失败，非本轮代码构建错误。本轮未启动目标游戏、未注入、未抓包、未发送真实游戏封包，等待手动实机复测。

## 2026-08-25 出售盘古精铁借鉴相聚长安挖宝发送逻辑本地热更（2026.8.25.19）

- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.25.19/`；发布目录包含 204 个文件和 88 个 `.deploy` 文件，EXE、应用清单、部署清单和固定入口版本均为 `2026.8.25.19`。
- 桌面 `小黑封包助手.lnk` 已更新到固定入口 `releases/小黑封包助手.application` 和 `.19` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_204143.lnk`；旧发布、数据库和用户配置未清理。
- `SendPresetTransportProtectionRegression.ps1`、`ReviewRegressionChecks.ps1`、`ClickOnceUpdateRegression.ps1` 和 `git diff --check` 通过；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。`UiDesignRegression.ps1 -Configuration Release` 仍因当前环境禁用 .NET `BinaryFormatter` 在窗体构造处失败，非本轮发布构建错误。
- 本轮未启动目标游戏、未注入、未抓包、未发送真实游戏封包；当前桌面助手进程未运行。需手动双击桌面快捷方式加载 `.19` 后，再进行“出售盘古精铁”实机测试。

## 2026-08-25 出售盘古精铁借鉴相聚长安挖宝发送逻辑（源码）

- 对比确认：数据库中的“挖宝图”是固定 22 字节 `0xB0F4` 包；“出售盘古精铁”是 26 字节 `0x3044` 包，未把挖宝包体或会话序号字段套用到出售包，数据库正文保持不变。
- 仅对精确预设“出售盘古精铁”借鉴藏宝图发送器的当前会话 Socket 缓存：实时显示列表自动清空时可从当前注入会话取回仍可用的 Socket；实时连接歧义、不可用或写入失败仍立即停止，其他预设保持原语义。
- 新增不记录包体的 JSONL 诊断日志：`%LOCALAPPDATA%\XNAS\WPE\socket-send\logs\socket-send.jsonl`，包含事件、Socket、包长、实际写入字节数和 WSA 错误码，便于 CLI 区分“发送成功后服务端断开”和“底层写入失败”。
- `SendPresetTransportProtectionRegression.ps1`、`SocketRouteResolutionRegression.ps1`、藏宝图编码/模板回归、Review 回归、WPELibrary Debug 隔离构建、当前会话 Socket 缓存回环验证和 `git diff --check` 通过；本轮未生成 ClickOnce 热更、未启动目标游戏、未注入、未抓包、未发送真实游戏封包。

## 2026-08-25 藏宝图动态会话包格式修复

- 对 `9.xls`、`10.xls`、`11.xls` 的客户端出站帧做了离线对比：`0x5828`/`0x783A`/`0xF0C2` 的包头第 5--8 字节不是固定全零，而是随当前连接推进的非零字段；三份样本的 `$It2...` 尾部参数一致，不能把它当作每包随机验证码。
- 修复藏宝图协议校验：Jump/Use 校验忽略包头中的当前会话字段但保留静态契约；兼容当前客户端 `0x783A` 的零标记加帧尾 UTF-8 参数格式，同时保留离线编码器原有的一字节长度格式。
- `TreasurePacketRuntime` 在发送前只对当前会话已观察到的非零字段取下一序号写入发送副本；会话未观察到有效字段时保持零发送，路由变化会清空旧会话状态。未改变“一战倾城”等其他预设，也未向真实游戏发送封包。
- WPELibrary Debug 构建和编码、模板、会话序号、C6 四项离线回归通过；真实游戏仍需在新版本上验收，当前“下一序号”的算法是基于样本的保守推断，不等同于已解出服务端协议。

## 2026-08-25 藏宝图动态会话修复本地热更（2026.8.25.13）

- 已按本地 ClickOnce 流程发布 `2026.8.25.13`，Release 构建 0 错误，保留项目原有 4 条 `EasyHook` `MSB3178` 警告；应用清单、部署清单和固定入口均已签名。
- 发布目录为 `releases/2026.8.25.13/`，固定入口仍为 `releases/小黑封包助手.application`；桌面 `小黑封包助手.lnk` 已更新到固定入口和 `.13` 图标，旧快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_190022.lnk`。
- `ReviewRegressionChecks`、Release UI 回归、`ClickOnceUpdateRegression` 和 `git diff --check` 均通过；未启动目标游戏、未注入、未抓包、未发送真实游戏封包。旧桌面进程当前未运行，下一步可直接点击桌面快捷方式启动 `.13`。

## 2026-08-25 相聚长安藏宝图掉线止损修复与本地热更（2026.8.25.14）

- 根据现场 runId `dfa9cf82-a84e-41e0-bb7e-c72869c67a25` 复核，原生产入口在 Jump 后约 300ms 继续 Use，并在连续完成 10 张后出现 `socket_send_failed`；本次未把该模糊写入重发到新连接。
- 保护逻辑按助手名限定：`相聚长安`/`相聚长安藏宝图`（及同前缀名称）启用 Jump→Use 1800ms、下一张 1000ms、Jump 最短间隔 1800ms，并在 Socket 写入结果不确定时结束本轮；`一战倾城`保留原来的 300ms、0ms、800ms 和失败目标继续监听语义。
- 新增 C6 离线回归，证明选中止损的运行器在 `socket_send_failed` 后不跳过目标、不发送后续包；WPELibrary Debug、C6 可执行回归、助手管理、封包编码、模板修补和 Review 回归通过。
- `TreasurePacketSessionSequenceRegression.ps1` 本轮因测试进程未提供可解析的当前连接而在 `GetCurrentRoute` 前置条件处失败，未归因于本次节拍分流改动；未启动游戏、未注入、未抓包、未发送真实游戏封包。
- 已发布签名本地 ClickOnce `releases/2026.8.25.14/`；固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式已更新到固定入口和 `.14` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260825_192546.lnk`。
- Release 构建 0 个错误，保留 4 条既有 `EasyHook` `MSB3178` 警告；`ReviewRegressionChecks`、`ClickOnceUpdateRegression` 和发布产物校验通过。`UiDesignRegression` 仍受当前环境禁用 .NET `BinaryFormatter` 阻塞，非本次藏宝图改动引起。

## 2026-08-25 相聚长安藏宝图完整动作门槛（源码）

- 复核最新运行 `cc165bc6-5d8d-4b73-953e-5cae6895f835`：出现过 `jump=sent` 后 `use_template_not_found`，说明旧路径可能先发 Jump、再发现当前会话没有可验证的 Use；旧运行 `dfa9cf82-a84e-41e0-bb7e-c72869c67a25` 的 `socket_send_failed` 仍是历史掉线证据。
- 仅“相聚长安”分支启用发送前完整动作门槛，并在每个新目标开始前重新选择当前会话的 Jump+Use 或已验证 AutoDig 路径；两者都不完整时记录 `action_templates_not_ready:*` 并零发送，不再发半套动作。“一战倾城”不启用该门槛。
- 本轮 WPELibrary Debug 重建、`TreasureC6StreamRegression`、`AssistantManagementRegression`、封包编码/模板回归、`ReviewRegressionChecks` 和 `git diff --check` 通过。`TreasurePacketSessionSequenceRegression` 仍因其回环测试未提供可解析的 WS2_Send 当前连接而失败，未归因于本轮改动。
- 本轮未生成 ClickOnce 热更、未启动目标游戏、未注入、未抓包、未发送真实游戏封包；源码验证通过后仍需单独热更，才能在桌面端验收。

## 2026-08-25 出售盘古精铁普通发送预设掉线止损（源码）

- 已确认数据库中的普通发送预设“出售盘古精铁”包含 1 个 `WS2_Send` 封包；未修改数据库中的封包正文、Socket 或地址快照。
- 仅当预设名称精确为“出售盘古精铁”时，发送线程才会在每次发送前重新解析当前连接；当前连接缺失/歧义、封包无效或 Socket 写入失败时立即停止，不继续发送或重发，并将运行时最小间隔限制为 1800ms。其他普通发送预设保持原语义。
- `SendPresetTransportProtectionRegression.ps1`、`SocketRouteResolutionRegression.ps1`、`ReviewRegressionChecks.ps1`、WPELibrary Debug 重建和 `git diff --check` 均通过；本次未生成 ClickOnce 热更、未启动目标游戏、未执行真实抓包或封包发送。

## 2026-08-25 出售盘古精铁掉线保护本地热更（2026.8.25.15）

- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.25.15/`；发布目录 100 个文件，固定入口 `releases/小黑封包助手.application` 已更新到该版本。
- 桌面 `小黑封包助手.lnk` 已切换到固定入口和 `.15` 图标，更新前快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_194821.lnk`，更新后预览状态为 `UpToDate`。旧版本、数据库和用户配置未清理。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；`SendPresetTransportProtectionRegression.ps1`、Release `SocketRouteResolutionRegression.ps1`、`ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。
- 本次只热更“出售盘古精铁”的发送保护；未修改“一战倾城”、`相聚长安` 或数据库封包正文，未启动目标游戏、未注入、未抓包、未发送真实游戏封包。需关闭旧桌面窗口后，双击桌面 `小黑封包助手.lnk` 加载 `.15`。

## 2026-08-25 相聚长安藏宝图完整动作门槛本地热更（2026.8.25.16）

- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.25.16/`；主程序文件版本为 `2026.8.25.16`，固定入口 `releases/小黑封包助手.application` 已更新。
- 桌面 `小黑封包助手.lnk` 已切换到固定入口和 `.16` 图标，更新前快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_200301.lnk`，更新后预览状态为 `UpToDate`；旧版本、数据库和用户配置未清理。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；C6、助手管理、封包编码/模板、Review、发送保护、ClickOnce 和发布版 UI 回归通过，`git diff --check` 通过。`TreasurePacketSessionSequenceRegression` 仍因回环测试未提供可解析的 WS2_Send 当前连接而失败，未归因于本次改动。
- 本版本只启用“相聚长安”发送前完整动作门槛和当前会话路径重选；“一战倾城”保持原逻辑。未启动目标游戏、未注入、未抓包、未发送真实游戏封包；需关闭旧窗口后双击桌面 `小黑封包助手.lnk`，再只运行“相聚长安藏宝图”进行实机验收。

## 2026-08-25 出售盘古精铁旧发送入口保护补漏本地热更（2026.8.25.17）

- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.25.17/`；发布目录 100 个文件、36 个部署文件，固定入口 `releases/小黑封包助手.application` 已更新。
- 桌面 `小黑封包助手.lnk` 已切换到固定入口和 `.17` 图标，更新前快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_200834.lnk`；旧版本、数据库和用户配置未清理。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；`SendPresetTransportProtectionRegression.ps1`、`ReviewRegressionChecks.ps1`、`ClickOnceUpdateRegression.ps1` 和 `git diff --check` 通过。`UiDesignRegression.ps1 -Configuration Release` 仍受当前环境禁用 .NET `BinaryFormatter` 阻塞，非本次改动引起。
- 本版本包含“出售盘古精铁”旧发送入口的统一保护补漏；未启动目标游戏、未注入、未抓包、未发送真实游戏封包。当前桌面助手进程未运行，下一步可双击桌面 `小黑封包助手.lnk` 加载 `.17` 后进行实机验收。

## 2026-08-25 相聚长安第 9 张 Use 旧 Socket 回退修复本地热更（2026.8.25.18）

- 现场 runId `cf5f8ffa-e036-46bd-b859-837be7234129` 在本地 20:12:50 的第 9 张 Use 出现 `socket_send_failed`；前 8 张已 `consume_confirmed`，该次 Use 日志显示 `current_template`，不是模板缺失。
- 仅“相聚长安”启用动作类型对应的当前会话缓存 Socket 回退：Use 优先 `sessionUseSocket`，Jump 优先 `sessionJumpSocket`；其他预设继续使用原通用回退顺序。“一战倾城”没有启用该分支。
- 失败日志现在追加 Socket、`bytes_sent`、WSA 错误码和写入 disposition，便于区分连接先关闭、部分写入或其他底层原因；未修改数据库封包正文。
- WPELibrary Debug、TreasureC6StreamRegression、AssistantManagement、封包编码/模板、Review、Release UI、ClickOnce 回归和 `git diff --check` 通过；`SendPresetTransportProtectionRegression.ps1` 仍因其既有字符串断言与源码字面量不一致而失败，未归因于本次改动。
- 已生成签名本地 ClickOnce `releases/2026.8.25.18/`，固定入口和桌面快捷方式已更新到 `.18`；快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_202811.lnk`，旧发布、数据库和用户配置未清理。
- 未启动目标游戏、未注入、未抓包、未发送真实游戏封包；需关闭旧窗口后双击桌面 `小黑封包助手.lnk`，只运行“相聚长安藏宝图”进行实机验收。未来失败日志应关注 `socket=...;bytes_sent=...;wsa_error=...;disposition=...`。

## 2026-08-25 出售盘古精铁旧发送入口保护补漏（源码）

- 修复预设详情执行窗口仍直接调用旧版 `StartSendWithPacketSockets`、绕过 `.15` 保护的问题；现在所有底层发送入口按精确预设名统一启用当前连接重解析、1800ms 最小间隔和写入失败即停止。
- 保护发送线程先刷新当前连接再检查旧 Socket 快照，避免旧入口因快照为空而直接失败或继续使用旧连接；未修改数据库中的 26 字节 `WS2_Send` 封包正文。
- `SendPresetTransportProtectionRegression.ps1`、`SocketRouteResolutionRegression.ps1`、`ReviewRegressionChecks.ps1`、WPELibrary/主程序 Debug 隔离构建和 `git diff --check` 通过；`UiDesignRegression.ps1` 仍受当前环境禁用 .NET `BinaryFormatter` 阻塞，未启动目标游戏、未注入、未抓包、未发送真实游戏封包。

## 2026-08-25 技能槽解码与原始状态证据增强

- `PetSkillBookReadOnlyStateAdapter` 现在显式保留运行时正整数技能 ID 候选，并通过本地目录标记“目录已确认”或“目录未收录”；不把未收录的运行时数值直接当作已确认的游戏技能。
- 技能值和锁值分别统计 `nil`、`0`、负哨兵、正值和其他原始类别；只读界面与启动提示显示 17 槽总数、正值候选、目录确认数和分类计数，并明确标注开槽/锁槽业务语义未确认。
- 当前现场只读复验仍为 `petId=600000591`、17 个槽位：技能值 `nil=8`、`0=7`、负哨兵 `=1`、正值候选 `=1`；锁值 `nil=8`、`0=7`、正值 `=2`。唯一正值技能候选为运行时整数 `83036`，当前本地技能目录未收录，不能映射为用户目录中的 `920xx/921xx/922xx` 技能。
- 当前现场技能书库存仍可读到 34 种；其中 `92133` 数量为 `3`、`92211` 数量为 `2`。本次仅增强只读解析和展示，未写内存、未注入、未控制电脑、未发送封包，真实操作适配器继续 fail-closed。
- `WPELibrary` 隔离 Debug 构建、快照协议/状态/UI/库存/目录/持久化/探针源码回归、审查检查和 `git diff --check` 均通过。
- 已发布签名本地 ClickOnce `releases/2026.8.25.5/`；固定入口和桌面 `小黑封包助手.lnk` 已更新到 `.5`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_013949.lnk`。Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；发布版 UI、ClickOnce 清单和依赖检查均通过。

## 2026-08-25 坐骑直接运行入口修复与本地 ClickOnce 热更新

- 修复 `Socket_Cache.DoRobotAsync` 直接运行助手时未传递 `MountSpeedPreset` 和 `MountStatusAndroidSnapshotReader` 的缺口；坐骑预设现在与编辑器入口统一使用本次运行专用的 Android LuaJIT 只读读取器，不再误回退到未配置的 Windows `MountStatusMemoryLayout`。
- 已发布签名本地 ClickOnce `releases/2026.8.25.4/`；旧的 `.0`–`.3` 发布目录、数据库和用户配置未清理，固定入口和桌面快捷方式已切到 `.4`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_012512.lnk`。
- Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告；助手管理回归、坐骑运行器、发布版 UI、ClickOnce 清单/依赖审计和 `git diff --check` 均通过。
- 未启动目标游戏，未执行真实注入、抓包、封包发送或坐骑操作。

## 2026-08-25 召唤兽技能只读启动流程修复与本地热更新

- 修复主界面启动召唤兽技能只读预演后“点确定无后续”的流程：当结果明确为“未加载召唤兽技能书配置”时，关闭提示框会自动打开对应助手编辑页，便于继续配置；仍不执行开格、学习、锁格、写内存或发包。
- 根因是内置助手包含执行模板但没有已保存的技能书目标配置；因此之前只能完成只读读取并弹出结果提示。未凭空生成技能书目标，避免把用户未确认的技能当成预设。
- 已发布签名本地 ClickOnce `releases/2026.8.25.3/`，包含 204 个文件和 88 个 `.deploy` 文件；固定入口与桌面 `小黑封包助手.lnk` 均已更新到 `2026.8.25.3`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_011401.lnk`。
- Release 构建 0 个错误，保留 4 条既有 EasyHook 清单 `MSB3178` 警告；`PetSkillBookUiPreflightRegression.ps1`、`ReviewRegressionChecks.ps1`、指向新发布目录的 Release `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。未启动目标游戏、未执行真实注入、抓包或封包发送。

## 2026-08-25 本地 ClickOnce 热更新（坐骑目标技能页）

- 已将坐骑目标技能编辑页及只读预演发布为签名本地 ClickOnce `releases/2026.8.25.2/`；发布目录包含 204 个文件和 88 个 `.deploy` 文件，坐骑 LuaJIT 探针及 `ride_skill_name_map.json` 均已打包。
- 固定入口为 `releases/小黑封包助手.application`，应用清单、部署清单和固定入口版本均为 `2026.8.25.2`；桌面 `小黑封包助手.lnk` 已备份为 `releases/shortcut-backups/小黑封包助手_20260825_011109.lnk`，并更新到 `.2` 图标。
- Release 构建 0 个错误，保留 4 条既有 EasyHook 清单 `MSB3178` 警告；`ReviewRegressionChecks.ps1`、指向新发布目录的 Release `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1`、发布依赖审计和 `git diff --check` 均通过。
- 本次只完成本地构建、签名、清单、依赖和桌面入口更新，未启动目标游戏、未执行真实注入、抓包或封包发送；旧发布目录、数据库和用户配置未清理。

## 2026-08-25 本地 ClickOnce 热更新

- 已将包含召唤兽技能书只读预演闭环的桌面端发布为签名本地 ClickOnce `releases/2026.8.25.0/`；版本源、应用清单和固定入口均为 `2026.8.25.0`，发布目录包含 202 个文件，应用 EXE、`WPELibrary.dll` 和 HexBox 依赖均存在。
- 固定入口为 `releases/小黑封包助手.application`；桌面 `小黑封包助手.lnk` 已备份为 `releases/shortcut-backups/小黑封包助手_20260825_005047.lnk`，并更新到 `2026.8.25.0` 图标。发布时没有正在运行的旧桌面进程，使用新版本时仍应从桌面快捷方式启动。
- Release 构建 0 个错误，保留 4 条既有 `EasyHook` 清单 `MSB3178` 警告；`ReviewRegressionChecks.ps1`、指向发布目录的 Release `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。
- 本次仅完成本地桌面构建、清单和入口更新，未启动目标游戏、未执行真实注入、抓包或封包发送；旧发布目录、数据库和用户配置未清理。

## 2026-08-25 坐骑预设启动前刷新当前坐骑与技能

- 坐骑预设启动时由 `Socket_Robot.StartRobot` 清除上一轮的坐骑只读快照，并通过本次运行专用的 Android LuaJIT 读取器重新读取当前坐骑、当前实例和技能名称；读取失败、骑乘实例无法唯一绑定或技能列表为空时拒绝启动。
- `IDLE` 步骤只使用本次启动预检生成的快照，不再重复读取或沿用上一轮数据；只读状态和日志显示本次读取到的坐骑编号、实例和技能名称。仍不写内存、不发包、不执行坐骑操作。

## 2026-08-25 坐骑目标技能只读预演

- `MountSpeedPreset.TargetSkills` 现在支持按技能 ID、名称或两者配置目标技能；该字段作为可选 JSON 属性进入现有坐骑预设序列化/SQLite 链路，不需要旧数据迁移。
- 每次助手启动会把已保存的坐骑预设复制到本次运行参数，并将目标技能与刚读取的当前 `LocalRide` 技能逐项比较，输出 `已满足`、`缺少` 或 `无法判断` 的只读预演明细。
- `Socket_RobotForm` 新增“坐骑目标”动态编辑页：支持预设名称、目标技能 ID/名称的添加或更新、删除、从助手读取、应用保存，以及刷新当前坐骑只读快照并显示预演结果。
- 当前阶段不会因为目标缺少而执行洗炼，也不会写内存、发包或调用游戏操作；保存失败会恢复原坐骑预设。

## 2026-08-25 技能书库存只读读取接入

- `pet_skill_snapshot.v1` 新增可选 `skillBookInventory` 字段；Android 只读探针先确认唯一 `BagMgr.m_ItemDict`，再对技能目录范围内的 `m_ItemTypeId` 校验 `m_LogicType=9`，从 `PropertyValueDict.num` 的 boxed scalar 读取数量。`packageNum` 只作为背包位置，不当作数量；读取不完整时省略可选字段并保留宠物技能快照。
- 当前现场 PID `1855` 输出 17 个技能槽和 34 条技能书库存；用户已手工对照界面确认技能 `92133` 数量为 `3`、技能 `92211` 数量为 `2`。桌面状态层现在支持按技能 ID查询数量，并在只读预演/快照区域显示 `skillId/count`；原有通用 `inventoryItems` 兼容字段仍保留。
- 已将新静态只读探针部署到模拟器 `/data/local/tmp/pet-skill-probe`，旧正式文件保留为 `/data/local/tmp/pet-skill-probe.before-skill-inventory-20260825`；部署后现场 JSON 复验通过。未使用 OCR、截图或电脑控制，未写内存、注入、调用游戏方法或发送封包。
- 桌面执行入口现在把当前技能书预设传入启动前只读预演；预演逐本判断目标技能是否已存在、技能书数量是否已读取及是否缺少，并回显到技能快照区域和资源摘要。纯只读启动不创建 Worker，完成后恢复执行控件；真实操作适配器仍保持 fail-closed。
- `PetSkillProbeSourceRegression.ps1`、`PetSkillBookUiPreflightRegression.ps1`、`PetSkillBookInventoryPreflightRegression.ps1`、快照/状态/目录/预设/持久化/Runner/助手管理/审查回归及 WPELibrary Debug 隔离构建均通过。

## 2026-08-25 坐骑 LocalRide 实例绑定只读验收

- 已确认当前角色 `m_RideId=9111` 与 `LocalRide` 实例 `2091811378351566852` 的唯一关系：该实例的 `PropertyValueDict.SHAPE=9111` 且 `PropertyValueDict.RIDEING=1`；其余三个实例的 shape 为 `9121/9131/9141`。
- 探针现在为实例输出可选 `rideShapeId`、`isRiding`、`isCurrent` 和 `bindingSource`，并在唯一证据成立时输出 `activeRideInstanceId`、`rideBindingStatus=bound`、`rideBindingSource=LocalRide.PropertyValueDict.SHAPE+RIDEING`；shape 缺失、重复或未处于骑乘状态时继续返回未绑定/歧义，不猜测当前技能。
- 当前现场复读（PID `1854`、启动 tick `1497`）成功解析 4 个实例、每个 3 个技能；当前实例 4 个技能名为 `秋水流弦`、`神枢鬼藏`、`中级坚壁清野`，其余实例同样补齐名称。
- LuaJIT 表节点扩容后字段跨度实测约 `0x5E8`，探针默认本地角色候选距离调整为 `0x2000`；引用扫描增加 8 字节节点对齐过滤，避免扩展字段名后把非节点字节误识别为引用。
- Python 编译、WPELibrary Release 隔离构建、MountSpeedRunnerHarness 7 项回归和 rooted-ADB 只读绑定复读通过；全链路仍不写内存、不注入、不暂停、不调用游戏方法、不发包。

## 2026-08-24 坐骑 LuaJIT Android 只读快照接入

- 已确认目标 Android 游戏进程的坐骑字段位于 LuaJIT GC64 表节点，不是可直接固化的原生结构体偏移；同一 PID `1853`/启动 tick `1466` 的真实只读前后样本已完成：未骑乘时 `m_RoleMoveSpeed=200`、`m_RideId=nil`，上马后唯一本地角色 `playerId=256` 变为 `m_RoleMoveSpeed=256`、`m_RideId=9111`、`isMounted=true`，标准探针复读通过。
- 新增 `tools/mount-reader/mount_status_luajit_probe.py`：通过 rooted ADB 有界读取匿名可读写映射，输出 `mount_status_snapshot.v1`；探针不写内存、不注入、不暂停进程、不调用游戏方法、不发包，并绑定 PID/启动 tick。
- 新增 `MountStatusReadOnlySnapshotProtocol` 与 `MountStatusAndroidSnapshotReader`；桌面 C# 桥接已现场读取并校验同一快照。`Socket_Robot` 在显式注入 `MountStatusAndroidSnapshotReader` 时优先走 Android LuaJIT 路径，未注入时保留 Windows 显式布局路径并继续 fail-closed。
- 已扩展探针读取 `LocalRide.m_Rideskills`：真实复测可解析 4 个坐骑实例、每个 3 个技能槽，字段为 `slotIndex`、`skillId`、`exp`；实例绑定证据在后续只读验收中通过 `PropertyValueDict.SHAPE` 与 `RIDEING` 补齐。
- 新增 `tools/mount-reader/ride_skill_name_map.json`：从 `data_RideSkill` LuaJIT 字节码的 213 条注册记录中按 `skillId` 与 `name` 精确配对，覆盖 213 条技能、其中 71 条为高级技能；不使用会在不同等级间复用的 `icon` 字段。
- `MountStatusReadOnlySnapshot` 现以可选 `RideInstances`/`SkillName` 接收名称；当前现场输出已验证 `62022=澧兰沅芷`、`62122=高级澧兰沅芷`、`62407=中级兰质蕙心`，旧的无名称快照仍可解析。
- 高级技能名称的 UTF-8 字节由名称自动生成并校正，用户清单 71/71 命中；手抄串中的错误字节不进入运行时映射。
- WPELibrary Release 隔离构建、MountSpeedRunnerHarness 7 项回归、Python 探针编译/映射表检查及真实 rooted-ADB 探针读取均通过；动态状态切换已验证，读取链路仍保持只读、唯一候选和进程身份绑定。

## 2026-08-24 参战宠物切换只读匹配修复

- 根因已确认：Android 探针 JSON 模式原先分别读取进程中第一个 `m_PetId`、`m_Petskills` 和 `m_PetskillsLock`，切换参战宠物后会把旧宠物技能数组与新 `m_CurFightPetId` 拼在一起；桌面严格协议因此正确拒绝快照。
- 已修复探针：先读取当前参战宠物 ID，再枚举 `m_Pets` 容器中的宠物对象，按对象 `m_PetId` 匹配，并要求唯一且成对的技能/锁槽数组；目标不存在或重复时继续 fail-closed。不增加 OCR、写内存、输入、注入或发包能力。
- 使用 Android NDK 重新编译 ARM64 动态 PIE 探针，修正 Bionic TLS 对齐后已部署到模拟器 `/data/local/tmp/pet-skill-probe`；旧设备探针已备份到 `work/PetSkillProbe/pet-skill-probe-device-backup-20260824`。
- 现场只读验证通过：PID `1853`，`petId=currentPetId=600000715`，17 个技能槽；槽 2 原始技能值 `83039`、锁值 `3`，槽 14 原始技能值 `-1`、锁值 `4`。未使用 OCR、截图或执行任何游戏操作。
- 切换参战宠物后再次只读验证通过：`petId=currentPetId=600000591`，17 个技能槽；槽 2 变为技能 `83036`、锁值 `3`，槽 10 变为技能 `-1`、锁值 `5`，桌面 C# 桥接同样返回成功，确认没有跨宠物串读。

## 2026-08-24 召唤兽技能只读预演状态层

- 新增 `PetSkillBookReadOnlyStateAdapter`：将严格校验后的 Android 原始技能/锁槽快照转换为只读规划状态，保留 `nil`、`0`、`-1`、正技能 ID及原始锁值，不猜测开放/锁定语义；同时生成基于进程身份和槽位原始值的本地状态指纹。
- `Socket_Robot` 的召唤兽技能只读观察路径现在保存规范化状态，并输出预设目标技能的只读预演结果；仍不会启动普通业务 Worker，不会开格、打书、锁格、写内存、控制电脑或发送封包。
- 现场 C# 桥接验证通过：当前宠物 `600000715`、17 个槽位、13 个正技能 ID；槽 15–17 仍为 `nil`。这只作为读取验证，不推断最大可用槽位。
- `PetSkillBookReadOnlyStateRegression.ps1`、快照协议、预设、持久化、Runner、助手管理、只读内存回归及隔离 `WPELibrary` Debug 构建均通过；背包技能书读取和真实操作适配器仍未接入，继续保持 fail-closed。

## 2026-08-24 技能书 ID 目录与预设校验

- 新增 `PetSkillBookCatalog`：录入用户提供的 114 个技能条目（29 个普通、84 个高级、1 个源码异常项）和 9 个技能书/礼包物品条目；每项保留十进制 ID、十六进制 ID、名称、等级，礼包额外标记 `IsBundle`。
- `SummonedPetSkillBookPreset.IsValid` 现在会校验目录中已知 `SkillId` 与已知 `ItemId` 的等级匹配；例如普通技能不能绑定高级/终极技能书。任一侧尚未收录的 ID 仍保持兼容，不将当前目录误当成完整游戏数据库。
- 只读预演日志现在显示已知技能名和技能书名；目录只用于本地校验与展示，不把目录当作背包库存，也不执行开格、学习、锁格、写内存或发包。
- `PetSkillBookCatalogRegression.ps1`、技能书预设/持久化/Runner/只读状态/快照协议回归及 `WPELibrary` Debug 隔离构建均通过，0 警告、0 错误。

## 2026-08-24 技能书库存只读预演与操作边界

- `pet_skill_snapshot.v1` 增加可选 `inventoryItems` 字段：每个条目严格校验正数 `itemId`、非负 `count`、唯一物品 ID和最多 4096 条；字段缺失表示当前探针未读取背包，空数组表示已读取但为空，兼容旧版只返回宠物技能/锁槽的探针。
- `PetSkillBookReadOnlyStateAdapter` 复制库存快照并提供按 `ItemId` 查询数量；预设只读预演会显示“背包数量=N/缺少该技能书”，库存字段缺失时仍明确显示“背包数量尚未读取”。库存也参与本地 `StateFingerprint`，避免把物品变化误当成同一状态。
- `Socket_RobotForm` 的只读快照区域会展示物品数量；在该阶段已部署的 `PetSkillProbe` 尚未输出 `inventoryItems`，因为当时的 `BagMgr.m_ItemDict` 材料只确认物品对象/ID线索，没有确认技能书堆叠数量字段，未猜测偏移或把 `packageNum` 冒充数量。后续已由 `skillBookInventory` 只读字段补充技能 ID 维度的数量。
- 新增 `FailClosedPetSkillBookOperationAdapter`：开格、学习、锁格三类调用统一返回 `OperationResult.Unavailable`，记录拒绝原因但不访问 socket、进程内存或游戏状态；真实操作适配器仍未接入。
- `PetSkillBookInventoryPreflightRegression.ps1` 与快照/只读状态/预设/Runner 回归通过；本轮未启动游戏、未控制电脑、未写内存、未发送封包。

## 2026-08-24 坐骑状态专用只读内存读取器

- 新增 `MountStatusMemoryReader`、`MountStatusMemoryLayout` 和 `MountStatusReadResult`：按显式地址读取骑乘标志及已确认的可选坐骑字段，绑定目标进程 ID/启动时间，并为每次读取生成会话 ID 与序号。
- 未配置布局、必需骑乘标志读取失败或进程身份变化时直接 fail-closed；合法的坐骑 ID `0`、骑乘标志 `false` 和未配置的可选字段不会被伪装成异常数据。读取器不做内存扫描、不猜偏移、不写内存、不发包。
- `Socket_Robot` 的坐骑预设入口已改为在 `IDLE` 步骤建立只读快照，后续步骤仅复用有效快照；Android LuaJIT 运行时路径已完成真实目标进程验收，Windows 显式布局路径仍需单独配置已确认地址。
- 隔离 `WPELibrary` Release 构建和 `MountSpeedRunnerHarness` 6 项回归均通过；旧的坐骑内存写入类未接入本读取链路。

## 2026-08-24 只读探针权限 fallback 热更新

- 已生成并签名本地 ClickOnce `releases/2026.8.24.4/`；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 清单警告。该版本包含只读探针权限 fallback：普通 ADB shell 遇进程内存权限错误时改用 `su -c` 重试同一条只读 JSON 命令。
- 发布目录包含 200 个文件和 86 个 `.deploy` 文件；应用清单、部署清单和固定入口版本均为 `2026.8.24.4`，固定入口为 `releases/小黑封包助手.application`。
- 桌面 `小黑封包助手.lnk` 已备份为 `releases/shortcut-backups/小黑封包助手_20260824_210435.lnk` 并更新到 `.4` 图标；旧发布目录、数据库和用户配置未清理。
- `ReviewRegressionChecks.ps1`、指向发布目录的 Release `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。未替用户关闭或启动程序；未执行真实目标注入、抓包或封包发送。

## 2026-08-24 只读探针权限 fallback 修复

- 现场复现新版本启动失败：设备 `adb shell` 为普通 `shell` 身份，探针直接执行返回 `open_readonly_failed=proc_mem_open_failed`；设备 `su` 可用，问题是读取 `/proc/<pid>/mem` 的权限，不是 OCR、包名或探针路径丢失。
- `PetSkillBookAndroidSnapshotReader` 现在先执行普通只读命令；仅在探针报告进程内存权限错误时，使用 `su -c` 重试同一条 `--json` 只读命令。root 不可用时仍 fail-closed，并合并展示两次失败原因；不增加写内存、注入、输入或发包能力。
- 新版隔离读取器现场验证通过：当前 PID `1861`、宠物 ID `600000591`、17 个技能槽；未使用 OCR、截图或执行任何游戏操作。`WPELibrary`/主程序隔离 Debug 构建、宠物快照协议、助手管理、预设、运行器回归和 `git diff --check` 均通过。
- 当时正式 ClickOnce 仍为 `2026.8.24.3`；后续已通过本地热更新发布包含本修复的 `2026.8.24.4`。

## 2026-08-24 本地 ClickOnce 热更新

- 已使用项目自带发布流程生成并签名 `releases/2026.8.24.3/`；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 清单警告。
- 发布目录包含 200 个文件和 86 个 `.deploy` 文件；应用清单、部署清单和固定入口版本均为 `2026.8.24.3`，固定入口为 `releases/小黑封包助手.application`。
- 桌面 `小黑封包助手.lnk` 已保留固定入口并更新到 `2026.8.24.3` 图标，旧快捷方式已备份；旧发布目录、数据库和用户配置未清理。
- `ReviewRegressionChecks.ps1`、发布版 `UiDesignRegression.ps1`、`ClickOnceUpdateRegression.ps1` 和 `git diff --check` 均通过。未替用户关闭或启动程序；应用切换需手动关闭旧实例后点击桌面快捷方式。此次仅完成构建、发布和清单校验，未执行真实目标注入、抓包或封包发送。

## 2026-08-24 助手启动前读取宠物技能情况

- 召唤兽技能助手启动前已接入现有 `PetSkillBookAndroidSnapshotReader` 只读快照读取器：先确认当前宠物 ID、技能槽数量和已填充技能槽，再进入助手执行链；指定宠物模式会校验宠物 ID 一致性。
- 该前置只读取当前宠物技能原始值，不使用 OCR、截图、写内存、注入或发包；开格、学习和锁格业务适配器未接入时仍保持 fail-closed，并显示具体原因。
- 纯召唤兽技能预设在只读快照成功后进入一次性的观察态：不再启动普通业务 Worker，也不把“业务适配器未接入”显示为启动失败；桌面助手改为显示只读快照成功、宠物/技能摘要和未执行游戏操作的提示。快照读取失败、宠物不一致或预设校验失败仍会阻止启动。
- 视觉助手改为按步骤条件懒加载：模板/颜色/原生窗口截图流程不创建 OCR 运行时，只有文字或数字条件才创建 OCR；Airtest 仅按需创建 Python Worker 捕获提供器。
- 本轮隔离 Debug 构建、主程序构建、相关回归和真实 ADB `StartRobot` 只读冒烟通过；已将只读静态探针部署到雷电模拟器 `/data/local/tmp/pet-skill-probe`，以 root ADB 对当前游戏进程 PID `1862` 读取成功：当前宠物 ID 为 `600000591`，技能槽 2 原始技能值为 `83036`，其余可解码技能槽为 `0`，槽 10 为 `-1`；锁槽原始值仅记录为槽 2=`3`、槽 10=`5`，不解释为业务锁状态。此次验证未使用 OCR、截图、写内存、注入或发包。
- 截图所示桌面窗口仍是本地 ClickOnce `2026.8.24.2` 旧包；本次修复已生成隔离可执行文件 `WinsockPacketEditor\bin\AssistantVisionMemoryValidation\小黑封包助手.exe`，未覆盖旧发布目录或桌面快捷方式。

## 已完成

- 增加 `ReadOnlyProcessIdentity` 和 `ReadOnlyProcessMemoryReader` 只读内存基础层：显式地址读取、进程身份复核、指针宽度校验和释放状态保护均已实现；当前未接入未经确认的游戏对象偏移。
- 增加内置助手预设 `召唤兽技能`：使用 `SummonedPetSkillBookPresetPlan` 持久化 V2 的 15 个步骤，启动阶段一次开满技能格，不做材料、银两或技能目录预检；每本书只允许写入空、开放、未锁定槽位，成功后立即锁定，默认禁用并通过现有 RobotInstruction SQLite/XML 链路保存。
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
- `SummonedPetSkillBookPresetRegression.ps1`、`SummonedPetSkillBookRunnerRegression.ps1` 和 `PetSkillBookPersistenceRegression.ps1` 均通过；覆盖 V1→V2 模板、15 步指令、fail-closed 生产入口、预设强制规则、结果模型和本地持久化。
- `PetSkillBookRunnerHarness` 已在 `Stage2Validation` 隔离配置编译并运行通过 18 个场景：一次开满、已存在技能（含已锁定）跳过、空槽限制、失败后继续、最终 FAILED、立即锁定、覆盖检测，以及材料/银两/目录读取次数为 0。
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

## 2026-08-24 召唤兽技能助手 V2 收口

- 当前内置模板为 15 步 V2：移除独立 `CHECK_MATERIALS` 步骤，保留 V1 的 16 步解码和迁移识别能力。
- 运行规则已固定：启动时一次开满技能格；不读取背包、银两或技能目录做前置判断；目标技能已存在时无论锁定状态都跳过；只允许空、开放、未锁定槽位；学习成功后立即锁定。
- 单本学习、差异校验或锁定失败会记录 `BookExecutionResult` 后继续下一本；只要有一本失败，整体状态为 `FAILED`。进程、召唤兽、开格和状态一致性等全局失败直接终止，不等待人工确认。
- 生产 `Socket_Robot` 入口仍保持 fail-closed；离线 Runner 只通过注入适配器和测试共享状态验证，未接入真实游戏读取、UI 业务动作或封包发送。
- 验证通过：`WPELibrary.csproj` 隔离 Debug 重建、`PetSkillBookRunnerHarness` 18 场景运行、三个召唤兽技能回归脚本；构建输出位于 `WPELibrary\bin\SummonedPetSkillBookValidation` 和 Harness `Stage2Validation` 隔离目录。

## 2026-08-24 召唤兽技能助手桌面热更新

- 已使用项目现有 `tools/Publish-ClickOnceStable.ps1` 生成签名本地 ClickOnce `releases/2026.8.24.0/`；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告。
- 主程序、WPELibrary、应用清单、部署清单和固定入口版本已同步为 `2026.8.24.0`；ClickOnce 清单签名和文件/依赖摘要校验通过。
- 桌面 `小黑封包助手.lnk` 已备份到 `releases/shortcut-backups/小黑封包助手_20260824_170619.lnk`，并更新为固定入口 `releases/小黑封包助手.application` 与 `2026.8.24.0` 图标；快捷方式复核状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、Release `UiDesignRegression.ps1`（指向 `releases/2026.8.24.0`）、`ClickOnceUpdateRegression.ps1`、召唤兽技能预设/运行器回归和 `git diff --check` 均通过。未结束运行中的旧进程；若旧窗口仍开着，需关闭后重新点击桌面快捷方式加载新版本。
- 本次仍未启动游戏、注入、抓包或发送真实游戏封包；该版本是签名本地发布包，不代表公网发布包。

## 2026-08-24 召唤兽助手启动失败提示修复热更新

- 修复助手启动失败只显示通用弹窗的问题：`Socket_Robot`、`Socket_Cache.Robot` 和助手 UI 现在传递并显示具体失败原因；召唤兽技能模板在启动前明确提示“真实游戏适配器尚未接入，当前仅支持离线验证”，继续保持 fail-closed。
- 已生成签名本地 ClickOnce `releases/2026.8.24.1/`；Release 构建 0 个错误，保留 4 条既有 EasyHook `MSB3178` 警告，主程序、WPELibrary、清单和固定入口版本均为 `2026.8.24.1`。
- 桌面 `小黑封包助手.lnk` 已备份到 `releases/shortcut-backups/小黑封包助手_20260824_172030.lnk`，并更新到固定入口和 `.1` 图标；快捷方式复核状态为 `UpToDate`。
- Debug 解决方案构建、召唤兽技能预设/运行器/持久化回归、助手管理、Review、Release UI、ClickOnce 回归和 `git diff --check` 均通过。未启动游戏、未注入、未抓包或发送真实游戏封包。

## 2026-08-24 召唤兽技能只读快照接入

- 增加 `pet_skill_snapshot.v1` 严格 JSON 契约、`PetSkillBookAndroidSnapshotReader` ADB 只读桥接器，以及召唤兽技能页的“真实客户端快照（只读）”展示区；展示只保留技能/锁槽原始值，不推断锁状态。
- 当前真实联调通过：Android 包名 `com.gdoo.yzqcxy`，本次 PID `1862`，当前宠物 ID `600000591`，17 个技能槽，槽位 2 技能值 `83036`、原始锁值 `3`；快照固定为 `readOnly=true`、`actionAuthorized=false`。
- 已修复同名 `m_PetId` 字段存在 `f64` 镜像值时 JSON 探针误选的问题：探针现在按协议要求选择 `i32` 宠物 ID；桌面桥接器和 UI 版本重新联调通过。
- 验证通过：`WPELibrary.csproj` 隔离 Debug 构建、`WinsockPacketEditor.csproj` 隔离 Debug 构建、`PetSkillBookSnapshotProtocolRegression.ps1` 和真实 ADB 只读桥接联调。主程序构建仍保留 4 个既有 `EasyHook` 清单警告。
- 当前仍未接入正式随发布包部署的 Android 探针、`PetMgr.m_Pets` 全量枚举、技能目录或锁值业务语义；生产 Runner 继续 fail-closed，不执行写内存、注入、发包或自动操作。

## 2026-08-24 召唤兽技能只读快照热更新

- 已生成签名本地 ClickOnce `releases/2026.8.24.2/`；Release 构建 0 个错误，保留 4 条既有 `EasyHook` 清单警告，主程序、WPELibrary、应用清单、部署清单和固定入口版本均为 `2026.8.24.2`。
- 桌面 `小黑封包助手.lnk` 已备份到 `releases/shortcut-backups/小黑封包助手_20260824_191559.lnk`，并更新到固定入口 `releases/小黑封包助手.application` 与 `.2` 图标；最终快捷方式复核状态为 `UpToDate`。
- `ReviewRegressionChecks.ps1`、`ClickOnceUpdateRegression.ps1`、发布目录必需文件/清单摘要核对、Release `UiDesignRegression.ps1`（指向 `releases/2026.8.24.2`）、发布版 `PetSkillBookSnapshotProtocolRegression.ps1` 和 `git diff --check` 均通过。
- 本地 ClickOnce 包不包含 Android 临时只读探针；探针仍需通过 `WPE_PET_SKILL_READER_PATH` 指向设备上的只读读取器。未结束运行中的旧进程；使用新版本前需关闭旧窗口，再点击桌面快捷方式。

## 尚未验收

- 尚未取得目标游戏运行时的完整版本化业务布局证据，因此召唤兽业务操作状态和业务锁状态尚未接入；技能书目录与按技能 ID的只读库存已接入，本轮也完成了当前进程的受限只读技能槽快照读取。
- 当前未把 APK 内的模型复制进项目，也未在没有许可证确认的情况下下载第三方模型。
- 尚未使用真实游戏截图验证 DBNet/CRNN 模型的准确率、延迟和中文字符集效果。
- 尚未进行真实目标窗口的业务结果验收；本次实现没有启动游戏、写入进程、注入、抓包或发送网络数据。
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

## 2026-08-24 装备炼化纯发包链路收口（结构样本已收到，业务映射待验收）

- 装备炼化已改为只读 Android C6 resident JSON + 已验收 rawFields + 纯封包模板状态机；旧 OCR 草稿未加入 `WPELibrary` 编译项，运行入口不读取截图、不点击、不调用 OCR。
- 新增严格 schema 3 inventory 解析、目标装备身份校验、精确字段映射、属性哈希变更确认、预设 JSON/SQLite 持久化和 `EquipmentRefine` 机器人流程模板；模板默认禁用，未配置目标/字段映射/协议证据时 fail-closed。
- WPELibrary Debug 构建通过；`EquipmentRefineRunnerHarness` 5 项离线回归通过；Android equipment-reader unittest 30/30 通过。未启动游戏、未注入、未抓包、未发送真实装备炼化封包。
- 用户已提供一条人工抓取的 44 字节 raw request 样本：MZ 头，偏移 10 的 raw code 为 `0x8008`，三个 4 字节大端观测值 `5/4/3003`，以及 19 字节 ASCII 装备 ID `2091811378422870016`。样本已加入离线回归；raw code 和观测值的业务语义仍未确认。
- 用户又提供了另一槽位/另一装备的 44 字节 `0x8008` 样本：偏移 12-15 的值由 `5` 变为 `4`，ASCII 装备 ID 变为 `2091811378418675715`，而 `4/3003` 两个中间观测值保持不变；离线回归现验证两条样本可由“槽位 + ASCII 装备 ID”变量重建。
- 用户又提供了同槽位、同装备的 raw request 样本：偏移 20-23 由 `0x0BBB (3003)` 变为 `0x0BB9 (3001)`；这只证明该字节区可变，未确认业务语义。
- 用户提供了另一条 raw request 样本：同一槽位/装备下偏移 16-19 与 20-23 发生变化；现有离线模板契约只记录这些观测变量，不启用真实发送。
- 单条样本仍不足以独立确认 `bagPos/type/operaTyoe` 的业务语义、方向/响应关联和可替换字段；未生成生产模板，未注入真实发送适配器，默认发送器继续拒绝真实网络发送。
- 新增 `EquipmentRefineResponse.cs` 并接入 `EquipmentRefineStateMachine.ConfigureResponseAdapter`：真实宿主可注入收包适配器和响应帧提供器；未配置或解码失败时停止，不把响应评估器误当作已接通真实收包。
- `RefineConfiguration.RequiredMatches` 已加入 K-of-N 校验；新响应路径只允许 `=`/`>=`，旧 `RuleLogic` 仅保留兼容字段，不参与响应卡判定。
- 实际测试：`EquipmentRefineRunnerHarness` 项目构建通过，运行 `EquipmentRefineRunnerHarness.exe` 为 8/8；完整 solution 中 `Be.Windows.Forms.HexBox` 与 `WPELibrary` 构建通过，主桌面项目在 ClickOnce 清单签名阶段因本机证书存储缺少签名证书失败（MSB3323），未修改签名配置。不存在 `tests/EquipmentRefineRunnerRegression.ps1`，未伪造该脚本通过。
- 3–7.xls 的同类原始记录重复证明：155 字节记录可按头部大端体长拆成 67+88（体长 `0x0039`、`0x004E`），1036 字节记录可按体长 `0x0402` 作为单帧；新增 `EquipmentRefineFrameFramer` 只验证协议中性边界并保留 raw code，不解释卡片、属性、错误码或业务协议含义。
- 历史阶段审计记录：在桌面入口接入前，尚未找到 `EquipmentRefineStateMachine`/`EquipmentRefineExecutor`/`EquipmentRefinePreset` 的生产实例化入口；该阶段未凭空接入 WinForms 窗口。随后已由 `Injector_Form` 的编辑/加载/启动/取消入口和 `EquipmentRefineHost` facade 接上离线宿主 seam。
- `IEquipmentRefineResultPresenter`、`StringEquipmentRefineResultPresenter` 和 `IRefineStopReasonMapper` 会输出卡号、全部属性和值、命中规则、K/N 和“未替换属性”；错误映射只接受显式字典，未知代码保持“未映射”。当前真实协议依赖仍未绑定，默认路径继续零发送 fail-closed。
- Harness 现为 10 项，覆盖 presenter/错误映射、状态机命中后单次发送、序列门槛、完整卡片和 preset K 编解码。
- 收尾核对：`sendBeforeSequence` 现在在调用 `SendRefinePacketAsync` 之前从本轮已读取的 pre-send `snapshot.Sequence` 捕获；响应序列等于该基线或更小均 fail-closed，严格更大才允许评估。状态机 fixture 验证命中后发送计数为 1，结果保留命中卡/K/N/规则且 `Replaced=false`。
- `EquipmentRefineRunnerHarness` 实际包含 9 个测试函数，输出已修正为 `PASS (9 tests)`；本轮复跑通过。不存在 `tests/EquipmentRefineRunnerRegression.ps1`，未声称其通过。
- 第二阶段收尾：响应模式在发送调用前固定 `sendBeforeSequence`，响应必须提供更大序列；命中卡结果通过 `ExecutionResult` 保留卡号、完整卡对象、命中规则索引、命中数/K/N 和 `Replaced=false`，不会调用任何替换/应用逻辑。未命中完整响应才允许下一轮，错误/超时/身份变化/不新序列/卡片不完整均终止。
- Harness 实际包含 9 个测试函数，构建并运行结果为 `PASS (9 tests)`；覆盖状态机级命中后发送计数保持 1。完整 solution 的 HexBox/WPELibrary 构建通过，主项目仍因本机缺少 ClickOnce 签名证书 `MSB3323` 阻断。
- 最终审计修正：`EquipmentRefineRunnerHarness` 实际运行 10 项，输出为 `PASS (10 tests)`；新增旧版无 K 字段预设布局兼容解码、状态机停止原因映射到 presenter 的回归。`EquipmentRefinePresetPlan` 版本升为 2，并保留版本 1 旧布局读取，避免已有预设静默失效。
- 状态机错误完成路径现在会调用注入的 `IRefineStopReasonMapper`，再调用 `IEquipmentRefineResultPresenter.PresentStop`；未知错误码仍显示“未映射”，成功结果保持 `Replaced=false`。当前未发现桌面生产实例化入口，presenter 尚未绑定真实窗口。
- 当前桌面入口已接入 `Injector_Form`：可编辑/保存、加载、启动和取消炼化 Host；结果/停止原因写入现有日志。`EquipmentRefineHost`/`EquipmentRefineRunnerFactory` 只编排显式依赖，默认依赖为空时发送前 `ProtocolUnverified` 且零发送，不自动导航页面或替换属性。
- 新增离线截图标签回归：四张图按上传顺序保存 20 张候选卡的原始评分、五条中文属性名称和值字符串；每张图只计上排中/右和下排左/中/右五张候选卡，左上当前卡单独排除。用户已确认这些标签对应 `7.xls` 套接字 `12808` 的同一次操作组（12/26/44/155/1036 字节记录），但该 association 仍不等于二进制偏移映射；标签模型不伪装成真实协议 fixture，不映射 `TargetAttribute`。当前 harness 为 `PASS (27 tests)`。
- 该阶段离线验证为 `EquipmentRefineRunnerHarness: PASS (27 tests)`，WPELibrary Debug 构建通过。真实 20 卡字段/解码、真实 response source/收包、真实 sender、真实资源错误码、主程序运行时 UI 和实机炼化仍未验证；当时主程序编译受 ClickOnce 证书 `MSB3323/MSB3327` 阻塞。
- `JsonFixtureRefineResponseAdapter` 在该阶段仅用于版本化 UTF-8 离线占位格式 `equipment-refine-fixture-v1`；schema 无效帧（坏 JSON、缺卡、重复/越界索引、未知字段/属性类型、缺失必需字段）由 adapter 直接拒绝，序列/身份/稳定性/错误码等语义异常由 evaluator 安全停止；错误码响应仍可解码供安全展示。该阶段 harness 为 `PASS (27 tests)`，WPELibrary Debug 构建通过。目标预设和 response-card 状态机现在都要求当前穿戴目标的 slot、memberIdentity、equipmentId 三元身份齐全；resident JSON 还必须显式提供 `isWorn=true`，response-card 未命中进入下一轮前会刷新并复核 resident 身份；同身份背包候选不会被选中；空规则、重复启用规则和未知属性 fail-closed。Injector_Form 完成/停止路径保留日志并通过 MessageBox 展示结果。该 adapter 不代表真实游戏协议；真实 20 卡字段/解码、收包 source、sender、资源错误码、主程序运行时 UI 和实机炼化仍未验证。
- 新增离线截图标签回归：四张图按上传顺序保存 20 张候选卡的原始评分、五条中文属性名称和值字符串；每张图只计上排中/右和下排左/中/右五张候选卡，左上当前卡单独排除。用户已确认这些标签对应 `7.xls` 套接字 `12808` 的同一次操作组（12/26/44/155/1036 字节记录），但该 association 仍不等于二进制偏移映射；标签模型不伪装成真实协议 fixture，不映射 `TargetAttribute`。该阶段 harness 为 `PASS (27 tests)`。
- 真实协议接入仍待样本。最小交接材料必须成组保存：发送前状态/序列及当前穿戴 slot、memberIdentity、equipmentId；真实炼化请求帧；完整 20 卡响应帧（同时提供正常响应与材料/资源不足响应）；发送后状态或序列变化。缺少任一关联证据前，不启用真实 adapter/source/sender。
- 新增离线截图标签回归：四张图按上传顺序保存 20 张候选卡的原始评分、五条中文属性名称和值字符串；每张图只计上排中/右和下排左/中/右五张候选卡，左上当前卡单独排除。用户已确认这些标签对应 `7.xls` 套接字 `12808` 的同一次操作组（12/26/44/155/1036 字节记录），但该 association 仍不等于二进制偏移映射；标签模型不伪装成真实协议 fixture，不映射 `TargetAttribute`。当前 harness 为 `PASS (27 tests)`。
- 3–7.xls 的同类原始记录重复显示 155 字节可按头部大端体长拆成 67+88（体长 `0x0039`、`0x004E`），1036 字节可按体长 `0x0402` 作为单帧；新增 `EquipmentRefineFrameFramer` 仅验证这些协议中性边界并保留 raw code，不解释卡片、属性、错误码或业务协议含义。1036 内部的机械 20×50 分块未被当作卡片结构。

## 2026-08-25 炼化 resident 内存结果模式（离线接线）

- 当前项目新增 `EquipmentRefineMemoryResult.cs`：`IRefineMemoryResultSource` 接收宿主提供的 typed resident 结果，`RefineMemoryBaseline` 在每轮发送前固定进程、容器、会话、snapshot、sequence 及目标模式身份；Worn 继续要求 slot/memberIdentity/equipmentId，Bag 要求 slot/memberIdentity 并在可用时绑定 itemId/itemTypeId。不复制旧 Python/Android 读取器，不实现进程附加、地址/偏移或真实 sender。
- `EquipmentRefineStateMachine` 的 `MemoryResultMode` 不读取旧正式属性快照作成功判定；发送后仅接受同进程/容器/会话、稳定、snapshot 与 sequence 均刷新、目标身份一致且恰好 20 张卡的 typed 结果，再复用现有属性顺序无关、一对一 K-of-N（`=`/`>=`）评估。未命中允许下一轮，命中保留完整卡片详情并阻断后续发送；缺 source、旧快照、身份变化、卡片不完整、错误、超时和取消均安全停止。
- `EquipmentRefinePreset.MemoryResultMode` 已传入 Runner/Host，并兼容 JSON 与现有版本化 preset plan；默认未配置 source 时发送前 `ProtocolUnverified`、零发送。满值、`maxKnown/maxValue`、`refineNeedFactories` 不参与该模式。
- 本轮实际验证：明确路径 MSBuild 重建 harness `HARNESS_BUILD=0`；最新 `EquipmentRefineRunnerHarness: PASS (39 tests)`、`HARNESS_RUN=0`；WPELibrary Debug 重建 `LIB_BUILD=0`。本轮新增 response source 超时/取消竞争的 fail-closed 回归，并让超时主动取消本轮接收等待；真实 resident source、20 卡二进制字段/解码、真实 sender、资源错误码、主程序运行时 UI 和实机炼化仍未验证。
- 审查补充：编辑器已可设置并保存 `MemoryResultMode`；状态机现在强制执行 memory source timeout，source 异常、超时、取消均不允许后续发送。新增 `ResidentJsonlRefineMemoryResultSource` 只接受注入的 schema 3 只读 JSON/JSONL，严格校验当前穿戴三元身份、稳定刷新序列和恰好 20 张卡，并保留 rawId/rawValue/rawOrder/propertyKey/原始名称；旧 resident 历史记录缺少 `isWorn/equipmentId` 的拒绝测试与完整合成 schema 回放测试均通过。该适配器不是游戏真实协议，不实现 live attach，真实 source/sender/20 卡二进制解码/资源码仍待样本。

## 2026-08-25 背包目标模式（离线接线）

- `EquipmentTargetMode.Bag`、`BagTargetSelector` 和 `EquipmentRefinePreset.BagTargetMode` 已独立于旧 Worn 模式；编辑模型与 `EquipmentRefinePresetEditor` 可保存/加载背包 `slot + memberIdentity`，并展示可选的 `m_ItemId`、`m_ItemTypeId`、原始 name/rawFields 摘要。旧 Worn 模式仍强制 `isWorn=true`、slot、memberIdentity 和 equipmentId。
- `EquipmentRefineDetector.ParseBagInventory`/`FindBagTarget` 只接受 schema 3、只读、同一 container/session/snapshot 的 Bag 快照，并要求所选 slot/memberIdentity 唯一；不从背包条目猜 equipmentId，不扫描候选替代目标。`ResidentJsonlRefineMemoryResultSource` 只解析与所选 slot 唯一关联的 `equipmentPath` 卡片，缺卡、重复/多路径和不完整响应 fail-closed。
- `EquipmentRefineRunner`/`EquipmentRefineHost` 将背包目标、itemId/itemTypeId、MemoryResultMode 和现有 K-of-N 状态机贯通；离线 fixture 已验证首轮未命中可续发，后续任一卡命中后 send count 不再增长，结果保留卡片详情且 `Replaced=false`；另有同会话变化停止和 response-card 属性读取保持身份字段测试。本轮实际 harness 为 `PASS (39 tests)`；真实 reader 实时桥接、请求帧、sender、二进制 20 卡解码、资源错误码、主程序运行时 UI 和实机炼化仍未验证。

## 2026-08-25 旧 resident reader 接入审计（离线）

- 已只读核对旧项目实际入口：`F:\项目目录\飘渺游戏助手\tools\equipment-reader\equipment_inventory_resident.py` 的 `ReaderProcess` 通过 ADB 启动带 `--resident` 的外部 reader；`make_state()` 输出 schema 3，`compact_refine_candidates()` 使用 `equipment_refine_cards.py::expand_refine_cards()` 展开卡片。
- 当前历史 state 实际包含 20 张 `refine-candidate`，索引为 1..20；但 item 节点只有 `memberIdentity/slot/rawFields/candidateOnly`，缺少当前 WPE 必需的 `isWorn/equipmentId`，同时样本 `refineProbeReason` 为 `key_node_owner_not_found`。现有拒绝测试确认不会补字段或把该记录当作实时目标。
- 当前 WPE 没有旧 Python/ADB reader 的自动进程、stdin 或 worker 接线。通用 `ReadOnlyProcessMemoryReader` 只接受显式地址，不包含游戏地址链；当前可用桥接边界仍是注入 JSON/JSONL provider 的 `ResidentJsonlRefineMemoryResultSource`，不实现 live attach。
- `EquipmentRefineExecutor` 仍要求显式验证的 packet template 和授权 sender；`DiscoverRefineTemplateFromCapture()` 无证据时返回空。真实请求接受条件、20 卡二进制 decoder、response source、材料/银子错误码和“关闭炼化页直接发包”语义均未验证。最小交接材料必须成组提供：带 `isWorn/equipmentId` 的无错误 ready snapshot、发送前/后状态与序列、完整请求帧、完整 20 卡响应、材料不足和银子不足响应。

## 2026-08-25 藏宝图当前连接与发送错误修复热更新（2026.8.25.6，已被取代）

- 修复藏宝图运行时在同一 Hook 会话内优先复用旧路由的问题：每次取当前路由先检查最新有效抓包，只有显示列表自动清空时才回退到仍可用的会话 socket；失效缓存会被清除。
- 当时尝试修复已保存 `0xB0F4` AutoDig 帧因旧目标地址不同而被排除的问题：先校验当前发送类型和帧契约，再把保存帧的路由元数据绑定到当前连接。该跨连接重绑定策略已在下一节根据现场结果撤回。
- 原生 `send`/`sendto` 失败现在保留已写入字节数和 `WSAGetLastError`；藏宝图日志会输出 `socket_send_failed:wsa_error=<code>`，发送歧义仍立即停止，不自动重复未知动作。
- 本轮验证：WPELibrary Debug 重建 0 错误；`TreasurePacketEncoderRegression`、`TreasurePacketTemplatePatcherRegression`、`TreasureC6StreamRegression`、`ReviewRegressionChecks` 均通过。签名本地 ClickOnce `releases/2026.8.25.6/` 已生成，发布包 UI、上述三项 Release 藏宝图回归和 `ClickOnceUpdateRegression` 均通过，保留 4 条既有 EasyHook `MSB3178` 警告。
- 固定入口 `releases/小黑封包助手.application` 和桌面 `小黑封包助手.lnk` 已切到 `2026.8.25.6`；快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_152424.lnk`。旧版本、数据库和配置未清理，未结束运行中的进程。
- 本轮未启动目标游戏、未注入、未抓包、未发送真实游戏封包；新版本需关闭旧桌面窗口后从桌面快捷方式重新启动，才能加载修复。

## 2026-08-25 藏宝图跨连接 AutoDig 回退修复

- 现场运行 `2026.8.25.6` 后，日志 `d5480154-2be3-41a9-95aa-d47d5ce43b38` 显示第一次 Jump 为 `sent`，紧接着 AutoDig 为 `socket_send_failed`，后续 Jump 也连续失败；这与游戏连接在 AutoDig 后失效的现象一致。
- 根因定位为上一轮修复新增的同 `PacketType` 兜底：保存的旧目标地址 22 字节 `0xB0F4` 帧被重绑定到新连接。现已撤掉该跨连接复用；AutoDig 只接受当前连接精确匹配的抓包或保存模板，找不到时不盲发旧帧。
- 离线回归已更新并通过：WPELibrary Debug 重建、`TreasurePacketEncoderRegression`、`TreasurePacketTemplatePatcherRegression`、`TreasureC6StreamRegression`、`ReviewRegressionChecks`。
- `2026.8.25.6` 已被本修复版本取代；新的签名本地 ClickOnce `2026.8.25.7` 已发布，固定入口和桌面快捷方式目标已指向固定入口，旧窗口不要再次启动藏宝图。

## 2026-08-25 藏宝图生产模板缺失导致掉线的诊断修复

- 现场运行 `2026.8.25.7` 的日志 run `db06702d-6366-456e-92e0-0d6949033c16` 显示：目标 `slot=22, scene=1000, x=8, y=45` 首次 Jump 记录为本地 `sent success=true`，随后未观察到到达证据；约 300 ms 后 Use 准备记录 `use_template_not_found` 并带有旧的 `type=13,num=1,param=2` 回退信息，Use 发送失败，后续 Jump 也因 Socket 失效连续失败。
- 这里的 `sent` 只代表本地发送边界完成写入，不代表服务器接受了业务封包。生产路径在当前会话没有真实 Jump/Use 模板时仍生成了猜测的 Jump，并准备默认 Use；这与首个 Jump 后连接被对端关闭、随后出现发送失败的时序一致，确认问题属于生产发包前置校验缺失，不是 C6 识别步骤数量问题。
- 修复为生产 `TreasureMapPresetOptions.RequireCurrentPacketTemplates=true`：Jump 和 Use 只能从当前 Hook 会话/当前连接中捕获并通过模板校验的真实帧生成，只重绑定已允许的目标字段；缺失时返回 `NotDispatched`，在进入 native send 前零发送，不再发送纯编码 Jump 或默认 Use。离线回归仍可使用闭合编码器，不改变测试接口。
- 自动挖宝兼容分支仍只接受已校验的当前连接 AutoDig 预设；本次没有改变其已存在的当前路由检查和旧跨连接拒绝逻辑。
- 本轮 Debug WPELibrary 与完整解决方案重建、`TreasurePacketEncoderRegression`、`TreasurePacketTemplatePatcherRegression`（含缺模板零发送前置校验）、`TreasureC6StreamRegression`、`ReviewRegressionChecks` 均通过；签名本地 ClickOnce `releases/2026.8.25.8/` 已生成，Release 构建 0 错误、保留 4 条既有 EasyHook `MSB3178` 警告，发布目录 204 个文件/88 个 `.deploy` 映射，`WPELibrary.dll` 与其 `.deploy` SHA-256 一致。Release 发布目录 UI 回归和 `ClickOnceUpdateRegression` 均通过。
- 固定入口仍为 `releases/小黑封包助手.application`；桌面快捷方式已更新到 `.8` 图标并备份为 `releases/shortcut-backups/小黑封包助手_20260825_160408.lnk`。旧发布、数据库、配置和当前运行进程均保留。未启动目标游戏、未注入、未抓包、未代替用户发送真实游戏封包；实机只需确认修复版不再因缺模板盲发掉线，并在日志中出现 `jump_template_not_found`/`use_template_not_found` 时保持零发送。

## 2026-08-25 藏宝图 AutoDig 受控跳图修复（2026.8.25.9）

- 现场运行 `2026.8.25.8` 的新日志 run `0b3644f4-7f0a-4a2d-ba72-a9004142cd7e` 已确认：版本确实生效，但每个目标都在真正发送前因 `jump_template_not_found` 被拦截，因此地图不跳转；同一运行未进入 AutoDig/Use。
- 修复生产模式选择：不再用兼容性的 `GetCurrentUseRequest` 误把旧参数当作当前 Use 模板。只有当前会话存在真实 `0x783A` Use 模板时才走 Jump→Use；否则必须同时存在匹配当前线路、已校验的 `0xB0F4` AutoDig 帧，才进入 AutoDig 兼容路径。
- 在该受控 AutoDig 路径中，若游戏没有暴露 `0x5828` Jump 模板，只允许按当前 C6 目标坐标生成 Jump；AutoDig 本身仍必须来自当前线路已校验帧。普通 Jump→Use 路径仍缺模板零发送，生产模式也不再在缺 AutoDig 模板时使用纯编码 AutoDig 回退。
- 新增离线回归覆盖“无 Jump 模板、当前线路有 AutoDig 帧”场景，确认发送 1 个 Jump 和 1 个 AutoDig；模板、编码、C6 流、Review、发布版 UI、ClickOnce 和 `git diff --check` 均通过。C6 回归中的冷却计数断言同步为当前 AutoDig 语义的 3 次调用。
- 已生成签名本地 ClickOnce `releases/2026.8.25.9/`；Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告。固定入口仍为 `releases/小黑封包助手.application`，桌面快捷方式已更新到 `.9` 图标，备份为 `releases/shortcut-backups/小黑封包助手_20260825_162209.lnk`。旧发布、数据库、配置和“一战倾城”等现有预设未修改、未清理。
- 本轮未启动目标游戏、未注入、未抓包、未代替用户发送真实游戏封包；需要关闭旧桌面窗口后，从桌面快捷方式重新启动 `.9` 才会加载修复。实机验收重点是日志出现 `auto_dig_prepare code=current_send_preset` 后再观察地图是否跳转和是否消费成功。

## 2026-08-25 藏宝图 Jump/AutoDig 分支修正（2026.8.25.10）

- 现场 `2026.8.25.9` 日志已确认 C6 坐标识别成功，但在 `jump_template_not_found` 后直接 `target_failed_skipped`；原因是当前会话存在 Use 证据时，旧分支没有进入“无 Jump、复用当前线路 AutoDig”的受控路径。
- 新增当前会话 Jump 模板可用性判断；只有当前线路存在已校验的 `0xB0F4` AutoDig，且 Jump 或 Use 任一模板缺失时，才选择 AutoDig 兼容路径。普通 Jump→Use 路径和缺模板零发送保护保持不变。
- WPELibrary Debug 构建、TreasureC6StreamRegression、TreasurePacketEncoderRegression、TreasurePacketTemplatePatcherRegression、ReviewRegressionChecks 均通过；未发送真实游戏封包。
- 已生成签名本地 ClickOnce `releases/2026.8.25.10/`，旧发布、数据库和用户预设保留；桌面快捷方式已备份并切换到 `.10` 图标。关闭旧窗口后重新点击桌面快捷方式，才会加载该修复版。

## 2026-08-25 藏宝图动态连接重绑修复（2026.8.25.11）

- 发送前和真正进入 native send 前都会重新解析当前有效路由；连接未变化时包体保持不变，重连或端口/目标变化时只更新当前 `PacketFrom/PacketTo/Socket`。
- 已保存的 `0xB0F4` AutoDig 仅在完整闭合契约校验通过时允许跨旧地址安全重绑；固定包体不改写，旧 Socket 不保留。Jump/Use 仍只接受当前线路模板，发现线路变化会清空旧模板和 Socket，避免跨连接复用。
- 新增回归验证旧地址 22 字节 AutoDig 会绑定到当前连接，并保持包体、方向和旧 Socket 清零；WPELibrary Debug、C6 流程 Debug/Release、编码 Debug/Release、模板 Debug/Release、UI、Assistant、Review 和 ClickOnce 回归均通过。
- 已生成签名本地 ClickOnce `releases/2026.8.25.11/`；固定入口保持为 `releases/小黑封包助手.application`，桌面快捷方式已备份为 `releases/shortcut-backups/小黑封包助手_20260825_175218.lnk`，当前状态 `UpToDate`。旧 `.10`、数据库、配置和用户预设保留，未结束运行中的旧进程。
- 本轮未启动目标游戏、未注入、未抓包、未发送真实游戏封包；请先关闭旧 `.10` 窗口，再从桌面快捷方式启动 `.11` 做实机验收。

## 2026-08-25 藏宝图撤回跨连接 AutoDig 重绑（2026.8.25.12）

- 现场运行 `.11` 的日志 run `586270b0-9753-4f66-92cf-74ed069c04bd` 显示：C6 识别成功，Jump 记录为本地 `sent`，紧接着 AutoDig 记录 `auto_dig_prepare code=current_send_preset` 后出现 `socket_send_failed`，随后原游戏连接消失。保存的 `挖宝图` 模板为旧目标地址上的 22 字节 `0xB0F4` 帧；仅凭该固定帧无法证明它可跨会话复用，动态状态更可能在会话/序列/服务端校验中。
- 已撤回历史 AutoDig 跨连接重绑定：当前只接受与当前 `PacketType + PacketFrom + PacketTo` 完整匹配的捕获或保存模板；不同路线/重连不再把旧帧改绑到新连接。最终发送前再次读取当前路由，发现变化直接返回 `current_route_changed`，不进入 native send；缺少当前路线可验证模板时保持零发送。
- 路由变化仍会清空旧 Jump/Use 模板和 Socket 缓存；新增回归确认旧地址闭合 AutoDig 帧不能跨到新路线。WPELibrary Debug 构建、编码/模板/助手/Review/C6 回归、Release 发布包编码/模板/UI/ClickOnce 回归和 `git diff --check` 均通过。
- 已生成签名本地 ClickOnce `releases/2026.8.25.12/`；固定入口和桌面快捷方式已指向 `.12`，快捷方式备份为 `releases/shortcut-backups/小黑封包助手_20260825_180421.lnk`。旧版本、数据库、配置和当前运行中的旧进程均保留；关闭旧 `.11` 窗口后从桌面快捷方式重新启动 `.12` 才会加载回退修复。
- 本轮未启动目标游戏、未注入、未抓包、未代替用户发送真实游戏封包；“动态验证码”目前是基于掉线时序和固定模板证据的合理推断，不是已解出的协议字段。

## 装备炼化背包模式与 resident 边界（2026-08-25）

- WPE 的 `BagTargetMode` 继续只接受同一只读快照内唯一的 `slot + memberIdentity`；`m_ItemId/m_ItemTypeId/name/rawFields` 仅作为原始确认字段，不猜 equipmentId，不扫描背包替代用户选择，也不替换属性。
- native reader 现在提供显式 `--inventory-only` resident 模式和 `inventoryMode:"bag"` 快照标记：它跳过可选 refineNeed/EquipMgr 探针并独立枚举 `BagMgr.m_ItemDict`。这不代表真实炼化卡已读到；WPE 仍要求候选 `equipmentPath/slot` 唯一关联及完整 1..20 卡片，否则 fail-closed。
- 当前源码审计显示 socket 创建发生在 `bind_pid` 成功之后、refine 探针之前；因此不能把 refineNeed 失败本身表述为 socket 未创建的已证实根因。BagMgr/m_ItemDict 读取失败仍会停止并不输出可用背包结果。
- 本阶段离线验证：native Android CMake production build 通过；native inventory-only protocol test 退出 0；Python reader 相关测试 `59/59`；WPE harness `39 tests`、WPELibrary Debug Rebuild 均通过。未启动客户端、未抓包、未发包、未炼化、未写内存；实时 resident bridge、真实 sender、真实二进制 20 卡解码和资源码仍未验证。

## 背包选定条目的独立炼化候选路径（2026-08-25）

- 当前 WPE 对 resident JSONL 增加了背包目标元数据契约：`inventoryMode:"bag"`、`bagTargetSlot`、`bagTargetMemberIdentity`、`bagTargetReason`，以及候选级 `targetSlot`、`targetMemberIdentity`、`associationState`。WPE 只接受与 Bag baseline 完全一致且唯一关联的候选路径。
- native 侧的 `--bag-slot` + `--bag-member-identity` 仅在 `--inventory-only --resident` 下可用；它从选定 BagMgr 条目的 `m_EqptRefineData` 输出原始字段，不依赖 owner/isWorn/refineNeed，也不把 raw 字段猜成业务属性。Python 负责展开已确认的原始卡路径；Bag 模式排除正式“当前”卡，避免 21 卡误判。
- 恰好 20 张卡、卡片/属性顺序无关、唯一 equipmentPath/slot 和身份绑定仍由 Python/WPE 严格门槛共同保证；不完整、重复/越界、目标不一致或关联失败均 fail-closed。Bag 模式不使用 `refineNeedFactories`、`maxKnown` 或 `maxValue`。
- 本轮离线验证：native Android CMake build 通过，native Visual Studio protocol test 退出 0；Python equipment tests `60/60`；`EquipmentRefineRunnerHarness: PASS (40 tests)`；WPELibrary Debug Rebuild 通过。真实 native resident 卡数据、真实 source/sender、二进制 20 卡字段、资源错误码、主程序运行时 UI 和实机炼化仍未验证。

## 背包目标桌面配置链复核（2026-08-25）

- `Injector_Form` 已提供“炼化预设”编辑、JSON 保存/加载、启动和取消入口；编辑器可填写背包 `slot`、`memberIdentity`，并显示/保存 `m_ItemId`、`m_ItemTypeId`、名称和 `rawFields` 摘要。规则表支持添加/删除/启用、`=`/`>=`、K 和最大尝试/间隔；保存前调用 `IsValid`，非法身份或 K/N 会被阻止。
- 修复 `EquipmentRefinePreset.Clone()` 复制背包模式及完整 `BagTarget` 字段，避免 Host/Runner 复制配置后丢失目标身份；新增 harness 回归覆盖该复制路径。预设加载失败现在同时写入日志并弹窗；启动后默认依赖为空仍在发送前安全停止、零发送，状态机停止原因/命中详情由 presenter 交给 UI MessageBox。
- 本轮离线验证目标为 `EquipmentRefineRunnerHarness: PASS (40 tests)`；真实 reader/source、请求帧/sender、二进制 20 卡解码、资源错误码、主程序运行时交互和实机炼化仍未验证。

## 装备名称解码接入预设（2026-08-25）

- 读取器现在对 native 有界 `name` table 展开结果生成 `displayName`、解码状态、来源、路径和失败原因；WPE 只接受 `displayNameStatus=decoded` 的名称，原始 `rawFields` 仍保留。
- `EquipmentRefinePresetEditorModel.SetBagTargetFromInventory` 从同一只读背包快照按唯一 `slot + memberIdentity` 绑定目标，并把已解码名称写入 `BagTarget.Name`；名称只作显示/确认，不替代稳定身份。
- 编辑器背包字段标签更新为“name（解码/确认）”，预设 JSON/旧版 plan 的已有 `BagTarget.Name` 持久化路径保持兼容。
- 验证：WPE `EquipmentRefineRunnerHarness` 重建并运行 `PASS (41 tests)`；对应现场 state 已由读取器项目记录 5 件装备的中文名。未发送真实炼化封包、未写游戏内存、未点击。

## 仙器阶数解码接入装备炼化预设（2026-08-26）

- `EquipmentRefineDetector` 读取 resident 条目的 `xianqiTier*` 解码证据；`BagTargetSelector` 可保存可选 `XianqiTier`/`XianqiTierLabel`，查找目标时仅在预设明确要求阶数时做额外确认。
- `EquipmentRefinePresetEditor` 增加只读“仙器阶数（解码/确认）”显示；`EquipmentRefinePreset.Clone`、`EquipmentRefineRunner` 和 `EquipmentRefinePresetPlan` 均保留阶数及标签，旧版 plan 的可选字段仍兼容。
- 阶数只用于显示和一致性确认，不替代 `slot + memberIdentity` 稳定身份；缺失、未知或冲突的阶数不会被当作目标身份或成功条件。
- 验证：WPE `EquipmentRefineRunnerHarness` 重建并运行 `PASS (41 tests)`；读取器项目现场状态确认 5 件装备均为一阶仙器。真实炼化发送链路仍保持默认零发送 fail-closed，未执行真实封包、点击或替换属性。

## 常驻背包扫描范围收窄（2026-08-26）

- resident reader 首轮输出完整背包 `scanMode:"full"`，建立容器成员结构缓存和 `m_LogicType=500` 装备节点缓存；后续状态可通过 `scanMode:"equipment"` 表示只更新装备详情。
- 后续仍会读取容器节点键/指针做轻量结构校验，以发现新加入、移除或替换的装备；普通物品不再展开 item/property 字段。结构变化时 native reader 回退全量扫描并重建缓存。
- `EquipmentRefineDetector` 保留并严格接受 `scanMode` 的 `full` / `equipment` 值；缺字段按旧 state 兼容为 `full`，未知值拒绝。该字段不改变 WPE 的 `slot + memberIdentity` 身份门槛，也不授权真实炼化发送。
- 验证：读取器 Android Release 构建、Python `89/89`、WPE `EquipmentRefineRunnerHarness PASS (41 tests)`；当前桌面 WPE 仍不自动启动旧 Python/ADB reader，resident 读取器需由外部 host 保持运行；本轮未发送真实封包。

## 装备炼化预设迁移到助手页（2026-08-26）

- 炼化配置入口已从 `Injector_Form` 移除，统一放到 `Socket_RobotForm` 的“助手页 → 装备炼化预设”页签；选择预设或点击“打开总炼化设置”会打开 `EquipmentRefinePresetEditor`，保存后回写当前助手并调用现有 `SaveRobotList_ToDB()`，不启动炼化、不注入、不发送封包。
- 总设置页的规则表改为固定属性/比较项：比较符只提供 `>=` 和 `=`；目标值为整数手动输入，百分比只填写数字，例如 `2` 保存为协议单位 `2`，对应 `2.0%`。预设加载、克隆、计划编解码和运行配置继续对未知属性、非法比较符、重复规则及 K/N 失败关闭。
- 本轮独立验证：`EquipmentRefineRunnerHarness` 重建并运行 `PASS (42 tests)`；WPELibrary Debug Rebuild 通过；主程序 Debug Rebuild（`SignManifests=false`）通过，保留 4 个既有 `MSB3178` 警告。未执行真实游戏操作或发送封包。
