# Unified APK independent validation report

Date: 2026-10-13. Scope: offline source/behavior checks and final APK package/signature inspection. No device install, game connection, real permission prompt, or network operation was performed.

## Results

- `run-battle-conflict-gate-offline.ps1`: PASS, 302 assertions. Includes synchronized cross-process loop/skip reservation in both directions and incomplete-cleanup release refusal.
- `run-production-gate-selftest.ps1`: PASS, production `BattleConflictGateSelfTest`.
- `verify-suite-source-contract.ps1`: PASS, 124 assertions on copied source. Covers one suite launcher, six registered child modules, module path mappings, `LocalActivityManager` lifecycle/selection restoration, host permission request/result routing, exact path guards, stop-clean gate, state ownership, and packaging config.
- `verify-review-followups.ps1`: PASS, 6 assertions. Jinchan dialogs resolve the ActivityGroup parent context; Anlei pre-attach cleanup references only current `run_id` staged paths; battle-panel role identity mismatch produces `Err(identity_mismatch_error(...))` with snapshot unconfirmed evidence.
- Jinchan JS runner: `node .\tests\jinchan-result-polling.cjs` (cwd `mobile-suite`): PASS.
- Mount-hold exact path predicate: extracted the production `valid_state_dir` function verbatim and compiled/executed it with local `rustc`; PASS, 2 accepted roots and 5 rejected cases (child path, traversal, app root, other package, relative path).
- Full mount-hold worker `cargo test --offline --manifest-path .\native\mounthold\rust-reader\Cargo.toml`: BLOCKED on Windows host, compile reports 34 errors rooted in unavailable `std::os::unix` and Unix `read_at`/`write_at` APIs. This is not a worker runtime test; the extracted predicate harness passed independently.
- No separate Lua test runner/file was present under `mobile-suite/tests`; Lua templates were not treated as host-executable acceptance.

## Final APK

Inspected `mobile-suite/app/build/outputs/apk/debug/app-debug.apk` after final build. The copied delivery artifact `artifacts/飘渺助手-整合版-v1.0.0-20261013-r1.apk` has identical byte size and SHA-256:

- Size: 258,978,247 bytes
- SHA-256: `2566F05E3C15E05197FB81D0B9E95B6AD556A717EBB0CF235A5C54B929BA31E7`
- Package/version: `com.xnas.wpe.suite`, 1.0.0 (code 1)
- minSdk 23, targetSdk 35, ABI x86_64
- Exactly one launcher: `com.xnas.wpe.suite.MainActivity`; six module activities are present
- DEX entries: 7; expected assets: 10; native libraries: 8
- APK signature v1 and v2 verify; one Android Debug signer, certificate SHA-256 `9f4085bc10118b0279dd3dcecceba16858e50c2691b525701891299534245826`

## Legacy source baseline

`legacy-source-baseline.json` contains 3,574 original files and has no paths under `.gradle` or `build`. A current hash comparison of those paths found 3,574 current baseline files, changed=0, missing=0. The 752 current additional files are entirely generated build state: `.gradle` 85, `build` 667. Per old module: mobile-anlei 122, mobile-battle-loop 104, mobile-battle-panel 166, mobile-battle-skip 98, mobile-jinchan 113, mobile-mount-speed-hold 149. The first ten added paths are all under `mobile-anlei\.gradle\...`. They are not source additions. This tester ran no Gradle build against the six legacy projects; its Java/Rust test outputs were confined to this validation directory. Because the baseline did not include generated APK/SO outputs, there is no recorded pre-run binary hash set for historical comparison.

## Boundaries and retained evidence

Permission transfer is source-structure validated only; Android callback behavior remains unverified. Native workers, Lua hooks, root execution, selected-module UI behavior, and game interactions were not device tested. The script, test source, harness source, and baseline are retained under this validation directory. Temporary `source-copies`, Cargo target, and generated harness executable/PDB could not be deleted: `exec_command` rejected `Remove-Item` as blocked by policy after the paths were checked to be inside validation. The APK inspection script cleans its own ASCII-path copy.

Commands used include:

```powershell
.\WinsockPacketEditor\.scratch\suite-20261013\validation\run-battle-conflict-gate-offline.ps1
.\WinsockPacketEditor\.scratch\suite-20261013\validation\run-production-gate-selftest.ps1
.\WinsockPacketEditor\.scratch\suite-20261013\validation\verify-suite-source-contract.ps1
.\WinsockPacketEditor\.scratch\suite-20261013\validation\verify-review-followups.ps1
node .\tests\jinchan-result-polling.cjs  # cwd: WinsockPacketEditor\mobile-suite
.\WinsockPacketEditor\.scratch\suite-20261013\validation\inspect-candidate-apk.ps1
cargo test --offline --manifest-path .\native\mounthold\rust-reader\Cargo.toml  # cwd: mobile-suite; Windows Unix API compile limitation
```

