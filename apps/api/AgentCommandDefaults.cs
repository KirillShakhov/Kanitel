namespace Kanitel.Api;

internal static class AgentCommandDefaults
{
    public const string CommandTemplate = "npx --yes --quiet --loglevel=error @gitlawb/openclaude@latest --print \"$(cat \"$KANITEL_TASK_PROMPT_FILE\")\"";

    private const string LegacyCommandTemplate = "npx -y @gitlawb/openclaude@latest --print \"$(cat \"$KANITEL_TASK_PROMPT_FILE\")\"";
    private const string EscapedPromptFile = "\\\"$KANITEL_TASK_PROMPT_FILE\\\"";
    private const string QuotedPromptFile = "\"$KANITEL_TASK_PROMPT_FILE\"";

    public static string Normalize(string? commandTemplate)
    {
        var command = string.IsNullOrWhiteSpace(commandTemplate)
            ? CommandTemplate
            : commandTemplate.Trim();

        command = command.Replace(EscapedPromptFile, QuotedPromptFile, StringComparison.Ordinal);
        return string.Equals(command, LegacyCommandTemplate, StringComparison.Ordinal)
            ? CommandTemplate
            : command;
    }
}
