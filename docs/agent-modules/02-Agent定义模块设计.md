# Agent 定义模块设计

## 1. 模块定位

Agent 定义模块负责描述“Agent 是什么”。它是运行时之外的静态能力边界层，用于管理一个 Agent 可使用的模型、工具、知识源、记忆策略、审批策略和路由策略。

## 2. 设计目标

- 将 Definition 与 Runtime 分离
- 支持版本化、审计和灰度发布
- 让平台可以以配置驱动方式管理 Agent
- 为 Runtime、Router、Tool Resolver 提供统一配置来源

## 3. 核心对象

### 3.1 AgentDefinition

建议至少包含以下字段：

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

### 3.2 关联配置

- ToolDefinition 引用
- RouteRule
- Prompt 模板版本
- 审批规则
- 上下文构造策略

## 4. 职责边界

负责：

- 定义 Agent 能力边界
- 定义默认模型与工具集合
- 定义记忆、检索、审批和路由策略
- 维护版本和启停状态

不负责：

- 执行状态推进
- 即时权限判定结果缓存
- 模型输出解释
- 工具实际执行

## 5. 设计原则

- Definition 必须是可审计的静态对象
- Definition 的变更必须支持版本记录
- Runtime 运行时应绑定明确版本，避免执行过程中读取到漂移配置
- 高风险能力默认显式声明，不允许隐式继承

## 6. 关键流程

### 6.1 发布流程

1. 创建或修改 AgentDefinition 草稿
2. 绑定模型、工具、知识源和策略
3. 进行配置校验
4. 发布新版本
5. 控制启用、灰度或回滚

### 6.2 运行时加载流程

1. API 接收 `agentCode`
2. Runtime 解析当前可用版本
3. 固化到本次 Run 的上下文中
4. 后续所有计划、工具和路由都基于该版本执行

## 7. 数据存储建议

- `agent_definition`
- `agent_definition_version`
- `tool_definition`
- `route_rule`

建议索引：

- `agent_definition(code, enabled)`
- `agent_definition_version(agent_definition_id, version)`

## 8. 风险控制点

- 禁止一个 Agent 在未声明的情况下访问新工具
- Prompt 模板变更需要与版本绑定
- `allowed_handoffs` 必须是 allowlist
- `max_turns`、`max_cost` 等执行上限应在 Definition 中显式配置

## 9. 验收重点

- 能按 `agentCode + version` 稳定加载定义
- 配置变更不会影响已启动 Run
- 工具、知识源和 handoff 目标均受 Definition 约束
- 版本回滚后新 Run 可立即使用旧配置
