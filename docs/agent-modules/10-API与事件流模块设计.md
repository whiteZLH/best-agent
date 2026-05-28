# API 与事件流模块设计

## 1. 模块定位

API 与事件流模块是系统对外契约层，负责统一暴露运行、查询、恢复、审批、取消和异步回调接口，并为前端或外部系统提供流式事件消费能力。

> 当前实现状态：当前已落地的对外接口集中在运行、查询、恢复和 SSE 事件流；审批、取消、外部工具回调、断线补拉、WebSocket / 队列消费仍属于目标态。

## 2. 设计目标

- 对外提供单一主入口
- 统一同步请求与异步执行体验
- 支持幂等、查询、恢复和回调
- 支持 SSE、WebSocket 和事件队列消费

> 当前实现状态：
> - 当前已实现统一主入口 `POST /agent-runs`。
> - 当前已支持查询、恢复和 SSE 事件流。
> - 当前尚未支持审批接口、取消接口、外部回调接口、事件补拉接口以及 WebSocket / 事件队列消费。

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
- 当前请求模型已包含基础运行输入，但尚未完整覆盖文档中的扩展 options 模型
- 当前创建后立即返回，由后台 Worker 异步执行 run

### 3.2 查询接口

目标接口：

- `GET /agent-runs/{runId}`
- `GET /agent-runs/{runId}/steps`

当前实现状态：

- 已落地
- 当前分别返回：
  - Run 快照
  - Step 列表

### 3.3 恢复与取消

目标接口：

- `POST /agent-runs/{runId}:resume`
- `POST /agent-runs/{runId}:cancel`

当前实现状态：

- 当前已落地 `POST /agent-runs/{runId}:resume`
- 当前 resume 语义是：
  - 客户端传入 `waitToken`
  - 客户端传入 `toolResult`
  - 服务端验证 run 处于 `WaitingTool`
  - 重新入队后台继续执行
- 当前尚未落地 `cancel`

### 3.4 审批接口

目标接口：

- `POST /agent-runs/{runId}/steps/{stepId}:approve`

当前实现状态：

- 尚未落地

### 3.5 外部回调

目标接口：

- `POST /agent-runs/{runId}/tool-invocations/{invocationId}:complete`
- `POST /agent-runs/{runId}/approvals/{approvalId}:complete`

当前实现状态：

- 尚未落地
- 当前异步工具恢复仍通过通用 `resume` 接口承接，而不是工具调用专用 complete 回调接口

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
  - `done`
  - `error`
- 当前没有 message delta、approval、tool lifecycle 拆分事件

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

- 当前事件流尚未使用统一事件信封
- 当前 `AgentRunEventBus` 发布的是更轻量的 `eventType + data`
- 当前没有 `eventId`、`seqNo`、`outbox`、补洞机制

## 6. 断线续传

目标设计支持：

- 基于 `runId + lastSeqNo` 的事件补拉
- SSE 或 WebSocket 重连时带最后消费 `seqNo`
- `GET /agent-runs/{runId}` 返回当前快照
- `GET /agent-runs/{runId}/events?afterSeqNo=...` 返回增量

当前实现状态：

- 当前仅支持：
  - `GET /agent-runs/{runId}` 返回当前快照
  - `GET /agent-runs/{runId}/stream` 建立实时 SSE
- 当前不支持基于 `seqNo` 的断线补拉
- 当前事件流更适合单连接实时消费，不适合可靠续传

## 7. 安全与幂等

目标设计：

- `POST /agent-runs` 支持 `Idempotency-Key`
- 回调接口必须校验来源身份和 `wait_token`
- 审批接口必须校验审批人权限
- 取消、恢复和回调都必须幂等

当前实现状态：

- 当前 `AgentRun` 模型中已包含 `IdempotencyKey` 字段
- 当前恢复接口会校验 `waitToken`
- 当前尚未形成完整的外部回调校验、审批权限校验和 cancel 幂等语义

## 8. 风险控制点

目标设计：

- 不复用通用恢复接口承载所有外部回调
- 不让事件流成为唯一真实来源，快照接口仍然必要
- 不允许客户端依赖文本解析判断状态
- 不允许跨租户订阅事件流

当前实现状态：

- 当前最明显的 MVP 特征是：异步工具恢复仍复用通用 `resume` 接口
- 当前快照接口仍然必要且已落地
- 当前 SSE 事件类型较少，客户端仍应以快照与状态字段为准
- 当前尚未看到完整租户隔离与跨租户订阅防护能力

## 9. 验收重点

目标验收：

- 同一幂等键重复调用只返回同一 Run
- SSE/WS 断开后能从最后 `seqNo` 续传
- 审批和异步工具回调接口语义清晰且审计完整
- 快照查询与事件流展示结果一致

当前实现验收重点：

- `POST /agent-runs`、`GET /agent-runs/{runId}`、`GET /agent-runs/{runId}/steps`、`POST /agent-runs/{runId}:resume`、`GET /agent-runs/{runId}/stream` 主链路可用
- SSE 流能实时推送 `step / waiting / done / error`
- `WaitingTool + resume` 能驱动异步工具恢复
- 快照查询与当前事件流展示保持基本一致
- `seqNo` 续传、审批回调、工具专用回调、cancel 接口仍属于后续目标态
