namespace Kanitel.Api;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class EndpointHelpers
{
    internal static int ReadInt(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    internal static KanitelState SanitizeForClient(KanitelState state)
    {
        state.Accounts = [];
        foreach (var agent in state.Agents)
        {
            agent.ApiKeySourceEnvName = "";
        }

        return state;
    }

    internal static Person? FindCurrentUser(KanitelState state, HttpRequest request)
    {
        var token = ReadBearerToken(request);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var account = state.Accounts.FirstOrDefault(item => item.SessionToken == token);
        return account is null
            ? null
            : state.People.FirstOrDefault(item => item.Id == account.PersonId);
    }

    internal static string? ReadBearerToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        var token = request.Headers["X-Kanitel-Token"].ToString();
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    internal static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    internal static string NewToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    internal static string HashPassword(string password, string salt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static bool VerifyPassword(string password, string salt, string expectedHash)
    {
        var actual = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expectedHash));
    }

    internal static void NormalizeColumnPositions(KanitelState state, string projectId)
    {
        var ordered = state.Columns
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.Position)
            .ThenBy(c => c.Name)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Position = i;
        }
    }

    internal static void NormalizeTaskPositions(KanitelState state, string projectId, string columnId)
    {
        var ordered = state.Tasks
            .Where(task => task.ProjectId == projectId && task.ColumnId == columnId)
            .OrderBy(task => task.Position)
            .ThenBy(task => task.UpdatedAt)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Position = i;
        }
    }

    internal static string InferAuthMode(string url)
    {
        return url.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
            ? "ssh"
            : "http";
    }

    internal static Dictionary<string, string> MergeEnvironment(
        Dictionary<string, string> first,
        Dictionary<string, string>? second)
    {
        var merged = new Dictionary<string, string>(first, StringComparer.OrdinalIgnoreCase);
        if (second is null)
        {
            return merged;
        }

        foreach (var pair in second)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                merged[pair.Key.Trim()] = pair.Value;
            }
        }

        return merged;
    }

    internal static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static bool CanAgentActOnProject(KanitelState state, string projectId, string agentId)
    {
        var normalizedAgentId = agentId.Trim();
        return state.Agents.Any(agent => agent.Id == normalizedAgentId && agent.Enabled) &&
            state.ProjectAgents.Any(projectAgent =>
                projectAgent.ProjectId == projectId &&
                projectAgent.AgentId == normalizedAgentId);
    }
}
