# BestAgent MVP 实现进度

更新日期：2026-05-27

## 1. 当前状态

当前仓库已经从纯设计文档阶段推进到可运行的单体式 MVP 原型，代码现状以仓库实现为准，已完成：

- `.NET 8` 分层解决方案搭建
- `ASP.NET Core Controller` 风格 API
- `MediatR` 命令与查询处理
- `EF Core + PostgreSQL` 持久化接入
- `AgentDefinition` 管理与版本切换接口
- 单次模型调用驱动的 `AgentRun` 创建主链路
- OpenAI 兼容模型网关抽象与实现
- 基础单元测试与控制器测试

当前实现目标仍然是验证主链路和数据模型的 MVP，不是完整平台版本。

## 2. 当前项目结构

解决方案文件：

- `best-agent.sln`

项目结构：

- `BestAgent.Api`
- `BestAgent.Application`
- `BestAgent.Domain`
- `BestAgent.Infrastructure`
- `BestAgent.Api.Tests`

当前仓库同时包含以下辅助文件：

- `.gitignore`
- `docker-compose.yml`
- `table.sql`
- `docs/agent-modules/*`

## 3. 已实现的核心能力

### 3.1 API

当前已实现两组 Controller 接口。

`AgentRun` 接口：

- `POST /agent-runs`
- `POST /agent-runs/{runId}:resume`
- `GET /agent-runs/{runId}`
- `GET /agent-runs/{runId}/steps`

`AgentDefinition` 接口：

- `GET /agent-definitions`
- `GET /agent-definitions/{agentCode}`
- `POST /agent-definitions`
- `GET /agent-definitions/{agentCode}/versions`
- `POST /agent-definitions/{agentCode}/versions`
- `POST /agent-definitions/{agentCode}:activate-version`

入口文件：

- `BestAgent.Api/Program.cs`
- `BestAgent.Api/Controllers/AgentRunsController.cs`
- `BestAgent.Api/Controllers/AgentDefinitionsController.cs`

当前 `Program.cs` 已完成：

- 基础服务注册、AutoMapper 注册
- `UseHttpsRedirection` 和 Controller 映射
- 统一异常处理：`AddProblemDetails()` + `GlobalExceptionHandler`（`IExceptionHandler`）
- 异常映射：`NotFoundException` → 404、`ConflictException` → 409、`InvalidOperationException` → 422、其他 → 500

### 3.2 应用层

当前已实现的命令与查询：

- `CreateAgentRunCommand`
- `ResumeAgentRunCommand`
- `GetAgentRunByIdQuery`
- `GetAgentRunStepsQuery`
- `CreateAgentDefinitionCommand`
- `CreateAgentDefinitionVersionCommand`
- `ActivateAgentDefinitionVersionCommand`
- `GetAgentDefinitionsQuery`
- `GetAgentDefinitionByCodeQuery`
- `GetAgentDefinitionVersionsQuery`

当前应用层的实际结构比较直接：

- 通过 `AddMediatR` 自动注册 handler
- Handler 直接依赖基础设施接口，无中间 Service 层
- `IStepDecisionParser` 负责解析模型 JSON 输出为 `StepDecision`
- 未看到单独的 `ValidationBehavior`
- 未看到单独的 `RequestLoggingBehavior`

`CreateAgentRunCommandHandler` 当前主链路为：

1. 按 `AgentCode` 加载已启用版本
2. 创建 `AgentRun`
3. 写入 `created`、`running` 步骤
4. 委托 `AgentRunLoop.ExecuteAsync` 进入循环（最多 `MaxTurns` 轮）：
   - 通过 `IModelGateway` 调用模型
   - 写入 `model_call` 步骤
   - 若模型返回 `respond`，返回 `AgentLoopCompleted`
   - 若模型返回 `tool_call`，校验权限后通过 `IToolExecutor` 执行工具
   - 若工具返回 `IsPending=true`，写入 Pending 步骤，返回 `AgentLoopSuspended`
   - 若工具同步完成，写入 `tool_call` 步骤，继续循环
5. 根据循环结果：完成 Run 或挂起为 `WaitingTool`
6. 失败时更新 `AgentRun` 为 `Failed`

`ResumeAgentRunCommandHandler` 恢复链路：

