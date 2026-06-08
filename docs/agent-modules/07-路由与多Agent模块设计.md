# 路由与多 Agent 模块设计

## 1. 模块定位

路由与多 Agent 模块负责“该交给谁做”和“多个 Agent 结果如何协同”。它不是具体执行层，而是围绕 handoff、上下文边界、权限继承和结果整合建立可控协作机制。

> 当前实现状态：该模块已开始进入最小可执行阶段。当前已落地 `handoff` 结构化决策、`route_only` / `delegate_and_wait` / `delegate_and_merge` 最小运行时闭环、父子 Run 关系写入与基础读侧可见性；`RouteRule` 当前也已开始进入最小 Runtime 自动路由切片：当版本级 `RoutingPolicy.strategy == "handoff-first"` 且存在匹配当前输入的启用规则时，Runtime 可在首轮模型调用前直接进入 handoff；`delegate_and_merge` 当前也已开始支持单子 Run 的显式 `merge_strategy`（默认 `supervisor_summary`，并支持 `first_success` / `all_results`）；但 Router Agent、Supervisor Agent、更细上下文裁剪与更丰富的多结果合并策略仍未完整落地。

## 2. 设计目标

- 支持 Router Agent 和 Supervisor Agent 模式
- 支持可审计的 handoff
- 控制父子 Run 关系和权限边界
- 支持多种结果整合策略

> 当前实现状态：
> - 当前还没有独立 Router / Supervisor。
> - 当前已支持最小子 Agent 执行与父子 Run 协同，但还没有多 Agent 结果合并逻辑。
> - 相关字段和方向已不再只是模型占位，部分能力已进入实际运行主链路。

## 3. 核心对象

### 3.1 RouteDecision

目标建议字段：

- `target_agent`
- `confidence`
- `reason`
- `context_overrides`
- `tool_overrides`
- `approval_required`

当前实现状态：

- 当前尚未落地独立 `RouteDecision` 对象或 Router Agent
- 当前模型决策已支持：
  - `respond`
  - `tool_call`
  - `handoff`
- 当前 `handoff` 决策已补上最小 route metadata：
  - `targetAgent` / `target_agent`
  - `input`
  - `mode`
  - `reason`
  - `confidence`
  - `context_overrides`
  - `tool_overrides`
  - `approval_required`
- 上述字段当前会进入 handoff 审计 payload 与 `GetAgentRunSteps` 读侧回显；同时在版本级 `RoutingPolicy.strategy == "handoff-first"` 时，Runtime 也已开始最小消费启用 `RouteRule` 做自动路由，但仍未形成独立 Router Agent / 更复杂自动路由执行器

### 3.2 Handoff 模式

目标模式：

- `route_only`
- `delegate_and_wait`
- `delegate_and_merge`

当前实现状态：

- 当前 `route_only`、`delegate_and_wait` 与 `delegate_and_merge` 已落地最小闭环

## 4. 职责边界

负责：

- 判断目标 Agent
- 创建父子 Run 关系
- 定义 handoff 上下文范围
- 定义结果合并方式

不负责：

- 子 Agent 内部如何规划与执行
- 业务工具逻辑
- 审批本身的执行界面

当前实现状态：

- 当前该模块职责已最小进入运行时：可创建子 Run、等待子 Run 完成，并按 handoff 模式将结果直接返回、继续回传父 Run 规划，或进入最小 merge 合成
- handoff 上下文裁剪与结果合并仍未完整进入主链路

## 5. 父子 Run 关系

需要明确记录：

- `parent_run_id`
- `root_run_id`
- `delegated_by_run_id`
- `delegated_by_agent`

父子关系必须能支持结果追溯、权限校验和聚合展示。

当前实现状态：

- 上述字段已存在于 `AgentRun` 模型中
- 当前主链路里：
  - 根 Run 创建时 `root_run_id` 初始化为自身
  - handoff 创建子 Run 时会写入 `parent_run_id`、`root_run_id`、`delegated_by_run_id` 与 `delegated_by_agent`
  - `GetAgentRunById` 已开始回显上述关系字段
- 当前也已存在真正创建父子 Run 关系的运行时流程，且已补上最小子 Run 查询与递归 run tree 查询接口；但更丰富的聚合过滤、统计视图与跨树检索仍未落地

## 6. 上下文边界

目标建议显式定义：

- `context_scope`
- `memory_scope`
- `tool_scope`
- `knowledge_scope`

