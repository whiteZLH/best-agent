using BestAgent.Api.Infrastructure;
using BestAgent.Api.Mappings;
using BestAgent.Application;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAutoMapper(
    _ => { },
    typeof(ApiMappingProfile).Assembly,
    typeof(CreateAgentRunMappingProfile).Assembly);

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapControllers();

app.Run();
