using BestAgent.Domain.AgentRuns;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class AgentApprovalRepositoryTests
{
    [Fact]
    public async Task ShouldAddListGetAndUpdateApproval()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 5, 31, 10, 30, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new AgentApprovalRepository(dbContext);
            var approval = new AgentApproval
            {
                ApprovalId = "approval-1",
                RunId = "run-1",
                StepId = "step-1",
                RequestedAction = "weather",
                RiskLevel = "internal_write",
                RequestPayload = "{\"city\":\"Shanghai\"}",
                Decision = "Pending",
                WaitToken = "wait-1",
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            };

            await repository.AddAsync(approval, CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new AgentApprovalRepository(dbContext);
            var listed = await repository.ListByRunIdAsync("run-1", CancellationToken.None);
            var fetched = await repository.GetByRunIdAndStepIdAsync("run-1", "step-1", CancellationToken.None);

            Assert.Single(listed);
            Assert.NotNull(fetched);
            Assert.Equal("Pending", fetched!.Decision);

            fetched = fetched with
            {
                Decision = "Approved",
                ApproverId = "u-1",
                ApproverName = "Alice",
                ApproverRole = "admin",
                Comment = "Approved",
                DecidedAt = now.AddMinutes(1),
                LastModifyTime = now.AddMinutes(1)
            };

            await repository.UpdateAsync(fetched, CancellationToken.None);
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new AgentApprovalRepository(dbContext);
            var updated = await repository.GetByRunIdAndStepIdAsync("run-1", "step-1", CancellationToken.None);
            Assert.NotNull(updated);
            Assert.Equal("Approved", updated!.Decision);
            Assert.Equal("Alice", updated.ApproverName);
        }
    }
}
