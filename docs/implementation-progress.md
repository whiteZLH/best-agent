# BestAgent MVP 实现进度

更新日期：2026-05-25

## 1. 当前状态

当前仓库已经从纯设计文档阶段推进到可运行的 MVP 脚手架阶段，已完成：

- `.NET 9` 分层解决方案搭建
- `ASP.NET Core Controller` 风格 API
- 官方 `MediatR` 命令、查询与管道行为
- `EF Core + PostgreSQL` 持久化模型
- 初始 `EF Core Migration`
- 单 Agent 同步工具调用主链路
- OpenAI 兼容模型网关抽象与实现
- 单元测试与 HTTP 级集成测试

当前实现目标是单体式 MVP，不是完整平台版本。

## 2. 已完成的项目结构

解决方案文件：

- `BestAgent.sln`

项目结构：

- `src/BestAgent.Api`
- `src/BestAgent.Application`
- `src/BestAgent.Domain`
- `src/BestAgent.Infrastructure`
- `src/BestAgent.Contracts`
- `tests/BestAgent.UnitTests`
- `tests/BestAgent.IntegrationTests`

关键基础文件：

- `global.json`
- `Directory.Build.props`
- `NuGet.Config`
- `.gitignore`
- `.config/dotnet-tools.json`

## 3. 已实现的核心能力

### 3.1 API

已实现 4 个核心接口：

- `POST /agent-runs`
- `GET /agent-runs/{runId}`
- `GET /agent-runs/{runId}/steps`
- `POST /agent-runs/{runId}:resume`

入口控制器：

- `src/BestAgent.Api/Controllers/AgentRunsController.cs`

异常统一映射为 `ProblemDetails`，并在 `Program.cs` 中注册全局异常处理。

### 3.2 应用层与 MediatR

已实现的命令与查询：

- `CreateAgentRunCommand`
- `ResumeAgentRunCommand`
- `GetAgentRunByIdQuery`
- `GetAgentRunStepsQuery`

已实现的管道行为：

- `ValidationBehavior`
- `RequestLoggingBehavior`

运行时编排核心：

- `src/BestAgent.Application/AgentRuns/Services/AgentRuntimeService.cs`

当前主链路支持：

1. 创建 Run
2. 写入输入消息与输入步骤
3. 加载默认 AgentDefinition
4. 组装上下文
5. 调用模型网关生成 `PlanDecision`
6. 若为 `respond`，直接完成 Run
7. 若为 `tool_call`，执行同步工具并再规划一次
8. 第二次规划要求收敛到最终回答

当前约束：

- 单 Run 最多 2 次规划循环
- 不支持并行工具
- 不支持异步工具
- 不支持审批
- 不支持 handoff
- 不支持长期记忆与检索增强

### 3.3 领域模型

已实现的核心实体：

- `AgentDefinition`
- `AgentRun`
- `AgentStep`
- `AgentMessage`
- `ToolInvocation`
- `OutboxEvent`
- `IdempotencyRecord`

统一审计基类：

- `AuditedEntity`

所有实际落库实体均包含以下字段：

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
- 查询默认过滤 `deleted = false`
- 首版不提供删除接口

### 3.4 持久化

数据库上下文：

- `src/BestAgent.Infrastructure/Persistence/BestAgentDbContext.cs`

已建模并迁移的表：

- `agent_definition`
- `agent_run`
- `agent_step`
- `agent_message`
- `tool_invocation`
- `idempotency_record`
- `run_outbox_event`

迁移文件：

- `src/BestAgent.Infrastructure/Persistence/Migrations/20260525011941_InitialCreate.cs`
- `src/BestAgent.Infrastructure/Persistence/Migrations/20260525011941_InitialCreate.Designer.cs`
- `src/BestAgent.Infrastructure/Persistence/Migrations/BestAgentDbContextModelSnapshot.cs`

数据库规则：

