using System.Security.Cryptography;
using System.Text;

namespace LocalCompanion.Services;

/// <summary>MCP の窓口が受け取ってよい文と、履歴の置き場所です。</summary>
public static class McpDoorPolicy
{
    public const int MaxMessageChars = 4000;

    public const string SessionPrefix = "mcp-door-";

    public const string SessionTitle = "MCP";

    public static string? Refusal(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "The message is empty.";

        var text = message.Trim();
        if (text.Length > MaxMessageChars)
            return "The message is too long.";

        if (ChatExportRequestParser.TryParse(text, out _))
            return "This door does not write files.";

        if (CharacterSelfImproveIntent.LooksLikePersonaUpdateRequest(text))
            return "This door does not change character settings.";

        return null;
    }

    public static string SessionIdFor(string presetKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(presetKey)))
            .ToLowerInvariant();
        return SessionPrefix + hash[..16];
    }

    public static bool IsDoorSession(string? sessionId) =>
        !string.IsNullOrEmpty(sessionId)
        && sessionId.StartsWith(SessionPrefix, StringComparison.Ordinal);
}

public sealed class McpDoorException : Exception
{
    public McpDoorException(string message) : base(message)
    {
    }
}