## 2026.10.13.1 最新重新打包与安装

2026-10-13：离线 assembleDebug 成功，包含最后暗雷和速度保持修正。APK 255009696 bytes，SHA256 B077D166F22D95ACB2B48DA0D18FC6EC85C2E39FF9B74D26CFA2F3B6790C6B1F；versionCode 3、x86_64、唯一 launcher、六子页、八原生库、十资产及 v1/v2 签名检查通过。emulator-5554 更新安装 Success，启动 Status: ok，设备版本与主界面均为 2026.10.13.1。截图 piaomiao-20261013-main.png。已有悬浮窗遮挡部分侧栏；本次没有点击功能启停或连接按钮。外部内存写入仍非原子；未实测游戏功能。结果判断 HTTP429/not_run。

### 2026.10.13.1 当前安装包与追加界面调整

当前侧栏顺序：角色速度、战斗快进、战投跳过（内部battleloop）、战斗面板、金蝉装备（内部jinchan）、暗雷遇敌。前三到第五显示名按用户输入更新，module id、目录及协议标识不变；未要求删除的暗雷暂留第六，后续模块追加。面板恢复原双方分栏、人物/宠物阵型卡及5到1槽位；状态可换行、窄屏阵型上下排。抗性下拉替换为全部13项按钮，选中高亮并更新下方表头和值。金蝉三下拉及统一目标显示浅色文字、边框和箭头；空候选adapter仍零项，单独空态不参与索引。

最终离线assembleDebug、36项后台源码契约、互斥门生产self-test及APK清单/签名检查通过。APK254996553 bytes，SHA256 98AE74F7A1554EA3F76AD91B952EADF31E5EB4C24AC4B782E3C7F6992F1F75A8，版本2026.10.13.1/code3，x86_64。已更新安装emulator-5554，安装Success、启动Status ok；屏幕确认侧栏顺序、原阵型、13按钮和抗风选中与表头对应，金蝉三个控件文字/空态清晰。截图在 .scratch/suite-background-validation/piaomiao-final-panel.png、piaomiao-final-resistance.png、piaomiao-jinchan-controls.png。本轮未点击功能启动/停止/连接、未执行游戏或装备操作；实际后台存活和游戏读取未实测。

CodeRabbit：后台转换9文件0 issues；原布局复审2issues（状态固定高度、窄屏挤压）已修正；后续7文件1条Minor措辞建议核对后不采用，guidance接收保护位owner/candidate而非请求者，交换名称会误指需要停止的功能。最后抗性按钮首次隐藏页高度修补的复审返回 rate_limit，提示33minutes，无on-demand可用，该修补复审尚未完成，没有替代手工审查。实际截图曾暴露按钮区域高度为0，修补后确认13按钮出现，不能用早期静态PASS代替屏幕验收。循环cleanup_unconfirmed与快进互斥仍保留，未删保护位。

### 2026.10.13.1 服务重建后的旧战斗任务恢复

根因：battleloop历史status保留stopping/cleanup_unconfirmed，服务重建worker句柄为空，原STOP不发起核查且START先被gate拦截。旧native只在内存保存原进程身份，无法在重新打开/覆盖安装后证明旧任务已消亡。新增strict只读 --reconcile 模式及显式CHECK action，p为空STOP走核查；开启页面不自动请求Root或启动脚本。模式先独占本模块worker.lock，严格读取/proc，读失败不视为空进程。新run attach前原子持久recovery-identity，禁止覆盖损坏或仍存活未确认身份。legacy无记录完整扫描唯一target仅观察，0 target才可确认不存在。原boot变化、PID目录消失或startTicks变化才确认原进程结束；exe/cmdline变化或权限失败不清保护。

Java gate识别state=stopped/processEnded/recoveryVerified/package/proof，缺证明、错误包、无效身份保持拒绝；release后只移除已经失效的gate-status提示，不改其他模块worker状态。快进页conflict_wait改为等待旧任务检查，循环页增加清理待确认、检查中、等待重启游戏状态及明亮文字，不再误报等待绑定。服务CHECK对活worker只提示，保留原监控与通知。CodeRabbit两轮分别指出扫描错误被当消亡、扫描失败仍可独立证实原PID结束，均已修正；最后单文件复审完成0 issues。没有替代手工审查。

