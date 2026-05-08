using System.Text.Json;
using Kanitel.Api;

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

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.MapGet("/api/openapi.json", () => Results.Ok(new
{
    name = "Kanitel Open API",
    version = "0.1.0",
    description = "Open JSON API for board clients, AI managers, and task agents.",
    endpoints = new object[]
    {
        new { method = "GET", path = "/api/bootstrap", purpose = "Load state, provider presets, agent templates, and scheduler info." },
        new { method = "POST", path = "/api/projects", purpose = "Create a project with default columns." },
        new { method = "POST", path = "/api/projects/{projectId}/tasks", purpose = "Create a task card." },
        new { method = "PATCH", path = "/api/tasks/{taskId}", purpose = "Update a task card, assignment, role, priority, or column." },
        new { method = "POST", path = "/api/tasks/{taskId}/comments", purpose = "Add a human/system comment." },
        new { method = "POST", path = "/api/agent/tasks/{taskId}/comments", purpose = "Add a comment as a project-linked agent." },
        new { method = "PATCH", path = "/api/agent/tasks/{taskId}", purpose = "Agent action endpoint: move status, comment, assign/unassign, or set assignment role." },
        new { method = "PATCH", path = "/api/project-agents/{projectAgentId}", purpose = "Change an agent role on a project, for example worker/reviewer/manager." },
        new { method = "PATCH", path = "/api/agent/project-agents/{projectAgentId}", purpose = "Agent action endpoint: change or remove a project-agent role." },
        new { method = "POST", path = "/api/scheduler/tick", purpose = "Force one scheduler scan." }
    }
}));

app.MapGet("/api/bootstrap", async (JsonDataStore store, IConfiguration configuration) =>
{
    var state = await store.SnapshotAsync();
    return Results.Ok(new
    {
        state,
        providerPresets = OpenClaudeCatalog.ProviderPresets,
        agentTemplates = OpenClaudeCatalog.AgentTemplates,
        scheduler = new
        {
            runner = configuration["KANITEL_AGENT_RUNNER"] ?? Environment.GetEnvironmentVariable("KANITEL_AGENT_RUNNER") ?? "docker",
            intervalSeconds = ReadInt(configuration, "KANITEL_AGENT_POLL_INTERVAL_SECONDS", 20)
        }
    });
});

app.MapPost("/api/scheduler/tick", async (AgentScheduler scheduler, CancellationToken cancellationToken) =>
{
    var result = await scheduler.ScanOnceAsync(cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/projects", async (JsonDataStore store, ProjectRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "Project name is required." });
    }

    var created = await store.MutateAsync(state =>
    {
        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? ""
        };
        state.Projects.Add(project);

        var defaults = new[]
        {
            ("Backlog", "#64748b"),
            ("Ready", "#0ea5e9"),
            ("In Progress", "#f59e0b"),
            ("Review", "#8b5cf6"),
            ("Done", "#22c55e")
        };

        for (var i = 0; i < defaults.Length; i++)
        {
            state.Columns.Add(new BoardColumn
            {
                ProjectId = project.Id,
                Name = defaults[i].Item1,
                Color = defaults[i].Item2,
                Position = i
            });
        }

        return project;
    }, cancellationToken);

    return Results.Ok(created);
});