1. 加载 `AgentRun`，验证 Status == `WaitingTool` 且 WaitToken 匹配
2. 完成 Pending 步骤（写入工具结果）
3. 设置 Run 为 `Running`，递增 `StatusVersion`
4. 委托 `AgentRunLoop.ExecuteAsync` 继续循环
5. 根据循环结果：完成 Run 或再次挂起

当前架构特点：

- 无 Service 层，Handler 直接编排基础设施接口
- 循环逻辑提取为 `AgentRunLoop` 静态方法类，Create 和 Resume handler 共享
- `StepDecision` 为模型输出的结构化解析结果，支持 `respond` 和 `tool_call` 两种动作
- 支持多轮 tool call 循环（受 `MaxTurns` 限制）
- 支持异步工具：工具返回 `IsPending=true` 时 Run 挂起，通过 resume 接口恢复
- `StatusVersion` 作为 EF Core 并发令牌，防止并发 resume

当前实现约束：

- 未实现审批、人机协同、handoff
- 未实现记忆、检索和多 Agent 编排

### 3.3 领域模型

当前核心持久化实体为：

- `AgentDefinition`
- `AgentDefinitionVersion`
- `AgentRun`
- `AgentStep`

统一审计基类：

- `AuditedEntity`

审计基类字段：

- `last_modifier`
- `last_modify_time`
- `last_modifier_name`
- `create_time`
- `creator_name`
- `creator`
- `deleted`

当前规则：

- 审计字段由应用层手工赋值
- 默认操作者为 `system`
- Repository 查询默认过滤 `deleted = false`
- 首版未提供删除接口

### 3.4 持久化

数据库上下文：

- `BestAgent.Infrastructure/Persistence/BestAgentDbContext.cs`

当前 `DbSet`：

- `AgentDefinitions`
- `AgentDefinitionVersions`
- `AgentRuns`
- `AgentSteps`

当前持久化特点：

- 使用 `Npgsql` 连接 PostgreSQL
- 通过 `ApplyConfigurationsFromAssembly` 应用实体配置
- 启动时由 `DatabaseInitializationHostedService` 调用 `EnsureCreatedAsync`
- 空库时自动 seed 一个 `default-agent`

当前仓库里尚未看到 EF Core Migration 文件，数据库初始化策略以 `EnsureCreated` 为主，而不是 migration 驱动。

### 3.5 模型网关

模型抽象：

- `BestAgent.Application/Models/IModelGateway.cs`

当前实现：

- `BestAgent.Infrastructure/Model/OpenAiCompatibleModelGateway.cs`

配置项：

- `ConnectionStrings:Postgres`
- `OpenAI:BaseUrl`
- `OpenAI:ApiKey`
- `OpenAI:Model`
- `OpenAI:TimeoutSeconds`

当前网关行为：

- 调用 `chat/completions`
- 支持 `system + user` 或仅 `user` 两种消息组合
- 对 HTTP 失败和空响应抛出 `InvalidOperationException`

### 3.6 测试

当前测试项目：

- `BestAgent.Api.Tests`

当前仓库可见测试用例共 `9` 个：

- `AgentRunsControllerTests` 中 `3` 个
- `CreateAgentRunCommandHandlerTests` 中 `2` 个（直接响应 + 工具调用后响应）
- `CreateAgentRunCommandHandlerIntegrationTests` 中 `1` 个
- `ResumeAgentRunCommandHandlerTests` 中 `3` 个（异步工具全流程 + 错误 token + 错误状态）

当前覆盖重点集中在：

- `AgentRun` 创建接口映射
- `GetAgentRunById` 查询返回
- `GetAgentRunSteps` 查询返回
- `CreateAgentRunCommandHandler` 成功流转
- 外部模型联调用例骨架

`AgentDefinition` 相关接口和 handler 目前尚未看到对应测试。

截至 2026-05-27，已实际验证 `dotnet test best-agent.sln` 可通过，结果为 `5/5` 通过。

## 4. 当前配置与启动方式

主配置文件：

- `BestAgent.Api/appsettings.json`
- `BestAgent.Api/appsettings.Development.json`

本地启动前需准备：

1. PostgreSQL 数据库
2. 可用的 OpenAI 兼容接口
3. 正确填写 `ConnectionStrings:Postgres` 与 `OpenAI` 配置

仓库已提供本地 PostgreSQL 容器编排：

- `docker-compose.yml`

推荐启动命令：

```powershell
dotnet build best-agent.sln
dotnet run --project BestAgent.Api
```

## 5. 当前未实现项

以下能力仍停留在设计层，尚未在当前代码中落地：