14项恢复生产纯模型、扩展gate selftest、36项后台契约、Android cargo check、最终assembleDebug与包清单/签名通过。用户在本聊天明确授权只读Root检查，并在观察基准建立后授权重启DHXY。实际初检state=waiting_restart/legacy_baseline_saved，旧status不曾被删；重启目标游戏后应用CHECK返回verified/original_process_ended，写独立process-ended终态，保护marker和两gate-status由正常release移除。重新打开APK屏幕确认战投跳过已停止，战斗快进未启动，无阻塞提示。未启动业务辅助、未执行装备操作；仅重启游戏、核查身份和打开页面。截图 .scratch/suite-background-validation/piaomiao-recovery-stopped.png 与piaomiao-skip-unblocked.png。

当前交付 mobile-suite/artifacts/PiaomiaoHelper-2026.10.13.1.apk，255019532 bytes，SHA256 AB184141C584C9140EE2E1815C3A22D6AA4FBDCADE28FE5613E19B7A3E79C6DD。版本仍2026.10.13.1/code3，x86_64。
### 2026.10.13.1 前台服务崩溃修复与独立悬浮面板

当前包固定横屏，仅本应用资源比例调整为 density 0.8、fontScale 0.9；模块名、状态、全局目标小按钮及版本共用顶部一行，系统仍720×1280/density320。战斗面板子页改为两个启停按钮及动态短状态；启动需悬浮权限，Service拥有独立renderer和可拖悬浮球，收起不停止读取，停止清理窗口/handler。双方人物在上、宠物在下；抗性只保留八项单排小按钮，真实字段码200、201、204、205、206、207、212、211，删除静态说明。其它功能仍无悬浮窗。

设备 crash buffer 确认21:36与21:37两次助手ForegroundServiceDidNotStartInTimeException，21:37启动请求因gate拒绝而在前台登记前stopSelf。循环、暗雷、速度、金蝉及面板入口现在先前台登记再判定；拒绝/幂等路径保持业务隔离。旧检查按钮已移除，仅用户点击启动且有未确认记录时调用只读内部核验，核验后不会自动启动。快进新增attach前身份持久化与--reconcile，独立recovery.lock/worker.lock，完整proc扫描和严格进程身份核验；新reservation starting阻止沿用旧证明。skip证明须recoveryModule=battleskip，不可清loop，仍不伪造Hooks/恢复字段。

前台服务真实生产源码stub harness：旧版unsupported START、gate-denied START及CHECK已有worker场景均得到红；新代码三项均PASS。扩展GateSelfTest、BattleFrameProtocol、ResistanceSnapshot自检PASS；快进恢复模型7/7、Android cargo check/fmt通过；37项后台/独立面板源码契约通过。CodeRabbit覆盖18文件：首轮1 Major（proc读错时已退出PID被误认不可读）已修正；一次复审1 Minor（错误启动请求可能中断已有面板）已修正。最后active分支保护与清洁状态短文本未再次复审，已重新完整构建验证，不声明0 issues。计划判断实际API返回HTTP429/not_run。

最终交付mobile-suite/artifacts/PiaomiaoHelper-2026.10.13.1.apk，255053220 bytes，SHA256 294988FF944409E09AC53CEF9CB4C3740B29AE27E9045DC6A3C6CF0478FA43D2，code3/x86_64/唯一launcher/六页/8原生库/10资产/Debug v1v2。安装emulator-5554 Success，六页逐一启动Status ok；截图.scratch/suite-background-validation/piaomiao-final-two-buttons.png与piaomiao-final-loop.png。UI启动期间DHXY同一PID持续存在，没有新增助手崩溃。不启动读取、速度、快进、跳过、连接或装备操作，不重新运行Root核查或重启游戏。当前快进旧记录仍缺完整身份，保护保留，核验与重启、悬浮读取实测分别等待用户授权。游戏PID2932的SIGSEGV与快进attach超时有进程关联，但dropbox仅unknown两帧，因果未确认。
随后用户明确授权本次快进旧记录核查及一次DHXY重启。设备首次运行BattleSkipRecoveryService返回waiting_restart/legacy_baseline_saved，保存当前进程身份；执行一次force-stop并重新打开DHXY后，再核查返回verified/original_process_ended。skip/status写state=stopped/processEnded=true/recoveryVerified=true/recoveryModule=battleskip等完整proof，正常release移除.battle-conflict.guard、loop/gate-status与skip/gate-status。页面截图piaomiao-skip-recovered.png显示已停止且无旧阻塞；当前游戏进程仍存在，crash buffer没有新增助手FATAL。该阶段只做已授权的proc身份检查及游戏重启，没有启动业务辅助或抗性查询；悬浮读取实测仍未运行。结果判断实际API亦HTTP429/not_run，主代理依实际构建/测试/审查及设备证据接手。交付APK及hash不变。
### 2026-10-14 悬浮球放大与拖动修复候选（未安装）