app.MapPatch("/api/projects/{projectId}", async (JsonDataStore store, string projectId, ProjectRequest request, CancellationToken cancellationToken) =>
{
    var updated = await store.MutateAsync(state =>
    {
        var project = state.Projects.FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            project.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            project.Description = request.Description.Trim();
        }

        return project;
    }, cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapPost("/api/projects/{projectId}/columns", async (JsonDataStore store, string projectId, ColumnRequest request, CancellationToken cancellationToken) =>
{
    var created = await store.MutateAsync<object?>(state =>
    {
        if (state.Projects.All(p => p.Id != projectId) || string.IsNullOrWhiteSpace(request.Name))
        {
            return null;
        }

        var nextPosition = state.Columns.Where(c => c.ProjectId == projectId).Select(c => c.Position).DefaultIfEmpty(-1).Max() + 1;
        var column = new BoardColumn
        {
            ProjectId = projectId,
            Name = request.Name.Trim(),
            Color = string.IsNullOrWhiteSpace(request.Color) ? "#3b82f6" : request.Color.Trim(),
            WipLimit = request.WipLimit,
            Position = nextPosition
        };
        state.Columns.Add(column);
        return column;
    }, cancellationToken);

    return created is null ? Results.BadRequest() : Results.Ok(created);
});

app.MapPatch("/api/columns/{columnId}", async (JsonDataStore store, string columnId, ColumnRequest request, CancellationToken cancellationToken) =>
{
    var updated = await store.MutateAsync(state =>
    {
        var column = state.Columns.FirstOrDefault(c => c.Id == columnId);
        if (column is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            column.Name = request.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Color))
        {
            column.Color = request.Color.Trim();
        }

        if (request.WipLimitSet)
        {
            column.WipLimit = request.WipLimit;
        }

        if (request.Position.HasValue)
        {
            column.Position = request.Position.Value;
            NormalizeColumnPositions(state, column.ProjectId);
        }

        return column;
    }, cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/api/columns/{columnId}", async (JsonDataStore store, string columnId, CancellationToken cancellationToken) =>
{
    var deleted = await store.MutateAsync(state =>
    {
        var column = state.Columns.FirstOrDefault(c => c.Id == columnId);
        if (column is null)
        {
            return false;
        }

        var fallback = state.Columns
            .Where(c => c.ProjectId == column.ProjectId && c.Id != column.Id)
            .OrderBy(c => c.Position)
            .FirstOrDefault();
        if (fallback is null)
        {
            return false;
        }

        foreach (var task in state.Tasks.Where(t => t.ColumnId == column.Id))
        {
            task.ColumnId = fallback.Id;
            task.UpdatedAt = DateTimeOffset.UtcNow;
        }

        state.Columns.Remove(column);
        NormalizeColumnPositions(state, column.ProjectId);
        return true;
    }, cancellationToken);

    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/people", async (JsonDataStore store, PersonRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName))
    {
        return Results.BadRequest(new { error = "Display name is required." });
    }

    var person = await store.MutateAsync(state =>
    {
        var created = new Person
        {
            DisplayName = request.DisplayName.Trim(),
            Email = request.Email?.Trim() ?? "",
            AvatarUrl = request.AvatarUrl?.Trim() ?? ""
        };
        state.People.Add(created);
        return created;
    }, cancellationToken);

    return Results.Ok(person);
});

app.MapPost("/api/projects/{projectId}/members", async (JsonDataStore store, string projectId, MemberRequest request, CancellationToken cancellationToken) =>
{
    var result = await store.MutateAsync<object?>(state =>
    {
        if (state.Projects.All(p => p.Id != projectId))
        {
            return null;
        }

        var person = !string.IsNullOrWhiteSpace(request.PersonId)
            ? state.People.FirstOrDefault(p => p.Id == request.PersonId)
            : state.People.FirstOrDefault(p => !string.IsNullOrWhiteSpace(request.Email) && string.Equals(p.Email, request.Email, StringComparison.OrdinalIgnoreCase));

        if (person is null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return null;
            }

            person = new Person
            {
                DisplayName = request.DisplayName.Trim(),
                Email = request.Email?.Trim() ?? "",
                AvatarUrl = request.AvatarUrl?.Trim() ?? ""
            };
            state.People.Add(person);
        }

        var existing = state.Members.FirstOrDefault(m => m.ProjectId == projectId && m.PersonId == person.Id);
        if (request.AvatarUrl is not null)
        {
            person.AvatarUrl = request.AvatarUrl.Trim();
        }

        if (existing is not null)
        {
            existing.Role = string.IsNullOrWhiteSpace(request.Role) ? existing.Role : request.Role.Trim();
            return new { person, member = existing };
        }

        var member = new ProjectMember
        {
            ProjectId = projectId,
            PersonId = person.Id,
            Role = string.IsNullOrWhiteSpace(request.Role) ? "viewer" : request.Role.Trim()
        };
        state.Members.Add(member);
        return new { person, member };
    }, cancellationToken);

    return result is null ? Results.BadRequest() : Results.Ok(result);
});

app.MapDelete("/api/members/{memberId}", async (JsonDataStore store, string memberId, CancellationToken cancellationToken) =>
{
    var removed = await store.MutateAsync(state =>
    {
        var member = state.Members.FirstOrDefault(m => m.Id == memberId);
        if (member is null)
        {
            return false;
        }

        state.Members.Remove(member);
        return true;
    }, cancellationToken);

    return removed ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/projects/{projectId}/repositories", async (JsonDataStore store, string projectId, RepositoryRequest request, CancellationToken cancellationToken) =>
{
    var created = await store.MutateAsync<object?>(state =>
    {
        if (state.Projects.All(p => p.Id != projectId) || string.IsNullOrWhiteSpace(request.Url))
        {
            return null;
        }

        var repo = new RepositoryLink
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.Url.Trim() : request.Name.Trim(),
            Url = request.Url.Trim(),
            Branch = request.Branch?.Trim() ?? "",
            AuthMode = request.AuthMode?.Trim() ?? InferAuthMode(request.Url)
        };
        state.Repositories.Add(repo);
        return repo;
    }, cancellationToken);

    return created is null ? Results.BadRequest() : Results.Ok(created);
});

app.MapPatch("/api/repositories/{repoId}", async (JsonDataStore store, string repoId, RepositoryRequest request, CancellationToken cancellationToken) =>
{
    var updated = await store.MutateAsync(state =>
    {
        var repo = state.Repositories.FirstOrDefault(r => r.Id == repoId);
        if (repo is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Name)) repo.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.Url)) repo.Url = request.Url.Trim();
        if (request.Branch is not null) repo.Branch = request.Branch.Trim();
        if (!string.IsNullOrWhiteSpace(request.AuthMode)) repo.AuthMode = request.AuthMode.Trim();
        return repo;
    }, cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/api/repositories/{repoId}", async (JsonDataStore store, string repoId, CancellationToken cancellationToken) =>
{
    var removed = await store.MutateAsync(state =>
    {
        var repo = state.Repositories.FirstOrDefault(r => r.Id == repoId);
        if (repo is null)
        {
            return false;
        }

        state.Repositories.Remove(repo);
        return true;
    }, cancellationToken);

    return removed ? Results.NoContent() : Results.NotFound();
});

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
            CommandTemplate = string.IsNullOrWhiteSpace(request.CommandTemplate)
                ? "npx -y @gitlawb/openclaude@latest --print \"$(cat \\\"$KANITEL_TASK_PROMPT_FILE\\\")\""
                : request.CommandTemplate.Trim(),
            SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? template.SystemPrompt : request.SystemPrompt.Trim(),
            Enabled = request.Enabled ?? true,
            ToolTags = template.ToolTags.ToList(),
            Environment = MergeEnvironment(preset.Environment, request.Environment)
        };
        state.Agents.Add(agent);
        return agent;
    }, cancellationToken);

    return created is null ? Results.BadRequest() : Results.Ok(created);
});

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
        if (!string.IsNullOrWhiteSpace(request.CommandTemplate)) agent.CommandTemplate = request.CommandTemplate.Trim();
        if (request.SystemPrompt is not null) agent.SystemPrompt = request.SystemPrompt.Trim();
        if (request.Enabled.HasValue) agent.Enabled = request.Enabled.Value;
        if (request.Environment is not null) agent.Environment = MergeEnvironment(agent.Environment, request.Environment);
        return agent;
    }, cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapPost("/api/projects/{projectId}/agents", async (JsonDataStore store, string projectId, ProjectAgentRequest request, CancellationToken cancellationToken) =>
{
    var linked = await store.MutateAsync<object?>(state =>
    {
        if (state.Projects.All(p => p.Id != projectId) || state.Agents.All(a => a.Id != request.AgentId))
        {
            return null;
        }

        var existing = state.ProjectAgents.FirstOrDefault(pa => pa.ProjectId == projectId && pa.AgentId == request.AgentId);
        if (existing is not null)
        {
            existing.Role = string.IsNullOrWhiteSpace(request.Role) ? existing.Role : request.Role.Trim();
            return existing;
        }

        var projectAgent = new ProjectAgent
        {
            ProjectId = projectId,
            AgentId = request.AgentId.Trim(),
            Role = string.IsNullOrWhiteSpace(request.Role) ? "worker" : request.Role.Trim()
        };
        state.ProjectAgents.Add(projectAgent);
        return projectAgent;
    }, cancellationToken);

    return linked is null ? Results.BadRequest() : Results.Ok(linked);
});

