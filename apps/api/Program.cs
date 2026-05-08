using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
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
        new { method = "POST", path = "/api/auth/register", purpose = "Register a local user and return an API token." },
        new { method = "POST", path = "/api/auth/login", purpose = "Login and return an API token." },
        new { method = "GET", path = "/api/auth/me", purpose = "Read the current user profile from the bearer token." },
        new { method = "PATCH", path = "/api/auth/me", purpose = "Update the current user profile." },
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

app.MapPost("/api/auth/register", async (JsonDataStore store, AuthRegisterRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName) ||
        string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { error = "Display name, email, and password are required." });
    }

    if (request.Password.Length < 6)
    {
        return Results.BadRequest(new { error = "Password must be at least 6 characters." });
    }

    var result = await store.MutateAsync<object?>(state =>
    {
        var email = NormalizeEmail(request.Email);
        var existingAccount = state.Accounts.FirstOrDefault(account =>
            string.Equals(account.Email, email, StringComparison.OrdinalIgnoreCase));
        if (existingAccount is not null)
        {
            return null;
        }

        var person = state.People.FirstOrDefault(item =>
            string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
        if (person is null)
        {
            person = new Person
            {
                DisplayName = request.DisplayName.Trim(),
                Email = email,
                AvatarUrl = request.AvatarUrl?.Trim() ?? ""
            };
            state.People.Add(person);
        }
        else
        {
            person.DisplayName = request.DisplayName.Trim();
            person.Email = email;
            if (request.AvatarUrl is not null)
            {
                person.AvatarUrl = request.AvatarUrl.Trim();
            }
        }

        var salt = NewToken();
        var account = new UserAccount
        {
            PersonId = person.Id,
            Email = email,
            PasswordSalt = salt,
            PasswordHash = HashPassword(request.Password, salt),
            SessionToken = NewToken(),
            LastLoginAt = DateTimeOffset.UtcNow
        };
        state.Accounts.Add(account);

        if (state.Accounts.Count == 1 && state.Projects.Count > 0)
        {
            var projectId = state.Projects[0].Id;
            if (state.Members.All(member => member.ProjectId != projectId || member.PersonId != person.Id))
            {
                state.Members.Add(new ProjectMember
                {
                    ProjectId = projectId,
                    PersonId = person.Id,
                    Role = "owner"
                });
            }
        }

        return new AuthResponse(account.SessionToken, person);
    }, cancellationToken);

    return result is null ? Results.Conflict(new { error = "Account already exists." }) : Results.Ok(result);
})
    .WithTags("Auth")
    .WithSummary("Register a local user")
    .WithDescription("Creates a local account, creates or updates the matching person profile, returns an auth token, and grants the first registered account owner access to the seed project.");

app.MapPost("/api/auth/login", async (JsonDataStore store, AuthLoginRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { error = "Email and password are required." });
    }

    var result = await store.MutateAsync<object?>(state =>
    {
        var email = NormalizeEmail(request.Email);
        var account = state.Accounts.FirstOrDefault(item =>
            string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
        if (account is null || !VerifyPassword(request.Password, account.PasswordSalt, account.PasswordHash))
        {
            return null;
        }

        account.SessionToken = NewToken();
        account.LastLoginAt = DateTimeOffset.UtcNow;
        var person = state.People.FirstOrDefault(item => item.Id == account.PersonId);
        return person is null ? null : new AuthResponse(account.SessionToken, person);
    }, cancellationToken);

    return result is null ? Results.Unauthorized() : Results.Ok(result);
})
    .WithTags("Auth")
    .WithSummary("Login")
    .WithDescription("Validates email and password, rotates the session token, and returns the token with the linked person profile. Use the returned token as `Bearer <token>` in Swagger Authorize.");

app.MapGet("/api/auth/me", async (JsonDataStore store, HttpRequest httpRequest, CancellationToken cancellationToken) =>
{
    var state = await store.SnapshotAsync(cancellationToken);
    var currentUser = FindCurrentUser(state, httpRequest);
    return currentUser is null ? Results.Unauthorized() : Results.Ok(currentUser);
})
    .WithTags("Auth")
    .WithSummary("Read current profile")
    .WithDescription("Reads the current person profile from the bearer token or `X-Kanitel-Token` header. Returns 401 when the token is missing or invalid.");

