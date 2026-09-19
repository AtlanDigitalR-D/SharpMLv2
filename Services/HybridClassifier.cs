using SharpML2.Models;

namespace SharpML2.Services;

public sealed class HybridClassifier : ISecretClassifier
{
    private readonly HeuristicClassifier _heuristic;
    private readonly ISecretClassifier? _jev;
    private readonly Action<string>? _onWarning;

    public HybridClassifier(HeuristicClassifier heuristic, ISecretClassifier? jev, Action<string>? onWarning = null)
    {
        _heuristic = heuristic;
        _jev = jev;
        _onWarning = onWarning;
    }

    public async Task<ClassificationResult> ClassifyAsync(CandidateSecret candidate, CancellationToken cancellationToken)
    {
        var local = await _heuristic.ClassifyAsync(candidate, cancellationToken);
        if (_jev is null)
        {
            return local;
        }

        ClassificationResult remote;
        try
        {
            remote = await _jev.ClassifyAsync(candidate, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _onWarning?.Invoke($"Jev classification failed for {Path.GetFileName(candidate.FilePath)}:{candidate.LineNumber}; using local classifier ({ex.Message})");
            return local with { Source = "heuristic-fallback" };
        }

        var likelyReal = (remote.LikelyReal * 0.70) + (local.LikelyReal * 0.30);
        var priority = (remote.PriorityScore * 0.70) + (local.PriorityScore * 0.30);
        var confidence = (remote.Confidence * 0.65) + (local.Confidence * 0.35);

        var type = remote.SecretType == "uncertain" ? local.SecretType : remote.SecretType;
        if (remote.SecretType == "non_secret" && remote.Confidence < 0.75)
        {
            type = local.SecretType;
        }

        return new ClassificationResult(
            SecretType: type,
            LikelyReal: Math.Round(Math.Clamp(likelyReal, 0, 1), 3),
            PriorityScore: Math.Round(Math.Clamp(priority, 0, 4), 3),
            Confidence: Math.Round(Math.Clamp(confidence, 0, 1), 3),
            Source: "jev+heuristic",
            LatencyMs: remote.LatencyMs
        );
    }
}