默认规则：

- 只传任务相关摘要，不透传整段历史
- 长期记忆默认只读
- 子 Agent 默认只见自身 allowlist 工具

当前实现状态：

- 当前已开始最小进入执行约束
- 当前 handoff 输入默认仍以最小任务载荷直接传递给子 Agent
- 当命中的 `RouteRule.context_scope` 或 handoff 决策里的 `context_overrides` 指定 `{"mode":"summary_only"}` 时，子 Run 输入当前会收敛为“委派任务摘要 + 父请求摘要”的最小载荷，而不再只透传原始 handoff 输入
- `summary_only` 当前也已开始进入更真实的执行约束：子 Agent 在模型调用前会进一步关闭 `summary_memory`、`session_memory`、`user_memory` 与 `knowledge_chunk` 注入，避免在输入已摘要化后又被运行时上下文装配重新放大
- `summary_only` 当前也已开始同步收紧子 Agent 的长期记忆写侧：会关闭 `toolResultMemoryEnabled`、`userMemoryWriteEnabled` 与 `summaryMemoryWriteEnabled`，避免子 Run 在仅拿到摘要上下文时继续向 `session_memory`、`user_memory` 或 `summary_memory` 写入放大后的派生结论
- `RouteRule` 的 `context_scope` / `memory_scope` / `tool_scope` / `knowledge_scope` 当前会进入 handoff 审计 payload
- `RouteRule.memory_scope` 当前已开始进入最小执行约束：支持 `{"mode":"read_only"}` 收紧子 Agent 的长期记忆写侧，只关闭 `toolResultMemoryEnabled`、`userMemoryWriteEnabled` 与 `summaryMemoryWriteEnabled`，但不额外关闭 `summary/session/user/knowledge` 的读侧上下文注入
- `RouteRule.memory_scope` 与 handoff `memory_overrides` 当前也已支持 `{"mode":"disabled"}`：会同时关闭子 Agent 的长期记忆写侧，以及 `summary_memory`、`session_memory`、`user_memory` 与 `knowledge_chunk` 的读侧注入
- Planner 产出的 `context_overrides` / `tool_overrides` 当前也会进入 handoff 审计 payload
- 模型 handoff 决策当前也已开始支持最小 `memory_overrides` 元数据，并会进入 handoff 审计 payload；当前支持 `{"mode":"read_only"}` 与 `{"mode":"disabled"}` 两类最小记忆边界收紧语义
- `tool_overrides` / `tool_scope` 当前也已开始最小影响子 Agent 的工具边界：支持用 `{"allowed":[...]}` 进一步收紧子 Agent 原本的 `AllowedTools`，但不会放大权限
- `knowledge_scope` 当前也已开始最小影响子 Agent 的知识边界：支持用 `{"allowed":[...]}` 或 `{"sources":[...]}` 进一步收紧子 Agent 原本的 `KnowledgeSources`，但不会放大检索范围
- 当版本级 `RoutingPolicy.strategy == "handoff-first"` 时，当前也已开始最小自动匹配 `RouteRule`：`match_type = intent|keyword` 时，会对 `match_expression` 中的 `intent` / `keyword` / `contains` / `any` / `all` / `keywords` / `terms` 做大小写不敏感包含匹配，命中后直接进入 handoff
- 当前仍没有真正按这些字段执行更细粒度的长期记忆边界治理或更多上下文/工具策略模式；`memory_scope` 当前虽已支持 `read_only` / `disabled`，但更多模式仍待继续落地

## 7. 权限继承规则

目标规则：

- 子 Agent 不自动继承父 Agent 全部高权限工具
- 审批策略取父子中更严格的一侧
- 租户隔离和用户隔离沿 `root_run_id` 继承

当前实现状态：

- 当前仅落地了最小租户/用户/session 继承：子 Run 会继承父 Run 的 `tenant_id`、`user_id` 与 `session_id`
- 当前已开始进入最小父子权限叠加治理：`tool_overrides` / `tool_scope` 仍可继续收紧子 Agent 的 `AllowedTools`，子 Agent 的 `AllowedHandoffs` 也已开始继承父链路收紧后的有效边界，避免继续向父未放行目标扩权委派；子 Agent 的 `ApprovalPolicy` 当前也已开始按父子更严格的一侧生效，至少会对审批触发等级、需角色等级、允许审批角色与参数规则做最小收紧；但更完整的多维权限治理仍未落地

