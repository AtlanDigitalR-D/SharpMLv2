using SharpML2.Models;

namespace SharpML2.Services;

public sealed class HeuristicClassifier : ISecretClassifier
{
    public Task<ClassificationResult> ClassifyAsync(CandidateSecret candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var type = candidate.Detector switch
        {
            "password_assignment" or "connection_string_password" => "password",
            "api_key_assignment" or "aws_access_key_id" => "api_key",
            "token_assignment" or "github_token" => "token",
            "private_key_header" => "private_key",
            _ => "uncertain"
        };

        var real = 0.45;
        if (candidate.Detector is "aws_access_key_id" or "github_token" or "private_key_header") real += 0.30;
        if (candidate.Entropy >= 3.5) real += 0.12;
        if (candidate.ValueLength >= 20) real += 0.08;
        if (candidate.LooksPlaceholder) real -= 0.55;

        var path = candidate.FilePath.ToLowerInvariant();
        if (ContainsAny(path, "prod", "production", "deploy", "terraform", "ansible", "finance", "config")) real += 0.10;
        if (ContainsAny(path, "example", "sample", "test", "fixture", "docs", "documentation")) real -= 0.22;

        real = Math.Clamp(real, 0.01, 0.99);

        var priority = real * 4.0;
        if (ContainsAny(path, "prod", "production", "finance", "domain", "admin")) priority += 0.5;
        if (candidate.LooksPlaceholder) priority -= 0.8;
        priority = Math.Clamp(priority, 0.0, 4.0);

        var confidence = candidate.Detector is "aws_access_key_id" or "github_token" or "private_key_header" ? 0.88 : 0.68;
        if (candidate.LooksPlaceholder) confidence = Math.Max(confidence, 0.78);

        return Task.FromResult(new ClassificationResult(
            SecretType: type,
            LikelyReal: Math.Round(real, 3),
            PriorityScore: Math.Round(priority, 3),
            Confidence: Math.Round(confidence, 3),
            Source: "heuristic"
        ));
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}
