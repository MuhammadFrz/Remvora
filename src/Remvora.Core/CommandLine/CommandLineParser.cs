namespace Remvora.Core.CommandLine;

/// <summary>
/// Parsed command line containing the separated target executable and argument list.
/// </summary>
public sealed record ParsedCommandLine(string ExecutablePath, string Arguments);

/// <summary>
/// Safe Windows command-line parser that separates executable paths from arguments
/// without naïve string splitting, respecting quotes and spaces.
/// </summary>
public static class CommandLineParser
{
    public static ParsedCommandLine? Parse(string? rawCommandLine)
    {
        if (string.IsNullOrWhiteSpace(rawCommandLine))
            return null;

        var span = rawCommandLine.AsSpan().Trim();

        // 1. Quoted executable: "C:\Path with spaces\uninstall.exe" /arg1 /arg2
        if (span.StartsWith("\""))
        {
            var closingQuoteIndex = span[1..].IndexOf('\"');
            if (closingQuoteIndex >= 0)
            {
                var exe = span.Slice(1, closingQuoteIndex).ToString().Trim();
                var args = span[(closingQuoteIndex + 2)..].Trim().ToString();
                return new ParsedCommandLine(exe, args);
            }
        }

        // 2. Unquoted executable with arguments
        // Look for common executable extensions (.exe, .bat, .cmd)
        var exeEndIndex = -1;
        string[] extensions = [".exe", ".cmd", ".bat"];

        foreach (var ext in extensions)
        {
            var extIndex = span.IndexOf(ext.AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (extIndex >= 0)
            {
                exeEndIndex = extIndex + ext.Length;
                break;
            }
        }

        if (exeEndIndex > 0)
        {
            var exe = span[..exeEndIndex].ToString().Trim();
            var args = span[exeEndIndex..].Trim().ToString();
            return new ParsedCommandLine(exe, args);
        }

        // 3. Fallback: split on first space
        var firstSpace = span.IndexOf(' ');
        if (firstSpace >= 0)
        {
            var exe = span[..firstSpace].ToString().Trim();
            var args = span[(firstSpace + 1)..].Trim().ToString();
            return new ParsedCommandLine(exe, args);
        }

        return new ParsedCommandLine(span.ToString(), string.Empty);
    }
}
