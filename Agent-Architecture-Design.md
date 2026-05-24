# 通用 Agent 架构设计方案

## 1. 文档定位

本文档给出一套独立于任何现有项目、框架或语言实现的通用 Agent 架构设计。

适用范围：

- 对话型 Agent
- Tool Use Agent
- Workflow Agent
- Router Agent
- Multi-Agent 协作系统
- 企业内部智能助手平台

本文档目标：

- 提供一套完整的 Agent 架构分层
- 给出运行时模型、状态机、接口、数据模型和部署建议
- 支持从 MVP 到企业级平台的持续演进

本文档不绑定：

- 特定大模型厂商
- 特定编程语言
- 特定消息中间件
- 特定数据库
- 特定前端形态

## 2. 设计目标

### 2.1 功能目标

- 支持自然语言交互
- 支持工具调用
- 支持多轮推理
- 支持子 Agent 路由与协作
- 支持知识库检索
- 支持人工审批
- 支持运行恢复
- 支持流式输出

### 2.2 工程目标

- 高内聚、低耦合
- 配置驱动优先
- 模型厂商可替换
- 工具体系可扩展
- 数据结构可审计
- 运行过程可观测
- 失败场景可恢复

### 2.3 非目标

- 不要求 Agent 完全自治
- 不要求所有场景都使用多 Agent
- 不要求第一版就具备复杂长期记忆
- 不要求第一版就实现自我优化闭环

## 3. 总体设计原则

### 3.1 单一主入口

外部调用方不应关心底层使用了多少模型、多少工具、多少子 Agent。

对外统一抽象应为：

- 运行一个 Agent
- 恢复一个 Agent Run
- 查询一个 Agent Run
- 审批一个 Agent Step

### 3.2 Runtime 与 Definition 分离

必须区分：

- Agent Definition：Agent 是什么
- Agent Runtime：Agent 这一次是怎么跑的

前者是配置与能力描述，后者是实例化后的执行状态。

### 3.3 日志与状态分离

必须区分：

- 审计日志
- 运行状态

审计日志解决“发生了什么”，运行状态解决“下一步该做什么”。

### 3.4 策略前置

对于工具调用、外部写操作、敏感数据访问，策略判断必须由 Runtime 控制，而不能完全交给模型自由决定。

### 3.5 结构化中间结果

路由、审批、工具计划、错误、中断原因都应使用结构化数据，而不是依赖字符串约定。

## 4. 总体架构

推荐采用以下分层：

1. Experience Layer
2. API Layer
3. Agent Runtime Layer
4. Agent Definition Layer
5. Planning and Reasoning Layer
6. Tool and Action Layer
7. Memory and Retrieval Layer
8. State and Persistence Layer
9. Observability and Governance Layer
10. Infrastructure Layer

### 4.1 Experience Layer

职责：

- Chat UI
- Copilot UI
- Workflow UI
- Approval UI
- Admin Console

### 4.2 API Layer

职责：

- 对外 HTTP/gRPC/WebSocket 接口
- 鉴权
- 会话路由
- 流式响应封装

### 4.3 Agent Runtime Layer

职责：

- 创建 Run
- 推进状态机
- 执行 Planner / Tool / Router / Approval
- 处理中断、超时、重试、恢复

这是整个系统的核心。

### 4.4 Agent Definition Layer

职责：

- 管理 Agent 定义
- 管理能力边界
- 管理默认模型、工具集、记忆策略、审批策略

### 4.5 Planning and Reasoning Layer

职责：

- 构造消息上下文
- 选择模型
- 请求模型输出计划、答案、工具调用或 handoff

### 4.6 Tool and Action Layer

职责：

- 工具注册
- 参数校验
- 权限控制
- 工具执行
- 幂等与超时控制

### 4.7 Memory and Retrieval Layer

职责：

- 会话短期记忆
- 长期记忆
- 知识库检索
- 历史摘要

### 4.8 State and Persistence Layer

职责：

- 持久化 Run
- 持久化 Step
- 持久化 Message
- 持久化 Tool Result
- 支持恢复执行

### 4.9 Observability and Governance Layer

职责：

- Trace
- Metrics
- Logs
- Cost Tracking
- Policy Audit
- Approval Audit

### 4.10 Infrastructure Layer

职责：

- DB
- Cache
- Queue
- Object Storage
- Vector Store
- Secret Manager

## 5. 核心抽象

### 5.1 AgentDefinition

