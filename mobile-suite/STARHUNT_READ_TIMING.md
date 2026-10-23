# 只读开启耗时实测（2026-10-17）

## 结论

现有四按钮版本在大唐边境开启读取后立即切回游戏，三轮首次观测到新会话有效地图数据分别为1.7226、0.8178、0.7974秒。三轮均读到地图1007、1星8个，并通过助手页面截图确认列表显示。此次未安装r3/r4，不能据此声称新版的提速效果。

第四轮保持助手前台，3.1357秒后才发起切回游戏；切回请求返回于3.2038秒，有效地图结果于3.5902秒首次被采样观测到（相差0.3864秒）。切回前虽已观测到会话输入文件与Frida映射，但未取得本轮有效地图数据。该对照支持“游戏后台执行暂停导致等待”的解释；没有测量各游戏线程，不能认定这是所有慢读场景的唯一原因。

一键版本已有显式开始后返回游戏逻辑，尚待安装验收。继续减小地图扫描周期无法解决游戏线程暂停造成的等待；本轮不再修改生产代码。

## 范围与测量方式

- 使用当前已安装四按钮版本的“开启读取”，未安装或覆盖任何APK。点击前核对新截图和上一轮清理结果；每轮新SCAN序号分别25、27、29、31，停止序号26、28、30、32。
- Windows单调计时从ADB点击请求发起开始，包含点击派发与切换Activity；通过ADB读取状态/命令及只读文件/映射采样。上述时间是首次观测上界，包含ADB与采样开销，不是内部函数精确执行时间，也不是页面像素刷新时间。
- 每轮都在同一新鲜进程身份上开始和结束：PID4525、startTicks9149389、exe `/system/bin/app_process64`；每轮开始无已有Frida-agent映射。
- 仅SCAN，无RUN，无传送或挑战；每轮新序号状态的calls/teleports/challenges均为0。最终序号32为stopped，hooksRemoved与bindingCleared均true。游戏留在前台。
- 会话输入文件/映射只能提供阶段观测点；未单独测量Frida attach、脚本编译、脚本加载和Lua扫描函数的内部用时，不能把这些观测点直接当作各函数耗时。

## 证据

根目录`.scratch/measure-starhunt-read.py`为本轮临时计时脚本。采样数据为`.scratch/starhunt-read-timing-01.json`、`02.json`、`03.json`、`04-helper-foreground.json`（后几项均保留相同`starhunt-read-timing-`前缀）。最终停止状态为`.scratch/starhunt-read-timing-final-status.txt`。截图为`.scratch/starhunt-timing-ready.png`、`starhunt-timing-reading.png`与`starhunt-timing-stop2.png`至`stop4.png`（后三项保留`starhunt-timing-`前缀）。
