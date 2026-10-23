# 暗雷助手 APK

独立包 `com.xnas.wpe.anlei`，当前版本 0.3.0，面向 Root x86_64 Android 模拟器中的 `com.gdoo.dhxy`。用户启动后可授予悬浮窗权限，在游戏上方拖动小窗查看“持续运行”或“战斗暂停”，并可直接停止。worker 以 PID、`/proc/<pid>/stat` 的 startTicks 和 executable 复核进程身份，游戏重启后重新发现并绑定。worker 持有单实例锁。UI 服务每两秒更新租约，worker 将租约与 heartbeat 镜像到游戏可读的 cache；任一超过 5 秒未更新，Lua 即停止调度并清理本模块资源，worker 收到清理确认后卸载 Frida 会话。

发送前要求当前本地角色、管理器与角色对象可用且已绑定，地图加载完成并且不在战斗中。地图编号和自动巡逻状态不作为门槛；每次调用前检查角色与地图身份仍有效。游戏线程每帧检查战斗状态，非战斗时最多每 100ms 调用一次 `MapMgr:sendPlayerPos`，不设每批或总次数上限；进入战斗立即暂停，战斗结束后的首个有效帧立即恢复调用。零间隔全局 scheduler 按 Cocos scheduler 每帧回调使用；约 60 FPS 时典型检查间隔约 16.7ms，帧率或游戏卡顿会延迟检查，不承诺 1ms 硬实时。位置坐标从固定十点序列 `(90,31)` 到 `(94,31)`，再从 `(94,32)` 到 `(94,36)` 轮流取用。这些坐标在已验证地图上做过验证，不随角色原点变化；本模块不检查其他地图的地形可通行性。位置同步计数表示本脚本调用了游戏函数，不表示网络发送或服务端接受；游戏函数自身条件可能直接返回。

本模块不包装游戏的 `netconn.send`，也不移除其他脚本设置的 hook。停止只取消本模块的 scheduler；Frida detach 需等到 Lua 清理确认。本机 root、游戏客户端与服务器的实际行为仍需在新鲜游戏进程中验收。

## 构建

从本目录运行 `gradlew.bat assembleDebug` 或 `build.ps1`。构建复用已安装的 Android SDK/NDK、Rust Android target 和工作区已有 Frida core-devkit，不下载依赖。
