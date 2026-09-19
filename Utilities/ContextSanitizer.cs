using System.Text.RegularExpressions;

namespace SharpML2.Utilities;

public static partial class ContextSanitizer
{
    [GeneratedRegex("""(?ix)["']?([A-Za-z0-9_.-]*(?:password|passwd|pwd|passphrase|secret|token|api[_-]?key|access[_-]?key|client[_-]?secret))["']?\s*[:=]\s*["']?([^\s,;"']+)""")]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"(?i)(://[^:/\s]+:)([^@/\s]+)(@)")]
    private static partial Regex UriCredentialRegex();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKeyRegex();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{20,255}\b")]
    private static partial Regex GithubTokenRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b")]
    private static partial Regex JwtRegex();

    public static string Sanitize(string line, string? exactCandidate = null)
    {
        var sanitized = line;
        if (!string.IsNullOrEmpty(exactCandidate))
        {
            sanitized = sanitized.Replace(exactCandidate, "<REDACTED>", StringComparison.Ordinal);
        }

        sanitized = KeyValueRegex().Replace(sanitized, m => $"{m.Groups[1].Value}=<REDACTED>");
        sanitized = UriCredentialRegex().Replace(sanitized, "$1<REDACTED>$3");
        sanitized = AwsAccessKeyRegex().Replace(sanitized, "<REDACTED:aws-access-key>");
        sanitized = GithubTokenRegex().Replace(sanitized, "<REDACTED:github-token>");
        sanitized = JwtRegex().Replace(sanitized, "<REDACTED:jwt>");

        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
