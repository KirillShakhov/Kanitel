namespace Kanitel.Api;

using static Kanitel.Api.EndpointHelpers;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/agents", async (JsonDataStore store, AgentRequest request, CancellationToken cancellationToken) =>
        {
            var created = await store.MutateAsync<object?>(state =>
            {
                var preset = OpenClaudeCatalog.ProviderPresets.FirstOrDefault(p => p.Id == request.ProviderPresetId)
                    ?? OpenClaudeCatalog.ProviderPresets.First(p => p.Id == "codex");
                var template = OpenClaudeCatalog.AgentTemplates.FirstOrDefault(t => t.Id == request.TemplateId)
                    ?? OpenClaudeCatalog.AgentTemplates.First(t => t.Id == "general-purpose");

                var agent = new AgentProfile
                {
                    Name = string.IsNullOrWhiteSpace(request.Name) ? template.Name : request.Name.Trim(),
                    AgentType = string.IsNullOrWhiteSpace(request.AgentType) ? template.AgentType : request.AgentType.Trim(),
                    AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? preset.LogoUrl : request.AvatarUrl.Trim(),
                    ProviderPresetId = preset.Id,
                    Provider = preset.Provider,
                    BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? preset.BaseUrl : request.BaseUrl.Trim(),
                    Model = string.IsNullOrWhiteSpace(request.Model) ? preset.DefaultModel : request.Model.Trim(),
                    ApiKeyEnvName = string.IsNullOrWhiteSpace(request.ApiKeyEnvName) ? preset.ApiKeyEnvName : request.ApiKeyEnvName.Trim(),
                    ContainerImage = string.IsNullOrWhiteSpace(request.ContainerImage) ? "node:22-bookworm" : request.ContainerImage.Trim(),
                    CommandTemplate = AgentCommandDefaults.Normalize(request.CommandTemplate),
                    SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? template.SystemPrompt : request.SystemPrompt.Trim(),
                    Enabled = request.Enabled ?? true,
                    ToolTags = template.ToolTags.ToList(),
                    Environment = MergeEnvironment(preset.Environment, request.Environment)
                };
                state.Agents.Add(agent);
                return agent;
            }, cancellationToken);

            return created is null ? Results.BadRequest() : Results.Ok(created);
        })
            .WithTags("Agents")
            .WithSummary("Create global agent")
            .WithDescription("Creates a global AI agent profile from an OpenClaude provider preset and template. Agents are not project-specific until added to a project as participants.");

        app.MapPatch("/api/agents/{agentId}", async (JsonDataStore store, string agentId, AgentRequest request, CancellationToken cancellationToken) =>
        {
            var updated = await store.MutateAsync(state =>
            {
                var agent = state.Agents.FirstOrDefault(a => a.Id == agentId);
                if (agent is null)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(request.Name)) agent.Name = request.Name.Trim();
                if (!string.IsNullOrWhiteSpace(request.AgentType)) agent.AgentType = request.AgentType.Trim();
                if (request.AvatarUrl is not null) agent.AvatarUrl = request.AvatarUrl.Trim();
                if (!string.IsNullOrWhiteSpace(request.ProviderPresetId))
                {
                    var preset = OpenClaudeCatalog.ProviderPresets.FirstOrDefault(p => p.Id == request.ProviderPresetId);
                    if (preset is not null)
                    {
                        agent.ProviderPresetId = preset.Id;
                        agent.Provider = preset.Provider;
                        agent.BaseUrl = preset.BaseUrl;
                        agent.Model = preset.DefaultModel;
                        agent.ApiKeyEnvName = preset.ApiKeyEnvName;
                        if (string.IsNullOrWhiteSpace(agent.AvatarUrl))
                        {
                            agent.AvatarUrl = preset.LogoUrl;
                        }
                        agent.Environment = MergeEnvironment(preset.Environment, request.Environment);
                    }
                }

                if (!string.IsNullOrWhiteSpace(request.BaseUrl)) agent.BaseUrl = request.BaseUrl.Trim();
                if (!string.IsNullOrWhiteSpace(request.Model)) agent.Model = request.Model.Trim();
                if (request.ApiKeyEnvName is not null) agent.ApiKeyEnvName = request.ApiKeyEnvName.Trim();
                if (!string.IsNullOrWhiteSpace(request.ContainerImage)) agent.ContainerImage = request.ContainerImage.Trim();
                if (!string.IsNullOrWhiteSpace(request.CommandTemplate)) agent.CommandTemplate = AgentCommandDefaults.Normalize(request.CommandTemplate);
                if (request.SystemPrompt is not null) agent.SystemPrompt = request.SystemPrompt.Trim();
                if (request.Enabled.HasValue) agent.Enabled = request.Enabled.Value;
                if (request.Environment is not null) agent.Environment = MergeEnvironment(agent.Environment, request.Environment);
                return agent;
            }, cancellationToken);

            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
            .WithTags("Agents")
            .WithSummary("Update global agent")
            .WithDescription("Updates an agent profile, provider configuration, model, avatar/logo, Docker image, command template, system prompt, enabled flag, or environment values.");

        app.MapDelete("/api/agents/{agentId}", async (JsonDataStore store, string agentId, CancellationToken cancellationToken) =>
        {
            var removed = await store.MutateAsync(state =>
            {
                var agent = state.Agents.FirstOrDefault(a => a.Id == agentId);
                if (agent is null)
                {
                    return false;
                }

                state.Agents.Remove(agent);
                state.ProjectAgents.RemoveAll(projectAgent => projectAgent.AgentId == agentId);

                foreach (var task in state.Tasks.Where(task => task.AssigneeAgentId == agentId))
                {
                    task.AssigneeAgentId = null;
                    task.UpdatedAt = DateTimeOffset.UtcNow;
                }

                return true;
            }, cancellationToken);

            return removed ? Results.NoContent() : Results.NotFound();
        })
            .WithTags("Agents")
            .WithSummary("Delete global agent")
            .WithDescription("Deletes a global agent profile, removes it from all projects, and clears task assignments to that agent. Historical comments and runs are kept for audit context.");

        return app;
    }
}