表示一个可执行的 Agent。

建议字段：

- `id`
- `code`
- `name`
- `description`
- `instruction`
- `system_prompt_template`
- `default_model`
- `allowed_tools`
- `knowledge_sources`
- `memory_policy`
- `routing_policy`
- `approval_policy`
- `execution_policy`
- `planner_policy`
- `context_policy`
- `max_turns`
- `max_cost`
- `allowed_handoffs`
- `output_schema`
- `enabled`
- `version`

### 5.2 AgentRun

表示 Agent 的一次运行实例。

建议字段：

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
- `last_heartbeat_at`
- `started_at`
- `ended_at`
- `interrupt_reason`

### 5.3 AgentStep

表示一次运行中的一个原子步骤。

建议字段：

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
- `started_at`
- `ended_at`
- `duration_ms`

### 5.4 Message

表示上下文中的消息单元。

建议角色：

- `system`
- `developer`
- `user`
- `assistant`
- `tool_call`
- `tool_result`
- `handoff`
- `approval_request`
- `approval_result`
- `summary`

### 5.5 ToolDefinition

表示一个可被 Agent 调用的工具。

建议字段：

- `tool_name`
- `description`
- `input_schema`
- `output_schema`
- `side_effect_level`
- `timeout_ms`
- `retry_policy`
- `auth_policy`
- `idempotency_policy`
- `async_supported`
- `consistency_mode`
- `compensation_policy`
- `enabled`

### 5.6 RouteDecision

表示 Router 的结构化决策。

建议字段：

- `target_agent`
- `confidence`
- `reason`
- `context_overrides`
- `tool_overrides`
- `approval_required`

### 5.7 PlanDecision

表示 Runtime 当前要执行的“下一动作”，它是 Planner 和 Runtime 之间的正式契约。

建议字段：

- `decision_id`
- `type`
- `reason`
- `confidence`
- `selected_model`
- `response_message`
- `tool_calls`
- `retrieval_request`
- `handoff_request`
- `approval_request`
- `terminate_reason`

其中 `type` 建议只允许以下枚举：

- `respond`
- `tool_call`
- `retrieve`
- `handoff`
- `request_approval`
- `request_human`
- `fail`

建议约束：

- 任一时刻只允许一个主 `type`
- `tool_calls` 可包含多个调用，但必须声明是否允许并行
- `handoff_request` 必须明确目标 Agent、上下文范围、是否等待返回
- `response_message` 只在 `respond` 时出现
- `fail` 必须给出结构化错误码而不是纯文本

建议的最小 JSON 结构：

```json
{
  "decision_id": "dec_001",
  "type": "tool_call",
  "reason": "需要先查询订单系统",
  "confidence": 0.93,
  "selected_model": "gpt-x",
  "tool_calls": [
    {
      "call_id": "call_001",
      "tool_name": "query_abnormal_orders",
      "arguments": {
        "date": "2026-05-24"
      },
      "parallel_group": "g1",
      "idempotency_key": "run_123:step_4:call_001"
    }
  ]
}
```

### 5.8 ApprovalDecision

表示审批结果。

建议字段：

- `decision`
- `approver_id`
- `approver_role`
- `comment`
- `decided_at`

### 5.9 ToolInvocation

表示一次具体的工具执行记录。它与 `ToolDefinition` 的区别是：前者描述“这次怎么执行”，后者描述“这个工具是什么”。

建议字段：

- `invocation_id`
- `run_id`
- `step_id`
- `tool_name`
- `status`
- `input_payload`
- `output_payload`
- `error_payload`
- `idempotency_key`
- `callback_token`
- `executor_node`
- `started_at`
- `ended_at`

建议 `status` 枚举：

- `Pending`
- `Running`
- `Succeeded`
- `Failed`
- `Expired`
- `Cancelled`

## 6. Agent 分类

### 6.1 Conversation Agent

用于常规对话问答，通常以回答为主，工具调用较少。

### 6.2 Tool Use Agent

用于信息查询、系统操作、自动化任务执行。

### 6.3 Router Agent

用于识别意图并将请求分发给合适的子 Agent。

### 6.4 Workflow Agent

用于多步骤、较强约束的任务执行。

### 6.5 Specialist Agent

用于特定领域，如法务、财务、运维、设备控制。

### 6.6 Supervisor Agent

用于管理多个子 Agent，并做 handoff、审批或结果整合。

## 7. 运行时状态机

