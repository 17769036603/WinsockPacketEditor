# 项目执行规则

## 适用范围

本文件只记录本项目专属约束。全局代理规则仍然适用；若未来加入更具体的目录级规则，以更接近目标文件的规则为准。

## 当前边界

- 项目尚未完成技术栈和运行方式登记。
- 不得凭文件名或历史经验假定存在抓包、注入、驱动、管理员权限或网络业务能力。
- 修改前必须读取相关源码、配置、测试和本项目文档。
- 默认最小改动；不新增依赖，不覆盖、删除或移动用户文件，除非用户明确授权。
- 涉及真实网络流量、驱动、数据发送、权限提升、发布或覆盖交付物时，必须在执行前说明影响并获得明确授权。
- 项目已有完整 CLI 能力时，必须优先使用 CLI 完成相应操作；仅当 CLI 不存在、能力不完整或无法完成验收时，才使用电脑操控或 GUI。

## 藏宝图预设隔离

- 每一个藏宝图预设都是独立业务单元，即使指令内容、来源或当前行为相似，也不得推定它们可以合并、联动或共享可变策略。
- 识别特殊藏宝图预设时必须使用精确名称，并同时尊重各自持久化 GUID、配置、授权、运行状态和日志边界；不得使用模糊匹配、前缀扩大或共享名称白名单把多个预设绑定到同一个行为开关。
- 修改某一个藏宝图预设时，只能改变该预设自己的策略入口和回归证据。未经用户明确逐项授权，不得把修复、兼容路径、参数或发送规则复制到其他藏宝图预设。
- 可以复用无状态的底层组件，但模板要求、会话序号规则、路由兼容、发送间隔、失败策略和授权必须按预设独立解析，修改其中一个不得自动改变其他预设。
- 用户明确要求复制预设时，复制完成后目标仍是独立预设：保留目标名称和 GUID，只复制用户指定的数据，不建立后续联动关系。
- 修改会影响两个或以上藏宝图预设的共享代码前，必须先列出所有受影响预设并获得用户明确授权；否则应选择单预设的最小改动。
- 验证必须同时证明目标预设的新行为和至少一个非目标预设未被扩大匹配；不得只验证共享路径本身。

## 文档维护

- 项目事实写入 `PROJECT_STATUS.md`，架构写入 `ARCHITECTURE.md`。
- 只在事实发生变化时更新文档，不创建无事实依据的示例配置或命令。
- 中文 Markdown 使用 UTF-8 编码读取和验证。

## Agent skills

### Issue tracker

当前根目录没有独立 Git remote 或已登记的 issue tracker，暂以 `.scratch/` 下的本地 Markdown 文件作为临时约定；正式 tracker 选择为 `【待确认】`。详见 `docs/agents/issue-tracker.md`。

### Domain docs

这是 single-context 项目；相关领域文档使用根目录 `CONTEXT.md`（如存在）和 `docs/adr/`（如存在）。详见 `docs/agents/domain.md`。


# Codex project instructions

For complex coding tasks, use the `astra-orchestrator` skill when its trigger conditions match.

非简单的委派开发任务由主代理制定计划与验收条件，然后启动“判断代理”检查计划；完成实施和必要验证后，再启动“判断代理”检查结果。判断代理的固定职责和实际 API 调用入口见 docs/agents/jev-judge.md。若工具没有原生“判断代理”角色，使用通用子代理 agent_type=default 并传入该文档中的固定职责，不得因此跳过判断或要求用户手工调用 API。每个阶段最多一次修正后的复判；未调用 API、调用失败或响应无效时必须报告 not_run。仅发送本次任务的精简脱敏摘要，源码片段、原始 diff、日志、密钥或用户数据必须有当前任务的明确授权才能发送。

## 固定中文代理名称

面向用户的代理名称固定如下，工具要求的角色标识、任务标识和模型 ID 保留其技术值：

| 中文名称 | 内部角色标识 | 职责 |
| --- | --- | --- |
| 主代理 | root | 规划、分工、整合、最终确认 |
| 探索代理 | explorer | 只读查找代码和调用路径 |
| 开发代理 | worker | 按指定范围修改代码 |
| 测试代理 | tester | 按任务要求验证结果 |
| 研究代理 | researcher | 查阅官方资料 |
| 判断代理 | 判断代理（原生角色不可用时 default） | 仅调用 Jev API 并返回判断 |
| 审查路由代理 | reviewer（旧占位） | 指向 CodeRabbit，禁止自行代码评审 |

多个同类代理用中文职责加序号展示，例如“后端探索代理一”“后端探索代理二”。内部角色名不是面向用户的代理名称。角色名称本地化不改变已有模型、权限或并发上限。

All project code reviews must use the installed `coderabbit:code-review` plugin skill by default. Do not conduct a manual review or spawn a Codex reviewer subagent for code review. Follow the plugin skill's documented setup and authentication steps; if setup or review still fails, report the exact blocker and do not fall back to another review method.

The root agent owns architecture, decomposition, integration, and final verification.
Prefer specialized subagents for bounded exploration, implementation, testing, and technical research.

Do not delegate trivial work merely for parallelism.
Do not let multiple implementation agents edit the same files without explicit ownership boundaries.
User instructions always take precedence over this orchestration policy.
- User prefers pnpm over npm for all package operations.
