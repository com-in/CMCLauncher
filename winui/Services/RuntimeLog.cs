using System.Text;

namespace CMCLauncher.WinUI;

// 按天写入程序目录；磁盘不可写时跳过日志，不能拖垮启动器操作。
static class RuntimeLog
{
    static readonly object Gate = new();
    public static bool DebugEnabled { get; set; }
    public static event Action<string>? Written;

    public static void Debug(string message)
    {
        if (DebugEnabled)
            Write("DEBUG", message);
    }

    static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "logs", $"launcher-{DateTime.Now:yyyyMMdd}.log");

    // 脱敏后再落盘，把消息中的换行转义，保持一条事件对应一行。
    public static void Write(string level, string message)
    {
        var clean = Redact(message).Replace("\r", " ").Replace("\n", "\\n");
        var line = $"[{DateTimeOffset.Now:O}] [{level}] {clean}{Environment.NewLine}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        { /* Logging must never prevent launcher operation. */
        }
        try
        {
            Written?.Invoke(line.TrimEnd());
        }
        catch
        { /* A closed UI must not interrupt logging. */
        }
    }

    public static string ReadTail(int maxCharacters = 250_000)
    {
        try
        {
            if (!File.Exists(FilePath))
                return "(launcher log not found)";
            var text = File.ReadAllText(FilePath);
            return text.Length <= maxCharacters ? text : text[^maxCharacters..];
        }
        catch (Exception ex)
        {
            return "(cannot read launcher log: " + ex.Message + ")";
        }
    }

    // 过滤常见凭据字段；调用方输出游戏命令时仍需自行隐藏令牌参数。
    static string Redact(string value)
    {
        value = System.Text.RegularExpressions.Regex.Replace(
            value,
            @"(?i)\bBearer\s+[^\s""'&,}]+",
            "Bearer [REDACTED]"
        );
        foreach (
            var key in new[]
            {
                "accessToken",
                "access_token",
                "client_secret",
                "refresh_token",
                "id_token",
                "device_code",
                "authorization",
                "password",
            }
        )
            value = System.Text.RegularExpressions.Regex.Replace(
                value,
                $"(?i)({key}[\"']?\\s*[=: ]+\\s*[\"']?)([^&\\s\"',}}]+)",
                "$1[REDACTED]"
            );
        return value;
    }
}
