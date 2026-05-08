using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kanitel.Api;

public sealed class JsonDataStore
{
    private const string CurrentSchemaVersion = "2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IConfiguration _configuration;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private KanitelState? _state;

    public JsonDataStore(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _path = configuration["KANITEL_DATA_PATH"]
            ?? Environment.GetEnvironmentVariable("KANITEL_DATA_PATH")
            ?? Path.Combine(environment.ContentRootPath, "data", "kanitel.json");
    }

    public async Task<KanitelState> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return Clone(_state!);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> MutateAsync<T>(Func<KanitelState, T> mutation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            var result = mutation(_state!);
            Normalize(_state!);
            await SaveAsync(_state!, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
        {
            return;
        }

        if (!File.Exists(_path))
        {
            _state = SeedState(_configuration);
            await SaveAsync(_state, cancellationToken);
            return;
        }

        await using var stream = File.OpenRead(_path);
        _state = await JsonSerializer.DeserializeAsync<KanitelState>(stream, JsonOptions, cancellationToken)
            ?? SeedState(_configuration);
        Normalize(_state);
    }

    private async Task SaveAsync(KanitelState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Copy(tempPath, _path, overwrite: true);
        File.Delete(tempPath);
    }

    private static KanitelState Clone(KanitelState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return JsonSerializer.Deserialize<KanitelState>(json, JsonOptions) ?? new KanitelState();
    }

    private static void Normalize(KanitelState state)
    {
        state.Projects ??= [];
        state.Accounts ??= [];
        state.Columns ??= [];
        state.People ??= [];
        state.Members ??= [];
        state.Repositories ??= [];
        state.Agents ??= [];
        state.ProjectAgents ??= [];
        state.Tasks ??= [];
        state.Comments ??= [];
        state.History ??= [];
        state.Runs ??= [];

        if (!string.Equals(state.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(state.SchemaVersion) ||
                string.Equals(state.SchemaVersion, "1", StringComparison.Ordinal))
            {
                RenameReadyColumns(state);
            }

            state.SchemaVersion = CurrentSchemaVersion;
        }

        foreach (var agent in state.Agents)
        {
            agent.Environment ??= [];
            agent.ToolTags ??= [];
            agent.AvatarUrl ??= "";
            agent.ApiKeySourceEnvName ??= "";
            agent.CommandTemplate = AgentCommandDefaults.Normalize(agent.CommandTemplate);
            NormalizeGithubModelsAgent(agent);
        }

        foreach (var person in state.People)
        {
            person.AvatarUrl ??= "";
        }

        foreach (var account in state.Accounts)
        {
            account.Email ??= "";
            account.PasswordHash ??= "";
            account.PasswordSalt ??= "";
            account.SessionToken ??= "";
        }
    }

    private static KanitelState SeedState(IConfiguration configuration)
    {
        var project = new Project
        {
            Name = "Kanitel Core",
            Description = "Demo workspace for repository tasks and AI agents."
        };

        var columns = new[]
        {
            new BoardColumn { ProjectId = project.Id, Name = "Backlog", Color = "#64748b", Position = 0 },
            new BoardColumn { ProjectId = project.Id, Name = "To Do", Color = "#0ea5e9", Position = 1 },
            new BoardColumn { ProjectId = project.Id, Name = "In Progress", Color = "#f59e0b", Position = 2, WipLimit = 3 },
            new BoardColumn { ProjectId = project.Id, Name = "Review", Color = "#8b5cf6", Position = 3 },
            new BoardColumn { ProjectId = project.Id, Name = "Done", Color = "#22c55e", Position = 4 }
        };

        var owner = new Person
        {
            DisplayName = "Project Owner",
            Email = "owner@example.local",
            AvatarUrl = ""
        };

        var firstTask = new TaskCard
        {
            ProjectId = project.Id,
            ColumnId = columns[1].Id,
            Title = "Create your first agent",
            Description = "Add an agent in Settings, attach it to this project as a participant, then assign a task to it.",
            Position = 0
        };

        return new KanitelState
        {
            Projects = [project],
            Columns = columns.ToList(),
            People = [owner],
            Members =
            [
                new ProjectMember { ProjectId = project.Id, PersonId = owner.Id }
            ],
            Agents = EnvAgentCatalog.Read(configuration),
            ProjectAgents = [],
            Tasks = [firstTask],
            History =
            [
                new TaskHistoryEntry
                {
                    TaskId = firstTask.Id,
                    AuthorType = "person",
                    AuthorId = owner.Id,
                    Action = "created",
                    Field = "task",
                    To = firstTask.Title
                }
            ],
            Comments =
            [
                new TaskComment
                {
                    TaskId = firstTask.Id,
                    AuthorType = "person",
                    AuthorId = owner.Id,
                    Body = "Initial assignment: prepare the workspace and report what would be changed."
                }
            ]
        };
    }

    private static void RenameReadyColumns(KanitelState state)
    {
        foreach (var column in state.Columns)
        {
            if (string.Equals(column.Name, "Ready", StringComparison.Ordinal))
            {
                column.Name = "To Do";
            }
        }
    }

    private static void NormalizeGithubModelsAgent(AgentProfile agent)
    {
        if (!string.Equals(agent.ProviderPresetId, "github-models", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var preset = OpenClaudeCatalog.ProviderPresets.First(item => item.Id == "github-models");
        var previousToken = ReadEnvironment(agent.Environment, "GITHUB_TOKEN")
            ?? ReadEnvironment(agent.Environment, "GH_TOKEN")
            ?? ReadEnvironment(agent.Environment, "OPENAI_API_KEY");

        agent.Provider = preset.Provider;
        agent.BaseUrl = string.IsNullOrWhiteSpace(agent.BaseUrl) ||
            string.Equals(agent.BaseUrl, "https://api.githubcopilot.com", StringComparison.OrdinalIgnoreCase)
                ? preset.BaseUrl
                : agent.BaseUrl;
        agent.Model = string.IsNullOrWhiteSpace(agent.Model) ? preset.DefaultModel : agent.Model;
        agent.ApiKeyEnvName = "GITHUB_TOKEN";

        RemoveEnvironment(agent.Environment, "CLAUDE_CODE_USE_OPENAI");
        RemoveEnvironment(agent.Environment, "OPENAI_API_KEY");
        agent.Environment["CLAUDE_CODE_USE_GITHUB"] = "1";
        agent.Environment["OPENAI_BASE_URL"] = agent.BaseUrl;
        agent.Environment["OPENAI_MODEL"] = agent.Model;

        if (!string.IsNullOrWhiteSpace(previousToken) && string.IsNullOrWhiteSpace(ReadEnvironment(agent.Environment, "GITHUB_TOKEN")))
        {
            agent.Environment["GITHUB_TOKEN"] = previousToken;
        }
    }

    private static string? ReadEnvironment(Dictionary<string, string> environment, string key)
    {
        foreach (var pair in environment)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static void RemoveEnvironment(Dictionary<string, string> environment, string key)
    {
        foreach (var existingKey in environment.Keys.ToList())
        {
            if (existingKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                environment.Remove(existingKey);
            }
        }
    }
}
