# BestAgent MVP 实现进度

更新日期：2026-06-09

## 1. 当前状态

当前仓库已经从纯设计文档阶段推进到可运行的单体式 MVP 原型，代码现状以仓库实现为准，已完成：

- `.NET 8` 分层解决方案搭建
- `ASP.NET Core Controller` 风格 API
- `MediatR` 命令与查询处理
- `EF Core + PostgreSQL` 持久化接入
- `AgentDefinition` 管理与版本切换接口
- `AgentDefinition` 创建 / 版本创建当前也已支持 `KnowledgeSources`、`MemoryPolicy` 与 `ApprovalPolicy` 配置透传和查询回显
- `AgentDefinition` 创建 / 版本创建当前也已支持 `OutputSchema` 配置透传和查询回显，最终答复会按版本级 schema 做最小运行时校验
- `AgentDefinition` 创建 / 版本创建当前也已支持 `RoutingPolicy`、`ExecutionPolicy`、`PlannerPolicy`、`ContextPolicy` 与 `AllowedHandoffs` 配置透传和查询回显
- `ToolDefinition` 管理接口
- 基于 `Channel + BackgroundService` 的异步执行架构
- `AgentRun` Step 级事件流与 SSE 推送接口
- `POST /agent-runs` 支持请求体 `IdempotencyKey` 与 HTTP `Idempotency-Key`，同一幂等键重复调用返回同一 Run
- `POST /agent-runs` 当前也已开始支持最小 `options` 请求模型：`options.maxRounds` 可按请求级收紧当前 run 的有效 `MaxTurns`；`options.stream` 当前已开始支持最小 discoverability 语义，请求显式传入 `true` 时，创建响应会直接返回对应的 `streamUrl`，实际流式消费仍通过 `GET /agent-runs/{runId}/stream`
- `GET /agent-runs/{runId}` 当前也已开始返回最小 `streamUrl`，便于调用方基于 run snapshot 直接衔接到 SSE event stream
- `GET /agent-runs/{runId}/children` 与 `GET /agent-runs/{runId}/tree` 当前也已开始为子 Run / 树节点补充最小 `streamUrl`，便于多 Agent 链路直接衔接到对应 SSE event stream
- 工具 complete 与审批 complete 外部回调支持 HTTP `Idempotency-Key`，同 key 同 payload 可重放复用结果，不重复入队
- OpenAI 兼容模型网关抽象与实现；当前已开始支持 `OpenAI:TimeoutSeconds`、`OpenAI:Temperature`、`OpenAI:MaxOutputTokens`、`OpenAI:TopP`、`OpenAI:PresencePenalty`、`OpenAI:FrequencyPenalty`、`OpenAI:LogitBias`、`OpenAI:Seed`、`OpenAI:StopSequences`、`OpenAI:ParallelToolCalls`、`OpenAI:ReasoningEffort`、`OpenAI:Verbosity`、`OpenAI:ServiceTier`、`OpenAI:Store`、`OpenAI:LogProbs`、`OpenAI:TopLogProbs` 与 `OpenAI:ToolChoice` 全局默认生成参数配置，并允许 `GenerateTextRequest` 做请求级覆盖；其中 `MaxOutputTokens` 当前已映射到 OpenAI 兼容 `chat/completions` 的 `max_completion_tokens`；同时 `GenerateTextRequest` 也已开始支持最小 `OutputMode/OutputSchema`、`UserId`、`Metadata`、`ServiceTier`、`Store`、`LogProbs`、`TopLogProbs` 与 `ToolChoice`，OpenAI 兼容请求可下发 `response_format`、`user`、最小 `metadata`、`service_tier`、`store`、`logprobs`、`top_logprobs` 与 `tool_choice`，其中显式 `OutputMode=text` 当前也已开始下发 `response_format={type:\"text\"}`，`json_schema` 当前也已开始支持请求级 `OutputName` / `OutputDescription` / `OutputStrict`，显式传入空白 `OutputSchema`、`OutputName`、`OutputDescription` 当前都会直接拒绝，并开始对这组 `json_schema` 专属参数做冲突校验，避免在 `text/json_object` 或未启用 schema 模式时被静默忽略；同时 `top_logprobs` 当前也已开始要求 `logprobs=true`，避免冲突配置被静默吞掉；同时模型返回原生 `tool_calls` 时，声明工具的 `InputSchema` 当前也已开始用于前置校验 `arguments`；此外 `OpenAI:ToolChoice` 全局默认值当前只会在请求存在有效工具定义时生效，无工具请求不会再被该默认值误伤；请求级 `ReasoningEffort`、`Verbosity`、`ServiceTier` 与 `ToolChoice` 若仅传入空白字符串，当前也不会再吞掉配置级默认值
- 由于当前 Runtime 仍只稳定支持单个原生工具调用，`AgentRunLoop` 在存在工具定义时也已开始默认向 OpenAI 兼容请求显式下发 `parallel_tool_calls = false`，优先把模型约束在单工具调用模式；同时即使绕过 Runtime 直接调用 `OpenAiCompatibleModelGateway`，只要显式传入 `GenerateTextRequest.Tools` 且未手动覆盖，当前也会自动补上 `parallel_tool_calls = false`
- OpenAI 兼容模型网关当前也已开始把 OpenAI 兼容响应中的 `finish_reason` 规范化为平台侧最小枚举（如 `completed` / `tool_call` / `max_output_tokens` / `content_filtered`），其中 `content_filter` / `content_filtered` 当前都会统一归一到平台侧 `content_filtered`，并把响应 `id` 与实际 `service_tier` 一并写入 `GenerateTextResult` 与 `model_call` 审计 payload
- `GenerateTextRequest` 当前也已开始支持最小 `Tools` 输入模型；`AgentRunLoop` 会按版本级 `AllowedTools` 解析 enabled `ToolDefinition`，并向 OpenAI 兼容请求下发最小 `tools.function.parameters`，同时当前也会对工具定义显式补上 `function.strict`；请求侧若显式传入 `Tools`，当前要求至少存在一个具名工具定义，空列表会直接拒绝，具名工具定义的名字也要求大小写不敏感唯一；`tool_choice=auto|required|具体工具名` 也要求至少存在有效声明工具
- `GenerateTextRequest` 当前也已开始支持最小 `Messages` 输入模型；显式传入消息列表时，OpenAI 兼容请求会优先按多消息形式下发，当前支持 `developer/system/user/assistant/tool` 角色，单条消息也已开始支持最小 `name` / `tool_call_id` 字段，其中 `tool` 角色消息要求显式携带 `tool_call_id`；同时消息 `content` 当前也已开始支持字符串与最小结构化 content parts（`text` / `image_url` / `input_audio` / `file`），`assistant` 角色消息也已开始支持显式 `tool_calls` 历史载荷；对显式 `Messages` 中的空列表或无效项，网关当前都会直接拒绝，不再静默丢弃坏消息或回退到 `system prompt + input`
- OpenAI 兼容模型网关当前也已开始兼容响应中 `message.content` 的字符串、数组文本片段与最小对象文本形态，不再只依赖单个字符串 `content`
- OpenAI 兼容模型网关当前也已开始支持消费原生单个 `tool_calls` 响应，并归一回现有 `{"action":"tool_call",...}` JSON 决策输出
- `GenerateTextRequest` 当前也已开始支持最小 `ToolChoice`；存在工具定义时，`AgentRunLoop` 会默认向 OpenAI 兼容请求下发 `tool_choice = auto`，同时直接调用 `OpenAiCompatibleModelGateway` 且显式传入 `GenerateTextRequest.Tools` 时，若未手动覆盖 `ToolChoice`，当前也会自动补上 `tool_choice = auto`
- OpenAI 兼容模型网关当前也已开始把响应中的最小 `reasoning_summary/reasoning` 归一到 `GenerateTextResult` 与 `model_call` 审计 payload
- OpenAI 兼容模型网关当前也已开始把响应中的原生 `tool_calls` 列表显式带入 `GenerateTextResult` 与 `model_call` 审计 payload；同时继续为现有 Runtime 兼容保留“单个 function tool call -> `{"action":"tool_call",...}`”的归一路径
- OpenAI 兼容模型网关当前也已开始对原生 `tool_calls` 做最小结构校验：返回的工具名必须命中当前声明工具列表，`arguments` 也必须是可解析的 JSON 对象
- `ToolDefinition` 驱动优先的工具执行链路（DB-first，支持 webhook、本地 handler 与 `inline_result` 固定结果执行）
- 已补上独立 `ToolResolver`，将工具绑定解析从 `ToolExecutor` 中拆出
- 工具运行时输入 schema 校验 MVP：执行前按 `ToolDefinition.InputSchema` 校验 `type`、`required`、`properties`、`enum` 以及 `additionalProperties` 的布尔/对象形态
- 工具运行时输出 schema 校验 MVP：同步完成结果会按 `ToolDefinition.OutputSchema` 校验 `type`、`required`、`properties`、`enum` 以及 `additionalProperties` 的布尔/对象形态，pending 结果暂跳过即时校验
- 工具 schema 校验能力已扩展常用约束：`items`、`prefixItems`、legacy `items: []` 元组写法、`additionalItems`、`minItems`、`maxItems`、`contains`、`minContains`、`maxContains`、`uniqueItems`、`minLength`、`maxLength`、`minimum`、`maximum`、`exclusiveMinimum`、`exclusiveMaximum`、`multipleOf`、`minProperties`、`maxProperties`、`propertyNames`、`patternProperties`、`dependentRequired`、`dependentSchemas`、`pattern`、`const`、`allOf`、`anyOf`、`oneOf`、`not`、`if/then/else` 与最小 `format`；当前 `format` 已覆盖 `email`、`uri`、`uri-reference`、`date-time`、`date`、`uuid`、`ipv4`、`ipv6` 与 `hostname`
- 工具参数级安全策略当前也已开始进入主链路：`ParameterPolicy` / 结构化 `Policies.Parameter` 可声明 `allowedPaths` / `deniedPaths`，运行时会在 schema 校验通过后继续按输入路径级规则拦截未放行或显式禁用的参数
- HTTP webhook 工具最小 `RetryPolicy` 执行：支持结构化 `{"maxAttempts":N,"delayMs":N}` 与 legacy `retry-once` / `retry-twice`，覆盖超时、网络异常、`408`、`429` 和 `5xx`
- HTTP webhook 工具返回协议兼容增强：除 legacy `output/isPending/waitToken` 外，也支持最小标准结果信封 `status/data/error/meta`
- HTTP webhook 工具最小 `IdempotencyPolicy` 执行：支持 `idempotent` / `non-idempotent` / `disabled` 与最小 JSON 对象策略，并在启用时透传 `Idempotency-Key`
- `ToolDefinition` 写入阶段已开始规范化策略字段：`RetryPolicy`、`AuthPolicy`、`CompensationPolicy` 可兼容 legacy 字符串输入并归一化为结构化 JSON，`ConsistencyMode` 已收紧为 `none` / `eventual` / `strong` 有限枚举，`SideEffectLevel` 已收紧为 `read_only` / `internal_write` / `external_write` / `destructive` 有限枚举，二者统一按小写持久化；`destructive` 工具当前要求显式声明 `CompensationPolicy`；同时 `AuthPolicy.scheme` 当前也已开始与执行定义做最小一致性校验：`local_handler` / `inline_result` 仅允许 `none`，`webhook` 下的 `bearer` / `oauth` 则要求实际认证头包含 `Authorization: Bearer ...`
- 启动初始化当前也已开始对旧存量工具定义统一做策略字段归一：`RetryPolicy` / `AuthPolicy` / `IdempotencyPolicy` / `CompensationPolicy` 会按与命令写入相同的规则规范化，`ConsistencyMode` / `SideEffectLevel` 也会在启动期收敛到规范小写枚举；若旧存量 `AuthPolicy` 与实际执行认证头已明显漂移，启动期会直接阻止继续带病运行
- `ToolDefinition` 查询返回当前已开始补充结构化 `Execution` 读侧模型；兼容 flat 字段当前也已按执行类型真实返回，非 webhook 工具不再暴露假的 `HttpMethod` / `AuthHeaders`，同时显式返回当前 webhook / local handler / `inline_result` 的规范化执行定义
- `ToolDefinition` 结构化 `Execution` 读写契约当前也已开始显式暴露最小 `version` 字段：查询返回会回显当前 binding 文档版本，结构化写入也会校验 `Execution.version` 必须与当前支持版本一致
- `ToolDefinition` 查询返回当前也已开始收敛兼容 flat `ExecutionBinding` 的敏感载荷脱敏边界：除 webhook 认证头外，`inline_result` binding 中的运行时敏感字段也会与结构化 `Execution.InlineResult` 视图保持一致地脱敏
- `ToolDefinition` 查询返回当前也已开始补充结构化 `Policies` 读侧模型；在继续保留 `RetryPolicy` / `AuthPolicy` / `IdempotencyPolicy` / `CompensationPolicy` 兼容字符串字段的同时，显式返回规范化后的策略视图
- `ToolDefinition` 查询 / 写入当前也已开始补充最小参数级策略视图：可通过 `ParameterPolicy` 或结构化 `Policies.Parameter` 持久化 `allowedPaths` / `deniedPaths`
- `ToolDefinition` 查询返回当前也已开始把兼容 flat 策略字段收敛到 canonical 形态：legacy `retry-once` / `disabled` / `bearer` / `manual` 等旧值在读侧会按规范化结果返回，而不再原样外泄
- `ToolDefinition` 查询返回当前也已开始对结构化 `AuthPolicy` 内的敏感字段做最小递归脱敏；`scheme` 等非敏感策略信息仍保留，`token` / `apiKey` / `secret` 等字段不再原样透出
- 对仍未完成启动期归一的 legacy webhook 记录，`ToolDefinition` 查询读侧当前也会按 flat 字段即时合成结构化 `Execution` 视图，并继续对认证头等敏感字段做脱敏
- `ToolDefinition` 创建/更新请求当前也已开始支持结构化 `Execution` 写侧模型；外层 API 可直接提交 webhook / local handler / `inline_result` 执行定义，再由 controller 归一化到既有命令链路，旧的平铺字段输入继续兼容
- `ToolDefinition` 创建/更新请求当前也已开始支持结构化 `Policies` 写侧模型；外层 API 可直接提交 `Retry` / `Auth` / `Idempotency` / `Compensation` 结构化策略，再由 controller 归一化到既有命令链路，旧的策略字符串输入继续兼容
- `ToolDefinition` API 当前也已开始收紧“结构化 `Execution` + 旧平铺字段并存”的兼容边界：两者并存时必须语义一致；对非 webhook 的结构化执行定义仍显式拒绝 legacy webhook flat 字段
- `ToolDefinition` API 当前也已开始收紧“结构化 `Policies` + 旧策略字符串字段并存”的兼容边界：同一策略字段并存时必须语义一致，但未提供结构化值的其他策略字段仍可继续沿用旧 flat 写法
- Runtime 侧最小前置去重：同一 run 内启用 `IdempotencyPolicy` 的同工具、同输入，若已有已完成结果可直接复用；若已有仍在 pending 的异步调用，也会直接复用原 `waitToken` / `invocationId`，避免重复执行
- 运行时工具失败回写已开始结构化：普通运行、工具专用 complete 回调恢复以及审批放行后的工具执行失败，除执行期直接抛错外，也已支持消费工具返回的标准 `failed/error` 结果对象，并统一优先落为 `tool_call Failed` 步骤、补记失败 `ToolInvocation` 审计；错误载荷当前已开始带上 `toolName` / `stage` / `message`，若工具声明了 `CompensationPolicy` 也会同步带出最小 `compensation.mode` 审计信息
- `GetAgentRunSteps` / `GET /agent-runs/{runId}/steps` 当前也已开始把结构化失败载荷补成 typed 读侧：`model_failure` 可返回 `ModelFailure` 视图，`tool_error` 可返回 `ToolFailure` 视图，外层无需再自行解析原始 `Error` JSON
- `CompensationPolicy={"mode":"manual"}` 当前已开始具备最小可执行闭环：工具执行失败、工具返回 failed/error，或异步恢复 / 人工替代结果命中输出 schema 校验失败时，运行时会自动复用既有 `WaitingHuman` 流程创建 `human_wait` step，保留失败来源工具上下文并等待人工补偿
- 运行时工具 payload 最小脱敏：`GetAgentRunSteps`、`GetAgentRunApprovals`、`GetAgentRunEvents`、SSE `stream` 与 `session_memory` 写回会对常见敏感字段做递归 JSON 脱敏
- 异步工具恢复与人工替代工具结果路径已补上最小输出 schema 校验，不再只有同步工具结果会校验 `OutputSchema`
- 多 Agent / handoff 当前已开始进入主链路：模型可返回 `handoff` 决策，Runtime 会按版本级 `AllowedHandoffs` 校验目标 Agent，创建父子 Run 关系并支持最小 `route_only` / `delegate_and_wait` / `delegate_and_merge` 闭环；子 Run 完成后父 Run 当前可按模式直接完成或恢复继续生成最终答复，其中 `delegate_and_merge` 已开始支持单子 Run 的显式 `merge_strategy`（默认 `supervisor_summary`，并支持 `first_success` / `all_results`）；相关 `ParentRunId` / `RootRunId` / `DelegatedBy*` 字段已进入 Run 查询读侧，`GetAgentRunSteps` 当前也已支持 typed `Handoff` 审计视图，`GET /agent-runs/{runId}/children` 与 `GET /agent-runs/{runId}/tree` 已开始提供最小父子链路聚合视图；handoff 链当前也已按版本级 `ExecutionPolicy.maxHandoffDepth`（默认 `3`）进入最小深度治理；同时 `RouteRule.context_scope` 与 handoff `context_overrides` 当前也已开始最小影响子 Run 输入，支持 `summary_only` 模式把子 Agent 输入收敛为“委派任务摘要 + 父请求摘要”，并进一步关闭子 Agent 模型调用前的 `summary/session/user/knowledge` 注入；`summary_only` 当前也会同步关闭子 Agent 的 `toolResultMemoryEnabled`、`userMemoryWriteEnabled` 与 `summaryMemoryWriteEnabled`，避免子 Run 在摘要上下文下继续写回长期记忆；`RouteRule.memory_scope` 与 handoff `memory_overrides` 当前也已开始进入最小长期记忆边界治理，支持 `{"mode":"read_only"}` 只关闭子 Agent 的长期记忆写侧、以及 `{"mode":"disabled"}` 同时关闭长期记忆/检索读写侧；`tool_scope/tool_overrides` 也已开始以 `allowed` 列表最小收紧子 Agent 的 `AllowedTools`，`knowledge_scope/knowledge_overrides` 当前也已开始以 `allowed` 或 `sources` 列表最小收紧子 Agent 的 `KnowledgeSources`，并在二者同时存在时优先使用 handoff 显式给出的 `knowledge_overrides`；子 Agent 的 `AllowedHandoffs` 也已开始继承父链路收紧后的有效边界，避免继续向父未放行目标扩权委派
- `RouteRule` 当前已从纯设计态进入最小定义管理态，并开始进入最小 Runtime 自动路由：`route_rule` 已接入 EF / Repository，并开始提供按 `agentCode + version` 创建与列表查询接口（`GET/POST /agent-definitions/{agentCode}/versions/{version}/route-rules`）；handoff Runtime 当前除会在模型未显式给出 `mode` 时回退使用命中规则中的 `handoff_mode`、在 `delegate_and_merge` 下继续回退消费规则级 `merge_strategy`，并把 `routeRuleId/contextScope/toolScope/knowledgeScope/approvalRequired` 写入 handoff 审计 payload 外，也已开始支持最小自动路由切片：当版本级 `RoutingPolicy.strategy == "handoff-first"` 且存在匹配当前输入的启用 `RouteRule` 时，Runtime 会在首轮模型调用前直接进入 handoff；当前最小匹配能力支持 `match_type = intent|keyword|regex`，其中 `intent` / `keyword` 会对 `match_expression` 中的 `intent` / `keyword` / `contains` / `any` / `all` / `keywords` / `terms` 做大小写不敏感包含匹配，`regex` 会消费 `pattern` / `regex` / `expression` 正则表达式做大小写不敏感匹配；同时模型 handoff 决策也已开始支持最小 route metadata（`reason/confidence/context_overrides/memory_overrides/tool_overrides/knowledge_overrides/approval_required`）并进入 handoff 审计与 `GetAgentRunSteps` 读侧；`approval_required` 当前也已不再只是审计字段，handoff 命中后可真正进入 `WaitingApproval`，并支持 approve / reject / timeout / request-human 收尾；但 Runtime 目前仍未形成更完整的 Router Agent / Supervisor Agent 与复杂自动路由策略
- 规划层结构化降级出口当前也已开始进入主链路：模型可直接返回 `request_human` 进入现有 `WaitingHuman` / `human_wait` 闭环，也可返回 `fail` 生成结构化 `model_failure` 审计载荷并进入失败终态
- 规划层当前也已开始支持最小显式检索/审批决策：模型可返回 `retrieve`，Runtime 会记录带结构化 `DecisionPayload` 的 `retrieval` step、构造显式 retrieval follow-up，并在下一轮模型调用前复用现有 `RuntimeContextComposer` 执行真实知识检索；`GetAgentRunSteps`、`GetAgentRunEvents` 与 SSE 当前也已开始为该显式 retrieval step 返回 typed `Retrieval` 视图；模型也可返回 `request_approval` 复用现有 `WaitingApproval` / approve / reject 闭环，审批通过后会把审批结果喂回下一轮规划继续执行
- 最小审批流闭环（`WaitingApproval` + `approve/reject`，当前已同时覆盖高风险 `tool_call` 与 `approval_required handoff`）
- 最小审批超时闭环（`expires_at` + 后台扫描；默认拒绝进入 `TimedOut`，也支持按配置转入 `WaitingHuman`）
- 最小人工接管闭环（`WaitingHuman` + `request-human` / `complete-human`）
- 人工接管最小身份闸门：`request-human` / `complete-human` 当前要求显式人工操作者身份
- 审批专用持久化与 run 级审批查询接口
- 可配置审批策略 MVP（审批触发风险级别、需角色风险级别、允许审批角色、最小参数模式触发审批；默认包含 `destructive` 审批）
- 审批策略配置已开始显式规范化：`ApprovalRequiredSideEffectLevels`、`RoleRequiredSideEffectLevels` 与 `ParameterApprovalRules.OverrideSideEffectLevel` 统一收紧为合法 side effect level 枚举，并在应用注册阶段 fail-fast 拒绝无效配置
- `AgentDefinitionVersion.ApprovalPolicy` 当前已开始进入主链路：版本创建接口可持久化最小审批策略 JSON，Runtime 会在保留全局默认策略兜底的同时，优先按 run 绑定版本上的 `ApprovalPolicy` 覆盖审批触发等级、审批角色要求与参数模式规则；在 handoff 子链路中，子 Agent 的有效审批策略当前也已开始按父子更严格的一侧收紧，避免继续通过更宽松的子版本策略绕过父审批边界
- `AgentDefinitionVersion.KnowledgeSources` 与 `MemoryPolicy` 当前也已开始进入主链路：定义创建与版本创建接口可持久化知识源列表和最小记忆策略 JSON，Runtime 上下文装配与检索范围会按 run 绑定版本持续生效
- `AgentDefinitionVersion.ContextPolicy` 当前也已开始最小进入主链路：`ContextPolicy.citations` 可同时控制检索知识注入时是否附带 citation/source 元数据，以及最终答复是否自动追加基于检索上下文提取的 `References`
- `AgentRun.MaxCost` / `AgentDefinitionVersion.MaxCost` 当前也已开始最小进入主链路：OpenAI 兼容模型网关会读取 `usage` 并按配置的 `OpenAI:PromptTokenPricePerMillion` / `CompletionTokenPricePerMillion` 计算最小模型成本，Runtime 会累计到 `AgentRun.TotalCost`，并在后续模型调用前或单次模型调用后对 `max_cost` 做最小超限拦截
- 模型调用审计当前也已开始补上最小 usage 读侧：`model_call` step 会把 `model`、`promptTokens`、`completionTokens`、`totalTokens` 与 `cost` 写入 `DecisionPayload`，`GetAgentRunSteps` / `GET /agent-runs/{runId}/steps` 现可返回 typed `ModelCall` 视图；若命中检索，当前也会补上最小 retrieval query / candidateCount / selectedSources / citations 审计
- run outbox 事件读侧当前也已开始补上最小 typed payload 视图：`GetAgentRunEvents` / `GET /agent-runs/{runId}/events` 除保留脱敏后的原始 `Payload` 外，也会返回统一 `Data` 结构，并可进一步把事件中的 `model_call` / `retrieval` / `model_failure` / `tool_error` 解析为 typed 读侧；SSE `stream` 当前也已开始同步回显最小 typed `ModelCall` 与显式 `Retrieval`
- `GetAgentRunEvents` 与 SSE `stream` 当前也已开始把事件中的 `Approval` / `Handoff` / `HumanWait` 决策载荷补成 typed 读侧，并对其中的工具输入 / 输出继续沿用最小递归脱敏
- `GetAgentRunEvents` 与 SSE `stream` 当前也已开始把异步 `waiting` 事件中的 `ToolInvocation` 恢复信息补成 typed 读侧，显式回显 `invocationId/toolName/mode/status/callbackToken`
- `GetAgentRunById` / `GET /agent-runs/{runId}` 当前也已开始补充最小等待定位字段：除 `WaitToken`、`CurrentStepNo`、`InterruptReason` 外，还会返回 `CurrentStepId`、`WaitStepType`，并在适用时补上 `CurrentInvocationId` / `CurrentApprovalId`
- `GetAgentRunById` / `GET /agent-runs/{runId}` 当前也已开始补充当前等待上下文的 typed 读侧：在适用时可直接返回 `CurrentToolInvocation`、`CurrentApproval`、`CurrentHumanWait` 与 `CurrentHandoff`
- `GetAgentRunChildren` / `GetAgentRunTree` 当前也已开始与单 Run 快照对齐：子 Run 列表与递归树节点在等待态下同样会返回 `CurrentStepId/WaitStepType`、`CurrentInvocationId/CurrentApprovalId` 与 typed 等待上下文
- `GetAgentRunSteps` 的 typed `Approval` 读侧当前也已开始同时返回通用 `RequestedAction/RequestPayload` 与兼容 `ToolName/ToolInput` 别名，以覆盖 planner `request_approval` 与工具审批两条链路
- `GET /agent-runs/{runId}/stream` 当前也已开始从轻量 `eventType + data` 收敛到最小统一事件 envelope：SSE `data` 会包含 `eventId/runId/seqNo/eventType/runStatus/occurredAt/data`，并在存在 `seqNo` 时写出 SSE `id`
- `GET /agent-runs/{runId}/stream` 当前也已开始最小支持 `Last-Event-ID` 断线续传：建连时会先注册进程内缓冲订阅，再按 `afterSeqNo = Last-Event-ID` 回放 outbox 事件，最后按 `seqNo` 去重切换到实时 SSE；若回放已包含终态事件，则流会直接结束
- `AgentDefinitionVersion.OutputSchema` 当前也已开始进入主链路：定义创建与版本创建接口可持久化最小 JSON Schema 对象，Worker 会在 run 完成前对最终答复执行最小结构化校验
- step 级 `approve/reject` 与外部 `approval complete` 当前也已和版本策略对齐：审批授权会优先按 run 绑定版本上的 `ApprovalPolicy` 做角色校验，不再只依赖全局默认审批角色配置
- Run 取消接口（`cancel`）与取消事件落库 / SSE 推送
- run 级 outbox 事件落库、回放 API 与独立投递器 MVP
- 审批超时后台处理器（`ApprovalTimeoutDispatcher`）与默认拒绝事件发布
- HTTP outbox publisher 与 `RunOutboxEventDispatcher` 当前也已补上最小可观测性：会分别记录 outbox publish / dispatch metrics，并用统一 `ActivitySource` 输出 `eventId/runId/eventType/retry/status` 维度 tracing span
- 外部 complete 回调增强来源认证（HMAC 签名校验，支持 per-tool callback secret / approval secret 轮换）
- 跨 `WaitingTool` / `WaitingApproval` 恢复场景的 run 级累计 `MaxTurns` 控制
- `Knowledge Store` / `Memory Store` 最小持久化接入（`knowledge_document`、`knowledge_chunk`、`embedding_index`、`session_memory`、`user_memory`、`summary_memory`）
- 运行时最小上下文装配器：模型调用前可注入 `summary_memory`、`session_memory`、`user_memory` 与 `knowledge_chunk`，并开始由统一 `MemoryPolicy` 显式控制各类上下文读取开关
- `CreateAgentRun` 已支持显式携带 `tenantId` / `userId` / `sessionId` 并写入 `AgentRun`
- `AgentRunsController` 当前也已开始最小消费 `tenant/user/session` scope：创建 Run 时会优先继承已认证身份字段，并兼容 `X-BestAgent-Tenant-Id` / `X-BestAgent-User-Id` / `X-BestAgent-Session-Id` 显式 scope headers；Run 查询、恢复、取消、审批、人机协同、外部 tool/approval complete 回调与 SSE stream 入口在存在上述 scope 时也会校验当前 Run 是否仍处于相同 tenant/user/session 边界内
- 已补上最小记忆写回闭环：可信工具完成结果可按 `MemoryPolicy` 最小 allowlist 门禁写入 `session_memory`，run 完成时可写入模板化 `summary_memory`
- `MemoryPolicy` 写侧已细化为独立开关：`session_memory` 工具结果写入、结构化 `user_memory` 写入与 `summary_memory` 写入可分别控制
- 检索链路已从固定顺序注入升级到最小 lexical retrieval：支持 query 归一化、候选召回、词法重排、prompt citation 注入与最终答复 `References` 追加；`model_call` 审计当前会回显最小 retrieval query / sources / citations 结构化信息，显式 `retrieval` step 本身也已开始在 `GetAgentRunSteps`、`GetAgentRunEvents` 与 SSE 中回显 typed retrieval query 视图
- `user_memory` 已开始支持最小可信写入：仅消费工具结果中显式声明的结构化 memory 条目，并按 `memory_key` 覆写更新
- 基础单元测试、控制器测试与部分集成 / 基础设施测试

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
- `POST /agent-runs/{runId}:cancel`
- `POST /agent-runs/{runId}:request-human`
- `POST /agent-runs/{runId}/tool-invocations/{invocationId}:complete`
- `POST /agent-runs/{runId}/approvals/{approvalId}:complete`
- `POST /agent-runs/{runId}/steps/{stepId}:complete-human`
- `POST /agent-runs/{runId}/steps/{stepId}:approve`
- `POST /agent-runs/{runId}/steps/{stepId}:reject`
- `GET /agent-runs/{runId}`
- `GET /agent-runs/{runId}/steps`
- `GET /agent-runs/{runId}/approvals`
- `GET /agent-runs/{runId}/events?afterSeqNo=...`
- `GET /agent-runs/{runId}/stream`

