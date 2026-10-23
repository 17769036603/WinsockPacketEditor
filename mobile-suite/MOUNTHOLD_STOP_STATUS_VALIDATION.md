# 角色速度停止状态修复候选r6b（2026-10-17）

## 原因与修复

截图正文为“已停止，未修改速度”，侧栏仍显示“清理中”。已定位可复现路径：没有workerThread、journal明确不存在且无未解决恢复状态时，requestUserStop调用finishService把active清为false，但未设置恢复完成标记；之后onDestroy因为stopping/userStopRequested为true且sawRecoveredState为false，把active重新设为true。侧栏又按正文含“停止”映射STOPPING，因此可持续显示清理中。

修复仅涉及角色速度模块两处Java源码：在该既有安全无变更分支标记terminal/recovered，再结束服务；侧栏优先识别恢复失败、未确认、未能确认及保留恢复记录为UNKNOWN。仍active但正文确认已停止时显示WAITING/等待任务退出，不提前宣称worker已退出；确认inactive且无未解决状态时为STOPPED，宿主现有逻辑会隐藏该标签。终态只精确匹配两条已确认文案的摘要，允许原worker实际追加的冒号和详细原因；未确认检查仍优先基于完整正文。

journal存在、无法检查、blocked、旧恢复未解决或worker仍在时继续原恢复流程；未修改速度数值、Rust worker、布局或其他模块业务规则。

## 验证

新增verify-mounthold-stop-status.ps1，从真实源码提取requestUserStop、finishService、onDestroy、currentJournalPresent、consumeWorkerLine、getModuleStatus及completionSummary，用最小协作者stub检查生命周期与状态。旧源码首轮20断言中6项失败，包含onDestroy重新active、已停止误分类为STOPPING、未确认恢复漏判；修复后20项通过。主代理以独立旧源码快照复跑红、当前源码复跑绿；脚本不替代真实Android生命周期验收。

CodeRabbit首轮三文件审查提出1项minor：实际RECOVERED状态会追加详细原因。新增测试以真实consumeWorkerLine生成stopped_without_changes和old_journal_restored状态，两条均先红；摘要识别补齐后25断言通过。journal存在/null、blocked、旧journal未解决、活worker保护均在通过项中。

Java集成契约33项通过，最终完整离线assembleDebug成功，构建期间app/src所有输入哈希稳定；v1/v2签名、授权配置和10原生库通过，包内与原生嵌入的Lua仍匹配此前r5扫描提速源码。

CodeRabbit修正后两文件实际复审完成，0 issues。计划判断已实际调用API，返回LOW_CONFIDENCE/not_run，主代理按本地红绿证据接手；结果实际API返回evaluated/pass、置信度0.72，四项验收概率0.97/0.97/0.97/1.0，仅作参考。后续设备页面验收见下方，实际启用后停止路径未验收，本轮未启动速度保持或修改游戏速度。

## 候选

`../artifacts/PiaomiaoHelper-2026.10.16.4-stop-status-r6b.apk`，67,767,333字节，SHA256 `B3D2DA7F24B1ECE27A70F317487325A96C55C612EDFBB1C2D82D040DA287958A`，已安装、未发布。该统一助手候选保留此前一键挂机和r5扫描提速源码，不覆盖本地旧候选文件。

首轮r6临时候选未安装，已由r6b替代并移除，保留构建、测试和复审证据。

## 覆盖安装与页面验收

用户明确回复“安装替换”后，于2026-10-17设备本地12:22:49执行adb install -r成功；未卸载或清数据。安装后base.apk的设备sha256sum与上述r6b哈希完全一致，versionCode仍10、versionName仍为候选标识。

打开角色速度页面，截图确认侧栏“清理中”消失，“启动保持”启用、“停止并恢复”禁用，顶部“已授权”保留，原输入设置保留。安装前后模块目录均无当前mount_hold.journal，仅原有expired存档，未改动这些存档。本轮没有点击启动或停止功能，也没有修改游戏速度；未覆盖真实启用后停止的Android恢复路径，不能以空闲页结果替代这一动态验收。

现状态：r6b已安装，未发布。页面证据位于根`.scratch/mounthold-r6b-before-install.png`与`.scratch/mounthold-r6b-installed.png`，安装元数据见`.scratch/mounthold-stop-r6b-package.json`。

证据在根`.scratch/`：mounthold-stop-r6b-build.log、mounthold-stop-r6b-tests.log、mounthold-stop-r6-red.log、mounthold-stop-r6-review.ndjson、mounthold-stop-r6b-review.ndjson、mounthold-stop-r6b-package.json、mounthold-stop-r6-oldsource/。主仓库index未改，审查使用隔离快照目录mount-stop-status-review/。