推荐 `AgentRunStatus`：

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

推荐 `AgentStepType`：

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

### 7.1 状态迁移约束

状态不能只定义“有哪些值”，还必须定义“允许怎么跳”。建议最少约束如下：

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
- `WaitingTool -> Failed`
- `WaitingTool -> Cancelled`
- `WaitingTool -> TimedOut`
- `WaitingApproval -> Running`
- `WaitingApproval -> Cancelled`
- `WaitingApproval -> TimedOut`
- `WaitingHuman -> Running`
- `WaitingHuman -> Cancelled`
- `WaitingHuman -> TimedOut`
- `Suspended -> Running`
- `Suspended -> Cancelled`
- `Suspended -> TimedOut`

额外建议：

- `Completed`、`Failed`、`Cancelled`、`TimedOut` 为终态，进入后不可再推进
- `WaitingTool` 仅用于异步工具或外部回调工具；同步工具可直接留在 `Running`
- `Suspended` 用于系统级中断，例如 worker 重启、依赖故障、人工冻结，不用于普通审批

### 7.2 并发控制与恢复语义

为了保证恢复和回调安全，建议：

- `agent_run` 和 `agent_step` 都带 `status_version`
- 每次状态迁移使用乐观锁更新：`where run_id = ? and status_version = ?`
- 任一外部回调都必须带 `run_id + step_id + wait_token`
- Runtime 推进前先检查当前状态是否仍允许该动作
- 同一个 `step_key` 只能成功创建一次，用于防止重复推进

推荐原则：

- 先落库 Step，再执行外部副作用
- 外部副作用成功后再写结果并推进 Run
- 事件发送使用 outbox，避免“库已提交但事件没发出去”

### 7.3 标准单 Agent 流程

1. 创建 Run
2. 写入用户输入
3. 初始化 `status_version`
4. 装配上下文
5. Planner 生成 `PlanDecision`
6. Runtime 校验 `PlanDecision` 是否满足策略约束
7. 如果是最终回答，写入响应并结束
8. 如果是同步工具调用，创建 `ToolInvocation` 并执行
9. 如果是异步工具调用，切换为 `WaitingTool`
10. 如果是审批请求，挂起等待
11. 如果是 handoff，创建子 Run
12. 直到完成或中断

### 7.4 恢复流程

1. 读取 Run 和最后一个未完成 Step
2. 判断当前挂起原因
3. 校验 `status_version` 和 `wait_token`
4. 恢复工具执行、审批后续或子 Agent 返回
5. 继续推进状态机
6. 若发现重复回调，仅返回当前状态，不重复执行副作用

## 8. Planning 模型

推荐将推理逻辑拆分为三个职责：

### 8.1 Planner

职责：

- 判断是否需要工具
- 判断是否需要检索
- 判断是否需要 handoff
- 判断是否可直接回答

输出要求：

- Planner 的输出必须符合 `PlanDecision`
- Planner 不能直接决定绕过策略，例如敏感工具审批
- Planner 可以建议并行工具，但最终并行度由 Runtime 或策略层裁剪

### 8.2 Executor

职责：

- 执行工具
- 收集结果
- 统一错误处理

执行约束：

- Executor 只消费结构化 `PlanDecision`
- Executor 不应重新解释自然语言 Prompt
- Executor 负责把工具、检索、handoff 结果归一化为标准 `Step` 输出

### 8.3 Reflector

职责：

- 判断工具结果是否足够
- 判断是否需要重试
- 判断是否需要降级
- 判断是否可以结束

对于简单场景，这三者可以复用同一个模型调用；对于复杂场景，可以拆成独立策略模块。

### 8.4 策略优先级

推荐采用以下执行优先级，避免模型输出与平台策略冲突：

1. 租户级安全策略
2. AgentDefinition 中的能力边界
3. Runtime 当前状态约束
4. 审批策略
5. Planner 输出建议

若低优先级结果与高优先级冲突，应由 Runtime 拒绝，并生成结构化错误或降级决策。

## 9. 上下文与记忆设计

### 9.1 短期记忆

包含当前会话的近期消息、最近工具结果、最近摘要。

适合存储：

- 最近 N 轮对话
- 最近一次工具结果
- 最近一段运行摘要

### 9.2 长期记忆

包含用户偏好、历史事实、任务经验。

适合存储：

- 用户偏好
- 历史业务事实
- 结构化画像

### 9.3 检索记忆