`AgentDefinition` 接口：

- `GET /agent-definitions`
- `GET /agent-definitions/{agentCode}`
- `POST /agent-definitions`
- `GET /agent-definitions/{agentCode}/versions`
- `POST /agent-definitions/{agentCode}/versions`
- `GET /agent-definitions/{agentCode}/versions/{version}/route-rules`
- `POST /agent-definitions/{agentCode}/versions/{version}/route-rules`
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
- 异常映射：`NotFoundException` → 404、`ForbiddenException` → 403、`ConflictException` → 409、`InvalidOperationException` → 422、其他 → 500

### 3.2 应用层

当前已实现的命令与查询：

- `CreateAgentRunCommand`
- `ResumeAgentRunCommand`
- `CancelAgentRunCommand`
- `RequestHumanAgentRunCommand`
- `CompleteHumanAgentRunCommand`
- `CompleteToolInvocationCommand`
- `CompleteApprovalCommand`
- `ApproveAgentRunStepCommand`
- `RejectAgentRunStepCommand`
- `GetAgentRunByIdQuery`
- `GetAgentRunStepsQuery`
- `GetAgentRunApprovalsQuery`
- `GetAgentRunEventsQuery`
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
2. 解析请求级运行选项；当前最小支持 `options.maxRounds`，会按“只收紧、不放大”的规则覆盖当前 run 的有效 `MaxTurns`
3. 创建 `AgentRun`
4. 写入 `created`、`running` 步骤
5. 将 `CreateAgentRunMessage` 入队到 `AgentRunChannel`
6. 由后台 `AgentRunWorker` 消费消息并委托 `AgentRunLoop.ExecuteAsync` 进入循环（单次 loop 调用内最多 `MaxTurns` 轮）：
   - 通过 `IModelGateway` 调用模型
   - 写入 `model_call` 步骤
   - 若模型返回 `respond`，返回 `AgentLoopCompleted`
   - 若模型返回 `tool_call`，先校验权限，再按可配置审批策略与 `ToolDefinition.SideEffectLevel` 判断是否需要审批
   - 若命中高风险工具，写入带审批 payload 的 Pending `tool_call` 步骤，并返回 `AgentLoopWaitingApproval`
   - 同时由 Worker 在 `ApplyLoopResult` 中落一条独立 `AgentApproval`
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
3. 通过 `IApprovalAuthorizer` 校验审批人身份与审批角色
4. 将 Run 状态先切回 `Running`
5. 解析审批人信息：优先使用服务端认证上下文（`HttpContext.User`），无认证用户时兼容回退到请求体字段
6. 将 `ApproveAgentRunStepMessage` 入队到 `AgentRunChannel`
7. 由后台 `AgentRunWorker` 真正执行工具、更新审批 payload 为 `Approved`、更新独立 `AgentApproval` 审计字段并继续循环

