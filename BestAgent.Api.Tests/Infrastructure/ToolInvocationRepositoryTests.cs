using BestAgent.Domain.Tools;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class ToolInvocationRepositoryTests
{
    [Fact]
    public async Task ShouldAddGetListAndUpdate()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 6, 13, 0, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new ToolInvocationRepository(dbContext);

            await repository.AddAsync(
                CreateInvocation("invocation-1", "run-1", "step-1", "Pending", "async", now),
                CancellationToken.None);
            await repository.AddAsync(
                CreateInvocation("invocation-2", "run-1", "step-2", "Completed", "sync", now.AddSeconds(1)),
                CancellationToken.None);
            await repository.AddAsync(
                CreateInvocation("invocation-3", "run-2", "step-3", "Pending", "async", now.AddSeconds(2)),
                CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new ToolInvocationRepository(dbContext);

            var byId = await repository.GetByInvocationIdAsync("invocation-1", CancellationToken.None);
            var pending = await repository.GetPendingByRunIdAndStepIdAsync("run-1", "step-1", CancellationToken.None);
            var notPending = await repository.GetPendingByRunIdAndStepIdAsync("run-1", "step-2", CancellationToken.None);
            var runInvocations = await repository.ListByRunIdAsync("run-1", CancellationToken.None);

            Assert.NotNull(byId);
            Assert.Equal("weather", byId!.ToolName);
            Assert.Equal("wait-1", byId.CallbackToken);
            Assert.NotNull(pending);
            Assert.Null(notPending);
            Assert.Equal(["invocation-1", "invocation-2"], runInvocations.Select(x => x.InvocationId));

            await repository.UpdateAsync(
                pending! with
                {
                    Status = "Completed",
                    OutputPayload = "{\"ok\":true}",
                    EndedAt = now.AddMinutes(1),
                    LastModifyTime = now.AddMinutes(1)
                },
                CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new ToolInvocationRepository(dbContext);

            var updated = await repository.GetByInvocationIdAsync("invocation-1", CancellationToken.None);
            var pending = await repository.GetPendingByRunIdAndStepIdAsync("run-1", "step-1", CancellationToken.None);

            Assert.NotNull(updated);
            Assert.Equal("Completed", updated!.Status);
            Assert.Equal("{\"ok\":true}", updated.OutputPayload);
            Assert.Null(pending);
        }
    }

    [Fact]
    public async Task Queries_ShouldIgnoreDeletedInvocations()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 6, 13, 0, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            await dbContext.ToolInvocations.AddAsync(
                CreateInvocation("invocation-1", "run-1", "step-1", "Pending", "async", now) with
                {
                    Deleted = true
                });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new ToolInvocationRepository(dbContext);

            var byId = await repository.GetByInvocationIdAsync("invocation-1", CancellationToken.None);
            var pending = await repository.GetPendingByRunIdAndStepIdAsync("run-1", "step-1", CancellationToken.None);
            var runInvocations = await repository.ListByRunIdAsync("run-1", CancellationToken.None);

            Assert.Null(byId);
            Assert.Null(pending);
            Assert.Empty(runInvocations);
        }
    }

    private static ToolInvocation CreateInvocation(
        string invocationId,
        string runId,
        string stepId,
        string status,
        string mode,
        DateTime now)
    {
        return new ToolInvocation
        {
            InvocationId = invocationId,
            RunId = runId,
            StepId = stepId,
            ToolName = "weather",
            Mode = mode,
            Status = status,
            InputPayload = "{\"city\":\"Shanghai\"}",
            OutputPayload = status == "Completed" ? "sunny" : null,
            IdempotencyKey = invocationId,
            CallbackToken = status == "Pending" ? "wait-1" : string.Empty,
            StartedAt = now,
            EndedAt = status == "Completed" ? now.AddMilliseconds(50) : null,
            DurationMs = status == "Completed" ? 50 : 0,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };
    }
}
