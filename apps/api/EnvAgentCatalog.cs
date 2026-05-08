namespace Kanitel.Api;

internal static class EnvAgentCatalog
{
    private const int MaxAgentCount = 100;
    private const int MaxEnvironmentRows = 100;
    private const string DefaultCommandTemplate = "npx -y @gitlawb/openclaude@latest --print \"$(cat \\\"$KANITEL_TASK_PROMPT_FILE\\\")\"";

    public static List<AgentProfile> Read(IConfiguration configuration)
    {
        var agents = new List<AgentProfile>();
        for (var index = 0; index < MaxAgentCount; index++)
        {
            var prefix = $"AGENT_{index}_";
            if (!HasAgentBlock(configuration, prefix))
            {
                continue;
            }

            var providerName = Read(configuration, $"{prefix}PROVIDER");
            var preset = FindPreset(providerName)
                ?? OpenClaudeCatalog.ProviderPresets.First(preset => preset.Id == "custom");

            var templateId = Read(configuration, $"{prefix}TEMPLATE") ?? "general-purpose";
            var template = OpenClaudeCatalog.AgentTemplates.FirstOrDefault(item =>
                    string.Equals(item.Id, templateId, StringComparison.OrdinalIgnoreCase))
                ?? OpenClaudeCatalog.AgentTemplates.First(item => item.Id == "general-purpose");

            var name = Read(configuration, $"{prefix}NAME") ?? $"{preset.Name} Agent";
            var baseUrl = Read(configuration, $"{prefix}API_URL")
                ?? Read(configuration, $"{prefix}BASE_URL")
                ?? preset.BaseUrl;
            var model = Read(configuration, $"{prefix}MODEL") ?? preset.DefaultModel;
            var apiKeyEnvName = Read(configuration, $"{prefix}API_KEY_ENV") ?? preset.ApiKeyEnvName;
            var apiKeySourceEnvName = string.IsNullOrWhiteSpace(Read(configuration, $"{prefix}API_KEY"))
                ? ""
                : $"{prefix}API_KEY";
            var environment = BuildEnvironment(configuration, prefix, preset, model, baseUrl);

            agents.Add(new AgentProfile
            {
                Name = name,
                AgentType = template.AgentType,
                AvatarUrl = Read(configuration, $"{prefix}AVATAR_URL") ?? preset.LogoUrl,
                ProviderPresetId = preset.Id,
                Provider = string.IsNullOrWhiteSpace(providerName) ? preset.Provider : providerName.Trim(),
                BaseUrl = baseUrl,
                Model = model,
                ApiKeyEnvName = apiKeyEnvName,
                ApiKeySourceEnvName = apiKeySourceEnvName,
                ContainerImage = Read(configuration, $"{prefix}CONTAINER_IMAGE") ?? "node:22-bookworm",
                CommandTemplate = Read(configuration, $"{prefix}COMMAND") ?? DefaultCommandTemplate,
                SystemPrompt = Read(configuration, $"{prefix}SYSTEM_PROMPT") ?? template.SystemPrompt,
                Enabled = ReadBool(configuration, $"{prefix}ENABLED", true),
                ToolTags = template.ToolTags.ToList(),
                Environment = environment
            });
        }

        return agents;
    }

    private static Dictionary<string, string> BuildEnvironment(
        IConfiguration configuration,
        string prefix,
        ProviderPreset preset,
        string model,
        string baseUrl)
    {
        var environment = new Dictionary<string, string>(preset.Environment, StringComparer.OrdinalIgnoreCase);
        foreach (var key in environment.Keys.ToList())
        {
            if (key.EndsWith("_MODEL", StringComparison.OrdinalIgnoreCase))
            {
                environment[key] = model;
            }
        }

        if (environment.ContainsKey("OPENAI_BASE_URL"))
        {
            environment["OPENAI_BASE_URL"] = baseUrl;
        }

        if (environment.ContainsKey("OPENAI_MODEL"))
        {
            environment["OPENAI_MODEL"] = model;
        }

        for (var index = 0; index < MaxEnvironmentRows; index++)
        {
            var key = Read(configuration, $"{prefix}ENV_{index}_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            environment[key] = Read(configuration, $"{prefix}ENV_{index}_VALUE") ?? "";
        }

        return environment;
    }

    private static bool HasAgentBlock(IConfiguration configuration, string prefix)
    {
        var keys = new[]
        {
            "NAME",
            "PROVIDER",
            "API_URL",
            "BASE_URL",
            "API_KEY",
            "API_KEY_ENV",
            "MODEL",
            "AVATAR_URL",
            "COMMAND",
            "SYSTEM_PROMPT"
        };

        return keys.Any(key => !string.IsNullOrWhiteSpace(Read(configuration, $"{prefix}{key}")));
    }

    private static ProviderPreset? FindPreset(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        return OpenClaudeCatalog.ProviderPresets.FirstOrDefault(preset =>
            string.Equals(preset.Id, providerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preset.Provider, providerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preset.Name, providerName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Read(IConfiguration configuration, string key)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ReadBool(IConfiguration configuration, string key, bool fallback)
    {
        var value = Read(configuration, key);
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