`RejectAgentRunStepCommandHandler` 审批拒绝链路：

1. 加载 `AgentRun`，验证 `Status == WaitingApproval`
2. 校验最后一个 Pending step 与 `stepId` 匹配，且审批 payload `Decision == Pending`
3. 通过 `IApprovalAuthorizer` 校验审批人身份与审批角色
4. 将 Run 状态先切回 `Running`
5. 解析审批人信息：优先使用服务端认证上下文（`HttpContext.User`），无认证用户时兼容回退到请求体字段
6. 将 `RejectAgentRunStepMessage` 入队到 `AgentRunChannel`
7. 由后台 `AgentRunWorker` 更新审批 payload 为 `Rejected`、更新独立 `AgentApproval` 审计字段、将 Pending step 标记为失败并终止 Run

当前架构特点：

- 无 Service 层，Handler 直接编排基础设施接口
- 循环逻辑提取为 `AgentRunLoop` 静态方法类，Create 和 Resume 共享
- `StepDecision` 为模型输出的结构化解析结果，当前支持 `respond`、`tool_call` 与最小 `handoff` 三种动作
- 支持多轮 tool call 循环（跨 create / resume / approve 入口按 run 生命周期累计 `model_call` 步骤控制 `MaxTurns`；`POST /agent-runs options.maxRounds` 当前可按请求级进一步收紧当前 run 的轮次预算）
- 支持异步工具：工具返回 `IsPending=true` 时 Run 挂起，通过 resume 接口恢复
- 支持高风险工具审批：命中 `ToolDefinition.SideEffectLevel` 写操作等级时，Run 会切换到 `WaitingApproval`
- 审批支持 approve / reject 两条恢复路径：approve 会继续执行工具并恢复 loop，reject 会终止 Run
- 审批信息同时保留在 `AgentStep.DecisionPayload`（兼容/运行时快照）与独立 `AgentApproval` 持久化记录中
- 人工接管信息保留在 `human_wait` step 的 `DecisionPayload` 中，记录人工处理人、角色、评论、处理时间与人工结果
- `GetAgentRunSteps` 现优先从 `AgentApproval` 投影审批信息，旧数据仍可 fallback 到 `DecisionPayload`
- `GetAgentRunSteps` 现已支持解析 `HumanWait` 与 `Handoff` 视图；`GetAgentRunById` 当前会返回 `WaitToken`，并补充 `CurrentStepNo`、`InterruptReason` 以及最小父子 Run 关系字段，便于前端按 `run snapshot + event stream` 重建等待/失败态
- `StatusVersion` 作为 EF Core 并发令牌，防止并发 resume / approval 冲突
- `AgentRunEventBus` 按 run 分发事件，SSE endpoint 订阅对应事件流
- `AgentRunWorker` 重新进入 `AgentRunLoop` 前会统计当前 run 已完成的 `model_call` 步骤数，因此 `MaxTurns` 已按 run 生命周期累计控制
- `AgentRunWorker` 当前也已优先按 `AgentRun.AgentDefinitionVersionId` 重新解析绑定版本；若 run 绑定版本仍存在，恢复、审批放行、人工替代工具结果继续执行与完成摘要写回都会继续使用该版本，而不受后续激活新版本影响

