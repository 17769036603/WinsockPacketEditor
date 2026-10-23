# 坐骑速度保持 Root shell 输出关闭：诊断证据

用户当前约束：只查看与源码修复，不重启或停止正在运行的软件/预设，不执行设备内存操作。

## 现场只读证据

- 2026-10-06 17:56:42，本次 runId `a68a3d84-c25a-429c-baa5-be9c3cd86b70` 在此前多次写入并读回 5000 后出现 `Android Root shell 已关闭输出。`；随后恢复快照失败，`runEnd` 为 `recovery-blocked / retained / error`。因此本次没有进入只允许 `failure-recovered / deleted` 的自动重启路径。
- 这证明 Root 通道曾经可用，不能根据最后错误认定用户未开启 Root。旧日志没有保留实际 guard 退出码，具体是哪项保护或是否有外部通道关闭，仍未确认。
- 恢复记录只读保留；本轮开始 SHA-256 为 `1B88B05896B07FDB2AF636A63D7BD9CD79E64A3C61295A12A7C373424CD4D0F2`。
- 数据库本轮开始 SHA-256 为 `E1035DDC324919024FFF146DBB50DA62F42574B47E29A183BAD83A81C26162B1`。
- `.4` 应用 EXE SHA-256 为 `3B3B30CD222343D456BCC8039A59A1FD841D65E71441DA41471FCB95467EF148`；包内速度 Python SHA-256 为 `AA449A0B4B814342FD1566CBECFA455B6A2ADEEF133E00433E0686C8782FC3C6`。

## 可执行缺陷证据

生产 `AdbTransport.write` 在打开进程内存、读取 stat、校验启动 tick、比较当前 TValue 失败时执行 `exit 51/52/53/54`。C# 持久 shell 的命令包装是 `set +e`、原命令、读取状态和打印结束标记；显式 exit 在顶层会直接结束这个 shell，结束标记无法返回。读取到 EOF 后只报告输出关闭，丢失了真正的 guard 退出码。

旧命令真实 POSIX shell 离线复现：运行 `.scratch/root-shell-repro-long-lived/long_lived_shell_repro.py`，得到 `bash_exit_code=54; cas_status=None; bash_alive_after_write=False; fixture_bytes=b'ABCDEFGH'`。写入比较失败，零写入，但父 shell 被结束。夹具不调用 ADB，保留在该明确的调试目录作为红基线证据。

最小修复方向：仅 Root broker 的整条受保护写入命令用 `sh -c` 和标准 shell quoting 封装；独立 adb/run-as 兼容路径保持原样。不可直接用括号 subshell：其中 `$$` 仍是父 shell PID，而 fd 3 位于子 shell，会破坏 `/proc/$$/fd/3` 绑定。保留保护码、比较写入和恢复 journal 规则，不盲目重试写命令，不关闭共享 Root 会话。

计划判断 API 实际返回 `evaluated / uncertain`，仅作为参考；正确性以正式同一夹具的旧源码失败、新源码通过及既有回归为证据。源码修复不等于正在运行的软件已经应用修复。

## 完成验证

- `tests/MountSpeedRootShellIsolationRegression.py` 加载旧 `.4` 源码时预期失败：父 shell 结束、marker 缺失，状态 -1；当前源码 2 项测试通过。内存打开错误接受 shell 实际的 1/2/51，stat/tick/CAS 严格检查 52/53/54，全部保留零写入、父 shell 存活和后续实际读取断言。
- `.scratch/root-shell-guard-fix-20261006/run_hold_root_shell_integration.py` 通过：真实首个 CAS 54 失败后，实际读取目标 5000、CAS 恢复 296.7 并真实读回；`failure-recovered / journal deleted / parent shell alive`。只有临时本地数据和唯一角色快照，没有 ADB。
- 既有速度 Python、保持机器人 PowerShell、成长率 Python、伙伴面板 Python 回归通过。
- CodeRabbit 首轮 `review_completed`，三个 `reviewedFiles` 与本次范围一致；一项测试错误码兼容问题已处理，复查为 0 issues 但服务注明没有新详细审查。
- 最终只读核对确认数据库、真实 journal、当前 `.4` EXE/速度脚本的四项 SHA-256 与本轮开始一致。没有应用到用户当前运行的程序。

## 桌面发布

- 用户随后要求立即修复，沿用本机 ClickOnce 热更新授权；构建/签名并发布 `.5`。Release 0 errors, 4 existing EasyHook warnings; all three Mage manifests verified. Source and package/deploy speed-script SHA-256 match. Preset database hash did not change.
- Existing Desktop `.lnk` previously pointed directly to `.4` EXE. It was backed up and updated to the stable `.application` entry, with `.5` as its icon. Stable entry now contains the signed `.5` deployment.
- Three desktop app processes remained at their previously observed PIDs/start times; none was restarted. The next normal launch through the desktop shortcut will load `.5`; live application/device behavior remains unverified until then.
