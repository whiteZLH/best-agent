# API 与事件流模块设计

## 1. 模块定位

API 与事件流模块是系统对外契约层，负责统一暴露运行、查询、恢复、审批、取消和异步回调接口，并为前端或外部系统提供流式事件消费能力。

> 当前实现状态：当前已落地的对外接口集中在运行、查询、恢复、取消、审批、外部工具 / 审批 complete 回调、SSE 事件流和 run outbox 事件补拉；WebSocket / 队列消费仍属于目标态。

## 2. 设计目标

- 对外提供单一主入口
- 统一同步请求与异步执行体验
- 支持幂等、查询、恢复和回调
- 支持 SSE、WebSocket 和事件队列消费

> 当前实现状态：
> - 当前已实现统一主入口 `POST /agent-runs`。
> - 当前已支持查询、恢复、取消、审批、人工接管、外部工具 / 审批 complete 回调、SSE 事件流和 `afterSeqNo` 事件补拉。
> - 当前已开始对 Run 相关 API 入口施加最小 scope 边界：若请求已带认证身份，则会优先消费其中的 `tenant/user/session` scope；若尚未接入正式鉴权，也可显式传入 `X-BestAgent-Tenant-Id` / `X-BestAgent-User-Id` / `X-BestAgent-Session-Id`。在存在这些 scope 时，Run 查询、恢复、取消、审批、人机协同、外部 tool/approval complete 回调与 SSE stream 入口会校验当前 Run 是否仍在同一边界内。
> - 当前尚未支持 WebSocket / 事件队列消费。

## 3. 核心接口

### 3.1 运行 Agent

目标接口：

`POST /agent-runs`

目标请求核心字段：

- `agentCode`
- `sessionId`
- `userId`
- `idempotencyKey`
- `input`
- `options.stream`
- `options.maxRounds`

当前实现状态：

- 当前已落地 `POST /agent-runs`
- 当前请求模型已包含基础运行输入，并支持请求体 `idempotencyKey`
- 当前也支持 HTTP `Idempotency-Key` header，且 header 优先于请求体字段
- 同一幂等键重复调用会复用已有 Run，不重复创建初始 steps 或重复入队
- 当前已开始补充最小扩展 options 模型：
  - `options.maxRounds` 已可按请求级收紧当前 run 的有效 `MaxTurns`，但不会突破 `AgentDefinitionVersion.MaxTurns`
  - `options.stream` 已进入请求契约层，当前主要作为兼容占位；实际流式消费仍通过 `GET /agent-runs/{runId}/stream`
- 当前创建后立即返回，由后台 Worker 异步执行 run

### 3.2 查询接口

目标接口：

- `GET /agent-runs/{runId}`
- `GET /agent-runs/{runId}/steps`

工具定义相关接口：

- `GET /tool-definitions`
- `GET /tool-definitions/{toolName}`
- `POST /tool-definitions`
- `PUT /tool-definitions/{id}`

当前实现状态：

- 已落地
- 当前分别返回：
  - Run 快照
  - Step 列表
  - 审批列表
  - run outbox 事件列表，支持 `afterSeqNo` 增量补拉
