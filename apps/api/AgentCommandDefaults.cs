namespace Kanitel.Api;

internal static class AgentCommandDefaults
{
    public const string CommandTemplate = "npx --yes --quiet --loglevel=error opencode-ai@latest run --pure --model \"$KANITEL_OPENCODE_MODEL\" \"$(cat \"$KANITEL_TASK_PROMPT_FILE\")\"";

    private const string LegacyCommandTemplate = "npx -y @gitlawb/openclaude@latest --print \"$(cat \"$KANITEL_TASK_PROMPT_FILE\")\"";
    private const string QuietCommandTemplate = "npx --yes --quiet --loglevel=error @gitlawb/openclaude@latest --print \"$(cat \"$KANITEL_TASK_PROMPT_FILE\")\"";
    private const string BareCommandTemplate = "npx --yes --quiet --loglevel=error @gitlawb/openclaude@latest --bare --print \"$(cat \"$KANITEL_TASK_PROMPT_FILE\")\"";
    private const string EscapedPromptFile = "\\\"$KANITEL_TASK_PROMPT_FILE\\\"";
    private const string QuotedPromptFile = "\"$KANITEL_TASK_PROMPT_FILE\"";

    public static string Normalize(string? commandTemplate)
    {
        var command = string.IsNullOrWhiteSpace(commandTemplate)
            ? CommandTemplate
            : commandTemplate.Trim();

        command = command.Replace(EscapedPromptFile, QuotedPromptFile, StringComparison.Ordinal);

        if (command.Contains("@gitlawb/openclaude", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("KANITEL_TASK_PROMPT_FILE", StringComparison.Ordinal))
        {
            return CommandTemplate;
        }

        return string.Equals(command, LegacyCommandTemplate, StringComparison.Ordinal) ||
            string.Equals(command, QuietCommandTemplate, StringComparison.Ordinal) ||
            string.Equals(command, BareCommandTemplate, StringComparison.Ordinal)
            ? CommandTemplate
            : command;
    }
}
