namespace SharpML2.Models;

public sealed record ClassificationResult(
    string SecretType,
    double LikelyReal,
    double PriorityScore,
    double Confidence,
    string Source,
    double? LatencyMs = null
);

public sealed record Finding(
    CandidateSecret Candidate,
    ClassificationResult Classification
);