当前工具执行语义：

- `AgentRunLoop` 统一通过 `IToolExecutor` 执行工具
- `ToolExecutor` 现已调整为 **DB-first**：先查 `ToolDefinition`
- 如果 `ToolDefinition.Enabled == false`，直接拒绝执行
- 若定义存在显式 `ExecutionKind + ExecutionBinding`，运行时会按 persisted binding 解析执行后端
- persisted `ExecutionBinding` 当前已开始升级为带 `version + type + payload` 的版本化执行定义文档；现有 webhook / local handler / `inline_result` 绑定都会按新文档形状持久化
- 对旧的仅含 webhook flat 字段（`EndpointUrl` / `HttpMethod` / `AuthHeaders`）定义，当前会在启动初始化阶段自动回填 persisted webhook binding，再由运行时走统一主路径解析
- 启动初始化当前还会继续按 persisted binding 反向修复存量 legacy flat 字段：webhook 记录会对齐 `EndpointUrl` / `HttpMethod` / `AuthHeaders`，非 webhook 记录会清空残留的 legacy webhook flat 字段，尽量把存量数据收敛到 binding 派生真值
- 对新的显式 binding 写入请求，当前已开始收紧 legacy webhook flat 字段兼容：`webhook` 绑定下若同时提供 `EndpointUrl` / `HttpMethod` / `AuthHeaders`，其值必须与 binding 内载荷一致；`local_handler` / `inline_result` 绑定下则要求这些 legacy webhook 字段显式省略
- 对旧的扁平 `ExecutionBinding` JSON 形状，当前解析器仍保留最小兼容读取；若未来 `version` 超出当前支持范围，则会显式拒绝执行
- 若定义既无 persisted binding，启动期也无法从 legacy webhook flat 字段回填，则运行时会直接返回工具定义不完整错误
- 如果数据库中不存在 `ToolDefinition`，当前实现已经不再允许继续执行本地 handler；所有可执行工具都必须先有 persisted 定义
- 从主流 Agent 实现视角看，这一方案属于**正确的 MVP 过渡形态**：工具定义与工具执行已分层、执行权仍由宿主应用掌握、数据库定义开始成为主要事实来源
- 当前仍不是最终平台化形态：`ToolDefinition` 仍偏向 HTTP webhook 配置模型，虽然已补上 persisted `execution kind / binding` 与内部 `ToolResolver`，并已要求所有可执行工具必须先有 persisted 定义，但 webhook flat 字段兼容与更通用的执行定义模型仍在演进中

