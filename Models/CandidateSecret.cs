namespace SharpML2.Models;

public sealed record CandidateSecret(
    string FilePath,
    string Extension,
    long FileSizeBytes,
    int LineNumber,
    string Detector,
    string? KeyName,
    string MaskedValue,
    string ValueFingerprint,
    int ValueLength,
    double Entropy,
    bool LooksPlaceholder,
    IReadOnlyList<string> Context,
    DateTimeOffset LastWriteTimeUtc
);
