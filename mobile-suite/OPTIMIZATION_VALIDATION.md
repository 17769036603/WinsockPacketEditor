# APK 独立优化验证记录

本轮工作在独立副本中完成；原目录另一会话的定时方案尚未合并。本文的设备、构建和源码状态分别记录，不互相替代。

## 实现范围

- 定时任务增加逐目标执行记录，区分派发失败、等待本地确认、已确认、超时未确认和部分结果。观察器不自动重发；只有存在本次关联证据时才确认模块本地状态。
- 设置页显示完整版本、构建号、授权模式、架构和复制诊断入口。诊断输出限定为完整版本与构建号、授权模式、ABI、模块状态枚举及新鲜度、最近调度聚合结果与分钟时间；不复制游戏身份、状态详情、原始日志或授权凭证。
- 显示文件缓存使用 500ms 刷新间隔、5s 硬过期、后台有界读取与同键合并。缓存不作为业务启动、动作发送、授权或恢复证据。宿主侧栏按显示值变化更新。
- 交付工具比较实际 APK 与显式基准的包名、版本、签名和架构，拒绝覆盖既有目录，写入独立清单、哈希和安装说明。定时方案合并状态单独记录。

## 离线验证

最终稳定入口退出码为 0，17 组适用回归通过，1 组旧断言单独记为 `OBSOLETE_EXPECTATION`。诊断 41 项、缓存 140 项、调度 21 项、战斗状态 11 项、速度停止 25 项、门禁 21 项断言及已有状态/授权/租约回归通过。交付工具另通过 23 项检查。最终汇总位于任务目录 `stability-verified/stability-report.md`；每项完整 stdout/stderr 均独立保留，普通失败仍使聚合进程返回非零。

快照模型测试推进 72 小时逻辑时间，覆盖 3,456 次模块/目标转换、10,828 项断言，没有实际等待 72 小时。缓存实测 101 次同文件请求仅产生 1 次物理读取；模块/文件隔离和目标切换的旧结果拒绝通过。未知宝宝技能回退场景推进 80 个模拟回合；它验证既有代码回退普攻且不发送未知技能，并非真实游戏服务器验收。

原 `unknown-target.lua` 仍保留旧断言“未知宝宝技能时整个回合不发送”。启动时的生产脚本已有回退普攻逻辑，因此旧断言与既有行为不一致；原失败被保留，不改生产脚本以迁就测试，也不计作该旧场景通过。新场景独立记录现有行为。

## 包体实验

使用同一 .NET Deflate 测量方式，当前 core 为 151,704,984 bytes（strip 后）、58,705,734 bytes（压缩后）；最佳静态实验候选为 111,185,664 / 46,133,001 bytes。分别减少 40,519,320 bytes（26.709%）和 12,572,733 bytes（21.417%）。这不是实际重打包 APK 的压缩差值。

七个 worker/query 的必需动态导入交集、ELF64 x86_64、SONAME、依赖、无 TEXTREL 及 API23 reallocarray 检查通过；仅保留 25 个导出的实验库尚无 Android 装载或真实模块运行证据，未进入本轮候选。取消 whole-archive 本身没有额外明显收益。

可重复脚本：`tools/Measure-FridaLinkExperiment.ps1`；有效原始结果位于任务目录 `size-lab/run-20261018-154911-13e0474c/experiment-result.json`。首轮误测已标 `invalid_superseded`；清理其大型二进制时执行策略拒绝，未改用其他方式绕过，相关输出保留。

## 构建、交付和审查

原生预编译与 11 个 ELF 的架构/依赖检查已通过。完整 Gradle 编译首轮发现宿主控件缓存字段类型不一致，修正后完整离线 `:app:assembleDebug` 通过（36 个任务）；CodeRabbit 修正后再次完整构建通过。最终 APK 为 `2026.10.18.2-friends-open-v1-test`、code15、67,939,564 bytes；SHA-256 为 `BDA5A0F0C0EEC1422CDA80AF37B12CA73010260F389AEC4F84892F5A76CB1406`。包名 `com.xnas.wpe.suite`、x86_64、minSdk23/targetSdk35。交付工具将再次与 code14 安装包快照比较实际证书和元数据。

CodeRabbit 首轮审查覆盖 53 个变更文件，提出 4 个 minor issues：快照负例应使用未登记的模块 ID、两处测试 JDK/路径解析需可移植、战斗跳过 provider 应先处理不支持的目标。四项均已按实际源码修正，复审覆盖后续 7 个文件，返回 **0 issues**。稳定入口另修正了失败输出捕获和真实退出码；只有精确命中过期断言字符的旧场景可记 `OBSOLETE_EXPECTATION`（仅还原 PowerShell 排版空白，原始双流保留），其他失败仍阻止通过。实际生产函数的 PASS/普通失败/过期断言和聚合退出码负例验证通过。业务脚本、原生业务源码和 AndroidManifest 相对隔离基线均未改变。

计划判断已实际调用 Decisions API，返回 `LOW_CONFIDENCE/not_run`，不计通过。结果阶段首次输入被本地 schema 校验拒绝，修正为契约要求的字符串摘要后实际调用 API，HTTP200 返回 `LOW_CONFIDENCE/not_run`，同样不计通过；主代理根据本地测试、构建和 CodeRabbit 证据接手。

本轮交付目录为任务目录 `delivery/code15-test/`，含最终 APK、`manifest.json`、SHA-256 文件及中文安装说明。清单记录实际包元数据、签名、最终隔离源码提交及摘要；`integrationStatus=pending_schedule_merge`、`deviceAcceptance=not_run`、`installed=false`、`published=false`。原始证据另见任务目录的 `coderabbit-review.ndjson`、`coderabbit-rereview.ndjson`、`stability-verified/stability-report.md` 和 `candidate-metadata-final.json`。

部分本轮临时编译目录和首轮无效实验二进制的递归清理被平台策略拒绝（`blocked by policy`），文件按限制保留，没有改换工具绕过。它们不进入 APK、源码摘要或补丁。

## 设备与集成边界

本轮只读保存了 code14 安装包快照用于签名预检；另一会话仍可能修改设备，快照不表示实时状态。本轮未安装候选、重启助手或游戏、启动业务 worker、修改游戏内存、发送游戏请求或发布网站。

副本仍是每日固定时间模型，原目录另一会话已改整点/半点模型。两者的数据契约需后续合并，`integrationStatus=pending_schedule_merge`；即使 Android 包名、证书、版本条件通过，也不能据此声称应用数据兼容。设置页实际显示、真实长时运行、停止恢复与服务端循环结果均未确认。
