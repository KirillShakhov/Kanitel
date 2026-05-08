namespace Kanitel.Api;

/// <summary>Project create/update payload.</summary>
/// <param name="Name">Human-readable project name. Required when creating.</param>
/// <param name="Description">Optional project description shown in the UI.</param>
public sealed record ProjectRequest(string Name, string? Description);

/// <summary>Registration payload for a local Kanitel account.</summary>
/// <param name="DisplayName">Name displayed in comments, task authors, and project participants.</param>
/// <param name="Email">Unique login email. Stored normalized to lowercase.</param>
/// <param name="Password">Plain password for registration. Must be at least six characters.</param>
/// <param name="ConfirmPassword">Password confirmation. Must match Password.</param>
/// <param name="AvatarUrl">Optional avatar image URL for the linked person profile.</param>
public sealed record AuthRegisterRequest(
    string DisplayName,
    string Email,
    string Password,
    string ConfirmPassword,
    string? AvatarUrl);

/// <summary>Login payload for local token authentication.</summary>
/// <param name="Email">Account email.</param>
/// <param name="Password">Account password.</param>
public sealed record AuthLoginRequest(string Email, string Password);

/// <summary>Authenticated profile update payload.</summary>
/// <param name="DisplayName">New display name. Blank values are ignored.</param>
/// <param name="Email">New unique email. Blank values are ignored.</param>
/// <param name="AvatarUrl">New avatar URL. Empty string clears the avatar.</param>
/// <param name="CurrentPassword">Current account password. Required when changing the password.</param>
/// <param name="NewPassword">New password. Must be at least six characters when provided.</param>
/// <param name="ConfirmNewPassword">New password confirmation. Must match NewPassword.</param>
public sealed record ProfileRequest(
    string? DisplayName,
    string? Email,
    string? AvatarUrl,
    string? CurrentPassword,
    string? NewPassword,
    string? ConfirmNewPassword);

internal sealed record ProfileUpdateResult(AuthResponse? Response, string? Error)
{
    public static ProfileUpdateResult Success(AuthResponse response) => new(response, null);
    public static ProfileUpdateResult Fail(string error) => new(null, error);
}

/// <summary>Authentication response containing the bearer token and person profile.</summary>
/// <param name="Token">Opaque session token. Send it as Authorization: Bearer token.</param>
/// <param name="Person">Linked user profile.</param>
public sealed record AuthResponse(string Token, Person Person);

/// <summary>Uploaded avatar response.</summary>
/// <param name="Url">Data URL that can be stored as a person or agent avatar URL.</param>
public sealed record AvatarUploadResponse(string Url);

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
/// <param name="PersonId">Existing registered person id.</param>
public sealed record MemberRequest(string PersonId);

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
/// <param name="ProviderPresetId">OpenClaude provider preset id, for example codex, anthropic, gemini, github-models, or github.</param>
/// <param name="BaseUrl">Provider API base URL.</param>
/// <param name="Model">Model name passed to the agent runtime.</param>
/// <param name="ApiKeyEnvName">Host environment variable name containing the provider API key.</param>
/// <param name="ApiKeyValue">Optional API key or token saved into ApiKeyEnvName for agents configured from the UI or API.</param>
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
    string? ApiKeyValue,
    string? ContainerImage,
    string? CommandTemplate,
    string? SystemPrompt,
    bool? Enabled,
    Dictionary<string, string>? Environment);

/// <summary>Add an existing global agent to a project.</summary>
/// <param name="AgentId">Global agent id.</param>
public sealed record ProjectAgentRequest(string AgentId);

/// <summary>Task create/update payload.</summary>
/// <param name="Title">Task title. Required when creating.</param>
/// <param name="Description">Task body shown in the modal and sent to agents.</param>
/// <param name="ColumnId">Board column id. Used as task status.</param>
/// <param name="AssigneeAgentId">Agent assignee id. Mutually exclusive with AssigneePersonId in normal UI use.</param>
/// <param name="AssigneePersonId">Human assignee id. Mutually exclusive with AssigneeAgentId in normal UI use.</param>
/// <param name="Position">Zero-based order inside the column. Used by drag-and-drop.</param>
/// <param name="InitialComment">Initial conversation message written when creating a task.</param>
/// <param name="AuthorType">Initial comment author type on create, or history actor fallback on update: person, agent, or system.</param>
/// <param name="AuthorId">Initial comment author id on create, or history actor fallback on update.</param>
public sealed record TaskRequest(
    string? Title,
    string? Description,
    string? ColumnId,
    string? AssigneeAgentId,
    string? AssigneePersonId,
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
/// <param name="UnassignAgent">When true, clears the current agent assignment.</param>
/// <param name="Title">Optional new title.</param>
/// <param name="Description">Optional new description.</param>
public sealed record AgentTaskActionRequest(
    string AgentId,
    string? Body,
    string? ColumnId,
    string? ColumnName,
    string? AssigneeAgentId,
    bool? UnassignAgent,
    string? Title,
    string? Description);
