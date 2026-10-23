# 飘渺助手统一 APK

「战斗跳过」页的「跳过战斗」与「隐藏战斗画面」现可独立选择：只跳过、只隐藏或同时开启；两项关闭时不启动。只隐藏保留游戏原来的战斗与发送流程。设置在下次启动生效，运行或清理未确认时不能修改。详细模式与验收边界见 [BATTLE_HIDE.md](BATTLE_HIDE.md)。

隐藏启用时，视觉日志附带切图诊断，区分发送入口调用、进入地图回包与本地地图变化，并记录当时的战斗编号和回合。诊断不发送请求，不把未知协议推定为“回家”；发送调用记录不能作为服务端接受证明，详见 [切图观察](BATTLE_HIDE.md#切图观察)。

## 战斗跳过：隐藏战斗画面

「战斗跳过」增加可选的「隐藏战斗画面」子功能，默认关闭，设置在下次启动时生效。隐藏与跳过共用原来的战斗处理和停止恢复流程，不另外增加侧栏模块。页面提供实际视觉状态、计数和独立日志复制入口。功能边界、分阶段优化计划及尚待实机验证的内容见 [BATTLE_HIDE.md](BATTLE_HIDE.md)。

## 本轮整合候选（2026-10-19）

本轮把独立副本中的调度执行反馈、白名单诊断、显示读取缓存和可追溯交付工具，与主目录最新的整点/半点任务和 BOSS 地图、停止恢复修改整合。默认构建号为 16，普通版本名为 `2026.10.19.1-friends`；本轮测试候选使用 `2026.10.19.1-friends-open-v1-test`。免激活仍须显式指定构建参数。

定时任务新建时支持每小时整点、半点或两者，提前 0–30 分钟执行；原有每日固定时间任务保留原时刻，编辑转为周期时须明确选择。派发记录和模块确认分开显示，缺少本次关联证据时保持未确认。当前整合的验证与设备边界见 [MERGE_VALIDATION.md](MERGE_VALIDATION.md)；[OPTIMIZATION_VALIDATION.md](OPTIMIZATION_VALIDATION.md) 保留上一轮 code15 的历史结果。

本轮测试构建须同时指定名称和渠道；PowerShell 中把每个 Gradle 属性作为完整字符串参数传递：

```powershell
$env:FRIDA_CORE_DEVKIT = 'F:\项目目录\封包监听工具\.scratch\frida-core-devkit-17.17.0-android-x86_64'
.\gradlew.bat --offline :app:assembleDebug '-PpiaomiaoLicenseBypass=true' '-PpiaomiaoVersionCode=16' '-PpiaomiaoVersionName=2026.10.19.1-friends-open-v1-test' '-PpiaomiaoReleaseChannel=test'
```

独立交付使用 `tools/New-SuiteDelivery.ps1 -ApkPath <候选APK> -BaselineApk <基准APK> -OutputDirectory <尚不存在的目录> -IntegrationStatus merged`；该脚本不执行安装。普通构建不要传免激活参数。

找星与 BOSS 保留主目录新增的显式“直接传送”入口，只提交一次所选地图的动作，等待本次命令和目标地图确认；不会接着巡回或挑战。传送必须等待当前任务停止清理，并沿用授权、角色、战斗、队伍权限与动作互斥检查。地图清单可共用，所选集合与运行状态分别保存。另保留主目录新增的找星巡回地图选择及其服务流程。当前只完成离线合同测试，真实传送尚未验收。

## 自动 BOSS 候选（2026-10-18）

新增独立「自动BOSS」页面，位于自动找星之后。地图通过多选弹窗选择并自动保存，优先使用客户端导出的地图清单，首次无清单时提供五张内置地图。显式开始后按所选地图巡回，扫描名称或称号含 `BOSS` 的动态 NPC，忽略英文大小写；前往目标后读取它实际返回的挑战按钮再开战。配置、运行文件和清理状态与找星独立，动作与找星、暗雷互斥。具体协议、地图数据来源及未完成的实机验收边界见 [BOSSHUNT.md](BOSSHUNT.md)。

角色速度、快进、战斗跳过、金蝉装备、暗雷和自动找星继续在后台运行，不创建悬浮窗口。战斗面板按用户新要求恢复独立悬浮窗：子页只保留启动读取、停止读取两个按钮；启动时需悬浮权限，展开原双方阵型卡，人物在上、宠物在下，可收成可拖动悬浮球，点击球再次展开。停止读取移除两类窗口；仅切换页面不会停止服务。抗性只显示抗混、抗毒、抗风、抗雷、抗水、抗火、抗物理、抗遗忘八个单排按钮，字段码保持原协议 200、201、204、205、206、207、212、211。

本目录构建一个独立的 Android 应用：包名 `com.xnas.wpe.suite`，本副本默认朋友版 `2026.10.19.1-friends`（code 16）、`minSdk 23`、`targetSdk 35`，仅包含 `x86_64`。code 12 修复战斗快进/战斗跳过的状态显示（`running calls=N` 不再被当成“未确认”，互斥阻塞原因不再被 1 秒刷新覆盖），已覆盖安装 emulator-5554，详见 [BATTLE_STATUS_DISPLAY_VALIDATION.md](BATTLE_STATUS_DISPLAY_VALIDATION.md)。它将八个功能的界面、服务和 Rust worker 放入一个 APK；旧项目目录不参与构建写入。历史发布记录中的线上安装包为 `2026.10.16.3-online`（code 9），下载地址为 https://my-project-library.yinzhiping.chatgpt.site/piaomiao/download ，原发布记录中的公网下载SHA-256与本机一致。code10候选已在 emulator-5554 更新安装并完成页面、当前地图只读扫描和停止清理验收，尚未发布或完成真实自动循环。线上授权及设备验收的当前边界见 [ONLINE_ACTIVATION_VALIDATION.md](ONLINE_ACTIVATION_VALIDATION.md)。

## 朋友版交付（2026-10-18）

可发送压缩包：[`artifacts/friend-share-2026.10.18.1/飘渺助手朋友版-2026.10.18.1.zip`](artifacts/friend-share-2026.10.18.1/飘渺助手朋友版-2026.10.18.1.zip)。其中包含朋友版 APK、安装说明和 APK 的 SHA-256 清单；激活码不在包内。APK SHA-256：`28BFB79213D7F4A158FD5F7729A4692A0B00D237D243874029C2F98E3AE50529`；ZIP SHA-256：`FA16A1533A16F221E5E1C1C102BCD2CBD5C27C73B3BEB19FC0F8A41D98FFF696`。仅支持 x86_64 Android 环境，使用 Android Debug 签名，与上一朋友版证书一致。

朋友版 r9 含授权403错误分类、固定应用请求标识，以及 r8 的战斗跳过重复停止与金蝉页面绑定修复。r9 已完成本地构建、签名校验和回归，CodeRabbit 0 issues；尚未安装或替换网站下载，朋友实际激活仍待更新后验收。交付与排查证据见 [ACTIVATION_403_R9_VALIDATION.md](ACTIVATION_403_R9_VALIDATION.md)。

## 激活码授权候选

授权方案见 [ACTIVATION_DESIGN.md](ACTIVATION_DESIGN.md)，管理页面和服务见 [licensing-server/README.md](licensing-server/README.md)。第一版采用随机激活码、首次激活开始连续计时、一个码绑定一个安装；不额外限制游戏角色。管理员可批量发码、续期、停用和解绑，解绑保留原到期时间。

授权服务地址在 `gradle.properties` 中配置为 `https://api.urbandance.top`，普通构建会保留此地址；需要其他服务时用 `-PpiaomiaoLicenseUrl=https://实际授权域名` 覆盖。显式传入空地址的构建仍拒绝功能启动，不能直接发给朋友使用。应用增加 INTERNET 权限用于激活和验证；后台租约最长 120 秒，临时断网不延长租约。重新打开应用会使用已保存的凭证自动联网验证，无需重新输入激活码；真实服务连通和设备上的到期恢复仍须独立验收，不能从本地构建结果推断。

功能启动和原生后台工作均受授权门禁；停止、恢复和旧任务核查仍可用。首次无授权记录时只展示界面，不启动功能服务或请求 Root。授权页的安装识别码用于管理页定位记录，不是原始安装凭证。

战斗循环在本轮命令与人物、宝宝行动确认到齐后，快速处理当前回合剩余动画，再校验角色、战斗号、回合号及动画完成状态，提交一次回合完成通知。已完成的动画不重复处理，暂停或命令未准备时等待；自然结束通知与模块通知按战斗回合防重。无宝宝时只处理人物行动，停止仍先恢复自动状态并清理自有回调。动画处理只作用于本机客户端，不能代组队玩家跳过动画。

自动战斗选敌改为优先选择原有合法候选范围内剩余血量最少的存活敌人，人物和宝宝都使用这项规则。比较当前血量的绝对值，血量相同时沿用原站位顺序；没有可读的有效正血量时回退到原先的首个合法目标，已知血量为零或负数的目标不参与选择。发送前再次检查目标有效性。技能目标限制和防御行为保持原规则；这项修改尚未完成游戏内验收。

界面固定横屏，左侧是可滚动的功能导航。无顶部标题栏；目标选择、授权状态、完整版本名、构建号、架构与诊断复制统一在侧栏底部的系统设置页，模块运行状态显示在侧栏。CompactUi 仅缩小本应用 Activity 和悬浮面板的资源比例（density 0.8、fontScale 0.9），不改变模拟器系统分辨率或 DPI。八页顺序为角色速度、战斗跳过（原战斗循环）、战斗面板、自动找星、自动BOSS、战斗快进、金蝉装备、暗雷遇敌；以后新增模块接在后面。页面移除了重复标题、工具类型、静态介绍及状态解释，保留必要状态、权限反馈和写操作确认。

界面外观按 [UI_STYLE.md](UI_STYLE.md) 执行；外观更新不得改变本节定义的布局、内容和交互。

目标游戏包名只在系统设置页选择并保存，首次默认为 DHXY。每个功能启动时仍会独立扫描并校验当前 PID 和进程身份；统一的是目标包名，不是共享 PID 或 Frida 会话。暗雷遇敌、自动找星、战斗循环和战斗快进只支持 DHXY；XXXY 下会显示不支持。角色速度保持、金蟾和战斗面板主读取支持 DHXY 与 XXXY，战斗面板抗性查询仍仅支持 DHXY。目标只能在所有已加载模块都处于已停止状态时切换。

宿主每秒读取当前页面的本地状态；侧栏保留各页最近一次读取的简要状态，右侧状态显示为短标签。状态包括已停止、等待中、运行中、正在清理、错误和未确认。服务启动、发现目标进程、绑定角色和读到有效战斗帧是不同事实；页面只报告其实际能确认的部分，不能确认时显示“未确认”。打开页面只显示状态，不自动启动功能 worker、连接游戏、发送操作或请求 Root。通知入口返回统一主界面并选中对应子页。

## 模块与数据隔离

原 Java 包名和各模块服务保留在本 APK 内。`ModuleActivity` 与 `ModuleService` 把每个模块的文件和缓存放到 `files/modules/<module-id>`、`cache/modules/<module-id>`，SharedPreferences 名称带模块 ID。抗性查询保留服务 Context，避免退回到全局 Application Context。战斗面板帧读取支持 DHXY 与 XXXY；抗性查询当前只支持 DHXY，选择 XXXY 时会明确拒绝查询。各 Frida Java bridge 在目标游戏缓存下使用独立 `wpe-modules/<module-id>` 目录。

Rust worker 只接受套件包名下对应模块的精确目录：暗雷 `files/modules/anlei`、角色速度保持 `files/modules/mounthold`、循环 `files/modules/battleloop`、快进 `files/modules/battleskip`、金蟾 `files/modules/jinchan`、自动找星 `files/modules/starhunt`。角色速度保持仅接受 Android 私有路径 `/data/user/0/com.xnas.wpe.suite/files/modules/mounthold` 或 `/data/data/com.xnas.wpe.suite/files/modules/mounthold`，拒绝任意 `--state-dir`、旧包目录及路径后缀。战斗快进 NativeActivity 只接受套件的两个明确 alias，并在 NativeActivity 的 `files` 路径下追加 `modules/battleskip`。其它包名、相似路径和任意调用目录均被拒绝。

战斗循环与快进会安装到同一游戏发送路径上的 Hooks，因此共享一个持久化启动门禁。任一项未停止、正在启动/清理或缺少明确清理结果时，另一项不能启动；不会替用户自动停止功能。循环需要 `hooksRemoved=true`、`bindingCleared=true` 和 `restoreConfirmed=true`，快进需要前两项为 true。快进另外接受“运行锁空闲”的核对结论（`recovery-status` 为 `state=startable` 且 `lockProbe=free`）：这并不主张 Hook 已移除，只确认没有 worker 在跑，允许以当前游戏进程作为新会话启动，游戏内脚本遇到旧 runId 会先还原；对方真的在运行时门禁直接点名正在运行的功能。两个模块的核对判定一致（各自的 `recovery.rs` 都有 `Startable`），两个页面都是点一次「启动」即完成核对与启动，核对结论最多续跑一次，不会反复起 `su`。核对结论带 `checkedAt` 时效：`status` 在核对于之后又被改过，结论即作废；`running calls=N` 与 `state=running` 始终拒绝。停止收尾仍只认真实清理证据（`isClean`），不因“锁空闲”而提前释放启动位。

## 战斗快进逐回合日志

`battle_skip.lua.in` 对每个 `battle:round` 记录一条结论，只追加、不参与判定：`skipped`（带 trigger 与 playIndexBefore）、`blocked`（带 `battle_invalid` / `round_not_current` / `empty_command_list` / `already_finished` / `analyze_paused` / `already_skipped` 之一）、`not_at_round_start`（带 `idx`、`readIdx`、`cur`、`cmd`，表示没抢到“回合刚开始”的窗口，该回合按正常速度播完）、`fatal`（带 reason/battle/round）。同一回合同一结论只记一次。worker 每 2 秒把游戏缓存里的 `codex-apk-skip-<pid>-<ticks>-<runId>-rounds.log` 尾部（最多 64 KB）同步为应用私有的 `files/modules/battleskip/rounds.log`，`chmod 0644` 且属应用 uid，可用 `adb shell run-as com.xnas.wpe.suite cat files/modules/battleskip/rounds.log` 直接查看，不需要 root。

## 战斗跳过逐回合日志

`battle_loop.lua.in` 用同一套 `roundLog/logRound`：`started`（带 playerSkill/petSkill）、`skipped`（带 playover、calls）、`blocked`（`animation_not_ready` 带 finish/pause/idx，`original_auto_not_true` 带 `cmd`/`cur`/`auto`/`finish`/`pause`，`command_already_committed` 带 `cmd`/`cur`/`auto`，`no_player_action_target`、`no_pet_action_target`、技能配置的 playerErr/petErr）、`abort`（`battle_manager_changed`、`action_retry_limit`、finishAnimation 其它 why）。去重按 `round+decision` 记表，同一回合同一结论只出现一次。同步目标为 `files/modules/battleloop/rounds.log`，读法与快进相同。循环每个角色行动最多发送 4 次；恢复自动状态最多发送 3 次、间隔 1 秒，仍须收到确认才会清理 Hooks 和绑定。只有同步入口派发失败，或 NativeActivity 确认 `su` 进程未能创建且状态仍为本次 `starting` 时，才会记录未启动终态；Root 已派发后的状态不完整时门禁保持锁定，不按超时推断清理完成。

ActivityGroup 子页的运行时通知权限请求通过宿主统一发起，再按发起页和请求码转交结果，避免把系统回调误投到主页面。

角色速度另有r6b停止状态修复候选：修正无变更停止后侧栏持续显示“清理中”，保留恢复未确认的异常提示；25条真实方法回归及构建通过，已按授权覆盖安装，页面确认空闲标签隐藏、授权与设置保留；未启动速度功能做动态恢复验收。详见[MOUNTHOLD_STOP_STATUS_VALIDATION.md](MOUNTHOLD_STOP_STATUS_VALIDATION.md)。

## 自动找星多选与持久预设候选（2026-10-18）

自动找星支持同时选择多个星级：主页面直接显示1～30星的三排预设按钮，点击亮起选中、再次点击取消，每次切换立即保存。没有独立“选择星级”或“清空”按钮；全部点灭后保持空白，未选择时不能开始。运行、等待启动确认、停止和重试清理期间不能改选。若历史预设包含30星以上等级，会显示为可移除标签，不静默丢弃。

本机上次选择保存在模块独立的 `settings/selected_levels_v1`。重新打开、重启和正常覆盖升级可保留选择；只在新键不存在时迁移旧单星设置一次。新键存在且为空时不恢复旧值或默认星级，恢复页面不会自动挂机。多个所选等级共同参与原有按实例轮询和每轮去重；没有目标时等待刷新。

候选包：[`artifacts/PiaomiaoHelper-2026.10.18-multistar-presets-r3.apk`](artifacts/PiaomiaoHelper-2026.10.18-multistar-presets-r3.apk)。沿用当前设备的 code14 免激活构建变体，未改授权源码和版本配置；仅支持 x86_64。独立离线构建、签名与包内容检查通过，本任务尚未安装或进行本轮设备验收；详见 [STARHUNT_MULTISELECT_VALIDATION.md](STARHUNT_MULTISELECT_VALIDATION.md)。

## 自动找星历史候选（2026-10-16）

2026-10-17另有r5地图扫描提速候选：注册间隔500→250ms，普通NPC先按标题早筛再读取详细字段；首加载立即扫描与各动作确认窗口不变。十组离线Lua用例、Java检查与构建通过，未安装或测量设备实际扫描负载，详见[STARHUNT_SCAN_SPEED_VALIDATION.md](STARHUNT_SCAN_SPEED_VALIDATION.md)。

2026-10-17新增一键挂机候选：固定选择星级，再点“开始挂机”，会自动开始读取并返回游戏。当前没有所选星级也可开启，之后保持等待刷新；点击“停止挂机”一并结束动作和读取并等待清理。上次选择会保留，开页不自动运行。当前新候选尚未安装验收，详情与包路径见 [STARHUNT_ONE_CLICK_VALIDATION.md](STARHUNT_ONE_CLICK_VALIDATION.md)。此前轮询版本的现场结果单独保留如下。

上一版已安装的轮询候选为 `artifacts/PiaomiaoHelper-2026.10.16.4-starhunt-round-robin.apk`，按用户选择增加每目标每轮一次、挑战未确认轮换和整轮5秒间隔，并按真实字符串协议修复观战入口识别。七组正式Lua与独立复现通过，完整构建和签名通过；该候选已安装，现场确认单个8星观战目标的跳过和轮间等待；多人手动开始混入记录，不同实例轮换和完整循环仍未独立验收，详见 [STARHUNT_ROUND_ROBIN_VALIDATION.md](STARHUNT_ROUND_ROBIN_VALIDATION.md)。此前近处userdata修复已实战1次传送、1次挑战，约2.75秒进入首场战斗；不能以此替代新版忙碌轮换验收，历史结果见 [STARHUNT_SCENE_NPC_VALIDATION.md](STARHUNT_SCENE_NPC_VALIDATION.md)。

`starhunt` 页面只支持 DHXY。页面打开不连接游戏；星级集合预选不依赖当前候选，显式点击“开始挂机”才启动扫描，并在确认地图角色后提交一次RUN等待所选星级刷新。扫描以实例ID去重，传送后等待服务器确认地图和坐标，再提交一次挑战；每颗星每轮最多尝试一次，进入战斗后等待结束再继续。目标显示战斗中或提供观战动作入口时提前跳过；挑战等待12秒仍未确认开战则记录未确认并换下一颗，不把超时当忙碌。全部尝试后至少等5秒再开下一轮；传送位置未确认等异常仍停止动作。

新页面的“停止挂机”结束worker并等待hook和绑定清理确认；清理失败会保留“重试停止”。底层仍保留STOP_RUN回到只读扫描的命令，但不再作为独立页面按钮。地图或角色变化会暂停动作，重绑后不会恢复旧运行命令。自动找星和暗雷移动动作互斥，不会代用户关闭另一功能；战投和战斗面板可另行使用，找星不修改它们的战斗命令或自动状态。候选状态机、Java 模型和集成验证已通过；r3 已更新安装，页面、当前地图只读扫描和停止清理通过设备验收，持续实战与其他模块共存尚未验收，候选未发布。详见 [STARHUNT_VALIDATION.md](STARHUNT_VALIDATION.md)。

应用侧命令位于 `files/modules/starhunt/command`，新命令由 `sequence`、`mode`、`levels` 和 `map` 组成；`levels` 为升序无重复CSV，SCAN可为空、RUN必须非空。三层解析器兼容旧单星 `level` 字段，拒绝混用两种字段；新APK可读旧命令不表示旧APK支持新集合命令。Rust worker 将有效命令镜像到游戏缓存的 `wpe-modules/starhunt` 本会话路径，Lua通过 `acceptedSequence` 确认；状态KV完整返回应用侧，未知或缺失清理标志不当作已停止。游戏业务请求仅在显式开始后执行，扫描模式不发业务请求。

## 构建方式

当前默认将七个 Frida worker 链接到同目录的一份 `libfrida-core.so`，共享模式打包 11 个原生库。用 `-PpiaomiaoFridaMode=static` 可回退原静态链接模式，打包 10 个原生库。两种模式的八功能和授权规则相同。七个 Frida worker 的显式 `--suite-native-self-test [--hold-ms 0..60000]` 入口只做 Frida 初始化、版本读取、驻留和反初始化，不枚举或连接设备、不附加游戏；它用于加载及有限内存对照，不能代替业务验收。

Android API 24 起支持 `$ORIGIN` RUNPATH；为保留 minSdk 23，所有相关 Root 启动和恢复命令还会把可信 `nativeLibraryDir` 明确设为 `LD_LIBRARY_PATH`，不继承额外目录。当前设备加载测试不等于 API 23 设备实测。共享实现复用现有 devkit，未新增外部依赖；细节及验证结果见 `SHARED_FRIDA_VALIDATION.md`。

在 Windows 上配置 Android SDK 35、NDK 27.2.12479018、JDK 17 或 21、Rust Android target `x86_64-linux-android` 与 Cargo，然后在本目录执行：

```powershell
.\gradlew.bat --offline assembleDebug
```

作者本机免激活构建追加 `-PpiaomiaoLicenseBypass=true`；开关默认关闭，默认构建与朋友版授权行为不变：

```powershell
.\gradlew.bat --offline -PpiaomiaoLicenseBypass=true assembleDebug
```

开关把 `BuildConfig.PIAOMIAO_LICENSE_BYPASS` 置为 true，Gradle 再以 `build-native.ps1 -LicenseBypass` 仅在该构建进程内设置 `PIAOMIAO_LICENSE_BYPASS=1`（不落盘、不改系统环境）。打开后 Java 端 `isActive()` 恒为真、不发起 `/v1/activate` 与 `/v1/verify` 请求、`expire()` 不清租约也不派发停止，9 个 Rust worker 的租约门禁短路为通过，授权页显示“本机免激活构建”。免激活包不写 `license-lease.json`；关闭开关重新构建时 9 个 crate 会因编译期环境变量变化重新编译，不会沿用免激活产物。契约检查为 `tests/activation/verify-license-bypass.ps1`。本轮测试候选显式延续 code14 的免激活模式；普通授权构建仍默认关闭该开关。

Gradle `preBuild` 调用本目录的 `build-native.ps1`。脚本只使用现有 Frida 17.17.0 Android x86_64 devkit（默认位于项目共享 `.scratch`）和 Cargo 离线缓存；每个 Cargo target 与 Gradle 生成的 JNI 库都写进本目录的 `build` 或 `app/build`。复制到套件 JNI 目录后，脚本对明确列出的生成副本执行 NDK `llvm-strip --strip-unneeded`（共享模式11个，静态模式10个）；Cargo target、旧独立模块和源码不变。早期副本试验中动态符号与 `DT_NEEDED` 保持一致，重打包实测减少约 11.9 MB。v1.0.1 构建包为 255002691 bytes；当前瘦身包实测及边界见 `SHARED_FRIDA_VALIDATION.md`，历史构建记录见根目录 `PROJECT_STATUS.md`。

APK 输出为 `app/build/outputs/apk/debug/app-debug.apk`。旧模块目录不参与构建写入；suite 副本中的角色速度 worker 仅新增套件 state-dir 精确校验和恢复通道断开后的有限退避，快进 worker 的原子状态文件会继承应用私有模块目录的 UID，避免 Root worker 写出的状态无法被 UI 读取。

## 验收边界

角色速度保持目前通过 `/proc/<pid>/mem` 执行身份校验、条件读写和写后回读。该序列不是对游戏线程的原子 compare-exchange：游戏线程仍可能在比较与写入之间更新速度，回读无法证明没有覆盖这次更新。代码使用 `checked_write` 命名并保留恢复日志；该竞争窗口尚未消除，真正修复需要目标进程内的原子写入机制。

v1.0.1 已安装并打开于 `emulator-5556`，确认应用标签、启动入口和首屏。设备上预先运行的肉包悬浮层遮住部分侧栏，因此导航点击验收未完成；本次没有关闭或操作该悬浮层。没有授予 Root、启动功能 worker、连接游戏或发送功能请求。构建与启动成功不证明悬浮窗表现、游戏进程绑定、网络端点响应、真实游戏操作或清理恢复正确；游戏侧行为仍需在指定 Root 环境中逐项验收，循环与快进应单独确认其清理状态。

2026.10.13.1 当前修复包已通过完整离线 assembleDebug 与原生链接；versionCode 3、x86_64、唯一 launcher、六页、10资产、8原生库及Debug v1/v2签名检查通过。交付 artifacts/PiaomiaoHelper-2026.10.13.1.apk，255053220 bytes，SHA-256 294988FF944409E09AC53CEF9CB4C3740B29AE27E9045DC6A3C6CF0478FA43D2。已更新安装 emulator-5554，六个子页逐一打开返回 Status: ok，游戏进程在此期间保持不变，未新增助手崩溃记录。用户随后明确授权快进旧记录的只读 Root 核查与一次 DHXY 重启：首次核查建立身份基准，重启后核验 original_process_ended，通过正常 release 移除保护位和两项旧提示，页面已停止且无新增助手崩溃。没有启动读取、快进、跳过、速度、连接或装备功能；悬浮窗读取实测尚未执行。

旧任务核查已移除独立检查按钮。只有用户点击启动且该战斗模块存在未确认记录时才进行内部只读核验；核验后不自动启动，需再次点击启动。打开助手或子页不会请求 Root 或连接 Frida。循环与快进各自记录包名、PID、startTicks、可执行路径、boot_id；快进新增 --reconcile 入口，必须取得本模块运行锁，读取 /proc 证明原进程已退出或完整扫描证明没有目标进程，才写独立 process-ended 终态。快进证明额外要求 recoveryModule=battleskip，不能代替循环证明；不会伪造 Hook 移除或自动状态恢复字段。旧版本缺少身份时，完整扫描只建立当前进程观察基准，需重启游戏后再核验；未知、锁忙或权限不足保留保护。

各服务入口在目标检查、互斥拒绝或幂等返回之前及时调用 startForeground，避免 startForegroundService 后未登记导致 Android 关闭整个助手。此修复对应已记录的 ForegroundServiceDidNotStartInTimeException；游戏原生 SIGSEGV 与快进 attach 超时有同一进程关联，因果仍未确认。
## 悬浮球修复候选（2026-10-14，未安装）

独立包 artifacts/PiaomiaoHelper-2026.10.13.1-floating-ball-20261014.apk，255055771 bytes，SHA-256 F1AB50F0C46ED6DDBF3CA12D47CF7D2CA4D666C632F78E48E1793E43851A193B，版本仍2026.10.13.1/code3。悬浮球增至76dp，青绿渐变双环；拖动补算松手位置并记住展开收起位置。纯Java手势18项断言和真实adapter原方法抽取harness通过，完整构建签名清单通过；新球实际系统跟手效果待安装后确认。原正式交付包未覆盖，当前设备运行多个功能，安装时间等待用户选择。
2026-10-14 已更新安装悬浮球修正版：artifacts/PiaomiaoHelper-2026.10.13.1-floating-ball-20261014-r2.apk，255056632 bytes，SHA256 CAD903E457A0514A338BDA72901FC53DB05C8AADD25764C3009AF433356D852F。球76dp，青绿双环；短拖、展开收起及位置保持实机通过。长拖仍存在系统conflicting pointer actions取消，来源未确认，未视为完整拖动验收通过。原交付文件未覆盖，首份floating-ball候选已被此修正版替代。

## 网站联网授权候选（2026-10-16）

授权服务已迁移到现有Sites站点，公开介绍与管理入口见 [ONLINE_ACTIVATION_VALIDATION.md](ONLINE_ACTIVATION_VALIDATION.md)。code8候选已显式配置该站点HTTPS基址，大小66,149,067 bytes并安装于emulator-5554。线上匿名管理拒绝、APK无效码401响应已实测；管理页需要所有者重新登录，线上有效码、上传与公网下载仍待验收。源码默认无URL的构建继续fail-closed；不能把已配置候选与默认构建混同，也不能将本地测试当作线上完整通过。

## 朋友版 2026-10-19（免激活开放版，锁定 DHXY）

按用户要求构建 `2026.10.19.13-friends-open-v1`：**免激活开放版**（`PIAOMIAO_LICENSE_BYPASS=true`），不需要激活码；只支持 `com.gdoo.dhxy`，目标游戏在编译期锁定，系统设置不显示目标选择，不能选进程；其他功能与作者版一致。分享包：`artifacts/friend-share-2026.10.19.13-open/飘渺助手朋友版开放版-2026.10.19.13.zip`，APK SHA-256 `DF735E22E21C0537F464E7FF4DEE959964DD5211C0A44C2058BFAA93A6E56590`，ZIP SHA-256 `3BB162978A3DD266C1A6AB75B6717A821CC3FEAB0A3E3DC23F4B37E76D2586E4`。仅 x86_64、Android Debug 签名；未在设备安装验收，未发送或发布。详见 [FRIEND_BUILD_LOCK_VALIDATION.md](FRIEND_BUILD_LOCK_VALIDATION.md)。
## 朋友版固定构建规则

以后朋友版统一按以下规则构建，不再可变：

```powershell
.\gradlew.bat --offline :app:assembleDebug `
  '-PpiaomiaoLicenseBypass=true' `
  '-PpiaomiaoVersionCode=<N>' `
  '-PpiaomiaoVersionName=<YYYY.MM.DD.N>-friends-open-vN' `
  '-PpiaomiaoLockTarget=com.gdoo.dhxy'
```

- 免激活开放版，包内不含激活码/用户数据；
- 只锁定 `com.gdoo.dhxy`，系统设置不显示目标选择，不能选进程/其他游戏；
- 其他功能与作者版一致；仅 x86_64、Debug 签名；
- 构建后确认 `PIAOMIAO_LICENSE_BYPASS=true`、`PIAOMIAO_LOCKED_TARGET=com.gdoo.dhxy`；
- 普通/作者构建不传 `-PpiaomiaoLockTarget`，保持 DHXY/XXXY 可选。

最新示例：`2026.10.19.13-friends-open-v1`（code31），详见 [FRIEND_BUILD_LOCK_VALIDATION.md](FRIEND_BUILD_LOCK_VALIDATION.md)。