app.MapPatch("/api/auth/me", async (JsonDataStore store, HttpRequest httpRequest, ProfileRequest request, CancellationToken cancellationToken) =>
{
    var token = ReadBearerToken(httpRequest);
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.Unauthorized();
    }

    var result = await store.MutateAsync<object?>(state =>
    {
        var account = state.Accounts.FirstOrDefault(item => item.SessionToken == token);
        if (account is null)
        {
            return null;
        }

        var person = state.People.FirstOrDefault(item => item.Id == account.PersonId);
        if (person is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = NormalizeEmail(request.Email);
            var occupied = state.Accounts.Any(item =>
                item.Id != account.Id &&
                string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
            if (occupied)
            {
                return null;
            }

            person.Email = email;
            account.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            person.DisplayName = request.DisplayName.Trim();
        }

        if (request.AvatarUrl is not null)
        {
            person.AvatarUrl = request.AvatarUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password.Length < 6)
            {
                return null;
            }

            var salt = NewToken();
            account.PasswordSalt = salt;
            account.PasswordHash = HashPassword(request.Password, salt);
        }

        return new AuthResponse(account.SessionToken, person);
    }, cancellationToken);

    return result is null ? Results.BadRequest(new { error = "Profile could not be updated." }) : Results.Ok(result);
})
    .WithTags("Auth")
    .WithSummary("Update current profile")
    .WithDescription("Updates the authenticated user's display name, email, avatar URL, or password. Password updates require at least six characters; blank password leaves it unchanged.");

app.MapPost("/api/scheduler/tick", async (AgentScheduler scheduler, CancellationToken cancellationToken) =>
{
    var result = await scheduler.ScanOnceAsync(cancellationToken);
    return Results.Ok(result);
})
    .WithTags("Scheduler")
    .WithSummary("Run one scheduler scan")
    .WithDescription("Forces an immediate scan for tasks that need agent action. The scheduler queues a run only when the assignee is an enabled project agent and the latest task comment is not from that agent or from the system.");

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
})
    .WithTags("Projects")
    .WithSummary("Create project")
    .WithDescription("Creates a project with the default kanban workflow columns: Backlog, Ready, In Progress, Review, and Done. Add people and agents as participants through the participant endpoints.");

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
})
    .WithTags("Projects")
    .WithSummary("Update project")
    .WithDescription("Updates a project's name and description. Columns, repositories, people, and agents are intentionally managed through separate endpoints.");

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
})
    .WithTags("Board")
    .WithSummary("Create board column")
    .WithDescription("Adds a configurable kanban column to a project. Columns are ordered by position; omitted position appends the new column to the end.");

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
})
    .WithTags("Board")
    .WithSummary("Update board column")
    .WithDescription("Updates a column name, color, WIP limit, or position. When position changes, Kanitel normalizes the project's column order.");

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
})
    .WithTags("Board")
    .WithSummary("Delete board column")
    .WithDescription("Deletes a column when the project has another fallback column. Existing tasks are moved to the first remaining column before positions are normalized.");

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
})
    .WithTags("Participants")
    .WithSummary("Create person")
    .WithDescription("Creates a standalone person profile. A person can later be added to one or more projects as a participant.");

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
})
    .WithTags("Participants")
    .WithSummary("Add or update project person")
    .WithDescription("Adds a person to a project by existing person id or by email/display name. If the person is already a member, this updates the project role.");

