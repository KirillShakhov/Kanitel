namespace Kanitel.Api;

public static class OpenClaudeCatalog
{
    private const string LogoPrefix = "https://www.google.com/s2/favicons?sz=128&domain=";

    // Provider presets are copied/adapted from openclaude integration descriptors.
    // Local endpoints use host.docker.internal because Kanitel launches agents in child containers.
    public static IReadOnlyList<ProviderPreset> ProviderPresets { get; } =
    [
        new()
        {
            Id = "codex",
            Name = "Codex / OpenClaude",
            Provider = "codex",
            BaseUrl = "https://chatgpt.com/backend-api/codex",
            DefaultModel = "codexplan",
            ApiKeyEnvName = "CODEX_API_KEY",
            Transport = "codex-responses",
            LogoUrl = $"{LogoPrefix}chatgpt.com",
            Environment = new()
            {
                ["OPENAI_MODEL"] = "codexplan",
                ["OPENAI_BASE_URL"] = "https://chatgpt.com/backend-api/codex"
            }
        },
        new()
        {
            Id = "anthropic",
            Name = "Anthropic",
            Provider = "anthropic",
            BaseUrl = "https://api.anthropic.com",
            DefaultModel = "claude-sonnet-4-6",
            ApiKeyEnvName = "ANTHROPIC_API_KEY",
            Transport = "anthropic",
            LogoUrl = $"{LogoPrefix}anthropic.com",
            Environment = new() { ["ANTHROPIC_MODEL"] = "claude-sonnet-4-6" }
        },
        new()
        {
            Id = "openai",
            Name = "OpenAI",
            Provider = "openai",
            BaseUrl = "https://api.openai.com/v1",
            DefaultModel = "gpt-5.4",
            ApiKeyEnvName = "OPENAI_API_KEY",
            LogoUrl = $"{LogoPrefix}openai.com",
            Environment = new()
            {
                ["CLAUDE_CODE_USE_OPENAI"] = "1",
                ["OPENAI_MODEL"] = "gpt-5.4"
            }
        },
        new()
        {
            Id = "gemini",
            Name = "Google Gemini",
            Provider = "gemini",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            DefaultModel = "gemini-3-flash-preview",
            ApiKeyEnvName = "GEMINI_API_KEY",
            Transport = "gemini",
            LogoUrl = $"{LogoPrefix}ai.google.dev",
            Environment = new()
            {
                ["CLAUDE_CODE_USE_GEMINI"] = "1",
                ["GEMINI_MODEL"] = "gemini-3-flash-preview"
            }
        },
        new()
        {
            Id = "github",
            Name = "GitHub Models / Copilot",
            Provider = "github",
            BaseUrl = "https://api.githubcopilot.com",
            DefaultModel = "github:copilot",
            ApiKeyEnvName = "GITHUB_TOKEN",
            Transport = "github",
            LogoUrl = $"{LogoPrefix}github.com",
            Environment = new()
            {
                ["CLAUDE_CODE_USE_GITHUB"] = "1",
                ["OPENAI_MODEL"] = "github:copilot"
            }
        },
        new()
        {
            Id = "ollama",
            Name = "Ollama",
            Provider = "ollama",
            BaseUrl = "http://host.docker.internal:11434/v1",
            DefaultModel = "llama3.1:8b",
            ApiKeyEnvName = "OPENAI_API_KEY",
            RequiresApiKey = false,
            LogoUrl = $"{LogoPrefix}ollama.com",
            Environment = new()
            {
                ["CLAUDE_CODE_USE_OPENAI"] = "1",
                ["OPENAI_API_KEY"] = "ollama",
                ["OPENAI_MODEL"] = "llama3.1:8b"
            }
        },
        new()
        {
            Id = "lmstudio",
            Name = "LM Studio",
            Provider = "lmstudio",
            BaseUrl = "http://host.docker.internal:1234/v1",
            DefaultModel = "local-model",
            ApiKeyEnvName = "OPENAI_API_KEY",
            RequiresApiKey = false,
            LogoUrl = $"{LogoPrefix}lmstudio.ai",
            Environment = new()
            {
                ["CLAUDE_CODE_USE_OPENAI"] = "1",
                ["OPENAI_API_KEY"] = "lmstudio",
                ["OPENAI_MODEL"] = "local-model"
            }
        },
        new()
        {
            Id = "bedrock",
            Name = "AWS Bedrock",
            Provider = "bedrock",
            BaseUrl = "https://bedrock-runtime.us-east-1.amazonaws.com",
            DefaultModel = "claude-sonnet-4-6",
            ApiKeyEnvName = "AWS_BEARER_TOKEN_BEDROCK",
            Transport = "bedrock",
            LogoUrl = $"{LogoPrefix}aws.amazon.com",
            Environment = new()
            {
                ["CLAUDE_CODE_USE_BEDROCK"] = "1",
                ["AWS_REGION"] = "us-east-1",
                ["AWS_DEFAULT_REGION"] = "us-east-1"
            }
        },
        new()
        {
            Id = "vertex",
            Name = "Google Vertex AI",
            Provider = "vertex",
            BaseUrl = "https://us-east5-aiplatform.googleapis.com",
            DefaultModel = "claude-sonnet-4-6",
            ApiKeyEnvName = "GOOGLE_APPLICATION_CREDENTIALS",
            Transport = "vertex",
            LogoUrl = $"{LogoPrefix}cloud.google.com",
            Environment = new()
            {
                ["CLAUDE_CODE_USE_VERTEX"] = "1",
                ["CLOUD_ML_REGION"] = "us-east5"
            }
        },
        OpenAiCompatible("dashscope-cn", "DashScope China", "https://coding.dashscope.aliyuncs.com/v1", "qwen3.6-plus", "DASHSCOPE_API_KEY", logoDomain: "aliyun.com"),
        OpenAiCompatible("dashscope-intl", "DashScope International", "https://coding-intl.dashscope.aliyuncs.com/v1", "qwen3.6-plus", "DASHSCOPE_API_KEY", logoDomain: "aliyun.com"),
        OpenAiCompatible("azure-openai", "Azure OpenAI", "https://YOUR-RESOURCE-NAME.openai.azure.com/openai/v1", "YOUR-DEPLOYMENT-NAME", "AZURE_OPENAI_API_KEY", logoDomain: "azure.microsoft.com"),
        OpenAiCompatible("bankr", "Bankr LLM Gateway", "https://llm.bankr.bot/v1", "claude-opus-4.6", "BNKR_API_KEY", logoDomain: "bankr.bot"),
        OpenAiCompatible("deepseek", "DeepSeek", "https://api.deepseek.com/v1", "deepseek-v4-pro", "DEEPSEEK_API_KEY", logoDomain: "deepseek.com"),
        OpenAiCompatible("mistral", "Mistral", "https://api.mistral.ai/v1", "devstral-latest", "MISTRAL_API_KEY", "mistral", "mistral.ai"),
        OpenAiCompatible("nvidia-nim", "NVIDIA NIM", "https://integrate.api.nvidia.com/v1", "nvidia/llama-3.1-nemotron-70b-instruct", "NVIDIA_API_KEY"),
        OpenAiCompatible("minimax", "MiniMax", "https://api.minimax.io/v1", "MiniMax-M2.7", "MINIMAX_API_KEY", logoDomain: "minimax.io"),
        OpenAiCompatible("moonshotai", "Moonshot AI API", "https://api.moonshot.ai/v1", "kimi-k2.5", "MOONSHOT_API_KEY", logoDomain: "moonshot.ai"),
        OpenAiCompatible("kimi-code", "Kimi Code", "https://api.kimi.com/coding/v1", "kimi-for-coding", "KIMI_API_KEY", logoDomain: "kimi.com"),
        OpenAiCompatible("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-5-mini", "OPENROUTER_API_KEY"),
        OpenAiCompatible("groq", "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", "GROQ_API_KEY"),
        OpenAiCompatible("together", "Together AI", "https://api.together.xyz/v1", "Qwen/Qwen3.5-9B", "TOGETHER_API_KEY"),
        OpenAiCompatible("atomic-chat", "Atomic Chat", "http://host.docker.internal:11888/v1", "local-model", "OPENAI_API_KEY", requiresApiKey: false, logoDomain: "atomic.chat"),
        OpenAiCompatible("zai", "Z.AI GLM Coding", "https://api.z.ai/api/coding/paas/v4", "GLM-5.1", "OPENAI_API_KEY", logoDomain: "z.ai"),
        OpenAiCompatible("xai", "xAI", "https://api.x.ai/v1", "grok-4", "XAI_API_KEY"),
        OpenAiCompatible("hicap", "Hicap", "https://api.hicap.ai/v1", "claude-opus-4.7", "HICAP_API_KEY"),
        OpenAiCompatible("custom", "Custom OpenAI-compatible", "http://host.docker.internal:11434/v1", "custom-model", "OPENAI_API_KEY")
    ];

