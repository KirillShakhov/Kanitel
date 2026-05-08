namespace Kanitel.Api;

using static Kanitel.Api.EndpointHelpers;

public static class ParticipantEndpoints
{
    public static IEndpointRouteBuilder MapParticipantEndpoints(this IEndpointRouteBuilder app)
    {
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
                if (state.Projects.All(p => p.Id != projectId) || string.IsNullOrWhiteSpace(request.PersonId))
                {
                    return null;
                }

                var personId = request.PersonId.Trim();
                var person = state.People.FirstOrDefault(p => p.Id == personId);
                if (person is null)
                {
                    return null;
                }

                var existing = state.Members.FirstOrDefault(m => m.ProjectId == projectId && m.PersonId == person.Id);
                if (existing is not null)
                {
                    return new { person, member = existing };
                }

                var member = new ProjectMember
                {
                    ProjectId = projectId,
                    PersonId = person.Id
                };
                state.Members.Add(member);
                return new { person, member };
            }, cancellationToken);

            return result is null ? Results.BadRequest() : Results.Ok(result);
        })
            .WithTags("Participants")
            .WithSummary("Add project person")
            .WithDescription("Adds an existing registered person to a project. If the person is already a member, the existing participant link is returned.");

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
                    return existing;
                }

                var projectAgent = new ProjectAgent
                {
                    ProjectId = projectId,
                    AgentId = request.AgentId.Trim()
                };
                state.ProjectAgents.Add(projectAgent);
                return projectAgent;
            }, cancellationToken);

            return linked is null ? Results.BadRequest() : Results.Ok(linked);
        })
            .WithTags("Participants")
            .WithSummary("Add project agent")
            .WithDescription("Adds a global agent to a project as a participant. Project agents can be assigned tasks like people.");

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

        return app;
    }
}
