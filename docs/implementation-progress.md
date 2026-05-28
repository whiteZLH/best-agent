# BestAgent MVP 实现进度

更新日期：2026-05-29

## 1. 当前状态

当前仓库已经从纯设计文档阶段推进到可运行的单体式 MVP 原型，代码现状以仓库实现为准，已完成：

- `.NET 8` 分层解决方案搭建
- `ASP.NET Core Controller` 风格 API
- `MediatR` 命令与查询处理
- `EF Core + PostgreSQL` 持久化接入
- `AgentDefinition` 管理与版本切换接口
- `ToolDefinition` 管理接口
- 基于 `Channel + BackgroundService` 的异步执行架构
- `AgentRun` Step 级事件流与 SSE 推送接口
- OpenAI 兼容模型网关抽象与实现
- `ToolDefinition` 驱动优先的工具执行链路（DB-first，支持 webhook 和本地 handler 兼容回退）
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

当前已实现三组 Controller 接口。

`AgentRun` 接口：

- `POST /agent-runs`
- `POST /agent-runs/{runId}:resume`
- `POST /agent-runs/{runId}/steps/{stepId}:approve`
- `POST /agent-runs/{runId}/steps/{stepId}:reject`
- `GET /agent-runs/{runId}`
- `GET /agent-runs/{runId}/steps`
- `GET /agent-runs/{runId}/stream`

`AgentDefinition` 接口：

- `GET /agent-definitions`
- `GET /agent-definitions/{agentCode}`
- `POST /agent-definitions`
- `GET /agent-definitions/{agentCode}/versions`
- `POST /agent-definitions/{agentCode}/versions`
- `POST /agent-definitions/{agentCode}:activate-version`

`ToolDefinition` 接口：

- `GET /tool-definitions`
- `GET /tool-definitions/{toolName}`
- `POST /tool-definitions`
- `PUT /tool-definitions/{id}`
- `DELETE /tool-definitions/{id}`

入口文件：

- `BestAgent.Api/Program.cs`
- `BestAgent.Api/Controllers/AgentRunsController.cs`
- `BestAgent.Api/Controllers/AgentDefinitionsController.cs`
- `BestAgent.Api/Controllers/ToolDefinitionsController.cs`

当前 `Program.cs` 已完成：

- 基础服务注册、AutoMapper 注册
- `UseHttpsRedirection` 和 Controller 映射
- 统一异常处理：`AddProblemDetails()` + `GlobalExceptionHandler`（`IExceptionHandler`）
- 异常映射：`NotFoundException` → 404、`ConflictException` → 409、`InvalidOperationException` → 422、其他 → 500

### 3.2 应用层

当前已实现的命令与查询：

- `CreateAgentRunCommand`
- `ResumeAgentRunCommand`
- `ApproveAgentRunStepCommand`
- `RejectAgentRunStepCommand`
- `GetAgentRunByIdQuery`
- `GetAgentRunStepsQuery`
- `CreateAgentDefinitionCommand`
- `CreateAgentDefinitionVersionCommand`
- `ActivateAgentDefinitionVersionCommand`
- `GetAgentDefinitionsQuery`
- `GetAgentDefinitionByCodeQuery`
- `GetAgentDefinitionVersionsQuery`
- `CreateToolDefinitionCommand`
- `UpdateToolDefinitionCommand`
- `DeleteToolDefinitionCommand`
- `GetToolDefinitionsQuery`
- `GetToolDefinitionByNameQuery`

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
4. 将 `CreateAgentRunMessage` 入队到 `AgentRunChannel`
5. 由后台 `AgentRunWorker` 消费消息并委托 `AgentRunLoop.ExecuteAsync` 进入循环（最多 `MaxTurns` 轮）：
   - 通过 `IModelGateway` 调用模型
   - 写入 `model_call` 步骤
   - 若模型返回 `respond`，返回 `AgentLoopCompleted`
   - 若模型返回 `tool_call`，先校验权限，再按 `ToolDefinition.SideEffectLevel` 判断是否需要审批
   - 若命中高风险工具，写入带审批 payload 的 Pending `tool_call` 步骤，并返回 `AgentLoopWaitingApproval`
   - 若无需审批，则通过 `IToolExecutor` 执行工具
   - 若工具返回 `IsPending=true`，写入 Pending 步骤，返回 `AgentLoopSuspended`
   - 若工具同步完成，写入 `tool_call` 步骤，继续循环