当前实现约束：

- 已落地最小审批流，并补齐了独立审批持久化与 run 级审批查询
- 未实现完整长期记忆写入策略、检索排序流水线和多 Agent 编排；当前已补上 `session_memory` / `user_memory` / `summary_memory` 写侧独立开关，但更复杂的分级写入治理仍未落地
- 当前运行时已经从 `ToolExecutor` 中拆出 `ToolResolver`，并引入 persisted `ExecutionKind + ExecutionBinding` 作为主路径；legacy webhook flat 字段兼容当前主要收敛到启动初始化阶段的自动回填 / 修复，以及查询读侧的最小展示兼容，而不再在运行时解析分支内直接 fallback
- `WaitingTool` / `WaitingApproval` 当前都依赖单步骤挂起语义，尚未演进为更通用的系统级挂起模型
- 审批身份来源已支持从认证上下文解析，并已通过 `DefaultApprovalAuthorizer` + `ApprovalPolicyOptions` 落地可配置审批策略 MVP；当前已支持最小参数模式审批规则，但仓库尚未正式接入完整认证鉴权中间件、租户权限策略与租户级审批策略解析

### 3.3 领域模型

当前核心持久化实体为：

- `AgentDefinition`
- `AgentDefinitionVersion`
- `AgentRun`
- `AgentStep`
- `AgentApproval`
- `ToolDefinition`
- `KnowledgeDocument`
- `KnowledgeChunk`
- `EmbeddingIndex`
- `SessionMemory`
- `UserMemory`
- `SummaryMemory`

当前审批模型状态：

- `AgentApproval` 已接入主路径，用于独立存储审批请求、审批人、审批结论与审计时间
- `AgentStep.DecisionPayload` 仍保留，用于运行时兼容和旧数据 fallback

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

- 审计字段仍主要由应用层手工赋值
- 默认操作者仍可回退为 `system`
- 审批操作在存在认证上下文时，会优先记录认证用户身份到 `AgentApproval`
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
- `AgentApprovals`
- `RunOutboxEvents`
- `ToolDefinitions`
- `ToolInvocations`
- `KnowledgeDocuments`
- `KnowledgeChunks`
- `EmbeddingIndexes`
- `SessionMemories`
- `UserMemories`
- `SummaryMemories`

当前持久化特点：

- 使用 `Npgsql` 连接 PostgreSQL
- 通过 `ApplyConfigurationsFromAssembly` 应用实体配置
- 已新增 `AgentApprovalConfiguration` 与 `AgentApprovalRepository`
- `AgentApproval` 已映射到 `agent_approval` 表，支持按 `run_id` / `step_id` 查询
- 已新增 `RunOutboxEventConfiguration` 与 `RunOutboxEventRepository`
- `RunOutboxEvent` 已映射到 `run_outbox_event` 表，支持按 run 回放、pending 查询、序号分配以及发布状态更新
- 已新增 `KnowledgeDocument/KnowledgeChunk/EmbeddingIndex/SessionMemory/UserMemory/SummaryMemory` 的 EF 配置与 Repository
- 当前运行时已可按 `AgentDefinitionVersion.KnowledgeSources` 查询 `knowledge_chunk`，并按统一 `MemoryPolicy` 读取 `summary_memory`、`session_memory`、`user_memory` 与 `knowledge_chunk`
- Worker 当前已开始在可信完成点写入最小 memory：
  - 工具完成后会按 `MemoryPolicy` 最小 allowlist 门禁将工具结果写入 `session_memory`
  - 工具结果中的显式结构化 `userMemories` 条目可按独立写侧开关写入 `user_memory`
  - Run 完成后可按独立写侧开关写入模板化 `summary_memory`
- `knowledge_chunk` 当前已支持最小 query-aware 检索：
  - 由 Runtime 基于当前输入构造 retrieval query
  - Repository 先召回候选，再按词法命中进行重排
  - 注入 prompt 时追加 citation / source 元数据
- `user_memory` 当前已支持最小长期记忆写入：
  - 仅接受工具结果 JSON 中的 `userMemories` 数组
  - 每条记录要求显式 `memoryKey` / `memoryValue`
  - 同一 `tenant_id + user_id + memory_key` 会覆写更新而不是重复插入
  - 结构化条目当前也已支持 `ttlSeconds` 相对 TTL，并会统一按 `effective_at <= now < expires_at` 过滤读侧注入
- 启动时由 `DatabaseInitializationHostedService` 调用 `EnsureCreatedAsync`
- 空库时自动 seed 一个 `default-agent`
- 默认工具定义当前已从“空库整体 seed”演进为“确保内置工具存在”的模式，至少会补齐 `echo_context`、`async_task` 两个默认工具定义
- 启动初始化当前还会扫描旧的 webhook 工具定义；若缺少 persisted `ExecutionKind + ExecutionBinding` 但仍保留 legacy webhook flat 字段，则会自动回填 persisted webhook binding 并写回数据库
- 对已存在 persisted binding 的存量工具定义，启动初始化当前也会继续做最小存储归一：
  - webhook 绑定会回写并修复漂移的 `EndpointUrl` / `HttpMethod` / `AuthHeaders`
  - `local_handler` / `inline_result` 绑定会清空残留 legacy webhook flat 字段
- persisted `ExecutionBinding` 当前已按最小版本化文档结构落库，当前已覆盖 webhook / local handler / `inline_result` 三类最小 executor type，并为后续继续扩展更多 executor type 和版本化解析策略保留空间
- `ToolDefinition` 当前已持久化 `EndpointUrl`、`HttpMethod`、`AuthHeaders` 等 webhook 配置字段
- `table.sql` 已与当前 `ToolDefinition`、`AgentApproval` 和 `RunOutboxEvent` 结构对齐

当前仓库里尚未看到 EF Core Migration 文件，数据库初始化策略以 `EnsureCreated` 为主，而不是 migration 驱动。

### 3.5 运行时与事件流

当前异步执行抽象：

- `BestAgent.Application/AgentRuns/Runtime/AgentRunChannel.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunEventBus.cs`
- `BestAgent.Infrastructure/Runtime/AgentRunWorker.cs`

当前运行时行为：

- `POST /agent-runs` 创建 Run 后立即返回，由后台 Worker 异步执行
- `POST /agent-runs/{runId}:resume` 将恢复消息重新入队，由后台 Worker 继续执行
- `POST /agent-runs/{runId}:cancel` 可取消 `Running` / `WaitingTool` / `WaitingApproval` run，取消 pending step，写入 `cancelled` outbox 事件并推送 SSE
- `POST /agent-runs/{runId}:request-human` 可将 `Running` / `WaitingTool` / `WaitingApproval` run 切换到 `WaitingHuman`，创建 `human_wait` step 并写入 `waiting_human` 事件
- `POST /agent-runs/{runId}/tool-invocations/{invocationId}:complete` 作为异步工具专用完成回调，当前已按 `invocationId` 查询 `ToolInvocation`、校验 pending invocation / callback token / step 关联，并支持 HTTP `Idempotency-Key` 重放保护
- `POST /agent-runs/{runId}/approvals/{approvalId}:complete` 作为外部审批系统回写入口，通过 `approvalId` 找到当前 pending 审批并入队 approve / reject 后台消息；支持 HTTP `Idempotency-Key` 重放保护
- `POST /agent-runs/{runId}/steps/{stepId}:complete-human` 会校验 `waitToken` 与当前 pending `human_wait` step，并由后台 Worker 将人工结果完成 Run、替代挂起工具结果继续 loop，或人工终止 Run
- `POST /agent-runs/{runId}/steps/{stepId}:approve` 将审批通过消息重新入队，由后台 Worker 执行待批工具并继续循环
- `POST /agent-runs/{runId}/steps/{stepId}:reject` 将审批拒绝消息重新入队，由后台 Worker 将待批步骤与 Run 终止为失败态
- Worker 在每个关键 step 完成时向 `AgentRunEventBus` 发布事件
- Worker / command handler / approval timeout dispatcher 发布的生命周期事件会先尝试写入 `run_outbox_event`；Worker 当前会先继续推送进程内事件流，再对已落库事件执行最小外部 publisher best-effort 投递，成功后才标记已发布；当前覆盖 `step`、`waiting`、`waiting_approval`、`waiting_human`、`approval_timed_out`、`approval_rejected`、`cancelled`、`done`、`error`
- 独立 `RunOutboxEventDispatcher` 会轮询仍为 `pending` 的 outbox 事件，通过 publisher 补偿投递并更新发布状态；当前失败投递会在达到 `Outbox:Dispatcher:MaxRetryCount` 前继续保留 `pending` 以便重试，超过上限后再转终态 `failed`；publisher 已开始支持最小可配置 HTTP POST 外部投递
- `GET /agent-runs/{runId}/events?afterSeqNo=...` 从 `run_outbox_event` 按 `seq_no` 回放 run 级事件，支持断线后增量补拉
- `GET /agent-runs/{runId}/stream` 通过 SSE 向前端实时推送 `step`、`waiting`、`waiting_approval`、`waiting_human`、`approval_rejected`、`cancelled`、`done`、`error` 事件，并支持基于 `Last-Event-ID` 的最小自动补拉

