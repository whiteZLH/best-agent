# Agent 运行时模块设计

## 1. 模块定位

Agent Runtime 是整个平台的核心编排器，负责创建 Run、推进状态机、执行业务决策、处理等待与恢复，并协调 Planner、Tool、Router、Approval 等下游模块。

> 当前实现状态：当前已经落地单 Agent 主运行时，能够创建 Run、推进状态、执行模型决策、调用工具、处理 `WaitingTool` 挂起与恢复；但多 Agent、审批、人机协同、系统级挂起等更完整状态机仍未落地。

## 2. 设计目标

- 统一承载单 Agent 与多 Agent 执行
- 保证状态推进可恢复、可幂等、可审计
- 把策略判断置于 Runtime，而非完全交给模型
- 支持同步执行、异步工具、审批等待和人工接管

> 当前实现状态：
> - 当前已实现单 Agent 执行主链路。
> - 当前已支持异步工具等待与恢复。
> - 当前尚未支持多 Agent、审批等待、人工接管。
> - 当前策略判断主要包括：AllowedTools 校验、等待 token 校验、运行状态冲突校验。

## 3. 核心对象

### 3.1 AgentRun

目标关键字段：

- `run_id`
- `agent_code`
- `tenant_id`
- `user_id`
- `session_id`
- `status`
- `input`
- `output`
- `current_step_no`
- `parent_run_id`
- `root_run_id`
- `status_version`
- `idempotency_key`
- `current_wait_token`
- `interrupt_reason`

当前实现状态：

- 上述大多数字段已经落地在 `AgentRun` 中
- 当前运行时真实使用最强的字段包括：
  - `Status`
  - `InputPayload`
  - `OutputPayload`
  - `CurrentStepNo`
  - `StatusVersion`
  - `IdempotencyKey`
  - `CurrentWaitToken`
  - `InterruptReason`
- `TenantId`、`UserId`、`SessionId`、父子 Run 相关字段目前还未真正进入主链路治理逻辑

### 3.2 AgentStep

目标关键字段：

- `step_id`
- `run_id`
- `step_no`
- `step_type`
- `status`
- `input_payload`
- `output_payload`
- `error_payload`
- `step_key`
- `retry_count`
- `depends_on_step_id`
- `decision_payload`

当前实现状态：

- 上述字段大多已落地在 `AgentStep` 中
- 当前步骤流主要使用：
  - `created`
  - `running`
  - `model_call`
  - `tool_call`
  - `failed`
- 当前还未落地独立 Plan / Approval / Handoff / Retrieve 等步骤类型

### 3.3 Run 状态

目标状态：

- `Created`
- `Running`
- `WaitingTool`
- `WaitingApproval`
- `WaitingHuman`
- `Suspended`
- `Completed`
- `Failed`
- `Cancelled`
- `TimedOut`

当前实现状态：

- 当前真正进入主链路的状态主要是：
  - `Running`
  - `WaitingTool`
  - `Completed`
  - `Failed`
- `Created` 在当前实现中更多以 step 表达，而不是持久化 Run 状态
- `WaitingApproval`、`WaitingHuman`、`Suspended`、`Cancelled`、`TimedOut` 仍未落地

### 3.4 Step 类型

目标类型：

- `Input`
- `Plan`
- `Reason`
- `ToolCall`
- `ToolResult`
- `Retrieve`
- `Handoff`
- `ApprovalRequest`
- `ApprovalResult`
- `Summarize`
- `Respond`
- `Interrupt`

当前实现状态：

- 当前实现尚未采用如此细分的 Step taxonomy
- 当前主要是更轻量的 step 类型：
  - `created`
  - `running`
  - `model_call`
  - `tool_call`
  - `failed`

## 4. 职责边界

负责：

- 创建或复用 Run
- 驱动状态机
- 调用 Planner、Tool、Approval、Router
- 校验策略优先级
- 处理等待、恢复、重试、超时和中断

不负责：

- 保存长期知识内容的业务语义
- 具体模型供应商适配
- 工具业务逻辑实现
- 前端展示渲染

当前实现状态：

- 当前 Runtime 实际负责：
  - 创建 Run
  - 推进运行状态
  - 调用模型与工具
  - 处理 `WaitingTool` 恢复
- 当前尚未真正调用独立 Planner、Approval、Router 模块
- 模型适配已下沉到 `IModelGateway`
- 工具业务逻辑已下沉到 `IToolExecutor` / `HttpToolInvoker` / 本地 handler

## 5. 状态迁移约束

目标至少支持以下迁移：

- `Created -> Running`
- `Running -> WaitingTool`
- `Running -> WaitingApproval`
- `Running -> WaitingHuman`
- `Running -> Suspended`
- `Running -> Completed`
- `Running -> Failed`
- `Running -> Cancelled`
- `Running -> TimedOut`
- `WaitingTool -> Running`
- `WaitingApproval -> Running`
- `WaitingHuman -> Running`
- `Suspended -> Running`

终态规则：

- `Completed`
- `Failed`
- `Cancelled`
- `TimedOut`

进入终态后不可再次推进。

当前实现状态：

- 当前主链路已支持：
  - `Running -> WaitingTool`
  - `Running -> Completed`
  - `Running -> Failed`
  - `WaitingTool -> Running`
