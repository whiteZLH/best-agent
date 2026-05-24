# Agent 运行时模块设计

## 1. 模块定位

Agent Runtime 是整个平台的核心编排器，负责创建 Run、推进状态机、执行业务决策、处理等待与恢复，并协调 Planner、Tool、Router、Approval 等下游模块。

## 2. 设计目标

- 统一承载单 Agent 与多 Agent 执行
- 保证状态推进可恢复、可幂等、可审计
- 把策略判断置于 Runtime，而非完全交给模型
- 支持同步执行、异步工具、审批等待和人工接管

## 3. 核心对象

### 3.1 AgentRun

关键字段：

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

### 3.2 AgentStep

关键字段：

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

### 3.3 Run 状态

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

### 3.4 Step 类型

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

## 5. 状态迁移约束

必须至少支持以下迁移：

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

## 6. 关键流程

### 6.1 标准执行流程

1. 基于幂等键创建或复用 Run
2. 读取 AgentDefinition 版本
3. 将 Run 状态由 `Created` 推进到 `Running`
4. 构造上下文
5. 调用 Planner 生成 `PlanDecision`
6. 执行策略校验
7. 根据 `PlanDecision.type` 分支处理
8. 写入 Step、Message、Invocation 和事件
9. 在完成、失败或等待状态退出

### 6.2 工具等待流程

1. 创建 `ToolInvocation`
2. 同步工具直接执行并回写结果
3. 异步工具设置 `wait_token`
4. Run 状态切换为 `WaitingTool`
5. 回调到达后校验 `run_id + step_id + wait_token`
6. 恢复为 `Running`

### 6.3 审批等待流程

1. 命中审批策略
2. 创建审批 Step
3. 设置 `wait_token`
4. Run 状态切换为 `WaitingApproval`
5. 审批结果回写后继续推进

### 6.4 恢复流程

1. 读取 Run 与最后未完成 Step
2. 判断挂起原因
3. 校验 `status_version` 和 `wait_token`
4. 恢复后续执行
5. 对重复回调只返回当前状态，不重复副作用

## 7. 并发与幂等

- `agent_run` 和 `agent_step` 都应维护 `status_version`
- 状态更新使用乐观锁
- 同一 `step_key` 只能成功创建一次
- 先落库 Step，再执行外部副作用
- 使用 outbox 投递事件，避免事务与事件不一致

## 8. 内部接口建议

- `CreateOrGetRun(request)`
- `LoadDefinition(agentCode, version?)`
- `BuildContext(run, definition)`
- `Plan(run, context)`
- `ValidatePlan(plan, definition, run)`
- `ExecutePlan(plan, run, step)`
- `ResumeRun(runId, resumeToken, source)`
- `TransitionRun(run, from, to)`

## 9. 风险控制点

- Runtime 必须拒绝越权 Plan
- Runtime 不能信任回调方重复提交
- Suspended 仅用于系统级中断，不滥用于审批
- 不可逆副作用前必须已经持久化足够恢复信息

## 10. 验收重点

- 同一 `Idempotency-Key` 重放只产生一个 Run
- 同一异步回调重放不会重复执行副作用
- Worker 崩溃后可从未完成 Step 恢复
- 状态迁移严格符合约束，不出现非法跳转
