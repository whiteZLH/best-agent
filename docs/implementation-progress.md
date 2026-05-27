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

当前 `Program.cs` 仅完成基础服务注册、AutoMapper 注册、`UseHttpsRedirection` 和 Controller 映射，尚未接入统一异常处理中间件或 `ProblemDetails` 输出规范。

### 3.2 应用层

当前已实现的命令与查询：

- `CreateAgentRunCommand`
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
- 未看到单独的 `ValidationBehavior`
- 未看到单独的 `RequestLoggingBehavior`
- 未看到独立的 `AgentRuntimeService`

`CreateAgentRunCommandHandler` 当前主链路为：

1. 按 `AgentCode` 加载已启用版本
2. 创建 `AgentRun`
3. 写入 `created`、`running` 步骤
4. 通过 `IModelGateway` 调用一次模型
5. 写入 `model_call` 步骤
6. 成功时更新 `AgentRun` 为 `Completed`
7. 失败时更新 `AgentRun` 为 `Failed`

当前实现约束：

- 仅支持单次模型调用，不存在规划循环
- 未实现 `resume`
- 未实现工具调度执行
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

当前仓库可见测试用例共 `5` 个：

- `AgentRunsControllerTests` 中 `3` 个
- `CreateAgentRunCommandHandlerTests` 中 `1` 个
- `CreateAgentRunCommandHandlerIntegrationTests` 中 `1` 个

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

- `POST /agent-runs/{runId}:resume`
- 统一异常处理与 `ProblemDetails`
- 规划器与显式状态机
- 工具注册、工具执行和工具回填链路
- 多 Agent / Router / handoff
- 审批与人工协同
- 记忆、检索与长期知识库
- Outbox 事件落库与投递
- 鉴权、租户隔离与后台管理界面

## 6. 建议的下一步

推荐按下面顺序继续推进：

1. 为 `AgentDefinition` 补齐控制器和 handler 测试，形成第二条稳定主链路
2. 为 `Program.cs` 增加统一异常映射和 `ProblemDetails` 输出
3. 抽出 `CreateAgentRun` 的运行时编排服务，避免 handler 继续膨胀
4. 设计并实现最小可用的 `resume / waiting` 状态语义
5. 在具备可恢复状态后，再引入异步工具、审批流和多 Agent 能力

## 7. 关键文件索引

关键实现文件：

- `BestAgent.Api/Program.cs`
- `BestAgent.Api/Controllers/AgentRunsController.cs`
- `BestAgent.Api/Controllers/AgentDefinitionsController.cs`
- `BestAgent.Application/DependencyInjection.cs`
- `BestAgent.Application/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandler.cs`
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
