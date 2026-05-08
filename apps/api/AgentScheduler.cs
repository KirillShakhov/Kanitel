using System.Text.Json;

namespace Kanitel.Api;

public sealed class AgentScheduler(
    JsonDataStore store,
    IAgentRunner runner,
    IConfiguration configuration,
    ILogger<AgentScheduler> logger) : BackgroundService
{
    private const string AgentActionMarker = "KANITEL_ACTION:";
    private static readonly JsonSerializerOptions AgentActionJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _scanGate = new(1, 1);

    public async Task<SchedulerTickResult> ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _scanGate.WaitAsync(0, cancellationToken))
        {
            return new SchedulerTickResult(0, "already-running");
        }

        try
        {
            var state = await store.SnapshotAsync(cancellationToken);
            var candidates = FindCandidates(state).ToList();
            foreach (var candidate in candidates)
            {
                _ = Task.Run(() => StartRunAsync(candidate, cancellationToken), CancellationToken.None);
            }

            return new SchedulerTickResult(candidates.Count, "ok");
        }
        finally
        {
            _scanGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(ReadInt("KANITEL_AGENT_POLL_INTERVAL_SECONDS", 20));
        logger.LogInformation("Kanitel agent scheduler started with {Interval}s interval", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler scan failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private IEnumerable<AgentCandidate> FindCandidates(KanitelState state)
    {
        foreach (var task in state.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.AssigneeAgentId))
            {
                continue;
            }

            var agent = state.Agents.FirstOrDefault(a => a.Id == task.AssigneeAgentId);
            if (agent is null || !agent.Enabled)
            {
                continue;
            }

            var hasProjectAccess = state.ProjectAgents.Any(pa => pa.ProjectId == task.ProjectId && pa.AgentId == agent.Id);
            if (!hasProjectAccess)
            {
                continue;
            }

            var lastComment = state.Comments
                .Where(c => c.TaskId == task.Id)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefault();
            if (lastComment is null)
            {
                continue;
            }

            if (lastComment.AuthorType == "system")
            {
                continue;
            }

            if (lastComment.AuthorType == "agent" && lastComment.AuthorId == agent.Id)
            {
                continue;
            }

            var hasRunForThisMessage = state.Runs.Any(r =>
                r.TaskId == task.Id &&
                r.AgentId == agent.Id &&
                r.TriggerCommentId == lastComment.Id);
            if (hasRunForThisMessage)
            {
                continue;
            }

            var hasActiveRun = state.Runs.Any(r =>
                r.TaskId == task.Id &&
                r.AgentId == agent.Id &&
                (r.Status == "queued" || r.Status == "running"));
            if (hasActiveRun)
            {
                continue;
            }

            var project = state.Projects.FirstOrDefault(p => p.Id == task.ProjectId);
            if (project is null)
            {
                continue;
            }

            yield return new AgentCandidate(project.Id, task.Id, agent.Id, lastComment.Id);
        }
    }

    private async Task StartRunAsync(AgentCandidate candidate, CancellationToken cancellationToken)
    {
        var run = await store.MutateAsync(state =>
        {
            var created = new AgentRun
            {
                ProjectId = candidate.ProjectId,
                TaskId = candidate.TaskId,
                AgentId = candidate.AgentId,
                TriggerCommentId = candidate.TriggerCommentId,
                Status = "running",
                StartedAt = DateTimeOffset.UtcNow
            };
            state.Runs.Add(created);
            return created;
        }, cancellationToken);

        try
        {
            var snapshot = await store.SnapshotAsync(cancellationToken);
            var project = snapshot.Projects.First(p => p.Id == candidate.ProjectId);
            var task = snapshot.Tasks.First(t => t.Id == candidate.TaskId);
            var agent = snapshot.Agents.First(a => a.Id == candidate.AgentId);
            var comments = snapshot.Comments.Where(c => c.TaskId == task.Id).OrderBy(c => c.CreatedAt).ToList();
            var columns = snapshot.Columns.Where(c => c.ProjectId == project.Id).OrderBy(c => c.Position).ToList();
            var repositories = snapshot.Repositories.Where(r => r.ProjectId == project.Id).ToList();

            var result = await runner.RunAsync(run, project, agent, task, comments, snapshot.People, snapshot.Agents, columns, repositories, cancellationToken);
            await CompleteRunAsync(run.Id, task.Id, agent.Id, result, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Agent run {RunId} crashed", run.Id);
            await store.MutateAsync(state =>
            {
                var storedRun = state.Runs.FirstOrDefault(r => r.Id == run.Id);
                if (storedRun is not null)
                {
                    storedRun.Status = "failed";
                    storedRun.Log = ex.ToString();
                    storedRun.FinishedAt = DateTimeOffset.UtcNow;
                }

                state.Comments.Add(new TaskComment
                {
                    TaskId = candidate.TaskId,
                    AuthorType = "system",
                    AuthorId = "scheduler",
                    Body = $"Agent run failed before completion: {ex.Message}"
                });
                return true;
            }, cancellationToken);
        }
    }

    private async Task CompleteRunAsync(
        string runId,
        string taskId,
        string agentId,
        AgentRunResult result,
        CancellationToken cancellationToken)
    {
        await store.MutateAsync(state =>
        {
            var storedRun = state.Runs.FirstOrDefault(r => r.Id == runId);
            if (storedRun is not null)
            {
                storedRun.Status = result.Success ? "succeeded" : "failed";
                storedRun.WorkspacePath = result.WorkspacePath;
                storedRun.Log = result.Log;
                storedRun.ExitCode = result.ExitCode;
                storedRun.FinishedAt = DateTimeOffset.UtcNow;
            }

            var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task is not null)
            {
                task.UpdatedAt = DateTimeOffset.UtcNow;
            }

            var outputAction = result.Success
                ? TryParseAgentOutputAction(result.Log) ?? TryInferAgentOutputAction(state, task, storedRun?.TriggerCommentId)
                : null;
            var actionApplied = task is not null &&
                outputAction is not null &&
                ApplyAgentOutputAction(state, task, agentId, outputAction);

            if (storedRun is not null && actionApplied)
            {
                storedRun.Log = $"{storedRun.Log}\n\nKanitel agent action applied.";
            }

            if (!actionApplied)
            {
                var commentBody = result.Success
                    ? $"Agent run completed.\n\n{Excerpt(result.Log)}"
                    : $"Agent run failed.\n\n{Excerpt(result.Log)}";

                state.Comments.Add(new TaskComment
                {
                    TaskId = taskId,
                    AuthorType = result.Success ? "agent" : "system",
                    AuthorId = result.Success ? agentId : "scheduler",
                    Body = commentBody
                });
            }

            return true;
        }, cancellationToken);
    }

    private static AgentOutputAction? TryParseAgentOutputAction(string log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return null;
        }

        var markerIndex = log.LastIndexOf(AgentActionMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var jsonText = ExtractFirstJsonObject(log[(markerIndex + AgentActionMarker.Length)..]);
        if (jsonText is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AgentOutputAction>(jsonText, AgentActionJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgentOutputAction? TryInferAgentOutputAction(
        KanitelState state,
        TaskCard? task,
        string? triggerCommentId)
    {
        if (task is null || string.IsNullOrWhiteSpace(triggerCommentId))
        {
            return null;
        }

        var trigger = state.Comments.FirstOrDefault(comment => comment.Id == triggerCommentId);
        if (trigger is null || !string.Equals(trigger.AuthorType, "person", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var text = trigger.Body.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text) ||
            text.Contains("не закры", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("don't close", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("do not close", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ContainsAny(text, "закрывай", "закрой", "закрыть задачу", "закрыть таск", "заверши", "завершай", "перенеси в done", "переведи в done", "move to done", "close task"))
        {
            return new AgentOutputAction
            {
                ColumnName = ResolveColumnName(state, task.ProjectId, "Done") ?? "Done",
                UnassignAgent = true,
                Body = "Done."
            };
        }

        if (ContainsAny(text, "на ревью", "в ревью", "на review", "в review", "перенеси в review", "переведи в review", "move to review"))
        {
            return new AgentOutputAction
            {
                ColumnName = ResolveColumnName(state, task.ProjectId, "Review") ?? "Review",
                Body = "Moved to review."
            };
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveColumnName(KanitelState state, string projectId, string name)
    {
        return state.Columns.FirstOrDefault(column =>
            column.ProjectId == projectId &&
            string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))?.Name;
    }

    private static string? ExtractFirstJsonObject(string value)
    {
        var start = value.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < value.Length; index++)
        {
            var current = value[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return value[start..(index + 1)];
                }
            }
        }

        return null;
    }

    private static bool ApplyAgentOutputAction(
        KanitelState state,
        TaskCard task,
        string agentId,
        AgentOutputAction action)
    {
        var previousColumnId = task.ColumnId;
        var previousTitle = task.Title;
        var previousDescription = task.Description;
        var previousStatus = DescribeColumn(state, task.ColumnId);
        var previousAssignee = DescribeAssignee(state, task.AssigneePersonId, task.AssigneeAgentId);

        if (!string.IsNullOrWhiteSpace(action.ColumnId) &&
            state.Columns.Any(column => column.Id == action.ColumnId && column.ProjectId == task.ProjectId))
        {
            task.ColumnId = action.ColumnId.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(action.ColumnName))
        {
            var column = state.Columns.FirstOrDefault(candidate =>
                candidate.ProjectId == task.ProjectId &&
                string.Equals(candidate.Name, action.ColumnName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (column is not null)
            {
                task.ColumnId = column.Id;
            }
        }

        if (action.UnassignAgent == true)
        {
            task.AssigneeAgentId = null;
        }
        else if (action.AssigneeAgentId is not null)
        {
            var assigneeAgentId = BlankToNull(action.AssigneeAgentId);
            if (assigneeAgentId is null || CanAgentActOnProject(state, task.ProjectId, assigneeAgentId))
            {
                task.AssigneeAgentId = assigneeAgentId;
            }
        }

        if (!string.IsNullOrWhiteSpace(action.Title))
        {
            task.Title = action.Title.Trim();
        }

        if (action.Description is not null)
        {
            task.Description = action.Description.Trim();
        }

        if (previousColumnId != task.ColumnId)
        {
            task.Position = state.Tasks
                .Where(candidate => candidate.ProjectId == task.ProjectId && candidate.ColumnId == task.ColumnId && candidate.Id != task.Id)
                .Select(candidate => candidate.Position)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            NormalizeTaskPositions(state, task.ProjectId, previousColumnId);
            NormalizeTaskPositions(state, task.ProjectId, task.ColumnId);
        }

        AddChange(state, task, agentId, "title", previousTitle, task.Title);
        AddChange(state, task, agentId, "description", previousDescription, task.Description);
        AddChange(state, task, agentId, "status", previousStatus, DescribeColumn(state, task.ColumnId));
        AddChange(state, task, agentId, "assignee", previousAssignee, DescribeAssignee(state, task.AssigneePersonId, task.AssigneeAgentId));

        var body = BlankToNull(action.Body) ?? "Updated task.";
        state.Comments.Add(new TaskComment
        {
            TaskId = task.Id,
            AuthorType = "agent",
            AuthorId = agentId,
            Body = body
        });

        task.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    private static bool CanAgentActOnProject(KanitelState state, string projectId, string agentId)
    {
        return state.Agents.Any(agent => agent.Id == agentId && agent.Enabled) &&
            state.ProjectAgents.Any(projectAgent => projectAgent.ProjectId == projectId && projectAgent.AgentId == agentId);
    }

    private static void AddChange(
        KanitelState state,
        TaskCard task,
        string agentId,
        string field,
        string? from,
        string? to)
    {
        var previous = from?.Trim() ?? "";
        var next = to?.Trim() ?? "";
        if (string.Equals(previous, next, StringComparison.Ordinal))
        {
            return;
        }

        state.History.Add(new TaskHistoryEntry
        {
            TaskId = task.Id,
            AuthorType = "agent",
            AuthorId = agentId,
            Action = "changed",
            Field = field,
            From = previous,
            To = next
        });
    }

    private static string DescribeColumn(KanitelState state, string columnId)
    {
        return state.Columns.FirstOrDefault(column => column.Id == columnId)?.Name ?? columnId;
    }

    private static string DescribeAssignee(KanitelState state, string? personId, string? agentId)
    {
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            return state.Agents.FirstOrDefault(agent => agent.Id == agentId)?.Name ?? agentId;
        }

        if (!string.IsNullOrWhiteSpace(personId))
        {
            return state.People.FirstOrDefault(person => person.Id == personId)?.DisplayName ?? personId;
        }

        return "";
    }

    private static void NormalizeTaskPositions(KanitelState state, string projectId, string columnId)
    {
        var ordered = state.Tasks
            .Where(task => task.ProjectId == projectId && task.ColumnId == columnId)
            .OrderBy(task => task.Position)
            .ThenBy(task => task.CreatedAt)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Position = index;
        }
    }

    private static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private int ReadInt(string key, int fallback)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static string Excerpt(string log)
    {
        const int maxLength = 4000;
        if (string.IsNullOrWhiteSpace(log))
        {
            return "No output.";
        }

        return log.Length <= maxLength ? log : log[^maxLength..];
    }

    private sealed record AgentCandidate(
        string ProjectId,
        string TaskId,
        string AgentId,
        string TriggerCommentId);

    private sealed class AgentOutputAction
    {
        public string? Body { get; set; }
        public string? ColumnId { get; set; }
        public string? ColumnName { get; set; }
        public string? AssigneeAgentId { get; set; }
        public bool? UnassignAgent { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
    }
}

public sealed record SchedulerTickResult(int QueuedRuns, string Status);
