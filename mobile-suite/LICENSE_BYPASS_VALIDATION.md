# 本机免激活构建验证（2026-10-17）

## 范围

按用户要求给作者本机提供“取消激活码激活”的构建方式：默认构建不变，只有显式传入 `-PpiaomiaoLicenseBypass=true` 时才生成免激活 APK，并覆盖安装到 emulator-5554。授权服务、数据库、发码记录和朋友版分发内容均未改动，也没有生成或删除任何激活码。

## 改动

- `app/build.gradle`：读取 `piaomiaoLicenseBypass`（默认 `false`），生成 `BuildConfig.PIAOMIAO_LICENSE_BYPASS`，并在开关打开时给 `buildSuiteNative` 追加 `-LicenseBypass`。
- `build-native.ps1`：新增 `-LicenseBypass` 开关，只在该构建进程内设置 `PIAOMIAO_LICENSE_BYPASS=1`，否则显式移除该变量；不写文件、不改系统环境。
- `native/license_lease.rs`：新增编译期 `LICENSE_BYPASS`（`option_env!`）与 `bypass_lease()`；`read_valid_file()` 在免激活构建下先返回合成租约（期限为当前 `boottime + 110s`，落在 battle_skip Lua 要求的 `(now, now+120s]` 内），不再读磁盘租约，因此 `require_suite_lease`/`require_module_lease`/`valid_*` 全部短路为通过。
- `LicenseManager.java`：`isActive()` 恒真；`displayStatus()` 返回“本机免激活构建”；`activate()`/`verify()` 直接返回成功且不发起网络请求；`expire()`/`invalidate()` 不清租约、不派发停止。
- `SuiteApplication.java`：免激活构建不启动授权监督任务。
- `ActivationActivity.java`：免激活构建不做自动验证，状态行显示“本机免激活构建：不校验激活码，全部功能直接可用”。
- `tests/activation/verify-license-bypass.ps1`：新增契约检查（默认关闭、原生短路顺序、Java 门禁无请求、授权页不自动验证）。
- `tests/activation-integration/Run-ActivationJavaStubs.ps1`：stub `BuildConfig` 增加 `PIAOMIAO_LICENSE_BYPASS=false`，原有非免激活用例保持原语义。

## 构建与安装

- 免激活构建：`.\gradlew.bat --offline -PpiaomiaoLicenseBypass=true assembleDebug`，退出码 0；生成的 `BuildConfig.java` 为 `PIAOMIAO_LICENSE_BYPASS = true`。
- 产物：`artifacts/PiaomiaoHelper-2026.10.17.2-license-bypass-20261017.apk`，67,053,802 bytes，SHA-256 `C3AE3586CA9C8B098B007DB439D8326AC43D1A3FA22F4BEAF53405F0C14111C1`。
- 安装：`adb -s emulator-5554 install -r` 返回 `Success`；包名、签名一致，已安装的 `versionName 2026.10.17.2-friends`（code 12）与既有应用数据保留。

## 设备侧结果

- 主界面右上角授权胶囊显示“已授权”（此前该状态需要有效会话与租约）。
- 授权页 `uiautomator` 文本实测：`飘渺助手授权`、`本机免激活构建`、`安装识别码：EDD215D05655（解绑时提供）…`、`本机免激活构建：不校验激活码，全部功能直接可用`、`版本 2026.10.17.2-friends`。
- `files/` 下没有 `license-lease.json`，说明没有走服务端校验路径；`shared_prefs/suite_license_private_v1.xml` 仍是 2026-10-16 21:23 的旧 `session_token`，本轮未读取、未修改、未清除用户数据。
- 未观察到助手崩溃或 ANR；期间模拟器自身的 `com.android.flysilkworm` 会定期广播 `TASK.SWITCH` 把 `com.gdoo.dhxy` 切回前台，属该环境既有行为，与本构建无关。

## 回归

- `tests/activation/verify-service-gates.ps1`：PASS（5 个服务门禁、STOP 旁路、worker 重启/恢复门禁、错误与传输边界）。
- `tests/activation-integration/Run-ActivationJavaStubs.ps1`：全部用例通过，覆盖 403/401/500、断网、租约耗尽、重复过期只派发一波 STOP、User-Agent 与 `SuiteApplication` 监督行为；即不打开开关时授权语义未变。
- `tests/activation/verify-license-bypass.ps1`：PASS。
- 关闭开关的 `.\gradlew.bat --offline assembleDebug`：退出码 0，9 个 Rust crate 因编译期环境变量变化全部重新编译，`BuildConfig.PIAOMIAO_LICENSE_BYPASS = false`，确认免激活开关不会因 cargo 缓存串到默认/朋友版构建。

## 未验收

- 免激活包在真实游戏中的模块启动、运行、停止与清理：本轮没有启动任何功能服务、没有连接游戏、没有发送业务请求，避免干扰当时正在进行的游戏会话。
- 与授权服务真实联通无关：免激活构建按设计不发起请求，因此不能据此推断线上激活是否可用。

## 构建环境注意

`build-native.ps1` 由 `powershell.exe`（Windows PowerShell 5.1）执行，无 BOM 的 UTF-8 中文注释会按 ANSI 解析并使脚本语法失败。该脚本需保持纯 ASCII；首次构建失败即由中注释引起，已改为英文注释后通过。
