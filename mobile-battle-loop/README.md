# 战斗循环助手

独立 Android 包 `com.xnas.wpe.battleloop`，版本 `0.1.0`（code `1`），目标为 Root x86_64 模拟器中的游戏进程 `com.gdoo.dhxy`。开始后以前台服务持有租约，由独立 Rust worker 绑定唯一游戏 PID、进程启动 tick 与 executable；游戏重开后重新发现和绑定。停止或租约过期时，worker 等待 Lua 恢复本助手取消的自动状态并确认 Hooks 与绑定已清理后才 detach。

每个战斗回合动态读取 `ui.m_AutoSkill[人物ID]` 与活跃宝宝的当前设置。当前支持正常攻击（`actType=0`）、防御（`actType=1`）和目标元表明确确认 `targetType[1]==1` 的敌对技能（`actType=7`）。其他动作、缺失设置和未确认目标类型会等待；活着的宝宝缺少设置时整回合等待。无宝宝或宝宝死亡时人物可单独行动，防御目标是演员自身，攻击目标按演员各自绑定。

桌面战斗循环全局键 `__codex_confusion_pet_continuous_20261013_r7` 或旧战斗跳过键 `__codex_apk_battle_skip_v1` 存在时，助手显示冲突等待并停止本回合动作，不会替用户停止桌面辅助。悬浮窗只显示简短运行状态；主界面可查看人物与宝宝当前技能 ID。需要 Android Root 与悬浮窗权限。

`build.ps1` 使用 Frida 17.17.0 Android x86_64 devkit 构建 Rust worker；可通过 `FRIDA_CORE_DEVKIT` 指定该 devkit 目录。随后用 `gradlew.bat --no-daemon assembleDebug` 构建 APK。发放产物应复制到 `artifacts/` 中唯一命名的文件。

本地 Lua 沙盒与状态字段静态检查可从模块根目录运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\battle-loop-sandbox\run-tests.ps1`。该 runner 执行 9 个 Lua 5.4 隔离 mock 场景和静态契约检查，不连接游戏或设备；APK 未安装，尚无真实设备/游戏验收。
