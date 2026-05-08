using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Kanitel.Api;

public interface IAgentRunner
{
    Task<AgentRunResult> RunAsync(
        AgentRun run,
        Project project,
        AgentProfile agent,
        TaskCard task,
        IReadOnlyList<TaskComment> comments,
        IReadOnlyList<Person> people,
        IReadOnlyList<AgentProfile> agents,
        IReadOnlyList<BoardColumn> columns,
        IReadOnlyList<RepositoryLink> repositories,
        CancellationToken cancellationToken);
}

public sealed record AgentRunResult(
    bool Success,
    string WorkspacePath,
    string Log,
    int? ExitCode);

public sealed class DockerAgentRunner(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<DockerAgentRunner> logger) : IAgentRunner
{
    public async Task<AgentRunResult> RunAsync(
        AgentRun run,
        Project project,
        AgentProfile agent,
        TaskCard task,
        IReadOnlyList<TaskComment> comments,
        IReadOnlyList<Person> people,
        IReadOnlyList<AgentProfile> agents,
        IReadOnlyList<BoardColumn> columns,
        IReadOnlyList<RepositoryLink> repositories,
        CancellationToken cancellationToken)
    {
        var workspace = CreateWorkspacePath(run.Id, task.Title);
        Directory.CreateDirectory(workspace);

        var log = new StringBuilder();
        log.AppendLine($"Kanitel run {run.Id}");
        log.AppendLine($"Workspace: {workspace}");
        log.AppendLine($"Agent provider: {agent.ProviderPresetId}/{agent.Provider}; model: {agent.Model}; base URL: {agent.BaseUrl}.");

        await CloneRepositoriesAsync(repositories, workspace, log, cancellationToken);

        var prompt = BuildPrompt(project, agent, task, comments, people, agents, columns, repositories);
        var promptPath = Path.Combine(workspace, "task.md");
        await File.WriteAllTextAsync(promptPath, prompt, cancellationToken);
        log.AppendLine($"Task prompt: {prompt.Length:n0} characters.");

        if (IsMockRunner())
        {
            log.AppendLine("Mock runner enabled; no Docker container was started.");
            log.AppendLine(prompt);
            return new AgentRunResult(true, workspace, TrimLog(log.ToString()), 0);
        }

        try
        {
            var dockerResult = await RunDockerAsync(run, agent, workspace, log, cancellationToken);
            log.AppendLine($"Agent container exit code: {dockerResult.ExitCode}.");
            log.AppendLine(AnnotateAgentRunLog(agent, dockerResult.Log));
            return new AgentRunResult(
                dockerResult.ExitCode == 0,
                workspace,
                TrimLog(log.ToString()),
                dockerResult.ExitCode);
        }
        catch (Win32Exception ex)
        {
            log.AppendLine($"Failed to start Docker CLI: {ex.Message}");
            logger.LogWarning(ex, "Docker CLI is unavailable for agent run {RunId}", run.Id);
            return new AgentRunResult(false, workspace, TrimLog(log.ToString()), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.AppendLine($"Runner failed: {ex.Message}");
            logger.LogError(ex, "Agent run {RunId} failed", run.Id);
            return new AgentRunResult(false, workspace, TrimLog(log.ToString()), null);
        }
    }

    private async Task CloneRepositoriesAsync(
        IReadOnlyList<RepositoryLink> repositories,
        string workspace,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        if (repositories.Count == 0)
        {
            log.AppendLine("No repositories linked to this project.");
            return;
        }

        var reposRoot = Path.Combine(workspace, "repos");
        Directory.CreateDirectory(reposRoot);

        foreach (var repo in repositories)
        {
            if (string.IsNullOrWhiteSpace(repo.Url))
            {
                continue;
            }

            var folder = SanitizePathSegment(string.IsNullOrWhiteSpace(repo.Name) ? repo.Id : repo.Name);
            var target = Path.Combine(reposRoot, folder);
            var args = new List<string> { "clone", "--depth", "1" };
            if (!string.IsNullOrWhiteSpace(repo.Branch))
            {
                args.Add("--branch");
                args.Add(repo.Branch);
            }

            args.Add(repo.Url);
            args.Add(target);

            try
            {
                var result = await RunProcessAsync("git", args, workspace, null, TimeSpan.FromMinutes(10), cancellationToken);
                log.AppendLine($"git clone {repo.Name}: exit {result.ExitCode}");
                if (!string.IsNullOrWhiteSpace(result.Log))
                {
                    log.AppendLine(result.Log);
                }
            }
            catch (Win32Exception ex)
            {
                log.AppendLine($"git is unavailable; repository {repo.Name} was not cloned: {ex.Message}");
            }
        }
    }

    private async Task<ProcessResult> RunDockerAsync(
        AgentRun run,
        AgentProfile agent,
        string workspace,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var command = RenderCommand(agent.CommandTemplate, run);
        var containerName = $"kanitel-agent-{run.Id}".Replace('_', '-');
        var image = string.IsNullOrWhiteSpace(agent.ContainerImage) ? "node:22-bookworm" : agent.ContainerImage;
        var workspaceMode = ReadString("KANITEL_DOCKER_WORKSPACE_MODE", "copy");
        var npmCacheVolume = ReadString("KANITEL_DOCKER_NPM_CACHE_VOLUME", "kanitel-agent-npm-cache");
        var agentEnvironment = BuildAgentEnvironment(agent);
        var validationError = ValidateAgentEnvironment(agent, agentEnvironment);
        if (validationError is not null)
        {
            return new ProcessResult(2, validationError);
        }

        var dockerOptions = BuildDockerOptions(run, agent, containerName, agentEnvironment);

        log.AppendLine($"Starting Docker container {containerName} using image {image}.");
        log.AppendLine($"Docker workspace mode: {workspaceMode}.");
        if (!IsDisabled(npmCacheVolume))
        {
            log.AppendLine($"Docker npm cache volume: {npmCacheVolume}.");
        }

        return string.Equals(workspaceMode, "bind", StringComparison.OrdinalIgnoreCase)
            ? await RunDockerWithBindWorkspaceAsync(containerName, image, command, dockerOptions, workspace, cancellationToken)
            : await RunDockerWithCopiedWorkspaceAsync(containerName, image, command, dockerOptions, workspace, cancellationToken);
    }

    private List<string> BuildDockerOptions(
        AgentRun run,
        AgentProfile agent,
        string containerName,
        IReadOnlyDictionary<string, string> agentEnvironment)
    {
        var dockerOptions = new List<string>
        {
            "--name",
            containerName,
            "-w",
            "/workspace",
            "-e",
            "KANITEL_TASK_PROMPT_FILE=/workspace/task.md",
            "-e",
            $"KANITEL_TASK_ID={run.TaskId}",
            "-e",
            $"KANITEL_RUN_ID={run.Id}",
            "-e",
            $"KANITEL_AGENT_ID={run.AgentId}",
            "-e",
            $"KANITEL_PROJECT_ID={run.ProjectId}",
            "-e",
            $"KANITEL_API_URL={GetPublicApiUrl()}"
        };

        if (ReadBool("KANITEL_DOCKER_ADD_HOST_GATEWAY", true))
        {
            dockerOptions.Add("--add-host");
            dockerOptions.Add("host.docker.internal:host-gateway");
        }

        var npmCacheVolume = ReadString("KANITEL_DOCKER_NPM_CACHE_VOLUME", "kanitel-agent-npm-cache");
        if (!IsDisabled(npmCacheVolume))
        {
            dockerOptions.Add("-v");
            dockerOptions.Add($"{npmCacheVolume}:/root/.npm");
        }

        foreach (var (key, value) in agentEnvironment)
        {
            dockerOptions.Add("-e");
            dockerOptions.Add($"{key}={value}");
        }

        if (!string.IsNullOrWhiteSpace(agent.ApiKeyEnvName) &&
            !agent.Environment.ContainsKey(agent.ApiKeyEnvName))
        {
            dockerOptions.Add("-e");
            dockerOptions.Add(agent.ApiKeyEnvName);
        }

        foreach (var envName in KnownProviderEnvNames)
        {
            if (!agent.Environment.ContainsKey(envName) &&
                !string.Equals(envName, agent.ApiKeyEnvName, StringComparison.OrdinalIgnoreCase) &&
                !agentEnvironment.ContainsKey(envName))
            {
                dockerOptions.Add("-e");
                dockerOptions.Add(envName);
            }
        }

        return dockerOptions;
    }

    private async Task<ProcessResult> RunDockerWithBindWorkspaceAsync(
        string containerName,
        string image,
        string command,
        IReadOnlyList<string> dockerOptions,
        string workspace,
        CancellationToken cancellationToken)
    {
        var dockerArgs = new List<string>
        {
            "run",
            "--rm",
            "-v",
            $"{workspace}:/workspace"
        };
        dockerArgs.AddRange(dockerOptions);
        dockerArgs.Add(image);
        dockerArgs.Add("/bin/sh");
        dockerArgs.Add("-lc");
        dockerArgs.Add(command);

        return await RunProcessAsync(
            "docker",
            dockerArgs,
            workspace,
            null,
            TimeSpan.FromMinutes(ReadInt("KANITEL_AGENT_TIMEOUT_MINUTES", 30)),
            cancellationToken);
    }

    private async Task<ProcessResult> RunDockerWithCopiedWorkspaceAsync(
        string containerName,
        string image,
        string command,
        IReadOnlyList<string> dockerOptions,
        string workspace,
        CancellationToken cancellationToken)
    {
        await TryRemoveContainerAsync(containerName, workspace, cancellationToken);

        var timeout = TimeSpan.FromMinutes(ReadInt("KANITEL_AGENT_TIMEOUT_MINUTES", 30));
        var createArgs = new List<string> { "create" };
        createArgs.AddRange(dockerOptions);
        createArgs.Add(image);
        createArgs.Add("/bin/sh");
        createArgs.Add("-lc");
        createArgs.Add(command);

        try
        {
            var create = await RunProcessAsync("docker", createArgs, workspace, null, TimeSpan.FromMinutes(2), cancellationToken);
            if (create.ExitCode != 0)
            {
                return create;
            }

            var copyIn = await RunProcessAsync(
                "docker",
                ["cp", WorkspaceCopySource(workspace), $"{containerName}:/workspace"],
                workspace,
                null,
                TimeSpan.FromMinutes(10),
                cancellationToken);
            if (copyIn.ExitCode != 0)
            {
                return copyIn;
            }

            var start = await RunProcessAsync("docker", ["start", "-a", containerName], workspace, null, timeout, cancellationToken);
            var exitCode = await InspectContainerExitCodeAsync(containerName, start.ExitCode, workspace, cancellationToken);
            var copyOut = await RunProcessAsync(
                "docker",
                ["cp", $"{containerName}:/workspace/.", workspace],
                workspace,
                null,
                TimeSpan.FromMinutes(10),
                cancellationToken);

            var combinedLog = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(start.Log))
            {
                combinedLog.AppendLine(start.Log);
            }

            if (copyOut.ExitCode != 0)
            {
                combinedLog.AppendLine("Failed to copy workspace back from the agent container:");
                combinedLog.AppendLine(copyOut.Log);
                return new ProcessResult(copyOut.ExitCode, TrimLog(combinedLog.ToString()));
            }

            return new ProcessResult(exitCode, TrimLog(combinedLog.ToString()));
        }
        finally
        {
            await TryRemoveContainerAsync(containerName, workspace, CancellationToken.None);
        }
    }