app.MapPatch("/api/members/{memberId}", async (JsonDataStore store, string memberId, MemberRequest request, CancellationToken cancellationToken) =>
{
    var updated = await store.MutateAsync(state =>
    {
        var member = state.Members.FirstOrDefault(m => m.Id == memberId);
        if (member is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            member.Role = request.Role.Trim();
        }

        return member;
    }, cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
})
    .WithTags("Participants")
    .WithSummary("Update project person role")
    .WithDescription("Updates the role of a human participant in a project. Common roles are viewer, editor, worker, reviewer, manager, and owner.");

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
})
    .WithTags("Participants")
    .WithSummary("Remove project person")
    .WithDescription("Removes a human participant from a project. The underlying person profile remains available for other projects.");

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
})
    .WithTags("Repositories")
    .WithSummary("Link repository")
    .WithDescription("Links an HTTP(S) or SSH Git repository to a project. Agent workspaces clone linked repositories before running a task.");

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
})
    .WithTags("Repositories")
    .WithSummary("Update repository link")
    .WithDescription("Updates repository display name, URL, branch, or auth mode. Auth mode is normally `http` or `ssh`.");

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
})
    .WithTags("Repositories")
    .WithSummary("Remove repository link")
    .WithDescription("Removes a repository from the project. Existing agent workspaces are not deleted.");

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
        if (!string.IsNullOrWhiteSpace(request.CommandTemplate)) agent.CommandTemplate = request.CommandTemplate.Trim();
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
})
    .WithTags("Participants")
    .WithSummary("Add or update project agent")
    .WithDescription("Adds a global agent to a project as a participant. If already linked, updates the agent's project role. Project agents can be assigned tasks like people.");

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
})
    .WithTags("Participants")
    .WithSummary("Update project agent role")
    .WithDescription("Updates the role of an agent participant in a project. Agents can use separate action endpoints to change or remove roles when allowed.");

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
})
    .WithTags("Participants")
    .WithSummary("Remove project agent")
    .WithDescription("Removes an agent participant from a project. The global agent profile remains configured and can be added to other projects.");

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
            AuthorType = string.IsNullOrWhiteSpace(request.AuthorType) ? "person" : request.AuthorType.Trim(),
            AuthorId = BlankToNull(request.AuthorId) ?? "owner",
            Body = string.IsNullOrWhiteSpace(request.InitialComment)
                ? "Task created."
                : request.InitialComment.Trim()
        });
        return task;
    }, cancellationToken);

    return created is null ? Results.BadRequest() : Results.Ok(created);
})
    .WithTags("Tasks")
    .WithSummary("Create task")
    .WithDescription("Creates a task card in a project column, assigns it to either a person or an agent, and writes the initial conversation comment. Agent assignment can trigger the scheduler after a non-agent comment.");

app.MapPatch("/api/tasks/{taskId}", async (JsonDataStore store, string taskId, TaskRequest request, CancellationToken cancellationToken) =>
{
    var updated = await store.MutateAsync(state =>
    {
        var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
        {
            return null;
        }

        var previousColumnId = task.ColumnId;
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
        if (request.Position.HasValue)
        {
            task.Position = Math.Max(0, request.Position.Value);
        }
        else if (previousColumnId != task.ColumnId)
        {
            task.Position = state.Tasks
                .Where(t => t.ProjectId == task.ProjectId && t.ColumnId == task.ColumnId && t.Id != task.Id)
                .Select(t => t.Position)
                .DefaultIfEmpty(-1)
                .Max() + 1;
        }

        if (previousColumnId != task.ColumnId || request.Position.HasValue)
        {
            NormalizeTaskPositions(state, task.ProjectId, previousColumnId);
            NormalizeTaskPositions(state, task.ProjectId, task.ColumnId);
        }

        task.UpdatedAt = DateTimeOffset.UtcNow;
        return task;
    }, cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
})
    .WithTags("Tasks")
    .WithSummary("Update task")
    .WithDescription("Updates title, description, status column, assignee, assignment role, priority, or position. Drag-and-drop uses this endpoint by changing `columnId` and `position`.");

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
})
    .WithTags("Tasks")
    .WithSummary("Add task comment")
    .WithDescription("Adds a human, agent, or system comment to a task. Human comments assigned to an agent can make the scheduler pick up the task.");

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
})
    .WithTags("Agent Actions")
    .WithSummary("Agent comment")
    .WithDescription("Allows an enabled agent that is linked to the project to comment as itself. This is the safe endpoint for containerized agents to report progress or final results.");

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
})
    .WithTags("Agent Actions")
    .WithSummary("Agent task action")
    .WithDescription("Allows a linked agent to update a task: move it by column id or column name, add a comment body, reassign to another allowed agent, unassign itself, update assignment role, priority, title, or description.");

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
})
    .WithTags("Agent Actions")
    .WithSummary("Agent project role action")
    .WithDescription("Allows a linked agent to update or remove an agent participant role in the same project. This supports AI manager workflows where an agent promotes another agent to manager or removes an assignment.");

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

static KanitelState SanitizeForClient(KanitelState state)
{
    state.Accounts = [];
    return state;
}

static Person? FindCurrentUser(KanitelState state, HttpRequest request)
{
    var token = ReadBearerToken(request);
    if (string.IsNullOrWhiteSpace(token))
    {
        return null;
    }

    var account = state.Accounts.FirstOrDefault(item => item.SessionToken == token);
    return account is null
        ? null
        : state.People.FirstOrDefault(item => item.Id == account.PersonId);
}

static string? ReadBearerToken(HttpRequest request)
{
    var authorization = request.Headers.Authorization.ToString();
    if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return authorization["Bearer ".Length..].Trim();
    }

    var token = request.Headers["X-Kanitel-Token"].ToString();
    return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
}

