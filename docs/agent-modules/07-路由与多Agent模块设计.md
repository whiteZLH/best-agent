# 路由与多 Agent 模块设计

## 1. 模块定位

路由与多 Agent 模块负责“该交给谁做”和“多个 Agent 结果如何协同”。它不是具体执行层，而是围绕 handoff、上下文边界、权限继承和结果整合建立可控协作机制。

## 2. 设计目标

- 支持 Router Agent 和 Supervisor Agent 模式
- 支持可审计的 handoff
- 控制父子 Run 关系和权限边界
- 支持多种结果整合策略

## 3. 核心对象

### 3.1 RouteDecision

建议字段：

- `target_agent`
- `confidence`
- `reason`
- `context_overrides`
- `tool_overrides`
- `approval_required`

### 3.2 Handoff 模式

- `route_only`
- `delegate_and_wait`
- `delegate_and_merge`

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

## 5. 父子 Run 关系

需要明确记录：

- `parent_run_id`
- `root_run_id`
- `delegated_by_run_id`
- `delegated_by_agent`

父子关系必须能支持结果追溯、权限校验和聚合展示。

## 6. 上下文边界

建议显式定义：

- `context_scope`
- `memory_scope`
- `tool_scope`
- `knowledge_scope`

默认规则：

- 只传任务相关摘要，不透传整段历史
- 长期记忆默认只读
- 子 Agent 默认只见自身 allowlist 工具

## 7. 权限继承规则

- 子 Agent 不自动继承父 Agent 全部高权限工具
- 审批策略取父子中更严格的一侧
- 租户隔离和用户隔离沿 `root_run_id` 继承

## 8. 结果合并策略

对于 `delegate_and_merge`，建议预先定义：

- `first_success`
- `all_results`
- `majority_vote`
- `supervisor_summary`

不建议把合并留给自由文本拼接。

## 9. 关键流程

1. Planner 产出 `handoff` 决策
2. Runtime 校验目标 Agent 是否在 `allowed_handoffs`
3. 创建子 Run 并固化上下文范围
4. 按 handoff 模式等待或继续
5. 子 Run 完成后按合并策略回收结果
6. 父 Run 再次进入规划或直接响应

## 10. 风险控制点

- 防止无限 handoff 链路
- 防止跨租户或跨用户上下文泄露
- 防止父 Agent 借子 Agent 绕过工具权限
- 合并策略必须可预测、可测试

## 11. 验收重点

- 父子 Run 关系可完整追踪
- 子 Agent 的工具和记忆边界按策略生效
- `delegate_and_wait` 与 `delegate_and_merge` 行为清晰可区分
- 多 Agent 结果整合可重放、可解释