- 当前创建阶段的 `Created -> Running` 更偏逻辑概念，Run 本身创建后即为 `Running`
- 其余状态迁移仍未落地

## 6. 关键流程

### 6.1 标准执行流程

目标流程：

1. 基于幂等键创建或复用 Run
2. 读取 AgentDefinition 版本
3. 将 Run 状态由 `Created` 推进到 `Running`
4. 构造上下文
5. 调用 Planner 生成 `PlanDecision`
6. 执行策略校验
7. 根据 `PlanDecision.type` 分支处理
8. 写入 Step、Message、Invocation 和事件
9. 在完成、失败或等待状态退出

当前实现状态：

- 当前流程更简化：
  1. 加载已启用 `AgentDefinition`
  2. 创建 `AgentRun`
  3. 写入 `created` / `running` steps
  4. 入队后台 Worker
  5. `AgentRunLoop` 调模型拿到结构化决策
  6. 根据 `respond` / `tool_call` 分支处理
  7. 写入 step 并根据结果结束或挂起
- 当前尚未引入独立 Planner / Message / Invocation 主链路

### 6.2 工具等待流程

目标流程：

1. 创建 `ToolInvocation`
2. 同步工具直接执行并回写结果
3. 异步工具设置 `wait_token`
4. Run 状态切换为 `WaitingTool`
5. 回调到达后校验 `run_id + step_id + wait_token`
6. 恢复为 `Running`

当前实现状态：

- 当前已落地等待恢复主链路，但实现更轻量：
  1. 工具返回 `IsPending = true`
  2. 写入 Pending `tool_call` step
  3. Run 切到 `WaitingTool`
  4. 保存 `CurrentWaitToken`
  5. 客户端调用 `resume`
  6. 服务端校验 `run_id + wait_token + run.Status`
  7. 将 Pending step 补全后继续执行
- 当前不是 `ToolInvocation` 驱动恢复

### 6.3 审批等待流程

目标流程：

1. 命中审批策略
2. 创建审批 Step
3. 设置 `wait_token`
4. Run 状态切换为 `WaitingApproval`
5. 审批结果回写后继续推进

当前实现状态：

- 尚未落地

### 6.4 恢复流程

目标流程：

1. 读取 Run 与最后未完成 Step
2. 判断挂起原因
3. 校验 `status_version` 和 `wait_token`
4. 恢复后续执行
5. 对重复回调只返回当前状态，不重复副作用

当前实现状态：

- 当前已落地 `WaitingTool` 恢复流程
- 当前会校验：
  - `run.Status == WaitingTool`
  - `CurrentWaitToken == request.WaitToken`
- 当前还未扩展到多种挂起原因统一恢复
- 当前也尚未形成完整的重复回调幂等回放语义

## 7. 并发与幂等

目标设计：

- `agent_run` 和 `agent_step` 都应维护 `status_version`
- 状态更新使用乐观锁
- 同一 `step_key` 只能成功创建一次
- 先落库 Step，再执行外部副作用
- 使用 outbox 投递事件，避免事务与事件不一致

当前实现状态：

- 当前 `AgentRun` 已维护 `StatusVersion`
- `AgentStep` 模型中也有 `StatusVersion` 字段，但当前主链路对其使用较轻
- 当前 `StepKey` 已进入模型
- 当前未落地 outbox
- 当前并发控制更多体现在：
  - `StatusVersion`
  - `waitToken`
  - Worker 对同一 `runId` 的并发执行限制

## 8. 内部接口建议

目标建议：

- `CreateOrGetRun(request)`
- `LoadDefinition(agentCode, version?)`
- `BuildContext(run, definition)`
- `Plan(run, context)`
- `ValidatePlan(plan, definition, run)`
- `ExecutePlan(plan, run, step)`
- `ResumeRun(runId, resumeToken, source)`
- `TransitionRun(run, from, to)`

当前实现状态：

- 当前实现尚未抽成这一层显式 runtime service API
- 当前能力分散在：
  - `CreateAgentRunCommandHandler`
  - `ResumeAgentRunCommandHandler`
  - `AgentRunLoop`
  - `AgentRunWorker`

## 9. 风险控制点

目标设计：

- Runtime 必须拒绝越权 Plan
- Runtime 不能信任回调方重复提交
- Suspended 仅用于系统级中断，不滥用于审批
- 不可逆副作用前必须已经持久化足够恢复信息

当前实现状态：

- 当前已落地的风险控制包括：
  - `AllowedTools` 校验，拒绝越权工具调用
  - `waitToken` 校验，避免错误恢复
  - Worker 对同一 Run 的并发执行限制
- `Suspended`、审批区分、不可逆副作用完整恢复信息等仍属后续目标态

## 10. 验收重点

目标验收：

- 同一 `Idempotency-Key` 重放只产生一个 Run
- 同一异步回调重放不会重复执行副作用
- Worker 崩溃后可从未完成 Step 恢复
- 状态迁移严格符合约束，不出现非法跳转

当前实现验收重点：

- `Create -> queue -> worker -> loop` 主链路可运行
- `tool_call` 的同步完成与异步挂起都能正确写 Step
- `WaitingTool + resume` 能恢复继续执行
- 非法恢复状态和错误 `waitToken` 会被拒绝
- 更完整的 idempotency replay、worker 崩溃恢复和多状态迁移约束仍属于后续目标态