当前 SSE 事件粒度：

- `step`：步骤完成
- `waiting`：异步工具挂起
- `waiting_approval`：高风险工具等待审批
- `approval_rejected`：审批被拒绝并终止 run
- `cancelled`：运行被取消
- `done`：运行完成
- `error`：运行失败

当前实现约束：

- SSE 事件通道仍是进程内实现，适合单体 MVP
- 已具备 run 级 outbox 事件落库、事件重放 API、独立投递器 MVP 与最小可配置 HTTP 外部投递；当前 dispatcher 也已支持批大小、轮询间隔和最大重试次数配置，并在重试耗尽后把事件转入终态失败，但尚未实现真正的跨实例外部队列分发
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
- `OpenAI:Temperature`
- `OpenAI:MaxOutputTokens`
- `OpenAI:TopP`
- `OpenAI:PresencePenalty`
- `OpenAI:FrequencyPenalty`
- `OpenAI:TimeoutSeconds`
- `WebhookSecurity:RequireSignature`
- `WebhookSecurity:ToolCallbackSecret`
- `WebhookSecurity:ApprovalCallbackSecret`
- `WebhookSecurity:ApprovalCallbackSecrets`
- `WebhookSecurity:SignatureHeaderName`
- `Approval:Policy:ApprovalRequiredSideEffectLevels`
- `Approval:Policy:RoleRequiredSideEffectLevels`
- `Approval:Policy:AllowedApproverRoles`

当前网关行为：

- 调用 `chat/completions`
- 支持 `system + user` 或仅 `user` 两种消息组合
- 对 HTTP 失败和空响应抛出 `InvalidOperationException`

### 3.7 测试

当前测试项目：

- `BestAgent.Api.Tests`

当前仓库可见测试文件共 `42` 个，其中包含 `[Fact]` 的测试文件 `39` 个，`[Fact]` 测试用例当前共 `299` 个：

- `AgentRunsControllerTests` 中 `19` 个
- `AgentDefinitionsControllerTests` 中 `8` 个
- `ToolDefinitionsControllerTests` 中 `19` 个
- `CreateAgentRunCommandHandlerTests` 中 `3` 个
- `CancelAgentRunCommandHandlerTests` 中 `3` 个
- `CompleteToolInvocationCommandHandlerTests` 中 `7` 个
- `CompleteApprovalCommandHandlerTests` 中 `6` 个
- `RequestHumanAgentRunCommandHandlerTests` 中 `8` 个
- `CompleteHumanAgentRunCommandHandlerTests` 中 `3` 个
- `ApprovalTimeoutDispatcherTests` 中 `2` 个
- `HmacWebhookRequestAuthorizerTests` 中 `5` 个
- `CreateAgentRunCommandHandlerIntegrationTests` 中 `1` 个
- `ResumeAgentRunCommandHandlerTests` 中 `3` 个
- `ApproveAgentRunStepCommandHandlerTests` 中 `6` 个
- `RejectAgentRunStepCommandHandlerTests` 中 `4` 个
- `AgentDefinitionCommandHandlerTests` 中 `4` 个
- `ToolDefinitionCommandHandlerTests` 中 `30` 个
- `AgentRunLoopTests` 中 `12` 个
- `AgentRunWorkerTests` 中 `23` 个
- `AgentRunWaitingResumeIntegrationTests` 中 `2` 个
- `GetAgentRunApprovalsQueryHandlerTests` 中 `1` 个
- `GetAgentRunEventsQueryHandlerTests` 中 `1` 个
- `GetAgentRunStepsQueryHandlerTests` 中 `1` 个
- `ToolExecutorTests` 中 `55` 个
- `HttpToolInvokerTests` 中 `14` 个
- `GlobalExceptionHandlerTests` 中 `7` 个
- `ProgramCompositionTests` 中 `3` 个
- `DatabaseInitializationHostedServiceTests` 中 `7` 个
- `AgentApprovalRepositoryTests` 中 `2` 个
- `IdempotencyRecordRepositoryTests` 中 `2` 个
- `KnowledgeChunkRepositoryTests` 中 `1` 个
- `RunOutboxEventDispatcherTests` 中 `3` 个
- `RunOutboxEventRepositoryTests` 中 `1` 个
- `RuntimeContextComposerTests` 中 `4` 个
- `ToolInvocationRepositoryTests` 中 `2` 个
- `ToolResolverTests` 中 `12` 个
- `ToolDefinitionViewModelTests` 中 `6` 个
- `UserMemoryRepositoryTests` 中 `1` 个
- `ApprovalPolicyRulesTests` 中 `8` 个

当前覆盖重点包括：

