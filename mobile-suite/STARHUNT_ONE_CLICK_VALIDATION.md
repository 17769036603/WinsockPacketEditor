# 一键挂机候选（2026-10-17）

现有四按钮版本的只读耗时已测：立即切回游戏三轮约1.72/0.82/0.80秒；助手前台停留3秒时等待到切回后才读出。未安装本候选，不能当作r3/r4设备验收；详见[STARHUNT_READ_TIMING.md](STARHUNT_READ_TIMING.md)。

## r4 启动响应优化（待复审、待设备验收）

针对“每次读取慢”，将一次性挂机请求等待SCAN确认期间的服务检查从1000ms缩短到250ms，页面刷新从1000ms缩短到250ms。非启动等待阶段按单调时钟限制后台完整检查不超过约1Hz，避免持续增加租约写入和通知频率。Lua地图扫描仍为500ms；传送、挑战和清理确认窗口不变。

这是轮询等待上限的缩短，单次服务检查相位最多减少约750ms；不是首次读取总耗时的实测结论。首次进程绑定、脚本加载和地图就绪耗时尚未测量。

r4服务入口15断言、集成契约33项通过，完整assembleDebug成功，v1/v2签名、授权地址、10个原生库及包内Lua与源码匹配已确认。CodeRabbit实际调用返回`rate_limit`，提示`waitTime: 42 minutes`，本次两文件提速差异尚未完成复审；未使用其他审查方式代替。

新增候选`../artifacts/PiaomiaoHelper-2026.10.16.4-one-click-hangup-r4.apk`，67,767,941字节，SHA256 `B184B8D8F1670283FECD1F070E1236A7AB576F7BA594BFF5E3DD9E0C68FCF9D3`。未安装、未发布；r3已复审候选保留。证据：`.scratch/starhunt-one-click-r4-build.log`、`.scratch/starhunt-one-click-r4-package.json`、`.scratch/one-click-service-r4/`。

## 操作与边界

页面改为固定1至999的目标星级输入和一个开始/停止挂机按钮，记住上次合法星级。星级选择不依赖当前地图是否刷出该星；运行期间不改级，停止后可重新选择。页面显示地图、目标数量和等待刷新/挑战/战斗等状态；显式开始后返回游戏，开页和刷新页面不会启动游戏或运行请求。

新 `ACTION_START_HANGUP` 在同一次服务请求内启动扫描，绑定新SCAN序号；只有该序号被worker确认、地图角色有效且状态新鲜时才提交一次RUN。RUN不要求已有该级候选；Lua会持续等待刷新。启动扫描确认阶段最多等待60秒，已开始RUN后的无星等待不受该60秒上限影响。

请求仅在内存中存在；停止、新手动操作、服务销毁、授权失效、worker退出或当前会话终态均清除。手动RUN继续使用原候选门禁。停止按钮直接结束读取和动作，并等待已有清理确认/重试流程；地图角色变化、重新绑定不恢复旧RUN。

## 验证

- Java模型：Command14、Status50、StopJournal12、HangupRequest16条断言通过。覆盖无候选、错误/陈旧上下文、unknown角色、gameProcessGone、取消、重复和60秒超时等。
- 真实Lua runner八组通过；新wait-refresh场景覆盖开始无8星→等待→出现新实例→一次挑战→战斗期间零发送→战后无目标继续等待→下一次刷新继续→停止后不再发送。
- Java集成源契约33项通过，更新了既有UI文案断言及用户新版导航图标构造参数；脚本以UTF-8 BOM兼容Windows PowerShell 5中文解析。
- 首轮CodeRabbit九文件报告1个major：新入口未启动读取。已修正，新增源码提取服务入口回归；同一测试对旧快照失败于“cold hangup must call the reader startup path once”，新源码通过15项断言。该测试使用真实方法与协作者stub，不代替Android设备验收。
- 修复后完整assembleDebug通过，构建期间全部app源码输入哈希稳定；v1/v2签名、10个native库、授权地址和包内Lua与当前源码匹配通过。

## 候选包

`../artifacts/PiaomiaoHelper-2026.10.16.4-one-click-hangup-r3.apk`，67,767,941字节，SHA256 `527E9969A12FE3EC91573C81CC01AA4B62610A6780DC373F29FBDD87D41EEAE8`。保持现有code10和候选版本标识，未发布；仍需安装和设备验收。

CodeRabbit第二轮复审提出2项minor：保留具体读取启动失败原因、测试JDK定位改用JAVA_HOME/PATH；均已修正，第三轮实际复审2个变更文件，0 issues。计划判断API返回LOW_CONFIDENCE/not_run，由主代理按本地证据接手；结果判断实际API evaluated/pass（置信度0.69，仅作参考）。首次有冷启动缺陷的临时候选未安装，已移除。

临时类文件：开发代理报告平台拒绝清理 `%TEMP%/codex-starhunt-selftest`，未绕过拒绝。根验证输出保留在项目`.scratch`供复跑。

证据：`.scratch/starhunt-one-click-review.ndjson`、`starhunt-one-click-r2-review.ndjson`、`starhunt-one-click-r3-build.log`、`starhunt-one-click-r3-package.json`、`starhunt-one-click-r3-review.ndjson`、`one-click-service-red/`与`one-click-service-green/`。
