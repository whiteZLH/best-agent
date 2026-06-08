# Agent 运行时模块设计

## 1. 模块定位

Agent Runtime 是整个平台的核心编排器，负责创建 Run、推进状态机、执行业务决策、处理等待与恢复，并协调 Planner、Tool、Router、Approval 等下游模块。

> 当前实现状态：当前已经落地单 Agent 主运行时，能够创建 Run、推进状态、执行模型决策、调用工具、处理 `WaitingTool` 挂起与恢复，并支持最小 `WaitingApproval` 审批闭环与最小 `WaitingHuman` 人工接管闭环；但多 Agent、系统级挂起等更完整状态机仍未落地。

## 2. 设计目标

- 统一承载单 Agent 与多 Agent 执行
- 保证状态推进可恢复、可幂等、可审计
- 把策略判断置于 Runtime，而非完全交给模型
- 支持同步执行、异步工具、审批等待和人工接管

> 当前实现状态：
> - 当前已实现单 Agent 执行主链路。
> - 当前已支持异步工具等待与恢复。
> - 当前已支持高风险工具审批等待与 approve / reject 恢复。
> - 当前尚未支持多 Agent。
> - 当前策略判断主要包括：AllowedTools 校验、等待 token 校验、可配置审批策略、审批授权校验、运行状态冲突校验。
> - 当前 Worker 恢复链路也已优先按 `AgentRun.AgentDefinitionVersionId` 解析绑定版本，避免运行中的配置漂移。

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
- 当前已开始落地独立 `handoff`、`retrieval` 与 `approval_request` 步骤类型；`plan` 等更细 taxonomy 仍未落地

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
  - `WaitingApproval`
  - `Cancelled`
  - `Completed`
  - `Failed`
- `Created` 在当前实现中更多以 step 表达，而不是持久化 Run 状态
- `WaitingHuman` 已落地最小闭环：可进入等待人工状态，并由人工完成或终止 Run
- `Suspended` 仍未落地
- `TimedOut` 当前已通过审批超时默认拒绝路径落地最小终态，但尚未扩展到更通用的系统级超时治理

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
  - 处理 `WaitingApproval` approve / reject 最小审批恢复
- 当前尚未真正调用独立 Planner、Router 模块，Approval 仍是运行时内的最小实现
- 版本级 `ApprovalPolicy` 当前已开始进入运行时：若 `AgentDefinitionVersion` 显式声明审批策略，会在保留全局默认兜底的同时覆盖最小审批触发和审批角色规则
- 版本级 `OutputSchema` 当前也已开始进入运行时：若 `AgentDefinitionVersion` 显式声明最终输出 schema，Worker 会在 run 完成前对最终 `respond` 内容做最小 JSON Schema 校验
- `MaxCost` 当前也已开始进入运行时：若模型网关返回 `usage` 且已配置最小 token 单价，Runtime 会累计 `AgentRun.TotalCost`，并在达到或超过 `max_cost` 后阻止继续模型调用
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
  - `Running -> WaitingApproval`
  - `Running -> WaitingHuman`
  - `Running -> Completed`
  - `Running -> Failed`
  - `Running -> Cancelled`
  - `WaitingApproval -> TimedOut`
  - `WaitingTool -> Running`
  - `WaitingTool -> Cancelled`
  - `WaitingApproval -> Running`
  - `WaitingApproval -> Cancelled`
  - `WaitingHuman -> Running`
  - `WaitingHuman -> Completed`
  - `WaitingHuman -> Failed`
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
  2. 若请求带 `IdempotencyKey` 且已有 Run，直接返回已有 Run
  3. 创建 `AgentRun`
  4. 写入 `created` / `running` steps
  5. 入队后台 Worker
  6. `AgentRunLoop` 调模型拿到结构化决策
  7. 根据 `respond` / `tool_call` 分支处理
  8. 写入 step 并根据结果结束或挂起
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
  5. 客户端调用通用 `resume`，或外部工具系统调用 `tool-invocations/{invocationId}:complete`
  6. 服务端校验 `run_id + wait_token + run.Status`；专用 complete 回调还会校验 `{invocationId}` 对应当前 pending step，并支持 `Idempotency-Key` 重放保护
  7. 将 Pending step 补全后继续执行
- 当前不是完整 `ToolInvocation` 实体驱动恢复，`invocationId` 在 MVP 中对应 pending `tool_call` step 的 `StepId`