通过向量检索或关键字检索引入外部知识。

适合存储：

- 文档库
- FAQ
- SOP
- 工单记录

### 9.4 摘要策略

建议在上下文超过阈值时自动摘要，而不是简单丢弃历史消息。

推荐策略：

- 保留最近 N 轮
- 保留关键工具结果
- 将更早历史压缩为 summary message

### 9.5 记忆写入策略

长期记忆不是“能存就存”，建议至少满足以下条件：

- 只允许特定来源写入，例如人工确认、可信工具结果、显式用户偏好
- 为每条记忆记录 `source_type`、`source_ref`、`confidence`
- 支持 `ttl` 或失效策略，避免一次性事实永久污染
- 支持撤销或覆写，而不是只能追加
- 默认不把模型自由生成的推测写入长期记忆

建议把长期记忆写入拆成独立决策：

- `should_write_memory`
- `memory_type`
- `memory_scope`
- `memory_value`
- `expires_at`

### 9.6 上下文装配顺序

推荐统一顺序，降低提示词漂移：

1. system/developer 指令
2. 当前用户输入
3. 最近 N 轮短期消息
4. 尚未过期的 summary
5. 必要的工具结果
6. 检索结果
7. 长期记忆

建议限制：

- 长期记忆只作为补充上下文，不覆盖当前用户明确输入
- 检索结果必须带来源元数据，供回答阶段引用
- summary 需要记录生成时点，避免无限递归摘要

## 10. 工具体系设计

### 10.1 Tool Registry

负责管理所有工具定义。

### 10.2 Tool Resolver

根据 AgentDefinition、租户、环境、权限决定本次可见工具。

### 10.3 Tool Validator

负责：

- 参数 JSON Schema 校验
- 类型校验
- 安全校验

### 10.4 Tool Executor

负责：

- 实际执行工具
- 超时控制
- 重试控制
- 幂等控制

### 10.5 Tool Policy

建议按副作用分级：

- `read_only`
- `internal_write`
- `external_write`
- `destructive`

副作用越高，越应要求审批或显式确认。

### 10.6 工具调用模式

建议统一支持两类模式：

- 同步模式：Runtime 发起调用后在当前执行片段内拿到结果
- 异步模式：Runtime 只创建 `ToolInvocation`，等待回调或轮询结果

推荐规则：

- 默认同步，只有长耗时或外部系统回调场景才进入异步
- 进入异步模式时，Run 状态切到 `WaitingTool`
- 异步工具必须提供 `callback_token`、过期时间、幂等键

### 10.7 工具幂等与补偿

建议：

- 每次工具调用都生成 `idempotency_key`
- 对外写工具必须支持“至少一次回调、至多一次生效”
- 对不可逆副作用工具，必须在 `ToolDefinition` 中声明 `compensation_policy`
- 若工具侧无法保证幂等，Runtime 必须在发起前做去重保护

### 10.8 标准 Tool Result

建议所有工具结果归一化为统一结构：

```json
{
  "status": "succeeded",
  "data": {},
  "error": null,
  "meta": {
    "durationMs": 320,
    "source": "order-service",
    "cached": false
  }
}
```

这样 Reflector、审计和前端展示都不需要理解每个工具的私有格式。

## 11. Router 与 Multi-Agent 设计

### 11.1 Router 的职责边界

Router 只负责“该交给谁”，不负责“具体怎么做”。

### 11.2 Handoff 模式

推荐支持三种 handoff：

- `route_only`
- `delegate_and_wait`
- `delegate_and_merge`

### 11.3 子 Agent 运行关系

需要明确：

- 父 Run
- 子 Run
- 根 Run
- 汇总关系

### 11.4 结果整合

Supervisor Agent 应能将多个子 Agent 结果整合为单一对用户可见的回答。

### 11.5 上下文边界

子 Agent 不应默认继承父 Agent 的全部上下文。建议显式定义：

- `context_scope`: 传哪些消息
- `memory_scope`: 是否可读写长期记忆
- `tool_scope`: 可见哪些工具
- `knowledge_scope`: 可访问哪些知识源

推荐默认值：

- 只传当前任务相关摘要，而不是整段历史消息
- 默认只读长期记忆
- 子 Agent 默认只能看到自己 allowlist 中的工具

### 11.6 权限继承规则

建议：

