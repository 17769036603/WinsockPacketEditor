# 项目状态

更新时间：2026-08-07

## 已完成

- 增加 ONNX OCR provider，支持 DBNet/CRNN 类模型文件发现、推理输出解析、CTC 贪心解码和外部模型状态提示。
- 增加 Python Worker OCR：C# 前端通过 JSONL 发送 PNG，Worker 使用 RapidOCR + ONNX Runtime，失败或低置信度时安全回退到 C# ONNX/Tesseract。
- 增加 Airtest Windows Worker 能力：支持按窗口句柄截图和受运行级授权保护的输入动作；当前状态机仍由 C# 控制，不自动执行未授权动作。
- 增加颜色出现/消失条件、RGB 容差、最小像素数和最小命中比例。
- 完成 WinForms 配置入口、SQLite/XML 持久化以及模型状态显示。
- 增加 ONNX Runtime 运行时依赖和 x64 native sidecar 复制配置。
- 补齐视觉助手的区域坐标转换、步骤编辑同步、验证区域边界检查、空白截图防误判、模板资源释放和诊断截图保留上限。
- 将视觉助手步骤接入右侧机器人指令集：新增视觉等待指令，支持与键盘、鼠标、延时和循环指令混排、保存、导入导出及标准机器人执行。
- ONNX 识别器支持从模型输入元数据读取 4D NCHW/NHWC 形状与通道数，并支持可选 `preprocess.json` / `ocr_preprocess.json` 预处理 sidecar；模型或 sidecar 变化会触发会话重载。
- 完成固定 `1280×720` 客户区助手模式：加入尺寸配置、启动/运行中/动作前保护、终止型失败、区域越界检查、固定像素坐标 UI 及 SQLite/XML 兼容持久化。
- 完成视觉稳定性审查修复：缓存改用完整位图指纹；Tesseract 重定向输入改为可取消的异步写入；模板匹配增加工作量上限和像素级取消检查；动作发送前重新验证前台窗口；机器人异常、编辑器关闭和识别资源释放流程完成收敛；Hook 过滤器延迟任务增加启动/停止生命周期与实际执行等待。
- 增加 `vision_worker/worker.py`、依赖清单和 `tools/Install-VisionWorker.ps1`；Python 运行时安装到当前用户 `%LOCALAPPDATA%\XNAS\WPE\vision-worker\python`，不覆盖仓库、数据库或 ClickOnce 历史发布目录。

## 已验证

- 使用 Visual Studio 2022 MSBuild 完成 `WPELibrary.csproj` Debug 构建，并完成主程序 Debug 构建（`SignManifests=false`）：0 个错误；保留原有 4 个 EasyHook 清单警告。
- 视觉审查修复、颜色/ONNX、SQLite 持久化、state machine、capture enhancements、diagnostics 和 OCR runtime 回归通过。
- `VisionCaptureRegression.ps1` 通过；当前桌面环境无法提供真实屏幕帧，因此详细屏幕诊断项按环境限制透明跳过。
- WinForms vision UI audit 通过，窄窗口截图已复查。
- 视觉等待指令映射、视觉 UI、视觉持久化和状态机回归通过；窄窗口审计截图确认右侧指令集显示视觉等待行。
- `VisionFixedClientSizeRegression.ps1`、`VisionRobotFormRealAcceptance.ps1`、`VisionRealAcceptance.ps1`、`VisionColorAndOnnxRegression.ps1` 和 `UiDesignRegression.ps1` 均通过；主程序 UI 回归使用隔离输出目录 `WinsockPacketEditor\bin\FixedSizeDebug` 验证。
- `WPELibrary.csproj` Debug 重建通过（0 警告、0 错误）；主程序隔离 Debug 构建通过（0 错误，保留 4 个既有 EasyHook 清单警告）。默认 Debug 输出仍被运行中的 `小黑封包助手.exe` 占用，因此未覆盖运行中的正式输出。
- 2026-08-04 本轮修复后重新完成 Release 和隔离 Debug 解决方案重建：均为 0 个错误，保留 4 个既有 EasyHook `MSB3178` 清单警告。`VisionReviewFixesRegression.ps1`、视觉采集/诊断/OCR/状态机/持久化/颜色与 ONNX/固定客户区回归、`ReviewRegressionChecks.ps1`、首页/发送/字节/可读内容回归、Release 视觉 UI 审计和发送预设 UI 审计均通过；`git diff --check` 通过。未执行真实目标注入、抓包或封包发送。
- 2026-08-04 已通过本地 ClickOnce 热更新生成 `releases/2026.8.4.8/`：Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告；应用清单 49 个文件节点、EXE/部署清单/固定入口版本一致，文件大小和 SHA-256 校验通过。当前用户证书存储无可用私钥证书，因此本次为未签名本地包；桌面 `小黑封包助手.lnk` 已备份并继续指向固定入口，图标已更新到 2026.8.4.8。发布后代码、UI、视觉采集/OCR/模板/状态机/持久化回归及 `git diff --check` 均通过；未执行真实目标注入、抓包或封包发送。
- 2026-08-07 已通过本地 ClickOnce 热更新生成未签名 `releases/2026.8.7.0/`：Release 构建 0 错误，保留 4 条既有 EasyHook `MSB3178` 警告；发布目录 192 个文件、82 个 `.deploy` 映射文件，主程序/清单/ONNX sidecar/Python Worker 文件均通过存在性和版本校验。桌面 `小黑封包助手.lnk` 已备份并更新到固定入口及 2026.8.7.0 图标；旧发布目录、数据库和用户配置保留。
- 2026-08-07 Python Worker 部署验收通过：官方 Python 3.12.10 当前用户运行时、Airtest 1.4.3、RapidOCR 3.9.2、ONNX Runtime 1.28.0 可导入；JSONL `ping` 报告三项能力可用，RapidOCR 合成 `TASK 42` 返回成功且置信度约 0.995，C# JSONL 桥接同样通过。C# Debug 重建、视觉回归、协议回归、Worker 运行时回归和代码审查通过；未选择真实目标、未执行 Airtest 输入、未注入、未抓包或发送真实封包。

