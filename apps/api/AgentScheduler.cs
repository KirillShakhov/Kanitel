namespace Kanitel.Api;

public sealed class AgentScheduler(
    JsonDataStore store,
    IAgentRunner runner,
    IConfiguration configuration,
    ILogger<AgentScheduler> logger) : BackgroundService
{
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

            return true;
        }, cancellationToken);
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
}

public sealed record SchedulerTickResult(int QueuedRuns, string Status);