- 子 Agent 不能天然继承父 Agent 的全部高权限工具
- 审批策略取“父子中更严格的一侧”
- 租户隔离和用户隔离必须沿着 `root_run_id` 继承
- handoff 时记录 `delegated_by_run_id` 和 `delegated_by_agent`

### 11.7 结果合并策略

对于 `delegate_and_merge`，建议预先定义合并方式：

- `first_success`
- `all_results`
- `majority_vote`
- `supervisor_summary`

如果不预定义，最后很容易把“结果整合”变成不可控的自由文本拼接。

## 12. 审批与人机协同

### 12.1 审批触发条件

建议支持：

- 特定工具
- 特定参数模式
- 特定租户策略
- 特定风险等级

### 12.2 审批流程

1. Runtime 命中审批策略
2. 创建 `approval_request` step
3. Run 状态切换为 `WaitingApproval`
4. 外部审批系统或人工台处理
5. 写入 `approval_result`
6. 恢复 Run

建议 `approval_request` 至少包含：

- `approval_id`
- `run_id`
- `step_id`
- `requested_action`
- `risk_level`
- `request_payload`
- `expires_at`
- `wait_token`

### 12.3 人工接管

对于高不确定性任务，允许：

- 人工补充信息
- 人工替换工具结果
- 人工终止任务

### 12.4 审批超时与默认策略

建议明确：

- 审批超时后默认是拒绝、取消还是转人工
- 已过期审批结果是否还能回写
- 审批后恢复是否需要重新校验当前上下文是否已变化

## 13. 错误处理与恢复

### 13.1 错误分类

- 模型错误
- 工具错误
- 策略错误
- 审批错误
- 网络错误
- 数据错误
- 超时错误

### 13.2 恢复策略

建议为不同步骤定义恢复方式：

- 重试
- 降级
- 跳过
- 转人工
- 中断结束

建议把恢复策略定义到 StepType 或 ToolDefinition 级别，而不是只写在通用文案里。

### 13.3 超时策略

应有三层超时：

- 整体 Run 超时
- 单次模型调用超时
- 单次工具调用超时

### 13.4 重试规则

建议最少区分：

- 可安全重试：读工具、幂等写工具、短暂网络失败
- 需谨慎重试：外部写工具、已产生副作用但未确认结果的调用
- 不可自动重试：人工审批、不可逆 destructive 工具

每次重试都应记录：

- `retry_count`
- `retry_reason`
- `previous_attempt_ref`

## 14. 数据模型

建议最少包含以下表或集合：

### 14.1 AgentDefinition Store

- `agent_definition`
- `agent_definition_version`
- `tool_definition`
- `route_rule`

### 14.2 Runtime Store

- `agent_run`
- `agent_step`
- `agent_message`
- `agent_approval`
- `tool_invocation`
- `idempotency_record`
- `run_outbox_event`

### 14.3 Audit Store

- `model_call_log`
- `tool_execution_log`
- `policy_audit_log`

### 14.4 Knowledge Store

- `knowledge_document`
- `knowledge_chunk`
- `embedding_index`

### 14.5 Memory Store

- `session_memory`
- `user_memory`
- `summary_memory`

### 14.6 关键索引建议

建议至少建立以下唯一键或索引：

- `agent_run(idempotency_key)` 唯一索引
- `agent_step(run_id, step_key)` 唯一索引
- `tool_invocation(idempotency_key)` 唯一索引
- `agent_run(root_run_id)` 普通索引
- `agent_message(run_id, created_at)` 普通索引
- `run_outbox_event(run_id, seq_no)` 唯一索引

## 15. 对外接口设计

### 15.1 运行 Agent

`POST /agent-runs`

请求建议：

```json
{
  "agentCode": "support-main",
  "sessionId": "sess_123",
  "userId": "u_001",
  "idempotencyKey": "req_20260524_001",
  "input": {
    "text": "帮我查询今天的异常订单并给出处理建议"
  },
  "options": {
    "stream": true,
    "maxRounds": 8
  }
}
```

建议：

- 请求头支持 `Idempotency-Key`
- 响应中返回 `runId`、`status`、`streamUrl` 或 `subscriptionToken`
- 若命中同一幂等键，直接返回已有 Run，而不是再创建一次

### 15.2 查询 Run

`GET /agent-runs/{runId}`

### 15.3 查询 Steps

`GET /agent-runs/{runId}/steps`

### 15.4 恢复 Run

`POST /agent-runs/{runId}:resume`

建议请求体支持：

