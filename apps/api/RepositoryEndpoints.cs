namespace Kanitel.Api;

using static Kanitel.Api.EndpointHelpers;

public static class RepositoryEndpoints
{
    public static IEndpointRouteBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder app)
    {
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

        return app;
    }
}