6. 根据循环结果：完成 Run、挂起为 `WaitingTool`，或挂起为 `WaitingApproval`
7. 失败时更新 `AgentRun` 为 `Failed`

`ResumeAgentRunCommandHandler` 恢复链路：

1. 加载 `AgentRun`，验证 `Status == WaitingTool` 且 `WaitToken` 匹配
2. 将 Run 状态更新为 `Running`
3. 将 `ResumeAgentRunMessage` 入队到 `AgentRunChannel`
4. 由后台 `AgentRunWorker` 完成 Pending 步骤并携带工具结果继续循环
5. 根据循环结果：完成 Run 或再次挂起

`ApproveAgentRunStepCommandHandler` 审批通过链路：

1. 加载 `AgentRun`，验证 `Status == WaitingApproval`
2. 校验最后一个 Pending step 与 `stepId` 匹配，且审批 payload `Decision == Pending`
3. 将 Run 状态先切回 `Running`
4. 将 `ApproveAgentRunStepMessage` 入队到 `AgentRunChannel`
5. 由后台 `AgentRunWorker` 真正执行工具、更新审批 payload 为 `Approved`、完成步骤并继续循环

`RejectAgentRunStepCommandHandler` 审批拒绝链路：

1. 加载 `AgentRun`，验证 `Status == WaitingApproval`
2. 校验最后一个 Pending step 与 `stepId` 匹配，且审批 payload `Decision == Pending`
3. 将 Run 状态先切回 `Running`
4. 将 `RejectAgentRunStepMessage` 入队到 `AgentRunChannel`
5. 由后台 `AgentRunWorker` 更新审批 payload 为 `Rejected`、将 Pending step 标记为失败并终止 Run

当前架构特点：

- 无 Service 层，Handler 直接编排基础设施接口
- 循环逻辑提取为 `AgentRunLoop` 静态方法类，Create 和 Resume 共享
- `StepDecision` 为模型输出的结构化解析结果，支持 `respond` 和 `tool_call` 两种动作
- 支持多轮 tool call 循环（受 `MaxTurns` 限制）
- 支持异步工具：工具返回 `IsPending=true` 时 Run 挂起，通过 resume 接口恢复
- 支持高风险工具审批：命中 `ToolDefinition.SideEffectLevel` 写操作等级时，Run 会切换到 `WaitingApproval`
- 审批支持 approve / reject 两条恢复路径：approve 会继续执行工具并恢复 loop，reject 会终止 Run
- 审批上下文与结果当前通过 `AgentStep.DecisionPayload` 存储，但对外 steps 查询已投影为 typed `Approval` DTO
- `StatusVersion` 作为 EF Core 并发令牌，防止并发 resume / approval 冲突
- `AgentRunEventBus` 按 run 分发事件，SSE endpoint 订阅对应事件流

当前工具执行语义：

- `AgentRunLoop` 统一通过 `IToolExecutor` 执行工具
- `ToolExecutor` 现已调整为 **DB-first**：先查 `ToolDefinition`
- 如果 `ToolDefinition.Enabled == false`，直接拒绝执行
- 如果配置了 `EndpointUrl`，通过 `HttpToolInvoker` 走 HTTP webhook
- 如果未配置 `EndpointUrl`，但本地 `ToolRegistry` 有同名 handler，则作为兼容路径回退到本地 handler
- 如果既无可执行 webhook 配置，也无本地 handler，则返回工具定义不完整错误
- 如果数据库中不存在 `ToolDefinition`，但本地存在 handler，当前仍允许兼容执行
- 从主流 Agent 实现视角看，这一方案属于**正确的 MVP 过渡形态**：工具定义与工具执行已分层、执行权仍由宿主应用掌握、数据库定义开始成为主要事实来源
- 当前仍不是最终平台化形态：`ToolDefinition` 仍偏向 HTTP webhook 配置模型，本地工具与 HTTP 工具仍是并行实现，尚缺统一的 execution kind / resolver 抽象

