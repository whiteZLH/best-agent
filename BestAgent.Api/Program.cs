using BestAgent.Api.Mappings;
using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAutoMapper(
    typeof(ApiMappingProfile).Assembly,
    typeof(CreateAgentRunMappingProfile).Assembly);

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