- `GET /agent-runs/{runId}/steps` 当前除 `ModelCall`、`Approval`、`Handoff`、`HumanWait` 等 typed 读侧外，也已开始为显式 `retrieval` step 返回最小 typed `Retrieval` 视图
- `GET /agent-runs/{runId}/events` 当前除保留脱敏后的原始 `Payload` 外，也已开始补充最小 typed `Data` 读侧：会显式返回 `stepNo`、`stepType`、`status`、`output`、`error`，并在 `error` 命中结构化载荷时进一步返回 typed `ModelFailure` / `ToolFailure`
- 当前事件 `Data` 读侧也已开始补充 `Retrieval` / `Approval` / `Handoff` / `HumanWait` 结构化视图；事件 payload 内的工具输入 / 输出字段继续按既有最小递归脱敏策略返回
- 异步 `waiting` 事件当前也已开始补充 typed `ToolInvocation` 视图，显式返回 `invocationId`、`toolName`、`mode`、`status` 与 `callbackToken`
- `GET /agent-runs/{runId}` 当前快照已包含 `WaitToken`、`CurrentStepNo` 与 `InterruptReason`，便于前端在事件流中断后快速恢复等待/失败态
- 当前快照读侧也已开始补充最小等待定位字段：`CurrentStepId`、`WaitStepType`，以及适用于外部回调 / 审批恢复的 `CurrentInvocationId` / `CurrentApprovalId`
- 当前快照读侧也已开始在等待态补充 typed 上下文：可按当前步骤类型直接返回 `CurrentToolInvocation`、`CurrentApproval`、`CurrentHumanWait` 或 `CurrentHandoff`
- `GET /agent-runs/{runId}/children` 与 `GET /agent-runs/{runId}/tree` 当前也已开始沿用同一套等待态快照语义：等待中的子 Run 节点会同步返回等待定位字段与 typed 上下文
- `GET /tool-definitions*` 当前也已落地，并开始同时返回两层工具执行定义视图：
  - 向后兼容的 flat 字段：`ExecutionKind`、`ExecutionBinding`、`EndpointUrl`、`HttpMethod`、`AuthHeaders`
  - 新的结构化 `Execution` 读侧：显式区分 `webhook`、`local_handler`、`inline_result`，并回显当前 `version`
- 对仍停留在 legacy webhook flat 字段、尚未完成启动归一的存量记录，当前查询读侧也会即时合成结构化 `Execution.webhook`
- `GET /tool-definitions*` 当前也已开始补充结构化 `Policies` 读侧：
  - 向后兼容的 flat 字段：`RetryPolicy`、`AuthPolicy`、`IdempotencyPolicy`、`CompensationPolicy`
  - 新的结构化 `Policies` 读侧：显式区分 `Retry`、`Auth`、`Idempotency`、`Compensation`
- 兼容保留的 flat 策略字段当前也会按 canonical 规范化结果返回，而不再把 legacy `retry-once` / `disabled` / `bearer` 一类旧值原样透出
- 若 `AuthPolicy` 采用结构化 JSON，当前查询读侧也会对其中常见敏感字段做最小递归脱敏，同时继续保留 `scheme` 等非敏感策略信息
- 兼容 flat 字段当前也已开始按执行类型真实返回：
  - `webhook` 继续返回 `EndpointUrl` / `HttpMethod` / `AuthHeaders`
  - `local_handler` / `inline_result` 不再返回假的 HTTP flat 字段值
- 兼容保留的 flat `ExecutionBinding` 当前也会和结构化 `Execution` 视图保持一致的最小脱敏语义：
  - webhook binding 中的认证头会脱敏
  - `inline_result` binding 中的常见敏感运行时字段也会递归脱敏
- `POST/PUT /tool-definitions*` 当前也已开始同时兼容两层写入视图：
  - 向后兼容的 flat / binding 字段组合
  - 新的结构化 `Execution` 写侧：可直接提交 `webhook`、`local_handler`、`inline_result` 执行定义，并可显式带 `version`
- `POST/PUT /tool-definitions*` 当前也已开始同时兼容两层策略写入视图：
  - 向后兼容的策略字符串字段组合
  - 新的结构化 `Policies` 写侧：可直接提交 `Retry`、`Auth`、`Idempotency`、`Compensation` 策略定义
