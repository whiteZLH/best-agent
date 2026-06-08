# Agent 定义模块设计

## 1. 模块定位

Agent 定义模块负责描述“Agent 是什么”。它是运行时之外的静态能力边界层，用于管理一个 Agent 可使用的模型、工具、知识源、记忆策略、审批策略和路由策略。

> 当前实现状态：当前已落地 AgentDefinition 与 AgentDefinitionVersion 的版本化管理，并已进入运行时主链路；当前 `ApprovalPolicy`、`KnowledgeSources`、`MemoryPolicy` 与 `OutputSchema` 也已开始由运行时消费最小子集；`RoutingPolicy`、`ExecutionPolicy`、`PlannerPolicy`、`ContextPolicy` 与 `AllowedHandoffs` 当前也已支持从 Definition API 写入和查询回显，其中 `RoutingPolicy.strategy == "handoff-first"` 与 `AllowedHandoffs` 也已开始被 handoff 主链路最小消费，但更完整的 Router / Planner / Context / Execution 策略仍未完整落地。

## 2. 设计目标

- 将 Definition 与 Runtime 分离
- 支持版本化、审计和灰度发布
- 让平台可以以配置驱动方式管理 Agent
- 为 Runtime、Router、Tool Resolver 提供统一配置来源

> 当前实现状态：
> - Definition 与 Runtime 已初步分离。
> - 版本化已落地，运行时会绑定已启用版本。
> - 当前配置驱动的核心能力已扩展到：模型、系统提示词、允许工具、知识源、记忆策略、审批策略、最终输出约束、最大轮数、最大成本。
> - Router、handoff 等更多策略字段虽然已进入模型，并已开始可由 API 管理，但大多尚未进入当前主链路。

## 3. 核心对象

### 3.1 AgentDefinition

目标建议至少包含以下字段：

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

当前实现状态：

- 当前 `AgentDefinition` 本体较轻，主要承载：
  - `id`
  - `code`
  - `name`
  - `description`
  - `enabled`
  - `current_version`
- 大部分运行相关配置实际放在 `AgentDefinitionVersion` 中

### 3.2 关联配置

目标关联配置：

- ToolDefinition 引用
- RouteRule
- Prompt 模板版本
- 审批规则
- 上下文构造策略

当前实现状态：

- 当前已稳定进入主链路的关联配置主要是：
  - `default_model`
  - `system_prompt_template`
  - `allowed_tools`
  - `knowledge_sources`
  - `memory_policy`
  - `routing_policy`
  - `approval_policy`
  - `execution_policy`
  - `planner_policy`
  - `context_policy`
  - `allowed_handoffs`
  - `output_schema`
  - `max_turns`
  - `max_cost`
- `ToolDefinition` 已与运行时形成实际关联
- `KnowledgeSources` 当前已开始进入运行时主链路：定义创建与版本创建都可写入知识源 code 列表，Runtime 会按版本配置检索 `knowledge_chunk`
- `MemoryPolicy` 当前已开始进入运行时主链路：定义创建与版本创建都可写入最小 JSON 策略，并用于控制 summary / session / user / knowledge 的上下文装配与最小写回
- `ApprovalPolicy` 当前已开始进入运行时主链路：定义创建与版本创建都可写入最小审批策略 JSON，并在 Runtime 中覆盖全局默认审批配置
- `OutputSchema` 当前也已开始进入运行时主链路：定义创建与版本创建可写入最小 JSON Schema 对象，最终 `respond` 输出会在 run 完成前执行最小结构化校验
- `RoutingPolicy`、`ExecutionPolicy`、`PlannerPolicy`、`ContextPolicy` 与 `AllowedHandoffs` 当前已开始进入 Definition API 与查询读侧：创建定义、创建版本和查询接口都可持久化并回显这些字段；其中 `RoutingPolicy.strategy == "handoff-first"` 当前也已开始被 Runtime 最小消费，用于在首轮模型调用前尝试命中 `RouteRule` 自动进入 handoff，`ContextPolicy.citations` 当前也已开始被 Runtime 最小消费，用于同时控制模型输入中的 citation/source 元数据注入，以及最终答复是否自动追加知识检索 `References`
- `MaxCost` 当前也已开始进入 Runtime 最小执行边界：模型网关会在支持 `usage` 的返回上计算最小调用成本，Worker 会累计 `AgentRun.TotalCost`，并在 `max_cost` 超限时终止继续调用模型
- `RouteRule` 当前已进入最小定义管理态，并开始被 Runtime 最小消费：可按 `agentCode + version` 创建与查询规则记录；当版本级 `RoutingPolicy.strategy == "handoff-first"` 且存在匹配当前输入的启用规则时，Runtime 可直接进入 handoff，但更复杂的 Router Agent / 自动路由策略仍未落地

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

