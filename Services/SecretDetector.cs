using System.Text.RegularExpressions;
using SharpML2.Models;
using SharpML2.Utilities;

namespace SharpML2.Services;

public sealed partial class SecretDetector
{
    private sealed record MatchSpec(string Name, Regex Regex, string? FixedKey = null);

    private readonly IReadOnlyList<MatchSpec> _specs;

    public SecretDetector()
    {
        _specs = new MatchSpec[]
        {
            new("password_assignment", PasswordAssignmentRegex()),
            new("api_key_assignment", ApiKeyAssignmentRegex()),
            new("token_assignment", TokenAssignmentRegex()),
            new("connection_string_password", ConnectionStringPasswordRegex(), "password"),
            new("aws_access_key_id", AwsAccessKeyRegex(), "aws_access_key_id"),
            new("github_token", GithubTokenRegex(), "github_token"),
            new("private_key_header", PrivateKeyHeaderRegex(), "private_key")
        };
    }

    public IEnumerable<CandidateSecret> Detect(
        string filePath,
        FileInfo info,
        IReadOnlyList<string> lines,
        int contextRadius)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.Length > 20_000)
            {
                continue;
            }

            foreach (var spec in _specs)
            {
                foreach (Match match in spec.Regex.Matches(line))
                {
                    if (!match.Success)
                    {
                        continue;
                    }

                    var value = ExtractValue(match);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var key = spec.FixedKey ?? ExtractKey(match);
                    var context = new List<string>();
                    if (spec.Name == "private_key_header")
                    {
                        // Never include adjacent private-key body lines in context.
                        context.Add(ContextSanitizer.Sanitize(lines[i], value));
                    }
                    else
                    {
                        var start = Math.Max(0, i - contextRadius);
                        var end = Math.Min(lines.Count - 1, i + contextRadius);
                        for (var c = start; c <= end; c++)
                        {
                            context.Add(ContextSanitizer.Sanitize(lines[c], value));
                        }
                    }

                    yield return new CandidateSecret(
                        FilePath: filePath,
                        Extension: info.Extension.ToLowerInvariant(),
                        FileSizeBytes: info.Length,
                        LineNumber: i + 1,
                        Detector: spec.Name,
                        KeyName: key,
                        MaskedValue: SecretMath.Mask(value),
                        ValueFingerprint: SecretMath.Fingerprint(value),
                        ValueLength: value.Length,
                        Entropy: SecretMath.ShannonEntropy(value),
                        LooksPlaceholder: SecretMath.LooksPlaceholder(value),
                        Context: context,
                        LastWriteTimeUtc: info.LastWriteTimeUtc
                    );
                }
            }
        }
    }

    private static string ExtractValue(Match match)
    {
        if (match.Groups["value"].Success)
        {
            return match.Groups["value"].Value.Trim().Trim('"', '\'', '`');
        }

        return match.Value.Trim();
    }

    private static string? ExtractKey(Match match)
    {
        return match.Groups["key"].Success ? match.Groups["key"].Value : null;
    }

    [GeneratedRegex("""(?ix)["']?(?<key>[A-Za-z0-9_.-]*(?:password|passwd|pwd|passphrase))["']?\s*[:=]\s*["']?(?<value>[^\s"'#,;]{4,256})""")]
    private static partial Regex PasswordAssignmentRegex();

    [GeneratedRegex("""(?ix)["']?(?<key>[A-Za-z0-9_.-]*(?:api[_-]?key|apikey|client[_-]?secret|app[_-]?secret))["']?\s*[:=]\s*["']?(?<value>[A-Za-z0-9_\-./+=]{8,512})""")]
    private static partial Regex ApiKeyAssignmentRegex();

    [GeneratedRegex("""(?ix)["']?(?<key>[A-Za-z0-9_.-]*(?:access[_-]?token|auth[_-]?token|bearer[_-]?token|token))["']?\s*[:=]\s*["']?(?<value>[A-Za-z0-9_\-./+=]{10,1024})""")]
    private static partial Regex TokenAssignmentRegex();

    [GeneratedRegex("""(?ix)\b(?:password|pwd)\s*=\s*(?<value>[^;\s"']{4,256})""")]
    private static partial Regex ConnectionStringPasswordRegex();

    [GeneratedRegex("""\b(?<value>AKIA[0-9A-Z]{16})\b""")]
    private static partial Regex AwsAccessKeyRegex();

    [GeneratedRegex("""\b(?<value>gh[pousr]_[A-Za-z0-9]{20,255})\b""")]
    private static partial Regex GithubTokenRegex();

    [GeneratedRegex("""-----BEGIN (?<value>(?:RSA |EC |OPENSSH )?PRIVATE KEY)-----""")]
    private static partial Regex PrivateKeyHeaderRegex();
}