- 结构化写侧当前会在 controller 层先归一化为既有命令参数，再复用后续策略校验与持久化链路
- 当结构化 `Execution` 与旧的 flat / binding 字段同时提供时，当前要求二者语义一致；若冲突则直接拒绝请求，而不再静默偏向其中一侧
- 当结构化 `Policies` 与旧的策略字符串字段同时提供时，当前会按字段逐项校验：同一策略字段并存时必须语义一致，未提供结构化值的其他策略字段仍可继续走旧 flat 输入
- 其中 webhook 认证头、callback secret 和运行时常见敏感字段仍会按现有最小脱敏策略返回
- `POST /agent-definitions` 与 `POST /agent-definitions/{agentCode}/versions` 当前也已开始支持 Definition 级 `knowledgeSources`、`memoryPolicy` 与 `approvalPolicy` 输入，并会在查询接口中原样返回对应读侧字段

### 3.3 恢复与取消

目标接口：

- `POST /agent-runs/{runId}:resume`
- `POST /agent-runs/{runId}:cancel`
- `POST /agent-runs/{runId}:request-human`
- `POST /agent-runs/{runId}/steps/{stepId}:complete-human`

当前实现状态：

- 当前已落地 `POST /agent-runs/{runId}:resume`
- 当前 resume 语义是：
  - 客户端传入 `waitToken`
  - 客户端传入 `toolResult`
  - 服务端验证 run 处于 `WaitingTool`
  - 重新入队后台继续执行
- 当前已落地 `POST /agent-runs/{runId}:cancel`
- 当前 cancel 语义是：
  - 可取消 `Running` / `WaitingTool` / `WaitingApproval`
  - 若最后一步是 Pending，则同步标记为 `Cancelled`
  - Run 切换为 `Cancelled`，清空 `CurrentWaitToken`，记录取消原因
  - 写入 `cancelled` outbox 事件并推送 SSE
  - 对已终态 Run 返回当前状态，不重复写入副作用
- 当前已落地 `POST /agent-runs/{runId}:request-human`
- 当前 request-human 语义是：
  - 可从 `Running` / `WaitingTool` / `WaitingApproval` 切换到 `WaitingHuman`
  - 若最后一步仍是 Pending，会先标记为 `Cancelled`
  - 创建 Pending `human_wait` step，生成新的 `waitToken`
  - 写入 `waiting_human` outbox 事件并推送 SSE
- 当前已落地 `POST /agent-runs/{runId}/steps/{stepId}:complete-human`
- 当前 complete-human 语义是：
  - 校验 Run、`waitToken` 和当前 pending `human_wait` step
  - 先将 Run 切回 `Running` 并入队后台消息
  - 后台 Worker 再按人工结果直接完成 Run，或按人工终止结束 Run

### 3.4 审批接口

目标接口：

- `POST /agent-runs/{runId}/steps/{stepId}:approve`
- `POST /agent-runs/{runId}/steps/{stepId}:reject`

当前实现状态：

- 当前已落地 approve / reject 最小审批闭环
- 当前审批接口会解析认证上下文或请求体中的审批人信息
- 当前已通过 `DefaultApprovalAuthorizer` 执行最小授权规则

### 3.5 外部回调

目标接口：

- `POST /agent-runs/{runId}/tool-invocations/{invocationId}:complete`
- `POST /agent-runs/{runId}/approvals/{approvalId}:complete`

当前实现状态：

- 当前已落地 `POST /agent-runs/{runId}/tool-invocations/{invocationId}:complete`
- 当前工具 complete 回调会校验：
  - Run 处于 `WaitingTool`
  - `waitToken` 匹配
  - `{invocationId}` 对应当前 pending `ToolInvocation`
  - invocation 关联 step 仍是当前 pending `tool_call`
  - 校验通过后切回 `Running` 并入队后台恢复消息
  - 可携带 HTTP `Idempotency-Key`，同 key 同 payload 会重放已记录结果，不重复入队；同 key 不同 payload 会被拒绝
