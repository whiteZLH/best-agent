# API 与事件流模块设计

## 1. 模块定位

API 与事件流模块是系统对外契约层，负责统一暴露运行、查询、恢复、审批、取消和异步回调接口，并为前端或外部系统提供流式事件消费能力。

## 2. 设计目标

- 对外提供单一主入口
- 统一同步请求与异步执行体验
- 支持幂等、查询、恢复和回调
- 支持 SSE、WebSocket 和事件队列消费

## 3. 核心接口

### 3.1 运行 Agent

`POST /agent-runs`

请求核心字段：

- `agentCode`
- `sessionId`
- `userId`
- `idempotencyKey`
- `input`
- `options.stream`
- `options.maxRounds`

### 3.2 查询接口

- `GET /agent-runs/{runId}`
- `GET /agent-runs/{runId}/steps`

### 3.3 恢复与取消

- `POST /agent-runs/{runId}:resume`
- `POST /agent-runs/{runId}:cancel`

### 3.4 审批接口

- `POST /agent-runs/{runId}/steps/{stepId}:approve`

### 3.5 外部回调

- `POST /agent-runs/{runId}/tool-invocations/{invocationId}:complete`
- `POST /agent-runs/{runId}/approvals/{approvalId}:complete`

## 4. 事件类型

建议统一事件：

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

## 5. 事件信封

建议结构：

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

## 6. 断线续传

建议支持：

- 基于 `runId + lastSeqNo` 的事件补拉
- SSE 或 WebSocket 重连时带最后消费 `seqNo`
- `GET /agent-runs/{runId}` 返回当前快照
- `GET /agent-runs/{runId}/events?afterSeqNo=...` 返回增量

## 7. 安全与幂等

- `POST /agent-runs` 支持 `Idempotency-Key`
- 回调接口必须校验来源身份和 `wait_token`
- 审批接口必须校验审批人权限
- 取消、恢复和回调都必须幂等

## 8. 风险控制点

- 不复用通用恢复接口承载所有外部回调
- 不让事件流成为唯一真实来源，快照接口仍然必要
- 不允许客户端依赖文本解析判断状态
- 不允许跨租户订阅事件流

## 9. 验收重点

- 同一幂等键重复调用只返回同一 Run
- SSE/WS 断开后能从最后 `seqNo` 续传
- 审批和异步工具回调接口语义清晰且审计完整
- 快照查询与事件流展示结果一致