static string NormalizeEmail(string email)
{
    return email.Trim().ToLowerInvariant();
}

static string NewToken()
{
    return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}

static string HashPassword(string password, string salt)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}"));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

static bool VerifyPassword(string password, string salt, string expectedHash)
{
    var actual = HashPassword(password, salt);
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(actual),
        Encoding.UTF8.GetBytes(expectedHash));
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

static void NormalizeTaskPositions(KanitelState state, string projectId, string columnId)
{
    var ordered = state.Tasks
        .Where(task => task.ProjectId == projectId && task.ColumnId == columnId)
        .OrderBy(task => task.Position)
        .ThenBy(task => task.UpdatedAt)
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

/// <summary>Project create/update payload.</summary>
/// <param name="Name">Human-readable project name. Required when creating.</param>
/// <param name="Description">Optional project description shown in the UI.</param>
public sealed record ProjectRequest(string Name, string? Description);

/// <summary>Registration payload for a local Kanitel account.</summary>
/// <param name="DisplayName">Name displayed in comments, task authors, and project participants.</param>
/// <param name="Email">Unique login email. Stored normalized to lowercase.</param>
/// <param name="Password">Plain password for registration. Must be at least six characters.</param>
/// <param name="AvatarUrl">Optional avatar image URL for the linked person profile.</param>
public sealed record AuthRegisterRequest(
    string DisplayName,
    string Email,
    string Password,
    string? AvatarUrl);

/// <summary>Login payload for local token authentication.</summary>
/// <param name="Email">Account email.</param>
/// <param name="Password">Account password.</param>
public sealed record AuthLoginRequest(string Email, string Password);

/// <summary>Authenticated profile update payload.</summary>
/// <param name="DisplayName">New display name. Blank values are ignored.</param>
/// <param name="Email">New unique email. Blank values are ignored.</param>
/// <param name="AvatarUrl">New avatar URL. Empty string clears the avatar.</param>
/// <param name="Password">New password. Blank values leave the password unchanged.</param>
public sealed record ProfileRequest(
    string? DisplayName,
    string? Email,
    string? AvatarUrl,
    string? Password);

/// <summary>Authentication response containing the bearer token and person profile.</summary>
/// <param name="Token">Opaque session token. Send it as Authorization: Bearer token.</param>
/// <param name="Person">Linked user profile.</param>
public sealed record AuthResponse(string Token, Person Person);

/// <summary>Board column create/update payload.</summary>
/// <param name="Name">Column name shown on the kanban board.</param>
/// <param name="Color">CSS color used as the column accent.</param>
/// <param name="Position">Zero-based position for ordering columns.</param>
/// <param name="WipLimit">Optional work-in-progress limit displayed in the header.</param>
/// <param name="WipLimitSet">Set true when the client intentionally updates the WIP limit, including clearing it.</param>
public sealed record ColumnRequest(
    string? Name,
    string? Color,
    int? Position,
    int? WipLimit,
    bool WipLimitSet);

/// <summary>Standalone person profile payload.</summary>
/// <param name="DisplayName">Person name.</param>
/// <param name="Email">Optional contact email.</param>
/// <param name="AvatarUrl">Optional avatar image URL.</param>
public sealed record PersonRequest(string DisplayName, string? Email, string? AvatarUrl);

/// <summary>Project human participant payload.</summary>
/// <param name="PersonId">Existing person id. If omitted, Kanitel can create or find a person by email.</param>
/// <param name="DisplayName">Name for a new person when PersonId is omitted.</param>
/// <param name="Email">Email used to find or create a person.</param>
/// <param name="AvatarUrl">Optional avatar URL for the person profile.</param>
/// <param name="Role">Project role such as viewer, editor, worker, reviewer, manager, or owner.</param>
public sealed record MemberRequest(
    string? PersonId,
    string? DisplayName,
    string? Email,
    string? AvatarUrl,
    string? Role);

/// <summary>Git repository link payload.</summary>
/// <param name="Name">Display name. Defaults to the URL when omitted.</param>
/// <param name="Url">HTTP(S) or SSH Git URL.</param>
/// <param name="Branch">Optional branch to clone with depth 1.</param>
/// <param name="AuthMode">Authentication mode. Usually http or ssh; inferred when omitted on create.</param>
public sealed record RepositoryRequest(
    string? Name,
    string Url,
    string? Branch,
    string? AuthMode);

/// <summary>Global AI agent profile payload.</summary>
/// <param name="Name">Agent display name.</param>
/// <param name="TemplateId">OpenClaude template id used for default prompt and tool tags.</param>
/// <param name="AgentType">Internal agent type. Defaults from the selected template.</param>
/// <param name="AvatarUrl">Agent avatar or provider logo URL.</param>
/// <param name="ProviderPresetId">OpenClaude provider preset id, for example codex, anthropic, gemini, or github.</param>
/// <param name="BaseUrl">Provider API base URL.</param>
/// <param name="Model">Model name passed to the agent runtime.</param>
/// <param name="ApiKeyEnvName">Host environment variable name containing the provider API key.</param>
/// <param name="ContainerImage">Docker image used for isolated task workspaces.</param>
/// <param name="CommandTemplate">Shell command run inside the agent container.</param>
/// <param name="SystemPrompt">System prompt prepended to the Kanitel task prompt.</param>
/// <param name="Enabled">When false, the scheduler will not start this agent.</param>
/// <param name="Environment">Extra environment variables passed to the agent container.</param>
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

/// <summary>Add an existing global agent to a project.</summary>
/// <param name="AgentId">Global agent id.</param>
/// <param name="Role">Project role for this agent, such as worker, reviewer, manager, or observer.</param>
public sealed record ProjectAgentRequest(string AgentId, string? Role);

/// <summary>Project agent role update payload.</summary>
/// <param name="Role">New project role for the agent participant.</param>
public sealed record ProjectAgentPatchRequest(string? Role);

/// <summary>Task create/update payload.</summary>
/// <param name="Title">Task title. Required when creating.</param>
/// <param name="Description">Task body shown in the modal and sent to agents.</param>
/// <param name="ColumnId">Board column id. Used as task status.</param>
/// <param name="AssigneeAgentId">Agent assignee id. Mutually exclusive with AssigneePersonId in normal UI use.</param>
/// <param name="AssigneePersonId">Human assignee id. Mutually exclusive with AssigneeAgentId in normal UI use.</param>
/// <param name="AssignmentRole">Role for the assignee on this task, such as worker, reviewer, or manager.</param>
/// <param name="Priority">Task priority: low, normal, high, or urgent.</param>
/// <param name="Position">Zero-based order inside the column. Used by drag-and-drop.</param>
/// <param name="InitialComment">Initial conversation message written when creating a task.</param>
/// <param name="AuthorType">Initial comment author type: person, agent, or system.</param>
/// <param name="AuthorId">Initial comment author id.</param>
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
    string? AuthorType,
    string? AuthorId);

