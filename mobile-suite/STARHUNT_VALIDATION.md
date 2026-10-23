# 自动找星候选验证记录（2026-10-16）

## 候选交付

- 包：`../artifacts/PiaomiaoHelper-2026.10.16.4-starhunt-candidate-r3.apk`
- 包名：`com.xnas.wpe.suite`；code10；`2026.10.16.4-starhunt-candidate`；仅 `x86_64`。
- 大小：67,766,514 bytes。
- SHA-256：`ADC15ED4A1F294EB457F9B32EBE729061A99BBED4F2C9D46910A8D22792EFA8C`。
- APK v1/v2 签名验证通过；包含10个 native 文件与两个自动找星脚本资源。构建保留既有公开授权服务地址。
- 用户明确授权更新安装并仅验证页面与读取后，已在 `emulator-5554` 覆盖安装 r3，包管理器确认 code10。未发布或启动真实自动找星循环；原有命名交付物保留。撤掉了本次产生的两份中间候选，交付只使用 r3。

## 功能与验收边界

统一助手第七页为“自动找星”。打开页面后需显式开启读取；显示当前地图的星级、名称、数量，并按星级数值排列。选择一个星级并点击开始后，按目标重新读取坐标，传送并等待匹配的位置确认，再发起一次挑战；战斗结束后继续扫描。只读扫描不发送游戏业务请求。

客户端可见战斗标记与目标观战入口仅用于保守跳过，不等于服务器占用状态确认。开战超时或未知结果会停止自动动作并显示未确认原因。停止找星等待命令确认；停止读取等待观察钩子和绑定清理。停止回执迟到、worker 已退出与服务重建有单独的持久化、超时和重试处理。

自动动作与暗雷移动互斥；新会话不会重放旧开始命令。页面显示和一次当前地图读取已设备验收；连续传送挑战、忙碌轮换及与战投/面板同时运行仍需设备验收。

## 设备只读验收

- 游戏进程保持 PID3083 / startTicks2122279。打开页面没有自动启动 worker；点击开启读取后 command 为 sequence1 / scan / level0 / map0。
- 17:49 读取到地图1007“大唐边境”、角色901190，`levels=1:2`、`levelNames=1:地狗星`；页面显示“地狗星（星级1）2个”。这是本次当前地图缓存快照，不证明目标仍能挑战。
- 读取和停止末态均为 `calls=0`、`teleports=0`、`challenges=0`。没有点击开始找星，没有执行传送或挑战验收。
- 切回游戏前台后读取更新；游戏在后台暂停时，助手保留上次结果并显示更新时间。持续后台刷新未确认，不能把旧快照当实时数据。
- 点击停止读取后切回游戏让清理执行，末态 `status=stopped`、`reason=worker_stop`、`acceptedSequence=2`、`hooksRemoved=true`、`bindingCleared=true`；进程列表已无 starhunt worker，页面显示“读取已停止，清理已确认”。最后留在该页面，读取与动作均停止。
- 设备证据：`device-starhunt-before.png`、`device-starhunt-result.png`、`device-starhunt-stopped.png`、`device-reading-status.txt`、`device-reading-command.txt`、`device-stopped-status.txt`。

## 已完成验证

- Java 自检：命令14、状态34、停止记录12项断言通过。
- Java 集成契约：34项检查通过。
- Lua 离线状态机：pipeline、timeout-and-guards、anlei-conflict、lease-expiry 四个场景通过，覆盖单次发送、匹配位置确认、战斗等待、忙碌轮换、身份/地图变化、停止、新会话基准、双心跳失效与恢复。
- 原生：最终 Android target 的 static 配置 `cargo check`、`cargo fmt --check` 和 JS 语法检查通过；最终 shared 模式完整 APK 离线构建通过。未运行 Rust 主机测试，也未把先前 static 构建当作最终 static 完整链接验收。
- CodeRabbit：首次31文件提出5项；修复复审14文件提出1项；最后3文件修复复审完成，`CodeRabbit raised 0 issues.`。原始输出保存在任务证据目录中。没有使用人工审查替代 CodeRabbit。
- 判断代理：计划阶段 API 返回 HTTP429；结果阶段修正输入格式后实际调用 API 返回 HTTP200，但低置信度，均为 `not_run`。未视为判断通过，由主代理按上述本地证据确认候选交付边界。

## 证据

任务证据目录：`F:\项目目录\封包监听工具\.scratch\starhunt-feature-20261016`，包含实施契约、独立审查快照、三轮审查输出、最终构建日志、候选元数据和判断输入/输出。

本次 Java 自检的 `starhunt-stop-java-*` 临时目录仍在系统 TEMP。安全工具拒绝清理，返回 `blocked by policy`；未绕过策略删除。