## 8. 结果合并策略

对于 `delegate_and_merge`，目标建议预先定义：

- `first_success`
- `all_results`
- `majority_vote`
- `supervisor_summary`

不建议把合并留给自由文本拼接。

当前实现状态：

- 当前已支持：
  - `route_only`：子 Run 完成后，父 Run 直接以子结果完成
  - `delegate_and_wait`：子 Run 完成后，父 Run 会将子结果作为 follow-up context 继续生成最终答复
  - `delegate_and_merge`：当前已支持单子 Run 的显式 `merge_strategy`
    - `supervisor_summary`：默认策略；子 Run 完成后，父 Run 会以显式 merge follow-up context 再生成最终答复
    - `first_success`：父 Run 直接以子结果完成，跳过额外 merge 模型调用
    - `all_results`：父 Run 会以更明确的“汇总全部子结果” follow-up context 再生成最终答复
- 当前仍未落地：
  - `majority_vote` 等更复杂 merge strategy
  - 多子 Run / 多结果回收场景下的真正汇聚与裁决逻辑

## 9. 关键流程

目标流程：

1. Planner 产出 `handoff` 决策
2. Runtime 校验目标 Agent 是否在 `allowed_handoffs`
3. 创建子 Run 并固化上下文范围
4. 按 handoff 模式等待或继续
5. 子 Run 完成后按合并策略回收结果
6. 父 Run 再次进入规划或直接响应

当前实现状态：

- 当前已落地其中的最小 `route_only` / `delegate_and_wait` / `delegate_and_merge` 主链路：
  0. 若版本级 `RoutingPolicy.strategy == "handoff-first"` 且命中启用 `RouteRule`，Runtime 当前可在首轮模型调用前直接进入 handoff
  1. Planner/模型可产出 `handoff` 决策
  2. Runtime 会校验目标 Agent 是否在 `allowed_handoffs`
  3. Runtime 会创建子 Run，并将父 Run 切换到 `WaitingHandoff`
  4. 子 Run 完成后：
     - `route_only` 会直接完成父 Run
     - `delegate_and_wait` 会自动恢复父 Run 并继续规划/答复
     - `delegate_and_merge` 会按 `merge_strategy` 恢复父 Run：`first_success` 直接完成，`supervisor_summary` / `all_results` 则基于不同 merge context 继续生成最终答复
- 当前 `approval_required` 也已不再只是 handoff 审计元数据：命中后可真正进入 `WaitingApproval`，并支持 approve / reject / timeout / request-human 收尾；但显式上下文范围固化与多子 Run 结果整合策略仍未落地

## 10. 风险控制点

目标设计：

- 防止无限 handoff 链路
- 防止跨租户或跨用户上下文泄露
- 防止父 Agent 借子 Agent 绕过工具权限
- 合并策略必须可预测、可测试

当前实现状态：

- 当前已开始进入主链路的风险控制包括：
  - handoff 目标必须命中 `AllowedHandoffs`
  - 子 Run 继承父 Run 的租户 / 用户 / session 边界
  - handoff 当前已开始具备最小链路深度治理：可按版本级 `ExecutionPolicy.maxHandoffDepth`（默认 `3`）限制继续委派层级
  - 当前三种模式都只支持单子 Run 的最小恢复/合成路径；`delegate_and_merge` 虽已支持显式 `merge_strategy`，但仍未扩展到多结果汇聚策略
- 防无限 handoff 链、父子更严格权限叠加与上下文泄露防护仍主要停留在设计层

## 11. 验收重点

目标验收：

- 父子 Run 关系可完整追踪
- 子 Agent 的工具和记忆边界按策略生效
- `delegate_and_wait` 与 `delegate_and_merge` 行为清晰可区分
- 多 Agent 结果整合可重放、可解释

当前实现状态：

- 当前已可验收最小切片：
  - 父子 Run 关系已可通过 Run 字段、子 Run 查询与递归 run tree 追踪
  - `route_only`、`delegate_and_wait` 与 `delegate_and_merge` 已具备可区分的运行时行为
  - `GetAgentRunSteps` 现已开始回显 typed `Handoff` 审计信息
- 后续真正实现时，可优先复用当前单 Agent Runtime 已验证的：
  - Run / Step 持久化模型
  - `WaitingTool` 恢复语义
  - `AllowedTools` allowlist 校验模式
