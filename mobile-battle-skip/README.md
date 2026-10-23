# 战斗快进 APK

独立包名 `com.xnas.wpe.battleskip`，用于当前 x86_64 Root 模拟器中的 `com.gdoo.dhxy`。

桌面只有一个“战斗快进”图标，打开后选择“启动快进”或“停止快进”，发送请求后返回游戏。Rust worker 在模拟器内部连接游戏，在目标回合第一条指令解析前执行动画快进并直接完成回合收尾。停止后请保持游戏在前台，让游戏线程完成钩子恢复。

按钮页面使用平台 Java Activity，调用应用内部的 Start/Stop 别名；启动壳和持续控制器继续使用 Rust，启动壳由系统 `android.app.NativeActivity` 承载。实际进入游戏线程和调用游戏函数仍使用内置的 Frida Java 桥接脚本与 Lua 模板。Frida core 是现成 C 运行库，因此整个调用链并非全部由 Rust 实现。

APK 不需要桌面 Python、Node 或独立 frida-server 持续运行。首次启动需要模拟器授予 Root；请求已发送并不代表游戏已经开始快进。运行状态写在应用私有 `files/status`，详细错误写在 `files/worker.log`。

进程绑定检查包名、PID、启动时刻与 executable；Lua 绑定本方角色及战斗管理对象，在目标回合首次读取指令前快进并直接完成普通战斗回合，不捕获或调用原结束 callback。若 tick 到达时回合已经开始，则保留快进补救；结束包按战斗与回合防重，迟到的同回合 callback 不会再次发送。游戏线程暂时不响应时保留会话等待清理，清理确认后再恢复；业务校验失败时停止并记录错误。

## 构建

使用已登记的 Android SDK、NDK 27.2.12479018、JDK 17 和 Rust `x86_64-linux-android` target。`gradlew.bat assembleDebug` 会编译两个 Rust 产物并打包。

Frida core-devkit 17.17.0 Android x86_64 必须放在工作区根目录 `F:/项目目录/封包监听工具/.scratch/frida-core-devkit-17.17.0-android-x86_64/`。从 `worker-rust` 目录向上三级即工作区根目录。官方压缩包 SHA-256 为 `607efea78a55194cc78b34df7d1ffc7bb14ce74d1f348df134889579fe413f56`，来源：[Frida 官方版本资产](https://github.com/frida/frida/releases/tag/17.17.0)。

`worker-rust` 的产物是 PIE executable，打包时命名为 `libbattle_skip_worker.so` 以使用 Android native library 解包机制；它由 Root 直接执行，不通过 `dlopen` 加载。
