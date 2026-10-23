# Battle loop Lua sandbox tests

从模块根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\battle-loop-sandbox\run-tests.ps1
```

运行器通过 PowerShell P/Invoke 调用本机 Lua 5.4，不需要安装 Lua CLI、Node 包或访问 Android 设备。默认 DLL 路径为 `C:\Users\Administrator\AppData\Local\Programs\darktable\bin\lua54.dll`；如机器上没有该文件，测试会明确失败。

`fixture.lua` 提供隔离的虚拟 scheduler、netconn、ProtoFunc、时钟、角色/战场/自动技能对象和内存文件。每个行为场景在独立 Lua 状态内执行，不连接游戏进程、不加载 Frida，也不发送真实网络数据。覆盖人物和宠物按每回合当前配置选技能、无响应 200ms 重试、拒绝后 50ms 重试、成功 ACK 不重发、3585 后不重发旧动作但接受晚到成功 ACK、playover 按 server round 单次发送、未知技能目标不发送、宠物缺失、offPending 时人物身份变化后不发恢复请求并清理自有 hook/binding（restoreConfirmed=false）、lease 过期清理、ACK 缺少 round 的兼容行为，以及 stop 对自有 hook/timer/binding 的清理。

`verify-status-contract.ps1` 静态检查 Lua 状态字段经过 Rust worker 并到达 Android 主界面。该检查不代替 APK 构建、签名验证或真实设备界面验收。