- PostgreSQL
- snake_case 表名
- JSON 字段使用 `jsonb`
- 时间字段使用 `timestamp with time zone`
- 启动时自动迁移数据库
- 空库自动 seed 默认 AgentDefinition

### 3.5 模型网关与工具

模型抽象：

- `IModelGateway`

当前基础设施实现：

- `OpenAiCompatibleModelGateway`

配置项：

- `ConnectionStrings:Postgres`
- `OpenAI:BaseUrl`
- `OpenAI:ApiKey`
- `OpenAI:Model`
- `OpenAI:TimeoutSeconds`

工具体系：

- `IToolRegistry`
- `IToolExecutor`

当前示例工具：

- `echo_context`

### 3.6 Outbox

当前已落库记录基础事件，但未实现外部投递：

- `run.created`
- `step.completed`
- `run.completed`
- `run.failed`

## 4. 测试状态

已完成测试：

- 单元测试 8 个
- 集成测试 8 个

已验证通过的命令：

```powershell
dotnet build BestAgent.sln
dotnet test tests/BestAgent.UnitTests/BestAgent.UnitTests.csproj
dotnet test tests/BestAgent.IntegrationTests/BestAgent.IntegrationTests.csproj
```

当前覆盖的典型场景：

- `AgentRun` 状态流转
- `PlanDecision` 解析
- 幂等请求复用同一 Run
- 软删过滤
- 审计字段写入
- 纯 `respond` 流程
- `tool_call -> respond` 流程
- 模型规划异常失败
- 工具不在 allowlist 时失败
- 已完成 Run 的 `resume` 拒绝

## 5. 当前配置与启动方式

主配置文件：

- `src/BestAgent.Api/appsettings.json`
- `src/BestAgent.Api/appsettings.Development.json`

本地启动前需准备：

1. PostgreSQL 数据库
2. OpenAI 兼容接口配置
3. 正确填写 `appsettings.json` 中的连接串与模型参数

推荐启动命令：

```powershell
dotnet tool restore
dotnet build BestAgent.sln
dotnet run --project src/BestAgent.Api
```

## 6. 当前未实现项

以下能力仍停留在设计层，尚未落地：

- 多 Agent / Router / handoff
- 审批与人工协同
- 异步工具与回调恢复
- 检索增强
- 长期记忆
- 成本治理
- 鉴权与租户隔离
- 实际 outbox 投递 worker
- 管理后台与 AgentDefinition 管理界面

## 7. 建议的下一步

推荐按下面顺序继续推进：

1. 增加真实 PostgreSQL 本地运行说明或容器编排
2. 为 `AgentDefinition` 增加管理接口
3. 抽出更清晰的状态机与策略层
4. 引入异步工具与 `WaitingTool` 恢复语义
5. 接入审批流
6. 增加检索与记忆模块

## 8. 关键文件索引

关键实现文件：

- `src/BestAgent.Api/Program.cs`
- `src/BestAgent.Api/Controllers/AgentRunsController.cs`
- `src/BestAgent.Application/DependencyInjection.cs`
- `src/BestAgent.Application/AgentRuns/Services/AgentRuntimeService.cs`
- `src/BestAgent.Application/Planning/PlanDecision.cs`
- `src/BestAgent.Domain/Common/AuditedEntity.cs`
- `src/BestAgent.Infrastructure/DependencyInjection.cs`
- `src/BestAgent.Infrastructure/Persistence/BestAgentDbContext.cs`
- `src/BestAgent.Infrastructure/Model/OpenAiCompatibleModelGateway.cs`
- `src/BestAgent.Infrastructure/Tools/ToolExecutor.cs`

测试文件：

- `tests/BestAgent.UnitTests/Application/AgentRuntimeServiceTests.cs`
- `tests/BestAgent.UnitTests/Domain/QueryFilterTests.cs`
- `tests/BestAgent.IntegrationTests/Api/AgentRunsApiTests.cs`
