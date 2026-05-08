using System.Reflection;
using System.Text.Json;
using Kanitel.Api;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddSingleton<JsonDataStore>();
builder.Services.AddSingleton<IAgentRunner, DockerAgentRunner>();
builder.Services.AddSingleton<AgentScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentScheduler>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Kanitel API",
        Version = "v1",
        Description = """
        Kanitel API for the kanban board, projects, participants, AI agents, and external AI managers.

        Authentication is local token-based auth. Register or login, copy the returned `token`,
        then click Authorize and paste the token value. Swagger sends it as `Authorization: Bearer <token>`.

        Task automation rule: the scheduler starts an enabled project agent only when the task is
        assigned to that agent and the latest task comment is not from that same agent or the system.
        """,
        Contact = new OpenApiContact
        {
            Name = "Kanitel local workspace"
        }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "opaque-token",
        Description = "Paste the raw token returned by login/register. Swagger sends it as `Authorization: Bearer <token>`."
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "Kanitel API Docs";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Kanitel API v1");
    options.RoutePrefix = "swagger";
    options.DisplayRequestDuration();
    options.EnableTryItOutByDefault();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSystemEndpoints();
app.MapAuthEndpoints();
app.MapProjectEndpoints();
app.MapParticipantEndpoints();
app.MapRepositoryEndpoints();
app.MapAgentEndpoints();
app.MapTaskEndpoints();

var indexPath = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
if (File.Exists(indexPath))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
