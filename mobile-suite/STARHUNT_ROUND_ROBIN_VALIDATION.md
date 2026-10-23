# 找星忙碌识别与逐目标轮询（2026-10-16）

用户确认每颗星每轮尝试一次：确认忙碌即换下一颗，挑战等待窗口结束仍未开战也轮换；一轮全部尝试后间隔再找，自己战斗时暂停。

## 原因与协议证据

既有读取记录为东海渔村1010、8星、2次传送/2次挑战/跳过0；截图展示观战对话。未保存该次目标的原始8247响应，不能仅凭截图断言具体动作键。当前只读状态已停止且清理确认，不重放旧命令。

历史当前版本运行时导出 `.scratch/scene-entities-20261016/challenge-functions.json` 的8247 schema为 `npcId:int, shape:int, name:string, msg:string, btns:string`。handler字节码按 `|` 分组、`/` 拆字段，第一项为msg、第二项为id；clickNpcBtns字节码确认guard走观战、离开只关闭对话。旧代码只接受table，且要求每个按钮文字包含观战，漏掉字符串及退出项。

## 实现

- 按实际 `msg/id` 字符串结构和精确动作键识别，只含guard与可选离开且至少有guard时提前轮换；保留table动作键兼容。其他目标、混入攻击、反向字段、畸形或未知动作不按观战跳过。
- 按实例ID记录本轮已尝试目标，每轮每目标最多一次传送、一次挑战；本轮耗尽后至少等待5秒再重置。目标本地显示忙碌也记录为本轮已检查。
- 挑战12秒未确认开战时使用 `challenge_unconfirmed_rotate`，清除当前动作，下一次轮询选择剩余目标，不增加忙碌跳过计数。传送位置8秒未确认仍进入unknown停止。
- 本轮曾挑战后，匹配本人角色的迟到3584优先阻止后续发送；须本地战斗状态共同确认，响应已到但3秒仍未进入战斗则unknown停止。战斗结束后继续本轮剩余目标，命令/停止清除挑战归属。
- 保留租约、角色地图变化、协议、实例及3秒目标重绑等检查；不改Java UI、native、挑战参数或其他模块。

## 离线验证与候选包

独立scratch字符串观战红测先失败，修复后通过；挑战12秒未确认的新规则红测先在unknown失败，修复后在超时边界记录未确认、下一0.5秒tick传送到不同目标21，通过。合成按钮样本基于已核对协议与动作键，并非现场payload。

正式Lua runner七组场景全部通过，包括新增round-robin和更新后的pipeline轮间隔断言；主代理独立重跑通过。补充真实Lua验证table按钮与本人响应后始终未进入战斗的3秒停止路径，均通过。

无额外临时授权参数的完整assembleDebug通过。候选 `../artifacts/PiaomiaoHelper-2026.10.16.4-starhunt-round-robin.apk`，SHA256 `77EA7D619812309E82177E46607ECD997884A967505B0F0F4B793CEFD7180F3B`；v1/v2签名通过、10个native库、授权地址已配置。包内Lua与测试源码一致，SHA256 `498209999544675648E29350E54D1DD1302E2369585CACA39499E0C186350337`。

CodeRabbit最终实际完成Lua与三个正式测试文件的四文件审查，0 issues；此前三文件检查未含untracked新增用例，随后在隔离审查仓库纳入该文件后完整重审。计划与结果判断实际API均返回LOW_CONFIDENCE，status=not_run，主代理按本地协议、复现、测试与审查证据接手，不称判断通过。

候选已按用户明确授权覆盖安装，并进行了真实8星观察；详见下方实测边界。证据位于 `.scratch/starhunt-busy-round-20261016/`、`.scratch/starhunt-busy-round-tests.log`、`.scratch/starhunt-busy-round-build.log` 和 `.scratch/starhunt-busy-round-review.ndjson`。

## 设备实测与手动操作边界

用户授权覆盖安装及当前地图8星最多30秒测试，进入首场战斗提前停止。新绑定PID32697/startTicks4591315，地图1010东海渔村、角色901190。

- 首次采样时序号18已在运行，记录7次传送/7次挑战/跳过1，存在battle状态；用户明确确认手动点过开始。该累计不可归因于受控计时，已停止清理。
- 新只读序号19与20均为scan，计数0。一次尝试未成功写入run即停止，未发业务请求。随后确认新序号21/run后才启动受控采样，独立30秒停止watchdog已部署。
- 序号21采样：约1.13秒传送目标1126500，1.92秒挑战后目标被清除且跳过从0变1，随后进入round_cooldown；约8.00秒和14.79秒开始下一轮重试。只剩一个8星目标，因此不能现场证明不同实例间轮换，但证明观战动作识别及有界轮间等待已走通。三个挑战后均提前跳过，并非等待12秒未知超时。状态中文在PowerShell逐行采样中有乱码，路径根据挑战计数/目标清除/跳过递增与状态机唯一分支确认，不把乱码当原始payload。
- 约21.35秒后出现外部新命令22，27.14秒停止时累计4传送/4挑战/跳过2。停止后又发现23/24的新run命令，因此整段不是单次开始的独立验收。已再次发停止并保持游戏前台，最终序号24状态worker_stop、hooksRemoved/bindingCleared均true。此后不追加实战窗口。
- 受控采样阶段没有确认进入战斗；此前用户手动序号18捕捉到battle。不同目标轮换、挑战无回应12秒的现场分支和完整战后持续循环，仍未取得独立设备验收。

设备证据：live-identity.json、live-timeline.txt、manual-stop-confirm.png、controlled-command.txt、controlled-timeline.txt、controlled-transitions.json、controlled-stop.txt、controlled-final-status.txt，位于`.scratch/starhunt-busy-round-20261016/`。设备watchdog均已结束，测试部署临时文件清理后保留本地验收证据。
