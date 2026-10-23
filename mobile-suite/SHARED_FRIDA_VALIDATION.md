# 共享 Frida 瘦身测试版（2026-10-16）

> 本文记录 code7 瘦身测试版的历史测量。当前自动找星候选为 code10 / `2026.10.16.4-starhunt-candidate`，包含七个功能入口、六个 Frida worker，shared 模式共10个 native 文件，static 模式共9个。以下包体、设备与授权记录不代表当前候选的验收结果；当前候选已在 emulator-5554 安装并完成页面、只读扫描和停止清理验收，未完成游戏内找星循环验证，详见 STARHUNT_VALIDATION.md。

## 已完成的实现

五个真实 worker（暗雷、战投跳过、战斗快进、金蝉、战斗面板抗性查询）改为动态链接同一份 `libfrida-core.so`。速度保持、战斗面板主读取和快进 NativeActivity 启动器保留各自原生实现，共享包共9个native文件。六个功能入口、游戏业务、授权租约和停止恢复规则保留；未更改独立 APK。

共享库从已有 Frida 17.17.0 devkit 构建，具有确定 SONAME，并补齐API23缺失的、带乘法溢出检查的reallocarray兼容实现，没有新增外部依赖。五个worker使用同一构建入口，套件默认shared，`-PpiaomiaoFridaMode=static`可回退静态构建。

五个worker增加显式 `--suite-native-self-test [--hold-ms 0..60000]` 入口，仅调用Frida初始化、版本查询、驻留和反初始化。它在参数解析、状态目录、授权、Root、设备枚举及游戏附加前返回；不能用作启动游戏功能或绕过授权。

## 包体实测

两包均为code7 / `2026.10.16.1-slim`、x86_64、min23/target35，使用相同业务和诊断源码完整离线构建。

| 项目 | 静态对照 | 共享测试版 |
| --- | ---: | ---: |
| APK bytes | 255,308,189 | 66,149,040 |
| native文件数量 | 8 | 9 |
| native未压缩bytes | 615,505,208 | 161,548,976 |
| native压缩bytes | 254,551,684 | 65,392,300 |

完整APK减少约74.1%。共享测试版已通过Debug v1/v2签名检查，唯一launcher与六功能入口保留。此处native未压缩大小是文件载荷，不是运行内存或设备实际安装占用。

交付：`artifacts/PiaomiaoHelper-2026.10.16.1-slim-candidate.apk`

SHA-256：`A1FB13A08EDC8F504F1586A32DF9B229359280CF1BE9FC118B7E6910050096D6`

原激活候选 `PiaomiaoHelper-2026.10.16.0-activation-candidate.apk` 保留，未覆盖。授权地址依然为空，正常功能fail-closed；尚未部署授权服务。

## 加载路径与兼容性

共享库和worker处于APK提取后的同一个nativeLibraryDir。五类Root启动及恢复命令显式把此可信目录设置为LD_LIBRARY_PATH，不继承其它目录；快进NativeActivity也使用相同规则。

`$ORIGIN` RUNPATH作为额外路径保留。Android官方说明此功能从API24起支持，因此API23不能仅依赖它；当前明确设置的搜索路径用于维持原min23设计。当前只有API34设备，不能宣称API23已实测。[Android Bionic链接器说明](https://android.googlesource.com/platform/bionic/+/master/android-changes-for-ndk-developers.md)

## 已执行测试

- shared/static完整离线clean assembleDebug通过；五个真实模块均构建，非重复某一个worker的模型。
- ELF检查：五worker依赖同一core及RUNPATH；core的SONAME、初始化/版本/反初始化导出、架构及无TEXTREL检查通过。
- 诊断stub：7项通过，覆盖精确CLI选择、边界及无效参数不触及Frida、初始化/反初始化配对、空/非法版本字符串。
- Java启动命令自测通过，包含特殊字符转义与不继承污染搜索路径。
- 授权服务STOP/重启/恢复契约通过；native租约3单元+5集成通过。
- Android API34、x86_64、shell UID2000：两组各五个真实worker并发初始化、版本读取及退出均成功，未请求Root、未安装或替换软件、未操作游戏。reallocarray Android执行测试exit0。

最终两组各五进程同时运行，hold均30000ms，全部ready后统一采样两帧，每个进程均报告finished和exit0，PID、startTicks和cmdline绑定全部通过。汇总如下（第二帧，PSS两帧完全相同）：

| 指标，五进程合计 | 静态 | 共享 | 解释 |
| --- | ---: | ---: | --- |
| PSS | 43,069 KiB / 42.060 MiB | 28,404 KiB / 27.738 MiB | 下降34.05%，共享页按比例计入 |
| RSS | 56,152 KiB / 54.836 MiB | 70,984 KiB / 69.320 MiB | 上升，不能直接相加当实际物理内存 |
| PrivateDirty | 13,360 KiB / 13.047 MiB | 15,304 KiB / 14.945 MiB | 上升约1.9 MiB，不能称所有内存指标均下降 |

这些是Frida初始化后驻留的自检场景，没有游戏连接；不代表六功能工作时的峰值、CPU、延迟或稳定性。完整逐进程两帧、三项原始指标行和退出状态见 `tests/shared-frida-validation/device-results.json`。单位为KiB；十进制MB=KiB×0.001024，MiB=KiB÷1024。

前期采样因后台进程管理及CRLF解析问题失败，未将缺失值算成0或当作内存下降。最终脚本使用五个异步前台ADB进程，全部ready后统一采样，按PID、startTicks和cmdline验证身份；本轮设备临时目录和进程均已清理。SDK精确比较负例、smaps LF/CRLF/缺字段测试通过；Android真实launcher单测7项通过。

## 审查与验收边界

本轮18个既有源文件均有修改前字节快照。CodeRabbit首轮31文件完成，提出1项minor（SDK应精确比较），由测试代理修复并增加负例；一次复审完成，37个文件中仅提出1项minor：临时审查摘要仍说设备测试待完成。该说明已更新为API34自检及PSS完成、真实Root/游戏仍未验收；没有改运行代码，也没有第三轮审查。不能把两次各1项说成“审查工具返回零问题”。

计划和结果判断API均实际调用，返回HTTP429/not_run，不计通过。首次复审命令因工作目录错误停在分支检查，修正隔离目录后完成；没有将外层脏工作树当作本轮审查范围。

实际测试的5个动态worker与core已逐项计算SHA-256，全部与交付APK中的文件逐字节匹配。静态回退在共享构建之后再次构建通过，确认8个native且没有core残留；随后共享clean构建也通过。交付保留最初已完成装载和采样的候选，不覆盖已保存的包。

本轮没有执行Root/真实游戏的六功能启动、停止恢复，也没有更换已安装助手。候选的业务等价性只有最小源码变更、构建与离线回归证据；游戏内效果和运行内存仍须在明确授权的环境下分别验收。
