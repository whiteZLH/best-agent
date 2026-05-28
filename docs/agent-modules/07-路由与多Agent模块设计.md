# 路由与多 Agent 模块设计

## 1. 模块定位

路由与多 Agent 模块负责“该交给谁做”和“多个 Agent 结果如何协同”。它不是具体执行层，而是围绕 handoff、上下文边界、权限继承和结果整合建立可控协作机制。

> 当前实现状态：该模块目前基本仍处于目标设计阶段。当前运行时仅支持单 Agent 主链路，尚未落地 Router Agent、Supervisor Agent、handoff、父子 Run 协同和结果合并。

## 2. 设计目标

- 支持 Router Agent 和 Supervisor Agent 模式
- 支持可审计的 handoff
- 控制父子 Run 关系和权限边界
- 支持多种结果整合策略

> 当前实现状态：
> - 当前还没有独立 Router / Supervisor。
> - 当前也没有子 Agent 执行和多 Agent 结果合并逻辑。
> - 相关字段和方向已体现在数据模型设计中，但尚未进入实际运行主链路。

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

- 尚未落地当前代码中的 `RouteDecision`
- 当前模型决策仅支持：
  - `respond`
  - `tool_call`
- 当前不支持 `handoff` 决策类型

### 3.2 Handoff 模式

目标模式：

- `route_only`
- `delegate_and_wait`
- `delegate_and_merge`

当前实现状态：

- 尚未落地

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

- 当前该模块的职责尚未进入实际运行时
- 单 Agent Runtime 当前不会创建子 Run，也不会执行 handoff 上下文裁剪和结果合并

## 5. 父子 Run 关系

需要明确记录：

- `parent_run_id`
- `root_run_id`
- `delegated_by_run_id`
- `delegated_by_agent`

父子关系必须能支持结果追溯、权限校验和聚合展示。

当前实现状态：

- 上述字段已存在于 `AgentRun` 模型中
- 但当前主链路里：
  - `root_run_id` 在创建 Run 时会初始化为自身
  - 其余父子协作相关字段尚未真正参与运行逻辑
- 当前并没有真正创建父子 Run 关系的运行时流程

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

- 尚未落地
- 当前没有多 Agent 上下文裁剪，因此也没有显式 context / memory / tool / knowledge scope 实施逻辑

## 7. 权限继承规则

目标规则：

- 子 Agent 不自动继承父 Agent 全部高权限工具
- 审批策略取父子中更严格的一侧
- 租户隔离和用户隔离沿 `root_run_id` 继承

当前实现状态：

- 尚未落地
- 当前只存在单 Agent 工具 allowlist 校验，没有父子 Agent 权限继承治理

## 8. 结果合并策略

对于 `delegate_and_merge`，目标建议预先定义：

- `first_success`
- `all_results`
- `majority_vote`
- `supervisor_summary`

不建议把合并留给自由文本拼接。

当前实现状态：

- 尚未落地
- 当前没有多 Agent 结果回收与合并过程

## 9. 关键流程

目标流程：

1. Planner 产出 `handoff` 决策
2. Runtime 校验目标 Agent 是否在 `allowed_handoffs`
3. 创建子 Run 并固化上下文范围
4. 按 handoff 模式等待或继续
5. 子 Run 完成后按合并策略回收结果
6. 父 Run 再次进入规划或直接响应

当前实现状态：

- 当前尚未落地该流程
- 当前运行时不存在 `handoff` 决策类型，也不会校验 `allowed_handoffs`
- 当前 `AgentDefinitionVersion` 中虽然已有 `AllowedHandoffs` 字段，但尚未进入实际执行路径

## 10. 风险控制点

目标设计：

- 防止无限 handoff 链路
- 防止跨租户或跨用户上下文泄露
- 防止父 Agent 借子 Agent 绕过工具权限
- 合并策略必须可预测、可测试

当前实现状态：

- 这些风险控制点大多仍处于设计层
- 当前运行时尚无多 Agent / handoff，因此对应风险尚未进入主链路

## 11. 验收重点

目标验收：

- 父子 Run 关系可完整追踪
- 子 Agent 的工具和记忆边界按策略生效
- `delegate_and_wait` 与 `delegate_and_merge` 行为清晰可区分
- 多 Agent 结果整合可重放、可解释

当前实现状态：

- 当前该模块尚未进入可验收落地状态
- 后续真正实现时，可优先复用当前单 Agent Runtime 已验证的：
  - Run / Step 持久化模型
  - `WaitingTool` 恢复语义
  - `AllowedTools` allowlist 校验模式