- `AgentRun` 创建接口映射、`Idempotency-Key` header 优先级与最小 `options.maxRounds` / `options.stream` 请求映射
- `GetAgentRunById` 查询返回
- `GetAgentRunById` 查询返回（包含 `WaitToken`、`CurrentStepNo`、`InterruptReason` 与最小父子 Run 关系字段）
- `GetAgentRunSteps` 查询返回（包含 typed `Approval` / `HumanWait` / `Handoff` DTO）
- `GetAgentRunApprovals` 独立审批查询返回
- `GetAgentRunEvents` run outbox 事件回放与 `afterSeqNo` 增量补拉
- `GET /agent-runs/{runId}/stream` 的 SSE 输出行为
- `CreateAgentRunCommandHandler` 创建、入队、同一 `IdempotencyKey` 重放复用已有 Run，以及请求级 `options.maxRounds` 对有效 `MaxTurns` 的收紧覆盖
- `CreateAgentRun` 创建后由后台 Worker 执行并完成 run 的集成链路
- `ResumeAgentRunCommandHandler` 恢复与状态校验
- `CancelAgentRunCommandHandler` 取消运行、取消 pending step、写入 outbox、推送事件以及终态幂等返回
- `RequestHumanAgentRunCommandHandler` 转人工、创建 `human_wait` step、写入 `waiting_human` outbox / SSE 事件
- `RequestHumanAgentRunCommandHandler` 从 `WaitingTool` / `WaitingApproval` 进入时，对原 pending `ToolInvocation` / `AgentApproval` 做一致性收尾
- `CompleteHumanAgentRunCommandHandler` 人工完成 / 终止校验与恢复入队
- `ApprovalTimeoutDispatcher` 的过期审批扫描、默认拒绝推进、事件发布与跳过非活动 Run 行为
- `HmacWebhookRequestAuthorizer` 的 HMAC 签名校验、缺失签名和错误签名拒绝行为
- 工具回调按 `invocationId -> ToolInvocation -> ToolDefinition.CallbackSecret` 解析回调 secret 的控制器行为
- `CompleteToolInvocationCommandHandler` 工具专用 complete 回调的 wait token、pending invocation 校验、恢复入队以及 `Idempotency-Key` 重放保护
- `CompleteApprovalCommandHandler` 外部审批 complete 回调的 approvalId 查询、授权、approve / reject 分支入队以及 `Idempotency-Key` 重放保护
- `ApproveAgentRunStepCommandHandler` 审批通过与状态校验
- `RejectAgentRunStepCommandHandler` 审批拒绝与状态校验
- 审批授权规则：缺少审批人身份、写类风险缺少审批角色时拒绝并映射为 403
- 可配置审批策略：自定义 side effect level 可触发审批，自定义审批角色可放行审批，最小参数模式规则可按工具输入触发审批并可选覆盖风险等级，且 `destructive` 当前已默认纳入审批触发等级
- `AgentDefinition` 控制器映射与命令分发
- `AgentDefinition` 创建、创建新版本、激活版本等 handler 逻辑
- `AgentDefinition` 创建 / 新版本创建对 `KnowledgeSources`、`MemoryPolicy` 与 `ApprovalPolicy` 的映射、规范化与查询回显
- `AgentDefinition` 创建 / 新版本创建对 `OutputSchema` 的映射、规范化与查询回显，以及运行时最终答复校验
- `AgentDefinition` 创建 / 新版本创建对 `RoutingPolicy`、`ExecutionPolicy`、`PlannerPolicy`、`ContextPolicy` 与 `AllowedHandoffs` 的映射、规范化与查询回显
- `ToolDefinition` 控制器映射与 CRUD 命令分发
- `ToolDefinition` 查询响应中的结构化 `Execution` 读侧映射与三类 binding 返回形状
- `ToolDefinition` 查询响应中兼容 flat `ExecutionBinding` 对 webhook / `inline_result` 敏感载荷做最小脱敏并与结构化 `Execution` 视图保持一致的行为
- `ToolDefinition` 查询响应中的结构化 `Policies` 读侧映射与最小策略视图返回形状
- `ToolDefinition` 查询响应中兼容 flat 策略字段按 canonical 规范化结果返回的行为
- `ToolDefinition` 查询读侧对仍停留在 legacy webhook flat 字段的存量记录合成结构化 `Execution` 视图的行为
- `ToolDefinition` 创建/更新请求中的结构化 `Execution` 写侧映射与三类 binding 归一化行为
- `ToolDefinition` 创建/更新请求中的结构化 `Policies` 写侧映射、逐字段兼容合并与冲突拒绝行为
- `ToolDefinition` API 对结构化 `Execution` 与 legacy flat 字段并存输入的一致性校验与冲突拒绝行为
- `ToolDefinition` 创建、更新、删除等 handler 逻辑
- `AgentRunLoop` 运行时分支行为（同步工具、异步工具、审批等待、最小 `handoff` 决策、`request_human` / `fail` 结构化降级决策、工具返回标准 failed 结果时的结构化失败回写）
- `AgentRunWorker` 后台消费执行行为（resume、approve、reject、complete-human、人工替代挂起工具结果继续 loop、最小 `delegate_and_wait` handoff 父子 Run 协同、审批记录创建与更新、run 级累计 `MaxTurns`、生命周期事件 outbox 落库、异步恢复/人工替代结果输出 schema 校验、工具专用 complete 回调返回标准 `succeeded/failed/error` 结果信封时的恢复行为，以及审批放行后工具返回 failed 结果时的结构化失败回写）
- 运行时结构化失败载荷在主 loop、审批放行后执行和异步恢复输出校验失败路径上携带 `CompensationPolicy` 最小审计信息的行为
- `ToolExecutor` 的 DB-first 工具调度行为（webhook、本地 handler 与 `inline_result` 调度、禁用拦截、缺失定义/配置错误）
- `ToolResolver` / `ToolExecutor` 对 persisted `ExecutionKind + ExecutionBinding` 主路径解析的行为、旧 binding 形状兼容解析、未来不支持 binding version 时的拒绝执行行为，以及缺失 persisted binding 时的拒绝执行行为
- `ToolDefinition` 创建/更新阶段对显式 binding 与 legacy webhook flat 字段冲突输入的拒绝行为，以及非 webhook 绑定拒绝 legacy webhook 字段的行为
- `ToolExecutor` 的运行时输入 schema 校验行为（缺必填字段、类型错误、额外字段拦截以及合法输入放行）
- `ToolExecutor` 的运行时输出 schema 校验行为（结果类型不匹配拦截、合法对象结果放行、pending 结果跳过即时校验；异步恢复与人工替代工具结果路径已补上后置校验）
- `ToolExecutor` 对数组 / 字符串 / 数值 / 对象常用 schema 约束与最小 `format` 的运行时校验行为（`items`、`prefixItems`、legacy `items: []` 元组写法、`additionalItems`、`min/maxItems`、`contains`、`min/maxContains`、`min/maxLength`、`minimum/maximum`、`exclusiveMinimum/exclusiveMaximum`、`min/maxProperties`、`pattern`、`const`、`email/uri/uri-reference/date-time/date/uuid/ipv4/ipv6/hostname`）
- `ToolExecutor` 对 `additionalProperties` 对象形态、无显式 `properties` 时的 `additionalProperties: false` 约束，以及 `dependentSchemas` 的运行时校验行为
- `ToolExecutor` 对最小组合关键字的运行时校验行为（`allOf`、`anyOf`、`oneOf`、`not`）
- `HttpToolInvoker` 的 HTTP 调用与响应处理
- `HttpToolInvoker` 的最小重试策略执行行为（可重试 HTTP 状态码、非重试 HTTP 状态码、超时后重试）
- `HttpToolInvoker` 对标准结果信封的兼容解析行为（`succeeded`、`pending`、`failed` 状态）以及 `status/error/meta` 到内部结果对象的最小映射
- `HttpToolInvoker` 对 `Idempotency-Key` 的透传行为
- `HttpToolInvoker` 的最小结构化日志行为（记录 tool/method/脱敏 endpoint/attempt/outcome，不外泄 query secret、认证头或原始输入 payload）
- `ToolResolver` 按最小 `IdempotencyPolicy` 生成 webhook 幂等键的行为
- `ToolDefinition` 命令层对 `IdempotencyPolicy` 的归一化与非法配置拦截
- 审批等待 / approve / reject 集成链路
- `AgentApprovalRepository` 的增查改行为
- `AgentApprovalRepository` 的按 `approvalId` 查询能力
- `AgentApprovalRepository` 的过期 pending 审批查询能力
- `IdempotencyRecordRepository` 的新增、scope 查询和 deleted 过滤
- `RunOutboxEventRepository` 的新增、run 级全量/增量回放、pending 查询、序号分配和发布状态更新
- `RunOutboxEventDispatcher` 的 pending 事件补偿发布、重试回 pending、重试耗尽后终态失败以及成功标记行为
- `GlobalExceptionHandler` 的异常映射与 500 错误 detail 暴露策略
- `Program.cs` 关键服务注册与 hosted service 组合
- `Program.cs` 中 webhook 来源认证相关服务注册
- `DatabaseInitializationHostedService` 的 `EnsureCreated` + 默认 seed 行为
- `DatabaseInitializationHostedService` 对 legacy webhook 工具定义自动回填 persisted binding、按 persisted binding 修复漂移 flat 字段、清理非 webhook 残留 flat 字段，以及对旧存量策略字段做统一规范化的行为

截至当前代码状态，测试覆盖已经不再局限于 `AgentRun` 主链路，`AgentDefinition`、`ToolDefinition`、运行时、工具执行、审批持久化以及启动 / 基础设施相关能力也已有较完整的单元测试与部分集成测试覆盖。

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

以下能力仍停留在设计层，或仅完成最小骨架，尚未在当前代码中完整落地：

