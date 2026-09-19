namespace SharpML2.Models;

public sealed class ScanOptions
{
    public required string RootPath { get; init; }
    public string OutputPath { get; init; } = "sharpml2-findings.jsonl";
    public long MaxFileSizeBytes { get; init; } = 2 * 1024 * 1024;
    public int ContextLines { get; init; } = 2;
    public int MaxCandidates { get; init; } = 5000;
    public bool UseJev { get; init; }
    public bool JevOnly { get; init; }
    public string? JevEndpoint { get; init; }
    public string? JevApiKey { get; init; }
    public string JevModel { get; init; } = "typesafe/jev-1.13";
    public double MinimumReportScore { get; init; } = 0.35;
}