    public static IReadOnlyList<AgentTemplate> AgentTemplates { get; } =
    [
        new()
        {
            Id = "general-purpose",
            Name = "General Purpose",
            AgentType = "general-purpose",
            WhenToUse = "Research, code search, implementation, and multi-step work.",
            ToolTags = ["*"],
            SystemPrompt = "You are a practical coding agent. Complete the assigned task fully, keep changes scoped, and report only the useful outcome."
        },
        new()
        {
            Id = "explore",
            Name = "Explore",
            AgentType = "explore",
            WhenToUse = "Explore repositories, map architecture, and answer focused codebase questions.",
            ToolTags = ["read", "search"],
            SystemPrompt = "You are an exploration agent. Search broadly, read the relevant files, and return concise findings with file paths."
        },
        new()
        {
            Id = "plan",
            Name = "Plan",
            AgentType = "plan",
            WhenToUse = "Break ambiguous work into a concrete implementation plan.",
            ToolTags = ["read", "search"],
            SystemPrompt = "You are a planning agent. Produce a short implementation plan, risks, and validation steps before code changes."
        },
        new()
        {
            Id = "verification",
            Name = "Verification",
            AgentType = "verification",
            WhenToUse = "Run checks, inspect diffs, and look for regressions after implementation.",
            ToolTags = ["test", "read", "search"],
            SystemPrompt = "You are a verification agent. Focus on bugs, failing tests, missing coverage, and concrete evidence."
        },
        new()
        {
            Id = "claude-code-guide",
            Name = "Claude Code Guide",
            AgentType = "claude-code-guide",
            WhenToUse = "Help with OpenClaude or Claude Code style workflows and provider setup.",
            ToolTags = ["read", "search"],
            SystemPrompt = "You are an OpenClaude workflow guide. Help configure providers, agent routing, and coding-agent practices."
        },
        new()
        {
            Id = "statusline-setup",
            Name = "Statusline Setup",
            AgentType = "statusline-setup",
            WhenToUse = "Configure lightweight developer status surfaces and automation feedback.",
            ToolTags = ["read", "edit"],
            SystemPrompt = "You are a setup agent. Make small, reversible configuration changes and explain exactly what changed."
        }
    ];

    private static ProviderPreset OpenAiCompatible(
        string id,
        string name,
        string baseUrl,
        string model,
        string apiKeyEnvName,
        string provider = "openai",
        string? logoDomain = null,
        bool requiresApiKey = true)
    {
        return new ProviderPreset
        {
            Id = id,
            Name = name,
            Provider = provider,
            BaseUrl = baseUrl,
            DefaultModel = model,
            ApiKeyEnvName = apiKeyEnvName,
            RequiresApiKey = requiresApiKey,
            LogoUrl = $"{LogoPrefix}{logoDomain ?? id + ".com"}",
            Environment = new()
            {
                ["CLAUDE_CODE_USE_OPENAI"] = "1",
                ["OPENAI_BASE_URL"] = baseUrl,
                ["OPENAI_MODEL"] = model
            }
        };
    }
}
