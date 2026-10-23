# 激活授权候选验收记录（2026-10-16）

## 范围

仅 `mobile-suite` 整合版，包名 `com.xnas.wpe.suite`，code 6 / `2026.10.16.0-activation`，x86_64，minSdk 23 / targetSdk 35。旧独立 APK、已安装版本、游戏和外部服务器没有被替换、部署或操作。

## 已执行验证

| 验证 | 结果与可复现入口 |
| --- | --- |
| 服务端单元/集成 | `cd licensing-server; pnpm test`，最终修复版 18/18 通过；真实HTTP到期续期场景见 `tests/activation-integration/server-expiry-renewal.mjs` |
| 真实本机 HTTP | 同安装并发会话均有效，跨安装409；解绑/停用撤销全部会话；Android规范化后的无连字符小写码激活/验证200 |
| 可信代理边界 | 默认忽略转发头；显式信任时仅loopback、单值合法X-Real-IP；无值/重复/列表/非法IP拒绝，管理路由不采纳转发身份 |
| Java租约 | `tests/activation/LicenseLeaseWindowSelfTest.java`，7项边界断言通过 |
| 安装显示摘要 | `InstallationHintSelfTest.java`，3项断言通过；独立Java/Node/HTTP fixture均为 `CFF45713079D`，列表不暴露原凭证 |
| Java生命周期/网络 | `tests/activation-integration/Run-ActivationJavaStubs.ps1`通过；首次未激活冷启动0服务；500/断网保留session+租约；activate401/403保留旧授权且0 STOP；verify401/403清授权并请求安全STOP |
| 服务入口 | `tests/activation/verify-service-gates.ps1`通过；5服务、STOP放行、2重启门禁和恢复专用参数契约 |
| 原生共享租约 | `cargo test --offline --locked --manifest-path tests/activation-native/Cargo.toml`，最终修复版3单元+5集成通过，测试代理独立重跑通过 |
| 快进Lua | `tests/activation-native/run-battle-skip-lease.ps1`，6种期限/失联场景；首次与独立回归通过 |
| Android原生编译 | 8个原生manifest Android target check通过；本轮状态恢复修复后3个变动manifest再次check通过 |

JVM stub 使用真实授权/应用类和模拟 Android 平台接口，不证明设备系统服务调度；部分原生集成检查是入口/顺序的源码契约测试，不等同于运行真实游戏。Windows 主机不能直接编译 mounthold 项目的既有 `std::os::unix` 单测；本轮 Android target 检查通过，但不能把它说成该项目全部单元测试通过。

## 代码审查

使用项目要求的 `coderabbit:code-review`。首次因隔离仓库未配置默认基线而启动失败；显式指定 `--base main` 后审查完成，51个选定文件，提出7个issues。已按实际控制流核实修复：

1. 激活码格式同时接受分组与APK规范化形式，并增加真实HTTP回归。
2. 代理后按明确配置的可信客户端IP限流，默认不信任转发头。
3. 战斗worker只有未尝试attach或已有清理确认时才能发布clean stopped；缺证据保持未确认。
4. 速度恢复增加严格 `--recover-only true|false`，无journal恢复可无变化结束，新启动仍需授权。
5. 输入错误新码不销毁现有有效授权，verify明确拒绝才失效。
6. 两个服务重启worker前复查租约。
7. 测试脚本优先JAVA_HOME、回退PATH，不固定单台机器JDK。

审查使用隔离临时仓库。suite原本为untracked，只有5份native文件有可信旧版副本；其余选定现有文件按完整文件作为新增内容审查，不能宣称覆盖了精确历史diff。没有发送原始日志、封包或用户凭证。

一次复审完成后仍提出4个issues（1 major、3 minor），随后已修复并做针对性验证：过期未吊销会话保留30天、明确 `403 + LICENSE_EXPIRED` 停止使用但保留token供管理员续期；激活409显示安装绑定冲突；暗雷过期启动分支对活worker走安全停止；快进最后租约检查早退释放Frida资源及本run缓存。服务端18项、HTTP到期续期场景、Java错误码矩阵和native3+5均通过。按项目最多一次复审的流程，没有第三次CodeRabbit审查，因此不能宣称CodeRabbit最终0 issues；最后四项修复的依据为针对性测试，且只做了本地候选验收。

自然到期与吊销不同：到期30天内管理员延长后，原会话可再次验证；只恢复授权，不自动启动功能。解绑/停用会撤销旧会话，后续续期不恢复旧token；超过会话保留窗需重新输入原码。

判断代理的计划阶段实际调用API，但返回低置信度 `not_run`，未算作通过；测试和CodeRabbit证据单独记录。结果阶段第一次输入因summary类型不匹配被拒绝；按允许的一次修正改为字符串后实际调用API，HTTP429，仍为 `not_run`，没有算作通过或继续重试。

## 最终候选

完整离线 `:app:clean assembleDebug` 通过（51秒），包含8个native库。第一次clean因一个生成文件被占用失败，停止本轮Gradle daemon后重试成功。Debug v1/v2签名通过。

`artifacts/PiaomiaoHelper-2026.10.16.0-activation-candidate.apk`，255,294,291 bytes；SHA-256 `7F6F89775A159ADFD862DD8CB56C9F094C02D71AA47B2E680A17E12E217DE693`。独立命名、不覆盖旧交付物；服务地址为空，尚不可直接分发使用。相较本轮增量包去掉约50MB冗余；进一步共享库瘦身仅在隔离原型，见 `SIZE_OPTIMIZATION.md`。

## 分发前仍需完成

- 确定并部署实际HTTPS授权地址，仅公开两个客户端POST路由；管理端留本机。启用可信代理时由代理覆盖X-Real-IP，不能透传客户端原头。
- 以真实地址重新构建APK；本轮默认地址为空，必须fail-closed，候选不能直接发给朋友使用。
- 指定设备安装后验证/proc读取权限、Root及后台生命周期、激活和到期后的六模块停止/恢复。没有执行ADB或真实游戏操作。
- Root/模拟器复制或客户端篡改不在绝对防破解承诺内。短租约控制正常客户端，临时断网最多使用已有租约至耗尽。

快进Lua在worker意外死亡后通过下次Hook回调检查期限；无回调时不会主动执行清理，下一次回调先恢复/透传，不继续辅助。清理失败不能据此宣称Hook已移除。

本轮空运行产物 `licensing-server/data`、`node_modules` 已核对无授权和会话（计数均0），本机8787无监听。递归删除被环境执行策略拒绝，因此保留；没有更换工具绕过限制或修改系统安全设置。