- 当前已落地 `POST /agent-runs/{runId}/approvals/{approvalId}:complete`
- 当前审批 complete 回调会通过 `approvalId` 查询 `AgentApproval`，校验当前 pending 审批和审批授权，再按 `Approved` / `Rejected` 入队后台 approve / reject 消息
- 当前审批 complete 回调同样支持 HTTP `Idempotency-Key` 重放保护
- 当前 tool complete / approval complete 都可按 `WebhookSecurity` 配置要求校验 `X-BestAgent-Signature` HMAC 签名
- 当前工具回调已支持优先使用 `ToolDefinition.CallbackSecret` 校验签名，未配置时回退到全局 `ToolCallbackSecret`
- 当前审批回调已支持 `ApprovalCallbackSecret` 与 `ApprovalCallbackSecrets` 轮换
- 当前工具 complete 已开始按 `ToolInvocation` 实体驱动恢复，但通用 `resume` 入口仍保留 `waitToken` 兼容语义

## 4. 事件类型

目标统一事件：

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

当前实现状态：

- 当前 SSE 事件模型更轻量，实际使用的是：
  - `step`
  - `waiting`
  - `waiting_approval`
  - `waiting_human`
  - `approval_timed_out`
  - `approval_rejected`
  - `cancelled`
  - `done`
  - `error`
- 当前没有 message delta、approval、tool lifecycle 拆分事件
- 当前 `approval_timed_out` 事件已开始与 Run 终态对齐：审批超时后 outbox `runStatus` 与事件 payload `status` 会标记为 `TimedOut`

## 5. 事件信封

目标建议结构：

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

关键约束：

- `seqNo` 在单个 Run 内严格递增
- 客户端按 `seqNo` 去重和补洞
- 事件从 outbox 投递，避免丢失

当前实现状态：

- 当前 `AgentRunEventBus` 发布的是更轻量的 `eventType + data`
- Worker 生命周期事件会先写入 `run_outbox_event`，再推送进程内 SSE
- `GET /agent-runs/{runId}/events?afterSeqNo=...` 返回带 `eventId`、`seqNo`、`runStatus`、`payload`、`publishStatus` 的 outbox 事件
- 当前 SSE 实时通道也已开始补齐最小统一事件信封：
  - `data` 中会显式返回 `eventId`、`runId`、`seqNo`、`eventType`、`runStatus`、`occurredAt` 与 typed `data`
  - typed `data` 当前除 `ModelCall` 外，也已同步回显 `ToolInvocation`、`Approval` / `Handoff` / `HumanWait`
  - 当事件带 `seqNo` 时，也会通过 SSE `id:` 输出，便于客户端记录最近消费位置
- 当前 SSE 重连已开始最小消费 `Last-Event-ID`：建连时会先注册进程内缓冲订阅，再按 `afterSeqNo = Last-Event-ID` 回放 outbox 事件，最后按 `seqNo` 去重切到实时流
- 在当前单体 MVP 的单进程事件总线模型下，上述切换已能覆盖“回放期间有新事件到达”的最小补洞场景；更高阶的跨实例一致性仍依赖后续 outbox/外部队列方案

## 6. 断线续传

目标设计支持：

- 基于 `runId + lastSeqNo` 的事件补拉
- SSE 或 WebSocket 重连时带最后消费 `seqNo`
- `GET /agent-runs/{runId}` 返回当前快照
- `GET /agent-runs/{runId}/events?afterSeqNo=...` 返回增量

当前实现状态：

- 当前已支持：
  - `GET /agent-runs/{runId}` 返回当前快照
  - `GET /agent-runs/{runId}/events?afterSeqNo=...` 从 `run_outbox_event` 增量补拉事件
  - `GET /agent-runs/{runId}/stream` 建立实时 SSE
- 当前 SSE 事件已开始输出 `id: {seqNo}`，服务端也已支持基于 `Last-Event-ID` 的最小自动补拉；若回放结果已包含终态事件，则本次 SSE 会在补发后直接结束
- 在当前单进程 EventBus 模型下，SSE 已通过“先注册缓冲订阅、再回放、最后按 `seqNo` 去重”的方式补上最小重连窗口；更高可靠的跨实例补洞仍属于后续目标态

