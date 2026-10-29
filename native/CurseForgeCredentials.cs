using System.Text.Json.Nodes;

namespace CMCLauncher;

public static class CurseForgeCredentials
{
    static readonly Lazy<string?> Key = new(ReadEmbeddedKey);
    public static string? ConfiguredApiKey => Key.Value;

    public static string RequireApiKey() =>
        ConfiguredApiKey
        ?? throw new InvalidOperationException(
            "CurseForge API key is not configured in this build."
        );

    static string? ReadEmbeddedKey()
    {
        using var stream = typeof(CurseForgeCredentials).Assembly.GetManifestResourceStream(
            "CMCLauncher.CurseForgeApi"
        );
        if (stream == null)
            return null;
        try
        {
            var key = JsonNode.Parse(stream)?["apiKey"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch
        {
            throw new InvalidDataException("Invalid embedded CurseForge API configuration.");
        }
    }
}