### 6.3 审批等待流程

目标流程：

1. 命中审批策略
2. 创建审批 Step
3. 设置 `wait_token`
4. Run 状态切换为 `WaitingApproval`
5. 审批结果回写后继续推进

当前实现状态：

- 当前已落地最小审批等待流程：
  1. 根据 `Approval:Policy:ApprovalRequiredSideEffectLevels`、`Approval:Policy:ParameterApprovalRules` 与 `ToolDefinition.SideEffectLevel` / 工具输入共同判断是否进入审批等待
  2. 写入 Pending `tool_call` step 和审批 payload
  3. Worker 创建独立 `AgentApproval` 记录
  4. Run 状态切换为 `WaitingApproval`
  5. `approve` 后执行待批工具并继续推进
  6. `reject` 后将待批步骤和 Run 标记为失败
- 当前也支持外部审批系统通过 `approvals/{approvalId}:complete` 回写 `Approved` / `Rejected`
- 当前外部审批 complete 回调已支持 `Idempotency-Key` 重放保护
- 当前审批授权也已支持通过 `Approval:Policy:RoleRequiredSideEffectLevels` 与 `Approval:Policy:AllowedApproverRoles` 配置风险级别和可审批角色
- 当前已落地最小人工接管闭环：可通过 `request-human` 进入 `WaitingHuman`，再由 `complete-human` 以人工结果直接完成 Run，或以人工终止结束 Run
- 当前已支持在 `WaitingTool` 场景下以人工结果替代挂起工具回调并继续 Runtime loop
- 当前已落地审批超时默认拒绝策略：过期 pending 审批会由后台服务终止 Run
- 当前已落地最小来源认证
- 当前尚未落地更通用的人工替换工具结果模型

### 6.4 恢复流程

目标流程：

1. 读取 Run 与最后未完成 Step
2. 判断挂起原因
3. 校验 `status_version` 和 `wait_token`
4. 恢复后续执行
5. 对重复回调只返回当前状态，不重复副作用

当前实现状态：

- 当前已落地 `WaitingTool` 恢复流程
- 当前已落地工具专用 complete 回调恢复流程
- 当前工具专用 complete 回调恢复流程也已开始兼容最小标准结果信封：`succeeded/success/completed` 会解包为真实工具输出继续 loop，`failed/error` 会按同步工具失败同样落结构化失败步骤/调用审计，并可继续进入手动补偿分支
- 当前已落地 `WaitingApproval` approve / reject 恢复流程
- 当前已落地 approval complete 回调恢复流程
- 当前已落地 `WaitingHuman` 人工完成 / 人工终止恢复流程
- 当前已落地 `Running` / `WaitingTool` / `WaitingApproval` 取消流程
- 当前会校验：
  - `run.Status == WaitingTool`
  - `CurrentWaitToken == request.WaitToken`
- 审批路径会校验 `run.Status == WaitingApproval`、pending step、审批 payload 状态和审批人授权
- 当前还未扩展到多种挂起原因统一恢复接口
- 当前创建 Run 已支持同一幂等键复用已有 Run
- 当前工具 complete 与 approval complete 已支持同一 `Idempotency-Key` + 同 payload 重放复用结果，不重复入队；同 key 不同 payload 会被拒绝
- 取消接口对已终态 Run 幂等返回当前状态，不重复写入副作用

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
- 当前已落地 run 生命周期事件 outbox，并提供按 run 回放与 `afterSeqNo` 增量补拉
- outbox 写入尚未与 Run / Step 状态更新纳入同一个严格本地事务
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
  - 审批授权校验，避免无身份或低权限审批写类风险工具
  - 取消后 Worker 不再用旧 loop 结果覆盖 `Cancelled` 终态
  - Worker 对同一 Run 的并发执行限制
  - 外部工具 complete / approval complete 的 `Idempotency-Key` 重放保护
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
- `WaitingApproval + approve/reject` 能恢复继续执行或终止 Run
- `cancel` 能将活动或等待中的 Run 终止为 `Cancelled`
- 同一 `Idempotency-Key` 重放只产生一个 Run
- 同一外部 complete 回调 `Idempotency-Key` 重放不会重复入队
- 非法恢复状态和错误 `waitToken` 会被拒绝
- 更完整的外部回调来源认证、worker 崩溃恢复和多状态迁移约束仍属于后续目标态
