# 地图扫描提速候选r5（2026-10-17）

## 改动

地图扫描注册间隔从500ms改为250ms，tick缺少dt时采用同一个间隔常量。首加载立即扫描保持不变；因此缩短的是后续扫描与状态检查的等待，不表示首次连接游戏的总耗时减半。

候选解析先检查标题是否匹配地煞星格式、沿用原有level条件，再读roleId、mapId、posX、posY、name。普通NPC不再读取这五个详细字段。不新增跨轮缓存，保留全部等级统计、实例去重、场景NPC优先及动作前重新核对。

r5包含一键挂机和r4的启动确认/页面250ms响应；非启动等待阶段完整服务检查仍限制约1Hz。无星继续等待刷新；传送确认8秒、挑战确认12秒、整轮冷却5秒及停止/身份/租约保护均未调整。

## 离线证据

- 独立测试代理先在旧生产Lua上验证两条红测试；主代理用同一旧快照独立复跑，cadence因注册间隔0.5秒超过250ms失败，filter因普通NPC额外读取5个详细字段失败。
- 新源码下十组正式Lua状态机测试通过。新增测试按生产注册间隔驱动保存的回调，确认新目标下一拍可见；11.75秒仍等待挑战，12秒记未确认轮换；4.75秒仍保持整轮冷却，5秒重新开轮；nil-dt下31拍仍等待传送，第32拍达到8秒后unknown且不自动重发。其余八组涵盖观战轮换、地图/角色变化、租约、停止和刷新后继续。
- Java集成契约33项、真实服务入口提取15条断言通过；本次不修改Java行为。
- 完整离线assembleDebug成功。v1/v2签名验证通过，10个原生库、授权服务地址检查通过；包内Lua与源码完全一致，并额外确认原生starhunt worker嵌入的是本次Lua全文。

这里的250ms行为来自离线调度模型，不是设备宿主真实墙钟、游戏帧率或CPU测量。更频繁的扫描会增加执行次数，早筛减少普通NPC字段读取不能据此推出总CPU一定下降。本轮未安装、未连接游戏或发起实战。

## 候选与审查

新增候选`../artifacts/PiaomiaoHelper-2026.10.16.4-scan-speed-r5.apk`，67,767,333字节，SHA256 `815D72702BC2F62953DF3C5D81212242A7350B82EEE40BD0CAD67AC0328DF4A5`，未安装/发布。已审r3及待审r4包保留。

CodeRabbit累计七文件实际审查完成，0 issues；包含本次Lua与四个测试文件及r4之前未完成复审的两个Java文件，全部列在reviewedFiles中。计划判断实际API返回LOW_CONFIDENCE/not_run，由主代理依据限定范围和红绿测试证据接手。结果判断实际API返回evaluated/pass，置信度0.71、四项验收概率0.95/0.94/1.0/1.0，仅作参考，不替代设备实测或安装授权。

证据位于根目录`.scratch/`：`starhunt-scan-speed-r5-build.log`、`starhunt-scan-speed-r5-tests.log`、`starhunt-scan-speed-r5-package.json`、`starhunt-scan-speed-r5-review.ndjson`、`starhunt-scan-speed-red-scan-cadence.log`、`starhunt-scan-speed-red-scan-filter.log`、`one-click-service-r5/`。
