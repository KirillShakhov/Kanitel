namespace Kanitel.Api;

using static Kanitel.Api.EndpointHelpers;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/projects/{projectId}/tasks", async (JsonDataStore store, string projectId, TaskRequest request, HttpRequest httpRequest, CancellationToken cancellationToken) =>
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
                    Position = state.Tasks.Where(t => t.ProjectId == projectId && t.ColumnId == columnId).Select(t => t.Position).DefaultIfEmpty(-1).Max() + 1
                };
                var actor = ResolveActor(state, httpRequest, request.AuthorType, request.AuthorId, "person", "owner");
                state.Tasks.Add(task);
                state.Comments.Add(new TaskComment
                {
                    TaskId = task.Id,
                    AuthorType = actor.Type,
                    AuthorId = actor.Id,
                    Body = string.IsNullOrWhiteSpace(request.InitialComment)
                        ? "Task created."
                        : request.InitialComment.Trim()
                });
                state.History.Add(new TaskHistoryEntry
                {
                    TaskId = task.Id,
                    AuthorType = actor.Type,
                    AuthorId = actor.Id,
                    Action = "created",
                    Field = "task",
                    To = task.Title
                });
                return task;
            }, cancellationToken);

            return created is null ? Results.BadRequest() : Results.Ok(created);
        })
            .WithTags("Tasks")
            .WithSummary("Create task")
            .WithDescription("Creates a task card in a project column, assigns it to either a person or an agent, and writes the initial conversation comment. Agent assignment can trigger the scheduler after a non-agent comment.");

        app.MapPatch("/api/tasks/{taskId}", async (JsonDataStore store, string taskId, TaskRequest request, HttpRequest httpRequest, CancellationToken cancellationToken) =>
        {
            var updated = await store.MutateAsync(state =>
            {
                var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
                if (task is null)
                {
                    return null;
                }

                var actor = ResolveActor(state, httpRequest, request.AuthorType, request.AuthorId, "system", "system");
                var previousColumnId = task.ColumnId;
                var previousTitle = task.Title;
                var previousDescription = task.Description;
                var previousStatus = DescribeColumn(state, task.ColumnId);
                var previousAssignee = DescribeAssignee(state, task.AssigneePersonId, task.AssigneeAgentId);
                if (!string.IsNullOrWhiteSpace(request.Title)) task.Title = request.Title.Trim();
                if (request.Description is not null) task.Description = request.Description.Trim();
                if (!string.IsNullOrWhiteSpace(request.ColumnId) && state.Columns.Any(c => c.Id == request.ColumnId && c.ProjectId == task.ProjectId))
                {
                    task.ColumnId = request.ColumnId;
                }
                if (request.AssigneeAgentId is not null) task.AssigneeAgentId = BlankToNull(request.AssigneeAgentId);
                if (request.AssigneePersonId is not null) task.AssigneePersonId = BlankToNull(request.AssigneePersonId);
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

                AddChange(state, task, actor, "title", previousTitle, task.Title);
                AddChange(state, task, actor, "description", previousDescription, task.Description);
                AddChange(state, task, actor, "status", previousStatus, DescribeColumn(state, task.ColumnId));
                AddChange(state, task, actor, "assignee", previousAssignee, DescribeAssignee(state, task.AssigneePersonId, task.AssigneeAgentId));
                task.UpdatedAt = DateTimeOffset.UtcNow;
                return task;
            }, cancellationToken);

            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
            .WithTags("Tasks")
            .WithSummary("Update task")
            .WithDescription("Updates title, description, status column, assignee, or position. Drag-and-drop uses this endpoint by changing `columnId` and `position`.");

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

                var actor = new HistoryActor("agent", request.AgentId.Trim());
                var previousColumnId = task.ColumnId;
                var previousTitle = task.Title;
                var previousDescription = task.Description;
                var previousStatus = DescribeColumn(state, task.ColumnId);
                var previousAssignee = DescribeAssignee(state, task.AssigneePersonId, task.AssigneeAgentId);

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

                if (!string.IsNullOrWhiteSpace(request.Title))
                {
                    task.Title = request.Title.Trim();
                }

                if (request.Description is not null)
                {
                    task.Description = request.Description.Trim();
                }

                if (previousColumnId != task.ColumnId)
                {
                    task.Position = state.Tasks
                        .Where(t => t.ProjectId == task.ProjectId && t.ColumnId == task.ColumnId && t.Id != task.Id)
                        .Select(t => t.Position)
                        .DefaultIfEmpty(-1)
                        .Max() + 1;
                    NormalizeTaskPositions(state, task.ProjectId, previousColumnId);
                    NormalizeTaskPositions(state, task.ProjectId, task.ColumnId);
                }

                AddChange(state, task, actor, "title", previousTitle, task.Title);
                AddChange(state, task, actor, "description", previousDescription, task.Description);
                AddChange(state, task, actor, "status", previousStatus, DescribeColumn(state, task.ColumnId));
                AddChange(state, task, actor, "assignee", previousAssignee, DescribeAssignee(state, task.AssigneePersonId, task.AssigneeAgentId));

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
            .WithDescription("Allows a linked agent to update a task: move it by column id or column name, add a comment body, reassign to another allowed agent, unassign itself, title, or description.");

        return app;
    }

    private sealed record HistoryActor(string Type, string Id);

    private static HistoryActor ResolveActor(
        KanitelState state,
        HttpRequest request,
        string? requestedType,
        string? requestedId,
        string fallbackType,
        string fallbackId)
    {
        var currentUser = FindCurrentUser(state, request);
        if (currentUser is not null)
        {
            return new HistoryActor("person", currentUser.Id);
        }

        return new HistoryActor(
            BlankToNull(requestedType) ?? fallbackType,
            BlankToNull(requestedId) ?? fallbackId);
    }

    private static void AddChange(
        KanitelState state,
        TaskCard task,
        HistoryActor actor,
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
            AuthorType = actor.Type,
            AuthorId = actor.Id,
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
}