    private Dictionary<string, string> BuildAgentEnvironment(AgentProfile agent)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in agent.Environment)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                env[pair.Key] = pair.Value;
            }
        }

        RemoveProviderSelectionFlags(env);
        env["CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST"] = "1";
        env["CLAUDE_CODE_PROVIDER_PROFILE_ENV_APPLIED"] = "1";
        env.TryAdd("NPM_CONFIG_LOGLEVEL", "error");
        env.TryAdd("NPM_CONFIG_UPDATE_NOTIFIER", "false");
        env.TryAdd("NPM_CONFIG_FUND", "false");
        env.TryAdd("NPM_CONFIG_AUDIT", "false");
        env.TryAdd("NO_UPDATE_NOTIFIER", "1");
        env.TryAdd("DISABLE_AUTOUPDATER", "1");
        env.TryAdd("DISABLE_TELEMETRY", "1");
        env.TryAdd("DISABLE_COST_WARNINGS", "1");

        var providerApiKey = ResolveAgentApiKey(agent);
        if (!string.IsNullOrWhiteSpace(providerApiKey) &&
            !string.IsNullOrWhiteSpace(agent.ApiKeyEnvName) &&
            !env.ContainsKey(agent.ApiKeyEnvName))
        {
            env[agent.ApiKeyEnvName] = providerApiKey;
        }

        switch ((agent.ProviderPresetId, agent.Provider))
        {
            case ("anthropic", _):
            case (_, "anthropic"):
                env["ANTHROPIC_BASE_URL"] = agent.BaseUrl;
                env["ANTHROPIC_MODEL"] = agent.Model;
                break;
            case ("gemini", _):
            case (_, "gemini"):
                env["CLAUDE_CODE_USE_GEMINI"] = "1";
                env["GEMINI_BASE_URL"] = agent.BaseUrl;
                env["GEMINI_MODEL"] = agent.Model;
                break;
            case ("mistral", _):
            case (_, "mistral"):
                env["CLAUDE_CODE_USE_MISTRAL"] = "1";
                env["MISTRAL_BASE_URL"] = agent.BaseUrl;
                env["MISTRAL_MODEL"] = agent.Model;
                break;
            case ("github", _):
            case (_, "github"):
                env["CLAUDE_CODE_USE_GITHUB"] = "1";
                env["OPENAI_BASE_URL"] = agent.BaseUrl;
                env["OPENAI_MODEL"] = agent.Model;
                var githubToken = IsGithubModelsAgent(agent)
                    ? FirstNonEmpty(env, "GITHUB_TOKEN", "GH_TOKEN", "OPENAI_API_KEY")
                    : FirstNonEmpty(env, "GITHUB_TOKEN", "GH_TOKEN");
                if (!string.IsNullOrWhiteSpace(githubToken))
                {
                    if (IsGithubModelsAgent(agent))
                    {
                        env["GITHUB_TOKEN"] = githubToken;
                    }

                    env["OPENAI_API_KEY"] = githubToken;
                }
                break;
            case ("bedrock", _):
            case (_, "bedrock"):
                env["CLAUDE_CODE_USE_BEDROCK"] = "1";
                env["ANTHROPIC_BEDROCK_BASE_URL"] = agent.BaseUrl;
                env["ANTHROPIC_MODEL"] = agent.Model;
                break;
            case ("vertex", _):
            case (_, "vertex"):
                env["CLAUDE_CODE_USE_VERTEX"] = "1";
                env["ANTHROPIC_VERTEX_BASE_URL"] = agent.BaseUrl;
                env["ANTHROPIC_MODEL"] = agent.Model;
                break;
            default:
                env["CLAUDE_CODE_USE_OPENAI"] = "1";
                env["OPENAI_BASE_URL"] = agent.BaseUrl;
                env["OPENAI_MODEL"] = agent.Model;
                if (!string.IsNullOrWhiteSpace(providerApiKey) &&
                    !env.ContainsKey("OPENAI_API_KEY") &&
                    !string.Equals(agent.ApiKeyEnvName, "OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
                {
                    env["OPENAI_API_KEY"] = providerApiKey;
                }
                break;
        }

        return env;
    }

    private static string? ValidateAgentEnvironment(
        AgentProfile agent,
        IReadOnlyDictionary<string, string> env)
    {
        if (IsTruthy(ReadEnv(env, "CLAUDE_CODE_USE_GITHUB")))
        {
            var token = IsGithubModelsAgent(agent)
                ? FirstNonEmpty(env, "GITHUB_TOKEN", "GH_TOKEN", "OPENAI_API_KEY")
                : FirstNonEmpty(env, "GITHUB_TOKEN", "GH_TOKEN");
            token ??= ResolveHostEnv("GITHUB_TOKEN")
                ?? ResolveHostEnv("GH_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                if (IsGithubModelsAgent(agent))
                {
                    return """
                        GitHub Models authentication is required before this agent can run.

                        Create a GitHub personal access token with the `models` scope
                        (or `models: read` for a fine-grained PAT/GitHub App token),
                        then paste it into the agent's API key / token field.
                        """;
                }

                return """
                    GitHub Copilot authentication is required before this agent can run.

                    Kanitel runs agents in disposable containers, so interactive `/onboard-github`
                    cannot be completed during a task run. Configure this agent with a valid
                    GITHUB_TOKEN/GH_TOKEN value, or switch the agent provider to GitHub Models (PAT),
                    OpenAI, Anthropic, Ollama, LM Studio, or another non-interactive provider.
                    """;
            }
        }

        if (agent.ProviderPresetId == "github" &&
            string.Equals(agent.BaseUrl.TrimEnd('/'), "https://api.githubcopilot.com", StringComparison.OrdinalIgnoreCase))
        {
            var token = FirstNonEmpty(env, "GITHUB_TOKEN", "GH_TOKEN")
                ?? ResolveHostEnv("GITHUB_TOKEN")
                ?? ResolveHostEnv("GH_TOKEN");
            if (token is not null &&
                (token.StartsWith("ghp_", StringComparison.Ordinal) ||
                 token.StartsWith("gho_", StringComparison.Ordinal) ||
                 token.StartsWith("ghs_", StringComparison.Ordinal) ||
                 token.StartsWith("ghr_", StringComparison.Ordinal) ||
                 token.StartsWith("github_pat_", StringComparison.Ordinal)))
            {
                return """
                    GitHub Copilot API does not accept a regular GitHub PAT for this endpoint.

                    Use a Copilot OAuth token produced by OpenClaude `/onboard-github`, or switch
                    this agent to GitHub Models (PAT) with a token that has the `models` scope.
                    """;
            }
        }

        return null;
    }

    private static string AnnotateAgentRunLog(AgentProfile agent, string log)
    {
        if (!IsGithubModelsAgent(agent))
        {
            return log;
        }

        if (log.Contains("Authentication failed for your OpenAI-compatible provider", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
                {log}

                GitHub Models authentication failed. For this provider Kanitel passes the token as GITHUB_TOKEN and OPENAI_API_KEY to https://models.github.ai/inference.
                Check that the token is active and has GitHub Models access: classic PAT needs the `models` scope; fine-grained PAT or GitHub App token needs `models: read`.
                """;
        }

        if (log.Contains("Request too large", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
                {log}

                GitHub Models rejected the OpenClaude request as too large. Kanitel's default command now uses OpenClaude --bare mode to avoid the oversized default tool catalog; if this agent has a custom command, add --bare before --print.
                """;
        }

        return log;
    }

    private static bool IsGithubModelsAgent(AgentProfile agent)
    {
        return string.Equals(agent.ProviderPresetId, "github-models", StringComparison.OrdinalIgnoreCase) ||
            IsGithubModelsBaseUrl(agent.BaseUrl);
    }

    private static bool IsGithubModelsBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        try
        {
            var host = new Uri(baseUrl).Host;
            return host.Equals("models.github.ai", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".github.ai", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string GetPublicApiUrl()
    {
        return configuration["KANITEL_PUBLIC_API_URL"]
            ?? Environment.GetEnvironmentVariable("KANITEL_PUBLIC_API_URL")
            ?? "http://host.docker.internal:8080";
    }

    private string CreateWorkspacePath(string runId, string taskTitle)
    {
        var root = configuration["KANITEL_WORKSPACES_PATH"]
            ?? Environment.GetEnvironmentVariable("KANITEL_WORKSPACES_PATH")
            ?? Path.Combine(environment.ContentRootPath, "workspaces");
        return Path.Combine(root, $"{runId}-{SanitizePathSegment(taskTitle)}");
    }

    private static string RenderCommand(string template, AgentRun run)
    {
        var command = AgentCommandDefaults.Normalize(template);

        return AgentCommandDefaults.Normalize(command
            .Replace("{{prompt_file}}", "$KANITEL_TASK_PROMPT_FILE", StringComparison.Ordinal)
            .Replace("{{task_id}}", run.TaskId, StringComparison.Ordinal)
            .Replace("{{run_id}}", run.Id, StringComparison.Ordinal));
    }

    private string BuildPrompt(
        Project project,
        AgentProfile agent,
        TaskCard task,
        IReadOnlyList<TaskComment> comments,
        IReadOnlyList<Person> people,
        IReadOnlyList<AgentProfile> agents,
        IReadOnlyList<BoardColumn> columns,
        IReadOnlyList<RepositoryLink> repositories)
    {
        var statusColumn = columns.FirstOrDefault(column => column.Id == task.ColumnId);
        var firstComment = comments.OrderBy(comment => comment.CreatedAt).FirstOrDefault();
        var conversationComments = comments
            .Where(comment => !string.Equals(comment.AuthorType, "system", StringComparison.OrdinalIgnoreCase))
            .OrderBy(comment => comment.CreatedAt)
            .ToList();
        var maxConversationComments = ReadInt("KANITEL_AGENT_PROMPT_MAX_COMMENTS", 25);
        var skippedComments = Math.Max(0, conversationComments.Count - maxConversationComments);
        var recentConversation = conversationComments.Skip(skippedComments).ToList();

        var prompt = new StringBuilder();
        prompt.AppendLine(TruncateForPrompt(agent.SystemPrompt, ReadInt("KANITEL_AGENT_PROMPT_SYSTEM_MAX_CHARS", 12_000)));
        prompt.AppendLine();
        prompt.AppendLine("# Kanitel Task");
        prompt.AppendLine($"Project: {project.Name}");
        prompt.AppendLine($"Task title: {task.Title}");
        prompt.AppendLine($"Task id: {task.Id}");
        prompt.AppendLine($"Status: {(statusColumn is null ? task.ColumnId : $"{statusColumn.Name} ({statusColumn.Id})")}");
        prompt.AppendLine($"Assigned agent id: {task.AssigneeAgentId ?? "none"}");
        prompt.AppendLine($"Assigned person id: {task.AssigneePersonId ?? "none"}");
        prompt.AppendLine($"Author: {(firstComment is null ? "unknown" : AuthorLabel(firstComment, people, agents))}");
        prompt.AppendLine();
        prompt.AppendLine("## Description");
        prompt.AppendLine(TruncateForPrompt(task.Description, ReadInt("KANITEL_AGENT_PROMPT_DESCRIPTION_MAX_CHARS", 20_000)));
        prompt.AppendLine();
        prompt.AppendLine("## Linked repositories");
        if (repositories.Count == 0)
        {
            prompt.AppendLine("- No repositories are linked.");
        }
        else
        {
            foreach (var repo in repositories)
            {
                var folder = SanitizePathSegment(string.IsNullOrWhiteSpace(repo.Name) ? repo.Id : repo.Name);
                prompt.AppendLine($"- {repo.Name}: {repo.Url} -> /workspace/repos/{folder}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("## Board columns");
        prompt.AppendLine("Use these column names or IDs when changing task status through the Kanitel API.");
        foreach (var column in columns.OrderBy(c => c.Position))
        {
            prompt.AppendLine($"- {column.Name}: {column.Id}");
        }

        prompt.AppendLine();
        prompt.AppendLine("## Conversation");
        if (comments.Count != conversationComments.Count)
        {
            prompt.AppendLine($"- {comments.Count - conversationComments.Count} system status/log comment(s) omitted from agent context.");
            prompt.AppendLine();
        }

        if (skippedComments > 0)
        {
            prompt.AppendLine($"- {skippedComments} older conversation comment(s) omitted to keep the request small.");
            prompt.AppendLine();
        }

        var maxCommentChars = ReadInt("KANITEL_AGENT_PROMPT_COMMENT_MAX_CHARS", 6_000);
        foreach (var comment in recentConversation)
        {
            prompt.AppendLine($"[{comment.CreatedAt:u}] {AuthorLabel(comment, people, agents)}");
            prompt.AppendLine(TruncateForPrompt(comment.Body, maxCommentChars));
            prompt.AppendLine();
        }

        prompt.AppendLine("## Kanitel API access");
        prompt.AppendLine("You may update the board directly from this container. Use the environment variables KANITEL_API_URL, KANITEL_TASK_ID, KANITEL_AGENT_ID, and KANITEL_PROJECT_ID.");
        prompt.AppendLine("Comment as yourself:");
        prompt.AppendLine("curl -s -X POST \"$KANITEL_API_URL/api/agent/tasks/$KANITEL_TASK_ID/comments\" -H 'Content-Type: application/json' -d '{\"agentId\":\"'\"$KANITEL_AGENT_ID\"'\",\"body\":\"your note\"}'");
        prompt.AppendLine("Move/change the task:");
        prompt.AppendLine("curl -s -X PATCH \"$KANITEL_API_URL/api/agent/tasks/$KANITEL_TASK_ID\" -H 'Content-Type: application/json' -d '{\"agentId\":\"'\"$KANITEL_AGENT_ID\"'\",\"columnName\":\"Review\",\"body\":\"Moved to review.\"}'");
        prompt.AppendLine("Assign or unassign an agent by PATCHing the same endpoint with assigneeAgentId or unassignAgent=true.");
        prompt.AppendLine();
        prompt.AppendLine("## Expected outcome");
        prompt.AppendLine("Work inside /workspace. If you change repositories, leave clear notes in your final response. Keep the response concise; Kanitel will attach it as an agent comment.");
        return TruncateForPrompt(prompt.ToString(), ReadInt("KANITEL_AGENT_PROMPT_MAX_CHARS", 120_000));
    }

    private static string TruncateForPrompt(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (maxChars <= 0 || value.Length <= maxChars)
        {
            return value;
        }

        var omitted = value.Length - maxChars;
        return $"{value[..maxChars]}\n\n[Kanitel truncated {omitted:n0} character(s) to keep the agent request small.]";
    }

    private static string AuthorLabel(
        TaskComment comment,
        IReadOnlyList<Person> people,
        IReadOnlyList<AgentProfile> agents)
    {
        return comment.AuthorType switch
        {
            "person" => $"{people.FirstOrDefault(person => person.Id == comment.AuthorId)?.DisplayName ?? comment.AuthorId} (person:{comment.AuthorId})",
            "agent" => $"{agents.FirstOrDefault(agent => agent.Id == comment.AuthorId)?.Name ?? comment.AuthorId} (agent:{comment.AuthorId})",
            "system" => $"System ({comment.AuthorId})",
            _ => $"{comment.AuthorType}:{comment.AuthorId}"
        };
    }

    private bool IsMockRunner()
    {
        return string.Equals(configuration["KANITEL_AGENT_RUNNER"], "mock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("KANITEL_AGENT_RUNNER"), "mock", StringComparison.OrdinalIgnoreCase);
    }

    private string ReadString(string key, string fallback)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private int ReadInt(string key, int fallback)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private bool ReadBool(string key, bool fallback)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisabled(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveHostEnv(string envName)
    {
        return string.IsNullOrWhiteSpace(envName)
            ? null
            : Environment.GetEnvironmentVariable(envName);
    }

    private static string? ResolveAgentApiKey(AgentProfile agent)
    {
        return ResolveHostEnv(agent.ApiKeySourceEnvName) ?? ResolveHostEnv(agent.ApiKeyEnvName);
    }

    private static void RemoveProviderSelectionFlags(Dictionary<string, string> env)
    {
        foreach (var key in ProviderSelectionEnvNames)
        {
            env.Remove(key);
        }
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.Equals("0", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("false", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadEnv(IReadOnlyDictionary<string, string> env, string key)
    {
        return env.TryGetValue(key, out var value) ? value : null;
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> env, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadEnv(env, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private async Task<int> InspectContainerExitCodeAsync(
        string containerName,
        int fallback,
        string workspace,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunProcessAsync(
                "docker",
                ["inspect", "--format", "{{.State.ExitCode}}", containerName],
                workspace,
                null,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            return result.ExitCode == 0 && int.TryParse(result.Log.Trim(), out var exitCode)
                ? exitCode
                : fallback;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return fallback;
        }
    }

    private async Task TryRemoveContainerAsync(
        string containerName,
        string workspace,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunProcessAsync(
                "docker",
                ["rm", "-f", containerName],
                workspace,
                null,
                TimeSpan.FromSeconds(30),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Best effort cleanup for a run-specific container name.
        }
    }

    private static string WorkspaceCopySource(string workspace)
    {
        return Path.Combine(workspace, ".");
    }

    private static readonly string[] KnownProviderEnvNames =
    [
        "ANTHROPIC_API_KEY",
        "OPENAI_API_KEY",
        "OPENAI_AUTH_HEADER",
        "OPENAI_AUTH_SCHEME",
        "OPENAI_AUTH_HEADER_VALUE",
        "CODEX_API_KEY",
        "CODEX_ACCOUNT_ID",
        "CHATGPT_ACCOUNT_ID",
        "GEMINI_API_KEY",
        "GOOGLE_API_KEY",
        "GEMINI_ACCESS_TOKEN",
        "GITHUB_TOKEN",
        "GH_TOKEN",
        "MISTRAL_API_KEY",
        "DEEPSEEK_API_KEY",
        "OPENROUTER_API_KEY",
        "GROQ_API_KEY",
        "TOGETHER_API_KEY",
        "NVIDIA_API_KEY",
        "MINIMAX_API_KEY",
        "MOONSHOT_API_KEY",
        "KIMI_API_KEY",
        "XAI_API_KEY",
        "HICAP_API_KEY",
        "DASHSCOPE_API_KEY",
        "AZURE_OPENAI_API_KEY",
        "BNKR_API_KEY",
        "AWS_BEARER_TOKEN_BEDROCK",
        "GOOGLE_APPLICATION_CREDENTIALS"
    ];

    private static readonly string[] ProviderSelectionEnvNames =
    [
        "CLAUDE_CODE_USE_OPENAI",
        "CLAUDE_CODE_USE_GEMINI",
        "CLAUDE_CODE_USE_MISTRAL",
        "CLAUDE_CODE_USE_GITHUB",
        "CLAUDE_CODE_USE_BEDROCK",
        "CLAUDE_CODE_USE_VERTEX",
        "CLAUDE_CODE_USE_FOUNDRY"
    ];

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        Dictionary<string, string>? environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ProcessResult(-1, $"Process timed out after {timeout.TotalMinutes:n0} minutes.");
        }

        var output = await stdoutTask;
        var error = await stderrTask;
        var combined = string.Join(Environment.NewLine, new[] { output, error }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ProcessResult(process.ExitCode, TrimLog(SanitizeProcessLog(combined)));
    }

    private static string SanitizeProcessLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var lines = value.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>();
        var repeated = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (ShouldDropProcessLogLine(line))
            {
                continue;
            }

            if (ShouldCollapseProcessLogLine(line))
            {
                repeated.TryGetValue(line, out var count);
                repeated[line] = count + 1;
                if (count == 0)
                {
                    output.Add(line);
                }
                continue;
            }

            output.Add(line);
        }

        foreach (var (line, count) in repeated)
        {
            if (count > 1)
            {
                output.Add($"[Kanitel] Previous line repeated {count - 1:n0} time(s): {line}");
            }
        }

        return string.Join(Environment.NewLine, output).Trim();
    }

    private static bool ShouldDropProcessLogLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("npm warn deprecated uuid@", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("npm notice", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("not in integration model metadata", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldCollapseProcessLogLine(string line)
    {
        return line.Contains("not in integration model metadata", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup; the scheduler will store the timeout result.
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "workspace" : normalized[..Math.Min(normalized.Length, 64)];
    }

    private static string TrimLog(string value)
    {
        const int maxLength = 24_000;
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[^maxLength..];
    }

    private sealed record ProcessResult(int ExitCode, string Log);
}