## 尚未验收

- 当前未把 APK 内的模型复制进项目，也未在没有许可证确认的情况下下载第三方模型。
- 尚未使用真实游戏截图验证 DBNet/CRNN 模型的准确率、延迟和中文字符集效果。
- 尚未进行真实目标窗口的业务结果验收；本次实现没有启动游戏、抓包、注入或发送网络数据。
- ONNX 推理的最终效果取决于模型输入预处理、模型输出布局和目标游戏字体，需用实际模型包做下一阶段标定。
- 尚未对真实目标窗口执行 Airtest 截图或输入动作；这些能力仅完成合成/协议层验收，并保留运行级授权门控。
## 2026-08-07 全面修复与 UI 审查前置

- 已修复 Python Worker 的离线模型校验、模型/语言缓存、OCR 预处理、阈值与字符过滤、原图坐标还原。
- 已接入 Airtest 捕获源；Python Worker 动作必须经过当前运行确认、目标窗口绑定和短时授权令牌。
- Python 可执行文件、Worker 脚本和超时已加入 WinForms 高级 OCR 设置，并写入 SQLite/XML。
- ClickOnce 输出已包含 `vision_worker/models/ocr` 的三个 ONNX 文件；安装脚本增加依赖、模型和 OCR 冒烟检查。
- Python Worker 增加 warmup 健康检查、模型 SHA-256 清单校验、有限滚动日志和请求诊断；Airtest 输入授权改为短时一次性 Token，并增加输入频率、坐标和截图区域边界保护。
- 视觉设置增加 Python/Worker 浏览、测试、恢复默认和 Auto 模式说明；Auto 继续使用本地 ONNX/Tesseract 顺序，Python Worker 显式启用并支持后台预热。

## 2026-08-07 本轮交付

- Release 与本地 ClickOnce 已更新至 `2026.8.7.1`，包内清单、Worker、三个 OCR 模型和部署文件核验通过。
- 桌面 `小黑封包助手.lnk` 已保留原入口并更新图标到 `2026.8.7.1`；旧版本、数据库和快捷方式备份保留。
- 本次发布为未签名本地包，仅用于本机验证，不能作为公网分发包。

## 2026-08-07 全部优化与热更新

- 已完成 Worker 健康检查、RapidOCR warmup、模型 SHA-256 清单、缓存指纹、请求诊断和用户目录有限滚动日志；安装脚本现在执行依赖、清单、warmup 与 OCR 冒烟验收。
- 已完成 Airtest 一次性短时授权、输入频率/坐标/时长边界和截图区域边界保护；未执行真实业务目标窗口输入、注入、抓包或封包发送。
- Python 高级设置增加路径浏览、Worker 测试、恢复默认和 Auto 模式说明；现有主流程保持不变，Python Worker 继续显式选择并支持后台预热。
- Release 与本地 ClickOnce 已更新至 `2026.8.7.4`；版本目录、固定入口和桌面快捷方式已核验，旧版本、数据库和快捷方式备份保留。
- 本次仍为 unsigned local release；对外分发前必须使用证书重新签名。