```json
{
  "resumeToken": "wait_xxx",
  "source": "approval_callback"
}
```

### 15.5 审批 Step

`POST /agent-runs/{runId}/steps/{stepId}:approve`

请求建议：

```json
{
  "decision": "approved",
  "comment": "允许执行"
}
```

### 15.6 取消 Run

`POST /agent-runs/{runId}:cancel`

### 15.7 回调外部结果

对于异步工具和外部审批，建议提供显式回调接口，而不是复用通用 `resume`：

- `POST /agent-runs/{runId}/tool-invocations/{invocationId}:complete`
- `POST /agent-runs/{runId}/approvals/{approvalId}:complete`

这样可以减少回调方误用，同时保留更清晰的审计语义。

## 16. 事件流设计

对于流式场景，建议输出统一事件：

- `run.created`
- `message.delta`
- `step.started`
- `step.completed`
- `tool.called`
- `tool.completed`
- `approval.requested`
- `approval.resolved`
- `run.completed`
- `run.failed`

建议支持：

- SSE
- WebSocket
- Queue Event

### 16.1 事件信封

建议所有事件统一以下结构：

```json
{
  "eventId": "evt_001",
  "runId": "run_123",
  "seqNo": 17,
  "eventType": "tool.completed",
  "occurredAt": "2026-05-24T15:00:00Z",
  "runStatus": "Running",
  "payload": {}
}
```

关键要求：

- `seqNo` 在单个 Run 内严格递增
- 客户端按 `seqNo` 去重和补洞
- 事件由 `run_outbox_event` 投递，避免丢失

### 16.2 重放与断线续传

建议支持：

- 基于 `runId + lastSeqNo` 的事件补拉
- SSE 或 WebSocket 重连时带上最后消费的 `seqNo`
- `GET /agent-runs/{runId}` 返回当前快照
- `GET /agent-runs/{runId}/events?afterSeqNo=17` 返回增量事件

## 17. 模型抽象层

建议抽象统一的 `ModelGateway`。

职责：

- 模型供应商适配
- 请求格式标准化
- 响应格式标准化
- token、成本、延迟统计

建议标准输出：

- 文本回答
- 结构化 JSON
- tool calls
- reasoning summary
- finish reason

补充要求：

- `tool calls` 必须带结构化参数，不接受纯文本工具意图
- `reasoning summary` 只保留可审计摘要，不依赖完整链路思维
- `finish reason` 建议枚举化，如 `completed`、`tool_call`、`handoff`、`max_turns`

## 18. 知识库与检索设计

### 18.1 Retrieval Pipeline

建议流程：

1. Query Rewrite
2. Hybrid Search
3. Rerank
4. Chunk Selection
5. Context Injection

### 18.2 检索结果结构

建议字段：

- `document_id`
- `chunk_id`
- `content`
- `score`
- `source`
- `metadata`

### 18.3 注入策略

建议支持：

- 直接拼接上下文
- 结构化 citation
- 摘要后拼接

## 19. 可观测性设计

### 19.1 Metrics

建议至少监控：

- run 数量
- 完成率
- 中断率
- 平均轮次
- 工具调用次数
- 模型调用耗时
- 工具耗时
- 审批等待时长
- 单 Run 成本

### 19.2 Logging

建议分层日志：

- API Log
- Runtime Log
- Model Log
- Tool Log
- Policy Log

### 19.3 Tracing

建议为以下对象分配 trace/span：

- AgentRun
- ModelCall
- ToolExecution
- Retrieval
- Approval
- Handoff

## 20. 安全与治理

### 20.1 鉴权

必须支持：

- 用户身份鉴权
- 服务身份鉴权
- 工具级权限控制

### 20.2 数据隔离

必须支持：

- Tenant 隔离
- User 隔离
- 环境隔离

### 20.3 Prompt 安全

建议支持：

- Prompt 模板版本管理
- Prompt 注入防护
- 输入输出审查

### 20.4 工具安全

建议支持：

- allowlist
- denylist
- 参数黑白名单
- 敏感工具审批

### 20.5 审计

关键动作必须可追踪：

- 谁发起了请求
- 调用了哪个 Agent
- 执行了哪些工具
- 谁审批了写操作

### 20.6 策略求值顺序

建议固定求值顺序，降低绕过风险：

1. 身份鉴权
2. 租户和环境隔离
3. Agent allowlist
4. Tool allowlist/denylist
5. 参数级校验
6. 审批策略
7. 执行

