using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BestAgent.Infrastructure.Persistence;

public sealed class BestAgentDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BestAgentDbContext>
{
    public BestAgentDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BestAgentDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=best_agent;Username=postgres;Password=postgres");
        return new BestAgentDbContext(optionsBuilder.Options);
    }
}
