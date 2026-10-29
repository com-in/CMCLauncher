using System.Text;

namespace CMCLauncher;

public static class LogTail
{
    // Bound disk reads before decoding; game logs can grow far beyond the report limit.
    public static string Read(string path, int maxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);
        if (maxCharacters == 0)
            return "";
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        stream.Seek(Math.Max(0, stream.Length - (maxCharacters * 4L + 4)), SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = reader.ReadToEnd();
        if (text.Length <= maxCharacters)
            return text;
        var start = text.Length - maxCharacters;
        if (char.IsLowSurrogate(text[start]))
            start++;
        return text[start..];
    }
}