## 21. 部署架构建议

### 21.1 MVP 形态

单体服务即可：

- API
- Runtime
- Tool Executor
- DB
- Cache

### 21.2 平台化形态

建议拆分：

- API Service
- Runtime Service
- Tool Service
- Retrieval Service
- Approval Service
- Admin Service

### 21.3 异步化建议

对于耗时任务，建议：

- Run 创建同步返回
- 实际执行异步推进
- 前端通过事件流或轮询获取结果

### 21.4 事务边界建议

推荐把以下动作纳入同一个本地事务：

- 创建或更新 Run
- 创建 Step
- 写入 ToolInvocation
- 写入 Outbox Event

事件实际发送和外部工具调用可在事务外执行，但必须依赖落库后的记录进行重试。

## 22. 版本演进路径

### 22.1 Phase 1

实现：

- 单 Agent
- Tool Use
- 基本上下文
- 基本日志

### 22.2 Phase 2

实现：

- Router Agent
- 子 Agent handoff
- 审批流程
- 检索增强

### 22.3 Phase 3

实现：

- 运行恢复
- 摘要记忆
- 成本治理
- 更完善的可观测性

### 22.4 Phase 4

实现：

- 多 Agent 协同
- 智能策略调整
- 经验学习
- 自动化评测闭环

建议补充里程碑门槛：

- Phase 1 不做多 Agent，但必须完成幂等和状态恢复骨架
- Phase 2 才引入 handoff 和审批，不要在 Phase 1 提前混入复杂编排
- Phase 3 再引入摘要记忆和成本治理，避免第一版被“长期记忆”拖慢

## 23. 参考运行伪代码

```text
RunAgent(request):
  run = create_or_get_run_by_idempotency_key(request)
  definition = load_agent_definition(request.agentCode)
  transition_run(run, from="Created", to="Running")

  while run not terminal:
    context = build_context(run, definition)
    plan = planner.plan(definition, context)
    validate_plan_against_policy(plan, definition, run)
    step = create_step(run, type="Plan", decision=plan)

    if plan.type == "respond":
      write_answer(run, plan.response_message)
      transition_run(run, from="Running", to="Completed")
      return

    if plan.type == "handoff":
      child_run = create_child_run(run, plan.handoff_request)
      wait_child_or_dispatch(child_run, plan.handoff_request.mode)
      continue

    if plan.type == "tool_call":
      invocation = create_tool_invocation(run, step, plan.tool_calls)
      if invocation.mode == "async":
        set_wait_token(run, invocation.callback_token)
        transition_run(run, from="Running", to="WaitingTool")
        return

      result = execute_tool(invocation)
      context = append_tool_result(context, result)
      continue

    if plan.type == "request_approval":
      approval = create_approval_request(run, step, plan.approval_request)
      set_wait_token(run, approval.wait_token)
      transition_run(run, from="Running", to="WaitingApproval")
      return

    if plan.type == "request_human":
      set_wait_token(run, generate_wait_token())
      transition_run(run, from="Running", to="WaitingHuman")
      return

    if plan.type == "fail":
      fail(run, plan.terminate_reason)
      return
```

## 24. 验收标准

一套实现若满足以下条件，可以认为具备完整的通用 Agent 平台骨架：

- 有统一的 Agent Run 主入口
- 有清晰的 Agent Definition 与 Runtime 区分
- 有可恢复的 Run / Step 状态模型
- 有结构化工具体系
- 有路由与 handoff 能力
- 有审批能力
- 有记忆与检索能力
- 有审计与可观测性
- 有安全与权限控制

进一步建议转为可量化验收：

- 同一 `Idempotency-Key` 重放 100 次，只产生 1 个 Run
- 同一异步工具回调重放 100 次，只产生 1 次有效状态推进
- Worker 在任意 Step 后崩溃，恢复后不重复执行不可逆副作用
- 单 Run 事件 `seqNo` 连续且无重复
- 审批超时、工具超时、模型超时三类场景都有自动化测试
- 至少覆盖单 Agent、同步工具、异步工具、审批、handoff、resume 六类主流程

## 25. 总结

一套成熟的 Agent 架构，本质上不是“把大模型接进来”，而是把以下能力系统化：

- 定义能力
- 推进状态
- 控制工具
- 管理风险
- 记录过程
- 支持恢复
- 支持扩展

真正稳定的 Agent 平台，核心不是 Prompt，而是 Runtime。