- 异步执行架构（后台 Worker + SSE 推送）
- 多 Agent / Router / handoff
- 审批与人工协同（`WaitingApproval`、`WaitingHuman`）
- 记忆、检索与长期知识库
- Outbox 事件落库与投递
- 鉴权、租户隔离与后台管理界面
- 工具注册管理接口（当前工具硬编码在 `ToolRegistry`）

## 6. 架构演进：异步执行 + SSE

### 6.1 问题

当前 `POST /agent-runs` 是同步阻塞的——整个 Agent 循环（模型调用 + 工具执行）在 HTTP 请求内完成后才返回响应。这导致：

- 模型调用耗时数秒，多轮循环可能超过 HTTP 超时
- 用户无法实时看到 Agent 的执行进展
- 不符合真实 Agent 产品的交互模式

### 6.2 目标架构

```
POST /agent-runs          → 创建 Run，入队，立即返回 { runId, status: "Running" }
                            ↓
Channel<AgentRunMessage>  → 后台 AgentRunWorker (IHostedService) 消费
                            ↓
AgentRunLoop.ExecuteAsync → 每完成一个 step，通过事件通道推送
                            ↓
GET /agent-runs/{runId}/stream → SSE 连接，实时推送 step 事件给前端
```

### 6.3 设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 后台执行方式 | 进程内 `Channel<T>` + `IHostedService` | MVP 够用，无外部依赖 |
| SSE 推送粒度 | Step 级别事件 | 每个 step 完成时推一个 event（不做 token 级流式） |
| 事件通道 | 进程内 `Channel<AgentRunEvent>` per run | SSE endpoint 订阅对应 run 的 channel |
| WaitingTool 语义 | Worker 内部暂停点 | 不再暴露给 HTTP 层，resume 变为内部回调 |

### 6.4 SSE 事件格式

```
event: step
data: {"stepNo":3,"stepType":"model_call","status":"Completed","output":"..."}

event: step
data: {"stepNo":4,"stepType":"tool_call","status":"Completed","output":"..."}

event: done
data: {"runId":"...","status":"Completed","output":"final answer"}
```

### 6.5 关键组件

- `AgentRunChannel`：封装 `Channel<AgentRunMessage>`，提供 Enqueue / Dequeue
- `AgentRunWorker`：`BackgroundService`，从 channel 消费，执行 `AgentRunLoop`
- `AgentRunEventBus`：per-run 事件分发，Worker 写入，SSE endpoint 读取
- `GET /agent-runs/{runId}/stream`：SSE endpoint，订阅 EventBus 推送事件

## 7. 建议的下一步

推荐按下面顺序继续推进：

1. 实现异步执行架构（Channel + Worker + SSE）
2. 为 `AgentDefinition` 补齐控制器和 handler 测试
3. 工具注册管理接口（动态注册工具，而非硬编码）
4. 审批流（`WaitingApproval` 状态 + 审批回调）
5. 多 Agent / Router / handoff

## 8. 关键文件索引

关键实现文件：

- `BestAgent.Api/Program.cs`
- `BestAgent.Api/Controllers/AgentRunsController.cs`
- `BestAgent.Api/Controllers/AgentDefinitionsController.cs`
- `BestAgent.Application/DependencyInjection.cs`
- `BestAgent.Application/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Commands/ResumeAgentRun/ResumeAgentRunCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunLoop.cs`
- `BestAgent.Application/AgentDefinitions/Commands/CreateAgentDefinition/CreateAgentDefinitionCommandHandler.cs`
- `BestAgent.Application/AgentDefinitions/Commands/CreateAgentDefinitionVersion/CreateAgentDefinitionVersionCommandHandler.cs`
- `BestAgent.Application/AgentDefinitions/Commands/ActivateAgentDefinitionVersion/ActivateAgentDefinitionVersionCommandHandler.cs`
- `BestAgent.Infrastructure/DependencyInjection.cs`
- `BestAgent.Infrastructure/Persistence/BestAgentDbContext.cs`
- `BestAgent.Infrastructure/Persistence/Seeding/DatabaseInitializationHostedService.cs`
- `BestAgent.Infrastructure/Model/OpenAiCompatibleModelGateway.cs`

测试文件：

- `BestAgent.Api.Tests/Controllers/AgentRunsControllerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandlerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandlerIntegrationTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/ResumeAgentRun/ResumeAgentRunCommandHandlerTests.cs`
