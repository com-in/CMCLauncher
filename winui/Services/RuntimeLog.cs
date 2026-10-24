using System.Text;

namespace CraftLauncher.WinUI;

static class RuntimeLog
{
    static readonly object Gate = new();
    static string FilePath => Path.Combine(AppContext.BaseDirectory, "logs", $"launcher-{DateTime.Now:yyyyMMdd}.log");

    public static void Write(string level, string message)
    {
        var clean = Redact(message).Replace("\r", " ").Replace("\n", "\\n");
        var line = $"[{DateTimeOffset.Now:O}] [{level}] {clean}{Environment.NewLine}";
        try { lock (Gate) { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.AppendAllText(FilePath, line, Encoding.UTF8); } }
        catch { /* Logging must never prevent launcher operation. */ }
    }

    public static string ReadTail(int maxCharacters = 250_000)
    {
        try
        {
            if (!File.Exists(FilePath)) return "(launcher log not found)";
            var text = File.ReadAllText(FilePath);
            return text.Length <= maxCharacters ? text : text[^maxCharacters..];
        }
        catch (Exception ex) { return "(cannot read launcher log: " + ex.Message + ")"; }
    }

    static string Redact(string value)
    {
        foreach (var key in new[] { "accessToken", "client_secret", "refresh_token", "authorization", "password" })
            value = System.Text.RegularExpressions.Regex.Replace(value, $"(?i)({key}[=: ]+)([^&\\s]+)", "$1[REDACTED]");
        return value;
    }
}
