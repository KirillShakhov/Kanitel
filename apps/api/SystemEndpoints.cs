namespace Kanitel.Api;

using static Kanitel.Api.EndpointHelpers;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }))
            .WithTags("System")
            .WithSummary("Health check")
            .WithDescription("Returns the current API status and server timestamp. Use this from Docker, reverse proxies, or local scripts to verify that the API is alive.");

        app.MapGet("/api/openapi.json", () => Results.Ok(new
        {
            name = "Kanitel Open API",
            version = "0.1.0",
            description = "Open JSON API for board clients, AI managers, and task agents.",
            endpoints = new object[]
            {
                new { method = "GET", path = "/api/bootstrap", purpose = "Load state, provider presets, agent templates, and scheduler info." },
                new { method = "POST", path = "/api/auth/register", purpose = "Register a local user with matching password confirmation and return an API token." },
                new { method = "POST", path = "/api/auth/login", purpose = "Login and return an API token." },
                new { method = "GET", path = "/api/auth/me", purpose = "Read the current user profile from the bearer token." },
                new { method = "PATCH", path = "/api/auth/me", purpose = "Update the current user profile; password changes require the current password and matching confirmation." },
                new { method = "POST", path = "/api/projects", purpose = "Create a project with default columns." },
                new { method = "POST", path = "/api/projects/{projectId}/tasks", purpose = "Create a task card." },
                new { method = "PATCH", path = "/api/tasks/{taskId}", purpose = "Update a task card, assignment, or column." },
                new { method = "POST", path = "/api/tasks/{taskId}/comments", purpose = "Add a human/system comment." },
                new { method = "POST", path = "/api/agent/tasks/{taskId}/comments", purpose = "Add a comment as a project-linked agent." },
                new { method = "PATCH", path = "/api/agent/tasks/{taskId}", purpose = "Agent action endpoint: move status, comment, assign, or unassign." },
                new { method = "DELETE", path = "/api/agents/{agentId}", purpose = "Delete a global agent and clear project/task links." },
                new { method = "POST", path = "/api/scheduler/tick", purpose = "Force one scheduler scan." }
            }
        }))
            .WithTags("System")
            .WithSummary("Legacy lightweight API index")
            .WithDescription("Returns a compact hand-written endpoint index kept for simple AI manager discovery. The full Swagger/OpenAPI document is available at `/swagger/v1/swagger.json` and the UI at `/swagger`.");

        app.MapGet("/api/bootstrap", async (JsonDataStore store, IConfiguration configuration, HttpRequest httpRequest) =>
        {
            var state = await store.SnapshotAsync();
            var currentUser = FindCurrentUser(state, httpRequest);
            var clientState = SanitizeForClient(state);
            return Results.Ok(new
            {
                state = clientState,
                currentUser,
                providerPresets = OpenClaudeCatalog.ProviderPresets,
                agentTemplates = OpenClaudeCatalog.AgentTemplates,
                scheduler = new
                {
                    runner = configuration["KANITEL_AGENT_RUNNER"] ?? Environment.GetEnvironmentVariable("KANITEL_AGENT_RUNNER") ?? "docker",
                    intervalSeconds = ReadInt(configuration, "KANITEL_AGENT_POLL_INTERVAL_SECONDS", 20)
                }
            });
        })
            .WithTags("System")
            .WithSummary("Load application bootstrap")
            .WithDescription("Returns the sanitized board state, current user if a bearer token is provided, provider presets copied from OpenClaude, agent templates, and scheduler settings. Account password hashes are never returned.");

        app.MapPost("/api/scheduler/tick", async (AgentScheduler scheduler, CancellationToken cancellationToken) =>
        {
            var result = await scheduler.ScanOnceAsync(cancellationToken);
            return Results.Ok(result);
        })
            .WithTags("Scheduler")
            .WithSummary("Run one scheduler scan")
            .WithDescription("Forces an immediate scan for tasks that need agent action. The scheduler queues a run only when the assignee is an enabled project agent and the latest task comment is not from that agent or from the system.");

        return app;
    }
}