当前实现约束：

- 已落地最小审批流，但尚未实现更完整的人机协同、人工接管、handoff
- 未实现记忆、检索和多 Agent 编排
- 工具执行已不是单纯依赖本地注册表，但内置工具与 webhook 工具仍是并行模型，尚未抽象为统一 execution kind
- `WaitingTool` / `WaitingApproval` 当前都依赖单步骤挂起语义，尚未演进为更通用的系统级挂起模型
- 审批身份鉴权、审批专用持久化实体（`AgentApproval`）与审批审计链路尚未接入主路径

### 3.3 领域模型

当前核心持久化实体为：

- `AgentDefinition`
- `AgentDefinitionVersion`
- `AgentRun`
- `AgentStep`
- `ToolDefinition`

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
- 已提供 `ToolDefinition` 删除接口

### 3.4 持久化

数据库上下文：

- `BestAgent.Infrastructure/Persistence/BestAgentDbContext.cs`

当前 `DbSet`：

- `AgentDefinitions`
- `AgentDefinitionVersions`
- `AgentRuns`
- `AgentSteps`
- `ToolDefinitions`

当前持久化特点：

- 使用 `Npgsql` 连接 PostgreSQL
- 通过 `ApplyConfigurationsFromAssembly` 应用实体配置
- 启动时由 `DatabaseInitializationHostedService` 调用 `EnsureCreatedAsync`
- 空库时自动 seed 一个 `default-agent`
- `ToolDefinition` 当前已持久化 `EndpointUrl`、`HttpMethod`、`AuthHeaders` 等 webhook 配置字段
- `table.sql` 已与当前 `ToolDefinition` 结构对齐，补齐 webhook 相关列定义

当前仓库里尚未看到 EF Core Migration 文件，数据库初始化策略以 `EnsureCreated` 为主，而不是 migration 驱动。

### 3.5 运行时与事件流

当前异步执行抽象：

- `BestAgent.Application/AgentRuns/Runtime/AgentRunChannel.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunEventBus.cs`
- `BestAgent.Infrastructure/Runtime/AgentRunWorker.cs`

当前运行时行为：

- `POST /agent-runs` 创建 Run 后立即返回，由后台 Worker 异步执行
- `POST /agent-runs/{runId}:resume` 将恢复消息重新入队，由后台 Worker 继续执行
- `POST /agent-runs/{runId}/steps/{stepId}:approve` 将审批通过消息重新入队，由后台 Worker 执行待批工具并继续循环
- `POST /agent-runs/{runId}/steps/{stepId}:reject` 将审批拒绝消息重新入队，由后台 Worker 将待批步骤与 Run 终止为失败态
- Worker 在每个关键 step 完成时向 `AgentRunEventBus` 发布事件
- `GET /agent-runs/{runId}/stream` 通过 SSE 向前端实时推送 `step`、`waiting`、`waiting_approval`、`approval_rejected`、`done`、`error` 事件

当前 SSE 事件粒度：

- `step`：步骤完成
- `waiting`：异步工具挂起
- `waiting_approval`：高风险工具等待审批
- `approval_rejected`：审批被拒绝并终止 run
- `done`：运行完成
- `error`：运行失败

当前实现约束：

- 事件通道是进程内实现，适合单体 MVP
- 未实现事件持久化、重放和跨实例分发
- `WaitingTool` 仍通过 HTTP resume 接口对外暴露，不是纯内部回调模型

### 3.6 模型网关

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

### 3.7 测试

当前测试项目：

- `BestAgent.Api.Tests`

当前仓库可见测试文件共 `15` 个，`[Fact]` 测试用例共 `58` 个：