app.MapPatch("/api/project-agents/{projectAgentId}", async (JsonDataStore store, string projectAgentId, ProjectAgentPatchRequest request, CancellationToken cancellationToken) =>
{
    var updated = await store.MutateAsync(state =>
    {
        var projectAgent = state.ProjectAgents.FirstOrDefault(pa => pa.Id == projectAgentId);
        if (projectAgent is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            projectAgent.Role = request.Role.Trim();
        }

        return projectAgent;
    }, cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/api/project-agents/{projectAgentId}", async (JsonDataStore store, string projectAgentId, CancellationToken cancellationToken) =>
{
    var removed = await store.MutateAsync(state =>
    {
        var projectAgent = state.ProjectAgents.FirstOrDefault(pa => pa.Id == projectAgentId);
        if (projectAgent is null)
        {
            return false;
        }

        state.ProjectAgents.Remove(projectAgent);
        return true;
    }, cancellationToken);

    return removed ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/projects/{projectId}/tasks", async (JsonDataStore store, string projectId, TaskRequest request, CancellationToken cancellationToken) =>
{
    var created = await store.MutateAsync<object?>(state =>
    {
        if (state.Projects.All(p => p.Id != projectId) || string.IsNullOrWhiteSpace(request.Title))
        {
            return null;
        }

        var columnId = !string.IsNullOrWhiteSpace(request.ColumnId)
            ? request.ColumnId
            : state.Columns.Where(c => c.ProjectId == projectId).OrderBy(c => c.Position).FirstOrDefault()?.Id;
        if (columnId is null || state.Columns.All(c => c.Id != columnId || c.ProjectId != projectId))
        {
            return null;
        }

        var task = new TaskCard
        {
            ProjectId = projectId,
            ColumnId = columnId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? "",
            AssigneeAgentId = BlankToNull(request.AssigneeAgentId),
            AssigneePersonId = BlankToNull(request.AssigneePersonId),
            AssignmentRole = string.IsNullOrWhiteSpace(request.AssignmentRole) ? "worker" : request.AssignmentRole.Trim(),
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority.Trim(),
            Position = state.Tasks.Where(t => t.ProjectId == projectId && t.ColumnId == columnId).Select(t => t.Position).DefaultIfEmpty(-1).Max() + 1
        };
        state.Tasks.Add(task);
        state.Comments.Add(new TaskComment
        {
            TaskId = task.Id,
            AuthorType = "person",
            AuthorId = BlankToNull(request.AuthorId) ?? "owner",
            Body = string.IsNullOrWhiteSpace(request.InitialComment)
                ? "Task created."
                : request.InitialComment.Trim()
        });
        return task;
    }, cancellationToken);

    return created is null ? Results.BadRequest() : Results.Ok(created);
});

app.MapPatch("/api/tasks/{taskId}", async (JsonDataStore store, string taskId, TaskRequest request, CancellationToken cancellationToken) =>
{
    var updated = await store.MutateAsync(state =>
    {
        var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Title)) task.Title = request.Title.Trim();
        if (request.Description is not null) task.Description = request.Description.Trim();
        if (!string.IsNullOrWhiteSpace(request.ColumnId) && state.Columns.Any(c => c.Id == request.ColumnId && c.ProjectId == task.ProjectId))
        {
            task.ColumnId = request.ColumnId;
        }
        if (request.AssigneeAgentId is not null) task.AssigneeAgentId = BlankToNull(request.AssigneeAgentId);
        if (request.AssigneePersonId is not null) task.AssigneePersonId = BlankToNull(request.AssigneePersonId);
        if (request.AssignmentRole is not null) task.AssignmentRole = string.IsNullOrWhiteSpace(request.AssignmentRole) ? "worker" : request.AssignmentRole.Trim();
        if (!string.IsNullOrWhiteSpace(request.Priority)) task.Priority = request.Priority.Trim();
        if (request.Position.HasValue) task.Position = request.Position.Value;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        return task;
    }, cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapPost("/api/tasks/{taskId}/comments", async (JsonDataStore store, string taskId, CommentRequest request, CancellationToken cancellationToken) =>
{
    var created = await store.MutateAsync<object?>(state =>
    {
        var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null || string.IsNullOrWhiteSpace(request.Body))
        {
            return null;
        }

        var comment = new TaskComment
        {
            TaskId = taskId,
            AuthorType = string.IsNullOrWhiteSpace(request.AuthorType) ? "person" : request.AuthorType.Trim(),
            AuthorId = string.IsNullOrWhiteSpace(request.AuthorId) ? "owner" : request.AuthorId.Trim(),
            Body = request.Body.Trim()
        };
        state.Comments.Add(comment);
        task.UpdatedAt = DateTimeOffset.UtcNow;
        return comment;
    }, cancellationToken);

    return created is null ? Results.BadRequest() : Results.Ok(created);
});

app.MapPost("/api/agent/tasks/{taskId}/comments", async (JsonDataStore store, string taskId, AgentCommentRequest request, CancellationToken cancellationToken) =>
{
    var created = await store.MutateAsync<object?>(state =>
    {
        var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null ||
            string.IsNullOrWhiteSpace(request.AgentId) ||
            string.IsNullOrWhiteSpace(request.Body) ||
            !CanAgentActOnProject(state, task.ProjectId, request.AgentId))
        {
            return null;
        }

        var comment = new TaskComment
        {
            TaskId = taskId,
            AuthorType = "agent",
            AuthorId = request.AgentId.Trim(),
            Body = request.Body.Trim()
        };
        state.Comments.Add(comment);
        task.UpdatedAt = DateTimeOffset.UtcNow;
        return comment;
    }, cancellationToken);

    return created is null ? Results.BadRequest(new { error = "Agent cannot comment on this task." }) : Results.Ok(created);
});

app.MapPatch("/api/agent/tasks/{taskId}", async (JsonDataStore store, string taskId, AgentTaskActionRequest request, CancellationToken cancellationToken) =>
{
    var result = await store.MutateAsync<object?>(state =>
    {
        var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null ||
            string.IsNullOrWhiteSpace(request.AgentId) ||
            !CanAgentActOnProject(state, task.ProjectId, request.AgentId))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.ColumnId) &&
            state.Columns.Any(c => c.Id == request.ColumnId && c.ProjectId == task.ProjectId))
        {
            task.ColumnId = request.ColumnId.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(request.ColumnName))
        {
            var column = state.Columns.FirstOrDefault(c =>
                c.ProjectId == task.ProjectId &&
                string.Equals(c.Name, request.ColumnName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (column is not null)
            {
                task.ColumnId = column.Id;
            }
        }

        if (request.UnassignAgent == true)
        {
            task.AssigneeAgentId = null;
            task.AssignmentRole = "worker";
        }
        else if (request.AssigneeAgentId is not null)
        {
            var assigneeAgentId = BlankToNull(request.AssigneeAgentId);
            if (assigneeAgentId is null ||
                CanAgentActOnProject(state, task.ProjectId, assigneeAgentId))
            {
                task.AssigneeAgentId = assigneeAgentId;
            }
        }

        if (request.AssignmentRole is not null)
        {
            task.AssignmentRole = string.IsNullOrWhiteSpace(request.AssignmentRole)
                ? "worker"
                : request.AssignmentRole.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            task.Priority = request.Priority.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            task.Title = request.Title.Trim();
        }

        if (request.Description is not null)
        {
            task.Description = request.Description.Trim();
        }

        TaskComment? comment = null;
        if (!string.IsNullOrWhiteSpace(request.Body))
        {
            comment = new TaskComment
            {
                TaskId = task.Id,
                AuthorType = "agent",
                AuthorId = request.AgentId.Trim(),
                Body = request.Body.Trim()
            };
            state.Comments.Add(comment);
        }

        task.UpdatedAt = DateTimeOffset.UtcNow;
        return new { task, comment };
    }, cancellationToken);

    return result is null ? Results.BadRequest(new { error = "Agent cannot update this task." }) : Results.Ok(result);
});

app.MapPatch("/api/agent/project-agents/{projectAgentId}", async (JsonDataStore store, string projectAgentId, AgentProjectAgentActionRequest request, CancellationToken cancellationToken) =>
{
    var result = await store.MutateAsync<object?>(state =>
    {
        var projectAgent = state.ProjectAgents.FirstOrDefault(pa => pa.Id == projectAgentId);
        if (projectAgent is null ||
            string.IsNullOrWhiteSpace(request.AgentId) ||
            !CanAgentActOnProject(state, projectAgent.ProjectId, request.AgentId))
        {
            return null;
        }

        if (request.Remove == true)
        {
            state.ProjectAgents.Remove(projectAgent);
            return new { removed = true, projectAgentId };
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            projectAgent.Role = request.Role.Trim();
        }

        return new { removed = false, projectAgent };
    }, cancellationToken);

    return result is null ? Results.BadRequest(new { error = "Agent cannot update this project role." }) : Results.Ok(result);
});

var indexPath = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
if (File.Exists(indexPath))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

static int ReadInt(IConfiguration configuration, string key, int fallback)
{
    var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}

static void NormalizeColumnPositions(KanitelState state, string projectId)
{
    var ordered = state.Columns
        .Where(c => c.ProjectId == projectId)
        .OrderBy(c => c.Position)
        .ThenBy(c => c.Name)
        .ToList();

    for (var i = 0; i < ordered.Count; i++)
    {
        ordered[i].Position = i;
    }
}

static string InferAuthMode(string url)
{
    return url.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
        ? "ssh"
        : "http";
}

static Dictionary<string, string> MergeEnvironment(
    Dictionary<string, string> first,
    Dictionary<string, string>? second)
{
    var merged = new Dictionary<string, string>(first, StringComparer.OrdinalIgnoreCase);
    if (second is null)
    {
        return merged;
    }

    foreach (var pair in second)
    {
        if (!string.IsNullOrWhiteSpace(pair.Key))
        {
            merged[pair.Key.Trim()] = pair.Value;
        }
    }

    return merged;
}

static string? BlankToNull(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

static bool CanAgentActOnProject(KanitelState state, string projectId, string agentId)
{
    var normalizedAgentId = agentId.Trim();
    return state.Agents.Any(agent => agent.Id == normalizedAgentId && agent.Enabled) &&
        state.ProjectAgents.Any(projectAgent =>
            projectAgent.ProjectId == projectId &&
            projectAgent.AgentId == normalizedAgentId);
}

public sealed record ProjectRequest(string Name, string? Description);

public sealed record ColumnRequest(
    string? Name,
    string? Color,
    int? Position,
    int? WipLimit,
    bool WipLimitSet);

public sealed record PersonRequest(string DisplayName, string? Email, string? AvatarUrl);

public sealed record MemberRequest(
    string? PersonId,
    string? DisplayName,
    string? Email,
    string? AvatarUrl,
    string? Role);

public sealed record RepositoryRequest(
    string? Name,
    string Url,
    string? Branch,
    string? AuthMode);

public sealed record AgentRequest(
    string? Name,
    string? TemplateId,
    string? AgentType,
    string? AvatarUrl,
    string? ProviderPresetId,
    string? BaseUrl,
    string? Model,
    string? ApiKeyEnvName,
    string? ContainerImage,
    string? CommandTemplate,
    string? SystemPrompt,
    bool? Enabled,
    Dictionary<string, string>? Environment);

public sealed record ProjectAgentRequest(string AgentId, string? Role);

public sealed record ProjectAgentPatchRequest(string? Role);

public sealed record TaskRequest(
    string? Title,
    string? Description,
    string? ColumnId,
    string? AssigneeAgentId,
    string? AssigneePersonId,
    string? AssignmentRole,
    string? Priority,
    int? Position,
    string? InitialComment,
    string? AuthorId);

public sealed record CommentRequest(
    string Body,
    string? AuthorType,
    string? AuthorId);

public sealed record AgentCommentRequest(
    string AgentId,
    string Body);

public sealed record AgentTaskActionRequest(
    string AgentId,
    string? Body,
    string? ColumnId,
    string? ColumnName,
    string? AssigneeAgentId,
    string? AssignmentRole,
    bool? UnassignAgent,
    string? Priority,
    string? Title,
    string? Description);

public sealed record AgentProjectAgentActionRequest(
    string AgentId,
    string? Role,
    bool? Remove);
