using System.Globalization;
using SharpML2.Models;
using SharpML2.Services;

namespace SharpML2;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        ScanOptions options;
        try
        {
            options = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine("Run with --help for usage.");
            return 2;
        }

        if (!Directory.Exists(options.RootPath))
        {
            Console.Error.WriteLine($"error: scan root does not exist or is not accessible: {options.RootPath}");
            return 2;
        }

        var warnings = 0;
        void Warn(string message)
        {
            warnings++;
            Console.Error.WriteLine($"warning: {message}");
        }

        Console.WriteLine("SharpML 2.0 - defensive secret exposure triage");
        Console.WriteLine($"Root: {options.RootPath}");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutputPath)}");
        Console.WriteLine($"Classifier: {(options.JevOnly ? "Jev only (raw calibration mode)" : options.UseJev ? "local heuristics + Jev" : "local heuristics")}");
        Console.WriteLine("Raw secret values are not written to reports or sent to Jev.");
        Console.WriteLine();

        var detector = new SecretDetector();
        var scanner = new FileScanner(detector);
        var heuristic = new HeuristicClassifier();

        JevClassifier? jev = null;
        if (options.UseJev)
        {
            if (string.IsNullOrWhiteSpace(options.JevEndpoint))
            {
                Console.Error.WriteLine("error: --use-jev requires --jev-endpoint or JEV_ENDPOINT.");
                return 2;
            }

            jev = new JevClassifier(options.JevEndpoint, options.JevApiKey, options.JevModel);
        }

        using (jev)
        {
            ISecretClassifier classifier = options.JevOnly ? jev! : new HybridClassifier(heuristic, jev, Warn);
            await using var report = new ReportWriter(options.OutputPath);

            var scannedCandidates = 0;
            var reportedFindings = 0;
            var duplicateCandidates = 0;
            var dedupe = new HashSet<string>(StringComparer.Ordinal);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            try
            {
                foreach (var candidate in scanner.Scan(options, Warn))
                {
                    cts.Token.ThrowIfCancellationRequested();
                    scannedCandidates++;

                    var dedupeKey = $"{candidate.FilePath}\u001f{candidate.LineNumber}\u001f{candidate.ValueFingerprint}";
                    if (!dedupe.Add(dedupeKey))
                    {
                        duplicateCandidates++;
                        continue;
                    }

                    var classification = await classifier.ClassifyAsync(candidate, cts.Token);
                    if (classification.LikelyReal < options.MinimumReportScore && classification.PriorityScore < 2.0)
                    {
                        continue;
                    }

                    var finding = new Finding(candidate, classification);
                    await report.WriteAsync(finding, cts.Token);
                    reportedFindings++;

                    Console.WriteLine(
                        $"[{classification.PriorityScore:0.0}/4] {classification.SecretType,-18} " +
                        $"p(real)={classification.LikelyReal:0.00} {Path.GetFileName(candidate.FilePath)}:{candidate.LineNumber} " +
                        $"({classification.Source})");
                }
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Scan cancelled.");
            }

            Console.WriteLine();
            Console.WriteLine($"Candidates examined: {scannedCandidates}");
            Console.WriteLine($"Duplicates skipped: {duplicateCandidates}");
            Console.WriteLine($"Findings reported: {reportedFindings}");
            Console.WriteLine($"Warnings: {warnings}");
        }

        return 0;
    }

    private static ScanOptions ParseArgs(string[] args)
    {
        string? root = null;
        var output = "sharpml2-findings.jsonl";
        var maxFileBytes = 2L * 1024 * 1024;
        var contextLines = 2;
        var maxCandidates = 5000;
        var minimumReportScore = 0.35;
        var useJev = false;
        var jevOnly = false;
        string? jevEndpoint = Environment.GetEnvironmentVariable("JEV_ENDPOINT");
        var jevApiKey = Environment.GetEnvironmentVariable("JEV_API_KEY");
        var jevModel = Environment.GetEnvironmentVariable("JEV_MODEL") ?? "typesafe/jev-1.13";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--root":
                case "-r":
                    root = RequireValue(args, ref i, arg);
                    break;
                case "--output":
                case "-o":
                    output = RequireValue(args, ref i, arg);
                    break;
                case "--max-file-mb":
                    maxFileBytes = checked((long)(ParseDouble(RequireValue(args, ref i, arg), arg, 0.01, 1024) * 1024 * 1024));
                    break;
                case "--context-lines":
                    contextLines = ParseInt(RequireValue(args, ref i, arg), arg, 0, 10);
                    break;
                case "--max-candidates":
                    maxCandidates = ParseInt(RequireValue(args, ref i, arg), arg, 1, 1_000_000);
                    break;
                case "--min-likelihood":
                    minimumReportScore = ParseDouble(RequireValue(args, ref i, arg), arg, 0, 1);
                    break;
                case "--use-jev":
                    useJev = true;
                    break;
                case "--jev-only":
                    useJev = true;
                    jevOnly = true;
                    break;
                case "--jev-endpoint":
                    jevEndpoint = RequireValue(args, ref i, arg);
                    break;
                case "--jev-api-key":
                    jevApiKey = RequireValue(args, ref i, arg);
                    break;
                case "--jev-model":
                    jevModel = RequireValue(args, ref i, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("--root is required.");
        }

        return new ScanOptions
        {
            RootPath = root,
            OutputPath = output,
            MaxFileSizeBytes = maxFileBytes,
            ContextLines = contextLines,
            MaxCandidates = maxCandidates,
            UseJev = useJev,
            JevOnly = jevOnly,
            JevEndpoint = jevEndpoint,
            JevApiKey = jevApiKey,
            JevModel = jevModel,
            MinimumReportScore = minimumReportScore
        };
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int ParseInt(string value, string option, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < min || parsed > max)
        {
            throw new ArgumentException($"{option} must be an integer from {min} to {max}.");
        }
        return parsed;
    }

    private static double ParseDouble(string value, string option, double min, double max)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed < min || parsed > max)
        {
            throw new ArgumentException($"{option} must be a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.");
        }
        return parsed;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
SharpML 2.0 - defensive secret exposure triage

Usage:
  SharpML2 --root <directory-or-UNC-path> [options]

Required:
  -r, --root <path>             Directory or UNC share to scan. You must already have read access.

Output and limits:
  -o, --output <file>           JSONL report path (default: sharpml2-findings.jsonl)
      --max-file-mb <n>         Skip files larger than n MiB (default: 2)
      --context-lines <n>       Redacted lines around a candidate, 0-10 (default: 2)
      --max-candidates <n>      Hard candidate cap (default: 5000)
      --min-likelihood <0..1>   Reporting threshold for p(real) (default: 0.35)

Optional Jev classification:
      --use-jev                 Blend local heuristics with Jev typed decisions
      --jev-only                Use raw Jev outputs only; intended for calibration testing
      --jev-endpoint <url>      Typed-question compatible Jev endpoint (or JEV_ENDPOINT)
      --jev-api-key <key>       Bearer token (prefer JEV_API_KEY environment variable)
      --jev-model <model>       Model identifier (default: typesafe/jev-1.13)

Safety properties:
  * Read-only file scanning; no credential authentication or password spraying.
  * Raw candidate values are converted immediately to redacted metadata.
  * External classification receives redacted context, not raw secret values.
  * Findings are triage signals, not proof that a credential is valid.

Examples:
  SharpML2 -r C:\\Shares\\Finance -o findings.jsonl
  SharpML2 -r \\\\fileserver\\deploy --use-jev --jev-endpoint https://example.invalid/api/decision
""");
    }
}