/// <summary>Task comment payload.</summary>
/// <param name="Body">Markdown/plain text comment body.</param>
/// <param name="AuthorType">Author type: person, agent, or system. Defaults to person.</param>
/// <param name="AuthorId">Author id. Defaults to owner when omitted.</param>
public sealed record CommentRequest(
    string Body,
    string? AuthorType,
    string? AuthorId);

/// <summary>Agent self-comment payload.</summary>
/// <param name="AgentId">Agent id. Must be enabled and linked to the task project.</param>
/// <param name="Body">Comment body written as that agent.</param>
public sealed record AgentCommentRequest(
    string AgentId,
    string Body);

/// <summary>Agent task action payload for containerized agents and AI managers.</summary>
/// <param name="AgentId">Acting agent id. Must be enabled and linked to the task project.</param>
/// <param name="Body">Optional comment body to append as the agent.</param>
/// <param name="ColumnId">Target board column id.</param>
/// <param name="ColumnName">Target board column name when the agent does not know the id.</param>
/// <param name="AssigneeAgentId">Agent id to assign next. Empty string clears the agent assignment.</param>
/// <param name="AssignmentRole">New task role, for example manager or reviewer.</param>
/// <param name="UnassignAgent">When true, clears the current agent assignment and resets role to worker.</param>
/// <param name="Priority">New priority.</param>
/// <param name="Title">Optional new title.</param>
/// <param name="Description">Optional new description.</param>
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

/// <summary>Agent action for another project-agent role.</summary>
/// <param name="AgentId">Acting agent id. Must be enabled and linked to the same project.</param>
/// <param name="Role">New role to assign to the project-agent link.</param>
/// <param name="Remove">When true, removes the project-agent link instead of updating the role.</param>
public sealed record AgentProjectAgentActionRequest(
    string AgentId,
    string? Role,
    bool? Remove);