- `AgentRunsControllerTests` 中 `5` 个
- `AgentDefinitionsControllerTests` 中 `8` 个
- `ToolDefinitionsControllerTests` 中 `6` 个
- `CreateAgentRunCommandHandlerTests` 中 `2` 个
- `CreateAgentRunCommandHandlerIntegrationTests` 中 `1` 个
- `ResumeAgentRunCommandHandlerTests` 中 `3` 个
- `ApproveAgentRunStepCommandHandlerTests` 中 `3` 个
- `RejectAgentRunStepCommandHandlerTests` 中 `1` 个
- `AgentDefinitionCommandHandlerTests` 中 `4` 个
- `ToolDefinitionCommandHandlerTests` 中 `6` 个
- `AgentRunLoopTests` 中 `3` 个
- `AgentRunWorkerTests` 中 `4` 个
- `AgentRunWaitingResumeIntegrationTests` 中 `2` 个
- `ToolExecutorTests` 中 `7` 个
- `HttpToolInvokerTests` 中 `6` 个

当前覆盖重点包括：

- `AgentRun` 创建接口映射
- `GetAgentRunById` 查询返回
- `GetAgentRunSteps` 查询返回（包含 typed `Approval` DTO）
- `GET /agent-runs/{runId}/stream` 的 SSE 输出行为
- `CreateAgentRunCommandHandler` 创建与入队
- `ResumeAgentRunCommandHandler` 恢复与状态校验
- `ApproveAgentRunStepCommandHandler` 审批通过与状态校验
- `RejectAgentRunStepCommandHandler` 审批拒绝与状态校验
- `AgentDefinition` 控制器映射与命令分发
- `AgentDefinition` 创建、创建新版本、激活版本等 handler 逻辑
- `ToolDefinition` 控制器映射与 CRUD 命令分发
- `ToolDefinition` 创建、更新、删除等 handler 逻辑
- `AgentRunLoop` 运行时分支行为（同步工具、异步工具、审批等待）
- `AgentRunWorker` 后台消费执行行为（resume、approve、reject）
- `ToolExecutor` 的 DB-first 工具调度行为（webhook 优先、本地 handler 回退、禁用拦截、缺失定义/配置错误）
- `HttpToolInvoker` 的 HTTP 调用与响应处理
- 异步工具等待 / 恢复集成链路
- 审批等待 / approve / reject 集成链路
- 外部模型联调用例骨架

截至当前代码状态，测试覆盖已经不再局限于 `AgentRun` 主链路，`AgentDefinition`、`ToolDefinition`、运行时与工具执行相关能力也已有较完整的单元测试与部分集成测试覆盖。

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

以下能力仍停留在设计层，尚未在当前代码中完整落地：

- 多 Agent / Router / handoff
- 更完整的人机协同（`WaitingHuman`、人工接管、人工替换结果）
- 记忆、检索与长期知识库
- Outbox 事件落库与投递
- 鉴权、租户隔离与后台管理界面
- 本地工具与 HTTP 工具的统一执行类型抽象，以及更彻底地去除对 `ToolRegistry` 兼容回退的依赖
- 工具定义从“HTTP webhook 配置模型”继续演进为更通用的工具执行定义（支持明确的 executor type / binding / resolution）
- 审批身份鉴权、审批专用持久化实体（如 `AgentApproval`）、审批历史与审计查询
- 事件持久化、跨实例分发与更完整的流式观测能力

## 6. 当前异步执行架构

### 6.1 已落地架构

```text
POST /agent-runs          → 创建 Run，写入初始步骤，入队，立即返回 { runId, status: "Running" }
                            ↓
Channel<AgentRunMessage>  → 后台 AgentRunWorker (BackgroundService) 消费
                            ↓
AgentRunLoop.ExecuteAsync → 每完成一个 step，通过事件总线推送
                            ↓
GET /agent-runs/{runId}/stream → SSE 连接，实时推送 step / waiting / done / error
```

### 6.2 当前设计决策

| 决策 | 选择 | 说明 |
|------|------|------|
| 后台执行方式 | 进程内 `Channel<T>` + `BackgroundService` | MVP 够用，无外部依赖 |
| SSE 推送粒度 | Step 级别事件 | 每个关键 step 完成时推一个 event |
| 事件通道 | 进程内 per-run 订阅通道 | SSE endpoint 订阅对应 run 的 channel |
| WaitingTool 恢复方式 | HTTP resume + 后台继续执行 | 当前仍对外暴露 resume 接口 |

### 6.3 关键组件