- 多 Agent / Router / handoff：当前已落地 `handoff` 决策、`AllowedHandoffs` 运行时校验、父子 Run 关系写入、`WaitingHandoff` 与最小 `route_only` / `delegate_and_wait` / `delegate_and_merge` 恢复闭环、最小 handoff 深度治理，以及 run/step 读侧、子 Run 查询与递归 run tree 查询的 handoff 可观测性；`RouteRule` 当前也已进入最小定义管理态并可按版本创建/查询，handoff Runtime 也已开始最小消费命中规则的默认 `handoff_mode`、`delegate_and_merge` 下的规则级 `merge_strategy` 与边界审计元数据；`summary_only`、`memory_scope={"mode":"read_only"}` 与 `memory_scope={"mode":"disabled"}` 也已开始形成真实子 Agent 上下文/记忆边界执行；`delegate_and_merge` 当前也已开始支持单子 Run 的显式 `merge_strategy`（默认 `supervisor_summary`，并支持 `first_success` / `all_results`）；模型 handoff 决策的最小路由元数据也已进入审计链路，但 Router Agent、Supervisor Agent、更细上下文边界的真正执行约束、更丰富权限继承治理、完整 `RouteRule` 自动路由，以及 `majority_vote` / 多子 Run 汇聚等更复杂 merge strategy 仍未完整落地
- 更细粒度的人机协同：当前已落地 `WaitingHuman` 最小闭环，并已支持替代挂起工具结果继续 loop；当前也已支持对已完成 `tool_call` 结果发起人工覆盖，并保留原工具结果后再用人工结果继续生成最终答复；当前已补上“仅允许覆盖当前 run 最新已完成且仍为当前步骤的 `tool_call` 结果”的最小服务端保护，并要求 `request-human` / `complete-human` 具备明确人工操作者身份；若配置 `HumanTakeover:AllowedRoles`，上述人工接管入口还会执行最小角色校验；审批策略配置当前也已开始做合法 side effect level 规范化，但更细粒度权限边界与正式认证鉴权仍未实现
- 更完整的记忆、检索与长期知识库：当前已完成最小 memory write、`effective_at/expires_at` 活跃窗口过滤、相对 TTL (`ttlSeconds`) 写入、轻量 query rewrite、query-aware lexical retrieval、词法重排、prompt citation 注入、`model_call` / steps 读侧的最小 retrieval 审计注入，以及最终答复 `References` 追加，但更复杂的长期记忆治理、向量检索、更强语义 rewrite 和更细粒度写入策略仍未完整落地
- 可观测性：当前已补上最小 Metrics 闭环，`IAgentMetrics` + `System.Diagnostics.Metrics` 已开始覆盖 Run 创建/终态、模型调用耗时与 token/cost、工具执行耗时、检索耗时/候选数/命中数、审批等待/超时时长、outbox publish / dispatch，以及 SSE stream 建连/事件发送/连接时长；同时也已开始用统一 `ActivitySource` 为 `AgentRun`、`ModelCall`、`ToolExecution`、`Retrieval`、`Approval`、`Handoff`、outbox publish / dispatch 与 `RunStream` 链路补最小 tracing span，并开始为 `BestAgentRequestLoggingMiddleware`、`OpenAiCompatibleModelGateway`、`ToolExecutor`、`HttpToolInvoker` 与 `DefaultApprovalAuthorizer` 补最小结构化日志，但更完整的日志规范、跨实例 exporter / dashboard、trace 传播与更细粒度经营指标仍未完整落地
- 跨实例事件分发、外部队列发布与更完整的 outbox 投递语义：当前已开始支持最小可配置 HTTP outbox publisher，并把 Worker 的即时本地事件推送与外部投递状态解耦；当外部投递失败时，事件会继续保留 `pending` 交由 `RunOutboxEventDispatcher` 按 `MaxRetryCount` 补偿重试，超限后再转终态 `failed`；但真正的消息队列、批量投递、死信路由与跨实例消费拓扑仍未完成
- 正式认证鉴权中间件、后台管理界面与更完整的租户隔离治理：当前已开始落地最小 ASP.NET Core 认证管道，支持配置化 `Bearer` token 用户 / 服务身份，并在请求显式携带无效 `Authorization` 时于控制器前返回 `401`；Run API 也会继续消费这些 claims 做 scope 继承与边界校验，但强制鉴权策略、更细粒度授权模型和完整租户隔离治理仍未完成
- 正式认证鉴权中间件、后台管理界面与更完整的租户隔离治理：当前已开始落地最小 ASP.NET Core 认证管道，支持配置化 `Bearer` token 用户 / 服务身份，并在请求显式携带无效 `Authorization` 时于控制器前返回 `401`；同时也已开始补上两类最小强制鉴权开关：当 `Authentication:RequireAuthenticatedRunAccess=true` 时，交互式 Run API（创建、查询、恢复、取消、审批、人机协同、SSE）要求已认证身份后方可访问；若进一步配置 `Authentication:RunAllowedRoles`，上述 Run 入口还会执行最小角色校验；当 `Authentication:RequireAuthenticatedManagementAccess=true` 时，`AgentDefinitionsController` / `ToolDefinitionsController` 管理接口也要求已认证身份后方可访问；若进一步配置 `Authentication:ManagementAllowedRoles`，上述管理接口还会执行最小角色校验；外部 `tool/approval complete` 回调仍继续走既有 HMAC 校验；但更细粒度授权模型和完整租户隔离治理仍未完成
- 租户级审批策略、更严格的服务端权限校验与审批授权规则扩展：当前已开始支持最小 `Approval:TenantPolicies` 配置解析，Worker 的审批触发判断以及 step 级 `approve/reject` / 外部 `approval complete` 授权校验都会按 `run.tenantId` 解析租户策略；租户策略会先在全局审批默认值之上形成 tenant 级有效策略，再与版本级 `ApprovalPolicy` 一起收敛到更严格结果，避免版本策略继续放宽租户边界；但更丰富的租户策略来源、动态配置与更完整权限模型仍未完成
- 更完整的工具治理：当前已落地运行时输入 / 输出 schema 校验 MVP、参数级 `allowedPaths/deniedPaths` 最小执行、AgentDefinitionVersion 级 `DeniedTools` 显式拒绝列表、异步恢复/人工替代结果后置输出校验、HTTP webhook 最小重试策略、最小 `IdempotencyPolicy` 执行、已完成幂等工具结果与同 run 内 pending 异步调用的最小 Runtime 前置复用、普通运行与审批放行后同步工具失败的最小结构化回写，以及工具定义响应层、运行时查询/事件读侧与 `session_memory` 写回层的最小敏感字段脱敏；`AuthPolicy` 当前也已开始进入执行一致性校验主链路，运行时解析会拒绝“声明 bearer/oauth 但实际未携带 Bearer 认证头”或“非 webhook 仍声明外部认证方案”的漂移定义；`CompensationPolicy={"mode":"manual"}` 当前也已开始复用 `WaitingHuman` 提供最小人工补偿闭环，但完整 JSON Schema 规范、更丰富补偿策略执行和更复杂副作用场景的 Runtime 前置去重仍未完整落地
- 更彻底地去除对 legacy webhook flat 字段和 `ToolRegistry` 兼容语义的依赖：当前运行时 DI 与测试主路径都已切到新的 `InMemoryToolHandlerRegistry`，旧的 `ToolRegistry` 过渡别名也已移除；同时“新写入”路径也已进一步收紧对 legacy webhook flat 字段的隐式兼容，但持久化模型与对旧存量定义的启动期归一仍未完全移除
- 工具定义从当前 `ExecutionKind + ExecutionBinding` 模型继续演进为更完整的执行定义（当前已支持 webhook / local handler / `inline_result`，但更丰富 executor type、策略化解析与版本化治理仍待继续扩展）
- 跨实例事件分发、外部队列投递与更完整的流式观测能力：当前已开始为 `GET /agent-runs/{runId}/stream` 补最小 SSE connection/event/duration metrics 与 `RunStream` tracing span，但跨实例 stream 汇聚、断线统计、消费者分布与平台级 dashboard 仍未完成

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
| 事件通道 | 进程内 per-run 订阅通道 + run outbox 落库 | SSE endpoint 订阅对应 run 的 channel，生命周期事件同时写入 outbox |
| WaitingTool 恢复方式 | HTTP resume + 后台继续执行 | 当前仍对外暴露 resume 接口 |
| WaitingApproval 审计 | `AgentApproval` + `DecisionPayload` 双轨 | 独立持久化 + 运行时兼容 |

### 6.3 关键组件

- `AgentRunChannel`：封装 `Channel<AgentRunMessage>`，提供 Enqueue / Dequeue
- `AgentRunWorker`：`BackgroundService`，从 channel 消费，执行 `AgentRunLoop`
- `AgentRunEventBus`：per-run 事件分发，Worker 写入，SSE endpoint 读取
- `GET /agent-runs/{runId}/stream`：SSE endpoint，订阅 EventBus 推送事件

## 7. 建议的下一步

推荐按下面顺序继续推进：

1. 继续收敛工具执行定义：弱化 legacy webhook flat 字段，扩展 executor type / binding / resolution 的版本化模型
2. 在当前可配置审批策略 MVP 之上，继续接入正式认证鉴权、租户隔离和更细粒度权限策略
3. 多 Agent / Router / handoff
4. 跨实例事件分发、外部队列投递与更完整 outbox 投递语义
5. 记忆、检索与长期知识库

## 8. 关键文件索引

关键实现文件：

- `BestAgent.Api/Program.cs`
- `BestAgent.Api/Controllers/AgentRunsController.cs`
- `BestAgent.Api/Controllers/AgentDefinitionsController.cs`
- `BestAgent.Api/Controllers/ToolDefinitionsController.cs`
- `BestAgent.Application/DependencyInjection.cs`
- `BestAgent.Application/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Commands/ResumeAgentRun/ResumeAgentRunCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Commands/CancelAgentRun/CancelAgentRunCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Commands/CompleteToolInvocation/CompleteToolInvocationCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Commands/CompleteApproval/CompleteApprovalCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Commands/ApproveAgentRunStep/ApproveAgentRunStepCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Commands/RejectAgentRunStep/RejectAgentRunStepCommandHandler.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunChannel.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunEventBus.cs`
- `BestAgent.Application/AgentRuns/Runtime/AgentRunLoop.cs`
- `BestAgent.Application/AgentRuns/Queries/GetAgentRunSteps/GetAgentRunStepsQueryHandler.cs`
- `BestAgent.Application/AgentRuns/Queries/GetAgentRunApprovals/GetAgentRunApprovalsQueryHandler.cs`
- `BestAgent.Application/AgentRuns/Queries/GetAgentRunEvents/GetAgentRunEventsQueryHandler.cs`
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
- `BestAgent.Infrastructure/Persistence/Configurations/AgentApprovalConfiguration.cs`
- `BestAgent.Infrastructure/Persistence/Repositories/AgentApprovalRepository.cs`
- `BestAgent.Infrastructure/Persistence/Seeding/DatabaseInitializationHostedService.cs`
- `BestAgent.Infrastructure/Runtime/HttpRunOutboxEventPublisher.cs`
- `BestAgent.Infrastructure/Runtime/RunOutboxDispatcherOptions.cs`
- `BestAgent.Infrastructure/Runtime/RunOutboxPublisherOptions.cs`
- `BestAgent.Infrastructure/Tools/ToolExecutor.cs`
- `BestAgent.Infrastructure/Tools/InMemoryToolHandlerRegistry.cs`
- `BestAgent.Infrastructure/Tools/HttpToolInvoker.cs`
- `BestAgent.Infrastructure/Model/OpenAiCompatibleModelGateway.cs`

测试文件：

- `BestAgent.Api.Tests/Controllers/AgentRunsControllerTests.cs`
- `BestAgent.Api.Tests/Controllers/AgentDefinitionsControllerTests.cs`
- `BestAgent.Api.Tests/Controllers/ToolDefinitionsControllerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandlerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/CreateAgentRun/CreateAgentRunCommandHandlerIntegrationTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/ResumeAgentRun/ResumeAgentRunCommandHandlerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/ApproveAgentRunStep/ApproveAgentRunStepCommandHandlerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Commands/RejectAgentRunStep/RejectAgentRunStepCommandHandlerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Runtime/AgentRunLoopTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Runtime/AgentRunWorkerTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Integration/AgentRunWaitingResumeIntegrationTests.cs`
- `BestAgent.Api.Tests/AgentRuns/Queries/GetAgentRunApprovals/GetAgentRunApprovalsQueryHandlerTests.cs`
- `BestAgent.Api.Tests/AgentDefinitions/Commands/AgentDefinitionCommandHandlerTests.cs`
- `BestAgent.Api.Tests/Tools/Commands/ToolDefinitionCommandHandlerTests.cs`
- `BestAgent.Api.Tests/Tools/ToolExecutorTests.cs`
- `BestAgent.Api.Tests/Tools/HttpToolInvokerTests.cs`
- `BestAgent.Api.Tests/Infrastructure/AgentApprovalRepositoryTests.cs`
- `BestAgent.Api.Tests/Infrastructure/GlobalExceptionHandlerTests.cs`
- `BestAgent.Api.Tests/Infrastructure/ProgramCompositionTests.cs`
- `BestAgent.Api.Tests/Infrastructure/DatabaseInitializationHostedServiceTests.cs`
