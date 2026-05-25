-- Agent platform database schema
-- Assumption: PostgreSQL 16+
-- Convention:
-- 1. String IDs are used to align with runtime-generated identifiers such as run_123 / step_456.
-- 2. JSONB fields are used for policy, payload, and schema-like content.
-- 3. Logical deletion is implemented through deleted.
-- 4. Every table includes the required audit columns:
--    last_modifier, last_modify_time, last_modifier_name,
--    create_time, creator_name, creator, deleted

CREATE OR REPLACE FUNCTION set_last_modify_time()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.last_modify_time = CURRENT_TIMESTAMP(3);
    RETURN NEW;
END;
$$;

CREATE TABLE IF NOT EXISTS agent_definition (
    id VARCHAR(64) PRIMARY KEY,
    code VARCHAR(128) NOT NULL,
    name VARCHAR(256) NOT NULL,
    description TEXT,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    current_version INT NOT NULL DEFAULT 1,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS agent_definition_version (
    id VARCHAR(64) PRIMARY KEY,
    agent_definition_id VARCHAR(64) NOT NULL,
    version INT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'draft',
    name VARCHAR(256) NOT NULL,
    description TEXT,
    instruction TEXT,
    system_prompt_template TEXT,
    default_model VARCHAR(128) NOT NULL DEFAULT '',
    allowed_tools JSONB,
    knowledge_sources JSONB,
    memory_policy JSONB,
    routing_policy JSONB,
    approval_policy JSONB,
    execution_policy JSONB,
    planner_policy JSONB,
    context_policy JSONB,
    allowed_handoffs JSONB,
    output_schema JSONB,
    max_turns INT NOT NULL DEFAULT 8,
    max_cost DECIMAL(18, 6) NOT NULL DEFAULT 0,
    published_at TIMESTAMP(3),
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS tool_definition (
    id VARCHAR(64) PRIMARY KEY,
    tool_name VARCHAR(128) NOT NULL,
    display_name VARCHAR(256) NOT NULL DEFAULT '',
    description TEXT,
    input_schema JSONB,
    output_schema JSONB,
    side_effect_level VARCHAR(32) NOT NULL DEFAULT 'read_only',
    timeout_ms INT NOT NULL DEFAULT 30000,
    retry_policy JSONB,
    auth_policy JSONB,
    idempotency_policy JSONB,
    async_supported BOOLEAN NOT NULL DEFAULT FALSE,
    consistency_mode VARCHAR(32) NOT NULL DEFAULT 'eventual',
    compensation_policy JSONB,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS route_rule (
    id VARCHAR(64) PRIMARY KEY,
    agent_definition_version_id VARCHAR(64) NOT NULL,
    source_agent_code VARCHAR(128) NOT NULL DEFAULT '',
    target_agent_code VARCHAR(128) NOT NULL,
    rule_name VARCHAR(128) NOT NULL,
    priority INT NOT NULL DEFAULT 100,
    match_type VARCHAR(64) NOT NULL DEFAULT 'intent',
    match_expression JSONB,
    handoff_mode VARCHAR(32) NOT NULL DEFAULT 'route_only',
    context_scope JSONB,
    tool_scope JSONB,
    knowledge_scope JSONB,
    approval_required BOOLEAN NOT NULL DEFAULT FALSE,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS agent_run (
    run_id VARCHAR(64) PRIMARY KEY,
    agent_code VARCHAR(128) NOT NULL,
    agent_definition_version_id VARCHAR(64) NOT NULL DEFAULT '',
    tenant_id VARCHAR(64) NOT NULL DEFAULT '',
    user_id VARCHAR(64) NOT NULL DEFAULT '',
    session_id VARCHAR(64) NOT NULL DEFAULT '',
    status VARCHAR(32) NOT NULL,
    input_payload JSONB,
    output_payload JSONB,
    current_step_no INT NOT NULL DEFAULT 0,
    parent_run_id VARCHAR(64) NOT NULL DEFAULT '',
    root_run_id VARCHAR(64) NOT NULL DEFAULT '',
    delegated_by_run_id VARCHAR(64) NOT NULL DEFAULT '',
    delegated_by_agent VARCHAR(128) NOT NULL DEFAULT '',
    status_version BIGINT NOT NULL DEFAULT 0,
    idempotency_key VARCHAR(128) NOT NULL,
    current_wait_token VARCHAR(128) NOT NULL DEFAULT '',
    interrupt_reason VARCHAR(256) NOT NULL DEFAULT '',
    max_turns INT NOT NULL DEFAULT 0,
    max_cost DECIMAL(18, 6) NOT NULL DEFAULT 0,
    total_cost DECIMAL(18, 6) NOT NULL DEFAULT 0,
    started_at TIMESTAMP(3),
    ended_at TIMESTAMP(3),
    last_heartbeat_at TIMESTAMP(3),
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS agent_step (
    step_id VARCHAR(64) PRIMARY KEY,
    run_id VARCHAR(64) NOT NULL,
    step_no INT NOT NULL,
    step_type VARCHAR(32) NOT NULL,
    status VARCHAR(32) NOT NULL,
    input_payload JSONB,
    output_payload JSONB,
    error_payload JSONB,
    step_key VARCHAR(128) NOT NULL,
    retry_count INT NOT NULL DEFAULT 0,
    depends_on_step_id VARCHAR(64) NOT NULL DEFAULT '',
    decision_payload JSONB,
    status_version BIGINT NOT NULL DEFAULT 0,
    started_at TIMESTAMP(3),
    ended_at TIMESTAMP(3),
    duration_ms BIGINT NOT NULL DEFAULT 0,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS agent_message (
    message_id VARCHAR(64) PRIMARY KEY,
    run_id VARCHAR(64) NOT NULL,
    step_id VARCHAR(64) NOT NULL DEFAULT '',
    role VARCHAR(32) NOT NULL,
    message_type VARCHAR(32) NOT NULL DEFAULT 'normal',
    seq_no BIGINT NOT NULL DEFAULT 0,
    content TEXT,
    content_json JSONB,
    source_ref VARCHAR(256) NOT NULL DEFAULT '',
    created_at TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS agent_approval (
    approval_id VARCHAR(64) PRIMARY KEY,
    run_id VARCHAR(64) NOT NULL,
    step_id VARCHAR(64) NOT NULL,
    requested_action VARCHAR(256) NOT NULL,
    risk_level VARCHAR(32) NOT NULL DEFAULT 'medium',
    request_payload JSONB,
    decision VARCHAR(32) NOT NULL DEFAULT 'pending',
    approver_id VARCHAR(64) NOT NULL DEFAULT '',
    approver_role VARCHAR(64) NOT NULL DEFAULT '',
    approver_name VARCHAR(128) NOT NULL DEFAULT '',
    "comment" VARCHAR(512) NOT NULL DEFAULT '',
    wait_token VARCHAR(128) NOT NULL DEFAULT '',
    expires_at TIMESTAMP(3),
    decided_at TIMESTAMP(3),
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS tool_invocation (
    invocation_id VARCHAR(64) PRIMARY KEY,
    run_id VARCHAR(64) NOT NULL,
    step_id VARCHAR(64) NOT NULL,
    tool_name VARCHAR(128) NOT NULL,
    mode VARCHAR(16) NOT NULL DEFAULT 'sync',
    status VARCHAR(32) NOT NULL,
    input_payload JSONB,
    output_payload JSONB,
    error_payload JSONB,
    idempotency_key VARCHAR(128) NOT NULL,
    callback_token VARCHAR(128) NOT NULL DEFAULT '',
    executor_node VARCHAR(128) NOT NULL DEFAULT '',
    started_at TIMESTAMP(3),
    ended_at TIMESTAMP(3),
    duration_ms BIGINT NOT NULL DEFAULT 0,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS idempotency_record (
    id VARCHAR(64) PRIMARY KEY,
    scope_type VARCHAR(32) NOT NULL,
    scope_key VARCHAR(128) NOT NULL,
    request_hash VARCHAR(128) NOT NULL DEFAULT '',
    target_id VARCHAR(64) NOT NULL DEFAULT '',
    status VARCHAR(32) NOT NULL DEFAULT 'created',
    expire_at TIMESTAMP(3),
    extra_payload JSONB,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS run_outbox_event (
    event_id VARCHAR(64) PRIMARY KEY,
    run_id VARCHAR(64) NOT NULL,
    seq_no BIGINT NOT NULL,
    event_type VARCHAR(64) NOT NULL,
    run_status VARCHAR(32) NOT NULL,
    payload JSONB,
    publish_status VARCHAR(32) NOT NULL DEFAULT 'pending',
    published_at TIMESTAMP(3),
    retry_count INT NOT NULL DEFAULT 0,
    occurred_at TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS model_call_log (
    id VARCHAR(64) PRIMARY KEY,
    run_id VARCHAR(64) NOT NULL DEFAULT '',
    step_id VARCHAR(64) NOT NULL DEFAULT '',
    model_name VARCHAR(128) NOT NULL,
    provider_name VARCHAR(64) NOT NULL DEFAULT '',
    request_mode VARCHAR(32) NOT NULL DEFAULT 'chat',
    request_payload JSONB,
    response_payload JSONB,
    prompt_tokens INT NOT NULL DEFAULT 0,
    completion_tokens INT NOT NULL DEFAULT 0,
    total_tokens INT NOT NULL DEFAULT 0,
    latency_ms BIGINT NOT NULL DEFAULT 0,
    cost_amount DECIMAL(18, 6) NOT NULL DEFAULT 0,
    finish_reason VARCHAR(64) NOT NULL DEFAULT '',
    success_flag BOOLEAN NOT NULL DEFAULT TRUE,
    error_code VARCHAR(64) NOT NULL DEFAULT '',
    error_message VARCHAR(512) NOT NULL DEFAULT '',
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS tool_execution_log (
    id VARCHAR(64) PRIMARY KEY,
    invocation_id VARCHAR(64) NOT NULL,
    run_id VARCHAR(64) NOT NULL DEFAULT '',
    step_id VARCHAR(64) NOT NULL DEFAULT '',
    tool_name VARCHAR(128) NOT NULL,
    request_payload JSONB,
    response_payload JSONB,
    latency_ms BIGINT NOT NULL DEFAULT 0,
    success_flag BOOLEAN NOT NULL DEFAULT TRUE,
    error_code VARCHAR(64) NOT NULL DEFAULT '',
    error_message VARCHAR(512) NOT NULL DEFAULT '',
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS policy_audit_log (
    id VARCHAR(64) PRIMARY KEY,
    run_id VARCHAR(64) NOT NULL DEFAULT '',
    step_id VARCHAR(64) NOT NULL DEFAULT '',
    policy_type VARCHAR(64) NOT NULL,
    policy_name VARCHAR(128) NOT NULL DEFAULT '',
    decision VARCHAR(32) NOT NULL,
    input_payload JSONB,
    result_payload JSONB,
    reason VARCHAR(512) NOT NULL DEFAULT '',
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS knowledge_document (
    id VARCHAR(64) PRIMARY KEY,
    tenant_id VARCHAR(64) NOT NULL DEFAULT '',
    knowledge_source_code VARCHAR(128) NOT NULL DEFAULT '',
    document_code VARCHAR(128) NOT NULL DEFAULT '',
    title VARCHAR(512) NOT NULL,
    source_uri VARCHAR(1024) NOT NULL DEFAULT '',
    content_type VARCHAR(64) NOT NULL DEFAULT '',
    metadata JSONB,
    parse_status VARCHAR(32) NOT NULL DEFAULT 'pending',
    version_no INT NOT NULL DEFAULT 1,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS knowledge_chunk (
    id VARCHAR(64) PRIMARY KEY,
    document_id VARCHAR(64) NOT NULL,
    tenant_id VARCHAR(64) NOT NULL DEFAULT '',
    chunk_no INT NOT NULL,
    content TEXT NOT NULL,
    token_count INT NOT NULL DEFAULT 0,
    source VARCHAR(512) NOT NULL DEFAULT '',
    metadata JSONB,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS embedding_index (
    id VARCHAR(64) PRIMARY KEY,
    tenant_id VARCHAR(64) NOT NULL DEFAULT '',
    source_type VARCHAR(32) NOT NULL,
    source_id VARCHAR(64) NOT NULL,
    model_name VARCHAR(128) NOT NULL DEFAULT '',
    vector_ref VARCHAR(256) NOT NULL DEFAULT '',
    vector_payload JSONB,
    metadata JSONB,
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS session_memory (
    id VARCHAR(64) PRIMARY KEY,
    tenant_id VARCHAR(64) NOT NULL DEFAULT '',
    user_id VARCHAR(64) NOT NULL DEFAULT '',
    session_id VARCHAR(64) NOT NULL DEFAULT '',
    run_id VARCHAR(64) NOT NULL DEFAULT '',
    memory_type VARCHAR(32) NOT NULL DEFAULT 'recent_context',
    content_json JSONB,
    source_type VARCHAR(32) NOT NULL DEFAULT '',
    source_ref VARCHAR(128) NOT NULL DEFAULT '',
    confidence DECIMAL(5, 4) NOT NULL DEFAULT 1.0000,
    expires_at TIMESTAMP(3),
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS user_memory (
    id VARCHAR(64) PRIMARY KEY,
    tenant_id VARCHAR(64) NOT NULL DEFAULT '',
    user_id VARCHAR(64) NOT NULL,
    memory_key VARCHAR(128) NOT NULL,
    memory_scope VARCHAR(64) NOT NULL DEFAULT 'user',
    memory_type VARCHAR(32) NOT NULL DEFAULT 'preference',
    memory_value JSONB,
    source_type VARCHAR(32) NOT NULL DEFAULT '',
    source_ref VARCHAR(128) NOT NULL DEFAULT '',
    confidence DECIMAL(5, 4) NOT NULL DEFAULT 1.0000,
    effective_at TIMESTAMP(3),
    expires_at TIMESTAMP(3),
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS summary_memory (
    id VARCHAR(64) PRIMARY KEY,
    tenant_id VARCHAR(64) NOT NULL DEFAULT '',
    run_id VARCHAR(64) NOT NULL DEFAULT '',
    session_id VARCHAR(64) NOT NULL DEFAULT '',
    summary_type VARCHAR(32) NOT NULL DEFAULT 'conversation',
    source_start_seq BIGINT NOT NULL DEFAULT 0,
    source_end_seq BIGINT NOT NULL DEFAULT 0,
    summary_text TEXT NOT NULL,
    summary_json JSONB,
    generated_by_model VARCHAR(128) NOT NULL DEFAULT '',
    generated_at TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    expires_at TIMESTAMP(3),
    last_modifier VARCHAR(64) NOT NULL DEFAULT '',
    last_modify_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_modifier_name VARCHAR(128) NOT NULL DEFAULT '',
    create_time TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    creator_name VARCHAR(128) NOT NULL DEFAULT '',
    creator VARCHAR(64) NOT NULL DEFAULT '',
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_agent_definition_code ON agent_definition (code);
CREATE INDEX IF NOT EXISTS idx_agent_definition_enabled ON agent_definition (enabled);
CREATE INDEX IF NOT EXISTS idx_agent_definition_deleted ON agent_definition (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_agent_def_ver ON agent_definition_version (agent_definition_id, version);
CREATE INDEX IF NOT EXISTS idx_agent_def_ver_status ON agent_definition_version (status);
CREATE INDEX IF NOT EXISTS idx_agent_def_ver_deleted ON agent_definition_version (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_tool_definition_name ON tool_definition (tool_name);
CREATE INDEX IF NOT EXISTS idx_tool_definition_effect ON tool_definition (side_effect_level);
CREATE INDEX IF NOT EXISTS idx_tool_definition_enabled ON tool_definition (enabled);
CREATE INDEX IF NOT EXISTS idx_tool_definition_deleted ON tool_definition (deleted);

CREATE INDEX IF NOT EXISTS idx_route_rule_source ON route_rule (source_agent_code);
CREATE INDEX IF NOT EXISTS idx_route_rule_target ON route_rule (target_agent_code);
CREATE INDEX IF NOT EXISTS idx_route_rule_priority ON route_rule (priority);
CREATE INDEX IF NOT EXISTS idx_route_rule_deleted ON route_rule (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_agent_run_idempotency ON agent_run (idempotency_key);
CREATE INDEX IF NOT EXISTS idx_agent_run_root_run ON agent_run (root_run_id);
CREATE INDEX IF NOT EXISTS idx_agent_run_parent_run ON agent_run (parent_run_id);
CREATE INDEX IF NOT EXISTS idx_agent_run_status ON agent_run (status);
CREATE INDEX IF NOT EXISTS idx_agent_run_tenant_session ON agent_run (tenant_id, session_id);
CREATE INDEX IF NOT EXISTS idx_agent_run_user ON agent_run (user_id);
CREATE INDEX IF NOT EXISTS idx_agent_run_deleted ON agent_run (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_agent_step_run_key ON agent_step (run_id, step_key);
CREATE UNIQUE INDEX IF NOT EXISTS uk_agent_step_run_no ON agent_step (run_id, step_no);
CREATE INDEX IF NOT EXISTS idx_agent_step_type ON agent_step (step_type);
CREATE INDEX IF NOT EXISTS idx_agent_step_status ON agent_step (status);
CREATE INDEX IF NOT EXISTS idx_agent_step_depends ON agent_step (depends_on_step_id);
CREATE INDEX IF NOT EXISTS idx_agent_step_deleted ON agent_step (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_agent_message_run_seq ON agent_message (run_id, seq_no);
CREATE INDEX IF NOT EXISTS idx_agent_message_run_created ON agent_message (run_id, created_at);
CREATE INDEX IF NOT EXISTS idx_agent_message_step ON agent_message (step_id);
CREATE INDEX IF NOT EXISTS idx_agent_message_role ON agent_message (role);
CREATE INDEX IF NOT EXISTS idx_agent_message_deleted ON agent_message (deleted);

CREATE INDEX IF NOT EXISTS idx_agent_approval_run ON agent_approval (run_id);
CREATE INDEX IF NOT EXISTS idx_agent_approval_step ON agent_approval (step_id);
CREATE INDEX IF NOT EXISTS idx_agent_approval_decision ON agent_approval (decision);
CREATE INDEX IF NOT EXISTS idx_agent_approval_expires ON agent_approval (expires_at);
CREATE INDEX IF NOT EXISTS idx_agent_approval_deleted ON agent_approval (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_tool_invocation_idempotency ON tool_invocation (idempotency_key);
CREATE INDEX IF NOT EXISTS idx_tool_invocation_run ON tool_invocation (run_id);
CREATE INDEX IF NOT EXISTS idx_tool_invocation_step ON tool_invocation (step_id);
CREATE INDEX IF NOT EXISTS idx_tool_invocation_tool_status ON tool_invocation (tool_name, status);
CREATE INDEX IF NOT EXISTS idx_tool_invocation_deleted ON tool_invocation (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_idempotency_scope ON idempotency_record (scope_type, scope_key);
CREATE INDEX IF NOT EXISTS idx_idempotency_target ON idempotency_record (target_id);
CREATE INDEX IF NOT EXISTS idx_idempotency_expire ON idempotency_record (expire_at);
CREATE INDEX IF NOT EXISTS idx_idempotency_deleted ON idempotency_record (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_outbox_run_seq ON run_outbox_event (run_id, seq_no);
CREATE INDEX IF NOT EXISTS idx_outbox_publish_status ON run_outbox_event (publish_status);
CREATE INDEX IF NOT EXISTS idx_outbox_event_type ON run_outbox_event (event_type);
CREATE INDEX IF NOT EXISTS idx_outbox_deleted ON run_outbox_event (deleted);

CREATE INDEX IF NOT EXISTS idx_model_call_run ON model_call_log (run_id);
CREATE INDEX IF NOT EXISTS idx_model_call_step ON model_call_log (step_id);
CREATE INDEX IF NOT EXISTS idx_model_call_model ON model_call_log (model_name);
CREATE INDEX IF NOT EXISTS idx_model_call_provider ON model_call_log (provider_name);
CREATE INDEX IF NOT EXISTS idx_model_call_deleted ON model_call_log (deleted);

CREATE INDEX IF NOT EXISTS idx_tool_execution_invocation ON tool_execution_log (invocation_id);
CREATE INDEX IF NOT EXISTS idx_tool_execution_run ON tool_execution_log (run_id);
CREATE INDEX IF NOT EXISTS idx_tool_execution_step ON tool_execution_log (step_id);
CREATE INDEX IF NOT EXISTS idx_tool_execution_tool ON tool_execution_log (tool_name);
CREATE INDEX IF NOT EXISTS idx_tool_execution_deleted ON tool_execution_log (deleted);

CREATE INDEX IF NOT EXISTS idx_policy_audit_run ON policy_audit_log (run_id);
CREATE INDEX IF NOT EXISTS idx_policy_audit_step ON policy_audit_log (step_id);
CREATE INDEX IF NOT EXISTS idx_policy_audit_type ON policy_audit_log (policy_type);
CREATE INDEX IF NOT EXISTS idx_policy_audit_decision ON policy_audit_log (decision);
CREATE INDEX IF NOT EXISTS idx_policy_audit_deleted ON policy_audit_log (deleted);

CREATE INDEX IF NOT EXISTS idx_knowledge_document_tenant ON knowledge_document (tenant_id);
CREATE INDEX IF NOT EXISTS idx_knowledge_document_source ON knowledge_document (knowledge_source_code);
CREATE INDEX IF NOT EXISTS idx_knowledge_document_code ON knowledge_document (document_code);
CREATE INDEX IF NOT EXISTS idx_knowledge_document_deleted ON knowledge_document (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_knowledge_chunk_doc_no ON knowledge_chunk (document_id, chunk_no);
CREATE INDEX IF NOT EXISTS idx_knowledge_chunk_tenant ON knowledge_chunk (tenant_id);
CREATE INDEX IF NOT EXISTS idx_knowledge_chunk_deleted ON knowledge_chunk (deleted);

CREATE INDEX IF NOT EXISTS idx_embedding_source ON embedding_index (source_type, source_id);
CREATE INDEX IF NOT EXISTS idx_embedding_tenant ON embedding_index (tenant_id);
CREATE INDEX IF NOT EXISTS idx_embedding_model ON embedding_index (model_name);
CREATE INDEX IF NOT EXISTS idx_embedding_deleted ON embedding_index (deleted);

CREATE INDEX IF NOT EXISTS idx_session_memory_session ON session_memory (tenant_id, session_id);
CREATE INDEX IF NOT EXISTS idx_session_memory_user ON session_memory (tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_session_memory_run ON session_memory (run_id);
CREATE INDEX IF NOT EXISTS idx_session_memory_expires ON session_memory (expires_at);
CREATE INDEX IF NOT EXISTS idx_session_memory_deleted ON session_memory (deleted);

CREATE UNIQUE INDEX IF NOT EXISTS uk_user_memory_key ON user_memory (tenant_id, user_id, memory_key);
CREATE INDEX IF NOT EXISTS idx_user_memory_scope ON user_memory (memory_scope);
CREATE INDEX IF NOT EXISTS idx_user_memory_type ON user_memory (memory_type);
CREATE INDEX IF NOT EXISTS idx_user_memory_expires ON user_memory (expires_at);
CREATE INDEX IF NOT EXISTS idx_user_memory_deleted ON user_memory (deleted);

CREATE INDEX IF NOT EXISTS idx_summary_memory_run ON summary_memory (run_id);
CREATE INDEX IF NOT EXISTS idx_summary_memory_session ON summary_memory (tenant_id, session_id);
CREATE INDEX IF NOT EXISTS idx_summary_memory_range ON summary_memory (source_start_seq, source_end_seq);
CREATE INDEX IF NOT EXISTS idx_summary_memory_expires ON summary_memory (expires_at);
CREATE INDEX IF NOT EXISTS idx_summary_memory_deleted ON summary_memory (deleted);

COMMENT ON TABLE agent_definition IS 'Agent definition master table';
COMMENT ON TABLE agent_definition_version IS 'Versioned snapshot of an agent definition';
COMMENT ON TABLE tool_definition IS 'Tool registry metadata';
COMMENT ON TABLE route_rule IS 'Routing and handoff rules';
COMMENT ON TABLE agent_run IS 'Runtime run instance';
COMMENT ON TABLE agent_step IS 'Atomic step inside a run';
COMMENT ON TABLE agent_message IS 'Conversation and runtime message log';
COMMENT ON TABLE agent_approval IS 'Approval and human-in-the-loop records';
COMMENT ON TABLE tool_invocation IS 'Tool invocation record';
COMMENT ON TABLE idempotency_record IS 'Reusable idempotency registry';
COMMENT ON TABLE run_outbox_event IS 'Transactional outbox for run events';
COMMENT ON TABLE model_call_log IS 'Model invocation audit log';
COMMENT ON TABLE tool_execution_log IS 'Tool execution audit log';
COMMENT ON TABLE policy_audit_log IS 'Policy evaluation audit log';
COMMENT ON TABLE knowledge_document IS 'Knowledge document metadata';
COMMENT ON TABLE knowledge_chunk IS 'Chunked knowledge content';
COMMENT ON TABLE embedding_index IS 'Embedding index metadata';
COMMENT ON TABLE session_memory IS 'Short-term session memory';
COMMENT ON TABLE user_memory IS 'Long-term user memory';
COMMENT ON TABLE summary_memory IS 'Summary memory for long context compression';

DO $$
DECLARE
    table_name TEXT;
BEGIN
    FOREACH table_name IN ARRAY ARRAY[
        'agent_definition',
        'agent_definition_version',
        'tool_definition',
        'route_rule',
        'agent_run',
        'agent_step',
        'agent_message',
        'agent_approval',
        'tool_invocation',
        'idempotency_record',
        'run_outbox_event',
        'model_call_log',
        'tool_execution_log',
        'policy_audit_log',
        'knowledge_document',
        'knowledge_chunk',
        'embedding_index',
        'session_memory',
        'user_memory',
        'summary_memory'
    ]
    LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS trg_set_last_modify_time ON %I', table_name);
        EXECUTE format(
            'CREATE TRIGGER trg_set_last_modify_time BEFORE UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION set_last_modify_time()',
            table_name
        );
    END LOOP;
END;
$$;