- `AgentRunChannel`：封装 `Channel<AgentRunMessage>`，提供 Enqueue / Dequeue
- `AgentRunWorker`：`BackgroundService`，从 channel 消费，执行 `AgentRunLoop`
- `AgentRunEventBus`：per-run 事件分发，Worker 写入，SSE endpoint 读取
- `GET /agent-runs/{runId}/stream`：SSE endpoint，订阅 EventBus 推送事件

## 7. 建议的下一步

推荐按下面顺序继续推进：

1. 将 `CreateAgentRunCommandHandlerIntegrationTests` 从骨架补成真正可执行的集成测试，或明确其保留目的
2. 补充 `Program.cs`、全局异常处理、数据库初始化等启动 / 基础设施相关测试
3. 为本地工具与 HTTP 工具补统一 execution kind / resolver 抽象，进一步收敛 `ToolRegistry` 兼容路径
4. 将 `ToolDefinition` 从当前偏 webhook 的配置模型，演进为更通用的工具执行定义
5. 审批流补齐身份鉴权、审批专用持久化与审计查询
6. 多 Agent / Router / handoff
7. 事件持久化与跨实例分发

## 8. 关键文件索引

关键实现文件：

- `BestAgent.Api/Program.cs`
- `BestAgent.Api/Controllers/AgentRunsController.cs`
- `BestAgent.Api/Controllers/AgentDefinitionsController.cs`
- `BestAgent.Api/Controllers/ToolDefinitionsController.cs`
- `BestAgent.Application/DependencyInjection.cs`
- `BestAgent.Application/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Commands/ResumeAgentRun/ResumeAgentRunCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunChannel.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunEventBus.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunLoop.cs`
- `BestAgent.Application/AgentDefinitions/Commands/CreateAgentDefinition/CreateAgentDefinitionCommandHandler.cs`
- `BestAgent.Application/AgentDefinitions/Commands/CreateAgentDefinitionVersion/CreateAgentDefinitionVersionCommandHandler.cs`
- `BestAgent.Application/AgentDefinitions/Commands/ActivateAgentDefinitionVersion/ActivateAgentDefinitionVersionCommandHandler.cs`
- `BestAgent.Application/Tools/Commands/CreateToolDefinition/CreateToolDefinitionCommandHandler.cs`
- `BestAgent.Application/Tools/Commands/UpdateToolDefinition/UpdateToolDefinitionCommandHandler.cs`
- `BestAgent.Application/Tools/Commands/DeleteToolDefinition/DeleteToolDefinitionCommandHandler.cs`
- `BestAgent.Application/Tools/ToolDefinitionViewModel.cs`
- `BestAgent.Infrastructure/DependencyInjection.cs`
- `BestAgent.Infrastructure/Runtime/AgentRunWorker.cs`
- `BestAgent.Infrastructure/Persistence/BestAgentDbContext.cs`
- `BestAgent.Infrastructure/Persistence/Seeding/DatabaseInitializationHostedService.cs`
- `BestAgent.Infrastructure/Tools/ToolExecutor.cs`
- `BestAgent.Infrastructure/Tools/ToolRegistry.cs`
- `BestAgent.Infrastructure/Tools/HttpToolInvoker.cs`
- `BestAgent.Infrastructure/Model/OpenAiCompatibleModelGateway.cs`

测试文件：

- `BestAgent.Api.Tests/Controllers/AgentRunsControllerTests.cs`
- `BestAgent.Api.Tests/Controllers/AgentDefinitionsControllerTests.cs`
- `BestAgent.Api.Tests/Controllers/ToolDefinitionsControllerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandlerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandlerIntegrationTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/ResumeAgentRun/ResumeAgentRunCommandHandlerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Runtime/AgentRunLoopTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Runtime/AgentRunWorkerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Integration/AgentRunWaitingResumeIntegrationTests.cs`
- `BestAgent.Api.Tests/AgentDefinitions/Commands/AgentDefinitionCommandHandlerTests.cs`
- `BestAgent.Api.Tests/Tools/Commands/ToolDefinitionCommandHandlerTests.cs`
- `BestAgent.Api.Tests/Tools/ToolExecutorTests.cs`
- `BestAgent.Api.Tests/Tools/HttpToolInvokerTests.cs`
