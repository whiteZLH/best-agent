using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_definition",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    instruction = table.Column<string>(type: "text", nullable: false),
                    default_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    allowed_tools = table.Column<string>(type: "jsonb", nullable: false),
                    max_turns = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_modifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_modify_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    creator = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_definition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_message",
                columns: table => new
                {
                    message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_modify_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    creator = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_message", x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "agent_run",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    agent_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    input_payload = table.Column<string>(type: "jsonb", nullable: false),
                    output_payload = table.Column<string>(type: "jsonb", nullable: true),
                    current_step_no = table.Column<int>(type: "integer", nullable: false),
                    status_version = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    last_modifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_modify_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    creator = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_run", x => x.run_id);
                });

            migrationBuilder.CreateTable(
                name: "agent_step",
                columns: table => new
                {
                    step_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    step_no = table.Column<int>(type: "integer", nullable: false),
                    step_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    input_payload = table.Column<string>(type: "text", nullable: false),
                    output_payload = table.Column<string>(type: "text", nullable: true),
                    error_payload = table.Column<string>(type: "text", nullable: true),
                    step_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_modify_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    creator = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_step", x => x.step_id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_record",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_modify_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    creator = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "run_outbox_event",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    seq_no = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_modify_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    creator = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_outbox_event", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "tool_invocation",
                columns: table => new
                {
                    invocation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    run_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    step_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tool_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    input_payload = table.Column<string>(type: "text", nullable: false),
                    output_payload = table.Column<string>(type: "text", nullable: true),
                    error_payload = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_modifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_modify_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modifier_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    create_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    creator = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_invocation", x => x.invocation_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_definition_code",
                table: "agent_definition",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_message_run_id",
                table: "agent_message",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_idempotency_key",
                table: "agent_run",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_step_run_id",
                table: "agent_step",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_step_run_id_step_key",
                table: "agent_step",
                columns: new[] { "run_id", "step_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_record_idempotency_key",
                table: "idempotency_record",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_run_outbox_event_run_id_seq_no",
                table: "run_outbox_event",
                columns: new[] { "run_id", "seq_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tool_invocation_idempotency_key",
                table: "tool_invocation",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_definition");

            migrationBuilder.DropTable(
                name: "agent_message");

            migrationBuilder.DropTable(
                name: "agent_run");

            migrationBuilder.DropTable(
                name: "agent_step");

            migrationBuilder.DropTable(
                name: "idempotency_record");

            migrationBuilder.DropTable(
                name: "run_outbox_event");

            migrationBuilder.DropTable(
                name: "tool_invocation");
        }
    }
}
