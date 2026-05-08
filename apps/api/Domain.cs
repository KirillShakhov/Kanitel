namespace Kanitel.Api;

public sealed class KanitelState
{
    public string SchemaVersion { get; set; } = "2";
    public List<UserAccount> Accounts { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
    public List<BoardColumn> Columns { get; set; } = [];
    public List<Person> People { get; set; } = [];
    public List<ProjectMember> Members { get; set; } = [];
    public List<RepositoryLink> Repositories { get; set; } = [];
    public List<AgentProfile> Agents { get; set; } = [];
    public List<ProjectAgent> ProjectAgents { get; set; } = [];
    public List<TaskCard> Tasks { get; set; } = [];
    public List<TaskComment> Comments { get; set; } = [];
    public List<TaskHistoryEntry> History { get; set; } = [];
    public List<AgentRun> Runs { get; set; } = [];
}

public sealed class Project
{
    public string Id { get; set; } = Ids.New("project");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BoardColumn
{
    public string Id { get; set; } = Ids.New("column");
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3b82f6";
    public int Position { get; set; }
    public int? WipLimit { get; set; }
}

public sealed class Person
{
    public string Id { get; set; } = Ids.New("person");
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserAccount
{
    public string Id { get; set; } = Ids.New("account");
    public string PersonId { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string SessionToken { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class ProjectMember
{
    public string Id { get; set; } = Ids.New("member");
    public string ProjectId { get; set; } = "";
    public string PersonId { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RepositoryLink
{
    public string Id { get; set; } = Ids.New("repo");
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Branch { get; set; } = "";
    public string AuthMode { get; set; } = "http";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentProfile
{
    public string Id { get; set; } = Ids.New("agent");
    public string Name { get; set; } = "";
    public string AgentType { get; set; } = "general-purpose";
    public string AvatarUrl { get; set; } = "";
    public string ProviderPresetId { get; set; } = "codex";
    public string Provider { get; set; } = "codex";
    public string BaseUrl { get; set; } = "https://chatgpt.com/backend-api/codex";
    public string Model { get; set; } = "codexplan";
    public string ApiKeyEnvName { get; set; } = "CODEX_API_KEY";
    public string ApiKeySourceEnvName { get; set; } = "";
    public string ContainerImage { get; set; } = "node:22-bookworm";
    public string CommandTemplate { get; set; } = "npx -y @gitlawb/openclaude@latest --print \"$(cat \\\"$KANITEL_TASK_PROMPT_FILE\\\")\"";
    public string SystemPrompt { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Environment { get; set; } = [];
    public List<string> ToolTags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectAgent
{
    public string Id { get; set; } = Ids.New("project_agent");
    public string ProjectId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TaskCard
{
    public string Id { get; set; } = Ids.New("task");
    public string ProjectId { get; set; } = "";
    public string ColumnId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? AssigneeAgentId { get; set; }
    public string? AssigneePersonId { get; set; }
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TaskComment
{
    public string Id { get; set; } = Ids.New("comment");
    public string TaskId { get; set; } = "";
    public string AuthorType { get; set; } = "person";
    public string AuthorId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TaskHistoryEntry
{
    public string Id { get; set; } = Ids.New("history");
    public string TaskId { get; set; } = "";
    public string AuthorType { get; set; } = "system";
    public string AuthorId { get; set; } = "";
    public string Action { get; set; } = "changed";
    public string Field { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentRun
{
    public string Id { get; set; } = Ids.New("run");
    public string ProjectId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string TriggerCommentId { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string WorkspacePath { get; set; } = "";
    public string Log { get; set; } = "";
    public int? ExitCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class ProviderPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string ApiKeyEnvName { get; set; } = "";
    public string Transport { get; set; } = "openai-compatible";
    public string LogoUrl { get; set; } = "";
    public bool RequiresApiKey { get; set; } = true;
    public Dictionary<string, string> Environment { get; set; } = [];
}

public sealed class AgentTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string AgentType { get; set; } = "";
    public string WhenToUse { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public List<string> ToolTags { get; set; } = [];
}

public static class Ids
{
    public static string New(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid().ToString("N")[..12]}";
    }
}