修改范围为battlepanel/BattleOverlayService.java、新增FloatingBallGesture.java及tests/FloatingBallGestureSelfTest.java。球从56dp改为76dp，当前模拟器资源比例下预期直径约122px（旧90px）；青绿渐变双圆环、战/面板标识、按压反馈。记录上次球心，展开收起复用原位置。手势以DOWN始点计算总位移，固定主指针，UP补算最后坐标，CANCEL不点击，拖动结束不展开；请求低延迟事件派发。读取、抗性和其它模块业务路径未修改。

旧设备长拖(260,120)仅移(68,32)，证据为WindowManager的球frame变化。旧生产listener原方法抽取harness在DOWN后UP但没有尾MOVE时得到预期红（位置未更新且click）；新纯Javahelper18断言PASS，当前生产adapter原样抽取harness对漏尾MOVE、MOVE后UP尾差、tap、CANCEL和unbuffered请求均PASS。harness不模拟完整Android系统，因此实际新球跟手效果尚未确认。

CodeRabbit三文件1条Major兼容性提示不采用：提示要求requestUnbufferedDispatch仅API30可用；当前调用MotionEvent重载，官方SDK35 data/api-versions.xml第68974行标明since21，API30属于int重载（第68973行），支持本项目minSdk23。完整离线assembleDebug、37窗口契约、包/签名清单通过。计划与结果判断实际API HTTP429/not_run，未计作通过。

独立候选mobile-suite/artifacts/PiaomiaoHelper-2026.10.13.1-floating-ball-20261014.apk，255055771 bytes，SHA256 F1AB50F0C46ED6DDBF3CA12D47CF7D2CA4D666C632F78E48E1793E43851A193B。版本仍2026.10.13.1/code3，仅x86_64、唯一launcher、六页、8原生库、10资产、Debugv1/v2。原交付PiaomiaoHelper-2026.10.13.1.apk未覆盖；设备仍运行旧包。发现角色速度/暗雷/战斗面板正在运行，已询问安装时间，尚未答复；没有更新安装或启停功能，避免中断运行中的业务。
### 2026-10-14 悬浮球修正版已更新安装

用户明确要求更新安装。第一份放大候选实机长拖仍出现 ACTION_CANCEL；暂停 MOVE 的 WindowManager 更新也复现。当前修正版使用同一 FrameLayout/window token：平时仅76dp小窗，DOWN临时扩为显示尺寸，MOVE只移动固定大小球子视图，UP/CANCEL恢复小窗；额外球外DOWN不会开启手势。保留球心，展开收起位置不变。全部 BallDragProbe 和 __ball_probe_defer 调试代码已移除。

真实设备 emulator-5554：完整30ms拖动从(579,299)到(839,419)，位移(260,120)；反向短拖亦完整，点击展开、收起回原位置已确认。600ms长拖仍被 InputDispatcher 以 conflicting pointer actions 取消，并接收到另一组固定位置DOWN/UP；来源未确认，不能宣称长拖问题完全解决。未关闭或操作其它应用，未启动速度、快进、跳过、暗雷或装备；只恢复原先的panel reader。读取数据目前角色队伍校验未通过，与本次球UI验收分开。

完整离线assembleDebug、八个native链接、37项源码窗口契约、纯Java18断言及生产host adapter检查通过。CodeRabbit复审三文件提出1条Major：误认为MotionEvent版requestUnbufferedDispatch仅API30可用；SDK35元数据标明该重载since21，API30是int重载，未采用，不声明0issues。计划修正判断HTTP429/not_run；结果修正判断INPUT_SCHEMA/not_run，未计作通过。

交付 mobile-suite/artifacts/PiaomiaoHelper-2026.10.13.1-floating-ball-20261014-r2.apk，255056632 bytes，SHA256 CAD903E457A0514A338BDA72901FC53DB05C8AADD25764C3009AF433356D852F。版本2026.10.13.1/code3、x86_64、唯一launcher/六页/8native/10资产、Debug v1/v2通过；adb install -r返回Success，打开Status ok，仅panel前台服务运行，crash buffer无本次新增记录。原稳定交付及首份球候选没有覆盖。最终球截图 .scratch/suite-background-validation/piaomiao-ball-r2.png。
补充验证：清除调试代码后的生产adapter新增球外DOWN/无活动MOVE/UP用例PASS，确认位置与translation不变、pressed清除且host保持小窗；helper18断言PASS。本次生成class已清理，保留测试源码、脚本与stubs。
