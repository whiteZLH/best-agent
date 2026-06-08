using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Knowledge;
using BestAgent.Infrastructure.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Runtime;

public class RuntimeMemoryWriterTests
{
    [Fact]
    public async Task RecordToolResultAsync_ShouldSupportRelativeUserMemoryTtl()
    {
        var sessionMemoryRepository = Substitute.For<ISessionMemoryRepository>();
        var userMemoryRepository = Substitute.For<IUserMemoryRepository>();
        var summaryMemoryRepository = Substitute.For<ISummaryMemoryRepository>();
        var agentStepRepository = Substitute.For<IAgentStepRepository>();
        var writer = new RuntimeMemoryWriter(
            sessionMemoryRepository,
            userMemoryRepository,
            summaryMemoryRepository,
            agentStepRepository,
            NullLogger<RuntimeMemoryWriter>.Instance);
        var run = new AgentRun
        {
            RunId = "run-001",
            TenantId = "tenant-1",
            UserId = "user-1",
            SessionId = "session-1"
        };
        var toolOutput = """
            {
              "userMemories": [
                {
                  "memoryKey": "seat_preference",
                  "memoryType": "preference",
                  "memoryValue": "\"window\"",
                  "ttlSeconds": 90
                }
              ]
            }
            """;
        var before = DateTime.UtcNow;

        await writer.RecordToolResultAsync(run, "profile_tool", "{\"userId\":\"user-1\"}", toolOutput, false, true, CancellationToken.None);

        var after = DateTime.UtcNow;
        await userMemoryRepository.Received(1).AddAsync(
            Arg.Is<UserMemory>(memory =>
                memory.MemoryKey == "seat_preference" &&
                memory.ExpiresAt != null &&
                memory.ExpiresAt >= before.AddSeconds(90) &&
                memory.ExpiresAt <= after.AddSeconds(90)),
            Arg.Any<CancellationToken>());
    }
}