当前实现状态：

- 当前已清晰负责：
  - 默认模型
  - 系统提示词
  - 允许工具集合
  - 知识源列表
  - 记忆策略
  - 审批策略
  - 输出约束
  - 运行上限（`max_turns` / `max_cost`）
  - 版本启用与激活
- 检索、记忆、审批和最终输出约束当前已形成最小执行边界；路由 / handoff / planner / context / execution 策略当前仍主要停留在字段承载与 API 契约层

## 5. 设计原则

- Definition 必须是可审计的静态对象
- Definition 的变更必须支持版本记录
- Runtime 运行时应绑定明确版本，避免执行过程中读取到漂移配置
- 高风险能力默认显式声明，不允许隐式继承

> 当前实现状态：
> - 版本记录已落地。
> - Runtime 当前会先解析已启用版本，再把版本 ID 绑定到 Run。
> - Worker 恢复路径当前也已优先按 `AgentDefinitionVersionId` 重新解析绑定版本，避免已启动 Run 因后续激活新版本而发生配置漂移。
> - 当前 AllowedTools 已形成显式 allowlist，属于“高风险能力显式声明”的已落地点。

## 6. 关键流程

### 6.1 发布流程

目标流程：

1. 创建或修改 AgentDefinition 草稿
2. 绑定模型、工具、知识源和策略
3. 进行配置校验
4. 发布新版本
5. 控制启用、灰度或回滚

当前实现状态：

- 当前已支持：
  - 创建定义
  - 创建新版本
  - 激活版本
- 当前定义创建与版本创建都已开始对 `knowledge_sources`、`memory_policy`、`routing_policy`、`approval_policy`、`execution_policy`、`planner_policy`、`context_policy`、`allowed_handoffs` 与 `output_schema` 做最小写侧校验与持久化
- 当前尚未形成完整灰度发布模型
- 当前配置校验仍偏基础字段层校验，而不是策略级联校验

### 6.2 运行时加载流程

目标流程：

1. API 接收 `agentCode`
2. Runtime 解析当前可用版本
3. 固化到本次 Run 的上下文中
4. 后续所有计划、工具和路由都基于该版本执行

当前实现状态：

- 已落地
- 当前运行时会按 `agentCode` 加载已启用版本
- Run 创建时会保存 `AgentDefinitionVersionId`
- 当前后续模型调用、工具 allowlist、知识检索、记忆装配与最小审批策略都已基于该版本执行
- 路由、handoff 仍未真正接入该加载链路

## 7. 数据存储建议

- `agent_definition`
- `agent_definition_version`
- `tool_definition`
- `route_rule`

建议索引：

- `agent_definition(code, enabled)`
- `agent_definition_version(agent_definition_id, version)`

当前实现状态：

- 前三个方向中，当前已实际主用的是：
  - `agent_definition`
  - `agent_definition_version`
  - `tool_definition`
- `route_rule` 当前已进入 Definition Store 最小管理链路，并已开始进入 Runtime 最小自动路由主链路：当版本级 `RoutingPolicy.strategy == "handoff-first"` 且存在匹配当前输入的启用规则时，Runtime 可在首轮模型调用前直接进入 handoff，但更完整的自动路由策略仍未落地

## 8. 风险控制点

- 禁止一个 Agent 在未声明的情况下访问新工具
- Prompt 模板变更需要与版本绑定
- `allowed_handoffs` 必须是 allowlist
- `max_turns`、`max_cost` 等执行上限应在 Definition 中显式配置

当前实现状态：

- 当前已落地：
  - `allowed_tools` allowlist 控制
  - `system_prompt_template` 与版本绑定
  - `max_turns` / `max_cost` 显式配置并进入运行时
- `allowed_handoffs` 当前已进入 handoff 执行链路：Runtime 会校验目标 Agent 是否在当前版本 allowlist 中，子 Run 也会开始继承父链路收紧后的有效 handoff 边界，避免继续向父未放行的目标 Agent 扩权委派

## 9. 验收重点

目标验收：

- 能按 `agentCode + version` 稳定加载定义
- 配置变更不会影响已启动 Run
- 工具、知识源和 handoff 目标均受 Definition 约束
- 版本回滚后新 Run 可立即使用旧配置

当前实现验收重点：

- 能按 `agentCode` 稳定加载当前启用版本
- Run 绑定具体版本后，不受后续配置变更影响
- 工具集合已受 Definition 中 `allowed_tools` 约束
- 知识源与记忆策略已可从 Definition API 写入并进入运行时最小主链路
- handoff 目标当前已受 Definition 中 `allowed_handoffs` 约束，且子 Run 的可继续委派目标也已开始受父链路边界收紧；但更完整的父子审批策略叠加与多维权限治理仍属后续目标态
