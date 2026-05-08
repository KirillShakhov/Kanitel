namespace Kanitel.Api;

using static Kanitel.Api.EndpointHelpers;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
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

        return app;
    }
}