## 7. 安全与幂等

目标设计：

- `POST /agent-runs` 支持 `Idempotency-Key`
- 回调接口必须校验来源身份和 `wait_token`
- 审批接口必须校验审批人权限
- 取消、恢复和回调都必须幂等

当前实现状态：

- 当前 `AgentRun` 模型中已包含 `IdempotencyKey` 字段
- 当前 `POST /agent-runs` 已支持 `Idempotency-Key`，重复调用只返回同一 Run
- 当前恢复接口会校验 `waitToken`
- 当前工具 complete 回调会校验 `waitToken`
- 当前审批接口已落地最小审批权限校验
- 当前审批 complete 回调复用同一套审批权限校验
- 当前工具 complete 与 approval complete 会使用 `IdempotencyRecord` 记录同一 `Idempotency-Key` 的完成结果，重复回调直接重放，不重复入队
- 当前取消接口对已终态 run 采用幂等返回，不重复写入副作用
- 当前已形成增强版外部回调来源认证：可按配置要求校验 HMAC 签名，支持 per-tool callback secret 与审批 secret 轮换；`ToolInvocation` 实体驱动恢复已进入主链路，但更彻底的恢复模型仍属后续目标态

## 8. 风险控制点

目标设计：

- 不复用通用恢复接口承载所有外部回调
- 不让事件流成为唯一真实来源，快照接口仍然必要
- 不允许客户端依赖文本解析判断状态
- 不允许跨租户订阅事件流

当前实现状态：

- 当前通用 `resume` 和工具专用 complete 回调都可驱动 `WaitingTool` 恢复；后续应逐步收敛到 `ToolInvocation` 实体驱动的专用回调模型
- 当前快照接口仍然必要且已落地
- 当前 SSE 事件类型较少，客户端仍应以快照与状态字段为准
- 当前事件补拉依赖 Worker 生命周期事件 outbox，尚未覆盖 message delta 等更细事件
- 当前已开始具备最小的 request-side tenant/user/session 边界校验，但完整租户隔离、服务身份鉴权与跨实例订阅防护能力仍未落地

## 9. 验收重点

目标验收：

- 同一幂等键重复调用只返回同一 Run
- SSE/WS 断开后能从最后 `seqNo` 续传
- 审批和异步工具回调接口语义清晰且审计完整
- 快照查询与事件流展示结果一致

当前实现验收重点：

- `POST /agent-runs`、`GET /agent-runs/{runId}`、`GET /agent-runs/{runId}/steps`、`GET /agent-runs/{runId}/approvals`、`GET /agent-runs/{runId}/events`、`POST /agent-runs/{runId}:resume`、`POST /agent-runs/{runId}:cancel`、`approve/reject`、`tool complete`、`approval complete`、`GET /agent-runs/{runId}/stream` 主链路可用
- `request-human / complete-human` 人工接管主链路可用
- SSE 流能实时推送 `step / waiting / waiting_approval / waiting_human / approval_timed_out / approval_rejected / cancelled / done / error`
- `GET /agent-runs/{runId}/events?afterSeqNo=...` 能从 outbox 补拉事件
- 同一 `Idempotency-Key` 重复创建只返回同一 Run
- 同一外部 complete 回调 `Idempotency-Key` 重放不会重复入队
- `WaitingTool + resume` 能驱动异步工具恢复
- `tool-invocations/{invocationId}:complete` 能以专用回调形式驱动异步工具恢复
- `WaitingApproval + approve/reject` 能驱动审批恢复或终止
- `approvals/{approvalId}:complete` 能以外部审批回调形式驱动 approve / reject
- `cancel` 能终止运行并通过 outbox / SSE 公开取消事件
- 快照查询与当前事件流展示保持基本一致
- 完整 `ToolInvocation` 实体、更强的外部回调来源认证模型和 WebSocket / 队列消费仍属于后续目标态
